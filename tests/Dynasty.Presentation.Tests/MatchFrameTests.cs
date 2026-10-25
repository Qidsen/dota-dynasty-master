using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;

namespace Dynasty.Presentation.Tests;

public class MatchFrameTests
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

    private static SimulationKernel<MatchState> Played(int seconds)
    {
        var kernel = Match.Create(BaseRuleset.Create(), 3, Lineup);
        HeroAi.Attach(kernel);
        kernel.RunTicks(seconds * 30L);
        return kernel;
    }

    [Fact]
    public void ObserverFrame_ShowsEveryLivingUnitWithItsState()
    {
        var kernel = Played(90);
        MatchState state = kernel.State;

        MatchFrame frame = MatchFrame.Capture(state, kernel.CurrentTick.Value, Viewpoint.Observer);

        Assert.Equal(state.Units.Count(u => u.IsAlive), frame.Units.Count);
        MatchUnit hero = state.Heroes.First(h => h.IsAlive);
        UnitFrame view = frame.Units.Single(u => u.Id == hero.Id.Value);
        Assert.Equal(hero.Position.X.ToDouble(), view.X, 0.01);   // frames hold floats: about 7 significant digits
        Assert.Equal(hero.Position.Y.ToDouble(), view.Y, 0.01);   // frames hold floats: about 7 significant digits
        Assert.Equal(hero.Hp.ToIntFloor(), view.Hp);
        Assert.Equal(hero.MaxHp, view.MaxHp);
        Assert.Equal(hero.Hero!.Level, view.Level);
        Assert.Equal(hero.Hero.Definition.Name, view.Name);
        Assert.Equal(state.IsVisibleTo(Team.Dawn, hero), view.SeenByDawn);
        Assert.Equal(state.IsVisibleTo(Team.Dusk, hero), view.SeenByDusk);
        Assert.Equal(kernel.CurrentTick.Value, frame.Tick);
        Assert.Equal(90f, frame.TimeSeconds, 1);
    }

    [Fact]
    public void TeamFrame_HidesWhatTheTeamCannotSee()
    {
        var kernel = Played(245);   // just after a wave spawns: the new Dusk creeps at their base are out of Dawn's sight
        MatchState state = kernel.State;

        MatchFrame frame = MatchFrame.Capture(state, kernel.CurrentTick.Value, Viewpoint.Dawn);

        var expected = state.Units.Where(u => u.IsAlive && (u.Team == Team.Dawn || state.IsVisibleTo(Team.Dawn, u))).Select(u => u.Id.Value).ToHashSet();
        Assert.Equal(expected, frame.Units.Select(u => u.Id).ToHashSet());
        Assert.Contains(state.Units, u => u.IsAlive && u.Team == Team.Dusk && !state.IsVisibleTo(Team.Dawn, u));   // the test means something
    }

    [Fact]
    public void TeamFrame_ShowsLastKnownPositionsOfEnemyHeroesOutOfSight()
    {
        var kernel = Played(60);
        MatchState state = kernel.State;
        // Play on until Dawn has lost sight of an enemy hero it saw before.
        while (kernel.CurrentTick.Value < 20 * 60 * 30 && !state.Heroes.Any(h => h.Team == Team.Dusk && !(h.IsAlive && state.IsVisibleTo(Team.Dawn, h)) &&
                                                                                state.KnowledgeOf(Team.Dawn).LastSeenOf(h.Id) != null))
            kernel.RunTicks(30);

        MatchFrame frame = MatchFrame.Capture(state, kernel.CurrentTick.Value, Viewpoint.Dawn);

        var unseen = state.Heroes.Where(h => h.Team == Team.Dusk && !(h.IsAlive && state.IsVisibleTo(Team.Dawn, h)) &&
                                             state.KnowledgeOf(Team.Dawn).LastSeenOf(h.Id) != null).ToList();
        Assert.NotEmpty(unseen);
        Assert.Equal(unseen.Select(h => h.Id.Value).ToHashSet(), frame.Ghosts.Select(g => g.HeroId).ToHashSet());
        GhostFrame ghost = frame.Ghosts.First();
        var seen = state.KnowledgeOf(Team.Dawn).LastSeenOf(new Dynasty.Core.Ids.EntityId(ghost.HeroId))!;
        Assert.Equal(seen.Position.X.ToDouble(), ghost.X, 0.01);   // frames hold floats: about 7 significant digits
        Assert.Equal(seen.Tick, ghost.SeenAtTick);
    }

    [Fact]
    public void ObserverFrame_HasNoGhosts()
    {
        var kernel = Played(240);

        Assert.Empty(MatchFrame.Capture(kernel.State, kernel.CurrentTick.Value, Viewpoint.Observer).Ghosts);
    }

    [Fact]
    public void Frame_IsASnapshotThatLaterTicksDoNotChange()
    {
        var kernel = Played(60);
        MatchFrame frame = MatchFrame.Capture(kernel.State, kernel.CurrentTick.Value, Viewpoint.Observer);
        var before = frame.Units.Select(u => (u.Id, u.X, u.Y, u.Hp)).ToList();

        kernel.RunTicks(300);

        Assert.Equal(before, frame.Units.Select(u => (u.Id, u.X, u.Y, u.Hp)).ToList());
    }

    [Fact]
    public void Frame_CarriesProjectilesTimeOfDayAndScore()
    {
        var kernel = Played(400);
        MatchState state = kernel.State;
        long tick = kernel.CurrentTick.Value;

        MatchFrame frame = MatchFrame.Capture(state, tick, Viewpoint.Observer);

        Assert.Equal(state.Projectiles.Count, frame.Projectiles.Count);
        Assert.Equal(state.IsNight(tick), frame.IsNight);
        Assert.Equal(state.Heroes.Where(h => h.Team == Team.Dawn).Sum(h => h.Hero!.Kills), frame.DawnKills);
        Assert.Equal(state.Heroes.Where(h => h.Team == Team.Dusk).Sum(h => h.Hero!.Kills), frame.DuskKills);
    }
}
