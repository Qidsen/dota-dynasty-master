using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.Abilities.TestAbilities;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Channelled abilities (Dota; the official notes of abilities_english.txt after 7.41f: any action cancels a channel, Phase Shift is
/// invulnerability during it, Freezing Field explodes every 0.1 s): after the cast the hero stands channelling; the pulse effects run
/// every period, the end effects only when the channel is completed; a stun, a silence, death or a new order ends it early.
/// </summary>
public class ChannelTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>A one-second channel pulsing 10 damage on beta every 0.2 s, healing alpha by 50 at the end, invulnerable meanwhile.</summary>
    private static RulesetDraft WithChannel()
    {
        var draft = Draft();
        draft.Abilities.Add(new AbilityDefinition("ritual", "Ritual", AbilityTargeting.Unit, TargetTeam.Enemy, 800, 0,
            new[] { 30000 }, new[] { 0 }, new[] { 1 }, Array.Empty<EffectDefinition>(),
            channel: new ChannelSpec(new[] { 1000 }, 200, new[] { EffectDefinition.Damage(DamageType.Pure, 10) }, new[] { EffectDefinition.OnCaster(EffectDefinition.Heal(50)) },
                new ModifierSpec("ritual_shield", -1, ModifierFlags.Invulnerable))));
        return draft;
    }

    private static SimulationKernel<MatchState> Arena(RulesetDraft draft, Action<SimulationContext<MatchState>>? setup = null)
    {
        draft.WithHeroAbilities("alpha", new[] { "ritual" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "gamma").Position = P(3000, 3000);
            H(ctx.State, "beta").Position = P(5400, 3000);
            H(ctx.State, "delta").Position = P(8000, 8000);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(100);
            setup?.Invoke(ctx);
        });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("ritual", H(ctx.State, "beta").Id)));
        return kernel;
    }

    private static int Pulses(SimulationKernel<MatchState> kernel) =>
        kernel.EventsOf<DamageDealt>().Count(d => d.Attacker == H(kernel.State, "alpha").Id && d.Target == H(kernel.State, "beta").Id);

    private static bool Healed(SimulationKernel<MatchState> kernel) =>
        kernel.EventsOf<HealApplied>().Any(h => h.Target == H(kernel.State, "alpha").Id && h.Amount == Fixed.FromInt(50));

    [Fact]
    public void Channel_PulsesEveryPeriod_AndEndsWithItsEndEffects()
    {
        var kernel = Arena(WithChannel());

        kernel.RunTicks(2 + 45);

        Assert.Equal(5, Pulses(kernel));   // at 0, 0.2, 0.4, 0.6 and 0.8 s
        Assert.True(Healed(kernel));
        Assert.False(H(kernel.State, "alpha").Hero!.IsChanneling);
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "ritual_shield");
    }

    [Fact]
    public void TheChannellingHero_StandsAndHasTheChannelsModifier()
    {
        var kernel = Arena(WithChannel());
        FixedVector2 at = default;
        bool shielded = false, channelling = false;
        At(kernel, 10, ctx =>
        {
            HeroOrders.Issue(H(ctx.State, "gamma"), HeroOrder.Move(P(1000, 3000)));   // another hero's order changes nothing
            at = H(ctx.State, "alpha").Position;
        });
        At(kernel, 20, ctx =>
        {
            shielded = H(ctx.State, "alpha").Modifiers.Any(m => m.Spec.Key == "ritual_shield");
            channelling = H(ctx.State, "alpha").Hero!.IsChanneling;
        });

        kernel.RunTicks(21);

        Assert.True(channelling);
        Assert.True(shielded);
        Assert.Equal(at, H(kernel.State, "alpha").Position);
        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);   // beta is in reach, yet no attack
    }

    [Theory]
    [InlineData("stun")]
    [InlineData("silence")]
    [InlineData("order")]
    public void Channel_EndsEarly_WithoutItsEndEffects(string how)
    {
        var kernel = Arena(WithChannel());
        At(kernel, 12, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            if (how == "order")
                HeroOrders.Issue(alpha, HeroOrder.Move(P(4000, 3000)));
            else
                Modifiers.Apply(ctx, alpha, ModifierSpec.Status(how, 2000, how == "stun" ? ModifierFlags.Stunned : ModifierFlags.Silenced), H(ctx.State, "beta").Id);
        });

        kernel.RunTicks(2 + 45);

        Assert.InRange(Pulses(kernel), 1, 3);
        Assert.False(Healed(kernel));
        Assert.False(H(kernel.State, "alpha").Hero!.IsChanneling);
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "ritual_shield");
    }

    /// <summary>
    /// Rearm (7.41: affects_items 0): at the end of its channel the hero's abilities are ready again; its items are not, and neither is
    /// the refreshing ability itself.
    /// </summary>
    [Fact]
    public void Rearm_RefreshesTheAbilities_NotTheItems_NorItself()
    {
        var draft = Draft();
        draft.Abilities.Add(new AbilityDefinition("zap", "Zap", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, new[] { 60000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.OnCaster(EffectDefinition.Heal(1)) }));
        draft.Items.Add(new ItemDefinition("orb", "Orb", 0, activeAbilityId: "zap"));
        draft.Abilities.Add(new AbilityDefinition("rearm", "Rearm", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0, new[] { 5000 }, new[] { 0 }, new[] { 1 },
            Array.Empty<EffectDefinition>(), channel: new ChannelSpec(new[] { 1000 }, 1000, Array.Empty<EffectDefinition>(), new[] { EffectDefinition.RefreshCooldowns(abilitiesOnly: true) }, null)));
        draft.WithHeroAbilities("alpha", new[] { "rearm", "bolt" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "beta").Position = P(5400, 3000);
            H(ctx.State, "alpha").Hero!.Level = 2;   // two skill points: rearm and bolt
            HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem("orb")!);
        });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("bolt", H(ctx.State, "beta").Id)));
        At(kernel, 20, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("zap")));
        At(kernel, 25, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("rearm")));

        kernel.RunTicks(25 + 35);

        HeroState hero = H(kernel.State, "alpha").Hero!;
        long now = kernel.CurrentTick.Value;
        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "zap");
        Assert.True(hero.Ability("bolt")!.IsReady(now), "bolt is ready again");
        Assert.False(hero.Inventory.Single(i => i.Definition.Id == "orb").Active!.IsReady(now), "the orb is not");
        Assert.False(hero.Ability("rearm")!.IsReady(now), "nor rearm itself");
    }

    /// <summary>Freezing Field's explosions: each lands at a random point between two distances from the caster, hitting enemies around it.</summary>
    [Fact]
    public void RandomArea_HitsAroundRandomPointsWithinTheRing_NeverInside()
    {
        var draft = Draft();
        draft.Abilities.Add(new AbilityDefinition("ritual", "Field", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
            new[] { 30000 }, new[] { 0 }, new[] { 1 }, Array.Empty<EffectDefinition>(),
            channel: new ChannelSpec(new[] { 10000 }, 100,
                new[] { EffectDefinition.RandomArea(100, 300, 400, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 1)) }, Array.Empty<EffectDefinition>(), null)));
        draft.WithHeroAbilities("alpha", new[] { "ritual" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "gamma").Position = P(3000, 3000);
            H(ctx.State, "beta").Position = P(5350, 3000);   // on the ring
            H(ctx.State, "delta").Position = P(5000, 3100);  // inside it: never within 100 of a point 300+ away
        });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("ritual")));

        kernel.RunTicks(2 + 10 * 30);

        var alpha = H(kernel.State, "alpha").Id;
        Assert.Contains(kernel.EventsOf<DamageDealt>(), d => d.Attacker == alpha && d.Target == H(kernel.State, "beta").Id);
        Assert.DoesNotContain(kernel.EventsOf<DamageDealt>(), d => d.Attacker == alpha && d.Target == H(kernel.State, "delta").Id);
    }
}
