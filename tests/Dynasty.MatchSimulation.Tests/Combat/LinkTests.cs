using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Links between units (Dota 7.41f data and official notes; the project owner on 2026-10-29: shares are taken from the damage after
/// reductions): Fatal Bonds share a part of the damage one bonded unit takes with the others, Disseminate spreads a part of the
/// damage its target takes to the caster's enemies around it, Static Link steals attack damage every second while the two stay
/// close, Tether passes Io's healing and mana to the ally and slows enemies on the line between them. Shared and spread damage is
/// reflected damage: never shared or spread again.
/// </summary>
public class LinkTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Heroes with 2000 health, no armor or regeneration, 50 attack damage; alpha and gamma Dawn, beta, delta and epsilon Dusk.</summary>
    private static SimulationKernel<MatchState> Arena(params (string Hero, int X, int Y)[] at)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta", "epsilon" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid"), ("epsilon", "mid") });
        At(kernel, 0, ctx =>
        {
            foreach (var (hero, x, y) in at)
                H(ctx.State, hero).Position = P(x, y);
        });
        return kernel;
    }

    private static Fixed Lost(MatchState state, string hero) => Fixed.FromInt(H(state, hero).MaxHp) - H(state, hero).Hp;

    private static void Run(SimulationKernel<MatchState> kernel, long tick, EffectDefinition effect, string target) =>
        At(kernel, tick, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { effect }, 1, H(ctx.State, target), H(ctx.State, target).Position));

    [Fact]
    public void Bonds_TakeTheNearestUnits_AndShareAPartOfTheDamageAfterReductions()
    {
        var kernel = Arena(("alpha", 3000, 5000), ("gamma", 3000, 3000), ("beta", 5000, 5000), ("delta", 5200, 5000), ("epsilon", 5600, 5000));
        var bond = new ModifierSpec("bonds", 18000, shareDamagePercent: 20);
        Run(kernel, 1, EffectDefinition.Area(700, AreaCenter.Target, TargetTeam.Enemy, 2, EffectDefinition.Modifier(bond)), "beta");   // beta and delta: the two nearest
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), new ModifierSpec("armor_cloak", 5000, magicResistBonus: 50), H(ctx.State, "beta").Id));
        At(kernel, 3, ctx => Damage.Deal(ctx, H(ctx.State, "gamma").Id, H(ctx.State, "beta"), Fixed.FromInt(200), DamageType.Magical));

        kernel.RunTicks(4);

        Assert.Equal(100, Lost(kernel.State, "beta").ToDouble(), 2);   // 200 after its 50% resistance
        Assert.Equal(20, Lost(kernel.State, "delta").ToDouble(), 2);   // 20% of the 100 it took, not shared back
        Assert.Equal(0, Lost(kernel.State, "epsilon").ToDouble(), 2);  // not bonded
    }

    [Fact]
    public void Disseminate_SpreadsAPartOfTheDamage_ToTheCastersEnemiesAroundTheTarget()
    {
        var kernel = Arena(("alpha", 3000, 5000), ("gamma", 5300, 5000), ("beta", 5000, 5000), ("delta", 5500, 5000), ("epsilon", 7000, 5000));
        var spread = new ModifierSpec("disseminate", 6000, spreadDamagePercent: 40, spreadRadius: 675);
        Run(kernel, 1, EffectDefinition.Modifier(spread), "beta");
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "gamma").Id, H(ctx.State, "beta"), Fixed.FromInt(100), DamageType.Pure));

        kernel.RunTicks(3);

        Assert.Equal(140, Lost(kernel.State, "beta").ToDouble(), 2);   // 100, and 40 again as an enemy in the radius
        Assert.Equal(40, Lost(kernel.State, "delta").ToDouble(), 2);
        Assert.Equal(0, Lost(kernel.State, "gamma").ToDouble(), 2);    // alpha's ally
        Assert.Equal(0, Lost(kernel.State, "epsilon").ToDouble(), 2);  // out of the radius
    }

    [Fact]
    public void PeriodicEffects_RunFromTheCasterEverySecond_UntilTheUnitsAreTooFarApart()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("gamma", 3000, 3000), ("beta", 5400, 5000), ("delta", 9000, 9000), ("epsilon", 9000, 9500));
        var stolen = new ModifierSpec("drained", 12000, damageBonus: -6, maxStacks: 99);
        var gained = new ModifierSpec("gained", 12000, damageBonus: 6, maxStacks: 99);
        var link = new ModifierSpec("link", 10000, periodMs: 1000, breakDistance: 800,
            periodicEffects: new[] { EffectDefinition.Modifier(stolen), EffectDefinition.OnCaster(EffectDefinition.Modifier(gained)) });
        Run(kernel, 1, EffectDefinition.Modifier(link), "beta");
        int stacksWhileNear = 0;
        At(kernel, 1 + 3 * 30 + 2, ctx => stacksWhileNear = H(ctx.State, "beta").FindModifier("drained", H(ctx.State, "alpha").Id)?.Stacks ?? 0);
        At(kernel, 1 + 3 * 30 + 3, ctx => H(ctx.State, "beta").Position = P(6000, 5000));   // 1000 away: the link breaks

        kernel.RunTicks(1 + 6 * 30);

        Assert.InRange(stacksWhileNear, 3, 4);
        Assert.Equal(stacksWhileNear, H(kernel.State, "alpha").FindModifier("gained", H(kernel.State, "alpha").Id)?.Stacks ?? 0);
        Assert.DoesNotContain(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "link");
        Assert.Equal(stacksWhileNear, H(kernel.State, "beta").FindModifier("drained", H(kernel.State, "alpha").Id)?.Stacks ?? 0);   // no more after the break
        Assert.Equal(50 - 6 * stacksWhileNear, H(kernel.State, "beta").AttackDamageMin);
    }

    [Fact]
    public void Tether_PassesTheCastersHealingAndMana_ToTheTetheredAlly_Amplified()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("gamma", 5300, 5000), ("beta", 9000, 9000), ("delta", 9000, 9500), ("epsilon", 9000, 9800));
        Run(kernel, 1, EffectDefinition.Modifier(new ModifierSpec("tether", 20000, healSharePercent: 150, manaSharePercent: 50)), "gamma");
        At(kernel, 2, ctx =>
        {
            H(ctx.State, "gamma").Hp = Fixed.FromInt(1000);
            H(ctx.State, "gamma").Hero!.Mana = Fixed.FromInt(100);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(1000);
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(100);
        });
        At(kernel, 3, ctx =>
        {
            Damage.Heal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "alpha"), Fixed.FromInt(100));
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.RestoreMana(100) }, 1, H(ctx.State, "alpha"), P(0, 0));
        });

        kernel.RunTicks(4);

        Assert.Equal(1150, H(kernel.State, "gamma").Hp.ToDouble(), 2);
        Assert.Equal(150, H(kernel.State, "gamma").Hero!.Mana.ToDouble(), 2);
    }

    [Fact]
    public void LineArea_HitsTheUnitsNearTheLineBetweenTheCasterAndTheTarget()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("gamma", 5800, 5000), ("beta", 5400, 5050), ("delta", 5400, 5400), ("epsilon", 6200, 5000));
        Run(kernel, 1, EffectDefinition.LineArea(100, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 10)), "gamma");

        kernel.RunTicks(2);

        Assert.Equal(10, Lost(kernel.State, "beta").ToDouble(), 2);     // 50 off the line
        Assert.Equal(0, Lost(kernel.State, "delta").ToDouble(), 2);     // 400 off it
        Assert.Equal(0, Lost(kernel.State, "epsilon").ToDouble(), 2);   // beyond the far end
    }
}
