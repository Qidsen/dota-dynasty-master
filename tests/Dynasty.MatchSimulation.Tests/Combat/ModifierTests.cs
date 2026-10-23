using Dynasty.Core.Ids;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

public class ModifierTests
{
    private static RulesetDraft LoneDawnCreep()
    {
        var draft = new RulesetDraft();
        draft.DuskWave.Clear();
        return draft;
    }

    private static void ApplyAt(Dynasty.Core.Kernel.SimulationKernel<MatchState> kernel, long tick, Func<MatchState, MatchUnit> target, ModifierSpec spec) =>
        kernel.AddSystem(new LambdaSystem(tick, ctx => Modifiers.Apply(ctx, target(ctx.State), spec, EntityId.None)));

    private static MatchUnit FirstDawnCreep(MatchState s) => s.Creeps(Team.Dawn).First();

    [Fact]
    public void Stun_FreezesMovementForItsDuration()
    {
        var kernel = LoneDawnCreep().Start();
        ApplyAt(kernel, 0, FirstDawnCreep, ModifierSpec.Status("stun", 1000, ModifierFlags.Stunned));

        kernel.RunTicks(30);
        Assert.Equal(P(1010, 5000), FirstDawnCreep(kernel.State).Position);

        kernel.Step();
        Assert.Equal(P(1020, 5000), FirstDawnCreep(kernel.State).Position);
    }

    [Fact]
    public void Root_StopsMovement()
    {
        var kernel = LoneDawnCreep().Start();
        ApplyAt(kernel, 0, FirstDawnCreep, ModifierSpec.Status("root", 5000, ModifierFlags.Rooted));

        kernel.RunTicks(60);

        Assert.Equal(P(1010, 5000), FirstDawnCreep(kernel.State).Position);
    }

    [Fact]
    public void Slow_ReducesMoveSpeedProportionally()
    {
        var kernel = LoneDawnCreep().Start();
        ApplyAt(kernel, 0, FirstDawnCreep, new ModifierSpec("slow", 5000, moveSpeedPercent: -50));

        kernel.RunTicks(31);   // tick 0 at full speed, then 30 ticks at half speed

        Assert.Equal(P(1160, 5000), FirstDawnCreep(kernel.State).Position);
    }

    [Fact]
    public void Stun_InterruptsAttackWindUp()
    {
        var kernel = new RulesetDraft().Start();
        EntityId attacker = EntityId.None;
        kernel.AddSystem(new TriggerSystem(
            _ => kernel.EventsOf<AttackStarted>().Any(),
            ctx =>
            {
                attacker = kernel.EventsOf<AttackStarted>().First().Attacker;
                Modifiers.Apply(ctx, ctx.State.Find(attacker)!, ModifierSpec.Status("stun", 2000, ModifierFlags.Stunned), EntityId.None);
            }));

        kernel.StepUntil(() => attacker.IsValid, 2000);
        kernel.RunTicks(40);

        Assert.DoesNotContain(kernel.EventsOf<DamageDealt>(), d => d.Attacker == attacker);
    }

    [Fact]
    public void ArmorBonus_ChangesEffectiveArmorUntilExpiry()
    {
        var kernel = LoneDawnCreep().Start();
        ApplyAt(kernel, 0, FirstDawnCreep, new ModifierSpec("plating", 1000, armorBonus: Fixed.FromInt(5)));

        kernel.Step();
        Assert.Equal(Fixed.FromInt(7), FirstDawnCreep(kernel.State).Armor);

        kernel.RunTicks(30);
        Assert.Equal(Fixed.FromInt(2), FirstDawnCreep(kernel.State).Armor);
    }

    [Fact]
    public void Reapplying_RefreshesDurationInsteadOfStacking()
    {
        var kernel = LoneDawnCreep().Start();
        var plating = new ModifierSpec("plating", 1000, armorBonus: Fixed.FromInt(5));
        ApplyAt(kernel, 0, FirstDawnCreep, plating);
        ApplyAt(kernel, 20, FirstDawnCreep, plating);

        kernel.RunTicks(40);

        MatchUnit creep = FirstDawnCreep(kernel.State);
        Assert.Equal(Fixed.FromInt(7), creep.Armor);
        Assert.Single(creep.Modifiers);
    }

    [Fact]
    public void DamageOverTime_DealsItsTotalAndCreditsTheSource()
    {
        var draft = LoneDawnCreep();
        draft.Creeps[0] = new CreepType("melee", MeleeStats(hp: 1000));
        var kernel = draft.Start();
        var burn = new ModifierSpec("burn", 2000, damagePerSecond: Fixed.FromInt(100), damageType: DamageType.Magical);
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            MatchUnit tower = ctx.State.FindStructure("dusk.mid.t1")!;
            Modifiers.Apply(ctx, FirstDawnCreep(ctx.State), burn, tower.Id);
        }));

        kernel.RunTicks(100);

        MatchUnit creep = FirstDawnCreep(kernel.State);
        Assert.InRange(creep.Hp.ToDouble(), 799.9, 800.1);
        Assert.All(kernel.EventsOf<DamageDealt>(), d => Assert.Equal(DamageType.Magical, d.DamageType));
        Assert.All(kernel.EventsOf<DamageDealt>(), d => Assert.Equal(kernel.State.FindStructure("dusk.mid.t1")!.Id, d.Attacker));
    }

    [Fact]
    public void ApplyAndExpiry_EmitEvents()
    {
        var kernel = LoneDawnCreep().Start();
        ApplyAt(kernel, 0, FirstDawnCreep, ModifierSpec.Status("stun", 500, ModifierFlags.Stunned));

        kernel.RunTicks(20);

        ModifierApplied applied = Assert.Single(kernel.EventsOf<ModifierApplied>());
        Assert.Equal("stun", applied.Key);
        (long tick, ModifierRemoved removed) = Assert.Single(kernel.TimedEventsOf<ModifierRemoved>());
        Assert.Equal("stun", removed.Key);
        Assert.Equal(15, tick);
    }
}
