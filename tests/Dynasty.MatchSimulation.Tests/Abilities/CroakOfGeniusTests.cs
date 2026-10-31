using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Croak of Genius (Dota 7.41f data and the official description): the hero's spells and items cost less mana, and a share of the damage
/// they deal reverberates on the target over the following 5 s; its attacks do not.
/// </summary>
public class CroakOfGeniusTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Croak = new ModifierSpec("croak", 20000, dispellable: false, manaCostReductionPercent: 15, echoPercent: 40, echoDurationMs: 5000);

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        setup?.Invoke(draft);
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "gamma", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? alphaAbilities : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "gamma").Position = P(4800, 5000);
            H(ctx.State, "beta").Position = P(5600, 5000);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, "beta").Id);
        });
        return kernel;
    }

    private static Fixed Lost(MatchState state) => Fixed.FromInt(5000) - H(state, "beta").Hp;

    [Fact]
    public void ASpellsDamage_Reverberates_OverFiveSeconds()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Croak, H(ctx.State, "gamma").Id));
        At(kernel, 2, ctx => Effects.RunSpell(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Damage(DamageType.Pure, 500) }, 1, H(ctx.State, "beta"), H(ctx.State, "beta").Position));
        Fixed afterHit = Fixed.Zero, midway = Fixed.Zero;
        At(kernel, 3, ctx => afterHit = Lost(ctx.State));
        At(kernel, 2 + 75, ctx => midway = Lost(ctx.State));

        kernel.RunTicks(2 + 6 * 30);

        Assert.Equal(500, afterHit.ToDouble(), 0);
        Assert.InRange(midway.ToDouble(), 500 + 40, 500 + 200 - 40);   // part of the echo so far
        Assert.Equal(500 + 200, Lost(kernel.State).ToDouble(), 0);      // 40% of 500 in all
    }

    [Fact]
    public void Attacks_DoNotReverberate_NorTheEchoItself()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Croak, H(ctx.State, "gamma").Id));
        At(kernel, 2, ctx => AttackSystem.ApplyHit(ctx, H(ctx.State, "alpha"), H(ctx.State, "beta"), Fixed.FromInt(100), critical: false));

        kernel.RunTicks(2 + 6 * 30);

        Assert.Equal(100, Lost(kernel.State).ToDouble(), 0);
    }

    [Fact]
    public void SpellsCostLess()
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("nuke", "Nuke", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0, new[] { 10000 },
            new[] { 100 }, new[] { 1 }, new[] { EffectDefinition.Damage(DamageType.Pure, 1) })), new[] { "nuke" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hero!.Abilities[0].Level = 1;
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), Croak, H(ctx.State, "gamma").Id);
        });
        At(kernel, 2, ctx => Dynasty.MatchSimulation.Heroes.HeroOrders.Issue(H(ctx.State, "alpha"), Dynasty.MatchSimulation.Heroes.HeroOrder.Cast("nuke", H(ctx.State, "beta").Id)));

        kernel.RunTicks(10);

        Assert.Equal(1000 - 85, H(kernel.State, "alpha").Hero!.Mana.ToDouble(), 0);
    }

    /// <summary>The AI croaks for an ally hero in a fight - the one with the most mana for spells, itself too.</summary>
    [Fact]
    public void Ai_CroaksForTheSpellcaster()
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("croak", "Croak of Genius", AbilityTargeting.Unit, TargetTeam.Ally, 800, 0,
            new[] { 25000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Modifier(Croak) })), new[] { "croak" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(100);   // gamma has more
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 * 30);

        Assert.Contains(H(kernel.State, "gamma").Modifiers, m => m.Spec.Key == "croak");
    }
}
