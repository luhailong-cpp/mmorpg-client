using System;
using System.Collections.Generic;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.World.CurrentMaps
{
    /// <summary>当前布局已交付的原生分片；空位继续显示同一版布局底图。</summary>
    [Serializable]
    public sealed class CurrentMapTilesManifest
    {
        public const string ResourceRoot = "World/CurrentMaps20261009/";
        public int schemaVersion;
        public string deliveryStatus;
        public string key;
        public uint sceneId;
        public string layoutSha256;
        public int columns;
        public int rows;
        public int tilePixels;
        public CityTileWorldRect worldRect;
        public Entry[] tiles;

        [Serializable]
        public sealed class Entry
        {
            public int row;
            public int column;
            public string resourcePath;
            public string sha256;
            public long bytes;
        }

        [Serializable]
        private sealed class LayoutSource
        {
            public int schemaVersion;
            public string key;
            public uint sceneId;
            public string layoutSha256;
        }

        public Rect WorldRect => worldRect;

        public static CurrentMapTilesManifest Parse(string expectedKey, string sourceJson, string manifestJson)
        {
            var source = JsonUtility.FromJson<LayoutSource>(sourceJson);
            var manifest = JsonUtility.FromJson<CurrentMapTilesManifest>(manifestJson);
            if (source == null || source.schemaVersion != 1 || source.key != expectedKey || !IsSha256(source.layoutSha256))
                throw new InvalidOperationException("当前布局来源身份或 SHA 无效。");
            if (manifest == null) throw new InvalidOperationException("当前地图分片清单为空。");
            if (!manifest.Validate(expectedKey, source.sceneId, source.layoutSha256, out var error))
                throw new InvalidOperationException("当前地图分片清单无效：" + error);
            return manifest;
        }

        public bool Validate(string expectedKey, uint expectedSceneId, string expectedLayoutSha, out string error)
        {
            error = null;
            uint requiredScene = expectedKey == "main" ? 1u : expectedKey == "island" ? 2u : expectedKey == "village" ? 3u : 0u;
            int requiredGrid = expectedKey == "main" ? 16 : 14;
            if (schemaVersion != 1 || deliveryStatus != "partial_native_tiles")
                error = "要求 schemaVersion 1 和 partial_native_tiles。";
            else if (requiredScene == 0 || key != expectedKey || sceneId != requiredScene || expectedSceneId != requiredScene)
                error = "分片与当前地图身份不一致。";
            else if (!IsSha256(layoutSha256) || !IsSha256(expectedLayoutSha) ||
                     !string.Equals(layoutSha256, expectedLayoutSha, StringComparison.OrdinalIgnoreCase))
                error = "分片必须绑定当前 source.json 的 layoutSha256。";
            else if (columns != requiredGrid || rows != requiredGrid || tilePixels != 4096 ||
                     WorldRect != new Rect(50f, 0f, 300f, 300f))
                error = "分片网格、4096 原图尺寸或世界范围错误。";
            else if (tiles == null || tiles.Length > rows * columns)
                error = "分片列表缺失或数量越界。";
            else
            {
                var occupied = new HashSet<int>();
                foreach (var tile in tiles)
                {
                    if (tile == null || tile.row < 1 || tile.row > rows || tile.column < 1 || tile.column > columns ||
                        !occupied.Add((tile.row - 1) * columns + tile.column - 1))
                    { error = "分片坐标越界或重复。"; break; }
                    var expectedPath = $"{ResourceRoot}{key}/tiles/r{tile.row:00}_c{tile.column:00}";
                    if (tile.resourcePath != expectedPath || !IsSha256(tile.sha256) || tile.bytes <= 0)
                    { error = "分片路径、原图 SHA 或字节数无效。"; break; }
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
    }
}
