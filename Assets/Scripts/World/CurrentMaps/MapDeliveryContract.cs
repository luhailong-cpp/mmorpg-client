using System;
using System.Collections.Generic;

namespace MmorpgClient.World.CurrentMaps
{
    /// <summary>Portable delivery rules shared by the runtime and offline validation.</summary>
    public static class MapDeliveryContract
    {
        public const int RuntimeTilePixels = 1024;
        public const int MaximumTiles = 128;
        public const string ResourceRoot = "World/QdaoMaps20261102/";
        public const string CatalogPath = ResourceRoot + "catalog";

        public static bool IsAppearance(string value)
            => value == "normal" || value == "spring" || value == "lantern" || value == "mid-autumn";

        public static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
            return true;
        }

        public static bool IsResourcePath(string value, bool folder = false)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith(ResourceRoot, StringComparison.Ordinal) ||
                value.Contains("//") || value.EndsWith("/", StringComparison.Ordinal) != folder) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') &&
                    c != '/' && c != '-' && c != '_') return false;
            return value.Length > ResourceRoot.Length;
        }

        public static bool IsKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') &&
                    c != '-' && c != '_') return false;
            return true;
        }

        public static string ValidateGeometry(int columns, int rows, int tilePixels,
            MapDeliveryRect world, MapDeliveryRect effective, MapDeliveryRect image,
            int referenceWidth, int referenceHeight)
        {
            if (columns < 1 || rows < 1 || (long)columns * rows > MaximumTiles)
                return "A positive rectangular grid of at most128 tiles is required; axes are not fixed.";
            if (tilePixels != RuntimeTilePixels) return "New runtime tiles must be1024 square.";
            if (!world.Valid || !effective.Valid || !image.Valid || !world.Contains(effective))
                return "Finite positive world/effective/image rectangles are required.";
            var raster = new MapDeliveryRect { width = columns * (float)tilePixels, height = rows * (float)tilePixels };
            if (!raster.Contains(image)) return "The effective image rectangle must stay inside the tile raster.";
            double tileWidth = world.width / columns, tileHeight = world.height / rows;
            if (Math.Abs(tileWidth - tileHeight) > Math.Max(tileWidth, tileHeight) * .00001)
                return "Square artwork tiles must not stretch in world space.";
            double scale = tileWidth / tilePixels;
            double x = world.x + image.x * scale;
            // Image rectangles use north-west pixel coordinates; world y denotes Unity Z south edge.
            double y = world.y + world.height - (image.y + image.height) * scale;
            double tolerance = Math.Max(world.width, world.height) * .00001;
            if (Math.Abs(effective.x - x) > tolerance || Math.Abs(effective.y - y) > tolerance ||
                Math.Abs(effective.width - image.width * scale) > tolerance ||
                Math.Abs(effective.height - image.height * scale) > tolerance)
                return "Effective image and world rectangles must use the same uniform scale.";
            if (referenceWidth < 1 || referenceHeight < 1 ||
                Math.Abs((double)image.width * referenceHeight - (double)image.height * referenceWidth) >
                Math.Max(referenceWidth, referenceHeight))
                return "The full reference aspect ratio must be preserved within one edge pixel.";
            return null;
        }
    }

    [Serializable]
    public struct MapDeliveryRect
    {
        public float x, y, width, height;
        public bool Valid => Finite(x) && Finite(y) && Finite(width) && Finite(height) &&
            Finite(x + width) && Finite(y + height) && width > 0 && height > 0;
        public bool Contains(MapDeliveryRect other) => Valid && other.Valid && other.x >= x && other.y >= y &&
            other.x + other.width <= x + width && other.y + other.height <= y + height;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [Serializable]
    public sealed class MapDeliveryCatalog
    {
        public int schemaVersion;
        public MapDeliveryRegistration[] maps;

        public void Validate()
        {
            if (schemaVersion != 2 || maps == null) throw new InvalidOperationException("Map delivery catalog requires schemaVersion2 and maps.");
            var scenes = new HashSet<uint>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in maps)
            {
                if (map == null) throw new InvalidOperationException("Null map registration.");
                map.Validate();
                if (!scenes.Add(map.sceneId) || !keys.Add(map.key)) throw new InvalidOperationException("Duplicate map identity or sceneId.");
            }
        }
    }

    [Serializable]
    public sealed class MapDeliveryRegistration
    {
        public string key, name, revision, defaultAppearance;
        public uint sceneId;
        // Client-only identities are not authoritative server scene registrations.
        public bool clientOnly;
        public MapAppearanceDelivery[] appearances;

        // Earlier schemaVersion2 catalogs did not serialize a default and selected normal.
        // A holiday-only formal delivery must opt in explicitly rather than invent normal art.
        public string DefaultAppearance => string.IsNullOrEmpty(defaultAppearance) ? "normal" : defaultAppearance;

        public void Validate()
        {
            if (sceneId == 0 || !MapDeliveryContract.IsKey(key) || string.IsNullOrWhiteSpace(name) ||
                !MapDeliveryContract.IsKey(revision) || appearances == null || appearances.Length == 0)
                throw new InvalidOperationException("Map identity, authoritative sceneId, revision and appearances are required.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var appearance in appearances)
            {
                if (appearance == null || !MapDeliveryContract.IsAppearance(appearance.id) || !ids.Add(appearance.id) ||
                    !roots.Add(appearance.resourceRoot ?? "") ||
                    appearance.artStatus != "formal_art_delivery" ||
                    !MapDeliveryContract.IsResourcePath(appearance.resourceRoot, true) ||
                    !MapDeliveryContract.IsSha256(appearance.layoutSha256) || appearance.resourceRoot !=
                        MapDeliveryContract.ResourceRoot + key + "/" + revision + "/" + appearance.id + "/" + appearance.layoutSha256 + "/")
                    throw new InvalidOperationException("Only explicit source-bound formal appearances can enter the runtime catalog.");
            }
            if (!MapDeliveryContract.IsAppearance(DefaultAppearance) || !ids.Contains(DefaultAppearance))
                throw new InvalidOperationException("The default appearance must be an explicitly delivered formal appearance.");
        }

        public MapAppearanceDelivery Find(string id)
        {
            foreach (var appearance in appearances ?? Array.Empty<MapAppearanceDelivery>())
                if (appearance.id == id) return appearance;
            return null;
        }

        public MapDeliveryRegistration Snapshot()
        {
            var copy = new MapDeliveryRegistration { key = key, name = name, revision = revision, sceneId = sceneId,
                clientOnly = clientOnly,
                defaultAppearance = defaultAppearance,
                appearances = new MapAppearanceDelivery[appearances.Length] };
            for (int i = 0; i < appearances.Length; i++)
            {
                var value = appearances[i];
                copy.appearances[i] = new MapAppearanceDelivery { id = value.id, resourceRoot = value.resourceRoot,
                    layoutSha256 = value.layoutSha256, artStatus = value.artStatus };
            }
            return copy;
        }
    }

    [Serializable]
    public sealed class MapAppearanceDelivery
    {
        public string id, resourceRoot, layoutSha256, artStatus;
    }
}
