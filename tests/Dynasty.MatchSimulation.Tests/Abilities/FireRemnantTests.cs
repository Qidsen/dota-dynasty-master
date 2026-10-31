using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Fire Remnant and Activate Fire Remnant (Dota 7.41f data and the official description): a remnant waits at the point; the activation
/// explodes every remnant of Ember's, each hurting the enemies around it, and Ember ends at the remnant closest to the targeted point.
/// </summary>
public class FireRemnantTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition Remnant = EffectDefinition.Summon("remnant", 45000, true, new[] { 1 }, Array.Empty<int>(),
        EffectDefinition.Modifier(new ModifierSpec("remnant", -1, ModifierFlags.Invulnerable | ModifierFlags.Rooted, dispellable: false)));

    private static readonly EffectDefinition[] Activate =
    {
        EffectDefinition.OnOwnUnits("remnant", false, EffectDefinition.Area(450, AreaCenter.Target, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 100))),
        EffectDefinition.JauntTo("remnant"),
        EffectDefinition.OnOwnUnits("remnant", true),
    };

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("remnant", new UnitStats(1, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16, visionRange: 0), new Bounty(0, 0, 0)));
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
            H(ctx.State, "beta").Position = P(6000, 5000);
            H(ctx.State, "gamma").Position = P(5000, 6000);
            foreach (string id in new[] { "beta", "gamma" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, id).Id);
        });
        return kernel;
    }

    private static List<MatchUnit> Remnants(MatchState state) => state.Units.Where(u => u.TypeId == "remnant" && u.IsAlive).ToList();

    [Fact]
    public void Activation_ExplodesEveryRemnant_AndEndsAtTheOneClosestToThePoint()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Remnant }, 1, H(ctx.State, "alpha"), P(6100, 5000));   // by beta
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Remnant }, 1, H(ctx.State, "alpha"), P(5000, 6100));   // by gamma
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Remnant }, 1, H(ctx.State, "alpha"), P(5900, 5000));   // by beta again
        });
        int placed = 0;
        At(kernel, 2, ctx => placed = Remnants(ctx.State).Count);
        At(kernel, 3, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Activate, 1, H(ctx.State, "alpha"), P(5000, 6500)));

        kernel.RunTicks(6);

        Assert.Equal(3, placed);
        Assert.Equal(5000 - 2 * 100, H(kernel.State, "beta").Hp.ToDouble(), 0);   // two remnants by him
        Assert.Equal(5000 - 100, H(kernel.State, "gamma").Hp.ToDouble(), 0);
        Assert.True(FixedVector2.Distance(H(kernel.State, "alpha").Position, P(5000, 6100)) < Fixed.FromInt(60));   // the one closest to the point
        Assert.Empty(Remnants(kernel.State));
    }

    /// <summary>The AI activates its remnants when one is by an enemy hero, not otherwise.</summary>
    [Theory]
    [InlineData(6100, true)]
    [InlineData(3000, false)]
    public void Ai_Activates_WithARemnantByAnEnemy(int remnantX, bool activates)
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("activate", "Activate Fire Remnant", AbilityTargeting.Point, TargetTeam.Enemy, 99999, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, Activate)), new[] { "activate" });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
            H(ctx.State, "gamma").Position = P(9000, 9000);
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Remnant }, 1, H(ctx.State, "alpha"), P(remnantX, 5000));
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30);

        Assert.Equal(activates, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "activate"));
    }
}
