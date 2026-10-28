using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

/// <summary>Healing consumables (backlog B-024): bought for the laning phase and drunk instead of walking home.</summary>
public class ConsumableAiTests
{
    private static readonly (string, string)[] NoHeroes = Array.Empty<(string, string)>();

    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    /// <summary>A test ruleset with a healing salve: 300 HP over ten seconds, one charge.</summary>
    private static RulesetDraft WithSalve()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("item.salve", "Salve", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Modifier(new ModifierSpec("item.salve", 10000, healPerSecond: Fixed.FromInt(30))) }));
        draft.Items.Add(new ItemDefinition("salve", "Salve", 100, activeAbilityId: "item.salve", charges: 1));
        return draft;
    }

    [Fact]
    public void Controller_BuysAHealingConsumableForTheLaningPhase()
    {
        var kernel = WithSalve().StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);

        kernel.RunTicks(10);

        Assert.Contains(kernel.EventsOf<ItemPurchased>(), p => p.ItemId == "salve");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HurtHero_DrinksTheConsumableAndStaysInLane_InsteadOfWalkingHome(bool hasSalve)
    {
        RulesetDraft draft = hasSalve ? WithSalve() : new RulesetDraft().WithoutWaves();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);
        kernel.AddSystem(new LambdaSystem(10, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            alpha.Position = P(3500, 5000);
            alpha.Hp = Fixed.FromInt(alpha.MaxHp * 25 / 100);   // below the usual 30% retreat threshold
        }));

        kernel.RunTicks(10 + 15 * 30);

        MatchUnit hero = H(kernel.State, "alpha");
        if (hasSalve)
        {
            Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "item.salve");
            Assert.True(hero.Position.X > Fixed.FromInt(2500), $"stays near the lane, x = {hero.Position.X.ToDouble():0}");
        }
        else
        {
            Assert.True(hero.Position.X < Fixed.FromInt(2500), $"walks home, x = {hero.Position.X.ToDouble():0}");
        }
    }

    /// <summary>
    /// An enemy hero in sight but out of reach (an offlaner across the creep wave) does not keep a hurt hero from drinking: it steps
    /// back and drinks instead of walking home; one close enough to hit it does.
    /// </summary>
    [Theory]
    [InlineData(1000, true)]
    [InlineData(400, false)]
    public void HurtHero_DrinksWithAnEnemyInSight_OnlyWhenTheEnemyIsOutOfReach(int enemyDistance, bool drinks)
    {
        var kernel = WithSalve().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("beta");
        HeroAi.Attach(kernel, settings);
        kernel.AddSystem(new LambdaSystem(10, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            alpha.Position = P(3500, 5000);
            alpha.Hp = Fixed.FromInt(alpha.MaxHp * 25 / 100);
            H(ctx.State, "beta").Position = P(3500 + enemyDistance, 5000);
        }));

        kernel.RunTicks(10 + 5);

        Assert.Equal(drinks, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "item.salve"));
    }

    [Theory]
    [InlineData(5000, true)]
    [InlineData(3000, false)]
    public void HealthyHeroAtBase_TeleportsToAFarLaneTower_AndWalksToANearOne(int towerX, bool teleports)
    {
        RulesetDraft draft = Dynasty.MatchSimulation.Tests.Heroes.TeleportTests.WithTeleport();
        StructurePlacement t1 = draft.Placements[0];
        draft.Placements[0] = new StructurePlacement(t1.Id, t1.Team, t1.StructureTypeId, P(towerX, 5000), false, Array.Empty<string>(), t1.Vulnerability);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);

        kernel.RunTicks(150);   // the scroll channels for three seconds

        Assert.Equal(teleports, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "teleport"));
    }
}
