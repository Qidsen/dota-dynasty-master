using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Lane aggro: with <see cref="CombatRules.HeroAggro"/> creeps and towers hit enemy creeps before
/// enemy heroes, and turn on a hero that attacks one of their heroes.
/// </summary>
public class AggroTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    /// <summary>
    /// One creep per side, alpha (Dawn) and beta (Dusk); everything placed at the end of tick 0. Returns
    /// once the placement is seen; at the end of the next tick alpha is ordered to attack beta, if asked.
    /// </summary>
    private static SimulationKernel<MatchState> Lane(bool aggro, Action<SimulationContext<MatchState>> place, bool alphaAttacksBeta = false)
    {
        var draft = new RulesetDraft { HeroAggro = aggro };
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, place));
        if (alphaAttacksBeta)
            kernel.AddSystem(new LambdaSystem(VisionRefresh, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id))));
        kernel.RunTicks(VisionRefresh);   // the new positions are seen at the next vision update
        return kernel;
    }

    private const int VisionRefresh = Dynasty.MatchSimulation.Vision.VisionSystem.IntervalTicks + 1;

    private static MatchUnit Creep(MatchState state, Team team) => state.Creeps(team).Single();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Creep_HitsTheEnemyCreepBeforeACloserHero_OnlyWithAggroRules(bool aggro)
    {
        var kernel = Lane(aggro, ctx =>
        {
            Creep(ctx.State, Team.Dusk).Position = P(5300, 5000);
            Creep(ctx.State, Team.Dawn).Position = P(4950, 5000);
            H(ctx.State, "alpha").Position = P(5150, 5000);
        });

        MatchUnit duskCreep = Creep(kernel.State, Team.Dusk);
        Assert.Equal(aggro ? Creep(kernel.State, Team.Dawn).Id : H(kernel.State, "alpha").Id, duskCreep.TargetId);
    }

    [Fact]
    public void Creep_TurnsOnAHeroThatAttacksAnAlliedHero()
    {
        var kernel = Lane(true, ctx =>
        {
            Creep(ctx.State, Team.Dusk).Position = P(5300, 5000);
            Creep(ctx.State, Team.Dawn).Position = P(5200, 5000);
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5450, 5000);
        }, alphaAttacksBeta: true);
        Assert.Equal(Creep(kernel.State, Team.Dawn).Id, Creep(kernel.State, Team.Dusk).TargetId);

        kernel.RunTicks(2);

        Assert.Equal(H(kernel.State, "alpha").Id, Creep(kernel.State, Team.Dusk).TargetId);
    }

    [Fact]
    public void Tower_HitsTheCreepBeforeTheHero_UntilTheHeroAttacksAHeroInItsRange()
    {
        var kernel = Lane(true, ctx =>
        {
            Creep(ctx.State, Team.Dusk).Position = P(8500, 5000);
            Creep(ctx.State, Team.Dawn).Position = P(6450, 5000);
            H(ctx.State, "alpha").Position = P(6600, 5000);
            H(ctx.State, "beta").Position = P(6750, 5000);
        }, alphaAttacksBeta: true);
        MatchUnit tower = kernel.State.Units.Single(u => u.Placement?.Id == "dusk.mid.t1");
        Assert.Equal(Creep(kernel.State, Team.Dawn).Id, tower.TargetId);

        kernel.RunTicks(2);

        Assert.Equal(H(kernel.State, "alpha").Id, tower.TargetId);
    }
}
