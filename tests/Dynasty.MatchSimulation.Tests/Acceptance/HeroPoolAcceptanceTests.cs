using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests.Acceptance;

/// <summary>
/// Phase 05 hero pool: the fourteen heroes added for drafting are played by the AI — each one
/// casts its active abilities in real 5v5 matches (two lineups that mix new and old heroes).
/// </summary>
[Trait("Category", "Acceptance")]
public class HeroPoolAcceptanceTests
{
    private static readonly MatchLineup LineupA = new(
        new[] { new HeroPick("duskrunner", "bot"), new HeroPick("lumenpriest", "bot"), new HeroPick("stormcaller", "mid"), new HeroPick("tidebreaker", "top"), new HeroPick("windsinger", "top") },
        new[] { new HeroPick("gunwright", "top"), new HeroPick("chainwarden", "top"), new HeroPick("voidseer", "mid"), new HeroPick("plaguebringer", "bot"), new HeroPick("spiritwarden", "bot") });

    private static readonly MatchLineup LineupB = new(
        new[] { new HeroPick("ashblade", "bot"), new HeroPick("thornkeeper", "bot"), new HeroPick("gloomfang", "mid"), new HeroPick("wildheart", "top"), new HeroPick("bulwark", "top") },
        new[] { new HeroPick("ironvow", "top"), new HeroPick("dawnmender", "top"), new HeroPick("emberwisp", "mid"), new HeroPick("stonewarden", "bot"), new HeroPick("hivecaller", "bot") });

    private static readonly Lazy<List<SimulationKernel<MatchState>>> Matches = new(() => new List<SimulationKernel<MatchState>>
    {
        Play(LineupA, 1), Play(LineupA, 2), Play(LineupB, 1), Play(LineupB, 2),
    });

    private static SimulationKernel<MatchState> Play(MatchLineup lineup, ulong seed)
    {
        var kernel = Match.Create(BaseRuleset.Create(), seed, lineup);
        HeroAi.Attach(kernel);
        kernel.RunUntilFinished(40L * 60 * 30);
        return kernel;
    }

    [Theory]
    [InlineData("duskrunner")]
    [InlineData("ashblade")]
    [InlineData("gunwright")]
    [InlineData("stormcaller")]
    [InlineData("voidseer")]
    [InlineData("gloomfang")]
    [InlineData("tidebreaker")]
    [InlineData("bulwark")]
    [InlineData("plaguebringer")]
    [InlineData("wildheart")]
    [InlineData("windsinger")]
    [InlineData("lumenpriest")]
    [InlineData("chainwarden")]
    [InlineData("spiritwarden")]
    public void NewHero_CastsItsActiveAbilities(string heroId)
    {
        HeroDefinition hero = BaseRuleset.Create().FindHero(heroId)!;
        var actives = hero.AbilityIds.Where(a => BaseRuleset.Create().FindAbility(a)!.Targeting != AbilityTargeting.Passive).ToList();

        var cast = Matches.Value
            .SelectMany(k => k.Events.Records.Select(r => r.Event).OfType<AbilityCast>()
                .Where(c => k.State.Heroes.Any(h => h.Id == c.Caster && h.Hero!.Definition.Id == heroId)))
            .Select(c => c.AbilityId)
            .ToHashSet();

        Assert.True(actives.All(cast.Contains), $"{heroId} cast {string.Join(", ", cast)}; actives {string.Join(", ", actives)}");
    }

    [Fact]
    public void MatchesWithTheNewHeroes_EndWithAWinner()
    {
        Assert.All(Matches.Value, k => Assert.NotNull(k.State.Winner));
    }
}
