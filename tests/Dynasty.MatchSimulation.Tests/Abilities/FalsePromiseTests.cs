using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// False Promise (Dota 7.41f data and official notes; the project owner: at the end the healing first, then the damage): healing and
/// damage are held off until it ends; the healing is amplified; the damage is reduced by armor and resistance as they are then.
/// </summary>
public class FalsePromiseTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Promise = new ModifierSpec("false_promise", 2000, delaysHealth: true, delayedHealAmpPercent: 100);

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(9000, 9000);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(1000);
        });
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Promise, H(ctx.State, "alpha").Id));
        return kernel;
    }

    private static void Hurt(SimulationKernel<MatchState> kernel, long tick, int amount, DamageType type = DamageType.Pure) =>
        At(kernel, tick, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(amount), type));

    private static void Mend(SimulationKernel<MatchState> kernel, long tick, int amount) =>
        At(kernel, tick, ctx => Damage.Heal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "alpha"), Fixed.FromInt(amount)));

    [Fact]
    public void HealingAndDamage_WaitForTheEnd_TheHealingDoubledFirst()
    {
        var kernel = Arena();
        Hurt(kernel, 10, 1500);   // more than it has: no death meanwhile
        Mend(kernel, 20, 300);
        double meanwhile = 0;
        At(kernel, 40, ctx => meanwhile = H(ctx.State, "alpha").Hp.ToDouble());

        kernel.RunTicks(2 + 60 + 3);

        Assert.Equal(1000, meanwhile, 0);
        Assert.True(H(kernel.State, "alpha").IsAlive);
        Assert.Equal(1000 + 600 - 1500, H(kernel.State, "alpha").Hp.ToDouble(), 0);   // healed to 1600 first, then 1500 damage
    }

    [Fact]
    public void TheDamage_KillsAtTheEnd_CreditingWhoDealtIt()
    {
        var kernel = Arena();
        Hurt(kernel, 10, 1200);

        kernel.RunTicks(2 + 60 + 3);

        Assert.False(H(kernel.State, "alpha").IsAlive);
        Assert.Contains(kernel.EventsOf<DamageDealt>(), d => d.Target == H(kernel.State, "alpha").Id && d.Attacker == H(kernel.State, "beta").Id && d.TargetHpAfter <= Fixed.Zero);
    }

    [Fact]
    public void TheDamage_IsReducedByTheResistanceAsItIsAtTheEnd()
    {
        var kernel = Arena();
        Hurt(kernel, 10, 400, DamageType.Magical);
        At(kernel, 50, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("ward", 10000, magicResistBonus: 50), H(ctx.State, "alpha").Id));

        kernel.RunTicks(2 + 60 + 3);

        Assert.Equal(1000 - 200, H(kernel.State, "alpha").Hp.ToDouble(), 0);
    }
}
