using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Keeps bound units (Soulbind) within their leash of each other after every move of the tick: a unit farther than the leash from
    /// where its partner stood at the start of the tick is put back on the leash's edge, so a partner standing still is not dragged and
    /// the order units are processed in does not matter. A partner dead or without its side of the bind holds nothing.
    /// </summary>
    internal sealed class LeashSystem : ISimulationSystem<MatchState>
    {
        public string Name => "leash";

        /// <summary>
        /// Dead in the Water: the unit ends a tick at most the chain's length and one drag step from the anchor, and the anchor follows
        /// to the chain's length: within the chain it walks freely, beyond it gains only the drag speed.
        /// </summary>
        private static void Drag(SimulationContext<MatchState> context, MatchUnit unit, ActiveModifier anchor)
        {
            Fixed chain = Fixed.FromInt(anchor.Spec.AnchorChain);
            FixedVector2 offset = unit.Position - anchor.Mark;
            if (offset.LengthSquared <= chain * chain)
                return;
            Fixed reach = chain + Fixed.FromRatio(anchor.Spec.AnchorDragSpeed, context.TickRate.TicksPerSecond);
            Fixed length = offset.Length;
            if (length > reach)
            {
                unit.Position = anchor.Mark + offset * (reach / length);
                length = reach;
            }

            anchor.Mark = unit.Position - (unit.Position - anchor.Mark) * (chain / length);
        }

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            foreach (MatchUnit unit in state.Heroes)
            {
                if (!unit.IsAlive || unit.Modifiers.Count == 0)
                    continue;
                foreach (ActiveModifier anchor in unit.Modifiers)
                {
                    if (anchor.Spec.AnchorChain > 0)
                        Drag(context, unit, anchor);
                }

                foreach (ActiveModifier bind in unit.Modifiers.Where(m => m.Partner.IsValid).ToList())
                {
                    MatchUnit? partner = state.Find(bind.Partner);
                    if (partner == null || !partner.Modifiers.Any(m => m.Partner == unit.Id))
                        continue;   // the partner died or its side of the bind is gone: no leash

                    FixedVector2 offset = unit.Position - partner.StartPosition;
                    if (offset.LengthSquared <= bind.LeashDistance * bind.LeashDistance)
                        continue;
                    unit.Position = partner.StartPosition + offset * (bind.LeashDistance / offset.Length);
                }
            }
        }
    }
}
