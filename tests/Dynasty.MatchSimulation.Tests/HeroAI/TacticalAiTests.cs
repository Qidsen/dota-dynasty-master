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
        HeroAi.Attach(kernel, settings ?? new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 });
        return kernel;
    }

    /// <summary>Settings without reaction noise where the given heroes are left uncontrolled (they stand still).</summary>
    private static HeroAiSettings Idle(params string[] heroes)
    {
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
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
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
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

    /// <summary>
    /// A push at even numbers takes the heroes it needs (the nearest three), not the whole team: the
    /// farthest one keeps its lane — someone has to catch the other waves.
    /// </summary>
    [Fact]
    public void EvenNumbersPush_TakesTheNearestHeroesItNeeds()
    {
        var draft = TestAbilities.Draft();
        draft.Heroes.Add(RulesetDraft.Hero("zeta"));
        draft.Heroes.Add(RulesetDraft.Hero("eta"));
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0, EvenPushFromMinute = 0 };
        foreach (string hero in new[] { "alpha", "gamma", "delta", "zeta" })
            settings.Personalities[hero] = new HeroPersonality();   // not greedy carries: those skip early pushes
        foreach (string hero in new[] { "beta", "epsilon", "eta" })
            settings.Uncontrolled.Add(hero);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid"), ("delta", "mid"), ("zeta", "mid") },
            new[] { ("beta", "mid"), ("epsilon", "mid"), ("eta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(2500, 5000);
            H(ctx.State, "gamma").Position = P(2500, 5200);
            H(ctx.State, "delta").Position = P(2500, 4800);
            H(ctx.State, "zeta").Position = P(1500, 8500);   // farthest from the Dusk tower
            foreach (string hero in new[] { "beta", "epsilon", "eta" })
                H(ctx.State, hero).Position = P(9600, 9600);   // alive, out of sight: even numbers
        });
        HeroAi.Attach(kernel, settings);
        FixedVector2 tower = kernel.State.FindStructure("dusk.mid.t1")!.Position;

        kernel.RunTicks(90);

        bool Pushes(string hero) => H(kernel.State, hero).Hero!.Order.Kind == HeroOrderKind.Move &&
                                    FixedVector2.Distance(H(kernel.State, hero).Hero!.Order.Point, tower) < Fixed.FromInt(1500);
        Assert.True(Pushes("alpha") && Pushes("gamma") && Pushes("delta"), "the three nearest should gather");
        Assert.False(Pushes("zeta"), "the farthest should keep farming");
    }

    /// <summary>
    /// Dota's Backdoor Protection heals what heroes alone deal to a building: a push waits for the creeps instead of
    /// hitting a protected building.
    /// </summary>
    [Fact]
    public void Push_DoesNotHitABuildingUnderBackdoorProtection()
    {
        var draft = TestAbilities.Draft().WithoutWaves();
        draft.StructureTypes.RemoveAll(t => t.Id == "tower");
        draft.StructureTypes.Add(new StructureType("tower", RulesetDraft.TowerStats(dmg: 0),
            backdoor: new BackdoorProtection(900, aroundAncient: false, damageReductionPercent: 50, summonDamageReductionPercent: 75, healPerSecond: 180, reactivationMs: 15500)));
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0, EvenPushFromMinute = 0 };
        foreach (string hero in new[] { "alpha", "gamma", "delta" })
            settings.Personalities[hero] = new HeroPersonality();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid"), ("delta", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(6300, 5000);
            H(ctx.State, "gamma").Position = P(6300, 5200);
            H(ctx.State, "delta").Position = P(6300, 4800);
        });
        HeroAi.Attach(kernel, settings);
        MatchUnit tower = kernel.State.FindStructure("dusk.mid.t1")!;

        kernel.RunTicks(150);

        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Target == tower.Id);
    }

    /// <summary>
    /// The glyph is pressed to save a building about to fall to a dive: ready, the building low (below 35%) and at least two
    /// enemy heroes seen at it. Not for a lone hero, nor for a building still healthy.
    /// </summary>
    [Theory]
    [InlineData(2, 30, true)]
    [InlineData(1, 30, false)]
    [InlineData(2, 60, false)]
    public void Glyph_IsUsedToSaveABuildingFromADive(int enemies, int hpPercent, bool expected)
    {
        var draft = TestAbilities.Draft().WithoutWaves();
        draft.Glyph = new GlyphRules(cooldownMs: 300000, initialCooldownMs: 1000, buildingDurationMs: 7000, creepDurationMs: 3000, resetDelayMs: 1000);
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        foreach (string hero in new[] { "beta", "delta" })
            settings.Uncontrolled.Add(hero);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(1000, 9000);
            H(ctx.State, "beta").Position = P(3300, 5000);
            H(ctx.State, "delta").Position = enemies >= 2 ? P(3300, 5200) : P(9000, 9000);
            MatchUnit tower = ctx.State.FindStructure("dawn.mid.t1")!;
            tower.Hp = Fixed.FromInt(tower.MaxHp * hpPercent / 100);
        });
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(90);

        Assert.Equal(expected, kernel.EventsOf<GlyphUsed>().Any(g => g.Team == Team.Dawn));
    }

    /// <summary>
    /// A target that runs away just as fast cannot be caught: after a short chase without closing in the
    /// heroes give it up instead of following it across the map (nothing slows or holds it).
    /// </summary>
    [Fact]
    public void FleeingEqualSpeedTarget_IsNotChasedAcrossTheMap()
    {
        var settings = Idle("beta");
        foreach (string hero in new[] { "alpha", "gamma" })
            settings.Personalities[hero] = new HeroPersonality();
        var kernel = Skirmish(
            new[] { ("alpha", 4000, 8000), ("gamma", 4000, 8150) },
            new[] { ("beta", 4700, 8000) },
            settings,
            setup: ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(9800, 8000))));

        kernel.RunTicks(300);

        Assert.True(H(kernel.State, "beta").Position.X > Fixed.FromInt(7000), "beta should have run");
        Assert.True(H(kernel.State, "alpha").Position.X < Fixed.FromInt(6000), $"alpha followed to x={H(kernel.State, "alpha").Position.X.ToDouble():0}");
        Assert.True(H(kernel.State, "gamma").Position.X < Fixed.FromInt(6000), $"gamma followed to x={H(kernel.State, "gamma").Position.X.ToDouble():0}");
    }

    /// <summary>
    /// One enemy hero at a tower is answered by the nearest heroes, one more than the attackers, not by
    /// the whole team from across the map: the farthest of three keeps doing its own thing.
    /// </summary>
    [Fact]
    public void LoneThreat_DrawsOnlyTheNearestDefenders()
    {
        var draft = TestAbilities.Draft();
        draft.Lanes.Add(new LaneDefinition("side", new[] { P(1000, 1000), P(9000, 1000) }));
        draft.StructureTypes[0] = new StructureType("tower", TowerStats(dmg: 1));
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        foreach (string hero in new[] { "alpha", "gamma", "delta" })
            settings.Personalities[hero] = new HeroPersonality();   // not greedy carries: those defend only nearby
        var kernel = Skirmish(
            new[] { ("alpha", 2500, 1000), ("gamma", 4500, 1000), ("delta", 8500, 1000) },
            new[] { ("beta", 3300, 5000) },
            settings,
            draft: draft,
            lane: "side",
            setup: ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(ctx.State.FindStructure("dawn.mid.t1")!.Id)));
        FixedVector2 tower = P(3000, 5000);

        kernel.RunTicks(60);

        bool Defends(string hero) => H(kernel.State, hero).Hero!.Order.Kind == HeroOrderKind.Move &&
                                     FixedVector2.Distance(H(kernel.State, hero).Hero!.Order.Point, tower) < Fixed.FromInt(400);
        Assert.True(Defends("alpha") && Defends("gamma"), "the two nearest should come");
        Assert.False(Defends("delta"), "the farthest should not cross the map for one attacker");
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
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
            settings.Personalities["alpha"] = new HeroPersonality { Aggression = aggression };
            settings.Personalities["beta"] = new HeroPersonality { Aggression = 0.5 };
            var kernel = Skirmish(new[] { ("alpha", 5000, 2000) }, new[] { ("beta", 5700, 2000) }, settings, setup: ctx => Pin(ctx, "beta"));
            kernel.RunTicks(120);
            return kernel.EventsOf<AttackStarted>().Count(a => a.Attacker == H(kernel.State, "alpha").Id);
        }

        Assert.True(AlphaAttacks(1.6) > 0);
        Assert.Equal(0, AlphaAttacks(0.6));
    }

    [Theory]
    [InlineData(60, false)]
    [InlineData(20, true)]
    public void WinningHero_DivesUnderAnEnemyTowerOnlyForAKill(int betaHpPercent, bool dives)
    {
        // Alpha is far stronger, beta stands beside the Dusk tower: hitting him there draws the tower (lane
        // aggro). Only a nearly dead beta is worth it.
        var settings = Idle("beta");
        settings.Personalities["alpha"] = new HeroPersonality();
        settings.SkillsOf(Team.Dawn)["alpha"] = new HeroSkill { ReactionJitterTicks = 0, MisjudgementPercent = 0, };
        var draft = new RulesetDraft().WithoutWaves();
        draft.HeroAggro = true;
        var kernel = Skirmish(new[] { ("alpha", 6100, 5000) }, new[] { ("beta", 6750, 5000) }, settings, draft: draft, setup: ctx =>
        {
            HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 2000);
            Pin(ctx, "beta");
            H(ctx.State, "beta").Hp = Fixed.FromInt(H(ctx.State, "beta").MaxHp * betaHpPercent / 100);
        });

        kernel.RunTicks(90);

        EntityId alpha = H(kernel.State, "alpha").Id, beta = H(kernel.State, "beta").Id;
        Assert.Equal(dives, kernel.EventsOf<AttackStarted>().Any(a => a.Attacker == alpha && a.Target == beta));
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
    [InlineData(10, 0.8, 75, 0, false)]      // laning: a farming core does not fight a healthy laner
    [InlineData(10, 0.5, 75, 0, false)]
    [InlineData(10, 0.5, 30, 0, true)]       // ... but goes for a kill
    [InlineData(0, 0.5, 75, 0, true)]        // after the laning phase the same odds (about 117%) are a fight
    [InlineData(10, 0.5, 75, 2000, false)]   // even far ahead (odds well over 150%) a core keeps farming
    [InlineData(10, 0.1, 75, 2000, true)]    // a support takes such a clear edge
    [InlineData(10, 0.1, 75, 0, false)]      // ... but not a merely favourable one in lane
    public void LaningCore_FightsOnlyForAKill(int laningMinutes, double greed, int betaHpPercent, int alphaXp, bool engages)
    {
        var settings = Idle("beta");
        settings.LaningPhaseMinutes = laningMinutes;
        settings.Personalities["alpha"] = new HeroPersonality { Aggression = 1.0, Greed = greed };
        // In the lane with an own creep beside alpha (no chase rule in the way), away from both towers.
        var kernel = Skirmish(new[] { ("alpha", 5000, 5000) }, new[] { ("beta", 5450, 5000) }, settings, draft: new RulesetDraft(), setup: ctx =>
        {
            ctx.State.Creeps(Team.Dawn).Single().Position = P(4900, 5150);
            ctx.State.Creeps(Team.Dusk).Single().Position = P(9500, 9000);
            Pin(ctx, "beta");
            if (alphaXp > 0)
                HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), alphaXp);
            H(ctx.State, "beta").Hp = Fixed.FromInt(H(ctx.State, "beta").MaxHp * betaHpPercent / 100);
        });

        kernel.RunTicks(120);

        EntityId alpha = H(kernel.State, "alpha").Id;
        Assert.Equal(engages, kernel.EventsOf<AttackStarted>().Any(a => a.Attacker == alpha));
    }

    [Theory]
    [InlineData(100, 4500, 9999)]   // healthy: keeps last hitting at the wave
    [InlineData(55, 0, 4450)]       // hurt: backs away out of the fight
    public void OutnumberedLaner_HoldsTheLaneWhileHealthy(int hpPercent, int minX, int maxX)
    {
        // Laning, alpha alone against two visible enemies (odds far below 85%), the waves meeting in mid.
        var settings = Idle("beta", "gamma");
        settings.LaningPhaseMinutes = 10;
        settings.Personalities["alpha"] = new HeroPersonality();
        var kernel = Skirmish(new[] { ("alpha", 4900, 5000) }, new[] { ("beta", 5600, 5000), ("gamma", 5600, 5150) }, settings, draft: new RulesetDraft(), setup: ctx =>
        {
            ctx.State.Creeps(Team.Dawn).Single().Position = P(5050, 5100);
            ctx.State.Creeps(Team.Dusk).Single().Position = P(5250, 5000);
            Pin(ctx, "beta");
            Pin(ctx, "gamma");
            H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp * hpPercent / 100);
        });

        kernel.RunTicks(150);

        int x = H(kernel.State, "alpha").Position.X.ToIntFloor();
        Assert.True(x >= minX && x <= maxX, $"alpha at x = {x}, expected {minX}..{maxX} (fountain at 200, own tower at 3000)");
    }

    [Theory]
    [InlineData(true, false)]    // beta stands away from any own creep or tower: no chase in the laning phase
    [InlineData(false, true)]    // the same fight inside the lane is taken
    public void LaningHero_DoesNotChaseAHealthyEnemyOutOfTheLane(bool outOfLane, bool attacks)
    {
        var settings = Idle("beta");
        settings.LaningPhaseMinutes = 10;
        settings.Personalities["alpha"] = new HeroPersonality { Greed = 0.1 };   // a support: takes clear edges in lane
        int y = outOfLane ? 2000 : 5000;
        var kernel = Skirmish(new[] { ("alpha", 5000, y) }, new[] { ("beta", 5450, y) }, settings, draft: new RulesetDraft(), setup: ctx =>
        {
            ctx.State.Creeps(Team.Dawn).Single().Position = P(4900, 5150);
            ctx.State.Creeps(Team.Dusk).Single().Position = P(9500, 9000);
            Pin(ctx, "beta");
            HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 2000);   // odds well over 150%
        });

        kernel.RunTicks(120);

        EntityId alpha = H(kernel.State, "alpha").Id, beta = H(kernel.State, "beta").Id;
        Assert.Equal(attacks, kernel.EventsOf<AttackStarted>().Any(a => a.Attacker == alpha && a.Target == beta));
    }

    [Fact]
    public void ReadyUltimate_MakesAnEvenFightWorthTaking()
    {
        bool AlphaEngages(bool ultimateReady)
        {
            var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "finale");
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
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
            HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 6, MisjudgementPercent = 0, LaningPhaseMinutes = 0 });
            kernel.RunTicks(120);
            return kernel.TimedEventsOf<AttackStarted>().First(a => a.Event.Attacker == H(kernel.State, "alpha").Id).Tick;
        }

        Assert.Equal(FirstAttack(1), FirstAttack(1));
        var timings = Enumerable.Range(1, 8).Select(s => FirstAttack((ulong)s)).Distinct().Count();
        Assert.True(timings > 1, "reaction times should vary with the seed");
    }
}
