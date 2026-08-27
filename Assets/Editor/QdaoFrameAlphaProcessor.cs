using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.UI.EditorTools
{
    /// <summary>
    /// Converts a light checker/white preview backdrop connected to the image
    /// border into real alpha. It never touches enclosed cream panel pixels.
    /// This is a deterministic import correction, not a runtime dependency.
    /// </summary>
    public static class QdaoFrameAlphaProcessor
    {
        public static int RemoveConnectedLightBackdrop(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Expected a project-relative Assets path", nameof(assetPath));

            var absolutePath = Path.GetFullPath(assetPath);
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException("Frame source is missing", absolutePath);

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                name = "QdaoFrameAlphaWork",
            };

            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(absolutePath), false))
                    throw new InvalidDataException($"Could not decode PNG: {assetPath}");

                var width = texture.width;
                var height = texture.height;
                var pixels = texture.GetPixels32();
                var background = new bool[pixels.Length];
                var queue = new Queue<int>(Math.Max(width, height) * 4);

                void EnqueueIfBackdrop(int x, int y)
                {
                    var index = y * width + x;
                    if (background[index] || !IsLightNeutral(pixels[index]))
                        return;
                    background[index] = true;
                    queue.Enqueue(index);
                }

                for (var x = 0; x < width; x++)
                {
                    EnqueueIfBackdrop(x, 0);
                    EnqueueIfBackdrop(x, height - 1);
                }
                for (var y = 0; y < height; y++)
                {
                    EnqueueIfBackdrop(0, y);
                    EnqueueIfBackdrop(width - 1, y);
                }

                while (queue.Count > 0)
                {
                    var index = queue.Dequeue();
                    var x = index % width;
                    var y = index / width;
                    if (x > 0) EnqueueIfBackdrop(x - 1, y);
                    if (x + 1 < width) EnqueueIfBackdrop(x + 1, y);
                    if (y > 0) EnqueueIfBackdrop(x, y - 1);
                    if (y + 1 < height) EnqueueIfBackdrop(x, y + 1);
                }

                var removed = 0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    if (!background[i])
                        continue;
                    pixels[i].a = 0;
                    removed++;
                }

                var ratio = removed / (double)pixels.Length;
                if (ratio < 0.1 || ratio > 0.8)
                    throw new InvalidOperationException($"Unexpected backdrop coverage {ratio:P1}; refusing to overwrite {assetPath}");

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"[QdaoFrameAlphaProcessor] Removed {removed} connected backdrop pixels ({ratio:P1}) from {assetPath}");
                return removed;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static bool IsLightNeutral(Color32 color)
        {
            var min = Math.Min(color.r, Math.Min(color.g, color.b));
            var max = Math.Max(color.r, Math.Max(color.g, color.b));
            return min >= 224 && max - min <= 8;
        }
    }
}
