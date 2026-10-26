using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
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

    /// <summary>Cleave: a melee attacker's hit also deals a percent of its damage to other enemies near the target.</summary>
    [Fact]
    public void Cleave_SplashesPartOfTheAttackOntoEnemiesAroundTheTarget()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Add(RulesetDraft.Hero("delta"));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid"), ("delta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5200, 5000);
            H(ctx.State, "gamma").Position = P(5300, 5100);    // near beta: cleaved
            H(ctx.State, "delta").Position = P(5200, 6500);    // far: not
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("cleave", -1, cleavePercent: 50, cleaveRadius: 300), EntityId.None);
        }));
        kernel.AddSystem(new LambdaSystem(VisionRefresh, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id))));
        kernel.StepUntil(() => HitsOnBeta(kernel).Any(), 30 * 5);

        EntityId alpha = H(kernel.State, "alpha").Id;
        Fixed main = HitsOnBeta(kernel).First().Amount;
        DamageDealt cleaved = Assert.Single(kernel.EventsOf<DamageDealt>(), d => d.Attacker == alpha && d.Target == H(kernel.State, "gamma").Id);
        Assert.InRange(cleaved.Amount.ToDouble(), main.ToDouble() * 0.5 - 1, main.ToDouble() * 0.5 + 1);
        Assert.DoesNotContain(kernel.EventsOf<DamageDealt>(), d => d.Attacker == alpha && d.Target == H(kernel.State, "delta").Id);
    }

    /// <summary>A barrier absorbs damage of any type until it is used up; the rest goes through.</summary>
    [Fact]
    public void Barrier_AbsorbsDamageUntilUsedUp()
    {
        var kernel = Duel(onBeta: new ModifierSpec("shield", 5000, barrier: 150), attack: false);
        kernel.AddSystem(new LambdaSystem(1, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(100), DamageType.Pure)));
        kernel.AddSystem(new LambdaSystem(2, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(100), DamageType.Pure)));

        kernel.RunTicks(3);

        Assert.Equal(new[] { Fixed.Zero, Fixed.FromInt(50) }, HitsOnBeta(kernel).Select(d => d.Amount));
        Assert.DoesNotContain(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "shield");   // used up
    }

    /// <summary>Shallow Grave: while it lasts, damage cannot bring the hero below 1 health.</summary>
    [Fact]
    public void CannotDie_KeepsAtLeastOneHealth()
    {
        var kernel = Duel(onBeta: ModifierSpec.Status("grave", 5000, ModifierFlags.CannotDie), attack: false);
        kernel.AddSystem(new LambdaSystem(1, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(99999), DamageType.Pure)));

        kernel.RunTicks(3);

        MatchUnit beta = H(kernel.State, "beta");
        Assert.True(beta.IsAlive);
        Assert.InRange(beta.Hp.ToDouble(), 1, 1.5);   // left at 1, then a tick or two of regeneration
    }

    /// <summary>"Next attack" buffs (Enchant Totem, Tidebringer): the modifier boosts the next landing attack and is then removed.</summary>
    [Fact]
    public void ConsumedOnHit_BoostsOnlyTheNextAttack()
    {
        var kernel = Run(Duel(onAlpha: new ModifierSpec("totem", 14000, attackDamagePercent: 300, consumedOnHit: true)));

        List<DamageDealt> hits = HitsOnBeta(kernel);
        Assert.True(hits.Count >= 2);
        Assert.InRange(hits[0].Amount.ToDouble(), hits[1].Amount.ToDouble() * 4 - 4, hits[1].Amount.ToDouble() * 4 + 4);
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "totem");
    }

    private static SimulationKernel<MatchState> Cast(Action<SimulationContext<MatchState>> setup, params EffectDefinition[] effects)
    {
        var kernel = Duel(attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            setup(ctx);
            Effects.Run(ctx, H(ctx.State, "alpha"), effects, 1, H(ctx.State, "beta"), H(ctx.State, "beta").Position);
        }));
        kernel.RunTicks(3);
        return kernel;
    }

    /// <summary>Mana Break: burns the target's mana and deals a percent of the mana burnt as damage; it cannot burn more than the target has.</summary>
    [Fact]
    public void ManaBurn_BurnsManaAndDealsAPercentOfIt()
    {
        var kernel = Cast(ctx => H(ctx.State, "beta").Hero!.Mana = Fixed.FromInt(30), EffectDefinition.ManaBurn(50, DamageType.Pure, 40));

        Assert.Equal(Fixed.Zero, H(kernel.State, "beta").Hero!.Mana);
        Assert.Equal(Fixed.FromInt(15), HitsOnBeta(kernel).Single().Amount);   // 30 burnt (not 40), half of it as damage
    }

    [Theory]
    [InlineData(DamageScaling.TargetMaxHealthPercent, 1000, 60)]          // 10% of 600 (amounts in hundredths of a percent)
    [InlineData(DamageScaling.TargetMissingHealthPercent, 7000, 210)]     // 70% of 300 missing
    [InlineData(DamageScaling.TargetMissingManaPercent, 10000, 200)]      // 100% of 200 missing mana
    [InlineData(DamageScaling.TargetCurrentHealthPercent, 345, 10.35)]    // 3.45% of 300 left
    [InlineData(DamageScaling.TargetMaxManaPercent, 2500, 0)]             // 25% of max mana, checked below against the hero's pool
    public void ScaledDamage_FollowsTheTargetsHealthOrMana(DamageScaling scaling, int percent, double expected)
    {
        var kernel = Cast(ctx =>
        {
            MatchUnit beta = H(ctx.State, "beta");
            beta.Hp = Fixed.FromInt(beta.MaxHp - 300);
            beta.Hero!.Mana = Fixed.FromInt(beta.Hero.MaxMana - 200);
        }, EffectDefinition.ScaledDamage(DamageType.Pure, scaling, percent));

        Assert.Equal(600, H(kernel.State, "beta").MaxHp);
        if (scaling == DamageScaling.TargetMaxManaPercent)
            expected = H(kernel.State, "beta").Hero!.MaxMana * 0.25;
        Assert.True(expected > 0);
        Assert.InRange(HitsOnBeta(kernel).Single().Amount.ToDouble(), expected - 0.01, expected + 0.01);
    }

    /// <summary>Damage over time as a percent of the target's max health each second (Heartstopper Aura, Infernal Blade).</summary>
    [Fact]
    public void DamageOverTime_CanBeAPercentOfMaxHealth()
    {
        var kernel = Duel(onBeta: new ModifierSpec("heartstopper", 5000, damagePerSecondMaxHealthPercent: Fixed.FromInt(2), damageType: DamageType.Pure), attack: false);

        kernel.RunTicks(31);

        Assert.InRange(kernel.EventsOf<DamageDealt>().Where(d => d.Target == H(kernel.State, "beta").Id).Sum(d => d.Amount.ToDouble()), 11.9, 12.1);   // 2% of 600 after one second
    }

    /// <summary>An aura's damage over time (Rot, Heartstopper) keeps ticking: the aura's regular refresh does not restart the period.</summary>
    [Fact]
    public void AuraDamageOverTime_KeepsTickingWhileRefreshed()
    {
        var aura = new ModifierSpec("heartstopper", -1, aura: new AuraSpec(900, TargetTeam.Enemy, heroesOnly: false,
            new ModifierSpec("heartstopper.debuff", 1000, damagePerSecondMaxHealthPercent: Fixed.FromInt(2), damageType: DamageType.Pure)));
        var kernel = Duel(onAlpha: aura, attack: false);

        kernel.RunTicks(30 * 5 + 1);

        double taken = kernel.EventsOf<DamageDealt>().Where(d => d.Target == H(kernel.State, "beta").Id).Sum(d => d.Amount.ToDouble());
        Assert.InRange(taken, 12 * 4 - 0.1, 12 * 5 + 0.1);   // 2% of 600 a second for about five seconds
    }
}

