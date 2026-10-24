#nullable enable
using System;
using System.IO;
using Dynasty.Presentation;
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>
    /// Player-side determinism check: started with <c>-dynasty-probe &lt;file&gt;</c>, the player plays the
    /// demo matches headless, writes their checksums to the file and quits. The build tools compare
    /// the file with the .NET reference (StreamingAssets/determinism-reference.txt).
    /// </summary>
    public static class DeterminismRunner
    {
        public const string Argument = "-dynasty-probe";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RunIfRequested()
        {
            string? output = OutputPath(Environment.GetCommandLineArgs());
            if (output == null)
                return;

            int exitCode = 0;
            try
            {
                File.WriteAllText(output, DeterminismProbe.Format(DeterminismProbe.RunDefault()));
                Debug.Log("Determinism probe written to " + output);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                exitCode = 1;
            }

            Application.Quit(exitCode);
        }

        private static string? OutputPath(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == Argument)
                    return args[i + 1];
            }

            return null;
        }
    }
}
