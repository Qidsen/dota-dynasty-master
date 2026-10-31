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
/// Dota 7.41f data and official descriptions. Shadow Poison: deferred damage of 1/2/4/8/16 times the stack damage by the stacks (60 more
/// for each stack beyond five), dealt when the poison ends or is released. Song of the Siren: enemies around in stasis, unable to act or
/// be hurt, waking when Naga is too far. Death Pact: a creep consumed for current and max health; the max goes back when it ends.
/// </summary>
public class PoisonSongPactTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

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
            H(ctx.State, "beta").Position = P(5600, 5000);
            H(ctx.State, "gamma").Position = P(5600, 5300);
        });
        return kernel;
    }

    private static Fixed Lost(MatchState state, string hero) => Fixed.FromInt(5000) - H(state, hero).Hp;

    private static readonly ModifierSpec Poison = new ModifierSpec("shadow_poison", 2000, maxStacks: int.MaxValue,
        onEnd: new[] { EffectDefinition.DamageByStacksDoubling(DamageType.Pure, 5, 60, 10) });

    [Theory]
    [InlineData(1, 10)]
    [InlineData(3, 40)]               // 4 × 10
    [InlineData(5, 160)]              // 16 × 10
    [InlineData(7, 160 + 2 * 60)]     // beyond five: 60 a stack
    public void Poison_DealsDeferredDamageByItsStacks_WhenItEnds(int stacks, int damage)
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            for (int i = 0; i < stacks; i++)
                Modifiers.Apply(ctx, H(ctx.State, "beta"), Poison, H(ctx.State, "alpha").Id);
        });
        double before = -1;
        At(kernel, 50, ctx => before = Lost(ctx.State, "beta").ToDouble());

        kernel.RunTicks(1 + 60 + 3);

        Assert.Equal(0, before, 0);   // deferred
        Assert.Equal(damage, Lost(kernel.State, "beta").ToDouble(), 0);
    }

    [Fact]
    public void Release_EndsTheCastersPoisonsAtOnce()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "beta"), Poison, H(ctx.State, "alpha").Id);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), Poison, H(ctx.State, "alpha").Id);
            Modifiers.Apply(ctx, H(ctx.State, "gamma"), Poison, H(ctx.State, "alpha").Id);
        });
        At(kernel, 5, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Release("shadow_poison") }, 1, H(ctx.State, "alpha"), P(0, 0)));

        kernel.RunTicks(8);

        Assert.Equal(20, Lost(kernel.State, "beta").ToDouble(), 0);
        Assert.Equal(10, Lost(kernel.State, "gamma").ToDouble(), 0);
        Assert.DoesNotContain(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "shadow_poison");
    }

    private static readonly ModifierSpec Stasis = new ModifierSpec("song_of_the_siren", 7000, ModifierFlags.Stunned | ModifierFlags.Invulnerable, breakDistance: 1000);

    [Fact]
    public void Song_EnemiesInStasis_CannotBeHurt_AndWakeWhenNagaIsFar()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), Stasis, H(ctx.State, "alpha").Id));
        At(kernel, 3, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(100), DamageType.Pure));
        At(kernel, 5, ctx => H(ctx.State, "alpha").Position = P(3000, 5000));   // 2600 away

        kernel.RunTicks(8);

        Assert.Equal(0, Lost(kernel.State, "beta").ToDouble(), 0);
        Assert.DoesNotContain(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "song_of_the_siren");
    }

    /// <summary>The AI sings to save itself when retreating with enemies close, not in a fight it is winning.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ai_Sings_OnlyToGetAway(bool hurt)
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("song", "Song of the Siren", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
            new[] { 100000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Area(900, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Modifier(Stasis)) })), new[] { "song" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(hurt ? 800 : 5000);
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);   // a weak enemy: a fight to take when whole
            H(ctx.State, "gamma").Position = P(9000, 9000);   // one on one
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(3 * 30);

        Assert.Equal(hurt, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "song"));
        if (!hurt)
            Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);   // it did fight
    }

    [Fact]
    public void DeathPact_ConsumesTheCreep_ForCurrentAndMaxHealth_TheMaxGoingBackAfter()
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("pact", "Death Pact", AbilityTargeting.Unit, TargetTeam.Enemy, 700, 0, new[] { 30000 },
            new[] { 0 }, new[] { 1 }, new[]
            {
                EffectDefinition.Kill(),
                EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("death_pact", 2000, maxHpBonus: 400)), EffectDefinition.Heal(400)),
            }, targetsCreepsOnly: true)), new[] { "pact" });
        MatchUnit? creep = null;
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(3000);
            creep = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, -1);
            creep.Position = P(5200, 5000);
            Modifiers.Apply(ctx, creep, ModifierSpec.Status("stun", -1, ModifierFlags.Stunned), creep.Id);
        });
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("pact", creep!.Id)));
        int maxDuring = 0;
        double hpDuring = 0;
        At(kernel, 8, ctx =>
        {
            maxDuring = H(ctx.State, "alpha").MaxHp;
            hpDuring = H(ctx.State, "alpha").Hp.ToDouble();
        });

        kernel.RunTicks(8 + 70);

        Assert.False(creep!.IsAlive);
        Assert.Equal(5400, maxDuring);
        Assert.Equal(3400, hpDuring, 0);
        Assert.Equal(5000, H(kernel.State, "alpha").MaxHp);
        Assert.Equal(3400, H(kernel.State, "alpha").Hp.ToDouble(), 0);   // the health stays
    }

    /// <summary>The AI releases its poison once an enemy hero it sees carries four stacks or more, not at the first one.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(4, true)]
    public void Ai_ReleasesThePoison_OnManyStacks(int stacks, bool releases)
    {
        var kernel = Arena(draft => draft.Abilities.Add(new AbilityDefinition("release", "Release", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0, new[] { 1000 },
            new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Release("shadow_poison") })), new[] { "release" });
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
            H(ctx.State, "gamma").Position = P(9000, 9000);
            var poison = new ModifierSpec("shadow_poison", 60000, maxStacks: int.MaxValue,
                onEnd: new[] { EffectDefinition.DamageByStacksDoubling(DamageType.Pure, 5, 60, 10) });
            for (int i = 0; i < stacks; i++)
                Modifiers.Apply(ctx, H(ctx.State, "beta"), poison, H(ctx.State, "alpha").Id);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 * 30);

        Assert.Equal(releases, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "release"));
        if (!releases)
            Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "alpha").Id);   // it did fight
    }
}
