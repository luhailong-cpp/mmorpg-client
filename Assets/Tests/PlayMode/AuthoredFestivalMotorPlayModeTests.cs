using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    using Object = UnityEngine.Object;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>真实作者网格、地图地板和角色控制器的离线运动回归，不替代联网Player验收。</summary>
    public sealed class AuthoredFestivalMotorPlayModeTests
    {
        private static readonly Vector3[] LongTargets =
        {
            new Vector3(292.5f, 0, 207.5f), new Vector3(242.5f, 0, 197.5f)
        };
        // 后端E2E选过的障碍附近原点和墙内点；实际墙面仍由Unity原生网格查询确认。
        private static readonly Vector3[] WallStarts =
        {
            new Vector3(110.921211f, 0, 9.777963f), new Vector3(103.0819257f, 0, 80.6206217f)
        };
        private static readonly Vector3[] WallTargets =
        {
            new Vector3(110.921211f, 0, 7.027963f), new Vector3(102.0295463f, 0, 78.079953f)
        };

        [UnityTest]
        public IEnumerator BothAuthoredMaps_RealMotorWalksToWallAndFollowsCornerWithoutLeavingGround()
        {
            var registry = Resources.Load<AuthoredFestivalAssets>("World/AuthoredFestival20261112/registry");
            Assert.That(registry, Is.Not.Null, "先生成并导入与共同交付绑定的运行登记。");
            var ctor = typeof(CurrentMapDefinition).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(AuthoredFestivalAssets.Map) }, null);
            Assert.That(ctor, Is.Not.Null);
            var originalCapture = Time.captureFramerate;
            var originalScale = Time.timeScale;
            Time.captureFramerate = 60;
            Time.timeScale = 1;
            try
            {
                for (int index = 0; index < 2; index++)
                {
                    uint id = index == 0 ? 26000u : 26008u;
                    AuthoredFestivalAssets.Map item = null;
                    foreach (var candidate in registry.maps)
                        if (candidate.sceneConfigId == id) item = candidate;
                    Assert.That(item, Is.Not.Null, "地图资源登记缺失：" + id);
                    var definition = (CurrentMapDefinition)ctor.Invoke(new object[] { item });
                    var parent = new GameObject("[AuthoredMotorTestMap]");
                    var player = new GameObject("[AuthoredMotorTestPlayer]");
                    var cameraObject = new GameObject("[AuthoredMotorTestCamera]");
                    var camera = cameraObject.AddComponent<Camera>();
                    TianyongMapInstance map = null;
                    try
                    {
                        map = CurrentMapBuilder.Build(parent.transform, definition);
                        var navigation = map.ActiveNavigation as AuthoredNavMeshNavigation;
                        Assert.That(navigation, Is.Not.Null);
                        navigation.Activate();
                        map.Root.SetActive(true);
                        Physics.SyncTransforms();
                        player.transform.position = item.spawn;
                        var controller = player.AddComponent<TianyongPlayerController>();
                        controller.Initialize(null, navigation, camera, null, item.spawn);
                        controller.SetScriptedInputOwner(true);
                        Assert.That(controller.Motor, Is.Not.Null);
                        Assert.That(controller.Motor.enabled, Is.True);
                        Assert.That(controller.Motor.height, Is.EqualTo(1.8f).Within(.001f));
                        Assert.That(controller.Motor.radius, Is.EqualTo(.4f).Within(.001f));
                        AssertFeet(navigation, controller.FeetPosition, id);

                        // 已在两张真实MSET上测过的墙内目标；仍独立验证当前Unity网格确实有墙。
                        var start = WallStarts[index];
                        var target = WallTargets[index];
                        Assert.That(navigation.IsWalkable(start), Is.True, $"{id} wall approach");
                        Assert.That(navigation.IsWalkable(target), Is.False, $"{id} wall target");
                        var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
                        Assert.That(NavMesh.Raycast(start, target, out var hit, filter), Is.True, $"{id} reference wall");
                        var direction = (target - start).normalized;
                        var wallDistance = Vector3.Dot(hit.position - start, direction);
                        Assert.That(wallDistance, Is.GreaterThan(.5f));
                        controller.WarpTo(start);
                        controller.SetDebugDirection(direction);
                        var previous = controller.FeetPosition;
                        var maximumDistance = 0f;
                        string firstFrameDiagnostic = "not sampled";
                        for (int frame = 0; frame < 180; frame++)
                        {
                            yield return null;
                            var feet = controller.FeetPosition;
                            if (frame == 0)
                            {
                                var speedField = typeof(TianyongPlayerController).GetField("_moveSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
                                var debugField = typeof(TianyongPlayerController).GetField("_debugDirection", BindingFlags.Instance | BindingFlags.NonPublic);
                                float speed = (float)speedField.GetValue(controller);
                                var candidate = previous + direction * (speed * Mathf.Min(Time.deltaTime, .05f));
                                var colliders = Physics.OverlapCapsule(feet + Vector3.up * .4f, feet + Vector3.up * 1.4f,
                                    .4f, ~0, QueryTriggerInteraction.Ignore);
                                var names = new List<string>();
                                foreach (var collider in colliders) names.Add(collider.name);
                                firstFrameDiagnostic = $"dt={Time.deltaTime:R} scale={Time.timeScale:R} " +
                                    $"enabled={controller.enabled} active={player.activeInHierarchy} motor={controller.Motor.enabled} " +
                                    $"grounded={controller.Motor.isGrounded} moving={controller.IsMoving} speed={speed:R} " +
                                    $"scripted={debugField.GetValue(controller)} keyboardBlocked={GameplayInputGate.IsKeyboardBlocked} " +
                                    $"before={previous} after={feet} candidate={candidate} " +
                                    $"candidateWalkable={navigation.IsWalkable(candidate)} candidateSegment={navigation.IsSegmentWalkable(previous, candidate)} " +
                                    $"motorVelocity={controller.Motor.velocity} contacts=[{string.Join(",", names)}]";
                                Debug.Log("[AuthoredMotorFirstFrame] " + id + " " + firstFrameDiagnostic);
                            }
                            AssertFeet(navigation, feet, id);
                            if ((feet - previous).sqrMagnitude > .00000001f)
                                Assert.That(navigation.IsSegmentWalkable(previous, feet), Is.True, $"{id} wall frame {frame}");
                            var travelled = Vector3.Dot(feet - start, direction);
                            maximumDistance = Mathf.Max(maximumDistance, travelled);
                            Assert.That(travelled, Is.LessThanOrEqualTo(wallDistance + .02f), $"{id} crossed the wall");
                            previous = feet;
                        }
                        controller.SetDebugDirection(Vector3.zero);
                        Assert.That(maximumDistance, Is.GreaterThan(.5f), $"{id} moved before the wall; {firstFrameDiagnostic}");
                        Assert.That(maximumDistance, Is.LessThan(wallDistance + .02f), $"{id} stopped at the wall");

                        // 原生CalculatePath独立选择短绕障路线；不得先依赖被测FindPath来选正例。
                        Assert.That(TryFindShortBend(navigation, item.spawn, LongTargets[index], filter,
                            out var bendStart, out var bendTarget), Is.True, $"{id} has no independently verified short bend");
                        controller.WarpTo(bendStart);
                        Assert.That(navigation.IsSegmentWalkable(bendStart, bendTarget), Is.False, $"{id} direct line must hit wall");
                        Assert.That(controller.ClickAt(bendTarget), Is.True, $"{id} click-to-move must accept complete detour");
                        previous = controller.FeetPosition;
                        var pathDistance = 0f;
                        for (int frame = 0; frame < 900; frame++)
                        {
                            yield return null;
                            var feet = controller.FeetPosition;
                            AssertFeet(navigation, feet, id);
                            if ((feet - previous).sqrMagnitude > .00000001f)
                                Assert.That(navigation.IsSegmentWalkable(previous, feet), Is.True, $"{id} detour frame {frame}");
                            pathDistance += Vector3.Distance(previous, feet);
                            previous = feet;
                            if (Vector3.Distance(feet, bendTarget) <= .4f && !controller.IsMoving) break;
                        }
                        Assert.That(pathDistance, Is.GreaterThan(.5f), $"{id} actually traversed detour");
                        Assert.That(Vector3.Distance(controller.FeetPosition, bendTarget), Is.LessThanOrEqualTo(.4f), $"{id} reached detour target");
                        Assert.That(controller.IsMoving, Is.False, $"{id} stopped at target");
                    }
                    finally
                    {
                        if (map?.Root != null) map.Root.SetActive(false);
                        map?.Dispose();
                        Object.Destroy(player);
                        Object.Destroy(parent);
                        Object.Destroy(cameraObject);
                    }
                    yield return null;
                }
            }
            finally
            {
                Time.captureFramerate = originalCapture;
                Time.timeScale = originalScale;
            }
        }

        private static void AssertFeet(AuthoredNavMeshNavigation navigation, Vector3 feet, uint id)
        {
            Assert.That(Mathf.Abs(feet.y), Is.LessThanOrEqualTo(.02f), $"{id} feet height");
            Assert.That(navigation.IsWalkable(feet), Is.True, $"{id} feet must stay on its mesh: {feet}");
        }

        private static bool TryFindShortBend(AuthoredNavMeshNavigation navigation, Vector3 start, Vector3 longTarget,
            NavMeshQueryFilter filter, out Vector3 first, out Vector3 last)
        {
            first = last = default;
            var longPath = new NavMeshPath();
            if (!NavMesh.CalculatePath(start, longTarget, filter, longPath) || longPath.status != NavMeshPathStatus.PathComplete)
                return false;
            var rawCorners = longPath.corners;
            for (int cornerIndex = 1; cornerIndex < rawCorners.Length - 1; cornerIndex++)
            {
                var center = rawCorners[cornerIndex];
                center.y = 0;
                foreach (float radius in new[] { 1.5f, 2.5f, 4f })
                {
                    var points = new List<Vector3>();
                    for (int i = 0; i < 24; i++)
                    {
                        float angle = i * Mathf.PI / 12;
                        var point = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                        if (navigation.IsWalkable(point)) points.Add(point);
                    }
                    for (int a = 0; a < points.Count; a++)
                    for (int b = a + 1; b < points.Count; b++)
                    {
                        if (Vector3.Distance(points[a], points[b]) < 1 || navigation.IsSegmentWalkable(points[a], points[b]))
                            continue;
                        var raw = new NavMeshPath();
                        if (!NavMesh.CalculatePath(points[a], points[b], filter, raw) ||
                            raw.status != NavMeshPathStatus.PathComplete || raw.corners.Length < 3) continue;
                        float length = 0;
                        var corners = raw.corners;
                        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
                        if (length > 12f || length <= Vector3.Distance(points[a], points[b]) + .01f) continue;
                        first = points[a]; last = points[b];
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
