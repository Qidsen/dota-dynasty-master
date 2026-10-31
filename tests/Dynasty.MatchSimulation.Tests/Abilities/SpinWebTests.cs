using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Spin Web (Dota 7.41f data and the official description): webs never expire, the oldest goes over the limit; within a web Broodmother
/// (and her own units) moves faster - not her allies; webs cannot be attacked.
/// </summary>
public class SpinWebTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec WebSpeed = new ModifierSpec("spin_web", 1000, moveSpeedPercent: 40, unique: true);

    private static readonly EffectDefinition Web = EffectDefinition.Summon("web", -1, true, new[] { 1 }, new[] { 2 },
        EffectDefinition.Modifier(new ModifierSpec("web", -1, ModifierFlags.Invulnerable, dispellable: false,
            aura: new AuraSpec(900, TargetTeam.Ally, heroesOnly: false, WebSpeed, ownerOnly: true))));

    private static SimulationKernel<MatchState> Field(Action<RulesetDraft>? setup = null, string[]? alphaAbilities = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("web", new UnitStats(100, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16), new Bounty(0, 0, 0)));
        setup?.Invoke(draft);
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? alphaAbilities : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 3000);
            H(ctx.State, "gamma").Position = P(3300, 3000);
            H(ctx.State, "beta").Position = P(3000, 3300);
        });
        return kernel;
    }

    private static List<MatchUnit> Webs(MatchState state) => state.Units.Where(u => u.TypeId == "web" && u.IsAlive).ToList();

    [Fact]
    public void WithinItsWeb_BroodmotherIsFaster_NotHerAllyOrEnemy_AndTheWebCannotBeHurt()
    {
        var kernel = Field();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Web }, 1, H(ctx.State, "alpha"), P(3200, 3000)));
        At(kernel, 3, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, Webs(ctx.State)[0], Fixed.FromInt(1000), DamageType.Pure));

        kernel.RunTicks(2 + 20);

        Assert.Equal(420, H(kernel.State, "alpha").MoveSpeed);
        Assert.Equal(300, H(kernel.State, "gamma").MoveSpeed);
        Assert.Equal(300, H(kernel.State, "beta").MoveSpeed);
        Assert.Single(Webs(kernel.State));
    }

    [Fact]
    public void TwoWebs_DoNotAddUp_AndOutsideThemThereIsNoBonus()
    {
        var kernel = Field();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Web }, 1, H(ctx.State, "alpha"), P(3200, 3000)));
        At(kernel, 3, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Web }, 1, H(ctx.State, "alpha"), P(2800, 3000)));
        int both = 0;
        At(kernel, 20, ctx => both = H(ctx.State, "alpha").MoveSpeed);
        At(kernel, 21, ctx => H(ctx.State, "alpha").Position = P(8000, 3000));

        kernel.RunTicks(21 + 60);

        Assert.Equal(420, both);
        Assert.Equal(300, H(kernel.State, "alpha").MoveSpeed);
    }

    /// <summary>The AI spins a web under itself when it stands on none of its own, not when it does, nor below half its mana.</summary>
    [Theory]
    [InlineData(false, 500, true)]
    [InlineData(true, 500, false)]
    [InlineData(false, 200, false)]
    public void Ai_SpinsAWebUnderItself_WhenOnNone(bool onWeb, int mana, bool spins)
    {
        var kernel = Field(draft => draft.Abilities.Add(new AbilityDefinition("spin_web", "Spin Web", AbilityTargeting.Point, TargetTeam.Enemy, 1000, 40,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { Web })), new[] { "spin_web" });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "beta").Position = P(9000, 9000);
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(mana);
            if (onWeb)
                Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Web }, 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30);

        Assert.Equal(spins, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "spin_web"));
    }
}
