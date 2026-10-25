using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// Original heroes, abilities, items and summons of the built-in ruleset: 24 heroes (5 carries,
    /// 6 mids, 7 offlaners, 6 supports) so a draft with bans leaves a real choice. Names and numbers are original.
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
            // Neutral camps after Dota's totals per camp (three small ≈ 57 gold / 1350 HP, three medium ≈ 81 / 1950,
            // two large ≈ 100 / 1900, two ancient ≈ 164 / 3500).
            new CreepType("jungle_small", Stats(hp: 450, armor: 1, speed: 320, dmgMin: 14, dmgMax: 16, range: 100, intervalMs: 1000, pointMs: 400, acquisition: 400, radius: 16), new Bounty(17, 21, 30)),
            new CreepType("jungle_medium", Stats(hp: 650, armor: 2, speed: 320, dmgMin: 24, dmgMax: 28, range: 100, intervalMs: 1100, pointMs: 400, acquisition: 400, radius: 20), new Bounty(25, 29, 40)),
            new CreepType("jungle_large", Stats(hp: 950, armor: 3, speed: 300, dmgMin: 38, dmgMax: 44, range: 120, intervalMs: 1300, pointMs: 400, acquisition: 400, radius: 24), new Bounty(47, 53, 70)),
            new CreepType("jungle_ancient", Stats(hp: 1750, armor: 5, speed: 300, dmgMin: 65, dmgMax: 75, range: 130, intervalMs: 1400, pointMs: 400, acquisition: 400, radius: 32), new Bounty(78, 86, 157)),
            new CreepType("spirit_wolf", Stats(hp: 320, armor: 1, speed: 360, dmgMin: 22, dmgMax: 26, range: 100, intervalMs: 1000, pointMs: 350, acquisition: 600, radius: 16), new Bounty(20, 26, 25)),
            new CreepType("wild_bear", Stats(hp: 1100, armor: 4, speed: 320, dmgMin: 40, dmgMax: 48, range: 120, intervalMs: 1300, pointMs: 400, acquisition: 600, radius: 28), new Bounty(40, 50, 60)),
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
                EffectDefinition.Projectile(1000, EffectDefinition.Damage(DamageType.Magical, 90, 150, 210, 270))));
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
                EffectDefinition.Projectile(1000,
                    EffectDefinition.Damage(DamageType.Magical, 60, 90, 120, 150),
                    EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("frostweaver.shackle", 1000 + 300 * l, ModifierFlags.Stunned))))));
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
                EffectDefinition.Projectile(1100,
                    EffectDefinition.Damage(DamageType.Magical, 60, 90, 120, 150),
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("hivecaller.sting.slow", 2000, moveSpeedPercent: -15 - 5 * l))))));
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
                EffectDefinition.Projectile(1100, EffectDefinition.Damage(DamageType.Magical, 70, 110, 150, 190))));
            list.Add(Passive("starborn.quickening", "Quickening", Basic,
                Levels(4, l => new ModifierSpec("starborn.quickening", -1, attackSpeedBonus: 10 * l))));
            list.Add(Passive("starborn.radiance", "Radiance", Basic,
                Levels(4, l => new ModifierSpec("starborn.radiance", -1, damageBonus: 5 * l))));
            list.Add(Passive("starborn.ascension", "Ascension", Ultimate,
                Levels(2, l => new ModifierSpec("starborn.ascension", -1, critChancePercent: 15 + 10 * l, critMultiplierPercent: 220, damageBonus: 15 * l))));

            // Duskrunner — agile carry: blink strike, fervor, bleed, frenzy.
            list.Add(Cast("duskrunner.blink_strike", "Blink Strike", AbilityTargeting.Unit, TargetTeam.Enemy, 700, 0, Basic, Cd(13000, 11000, 9000, 7000), Mana(60, 65, 70, 75),
                EffectDefinition.Dash(),
                EffectDefinition.Damage(DamageType.Physical, 60, 90, 120, 150)));
            list.Add(Passive("duskrunner.fervor", "Fervor", Basic,
                Levels(4, l => new ModifierSpec("duskrunner.fervor", -1, attackSpeedBonus: 8 + 8 * l))));
            list.Add(Passive("duskrunner.bleed", "Bleed", Basic,
                Levels(4, l => new ModifierSpec("duskrunner.bleed", -1, lifestealPercent: 4 + 4 * l, damageBonus: 3 * l))));
            list.Add(Cast("duskrunner.frenzy", "Frenzy", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, Ultimate, Cd(60000, 45000), Mana(100, 150),
                EffectDefinition.Modifier(Levels(2, l => new ModifierSpec("duskrunner.frenzy", 8000, attackSpeedBonus: 60 + 40 * l, moveSpeedPercent: 15)))));

            // Ashblade — brawler carry: whirl, tempered steel, cinder blood, ashen fury.
            list.Add(Cast("ashblade.whirl", "Whirl", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 200, Basic, Cd(11000, 10000, 9000, 8000), Mana(70, 80, 90, 100),
                EffectDefinition.Area(275, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Physical, 70, 110, 150, 190))));
            list.Add(Passive("ashblade.tempered_steel", "Tempered Steel", Basic,
                Levels(4, l => new ModifierSpec("ashblade.tempered_steel", -1, armorBonus: Fixed.FromInt(l + 1), maxHpBonus: 40 * l))));
            list.Add(Passive("ashblade.cinder_blood", "Cinder Blood", Basic,
                Levels(4, l => new ModifierSpec("ashblade.cinder_blood", -1, hpRegenBonus: Fixed.FromInt(l + 1), damageBonus: 4 * l))));
            list.Add(Cast("ashblade.ashen_fury", "Ashen Fury", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 300, Ultimate, Cd(70000, 55000), Mana(150, 200),
                EffectDefinition.Area(400, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Physical, 220, 340),
                    EffectDefinition.Modifier(Levels(2, l => new ModifierSpec("ashblade.ashen_fury.slow", 3000, moveSpeedPercent: -25 - 10 * l))))));

            // Gunwright — ranged late carry: scattershot, steady aim, deadeye, headshot.
            list.Add(Cast("gunwright.scattershot", "Scattershot", AbilityTargeting.Point, TargetTeam.Enemy, 750, 300, Basic, Cd(12000, 11000, 10000, 9000), Mana(80, 90, 100, 110),
                EffectDefinition.Area(250, AreaCenter.Point, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Physical, 60, 100, 140, 180))));
            list.Add(Passive("gunwright.steady_aim", "Steady Aim", Basic,
                Levels(4, l => new ModifierSpec("gunwright.steady_aim", -1, attackSpeedBonus: 6 * l, damageBonus: 4 * l))));
            list.Add(Passive("gunwright.deadeye", "Deadeye", Basic,
                Levels(4, l => new ModifierSpec("gunwright.deadeye", -1, critChancePercent: 8 + 4 * l, critMultiplierPercent: 190))));
            list.Add(Cast("gunwright.headshot", "Headshot", AbilityTargeting.Unit, TargetTeam.Enemy, 1200, 800, Ultimate, Cd(60000, 45000), Mana(150, 225),
                EffectDefinition.Projectile(1600, EffectDefinition.Damage(DamageType.Physical, 350, 550))));

            // Stormcaller — mid nuker: arc bolt (silence), static field, conductive, thunderstorm.
            list.Add(Cast("stormcaller.arc_bolt", "Arc Bolt", AbilityTargeting.Unit, TargetTeam.Enemy, 650, 300, Basic, Cd(11000, 10000, 9000, 8000), Mana(90, 100, 110, 120),
                EffectDefinition.Projectile(1200,
                    EffectDefinition.Damage(DamageType.Magical, 80, 120, 160, 200),
                    EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("stormcaller.arc_bolt.silence", 800 + 300 * l, ModifierFlags.Silenced))))));
            list.Add(Cast("stormcaller.static_field", "Static Field", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 300, Basic, Cd(13000, 12000, 11000, 10000), Mana(100, 110, 120, 130),
                EffectDefinition.Area(350, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Magical, 70, 110, 150, 190))));
            list.Add(Passive("stormcaller.conductive", "Conductive", Basic,
                Levels(4, l => new ModifierSpec("stormcaller.conductive", -1, magicResistBonus: 4 * l, maxManaBonus: 40 * l))));
            list.Add(Cast("stormcaller.thunderstorm", "Thunderstorm", AbilityTargeting.Point, TargetTeam.Enemy, 900, 500, Ultimate, Cd(75000, 60000), Mana(200, 300),
                EffectDefinition.Area(450, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 260, 400),
                    EffectDefinition.Modifier(Levels(2, l => new ModifierSpec("stormcaller.thunderstorm.slow", 3000, moveSpeedPercent: -30))))));

            // Voidseer — mid control: null field (silence), mind spike, void mantle, collapse.
            list.Add(Cast("voidseer.null_field", "Null Field", AbilityTargeting.Point, TargetTeam.Enemy, 700, 300, Basic, Cd(15000, 14000, 13000, 12000), Mana(100, 110, 120, 130),
                EffectDefinition.Area(275, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("voidseer.null_field.silence", 1500 + 500 * l, ModifierFlags.Silenced))))));
            list.Add(Cast("voidseer.mind_spike", "Mind Spike", AbilityTargeting.Unit, TargetTeam.Enemy, 650, 300, Basic, Cd(9000, 8000, 7000, 6000), Mana(80, 95, 110, 125),
                EffectDefinition.Damage(DamageType.Magical, 80, 130, 180, 230),
                EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("voidseer.mind_spike.slow", 2000, moveSpeedPercent: -20)))));
            list.Add(Passive("voidseer.void_mantle", "Void Mantle", Basic,
                Levels(4, l => new ModifierSpec("voidseer.void_mantle", -1, magicResistBonus: 5 * l, maxManaBonus: 60 * l))));
            list.Add(Cast("voidseer.collapse", "Collapse", AbilityTargeting.Point, TargetTeam.Enemy, 800, 400, Ultimate, Cd(80000, 65000), Mana(200, 275),
                EffectDefinition.Area(400, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 180, 280),
                    EffectDefinition.Modifier(Levels(2, l => ModifierSpec.Status("voidseer.collapse.root", 2000 + 500 * l, ModifierFlags.Rooted))))));

            // Gloomfang — mid assassin: pounce (root), gloom claws, feral instinct, devour.
            list.Add(Cast("gloomfang.pounce", "Pounce", AbilityTargeting.Unit, TargetTeam.Enemy, 650, 0, Basic, Cd(14000, 12000, 10000, 8000), Mana(70, 75, 80, 85),
                EffectDefinition.Dash(),
                EffectDefinition.Damage(DamageType.Physical, 50, 80, 110, 140),
                EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("gloomfang.pounce.root", 600 + 200 * l, ModifierFlags.Rooted)))));
            list.Add(Passive("gloomfang.gloom_claws", "Gloom Claws", Basic,
                Levels(4, l => new ModifierSpec("gloomfang.gloom_claws", -1, damageBonus: 6 * l))));
            list.Add(Passive("gloomfang.feral_instinct", "Feral Instinct", Basic,
                Levels(4, l => new ModifierSpec("gloomfang.feral_instinct", -1, moveSpeedPercent: 3 * l, critChancePercent: 5 + 3 * l, critMultiplierPercent: 175))));
            list.Add(Cast("gloomfang.devour", "Devour", AbilityTargeting.Unit, TargetTeam.Enemy, 250, 200, Ultimate, Cd(55000, 40000), Mana(125, 175),
                EffectDefinition.Damage(DamageType.Pure, 220, 360),
                EffectDefinition.Modifier(Levels(2, l => ModifierSpec.Status("gloomfang.devour.stun", 1000 + 500 * l, ModifierFlags.Stunned)))));

            // Tidebreaker — offlane initiator: undertow, anchor slam, barnacle hide, maelstrom.
            list.Add(Cast("tidebreaker.undertow", "Undertow", AbilityTargeting.Point, TargetTeam.Enemy, 650, 300, Basic, Cd(12000, 11000, 10000, 9000), Mana(90, 100, 110, 120),
                EffectDefinition.Area(300, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 60, 90, 120, 150),
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("tidebreaker.undertow.slow", 2500, moveSpeedPercent: -25 - 5 * l))))));
            list.Add(Cast("tidebreaker.anchor_slam", "Anchor Slam", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 300, Basic, Cd(14000, 13000, 12000, 11000), Mana(80, 90, 100, 110),
                EffectDefinition.Area(250, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Physical, 60, 90, 120, 150),
                    EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("tidebreaker.anchor_slam.stun", 600 + 200 * l, ModifierFlags.Stunned))))));
            list.Add(Passive("tidebreaker.barnacle_hide", "Barnacle Hide", Basic,
                Levels(4, l => new ModifierSpec("tidebreaker.barnacle_hide", -1, armorBonus: Fixed.FromInt(l), hpRegenBonus: Fixed.FromInt(l + 1)))));
            list.Add(Cast("tidebreaker.maelstrom", "Maelstrom", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 400, Ultimate, Cd(85000, 70000), Mana(175, 250),
                EffectDefinition.Area(550, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 150, 250),
                    EffectDefinition.Modifier(Levels(2, l => ModifierSpec.Status("tidebreaker.maelstrom.stun", 1200 + 400 * l, ModifierFlags.Stunned))))));

            // Bulwark — offlane tank: shield bash, fortify, iron hide, last stand.
            list.Add(Cast("bulwark.shield_bash", "Shield Bash", AbilityTargeting.Unit, TargetTeam.Enemy, 200, 200, Basic, Cd(12000, 11000, 10000, 9000), Mana(70, 80, 90, 100),
                EffectDefinition.Damage(DamageType.Physical, 70, 100, 130, 160),
                EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("bulwark.shield_bash.stun", 1000 + 250 * l, ModifierFlags.Stunned)))));
            list.Add(Cast("bulwark.fortify", "Fortify", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, Basic, Cd(18000, 16000, 14000, 12000), Mana(60, 70, 80, 90),
                EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("bulwark.fortify", 6000, armorBonus: Fixed.FromInt(4 + 3 * l), magicResistBonus: 10 + 5 * l)))));
            list.Add(Passive("bulwark.iron_hide", "Iron Hide", Basic,
                Levels(4, l => new ModifierSpec("bulwark.iron_hide", -1, maxHpBonus: 75 * l))));
            list.Add(Cast("bulwark.last_stand", "Last Stand", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 300, Ultimate, Cd(90000, 70000), Mana(150, 225),
                EffectDefinition.Area(700, AreaCenter.Caster, TargetTeam.Ally,
                    EffectDefinition.Heal(150, 250),
                    EffectDefinition.Modifier(Levels(2, l => new ModifierSpec("bulwark.last_stand", 6000, armorBonus: Fixed.FromInt(5 + 5 * l)))))));

            // Plaguebringer — offlane attrition: miasma, contagion, rot hide, pandemic.
            list.Add(Cast("plaguebringer.miasma", "Miasma", AbilityTargeting.Point, TargetTeam.Enemy, 700, 300, Basic, Cd(13000, 12000, 11000, 10000), Mana(90, 100, 110, 120),
                EffectDefinition.Area(325, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("plaguebringer.miasma", 5000, damagePerSecond: Fixed.FromInt(10 + 10 * l)))))));
            list.Add(Cast("plaguebringer.contagion", "Contagion", AbilityTargeting.Unit, TargetTeam.Enemy, 600, 300, Basic, Cd(11000, 10000, 9000, 8000), Mana(80, 90, 100, 110),
                EffectDefinition.Projectile(900,
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("plaguebringer.contagion", 4000, damagePerSecond: Fixed.FromInt(15 + 10 * l), moveSpeedPercent: -15 - 5 * l))))));
            list.Add(Passive("plaguebringer.rot_hide", "Rot Hide", Basic,
                Levels(4, l => new ModifierSpec("plaguebringer.rot_hide", -1, hpRegenBonus: Fixed.FromInt(2 * l), maxHpBonus: 30 * l))));
            list.Add(Cast("plaguebringer.pandemic", "Pandemic", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 400, Ultimate, Cd(75000, 60000), Mana(175, 250),
                EffectDefinition.Area(650, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Modifier(Levels(2, l => new ModifierSpec("plaguebringer.pandemic", 6000, damagePerSecond: Fixed.FromInt(40 + 20 * l), moveSpeedPercent: -20))))));

            // Wildheart — offlane beastmaster: call bear, howl, thick fur, stampede.
            list.Add(Cast("wildheart.call_bear", "Call Bear", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 400, Basic, Cd(35000, 32000, 29000, 26000), Mana(110, 120, 130, 140),
                EffectDefinition.Summon("wild_bear", 40000, 1, 1, 1, 2)));
            list.Add(Cast("wildheart.howl", "Howl", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, Basic, Cd(20000, 18000, 16000, 14000), Mana(60, 70, 80, 90),
                EffectDefinition.Area(800, AreaCenter.Caster, TargetTeam.Ally,
                    EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("wildheart.howl", 8000, damageBonus: 6 + 6 * l, moveSpeedPercent: 5 + 3 * l))))));
            list.Add(Passive("wildheart.thick_fur", "Thick Fur", Basic,
                Levels(4, l => new ModifierSpec("wildheart.thick_fur", -1, armorBonus: Fixed.FromInt(l + 1)))));
            list.Add(Cast("wildheart.stampede", "Stampede", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 400, Ultimate, Cd(80000, 65000), Mana(175, 250),
                EffectDefinition.Summon("spirit_wolf", 30000, 4, 6)));

            // Windsinger — support: gust (knockback), tailwind, zephyr shield, cyclone.
            list.Add(Cast("windsinger.gust", "Gust", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 200, Basic, Cd(14000, 13000, 12000, 11000), Mana(80, 90, 100, 110),
                EffectDefinition.Area(350, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Knockback(250, 300, 350, 400),
                    EffectDefinition.Damage(DamageType.Magical, 40, 70, 100, 130))));
            list.Add(Cast("windsinger.tailwind", "Tailwind", AbilityTargeting.Unit, TargetTeam.Ally, 700, 0, Basic, Cd(16000, 15000, 14000, 13000), Mana(60, 70, 80, 90),
                EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("windsinger.tailwind", 5000, moveSpeedPercent: 15 + 5 * l, attackSpeedBonus: 10 * l)))));
            list.Add(Cast("windsinger.zephyr_shield", "Zephyr Shield", AbilityTargeting.Unit, TargetTeam.Ally, 650, 0, Basic, Cd(15000, 14000, 13000, 12000), Mana(70, 80, 90, 100),
                EffectDefinition.Heal(60, 100, 140, 180),
                EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("windsinger.zephyr_shield", 5000, armorBonus: Fixed.FromInt(2 + 2 * l), magicResistBonus: 5 * l)))));
            list.Add(Cast("windsinger.cyclone", "Cyclone", AbilityTargeting.Point, TargetTeam.Enemy, 800, 400, Ultimate, Cd(80000, 65000), Mana(175, 250),
                EffectDefinition.Area(400, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 120, 200),
                    EffectDefinition.Modifier(Levels(2, l => ModifierSpec.Status("windsinger.cyclone.stun", 1500 + 500 * l, ModifierFlags.Stunned))))));

            // Lumenpriest — healer: radiant touch, purify (strong dispel), judgement, halo.
            list.Add(Cast("lumenpriest.radiant_touch", "Radiant Touch", AbilityTargeting.Unit, TargetTeam.Ally, 650, 300, Basic, Cd(9000, 8000, 7000, 6000), Mana(80, 90, 100, 110),
                EffectDefinition.Heal(100, 150, 200, 250)));
            list.Add(Cast("lumenpriest.purify", "Purify", AbilityTargeting.Unit, TargetTeam.Ally, 650, 0, Basic, Cd(22000, 20000, 18000, 16000), Mana(70, 80, 90, 100),
                EffectDefinition.Dispel(strong: true),
                EffectDefinition.Heal(50, 75, 100, 125)));
            list.Add(Cast("lumenpriest.judgement", "Judgement", AbilityTargeting.Unit, TargetTeam.Enemy, 600, 300, Basic, Cd(13000, 12000, 11000, 10000), Mana(80, 90, 100, 110),
                EffectDefinition.Projectile(1000,
                    EffectDefinition.Damage(DamageType.Magical, 70, 100, 130, 160),
                    EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("lumenpriest.judgement.silence", 1000 + 400 * l, ModifierFlags.Silenced))))));
            list.Add(Cast("lumenpriest.halo", "Halo", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 300, Ultimate, Cd(85000, 70000), Mana(200, 300),
                EffectDefinition.Area(800, AreaCenter.Caster, TargetTeam.Ally,
                    EffectDefinition.Heal(200, 300),
                    EffectDefinition.Modifier(Levels(2, l => new ModifierSpec("lumenpriest.halo", 8000, healPerSecond: Fixed.FromInt(20 + 10 * l)))))));

            // Chainwarden — support disabler: chain hook (root), iron net, warden's resolve, lockdown.
            list.Add(Cast("chainwarden.chain_hook", "Chain Hook", AbilityTargeting.Unit, TargetTeam.Enemy, 750, 300, Basic, Cd(14000, 13000, 12000, 11000), Mana(90, 100, 110, 120),
                EffectDefinition.Projectile(1100,
                    EffectDefinition.Damage(DamageType.Physical, 60, 90, 120, 150),
                    EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("chainwarden.chain_hook.root", 1200 + 300 * l, ModifierFlags.Rooted))))));
            list.Add(Cast("chainwarden.iron_net", "Iron Net", AbilityTargeting.Point, TargetTeam.Enemy, 650, 300, Basic, Cd(16000, 15000, 14000, 13000), Mana(90, 100, 110, 120),
                EffectDefinition.Area(250, AreaCenter.Point, TargetTeam.Enemy,
                    EffectDefinition.Modifier(Levels(4, l => ModifierSpec.Status("chainwarden.iron_net.root", 1000 + 250 * l, ModifierFlags.Rooted))))));
            list.Add(Passive("chainwarden.resolve", "Warden's Resolve", Basic,
                Levels(4, l => new ModifierSpec("chainwarden.resolve", -1, armorBonus: Fixed.FromInt(l), maxHpBonus: 40 * l))));
            list.Add(Cast("chainwarden.lockdown", "Lockdown", AbilityTargeting.Unit, TargetTeam.Enemy, 500, 300, Ultimate, Cd(70000, 55000), Mana(150, 225),
                EffectDefinition.Damage(DamageType.Magical, 150, 250),
                EffectDefinition.Modifier(Levels(2, l => ModifierSpec.Status("chainwarden.lockdown.stun", 2500 + 750 * l, ModifierFlags.Stunned)))));

            // Spiritwarden — summoner support: spirit wolves, pack bond, spirit sight, alpha call.
            list.Add(Cast("spiritwarden.spirit_wolves", "Spirit Wolves", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 300, Basic, Cd(25000, 23000, 21000, 19000), Mana(90, 100, 110, 120),
                EffectDefinition.Summon("spirit_wolf", 25000, 1, 2, 2, 3)));
            list.Add(Cast("spiritwarden.pack_bond", "Pack Bond", AbilityTargeting.Unit, TargetTeam.Ally, 650, 0, Basic, Cd(14000, 13000, 12000, 11000), Mana(70, 80, 90, 100),
                EffectDefinition.Heal(80, 120, 160, 200),
                EffectDefinition.Modifier(Levels(4, l => new ModifierSpec("spiritwarden.pack_bond", 5000, damageBonus: 5 + 5 * l)))));
            list.Add(Passive("spiritwarden.spirit_sight", "Spirit Sight", Basic,
                Levels(4, l => new ModifierSpec("spiritwarden.spirit_sight", -1, maxManaBonus: 40 * l, hpRegenBonus: Fixed.FromInt(l)))));
            list.Add(Cast("spiritwarden.alpha_call", "Alpha Call", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 400, Ultimate, Cd(75000, 60000), Mana(175, 250),
                EffectDefinition.Area(450, AreaCenter.Caster, TargetTeam.Enemy,
                    EffectDefinition.Damage(DamageType.Magical, 120, 200),
                    EffectDefinition.Modifier(Levels(2, l => new ModifierSpec("spiritwarden.alpha_call.slow", 3500, moveSpeedPercent: -40, attackSpeedBonus: -30 * l))))));

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

            // Healing consumable: health over time, not interrupted by damage (backlog B-024).
            list.Add(Cast("item.salve", "Healing Salve", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, new[] { 1 }, new[] { 1000 }, new[] { 0 },
                EffectDefinition.Modifier(new ModifierSpec("item.salve", 10000, healPerSecond: Fixed.FromInt(30)))));

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
            new ItemDefinition("healing_salve", "Healing Salve", 110, activeAbilityId: "item.salve", charges: 1),
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
                Hero("duskrunner", "Duskrunner", new[] { "carry" }, melee: true, hp: 560, armor: 2, speed: 320, dmg: 50, hpPerLevel: 80, dmgPerLevel: 3, mana: 240, manaPerLevel: 25,
                    "duskrunner.blink_strike", "duskrunner.fervor", "duskrunner.bleed", "duskrunner.frenzy", carry),
                Hero("ashblade", "Ashblade", new[] { "carry", "offlane" }, melee: true, hp: 620, armor: 3, speed: 305, dmg: 51, hpPerLevel: 95, dmgPerLevel: 3, mana: 260, manaPerLevel: 25,
                    "ashblade.whirl", "ashblade.tempered_steel", "ashblade.cinder_blood", "ashblade.ashen_fury", tank),
                Hero("gunwright", "Gunwright", new[] { "carry", "late" }, melee: false, hp: 470, armor: 1, speed: 295, dmg: 47, hpPerLevel: 70, dmgPerLevel: 4, mana: 270, manaPerLevel: 30,
                    "gunwright.scattershot", "gunwright.steady_aim", "gunwright.deadeye", "gunwright.headshot", carry),
                Hero("stormcaller", "Stormcaller", new[] { "mid", "nuker" }, melee: false, hp: 500, armor: 1, speed: 300, dmg: 45, hpPerLevel: 70, dmgPerLevel: 2, mana: 370, manaPerLevel: 50,
                    "stormcaller.arc_bolt", "stormcaller.static_field", "stormcaller.conductive", "stormcaller.thunderstorm", caster),
                Hero("voidseer", "Voidseer", new[] { "mid", "control" }, melee: false, hp: 510, armor: 1, speed: 295, dmg: 44, hpPerLevel: 70, dmgPerLevel: 2, mana: 380, manaPerLevel: 50,
                    "voidseer.null_field", "voidseer.mind_spike", "voidseer.void_mantle", "voidseer.collapse", caster),
                Hero("gloomfang", "Gloomfang", new[] { "mid", "assassin" }, melee: true, hp: 550, armor: 2, speed: 315, dmg: 53, hpPerLevel: 80, dmgPerLevel: 3, mana: 260, manaPerLevel: 30,
                    "gloomfang.pounce", "gloomfang.gloom_claws", "gloomfang.feral_instinct", "gloomfang.devour", carry),
                Hero("tidebreaker", "Tidebreaker", new[] { "offlane", "initiator" }, melee: true, hp: 660, armor: 3, speed: 295, dmg: 49, hpPerLevel: 100, dmgPerLevel: 3, mana: 290, manaPerLevel: 30,
                    "tidebreaker.undertow", "tidebreaker.anchor_slam", "tidebreaker.barnacle_hide", "tidebreaker.maelstrom", tank),
                Hero("bulwark", "Bulwark", new[] { "offlane", "support" }, melee: true, hp: 680, armor: 4, speed: 290, dmg: 47, hpPerLevel: 105, dmgPerLevel: 2, mana: 280, manaPerLevel: 30,
                    "bulwark.shield_bash", "bulwark.fortify", "bulwark.iron_hide", "bulwark.last_stand", tank),
                Hero("plaguebringer", "Plaguebringer", new[] { "offlane" }, melee: false, hp: 560, armor: 2, speed: 295, dmg: 45, hpPerLevel: 85, dmgPerLevel: 2, mana: 320, manaPerLevel: 40,
                    "plaguebringer.miasma", "plaguebringer.contagion", "plaguebringer.rot_hide", "plaguebringer.pandemic", tank),
                Hero("wildheart", "Wildheart", new[] { "offlane", "summoner", "pusher" }, melee: true, hp: 600, armor: 3, speed: 305, dmg: 50, hpPerLevel: 90, dmgPerLevel: 3, mana: 290, manaPerLevel: 30,
                    "wildheart.call_bear", "wildheart.howl", "wildheart.thick_fur", "wildheart.stampede", tank),
                Hero("windsinger", "Windsinger", new[] { "support" }, melee: false, hp: 500, armor: 1, speed: 310, dmg: 42, hpPerLevel: 70, dmgPerLevel: 2, mana: 350, manaPerLevel: 45,
                    "windsinger.gust", "windsinger.tailwind", "windsinger.zephyr_shield", "windsinger.cyclone", support),
                Hero("lumenpriest", "Lumenpriest", new[] { "support", "healer" }, melee: false, hp: 510, armor: 1, speed: 300, dmg: 41, hpPerLevel: 75, dmgPerLevel: 2, mana: 370, manaPerLevel: 45,
                    "lumenpriest.radiant_touch", "lumenpriest.purify", "lumenpriest.judgement", "lumenpriest.halo", support),
                Hero("chainwarden", "Chainwarden", new[] { "support", "initiator" }, melee: false, hp: 540, armor: 2, speed: 300, dmg: 44, hpPerLevel: 80, dmgPerLevel: 2, mana: 330, manaPerLevel: 40,
                    "chainwarden.chain_hook", "chainwarden.iron_net", "chainwarden.resolve", "chainwarden.lockdown", support),
                Hero("spiritwarden", "Spiritwarden", new[] { "support", "summoner" }, melee: false, hp: 520, armor: 1, speed: 305, dmg: 43, hpPerLevel: 75, dmgPerLevel: 2, mana: 340, manaPerLevel: 40,
                    "spiritwarden.spirit_wolves", "spiritwarden.pack_bond", "spiritwarden.spirit_sight", "spiritwarden.alpha_call", support),
            };
        }

        private static HeroDefinition Hero(string id, string name, string[] roles, bool melee, int hp, int armor, int speed, int dmg,
            int hpPerLevel, int dmgPerLevel, int mana, int manaPerLevel, string q, string w, string e, string r, string[] build)
        {
            UnitStats stats = melee
                ? Stats(hp, armor, speed, dmg, dmg + 6, range: 150, intervalMs: 1700, pointMs: 400, acquisition: 800, radius: 24, magicResist: 25, vision: 1200, nightVision: 800)
                : Stats(hp, armor, speed, dmg, dmg + 6, range: 575, intervalMs: 1700, pointMs: 450, acquisition: 800, radius: 24, magicResist: 25, vision: 1200, projectileSpeed: 900, nightVision: 800);
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
            int magicResist = 0, int vision = 800, int trueSight = 0, ModifierFlags innate = ModifierFlags.None, int projectileSpeed = 0, int nightVision = -1) =>
            new UnitStats(hp, Fixed.FromInt(armor), speed, dmgMin, dmgMax, range, intervalMs, pointMs, acquisition, radius, magicResist, vision, trueSight, innate, projectileSpeed, nightVision);
    }
}
