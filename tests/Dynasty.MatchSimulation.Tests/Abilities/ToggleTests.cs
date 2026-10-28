using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.Abilities.TestAbilities;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Toggled abilities (Dota's Pulse Nova: "creates waves of damaging energy around Leshrac, one per second ... drains Leshrac's mana
/// with each pulse"): casting turns the level's modifier on for the cast's mana and cooldown, casting again turns it off for free;
/// the modifier drains mana every period and turns itself off when the mana runs out.
/// </summary>
public class ToggleTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static RulesetDraft WithNova()
    {
        var draft = Draft();
        var pulse = new ModifierSpec("nova_burn", 1000, damagePerSecond: Fixed.FromInt(50), damageType: DamageType.Pure);
        draft.Abilities.Add(new AbilityDefinition("nova", "Nova", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
            new[] { 1000 }, new[] { 60 }, new[] { 1 }, Array.Empty<EffectDefinition>(),
            toggle: new[] { new ModifierSpec("nova", -1, manaCostPerSecond: Fixed.FromInt(20), aura: new AuraSpec(500, TargetTeam.Enemy, false, pulse)) }));
        draft.WithHeroAbilities("alpha", new[] { "nova" });
        return draft;
    }

    private static SimulationKernel<MatchState> Arena(RulesetDraft draft, Action<SimulationContext<MatchState>>? setup = null)
    {
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "gamma").Position = P(3000, 3000);
            H(ctx.State, "beta").Position = P(5300, 3000);
            H(ctx.State, "delta").Position = P(8000, 8000);
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(200);
            setup?.Invoke(ctx);
        });
        return kernel;
    }

    private static bool On(MatchState state) => H(state, "alpha").Modifiers.Any(m => m.Spec.Key == "nova");

    [Fact]
    public void Toggle_TurnsOnForItsCost_PulsesAndDrainsManaEverySecond()
    {
        var kernel = Arena(WithNova());
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("nova")));
        Fixed manaAfterCast = Fixed.Zero;
        At(kernel, 3, ctx => manaAfterCast = H(ctx.State, "alpha").Hero!.Mana);

        kernel.RunTicks(2 + 3 * 30 + 5);

        Assert.True(On(kernel.State));
        Assert.True(manaAfterCast <= Fixed.FromInt(140) + Fixed.One, $"60 to turn it on: {manaAfterCast.ToDouble():0}");
        Assert.True(H(kernel.State, "alpha").Hero!.Mana < manaAfterCast - Fixed.FromInt(50), "about 20 a second since");
        Assert.True(kernel.EventsOf<DamageDealt>().Count(d => d.Attacker == H(kernel.State, "alpha").Id && d.Target == H(kernel.State, "beta").Id) >= 2);
    }

    [Fact]
    public void Toggle_CastAgain_TurnsItOffForFree_EvenOnCooldown()
    {
        var kernel = Arena(WithNova());
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("nova")));
        Fixed before = Fixed.Zero;
        At(kernel, 5, ctx =>
        {
            before = H(ctx.State, "alpha").Hero!.Mana;
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("nova"));   // within the 1 s cooldown
        });

        kernel.RunTicks(8);

        Assert.False(On(kernel.State));
        Assert.True(H(kernel.State, "alpha").Hero!.Mana >= before);
    }

    [Fact]
    public void Toggle_TurnsItselfOff_WhenTheManaRunsOut()
    {
        var kernel = Arena(WithNova());
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("nova")));
        At(kernel, 4, ctx => H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(25));   // one more second at most

        kernel.RunTicks(4 + 3 * 30);

        Assert.False(On(kernel.State));
        Assert.True(H(kernel.State, "alpha").Hero!.Mana >= Fixed.Zero, "it stops before the mana would go below zero");
    }

    /// <summary>The AI turns a toggle on with an enemy in its reach and off once no enemy is near (it does not leave it draining mana).</summary>
    [Fact]
    public void Ai_TurnsTheToggleOnNearEnemies_AndOffWithoutThem()
    {
        var kernel = Arena(WithNova());
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        foreach (string hero in new[] { "beta", "gamma", "delta" })
            settings.Uncontrolled.Add(hero);
        HeroAi.Attach(kernel, settings);
        bool onNearEnemy = false;
        At(kernel, 20, ctx => onNearEnemy = On(ctx.State));
        At(kernel, 21, ctx => H(ctx.State, "beta").Position = P(9000, 9000));   // gone

        kernel.RunTicks(21 + 3 * 30);

        Assert.True(onNearEnemy);
        Assert.False(On(kernel.State));
    }
}
