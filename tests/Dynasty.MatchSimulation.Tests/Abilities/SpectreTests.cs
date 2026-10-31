using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Dota 7.41f data and official descriptions: Shadow Step sends an illusion of Spectre at a target that follows and attacks it; Haunt
/// sends one at every enemy hero; Reality takes Spectre to the illusion nearest the point, destroying it.
/// </summary>
public class SpectreTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Haunt: an image at every enemy hero on the map (the area is global).</summary>
    private static readonly EffectDefinition Haunt = EffectDefinition.Area(30000, AreaCenter.Caster, TargetTeam.Enemy, true, EffectDefinition.Haunt(5000,
        new[] { new ModifierSpec("haunt_illusion", -1, attackDamagePercent: -50, incomingDamagePercent: 100) }));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 100, 100, 150, 1000, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("delta", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(2000, 2000);
            H(ctx.State, "delta").Position = P(2000, 2300);
            H(ctx.State, "beta").Position = P(6000, 6000);
            H(ctx.State, "gamma").Position = P(6000, 7000);
        });
        return kernel;
    }

    private static List<MatchUnit> Images(MatchState state) =>
        state.Units.Where(u => u.IsAlive && u.IllusionOf == H(state, "alpha").Id).ToList();

    [Fact]
    public void Haunt_SendsAnImageAtEveryEnemyHero_AttackingOnlyIt_ForLessDamage()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Haunt }, 1, H(ctx.State, "alpha"), P(0, 0)));

        kernel.RunTicks(2 * 30);

        List<MatchUnit> images = Images(kernel.State);
        Assert.Equal(2, images.Count);
        Assert.Equal(new[] { H(kernel.State, "beta").Id, H(kernel.State, "gamma").Id }.OrderBy(i => i.Value), images.Select(i => i.LockedTarget).OrderBy(i => i.Value));
        List<DamageDealt> hits = kernel.EventsOf<DamageDealt>().Where(d => images.Any(i => i.Id == d.Attacker)).ToList();
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal(50, h.Amount.ToDouble(), 0));   // half of 100
        Assert.All(hits, h => Assert.True(h.Target == H(kernel.State, "beta").Id || h.Target == H(kernel.State, "gamma").Id));
    }

    [Fact]
    public void Haunt_Images_GoAfterTheirTime()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Haunt }, 1, H(ctx.State, "alpha"), P(0, 0)));

        kernel.RunTicks(2 + 5 * 30 + 3);

        Assert.Empty(Images(kernel.State));
    }

    [Fact]
    public void Reality_TakesSpectreToTheImageNearestThePoint_DestroyingIt()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Haunt }, 1, H(ctx.State, "alpha"), P(0, 0)));
        FixedVector2 at = default;
        At(kernel, 10, ctx =>
        {
            MatchUnit nearGamma = Images(ctx.State).Single(i => i.LockedTarget == H(ctx.State, "gamma").Id);
            at = nearGamma.Position;
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Assume() }, 1, H(ctx.State, "alpha"), P(6000, 7200));
        });

        kernel.RunTicks(12);

        Assert.True(FixedVector2.Distance(at, H(kernel.State, "alpha").Position) < Fixed.FromInt(20));   // there (the crowd pushes a little)
        Assert.Single(Images(kernel.State));
        Assert.Equal(H(kernel.State, "beta").Id, Images(kernel.State)[0].LockedTarget);
    }

    /// <summary>The AI takes Reality only to an image of its own at the enemy it fights, never with none.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ai_TakesReality_OnlyToAnImageAtItsEnemy(bool image)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("reality", "Reality", AbilityTargeting.Point, TargetTeam.Enemy, 30000, 0, new[] { 1500 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Assume() }));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "reality" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5200, 5000);
            H(ctx.State, "beta").Hp = Fixed.FromInt(600);
            H(ctx.State, "gamma").Position = P(3000, 5000);
        });
        if (image)
            At(kernel, 1, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Haunt(10000, new[] { new ModifierSpec("img", -1) }) }, 1,
                H(ctx.State, "beta"), P(0, 0)));
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        foreach (string hero in new[] { "beta", "gamma" })
            settings.Uncontrolled.Add(hero);
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 * 30);

        Assert.Equal(image, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "reality"));
        if (!image)
            Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);   // it did fight
    }

    /// <summary>A Haunt image keeps after its target in the fog (Dota: it goes only when the target dies).</summary>
    [Fact]
    public void Haunt_Image_KeepsAfterAnUnseenTarget()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Haunt }, 1, H(ctx.State, "alpha"), P(0, 0)));
        At(kernel, 4, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("invisible", -1, ModifierFlags.Invisible), H(ctx.State, "beta").Id));

        kernel.RunTicks(30);

        Assert.Contains(Images(kernel.State), i => i.LockedTarget == H(kernel.State, "beta").Id);
    }
}
