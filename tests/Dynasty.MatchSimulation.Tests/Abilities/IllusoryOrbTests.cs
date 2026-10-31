using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Illusory Orb and Ethereal Jaunt (Dota 7.41f data and official notes): the orb flies from Puck to the point, striking each enemy on its
/// way once, and is gone at the end; Ethereal Jaunt takes Puck to the flying orb.
/// </summary>
public class IllusoryOrbTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition Orb = EffectDefinition.Summon("orb", 6000, false, new[] { 1 }, Array.Empty<int>(),
        EffectDefinition.Modifier(new ModifierSpec("orb", -1, ModifierFlags.Invulnerable, dispellable: false, travelsToPoint: true, fixedMoveSpeed: 750,
            trampleRadius: 225, trampleMarker: "orb_struck",
            onTrample: new[]
            {
                EffectDefinition.Damage(DamageType.Pure, 100),
                EffectDefinition.Modifier(new ModifierSpec("orb_struck", 6000, dispellable: false)),
            })));

    private static readonly EffectDefinition Jaunt = EffectDefinition.JauntTo("orb");

    /// <summary>The orb as Dota throws it: always the full distance towards the point.</summary>
    private static readonly EffectDefinition FarOrb = EffectDefinition.Summon("orb", 6000, false, new[] { 1 }, Array.Empty<int>(),
        EffectDefinition.Modifier(new ModifierSpec("orb", -1, ModifierFlags.Invulnerable, dispellable: false, travelsToPoint: true, travelDistance: 1000,
            fixedMoveSpeed: 750)));

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("orb", new UnitStats(100, Fixed.Zero, 750, 0, 0, 0, 1000, 0, 0, 16, visionRange: 450), new Bounty(0, 0, 0)));
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
            H(ctx.State, "beta").Position = P(5600, 5100);
            H(ctx.State, "gamma").Position = P(6200, 4900);
            foreach (string id in new[] { "beta", "gamma" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Stunned), H(ctx.State, id).Id);
        });
        return kernel;
    }

    private static List<MatchUnit> Orbs(MatchState state) => state.Units.Where(u => u.TypeId == "orb" && u.IsAlive).ToList();

    [Fact]
    public void TheOrb_FliesToThePoint_StrikingEachEnemyOnItsWayOnce_AndIsGoneThere()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Orb }, 1, H(ctx.State, "alpha"), P(6500, 5000)));
        double halfway = 0;
        At(kernel, 2 + 30, ctx => halfway = Orbs(ctx.State).Single().Position.X.ToDouble());

        kernel.RunTicks(2 + 3 * 30);

        Assert.InRange(halfway, 5000 + 750 - 30, 5000 + 750 + 30);   // a second at 750
        Assert.Empty(Orbs(kernel.State));
        Assert.Equal(4900, H(kernel.State, "beta").Hp.ToDouble(), 0);
        Assert.Equal(4900, H(kernel.State, "gamma").Hp.ToDouble(), 0);
    }

    [Fact]
    public void Jaunt_TakesPuckToTheFlyingOrb()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Orb }, 1, H(ctx.State, "alpha"), P(6500, 5000)));
        FixedVector2 orbAt = default;
        At(kernel, 2 + 30, ctx =>
        {
            orbAt = Orbs(ctx.State).Single().Position;
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Jaunt }, 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position);
        });
        double puckX = 0;
        At(kernel, 2 + 30, ctx => puckX = H(ctx.State, "alpha").Position.X.ToDouble());

        kernel.RunTicks(2 + 34);

        Assert.Equal(orbAt.X.ToDouble(), puckX, 0);
        Assert.Single(Orbs(kernel.State));   // the orb flies on
    }

    [Fact]
    public void TheOrb_FliesItsWholeDistance_TowardsANearerPoint()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { FarOrb }, 1, H(ctx.State, "alpha"), P(5000, 5300)));
        FixedVector2 last = default;
        for (long t = 3; t < 2 + 3 * 30; t++)
            At(kernel, t, ctx => { if (Orbs(ctx.State).Count > 0) last = Orbs(ctx.State)[0].Position; });

        kernel.RunTicks(2 + 3 * 30);

        Assert.Equal(5000, last.X.ToDouble(), 0);
        Assert.InRange(last.Y.ToDouble(), 5950, 6000);
        Assert.Empty(Orbs(kernel.State));
    }

    /// <summary>Retreating without an orb flying, the AI throws one towards home and then jaunts to it.</summary>
    [Fact]
    public void Ai_ThrowsTheOrbHome_ThenJaunts()
    {
        var kernel = Arena(draft =>
        {
            draft.Abilities.Add(new AbilityDefinition("orb", "Illusory Orb", AbilityTargeting.Point, TargetTeam.Enemy, 1950, 0, new[] { 10000 }, new[] { 0 },
                new[] { 1 }, new[] { FarOrb }));
            draft.Abilities.Add(new AbilityDefinition("jaunt", "Ethereal Jaunt", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 0 }, new[] { 0 },
                new[] { 1 }, new[] { Jaunt }));
        }, new[] { "orb", "jaunt" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(800);
            foreach (var ability in H(ctx.State, "alpha").Hero!.Abilities)
                ability.Level = 1;   // both learnt
            H(ctx.State, "beta").Position = P(5300, 5000);
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
            H(ctx.State, "gamma").Position = P(9000, 9000);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(3 * 30);

        var casts = kernel.EventsOf<AbilityCast>().Select(c => c.AbilityId).ToList();
        Assert.Equal(new[] { "orb", "jaunt" }, casts.Distinct().ToArray());
    }

    /// <summary>The AI jaunts away to its orb when it retreats from enemies close and the orb is farther from them; not when it fights.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Ai_JauntsToGetAway(bool hurt, bool jaunts)
    {
        var kernel = Arena(draft =>
        {
            draft.Abilities.Add(new AbilityDefinition("jaunt", "Ethereal Jaunt", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 0 }, new[] { 0 },
                new[] { 1 }, new[] { Jaunt }));
        }, new[] { "jaunt" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(hurt ? 800 : 5000);
            H(ctx.State, "beta").Position = P(5300, 5000);
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
            H(ctx.State, "gamma").Position = P(9000, 9000);
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Orb }, 1, H(ctx.State, "alpha"), P(3500, 5000));   // away from beta
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 * 30);

        Assert.Equal(jaunts, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "jaunt"));
    }
}
