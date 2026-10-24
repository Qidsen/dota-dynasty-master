#nullable enable
using System.Collections.Generic;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>
    /// Match HUD (placeholder look, IMGUI): score, clock, speed and viewpoint, kill feed, analyst
    /// scoreboard (spec §13.3) and a controls hint. A team viewpoint hides the enemy's economy.
    /// </summary>
    public sealed class Hud
    {
        private GUIStyle? _label;
        private GUIStyle? _big;
        private GUIStyle? _panel;
        private Texture2D? _panelTexture;

        public void DrawTopBar(MatchFrame frame, MatchClock clock, string mode)
        {
            Styles();
            int minutes = (int)(frame.TimeSeconds / 60f);
            int seconds = (int)frame.TimeSeconds % 60;
            string speed = clock.IsPaused ? "PAUSED" : "x" + clock.Speed;
            float w = 520f;
            var area = new Rect((Screen.width - w) / 2f, 8f, w, 58f);
            GUI.Box(area, GUIContent.none, _panel);
            GUI.color = Palette.Dawn;
            GUI.Label(new Rect(area.x + 16f, area.y + 6f, 150f, 32f), "Dawn  " + frame.DawnKills, _big);
            GUI.color = Palette.Dusk;
            GUI.Label(new Rect(area.xMax - 166f, area.y + 6f, 150f, 32f), frame.DuskKills + "  Dusk", RightAligned(_big!));
            GUI.color = Color.white;
            GUI.Label(new Rect(area.x, area.y + 4f, w, 30f), $"{minutes:00}:{seconds:00}  {(frame.IsNight ? "night" : "day")}", Centered(_big!));
            GUI.Label(new Rect(area.x, area.y + 34f, w, 20f), $"{speed}   view: {frame.Viewpoint}   {mode}", Centered(_label!));
            if (frame.Winner is Team winner)
            {
                GUI.color = Palette.Of(winner);
                GUI.Label(new Rect(0f, Screen.height * 0.4f, Screen.width, 60f), winner + " wins", Centered(_big!));
                GUI.color = Color.white;
            }
        }

        public void DrawFeed(IReadOnlyList<FeedItem> feed, int ticksPerSecond)
        {
            Styles();
            int first = Mathf.Max(0, feed.Count - 8);
            if (feed.Count > first)
                GUI.Box(new Rect(Screen.width - 440f, 72f, 432f, (feed.Count - first) * 22f + 8f), GUIContent.none, _panel);
            float y = 76f;
            for (int i = first; i < feed.Count; i++)
            {
                FeedItem item = feed[i];
                GUI.color = item.Team is Team team ? Palette.Of(team) : Color.white;
                long s = item.Tick / ticksPerSecond;
                GUI.Label(new Rect(Screen.width - 430f, y, 420f, 22f), $"{s / 60:00}:{s % 60:00}  {Describe(item)}", RightAligned(_label!));
                y += 22f;
            }

            GUI.color = Color.white;
        }

        public void DrawScoreboard(IReadOnlyList<HeroLine> heroes, Viewpoint viewpoint)
        {
            Styles();
            float w = 860f;
            float h = 60f + heroes.Count * 24f;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none, _panel);
            string header = "Hero              Lvl   K / D / A    LH / DN   Net worth   GPM   XPM   Hero dmg   Heal   Wards";
            GUI.Label(new Rect(area.x + 14f, area.y + 12f, w, 22f), header, _label);
            float y = area.y + 40f;
            foreach (HeroLine line in heroes)
            {
                bool hidden = viewpoint != Viewpoint.Observer && (line.Team == Team.Dawn) != (viewpoint == Viewpoint.Dawn);
                string economy = hidden ? $"{"?",9}   {"?",3}   {"?",3}" : $"{line.NetWorth,9}   {line.GoldPerMinute,3}   {line.XpPerMinute,3}";
                GUI.color = Palette.Of(line.Team);
                GUI.Label(new Rect(area.x + 14f, y, w, 22f),
                    $"{line.Name,-16}  {line.Level,3}   {line.Kills,2} / {line.Deaths,2} / {line.Assists,2}   {line.LastHits,3} / {line.Denies,-3}  {economy}   {line.HeroDamage,8}   {line.Healing,5}   {line.WardsPlaced,4}",
                    _label);
                y += 24f;
            }

            GUI.color = Color.white;
        }

        public void DrawHelp()
        {
            Styles();
            GUI.Label(new Rect(12f, Screen.height - 26f, Screen.width, 22f),
                "Space pause   . step   1-6 speed   V viewpoint   M 3D/tactical   F follow hero   Tab scoreboard   WASD / right-drag pan   wheel zoom",
                _label);
        }

        private static string Describe(FeedItem item) => item.Kind switch
        {
            FeedKind.HeroKilled => (item.Actor ?? "creeps / tower") + " killed " + item.Subject,
            FeedKind.StructureDestroyed => item.Subject + " destroyed",
            FeedKind.BossKilled => item.Subject + " slain" + (item.Actor != null ? " by " + item.Actor : ""),
            FeedKind.Buyback => item.Subject + " bought back",
            FeedKind.NightFell => "night falls",
            FeedKind.DayBroke => "day breaks",
            FeedKind.MatchFinished => item.Subject + " wins the match",
            _ => item.Kind.ToString(),
        };

        private void Styles()
        {
            if (_label != null)
                return;
            _panelTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _panelTexture.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.82f));
            _panelTexture.Apply();
            _panel = new GUIStyle(GUI.skin.box) { normal = { background = _panelTexture } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white }, font = Font.CreateDynamicFontFromOSFont("Consolas", 14) };
            _big = new GUIStyle(_label) { fontSize = 22, fontStyle = FontStyle.Bold, font = null };
        }

        private static GUIStyle Centered(GUIStyle style) => new GUIStyle(style) { alignment = TextAnchor.MiddleCenter };

        private static GUIStyle RightAligned(GUIStyle style) => new GUIStyle(style) { alignment = TextAnchor.MiddleRight };
    }
}
