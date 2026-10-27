using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Dota 7.41f structures (docs/DOTA_RULES_AUDIT.md; Liquipedia «Buildings» as of 7.41f; Valve 7.27; decisions D-051):
/// a tower gives its team bounty to every enemy hero and the last hitter a further 110–130 (T1); a barracks' last hitter
/// gets only the last-hit bounty; a building below 10% HP can be denied — a denied tower gives both teams half its team
/// bounty, a denied barracks still gives its enemies the full team bounty; the ancient cannot be denied.
/// </summary>
public class DotaStructureTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly StructureBounty Tower = new(teamGold: 90, lastHitGoldMin: 110, lastHitGoldMax: 130, lastHitterGetsTeamGold: true,
        deniedEnemyGoldPercent: 50, deniedOwnGoldPercent: 50);

    private static readonly StructureBounty Barracks = new(teamGold: 155, lastHitGoldMin: 90, lastHitGoldMax: 135, lastHitterGetsTeamGold: false,
        deniedEnemyGoldPercent: 100, deniedOwnGoldPercent: 0);

    /// <summary>Dawn: alpha, gamma; Dusk: beta, delta; all far from the towers. Dusk also has a barracks.</summary>
    private static SimulationKernel<MatchState> Arena()
    {
        RulesetDraft draft = TestAbilities.Draft();
        draft.StructureTypes.Clear();
        draft.StructureTypes.Add(new StructureType("tower", TowerStats(), Tower, denyHealthPercent: 10));
        draft.StructureTypes.Add(new StructureType("ancient", AncientStats()));
        draft.StructureTypes.Add(new StructureType("barracks", AncientStats(), Barracks, denyHealthPercent: 10));
        draft.Placements.Add(new StructurePlacement("dusk.mid.barracks", Team.Dusk, "barracks", P(8500, 3000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 2000);
            H(ctx.State, "gamma").Position = P(5000, 2200);
            H(ctx.State, "beta").Position = P(5000, 8000);
            H(ctx.State, "delta").Position = P(5000, 8200);
        });
        return kernel;
    }

    private static MatchUnit S(MatchState state, string placement) => state.Units.Single(u => u.Placement?.Id == placement);

    private static Dictionary<string, double> Gold(MatchState state) =>
        new[] { "alpha", "gamma", "beta", "delta" }.ToDictionary(h => h, h => H(state, h).Hero!.Gold.ToDouble());

    private static Dictionary<string, double> GainsAfter(SimulationKernel<MatchState> kernel, Action<SimulationContext<MatchState>> kill)
    {
        Dictionary<string, double> before = new();
        At(kernel, 1, ctx => before = Gold(ctx.State));
        At(kernel, 2, kill);
        kernel.RunTicks(4);
        return Gold(kernel.State).ToDictionary(p => p.Key, p => Math.Floor(p.Value - before[p.Key]));   // without the passive income of these ticks
    }

    [Fact]
    public void TowerLastHit_GivesTheTeamBounty_AndTheLastHitterABonus()
    {
        var kernel = Arena();
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, S(ctx.State, "dusk.mid.t1"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.InRange(gains["alpha"], 90 + 110, 90 + 130);
        Assert.Equal(90, gains["gamma"]);
        Assert.Equal((0d, 0d), (gains["beta"], gains["delta"]));
    }

    [Fact]
    public void TowerDestroyedWithoutAHeroLastHit_GivesOnlyTheTeamBounty()
    {
        var kernel = Arena();
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, default, S(ctx.State, "dusk.mid.t1"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.Equal((90d, 90d), (gains["alpha"], gains["gamma"]));
    }

    [Fact]
    public void BarracksLastHitter_GetsOnlyTheLastHitBounty()
    {
        var kernel = Arena();
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, S(ctx.State, "dusk.mid.barracks"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.InRange(gains["alpha"], 90, 135);
        Assert.Equal(155, gains["gamma"]);
    }

    [Fact]
    public void ABuildingCanBeDeniedOnlyBelowTenPercent_AndADeniedTowerGivesBothTeamsHalf()
    {
        var kernel = Arena();
        bool above = true, below = false, ancient = true;
        At(kernel, 1, ctx =>
        {
            MatchUnit tower = S(ctx.State, "dusk.mid.t1");
            tower.Hp = Fixed.FromInt(tower.MaxHp / 10);
            above = ctx.State.CanAttack(H(ctx.State, "beta"), tower);
            tower.Hp = Fixed.FromInt(tower.MaxHp / 10 - 1);
            below = ctx.State.CanAttack(H(ctx.State, "beta"), tower);
        });
        At(kernel, 3, ctx =>
        {
            MatchUnit fort = S(ctx.State, "dusk.ancient");   // vulnerable once the tower is gone
            fort.Hp = Fixed.One;
            ancient = ctx.State.CanAttack(H(ctx.State, "beta"), fort) || !ctx.State.CanAttack(H(ctx.State, "alpha"), fort);
        });
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, S(ctx.State, "dusk.mid.t1"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.False(above);
        Assert.True(below);
        Assert.False(ancient);
        Assert.Equal(new[] { 45d, 45d, 45d, 45d }, new[] { gains["alpha"], gains["gamma"], gains["beta"], gains["delta"] });
    }

    [Fact]
    public void ADeniedBarracks_StillGivesItsEnemiesTheFullTeamBounty()
    {
        var kernel = Arena();
        var gains = GainsAfter(kernel, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, S(ctx.State, "dusk.mid.barracks"), Fixed.FromInt(99999), DamageType.Pure));

        Assert.Equal(new[] { 155d, 155d, 0d, 0d }, new[] { gains["alpha"], gains["gamma"], gains["beta"], gains["delta"] });
    }

    /// <summary>Tower Protection: +armor and +regen to allied heroes within 900, lingering 0.5 s; the same aura does not stack.</summary>
    private static ModifierSpec TowerAura(int armor, int regen) => new("tower_aura_source", -1,
        aura: new AuraSpec(900, TargetTeam.Ally, heroesOnly: true,
            new ModifierSpec("tower_aura", 500, armorBonus: Fixed.FromInt(armor), hpRegenBonus: Fixed.FromInt(regen), unique: true)));

    private static SimulationKernel<MatchState> AuraArena(Action<RulesetDraft> configure)
    {
        RulesetDraft draft = TestAbilities.Draft();
        configure(draft);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 5500);   // 500 from the Dawn tower
            H(ctx.State, "gamma").Position = P(3000, 6200);   // 1200 away
            H(ctx.State, "beta").Position = P(3000, 4500);    // an enemy near the Dawn tower
            H(ctx.State, "delta").Position = P(5000, 8000);
        });
        return kernel;
    }

    private static Fixed BonusArmor(MatchState state, string hero) => H(state, hero).Armor - H(state, hero).Stats.Armor;

    [Fact]
    public void TowerAura_GivesArmorAndRegenToAlliedHeroesNearby_Only()
    {
        var kernel = AuraArena(draft =>
        {
            draft.StructureTypes.Clear();
            draft.StructureTypes.Add(new StructureType("tower", TowerStats(dmg: 0), modifiers: new[] { TowerAura(3, 1) }));
            draft.StructureTypes.Add(new StructureType("ancient", AncientStats()));
        });
        kernel.RunTicks(30);

        Assert.Equal(Fixed.FromInt(3), BonusArmor(kernel.State, "alpha"));
        Assert.Equal(Fixed.Zero, BonusArmor(kernel.State, "gamma"));
        Assert.Equal(Fixed.Zero, BonusArmor(kernel.State, "beta"));
        Assert.Contains(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "tower_aura" && m.Spec.HpRegenBonus == Fixed.One);
    }

    [Fact]
    public void TheSameAuraFromTwoTowers_DoesNotStack_TheStrongerCounts()
    {
        var kernel = AuraArena(draft =>
        {
            draft.StructureTypes.Clear();
            draft.StructureTypes.Add(new StructureType("tower", TowerStats(dmg: 0), modifiers: new[] { TowerAura(3, 1) }));
            draft.StructureTypes.Add(new StructureType("tower.t2", TowerStats(dmg: 0), modifiers: new[] { TowerAura(5, 3) }));
            draft.StructureTypes.Add(new StructureType("ancient", AncientStats()));
            draft.Placements.Add(new StructurePlacement("dawn.mid.t2", Team.Dawn, "tower.t2", P(2300, 5500), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));
            draft.Placements.Add(new StructurePlacement("dawn.mid.t2b", Team.Dawn, "tower.t2", P(2300, 5600), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));
        });
        kernel.RunTicks(30);

        Assert.Equal(Fixed.FromInt(5), BonusArmor(kernel.State, "alpha"));
    }

    [Fact]
    public void ATier4Tower_GetsArmorForEachStandingBarracks()
    {
        var kernel = AuraArena(draft =>
        {
            draft.StructureTypes.Add(new StructureType("tower.t4", TowerStats(dmg: 0), armorPerStandingBarracks: 4));
            draft.StructureTypes.Add(new StructureType("barracks", AncientStats()));
            draft.Placements.Add(new StructurePlacement("dusk.base.t4", Team.Dusk, "tower.t4", P(8500, 6000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));
            draft.Placements.Add(new StructurePlacement("dusk.top.barracks", Team.Dusk, "barracks", P(8500, 7000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed, barracksLane: "mid"));
            draft.Placements.Add(new StructurePlacement("dusk.bot.barracks", Team.Dusk, "barracks", P(8500, 3000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed, barracksLane: "mid"));
            draft.Placements.Add(new StructurePlacement("dawn.bot.barracks", Team.Dawn, "barracks", P(1500, 3000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed, barracksLane: "mid"));
        });
        Fixed before = Fixed.Zero, after = Fixed.Zero;
        At(kernel, 1, ctx => before = S(ctx.State, "dusk.base.t4").Armor);
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, S(ctx.State, "dusk.top.barracks"), Fixed.FromInt(99999), DamageType.Pure));
        At(kernel, 4, ctx => after = S(ctx.State, "dusk.base.t4").Armor);
        kernel.RunTicks(5);

        Fixed baseArmor = S(kernel.State, "dusk.base.t4").Stats.Armor;
        Assert.Equal((baseArmor + Fixed.FromInt(8), baseArmor + Fixed.FromInt(4)), (before, after));
    }

    /// <summary>
    /// Backdoor Protection: without enemy lane creeps in its radius (its own 900, or 4000 around the ancient for buildings in
    /// the base) a building takes 50% less damage (75% from illusions and summons) and heals the enemy damage at 180 HP/s;
    /// an enemy lane creep in the radius turns it off until 15.5 s after the last one leaves.
    /// </summary>
    private static SimulationKernel<MatchState> BackdoorArena()
    {
        RulesetDraft draft = TestAbilities.Draft();
        var own = new BackdoorProtection(radius: 900, aroundAncient: false, damageReductionPercent: 50, summonDamageReductionPercent: 75, healPerSecond: 180, reactivationMs: 15500);
        var inBase = new BackdoorProtection(radius: 4000, aroundAncient: true, damageReductionPercent: 50, summonDamageReductionPercent: 75, healPerSecond: 180, reactivationMs: 15500);
        draft.StructureTypes.Clear();
        draft.StructureTypes.Add(new StructureType("tower", TowerStats(dmg: 0), backdoor: own));
        draft.StructureTypes.Add(new StructureType("ancient", AncientStats(), backdoor: inBase));
        draft.StructureTypes.Add(new StructureType("barracks", AncientStats(), backdoor: inBase));
        draft.Placements.Add(new StructurePlacement("dusk.mid.barracks", Team.Dusk, "barracks", P(9000, 6500), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed, barracksLane: "mid"));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 2000);
            H(ctx.State, "beta").Position = P(5000, 8000);
        });
        return kernel;
    }

    private static Fixed Hit(SimulationContext<MatchState> ctx, EntityId attacker, string placement, int amount)
    {
        MatchUnit building = S(ctx.State, placement);
        Fixed before = building.Hp;
        Damage.Deal(ctx, attacker, building, Fixed.FromInt(amount), DamageType.Pure);
        return before - building.Hp;
    }

    private static MatchUnit DawnCreepAt(SimulationContext<MatchState> ctx, FixedVector2 position)
    {
        MatchUnit creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Dawn, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
        creep.Position = position;
        return creep;
    }

    [Fact]
    public void AProtectedBuilding_TakesHalfTheDamage_AndHealsItBack()
    {
        var kernel = BackdoorArena();
        Fixed taken = Fixed.Zero;
        At(kernel, 2, ctx => taken = Hit(ctx, H(ctx.State, "alpha").Id, "dusk.mid.t1", 100));
        kernel.RunTicks(20);   // 50 HP back at 180/s: within 0.3 s

        Assert.Equal(Fixed.FromInt(50), taken);
        MatchUnit tower = S(kernel.State, "dusk.mid.t1");
        Assert.Equal(Fixed.FromInt(tower.MaxHp), tower.Hp);
    }

    [Fact]
    public void ASummon_DealsAQuarterToAProtectedBuilding()
    {
        var kernel = BackdoorArena();
        Fixed taken = Fixed.Zero;
        At(kernel, 2, ctx =>
        {
            MatchUnit bug = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "alpha"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, ctx.Tick.Value + 100000);
            taken = Hit(ctx, bug.Id, "dusk.mid.t1", 100);
        });
        kernel.RunTicks(3);

        Assert.Equal(Fixed.FromInt(25), taken);
    }

    [Fact]
    public void AnEnemyLaneCreepNearby_TurnsItOff_UntilFifteenAndAHalfSecondsAfterItLeaves()
    {
        var kernel = BackdoorArena();
        MatchUnit? creep = null;
        Fixed whileThere = Fixed.Zero, soonAfter = Fixed.Zero, later = Fixed.Zero, healed = Fixed.Zero;
        At(kernel, 2, ctx => creep = DawnCreepAt(ctx, P(6300, 5000)));   // 700 from the tower
        At(kernel, 4, ctx => whileThere = Hit(ctx, H(ctx.State, "alpha").Id, "dusk.mid.t1", 100));
        At(kernel, 5, ctx => Damage.Deal(ctx, default, creep!, Fixed.FromInt(99999), DamageType.Pure));
        At(kernel, 30, ctx => healed = Fixed.FromInt(S(ctx.State, "dusk.mid.t1").MaxHp) - S(ctx.State, "dusk.mid.t1").Hp);
        At(kernel, 5 + 450, ctx => soonAfter = Hit(ctx, H(ctx.State, "alpha").Id, "dusk.mid.t1", 100));   // 15 s after
        At(kernel, 5 + 480, ctx => later = Hit(ctx, H(ctx.State, "alpha").Id, "dusk.mid.t1", 100));      // 16 s after
        kernel.RunTicks(5 + 482);

        Assert.Equal(Fixed.FromInt(100), whileThere);
        Assert.True(healed >= Fixed.FromInt(100), "no healing while off");
        Assert.Equal(Fixed.FromInt(100), soonAfter);
        Assert.Equal(Fixed.FromInt(50), later);
    }

    [Fact]
    public void BuildingsInTheBase_AreProtectedByTheAncientsRadius()
    {
        var kernel = BackdoorArena();
        Fixed barracks = Fixed.Zero, tower = Fixed.Zero;
        At(kernel, 2, ctx => DawnCreepAt(ctx, P(6500, 2500)));   // 3900 from the ancient; 4700 from the barracks, 2550 from the tower
        At(kernel, 4, ctx =>
        {
            barracks = Hit(ctx, H(ctx.State, "alpha").Id, "dusk.mid.barracks", 100);
            tower = Hit(ctx, H(ctx.State, "alpha").Id, "dusk.mid.t1", 100);
        });
        kernel.RunTicks(5);

        Assert.Equal((Fixed.FromInt(100), Fixed.FromInt(50)), (barracks, tower));
    }
}
