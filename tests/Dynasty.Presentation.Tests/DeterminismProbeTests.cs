using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;

namespace Dynasty.Presentation.Tests;

public class DeterminismProbeTests
{
    [Fact]
    public void Probe_ReportsTheChecksumOfTheDemoMatchPlayedHeadless()
    {
        ProbeResult result = DeterminismProbe.Run(seed: 2, ticks: 900);

        var kernel = Match.Create(BaseRuleset.Create(), 2, DemoMatch.Lineup);
        HeroAi.Attach(kernel);
        kernel.RunTicks(900);
        Assert.Equal(kernel.Events.Checksum, result.Checksum);
        Assert.Equal(900, result.Ticks);
        Assert.Equal(BaseRuleset.Create().ToDescriptor().ToString(), result.Ruleset);
    }

    [Fact]
    public void ReportText_RoundTrips()
    {
        var results = new[] { DeterminismProbe.Run(1, 300), DeterminismProbe.Run(2, 300) };

        string text = DeterminismProbe.Format(results);

        Assert.Equal(results.Select(r => (r.Seed, r.Ticks, r.Checksum, r.Ruleset)), DeterminismProbe.Parse(text).Select(r => (r.Seed, r.Ticks, r.Checksum, r.Ruleset)));
    }
}
