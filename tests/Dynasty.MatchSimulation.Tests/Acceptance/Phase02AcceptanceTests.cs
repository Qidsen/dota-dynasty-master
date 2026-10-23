using Dynasty.Core.Events;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests.Acceptance;

/// <summary>
/// Phase 02 acceptance: on the built-in ruleset, ten AI-controlled original heroes play 5v5 and
/// each hero's kit shows up as distinguishable behaviour in the event stream.
/// </summary>
public class Phase02AcceptanceTests
{
    private const int TicksPerMinute = 30 * 60;

    private static readonly MatchLineup Lineup = new(
        new[]
        {
            new HeroPick("ironvow", "bot"), new HeroPick("dawnmender", "bot"), new HeroPick("emberwisp", "mid"),
            new HeroPick("stonewarden", "top"), new HeroPick("thornkeeper", "top"),
        },
        new[]
        {
            new HeroPick("starborn", "top"), new HeroPick("shadeblade", "top"), new HeroPick("frostweaver", "mid"),
            new HeroPick("hivecaller", "bot"), new HeroPick("siegewright", "bot"),
        });

    private static SimulationKernel<MatchState> Play(ulong seed, int minutes, MatchLineup? lineup = null)
    {
        var kernel = Match.Create(BaseRuleset.Create(), seed, lineup ?? Lineup);
        HeroAi.Attach(kernel);
        kernel.RunTicks(minutes * TicksPerMinute);
        return kernel;
    }

    private static readonly Lazy<List<SimulationKernel<MatchState>>> Matches =
        new(() => new List<SimulationKernel<MatchState>> { Play(1, 20), Play(2, 20) });

    private static IEnumerable<T> All<T>() =>
        Matches.Value.SelectMany(k => k.Events.Records.Select(r => r.Event).OfType<T>());

    private static HashSet<EntityId> IdsOf(string heroId) =>
        Matches.Value.SelectMany(k => k.State.Heroes.Where(h => h.Hero!.Definition.Id == heroId).Select(h => h.Id)).ToHashSet();

    [Theory]
    [InlineData("ironvow", "ironvow.iron_will")]
    [InlineData("emberwisp", "emberwisp.fire_lance")]
    [InlineData("stonewarden", "stonewarden.ram")]
    [InlineData("frostweaver", "frostweaver.frost_shackle")]
    [InlineData("dawnmender", "dawnmender.mend")]
    [InlineData("hivecaller", "hivecaller.brood")]
    [InlineData("shadeblade", "shadeblade.shadow_step")]
    [InlineData("siegewright", "siegewright.mortar")]
    [InlineData("thornkeeper", "thornkeeper.bramble")]
    [InlineData("starborn", "starborn.star_bolt")]
    public void EveryHero_UsesItsSignatureAbility(string heroId, string abilityId)
    {
        HashSet<EntityId> hero = IdsOf(heroId);

        Assert.Contains(All<AbilityCast>(), c => hero.Contains(c.Caster) && c.AbilityId == abilityId);
    }

    [Fact]
    public void Healer_IsTheOnlyHeroHealingOthers()
    {
        HashSet<EntityId> healer = IdsOf("dawnmender");
        var healsOnOthers = All<HealApplied>().Where(h => h.Source != h.Target && h.Source.IsValid).ToList();

        Assert.Contains(healsOnOthers, h => healer.Contains(h.Source));
        HashSet<EntityId> heroes = Matches.Value.SelectMany(k => k.State.Heroes.Select(h => h.Id)).ToHashSet();
        var otherHeroHealers = healsOnOthers.Where(h => heroes.Contains(h.Source) && !healer.Contains(h.Source))
            .Select(h => h.Source).Distinct().ToList();
        // Thornkeeper's Barkskin heals over time too; nobody else may heal allies.
        HashSet<EntityId> thorn = IdsOf("thornkeeper");
        Assert.All(otherHeroHealers, id => Assert.Contains(id, thorn));
    }

    [Fact]
    public void Summoners_AndOnlySummoners_CreateUnits()
    {
        var owners = All<UnitSummoned>().Select(s => s.Owner).ToHashSet();
        HashSet<EntityId> summoners = IdsOf("hivecaller").Concat(IdsOf("siegewright")).ToHashSet();

        Assert.NotEmpty(owners);
        Assert.Subset(summoners, owners);
        Assert.Contains(owners, o => IdsOf("hivecaller").Contains(o));
    }

    [Fact]
    public void Initiators_StunEnemyHeroes()
    {
        HashSet<EntityId> enemyHeroes = Matches.Value.SelectMany(k => k.State.Heroes.Select(h => h.Id)).ToHashSet();
        var stunKeys = All<ModifierApplied>().Where(m => enemyHeroes.Contains(m.Target)).Select(m => m.Key).ToHashSet();

        Assert.Contains("stonewarden.ram.stun", stunKeys);
        Assert.Contains("frostweaver.shackle", stunKeys);
    }

    [Fact]
    public void DamageProfiles_ReflectTheKits()
    {
        double Share(string heroId, DamageType type)
        {
            HashSet<EntityId> hero = IdsOf(heroId);
            var dealt = All<DamageDealt>().Where(d => hero.Contains(d.Attacker)).ToList();
            return dealt.Where(d => d.DamageType == type).Sum(d => d.Amount.ToDouble()) / dealt.Sum(d => d.Amount.ToDouble());
        }

        Assert.True(Share("emberwisp", DamageType.Magical) > Share("ironvow", DamageType.Magical) + 0.1);
        Assert.True(Share("ironvow", DamageType.Physical) > 0.9);
        HashSet<EntityId> crits = All<DamageDealt>().Where(d => d.IsCritical).Select(d => d.Attacker).ToHashSet();
        Assert.Contains(crits, IdsOf("ironvow").Contains);
        Assert.Contains(crits, IdsOf("shadeblade").Contains);
        Assert.DoesNotContain(crits, IdsOf("dawnmender").Contains);
    }

    [Fact]
    public void FiveVersusFive_KeepsMatchInvariants()
    {
        foreach (SimulationKernel<MatchState> kernel in Matches.Value)
        {
            MatchState state = kernel.State;
            Assert.Equal(10, state.Heroes.Count());
            foreach (MatchUnit hero in state.Heroes)
            {
                Assert.True(hero.Hero!.Level >= 5, $"{hero.TypeId} only reached level {hero.Hero.Level}");
                Assert.True(hero.Hero.LastHits > 0 || hero.Hero.Assists + hero.Hero.Kills > 0, $"{hero.TypeId} did nothing");
                Assert.True(hero.Hero.Gold >= Fixed.Zero);
                Assert.True(hero.Hp <= Fixed.FromInt(hero.MaxHp));
            }

            foreach (MatchUnit unit in state.Units)
            {
                Assert.InRange(unit.Position.X.Raw, 0, Fixed.FromInt(10000).Raw);
                Assert.InRange(unit.Position.Y.Raw, 0, Fixed.FromInt(10000).Raw);
            }

            MatchTestKit.AssertProtectionOrderRespected(kernel);
        }
    }

    [Fact]
    public void MirrorMatch_FavoursNeitherSide()
    {
        HeroPick[] dawn =
        {
            new("ironvow", "bot"), new("dawnmender", "bot"), new("emberwisp", "mid"),
            new("stonewarden", "top"), new("thornkeeper", "top"),
        };
        HeroPick[] dusk = dawn.Select(p => new HeroPick(p.HeroId, p.LaneId == "top" ? "bot" : p.LaneId == "bot" ? "top" : p.LaneId)).ToArray();
        var mirror = new MatchLineup(dawn, dusk);

        int dawnKills = 0, duskKills = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4 })
        {
            var kernel = Play(seed, 15, mirror);
            dawnKills += kernel.State.Heroes.Where(h => h.Team == Team.Dawn).Sum(h => h.Hero!.Kills);
            duskKills += kernel.State.Heroes.Where(h => h.Team == Team.Dusk).Sum(h => h.Hero!.Kills);
        }

        double dawnShare = dawnKills / (double)(dawnKills + duskKills);
        Assert.InRange(dawnShare, 0.35, 0.65);
    }

    [Fact]
    public void ControlledFiveVersusFive_IsReproducible()
    {
        Assert.Equal(Play(9, 5).Events.Checksum, Play(9, 5).Events.Checksum);
    }
}
