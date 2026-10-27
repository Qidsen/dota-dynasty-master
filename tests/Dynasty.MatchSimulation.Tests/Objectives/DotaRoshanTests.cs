using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests;
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

    /// <summary>
    /// The Aegis (game file `item_aegis`: reincarnate after 5 s, gone after 300 s; Fandom «Aegis of the Immortal»: unused, it
    /// restores 20% of health and mana per second for 5 s): the killing team's richest living hero carries it (in Dota it lies on
    /// the ground — B-149); dying with it, the hero comes back where it died after the delay with full health and mana; the
    /// death is not counted and gives no bounty. Shortened times here.
    /// </summary>
    private static readonly ModifierSpec AegisRegen = new("aegis_regen", 1000, healPerSecondMaxHealthPercent: Fixed.FromInt(20), manaPerSecondMaxManaPercent: Fixed.FromInt(20));

    private static readonly ModifierSpec Aegis = new("aegis", 4000, revives: true, reviveDelayMs: 1000, consumedOnRevive: true, expireModifier: AegisRegen, dispellable: false);

    private static SimulationKernel<MatchState> AegisArena()
    {
        RulesetDraft draft = WithRoshan();
        draft.Boss = new BossDefinition("roshan", "roshan", P(5000, 2500), leashRadius: 600, firstSpawnMs: 0, respawnMinMs: 480000, respawnMaxMs: 660000,
            teamGold: 135, reward: null, killerGoldMin: 200, killerGoldMax: 290, xp: 400, aegis: Aegis);
        draft.BuybackBaseGold = 100;   // buyback on: refused while reincarnating
        draft.BuybackCooldownMs = 1000;
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 2700);
            H(ctx.State, "gamma").Position = P(3000, 6000);
            H(ctx.State, "beta").Position = P(3000, 6200);
            H(ctx.State, "gamma").Hero!.Gold = Fixed.FromInt(5000);   // the richest of Dawn
        });
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Boss!, Fixed.FromInt(999999), DamageType.Pure));
        return kernel;
    }

    [Fact]
    public void TheAegis_RevivesItsCarrierWhereItDied_AfterTheDelay_WithoutCountingTheDeath()
    {
        var kernel = AegisArena();
        bool hadAegis = false, deadDuring = false;
        double betaGold = 0;
        At(kernel, 4, ctx => hadAegis = H(ctx.State, "gamma").Modifiers.Any(m => m.Spec.Key == "aegis") && !H(ctx.State, "alpha").Modifiers.Any(m => m.Spec.Key == "aegis"));
        At(kernel, 5, ctx =>
        {
            betaGold = H(ctx.State, "beta").Hero!.Gold.ToDouble();
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "gamma"), Fixed.FromInt(99999), DamageType.Pure);
        });
        At(kernel, 20, ctx =>
        {
            deadDuring = H(ctx.State, "gamma").Hero!.IsDead && !H(ctx.State, "gamma").IsAlive;
            Dynasty.MatchSimulation.Heroes.HeroActions.QueueBuyback(H(ctx.State, "gamma"));
        });
        kernel.RunTicks(5 + 35);

        MatchUnit gamma = H(kernel.State, "gamma");
        Assert.True(hadAegis, "the richest hero of the killing team carries the Aegis");
        Assert.True(deadDuring, "down during the delay");
        Assert.True(gamma.IsAlive && !gamma.Hero!.IsDead);
        Assert.Equal(P(3000, 6000), gamma.Position);                   // where it died
        Assert.Equal(gamma.MaxHp, gamma.Hp.ToIntFloor());
        Assert.Equal(0, gamma.Hero.Deaths);
        Assert.DoesNotContain(gamma.Modifiers, m => m.Spec.Key == "aegis");   // used up
        Assert.True(H(kernel.State, "beta").Hero!.Gold.ToDouble() - betaGold < 5, "no bounty");
        Assert.Contains(kernel.EventsOf<Dynasty.MatchSimulation.Events.BuybackFailed>(), f => f.Reason == "reincarnating");
    }

    [Fact]
    public void AnUnusedAegis_Expires_IntoAQuickRegeneration()
    {
        var kernel = AegisArena();
        At(kernel, 5, ctx =>
        {
            MatchUnit gamma = H(ctx.State, "gamma");
            gamma.Hp = Fixed.FromInt(gamma.MaxHp / 2);
        });
        bool regen = false;
        At(kernel, 3 + 120 + 5, ctx => regen = H(ctx.State, "gamma").Modifiers.Any(m => m.Spec.Key == "aegis_regen"));
        kernel.RunTicks(3 + 120 + 40);

        MatchUnit g = H(kernel.State, "gamma");
        Assert.True(regen, "the regeneration follows the expiry");
        Assert.DoesNotContain(g.Modifiers, m => m.Spec.Key == "aegis");
        Assert.InRange(g.Hp.ToDouble() / g.MaxHp, 0.69, 0.75);   // half, then 20% a second for 1 s (and a little regeneration)
    }

    [Fact]
    public void RoshansRespawn_TakesBackAnUnusedAegis()
    {
        RulesetDraft draft = WithRoshan();
        draft.Boss = new BossDefinition("roshan", "roshan", P(5000, 2500), leashRadius: 600, firstSpawnMs: 0, respawnMinMs: 1000, respawnMaxMs: 1000,
            teamGold: 135, reward: null, aegis: new ModifierSpec("aegis", 600000, revives: true, reviveDelayMs: 1000, consumedOnRevive: true, expireModifier: AegisRegen));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(3000, 6000));
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Boss!, Fixed.FromInt(999999), DamageType.Pure));
        bool had = false;
        At(kernel, 10, ctx => had = H(ctx.State, "alpha").Modifiers.Any(m => m.Spec.Key == "aegis"));
        kernel.RunTicks(2 + 30 + 5);   // respawned after 1 s

        Assert.True(had);
        Assert.NotNull(kernel.State.Boss);
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "aegis");
        Assert.Contains(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "aegis_regen");
    }
}
