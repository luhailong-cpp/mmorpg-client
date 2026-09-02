using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    public sealed class TianyongPaintedCityTests
    {
        [Test]
        public void MainCityArtwork_IsAvailableAsRuntimeTexture()
        {
            var texture = Resources.Load<Texture2D>(TianyongPaintedCity.ResourcePath);

            Assert.That(texture, Is.Not.Null);
            Assert.That((float)texture.width / texture.height,
                Is.EqualTo(64f / 27f).Within(0.002f));
        }

        [Test]
        public void CoverCrop_NarrowTarget_CropsSidesWithoutDistortion()
        {
            var uv = TianyongPaintedCity.CalculateCoverUvRect(64f / 27f, 16f / 9f);

            Assert.That(uv.x, Is.EqualTo(0.125f).Within(0.0001f));
            Assert.That(uv.width, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(uv.y, Is.Zero);
            Assert.That(uv.height, Is.EqualTo(1f));
        }

        [Test]
        public void CoverCrop_MatchingTarget_UsesTheWholeArtwork()
        {
            var uv = TianyongPaintedCity.CalculateCoverUvRect(64f / 27f, 64f / 27f);

            Assert.That(uv, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
        }

        [Test]
        public void CityTiles_AreAvailableAsRuntimeTextures()
        {
            for (var r = 1; r <= TianyongPaintedCity.TileRows; r++)
            for (var c = 1; c <= TianyongPaintedCity.TileColumns; c++)
            {
                var tile = Resources.Load<Texture2D>(
                    $"{TianyongPaintedCity.TileResourceFolder}/tianyong_r{r:00}_c{c:00}");
                Assert.That(tile, Is.Not.Null, $"tile r{r} c{c} must be in Resources");
                Assert.That(tile.width, Is.EqualTo(TianyongPaintedCity.TilePixels),
                    $"tile r{r} c{c} must retain its full source width");
                Assert.That(tile.height, Is.EqualTo(TianyongPaintedCity.TilePixels),
                    $"tile r{r} c{c} must retain its full source height");
                Assert.That(tile.mipmapCount, Is.EqualTo(1), $"tile r{r} c{c} must not use mipmaps");
                Assert.That(tile.filterMode, Is.EqualTo(FilterMode.Bilinear),
                    $"tile r{r} c{c} must use clean bilinear sampling");
                Assert.That(tile.wrapMode, Is.EqualTo(TextureWrapMode.Clamp),
                    $"tile r{r} c{c} must clamp at seams");
            }
        }

        [Test]
        public void PlayerSprite_IsReadableAtDefaultZoom_AndKeepsHdSampling()
        {
            var texture = Resources.Load<Texture2D>(
                "World/Characters/QdaoHeadbandBoy/walk_S");

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(4096));
            Assert.That(texture.height, Is.EqualTo(512));
            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));

            var frameWorldHeight = texture.height /
                                   MmorpgClient.World.QdaoBoySpriteAnimator.PixelsPerUnit;
            Assert.That(frameWorldHeight, Is.EqualTo(8f).Within(0.001f),
                "512 px / 64 ppu should render as an 8-unit-tall visual without scaling the actor root");

            var config = TianyongMapConfig.LoadDefault();
            Assert.That(config, Is.Not.Null);
            var frameScreenHeight = frameWorldHeight * 1080f / (2f * config.CameraZoomDefault);
            Assert.That(frameScreenHeight, Is.InRange(150f, 170f),
                "The run frame should match the reference character-to-travel ratio at 1080p");
            Assert.That(frameScreenHeight, Is.LessThanOrEqualTo(texture.height),
                "The default camera should downsample the HD character frame, not magnify it");
        }

        [Test]
        public void PaintingCoordinates_RoundTrip_AndAnchorTheSquareArtOnThePlayArea()
        {
            var rect = TianyongPaintedCity.PaintingWorldRect;
            Assert.That(rect.height, Is.EqualTo(TianyongMapDefinition.Depth).Within(0.001f));
            Assert.That(rect.width, Is.EqualTo(rect.height).Within(0.001f), "the painting is square");
            Assert.That(rect.center.x, Is.EqualTo(TianyongMapDefinition.Width * 0.5f).Within(0.001f));

            // Top-left pixel is the north-west corner; bottom-right is south-east.
            var topLeft = TianyongPaintedCity.PaintingToWorld(Vector2.zero);
            Assert.That(topLeft.x, Is.EqualTo(rect.xMin).Within(0.001f));
            Assert.That(topLeft.z, Is.EqualTo(rect.yMax).Within(0.001f));
            var bottomRight = TianyongPaintedCity.PaintingToWorld(
                new Vector2(TianyongPaintedCity.PaintingPixels, TianyongPaintedCity.PaintingPixels));
            Assert.That(bottomRight.x, Is.EqualTo(rect.xMax).Within(0.001f));
            Assert.That(bottomRight.z, Is.EqualTo(rect.yMin).Within(0.001f));

            var pixel = new Vector2(1234f, 4321f);
            var back = TianyongPaintedCity.WorldToPainting(TianyongPaintedCity.PaintingToWorld(pixel));
            Assert.That(back.x, Is.EqualTo(pixel.x).Within(0.01f));
            Assert.That(back.y, Is.EqualTo(pixel.y).Within(0.01f));
        }

        [Test]
        public void WalkMask_FollowsThePaintedPavement()
        {
            // Plaza centre (the yin-yang) and the spawn on the central avenue.
            var plaza = TianyongPaintedCity.PaintingToWorld(new Vector2(2880f, 2790f));
            Assert.That(TianyongPaintedCity.IsPaintingWalkable(plaza), Is.True, "central plaza");
            Assert.That(TianyongPaintedCity.IsPaintingWalkable(TianyongMapDefinition.DefaultSpawn), Is.True, "spawn");

            // Main temple roof, the north-west canal and outside the painting.
            var temple = TianyongPaintedCity.PaintingToWorld(new Vector2(2880f, 1500f));
            Assert.That(TianyongPaintedCity.IsPaintingWalkable(temple), Is.False, "temple roof");
            var canal = TianyongPaintedCity.PaintingToWorld(new Vector2(1960f, 900f));
            Assert.That(TianyongPaintedCity.IsPaintingWalkable(canal), Is.False, "canal");
            Assert.That(TianyongPaintedCity.IsPaintingWalkable(new Vector3(10f, 0f, 150f)), Is.False, "west margin");

            var navigation = TianyongPaintedCity.CreateNavigation();
            Assert.That(navigation.CellSize, Is.EqualTo(TianyongPaintedCity.NavigationCellSize));
            Assert.That(navigation.IsWalkable(plaza), Is.True);
            Assert.That(navigation.IsWalkable(temple), Is.False);
            var path = navigation.FindPath(TianyongMapDefinition.DefaultSpawn, plaza);
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(2), "spawn must reach the plaza");
            foreach (var point in path)
                Assert.That(navigation.IsWalkable(point), Is.True, $"path point {point} must be on pavement");
        }

        [Test]
        public void CityBuild_HidesTownRenderers_AndLaysTilesOnPlayArea()
        {
            var parent = new GameObject("PaintedCityTestRoot");
            TianyongMapInstance map = null;
            try
            {
                map = TianyongMapBuilder.Build(parent.transform, TianyongTheme.City, null);

                var ground = map.Root.transform.Find(TianyongPaintedCity.RootName);
                Assert.That(ground, Is.Not.Null, "painted ground root must exist under the map root");
                Assert.That(ground.childCount,
                    Is.EqualTo(TianyongPaintedCity.TileColumns * TianyongPaintedCity.TileRows));

                var tileUnits = TianyongPaintedCity.TilePixels / TianyongPaintedCity.PixelsPerUnit;
                var bounds = new Bounds();
                var first = true;
                foreach (Transform tile in ground)
                {
                    Assert.That(tile.GetComponent<MeshRenderer>().enabled, Is.True);
                    Assert.That(tile.GetComponent<Collider>(), Is.Null,
                        "the painting must not intercept click-to-move rays");
                    Assert.That(tile.localScale.x, Is.EqualTo(tileUnits).Within(0.01f));
                    Assert.That(tile.localScale.y, Is.EqualTo(tileUnits).Within(0.01f));
                    var tileBounds = new Bounds(tile.position, new Vector3(tileUnits, 0f, tileUnits));
                    if (first) { bounds = tileBounds; first = false; }
                    else bounds.Encapsulate(tileBounds);
                }

                var rect = TianyongPaintedCity.PaintingWorldRect;
                Assert.That(bounds.min.x, Is.EqualTo(rect.xMin).Within(0.01f));
                Assert.That(bounds.max.x, Is.EqualTo(rect.xMax).Within(0.01f));
                Assert.That(bounds.min.z, Is.EqualTo(rect.yMin).Within(0.01f));
                Assert.That(bounds.max.z, Is.EqualTo(rect.yMax).Within(0.01f));

                foreach (var renderer in map.Root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.transform.parent == ground) continue;
                    Assert.That(renderer.enabled, Is.False,
                        $"town renderer '{renderer.name}' must be hidden in painted mode");
                }

                // Only the chunk ground colliders survive; the procedural
                // buildings, roads and bridges do not match the artwork.
                // (Matched by name: Collider.bounds is stale for colliders
                // created in the same frame while autoSyncTransforms is off.)
                var groundColliders = 0;
                foreach (var collider in map.Root.GetComponentsInChildren<Collider>(true))
                {
                    if (!collider.enabled) continue;
                    Assert.That(collider.gameObject.name, Is.EqualTo(TianyongMapBuilder.GroundColliderName),
                        $"collider '{collider.name}' must be disabled in painted mode");
                    groundColliders++;
                }
                Assert.That(groundColliders, Is.GreaterThan(0), "the floor must keep its colliders");

                // Movement follows the painted pavement mask.
                Assert.That(map.Navigation.CellSize, Is.EqualTo(TianyongPaintedCity.NavigationCellSize));
                Assert.That(map.Navigation.IsWalkable(TianyongMapDefinition.DefaultSpawn), Is.True);
            }
            finally
            {
                map?.Dispose();
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void NonCityThemes_KeepTheGenerated3dTown()
        {
            var parent = new GameObject("PaintedCityThemeTestRoot");
            TianyongMapInstance map = null;
            try
            {
                map = TianyongMapBuilder.Build(parent.transform, TianyongTheme.Market, null);

                Assert.That(map.Root.transform.Find(TianyongPaintedCity.RootName), Is.Null);
                var anyEnabled = false;
                foreach (var renderer in map.Root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled) continue;
                    anyEnabled = true;
                    break;
                }
                Assert.That(anyEnabled, Is.True, "non-city themes must keep their 3D visuals");
            }
            finally
            {
                map?.Dispose();
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void CameraController_AppliesConfiguredZoomWindow()
        {
            var cameraObject = new GameObject("PaintedCityCameraTest", typeof(Camera));
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                var controller = new TianyongCameraController(camera, 10f, 55f, 18f);

                Assert.That(camera.orthographic, Is.True);
                Assert.That(camera.orthographicSize, Is.EqualTo(18f).Within(0.001f));
                Assert.That(controller, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void DefaultPaintedCityZoom_KeepsNativeTexelDensityAt1080p()
        {
            var config = TianyongMapConfig.LoadDefault();

            Assert.That(config, Is.Not.Null);
            var texelsPerPixel = TianyongPaintedCity.CalculateTexelsPerScreenPixel(
                config.CameraZoomDefault, 1080);
            Assert.That(texelsPerPixel, Is.GreaterThanOrEqualTo(1f),
                "The entry camera must not magnify the painted map at 1080p; " +
                "magnification makes the otherwise full-resolution tiles look blurry.");
        }
    }
}
