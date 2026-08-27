using System.Linq;
using MmorpgClient.UI;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class TianyongProjectAssetTests
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string SandboxScenePath = "Assets/Scenes/World/TianyongSandbox.unity";
        private const string AppRootPrefabPath = "Assets/Prefabs/App/AppRoot.prefab";
        private const string DebugPlayerPrefabPath =
            "Assets/Prefabs/World/Tianyong/TianyongDebugPlayer.prefab";
        private const string ConfigPath =
            "Assets/Resources/World/Tianyong/TianyongMapConfig.asset";

        private static readonly string[] TexturePaths =
        {
            "Assets/Resources/World/Tianyong/Textures/grass_ground.png",
            "Assets/Resources/World/Tianyong/Textures/stone_paving.png",
            "Assets/Resources/World/Tianyong/Textures/jade_water.png",
            "Assets/Resources/World/Tianyong/Textures/red_timber_wall.png",
            "Assets/Resources/World/Tianyong/Textures/roof_tiles_teal.png",
        };

        [Test]
        public void StandardScenesAndConfig_AreRegisteredAndComplete()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(scenes[0].path, Is.EqualTo(BootstrapScenePath));
            Assert.That(scenes[0].enabled, Is.True);
            Assert.That(scenes[1].path, Is.EqualTo(SandboxScenePath));
            Assert.That(scenes[1].enabled, Is.True);

            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxScenePath), Is.Not.Null);
            CollectionAssert.Contains(
                AssetDatabase.GetDependencies(BootstrapScenePath, true),
                AppRootPrefabPath,
                "Bootstrap scene must contain the canonical AppRoot prefab instance.");
            CollectionAssert.Contains(
                AssetDatabase.GetDependencies(SandboxScenePath, true),
                ConfigPath,
                "Sandbox scene must serialize its Tianyong map config reference.");
            CollectionAssert.Contains(
                AssetDatabase.GetDependencies(SandboxScenePath, true),
                DebugPlayerPrefabPath,
                "Sandbox scene must serialize its debug-player prefab reference.");

            var config = Resources.Load<TianyongMapConfig>(TianyongMapConfig.ResourcesPath);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.SceneConfigId, Is.EqualTo(TianyongMapDefinition.DefaultSceneConfigId));
            Assert.That(config.InitialTheme, Is.EqualTo(TianyongTheme.City));
            Assert.That(config.VisibleChunkRadius, Is.EqualTo(3));
            Assert.That(config.MoveSpeed, Is.EqualTo(9f));
            Assert.That(config.PlayerHeight, Is.EqualTo(1.8f));
            Assert.That(config.PlayerRadius, Is.EqualTo(0.38f));
            Assert.That(config.PlayerStepOffset, Is.EqualTo(0.35f));
            Assert.That(config.DebugPlayerPrefab, Is.Not.Null);
            foreach (TianyongMaterialSlot slot in System.Enum.GetValues(typeof(TianyongMaterialSlot)))
                Assert.That(config.GetBaseMaterial(slot), Is.Not.Null, $"Missing material slot {slot}.");
        }

        [Test]
        public void GeneratedPrefabs_HaveCanonicalRuntimeComponents()
        {
            var appRoot = AssetDatabase.LoadAssetAtPath<GameObject>(AppRootPrefabPath);
            Assert.That(appRoot, Is.Not.Null);
            var bootstrap = appRoot.GetComponent<AppBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            var mapRuntime = appRoot.GetComponent<TianyongMapRuntime>();
            Assert.That(mapRuntime, Is.Not.Null);
            var camera = appRoot.GetComponentInChildren<Camera>(true);
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.CompareTag("MainCamera"), Is.True);
            Assert.That(camera.GetComponent<AudioListener>(), Is.Not.Null);
            Assert.That(appRoot.GetComponentsInChildren<Light>(true)
                .Count(light => light.type == LightType.Directional), Is.EqualTo(1));
            var eventSystem = appRoot.GetComponentInChildren<EventSystem>(true);
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(eventSystem.GetComponent<StandaloneInputModule>(), Is.Not.Null);

            var config = AssetDatabase.LoadAssetAtPath<TianyongMapConfig>(ConfigPath);
            Assert.That(config, Is.Not.Null);
            var bootstrapSerialized = new SerializedObject(bootstrap);
            Assert.That(bootstrapSerialized.FindProperty("useUgui").boolValue, Is.True);
            Assert.That(bootstrapSerialized.FindProperty("worldCamera").objectReferenceValue,
                Is.EqualTo(camera));
            Assert.That(bootstrapSerialized.FindProperty("directionalSun").objectReferenceValue,
                Is.EqualTo(appRoot.GetComponentsInChildren<Light>(true)
                    .Single(light => light.type == LightType.Directional)));
            Assert.That(bootstrapSerialized.FindProperty("mapConfig").objectReferenceValue,
                Is.EqualTo(config));
            Assert.That(mapRuntime.Config, Is.EqualTo(config));
            Assert.That(mapRuntime.WorldCamera, Is.EqualTo(camera));
            Assert.That(mapRuntime.DirectionalSun,
                Is.EqualTo(bootstrapSerialized.FindProperty("directionalSun").objectReferenceValue));
            Assert.That(mapRuntime.Theme, Is.EqualTo(TianyongTheme.City));

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(DebugPlayerPrefabPath);
            Assert.That(player, Is.Not.Null);
            Assert.That(player.transform.localScale, Is.EqualTo(Vector3.one),
                "CharacterController dimensions must not be multiplied by prefab-root scale.");
            Assert.That(player.GetComponent<TianyongPlayerController>(), Is.Not.Null);
            var motor = player.GetComponent<CharacterController>();
            Assert.That(motor, Is.Not.Null);
            Assert.That(motor.center, Is.EqualTo(new Vector3(0f, 0.9f, 0f)),
                "Prefab root is the protocol feet position; the capsule centre is half-height above it.");
            var visual = player.transform.Find("Visual");
            Assert.That(visual, Is.Not.Null);
            Assert.That(visual.localPosition, Is.EqualTo(new Vector3(0f, 0.9f, 0f)));
            Assert.That(visual.GetComponent<Renderer>(), Is.Not.Null);
            Assert.That(visual.GetComponent<Collider>(), Is.Null,
                "The visual capsule must not compete with the root CharacterController.");
        }

        [Test]
        public void TianyongTextures_UseDeterministicWorldImportSettings()
        {
            foreach (var path in TexturePaths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, $"Missing TextureImporter for {path}.");
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), path);
                Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.Texture2D), path);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat), path);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), path);
                Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.ToNearest), path);
                Assert.That(importer.mipmapEnabled, Is.True, path);
                Assert.That(importer.anisoLevel, Is.EqualTo(4), path);
                Assert.That(importer.maxTextureSize, Is.EqualTo(2048), path);
                Assert.That(importer.textureCompression,
                    Is.EqualTo(TextureImporterCompression.CompressedHQ), path);
                Assert.That(importer.crunchedCompression, Is.False, path);
                Assert.That(importer.compressionQuality, Is.EqualTo(75), path);
            }
        }
    }
}
