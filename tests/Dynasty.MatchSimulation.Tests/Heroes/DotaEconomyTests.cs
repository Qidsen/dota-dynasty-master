using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Heroes;

/// <summary>
/// Dota 7.41f economy (docs/DOTA_RULES_AUDIT.md; Liquipedia «Gold» with changes up to 7.40, «Experience»; Valve patch
/// notes; decisions D-051): kill bounty 125 + 8 × level + streak + first blood 135; AoE assist gold
/// f = 15 + (50 + 0.037 × victim NW) / n, times the teams' net worth ratio; reliable gold only from the periodic
/// income; death costs NW / 40 of unreliable gold; buyback ⌊200 + NW / 13⌋, reliable first, +25 s on the next
/// respawn; respawn by level; hero kill XP (100 + 0.13 × XP, capped at level 25) / n + streak XP; denied creeps give
/// 50% XP, neutral creeps share XP with both teams.
/// </summary>
public class DotaEconomyTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>The 7.41f values, without periodic gold unless asked (so net worths stay the starting 600).</summary>
    private static EconomyRules Dota(bool periodicGold = false) => new(
        killGoldBase: 125, killGoldPerLevel: 8, firstBloodGold: 135,
        streakGold: new[] { 60, 100, 150, 210, 280, 360, 450, 550 },
        streakXpPerLevel: new[] { Fixed.FromRatio(1375, 100), Fixed.FromInt(20), Fixed.FromRatio(2875, 100), Fixed.FromInt(40), Fixed.FromRatio(5375, 100), Fixed.FromInt(70), Fixed.FromRatio(8875, 100), Fixed.FromInt(110) },
        assistGoldBase: 15, assistGoldPool: 50, assistGoldNetWorthPermille: 37, assistRadius: 1500, killCreditWindowMs: 20000,
        deathGoldLossNetWorthDivisor: 40, buybackBaseGold: 200, buybackNetWorthDivisor: 13, buybackRespawnPenaltyMs: 25000,
        respawnMs: new[] { 12, 15, 18, 21, 24, 26, 28, 30, 32, 34, 36, 44, 46, 48, 50, 52, 54, 65, 70, 75, 80, 85, 90, 95, 100 }.Select(s => s * 1000).ToArray(),
        neutralRespawnMinMs: 26000,
        periodicGold: periodicGold ? new[] { new PeriodicGold(0, 9000), new PeriodicGold(12 * 60000, 9480) } : Array.Empty<PeriodicGold>(),
        heroXpCapLevel: 25, deniedCreepXpPercent: 50);

    /// <summary>Dawn: alpha, gamma; Dusk: beta, delta (delta far away unless moved). Dota economy on.</summary>
    private static SimulationKernel<MatchState> Arena(EconomyRules? economy = null, Action<RulesetDraft>? configure = null)
    {
        RulesetDraft draft = TestAbilities.Draft();
        draft.Economy = economy ?? Dota();
        draft.HeroKillXpPerLevel = 0;            // Dota: 100 + 13% of the victim's XP
        draft.HeroKillXpPercentOfVictim = 13;
        configure?.Invoke(draft);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "gamma").Position = P(3000, 5000);   // more than 1500 away
            H(ctx.State, "beta").Position = P(5200, 5000);
            H(ctx.State, "delta").Position = P(8000, 5000);   // more than 1500 away
        });
        return kernel;
    }

    private static void Kill(SimulationKernel<MatchState> kernel, long tick, string victim, Func<MatchState, EntityId> killer) =>
        At(kernel, tick, ctx => Damage.Deal(ctx, killer(ctx.State), H(ctx.State, victim), Fixed.FromInt(99999), DamageType.Pure));

    private static double GoldOf(SimulationKernel<MatchState> kernel, string hero) => H(kernel.State, hero).Hero!.Gold.ToDouble();

    [Fact]
    public void FirstKill_GivesBountyFirstBloodAndAssistGold()
    {
        var kernel = Arena();
        Kill(kernel, 2, "alpha", s => H(s, "beta").Id);
        kernel.RunTicks(4);

        // Bounty 125 + 8 × 1 + 135; assist: n = 1 (beta alone in 1500), f = 15 + (50 + 0.037 × 600) / 1 = 87.2,
        // times Dawn NW 1200 / Dusk NW 1200 = 1.
        Assert.InRange(GoldOf(kernel, "beta") - 600, 268 + 87.2 - 0.01, 268 + 87.2 + 0.01);
        Assert.Equal(600, GoldOf(kernel, "delta"));   // out of range and no damage: nothing
        Assert.Equal(1, H(kernel.State, "beta").Hero!.KillStreak);
    }

    [Fact]
    public void SecondKill_HasNoFirstBlood_AndAStreakIsPaidWhenEnded()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => H(ctx.State, "gamma").Hero!.KillStreak = 3);
        Kill(kernel, 2, "alpha", s => H(s, "beta").Id);            // first blood
        Kill(kernel, 4, "gamma", s => H(s, "beta").Id);            // ends gamma's streak of 3
        double before = 0;
        At(kernel, 3, ctx => before = H(ctx.State, "beta").Hero!.Gold.ToDouble());
        At(kernel, 4, ctx => H(ctx.State, "gamma").Position = P(5000, 5100));
        kernel.RunTicks(6);

        HeroState beta = H(kernel.State, "beta").Hero!;
        double gained = beta.Gold.ToDouble() - before;
        // 125 + 8 + streak 60, no first blood; assist (n = 1): f = 15 + (50 + 0.037 × 600) = 87.2, times Dawn NW / Dusk NW
        // before the death's loss: (585 + 600) / (955.2 + 600) → 66.44.
        Assert.InRange(gained, 193 + 66.43, 193 + 66.45);
        Assert.Equal(0, H(kernel.State, "gamma").Hero!.KillStreak);
        Assert.Equal(2, beta.KillStreak);
    }

    [Fact]
    public void DeathToACreepWithoutHeroDamage_SplitsTheBounty_AndNeitherEndsTheStreakNorIsFirstBlood()
    {
        var kernel = Arena();
        MatchUnit? creep = null;
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hero!.KillStreak = 3;
            creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Dusk, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
        });
        Kill(kernel, 2, "alpha", _ => creep!.Id);
        kernel.RunTicks(4);

        // 125 + 8 + 60 (the streak ends only through heroes: here it is not ended, so no streak gold) → 133 split between beta and delta.
        Assert.InRange(GoldOf(kernel, "beta") - 600, 66.49, 66.51);
        Assert.InRange(GoldOf(kernel, "delta") - 600, 66.49, 66.51);
        Assert.Equal(3, H(kernel.State, "alpha").Hero!.KillStreak);
        Assert.False(kernel.State.FirstBloodTaken);
    }

    [Fact]
    public void DeathToACreepAfterOneHerosDamage_CreditsThatHero()
    {
        var kernel = Arena();
        MatchUnit? creep = null;
        At(kernel, 1, ctx =>
        {
            creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Dusk, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
            H(ctx.State, "beta").Position = P(9000, 3000);   // far away: no assist by range
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(10), DamageType.Pure);
        });
        Kill(kernel, 2, "alpha", _ => creep!.Id);
        kernel.RunTicks(4);

        Assert.Equal(1, H(kernel.State, "beta").Hero!.Kills);
        Assert.True(kernel.State.FirstBloodTaken);
        Assert.True(GoldOf(kernel, "beta") - 600 >= 268);   // bounty with first blood (and the assist share: beta damaged alpha)
    }

    [Fact]
    public void DeathToANeutral_GivesNothing()
    {
        var kernel = Arena();
        MatchUnit? neutral = null;
        At(kernel, 1, ctx =>
        {
            neutral = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Neutral, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
        });
        Kill(kernel, 2, "alpha", _ => neutral!.Id);
        kernel.RunTicks(4);

        Assert.Equal(600, GoldOf(kernel, "beta"));
        Assert.Equal(0, H(kernel.State, "beta").Hero!.Xp);
        Assert.DoesNotContain(kernel.EventsOf<HeroKilled>(), k => k.Killer.IsValid && kernel.State.Find(k.Killer)?.Hero != null);
        Assert.True(H(kernel.State, "alpha").Hero!.RespawnTick - 3 >= kernel.State.Rate.TicksFor(26000));   // neutral deaths: at least 26 s (the death is handled on tick 3)
    }

    [Fact]
    public void Death_CostsNetWorthOver40_OfUnreliableGoldOnly()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            HeroState alpha = H(ctx.State, "alpha").Hero!;
            alpha.Gold = Fixed.FromInt(1000);
            alpha.ReliableGold = Fixed.FromInt(990);
        });
        Kill(kernel, 2, "alpha", s => H(s, "beta").Id);
        kernel.RunTicks(4);

        Assert.Equal(990, GoldOf(kernel, "alpha"));   // NW 1000 / 40 = 25, but only 10 is unreliable
    }

    [Fact]
    public void Buyback_CostsFromNetWorth_ReliableFirst_AndLengthensTheNextRespawn()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            HeroState alpha = H(ctx.State, "alpha").Hero!;
            alpha.Gold = Fixed.FromInt(2600);
            alpha.ReliableGold = Fixed.FromInt(300);
        });
        Kill(kernel, 2, "alpha", s => H(s, "beta").Id);
        At(kernel, 4, ctx => HeroActions.QueueBuyback(H(ctx.State, "alpha")));
        Kill(kernel, 6, "alpha", s => H(s, "beta").Id);
        kernel.RunTicks(8);

        HeroBoughtBack buyback = Assert.Single(kernel.EventsOf<HeroBoughtBack>());
        // NW after the death: 2600 − 2600 / 40 = 2535; buyback ⌊200 + 2535 / 13⌋ = 395.
        Assert.Equal(395, buyback.Cost);
        HeroState alpha = H(kernel.State, "alpha").Hero!;
        Assert.Equal(Fixed.Zero, alpha.ReliableGold);   // the 300 reliable went first
        long respawnTicks = alpha.RespawnTick - 7;   // the second death is handled on tick 7
        Assert.Equal(kernel.State.Rate.TicksFor(12000 + 25000), respawnTicks);   // level 1: 12 s, + 25 s after a buyback
    }

    [Theory]
    [InlineData(1, 12000)]
    [InlineData(4, 21000)]
    public void Respawn_FollowsTheLevelTable(int level, int ms)
    {
        var kernel = Arena(configure: d => { d.XpTable.Clear(); d.XpTable.AddRange(Enumerable.Range(0, 30).Select(i => i * 100)); });
        At(kernel, 1, ctx => HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), (level - 1) * 100));
        Kill(kernel, 2, "alpha", s => H(s, "beta").Id);
        kernel.RunTicks(4);

        Assert.Equal(kernel.State.Rate.TicksFor(ms), H(kernel.State, "alpha").Hero!.RespawnTick - 3);
    }

    [Fact]
    public void PeriodicGold_IsReliable_AndFollowsItsTable()
    {
        var kernel = Arena(Dota(periodicGold: true));
        kernel.RunTicks(30 * 60);

        HeroState alpha = H(kernel.State, "alpha").Hero!;
        Assert.InRange(alpha.Gold.ToDouble() - 600, 89.9, 90.1);   // 90 a minute at first
        Assert.InRange(alpha.ReliableGold.ToDouble(), 89.9, 90.1);
    }

    [Fact]
    public void HeroKillXp_IsCappedAtLevel25_PlusStreakXp()
    {
        var kernel = Arena(configure: d => { d.XpTable.Clear(); d.XpTable.AddRange(Enumerable.Range(0, 30).Select(i => i * 100)); });
        At(kernel, 1, ctx =>
        {
            HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 2900);   // level 30
            H(ctx.State, "alpha").Hero!.KillStreak = 3;
        });
        Kill(kernel, 2, "alpha", s => H(s, "beta").Id);
        kernel.RunTicks(4);

        // (100 + 0.13 × 2400 [XP of level 25]) / 1 + 13.75 × level 30.
        Assert.Equal(100 + 312 + 412, H(kernel.State, "beta").Hero!.Xp);
    }

    [Fact]
    public void DeniedLaneCreep_GivesEnemiesHalfItsXp()
    {
        var kernel = Arena();
        MatchUnit? creep = null;
        At(kernel, 1, ctx =>
        {
            creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Dawn, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
            creep.Position = P(5100, 5000);
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, creep, Fixed.FromInt(99999), DamageType.Pure);
        });
        kernel.RunTicks(3);

        Assert.Equal(30, H(kernel.State, "beta").Hero!.Xp);   // half of the melee creep's 60
    }

    [Fact]
    public void NeutralCreepXp_IsSharedWithHeroesOfBothTeams()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            MatchUnit neutral = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Neutral, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
            neutral.Position = P(5100, 5000);
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, neutral, Fixed.FromInt(99999), DamageType.Pure);
        });
        kernel.RunTicks(3);

        Assert.Equal((30, 30), (H(kernel.State, "alpha").Hero!.Xp, H(kernel.State, "beta").Hero!.Xp));   // 60 split between the two in 1500
    }
}
