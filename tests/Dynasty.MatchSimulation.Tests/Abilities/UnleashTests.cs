using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Unleash (Dota 7.41f data and official notes): Fury combos of five fast strikes, above the attack speed limit, each slowing the target;
/// the last strike of a combo pulses damage around the target; between combos Marci cannot attack for 1.5 s; no new combo after Unleash.
/// </summary>
public class UnleashTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Slow = new ModifierSpec("pulse_slow", 2000, moveSpeedPercent: -30, attackSpeedBonus: -60);

    private static EffectDefinition[] Unleash(int durationMs) => new[]
    {
        EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("unleash", durationMs, moveSpeedPercent: 15, attackSpeedBonus: 1000,
            noAttackSpeedLimit: true, attackCharges: 5, rechargeMs: 1500, onHit: new[] { EffectDefinition.Modifier(Slow) },
            onLastCharge: new[] { EffectDefinition.Area(800, AreaCenter.Target, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 100), EffectDefinition.Modifier(Slow)) }))),
    };

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.MaxAttackSpeed = 700;
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5150, 5000);
            H(ctx.State, "gamma").Position = P(5150, 5400);
            foreach (string id in new[] { "beta", "gamma" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Stunned), H(ctx.State, id).Id);
        });
        return kernel;
    }

    private static List<long> HitsOnBeta(SimulationKernel<MatchState> kernel) => kernel.TimedEventsOf<DamageDealt>()
        .Where(e => e.Event.Attacker == H(kernel.State, "alpha").Id && e.Event.Target == H(kernel.State, "beta").Id && e.Event.DamageType == DamageType.Physical)
        .Select(e => e.Tick).ToList();

    [Fact]
    public void FiveFastStrikes_APulseOnTheLast_ThenAPause_ThenTheNextCombo()
    {
        var kernel = Arena();
        At(kernel, 2, ctx =>
        {
            Effects.Run(ctx, H(ctx.State, "alpha"), Unleash(16000), 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id));
        });

        kernel.RunTicks(2 + 4 * 30);

        List<long> hits = HitsOnBeta(kernel);
        Assert.True(hits.Count >= 10, "two combos: " + string.Join(",", hits));
        Assert.True(hits[4] - hits[0] <= 20, "a fast combo: " + string.Join(",", hits));
        Assert.True(hits[5] - hits[4] >= 45, "a 1.5 s pause between combos: " + string.Join(",", hits));
        Assert.True(hits[9] - hits[5] <= 20, "the next combo is fast again: " + string.Join(",", hits));
        // The pulses after the fifth and the tenth strikes hit gamma, 400 away from beta.
        Assert.Equal(2, kernel.EventsOf<DamageDealt>().Count(d => d.Target == H(kernel.State, "gamma").Id && d.DamageType == DamageType.Pure));
        Assert.Contains(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "pulse_slow");
    }

    [Fact]
    public void FuryGoesAboveTheAttackSpeedLimit()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Unleash(16000), 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position));

        kernel.RunTicks(4);

        Assert.Equal(1000, H(kernel.State, "alpha").AttackSpeedBonus);
    }

    [Fact]
    public void WhileRecharging_NoFasterAttacks()
    {
        var kernel = Arena();
        At(kernel, 2, ctx =>
        {
            Effects.Run(ctx, H(ctx.State, "alpha"), Unleash(16000), 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id));
        });
        var bonus = new Dictionary<long, int>();
        for (long t = 3; t < 2 + 3 * 30; t++)
        {
            long tick = t;
            At(kernel, tick, ctx => bonus[tick] = H(ctx.State, "alpha").AttackSpeedBonus);
        }

        kernel.RunTicks(2 + 3 * 30);

        List<long> hits = HitsOnBeta(kernel);
        int during = bonus[(hits[4] + hits[5]) / 2];
        Assert.Equal(0, during);
    }
}
