using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Bone Guard (Dota 7.41f data and the official description): a charge for every 2 creeps Wraith King kills and 2 per hero, up to a
/// limit; the cast spends them all on a skeleton each, and a skeleton comes back once when killed.
/// </summary>
public class BoneGuardTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Half charges: a creep 1, a hero 4; at most 8 (four charges).</summary>
    private static readonly ModifierSpec Half = new ModifierSpec("bone_half", -1, maxStacks: 8, dispellable: false);

    private static readonly ModifierSpec Gathering = new ModifierSpec("bone_guard", -1, dispellable: false,
        onKill: new[]
        {
            EffectDefinition.IfHero(EffectDefinition.OnCaster(EffectDefinition.ModifierTimes(new[] { 4 }, Half))),
            EffectDefinition.IfNotHero(EffectDefinition.OnCaster(EffectDefinition.Modifier(Half))),
        });

    private static readonly EffectDefinition[] Cast =
    {
        EffectDefinition.Summon("skeleton", 40000, false, new[] { 2 }, Array.Empty<int>(),
            EffectDefinition.Modifier(new ModifierSpec("skeleton", -1, dispellable: false, revives: true, consumedOnRevive: true))).StacksOf("bone_half"),
        EffectDefinition.OnCaster(EffectDefinition.Consume("bone_half")),
    };

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("skeleton", new UnitStats(300, Fixed.Zero, 300, 40, 40, 100, 1500, 300, 400, 16, visionRange: 800), new Bounty(5, 5, 5)));
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

    private static int Halves(MatchUnit unit) => unit.Modifiers.Where(m => m.Spec.Key == "bone_half").Sum(m => m.Stacks);

    private static List<MatchUnit> Skeletons(MatchState state) => state.Units.Where(u => u.TypeId == "skeleton" && u.IsAlive).ToList();

    [Fact]
    public void Kills_GiveHalfAChargePerCreep_AndTwoChargesPerHero_UpToTheLimit()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Gathering, H(ctx.State, "alpha").Id));
        At(kernel, 2, ctx =>
        {
            MatchUnit creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), H(ctx.State, "beta").Team, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes.First(), ctx.Tick.Value);
            creep.Position = P(5100, 5000);
            Damage.Execute(ctx, H(ctx.State, "alpha").Id, creep);
        });
        int afterCreep = 0;
        At(kernel, 4, ctx => afterCreep = Halves(H(ctx.State, "alpha")));
        At(kernel, 5, ctx => Damage.Execute(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta")));
        int afterHero = 0;
        At(kernel, 7, ctx => afterHero = Halves(H(ctx.State, "alpha")));
        At(kernel, 8, ctx => Damage.Execute(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "gamma")));

        kernel.RunTicks(11);

        Assert.Equal(1, afterCreep);
        Assert.Equal(5, afterHero);
        Assert.Equal(8, Halves(H(kernel.State, "alpha")));
    }

    [Fact]
    public void TheCast_SpendsTheCharges_OnASkeletonEach_WhichComesBackOnce()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            for (int i = 0; i < 7; i++)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), Half, H(ctx.State, "alpha").Id);   // three charges and a half
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Cast, 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position));
        int summoned = 0;
        At(kernel, 3, ctx => summoned = Skeletons(ctx.State).Count);
        At(kernel, 4, ctx => Damage.Execute(ctx, H(ctx.State, "beta").Id, Skeletons(ctx.State)[0]));
        int afterFirstDeath = 0;
        At(kernel, 6, ctx => afterFirstDeath = Skeletons(ctx.State).Count);
        At(kernel, 7, ctx => Damage.Execute(ctx, H(ctx.State, "beta").Id, Skeletons(ctx.State)[0]));

        kernel.RunTicks(10);

        Assert.Equal(3, summoned);
        Assert.Equal(0, Halves(H(kernel.State, "alpha")));
        Assert.Equal(3, afterFirstDeath);   // it came back
        Assert.Equal(2, Skeletons(kernel.State).Count);   // not twice
    }

    /// <summary>The AI calls the skeletons in a fight with charges enough for two, not with too few for one.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(4, true)]
    public void Ai_CallsTheGuard_WithCharges(int halves, bool calls)
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("bone_guard", "Bone Guard", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
            new[] { 42000 }, new[] { 0 }, new[] { 1 }, Cast)), new[] { "bone_guard" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
            for (int i = 0; i < halves; i++)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), Half, H(ctx.State, "alpha").Id);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 * 30);

        Assert.Equal(calls, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "bone_guard"));
    }
}
