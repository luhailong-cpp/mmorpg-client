using System.IO;
using System.Linq;
using MmorpgClient.UI.Ugui;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MmorpgClient.UI.EditorTools
{
    /// <summary>
    /// Reproducible import, prefab and screenshot pipeline for the first uGUI page.
    /// It is callable from the Unity menu or from -executeMethod in batch mode.
    /// </summary>
    public static class QdaoUguiBuilder
    {
        private const string NativeArtFolder = "Assets/Resources/UI/Ugui/Native";
        private const string ScreenArtAssetPath = NativeArtFolder + "/screen_art_headband.png";
        private const string StatusDotAssetPath = NativeArtFolder + "/status_dot_mask.png";
        private const string CredentialCancelAssetPath = NativeArtFolder + "/credential_btn_cancel.png";
        private const string CredentialSubmitAssetPath = NativeArtFolder + "/credential_btn_submit.png";
        private static readonly string[] ArtAssetPaths =
        {
            ScreenArtAssetPath,
            StatusDotAssetPath,
            NativeArtFolder + "/credential_panel_v2.png",
            CredentialCancelAssetPath,
            CredentialSubmitAssetPath,
        };
        private const string FontSourcePath = "Assets/Resources/Fonts/SimKai.ttf";
        private const string FontAssetPath = "Assets/Resources/Fonts/SimKai SDF.asset";
        private const string TmpEssentialsPackagePath =
            "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        private const string PrefabFolder = "Assets/Resources/UI/Ugui/Prefabs";
        private const string PrefabPath = PrefabFolder + "/QdaoServerSelect.prefab";
        private const string PrefabResourcePath = "UI/Ugui/Prefabs/QdaoServerSelect";
        private const string BootstrapSceneFolder = "Assets/Scenes";
        private const string BootstrapScenePath = BootstrapSceneFolder + "/Bootstrap.unity";

        [MenuItem("MMORPG/UI/Build qdao uGUI vertical slice")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Build the qdao uGUI prefab outside Play Mode.");

            EnsureGeneratedStatusDot();
            EnsureGeneratedCredentialButtons();
            ConfigureArtTextures();
            EnsureTmpEssentialResources();
            EnsureFontAsset();
            QdaoUguiTheme.ResetRuntimeCaches();
            EnsureFolder(PrefabFolder);
            BuildPrefab();
            EnsureBootstrapScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log($"[QdaoUguiBuilder] Built {PrefabPath}");
        }

        [MenuItem("MMORPG/UI/Build and capture qdao uGUI")]
        public static void BuildAndCapture()
        {
            BuildAll();
            CapturePreview();
        }

        [MenuItem("MMORPG/UI/Open Bootstrap scene for testing")]
        public static void OpenBootstrapForTesting()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Exit Play Mode before opening the Bootstrap scene.");

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath);
            if (sceneAsset == null)
                throw new FileNotFoundException(
                    "Build the qdao uGUI vertical slice before opening its Bootstrap scene.",
                    BootstrapScenePath);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
            Selection.activeObject = sceneAsset;
            Debug.Log($"[QdaoUguiBuilder] Ready for Play Mode: {BootstrapScenePath}");
        }

        private static void ConfigureArtTextures()
        {
            foreach (var assetPath in ArtAssetPaths)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                    throw new FileNotFoundException("Native qdao art was not imported", assetPath);

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.alphaIsTransparency = assetPath != ScreenArtAssetPath;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// Produces a neutral, anti-aliased white mask. Runtime code applies
        /// semantic status colors through Image.color, so one small sprite can
        /// represent smooth/busy/full/maintenance without generated textures.
        /// </summary>
        private static void EnsureGeneratedStatusDot()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = "status_dot_mask",
            };

            try
            {
                var pixels = new Color32[size * size];
                var center = (size - 1f) * 0.5f;
                // Fill the sprite rect. The Image rect is measured against the
                // painted lamp core in the screen art, so hidden texture padding
                // would shift the semantic color away from the metal bezel.
                var radius = size * 0.47f;
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var coverage = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(
                        byte.MaxValue,
                        byte.MaxValue,
                        byte.MaxValue,
                        (byte)Mathf.RoundToInt(coverage * byte.MaxValue));
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                var png = texture.EncodeToPNG();
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                  ?? throw new DirectoryNotFoundException("Unity project root is missing.");
                var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, StatusDotAssetPath));
                if (!File.Exists(absolutePath) || !File.ReadAllBytes(absolutePath).SequenceEqual(png))
                    File.WriteAllBytes(absolutePath, png);
                AssetDatabase.ImportAsset(StatusDotAssetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Bakes the two login-dialog buttons as anti-aliased rounded rects in
        /// the reference palette (paper "取消", jade "登录", gilt border), so the
        /// dialog chrome matches the painted screen art without hand-made assets.
        /// </summary>
        private static void EnsureGeneratedCredentialButtons()
        {
            WriteRoundedRectSprite(
                CredentialCancelAssetPath,
                fill: new Color32(0xF6, 0xEC, 0xD9, 0xFF),
                border: new Color32(0x8F, 0x6B, 0x3A, 0xFF));
            WriteRoundedRectSprite(
                CredentialSubmitAssetPath,
                fill: new Color32(0x2F, 0x75, 0x66, 0xFF),
                border: new Color32(0xC6, 0xA1, 0x5B, 0xFF));
        }

        private static void WriteRoundedRectSprite(string assetPath, Color32 fill, Color32 border)
        {
            // 2x the on-screen 190x56 rect for crisp bilinear downscale.
            const int width = 380, height = 112;
            const float cornerRadius = 26f, borderWidth = 6f;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = Path.GetFileNameWithoutExtension(assetPath),
            };
            try
            {
                var pixels = new Color32[width * height];
                var halfW = width * 0.5f - 1.5f;
                var halfH = height * 0.5f - 1.5f;
                var fillColor = (Color)fill;
                var borderColor = (Color)border;
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    // Signed distance to the rounded rect edge (negative inside).
                    var qx = Mathf.Abs(x + 0.5f - width * 0.5f) - (halfW - cornerRadius);
                    var qy = Mathf.Abs(y + 0.5f - height * 0.5f) - (halfH - cornerRadius);
                    var outside = Mathf.Sqrt(
                        Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                        Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    var dist = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - cornerRadius;

                    var coverage = Mathf.Clamp01(0.5f - dist);
                    var borderMix = Mathf.Clamp01(dist + borderWidth + 0.5f);
                    var rgb = Color.Lerp(fillColor, borderColor, borderMix);
                    pixels[y * width + x] = new Color32(
                        (byte)Mathf.RoundToInt(rgb.r * 255f),
                        (byte)Mathf.RoundToInt(rgb.g * 255f),
                        (byte)Mathf.RoundToInt(rgb.b * 255f),
                        (byte)Mathf.RoundToInt(coverage * 255f));
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                var png = texture.EncodeToPNG();
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                  ?? throw new DirectoryNotFoundException("Unity project root is missing.");
                var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
                if (!File.Exists(absolutePath) || !File.ReadAllBytes(absolutePath).SequenceEqual(png))
                    File.WriteAllBytes(absolutePath, png);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static void EnsureFontAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath) != null)
                return;

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (sourceFont == null)
                throw new FileNotFoundException("SimKai font source is missing", FontSourcePath);

            var fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
            fontAsset.name = "SimKai SDF";
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
            {
                fontAsset.material.name = "SimKai SDF Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            foreach (var atlas in fontAsset.atlasTextures)
            {
                if (atlas != null && !AssetDatabase.Contains(atlas))
                {
                    atlas.name = "SimKai SDF Atlas";
                    AssetDatabase.AddObjectToAsset(atlas, fontAsset);
                }
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureTmpEssentialResources()
        {
            var hasSettings = AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;
            var hasSdfShader = Shader.Find("TextMeshPro/Mobile/Distance Field") != null;
            if (hasSettings && hasSdfShader)
                return;

            var packagePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", TmpEssentialsPackagePath));
            if (!File.Exists(packagePath))
                throw new FileNotFoundException("TMP Essential Resources package is missing", packagePath);

            AssetDatabase.ImportPackage(packagePath, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            hasSettings = AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;
            hasSdfShader = Shader.Find("TextMeshPro/Mobile/Distance Field") != null;
            if (!hasSettings || !hasSdfShader)
                throw new MissingReferenceException("TMP Essential Resources failed to import.");
        }

        private static void BuildPrefab()
        {
            var template = QdaoServerSelectView.CreateTemplate(null);
            try
            {
                template.PrepareForPreview();
                var prefab = PrefabUtility.SaveAsPrefabAsset(template.gameObject, PrefabPath, out var success);
                if (!success || prefab == null)
                    throw new IOException($"Failed to save native qdao prefab: {PrefabPath}");

                var images = template.GetComponentsInChildren<Image>(true).Length;
                var buttons = template.GetComponentsInChildren<Button>(true).Length;
                var texts = template.GetComponentsInChildren<TMP_Text>(true).Length;
                var retiredReference = template.transform.Find("ScreenBaseHeadband") != null ||
                                       template.transform.Find("ReferenceArtwork") != null;
                if (retiredReference)
                    throw new InvalidDataException("An editor-only reference object returned to the runtime hierarchy.");

                var screenArtwork = template.GetComponentsInChildren<Image>(true)
                    .SingleOrDefault(image => image.name == "ScreenArtwork");
                if (screenArtwork == null || screenArtwork.sprite == null || screenArtwork.raycastTarget)
                    throw new InvalidDataException(
                        "ScreenArtwork must be a serialized, non-interactive production art layer.");
                var artRect = screenArtwork.rectTransform;
                if (artRect.sizeDelta != new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight))
                    throw new InvalidDataException("ScreenArtwork must retain the 2560x1080 design size.");

                Debug.Log($"[QdaoUguiBuilder] PREFAB_PATH={PrefabPath} IMAGES={images} BUTTONS={buttons} TMP={texts} SCREEN_ART=1");
            }
            finally
            {
                Object.DestroyImmediate(template.gameObject);
            }
        }

        private static void CapturePreview()
        {
            var prefab = Resources.Load<GameObject>(PrefabResourcePath);
            if (prefab == null)
                throw new FileNotFoundException(
                    $"uGUI prefab is missing from Resources/{PrefabResourcePath}.prefab",
                    PrefabPath);

            GameObject cameraObject = null;
            GameObject canvasObject = null;
            RenderTexture target = null;
            Texture2D capture = null;
            var previousRenderTexture = RenderTexture.active;
            try
            {
                cameraObject = new GameObject("QdaoCaptureCamera", typeof(Camera));
                var camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.orthographic = true;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                camera.transform.position = new UnityEngine.Vector3(0f, 0f, -10f);

                target = new RenderTexture(
                    (int)QdaoUguiTheme.DesignWidth,
                    (int)QdaoUguiTheme.DesignHeight,
                    24,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                target.name = "QdaoUguiCapture";
                camera.targetTexture = target;

                canvasObject = new GameObject(
                    "QdaoCaptureCanvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                canvas.pixelPerfect = true;

                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(
                    QdaoUguiTheme.DesignWidth,
                    QdaoUguiTheme.DesignHeight);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasObject.transform);
                var view = instance.GetComponent<QdaoServerSelectView>();
                view.PrepareForPreview();

                capture = new Texture2D(
                    target.width,
                    target.height,
                    TextureFormat.RGB24,
                    false,
                    false);
                var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "image"));
                Directory.CreateDirectory(outputDirectory);

                void Shoot(string fileName)
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (var text in instance.GetComponentsInChildren<TMP_Text>(true))
                        text.ForceMeshUpdate(true, true);
                    Canvas.ForceUpdateCanvases();
                    camera.Render();

                    RenderTexture.active = target;
                    capture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
                    capture.Apply(false, false);
                    var outputPath = Path.Combine(outputDirectory, fileName);
                    File.WriteAllBytes(outputPath, capture.EncodeToPNG());
                    Debug.Log($"[QdaoUguiBuilder] Capture written: {outputPath}");
                }

                Shoot("ugui_qdao_headband_native_2560x1080.png");
                view.ShowCredentialForPreview();
                Shoot("ugui_qdao_credential_login_2560x1080.png");
            }
            finally
            {
                RenderTexture.active = previousRenderTexture;
                if (capture != null)
                    Object.DestroyImmediate(capture);
                if (target != null)
                {
                    if (cameraObject != null)
                        cameraObject.GetComponent<Camera>().targetTexture = null;
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                if (canvasObject != null)
                    Object.DestroyImmediate(canvasObject);
                if (cameraObject != null)
                    Object.DestroyImmediate(cameraObject);
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void EnsureBootstrapScene()
        {
            EnsureFolder(BootstrapSceneFolder);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath) == null)
            {
                var replaceUntitledScene = CanReplaceOnlyUntitledScene();
                if (!replaceUntitledScene && HasUntitledScene())
                {
                    throw new System.InvalidOperationException(
                        "Save or close the untitled scene before building the qdao UI. " +
                        "The builder will not discard unsaved scene contents.");
                }

                var mode = replaceUntitledScene ? NewSceneMode.Single : NewSceneMode.Additive;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
                if (!EditorSceneManager.SaveScene(scene, BootstrapScenePath))
                    throw new IOException($"Failed to save bootstrap scene: {BootstrapScenePath}");
                if (mode == NewSceneMode.Additive)
                    EditorSceneManager.CloseScene(scene, true);
            }

            var existing = EditorBuildSettings.scenes
                .Where(scene => !string.Equals(scene.path, BootstrapScenePath, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            existing.Insert(0, new EditorBuildSettingsScene(BootstrapScenePath, true));
            EditorBuildSettings.scenes = existing.ToArray();
        }

        private static bool CanReplaceOnlyUntitledScene()
        {
            if (SceneManager.sceneCount != 1)
                return false;

            var scene = SceneManager.GetSceneAt(0);
            return scene.IsValid() && string.IsNullOrEmpty(scene.path) &&
                   !scene.isDirty && scene.rootCount == 0;
        }

        private static bool HasUntitledScene()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    return true;
            }

            return false;
        }
    }
}
