using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.Abilities.TestAbilities;

namespace Dynasty.MatchSimulation.Tests.Abilities;

public class PassiveAndLearningTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    /// <summary>Alpha (with the given abilities) attacks a rooted enemy creep standing next to it.</summary>
    private static SimulationKernel<MatchState> AttackCreep(params string[] abilities)
    {
        var draft = Draft();
        draft.DuskWave.Add(new WaveEntry("melee", 1));
        draft.Creeps[0] = new CreepType("melee", RulesetDraft.MeleeStats(hp: 5000, armor: 0, dmgMin: 1, dmgMax: 1), new Bounty(40, 40, 60));
        draft.WithHeroAbilities("alpha", abilities);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            MatchUnit creep = ctx.State.Creeps(Team.Dusk).First();
            Modifiers.Apply(ctx, creep, ModifierSpec.Status("root", -1, ModifierFlags.Rooted), EntityId.None);
            H(ctx.State, "alpha").Position = creep.Position - FixedVector2.FromInts(100, 0);
        }));
        // Ordered once the tick-3 vision update has seen the creep next to the hero.
        kernel.AddSystem(new LambdaSystem(4, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(ctx.State.Creeps(Team.Dusk).First().Id))));
        return kernel;
    }

    private static List<DamageDealt> AlphaHits(SimulationKernel<MatchState> kernel) =>
        kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id).ToList();

    [Fact]
    public void CritPassive_MultipliesAttackDamage()
    {
        var kernel = AttackCreep("fury");

        kernel.RunTicks(200);

        var hits = AlphaHits(kernel);
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.InRange(h.Amount.ToDouble(), 100, 108));   // (50..54) × 2, creep armor 0
        Assert.All(hits, h => Assert.True(h.IsCritical));
    }

    [Fact]
    public void AttackSpeedPassive_ShortensTheAttackInterval()
    {
        var kernel = AttackCreep("frenzy");

        kernel.RunTicks(200);

        var starts = kernel.TimedEventsOf<AttackStarted>().Where(e => e.Event.Attacker == H(kernel.State, "alpha").Id).Select(e => e.Tick).ToList();
        Assert.Equal(23, starts[1] - starts[0]);   // 1500 ms / 2 = 750 ms ≈ 23 ticks
    }

    [Fact]
    public void LifestealPassive_HealsTheAttacker()
    {
        var kernel = AttackCreep("leech");
        kernel.AddSystem(new LambdaSystem(1, ctx => H(ctx.State, "alpha").Hp = Fixed.FromInt(100)));

        kernel.RunTicks(60);

        double dealt = AlphaHits(kernel).Sum(h => h.Amount.ToDouble());
        double hp = H(kernel.State, "alpha").Hp.ToDouble();
        Assert.True(dealt > 0);
        Assert.True(hp >= 100 + dealt * 0.5 - 1, $"hp {hp} after dealing {dealt}");
    }

    [Fact]
    public void StructureDamagePassive_IncreasesDamageAgainstStructuresOnly()
    {
        var draft = Draft();
        draft.WithHeroAbilities("alpha", "siegecraft");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            MatchUnit tower = ctx.State.FindStructure("dusk.mid.t1")!;
            H(ctx.State, "alpha").Position = tower.Position - FixedVector2.FromInts(200, 0);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(tower.Id));
        }));

        kernel.RunTicks(100);

        var hits = AlphaHits(kernel);
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.InRange(h.Amount.ToDouble(), 100, 108));   // tower armor 0
    }

    [Fact]
    public void Passives_SurviveDeathAndRespawn()
    {
        var draft = Draft();
        draft.WithHeroAbilities("alpha", "frenzy");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(9999), DamageType.Pure)));

        kernel.RunTicks(300);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.True(alpha.IsAlive);
        Assert.Equal(100, alpha.AttackSpeedBonus);
    }

    [Fact]
    public void SkillPoints_FollowPriorityAndLevelRequirements()
    {
        var draft = Draft();
        draft.WithHeroAbilities("alpha", "bolt", "quake", "finale");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        kernel.AddSystem(new LambdaSystem(0, ctx => HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 2000)));

        kernel.RunTicks(2);   // skill points from the tick-0 level-ups are spent on tick 1

        HeroState hero = H(kernel.State, "alpha").Hero!;
        Assert.Equal(6, hero.Level);
        Assert.Equal(3, hero.AbilityLevel("bolt"));
        Assert.Equal(2, hero.AbilityLevel("quake"));
        Assert.Equal(1, hero.AbilityLevel("finale"));
        Assert.Equal(6, kernel.EventsOf<AbilityLearned>().Count());
    }

    [Fact]
    public void Validation_RejectsUnknownAbilityReference()
    {
        var draft = Draft();
        draft.WithHeroAbilities("alpha", "teleport");

        Assert.Contains(draft.Build().Validate(), e => e.Contains("teleport"));
    }

    [Fact]
    public void Validation_RejectsSummonOfUnknownType()
    {
        var draft = Draft();
        draft.Abilities.Add(new AbilityDefinition("ghosts", "Ghosts", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
            new[] { 1000 }, new[] { 10 }, new[] { 1 }, new[] { EffectDefinition.Summon("ghost", 1000, 1) }));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("ghost"));
    }

    [Fact]
    public void Fingerprint_ChangesWhenAnAbilityValueChanges()
    {
        var buffed = Draft();
        buffed.Abilities[0] = new AbilityDefinition("bolt", "Bolt", AbilityTargeting.Unit, TargetTeam.Enemy, 600, 300,
            new[] { 8000, 7000, 6000, 5000 }, new[] { 100, 110, 120, 130 }, new[] { 1, 3, 5, 7 },
            new[] { EffectDefinition.Damage(DamageType.Magical, 101, 150, 200, 250) });

        Assert.NotEqual(Draft().Build().ToDescriptor().Fingerprint, buffed.Build().ToDescriptor().Fingerprint);
    }
}
