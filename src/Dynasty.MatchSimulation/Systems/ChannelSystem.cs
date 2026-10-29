using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Channels under way (see <see cref="ChannelSpec"/>): pulses every period, the end effects when completed; a stun, a silence,
    /// fear, a taunt, death or a new order end it early, without the end effects. The hero stands meanwhile: it has no order, and
    /// any order would end the channel.
    /// </summary>
    internal sealed class ChannelSystem : ISimulationSystem<MatchState>
    {
        public string Name => "channels";

        internal static void Start(SimulationContext<MatchState> context, MatchUnit caster, HeroState hero, AbilityDefinition ability, int level, MatchUnit target, FixedVector2 point)
        {
            ChannelSpec spec = ability.Channel!;
            long now = context.Tick.Value;
            hero.Channel = new ChannelState(ability, level, target.Id, point, now, now + context.TickRate.TicksFor(spec.DurationAt(level))) { StartTick = now };
            if (spec.WhileChannelling != null)
                Modifiers.Apply(context, caster, spec.WhileChannelling, caster.Id);
        }

        public void Tick(SimulationContext<MatchState> context)
        {
            long now = context.Tick.Value;
            foreach (MatchUnit unit in context.State.Heroes.Where(h => h.Hero!.Channel != null).ToList())
            {
                ChannelState channel = unit.Hero!.Channel!;
                ChannelSpec spec = channel.Ability.Channel!;
                if (!unit.IsAlive || channel.Cancelled ||
                    (unit.StartFlags & (ModifierFlags.Stunned | ModifierFlags.Silenced | ModifierFlags.Feared | ModifierFlags.Taunted)) != 0)
                {
                    End(context, unit, completed: false);
                    continue;
                }

                MatchUnit target = context.State.Find(channel.Target) ?? unit;
                long period = System.Math.Max(1, context.TickRate.TicksFor(spec.PeriodMs));
                while (spec.Pulse.Count > 0 && now >= channel.NextPulseTick && channel.NextPulseTick < channel.EndTick)
                {
                    Effects.RunSpell(context, unit, spec.Pulse, channel.Level, target, channel.Point);
                    channel.NextPulseTick += period;
                }

                if (now >= channel.EndTick)
                    End(context, unit, completed: true);
            }
        }

        private static void End(SimulationContext<MatchState> context, MatchUnit unit, bool completed)
        {
            ChannelState channel = unit.Hero!.Channel!;
            unit.Hero.Channel = null;
            ChannelSpec spec = channel.Ability.Channel!;
            foreach (ActiveModifier modifier in unit.Modifiers.Where(m => spec.WhileChannelling != null && m.Spec.Key == spec.WhileChannelling.Key).ToList())
                Modifiers.Remove(context, unit, modifier, completed ? "channel_completed" : "channel_interrupted");
            if (completed && unit.IsAlive && spec.End.Count > 0)
                Effects.RunSpell(context, unit, spec.End, channel.Level, context.State.Find(channel.Target) ?? unit, channel.Point);
        }
    }
}
