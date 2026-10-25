using System.IO;
using System.Linq;
using Dynasty.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Dynasty.Unity.Tests
{
    /// <summary>The core plays the probe matches (demo, full, drafted with players) in the Unity editor (Mono) exactly as in .NET.</summary>
    public class DeterminismTests
    {
        [Test]
        public void DemoMatches_InMono_MatchTheDotNetReference()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "determinism-reference.txt");
            Assert.That(File.Exists(path), "Run tools/sync-unity.ps1 first: " + path);
            var reference = DeterminismProbe.Parse(File.ReadAllText(path));

            var actual = reference.Select(DeterminismProbe.Replay).ToList();

            Assert.That(DeterminismProbe.Format(actual), Is.EqualTo(DeterminismProbe.Format(reference)));
        }
    }
}
