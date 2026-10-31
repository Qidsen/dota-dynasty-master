using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Entangle (Dota 7.41f data and official notes): at 5 stacks an enemy is entangled - rooted and burnt - and gains no stacks meanwhile;
/// the cast gives 2 stacks to heroes in the area and entangles creeps at once, and for 10 s each of Lone Druid's attacks on a hero adds one.
/// </summary>
public class EntangleTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Root = new ModifierSpec("entangled", 2000, ModifierFlags.Rooted, damagePerSecond: Fixed.FromInt(90), damageType: DamageType.Pure,
        periodMs: 200);

    private static readonly ModifierSpec Stack = new ModifierSpec("entangle_stacks", 8000, maxStacks: int.MaxValue, dispellable: false, triggerAtStacks: 5,
        onStacks: new[] { EffectDefinition.Modifier(Root) }, noStacksWhile: "entangled");

    private static readonly EffectDefinition[] Cast =
    {
        EffectDefinition.Area(350, AreaCenter.Point, TargetTeam.Enemy,
            EffectDefinition.IfHero(EffectDefinition.Modifier(Stack), EffectDefinition.Modifier(Stack)),
            EffectDefinition.IfNotHero(EffectDefinition.Modifier(Root))),
        EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("entangle_empowered", 10000, dispellable: false, onHitHeroesOnly: true,
            onHit: new[] { EffectDefinition.Modifier(Stack) }))),
    };

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5600, 5000);
        });
        return kernel;
    }

    private static int Stacks(MatchUnit unit) => unit.Modifiers.Where(m => m.Spec.Key == "entangle_stacks").Sum(m => m.Stacks);

    [Fact]
    public void FiveStacks_Entangle_AndNoStacksAreGainedMeanwhile()
    {
        var kernel = Arena();
        At(kernel, 2, ctx =>
        {
            for (int i = 0; i < 4; i++)
                Modifiers.Apply(ctx, H(ctx.State, "beta"), Stack, H(ctx.State, "alpha").Id);
        });
        bool rootedAtFour = true;
        At(kernel, 3, ctx => rootedAtFour = H(ctx.State, "beta").Modifiers.Any(m => m.Spec.Key == "entangled"));
        At(kernel, 4, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), Stack, H(ctx.State, "alpha").Id));
        At(kernel, 6, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), Stack, H(ctx.State, "alpha").Id));   // entangled: none

        kernel.RunTicks(4 + 31);

        MatchUnit beta = H(kernel.State, "beta");
        Assert.False(rootedAtFour);
        Assert.Contains(beta.Modifiers, m => m.Spec.Key == "entangled");
        Assert.Equal(0, Stacks(beta));
        Assert.Equal(90, (Fixed.FromInt(5000) - beta.Hp).ToDouble(), 0);   // 1 s of burning
    }

    [Fact]
    public void TheCast_GivesHeroesTwoStacks_EntanglesCreeps_AndEmpowersTheDruidsAttacks()
    {
        var kernel = Arena();
        MatchUnit? creep = null;
        At(kernel, 1, ctx =>
        {
            creep = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, -1);
            creep.Position = P(5700, 5000);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Cast, 1, H(ctx.State, "alpha"), P(5650, 5000)));
        int afterCast = 0;
        At(kernel, 3, ctx => afterCast = Stacks(H(ctx.State, "beta")));
        At(kernel, 4, ctx =>
        {
            for (int i = 0; i < 2; i++)
                AttackSystem.ApplyHit(ctx, H(ctx.State, "alpha"), H(ctx.State, "beta"), Fixed.FromInt(10), critical: false);
        });
        int afterTwoHits = 0;
        At(kernel, 5, ctx => afterTwoHits = Stacks(H(ctx.State, "beta")));
        At(kernel, 6, ctx => AttackSystem.ApplyHit(ctx, H(ctx.State, "alpha"), H(ctx.State, "beta"), Fixed.FromInt(10), critical: false));

        kernel.RunTicks(8);

        Assert.Equal(2, afterCast);
        Assert.Equal(4, afterTwoHits);
        Assert.Contains(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "entangled");
        Assert.Contains(creep!.Modifiers, m => m.Spec.Key == "entangled");
        Assert.Equal(0, Stacks(creep));
    }
}
