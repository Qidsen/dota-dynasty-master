using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's Roshan (game file `npc_dota_roshan`; Liquipedia «Roshan»; D-051): he grows every minute from the horn, alive or
/// dead (+130 health, +0.4 armor — the first already at 0:00 —, +7 damage, +20 XP); killed, he gives every hero of the killing
/// team 135 gold, the hero who landed the last hit 200–290 more, and 400 XP plus the growth to the killing team's heroes around.
/// Here an interval is 2 s.
/// </summary>
public class DotaRoshanTests
{
    private static RulesetDraft WithRoshan()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("roshan", new UnitStats(6000, Fixed.FromInt(30), 270, 75, 75, 150, 2000, 600, 150, 60, visionRange: 1400),
            modifiers: new[] { new ModifierSpec("roshan_bash", -1, onHitChancePercent: 15, pseudoRandom: true) }));
        draft.Boss = new BossDefinition("roshan", "roshan", P(5000, 2500), leashRadius: 600, firstSpawnMs: 0, respawnMinMs: 480000, respawnMaxMs: 660000,
            teamGold: 135, reward: null, killerGoldMin: 200, killerGoldMax: 290, xp: 400,
            growth: new BossGrowth(intervalMs: 2000, health: 130, armor: Fixed.FromRatio(2, 5), damage: 7, xp: 20, armorFromStart: true));
        return draft;
    }

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    [Fact]
    public void Roshan_GrowsEveryInterval()
    {
        var kernel = WithRoshan().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        int hpAtStart = 0;
        Fixed armorAtStart = Fixed.Zero;
        At(kernel, 2, ctx =>
        {
            hpAtStart = ctx.State.Boss!.Hp.ToIntFloor();
            armorAtStart = ctx.State.Boss!.Armor;
        });
        kernel.RunTicks(3 * 60 + 2);   // three intervals in

        MatchUnit roshan = kernel.State.Boss!;
        Assert.Equal(6000 + 3 * 130, roshan.MaxHp);
        Assert.Equal(6000 + 3 * 130, roshan.Hp.ToIntFloor());   // the gain heals as well
        Assert.Equal(Fixed.FromInt(30) + Fixed.FromRatio(2, 5) * Fixed.FromInt(4), roshan.Armor);   // from 0:00, then every interval
        Assert.Equal(75 + 3 * 7, roshan.AttackDamageMin);
        Assert.Contains(roshan.Modifiers, m => m.Spec.Key == "roshan_bash");   // its type's own modifiers
        Assert.Equal((6000, Fixed.FromInt(30) + Fixed.FromRatio(2, 5)), (hpAtStart, armorAtStart));
    }

    [Fact]
    public void KillingRoshan_PaysTheTeamTheKillerAndXp()
    {
        var kernel = WithRoshan().StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        Dictionary<string, double> before = new();
        Dictionary<string, int> xpBefore = new();
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 2700);
            H(ctx.State, "gamma").Position = P(5000, 3000);
            H(ctx.State, "beta").Position = P(5000, 9000);
        });
        At(kernel, 2 * 30 + 1, ctx =>   // one interval in
        {
            foreach (string hero in new[] { "alpha", "gamma", "beta" })
            {
                before[hero] = H(ctx.State, hero).Hero!.Gold.ToDouble();
                xpBefore[hero] = H(ctx.State, hero).Hero!.Xp;
            }

            Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Boss!, Fixed.FromInt(999999), DamageType.Pure);
        });
        kernel.RunTicks(2 * 30 + 4);

        double Gain(string hero) => System.Math.Floor(H(kernel.State, hero).Hero!.Gold.ToDouble() - before[hero]);
        Assert.InRange(Gain("alpha"), 135 + 200, 135 + 290);
        Assert.Equal(135, Gain("gamma"));
        Assert.Equal(0, Gain("beta"));
        Assert.Equal((400 + 20) / 2, H(kernel.State, "gamma").Hero!.Xp - xpBefore["gamma"]);
        Assert.Equal(0, H(kernel.State, "beta").Hero!.Xp - xpBefore["beta"]);
    }
}
