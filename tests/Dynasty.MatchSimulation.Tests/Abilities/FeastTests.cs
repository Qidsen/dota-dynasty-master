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
/// Midnight Feast (Dota 7.41f data and the official description): at night only, it destroys a creep and restores a share of the
/// caster's maximum health and mana; heroes are no targets.
/// </summary>
public class FeastTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static AbilityDefinition Feast() => new AbilityDefinition("feast", "Midnight Feast", AbilityTargeting.Unit, TargetTeam.Enemy, 300, 0, new[] { 30000 },
        new[] { 0 }, new[] { 1 }, new[]
        {
            EffectDefinition.Kill(),
            EffectDefinition.OnCaster(EffectDefinition.ScaledHeal(DamageScaling.TargetMaxHealthPercent, 2500),
                EffectDefinition.ScaledRestoreMana(DamageScaling.TargetMaxManaPercent, 1600)),
        }, nightOnly: true, targetsCreepsOnly: true);

    /// <summary>Day for the first second, then night for a second.</summary>
    private static SimulationKernel<MatchState> Arena(bool ai = false)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.DayNight = new DayNightRules(1000, 1000);
        draft.Abilities.Add(Feast());
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "feast" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5200, 5000);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(1000);
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(100);
        });
        return kernel;
    }

    private static MatchUnit Creep(SimulationContext<MatchState> ctx)
    {
        MatchUnit creep = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, -1);
        creep.Position = P(5150, 5100);
        Modifiers.Apply(ctx, creep, ModifierSpec.Status("stun", -1, ModifierFlags.Stunned), creep.Id);
        return creep;
    }

    [Theory]
    [InlineData(5, false)]    // day
    [InlineData(35, true)]    // night
    public void Feast_AtNightOnly_DestroysTheCreep_AndRestoresHealthAndMana(int castTick, bool eats)
    {
        var kernel = Arena();
        MatchUnit? creep = null;
        At(kernel, 1, ctx => creep = Creep(ctx));
        At(kernel, castTick, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("feast", creep!.Id)));

        kernel.RunTicks(castTick + 3);

        Assert.Equal(!eats, creep!.IsAlive);
        Assert.Equal(eats ? 1500 : 1000, H(kernel.State, "alpha").Hp.ToDouble(), 0);       // 25% of 2000
        Assert.Equal(eats ? 260 : 100, H(kernel.State, "alpha").Hero!.Mana.ToDouble(), 0);  // 16% of 1000
    }

    [Fact]
    public void Feast_DoesNotTakeAHero()
    {
        var kernel = Arena();
        At(kernel, 35, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("feast", H(ctx.State, "beta").Id)));

        kernel.RunTicks(40);

        Assert.True(H(kernel.State, "beta").IsAlive);
        Assert.DoesNotContain(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "feast");
    }

    /// <summary>The AI feasts on a creep when hurt at night.</summary>
    [Fact]
    public void Ai_FeastsOnACreep_WhenHurtAtNight()
    {
        var kernel = Arena();
        MatchUnit? creep = null;
        At(kernel, 1, ctx => creep = Creep(ctx));
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(55);

        List<AbilityCast> feasts = kernel.EventsOf<AbilityCast>().Where(c => c.AbilityId == "feast").ToList();
        Assert.Single(feasts);
        Assert.True(kernel.Events.Records.First(r => r.Event is AbilityCast c && c.AbilityId == "feast").Tick.Value >= 30);   // not by day
        Assert.False(creep!.IsAlive);
        Assert.DoesNotContain(kernel.EventsOf<AbilityCastFailed>(), f => f.AbilityId == "feast");   // no tries by day
    }

    [Fact]
    public void Ai_DoesNotFeast_WhenWhole()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            Creep(ctx);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(2000);
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(1000);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(55);

        Assert.DoesNotContain(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "feast");
    }

    /// <summary>An active ability's passive part (Midnight Feast's lifesteal, Kraken Shell's block) is on once the ability is learned.</summary>
    [Fact]
    public void AnActiveAbility_WithAPassivePart_HasItOnceLearned()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("shell", "Shell", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, new[] { 10000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.OnCaster(EffectDefinition.Heal(1)) }, passiveModifiers: new[] { new ModifierSpec("shell_block", -1, armorBonus: Fixed.FromInt(7)) }));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "shell" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });

        kernel.RunTicks(3);

        Assert.Equal(7, H(kernel.State, "alpha").Armor.ToDouble(), 1);
    }
}
