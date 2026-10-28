#nullable enable
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>Shared colours and the map-to-world scale of the viewer.</summary>
    public static class Palette
    {
        /// <summary>Map units per Unity metre: the 10000-unit map becomes a 100 m board.</summary>
        public const float MapUnitsPerMetre = 100f;

        public static readonly Color Dawn = new Color(0.25f, 0.78f, 0.45f);
        public static readonly Color Dusk = new Color(0.86f, 0.30f, 0.28f);
        public static readonly Color Neutral = new Color(0.75f, 0.70f, 0.55f);
        public static readonly Color Ground = new Color(0.33f, 0.42f, 0.26f);
        public static readonly Color HighGround = new Color(0.42f, 0.48f, 0.30f);
        public static readonly Color Lane = new Color(0.55f, 0.47f, 0.33f);
        public static readonly Color Tree = new Color(0.12f, 0.30f, 0.14f);
        public static readonly Color Rune = new Color(0.40f, 0.75f, 1.00f);
        public static readonly Color Projectile = new Color(1.00f, 0.90f, 0.50f);

        public static readonly Color Boss = new Color(0.55f, 0.35f, 0.75f);
        public static readonly Color Tormentor = new Color(0.95f, 0.55f, 0.15f);
        public static readonly Color Aegis = new Color(1.00f, 0.85f, 0.25f);

        /// <summary>Colour of a map object (pits, shrines, lotus pools, watchers, Twin Gates).</summary>
        public static Color Of(MapMarkerKind kind) => kind switch
        {
            MapMarkerKind.BossPit => Boss,
            MapMarkerKind.Tormentor => Tormentor,
            MapMarkerKind.Shrine => new Color(0.45f, 0.85f, 0.95f),
            MapMarkerKind.LotusPool => new Color(0.95f, 0.55f, 0.80f),
            MapMarkerKind.Watcher => new Color(0.92f, 0.92f, 0.92f),
            _ => new Color(0.30f, 0.90f, 0.75f),
        };

        public static Color Of(Team team) => team == Team.Dawn ? Dawn : team == Team.Dusk ? Dusk : Neutral;

        public static Vector3 World(float mapX, float mapY, float height = 0f) =>
            new Vector3(mapX / MapUnitsPerMetre, height, mapY / MapUnitsPerMetre);
    }
}
