using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Content.Tests;

/// <summary>
/// Pack format v1 beyond heroes: abilities as the engine's effect tree, items with stat bonuses,
/// components and actives, and unit types for summons (wards). A pack with abilities or items replaces
/// the base ones; unit types are added to the base ones. Times are in seconds in the document.
/// </summary>
public class PackContentTests
{
    private static readonly string Fillers = string.Concat(Enumerable.Range(1, 18).Select(i => "," + Hero("filler" + i, "[]")));

    private static string Hero(string id, string abilities) => @"{
        ""id"": """ + id + @""", ""name"": """ + id + @""", ""roles"": [""support""], ""attackType"": ""ranged"",
        ""primaryAttribute"": ""intelligence"", ""strength"": 18, ""agility"": 15, ""intelligence"": 22,
        ""strengthGain"": 2.3, ""agilityGain"": 1.5, ""intelligenceGain"": 3.5,
        ""baseHealth"": 120, ""baseHealthRegen"": 0.5, ""baseMana"": 75, ""baseManaRegen"": 0, ""baseArmor"": 1, ""magicResist"": 25,
        ""attackDamageMin"": 29, ""attackDamageMax"": 35, ""attackRange"": 600, ""projectileSpeed"": 1000,
        ""baseAttackTime"": 1.7, ""baseAttackSpeed"": 100, ""attackPoint"": 0.43, ""moveSpeed"": 290, ""dayVision"": 1800, ""nightVision"": 800,
        ""abilities"": " + abilities + @" }";

    private const string Spike = @"{
        ""id"": ""lion_impale"", ""name"": ""Earth Spike"", ""targeting"": ""unit"", ""targetTeam"": ""enemy"", ""castRange"": 650, ""castPoint"": 0.3,
        ""cooldown"": [14, 13, 12, 11], ""manaCost"": [90, 110, 130, 150], ""requiredLevels"": [1, 3, 5, 7],
        ""effects"": [ { ""kind"": ""projectile"", ""speed"": 1600, ""effects"": [
            { ""kind"": ""damage"", ""damageType"": ""magical"", ""amounts"": [105, 170, 235, 300] },
            { ""kind"": ""modifier"", ""modifiers"": [
                { ""key"": ""lion_impale"", ""duration"": 1.3, ""flags"": [""stunned""] },
                { ""key"": ""lion_impale"", ""duration"": 1.6, ""flags"": [""stunned""] },
                { ""key"": ""lion_impale"", ""duration"": 1.9, ""flags"": [""stunned""] },
                { ""key"": ""lion_impale"", ""duration"": 2.2, ""flags"": [""stunned""] } ] } ] } ] }";

    private const string WardPlace = @"{
        ""id"": ""item_ward_observer"", ""name"": ""Observer Ward"", ""targeting"": ""point"", ""targetTeam"": ""any"", ""castRange"": 500, ""castPoint"": 0,
        ""cooldown"": [1], ""requiredLevels"": [1],
        ""effects"": [ { ""kind"": ""summon"", ""unitType"": ""ward_observer"", ""duration"": 360, ""counts"": [1], ""atPoint"": true } ] }";

    private const string Items = @"[
        { ""id"": ""belt_of_strength"", ""name"": ""Belt of Strength"", ""cost"": 450, ""bonuses"": { ""key"": ""item_belt_of_strength"", ""strength"": 6 } },
        { ""id"": ""boots"", ""name"": ""Boots of Speed"", ""cost"": 500, ""bonuses"": { ""key"": ""item_boots"", ""moveSpeed"": 45 } },
        { ""id"": ""gloves"", ""name"": ""Gloves of Haste"", ""cost"": 450, ""bonuses"": { ""key"": ""item_gloves"", ""attackSpeed"": 20 } },
        { ""id"": ""power_treads"", ""name"": ""Power Treads"", ""cost"": 1400, ""components"": [""boots"", ""gloves"", ""belt_of_strength""],
          ""bonuses"": { ""key"": ""item_power_treads"", ""moveSpeed"": 45, ""attackSpeed"": 25, ""strength"": 10 } },
        { ""id"": ""ward_observer"", ""name"": ""Observer Ward"", ""cost"": 0, ""active"": ""item_ward_observer"", ""charges"": 1 } ]";

    private const string Units = @"[ { ""id"": ""ward_observer"", ""health"": 1, ""armor"": 0, ""moveSpeed"": 0, ""visionRange"": 1600, ""invisible"": true,
        ""bounty"": { ""goldMin"": 50, ""goldMax"": 50, ""xp"": 50 } } ]";

    private static RulesetContent Apply(string extra, string heroes = "") => RulesetPack.Apply(BaseRuleset.Create(), @"{
        ""format"": ""dynasty.ruleset-pack"", ""formatVersion"": 1, ""id"": ""dota"", ""version"": 1,
        ""heroes"": [" + (heroes.Length > 0 ? heroes : Hero("lion", @"[""lion_impale""]")) + Fillers + "], " + extra + " }");

    private static RulesetContent Full() => Apply(@"""unitTypes"": " + Units + @", ""abilities"": [" + Spike + "," + WardPlace + @"], ""items"": " + Items);

    [Fact]
    public void Ability_ReadsTheEffectTreeWithValuesPerLevel()
    {
        AbilityDefinition spike = Full().FindAbility("lion_impale")!;

        Assert.Equal((AbilityTargeting.Unit, TargetTeam.Enemy, 650, 300), (spike.Targeting, spike.TargetTeam, spike.CastRange, spike.CastPointMs));
        Assert.Equal(new[] { 14000, 13000, 12000, 11000 }, spike.CooldownMs);
        Assert.Equal(new[] { 90, 110, 130, 150 }, spike.ManaCost);
        Assert.Equal(new[] { 1, 3, 5, 7 }, spike.RequiredHeroLevel);
        EffectDefinition projectile = spike.Effects.Single();
        Assert.Equal((EffectKind.Projectile, 1600), (projectile.Kind, projectile.Speed));
        Assert.Equal(new[] { 105, 170, 235, 300 }, projectile.Inner[0].Amounts);
        Assert.Equal(DamageType.Magical, projectile.Inner[0].DamageType);
        Assert.Equal(new[] { 1300, 1600, 1900, 2200 }, projectile.Inner[1].Modifiers.Select(m => m.DurationMs));
        Assert.All(projectile.Inner[1].Modifiers, m => Assert.Equal(ModifierFlags.Stunned, m.Flags));
    }

    [Fact]
    public void Items_ReadBonusesComponentsAndActives_AndReplaceTheBaseItems()
    {
        RulesetContent rules = Full();

        Assert.Equal(new[] { "belt_of_strength", "boots", "gloves", "power_treads", "ward_observer" }, rules.Items.Select(i => i.Id));
        ItemDefinition treads = rules.FindItem("power_treads")!;
        Assert.Equal(1400, treads.Cost);
        Assert.Equal(new[] { "boots", "gloves", "belt_of_strength" }, treads.Components);
        Assert.Equal((45, 25, 10), (treads.Bonuses!.MoveSpeedBonus, treads.Bonuses.AttackSpeedBonus, treads.Bonuses.StrengthBonus));
        Assert.True(treads.Bonuses.IsPermanent);
        ItemDefinition ward = rules.FindItem("ward_observer")!;
        Assert.Equal(("item_ward_observer", 1), (ward.ActiveAbilityId, ward.Charges));
    }

    [Fact]
    public void UnitTypes_AreAddedToTheBase_ForSummons()
    {
        RulesetContent rules = Full();

        CreepType ward = rules.FindCreepType("ward_observer")!;
        Assert.Equal((1, 0, 1600), (ward.Stats.MaxHp, ward.Stats.MoveSpeed, ward.Stats.VisionRange));
        Assert.Equal(ModifierFlags.Invisible, ward.Stats.InnateFlags);
        Assert.Equal((50, 50), (ward.Bounty.GoldMax, ward.Bounty.Xp));
        Assert.NotNull(rules.FindCreepType("melee"));   // the base lane creeps stay
        EffectDefinition summon = rules.FindAbility("item_ward_observer")!.Effects.Single();
        Assert.Equal((EffectKind.Summon, "ward_observer", 360000, true), (summon.Kind, summon.SummonTypeId, summon.DurationMs, summon.AtPoint));
    }

    [Fact]
    public void Modifier_ReadsEveryKnownField()
    {
        RulesetContent rules = Apply(@"""abilities"": [{ ""id"": ""aura"", ""name"": ""Aura"", ""targeting"": ""passive"", ""targetTeam"": ""ally"", ""castRange"": 0, ""castPoint"": 0,
            ""cooldown"": [0], ""requiredLevels"": [1], ""effects"": [], ""passive"": [{ ""key"": ""aura"", ""armor"": 2.5, ""moveSpeedPercent"": -20, ""attackSpeed"": 30, ""damage"": 12,
            ""magicResist"": 15, ""damagePerSecond"": 40, ""damageType"": ""pure"", ""healPerSecond"": 3, ""period"": 0.5, ""critChance"": 30, ""critMultiplier"": 160,
            ""structureDamage"": 40, ""lifesteal"": 15, ""maxHealth"": 250, ""maxMana"": 150, ""healthRegen"": 1.5, ""manaRegen"": 2.75,
            ""strength"": 5, ""agility"": 6, ""intelligence"": 7, ""moveSpeed"": 20, ""attackRange"": 150, ""flags"": [""silenced"", ""rooted""],
            ""breakOnAction"": true, ""breakRadius"": 1000, ""dispellable"": false }] }], ""items"": []", Hero("lion", @"[""aura""]"));

        ModifierSpec m = rules.FindAbility("aura")!.PassiveModifiers.Single();

        Assert.True(m.IsPermanent);
        Assert.Equal(Fixed.FromRatio(25, 10), m.ArmorBonus);
        Assert.Equal((-20, 30, 12, 15), (m.MoveSpeedPercent, m.AttackSpeedBonus, m.DamageBonus, m.MagicResistBonus));
        Assert.Equal((Fixed.FromInt(40), DamageType.Pure, Fixed.FromInt(3), 500), (m.DamagePerSecond, m.DamageType, m.HealPerSecond, m.PeriodMs));
        Assert.Equal((30, 160, 40, 15, 250, 150), (m.CritChancePercent, m.CritMultiplierPercent, m.StructureDamagePercent, m.LifestealPercent, m.MaxHpBonus, m.MaxManaBonus));
        Assert.Equal((Fixed.FromRatio(15, 10), Fixed.FromRatio(275, 100)), (m.HpRegenBonus, m.ManaRegenBonus));
        Assert.Equal((5, 6, 7, 20, 150), (m.StrengthBonus, m.AgilityBonus, m.IntelligenceBonus, m.MoveSpeedBonus, m.AttackRangeBonus));
        Assert.Equal(ModifierFlags.Silenced | ModifierFlags.Rooted, m.Flags);
        Assert.Equal((true, 1000, false), (m.BreakOnAction, m.BreakRadius, m.Dispellable));
    }

    /// <summary>Dota mechanics in modifiers: damage percents, evasion, on-hit effects with a chance, auras, debuff immunity; innate abilities.</summary>
    [Fact]
    public void Modifier_ReadsDotaMechanics_AndInnateAbilities()
    {
        RulesetContent rules = Apply(@"""abilities"": [{ ""id"": ""bash"", ""name"": ""Bash"", ""targeting"": ""passive"", ""targetTeam"": ""ally"", ""castRange"": 0, ""castPoint"": 0,
            ""cooldown"": [0], ""requiredLevels"": [1], ""effects"": [], ""innate"": true, ""passive"": [{ ""key"": ""bash"", ""incomingDamagePercent"": -10, ""attackDamagePercent"": 15,
            ""evasion"": 20, ""onHitChance"": 25, ""onHit"": [ { ""kind"": ""damage"", ""damageType"": ""magical"", ""amounts"": [40] },
            { ""kind"": ""modifier"", ""modifiers"": [ { ""key"": ""bashed"", ""duration"": 1, ""flags"": [""stunned""] } ] } ],
            ""aura"": { ""radius"": 900, ""team"": ""ally"", ""heroesOnly"": true, ""modifier"": { ""key"": ""bash.aura"", ""duration"": 1, ""armor"": 3 } },
            ""cleavePercent"": 60, ""cleaveRadius"": 400, ""barrier"": 120,
            ""flags"": [""debuffImmune"", ""cannotDie""] }] }], ""items"": []", Hero("lion", @"[""bash""]"));

        AbilityDefinition bash = rules.FindAbility("bash")!;
        ModifierSpec m = bash.PassiveModifiers.Single();

        Assert.True(bash.Innate);
        Assert.Equal((-10, 15, 20, 25), (m.IncomingDamagePercent, m.AttackDamagePercent, m.EvasionPercent, m.OnHitChancePercent));
        Assert.Equal(new[] { EffectKind.Damage, EffectKind.ApplyModifier }, m.OnHit.Select(e => e.Kind));
        Assert.Equal((900, TargetTeam.Ally, true, 3), (m.Aura!.Radius, m.Aura.Team, m.Aura.HeroesOnly, m.Aura.Modifier.ArmorBonus.ToIntFloor()));
        Assert.Equal(ModifierFlags.DebuffImmune | ModifierFlags.CannotDie, m.Flags);
        Assert.Equal((60, 400, 120), (m.CleavePercent, m.CleaveRadius, m.Barrier));
    }

    /// <summary>Mana and percent damage: mana burn with a share of it as damage, damage scaled by the target's health or mana, damage over time in percent of max health.</summary>
    [Fact]
    public void Effects_ReadManaBurnAndScaledDamage()
    {
        RulesetContent rules = Apply(@"""abilities"": [{ ""id"": ""void"", ""name"": ""Void"", ""targeting"": ""unit"", ""targetTeam"": ""enemy"", ""castRange"": 600, ""castPoint"": 0,
            ""cooldown"": [70], ""requiredLevels"": [6], ""effects"": [
            { ""kind"": ""manaBurn"", ""damageType"": ""physical"", ""damagePercent"": 50, ""amounts"": [25, 30] },
            { ""kind"": ""damage"", ""damageType"": ""magical"", ""scaling"": ""targetMissingManaPercent"", ""amounts"": [80, 95] },
            { ""kind"": ""damage"", ""damageType"": ""magical"", ""scaling"": ""targetMissingHealthPercent"", ""amounts"": [70] },
            { ""kind"": ""damage"", ""damageType"": ""pure"", ""scaling"": ""targetMaxHealthPercent"", ""amounts"": [5] },
            { ""kind"": ""damage"", ""damageType"": ""magical"", ""scaling"": ""targetCurrentHealthPercent"", ""amounts"": [1.45, 3.45] },
            { ""kind"": ""damage"", ""damageType"": ""magical"", ""scaling"": ""targetMaxManaPercent"", ""amounts"": [25] },
            { ""kind"": ""modifier"", ""modifiers"": [ { ""key"": ""burn"", ""duration"": 3, ""damagePerSecondMaxHealthPercent"": 1.5, ""consumedOnHit"": true } ] } ] }], ""items"": []",
            Hero("lion", @"[""void""]"));

        IReadOnlyList<EffectDefinition> effects = rules.FindAbility("void")!.Effects;

        Assert.Equal((EffectKind.ManaBurn, DamageType.Physical, 50), (effects[0].Kind, effects[0].DamageType, effects[0].DamagePercent));
        Assert.Equal(new[] { 25, 30 }, effects[0].Amounts);
        Assert.Equal(new[] { DamageScaling.TargetMissingManaPercent, DamageScaling.TargetMissingHealthPercent, DamageScaling.TargetMaxHealthPercent, DamageScaling.TargetCurrentHealthPercent },
            effects.Skip(1).Take(4).Select(e => e.Scaling));
        Assert.Equal(new[] { 8000, 9500 }, effects[1].Amounts);   // scaled amounts: percents in the pack, hundredths of a percent in the ruleset
        Assert.Equal(new[] { 145, 345 }, effects[4].Amounts);
        Assert.Equal(DamageScaling.TargetMaxManaPercent, effects[5].Scaling);
        ModifierSpec burn = effects[6].Modifiers.Single();
        Assert.Equal((Fixed.FromRatio(15, 10), true), (burn.DamagePerSecondMaxHealthPercent, burn.ConsumedOnHit));
    }

    /// <summary>Mana restore, "after N attacks" on-hit effects, and effects on every Nth attack taken.</summary>
    [Fact]
    public void Effects_ReadManaRestoreAndAttackCounters()
    {
        RulesetContent rules = Apply(@"""abilities"": [{ ""id"": ""chakra"", ""name"": ""Chakra"", ""targeting"": ""unit"", ""targetTeam"": ""ally"", ""castRange"": 900, ""castPoint"": 0,
            ""cooldown"": [18], ""requiredLevels"": [1], ""effects"": [ { ""kind"": ""restoreMana"", ""amounts"": [105] } ],
            ""passive"": [ { ""key"": ""bash"", ""onHit"": [ { ""kind"": ""damage"", ""damageType"": ""physical"", ""amounts"": [35] } ], ""onHitAfterAttacks"": 3,
                          ""whenAttacked"": [ { ""kind"": ""damage"", ""damageType"": ""pure"", ""amounts"": [100] } ], ""whenAttackedEvery"": 7 } ] }], ""items"": []",
            Hero("lion", @"[""chakra""]"));

        AbilityDefinition chakra = rules.FindAbility("chakra")!;
        Assert.Equal((EffectKind.RestoreMana, 105), (chakra.Effects[0].Kind, chakra.Effects[0].Amounts[0]));
        ModifierSpec bash = chakra.PassiveModifiers.Single();
        Assert.Equal((3, 7, DamageType.Pure), (bash.OnHitAfterAttacks, bash.WhenAttackedEvery, bash.WhenAttacked.Single().DamageType));
    }

    [Fact]
    public void Effects_ReadStacksAttacksCasterEffectsAndProcLimits()
    {
        RulesetContent rules = Apply(@"""abilities"": [{ ""id"": ""swipes"", ""name"": ""Swipes"", ""targeting"": ""unit"", ""targetTeam"": ""enemy"", ""castRange"": 900, ""castPoint"": 0,
            ""cooldown"": [18], ""requiredLevels"": [1], ""effects"": [
                { ""kind"": ""attack"", ""amounts"": [150] }, { ""kind"": ""attack"" },
                { ""kind"": ""ifHero"", ""effects"": [ { ""kind"": ""execute"", ""amounts"": [250] } ] },
                { ""kind"": ""onCaster"", ""effects"": [ { ""kind"": ""heal"", ""amounts"": [50] } ] },
                { ""kind"": ""damage"", ""damageType"": ""physical"", ""scaling"": ""perTargetStack"", ""stackKey"": ""fury"", ""amounts"": [15] },
                { ""kind"": ""damage"", ""damageType"": ""magical"", ""scaling"": ""casterIntelligencePercent"", ""amounts"": [37.5] } ],
            ""passive"": [ { ""key"": ""fury"", ""duration"": 20, ""stacking"": true, ""procCooldown"": 1.5, ""whenAttackedChance"": 25,
                          ""whenAttacked"": [ { ""kind"": ""damage"", ""damageType"": ""pure"", ""amounts"": [100] } ] },
                          { ""key"": ""shift"", ""stacking"": true, ""maxStacks"": 4, ""onHitHeroesOnly"": true, ""onKillHeroesOnly"": true, ""revives"": true, ""attackCharges"": 4, ""extraTargetRadiusFromAttackRange"": true, ""extraTargetChance"": 35, ""flags"": [""physicalImmune"", ""feared"", ""taunted""],
                            ""extraTargets"": 2, ""extraTargetRadius"": 700, ""extraTargetDamagePercent"": 60,
                            ""onCast"": [ { ""kind"": ""heal"", ""amounts"": [1] } ], ""onKill"": [ { ""kind"": ""heal"", ""amounts"": [2] } ],
                            ""onDeath"": [ { ""kind"": ""heal"", ""amounts"": [3] } ] } ] }], ""items"": []",
            Hero("lion", @"[""swipes""]"));

        AbilityDefinition swipes = rules.FindAbility("swipes")!;
        Assert.Equal((EffectKind.Attack, 150), (swipes.Effects[0].Kind, swipes.Effects[0].Amounts[0]));
        Assert.Equal(100, swipes.Effects[1].Amounts.Single());   // without amounts: a normal attack
        Assert.Equal((EffectKind.IfHero, EffectKind.Execute, 250), (swipes.Effects[2].Kind, swipes.Effects[2].Inner[0].Kind, swipes.Effects[2].Inner[0].Amounts[0]));
        Assert.Equal((EffectKind.OnCaster, EffectKind.Heal), (swipes.Effects[3].Kind, swipes.Effects[3].Inner.Single().Kind));
        Assert.Equal((DamageScaling.PerTargetStack, "fury", 15), (swipes.Effects[4].Scaling, swipes.Effects[4].StackKey, swipes.Effects[4].Amounts[0]));
        Assert.Equal((DamageScaling.CasterIntelligencePercent, 3750), (swipes.Effects[5].Scaling, swipes.Effects[5].Amounts[0]));
        ModifierSpec fury = swipes.PassiveModifiers[0];
        Assert.Equal((int.MaxValue, 1500, 25), (fury.MaxStacks, fury.ProcCooldownMs, fury.WhenAttackedChancePercent));
        ModifierSpec shift = swipes.PassiveModifiers[1];
        Assert.Equal((4, true, true, ModifierFlags.PhysicalImmune | ModifierFlags.Feared | ModifierFlags.Taunted), (shift.MaxStacks, shift.OnHitHeroesOnly, shift.OnKillHeroesOnly, shift.Flags));
        Assert.Equal((2, 700, 60, true), (shift.ExtraTargets, shift.ExtraTargetRadius, shift.ExtraTargetDamagePercent, shift.Revives));
        Assert.Equal((4, true, 35), (shift.AttackCharges, shift.ExtraTargetRadiusFromAttackRange, shift.ExtraTargetChancePercent));
        Assert.Equal((1, 2, 3), (shift.OnCast.Single().Amounts[0], shift.OnKill.Single().Amounts[0], shift.OnDeath.Single().Amounts[0]));
    }

    [Fact]
    public void ExtraTargetDamage_CanBeGivenAsAReduction()
    {
        RulesetContent rules = Apply(@"""abilities"": [{ ""id"": ""glaive"", ""name"": ""Glaive"", ""targeting"": ""passive"", ""targetTeam"": ""enemy"", ""castRange"": 0, ""castPoint"": 0,
            ""cooldown"": [0], ""requiredLevels"": [1, 3], ""effects"": [],
            ""passive"": [ { ""key"": ""glaive"", ""extraTargets"": 4, ""extraTargetRadius"": 500, ""extraTargetDamageReduction"": 35 },
                          { ""key"": ""gunslinger"", ""extraTargetRadius"": 500, ""extraTargetChance"": 40,
                            ""moveSpeedPercent"": 2.5, ""critChance"": 33.33, ""evasion"": 9.5, ""onHitChance"": 14.25 } ] }], ""items"": []",
            Hero("lion", @"[""glaive""]"));

        Assert.Equal(65, rules.FindAbility("glaive")!.PassiveModifiers[0].ExtraTargetDamagePercent);
        ModifierSpec gunslinger = rules.FindAbility("glaive")!.PassiveModifiers[1];
        Assert.Equal((1, 40), (gunslinger.ExtraTargets, gunslinger.ExtraTargetChancePercent));   // a chance alone: one extra target
        Assert.Equal((250, 3333, 950, 1425, 10000), (gunslinger.MoveSpeedHundredths, gunslinger.CritChanceHundredths, gunslinger.EvasionHundredths,
            gunslinger.OnHitChanceHundredths, gunslinger.WhenAttackedChanceHundredths));   // decimals kept; chances default to 100%
    }

    [Fact]
    public void Effects_ReadZonesAndPulls()
    {
        RulesetContent rules = Apply(@"""abilities"": [{ ""id"": ""pyre"", ""name"": ""Pyre"", ""targeting"": ""point"", ""targetTeam"": ""enemy"", ""castRange"": 900, ""castPoint"": 0,
            ""cooldown"": [18], ""requiredLevels"": [1], ""charges"": [3], ""chargeRestore"": [35], ""effects"": [
                { ""kind"": ""zone"", ""radius"": 260, ""center"": ""point"", ""team"": ""enemy"", ""duration"": 10, ""period"": 0.5, ""heroesOnly"": true,
                  ""effects"": [ { ""kind"": ""damage"", ""damageType"": ""magical"", ""amounts"": [50] } ] },
                { ""kind"": ""area"", ""radius"": 400, ""center"": ""point"", ""team"": ""enemy"", ""effects"": [ { ""kind"": ""pull"", ""toward"": ""point"", ""amounts"": [300] } ] },
                { ""kind"": ""illusion"", ""counts"": [2], ""duration"": 8, ""modifiers"": [ { ""key"": ""image"", ""incomingDamagePercent"": 250, ""attackDamagePercent"": -84 } ] } ] }], ""items"": []",
            Hero("lion", @"[""pyre""]"));

        Assert.Equal((3, 35000), (rules.FindAbility("pyre")!.MaxChargesAt(1), rules.FindAbility("pyre")!.ChargeRestoreAt(1)));
        EffectDefinition zone = rules.FindAbility("pyre")!.Effects[0];
        Assert.Equal((EffectKind.Zone, 260, AreaCenter.Point, 10000, 500), (zone.Kind, zone.Radius, zone.Center, zone.DurationMs, zone.PeriodMs));
        Assert.True(zone.HeroesOnly);
        EffectDefinition pull = rules.FindAbility("pyre")!.Effects[1].Inner.Single();
        Assert.Equal((EffectKind.Pull, AreaCenter.Point, 300), (pull.Kind, pull.Center, pull.Amounts[0]));
        EffectDefinition images = rules.FindAbility("pyre")!.Effects[2];
        Assert.Equal((EffectKind.Illusion, 2, 8000, 250, -84), (images.Kind, images.Amounts[0], images.DurationMs, images.Modifiers[0].IncomingDamagePercent, images.Modifiers[0].AttackDamagePercent));
    }

    [Theory]
    [InlineData(@"""abilities"": [{ ""id"": ""x"", ""name"": ""X"", ""targeting"": ""sideways"", ""targetTeam"": ""enemy"", ""castRange"": 0, ""castPoint"": 0, ""cooldown"": [1], ""requiredLevels"": [1], ""effects"": [] }]", "abilities[0].targeting")]
    [InlineData(@"""abilities"": [{ ""id"": ""x"", ""name"": ""X"", ""targeting"": ""noTarget"", ""targetTeam"": ""enemy"", ""castRange"": 0, ""castPoint"": 0, ""cooldown"": [1], ""requiredLevels"": [1], ""effects"": [{ ""kind"": ""teleport"", ""bogus"": 1 }] }]", "abilities[0].effects[0].bogus")]
    [InlineData(@"""abilities"": [{ ""id"": ""x"", ""name"": ""X"", ""targeting"": ""noTarget"", ""targetTeam"": ""enemy"", ""castRange"": 0, ""castPoint"": 0, ""cooldown"": [1], ""requiredLevels"": [1], ""effects"": [{ ""kind"": ""modifier"", ""modifiers"": [{ ""key"": ""m"", ""flags"": [""asleep""] }] }] }]", "abilities[0].effects[0].modifiers[0].flags")]
    [InlineData(@"""abilities"": [{ ""id"": ""x"", ""name"": ""X"", ""targeting"": ""noTarget"", ""targetTeam"": ""enemy"", ""castRange"": 0, ""castPoint"": 0, ""cooldown"": [1], ""requiredLevels"": [1], ""effects"": [{ ""kind"": ""zone"", ""radius"": 300, ""center"": ""caster"", ""team"": ""enemy"", ""duration"": 5, ""effects"": [{ ""kind"": ""damage"", ""damageType"": ""pure"", ""amounts"": [10] }] }] }]", "abilities[0].effects[0].effects")]
    public void Mistakes_AreReportedWithThePath(string extra, string path)
    {
        var error = Assert.Throws<RulesetPackException>(() => Apply(extra, Hero("lion", "[]")));

        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void UnknownReferences_AreRejected()
    {
        var error = Assert.Throws<RulesetPackException>(() => Apply(@"""abilities"": []", Hero("lion", @"[""lion_impale""]")));

        Assert.Contains("lion_impale", error.Message);
    }
}
