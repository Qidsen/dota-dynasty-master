using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

/// <summary>
/// Roshan's items in the AI's hands: Cheese (a big instant heal) is kept for a fight — eaten below 25% health with enemy
/// heroes in sight, not to top up; a Refresher Shard is used in a fight while the ultimate is on cooldown, to cast it again.
/// </summary>
public class RoshanItemAiTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena(string item, bool enemyNear, Action<SimulationContext<MatchState>> setup)
    {
        var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "finale").WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("item_cheese", "Cheese", AbilityTargeting.NoTarget, TargetTeam.Ally, castRange: 0, castPointMs: 0,
            cooldownMs: new[] { 40000 }, manaCost: new[] { 0 }, requiredHeroLevel: new[] { 1 }, effects: new[] { EffectDefinition.Heal(2500), EffectDefinition.RestoreMana(1500) }));
        draft.Abilities.Add(new AbilityDefinition("item_refresher_shard", "Refresher Shard", AbilityTargeting.NoTarget, TargetTeam.Ally, castRange: 0, castPointMs: 0,
            cooldownMs: new[] { 180000 }, manaCost: new[] { 200 }, requiredHeroLevel: new[] { 1 }, effects: new[] { EffectDefinition.RefreshCooldowns() }));
        draft.Items.Add(new ItemDefinition("item_cheese", "Cheese", 0, activeAbilityId: "item_cheese", charges: 1));
        draft.Items.Add(new ItemDefinition("item_refresher_shard", "Refresher Shard", 0, activeAbilityId: "item_refresher_shard", charges: 1));
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("beta");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(4000, 2000);
            H(ctx.State, "beta").Position = enemyNear ? P(4300, 2000) : P(9000, 9000);
            HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 100000);   // level 6+: the ultimate learned
            HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem(item)!);
            setup(ctx);
        });
        HeroAi.Attach(kernel, settings);
        return kernel;
    }

    private static bool Used(SimulationKernel<MatchState> kernel, string ability) =>
        kernel.EventsOf<AbilityCast>().Any(c => c.Caster == H(kernel.State, "alpha").Id && c.AbilityId == ability);

    [Theory]
    [InlineData(20, true, true)]
    [InlineData(20, false, false)]   // no fight: kept
    [InlineData(50, true, false)]    // not low enough
    public void Cheese_IsEatenLowInAFight(int hpPercent, bool enemyNear, bool expected)
    {
        var kernel = Arena("item_cheese", enemyNear, ctx => H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp * hpPercent / 100));
        kernel.RunTicks(20);

        Assert.Equal(expected, Used(kernel, "item_cheese"));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]   // the ultimate is ready: nothing to refresh
    [InlineData(true, false, false)]   // no fight
    public void ARefresherShard_IsUsedInAFight_WhileTheUltimateIsOnCooldown(bool ultimateJustCast, bool enemyNear, bool expected)
    {
        var kernel = Arena("item_refresher_shard", enemyNear, ctx =>
        {
            AbilityState finale = H(ctx.State, "alpha").Hero!.Ability("finale")!;
            if (ultimateJustCast)
                finale.CooldownReadyTick = ctx.State.Rate.TicksFor(finale.Definition.CooldownAt(finale.Level));
        });
        kernel.RunTicks(20);

        Assert.Equal(expected, Used(kernel, "item_refresher_shard"));
    }
}
