using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

/// <summary>
/// Spells on objectives (backlog B-162): heroes taking the boss or the miniboss use their damaging spells and item actives on it,
/// and a team counts the damage of its ready spells when judging whether it can break the miniboss's barrier.
/// </summary>
public class ObjectiveSpellTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static AbilityDefinition Nuke(int damage) => new("nuke", "Nuke", AbilityTargeting.Unit, TargetTeam.Enemy, 600, 0,
        new[] { 3000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Damage(DamageType.Pure, damage) });

    [Fact]
    public void TakingTheBoss_TheHeroesCastTheirDamagingSpellsOnIt()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("colossus", new UnitStats(5000, Fixed.FromInt(5), 270, 60, 60, 150, 1000, 300, 400, 60, visionRange: 600)));
        draft.Boss = new BossDefinition("colossus", "colossus", P(5000, 2500), leashRadius: 600, firstSpawnMs: 0, respawnMinMs: 60000, respawnMaxMs: 60000, teamGold: 150, reward: null);
        draft.Abilities.Add(Nuke(100));
        for (int i = 0; i < draft.Heroes.Count; i++)
            draft.Heroes[i] = Hero(draft.Heroes[i].Id, abilities: new[] { "nuke" });
        draft.Heroes.Add(Hero("delta"));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid"), ("gamma", "mid") }, new[] { ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            foreach (string hero in new[] { "alpha", "beta", "gamma" })
                HeroProgression.GrantXp(ctx, H(ctx.State, hero), 2000);
            Damage.Deal(ctx, EntityId.None, H(ctx.State, "delta"), Fixed.FromInt(99999), DamageType.Pure);
        });
        HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 });

        kernel.RunTicks(30 * 40);

        EntityId bossId = kernel.EventsOf<BossSpawned>().First().Boss;
        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "nuke" && c.Target == bossId);
    }

    /// <summary>
    /// Four against four, the enemy far and unseen; the Tormentor's barrier regenerates faster than the heroes' attacks hurt it, but
    /// their nukes together break it at once.
    /// </summary>
    [Fact]
    public void TheTeam_TakesTheMiniboss_WhenItsSpellsBreakTheBarrier()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("tormentor", new UnitStats(1, Fixed.FromInt(20), 0, 0, 0, 0, 1000, 0, 0, 110, magicResistPercent: 55, visionRange: 1200)));
        draft.Items.Add(new ItemDefinition("aghanims_shard", "Aghanim's Shard", 1400, upgrade: "shard"));
        draft.Miniboss = new MinibossDefinition("tormentor", "tormentor", dayPosition: P(5000, 2500), nightPosition: P(5000, 2500), firstSpawnMs: 0, respawnMs: 600000,
            teamGold: 175, rewardItemId: "aghanims_shard", teamGoldWithoutItem: 240, growthIntervalMs: 60000, barrier: 900, barrierPerInterval: 0,
            barrierRegen: Fixed.FromInt(500), barrierRegenPerInterval: Fixed.Zero, reflectPercent: Fixed.FromInt(10), reflectPercentPerInterval: Fixed.Zero,
            reflectRadius: 1200, reflectIntervalMs: 1000, shiningRadius: 1000, shiningDamage: Fixed.FromInt(5), shiningDamagePerInterval: Fixed.Zero,
            shiningIntervalMs: 200, shiningAfterHitMs: 10000);
        draft.Abilities.Add(Nuke(400));
        foreach (string id in new[] { "delta", "epsilon", "zeta", "eta", "theta" })
            draft.Heroes.Add(Hero(id));
        for (int i = 0; i < draft.Heroes.Count; i++)
            draft.Heroes[i] = Hero(draft.Heroes[i].Id, abilities: new[] { "nuke" });
        var kernel = draft.StartWithHeroes(
            new[] { ("alpha", "mid"), ("beta", "mid"), ("gamma", "mid"), ("delta", "mid") },
            new[] { ("epsilon", "mid"), ("zeta", "mid"), ("eta", "mid"), ("theta", "mid") });
        At(kernel, 0, ctx =>
        {
            foreach (string hero in new[] { "alpha", "beta", "gamma", "delta" })
                HeroProgression.GrantXp(ctx, H(ctx.State, hero), 2000);
            int i = 0;
            foreach (string hero in new[] { "epsilon", "zeta", "eta", "theta" })
                H(ctx.State, hero).Position = P(9000, 8000 + 200 * i++);
        });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, MinibossFromMinute = 0, BossAtEvenNumbersFromMinute = 99 };
        foreach (string hero in new[] { "epsilon", "zeta", "eta", "theta" })
            settings.Uncontrolled.Add(hero);
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 60);

        Assert.Equal(Team.Dawn, Assert.Single(kernel.EventsOf<MinibossKilled>()).Team);
    }
}
