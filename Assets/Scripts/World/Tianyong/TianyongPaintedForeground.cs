using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// A foreground cutout of the existing west temple lamp roof. The source
    /// painting remains intact; only this traced silhouette participates in
    /// the same feet-depth sorting as actors. No pavement or walk mask changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongPaintedForeground : MonoBehaviour
    {
        public const string WestLampObjectName = "TempleWestLampRoofForeground";
        public const int SourceTileRow = 2; // zero-based: r03_c03
        public const int SourceTileColumn = 2;
        private const float SurfaceHeight = 0.01f;

        // Centre of the lamp's south ground contact, in original 6144px art.
        // Using the roof centre instead would sort its north-side actors wrong.
        public static readonly Vector2 WestLampBasePixel = new(2823f, 2503f);

        // Traced directly from r03_c03, top-left pixel coordinates. Each row is
        // (source y, left edge, right edge). The single-interval scanlines cover
        // the finial and eave silhouette, with no rectangular pavement patch.
        private static readonly Vector3[] RoofRows =
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

        private Mesh _ownedMesh;
        private MeshRenderer _renderer;

        public static void AddWestLamp(TianyongMapInstance map, Transform parent, Texture2D tile)
        {
            if (map == null || parent == null || tile == null) return;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError("[TianyongPaintedForeground] Sprites/Default is unavailable; west lamp occlusion cannot render.");
                return;
            }

            var go = new GameObject(WestLampObjectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = TianyongPaintedCity.PaintingToWorld(WestLampBasePixel, SurfaceHeight);
            var foreground = go.AddComponent<TianyongPaintedForeground>();
            foreground._ownedMesh = CreateWestLampRoofMesh();
            go.AddComponent<MeshFilter>().sharedMesh = foreground._ownedMesh;
            var material = new Material(shader)
            {
                name = WestLampObjectName + "Material",
                mainTexture = tile,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent,
            };
            map._materials.Add(material);
            foreground._renderer = go.AddComponent<MeshRenderer>();
            foreground._renderer.sharedMaterial = material;
            foreground._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreground._renderer.receiveShadows = false;
            foreground._renderer.sortingOrder = Mathf.RoundToInt(-go.transform.position.z * 10f);
        }

        /// <summary>
        /// Reproducible mesh data for the original r03_c03 texture. Vertices are
        /// relative to the ground-contact anchor; UVs use the unchanged tile.
        /// </summary>
        public static Mesh CreateWestLampRoofMesh()
        {
            var vertices = new Vector3[RoofRows.Length * 2];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[(RoofRows.Length - 1) * 6];
            var anchor = TianyongPaintedCity.PaintingToWorld(WestLampBasePixel, SurfaceHeight);
            for (var row = 0; row < RoofRows.Length; row++)
            {
                var source = RoofRows[row];
                for (var side = 0; side < 2; side++)
                {
                    var index = row * 2 + side;
                    var x = side == 0 ? source.y : source.z;
                    var pixel = new Vector2(SourceTileColumn * TianyongPaintedCity.TilePixels + x,
                        SourceTileRow * TianyongPaintedCity.TilePixels + source.x);
                    vertices[index] = TianyongPaintedCity.PaintingToWorld(pixel, SurfaceHeight) - anchor;
                    uv[index] = new Vector2(x / TianyongPaintedCity.TilePixels,
                        1f - source.x / TianyongPaintedCity.TilePixels);
                    colors[index] = Color.white;
                }
                if (row == RoofRows.Length - 1) continue;
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
                name = WestLampObjectName + "Mesh",
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
        {
            if (root == null) return false;
            foreach (var foreground in root.GetComponentsInChildren<TianyongPaintedForeground>(true))
            {
                if (foreground.name != WestLampObjectName || foreground._renderer == null) continue;
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
