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
/// Eclipse (Dota 7.41f data and official notes; the project owner: all the beams, 0.6 s apart, the first at once): every beam strikes a
/// random visible enemy around with Lucent Beam of its current level, at most five beams a unit; none without Lucent Beam learned.
/// </summary>
public class EclipseTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static EffectDefinition Strike() => EffectDefinition.RandomStrike(675, TargetTeam.Enemy, "eclipse_hits", 5,
        new ModifierSpec("eclipse_hits", 10000, maxStacks: int.MaxValue),
        EffectDefinition.Damage(DamageType.Pure, 75, 125, 175, 225).LevelledBy("lucent"));

    /// <summary>Six beams: one at once, five more 0.6 s apart.</summary>
    private static AbilityDefinition Eclipse() => new AbilityDefinition("eclipse", "Eclipse", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 100000 },
        new[] { 0 }, new[] { 1 }, new[]
        {
            Strike(),
            EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("eclipse", 3000, periodMs: 600, periodicFromBearer: true, periodicEffects: new[] { Strike() }))),
        });

    private static SimulationKernel<MatchState> Arena(int lucentLevel, int enemies)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("lucent", "Lucent Beam", AbilityTargeting.Unit, TargetTeam.Enemy, 800, 0, new[] { 5000, 5000, 5000, 5000 },
            new[] { 0, 0, 0, 0 }, new[] { 1, 1, 1, 1 }, new[] { EffectDefinition.Damage(DamageType.Pure, 75, 125, 175, 225) }));
        draft.Abilities.Add(Eclipse());
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "eclipse", "lucent" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5300, 5000);
            H(ctx.State, "gamma").Position = enemies >= 2 ? P(5000, 5300) : P(9000, 9000);
            H(ctx.State, "delta").Position = enemies >= 3 ? P(4700, 5000) : P(9000, 8000);
            AbilityState eclipse = H(ctx.State, "alpha").Hero!.Abilities.Single(a => a.Definition.Id == "eclipse");
            AbilityState lucent = H(ctx.State, "alpha").Hero!.Abilities.Single(a => a.Definition.Id == "lucent");
            eclipse.Level = 1;
            lucent.Level = lucentLevel;
        });
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("eclipse")));
        return kernel;
    }

    private static List<DamageDealt> Beams(SimulationKernel<MatchState> kernel) =>
        kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id).ToList();

    [Fact]
    public void AllTheBeams_RandomVisibleEnemies_ByLucentBeamsLevel()
    {
        var kernel = Arena(lucentLevel: 3, enemies: 3);

        kernel.RunTicks(3 + 4 * 30);

        List<DamageDealt> beams = Beams(kernel);
        Assert.Equal(6, beams.Count);
        Assert.All(beams, b => Assert.Equal(175, b.Amount.ToDouble(), 0));
        Assert.True(beams.GroupBy(b => b.Target).Max(g => g.Count()) < 5);   // spread at random, not the first one up to the cap
    }

    [Fact]
    public void AtMostFiveBeams_OnOneUnit()
    {
        var kernel = Arena(lucentLevel: 1, enemies: 1);

        kernel.RunTicks(3 + 4 * 30);

        Assert.Equal(5, Beams(kernel).Count);
    }

    [Fact]
    public void NoBeams_WithoutLucentBeam()
    {
        var kernel = Arena(lucentLevel: 0, enemies: 3);

        kernel.RunTicks(3 + 4 * 30);

        Assert.Empty(Beams(kernel));
    }

    [Fact]
    public void AnInvisibleEnemy_IsNotStruck()
    {
        var kernel = Arena(lucentLevel: 1, enemies: 1);
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("invisible", -1, ModifierFlags.Invisible), H(ctx.State, "beta").Id));

        kernel.RunTicks(3 + 4 * 30);

        Assert.Empty(Beams(kernel));
    }

    /// <summary>A stacking modifier given again by another spec of the same key (a new level, another part of the ability) keeps its stacks.</summary>
    [Fact]
    public void StackingModifier_FromAnotherSpecOfTheSameKey_KeepsItsStacks()
    {
        var kernel = Arena(lucentLevel: 1, enemies: 1);
        At(kernel, 1, ctx =>
        {
            MatchUnit beta = H(ctx.State, "beta");
            Modifiers.Apply(ctx, beta, new ModifierSpec("swipes", 10000, maxStacks: int.MaxValue), H(ctx.State, "alpha").Id);
            Modifiers.Apply(ctx, beta, new ModifierSpec("swipes", 10000, maxStacks: int.MaxValue), H(ctx.State, "alpha").Id);
            Modifiers.Apply(ctx, beta, new ModifierSpec("swipes", 10000, maxStacks: int.MaxValue, armorBonus: Fixed.FromInt(-1)), H(ctx.State, "alpha").Id);
        });

        kernel.RunTicks(2);

        Assert.Equal(3, H(kernel.State, "beta").Modifiers.Single(m => m.Spec.Key == "swipes").Stacks);
    }
}
