using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Simulation;

public class CombatTests
{
    private static readonly Fixed Factor = Fixed.FromRatio(6, 100);

    [Fact]
    public void ArmorMultiplier_IsOneWithoutArmor()
    {
        Assert.Equal(Fixed.One, CombatMath.ArmorMultiplier(Fixed.Zero, Factor));
    }

    [Fact]
    public void ArmorMultiplier_ReducesDamageForPositiveArmor()
    {
        // 1 - 0.12 / 1.12 = 0.892857...
        Assert.InRange(CombatMath.ArmorMultiplier(Fixed.FromInt(2), Factor).ToDouble(), 0.8928, 0.8929);
    }

    [Fact]
    public void ArmorMultiplier_AmplifiesDamageForNegativeArmor()
    {
        // 1 + 0.12 / 1.12 = 1.107142...
        Assert.InRange(CombatMath.ArmorMultiplier(Fixed.FromInt(-2), Factor).ToDouble(), 1.1071, 1.1072);
    }

    [Fact]
    public void OpposingCreeps_MeetAndDealArmorReducedDamage()
    {
        var kernel = new RulesetDraft().Start();

        kernel.StepUntil(() => kernel.EventsOf<DamageDealt>().Any(), 2000);

        DamageDealt hit = kernel.EventsOf<DamageDealt>().First();
        Assert.InRange(hit.Amount.ToDouble(), 17.857, 17.858);   // 20 dmg vs armor 2
        Assert.Equal(Fixed.FromInt(550) - hit.Amount, hit.TargetHpAfter);
    }

    [Fact]
    public void Damage_LandsOnlyAfterTheAttackPoint()
    {
        var kernel = new RulesetDraft().Start();

        kernel.StepUntil(() => kernel.EventsOf<DamageDealt>().Any(), 2000);

        long started = kernel.TimedEventsOf<AttackStarted>().First().Tick;
        long landed = kernel.TimedEventsOf<DamageDealt>().First().Tick;
        Assert.Equal(15, landed - started);   // 500 ms at 30 tps
    }

    [Fact]
    public void Attacks_RepeatAtTheAttackInterval()
    {
        var kernel = new RulesetDraft().Start();
        kernel.StepUntil(() => kernel.EventsOf<AttackStarted>().Count() >= 4, 3000);

        var dawnAttacks = kernel.TimedEventsOf<AttackStarted>()
            .Where(e => kernel.State.Find(e.Event.Attacker)?.Team == Team.Dawn)
            .Select(e => e.Tick).Take(2).ToList();

        Assert.Equal(30, dawnAttacks[1] - dawnAttacks[0]);
    }

    [Fact]
    public void DamageRolls_StayWithinTheTypeRangeAndVary()
    {
        var draft = new RulesetDraft();
        draft.Creeps[0] = new CreepType("melee", MeleeStats(armor: 0, dmgMin: 19, dmgMax: 23));
        var kernel = draft.Start(seed: 9);

        kernel.StepUntil(() => kernel.EventsOf<DamageDealt>().Count() >= 20, 5000);

        var amounts = kernel.EventsOf<DamageDealt>().Select(d => d.Amount.ToDouble()).ToList();
        Assert.All(amounts, a => Assert.InRange(a, 19, 23));
        Assert.True(amounts.Distinct().Count() > 1);
    }

    [Fact]
    public void LethalDamage_KillsAndRemovesTheUnitWithKillCredit()
    {
        var draft = new RulesetDraft();
        draft.Creeps.Add(new CreepType("brute", MeleeStats(dmgMin: 1000, dmgMax: 1000)));
        draft.DawnWave[0] = new WaveEntry("brute", 1);
        var kernel = draft.Start();

        kernel.StepUntil(() => kernel.EventsOf<UnitKilled>().Any(), 2000);
        kernel.Step();

        UnitKilled kill = kernel.EventsOf<UnitKilled>().Single();
        MatchUnit brute = Assert.Single(kernel.State.Creeps(Team.Dawn));
        Assert.Equal(brute.Id, kill.Killer);
        Assert.Empty(kernel.State.Creeps(Team.Dusk));
        Assert.Null(kernel.State.Find(kill.Victim));
    }

    [Fact]
    public void SimultaneousHits_BothLandEvenIfOneAttackerDiesThatTick()
    {
        var draft = new RulesetDraft();
        draft.Creeps[0] = new CreepType("melee", MeleeStats(hp: 20, armor: 0, dmgMin: 50, dmgMax: 50));
        var kernel = draft.Start();

        kernel.StepUntil(() => kernel.EventsOf<UnitKilled>().Any(), 2000);

        Assert.Equal(2, kernel.EventsOf<UnitKilled>().Count());
    }

    [Fact]
    public void Tower_EngagesEnemyCreepWhenItEntersRange()
    {
        var draft = new RulesetDraft();
        draft.DawnWave.Clear();
        var kernel = draft.Start();
        MatchUnit tower = kernel.State.FindStructure("dawn.mid.t1")!;

        kernel.StepUntil(() => tower.TargetId.IsValid, 3000);

        MatchUnit creep = kernel.State.Find(tower.TargetId)!;
        double distance = FixedVector2.Distance(tower.Position, creep.Position).ToDouble();
        // 700 range + 100 tower radius + 16 creep radius. The tower acquires at the start of a
        // tick, then the creep still walks 10 units during that same tick.
        Assert.InRange(distance, 796, 816);
    }

    [Fact]
    public void Towers_NeverAttackStructures()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Placements.Add(new StructurePlacement("dusk.close", Team.Dusk, "tower", P(3300, 5000), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));
        var kernel = draft.Start();

        kernel.RunTicks(300);

        Assert.Empty(kernel.EventsOf<AttackStarted>());
    }
}
