using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Abilities working on where a hero was (Dota 7.41f data and official notes): Time Lapse takes the caster back to its position, health
/// and mana of five seconds ago; Glimpse takes the target back to where it was four seconds ago, damaging it by a share of the
/// distance within a minimum and a maximum, and kills illusions at once; X Marks the Spot returns the marked hero to the mark when it
/// ends.
/// </summary>
public class HistoryTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5000, 3000);
            H(ctx.State, "gamma").Position = P(3000, 5000);
        });
        return kernel;
    }

    private static void Run(SimulationKernel<MatchState> kernel, long tick, EffectDefinition effect, string target) =>
        At(kernel, tick, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { effect }, 1, H(ctx.State, target), H(ctx.State, target).Position));

    [Fact]
    public void Rewind_WithVitals_TakesTheUnitBackToItsPositionHealthAndManaOfThatTime()
    {
        var kernel = Arena();
        At(kernel, 9, ctx =>   // recorded at the start of tick 10
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(1500);
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(400);
        });
        At(kernel, 10 + 3 * 30, ctx =>
        {
            H(ctx.State, "alpha").Position = P(7000, 5000);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(300);
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(50);
        });
        Run(kernel, 10 + 5 * 30, EffectDefinition.Rewind(5000, restoresVitals: true), "alpha");

        kernel.RunTicks(12 + 5 * 30);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Equal(P(5000, 5000), alpha.Position);
        Assert.Equal(1500, alpha.Hp.ToDouble(), 1);
        Assert.Equal(400, alpha.Hero!.Mana.ToDouble(), 1);
    }

    [Theory]
    [InlineData(400, 100)]    // 25% of 400
    [InlineData(2000, 160)]   // 25% of 2000 = 500: capped at the maximum
    [InlineData(40, 25)]      // 25% of 40 = 10: raised to the minimum
    public void Rewind_ThenDamageByTheDistance_WithinTheBounds(int moved, int expected)
    {
        var kernel = Arena();
        At(kernel, 10, ctx => H(ctx.State, "beta").Position = P(5000, 3000));
        At(kernel, 11 + 4 * 30 - 30, ctx => H(ctx.State, "beta").Position = P(5000 + moved, 3000));
        Run(kernel, 11 + 4 * 30,
            EffectDefinition.Rewind(4000, restoresVitals: false,
                EffectDefinition.BoundedScaledDamage(DamageType.Pure, DamageScaling.RewoundDistancePercent, new[] { 25 }, new[] { 160 }, 2500)), "beta");

        kernel.RunTicks(13 + 4 * 30);

        Assert.Equal(P(5000, 3000), H(kernel.State, "beta").Position);
        Assert.Equal(expected, (Fixed.FromInt(H(kernel.State, "beta").MaxHp) - H(kernel.State, "beta").Hp).ToDouble(), 0);
    }

    [Fact]
    public void Rewind_KillsAnIllusionAtOnce()
    {
        var kernel = Arena();
        MatchUnit? illusion = null;
        At(kernel, 5, ctx =>
        {
            illusion = ctx.State.SpawnIllusion(ctx.Ids.Allocate(), H(ctx.State, "beta"), H(ctx.State, "beta"), ctx.Tick.Value, ctx.Tick.Value + 3000);
            illusion.Position = P(5200, 3000);
        });
        At(kernel, 6, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Rewind(4000, restoresVitals: false) }, 1, illusion!, illusion!.Position));

        kernel.RunTicks(8);

        Assert.False(illusion!.IsAlive);
    }

    [Theory]
    [InlineData("beta", 3 * 30)]
    [InlineData("gamma", 6 * 30)]
    public void Mark_ReturnsTheHeroToTheMark_WhenItEnds(string target, long lasts)
    {
        var kernel = Arena();
        var mark = new ModifierSpec("x_mark", (int)(lasts * 1000 / 30), returnsOnExpiry: true);
        FixedVector2 marked = default;
        At(kernel, 5, ctx => marked = H(ctx.State, target).Position);
        Run(kernel, 5, EffectDefinition.Modifier(mark), target);
        At(kernel, 30, ctx => H(ctx.State, target).Position = P(8000, 8000));
        FixedVector2 before = default;
        At(kernel, 5 + lasts - 2, ctx => before = H(ctx.State, target).Position);

        kernel.RunTicks(5 + lasts + 3);

        Assert.Equal(P(8000, 8000), before);
        Assert.Equal(marked, H(kernel.State, target).Position);
    }

    /// <summary>The AI lapses back only when the hero has lost much of its health over the time it would go back, not as a fight spell.</summary>
    [Theory]
    [InlineData(true, 4 * 30)]
    [InlineData(false, 10 * 30)]   // a long fight against a weak enemy: no lapse at full health
    public void Ai_LapsesBack_WhenItLostMuchHealthMeanwhile(bool hurt, int ticks)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("lapse", "Time Lapse", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, new[] { 60000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Rewind(5000, restoresVitals: true) }));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "lapse" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5200, 5000);   // a fight next to it either way
            H(ctx.State, "beta").Hp = Fixed.FromInt(600);
            H(ctx.State, "gamma").Position = P(3000, 5000);
        });
        At(kernel, 2 * 30, ctx => H(ctx.State, "alpha").Hp = Fixed.FromInt(hurt ? 700 : 1900));
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        foreach (string hero in new[] { "beta", "gamma" })
            settings.Uncontrolled.Add(hero);
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(ticks);

        Assert.Equal(hurt, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "lapse"));
        if (!hurt)
            Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);   // it did fight
    }
}
