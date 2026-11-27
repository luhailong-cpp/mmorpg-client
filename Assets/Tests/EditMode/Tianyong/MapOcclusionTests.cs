using System;
using System.Reflection;
using MmorpgClient.Game.CityNpc;
using MmorpgClient.World;
using MmorpgClient.World.Occlusion;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class MapOcclusionTests
    {
        private static readonly Rect MapRect = new(0f, 0f, 300f, 300f);

        private GameObject _cameraObject;
        private Camera _topDownCamera;

        [SetUp]
        public void SetUp()
        {
            TianyongClickMarker.ResetStaticCache();
            _cameraObject = new GameObject("MapOcclusionTestCamera", typeof(Camera));
            _cameraObject.transform.position = new Vector3(150f, 110f, 150f);
            _cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _topDownCamera = _cameraObject.GetComponent<Camera>();
            _topDownCamera.orthographic = true;
            _topDownCamera.orthographicSize = 27f;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MapOcclusionRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer == null) continue;
                renderer.DisposeOwnedResources();
                UnityEngine.Object.DestroyImmediate(renderer.gameObject);
            }
            foreach (var marker in UnityEngine.Object.FindObjectsByType<TianyongClickMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker != null) UnityEngine.Object.DestroyImmediate(marker.gameObject);
            }
            TianyongClickMarker.ResetStaticCache();
            MapOcclusionRenderer.ResetSharedFallbackMaterialsForTests();

            if (_cameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_cameraObject);
                _cameraObject = null;
                _topDownCamera = null;
            }
        }

        [Test]
        public void Contract_RoundTripsJson_AndDefaultsHorizontalBaseline()
        {
            var data = CreateSampleData();
            string json = data.ToJson(true);
            var parsed = MapOcclusionData.Parse(json);

            Assert.That(parsed.schemaVersion, Is.EqualTo(MapOcclusionData.CurrentSchemaVersion));
            Assert.That(parsed.occluders.Length, Is.EqualTo(2));
            Assert.That(parsed.fadeZones.Length, Is.EqualTo(2));

            // Second occluder omits baselinePixels and must default to its southmost horizontal edge (y=700).
            parsed.occluders[1].ResolveBaselinePixels(out var p0, out var p1);
            Assert.That(p0.y, Is.EqualTo(700f).Within(0.01f));
            Assert.That(p1.y, Is.EqualTo(700f).Within(0.01f));
            Assert.That(p0.x, Is.EqualTo(600f).Within(0.01f));
            Assert.That(p1.x, Is.EqualTo(800f).Within(0.01f));
        }

        [Test]
        public void Contract_RejectsSelfIntersectionOutOfBoundsDuplicateIdsAndInvalidAlpha()
        {
            var bowtie = CreateSampleData();
            bowtie.occluders[0].polygonPixels = new[]
            {
                new MapOcclusionPoint(100, 100),
                new MapOcclusionPoint(200, 200),
                new MapOcclusionPoint(200, 100),
                new MapOcclusionPoint(100, 200)
            };
            Assert.Throws<InvalidOperationException>(() => bowtie.Validate());

            var outOfBounds = CreateSampleData();
            outOfBounds.occluders[0].polygonPixels[0] = new MapOcclusionPoint(1001, 100);
            Assert.Throws<InvalidOperationException>(() => outOfBounds.Validate());

            var duplicateId = CreateSampleData();
            duplicateId.occluders[1].id = duplicateId.occluders[0].id;
            Assert.Throws<InvalidOperationException>(() => duplicateId.Validate());

            var badAlpha = CreateSampleData();
            badAlpha.fadeZones[0].targetAlpha = 1.5f;
            Assert.Throws<InvalidOperationException>(() => badAlpha.Validate());
        }

        [Test]
        public void DepthMapper_MapsSouthCloserThanNorth_WithHighPrecisionBand()
        {
            float northY = OcclusionDepthMapper.WorldZToDepthY(280f, MapRect);
            float midY = OcclusionDepthMapper.WorldZToDepthY(150f, MapRect);
            float southY = OcclusionDepthMapper.WorldZToDepthY(20f, MapRect);

            Assert.That(northY, Is.GreaterThanOrEqualTo(OcclusionDepthMapper.DepthYMin));
            Assert.That(southY, Is.LessThanOrEqualTo(OcclusionDepthMapper.DepthYMax));
            Assert.That(southY, Is.GreaterThan(midY));
            Assert.That(midY, Is.GreaterThan(northY));
            Assert.That(OcclusionDepthMapper.LabelClearanceY, Is.GreaterThan(OcclusionDepthMapper.DepthYMax));
        }

        [Test]
        public void DepthMapper_EvaluatesDiagonalBaselineAndClampsEndpoints()
        {
            var p0 = new Vector2(100f, 100f);
            var p1 = new Vector2(200f, 200f);

            Assert.That(OcclusionDepthMapper.EvaluateBaselineZ(p0, p1, 100f), Is.EqualTo(100f).Within(0.001f));
            Assert.That(OcclusionDepthMapper.EvaluateBaselineZ(p0, p1, 150f), Is.EqualTo(150f).Within(0.001f));
            Assert.That(OcclusionDepthMapper.EvaluateBaselineZ(p0, p1, 200f), Is.EqualTo(200f).Within(0.001f));
            Assert.That(OcclusionDepthMapper.EvaluateBaselineZ(p0, p1, 50f), Is.EqualTo(100f).Within(0.001f));
            Assert.That(OcclusionDepthMapper.EvaluateBaselineZ(p0, p1, 250f), Is.EqualTo(200f).Within(0.001f));
        }

        [Test]
        public void FadeZones_OverlapTakesLowestAlpha_AndSmoothlyTransitionsWithoutJump()
        {
            var data = CreateSampleData();
            var compiled = OcclusionDepthMapper.CompileFadeZones(data, MapRect);

            // Pixel (150, 250) -> world (45, 225) lies inside zone A (alpha=0.5) only.
            var zoneAOnly = OcclusionDepthMapper.PixelToWorld(new Vector2(150, 250), 1000, 1000, MapRect);
            Assert.That(OcclusionDepthMapper.EvaluateFadeZones(zoneAOnly, compiled, out float aAlpha, out float aDur), Is.True);
            Assert.That(aAlpha, Is.EqualTo(0.5f).Within(0.001f));

            // Pixel (250, 250) -> world (75, 225) lies inside BOTH zone A (0.5) and zone B (0.3).
            var overlap = OcclusionDepthMapper.PixelToWorld(new Vector2(250, 250), 1000, 1000, MapRect);
            Assert.That(OcclusionDepthMapper.EvaluateFadeZones(overlap, compiled, out float minAlpha, out _), Is.True);
            Assert.That(minAlpha, Is.EqualTo(0.3f).Within(0.001f));

            var model = new ActorTranslucencyModel();
            Assert.That(model.CurrentAlpha, Is.EqualTo(1f).Within(0.001f));

            // Halfway into 0.2s fade from 1.0 -> 0.5
            Assert.That(model.Step(true, aAlpha, aDur, 0.1f), Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(model.Step(true, aAlpha, aDur, 0.1f), Is.EqualTo(0.5f).Within(0.001f));

            // Enter deeper overlap 0.5 -> 0.3
            Assert.That(model.Step(true, 0.3f, 0.2f, 0.2f), Is.EqualTo(0.3f).Within(0.001f));

            // Leave zone B while still in zone A: transitions to 0.5, never jumps to 1.0
            Assert.That(model.Step(true, 0.5f, 0.2f, 0.1f), Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(model.Step(true, 0.5f, 0.2f, 0.1f), Is.EqualTo(0.5f).Within(0.001f));

            // Leave all zones: fades smoothly back to 1.0
            Assert.That(model.Step(false, 1f, 0f, 0.1f), Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(model.Step(false, 1f, 0f, 0.1f), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void MeshBuilder_MergesMultipleOccludersIntoOneMeshWithInterpolatedDepthY()
        {
            var data = CreateSampleData();
            var mesh = MapOcclusionMeshBuilder.BuildMergedMesh(data, MapRect);
            Assert.That(mesh, Is.Not.Null);
            try
            {
                // Two quads -> 4 triangles -> 12 vertices in one merged mesh.
                Assert.That(mesh.triangles.Length, Is.EqualTo(12));
                Assert.That(mesh.vertexCount, Is.EqualTo(12));

                // Diagonal occluder #1 baseline runs from (100,400) [Z=180] to (300,600) [Z=120]:
                // east vertices must have larger depth Y (closer to camera) than west vertices.
                float westY = float.NaN;
                float eastY = float.NaN;
                foreach (var v in mesh.vertices)
                {
                    Assert.That(v.y, Is.InRange(OcclusionDepthMapper.DepthYMin, OcclusionDepthMapper.DepthYMax));
                    if (Mathf.Abs(v.x - 30f) < 0.05f) westY = v.y;
                    if (Mathf.Abs(v.x - 90f) < 0.05f) eastY = v.y;
                }
                Assert.That(float.IsNaN(westY), Is.False);
                Assert.That(float.IsNaN(eastY), Is.False);
                Assert.That(eastY, Is.GreaterThan(westY));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RendererLifecycle_RegistersUnregistersAndDisposesOwnedResources()
        {
            var mapRoot = new GameObject("MapOcclusionLifecycleRoot");
            try
            {
                Assert.That(MapOcclusionRenderer.Active, Is.Null);
                var renderer = MapOcclusionRenderer.Attach(mapRoot, CreateSampleData(), MapRect);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(MapOcclusionRenderer.Active, Is.SameAs(renderer));
                Assert.That(renderer.HasDepthOccluders, Is.True);
                Assert.That(renderer.HasFadeZones, Is.True);

                var ownedMesh = renderer.DepthMaskMesh;
                Assert.That(ownedMesh != null, Is.True);

                // Disabling unregisters cleanly without destroying mesh (supports rollback/toggle).
                mapRoot.SetActive(false);
                Assert.That(MapOcclusionRenderer.Active, Is.Null);
                mapRoot.SetActive(true);
                Assert.That(MapOcclusionRenderer.Active, Is.SameAs(renderer));

                // Disposing frees owned mesh and clears active registry.
                renderer.DisposeOwnedResources();
                Assert.That(MapOcclusionRenderer.Active, Is.Null);
                Assert.That(ownedMesh == null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mapRoot);
            }
        }

        [Test]
        public void TopDownClearance_LiftsNameplateAboveOccluders_AndPreservesDefaultWhenInactive()
        {
            var actor = new GameObject("ActorRoot");
            actor.transform.position = new Vector3(60f, 0f, 150f);
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(actor.transform, false);
            var billboard = WorldLabelBillboard.Attach(labelGo);
            var mapRoot = new GameObject("MapOcclusionClearanceRoot");

            var refresh = typeof(WorldLabelBillboard).GetMethod("RefreshForCamera", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(refresh, Is.Not.Null);

            try
            {
                refresh.Invoke(billboard, new object[] { _topDownCamera });
                Assert.That(labelGo.transform.position.y, Is.EqualTo(0.2f).Within(0.001f));

                MapOcclusionRenderer.Attach(mapRoot, CreateSampleData(), MapRect);
                refresh.Invoke(billboard, new object[] { _topDownCamera });
                Assert.That(labelGo.transform.position.y, Is.EqualTo(OcclusionDepthMapper.LabelClearanceY).Within(0.001f));
                Assert.That(labelGo.transform.position.y, Is.GreaterThan(OcclusionDepthMapper.DepthYMax));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mapRoot);
                UnityEngine.Object.DestroyImmediate(actor);
            }
        }

        [Test]
        public void ClickMarker_UsesDepthBandWhenOccluded_AndKeepsGroundLiftWhenInactive()
        {
            var point = new Vector3(150f, 0f, 180f);
            var mapRoot = new GameObject("MapOcclusionMarkerRoot");
            try
            {
                var plain = TianyongClickMarker.Spawn(point, _topDownCamera);
                Assert.That(plain.transform.position.y, Is.EqualTo(TianyongClickMarker.GroundLift).Within(0.001f));

                MapOcclusionRenderer.Attach(mapRoot, CreateSampleData(), MapRect);
                var occluded = TianyongClickMarker.Spawn(point, _topDownCamera);
                float expectedY = OcclusionDepthMapper.WorldZToDepthY(point.z, MapRect) + OcclusionDepthMapper.ClickMarkerDepthOffset;
                Assert.That(occluded.transform.position.y, Is.EqualTo(expectedY).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mapRoot);
            }
        }

        [Test]
        public void GuiyuanNpc_DedicatedSprite_UsesWorldSortingOrderInsteadOfLabelBoost()
        {
            var host = new GameObject("GuiyuanNpcTestHost");
            host.transform.position = new Vector3(120f, 0f, 140f);
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.04f), 16f);
            try
            {
                var npc = host.AddComponent<GuiyuanCityNpcActor>();
                var attach = typeof(GuiyuanCityNpcActor).GetMethod("AttachDedicatedSprite", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(attach, Is.Not.Null);
                attach.Invoke(npc, new object[] { host, sprite });

                var visual = host.transform.Find("DedicatedNpcSprite");
                Assert.That(visual, Is.Not.Null);
                Assert.That(visual.GetComponent<WorldLabelBillboard>(), Is.Null,
                    "NPC 立绘本体不应挂载 WorldLabelBillboard（会错误叠加 +8000 名字牌排序层级）");

                npc.RefreshDedicatedVisual(_topDownCamera);
                var sr = visual.GetComponent<SpriteRenderer>();
                Assert.That(sr.sortingOrder, Is.EqualTo(QdaoBoySpriteAnimator.WorldSortingOrder(host.transform.position, _topDownCamera)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void Resources_ContainSharedOcclusionMaterialsAndShaders()
        {
            var depthMat = Resources.Load<Material>(MapOcclusionRenderer.DepthMaskMaterialResourcePath);
            var actorMat = Resources.Load<Material>(MapOcclusionRenderer.ActorSpriteMaterialResourcePath);

            Assert.That(depthMat, Is.Not.Null, "OcclusionDepthMask.mat 必须存在于 Resources 防止构建裁剪");
            Assert.That(actorMat, Is.Not.Null, "ActorOcclusionSprite.mat 必须存在于 Resources 防止构建裁剪");
            Assert.That(depthMat.shader.name, Is.EqualTo(MapOcclusionRenderer.DepthMaskShaderName));
            Assert.That(actorMat.shader.name, Is.EqualTo(MapOcclusionRenderer.ActorSpriteShaderName));
        }

        private static MapOcclusionData CreateSampleData()
        {
            return new MapOcclusionData
            {
                schemaVersion = MapOcclusionData.CurrentSchemaVersion,
                mapKey = "test_map",
                imageWidth = 1000,
                imageHeight = 1000,
                occluders = new[]
                {
                    new MapOccluderDefinition
                    {
                        id = "diag_roof",
                        name = "斜屋檐",
                        showSilhouette = true,
                        baselinePixels = new[]
                        {
                            new MapOcclusionPoint(100, 400),
                            new MapOcclusionPoint(300, 600)
                        },
                        polygonPixels = new[]
                        {
                            new MapOcclusionPoint(100, 200),
                            new MapOcclusionPoint(300, 400),
                            new MapOcclusionPoint(300, 600),
                            new MapOcclusionPoint(100, 400)
                        }
                    },
                    new MapOccluderDefinition
                    {
                        id = "pillar_east",
                        name = "东石柱",
                        showSilhouette = true,
                        polygonPixels = new[]
                        {
                            new MapOcclusionPoint(600, 500),
                            new MapOcclusionPoint(800, 500),
                            new MapOcclusionPoint(800, 700),
                            new MapOcclusionPoint(600, 700)
                        }
                    }
                },
                fadeZones = new[]
                {
                    new MapFadeZoneDefinition
                    {
                        id = "grass_a",
                        name = "草丛A",
                        targetAlpha = 0.5f,
                        fadeDurationSec = 0.2f,
                        polygonPixels = new[]
                        {
                            new MapOcclusionPoint(100, 200),
                            new MapOcclusionPoint(300, 200),
                            new MapOcclusionPoint(300, 300),
                            new MapOcclusionPoint(100, 300)
                        }
                    },
                    new MapFadeZoneDefinition
                    {
                        id = "grass_b",
                        name = "草丛B",
                        targetAlpha = 0.3f,
                        fadeDurationSec = 0.2f,
                        polygonPixels = new[]
                        {
                            new MapOcclusionPoint(200, 200),
                            new MapOcclusionPoint(400, 200),
                            new MapOcclusionPoint(400, 300),
                            new MapOcclusionPoint(200, 300)
                        }
                    }
                }
            };
        }
    }
}
