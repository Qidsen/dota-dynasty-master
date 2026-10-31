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
/// Uproar (Dota 7.41f data and the official description; the project owner: the roar spends the stacks): every instance of 50 or more
/// damage from an enemy hero gives a stack (at most six); the roar slows enemies around and gives the Beast damage and armor, all by the
/// stacks, which are then gone.
/// </summary>
public class UproarTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Stack = new ModifierSpec("uproar_stacks", 20000, maxStacks: 6);

    private static readonly ModifierSpec Temper = new ModifierSpec("uproar", -1, onHeroDamageAtLeast: 50,
        onHeroDamage: new[] { EffectDefinition.Modifier(Stack) });

    private static AbilityDefinition Roar() => new AbilityDefinition("uproar", "Uproar", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 0 }, new[] { 0 },
        new[] { 1 }, new[]
        {
            EffectDefinition.Area(900, AreaCenter.Caster, TargetTeam.Enemy,
                EffectDefinition.Modifier(new ModifierSpec("uproar_slow", 3000, moveSpeedPercent: -10, maxStacks: 6)).StacksOf("uproar_stacks")),
            EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("uproar_roared", 7000, damageBonus: 40, armorBonus: Fixed.FromInt(5), maxStacks: 6))
                .StacksOf("uproar_stacks")),
            EffectDefinition.OnCaster(EffectDefinition.Consume("uproar_stacks")),
        }, passiveModifiers: new[] { Temper });

    private static SimulationKernel<MatchState> Arena(bool ai = false)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(Roar());
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "uproar" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5300, 5000);
            H(ctx.State, "gamma").Position = P(5000, 5300);
        });
        return kernel;
    }

    private static int Stacks(MatchState state) => H(state, "alpha").Modifiers.FirstOrDefault(m => m.Spec.Key == "uproar_stacks")?.Stacks ?? 0;

    private static void Hit(SimulationKernel<MatchState> kernel, long tick, string from, int amount) =>
        At(kernel, tick, ctx => Damage.Deal(ctx, H(ctx.State, from).Id, H(ctx.State, "alpha"), Fixed.FromInt(amount), DamageType.Pure));

    [Fact]
    public void Stacks_FromEnemyHeroDamageOfFiftyOrMore_AtMostSix()
    {
        var kernel = Arena();
        Hit(kernel, 2, "beta", 60);
        Hit(kernel, 3, "beta", 40);    // too little
        Hit(kernel, 4, "gamma", 80);   // an ally
        Hit(kernel, 5, "alpha", 80);   // itself
        int afterFirst = -1;
        At(kernel, 7, ctx => afterFirst = Stacks(ctx.State));
        for (int i = 0; i < 8; i++)
            Hit(kernel, 8 + i, "beta", 50);

        kernel.RunTicks(18);

        Assert.Equal(1, afterFirst);
        Assert.Equal(6, Stacks(kernel.State));
    }

    [Fact]
    public void Roar_SlowsAndEmpowersByTheStacks_ThenTheyAreGone()
    {
        var kernel = Arena();
        for (int i = 0; i < 3; i++)
            Hit(kernel, 2 + i, "beta", 60);
        int damageBefore = 0;
        At(kernel, 7, ctx => damageBefore = H(ctx.State, "alpha").AttackDamageMin);
        At(kernel, 8, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("uproar")));

        kernel.RunTicks(12);

        Assert.Equal(damageBefore + 3 * 40, H(kernel.State, "alpha").AttackDamageMin);
        Assert.Equal(15, H(kernel.State, "alpha").Armor.ToDouble(), 1);
        Assert.Equal(300 * 70 / 100, H(kernel.State, "beta").MoveSpeed);   // 3 × 10%
        Assert.Equal(0, Stacks(kernel.State));
    }

    [Fact]
    public void Roar_WithoutStacks_DoesNothing()
    {
        var kernel = Arena();
        At(kernel, 8, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("uproar")));

        kernel.RunTicks(12);

        Assert.Equal(300, H(kernel.State, "beta").MoveSpeed);
        Assert.Equal(0, H(kernel.State, "alpha").Armor.ToDouble(), 1);
    }

    /// <summary>The AI roars once it has three stacks or more, not before.</summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Ai_Roars_WithEnoughStacks(int hits, bool roars)
    {
        var kernel = Arena();
        for (int i = 0; i < hits; i++)
            Hit(kernel, 2 + i, "beta", 60);
        At(kernel, 6, ctx => H(ctx.State, "beta").Hp = Fixed.FromInt(800));
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 * 30);

        Assert.Equal(roars, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "uproar"));
        if (!roars)
            Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);   // it did fight
    }
}
