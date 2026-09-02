using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoRunAssetTests
    {
        private const string CharacterFolder =
            "Assets/Resources/World/Characters/QdaoHeadbandBoy";
        private const int FrameCount = 8;
        private const int FrameSize = 512;
        private const int UpperBodyHeight = 384;
        private static readonly string[] Directions =
            { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        [Test]
        public void DirectionalRunStrips_HaveDistinctUpperBodyPosesAndGroundedFeet()
        {
            foreach (var direction in Directions)
            {
                var path = $"{CharacterFolder}/walk_{direction}.png";
                Assert.That(File.Exists(path), Is.True, path);

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false),
                        Is.True, path);
                    Assert.That(texture.width, Is.EqualTo(FrameSize * FrameCount), path);
                    Assert.That(texture.height, Is.EqualTo(FrameSize), path);

                    var stripPixels = texture.GetPixels32();
                    var upperBodyHashes = new HashSet<string>();
                    for (var frame = 0; frame < FrameCount; frame++)
                    {
                        var pixels = ExtractFrame(stripPixels, texture.width, frame);
                        var firstOpaqueRow = FirstOpaqueRow(pixels);
                        Assert.That(firstOpaqueRow, Is.InRange(38, 42),
                            $"{path} frame {frame} must keep its feet on pivot y=0.08.");
                        Assert.That(OpaqueCoverage(pixels), Is.LessThan(0.35f),
                            $"{path} frame {frame} contains scene/occluder-sized pixel coverage.");
                        upperBodyHashes.Add(HashUpperBody(pixels));
                    }

                    Assert.That(upperBodyHashes.Count, Is.EqualTo(FrameCount),
                        $"{path} repeats a frozen upper-body pose inside its run cycle.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private static Color32[] ExtractFrame(
            IReadOnlyList<Color32> stripPixels,
            int stripWidth,
            int frame)
        {
            var pixels = new Color32[FrameSize * FrameSize];
            for (var y = 0; y < FrameSize; y++)
            {
                var source = y * stripWidth + frame * FrameSize;
                var destination = y * FrameSize;
                for (var x = 0; x < FrameSize; x++)
                    pixels[destination + x] = stripPixels[source + x];
            }
            return pixels;
        }

        private static int FirstOpaqueRow(IReadOnlyList<Color32> pixels)
        {
            for (var y = 0; y < FrameSize; y++)
            {
                for (var x = 0; x < FrameSize; x++)
                {
                    if (pixels[y * FrameSize + x].a > 8) return y;
                }
            }
            return -1;
        }

        private static float OpaqueCoverage(IReadOnlyList<Color32> pixels)
        {
            var opaque = 0;
            foreach (var pixel in pixels)
                if (pixel.a > 8) opaque++;
            return opaque / (float)pixels.Count;
        }

        private static string HashUpperBody(IReadOnlyList<Color32> pixels)
        {
            var firstRow = FrameSize - UpperBodyHeight;
            var bytes = new byte[FrameSize * UpperBodyHeight * 4];
            var output = 0;
            for (var y = firstRow; y < FrameSize; y++)
            {
                for (var x = 0; x < FrameSize; x++)
                {
                    var pixel = pixels[y * FrameSize + x];
                    bytes[output++] = pixel.r;
                    bytes[output++] = pixel.g;
                    bytes[output++] = pixel.b;
                    bytes[output++] = pixel.a;
                }
            }

            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes));
        }
    }
}
