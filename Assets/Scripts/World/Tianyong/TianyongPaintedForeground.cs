using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Foreground cutouts of painted props. The whole town is one flat painting
    /// drawn beneath every sprite, so an actor standing beside or behind an
    /// upright prop (feet north of the prop's ground base) is drawn over the
    /// prop instead of behind it. Each cutout re-draws a traced silhouette of
    /// one prop from its unchanged source tile and sorts it by the prop's
    /// ground base, exactly like an actor's feet: actors whose feet are north
    /// of the base sort beneath it, actors south of it sort above.
    /// <para>
    /// The source painting, walk mask and colliders are untouched. Only props
    /// listed in <see cref="Cutouts"/> are fixed; the painting has many more
    /// upright objects that need the same treatment (see the delivery notes).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongPaintedForeground : MonoBehaviour
    {
        /// <summary>One traced prop silhouette on one source tile.</summary>
        public sealed class Cutout
        {
            /// <summary>GameObject name; also the key for <see cref="SetVisible"/>.</summary>
            public readonly string Name;
            /// <summary>All source-tile pieces of this prop toggle together.</summary>
            public readonly string VisibilityGroup;
            /// <summary>Zero-based source tile row (r03 = 2).</summary>
            public readonly int TileRow;
            /// <summary>Zero-based source tile column (c03 = 2).</summary>
            public readonly int TileColumn;
            /// <summary>
            /// Centre of the prop's south ground contact in original 6144 px art.
            /// Sorting uses this point. Using the roof centre instead would
            /// sort the actors standing just north of the base wrongly.
            /// </summary>
            public readonly Vector2 BasePixel;
            /// <summary>
            /// Tile-local scanlines, top to bottom: (source y, left edge, right
            /// edge exclusive). One interval per row; consecutive rows are
            /// joined into trapezoids, so no rectangular pavement patch is drawn.
            /// </summary>
            public readonly Vector3[] Rows;

            public Cutout(string name, int tileRow, int tileColumn, Vector2 basePixel, Vector3[] rows, string visibilityGroup = null)
            {
                Name = name;
                VisibilityGroup = visibilityGroup ?? name;
                TileRow = tileRow;
                TileColumn = tileColumn;
                BasePixel = basePixel;
                Rows = rows;
            }

            internal int TileIndex => TileRow * TianyongPaintedCity.TileColumns + TileColumn;
        }

        public const string WestLampObjectName = "TempleWestLampRoofForeground";
        public const string EastLampObjectName = "TempleEastLampRoofForeground";
        /// <summary>West lamp source tile (r03_c03); kept for existing callers.</summary>
        public const int SourceTileRow = 2;
        public const int SourceTileColumn = 2;
        private const float SurfaceHeight = 0.01f;

        // Native festival painting: the two red knot standards and jade lamp
        // pavilions cross y=2048. Both tile pieces sort from the same south
        // ground contact, so the source seam cannot change actor occlusion.
        public static readonly Vector2 WestLampBasePixel = new(2397f, 2349f);
        public static readonly Vector2 EastLampBasePixel = new(3742f, 2349f);

        // Hand-traced silhouette in r03_c03; no rectangular pavement overlay.
        private static readonly Vector3[] WestLampRows =
        {
            new(0f, 332f, 369f), new(4f, 330f, 372f), new(8f, 323f, 376f),
            new(14f, 321f, 379f), new(20f, 326f, 382f), new(25f, 329f, 382f),
            new(32f, 329f, 382f), new(47f, 326f, 383f), new(56f, 327f, 380f),
            new(62f, 332f, 377f), new(67f, 332f, 375f), new(107f, 332f, 375f),
            new(124f, 332f, 375f), new(127f, 310f, 375f), new(132f, 306f, 375f),
            new(137f, 308f, 375f), new(141f, 302f, 397f), new(147f, 302f, 400f),
            new(152f, 305f, 397f), new(157f, 309f, 402f), new(162f, 289f, 399f),
            new(167f, 287f, 400f), new(172f, 290f, 416f), new(178f, 283f, 417f),
            new(182f, 281f, 420f), new(187f, 285f, 420f), new(192f, 288f, 415f),
            new(198f, 289f, 412f), new(202f, 288f, 407f), new(207f, 292f, 407f),
            new(212f, 302f, 404f), new(216f, 309f, 400f), new(222f, 310f, 399f),
            new(242f, 310f, 399f), new(262f, 310f, 399f), new(269f, 306f, 400f),
            new(272f, 302f, 403f), new(282f, 302f, 404f), new(290f, 306f, 402f),
            new(295f, 312f, 396f), new(298f, 326f, 389f),
        };

        // Hand-traced silhouette in r03_c04; no rectangular pavement overlay.
        private static readonly Vector3[] EastLampRows =
        {
            new(0f, 651.25f, 686.75f), new(1f, 651f, 688f), new(5f, 645f, 693f),
            new(10f, 644f, 697f), new(15f, 639f, 695f), new(20f, 638f, 688f),
            new(27f, 636f, 688f), new(47f, 636f, 688f), new(56f, 637f, 688f),
            new(62f, 643f, 688f), new(64f, 648f, 687f), new(108f, 648f, 687f),
            new(127f, 648f, 702f), new(132f, 643f, 708f), new(137f, 640f, 705f),
            new(142f, 627f, 713f), new(147f, 623f, 714f), new(152f, 625f, 713f),
            new(157f, 628f, 711f), new(162f, 610f, 710f), new(167f, 609f, 719f),
            new(172f, 611f, 727f), new(178f, 610f, 727f), new(182f, 606f, 730f),
            new(187f, 606f, 731f), new(192f, 610f, 731f), new(197f, 614f, 729f),
            new(202f, 617f, 726f), new(207f, 620f, 723f), new(212f, 624f, 724f),
            new(217f, 625f, 726f), new(222f, 624f, 725f), new(227f, 626f, 709f),
            new(247f, 631f, 709f), new(262f, 630f, 712f), new(270f, 626f, 715f),
            new(277f, 626f, 717f), new(286f, 629f, 718f), new(294f, 637f, 714f),
            new(298f, 648f, 707f),
        };

        // Hand-traced silhouette in r02_c03; no rectangular pavement overlay.
        private static readonly Vector3[] WestUpperLampRows =
        {
            new(968f, 350f, 354f), new(971f, 345f, 357f), new(976f, 344f, 359f),
            new(981f, 345f, 358f), new(983f, 347f, 357f), new(988f, 345f, 358f),
            new(990f, 323f, 379f), new(994f, 320f, 383f), new(999f, 319f, 384f),
            new(1003f, 322f, 382f), new(1008f, 329f, 376f), new(1013f, 333f, 370f),
            new(1016f, 345f, 360f), new(1020f, 345f, 361f), new(1024f, 332f, 369f),
        };

        // Hand-traced silhouette in r02_c04; no rectangular pavement overlay.
        private static readonly Vector3[] EastUpperLampRows =
        {
            new(968f, 665f, 669f), new(972f, 660f, 673f), new(978f, 660f, 673f),
            new(984f, 661f, 672f), new(988f, 662f, 672f), new(991f, 638f, 694f),
            new(994f, 635f, 698f), new(998f, 636f, 699f), new(1002f, 641f, 695f),
            new(1008f, 645f, 691f), new(1013f, 652f, 682f), new(1017f, 659f, 674f),
            new(1021f, 652f, 683f), new(1024f, 651.25f, 686.75f),
        };

        public static readonly Cutout WestLamp =
            new(WestLampObjectName, SourceTileRow, SourceTileColumn, WestLampBasePixel, WestLampRows);
        public static readonly Cutout EastLamp =
            new(EastLampObjectName, 2, 3, EastLampBasePixel, EastLampRows);
        public static readonly Cutout WestLampUpper =
            new(WestLampObjectName + "Upper", 1, 2, WestLampBasePixel, WestUpperLampRows, WestLampObjectName);
        public static readonly Cutout EastLampUpper =
            new(EastLampObjectName + "Upper", 1, 3, EastLampBasePixel, EastUpperLampRows, EastLampObjectName);

        /// <summary>Every source-tile cutout built with the painted city.</summary>
        public static readonly IReadOnlyList<Cutout> Cutouts =
            new[] { WestLamp, EastLamp, WestLampUpper, EastLampUpper };

        private Mesh _ownedMesh;
        private MeshRenderer _renderer;
        private string _visibilityGroup;

        /// <summary>Builds every registered cutout from the city's source tiles.</summary>
        public static int AddAll(TianyongMapInstance map, Transform parent, IReadOnlyList<Texture2D> tiles)
        {
            if (map == null || parent == null || tiles == null) return 0;
            var built = 0;
            foreach (var cutout in Cutouts)
            {
                var index = cutout.TileIndex;
                if (index < 0 || index >= tiles.Count || tiles[index] == null) continue;
                if (Add(map, parent, cutout, tiles[index])) built++;
            }
            return built;
        }

        public static void AddWestLamp(TianyongMapInstance map, Transform parent, Texture2D tile)
            => Add(map, parent, WestLamp, tile);

        public static bool Add(TianyongMapInstance map, Transform parent, Cutout cutout, Texture2D tile)
        {
            if (map == null || parent == null || cutout == null || tile == null) return false;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError($"[TianyongPaintedForeground] Sprites/Default is unavailable; {cutout.Name} occlusion cannot render.");
                return false;
            }

            var go = new GameObject(cutout.Name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = TianyongPaintedCity.PaintingToWorld(cutout.BasePixel, SurfaceHeight);
            var foreground = go.AddComponent<TianyongPaintedForeground>();
            foreground._ownedMesh = CreateMesh(cutout);
            foreground._visibilityGroup = cutout.VisibilityGroup;
            go.AddComponent<MeshFilter>().sharedMesh = foreground._ownedMesh;
            var material = new Material(shader)
            {
                name = cutout.Name + "Material",
                mainTexture = tile,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent,
            };
            map._materials.Add(material);
            foreground._renderer = go.AddComponent<MeshRenderer>();
            foreground._renderer.sharedMaterial = material;
            foreground._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreground._renderer.receiveShadows = false;
            foreground._renderer.sortingOrder = Mathf.RoundToInt(-go.transform.position.z * 10f);
            return true;
        }

        public static Mesh CreateWestLampRoofMesh() => CreateMesh(WestLamp);

        /// <summary>
        /// Reproducible mesh for one cutout. Vertices are relative to the
        /// ground-contact anchor; UVs address the unchanged source tile.
        /// </summary>
        public static Mesh CreateMesh(Cutout cutout)
        {
            var rows = cutout.Rows;
            var vertices = new Vector3[rows.Length * 2];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[(rows.Length - 1) * 6];
            var anchor = TianyongPaintedCity.PaintingToWorld(cutout.BasePixel, SurfaceHeight);
            for (var row = 0; row < rows.Length; row++)
            {
                var source = rows[row];
                for (var side = 0; side < 2; side++)
                {
                    var index = row * 2 + side;
                    var x = side == 0 ? source.y : source.z;
                    var pixel = new Vector2(cutout.TileColumn * TianyongPaintedCity.TilePixels + x,
                        cutout.TileRow * TianyongPaintedCity.TilePixels + source.x);
                    vertices[index] = TianyongPaintedCity.PaintingToWorld(pixel, SurfaceHeight) - anchor;
                    uv[index] = new Vector2(x / TianyongPaintedCity.TilePixels,
                        1f - source.x / TianyongPaintedCity.TilePixels);
                    colors[index] = Color.white;
                }
                if (row == rows.Length - 1) continue;
                var t = row * 6;
                var a = row * 2;
                triangles[t] = a;
                triangles[t + 1] = a + 1;
                triangles[t + 2] = a + 2;
                triangles[t + 3] = a + 1;
                triangles[t + 4] = a + 3;
                triangles[t + 5] = a + 2;
            }
            var mesh = new Mesh
            {
                name = cutout.Name + "Mesh",
                vertices = vertices,
                uv = uv,
                colors = colors,
                triangles = triangles,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Acceptance A/B switch; the original ground is always retained.</summary>
        public static bool SetWestLampVisible(Transform root, bool visible)
            => SetVisible(root, WestLampObjectName, visible);

        /// <summary>Shows or hides a prop group (or one piece by its name). False when absent.</summary>
        public static bool SetVisible(Transform root, string name, bool visible)
        {
            if (root == null) return false;
            var found = false;
            foreach (var foreground in root.GetComponentsInChildren<TianyongPaintedForeground>(true))
            {
                if ((foreground.name != name && foreground._visibilityGroup != name) || foreground._renderer == null) continue;
                foreground._renderer.enabled = visible;
                found = true;
            }
            return found;
        }

        private void LateUpdate()
        {
            if (_renderer != null)
                _renderer.sortingOrder = QdaoBoySpriteAnimator.WorldSortingOrder(transform.position, Camera.main);
        }

        private void OnDestroy()
        {
            if (_ownedMesh == null) return;
            if (Application.isPlaying) Destroy(_ownedMesh);
            else DestroyImmediate(_ownedMesh);
            _ownedMesh = null;
        }
    }
}
