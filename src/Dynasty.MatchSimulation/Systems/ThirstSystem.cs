using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Thirst's movement speed, worked out every tick from the enemy heroes' health (Dota counts it wherever they are): each living enemy
    /// hero that is not an illusion and is below the starting share of its health adds a part of the bonus, all of it at the full share.
    /// </summary>
    internal sealed class ThirstSystem : ISimulationSystem<MatchState>
    {
        public string Name => "thirst";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            foreach (MatchUnit unit in state.Heroes)
            {
                ModifierSpec? thirst = null;
                foreach (ActiveModifier modifier in unit.Modifiers)
                {
                    if (modifier.Spec.ThirstMoveSpeedPercent > 0)
                    {
                        thirst = modifier.Spec;
                        break;
                    }
                }

                int bonus = thirst == null || !unit.IsAlive ? 0 : Bonus(state, unit, thirst);
                if (bonus == unit.ThirstHundredths)
                    continue;
                unit.ThirstHundredths = bonus;
                unit.Recalculate();
            }
        }

        private static int Bonus(MatchState state, MatchUnit unit, ModifierSpec thirst)
        {
            long span = System.Math.Max(1, thirst.ThirstFromHealthPercent - thirst.ThirstFullBelowHealthPercent);
            long bonus = 0;
            foreach (MatchUnit enemy in state.Heroes)
            {
                if (enemy.Team == unit.Team || !enemy.IsAlive || enemy.MaxHp <= 0)   // illusions are not heroes
                    continue;
                // Missing share below the start, in hundredths of a percent of the health, up to the full span.
                long health = enemy.Hp.ToIntFloor() * 10000L / enemy.MaxHp;
                long below = System.Math.Min(span * 100, System.Math.Max(0, thirst.ThirstFromHealthPercent * 100L - health));
                bonus += thirst.ThirstMoveSpeedPercent * 100L * below / (span * 100);
            }

            return (int)bonus;
        }
    }
}
