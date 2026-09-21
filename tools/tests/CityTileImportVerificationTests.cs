using System;
using System.IO;
using MmorpgClient.Editor.Tianyong;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    // Copied only into the isolated validation project. This is not delivered city artwork.
    public sealed class CityTileImportVerificationTests
    {
        private const string PathInProject = "Assets/Resources/World/CityTiles4K/__import_test/day/tiles/r01_c01.png";
        private static readonly string[] Platforms = { "Standalone", "Android", "iPhone", "WebGL" };

        private static CityTileManifest ProductionManifest(string appearance = "penglai/day")
        {
            var manifest = new CityTileManifest {
                columns = 16, rows = 16, worldRect = new Rect(50, 0, 300, 300), tiles = new string[256]
            };
            for (int i = 0; i < 256; i++)
                manifest.tiles[i] = $"World/CityTiles4K/{appearance}/tiles/r{i / 16 + 1:00}_c{i % 16 + 1:00}";
            return manifest;
        }

        [Test]
        public void ProductionVerifierAcceptsExactCompletedContract()
        {
            Assert.DoesNotThrow(() => CityTileProductionVerification.ValidateManifest(ProductionManifest(), "penglai/day"));
        }

        [TestCase("wrong-order")]
        [TestCase("wrong-city")]
        [TestCase("wrong-grid")]
        [TestCase("unapproved-foreground")]
        public void ProductionVerifierRejectsIncorrectDelivery(string problem)
        {
            string appearance = problem == "unapproved-foreground" ? "tianyong/festival" : "penglai/day";
            var manifest = ProductionManifest(appearance);
            if (problem == "wrong-order") {
                var first = manifest.tiles[0]; manifest.tiles[0] = manifest.tiles[1]; manifest.tiles[1] = first;
            }
            if (problem == "wrong-city") manifest.tiles[0] = "World/CityTiles4K/donghai/day/tiles/r01_c01";
            if (problem == "wrong-grid") {
                manifest.columns = 8; manifest.rows = 8; Array.Resize(ref manifest.tiles, 64);
            }
            Assert.Throws<InvalidDataException>(() => CityTileProductionVerification.ValidateManifest(manifest, appearance));
        }

        [Test]
        public void EmptyProductionReportDoesNotClaimImportOrGameplayAcceptance()
        {
            var report = CityTileProductionVerification.VerifyPublished();
            Assert.That(report.publishedAppearanceCount, Is.Zero);
            Assert.That(report.passedAppearanceCount, Is.Zero);
            Assert.That(report.allPublishedImportsPassed, Is.False);
            Assert.That(report.gameplayAcceptancePassed, Is.False);
            Assert.That(report.appearances.Count, Is.EqualTo(7));
            foreach (var appearance in report.appearances)
                Assert.That(appearance.status, Is.EqualTo("not_delivered"));
        }

        [Test]
        public void Complete64KContractHas256NorthWestRowMajorTiles()
        {
            var manifest = new CityTileManifest {
                columns = 16, rows = 16, worldRect = new Rect(50, 0, 300, 300), tiles = new string[256]
            };
            for (int index = 0; index < 256; index++)
                manifest.tiles[index] = $"World/CityTiles4K/penglai/day/tiles/r{index / 16 + 1:00}_c{index % 16 + 1:00}";
            Assert.That(manifest.Validate(out var error), Is.True, error);
            Assert.That(manifest.PixelWidth, Is.EqualTo(65536));
            Assert.That(manifest.PixelHeight, Is.EqualTo(65536));
            Assert.That(manifest.TileWorldRect(0, 0), Is.EqualTo(new Rect(50, 281.25f, 18.75f, 18.75f)));
            Assert.That(manifest.TileWorldRect(15, 15), Is.EqualTo(new Rect(331.25f, 0, 18.75f, 18.75f)));
            Assert.That(manifest.VisibleRange(new Rect(136, 123, 128, 54), 1), Is.EqualTo(new RectInt(3, 5, 10, 6)));
        }

        [Test]
        public void Actual4KImportPreservesTexelsAndClearsAll2048Overrides()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathInProject));
            var source = new Texture2D(4096, 4096, TextureFormat.RGB24, false);
            try { File.WriteAllBytes(PathInProject, source.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(source); }
            AssetDatabase.ImportAsset(PathInProject, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(PathInProject);
            Verify(importer);
            // Reproduce stale/copied .meta settings before a real reimport.
            foreach (var platform in Platforms) {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = 2048;
                settings.format = TextureImporterFormat.RGBA32;
                importer.SetPlatformTextureSettings(settings);
                Assert.That(importer.GetPlatformTextureSettings(platform).overridden, Is.True);
                Assert.That(importer.GetPlatformTextureSettings(platform).maxTextureSize, Is.EqualTo(2048));
            }
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.isReadable = true;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
            importer = (TextureImporter)AssetImporter.GetAtPath(PathInProject);
            Verify(importer);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PathInProject);
            var report = new ImportReport {
                source = "Synthetic texture in isolated validation project; not delivered artwork",
                width = texture.width, height = texture.height, format = texture.format.ToString(),
                mipmapCount = texture.mipmapCount, isReadable = texture.isReadable, sRGB = importer.sRGBTexture,
                wrap = texture.wrapMode.ToString(), filter = texture.filterMode.ToString(), maxTextureSize = importer.maxTextureSize,
                cleared2048Overrides = Platforms, allChecksPassed = true
            };
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-cityTileValidationEvidence")
                    File.WriteAllText(System.IO.Path.Combine(args[i + 1], "actual-import-settings.json"), JsonUtility.ToJson(report, true));
        }

        private static void Verify(TextureImporter importer)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PathInProject);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(4096));
            Assert.That(texture.height, Is.EqualTo(4096));
            Assert.That(texture.format, Is.EqualTo(TextureFormat.RGB24));
            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(texture.isReadable, Is.False);
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default));
            Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.Texture2D));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(importer.maxTextureSize, Is.EqualTo(4096));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.GetDefaultPlatformTextureSettings().format, Is.EqualTo(TextureImporterFormat.RGB24));
            foreach (var platform in Platforms)
                Assert.That(importer.GetPlatformTextureSettings(platform).overridden, Is.False, platform);
            Assert.DoesNotThrow(() => CityTileProductionVerification.ValidateTexture(PathInProject, texture));
        }

        [Serializable]
        private sealed class ImportReport
        {
            public string source;
            public int width, height, mipmapCount, maxTextureSize;
            public string format, wrap, filter;
            public bool isReadable, sRGB, allChecksPassed;
            public string[] cleared2048Overrides;
        }
    }
}
