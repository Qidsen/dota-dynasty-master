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
    /// <summary>A ring wall standing from <see cref="FormsTick"/> to <see cref="ExpiresTick"/> that units of the blocked team can't cross.</summary>
    public sealed class GroundWall
    {
        internal GroundWall(FixedVector2 position, int radius, Team team, TargetTeam blocks, long formsTick, long expiresTick)
        {
            Position = position;
            Radius = radius;
            Team = team;
            Blocks = blocks;
            FormsTick = formsTick;
            ExpiresTick = expiresTick;
        }

        public FixedVector2 Position { get; }
        public int Radius { get; }

        /// <summary>The caster's team; <see cref="Blocks"/> is relative to it.</summary>
        public Team Team { get; }
        public TargetTeam Blocks { get; }
        public long FormsTick { get; }
        public long ExpiresTick { get; }

        /// <summary>The units inside when the wall formed or when they first came under it, by id; kept on their side while it stands.</summary>
        internal HashSet<EntityId> Inside { get; } = new HashSet<EntityId>();

        /// <summary>Units whose side is already known (all units present since the wall formed).</summary>
        internal HashSet<EntityId> Known { get; } = new HashSet<EntityId>();
    }
}
