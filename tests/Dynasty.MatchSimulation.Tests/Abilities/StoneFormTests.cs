using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Stone Form (Visage's self-cast; Dota 7.41f data and the official description): the Familiar closest to Visage turns to stone -
/// invulnerable, still, regenerating fast - and after a short delay smashes the ground, damaging and stunning enemies around it.
/// </summary>
public class StoneFormTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition Familiars = EffectDefinition.Summon("familiar", -1, false, new[] { 2 }, new[] { 2 }, Array.Empty<EffectDefinition>());

    private static readonly EffectDefinition StoneForm = EffectDefinition.OnOwnUnit("familiar",
        EffectDefinition.Modifier(new ModifierSpec("stone_form", 6000, ModifierFlags.Invulnerable | ModifierFlags.Stunned, dispellable: false,
            healPerSecond: Fixed.FromInt(150), periodMs: 200)),
        EffectDefinition.Modifier(new ModifierSpec("stone_landing", 550, dispellable: false,
            onEnd: new[]
            {
                EffectDefinition.Area(375, AreaCenter.Target, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 100),
                    EffectDefinition.Modifier(ModifierSpec.Status("stone_stun", 1000, ModifierFlags.Stunned))),
            })));

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("familiar", new UnitStats(600, Fixed.Zero, 430, 0, 0, 180, 1000, 300, 0, 16, visionRange: 600), new Bounty(100, 100, 0)));
        setup?.Invoke(draft);
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? alphaAbilities : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5900, 5000);
            H(ctx.State, "gamma").Position = P(9000, 9000);
        });
        return kernel;
    }

    private static List<MatchUnit> Pets(MatchState state) => state.Units.Where(u => u.TypeId == "familiar" && u.IsAlive).ToList();

    [Fact]
    public void TheClosestFamiliar_TurnsToStone_ThenSmashesTheGround()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Familiars }, 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position);
            List<MatchUnit> pets = Pets(ctx.State);
            pets[0].Position = P(5800, 5000);   // the closer one, next to beta
            pets[1].Position = P(6500, 6500);
            foreach (MatchUnit pet in pets)
            {
                pet.Hp = Fixed.FromInt(300);
                Modifiers.Apply(ctx, pet, ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), pet.Id);
            }
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, "beta").Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { StoneForm }, 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position));
        bool stunnedEarly = true;
        At(kernel, 10, ctx => stunnedEarly = H(ctx.State, "beta").Modifiers.Any(m => m.Spec.Key == "stone_stun"));
        At(kernel, 12, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, Pets(ctx.State)[0], Fixed.FromInt(1000), DamageType.Pure));   // invulnerable

        kernel.RunTicks(2 + 32);

        List<MatchUnit> pets = Pets(kernel.State);
        Assert.False(stunnedEarly);
        Assert.Contains(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "stone_stun");
        Assert.Equal(4900, H(kernel.State, "beta").Hp.ToDouble(), 0);
        Assert.Contains(pets[0].Modifiers, m => m.Spec.Key == "stone_form");
        Assert.DoesNotContain(pets[1].Modifiers, m => m.Spec.Key == "stone_form");
        Assert.True(pets[0].Hp > Fixed.FromInt(400));   // regenerating
        Assert.Equal(300, pets[1].Hp.ToDouble(), 0);
    }

    /// <summary>The AI turns a Familiar to stone when an enemy hero is close to it, not otherwise.</summary>
    [Theory]
    [InlineData(5800, true)]
    [InlineData(4000, false)]
    public void Ai_StonesAFamiliar_NearAnEnemy(int familiarX, bool stones)
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("stone", "Stone Form", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { StoneForm })), new[] { "stone" });
        At(kernel, 0, ctx =>
        {
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Summon("familiar", -1, false, new[] { 1 }, new[] { 1 }, Array.Empty<EffectDefinition>()) },
                1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position);
            MatchUnit pet = Pets(ctx.State)[0];
            pet.Position = P(familiarX, 5000);
            Modifiers.Apply(ctx, pet, ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), pet.Id);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, "beta").Id);
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30);

        Assert.Equal(stones, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "stone"));
    }
}
