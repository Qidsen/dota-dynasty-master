using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

public class BasicHeroControllerTests
{
    private static readonly (string, string)[] NoHeroes = Array.Empty<(string, string)>();

    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    [Fact]
    public void Perception_SeesEnemiesOnlyNearAlliedUnits()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(4000, 5000);
            H(ctx.State, "beta").Position = P(4900, 5000);    // 900 from alpha: seen
            H(ctx.State, "gamma").Position = P(5600, 6500);   // far from every Dawn unit
        });
        kernel.RunTicks(4);   // the tick-3 vision update sees the new positions

        var perception = TeamPerception.Build(kernel.State, Team.Dawn, new HeroAiSettings());

        Assert.True(perception.CanSee(H(kernel.State, "beta")));
        Assert.False(perception.CanSee(H(kernel.State, "gamma")));
        Assert.True(perception.CanSee(H(kernel.State, "alpha")));   // own units are always known
    }

    [Fact]
    public void Controller_NeverTargetsUnseenEnemiesButReactsOnceTheyAppear()
    {
        var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "bolt");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        HeroAi.Attach(kernel);
        At(kernel, 0, ctx => H(ctx.State, "beta").Position = P(6000, 8500));
        At(kernel, 150, ctx => H(ctx.State, "beta").Position = H(ctx.State, "alpha").Position + FixedVector2.FromInts(400, 0));

        kernel.RunTicks(150);
        Assert.Empty(kernel.EventsOf<AbilityCast>());
        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Target == H(kernel.State, "beta").Id);

        kernel.RunTicks(60);
        AbilityCast cast = Assert.Single(kernel.EventsOf<AbilityCast>());
        Assert.Equal(H(kernel.State, "beta").Id, cast.Target);
    }

    [Fact]
    public void Controller_GoesToItsLaneAndLastHitsCreeps()
    {
        var kernel = new RulesetDraft().StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);
        kernel.RunTicks(5 * 60 * 30);   // ten waves: enough samples that a single missed last hit does not decide the test

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.True(alpha.Hero!.LastHits >= 2, "last hits: " + alpha.Hero.LastHits);
        Assert.Contains(kernel.EventsOf<GoldEarned>(), g => g.Hero == alpha.Id && g.Reason == "last_hit");
        Assert.Equal(0, alpha.Hero.Deaths);
    }

    [Fact]
    public void Controller_RetreatsWhenHurtAndReturnsAfterHealing()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);
        At(kernel, 0, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            alpha.Position = P(3500, 5000);
            alpha.Hp = Fixed.FromInt(100);
        });

        kernel.RunTicks(150);
        Assert.True(H(kernel.State, "alpha").Position.X < Fixed.FromInt(2500), "should walk home");

        kernel.RunTicks(30 * 30);
        Assert.True(H(kernel.State, "alpha").Hp.ToDouble() > 500, "should heal at the fountain");
    }

    [Fact]
    public void Controller_BuysItemsFromTheHeroBuild()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Items.Add(new ItemDefinition("blade", "Blade", 500, new Dynasty.MatchSimulation.Combat.ModifierSpec("blade", -1, damageBonus: 20)));
        draft.Heroes[0] = new HeroDefinition("alpha", "alpha", new[] { "carry" }, HeroStats(), 80, Fixed.FromRatio(3, 10), 3, 300, 40,
            Fixed.FromInt(2), Fixed.One, itemBuild: new[] { "blade" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);

        kernel.RunTicks(10);

        Assert.Equal("blade", Assert.Single(kernel.EventsOf<ItemPurchased>()).ItemId);
    }

    [Fact]
    public void Controller_BuysRecipeComponentsFirstWhenTheFullItemIsUnaffordable()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Items.Add(new ItemDefinition("blade", "Blade", 500, new Dynasty.MatchSimulation.Combat.ModifierSpec("blade", -1, damageBonus: 20)));
        draft.Items.Add(new ItemDefinition("plate", "Plate", 400, new Dynasty.MatchSimulation.Combat.ModifierSpec("plate", -1, armorBonus: Fixed.FromInt(5))));
        draft.Items.Add(new ItemDefinition("crest", "Crest", 1500, new Dynasty.MatchSimulation.Combat.ModifierSpec("crest", -1, damageBonus: 30), new[] { "blade", "plate" }));
        draft.Heroes[0] = new HeroDefinition("alpha", "alpha", new[] { "carry" }, HeroStats(), 80, Fixed.FromRatio(3, 10), 3, 300, 40,
            Fixed.FromInt(2), Fixed.One, itemBuild: new[] { "crest" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);
        At(kernel, 30, ctx => H(ctx.State, "alpha").Hero!.Gold = Fixed.FromInt(5000));

        kernel.RunTicks(60);

        Assert.Equal(new[] { "blade", "plate", "crest" }, kernel.EventsOf<ItemPurchased>().Select(p => p.ItemId));
        Assert.Equal(new[] { "crest" }, H(kernel.State, "alpha").Hero!.Inventory.Select(i => i.Definition.Id));
    }

    [Fact]
    public void ControlledMatch_IsDeterministic()
    {
        SimulationKernel<MatchState> Run()
        {
            var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "bolt").WithHeroAbilities("beta", "quake");
            draft.DawnWave.Add(new WaveEntry("melee", 2));
            draft.DuskWave.Add(new WaveEntry("melee", 2));
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") }, seed: 5);
            HeroAi.Attach(kernel);
            kernel.RunTicks(2 * 60 * 30);
            return kernel;
        }

        Assert.Equal(Run().Events.Checksum, Run().Events.Checksum);
    }
}
