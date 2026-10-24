using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// Original heroes, abilities, items and summons of the built-in ruleset. Ten archetypes:
    /// carry, mid nuker, initiator, control mage, healer, summoner, mobile assassin, pusher,
    /// support and late scaler. Names and numbers are original.
    /// </summary>
    public static class BaseContent
    {
        private static readonly int[] Basic = { 1, 3, 5, 7 };
        private static readonly int[] Ultimate = { 6, 11 };

        public static IReadOnlyList<CreepType> SummonTypes { get; } = new[]
        {
            new CreepType("swarmling", Stats(hp: 220, armor: 0, speed: 350, dmgMin: 14, dmgMax: 16, range: 100, intervalMs: 1000, pointMs: 400, acquisition: 600, radius: 12), new Bounty(18, 22, 20)),
            new CreepType("hive_guard", Stats(hp: 650, armor: 3, speed: 330, dmgMin: 34, dmgMax: 40, range: 120, intervalMs: 1200, pointMs: 400, acquisition: 600, radius: 20), new Bounty(30, 36, 40)),
            new CreepType("golem", Stats(hp: 700, armor: 4, speed: 300, dmgMin: 30, dmgMax: 36, range: 120, intervalMs: 1300, pointMs: 400, acquisition: 600, radius: 24), new Bounty(30, 36, 40)),
            new CreepType("jungle_small", Stats(hp: 420, armor: 1, speed: 320, dmgMin: 16, dmgMax: 19, range: 100, intervalMs: 1100, pointMs: 400, acquisition: 400, radius: 16), new Bounty(32, 38, 42)),
            new CreepType("jungle_large", Stats(hp: 850, armor: 3, speed: 300, dmgMin: 30, dmgMax: 36, range: 120, intervalMs: 1300, pointMs: 400, acquisition: 400, radius: 24), new Bounty(55, 65, 80)),
            new CreepType("colossus", Stats(hp: 6000, armor: 15, speed: 270, dmgMin: 75, dmgMax: 85, range: 150, intervalMs: 1200, pointMs: 300, acquisition: 400, radius: 60,
                magicResist: 50, vision: 600)),
            new CreepType("observer_ward", Stats(hp: 4, armor: 0, speed: 0, dmgMin: 0, dmgMax: 0, range: 0, intervalMs: 1000, pointMs: 0, acquisition: 0, radius: 8,
                vision: 1600, innate: ModifierFlags.Invisible), new Bounty(30, 30, 0)),
            new CreepType("sentry_ward", Stats(hp: 4, armor: 0, speed: 0, dmgMin: 0, dmgMax: 0, range: 0, intervalMs: 1000, pointMs: 0, acquisition: 0, radius: 8,
                vision: 300, trueSight: 900, innate: ModifierFlags.Invisible), new Bounty(20, 20, 0)),
        };

        public static IReadOnlyList<AbilityDefinition> Abilities { get; } = BuildAbilities();

        public static IReadOnlyList<ItemDefinition> Items { get; } = BuildItems();

        public static IReadOnlyList<HeroDefinition> Heroes { get; } = BuildHeroes();

        /// <summary>
        /// The Colossus pit sits on the anti-diagonal (x + y = 10000), the same distance from both
        /// fountains; the map's only point-symmetric spot, the center, is on the mid lane.
        /// </summary>
        public static BossDefinition Boss { get; } = new BossDefinition(
            "colossus", "colossus", FixedVector2.FromInts(3800, 6200), leashRadius: 700, firstSpawnMs: 0,
            respawnMinMs: 480000, respawnMaxMs: 660000, teamGold: 200,
            reward: new ModifierSpec("colossus.blessing", 180000, damageBonus: 30, armorBonus: Fixed.FromInt(5), hpRegenBonus: Fixed.FromInt(10)));

        /// <summary>Two river spots on the anti-diagonal (mirror images, equidistant from both fountains); a rune every 2 minutes.</summary>
        public static RuneRules Runes { get; } = new RuneRules(
            new[] { FixedVector2.FromInts(3000, 7000), FixedVector2.FromInts(7000, 3000) },
            firstSpawnMs: 120000,
            intervalMs: 120000,
            new[]
            {
                new RuneType("haste", new ModifierSpec("rune.haste", 20000, moveSpeedPercent: 40)),
                new RuneType("might", new ModifierSpec("rune.might", 30000, damageBonus: 40)),
                new RuneType("renewal", new ModifierSpec("rune.renewal", 5000, healPerSecond: Fixed.FromInt(60))),
                new RuneType("bounty", gold: 75),
            });

        public static HeroRules Rules { get; } = new HeroRules(
            xpForLevel: new[] { 0, 230, 600, 1080, 1660, 2260, 2980, 3730, 4510, 5320, 6160, 7030, 7930, 9000, 10200 },
            xpShareRadius: 1200,
            heroKillXpBase: 120,
            heroKillXpPerLevel: 30,
            heroKillGoldBase: 150,
            heroKillGoldPerLevel: 8,
            assistWindowMs: 10000,
            goldPerMinute: 90,
            startingGold: 600,
            respawnBaseMs: 4000,
            respawnPerLevelMs: 2500,
            goldLossPerLevel: 20,
            structureTeamGold: 120,
            fountainHealPercentPerSecond: 5,
            buybackBaseGold: 150,
            buybackGoldPerLevel: 40,
            buybackCooldownMs: 480000);

        private static IReadOnlyList<AbilityDefinition> BuildAbilities()
        {
            var list = new List<AbilityDefinition>();

            // Ironvow — carry: crit, self buff, lifesteal; passive attack speed ultimate.
            list.Add(Passive("ironvow.rending_strike", "Rending Strike", Basic,
                Levels(4, l => new ModifierSpec("ironvow.rending_strike", -1, critChancePercent: 10 + 5 * l, critMultiplierPercent: 180))));
            list.Add(Cast("ironvow.iron_will", "Iron Will", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, Basic, Cd(16000, 14000, 12000, 10000), Mana(60, 60, 60, 60),
                EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("ironvow.iron_will", 6000, armorBonus: Fixed.FromInt(2 + 2 * l), attackSpeedBonus: 15 + 10 * l)))));
            list.Add(Passive("ironvow.bloodthirst", "Bloodthirst", Basic,
                Levels(4, l => new ModifierSpec("ironvow.bloodthirst", -1, lifestealPercent: 5 + 5 * l))));
            list.Add(Passive("ironvow.unbroken", "Unbroken", Ultimate,
                Levels(2, l => new ModifierSpec("ironvow.unbroken", -1, attackSpeedBonus: 10 + 30 * l, damageBonus: 10 * l))));

            // Emberwisp — mid nuker: lance, burning field, ember passive, inferno.
            list.Add(Cast("emberwisp.fire_lance", "Fire Lance", AbilityTargeting.Unit, TargetTeam.Enemy, 650, 300, Basic, Cd(9000, 8000, 7000, 6000), Mana(90, 105, 120, 135),
                EffectDefinition.Damage(DamageType.Magical, 90, 150, 210, 270)));
            list.Add(Cast("emberwisp.ember_field", "Ember Field", AbilityTargeting.Point, TargetTeam.Enemy, 700, 400, Basic, Cd(14000, 13000, 12000, 11000), Mana(110, 120, 130, 140),
                EffectDefinition.Area(275, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 50, 80, 110, 140),
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("emberwisp.burn", 3000, damagePerSecond: Fixed.FromInt(10 + 10 * l)))))));
            list.Add(Passive("emberwisp.kindling", "Kindling", Basic,
                Levels(4, l => new ModifierSpec("emberwisp.kindling", -1, damageBonus: 4 * l, magicResistBonus: 3 * l))));
            list.Add(Cast("emberwisp.inferno", "Inferno", AbilityTargeting.Point, TargetTeam.Enemy, 800, 500, Ultimate, Cd(70000, 55000), Mana(200, 300),
                EffectDefinition.Area(400, AreaCenter.Point, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 280, 420))));

            // Stonewarden — initiator: ram (dash + stun), tremor slow, stone skin, earthshatter.
            list.Add(Cast("stonewarden.ram", "Ram", AbilityTargeting.Unit, TargetTeam.Enemy, 700, 200, Basic, Cd(14000, 13000, 12000, 11000), Mana(90, 100, 110, 120),
                EffectDefinition.Dash(),
                EffectDefinition.Damage(DamageType.Physical, 70, 100, 130, 160),
                EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("stonewarden.ram.stun", 800 + 200 * l, ModifierFlags.Stunned)))));
            list.Add(Cast("stonewarden.tremor", "Tremor", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 300, Basic, Cd(12000, 11000, 10000, 9000), Mana(80, 90, 100, 110),
                EffectDefinition.Area(300, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 60, 90, 120, 150),
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("stonewarden.tremor.slow", 2500, moveSpeedPercent: -15 - 5 * l))))));
            list.Add(Passive("stonewarden.stone_skin", "Stone Skin", Basic,
                Levels(4, l => new ModifierSpec("stonewarden.stone_skin", -1, armorBonus: Fixed.FromInt(l + 1), maxHpBonus: 50 * l))));
            list.Add(Cast("stonewarden.earthshatter", "Earthshatter", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 400, Ultimate, Cd(80000, 65000), Mana(150, 225),
                EffectDefinition.Area(450, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 200, 300),
                    EffectDefinition.Modifier(Levels(2, l => ModifierSpec.Status("stonewarden.earthshatter.stun", 1500 + 500 * l, ModifierFlags.Stunned))))));

            // Frostweaver — control mage: shackle stun, glacier slow, frost mantle, deep winter.
            list.Add(Cast("frostweaver.frost_shackle", "Frost Shackle", AbilityTargeting.Unit, TargetTeam.Enemy, 600, 300, Basic, Cd(13000, 12000, 11000, 10000), Mana(100, 110, 120, 130),
                EffectDefinition.Damage(DamageType.Magical, 60, 90, 120, 150),
                EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("frostweaver.shackle", 1000 + 300 * l, ModifierFlags.Stunned)))));
            list.Add(Cast("frostweaver.glacier", "Glacier", AbilityTargeting.Point, TargetTeam.Enemy, 700, 300, Basic, Cd(12000, 11000, 10000, 9000), Mana(90, 100, 110, 120),
                EffectDefinition.Area(300, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 40, 70, 100, 130),
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("frostweaver.glacier.slow", 3000, moveSpeedPercent: -20 - 8 * l, attackSpeedBonus: -10 * l))))));
            list.Add(Passive("frostweaver.frost_mantle", "Frost Mantle", Basic,
                Levels(4, l => new ModifierSpec("frostweaver.frost_mantle", -1, magicResistBonus: 5 * l, maxManaBonus: 50 * l))));
            list.Add(Cast("frostweaver.deep_winter", "Deep Winter", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 500, Ultimate, Cd(75000, 60000), Mana(200, 275),
                EffectDefinition.Area(500, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Modifier(Levels(2, l => new ModifierSpec("frostweaver.deep_winter", 4000, moveSpeedPercent: -40 - 10 * l, damagePerSecond: Fixed.FromInt(40 + 20 * l)))))));

            // Dawnmender — healer: mend, sanctuary heal-over-time, smite silence, renewal.
            list.Add(Cast("dawnmender.mend", "Mend", AbilityTargeting.Unit, TargetTeam.Ally, 600, 300, Basic, Cd(10000, 9000, 8000, 7000), Mana(90, 100, 110, 120),
                EffectDefinition.Heal(120, 180, 240, 300)));
            list.Add(Cast("dawnmender.sanctuary", "Sanctuary", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, Basic, Cd(20000, 18000, 16000, 14000), Mana(100, 110, 120, 130),
                EffectDefinition.Area(500, AreaCenter.Caster, TargetTeam.Ally,
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("dawnmender.sanctuary", 5000, healPerSecond: Fixed.FromInt(10 + 10 * l), armorBonus: Fixed.FromInt(l)))))));
            list.Add(Cast("dawnmender.smite", "Smite", AbilityTargeting.Unit, TargetTeam.Enemy, 600, 300, Basic, Cd(14000, 13000, 12000, 11000), Mana(80, 90, 100, 110),
                EffectDefinition.Damage(DamageType.Magical, 70, 100, 130, 160),
                EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("dawnmender.smite.silence", 1000 + 500 * l, ModifierFlags.Silenced)))));
            list.Add(Cast("dawnmender.renewal", "Renewal", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 300, Ultimate, Cd(90000, 70000), Mana(200, 300),
                EffectDefinition.Area(900, AreaCenter.Caster, TargetTeam.Ally, EffectDefinition.Heal(300, 450))));

            // Hivecaller — summoner: brood, sting, chitin, swarm lord.
            list.Add(Cast("hivecaller.brood", "Brood", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 300, Basic, Cd(18000, 17000, 16000, 15000), Mana(90, 100, 110, 120),
                EffectDefinition.Summon("swarmling", 25000, 2, 2, 3, 3)));
            list.Add(Cast("hivecaller.sting", "Sting", AbilityTargeting.Unit, TargetTeam.Enemy, 550, 200, Basic, Cd(10000, 9000, 8000, 7000), Mana(70, 80, 90, 100),
                EffectDefinition.Damage(DamageType.Magical, 60, 90, 120, 150),
                EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("hivecaller.sting.slow", 2000, moveSpeedPercent: -15 - 5 * l)))));
            list.Add(Passive("hivecaller.chitin", "Chitin", Basic,
                Levels(4, l => new ModifierSpec("hivecaller.chitin", -1, armorBonus: Fixed.FromInt(l), hpRegenBonus: Fixed.FromInt(l)))));
            list.Add(Cast("hivecaller.swarm_lord", "Swarm Lord", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 500, Ultimate, Cd(70000, 60000), Mana(200, 275),
                EffectDefinition.Summon("hive_guard", 40000, 3, 5)));

            // Shadeblade — mobile assassin: shadow step, rupture, keen edge, death mark.
            list.Add(Cast("shadeblade.shadow_step", "Shadow Step", AbilityTargeting.Unit, TargetTeam.Enemy, 800, 0, Basic, Cd(12000, 10000, 8000, 6000), Mana(70, 75, 80, 85),
                EffectDefinition.Dash(),
                EffectDefinition.Damage(DamageType.Physical, 80, 120, 160, 200)));
            list.Add(Cast("shadeblade.rupture", "Rupture", AbilityTargeting.Unit, TargetTeam.Enemy, 400, 200, Basic, Cd(14000, 13000, 12000, 11000), Mana(80, 90, 100, 110),
                EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("shadeblade.rupture", 4000, damagePerSecond: Fixed.FromInt(20 + 15 * l), damageType: DamageType.Physical, moveSpeedPercent: -10)))));
            list.Add(Passive("shadeblade.keen_edge", "Keen Edge", Basic,
                Levels(4, l => new ModifierSpec("shadeblade.keen_edge", -1, critChancePercent: 10 + 3 * l, critMultiplierPercent: 200))));
            list.Add(Cast("shadeblade.death_mark", "Death Mark", AbilityTargeting.Unit, TargetTeam.Enemy, 500, 300, Ultimate, Cd(60000, 45000), Mana(150, 200),
                EffectDefinition.Damage(DamageType.Pure, 250, 400),
                EffectDefinition.Modifier(Levels(2, l => new ModifierSpec("shadeblade.death_mark.slow", 3000, moveSpeedPercent: -30 - 10 * l)))));

            // Siegewright — pusher: demolition, mortar, construct golem, bombardment.
            list.Add(Passive("siegewright.demolition", "Demolition", Basic,
                Levels(4, l => new ModifierSpec("siegewright.demolition", -1, structureDamagePercent: 20 + 20 * l))));
            list.Add(Cast("siegewright.mortar", "Mortar", AbilityTargeting.Point, TargetTeam.Enemy, 800, 400, Basic, Cd(10000, 9000, 8000, 7000), Mana(80, 90, 100, 110),
                EffectDefinition.Area(250, AreaCenter.Point, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Physical, 80, 120, 160, 200))));
            list.Add(Cast("siegewright.construct", "Construct", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 500, Basic, Cd(30000, 28000, 26000, 24000), Mana(120, 130, 140, 150),
                EffectDefinition.Summon("golem", 30000, 1, 1, 2, 2)));
            list.Add(Cast("siegewright.bombardment", "Bombardment", AbilityTargeting.Point, TargetTeam.Enemy, 900, 500, Ultimate, Cd(60000, 50000), Mana(175, 250),
                EffectDefinition.Area(350, AreaCenter.Point, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Magical, 250, 375))));

            // Thornkeeper — support: bramble root, thorn ward slow, barkskin, overgrowth.
            list.Add(Cast("thornkeeper.bramble", "Bramble", AbilityTargeting.Unit, TargetTeam.Enemy, 600, 300, Basic, Cd(12000, 11000, 10000, 9000), Mana(80, 90, 100, 110),
                EffectDefinition.Damage(DamageType.Magical, 50, 75, 100, 125),
                EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("thornkeeper.bramble.root", 1000 + 400 * l, ModifierFlags.Rooted)))));
            list.Add(Cast("thornkeeper.thorn_ward", "Thorn Ward", AbilityTargeting.Point, TargetTeam.Enemy, 700, 300, Basic, Cd(14000, 13000, 12000, 11000), Mana(80, 90, 100, 110),
                EffectDefinition.Area(325, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("thornkeeper.thorn_ward", 3000, moveSpeedPercent: -20 - 5 * l, damagePerSecond: Fixed.FromInt(10 + 5 * l)))))));
            list.Add(Cast("thornkeeper.barkskin", "Barkskin", AbilityTargeting.Unit, TargetTeam.Ally, 600, 0, Basic, Cd(16000, 15000, 14000, 13000), Mana(70, 80, 90, 100),
                EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("thornkeeper.barkskin", 6000, armorBonus: Fixed.FromInt(3 + 2 * l), healPerSecond: Fixed.FromInt(5 + 5 * l))))));
            list.Add(Cast("thornkeeper.overgrowth", "Overgrowth", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 500, Ultimate, Cd(80000, 65000), Mana(175, 250),
                EffectDefinition.Area(500, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Modifier(Levels(2, l => ModifierSpec.Status("thornkeeper.overgrowth.root", 2000 + 500 * l, ModifierFlags.Rooted))))));

            // Starborn — late scaler: star bolt, quickening, radiance, ascension.
            list.Add(Cast("starborn.star_bolt", "Star Bolt", AbilityTargeting.Unit, TargetTeam.Enemy, 650, 300, Basic, Cd(10000, 9000, 8000, 7000), Mana(80, 90, 100, 110),
                EffectDefinition.Damage(DamageType.Magical, 70, 110, 150, 190)));
            list.Add(Passive("starborn.quickening", "Quickening", Basic,
                Levels(4, l => new ModifierSpec("starborn.quickening", -1, attackSpeedBonus: 10 * l))));
            list.Add(Passive("starborn.radiance", "Radiance", Basic,
                Levels(4, l => new ModifierSpec("starborn.radiance", -1, damageBonus: 5 * l))));
            list.Add(Passive("starborn.ascension", "Ascension", Ultimate,
                Levels(2, l => new ModifierSpec("starborn.ascension", -1, critChancePercent: 15 + 10 * l, critMultiplierPercent: 220, damageBonus: 15 * l))));

            // Wards.
            list.Add(Cast("item.place_observer", "Place Observer Ward", AbilityTargeting.Point, TargetTeam.Ally, 500, 0, new[] { 1 }, new[] { 1000 }, new[] { 0 },
                EffectDefinition.SummonAtPoint("observer_ward", 360000, 1)));
            list.Add(Cast("item.place_sentry", "Place Sentry Ward", AbilityTargeting.Point, TargetTeam.Ally, 500, 0, new[] { 1 }, new[] { 1000 }, new[] { 0 },
                EffectDefinition.SummonAtPoint("sentry_ward", 300000, 1)));

            list.Add(Cast("item.smoke", "Smoke of Veiling", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, new[] { 1 }, new[] { 1000 }, new[] { 0 },
                EffectDefinition.Area(1200, AreaCenter.Caster, TargetTeam.Ally, heroesOnly: true,
                    EffectDefinition.Modifier(new ModifierSpec("smoke", 35000, ModifierFlags.Invisible, moveSpeedPercent: 15, breakOnAction: true, breakRadius: 1025)))));
            list.Add(Cast("item.cleanse", "Cleanse", AbilityTargeting.Unit, TargetTeam.Any, 700, 0, new[] { 1 }, new[] { 18000 }, new[] { 40 },
                EffectDefinition.Dispel(strong: false)));
            list.Add(new AbilityDefinition("item.teleport", "Teleport", AbilityTargeting.Unit, TargetTeam.Ally, 20000, 3000,
                new[] { 60000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Teleport() }, targetsStructures: true));

            // Item active.
            list.Add(Cast("item.arc_pulse", "Arc Pulse", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 1 }, new[] { 25000 }, new[] { 50 },
                EffectDefinition.Area(400, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Magical, 150))));
            return list;
        }

        private static IReadOnlyList<ItemDefinition> BuildItems() => new[]
        {
            new ItemDefinition("iron_blade", "Iron Blade", 450, new ModifierSpec("item.iron_blade", -1, damageBonus: 15)),
            new ItemDefinition("swift_gloves", "Swift Gloves", 500, new ModifierSpec("item.swift_gloves", -1, attackSpeedBonus: 25)),
            new ItemDefinition("warden_plate", "Warden Plate", 550, new ModifierSpec("item.warden_plate", -1, armorBonus: Fixed.FromInt(6))),
            new ItemDefinition("vital_orb", "Vital Orb", 450, new ModifierSpec("item.vital_orb", -1, maxHpBonus: 250)),
            new ItemDefinition("mana_crystal", "Mana Crystal", 400, new ModifierSpec("item.mana_crystal", -1, maxManaBonus: 200)),
            new ItemDefinition("swift_boots", "Swift Boots", 400, new ModifierSpec("item.swift_boots", -1, moveSpeedPercent: 15)),
            new ItemDefinition("tp_scroll", "Teleport Scroll", 90, activeAbilityId: "item.teleport", charges: 1),
            new ItemDefinition("smoke", "Smoke of Veiling", 80, activeAbilityId: "item.smoke", charges: 1),
            new ItemDefinition("observer_ward", "Observer Ward", 75, activeAbilityId: "item.place_observer", charges: 1),
            new ItemDefinition("sentry_ward", "Sentry Ward", 100, activeAbilityId: "item.place_sentry", charges: 1),
            new ItemDefinition("crest_of_valor", "Crest of Valor", 1600,
                new ModifierSpec("item.crest_of_valor", -1, damageBonus: 30, armorBonus: Fixed.FromInt(10)), new[] { "iron_blade", "warden_plate" }),
            new ItemDefinition("storm_edge", "Storm Edge", 2400,
                new ModifierSpec("item.storm_edge", -1, damageBonus: 35, attackSpeedBonus: 40, critChancePercent: 20, critMultiplierPercent: 170), new[] { "iron_blade", "swift_gloves" }),
            new ItemDefinition("aegis_heart", "Aegis Heart", 2600,
                new ModifierSpec("item.aegis_heart", -1, maxHpBonus: 500, armorBonus: Fixed.FromInt(8), hpRegenBonus: Fixed.FromInt(6)), new[] { "vital_orb", "warden_plate" }),
            new ItemDefinition("cleansing_charm", "Cleansing Charm", 1300,
                new ModifierSpec("item.cleansing_charm", -1, maxManaBonus: 150, magicResistBonus: 10), new[] { "mana_crystal" }, "item.cleanse"),
            new ItemDefinition("spellshard", "Spellshard", 2000,
                new ModifierSpec("item.spellshard", -1, maxManaBonus: 300, maxHpBonus: 250, magicResistBonus: 15), new[] { "mana_crystal", "vital_orb" }, "item.arc_pulse"),
        };

        private static IReadOnlyList<HeroDefinition> BuildHeroes()
        {
            string[] carry = { "swift_boots", "storm_edge", "crest_of_valor" };
            string[] caster = { "swift_boots", "spellshard", "aegis_heart" };
            string[] support = { "swift_boots", "cleansing_charm", "spellshard", "aegis_heart" };
            string[] tank = { "swift_boots", "aegis_heart", "crest_of_valor" };

            return new[]
            {
                Hero("ironvow", "Ironvow", new[] { "carry" }, melee: true, hp: 600, armor: 3, speed: 310, dmg: 52, hpPerLevel: 90, dmgPerLevel: 3, mana: 250, manaPerLevel: 25,
                    "ironvow.rending_strike", "ironvow.iron_will", "ironvow.bloodthirst", "ironvow.unbroken", carry),
                Hero("emberwisp", "Emberwisp", new[] { "mid", "nuker" }, melee: false, hp: 520, armor: 1, speed: 305, dmg: 46, hpPerLevel: 75, dmgPerLevel: 3, mana: 350, manaPerLevel: 45,
                    "emberwisp.fire_lance", "emberwisp.ember_field", "emberwisp.kindling", "emberwisp.inferno", caster),
                Hero("stonewarden", "Stonewarden", new[] { "offlane", "initiator" }, melee: true, hp: 650, armor: 4, speed: 300, dmg: 50, hpPerLevel: 100, dmgPerLevel: 3, mana: 280, manaPerLevel: 30,
                    "stonewarden.ram", "stonewarden.tremor", "stonewarden.stone_skin", "stonewarden.earthshatter", tank),
                Hero("frostweaver", "Frostweaver", new[] { "mid", "control" }, melee: false, hp: 500, armor: 1, speed: 295, dmg: 44, hpPerLevel: 70, dmgPerLevel: 2, mana: 380, manaPerLevel: 50,
                    "frostweaver.frost_shackle", "frostweaver.glacier", "frostweaver.frost_mantle", "frostweaver.deep_winter", caster),
                Hero("dawnmender", "Dawnmender", new[] { "support", "healer" }, melee: false, hp: 520, armor: 1, speed: 300, dmg: 42, hpPerLevel: 75, dmgPerLevel: 2, mana: 360, manaPerLevel: 45,
                    "dawnmender.mend", "dawnmender.sanctuary", "dawnmender.smite", "dawnmender.renewal", support),
                Hero("hivecaller", "Hivecaller", new[] { "offlane", "summoner" }, melee: false, hp: 540, armor: 2, speed: 300, dmg: 44, hpPerLevel: 80, dmgPerLevel: 2, mana: 330, manaPerLevel: 40,
                    "hivecaller.brood", "hivecaller.sting", "hivecaller.chitin", "hivecaller.swarm_lord", tank),
                Hero("shadeblade", "Shadeblade", new[] { "mid", "assassin" }, melee: true, hp: 540, armor: 2, speed: 320, dmg: 54, hpPerLevel: 80, dmgPerLevel: 3, mana: 270, manaPerLevel: 30,
                    "shadeblade.shadow_step", "shadeblade.rupture", "shadeblade.keen_edge", "shadeblade.death_mark", carry),
                Hero("siegewright", "Siegewright", new[] { "offlane", "pusher" }, melee: false, hp: 560, armor: 2, speed: 295, dmg: 48, hpPerLevel: 80, dmgPerLevel: 3, mana: 300, manaPerLevel: 35,
                    "siegewright.demolition", "siegewright.mortar", "siegewright.construct", "siegewright.bombardment", tank),
                Hero("thornkeeper", "Thornkeeper", new[] { "support" }, melee: false, hp: 530, armor: 2, speed: 300, dmg: 43, hpPerLevel: 75, dmgPerLevel: 2, mana: 340, manaPerLevel: 40,
                    "thornkeeper.bramble", "thornkeeper.thorn_ward", "thornkeeper.barkskin", "thornkeeper.overgrowth", support),
                Hero("starborn", "Starborn", new[] { "carry", "late" }, melee: false, hp: 480, armor: 1, speed: 300, dmg: 46, hpPerLevel: 70, dmgPerLevel: 5, mana: 280, manaPerLevel: 30,
                    "starborn.star_bolt", "starborn.quickening", "starborn.radiance", "starborn.ascension", carry),
            };
        }

        private static HeroDefinition Hero(string id, string name, string[] roles, bool melee, int hp, int armor, int speed, int dmg,
            int hpPerLevel, int dmgPerLevel, int mana, int manaPerLevel, string q, string w, string e, string r, string[] build)
        {
            UnitStats stats = melee
                ? Stats(hp, armor, speed, dmg, dmg + 6, range: 150, intervalMs: 1700, pointMs: 400, acquisition: 800, radius: 24, magicResist: 25, vision: 1200)
                : Stats(hp, armor, speed, dmg, dmg + 6, range: 575, intervalMs: 1700, pointMs: 450, acquisition: 800, radius: 24, magicResist: 25, vision: 1200);
            return new HeroDefinition(id, name, roles, stats, hpPerLevel, Fixed.FromRatio(3, 10), dmgPerLevel, mana, manaPerLevel,
                hpRegenPerSecond: Fixed.FromRatio(5, 2), manaRegenPerSecond: Fixed.FromRatio(3, 2),
                abilityIds: new[] { q, w, e, r }, itemBuild: build);
        }

        private static AbilityDefinition Cast(string id, string name, AbilityTargeting targeting, TargetTeam team, int range, int castPointMs,
            int[] requiredLevels, int[] cooldowns, int[] manaCosts, params EffectDefinition[] effects) =>
            new AbilityDefinition(id, name, targeting, team, range, castPointMs, cooldowns, manaCosts, requiredLevels, effects);

        private static AbilityDefinition Passive(string id, string name, int[] requiredLevels, ModifierSpec[] perLevel) =>
            new AbilityDefinition(id, name, AbilityTargeting.Passive, TargetTeam.Ally, 0, 0, new int[0], new int[0], requiredLevels,
                new EffectDefinition[0], perLevel);

        private static ModifierSpec[] Levels(int count, System.Func<int, ModifierSpec> perLevel) =>
            Enumerable.Range(1, count).Select(perLevel).ToArray();

        private static int[] Cd(params int[] values) => values;

        private static int[] Mana(params int[] values) => values;

        internal static UnitStats Stats(int hp, int armor, int speed, int dmgMin, int dmgMax, int range, int intervalMs, int pointMs, int acquisition, int radius,
            int magicResist = 0, int vision = 800, int trueSight = 0, ModifierFlags innate = ModifierFlags.None) =>
            new UnitStats(hp, Fixed.FromInt(armor), speed, dmgMin, dmgMax, range, intervalMs, pointMs, acquisition, radius, magicResist, vision, trueSight, innate);
    }
}
