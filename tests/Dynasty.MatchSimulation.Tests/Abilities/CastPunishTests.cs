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
/// Damage for casting (Dota 7.41f data and official notes on Nether Ward): an enemy hero who casts a spell while under the ward takes
/// the base damage plus a share of the mana spent, from the ward, before the spell; if that kills it, the spell has no effect.
/// </summary>
public class CastPunishTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Punish = new ModifierSpec("nether_ward_aura", 60000, castDamage: 50, castDamageManaPercent: 160);

    /// <summary>beta (with a 100-mana nuke) against alpha; gamma is alpha's ally far away.</summary>
    private static SimulationKernel<MatchState> Duel(int betaHp = 2000, bool punished = true, bool sourceDies = false, bool byItem = false)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("nuke", "Nuke", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0, new[] { 10000 }, new[] { 100 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 300) }));
        draft.Abilities.Add(new AbilityDefinition("dagon", "Dagon", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0, new[] { 10000 }, new[] { 100 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 300) }));
        draft.Items.Add(new ItemDefinition("dagon_item", "Dagon", 0, activeAbilityId: "dagon"));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "beta" ? new[] { "nuke" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5500, 5000);
            H(ctx.State, "gamma").Position = P(2000, 2000);
            H(ctx.State, "beta").Hp = Fixed.FromInt(betaHp);
            HeroShop.Give(ctx, H(ctx.State, "beta"), ctx.State.Ruleset.FindItem("dagon_item")!);
        });
        if (punished)
            At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), Punish, H(ctx.State, "gamma").Id));
        if (sourceDies)
            At(kernel, 2, ctx => Damage.Execute(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "gamma")));
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Cast(byItem ? "dagon" : "nuke", H(ctx.State, "alpha").Id)));
        return kernel;
    }

    private static Fixed Lost(MatchUnit unit, int from) => Fixed.FromInt(from) - unit.Hp;

    [Fact]
    public void Caster_TakesBaseDamagePlusAShareOfTheManaSpent_FromTheSource()
    {
        var kernel = Duel();
        var control = Duel(punished: false);

        kernel.RunTicks(10);
        control.RunTicks(10);

        Fixed punished = Lost(H(kernel.State, "beta"), 2000) - Lost(H(control.State, "beta"), 2000);
        DamageDealt hit = kernel.EventsOf<DamageDealt>().Single(d => d.Target == H(kernel.State, "beta").Id);
        Assert.Equal(H(kernel.State, "gamma").Id, hit.Attacker);
        Assert.Equal(DamageType.Magical, hit.DamageType);
        // 50 + 160% of 100 mana = 210 before the hero's magic resistance.
        Assert.Equal(210 * (10000 - H(kernel.State, "beta").MagicResistHundredths) / 10000.0, punished.ToDouble(), 0);
        Assert.Equal(300, Lost(H(kernel.State, "alpha"), 2000).ToDouble(), 0);   // the spell itself lands
    }

    [Fact]
    public void Caster_KilledByIt_CastsNothing()
    {
        var kernel = Duel(betaHp: 100);

        kernel.RunTicks(10);

        Assert.False(H(kernel.State, "beta").IsAlive);
        Assert.Equal(0, Lost(H(kernel.State, "alpha"), 2000).ToDouble(), 0);
    }

    [Fact]
    public void Nothing_WhenTheSourceIsDead()
    {
        var kernel = Duel(sourceDies: true);

        kernel.RunTicks(10);

        Assert.Equal(0, Lost(H(kernel.State, "beta"), 2000).ToDouble(), 0);
    }

    /// <summary>The official note speaks of spells: items are not punished.</summary>
    [Fact]
    public void Nothing_ForAnItem()
    {
        var kernel = Duel(byItem: true);

        kernel.RunTicks(10);

        Assert.Equal(300, Lost(H(kernel.State, "alpha"), 2000).ToDouble(), 0);   // the item was used
        Assert.Equal(0, Lost(H(kernel.State, "beta"), 2000).ToDouble(), 0);
    }
}
