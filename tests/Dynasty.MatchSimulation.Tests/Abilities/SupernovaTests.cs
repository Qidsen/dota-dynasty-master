using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Supernova (Dota 7.41f data and the official description; the project owner: Phoenix itself is the sun): no damage gets through,
/// enemy heroes' attacks are counted and enough of them kill it; it burns enemies around; surviving, it stuns them and comes back with
/// full health and mana and its abilities ready.
/// </summary>
public class SupernovaTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Sun = new ModifierSpec("supernova", 6000, ModifierFlags.Busy, dispellable: false, heroAttacksToBreak: 3, periodMs: 200,
        periodicFromBearer: true,
        periodicEffects: new[] { EffectDefinition.Area(1200, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 10)) },
        onEnd: new[]
        {
            EffectDefinition.Area(1200, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Modifier(ModifierSpec.Status("supernova_stun", 2000, ModifierFlags.Stunned))),
            EffectDefinition.ScaledHeal(DamageScaling.TargetMaxHealthPercent, 10000),
            EffectDefinition.ScaledRestoreMana(DamageScaling.TargetMaxManaPercent, 10000),
            EffectDefinition.RefreshCooldowns(abilitiesOnly: true),
        });

    private static SimulationKernel<MatchState> Arena(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        setup?.Invoke(draft);
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? alphaAbilities : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5150, 5000);
            H(ctx.State, "gamma").Position = P(9000, 9000);   // one on one
            H(ctx.State, "alpha").Hp = Fixed.FromInt(300);
        });
        return kernel;
    }

    [Fact]
    public void NoDamageGetsThrough_ButEnoughHeroAttacksKillIt()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Sun, H(ctx.State, "alpha").Id));
        At(kernel, 3, ctx =>
        {
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(1000), DamageType.Pure);   // a spell: nothing
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id));                     
        });
        double afterSpell = 0;
        At(kernel, 5, ctx => afterSpell = H(ctx.State, "alpha").Hp.ToDouble());

        kernel.RunTicks(3 + 5 * 30);

        Assert.Equal(300, afterSpell, 0);
        Assert.False(H(kernel.State, "alpha").IsAlive);
        Assert.Contains(kernel.EventsOf<HeroKilled>(), k => k.Victim == H(kernel.State, "alpha").Id && k.Killer == H(kernel.State, "beta").Id);
    }

    [Fact]
    public void OnlyHeroesAttacksCount()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Sun, H(ctx.State, "alpha").Id));
        At(kernel, 3, ctx =>
        {
            MatchUnit creep = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, -1);
            for (int i = 0; i < 5; i++)
                AttackSystem.ApplyHit(ctx, creep, H(ctx.State, "alpha"), Fixed.FromInt(100), critical: false);
        });

        kernel.RunTicks(5);

        Assert.True(H(kernel.State, "alpha").IsAlive);
        Assert.Equal(300, H(kernel.State, "alpha").Hp.ToDouble(), 0);
    }

    [Fact]
    public void Surviving_ItStunsEnemies_AndComesBackWhole_WithItsAbilitiesReady()
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("nuke", "Nuke", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0, new[] { 60000 },
            new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Damage(DamageType.Pure, 1) })), new[] { "nuke" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hero!.Abilities[0].CooldownReadyTick = 100000;
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(50);
        });
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Sun, H(ctx.State, "alpha").Id));

        kernel.RunTicks(2 + 180 + 3);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Equal(5000, alpha.Hp.ToDouble(), 0);
        Assert.Equal(1000, alpha.Hero!.Mana.ToDouble(), 0);
        Assert.True(alpha.Hero.Abilities[0].IsReady(kernel.CurrentTick.Value));
        Assert.Contains(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "supernova_stun");
        Assert.True(H(kernel.State, "beta").Hp < Fixed.FromInt(5000));   // burnt meanwhile
    }

    /// <summary>The AI goes supernova when it is about to die with enemies close, not when it is well.</summary>
    [Theory]
    [InlineData(300, true)]
    [InlineData(5000, false)]
    public void Ai_GoesSupernova_WhenDying(int hp, bool casts)
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("supernova", "Supernova", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
            new[] { 100000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.OnCaster(EffectDefinition.Modifier(Sun)) })), new[] { "supernova" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(hp);
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);   // a weak enemy: a fight to take when whole
            H(ctx.State, "beta").Position = P(5600, 5000);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 * 30);

        Assert.Equal(casts, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "supernova"));
        if (!casts)
            Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);   // it did fight
    }
}
