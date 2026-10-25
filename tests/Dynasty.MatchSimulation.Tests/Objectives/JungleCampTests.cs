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

public class JungleCampTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static List<MatchUnit> CampCreeps(MatchState state) =>
        state.Units.Where(u => u.Team == Team.Neutral && u.Kind == UnitKind.Creep).ToList();

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>One camp of two wolves at (3000, 2500); first spawn at 0 s, respawn 10 s after clearing.</summary>
    private static RulesetDraft WithCamp()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("wolf", MeleeStats(hp: 300, armor: 0, dmgMin: 15, dmgMax: 15, speed: 320), new Bounty(30, 30, 40)));
        draft.Camps.Add(new NeutralCampDefinition("camp.a", P(3000, 2500), new[] { new WaveEntry("wolf", 2) },
            firstSpawnMs: 0, respawnMs: 10000, leashRadius: 600));
        return draft;
    }

    [Fact]
    public void Camp_SpawnsItsCreepsAsNeutrals()
    {
        var kernel = WithCamp().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());

        kernel.Step();

        var creeps = CampCreeps(kernel.State);
        Assert.Equal(2, creeps.Count);
        Assert.All(creeps, c => Assert.Equal("wolf", c.TypeId));
        Assert.Single(kernel.EventsOf<CampSpawned>());
    }

    [Fact]
    public void CampCreeps_IgnorePassersByAndLaneCreepsIgnoreThem()
    {
        var draft = WithCamp();
        draft.Lanes[0] = new LaneDefinition("mid", new[] { P(1000, 2500), P(9000, 2500) });   // the lane runs through the camp
        draft.DawnWave.Add(new WaveEntry("melee", 1));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 1, ctx => H(ctx.State, "alpha").Position = P(3100, 2500));

        kernel.RunTicks(30 * 15);

        HashSet<EntityId> camp = CampCreeps(kernel.State).Select(c => c.Id).ToHashSet();
        Assert.Equal(2, camp.Count);
        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => camp.Contains(a.Attacker) || camp.Contains(a.Target));
    }

    [Fact]
    public void HittingOneCampCreep_AggroesTheWholeCamp()
    {
        var kernel = WithCamp().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 1, ctx => H(ctx.State, "alpha").Position = P(3150, 2500));
        At(kernel, 4, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(CampCreeps(ctx.State)[0].Id)));

        kernel.RunTicks(90);

        EntityId alpha = H(kernel.State, "alpha").Id;
        var attackers = kernel.EventsOf<AttackStarted>().Where(a => a.Target == alpha).Select(a => a.Attacker).ToHashSet();
        Assert.Equal(2, attackers.Count);
    }

    [Fact]
    public void CampCreeps_ReturnHomeWhenTheAttackerLeavesTheLeash()
    {
        var kernel = WithCamp().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 1, ctx => H(ctx.State, "alpha").Position = P(3150, 2500));
        At(kernel, 4, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(CampCreeps(ctx.State)[0].Id)));
        At(kernel, 40, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Move(P(200, 5000))));

        kernel.RunTicks(400);

        Assert.All(CampCreeps(kernel.State), c => Assert.True(FixedVector2.Distance(c.Position, P(3000, 2500)).ToDouble() < 60));
    }

    [Fact]
    public void KillingCampCreeps_PaysTheKillerAndSharesXpOnlyWithItsTeam()
    {
        var kernel = WithCamp().StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid") });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3200, 2500);
            H(ctx.State, "beta").Position = P(3200, 2700);
            H(ctx.State, "gamma").Position = P(3200, 2300);
            foreach (MatchUnit creep in CampCreeps(ctx.State))
                Damage.Deal(ctx, H(ctx.State, "alpha").Id, creep, Fixed.FromInt(9999), DamageType.Pure);
        });

        kernel.RunTicks(3);

        Assert.Equal(2, H(kernel.State, "alpha").Hero!.LastHits);
        Assert.Equal(2, kernel.EventsOf<GoldEarned>().Count(g => g.Reason == "last_hit" && g.Hero == H(kernel.State, "alpha").Id));
        Assert.Equal(40, H(kernel.State, "alpha").Hero!.Xp);   // 2 × 40 XP shared by alpha and beta
        Assert.Equal(40, H(kernel.State, "beta").Hero!.Xp);
        Assert.Equal(0, H(kernel.State, "gamma").Hero!.Xp);
    }

    [Fact]
    public void Camp_RespawnsOnlyAfterBeingCleared()
    {
        var kernel = WithCamp().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 1, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, CampCreeps(ctx.State)[0], Fixed.FromInt(9999), DamageType.Pure));
        At(kernel, 400, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, CampCreeps(ctx.State)[0], Fixed.FromInt(9999), DamageType.Pure));

        kernel.RunTicks(399);
        Assert.Single(CampCreeps(kernel.State));   // one wolf left: no respawn yet
        Assert.Single(kernel.EventsOf<CampSpawned>());

        kernel.RunTicks(303);   // ticks 399..701
        Assert.Equal(2, CampCreeps(kernel.State).Count);   // seen cleared on tick 401, back 300 ticks later
        Assert.Equal(2, kernel.EventsOf<CampSpawned>().Count());
    }

    /// <summary>
    /// Dota's camps spawn on the clock: a cleared camp comes back at the next spawn time
    /// (first spawn + whole intervals), not a full interval after it was cleared.
    /// </summary>
    [Fact]
    public void ClockCamp_RespawnsAtTheNextIntervalMark()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("wolf", MeleeStats(hp: 300, armor: 0, dmgMin: 15, dmgMax: 15, speed: 320), new Bounty(30, 30, 40)));
        draft.Camps.Add(new NeutralCampDefinition("camp.a", P(3000, 2500), new[] { new WaveEntry("wolf", 2) },
            firstSpawnMs: 0, respawnMs: 10000, leashRadius: 600, alignedToClock: true));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        void ClearAt(long tick) => At(kernel, tick, ctx =>
        {
            foreach (MatchUnit creep in CampCreeps(ctx.State).Where(c => c.IsAlive).ToList())
                Damage.Deal(ctx, H(ctx.State, "alpha").Id, creep, Fixed.FromInt(9999), DamageType.Pure);
        });
        ClearAt(250);   // cleared late in the first interval: back at tick 300, not 550
        ClearAt(310);   // cleared early in the second: back at tick 600

        kernel.RunTicks(302);
        Assert.Equal(2, kernel.EventsOf<CampSpawned>().Count());
        kernel.RunTicks(297);   // up to tick 599
        Assert.Equal(2, kernel.EventsOf<CampSpawned>().Count());
        kernel.RunTicks(3);
        Assert.Equal(3, kernel.EventsOf<CampSpawned>().Count());
    }

    [Fact]
    public void Validation_RejectsCampsWithUnknownCreepsOrInsideObstacles()
    {
        var draft = WithCamp();
        draft.Camps.Add(new NeutralCampDefinition("camp.ghost", P(4000, 4000), new[] { new WaveEntry("ghost", 1) }, 0, 10000, 600));
        draft.Obstacles.Add(ObstacleDefinition.Circle("rock", P(3000, 2500), 100));

        IReadOnlyList<string> errors = draft.Build().Validate();

        Assert.Contains(errors, e => e.Contains("ghost"));
        Assert.Contains(errors, e => e.Contains("camp.a"));
    }

    [Fact]
    public void Ai_FarmsTheJungleWhenItsLaneIsEmpty()
    {
        var draft = WithCamp();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 });

        kernel.RunTicks(30 * 60);

        Assert.True(H(kernel.State, "alpha").Hero!.LastHits >= 2, "alpha should have cleared the camp");
    }

    /// <summary>
    /// After the laning phase a core farms the jungle between waves (backlog B-108): its lane always has
    /// allied creeps, yet with no enemy wave at the front it clears a nearby camp, and it is back for the waves.
    /// </summary>
    [Fact]
    public void Ai_CoreFarmsANearbyCampBetweenWaves_AfterLaning()
    {
        var draft = new RulesetDraft();
        draft.Creeps[0] = new CreepType("melee", MeleeStats(hp: 250), new Bounty(40, 40, 60));   // waves die fast: time between them
        draft.Creeps.Add(new CreepType("wolf", MeleeStats(hp: 300, armor: 0, dmgMin: 15, dmgMax: 15, speed: 320), new Bounty(30, 30, 40)));
        draft.Camps.Add(new NeutralCampDefinition("camp.a", P(4000, 3900), new[] { new WaveEntry("wolf", 2) },
            firstSpawnMs: 0, respawnMs: 30000, leashRadius: 600));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Personalities["alpha"] = new HeroPersonality { Greed = 0.8 };
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 60 * 4);

        EntityId alpha = H(kernel.State, "alpha").Id;
        var kills = kernel.EventsOf<UnitKilled>().Where(e => e.Killer == alpha).ToList();
        int neutrals = kills.Count(e => e.VictimTeam == Team.Neutral);
        int laneCreeps = kills.Count(e => e.VictimTeam == Team.Dusk);
        Assert.True(neutrals >= 4, $"alpha killed {neutrals} camp creeps, {laneCreeps} lane creeps");
        Assert.True(laneCreeps >= 5, $"alpha killed {laneCreeps} lane creeps of 8 waves, {neutrals} camp creeps");
    }
}
