using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using Dynasty.Players;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.HeroAI;

/// <summary>The player on a hero sets how the hero AI plays (spec §14, §38: swapping a player changes decisions).</summary>
public class PlayerSkillTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static PlayerProfile Player(int level, string hero = "alpha", int mastery = 80, Func<AttributeSet, AttributeSet>? tweak = null)
    {
        AttributeSet attributes = AttributeSet.Uniform(level);
        if (tweak != null)
            attributes = tweak(attributes);
        return new PlayerProfile("p", "p", "p", "", 2000, new[] { "en" }, "", new[] { 90, 50, 50, 50, 50 }, attributes,
            new HiddenQualities(level, 10, 10, 5, 10), new[] { new HeroPoolRecord(hero, mastery, 60, 60, 80, 60, 70) });
    }

    [Fact]
    public void Skill_FollowsTheAttributesAndTheHeroReadiness()
    {
        HeroSkill fast = HeroSkill.FromPlayer(Player(18), "alpha");
        HeroSkill slow = HeroSkill.FromPlayer(Player(6), "alpha");
        HeroSkill offHero = HeroSkill.FromPlayer(Player(18), "beta");   // not in the pool

        Assert.True(fast.DecisionIntervalTicks < slow.DecisionIntervalTicks);
        Assert.True(fast.LastHitMissPercent < slow.LastHitMissPercent);
        Assert.True(fast.SpellHesitationPercent < slow.SpellHesitationPercent);
        Assert.True(fast.MisjudgementPercent < slow.MisjudgementPercent);
        Assert.True(fast.LastHitMissPercent < offHero.LastHitMissPercent, "an unfamiliar hero plays worse");
    }

    [Fact]
    public void Fatigue_MakesTheSamePlayerPlayWorse()
    {
        HeroSkill fresh = HeroSkill.FromPlayer(Player(14), "alpha");
        HeroSkill tired = HeroSkill.FromPlayer(Player(14), "alpha", fatigue: 80);

        Assert.True(tired.MisjudgementPercent > fresh.MisjudgementPercent);
        Assert.True(tired.ReactionJitterTicks > fresh.ReactionJitterTicks);
        Assert.True(tired.FightFumblePercent > fresh.FightFumblePercent);
    }

    [Fact]
    public void LastHitSkill_ChangesHowManyCreepsAnUncontestedCarryGets()
    {
        int LastHits(PlayerProfile player)
        {
            int total = 0;
            foreach (ulong seed in new ulong[] { 1, 2 })
            {
                var lineup = new MatchLineup(new[] { new HeroPick("ironvow", "mid") }, Array.Empty<HeroPick>());
                var kernel = Match.Create(BaseRuleset.Create(), seed, lineup);
                var settings = new HeroAiSettings();
                settings.SkillsOf(Team.Dawn)["ironvow"] = HeroSkill.FromPlayer(player, "ironvow");
                HeroAi.Attach(kernel, settings);
                kernel.RunTicks(6 * 60 * 30);
                total += kernel.State.Heroes.Single().Hero!.LastHits;
            }

            return total;
        }

        int elite = LastHits(Player(18, "ironvow", 95));
        int poor = LastHits(Player(4, "ironvow", 30));
        Assert.True(elite > poor * 1.3, $"last hits elite {elite}, poor {poor}");
    }

    private static SimulationKernel<MatchState> Duel(HeroSkill skill, string abilities = "bolt")
    {
        var draft = TestAbilities.Draft().WithHeroAbilities("alpha", abilities);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 2000);
            H(ctx.State, "beta").Position = P(5450, 2000);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
            H(ctx.State, "beta").Hp = Fixed.FromInt(300);   // a fight worth taking
        }));
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("beta");
        settings.SkillsOf(Team.Dawn)["alpha"] = skill;
        HeroAi.Attach(kernel, settings);
        return kernel;
    }

    [Fact]
    public void SpellHesitation_DelaysTheFirstCast()
    {
        long FirstCast(int hesitation)
        {
            var kernel = Duel(new HeroSkill { SpellHesitationPercent = hesitation, ReactionJitterTicks = 0 });
            kernel.RunTicks(150);
            return kernel.Events.Records.Where(r => r.Event is AbilityCast).Select(r => r.Tick.Value).DefaultIfEmpty(9999).First();
        }

        Assert.True(FirstCast(85) > FirstCast(0), $"hesitant {FirstCast(85)}, decisive {FirstCast(0)}");
    }

    [Fact]
    public void FightFumbles_CostAttacks()
    {
        int Attacks(int fumble)
        {
            var kernel = Duel(new HeroSkill { FightFumblePercent = fumble, ReactionJitterTicks = 0 }, abilities: "mend");
            kernel.RunTicks(240);
            EntityId alpha = H(kernel.State, "alpha").Id;
            return kernel.EventsOf<AttackStarted>().Count(a => a.Attacker == alpha);
        }

        Assert.True(Attacks(80) < Attacks(0), $"fumbling {Attacks(80)}, clean {Attacks(0)}");
    }

    [Fact]
    public void Reaction_SetsHowFastTheHeroAnswersASurprise()
    {
        long ReactionTicks(int interval)
        {
            var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "bolt");
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                H(ctx.State, "alpha").Position = P(5000, 2000);
                H(ctx.State, "beta").Position = P(9000, 8000);   // far away, unseen
            }));
            kernel.AddSystem(new LambdaSystem(61, ctx =>
            {
                H(ctx.State, "beta").Position = P(5400, 2000);   // appears next to alpha
                H(ctx.State, "beta").Hp = Fixed.FromInt(200);
                Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
            }));
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
            settings.Uncontrolled.Add("beta");
            settings.SkillsOf(Team.Dawn)["alpha"] = new HeroSkill { DecisionIntervalTicks = interval, ReactionJitterTicks = 0 };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(150);
            EntityId alpha = H(kernel.State, "alpha").Id;
            long first = kernel.Events.Records.Where(r => (r.Event is AbilityCast c && c.Caster == alpha) || (r.Event is AttackStarted a && a.Attacker == alpha))
                .Select(r => r.Tick.Value).DefaultIfEmpty(9999).First();
            return first - 61;
        }

        Assert.True(ReactionTicks(2) < ReactionTicks(12), $"fast {ReactionTicks(2)}, slow {ReactionTicks(12)}");
    }

    [Fact]
    public void InitiationPatience_DecidesWhetherAnAreaUltimateIsSpentOnOneHero()
    {
        bool CastOnOne(bool patient)
        {
            var draft = TestAbilities.Draft().WithHeroAbilities("alpha", "finale");
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 2000);
                H(ctx.State, "alpha").Position = P(5000, 2000);
                H(ctx.State, "beta").Position = P(5300, 2000);    // in Finale's 500 radius
                H(ctx.State, "gamma").Position = P(6000, 2600);   // seen, but outside the radius
                Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
                Modifiers.Apply(ctx, H(ctx.State, "gamma"), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
            }));
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
            settings.Uncontrolled.Add("beta");
            settings.Uncontrolled.Add("gamma");
            settings.Personalities["alpha"] = new HeroPersonality { Aggression = 2.0 };
            settings.SkillsOf(Team.Dawn)["alpha"] = new HeroSkill { InitiationPatience = patient, ReactionJitterTicks = 0 };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(60);
            return kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "finale");
        }

        Assert.True(CastOnOne(patient: false));
        Assert.False(CastOnOne(patient: true));
    }

    [Fact]
    public void FocusDiscipline_DecidesWhetherTheTeamsFocusTargetIsFollowed()
    {
        int HitsOnTheWeakest(int discipline)
        {
            var draft = TestAbilities.Draft();
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("epsilon", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                H(ctx.State, "alpha").Position = P(5000, 2000);
                H(ctx.State, "gamma").Position = P(5000, 2300);
                H(ctx.State, "beta").Position = P(5200, 2000);      // nearest to alpha
                H(ctx.State, "epsilon").Position = P(5500, 2500);   // weakest: the team's focus
                H(ctx.State, "epsilon").Hp = Fixed.FromInt(250);
                Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
                Modifiers.Apply(ctx, H(ctx.State, "epsilon"), ModifierSpec.Status("test.pin", -1, ModifierFlags.Rooted), EntityId.None);
            }));
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
            settings.Uncontrolled.Add("beta");
            settings.Uncontrolled.Add("epsilon");
            settings.SkillsOf(Team.Dawn)["alpha"] = new HeroSkill { FocusDisciplinePercent = discipline, ReactionJitterTicks = 0 };
            settings.Personalities["alpha"] = new HeroPersonality { Aggression = 2.0 };   // fight now
            settings.Personalities["gamma"] = new HeroPersonality { Aggression = 2.0 };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(60);
            EntityId alpha = H(kernel.State, "alpha").Id;
            EntityId weakest = H(kernel.State, "epsilon").Id;
            return kernel.EventsOf<AttackStarted>().Count(a => a.Attacker == alpha && a.Target == weakest);
        }

        Assert.True(HitsOnTheWeakest(100) > HitsOnTheWeakest(0));
    }

    [Fact]
    public void DangerSense_PullsAHurtLoneHeroBackFromDeepWhenEnemiesAreMissing()
    {
        double DistanceToOwnFountain(bool danger)
        {
            var draft = TestAbilities.Draft();
            // A rune deep on the enemy side (but outside its tower's range) is a reason to go there.
            draft.Runes = new RuneRules(new[] { P(5800, 4000) }, firstSpawnMs: 0, intervalMs: 60000,
                new[] { new RuneType("haste", new ModifierSpec("rune.haste", 20000, moveSpeedPercent: 40)) });
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid"), ("delta", "mid") });
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                H(ctx.State, "alpha").Position = P(5300, 4300);   // near the Dusk tower, alone
                H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp * 60 / 100);
                H(ctx.State, "beta").Position = P(9000, 8000);    // alive, unseen
                H(ctx.State, "gamma").Position = P(9000, 8300);
                H(ctx.State, "delta").Position = P(9000, 8600);
            }));
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
            foreach (string enemy in new[] { "beta", "gamma", "delta" })
                settings.Uncontrolled.Add(enemy);
            settings.SkillsOf(Team.Dawn)["alpha"] = new HeroSkill { DangerSense = danger, ReactionJitterTicks = 0 };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(60);
            return FixedVector2.Distance(H(kernel.State, "alpha").Position, kernel.State.Ruleset.Map.FountainOf(Team.Dawn)!.Position).ToDouble();
        }

        Assert.True(DistanceToOwnFountain(true) < DistanceToOwnFountain(false) - 300, $"aware {DistanceToOwnFountain(true):0}, unaware {DistanceToOwnFountain(false):0}");
    }

    [Fact]
    public void RetreatShift_SetsWhenAHurtHeroLeaves()
    {
        double DistanceToOwnFountain(int shift)
        {
            var draft = TestAbilities.Draft();
            var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
            kernel.AddSystem(new LambdaSystem(0, ctx =>
            {
                H(ctx.State, "alpha").Position = P(2000, 5000);   // behind the own tower: lane is right, home is left
                H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp * 35 / 100);   // 35%: above the usual 30% threshold
            }));
            var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
            settings.Personalities["alpha"] = new HeroPersonality { RetreatHpPercent = 30 };
            settings.SkillsOf(Team.Dawn)["alpha"] = new HeroSkill { RetreatShiftPercent = shift, ReactionJitterTicks = 0 };
            HeroAi.Attach(kernel, settings);
            kernel.RunTicks(90);
            return FixedVector2.Distance(H(kernel.State, "alpha").Position, kernel.State.Ruleset.Map.FountainOf(Team.Dawn)!.Position).ToDouble();
        }

        Assert.True(DistanceToOwnFountain(10) < DistanceToOwnFountain(-5) - 300);
    }

    private sealed class EveryTickSystem : ISimulationSystem<MatchState>
    {
        private readonly Action<SimulationContext<MatchState>> _action;
        public EveryTickSystem(Action<SimulationContext<MatchState>> action) => _action = action;
        public string Name => "test-every-tick";
        public void Tick(SimulationContext<MatchState> context) => _action(context);
    }
}
