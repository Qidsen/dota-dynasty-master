#nullable enable
using System.Collections.Generic;
using Dynasty.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dynasty.Unity
{
    /// <summary>
    /// A map given cell by cell (the Dota pack's terrain) for both views: a texture with one pixel per cell (river, ground by level,
    /// cliffs, trees, lanes), a ground mesh raised by level for the 3D board and the trees merged into a few meshes.
    /// </summary>
    public static class TerrainView
    {
        /// <summary>Metres per ground level in the 3D view.</summary>
        public const float LevelHeight = 1.0f;

        /// <summary>Levels above this (the mountains around the map) are drawn no higher, so they do not wall off the camera.</summary>
        private const int HighestDrawnLevel = 5;

        private static readonly Color River = new Color(0.20f, 0.42f, 0.50f);
        private static readonly Color Cliff = new Color(0.30f, 0.27f, 0.22f);

        /// <summary>Height of the ground at a level.</summary>
        public static float Height(int level) => Mathf.Min(level, HighestDrawnLevel) * LevelHeight;

        /// <summary>One pixel per cell; <paramref name="dim"/> darkens it (the tactical map is darker than the board).</summary>
        public static Texture2D Texture(MapLayout map, MapGround ground, float dim)
        {
            var pixels = new Color[ground.Columns * ground.Rows];
            for (int i = 0; i < pixels.Length; i++)
            {
                int level = ground.Levels[i];
                pixels[i] = ground.Walkable[i]
                    ? level <= 0 ? River : Color.Lerp(Palette.Ground, Palette.HighGround * 1.25f, (level - 1) / 2f)
                    : Cliff * (0.8f + 0.04f * Mathf.Min(level, HighestDrawnLevel));
            }

            PaintLanes(map, ground, pixels);
            int half = Mathf.Max(1, ground.TreeSize / ground.CellSize / 2);
            foreach (MapPoint tree in ground.Trees)
            {
                int column = (int)(tree.X / ground.CellSize), row = (int)(tree.Y / ground.CellSize);
                for (int r = row - half; r < row + half; r++)
                    for (int c = column - half; c < column + half; c++)
                        if (r >= 0 && r < ground.Rows && c >= 0 && c < ground.Columns)
                            pixels[r * ground.Columns + c] = Palette.Tree;
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i] * dim;
                c.a = 1f;
                pixels[i] = c;
            }

            var texture = new Texture2D(ground.Columns, ground.Rows, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static void PaintLanes(MapLayout map, MapGround ground, Color[] pixels)
        {
            const float halfWidth = 110f;
            foreach (MapLane lane in map.Lanes)
            {
                for (int s = 0; s + 1 < lane.Points.Count; s++)
                {
                    MapPoint a = lane.Points[s], b = lane.Points[s + 1];
                    int minC = Mathf.Max(0, (int)((Mathf.Min(a.X, b.X) - halfWidth) / ground.CellSize));
                    int maxC = Mathf.Min(ground.Columns - 1, (int)((Mathf.Max(a.X, b.X) + halfWidth) / ground.CellSize));
                    int minR = Mathf.Max(0, (int)((Mathf.Min(a.Y, b.Y) - halfWidth) / ground.CellSize));
                    int maxR = Mathf.Min(ground.Rows - 1, (int)((Mathf.Max(a.Y, b.Y) + halfWidth) / ground.CellSize));
                    for (int r = minR; r <= maxR; r++)
                    {
                        for (int c = minC; c <= maxC; c++)
                        {
                            var p = new Vector2((c + 0.5f) * ground.CellSize, (r + 0.5f) * ground.CellSize);
                            if (DistanceToSegment(p, new Vector2(a.X, a.Y), new Vector2(b.X, b.Y)) <= halfWidth && ground.Walkable[r * ground.Columns + c])
                                pixels[r * ground.Columns + c] = Palette.Lane;
                        }
                    }
                }
            }
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>The ground as one mesh: a vertex at each cell corner, raised to the mean level of the cells around it.</summary>
        public static Mesh GroundMesh(MapGround ground)
        {
            int columns = ground.Columns + 1, rows = ground.Rows + 1;
            var vertices = new Vector3[columns * rows];
            var uv = new Vector2[vertices.Length];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int dr = -1; dr <= 0; dr++)
                    {
                        for (int dc = -1; dc <= 0; dc++)
                        {
                            int cr = r + dr, cc = c + dc;
                            if (cr < 0 || cc < 0 || cr >= ground.Rows || cc >= ground.Columns)
                                continue;
                            sum += Height(ground.Levels[cr * ground.Columns + cc]);
                            count++;
                        }
                    }

                    int i = r * columns + c;
                    vertices[i] = Palette.World(c * ground.CellSize, r * ground.CellSize, count > 0 ? sum / count : 0f);
                    uv[i] = new Vector2(c / (float)ground.Columns, r / (float)ground.Rows);
                }
            }

            var triangles = new int[ground.Columns * ground.Rows * 6];
            int t = 0;
            for (int r = 0; r < ground.Rows; r++)
            {
                for (int c = 0; c < ground.Columns; c++)
                {
                    int i = r * columns + c;
                    triangles[t++] = i;
                    triangles[t++] = i + columns;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + columns;
                    triangles[t++] = i + columns + 1;
                }
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32, vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The trees as cylinders standing on the ground, merged into meshes of a thousand trees each.</summary>
        public static List<Mesh> TreeMeshes(MapGround ground)
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Mesh cylinder = probe.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(probe);
            float width = ground.TreeSize / Palette.MapUnitsPerMetre * 0.85f;
            const float tall = 3f;
            var meshes = new List<Mesh>();
            var batch = new List<CombineInstance>();
            foreach (MapPoint tree in ground.Trees)
            {
                float baseHeight = Height(ground.LevelAt(tree.X, tree.Y));
                batch.Add(new CombineInstance
                {
                    mesh = cylinder,
                    transform = Matrix4x4.TRS(Palette.World(tree.X, tree.Y, baseHeight + tall / 2f), Quaternion.identity, new Vector3(width, tall / 2f, width)),
                });
                if (batch.Count == 1000)
                    meshes.Add(Combine(batch));
            }

            if (batch.Count > 0)
                meshes.Add(Combine(batch));
            return meshes;
        }

        private static Mesh Combine(List<CombineInstance> batch)
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(batch.ToArray(), mergeSubMeshes: true, useMatrices: true);
            batch.Clear();
            return mesh;
        }
    }
}
