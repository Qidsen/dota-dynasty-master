using System.Collections;
using Dynasty.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Dynasty.Unity.Tests
{
    public class ViewerTests
    {
        [UnityTest]
        public IEnumerator Board_ShowsExactlyTheUnitsOfTheCurrentFrame_InEveryViewpoint()
        {
            foreach (MatchViewer existing in Object.FindObjectsByType<MatchViewer>(FindObjectsSortMode.None))
                Object.Destroy(existing.gameObject);
            MatchViewer viewer = MatchViewer.Create(seed: 1);
            viewer.Session.Clock.SetSpeed(MatchClock.MaxSpeed);
            for (int i = 0; i < 120; i++)
                yield return null;   // a couple of match minutes

            foreach (Viewpoint viewpoint in new[] { Viewpoint.Observer, Viewpoint.Dawn, Viewpoint.Dusk })
            {
                viewer.Session.Viewpoint = viewpoint;
                yield return null;
                Assert.That(viewer.ShownUnits, Is.EqualTo(viewer.Session.Current.Units.Count), viewpoint.ToString());
            }

            Assert.That(viewer.Session.Kernel.CurrentTick.Value, Is.GreaterThan(1000));
            Object.Destroy(viewer.gameObject);
        }
    }
}
