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
/// Holy Persuasion (Dota 7.41f data and official notes): Chen takes control of an enemy or neutral creep, which follows him and gets
/// its bonuses, and receives a share of its bounty; over the limit of creeps, the oldest one dies.
/// </summary>
public class PersuasionTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static EffectDefinition Persuade(int maxUnits) => EffectDefinition.Persuade("persuaded", new[] { maxUnits }, new[] { 50 },
        EffectDefinition.Modifier(new ModifierSpec("persuaded", -1, moveSpeedBonus: 30, attackDamagePercent: 20)));

    private static SimulationKernel<MatchState> Arena(out Func<MatchState, List<MatchUnit>> creeps)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        var spawned = new List<MatchUnit>();
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(9000, 9000);
            foreach (int x in new[] { 5200, 5300 })
            {
                MatchUnit creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), H(ctx.State, "beta").Team, ctx.State.Ruleset.CreepTypes.First(),
                    ctx.State.Ruleset.Map.Lanes.First(), ctx.Tick.Value);   // a lane creep of beta's
                creep.Position = P(x, 5000);
                Modifiers.Apply(ctx, creep, ModifierSpec.Status("stun", 500, ModifierFlags.Stunned), creep.Id);
                spawned.Add(creep);
            }
        });
        creeps = _ => spawned;
        return kernel;
    }

    [Fact]
    public void TheCreep_JoinsChen_FollowsHim_WithItsBonuses_AndHeGetsAShareOfItsBounty()
    {
        var kernel = Arena(out var creeps);
        Fixed goldBefore = Fixed.Zero;
        At(kernel, 1, ctx => Damage.Execute(ctx, Dynasty.Core.Ids.EntityId.None, creeps(ctx.State)[1]));   // no other enemy to fight
        At(kernel, 2, ctx =>
        {
            goldBefore = H(ctx.State, "alpha").Hero!.Gold;
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Persuade(2) }, 1, creeps(ctx.State)[0], creeps(ctx.State)[0].Position);
        });
        Fixed goldAfter = Fixed.Zero;
        At(kernel, 2, ctx => goldAfter = H(ctx.State, "alpha").Hero!.Gold);   // right after the cast
        At(kernel, 30, ctx => H(ctx.State, "alpha").Position = P(3000, 5000));   // Chen moves away

        kernel.RunTicks(30 + 7 * 30);

        MatchUnit creep = creeps(kernel.State)[0];
        CreepType type = kernel.State.Ruleset.CreepTypes.First();
        Assert.Equal(H(kernel.State, "alpha").Team, creep.Team);
        Assert.Equal(H(kernel.State, "alpha").Id, creep.Owner);
        Assert.Contains(creep.Modifiers, m => m.Spec.Key == "persuaded");
        Assert.True(FixedVector2.Distance(creep.Position, H(kernel.State, "alpha").Position) < Fixed.FromInt(600));   // it followed
        Assert.Equal((type.Bounty.GoldMin + type.Bounty.GoldMax) / 2 * 50 / 100, (goldAfter - goldBefore).ToDouble(), 0);
    }

    [Fact]
    public void OverTheLimit_TheOldestDies()
    {
        var kernel = Arena(out var creeps);
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Persuade(1) }, 1, creeps(ctx.State)[0], P(0, 0)));
        At(kernel, 4, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Persuade(1) }, 1, creeps(ctx.State)[1], P(0, 0)));

        kernel.RunTicks(7);

        Assert.False(creeps(kernel.State)[0].IsAlive);
        Assert.True(creeps(kernel.State)[1].IsAlive);
        Assert.Equal(H(kernel.State, "alpha").Team, creeps(kernel.State)[1].Team);
    }

    /// <summary>The AI persuades a creep in reach whenever it can, whole or not.</summary>
    [Fact]
    public void Ai_Persuades_ACreepInReach() => AiPersuasion(limit: 2, persuades: true);

    /// <summary>At its limit the AI does not persuade another (the oldest would die).</summary>
    [Fact]
    public void Ai_DoesNotPersuade_AtTheLimit() => AiPersuasion(limit: 1, persuades: false);

    private static void AiPersuasion(int limit, bool persuades)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("persuasion", "Holy Persuasion", AbilityTargeting.Unit, TargetTeam.Enemy, 600, 0, new[] { 15000 }, new[] { 0 },
            new[] { 1 }, new[] { Persuade(limit) }, targetsCreepsOnly: true));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "persuasion" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        MatchUnit? creep = null;
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(9000, 9000);
            creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), H(ctx.State, "beta").Team, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes.First(), ctx.Tick.Value);
            creep.Position = P(5300, 5000);
            Modifiers.Apply(ctx, creep, ModifierSpec.Status("stun", -1, ModifierFlags.Stunned), creep.Id);
            // One already held.
            MatchUnit held = ctx.State.SpawnCreep(ctx.Ids.Allocate(), H(ctx.State, "beta").Team, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes.First(), ctx.Tick.Value);
            held.Position = P(4800, 5000);
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Persuade(limit) }, 1, held, held.Position);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(2 * 30);

        Assert.Equal(persuades, creep!.Team == H(kernel.State, "alpha").Team);
    }
}
