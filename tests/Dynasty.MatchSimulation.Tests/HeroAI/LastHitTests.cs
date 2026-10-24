using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

[Trait("Category", "Acceptance")]
public class LastHitTests
{
    [Fact]
    public void UncontestedHeroes_LastHitAGoodShareOfTheirLaneCreeps()
    {
        // Alone in mid with nobody to fight, a melee and a ranged carry over several seeds. Swinging
        // only once a creep is already within one hit got about 30%: allied creeps and towers land
        // their hits during the wind-up. Timing the swing against the hits already on the way does better.
        int lastHits = 0, laneCreepsKilled = 0;
        var perRun = new List<string>();
        foreach (string heroId in new[] { "ironvow", "starborn" })
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            var lineup = new MatchLineup(new[] { new HeroPick(heroId, "mid") }, Array.Empty<HeroPick>());
            SimulationKernel<MatchState> kernel = Match.Create(BaseRuleset.Create(), seed, lineup);
            HeroAi.Attach(kernel);
            kernel.RunTicks(8 * 60 * 30);

            MatchUnit hero = kernel.State.Heroes.Single();
            var midCreeps = kernel.EventsOf<CreepSpawned>().Where(c => c.Team == Team.Dusk && c.LaneId == "mid").Select(c => c.Unit).ToHashSet();
            int killed = kernel.EventsOf<UnitKilled>().Count(k => midCreeps.Contains(k.Victim));
            lastHits += hero.Hero!.LastHits;
            laneCreepsKilled += killed;
            perRun.Add($"{heroId}/{seed}: {hero.Hero.LastHits}/{killed}");
        }

        Assert.True(lastHits * 100 >= laneCreepsKilled * 38, $"last hits {lastHits}/{laneCreepsKilled} ({string.Join(", ", perRun)})");
    }
}
