using System;
using System.Collections.Generic;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.World.CurrentMaps
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>Current complete layout images and their matching conservative navigation.</summary>
    public static class CurrentMapCatalog
    {
        public const string ResourceRoot = "World/CurrentMaps20261009/";
        public const int LayoutPixels = 1254;
        public const int MaskResolution = 150;
        public const float CellSize = 2f;
        public static readonly Rect WorldRect = new(50f, 0f, 300f, 300f);
        public static readonly IReadOnlyList<CurrentMapDefinition> All = Array.AsReadOnly(new[]
        {
            new CurrentMapDefinition("main", "主城", 1),
            new CurrentMapDefinition("island", "八仙岛", 2),
            new CurrentMapDefinition("village", "东海渔村", 3),
        });

        public static CurrentMapDefinition Find(string key)
        {
            foreach (var map in All) if (string.Equals(map.Key, key, StringComparison.Ordinal)) return map;
            return null;
        }

        public static CurrentMapDefinition Find(uint sceneConfigId)
        {
            foreach (var map in All) if (map.SceneConfigId == sceneConfigId) return map;
            return null;
        }
    }

    public sealed class CurrentMapDefinition
    {
        private CurrentMapData _data;
        internal CurrentMapDefinition(string key, string name, uint sceneConfigId)
        { Key = key; Name = name; SceneConfigId = sceneConfigId; }
        public string Key { get; }
        public string Name { get; }
        public uint SceneConfigId { get; }
        public string TexturePath => CurrentMapCatalog.ResourceRoot + Key + "/layout";
        public string SourcePath => CurrentMapCatalog.ResourceRoot + Key + "/source";
        public string MaskPath => CurrentMapCatalog.ResourceRoot + Key + "/walkmask";
        public Vector3 Spawn => Data.Spawn;
        public Rect WorldRect => Data.WorldRect;
        public bool IsWalkable(Vector3 world) => Data.IsWalkable(world);
        public TianyongNavigationGrid CreateNavigation()
            => new(CurrentMapCatalog.CellSize, IsWalkable);

        public Texture2D LoadTexture()
        {
            var texture = Resources.Load<Texture2D>(TexturePath);
            if (texture == null) throw new InvalidOperationException($"当前地图原图缺失：{TexturePath}");
            if (texture.width != CurrentMapCatalog.LayoutPixels || texture.height != CurrentMapCatalog.LayoutPixels)
                throw new InvalidOperationException($"当前地图原图尺寸错误：{TexturePath}，要求原始 1254×1254。");
            return texture;
        }

        private CurrentMapData Data
        {
            get
            {
                if (_data != null) return _data;
                var source = Resources.Load<TextAsset>(SourcePath);
                if (source == null) throw new InvalidOperationException($"当前地图来源清单缺失：{SourcePath}");
                var mask = Resources.Load<TextAsset>(MaskPath);
                if (mask == null) throw new InvalidOperationException($"当前地图导航缺失：{MaskPath}");
                var loaded = CurrentMapData.Parse(Key, source.text, mask.text);
                if (!loaded.IsWalkable(loaded.Spawn))
                    throw new InvalidOperationException($"当前地图出生点不可行走：{Key} {loaded.Spawn}");
                _data = loaded;
                return _data;
            }
        }
    }

    /// <summary>Validated immutable mask shared by runtime construction and resource checks.</summary>
    public sealed class CurrentMapData
    {
        [Serializable]
        private sealed class Source
        {
            public int schemaVersion;
            public string key;
            public uint sceneId;
            public PixelSize layoutPixels;
            public int walkmaskResolution;
            public float cellSize;
            public CityTileWorldRect worldRect;
            public Vector3 spawnWorld;
        }

        [Serializable]
        private sealed class PixelSize { public int width; public int height; }

        private readonly byte[] _walkMask;
        public Rect WorldRect { get; }
        public Vector3 Spawn { get; }

        private CurrentMapData(Rect rect, Vector3 spawn, byte[] walkMask)
        { WorldRect = rect; Spawn = spawn; _walkMask = walkMask; }

        public static CurrentMapData Parse(string key, string sourceJson, string maskBase64)
        {
            if (string.IsNullOrWhiteSpace(sourceJson))
                throw new InvalidOperationException($"当前地图来源清单为空：{key}");
            Source source;
            try { source = JsonUtility.FromJson<Source>(sourceJson); }
            catch (Exception error) { throw new InvalidOperationException($"当前地图来源清单格式错误：{key}", error); }
            if (source == null || source.schemaVersion != 1)
                throw new InvalidOperationException($"当前地图清单版本错误：{key}，要求 schemaVersion 1。");
            var definition = CurrentMapCatalog.Find(key);
            if (definition == null || source.key != key || source.sceneId != definition.SceneConfigId)
                throw new InvalidOperationException($"当前地图清单身份错误：{key}");
            if (source.layoutPixels == null || source.layoutPixels.width != CurrentMapCatalog.LayoutPixels ||
                source.layoutPixels.height != CurrentMapCatalog.LayoutPixels ||
                source.walkmaskResolution != CurrentMapCatalog.MaskResolution || source.cellSize != CurrentMapCatalog.CellSize)
                throw new InvalidOperationException($"当前地图清单尺寸错误：{key}，要求 1254×1254 原图和 150×150 导航。");
            Rect rect = source.worldRect;
            if (!Finite(rect.x) || !Finite(rect.y) || !Finite(rect.width) || !Finite(rect.height) ||
                rect != CurrentMapCatalog.WorldRect)
                throw new InvalidOperationException($"当前地图世界范围错误：{key}，要求 (50,0,300,300)。");
            var spawn = source.spawnWorld;
            if (!Finite(spawn.x) || !Finite(spawn.y) || !Finite(spawn.z) || spawn.y != 0f ||
                spawn.x < rect.xMin || spawn.x >= rect.xMax || spawn.z < rect.yMin || spawn.z >= rect.yMax)
                throw new InvalidOperationException($"当前地图出生点越界：{key} {spawn}");
            byte[] mask;
            try { mask = Convert.FromBase64String(maskBase64?.Trim() ?? ""); }
            catch (FormatException error) { throw new InvalidOperationException($"当前地图导航不是有效 Base64：{key}", error); }
            int bits = CurrentMapCatalog.MaskResolution * CurrentMapCatalog.MaskResolution;
            if (mask.Length != (bits + 7) / 8)
                throw new InvalidOperationException($"当前地图导航长度错误：{key}，要求 {(bits + 7) / 8} 字节。");
            if ((mask[mask.Length - 1] & 0x0f) != 0)
                throw new InvalidOperationException($"当前地图导航填充位错误：{key}");
            var data = new CurrentMapData(rect, spawn, mask);
            if (!data.IsWalkable(spawn))
                throw new InvalidOperationException($"当前地图出生点不可行走：{key} {spawn}");
            return data;
        }

        public bool IsWalkable(Vector3 world)
        {
            if (!Finite(world.x) || !Finite(world.z) || world.x < WorldRect.xMin || world.x >= WorldRect.xMax ||
                world.z < WorldRect.yMin || world.z >= WorldRect.yMax) return false;
            int x = Mathf.FloorToInt((world.x - WorldRect.xMin) / CurrentMapCatalog.CellSize);
            // Navigation cells include their west/south edge, like the controller
            // and server grids. Only storage order is reversed north-to-south.
            int row = CurrentMapCatalog.MaskResolution - 1 -
                Mathf.FloorToInt((world.z - WorldRect.yMin) / CurrentMapCatalog.CellSize);
            if (x < 0 || x >= CurrentMapCatalog.MaskResolution || row < 0 || row >= CurrentMapCatalog.MaskResolution) return false;
            int index = row * CurrentMapCatalog.MaskResolution + x;
            return (_walkMask[index >> 3] & (0x80 >> (index & 7))) != 0;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
