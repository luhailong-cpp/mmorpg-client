using System.Collections;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class QdaoRosterSandboxPlayModeTests
    {
        [UnityTest]
        public IEnumerator RealCitySandbox_SwitchesAllEightAppearancesWithoutReplacingThePlayer_AndWalksWithTheRealMotor()
        {
            var previousCapture = Time.captureFramerate;
            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbient = RenderSettings.ambientLight;
            Time.captureFramerate = 60;
            var root = new GameObject("RosterCitySandboxTest");
            var cameraObject = new GameObject("RosterCityCamera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            var lightObject = new GameObject("RosterCitySun");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.AddComponent<Light>().type = LightType.Directional;
            var sandbox = root.AddComponent<TianyongSandboxBootstrap>();
            try
            {
                sandbox.SetHelpVisible(false);
                sandbox.BuildSandbox();
                var player = sandbox.Player;
                Assert.That(player, Is.Not.Null);
                var map = sandbox.Map;
                var controller = player.GetComponent<TianyongPlayerController>();
                var animator = player.GetComponent<QdaoBoySpriteAnimator>();
                var motor = controller.Motor;
                controller.SetScriptedInputOwner(true);
                Assert.That(motor, Is.Not.Null);
                Assert.That(motor.enabled, Is.True);
                var height = motor.height;
                var radius = motor.radius;
                var centre = motor.center;
                yield return null;
                yield return null;

                foreach (var definition in QdaoCharacterCatalog.All)
                {
                    var position = player.transform.position;
                    Assert.That(sandbox.SelectCharacter(definition.Id), Is.True);
                    Assert.That(player.transform.position, Is.EqualTo(position), "Changing art must not warp the player.");
                    yield return null;
                    Assert.That(sandbox.Player, Is.SameAs(player));
                    Assert.That(sandbox.Map, Is.SameAs(map), "Appearance switching must not rebuild map/navigation.");
                    Assert.That(player.GetComponent<TianyongPlayerController>(), Is.SameAs(controller));
                    Assert.That(controller.Motor, Is.SameAs(motor));
                    Assert.That(motor.enabled, Is.True);
                    Assert.That(motor.height, Is.EqualTo(height));
                    Assert.That(motor.radius, Is.EqualTo(radius));
                    Assert.That(motor.center, Is.EqualTo(centre));
                    Assert.That(animator.CharacterId, Is.EqualTo(definition.Id));
                    Assert.That(animator.FrameCount, Is.EqualTo(definition.FrameCount));
                    Assert.That(animator.ArtworkVersion, Is.EqualTo(definition.Version));
                    Assert.That(player.GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo(definition.Name));
                    if (definition.Id == "24_lu_dongbin" || definition.Id == "29_he_xiangu")
                        CaptureIfRequested(sandbox, definition.Id);
                }
                Assert.That(sandbox.SelectCharacter("24_crane_hermit"), Is.False);

                Assert.That(sandbox.SelectCharacter("24_lu_dongbin"), Is.True);
                var start = controller.FeetPosition;
                var direction = FindOpenDirection(map.Navigation, start);
                Assert.That(direction, Is.Not.EqualTo(Vector3.zero), "The city spawn must expose a short walkable test route.");
                var renderer = player.transform.Find("sprite").GetComponent<SpriteRenderer>();
                var poses = new HashSet<Sprite>();
                controller.SetDebugDirection(direction);
                for (var frame = 0; frame < 32; frame++)
                {
                    yield return null;
                    if (animator.State == QdaoBoySpriteAnimator.LocomotionState.Run) poses.Add(renderer.sprite);
                }
                controller.SetDebugDirection(Vector3.zero);
                var travel = controller.FeetPosition - start;
                travel.y = 0f;
                Assert.That(travel.magnitude, Is.GreaterThan(3f), "The real CharacterController must travel across the city pavement.");
                Assert.That(poses.Count, Is.EqualTo(QdaoCharacterCatalog.Find("24_lu_dongbin").FrameCount), "Real controller movement must animate every separately imported pose.");
                for (var frame = 0; frame < 48; frame++)
                {
                    yield return null;
                    if (animator.State == QdaoBoySpriteAnimator.LocomotionState.Idle) break;
                }
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                Assert.That(controller.Motor, Is.SameAs(motor));
                Debug.Log($"[QdaoRosterCity] eight appearances switched; real motor travelled {travel.magnitude:0.00} world units; {poses.Count} poses observed; stopped idle. Offline sandbox, no online login asserted.");
            }
            finally
            {
                Time.captureFramerate = previousCapture;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbient;
                Object.Destroy(root);
                Object.Destroy(cameraObject);
            }
            yield return null;
        }

        private static Vector3 FindOpenDirection(TianyongNavigationGrid navigation, Vector3 start)
        {
            for (var sector = 0; sector < 8; sector++)
            {
                var radians = sector * 45f * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                var side = new Vector3(direction.z, 0f, -direction.x) * 0.5f;
                var clear = true;
                for (var step = 1; step <= 24; step++)
                {
                    var sample = start + direction * (step * 0.25f);
                    if (navigation.IsWalkable(sample) && navigation.IsWalkable(sample + side) && navigation.IsWalkable(sample - side)) continue;
                    clear = false;
                    break;
                }
                if (clear) return direction;
            }
            return Vector3.zero;
        }

        private static void CaptureIfRequested(TianyongSandboxBootstrap sandbox, string characterId)
        {
            var outputDirectory = System.Environment.GetEnvironmentVariable("QDAO_ROSTER_CAPTURE_DIR");
            if (string.IsNullOrEmpty(outputDirectory) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Directory.CreateDirectory(outputDirectory);
            var camera = sandbox.WorldCamera;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24);
            var capture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                sandbox.CameraRig.Snap();
                camera.Render();
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                capture.Apply();
                var path = Path.Combine(outputDirectory, "tianyong-" + characterId + ".png");
                File.WriteAllBytes(path, capture.EncodeToPNG());
                Assert.That(new FileInfo(path).Length, Is.GreaterThan(100000), "The real map capture should contain visible city artwork.");
                Debug.Log("[QdaoRosterCityCapture] " + path + " (offline Tianyong sandbox)");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(capture);
            }
        }
    }
}
