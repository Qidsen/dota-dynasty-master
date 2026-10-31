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
/// Pierce the Veil (Dota 7.41f data and the official description): Muerta turns ethereal (physical damage cannot hurt her) and her
/// attacks are magical; passively, her attacks on an ethereal target are magical too, so they hurt it.
/// </summary>
public class VeilTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 100, 100, 600, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5400, 5000);
        });
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("veil_passive", -1, attacksMagicalOnEthereal: true), H(ctx.State, "alpha").Id));
        return kernel;
    }

    private static List<DamageDealt> HitsBy(SimulationKernel<MatchState> kernel, string attacker) =>
        kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, attacker).Id).ToList();

    [Fact]
    public void Veiled_SheTakesNoPhysicalDamage_AndHerAttacksAreMagical()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("veil", 10000, ModifierFlags.PhysicalImmune, attacksMagical: true), H(ctx.State, "alpha").Id));
        At(kernel, 3, ctx =>
        {
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id));
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id));
        });

        kernel.RunTicks(3 + 30);

        Assert.All(HitsBy(kernel, "alpha"), h => Assert.Equal(DamageType.Magical, h.DamageType));
        Assert.Contains(HitsBy(kernel, "alpha"), h => h.Amount > Fixed.Zero);
        Assert.Equal(5000, H(kernel.State, "alpha").Hp.ToDouble(), 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Passively_HerAttacksOnAnEtherealTarget_AreMagical(bool ethereal)
    {
        var kernel = Arena();
        if (ethereal)
            At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("ghost", 10000, ModifierFlags.PhysicalImmune), H(ctx.State, "beta").Id));
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id)));

        kernel.RunTicks(3 + 30);

        DamageDealt hit = HitsBy(kernel, "alpha").First();
        Assert.Equal(ethereal ? DamageType.Magical : DamageType.Physical, hit.DamageType);
        Assert.True(hit.Amount > Fixed.Zero);
    }
}
