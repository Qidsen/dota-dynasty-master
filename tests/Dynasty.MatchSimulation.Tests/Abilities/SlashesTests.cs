using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.Abilities.TestAbilities;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Slashes (Dota's Omnislash; the project owner on 2026-10-29: each slash is the hero's normal attack on a random visible enemy
/// within the radius of the current target, the hero moved next to it; one slash per attack interval divided by the rate; the hero
/// is invulnerable and takes no orders; with no enemy near, the slashes end early).
/// </summary>
public class SlashesTests
{
    private const int Duration = 3000;

    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static RulesetDraft WithSlashes(int ratePercent = 140)
    {
        var draft = Draft();
        draft.Abilities.Add(new AbilityDefinition("omni", "Omnislash", AbilityTargeting.Unit, TargetTeam.Enemy, 450, 0,
            new[] { 100000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Slashes(425, new[] { ratePercent }, new ModifierSpec("omni", Duration, ModifierFlags.Invulnerable | ModifierFlags.Busy, damageBonus: 30, dispellable: false)) }));
        return draft;
    }

    /// <summary>Dawn: alpha (with the slashes) and gamma; Dusk: beta next to alpha, delta 500 beyond beta.</summary>
    private static SimulationKernel<MatchState> Arena(RulesetDraft draft, Action<SimulationContext<MatchState>>? setup = null)
    {
        draft.WithHeroAbilities("alpha", new[] { "omni" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "gamma").Position = P(4000, 3000);
            H(ctx.State, "beta").Position = P(5200, 3000);
            H(ctx.State, "delta").Position = P(5700, 3000);
            foreach (string hero in new[] { "beta", "delta" })
                H(ctx.State, hero).Hp = Fixed.FromInt(100000);   // the slashes must not end by killing
            setup?.Invoke(ctx);
        });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("omni", H(ctx.State, "beta").Id)));
        return kernel;
    }

    private static List<long> SlashTicks(SimulationKernel<MatchState> kernel, string target) =>
        kernel.Events.Records.Where(r => r.Event is DamageDealt d && d.Attacker == H(kernel.State, "alpha").Id && d.Target == H(kernel.State, target).Id)
            .Select(r => r.Tick.Value).ToList();

    [Fact]
    public void Slashes_HitTheTarget_OncePerAttackIntervalDividedByTheRate_ForTheDuration()
    {
        var kernel = Arena(WithSlashes());

        kernel.RunTicks(2 + 4 * 30);

        MatchUnit alpha = H(kernel.State, "alpha");
        List<long> ticks = SlashTicks(kernel, "beta").Where(t => t <= 3 + Duration * 30 / 1000).ToList();   // later: its own attacks again
        long interval = System.Math.Max(1, alpha.AttackIntervalTicks * 100 / 140);
        long expected = 1 + Duration * 30 / 1000 / interval;   // one slash at once, then one per interval
        Assert.InRange(ticks.Count, expected - 1, expected + 1);
        Assert.True(ticks.Zip(ticks.Skip(1)).All(p => p.Second - p.First == interval), $"interval {interval}, slashes at {string.Join(",", ticks)}");
        Assert.DoesNotContain(alpha.Modifiers, m => m.Spec.Key == "omni");   // over after the duration
    }

    [Fact]
    public void Slashes_FasterRate_SlashMoreOften()
    {
        var slow = Arena(WithSlashes(100));
        var fast = Arena(WithSlashes(200));

        slow.RunTicks(2 + 4 * 30);
        fast.RunTicks(2 + 4 * 30);

        Assert.True(SlashTicks(fast, "beta").Count > SlashTicks(slow, "beta").Count * 3 / 2,
            $"{SlashTicks(fast, "beta").Count} slashes at 200%, {SlashTicks(slow, "beta").Count} at 100%");
    }

    [Fact]
    public void Slashes_JumpToOtherEnemiesNearTheTarget_AtRandom()
    {
        var kernel = Arena(WithSlashes(), ctx => H(ctx.State, "delta").Position = P(5500, 3000));   // within 425 of beta

        kernel.RunTicks(2 + 4 * 30);

        Assert.NotEmpty(SlashTicks(kernel, "beta"));
        Assert.NotEmpty(SlashTicks(kernel, "delta"));
    }

    /// <summary>The first slash lands on the target itself, at once, even when it stands beyond the radius of the later slashes.</summary>
    [Fact]
    public void TheFirstSlash_LandsOnTheTarget_AtTheCast()
    {
        var kernel = Arena(WithSlashes(), ctx =>
        {
            H(ctx.State, "beta").Position = P(5470, 3000);   // within the cast range, beyond 425 + its radius
            H(ctx.State, "delta").Position = P(9000, 9000);
        });

        kernel.RunTicks(5);

        Assert.Equal(new long[] { 3 }, SlashTicks(kernel, "beta"));
    }

    [Fact]
    public void Slashes_NeverReachAnEnemyOutOfTheRadius()
    {
        var kernel = Arena(WithSlashes());   // delta is 500 from beta

        kernel.RunTicks(2 + 4 * 30);

        Assert.Empty(SlashTicks(kernel, "delta"));
    }

    [Fact]
    public void Slashes_EndEarly_WhenNoEnemyIsNear()
    {
        var kernel = Arena(WithSlashes());
        At(kernel, 20, ctx => H(ctx.State, "beta").Position = P(9000, 9000));   // gone (out of the radius and of sight)

        kernel.RunTicks(40);   // the next slash is due at tick 35: no enemy near, they end there (not after the full 3 s)

        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "omni");
    }

    [Fact]
    public void TheSlashingHero_IsInvulnerable_AndTakesNoOrders()
    {
        var kernel = Arena(WithSlashes());
        At(kernel, 10, ctx =>
        {
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Move(P(1000, 3000)));
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id));
        });
        int hp = 0;
        bool attackable = true, targetable = true;
        FixedVector2 afterOrder = default, beforeNextSlash = default;
        At(kernel, 11, ctx => afterOrder = H(ctx.State, "alpha").Position);
        At(kernel, 34, ctx => beforeNextSlash = H(ctx.State, "alpha").Position);   // the next slash is due at 35
        At(kernel, 40, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            hp = alpha.Hp.ToIntFloor();
            Damage.Deal(ctx, H(ctx.State, "beta").Id, alpha, Fixed.FromInt(500), DamageType.Pure);
            attackable = ctx.State.CanAttack(H(ctx.State, "beta"), alpha);
            targetable = Effects.Matches(ctx.State, H(ctx.State, "beta"), alpha, TargetTeam.Enemy);
        });

        kernel.RunTicks(45);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Equal(hp, alpha.Hp.ToIntFloor());
        Assert.False(attackable);
        Assert.False(targetable);
        Assert.Equal(afterOrder, beforeNextSlash);   // the move order did nothing
    }
}
