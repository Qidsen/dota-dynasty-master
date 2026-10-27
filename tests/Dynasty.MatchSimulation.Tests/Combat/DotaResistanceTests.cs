using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Dota's resistances (Fandom «Status Resistance», «Slow Resistance»): status resistance shortens enemy debuffs, every
/// source multiplied — not allies' debuffs, nor damage over time (Dota keeps its total); slow resistance scales percentage
/// slows down, sources multiplied; a spell block stops one enemy targeted spell per cooldown (Linken's Sphere, Roshan);
/// Roshan takes 80% less damage from illusions.
/// </summary>
public class DotaResistanceTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena(params ModifierSpec[] onBeta)
    {
        var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "bolt");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "gamma").Position = P(4900, 3000);
            H(ctx.State, "beta").Position = P(5200, 3000);
            foreach (ModifierSpec spec in onBeta)
                Modifiers.Apply(ctx, H(ctx.State, "beta"), spec, H(ctx.State, "beta").Id);
        });
        return kernel;
    }

    private static long ExpiresIn(MatchUnit unit, string key, long now) => unit.Modifiers.Single(m => m.Spec.Key == key).ExpiresTick - now;

    [Fact]
    public void StatusResistance_ShortensEnemyDebuffs_SourcesMultiplied_NotAlliesOrDamageOverTime()
    {
        var kernel = Arena(new ModifierSpec("sange", -1, statusResistancePercent: 50), new ModifierSpec("bulldoze", -1, statusResistancePercent: 50));
        long stun = 0, allied = 0, burn = 0;
        At(kernel, 2, ctx =>
        {
            MatchUnit beta = H(ctx.State, "beta");
            Modifiers.Apply(ctx, beta, new ModifierSpec("stun", 4000, ModifierFlags.Stunned), H(ctx.State, "alpha").Id);
            Modifiers.Apply(ctx, beta, new ModifierSpec("ally_root", 4000, ModifierFlags.Rooted), beta.Id);
            Modifiers.Apply(ctx, beta, new ModifierSpec("burn", 4000, damagePerSecond: Fixed.FromInt(10)), H(ctx.State, "alpha").Id);
            stun = ExpiresIn(beta, "stun", ctx.Tick.Value);
            allied = ExpiresIn(beta, "ally_root", ctx.Tick.Value);
            burn = ExpiresIn(beta, "burn", ctx.Tick.Value);
        });
        kernel.RunTicks(3);

        Assert.Equal(7500, H(kernel.State, "beta").StatusResistanceHundredths);   // 1 − 0.5 × 0.5
        Assert.Equal((30L, 120L, 120L), (stun, allied, burn));                    // 4 s × 0.25 = 1 s
    }

    [Fact]
    public void SlowResistance_ScalesPercentageSlows_SourcesMultiplied()
    {
        var kernel = Arena();
        int plain = 0, resisted = 0, baseSpeed = 0;
        At(kernel, 2, ctx =>
        {
            MatchUnit beta = H(ctx.State, "beta");
            baseSpeed = beta.MoveSpeed;
            Modifiers.Apply(ctx, beta, new ModifierSpec("frost", 4000, moveSpeedPercent: -40), H(ctx.State, "alpha").Id);
            plain = beta.MoveSpeed;
            Modifiers.Apply(ctx, beta, new ModifierSpec("armlet", -1, slowResistancePercent: 50), beta.Id);
            Modifiers.Apply(ctx, beta, new ModifierSpec("gods", -1, slowResistancePercent: 50), beta.Id);
            resisted = beta.MoveSpeed;
        });
        kernel.RunTicks(3);

        Assert.Equal(baseSpeed * 60 / 100, plain);
        Assert.Equal(baseSpeed * 90 / 100, resisted);   // 40% × 0.5 × 0.5 = 10%
    }

    [Fact]
    public void ASpellBlock_StopsOneEnemyTargetedSpellPerCooldown()
    {
        var kernel = Arena(new ModifierSpec("linkens", -1, spellBlockCooldownMs: 5000));
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("bolt", H(ctx.State, "beta").Id)));
        int hpAfterFirst = 0;
        At(kernel, 30, ctx =>
        {
            hpAfterFirst = H(ctx.State, "beta").Hp.ToIntFloor();
            H(ctx.State, "alpha").Hero!.Ability("bolt")!.CooldownReadyTick = 0;
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(H(ctx.State, "alpha").Hero!.MaxMana);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("bolt", H(ctx.State, "beta").Id));
        });
        kernel.RunTicks(50);

        MatchUnit beta = H(kernel.State, "beta");
        Assert.Single(kernel.EventsOf<SpellBlocked>());
        Assert.Equal(beta.MaxHp, hpAfterFirst);                      // the first bolt blocked
        Assert.True(beta.Hp.ToIntFloor() < beta.MaxHp - 50, "the second bolt lands, the block is on cooldown");
    }

    [Fact]
    public void IllusionDamageTaken_CutsWhatIllusionsDeal()
    {
        var kernel = Arena(new ModifierSpec("immortal", -1, illusionDamageTakenPercent: -80));
        int taken = 0;
        At(kernel, 2, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha"), beta = H(ctx.State, "beta");
            MatchUnit illusion = ctx.State.SpawnIllusion(ctx.Ids.Allocate(), alpha, alpha, ctx.Tick.Value, ctx.Tick.Value + 1000);
            Fixed before = beta.Hp;
            Damage.Deal(ctx, illusion.Id, beta, Fixed.FromInt(100), DamageType.Pure);
            taken = (before - beta.Hp).ToIntFloor();
        });
        kernel.RunTicks(3);

        Assert.Equal(20, taken);
    }
}
