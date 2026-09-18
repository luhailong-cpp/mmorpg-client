using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using UVector3 = UnityEngine.Vector3;

namespace MmorpgClient.Editor.Tianyong
{
    /// <summary>Editor-only review of incomplete candidates. Never publishes a production manifest.</summary>
    public static class CityTileCandidateReview
    {
        public const string Root = "Assets/Editor/CityTiles4KReview";
        [Serializable] public sealed class Catalog
        {
            public int schemaVersion, tilePixels, wholeCityPixels, rows, columns, acceptedDeliveryTileCount;
            public bool runtimePublished;
            public string purpose, sourceLedgerSha256;
            public string[] appearances;
            public Candidate[] candidates;
        }
        [Serializable] public sealed class Candidate
        {
            public string appearance, tile, assetPath, sourceSha256;
            public int row, column;
            public float x, z, size;
        }
        [Serializable] private sealed class Proof
        {
            public string unityVersion, catalogSha256, sourceLedgerSha256, verifiedAtUtc;
            public bool importChecksPassed, productionManifestPublished;
            public int candidateCount, appearanceCount;
            public List<string> renderedFiles = new();
            public List<string> importedAssets = new();
            public string[] limitations = { "Local candidates only; complete cities remain unfinished.", "No production scene replacement or navigation/foreground/device acceptance.", "Captures verify editor rendering, not all seams or runtime streaming performance." };
        }

        [MenuItem("Tools/主城高清候选检查/天墉节庆")]
        private static void Tianyong() => Open("tianyong_festival");
        [MenuItem("Tools/主城高清候选检查/蓬莱日景")]
        private static void PenglaiDay() => Open("penglai_day");
        [MenuItem("Tools/主城高清候选检查/蓬莱中秋")]
        private static void PenglaiFestival() => Open("penglai_mid_autumn");
        [MenuItem("Tools/主城高清候选检查/东海日景")]
        private static void DonghaiDay() => Open("donghai_day");
        [MenuItem("Tools/主城高清候选检查/东海元宵")]
        private static void DonghaiFestival() => Open("donghai_lantern");
        [MenuItem("Tools/主城高清候选检查/揽仙日景")]
        private static void LanxianDay() => Open("lanxian_day");
        [MenuItem("Tools/主城高清候选检查/揽仙春景")]
        private static void LanxianSpring() => Open("lanxian_spring");

        private static Catalog LoadCatalog()
        {
            if (!File.Exists(Root + "/catalog.json")) throw new InvalidOperationException("Run tools/stage_city_tile_candidates.py first.");
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Root + "/catalog.json"));
            if (catalog == null || catalog.schemaVersion != 1 || catalog.purpose != "local_candidate_editor_review_only" ||
                catalog.tilePixels != 4096 || catalog.wholeCityPixels != 65536 || catalog.rows != 16 || catalog.columns != 16 ||
                catalog.runtimePublished || catalog.acceptedDeliveryTileCount != 0 || catalog.candidates == null)
                throw new InvalidDataException("Expected an incomplete, editor-only 64K candidate catalog.");
            var coordinates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in catalog.candidates)
            {
                if (c.row < 1 || c.row > 16 || c.column < 1 || c.column > 16 ||
                    c.tile != $"r{c.row:00}_c{c.column:00}" || !catalog.appearances.Contains(c.appearance) ||
                    !coordinates.Add(c.appearance + "/" + c.tile) || !c.assetPath.StartsWith(Root + "/Tiles/", StringComparison.Ordinal) ||
                    c.assetPath.Contains("..") || c.assetPath.Contains("\\")) throw new InvalidDataException("Invalid review candidate.");
                if (Mathf.Abs(c.x - (50 + (c.column - 1) * 18.75f)) > .001f ||
                    Mathf.Abs(c.z - (300 - c.row * 18.75f)) > .001f || Mathf.Abs(c.size - 18.75f) > .001f)
                    throw new InvalidDataException("Candidate world coordinates disagree with the 16 x 16 map.");
            }
            return catalog;
        }

        private static Texture2D LoadTexture(Candidate c)
        {
            if (Hash(c.assetPath) != c.sourceSha256) throw new InvalidDataException("Staged bytes changed: " + c.assetPath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(c.assetPath);
            var importer = AssetImporter.GetAtPath(c.assetPath) as TextureImporter;
            if (texture == null || texture.width != 4096 || texture.height != 4096 || texture.format != TextureFormat.RGB24 ||
                importer == null || importer.maxTextureSize != 4096 || importer.mipmapEnabled || importer.isReadable ||
                importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != FilterMode.Bilinear ||
                !importer.sRGBTexture || importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.npotScale != TextureImporterNPOTScale.None)
                throw new InvalidDataException("4K import settings failed: " + c.assetPath);
            foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                if (importer.GetPlatformTextureSettings(platform).overridden) throw new InvalidDataException("Platform override: " + c.assetPath);
            return texture;
        }

        private static Scene Build(string appearance, Catalog catalog, bool preview, out Camera camera, out Rect bounds, out List<Material> temporary)
        {
            var items = catalog.candidates.Where(c => c.appearance == appearance).OrderBy(c => c.row).ThenBy(c => c.column).ToArray();
            if (items.Length == 0) throw new InvalidDataException("No reviewed candidates for " + appearance);
            var textures = items.Select(LoadTexture).ToArray();
            var scene = preview ? EditorSceneManager.NewPreviewScene() : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            temporary = new List<Material>();
            var group = new GameObject($"{appearance} — 局部候选 {items.Length}/256 · 未正式验收");
            SceneManager.MoveGameObjectToScene(group, scene);
            var manifest = new CityTileManifest { rows = 16, columns = 16, worldRect = new Rect(50, 0, 300, 300) };
            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) throw new InvalidOperationException("Unlit/Texture shader is unavailable.");
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var rect = manifest.TileWorldRect(item.row - 1, item.column - 1);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                SceneManager.MoveGameObjectToScene(quad, scene);
                quad.name = item.tile; quad.transform.SetParent(group.transform, false);
                quad.transform.position = new UVector3(rect.center.x, .02f, rect.center.y);
                quad.transform.rotation = Quaternion.Euler(90, 0, 0);
                quad.transform.localScale = new UVector3(rect.width, rect.height, 1);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                var material = new Material(shader) { name = appearance + "_" + item.tile, mainTexture = textures[i] };
                if (preview) temporary.Add(material);
                else
                {
                    string folder = Root + "/Materials/" + appearance;
                    Directory.CreateDirectory(folder);
                    string path = folder + "/" + item.tile + "_" + item.sourceSha256.Substring(0, 16) + ".mat";
                    var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (existing != null) { Object.DestroyImmediate(material); material = existing; }
                    else AssetDatabase.CreateAsset(material, path);
                }
                var renderer = quad.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
            bounds = Rect.MinMaxRect(items.Min(c => c.x), items.Min(c => c.z), items.Max(c => c.x + c.size), items.Max(c => c.z + c.size));
            var cameraObject = new GameObject("候选检查相机 — 正交俯视");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .1f, .12f); camera.allowHDR = false; camera.allowMSAA = false;
            camera.nearClipPlane = .1f; camera.farClipPlane = 200; camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            camera.transform.position = new UVector3(bounds.center.x, 100, bounds.center.y);
            camera.orthographicSize = Mathf.Max(bounds.height / 2, bounds.width / 2 / (16f / 9f)) * 1.04f;
            return scene;
        }

        private static void Open(string appearance)
        {
            var catalog = LoadCatalog();
            string scenePath = Root + "/Scenes/" + appearance + ".unity";
            // Rebuild from the latest catalog and isolate SceneView from other appearances.
            // The standard editor save prompt protects unsaved work before switching scenes.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = Build(appearance, catalog, false, out var camera, out var bounds, out _);
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Could not save candidate scene.");
            SceneManager.SetActiveScene(scene);
            Selection.activeGameObject = camera.gameObject;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new UVector3(bounds.center.x, .02f, bounds.center.y), Quaternion.Euler(90, 0, 0), Mathf.Max(bounds.width, bounds.height) * .6f, true);
            Debug.Log($"已打开 {appearance} 的局部候选检查场景；全城、导航和前景尚未验收。当前生产地图未替换。");
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path); using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void Capture(Camera camera, string path, int width, int height, Vector2 center, float worldHeight)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D output = null;
            try
            {
                camera.transform.position = new UVector3(center.x, 100, center.y);
                camera.aspect = (float)width / height; camera.orthographicSize = worldHeight / 2;
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                output = new Texture2D(width, height, TextureFormat.RGB24, false);
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0); output.Apply();
                File.WriteAllBytes(path, output.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                if (output != null) Object.DestroyImmediate(output);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        /// <summary>Unity -batchmode -executeMethod MmorpgClient.Editor.Tianyong.CityTileCandidateReview.VerifyBatch</summary>
        public static void VerifyBatch()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var catalog = LoadCatalog();
                string evidence = "Docs/VerificationEvidence/city-candidate-review-20260918";
                Directory.CreateDirectory(evidence);
                var proof = new Proof { unityVersion = Application.unityVersion, catalogSha256 = Hash(Root + "/catalog.json"), sourceLedgerSha256 = catalog.sourceLedgerSha256,
                    verifiedAtUtc = DateTime.UtcNow.ToString("O"), candidateCount = catalog.candidates.Length, appearanceCount = catalog.appearances.Length,
                    importChecksPassed = true, productionManifestPublished = false };
                foreach (string appearance in catalog.appearances)
                {
                    var items = catalog.candidates.Where(c => c.appearance == appearance).OrderBy(c => c.row).ThenBy(c => c.column).ToArray();
                    var scene = Build(appearance, catalog, true, out var camera, out var bounds, out var materials);
                    try
                    {
                        var overview = evidence + "/" + appearance + "_overview.png";
                        Capture(camera, overview, 1536, 864, bounds.center, Mathf.Max(bounds.height, bounds.width * 864f / 1536) * 1.04f);
                        proof.renderedFiles.Add(overview);
                        var last = items[items.Length - 1];
                        var detail = evidence + "/" + appearance + "_native1024.png";
                        Capture(camera, detail, 1024, 1024, new Vector2(last.x + last.size / 2, last.z + last.size / 2), 1024 * last.size / 4096);
                        proof.renderedFiles.Add(detail);
                        var adjacent = items.FirstOrDefault(c => c.row == last.row && c.column == last.column - 1);
                        if (adjacent != null)
                        {
                            var seam = evidence + "/" + appearance + "_boundary_native1024.png";
                            Capture(camera, seam, 1024, 1024, new Vector2(last.x, last.z + last.size / 2), 1024 * last.size / 4096);
                            proof.renderedFiles.Add(seam);
                        }
                        proof.importedAssets.AddRange(items.Select(c => c.assetPath));
                    }
                    finally
                    {
                        EditorSceneManager.ClosePreviewScene(scene);
                        foreach (var material in materials) Object.DestroyImmediate(material);
                        foreach (var item in items)
                        {
                            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(item.assetPath);
                            if (texture != null) Resources.UnloadAsset(texture);
                        }
                    }
                }
                File.WriteAllText(evidence + "/summary.json", JsonUtility.ToJson(proof, true));
                Debug.Log($"CITY_CANDIDATE_REVIEW_OK candidates={proof.candidateCount} screenshots={proof.renderedFiles.Count}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }
    }
}



