using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Heroes
{
    /// <summary>Hero actions outside the order system; requests are validated and carried out on the next tick.</summary>
    public static class HeroActions
    {
        /// <summary>Requests an instant respawn for gold (see <see cref="Rules.HeroRules.BuybackCost"/>).</summary>
        public static void QueueBuyback(MatchUnit hero) => hero.Hero!.BuybackRequested = true;
    }

    /// <summary>Team actions; requests are validated and carried out on the next tick.</summary>
    public static class TeamActions
    {
        /// <summary>Requests the team's Glyph of Fortification (ignored while on cooldown).</summary>
        public static void QueueGlyph(MatchState state, Team team) => state.RequestGlyph(team);
    }
}
