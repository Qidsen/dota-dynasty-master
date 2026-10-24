#nullable enable
using System.Collections;
using System.IO;
using System.Linq;
using Dynasty.Drafting;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>
    /// Draft room (placeholder look, IMGUI): two AI teams ban and pick step by step, each choice
    /// with its reasons; at the end both game plans; Enter starts the match with the drafted heroes
    /// playing their plans. Space pauses, 1–3 change the pace, Enter also skips to the end.
    /// </summary>
    public sealed class DraftScreen : MonoBehaviour
    {
        private static readonly double[] Paces = { 2.0, 0.8, 0.25 };

        private DraftWatch _watch = null!;
        private RulesetContent _ruleset = null!;
        private ulong _seed;
        private bool _paused;
        private double _pace = Paces[1];
        private GUIStyle? _label;
        private GUIStyle? _title;
        private GUIStyle? _panel;

        public static DraftScreen Create(ulong seed)
        {
            var go = new GameObject("Draft Screen");
            var screen = go.AddComponent<DraftScreen>();
            screen._seed = seed;
            screen._ruleset = BaseRuleset.Create();
            screen._watch = DraftWatch.Start(screen._ruleset, seed, 1.0);
            var camera = new GameObject("Draft Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            camera.transform.SetParent(go.transform);
            return screen;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                _paused = !_paused;
            for (int i = 0; i < Paces.Length; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    _pace = Paces[i];
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_watch.Session.IsComplete)
                    StartMatch();
                else
                    _watch.Finish();
                return;
            }

            if (!_paused)
                _watch.Update(Time.unscaledDeltaTime / _pace);
        }

        private void StartMatch()
        {
            MatchViewer.Create(_watch.StartMatch(_seed));
            Destroy(gameObject);
        }

        private void OnGUI()
        {
            Styles();
            DraftSession session = _watch.Session;
            string first = session.FirstPick == Team.Dawn ? _watch.DawnRoster.Name : _watch.DuskRoster.Name;
            string state = session.IsComplete ? "draft complete — Enter: start the match" :
                $"step {session.StepIndex + 1}/{session.Rules.Steps.Count}: {(session.TeamToAct == Team.Dawn ? _watch.DawnRoster.Name : _watch.DuskRoster.Name)} {session.ActionToTake?.ToString().ToLowerInvariant()}s{(_paused ? "  (paused)" : "")}";
            GUI.Label(new Rect(0, 12, Screen.width, 34), $"DRAFT   {_watch.DawnRoster.Name}  vs  {_watch.DuskRoster.Name}", Centered(_title!));
            GUI.Label(new Rect(0, 46, Screen.width, 22), $"first pick: {first}    {state}", Centered(_label!));

            float column = 360f;
            DrawTeam(new Rect(20, 90, column, Screen.height - 140), Team.Dawn, _watch.DawnRoster, _watch.DawnPlan);
            DrawTeam(new Rect(Screen.width - column - 20, 90, column, Screen.height - 140), Team.Dusk, _watch.DuskRoster, _watch.DuskPlan);
            DrawCenter(new Rect(column + 40, 90, Screen.width - 2 * column - 80, Screen.height - 140));

            GUI.Label(new Rect(12, Screen.height - 26, Screen.width, 22), "Space pause   1-3 pace   Enter skip to the end / start the match", _label);
        }

        private void DrawTeam(Rect area, Team team, TeamRoster roster, GamePlan? plan)
        {
            GUI.Box(area, GUIContent.none, _panel);
            DraftSession session = _watch.Session;
            float x = area.x + 14, y = area.y + 10, w = area.width - 28;
            GUI.color = Palette.Of(team);
            GUI.Label(new Rect(x, y, w, 28), roster.Name + "  (" + team + ")", _title);
            GUI.color = Color.white;
            y += 36;
            GUI.Label(new Rect(x, y, w, 44), "Bans: " + string.Join(", ", session.BansOf(team).Select(Name)), Wrapped(_label!));
            y += 50;

            PositionPlan? positions = session.PicksOf(team).Count == 5 ? PositionPlan.For(_ruleset, team, session.PicksOf(team)) : null;
            for (int i = 0; i < 5; i++)
            {
                string line;
                if (positions != null)
                {
                    string hero = positions.HeroAt(i + 1);
                    RosterPlayer player = roster.At(i + 1);
                    HeroPoolEntry? entry = player.Entry(hero);
                    line = $"pos {i + 1}  {Name(hero),-13} {player.Name} ({(entry != null ? entry.Mastery.ToString() : "new")})";
                }
                else
                {
                    line = i < session.PicksOf(team).Count ? $"pick {i + 1}  {Name(session.PicksOf(team)[i])}" : $"pick {i + 1}  —";
                }

                GUI.Label(new Rect(x, y, w, 22), line, _label);
                y += 24;
            }

            if (plan == null)
                return;
            y += 12;
            GUI.color = Palette.Of(team);
            GUI.Label(new Rect(x, y, w, 44), $"Plan: {plan.Archetype}, strongest around minute {plan.PowerPeakMinute}", Wrapped(_label!));
            GUI.color = Color.white;
            y += 48;
            GUI.Label(new Rect(x, y, w, 60), "Strengths: " + (plan.Strengths.Count == 0 ? "—" : string.Join(", ", plan.Strengths)), Wrapped(_label!));
            y += 48;
            GUI.Label(new Rect(x, y, w, 60), "Weaknesses: " + (plan.Weaknesses.Count == 0 ? "—" : string.Join(", ", plan.Weaknesses)), Wrapped(_label!));
        }

        private void DrawCenter(Rect area)
        {
            GUI.Box(area, GUIContent.none, _panel);
            float x = area.x + 14, y = area.y + 10, w = area.width - 28;
            DraftCandidate? last = _watch.LastChoice;
            if (last != null && _watch.LastTeam is Team team)
            {
                bool ban = _watch.Session.BansOf(team).Contains(last.HeroId);
                GUI.color = Palette.Of(team);
                GUI.Label(new Rect(x, y, w, 26), $"{(team == Team.Dawn ? _watch.DawnRoster.Name : _watch.DuskRoster.Name)} {(ban ? "bans" : "picks")} {Name(last.HeroId)}", _title);
                GUI.color = Color.white;
                y += 32;
                GUI.Label(new Rect(x, y, w, 22), ban
                    ? $"their comfort {last.Comfort}, threat {last.Counter}, meta {last.Meta}"
                    : $"comfort {last.Comfort}, composition {last.Composition}, counters {last.Counter}, synergy {last.Synergy}, meta {last.Meta}", _label);
                y += 24;
                GUI.Label(new Rect(x, y, w, 22), "also considered: " + string.Join(", ", _watch.LastAlternatives.Select(c => Name(c.HeroId))), _label);
                y += 36;
            }

            GUI.Label(new Rect(x, y, w, 22), "Available heroes", _label);
            y += 26;
            int columns = Mathf.Max(1, (int)(w / 170f));
            int index = 0;
            foreach (string hero in _watch.Session.AvailableHeroes)
            {
                HeroDefinition definition = _ruleset.FindHero(hero)!;
                float cx = x + index % columns * 170f;
                float cy = y + index / columns * 40f;
                GUI.Label(new Rect(cx, cy, 165, 20), definition.Name, _label);
                GUI.color = new Color(0.7f, 0.7f, 0.75f);
                GUI.Label(new Rect(cx, cy + 17, 165, 20), string.Join(", ", definition.Roles), Small());
                GUI.color = Color.white;
                index++;
            }
        }

        /// <summary>Verification aid: runs the draft to the end, saves a screenshot, quits.</summary>
        public IEnumerator CaptureAndQuit(string directory)
        {
            Directory.CreateDirectory(directory);
            for (int i = 0; i < 6; i++)
            {
                _watch.Update(1.0);
                yield return null;
            }

            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "draft-progress.png"));
            yield return null;
            _watch.Finish();
            for (int i = 0; i < 3; i++)
                yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "draft-complete.png"));
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(0);
        }

        private string Name(string heroId) => _ruleset.FindHero(heroId)?.Name ?? heroId;

        private void Styles()
        {
            if (_label != null)
                return;
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, new Color(0.10f, 0.11f, 0.14f, 0.95f));
            texture.Apply();
            _panel = new GUIStyle(GUI.skin.box) { normal = { background = texture } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white }, font = Font.CreateDynamicFontFromOSFont("Consolas", 15) };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        }

        private GUIStyle Small() => new GUIStyle(_label!) { fontSize = 12 };

        private static GUIStyle Centered(GUIStyle style) => new GUIStyle(style) { alignment = TextAnchor.MiddleCenter };

        private static GUIStyle Wrapped(GUIStyle style) => new GUIStyle(style) { wordWrap = true };
    }
}
