using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Multicast (Dota 7.41f data and official notes; the project owner: the extra casts go to other targets within the cast range,
/// Fireblast's to the same one): unit-targeted abilities and items are cast again by chance (four, three, then two times), the chances
/// growing with strength.
/// </summary>
public class MulticastTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena(int twice, int thrice, int fourTimes, int strength = 0, bool sameTarget = false, int enemies = 3)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("ignite", "Ignite", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0, new[] { 5000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 10) }, multicastSameTarget: sameTarget));
        draft.Abilities.Add(new AbilityDefinition("multicast", "Multicast", AbilityTargeting.Passive, TargetTeam.Ally, 0, 0, new[] { 0 }, new int[0], new[] { 1 },
            Array.Empty<EffectDefinition>(), passiveModifiers: new[]
            {
                new ModifierSpec("multicast", -1, multicastHundredths: new[] { twice * 100, thrice * 100, fourTimes * 100 }, multicastStrengthPerPercent: 16),
            }));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "ignite", "multicast" } : null,
                attributes: id == "alpha" && strength > 0
                    ? new HeroAttributes(PrimaryAttribute.Strength, strength, 0, 0, Fixed.Zero, Fixed.Zero, Fixed.Zero)
                    : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5500, 5000);
            H(ctx.State, "gamma").Position = enemies >= 2 ? P(5000, 5500) : P(9000, 9000);
            H(ctx.State, "delta").Position = enemies >= 3 ? P(4500, 5000) : P(9000, 8000);
            H(ctx.State, "alpha").Hero!.Level = 2;
            HeroProgression.LearnSkills(ctx, H(ctx.State, "alpha"));
        });
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("ignite", H(ctx.State, "beta").Id)));
        return kernel;
    }

    private static List<DamageDealt> Hits(SimulationKernel<MatchState> kernel) =>
        kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id).ToList();

    [Theory]
    [InlineData(100, 0, 0, 2)]
    [InlineData(100, 100, 0, 3)]
    [InlineData(100, 100, 100, 4)]
    [InlineData(0, 0, 0, 1)]
    public void TheSpell_IsCastAgain_ByChance(int twice, int thrice, int fourTimes, int casts)
    {
        var kernel = Arena(twice, thrice, fourTimes);

        kernel.RunTicks(8);

        Assert.Equal(casts, Hits(kernel).Count);
    }

    [Fact]
    public void TheExtraCasts_GoToOtherTargetsInRange()
    {
        var kernel = Arena(100, 100, 0);

        kernel.RunTicks(8);

        Assert.Equal(3, Hits(kernel).Select(h => h.Target).Distinct().Count());
    }

    [Fact]
    public void WithNoOtherTarget_TheyGoToTheSameOne()
    {
        var kernel = Arena(100, 100, 0, enemies: 1);

        kernel.RunTicks(8);

        Assert.All(Hits(kernel), h => Assert.Equal(H(kernel.State, "beta").Id, h.Target));
        Assert.Equal(3, Hits(kernel).Count);
    }

    [Fact]
    public void AFireblast_GoesToTheSameTarget()
    {
        var kernel = Arena(100, 100, 0, sameTarget: true);

        kernel.RunTicks(8);

        Assert.All(Hits(kernel), h => Assert.Equal(H(kernel.State, "beta").Id, h.Target));
    }

    [Fact]
    public void Strength_AddsToTheChance()
    {
        var kernel = Arena(1, 0, 0, strength: 1600);   // 1% + 100%

        kernel.RunTicks(8);

        Assert.Equal(2, Hits(kernel).Count);
    }

    /// <summary>Each extra cast takes a target not yet struck; once all are, the first target again.</summary>
    [Fact]
    public void TheExtraCasts_TakeEachTargetOnce_ThenTheFirstAgain()
    {
        var kernel = Arena(100, 100, 100);   // four casts, three enemies

        kernel.RunTicks(8);

        List<DamageDealt> hits = Hits(kernel);
        Assert.Equal(2, hits.Count(h => h.Target == H(kernel.State, "beta").Id));
        Assert.Equal(1, hits.Count(h => h.Target == H(kernel.State, "gamma").Id));
        Assert.Equal(1, hits.Count(h => h.Target == H(kernel.State, "delta").Id));
    }
}
