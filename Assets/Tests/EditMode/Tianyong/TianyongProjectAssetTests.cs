using MmorpgClient.World.CurrentMaps;
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
        public void StandardScenesAndCurrentMapSources_AreRegisteredWithoutRetiredConfig()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(scenes[0].path, Is.EqualTo(BootstrapScenePath));
            Assert.That(scenes[0].enabled, Is.True);
            Assert.That(scenes[1].path, Is.EqualTo(SandboxScenePath));
            Assert.That(scenes[1].enabled, Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxScenePath), Is.Not.Null);
            CollectionAssert.Contains(AssetDatabase.GetDependencies(BootstrapScenePath, true), AppRootPrefabPath);
            CollectionAssert.Contains(AssetDatabase.GetDependencies(SandboxScenePath, true), DebugPlayerPrefabPath);
            CollectionAssert.DoesNotContain(AssetDatabase.GetDependencies(BootstrapScenePath, true), ConfigPath);
            CollectionAssert.DoesNotContain(AssetDatabase.GetDependencies(SandboxScenePath, true), ConfigPath);
            StringAssert.DoesNotContain("6aef98b5e7df8384ca8489d3c1247c6f", System.IO.File.ReadAllText(AppRootPrefabPath));
            StringAssert.DoesNotContain("6aef98b5e7df8384ca8489d3c1247c6f", System.IO.File.ReadAllText(SandboxScenePath));
            Assert.That(AssetDatabase.LoadAssetAtPath<TianyongMapConfig>(ConfigPath), Is.Null);
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, CurrentMapCatalog.All.Select(map => map.SceneConfigId));
            foreach (var definition in CurrentMapCatalog.All)
            {
                Assert.That(Resources.Load<TextAsset>(definition.SourcePath), Is.Not.Null, definition.Key);
                Assert.That(Resources.Load<TextAsset>(definition.MaskPath), Is.Not.Null, definition.Key);
                Assert.That(definition.LoadTexture().width, Is.EqualTo(1254), definition.Key);
                Assert.That(definition.CreateNavigation().IsWalkable(definition.Spawn), Is.True, definition.Key);
            }
        }

        [Test]
        public void BootstrapAndDebugPrefabs_KeepRuntimeReferencesWithExplicitOptionalMapConfig()
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
            Assert.That(config, Is.Null, "旧资源已退役，不能把missing引用当作完整配置");
            var bootstrapSerialized = new SerializedObject(bootstrap);
            Assert.That(bootstrapSerialized.FindProperty("worldCamera").objectReferenceValue,
                Is.EqualTo(camera));
            Assert.That(bootstrapSerialized.FindProperty("directionalSun").objectReferenceValue,
                Is.EqualTo(appRoot.GetComponentsInChildren<Light>(true)
                    .Single(light => light.type == LightType.Directional)));
            Assert.That(bootstrapSerialized.FindProperty("mapConfig").objectReferenceValue,
                Is.EqualTo(config));
            Assert.That(mapRuntime.Config, Is.Null);
            Assert.That(bootstrapSerialized.FindProperty("mapConfig").objectReferenceEntityIdValue, Is.EqualTo(EntityId.None));
            var runtimeSerialized = new SerializedObject(mapRuntime);
            Assert.That(runtimeSerialized.FindProperty("config").objectReferenceEntityIdValue, Is.EqualTo(EntityId.None));
            StringAssert.DoesNotContain("6aef98b5e7df8384ca8489d3c1247c6f", System.IO.File.ReadAllText(AppRootPrefabPath));
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
        public void CurrentLayoutTextures_KeepTheirOfficialNativeImportSettings()
        {
            foreach (var definition in CurrentMapCatalog.All)
            {
                var path = "Assets/Resources/" + definition.TexturePath + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), path);
                Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.Texture2D), path);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), path);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), path);
                Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None), path);
                Assert.That(importer.mipmapEnabled, Is.False, path);
                Assert.That(importer.anisoLevel, Is.EqualTo(1), path);
                Assert.That(importer.maxTextureSize, Is.EqualTo(2048), path);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
                Assert.That(importer.isReadable, Is.False, path);
                Assert.That(definition.LoadTexture().width, Is.EqualTo(1254), path);
                Assert.That(definition.LoadTexture().height, Is.EqualTo(1254), path);
            }
        }
    }
}
