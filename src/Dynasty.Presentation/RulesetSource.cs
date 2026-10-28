using System.IO;
using Dynasty.Content;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Presentation
{
    /// <summary>
    /// The rules a viewer plays (backlog B-118): the base ruleset, or the base with a ruleset pack (<c>dynasty.ruleset-pack</c>,
    /// such as the locally generated Dota pack) read from a file and applied over it.
    /// </summary>
    public static class RulesetSource
    {
        /// <summary>
        /// The base rules when <paramref name="packPath"/> is null; otherwise the pack in that file applied over them. A missing
        /// file or a bad pack is an error (<see cref="FileNotFoundException"/>, <see cref="RulesetPackException"/>), never quietly the base rules.
        /// </summary>
        public static RulesetContent Load(string? packPath)
        {
            RulesetContent baseRules = BaseRuleset.Create();
            if (packPath == null)
                return baseRules;
            if (!File.Exists(packPath))
                throw new FileNotFoundException("Ruleset pack not found.", packPath);
            return RulesetPack.Apply(baseRules, File.ReadAllText(packPath));
        }
    }
}
