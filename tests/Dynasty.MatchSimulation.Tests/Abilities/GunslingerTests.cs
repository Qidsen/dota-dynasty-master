using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Gunslinger (Dota 7.41f data and official notes): by chance an attack fires a second shot at another target within the attack range
/// plus a bonus, heroes first, then other units; with no other unit in range both shots hit the target.
/// </summary>
public class GunslingerTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Gunslinger = new ModifierSpec("gunslinger", -1, extraTargets: 1, extraTargetChancePercent: 100, extraTargetDamagePercent: 100,
        extraTargetRadius: 175, extraTargetRadiusFromAttackRange: true, extraTargetHeroesFirst: true, extraTargetElseSame: true);

    private static SimulationKernel<MatchState> Arena(bool gammaNear, bool creepNear)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 600, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5400, 5000);
            H(ctx.State, "gamma").Position = gammaNear ? P(5000, 5700) : P(9000, 9000);
        });
        At(kernel, 1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), Gunslinger, H(ctx.State, "alpha").Id);
            if (creepNear)
            {
                MatchUnit creep = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, -1);
                creep.Position = P(5100, 5100);   // nearer than gamma
                creep.Hp = Fixed.FromInt(100000);
                Modifiers.Apply(ctx, creep, ModifierSpec.Status("stun", -1, ModifierFlags.Stunned), creep.Id);
            }
        });
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id)));
        return kernel;
    }

    private static List<DamageDealt> FirstVolley(SimulationKernel<MatchState> kernel)
    {
        List<DamageDealt> hits = kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id).ToList();
        return hits.Take(2).ToList();
    }

    [Fact]
    public void TheSecondShot_GoesToAnotherHero_BeforeANearerCreep()
    {
        var kernel = Arena(gammaNear: true, creepNear: true);

        kernel.RunTicks(3 + 20);

        Assert.Contains(FirstVolley(kernel), h => h.Target == H(kernel.State, "gamma").Id);
    }

    [Fact]
    public void WithNoOtherUnit_BothShotsHitTheTarget()
    {
        var kernel = Arena(gammaNear: false, creepNear: false);

        kernel.RunTicks(3 + 20);

        List<DamageDealt> volley = FirstVolley(kernel);
        Assert.Equal(2, volley.Count);
        Assert.All(volley, h => Assert.Equal(H(kernel.State, "beta").Id, h.Target));
    }
}
