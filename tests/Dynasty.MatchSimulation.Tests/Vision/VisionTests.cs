using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Vision;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Vision;

public class VisionTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    /// <summary>Dawn alpha at (5000,1500); Dusk beta and gamma placed by the caller. No creeps.</summary>
    private static SimulationKernel<MatchState> Field(FixedVector2 beta, FixedVector2 gamma, RulesetDraft? draft = null)
    {
        draft ??= new RulesetDraft().WithoutWaves();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 1500);
            H(ctx.State, "beta").Position = beta;
            H(ctx.State, "gamma").Position = gamma;
        }));
        return kernel;
    }

    [Fact]
    public void OwnUnits_AreAlwaysVisibleToTheirTeam()
    {
        var kernel = Field(P(9000, 1500), P(9000, 2500));
        kernel.RunTicks(4);

        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "alpha")));
        Assert.True(kernel.State.IsVisibleTo(Team.Dusk, H(kernel.State, "beta")));
    }

    [Fact]
    public void Enemies_AreVisibleOnlyInsideVisionRange()
    {
        var kernel = Field(P(6100, 1500), P(5000, 3000));
        kernel.RunTicks(4);

        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));    // 1100 away
        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "gamma")));  // 1500 away
    }

    [Fact]
    public void Obstacles_BlockLineOfSight()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Obstacles.Add(ObstacleDefinition.Rectangle("cliff", P(5400, 1000), P(5500, 2000)));
        var kernel = Field(P(6000, 1500), P(5000, 2300), draft);
        kernel.RunTicks(4);

        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));   // behind the cliff
        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "gamma")));   // in the open, 800 away
    }

    [Fact]
    public void DeadHeroes_GiveNoVision()
    {
        var kernel = Field(P(6000, 1500), P(9000, 2500));
        kernel.AddSystem(new LambdaSystem(0, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(9999), DamageType.Pure)));
        kernel.RunTicks(7);

        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));
    }

    private static RulesetDraft WithWard()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("ward", new UnitStats(10, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 8,
            visionRange: 1600, innateFlags: ModifierFlags.Invisible)));
        return draft;
    }

    private static MatchUnit SpawnWard(SimulationContext<MatchState> ctx, MatchUnit owner, FixedVector2 at)
    {
        MatchUnit ward = ctx.State.SpawnSummon(ctx.Ids.Allocate(), owner, ctx.State.Ruleset.FindCreepType("ward")!, ctx.Tick.Value, -1);
        ward.Position = at;
        return ward;
    }

    [Fact]
    public void InvisibleUnits_StayHiddenWithoutTrueSight()
    {
        var kernel = Field(P(5300, 1500), P(9000, 2500), WithWard());
        MatchUnit? ward = null;
        kernel.AddSystem(new LambdaSystem(0, ctx => ward = SpawnWard(ctx, H(ctx.State, "beta"), P(5200, 1500))));
        kernel.RunTicks(4);

        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, ward!));
        Assert.True(kernel.State.IsVisibleTo(Team.Dusk, ward!));
    }

    [Fact]
    public void TrueSight_RevealsInvisibleUnits()
    {
        var kernel = Field(P(9000, 1500), P(9000, 2500), WithWard());
        MatchUnit? ward = null;
        kernel.AddSystem(new LambdaSystem(0, ctx => ward = SpawnWard(ctx, H(ctx.State, "beta"), P(3300, 5000))));   // 300 from the Dawn tower
        kernel.RunTicks(4);

        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, ward!));
    }

    [Fact]
    public void Wards_GrantVisionToTheirTeam()
    {
        var kernel = Field(P(9000, 1500), P(5000, 2900), WithWard());
        kernel.AddSystem(new LambdaSystem(0, ctx => SpawnWard(ctx, H(ctx.State, "alpha"), P(5000, 2200))));
        kernel.RunTicks(4);

        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "gamma")));   // 1400 from alpha (out of its sight), 700 from the ward
    }

    [Fact]
    public void Towers_DoNotAttackUndetectedInvisibleUnits()
    {
        var kernel = Field(P(9000, 1500), P(9000, 2500), WithWard());
        kernel.AddSystem(new LambdaSystem(0, ctx => SpawnWard(ctx, H(ctx.State, "alpha"), P(6200, 5000))));   // 800 from the Dusk tower: in range, outside its 700 true sight
        kernel.RunTicks(90);

        Assert.Empty(kernel.EventsOf<AttackStarted>());
    }

    [Fact]
    public void Knowledge_KeepsTheLastSeenPositionAfterAnEnemyLeavesVision()
    {
        var kernel = Field(P(6000, 1500), P(9000, 2500));
        kernel.AddSystem(new LambdaSystem(30, ctx => H(ctx.State, "beta").Position = P(9000, 1500)));
        kernel.RunTicks(40);

        MatchUnit beta = H(kernel.State, "beta");
        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, beta));
        LastSeen? seen = kernel.State.KnowledgeOf(Team.Dawn).LastSeenOf(beta.Id);
        Assert.NotNull(seen);
        Assert.Equal(P(6000, 1500), seen!.Position);
        Assert.InRange(seen.Tick, 27, 30);
    }

    [Fact]
    public void AiControlledMatch_NeverHoldsOrdersOnUnseenUnits()
    {
        var lineup = new MatchLineup(
            new[] { new HeroPick("ironvow", "bot"), new HeroPick("dawnmender", "bot"), new HeroPick("emberwisp", "mid"), new HeroPick("stonewarden", "top"), new HeroPick("thornkeeper", "top") },
            new[] { new HeroPick("starborn", "top"), new HeroPick("shadeblade", "top"), new HeroPick("frostweaver", "mid"), new HeroPick("hivecaller", "bot"), new HeroPick("siegewright", "bot") });
        var kernel = Match.Create(BaseRuleset.Create(), 3, lineup);
        Dynasty.HeroAI.HeroAi.Attach(kernel);
        var leaks = new List<string>();
        kernel.AddSystem(new OrderAuditSystem(leaks));

        kernel.RunTicks(10 * 60 * 30);

        Assert.Empty(leaks);
    }

    /// <summary>After every tick: no hero holds an attack/cast order on a unit its team cannot see.</summary>
    private sealed class OrderAuditSystem : Dynasty.Core.Kernel.ISimulationSystem<MatchState>
    {
        private readonly List<string> _leaks;

        public OrderAuditSystem(List<string> leaks) => _leaks = leaks;

        public string Name => "test-order-audit";

        public void Tick(SimulationContext<MatchState> context)
        {
            foreach (MatchUnit hero in context.State.Heroes)
            {
                HeroOrder order = hero.Hero!.Order;
                if (!order.Target.IsValid || hero.Hero.IsCasting)
                    continue;
                MatchUnit? target = context.State.Find(order.Target);
                if (target != null && !context.State.IsVisibleTo(hero.Team, target) && _leaks.Count < 20)
                    _leaks.Add($"tick {context.Tick.Value}: {hero.TypeId} {order.Kind} on unseen {target.TypeId}");
            }
        }
    }

    [Fact]
    public void AttackOrders_OnTargetsThatVanishIntoFog_AreDropped()
    {
        var kernel = Field(P(6000, 1500), P(9000, 2500));
        kernel.AddSystem(new LambdaSystem(1, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id))));
        kernel.AddSystem(new LambdaSystem(10, ctx => H(ctx.State, "beta").Position = P(9500, 3000)));
        kernel.RunTicks(30);

        Assert.Equal(HeroOrderKind.Idle, H(kernel.State, "alpha").Hero!.Order.Kind);
    }
}
