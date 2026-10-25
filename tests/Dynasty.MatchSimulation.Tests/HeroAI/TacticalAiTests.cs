using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

/// <summary>Scenario tests of the tactical/team AI layers on small test maps (no creeps unless stated).</summary>
public class TacticalAiTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Skirmish(
        (string Hero, int X, int Y)[] dawn,
        (string Hero, int X, int Y)[] dusk,
        HeroAiSettings? settings = null,
        Action<SimulationContext<MatchState>>? setup = null,
        RulesetDraft? draft = null,
        string lane = "mid")
    {
        draft ??= TestAbilities.Draft();
        var kernel = draft.StartWithHeroes(dawn.Select(d => (d.Hero, lane)), dusk.Select(d => (d.Hero, lane)));
        At(kernel, 0, ctx =>
        {
            foreach (var (hero, x, y) in dawn.Concat(dusk))
                H(ctx.State, hero).Position = P(x, y);
            setup?.Invoke(ctx);
        });
        HeroAi.Attach(kernel, settings ?? new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 });
        return kernel;
    }

    /// <summary>Settings without reaction noise where the given heroes are left uncontrolled (they stand still).</summary>
    private static HeroAiSettings Idle(params string[] heroes)
    {
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        foreach (string hero in heroes)
            settings.Uncontrolled.Add(hero);
        return settings;
    }

    /// <summary>Roots a hero permanently (it can still fight back).</summary>
    private static void Pin(SimulationContext<MatchState> ctx, string hero) =>
        Modifiers.Apply(ctx, H(ctx.State, hero), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);

    private static double DistanceToFountain(SimulationKernel<MatchState> kernel, string hero, Team team) =>
        FixedVector2.Distance(H(kernel.State, hero).Position, kernel.State.Ruleset.Map.FountainOf(team)!.Position).ToDouble();

    [Fact]
    public void Outnumbered_HealthyHeroRetreats()
    {
        var kernel = Skirmish(
            new[] { ("alpha", 5000, 2000) },
            new[] { ("beta", 5600, 2000), ("delta", 5600, 2200), ("epsilon", 5600, 1800) });
        double before = FixedVector2.Distance(P(5000, 2000), P(200, 5000)).ToDouble();

        kernel.RunTicks(90);

        Assert.True(DistanceToFountain(kernel, "alpha", Team.Dawn) < before - 300, "alpha should back off toward its base");
        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);
    }

    [Fact]
    public void Advantage_HeroesEngageTogether()
    {
        var kernel = Skirmish(
            new[] { ("alpha", 5000, 2000), ("gamma", 5000, 2200) },
            new[] { ("beta", 5600, 2100) },
            setup: ctx => Pin(ctx, "beta"));   // outnumbered, it would flee at equal speed and never be caught

        kernel.RunTicks(150);

        EntityId beta = H(kernel.State, "beta").Id;
        var attackers = kernel.EventsOf<AttackStarted>().Where(a => a.Target == beta).Select(a => a.Attacker).ToHashSet();
        Assert.Contains(H(kernel.State, "alpha").Id, attackers);
        Assert.Contains(H(kernel.State, "gamma").Id, attackers);
    }

    [Fact]
    public void Teamfight_FocusesTheWeakestVisibleEnemy()
    {
        var kernel = Skirmish(
            new[] { ("alpha", 5000, 2000), ("gamma", 5000, 2200), ("delta", 5000, 1800) },
            new[] { ("beta", 5500, 2000), ("epsilon", 5500, 2300) },
            setup: ctx =>
            {
                H(ctx.State, "epsilon").Hp = Fixed.FromInt(150);
                Pin(ctx, "beta");
                Pin(ctx, "epsilon");
            });

        kernel.RunTicks(60);

        EntityId weak = H(kernel.State, "epsilon").Id;
        var firstTargets = kernel.EventsOf<AttackStarted>()
            .Where(a => new[] { "alpha", "gamma", "delta" }.Any(h => H(kernel.State, h).Id == a.Attacker))
            .GroupBy(a => a.Attacker).Select(g => g.First().Target).ToList();
        Assert.NotEmpty(firstTargets);
        Assert.All(firstTargets, t => Assert.Equal(weak, t));
    }

    [Fact]
    public void ThreatenedTower_DrawsDefenders()
    {
        var draft = TestAbilities.Draft();
        draft.Lanes.Add(new LaneDefinition("side", new[] { P(1000, 1000), P(9000, 1000) }));
        draft.StructureTypes[0] = new StructureType("tower", TowerStats(dmg: 1));   // the attacker must survive long enough to be a threat
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("beta");
        var kernel = Skirmish(
            new[] { ("alpha", 2500, 1000) },
            new[] { ("beta", 3300, 5000) },
            settings,
            draft: draft,
            lane: "side",   // its lane rally is not the threatened tower, so only the defend plan brings it there
            setup: ctx =>
            {
                // Dusk hero is already hitting the Dawn tower.
                HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(ctx.State.FindStructure("dawn.mid.t1")!.Id));
            });
        double start = FixedVector2.Distance(P(2500, 1000), P(3000, 5000)).ToDouble();

        kernel.RunTicks(240);

        double now = FixedVector2.Distance(H(kernel.State, "alpha").Position, P(3000, 5000)).ToDouble();
        Assert.True(now < start - 1500, $"alpha should rotate to defend (distance {start:0} -> {now:0})");
    }

    /// <summary>Two Dawn heroes against nobody, Dawn mid creeps sieging the Dusk mid tower, alpha's own lane wave holding near its spawn.</summary>
    private static SimulationKernel<MatchState> SiegeScenario(HeroAiSettings settings)
    {
        var draft = TestAbilities.Draft();
        draft.DuskWave.Clear();
        draft.DawnWave.Add(new WaveEntry("melee", 2));
        draft.Lanes.Add(new LaneDefinition("side", new[] { P(1000, 2000), P(9000, 2000) }));
        draft.StructureTypes[0] = new StructureType("tower", TowerStats(dmg: 1));   // the sieging creeps must survive
        // Closer to the Dawn fountain than the mid tower, so a plain "nearest structure" choice picks it.
        draft.Placements.Add(new StructurePlacement("dusk.side.t1", Team.Dusk, "tower", P(5500, 2000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));
        settings.Personalities["alpha"] = new HeroPersonality { Greed = 0.3 };   // not a carry holding out for farm
        settings.Personalities["gamma"] = new HeroPersonality { Greed = 0.3 };
        var kernel = Skirmish(
            new[] { ("alpha", 3000, 3500), ("gamma", 3000, 3600) },
            Array.Empty<(string, int, int)>(),
            settings,
            draft: draft,
            lane: "side");
        At(kernel, 1, ctx =>
        {
            // Mid creeps are sieging the mid tower; the side wave (alpha's own lane) holds near its spawn.
            int i = 0;
            foreach (MatchUnit creep in ctx.State.Creeps(Team.Dawn))
            {
                Modifiers.Apply(ctx, creep, ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
                if (creep.Position.Y > Fixed.FromInt(3500))
                    creep.Position = P(6500, 4900 + 60 * i++);
            }
        });

        return kernel;
    }

    [Fact]
    public void Push_JoinsTheSiegeOurCreepsAlreadyHave()
    {
        var kernel = SiegeScenario(new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 });

        kernel.RunTicks(240);

        FixedVector2 alpha = H(kernel.State, "alpha").Position;
        double toMid = FixedVector2.Distance(alpha, P(7000, 5000)).ToDouble();
        double toSide = FixedVector2.Distance(alpha, P(5500, 2000)).ToDouble();
        Assert.True(toMid < toSide, $"alpha should push the sieged mid tower (mid {toMid:0}, side {toSide:0})");
    }

    [Fact]
    public void LaningPhase_ATwoHeroAdvantageDoesNotPullHeroesOutOfLane()
    {
        var kernel = SiegeScenario(new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 10 });

        kernel.RunTicks(240);

        FixedVector2 alpha = H(kernel.State, "alpha").Position;
        double toMid = FixedVector2.Distance(alpha, P(7000, 5000)).ToDouble();
        double toOwnWave = FixedVector2.Distance(alpha, P(1020, 2000)).ToDouble();
        Assert.True(toOwnWave < toMid, $"alpha should stay with its lane wave (own wave {toOwnWave:0}, mid tower {toMid:0})");
    }

    [Fact]
    public void Support_PlacesObserverWardsAtWardSpots()
    {
        var draft = TestAbilities.Draft();
        draft.Creeps.Add(new CreepType("observer_ward", new UnitStats(4, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 8, visionRange: 1600, innateFlags: ModifierFlags.Invisible)));
        draft.Abilities.Add(new AbilityDefinition("place_observer", "Place Observer", AbilityTargeting.Point, TargetTeam.Ally, 500, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.SummonAtPoint("observer_ward", 360000, 1) }));
        draft.Items.Add(new ItemDefinition("observer_ward", "Observer Ward", 75, activeAbilityId: "place_observer", charges: 1));
        draft.Heroes[draft.Heroes.FindIndex(h => h.Id == "gamma")] = new HeroDefinition("gamma", "gamma", new[] { "support" }, HeroStats(), 80,
            Fixed.FromRatio(3, 10), 3, 300, 40, Fixed.FromInt(2), Fixed.One);
        var kernel = draft.StartWithHeroes(new[] { ("gamma", "mid") }, Array.Empty<(string, string)>());
        HeroAi.Attach(kernel);

        kernel.RunTicks(60 * 30);

        MatchUnit ward = Assert.Single(kernel.State.Units, u => u.TypeId == "observer_ward");
        Assert.Equal(Team.Dawn, ward.Team);
        Assert.True(ward.Position.X > Fixed.FromInt(3000), "ward should watch the lane, not the base");
    }

    [Fact]
    public void Initiator_OpensWithItsAreaDisableOnAGroup()
    {
        var draft = TestAbilities.Draft()
            .WithHeroAbilities("alpha", "quake")
            .WithHeroAbilities("gamma");
        draft.Heroes[draft.Heroes.FindIndex(h => h.Id == "alpha")] = new HeroDefinition("alpha", "alpha", new[] { "initiator" }, HeroStats(), 80,
            Fixed.FromRatio(3, 10), 3, 300, 40, Fixed.FromInt(2), Fixed.One, abilityIds: new[] { "quake" });
        var kernel = Skirmish(
            new[] { ("alpha", 5000, 2000), ("gamma", 5300, 1950), ("delta", 5300, 2150) },   // followers start closer to the enemy than the initiator
            new[] { ("beta", 5600, 2000), ("epsilon", 5650, 2150) },
            Idle("beta", "epsilon"),   // standing still but not rooted: a rooted enemy already counts as "initiated on"
            draft: draft);

        kernel.RunTicks(150);

        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "quake");
        long quakeTick = kernel.TimedEventsOf<AbilityCast>().First(c => c.Event.AbilityId == "quake").Tick;
        HashSet<EntityId> followers = new[] { "gamma", "delta" }.Select(h => H(kernel.State, h).Id).ToHashSet();
        var firstFollowerHit = kernel.TimedEventsOf<DamageDealt>().Where(d => followers.Contains(d.Event.Attacker)).Select(d => d.Tick).DefaultIfEmpty(long.MaxValue).Min();
        Assert.True(firstFollowerHit >= quakeTick, "followers should wait for the initiation");
    }

    [Fact]
    public void Personality_AggressiveHeroTakesEvenFightsCautiousOneDoesNot()
    {
        int AlphaAttacks(double aggression)
        {
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
            settings.Personalities["alpha"] = new HeroPersonality { Aggression = aggression };
            settings.Personalities["beta"] = new HeroPersonality { Aggression = 0.5 };
            var kernel = Skirmish(new[] { ("alpha", 5000, 2000) }, new[] { ("beta", 5700, 2000) }, settings, setup: ctx => Pin(ctx, "beta"));
            kernel.RunTicks(120);
            return kernel.EventsOf<AttackStarted>().Count(a => a.Attacker == H(kernel.State, "alpha").Id);
        }

        Assert.True(AlphaAttacks(1.6) > 0);
        Assert.Equal(0, AlphaAttacks(0.6));
    }

    /// <summary>
    /// Laning, nothing to last hit, the enemy laner in attack range; the Dusk creep stands either far away
    /// or next to alpha (hitting the enemy hero there draws its aggro). Beta is pinned and not controlled.
    /// </summary>
    private static bool AlphaTrades(int harassPercent, int carelessPercent, bool creepNear)
    {
        var kernel = new RulesetDraft().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            ctx.State.Creeps(Team.Dusk).Single().Position = creepNear ? P(5250, 5300) : P(9000, 9000);
            ctx.State.Creeps(Team.Dawn).Single().Position = P(500, 9500);
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5140, 5000);
            Pin(ctx, "beta");
        });
        var settings = Idle("beta");
        settings.Personalities["alpha"] = new HeroPersonality();   // even odds: no fight, nothing to poke with
        settings.SkillsOf(Team.Dawn)["alpha"] = new HeroSkill { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaneHarassPercent = harassPercent, CarelessTradePercent = carelessPercent };
        HeroAi.Attach(kernel, settings);
        kernel.RunTicks(30);
        EntityId alpha = H(kernel.State, "alpha").Id, beta = H(kernel.State, "beta").Id;
        return kernel.EventsOf<AttackStarted>().Any(a => a.Attacker == alpha && a.Target == beta);
    }

    [Theory]
    [InlineData(100, 0, false, true)]    // a free trade is taken
    [InlineData(0, 0, false, false)]     // ... only by a player who looks for it
    [InlineData(100, 0, true, false)]    // a disciplined player does not trade into creep aggro
    [InlineData(100, 100, true, true)]   // a careless one does
    public void Laner_TradesHitsWithTheEnemyLaner_MindingCreepAggro(int harassPercent, int carelessPercent, bool creepNear, bool trades)
    {
        Assert.Equal(trades, AlphaTrades(harassPercent, carelessPercent, creepNear));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(0, false)]
    public void FreeHeroAfterLaning_PushesTheWave_AsOftenAsThePlayerDoes(int wavePushPercent, bool pushes)
    {
        // A Dusk creep at full health mid-lane, no enemy hero around, laning over: a player who plays
        // for objectives shoves the wave instead of waiting for last hits (ObjectiveControl).
        var kernel = new RulesetDraft().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx =>
        {
            ctx.State.Creeps(Team.Dusk).Single().Position = P(5000, 5000);
            ctx.State.Creeps(Team.Dawn).Single().Position = P(500, 9500);
            H(ctx.State, "alpha").Position = P(4800, 5000);
        });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.SkillsOf(Team.Dawn)["alpha"] = new HeroSkill { ReactionJitterTicks = 0, MisjudgementPercent = 0, WavePushPercent = wavePushPercent };
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30);

        EntityId alpha = H(kernel.State, "alpha").Id;
        Assert.Equal(pushes, kernel.EventsOf<AttackStarted>().Any(a => a.Attacker == alpha));
    }

    [Fact]
    public void HeroNearAnOwnTower_ClearsTheEnemyWaveHittingIt()
    {
        // A Dusk creep at full health at the Dawn tower, no Dawn creep around: not a last hit yet, but
        // left alone the wave takes the tower. The hero beside the tower clears it.
        var kernel = new RulesetDraft().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx =>
        {
            ctx.State.Creeps(Team.Dusk).Single().Position = P(3300, 5000);
            ctx.State.Creeps(Team.Dawn).Single().Position = P(500, 9500);   // far away: nobody tanks
            H(ctx.State, "alpha").Position = P(2900, 5000);
        });
        HeroAi.Attach(kernel, Idle());

        kernel.RunTicks(30);

        EntityId alpha = H(kernel.State, "alpha").Id;
        Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == alpha);   // the creep is the only enemy around
    }

    [Theory]
    [InlineData(0.8, false, false)]
    [InlineData(0.5, false, true)]
    [InlineData(0.8, true, true)]
    public void GreedyCarry_KeepsFarmingThroughAFarPushAtEvenNumbers_ButJoinsOnTheHighGround(double greed, bool highGround, bool joins)
    {
        // Minute 18, three against two (unseen at their fountain): the team pushes the far tower by the
        // clock. A greedy carry stays in its lane unless the tower is on high ground; others go (backlog B-020).
        const long Minute18 = 18L * 60 * 30;
        var draft = TestAbilities.Draft();
        if (highGround)
            draft.Elevation.Add(new ElevationZone("plateau.dusk", P(7500, 5000), 1000, 1));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid"), ("delta", "mid") }, new[] { ("beta", "mid"), ("epsilon", "mid") });
        var settings = Idle("beta", "epsilon");
        settings.EvenPushFromMinute = 0;
        settings.Personalities["alpha"] = new HeroPersonality { Greed = greed };
        settings.Personalities["gamma"] = new HeroPersonality();   // test heroes are carries by role
        settings.Personalities["delta"] = new HeroPersonality();
        At(kernel, Minute18, ctx =>
        {
            H(ctx.State, "alpha").Position = P(2500, 5000);
            H(ctx.State, "gamma").Position = P(2600, 5100);
            H(ctx.State, "delta").Position = P(2600, 4900);
        });
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(Minute18 + 1 + 450);   // fifteen seconds: enough to walk most of the lane

        Assert.Equal(joins, H(kernel.State, "alpha").Position.X > Fixed.FromInt(4000));
        Assert.True(H(kernel.State, "gamma").Position.X > Fixed.FromInt(4000), "the rest of the team pushes");
    }

    [Theory]
    [InlineData(0.8, false)]
    [InlineData(0.5, true)]
    public void GreedyCarry_TakesOnlyClearlyWonFightsInTheLaningPhase(double greed, bool engages)
    {
        var settings = Idle("beta");
        settings.Personalities["alpha"] = new HeroPersonality { Aggression = 1.0, Greed = greed };
        var kernel = Skirmish(new[] { ("alpha", 5000, 2000) }, new[] { ("beta", 5450, 2000) }, settings, setup: ctx =>
        {
            Pin(ctx, "beta");
            H(ctx.State, "beta").Hp = Fixed.FromInt(H(ctx.State, "beta").MaxHp * 75 / 100);   // odds about 117%: worth it, not clearly won
        });

        kernel.RunTicks(120);

        EntityId alpha = H(kernel.State, "alpha").Id;
        Assert.Equal(engages, kernel.EventsOf<AttackStarted>().Any(a => a.Attacker == alpha));
    }

    [Fact]
    public void ReadyUltimate_MakesAnEvenFightWorthTaking()
    {
        bool AlphaEngages(bool ultimateReady)
        {
            var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "finale");
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
            settings.Personalities["alpha"] = new HeroPersonality { Aggression = 1.0 };
            settings.Uncontrolled.Add("beta");
            var kernel = Skirmish(new[] { ("alpha", 5000, 2000) }, new[] { ("beta", 5700, 2000) }, settings, draft: draft, setup: ctx =>
            {
                Pin(ctx, "beta");
                HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 2000);   // level 6 each: an even fight on paper
                HeroProgression.GrantXp(ctx, H(ctx.State, "beta"), 2000);
            });
            At(kernel, 2, ctx =>
            {
                if (!ultimateReady)
                    H(ctx.State, "alpha").Hero!.Ability("finale")!.CooldownReadyTick = 100000;
            });
            kernel.RunTicks(120);
            EntityId alpha = H(kernel.State, "alpha").Id;
            return kernel.EventsOf<AttackStarted>().Any(a => a.Attacker == alpha) || kernel.EventsOf<AbilityCast>().Any(c => c.Caster == alpha);
        }

        Assert.True(AlphaEngages(ultimateReady: true), "with its ultimate ready alpha should take the even fight");
        Assert.False(AlphaEngages(ultimateReady: false), "with its ultimate on cooldown alpha should not");
    }

    [Fact]
    public void ReactionJitter_ChangesTimingAcrossSeedsButNotWithinASeed()
    {
        long FirstAttack(ulong seed)
        {
            var draft = TestAbilities.Draft();
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") }, seed);
            At(kernel, 0, ctx =>
            {
                H(ctx.State, "alpha").Position = P(5000, 2000);
                H(ctx.State, "gamma").Position = P(5000, 2200);
                H(ctx.State, "beta").Position = P(5600, 2100);
                Pin(ctx, "beta");
            });
            HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 6, MisjudgementPercent = 0 });
            kernel.RunTicks(120);
            return kernel.TimedEventsOf<AttackStarted>().First(a => a.Event.Attacker == H(kernel.State, "alpha").Id).Tick;
        }

        Assert.Equal(FirstAttack(1), FirstAttack(1));
        var timings = Enumerable.Range(1, 8).Select(s => FirstAttack((ulong)s)).Distinct().Count();
        Assert.True(timings > 1, "reaction times should vary with the seed");
    }
}
