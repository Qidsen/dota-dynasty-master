using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Nothl Projection (Dota 7.41f data and the official description; the project owner: the body is a still copy): Dazzle's spirit goes to
/// the point, untouchable; his body stays where he was, and damage to it is damage to him - its death is his; at the end he is back in it.
/// </summary>
public class NothlProjectionTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition[] Projection =
    {
        EffectDefinition.OnCaster(EffectDefinition.Illusion(3000, new[]
        {
            new ModifierSpec("nothl_body", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed | ModifierFlags.Silenced, dispellable: false, damageToOwner: true),
        }, 1)),
        EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("nothl", 3000, ModifierFlags.Invulnerable, dispellable: false, returnsOnExpiry: true))),
        EffectDefinition.Dash(),
    };

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.FromInt(id == "alpha" ? 5 : 0), 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5600, 5000);
            H(ctx.State, "gamma").Position = P(9000, 9000);
            foreach (string id in new[] { "alpha", "beta" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", 2000, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, id).Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Projection, 1, H(ctx.State, "alpha"), P(5400, 5000)));
        return kernel;
    }

    private static MatchUnit? Body(MatchState state) => state.Units.FirstOrDefault(u => u.IsAlive && u.IllusionOf == H(state, "alpha").Id);

    [Fact]
    public void TheSpirit_GoesToThePoint_Untouchable_TheBodyStays_AndDamageToItIsHis()
    {
        var kernel = Arena();
        double spiritHit = -1;
        At(kernel, 3, ctx =>
        {
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(500), DamageType.Pure);   // the spirit: nothing
            spiritHit = (Fixed.FromInt(5000) - H(ctx.State, "alpha").Hp).ToDouble();
            Damage.Deal(ctx, H(ctx.State, "beta").Id, Body(ctx.State)!, Fixed.FromInt(1000), DamageType.Physical);
        });

        kernel.RunTicks(5);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Equal(P(5400, 5000), alpha.Position);
        Assert.Equal(P(5000, 5000), Body(kernel.State)!.Position);
        Assert.Equal(0, spiritHit, 0);
        double taken = 5000 - alpha.Hp.ToDouble();
        Assert.InRange(taken, 700, 800);   // 1000 physical through his 5 armor
        Assert.Equal(alpha.Hp.ToDouble(), Body(kernel.State)!.Hp.ToDouble(), 0);
    }

    [Fact]
    public void KillingTheBody_KillsHim()
    {
        var kernel = Arena();
        At(kernel, 3, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, Body(ctx.State)!, Fixed.FromInt(9000), DamageType.Pure));

        kernel.RunTicks(6);

        Assert.False(H(kernel.State, "alpha").IsAlive);
        Assert.Contains(kernel.EventsOf<HeroKilled>(), k => k.Victim == H(kernel.State, "alpha").Id && k.Killer == H(kernel.State, "beta").Id);
    }

    [Fact]
    public void AtTheEnd_HeIsBackInTheBody()
    {
        var kernel = Arena();

        kernel.RunTicks(2 + 90 + 3);

        Assert.True(FixedVector2.Distance(H(kernel.State, "alpha").Position, P(5000, 5000)) < Fixed.FromInt(10));   // back where the body stood
        Assert.Null(Body(kernel.State));
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "nothl");
    }
}
