#nullable enable
using System.Collections.Generic;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>
    /// The 3D board (spec §13.1, placeholder art): terrain from the <see cref="MapLayout"/>, one
    /// primitive per visible unit from the current <see cref="MatchFrame"/>, interpolated poses,
    /// HP bars, projectiles and the day/night light. It only mirrors frames; it never touches the simulation.
    /// </summary>
    public sealed class WorldView
    {
        private const float PlateauHeight = 1.2f;

        private readonly Transform _root;
        private readonly Material _material;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly Dictionary<long, UnitVisual> _units = new Dictionary<long, UnitVisual>();
        private readonly Dictionary<long, Transform> _projectiles = new Dictionary<long, Transform>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly List<long> _gone = new List<long>();
        private readonly Light _sun;
        private readonly MapGround? _ground;

        public WorldView(MapLayout map, Material material)
        {
            _material = material;
            _ground = map.Ground;
            _root = new GameObject("World").transform;
            _sun = CreateSun();
            BuildTerrain(map);
        }

        /// <summary>World position of a unit by ID, if it is shown (for the camera to follow).</summary>
        public bool TryGetPosition(long id, out Vector3 position)
        {
            if (_units.TryGetValue(id, out UnitVisual? visual))
            {
                position = visual.Body.position;
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>How many unit visuals are on the board (one per unit in the last frame shown).</summary>
        public int ShownUnits => _units.Count;

        public void Show(MatchFrame frame, IReadOnlyList<UnitPose> poses, Camera? viewer = null)
        {
            var poseById = new Dictionary<long, UnitPose>(poses.Count);
            foreach (UnitPose pose in poses)
                poseById[pose.Id] = pose;

            _seen.Clear();
            foreach (UnitFrame unit in frame.Units)
            {
                _seen.Add(unit.Id);
                if (!_units.TryGetValue(unit.Id, out UnitVisual? visual))
                {
                    visual = CreateUnit(unit);
                    _units.Add(unit.Id, visual);
                }

                UnitPose pose = poseById.TryGetValue(unit.Id, out UnitPose p) ? p : new UnitPose(unit.Id, unit.X, unit.Y);
                float ground = _ground != null ? TerrainView.Height(unit.Elevation) : unit.Elevation > 0 ? PlateauHeight : 0f;
                visual.Body.position = Palette.World(pose.X, pose.Y, ground + visual.HalfHeight);
                visual.SetHealth(unit.MaxHp > 0 ? Mathf.Clamp01(unit.Hp / (float)unit.MaxHp) : 1f, viewer);
                visual.SetInvisible(unit.Invisible);
            }

            Retire(_units, _seen);

            _seen.Clear();
            foreach (ProjectileFrame projectile in frame.Projectiles)
            {
                _seen.Add(projectile.Id);
                if (!_projectiles.TryGetValue(projectile.Id, out Transform? shot))
                {
                    shot = Primitive(PrimitiveType.Sphere, "Projectile", Palette.Projectile, new Vector3(0.35f, 0.35f, 0.35f));
                    _projectiles.Add(projectile.Id, shot);
                }

                shot.position = Palette.World(projectile.X, projectile.Y, GroundAt(projectile.X, projectile.Y) + 1.2f);
            }

            RetireTransforms(_projectiles, _seen);

            _sun.intensity = Mathf.MoveTowards(_sun.intensity, frame.IsNight ? 0.35f : 1.15f, Time.unscaledDeltaTime * 0.5f);
            _sun.color = Color.Lerp(new Color(0.55f, 0.62f, 0.95f), new Color(1f, 0.96f, 0.86f), Mathf.InverseLerp(0.35f, 1.15f, _sun.intensity));
        }

        private void Retire(Dictionary<long, UnitVisual> visuals, HashSet<long> keep)
        {
            _gone.Clear();
            foreach (KeyValuePair<long, UnitVisual> pair in visuals)
            {
                if (!keep.Contains(pair.Key))
                    _gone.Add(pair.Key);
            }

            foreach (long id in _gone)
            {
                Object.Destroy(visuals[id].Body.gameObject);
                visuals.Remove(id);
            }
        }

        private void RetireTransforms(Dictionary<long, Transform> transforms, HashSet<long> keep)
        {
            _gone.Clear();
            foreach (KeyValuePair<long, Transform> pair in transforms)
            {
                if (!keep.Contains(pair.Key))
                    _gone.Add(pair.Key);
            }

            foreach (long id in _gone)
            {
                Object.Destroy(transforms[id].gameObject);
                transforms.Remove(id);
            }
        }

        private UnitVisual CreateUnit(UnitFrame unit)
        {
            Color color = unit.Kind == UnitKind.Rune ? Palette.Rune : Palette.Of(unit.Team);
            Transform body;
            float halfHeight;
            switch (unit.Kind)
            {
                case UnitKind.Hero:
                    body = Primitive(PrimitiveType.Capsule, unit.Name ?? "Hero", color, new Vector3(1.1f, 1.1f, 1.1f));
                    halfHeight = 1.1f;
                    break;
                case UnitKind.Structure:
                    bool ancient = unit.TypeId.Contains("ancient");
                    bool barracks = unit.TypeId.Contains("barracks");
                    body = ancient
                        ? Primitive(PrimitiveType.Cube, unit.TypeId, color, new Vector3(4f, 4f, 4f))
                        : barracks
                            ? Primitive(PrimitiveType.Cube, unit.TypeId, color * 0.85f, new Vector3(2.4f, 1.6f, 2.4f))
                            : Primitive(PrimitiveType.Cylinder, unit.TypeId, color, new Vector3(1.6f, 2.2f, 1.6f));
                    halfHeight = ancient ? 2f : barracks ? 0.8f : 2.2f;
                    break;
                case UnitKind.Boss:
                    body = Primitive(PrimitiveType.Sphere, "Boss", Palette.Boss, new Vector3(3f, 3f, 3f));
                    halfHeight = 1.5f;
                    break;
                case UnitKind.Miniboss:
                    body = Primitive(PrimitiveType.Cube, "Tormentor", Palette.Tormentor, new Vector3(2.4f, 2.4f, 2.4f));
                    halfHeight = 1.2f;
                    break;
                case UnitKind.Outpost:
                    body = Primitive(PrimitiveType.Cylinder, "Outpost", color, new Vector3(1.2f, 2.6f, 1.2f));
                    halfHeight = 2.6f;
                    break;
                case UnitKind.GroundItem:
                    body = Primitive(PrimitiveType.Sphere, "Aegis", Palette.Aegis, new Vector3(0.9f, 0.9f, 0.9f));
                    halfHeight = 0.45f;
                    break;
                case UnitKind.Rune:
                    body = Primitive(PrimitiveType.Sphere, "Rune", color, new Vector3(0.8f, 0.8f, 0.8f));
                    halfHeight = 0.6f;
                    break;
                default:
                    bool ward = unit.TypeId.Contains("ward");
                    float size = ward ? 0.35f : unit.Team == Team.Neutral ? 0.8f : unit.TypeId.Contains("super") || unit.TypeId.Contains("siege") ? 0.85f : 0.6f;
                    body = Primitive(ward ? PrimitiveType.Sphere : PrimitiveType.Cube, unit.TypeId, color, new Vector3(size, size, size));
                    halfHeight = size / 2f;
                    break;
            }

            body.SetParent(_root, true);
            HealthBar? bar = unit.Kind == UnitKind.Hero || unit.Kind == UnitKind.Structure || unit.Kind == UnitKind.Boss || unit.Kind == UnitKind.Miniboss
                ? new HealthBar(body, halfHeight * 2f + 0.5f, _material, _block)
                : null;
            return new UnitVisual(body, halfHeight, bar, _material, _block, color);
        }

        private Transform Primitive(PrimitiveType type, string name, Color color, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            _block.Clear();
            _block.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(_block);
            go.transform.SetParent(_root, true);
            return go.transform;
        }

        /// <summary>Height of the drawn ground at a map point (flat without a cell-by-cell map).</summary>
        private float GroundAt(float x, float y) => _ground != null ? TerrainView.Height(_ground.LevelAt(x, y)) : 0f;

        private void BuildGround(MapLayout map, MapGround ground)
        {
            var groundObject = new GameObject("Ground");
            groundObject.transform.SetParent(_root, false);
            groundObject.AddComponent<MeshFilter>().sharedMesh = TerrainView.GroundMesh(ground);
            var material = new Material(_material) { hideFlags = HideFlags.HideAndDontSave };
            Texture2D texture = TerrainView.Texture(map, ground, 1f);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            material.color = Color.white;
            if (material.HasProperty(BaseColor))
                material.SetColor(BaseColor, Color.white);
            groundObject.AddComponent<MeshRenderer>().sharedMaterial = material;

            foreach (Mesh trees in TerrainView.TreeMeshes(ground))
            {
                var treesObject = new GameObject("Trees");
                treesObject.transform.SetParent(_root, false);
                treesObject.AddComponent<MeshFilter>().sharedMesh = trees;
                var renderer = treesObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                _block.Clear();
                _block.SetColor(BaseColor, Palette.Tree);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void BuildTerrain(MapLayout map)
        {
            if (map.Ground != null)
            {
                BuildGround(map, map.Ground);   // the Dota map: ground, river, cliffs, trees and lanes in the mesh and its texture
                BuildMarkersAndFountains(map);
                return;
            }

            float w = map.Width / Palette.MapUnitsPerMetre;
            float h = map.Height / Palette.MapUnitsPerMetre;
            Transform ground = Primitive(PrimitiveType.Cube, "Ground", Palette.Ground, new Vector3(w, 0.2f, h));
            ground.position = new Vector3(w / 2f, -0.1f, h / 2f);

            foreach (MapCircle plateau in map.Plateaus)
            {
                float d = plateau.Radius * 2f / Palette.MapUnitsPerMetre;
                Transform disc = Primitive(PrimitiveType.Cylinder, "HighGround", Palette.HighGround, new Vector3(d, PlateauHeight / 2f, d));
                disc.position = Palette.World(plateau.X, plateau.Y, PlateauHeight / 2f);
            }

            foreach (MapLane lane in map.Lanes)
            {
                for (int i = 0; i + 1 < lane.Points.Count; i++)
                {
                    Vector3 a = Palette.World(lane.Points[i].X, lane.Points[i].Y, 0.02f);
                    Vector3 b = Palette.World(lane.Points[i + 1].X, lane.Points[i + 1].Y, 0.02f);
                    Transform strip = Primitive(PrimitiveType.Cube, "Lane " + lane.Id, Palette.Lane, new Vector3(2.2f, 0.04f, Vector3.Distance(a, b) + 2.2f));
                    strip.position = (a + b) / 2f;
                    strip.rotation = Quaternion.LookRotation(b - a, Vector3.up);
                }
            }

            foreach (MapObstacle tree in map.Obstacles)
            {
                if (tree.IsCircle)
                {
                    float d = tree.Radius * 2f / Palette.MapUnitsPerMetre;
                    Transform t = Primitive(PrimitiveType.Cylinder, "Trees", Palette.Tree, new Vector3(d, 1.5f, d));
                    t.position = Palette.World(tree.CenterX, tree.CenterY, 1.5f);
                }
                else
                {
                    Transform t = Primitive(PrimitiveType.Cube, "Trees", Palette.Tree,
                        new Vector3((tree.MaxX - tree.MinX) / Palette.MapUnitsPerMetre, 3f, (tree.MaxY - tree.MinY) / Palette.MapUnitsPerMetre));
                    t.position = Palette.World(tree.CenterX, tree.CenterY, 1.5f);
                }
            }

            BuildMarkersAndFountains(map);
        }

        private void BuildMarkersAndFountains(MapLayout map)
        {
            foreach (MapMarker marker in map.Markers)
            {
                // A flat coloured disc for each place (the units on it — Roshan, the Tormentor — are drawn by themselves).
                float d = marker.Kind == MapMarkerKind.BossPit || marker.Kind == MapMarkerKind.Tormentor ? 7f : 3.5f;
                Transform disc = Primitive(PrimitiveType.Cylinder, marker.Kind.ToString(), Palette.Of(marker.Kind), new Vector3(d, 0.06f, d));
                disc.position = Palette.World(marker.X, marker.Y, GroundAt(marker.X, marker.Y) + 0.08f);
                if (marker.Kind == MapMarkerKind.Watcher || marker.Kind == MapMarkerKind.Shrine || marker.Kind == MapMarkerKind.TwinGate)
                {
                    Transform pillar = Primitive(PrimitiveType.Cylinder, marker.Kind + " pillar", Palette.Of(marker.Kind), new Vector3(0.6f, 1.6f, 0.6f));
                    pillar.position = Palette.World(marker.X, marker.Y, GroundAt(marker.X, marker.Y) + 1.6f);
                }
            }

            foreach (MapFountain fountain in map.Fountains)
            {
                float d = fountain.Radius * 2f / Palette.MapUnitsPerMetre;
                Transform f = Primitive(PrimitiveType.Cylinder, "Fountain", Palette.Of(fountain.Team) * 0.6f, new Vector3(d, 0.1f, d));
                f.position = Palette.World(fountain.X, fountain.Y, GroundAt(fountain.X, fountain.Y) + 0.05f);
            }
        }

        private Light CreateSun()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.42f);
            return light;
        }

        internal static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private sealed class UnitVisual
        {
            private readonly HealthBar? _bar;
            private readonly Renderer _renderer;
            private readonly Material _material;
            private readonly MaterialPropertyBlock _block;
            private readonly Color _color;
            private bool _invisible;

            public UnitVisual(Transform body, float halfHeight, HealthBar? bar, Material material, MaterialPropertyBlock block, Color color)
            {
                Body = body;
                HalfHeight = halfHeight;
                _bar = bar;
                _renderer = body.GetComponent<Renderer>();
                _material = material;
                _block = block;
                _color = color;
            }

            public Transform Body { get; }
            public float HalfHeight { get; }

            public void SetHealth(float fraction, Camera? viewer) => _bar?.Set(fraction, viewer);

            /// <summary>Invisible units the viewpoint still knows of (own team, observer) are drawn darker.</summary>
            public void SetInvisible(bool invisible)
            {
                if (invisible == _invisible)
                    return;
                _invisible = invisible;
                _block.Clear();
                _block.SetColor(BaseColor, invisible ? _color * 0.45f : _color);
                _renderer.SetPropertyBlock(_block);
            }
        }

        private sealed class HealthBar
        {
            private const float Width = 1.4f;

            private readonly Transform _pivot;
            private readonly Transform _fill;

            public HealthBar(Transform owner, float height, Material material, MaterialPropertyBlock block)
            {
                // A pivot that cancels the owner's scale, so bars are the same size above every unit.
                _pivot = new GameObject("HpBar").transform;
                _pivot.SetParent(owner, false);
                Vector3 s = owner.localScale;
                _pivot.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
                _pivot.localPosition = new Vector3(0f, height / s.y, 0f);
                Transform back = Bar(_pivot, "HpBack", new Color(0.1f, 0.1f, 0.1f), material, block);
                back.localScale = new Vector3(Width, 0.12f, 0.06f);
                _fill = Bar(_pivot, "Hp", new Color(0.35f, 0.9f, 0.35f), material, block);
                _fill.localScale = new Vector3(Width, 0.14f, 0.08f);
                _fill.localPosition = new Vector3(0f, 0f, -0.02f);
            }

            public void Set(float fraction, Camera? viewer)
            {
                if (viewer != null)
                    _pivot.rotation = viewer.transform.rotation;   // face the camera
                Vector3 scale = _fill.localScale;
                scale.x = Mathf.Max(0.001f, Width * fraction);
                _fill.localScale = scale;
                _fill.localPosition = new Vector3((scale.x - Width) / 2f, 0f, -0.02f);
            }

            private static Transform Bar(Transform owner, string name, Color color, Material material, MaterialPropertyBlock block)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Object.Destroy(go.GetComponent<Collider>());
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                block.Clear();
                block.SetColor(BaseColor, color);
                renderer.SetPropertyBlock(block);
                go.transform.SetParent(owner, false);
                return go.transform;
            }

        }
    }
}
