using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    public sealed class TianyongPaintedCityTests
    {
        [Test]
        public void CurrentMainArtwork_IsTheDeliveredNativeSquareLayout()
        {
            var main = CurrentMapCatalog.Find("main");
            Assert.That(main.SceneConfigId, Is.EqualTo(1u));
            Assert.That(main.TexturePath, Is.EqualTo("World/CurrentMaps20261009/main/layout"));
            var texture = main.LoadTexture();
            Assert.That(texture.width, Is.EqualTo(1254));
            Assert.That(texture.height, Is.EqualTo(1254));
            Assert.That(main.WorldRect, Is.EqualTo(new Rect(50f, 0f, 300f, 300f)));
            Assert.That(main.Spawn, Is.EqualTo(new Vector3(199f, 0f, 133f)));
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
        public void AllCurrentLayoutsAndMatchingNavigation_AreDeliveredRuntimeResources()
        {
            Assert.That(CurrentMapCatalog.All.Count, Is.EqualTo(3));
            foreach (var definition in CurrentMapCatalog.All)
            {
                Assert.That(Resources.Load<TextAsset>(definition.SourcePath), Is.Not.Null, definition.Key);
                Assert.That(Resources.Load<TextAsset>(definition.MaskPath), Is.Not.Null, definition.Key);
                var texture = definition.LoadTexture();
                Assert.That(texture.width, Is.EqualTo(1254), definition.Key);
                Assert.That(texture.height, Is.EqualTo(1254), definition.Key);
                Assert.That(texture.mipmapCount, Is.EqualTo(1), definition.Key);
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear), definition.Key);
                Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), definition.Key);
                Assert.That(definition.CreateNavigation().CellSize, Is.EqualTo(2f), definition.Key);
                Assert.That(definition.CreateNavigation().IsWalkable(definition.Spawn), Is.True, definition.Key);
            }
        }

        [Test]
        public void PlayerSprite_KeepsNativeScaleUnderCurrentRuntimeCameraDefaults()
        {
            using var run = QdaoActionResources.Acquire("00_reference_topright_boy", "run", "S");
            Assert.That(run, Is.Not.Null);
            var sprite = run.Frames[0];
            var texture = sprite.texture;
            Assert.That(texture.width, Is.EqualTo(1024));
            Assert.That(texture.height, Is.EqualTo(1024));
            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(104f));
            var frameWorldHeight = sprite.rect.height / sprite.pixelsPerUnit;
            Assert.That(frameWorldHeight, Is.EqualTo(QdaoBoySpriteAnimator.FrameWorldHeight).Within(.01f));
            var host = new GameObject("CurrentMapCameraContract");
            var cameraObject = new GameObject("CurrentMapCamera", typeof(Camera));
            var sunObject = new GameObject("CurrentMapSun", typeof(Light));
            try
            {
                Assert.That(TianyongMapConfig.LoadDefault(), Is.Null, "旧配置已退役，当前地图不恢复旧资源");
                var camera = cameraObject.GetComponent<Camera>();
                var runtime = host.AddComponent<TianyongMapRuntime>();
                runtime.Initialize(null, camera, sunObject.GetComponent<Light>(), null);
                Assert.That(camera.orthographic, Is.True);
                Assert.That(camera.orthographicSize, Is.EqualTo(35f));
                var projectedHeight = frameWorldHeight * 1080f / (2f * camera.orthographicSize);
                Assert.That(projectedHeight, Is.EqualTo(1024f / 104f * 1080f / 70f).Within(.01f));
                Assert.That(projectedHeight, Is.LessThanOrEqualTo(texture.height));
                // 当前约152屏幕像素只验证实际35相机合同，不代表旧27相机约197像素的视觉SLO仍成立。
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(sunObject); }
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
        public void CurrentMainBuild_UsesOnlyItsOwnGroundColliderAndNavigation()
        {
            var parent = new GameObject("CurrentMapGeometryContract");
            TianyongMapInstance map = null;
            try
            {
                var main = CurrentMapCatalog.Find("main");
                map = CurrentMapBuilder.Build(parent.transform, main);
                var ground = map.Root.transform.Find(CurrentMapBuilder.GroundName);
                Assert.That(ground, Is.Not.Null);
                var renderer = ground.GetComponent<MeshRenderer>();
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.sharedMaterial.mainTexture, Is.SameAs(main.LoadTexture()));
                Assert.That(ground.localScale, Is.EqualTo(new Vector3(300f, 300f, 1f)));
                Assert.That(ground.localPosition, Is.EqualTo(new Vector3(200f, -.045f, 150f)));
                Assert.That(ground.GetComponent<Collider>(), Is.Null, "布局图不能截走点击寻路射线");
                Assert.That(map.Root.GetComponentsInChildren<TianyongPaintedForeground>(true), Is.Empty);
                Assert.That(map.Root.GetComponentsInChildren<TianyongCityNpcs>(true), Is.Empty);
                var floors = map.Root.GetComponentsInChildren<Collider>(true);
                Assert.That(floors.Length, Is.EqualTo(1));
                Assert.That(floors[0].name, Is.EqualTo(TianyongMapBuilder.GroundColliderName));
                Assert.That(floors[0].enabled, Is.True);
                Assert.That(map.Navigation.CellSize, Is.EqualTo(2f));
                Assert.That(map.Navigation.IsWalkable(main.Spawn), Is.True);
                Assert.That(map.Navigation.IsWalkable(new Vector3(49f, 0f, 150f)), Is.False);
            }
            finally { map?.Dispose(); Object.DestroyImmediate(parent); }
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
        public void CurrentMainLayout_KeepsNative1254PixelsAtItsAuthoredWorldScale()
        {
            var main = CurrentMapCatalog.Find("main");
            var texture = main.LoadTexture();
            Assert.That(texture.width, Is.EqualTo(1254));
            Assert.That(texture.height, Is.EqualTo(1254));
            Assert.That(main.WorldRect.width, Is.EqualTo(300f));
            Assert.That(main.WorldRect.height, Is.EqualTo(300f));
            Assert.That(texture.width / main.WorldRect.width, Is.EqualTo(4.18f).Within(.0001f));
            Assert.That(texture.height / main.WorldRect.height, Is.EqualTo(4.18f).Within(.0001f));
            Assert.That(main.LoadTexture(), Is.SameAs(texture), "加载原图，不制造假高清上采样副本");
        }
    }
}
