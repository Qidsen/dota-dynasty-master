using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Amphibian Rhapsody (Dota 7.41f data and the official description; the project owner: the AI picks the song and is always on the beat):
/// in song mode Largo is disarmed and his songs work, each strum a stack of Groovin' (armor); out of it they do nothing. The AI strums
/// once a beat: Double Time when getting away, Good Vibrations for a hurt ally hero near, Fight Song otherwise.
/// </summary>
public class RhapsodyTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Mode = new ModifierSpec("rhapsody", -1, ModifierFlags.Disarmed, dispellable: false);

    private static readonly EffectDefinition Groove =
        EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("groovin", 5000, maxStacks: 5, armorBonus: Fixed.FromInt(2), dispellable: false)));

    private static AbilityDefinition Song(string id, params EffectDefinition[] effects) => new AbilityDefinition(id, id, AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 25,
        new[] { 200 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.IfCasterHas("rhapsody", effects.Concat(new[] { Groove }).ToArray()) });

    private static RulesetDraft Draft()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("rhapsody", "Amphibian Rhapsody", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 2000 }, new[] { 0 },
            new[] { 1 }, Array.Empty<EffectDefinition>(), toggle: new[] { Mode }));
        draft.Abilities.Add(Song("fight_song", EffectDefinition.Area(800, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 20))));
        draft.Abilities.Add(Song("double_time", EffectDefinition.Area(800, AreaCenter.Caster, TargetTeam.Ally,
            EffectDefinition.Modifier(new ModifierSpec("double_time", 1000, moveSpeedPercent: 20)))));
        draft.Abilities.Add(Song("good_vibrations", EffectDefinition.Area(800, AreaCenter.Caster, TargetTeam.Ally, true, EffectDefinition.Heal(50))));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "gamma", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 5000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "rhapsody", "fight_song", "double_time", "good_vibrations" } : null));
        return draft;
    }

    private static SimulationKernel<MatchState> Arena()
    {
        var kernel = Draft().StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "gamma").Position = P(5000, 5300);
            H(ctx.State, "beta").Position = P(5600, 5000);
            foreach (var ability in H(ctx.State, "alpha").Hero!.Abilities)
                ability.Level = 1;
        });
        return kernel;
    }

    [Theory]
    [InlineData(true, 20)]
    [InlineData(false, 0)]
    public void ASong_WorksOnlyInSongMode_AndGivesAStackOfGroovin(bool inMode, int damage)
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            if (inMode)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), Mode, H(ctx.State, "alpha").Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindAbility("fight_song")!.Effects, 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position));

        kernel.RunTicks(4);

        Assert.Equal(5000 - damage, H(kernel.State, "beta").Hp.ToDouble(), 0);
        Assert.Equal(inMode, H(kernel.State, "alpha").Modifiers.Any(m => m.Spec.Key == "groovin"));
    }

    /// <summary>In song mode the AI strums once a beat, picking the song for the moment.</summary>
    [Theory]
    [InlineData("fight", "fight_song")]
    [InlineData("ally_hurt", "good_vibrations")]
    [InlineData("self_hurt", "double_time")]
    public void Ai_StrumsOnTheBeat_TheSongForTheMoment(string moment, string song)
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), Mode, H(ctx.State, "alpha").Id);
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
            if (moment == "ally_hurt")
                H(ctx.State, "gamma").Hp = Fixed.FromInt(1000);
            if (moment == "self_hurt")
            {
                H(ctx.State, "alpha").Hp = Fixed.FromInt(600);
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), ModifierSpec.Status("net", -1, ModifierFlags.Rooted), H(ctx.State, "beta").Id);   // held near beta
            }
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 + 3 * 30);

        List<string> songs = kernel.EventsOf<AbilityCast>().Where(c => c.AbilityId != "rhapsody").Select(c => c.AbilityId).ToList();
        Assert.InRange(songs.Count, 2, 4);   // once a second
        Assert.All(songs, s => Assert.Equal(song, s));
    }
}
