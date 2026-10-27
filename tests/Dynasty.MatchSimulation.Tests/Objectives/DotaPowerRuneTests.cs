using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// The power runes' mechanics (Fandom «… Rune», Valve 7.35–7.40): Amplify Damage +80% base damage and +15% spell damage;
/// Arcane −25% cooldowns, −30% mana costs; Haste: exactly 550 speed, slows or not; Shield: a barrier of 50% of the maximum
/// health; Regeneration: 6% a second, 1% for 3 s after a player's damage, gone when full; Illusion rune illusions take
/// 200% (melee) or 300% (ranged).
/// </summary>
public class DotaPowerRuneTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena(Action<SimulationContext<MatchState>>? setup = null)
    {
        var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "bolt");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "beta").Position = P(5300, 3000);
            setup?.Invoke(ctx);
        });
        return kernel;
    }

    private static void Give(SimulationContext<MatchState> ctx, string hero, ModifierSpec spec) => Modifiers.Apply(ctx, H(ctx.State, hero), spec, H(ctx.State, hero).Id);

    [Fact]
    public void AmplifyDamage_AddsToTheBaseDamage_AndToSpellDamage()
    {
        int plainBolt = 0, ampBolt = 0;
        foreach (bool amp in new[] { false, true })
        {
            var kernel = Arena(ctx =>
            {
                if (amp)
                    Give(ctx, "alpha", new ModifierSpec("amplify", 45000, baseDamagePercent: 80, spellAmplificationPercent: 15));
            });
            At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("bolt", H(ctx.State, "beta").Id)));
            kernel.RunTicks(30);
            int bolt = kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id && d.DamageType == DamageType.Magical).Sum(d => d.Amount.ToIntFloor());
            if (amp)
            {
                ampBolt = bolt;
                MatchUnit alpha = H(kernel.State, "alpha");
                Assert.Equal(alpha.Stats.AttackDamageMin * 180 / 100, alpha.AttackDamageMin);
            }
            else
            {
                plainBolt = bolt;
            }
        }

        Assert.InRange(ampBolt, plainBolt * 115 / 100 - 1, plainBolt * 115 / 100 + 1);
    }

    [Fact]
    public void Arcane_ShortensCooldownsAndMakesSpellsCheaper()
    {
        var kernel = Arena(ctx => Give(ctx, "alpha", new ModifierSpec("arcane", 50000, cooldownReductionPercent: 25, manaCostReductionPercent: 30)));
        int manaBefore = 0;
        At(kernel, 2, ctx => manaBefore = H(ctx.State, "alpha").Hero!.Mana.ToIntFloor());
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("bolt", H(ctx.State, "beta").Id)));
        kernel.RunTicks(30);

        AbilityState bolt = H(kernel.State, "alpha").Hero!.Ability("bolt")!;
        long castTick = kernel.TimedEventsOf<AbilityCast>().Single().Tick;
        Assert.Equal(castTick + 180, bolt.CooldownReadyTick);   // 8 s × 0.75 = 6 s
        Assert.InRange(manaBefore - H(kernel.State, "alpha").Hero!.Mana.ToIntFloor(), 69, 70);   // 100 × 0.7, less a little regeneration
    }

    [Fact]
    public void Haste_MovesAtExactly550_SlowedOrNot()
    {
        var kernel = Arena(ctx =>
        {
            Give(ctx, "alpha", new ModifierSpec("haste", 22000, fixedMoveSpeed: 550));
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("frost", 4000, moveSpeedPercent: -50), H(ctx.State, "beta").Id);
        });
        kernel.RunTicks(2);

        Assert.Equal(550, H(kernel.State, "alpha").MoveSpeed);
    }

    [Fact]
    public void Shield_IsABarrierOfHalfTheMaximumHealth()
    {
        int taken = 0;
        var kernel = Arena(ctx => Give(ctx, "alpha", new ModifierSpec("shield", 75000, barrierMaxHealthPercent: 50)));
        At(kernel, 2, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            Fixed before = alpha.Hp;
            Damage.Deal(ctx, H(ctx.State, "beta").Id, alpha, Fixed.FromInt(alpha.MaxHp / 2 + 100), DamageType.Pure);
            taken = (before - alpha.Hp).ToIntFloor();
        });
        kernel.RunTicks(3);

        Assert.InRange(taken, 99, 100);
    }

    [Fact]
    public void Regeneration_SlowsAfterAPlayersDamage_AndEndsWhenFull()
    {
        var regen = new ModifierSpec("regeneration", 30000, healPerSecondMaxHealthPercent: Fixed.FromInt(6), manaPerSecondMaxManaPercent: Fixed.FromInt(6),
            damagedRegenMaxPercent: Fixed.One, damagedRegenMs: 3000, endsWhenFull: true);
        int healedDamaged = 0, healedFree = 0;
        bool goneWhenFull = false;
        var kernel = Arena();
        At(kernel, 2, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            alpha.Hp = Fixed.FromInt(alpha.MaxHp / 10);
            alpha.Hero!.Mana = Fixed.FromInt(alpha.Hero.MaxMana / 10);
            Give(ctx, "alpha", regen);
            Damage.Deal(ctx, H(ctx.State, "beta").Id, alpha, Fixed.One, DamageType.Pure);
        });
        int hp = 0;
        At(kernel, 3, ctx => hp = H(ctx.State, "alpha").Hp.ToIntFloor());
        At(kernel, 33, ctx => healedDamaged = H(ctx.State, "alpha").Hp.ToIntFloor() - hp);   // 1 s within the 3 s
        At(kernel, 3 + 120, ctx => hp = H(ctx.State, "alpha").Hp.ToIntFloor());
        At(kernel, 3 + 150, ctx => healedFree = H(ctx.State, "alpha").Hp.ToIntFloor() - hp);   // 1 s after
        At(kernel, 3 + 600, ctx => goneWhenFull = !H(ctx.State, "alpha").Modifiers.Any(m => m.Spec.Key == "regeneration"));
        kernel.RunTicks(3 + 601);

        int max = H(kernel.State, "alpha").MaxHp;
        Assert.InRange(healedDamaged, max / 100 - 3, max / 100 + 6);      // 1% + base regeneration
        Assert.InRange(healedFree, max * 6 / 100 - 3, max * 6 / 100 + 6); // 6%
        Assert.True(goneWhenFull);
    }

    [Fact]
    public void IllusionRuneIllusions_TakeMoreDamageWhenRanged()
    {
        var melee = new ModifierSpec("rune_illusion", 75000, incomingDamagePercent: 100, attackDamagePercent: -65);
        var ranged = new ModifierSpec("rune_illusion", 75000, incomingDamagePercent: 200, attackDamagePercent: -65);
        var kernel = Arena(ctx => Effects.Run(ctx, H(ctx.State, "alpha"),
            new[] { EffectDefinition.IfMelee(EffectDefinition.Illusion(75000, new[] { melee }, 2)), EffectDefinition.IfRanged(EffectDefinition.Illusion(75000, new[] { ranged }, 2)) },
            1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position));
        kernel.RunTicks(2);

        var illusions = kernel.State.Units.Where(u => u.IllusionOf == H(kernel.State, "alpha").Id).ToList();
        int expected = H(kernel.State, "alpha").Stats.ProjectileSpeed == 0 ? 10000 : 20000;
        Assert.Equal(2, illusions.Count);
        Assert.True(illusions.All(i => i.IncomingDamageHundredths == expected));
    }
}
