using System.Collections.Generic;
using Dynasty.Core.Ids;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Combat
{
    /// <summary>
    /// A homing projectile in flight: an attack (damage already rolled at launch) or an ability
    /// (effects applied on arrival). Disappears when its target dies or disjoints.
    /// </summary>
    public sealed class Projectile
    {
        internal Projectile(long id, EntityId source, EntityId target, FixedVector2 position, Fixed stepPerTick)
        {
            Id = id;
            Source = source;
            Target = target;
            Position = position;
            StepPerTick = stepPerTick;
        }

        public long Id { get; }
        public EntityId Source { get; }
        public EntityId Target { get; }
        public FixedVector2 Position { get; internal set; }
        internal Fixed StepPerTick { get; }

        // Attack payload.
        internal Fixed Damage { get; set; }
        internal bool IsCritical { get; set; }

        // Ability payload.
        internal IReadOnlyList<EffectDefinition>? Effects { get; set; }
        internal int Level { get; set; }

        public bool IsAbility => Effects != null;
    }
}
