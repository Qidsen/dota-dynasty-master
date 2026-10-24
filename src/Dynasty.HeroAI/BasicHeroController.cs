using System.Collections.Generic;
using System.Linq;
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

        public void UpdatePlan(MatchState state, long now, int ticksPerMinute)
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

            if (state.Boss != null && ownAlive >= 3 && ownAlive - enemyAlive >= 2)
            {
                Plan = TeamPlanKind.Boss;
                Objective = state.Boss;
                return;
            }
            bool advantage = ownAlive - enemyAlive >= 2 || (ownAlive >= enemyAlive && ownAlive >= 3 && minute >= 20);
            if (advantage)
            {
                FixedVector2 home = state.Ruleset.Map.FountainOf(Team)!.Position;
                Objective = state.Units
                    .Where(u => u.Kind == UnitKind.Structure && u.Team != Team && u.IsAlive && state.IsVulnerable(u))
                    .OrderBy(u => FixedVector2.DistanceSquared(u.Position, home))
                    .FirstOrDefault();
                Plan = Objective != null ? TeamPlanKind.Push : TeamPlanKind.Lane;
                return;
            }

            Plan = TeamPlanKind.Lane;
            Objective = null;
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

        /// <summary>Camps this hero found empty, with the tick they are expected back.</summary>
        public Dictionary<string, long> CampEmptyUntil { get; } = new Dictionary<string, long>();
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
                _dawn.UpdatePlan(state, now, ticksPerMinute);
                _dusk.UpdatePlan(state, now, ticksPerMinute);
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
                memory.NextDecisionTick = now + _settings.DecisionIntervalTicks +
                                          (_settings.ReactionJitterTicks > 0 ? random.NextInt(0, _settings.ReactionJitterTicks + 1) : 0);

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
            _random = random;
            _now = now;
            _ticksPerMinute = ticksPerMinute;
        }

        private bool Sees(MatchUnit unit) => _state.IsVisibleTo(_hero.Team, unit);

        public void Run()
        {
            Shop();
            if (_h.IsDead || _h.IsCasting || !_hero.IsAlive)
                return;

            int hpPercent = Math.HpPercent(_hero);
            if (_memory.Retreating && hpPercent >= _personality.ReturnHpPercent)
                _memory.Retreating = false;
            else if (!_memory.Retreating && hpPercent < _personality.RetreatHpPercent)
                _memory.Retreating = true;
            if (_memory.Retreating)
            {
                GoHome();
                return;
            }

            if (Fight())
                return;
            if (FollowPlan())
                return;
            if (PlaceWard())
                return;
            if (Gank())
                return;
            if (FarmJungle())
                return;
            Lane();
        }

        // ── Jungle ───────────────────────────────────────────────────────────────

        /// <summary>
        /// With no allied creeps in its lane, a healthy hero farms the nearest own-half camp it has
        /// not recently found empty. Camp positions are static map knowledge; creeps must be seen.
        /// </summary>
        private bool FarmJungle()
        {
            if (Math.HpPercent(_hero) < 50 || _state.Ruleset.Map.Camps.Count == 0)
                return false;
            HeroDecision self = this;
            bool laneHasCreeps = _state.Units.Any(u => u.Team == self._hero.Team && u.Kind == UnitKind.Creep && u.IsAlive && u.LaneId == self._h.AssignedLane);
            if (laneHasCreeps)
                return false;

            FixedVector2 enemyHome = _state.Ruleset.Map.FountainOf(_hero.Team.Opponent())!.Position;
            NeutralCampDefinition? camp = _state.Ruleset.Map.Camps
                .Where(c => FixedVector2.DistanceSquared(c.Position, self.Home) < FixedVector2.DistanceSquared(c.Position, enemyHome))
                .Where(c => !self._memory.CampEmptyUntil.TryGetValue(c.Id, out long until) || self._now >= until)
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
                _memory.CampEmptyUntil[camp.Id] = _now + camp.RespawnMs * (_ticksPerMinute / 60) / 1000;
                return false;
            }

            Issue(HeroOrder.Attack(creep.Id));
            return true;
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
            double own = allies.Sum(Math.Power) + TowerSupport(_hero.Team, _hero.Position);
            double theirs = enemies.Sum(Math.Power) + TowerSupport(_hero.Team.Opponent(), _hero.Position);
            double noise = _settings.MisjudgementPercent > 0 ? _random.NextInt(-_settings.MisjudgementPercent, _settings.MisjudgementPercent + 1) / 100.0 : 0;
            double odds = own / System.Math.Max(theirs, 1) * _personality.Aggression * (1 + noise);

            if (odds < 0.85)
            {
                GoHome();
                return true;
            }

            if (odds < 1.15)
                return Harass(enemies);

            MatchUnit? focus = _brain.FocusTarget(_state, _hero.Position, range) ?? enemies[0];
            if (TryCastInFight(focus, enemies, allies))
                return true;
            if (ShouldWaitForInitiation(enemies, allies))
            {
                Hold();
                return true;
            }

            Issue(HeroOrder.Attack(focus.Id));
            return true;
        }

        /// <summary>Even fight: poke with single-target nukes when not worse off than the target.</summary>
        private bool Harass(List<MatchUnit> enemies)
        {
            MatchUnit target = enemies.OrderBy(e => e.Hp).First();
            if (Math.HpPercent(_hero) < Math.HpPercent(target))
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
            int minTargets = Math.IsUltimate(def) && enemies.Count > 1 ? 2 : 1;
            switch (def.Targeting)
            {
                case AbilityTargeting.Unit when def.TargetTeam == TargetTeam.Ally:
                    MatchUnit? hurt = allies.Where(a => Math.HpPercent(a) < 60 && Math.Within(_hero.Position, a.Position, reach)).OrderBy(a => a.Hp).FirstOrDefault();
                    return hurt != null ? HeroOrder.Cast(def.Id, hurt.Id) : null;

                case AbilityTargeting.Unit:
                    return Math.Within(_hero.Position, focus.Position, reach) ? HeroOrder.Cast(def.Id, focus.Id) : null;

                case AbilityTargeting.Point:
                    int radius = Math.AreaRadius(def);
                    FixedVector2 point = focus.Position;
                    return Math.Within(_hero.Position, point, reach) && enemies.Count(e => Math.Within(e.Position, point, radius)) >= minTargets
                        ? HeroOrder.CastAtPoint(def.Id, point)
                        : null;

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
            if (Math.HpPercent(_hero) < 70)
                return false;

            HeroDecision self = this;
            MatchUnit? prey = VisibleEnemyHeroes(_hero.Position, 3000)
                .Where(e => self.VisibleEnemyHeroes(e.Position, 1200).Count == 1 && !self.UnderEnemyTower(e.Position))
                .OrderBy(e => e.Hp)
                .FirstOrDefault();
            if (prey == null)
                return false;

            double own = _state.Heroes.Where(h => h.Team == self._hero.Team && h.IsAlive && Math.Within(h.Position, prey.Position, 3000)).Sum(Math.Power);
            if (own * _personality.Aggression < Math.Power(prey) * 1.3)
                return false;

            Issue(HeroOrder.Attack(prey.Id));
            return true;
        }

        // ── Team plan ────────────────────────────────────────────────────────────

        private bool FollowPlan()
        {
            MatchUnit? objective = _brain.Objective;
            if (objective == null || !objective.IsAlive)
                return false;

            if (_brain.Plan == TeamPlanKind.Defend)
            {
                if (Math.HpPercent(_hero) < 50)
                    return false;
                if (!Math.Within(_hero.Position, objective.Position, 4000) && TryTeleportTo(objective))
                    return true;
                if (!Math.Within(_hero.Position, objective.Position, 7000))
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

                MatchUnit? lastHit = FindLastHit(400);
                if (lastHit != null)
                {
                    Issue(HeroOrder.Attack(lastHit.Id));
                    return true;
                }

                bool towerBusy = !objective.CanAttackAtAll || CreepsTanking(objective) || AlliedHeroesNear(objective.Position, 1200) >= 3;
                if (towerBusy && _state.IsVulnerable(objective))
                {
                    Issue(HeroOrder.Attack(objective.Id));
                    return true;
                }

                // Gather just outside its range until creeps arrive or the team is assembled.
                int wait = Math.TowerReach(objective) + 250;
                Issue(HeroOrder.Move(FixedVector2.MoveTowards(objective.Position, Home, Fixed.FromInt(wait))));
                return true;
            }

            return false;
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
            AbilityState? place = _h.Inventory.Where(i => i.Definition.Id == "observer_ward" && i.Active != null).Select(i => i.Active!).FirstOrDefault();
            if (place == null || !place.IsReady(_now))
                return false;

            HeroDecision self = this;
            List<MatchUnit> ownWards = _state.Units.Where(u => u.Team == self._hero.Team && u.TypeId == "observer_ward" && u.IsAlive).ToList();
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
            if (_h.Order.Kind == HeroOrderKind.Attack)
            {
                MatchUnit? current = _state.Find(_h.Order.Target);
                if (current != null && current.IsAlive && current.Kind != UnitKind.Hero && current.Kind != UnitKind.Structure && Sees(current))
                    return;
            }

            MatchUnit? lastHit = FindLastHit(_hero.AttackRange + 400);
            if (lastHit != null)
            {
                Issue(HeroOrder.Attack(lastHit.Id));
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
            Issue(HeroOrder.Move(front));
        }

        private MatchUnit? FindLastHit(int reach)
        {
            Fixed factor = _state.Ruleset.Combat.ArmorFactor;
            MatchUnit? best = null;
            foreach (MatchUnit unit in _state.Units)
            {
                if ((unit.Kind != UnitKind.Creep && unit.Kind != UnitKind.Summon) || !unit.IsAlive || !Sees(unit))
                    continue;
                if (!Math.Within(_hero.Position, unit.Position, reach) || UnderEnemyTower(unit.Position))
                    continue;

                Fixed hit = Fixed.FromInt(_hero.AttackDamageMin) * CombatMath.ArmorMultiplier(unit.Armor, factor);
                bool deny = unit.Team == _hero.Team && unit.Hp * Fixed.FromInt(2) < Fixed.FromInt(unit.MaxHp);
                if (unit.Hp <= hit && (unit.Team != _hero.Team || deny) && (best == null || unit.Hp < best.Hp))
                    best = unit;
            }

            return best;
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
                if (!Math.Within(_hero.Position, structure.Position, 1000))
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

            if (WantsWard() && CanSpend(_state.Ruleset.FindItem("observer_ward")!.Cost))
            {
                HeroShop.QueuePurchase(_hero, "observer_ward");
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
        private bool WantsWard()
        {
            if (_state.Ruleset.FindItem("observer_ward") == null)
                return false;
            if (!_h.Definition.Roles.Contains("support") && !_h.Definition.Roles.Contains("healer"))
                return false;
            if (_h.Inventory.Any(i => i.Definition.Id == "observer_ward") || _h.Inventory.Count >= HeroShop.InventorySize)
                return false;
            HeroDecision self = this;
            return _state.Units.Count(u => u.Team == self._hero.Team && u.TypeId == "observer_ward" && u.IsAlive) < 2;
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
        private double TowerSupport(Team team, FixedVector2 point) =>
            _state.Units.Count(u => u.Kind == UnitKind.Structure && u.Team == team && u.IsAlive && u.CanAttackAtAll &&
                                    Math.Within(point, u.Position, Math.TowerReach(u) + 200)) * 25.0;

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

        /// <summary>Rough fighting strength: level and item value, scaled by remaining health. Uses only information visible on a hero.</summary>
        public static double Power(MatchUnit hero)
        {
            double strength = hero.Hero!.Level * 10 + hero.Hero.Inventory.Sum(i => i.Definition.Cost) / 100.0;
            return strength * (0.4 + 0.6 * HpPercent(hero) / 100.0);
        }

        public static bool IsTeleport(AbilityDefinition ability) => ability.Effects.Any(e => e.Kind == EffectKind.Teleport);

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
