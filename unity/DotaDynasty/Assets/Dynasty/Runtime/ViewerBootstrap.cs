#nullable enable
using System;
using System.Globalization;
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>
    /// Starts the viewer when the match scene loads, unless the player runs the determinism probe.
    /// Command line: <c>-dynasty-seed N</c>; <c>-dynasty-screenshots DIR [-dynasty-minute M]</c> for automated checks.
    /// </summary>
    public static class ViewerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Value(args, DeterminismRunner.Argument) != null || UnityEngine.Object.FindAnyObjectByType<MatchViewer>() != null)
                return;

            ulong seed = ulong.TryParse(Value(args, "-dynasty-seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong s) ? s : 1;
            MatchViewer viewer = MatchViewer.Create(seed);

            string? screenshots = Value(args, "-dynasty-screenshots");
            if (screenshots != null)
            {
                int minute = int.TryParse(Value(args, "-dynasty-minute"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ? m : 10;
                viewer.StartCoroutine(viewer.CaptureAndQuit(screenshots, minute));
            }
        }

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
