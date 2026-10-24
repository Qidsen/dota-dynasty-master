#nullable enable
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>
    /// Tactical 2D map (spec §13.2): the static map rasterised once, then each frame the units the
    /// viewpoint can see, last-known enemy hero positions (hollow rings) and projectiles. A team
    /// viewpoint shows only that team's knowledge — the same frame the 3D view draws.
    /// </summary>
    public sealed class TacticalMap
    {
        private const int Resolution = 512;

        private readonly MapLayout _map;
        private readonly Texture2D _background;
        private readonly Texture2D _disc;
        private readonly Texture2D _ring;
        private readonly Texture2D _pixel;

        public TacticalMap(MapLayout map)
        {
            _map = map;
            _background = Rasterise(map);
            _disc = Circle(32, filled: true);
            _ring = Circle(32, filled: false);
            _pixel = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        /// <summary>Draws the map into the given screen rectangle (call from OnGUI).</summary>
        public void Draw(Rect area, MatchFrame frame, long? highlighted)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(area, _background);

            foreach (UnitFrame unit in frame.Units)
            {
                Vector2 at = ToScreen(area, unit.X, unit.Y);
                Color color = unit.Kind == UnitKind.Rune ? Palette.Rune : Palette.Of(unit.Team);
                switch (unit.Kind)
                {
                    case UnitKind.Hero:
                        Dot(at, area.width * 0.022f, highlighted == unit.Id ? Color.white : Color.black);
                        Dot(at, area.width * 0.017f, color);
                        break;
                    case UnitKind.Structure:
                        Square(at, area.width * (unit.TypeId.Contains("ancient") ? 0.03f : 0.016f), color);
                        break;
                    case UnitKind.Boss:
                        Dot(at, area.width * 0.024f, new Color(0.55f, 0.35f, 0.75f));
                        break;
                    default:
                        bool ward = unit.TypeId.Contains("ward");
                        Dot(at, area.width * (ward ? 0.012f : 0.007f), ward ? Color.yellow : color * 0.9f);
                        break;
                }
            }

            foreach (GhostFrame ghost in frame.Ghosts)
            {
                GUI.color = Palette.Of(ghost.Team);
                Vector2 at = ToScreen(area, ghost.X, ghost.Y);
                float size = area.width * 0.022f;
                GUI.DrawTexture(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), _ring);
            }

            foreach (ProjectileFrame projectile in frame.Projectiles)
                Dot(ToScreen(area, projectile.X, projectile.Y), area.width * 0.005f, Palette.Projectile);

            GUI.color = Color.white;
        }

        private Vector2 ToScreen(Rect area, float x, float y) =>
            new Vector2(area.x + x / _map.Width * area.width, area.y + (1f - y / _map.Height) * area.height);

        private void Dot(Vector2 at, float size, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), _disc);
        }

        private void Square(Vector2 at, float size, Color color)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(at.x - size / 2f - 1f, at.y - size / 2f - 1f, size + 2f, size + 2f), _pixel);
            GUI.color = color;
            GUI.DrawTexture(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), _pixel);
        }

        private static Texture2D Rasterise(MapLayout map)
        {
            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[Resolution * Resolution];
            float cell = map.Width / (float)Resolution;
            for (int py = 0; py < Resolution; py++)
            {
                for (int px = 0; px < Resolution; px++)
                {
                    float x = (px + 0.5f) * cell;
                    float y = (py + 0.5f) * cell;
                    Color c = Palette.Ground * 0.55f;
                    foreach (MapCircle plateau in map.Plateaus)
                    {
                        if (Sq(x - plateau.X) + Sq(y - plateau.Y) <= Sq(plateau.Radius))
                            c = Palette.HighGround * 0.6f;
                    }

                    if (NearLane(map, x, y, 110f))
                        c = Palette.Lane * 0.7f;
                    foreach (MapObstacle o in map.Obstacles)
                    {
                        bool inside = o.IsCircle ? Sq(x - o.CenterX) + Sq(y - o.CenterY) <= Sq(o.Radius) : x >= o.MinX && x <= o.MaxX && y >= o.MinY && y <= o.MaxY;
                        if (inside)
                            c = Palette.Tree * 0.8f;
                    }

                    c.a = 1f;
                    pixels[py * Resolution + px] = c;   // texture rows go bottom-up, like map Y
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static bool NearLane(MapLayout map, float x, float y, float halfWidth)
        {
            foreach (MapLane lane in map.Lanes)
            {
                for (int i = 0; i + 1 < lane.Points.Count; i++)
                {
                    var a = new Vector2(lane.Points[i].X, lane.Points[i].Y);
                    var b = new Vector2(lane.Points[i + 1].X, lane.Points[i + 1].Y);
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(new Vector2(x, y) - a, ab) / Mathf.Max(ab.sqrMagnitude, 1f));
                    if ((a + ab * t - new Vector2(x, y)).sqrMagnitude <= halfWidth * halfWidth)
                        return true;
                }
            }

            return false;
        }

        private static Texture2D Circle(int size, bool filled)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt(Sq(x + 0.5f - r) + Sq(y + 0.5f - r));
                    float alpha = filled ? Mathf.Clamp01(r - d) : Mathf.Clamp01(1.5f - Mathf.Abs(d - (r - 2.5f)));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private static float Sq(float v) => v * v;
    }
}
