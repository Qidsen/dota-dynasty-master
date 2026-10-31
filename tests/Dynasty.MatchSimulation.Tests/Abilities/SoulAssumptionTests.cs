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
/// Soul Assumption (Dota 7.41f data and official notes): Visage gathers a charge each time a hero near it takes more than 100 damage at
/// once from a player (not from itself), up to a limit; the release deals base damage plus damage for each charge, spending them.
/// </summary>
public class SoulAssumptionTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Gathering = new ModifierSpec("soul_assumption", -1, dispellable: false, nearbyHeroDamageAbove: 100,
        nearbyHeroDamageBelow: 3000, nearbyRadius: 1800,
        onNearbyHeroDamage: new[] { EffectDefinition.Modifier(new ModifierSpec("soul_charge", 6000, maxStacks: 3, dispellable: false)) });

    private static readonly EffectDefinition[] Release =
    {
        EffectDefinition.Damage(DamageType.Pure, 20),
        EffectDefinition.DamagePerCasterStack(DamageType.Pure, "soul_charge", 70),
        EffectDefinition.OnCaster(EffectDefinition.Consume("soul_charge")),
    };

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        setup?.Invoke(draft);
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? alphaAbilities : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5600, 5000);
            H(ctx.State, "gamma").Position = P(9000, 9000);
        });
        return kernel;
    }

    private static int Charges(MatchUnit unit) => unit.Modifiers.Where(m => m.Spec.Key == "soul_charge").Sum(m => m.Stacks);

    [Fact]
    public void ACharge_ForEachBigEnoughHitOnAHeroNear_NotSmallOnes_NotFarOnes_NotSelfDamage_UpToTheLimit()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Gathering, H(ctx.State, "alpha").Id));
        At(kernel, 2, ctx =>
        {
            Damage.Deal(ctx, H(ctx.State, "gamma").Id, H(ctx.State, "beta"), Fixed.FromInt(150), DamageType.Pure);    // near: a charge
            Damage.Deal(ctx, H(ctx.State, "gamma").Id, H(ctx.State, "beta"), Fixed.FromInt(80), DamageType.Pure);     // too small
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "gamma"), Fixed.FromInt(150), DamageType.Pure);    // too far
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "beta"), Fixed.FromInt(150), DamageType.Pure);     // its own
        });
        int afterOne = 0;
        At(kernel, 3, ctx => afterOne = Charges(H(ctx.State, "alpha")));
        At(kernel, 4, ctx =>
        {
            for (int i = 0; i < 5; i++)
                Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(200), DamageType.Pure);   // Visage itself counts too
        });

        kernel.RunTicks(6);

        Assert.Equal(1, afterOne);
        Assert.Equal(3, Charges(H(kernel.State, "alpha")));
    }

    [Fact]
    public void TheRelease_DealsBasePlusForEachCharge_AndSpendsThem()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            for (int i = 0; i < 2; i++)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("soul_charge", 6000, maxStacks: 3, dispellable: false), H(ctx.State, "alpha").Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Release, 1, H(ctx.State, "beta"), H(ctx.State, "beta").Position));

        kernel.RunTicks(4);

        Assert.Equal(5000 - 20 - 2 * 70, H(kernel.State, "beta").Hp.ToDouble(), 0);
        Assert.Equal(0, Charges(H(kernel.State, "alpha")));
    }

    /// <summary>The AI releases with three charges or more, not with fewer.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    public void Ai_ReleasesWithEnoughCharges(int charges, bool releases)
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("soul", "Soul Assumption", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0,
            new[] { 4000 }, new[] { 0 }, new[] { 1 }, Release)), new[] { "soul" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
            for (int i = 0; i < charges; i++)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("soul_charge", 60000, maxStacks: 6, dispellable: false), H(ctx.State, "alpha").Id);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 * 30);

        Assert.Equal(releases, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "soul"));
    }
}
