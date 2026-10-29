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

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            foreach (MatchUnit unit in state.Heroes)
            {
                if (!unit.IsAlive || unit.Modifiers.Count == 0)
                    continue;
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
