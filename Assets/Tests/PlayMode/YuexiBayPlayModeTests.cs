#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using MmorpgClient.UI;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class YuexiBayPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/World/YuexiBay.unity";
        private const string Revision = "yuexi-bay-20261115";
        private static readonly Vector3 Spawn = new(20.19375f, 0, 36.73125f);
        private static readonly Vector3 NorthDock = new(59.34375f, 0, 44.4375f);
        private static readonly Vector3 SouthDock = new(48.15f, 0, 21.88125f);
        private static readonly Vector3 NorthRoad = new(3.825f, 0, 67.275f);
        private static readonly (string Label, Vector3 Feet)[] Blocked =
        {
            ("海水", new Vector3(75.9375f, 0, 40.275f)),
            ("盐池", new Vector3(14.625f, 0, 17.2125f)),
            ("北码头船身", new Vector3(64.40625f, 0, 42.91875f)),
            ("春节台", new Vector3(29.8125f, 0, 55.06875f)),
            ("中秋花灯", new Vector3(38.30625f, 0, 19.2375f)),
        };

        [UnityTest]
        public IEnumerator SavedSceneWalksToBothDocksAndKeepsFeetWhenChangingFestival()
        {
            var previousApp = AppBootstrap.Instance;
            if (previousApp != null)
            {
                Object.Destroy(previousApp.gameObject);
                yield return null;
            }

            CurrentMapCatalog.ReloadDeliveries();
            var delivery = CurrentMapCatalog.Find(3u);
            Assert.That(delivery, Is.Not.Null);
            Assert.That(delivery.Revision, Is.EqualTo(Revision));
            Assert.That(Vector3.Distance(delivery.Spawn, Spawn), Is.LessThan(.001f));
            Assert.That(delivery.SharesNavigationWith(delivery.ForAppearance("mid-autumn")), Is.True);

            // The offline scene is deliberately absent from the production build settings.
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            var sandbox = Object.FindAnyObjectByType<TianyongSandboxBootstrap>();
            Assert.That(sandbox, Is.Not.Null);
            Assert.That(sandbox.gameObject.scene.path, Is.EqualTo(ScenePath));
            Assert.That(sandbox.DeliveredMapId, Is.EqualTo(3u));
            Assert.That(sandbox.DeliveredAppearance, Is.EqualTo("spring"));
            Assert.That(sandbox.WorldCamera, Is.Not.Null);

            // The test runner began Play before loading this scene. Exercise the same
            // runtime startup hook that runs when the saved scene directly enters Play.
            var autoSpawn = typeof(AppBootstrap).GetMethod("AutoSpawn", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(autoSpawn, Is.Not.Null);
            autoSpawn.Invoke(null, null);
            Assert.That(AppBootstrap.Instance, Is.Null);
            Assert.That(Object.FindAnyObjectByType<AppBootstrap>(), Is.Null);

            var player = sandbox.Player;
            Assert.That(player, Is.Not.Null);
            var controller = player.GetComponent<TianyongPlayerController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.Motor, Is.Not.Null);
            controller.SetScriptedInputOwner(true);
            Assert.That(HorizontalDistance(controller.FeetPosition, Spawn), Is.LessThan(.001f));
            Assert.That(controller.ActiveNavigation, Is.SameAs(sandbox.Map.Navigation));
            AssertLoadedAppearance(sandbox.Map, "spring");
            AssertGroundRay(sandbox.Map, SouthDock);
            AssertNavigation(sandbox.Map.Navigation);

            float previousScale = Time.timeScale;
            int previousCaptureRate = Time.captureFramerate;
            try
            {
                // Exercise the normal 60 Hz walk step consistently even in a fast batch run.
                Time.timeScale = 1f;
                Time.captureFramerate = 60;
                yield return WalkToDock(controller, SouthDock, "春节南码头");
                var switchFeet = controller.FeetPosition;

                var previousMap = sandbox.Map;
                Assert.That(sandbox.SelectDeliveredMap(3u, "mid-autumn"), Is.True);
                Assert.That(sandbox.DeliveredAppearance, Is.EqualTo("mid-autumn"));
                Assert.That(sandbox.Player, Is.SameAs(player));
                Assert.That(previousMap.Root == null || !previousMap.Root.activeSelf, Is.True);
                Assert.That(controller.ActiveNavigation, Is.SameAs(sandbox.Map.Navigation));
                Assert.That(Vector3.Distance(controller.FeetPosition, switchFeet), Is.LessThan(.001f));
                AssertLoadedAppearance(sandbox.Map, "mid-autumn");
                AssertGroundRay(sandbox.Map, NorthDock);
                AssertNavigation(sandbox.Map.Navigation);
                yield return WalkToDock(controller, NorthDock, "中秋北码头");
            }
            finally
            {
                Time.captureFramerate = previousCaptureRate;
                Time.timeScale = previousScale;
            }
        }

        private static IEnumerator WalkToDock(TianyongPlayerController controller, Vector3 target, string label)
        {
            Assert.That(controller.SetDestination(target), Is.True, $"{label}没有接受路径。");
            float realDeadline = Time.realtimeSinceStartup + 15f;
            float gameDeadline = Time.time + 15f;
            do
            {
                yield return null;
                var feet = controller.FeetPosition;
                Assert.That(controller.ActiveNavigation.IsWalkable(feet), Is.True, $"{label}途中脚点不可走 {feet}。");
            }
            while ((controller.PathDestination.HasValue || controller.IsMoving) &&
                   Time.realtimeSinceStartup < realDeadline && Time.time < gameDeadline);

            var arrived = controller.FeetPosition;
            Assert.That(HorizontalDistance(arrived, target), Is.LessThan(.4f), $"{label}未抵达 {target}，角色停在 {arrived}。");
            Assert.That(controller.PathDestination.HasValue || controller.IsMoving, Is.False, $"{label}抵达后尚未停步。");
        }

        private static void AssertLoadedAppearance(TianyongMapInstance map, string appearance)
        {
            var stream = map.Root.GetComponent<CurrentMapTileStreaming>();
            Assert.That(stream, Is.Not.Null);
            Assert.That(stream.Manifest.revision, Is.EqualTo(Revision));
            Assert.That(stream.Manifest.appearance, Is.EqualTo(appearance));
            Assert.That(stream.Manifest.tiles.Length, Is.EqualTo(30));
        }

        private static void AssertGroundRay(TianyongMapInstance map, Vector3 feet)
        {
            var ground = map.Root.transform.Find(TianyongMapBuilder.GroundColliderName);
            Assert.That(ground, Is.Not.Null);
            var collider = ground.GetComponent<BoxCollider>();
            Assert.That(collider, Is.Not.Null);
            Physics.SyncTransforms();
            Assert.That(collider.Raycast(new Ray(feet + Vector3.up * 5f, Vector3.down), out var hit, 10f), Is.True);
            Assert.That(hit.point.y, Is.EqualTo(-.05f).Within(.001f));
        }

        private static void AssertNavigation(TianyongNavigationGrid navigation)
        {
            AssertRoute(navigation, Spawn, NorthRoad);
            AssertRoute(navigation, Spawn, NorthDock);
            AssertRoute(navigation, NorthDock, SouthDock);
            foreach (var point in Blocked)
                Assert.That(navigation.IsWalkable(point.Feet), Is.False, point.Label);
        }

        private static void AssertRoute(TianyongNavigationGrid navigation, Vector3 start, Vector3 target)
        {
            Assert.That(navigation.IsWalkable(start) && navigation.IsWalkable(target), Is.True, $"{start} → {target}");
            var path = navigation.FindPath(start, target);
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(2), $"生产 A* 未找到 {start} → {target}");
            Assert.That(Vector3.Distance(path[0], start), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(path[^1], target), Is.LessThan(.001f));
            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];
                int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .05f));
                for (int step = 0; step <= samples; step++)
                {
                    var feet = Vector3.Lerp(from, to, (float)step / samples);
                    Assert.That(navigation.IsWalkable(feet), Is.True, $"路径跨过阻挡格 {feet}");
                }
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0;
            return Vector3.Distance(a, b);
        }
    }
}
#endif
