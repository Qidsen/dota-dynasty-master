using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Gyroshell (Dota 7.41f data and official notes): Pangolier rolls - debuff immune, fast, not attacking; an enemy he strikes takes a
/// share of his attack damage, is knocked back and stunned, and can be struck again only once that stun is over.
/// </summary>
public class GyroshellTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Roll = new ModifierSpec("gyroshell", 6000, ModifierFlags.DebuffImmune | ModifierFlags.Disarmed, dispellable: false,
        fixedMoveSpeed: 550, trampleRadius: 150, trampleMarker: "gyroshell_stun",
        onTrample: new[]
        {
            EffectDefinition.ScaledDamage(DamageType.Pure, DamageScaling.CasterAttackDamagePercent, 8000),
            EffectDefinition.Knockback(150),
            EffectDefinition.Modifier(ModifierSpec.Status("gyroshell_stun", 1000, ModifierFlags.Stunned)),
        });

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        setup?.Invoke(draft);
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 100, 100, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? alphaAbilities : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5600, 5000);
            H(ctx.State, "gamma").Position = P(9000, 9000);
        });
        return kernel;
    }

    private static int Strikes(SimulationKernel<MatchState> kernel) =>
        kernel.EventsOf<ModifierApplied>().Count(m => m.Key == "gyroshell_stun" && m.Target == H(kernel.State, "beta").Id);

    [Fact]
    public void AStruckEnemy_TakesAShareOfTheAttack_IsKnockedBack_AndStunned_AndStruckAgainOnlyAfterTheStun()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted), H(ctx.State, "beta").Id));
        At(kernel, 2, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), Roll, H(ctx.State, "alpha").Id);
            H(ctx.State, "alpha").Position = P(5500, 5000);   // right next to beta
        });
        int afterFirst = 0;
        double lost = 0, pushedTo = 0;
        At(kernel, 4, ctx =>
        {
            afterFirst = Strikes(kernel);
            lost = (Fixed.FromInt(5000) - H(ctx.State, "beta").Hp).ToDouble();
            pushedTo = H(ctx.State, "beta").Position.X.ToDouble();
        });
        At(kernel, 5, ctx => H(ctx.State, "alpha").Position = H(ctx.State, "beta").Position);   // still on him
        int duringStun = 0;
        At(kernel, 20, ctx => duringStun = Strikes(kernel));

        kernel.RunTicks(2 + 40);

        Assert.Equal(1, afterFirst);
        Assert.Equal(80, lost, 0);
        Assert.Equal(5750, pushedTo, 0);
        Assert.Equal(1, duringStun);
        Assert.True(Strikes(kernel) >= 2);   // after the stun, struck again
        Assert.Equal(550, H(kernel.State, "alpha").MoveSpeed);
    }

    /// <summary>The AI rolls into the enemies it fights, striking them.</summary>
    [Fact]
    public void Ai_RollsIntoTheFight()
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("gyroshell", "Gyroshell", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
            new[] { 100000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.OnCaster(EffectDefinition.Modifier(Roll)) })), new[] { "gyroshell" });
        At(kernel, 1, ctx => H(ctx.State, "beta").Hp = Fixed.FromInt(1500));
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(4 * 30);

        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "gyroshell");
        Assert.True(Strikes(kernel) >= 1);
    }
}
