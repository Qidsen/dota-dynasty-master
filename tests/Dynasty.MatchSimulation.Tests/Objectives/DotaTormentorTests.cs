using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's Tormentor (game file `npc_dota_miniboss` and its abilities; Valve 7.38–7.41f; the project owner on 2026-10-28 where the
/// sources are silent): it cannot move or attack; a barrier of 3000 + 50 a minute regenerating 20 + 5 a minute per second guards
/// its 1 health; every second it reflects 30% + 1.5% a minute of the damage it took, split evenly among the heroes within
/// 1200 and whoever dealt it (a unit that is not a hero dies when no hero is among them); The Shining burns everything within
/// 1000 for 20 + 2 a minute magical a second while it was hit in the last 10 s. It stands on the side away from Roshan — the
/// bottom by day, the top by night — and moves with him; killed, every hero of the killing team gets 175 gold and the one with
/// the lowest net worth without a shard gets an Aghanim's Shard (all have one: 240 more each), and the heroes within 900 get
/// Alleviation; it comes back 10 minutes later. Here a «minute» is 1 s, day and night 30 s, the first spawn at 2 s, the next 10 s
/// after a kill.
/// </summary>
public class DotaTormentorTests
{
    private static readonly FixedVector2 Bottom = P(7000, 2000);
    private static readonly FixedVector2 Top = P(3000, 8000);

    private static RulesetDraft WithTormentor(int growthIntervalMs = 1000)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.DayNight = new DayNightRules(dayMs: 30000, nightMs: 30000);
        draft.Creeps.Add(new CreepType("tormentor", new UnitStats(1, Fixed.FromInt(20), 0, 0, 0, 0, 1000, 0, 0, 110, magicResistPercent: 55, visionRange: 1200)));
        draft.Items.Add(new ItemDefinition("aghanims_shard", "Aghanim's Shard", 1400, upgrade: "shard"));
        draft.Miniboss = new MinibossDefinition("tormentor", "tormentor", dayPosition: Bottom, nightPosition: Top, firstSpawnMs: 2000, respawnMs: 10000,
            teamGold: 175, rewardItemId: "aghanims_shard", teamGoldWithoutItem: 240, growthIntervalMs: growthIntervalMs,
            barrier: 3000, barrierPerInterval: 50, barrierRegen: Fixed.FromInt(20), barrierRegenPerInterval: Fixed.FromInt(5),
            reflectPercent: Fixed.FromInt(30), reflectPercentPerInterval: Fixed.FromRatio(3, 2), reflectRadius: 1200, reflectIntervalMs: 1000,
            shiningRadius: 1000, shiningDamage: Fixed.FromInt(20), shiningDamagePerInterval: Fixed.FromInt(2), shiningIntervalMs: 200, shiningAfterHitMs: 10000,
            deathModifier: new ModifierSpec("tormentor_alleviation", 15000, healPerSecondMaxHealthPercent: Fixed.FromRatio(9, 4)), deathModifierRadius: 900);
        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) =>
        draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid") });

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Puts the heroes at fixed spots each tick from the given one (they have no AI here, but keeps them still).</summary>
    private static void Place(SimulationKernel<MatchState> kernel, long tick, FixedVector2 alpha, FixedVector2 beta, FixedVector2 gamma) =>
        At(kernel, tick, ctx =>
        {
            H(ctx.State, "alpha").Position = alpha;
            H(ctx.State, "beta").Position = beta;
            H(ctx.State, "gamma").Position = gamma;
        });

    [Fact]
    public void TheTormentor_StandsAwayFromRoshan_AndMovesWithTheTimeOfDay()
    {
        var kernel = Start(WithTormentor());
        FixedVector2 early = default, byDay = default;
        At(kernel, 30, ctx => early = ctx.State.Miniboss?.Position ?? P(-1, -1));
        At(kernel, 30 * 3, ctx => byDay = ctx.State.Miniboss!.Position);

        kernel.RunTicks(30 * 31);

        Assert.Equal(P(-1, -1), early);   // not before its first spawn
        Assert.Equal(Bottom, byDay);
        Assert.Equal(Top, kernel.State.Miniboss!.Position);
    }

    [Fact]
    public void ItsBarrier_TakesTheDamage_GrowsWithTime_AndRegenerates()
    {
        var kernel = Start(WithTormentor(growthIntervalMs: 60000));
        Fixed barrierAtSpawn = Fixed.Zero, afterHit = Fixed.Zero, secondLater = Fixed.Zero;
        At(kernel, 30 * 2 + 1, ctx => barrierAtSpawn = ctx.State.Miniboss!.MinibossBarrier);
        At(kernel, 30 * 3, ctx =>
        {
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Miniboss!, Fixed.FromInt(1000), DamageType.Pure);
            afterHit = ctx.State.Miniboss!.MinibossBarrier;
        });
        At(kernel, 30 * 4, ctx => secondLater = ctx.State.Miniboss!.MinibossBarrier);

        kernel.RunTicks(30 * 4 + 1);

        Assert.Equal(Fixed.FromInt(3000), barrierAtSpawn);
        Assert.Equal(Fixed.FromInt(2000), afterHit);
        Assert.Equal(Fixed.FromInt(2020), secondLater);   // 20 a second
        Assert.Equal(Fixed.One, kernel.State.Miniboss!.Hp);

        var grown = Start(WithTormentor(growthIntervalMs: 1000));
        grown.RunTicks(30 * 5 + 1);
        Assert.Equal(Fixed.FromInt(3000 + 5 * 50), grown.State.Miniboss!.MinibossBarrier);   // full: no damage taken
    }

    [Fact]
    public void ItReflects_ThePercentOfTheTime_SplitAmongTheHeroesAround()
    {
        var kernel = Start(WithTormentor(growthIntervalMs: 60000));
        Place(kernel, 1, P(7300, 2000), P(6500, 2600), P(1000, 9000));   // alpha and beta within 1200, gamma far
        Fixed alphaBefore = Fixed.Zero, betaBefore = Fixed.Zero, gammaBefore = Fixed.Zero;
        At(kernel, 30 * 3, ctx =>
        {
            alphaBefore = H(ctx.State, "alpha").Hp;
            betaBefore = H(ctx.State, "beta").Hp;
            gammaBefore = H(ctx.State, "gamma").Hp;
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Miniboss!, Fixed.FromInt(1000), DamageType.Pure);
        });
        Fixed alphaTaken = Fixed.Zero, betaTaken = Fixed.Zero, gammaTaken = Fixed.Zero;
        At(kernel, 30 * 4 + 2, ctx =>
        {
            alphaTaken = alphaBefore - H(ctx.State, "alpha").Hp;
            betaTaken = betaBefore - H(ctx.State, "beta").Hp;
            gammaTaken = gammaBefore - H(ctx.State, "gamma").Hp;
        });

        kernel.RunTicks(30 * 4 + 3);

        // 30% of 1000, half each, and The Shining of the second after the hit (15 after 25% magic resistance); the heroes'
        // regeneration gives a little back.
        Assert.InRange(alphaTaken.ToIntFloor(), 160, 165);
        Assert.InRange(betaTaken.ToIntFloor(), 160, 165);
        Assert.True(gammaTaken <= Fixed.Zero, "gamma is out of range: " + gammaTaken);
    }

    [Fact]
    public void TheReflectedPart_GrowsWithTheTime()
    {
        var kernel = Start(WithTormentor(growthIntervalMs: 1000));
        Fixed pending = Fixed.Zero;
        At(kernel, 30 * 3 + 1, ctx =>
        {
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Miniboss!, Fixed.FromInt(1000), DamageType.Pure);
            pending = ctx.State.Miniboss!.MinibossReflected![(int)DamageType.Pure];
        });

        kernel.RunTicks(30 * 3 + 2);

        Assert.Equal(Fixed.FromInt(345), pending);   // 30% + 3 × 1.5%
    }

    [Fact]
    public void AUnitThatIsNotAHero_DiesOfTheReflection_WithNoHeroAround()
    {
        var kernel = Start(WithTormentor());
        Place(kernel, 1, P(1000, 9000), P(1200, 9000), P(1400, 9000));
        MatchUnit? creep = null;
        At(kernel, 30 * 3, ctx =>
        {
            creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Dawn, ctx.State.Ruleset.FindCreepType("melee")!, ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
            creep.Position = P(7200, 2000);
            Damage.Deal(ctx, creep.Id, ctx.State.Miniboss!, Fixed.FromInt(10), DamageType.Physical);
        });

        kernel.RunTicks(30 * 4 + 3);

        Assert.False(creep!.IsAlive);
    }

    [Fact]
    public void TheShining_BurnsAroundIt_OnlyWithinTenSecondsOfAHit()
    {
        var kernel = Start(WithTormentor(growthIntervalMs: 60000));
        Place(kernel, 1, P(1000, 9000), P(1200, 9000), P(7500, 2000));   // gamma within 1000
        EntityIdHolder tormentor = new();
        At(kernel, 30 * 3, ctx => tormentor.Id = ctx.State.Miniboss!.Id.Value);
        At(kernel, 30 * 5, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Miniboss!, Fixed.FromInt(1), DamageType.Pure));

        kernel.RunTicks(30 * 17);

        var burns = kernel.TimedEventsOf<DamageDealt>().Where(e => e.Event.Attacker.Value == tormentor.Id && e.Event.Target == H(kernel.State, "gamma").Id).ToList();
        Assert.DoesNotContain(burns, e => e.Tick < 30 * 5);
        Assert.Contains(burns, e => e.Tick > 30 * 5 && e.Tick < 30 * 6);
        Assert.DoesNotContain(burns, e => e.Tick > 30 * 15 + 6);
        Fixed total = burns.Where(e => e.Tick > 30 * 5 && e.Tick <= 30 * 6).Aggregate(Fixed.Zero, (sum, e) => sum + e.Event.Amount);
        Assert.InRange(total.ToIntFloor(), 12, 15);   // 20 a second less gamma's 25% magic resistance
    }

    private sealed class EntityIdHolder
    {
        public long Id { get; set; } = -1;
    }

    [Fact]
    public void KillingIt_PaysTheTeam_GivesTheShardToTheHeroWithTheLowestNetWorth_AndAlleviationAround()
    {
        var kernel = Start(WithTormentor(growthIntervalMs: 60000));
        Place(kernel, 1, P(7300, 2000), P(1000, 9000), P(7600, 2000));   // alpha and gamma within 900
        Fixed alphaGold = Fixed.Zero, betaGold = Fixed.Zero, gammaGold = Fixed.Zero;
        At(kernel, 30 * 3, ctx =>
        {
            H(ctx.State, "beta").Hero!.Gold = Fixed.Zero;
            alphaGold = H(ctx.State, "alpha").Hero!.Gold;
            betaGold = H(ctx.State, "beta").Hero!.Gold;
            gammaGold = H(ctx.State, "gamma").Hero!.Gold;
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Miniboss!, Fixed.FromInt(99999), DamageType.Pure);
        });

        kernel.RunTicks(30 * 3 + 3);

        MatchState state = kernel.State;
        Assert.Null(state.Miniboss);
        Assert.Contains(H(state, "beta").Hero!.Inventory, i => i.Definition.Id == "aghanims_shard");
        Assert.DoesNotContain(H(state, "alpha").Hero!.Inventory, i => i.Definition.Id == "aghanims_shard");
        Assert.InRange((H(state, "alpha").Hero!.Gold - alphaGold).ToIntFloor(), 175, 177);   // and the passive gold of the ticks
        Assert.InRange((H(state, "beta").Hero!.Gold - betaGold).ToIntFloor(), 175, 177);
        Assert.InRange((H(state, "gamma").Hero!.Gold - gammaGold).ToIntFloor(), 0, 2);
        Assert.Contains(H(state, "alpha").Modifiers, m => m.Spec.Key == "tormentor_alleviation");
        Assert.Contains(H(state, "gamma").Modifiers, m => m.Spec.Key == "tormentor_alleviation");
        Assert.DoesNotContain(H(state, "beta").Modifiers, m => m.Spec.Key == "tormentor_alleviation");
    }

    [Fact]
    public void WithAShardOnEveryHero_TheTeamGetsMoreGoldInstead_AndItComesBackLater()
    {
        var kernel = Start(WithTormentor(growthIntervalMs: 60000));
        Fixed alphaGold = Fixed.Zero;
        At(kernel, 30 * 3, ctx =>
        {
            ItemDefinition shard = ctx.State.Ruleset.FindItem("aghanims_shard")!;
            HeroShop.Give(ctx, H(ctx.State, "alpha"), shard);
            HeroShop.Give(ctx, H(ctx.State, "beta"), shard);
            alphaGold = H(ctx.State, "alpha").Hero!.Gold;
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Miniboss!, Fixed.FromInt(99999), DamageType.Pure);
        });
        Fixed alphaAfter = Fixed.Zero;
        At(kernel, 30 * 3 + 2, ctx => alphaAfter = H(ctx.State, "alpha").Hero!.Gold);

        kernel.RunTicks(30 * 14);

        Assert.InRange((alphaAfter - alphaGold).ToIntFloor(), 175 + 240, 175 + 242);
        Assert.Single(H(kernel.State, "alpha").Hero!.Inventory, i => i.Definition.Id == "aghanims_shard");
        Assert.NotNull(kernel.State.Miniboss);   // back 10 s after the kill
        Assert.Equal(Bottom, kernel.State.Miniboss!.Position);
    }

    /// <summary>
    /// The team AI takes the Tormentor (its timer is public in Dota, like Roshan's) from a set minute, with at least three heroes
    /// up and no fewer than the enemy, no enemy seen near it, and someone still without a shard. Four against four; the enemy far
    /// away and unseen.
    /// </summary>
    private static SimulationKernel<MatchState> TeamsAround(bool allHaveShards, int barrierRegen = 20)
    {
        var draft = WithTormentor(growthIntervalMs: 60000);
        draft.Miniboss = new MinibossDefinition("tormentor", "tormentor", dayPosition: P(5000, 2500), nightPosition: P(5000, 2500), firstSpawnMs: 0, respawnMs: 600000,
            teamGold: 175, rewardItemId: "aghanims_shard", teamGoldWithoutItem: 240, growthIntervalMs: 60000, barrier: 600, barrierPerInterval: 0,
            barrierRegen: Fixed.FromInt(barrierRegen), barrierRegenPerInterval: Fixed.Zero, reflectPercent: Fixed.FromInt(30), reflectPercentPerInterval: Fixed.Zero,
            reflectRadius: 1200, reflectIntervalMs: 1000, shiningRadius: 1000, shiningDamage: Fixed.FromInt(20), shiningDamagePerInterval: Fixed.Zero,
            shiningIntervalMs: 200, shiningAfterHitMs: 10000);
        foreach (string id in new[] { "delta", "epsilon", "zeta", "eta", "theta" })
            draft.Heroes.Add(Hero(id));
        var kernel = draft.StartWithHeroes(
            new[] { ("alpha", "mid"), ("beta", "mid"), ("gamma", "mid"), ("delta", "mid") },
            new[] { ("epsilon", "mid"), ("zeta", "mid"), ("eta", "mid"), ("theta", "mid") });
        At(kernel, 0, ctx =>
        {
            foreach (string hero in new[] { "alpha", "beta", "gamma", "delta" })
            {
                HeroProgression.GrantXp(ctx, H(ctx.State, hero), 2000);
                if (allHaveShards)
                    HeroShop.Give(ctx, H(ctx.State, hero), ctx.State.Ruleset.FindItem("aghanims_shard")!);
            }

            int i = 0;
            foreach (string hero in new[] { "epsilon", "zeta", "eta", "theta" })
                H(ctx.State, hero).Position = P(9000, 8000 + 200 * i++);
        });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, MinibossFromMinute = 0, BossAtEvenNumbersFromMinute = 99 };
        foreach (string hero in new[] { "epsilon", "zeta", "eta", "theta" })
            settings.Uncontrolled.Add(hero);
        HeroAi.Attach(kernel, settings);
        return kernel;
    }

    [Fact]
    public void TeamAi_TakesTheTormentor_WhenSomeoneHasNoShard()
    {
        var kernel = TeamsAround(allHaveShards: false);

        kernel.RunTicks(30 * 60);

        Assert.Equal(Team.Dawn, Assert.Single(kernel.EventsOf<MinibossKilled>()).Team);
    }

    [Fact]
    public void TeamAi_LeavesTheTormentor_WhenItCannotOutpaceItsBarrier()
    {
        var kernel = TeamsAround(allHaveShards: false, barrierRegen: 500);

        kernel.RunTicks(30 * 30);

        Assert.Equal(-1, kernel.State.Miniboss!.MinibossLastHitTick);   // never even hit
    }

    [Fact]
    public void TeamAi_LeavesTheTormentor_WhenEveryoneHasAShard()
    {
        var kernel = TeamsAround(allHaveShards: true);

        kernel.RunTicks(30 * 60);

        Assert.Empty(kernel.EventsOf<MinibossKilled>());
    }

    [Fact]
    public void Validation_RejectsAnUnknownUnitOrItem_AndPlacesOutsideTheMap_AndItIsPartOfTheIdentity()
    {
        var bad = WithTormentor();
        bad.Miniboss = new MinibossDefinition("tormentor", "golem", dayPosition: P(70000, 2000), nightPosition: Top, firstSpawnMs: 2000, respawnMs: 10000,
            teamGold: 175, rewardItemId: "blessing", teamGoldWithoutItem: 240, growthIntervalMs: 1000, barrier: 3000, barrierPerInterval: 50,
            barrierRegen: Fixed.FromInt(20), barrierRegenPerInterval: Fixed.FromInt(5), reflectPercent: Fixed.FromInt(30), reflectPercentPerInterval: Fixed.Zero,
            reflectRadius: 1200, reflectIntervalMs: 1000, shiningRadius: 1000, shiningDamage: Fixed.FromInt(20), shiningDamagePerInterval: Fixed.Zero,
            shiningIntervalMs: 200, shiningAfterHitMs: 10000);

        IReadOnlyList<string> errors = bad.Build().Validate();

        Assert.Contains(errors, e => e.Contains("golem"));
        Assert.Contains(errors, e => e.Contains("blessing"));
        Assert.Contains(errors, e => e.Contains("tormentor") && e.Contains("map"));
        var none = WithTormentor();
        none.Miniboss = null;
        Assert.NotEqual(none.Build().ToDescriptor().Fingerprint, WithTormentor().Build().ToDescriptor().Fingerprint);
        Assert.NotEqual(WithTormentor(2000).Build().ToDescriptor().Fingerprint, WithTormentor().Build().ToDescriptor().Fingerprint);
    }
}
