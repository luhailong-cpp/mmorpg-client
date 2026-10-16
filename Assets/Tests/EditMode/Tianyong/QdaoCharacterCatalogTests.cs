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
                Assert.That(definition.Version, Is.EqualTo(11).Or.EqualTo(12).Or.EqualTo(13));
                Assert.That(definition.FrameCount, Is.EqualTo(definition.Version == 13 ? 16 : definition.Version == 12 ? 8 : 4));
                Assert.That(definition.HasDedicatedIdle, Is.EqualTo(definition.Version >= 12 || v11IdleComplete), definition.Id);
            }
            Assert.That(actual, Is.EquivalentTo(ExpectedIds));
            Assert.That(QdaoCharacterCatalog.Find("24_crane_hermit"), Is.Null);
            Assert.That(QdaoCharacterCatalog.Find("unknown-character"), Is.Null);
        }

        [TestCase(1u, 1u, "04_mountain_guardian_boy")]
        [TestCase(1u, 2u, "01_ice_sword_girl")]
        [TestCase(2u, 1u, "06_thunder_caster_boy")]
        [TestCase(2u, 2u, "20_star_formation_master_girl")]
        [TestCase(3u, 1u, "08_alchemy_prodigy_boy")]
        [TestCase(3u, 2u, "03_lotus_healer_girl")]
        [TestCase(4u, 1u, "00_reference_topright_boy")]
        [TestCase(4u, 2u, "09_bamboo_archer_girl")]
        public void PersistedProfessionAndGender_SelectTheSameAppearance(uint profession, uint gender, string expected)
        {
            Assert.That(QdaoCharacterCatalog.ResolveRole(profession, gender), Is.EqualTo(expected));
        }

        [Test]
        public void UnknownRoleMetadata_UsesTheDeliveredDefault()
        {
            Assert.That(QdaoCharacterCatalog.ResolveRole(0, 0), Is.EqualTo("00_reference_topright_boy"));
            Assert.That(QdaoCharacterCatalog.ResolveRole(uint.MaxValue, uint.MaxValue), Is.EqualTo("00_reference_topright_boy"));
        }

        [TestCase("QdaoHeadbandBoy", "00_reference_topright_boy")]
        [TestCase("23_lantern_courier", "01_ice_sword_girl")]
        [TestCase("24_lu_dongbin", "04_mountain_guardian_boy")]
        [TestCase("25_lion_drum_guard", "00_reference_topright_boy")]
        [TestCase("26_osmanthus_healer", "09_bamboo_archer_girl")]
        [TestCase("27_ink_kite_ranger", "08_alchemy_prodigy_boy")]
        [TestCase("28_moon_rabbit_artificer", "20_star_formation_master_girl")]
        [TestCase("29_he_xiangu", "03_lotus_healer_girl")]
        [TestCase("30_han_xiangzi", "06_thunder_caster_boy")]
        [TestCase("11_jade_fist_flat_top_boy", "04_mountain_guardian_boy")]
        [TestCase("12_iron_saber_flat_top_boy", "00_reference_topright_boy")]
        [TestCase("13_short_hair_wind_blade_girl", "01_ice_sword_girl")]
        [TestCase("16_golden_bell_dancer_girl", "05_celestial_musician_girl")]
        [TestCase("18_desert_sun_monk_girl", "10_crimson_spear_girl")]
        [TestCase("19_spirit_beast_tamer_boy", "06_thunder_caster_boy")]
        [TestCase("21_lidazui_hair_cook_boy", "08_alchemy_prodigy_boy")]
        [TestCase("22_lidazui_hair_waiter_saber_boy", "00_reference_topright_boy")]
        public void RetiredArtwork_MapsOnlyToAcceptedCurrentIdentities(string saved, string expected)
        {
            Assert.That(QdaoCharacterCatalog.ResolveVisualId(saved), Is.EqualTo(expected));
            // A saved visual alias overrides even a different profession/gender default.
            Assert.That(QdaoCharacterCatalog.ResolveRole(4, 2, saved), Is.EqualTo(expected));
            Assert.That(QdaoCharacterCatalog.ResolveVisualId(expected), Is.EqualTo(expected));
            Assert.That(QdaoCharacterCatalog.IsRetainedOriginal(expected), Is.True);
            Assert.That(expected, Is.Not.EqualTo("07_moon_shadow_assassin_girl"));
            Assert.That(expected, Is.Not.EqualTo("15_water_dragon_scholar_boy"));
        }

        [Test]
        public void CurrentSavedIdentitiesAndUnknownExplicitIds_AreNeverReplacedByCompatibilityDefaults()
        {
            foreach (var entry in QdaoCharacterCatalog.RetainedOriginalAll)
            {
                Assert.That(QdaoCharacterCatalog.ResolveVisualId(entry.Id), Is.EqualTo(entry.Id));
                Assert.That(QdaoCharacterCatalog.ResolveRole(4, 2, entry.Id), Is.EqualTo(entry.Id));
            }
            foreach (var id in new[] { "future_unavailable_identity", "24_crane_hermit", "24", " " })
            {
                Assert.That(QdaoCharacterCatalog.ResolveVisualId(id), Is.EqualTo(id));
                Assert.That(QdaoCharacterCatalog.ResolveRole(1, 1, id), Is.EqualTo(id));
                Assert.That(QdaoCharacterCatalog.Find(id), Is.Null);
            }
            Assert.That(QdaoCharacterCatalog.ResolveVisualId(null), Is.Null);
            Assert.That(QdaoCharacterCatalog.ResolveRole(1, 2, null), Is.EqualTo("01_ice_sword_girl"));
            Assert.That(QdaoCharacterCatalog.ResolveRole(1, 2, ""), Is.EqualTo("01_ice_sword_girl"));
        }

        // PNG pixel Y is bottom-up in Unity; alignment metadata uses top-left.
        private static Vector2 MeasureHead(Color32[] pixels, int width, int frame, int fixedRoi, out int roi)
        {
            var top = 512;
            var bottom = -1;
            for (var y = 0; y < 512; y++)
            for (var x = 0; x < 512; x++)
                if (pixels[y * width + frame * 512 + x].a > 8)
                {
                    top = Math.Min(top, 511 - y);
                    bottom = Math.Max(bottom, 511 - y);
                }
            Assert.That(bottom, Is.GreaterThanOrEqualTo(top), "Missing head silhouette");
            roi = fixedRoi > 0 ? fixedRoi : Math.Max(1, (int)((bottom - top) * .42));
            var xs = new List<int>();
            for (var y = top; y < Math.Min(512, top + roi); y++)
            for (var x = 0; x < 512; x++)
                if (pixels[(511 - y) * width + frame * 512 + x].a > 8) xs.Add(x);
            Assert.That(xs.Count, Is.GreaterThan(0));
            xs.Sort();
            var axis = (xs[(xs.Count - 1) / 2] + xs[xs.Count / 2]) / 2f;
            return new Vector2(axis, top);
        }

        [Test, Timeout(600000)]
        public void EveryPortraitAndDirectionalStrip_LoadsWithCleanImportSettingsAndItsDeclaredGroundedFrames()
        {
            foreach (var definition in QdaoCharacterCatalog.AvailableAll)
            {
                var portrait = QdaoCharacterCatalog.LoadPortrait(definition.Id);
                Assert.That(portrait, Is.Not.Null, definition.Id);
                Assert.That(portrait.texture.width, Is.EqualTo(1024), definition.Id);
                Assert.That(portrait.texture.height, Is.EqualTo(1024), definition.Id);
                var appearance = definition.ResolveAppearance();
                foreach (var direction in Directions)
                {
                    if (appearance.IsHd)
                    {
                        Assert.That(appearance.StripResourcePath(direction), Is.Null,
                            "V14 runtime contracts contain individual frames, never review strips.");
                        ValidateHdDirection(appearance, direction);
                        continue;
                    }
                    var path = $"Assets/Resources/{appearance.StripResourcePath(direction)}.png";
                    Assert.That(File.Exists(path), Is.True, path);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    Assert.That(importer, Is.Not.Null, path);
                    Assert.That(importer.maxTextureSize, Is.EqualTo(appearance.Version == 13 ? 8192 : 4096), path);
                    if (appearance.Version == 13)
                        Debug.Log($"[QdaoV13] Device max texture size={SystemInfo.maxTextureSize}; runtime uses 512px frames, 8192px strips are review-only.");
                    Assert.That(importer.mipmapEnabled, Is.False, path);
                    Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
                    Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), path);
                    Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput), path);
                    Assert.That(importer.alphaIsTransparency, Is.True, path);
                    var idleHead = Vector2.zero;
                    var idleHeadRoi = 0;
                    if (appearance.AlignmentVersion == 3)
                    {
                        var idlePath = $"Assets/Resources/{appearance.IdleResourcePath(direction)}.png";
                        var idlePixels = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        try
                        {
                            Assert.That(ImageConversion.LoadImage(idlePixels, File.ReadAllBytes(idlePath), false), Is.True);
                            idleHead = MeasureHead(idlePixels.GetPixels32(), 512, 0, 0, out idleHeadRoi);
                            Assert.That(idleHead.x, Is.EqualTo(256).Within(.5f));
                        }
                        finally { UnityEngine.Object.DestroyImmediate(idlePixels); }
                    }
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
                            if (appearance.AlignmentVersion == 2)
                                Assert.That(bottom, Is.InRange(39, 41), $"{path} frame {frame} has a displaced foot anchor.");
                            else
                            {
                                var head = MeasureHead(pixels, texture.width, frame, idleHeadRoi, out _);
                                Assert.That(head.x, Is.EqualTo(256).Within(.5f), $"{path} frame {frame} head axis");
                                Assert.That(head.y, Is.EqualTo(idleHead.y), $"{path} frame {frame} must share its idle head height");
                            }
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

        private static void ValidateHdDirection(QdaoCharacterCatalog.Appearance appearance, string direction)
        {
            var hashes = new HashSet<string>();
            for (var frame = 0; frame <= appearance.FrameCount; frame++)
            {
                var resource = frame == appearance.FrameCount
                    ? appearance.IdleResourcePath(direction) : appearance.FrameResourcePath(direction, frame);
                var geometry = appearance.GeometryForResource(resource);
                var path = "Assets/Resources/" + resource + ".png";
                Assert.That(File.Exists(path), Is.True, path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.maxTextureSize, Is.GreaterThanOrEqualTo(geometry.Width), path);
                // The existing loader creates leased Sprites from default Texture2D assets.
                // Sprite PPU/pivot are verified on the rendered sprite in PlayMode.
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), path);
                Assert.That(importer.mipmapEnabled, Is.False, path);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), path);
                Assert.That(importer.alphaIsTransparency, Is.True, path);
                Assert.That(geometry.Height / geometry.PixelsPerUnit, Is.EqualTo(512f / 52f).Within(.0001f));
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false), Is.True, path);
                    Assert.That(texture.width, Is.EqualTo(geometry.Width), path);
                    Assert.That(texture.height, Is.EqualTo(geometry.Height), path);
                    var pixels = texture.GetPixels32();
                    for (var x = 0; x < texture.width; x++)
                    {
                        Assert.That(pixels[x].a, Is.Zero, path + " bottom edge");
                        Assert.That(pixels[(texture.height - 1) * texture.width + x].a, Is.Zero, path + " top edge");
                    }
                    for (var y = 0; y < texture.height; y++)
                    {
                        Assert.That(pixels[y * texture.width].a, Is.Zero, path + " left edge");
                        Assert.That(pixels[y * texture.width + texture.width - 1].a, Is.Zero, path + " right edge");
                    }
                    if (frame < appearance.FrameCount)
                    {
                        using var sha = SHA256.Create();
                        Assert.That(hashes.Add(BitConverter.ToString(sha.ComputeHash(texture.GetRawTextureData()))),
                            Is.True, path + " duplicates another decoded walk pose");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
            Assert.That(hashes.Count, Is.EqualTo(appearance.FrameCount));
        }
    }
}
