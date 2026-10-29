using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Tombstone (Dota 7.41f data and official notes): every interval a zombie appears next to every enemy unit around the tombstone and
/// attacks that unit only; a zombie whose target dies or goes out of sight dies; the tombstone keeps working after its owner died.
/// </summary>
public class TombstoneTests
{
    private static readonly FixedVector2 StoneAt = P(5000, 5000);

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>A zombie next to each enemy within 1200 of the tombstone every 2 seconds.</summary>
    private static readonly ModifierSpec Stone = new ModifierSpec("tombstone", -1, periodMs: 2000, periodicFromBearer: true,
        periodicEffects: new[] { EffectDefinition.Area(1200, AreaCenter.Target, TargetTeam.Enemy, EffectDefinition.SummonNear("zombie", -1, new[] { 1 })) });

    private static List<MatchUnit> Zombies(MatchState state) => state.Units.Where(u => u.TypeId == "zombie" && u.IsAlive).ToList();

    /// <summary>alpha's tombstone at the centre; beta and delta (enemies) where given, gamma (alpha's ally) next to the stone.</summary>
    private static SimulationKernel<MatchState> Graveyard(FixedVector2 beta, FixedVector2 delta)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("stone", new UnitStats(300, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16), new Bounty(20, 20, 0)));
        draft.Creeps.Add(new CreepType("zombie", new UnitStats(200, Fixed.Zero, 300, 30, 30, 100, 1000, 300, 300, 16), new Bounty(5, 5, 0)));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(4800, 5000);
            H(ctx.State, "beta").Position = beta;
            H(ctx.State, "delta").Position = delta;
            H(ctx.State, "gamma").Position = StoneAt + FixedVector2.FromInts(0, 200);
            MatchUnit stone = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "alpha"), ctx.State.Ruleset.FindCreepType("stone")!, ctx.Tick.Value, -1);
            stone.Position = StoneAt;
            Modifiers.Apply(ctx, stone, Stone, H(ctx.State, "alpha").Id);
        });
        return kernel;
    }

    [Fact]
    public void Zombies_AppearNextToEveryEnemyAround_EveryInterval()
    {
        var kernel = Graveyard(P(5800, 5000), P(9000, 9000));   // delta far away

        kernel.RunTicks(2 * 30 + 3);
        List<MatchUnit> first = Zombies(kernel.State);
        kernel.RunTicks(2 * 30);

        Assert.Single(first);
        MatchUnit zombie = first[0];
        Assert.Equal(H(kernel.State, "alpha").Team, zombie.Team);
        Assert.Equal(2, Zombies(kernel.State).Count);
        Assert.True(FixedVector2.Distance(Zombies(kernel.State)[1].Position, H(kernel.State, "beta").Position) < Fixed.FromInt(200));
    }

    [Fact]
    public void Zombie_AttacksOnlyItsTarget_EvenWithAnotherEnemyCloser()
    {
        var kernel = Graveyard(P(5600, 5000), P(9000, 9000));
        At(kernel, 2 * 30 + 3, ctx =>
        {
            H(ctx.State, "delta").Position = Zombies(ctx.State)[0].Position + FixedVector2.FromInts(0, 30);   // delta right next to it
            H(ctx.State, "beta").Position = P(6200, 5000);   // beta beyond its acquisition range, still in sight
        });

        kernel.RunTicks(4 * 30);

        MatchUnit zombie = Zombies(kernel.State)[0];
        Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == zombie.Id && a.Target == H(kernel.State, "beta").Id);
        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Attacker == zombie.Id && a.Target == H(kernel.State, "delta").Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Zombie_Dies_WhenItsTargetDiesOrGoesOutOfSight(bool dies)
    {
        var kernel = Graveyard(P(5600, 5000), P(9000, 9000));
        At(kernel, 2 * 30 + 5, ctx =>
        {
            MatchUnit beta = H(ctx.State, "beta");
            if (dies)
                Damage.Execute(ctx, H(ctx.State, "gamma").Id, beta);
            else
                Modifiers.Apply(ctx, beta, ModifierSpec.Status("invisible", 10000, ModifierFlags.Invisible), beta.Id);
        });

        kernel.RunTicks(2 * 30 + 4);
        Assert.Single(Zombies(kernel.State));
        kernel.RunTicks(4);

        Assert.Empty(Zombies(kernel.State));
    }

    [Fact]
    public void Tombstone_KeepsRaisingZombies_AfterItsOwnerDied()
    {
        var kernel = Graveyard(P(5600, 5000), P(9000, 9000));
        At(kernel, 1, ctx => Damage.Execute(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha")));

        kernel.RunTicks(2 * 30 + 3);

        Assert.Single(Zombies(kernel.State));
    }
}
