using System.IO;
using System.Linq;
using Dynasty.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Dynasty.Unity.Tests
{
    /// <summary>
    /// The core plays the probe matches (demo, full, drafted with players, and drafted on the Dota pack when it is bundled) in the
    /// Unity editor (Mono) exactly as in .NET.
    /// </summary>
    public class DeterminismTests
    {
        [Test]
        [Timeout(15 * 60 * 1000)]   // nine matches, two of them on the Dota pack (one to its end)
        public void DemoMatches_InMono_MatchTheDotNetReference()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "determinism-reference.txt");
            Assert.That(File.Exists(path), "Run tools/sync-unity.ps1 first: " + path);
            var reference = DeterminismProbe.Parse(File.ReadAllText(path));

            var pack = ViewerBootstrap.BundledPack();   // the Dota pack's runs are in the reference when the pack is in StreamingAssets
            var actual = reference.Select(r => DeterminismProbe.Replay(r, pack)).ToList();

            Assert.That(DeterminismProbe.Format(actual), Is.EqualTo(DeterminismProbe.Format(reference)));
        }
    }
}
