using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Drums of Slom (Beastmaster's Aghanim's Scepter; Dota 7.41f data and the official description): attacks by Beastmaster or his units
/// near him bang the drum - damage around him, healing him and his units by a share of it - no more often than the interval, which
/// shortens with every attack from 3 s down to 0.5 s after 20; without attacks it lengthens again.
/// </summary>
public class DrumsOfSlomTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Drums = new ModifierSpec("drums", -1, dispellable: false, drum: new DrumSpec(525, 70, 20, 5, 500, 3000, 20, 1000, 1200));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5300, 5000);
            H(ctx.State, "gamma").Position = P(9000, 9000);
            H(ctx.State, "alpha").Hp = Fixed.FromInt(1000);
            foreach (string id in new[] { "alpha", "beta" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, id).Id);
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), Drums, H(ctx.State, "alpha").Id);
        });
        return kernel;
    }

    private static int Bangs(SimulationKernel<MatchState> kernel) =>
        kernel.EventsOf<DamageDealt>().Count(d => d.Attacker == H(kernel.State, "alpha").Id && d.Target == H(kernel.State, "beta").Id && d.DamageType == DamageType.Magical);

    private static void Hit(SimulationContext<MatchState> ctx, MatchUnit attacker) =>
        AttackSystem.ApplyHit(ctx, attacker, H(ctx.State, "beta"), Fixed.FromInt(1), critical: false);

    [Fact]
    public void AnAttack_BangsTheDrum_DamagingAround_AndHealingByAShare_NotAgainWithinTheInterval()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Hit(ctx, H(ctx.State, "alpha")));
        At(kernel, 10, ctx => Hit(ctx, H(ctx.State, "alpha")));   // within the interval (about 2.9 s now)

        kernel.RunTicks(12);

        Assert.Equal(1, Bangs(kernel));
        Assert.Equal(1000 + 70 * 20 / 100, H(kernel.State, "alpha").Hp.ToDouble(), 0);
    }

    [Fact]
    public void TwentyAttacks_ShortenTheIntervalToHalfASecond()
    {
        var kernel = Arena();
        At(kernel, 2, ctx =>
        {
            for (int i = 0; i < 20; i++)
                Hit(ctx, H(ctx.State, "alpha"));
        });
        At(kernel, 2 + 16, ctx => Hit(ctx, H(ctx.State, "alpha")));   // 0.53 s later: past the half second

        kernel.RunTicks(2 + 18);

        Assert.Equal(2, Bangs(kernel));
    }

    [Fact]
    public void WithoutAttacks_TheCountGoesDown_OneASecond()
    {
        var kernel = Arena();
        At(kernel, 2, ctx =>
        {
            for (int i = 0; i < 20; i++)
                Hit(ctx, H(ctx.State, "alpha"));
        });

        kernel.RunTicks(2 + 5 * 30 + 1);

        Assert.Equal(15, H(kernel.State, "alpha").Modifiers.Single(m => m.Spec.Key == "drums").DrumStacks);
    }

    [Fact]
    public void HisUnitsAttacks_BangItToo()
    {
        var kernel = Arena();
        MatchUnit? boar = null;
        At(kernel, 1, ctx =>
        {
            boar = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "alpha"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, -1);
            boar.Position = P(5100, 5100);
        });
        At(kernel, 2, ctx => Hit(ctx, boar!));

        kernel.RunTicks(4);

        Assert.Equal(1, Bangs(kernel));
    }
}
