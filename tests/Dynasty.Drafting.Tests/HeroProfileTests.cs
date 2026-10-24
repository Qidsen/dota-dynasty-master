using Dynasty.Drafting;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting.Tests;

public class HeroProfileTests
{
    private static readonly RulesetContent Ruleset = BaseRuleset.Create();

    private static HeroProfile P(string heroId) => HeroProfile.Of(Ruleset, heroId);

    [Theory]
    [InlineData("frostweaver", "ironvow")]
    [InlineData("chainwarden", "duskrunner")]
    [InlineData("voidseer", "gunwright")]
    public void Control_IsHigherForDisablers(string disabler, string other) => Assert.True(P(disabler).Control > P(other).Control);

    [Theory]
    [InlineData("stonewarden", "emberwisp")]
    [InlineData("tidebreaker", "starborn")]
    public void Initiation_IsHigherForInitiators(string initiator, string other) => Assert.True(P(initiator).Initiation > P(other).Initiation);

    [Fact]
    public void Sustain_IsHighestForTheHealers()
    {
        var top = Ruleset.Heroes.OrderByDescending(h => P(h.Id).Sustain).Take(2).Select(h => h.Id).ToHashSet();

        Assert.Equal(new[] { "dawnmender", "lumenpriest" }.ToHashSet(), top);
    }

    [Theory]
    [InlineData("siegewright", "frostweaver")]
    [InlineData("wildheart", "voidseer")]
    public void Push_IsHigherForPushers(string pusher, string other) => Assert.True(P(pusher).Push > P(other).Push);

    [Theory]
    [InlineData("starborn", "thornkeeper")]
    [InlineData("gunwright", "windsinger")]
    public void Scaling_IsHigherForLateGameCarries(string carry, string other) => Assert.True(P(carry).Scaling > P(other).Scaling);

    [Fact]
    public void Durability_IsHigherForTanks() => Assert.True(P("bulwark").Durability > P("emberwisp").Durability);

    [Fact]
    public void Burst_IsHigherForNukersThanForSupports() => Assert.True(P("stormcaller").Burst > P("windsinger").Burst);

    [Fact]
    public void Mobility_CountsDashes() => Assert.True(P("shadeblade").Mobility > P("dawnmender").Mobility);

    [Fact]
    public void Profile_FollowsTheNumbers_SoAPatchChangesIt()
    {
        // A "patch" that doubles Frost Shackle's stun raises Frostweaver's control and nothing else's.
        AbilityDefinition shackle = Ruleset.FindAbility("frostweaver.frost_shackle")!;
        EffectDefinition projectile = shackle.Effects[0];
        var longer = EffectDefinition.Projectile(projectile.Speed,
            projectile.Inner[0],
            EffectDefinition.Modifier(projectile.Inner[1].Modifiers.Select(m => Dynasty.MatchSimulation.Combat.ModifierSpec.Status(m.Key, m.DurationMs * 2, m.Flags)).ToArray()));
        var patched = new AbilityDefinition(shackle.Id, shackle.Name, shackle.Targeting, shackle.TargetTeam, shackle.CastRange, shackle.CastPointMs,
            shackle.CooldownMs, shackle.ManaCost, shackle.RequiredHeroLevel, new[] { longer });
        var abilities = Ruleset.Abilities.Select(a => a.Id == shackle.Id ? patched : a).ToList();

        Assert.True(HeroProfile.Of(Ruleset.FindHero("frostweaver")!, abilities).Control > P("frostweaver").Control);
        Assert.Equal(P("ironvow").Control, HeroProfile.Of(Ruleset.FindHero("ironvow")!, abilities).Control);
    }
}
