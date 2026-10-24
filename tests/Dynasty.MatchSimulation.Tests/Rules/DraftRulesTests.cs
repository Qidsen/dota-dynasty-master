using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests.Rules;

public class DraftRulesTests
{
    [Fact]
    public void BaseFormat_GivesEachSideFourBansAndFivePicks()
    {
        DraftRules rules = BaseRuleset.Create().Draft!;

        foreach (DraftSide side in new[] { DraftSide.First, DraftSide.Second })
        {
            Assert.Equal(4, rules.Steps.Count(s => s.Side == side && s.Action == DraftAction.Ban));
            Assert.Equal(5, rules.Steps.Count(s => s.Side == side && s.Action == DraftAction.Pick));
        }

        Assert.Equal(DraftAction.Ban, rules.Steps[0].Action);
        Assert.Equal(DraftSide.First, rules.Steps[0].Side);
    }

    [Fact]
    public void Validation_RejectsAFormatWithoutFivePicksPerSide()
    {
        var draft = new RulesetDraft { Draft = new DraftRules(new[] { new DraftStep(DraftSide.First, DraftAction.Pick), new DraftStep(DraftSide.Second, DraftAction.Pick) }) };

        Assert.Contains(draft.Build().Validate(), e => e.Contains("five picks"));
    }

    [Fact]
    public void Validation_RejectsAFormatThatNeedsMoreHeroesThanAvailable()
    {
        // The test ruleset has three heroes; a full draft needs ten picks.
        var draft = new RulesetDraft { Draft = BaseRuleset.Create().Draft };

        Assert.Contains(draft.Build().Validate(), e => e.Contains("need 18 heroes"));
    }

    [Fact]
    public void Validation_RejectsUnknownDisabledHero()
    {
        var rules = new DraftRules(BaseRuleset.Create().Draft!.Steps, disabledHeroes: new[] { "nobody" });
        RulesetContent ruleset = BaseRuleset.Create(rules);

        Assert.Contains(ruleset.Validate(), e => e.Contains("nobody"));
    }

    [Fact]
    public void DraftRules_ArePartOfTheRulesetFingerprint()
    {
        var rules = new DraftRules(BaseRuleset.Create().Draft!.Steps, disabledHeroes: new[] { "ironvow" });

        Assert.NotEqual(BaseRuleset.Create().ToDescriptor().Fingerprint, BaseRuleset.Create(rules).ToDescriptor().Fingerprint);
    }
}
