using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoCharacterCatalogTests
    {
        private static readonly string[] ExpectedIds =
        {
            "23_lantern_courier", "24_lu_dongbin", "25_lion_drum_guard",
            "26_osmanthus_healer", "27_ink_kite_ranger", "28_moon_rabbit_artificer",
            "29_he_xiangu", "30_han_xiangzi",
        };
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        [Test]
        public void ApprovedRoster_HasEightDistinctCharactersAndExcludesRejectedHermit()
        {
            var actual = new HashSet<string>();
            Assert.That(QdaoCharacterCatalog.All.Count, Is.EqualTo(8));
            foreach (var definition in QdaoCharacterCatalog.All)
            {
                Assert.That(actual.Add(definition.Id), Is.True, "Duplicate appearance identifier.");
                Assert.That(definition.Name, Is.Not.Empty);
                Assert.That(QdaoCharacterCatalog.Find(definition.Id), Is.SameAs(definition));
                Assert.That(definition.BaselineAppearance.FrameCount, Is.EqualTo(4));
                Assert.That(definition.BaselineAppearance.FramesPerSecond, Is.EqualTo(1f / 0.12f).Within(0.001f));
                // The V11 fallback keeps its four-pose walk contract; it reports
                // dedicated idle only when all eight idle/<DIR> textures exist
                // under its own V11 folder (published early for characters
                // whose V12 costume did not change).
                var v11IdleComplete = true;
                foreach (var direction in new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" })
                {
                    var idle = Resources.Load<Texture2D>(definition.BaselineAppearance.ResourceFolder + "/idle/" + direction);
                    if (idle == null || idle.width != 512 || idle.height != 512) v11IdleComplete = false;
                }
                Assert.That(definition.BaselineAppearance.HasDedicatedIdle, Is.EqualTo(v11IdleComplete), definition.Id);
                Assert.That(definition.BaselineAppearance.FrameCount, Is.EqualTo(4));
                Assert.That(definition.Version, Is.EqualTo(11).Or.EqualTo(12));
                Assert.That(definition.FrameCount, Is.EqualTo(definition.Version == 12 ? 8 : 4));
                Assert.That(definition.HasDedicatedIdle, Is.EqualTo(definition.Version == 12 || v11IdleComplete), definition.Id);
            }
            Assert.That(actual, Is.EquivalentTo(ExpectedIds));
            Assert.That(QdaoCharacterCatalog.Find("24_crane_hermit"), Is.Null);
            Assert.That(QdaoCharacterCatalog.Find("unknown-character"), Is.Null);
        }

        [TestCase(1u, 1u, "24_lu_dongbin")]
        [TestCase(1u, 2u, "23_lantern_courier")]
        [TestCase(2u, 1u, "30_han_xiangzi")]
        [TestCase(2u, 2u, "28_moon_rabbit_artificer")]
        [TestCase(3u, 1u, "27_ink_kite_ranger")]
        [TestCase(3u, 2u, "29_he_xiangu")]
        [TestCase(4u, 1u, "25_lion_drum_guard")]
        [TestCase(4u, 2u, "26_osmanthus_healer")]
        public void PersistedProfessionAndGender_SelectTheSameAppearance(uint profession, uint gender, string expected)
        {
            Assert.That(QdaoCharacterCatalog.ResolveRole(profession, gender), Is.EqualTo(expected));
        }

        [Test]
        public void UnknownRoleMetadata_UsesTheDocumentedSwordImmortalDefault()
        {
            Assert.That(QdaoCharacterCatalog.ResolveRole(0, 0), Is.EqualTo("24_lu_dongbin"));
            Assert.That(QdaoCharacterCatalog.ResolveRole(uint.MaxValue, uint.MaxValue), Is.EqualTo("24_lu_dongbin"));
        }

        [Test]
        public void EveryPortraitAndDirectionalStrip_LoadsWithCleanImportSettingsAndItsDeclaredGroundedFrames()
        {
            foreach (var definition in QdaoCharacterCatalog.All)
            {
                var portrait = QdaoCharacterCatalog.LoadPortrait(definition.Id);
                Assert.That(portrait, Is.Not.Null, definition.Id);
                Assert.That(portrait.texture.width, Is.EqualTo(1024), definition.Id);
                Assert.That(portrait.texture.height, Is.EqualTo(1024), definition.Id);
                var appearance = definition.ResolveAppearance();
                foreach (var direction in Directions)
                {
                    var path = $"Assets/Resources/{appearance.StripResourcePath(direction)}.png";
                    Assert.That(File.Exists(path), Is.True, path);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    Assert.That(importer, Is.Not.Null, path);
                    Assert.That(importer.mipmapEnabled, Is.False, path);
                    Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
                    Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), path);
                    Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput), path);
                    Assert.That(importer.alphaIsTransparency, Is.True, path);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    try
                    {
                        Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false), Is.True, path);
                        Assert.That(texture.width, Is.EqualTo(512 * appearance.FrameCount), path);
                        Assert.That(texture.height, Is.EqualTo(512), path);
                        var pixels = texture.GetPixels32();
                        var hashes = new HashSet<string>();
                        for (var frame = 0; frame < appearance.FrameCount; frame++)
                        {
                            var bytes = new byte[512 * 512 * 4];
                            var bottom = -1;
                            var output = 0;
                            for (var y = 0; y < 512; y++)
                            for (var x = 0; x < 512; x++)
                            {
                                var pixel = pixels[y * texture.width + frame * 512 + x];
                                bytes[output++] = pixel.r;
                                bytes[output++] = pixel.g;
                                bytes[output++] = pixel.b;
                                bytes[output++] = pixel.a;
                                if (bottom < 0 && pixel.a > 8) bottom = y;
                                if (x == 0 || x == 511 || y == 0 || y == 511)
                                    Assert.That(pixel.a, Is.Zero, $"{path} frame {frame} touches a frame edge.");
                            }
                            Assert.That(bottom, Is.InRange(39, 41), $"{path} frame {frame} has a displaced foot anchor.");
                            using var sha = SHA256.Create();
                            var frameHash = BitConverter.ToString(sha.ComputeHash(bytes));
                            hashes.Add(frameHash);
                            var splitPath = $"Assets/Resources/{definition.ResourceFolder}/walk/{direction}/{frame + 1:00}.png";
                            Assert.That(File.Exists(splitPath), Is.True, splitPath);
                            var splitImporter = AssetImporter.GetAtPath(splitPath) as TextureImporter;
                            Assert.That(splitImporter, Is.Not.Null, splitPath);
                            Assert.That(splitImporter.mipmapEnabled, Is.False, splitPath);
                            Assert.That(splitImporter.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), splitPath);
                            Assert.That(splitImporter.alphaIsTransparency, Is.True, splitPath);
                            var splitTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            try
                            {
                                Assert.That(ImageConversion.LoadImage(splitTexture, File.ReadAllBytes(splitPath), false), Is.True, splitPath);
                                Assert.That(splitTexture.width, Is.EqualTo(512), splitPath);
                                Assert.That(splitTexture.height, Is.EqualTo(512), splitPath);
                                var splitPixels = splitTexture.GetPixels32();
                                var splitBytes = new byte[splitPixels.Length * 4];
                                for (var pixelIndex = 0; pixelIndex < splitPixels.Length; pixelIndex++)
                                {
                                    var pixel = splitPixels[pixelIndex];
                                    var offset = pixelIndex * 4;
                                    splitBytes[offset] = pixel.r;
                                    splitBytes[offset + 1] = pixel.g;
                                    splitBytes[offset + 2] = pixel.b;
                                    splitBytes[offset + 3] = pixel.a;
                                }
                                Assert.That(BitConverter.ToString(sha.ComputeHash(splitBytes)), Is.EqualTo(frameHash),
                                    $"{splitPath} must match its approved pose exactly, with no crop shift or frame-order swap.");
                            }
                            finally
                            {
                                UnityEngine.Object.DestroyImmediate(splitTexture);
                            }
                        }
                        Assert.That(hashes.Count, Is.EqualTo(appearance.FrameCount), $"{path} must contain its declared number of genuine poses.");
                        var idleTexture = Resources.Load<Texture2D>(appearance.IdleResourcePath(direction));
                        Assert.That(idleTexture, Is.Not.Null, $"{definition.Id}/{direction} standing pose");
                        Assert.That(idleTexture.width, Is.EqualTo(512));
                        Assert.That(idleTexture.height, Is.EqualTo(512));
                        if (appearance.HasDedicatedIdle)
                            Assert.That(idleTexture, Is.Not.SameAs(Resources.Load<Texture2D>(appearance.FrameResourcePath(direction, 0))));
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
            }
        }
    }
}
