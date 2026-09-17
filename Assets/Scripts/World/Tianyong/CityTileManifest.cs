using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    /// <summary>Explicit JSON fields; Unity Rect uses engine-private field names in JsonUtility.</summary>
    [Serializable]
    public struct CityTileWorldRect
    {
        public float x;
        public float y;
        public float width;
        public float height;
        public static implicit operator Rect(CityTileWorldRect value)
            => new(value.x, value.y, value.width, value.height);
        public static implicit operator CityTileWorldRect(Rect value)
            => new() { x = value.x, y = value.y, width = value.width, height = value.height };
    }
    /// <summary>Complete, approved 4K delivery. Grid dimensions never imply a fixed total resolution.</summary>
    [Serializable]
    public sealed class CityTileManifest
    {
        public const string ResourceRoot = "World/CityTiles4K/";
        public int schemaVersion = 1;
        public int tilePixels = 4096;
        public int columns;
        public int rows;
        public CityTileWorldRect worldRect;
        public Rect WorldRect => worldRect;
        // Tianyong publication gate: the new painting must match every retained foreground silhouette.
        public bool legacyForegroundCompatible;
        // Complete row-major resource paths, with the north-west tile first.
        public string[] tiles;

        public int PixelWidth => checked(columns * tilePixels);
        public int PixelHeight => checked(rows * tilePixels);

        public bool Validate(out string error)
        {
            error = null;
            if (schemaVersion != 1 || tilePixels != 4096)
                error = "Expected schemaVersion 1 and individual 4096 x 4096 tiles.";
            else if (columns < 1 || rows < 1 || columns > 256 || rows > 256 || (long)columns * rows > 4096)
                error = "Invalid map grid dimensions.";
            else if (!Finite(worldRect.x) || !Finite(worldRect.y) || !Finite(worldRect.width) ||
                     !Finite(worldRect.height) || worldRect.width <= 0 || worldRect.height <= 0)
                error = "Invalid world rectangle.";
            else if (Mathf.Abs(worldRect.width / worldRect.height - (float)columns / rows) > 0.0001f)
                error = "Square tile artwork must keep its aspect ratio in world space.";
            else if (tiles == null || tiles.Length != columns * rows)
                error = "A complete row-major tile list is required before activation.";
            else
            {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in tiles)
                    if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(ResourceRoot, StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("..") || path.Contains("\\") || path.Contains(".") || !paths.Add(path))
                    { error = "Tile paths must be unique extensionless Resources paths under " + ResourceRoot; break; }
            }
            return error == null;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public Rect TileWorldRect(int row, int column)
        {
            float width = worldRect.width / columns, height = worldRect.height / rows;
            return new Rect(WorldRect.xMin + column * width, WorldRect.yMax - (row + 1) * height, width, height);
        }

        /// <summary>Returns row/column indices covering the view plus a ring; never caps a wide view to nine tiles.</summary>
        public RectInt VisibleRange(Rect view, int padding)
        {
            if (!WorldRect.Overlaps(view)) return new RectInt();
            float width = worldRect.width / columns, height = worldRect.height / rows;
            int left = Mathf.Clamp(Mathf.FloorToInt((view.xMin - WorldRect.xMin) / width) - padding, 0, columns);
            int right = Mathf.Clamp(Mathf.CeilToInt((view.xMax - WorldRect.xMin) / width) + padding, 0, columns);
            int top = Mathf.Clamp(Mathf.FloorToInt((WorldRect.yMax - view.yMax) / height) - padding, 0, rows);
            int bottom = Mathf.Clamp(Mathf.CeilToInt((WorldRect.yMax - view.yMin) / height) + padding, 0, rows);
            return new RectInt(left, top, right - left, bottom - top);
        }
    }
}
