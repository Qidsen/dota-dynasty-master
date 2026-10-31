using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Blind (Dota's miss chance: Blinding Light, Smoke Screen, Laser): the blinded unit's attacks miss by the chance; with Dota's stacking
/// two sources multiply like evasion.
/// </summary>
public class BlindTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static SimulationKernel<MatchState> Arena(bool dota)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.DotaStacking = dota;
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5100, 5000);
        }));
        return kernel;
    }

    [Theory]
    [InlineData(10000, 0, 0)]
    [InlineData(5000, 120, 280)]
    [InlineData(0, 400, 400)]
    public void ABlindedUnitsAttacks_MissByTheChance(int missHundredths, int fewest, int most)
    {
        var kernel = Arena(dota: false);
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            if (missHundredths > 0)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("blind", -1, missChanceHundredths: missHundredths), H(ctx.State, "beta").Id);
        }));
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            for (int i = 0; i < 400; i++)
                AttackSystem.ApplyHit(ctx, H(ctx.State, "alpha"), H(ctx.State, "beta"), Fixed.One, critical: false);
        }));

        kernel.RunTicks(3);

        int hits = kernel.EventsOf<DamageDealt>().Count(d => d.Attacker == H(kernel.State, "alpha").Id);
        Assert.InRange(hits, fewest, most);
    }

    [Fact]
    public void TwoBlinds_Multiply_WithDotaStacking()
    {
        var kernel = Arena(dota: true);
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("blind_a", -1, missChanceHundredths: 5000), H(ctx.State, "beta").Id);
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("blind_b", -1, missChanceHundredths: 5000), H(ctx.State, "beta").Id);
        }));

        kernel.RunTicks(3);

        Assert.Equal(7500, H(kernel.State, "alpha").MissChanceHundredths);
    }
}
