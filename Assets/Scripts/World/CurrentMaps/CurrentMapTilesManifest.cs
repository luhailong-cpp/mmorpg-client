using System;
using System.Collections.Generic;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.World.CurrentMaps
{
    /// <summary>正式地图运行分片清单（schemaVersion 2，1024×1024 formal_runtime_tiles）。</summary>
    [Serializable]
    public sealed class CurrentMapTilesManifest
    {
        public int schemaVersion;
        public string deliveryStatus;
        public string key;
        public uint sceneId;
        public string layoutSha256;
        public string revision;
        public string appearance;
        public string resourceRoot;
        public int columns;
        public int rows;
        public int tilePixels;
        public CityTileWorldRect worldRect;
        public MapDeliveryRect effectiveWorldRect;
        public MapDeliveryRect imageRect;
        public int referenceWidth;
        public int referenceHeight;
        public Entry[] tiles;

        [Serializable]
        public sealed class Entry
        {
            public int row;
            public int column;
            public string resourcePath;
            public string sha256;
            public long bytes;
            public string nativeSha256;
            public int nativePixels;
        }

        [Serializable]
        private sealed class LayoutSource
        {
            public int schemaVersion;
            public string key;
            public uint sceneId;
            public string layoutSha256;
            public string revision;
            public string appearance;
            public string artStatus;
            public CityTileWorldRect worldRect;
            public MapDeliveryRect effectiveWorldRect;
        }

        public Rect WorldRect => worldRect;
        public Rect EffectiveWorldRect => new Rect(effectiveWorldRect.x, effectiveWorldRect.y, effectiveWorldRect.width, effectiveWorldRect.height);

        public static CurrentMapTilesManifest Parse(string expectedKey, string sourceJson, string manifestJson,
            string expectedResourceRoot = null, string expectedRevision = null, string expectedAppearance = null)
        {
            var source = JsonUtility.FromJson<LayoutSource>(sourceJson);
            var manifest = JsonUtility.FromJson<CurrentMapTilesManifest>(manifestJson);
            if (source == null || source.schemaVersion != 2 ||
                source.key != expectedKey || !IsSha256(source.layoutSha256))
                throw new InvalidOperationException("当前布局来源身份或 SHA 无效。");
            if (manifest == null) throw new InvalidOperationException("当前地图分片清单为空。");
            if (manifest.schemaVersion != 2)
                throw new InvalidOperationException("地图来源和分片清单版本不一致，要求 schemaVersion 2。");
            if (source.artStatus != "formal_tiles_overview" ||
                source.revision != manifest.revision || source.appearance != manifest.appearance ||
                (Rect)source.worldRect != manifest.WorldRect ||
                !SameRect(source.effectiveWorldRect, manifest.effectiveWorldRect) ||
                expectedResourceRoot != manifest.resourceRoot || expectedRevision != manifest.revision ||
                expectedAppearance != manifest.appearance)
                throw new InvalidOperationException("新正式地图必须匹配已选择的外观、版本、来源和有效范围。");
            if (!manifest.Validate(expectedKey, source.sceneId, source.layoutSha256, out var error))
                throw new InvalidOperationException("当前地图分片清单无效：" + error);
            return manifest;
        }

        public bool Validate(string expectedKey, uint expectedSceneId, string expectedLayoutSha, out string error)
        {
            error = null;
            if (schemaVersion != 2 || deliveryStatus != "formal_runtime_tiles")
                error = "要求 schemaVersion 2 和 formal_runtime_tiles。";
            else if (expectedSceneId == 0 || key != expectedKey || sceneId != expectedSceneId)
                error = "分片与当前地图身份不一致。";
            else if (!IsSha256(layoutSha256) || !IsSha256(expectedLayoutSha) ||
                     !string.Equals(layoutSha256, expectedLayoutSha, StringComparison.OrdinalIgnoreCase))
                error = "分片必须绑定当前 source.json 的 layoutSha256。";
            else if (string.IsNullOrWhiteSpace(revision) || !MapDeliveryContract.IsAppearance(appearance) ||
                     !MapDeliveryContract.IsResourcePath(resourceRoot, true))
                error = "新地图版本、外观或资源目录无效。";
            else if ((error = MapDeliveryContract.ValidateGeometry(columns, rows, tilePixels,
                     new MapDeliveryRect { x = worldRect.x, y = worldRect.y, width = worldRect.width, height = worldRect.height },
                     effectiveWorldRect, imageRect, referenceWidth, referenceHeight)) != null) { }
            else if (tiles == null || tiles.Length != columns * rows)
                error = "新正式地图要求完整分片，不用预览补缺。";
            else
            {
                var occupied = new HashSet<int>();
                foreach (var tile in tiles)
                {
                    if (tile == null || tile.row < 1 || tile.row > rows || tile.column < 1 || tile.column > columns ||
                        !occupied.Add((tile.row - 1) * columns + tile.column - 1))
                    { error = "分片坐标越界或重复。"; break; }
                    var expectedPath = $"{resourceRoot}tiles/r{tile.row:00}_c{tile.column:00}";
                    if (tile.resourcePath != expectedPath || !IsSha256(tile.sha256) || tile.bytes <= 0)
                    { error = "分片路径、原图 SHA 或字节数无效。"; break; }
                    if (!IsSha256(tile.nativeSha256) || tile.nativePixels < tilePixels)
                    { error = "1024运行派生必须保留最高实际原生来源身份。"; break; }
                }
            }
            return error == null;
        }

        public Rect TileWorldRect(int row, int column)
        {
            var bounds = WorldRect;
            // 清单从西北开始编号。先按整图比例用double计算共享分界，再一次转float；
            // 14格的单格宽高不能精确表示，先取float格长再相乘会把最后一行推出世界边界。
            float west = column == 1 ? bounds.xMin :
                (float)(worldRect.x + (double)worldRect.width * (column - 1) / columns);
            float east = column == columns ? bounds.xMax :
                (float)(worldRect.x + (double)worldRect.width * column / columns);
            float south = row == rows ? bounds.yMin :
                (float)(worldRect.y + worldRect.height - (double)worldRect.height * row / rows);
            float north = row == 1 ? bounds.yMax :
                (float)(worldRect.y + worldRect.height - (double)worldRect.height * (row - 1) / rows);
            return Rect.MinMaxRect(west, south, east, north);
        }

        public static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
            return true;
        }

        private static bool SameRect(MapDeliveryRect a, MapDeliveryRect b)
            => a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
    }
}
