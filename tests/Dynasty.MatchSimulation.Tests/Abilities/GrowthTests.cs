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
/// Lil' Shredder's base attack time (Dota 7.41f data and official notes: it sets the base attack time for the duration) and
/// Upheaval (the project owner: damage per second grows by 10 for every second of the channel up to the level's maximum; the slow
/// grows the same way; the official note: by the length of the channel, not by the time an enemy stood in the area).
/// </summary>
public class GrowthTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena(RulesetDraft draft, string[]? alphaAbilities = null)
    {
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1700, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? alphaAbilities : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5300, 5000);
            H(ctx.State, "gamma").Position = P(2000, 2000);
        });
        return kernel;
    }

    [Fact]
    public void BaseAttackTime_IsSetByTheModifier()
    {
        var kernel = Arena(new RulesetDraft().WithoutWaves());
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("shredder", 6000, baseAttackTimeMs: 1000, attackSpeedBonus: 100), H(ctx.State, "alpha").Id));

        kernel.RunTicks(3);

        Assert.Equal(15, H(kernel.State, "alpha").AttackIntervalTicks);   // 1 s at +100% attack speed, not 1.7 s
    }

    /// <summary>A 5-second Upheaval pulsing every second from its start: at 0 s nothing yet, at 1, 2, 3 and 4 s 10, 20, 30 and 35 (the maximum).</summary>
    private static AbilityDefinition Upheaval() => new AbilityDefinition("upheaval", "Upheaval", AbilityTargeting.Point, TargetTeam.Enemy, 1000, 0, new[] { 30000 }, new[] { 0 }, new[] { 1 },
        Array.Empty<EffectDefinition>(), channel: new ChannelSpec(new[] { 5000 }, 1000, new[]
        {
            EffectDefinition.Area(500, AreaCenter.Point, TargetTeam.Enemy,
                EffectDefinition.Modifier(new ModifierSpec("upheaval_slow", 1000, moveSpeedPercent: -55, growsWithChannelMs: 5000)),
                EffectDefinition.ChannelGrowthDamage(DamageType.Pure, new[] { 35 }, 10)),
        }, Array.Empty<EffectDefinition>(), null));

    [Fact]
    public void Upheaval_DamageAndSlowGrowWithTheChannel()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(Upheaval());
        var kernel = Arena(draft, new[] { "upheaval" });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastAtPoint("upheaval", P(5300, 5000))));
        var slowAt = new List<int>();
        foreach (int second in new[] { 1, 2, 3, 4 })
            At(kernel, 6 + second * 30, ctx => slowAt.Add(300 - H(ctx.State, "beta").MoveSpeed));

        kernel.RunTicks(6 + 5 * 30);

        MatchUnit beta = H(kernel.State, "beta");
        List<double> hits = kernel.EventsOf<DamageDealt>().Where(d => d.Target == beta.Id).Select(d => d.Amount.ToDouble()).ToList();
        Assert.Equal(new[] { 10.0, 20.0, 30.0, 35.0 }, hits.Select(h => System.Math.Round(h)));
        Assert.Equal(new[] { 33, 66, 99, 132 }, slowAt);   // 11% of 300 more each second (55% over 5 s)
    }

    [Fact]
    public void Upheaval_AnEnemyComingLate_GetsTheSlowOfTheChannelSoFar()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(Upheaval());
        var kernel = Arena(draft, new[] { "upheaval" });
        At(kernel, 1, ctx => H(ctx.State, "beta").Position = P(8000, 5000));   // out of the area
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastAtPoint("upheaval", P(5300, 5000))));
        At(kernel, 3 + 3 * 30 - 5, ctx => H(ctx.State, "beta").Position = P(5300, 5000));   // in just before the third second

        kernel.RunTicks(3 + 3 * 30 + 2);

        Assert.Equal(300 - 99, H(kernel.State, "beta").MoveSpeed);
    }
}
