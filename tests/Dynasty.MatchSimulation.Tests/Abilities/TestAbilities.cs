using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>Small ability catalogue used by ability tests.</summary>
public static class TestAbilities
{
    private static readonly int[] FourLevels = { 1, 3, 5, 7 };

    public static AbilityDefinition Bolt { get; } = new("bolt", "Bolt", AbilityTargeting.Unit, TargetTeam.Enemy,
        castRange: 600, castPointMs: 300, cooldownMs: new[] { 8000, 7000, 6000, 5000 }, manaCost: new[] { 100, 110, 120, 130 },
        requiredHeroLevel: FourLevels, effects: new[] { EffectDefinition.Damage(DamageType.Magical, 100, 150, 200, 250) });

    public static AbilityDefinition Quake { get; } = new("quake", "Quake", AbilityTargeting.NoTarget, TargetTeam.Enemy,
        castRange: 0, castPointMs: 0, cooldownMs: new[] { 12000, 12000, 12000, 12000 }, manaCost: new[] { 120, 120, 120, 120 },
        requiredHeroLevel: FourLevels, effects: new[]
        {
            EffectDefinition.Area(300, AreaCenter.Caster, TargetTeam.Enemy,
                EffectDefinition.Damage(DamageType.Magical, 50),
                EffectDefinition.Modifier(ModifierSpec.Status("quake.stun", 1500, ModifierFlags.Stunned))),
        });

    public static AbilityDefinition Mend { get; } = new("mend", "Mend", AbilityTargeting.Unit, TargetTeam.Ally,
        castRange: 500, castPointMs: 0, cooldownMs: new[] { 6000, 6000, 6000, 6000 }, manaCost: new[] { 80, 80, 80, 80 },
        requiredHeroLevel: FourLevels, effects: new[] { EffectDefinition.Heal(200) });

    public static AbilityDefinition Leap { get; } = new("leap", "Leap", AbilityTargeting.Unit, TargetTeam.Enemy,
        castRange: 800, castPointMs: 0, cooldownMs: new[] { 10000, 10000, 10000, 10000 }, manaCost: new[] { 90, 90, 90, 90 },
        requiredHeroLevel: FourLevels, effects: new[] { EffectDefinition.Dash(), EffectDefinition.Damage(DamageType.Physical, 60) });

    public static AbilityDefinition Shove { get; } = new("shove", "Shove", AbilityTargeting.Unit, TargetTeam.Enemy,
        castRange: 300, castPointMs: 0, cooldownMs: new[] { 10000, 10000, 10000, 10000 }, manaCost: new[] { 50, 50, 50, 50 },
        requiredHeroLevel: FourLevels, effects: new[] { EffectDefinition.Knockback(400) });

    public static AbilityDefinition Swarm { get; } = new("swarm", "Swarm", AbilityTargeting.NoTarget, TargetTeam.Enemy,
        castRange: 0, castPointMs: 0, cooldownMs: new[] { 20000, 20000, 20000, 20000 }, manaCost: new[] { 100, 100, 100, 100 },
        requiredHeroLevel: FourLevels, effects: new[] { EffectDefinition.Summon("bug", 10000, 2) });

    public static AbilityDefinition Meteor { get; } = new("meteor", "Meteor", AbilityTargeting.Point, TargetTeam.Enemy,
        castRange: 800, castPointMs: 0, cooldownMs: new[] { 15000, 15000, 15000, 15000 }, manaCost: new[] { 150, 150, 150, 150 },
        requiredHeroLevel: FourLevels, effects: new[]
        {
            EffectDefinition.Area(250, AreaCenter.Point, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Magical, 300)),
        });

    public static AbilityDefinition Finale { get; } = new("finale", "Finale", AbilityTargeting.NoTarget, TargetTeam.Enemy,
        castRange: 0, castPointMs: 0, cooldownMs: new[] { 60000, 50000, 40000 }, manaCost: new[] { 200, 250, 300 },
        requiredHeroLevel: new[] { 6, 12, 18 }, effects: new[]
        {
            EffectDefinition.Area(500, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 400)),
        });

    public static AbilityDefinition Passive(string id, ModifierSpec spec) => new(id, id, AbilityTargeting.Passive, TargetTeam.Ally,
        castRange: 0, castPointMs: 0, cooldownMs: Array.Empty<int>(), manaCost: Array.Empty<int>(),
        requiredHeroLevel: new[] { 1 }, effects: Array.Empty<EffectDefinition>(), passiveModifiers: new[] { spec });

    public static AbilityDefinition Fury { get; } = Passive("fury", new ModifierSpec("fury", -1, critChancePercent: 100, critMultiplierPercent: 200));
    public static AbilityDefinition Frenzy { get; } = Passive("frenzy", new ModifierSpec("frenzy", -1, attackSpeedBonus: 100));
    public static AbilityDefinition Siegecraft { get; } = Passive("siegecraft", new ModifierSpec("siegecraft", -1, structureDamagePercent: 100));
    public static AbilityDefinition Leech { get; } = Passive("leech", new ModifierSpec("leech", -1, lifestealPercent: 50));

    public static IEnumerable<AbilityDefinition> All => new[] { Bolt, Quake, Mend, Leap, Shove, Swarm, Meteor, Finale, Fury, Frenzy, Siegecraft, Leech };

    /// <summary>A draft with every test ability, a "bug" summon type and extra heroes delta/epsilon.</summary>
    public static RulesetDraft Draft()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.AddRange(All);
        draft.Creeps.Add(new CreepType("bug", RulesetDraft.MeleeStats(hp: 200, armor: 0, dmgMin: 15, dmgMax: 15, speed: 350)));
        draft.Heroes.Add(RulesetDraft.Hero("delta"));
        draft.Heroes.Add(RulesetDraft.Hero("epsilon"));
        return draft;
    }

    public static RulesetDraft WithHeroAbilities(this RulesetDraft draft, string heroId, params string[] abilityIds)
    {
        int index = draft.Heroes.FindIndex(h => h.Id == heroId);
        draft.Heroes[index] = RulesetDraft.Hero(heroId, abilities: abilityIds);
        return draft;
    }

    public static FixedVector2 P(int x, int y) => FixedVector2.FromInts(x, y);
}
