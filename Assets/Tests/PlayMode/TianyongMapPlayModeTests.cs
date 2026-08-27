using System.Collections;
using System.Linq;
using MmorpgClient.UI;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class TianyongMapPlayModeTests
    {
        private const int MaximumLanternPointLights = 32;
        private TianyongSandboxBootstrap _sandbox;

        [UnitySetUp]
        public IEnumerator LoadSandbox()
        {
            var app = Object.FindAnyObjectByType<AppBootstrap>();
            if (app != null)
            {
                Object.Destroy(app.gameObject);
                yield return null;
            }

            var load = SceneManager.LoadSceneAsync("TianyongSandbox", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "TianyongSandbox must be registered in Build Settings.");
            yield return load;
            yield return null;
            yield return null;

            _sandbox = Object.FindAnyObjectByType<TianyongSandboxBootstrap>();
            Assert.That(_sandbox, Is.Not.Null);
            Assert.That(_sandbox.Map, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator SandboxScene_BuildsAndWalksAcrossAllThreePhysicalBridges()
        {
            // The painted City theme walks on the artwork's pavement mask and
            // disables the procedural canal/bridge colliders; the 3D bridge
            // physics this test covers are only authoritative in 3D themes.
            _sandbox.SetTheme(TianyongTheme.Market);
            yield return null;
            yield return null;

            Assert.That(_sandbox.Map.Chunks.Count, Is.EqualTo(48));
            Assert.That(_sandbox.WorldCamera, Is.Not.Null);
            Assert.That(_sandbox.WorldCamera.orthographic, Is.True);
            Assert.That(_sandbox.DirectionalLight, Is.Not.Null);
            Assert.That(_sandbox.DirectionalLight.type, Is.EqualTo(LightType.Directional));
            Assert.That(_sandbox.Player, Is.Not.Null);

            var bridgeDecks = _sandbox.Map.Root.GetComponentsInChildren<Collider>(true)
                .Where(collider => collider.name == "StoneBridge")
                .ToArray();
            Assert.That(bridgeDecks, Has.Length.EqualTo(3),
                "The rendered map must contain three collider-backed bridge decks.");

            var controller = _sandbox.Player.GetComponent<TianyongPlayerController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.Motor, Is.Not.Null);
            Assert.That(Vector3.Distance(
                _sandbox.Player.transform.position,
                new Vector3(200f, _sandbox.Player.transform.position.y, 150f)),
                Is.GreaterThan(10f), "Spawn must not overlap the central fountain.");

            for (var bridgeIndex = 0; bridgeIndex < TianyongMapDefinition.Bridges.Length; bridgeIndex++)
            {
                var bridge = TianyongMapDefinition.Bridges[bridgeIndex];
                Assert.That(bridgeDecks.Any(deck =>
                        deck.bounds.Contains(new Vector3(bridge.center.x, deck.bounds.center.y, bridge.center.y))),
                    Is.True,
                    $"Bridge {bridgeIndex} has no collider covering its authored centre.");

                var start = new Vector3(bridge.center.x, 0f, TianyongMapDefinition.Canal.yMin - 4f);
                var target = new Vector3(bridge.center.x, 0f, TianyongMapDefinition.Canal.yMax + 4f);
                controller.WarpTo(start);
                Assert.That(controller.SetDestination(target), Is.True,
                    $"Navigation could not create a route over bridge {bridgeIndex}.");

                var enteredCanal = false;
                var deadline = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < deadline &&
                       _sandbox.Player.transform.position.z < target.z - 1f)
                {
                    var position = _sandbox.Player.transform.position;
                    if (position.z >= TianyongMapDefinition.Canal.yMin &&
                        position.z <= TianyongMapDefinition.Canal.yMax)
                    {
                        enteredCanal = true;
                        Assert.That(position.x,
                            Is.InRange(bridge.xMin - 0.5f, bridge.xMax + 0.5f),
                            $"Player left bridge {bridgeIndex} while physically crossing the canal.");
                    }
                    yield return null;
                }

                Assert.That(enteredCanal, Is.True,
                    $"CharacterController never entered the canal span on bridge {bridgeIndex}.");
                Assert.That(_sandbox.Player.transform.position.z, Is.GreaterThanOrEqualTo(target.z - 1f),
                    $"CharacterController could not physically cross bridge {bridgeIndex} at x={bridge.center.x}.");
            }
        }

        [UnityTest]
        public IEnumerator ThemeSwitch_RebuildsAllThemesAndCapsRealTimeLights()
        {
            var themes = new[]
            {
                TianyongTheme.City,
                TianyongTheme.Market,
                TianyongTheme.Snow,
                TianyongTheme.Lantern,
                TianyongTheme.City,
            };

            foreach (var theme in themes)
            {
                var previousRoot = _sandbox.Map.Root;
                _sandbox.SetTheme(theme);
                yield return null;
                yield return null;

                Assert.That(_sandbox.Map.Theme, Is.EqualTo(theme));
                Assert.That(_sandbox.Map.Root, Is.Not.SameAs(previousRoot));
                Assert.That(previousRoot == null, Is.True,
                    $"Old {theme} map root was not destroyed after the rebuild.");

                var pointLights = _sandbox.Map.Root.GetComponentsInChildren<Light>(true)
                    .Where(light => light.type == LightType.Point)
                    .ToArray();
                if (theme == TianyongTheme.Lantern)
                {
                    Assert.That(pointLights.Length, Is.GreaterThan(0));
                    Assert.That(pointLights.Length, Is.LessThanOrEqualTo(MaximumLanternPointLights),
                        "Lantern mode must use emissive materials beyond the real-time light budget.");
                    Assert.That(_sandbox.DirectionalLight.intensity, Is.EqualTo(0.55f).Within(0.01f));
                }
                else
                {
                    Assert.That(pointLights, Is.Empty,
                        $"Theme {theme} must not retain Lantern point lights.");
                    Assert.That(_sandbox.DirectionalLight.intensity, Is.EqualTo(1.15f).Within(0.01f));
                }
            }

            Assert.That(Object.FindObjectsByType<TianyongSandboxBootstrap>().Length, Is.EqualTo(1));
        }
    }
}
