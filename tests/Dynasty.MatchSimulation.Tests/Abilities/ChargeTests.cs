using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.Abilities.TestAbilities;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Abilities with charges (Shrapnel, Phantom Strike, Fire Remnant): each cast spends a charge, a spent charge
/// comes back after the restore time, and the short cooldown separates casts.
/// </summary>
public class ChargeTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    /// <summary>Two charges, one back every 3 s, 0.5 s between casts; a free nuke with no cast point.</summary>
    private static readonly AbilityDefinition Volley = new("volley", "Volley", AbilityTargeting.Unit, TargetTeam.Enemy,
        castRange: 600, castPointMs: 0, cooldownMs: new[] { 500 }, manaCost: new[] { 0 }, requiredHeroLevel: new[] { 1 },
        effects: new[] { EffectDefinition.Damage(DamageType.Pure, 10) }, maxCharges: new[] { 2 }, chargeRestoreMs: new[] { 3000 });

    private static SimulationKernel<MatchState> Arena(params long[] castTicks)
    {
        RulesetDraft draft = Draft();
        draft.Abilities.Add(Volley);
        draft.WithHeroAbilities("alpha", "volley");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "beta").Position = P(5200, 3000);
        }));
        foreach (long tick in castTicks)
            kernel.AddSystem(new LambdaSystem(tick, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("volley", H(ctx.State, "beta").Id))));
        return kernel;
    }

    [Fact]
    public void Charges_AllowCastsUntilSpent_ThenRestoreOneAtATime()
    {
        // Orders resolve on the next tick. Casts 0.5 s apart use both charges, the third finds none; one charge is back 3 s (90 ticks) after the first cast.
        var kernel = Arena(20, 36, 52, 105, 115);

        kernel.RunTicks(200);

        Assert.Equal(new long[] { 21, 37, 116 }, kernel.TimedEventsOf<AbilityCast>().Select(e => e.Item1));
        Assert.Equal(2, kernel.EventsOf<AbilityCastFailed>().Count(f => f.Reason == "cooldown"));   // at 53 and at 106, before the charge is back at 111
    }

    [Fact]
    public void Charges_StartFullWhenLearned_AndTheCooldownStillSeparatesCasts()
    {
        var kernel = Arena(20, 22);

        kernel.RunTicks(40);

        Assert.Single(kernel.EventsOf<AbilityCast>());
        Assert.Equal(1, H(kernel.State, "alpha").Hero!.Ability("volley")!.Charges);
    }
}
