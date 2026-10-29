using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Berserker's Blood (bonuses by missing health, none at full, all below the threshold — the official note), Backstab of 7.41
/// (invisible after a while without attacking, an attack reveals; experience for hero kills), Nether Swap (positions swapped, damage
/// to an enemy only, a barrier for the caster and an ally; it interrupts a channel — the official note) and Sunder (shares of current
/// health exchanged, each kept at least at the minimum; debuff-immune enemies untouched — the official note).
/// </summary>
public class SwapAndScalingTests
{
    private static MatchUnit H(MatchState state, string id) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == id);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static HeroDefinition Hero(string id, IReadOnlyList<string>? abilities = null) => new(
        id, id, new[] { "carry" }, new UnitStats(1000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
        hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
        abilityIds: abilities,
        attributes: new HeroAttributes(PrimaryAttribute.Strength, strength: 20, agility: 0, intelligence: 0, strengthGain: Fixed.Zero, agilityGain: Fixed.Zero, intelligenceGain: Fixed.Zero));

    private static SimulationKernel<MatchState> Arena(RulesetDraft draft, string[]? alphaAbilities = null)
    {
        draft.Heroes.Clear();
        draft.Heroes.Add(Hero("alpha", alphaAbilities));
        foreach (string id in new[] { "beta", "gamma", "delta" })
            draft.Heroes.Add(Hero(id));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5600, 5000);
            H(ctx.State, "gamma").Position = P(4400, 5000);
            H(ctx.State, "delta").Position = P(9000, 9000);
        });
        return kernel;
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(55, 50)]   // halfway between full and 10%
    [InlineData(5, 100)]   // below 10%: all of it
    public void MissingHealthScaling_GrowsLinearly_FullBelowTheThreshold(int hpPercent, int expectedPercent)
    {
        var kernel = Arena(new RulesetDraft().WithoutWaves());
        var blood = new ModifierSpec("blood", -1, attackSpeedBonus: 200, magicResistBonus: 30, fullBelowHealthPercent: 10, hpRegenStrengthPercent: Fixed.FromInt(50));
        int baseSpeed = 0, baseResist = 0;
        double baseRegen = 0;
        At(kernel, 1, ctx =>
        {
            baseSpeed = H(ctx.State, "alpha").AttackSpeedBonus;
            baseRegen = H(ctx.State, "alpha").HpRegenPerSecond.ToDouble();
            baseResist = H(ctx.State, "alpha").MagicResistHundredths;
        });
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), blood, H(ctx.State, "alpha").Id));   // at full health
        At(kernel, 3, ctx => H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp * hpPercent / 100));   // then hurt: the bonuses follow

        kernel.RunTicks(5);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.InRange(alpha.AttackSpeedBonus - baseSpeed, 200 * expectedPercent / 100 - 1, 200 * expectedPercent / 100 + 1);
        Assert.Equal(10 * expectedPercent / 100.0, alpha.HpRegenPerSecond.ToDouble() - baseRegen, 1);   // 50% of 20 strength
        Assert.InRange(alpha.MagicResistHundredths - baseResist, 3000 * expectedPercent / 100 - 20, 3000 * expectedPercent / 100 + 20);
    }

    [Fact]
    public void Fade_InvisibleAfterAWhileWithoutAttacking_AnAttackReveals()
    {
        var kernel = Arena(new RulesetDraft().WithoutWaves());
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("backstab", -1, invisibleAfterIdleMs: 2000), H(ctx.State, "alpha").Id));
        bool early = true, faded = false;
        At(kernel, 30, ctx => early = ((H(ctx.State, "alpha").Flags & ModifierFlags.Invisible) != 0));
        At(kernel, 70, ctx => faded = ((H(ctx.State, "alpha").Flags & ModifierFlags.Invisible) != 0));
        At(kernel, 71, ctx =>
        {
            H(ctx.State, "beta").Position = P(5100, 5000);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id));
        });

        kernel.RunTicks(80);

        Assert.False(early);
        Assert.True(faded);
        Assert.False((H(kernel.State, "alpha").Flags & ModifierFlags.Invisible) != 0);
    }

    [Fact]
    public void Experience_IsGrantedToTheCaster()
    {
        var draft = new RulesetDraft().WithoutWaves();
        var kernel = Arena(draft);
        int before = 0;
        At(kernel, 1, ctx => before = H(ctx.State, "alpha").Hero!.Xp);
        At(kernel, 2, ctx => Systems.Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Experience(130) }, 1, H(ctx.State, "beta"), P(0, 0)));

        kernel.RunTicks(3);

        Assert.Equal(before + 130, H(kernel.State, "alpha").Hero!.Xp);
    }

    private static RulesetDraft WithSwap()
    {
        var draft = new RulesetDraft().WithoutWaves();
        var barrier = new ModifierSpec("swap_barrier", 10000, barrier: 150);
        draft.Abilities.Add(new AbilityDefinition("swap", "Nether Swap", AbilityTargeting.Unit, TargetTeam.Any, 1500, 0, new[] { 30000 }, new[] { 0 }, new[] { 1 },
            new[]
            {
                EffectDefinition.SwapPositions(),
                EffectDefinition.IfTeam(TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 150)),
                EffectDefinition.IfTeam(TargetTeam.Ally, EffectDefinition.Modifier(barrier)),
                EffectDefinition.OnCaster(EffectDefinition.Modifier(barrier)),
            }));
        return draft;
    }

    [Theory]
    [InlineData("beta", true)]
    [InlineData("gamma", false)]
    public void Swap_ExchangesPositions_DamagesOnlyAnEnemy_ShieldsTheCasterAndAnAlly(string target, bool enemy)
    {
        var kernel = Arena(WithSwap(), new[] { "swap" });
        FixedVector2 from = default, to = default;
        At(kernel, 1, ctx =>
        {
            from = H(ctx.State, "alpha").Position;
            to = H(ctx.State, target).Position;
        });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("swap", H(ctx.State, target).Id)));

        kernel.RunTicks(4);

        Assert.Equal(to, H(kernel.State, "alpha").Position);
        Assert.Equal(from, H(kernel.State, target).Position);
        int max = H(kernel.State, target).MaxHp;
        Assert.InRange(H(kernel.State, target).Hp.ToDouble(), (enemy ? max - 150 : max) - 1, enemy ? max - 150 + 1 : max);
        Assert.Contains(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "swap_barrier");
        Assert.Equal(!enemy, H(kernel.State, target).Modifiers.Any(m => m.Spec.Key == "swap_barrier"));
    }

    [Fact]
    public void Swap_InterruptsTheTargetsChannel()
    {
        var draft = WithSwap();
        draft.Abilities.Add(new AbilityDefinition("ritual", "Ritual", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 30000 }, new[] { 0 }, new[] { 1 },
            Array.Empty<EffectDefinition>(), channel: new ChannelSpec(new[] { 5000 }, 1000, new[] { EffectDefinition.OnCaster(EffectDefinition.Heal(1)) }, Array.Empty<EffectDefinition>(), null)));
        draft.Heroes.Clear();
        draft.Heroes.Add(Hero("alpha", new[] { "swap" }));
        draft.Heroes.Add(Hero("beta", new[] { "ritual" }));
        var k = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(k, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5600, 5000);
        });
        At(k, 2, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.CastNoTarget("ritual")));
        At(k, 5, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("swap", H(ctx.State, "beta").Id)));

        k.RunTicks(8);

        Assert.False(H(k.State, "beta").Hero!.IsChanneling);
    }

    [Theory]
    [InlineData(100, 20, 20, 100)]   // 100% <-> 20%: but the caster keeps at least... 20 is under 25: 25
    [InlineData(80, 60, 60, 80)]
    [InlineData(10, 90, 90, 25)]     // the target would drop to 10%: kept at 25%
    public void SwapHealth_ExchangesTheShares_EachKeptAtTheMinimum(int casterPercent, int targetPercent, int casterAfter, int targetAfter)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("sunder", "Sunder", AbilityTargeting.Unit, TargetTeam.Any, 800, 0, new[] { 30000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.SwapHealthPercent(25) }));
        var kernel = Arena(draft, new[] { "sunder" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp * casterPercent / 100);
            H(ctx.State, "beta").Hp = Fixed.FromInt(H(ctx.State, "beta").MaxHp * targetPercent / 100);
        });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("sunder", H(ctx.State, "beta").Id)));

        kernel.RunTicks(4);

        int max = H(kernel.State, "alpha").MaxHp;   // the same for both
        Assert.InRange(H(kernel.State, "alpha").Hp.ToDouble(), max * System.Math.Max(25, casterAfter) / 100.0 - 1, max * System.Math.Max(25, casterAfter) / 100.0 + 1);
        Assert.InRange(H(kernel.State, "beta").Hp.ToDouble(), max * System.Math.Max(25, targetAfter) / 100.0 - 1, max * System.Math.Max(25, targetAfter) / 100.0 + 1);
    }

    /// <summary>The AI sunders only when it gains by it: the target has a clearly larger share of health than the caster.</summary>
    [Theory]
    [InlineData(100, 30, false)]   // a fight against a weak enemy: no sunder at full health
    [InlineData(20, 90, true)]
    public void Ai_SwapsHealth_OnlyWhenTheTargetHasMore(int casterPercent, int targetPercent, bool casts)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("sunder", "Sunder", AbilityTargeting.Unit, TargetTeam.Any, 800, 0, new[] { 30000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.SwapHealthPercent(25) }));
        var kernel = Arena(draft, new[] { "sunder" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "beta").Position = P(5300, 5000);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp * casterPercent / 100);
            H(ctx.State, "beta").Hp = Fixed.FromInt(H(ctx.State, "beta").MaxHp * targetPercent / 100);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        foreach (string hero in new[] { "beta", "gamma", "delta" })
            settings.Uncontrolled.Add(hero);
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(300);

        Assert.Equal(casts, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "sunder"));
        if (!casts)
            Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);   // it did fight
    }

    [Fact]
    public void SwapHealth_LeavesADebuffImmuneEnemy()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("sunder", "Sunder", AbilityTargeting.Unit, TargetTeam.Any, 800, 0, new[] { 30000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.SwapHealthPercent(25) }));
        var kernel = Arena(draft, new[] { "sunder" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp / 4);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("bkb", 5000, ModifierFlags.DebuffImmune), H(ctx.State, "beta").Id);
        });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("sunder", H(ctx.State, "beta").Id)));

        kernel.RunTicks(4);

        int full = H(kernel.State, "beta").MaxHp;
        Assert.InRange(H(kernel.State, "beta").Hp.ToDouble(), full - 1, full);
        Assert.InRange(H(kernel.State, "alpha").Hp.ToDouble(), full - 1, full);   // the caster still takes the target's share
    }
}
