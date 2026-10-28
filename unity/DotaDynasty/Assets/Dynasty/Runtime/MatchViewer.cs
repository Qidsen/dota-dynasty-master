#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>
    /// Live match viewer (phase 04): one <see cref="MatchSession"/> shown two ways — the 3D board
    /// and the Tactical 2D map — plus HUD, kill feed and analyst scoreboard. Input changes only how
    /// the match is watched (speed, viewpoint, camera), never the match itself.
    /// </summary>
    public sealed class MatchViewer : MonoBehaviour
    {
        private static readonly int[] Speeds = { 1, 2, 4, 8, 16, 64 };
        /// <summary>Real time a frame may spend computing the match; at higher speeds the match runs as fast as this allows.</summary>
        private const double FrameBudgetMilliseconds = 25;

        private static readonly Viewpoint[] Viewpoints = { Viewpoint.Observer, Viewpoint.Dawn, Viewpoint.Dusk };

        private readonly List<FeedItem> _feed = new List<FeedItem>();
        private readonly Hud _hud = new Hud();
        private MatchSession _session = null!;
        private MatchStatistics _statistics = null!;
        private WorldView _world = null!;
        private BroadcastCamera _camera = null!;
        private TacticalMap _tactical = null!;
        private MapLayout _map = null!;
        private bool _tacticalMode;
        private float _statisticsAge;
        private int _followIndex = -1;

        /// <summary>The demo match (reference line-ups, no draft).</summary>
        public static MatchViewer Create(ulong seed) =>
            Create(MatchSession.Start(BaseRuleset.Create(), seed, DemoMatch.Lineup));

        /// <summary>
        /// A match on the given rules without watching the draft: the base rules play the demo line-ups; on a pack (its heroes are
        /// not the demo line-ups' base heroes) the two demo teams draft at once.
        /// </summary>
        public static MatchViewer Create(ulong seed, RulesetContent ruleset)
        {
            if (ruleset.FindHero(DemoMatch.Lineup.Dawn[0].HeroId) != null)
                return Create(MatchSession.Start(ruleset, seed, DemoMatch.Lineup));
            DraftWatch draft = DraftWatch.Start(ruleset, seed, 1.0);
            draft.Finish();
            return Create(draft.StartMatch(seed));
        }

        /// <summary>Watches a prepared session, e.g. one started from a draft.</summary>
        public static MatchViewer Create(MatchSession session)
        {
            var go = new GameObject("Match Viewer");
            var viewer = go.AddComponent<MatchViewer>();
            viewer.Begin(session);
            return viewer;
        }

        public MatchSession Session => _session;

        /// <summary>Unit visuals currently on the 3D board.</summary>
        public int ShownUnits => _world.ShownUnits;

        private void Begin(MatchSession session)
        {
            RulesetContent ruleset = session.Kernel.State.Ruleset;
            _session = session;
            _statistics = new MatchStatistics();
            _map = MapLayout.From(ruleset);
            _world = new WorldView(_map, LoadMaterial());
            _tactical = new TacticalMap(_map);
            MapFountain? home = null;
            foreach (MapFountain f in _map.Fountains)
            {
                if (f.Team == Team.Dawn)
                    home = f;
            }

            _camera = new BroadcastCamera(home != null ? Palette.World(home.X + 1500f, home.Y + 1500f) : Vector3.zero);
            _world.Show(_session.Current, _session.Poses());
        }

        private void Update()
        {
            ReadInput();
            _session.Update(Time.unscaledDeltaTime, FrameBudgetMilliseconds);
            _feed.AddRange(_session.TakeFeed());
            _statisticsAge += Time.unscaledDeltaTime;
            if (_statisticsAge > 0.5f)
            {
                _statistics.Update(_session.Kernel);
                _statisticsAge = 0f;
            }

            _world.Show(_session.Current, _session.Poses(), _camera.Camera);
            _camera.Camera.enabled = !_tacticalMode;
            _camera.Update(_world, _map.Width / Palette.MapUnitsPerMetre, acceptInput: !_tacticalMode);
        }

        private void ReadInput()
        {
            MatchClock clock = _session.Clock;
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (clock.IsPaused)
                    clock.Resume();
                else
                    clock.Pause();
            }

            if (Input.GetKeyDown(KeyCode.Period))
            {
                clock.Pause();
                clock.StepOnce();
            }

            for (int i = 0; i < Speeds.Length; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    clock.SetSpeed(Speeds[i]);
            }

            if (Input.GetKeyDown(KeyCode.V))
                _session.Viewpoint = Viewpoints[(System.Array.IndexOf(Viewpoints, _session.Viewpoint) + 1) % Viewpoints.Length];
            if (Input.GetKeyDown(KeyCode.M))
                _tacticalMode = !_tacticalMode;
            if (Input.GetKeyDown(KeyCode.F))
                FollowNextHero();
        }

        private void FollowNextHero()
        {
            var heroes = new List<UnitFrame>();
            foreach (UnitFrame unit in _session.Current.Units)
            {
                if (unit.Kind == UnitKind.Hero)
                    heroes.Add(unit);
            }

            if (heroes.Count == 0)
                return;
            _followIndex = (_followIndex + 1) % heroes.Count;
            _camera.Following = heroes[_followIndex].Id;
        }

        private void OnGUI()
        {
            MatchFrame frame = _session.Current;
            if (_tacticalMode)
            {
                float size = Mathf.Min(Screen.width, Screen.height) - 100f;
                GUI.color = new Color(0.06f, 0.07f, 0.09f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                _tactical.Draw(new Rect((Screen.width - size) / 2f, 72f, size, size), frame, _camera.Following);
            }
            else
            {
                float size = Mathf.Min(300f, Screen.height * 0.35f);
                _tactical.Draw(new Rect(12f, Screen.height - size - 36f, size, size), frame, _camera.Following);
            }

            _hud.DrawTopBar(frame, _session.Clock, _tacticalMode ? "tactical 2D" : "3D broadcast");
            _hud.DrawFeed(_feed, _session.Kernel.Config.TickRate.TicksPerSecond);
            _hud.DrawHelp();
            if (Input.GetKey(KeyCode.Tab))
                _hud.DrawScoreboard(_statistics.Heroes, frame.Viewpoint, line => _session.PlayerHandle(line.Team, line.DefinitionId));
        }

        /// <summary>
        /// Verification aid (<c>-dynasty-screenshots &lt;dir&gt;</c>): fast-forwards to the given match minute,
        /// saves a 3D and a tactical screenshot for each viewpoint, then quits.
        /// </summary>
        public IEnumerator CaptureAndQuit(string directory, int minute)
        {
            Directory.CreateDirectory(directory);
            _session.Clock.SetSpeed(MatchClock.MaxSpeed);
            long target = (long)minute * 60 * _session.Kernel.Config.TickRate.TicksPerSecond;
            while (_session.Kernel.CurrentTick.Value < target && !_session.IsFinished)
                _session.Update(1.0);
            _session.Clock.Pause();
            _feed.AddRange(_session.TakeFeed());
            _statistics.Update(_session.Kernel);
            _camera.LookAt(Palette.World(5000f, 5000f));

            foreach (Viewpoint viewpoint in Viewpoints)
            {
                _session.Viewpoint = viewpoint;
                foreach (bool tactical in new[] { false, true })
                {
                    _tacticalMode = tactical;
                    for (int i = 0; i < 3; i++)
                        yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(directory, $"{viewpoint}-{(tactical ? "tactical" : "3d")}.png"));
                    yield return new WaitForEndOfFrame();
                }
            }

            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(0);
        }

        private static Material LoadMaterial()
        {
            var material = Resources.Load<Material>("DynastyLit");
            if (material != null)
                return material;
            Debug.LogWarning("Resources/DynastyLit missing (run Dynasty/Setup URP); falling back to Shader.Find.");
            return new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }
    }
}
