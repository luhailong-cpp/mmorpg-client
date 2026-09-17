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
            {
                CreateTileGround(instance, root.transform, tiles);
                TianyongPaintedForeground.AddAll(instance, root.transform, tiles);
            }
            else
                CreateSingleImageGround(instance, root.transform, fallback,
                    new Rect(0f, 0f, TianyongMapDefinition.Width, TianyongMapDefinition.Depth));
            instance.ConfigureCityTiles("tianyong", "festival");
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
        /// MSB first). Festival artwork ground and obstacle polygons are traced in
        /// image/tianyong_festival_hd_20260910/runtime/build_city_runtime.py.
        /// Cells require 70% ground coverage and belong to the spawn component;
        /// bridges and stairs remain connected, roofs/canals/props are blocked.
        /// </summary>
        private const string WalkMaskBase64 =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAACgAAAAAAAeAAAAAAAAAAAAAAAHgAAAAAAAeAAAAAAAOAAAAAAAHgAAAAAAAeAAAAAAAHAAAA" +
            "AAAHgAAAAAAAeAAAAAAAHAAAAAAAfgAAAAA////4AAAAAHgAAAAAAfgAAAAAj///wAAABAHgAAAAAAfgAAAAAh///wAA//AHwAAA" +
            "AAA/gAAAAAh///wAA//AHgAAAAAA/gAAAAAh///wAA//AH4AAAAAAxwAAAAAh///wAA/wAH4AAAAAAz4AAAAAh///wAA/wAH8AAA" +
            "AAB/4AAAAB/////AA/gAA+AAAAAB/AAAAAB/////AA/gcA+AAAAAD+AAAAAB/////AAPg8AeAAAAAD+AAAAAB/////wA/38AOAAA" +
            "AAD+AAA+AB/////wA//4AOAAAAAD8AAA+AB+AAAfwA//4AOAAAAAD8AAA/eB8AAAfwQ//4AfAAAAAB+AAA//h8AAAPz+/wcAfAAA" +
            "AAB+AAA/h58AAAH2H/wOA/AAAAADeAAA+AZ8AAAH8B/gPh+AAAAAC+AAA+AB4AAAH4AfgH/+AAAAADfAAB+AB4AAAHAAfgP//AAA" +
            "AACPgAB+AB4AAAHwAfwPn8gAAAACHwAH4AD4AAAHAAfwPL/gAAAAAP9//8AD4AAAHgAP////gAAAAAH///4AC4AAAHQAP/+//gAA" +
            "AAAH2AAAAH4AAAH4AP////gAAAAAHkAAAAH4AAAD4AP/8AAAAAAAAHsAAAAHwAAAD4AH/8AAAAAAAAH8AAAAH4AAAD4AH/8AAAAA" +
            "AAAH8AAAAH4AAAD4AHH8AAAAAAAHCIAAAAH4AAAH4AAD8AAAAAAAHz4AAAAH4AAAH4AAD8AAAAAAAGb8AAAAH4AAAH4AAD8AAAAA" +
            "AAP///wAAA4AAAHAAAB//+AAAAAH///wAAA4AAAHAAAB///gAAAAP///4AAAeAAAfAACD///gAAAAP///4AAIUAAAPAACD///wAA" +
            "AAf///+AAIUB/gLAACD/wBwAAAAf////wAIcB/gPAAD/8AAwAAAAf+AH/wAIcB/gPAAH/8AAwAAAAfgAH/4AIcB/gOGAH/4AAYAA" +
            "AAfgAD/4AY8D/gPHAH/oAAYAAAA+AAC/wB8/////PwH/YAAYAAAA+AAD/gD///////4D/4AAYAAAA/AAD/gH///////8D9YAA8AA" +
            "AA/AAD/Af///////8D4MAA8AAAA/AAD/A////////+B4MAA8AAABwAAAPB/////////B4H//8AAABwAAAHB/////////hwH+H+AA" +
            "ABwP+wHB/////////gwD4H+AAABwH+wHB/////////gwDwD+AAABwD/wDP/////////wwDwD+AAADwB/wDP/////////wwDgB/AA" +
            "ADwB8ADP/////////wwDgB/AAADwB8ADP/////////4wDgDPAAADwB8ADf/////////44DgDPAAADwB8ADf/////////44HwDPgA" +
            "ADwB+ADf/////////4//4HHAAAD///wHf/////////4//8/HAAAB/////f/////////4Z///HAAAB4fw4Pf/////////4b////AA" +
            "ABgPw4HP/////////8WA8B9AAABgPwYHP/////////8cAABgAAABgHwYDP/////////48AAAAAAAAgHwYDP/////////w4AAAAAA" +
            "AAgHgYCP/////////h4AAAAAAAAgHgYCDw//////+DBYAAAAAAAAgPgYCAQ//////+ACYAAAAAAAAgPgYDAAD/////gAC4AAAAAA" +
            "AAAPgYDgAB/////AAD4AAAAAAAAAHgYDgAB/////AAH8AAAAAAAAAB/ABwAA/////AAH8AAAAAAAAABPABwAA/////AAD/v//8AA" +
            "AAAB/AAAAA+P/8PAAB/v//YAAAAAB/AAAAA+P/8OAAAv//+AAAAAAB/AAAAAeP/8IAAAG//+AAAAAAB/AAAAAGP/8AAAAA///4AA" +
            "AAAA/AAAAAD///wAAAAB//sAAAAAA/AAAAAD///wAAAAA//8AAAAAB/8AAAAH///wAAAAB//8AAAAAB8AAAAAH///wAAAAB//8AA" +
            "AAAD4AAAAAH///wAADwP//8AAAAf/4AAAAAH///wAAHwf//8AAAAf/wAAAgAH///wAAPwf//8AAAAB/wAABgAH///wAAHwf//+AA" +
            "AAB/gAABwAH///wAAHwf//+AAAAA/gAABwAH///4AADwf//+AAAAA/wAAB4AD///4AAD+//8+AAAAAfwAAB4AD///4AAD8AH4AAA" +
            "AAAf4AAB8AD///wAAD+AH8AAAAAAf/8AIMAD///wAAD+ADwAAAAAAYeAH4MAD///4AAH8ADwAAAAAAQcAD4MAD///4AP/8AH4AAA" +
            "AAAQcAD4MAH///4AO/8AH4AAAAAAQIAB4cAf///8AM4AAH8AAAAAAwIAB/8AP///8AfgAAP8AAAAA/wYABwcAP///8AcAAD///AA" +
            "AB/wYABwd/P///9/YAACf/fAAAB/wYABwf/4///H3wAAB//fAAAB/f4ABw+B4f/+GA4AAA//AAAAB4P8AD/4AYf/+EAYAAAf/AAA" +
            "ABwH+AD/YAIf/+AAYAAAYAAAAABwD/AH+YAAf/+AAAAAAQAAAAABwB/8P/QAAf/+AAAAAAAAAAAABwB///+AAAf/+AAAAAAAAAAA" +
            "ABwB//wAAAAf/+AAAAAAAAAAAABgBv/AAAAAf/+AAAAAAAAAAAABgAH/gAAAH///wAAAAAAAAAAAAgAH+AAAAD///wAAAAAAAAAA" +
            "AAQAH+AAAAAAAAAAAAAAAAAAAAAfgH/AAAAAAAAAAAAAAAAAAAAAPDv/AAAAAAAAAAAAAAAAAAAAAPX/7AAAAAAAAAAAAAAAAAAA" +
            "AAH/f7gAAAAAAAAAAAAAAAAAAAAF///gAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    }
}
