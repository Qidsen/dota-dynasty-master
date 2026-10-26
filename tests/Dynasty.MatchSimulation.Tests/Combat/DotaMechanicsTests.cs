using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Mechanics Dota's abilities and items need (docs/DOTA_PACK.md, wave 2): damage taken and attack damage
/// changed by percent, evasion, effects on attack hits (bash, bonus magic damage) with a chance, debuff
/// immunity, auras, and innate abilities learned for free at level one.
/// </summary>
public class DotaMechanicsTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private const int VisionRefresh = Dynasty.MatchSimulation.Vision.VisionSystem.IntervalTicks + 1;

    /// <summary>Alpha (Dawn) and beta (Dusk) face each other 200 apart; modifiers are applied at tick 0; alpha attacks beta once seen.</summary>
    private static SimulationKernel<MatchState> Duel(ModifierSpec? onAlpha = null, ModifierSpec? onBeta = null, bool attack = true, RulesetDraft? draft = null)
    {
        draft ??= new RulesetDraft().WithoutWaves();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5200, 5000);
            H(ctx.State, "gamma").Position = P(4900, 5000);
            if (onAlpha != null)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), onAlpha, EntityId.None);
            if (onBeta != null)
                Modifiers.Apply(ctx, H(ctx.State, "beta"), onBeta, EntityId.None);
        }));
        if (attack)
            kernel.AddSystem(new LambdaSystem(VisionRefresh, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id))));
        return kernel;
    }

    private static List<DamageDealt> HitsOnBeta(SimulationKernel<MatchState> kernel) =>
        kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id && d.Target == H(kernel.State, "beta").Id).ToList();

    [Theory]
    [InlineData(20, 120)]
    [InlineData(-50, 50)]
    public void IncomingDamagePercent_AmplifiesOrReducesDamageTaken(int percent, int taken)
    {
        var kernel = Duel(onBeta: new ModifierSpec("amp", -1, incomingDamagePercent: percent), attack: false);
        kernel.AddSystem(new LambdaSystem(1, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(100), DamageType.Pure)));

        kernel.RunTicks(2);

        Assert.Equal(Fixed.FromInt(taken), HitsOnBeta(kernel).Single().Amount);
    }

    [Fact]
    public void AttackDamagePercent_ScalesTheAttacksDamage()
    {
        Fixed First(ModifierSpec? spec) => HitsOnBeta(Run(Duel(onAlpha: spec))).First().Amount;

        Fixed plain = First(null);
        Fixed reduced = First(new ModifierSpec("weak", -1, attackDamagePercent: -25));

        Assert.InRange(reduced.ToDouble(), plain.ToDouble() * 0.75 - 1, plain.ToDouble() * 0.75 + 1);
    }

    private static SimulationKernel<MatchState> Run(SimulationKernel<MatchState> kernel)
    {
        kernel.RunTicks(30 * 6);
        return kernel;
    }

    [Fact]
    public void Evasion_MakesAttacksMiss()
    {
        var kernel = Run(Duel(onBeta: new ModifierSpec("evade", -1, evasionPercent: 100)));

        Assert.True(kernel.EventsOf<AttackStarted>().Count(a => a.Attacker == H(kernel.State, "alpha").Id) >= 2);
        Assert.Empty(HitsOnBeta(kernel));
    }

    [Fact]
    public void OnHitEffects_RunOnTheAttacksTarget()
    {
        var bash = new ModifierSpec("bash", -1, onHit: new[]
        {
            EffectDefinition.Damage(DamageType.Magical, 40),
            EffectDefinition.Modifier(ModifierSpec.Status("bashed", 1000, ModifierFlags.Stunned)),
        });
        var kernel = Duel(onAlpha: bash);
        kernel.StepUntil(() => HitsOnBeta(kernel).Any(), 30 * 5);
        kernel.Step();

        Assert.Contains(HitsOnBeta(kernel), d => d.DamageType == DamageType.Magical);
        Assert.True(H(kernel.State, "beta").IsStunned);
    }

    [Fact]
    public void OnHitChance_ZeroNeverProcs()
    {
        var kernel = Run(Duel(onAlpha: new ModifierSpec("bash", -1, onHit: new[] { EffectDefinition.Damage(DamageType.Magical, 40) }, onHitChancePercent: 0)));

        Assert.NotEmpty(HitsOnBeta(kernel));
        Assert.DoesNotContain(HitsOnBeta(kernel), d => d.DamageType == DamageType.Magical);
    }

    [Fact]
    public void DebuffImmunity_BlocksEnemyDebuffs_ButNotDamageOrAlliedBuffs()
    {
        var kernel = Duel(onBeta: ModifierSpec.Status("bkb", -1, ModifierFlags.DebuffImmune), attack: false);
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("stun", 2000, ModifierFlags.Stunned), H(ctx.State, "alpha").Id);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), new ModifierSpec("haste", 2000, moveSpeedPercent: 20), H(ctx.State, "beta").Id);
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(100), DamageType.Pure);
        }));

        kernel.RunTicks(3);

        MatchUnit beta = H(kernel.State, "beta");
        Assert.False(beta.IsStunned);
        Assert.Contains(beta.Modifiers, m => m.Spec.Key == "haste");
        Assert.NotEmpty(HitsOnBeta(kernel));
    }

    [Fact]
    public void Aura_GivesItsModifierToUnitsInRange_AndItFadesOutside()
    {
        var aura = new ModifierSpec("aura", -1, aura: new AuraSpec(900, TargetTeam.Ally, heroesOnly: true, new ModifierSpec("aura.armor", 1000, armorBonus: Fixed.FromInt(5))));
        var kernel = Duel(onAlpha: aura, attack: false);
        kernel.AddSystem(new LambdaSystem(31, ctx => H(ctx.State, "gamma").Position = P(1000, 1000)));

        kernel.RunTicks(30);
        Assert.Equal(H(kernel.State, "gamma").Stats.Armor + Fixed.FromInt(5), H(kernel.State, "gamma").Armor);
        Assert.Equal(H(kernel.State, "beta").Stats.Armor, H(kernel.State, "beta").Armor);   // an enemy: not affected

        kernel.RunTicks(30 * 3);   // gamma moved far away at tick 31
        Assert.Equal(H(kernel.State, "gamma").Stats.Armor, H(kernel.State, "gamma").Armor);
    }

    [Fact]
    public void InnateAbility_IsLearnedAtLevelOne_WithoutASkillPoint()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("dragon_blood", "Dragon Blood", AbilityTargeting.Passive, TargetTeam.Ally, 0, 0,
            new[] { 0 }, Array.Empty<int>(), new[] { 1 }, Array.Empty<EffectDefinition>(),
            new[] { new ModifierSpec("dragon_blood", -1, armorBonus: Fixed.FromInt(2)) }, innate: true));
        HeroDefinition basic = draft.Heroes[0];
        draft.Heroes[0] = new HeroDefinition(basic.Id, basic.Name, basic.Roles, basic.BaseStats, 80, Fixed.FromRatio(3, 10), 3, 300, 40, Fixed.FromInt(2), Fixed.One,
            abilityIds: new[] { "dragon_blood" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());

        kernel.Step();

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Equal(1, alpha.Hero!.AbilityLevel("dragon_blood"));
        Assert.Equal(alpha.Stats.Armor + Fixed.FromInt(2), alpha.Armor);
        Assert.Equal(1, alpha.Hero.UnspentSkillPoints);   // the innate does not use the level-one point
    }
}
