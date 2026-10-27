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

    /// <summary>A fractional percent of damage taken (Wild Axes with its talent: +6.5%) is kept in hundredths.</summary>
    [Fact]
    public void IncomingDamagePercent_KeepsFractions()
    {
        var kernel = Duel(onBeta: new ModifierSpec("axes", -1, incomingDamageHundredths: 650), attack: false);
        kernel.AddSystem(new LambdaSystem(1, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(200), DamageType.Pure)));

        kernel.RunTicks(2);

        Assert.InRange(HitsOnBeta(kernel).Single().Amount.ToDouble(), 212.99, 213.01);   // 200 × 1.065
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

    /// <summary>Restores the target's mana, not above its max (Chakra Magic).</summary>
    [Fact]
    public void RestoreMana_GivesManaUpToTheMax()
    {
        var kernel = Cast(ctx => H(ctx.State, "beta").Hero!.Mana = Fixed.FromInt(10), EffectDefinition.RestoreMana(40));
        Assert.Equal(Fixed.FromInt(50), H(kernel.State, "beta").Hero!.Mana);

        kernel = Cast(ctx => H(ctx.State, "beta").Hero!.Mana = Fixed.FromInt(H(ctx.State, "beta").Hero!.MaxMana - 5), EffectDefinition.RestoreMana(40));
        Assert.Equal(Fixed.FromInt(H(kernel.State, "beta").Hero!.MaxMana), H(kernel.State, "beta").Hero!.Mana);
    }

    /// <summary>"After 2 attacks, the next attack bashes" (Bash of the Deep, Luminosity): on-hit effects run on every third attack only.</summary>
    [Fact]
    public void OnHitAfterAttacks_ProcsOnlyOnTheAttackAfterThatMany()
    {
        var bash = new ModifierSpec("bash", -1, onHit: new[] { EffectDefinition.Damage(DamageType.Magical, 40) }, onHitAfterAttacks: 2);
        var kernel = Duel(onAlpha: bash);
        kernel.RunTicks(30 * 12);

        List<DamageDealt> hits = HitsOnBeta(kernel);
        List<bool> magical = hits.Select(d => d.DamageType == DamageType.Magical).ToList();
        int physical = magical.Count(m => !m);
        Assert.True(physical >= 6);
        Assert.Equal(physical / 3, magical.Count(m => m));
        Assert.False(magical[1]);   // the first two attacks are plain, the third one procs
        Assert.True(magical[3]);
    }

    /// <summary>Counter Helix: every Nth attack the bearer takes runs effects with the bearer as the caster and the attacker as the target.</summary>
    [Fact]
    public void WhenAttackedEvery_RunsEffectsOnEveryNthAttackTaken()
    {
        var helix = new ModifierSpec("helix", -1, whenAttacked: new[]
        {
            EffectDefinition.Area(275, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 100)),
        }, whenAttackedEvery: 3);
        var kernel = Duel(onBeta: helix);
        kernel.RunTicks(30 * 12);

        int taken = HitsOnBeta(kernel).Count;
        int helixes = kernel.EventsOf<DamageDealt>().Count(d => d.Attacker == H(kernel.State, "beta").Id && d.DamageType == DamageType.Pure);
        Assert.True(taken >= 6);
        Assert.Equal(taken / 3, helixes);
    }

    /// <summary>Stacking modifiers (Fury Swipes, Essence Shift, Reactive Armor): each application adds a stack up to the max, and every stack adds the modifier's stats.</summary>
    [Fact]
    public void StackingModifier_AddsAStackPerApplication_UpToTheMax()
    {
        var shred = new ModifierSpec("shred", 5000, armorBonus: Fixed.FromInt(-2), maxStacks: 3);
        Fixed before = Fixed.Zero;
        var kernel = Cast(ctx => before = H(ctx.State, "beta").Armor,
            EffectDefinition.Modifier(shred), EffectDefinition.Modifier(shred), EffectDefinition.Modifier(shred), EffectDefinition.Modifier(shred));

        Assert.Equal(before - Fixed.FromInt(6), H(kernel.State, "beta").Armor);
    }

    /// <summary>Fury Swipes: damage for each stack of a modifier the caster put on the target.</summary>
    [Fact]
    public void DamagePerStack_CountsTheCastersStacksOnTheTarget()
    {
        var swipes = new ModifierSpec("swipes", 5000, maxStacks: 99);
        EffectDefinition perStack = EffectDefinition.DamagePerStack(DamageType.Pure, "swipes", 10);
        var kernel = Cast(_ => { },
            EffectDefinition.Modifier(swipes), perStack, EffectDefinition.Modifier(swipes), perStack, EffectDefinition.Modifier(swipes), perStack);

        Assert.Equal(new[] { 10, 20, 30 }, HitsOnBeta(kernel).Select(d => d.Amount.ToIntFloor()));
    }

    /// <summary>Effects on the caster from inside an attack or a targeted ability (Essence Shift's stolen agility, a self buff after a nuke).</summary>
    [Fact]
    public void CasterEffect_RunsOnTheCaster()
    {
        Fixed alphaBefore = Fixed.Zero, betaBefore = Fixed.Zero;
        var kernel = Cast(ctx => { alphaBefore = H(ctx.State, "alpha").Armor; betaBefore = H(ctx.State, "beta").Armor; },
            EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("guard", 5000, armorBonus: Fixed.FromInt(5)))));

        Assert.Equal(alphaBefore + Fixed.FromInt(5), H(kernel.State, "alpha").Armor);
        Assert.Equal(betaBefore, H(kernel.State, "beta").Armor);
    }

    /// <summary>An ability that performs an attack (Moment of Courage, Stifling Dagger): a percent of the attack's damage, with its on-hit effects.</summary>
    [Fact]
    public void AttackEffect_HitsWithAPercentOfTheAttack_AndItsOnHitEffects()
    {
        var kernel = Duel(onAlpha: new ModifierSpec("burn", -1, onHit: new[] { EffectDefinition.Damage(DamageType.Pure, 40) }), attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Attack(200) }, 1, H(ctx.State, "beta"), H(ctx.State, "beta").Position)));
        kernel.RunTicks(3);

        MatchUnit alpha = H(kernel.State, "alpha"), beta = H(kernel.State, "beta");
        double multiplier = DamageMath.ArmorMultiplier(beta.Armor, kernel.State.Ruleset.Combat.ArmorFactor).ToDouble();
        DamageDealt hit = HitsOnBeta(kernel).Single(d => d.DamageType == DamageType.Physical);
        Assert.InRange(hit.Amount.ToDouble(), alpha.AttackDamageMin * 2 * multiplier - 0.01, alpha.AttackDamageMax * 2 * multiplier + 0.01);
        Assert.Equal(40, HitsOnBeta(kernel).Single(d => d.DamageType == DamageType.Pure).Amount.ToIntFloor());
    }

    /// <summary>Damage as a percent of the caster's attack damage (Echo Slash) or of an attribute (Glaives of Wisdom, Ion Shell).</summary>
    [Theory]
    [InlineData(DamageScaling.CasterAttackDamagePercent, 15000)]
    [InlineData(DamageScaling.CasterStrengthPercent, 5000)]
    [InlineData(DamageScaling.CasterIntelligencePercent, 20000)]
    public void ScaledDamage_FollowsTheCastersAttackOrAttributes(DamageScaling scaling, int percent)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes[0] = new HeroDefinition("alpha", "alpha", new[] { "carry" }, HeroStats(), hpPerLevel: 80, armorPerLevel: Fixed.FromRatio(3, 10),
            damagePerLevel: 3, baseMana: 300, manaPerLevel: 40, hpRegenPerSecond: Fixed.FromInt(2), manaRegenPerSecond: Fixed.One,
            attributes: new HeroAttributes(PrimaryAttribute.Strength, strength: 20, agility: 10, intelligence: 30, Fixed.Zero, Fixed.Zero, Fixed.Zero));
        var kernel = Duel(attack: false, draft: draft);
        kernel.AddSystem(new LambdaSystem(2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.ScaledDamage(DamageType.Pure, scaling, percent) },
            1, H(ctx.State, "beta"), H(ctx.State, "beta").Position)));
        kernel.RunTicks(3);

        MatchUnit alpha = H(kernel.State, "alpha");
        double expected = scaling == DamageScaling.CasterAttackDamagePercent ? (alpha.AttackDamageMin + alpha.AttackDamageMax) / 2.0 * 1.5
            : scaling == DamageScaling.CasterStrengthPercent ? 20 * 0.5 : 30 * 2.0;
        Assert.InRange(HitsOnBeta(kernel).Single().Amount.ToDouble(), expected - 0.01, expected + 0.01);
    }

    /// <summary>Proc cooldown (Greater Bash, Mortal Strike): after an on-hit proc, no other proc until the cooldown is over.</summary>
    [Fact]
    public void ProcCooldown_StopsOnHitEffectsUntilItIsOver()
    {
        var bash = new ModifierSpec("bash", -1, onHit: new[] { EffectDefinition.Damage(DamageType.Magical, 40) }, procCooldownMs: 60000);
        var kernel = Run(Duel(onAlpha: bash));

        List<DamageDealt> hits = HitsOnBeta(kernel);
        Assert.True(hits.Count(d => d.DamageType == DamageType.Physical) >= 3);
        Assert.Single(hits, d => d.DamageType == DamageType.Magical);
    }

    /// <summary>Effects on being attacked with a chance for each attack taken (Storm Surge).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void WhenAttackedChance_RollsForEachAttackTaken(int chance)
    {
        var surge = new ModifierSpec("surge", -1, whenAttacked: new[] { EffectDefinition.Damage(DamageType.Pure, 30) }, whenAttackedChancePercent: chance);
        var kernel = Run(Duel(onBeta: surge));

        int taken = HitsOnBeta(kernel).Count;
        int procs = kernel.EventsOf<DamageDealt>().Count(d => d.Attacker == H(kernel.State, "beta").Id && d.DamageType == DamageType.Pure);
        Assert.True(taken >= 3);
        Assert.Equal(chance == 0 ? 0 : taken, procs);
    }

    /// <summary>A ground zone (Macropyre, Midnight Pulse, Chemical Rage's acid): every period it runs its effects on the units inside, until it expires.</summary>
    [Fact]
    public void Zone_HitsUnitsInsideEveryPeriod_UntilItExpires()
    {
        var kernel = Duel(attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"),
            new[] { EffectDefinition.Zone(150, AreaCenter.Point, TargetTeam.Any, 3000, 1000, EffectDefinition.Damage(DamageType.Pure, 10)) },
            1, H(ctx.State, "alpha"), H(ctx.State, "beta").Position)));
        kernel.RunTicks(30 * 6);

        Assert.Equal(new[] { 10, 10, 10 }, HitsOnBeta(kernel).Select(d => d.Amount.ToIntFloor()));
        Assert.DoesNotContain(kernel.EventsOf<DamageDealt>(), d => d.Target == H(kernel.State, "alpha").Id);   // 200 away: outside the zone
        Assert.Empty(kernel.State.Zones);
    }

    [Fact]
    public void Zone_StopsHittingAUnitThatLeftIt()
    {
        var kernel = Duel(attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"),
            new[] { EffectDefinition.Zone(150, AreaCenter.Point, TargetTeam.Enemy, 5000, 1000, EffectDefinition.Damage(DamageType.Pure, 10)) },
            1, H(ctx.State, "alpha"), H(ctx.State, "beta").Position)));
        kernel.AddSystem(new LambdaSystem(2 + 45, ctx => H(ctx.State, "beta").Position = P(5200, 5600)));
        kernel.RunTicks(30 * 7);

        Assert.Single(HitsOnBeta(kernel));
    }

    /// <summary>Pulls (Vacuum, Black Hole, Walrus Kick's opposite): the unit moves toward the caster or the point, without passing it.</summary>
    [Theory]
    [InlineData(100, 5100)]
    [InlineData(500, 5048)]   // stops next to alpha: 24 + 24 bounding radii
    public void Pull_MovesTheTargetTowardTheCaster(int distance, int x)
    {
        var kernel = Cast(_ => { }, EffectDefinition.Pull(AreaCenter.Caster, distance));

        Assert.Equal(P(x, 5000), H(kernel.State, "beta").Position);
    }

    [Fact]
    public void Pull_TowardThePoint_StopsAtIt()
    {
        var kernel = Duel(attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"),
            new[] { EffectDefinition.Area(400, AreaCenter.Point, TargetTeam.Enemy, EffectDefinition.Pull(AreaCenter.Point, 300)) },
            1, H(ctx.State, "alpha"), P(5300, 5000))));
        kernel.RunTicks(3);

        Assert.Equal(P(5300, 5000), H(kernel.State, "beta").Position);
    }

    /// <summary>An attack made by an on-hit effect (Time Lock, Geminate Attack) does not trigger that same effect again: no chain of procs.</summary>
    [Fact]
    public void OnHitAttack_DoesNotTriggerItsOwnModifierAgain()
    {
        var timeLock = new ModifierSpec("lock", -1, onHit: new[] { EffectDefinition.Damage(DamageType.Pure, 10), EffectDefinition.Attack(100) });
        var kernel = Duel(onAlpha: timeLock, attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx => AttackSystem.Strike(ctx, H(ctx.State, "alpha"), H(ctx.State, "beta"), 100)));
        kernel.RunTicks(3);

        Assert.Equal(2, HitsOnBeta(kernel).Count(d => d.DamageType == DamageType.Physical));   // the attack and the one it made
        Assert.Single(HitsOnBeta(kernel), d => d.DamageType == DamageType.Pure);
    }

    [Fact]
    public void Zone_CanBeLimitedToHeroes()
    {
        var kernel = Duel(attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"),
            new[] { EffectDefinition.Zone(150, AreaCenter.Point, TargetTeam.Enemy, 3000, 1000, heroesOnly: true, EffectDefinition.Damage(DamageType.Pure, 10)) },
            1, H(ctx.State, "alpha"), H(ctx.State, "beta").Position)));
        kernel.RunTicks(40);

        Assert.True(kernel.State.Zones.Single().Definition.HeroesOnly);
        Assert.Single(HitsOnBeta(kernel));
    }

    /// <summary>Illusions (Juxtapose, Mirror Image, Conjure Image): copies of the hero with its current stats that deal and take a changed share of damage, gone after the duration.</summary>
    [Fact]
    public void Illusion_CopiesTheHerosStats_AndCarriesItsDamageModifier()
    {
        var weak = new ModifierSpec("image", -1, incomingDamagePercent: 200, attackDamagePercent: -70);
        var kernel = Duel(onAlpha: new ModifierSpec("boots", -1, moveSpeedBonus: 45, armorBonus: Fixed.FromInt(3)), attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(400);
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Illusion(4000, new[] { weak }, 2) }, 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position);
        }));
        kernel.RunTicks(3);

        MatchUnit alpha = H(kernel.State, "alpha");
        List<MatchUnit> images = kernel.State.Units.Where(u => u.IllusionOf == alpha.Id).ToList();
        Assert.Equal(2, images.Count);
        foreach (MatchUnit image in images)
        {
            Assert.Equal((alpha.Team, alpha.Id, alpha.MaxHp, alpha.Armor, alpha.MoveSpeed), (image.Team, image.Owner, image.MaxHp, image.Armor, image.MoveSpeed));
            Assert.Equal((alpha.AttackDamageMin, alpha.AttackRange, Fixed.FromInt(400)), (image.AttackDamageMin, image.AttackRange, image.Hp));
            Assert.Equal((200, -70), (image.IncomingDamagePercent, image.AttackDamagePercent));
        }

        kernel.RunTicks(30 * 4);
        Assert.DoesNotContain(kernel.State.Units, u => u.IllusionOf == alpha.Id);
    }

    /// <summary>A zone without a period (Ice Vortex, Chronosphere): like an aura on the ground, it keeps refreshing its modifiers on the units inside; they fade soon after leaving.</summary>
    [Fact]
    public void ContinuousZone_KeepsItsModifierOnUnitsInside()
    {
        var vortex = new ModifierSpec("vortex", 1000, moveSpeedPercent: -20);
        var kernel = Duel(attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"),
            new[] { EffectDefinition.Zone(150, AreaCenter.Point, TargetTeam.Enemy, 6000, 0, EffectDefinition.Modifier(vortex)) },
            1, H(ctx.State, "alpha"), H(ctx.State, "beta").Position)));
        bool slowedAtOnce = false, slowedLater = false, slowedAfterLeaving = true;
        kernel.AddSystem(new LambdaSystem(2 + AuraSystem.IntervalTicks + 1, ctx => slowedAtOnce = H(ctx.State, "beta").MoveSpeed < 300));
        kernel.AddSystem(new LambdaSystem(30 * 3, ctx => slowedLater = H(ctx.State, "beta").MoveSpeed < 300));
        kernel.AddSystem(new LambdaSystem(30 * 3 + 1, ctx => H(ctx.State, "beta").Position = P(5200, 5800)));
        kernel.AddSystem(new LambdaSystem(30 * 3 + 40, ctx => slowedAfterLeaving = H(ctx.State, "beta").MoveSpeed < 300));
        kernel.RunTicks(30 * 3 + 41);

        Assert.Equal((true, true, false), (slowedAtOnce, slowedLater, slowedAfterLeaving));
        int refreshes = kernel.EventsOf<ModifierApplied>().Count(m => m.Key == "vortex");
        Assert.InRange(refreshes, (30 * 3 - 2) / AuraSystem.IntervalTicks, (30 * 3 - 2) / AuraSystem.IntervalTicks + 1);   // with each aura refresh, not every tick
    }

    /// <summary>Juxtapose: an attack that creates an illusion (a new unit) in the middle of the attack pass; the new unit attacks from the next tick on.</summary>
    [Fact]
    public void OnHitIllusion_IsCreatedDuringTheAttackPass()
    {
        var image = new ModifierSpec("image", -1, incomingDamagePercent: 300, attackDamagePercent: -80);
        var juxtapose = new ModifierSpec("juxtapose", -1, onHit: new[] { EffectDefinition.OnCaster(EffectDefinition.Illusion(4000, new[] { image }, 1)) });
        var kernel = Run(Duel(onAlpha: juxtapose));

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.True(kernel.State.Units.Count(u => u.IllusionOf == alpha.Id) >= 2);
        Assert.Contains(kernel.EventsOf<DamageDealt>(), d => kernel.State.Find(d.Attacker)?.IllusionOf == alpha.Id);
    }

    /// <summary>Split Shot, Flak Cannon: each attack also hits up to N other enemies near the attacker, with a percent of its damage and its on-hit effects.</summary>
    [Fact]
    public void ExtraTargets_SplitTheAttackOntoNearbyEnemies()
    {
        var split = new ModifierSpec("split", -1, extraTargets: 3, extraTargetRadius: 500, extraTargetDamagePercent: 50,
            onHit: new[] { EffectDefinition.Damage(DamageType.Pure, 7) });
        var kernel = Duel(onAlpha: split);
        var bugs = new List<MatchUnit>();
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            CreepType melee = ctx.State.Ruleset.CreepTypes.First();
            foreach (int y in new[] { 5100, 4900, 4000 })   // two in reach, one far away
            {
                MatchUnit bug = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), melee, ctx.Tick.Value, ctx.Tick.Value + 100000);
                bug.Position = P(5050, y);
                bug.StartPosition = bug.Position;
                bugs.Add(bug);
            }
        }));
        kernel.AddSystem(new LambdaSystem(2, ctx => { foreach (MatchUnit bug in bugs) Modifiers.Apply(ctx, bug, ModifierSpec.Status("hold", 100000, ModifierFlags.Stunned | ModifierFlags.Disarmed), EntityId.None); }));
        kernel.RunTicks(30 * 4);

        List<DamageDealt> onBeta = HitsOnBeta(kernel).Where(d => d.DamageType == DamageType.Physical).ToList();
        Assert.NotEmpty(onBeta);
        for (int i = 0; i < 2; i++)
        {
            List<DamageDealt> onBug = kernel.EventsOf<DamageDealt>().Where(d => d.Target == bugs[i].Id).ToList();
            Assert.Equal(onBeta.Count, onBug.Count(d => d.DamageType == DamageType.Physical));
            Assert.Equal(onBeta.Count, onBug.Count(d => d.DamageType == DamageType.Pure));
        }

        Assert.DoesNotContain(kernel.EventsOf<DamageDealt>(), d => d.Target == bugs[2].Id);
        double main = onBeta.Average(d => d.Amount.ToDouble()), side = kernel.EventsOf<DamageDealt>().Where(d => d.Target == bugs[0].Id && d.DamageType == DamageType.Physical).Average(d => d.Amount.ToDouble());
        Assert.True(side < main * 0.75);   // half the attack (armor differs a little between the hero and the creep)
    }

    /// <summary>Overpower, Lil' Shredder: a buff for the next N landing attacks, then it is gone.</summary>
    [Fact]
    public void AttackCharges_EndTheBuffAfterThatManyAttacks()
    {
        var overpower = new ModifierSpec("overpower", 60000, onHit: new[] { EffectDefinition.Damage(DamageType.Pure, 5) }, attackCharges: 3);
        var kernel = Run(Duel(onAlpha: overpower));

        Assert.True(HitsOnBeta(kernel).Count(d => d.DamageType == DamageType.Physical) >= 4);
        Assert.Equal(3, HitsOnBeta(kernel).Count(d => d.DamageType == DamageType.Pure));
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "overpower");
    }

    /// <summary>A part of an effect list that applies to heroes only (Feast's hero bonus, Glaives' intelligence steal).</summary>
    [Fact]
    public void IfHero_RunsItsEffectsOnHeroesOnly()
    {
        var kernel = Duel(attack: false);
        MatchUnit? bug = null;
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            bug = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, ctx.Tick.Value + 100000);
            EffectDefinition[] effects = { EffectDefinition.Damage(DamageType.Pure, 10), EffectDefinition.IfHero(EffectDefinition.Damage(DamageType.Pure, 7)) };
            Effects.Run(ctx, H(ctx.State, "alpha"), effects, 1, H(ctx.State, "beta"), H(ctx.State, "beta").Position);
            Effects.Run(ctx, H(ctx.State, "alpha"), effects, 1, bug, bug.Position);
        }));
        kernel.RunTicks(3);

        Assert.Equal(new[] { 10, 7 }, HitsOnBeta(kernel).Select(d => d.Amount.ToIntFloor()));
        Assert.Equal(new[] { 10 }, kernel.EventsOf<DamageDealt>().Where(d => d.Target == bug!.Id).Select(d => d.Amount.ToIntFloor()));
    }

    /// <summary>Culling Blade: a unit at or below the threshold dies; above it the effect does nothing (the damage is a separate effect).</summary>
    [Theory]
    [InlineData(250, true)]
    [InlineData(251, false)]
    public void Execute_KillsAtOrBelowTheThreshold(int hp, bool dies)
    {
        var kernel = Cast(ctx => H(ctx.State, "beta").Hp = Fixed.FromInt(hp), EffectDefinition.Execute(250));
        kernel.RunTicks(2);

        Assert.Equal(dies, H(kernel.State, "beta").Hero!.IsDead);
    }

    /// <summary>Split Shot's reach is the attack range plus a bonus; Gunslinger's extra target comes with a chance.</summary>
    [Theory]
    [InlineData(100, true)]
    [InlineData(0, false)]
    public void ExtraTargets_CanReachFromTheAttackRange_WithAChance(int chance, bool hit)
    {
        var split = new ModifierSpec("split", -1, extraTargets: 1, extraTargetRadius: 100, extraTargetRadiusFromAttackRange: true, extraTargetChancePercent: chance);
        var kernel = Duel(onAlpha: split);
        MatchUnit? bug = null;
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            // 240 away: outside a plain 100 radius, inside the attack range (150) + 100.
            bug = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, ctx.Tick.Value + 100000);
            bug.Position = P(5000, 4760);
            bug.StartPosition = bug.Position;
            Modifiers.Apply(ctx, bug, ModifierSpec.Status("hold", 100000, ModifierFlags.Stunned), EntityId.None);
        }));
        kernel.RunTicks(30 * 3);

        Assert.NotEmpty(HitsOnBeta(kernel));
        Assert.Equal(hit, kernel.EventsOf<DamageDealt>().Any(d => d.Target == bug!.Id));
    }

    /// <summary>Fractional percents (Warpath's 2.5% per stack, Chaos Strike's 33.33%): move speed and chances keep hundredths of a percent.</summary>
    [Fact]
    public void FractionalMoveSpeedPercent_IsNotRoundedAway()
    {
        var warpath = new ModifierSpec("warpath", 60000, moveSpeedHundredths: 250, maxStacks: 10);
        var kernel = Cast(_ => { }, Enumerable.Repeat(EffectDefinition.OnCaster(EffectDefinition.Modifier(warpath)), 4).ToArray());

        Assert.Equal(300 * 110 / 100, H(kernel.State, "alpha").MoveSpeed);   // 4 stacks × 2.5% = 10%
    }

    [Theory]
    [InlineData(3333, 0.3333)]
    [InlineData(950, 0.095)]
    public void FractionalChances_ProcAtTheirRate(int hundredths, double rate)
    {
        var strike = new ModifierSpec("strike", -1, onHit: new[] { EffectDefinition.Damage(DamageType.Pure, 0) }, onHitChancePercent: 0, onHitChanceHundredths: hundredths);
        var kernel = Duel(onAlpha: strike, attack: false);
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            for (int i = 0; i < 4000; i++)
                AttackSystem.Strike(ctx, H(ctx.State, "alpha"), H(ctx.State, "beta"), 0);
        }));
        kernel.RunTicks(3);

        double procs = HitsOnBeta(kernel).Count(d => d.DamageType == DamageType.Pure);
        Assert.InRange(procs / 4000, rate - 0.025, rate + 0.025);
    }

    /// <summary>Jingu Mastery: a charged buff granted by a hit does not spend a charge on that hit.</summary>
    [Fact]
    public void AttackCharges_GrantedByAHit_StartCountingFromTheNextAttack()
    {
        var charged = new ModifierSpec("jingu.charges", 60000, onHit: new[] { EffectDefinition.Damage(DamageType.Pure, 5) }, attackCharges: 2);
        var grant = new ModifierSpec("jingu", -1, onHit: new[] { EffectDefinition.OnCaster(EffectDefinition.Modifier(charged)) }, consumedOnHit: true);
        var kernel = Duel(onAlpha: grant, attack: false);
        kernel.AddSystem(new LambdaSystem(3, ctx =>
        {
            for (int i = 0; i < 4; i++)
                AttackSystem.Strike(ctx, H(ctx.State, "alpha"), H(ctx.State, "beta"), 100);
        }));
        kernel.RunTicks(4);

        Assert.Equal(2, HitsOnBeta(kernel).Count(d => d.DamageType == DamageType.Pure));   // the granting hit, then two charged ones
    }

    /// <summary>Flak Cannon: the hits on extra targets come with the attack and do not spend its charges.</summary>
    [Fact]
    public void AttackCharges_AreNotSpentByExtraTargetHits()
    {
        var flak = new ModifierSpec("flak", 60000, extraTargets: 20000, extraTargetRadius: 500, attackCharges: 3);
        var kernel = Duel(onAlpha: flak);
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            foreach (int y in new[] { 5100, 4900, 5150, 4850 })
            {
                MatchUnit bug = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, ctx.Tick.Value + 100000);
                bug.Position = P(5050, y);
                bug.StartPosition = bug.Position;
                Modifiers.Apply(ctx, bug, ModifierSpec.Status("hold", 100000, ModifierFlags.Stunned), EntityId.None);
            }
        }));
        kernel.RunTicks(30 * 8);

        Assert.True(HitsOnBeta(kernel).Count >= 5);
        int splashHits = kernel.EventsOf<DamageDealt>().Count(d => d.Attacker == H(kernel.State, "alpha").Id && d.Target != H(kernel.State, "beta").Id);
        Assert.Equal(3 * 4, splashHits);   // three attacks, four extra targets each
    }

    private static RulesetDraft DotaCombat() => new RulesetDraft { DotaStacking = true, MaxAttackSpeed = 700, IllusionBuildingPercent = 60, SummonBuildingPercent = 90 }.WithoutWaves();

    /// <summary>Attack speed is capped at 700 (Liquipedia/fandom «Attack speed»: 20 ≤ attack speed ≤ 700).</summary>
    [Fact]
    public void AttackSpeed_IsCappedAt700()
    {
        var kernel = Duel(onAlpha: new ModifierSpec("frenzy", -1, attackSpeedBonus: 1000), attack: false, draft: DotaCombat());
        kernel.RunTicks(2);

        Assert.Equal(600, H(kernel.State, "alpha").AttackSpeedBonus);   // 100 base + 600 = 700
    }

    /// <summary>Evasion sources stack diminishingly: two 50% sources make 75%.</summary>
    [Fact]
    public void Evasion_StacksDiminishingly()
    {
        var kernel = Duel(attack: false, draft: DotaCombat());
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "beta"), new ModifierSpec("butterfly", -1, evasionPercent: 50), EntityId.None);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), new ModifierSpec("talisman", -1, evasionPercent: 50), EntityId.None);
        }));
        kernel.RunTicks(2);

        Assert.Equal(7500, H(kernel.State, "beta").EvasionHundredths);
    }

    /// <summary>Magic resistance sources multiply: 25% base and +50% make 62.5%.</summary>
    [Fact]
    public void MagicResistance_StacksMultiplicatively()
    {
        var kernel = Duel(onBeta: new ModifierSpec("cloak", -1, magicResistBonus: 50), attack: false, draft: DotaCombat());
        kernel.AddSystem(new LambdaSystem(1, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(100), DamageType.Magical)));
        kernel.RunTicks(2);

        Assert.Equal(6250, H(kernel.State, "beta").MagicResistHundredths);
        Assert.InRange(HitsOnBeta(kernel).Single().Amount.ToDouble(), 37.49, 37.51);
    }

    /// <summary>Critical strikes roll from the strongest multiplier to the weakest; the first that procs wins.</summary>
    [Fact]
    public void CriticalStrikes_RollFromTheStrongestMultiplier()
    {
        var kernel = Duel(attack: false, draft: DotaCombat());
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("crystalys", -1, critChancePercent: 100, critMultiplierPercent: 150), EntityId.None);
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("daedalus", -1, critChancePercent: 50, critMultiplierPercent: 300), EntityId.None);
        }));
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            for (int i = 0; i < 200; i++)
            {
                H(ctx.State, "beta").Hp = Fixed.FromInt(H(ctx.State, "beta").MaxHp);
                AttackSystem.Strike(ctx, H(ctx.State, "alpha"), H(ctx.State, "beta"), 100);
            }
        }));
        kernel.RunTicks(3);

        // Attack 50–54 (+ level damage) times the armor multiplier: ×3 crits are above twice the largest ×1.5 one.
        MatchUnit alpha = H(kernel.State, "alpha");
        double armor = DamageMath.ArmorMultiplier(H(kernel.State, "beta").Armor, kernel.State.Ruleset.Combat.ArmorFactor).ToDouble();
        List<double> hits = HitsOnBeta(kernel).Select(d => d.Amount.ToDouble()).ToList();
        Assert.All(HitsOnBeta(kernel), d => Assert.True(d.IsCritical));   // the ×1.5 source always procs when the ×3 one does not
        int triple = hits.Count(h => h > alpha.AttackDamageMax * 2 * armor);
        Assert.InRange(triple, 60, 140);   // about half: the ×3 source rolls first at 50%
    }

    /// <summary>Against buildings illusions deal 60% and summons 90% of their attack (Liquipedia «Attack Damage», D-051).</summary>
    [Fact]
    public void IllusionsAndSummons_HaveTheirOwnFactorAgainstBuildings()
    {
        RulesetDraft draft = DotaCombat();
        draft.Creeps.Add(new CreepType("wolf", MeleeStats(hp: 300, armor: 0, dmgMin: 100, dmgMax: 100, speed: 300)));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        MatchUnit? wolf = null, image = null;
        MatchUnit? tower = null;
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            tower = ctx.State.Units.First(u => u.Kind == UnitKind.Structure && u.Team == Team.Dusk && u.CanAttackAtAll);
            wolf = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "alpha"), ctx.State.Ruleset.FindCreepType("wolf")!, ctx.Tick.Value, ctx.Tick.Value + 100000);
            image = ctx.State.SpawnIllusion(ctx.Ids.Allocate(), H(ctx.State, "alpha"), H(ctx.State, "alpha"), ctx.Tick.Value, ctx.Tick.Value + 100000);
            AttackSystem.Strike(ctx, wolf, tower, 100);
            AttackSystem.Strike(ctx, image, tower, 100);
        }));
        kernel.RunTicks(2);

        double armor = DamageMath.ArmorMultiplier(tower!.Armor, kernel.State.Ruleset.Combat.ArmorFactor).ToDouble();
        Assert.InRange(kernel.EventsOf<DamageDealt>().Single(d => d.Attacker == wolf!.Id).Amount.ToDouble(), 100 * 0.9 * armor - 0.01, 100 * 0.9 * armor + 0.01);
        DamageDealt byImage = kernel.EventsOf<DamageDealt>().Single(d => d.Attacker == image!.Id);
        Assert.InRange(byImage.Amount.ToDouble(), image!.AttackDamageMin * 0.6 * armor - 0.01, image.AttackDamageMax * 0.6 * armor + 0.01);
    }
}
