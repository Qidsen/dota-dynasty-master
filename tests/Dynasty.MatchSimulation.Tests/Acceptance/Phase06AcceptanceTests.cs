using Dynasty.Core.Kernel;
using Dynasty.Core.Randomness;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Players;

namespace Dynasty.MatchSimulation.Tests.Acceptance;

/// <summary>
/// Phase 06 acceptance (spec §38: "swapping a player changes decisions"): on mirrored line-ups —
/// the same heroes on both sides, so only the players differ — better players fight better, farm
/// more and win most matches, on either side of the map; and swapping one player changes that
/// player's hero.
/// </summary>
[Trait("Category", "Acceptance")]
public class Phase06AcceptanceTests
{
    private static readonly HeroPick[] Heroes =
    {
        new("ironvow", "bot"), new("emberwisp", "mid"), new("stonewarden", "top"), new("thornkeeper", "top"), new("dawnmender", "bot"),
    };

    private static readonly MatchLineup Mirror = new(Heroes, Heroes.Select(p => new HeroPick(p.HeroId, p.LaneId == "top" ? "bot" : p.LaneId == "bot" ? "top" : p.LaneId)).ToArray());

    private static readonly ulong[] Seeds = Enumerable.Range(1, 8).Select(i => (ulong)i).ToArray();

    private static void AddPlayers(HeroAiSettings settings, Team team, int quality, ulong seed)
    {
        var random = new DeterministicRandom(seed);
        IReadOnlyList<HeroPick> picks = team == Team.Dawn ? Mirror.Dawn : Mirror.Dusk;
        for (int i = 0; i < picks.Count; i++)
        {
            PlayerProfile player = PlayerGenerator.Generate(random, team + picks[i].HeroId, i + 1, quality, new[] { picks[i].HeroId });
            settings.SkillsOf(team)[picks[i].HeroId] = HeroSkill.FromPlayer(player, picks[i].HeroId);
        }
    }

    private static SimulationKernel<MatchState> Play(ulong seed, HeroAiSettings settings, int minutes)
    {
        var kernel = Match.Create(BaseRuleset.Create(), seed, Mirror);
        HeroAi.Attach(kernel, settings);
        kernel.RunUntilFinished(minutes * 60L * 30);
        return kernel;
    }

    private static readonly Lazy<List<(Team Elite, SimulationKernel<MatchState> Kernel)>> EliteVsPoor = new(() =>
        // Independent deterministic matches: played in parallel, results in a fixed order.
        new[] { Team.Dawn, Team.Dusk }.SelectMany(elite => Seeds.Select(seed => (elite, seed))).AsParallel().AsOrdered().Select(m =>
        {
            var settings = new HeroAiSettings();
            AddPlayers(settings, m.elite, 17, m.seed * 31);
            AddPlayers(settings, m.elite.Opponent(), 7, m.seed * 37);
            return (m.elite, Play(m.seed, settings, 60));
        }).ToList());

    [Fact]
    public void BetterPlayers_OnTheSameHeroes_WinMostMatches_OnEitherSide()
    {
        foreach (Team side in new[] { Team.Dawn, Team.Dusk })
        {
            int wins = EliteVsPoor.Value.Count(m => m.Elite == side && m.Kernel.State.Winner == side);
            Assert.True(wins >= 6, $"better players on {side} won {wins} of {Seeds.Length}");
        }
    }

    [Fact]
    public void BetterPlayers_FightAndFarmBetter()
    {
        int Sum(Func<MatchUnit, int> value, bool elite) => EliteVsPoor.Value.Sum(m =>
            m.Kernel.State.Heroes.Where(h => (h.Team == m.Elite) == elite).Sum(value));

        Assert.True(Sum(h => h.Hero!.Kills, true) > Sum(h => h.Hero!.Kills, false) * 1.5, $"kills elite {Sum(h => h.Hero!.Kills, true)}, poor {Sum(h => h.Hero!.Kills, false)}");
        Assert.True(Sum(h => h.Hero!.LastHits, true) > Sum(h => h.Hero!.LastHits, false) * 1.2, $"last hits elite {Sum(h => h.Hero!.LastHits, true)}, poor {Sum(h => h.Hero!.LastHits, false)}");
    }

    [Fact]
    public void SwappingOnePlayer_ChangesThatHerosPlay()
    {
        // Everyone average; only Dawn's Ironvow player changes.
        (double LastHits, double Deaths) Carry(int quality)
        {
            var runs = Seeds.Take(4).AsParallel().AsOrdered().Select(seed =>
            {
                var settings = new HeroAiSettings();
                AddPlayers(settings, Team.Dawn, 12, 7);
                AddPlayers(settings, Team.Dusk, 12, 8);
                PlayerProfile carry = PlayerGenerator.Generate(new DeterministicRandom(9), "carry", 1, quality, new[] { "ironvow" });
                settings.SkillsOf(Team.Dawn)["ironvow"] = HeroSkill.FromPlayer(carry, "ironvow");
                return Play(seed, settings, 20).State.Heroes.Single(h => h.Team == Team.Dawn && h.Hero!.Definition.Id == "ironvow").Hero!;
            }).ToList();
            return (runs.Average(h => h.LastHits), runs.Average(h => h.Deaths));
        }

        var good = Carry(18);
        var bad = Carry(4);
        Assert.True(good.LastHits > bad.LastHits * 1.2 || good.Deaths < bad.Deaths * 0.8,
            $"carry by 20 min: good {good.LastHits:0.0} LH / {good.Deaths:0.0} deaths, bad {bad.LastHits:0.0} LH / {bad.Deaths:0.0} deaths");
    }
}
