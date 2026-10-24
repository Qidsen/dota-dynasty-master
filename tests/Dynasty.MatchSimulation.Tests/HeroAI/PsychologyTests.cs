using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using Dynasty.Players;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

/// <summary>Psychology and chemistry act through concrete decisions (spec §17), not a hidden bonus.</summary>
public class PsychologyTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    [Fact]
    public void Tilt_MakesAFragileHeroTakeAFightAfterDyingTwice()
    {
        int AttacksAfterTwoDeaths(int tiltSusceptibility)
        {
            var draft = TestAbilities.Draft();
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx => H(ctx.State, "beta").Position = P(9000, 8000)));
            foreach (long tick in new long[] { 30, 400 })
                kernel.AddSystem(new LambdaSystem(tick, ctx => Damage.Deal(ctx, EntityId.None, H(ctx.State, "alpha"), Fixed.FromInt(99999), DamageType.Pure)));
            const long fight = 800;   // both respawns are over
            kernel.AddSystem(new LambdaSystem(fight, ctx =>
            {
                H(ctx.State, "alpha").Position = P(5000, 2000);
                H(ctx.State, "beta").Position = P(5700, 2000);
                Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
            }));
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
            settings.Uncontrolled.Add("beta");
            settings.Personalities["alpha"] = new HeroPersonality { Aggression = 1.0 };   // an even fight: normally just a poke
            settings.SkillsOf(Team.Dawn)["alpha"] = new HeroSkill { TiltSusceptibility = tiltSusceptibility, ReactionJitterTicks = 0, MisjudgementPercent = 0 };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(fight + 120);
            EntityId alpha = H(kernel.State, "alpha").Id;
            return kernel.TimedEventsOf<AttackStarted>().Count(a => a.Tick > fight && a.Event.Attacker == alpha);
        }

        Assert.True(AttacksAfterTwoDeaths(10) > 0, "a tilted hero should go in");
        Assert.Equal(0, AttacksAfterTwoDeaths(0));
    }

    [Fact]
    public void TiltSusceptibility_ComesFromTiltResistance()
    {
        static PlayerProfile Player(int tiltResistance) => new("p", "p", "p", "", 2000, new[] { "en" }, "", new[] { 90, 50, 50, 50, 50 },
            AttributeSet.Uniform(12).With(Dynasty.Players.Attribute.TiltResistance, tiltResistance), new HiddenQualities(12, 10, 10, 5, 10), Array.Empty<HeroPoolRecord>());

        Assert.True(HeroSkill.FromPlayer(Player(3), "x").TiltSusceptibility > 0);
        Assert.Equal(0, HeroSkill.FromPlayer(Player(18), "x").TiltSusceptibility);
    }

    [Fact]
    public void CallDelay_SlowsTheTeamsResponseToANewPlan()
    {
        double DistanceToEnemyTower(int callDelayTicks)
        {
            var draft = TestAbilities.Draft();
            draft.DawnWave.Clear();
            draft.DuskWave.Clear();
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid"), ("delta", "mid") }, new[] { ("beta", "mid"), ("epsilon", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                foreach (string hero in new[] { "alpha", "gamma", "delta" })
                    H(ctx.State, hero).Position = P(3200, 5000 + (hero == "alpha" ? 0 : hero == "gamma" ? 150 : -150));
                H(ctx.State, "beta").Position = P(9000, 8000);
                H(ctx.State, "epsilon").Position = P(9000, 8400);
            }));
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0, EvenPushFromMinute = 0 };
            settings.Uncontrolled.Add("beta");
            settings.Uncontrolled.Add("epsilon");
            foreach (string hero in new[] { "alpha", "gamma", "delta" })
                settings.Personalities[hero] = new HeroPersonality { Greed = 0.3 };
            settings.DawnStrategy = new TeamStrategy { CallDelayTicks = callDelayTicks };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(150);
            return FixedVector2.Distance(H(kernel.State, "alpha").Position, kernel.State.FindStructure("dusk.mid.t1")!.Position).ToDouble();
        }

        Assert.True(DistanceToEnemyTower(120) > DistanceToEnemyTower(0) + 500, $"slow calls {DistanceToEnemyTower(120):0}, quick calls {DistanceToEnemyTower(0):0}");
    }

    [Fact]
    public void Communication_FollowsTeamworkLeadershipAndACommonLanguage()
    {
        static PlayerProfile Player(int teamwork, int leadership, string language) => new("p", "p", "p", "", 2000, new[] { language }, "", new[] { 90, 50, 50, 50, 50 },
            AttributeSet.Uniform(12).With(Dynasty.Players.Attribute.Teamwork, teamwork).With(Dynasty.Players.Attribute.Leadership, leadership),
            new HiddenQualities(12, 10, 10, 5, 10), Array.Empty<HeroPoolRecord>());

        int tight = TeamCommunication.CallDelayTicks(Enumerable.Range(0, 5).Select(_ => Player(18, 18, "en")).ToList());
        int loose = TeamCommunication.CallDelayTicks(Enumerable.Range(0, 5).Select(_ => Player(6, 6, "en")).ToList());
        int split = TeamCommunication.CallDelayTicks(Enumerable.Range(0, 5).Select(i => Player(18, 18, i < 3 ? "en" : "ru")).ToList());

        Assert.True(tight < loose);
        Assert.True(tight < split, "no language everyone speaks slows the calls");
    }
}
