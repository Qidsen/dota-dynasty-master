using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;

namespace Dynasty.Presentation.Tests;

public class MatchStatisticsTests
{
    private static readonly MatchLineup Lineup = new(
        new[]
        {
            new HeroPick("ironvow", "bot"), new HeroPick("dawnmender", "bot"), new HeroPick("emberwisp", "mid"),
            new HeroPick("stonewarden", "top"), new HeroPick("thornkeeper", "top"),
        },
        new[]
        {
            new HeroPick("starborn", "top"), new HeroPick("shadeblade", "top"), new HeroPick("frostweaver", "mid"),
            new HeroPick("hivecaller", "bot"), new HeroPick("siegewright", "bot"),
        });

    /// <summary>Plays in chunks, feeding the collector between chunks as a live view would.</summary>
    private static (SimulationKernel<MatchState> Kernel, MatchStatistics Stats) Played(int minutes)
    {
        var kernel = Match.Create(BaseRuleset.Create(), 2, Lineup);
        HeroAi.Attach(kernel);
        var stats = new MatchStatistics();
        for (int i = 0; i < minutes * 6; i++)
        {
            kernel.RunTicks(300);
            stats.Update(kernel);
        }

        return (kernel, stats);
    }

    [Fact]
    public void HeroLines_MatchTheScoreboardAndTheEventStream()
    {
        var (kernel, stats) = Played(12);
        MatchState state = kernel.State;
        var records = kernel.Events.Records;

        foreach (MatchUnit hero in state.Heroes)
        {
            HeroLine line = stats.Heroes.Single(l => l.HeroId == hero.Id.Value);
            var h = hero.Hero!;
            Assert.Equal((h.Kills, h.Deaths, h.Assists, h.LastHits, h.Denies, h.Level), (line.Kills, line.Deaths, line.Assists, line.LastHits, line.Denies, line.Level));
            Assert.Equal(h.Gold.ToIntFloor() + h.Inventory.Sum(i => i.Definition.Cost), line.NetWorth);
            Assert.Equal(kernel.EventsOf<GoldEarned>().Where(g => g.Hero == hero.Id).Sum(g => g.Amount), line.GoldEarned);
            int heroDamage = (int)kernel.EventsOf<DamageDealt>()
                .Where(d => d.Attacker == hero.Id && state.Heroes.Any(t => t.Id == d.Target))
                .Sum(d => d.Amount.ToDouble());
            Assert.InRange(line.HeroDamage, heroDamage - 1 - records.Count / 1000, heroDamage + 1);
            Assert.Equal(line.GoldEarned / 12, line.GoldPerMinute);
            Assert.Equal(h.Xp / 12, line.XpPerMinute);
        }

        Assert.Contains(stats.Heroes, l => l.HeroDamage > 0);
        Assert.Contains(stats.Heroes, l => l.Healing > 0);   // Dawnmender
        Assert.Contains(stats.Heroes, l => l.WardsPlaced > 0);
    }

    [Fact]
    public void TeamSeries_HasOneSamplePerMinuteOfGoldAndXp()
    {
        var (kernel, stats) = Played(10);

        Assert.Equal(10, stats.Minutes.Count);
        MinuteSample last = stats.Minutes[^1];
        Assert.Equal(10, last.Minute);
        Assert.Equal(stats.Heroes.Where(l => l.Team == Team.Dawn).Sum(l => l.GoldEarned), last.DawnGold);
        Assert.True(stats.Minutes[0].DawnGold < last.DawnGold);
        Assert.Equal(kernel.State.Heroes.Where(h => h.Team == Team.Dusk).Sum(h => h.Hero!.Xp), last.DuskXp);
    }

    [Fact]
    public void GoldSeries_ComesFromTheEventsSoItDoesNotDependOnHowOftenTheViewUpdates()
    {
        var (kernel, often) = Played(5);
        var rarely = new MatchStatistics();
        rarely.Update(kernel);

        Assert.Equal(often.Minutes.Select(m => (m.Minute, m.DawnGold, m.DuskGold)), rarely.Minutes.Select(m => (m.Minute, m.DawnGold, m.DuskGold)));
    }

    [Fact]
    public void Update_IsIncremental_FeedingOftenOrRarelyGivesTheSameTotals()
    {
        var (kernel, often) = Played(6);
        var rarely = new MatchStatistics();
        rarely.Update(kernel);

        Assert.Equal(often.Heroes.Select(l => (l.HeroDamage, l.Healing, l.GoldEarned, l.WardsPlaced, l.SmokesUsed, l.Buybacks)),
                     rarely.Heroes.Select(l => (l.HeroDamage, l.Healing, l.GoldEarned, l.WardsPlaced, l.SmokesUsed, l.Buybacks)));
    }
}
