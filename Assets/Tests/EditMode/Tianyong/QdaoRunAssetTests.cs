using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoRunAssetTests
    {
        private const string CharacterId = "00_reference_topright_boy";
        private const int FrameCount = 16;
        private const int FrameSize = 1024;
        private const int UpperBodyHeight = 768;
        private static readonly string[] Directions =
            { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        [Test]
        public void CurrentDirectionalRunFrames_HaveDistinctUpperBodyPosesAndTheDeliveredRootPivot()
        {
            foreach (var direction in Directions)
            {
                using var run = QdaoActionResources.Acquire(CharacterId, "run", direction);
                Assert.That(run, Is.Not.Null, direction);
                Assert.That(run.Frames.Length, Is.EqualTo(FrameCount));
                Assert.That(run.Pivot.x, Is.EqualTo(0.5f));
                Assert.That(run.Pivot.y, Is.EqualTo(0.080078125f));
                var upperBodyHashes = new HashSet<string>();
                for (var frame = 0; frame < FrameCount; frame++)
                {
                    var path = $"Assets/Resources/{QdaoActionResources.ResourceRoot}/{CharacterId}/run/{direction}/{frame + 1:00}.png";
                    Assert.That(File.Exists(path), Is.True, path);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    try
                    {
                        Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false), Is.True, path);
                        Assert.That(texture.width, Is.EqualTo(FrameSize), path);
                        Assert.That(texture.height, Is.EqualTo(FrameSize), path);
                        var pixels = texture.GetPixels32();
                        Assert.That(FirstOpaqueRow(pixels), Is.GreaterThan(0), path);
                        var edgeAlpha = 0;
                        for (var coordinate = 0; coordinate < FrameSize; coordinate++)
                        {
                            edgeAlpha = Math.Max(edgeAlpha, pixels[coordinate].a);
                            edgeAlpha = Math.Max(edgeAlpha, pixels[(FrameSize - 1) * FrameSize + coordinate].a);
                            edgeAlpha = Math.Max(edgeAlpha, pixels[coordinate * FrameSize].a);
                            edgeAlpha = Math.Max(edgeAlpha, pixels[coordinate * FrameSize + FrameSize - 1].a);
                        }
                        Assert.That(edgeAlpha, Is.LessThanOrEqualTo(1),
                            path + " must have no clipped silhouette; the delivered SW frames retain only alpha-1 edge residue.");
                        var sprite = run.Frames[frame];
                        Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(FrameSize, FrameSize)));
                        Assert.That(sprite.pixelsPerUnit, Is.EqualTo(104f));
                        Assert.That(sprite.pivot, Is.EqualTo(new Vector2(512f, 82f)),
                            "Airborne feet may move within a pose; the delivered world root must stay fixed.");
                        Assert.That(run.FrameDurationsSeconds[frame], Is.EqualTo(0.06f).Within(0.000001f));
                        upperBodyHashes.Add(HashUpperBody(pixels));
                    }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                }
                Assert.That(upperBodyHashes.Count, Is.EqualTo(FrameCount),
                    $"{CharacterId}/{direction} repeats a frozen upper-body pose inside its run cycle.");
            }
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
