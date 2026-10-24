using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests;

/// <summary>
/// Mutable builder for tiny test rulesets. Default: one straight lane along y=5000,
/// one tower and one ancient per side, one melee creep per wave.
/// </summary>
public sealed class RulesetDraft
{
    public static UnitStats MeleeStats(
        int hp = 550, int armor = 2, int dmgMin = 20, int dmgMax = 20, int range = 100,
        int intervalMs = 1000, int pointMs = 500, int speed = 300, int acquisition = 500) =>
        new(hp, Fixed.FromInt(armor), speed, dmgMin, dmgMax, range, intervalMs, pointMs, acquisition, 16);

    public static UnitStats TowerStats(int hp = 1800, int armor = 0, int dmg = 100, int range = 700, int pointMs = 0) =>
        new(hp, Fixed.FromInt(armor), 0, dmg, dmg, range, 1000, pointMs, range, 100, visionRange: 900, trueSightRange: 700);

    public static UnitStats AncientStats(int hp = 3000) =>
        new(hp, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 150);

    public static FixedVector2 P(int x, int y) => FixedVector2.FromInts(x, y);

    public string Id { get; set; } = "dynasty.test";
    public int Version { get; set; } = 1;
    public int TicksPerSecond { get; set; } = 30;
    public Fixed ArmorFactor { get; set; } = Fixed.FromRatio(6, 100);
    public int Width { get; set; } = 10000;
    public int Height { get; set; } = 10000;

    public List<CreepType> Creeps { get; } = new() { new CreepType("melee", MeleeStats(), new Bounty(40, 40, 60)) };

    public List<StructureType> StructureTypes { get; } = new()
    {
        new StructureType("tower", TowerStats()),
        new StructureType("ancient", AncientStats()),
    };

    public List<LaneDefinition> Lanes { get; } = new()
    {
        new LaneDefinition("mid", new[] { P(1000, 5000), P(9000, 5000) }),
    };

    public List<StructurePlacement> Placements { get; } = new()
    {
        new StructurePlacement("dawn.mid.t1", Team.Dawn, "tower", P(3000, 5000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed),
        new StructurePlacement("dawn.ancient", Team.Dawn, "ancient", P(500, 5000), true, new[] { "dawn.mid.t1" }, VulnerabilityRule.AllProtectorsDestroyed),
        new StructurePlacement("dusk.mid.t1", Team.Dusk, "tower", P(7000, 5000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed),
        new StructurePlacement("dusk.ancient", Team.Dusk, "ancient", P(9500, 5000), true, new[] { "dusk.mid.t1" }, VulnerabilityRule.AllProtectorsDestroyed),
    };

    public List<ObstacleDefinition> Obstacles { get; } = new();

    public List<NeutralCampDefinition> Camps { get; } = new();

    public static UnitStats HeroStats(int hp = 600, int dmg = 50, int range = 150, int speed = 300, int intervalMs = 1500, int pointMs = 300) =>
        new(hp, Fixed.FromInt(2), speed, dmg, dmg + 4, range, intervalMs, pointMs, 800, 24, 25, visionRange: 1200);

    public static HeroDefinition Hero(string id, UnitStats? stats = null, IReadOnlyList<string>? abilities = null) =>
        new(id, id, new[] { "carry" }, stats ?? HeroStats(), hpPerLevel: 80, armorPerLevel: Fixed.FromRatio(3, 10),
            damagePerLevel: 3, baseMana: 300, manaPerLevel: 40, hpRegenPerSecond: Fixed.FromInt(2), manaRegenPerSecond: Fixed.One,
            abilityIds: abilities);

    public List<HeroDefinition> Heroes { get; } = new() { Hero("alpha"), Hero("beta"), Hero("gamma") };

    public List<AbilityDefinition> Abilities { get; } = new();

    public List<ItemDefinition> Items { get; } = new();

    public BossDefinition? Boss { get; set; }

    public List<int> XpTable { get; } = new() { 0, 200, 500, 900, 1400, 2000 };
    public int XpShareRadius { get; set; } = 1200;
    public int GoldPerMinute { get; set; } = 90;
    public int StartingGold { get; set; } = 600;
    public int RespawnBaseMs { get; set; } = 4000;
    public int RespawnPerLevelMs { get; set; } = 2000;

    public List<FountainPlacement> Fountains { get; } = new()
    {
        new FountainPlacement(Team.Dawn, P(200, 5000), 400),
        new FountainPlacement(Team.Dusk, P(9800, 5000), 400),
    };

    public int BuybackBaseGold { get; set; }
    public int BuybackGoldPerLevel { get; set; }
    public int BuybackCooldownMs { get; set; }

    public HeroRules BuildHeroRules() => new(
        XpTable, XpShareRadius,
        heroKillXpBase: 100, heroKillXpPerLevel: 20,
        heroKillGoldBase: 200, heroKillGoldPerLevel: 10,
        assistWindowMs: 10000, GoldPerMinute, StartingGold,
        RespawnBaseMs, RespawnPerLevelMs,
        goldLossPerLevel: 30, structureTeamGold: 100, fountainHealPercentPerSecond: 5,
        buybackBaseGold: BuybackBaseGold, buybackGoldPerLevel: BuybackGoldPerLevel, buybackCooldownMs: BuybackCooldownMs);

    public int FirstSpawnMs { get; set; } = 0;
    public int IntervalMs { get; set; } = 30000;
    public List<WaveEntry> DawnWave { get; } = new() { new WaveEntry("melee", 1) };
    public List<WaveEntry> DuskWave { get; } = new() { new WaveEntry("melee", 1) };
    public List<CreepUpgrade> SuperUpgrades { get; } = new();

    public RulesetDraft WithoutStructures()
    {
        Placements.Clear();
        return this;
    }

    public RulesetDraft WithoutWaves()
    {
        DawnWave.Clear();
        DuskWave.Clear();
        return this;
    }

    public RulesetContent Build() => new(
        Id,
        Version,
        TicksPerSecond,
        new CombatRules(ArmorFactor),
        Creeps,
        StructureTypes,
        new MapDefinition(Width, Height, Lanes, Placements, Obstacles, fountains: Fountains, camps: Camps),
        new WaveRules(FirstSpawnMs, IntervalMs, DawnWave, DuskWave, SuperUpgrades),
        Heroes,
        BuildHeroRules(),
        Abilities,
        Items,
        Boss);

    /// <summary>Starts a match with the given heroes; each pick is (heroId, laneId).</summary>
    public Dynasty.Core.Kernel.SimulationKernel<MatchState> StartWithHeroes(
        IEnumerable<(string Hero, string Lane)> dawn,
        IEnumerable<(string Hero, string Lane)> dusk,
        ulong seed = 1) =>
        Match.Create(Build(), seed, new MatchLineup(
            dawn.Select(p => new HeroPick(p.Hero, p.Lane)).ToList(),
            dusk.Select(p => new HeroPick(p.Hero, p.Lane)).ToList()));
}
