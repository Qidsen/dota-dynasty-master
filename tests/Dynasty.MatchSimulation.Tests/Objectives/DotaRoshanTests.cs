using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Tests.Abilities;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.MatchTestKit;
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

    /// <summary>
    /// Roshan's spells (game file `roshan_slam`, `roshan_revengeroar`; Valve 7.35, 7.39): Slam, when he fights, hits everything
    /// around (creeps twice as hard) and slows it; Roar of Retribution, when the team that last killed him hits him below 80%,
    /// damages, disarms and amplifies the damage the units around take. Both grow with the minutes. Pure damage and short times here.
    /// </summary>
    private static readonly ModifierSpec SlamSlow = new("roshan_slam_slow", 4000, moveSpeedPercent: -50);

    private static BossAbility Slam => new("roshan_slam", BossAbilityTrigger.Engaged, cooldownMs: 10000, castPointMs: 470, radius: 350,
        DamageType.Pure, damage: 70, damagePerInterval: 10, creepDamagePercent: 200, modifiers: new[] { SlamSlow });

    private static BossAbility Roar => new("roshan_roar", BossAbilityTrigger.HitByLastKillersBelowHealth, cooldownMs: 60000, castPointMs: 500, radius: 600,
        DamageType.Pure, damage: 50, damagePerInterval: 10, modifiers: new[] { new ModifierSpec("roshan_roar_disarm", 2000, ModifierFlags.Disarmed) },
        belowHealthPercent: 80, ignoresDisables: true, dispelsSelf: true);

    private static SimulationKernel<MatchState> SpellArena(int respawnMs = 480000)
    {
        RulesetDraft draft = WithRoshan();
        draft.Creeps.Add(new CreepType("bug", MeleeStats(hp: 100000, speed: 0)));
        draft.Boss = new BossDefinition("roshan", "roshan", P(5000, 2500), leashRadius: 600, firstSpawnMs: 0, respawnMinMs: respawnMs, respawnMaxMs: respawnMs,
            teamGold: 135, reward: null, growth: new BossGrowth(intervalMs: 2000, health: 0, armor: Fixed.Zero, damage: 0, xp: 0, armorFromStart: false),
            abilities: new[] { Slam, Roar }, provokeRadius: 0);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3100);   // out of Roshan's 300 provoke radius until it steps in
            H(ctx.State, "beta").Position = P(5000, 9000);
        });
        return kernel;
    }

    private static List<(long Tick, Dynasty.MatchSimulation.Events.DamageDealt Hit)> HitsBy(SimulationKernel<MatchState> kernel, DamageType type) =>
        kernel.TimedEventsOf<Dynasty.MatchSimulation.Events.DamageDealt>().Where(e => e.Event.Attacker == kernel.State.Boss?.Id && e.Event.DamageType == type).ToList();

    [Fact]
    public void Slam_HitsAndSlowsEverythingAroundWhenRoshanFights_CreepsTwiceAsHard_OnItsCooldown()
    {
        var kernel = SpellArena();
        MatchUnit? bug = null;
        At(kernel, 60, ctx =>   // one interval in: 80 damage
        {
            bug = ctx.State.SpawnCreep(ctx.Ids.Allocate(), Team.Dawn, ctx.State.Ruleset.FindCreepType("bug")!, ctx.State.Ruleset.Map.Lanes[0], ctx.Tick.Value);
            bug.Position = P(5200, 2500);
            H(ctx.State, "alpha").Position = P(5000, 2750);
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Boss!, Fixed.FromInt(1), DamageType.Pure);   // provoke
        });
        kernel.RunTicks(60 + 15 + 30);   // the cast point, and a second for another try within the cooldown

        var slams = HitsBy(kernel, DamageType.Pure);
        Assert.Equal(new[] { 80, 160 }, slams.OrderBy(h => h.Hit.Amount).Select(h => h.Hit.Amount.ToIntFloor()).ToArray());   // hero, creep
        Assert.Contains(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "roshan_slam_slow");
        Assert.Single(slams.Select(h => h.Tick).Distinct());   // once in its cooldown
        Assert.Equal(61 + 15, slams[0].Tick);   // starts as he takes the target (the tick after the hit), lands 0.47 s (15 ticks) later
    }

    [Fact]
    public void Roar_ComesOnlyForTheTeamThatLastKilledHim_BelowEightyPercent()
    {
        var kernel = SpellArena(respawnMs: 1000);
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Boss!, Fixed.FromInt(9999999), DamageType.Pure));   // Dawn killed him
        At(kernel, 40, ctx =>   // respawned
        {
            H(ctx.State, "alpha").Position = P(5000, 2900);   // out of Slam's reach
            H(ctx.State, "beta").Position = P(5100, 2900);
            MatchUnit roshan = ctx.State.Boss!;
            roshan.Hp = Fixed.FromInt(roshan.MaxHp * 7 / 10);
            Damage.Deal(ctx, H(ctx.State, "beta").Id, roshan, Fixed.FromInt(1), DamageType.Pure);   // Dusk: no roar
        });
        bool roaredAtDusk = true;
        At(kernel, 70, ctx =>
        {
            roaredAtDusk = H(ctx.State, "beta").Modifiers.Any(m => m.Spec.Key == "roshan_roar_disarm");
            Modifiers.Apply(ctx, ctx.State.Boss!, new ModifierSpec("stun", 3000, ModifierFlags.Stunned), H(ctx.State, "alpha").Id);   // roars anyway (7.39)
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Boss!, Fixed.FromInt(1), DamageType.Pure);   // Dawn: the roar
        });
        kernel.RunTicks(70 + 20);

        Assert.False(roaredAtDusk);
        Assert.Contains(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "roshan_roar_disarm");
        Assert.DoesNotContain(kernel.State.Boss!.Modifiers, m => m.Spec.Key == "stun");   // the roar dispels him
    }

    [Fact]
    public void Roshan_LeavesHeroesStandingNextToHimAlone_UntilAttacked()
    {
        var kernel = SpellArena();
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(5000, 2700));   // 200 from him
        kernel.RunTicks(90);

        Assert.False(kernel.State.Boss!.TargetId.IsValid);
        Assert.Equal(H(kernel.State, "alpha").MaxHp, H(kernel.State, "alpha").Hp.ToIntFloor());
    }

    /// <summary>
    /// Roshan's drops (Valve 7.37–7.38, D-051): from his third death Cheese (game file `item_cheese`: +2500 health, +1500 mana) and a
    /// Refresher Shard (`item_refresher_shard`: refreshes the cooldowns, 200 mana) — each to the killing team's richest living hero
    /// with room in its inventory (in Dota they lie on the ground — B-149).
    /// </summary>
    private static readonly AbilityDefinition CheeseActive = new("item_cheese", "Cheese", AbilityTargeting.NoTarget, TargetTeam.Ally, castRange: 0, castPointMs: 0,
        cooldownMs: new[] { 40000 }, manaCost: new[] { 0 }, requiredHeroLevel: new[] { 1 },
        effects: new[] { EffectDefinition.Heal(2500), EffectDefinition.RestoreMana(1500) });

    private static readonly AbilityDefinition ShardActive = new("item_refresher_shard", "Refresher Shard", AbilityTargeting.NoTarget, TargetTeam.Ally, castRange: 0,
        castPointMs: 0, cooldownMs: new[] { 180000 }, manaCost: new[] { 200 }, requiredHeroLevel: new[] { 1 }, effects: new[] { EffectDefinition.RefreshCooldowns() });

    private static RulesetDraft WithDrops(int respawnMs)
    {
        RulesetDraft draft = WithRoshan();
        draft.Abilities.Add(CheeseActive);
        draft.Abilities.Add(ShardActive);
        draft.Items.Add(new ItemDefinition("item_cheese", "Cheese", 0, activeAbilityId: "item_cheese", charges: 1, inShop: false));
        draft.Items.Add(new ItemDefinition("item_refresher_shard", "Refresher Shard", 0, activeAbilityId: "item_refresher_shard", charges: 1, inShop: false));
        draft.Boss = new BossDefinition("roshan", "roshan", P(5000, 2500), leashRadius: 600, firstSpawnMs: 0, respawnMinMs: respawnMs, respawnMaxMs: respawnMs,
            teamGold: 135, reward: null, provokeRadius: 0, drops: new[] { new BossDrop("item_cheese", fromDeath: 3), new BossDrop("item_refresher_shard", fromDeath: 3) });
        return draft;
    }

    private static bool Carries(MatchState state, string hero, string item) => H(state, hero).Hero!.Inventory.Any(i => i.Definition.Id == item);

    [Fact]
    public void CheeseAndARefresherShard_DropFromTheThirdDeath_ToTheRichestHeroWithRoom()
    {
        var kernel = WithDrops(respawnMs: 1000).StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 6000);
            H(ctx.State, "gamma").Position = P(3000, 6200);
            H(ctx.State, "gamma").Hero!.Gold = Fixed.FromInt(5000);
        });
        bool afterTwo = true;
        foreach (long tick in new long[] { 2, 40, 80 })
            At(kernel, tick, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Boss!, Fixed.FromInt(9999999), DamageType.Pure));
        At(kernel, 75, ctx => afterTwo = Carries(ctx.State, "gamma", "item_cheese") || Carries(ctx.State, "alpha", "item_cheese"));
        kernel.RunTicks(85);

        Assert.False(afterTwo);
        Assert.True(Carries(kernel.State, "gamma", "item_cheese") && Carries(kernel.State, "gamma", "item_refresher_shard"));
    }

    [Fact]
    public void TheRefresherShard_RefreshesTheCooldowns_AndIsUsedUp()
    {
        var draft = WithDrops(respawnMs: 480000);
        draft.Abilities.Add(Dynasty.MatchSimulation.Tests.Abilities.TestAbilities.Bolt);
        draft.WithHeroAbilities("alpha", "bolt");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 6000);
            H(ctx.State, "beta").Position = P(3200, 6000);
        });
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("bolt", H(ctx.State, "beta").Id)));
        At(kernel, 30, ctx =>
        {
            HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem("item_refresher_shard")!);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("item_refresher_shard"));
        });
        long boltReadyBefore = 0;
        At(kernel, 29, ctx => boltReadyBefore = H(ctx.State, "alpha").Hero!.Ability("bolt")!.CooldownReadyTick);
        kernel.RunTicks(35);

        Assert.True(boltReadyBefore > 30);
        Assert.True(H(kernel.State, "alpha").Hero!.Ability("bolt")!.CooldownReadyTick <= 31, "bolt ready again");
        Assert.False(Carries(kernel.State, "alpha", "item_refresher_shard"));
    }

    [Fact]
    public void ARefresher_DoesNotRefreshItself()
    {
        var draft = WithDrops(respawnMs: 480000);
        draft.Abilities.Add(new AbilityDefinition("item_refresher", "Refresher Orb", AbilityTargeting.NoTarget, TargetTeam.Ally, castRange: 0, castPointMs: 0,
            cooldownMs: new[] { 180000 }, manaCost: new[] { 0 }, requiredHeroLevel: new[] { 1 }, effects: new[] { EffectDefinition.RefreshCooldowns() }));
        draft.Items.Add(new ItemDefinition("item_refresher", "Refresher Orb", 0, activeAbilityId: "item_refresher"));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 2, ctx =>
        {
            HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem("item_refresher")!);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("item_refresher"));
        });
        kernel.RunTicks(6);

        Assert.True(H(kernel.State, "alpha").Hero!.Inventory.Single(i => i.Definition.Id == "item_refresher").Active!.CooldownReadyTick > 100);
    }

    [Fact]
    public void ADrop_GoesToTheNextRichestHero_WhenTheRichestHasNoRoom()
    {
        var kernel = WithDrops(respawnMs: 1000).StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 6000);
            H(ctx.State, "gamma").Position = P(3000, 6200);
            H(ctx.State, "gamma").Hero!.Gold = Fixed.FromInt(5000);
            for (int i = 0; i < HeroShop.InventorySize; i++)
                HeroShop.Give(ctx, H(ctx.State, "gamma"), ctx.State.Ruleset.FindItem("item_cheese")!);
        });
        foreach (long tick in new long[] { 2, 40, 80 })
            At(kernel, tick, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Boss!, Fixed.FromInt(9999999), DamageType.Pure));
        kernel.RunTicks(85);

        Assert.True(Carries(kernel.State, "alpha", "item_refresher_shard"));
    }

    [Fact]
    public void RoshansItems_AreNotSoldInTheShop()
    {
        var kernel = WithDrops(respawnMs: 480000).StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 2, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "item_cheese"));
        kernel.RunTicks(5);

        Assert.False(Carries(kernel.State, "alpha", "item_cheese"));
        Assert.Contains(kernel.EventsOf<Dynasty.MatchSimulation.Events.ItemPurchaseFailed>(), f => f.Reason == "not_for_sale");
    }
}
