using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// What a unit does with the damage it takes (Dota's official ability notes, abilities_english.txt of the build after 7.41f, and the
/// project owner on 2026-10-29): Dispersion reflects a share before reductions to the enemies around (full within one radius, falling
/// linearly to nothing at the other), Spiked Carapace negates a player's damage once per source and returns it with a stun, Kraken Shell
/// blocks attack damage (half against creeps) and cleanses past a threshold of players' damage in a window, Refraction swallows whole
/// instances of damage. Reflected damage is never reflected again.
/// </summary>
public class DamageResponseTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Dawn: alpha (who takes the damage), gamma; Dusk: beta (who deals it), delta. Everyone on full, large health; no armor.</summary>
    private static SimulationKernel<MatchState> Arena(params (string Hero, int X, int Y)[] at)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })   // no armor, no regeneration: the numbers are exact
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1200),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 300, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            foreach (var (hero, x, y) in at)
                H(ctx.State, hero).Position = P(x, y);
        });
        return kernel;
    }

    private static void Give(SimulationKernel<MatchState> kernel, string hero, ModifierSpec spec) =>
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, hero), spec, H(ctx.State, hero).Id));

    private static Fixed Lost(MatchState state, string hero) => Fixed.FromInt(H(state, hero).MaxHp) - H(state, hero).Hp;

    [Fact]
    public void Dispersion_ReflectsAShareBeforeReductions_FullNear_LessFarther_NoneBeyond()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", 5300, 5000), ("delta", 5525, 5000), ("gamma", 3000, 5000));
        Give(kernel, "alpha", new ModifierSpec("dispersion", -1, reflectHundredths: 2000, reflectFullRadius: 350, reflectRadius: 700));
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(100), DamageType.Pure));

        kernel.RunTicks(3);

        Assert.Equal(Fixed.FromInt(80), Lost(kernel.State, "alpha"));   // the reflected 20 is not dealt to her
        Assert.Equal(Fixed.FromInt(20), Lost(kernel.State, "beta"));    // 300 away: full
        Assert.Equal(Fixed.FromInt(10), Lost(kernel.State, "delta"));   // 525 away: halfway from 350 to 700
        Assert.Equal(Fixed.Zero, Lost(kernel.State, "gamma"));          // an ally
    }

    [Fact]
    public void ReflectedDamage_IsNotReflectedAgain()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", 5300, 5000), ("delta", 9000, 9000), ("gamma", 3000, 5000));
        var dispersion = new ModifierSpec("dispersion", -1, reflectHundredths: 2000, reflectFullRadius: 350, reflectRadius: 700);
        Give(kernel, "alpha", dispersion);
        Give(kernel, "beta", dispersion);
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(100), DamageType.Pure));

        kernel.RunTicks(3);

        Assert.Equal(Fixed.FromInt(80), Lost(kernel.State, "alpha"));
        Assert.Equal(Fixed.FromInt(20), Lost(kernel.State, "beta"));   // beta's own Dispersion does not bounce it back
    }

    [Fact]
    public void Carapace_NegatesAPlayersDamage_ReturnsItAndStuns_OncePerSource()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", 5300, 5000), ("delta", 9000, 9000), ("gamma", 3000, 5000));
        var stun = new ModifierSpec("carapace_stun", 1000, ModifierFlags.Stunned);
        Give(kernel, "alpha", new ModifierSpec("carapace", 2000, returnPercent: 140, onReturn: new[] { EffectDefinition.Modifier(stun) }));
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(100), DamageType.Pure));
        At(kernel, 3, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(50), DamageType.Pure));

        kernel.RunTicks(4);

        Assert.Equal(Fixed.FromInt(50), Lost(kernel.State, "alpha"));     // the first hit negated, the second (same source) not
        Assert.Equal(Fixed.FromInt(140), Lost(kernel.State, "beta"));     // 140% of the first, once
        Assert.Contains(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "carapace_stun");
    }

    [Fact]
    public void Carapace_IgnoresDamageFromCreeps()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", 9000, 9000), ("delta", 9000, 9500), ("gamma", 3000, 5000));
        Give(kernel, "alpha", new ModifierSpec("carapace", 2000, returnPercent: 140));
        At(kernel, 2, ctx =>
        {
            MatchUnit creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Dusk, ctx.State.Ruleset.CreepTypes[0], ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
            creep.Position = P(5200, 5000);
            Damage.Deal(ctx, creep.Id, H(ctx.State, "alpha"), Fixed.FromInt(100), DamageType.Pure);
        });

        kernel.RunTicks(3);

        Assert.Equal(Fixed.FromInt(100), Lost(kernel.State, "alpha"));
    }

    [Fact]
    public void DamageBlock_TakesAFlatAmountOffAttacks_HalfAgainstCreeps_NotOffSpells()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", 5150, 5000), ("delta", 9000, 9000), ("gamma", 3000, 5000));
        Give(kernel, "alpha", new ModifierSpec("kraken", -1, damageBlock: 30, damageBlockCreepPercent: 50));
        Fixed afterAttack = Fixed.Zero, afterCreep = Fixed.Zero;
        At(kernel, 2, ctx => Systems.AttackSystem.ApplyHit(ctx, H(ctx.State, "beta"), H(ctx.State, "alpha"), Fixed.FromInt(100), false));
        At(kernel, 3, ctx =>
        {
            afterAttack = Lost(ctx.State, "alpha");
            MatchUnit creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Dusk, ctx.State.Ruleset.CreepTypes[0], ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
            creep.Position = P(5100, 5000);
            Systems.AttackSystem.ApplyHit(ctx, creep, H(ctx.State, "alpha"), Fixed.FromInt(100), false);
            afterCreep = Lost(ctx.State, "alpha");
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(100), DamageType.Pure);
        });

        kernel.RunTicks(4);

        Assert.Equal(Fixed.FromInt(70), afterAttack);
        Assert.Equal(Fixed.FromInt(70 + 85), afterCreep);
        Assert.Equal(Fixed.FromInt(70 + 85 + 100), Lost(kernel.State, "alpha"));
    }

    [Fact]
    public void Cleanse_RemovesDebuffs_WhenPlayersDamageCrossesTheThreshold_WithinTheWindow()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", 5300, 5000), ("delta", 9000, 9000), ("gamma", 3000, 5000));
        Give(kernel, "alpha", new ModifierSpec("kraken", -1, cleanseThreshold: 300, cleanseWindowMs: 7000));
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("slow", 30000, moveSpeedPercent: -50), H(ctx.State, "beta").Id));
        At(kernel, 3, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(200), DamageType.Pure));
        At(kernel, 3 + 8 * 30, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(200), DamageType.Pure));   // the window has passed
        bool slowedAfterTwo = false;
        At(kernel, 5 + 8 * 30, ctx => slowedAfterTwo = H(ctx.State, "alpha").Modifiers.Any(m => m.Spec.Key == "slow"));
        At(kernel, 6 + 8 * 30, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(150), DamageType.Pure));

        kernel.RunTicks(8 + 8 * 30);

        Assert.True(slowedAfterTwo, "200 and, 8 s later, 200: the counter started again");
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "slow");   // 200 + 150 within the window
    }

    /// <summary>
    /// Gravekeeper's Cloak: each layer cuts damage by its share; a player's damage over the minimum (after the cut) takes a layer off,
    /// which comes back after the recovery time; smaller hits leave the layers.
    /// </summary>
    [Fact]
    public void Layers_CutDamage_ALayerLostPerBigHit_BackAfterTheRecovery()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", 5300, 5000), ("delta", 9000, 9000), ("gamma", 3000, 5000));
        Give(kernel, "alpha", new ModifierSpec("cloak", -1, layers: 4, layerReductionPercent: 20, layerRecoveryMs: 4000, layerMinimumDamage: 40));
        var lost = new List<Fixed>();
        void Hit(long tick, int damage) => At(kernel, tick, ctx =>
        {
            Fixed before = H(ctx.State, "alpha").Hp;
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(damage), DamageType.Pure);
            lost.Add(before - H(ctx.State, "alpha").Hp);
        });
        Hit(2, 100);            // 4 layers: 80% off -> 20 taken, under the minimum: no layer lost
        Hit(3, 500);            // still 4 layers: 100 taken, a layer lost
        Hit(4, 500);            // 3 layers: 200 taken, a layer lost
        Hit(3 + 4 * 30, 100);   // the first lost layer is back (lost at tick 3), the second not yet: 3 layers -> 40 taken, not over the minimum

        kernel.RunTicks(7 + 4 * 30);

        Assert.Equal(new[] { Fixed.FromInt(20), Fixed.FromInt(100), Fixed.FromInt(200), Fixed.FromInt(40) }, lost);
    }

    /// <summary>
    /// Bristleback: less damage from the side (within 110 degrees of the back) and more less from the rear (within 70 degrees of the
    /// back), by where the source stands against the way the bearer faces; enough damage from the rear releases its Quill Spray.
    /// </summary>
    [Theory]
    [InlineData(5300, 5000, 100)]   // in front
    [InlineData(5000, 5300, 80)]    // the side: 90 degrees off the front
    [InlineData(4700, 5000, 60)]    // the rear
    [InlineData(4900, 5300, 80)]    // 72 degrees off the back: the side, not the rear (within 70 is the rear)
    [InlineData(4800, 5300, 60)]    // 56 degrees off the back: the rear
    public void Directional_LessDamageFromTheSideAndRear(int x, int y, int expected)
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", x, y), ("delta", 9000, 9000), ("gamma", 3000, 5000));
        Give(kernel, "alpha", new ModifierSpec("bristleback", -1, sideReductionPercent: 20, backReductionPercent: 40, sideAngle: 110, backAngle: 70));
        At(kernel, 2, ctx =>
        {
            H(ctx.State, "alpha").Facing = P(1, 0);   // faces east
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(100), DamageType.Pure);
        });

        kernel.RunTicks(3);

        Assert.Equal(Fixed.FromInt(expected), Lost(kernel.State, "alpha"));
    }

    [Fact]
    public void Directional_RearDamageAddsUp_AndReleasesTheBearersAbility()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1200),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 300, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "quills" } : null));
        draft.Abilities.Add(new AbilityDefinition("quills", "Quills", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 3000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Area(700, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 33)) }));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(4700, 5000);   // behind
            H(ctx.State, "gamma").Position = P(3000, 3000);
            H(ctx.State, "delta").Position = P(5300, 5000);  // in front, near: the quills hit it too
        });
        Give(kernel, "alpha", new ModifierSpec("bristleback", -1, backReductionPercent: 50, sideAngle: 110, backAngle: 70, releaseAbilityId: "quills", releaseThreshold: 100));
        for (long tick = 2; tick <= 4; tick++)
            At(kernel, tick, ctx =>
            {
                H(ctx.State, "alpha").Facing = P(1, 0);
                Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(80), DamageType.Pure);   // 40 each from behind
            });

        kernel.RunTicks(5);

        Assert.Equal(Fixed.FromInt(33), Lost(kernel.State, "delta"));   // 40 + 40 + 40 = 120: one release at 100
        Assert.Equal(Fixed.FromInt(33), Lost(kernel.State, "beta"));
    }

    [Fact]
    public void Facing_FollowsTheWalk_AndTurnsToTheAttackTarget()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", 5000, 5100), ("delta", 9000, 9000), ("gamma", 3000, 5000));
        At(kernel, 2, ctx => Dynasty.MatchSimulation.Heroes.HeroOrders.Issue(H(ctx.State, "gamma"), Dynasty.MatchSimulation.Heroes.HeroOrder.Move(P(3000, 4000))));   // walks south
        At(kernel, 2, ctx => Dynasty.MatchSimulation.Heroes.HeroOrders.Issue(H(ctx.State, "alpha"), Dynasty.MatchSimulation.Heroes.HeroOrder.Attack(H(ctx.State, "beta").Id)));   // beta is north

        kernel.RunTicks(10);

        FixedVector2 gamma = H(kernel.State, "gamma").Facing, alpha = H(kernel.State, "alpha").Facing;
        Assert.True(gamma.Y < Fixed.Zero && Fixed.Abs(gamma.X) < Fixed.Abs(gamma.Y), "gamma faces south");
        Assert.True(alpha.Y > Fixed.Zero && Fixed.Abs(alpha.X) < Fixed.Abs(alpha.Y), "alpha faces beta, north");
    }

    [Fact]
    public void AbsorbInstances_SwallowWholeHits_UntilTheChargesAreGone()
    {
        var kernel = Arena(("alpha", 5000, 5000), ("beta", 5300, 5000), ("delta", 9000, 9000), ("gamma", 3000, 5000));
        Give(kernel, "alpha", new ModifierSpec("refraction", 17000, absorbInstances: 2, absorbBarrier: 30));
        At(kernel, 2, ctx =>
        {
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(20), DamageType.Pure);    // under the barrier: absorbed, the charge stays
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(500), DamageType.Pure);   // over the 10 left: all absorbed, a charge used
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(500), DamageType.Pure);   // the last charge
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(100), DamageType.Pure);   // no charges: taken
        });

        kernel.RunTicks(3);

        Assert.Equal(Fixed.FromInt(100), Lost(kernel.State, "alpha"));
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "refraction");
    }
}
