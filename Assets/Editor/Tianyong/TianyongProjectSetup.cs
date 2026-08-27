using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.UI;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace MmorpgClient.Editor.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public static class TianyongProjectSetup
    {
        public const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        public const string SandboxScenePath = "Assets/Scenes/World/TianyongSandbox.unity";
        public const string AppRootPrefabPath = "Assets/Prefabs/App/AppRoot.prefab";
        public const string DebugPlayerPrefabPath = "Assets/Prefabs/World/Tianyong/TianyongDebugPlayer.prefab";
        public const string ConfigPath = "Assets/Resources/World/Tianyong/TianyongMapConfig.asset";

        private const string MaterialFolder = "Assets/Art/World/Tianyong/Materials";
        private static readonly string[] TexturePaths =
        {
            "Assets/Resources/World/Tianyong/Textures/grass_ground.png",
            "Assets/Resources/World/Tianyong/Textures/stone_paving.png",
            "Assets/Resources/World/Tianyong/Textures/jade_water.png",
            "Assets/Resources/World/Tianyong/Textures/red_timber_wall.png",
            "Assets/Resources/World/Tianyong/Textures/roof_tiles_teal.png",
        };

        [MenuItem("MMORPG/World/Tianyong/Rebuild standard test content")]
        public static void BuildAll()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder("Assets/Scenes/World");
            EnsureFolder("Assets/Prefabs/App");
            EnsureFolder("Assets/Prefabs/World/Tianyong");
            EnsureFolder(MaterialFolder);

            ConfigureTextureImporters();
            var materials = CreateMaterials();
            var playerPrefab = CreateDebugPlayerPrefab(materials["Neutral"]);
            var config = CreateConfig(materials, playerPrefab);
            AssetDatabase.SaveAssetIfDirty(config);
            var appRootPrefab = CreateAppRootPrefab(config);

            CreateBootstrapScene(appRootPrefab);
            CreateSandboxScene(config, playerPrefab);
            RegisterBuildScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[TianyongProjectSetup] Ready: {BootstrapScenePath} + {SandboxScenePath}");
        }

        [MenuItem("MMORPG/World/Tianyong/Open sandbox")]
        public static void OpenSandbox()
        {
            BuildAll();
            EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Single);
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new System.InvalidOperationException(
                    "Neither the built-in Standard shader nor URP/Lit is available.");

            return new Dictionary<string, Material>
            {
                ["Grass"] = UpsertMaterial("Grass", shader, "Assets/Resources/World/Tianyong/Textures/grass_ground.png"),
                ["Stone"] = UpsertMaterial("Stone", shader, "Assets/Resources/World/Tianyong/Textures/stone_paving.png"),
                ["Water"] = UpsertMaterial("Water", shader, "Assets/Resources/World/Tianyong/Textures/jade_water.png", 0.65f),
                ["Timber"] = UpsertMaterial("Timber", shader, "Assets/Resources/World/Tianyong/Textures/red_timber_wall.png", 0.18f),
                ["Roof"] = UpsertMaterial("Roof", shader, "Assets/Resources/World/Tianyong/Textures/roof_tiles_teal.png", 0.35f),
                ["Neutral"] = UpsertMaterial("Neutral", shader, null, 0.12f),
            };
        }

        private static void ConfigureTextureImporters()
        {
            foreach (var texturePath in TexturePaths)
            {
                var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (importer == null)
                    throw new FileNotFoundException("Missing or unimported Tianyong texture", texturePath);

                var changed = importer.textureType != TextureImporterType.Default ||
                              importer.textureShape != TextureImporterShape.Texture2D ||
                              importer.wrapMode != TextureWrapMode.Repeat ||
                              importer.filterMode != FilterMode.Trilinear ||
                              importer.npotScale != TextureImporterNPOTScale.ToNearest ||
                              importer.anisoLevel != 4 ||
                              !importer.mipmapEnabled ||
                              importer.alphaSource != TextureImporterAlphaSource.None ||
                              !importer.sRGBTexture ||
                              importer.textureCompression != TextureImporterCompression.CompressedHQ ||
                              importer.maxTextureSize != 2048 ||
                              importer.crunchedCompression ||
                              importer.compressionQuality != 75;
                if (!changed) continue;

                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.npotScale = TextureImporterNPOTScale.ToNearest;
                importer.anisoLevel = 4;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = 2048;
                importer.crunchedCompression = false;
                importer.compressionQuality = 75;
                importer.SaveAndReimport();
            }
        }

        private static Material UpsertMaterial(string name, Shader shader, string texturePath, float smoothness = 0.08f)
        {
            var path = $"{MaterialFolder}/Tianyong_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "Tianyong_" + name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.color = Color.white;
            material.mainTexture = string.IsNullOrEmpty(texturePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (!string.IsNullOrEmpty(texturePath) && material.mainTexture == null)
                throw new FileNotFoundException("Missing Tianyong texture", texturePath);
            material.mainTextureScale = new Vector2(3f, 3f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreateDebugPlayerPrefab(Material material)
        {
            var root = new GameObject("TianyongDebugPlayer");
            root.name = "TianyongDebugPlayer";
            var motor = root.AddComponent<CharacterController>();
            motor.height = 1.8f;
            motor.radius = 0.38f;
            motor.center = new Vector3(0f, 0.9f, 0f);
            motor.stepOffset = 0.35f;
            motor.slopeLimit = 45f;
            root.AddComponent<TianyongPlayerController>();

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.76f, 0.9f, 0.76f);
            visual.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, DebugPlayerPrefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null)
                throw new System.InvalidOperationException($"Could not save {DebugPlayerPrefabPath}.");
            return prefab;
        }

        private static TianyongMapConfig CreateConfig(
            IReadOnlyDictionary<string, Material> materials,
            GameObject playerPrefab)
        {
            var config = AssetDatabase.LoadAssetAtPath<TianyongMapConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<TianyongMapConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            var serialized = new SerializedObject(config);
            serialized.FindProperty("sceneConfigId").uintValue = TianyongMapDefinition.DefaultSceneConfigId;
            serialized.FindProperty("initialTheme").enumValueIndex = (int)TianyongTheme.City;
            serialized.FindProperty("visibleChunkRadius").intValue = 3;
            serialized.FindProperty("moveSpeed").floatValue = 9f; // ~5 body heights per second; 6 felt too sluggish in playtests
            serialized.FindProperty("playerHeight").floatValue = 1.8f;
            serialized.FindProperty("playerRadius").floatValue = 0.38f;
            serialized.FindProperty("playerStepOffset").floatValue = 0.35f;
            serialized.FindProperty("debugPlayerPrefab").objectReferenceValue = playerPrefab;
            serialized.FindProperty("grassMaterial").objectReferenceValue = materials["Grass"];
            serialized.FindProperty("stoneMaterial").objectReferenceValue = materials["Stone"];
            serialized.FindProperty("waterMaterial").objectReferenceValue = materials["Water"];
            serialized.FindProperty("timberMaterial").objectReferenceValue = materials["Timber"];
            serialized.FindProperty("roofMaterial").objectReferenceValue = materials["Roof"];
            serialized.FindProperty("neutralMaterial").objectReferenceValue = materials["Neutral"];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return config;
        }

        private static GameObject CreateAppRootPrefab(TianyongMapConfig config)
        {
            var root = new GameObject(AppBootstrap.GameObjectName);
            var bootstrap = root.AddComponent<AppBootstrap>();
            var mapRuntime = root.AddComponent<TianyongMapRuntime>();

            var cameraGo = new GameObject("[MainCamera]");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(root.transform, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.20f, 0.23f, 0.19f);
            cameraGo.AddComponent<AudioListener>();

            var sunGo = new GameObject("[Sun]");
            sunGo.transform.SetParent(root.transform, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sunGo.transform.rotation = Quaternion.Euler(40f, 30f, 0f);

            var eventSystemGo = new GameObject("[EventSystem]");
            eventSystemGo.transform.SetParent(root.transform, false);
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();

            var bootstrapSerialized = new SerializedObject(bootstrap);
            bootstrapSerialized.FindProperty("useUgui").boolValue = true;
            bootstrapSerialized.FindProperty("worldCamera").objectReferenceValue = camera;
            bootstrapSerialized.FindProperty("directionalSun").objectReferenceValue = sun;
            bootstrapSerialized.FindProperty("mapConfig").objectReferenceValue = config;
            bootstrapSerialized.ApplyModifiedPropertiesWithoutUndo();

            var runtimeSerialized = new SerializedObject(mapRuntime);
            runtimeSerialized.FindProperty("config").objectReferenceValue = config;
            runtimeSerialized.FindProperty("worldCamera").objectReferenceValue = camera;
            runtimeSerialized.FindProperty("directionalSun").objectReferenceValue = sun;
            runtimeSerialized.FindProperty("initialTheme").enumValueIndex = (int)TianyongTheme.City;
            runtimeSerialized.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, AppRootPrefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null)
                throw new System.InvalidOperationException($"Could not save {AppRootPrefabPath}.");
            return prefab;
        }

        private static void CreateBootstrapScene(GameObject appRootPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = PrefabUtility.InstantiatePrefab(appRootPrefab, scene) as GameObject;
            if (instance == null)
                throw new System.InvalidOperationException("Could not instantiate the AppRoot prefab.");
            if (!EditorSceneManager.SaveScene(scene, BootstrapScenePath))
                throw new IOException($"Could not save {BootstrapScenePath}.");
        }

        private static void CreateSandboxScene(TianyongMapConfig config, GameObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            config = AssetDatabase.LoadAssetAtPath<TianyongMapConfig>(ConfigPath);
            if (config == null)
                throw new FileNotFoundException("Could not reload the persistent Tianyong map config", ConfigPath);
            var root = new GameObject("[TianyongSandbox]");
            SceneManager.MoveGameObjectToScene(root, scene);

            var cameraGo = new GameObject("[MainCamera]");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(root.transform, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.35f, 0.72f, 0.92f);
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            cameraGo.AddComponent<AudioListener>();

            var sunGo = new GameObject("[TianyongSun]");
            sunGo.transform.SetParent(root.transform, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;

            var bootstrap = root.AddComponent<TianyongSandboxBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.FindProperty("worldCamera").objectReferenceValue = camera;
            serialized.FindProperty("directionalLight").objectReferenceValue = sun;
            serialized.FindProperty("debugPlayerPrefab").objectReferenceValue = playerPrefab;
            serialized.FindProperty("initialTheme").enumValueIndex = (int)TianyongTheme.City;
            serialized.FindProperty("showHelp").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorSceneManager.SaveScene(scene, SandboxScenePath))
                throw new IOException($"Could not save {SandboxScenePath}.");
        }

        private static void RegisterBuildScenes()
        {
            var ordered = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true),
                new EditorBuildSettingsScene(SandboxScenePath, true),
            };
            ordered.AddRange(EditorBuildSettings.scenes.Where(scene =>
                scene.path != BootstrapScenePath && scene.path != SandboxScenePath));
            EditorBuildSettings.scenes = ordered.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
