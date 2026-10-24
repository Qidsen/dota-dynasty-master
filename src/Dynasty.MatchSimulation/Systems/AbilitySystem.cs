using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Spends skill points and carries out cast orders: validates (learned, cooldown, mana,
    /// silence, target), runs the cast point, then executes the ability's effects. Moving into
    /// cast range is done by the hero order system before this runs.
    /// </summary>
    internal sealed class AbilitySystem : ISimulationSystem<MatchState>
    {
        public string Name => "abilities";

        public void Tick(SimulationContext<MatchState> context)
        {
            long now = context.Tick.Value;
            IReadOnlyList<MatchUnit> units = context.State.Units;
            // Index loop with a fixed count: summons spawned by casts are appended and skipped this tick.
            for (int i = 0, count = units.Count; i < count; i++)
            {
                MatchUnit unit = units[i];
                HeroState? hero = unit.Hero;
                if (hero == null || hero.IsDead || !unit.IsAlive)
                    continue;

                if (hero.UnspentSkillPoints > 0)
                    HeroProgression.LearnSkills(context, unit);

                if (hero.Order.Kind == HeroOrderKind.Cast)
                    ProcessCast(context, unit, hero, now);
            }
        }

        private static void ProcessCast(SimulationContext<MatchState> context, MatchUnit caster, HeroState hero, long now)
        {
            HeroOrder order = hero.Order;
            AbilityState? ability = hero.Ability(order.AbilityId!);

            // Status checks use the tick-start snapshot (see MatchUnit.StartFlags): a stun landing this
            // tick does not stop a cast resolving this tick, whichever hero is processed first.
            if (hero.IsCasting)
            {
                if ((caster.StartFlags & (ModifierFlags.Stunned | ModifierFlags.Silenced)) != 0)
                {
                    Fail(context, caster, order.AbilityId!, "interrupted");
                    return;
                }

                if (now >= hero.CastEndTick)
                    Complete(context, caster, hero, ability!, order, now);
                return;
            }

            // A stunned hero keeps its order and tries again once the stun ends.
            if ((caster.StartFlags & ModifierFlags.Stunned) != 0)
                return;

            string? failure = Validate(context.State, caster, hero, ability, order, now);
            if (failure != null)
            {
                Fail(context, caster, order.AbilityId!, failure);
                return;
            }

            if (!InCastRange(caster, ability!.Definition, order, context.State))
                return;

            hero.CastEndTick = now + context.TickRate.TicksFor(ability.Definition.CastPointMs);
            if (now >= hero.CastEndTick)
                Complete(context, caster, hero, ability, order, now);
        }

        private static string? Validate(MatchState state, MatchUnit caster, HeroState hero, AbilityState? ability, HeroOrder order, long now)
        {
            if (ability == null || ability.Level == 0 || ability.Definition.Targeting == AbilityTargeting.Passive)
                return "not_learned";
            if ((caster.StartFlags & ModifierFlags.Silenced) != 0)
                return "silenced";
            if (!ability.IsReady(now))
                return "cooldown";
            if (hero.Mana < Fixed.FromInt(ability.Definition.ManaCostAt(ability.Level)))
                return "mana";
            if (ability.Definition.Targeting == AbilityTargeting.Unit)
            {
                MatchUnit? target = state.Find(order.Target);
                if (target == null || !Effects.Matches(state, caster, target, ability.Definition.TargetTeam, ability.Definition.TargetsStructures) ||
                    !state.IsVisibleTo(caster.Team, target))
                    return "invalid_target";
            }

            return null;
        }

        internal static bool InCastRange(MatchUnit caster, AbilityDefinition ability, HeroOrder order, MatchState state)
        {
            switch (ability.Targeting)
            {
                case AbilityTargeting.Unit:
                    MatchUnit? target = state.Find(order.Target);
                    return target != null && Geometry.WithinEdgeRange(caster, target, ability.CastRange);
                case AbilityTargeting.Point:
                    return Geometry.WithinRange(caster.Position, order.Point, ability.CastRange);
                default:
                    return true;
            }
        }

        private static void Complete(SimulationContext<MatchState> context, MatchUnit caster, HeroState hero, AbilityState ability, HeroOrder order, long now)
        {
            AbilityDefinition definition = ability.Definition;
            MatchUnit? target = definition.Targeting == AbilityTargeting.Unit ? context.State.Find(order.Target) : caster;
            if (target == null || !target.IsAlive)
            {
                Fail(context, caster, definition.Id, "invalid_target");
                return;
            }

            hero.Mana -= Fixed.FromInt(definition.ManaCostAt(ability.Level));
            ability.CooldownReadyTick = now + context.TickRate.TicksFor(definition.CooldownAt(ability.Level));
            ConsumeCharge(context, caster, hero, ability);
            hero.CastEndTick = -1;
            HeroOrders.Issue(caster, HeroOrder.Idle);
            context.Emit(new AbilityCast(caster.Id, definition.Id, ability.Level, definition.Targeting == AbilityTargeting.Unit ? target.Id : EntityId.None, order.Point));

            FixedVector2 point = definition.Targeting == AbilityTargeting.Point ? order.Point : target.Position;
            Effects.Run(context, caster, definition.Effects, ability.Level, target, point);
        }

        /// <summary>Using a consumable item's active spends a charge; the item is gone at zero.</summary>
        private static void ConsumeCharge(SimulationContext<MatchState> context, MatchUnit caster, HeroState hero, AbilityState ability)
        {
            ItemSlot? slot = hero.InventorySlots.FirstOrDefault(s => ReferenceEquals(s.Active, ability));
            if (slot == null || slot.Definition.Charges == 0)
                return;

            slot.Charges--;
            if (slot.Charges > 0)
                return;

            hero.InventorySlots.Remove(slot);
            if (slot.Bonus != null)
                Modifiers.Remove(context, caster, slot.Bonus, "item_consumed");
        }

        private static void Fail(SimulationContext<MatchState> context, MatchUnit caster, string abilityId, string reason)
        {
            HeroOrders.Issue(caster, HeroOrder.Idle);
            context.Emit(new AbilityCastFailed(caster.Id, abilityId, reason));
        }
    }

    /// <summary>Executes effect primitives. Each kind is a small, independently tested handler.</summary>
    internal static class Effects
    {
        /// <summary>Knockback moves in steps of this size and stops at the first unwalkable step.</summary>
        private static readonly Fixed KnockbackStep = Fixed.FromInt(25);

        public static void Run(SimulationContext<MatchState> context, MatchUnit caster, IReadOnlyList<EffectDefinition> effects, int level, MatchUnit target, FixedVector2 point)
        {
            foreach (EffectDefinition effect in effects)
                RunOne(context, caster, effect, level, target, point);
        }

        /// <summary>
        /// Whether <paramref name="unit"/> is a legal target for the team filter. Abilities never affect
        /// structures, except allied ones for abilities that explicitly target structures (teleports).
        /// </summary>
        public static bool Matches(MatchState state, MatchUnit caster, MatchUnit unit, TargetTeam team, bool allowAlliedStructures = false)
        {
            if (!unit.IsAlive)
                return false;
            if (unit.Kind == UnitKind.Structure)
                return allowAlliedStructures && unit.Team == caster.Team && team != TargetTeam.Enemy;
            switch (team)
            {
                case TargetTeam.Enemy:
                    return unit.Team != caster.Team && state.IsVulnerable(unit);
                case TargetTeam.Ally:
                    return unit.Team == caster.Team;
                default:
                    return true;
            }
        }

        private static void RunOne(SimulationContext<MatchState> context, MatchUnit caster, EffectDefinition effect, int level, MatchUnit target, FixedVector2 point)
        {
            switch (effect.Kind)
            {
                case EffectKind.Damage:
                    Damage.Deal(context, caster.Id, target, Fixed.FromInt(effect.AmountAt(level)), effect.DamageType);
                    break;
                case EffectKind.Heal:
                    Damage.Heal(context, caster.Id, target, Fixed.FromInt(effect.AmountAt(level)));
                    break;
                case EffectKind.ApplyModifier:
                    if (target.IsAlive)
                        Modifiers.Apply(context, target, effect.ModifierAt(level), caster.Id);
                    break;
                case EffectKind.Area:
                    RunArea(context, caster, effect, level, target, point);
                    break;
                case EffectKind.Dash:
                    Dash(caster, target, point);
                    break;
                case EffectKind.Knockback:
                    Knockback(context.State, caster, target, effect.AmountAt(level));
                    break;
                case EffectKind.Summon:
                    Summon(context, caster, effect, level, point);
                    break;
                case EffectKind.Teleport:
                    Teleport(context.State, caster, target);
                    break;
            }
        }

        private static void RunArea(SimulationContext<MatchState> context, MatchUnit caster, EffectDefinition effect, int level, MatchUnit target, FixedVector2 point)
        {
            FixedVector2 center = effect.Center == AreaCenter.Caster ? caster.Position
                : effect.Center == AreaCenter.Target ? target.Position
                : point;

            List<MatchUnit> affected = context.State.Units
                .Where(u => Matches(context.State, caster, u, effect.Team) && Geometry.WithinRange(center, u.Position, effect.Radius + u.BoundingRadius))
                .ToList();
            foreach (MatchUnit unit in affected)
            {
                foreach (EffectDefinition inner in effect.Inner)
                    RunOne(context, caster, inner, level, unit, point);
            }
        }

        private static void Dash(MatchUnit caster, MatchUnit target, FixedVector2 point)
        {
            if (target == caster)
            {
                caster.Position = point;
            }
            else
            {
                Fixed distance = FixedVector2.Distance(caster.Position, target.Position);
                Fixed stopShort = Fixed.FromInt(caster.BoundingRadius + target.BoundingRadius);
                if (distance > stopShort)
                    caster.Position = FixedVector2.MoveTowards(caster.Position, target.Position, distance - stopShort);
            }

            if (caster.Hero != null)
                caster.Hero.Path = null;
        }

        /// <summary>Arrives just outside the destination, on the side facing the caster's fountain.</summary>
        private static void Teleport(MatchState state, MatchUnit caster, MatchUnit destination)
        {
            FixedVector2 home = state.Ruleset.Map.FountainOf(caster.Team)!.Position;
            Fixed offset = Fixed.FromInt(destination.BoundingRadius + caster.BoundingRadius + 100);
            caster.Position = FixedVector2.MoveTowards(destination.Position, home, offset);
            if (caster.Hero != null)
                caster.Hero.Path = null;
        }

        private static void Knockback(MatchState state, MatchUnit caster, MatchUnit target, int distance)
        {
            FixedVector2 away = target.Position - caster.Position;
            Fixed length = away.Length;
            if (length <= Fixed.Zero)
                return;

            Fixed d = Fixed.FromInt(distance);
            FixedVector2 destination = new FixedVector2(target.Position.X + away.X * d / length, target.Position.Y + away.Y * d / length);
            FixedVector2 position = target.Position;
            while (position != destination)
            {
                FixedVector2 next = FixedVector2.MoveTowards(position, destination, KnockbackStep);
                if (!state.Nav.IsWalkable(next))
                    break;
                position = next;
            }

            target.Position = position;
            if (target.Hero != null)
                target.Hero.Path = null;
        }

        private static void Summon(SimulationContext<MatchState> context, MatchUnit caster, EffectDefinition effect, int level, FixedVector2 point)
        {
            CreepType type = context.State.Ruleset.FindCreepType(effect.SummonTypeId!)!;
            long now = context.Tick.Value;
            long expires = now + context.TickRate.TicksFor(effect.DurationMs);
            for (int i = 0; i < effect.AmountAt(level); i++)
            {
                MatchUnit unit = context.State.SpawnSummon(context.Ids.Allocate(), caster, type, now, expires);
                if (effect.AtPoint)
                {
                    unit.Position = point;
                    unit.StartPosition = point;
                }

                context.Emit(new UnitSummoned(unit.Id, caster.Id, type.Id, expires));
            }
        }
    }
}
