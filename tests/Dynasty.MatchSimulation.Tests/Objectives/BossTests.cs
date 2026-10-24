using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

public class BossTests
{
    private static readonly ModifierSpec Blessing = new("colossus.blessing", 60000, damageBonus: 40, armorBonus: Fixed.FromInt(5));

    private static RulesetDraft WithBoss(int firstSpawnMs = 0)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("colossus", new UnitStats(2000, Fixed.FromInt(5), 270, 60, 60, 150, 1000, 300, 400, 60, visionRange: 600)));
        draft.Boss = new BossDefinition("colossus", "colossus", P(5000, 2500), leashRadius: 600, firstSpawnMs: firstSpawnMs,
            respawnMinMs: 10000, respawnMaxMs: 20000, teamGold: 150, reward: Blessing);
        return draft;
    }

    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static MatchUnit? Boss(MatchState state) => state.Units.SingleOrDefault(u => u.TypeId == "colossus");

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    [Fact]
    public void Boss_SpawnsAtItsFirstSpawnTime()
    {
        var kernel = WithBoss(firstSpawnMs: 1000).StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());

        kernel.RunTicks(30);
        Assert.Null(Boss(kernel.State));

        kernel.Step();
        MatchUnit boss = Boss(kernel.State)!;
        Assert.Equal(Team.Neutral, boss.Team);
        Assert.Equal(P(5000, 2500), boss.Position);
        Assert.Single(kernel.EventsOf<BossSpawned>());
    }

    [Fact]
    public void Boss_IgnoresPassingCreeps()
    {
        var draft = WithBoss();
        draft.Lanes[0] = new LaneDefinition("mid", new[] { P(1000, 2500), P(9000, 2500) });   // the lane runs through the pit
        draft.DawnWave.Add(new WaveEntry("melee", 1));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());

        kernel.RunTicks(30 * 20);

        MatchUnit boss = Boss(kernel.State)!;
        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Attacker == boss.Id || a.Target == boss.Id);
    }

    [Fact]
    public void Boss_FightsBackAgainstItsAttacker()
    {
        var kernel = WithBoss().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 1, ctx => H(ctx.State, "alpha").Position = P(5000, 2300));
        At(kernel, 4, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(Boss(ctx.State)!.Id)));

        kernel.RunTicks(90);

        MatchUnit boss = Boss(kernel.State)!;
        Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == boss.Id && a.Target == H(kernel.State, "alpha").Id);
    }

    [Fact]
    public void Boss_ReturnsToItsPitWhenTheTargetLeavesTheLeash()
    {
        var kernel = WithBoss().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 1, ctx => H(ctx.State, "alpha").Position = P(5000, 2300));
        At(kernel, 4, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(Boss(ctx.State)!.Id)));
        At(kernel, 60, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Move(P(5000, 600))));

        kernel.RunTicks(300);

        MatchUnit boss = Boss(kernel.State)!;
        Assert.True(FixedVector2.Distance(boss.Position, P(5000, 2500)).ToDouble() < 50, "boss should walk back to its pit");
    }

    [Fact]
    public void KillingTheBoss_RewardsTheWholeTeamAndSchedulesARespawn()
    {
        var kernel = WithBoss().StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid") });
        At(kernel, 1, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, Boss(ctx.State)!, Fixed.FromInt(99999), DamageType.Pure));

        kernel.RunTicks(3);

        BossKilled kill = Assert.Single(kernel.EventsOf<BossKilled>());
        Assert.Equal(Team.Dawn, kill.Team);
        Assert.Contains(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "colossus.blessing");
        Assert.Contains(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "colossus.blessing");
        Assert.DoesNotContain(H(kernel.State, "gamma").Modifiers, m => m.Spec.Key == "colossus.blessing");
        Assert.Equal(2, kernel.EventsOf<GoldEarned>().Count(g => g.Reason == "boss"));
        Assert.InRange(kill.RespawnTick - kernel.TimedEventsOf<BossKilled>().Single().Tick, 300, 600);

        kernel.RunTicks(600);
        Assert.Equal(2, kernel.EventsOf<BossSpawned>().Count());
    }

    [Fact]
    public void TeamAi_TakesTheBossWhenTheEnemyIsDown()
    {
        var draft = WithBoss();
        draft.Heroes.Add(Hero("delta"));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid"), ("gamma", "mid") }, new[] { ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            foreach (string hero in new[] { "alpha", "beta", "gamma" })
                HeroProgression.GrantXp(ctx, H(ctx.State, hero), 2000);
            Damage.Deal(ctx, EntityId.None, H(ctx.State, "delta"), Fixed.FromInt(99999), DamageType.Pure);
        });
        HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 });

        kernel.RunTicks(30 * 60);   // ~18 s walk to the pit plus the fight

        Assert.Equal(Team.Dawn, Assert.Single(kernel.EventsOf<BossKilled>()).Team);
    }

    [Fact]
    public void TeamAi_TakesTheBossAtEvenNumbersLateWhenTheEnemyIsNotAround()
    {
        var draft = WithBoss();
        foreach (string id in new[] { "delta", "epsilon", "zeta", "eta", "theta" })
            draft.Heroes.Add(Hero(id));
        var kernel = draft.StartWithHeroes(
            new[] { ("alpha", "mid"), ("beta", "mid"), ("gamma", "mid"), ("delta", "mid") },
            new[] { ("epsilon", "mid"), ("zeta", "mid"), ("eta", "mid"), ("theta", "mid") });
        At(kernel, 0, ctx =>
        {
            foreach (string hero in new[] { "alpha", "beta", "gamma", "delta" })
                HeroProgression.GrantXp(ctx, H(ctx.State, hero), 2000);
            int i = 0;
            foreach (string hero in new[] { "epsilon", "zeta", "eta", "theta" })
                H(ctx.State, hero).Position = P(9000, 8000 + 200 * i++);   // all alive, far from the pit and unseen
        });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, BossAtEvenNumbersFromMinute = 0 };
        foreach (string hero in new[] { "epsilon", "zeta", "eta", "theta" })
            settings.Uncontrolled.Add(hero);
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 60);

        Assert.Equal(Team.Dawn, Assert.Single(kernel.EventsOf<BossKilled>()).Team);
    }

    [Fact]
    public void Validation_RejectsBossWithUnknownCreepType()
    {
        var draft = WithBoss();
        draft.Boss = new BossDefinition("x", "unknown_beast", P(5000, 2500), 600, 0, 10000, 20000, 150, Blessing);

        Assert.Contains(draft.Build().Validate(), e => e.Contains("unknown_beast"));
    }
}
