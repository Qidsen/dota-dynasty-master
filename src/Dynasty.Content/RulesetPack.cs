using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Json;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Content
{
    public sealed class RulesetPackException : Exception
    {
        public RulesetPackException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Gameplay ruleset packs (spec §27, decision D-048): a JSON document that replaces the heroes, hero
    /// rules, abilities and items of a base ruleset and adds unit types for summons; the map, lane creeps,
    /// structures, waves and the rest stay from the base. Format <c>dynasty.ruleset-pack</c>, version 1
    /// (documented in <c>docs/DOTA_PACK.md</c>). Unknown fields are errors, so a typo cannot pass silently.
    /// </summary>
    public static class RulesetPack
    {
        public const string Format = "dynasty.ruleset-pack";
        public const int FormatVersion = 1;

        /// <summary>The base ruleset with the pack applied; throws <see cref="RulesetPackException"/> naming the bad field.</summary>
        public static RulesetContent Apply(RulesetContent baseRules, string json)
        {
            JsonValue doc;
            try
            {
                doc = JsonValue.Parse(json);
            }
            catch (JsonException e)
            {
                throw new RulesetPackException("Not valid JSON: " + e.Message);
            }

            var reader = new Reader(doc, "");
            if (reader.String("format") != Format)
                throw new RulesetPackException("Unknown format: expected \"" + Format + "\".");
            int formatVersion = reader.Int("formatVersion");
            if (formatVersion != FormatVersion)
                throw new RulesetPackException("Unsupported formatVersion " + formatVersion + " (this build reads " + FormatVersion + ").");

            string id = reader.String("id");
            int version = reader.Int("version");
            reader.OptionalString("name");
            reader.Ignore("provenance");
            IReadOnlyList<HeroDefinition> heroes = reader.Array("heroes").Select(ReadHero).ToList();
            HeroRules? heroRules = reader.OptionalObject("heroRules") is Reader rules ? ReadHeroRules(rules, baseRules.HeroRules) : baseRules.HeroRules;
            CombatRules combat = baseRules.Combat;
            if (reader.OptionalObject("combat") is Reader c)
            {
                combat = new CombatRules(combat.ArmorFactor, combat.HeroAggro, combat.AttackClasses, c.OptionalBool("dotaStacking") ?? combat.DotaStacking,
                    c.OptionalInt("maxAttackSpeed") ?? combat.MaxAttackSpeed, c.OptionalInt("illusionBuildingPercent") ?? combat.IllusionBuildingPercent,
                    c.OptionalInt("summonBuildingPercent") ?? combat.SummonBuildingPercent, c.OptionalObject("glyph") is Reader g ? ReadGlyph(g) : combat.Glyph,
                    c.OptionalObject("aggro") is Reader ag ? ReadAggro(ag) : combat.DotaAggro);
                c.RejectUnknown();
            }
            List<CreepType> unitTypes = reader.OptionalArray("unitTypes").Select(u => ReadUnitType(u, baseRules)).ToList();
            MapDefinition? packMap = reader.OptionalObject("map") is Reader mp ? ReadMap(mp) : null;
            // Structure types replace the base ones of the same id; a structure keeps its base size (the map stays the base one).
            var structureOverrides = reader.OptionalArray("structureTypes").Select(s => ReadStructureType(s, baseRules)).ToDictionary(s => s.Id);
            IReadOnlyList<AbilityDefinition> abilities = reader.Has("abilities") ? reader.Array("abilities").Select(ReadAbility).ToList() : baseRules.Abilities;
            IReadOnlyList<ItemDefinition> items = reader.Has("items") ? reader.Array("items").Select(ReadItem).ToList() : baseRules.Items;
            WaveRules waves = reader.OptionalObject("waves") is Reader w ? ReadWaves(w, baseRules.Waves) : baseRules.Waves;
            BossDefinition? boss = reader.OptionalObject("boss") is Reader bo ? ReadBoss(bo, baseRules.Boss) : baseRules.Boss;
            if (packMap != null && !reader.Has("boss") && boss != null)
                throw reader.Error("boss", "a pack with its own map must place the boss pit (the base one is on the base map)");
            RuneRules? runes = reader.OptionalObject("runes") is Reader ru ? ReadRunes(ru, baseRules.Runes) : baseRules.Runes;
            MinibossDefinition? miniboss = reader.OptionalObject("miniboss") is Reader mb ? ReadMiniboss(mb) : baseRules.Miniboss;
            TwinGateRules? twinGates = baseRules.TwinGates;
            if (reader.OptionalObject("twinGates") is Reader tg)
            {
                twinGates = new TwinGateRules(Points(tg, "positions"), tg.Int("radius"), tg.Milliseconds("channel"), tg.Int("manaCost"));
                tg.RejectUnknown();
            }

            OutpostRules? outposts = baseRules.Outposts;
            if (reader.OptionalObject("outposts") is Reader op)
            {
                IReadOnlyList<FixedVector2> places = Points(op, "positions");
                var owners = op.Strings("owners").Select((o, i) => o switch
                {
                    "dawn" => Team.Dawn,
                    "dusk" => Team.Dusk,
                    _ => throw op.Error("owners", "expected dawn or dusk"),
                }).ToList();
                outposts = new OutpostRules(places, owners, op.Int("radius"), op.Milliseconds("capture"));
                op.RejectUnknown();
            }

            WatcherRules? watchers = baseRules.Watchers;
            if (reader.OptionalObject("watchers") is Reader wa)
            {
                watchers = new WatcherRules(Points(wa, "positions"), wa.Int("radius"), wa.Milliseconds("channel"), wa.Milliseconds("on"), wa.Milliseconds("off"),
                    wa.Int("dayVision"), wa.Int("nightVision"));
                wa.RejectUnknown();
            }

            LotusPoolRules? lotusPools = baseRules.LotusPools;
            if (reader.OptionalObject("lotusPools") is Reader lp)
            {
                lotusPools = new LotusPoolRules(Points(lp, "positions"), lp.Milliseconds("first"), lp.Milliseconds("interval"), lp.Int("max"), lp.Int("radius"),
                    lp.Milliseconds("firstPick"), lp.Int("nextPickPercent"), lp.Milliseconds("minPick"), lp.String("item"));
                lp.RejectUnknown();
            }

            ShrineRules? shrines = baseRules.Shrines;
            if (reader.OptionalObject("shrines") is Reader sh)
            {
                shrines = new ShrineRules(Points(sh, "positions"), sh.Milliseconds("first"), sh.Milliseconds("interval"), sh.Int("radius"), sh.Milliseconds("capture"),
                    sh.Int("xp"), sh.OptionalInt("xpPerInterval") ?? 0);
                sh.RejectUnknown();
            }
            reader.RejectUnknown();

            var unitIds = new HashSet<string>(unitTypes.Select(u => u.Id));
            IReadOnlyList<CreepType> creepTypes = baseRules.CreepTypes.Where(c => !unitIds.Contains(c.Id)).Concat(unitTypes).ToList();
            IReadOnlyList<StructureType> structureTypes = baseRules.StructureTypes.Select(s => structureOverrides.TryGetValue(s.Id, out StructureType? o) ? o : s).ToList();
            // The fountains' type may be added (each fountain then becomes a building); with the pack's own map any new type may,
            // otherwise other new ids are typos.
            foreach (StructureType added in structureOverrides.Values.Where(o => baseRules.StructureTypes.All(s => s.Id != o.Id)))
            {
                if (packMap == null && added.Id != StructureType.FountainId)
                    throw new RulesetPackException("structureTypes: \"" + added.Id + "\" is not a structure type of the base rules.");
                structureTypes = structureTypes.Append(added).ToList();
            }
            var content = new RulesetContent(id, version, baseRules.TicksPerSecond, combat, creepTypes, structureTypes,
                packMap ?? baseRules.Map, waves, heroes, heroRules, abilities, items, boss, runes,
                baseRules.DayNight, baseRules.Draft, miniboss, shrines, lotusPools, watchers, outposts, twinGates);
            IReadOnlyList<string> errors = content.Validate();
            if (errors.Count > 0)
                throw new RulesetPackException((errors.Any(e => e.Contains("terrain")) ? "map.terrain: " : "") + "The pack makes an invalid ruleset: " + string.Join("; ", errors));
            return content;
        }

        /// <summary>
        /// The pack's own map (Dota's, D-053): its size and navigation cells, the map cell by cell, each side's path of every lane,
        /// the buildings, fountains and camps. Points are flat lists [x, y, x, y, ...]; cell layers are base64, row by row from y = 0.
        /// </summary>
        private static MapDefinition ReadMap(Reader m)
        {
            MapTerrain? terrain = m.OptionalObject("terrain") is Reader t ? ReadTerrain(t) : null;
            var lanes = m.Array("lanes").Select(l =>
            {
                var lane = new LaneDefinition(l.String("id"), Points(l, "dawn"), l.Has("dusk") ? Points(l, "dusk") : null);
                l.RejectUnknown();
                return lane;
            }).ToList();
            var structures = m.Array("structures").Select(s =>
            {
                VulnerabilityRule rule = s.OptionalString("vulnerability") switch
                {
                    null or "anyProtectorDestroyed" => VulnerabilityRule.AnyProtectorDestroyed,
                    "allProtectorsDestroyed" => VulnerabilityRule.AllProtectorsDestroyed,
                    "never" => VulnerabilityRule.Never,
                    _ => throw s.Error("vulnerability", "expected anyProtectorDestroyed, allProtectorsDestroyed or never"),
                };
                var placement = new StructurePlacement(s.String("id"), ReadTeam(s), s.String("type"), FixedVector2.FromInts(s.Int("x"), s.Int("y")),
                    s.OptionalBool("ancient") ?? false, s.OptionalStrings("protectedBy") ?? System.Array.Empty<string>(), rule, s.OptionalString("barracksLane"));
                s.RejectUnknown();
                return placement;
            }).ToList();
            var fountains = m.Array("fountains").Select(f =>
            {
                var fountain = new FountainPlacement(ReadTeam(f), FixedVector2.FromInts(f.Int("x"), f.Int("y")), f.Int("radius"));
                f.RejectUnknown();
                return fountain;
            }).ToList();
            var camps = m.OptionalArray("camps").Select(c =>
            {
                // A camp's creeps, or the camp types it may spawn (variants), or its stages (evolution).
                List<IReadOnlyList<WaveEntry>>? variants = c.Has("variants") ? Compositions(c, "variants") : null;
                CampEvolution? evolution = null;
                if (c.OptionalObject("evolution") is Reader ev)
                {
                    evolution = new CampEvolution(ev.Milliseconds("interval"), Compositions(ev, "stages"));
                    ev.RejectUnknown();
                }

                IReadOnlyList<WaveEntry> creeps = c.Has("creeps") ? CampCreeps(c.Array("creeps"))
                    : evolution?.Stages.FirstOrDefault() ?? variants?.FirstOrDefault() ?? throw c.Error("creeps", "missing (or give variants or evolution)");
                var camp = new NeutralCampDefinition(c.String("id"), FixedVector2.FromInts(c.Int("x"), c.Int("y")), creeps, c.Milliseconds("firstSpawn"),
                    c.Milliseconds("respawn"), c.Int("leashRadius"), c.OptionalBool("alignedToClock") ?? false, variants, evolution);
                c.RejectUnknown();
                return camp;
            }).ToList();
            var map = new MapDefinition(m.Int("width"), m.Int("height"), lanes, structures, null, m.Int("navCellSize"), m.OptionalInt("navClearance") ?? 0,
                fountains, camps, null, terrain);
            m.RejectUnknown();
            return map;
        }

        private static List<IReadOnlyList<WaveEntry>> Compositions(Reader r, string field) =>
            r.Read(field, v => v.Items.Select((items, i) =>
            {
                if (items.Kind != Dynasty.Core.Json.JsonKind.Array)
                    throw r.Error(field + "[" + i + "]", "expected a list of creeps");
                return CampCreeps(items.Items.Select((e, j) => new Reader(e, field + "[" + i + "][" + j + "]")));
            }).ToList());

        private static IReadOnlyList<WaveEntry> CampCreeps(IEnumerable<Reader> entries) => entries.Select(e =>
        {
            var entry = new WaveEntry(e.String("creep"), e.Int("count"));
            e.RejectUnknown();
            return entry;
        }).ToList();

        private static MapTerrain ReadTerrain(Reader t)
        {
            int columns = t.Int("columns");
            int rows = t.Int("rows");
            int cells = columns * rows;
            bool[] walkable = Bits(t, "walkable", cells);
            bool[]? blockers = t.Has("visionBlockers") ? Bits(t, "visionBlockers", cells) : null;
            int[]? levels = t.Has("levels") ? t.Base64("levels").Select(b => (int)b).ToArray() : null;
            if (levels != null && levels.Length != cells)
                throw t.Error("levels", "expected " + cells + " bytes, found " + levels.Length);
            var terrain = new MapTerrain(columns, rows, walkable, blockers, levels, t.Has("trees") ? Points(t, "trees") : null, t.OptionalInt("treeSize") ?? 128);
            t.RejectUnknown();
            return terrain;
        }

        /// <summary>One bit per cell, the lowest bit of each byte first.</summary>
        private static bool[] Bits(Reader r, string field, int cells)
        {
            byte[] bytes = r.Base64(field);
            if (bytes.Length != (cells + 7) / 8)
                throw r.Error(field, "expected " + (cells + 7) / 8 + " bytes for " + cells + " cells, found " + bytes.Length);
            var bits = new bool[cells];
            for (int i = 0; i < cells; i++)
                bits[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
            return bits;
        }

        private static IReadOnlyList<FixedVector2> Points(Reader r, string field)
        {
            IReadOnlyList<int> flat = r.Ints(field);
            if (flat.Count % 2 != 0)
                throw r.Error(field, "expected pairs x, y");
            return Enumerable.Range(0, flat.Count / 2).Select(i => FixedVector2.FromInts(flat[2 * i], flat[2 * i + 1])).ToList();
        }

        private static Team ReadTeam(Reader r) => r.String("team") switch
        {
            "dawn" => Team.Dawn,
            "dusk" => Team.Dusk,
            _ => throw r.Error("team", "expected dawn or dusk"),
        };

        private static HeroDefinition ReadHero(Reader h)
        {
            bool melee = h.String("attackType") switch
            {
                "melee" => true,
                "ranged" => false,
                _ => throw h.Error("attackType", "expected \"melee\" or \"ranged\""),
            };
            PrimaryAttribute primary = h.String("primaryAttribute") switch
            {
                "strength" => PrimaryAttribute.Strength,
                "agility" => PrimaryAttribute.Agility,
                "intelligence" => PrimaryAttribute.Intelligence,
                "universal" => PrimaryAttribute.Universal,
                _ => throw h.Error("primaryAttribute", "expected strength, agility, intelligence or universal"),
            };
            int day = h.Int("dayVision");
            int projectileSpeed = h.Int("projectileSpeed");
            var stats = new UnitStats(
                h.Int("baseHealth"), h.Fixed("baseArmor"), h.Int("moveSpeed"), h.Int("attackDamageMin"), h.Int("attackDamageMax"),
                h.Int("attackRange"), h.Milliseconds("baseAttackTime"), h.Milliseconds("attackPoint"),
                acquisitionRange: System.Math.Max(800, h.Int("attackRange")), boundingRadius: 24, magicResistPercent: h.Int("magicResist"),
                visionRange: day, projectileSpeed: melee ? 0 : projectileSpeed, nightVisionRange: h.Int("nightVision"));
            var attributes = new HeroAttributes(primary, h.Int("strength"), h.Int("agility"), h.Int("intelligence"),
                h.Fixed("strengthGain"), h.Fixed("agilityGain"), h.Fixed("intelligenceGain"), h.Int("baseAttackSpeed"));
            var hero = new HeroDefinition(h.String("id"), h.String("name"), h.Strings("roles"), stats,
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: h.Int("baseMana"), manaPerLevel: 0,
                hpRegenPerSecond: h.Fixed("baseHealthRegen"), manaRegenPerSecond: h.Fixed("baseManaRegen"),
                abilityIds: h.OptionalStrings("abilities"), itemBuild: h.OptionalStrings("itemBuild"), attributes: attributes,
                talents: h.OptionalArray("talents").Select(ReadTalent).ToList(), positionShares: h.OptionalInts("positions"));
            if (hero.PositionShares.Count != 0 && hero.PositionShares.Count != 5)
                throw h.Error("positions", "expected five shares, for positions 1 to 5");
            h.RejectUnknown();
            return hero;
        }

        private static EconomyRules ReadEconomy(Reader e)
        {
            var periodic = e.Array("periodicGold").Select(p =>
            {
                var step = new PeriodicGold(p.Milliseconds("from"), p.OptionalHundredth("perMinute") ?? throw p.Error("perMinute", "missing"));
                p.RejectUnknown();
                return step;
            }).ToList();
            var economy = new EconomyRules(e.Int("killGoldBase"), e.Int("killGoldPerLevel"), e.Int("firstBloodGold"), e.Ints("streakGold"),
                e.Fixeds("streakXpPerLevel"), e.Int("assistGoldBase"), e.Int("assistGoldPool"), e.Int("assistGoldNetWorthPermille"), e.Int("assistRadius"),
                e.Milliseconds("killCreditWindow"), e.Int("deathGoldLossNetWorthDivisor"), e.Int("buybackBaseGold"), e.Int("buybackNetWorthDivisor"),
                e.Milliseconds("buybackRespawnPenalty"), e.Seconds("respawn"), e.Milliseconds("neutralRespawnMin"), periodic, e.Int("heroXpCapLevel"),
                e.Int("deniedCreepXpPercent"));
            e.RejectUnknown();
            return economy;
        }

        private static TalentDefinition ReadTalent(Reader t)
        {
            var talent = new TalentDefinition(t.String("id"), t.String("name"), t.Int("level"), t.OptionalObject("bonus") is Reader b ? ReadModifier(b) : null);
            t.RejectUnknown();
            return talent;
        }

        private static HeroRules ReadHeroRules(Reader r, HeroRules? b)
        {
            if (b == null)
                throw r.Error("heroRules", "the base ruleset has no hero rules to extend");
            var rules = new HeroRules(
                r.OptionalInts("xpForLevel") ?? b.XpForLevel, r.OptionalInt("xpShareRadius") ?? b.XpShareRadius,
                r.OptionalInt("heroKillXpBase") ?? b.HeroKillXpBase, r.OptionalInt("heroKillXpPerLevel") ?? 0,
                b.HeroKillGoldBase, b.HeroKillGoldPerLevel, b.AssistWindowMs, b.GoldPerMinute, b.StartingGold,
                b.RespawnBaseMs, b.RespawnPerLevelMs, b.GoldLossPerLevel, b.StructureTeamGold, b.FountainHealPercentPerSecond,
                b.BuybackBaseGold, b.BuybackGoldPerLevel, b.BuybackCooldownMs,
                r.OptionalInt("heroKillXpPercentOfVictim") ?? b.HeroKillXpPercentOfVictim, b.Attributes,
                r.OptionalInts("skillPointLevels") ?? b.SkillPointLevels, r.OptionalInts("talentPointLevels") ?? b.TalentPointLevels,
                r.OptionalInt("attributeBonusLevels") ?? b.AttributeBonusLevels, r.OptionalInt("attributeBonusPerLevel") ?? b.AttributeBonusPerLevel,
                r.OptionalObject("economy") is Reader economy ? ReadEconomy(economy) : b.Economy,
                r.OptionalObject("stash") is Reader stash ? ReadStash(stash) : b.Stash);
            r.RejectUnknown();
            return rules;
        }

        private static StashRules ReadStash(Reader s)
        {
            var rules = new StashRules(s.Int("backpackSlots"), s.Int("stashSlots"), s.Int("courierSpeed"), s.Int("courierSpeedPerLevel"), s.Int("courierFliesFromLevel"));
            s.RejectUnknown();
            return rules;
        }

        /// <summary>Lane waves, the same for both teams; the super and mega creeps of destroyed barracks are the base rules' ones unless given.</summary>
        private static WaveRules ReadWaves(Reader w, WaveRules baseWaves)
        {
            var composition = w.Array("composition").Select(e =>
            {
                var entry = new WaveEntry(e.String("creep"), e.Int("count"), e.OptionalInt("firstWave") ?? 1, e.OptionalInt("everyWaves") ?? 1,
                    e.OptionalMilliseconds("from") ?? 0, e.OptionalString("replaces"));
                e.RejectUnknown();
                return entry;
            }).ToList();
            CreepUpgradeRules? upgrades = null;
            if (w.OptionalObject("upgrades") is Reader u)
            {
                upgrades = new CreepUpgradeRules(u.Milliseconds("interval"), u.Int("maxStatCycles"), u.Array("steps").Select(st =>
                {
                    var step = new CreepUpgradeStep(st.String("creep"), st.OptionalInt("health") ?? 0, st.OptionalInt("damage") ?? 0,
                        st.OptionalHundredth("gold") ?? 0, st.OptionalInt("xp") ?? 0, st.OptionalInt("auraMagicResist") ?? 0, st.OptionalInt("auraMaxCycles") ?? 0,
                        st.OptionalInt("auraRadius") ?? 0);
                    st.RejectUnknown();
                    return step;
                }).ToList());
                u.RejectUnknown();
            }

            var waves = new WaveRules(w.OptionalMilliseconds("firstSpawn") ?? baseWaves.FirstSpawnMs, w.OptionalMilliseconds("interval") ?? baseWaves.IntervalMs,
                composition, composition, w.Has("superUpgrades") ? CreepUpgrades(w, "superUpgrades") : baseWaves.SuperUpgrades, upgrades,
                w.Has("megaUpgrades") ? CreepUpgrades(w, "megaUpgrades") : baseWaves.MegaUpgrades);
            w.RejectUnknown();
            return waves;
        }

        /// <summary>Dota's Tormentor: its places by day and night, timings, rewards and the numbers of its barrier, reflection and burn.</summary>
        private static MinibossDefinition ReadMiniboss(Reader m)
        {
            FixedVector2 Place(string field)
            {
                Reader p = m.OptionalObject(field) ?? throw m.Error(field, "missing");
                var point = FixedVector2.FromInts(p.Int("x"), p.Int("y"));
                p.RejectUnknown();
                return point;
            }

            var miniboss = new MinibossDefinition(m.String("id"), m.String("creep"), Place("dayPosition"), Place("nightPosition"), m.Milliseconds("firstSpawn"),
                m.Milliseconds("respawn"), m.Int("teamGold"), m.OptionalString("rewardItem"), m.OptionalInt("teamGoldWithoutItem") ?? 0, m.Milliseconds("growthInterval"),
                m.Int("barrier"), m.OptionalInt("barrierPerInterval") ?? 0, m.Fixed("barrierRegen"), m.OptionalFixed("barrierRegenPerInterval") ?? Fixed.Zero,
                m.Fixed("reflectPercent"), m.OptionalFixed("reflectPercentPerInterval") ?? Fixed.Zero, m.Int("reflectRadius"), m.Milliseconds("reflectInterval"),
                m.Int("shiningRadius"), m.Fixed("shiningDamage"), m.OptionalFixed("shiningDamagePerInterval") ?? Fixed.Zero, m.Milliseconds("shiningInterval"),
                m.Milliseconds("shiningAfterHit"), m.OptionalObject("deathModifier") is Reader d ? ReadModifier(d) : null, m.OptionalInt("deathModifierRadius") ?? 0);
            m.RejectUnknown();
            return miniboss;
        }

        private static IReadOnlyList<CreepUpgrade> CreepUpgrades(Reader w, string field) => w.Array(field).Select(u =>
        {
            var upgrade = new CreepUpgrade(u.String("from"), u.String("to"));
            u.RejectUnknown();
            return upgrade;
        }).ToList();

        /// <summary>The boss of the base map's pit (its place and leash stay the base ones) with Dota's stats, rewards and growth.</summary>
        private static BossDefinition ReadBoss(Reader b, BossDefinition? baseBoss)
        {
            if (baseBoss == null)
                throw b.Error("boss", "the base rules have no boss pit");
            IReadOnlyList<int> killerGold = b.OptionalInts("killerGold") ?? new[] { 0, 0 };
            BossGrowth? growth = null;
            if (b.OptionalObject("growth") is Reader g)
            {
                growth = new BossGrowth(g.Milliseconds("interval"), g.OptionalInt("health") ?? 0, g.OptionalFixed("armor") ?? Dynasty.Core.Mathematics.Fixed.Zero,
                    g.OptionalInt("damage") ?? 0, g.OptionalInt("xp") ?? 0, g.OptionalBool("armorFromStart") ?? false);
                g.RejectUnknown();
            }

            FixedVector2 position = baseBoss.Position;
            if (b.OptionalObject("position") is Reader p)
            {
                position = FixedVector2.FromInts(p.Int("x"), p.Int("y"));
                p.RejectUnknown();
            }

            FixedVector2? nightPosition = null;
            if (b.OptionalObject("nightPosition") is Reader n)
            {
                nightPosition = FixedVector2.FromInts(n.Int("x"), n.Int("y"));
                n.RejectUnknown();
            }

            var boss = new BossDefinition(b.String("id"), b.String("creep"), position, b.OptionalInt("leashRadius") ?? baseBoss.LeashRadius, b.OptionalMilliseconds("firstSpawn") ?? 0,
                b.Milliseconds("respawnMin"), b.Milliseconds("respawnMax"), b.Int("teamGold"), null, killerGold[0], killerGold[1], b.OptionalInt("xp") ?? 0, growth,
                b.OptionalObject("aegis") is Reader ae ? ReadModifier(ae) : null, b.OptionalArray("abilities").Select(ReadBossAbility).ToList(),
                b.OptionalInt("provokeRadius") ?? baseBoss.ProvokeRadius, b.OptionalArray("drops").Select(d =>
                {
                    var drop = new BossDrop(d.String("item"), d.Int("fromDeath"));
                    d.RejectUnknown();
                    return drop;
                }).ToList(), nightPosition, b.OptionalBool("aegisDrops") ?? false);
            b.RejectUnknown();
            return boss;
        }

        /// <summary>
        /// Dota's runes: their types and their schedules (spots on the base map until the Dota map comes); the base rules' plain
        /// spots and types stay underneath, unused once there are schedules.
        /// </summary>
        private static RuneRules ReadRunes(Reader r, RuneRules? baseRunes)
        {
            var types = r.Array("types").Select(t =>
            {
                var type = new RuneType(t.String("id"), t.OptionalObject("modifier") is Reader m ? ReadModifier(m) : null, t.OptionalInt("gold") ?? 0,
                    t.OptionalArray("effects").Select(ReadEffect).ToList(), t.OptionalInt("teamGold") ?? 0, t.OptionalInt("teamGoldPerInterval") ?? 0,
                    t.OptionalMilliseconds("teamGoldInterval") ?? 0);
                t.RejectUnknown();
                return type;
            }).ToDictionary(t => t.Id);
            var schedules = r.Array("schedules").Select(s =>
            {
                RunePlacement placement = s.OptionalString("placement") switch
                {
                    null or "everySpot" => RunePlacement.EverySpot,
                    "oneRandomSpot" => RunePlacement.OneRandomSpot,
                    _ => throw s.Error("placement", "expected everySpot or oneRandomSpot"),
                };
                var spots = s.Array("spots").Select(p =>
                {
                    var spot = Dynasty.Core.Mathematics.FixedVector2.FromInts(p.Int("x"), p.Int("y"));
                    p.RejectUnknown();
                    return spot;
                }).ToList();
                var schedule = new RuneSchedule(s.String("id"), spots, s.Milliseconds("first"), s.OptionalMilliseconds("interval") ?? 0, s.OptionalMilliseconds("until") ?? 0,
                    s.Strings("types").Select(id => types.TryGetValue(id, out RuneType? t) ? t : throw s.Error("types", "unknown rune type \"" + id + "\"")).ToList(),
                    placement, s.OptionalBool("noRepeat") ?? false, s.OptionalBool("stay") ?? false);
                s.RejectUnknown();
                return schedule;
            }).ToList();
            var rules = new RuneRules(baseRunes?.Spots ?? System.Array.Empty<Dynasty.Core.Mathematics.FixedVector2>(), baseRunes?.FirstSpawnMs ?? 0,
                baseRunes?.IntervalMs ?? 1000, baseRunes?.Types ?? System.Array.Empty<RuneType>(), r.OptionalInt("pickupRadius") ?? baseRunes?.PickupRadius ?? 100, schedules);
            r.RejectUnknown();
            return rules;
        }

        private static BossAbility ReadBossAbility(Reader a)
        {
            BossAbilityTrigger trigger = a.String("trigger") switch
            {
                "engaged" => BossAbilityTrigger.Engaged,
                "hitByLastKillersBelowHealth" => BossAbilityTrigger.HitByLastKillersBelowHealth,
                _ => throw a.Error("trigger", "expected engaged or hitByLastKillersBelowHealth"),
            };
            var ability = new BossAbility(a.String("id"), trigger, a.Milliseconds("cooldown"), a.OptionalMilliseconds("castPoint") ?? 0, a.Int("radius"),
                ReadDamageType(a, "damageType"), a.Int("damage"), a.OptionalInt("damagePerInterval") ?? 0, a.OptionalInt("creepDamagePercent") ?? 100,
                a.OptionalArray("modifiers").Select(ReadModifier).ToList(), a.OptionalInt("belowHealthPercent") ?? 100, a.OptionalBool("ignoresDisables") ?? false,
                a.OptionalBool("dispelsSelf") ?? false);
            a.RejectUnknown();
            return ability;
        }

        private static DotaAggroRules ReadAggro(Reader a)
        {
            var aggro = new DotaAggroRules(a.Int("drawRadius"), a.Milliseconds("forced"), a.Milliseconds("cooldown"), a.Milliseconds("lockedUntil"),
                a.Int("unlockRadius"), a.String("unlockStructureType"), a.Int("towerProtectRadius"), a.Int("towerThreatRadius"));
            a.RejectUnknown();
            return aggro;
        }

        private static GlyphRules ReadGlyph(Reader g)
        {
            var glyph = new GlyphRules(g.Milliseconds("cooldown"), g.Milliseconds("initialCooldown"), g.Milliseconds("buildingDuration"),
                g.Milliseconds("creepDuration"), g.Milliseconds("resetDelay"));
            g.RejectUnknown();
            return glyph;
        }

        private static StructureType ReadStructureType(Reader s, RulesetContent baseRules)
        {
            string id = s.String("id");
            UnitStats? baseStats = baseRules.StructureTypes.FirstOrDefault(t => t.Id == id)?.Stats;
            int vision = s.Int("visionRange");
            var stats = new UnitStats(s.Int("health"), s.Fixed("armor"), 0, s.OptionalInt("attackDamageMin") ?? 0, s.OptionalInt("attackDamageMax") ?? 0,
                s.OptionalInt("attackRange") ?? 0, s.OptionalMilliseconds("baseAttackTime") ?? 1000, s.OptionalMilliseconds("attackPoint") ?? 0,
                s.OptionalInt("acquisitionRange") ?? 0, s.OptionalInt("radius") ?? baseStats?.BoundingRadius ?? 100, s.OptionalInt("magicResist") ?? 0, vision,
                // True sight is not in the game's unit file (towers' 700 is set by the game's code): kept from the base type unless given.
                s.OptionalInt("trueSight") ?? baseStats?.TrueSightRange ?? 0, ModifierFlags.None, s.OptionalInt("projectileSpeed") ?? 0, s.OptionalInt("nightVision") ?? vision,
                s.OptionalString("attackClass") switch
                {
                    null => AttackClass.Basic,
                    "basic" => AttackClass.Basic,
                    "runty" => AttackClass.Runty,
                    "piercing" => AttackClass.Piercing,
                    "reinforced" => AttackClass.Reinforced,
                    _ => throw s.Error("attackClass", "expected basic, runty, piercing or reinforced"),
                });
            s.Ignore("moveSpeed");
            StructureBounty? bounty = null;
            Reader? b = s.OptionalObject("bounty");
            if (b != null)
            {
                IReadOnlyList<int> lastHit = b.Ints("lastHitGold");
                if (lastHit.Count != 2 || lastHit[0] > lastHit[1])
                    throw b.Error("lastHitGold", "expected [min, max]");
                bounty = new StructureBounty(b.Int("teamGold"), lastHit[0], lastHit[1], b.OptionalBool("lastHitterGetsTeamGold") ?? true,
                    b.OptionalInt("deniedEnemyGoldPercent") ?? 0, b.OptionalInt("deniedOwnGoldPercent") ?? 0);
                b.RejectUnknown();
            }

            int deny = s.OptionalInt("denyHealthPercent") ?? 0;
            var modifiers = s.OptionalArray("modifiers").Select(ReadModifier).ToList();
            int reinforcement = s.OptionalInt("armorPerStandingBarracks") ?? 0;
            BackdoorProtection? backdoor = null;
            if (s.OptionalObject("backdoor") is Reader d)
            {
                backdoor = new BackdoorProtection(d.Int("radius"), d.OptionalBool("aroundAncient") ?? false, d.Int("damageReduction"), d.Int("summonDamageReduction"),
                    d.Int("healPerSecond"), d.Milliseconds("reactivation"));
                d.RejectUnknown();
            }

            int glyphTargets = s.OptionalInt("glyphExtraTargets") ?? 0;
            bool resetsGlyph = s.OptionalBool("resetsGlyphOnFirstLoss") ?? false;
            IReadOnlyList<string>? upgrades = s.OptionalStrings("upgradesCreeps");
            s.RejectUnknown();
            return new StructureType(id, stats, bounty, deny, modifiers, reinforcement, backdoor, glyphTargets, resetsGlyph, upgrades);
        }

        private static CreepType ReadUnitType(Reader u, RulesetContent? baseRules = null)
        {
            string? classText = u.OptionalString("attackClass");
            AttackClass attackClass = classText switch
            {
                null => AttackClass.Basic,
                "basic" => AttackClass.Basic,
                "runty" => AttackClass.Runty,
                "piercing" => AttackClass.Piercing,
                "reinforced" => AttackClass.Reinforced,
                _ => throw u.Error("attackClass", "expected basic, runty, piercing or reinforced"),
            };
            int vision = u.Int("visionRange");
            var stats = new UnitStats(u.Int("health"), u.Fixed("armor"), u.Int("moveSpeed"), u.OptionalInt("attackDamageMin") ?? 0, u.OptionalInt("attackDamageMax") ?? 0,
                u.OptionalInt("attackRange") ?? 0, u.OptionalMilliseconds("baseAttackTime") ?? 1000, u.OptionalMilliseconds("attackPoint") ?? 0,
                // A unit type replacing a base one (lane creeps) keeps its base size unless the pack gives one.
                u.OptionalInt("acquisitionRange") ?? 0, u.OptionalInt("radius") ?? baseRules?.FindCreepType(u.String("id"))?.Stats.BoundingRadius ?? 16, u.OptionalInt("magicResist") ?? 0, vision,
                u.OptionalInt("trueSight") ?? 0, u.OptionalBool("invisible") == true ? ModifierFlags.Invisible : ModifierFlags.None,
                u.OptionalInt("projectileSpeed") ?? 0, u.OptionalInt("nightVision") ?? vision, attackClass);
            Bounty bounty = Bounty.None;
            if (u.OptionalObject("bounty") is Reader b)
            {
                bounty = new Bounty(b.Int("goldMin"), b.Int("goldMax"), b.Int("xp"));
                b.RejectUnknown();
            }

            AreaBounty? area = null;
            if (u.OptionalObject("areaBounty") is Reader a)
            {
                area = new AreaBounty(a.Int("gold"), a.Int("bountyPercent"), a.Int("radius"));
                a.RejectUnknown();
            }

            var type = new CreepType(u.String("id"), stats, bounty, u.OptionalArray("modifiers").Select(ReadModifier).ToList(), area);
            u.RejectUnknown();
            return type;
        }

        private static AbilityDefinition ReadAbility(Reader a)
        {
            AbilityTargeting targeting = a.String("targeting") switch
            {
                "noTarget" => AbilityTargeting.NoTarget,
                "unit" => AbilityTargeting.Unit,
                "point" => AbilityTargeting.Point,
                "passive" => AbilityTargeting.Passive,
                _ => throw a.Error("targeting", "expected noTarget, unit, point or passive"),
            };
            IReadOnlyList<int> levels = a.Ints("requiredLevels");
            IReadOnlyList<int> cooldowns = a.Seconds("cooldown");
            IReadOnlyList<int> mana = a.OptionalInts("manaCost") ?? (targeting == AbilityTargeting.Passive ? new int[0] : levels.Select(_ => 0).ToArray());
            var ability = new AbilityDefinition(a.String("id"), a.String("name"), targeting, ReadTeam(a, "targetTeam"), a.Int("castRange"), a.Milliseconds("castPoint"),
                cooldowns, mana, levels, a.Array("effects").Select(ReadEffect).ToList(),
                a.OptionalArray("passive").Select(ReadModifier).ToList(), a.OptionalBool("targetsStructures") ?? false, a.OptionalBool("innate") ?? false,
                a.OptionalInts("charges"), a.Has("charges") ? a.Seconds("chargeRestore") : null,
                a.OptionalArray("variants").Select(v => new AbilityVariant(v.Strings("upgrades"), ReadAbility(v))).ToList(),
                a.OptionalObject("channel") is Reader c ? ReadChannel(c) : null, a.OptionalArray("toggle").Select(ReadModifier).ToList(),
                a.OptionalString("levelsWith"), a.OptionalInt("autoCastBelowHealth") ?? 0, a.OptionalString("grantedBy"), a.OptionalBool("nightOnly") ?? false,
                a.OptionalBool("targetsCreepsOnly") ?? false, a.OptionalBool("multicastSameTarget") ?? false);
            a.RejectUnknown();
            return ability;
        }

        private static ChannelSpec ReadChannel(Reader c)
        {
            var channel = new ChannelSpec(c.Seconds("duration"), c.OptionalMilliseconds("period") ?? 1000, c.OptionalArray("pulse").Select(ReadEffect).ToList(),
                c.OptionalArray("end").Select(ReadEffect).ToList(), c.OptionalObject("whileModifier") is Reader m ? ReadModifier(m) : null);
            c.RejectUnknown();
            return channel;
        }

        private static TargetTeam ReadTeam(Reader r, string field) => r.String(field) switch
        {
            "enemy" => TargetTeam.Enemy,
            "ally" => TargetTeam.Ally,
            "any" => TargetTeam.Any,
            _ => throw r.Error(field, "expected enemy, ally or any"),
        };

        private static EffectDefinition ReadEffect(Reader e)
        {
            EffectDefinition effect;
            switch (e.String("kind"))
            {
                case "damage":
                    DamageScaling scaling = e.Has("scaling") ? ReadScaling(e, "scaling") : DamageScaling.Flat;
                    effect = scaling == DamageScaling.Flat
                        ? EffectDefinition.Damage(ReadDamageType(e, "damageType"), e.Ints("amounts").ToArray())
                        : scaling == DamageScaling.PerTargetStack
                        ? EffectDefinition.DamagePerStack(ReadDamageType(e, "damageType"), e.String("stackKey"), e.Ints("amounts").ToArray())
                        : scaling == DamageScaling.DoublingPerStack
                        ? EffectDefinition.DamageByStacksDoubling(ReadDamageType(e, "damageType"), e.Int("doublingStacks"), e.Int("extraPerStack"), e.Ints("amounts").ToArray())
                        : scaling == DamageScaling.PerUnitAround
                        ? EffectDefinition.PerUnitAround(EffectKind.Damage, ReadDamageType(e, "damageType"), e.Int("radius"), e.Int("maxUnits"), e.Ints("amounts").ToArray())
                        : scaling == DamageScaling.PerChannelSecond
                        ? EffectDefinition.ChannelGrowthDamage(ReadDamageType(e, "damageType"), e.Ints("ceiling").ToArray(), e.Ints("amounts").ToArray())
                        : scaling == DamageScaling.FullChargeOnly
                        ? EffectDefinition.FullChargeDamage(ReadDamageType(e, "damageType"), e.Ints("amounts").ToArray())
                        : scaling == DamageScaling.DistanceFalloff
                        ? EffectDefinition.FalloffDamage(ReadDamageType(e, "damageType"), e.Int("fullRadius"), e.Int("edgeRadius"), e.Int("edgePercent"), e.Ints("amounts").ToArray())
                        : e.Has("floor") ? EffectDefinition.BoundedScaledDamage(ReadDamageType(e, "damageType"), scaling, e.Ints("floor").ToArray(), e.Ints("ceiling").ToArray(), e.Hundredths("amounts").ToArray())
                        : e.Has("cap") ? EffectDefinition.CappedScaledDamage(ReadDamageType(e, "damageType"), scaling, e.Int("cap"), e.Hundredths("amounts").ToArray())
                        : EffectDefinition.ScaledDamage(ReadDamageType(e, "damageType"), scaling, e.Hundredths("amounts").ToArray());
                    break;
                case "manaBurn":
                    effect = EffectDefinition.ManaBurn(e.OptionalInt("damagePercent") ?? 0, ReadDamageType(e, "damageType"), e.Ints("amounts").ToArray());
                    break;
                case "heal":
                    effect = !e.Has("scaling") ? EffectDefinition.Heal(e.Ints("amounts").ToArray())
                        : ReadScaling(e, "scaling") == DamageScaling.PerUnitAround
                        ? EffectDefinition.PerUnitAround(EffectKind.Heal, DamageType.Magical, e.Int("radius"), e.Int("maxUnits"), e.Ints("amounts").ToArray())
                        : EffectDefinition.ScaledHeal(ReadScaling(e, "scaling"), e.Hundredths("amounts").ToArray());
                    break;
                case "restoreMana":
                    effect = e.Has("scaling") ? EffectDefinition.ScaledRestoreMana(ReadScaling(e, "scaling"), e.Hundredths("amounts").ToArray())
                        : EffectDefinition.RestoreMana(e.Ints("amounts").ToArray());
                    break;
                case "modifier":
                    effect = EffectDefinition.Modifier(e.Array("modifiers").Select(ReadModifier).ToArray());
                    break;
                case "area":
                    AreaCenter center = ReadCenter(e, "center");
                    effect = e.OptionalInt("maxTargets") is int most
                        ? EffectDefinition.Area(e.Int("radius"), center, ReadTeam(e, "team"), most, e.OptionalBool("lowestHealthFirst") ?? false, e.Array("effects").Select(ReadEffect).ToArray())
                        : EffectDefinition.Area(e.Int("radius"), center, ReadTeam(e, "team"), e.OptionalBool("heroesOnly") ?? false, e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "dash":
                    effect = EffectDefinition.Dash();
                    break;
                case "rewind":
                    effect = EffectDefinition.Rewind(e.Milliseconds("back"), e.OptionalBool("restoresVitals") ?? false, e.OptionalArray("effects").Select(ReadEffect).ToArray());
                    break;
                case "bind":
                    effect = EffectDefinition.Bind(e.Int("radius"), e.Int("leashPercent"), e.Int("leashBuffer"), e.Array("modifiers").Select(ReadModifier).ToArray());
                    break;
                case "spring":
                    effect = EffectDefinition.Spring(e.String("unitType"), e.Milliseconds("charge"), e.Int("minChargePercent"), e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "summonNear":
                    effect = EffectDefinition.SummonNear(e.String("unitType"), e.OptionalMilliseconds("duration") ?? -1, e.Ints("counts").ToArray(),
                        e.OptionalArray("effects").Select(ReadEffect).ToArray());
                    break;
                case "cogs":
                    effect = EffectDefinition.Cogs(e.Int("radius"), e.Int("shockReach"), e.Seconds("durations").ToArray(), e.Array("shock").Select(ReadEffect).ToArray());
                    break;
                case "wall":
                    effect = EffectDefinition.Wall(e.Int("radius"), ReadCenter(e, "center"), ReadTeam(e, "team"), e.OptionalMilliseconds("formation") ?? 0,
                        e.OptionalInt("arc") ?? 0, e.OptionalBool("blocksImmune") ?? false, e.Seconds("durations").ToArray());
                    break;
                case "lineArea":
                    effect = EffectDefinition.LineArea(e.OptionalInt("width") ?? 0, ReadTeam(e, "team"), e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "randomStrike":
                    effect = e.Has("marker")
                        ? EffectDefinition.RandomStrike(e.Int("radius"), ReadTeam(e, "team"), e.String("marker"), e.Int("hitsPerUnit"),
                            ReadModifier(e.OptionalObject("markerModifier") ?? throw e.Error("markerModifier", "missing")), e.Array("effects").Select(ReadEffect).ToArray())
                        : EffectDefinition.RandomStrike(e.Int("radius"), ReadTeam(e, "team"), e.Ints("strikes").ToArray(), e.OptionalBool("buildings") ?? false,
                            e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "haunt":
                    effect = EffectDefinition.Haunt(e.Milliseconds("duration"), e.Array("modifiers").Select(ReadModifier).ToArray());
                    break;
                case "assume":
                    effect = EffectDefinition.Assume();
                    break;
                case "consume":
                    effect = EffectDefinition.Consume(e.String("modifier"));
                    break;
                case "release":
                    effect = EffectDefinition.Release(e.String("modifier"));
                    break;
                case "kill":
                    effect = EffectDefinition.Kill();
                    break;
                case "experience":
                    effect = EffectDefinition.Experience(e.Ints("amounts").ToArray());
                    break;
                case "swapPositions":
                    effect = EffectDefinition.SwapPositions();
                    break;
                case "ifTeam":
                    effect = EffectDefinition.IfTeam(ReadTeam(e, "team"), e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "swapHealthPercent":
                    effect = EffectDefinition.SwapHealthPercent(e.Ints("amounts").ToArray());
                    break;
                case "randomArea":
                    effect = EffectDefinition.RandomArea(e.Int("radius"), e.Int("minDistance"), e.Int("maxDistance"), ReadTeam(e, "team"),
                        e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "slashes":
                    effect = EffectDefinition.Slashes(e.Int("radius"), e.Ints("amounts").ToArray(), e.Array("modifiers").Select(ReadModifier).ToArray());
                    break;
                case "knockback":
                    effect = EffectDefinition.Knockback(e.Ints("amounts").ToArray());
                    break;
                case "summon":
                    string unit = e.String("unitType");
                    int duration = e.OptionalMilliseconds("duration") ?? -1;   // none: until destroyed (mines)
                    int[] counts = e.Ints("counts").ToArray();
                    effect = e.Has("maxAlive")
                        ? EffectDefinition.Summon(unit, duration, e.OptionalBool("atPoint") == true, counts, e.Ints("maxAlive").ToArray(), e.OptionalArray("effects").Select(ReadEffect).ToArray())
                        : EffectDefinition.Summon(unit, duration, e.OptionalBool("atPoint") == true, counts, e.OptionalArray("effects").Select(ReadEffect).ToArray());
                    break;
                case "teleport":
                    effect = EffectDefinition.Teleport();
                    break;
                case "dispel":
                    effect = EffectDefinition.Dispel(e.OptionalBool("strong") ?? false);
                    break;
                case "projectile":
                    effect = EffectDefinition.Projectile(e.Int("speed"), e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "onCaster":
                    effect = EffectDefinition.OnCaster(e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "zone":
                    EffectDefinition[] pulse = e.Array("effects").Select(ReadEffect).ToArray();
                    int period = e.OptionalMilliseconds("period") ?? 0;
                    // Without a period the zone refreshes its effects continuously: only lasting modifiers make sense there.
                    if (period == 0 && pulse.Any(p => p.Kind != EffectKind.ApplyModifier || p.Modifiers.Any(m => m.IsPermanent)))
                        throw e.Error("effects", "a zone without a period may only apply modifiers with a duration");
                    effect = EffectDefinition.Zone(e.Int("radius"), ReadCenter(e, "center"), ReadTeam(e, "team"), e.Milliseconds("duration"), period,
                        e.OptionalBool("heroesOnly") ?? false, pulse);
                    break;
                case "pull":
                    AreaCenter toward = e.Has("toward") ? ReadCenter(e, "toward") : AreaCenter.Caster;
                    if (toward == AreaCenter.Target)
                        throw e.Error("toward", "expected caster or point");
                    effect = EffectDefinition.Pull(toward, e.Ints("amounts").ToArray());
                    break;
                case "illusion":
                    effect = EffectDefinition.Illusion(e.Milliseconds("duration"), e.Array("modifiers").Select(ReadModifier).ToArray(), e.Ints("counts").ToArray());
                    break;
                case "ifHero":
                    effect = EffectDefinition.IfHero(e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "execute":
                    effect = EffectDefinition.Execute(e.Ints("amounts").ToArray());
                    break;
                case "refreshCooldowns":
                    effect = EffectDefinition.RefreshCooldowns(e.OptionalBool("abilitiesOnly") ?? false);
                    break;
                case "ifMelee":
                    effect = EffectDefinition.IfMelee(e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "ifRanged":
                    effect = EffectDefinition.IfRanged(e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "attack":
                    effect = EffectDefinition.Attack(e.OptionalInts("amounts")?.ToArray() ?? new[] { 100 });   // no amounts: a normal attack
                    break;
                default:
                    throw e.Error("kind", "unknown effect kind");
            }

            if (e.OptionalString("levelOf") is string levelOf)
                effect.LevelledBy(levelOf);   // Eclipse's beams at Lucent Beam's level
            if (e.OptionalString("stacksFrom") is string stacksFrom)
                effect.StacksOf(stacksFrom);   // Uproar's roar by its stacks
            if (e.OptionalInt("structurePercent") is int structurePercent)
                effect.WithStructurePercent(structurePercent);   // Exorcism's half to buildings
            e.RejectUnknown();
            return effect;
        }

        private static AreaCenter ReadCenter(Reader r, string field) => r.String(field) switch
        {
            "caster" => AreaCenter.Caster,
            "target" => AreaCenter.Target,
            "point" => AreaCenter.Point,
            _ => throw r.Error(field, "expected caster, target or point"),
        };

        private static DamageType ReadDamageType(Reader r, string field) => r.String(field) switch
        {
            "physical" => DamageType.Physical,
            "magical" => DamageType.Magical,
            "pure" => DamageType.Pure,
            _ => throw r.Error(field, "expected physical, magical or pure"),
        };

        private static DamageScaling ReadScaling(Reader r, string field) => r.String(field) switch
        {
            "flat" => DamageScaling.Flat,
            "targetMaxHealthPercent" => DamageScaling.TargetMaxHealthPercent,
            "targetMissingHealthPercent" => DamageScaling.TargetMissingHealthPercent,
            "targetMissingManaPercent" => DamageScaling.TargetMissingManaPercent,
            "targetCurrentHealthPercent" => DamageScaling.TargetCurrentHealthPercent,
            "targetMaxManaPercent" => DamageScaling.TargetMaxManaPercent,
            "casterAttackDamagePercent" => DamageScaling.CasterAttackDamagePercent,
            "casterStrengthPercent" => DamageScaling.CasterStrengthPercent,
            "casterAgilityPercent" => DamageScaling.CasterAgilityPercent,
            "casterIntelligencePercent" => DamageScaling.CasterIntelligencePercent,
            "perTargetStack" => DamageScaling.PerTargetStack,
            "casterCurrentManaPercent" => DamageScaling.CasterCurrentManaPercent,
            "distanceToTargetPercent" => DamageScaling.DistanceToTargetPercent,
            "rewoundDistancePercent" => DamageScaling.RewoundDistancePercent,
            "distanceFalloff" => DamageScaling.DistanceFalloff,
            "fullChargeOnly" => DamageScaling.FullChargeOnly,
            "perChannelSecond" => DamageScaling.PerChannelSecond,
            "perUnitAround" => DamageScaling.PerUnitAround,
            "doublingPerStack" => DamageScaling.DoublingPerStack,
            _ => throw r.Error(field, "expected flat, targetMaxHealthPercent, targetMissingHealthPercent, targetCurrentHealthPercent, targetMaxManaPercent, " +
                                      "targetMissingManaPercent, casterAttackDamagePercent, casterStrengthPercent, casterAgilityPercent, casterIntelligencePercent, perTargetStack, casterCurrentManaPercent, distanceToTargetPercent, rewoundDistancePercent, distanceFalloff, fullChargeOnly, perChannelSecond, perUnitAround or doublingPerStack"),
        };

        private static ModifierFlags ReadFlag(Reader r, string flag) => flag switch
        {
            "stunned" => ModifierFlags.Stunned,
            "rooted" => ModifierFlags.Rooted,
            "silenced" => ModifierFlags.Silenced,
            "disarmed" => ModifierFlags.Disarmed,
            "invisible" => ModifierFlags.Invisible,
            "debuffImmune" => ModifierFlags.DebuffImmune,
            "cannotDie" => ModifierFlags.CannotDie,
            "physicalImmune" => ModifierFlags.PhysicalImmune,
            "feared" => ModifierFlags.Feared,
            "taunted" => ModifierFlags.Taunted,
            "invulnerable" => ModifierFlags.Invulnerable,
            "busy" => ModifierFlags.Busy,
            _ => throw r.Error("flags", "unknown flag '" + flag + "'"),
        };

        private static ModifierSpec ReadModifier(Reader m)
        {
            ModifierFlags flags = ModifierFlags.None;
            foreach (string flag in m.OptionalStrings("flags") ?? new string[0])
                flags |= ReadFlag(m, flag);

            var spec = new ModifierSpec(m.String("key"), m.OptionalMilliseconds("duration") ?? -1, flags,
                armorBonus: m.OptionalFixed("armor") ?? Fixed.Zero, moveSpeedHundredths: m.OptionalHundredth("moveSpeedPercent") ?? 0,
                attackSpeedBonus: m.OptionalInt("attackSpeed") ?? 0, damageBonus: m.OptionalInt("damage") ?? 0, magicResistBonus: m.OptionalInt("magicResist") ?? 0,
                damagePerSecond: m.OptionalFixed("damagePerSecond") ?? Fixed.Zero,
                damageType: m.Has("damageType") ? ReadDamageType(m, "damageType") : DamageType.Magical,
                healPerSecond: m.OptionalFixed("healPerSecond") ?? Fixed.Zero, periodMs: m.OptionalMilliseconds("period") ?? (m.OptionalInt("shiftsPerSecond") is int rate && rate > 0 ? 1000 / rate : 1000),
                critChanceHundredths: m.OptionalHundredth("critChance") ?? 0, critMultiplierPercent: m.OptionalInt("critMultiplier") ?? 0,
                structureDamagePercent: m.OptionalInt("structureDamage") ?? 0, lifestealPercent: m.OptionalInt("lifesteal") ?? 0,
                maxHpBonus: m.OptionalInt("maxHealth") ?? 0, maxManaBonus: m.OptionalInt("maxMana") ?? 0, hpRegenBonus: m.OptionalFixed("healthRegen") ?? Fixed.Zero,
                breakOnAction: m.OptionalBool("breakOnAction") ?? false, breakRadius: m.OptionalInt("breakRadius") ?? 0, dispellable: m.OptionalBool("dispellable") ?? true,
                strengthBonus: m.OptionalInt("strength") ?? 0, agilityBonus: m.OptionalInt("agility") ?? 0, intelligenceBonus: m.OptionalInt("intelligence") ?? 0,
                manaRegenBonus: m.OptionalFixed("manaRegen") ?? Fixed.Zero, moveSpeedBonus: m.OptionalInt("moveSpeed") ?? 0, attackRangeBonus: m.OptionalInt("attackRange") ?? 0,
                incomingDamageHundredths: m.OptionalHundredth("incomingDamagePercent") ?? 0, attackDamageHundredths: m.OptionalHundredth("attackDamagePercent") ?? 0,
                evasionHundredths: m.OptionalHundredth("evasion") ?? 0, onHit: m.OptionalArray("onHit").Select(ReadEffect).ToList(),
                onHitChancePercent: 0, onHitChanceHundredths: m.OptionalHundredth("onHitChance") ?? 10000, aura: m.OptionalObject("aura") is Reader a ? ReadAura(a) : null,
                cleavePercent: m.OptionalInt("cleavePercent") ?? 0, cleaveRadius: m.OptionalInt("cleaveRadius") ?? 0,
                splashPercent: m.OptionalInt("splash") ?? 0, splashRadius: m.OptionalInt("splashRadius") ?? 0,
                reviveDelayMs: m.OptionalMilliseconds("reviveDelay") ?? 0, consumedOnRevive: m.OptionalBool("consumedOnRevive") ?? false,
                expireModifier: m.OptionalObject("expireModifier") is Reader ex ? ReadModifier(ex) : null,
                healPerSecondMaxHealthPercent: m.OptionalFixed("healthRegenMaxPercent") ?? Dynasty.Core.Mathematics.Fixed.Zero,
                manaPerSecondMaxManaPercent: m.OptionalFixed("manaRegenMaxPercent") ?? Dynasty.Core.Mathematics.Fixed.Zero,
                statusResistancePercent: m.OptionalInt("statusResistance") ?? 0, slowResistancePercent: m.OptionalInt("slowResistance") ?? 0,
                spellBlockCooldownMs: m.OptionalMilliseconds("spellBlockCooldown") ?? 0, illusionDamageTakenPercent: m.OptionalInt("illusionDamageTaken") ?? 0,
                baseDamagePercent: m.OptionalInt("baseDamagePercent") ?? 0, spellAmplificationPercent: m.OptionalInt("spellAmplification") ?? 0,
                cooldownReductionPercent: m.OptionalInt("cooldownReduction") ?? 0, manaCostReductionPercent: m.OptionalInt("manaCostReduction") ?? 0,
                fixedMoveSpeed: m.OptionalInt("fixedMoveSpeed") ?? 0, barrierMaxHealthPercent: m.OptionalInt("barrierMaxHealthPercent") ?? 0,
                damagedRegenMaxPercent: m.OptionalFixed("damagedRegenMaxPercent") ?? Dynasty.Core.Mathematics.Fixed.Zero, damagedRegenMs: m.OptionalMilliseconds("damagedRegen") ?? 0,
                endsWhenFull: m.OptionalBool("endsWhenFull") ?? false, barrier: m.OptionalInt("barrier") ?? 0, consumedOnHit: m.OptionalBool("consumedOnHit") ?? false,
                damagePerSecondMaxHealthPercent: m.OptionalFixed("damagePerSecondMaxHealthPercent") ?? Fixed.Zero,
                onHitAfterAttacks: m.OptionalInt("onHitAfterAttacks") ?? 0, whenAttacked: m.OptionalArray("whenAttacked").Select(ReadEffect).ToList(),
                whenAttackedEvery: m.OptionalInt("whenAttackedEvery") ?? 1,
                maxStacks: m.OptionalBool("stacking") == true ? m.OptionalInt("maxStacks") ?? int.MaxValue : 0,
                procCooldownMs: m.OptionalMilliseconds("procCooldown") ?? 0, whenAttackedChancePercent: 0, whenAttackedChanceHundredths: m.OptionalHundredth("whenAttackedChance") ?? 10000,
                onCast: m.OptionalArray("onCast").Select(ReadEffect).ToList(), onKill: m.OptionalArray("onKill").Select(ReadEffect).ToList(),
                onDeath: m.OptionalArray("onDeath").Select(ReadEffect).ToList(), onHitHeroesOnly: m.OptionalBool("onHitHeroesOnly") ?? false,
                // A chance alone means one extra target (Gunslinger).
                extraTargets: m.OptionalInt("extraTargets") ?? (m.Has("extraTargetChance") ? 1 : 0), extraTargetRadius: m.OptionalInt("extraTargetRadius") ?? 0,
                // Dota gives bounces either as the share dealt or as a reduction (Moon Glaive: damage_reduction_percent).
                extraTargetDamagePercent: m.OptionalInt("extraTargetDamagePercent") ?? 100 - (m.OptionalInt("extraTargetDamageReduction") ?? 0), onKillHeroesOnly: m.OptionalBool("onKillHeroesOnly") ?? false,
                revives: m.OptionalBool("revives") ?? false, attackCharges: m.OptionalInt("attackCharges") ?? 0,
                extraTargetRadiusFromAttackRange: m.OptionalBool("extraTargetRadiusFromAttackRange") ?? false,
                extraTargetChancePercent: m.OptionalInt("extraTargetChance") ?? 100, pseudoRandom: m.OptionalBool("pseudoRandom") ?? false,
                unique: m.OptionalBool("unique") ?? false,
                reflectHundredths: m.OptionalHundredth("reflect") ?? 0, reflectFullRadius: m.OptionalInt("reflectFullRadius") ?? 0,
                reflectRadius: m.OptionalInt("reflectRadius") ?? 0, returnPercent: m.OptionalInt("returnPercent") ?? 0,
                onReturn: m.OptionalArray("onReturn").Select(ReadEffect).ToList(), damageBlock: m.OptionalInt("damageBlock") ?? 0,
                damageBlockCreepPercent: m.OptionalInt("damageBlockCreepPercent") ?? 100 - (m.OptionalInt("damageBlockCreepReduction") ?? 0), cleanseThreshold: m.OptionalInt("cleanseThreshold") ?? 0,
                cleanseWindowMs: m.OptionalMilliseconds("cleanseWindow") ?? 0, absorbInstances: m.OptionalInt("absorbInstances") ?? 0,
                absorbBarrier: m.OptionalInt("absorbBarrier") ?? 0, layers: m.OptionalInt("layers") ?? 0,
                layerReductionPercent: m.OptionalInt("layerReduction") ?? 0, layerRecoveryMs: m.OptionalMilliseconds("layerRecovery") ?? 0,
                layerMinimumDamage: m.OptionalInt("layerMinimumDamage") ?? 0, sideReductionPercent: m.OptionalInt("sideReduction") ?? 0,
                backReductionPercent: m.OptionalInt("backReduction") ?? 0, sideAngle: m.OptionalInt("sideAngle") ?? 0, backAngle: m.OptionalInt("backAngle") ?? 0,
                releaseAbilityId: m.OptionalString("releaseAbility"), releaseThreshold: m.OptionalInt("releaseThreshold") ?? 0,
                manaCostPerSecond: m.OptionalFixed("manaCostPerSecond") ?? Fixed.Zero, instanceBlock: m.OptionalInt("instanceBlock") ?? 0, damageHeals: m.OptionalBool("damageHeals") ?? false,
                critMultiplierMaxPercent: m.OptionalInt("critMultiplierMax") ?? 0, critLifestealPercent: m.OptionalInt("critLifesteal") ?? 0,
                resetStacksKey: m.OptionalString("resetStacksKey"), onHitManaCost: m.OptionalInt("onHitManaCost") ?? 0,
                onHitManaCostPercent: m.OptionalInt("onHitManaCostPercent") ?? 0, fullBelowHealthPercent: m.OptionalInt("fullBelowHealthPercent") ?? 0,
                hpRegenStrengthPercent: m.OptionalFixed("healthRegenStrengthPercent") ?? Fixed.Zero, invisibleAfterIdleMs: m.OptionalMilliseconds("invisibleAfterIdle") ?? 0,
                shareDamagePercent: m.OptionalInt("shareDamagePercent") ?? 0, spreadDamagePercent: m.OptionalInt("spreadDamagePercent") ?? 0,
                spreadRadius: m.OptionalInt("spreadRadius") ?? 0, periodicEffects: m.OptionalArray("periodicEffects").Select(ReadEffect).ToList(),
                breakDistance: m.OptionalInt("breakDistance") ?? 0, healSharePercent: m.OptionalInt("healSharePercent") ?? 0,
                manaSharePercent: m.OptionalInt("manaSharePercent") ?? 0, returnsOnExpiry: m.OptionalBool("returnsOnExpiry") ?? false,
                castDamage: m.OptionalInt("castDamage") ?? 0, castDamageManaPercent: m.OptionalInt("castDamageManaPercent") ?? 0,
                proximityRadius: m.OptionalInt("proximityRadius") ?? 0, proximityDelayMs: m.OptionalMilliseconds("proximityDelay") ?? 0,
                onProximity: m.OptionalArray("onProximity").Select(ReadEffect).ToList(), periodicFromBearer: m.OptionalBool("periodicFromBearer") ?? false,
                baseArmorReductionPercent: m.OptionalInt("baseArmorReductionPercent") ?? 0, baseMagicResistReductionPercent: m.OptionalInt("baseMagicResistReductionPercent") ?? 0,
                baseAttackTimeMs: m.OptionalMilliseconds("baseAttackTime") ?? 0,
                critEveryAttacks: m.OptionalInt("critEveryAttacks") ?? 0, critHealPercent: m.OptionalInt("critHealPercent") ?? 0,
                critHealRadius: m.OptionalInt("critHealRadius") ?? 0, critHealAlliesPercent: m.OptionalInt("critHealAlliesPercent") ?? 0,
                critHealCreepPercent: m.OptionalInt("critHealCreepPercent") ?? 100,
                onEnd: m.OptionalArray("onEnd").Select(ReadEffect).ToList(), healAtEndPercent: m.OptionalInt("healAtEndPercent") ?? 0,
                multicastHundredths: m.Has("multicast") ? m.Hundredths("multicast") : null,
                extraTargetHeroesFirst: m.OptionalBool("extraTargetHeroesFirst") ?? false, extraTargetElseSame: m.OptionalBool("extraTargetElseSame") ?? false, multicastStrengthPerPercent: m.OptionalInt("multicastStrengthPerPercent") ?? 0,
                trampleRadius: m.OptionalInt("trampleRadius") ?? 0, trampleMarker: m.OptionalString("trampleMarker"),
                onTrample: m.OptionalArray("onTrample").Select(ReadEffect).ToList(),
                delaysHealth: m.OptionalBool("delaysHealth") ?? false, delayedHealAmpPercent: m.OptionalInt("delayedHealAmpPercent") ?? 0,
                onHeroDamageAtLeast: m.OptionalInt("onHeroDamageAtLeast") ?? 0, onHeroDamage: m.OptionalArray("onHeroDamage").Select(ReadEffect).ToList(),
                cooldownSlowPercent: m.OptionalInt("cooldownSlowPercent") ?? 0, stacksPerCooldown: m.OptionalBool("stacksPerCooldown") ?? false,
                anchorChain: m.OptionalInt("anchorChain") ?? 0, anchorDragSpeed: m.OptionalInt("anchorDragSpeed") ?? 0, redirectsSpells: m.OptionalBool("redirectsSpells") ?? false,
                thirstMoveSpeedPercent: m.OptionalInt("thirstMoveSpeedPercent") ?? 0, thirstFromHealthPercent: m.OptionalInt("thirstFromHealthPercent") ?? 100,
                thirstFullBelowHealthPercent: m.OptionalInt("thirstFullBelowHealthPercent") ?? 0, revealsBelowHealthPercent: m.OptionalInt("revealsBelowHealthPercent") ?? 0, growsWithChannelMs: m.OptionalMilliseconds("growsWithChannel") ?? 0,
                attributeShift: m.OptionalString("attributeShift") switch
                {
                    null => 0,
                    "toAgility" => 1,
                    "toStrength" => -1,
                    _ => throw m.Error("attributeShift", "expected toAgility or toStrength"),
                });
            m.RejectUnknown();
            return spec;
        }

        private static AuraSpec ReadAura(Reader a)
        {
            var aura = new AuraSpec(a.Int("radius"), ReadTeam(a, "team"), a.OptionalBool("heroesOnly") ?? false, ReadModifier(a.OptionalObject("modifier") ?? throw a.Error("modifier", "missing")),
                a.OptionalBool("creepsOnly") ?? false);
            a.RejectUnknown();
            return aura;
        }

        private static ItemDefinition ReadItem(Reader i)
        {
            ModifierSpec? bonuses = i.OptionalObject("bonuses") is Reader b ? ReadModifier(b) : null;
            var item = new ItemDefinition(i.String("id"), i.String("name"), i.Int("cost"), bonuses, i.OptionalStrings("components"),
                i.OptionalString("active"), i.OptionalInt("charges") ?? 0, i.OptionalString("upgrade"), i.OptionalBool("inShop") ?? true);
            i.RejectUnknown();
            return item;
        }

        /// <summary>Reads fields of one JSON object and names the field in every error (e.g. <c>heroes[1].agility</c>).</summary>
        private sealed class Reader
        {
            private readonly JsonValue _value;
            private readonly string _path;
            private readonly HashSet<string> _read = new HashSet<string>();

            public Reader(JsonValue value, string path)
            {
                if (value.Kind != JsonKind.Object)
                    throw new RulesetPackException((path.Length == 0 ? "The pack" : path) + ": expected an object.");
                _value = value;
                _path = path;
            }

            public RulesetPackException Error(string field, string what) =>
                new RulesetPackException(Path(field) + ": " + what + ".");

            private string Path(string field) => _path.Length == 0 ? field : _path + "." + field;

            public bool Has(string field)
            {
                _read.Add(field);
                return _value.Has(field);
            }

            /// <summary>A known field this build does not use.</summary>
            public void Ignore(string field) => _read.Add(field);

            /// <summary>A field the format does not know is a mistake: a typo would otherwise be dropped silently.</summary>
            public void RejectUnknown()
            {
                foreach (KeyValuePair<string, JsonValue> member in _value.Members)
                {
                    if (!_read.Contains(member.Key))
                        throw Error(member.Key, "unknown field");
                }
            }

            public T Read<T>(string field, Func<JsonValue, T> read)
            {
                _read.Add(field);
                JsonValue? value = _value.Get(field);
                if (value == null)
                    throw Error(field, "missing");
                try
                {
                    return read(value);
                }
                catch (JsonException e)
                {
                    throw Error(field, e.Message.TrimEnd('.'));
                }
            }

            public string String(string field) => Read(field, v => v.AsString());

            public byte[] Base64(string field)
            {
                string text = String(field);
                try
                {
                    return Convert.FromBase64String(text);
                }
                catch (FormatException)
                {
                    throw Error(field, "expected base64");
                }
            }
            public int Int(string field) => Read(field, v => v.AsInt());
            public Fixed Fixed(string field) => Read(field, v => v.AsFixed());

            /// <summary>Seconds in the document, milliseconds in the ruleset.</summary>
            public int Milliseconds(string field) => ToMilliseconds(Read(field, v => v.AsFixed()));

            public int? OptionalInt(string field) => Has(field) ? Int(field) : (int?)null;

            /// <summary>A percent that may have decimals (33.33) in the document, hundredths of a percent in the ruleset.</summary>
            public int? OptionalHundredth(string field) => Has(field)
                ? (Fixed(field) * Dynasty.Core.Mathematics.Fixed.FromInt(100) + Dynasty.Core.Mathematics.Fixed.FromRatio(1, 2)).ToIntFloor()
                : (int?)null;
            public Fixed? OptionalFixed(string field) => Has(field) ? Fixed(field) : (Fixed?)null;
            public bool? OptionalBool(string field) => Has(field) ? Read(field, v => v.AsBool()) : (bool?)null;
            public string? OptionalString(string field) => Has(field) ? String(field) : null;
            public int? OptionalMilliseconds(string field) => Has(field) ? Milliseconds(field) : (int?)null;

            public IReadOnlyList<int> Ints(string field) => Read(field, v => ItemsOf(v).Select(i => i.AsInt()).ToList());

            /// <summary>Seconds per level in the document, milliseconds in the ruleset.</summary>
            public IReadOnlyList<int> Seconds(string field) => Read(field, v => ItemsOf(v).Select(i => ToMilliseconds(i.AsFixed())).ToList());

            /// <summary>Percents per level in the document, hundredths of a percent in the ruleset.</summary>
            public IReadOnlyList<int> Hundredths(string field) => Read(field, v => ItemsOf(v)
                .Select(i => (i.AsFixed() * Dynasty.Core.Mathematics.Fixed.FromInt(100) + Dynasty.Core.Mathematics.Fixed.FromRatio(1, 2)).ToIntFloor()).ToList());

            public IReadOnlyList<string> Strings(string field) => Read(field, v => ItemsOf(v).Select(i => i.AsString()).ToList());

            public IReadOnlyList<Fixed> Fixeds(string field) => Read(field, v => ItemsOf(v).Select(i => i.AsFixed()).ToList());

            public IReadOnlyList<string>? OptionalStrings(string field) => Has(field) ? Strings(field) : null;

            public IReadOnlyList<int>? OptionalInts(string field) => Has(field) ? Ints(field) : null;

            public IEnumerable<Reader> Array(string field)
            {
                IReadOnlyList<JsonValue> items = Read(field, ItemsOf);
                for (int i = 0; i < items.Count; i++)
                    yield return new Reader(items[i], Path(field) + "[" + i + "]");
            }

            public IEnumerable<Reader> OptionalArray(string field) => Has(field) ? Array(field) : Enumerable.Empty<Reader>();

            public Reader? OptionalObject(string field) => Has(field) ? new Reader(_value[field], Path(field)) : null;

            private static int ToMilliseconds(Fixed seconds) =>
                (seconds * Dynasty.Core.Mathematics.Fixed.FromInt(1000) + Dynasty.Core.Mathematics.Fixed.FromRatio(1, 2)).ToIntFloor();

            private static IReadOnlyList<JsonValue> ItemsOf(JsonValue v) =>
                v.Kind == JsonKind.Array ? v.Items : throw new JsonException("Expected Array, found " + v.Kind + ".");
        }
    }
}
