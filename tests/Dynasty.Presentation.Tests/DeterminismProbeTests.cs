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

        Assert.Equal(results.Select(r => (r.Kind, r.Seed, r.Ticks, r.Checksum, r.Ruleset)), DeterminismProbe.Parse(text).Select(r => (r.Kind, r.Seed, r.Ticks, r.Checksum, r.Ruleset)));
    }

    private static RulesetContent APack(int version = 7410)
    {
        string path = Path.Combine(Path.GetTempPath(), "dynasty-pack-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, RulesetSourceTests.PositionedPack().Replace(@"""version"": 7410", @"""version"": " + version));
        try
        {
            return RulesetSource.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A run on a ruleset pack (the Dota pack in the viewer) names the pack's rules, and replaying it plays the pack again; a
    /// reference whose rules are not at hand is an error, never quietly replayed on other rules.
    /// </summary>
    [Fact]
    public void PackRun_IsReplayedOnThePack_AndOnlyThere()
    {
        RulesetContent pack = APack();
        ProbeResult onPack = DeterminismProbe.Run(ProbeKind.Drafted, 6, 600, pack);

        Assert.Equal(pack.ToDescriptor().ToString(), onPack.Ruleset);
        Assert.Equal(onPack.Checksum, DeterminismProbe.Replay(onPack, pack).Checksum);
        Assert.NotEqual(DeterminismProbe.Run(ProbeKind.Drafted, 6, 600).Checksum, onPack.Checksum);
        Assert.Throws<InvalidOperationException>(() => DeterminismProbe.Replay(onPack, null));
        Assert.Throws<InvalidOperationException>(() => DeterminismProbe.Replay(onPack, APack(version: 7411)));   // another pack
        ProbeResult onBase = DeterminismProbe.Run(ProbeKind.Drafted, 6, 600);
        Assert.Equal(onBase.Checksum, DeterminismProbe.Replay(onBase, pack).Checksum);   // a base-rules reference still replays on the base rules
    }

    [Fact]
    public void DraftedRun_IsReproducible_AndCoversDraftingAndPlayers()
    {
        ProbeResult first = DeterminismProbe.Run(ProbeKind.Drafted, 6, 3000);

        Assert.Equal(first.Checksum, DeterminismProbe.Replay(first).Checksum);
        Assert.NotEqual(DeterminismProbe.Run(ProbeKind.Demo, 6, 3000).Checksum, first.Checksum);
    }
}
