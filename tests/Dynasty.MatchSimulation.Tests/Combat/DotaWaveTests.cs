using Dynasty.Core.Mathematics;
using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Tests.Abilities;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Systems;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Dota 7.41f lane waves (Liquipedia «Lane Creeps», updated for 7.41): every 30 s 3 melee and 1 ranged; a siege creep from
/// the 11th wave on every 10th; a flagbearer from the 5th wave on every 2nd, in place of a melee creep; more creeps from set
/// times (+1 melee at 15:00, 30:00 and 45:00, +1 ranged at 40:00, +1 siege at 30:00 and 60:00). Here with a 1 s interval.
/// </summary>
public class DotaWaveTests
{
    private static SimulationKernel<MatchState> Lanes(params WaveEntry[] wave)
    {
        var draft = new RulesetDraft { IntervalMs = 1000 };
        draft.Creeps.Add(new CreepType("ranged", MeleeStats(), new Bounty(40, 40, 60)));
        draft.Creeps.Add(new CreepType("siege", MeleeStats(), new Bounty(40, 40, 60)));
        draft.Creeps.Add(new CreepType("flagbearer", MeleeStats(), new Bounty(40, 40, 60)));
        draft.DawnWave.Clear();
        draft.DuskWave.Clear();
        draft.DawnWave.AddRange(wave);
        draft.DuskWave.AddRange(wave);
        return draft.Start();
    }

    /// <summary>Dawn's creeps of each type spawned in the wave that came at the given second (wave 1 at 0 s).</summary>
    private static Dictionary<string, int> WaveAt(SimulationKernel<MatchState> kernel, int second) =>
        kernel.TimedEventsOf<CreepSpawned>()
            .Where(e => e.Tick == second * 30L && e.Event.Team == Team.Dawn)
            .GroupBy(e => e.Event.CreepTypeId).ToDictionary(g => g.Key, g => g.Count());

    [Fact]
    public void Entries_StartAtAWave_ComeEveryNthWave_AndJoinFromATime()
    {
        var kernel = Lanes(
            new WaveEntry("melee", 3),
            new WaveEntry("ranged", 1),
            new WaveEntry("siege", 1, firstWave: 11, everyWaves: 10),
            new WaveEntry("melee", 1, fromMs: 5000),
            new WaveEntry("siege", 1, firstWave: 11, everyWaves: 10, fromMs: 15000));
        kernel.RunTicks(30 * 25);

        Assert.Equal(new Dictionary<string, int> { ["melee"] = 3, ["ranged"] = 1 }, WaveAt(kernel, 0));                  // wave 1
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 4, ["ranged"] = 1 }, WaveAt(kernel, 5));                  // wave 6, from 5 s
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 4, ["ranged"] = 1, ["siege"] = 1 }, WaveAt(kernel, 10));  // wave 11
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 4, ["ranged"] = 1 }, WaveAt(kernel, 11));
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 4, ["ranged"] = 1, ["siege"] = 2 }, WaveAt(kernel, 20));  // wave 21, from 15 s
    }

    [Fact]
    public void AFlagbearer_TakesTheplaceOfAMeleeCreep()
    {
        var kernel = Lanes(
            new WaveEntry("melee", 3),
            new WaveEntry("ranged", 1),
            new WaveEntry("flagbearer", 1, firstWave: 5, everyWaves: 2, replacesCreepTypeId: "melee"));
        kernel.RunTicks(30 * 8);

        Assert.Equal(new Dictionary<string, int> { ["melee"] = 3, ["ranged"] = 1 }, WaveAt(kernel, 3));                       // wave 4
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 2, ["ranged"] = 1, ["flagbearer"] = 1 }, WaveAt(kernel, 4));   // wave 5
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 3, ["ranged"] = 1 }, WaveAt(kernel, 5));
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 2, ["ranged"] = 1, ["flagbearer"] = 1 }, WaveAt(kernel, 6));   // wave 7
    }

    /// <summary>
    /// Every 7:30 lane creeps get stronger (Liquipedia «Lane Creeps», 7.41): a creep spawned after n cycles has n times its
    /// type's health and damage step (at most 30 times) and n times its gold and XP step (no limit; super melee +1.5 gold).
    /// Here a cycle is 2 s and the stats stop after 2 cycles.
    /// </summary>
    private static SimulationKernel<MatchState> Upgrading()
    {
        var draft = TestAbilities.Draft();   // without waves: one melee creep a side
        draft.IntervalMs = 1000;
        draft.DawnWave.Add(new WaveEntry("melee", 1));
        draft.DuskWave.Add(new WaveEntry("melee", 1));
        draft.CreepUpgrades = new CreepUpgradeRules(intervalMs: 2000, maxStatCycles: 2, new[]
        {
            new CreepUpgradeStep("melee", health: 12, damage: 1, goldHundredths: 150, xp: 8),
        });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(5000, 9000));
        return kernel;
    }

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    [Fact]
    public void CreepsSpawnedLater_HaveMoreHealthAndDamage_UpToTheLimit_AndMoreBounty()
    {
        var kernel = Upgrading();
        var spawned = new Dictionary<int, MatchUnit>();
        var spawnHp = new Dictionary<int, int>();
        foreach (int second in new[] { 0, 2, 4, 6 })
            At(kernel, second * 30L + 1, ctx =>
            {
                spawned[second] = ctx.State.Units.Last(u => u.Kind == UnitKind.Creep && u.Team == Team.Dusk);
                spawnHp[second] = spawned[second].Hp.ToIntFloor();
            });
        int lastHitGold = 0;
        int xpBefore = 0;
        At(kernel, 6 * 30 + 2, ctx =>
        {
            H(ctx.State, "alpha").Position = spawned[6].Position;   // within the XP radius
            xpBefore = H(ctx.State, "alpha").Hero!.Xp;
            Dynasty.MatchSimulation.Combat.Damage.Deal(ctx, H(ctx.State, "alpha").Id, spawned[6], Dynasty.Core.Mathematics.Fixed.FromInt(99999), Dynasty.MatchSimulation.Combat.DamageType.Pure);
        });
        kernel.RunTicks(6 * 30 + 5);
        lastHitGold = kernel.EventsOf<GoldEarned>().Last(g => g.Reason == "last_hit").Amount;

        int baseHp = spawned[0].MaxHp, baseDamage = spawned[0].AttackDamageMin;
        Assert.Equal(new[] { baseHp, baseHp + 12, baseHp + 24, baseHp + 24 }, new[] { 0, 2, 4, 6 }.Select(t => spawned[t].MaxHp).ToArray());
        Assert.Equal(new[] { baseDamage, baseDamage + 1, baseDamage + 2, baseDamage + 2 }, new[] { 0, 2, 4, 6 }.Select(t => spawned[t].AttackDamageMin).ToArray());
        Assert.Equal(spawned[4].MaxHp, spawnHp[4]);   // spawned at full health
        Assert.Equal(40 + 4, lastHitGold);                                  // 40 + 3 × 1.5 = 44.5
        Assert.Equal(60 + 24, H(kernel.State, "alpha").Hero!.Xp - xpBefore);   // the type's 60 + 3 × 8
    }

    /// <summary>
    /// The flagbearer (Liquipedia «Lane Creeps», 7.41; game file `flagbearer_creep_aura_effect`): its aura gives allied creeps
    /// within 700 +3 health regeneration and 4% magic resistance per upgrade cycle (at most 15), not heroes; killed by an enemy
    /// it gives the killer and every enemy hero within 1500 10 gold plus its bounty again, denied it gives none.
    /// </summary>
    private static SimulationKernel<MatchState> Flagbearers(int cycleMs = 2000)
    {
        var draft = TestAbilities.Draft();
        var aura = new ModifierSpec("inspiration", -1, aura: new AuraSpec(700, TargetTeam.Ally, heroesOnly: false,
            new ModifierSpec("inspiration_regen", 500, hpRegenBonus: Dynasty.Core.Mathematics.Fixed.FromInt(3), unique: true), creepsOnly: true));
        draft.Creeps.Add(new CreepType("flagbearer", MeleeStats(), new Bounty(40, 40, 60), modifiers: new[] { aura },
            areaBounty: new AreaBounty(baseGold: 10, bountyPercent: 100, radius: 1500)));
        draft.CreepUpgrades = new CreepUpgradeRules(cycleMs, maxStatCycles: 30, new[]
        {
            new CreepUpgradeStep("flagbearer", 0, 0, 0, 0, auraMagicResistPerCycle: 4, auraMaxCycles: 15, auraRadius: 700),
        });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            foreach (string hero in new[] { "alpha", "gamma", "beta", "delta" })
                H(ctx.State, hero).Position = P(5000, 9000);
        });
        return kernel;
    }

    private static MatchUnit Spawn(SimulationContext<MatchState> ctx, Team team, string type, Dynasty.Core.Mathematics.FixedVector2 at)
    {
        MatchUnit creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), team, ctx.State.Ruleset.FindCreepType(type)!, ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
        WaveSpawnSystem.Upgrade(creep, ctx.State.Ruleset.Waves.Upgrades, ctx.Tick.Value * 1000 / 30);
        creep.Position = at;
        return creep;
    }

    [Fact]
    public void TheFlagbearersAura_GivesAlliedCreepsRegenAndMagicResistanceGrowingWithTheCycles()
    {
        var kernel = Flagbearers();
        MatchUnit? near = null, far = null, enemy = null;
        At(kernel, 4 * 30, ctx =>   // two cycles in: +8% magic resistance
        {
            Spawn(ctx, Team.Dawn, "flagbearer", P(5000, 5000));
            near = Spawn(ctx, Team.Dawn, "melee", P(5300, 5000));
            far = Spawn(ctx, Team.Dawn, "melee", P(6000, 5000));
            enemy = Spawn(ctx, Team.Dusk, "melee", P(5000, 5300));
            H(ctx.State, "alpha").Position = P(4800, 5000);
        });
        kernel.RunTicks(4 * 30 + 15);

        Assert.Equal(near!.Stats.MagicResistPercent + 8, near.MagicResistPercent);
        Assert.Contains(near.Modifiers, m => m.Spec.Key == "inspiration_regen");
        Assert.DoesNotContain(far!.Modifiers, m => m.Spec.Key.StartsWith("inspiration"));
        Assert.DoesNotContain(enemy!.Modifiers, m => m.Spec.Key.StartsWith("inspiration"));
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key.StartsWith("inspiration"));
    }

    [Fact]
    public void AKilledFlagbearer_GivesTheKillerAndNearbyEnemyHeroesBonusGold_ButNotWhenDenied()
    {
        var kernel = Flagbearers(cycleMs: 600000);
        MatchUnit? flag = null, denied = null;
        Dictionary<string, double> before = new(), mid = new();
        Dictionary<string, double> Gold(MatchState s) => new[] { "alpha", "gamma", "beta", "delta" }.ToDictionary(h => h, h => H(s, h).Hero!.Gold.ToDouble());
        At(kernel, 2, ctx =>
        {
            flag = Spawn(ctx, Team.Dusk, "flagbearer", P(5000, 5000));
            denied = Spawn(ctx, Team.Dusk, "flagbearer", P(5000, 1000));
            H(ctx.State, "alpha").Position = P(5000, 7000);   // 2000 away: the killer gets it anyway
            H(ctx.State, "gamma").Position = P(5000, 6000);   // within 1500
            H(ctx.State, "beta").Position = P(5000, 1200);    // the denier
            before = Gold(ctx.State);
        });
        At(kernel, 3, ctx => Dynasty.MatchSimulation.Combat.Damage.Deal(ctx, H(ctx.State, "alpha").Id, flag!, Dynasty.Core.Mathematics.Fixed.FromInt(99999), Dynasty.MatchSimulation.Combat.DamageType.Pure));
        At(kernel, 5, ctx =>
        {
            mid = Gold(ctx.State);
            denied!.Hp = Dynasty.Core.Mathematics.Fixed.FromInt(10);
            Dynasty.MatchSimulation.Combat.Damage.Deal(ctx, H(ctx.State, "beta").Id, denied, Dynasty.Core.Mathematics.Fixed.FromInt(99999), Dynasty.MatchSimulation.Combat.DamageType.Pure);
        });
        kernel.RunTicks(7);

        Dictionary<string, double> after = Gold(kernel.State);
        Assert.Equal(40 + 10 + 40, System.Math.Floor(mid["alpha"] - before["alpha"]));   // bounty + area gold
        Assert.Equal(10 + 40, System.Math.Floor(mid["gamma"] - before["gamma"]));
        Assert.Equal(0, System.Math.Floor(after["gamma"] - mid["gamma"]));                // a deny: nothing
        Assert.Equal(0, System.Math.Floor(after["alpha"] - mid["alpha"]));
        Assert.Equal((0d, 0d), (System.Math.Floor(after["beta"] - mid["beta"]), System.Math.Floor(after["delta"] - mid["delta"])));
    }

    [Fact]
    public void TwoFlagbearers_DoNotStackTheirAuras_TheStrongerCounts()
    {
        var kernel = Flagbearers();
        MatchUnit? near = null, first = null;
        At(kernel, 2 * 30, ctx => first = Spawn(ctx, Team.Dawn, "flagbearer", P(5000, 5000)));   // one cycle: 4%
        At(kernel, 6 * 30, ctx =>
        {
            first!.Position = P(5000, 5000);                                                       // back by the others
            Spawn(ctx, Team.Dawn, "flagbearer", P(5100, 5000));                                    // three cycles: 12%
            near = Spawn(ctx, Team.Dawn, "melee", P(5300, 5000));
        });
        kernel.RunTicks(6 * 30 + 15);

        Assert.Equal(near!.Stats.MagicResistPercent + 12, near.MagicResistPercent);
    }

    /// <summary>
    /// Dota's two barracks per lane (Liquipedia «Lane Creeps» and «Buildings», 7.41f): losing the melee one makes the enemy's
    /// melee creeps on that lane super, losing the ranged one their ranged and siege creeps (siege: +16 damage); with every
    /// barracks of a team down all lanes get mega creeps; flagbearers never change. Waves every second, one of each.
    /// </summary>
    private static SimulationKernel<MatchState> TwoBarracks()
    {
        var draft = new RulesetDraft { IntervalMs = 1000 };
        foreach (string type in new[] { "ranged", "siege", "flagbearer", "melee_super", "ranged_super", "siege_super", "melee_mega", "ranged_mega", "siege_mega" })
            draft.Creeps.Add(new CreepType(type, MeleeStats(), new Bounty(40, 40, 60)));
        draft.DawnWave.Clear();
        draft.DuskWave.Clear();
        foreach (string type in new[] { "melee", "ranged", "siege", "flagbearer" })
        {
            draft.DawnWave.Add(new WaveEntry(type, 1));
            draft.DuskWave.Add(new WaveEntry(type, 1));
        }

        draft.SuperUpgrades.AddRange(new[] { new CreepUpgrade("melee", "melee_super"), new CreepUpgrade("ranged", "ranged_super"), new CreepUpgrade("siege", "siege_super") });
        draft.MegaUpgrades.AddRange(new[] { new CreepUpgrade("melee", "melee_mega"), new CreepUpgrade("ranged", "ranged_mega"), new CreepUpgrade("siege", "siege_mega") });
        draft.StructureTypes.Add(new StructureType("barracks.melee", AncientStats(), upgradesCreepTypeIds: new[] { "melee" }));
        draft.StructureTypes.Add(new StructureType("barracks.ranged", AncientStats(), upgradesCreepTypeIds: new[] { "ranged", "siege" }));
        draft.Lanes.Add(new LaneDefinition("top", new[] { P(1000, 9000), P(9000, 9000) }));
        foreach ((string lane, int y) in new[] { ("mid", 6000), ("top", 9500) })
        {
            draft.Placements.Add(new StructurePlacement("dusk." + lane + ".melee_rax", Team.Dusk, "barracks.melee", P(9500, y), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed, barracksLane: lane));
            draft.Placements.Add(new StructurePlacement("dusk." + lane + ".ranged_rax", Team.Dusk, "barracks.ranged", P(9000, y), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed, barracksLane: lane));
        }

        return draft.Start();
    }

    private static void Raze(SimulationKernel<MatchState> kernel, long tick, string placement) =>
        At(kernel, tick, ctx =>
        {
            MatchUnit building = ctx.State.Units.Single(u => u.Placement?.Id == placement);
            MatchUnit ancient = ctx.State.Units.Single(u => u.Placement?.Id == "dawn.ancient");
            Damage.Deal(ctx, ancient.Id, building, Fixed.FromInt(99999), DamageType.Pure);
        });

    private static Dictionary<string, int> DawnWaveAt(SimulationKernel<MatchState> kernel, int second, string lane) =>
        kernel.TimedEventsOf<CreepSpawned>()
            .Where(e => e.Tick == second * 30L && e.Event.Team == Team.Dawn && e.Event.LaneId == lane)
            .GroupBy(e => e.Event.CreepTypeId).ToDictionary(g => g.Key, g => g.Count());

    [Fact]
    public void TheMeleeBarracks_UpgradesMeleeCreeps_TheRangedOne_RangedAndSiegeCreeps_OnItsLane()
    {
        var kernel = TwoBarracks();
        Raze(kernel, 15, "dusk.mid.melee_rax");
        Raze(kernel, 45, "dusk.mid.ranged_rax");
        kernel.RunTicks(61);

        Assert.Equal(new Dictionary<string, int> { ["melee"] = 1, ["ranged"] = 1, ["siege"] = 1, ["flagbearer"] = 1 }, DawnWaveAt(kernel, 0, "mid"));
        Assert.Equal(new Dictionary<string, int> { ["melee_super"] = 1, ["ranged"] = 1, ["siege"] = 1, ["flagbearer"] = 1 }, DawnWaveAt(kernel, 1, "mid"));
        Assert.Equal(new Dictionary<string, int> { ["melee_super"] = 1, ["ranged_super"] = 1, ["siege_super"] = 1, ["flagbearer"] = 1 }, DawnWaveAt(kernel, 2, "mid"));
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 1, ["ranged"] = 1, ["siege"] = 1, ["flagbearer"] = 1 }, DawnWaveAt(kernel, 2, "top"));
    }

    [Fact]
    public void Validation_RejectsABarracksUpgradingAnUnknownCreepType_AndUnknownMegaCreeps()
    {
        var draft = new RulesetDraft();
        draft.StructureTypes.Add(new StructureType("barracks.odd", AncientStats(), upgradesCreepTypeIds: new[] { "golem" }));
        draft.MegaUpgrades.Add(new CreepUpgrade("melee", "melee_titan"));

        IReadOnlyList<string> errors = draft.Build().Validate();
        Assert.Contains(errors, e => e.Contains("golem"));
        Assert.Contains(errors, e => e.Contains("melee_titan"));
    }

    [Fact]
    public void WithEveryEnemyBarracksDown_AllLanesGetMegaCreeps_ButNotFlagbearers()
    {
        var kernel = TwoBarracks();
        foreach (string placement in new[] { "dusk.mid.melee_rax", "dusk.mid.ranged_rax", "dusk.top.melee_rax" })
            Raze(kernel, 15, placement);
        Raze(kernel, 45, "dusk.top.ranged_rax");
        kernel.RunTicks(61);

        Assert.Equal(new Dictionary<string, int> { ["melee_super"] = 1, ["ranged"] = 1, ["siege"] = 1, ["flagbearer"] = 1 }, DawnWaveAt(kernel, 1, "top"));
        foreach (string lane in new[] { "mid", "top" })
            Assert.Equal(new Dictionary<string, int> { ["melee_mega"] = 1, ["ranged_mega"] = 1, ["siege_mega"] = 1, ["flagbearer"] = 1 }, DawnWaveAt(kernel, 2, lane));
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 1, ["ranged"] = 1, ["siege"] = 1, ["flagbearer"] = 1 },
            kernel.TimedEventsOf<CreepSpawned>().Where(e => e.Tick == 60 && e.Event.Team == Team.Dusk && e.Event.LaneId == "mid")
                .GroupBy(e => e.Event.CreepTypeId).ToDictionary(g => g.Key, g => g.Count()));
    }
}
