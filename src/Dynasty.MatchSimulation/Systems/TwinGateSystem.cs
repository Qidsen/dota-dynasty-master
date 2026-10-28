using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Dota's Twin Gates (<see cref="TwinGateRules"/>): runs the channel of heroes ordered to use a gate (the hero order system walks
    /// them there) — starting it within range when not rooted and the mana is there, breaking it on a stun, a root or another order
    /// with the mana given back, and moving the hero by the other gate when it ends.
    /// </summary>
    internal sealed class TwinGateSystem : ISimulationSystem<MatchState>
    {
        public string Name => "twin-gates";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            TwinGateRules? rules = state.Ruleset.TwinGates;
            if (rules == null)
                return;
            long now = context.Tick.Value;
            foreach (MatchUnit unit in state.Heroes)
            {
                HeroState hero = unit.Hero!;
                bool ordered = hero.Order.Kind == HeroOrderKind.UseGate && unit.IsAlive;
                if (hero.GateChannelEndTick >= 0)
                {
                    bool broken = !ordered || (unit.Flags & (ModifierFlags.Stunned | ModifierFlags.Rooted)) != 0;
                    if (broken)
                    {
                        hero.Mana += Fixed.FromInt(hero.GateChannelMana);   // Valve 7.40: an interrupted channel gives the mana back
                        hero.GateChannelEndTick = -1;
                        continue;
                    }

                    if (now < hero.GateChannelEndTick)
                        continue;
                    (FixedVector2 from, FixedVector2 to) = rules.Route(hero.Order.Point);
                    var center = FixedVector2.FromInts(state.Ruleset.Map.Width / 2, state.Ruleset.Map.Height / 2);
                    unit.Position = FixedVector2.MoveTowards(to, center, Fixed.FromInt(rules.Radius));
                    unit.StartPosition = unit.Position;
                    hero.Path = null;
                    hero.GateChannelEndTick = -1;
                    HeroOrders.Issue(unit, HeroOrder.Idle);
                    context.Emit(new TwinGateUsed(unit.Id, from, to));
                    continue;
                }

                if (!ordered || (unit.Flags & (ModifierFlags.Stunned | ModifierFlags.Rooted)) != 0)
                    continue;
                FixedVector2 gate = rules.Route(hero.Order.Point).From;
                if (!Geometry.WithinRange(unit.Position, gate, rules.Radius))
                    continue;
                // Units without mana use the gates for free (Valve 7.35).
                int cost = hero.MaxMana > 0 ? rules.ManaCost : 0;
                if (hero.Mana < Fixed.FromInt(cost))
                {
                    HeroOrders.Issue(unit, HeroOrder.Idle);
                    continue;
                }

                hero.Mana -= Fixed.FromInt(cost);
                hero.GateChannelMana = cost;
                hero.GateChannelEndTick = now + context.TickRate.TicksFor(rules.ChannelMs);
            }
        }
    }
}
