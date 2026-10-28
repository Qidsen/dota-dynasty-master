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
                if (hero == null)
                    continue;
                foreach (AbilityState charged in hero.Abilities)
                    charged.RestoreCharges(now, context.TickRate);   // charges come back while dead too
                if (hero.IsDead || !unit.IsAlive)
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
                if ((caster.StartFlags & (ModifierFlags.Stunned | ModifierFlags.Silenced | ModifierFlags.Busy)) != 0)
                {
                    Fail(context, caster, order.AbilityId!, "interrupted");
                    return;
                }

                if (now >= hero.CastEndTick)
                    Complete(context, caster, hero, ability!, order, now);
                return;
            }

            // A stunned hero keeps its order and tries again once the stun ends.
            if ((caster.StartFlags & (ModifierFlags.Stunned | ModifierFlags.Busy)) != 0)
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
            if (hero.Mana < ManaCost(caster, ability))
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

            hero.Mana -= ManaCost(caster, ability);
            ability.CooldownReadyTick = now + context.TickRate.TicksFor((int)((long)definition.CooldownAt(ability.Level) * caster.CooldownLeftHundredths / 10000));
            ability.SpendCharge(now, context.TickRate);
            ConsumeCharge(context, caster, hero, ability);
            hero.CastEndTick = -1;
            HeroOrders.Issue(caster, HeroOrder.Idle);
            context.Emit(new AbilityCast(caster.Id, definition.Id, ability.Level, definition.Targeting == AbilityTargeting.Unit ? target.Id : EntityId.None, order.Point));

            // Acting reveals the caster first, so a smoke being cast is not broken by its own cast.
            Modifiers.BreakOnAction(context, caster);
            // Dota: a ready spell block on an enemy target stops a spell aimed at it (simplified: at the cast, not when the spell lands).
            ActiveModifier? block = definition.Targeting == AbilityTargeting.Unit && target.Team != caster.Team ? target.SpellBlocker : null;
            if (block != null && now >= block.NextProcTick)
            {
                block.NextProcTick = now + context.TickRate.TicksFor(block.Spec.SpellBlockCooldownMs);
                context.Emit(new SpellBlocked(target.Id, caster.Id, definition.Id));
                return;
            }

            FixedVector2 point = definition.Targeting == AbilityTargeting.Point ? order.Point : target.Position;
            Effects.RunSpell(context, caster, definition.Effects, ability.Level, target, point);
            foreach (ActiveModifier modifier in caster.Modifiers.Where(m => m.Spec.OnCast.Count > 0).ToList())
                Effects.Run(context, caster, modifier.Spec.OnCast, 1, target, point);
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
            HeroProgression.RefreshUpgrades(context, caster);
        }

        /// <summary>The ability's mana cost for this caster (Dota's Arcane rune makes it cheaper).</summary>
        internal static Fixed ManaCost(MatchUnit caster, AbilityState ability) =>
            Fixed.FromInt((int)((long)ability.Definition.ManaCostAt(ability.Level) * caster.ManaCostLeftHundredths / 10000));

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

        /// <summary>A damage effect's amount for this target: flat, or a percent of its max health, missing health or missing mana.</summary>
        private static Fixed ScaledAmount(EffectDefinition effect, int level, MatchUnit caster, MatchUnit target)
        {
            Fixed amount = Fixed.FromInt(effect.AmountAt(level));
            Fixed percent = amount / Fixed.FromInt(10000);   // scaled amounts are hundredths of a percent
            switch (effect.Scaling)
            {
                case DamageScaling.TargetMaxHealthPercent:
                    return Fixed.FromInt(target.MaxHp) * percent;
                case DamageScaling.TargetMissingHealthPercent:
                    return (Fixed.FromInt(target.MaxHp) - target.Hp) * percent;
                case DamageScaling.TargetCurrentHealthPercent:
                    return target.Hp * percent;
                case DamageScaling.TargetMaxManaPercent:
                    return target.Hero == null ? Fixed.Zero : Fixed.FromInt(target.Hero.MaxMana) * percent;
                case DamageScaling.TargetMissingManaPercent:
                    return target.Hero == null ? Fixed.Zero : (Fixed.FromInt(target.Hero.MaxMana) - target.Hero.Mana) * percent;
                case DamageScaling.CasterAttackDamagePercent:
                    return Fixed.FromRatio(caster.AttackDamageMin + caster.AttackDamageMax, 2) * percent;
                case DamageScaling.CasterStrengthPercent:
                    return caster.Strength * percent;
                case DamageScaling.CasterAgilityPercent:
                    return caster.Agility * percent;
                case DamageScaling.CasterIntelligencePercent:
                    return caster.Intelligence * percent;
                case DamageScaling.PerTargetStack:
                    return amount * Fixed.FromInt(target.FindModifier(effect.StackKey!, caster.Id)?.Stacks ?? 0);
                default:
                    return amount;
            }
        }

        /// <summary>A spell's effects: its damage gets the caster's spell amplification.</summary>
        public static void RunSpell(SimulationContext<MatchState> context, MatchUnit caster, IReadOnlyList<EffectDefinition> effects, int level, MatchUnit target, FixedVector2 point)
        {
            caster.CastingSpell++;
            try
            {
                Run(context, caster, effects, level, target, point);
            }
            finally
            {
                caster.CastingSpell--;
            }
        }

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
            if (!unit.IsAlive || unit.Kind == UnitKind.Rune || (unit != caster && (unit.Flags & ModifierFlags.Invulnerable) != 0))
                return false;
            if (unit.Kind == UnitKind.Structure || unit.Kind == UnitKind.Outpost)
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
                    Damage.Deal(context, caster.Id, target, ScaledAmount(effect, level, caster, target), effect.DamageType);
                    break;
                case EffectKind.ManaBurn:
                    if (target.Hero != null)
                    {
                        Fixed burnt = Fixed.Min(target.Hero.Mana, Fixed.FromInt(effect.AmountAt(level)));
                        target.Hero.Mana -= burnt;
                        if (effect.DamagePercent > 0 && burnt > Fixed.Zero)
                            Damage.Deal(context, caster.Id, target, burnt * Fixed.FromInt(effect.DamagePercent) / Fixed.FromInt(100), effect.DamageType);
                    }

                    break;
                case EffectKind.RestoreMana:
                    if (target.Hero != null && target.IsAlive)
                        target.Hero.Mana = Fixed.Min(Fixed.FromInt(target.Hero.MaxMana), target.Hero.Mana + Fixed.FromInt(effect.AmountAt(level)));
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
                    DisjointProjectiles(context, caster);
                    break;
                case EffectKind.Projectile:
                    Projectile projectile = context.State.LaunchProjectile(caster, target, effect.Speed);
                    projectile.Effects = effect.Inner;
                    projectile.Level = level;
                    context.Emit(new ProjectileLaunched(projectile.Id, caster.Id, target.Id, isAbility: true));
                    break;
                case EffectKind.Knockback:
                    Knockback(context.State, caster, target, effect.AmountAt(level));
                    break;
                case EffectKind.Summon:
                    Summon(context, caster, effect, level, point);
                    break;
                case EffectKind.Slashes:
                    ModifierSpec slashing = effect.ModifierAt(level);
                    if (Modifiers.Apply(context, caster, slashing, caster.Id) != null)
                    {
                        caster.Slashes = new SlashState(slashing.Key, effect.Radius, effect.AmountAt(level), context.Tick.Value);
                        caster.TargetId = target.Id;
                        SlashSystem.Slash(context, caster, target);
                    }
                    break;
                case EffectKind.Teleport:
                    Teleport(context.State, caster, target);
                    DisjointProjectiles(context, caster);
                    break;
                case EffectKind.Dispel:
                    Dispel(context, caster, target, effect.Strong);
                    break;
                case EffectKind.OnCaster:
                    foreach (EffectDefinition inner in effect.Inner)
                        RunOne(context, caster, inner, level, caster, point);
                    break;
                case EffectKind.Zone:
                    FixedVector2 at = effect.Center == AreaCenter.Caster ? caster.Position : effect.Center == AreaCenter.Target ? target.Position : point;
                    context.State.AddZone(caster.Id, at, effect, level, context.Tick.Value);
                    break;
                case EffectKind.Pull:
                    Pull(context.State, caster, target, effect.Center == AreaCenter.Point ? point : caster.Position,
                        effect.Center == AreaCenter.Point ? 0 : caster.BoundingRadius + target.BoundingRadius, effect.AmountAt(level));
                    break;
                case EffectKind.Illusion:
                    if (target.Hero != null || target.IllusionOf.IsValid)
                    {
                        long expires = context.Tick.Value + context.TickRate.TicksFor(effect.DurationMs);
                        MatchUnit original = context.State.Find(target.IllusionOf) ?? target;
                        for (int i = 0; i < effect.AmountAt(level); i++)
                        {
                            MatchUnit image = context.State.SpawnIllusion(context.Ids.Allocate(), caster, original, context.Tick.Value, expires);
                            Modifiers.AddInstance(context, image, effect.ModifierAt(level), caster.Id);
                            context.Emit(new UnitSummoned(image.Id, caster.Id, image.TypeId, expires));
                        }
                    }

                    break;
                case EffectKind.IfMelee:
                case EffectKind.IfRanged:
                    if ((target.Stats.ProjectileSpeed == 0) == (effect.Kind == EffectKind.IfMelee))
                    {
                        foreach (EffectDefinition inner in effect.Inner)
                            RunOne(context, caster, inner, level, target, point);
                    }

                    break;
                case EffectKind.IfHero:
                    if (target.Hero != null)
                    {
                        foreach (EffectDefinition inner in effect.Inner)
                            RunOne(context, caster, inner, level, target, point);
                    }

                    break;
                case EffectKind.Execute:
                    if (target.IsAlive && target.Hp <= Fixed.FromInt(effect.AmountAt(level)))
                        Damage.Execute(context, caster.Id, target);
                    break;
                case EffectKind.RefreshCooldowns:
                    if (caster.Hero != null)
                    {
                        long now = context.Tick.Value;
                        foreach (AbilityState ability in caster.Hero.Abilities.Concat(caster.Hero.Inventory.Where(i => i.Active != null).Select(i => i.Active!)))
                        {
                            if (!ability.Definition.Effects.SelectMany(e => e.SelfAndDescendants()).Any(e => e.Kind == EffectKind.RefreshCooldowns) && ability.CooldownReadyTick > now)
                                ability.CooldownReadyTick = now;
                        }
                    }

                    break;
                case EffectKind.Attack:
                    if (target.IsAlive && context.State.CanAttack(caster, target))
                        AttackSystem.Strike(context, caster, target, effect.AmountAt(level));
                    break;
            }
        }

        private static void RunArea(SimulationContext<MatchState> context, MatchUnit caster, EffectDefinition effect, int level, MatchUnit target, FixedVector2 point)
        {
            FixedVector2 center = effect.Center == AreaCenter.Caster ? caster.Position
                : effect.Center == AreaCenter.Target ? target.Position
                : point;
            RunAround(context, caster, effect, level, center, point);
        }

        /// <summary>One pulse of a ground zone: its effects on the matching units inside, with the zone's point as the point.</summary>
        public static void Pulse(SimulationContext<MatchState> context, MatchUnit caster, GroundZone zone)
        {
            caster.CastingSpell++;
            try
            {
                RunAround(context, caster, zone.Definition, zone.Level, zone.Position, zone.Position);
            }
            finally
            {
                caster.CastingSpell--;
            }
        }

        private static void RunAround(SimulationContext<MatchState> context, MatchUnit caster, EffectDefinition effect, int level, FixedVector2 center, FixedVector2 point)
        {
            List<MatchUnit> affected = context.State.Units
                .Where(u => Matches(context.State, caster, u, effect.Team) && (!effect.HeroesOnly || u.Hero != null) &&
                            Geometry.WithinRange(center, u.Position, effect.Radius + u.BoundingRadius))
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

        /// <summary>Blinking or teleporting makes projectiles homing on the unit lose it.</summary>
        private static void DisjointProjectiles(SimulationContext<MatchState> context, MatchUnit unit)
        {
            foreach (Projectile projectile in context.State.Disjoint(unit))
                context.Emit(new ProjectileDodged(projectile.Id, projectile.Source, unit.Id));
        }

        /// <summary>Allies lose their dispellable debuffs, enemies their dispellable buffs; stuns need a strong dispel.</summary>
        internal static void Dispel(SimulationContext<MatchState> context, MatchUnit caster, MatchUnit target, bool strong)
        {
            bool ally = target.Team == caster.Team;
            foreach (ActiveModifier modifier in target.Modifiers.ToList())
            {
                ModifierSpec spec = modifier.Spec;
                if (spec.IsPermanent || !spec.Dispellable || spec.IsDebuff != ally)
                    continue;
                if ((spec.Flags & ModifierFlags.Stunned) != 0 && !strong)
                    continue;
                Modifiers.Remove(context, target, modifier, "dispelled");
            }
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

        /// <summary>Moves the target toward <paramref name="destination"/> by up to the distance, stopping <paramref name="stopShort"/> before it or at the first unwalkable step.</summary>
        private static void Pull(MatchState state, MatchUnit caster, MatchUnit target, FixedVector2 destination, int stopShort, int distance)
        {
            if (target == caster)
                return;
            Fixed room = FixedVector2.Distance(target.Position, destination) - Fixed.FromInt(stopShort);
            Fixed travel = Fixed.Min(room, Fixed.FromInt(distance));
            FixedVector2 position = target.Position;
            FixedVector2 end = FixedVector2.MoveTowards(position, destination, Fixed.Max(Fixed.Zero, travel));
            while (position != end)
            {
                FixedVector2 next = FixedVector2.MoveTowards(position, end, KnockbackStep);
                if (!state.Nav.IsWalkable(next))
                    break;
                position = next;
            }

            target.Position = position;
            if (target.Hero != null)
                target.Hero.Path = null;
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
                foreach (EffectDefinition inner in effect.Inner)
                    RunOne(context, caster, inner, level, unit, point);
            }
        }
    }
}
