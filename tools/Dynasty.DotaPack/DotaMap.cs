using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;

/// <summary>
/// Dota's map of patch 7.41f (decision D-053) from sikleq/Oldgrowth at a pinned commit: data its scripts extract from the game's
/// maps/dota.vpk (client update of 2026-10-07, the latest inside 7.41f) — the walk grid (gridnav), height levels in steps of 128,
/// vision blockers, trees and the map's entities. Dota's coordinates start at (min_x, min_y) of the grid; the pack's at 0, so
/// everything is shifted by (−min_x, −min_y).
/// </summary>
sealed class DotaMap
{
    public const string Commit = "2de3a39bb13850d9e760dc104be51ca0a7af593e";   // 2026-10-20
    public const string Version = "7.41f-1007";

    /// <summary>Fountain aura (Fandom «Fountain»): heals within 1200; the fountain's shop is the same circle here (B-157).</summary>
    private const int FountainRadius = 1200;

    private DotaMap(JsonObject map, (int X, int Y)[] powerSpots, (int X, int Y)[] bountySpots, (int X, int Y) roshanTop, (int X, int Y) roshanBottom,
        (int X, int Y) tormentorTop, (int X, int Y) tormentorBottom, IReadOnlyList<string> warnings)
    {
        TormentorTop = tormentorTop;
        TormentorBottom = tormentorBottom;
        RoshanBottom = roshanBottom;
        Map = map;
        PowerSpots = powerSpots;
        BountySpots = bountySpots;
        RoshanTop = roshanTop;
        Warnings = warnings;
    }

    public JsonObject Map { get; }
    public (int X, int Y)[] PowerSpots { get; }
    public (int X, int Y)[] BountySpots { get; }

    /// <summary>Roshan's pits: the top one by day, the bottom one by night (Valve 7.41).</summary>
    public (int X, int Y) RoshanTop { get; }

    public (int X, int Y) RoshanBottom { get; }

    /// <summary>The Tormentor's places (the map's miniboss spawners): the top-left corner and the bottom-right one.</summary>
    public (int X, int Y) TormentorTop { get; }

    public (int X, int Y) TormentorBottom { get; }

    /// <summary>The Shrines of Wisdom (the map's xp fountains).</summary>
    public (int X, int Y)[] Shrines { get; private set; } = Array.Empty<(int X, int Y)>();

    /// <summary>Points that are not on walkable ground (lane corners, camps, runes): printed by the generator.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public static DotaMap Load(string cache)
    {
        Directory.CreateDirectory(cache);
        using var http = new HttpClient();
        byte[] File(string name)
        {
            string path = Path.Combine(cache, name);
            if (!System.IO.File.Exists(path))
            {
                string url = $"https://raw.githubusercontent.com/sikleq/Oldgrowth/{Commit}/versions/{Version}/{name}";
                Console.WriteLine("download " + url);
                System.IO.File.WriteAllBytes(path, http.GetByteArrayAsync(url).GetAwaiter().GetResult());
            }

            return System.IO.File.ReadAllBytes(path);
        }

        byte[] Gunzip(byte[] bytes)
        {
            using var input = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }

        JsonNode grid = JsonNode.Parse(File("info.json"))!["grid"]!;
        int columns = grid["w"]!.GetValue<int>(), rows = grid["h"]!.GetValue<int>(), cell = grid["edge"]!.GetValue<int>();
        int offsetX = -(int)grid["min_x"]!.GetValue<double>(), offsetY = -(int)grid["min_y"]!.GetValue<double>();
        byte[] gridnav = Gunzip(File("gridnav.bin.gz")), elevation = Gunzip(File("elev.bin.gz")), fow = Gunzip(File("fow.bin.gz"));
        foreach (byte[] layer in new[] { gridnav, elevation, fow })
            if (layer.Length != columns * rows)
                throw new InvalidDataException($"Oldgrowth {Version}: a grid layer has {layer.Length} cells, expected {columns}×{rows}.");
        JsonNode data = JsonNode.Parse(File("mapdata.json"))!["data"]!;
        JsonArray entities = JsonNode.Parse(Gunzip(File("entities.json.gz")))!.AsArray();

        (int X, int Y) At(double x, double y) => ((int)Math.Round(x) + offsetX, (int)Math.Round(y) + offsetY);
        (int X, int Y) Point(JsonNode p) => At(p["x"]!.GetValue<double>(), p["y"]!.GetValue<double>());
        JsonArray Flat(IEnumerable<(int X, int Y)> points) => new(points.SelectMany(p => new JsonNode[] { p.X, p.Y }).ToArray());
        var warnings = new List<string>();
        void MustWalk(string what, (int X, int Y) p)
        {
            int index = p.Y / cell * columns + p.X / cell;
            if ((gridnav[index] & 1) == 0)
                warnings.Add($"{what} at {p} is not on walkable ground");
        }

        // The map cell by cell: gridnav bit 0 walkable, 1 in the vision blocker layer, heights 0 (river) and up; 255 = no ground.
        var walkable = new byte[(columns * rows + 7) / 8];
        var blockers = new byte[walkable.Length];
        var levels = new byte[columns * rows];
        for (int i = 0; i < columns * rows; i++)
        {
            if ((gridnav[i] & 1) != 0)
                walkable[i >> 3] |= (byte)(1 << (i & 7));
            if (fow[i] != 0)
                blockers[i >> 3] |= (byte)(1 << (i & 7));
            levels[i] = elevation[i] == 255 ? (byte)0 : elevation[i];
        }

        var trees = data["ent_dota_tree"]!.AsArray().Select(t => Point(t!)).ToList();

        // Lanes: each side's creeps walk from their spawner along their own chain of path corners.
        var corners = data["path_corner"]!.AsArray().ToDictionary(p => p!["name"]!.GetValue<string>(), p => p!);
        List<(int X, int Y)> Chain(string team, string lane)
        {
            JsonNode spawner = data["npc_dota_spawner"]!.AsArray().Single(s => s!["team"]!.GetValue<string>() == team && s["lane"]!.GetValue<string>() == lane)!;
            var chain = new List<(int X, int Y)> { Point(spawner) };
            var seen = new HashSet<string>();
            for (string? next = spawner["first"]!.GetValue<string>(); !string.IsNullOrEmpty(next) && seen.Add(next); next = corners[next]["next"]!.GetValue<string>())
                chain.Add(Point(corners[next]));
            foreach ((int X, int Y) p in chain)
                MustWalk($"{team} {lane} lane corner", p);
            return chain;
        }

        string[] laneIds = { "top", "mid", "bot" };
        var lanes = new JsonArray(laneIds.Select(l => (JsonNode)new JsonObject
        {
            ["id"] = l, ["dawn"] = Flat(Chain("good", l)), ["dusk"] = Flat(Chain("bad", l)),
        }).ToArray());

        // Buildings from the map's entities, by their names: dota_goodguys_tower2_mid, good_rax_range_top, good_filler_3, dota_goodguys_fort.
        // Liquipedia «Buildings» (7.41f): a tower is invulnerable until the tower before it in its lane falls, the tier 4 towers until
        // any tier 3 does (the map: invuln_count 1), the barracks until their lane's tier 3, the ancient until both tier 4 (invuln_count 2).
        var named = entities.Where(e => e is JsonObject o && o["targetname"] is JsonValue)
            .Select(e => (Name: e!["targetname"]!.GetValue<string>().Replace("[PR#]", ""), Entity: e!))
            .ToList();
        var structures = new JsonArray();
        foreach ((string side, string team) in new[] { ("good", "dawn"), ("bad", "dusk") })
        {
            (int X, int Y) Origin(string name)
            {
                JsonArray origin = named.Single(n => n.Name == name).Entity["origin"]!.AsArray();
                return At(origin[0]!.GetValue<double>(), origin[1]!.GetValue<double>());
            }

            JsonObject Building(string id, string type, (int X, int Y) at, string[] protectors, string? rule = null, string? barracksLane = null)
            {
                var building = new JsonObject { ["id"] = id, ["team"] = team, ["type"] = type, ["x"] = at.X, ["y"] = at.Y };
                if (protectors.Length > 0)
                    building["protectedBy"] = new JsonArray(protectors.Select(p => (JsonNode)p).ToArray());
                if (rule != null)
                    building["vulnerability"] = rule;
                if (barracksLane != null)
                    building["barracksLane"] = barracksLane;
                return building;
            }

            string[] tier3 = laneIds.Select(l => $"{team}.{l}.t3").ToArray();
            foreach (string lane in laneIds)
            {
                for (int tier = 1; tier <= 3; tier++)
                    structures.Add(Building($"{team}.{lane}.t{tier}", "tower.t" + tier, Origin($"dota_{side}guys_tower{tier}_{lane}"),
                        tier == 1 ? Array.Empty<string>() : new[] { $"{team}.{lane}.t{tier - 1}" }));
                structures.Add(Building($"{team}.{lane}.melee_rax", "barracks.melee", Origin($"{side}_rax_melee_{lane}"), new[] { $"{team}.{lane}.t3" }, barracksLane: lane));
                structures.Add(Building($"{team}.{lane}.ranged_rax", "barracks.ranged", Origin($"{side}_rax_range_{lane}"), new[] { $"{team}.{lane}.t3" }, barracksLane: lane));
            }

            structures.Add(Building($"{team}.base.t4a", "tower.t4", Origin($"dota_{side}guys_tower4_top"), tier3));
            structures.Add(Building($"{team}.base.t4b", "tower.t4", Origin($"dota_{side}guys_tower4_bot"), tier3));
            // Effigy buildings (7 a side as of 7.41f; the map: invuln_count 1): which fall opens them is not in the map's data
            // nor in the wikis' texts — for now as the tier 4 towers, any tier 3 (the project owner's decision, B-156).
            foreach (var (name, _) in named.Where(n => n.Name.StartsWith(side + "_filler_", StringComparison.Ordinal)).OrderBy(n => n.Name, StringComparer.Ordinal))
                structures.Add(Building($"{team}.base.effigy{name[(side + "_filler_").Length..]}", "effigy", Origin(name), tier3));
            var ancient = Building($"{team}.ancient", "ancient", Origin($"dota_{side}guys_fort"), new[] { $"{team}.base.t4a", $"{team}.base.t4b" }, "allProtectorsDestroyed");
            ancient["ancient"] = true;
            structures.Add(ancient);
        }

        var fountains = new JsonArray(entities.Where(e => e is JsonObject o && o["classname"]?.GetValue<string>() == "ent_dota_fountain")
            .OrderBy(e => e!["teamnumber"]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(e =>
            {
                JsonArray origin = e!["origin"]!.AsArray();
                (int X, int Y) p = At(origin[0]!.GetValue<double>(), origin[1]!.GetValue<double>());
                return (JsonNode)new JsonObject
                {
                    ["team"] = e["teamnumber"]!.GetValue<string>() == "2" ? "dawn" : "dusk", ["x"] = p.X, ["y"] = p.Y, ["radius"] = FountainRadius,
                };
            }).ToArray());

        // Camps: the map's 28 neutral spawners, 14 a side (3 small, 7 medium, 3 large, 1 ancient by the map's neutral type, D-053);
        // «good» and «evil» name the half of the map. On the minute from 1:00 (Fandom «Neutral creeps»). Flooded camps — 1 small and
        // 2 medium a side, as Liquipedia counts them: the two in the river (height level 0) and the medium camp nearest each bounty
        // rune (in a stream, 500–700 from it; Valve 7.41: «the medium flooded camp near the bounty runes»).
        string[] sizes = { "small", "medium", "large", "ancient" };
        var spawners = data["npc_dota_neutral_spawner"]!.AsArray().Select(s => s!).ToList();
        var byBountyRunes = data["dota_item_rune_spawner_bounty"]!.AsArray()
            .Select(rune => spawners.Where(s => s["neutralType"]!.GetValue<string>() == "1")
                .OrderBy(s => Math.Pow(s["x"]!.GetValue<double>() - rune!["x"]!.GetValue<double>(), 2) + Math.Pow(s["y"]!.GetValue<double>() - rune["y"]!.GetValue<double>(), 2))
                .First()["triggerName"]!.GetValue<string>())
            .ToHashSet();
        var camps = new JsonArray(data["npc_dota_neutral_spawner"]!.AsArray()
            .Select(s => (Spawner: s!, Trigger: s!["triggerName"]!.GetValue<string>()))
            .OrderBy(s => s.Trigger, StringComparer.Ordinal)
            .Select(s =>
            {
                int size = int.Parse(s.Spawner["neutralType"]!.GetValue<string>(), CultureInfo.InvariantCulture);
                string half = s.Trigger.Contains("_good_") ? "dawn" : "dusk";
                (int X, int Y) p = Point(s.Spawner);
                MustWalk("camp " + s.Trigger, p);
                bool river = elevation[p.Y / cell * columns + p.X / cell] == 0;
                bool byBountyRune = byBountyRunes.Contains(s.Trigger);
                var camp = new JsonObject { ["id"] = $"camp.{half}.{sizes[size]}.{s.Trigger[(s.Trigger.LastIndexOf('_') + 1)..]}", ["x"] = p.X, ["y"] = p.Y };
                if (river || byBountyRune)
                {
                    if (size > 1)
                        throw new InvalidDataException($"{s.Trigger}: a flooded camp of neutral type {size}");
                    camp["evolution"] = Jungle.Evolution(startTier: size, evolutions: size == 0 || byBountyRune ? 6 : 3);
                }
                else
                    camp["variants"] = Jungle.Variants(size);
                camp["firstSpawn"] = 60;
                camp["respawn"] = 60;
                camp["leashRadius"] = 600;
                camp["alignedToClock"] = true;
                return (JsonNode)camp;
            }).ToArray());

        (int X, int Y)[] Spots(string kind)
        {
            var spots = data[kind]!.AsArray().Select(p => Point(p!)).OrderBy(p => p.Y).ToArray();
            foreach ((int X, int Y) p in spots)
                MustWalk(kind, p);
            return spots;
        }

        var map = new JsonObject
        {
            ["width"] = columns * cell, ["height"] = rows * cell, ["navCellSize"] = cell, ["navClearance"] = 0,
            ["terrain"] = new JsonObject
            {
                ["columns"] = columns, ["rows"] = rows, ["walkable"] = Convert.ToBase64String(walkable),
                ["visionBlockers"] = Convert.ToBase64String(blockers), ["levels"] = Convert.ToBase64String(levels),
                ["treeSize"] = 128, ["trees"] = Flat(trees),
            },
            ["lanes"] = lanes, ["structures"] = structures, ["fountains"] = fountains, ["camps"] = camps,
        };
        var pits = data["npc_dota_roshan_spawner"]!.AsArray().Select(p => Point(p!)).OrderByDescending(p => p.Y).ToArray();
        var tormentors = data["npc_dota_miniboss_spawner"]!.AsArray().Select(p => Point(p!)).OrderByDescending(p => p.Y).ToArray();
        foreach ((int X, int Y) p in tormentors)
            MustWalk("tormentor", p);
        var shrines = Spots("npc_dota_xp_fountain");
        return new DotaMap(map, Spots("dota_item_rune_spawner_powerup"), Spots("dota_item_rune_spawner_bounty"), pits[0], pits[1], tormentors[0], tormentors[1], warnings)
        {
            Shrines = shrines,
        };
    }
}
