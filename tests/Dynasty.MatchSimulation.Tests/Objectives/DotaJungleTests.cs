using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's jungle (Fandom «Neutral creeps» «Spawning», Liquipedia «Neutral Creeps» 7.41f): each spawn a camp takes one of its
/// tier's camp types at random, never the same twice in a row; the flooded camps evolve every 5 minutes instead — the camp is
/// replaced by its next stage unless it is fighting or blocked by a unit of the teams within its leash, and spawns that stage
/// next time otherwise.
/// </summary>
public class DotaJungleTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static RulesetDraft Jungle(params string[] types)
    {
        var draft = new RulesetDraft().WithoutWaves();
        foreach (string type in types)
            draft.Creeps.Add(new CreepType(type, MeleeStats(hp: 300), new Bounty(30, 30, 40)));
        return draft;
    }

    /// <summary>Records the creep types of each new spawn of the camp and kills them at once.</summary>
    private sealed class Clearer : ISimulationSystem<MatchState>
    {
        public List<string> Spawns { get; } = new();

        public string Name => "test-clearer";

        public void Tick(SimulationContext<MatchState> context)
        {
            var alive = context.State.Units.Where(u => u.Team == Team.Neutral && u.Kind == UnitKind.Creep && u.IsAlive).ToList();
            if (alive.Count == 0)
                return;
            Spawns.Add(string.Join("+", alive.Select(u => u.TypeId).OrderBy(t => t, StringComparer.Ordinal)));
            MatchUnit hero = H(context.State, "alpha");
            foreach (MatchUnit creep in alive)
                Damage.Deal(context, hero.Id, creep, Fixed.FromInt(99999), DamageType.Pure);
        }
    }

    [Fact]
    public void ACampOfManyTypes_TakesOneAtRandomEachSpawn_NeverTheSameTwiceInARow()
    {
        var draft = Jungle("kobold", "ghost", "harpy");
        draft.Camps.Add(new NeutralCampDefinition("camp.small", P(3000, 2500), new[] { new WaveEntry("kobold", 3) }, firstSpawnMs: 0, respawnMs: 100,
            leashRadius: 600, variants: new[] { new[] { new WaveEntry("kobold", 3) }, new[] { new WaveEntry("ghost", 2) }, new[] { new WaveEntry("harpy", 3) } }));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        var clearer = new Clearer();
        kernel.AddSystem(clearer);

        kernel.RunTicks(30 * 12);

        Assert.True(clearer.Spawns.Count >= 60, "spawns: " + clearer.Spawns.Count);
        Assert.Equal(new[] { "ghost+ghost", "harpy+harpy+harpy", "kobold+kobold+kobold" }, clearer.Spawns.Distinct().OrderBy(s => s, StringComparer.Ordinal));
        for (int i = 1; i < clearer.Spawns.Count; i++)
            Assert.NotEqual(clearer.Spawns[i - 1], clearer.Spawns[i]);
    }

    /// <summary>Stages: 2 pollywogs, then a boglet in place of one, then 2 boglets; every 5 s, the last one stays.</summary>
    private static RulesetDraft Flooded()
    {
        var draft = Jungle("pollywog", "boglet");
        draft.Camps.Add(new NeutralCampDefinition("camp.flooded", P(3000, 2500), new[] { new WaveEntry("pollywog", 2) }, firstSpawnMs: 1000, respawnMs: 60000,
            leashRadius: 600, evolution: new CampEvolution(5000, new[]
            {
                new[] { new WaveEntry("pollywog", 2) },
                new[] { new WaveEntry("pollywog", 1), new WaveEntry("boglet", 1) },
                new[] { new WaveEntry("boglet", 2) },
            })));
        return draft;
    }

    private static string Camp(MatchState state) => string.Join("+", state.Units
        .Where(u => u.Team == Team.Neutral && u.Kind == UnitKind.Creep && u.IsAlive).Select(u => u.TypeId).OrderBy(t => t, StringComparer.Ordinal));

    [Fact]
    public void AFloodedCamp_IsReplacedByItsNextStageEveryInterval_UpToTheLast()
    {
        var kernel = Flooded().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        string first = "", second = "", third = "", last = "";
        At(kernel, 30 * 4, ctx => first = Camp(ctx.State));
        At(kernel, 30 * 6, ctx => second = Camp(ctx.State));
        At(kernel, 30 * 11, ctx => third = Camp(ctx.State));
        At(kernel, 30 * 16, ctx => last = Camp(ctx.State));

        kernel.RunTicks(30 * 16 + 1);

        Assert.Equal(("pollywog+pollywog", "boglet+pollywog", "boglet+boglet", "boglet+boglet"), (first, second, third, last));
        Assert.Equal(3, kernel.EventsOf<CampSpawned>().Count());
    }

    [Fact]
    public void ABlockedFloodedCamp_KeepsItsCreeps_AndSpawnsTheEvolvedStageNextTime()
    {
        var kernel = Flooded().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 30 * 4, ctx => H(ctx.State, "alpha").Position = P(3300, 2500));   // inside the leash at 5 s
        string atSix = "";
        At(kernel, 30 * 6, ctx =>
        {
            atSix = Camp(ctx.State);
            H(ctx.State, "alpha").Position = P(8000, 8000);
            foreach (MatchUnit creep in ctx.State.Units.Where(u => u.Team == Team.Neutral && u.Kind == UnitKind.Creep && u.IsAlive).ToList())
                Damage.Deal(ctx, H(ctx.State, "alpha").Id, creep, Fixed.FromInt(99999), DamageType.Pure);
        });

        kernel.RunTicks(30 * 67);

        Assert.Equal("pollywog+pollywog", atSix);
        Assert.Equal("boglet+boglet", Camp(kernel.State));   // respawned 60 s after clearing, with the stage of then
    }

    [Fact]
    public void Validation_RejectsUnknownCreepsInVariantsAndStages_AndANonPositiveInterval()
    {
        var draft = Jungle("kobold");
        draft.Camps.Add(new NeutralCampDefinition("camp.v", P(3000, 2500), new[] { new WaveEntry("kobold", 1) }, 0, 1000, 600,
            variants: new[] { new[] { new WaveEntry("kobold", 1) }, new[] { new WaveEntry("gremlin", 1) } }));
        draft.Camps.Add(new NeutralCampDefinition("camp.e", P(4000, 2500), new[] { new WaveEntry("kobold", 1) }, 0, 1000, 600,
            evolution: new CampEvolution(0, new[] { new[] { new WaveEntry("kobold", 1) }, new[] { new WaveEntry("goblin", 1) } })));

        IReadOnlyList<string> errors = draft.Build().Validate();

        Assert.Contains(errors, e => e.Contains("camp.v") && e.Contains("gremlin"));
        Assert.Contains(errors, e => e.Contains("camp.e") && e.Contains("goblin"));
        Assert.Contains(errors, e => e.Contains("camp.e") && e.Contains("interval"));
    }

    [Fact]
    public void VariantsAndStages_ArePartOfTheRulesetIdentity()
    {
        string Fingerprint(NeutralCampDefinition camp)
        {
            var draft = Jungle("kobold", "ghost");
            draft.Camps.Add(camp);
            return draft.Build().ToDescriptor().Fingerprint.ToString();
        }

        WaveEntry[] kobolds = { new WaveEntry("kobold", 1) };
        WaveEntry[] ghosts = { new WaveEntry("ghost", 1) };
        string plain = Fingerprint(new NeutralCampDefinition("c", P(3000, 2500), kobolds, 0, 1000, 600));
        string variants = Fingerprint(new NeutralCampDefinition("c", P(3000, 2500), kobolds, 0, 1000, 600, variants: new[] { kobolds, ghosts }));
        string stages = Fingerprint(new NeutralCampDefinition("c", P(3000, 2500), kobolds, 0, 1000, 600, evolution: new CampEvolution(5000, new[] { kobolds, ghosts })));
        string otherStages = Fingerprint(new NeutralCampDefinition("c", P(3000, 2500), kobolds, 0, 1000, 600, evolution: new CampEvolution(5000, new[] { kobolds, kobolds })));

        Assert.Equal(4, new[] { plain, variants, stages, otherStages }.Distinct().Count());
    }
}
