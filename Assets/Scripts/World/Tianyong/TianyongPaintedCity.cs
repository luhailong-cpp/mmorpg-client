using System;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// World-anchored painted main city (2.5D, 天墉城). The 6144×6144 painted
    /// artwork (36 × 1024 px tiles) lies flat on the XZ play area and is viewed
    /// by a straight-down orthographic camera, so the perspective baked into
    /// the painting is shown as-is, the way classic Chinese 2.5D MMOs
    /// present their towns. Actors are camera-facing sprites drawn above it
    /// and sorted by Z (south draws in front).
    ///
    /// Walkability comes from a 150×150 mask derived from the artwork's
    /// pavement colours (see <see cref="IsPaintingWalkable"/>), replacing the
    /// procedural town's building footprints, which do not match the art.
    /// </summary>
    public static class TianyongPaintedCity
    {
        public const string RootName = "[TianyongPaintedCity]";
        public const string ResourcePath =
            "World/Tianyong/Backgrounds/tianyong_city_main_64x27_v1";
        public const string TileResourceFolder = "World/Tianyong/SceneTiles6x6/Tiles";

        public const int TileColumns = 6;
        public const int TileRows = 6;
        public const int TilePixels = 1024;
        public const int PaintingPixels = TileColumns * TilePixels; // 6144

        /// <summary>
        /// Where the square painting lies on the 400×300 play area: it spans
        /// the full depth and is centred in X, so nothing is cropped and the
        /// 50-unit margins on either side are never walkable.
        /// </summary>
        public static readonly Rect PaintingWorldRect = new(
            (TianyongMapDefinition.Width - TianyongMapDefinition.Depth) * 0.5f,
            0f,
            TianyongMapDefinition.Depth,
            TianyongMapDefinition.Depth);

        public static float PixelsPerUnit => PaintingPixels / PaintingWorldRect.height; // 20.48

        /// <summary>
        /// Source-map texels available for each rendered screen pixel at a
        /// given orthographic camera size. Values below 1 mean Unity must
        /// magnify the painting and bilinear filtering will soften it.
        /// </summary>
        public static float CalculateTexelsPerScreenPixel(float orthographicSize, int screenPixelHeight)
        {
            if (orthographicSize <= 0f || screenPixelHeight <= 0) return 0f;
            return 2f * orthographicSize * PixelsPerUnit / screenPixelHeight;
        }

        /// <summary>Navigation resolution used with the painted walk mask.</summary>
        public const float NavigationCellSize = 2f;
        public const int MaskResolution = 150;

        // Chunk ground boxes top out at y = -0.05; the painting sits just
        // above them so it wins the depth test while actor feet (resting on
        // the collider tops) still visually touch it.
        private const float GroundHeight = -0.045f;

        private static bool[] _mask;

        /// <summary>Painted mode applies only where matching art exists.</summary>
        public static bool IsEnabledFor(TianyongTheme theme, TianyongMapConfig config)
            => theme == TianyongTheme.City &&
               (config == null || config.PaintedCityGround);

        /// <summary>Painting pixel (origin top-left, y down) → world feet point.</summary>
        public static Vector3 PaintingToWorld(Vector2 pixel, float y = 0f)
            => new(
                PaintingWorldRect.xMin + pixel.x / PixelsPerUnit,
                y,
                PaintingWorldRect.yMax - pixel.y / PixelsPerUnit);

        /// <summary>World point → painting pixel (origin top-left, y down).</summary>
        public static Vector2 WorldToPainting(Vector3 world)
            => new(
                (world.x - PaintingWorldRect.xMin) * PixelsPerUnit,
                (PaintingWorldRect.yMax - world.z) * PixelsPerUnit);

        /// <summary>True when the world point lies on painted pavement.</summary>
        public static bool IsPaintingWalkable(Vector3 world)
        {
            var mask = LoadMask();
            var pixel = WorldToPainting(world);
            var cx = Mathf.FloorToInt(pixel.x / PaintingPixels * MaskResolution);
            var cy = Mathf.FloorToInt(pixel.y / PaintingPixels * MaskResolution);
            if (cx < 0 || cy < 0 || cx >= MaskResolution || cy >= MaskResolution) return false;
            return mask[cy * MaskResolution + cx];
        }

        /// <summary>Navigation grid that follows the painted pavement.</summary>
        public static TianyongNavigationGrid CreateNavigation()
            => new(NavigationCellSize, IsPaintingWalkable);

        private static bool[] LoadMask()
        {
            if (_mask != null) return _mask;
            var bytes = Convert.FromBase64String(WalkMaskBase64);
            var mask = new bool[MaskResolution * MaskResolution];
            for (var i = 0; i < mask.Length; i++)
                mask[i] = (bytes[i >> 3] & (0x80 >> (i & 7))) != 0;
            _mask = mask;
            return _mask;
        }

        /// <summary>
        /// Returns the source UV rectangle that fills a target aspect without
        /// stretching. Narrow targets crop the artwork's sides; wider targets
        /// crop top and bottom. Used by the single-image fallback.
        /// </summary>
        public static Rect CalculateCoverUvRect(float textureAspect, float targetAspect)
        {
            if (textureAspect <= 0f || targetAspect <= 0f)
                return new Rect(0f, 0f, 1f, 1f);

            if (targetAspect < textureAspect)
            {
                var width = targetAspect / textureAspect;
                return new Rect((1f - width) * 0.5f, 0f, width, 1f);
            }

            if (targetAspect > textureAspect)
            {
                var height = textureAspect / targetAspect;
                return new Rect(0f, (1f - height) * 0.5f, 1f, height);
            }

            return new Rect(0f, 0f, 1f, 1f);
        }

        /// <summary>
        /// Converts the built 3D town into painted-city presentation: all
        /// generated renderers are hidden and every collider above the ground
        /// plane is disabled (the painted walk mask is authoritative instead),
        /// then the 36 city tiles are laid over the play area as unlit quads.
        /// Falls back to the single wide image when the tiles are missing, and
        /// returns false — leaving the 3D look untouched — when neither exists.
        /// </summary>
        public static bool Apply(TianyongMapInstance instance)
        {
            if (instance?.Root == null) return false;

            var tiles = LoadTiles();
            Texture2D fallback = null;
            if (tiles == null)
            {
                fallback = Resources.Load<Texture2D>(ResourcePath);
                if (fallback == null)
                {
                    Debug.LogWarning(
                        $"[TianyongPaintedCity] Missing Resources/{TileResourceFolder} and {ResourcePath}; keeping 3D town visuals.");
                    return false;
                }
            }

            foreach (var renderer in instance.Root.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;

            // Only the chunk ground (top at y = -0.05) keeps its collider so
            // the CharacterController has a floor; roads, bridges, walls and
            // buildings belong to the procedural town, not the painting.
            // Matched by name rather than bounds: with autoSyncTransforms off,
            // Collider.bounds is stale for colliders created this frame.
            foreach (var collider in instance.Root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.gameObject.name != TianyongMapBuilder.GroundColliderName)
                    collider.enabled = false;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(instance.Root.transform, false);

            if (tiles != null)
                CreateTileGround(instance, root.transform, tiles);
            else
                CreateSingleImageGround(instance, root.transform, fallback,
                    new Rect(0f, 0f, TianyongMapDefinition.Width, TianyongMapDefinition.Depth));
            return true;
        }

        private static Texture2D[] LoadTiles()
        {
            var tiles = new Texture2D[TileColumns * TileRows];
            for (var r = 0; r < TileRows; r++)
            for (var c = 0; c < TileColumns; c++)
            {
                var tile = Resources.Load<Texture2D>($"{TileResourceFolder}/tianyong_r{r + 1:00}_c{c + 1:00}");
                if (tile == null) return null;
                tiles[r * TileColumns + c] = tile;
            }
            return tiles;
        }

        private static void CreateTileGround(TianyongMapInstance instance, Transform parent, Texture2D[] tiles)
        {
            var shader = GroundShader();
            var tileUnits = TilePixels / PixelsPerUnit; // 50 world units
            for (var r = 0; r < TileRows; r++)
            for (var c = 0; c < TileColumns; c++)
            {
                var tile = tiles[r * TileColumns + c];
                // Bilinear sampling must not pull the opposite edge in.
                tile.wrapMode = TextureWrapMode.Clamp;

                var material = new Material(shader) { name = $"TianyongTile_r{r + 1}_c{c + 1}" };
                material.mainTexture = tile;
                instance._materials.Add(material); // freed by TianyongMapInstance.Dispose

                var centerPixel = new Vector2((c + 0.5f) * TilePixels, (r + 0.5f) * TilePixels);
                CreateGroundQuad($"Tile_r{r + 1}_c{c + 1}", parent,
                    PaintingToWorld(centerPixel, GroundHeight), new Vector2(tileUnits, tileUnits), material);
            }
        }

        private static void CreateSingleImageGround(
            TianyongMapInstance instance, Transform parent, Texture2D art, Rect worldRect)
        {
            var shader = GroundShader();
            var material = new Material(shader) { name = "TianyongPaintedCityGround" };
            material.mainTexture = art;
            instance._materials.Add(material);

            // Cover-crop through the material's texture ST so the artwork
            // keeps its aspect over the (differently shaped) play area.
            var uv = CalculateCoverUvRect(
                (float)art.width / art.height,
                worldRect.width / worldRect.height);
            material.mainTextureOffset = new Vector2(uv.x, uv.y);
            material.mainTextureScale = new Vector2(uv.width, uv.height);

            CreateGroundQuad("Ground", parent,
                new Vector3(worldRect.center.x, GroundHeight, worldRect.center.y),
                new Vector2(worldRect.width, worldRect.height), material);
        }

        private static Shader GroundShader()
            => Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");

        private static GameObject CreateGroundQuad(
            string name, Transform parent, Vector3 position, Vector2 size, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            // +90° about X turns the quad's -Z face upward and its +Y (texture
            // top) toward world +Z, i.e. the painting's north is screen-up.
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            // The play area is walked on colliders built by the map; the
            // painting itself must not occlude the click-to-move ray.
            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
                else UnityEngine.Object.DestroyImmediate(collider);
            }

            // The ground must draw before every sprite whatever shader was
            // found: a transparent-queue fallback (Sprites/Default) would
            // otherwise sort against the actors' negative sorting orders.
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return quad;
        }

        /// <summary>
        /// 150×150 walk mask over the painting (row-major from the top-left,
        /// MSB first). Generated from the master image by classifying beige
        /// pavement pixels (low saturation, bright, warm), keeping cells with
        /// ≥55 % pavement, clipping the outer city wall and despeckling twice.
        /// </summary>
        private const string WalkMaskBase64 =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD8wAAAAAAAAAAAAAAAAAAAAAAD8wAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAAAAAA4DAAAAAAAAAAAAAAAAMAAAAAA4DAAAAAAAAAAAAAAAAAAAAAAA4DAAAA" +
            "AAAAAEAAAAAAAAAAAH/gAAIAAAAMAA/hAAAA8eeAAAH+AAAcAAAAOAA/xgAAA/+eAAABwAAAMAAAAMAAuxQAAA//+AAAx4AAAMAA" +
            "AAOAA/w4AAf///MAAx4ABAMAAAAeAB+wYAAf///8AB5wwDgMAAAAcAH/4AAA9///+AA9g73gMAAAAcA+EQAAAd///8AA4A//4MAA" +
            "AAeAwAAAAAZ///MAAQAeA4MAAAAcBgAAAAAZ///MAAdA8A4OAAAAcAAAAAAAB//8AAAP/wA8OAAAA8AAeAwAAB///IAAP/gAoGAA" +
            "AA8AB/gAAAf///8AAf+ABsGAAAA8YDpwAAAf///8AARwABkGAAAA8YDewAAAf/3/8ABh8ABsGAAAAwQDewOCD//3//Ahj8AAcGAA" +
            "AA4YDtwP/3/5jv///744IcHAAAB4AB/gP///AAN///7g9/8HAAAB4AAIAff1/AAB/39xAf/8HAAAB4AAAAeAA/AAB+ABgAf3gDAA" +
            "AB4AAAAAAAfAAB8AA4AfBgDAAAB4AAAAAAAfAAB4AA4AYAADAAAB4AQIAAAAcAAAcAAAw4AADAAADgAA/jAAAYAAAIAAA/4AwDgA" +
            "ADwAA/jAAAQAAAMAAAfgAwDgAADwAA/AAAAAAAAMAAAfwAADgAADwAAfAAAAAAAAIAAAPwAABAAADAAAOAAAAAAAAOAAAPgAAAAA" +
            "AAAAAOAAAAAAAAPAAAPgAAAAAAAAAAOAAADAAAAPAAAAAAAAAAAAAAAeAAADAAAAPAAAHgAAAAAAwAAAeAAADAAAAPAAAPgAAAGA" +
            "BwAAD/gAAAAAADOAAA/+AAAHAB4AAP/4AAAAAAPMAAA/+AAAHAB4AAP/8AAAAEB/MAAB//v+AHAB4AAP/4AAAAH/3IAAD///+AHA" +
            "BgAN///wAAAD/wMAAD//6vADABgAf///wAEQD9gMwAB/8AAgDABgB////wAM4H9AcwAD+8AAwDABwB+gB/gAM/H9j8wAB8YAAcDg" +
            "BwBuAAPwAA/n/nwgAB8AAA8DgBwAEAAHwAAfn/3weAHwAAAcDgBwAAAABwA//z/3/+AHgADA4DgBQAAAABwA//j/D//AHAADg4Bg" +
            "AwAAAABwH//j/j//4HAH//YBgAAAAAADx/f/////98HAH//wBgAAAAAAD7+f/////5+/AP8/AAAAABgCAD5+f/+c//4+8Af4fAAA" +
            "AABACAD5////////+8APwfAAAAACAHADg////////8+AfwfgAAAADAfADv////////4cAPgfgAAAADAfADv/////3///8AOAfgAA" +
            "AADAeADv///+g////+AHAfgAAAACAeEDv///dgf///+AP4fgAAAHiAf/Dv//P7gP9///AP//gAAAH/g///////b4P////AH//gAA" +
            "AP////////+72P////////wAAAP/////v//P9mf9///////wAAAP/0+f1////c8d////7///wAAAP/AcGBv///PB/////A/A/wAA" +
            "AH+AcABv///n/7////A/AfwAAAD8AIABv///5/v///8AAAOAAAAAMAAABgP//+s9//wAAAAAAAAAAMAAABgDf//f//9gAAAAAAAA" +
            "AAAAAABADP/+e//8AAAAAAAAAAAAAAADAAP/////8AAAAAAAAAAAAAAADAAP/////wAAAAAAAAAAAAAAABAAP/////4AAAAAAAAA" +
            "AAAAAMDAD///////gAADMgAAAAAPwD8AAD+f3/7+/gAAH/4AAAAAPAD8AAAGfn/z+4AAAH/8AAAAAHAAcAAAA/H/z+wAAAH/4AAA" +
            "AACAAYAAAAf///+wAAAH/4AAAAAAAAAAAAAf///4AAAAD/wAAAAAAAEAAAAAH///wAAAAAsAAAAAAAAOAAAAAH///wAAAAH/wAAA" +
            "AAAAOAAAAAEABAQAAAAf/4AAAAAAAOAAAAAEIGDQAAAAf/8AAAAAAAeAAAAAGIGDwAAAAf/8AAAAD///APwAAEAAAQAAAA///AAA" +
            "AD///APwAAH///wAAAB///gAAADn/8AAAAAH///gAAAB///gAAAAB/8AAAAA/////AAAB///4AAAAAfwAAAAA/////AAAP////gA" +
            "AAAPgAAAAA////+AAA+////gAAAAfgAAAAA9///PAAB8b/XPgAAAAfgAAAAA8///PAAB8A+APwAAAAfwAAAAAA//+PAABwA8AHwA" +
            "AAAfwD/AYAA//8PAAB4AcAHgAAAAfAD/AYAB///DAABwAcAHAAAAB4AHB0IAA///CAABwAMAHgAAAHwAcA+OBD///GgADwAMAHwA" +
            "AAHgAYAcPP////P+//wIegHgAAAHwAYAYP////////7/////gAAEPwAwAAH///////5BfP///gAAf/wAgAAD/////3/wACN/9/AA" +
            "AZ+OAwAADP5///H8gAAA/4+AAAA8HDgAACAA///AAAAAAfgIAAAA4B/gAOOAA///AAAAAAPAAAAAAwB/AAH/AA//8AAAAAAeAAAA" +
            "AAwAHgAH+AA//+AAAAAAcAAAAAAgAD8AfGAA///AAAAAA8AAAAABwAD8Y+GAA//+AAAAAB+AAAAABgAD//+AAA///AAAAAD+AAAA" +
            "ABgAD/8gAAA//+AAAAADn+AAAABgAH/8AAAA///AAAAADj/gAAABgAf/4AAAA///AAAAADD/gAAAB4M//wAAAP///wAAAAAAngAA" +
            "AA///9gAAAP///wAAAAAAD4AAAAHnz4AAAAP///wAAAAAAAcAAAADDjwAAAAPwH/wAAAAAAAcAAAAABHwAAAAPgAfgAAAAAAAMAA" +
            "AAAADwAAAAfgAPAAAAAAAAAAAAAAADwAAAAfAAPgAAAAAAAAAAAAAABgAAAAAAAHgAAAAAAAIAAAAAADgAAAAAAAAAAAAAH8AcAA" +
            "AAAABAAAAAAAADAAAAAH8AYAAAAAAD4AAAcAAADAAAAAJwBAAAAAAAD4AAAcAAAAf8AAAPwBAAAAAAAAAAAAAAAAAf8AAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAAAAAAAAAA" +
            "AAAAAAAAAAwPwAAAAAAAAAAAAAAAAAAAAAAwPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    }
}
