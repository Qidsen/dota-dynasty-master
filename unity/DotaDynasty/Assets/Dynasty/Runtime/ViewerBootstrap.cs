#nullable enable
using System;
using System.Globalization;
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>
    /// Starts the draft room (then the match) when the scene loads, unless the player runs the determinism probe.
    /// Command line: <c>-dynasty-seed N</c>, <c>-dynasty-skip-draft</c>; for automated checks
    /// <c>-dynasty-screenshots DIR [-dynasty-minute M]</c> (demo match) and <c>-dynasty-draft-screenshots DIR</c>.
    /// Without a seed an interactive run picks a random one (shown on screen, so a match can be replayed);
    /// the automated checks default to seed 1.
    /// </summary>
    public static class ViewerBootstrap
    {
        /// <summary>Seed of the draft and the match on screen.</summary>
        public static ulong Seed { get; private set; } = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Value(args, DeterminismRunner.Argument) != null || UnityEngine.Object.FindAnyObjectByType<MatchViewer>() != null)
                return;

            string? draftShots = Value(args, "-dynasty-draft-screenshots");
            string? screenshots = Value(args, "-dynasty-screenshots");
            bool automated = draftShots != null || screenshots != null;
            ulong seed = ulong.TryParse(Value(args, "-dynasty-seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong s) ? s
                : automated ? 1UL : (ulong)new System.Random().Next(1, 1000000);   // the viewer, not the simulation: the seed is its input
            Seed = seed;

            if (draftShots != null)
            {
                DraftScreen draft = DraftScreen.Create(seed);
                draft.StartCoroutine(draft.CaptureAndQuit(draftShots));
                return;
            }

            if (screenshots == null && !Has(args, "-dynasty-skip-draft"))
            {
                DraftScreen.Create(seed);   // the draft first; it starts the match
                return;
            }

            MatchViewer viewer = MatchViewer.Create(seed);
            if (screenshots != null)
            {
                int minute = int.TryParse(Value(args, "-dynasty-minute"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ? m : 10;
                viewer.StartCoroutine(viewer.CaptureAndQuit(screenshots, minute));
            }
        }

        private static bool Has(string[] args, string name) => System.Array.IndexOf(args, name) >= 0;

        private static string? Value(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }

            return null;
        }
    }
}
