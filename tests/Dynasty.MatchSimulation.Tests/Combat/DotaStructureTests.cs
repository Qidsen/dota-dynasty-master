using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Dota 7.41f structures (docs/DOTA_RULES_AUDIT.md; Liquipedia «Buildings» as of 7.41f; Valve 7.27; decisions D-051):
/// a tower gives its team bounty to every enemy hero and the last hitter a further 110–130 (T1); a barracks' last hitter
/// gets only the last-hit bounty; a building below 10% HP can be denied — a denied tower gives both teams half its team
/// bounty, a denied barracks still gives its enemies the full team bounty; the ancient cannot be denied.
/// </summary>
public class DotaStructureTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly StructureBounty Tower = new(teamGold: 90, lastHitGoldMin: 110, lastHitGoldMax: 130, lastHitterGetsTeamGold: true,
        deniedEnemyGoldPercent: 50, deniedOwnGoldPercent: 50);

    private static readonly StructureBounty Barracks = new(teamGold: 155, lastHitGoldMin: 90, lastHitGoldMax: 135, lastHitterGetsTeamGold: false,
        deniedEnemyGoldPercent: 100, deniedOwnGoldPercent: 0);

    /// <summary>Dawn: alpha, gamma; Dusk: beta, delta; all far from the towers. Dusk also has a barracks.</summary>
    private static SimulationKernel<MatchState> Arena()
    {
        RulesetDraft draft = TestAbilities.Draft();
        draft.StructureTypes.Clear();
        draft.StructureTypes.Add(new StructureType("tower", TowerStats(), Tower, denyHealthPercent: 10));
        draft.StructureTypes.Add(new StructureType("ancient", AncientStats()));
        draft.StructureTypes.Add(new StructureType("barracks", AncientStats(), Barracks, denyHealthPercent: 10));
        draft.Placements.Add(new StructurePlacement("dusk.mid.barracks", Team.Dusk, "barracks", P(8500, 3000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 2000);
            H(ctx.State, "gamma").Position = P(5000, 2200);
            H(ctx.State, "beta").Position = P(5000, 8000);
            H(ctx.State, "delta").Position = P(5000, 8200);
        });
        return kernel;
    }

    private static MatchUnit S(MatchState state, string placement) => state.Units.Single(u => u.Placement?.Id == placement);

    private static Dictionary<string, double> Gold(MatchState state) =>
        new[] { "alpha", "gamma", "beta", "delta" }.ToDictionary(h => h, h => H(state, h).Hero!.Gold.ToDouble());

    private static Dictionary<string, double> GainsAfter(SimulationKernel<MatchState> kernel, Action<SimulationContext<MatchState>> kill)
    {
        Dictionary<string, double> before = new();
        At(kernel, 1, ctx => before = Gold(ctx.State));
        At(kernel, 2, kill);
        kernel.RunTicks(4);
        return Gold(kernel.State).ToDictionary(p => p.Key, p => Math.Floor(p.Value - before[p.Key]));   // without the passive income of these ticks
    }

    [Fact]
    public void TowerLastHit_GivesTheTeamBounty_AndTheLastHitterABonus()
    {
        var kernel = Arena();
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, S(ctx.State, "dusk.mid.t1"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.InRange(gains["alpha"], 90 + 110, 90 + 130);
        Assert.Equal(90, gains["gamma"]);
        Assert.Equal((0d, 0d), (gains["beta"], gains["delta"]));
    }

    [Fact]
    public void TowerDestroyedWithoutAHeroLastHit_GivesOnlyTheTeamBounty()
    {
        var kernel = Arena();
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, default, S(ctx.State, "dusk.mid.t1"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.Equal((90d, 90d), (gains["alpha"], gains["gamma"]));
    }

    [Fact]
    public void BarracksLastHitter_GetsOnlyTheLastHitBounty()
    {
        var kernel = Arena();
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, S(ctx.State, "dusk.mid.barracks"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.InRange(gains["alpha"], 90, 135);
        Assert.Equal(155, gains["gamma"]);
    }

    [Fact]
    public void ABuildingCanBeDeniedOnlyBelowTenPercent_AndADeniedTowerGivesBothTeamsHalf()
    {
        var kernel = Arena();
        bool above = true, below = false, ancient = true;
        At(kernel, 1, ctx =>
        {
            MatchUnit tower = S(ctx.State, "dusk.mid.t1");
            tower.Hp = Fixed.FromInt(tower.MaxHp / 10);
            above = ctx.State.CanAttack(H(ctx.State, "beta"), tower);
            tower.Hp = Fixed.FromInt(tower.MaxHp / 10 - 1);
            below = ctx.State.CanAttack(H(ctx.State, "beta"), tower);
        });
        At(kernel, 3, ctx =>
        {
            MatchUnit fort = S(ctx.State, "dusk.ancient");   // vulnerable once the tower is gone
            fort.Hp = Fixed.One;
            ancient = ctx.State.CanAttack(H(ctx.State, "beta"), fort) || !ctx.State.CanAttack(H(ctx.State, "alpha"), fort);
        });
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, S(ctx.State, "dusk.mid.t1"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.False(above);
        Assert.True(below);
        Assert.False(ancient);
        Assert.Equal(new[] { 45d, 45d, 45d, 45d }, new[] { gains["alpha"], gains["gamma"], gains["beta"], gains["delta"] });
    }

    [Fact]
    public void ADeniedBarracks_StillGivesItsEnemiesTheFullTeamBounty()
    {
        var kernel = Arena();
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, S(ctx.State, "dusk.mid.barracks"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.Equal(new[] { 155d, 155d, 0d, 0d }, new[] { gains["alpha"], gains["gamma"], gains["beta"], gains["delta"] });
    }
}
