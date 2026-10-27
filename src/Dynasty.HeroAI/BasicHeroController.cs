using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.Core.Randomness;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.HeroAI
{
    public static class HeroAi
    {
        /// <summary>Adds the team AI to a match; it then decides for every hero not marked uncontrolled.</summary>
        public static void Attach(SimulationKernel<MatchState> kernel, HeroAiSettings? settings = null) =>
            kernel.AddSystem(new TeamAiController(settings ?? new HeroAiSettings()));
    }

    public enum TeamPlanKind
    {
        Lane = 0,
        Push = 1,
        Defend = 2,

        /// <summary>Kill the neutral boss (its being alive is public knowledge, like a kill feed).</summary>
        Boss = 3,
    }

    /// <summary>
    /// Strategic layer of one team (spec §10–11): picks a plan (lane, push a structure, defend a
    /// structure) about once per second, and supplies the shared focus target for fights. It reads
    /// only team knowledge (vision), the static map and public kill-feed information (who is dead).
    /// </summary>
    internal sealed class TeamBrain
    {
        private List<FixedVector2>? _wardSpots;

        public TeamBrain(Team team)
        {
            Team = team;
        }

        public Team Team { get; }
        public TeamPlanKind Plan { get; private set; }
        public MatchUnit? Objective { get; private set; }

        /// <summary>The push was called at even numbers (by the clock), not on a numbers advantage.</summary>
        public bool PushAtEvenNumbers { get; private set; }

        /// <summary>Tick the current plan (or its objective) was called.</summary>
        public long PlanSince { get; private set; }

        /// <summary>Camps a hero of this team found empty, with the tick they are expected back (shared by the team).</summary>
        public Dictionary<string, long> CampEmptyUntil { get; } = new Dictionary<string, long>();

        public void UpdatePlan(MatchState state, long now, int ticksPerMinute, HeroAiSettings settings)
        {
            TeamPlanKind previousPlan = Plan;
            MatchUnit? previousObjective = Objective;
            Decide(state, now, ticksPerMinute, settings);
            if (Plan != previousPlan || Objective != previousObjective)
                PlanSince = now;
        }

        private void Decide(MatchState state, long now, int ticksPerMinute, HeroAiSettings settings)
        {
            MatchUnit? threatened = FindThreatenedStructure(state);
            if (threatened != null)
            {
                Plan = TeamPlanKind.Defend;
                Objective = threatened;
                return;
            }

            int ownAlive = state.Heroes.Count(h => h.Team == Team && h.IsAlive);
            int enemyAlive = state.Heroes.Count(h => h.Team != Team && !h.Hero!.IsDead);
            long minute = now / ticksPerMinute;

            MatchUnit? boss = state.Boss;
            bool bossUncontested = boss != null && minute >= settings.BossAtEvenNumbersFromMinute && ownAlive >= 4 && ownAlive >= enemyAlive &&
                                   !state.Heroes.Any(h => h.Team != Team && h.IsAlive && state.IsVisibleTo(Team, h) && Math.Within(h.Position, boss.Position, 2500));
            if (boss != null && ((ownAlive >= 3 && ownAlive - enemyAlive >= 2) || bossUncontested))
            {
                Plan = TeamPlanKind.Boss;
                Objective = state.Boss;
                return;
            }
            int needed = minute < settings.LaningPhaseFor(Team) ? 3 : 2;   // the laning phase is for farming
            bool numbers = ownAlive - enemyAlive >= needed;
            bool advantage = numbers || (ownAlive >= enemyAlive && ownAlive >= 3 && minute >= settings.EvenPushFor(Team));
            PushAtEvenNumbers = !numbers;
            if (advantage)
            {
                FixedVector2 home = state.Ruleset.Map.FountainOf(Team)!.Position;
                // Join the siege already under way: a structure our creeps are at beats a closer one.
                Objective = state.Units
                    .Where(u => u.Kind == UnitKind.Structure && u.Team != Team && u.IsAlive && state.IsVulnerable(u))
                    .OrderByDescending(u => AlliedCreepsAt(state, u))
                    .ThenBy(u => FixedVector2.DistanceSquared(u.Position, home))
                    .FirstOrDefault();
                Plan = Objective != null ? TeamPlanKind.Push : TeamPlanKind.Lane;
                return;
            }

            Plan = TeamPlanKind.Lane;
            Objective = null;
        }

        private int AlliedCreepsAt(MatchState state, MatchUnit structure)
        {
            int reach = Math.TowerReach(structure) + 300;
            return state.Units.Count(u => u.Kind == UnitKind.Creep && u.Team == Team && u.IsAlive && Math.Within(u.Position, structure.Position, reach));
        }

        /// <summary>An own structure with a visible enemy hero in or near its attack range.</summary>
        private MatchUnit? FindThreatenedStructure(MatchState state)
        {
            MatchUnit? best = null;
            int bestCount = 0;
            foreach (MatchUnit structure in state.Units)
            {
                if (structure.Kind != UnitKind.Structure || structure.Team != Team || !structure.IsAlive)
                    continue;
                int reach = Math.TowerReach(structure) + 300;
                int count = state.Heroes.Count(h => h.Team != Team && h.IsAlive && state.IsVisibleTo(Team, h) && Math.Within(h.Position, structure.Position, reach));
                if (count > bestCount)
                {
                    best = structure;
                    bestCount = count;
                }
            }

            return best;
        }

        /// <summary>Shared focus: the visible enemy hero with the least HP among those near the given point.</summary>
        public MatchUnit? FocusTarget(MatchState state, FixedVector2 around, int range) =>
            state.Heroes
                .Where(h => h.Team != Team && h.IsAlive && state.IsVisibleTo(Team, h) && state.IsVulnerable(h) && Math.Within(around, h.Position, range))
                .OrderBy(h => h.Hp)
                .ThenBy(h => FixedVector2.DistanceSquared(around, h.Position))
                .FirstOrDefault();

        /// <summary>Observer spots: midway between the opposing outer towers of each lane, pulled toward the map center.</summary>
        public IReadOnlyList<FixedVector2> WardSpots(MatchState state)
        {
            if (_wardSpots != null)
                return _wardSpots;

            var center = FixedVector2.FromInts(state.Ruleset.Map.Width / 2, state.Ruleset.Map.Height / 2);
            _wardSpots = new List<FixedVector2>();
            foreach (LaneDefinition lane in state.Ruleset.Map.Lanes)
            {
                string tag = "." + lane.Id + ".";
                StructurePlacement? own = OuterTower(state, Team, tag);
                StructurePlacement? enemy = OuterTower(state, Team.Opponent(), tag);
                if (own == null || enemy == null)
                    continue;
                var mid = new FixedVector2((own.Position.X + enemy.Position.X) / Fixed.FromInt(2), (own.Position.Y + enemy.Position.Y) / Fixed.FromInt(2));
                _wardSpots.Add(FixedVector2.MoveTowards(mid, center, Fixed.FromInt(400)));
            }

            return _wardSpots;
        }

        private static StructurePlacement? OuterTower(MatchState state, Team team, string laneTag)
        {
            FixedVector2 enemyFountain = state.Ruleset.Map.FountainOf(team.Opponent())!.Position;
            return state.Ruleset.Map.Structures
                .Where(s => s.Team == team && s.Id.Contains(laneTag))
                .OrderBy(s => FixedVector2.DistanceSquared(s.Position, enemyFountain))
                .FirstOrDefault();
        }
    }

    internal sealed class HeroMemory
    {
        public long NextDecisionTick { get; set; }
        public bool Retreating { get; set; }

        /// <summary>Own deaths already noticed, and the ticks of those in the last five minutes (tilt).</summary>
        public int SeenDeaths { get; set; }

        public List<long> RecentDeathTicks { get; } = new List<long>();

        /// <summary>The enemy hero being chased, the closest it has been and when, and the last decision of the chase.</summary>
        public EntityId ChaseTarget { get; set; }
        public Fixed ChaseClosest { get; set; }
        public long ChaseClosestTick { get; set; }
        public long ChaseLastTick { get; set; }

        /// <summary>A chase given up: this target is not chased again until the tick.</summary>
        public EntityId GaveUpOn { get; set; }
        public long GaveUpUntil { get; set; }

    }

    /// <summary>
    /// Team AI for every hero: strategic plan from <see cref="TeamBrain"/>, tactical choice per hero
    /// (retreat, fight with focus and initiation, defend/push, ward, gank, lane), and execution via
    /// hero orders and shop requests only. Decision timing and fight judgement carry seeded,
    /// plausible noise. Sees only team knowledge (fog of war).
    /// </summary>
    public sealed class TeamAiController : ISimulationSystem<MatchState>
    {
        private const int PlanIntervalTicks = 30;

        private readonly HeroAiSettings _settings;
        private readonly TeamBrain _dawn = new TeamBrain(Team.Dawn);
        private readonly TeamBrain _dusk = new TeamBrain(Team.Dusk);
        private readonly Dictionary<long, HeroMemory> _memory = new Dictionary<long, HeroMemory>();

        public TeamAiController(HeroAiSettings settings)
        {
            _settings = settings;
        }

        public string Name => "hero-ai-team";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            int ticksPerMinute = context.TickRate.TicksPerSecond * 60;
            if (now % PlanIntervalTicks == 0)
            {
                _dawn.UpdatePlan(state, now, ticksPerMinute, _settings);
                _dusk.UpdatePlan(state, now, ticksPerMinute, _settings);
            }

            DeterministicRandom random = context.Random("ai");
            foreach (MatchUnit hero in state.Units)
            {
                if (hero.Hero == null || _settings.Uncontrolled.Contains(hero.Hero.Definition.Id))
                    continue;

                if (!_memory.TryGetValue(hero.Id.Value, out HeroMemory? memory))
                {
                    memory = new HeroMemory();
                    _memory.Add(hero.Id.Value, memory);
                }

                if (now < memory.NextDecisionTick)
                    continue;
                HeroSkill skill = _settings.SkillFor(hero.Team, hero.Hero.Definition.Id);
                memory.NextDecisionTick = now + skill.DecisionIntervalTicks +
                                          (skill.ReactionJitterTicks > 0 ? random.NextInt(0, skill.ReactionJitterTicks + 1) : 0);

                TeamBrain brain = hero.Team == Team.Dawn ? _dawn : _dusk;
                new HeroDecision(state, hero, brain, memory, _settings, random, now, ticksPerMinute).Run();
            }
        }
    }

    /// <summary>One decision of one hero.</summary>
    internal sealed class HeroDecision
    {
        private readonly MatchState _state;
        private readonly MatchUnit _hero;
        private readonly HeroState _h;
        private readonly TeamBrain _brain;
        private readonly HeroMemory _memory;
        private readonly HeroAiSettings _settings;
        private readonly HeroPersonality _personality;
        private readonly HeroSkill _skill;
        private readonly DeterministicRandom _random;
        private readonly long _now;
        private readonly int _ticksPerMinute;

        public HeroDecision(MatchState state, MatchUnit hero, TeamBrain brain, HeroMemory memory, HeroAiSettings settings, DeterministicRandom random, long now, int ticksPerMinute)
        {
            _state = state;
            _hero = hero;
            _h = hero.Hero!;
            _brain = brain;
            _memory = memory;
            _settings = settings;
            _personality = settings.PersonalityFor(hero.Hero!.Definition);
            _skill = settings.SkillFor(hero.Team, hero.Hero!.Definition.Id);
            _random = random;
            _now = now;
            _ticksPerMinute = ticksPerMinute;
        }

        private bool Sees(MatchUnit unit) => _state.IsVisibleTo(_hero.Team, unit);

        /// <summary>A greedy carry of a late-game line-up before its power spike: farm, do not gamble.</summary>
        private bool ProtectingCarry => _personality.Greed > 0.7 && _now / _ticksPerMinute < _settings.StrategyFor(_hero.Team).ProtectCarriesUntilMinute;

        /// <summary>
        /// Siege plan: at an enemy structure that allied creeps are already tanking, call summons
        /// so they join the attack (otherwise summons are only called in fights).
        /// </summary>
        private bool SiegeWithSummons()
        {
            if (!_settings.StrategyFor(_hero.Team).SiegeWithSummons || (_hero.Flags & ModifierFlags.Silenced) != 0)
                return false;
            HeroDecision self = this;
            bool sieging = _state.Units.Any(u => u.Kind == UnitKind.Structure && u.Team != self._hero.Team && u.IsAlive && self._state.IsVulnerable(u) &&
                                                 Math.Within(u.Position, self._hero.Position, 1000) && self.CreepsTanking(u));
            if (!sieging)
                return false;
            AbilityState? summon = ReadyAbilities().FirstOrDefault(a => a.Definition.Targeting == AbilityTargeting.NoTarget && a.Definition.Effects.Any(e => e.Kind == EffectKind.Summon));
            if (summon == null)
                return false;
            Issue(HeroOrder.CastNoTarget(summon.Definition.Id));
            return true;
        }

        /// <summary>
        /// The player gets this creep's last-hit timing wrong (last-hit skill). Fixed per creep and hero
        /// (a hash, no random draw), so the miss is a whole missed chance, not a coin flip every decision.
        /// </summary>
        private bool MissesTiming(MatchUnit creep)
        {
            if (_skill.LastHitMissPercent <= 0)
                return false;
            ulong h = (ulong)creep.Id.Value * 0x9E3779B97F4A7C15UL ^ (ulong)_hero.Id.Value * 0xC2B2AE3D27D4EB4FUL;
            h ^= h >> 31;
            return (int)(h % 100) < _skill.LastHitMissPercent;
        }

        /// <summary>
        /// Map awareness: deep on the enemy side (near an enemy structure), hurt, with neither allied
        /// creeps nor allied heroes around, while three or more enemy heroes are missing from the map
        /// (alive but unseen), a gank is likely — fall back to the own outer tower. Pushing with a wave
        /// or with the team is not interrupted.
        /// </summary>
        private bool SenseDanger()
        {
            if (!_skill.DangerSense || Math.HpPercent(_hero) >= 80)
                return false;
            HeroDecision self = this;
            bool deep = _state.Units.Any(u => u.Kind == UnitKind.Structure && u.Team != self._hero.Team && u.IsAlive && Math.Within(u.Position, self._hero.Position, 2500));
            if (!deep || AlliedHeroesNear(_hero.Position, 1200) > 1)
                return false;
            if (_state.Units.Any(u => u.Team == self._hero.Team && u.Kind == UnitKind.Creep && u.IsAlive && Math.Within(u.Position, self._hero.Position, 1000)))
                return false;
            int missing = _state.Heroes.Count(h => h.Team != self._hero.Team && !h.Hero!.IsDead && !self.Sees(h));
            if (missing < 3)
                return false;
            Issue(HeroOrder.Move(LaneRally()));
            return true;
        }

        /// <summary>The player lets a good cast moment pass (spell accuracy); draws no random number for a sure player.</summary>
        private bool Hesitates() => _skill.SpellHesitationPercent > 0 && _random.NextInt(0, 100) < _skill.SpellHesitationPercent;

        /// <summary>
        /// The hero's own aggression scaled by the team's strategy, the player's confidence and tilt, in
        /// percent. The personality value is a configured number, turned into an integer once.
        /// </summary>
        private long AggressionPercent =>
            (long)System.Math.Round(_personality.Aggression * 100) * _settings.StrategyFor(_hero.Team).AggressionPercent * _skill.AggressionPercent
            * (Tilted ? 100 + _skill.TiltSusceptibility * 3 : 100) / 1000000;

        /// <summary>Two or more own deaths in the last five minutes rattle a susceptible player.</summary>
        private bool Tilted => _skill.TiltSusceptibility > 0 && _memory.RecentDeathTicks.Count >= 2;

        /// <summary>Records own deaths (public scoreboard information) and forgets those older than five minutes.</summary>
        private void NoticeDeaths()
        {
            for (; _memory.SeenDeaths < _h.Deaths; _memory.SeenDeaths++)
                _memory.RecentDeathTicks.Add(_now);
            _memory.RecentDeathTicks.RemoveAll(t => _now - t > 5L * _ticksPerMinute);
        }

        /// <summary>Reports the branch that decided (with the team plan when it is not laning) to <see cref="HeroAiSettings.DecisionTrace"/>.</summary>
        private void Trace(string branch) =>
            _settings.DecisionTrace?.Invoke(_hero, _now, _brain.Plan == TeamPlanKind.Lane ? branch : branch + "/" + _brain.Plan);

        private bool Decided(bool acted, string branch)
        {
            if (acted)
                Trace(branch);
            return acted;
        }

        public void Run()
        {
            Shop();
            if (_h.IsDead || _h.IsCasting || !_hero.IsAlive)
                return;

            NoticeDeaths();
            int hpPercent = Math.HpPercent(_hero);
            if (DrinkConsumable(hpPercent))
                return;
            // Healing over time that will bring the hero back to a fighting state replaces the trip home.
            bool patchedUp = Healing && ExpectedHpPercent() >= _personality.ReturnHpPercent - PatchedUpMarginPercent &&
                             VisibleEnemyHeroes(_hero.Position, _settings.EngageRange).Count == 0;
            if (_memory.Retreating && (hpPercent >= _personality.ReturnHpPercent || patchedUp))
                _memory.Retreating = false;
            else if (!_memory.Retreating && hpPercent < _personality.RetreatHpPercent + _skill.RetreatShiftPercent && !patchedUp)
                _memory.Retreating = true;
            if (_memory.Retreating)
            {
                Trace("Retreat");
                GoHome();
                return;
            }

            if (Decided(Cleanse(), "Cleanse"))
                return;
            if (Decided(Fight(), "Fight"))
                return;
            if (Decided(SiegeWithSummons(), "SiegeWithSummons"))
                return;
            if (Decided(SenseDanger(), "SenseDanger"))
                return;
            if (Decided(FollowPlan(), "FollowPlan"))
                return;
            if (Decided(PlaceWard(), "PlaceWard"))
                return;
            if (Decided(Gank(), "Gank"))
                return;
            if (Decided(PickUpRune(), "PickUpRune"))
                return;
            if (Decided(FarmJungle(), "FarmJungle"))
                return;
            Trace("Lane");
            Lane();
        }

        // ── Consumables ──────────────────────────────────────────────────────────

        private const int ConsumableHpPercent = 55;
        private const int ConsumablesCarried = 1;
        private const int PatchedUpMarginPercent = 30;

        /// <summary>Drinks a carried healing consumable when hurt and not already healing (backlog B-024).</summary>
        private bool DrinkConsumable(int hpPercent)
        {
            if (hpPercent >= ConsumableHpPercent || Healing || VisibleEnemyHeroes(_hero.Position, _settings.EngageRange).Count > 0)
                return false;
            AbilityState? potion = _h.Inventory
                .Where(i => i.Active != null && Math.IsHealingConsumable(i.Definition, i.Active.Definition))
                .Select(i => i.Active!)
                .FirstOrDefault(a => a.IsReady(_now));
            if (potion == null)
                return false;

            Issue(HeroOrder.CastNoTarget(potion.Definition.Id));
            return true;
        }

        /// <summary>Healing over time is running on the hero (a consumable, an ally's spell, a rune).</summary>
        private bool Healing => _hero.Modifiers.Any(m => m.Spec.HealPerSecond > Fixed.Zero && !m.Spec.IsPermanent && m.ExpiresTick > _now);

        /// <summary>Health in percent once the healing over time running on the hero has landed.</summary>
        private int ExpectedHpPercent()
        {
            Fixed pending = Fixed.Zero;
            foreach (ActiveModifier modifier in _hero.Modifiers)
                if (modifier.Spec.HealPerSecond > Fixed.Zero && !modifier.Spec.IsPermanent && modifier.ExpiresTick > _now)
                    pending += modifier.Spec.HealPerSecond * Fixed.FromInt((int)(modifier.ExpiresTick - _now)) / Fixed.FromInt(TicksPerSecond);
            return System.Math.Min(100, ((_hero.Hp + pending) * Fixed.FromInt(100) / Fixed.FromInt(_hero.MaxHp)).ToIntFloor());
        }

        // ── Runes ────────────────────────────────────────────────────────────────

        /// <summary>Walks onto a visible rune nearby (runes are only known while seen).</summary>
        private bool PickUpRune()
        {
            if (Math.HpPercent(_hero) < 40)
                return false;
            HeroDecision self = this;
            MatchUnit? rune = _state.Units
                .Where(u => u.Kind == UnitKind.Rune && self.Sees(u) && Math.Within(u.Position, self._hero.Position, 2500) && !self.UnderEnemyTower(u.Position))
                .OrderBy(u => FixedVector2.DistanceSquared(u.Position, self._hero.Position))
                .FirstOrDefault();
            if (rune == null)
                return false;

            Issue(HeroOrder.Move(rune.Position));
            return true;
        }

        // ── Jungle ───────────────────────────────────────────────────────────────

        /// <summary>
        /// With no allied creeps in its lane, a healthy hero farms the nearest own-half camp it has
        /// not recently found empty. After the laning phase a core farms camps near its lane between
        /// waves instead: whenever no enemy wave is at its lane front (backlog B-108). Camp positions
        /// are static map knowledge; creeps must be seen.
        /// </summary>
        private bool FarmJungle()
        {
            if (Math.HpPercent(_hero) < 50 || _state.Ruleset.Map.Camps.Count == 0)
                return false;
            HeroDecision self = this;
            bool core = !Laning && _personality.Greed >= CoreGreed;
            FixedVector2 front = LaneFront();
            FixedVector2 rally = LaneRally();   // a wave pushed into the enemy half is not followed: farm near the own tower
            if (core)
            {
                if (FinishingCamp())
                    return true;
                if (EnemyWaveAt(front))
                    return false;
            }
            else if (_state.Units.Any(u => u.Team == self._hero.Team && u.Kind == UnitKind.Creep && u.IsAlive && u.LaneId == self._h.AssignedLane))
                return false;

            FixedVector2 enemyHome = _state.Ruleset.Map.FountainOf(_hero.Team.Opponent())!.Position;
            NeutralCampDefinition? camp = _state.Ruleset.Map.Camps
                .Where(c => FixedVector2.DistanceSquared(c.Position, self.Home) < FixedVector2.DistanceSquared(c.Position, enemyHome))
                .Where(c => !core || (Math.Within(c.Position, front, CampReachFromLane) && Math.Within(c.Position, self._hero.Position, CampReachFromHero)) ||
                            Math.Within(c.Position, rally, CampReachFromLane))
                .Where(c => !self._brain.CampEmptyUntil.TryGetValue(c.Id, out long until) || self._now >= until)
                .OrderBy(c => FixedVector2.DistanceSquared(c.Position, self._hero.Position))
                .FirstOrDefault();
            if (camp == null)
                return false;

            if (!Math.Within(_hero.Position, camp.Position, 500))
            {
                Issue(HeroOrder.Move(camp.Position));
                return true;
            }

            MatchUnit? creep = _state.Units
                .Where(u => u.Team == Team.Neutral && u.Kind == UnitKind.Creep && u.IsAlive && self.Sees(u) && Math.Within(u.Position, camp.Position, camp.LeashRadius))
                .OrderBy(u => u.Hp)
                .FirstOrDefault();
            if (creep == null)
            {
                _brain.CampEmptyUntil[camp.Id] = ExpectedRespawn(camp);
                return false;
            }
            Issue(HeroOrder.Attack(creep.Id));
            return true;
        }

        /// <summary>When an empty camp is back: the next spawn mark of a camp on the clock (the game clock is public), else a full interval.</summary>
        private long ExpectedRespawn(NeutralCampDefinition camp)
        {
            long interval = (long)camp.RespawnMs * TicksPerSecond / 1000;
            if (!camp.AlignedToClock)
                return _now + interval;
            long first = (long)camp.FirstSpawnMs * TicksPerSecond / 1000;
            return _now < first ? first : first + ((_now - first) / interval + 1) * interval;
        }

        private const double CoreGreed = 0.5;
        private const int CampReachFromHero = 2000;
        private const int CampReachFromLane = 3000;
        private const int WaveReach = 1500;

        /// <summary>Already hitting a camp: finish it rather than leave the creeps half dead.</summary>
        private bool FinishingCamp()
        {
            if (_h.Order.Kind != HeroOrderKind.Attack)
                return false;
            MatchUnit? target = _state.Find(_h.Order.Target);
            return target != null && target.IsAlive && target.Team == Team.Neutral && target.Kind == UnitKind.Creep && Sees(target);
        }

        /// <summary>A visible enemy creep of the hero's lane near its lane front, outside enemy tower range.</summary>
        private bool EnemyWaveAt(FixedVector2 front)
        {
            HeroDecision self = this;
            return _state.Units.Any(u => u.Team == self._hero.Team.Opponent() && u.Kind == UnitKind.Creep && u.IsAlive && u.LaneId == self._h.AssignedLane &&
                                         self.Sees(u) && Math.Within(u.Position, front, WaveReach) && !self.UnderEnemyTower(u.Position));
        }

        // ── Support ──────────────────────────────────────────────────────────────

        /// <summary>Dispels a disabled or slowed allied hero in range.</summary>
        private bool Cleanse()
        {
            if ((_hero.Flags & ModifierFlags.Silenced) != 0)
                return false;
            HeroDecision self = this;
            foreach (AbilityState ability in ReadyAbilities())
            {
                AbilityDefinition def = ability.Definition;
                EffectDefinition? dispel = def.Effects.FirstOrDefault(e => e.Kind == EffectKind.Dispel);
                if (dispel == null || def.Targeting != AbilityTargeting.Unit || def.TargetTeam == TargetTeam.Enemy)
                    continue;

                MatchUnit? ally = _state.Heroes
                    .Where(h => h.Team == self._hero.Team && h.IsAlive && Math.Within(h.Position, self._hero.Position, def.CastRange + 200) &&
                                h.Modifiers.Any(m => Math.WorthCleansing(m.Spec, dispel.Strong)))
                    .OrderBy(h => h.Hp)
                    .FirstOrDefault();
                if (ally != null)
                {
                    Issue(HeroOrder.Cast(def.Id, ally.Id));
                    return true;
                }
            }

            return false;
        }

        // ── Fighting ─────────────────────────────────────────────────────────────

        /// <summary>Evaluates the local fight; engages, disengages or lets other layers act.</summary>
        private bool Fight()
        {
            int range = _settings.EngageRange;
            List<MatchUnit> enemies = VisibleEnemyHeroes(_hero.Position, range);
            if (enemies.Count == 0)
                return false;

            HeroDecision self = this;
            List<MatchUnit> allies = _state.Heroes.Where(h => h.Team == self._hero.Team && h.IsAlive && Math.Within(h.Position, self._hero.Position, range)).ToList();
            // Integer arithmetic throughout: the decision feeds the authoritative match (backlog B-029).
            long own = allies.Sum(PowerOf) + TowerSupport(_hero.Team, _hero.Position);
            long theirs = enemies.Sum(PowerOf) + TowerSupport(_hero.Team.Opponent(), _hero.Position);
            int misjudgement = _skill.MisjudgementPercent + (Tilted ? _skill.TiltSusceptibility * 2 : 0);
            int noise = misjudgement > 0 ? _random.NextInt(-misjudgement, misjudgement + 1) : 0;
            long oddsPercent = own * AggressionPercent * (100 + noise) / (System.Math.Max(theirs, 1) * 100);

            if (oddsPercent < 85)
            {
                if (Laning && Math.HpPercent(_hero) >= LaneHoldHpPercent)
                    return false;   // outnumbered but healthy in lane: keep last hitting carefully
                GoHome();
                return true;
            }
            if (oddsPercent < CommitOddsPercent(enemies))
            {
                return Harass(enemies);
            }

            if (_skill.FightFumblePercent > 0 && _random.NextInt(0, 100) < _skill.FightFumblePercent)
            {
                Hold();   // fumbles the moment: no swing, no spell
                return true;
            }

            MatchUnit? focus = _brain.FocusTarget(_state, _hero.Position, range) ?? enemies[0];
            if (_skill.FocusDisciplinePercent < 100 && _random.NextInt(0, 100) >= _skill.FocusDisciplinePercent)
            {
                // An undisciplined hero hits whoever is closest instead of the team's target.
                HeroDecision me = this;
                focus = enemies.OrderBy(e => FixedVector2.DistanceSquared(e.Position, me._hero.Position)).First();
            }

            if (Laning && Math.HpPercent(focus) > LaneKillHpPercent && !InOwnLaneArea(focus.Position))
                return TryCastInFight(focus, enemies, allies);   // no chase out of the lane in the laning phase

            if (DiveUnderTower(focus))
            {
                // Hitting a hero beside an enemy tower draws its fire: pick one out of its reach, or only cast.
                HeroDecision me = this;
                MatchUnit? safe = enemies.Where(e => !me.DiveUnderTower(e)).OrderBy(e => e.Hp).FirstOrDefault();
                if (safe == null)
                    return TryCastInFight(focus, enemies, allies);
                focus = safe;
            }

            if (TryCastInFight(focus, enemies, allies))
                return true;
            if (ShouldWaitForInitiation(enemies, allies))
            {
                Hold();
                return true;
            }

            if (!Catchable(focus))
            {
                return false;   // it gets away: back to farming
            }

            Issue(HeroOrder.Attack(focus.Id));
            return true;
        }

        private const int ChaseGiveUpTicks = 60;
        private const int ChaseForgetTicks = 150;

        /// <summary>
        /// An enemy hero out of attack reach that is not slower, not held in place, and has not come any
        /// closer for two seconds of chasing gets away: the chase is given up for a few seconds. Judged
        /// from seen positions only.
        /// </summary>
        private bool Catchable(MatchUnit target)
        {
            Fixed distance = FixedVector2.Distance(_hero.Position, target.Position);
            Fixed reach = Fixed.FromInt(_hero.AttackRange + _hero.BoundingRadius + target.BoundingRadius + 100);
            bool held = (target.Flags & (ModifierFlags.Stunned | ModifierFlags.Rooted)) != 0 || target.MoveSpeed < _hero.MoveSpeed;
            if (distance <= reach || held)
            {
                _memory.ChaseTarget = EntityId.None;
                return true;
            }

            if (_memory.GaveUpOn == target.Id && _now < _memory.GaveUpUntil)
                return false;
            if (_memory.ChaseTarget != target.Id || _now - _memory.ChaseLastTick > ChaseGiveUpTicks / 2)
            {
                _memory.ChaseTarget = target.Id;
                _memory.ChaseClosest = distance;
                _memory.ChaseClosestTick = _now;
            }
            else if (distance < _memory.ChaseClosest - Fixed.FromInt(50))
            {
                _memory.ChaseClosest = distance;
                _memory.ChaseClosestTick = _now;
            }

            _memory.ChaseLastTick = _now;
            if (_now - _memory.ChaseClosestTick <= ChaseGiveUpTicks)
                return true;
            _memory.GaveUpOn = target.Id;
            _memory.GaveUpUntil = _now + ChaseForgetTicks;
            _memory.ChaseTarget = EntityId.None;
            return false;
        }

        /// <summary>An enemy hero within reach of an enemy tower and not nearly dead: attacking him is a dive.</summary>
        private bool DiveUnderTower(MatchUnit enemy)
        {
            if (Math.HpPercent(enemy) <= DiveForKillHpPercent)
                return false;
            HeroDecision self = this;
            return _state.Units.Any(u => u.Kind == UnitKind.Structure && u.Team != self._hero.Team && u.IsAlive && u.CanAttackAtAll &&
                                         Math.Within(enemy.Position, u.Position, Math.TowerReach(u) + 100));
        }

        private const int DiveForKillHpPercent = 30;

        /// <summary>
        /// Odds needed to start a fight: a greedy carry protecting its farm wants them clearly won; in the
        /// laning phase anyone wants a clear edge unless an enemy is low enough to kill (lane fights are for kills).
        /// </summary>
        private int CommitOddsPercent(List<MatchUnit> enemies)
        {
            if (_personality.Greed >= 0.5 && Laning && enemies.All(e => Math.HpPercent(e) > LaneKillHpPercent))
                return int.MaxValue;   // a farming core fights in lane only for a kill
            if (ProtectingCarry && !KillLikely(enemies))
                return int.MaxValue;   // a carry farming toward its items fights only for a likely kill
            if (ProtectingCarry || LaningCore)
                return 140;
            bool laning = _now / _ticksPerMinute < _settings.LaningPhaseFor(_hero.Team);
            return laning && enemies.All(e => Math.HpPercent(e) > LaneKillHpPercent) ? LaneCommitOddsPercent : 115;
        }

        /// <summary>A kill is likely: an enemy is low or held in place, or the own side has two heroes more around.</summary>
        private bool KillLikely(List<MatchUnit> enemies)
        {
            if (enemies.Any(e => Math.HpPercent(e) <= LaneKillHpPercent || (e.Flags & (ModifierFlags.Stunned | ModifierFlags.Rooted)) != 0))
                return true;
            HeroDecision self = this;
            int allies = _state.Heroes.Count(h => h.Team == self._hero.Team && h.IsAlive && Math.Within(h.Position, self._hero.Position, self._settings.EngageRange));
            return allies >= enemies.Count + 2;
        }

        private const int LaneCommitOddsPercent = 150;
        private const int LaneKillHpPercent = 35;

        private bool Laning => _now / _ticksPerMinute < _settings.LaningPhaseFor(_hero.Team);

        private const int LaneHoldHpPercent = 60;

        private const int LastHitReach = 700;   // the walk is part of the timing check

        /// <summary>Within reach of an own creep or own tower: still the lane, not a chase into the enemy's ground.</summary>
        private bool InOwnLaneArea(FixedVector2 point)
        {
            HeroDecision self = this;
            return _state.Units.Any(u => u.Team == self._hero.Team && u.IsAlive && (u.Kind == UnitKind.Creep || (u.Kind == UnitKind.Structure && u.CanAttackAtAll)) &&
                                         Math.Within(u.Position, point, LaneAreaReach));
        }

        private const int LaneAreaReach = 900;

        /// <summary>A greedy carry in the laning phase: last hits first, fights only clearly won.</summary>
        private bool LaningCore => _personality.Greed > 0.7 && _now / _ticksPerMinute < _settings.LaningPhaseFor(_hero.Team);

        /// <summary>
        /// Fighting strength as this team judges it: an ultimate ready to cast counts for a lot.
        /// Allies' cooldowns and mana are known; an enemy's learned ultimate is assumed ready,
        /// since its cooldown cannot be seen.
        /// </summary>
        private long PowerOf(MatchUnit hero)
        {
            long power = Math.Power(hero);
            foreach (AbilityState ability in hero.Hero!.Abilities)
            {
                if (ability.Level == 0 || !Math.IsUltimate(ability.Definition))
                    continue;
                bool ready = hero.Team != _hero.Team ||
                             (ability.IsReady(_now) && hero.Hero.Mana >= Fixed.FromInt(ability.Definition.ManaCostAt(ability.Level)));
                if (ready)
                    return power * UltimateReadyPercent / 100;
            }

            return power;
        }

        private const int UltimateReadyPercent = 125;

        /// <summary>Even fight: poke with single-target nukes when not worse off than the target.</summary>
        private bool Harass(List<MatchUnit> enemies)
        {
            MatchUnit target = enemies.OrderBy(e => e.Hp).First();
            if (Math.HpPercent(_hero) < Math.HpPercent(target) || Hesitates())
                return false;

            foreach (AbilityState ability in ReadyAbilities())
            {
                AbilityDefinition def = ability.Definition;
                if (def.Targeting == AbilityTargeting.Unit && def.TargetTeam == TargetTeam.Enemy && !Math.IsUltimate(def) &&
                    Math.Within(_hero.Position, target.Position, def.CastRange + 200))
                {
                    Issue(HeroOrder.Cast(def.Id, target.Id));
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Non-initiators hold while an allied initiator nearby still has its disable ready and the
        /// fight has not started (no enemy disabled, nobody in melee contact).
        /// </summary>
        private bool ShouldWaitForInitiation(List<MatchUnit> enemies, List<MatchUnit> allies)
        {
            if (IsInitiator(_hero))
                return false;
            if (enemies.Any(e => (e.Flags & (ModifierFlags.Stunned | ModifierFlags.Rooted)) != 0))
                return false;
            HeroDecision self = this;
            if (enemies.Any(e => allies.Any(a => Math.Within(a.Position, e.Position, 250))))
                return false;
            return allies.Any(a => a != self._hero && IsInitiator(a) && a.Hero!.Abilities.Any(ab => Math.IsDisable(ab.Definition) && ab.IsReady(self._now) &&
                                                                                                     a.Hero.Mana >= Fixed.FromInt(ab.Definition.ManaCostAt(ab.Level))));
        }

        private static bool IsInitiator(MatchUnit hero) => hero.Hero!.Definition.Roles.Contains("initiator");

        private bool TryCastInFight(MatchUnit focus, List<MatchUnit> enemies, List<MatchUnit> allies)
        {
            if ((_hero.Flags & ModifierFlags.Silenced) != 0)
                return false;
            if (Hesitates())
                return false;

            // Disables first, then everything else, in definition order.
            foreach (AbilityState ability in ReadyAbilities().OrderBy(a => Math.IsDisable(a.Definition) ? 0 : 1))
            {
                HeroOrder? order = ChooseFightCast(ability.Definition, focus, enemies, allies);
                if (order != null)
                {
                    Issue(order);
                    return true;
                }
            }

            return false;
        }

        private HeroOrder? ChooseFightCast(AbilityDefinition def, MatchUnit focus, List<MatchUnit> enemies, List<MatchUnit> allies)
        {
            int reach = def.CastRange + 200;
            int minTargets = Math.IsUltimate(def) && enemies.Count > 1 && _skill.InitiationPatience ? 2 : 1;
            switch (def.Targeting)
            {
                case AbilityTargeting.Unit when def.TargetTeam == TargetTeam.Ally:
                    MatchUnit? hurt = allies.Where(a => Math.HpPercent(a) < 60 && Math.Within(_hero.Position, a.Position, reach)).OrderBy(a => a.Hp).FirstOrDefault();
                    return hurt != null ? HeroOrder.Cast(def.Id, hurt.Id) : null;

                case AbilityTargeting.Unit:
                    // A dispel on an enemy is only worth it when there is a buff to strip.
                    if (def.Effects.Any(e => e.Kind == EffectKind.Dispel) && !focus.Modifiers.Any(m => !m.Spec.IsPermanent && m.Spec.Dispellable && !m.Spec.IsDebuff))
                        return null;
                    return Math.Within(_hero.Position, focus.Position, reach) ? HeroOrder.Cast(def.Id, focus.Id) : null;

                case AbilityTargeting.Point:
                    int radius = Math.AreaRadius(def);
                    FixedVector2 point = focus.Position;
                    if (!Math.Within(_hero.Position, point, reach) || enemies.Count(e => Math.Within(e.Position, point, radius)) < minTargets)
                        return null;
                    int error = _skill.PointCastErrorUnits;
                    if (error > 0)
                        point = point + FixedVector2.FromInts(_random.NextInt(-error, error + 1), _random.NextInt(-error, error + 1));
                    return HeroOrder.CastAtPoint(def.Id, point);

                default:
                    if (def.Effects.Any(e => e.Kind == EffectKind.Summon))
                        return HeroOrder.CastNoTarget(def.Id);
                    EffectDefinition? area = def.Effects.FirstOrDefault(e => e.Kind == EffectKind.Area);
                    if (area != null && area.Team == TargetTeam.Ally)
                        return allies.Any(a => Math.HpPercent(a) < 60 && Math.Within(_hero.Position, a.Position, area.Radius)) ? HeroOrder.CastNoTarget(def.Id) : null;
                    int around = area?.Radius ?? 600;
                    HeroDecision self = this;
                    return enemies.Count(e => Math.Within(self._hero.Position, e.Position, around)) >= minTargets ? HeroOrder.CastNoTarget(def.Id) : null;
            }
        }

        /// <summary>Kills an isolated visible enemy hero a little further away when the odds are clearly good.</summary>
        private bool Gank()
        {
            if (Math.HpPercent(_hero) < 70 || ProtectingCarry)
                return false;

            HeroDecision self = this;
            int gankRange = _settings.StrategyFor(_hero.Team).GankRange;
            // In the laning phase the own lane opponent is a matter of laning (the fight rules), not a gank.
            bool laning = _now / _ticksPerMinute < _settings.LaningPhaseFor(_hero.Team);
            MatchUnit? prey = VisibleEnemyHeroes(_hero.Position, gankRange)
                .Where(e => self.VisibleEnemyHeroes(e.Position, 1200).Count == 1 && !self.DiveUnderTower(e))
                .Where(e => !laning || Math.NearestLane(self._state.Ruleset.Map, e.Position) != self._h.AssignedLane)
                .OrderBy(e => e.Hp)
                .FirstOrDefault();
            if (prey == null)
                return false;

            long own = _state.Heroes.Where(h => h.Team == self._hero.Team && h.IsAlive && Math.Within(h.Position, prey.Position, gankRange)).Sum(PowerOf);
            if (own * AggressionPercent < PowerOf(prey) * 130)
                return false;

            if (!Math.Within(_hero.Position, prey.Position, 1500) && TrySmoke())
                return true;
            if (!Catchable(prey))
                return false;

            Issue(HeroOrder.Attack(prey.Id));
            return true;
        }

        /// <summary>Smokes the nearby group before a long approach (at least one ally close by).</summary>
        private bool TrySmoke()
        {
            if ((_hero.Flags & ModifierFlags.Invisible) != 0)
                return false;
            HeroDecision self = this;
            if (_state.Heroes.Count(h => h.Team == self._hero.Team && h.IsAlive && h != self._hero && Math.Within(h.Position, self._hero.Position, 800)) == 0)
                return false;
            AbilityState? smoke = _h.Inventory
                .Where(i => i.Active != null && Math.IsSmoke(i.Active.Definition))
                .Select(i => i.Active!)
                .FirstOrDefault(a => a.IsReady(_now));
            if (smoke == null)
                return false;

            Issue(HeroOrder.CastNoTarget(smoke.Definition.Id));
            return true;
        }

        // ── Team plan ────────────────────────────────────────────────────────────

        private bool FollowPlan()
        {
            MatchUnit? objective = _brain.Objective;
            if (objective == null || !objective.IsAlive)
                return false;
            if (_now < _brain.PlanSince + _settings.StrategyFor(_hero.Team).CallDelayTicks)
                return false;   // the call has not got through yet

            if (_brain.Plan == TeamPlanKind.Defend)
            {
                if (Math.HpPercent(_hero) < 50)
                    return false;
                // While laning, and for a greedy carry, only heroes already close come to help.
                long minute = _now / _ticksPerMinute;
                int responseRange = minute < _settings.LaningPhaseFor(_hero.Team) || _personality.Greed > 0.7 ? 3000 : 7000;
                if (responseRange > NearbyDefenseRange && !Math.Within(_hero.Position, objective.Position, NearbyDefenseRange) && !AmongNearestDefenders(objective))
                    return false;   // others answer this threat; keep farming
                if (responseRange > 4000 && !Math.Within(_hero.Position, objective.Position, 4000) && TryTeleportTo(objective))
                    return true;
                if (!Math.Within(_hero.Position, objective.Position, responseRange))
                    return false;
                Issue(HeroOrder.Move(FixedVector2.MoveTowards(objective.Position, Home, Fixed.FromInt(250))));
                return true;
            }

            if (_brain.Plan == TeamPlanKind.Boss)
            {
                if (Math.HpPercent(_hero) < 50)
                    return false;
                if (Sees(objective) && Math.Within(_hero.Position, objective.Position, 1500))
                    Issue(HeroOrder.Attack(objective.Id));
                else
                    Issue(HeroOrder.Move(objective.Position));
                return true;
            }

            if (_brain.Plan == TeamPlanKind.Push)
            {
                long minute = _now / _ticksPerMinute;
                if (_personality.Greed > 0.7 && minute < 18)
                    return false;
                // A greedy carry keeps farming through a push at even numbers on a low-ground tower far away;
                // it joins a push on numbers, on the high ground, or one that comes to it (backlog B-020).
                if (_personality.Greed > 0.7 && _brain.PushAtEvenNumbers && _state.ElevationAt(objective.Position) == 0 &&
                    !Math.Within(_hero.Position, objective.Position, CarryJoinsPushWithin))
                    return false;
                HeroDecision self = this;
                int needed = System.Math.Min(3, _state.Heroes.Count(h => h.Team == self._hero.Team && h.IsAlive));
                if (_brain.PushAtEvenNumbers && !_settings.StrategyFor(_hero.Team).GroupsWholeTeamForPushes &&
                    !Math.Within(_hero.Position, objective.Position, PushJoinsWithin) && !AmongNearestPushers(objective, needed))
                    return false;   // the others keep catching waves

                MatchUnit? lastHit = FindLastHit(400);
                if (lastHit != null)
                {
                    Issue(HeroOrder.Attack(lastHit.Id));
                    return true;
                }

                // Hit it together: wait at the gathering point until enough of the team has arrived.
                bool assembled = !objective.CanAttackAtAll || AlliedHeroesNear(objective.Position, Math.TowerReach(objective) + 700) >= needed;
                if (assembled && _state.IsVulnerable(objective) && !objective.BackdoorActive)
                {
                    Issue(HeroOrder.Attack(objective.Id));
                    return true;
                }

                // Gather just outside its range until creeps arrive (lifting Backdoor Protection) or the team is assembled.
                int wait = Math.TowerReach(objective) + 250;
                Issue(HeroOrder.Move(FixedVector2.MoveTowards(objective.Position, Home, Fixed.FromInt(wait))));
                return true;
            }

            return false;
        }

        private const int CarryJoinsPushWithin = 3000;
        private const int NearbyDefenseRange = 3000;
        private const int PushJoinsWithin = 2000;

        /// <summary>
        /// A push at even numbers takes the heroes it needs: the nearest living heroes that would join it
        /// (greedy carries skip early pushes), ties by ID so the choice does not depend on processing order.
        /// </summary>
        private bool AmongNearestPushers(MatchUnit objective, int needed)
        {
            HeroDecision self = this;
            int rank = _state.Heroes
                .Where(h => h.Team == self._hero.Team && h.IsAlive && (h == self._hero || self._settings.PersonalityFor(h.Hero!.Definition).Greed <= 0.7))
                .OrderBy(h => FixedVector2.DistanceSquared(h.Position, objective.Position))
                .ThenBy(h => h.Id.Value)
                .TakeWhile(h => h != self._hero)
                .Count();
            return rank < needed;
        }

        /// <summary>
        /// A threat is answered by one more hero than the visible attackers: the nearest living ones
        /// (ties by ID, so the choice does not depend on processing order).
        /// </summary>
        private bool AmongNearestDefenders(MatchUnit structure)
        {
            HeroDecision self = this;
            int attackers = _state.Heroes.Count(h => h.Team != self._hero.Team && h.IsAlive && self.Sees(h) &&
                                                     Math.Within(h.Position, structure.Position, Math.TowerReach(structure) + 300));
            int rank = _state.Heroes
                .Where(h => h.Team == self._hero.Team && h.IsAlive)
                .OrderBy(h => FixedVector2.DistanceSquared(h.Position, structure.Position))
                .ThenBy(h => h.Id.Value)
                .TakeWhile(h => h != self._hero)
                .Count();
            return rank <= attackers;
        }

        /// <summary>Channels a carried teleport onto an allied structure.</summary>
        private bool TryTeleportTo(MatchUnit structure)
        {
            if (structure.Kind != UnitKind.Structure || structure.Team != _hero.Team)
                return false;
            AbilityState? teleport = _h.Inventory
                .Where(i => i.Active != null && Math.IsTeleport(i.Active.Definition))
                .Select(i => i.Active!)
                .FirstOrDefault(a => a.IsReady(_now));
            if (teleport == null)
                return false;

            Issue(HeroOrder.Cast(teleport.Definition.Id, structure.Id));
            return true;
        }

        // ── Vision ───────────────────────────────────────────────────────────────

        private bool PlaceWard()
        {
            ItemDefinition? wardItem = Math.ObserverWardItem(_state.Ruleset);
            AbilityState? place = wardItem == null ? null : _h.Inventory.Where(i => i.Definition == wardItem && i.Active != null).Select(i => i.Active!).FirstOrDefault();
            if (place == null || !place.IsReady(_now))
                return false;

            HeroDecision self = this;
            string wardType = Math.WardUnitType(_state.Ruleset, wardItem!)!;
            List<MatchUnit> ownWards = _state.Units.Where(u => u.Team == self._hero.Team && u.TypeId == wardType && u.IsAlive).ToList();
            FixedVector2? spot = _brain.WardSpots(_state)
                .Where(s => !ownWards.Any(w => Math.Within(w.Position, s, 1000)) && !self.UnderEnemyTower(s))
                .OrderBy(s => FixedVector2.DistanceSquared(s, self._hero.Position))
                .Select(s => (FixedVector2?)s)
                .FirstOrDefault();
            if (spot == null)
                return false;

            Issue(HeroOrder.CastAtPoint(place.Definition.Id, spot.Value));
            return true;
        }

        // ── Laning ───────────────────────────────────────────────────────────────

        private void Lane()
        {
            MatchUnit? lastHit = FindLastHit(_hero.AttackRange + LastHitReach);
            if (_h.Order.Kind == HeroOrderKind.Attack)
            {
                // Keep a swing at a creep only while it is still worth it; otherwise stop hitting the
                // wave (that only helps the allied creeps take the kill) and wait for the next chance.
                MatchUnit? current = _state.Find(_h.Order.Target);
                if (current != null && current.Kind == UnitKind.Structure && current.IsAlive)
                    return;
                if (current != null && current.IsAlive && current.Kind != UnitKind.Hero && Sees(current) && IsLastHitCandidate(current))
                    return;
            }

            if (lastHit != null)
            {
                Issue(HeroOrder.Attack(lastHit.Id));
                return;
            }

            MatchUnit? creep = WaveToShove();
            if (creep != null)
            {
                Issue(HeroOrder.Attack(creep.Id));
                return;
            }

            MatchUnit? structure = FindPushTarget();
            if (structure != null)
            {
                Issue(HeroOrder.Attack(structure.Id));
                return;
            }

            FixedVector2 front = LaneFront();
            if (UnderEnemyTower(front))
                front = LaneRally();
            if (TeleportBackToLane(front))
                return;
            Issue(HeroOrder.Move(front));
        }

        /// <summary>
        /// Healed up at the base with a long walk ahead: teleport to the own lane tower nearest the lane
        /// front instead of walking (backlog B-024).
        /// </summary>
        private bool TeleportBackToLane(FixedVector2 front)
        {
            if (!Math.Within(_hero.Position, Home, AtBaseRange) || Math.Within(_hero.Position, front, TeleportWorthDistance))
                return false;
            HeroDecision self = this;
            MatchUnit? tower = _state.Units
                .Where(u => u.Kind == UnitKind.Structure && u.Team == self._hero.Team && u.IsAlive && u.CanAttackAtAll &&
                            !Math.Within(u.Position, self.Home, TeleportWorthDistance))
                .OrderBy(u => FixedVector2.DistanceSquared(u.Position, front))
                .FirstOrDefault();
            return tower != null && TryTeleportTo(tower);
        }

        private const int AtBaseRange = 1500;
        private const int TeleportWorthDistance = 4000;

        private MatchUnit? FindLastHit(int reach)
        {
            MatchUnit? best = null;
            foreach (MatchUnit unit in _state.Units)
            {
                if ((unit.Kind != UnitKind.Creep && unit.Kind != UnitKind.Summon) || !unit.IsAlive || !Sees(unit))
                    continue;
                if (!Math.Within(_hero.Position, unit.Position, reach) || UnderEnemyTower(unit.Position))
                    continue;
                if (IsLastHitCandidate(unit) && (best == null || unit.Hp < best.Hp))
                    best = unit;
            }

            return best;
        }

        /// <summary>
        /// An enemy creep (or an allied one below half HP, a deny) that a swing started now finishes:
        /// the hits already on their way to it (visible wind-ups and the attackers' rhythm) leave it
        /// alive but within one hit by the time the swing lands.
        /// </summary>
        private bool IsLastHitCandidate(MatchUnit unit)
        {
            if (unit.Kind != UnitKind.Creep && unit.Kind != UnitKind.Summon)
                return false;
            bool deny = unit.Team == _hero.Team && unit.Hp * Fixed.FromInt(2) < Fixed.FromInt(unit.MaxHp);
            if (unit.Team == _hero.Team && !deny)
                return false;
            if (MissesTiming(unit))
                return false;

            Fixed factor = _state.Ruleset.Combat.ArmorFactor;
            Fixed hit = Fixed.FromInt(_hero.AttackDamageMin) * CombatMath.ArmorMultiplier(unit.Armor, factor);
            if (unit.Hp <= hit)
                return true;
            if (deny)
                return false;   // a speculative deny would cost the cooldown a last hit needs
            Fixed expected = unit.Hp - DamageLandingBefore(unit, _now + TicksUntilMyHitLands(unit), factor);
            return expected <= hit && expected > Fixed.Zero;
        }

        /// <summary>Walk into range (while the attack cooldown runs out), then wind-up and projectile flight, in ticks.</summary>
        private int TicksUntilMyHitLands(MatchUnit target)
        {
            Fixed distance = FixedVector2.Distance(_hero.Position, target.Position);
            Fixed reach = Fixed.FromInt(_hero.AttackRange + _hero.BoundingRadius + target.BoundingRadius);
            long walk = 0;
            if (distance > reach && _hero.MoveSpeed > 0)
                walk = ((distance - reach) * Fixed.FromInt(TicksPerSecond) / Fixed.FromInt(_hero.MoveSpeed)).ToIntFloor();
            long swingStarts = System.Math.Max(walk, _hero.NextAttackTick - _now);
            return (int)swingStarts + WindUpTicks(_hero) + FlightTicks(_hero, Fixed.Min(distance, reach));
        }

        /// <summary>
        /// Damage the visible units attacking the target from within range will land before
        /// <paramref name="tick"/>: a wind-up in progress, then one swing per attack interval.
        /// </summary>
        private Fixed DamageLandingBefore(MatchUnit target, long tick, Fixed armorFactor)
        {
            Fixed perHit = CombatMath.ArmorMultiplier(target.Armor, armorFactor);
            Fixed total = Fixed.Zero;
            foreach (MatchUnit attacker in _state.Units)
            {
                if (attacker.Team == target.Team || attacker.Kind == UnitKind.Hero || !attacker.IsAlive || !Sees(attacker))
                    continue;
                bool windingUp = attacker.PendingAttackTick >= 0 && attacker.PendingTargetId == target.Id;
                if (!windingUp && attacker.TargetId != target.Id)
                    continue;
                if (!Math.Within(attacker.Position, target.Position, attacker.AttackRange + attacker.BoundingRadius + target.BoundingRadius + 25))
                    continue;   // still walking up: not hitting yet

                Fixed average = Fixed.FromRatio(attacker.AttackDamageMin + attacker.AttackDamageMax, 2) * perHit;
                long flight = FlightTicks(attacker, FixedVector2.Distance(attacker.Position, target.Position));
                int hits = 0;
                if (windingUp && attacker.PendingAttackTick + flight < tick)
                    hits++;
                long interval = System.Math.Max(1, attacker.Stats.AttackIntervalMs * 100 / (100 + attacker.AttackSpeedBonus) * TicksPerSecond / 1000);
                for (long start = System.Math.Max(_now, attacker.NextAttackTick); start + WindUpTicks(attacker) + flight < tick; start += interval)
                    hits++;
                total += average * Fixed.FromInt(hits);
            }

            return total;
        }

        private int TicksPerSecond => _ticksPerMinute / 60;

        private int WindUpTicks(MatchUnit unit) => unit.Stats.AttackPointMs * 100 / (100 + unit.AttackSpeedBonus) * TicksPerSecond / 1000;

        private int FlightTicks(MatchUnit unit, Fixed distance) =>
            unit.Stats.ProjectileSpeed > 0 ? (distance * Fixed.FromInt(TicksPerSecond) / Fixed.FromInt(unit.Stats.ProjectileSpeed)).ToIntFloor() : 0;

        /// <summary>A percent chance; draws no random number when the outcome is certain.</summary>
        private bool Rolls(int percent) => percent >= 100 || (percent > 0 && _random.NextInt(0, 100) < percent);

        /// <summary>
        /// After the laning phase, with no enemy hero in sight, a player who plays for objectives shoves the
        /// wave in front of him (the weakest creep in reach) instead of waiting for last hits (backlog B-060).
        /// </summary>
        private MatchUnit? WaveToShove()
        {
            if (_now / _ticksPerMinute < _settings.LaningPhaseFor(_hero.Team) || _skill.WavePushPercent <= 0 ||
                VisibleEnemyHeroes(_hero.Position, _settings.EngageRange).Count > 0)
                return null;
            if (!Rolls(_skill.WavePushPercent))
                return null;
            HeroDecision self = this;
            return _state.Units
                .Where(u => u.Team == self._hero.Team.Opponent() && (u.Kind == UnitKind.Creep || u.Kind == UnitKind.Summon) && u.IsAlive && self.Sees(u) &&
                            Math.Within(u.Position, self._hero.Position, self._hero.AttackRange + 400) && !self.UnderEnemyTower(u.Position))
                .OrderBy(u => u.Hp)
                .ThenBy(u => u.Id.Value)
                .FirstOrDefault();
        }

        private MatchUnit? FindPushTarget()
        {
            HeroDecision self = this;
            bool enemyCreepsNear = _state.Units.Any(u => u.Team != self._hero.Team && u.IsAlive && (u.Kind == UnitKind.Creep || u.Kind == UnitKind.Summon) &&
                                                         self.Sees(u) && Math.Within(self._hero.Position, u.Position, 900));
            if (enemyCreepsNear)
                return null;

            foreach (MatchUnit structure in _state.Units)
            {
                if (structure.Kind != UnitKind.Structure || structure.Team == _hero.Team || !structure.IsAlive || !_state.IsVulnerable(structure))
                    continue;
                // Backdoor Protection (shown on the building) heals what heroes alone deal: push it with the creeps.
                if (structure.BackdoorActive || !Math.Within(_hero.Position, structure.Position, 1000))
                    continue;
                if (CreepsTanking(structure) || !structure.CanAttackAtAll)
                    return structure;
            }

            return null;
        }

        private FixedVector2 LaneFront()
        {
            FixedVector2 enemyFountain = _state.Ruleset.Map.FountainOf(_hero.Team.Opponent())!.Position;
            MatchUnit? front = null;
            Fixed best = Fixed.Zero;
            foreach (MatchUnit unit in _state.Units)
            {
                if (unit.Team != _hero.Team || unit.Kind != UnitKind.Creep || !unit.IsAlive || unit.LaneId != _h.AssignedLane)
                    continue;
                Fixed distance = FixedVector2.DistanceSquared(unit.Position, enemyFountain);
                if (front == null || distance < best)
                {
                    front = unit;
                    best = distance;
                }
            }

            return front != null ? FixedVector2.MoveTowards(front.Position, Home, Fixed.FromInt(150)) : LaneRally();
        }

        private FixedVector2 LaneRally()
        {
            FixedVector2 enemyFountain = _state.Ruleset.Map.FountainOf(_hero.Team.Opponent())!.Position;
            string laneTag = "." + _h.AssignedLane + ".";
            MatchUnit? outer = null;
            Fixed best = Fixed.Zero;
            foreach (MatchUnit unit in _state.Units)
            {
                if (unit.Team != _hero.Team || unit.Placement == null || !unit.Placement.Id.Contains(laneTag))
                    continue;
                Fixed distance = FixedVector2.DistanceSquared(unit.Position, enemyFountain);
                if (outer == null || distance < best)
                {
                    outer = unit;
                    best = distance;
                }
            }

            return outer?.Position ?? Home;
        }

        // ── Shopping ─────────────────────────────────────────────────────────────

        private void Shop()
        {
            if (_h.IsDead && TryBuyback())
                return;

            FountainPlacement? fountain = _state.Ruleset.Map.FountainOf(_hero.Team);
            bool inShop = _h.IsDead || (fountain != null && Math.Within(_hero.Position, fountain.Position, fountain.Radius));
            if (!inShop)
                return;

            ItemDefinition? wardItem = Math.ObserverWardItem(_state.Ruleset);
            if (wardItem != null && WantsWard(wardItem) && CanSpend(wardItem.Cost))
            {
                HeroShop.QueuePurchase(_hero, wardItem.Id);
                return;
            }

            // Supports carry the team's smoke.
            ItemDefinition? smokeItem = _state.Ruleset.Items.FirstOrDefault(i =>
                i.ActiveAbilityId != null && Math.IsSmoke(_state.Ruleset.FindAbility(i.ActiveAbilityId)!));
            bool isSupport = _h.Definition.Roles.Contains("support") || _h.Definition.Roles.Contains("healer");
            HeroDecision me = this;
            bool teamHasSmoke = smokeItem != null && _state.Heroes.Any(h => h.Team == me._hero.Team && h.Hero!.Inventory.Any(i => i.Definition.Id == smokeItem.Id));
            if (smokeItem != null && isSupport && !teamHasSmoke && _h.Inventory.Count < HeroShop.InventorySize && CanSpend(smokeItem.Cost))
            {
                HeroShop.QueuePurchase(_hero, smokeItem.Id);
                return;
            }

            // In the laning phase every hero carries a healing consumable.
            ItemDefinition? potionItem = _state.Ruleset.Items.FirstOrDefault(i =>
                i.ActiveAbilityId != null && Math.IsHealingConsumable(i, _state.Ruleset.FindAbility(i.ActiveAbilityId)));
            if (potionItem != null && _now / _ticksPerMinute < _settings.LaningPhaseFor(_hero.Team) && _h.Inventory.Count(i => i.Definition.Id == potionItem.Id) < ConsumablesCarried &&
                _h.Inventory.Count < HeroShop.InventorySize && CanSpend(potionItem.Cost))
            {
                HeroShop.QueuePurchase(_hero, potionItem.Id);
                return;
            }

            // Every hero keeps one teleport scroll.
            ItemDefinition? scroll = _state.Ruleset.Items.FirstOrDefault(i =>
                i.ActiveAbilityId != null && Math.IsTeleport(_state.Ruleset.FindAbility(i.ActiveAbilityId)!));
            if (scroll != null && _h.Inventory.All(i => i.Definition.Id != scroll.Id) && _h.Inventory.Count < HeroShop.InventorySize && CanSpend(scroll.Cost))
            {
                HeroShop.QueuePurchase(_hero, scroll.Id);
                return;
            }

            string? target = NextBuildItem();
            ItemDefinition? item = target != null ? NextPurchaseToward(_state.Ruleset.FindItem(target)) : null;
            if (item == null)
                return;

            HeroDecision self = this;
            int ownedComponentCost = item.Components.Where(c => self._h.Inventory.Any(slot => slot.Definition.Id == c)).Sum(c => self._state.Ruleset.FindItem(c)!.Cost);
            if (CanSpend(item.Cost - ownedComponentCost))
                HeroShop.QueuePurchase(_hero, item.Id);
        }

        /// <summary>Whether the price is affordable while keeping a buyback in reserve in the late game.</summary>
        private bool CanSpend(int price)
        {
            HeroRules? rules = _state.Ruleset.HeroRules;
            bool reserve = rules != null && rules.BuybackEnabled && _now / _ticksPerMinute >= _settings.BuybackReserveFromMinute;
            int keep = reserve ? rules!.BuybackCost(_h.Level) : 0;
            return _h.Gold >= Fixed.FromInt(price + keep);
        }

        /// <summary>Buy back when the team is defending and the natural respawn is still far away.</summary>
        private bool TryBuyback()
        {
            HeroRules? rules = _state.Ruleset.HeroRules;
            if (rules == null || !rules.BuybackEnabled || _brain.Plan != TeamPlanKind.Defend || _now < _h.BuybackReadyTick)
                return false;
            long ticksPerSecond = _ticksPerMinute / 60;
            if (_h.RespawnTick - _now < _settings.BuybackMinWaitSeconds * ticksPerSecond)
                return false;
            if (_h.Gold < Fixed.FromInt(rules.BuybackCost(_h.Level)))
                return false;

            HeroActions.QueueBuyback(_hero);
            return true;
        }

        /// <summary>Supports keep an observer ward while the team has fewer than two placed.</summary>
        private bool WantsWard(ItemDefinition wardItem)
        {
            if (!_h.Definition.Roles.Contains("support") && !_h.Definition.Roles.Contains("healer"))
                return false;
            if (_h.Inventory.Any(i => i.Definition == wardItem) || _h.Inventory.Count >= HeroShop.InventorySize)
                return false;
            HeroDecision self = this;
            string wardType = Math.WardUnitType(_state.Ruleset, wardItem)!;
            return _state.Units.Count(u => u.Team == self._hero.Team && u.TypeId == wardType && u.IsAlive) < 2;
        }

        private string? NextBuildItem()
        {
            var owned = _h.Inventory.Select(s => s.Definition.Id).ToList();
            foreach (string itemId in _h.Definition.ItemBuild)
            {
                if (owned.Remove(itemId))
                    continue;
                return itemId;
            }

            return null;
        }

        /// <summary>The first missing component of the target (recursively), or the target itself once all are owned.</summary>
        private ItemDefinition? NextPurchaseToward(ItemDefinition? target)
        {
            if (target == null)
                return null;

            var owned = _h.Inventory.Select(s => s.Definition.Id).ToList();
            foreach (string componentId in target.Components)
            {
                if (owned.Remove(componentId))
                    continue;
                return NextPurchaseToward(_state.Ruleset.FindItem(componentId));
            }

            return target;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private FixedVector2 Home => _state.Ruleset.Map.FountainOf(_hero.Team)!.Position;

        private void GoHome() => Issue(HeroOrder.Move(Home));

        private void Hold()
        {
            if (_h.Order.Kind != HeroOrderKind.Idle)
                HeroOrders.Issue(_hero, HeroOrder.Idle);
        }

        private IEnumerable<AbilityState> ReadyAbilities()
        {
            HeroDecision self = this;
            return _h.Abilities.Concat(_h.Inventory.Where(i => i.Active != null && i.Definition.Charges == 0).Select(i => i.Active!))
                .Where(a => a.Definition.Targeting != AbilityTargeting.Passive && a.IsReady(self._now) &&
                            self._h.Mana >= Fixed.FromInt(a.Definition.ManaCostAt(a.Level)));
        }

        private List<MatchUnit> VisibleEnemyHeroes(FixedVector2 around, int range)
        {
            HeroDecision self = this;
            return _state.Heroes
                .Where(h => h.Team != self._hero.Team && h.IsAlive && self.Sees(h) && self._state.IsVulnerable(h) && Math.Within(around, h.Position, range))
                .OrderBy(h => FixedVector2.DistanceSquared(around, h.Position))
                .ToList();
        }

        /// <summary>Extra fight power from living towers of <paramref name="team"/> covering the point.</summary>
        private long TowerSupport(Team team, FixedVector2 point) =>
            _state.Units.Count(u => u.Kind == UnitKind.Structure && u.Team == team && u.IsAlive && u.CanAttackAtAll &&
                                    Math.Within(point, u.Position, Math.TowerReach(u) + 200)) * 2500L;   // 25 power points each

        private bool CreepsTanking(MatchUnit tower)
        {
            HeroDecision self = this;
            return _state.Units.Any(u => u.Team == self._hero.Team && (u.Kind == UnitKind.Creep || u.Kind == UnitKind.Summon) && u.IsAlive &&
                                         Math.Within(u.Position, tower.Position, Math.TowerReach(tower)));
        }

        private int AlliedHeroesNear(FixedVector2 point, int range)
        {
            HeroDecision self = this;
            return _state.Heroes.Count(h => h.Team == self._hero.Team && h.IsAlive && Math.Within(h.Position, point, range));
        }

        /// <summary>Whether standing at the point would draw fire from an enemy tower no allied creep is tanking.</summary>
        private bool UnderEnemyTower(FixedVector2 point)
        {
            foreach (MatchUnit tower in _state.Units)
            {
                if (tower.Kind != UnitKind.Structure || tower.Team == _hero.Team || !tower.IsAlive || !tower.CanAttackAtAll)
                    continue;
                if (Math.Within(point, tower.Position, Math.TowerReach(tower) + 100) && !CreepsTanking(tower))
                    return true;
            }

            return false;
        }

        /// <summary>Issues the order unless the hero is already doing the same thing (avoids re-pathing every decision).</summary>
        private void Issue(HeroOrder order)
        {
            HeroOrder current = _h.Order;
            if (current.Kind == order.Kind && current.Target == order.Target && current.AbilityId == order.AbilityId &&
                (order.Kind != HeroOrderKind.Move || Math.Within(current.Point, order.Point, 100)))
                return;

            HeroOrders.Issue(_hero, order);
        }
    }

    internal static class Math
    {
        public static bool Within(FixedVector2 a, FixedVector2 b, int range)
        {
            Fixed r = Fixed.FromInt(range);
            return FixedVector2.DistanceSquared(a, b) <= r * r;
        }

        public static int HpPercent(MatchUnit unit) => (int)(unit.Hp * Fixed.FromInt(100) / Fixed.FromInt(unit.MaxHp)).ToIntFloor();

        public static int TowerReach(MatchUnit tower) => tower.AttackRange + tower.BoundingRadius + 24;

        /// <summary>
        /// Rough fighting strength in hundredths of a point: level ×10 plus item value / 100, scaled
        /// by remaining health (40% at none to 100% at full). Uses only information visible on a hero.
        /// </summary>
        public static long Power(MatchUnit hero)
        {
            long strength = hero.Hero!.Level * 1000L + hero.Hero.Inventory.Sum(i => i.Definition.Cost);
            return strength * (4000 + 60 * HpPercent(hero)) / 10000;
        }

        /// <summary>The lane whose path passes closest to the point (static map knowledge).</summary>
        public static string? NearestLane(MapDefinition map, FixedVector2 point)
        {
            string? best = null;
            Fixed bestDistance = Fixed.Zero;
            foreach (LaneDefinition lane in map.Lanes)
                for (int i = 1; i < lane.Waypoints.Count; i++)
                {
                    Fixed distance = SegmentDistanceSquared(point, lane.Waypoints[i - 1], lane.Waypoints[i]);
                    if (best == null || distance < bestDistance)
                    {
                        best = lane.Id;
                        bestDistance = distance;
                    }
                }

            return best;
        }

        private static Fixed SegmentDistanceSquared(FixedVector2 p, FixedVector2 a, FixedVector2 b)
        {
            FixedVector2 ab = b - a;
            Fixed length = ab.LengthSquared;
            if (length == Fixed.Zero)
                return FixedVector2.DistanceSquared(p, a);
            Fixed t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / length;
            t = Fixed.Min(Fixed.One, Fixed.Max(Fixed.Zero, t));
            return FixedVector2.DistanceSquared(p, a + ab * t);
        }

        public static bool IsTeleport(AbilityDefinition ability) => ability.Effects.Any(e => e.Kind == EffectKind.Teleport);

        /// <summary>
        /// The observer ward: an item whose active summons an immobile, invisible unit without true sight (a
        /// sentry has true sight). Found by its mechanics, so packs may name it as they like.
        /// </summary>
        public static ItemDefinition? ObserverWardItem(RulesetContent ruleset) =>
            ruleset.Items.FirstOrDefault(i => WardUnitType(ruleset, i) != null);

        /// <summary>The unit type an observer ward item places, or null when the item is not one.</summary>
        public static string? WardUnitType(RulesetContent ruleset, ItemDefinition item)
        {
            AbilityDefinition? active = item.ActiveAbilityId != null ? ruleset.FindAbility(item.ActiveAbilityId) : null;
            if (active == null)
                return null;
            foreach (EffectDefinition effect in active.Effects)
            {
                CreepType? type = effect.Kind == EffectKind.Summon && effect.SummonTypeId != null ? ruleset.FindCreepType(effect.SummonTypeId) : null;
                if (type != null && type.Stats.MoveSpeed == 0 && (type.Stats.InnateFlags & ModifierFlags.Invisible) != 0 && type.Stats.TrueSightRange == 0)
                    return type.Id;
            }

            return null;
        }

        /// <summary>A healing consumable: a no-target item ability with charges that only applies healing over time.</summary>
        public static bool IsHealingConsumable(ItemDefinition item, AbilityDefinition? ability) =>
            item.Charges > 0 && ability != null && ability.Targeting == AbilityTargeting.NoTarget && ability.Effects.Count > 0 &&
            ability.Effects.All(e => e.Kind == EffectKind.ApplyModifier && e.Modifiers.Count > 0 && e.Modifiers.All(m => m.HealPerSecond > Fixed.Zero));

        /// <summary>A removable debuff that hampers an ally: a disable (stun only with a strong dispel) or a slow.</summary>
        public static bool WorthCleansing(ModifierSpec spec, bool strong)
        {
            if (spec.IsPermanent || !spec.Dispellable || !spec.IsDebuff)
                return false;
            if ((spec.Flags & ModifierFlags.Stunned) != 0)
                return strong;
            return (spec.Flags & (ModifierFlags.Rooted | ModifierFlags.Silenced | ModifierFlags.Disarmed)) != 0 || spec.MoveSpeedPercent < 0;
        }

        /// <summary>A group-invisibility ability: a heroes-only allied area that grants invisibility.</summary>
        public static bool IsSmoke(AbilityDefinition ability) =>
            ability.Effects.Any(e => e.Kind == EffectKind.Area && e.HeroesOnly && e.Team == TargetTeam.Ally &&
                                     e.Inner.Any(i => i.Kind == EffectKind.ApplyModifier && i.Modifiers.Any(m => (m.Flags & ModifierFlags.Invisible) != 0)));

        public static bool IsUltimate(AbilityDefinition ability) => ability.RequiredHeroLevel.Count > 0 && ability.RequiredHeroLevel[0] >= 6;

        public static bool IsDisable(AbilityDefinition ability) =>
            ability.Effects.Any(Disables);

        private static bool Disables(EffectDefinition effect) =>
            (effect.Kind == EffectKind.ApplyModifier && effect.Modifiers.Any(m => (m.Flags & (ModifierFlags.Stunned | ModifierFlags.Rooted)) != 0)) ||
            effect.Inner.Any(Disables);

        public static int AreaRadius(AbilityDefinition ability) =>
            ability.Effects.Where(e => e.Kind == EffectKind.Area).Select(e => e.Radius).DefaultIfEmpty(250).First();
    }
}
