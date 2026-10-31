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
/// Dota 7.41f data and the official descriptions. Luminosity: after a number of attacks the next one is a critical strike healing the
/// attacker by a share of the damage and allied heroes around by a share of that (a creep target heals 60% as much: the project owner).
/// Soul Rip: damage to an enemy or healing to an ally for every visible unit near the caster, up to a number (the project owner: the
/// units around lose nothing).
/// </summary>
public class LuminosityAndRipTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        setup?.Invoke(draft);
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 100, 100, 150, 1000, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? alphaAbilities : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5150, 5000);
            H(ctx.State, "gamma").Position = P(5000, 5400);
            H(ctx.State, "delta").Position = P(9000, 9000);
        });
        return kernel;
    }

    private static readonly ModifierSpec Luminosity = new ModifierSpec("luminosity", -1, critMultiplierPercent: 200, critEveryAttacks: 3,
        critHealPercent: 50, critHealRadius: 650, critHealAlliesPercent: 50, critHealCreepPercent: 60);

    [Fact]
    public void Luminosity_EveryFourthAttackIsACrit_HealingTheAttackerAndAlliesAround()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), Luminosity, H(ctx.State, "alpha").Id);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(1000);
            H(ctx.State, "gamma").Hp = Fixed.FromInt(1000);
        });
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id)));

        kernel.RunTicks(3 + 5 * 30 - 10);   // five attacks at most

        List<DamageDealt> hits = kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id).ToList();
        Assert.True(hits.Count >= 5);
        Assert.Equal(new[] { false, false, false, true, false }, hits.Take(5).Select(h => h.IsCritical));   // the count starts again
        Assert.Equal(200, hits[3].Amount.ToDouble(), 0);
        Assert.Equal(1100, H(kernel.State, "alpha").Hp.ToDouble(), 0);   // 50% of 200
        Assert.Equal(1050, H(kernel.State, "gamma").Hp.ToDouble(), 0);   // half of that
    }

    [Fact]
    public void Luminosity_OnACreep_HealsLess()
    {
        var kernel = Arena();
        MatchUnit? creep = null;
        At(kernel, 1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), Luminosity, H(ctx.State, "alpha").Id);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(1000);
            H(ctx.State, "beta").Position = P(9000, 8000);
            creep = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, -1);
            creep.Position = P(5150, 5000);
            creep.Hp = Fixed.FromInt(100000);
            Modifiers.Apply(ctx, creep, ModifierSpec.Status("stun", -1, ModifierFlags.Stunned), creep.Id);   // it does not hit back
        });
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(creep!.Id)));

        kernel.RunTicks(3 + 5 * 30 - 10);

        DamageDealt crit = kernel.EventsOf<DamageDealt>().First(d => d.Attacker == H(kernel.State, "alpha").Id && d.IsCritical);
        Assert.Equal(1000 + crit.Amount.ToDouble() * 0.5 * 0.6, H(kernel.State, "alpha").Hp.ToDouble(), 0);
    }

    /// <summary>beta (enemy) is the target; around alpha: gamma (ally), beta, a creep of beta's; delta far away; 30 a unit, at most 2.</summary>
    [Theory]
    [InlineData("beta", 2, -60)]    // three units around, the cap is two: 60 damage
    [InlineData("gamma", 2, 60)]    // the ally healed by as much
    [InlineData("beta", 10, -90)]   // all three
    public void SoulRip_DamagesAnEnemyOrHealsAnAlly_ForEveryUnitAround(string target, int cap, int change)
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("rip", "Soul Rip", AbilityTargeting.Unit, TargetTeam.Any, 1000, 0, new[] { 1000 }, new[] { 0 },
            new[] { 1 }, new[]
            {
                EffectDefinition.IfTeam(TargetTeam.Enemy, EffectDefinition.PerUnitAround(EffectKind.Damage, DamageType.Pure, 1300, cap, 30)),
                EffectDefinition.IfTeam(TargetTeam.Ally, EffectDefinition.PerUnitAround(EffectKind.Heal, DamageType.Pure, 1300, cap, 30)),
            })), new[] { "rip" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "gamma").Hp = Fixed.FromInt(1000);
            H(ctx.State, "delta").Position = P(5200, 5200);   // near, but unseen: not counted
            Modifiers.Apply(ctx, H(ctx.State, "delta"), ModifierSpec.Status("invisible", -1, ModifierFlags.Invisible), H(ctx.State, "delta").Id);
            MatchUnit creep = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, -1);
            creep.Position = P(5300, 5300);
            Modifiers.Apply(ctx, creep, ModifierSpec.Status("stun", -1, ModifierFlags.Stunned), creep.Id);
        });
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("rip", H(ctx.State, target).Id)));

        kernel.RunTicks(6);

        Fixed before = Fixed.FromInt(target == "gamma" ? 1000 : 2000);
        Assert.Equal(change, (H(kernel.State, target).Hp - before).ToDouble(), 0);
    }
}
