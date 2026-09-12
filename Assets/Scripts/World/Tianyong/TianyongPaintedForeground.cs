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

            public Cutout(string name, int tileRow, int tileColumn, Vector2 basePixel, Vector3[] rows)
            {
                Name = name;
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

        public static readonly Vector2 WestLampBasePixel = new(2823f, 2503f);
        /// <summary>
        /// The temple steps are flanked by two lamps mirrored about the spawn
        /// axis. Same ground-contact row as the west lamp; x is the centre of
        /// the east lamp's dome and finial column on r03_c04.
        /// </summary>
        public static readonly Vector2 EastLampBasePixel = new(3299f, 2503f);

        // West lamp, traced by hand from r03_c03: finial and eave.
        private static readonly Vector3[] WestLampRows =
        {
            new(277f, 768f, 769f), new(282f, 768f, 770f),
            new(285f, 767f, 771f), new(287f, 766f, 771f),
            new(289f, 766f, 773f), new(292f, 766f, 775f),
            new(295f, 762f, 774f), new(298f, 761f, 770f),
            new(300f, 765f, 771f), new(303f, 764f, 773f),
            new(306f, 761f, 774f), new(309f, 760f, 776f),
            new(312f, 759f, 779f), new(315f, 760f, 779f),
            new(318f, 763f, 776f), new(320f, 759f, 775f),
            new(323f, 758f, 774f), new(326f, 756f, 775f),
            new(329f, 754f, 778f), new(333f, 750f, 782f),
            new(337f, 743f, 786f), new(339f, 737f, 790f),
            new(341f, 732f, 800f), new(343f, 730f, 803f),
            new(348f, 730f, 804f), new(353f, 732f, 803f),
            new(358f, 734f, 801f), new(362f, 737f, 798f),
            new(366f, 742f, 793f), new(370f, 749f, 785f),
            new(373f, 758f, 777f), new(375f, 766f, 768f),
        };

        // East lamp, r03_c04: the tall bead finial (y 248-330) and the dome
        // with its rim (y 330-375). Traced semi-automatically (colour flood
        // from the rim, bounded per segment so the planter foliage and gold
        // flowers directly behind the finial stay out), then checked by eye.
        private static readonly Vector3[] EastLampRows =
        {
            new(248f, 219f, 232f), new(252f, 219f, 232f), new(253f, 219f, 229f),
            new(254f, 218f, 230f), new(255f, 218f, 232f), new(259f, 220f, 230f),
            new(260f, 222f, 232f), new(261f, 223f, 232f), new(262f, 227f, 238f),
            new(263f, 223f, 237f), new(264f, 222f, 237f), new(265f, 220f, 236f),
            new(267f, 220f, 235f), new(268f, 219f, 233f), new(269f, 219f, 234f),
            new(270f, 217f, 236f), new(271f, 217f, 238f), new(277f, 217f, 236f),
            new(278f, 217f, 236f), new(279f, 217f, 238f), new(290f, 217f, 236f),
            new(294f, 217f, 238f), new(304f, 218f, 238f), new(305f, 218f, 241f),
            new(309f, 218f, 240f), new(310f, 218f, 238f), new(311f, 217f, 236f),
            new(315f, 217f, 238f), new(317f, 217f, 239f), new(318f, 218f, 241f),
            new(319f, 220f, 238f), new(321f, 220f, 238f), new(322f, 217f, 238f),
            new(326f, 217f, 236f), new(327f, 217f, 236f), new(328f, 217f, 238f),
            new(330f, 216f, 239f), new(331f, 216f, 245f), new(332f, 215f, 246f),
            new(333f, 210f, 246f), new(334f, 210f, 246f), new(335f, 208f, 246f),
            new(338f, 208f, 245f), new(339f, 207f, 248f), new(340f, 207f, 247f),
            new(341f, 203f, 248f), new(343f, 201f, 248f), new(344f, 201f, 249f),
            new(345f, 202f, 247f), new(346f, 202f, 247f), new(347f, 204f, 251f),
            new(348f, 199f, 252f), new(349f, 199f, 254f), new(352f, 200f, 256f),
            new(353f, 200f, 256f), new(354f, 200f, 254f), new(357f, 201f, 255f),
            new(358f, 201f, 253f), new(362f, 201f, 254f), new(363f, 198f, 254f),
            new(365f, 198f, 252f), new(366f, 198f, 254f), new(371f, 198f, 254f),
            new(372f, 198f, 251f), new(373f, 198f, 250f), new(374f, 198f, 252f),
            new(375f, 198f, 252f),
        };

        public static readonly Cutout WestLamp =
            new(WestLampObjectName, SourceTileRow, SourceTileColumn, WestLampBasePixel, WestLampRows);
        public static readonly Cutout EastLamp =
            new(EastLampObjectName, 2, 3, EastLampBasePixel, EastLampRows);

        /// <summary>Every cutout built with the painted city, in build order.</summary>
        public static readonly IReadOnlyList<Cutout> Cutouts = new[] { WestLamp, EastLamp };

        private Mesh _ownedMesh;
        private MeshRenderer _renderer;

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

        /// <summary>Shows or hides one cutout by name. False when it was not built.</summary>
        public static bool SetVisible(Transform root, string name, bool visible)
        {
            if (root == null) return false;
            foreach (var foreground in root.GetComponentsInChildren<TianyongPaintedForeground>(true))
            {
                if (foreground.name != name || foreground._renderer == null) continue;
                foreground._renderer.enabled = visible;
                return true;
            }
            return false;
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
