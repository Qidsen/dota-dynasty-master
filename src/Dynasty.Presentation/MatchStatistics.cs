using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Events;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Presentation
{
    /// <summary>
    /// Analyst view numbers (spec §13.3), built from the event stream and the match state. Call
    /// <see cref="Update"/> as often as the view likes: event totals do not depend on the cadence.
    /// Observer information (net worth, gold of both teams); a team's own view should not show the
    /// enemy's economy.
    /// </summary>
    public sealed class MatchStatistics
    {
        private readonly Dictionary<long, HeroLine> _lines = new Dictionary<long, HeroLine>();
        private readonly List<HeroLine> _ordered = new List<HeroLine>();
        private readonly Dictionary<long, Fixed> _damage = new Dictionary<long, Fixed>();
        private readonly Dictionary<long, Fixed> _healing = new Dictionary<long, Fixed>();
        private readonly List<MinuteSample> _minutes = new List<MinuteSample>();
        private int _eventsRead;
        private int _dawnGold;
        private int _duskGold;

        /// <summary>One line per hero, in spawn order (Dawn first).</summary>
        public IReadOnlyList<HeroLine> Heroes => _ordered;

        /// <summary>Team gold earned (exact, from events) and XP (sampled at updates) at each whole minute.</summary>
        public IReadOnlyList<MinuteSample> Minutes => _minutes;

        public void Update(SimulationKernel<MatchState> kernel)
        {
            MatchState state = kernel.State;
            if (_ordered.Count == 0)
            {
                foreach (MatchUnit hero in state.Heroes)
                {
                    var line = new HeroLine(hero.Id.Value, hero.Team, hero.Hero!.Definition.Name, hero.Hero.Definition.Id);
                    _lines.Add(hero.Id.Value, line);
                    _ordered.Add(line);
                }
            }

            int ticksPerMinute = kernel.Config.TickRate.TicksPerSecond * 60;
            IReadOnlyList<EventRecord> records = kernel.Events.Records;
            for (; _eventsRead < records.Count; _eventsRead++)
            {
                EventRecord record = records[_eventsRead];
                // A minute closes before the first event of the next one.
                while (record.Tick.Value >= (_minutes.Count + 1) * (long)ticksPerMinute)
                    CloseMinute(state, sampleXpNow: false);
                Read(state, record.Event);
            }

            while (kernel.CurrentTick.Value >= (_minutes.Count + 1) * (long)ticksPerMinute)
                CloseMinute(state, sampleXpNow: true);

            long minutes = kernel.CurrentTick.Value / ticksPerMinute;
            foreach (MatchUnit hero in state.Heroes)
            {
                HeroLine line = _lines[hero.Id.Value];
                var h = hero.Hero!;
                line.Kills = h.Kills;
                line.Deaths = h.Deaths;
                line.Assists = h.Assists;
                line.LastHits = h.LastHits;
                line.Denies = h.Denies;
                line.Level = h.Level;
                line.Xp = h.Xp;
                line.NetWorth = h.Gold.ToIntFloor() + h.Inventory.Sum(i => i.Definition.Cost);
                line.HeroDamage = _damage.TryGetValue(hero.Id.Value, out Fixed damage) ? damage.ToIntFloor() : 0;
                line.Healing = _healing.TryGetValue(hero.Id.Value, out Fixed healing) ? healing.ToIntFloor() : 0;
                line.GoldPerMinute = minutes > 0 ? (int)(line.GoldEarned / minutes) : 0;
                line.XpPerMinute = minutes > 0 ? (int)(h.Xp / minutes) : 0;
            }
        }

        private void Read(MatchState state, IDomainEvent domainEvent)
        {
            switch (domainEvent)
            {
                case GoldEarned gold when _lines.TryGetValue(gold.Hero.Value, out HeroLine? earner):
                    earner.GoldEarned += gold.Amount;
                    if (earner.Team == Team.Dawn)
                        _dawnGold += gold.Amount;
                    else
                        _duskGold += gold.Amount;
                    break;
                case DamageDealt damage when _lines.ContainsKey(damage.Attacker.Value) && _lines.ContainsKey(damage.Target.Value):
                    _damage[damage.Attacker.Value] = (_damage.TryGetValue(damage.Attacker.Value, out Fixed dealt) ? dealt : Fixed.Zero) + damage.Amount;
                    break;
                case HealApplied heal when _lines.ContainsKey(heal.Source.Value):
                    _healing[heal.Source.Value] = (_healing.TryGetValue(heal.Source.Value, out Fixed healed) ? healed : Fixed.Zero) + heal.Amount;
                    break;
                case UnitSummoned summon when _lines.TryGetValue(summon.Owner.Value, out HeroLine? owner) && IsWard(state, summon.CreepTypeId):
                    owner.WardsPlaced++;
                    break;
                case AbilityCast cast when _lines.TryGetValue(cast.Caster.Value, out HeroLine? caster) && IsSmoke(state, cast.AbilityId):
                    caster.SmokesUsed++;
                    break;
                case HeroBoughtBack buyback when _lines.TryGetValue(buyback.Hero.Value, out HeroLine? buyer):
                    buyer.Buybacks++;
                    break;
            }
        }

        private void CloseMinute(MatchState state, bool sampleXpNow)
        {
            int dawnXp = 0, duskXp = 0;
            if (sampleXpNow || _minutes.Count == 0)
            {
                foreach (MatchUnit hero in state.Heroes)
                {
                    if (hero.Team == Team.Dawn)
                        dawnXp += hero.Hero!.Xp;
                    else
                        duskXp += hero.Hero!.Xp;
                }
            }
            else
            {
                // XP is not in the event stream: between updates the last sample carries over.
                dawnXp = _minutes[_minutes.Count - 1].DawnXp;
                duskXp = _minutes[_minutes.Count - 1].DuskXp;
            }

            _minutes.Add(new MinuteSample(_minutes.Count + 1, _dawnGold, _duskGold, dawnXp, duskXp));
        }

        /// <summary>A summoned unit that cannot move and is innately invisible: a ward.</summary>
        private static bool IsWard(MatchState state, string creepTypeId)
        {
            CreepType? type = state.Ruleset.FindCreepType(creepTypeId);
            return type != null && type.Stats.MoveSpeed == 0 && (type.Stats.InnateFlags & Dynasty.MatchSimulation.Combat.ModifierFlags.Invisible) != 0;
        }

        /// <summary>An ability that makes nearby allied heroes invisible: a smoke.</summary>
        private static bool IsSmoke(MatchState state, string abilityId)
        {
            AbilityDefinition? ability = state.Ruleset.FindAbility(abilityId);
            return ability != null && ability.Effects.Any(e => e.Kind == EffectKind.Area && e.HeroesOnly &&
                e.Inner.Any(i => i.Kind == EffectKind.ApplyModifier && i.Modifiers.Any(m => (m.Flags & Dynasty.MatchSimulation.Combat.ModifierFlags.Invisible) != 0)));
        }
    }

    /// <summary>One hero's analyst line.</summary>
    public sealed class HeroLine
    {
        internal HeroLine(long heroId, Team team, string name, string definitionId)
        {
            HeroId = heroId;
            Team = team;
            Name = name;
            DefinitionId = definitionId;
        }

        public long HeroId { get; }
        public Team Team { get; }

        /// <summary>Hero display name.</summary>
        public string Name { get; }

        /// <summary>Hero definition ID (e.g. to look up the player on it).</summary>
        public string DefinitionId { get; }
        public int Kills { get; internal set; }
        public int Deaths { get; internal set; }
        public int Assists { get; internal set; }
        public int LastHits { get; internal set; }
        public int Denies { get; internal set; }
        public int Level { get; internal set; }
        public int Xp { get; internal set; }

        /// <summary>Unspent gold plus the full cost of carried items.</summary>
        public int NetWorth { get; internal set; }

        public int GoldEarned { get; internal set; }
        public int GoldPerMinute { get; internal set; }
        public int XpPerMinute { get; internal set; }

        /// <summary>Damage dealt to enemy heroes.</summary>
        public int HeroDamage { get; internal set; }

        public int Healing { get; internal set; }
        public int WardsPlaced { get; internal set; }
        public int SmokesUsed { get; internal set; }
        public int Buybacks { get; internal set; }
    }

    public sealed class MinuteSample
    {
        public MinuteSample(int minute, int dawnGold, int duskGold, int dawnXp, int duskXp)
        {
            Minute = minute;
            DawnGold = dawnGold;
            DuskGold = duskGold;
            DawnXp = dawnXp;
            DuskXp = duskXp;
        }

        public int Minute { get; }

        /// <summary>Gold earned by the team so far.</summary>
        public int DawnGold { get; }

        public int DuskGold { get; }
        public int DawnXp { get; }
        public int DuskXp { get; }
    }
}
