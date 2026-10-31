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
/// Time Dilation (Dota 7.41f data and official notes): one stack, and one more for every ability on cooldown (casting one adds a stack,
/// a cooldown ending takes one); each stack deals damage per second and slows; cooldowns of abilities, not items, run slower.
/// </summary>
public class DilationTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Dilation = new ModifierSpec("time_dilation", 20000, moveSpeedPercent: -5, damagePerSecond: Fixed.FromInt(10),
        cooldownSlowPercent: 50, stacksPerCooldown: true);

    private static SimulationKernel<MatchState> Arena(int nukeCooldownMs = 10000)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("nuke", "Nuke", AbilityTargeting.Unit, TargetTeam.Enemy, 2000, 0, new[] { nukeCooldownMs }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 1) }));
        draft.Abilities.Add(new AbilityDefinition("bolt", "Bolt", AbilityTargeting.Unit, TargetTeam.Enemy, 2000, 0, new[] { 20000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 1) }));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: new[] { "nuke", "bolt" }));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5500, 5000);
            H(ctx.State, "beta").Hero!.Level = 2;   // both abilities learned
            HeroProgression.LearnSkills(ctx, H(ctx.State, "beta"));
        });
        return kernel;
    }

    private static AbilityState Ability(MatchState state, string id) => H(state, "beta").Hero!.Abilities.Single(a => a.Definition.Id == id);

    [Fact]
    public void Stacks_OnePlusOneForEveryAbilityOnCooldown_EachSlowing()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Cast("nuke", H(ctx.State, "alpha").Id)));
        At(kernel, 5, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), Dilation, H(ctx.State, "alpha").Id));
        int speedWithOne = 0;
        At(kernel, 8, ctx => speedWithOne = H(ctx.State, "beta").MoveSpeed);
        At(kernel, 9, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Cast("bolt", H(ctx.State, "alpha").Id)));

        kernel.RunTicks(14);

        Assert.Equal(270, speedWithOne);                        // the stack of its own and one for the nuke: 2 × 5%
        Assert.Equal(255, H(kernel.State, "beta").MoveSpeed);   // casting the bolt adds one
    }

    [Fact]
    public void Cooldowns_RunSlower()
    {
        var kernel = Arena(nukeCooldownMs: 2000);
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Cast("nuke", H(ctx.State, "alpha").Id)));
        At(kernel, 3, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), Dilation, H(ctx.State, "alpha").Id));
        long readyAtCast = 0;
        At(kernel, 4, ctx => readyAtCast = Ability(ctx.State, "nuke").CooldownReadyTick);

        kernel.RunTicks(4 + 60);

        // 60 ticks under a 50% slow move the cooldown 30 ticks later: half of the 2 s left after 2 s.
        Assert.InRange(Ability(kernel.State, "nuke").CooldownReadyTick, readyAtCast + 29, readyAtCast + 31);
    }

    [Fact]
    public void DamagePerSecond_ForEveryStack()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Cast("nuke", H(ctx.State, "alpha").Id)));
        At(kernel, 5, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), Dilation, H(ctx.State, "alpha").Id));

        kernel.RunTicks(5 + 3 * 30 + 2);

        // Two stacks for 3 s at 10 a stack a second.
        Assert.Equal(60, (Fixed.FromInt(2000) - H(kernel.State, "beta").Hp).ToDouble(), 0);
    }
}
