using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MmorpgClient.Editor.Tianyong
{
    /// <summary>Checks actual production imports sequentially; does not publish or certify gameplay.</summary>
    public static class CityTileProductionVerification
    {
        private static readonly string[] Appearances =
        {
            "tianyong/festival", "penglai/day", "penglai/festival", "donghai/day",
            "donghai/festival", "lanxian/day", "lanxian/festival"
        };

        [Serializable] public sealed class TileProof
        {
            public string resourcePath, pngSha256, format;
            public int width, height, mipmapCount;
            public bool passed;
            public string error;
        }

        [Serializable] public sealed class AppearanceProof
        {
            public string appearance, manifestSha256, status, error;
            public List<TileProof> tiles = new();
        }

        [Serializable] public sealed class Report
        {
            public string verifiedAtUtc, unityVersion, buildTarget, projectPath;
            public int publishedAppearanceCount, passedAppearanceCount;
            public bool allPublishedImportsPassed, gameplayAcceptancePassed;
            public List<AppearanceProof> appearances = new();
            public string[] limitations =
            {
                "Import verification only. Source acceptance is checked by publish_city_tiles.py.",
                "No gameplay screenshots, navigation/foreground acceptance or device performance measurement.",
                "Missing appearances remain unpublished; candidate review resources are excluded."
            };
        }

        // Public so the isolated importer regression uses exactly the production check.
        public static void ValidateTexture(string assetPath, Texture2D texture)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (texture == null || texture.width != 4096 || texture.height != 4096 ||
                texture.format != TextureFormat.RGB24 || texture.mipmapCount != 1 || texture.isReadable ||
                texture.wrapMode != TextureWrapMode.Clamp || texture.filterMode != FilterMode.Bilinear ||
                importer == null || importer.textureType != TextureImporterType.Default ||
                importer.textureShape != TextureImporterShape.Texture2D || importer.maxTextureSize != 4096 ||
                importer.npotScale != TextureImporterNPOTScale.None || importer.isReadable ||
                importer.mipmapEnabled || !importer.sRGBTexture || importer.crunchedCompression ||
                importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != FilterMode.Bilinear ||
                importer.alphaSource != TextureImporterAlphaSource.None ||
                importer.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidDataException("Expected actual 4096 RGB24 sRGB Clamp/Bilinear, unreadable, no mipmaps: " + assetPath);
            var defaults = importer.GetDefaultPlatformTextureSettings();
            if (defaults.maxTextureSize != 4096 || defaults.format != TextureImporterFormat.RGB24 ||
                defaults.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidDataException("Default platform import settings differ: " + assetPath);
            foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                if (importer.GetPlatformTextureSettings(platform).overridden)
                    throw new InvalidDataException("Unexpected platform override (" + platform + "): " + assetPath);
        }

        public static void ValidateManifest(CityTileManifest manifest, string appearance)
        {
            if (Array.IndexOf(Appearances, appearance) < 0)
                throw new InvalidDataException("Unknown city/variant: " + appearance);
            if (manifest == null) throw new InvalidDataException("Null manifest.");
            if (!manifest.Validate(out string error)) throw new InvalidDataException(error);
            if (manifest.columns != 16 || manifest.rows != 16 ||
                manifest.WorldRect != TianyongPaintedCity.PaintingWorldRect)
                throw new InvalidDataException("Production delivery requires the existing world rectangle and 16 x 16 tiles.");
            if (appearance == "tianyong/festival" && !manifest.legacyForegroundCompatible)
                throw new InvalidDataException("Tianyong foreground approval is missing.");
            for (int i = 0; i < 256; i++)
            {
                string expected = $"{CityTileManifest.ResourceRoot}{appearance}/tiles/r{i / 16 + 1:00}_c{i % 16 + 1:00}";
                if (manifest.tiles[i] != expected)
                    throw new InvalidDataException($"Incorrect row-major Resources path at index {i}: expected {expected}");
            }
        }

        /// <summary>Unity -batchmode -executeMethod MmorpgClient.Editor.Tianyong.CityTileProductionVerification.VerifyBatch</summary>
        public static void VerifyBatch()
        {
            string output = Argument("-cityTileEvidence") ?? Path.Combine("Docs", "VerificationEvidence",
                "city-tiles4k-import-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            int code = 1;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Stop Play Mode before running the sequential import audit.");
                // Do not unload textures referenced by any open scene. Run in a fresh batch editor.
                if (!Application.isBatchMode)
                    throw new InvalidOperationException("Run this audit in a separate batch editor with the project closed; see Docs/CityTiles4K.md.");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var report = VerifyPublished();
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "production-imports.json"), JsonUtility.ToJson(report, true));
                code = report.publishedAppearanceCount == 0 ? 2 : report.allPublishedImportsPassed ? 0 : 1;
                Debug.Log($"CITY_TILE_PRODUCTION_IMPORTS published={report.publishedAppearanceCount} passed={report.passedAppearanceCount} exit={code} evidence={Path.GetFullPath(output)}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "verification-error.txt"), exception.ToString());
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static Report VerifyPublished()
        {
            var report = new Report
            {
                verifiedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                gameplayAcceptancePassed = false
            };
            foreach (string appearance in Appearances)
            {
                var proof = new AppearanceProof { appearance = appearance, status = "not_delivered" };
                report.appearances.Add(proof);
                string path = "Assets/Resources/" + CityTileManifest.ResourceRoot + appearance + "/manifest.json";
                if (!File.Exists(path)) continue;
                report.publishedAppearanceCount++;
                try
                {
                    proof.manifestSha256 = Hash(path);
                    var manifest = JsonUtility.FromJson<CityTileManifest>(File.ReadAllText(path));
                    ValidateManifest(manifest, appearance);
                    string tileDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "tiles"));
                    int pngCount = 0;
                    foreach (string file in Directory.GetFiles(tileDirectory, "*", SearchOption.AllDirectories))
                    {
                        if (file.EndsWith(".meta", StringComparison.Ordinal)) continue;
                        if (Path.GetDirectoryName(file) != tileDirectory || !file.EndsWith(".png", StringComparison.Ordinal))
                            throw new InvalidDataException("Unexpected file in production tiles: " + file);
                        pngCount++;
                    }
                    if (pngCount != 256) throw new InvalidDataException("Production tiles directory must contain exactly 256 PNGs.");
                    bool valid = true;
                    foreach (string resourcePath in manifest.tiles)
                    {
                        var tile = new TileProof { resourcePath = resourcePath };
                        proof.tiles.Add(tile);
                        Texture2D texture = null;
                        try
                        {
                            string assetPath = "Assets/Resources/" + resourcePath + ".png";
                            tile.pngSha256 = Hash(assetPath);
                            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                            if (texture != null)
                            {
                                tile.width = texture.width; tile.height = texture.height;
                                tile.format = texture.format.ToString(); tile.mipmapCount = texture.mipmapCount;
                            }
                            ValidateTexture(assetPath, texture);
                            tile.passed = true;
                        }
                        catch (Exception exception) { tile.error = exception.Message; valid = false; }
                        finally { if (texture != null) Resources.UnloadAsset(texture); }
                    }
                    proof.status = valid ? "imports_passed_gameplay_pending" : "failed";
                    if (valid) report.passedAppearanceCount++;
                }
                catch (Exception exception) { proof.status = "failed"; proof.error = exception.Message; }
            }
            report.allPublishedImportsPassed = report.publishedAppearanceCount > 0 &&
                report.publishedAppearanceCount == report.passedAppearanceCount;
            return report;
        }

        private static string Argument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++) if (arguments[i] == name) return arguments[i + 1];
            return null;
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
