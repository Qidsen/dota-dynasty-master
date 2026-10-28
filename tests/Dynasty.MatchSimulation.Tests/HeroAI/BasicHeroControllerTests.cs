using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

public class BasicHeroControllerTests
{
    private static readonly (string, string)[] NoHeroes = Array.Empty<(string, string)>();

    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    [Fact]
    public void Perception_SeesEnemiesOnlyNearAlliedUnits()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(4000, 5000);
            H(ctx.State, "beta").Position = P(4900, 5000);    // 900 from alpha: seen
            H(ctx.State, "gamma").Position = P(5600, 6500);   // far from every Dawn unit
        });
        kernel.RunTicks(4);   // the tick-3 vision update sees the new positions

        var perception = TeamPerception.Build(kernel.State, Team.Dawn, new HeroAiSettings());

        Assert.True(perception.CanSee(H(kernel.State, "beta")));
        Assert.False(perception.CanSee(H(kernel.State, "gamma")));
        Assert.True(perception.CanSee(H(kernel.State, "alpha")));   // own units are always known
    }

    [Fact]
    public void Controller_NeverTargetsUnseenEnemiesButReactsOnceTheyAppear()
    {
        var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "bolt");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        HeroAi.Attach(kernel);
        At(kernel, 0, ctx => H(ctx.State, "beta").Position = P(6000, 8500));
        At(kernel, 150, ctx => H(ctx.State, "beta").Position = H(ctx.State, "alpha").Position + FixedVector2.FromInts(400, 0));

        kernel.RunTicks(150);
        Assert.Empty(kernel.EventsOf<AbilityCast>());
        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Target == H(kernel.State, "beta").Id);

        kernel.RunTicks(60);
        AbilityCast cast = Assert.Single(kernel.EventsOf<AbilityCast>());
        Assert.Equal(H(kernel.State, "beta").Id, cast.Target);
    }

    [Fact]
    public void Controller_DoesNotHitABuildingUnderBackdoorProtection_ButDoesOnceItIsOff()
    {
        var draft = TestAbilities.Draft().WithoutWaves();
        draft.StructureTypes.Add(new StructureType("barracks", AncientStats(),
            backdoor: new BackdoorProtection(4000, aroundAncient: true, damageReductionPercent: 50, summonDamageReductionPercent: 75, healPerSecond: 180, reactivationMs: 15500)));
        draft.Placements.Add(new StructurePlacement("dusk.mid.barracks", Team.Dusk, "barracks", P(8000, 3000), false, Array.Empty<string>(),
            VulnerabilityRule.AnyProtectorDestroyed, barracksLane: "mid"));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(7400, 3000));
        MatchUnit barracks = kernel.State.Units.Single(u => u.Placement?.Id == "dusk.mid.barracks");
        At(kernel, 151, ctx =>
        {
            barracks.BackdoorOffUntilTick = long.MaxValue / 2;   // as if enemy creeps were near
            H(ctx.State, "alpha").Position = P(7400, 3000);
        });

        kernel.RunTicks(150);
        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Target == barracks.Id);

        kernel.RunTicks(150);
        Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Target == barracks.Id);
    }

    /// <summary>
    /// Two runes exactly as far away: the choice must not follow the order units were made in (D-014: in a mirror match both
    /// mid heroes on the diagonal would walk to the same rune), so it is drawn from the AI's stream.
    /// </summary>
    [Fact]
    public void Controller_TwoRunesEquallyFar_IsNotDecidedByTheirOrder()
    {
        var picks = new HashSet<long>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var draft = new RulesetDraft().WithoutWaves();
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes, seed);
            MatchUnit? first = null, second = null;
            At(kernel, 0, ctx =>
            {
                H(ctx.State, "alpha").Position = P(5000, 5000);
                Dynasty.MatchSimulation.Combat.Modifiers.Apply(ctx, H(ctx.State, "alpha"), new Dynasty.MatchSimulation.Combat.ModifierSpec("held", 2000,
                    Dynasty.MatchSimulation.Combat.ModifierFlags.Rooted), H(ctx.State, "alpha").Id);   // stays on the diagonal while it decides
                var type = new RuneType("bounty", gold: 1);
                first = ctx.State.SpawnRune(ctx.Ids.Allocate(), type, 0, P(4600, 5400));
                second = ctx.State.SpawnRune(ctx.Ids.Allocate(), type, 1, P(5400, 4600));
            });
            long pick = -1;
            for (long t = 1; t <= 30; t++)
                At(kernel, t, ctx =>
                {
                    HeroOrder order = H(ctx.State, "alpha").Hero!.Order;
                    if (pick < 0 && order.Kind == HeroOrderKind.Move && (order.Point == first!.Position || order.Point == second!.Position))
                        pick = order.Point == first.Position ? 0 : 1;
                });
            HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0 });
            kernel.RunTicks(30);
            picks.Add(pick);
        }

        Assert.Equal(new long[] { 0, 1 }, picks.OrderBy(p => p).ToArray());
    }

    /// <summary>An own tower below its deny threshold is finished by a hero close to it (B-140).</summary>
    [Theory]
    [InlineData(5, true)]
    [InlineData(20, false)]
    public void Controller_DeniesItsOwnBuildingWhenLowEnough(int hpPercent, bool denied)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.StructureTypes[0] = new StructureType("tower", TowerStats(dmg: 0), denyHealthPercent: 10);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        At(kernel, 0, ctx =>
        {
            MatchUnit tower = ctx.State.FindStructure("dawn.mid.t1")!;
            tower.Hp = Fixed.FromInt(tower.MaxHp * hpPercent / 100);
            H(ctx.State, "alpha").Position = P(3300, 5000);
        });
        bool tried = false;
        HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0, DecisionTrace = (h, now, branch) => tried |= branch == "DenyBuilding" });

        kernel.RunTicks(30 * 20);

        Assert.Equal(denied, kernel.EventsOf<StructureDestroyed>().Any(d => d.PlacementId == "dawn.mid.t1"));
        Assert.Equal(denied, tried);
    }

    /// <summary>Two camps exactly as far from the hero: which it farms is drawn, not left to the order of the camps (B-155).</summary>
    [Fact]
    public void Controller_TwoCampsEquallyFar_AreNotDecidedByTheirOrder()
    {
        var picks = new HashSet<string>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var draft = new RulesetDraft().WithoutWaves();
            draft.Creeps.Add(new CreepType("wolf", MeleeStats(hp: 300), new Bounty(30, 30, 40)));
            draft.Camps.Add(new NeutralCampDefinition("camp.a", P(2000, 2000), new[] { new WaveEntry("wolf", 1) }, firstSpawnMs: 0, respawnMs: 60000, leashRadius: 600));
            draft.Camps.Add(new NeutralCampDefinition("camp.b", P(4000, 2000), new[] { new WaveEntry("wolf", 1) }, firstSpawnMs: 0, respawnMs: 60000, leashRadius: 600));
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes, seed);
            At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(3000, 2000));
            string pick = "";
            for (long t = 1; t <= 30; t++)
                At(kernel, t, ctx =>
                {
                    HeroOrder order = H(ctx.State, "alpha").Hero!.Order;
                    if (pick == "" && order.Kind == HeroOrderKind.Move && (order.Point == P(2000, 2000) || order.Point == P(4000, 2000)))
                        pick = order.Point == P(2000, 2000) ? "a" : "b";
                });
            HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0 });
            kernel.RunTicks(30);
            picks.Add(pick);
        }

        Assert.Equal(new[] { "a", "b" }, picks.OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// A core walking to a camp near its lane goes on to farm it when an enemy wave reaches the lane meanwhile, instead of turning
    /// back halfway (on the long Dota lanes such walks were mostly wasted: 208 s walking to 60 s farming in minutes 10-20).
    /// </summary>
    [Fact]
    public void Controller_WalkingToACamp_FarmsItEvenWhenAWaveArrivesMeanwhile()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("wolf", MeleeStats(hp: 300), new Bounty(30, 30, 40)));
        draft.Creeps.Add(new CreepType("brute", MeleeStats(hp: 5000)));
        draft.Camps.Add(new NeutralCampDefinition("camp.a", P(3000, 3200), new[] { new WaveEntry("wolf", 1) }, firstSpawnMs: 0, respawnMs: 60000, leashRadius: 600));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(3000, 5000));
        At(kernel, 20, ctx =>
        {
            MatchUnit creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Dusk, ctx.State.Ruleset.FindCreepType("brute")!, ctx.State.Ruleset.Map.Lanes.Single(l => l.Id == "mid"), ctx.Tick.Value);
            creep.Position = P(3800, 5000);
            creep.StartPosition = creep.Position;
        });
        HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 });

        kernel.RunTicks(30 * 20);

        Assert.Contains(kernel.EventsOf<UnitKilled>(), k => k.VictimTypeId == "wolf" && k.Killer == H(kernel.State, "alpha").Id);
    }

    [Fact]
    public void Controller_GoesToItsLaneAndLastHitsCreeps()
    {
        var kernel = new RulesetDraft().StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);
        kernel.RunTicks(5 * 60 * 30);   // ten waves: enough samples that a single missed last hit does not decide the test

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.True(alpha.Hero!.LastHits >= 2, "last hits: " + alpha.Hero.LastHits);
        Assert.Contains(kernel.EventsOf<GoldEarned>(), g => g.Hero == alpha.Id && g.Reason == "last_hit");
        Assert.Equal(0, alpha.Hero.Deaths);
    }

    [Fact]
    public void Controller_RetreatsWhenHurtAndReturnsAfterHealing()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);
        At(kernel, 0, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            alpha.Position = P(3500, 5000);
            alpha.Hp = Fixed.FromInt(100);
        });

        kernel.RunTicks(150);
        Assert.True(H(kernel.State, "alpha").Position.X < Fixed.FromInt(2500), "should walk home");

        kernel.RunTicks(30 * 30);
        Assert.True(H(kernel.State, "alpha").Hp.ToDouble() > 500, "should heal at the fountain");
    }

    [Fact]
    public void Controller_BuysItemsFromTheHeroBuild()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Items.Add(new ItemDefinition("blade", "Blade", 500, new Dynasty.MatchSimulation.Combat.ModifierSpec("blade", -1, damageBonus: 20)));
        draft.Heroes[0] = new HeroDefinition("alpha", "alpha", new[] { "carry" }, HeroStats(), 80, Fixed.FromRatio(3, 10), 3, 300, 40,
            Fixed.FromInt(2), Fixed.One, itemBuild: new[] { "blade" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);

        kernel.RunTicks(10);

        Assert.Equal("blade", Assert.Single(kernel.EventsOf<ItemPurchased>()).ItemId);
    }

    [Fact]
    public void Controller_BuysRecipeComponentsFirstWhenTheFullItemIsUnaffordable()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Items.Add(new ItemDefinition("blade", "Blade", 500, new Dynasty.MatchSimulation.Combat.ModifierSpec("blade", -1, damageBonus: 20)));
        draft.Items.Add(new ItemDefinition("plate", "Plate", 400, new Dynasty.MatchSimulation.Combat.ModifierSpec("plate", -1, armorBonus: Fixed.FromInt(5))));
        draft.Items.Add(new ItemDefinition("crest", "Crest", 1500, new Dynasty.MatchSimulation.Combat.ModifierSpec("crest", -1, damageBonus: 30), new[] { "blade", "plate" }));
        draft.Heroes[0] = new HeroDefinition("alpha", "alpha", new[] { "carry" }, HeroStats(), 80, Fixed.FromRatio(3, 10), 3, 300, 40,
            Fixed.FromInt(2), Fixed.One, itemBuild: new[] { "crest" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, NoHeroes);
        HeroAi.Attach(kernel);
        At(kernel, 30, ctx => H(ctx.State, "alpha").Hero!.Gold = Fixed.FromInt(5000));

        kernel.RunTicks(60);

        Assert.Equal(new[] { "blade", "plate", "crest" }, kernel.EventsOf<ItemPurchased>().Select(p => p.ItemId));
        Assert.Equal(new[] { "crest" }, H(kernel.State, "alpha").Hero!.Inventory.Select(i => i.Definition.Id));
    }

    [Fact]
    public void ControlledMatch_IsDeterministic()
    {
        SimulationKernel<MatchState> Run()
        {
            var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "bolt").WithHeroAbilities("beta", "quake");
            draft.DawnWave.Add(new WaveEntry("melee", 2));
            draft.DuskWave.Add(new WaveEntry("melee", 2));
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") }, seed: 5);
            HeroAi.Attach(kernel);
            kernel.RunTicks(2 * 60 * 30);
            return kernel;
        }

        Assert.Equal(Run().Events.Checksum, Run().Events.Checksum);
    }
}
