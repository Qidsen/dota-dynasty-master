using System.Collections.Generic;
using Dynasty.Core.Ids;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Combat
{
    /// <summary>
    /// An area on the ground left by an ability (Macropyre, Midnight Pulse): every period it runs its effects
    /// on the matching units inside, with its source as the caster, until it expires.
    /// </summary>
    public sealed class GroundZone
    {
        internal GroundZone(long id, EntityId source, FixedVector2 position, EffectDefinition definition, int level, long expiresTick, long nextPulseTick)
        {
            Id = id;
            Source = source;
            Position = position;
            Definition = definition;
            Level = level;
            ExpiresTick = expiresTick;
            NextPulseTick = nextPulseTick;
        }

        public long Id { get; }
        public EntityId Source { get; }
        public FixedVector2 Position { get; }
        public int Radius => Definition.Radius;
        public long ExpiresTick { get; }

        /// <summary>The zone effect: radius, team, period and the effects of each pulse.</summary>
        internal EffectDefinition Definition { get; }
        internal int Level { get; }
        internal long NextPulseTick { get; set; }

        internal IReadOnlyList<EffectDefinition> Effects => Definition.Inner;
    }
}
