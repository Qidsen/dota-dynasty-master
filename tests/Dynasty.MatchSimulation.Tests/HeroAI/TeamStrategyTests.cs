using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.Drafting;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

/// <summary>A team's strategy (from its draft) changes what its heroes do.</summary>
public class TeamStrategyTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static HeroAiSettings Calm() => new() { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };

    [Fact]
    public void LateGameStrategy_DelaysTheEvenNumbersPush()
    {
        double DistanceToEnemyTower(TeamStrategy strategy)
        {
            var draft = TestAbilities.Draft();
            draft.DawnWave.Clear();
            draft.DuskWave.Clear();
            var kernel = draft.StartWithHeroes(
                new[] { ("alpha", "mid"), ("gamma", "mid"), ("delta", "mid") },
                new[] { ("beta", "mid"), ("epsilon", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                H(ctx.State, "beta").Position = P(9000, 8000);    // alive, far away, unseen: even-ish numbers, no fight
                H(ctx.State, "epsilon").Position = P(9000, 8400);
            }));
            HeroAiSettings settings = Calm();
            settings.EvenPushFromMinute = 0;
            settings.Uncontrolled.Add("beta");
            settings.Uncontrolled.Add("epsilon");
            foreach (string hero in new[] { "alpha", "gamma", "delta" })
                settings.Personalities[hero] = new HeroPersonality { Greed = 0.3 };
            settings.DawnStrategy = strategy;
            HeroAi.Attach(kernel, settings);

            kernel.RunTicks(600);   // 20 s: long enough to walk from the fountain to either spot
            return FixedVector2.Distance(H(kernel.State, "alpha").Position, kernel.State.FindStructure("dusk.mid.t1")!.Position).ToDouble();
        }

        double balanced = DistanceToEnemyTower(new TeamStrategy());
        double lateGame = DistanceToEnemyTower(new TeamStrategy { EvenPushShiftMinutes = 9 });

        Assert.True(lateGame > balanced + 1000, $"late-game team should not push yet (balanced {balanced:0}, late {lateGame:0})");
    }

    [Fact]
    public void Aggression_DecidesWhetherAnEvenFightIsTaken()
    {
        int AlphaAttacks(int aggressionPercent)
        {
            var draft = TestAbilities.Draft();
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                H(ctx.State, "alpha").Position = P(5000, 2000);
                H(ctx.State, "beta").Position = P(5700, 2000);
                Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
            }));
            HeroAiSettings settings = Calm();
            settings.Personalities["alpha"] = new HeroPersonality { Aggression = 1.0 };
            settings.Uncontrolled.Add("beta");
            settings.DawnStrategy = new TeamStrategy { AggressionPercent = aggressionPercent };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(120);
            return kernel.EventsOf<AttackStarted>().Count(a => a.Attacker == H(kernel.State, "alpha").Id);
        }

        Assert.True(AlphaAttacks(130) > 0);
        Assert.Equal(0, AlphaAttacks(100));
    }

    [Fact]
    public void Gank_SkipsAnEnemyBesideHisTower_EvenWithOwnCreepsTankingIt()
    {
        // Lane aggro: hitting a hero within a tower's reach draws the tower even while our creeps tank it,
        // so even a team strong enough for the fight does not dive a healthy hero there.
        var draft = new RulesetDraft { HeroAggro = true };
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            ctx.State.Creeps(Team.Dusk).Single().Position = P(9000, 9000);
            ctx.State.Creeps(Team.Dawn).Single().Position = P(6650, 5000);   // tanking the Dusk tower, keeps beta in sight
            H(ctx.State, "alpha").Position = P(4800, 4000);
            H(ctx.State, "gamma").Position = P(4800, 4200);
            H(ctx.State, "beta").Position = P(6850, 5000);   // beside the tower, full health, alone
            Dynasty.MatchSimulation.Heroes.HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 2000);   // strong enough to take on beta and his tower
            Dynasty.MatchSimulation.Heroes.HeroProgression.GrantXp(ctx, H(ctx.State, "gamma"), 2000);
        }));
        HeroAiSettings settings = Calm();
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(330);   // time to walk the 2300 to beta

        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id && a.Target == H(kernel.State, "beta").Id);
    }

    [Fact]
    public void GankRange_ReachesIsolatedEnemiesFurtherAway()
    {
        double Approach(int gankRange)
        {
            var draft = TestAbilities.Draft();
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid"), ("delta", "mid") }, new[] { ("beta", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                H(ctx.State, "alpha").Position = P(3000, 3000);
                H(ctx.State, "gamma").Position = P(3000, 3200);
                H(ctx.State, "beta").Position = P(6500, 3000);   // 3500 away: outside the default gank range
                H(ctx.State, "delta").Position = P(7400, 3000);  // a scout that keeps beta in sight
            }));
            HeroAiSettings settings = Calm();
            settings.Uncontrolled.Add("beta");
            settings.Uncontrolled.Add("delta");
            settings.DawnStrategy = new TeamStrategy { GankRange = gankRange };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(90);
            return FixedVector2.Distance(H(kernel.State, "alpha").Position, H(kernel.State, "beta").Position).ToDouble();
        }

        Assert.True(Approach(4000) < Approach(3000) - 500, $"long gank range {Approach(4000):0}, default {Approach(3000):0}");
    }

    [Fact]
    public void SiegeStrategy_CallsSummonsAtATowerItsCreepsAreHitting()
    {
        bool Summoned(bool siege)
        {
            var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "swarm");
            draft.DawnWave.Add(new WaveEntry("melee", 2));   // the test kit drafts without waves
            draft.StructureTypes[0] = new StructureType("tower", TowerStats(dmg: 1));
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
            kernel.AddSystem(new LambdaSystem(1, ctx =>
            {
                H(ctx.State, "alpha").Position = P(6200, 5000);
                foreach (MatchUnit creep in ctx.State.Creeps(Team.Dawn))
                {
                    Modifiers.Apply(ctx, creep, ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
                    creep.Position = P(6700, 5000);   // tanking the Dusk mid tower
                }
            }));
            HeroAiSettings settings = Calm();
            settings.DawnStrategy = new TeamStrategy { SiegeWithSummons = siege };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(90);
            return kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "swarm");
        }

        Assert.True(Summoned(siege: true));
        Assert.False(Summoned(siege: false));
    }

    [Fact]
    public void LateGameStrategy_KeepsAGreedyCarryOutOfMarginalFights()
    {
        int CarryAttacks(int protectUntilMinute)
        {
            var draft = TestAbilities.Draft();
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                H(ctx.State, "alpha").Position = P(5000, 2000);
                H(ctx.State, "beta").Position = P(5700, 2000);
                Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
            }));
            HeroAiSettings settings = Calm();
            settings.Personalities["alpha"] = new HeroPersonality { Aggression = 1.3, Greed = 0.8 };   // odds 1.3: worth it for most
            settings.Uncontrolled.Add("beta");
            settings.DawnStrategy = new TeamStrategy { ProtectCarriesUntilMinute = protectUntilMinute };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(120);
            return kernel.EventsOf<AttackStarted>().Count(a => a.Attacker == H(kernel.State, "alpha").Id);
        }

        Assert.True(CarryAttacks(0) > 0);
        Assert.Equal(0, CarryAttacks(30));
    }

    [Fact]
    public void Strategy_FollowsTheDraftedGamePlan()
    {
        RulesetContent ruleset = BaseRuleset.Create();

        TeamStrategy push = TeamStrategy.From(GamePlan.For(ruleset, new[] { "siegewright", "wildheart", "hivecaller", "spiritwarden", "duskrunner" }));
        TeamStrategy late = TeamStrategy.From(GamePlan.For(ruleset, new[] { "starborn", "gunwright", "bulwark", "lumenpriest", "dawnmender" }));

        Assert.True(push.EvenPushShiftMinutes < 0 && push.LaningShiftMinutes < 0);
        Assert.True(late.EvenPushShiftMinutes > 0 && late.LaningShiftMinutes > 0 && late.AggressionPercent < 100);
    }
}
