using System.IO;
using MmorpgClient.Editor.CurrentMaps;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>隔离工程合成测试夹具，绝不作为交付美术。</summary>
    public sealed class CurrentMapTileImportVerificationTests
    {
        private const string Fixture = "Assets/Resources/World/CurrentMaps20261009/main/tiles/r01_c01.png";

        [TestCase(Fixture, true)]
        [TestCase("Assets/Resources/World/CurrentMaps20261009/island/tiles/r14_c14.png", true)]
        [TestCase("Assets/Resources/World/CurrentMaps20261009/village/tiles/r01_c01.png", true)]
        [TestCase("Assets/Resources/World/CurrentMaps20261009/main/layout.png", false)]
        [TestCase("Assets/Resources/World/CurrentMaps20261009/old/tiles/r01_c01.png", false)]
        [TestCase("Assets/Resources/World/CityTiles4K/main/tiles/r01_c01.png", false)]
        public void ImporterOnlyChangesCurrentNativeTilePaths(string path, bool expected)
            => Assert.That(CurrentMapTileImporter.IsTilePath(path), Is.EqualTo(expected));

        [Test]
        public void ImportedNativeTileKeeps4096RgbAndClearsStaleOverrides()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Fixture));
            var source = new Texture2D(4096, 4096, TextureFormat.RGB24, false);
            try { File.WriteAllBytes(Fixture, source.EncodeToPNG()); }
            finally { Object.DestroyImmediate(source); }
            try
            {
                AssetDatabase.ImportAsset(Fixture, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(Fixture);
                foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                {
                    var settings = importer.GetPlatformTextureSettings(platform);
                    settings.overridden = true;
                    settings.maxTextureSize = 2048;
                    settings.format = TextureImporterFormat.RGBA32;
                    importer.SetPlatformTextureSettings(settings);
                }
                importer.maxTextureSize = 2048;
                importer.isReadable = true;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Fixture);
                Assert.That(texture.width, Is.EqualTo(4096));
                Assert.That(texture.height, Is.EqualTo(4096));
                Assert.That(texture.format, Is.EqualTo(TextureFormat.RGB24));
                Assert.That(texture.mipmapCount, Is.EqualTo(1));
                Assert.That(texture.isReadable, Is.False);
                Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
                Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
                Assert.That(importer.sRGBTexture, Is.True);
                foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                    Assert.That(importer.GetPlatformTextureSettings(platform).overridden, Is.False);
            }
            finally { AssetDatabase.DeleteAsset(Fixture); }
        }
    }
}
