using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

public class BuybackAiTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static RulesetDraft Draft()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.BuybackBaseGold = 200;
        draft.BuybackGoldPerLevel = 50;
        draft.BuybackCooldownMs = 60000;
        draft.StructureTypes[0] = new StructureType("tower", TowerStats(dmg: 1));
        return draft;
    }

    /// <summary>Alpha (level 6, so a 16 s respawn) dies; optionally Dusk's beta is attacking the Dawn tower.</summary>
    private static SimulationKernel<MatchState> DeadHero(bool towerUnderAttack)
    {
        var kernel = Draft().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            HeroProgression.GrantXp(ctx, alpha, 2000);
            alpha.Hero!.Gold = Fixed.FromInt(2000);
            alpha.Position = P(5000, 5000);
            Damage.Deal(ctx, EntityId.None, alpha, Fixed.FromInt(99999), DamageType.Pure);
            H(ctx.State, "beta").Position = towerUnderAttack ? P(3300, 5000) : P(9000, 2000);
            if (towerUnderAttack)
                HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(ctx.State.FindStructure("dawn.mid.t1")!.Id));
        });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("beta");
        HeroAi.Attach(kernel, settings);
        return kernel;
    }

    [Fact]
    public void DeadHero_BuysBackToDefendAThreatenedStructure()
    {
        var kernel = DeadHero(towerUnderAttack: true);

        kernel.RunTicks(90);

        Assert.Single(kernel.EventsOf<HeroBoughtBack>());
    }

    [Fact]
    public void DeadHero_WaitsForItsRespawnWhenNothingIsThreatened()
    {
        var kernel = DeadHero(towerUnderAttack: false);

        kernel.RunTicks(90);

        Assert.Empty(kernel.EventsOf<HeroBoughtBack>());
    }

    private static int ItemsBought(int gold)
    {
        var draft = Draft();
        draft.Items.Add(new ItemDefinition("blade", "Blade", 500, new ModifierSpec("blade", -1, damageBonus: 20)));
        draft.Heroes[0] = new HeroDefinition("alpha", "alpha", new[] { "carry" }, HeroStats(), 80, Fixed.FromRatio(3, 10), 3, 300, 40,
            Fixed.FromInt(2), Fixed.One, itemBuild: new[] { "blade" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx => H(ctx.State, "alpha").Hero!.Gold = Fixed.FromInt(gold));
        HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, BuybackReserveFromMinute = 0 });
        kernel.RunTicks(10);
        return kernel.EventsOf<ItemPurchased>().Count();
    }

    [Fact]
    public void LateGame_KeepsGoldForBuyback()
    {
        Assert.Equal(0, ItemsBought(600));   // 600 − 500 would leave less than the 250 buyback
        Assert.Equal(1, ItemsBought(800));
    }
}
