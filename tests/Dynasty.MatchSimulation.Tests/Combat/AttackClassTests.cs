using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Attack classes (Dota 7.31+): an attack's damage is scaled by the attacker's class against the
/// target's defense class — heroes hit buildings for half, melee creeps (Runty) heroes for 75% and
/// buildings for 70%, ranged creeps (Piercing) creeps for 150%, heroes for 50% and buildings for 35%,
/// siege creeps and towers (Reinforced) buildings for 250%. Each case compares the same scene with
/// the Dota table and with the uniform one, so armor and the damage roll cancel out.
/// </summary>
public class AttackClassTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static UnitStats Ranged(AttackClass attackClass) =>
        new(300, Fixed.Zero, 300, 20, 20, 500, 1000, 300, 500, 16, projectileSpeed: 900, attackClass: attackClass);

    private static UnitStats Melee(AttackClass attackClass) =>
        new(550, Fixed.Zero, 300, 20, 20, 100, 1000, 500, 500, 16, attackClass: attackClass);

    /// <summary>
    /// Plays the scene and returns the first damage the chosen attacker deals to the chosen target.
    /// The Dusk wave is one creep of type "dusk_creep", the Dawn wave one of type "dawn_creep" (or none).
    /// </summary>
    private static Fixed FirstHit(
        bool dota,
        CreepType duskCreep,
        CreepType? dawnCreep,
        Action<SimulationContext<MatchState>> place,
        Func<MatchState, (EntityId Attacker, EntityId Target)> pair,
        Action<SimulationContext<MatchState>>? order = null)
    {
        var draft = new RulesetDraft { AttackClasses = dota ? AttackClassTable.Dota : AttackClassTable.Uniform };
        draft.StructureTypes[0] = new StructureType("tower", TowerStats(armor: 0));
        draft.Creeps.Add(duskCreep);
        draft.DuskWave.Clear();
        draft.DuskWave.Add(new WaveEntry(duskCreep.Id, 1));
        draft.DawnWave.Clear();
        if (dawnCreep != null)
        {
            draft.Creeps.Add(dawnCreep);
            draft.DawnWave.Add(new WaveEntry(dawnCreep.Id, 1));
        }

        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        (EntityId attacker, EntityId target) = (EntityId.None, EntityId.None);
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            place(ctx);
            (attacker, target) = pair(ctx.State);
        }));
        if (order != null)
            kernel.AddSystem(new LambdaSystem(VisionRefresh, order));
        kernel.RunTicks(30 * 20);

        return kernel.EventsOf<DamageDealt>().First(d => d.Attacker == attacker && d.Target == target).Amount;
    }

    private const int VisionRefresh = Dynasty.MatchSimulation.Vision.VisionSystem.IntervalTicks + 1;

    private static void AssertPercent(int percent, Fixed dota, Fixed uniform) =>
        Assert.InRange(dota.ToDouble(), uniform.ToDouble() * percent / 100 - 0.5, uniform.ToDouble() * percent / 100 + 0.5);

    private static MatchUnit CreepOf(MatchState state, Team team) => state.Creeps(team).First();

    [Fact]
    public void Hero_HitsBuildingsForHalf_AndCreepsInFull()
    {
        Fixed Tower(bool dota) => FirstHit(dota, new CreepType("dusk_creep", Melee(AttackClass.Basic)), null,
            ctx => { H(ctx.State, "alpha").Position = P(6800, 5000); CreepOf(ctx.State, Team.Dusk).Position = P(9000, 9000); },
            s => (H(s, "alpha").Id, s.FindStructure("dusk.mid.t1")!.Id),
            ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(ctx.State.FindStructure("dusk.mid.t1")!.Id)));
        AssertPercent(50, Tower(true), Tower(false));

        Fixed Creep(bool dota) => FirstHit(dota, new CreepType("dusk_creep", Melee(AttackClass.Basic)), null,
            ctx => { H(ctx.State, "alpha").Position = P(5000, 5000); CreepOf(ctx.State, Team.Dusk).Position = P(5100, 5000); },
            s => (H(s, "alpha").Id, CreepOf(s, Team.Dusk).Id),
            ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(CreepOf(ctx.State, Team.Dusk).Id)));
        AssertPercent(100, Creep(true), Creep(false));
    }

    [Fact]
    public void PiercingCreep_Hits150PercentOnCreeps_HalfOnHeroes_35PercentOnBuildings()
    {
        var archer = new CreepType("dusk_creep", Ranged(AttackClass.Piercing));

        Fixed OnCreep(bool dota) => FirstHit(dota, archer, new CreepType("dawn_creep", Melee(AttackClass.Basic)),
            ctx => { H(ctx.State, "alpha").Position = P(500, 9000); CreepOf(ctx.State, Team.Dusk).Position = P(5300, 5000); CreepOf(ctx.State, Team.Dawn).Position = P(5000, 5000); },
            s => (CreepOf(s, Team.Dusk).Id, CreepOf(s, Team.Dawn).Id));
        AssertPercent(150, OnCreep(true), OnCreep(false));

        Fixed OnHero(bool dota) => FirstHit(dota, archer, null,
            ctx => { H(ctx.State, "alpha").Position = P(5000, 5000); CreepOf(ctx.State, Team.Dusk).Position = P(5300, 5000); },
            s => (CreepOf(s, Team.Dusk).Id, H(s, "alpha").Id));
        AssertPercent(50, OnHero(true), OnHero(false));

        Fixed OnTower(bool dota) => FirstHit(dota, archer, null,
            ctx => { H(ctx.State, "alpha").Position = P(500, 9000); CreepOf(ctx.State, Team.Dusk).Position = P(3500, 5000); },
            s => (CreepOf(s, Team.Dusk).Id, s.FindStructure("dawn.mid.t1")!.Id));
        AssertPercent(35, OnTower(true), OnTower(false));
    }

    [Fact]
    public void RuntyCreep_HitsHeroesFor75Percent_BuildingsFor70()
    {
        var melee = new CreepType("dusk_creep", Melee(AttackClass.Runty));

        Fixed OnHero(bool dota) => FirstHit(dota, melee, null,
            ctx => { H(ctx.State, "alpha").Position = P(5000, 5000); CreepOf(ctx.State, Team.Dusk).Position = P(5100, 5000); },
            s => (CreepOf(s, Team.Dusk).Id, H(s, "alpha").Id));
        AssertPercent(75, OnHero(true), OnHero(false));

        Fixed OnTower(bool dota) => FirstHit(dota, melee, null,
            ctx => { H(ctx.State, "alpha").Position = P(500, 9000); CreepOf(ctx.State, Team.Dusk).Position = P(3150, 5000); },
            s => (CreepOf(s, Team.Dusk).Id, s.FindStructure("dawn.mid.t1")!.Id));
        AssertPercent(70, OnTower(true), OnTower(false));
    }

    [Fact]
    public void ReinforcedCreep_HitsBuildingsFor250Percent()
    {
        Fixed OnTower(bool dota) => FirstHit(dota, new CreepType("dusk_creep", Ranged(AttackClass.Reinforced)), null,
            ctx => { H(ctx.State, "alpha").Position = P(500, 9000); CreepOf(ctx.State, Team.Dusk).Position = P(3500, 5000); },
            s => (CreepOf(s, Team.Dusk).Id, s.FindStructure("dawn.mid.t1")!.Id));
        AssertPercent(250, OnTower(true), OnTower(false));
    }

    [Fact]
    public void AttackClasses_ArePartOfTheRulesetFingerprint()
    {
        var uniform = new RulesetDraft().Build();
        var dota = new RulesetDraft { AttackClasses = AttackClassTable.Dota }.Build();

        Assert.NotEqual(uniform.ToDescriptor().Fingerprint, dota.ToDescriptor().Fingerprint);
    }

    [Fact]
    public void BaseRuleset_UsesDotaAttackClasses()
    {
        RulesetContent ruleset = BaseRuleset.Create();

        Assert.Equal(50, ruleset.Combat.AttackClasses.Percent(AttackClass.Hero, DefenseClass.Reinforced));
        Assert.Equal(AttackClass.Runty, ruleset.CreepTypes.Single(c => c.Id == "melee").Stats.AttackClass);
        Assert.Equal(AttackClass.Piercing, ruleset.CreepTypes.Single(c => c.Id == "ranged").Stats.AttackClass);
        Assert.Equal(AttackClass.Reinforced, ruleset.CreepTypes.Single(c => c.Id == "siege").Stats.AttackClass);
    }
}
