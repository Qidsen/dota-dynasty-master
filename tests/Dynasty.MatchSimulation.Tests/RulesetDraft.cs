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
        new(hp, Fixed.FromInt(armor), 0, dmg, dmg, range, 1000, pointMs, range, 100);

    public static UnitStats AncientStats(int hp = 3000) =>
        new(hp, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 150);

    public static FixedVector2 P(int x, int y) => FixedVector2.FromInts(x, y);

    public string Id { get; set; } = "dynasty.test";
    public int Version { get; set; } = 1;
    public int TicksPerSecond { get; set; } = 30;
    public Fixed ArmorFactor { get; set; } = Fixed.FromRatio(6, 100);
    public int Width { get; set; } = 10000;
    public int Height { get; set; } = 10000;

    public List<CreepType> Creeps { get; } = new() { new CreepType("melee", MeleeStats()) };

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

    public int FirstSpawnMs { get; set; } = 0;
    public int IntervalMs { get; set; } = 30000;
    public List<WaveEntry> DawnWave { get; } = new() { new WaveEntry("melee", 1) };
    public List<WaveEntry> DuskWave { get; } = new() { new WaveEntry("melee", 1) };

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
        new MapDefinition(Width, Height, Lanes, Placements, Obstacles),
        new WaveRules(FirstSpawnMs, IntervalMs, DawnWave, DuskWave));
}
