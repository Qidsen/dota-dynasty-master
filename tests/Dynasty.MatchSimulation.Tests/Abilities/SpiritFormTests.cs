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
/// Spirit Form (Dota 7.41f data and the official description): more cast range, and Illuminate heals allies in its area as well.
/// The cast range bonus is the general one (Aether Lens has it too).
/// </summary>
public class SpiritFormTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Form = new ModifierSpec("spirit_form", 40000, dispellable: false, castRangeBonus: 250);

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        setup?.Invoke(draft);
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? alphaAbilities : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(6100, 5000);
            H(ctx.State, "gamma").Position = P(6000, 5100);
            H(ctx.State, "gamma").Hp = Fixed.FromInt(1000);
        });
        return kernel;
    }

    private static readonly AbilityDefinition Nuke = new AbilityDefinition("nuke", "Nuke", AbilityTargeting.Unit, TargetTeam.Enemy, 900, 0, new[] { 60000 },
        new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Damage(DamageType.Pure, 100) });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheBonus_LetsItCastFromFarther(bool inForm)
    {
        var kernel = Arena(draft => draft.Abilities.Add(Nuke), new[] { "nuke" });
        At(kernel, 1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("hold", -1, ModifierFlags.Stunned), H(ctx.State, "beta").Id);
            if (inForm)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), Form, H(ctx.State, "alpha").Id);
        });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("nuke", H(ctx.State, "beta").Id)));

        kernel.RunTicks(30);

        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "nuke");
        double moved = (H(kernel.State, "alpha").Position.X - Fixed.FromInt(5000)).ToDouble();
        Assert.Equal(inForm, moved < 1);   // without the bonus it walks the rest of the way
    }

    [Theory]
    [InlineData(true, 1060)]
    [InlineData(false, 1000)]
    public void Illuminate_HealsAlliesInTheArea_OnlyInSpiritForm(bool inForm, int gammaHp)
    {
        var kernel = Arena();
        EffectDefinition[] illuminate =
        {
            EffectDefinition.Area(400, AreaCenter.Point, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 100)),
            EffectDefinition.IfCasterHas("spirit_form", EffectDefinition.Area(400, AreaCenter.Point, TargetTeam.Ally, EffectDefinition.Heal(60))),
        };
        At(kernel, 1, ctx =>
        {
            if (inForm)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), Form, H(ctx.State, "alpha").Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), illuminate, 1, H(ctx.State, "alpha"), P(6000, 5000)));

        kernel.RunTicks(4);

        Assert.Equal(gammaHp, H(kernel.State, "gamma").Hp.ToDouble(), 0);
        Assert.Equal(4900, H(kernel.State, "beta").Hp.ToDouble(), 0);
    }

    /// <summary>The AI reckons with the bonus: it casts from where it stands rather than walking up first.</summary>
    [Fact]
    public void Ai_CastsWithTheBonusRange()
    {
        var kernel = Arena(draft => draft.Abilities.Add(Nuke), new[] { "nuke" });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "beta").Position = P(6190, 5000);   // 1190 away: beyond 900 + 200 of reckoning without the bonus
            H(ctx.State, "gamma").Position = P(1000, 1000);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("hold", -1, ModifierFlags.Stunned), H(ctx.State, "beta").Id);
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), Form, H(ctx.State, "alpha").Id);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0, EngageRange = 1600 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);
        double xAtCast = -1;
        kernel.AddSystem(new CastWatch(kernel, () => xAtCast < 0, x => xAtCast = x));

        kernel.RunTicks(2 * 30);

        Assert.True(xAtCast >= 0, "it cast");
        Assert.True(xAtCast < 5030, "from where it stood: " + xAtCast);
    }

    /// <summary>Notes where alpha stands when its nuke is cast.</summary>
    private sealed class CastWatch : ISimulationSystem<MatchState>
    {
        private readonly SimulationKernel<MatchState> _kernel;
        private readonly Func<bool> _waiting;
        private readonly Action<double> _note;

        public CastWatch(SimulationKernel<MatchState> kernel, Func<bool> waiting, Action<double> note)
        {
            _kernel = kernel;
            _waiting = waiting;
            _note = note;
        }

        public string Name => "cast-watch";

        public void Tick(SimulationContext<MatchState> context)
        {
            if (_waiting() && _kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "nuke"))
                _note(H(context.State, "alpha").Position.X.ToDouble());
        }
    }
}
