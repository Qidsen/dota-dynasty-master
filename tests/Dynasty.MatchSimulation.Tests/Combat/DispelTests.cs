using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

public class DispelTests
{
    private static readonly ModifierSpec Slow = new("slow", 10000, moveSpeedPercent: -30);
    private static readonly ModifierSpec Hush = ModifierSpec.Status("hush", 10000, ModifierFlags.Silenced);
    private static readonly ModifierSpec Poison = new("poison", 10000, damagePerSecond: Fixed.FromInt(5));
    private static readonly ModifierSpec Stun = ModifierSpec.Status("stun", 10000, ModifierFlags.Stunned);
    private static readonly ModifierSpec Shield = new("shield", 10000, armorBonus: Fixed.FromInt(10));
    private static readonly ModifierSpec Haste = new("haste", 10000, moveSpeedPercent: 30);
    private static readonly ModifierSpec Curse = new("curse", 10000, armorBonus: Fixed.FromInt(-5), dispellable: false);

    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static RulesetDraft Draft()
    {
        var draft = TestAbilities.Draft();
        draft.Abilities.Add(new AbilityDefinition("cleanse", "Cleanse", AbilityTargeting.Unit, TargetTeam.Any, 600, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Dispel(strong: false) }));
        draft.Abilities.Add(new AbilityDefinition("purge", "Purge", AbilityTargeting.Unit, TargetTeam.Any, 600, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Dispel(strong: true) }));
        draft.WithHeroAbilities("alpha", "cleanse", "purge");
        return draft;
    }

    /// <summary>Alpha (caster) at (5000, 3000); ally gamma and enemy beta next to it, each with the given modifiers.</summary>
    private static SimulationKernel<MatchState> Cast(string abilityId, string targetHero, params ModifierSpec[] onTarget)
    {
        var kernel = Draft().StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 200);   // level 2: both dispels learned
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "gamma").Position = P(5100, 3000);
            H(ctx.State, "beta").Position = P(5300, 3000);
            foreach (ModifierSpec spec in onTarget)
                Modifiers.Apply(ctx, H(ctx.State, targetHero), spec, EntityId.None);
        }));
        kernel.AddSystem(new LambdaSystem(3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast(abilityId, H(ctx.State, targetHero).Id))));
        kernel.RunTicks(6);
        return kernel;
    }

    private static string[] Keys(SimulationKernel<MatchState> kernel, string hero) =>
        H(kernel.State, hero).Modifiers.Select(m => m.Spec.Key).OrderBy(k => k).ToArray();

    [Fact]
    public void BasicDispelOnAnAlly_RemovesDebuffsButNotStunsOrBuffs()
    {
        var kernel = Cast("cleanse", "gamma", Slow, Hush, Poison, Stun, Shield);

        Assert.Equal(new[] { "shield", "stun" }, Keys(kernel, "gamma"));
        Assert.Equal(3, kernel.EventsOf<ModifierRemoved>().Count(r => r.Reason == "dispelled"));
    }

    [Fact]
    public void StrongDispelOnAnAlly_AlsoRemovesStuns()
    {
        var kernel = Cast("purge", "gamma", Slow, Stun, Shield);

        Assert.Equal(new[] { "shield" }, Keys(kernel, "gamma"));
    }

    [Fact]
    public void DispelOnAnEnemy_RemovesBuffsButNotDebuffs()
    {
        var kernel = Cast("cleanse", "beta", Shield, Haste, Slow);

        Assert.Equal(new[] { "slow" }, Keys(kernel, "beta"));
    }

    [Fact]
    public void UndispellableAndPermanentModifiers_Stay()
    {
        var draft = Draft();
        var kernel = Cast("purge", "gamma", Curse, new ModifierSpec("passive.like", -1, moveSpeedPercent: -10));

        Assert.Equal(new[] { "curse", "passive.like" }, Keys(kernel, "gamma"));
    }

    [Fact]
    public void Ai_CleansesASilencedAlly()
    {
        var kernel = Draft().StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 200);
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "gamma").Position = P(5100, 3000);
            H(ctx.State, "beta").Position = P(9000, 9000);
            Modifiers.Apply(ctx, H(ctx.State, "gamma"), Hush, H(ctx.State, "beta").Id);
        }));
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("gamma");
        settings.Uncontrolled.Add("beta");
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30);

        // Whichever dispel is learned first (here the strong one, learned on tick 0) may be used.
        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => (c.AbilityId == "cleanse" || c.AbilityId == "purge") && c.Target == H(kernel.State, "gamma").Id);
        Assert.DoesNotContain(H(kernel.State, "gamma").Modifiers, m => m.Spec.Key == "hush");
    }
}
