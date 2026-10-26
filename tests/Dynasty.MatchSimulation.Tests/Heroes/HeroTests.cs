using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Heroes;

public class HeroTests
{
    private static readonly (string, string)[] None = Array.Empty<(string, string)>();

    private static MatchUnit HeroOf(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Once Dawn sees a Dusk creep, the given heroes are ordered to attack it (orders on unseen units are rejected).</summary>
    private static void AttackFirstVisibleDuskCreep(SimulationKernel<MatchState> kernel, params string[] heroes) =>
        kernel.AddSystem(new TriggerSystem(
            ctx => ctx.State.Creeps(Team.Dusk).Any(c => ctx.State.IsVisibleTo(Team.Dawn, c)),
            ctx =>
            {
                EntityId creep = ctx.State.Creeps(Team.Dusk).First(c => ctx.State.IsVisibleTo(Team.Dawn, c)).Id;
                foreach (string hero in heroes)
                    HeroOrders.Issue(HeroOf(ctx.State, hero), HeroOrder.Attack(creep));
            }));

    [Fact]
    public void Lineup_SpawnsHeroesAtTheirFountainWithLevelOneStats()
    {
        var kernel = new RulesetDraft().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });

        MatchUnit alpha = HeroOf(kernel.State, "alpha");
        MatchUnit beta = HeroOf(kernel.State, "beta");
        Assert.Equal(UnitKind.Hero, alpha.Kind);
        Assert.Equal(P(200, 5000), alpha.Position);
        Assert.Equal(P(9800, 5000), beta.Position);
        Assert.Equal(Team.Dusk, beta.Team);
        Assert.Equal(1, alpha.Hero!.Level);
        Assert.Equal(600, alpha.MaxHp);
        Assert.Equal(Fixed.FromInt(600), alpha.Hp);
        Assert.Equal(Fixed.FromInt(600), alpha.Hero.Gold);
        Assert.Equal(Fixed.FromInt(300), alpha.Hero.Mana);
        Assert.Equal("mid", alpha.Hero.AssignedLane);
    }

    [Theory]
    [InlineData("nobody", "mid")]
    [InlineData("alpha", "river")]
    public void Lineup_RejectsUnknownHeroOrLane(string hero, string lane)
    {
        Assert.Throws<InvalidOperationException>(() => new RulesetDraft().StartWithHeroes(new[] { (hero, lane) }, None));
    }

    [Fact]
    public void Lineup_RejectsTheSameHeroTwiceOnOneTeam()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new RulesetDraft().StartWithHeroes(new[] { ("alpha", "mid"), ("alpha", "mid") }, None));
    }

    [Fact]
    public void MoveOrder_WalksAroundObstaclesToTheDestination()
    {
        var draft = new RulesetDraft().WithoutWaves();
        var wall = ObstacleDefinition.Rectangle("wall", P(1000, 5500), P(1200, 9000));
        draft.Obstacles.Add(wall);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, None);
        At(kernel, 0, ctx => HeroOrders.Issue(HeroOf(ctx.State, "alpha"), HeroOrder.Move(P(2000, 7000))));

        for (int i = 0; i < 600; i++)
        {
            kernel.Step();
            Assert.False(wall.Contains(HeroOf(kernel.State, "alpha").Position, 0), "hero walked into the wall");
        }

        Assert.Equal(P(2000, 7000), HeroOf(kernel.State, "alpha").Position);
    }

    [Fact]
    public void AttackOrder_ChasesKillsAndEarnsLastHitGold()
    {
        var draft = new RulesetDraft();
        draft.DawnWave.Clear();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, None);
        At(kernel, 0, ctx => HeroOrders.Issue(HeroOf(ctx.State, "alpha"), HeroOrder.Move(P(4500, 5000))));
        AttackFirstVisibleDuskCreep(kernel, "alpha");

        kernel.StepUntil(() => kernel.EventsOf<UnitKilled>().Any(), 3000);

        MatchUnit alpha = HeroOf(kernel.State, "alpha");
        Assert.Equal(alpha.Id, kernel.EventsOf<UnitKilled>().Single().Killer);
        Assert.Equal(1, alpha.Hero!.LastHits);
        GoldEarned bounty = Assert.Single(kernel.EventsOf<GoldEarned>(), g => g.Reason == "last_hit");
        Assert.Equal(40, bounty.Amount);
    }

    [Fact]
    public void CreepXp_IsSharedByEnemyHeroesInRangeOnly()
    {
        var draft = new RulesetDraft();
        draft.DawnWave.Clear();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid"), ("gamma", "mid") }, None);
        At(kernel, 0, ctx =>
        {
            HeroOrders.Issue(HeroOf(ctx.State, "alpha"), HeroOrder.Move(P(4500, 5000)));
            HeroOrders.Issue(HeroOf(ctx.State, "beta"), HeroOrder.Move(P(4500, 5100)));
        });
        AttackFirstVisibleDuskCreep(kernel, "alpha", "beta");

        kernel.StepUntil(() => kernel.EventsOf<UnitKilled>().Any(), 3000);

        Assert.Equal(30, HeroOf(kernel.State, "alpha").Hero!.Xp);
        Assert.Equal(30, HeroOf(kernel.State, "beta").Hero!.Xp);
        Assert.Equal(0, HeroOf(kernel.State, "gamma").Hero!.Xp);
    }

    [Fact]
    public void Xp_LevelsUpThroughTheTableAndGrowsStats()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, None);
        At(kernel, 0, ctx => HeroProgression.GrantXp(ctx, HeroOf(ctx.State, "alpha"), 1000));

        kernel.Step();

        MatchUnit alpha = HeroOf(kernel.State, "alpha");
        Assert.Equal(4, alpha.Hero!.Level);
        Assert.Equal(600 + 3 * 80, alpha.MaxHp);
        Assert.Equal(Fixed.FromInt(840), alpha.Hp);
        Assert.Equal(50 + 3 * 3, alpha.AttackDamageMin);
        Assert.Equal(new[] { 2, 3, 4 }, kernel.EventsOf<HeroLeveledUp>().Select(e => e.Level));
    }

    [Fact]
    public void Xp_StopsAtMaxLevel()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, None);
        At(kernel, 0, ctx => HeroProgression.GrantXp(ctx, HeroOf(ctx.State, "alpha"), 1_000_000));

        kernel.Step();

        Assert.Equal(6, HeroOf(kernel.State, "alpha").Hero!.Level);
    }

    /// <summary>
    /// Dota's hero kill XP: a base plus a share of the victim's own XP, split among the killing team's heroes
    /// near the victim — and the killer always gets a share, even from beyond the share radius.
    /// </summary>
    [Fact]
    public void HeroKillXp_IsBasePlusPartOfTheVictimsXp_AndTheKillerAlwaysShares()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.HeroKillXpPerLevel = 0;
        draft.HeroKillXpPercentOfVictim = 13;
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            HeroOf(ctx.State, "alpha").Position = P(5000, 5000);
            HeroOf(ctx.State, "gamma").Position = P(5200, 5000);
            HeroOf(ctx.State, "beta").Position = P(8000, 5000);   // the killer, outside the share radius
            HeroProgression.GrantXp(ctx, HeroOf(ctx.State, "alpha"), 1000);
        });
        At(kernel, 1, ctx => Damage.Deal(ctx, HeroOf(ctx.State, "beta").Id, HeroOf(ctx.State, "alpha"), Fixed.FromInt(5000), DamageType.Pure));

        kernel.RunTicks(3);

        Assert.Single(kernel.EventsOf<HeroKilled>());
        Assert.Equal(115, HeroOf(kernel.State, "beta").Hero!.Xp);    // (100 + 13% of 1000) / 2
        Assert.Equal(115, HeroOf(kernel.State, "gamma").Hero!.Xp);
    }

    /// <summary>Heroes attack faster with every level (Dota's agility), on top of item and ability bonuses.</summary>
    [Fact]
    public void Hero_GainsAttackSpeedWithLevels()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes[0] = new HeroDefinition("alpha", "alpha", new[] { "carry" }, HeroStats(), hpPerLevel: 80, armorPerLevel: Fixed.FromRatio(3, 10),
            damagePerLevel: 3, baseMana: 300, manaPerLevel: 40, hpRegenPerSecond: Fixed.FromInt(2), manaRegenPerSecond: Fixed.One, attackSpeedPerLevel: 3);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, None);
        At(kernel, 0, ctx => HeroProgression.GrantXp(ctx, HeroOf(ctx.State, "alpha"), 1000));   // level 4

        kernel.Step();

        MatchUnit alpha = HeroOf(kernel.State, "alpha");
        Assert.Equal(9, alpha.AttackSpeedBonus);
        Assert.Equal((1500 * 100 / 109 * 30 + 500) / 1000, alpha.AttackIntervalTicks);   // 1376 ms at 30 ticks per second
    }

    [Fact]
    public void PassiveGold_AccruesPerMinute()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, None);

        kernel.RunTicks(30 * 60);

        Assert.InRange(HeroOf(kernel.State, "alpha").Hero!.Gold.ToDouble(), 689.9, 690.1);
    }

    [Fact]
    public void HeroDeath_KeepsTheHeroRewardsTheKillerAndRespawnsAtTheFountain()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx => Damage.Deal(ctx, HeroOf(ctx.State, "beta").Id, HeroOf(ctx.State, "alpha"), Fixed.FromInt(5000), DamageType.Pure));

        kernel.RunTicks(2);   // test lambdas run after cleanup, so the death is processed on tick 1

        MatchUnit alpha = HeroOf(kernel.State, "alpha");
        MatchUnit beta = HeroOf(kernel.State, "beta");
        Assert.False(alpha.IsAlive);
        Assert.NotNull(kernel.State.Find(alpha.Id));
        HeroKilled kill = Assert.Single(kernel.EventsOf<HeroKilled>());
        Assert.Equal(beta.Id, kill.Killer);
        Assert.Equal(1, alpha.Hero!.Deaths);
        Assert.Equal(1, beta.Hero!.Kills);
        Assert.Equal(210, kernel.EventsOf<GoldEarned>().Single(g => g.Reason == "hero_kill").Amount);
        Assert.InRange(alpha.Hero.Gold.ToDouble(), 569.9, 570.2);

        long respawnAt = kernel.TimedEventsOf<HeroKilled>().Single().Tick + 180;   // 4 s + 2 s per level
        kernel.StepUntil(() => alpha.IsAlive, 400);
        Assert.Equal(respawnAt, kernel.TimedEventsOf<HeroRespawned>().Single().Tick);
        Assert.Equal(P(200, 5000), alpha.Position);
        Assert.Equal(Fixed.FromInt(alpha.MaxHp), alpha.Hp);
    }

    [Fact]
    public void DeathFromDamageOverTime_IsNotUndoneByRegeneration()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        var poison = new ModifierSpec("poison", 5000, damagePerSecond: Fixed.FromInt(10_000), damageType: DamageType.Pure);
        At(kernel, 0, ctx => Modifiers.Apply(ctx, HeroOf(ctx.State, "alpha"), poison, HeroOf(ctx.State, "beta").Id));

        kernel.RunTicks(40);

        Assert.Single(kernel.EventsOf<HeroKilled>());
        Assert.False(HeroOf(kernel.State, "alpha").IsAlive);
    }

    [Fact]
    public void RecentDamage_GrantsAnAssist()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx => Damage.Deal(ctx, HeroOf(ctx.State, "gamma").Id, HeroOf(ctx.State, "alpha"), Fixed.FromInt(10), DamageType.Pure));
        At(kernel, 5, ctx => Damage.Deal(ctx, HeroOf(ctx.State, "beta").Id, HeroOf(ctx.State, "alpha"), Fixed.FromInt(5000), DamageType.Pure));

        kernel.RunTicks(7);

        MatchUnit gamma = HeroOf(kernel.State, "gamma");
        Assert.Contains(gamma.Id, kernel.EventsOf<HeroKilled>().Single().Assists);
        Assert.Equal(1, gamma.Hero!.Assists);
    }

    [Fact]
    public void Regeneration_RestoresHpOverTime()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Fountains[0] = new FountainPlacement(Team.Dawn, P(200, 5000), 400);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, None);
        At(kernel, 0, ctx => Damage.Deal(ctx, EntityId.None, HeroOf(ctx.State, "alpha"), Fixed.FromInt(300), DamageType.Pure));

        kernel.RunTicks(31);

        // At the fountain: 2 HP/s base regen + 5 % of 600 per second.
        Assert.InRange(HeroOf(kernel.State, "alpha").Hp.ToDouble(), 331.5, 332.5);
    }

    [Fact]
    public void DestroyingAStructure_GivesGoldToEveryHeroOfTheTeam()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, None);
        At(kernel, 0, ctx => Damage.Deal(ctx, HeroOf(ctx.State, "alpha").Id, ctx.State.FindStructure("dusk.mid.t1")!, Fixed.FromInt(99999), DamageType.Pure));

        kernel.RunTicks(2);

        var structureGold = kernel.EventsOf<GoldEarned>().Where(g => g.Reason == "structure").ToList();
        Assert.Equal(2, structureGold.Count);
        Assert.All(structureGold, g => Assert.Equal(100, g.Amount));
    }

    [Fact]
    public void AlliedCreepBelowHalfHp_CanBeDeniedForNoGold()
    {
        var draft = new RulesetDraft();
        draft.DuskWave.Clear();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, None);
        At(kernel, 1, ctx =>
        {
            MatchUnit creep = ctx.State.Creeps(Team.Dawn).First();
            Modifiers.Apply(ctx, creep, ModifierSpec.Status("root", -1, ModifierFlags.Rooted), EntityId.None);
            Damage.Deal(ctx, EntityId.None, creep, Fixed.FromInt(330), DamageType.Pure);
            HeroOrders.Issue(HeroOf(ctx.State, "alpha"), HeroOrder.Attack(creep.Id));
        });

        kernel.StepUntil(() => kernel.EventsOf<UnitKilled>().Any(), 3000);

        MatchUnit alpha = HeroOf(kernel.State, "alpha");
        Assert.Equal(1, alpha.Hero!.Denies);
        Assert.Equal(0, alpha.Hero.LastHits);
        Assert.DoesNotContain(kernel.EventsOf<GoldEarned>(), g => g.Reason == "last_hit");
    }

    [Fact]
    public void AlliedCreepAboveHalfHp_CannotBeAttacked()
    {
        var draft = new RulesetDraft();
        draft.DuskWave.Clear();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, None);
        At(kernel, 1, ctx =>
        {
            // Rooted so the hero can actually reach it; only the deny rule may prevent the attack.
            MatchUnit creep = ctx.State.Creeps(Team.Dawn).First();
            Modifiers.Apply(ctx, creep, ModifierSpec.Status("root", -1, ModifierFlags.Rooted), EntityId.None);
            HeroOrders.Issue(HeroOf(ctx.State, "alpha"), HeroOrder.Attack(creep.Id));
        });

        kernel.RunTicks(600);

        Assert.Empty(kernel.EventsOf<AttackStarted>());
    }
}
