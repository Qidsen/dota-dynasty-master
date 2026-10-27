using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Dota's lane aggro (Liquipedia «Lane Creeps» and «Buildings», 7.41): a lane creep keeps its target, switching only to a
/// unit of a higher type priority in attack range (heroes and other units alike, then siege creeps, then buildings; siege
/// creeps hit buildings first) or, once its target leaves its attack range, to the best one in range (threat: attacking it,
/// then attacking an ally, then the rest; then the nearest). An attack order on an enemy hero draws the enemy creeps within
/// 500 of the attacker for 2.3 s (cooldown 3 s); a started attack on a hero draws them without the chase. Creeps spawned
/// before 5:00 cannot be drawn until they come within 1500 of their tier 1 tower. A tower turns on a hero within 500 of it
/// that attacks a hero, and otherwise keeps its target.
/// </summary>
public class DotaAggroTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Lane(int lockedUntilMs = 0)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.DotaAggro = new DotaAggroRules(drawRadius: 500, forcedMs: 2300, cooldownMs: 3000, lockedUntilMs: lockedUntilMs, unlockRadius: 1500,
            unlockStructureTypeId: "tower", towerProtectRadius: 500, towerThreatRadius: 700);
        draft.Creeps.Add(new CreepType("siege", new UnitStats(900, Fixed.Zero, 0, 30, 30, 690, 3000, 700, 800, 24, attackClass: AttackClass.Reinforced)));
        draft.Creeps.Add(new CreepType("post", MeleeStats(hp: 100000, speed: 0, dmgMin: 1, dmgMax: 1)));   // a sturdy creep that stays put
        return draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
    }

    private static MatchUnit Creep(SimulationContext<MatchState> ctx, Team team, int x, int y, string type = "post")
    {
        MatchUnit creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), team, ctx.State.Ruleset.FindCreepType(type)!, ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
        creep.Position = P(x, y);
        creep.StartPosition = creep.Position;
        return creep;
    }

    private static void Heroes(SimulationContext<MatchState> ctx, int ax, int ay, int bx, int by)
    {
        H(ctx.State, "alpha").Position = P(ax, ay);
        H(ctx.State, "beta").Position = P(bx, by);
    }

    [Fact]
    public void ACreep_KeepsItsTarget_WhenAnotherComesCloser()
    {
        var kernel = Lane();
        MatchUnit? creep = null, first = null, second = null;
        At(kernel, 0, ctx =>
        {
            Heroes(ctx, 1000, 9000, 9000, 9000);
            creep = Creep(ctx, Team.Dusk, 5000, 5000);
            first = Creep(ctx, Team.Dawn, 5090, 5000);
        });
        At(kernel, 20, ctx => second = Creep(ctx, Team.Dawn, 5040, 5000));
        kernel.RunTicks(40);

        Assert.Equal(first!.Id, creep!.TargetId);
    }

    [Fact]
    public void ACreep_SwitchesFromASiegeCreepToAHero_AndASiegeCreepHitsTheBuildingFirst()
    {
        var kernel = Lane();
        MatchUnit? creep = null, siege = null, dawnSiege = null;
        At(kernel, 0, ctx =>
        {
            Heroes(ctx, 1000, 9000, 9000, 9000);
            creep = Creep(ctx, Team.Dusk, 5000, 5000);
            siege = Creep(ctx, Team.Dawn, 5090, 5000, "siege");
            dawnSiege = Creep(ctx, Team.Dawn, 6400, 5000, "siege");   // 600 from the Dusk tower
            Creep(ctx, Team.Dusk, 6300, 5000);                        // a nearer creep for it
        });
        At(kernel, 20, ctx => H(ctx.State, "alpha").Position = P(5000, 5090));
        kernel.RunTicks(40);

        Assert.Equal(H(kernel.State, "alpha").Id, creep!.TargetId);
        Assert.Equal(kernel.State.FindStructure("dusk.mid.t1")!.Id, dawnSiege!.TargetId);
    }

    [Fact]
    public void AnAttackOrderOnAnEnemyHero_DrawsTheEnemyCreepsNearTheAttacker_OnceEveryThreeSeconds()
    {
        var kernel = Lane();
        MatchUnit? near = null, far = null, later = null;
        At(kernel, 0, ctx =>
        {
            Heroes(ctx, 1000, 9000, 5000, 7000);
            near = Creep(ctx, Team.Dawn, 5300, 7000);    // 300 from beta
            Creep(ctx, Team.Dusk, 5390, 7000);            // its target
            far = Creep(ctx, Team.Dawn, 5800, 7000);     // 800 from beta
            Creep(ctx, Team.Dusk, 5890, 7000);
        });
        At(kernel, 30, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id)));   // alpha far away: no matter
        At(kernel, 40, ctx =>
        {
            H(ctx.State, "beta").Position = P(5000, 6000);   // 1000 away: drawn creeps still chase
            later = Creep(ctx, Team.Dawn, 5000, 6300);
            Creep(ctx, Team.Dusk, 5000, 6390);
        });
        At(kernel, 60, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id)));   // on cooldown
        bool laterDrawnEarly = false, nearChasing = false;
        At(kernel, 90, ctx =>   // within the 2.3 s
        {
            laterDrawnEarly = later!.TargetId == H(ctx.State, "beta").Id;
            nearChasing = near!.TargetId == H(ctx.State, "beta").Id;
        });
        At(kernel, 130, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id)));  // cooldown over
        kernel.RunTicks(135);

        Assert.True(nearChasing, "the drawn creep chases beta");
        Assert.NotEqual(H(kernel.State, "beta").Id, far!.TargetId);
        Assert.False(laterDrawnEarly, "an order on cooldown draws nothing");
        Assert.Equal(H(kernel.State, "beta").Id, later!.TargetId);
    }

    [Fact]
    public void AStartedAttackOnAHero_DrawsTheEnemyCreepsNearTheAttacker()
    {
        var kernel = Lane();
        MatchUnit? creep = null;
        At(kernel, 0, ctx => Heroes(ctx, 5000, 5000, 5100, 5000));
        At(kernel, 12, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id)));   // no creeps near yet
        At(kernel, 110, ctx =>   // the order's cooldown is over; beta keeps attacking
        {
            creep = Creep(ctx, Team.Dawn, 5100, 5400);   // stays put; nothing in its attack range
            Creep(ctx, Team.Dusk, 5100, 5700).TargetId = creep.Id;   // its target, 300 away and after it (a higher threat than beta)
            H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp);
        });
        kernel.RunTicks(110 + 70);   // the next attack beta starts

        Assert.Equal(H(kernel.State, "beta").Id, creep!.TargetId);
    }

    [Fact]
    public void CreepsSpawnedBeforeFiveMinutes_CannotBeDrawn_UntilTheyComeNearTheirTier1Tower()
    {
        var kernel = Lane(lockedUntilMs: 300000);
        MatchUnit? away = null, home = null;
        At(kernel, 0, ctx =>
        {
            Heroes(ctx, 1000, 9000, 6200, 7000);
            away = Creep(ctx, Team.Dawn, 6000, 7000);    // 3600 from the Dawn tower
            Creep(ctx, Team.Dusk, 6000, 7090);
            home = Creep(ctx, Team.Dawn, 3500, 6000);    // 1100 from it
            Creep(ctx, Team.Dusk, 3500, 6090);
        });
        At(kernel, 30, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id)));
        bool awayDrawn = true;
        At(kernel, 40, ctx =>
        {
            awayDrawn = away!.TargetId == H(ctx.State, "beta").Id;
            H(ctx.State, "beta").Position = P(3700, 6000);
        });
        At(kernel, 130, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id)));
        kernel.RunTicks(135);

        Assert.False(awayDrawn);
        Assert.Equal(H(kernel.State, "beta").Id, home!.TargetId);
    }

    [Theory]
    [InlineData(6600, true)]    // 400 from the tower
    [InlineData(6350, false)]   // 650: in its range, not within 500
    public void ATower_TurnsOnAHeroAttackingAHeroNearIt_AndOtherwiseKeepsItsTarget(int alphaX, bool expected)
    {
        var kernel = Lane();
        MatchUnit? creep = null;
        At(kernel, 0, ctx =>
        {
            Heroes(ctx, alphaX, 5000, alphaX - 100, 5000);
            creep = Creep(ctx, Team.Dawn, 6800, 5100);   // nearer the tower than alpha: its first target
        });
        At(kernel, 30, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id)));
        kernel.RunTicks(75);

        MatchUnit tower = kernel.State.FindStructure("dusk.mid.t1")!;
        Assert.Equal(expected ? H(kernel.State, "alpha").Id : creep!.Id, tower.TargetId);
    }
}
