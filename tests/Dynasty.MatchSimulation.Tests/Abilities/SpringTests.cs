using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Psionic Trap (Dota 7.41f data and the official description; the project owner: a trap charges from when it was placed): at most
/// a number of traps, the oldest goes; Trap springs the trap nearest to the caster: enemies around it are slowed by 20% rising to 50%
/// with the trap's charge (full after 3.5 s), and a full trap also deals its damage; the trap is gone.
/// </summary>
public class SpringTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition Place = EffectDefinition.Summon("trap", -1, true, new[] { 1 }, new[] { 2 }, Array.Empty<EffectDefinition>());

    private static readonly EffectDefinition Spring = EffectDefinition.Spring("trap", 3500, 40,
        EffectDefinition.Area(400, AreaCenter.Caster, TargetTeam.Enemy,
            EffectDefinition.Modifier(new ModifierSpec("trap_slow", 5000, moveSpeedPercent: -50)),
            EffectDefinition.FullChargeDamage(DamageType.Pure, 300)));

    private static List<MatchUnit> Traps(MatchState state) => state.Units.Where(u => u.TypeId == "trap" && u.IsAlive).ToList();

    private static SimulationKernel<MatchState> Field()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("trap", new UnitStats(100, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16), new Bounty(0, 0, 0)));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 3000);
            H(ctx.State, "beta").Position = P(9000, 9000);
            H(ctx.State, "gamma").Position = P(1000, 1000);
        });
        return kernel;
    }

    private static void PlaceAt(SimulationKernel<MatchState> kernel, long tick, FixedVector2 point) =>
        At(kernel, tick, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Place }, 1, H(ctx.State, "alpha"), point));

    private static void SpringAt(SimulationKernel<MatchState> kernel, long tick) =>
        At(kernel, tick, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Spring }, 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position));

    [Fact]
    public void AtMostTheLevelsTraps_TheOldestGoes()
    {
        var kernel = Field();
        PlaceAt(kernel, 1, P(4000, 3000));
        PlaceAt(kernel, 2, P(5000, 3000));
        PlaceAt(kernel, 3, P(6000, 3000));

        kernel.RunTicks(5);

        Assert.Equal(new[] { P(5000, 3000), P(6000, 3000) }, Traps(kernel.State).Select(t => t.Position));
    }

    [Theory]
    [InlineData(4 * 30, 50, 300)]   // full: the whole slow and the damage
    [InlineData(1, 20, 0)]          // just placed: 40% of the slow, no damage
    [InlineData(53, 35, 0)]         // about half charged (53 of 105 ticks): 40% + 60% * 53 / 105 = 70% of 50
    public void Spring_SlowsByTheCharge_AndAFullTrapDamages(int age, int slow, int damage)
    {
        var kernel = Field();
        PlaceAt(kernel, 1, P(5000, 3000));
        At(kernel, 1 + age, ctx => H(ctx.State, "beta").Position = P(5200, 3000));
        SpringAt(kernel, 1 + age);

        kernel.RunTicks(3 + age);

        MatchUnit beta = H(kernel.State, "beta");
        Assert.Equal(300 * (100 - slow) / 100, beta.MoveSpeed);
        Assert.Equal(damage, (Fixed.FromInt(beta.MaxHp) - beta.Hp).ToDouble(), 0);
        Assert.Empty(Traps(kernel.State));
    }

    [Fact]
    public void Spring_TakesTheTrapNearestToTheCaster()
    {
        var kernel = Field();
        PlaceAt(kernel, 1, P(8000, 3000));
        PlaceAt(kernel, 2, P(3500, 3000));   // near alpha

        SpringAt(kernel, 5);
        kernel.RunTicks(7);

        Assert.Equal(P(8000, 3000), Traps(kernel.State).Single().Position);
    }

    /// <summary>
    /// The AI springs its nearest trap when enemies it sees are around that trap, not otherwise; it waits for the full charge unless
    /// an enemy is near the trap's edge.
    /// </summary>
    [Theory]
    [InlineData("near")]
    [InlineData("edge")]
    [InlineData("far")]
    [InlineData("none")]   // no trap at all
    public void Ai_SpringsTheNearestTrap_WhenEnemiesAreAroundIt(string trap)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("trap", new UnitStats(100, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16), new Bounty(0, 0, 0)));
        draft.Abilities.Add(new AbilityDefinition("spring", "Trap", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 500 }, new[] { 0 }, new[] { 1 },
            new[] { Spring }));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "spring" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5500, 5000);   // a fight next to alpha either way
            H(ctx.State, "beta").Hp = Fixed.FromInt(600);
            H(ctx.State, "gamma").Position = P(3000, 3000);
        });
        bool enemyNearTrap = trap is "near" or "edge";
        if (trap != "none")
            PlaceAt(kernel, 1, trap == "near" ? P(5600, 5000) : trap == "edge" ? P(5850, 5000) : P(5000, 5300));
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        foreach (string hero in new[] { "beta", "gamma" })
            settings.Uncontrolled.Add(hero);
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(5 * 30);

        Assert.Equal(enemyNearTrap, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "spring"));
        if (enemyNearTrap)
        {
            long sprung = kernel.Events.Records.First(r => r.Event is AbilityCast c && c.AbilityId == "spring").Tick.Value;
            Assert.Equal(trap == "near", sprung >= 1 + 105);   // full after 3.5 s; the enemy at the edge is caught at once
        }
        if (!enemyNearTrap)
            Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);   // it did fight
    }
}
