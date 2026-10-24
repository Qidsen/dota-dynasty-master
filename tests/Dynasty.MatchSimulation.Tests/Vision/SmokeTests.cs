using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Vision;

public class SmokeTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    public static readonly ModifierSpec SmokeSpec = new("smoke", 35000, ModifierFlags.Invisible, moveSpeedPercent: 15,
        breakOnAction: true, breakRadius: 1025);

    public static RulesetDraft WithSmoke(RulesetDraft? draft = null)
    {
        draft ??= new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("smoke", "Smoke", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Area(1200, AreaCenter.Caster, TargetTeam.Ally, heroesOnly: true, EffectDefinition.Modifier(SmokeSpec)) }));
        draft.Items.Add(new ItemDefinition("smoke", "Smoke", 80, activeAbilityId: "smoke", charges: 1));
        return draft;
    }

    /// <summary>Dawn alpha and gamma smoke up at (3000, 2000); Dusk beta waits far away.</summary>
    private static SimulationKernel<MatchState> Smoked(RulesetDraft? draft = null)
    {
        var kernel = WithSmoke(draft).StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "smoke"));
        At(kernel, 2, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 2000);
            H(ctx.State, "gamma").Position = P(3200, 2000);
            H(ctx.State, "beta").Position = P(8000, 2000);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("smoke"));
        });
        return kernel;
    }

    [Fact]
    public void Smoke_HidesAndSpeedsUpAlliedHeroesNearby()
    {
        var kernel = Smoked();
        kernel.RunTicks(5);

        MatchUnit alpha = H(kernel.State, "alpha");
        MatchUnit gamma = H(kernel.State, "gamma");
        Assert.True((alpha.Flags & ModifierFlags.Invisible) != 0);
        Assert.True((gamma.Flags & ModifierFlags.Invisible) != 0);
        Assert.Equal(345, alpha.MoveSpeed);
    }

    [Fact]
    public void SmokedHeroes_AreInvisibleToEnemiesOutsideTheBreakRadius()
    {
        var kernel = Smoked();
        At(kernel, 5, ctx => H(ctx.State, "beta").Position = P(4300, 2000));   // 1100 from gamma: sees it normally, but smoke holds
        kernel.RunTicks(10);

        Assert.False(kernel.State.IsVisibleTo(Team.Dusk, H(kernel.State, "gamma")));
        Assert.True((H(kernel.State, "gamma").Flags & ModifierFlags.Invisible) != 0);
    }

    [Fact]
    public void EnemyHeroNearby_BreaksTheSmoke()
    {
        var kernel = Smoked();
        At(kernel, 5, ctx => H(ctx.State, "beta").Position = P(4000, 2000));   // 800 from gamma
        kernel.RunTicks(10);

        Assert.True((H(kernel.State, "gamma").Flags & ModifierFlags.Invisible) == 0);
        Assert.True(kernel.State.IsVisibleTo(Team.Dusk, H(kernel.State, "gamma")));
        Assert.Contains(kernel.EventsOf<ModifierRemoved>(), r => r.Key == "smoke" && r.Reason == "broken");
    }

    [Fact]
    public void Attacking_BreaksTheAttackersSmoke()
    {
        var draft = new RulesetDraft();
        draft.DawnWave.Clear();
        var kernel = Smoked(draft);
        kernel.AddSystem(new TriggerSystem(
            ctx => ctx.State.Creeps(Team.Dusk).Any(c => ctx.State.IsVisibleTo(Team.Dawn, c) && FixedVector2.Distance(c.Position, H(ctx.State, "alpha").Position) < Fixed.FromInt(1000)),
            ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(ctx.State.Creeps(Team.Dusk).First().Id))));
        At(kernel, 6, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5200);
            H(ctx.State, "gamma").Position = P(3000, 1000);
        });

        kernel.RunTicks(30 * 20);

        Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);
        Assert.True((H(kernel.State, "alpha").Flags & ModifierFlags.Invisible) == 0);
        Assert.True((H(kernel.State, "gamma").Flags & ModifierFlags.Invisible) != 0);
    }

    [Fact]
    public void Smoke_DoesNotAffectCreeps()
    {
        var draft = new RulesetDraft();
        draft.DuskWave.Clear();
        var kernel = Smoked(draft);
        At(kernel, 1, ctx => ctx.State.Creeps(Team.Dawn).First().Position = P(3100, 2100));

        kernel.RunTicks(5);

        Assert.All(kernel.State.Creeps(Team.Dawn), c => Assert.True((c.Flags & ModifierFlags.Invisible) == 0));
    }

    [Fact]
    public void Ai_SmokesBeforeGankingAFarTarget()
    {
        var draft = WithSmoke();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "smoke"));
        At(kernel, 2, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 2000);
            H(ctx.State, "gamma").Position = P(3100, 2100);
            H(ctx.State, "beta").Position = P(5500, 2000);   // isolated, 2500 away, seen through a Dawn ward
            MatchUnit ward = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "alpha"),
                new CreepType("eye", new UnitStats(5, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 8, visionRange: 1000)), ctx.Tick.Value, -1);
            ward.Position = P(5400, 2000);
        });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("beta");
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(60);

        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "smoke");
    }
}
