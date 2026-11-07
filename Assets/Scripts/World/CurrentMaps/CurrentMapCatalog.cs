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
        private static readonly CurrentMapDefinition[] Legacy =
        {
            new CurrentMapDefinition("main", "主城", 1),
            new CurrentMapDefinition("island", "八仙岛", 2),
            new CurrentMapDefinition("village", "东海渔村", 3),
        };
        private static IReadOnlyList<CurrentMapDefinition> _all = Array.AsReadOnly(Legacy);
        private static bool _catalogRead;
        public static IReadOnlyList<CurrentMapDefinition> All
        {
            get { if (!_catalogRead) ReloadDeliveries(); return _all; }
        }

        public static void ReloadDeliveries()
        {
            var text = Resources.Load<TextAsset>(MapDeliveryContract.CatalogPath);
            if (text == null) { _all = Array.AsReadOnly(Legacy); _catalogRead = true; return; }
            try { ApplyDeliveries(JsonUtility.FromJson<MapDeliveryCatalog>(text.text)); }
            catch (Exception error)
            {
                _catalogRead = true;
                Debug.LogError("[CurrentMap] 拒绝无效正式交付catalog，保留现有注册：" + error.Message);
            }
        }

        public static void ApplyDeliveries(MapDeliveryCatalog catalog)
        {
            catalog?.Validate();
            if (catalog == null) throw new InvalidOperationException("地图正式交付catalog为空。");
            foreach (var delivery in catalog.maps)
            foreach (var legacy in Legacy)
                if (delivery.key == legacy.Key && delivery.sceneId != legacy.SceneConfigId)
                    throw new InvalidOperationException("已有地图key不能改用另一个场景编号。");
            var next = new List<CurrentMapDefinition>();
            foreach (var map in Legacy)
            {
                bool replaced = false;
                foreach (var delivery in catalog.maps) if (delivery.sceneId == map.SceneConfigId) replaced = true;
                if (!replaced) next.Add(map);
            }
            foreach (var delivery in catalog.maps)
                next.Add(new CurrentMapDefinition(delivery.Snapshot(), delivery.DefaultAppearance));
            _all = next.AsReadOnly();
            _catalogRead = true;
        }

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
        private readonly MapDeliveryRegistration _delivery;
        private readonly MapAppearanceDelivery _appearance;
        internal CurrentMapDefinition(string key, string name, uint sceneConfigId)
        { Key = key; Name = name; SceneConfigId = sceneConfigId; }
        internal CurrentMapDefinition(MapDeliveryRegistration delivery, string appearance)
        {
            _delivery = delivery;
            _appearance = delivery.Find(appearance) ?? throw new InvalidOperationException("未交付的地图外观：" + appearance);
            Key = delivery.key; Name = delivery.name; SceneConfigId = delivery.sceneId;
        }
        public string Key { get; }
        public string Name { get; }
        public uint SceneConfigId { get; }
        public bool IsModern => _delivery != null;
        public bool CanTravelOnline => _delivery == null || !_delivery.clientOnly;
        public string Appearance => _appearance?.id ?? "normal";
        public string Revision => _delivery?.revision ?? "legacy20261009";
        public string ResourceFolder => _appearance?.resourceRoot ?? CurrentMapCatalog.ResourceRoot + Key + "/";
        public string ContentIdentity => Key + ":" + Revision + ":" + Appearance + ":" + (_appearance?.layoutSha256 ?? "legacy");
        public string TexturePath => ResourceFolder + "layout";
        public string SourcePath => ResourceFolder + "source";
        public string MaskPath => ResourceFolder + "walkmask";
        public Vector3 Spawn => Data.Spawn;
        public Rect WorldRect => Data.WorldRect;
        public Rect EffectiveWorldRect => Data.EffectiveWorldRect;
        public Rect NavigationWorldRect => IsModern ? WorldRect : new Rect(0, 0, TianyongMapDefinition.Width, TianyongMapDefinition.Depth);
        public bool IsWalkable(Vector3 world) => Data.IsWalkable(world);
        public TianyongNavigationGrid CreateNavigation()
            => new(Data.CellSize, IsWalkable, NavigationWorldRect, IsModern ? EffectiveWorldRect : NavigationWorldRect);

        public CurrentMapDefinition ForAppearance(string appearance)
            => !MapDeliveryContract.IsAppearance(appearance) ? null : !IsModern ? appearance == "normal" ? this : null :
                _delivery.Find(appearance) == null ? null : new CurrentMapDefinition(_delivery, appearance);

        public bool SharesNavigationWith(CurrentMapDefinition other)
            => other != null && Data.SharesNavigationWith(other.Data);

        public Texture2D LoadTexture()
        {
            var texture = Resources.Load<Texture2D>(TexturePath);
            if (texture == null) throw new InvalidOperationException($"当前地图原图缺失：{TexturePath}");
            if (texture.width != Data.LayoutWidth || texture.height != Data.LayoutHeight)
                throw new InvalidOperationException($"地图底图尺寸错误：{TexturePath}，要求{Data.LayoutWidth}×{Data.LayoutHeight}。");
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
                var loaded = CurrentMapData.Parse(Key, SceneConfigId, source.text, mask.text);
                if (IsModern && (loaded.SchemaVersion != 2 || loaded.Revision != Revision || loaded.Appearance != Appearance ||
                    loaded.LayoutSha256 != _appearance.layoutSha256))
                    throw new InvalidOperationException("地图实际source与选定revision/appearance/SHA不一致。");
                if (IsModern)
                {
                    var tiles = Resources.Load<TextAsset>(ResourceFolder + "tiles");
                    if (tiles == null) throw new InvalidOperationException("新地图正式运行分片清单缺失。");
                    CurrentMapTilesManifest.Parse(Key, source.text, tiles.text, ResourceFolder, Revision, Appearance);
                }
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
            public MapDeliveryRect effectiveWorldRect;
            public string revision, appearance, layoutSha256, artStatus;
            public int walkmaskColumns, walkmaskRows;
            public Vector3 spawnWorld;
        }

        [Serializable]
        private sealed class PixelSize { public int width; public int height; }

        private readonly byte[] _walkMask;
        public Rect WorldRect { get; }
        public Rect EffectiveWorldRect { get; }
        public float CellSize { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int LayoutWidth { get; }
        public int LayoutHeight { get; }
        public int SchemaVersion { get; }
        public string Revision { get; }
        public string Appearance { get; }
        public string LayoutSha256 { get; }
        public Vector3 Spawn { get; }

        private CurrentMapData(Source source, byte[] walkMask)
        {
            WorldRect = source.worldRect; Spawn = source.spawnWorld; _walkMask = walkMask;
            SchemaVersion = source.schemaVersion; CellSize = source.cellSize;
            Columns = SchemaVersion == 1 ? source.walkmaskResolution : source.walkmaskColumns;
            Rows = SchemaVersion == 1 ? source.walkmaskResolution : source.walkmaskRows;
            EffectiveWorldRect = SchemaVersion == 1 ? WorldRect : new Rect(source.effectiveWorldRect.x,
                source.effectiveWorldRect.y, source.effectiveWorldRect.width, source.effectiveWorldRect.height);
            LayoutWidth = source.layoutPixels.width; LayoutHeight = source.layoutPixels.height;
            Revision = source.revision; Appearance = source.appearance; LayoutSha256 = source.layoutSha256;
        }

        public static CurrentMapData Parse(string key, string sourceJson, string maskBase64)
            => Parse(key, CurrentMapCatalog.Find(key)?.SceneConfigId ?? 0, sourceJson, maskBase64);

        public static CurrentMapData Parse(string key, uint sceneId, string sourceJson, string maskBase64)
        {
            if (string.IsNullOrWhiteSpace(sourceJson))
                throw new InvalidOperationException($"当前地图来源清单为空：{key}");
            Source source;
            try { source = JsonUtility.FromJson<Source>(sourceJson); }
            catch (Exception error) { throw new InvalidOperationException($"当前地图来源清单格式错误：{key}", error); }
            if (source == null || (source.schemaVersion != 1 && source.schemaVersion != 2))
                throw new InvalidOperationException($"当前地图清单版本错误：{key}，要求 schemaVersion 1。");
            bool modern = source.schemaVersion == 2;
            if (sceneId == 0 || source.key != key || source.sceneId != sceneId)
                throw new InvalidOperationException($"当前地图清单身份错误：{key}");
            if (!modern && (source.layoutPixels == null || source.layoutPixels.width != CurrentMapCatalog.LayoutPixels ||
                source.layoutPixels.height != CurrentMapCatalog.LayoutPixels ||
                source.walkmaskResolution != CurrentMapCatalog.MaskResolution || source.cellSize != CurrentMapCatalog.CellSize))
                throw new InvalidOperationException($"当前地图清单尺寸错误：{key}，要求 1254×1254 原图和 150×150 导航。");
            Rect rect = source.worldRect;
            if (!Finite(rect.x) || !Finite(rect.y) || !Finite(rect.width) || !Finite(rect.height) ||
                rect.width <= 0 || rect.height <= 0 || (!modern && rect != CurrentMapCatalog.WorldRect))
                throw new InvalidOperationException($"当前地图世界范围错误：{key}，要求 (50,0,300,300)。");
            if (modern && (source.artStatus != "formal_tiles_overview" || string.IsNullOrWhiteSpace(source.revision) ||
                !MapDeliveryContract.IsAppearance(source.appearance) || !MapDeliveryContract.IsSha256(source.layoutSha256) ||
                source.layoutPixels == null || source.layoutPixels.width < 1 || source.layoutPixels.height < 1 ||
                source.layoutPixels.width > 8192 || source.layoutPixels.height > 8192 ||
                Math.Abs((double)source.layoutPixels.width * rect.height - (double)source.layoutPixels.height * rect.width) >
                    Math.Max(rect.width, rect.height) ||
                !Finite(source.cellSize) || source.cellSize < .5f || source.walkmaskColumns < 1 || source.walkmaskRows < 1 ||
                (long)source.walkmaskColumns * source.walkmaskRows > 4000000 ||
                source.walkmaskColumns != Mathf.CeilToInt(rect.width / source.cellSize) ||
                source.walkmaskRows != Mathf.CeilToInt(rect.height / source.cellSize) ||
                !new MapDeliveryRect { x = rect.x, y = rect.y, width = rect.width, height = rect.height }.Contains(source.effectiveWorldRect)))
                throw new InvalidOperationException("新地图必须包含正式图块总览、有效范围和同源矩形导航。");
            var spawn = source.spawnWorld;
            if (!Finite(spawn.x) || !Finite(spawn.y) || !Finite(spawn.z) || spawn.y != 0f ||
                spawn.x < rect.xMin || spawn.x >= rect.xMax || spawn.z < rect.yMin || spawn.z >= rect.yMax)
                throw new InvalidOperationException($"当前地图出生点越界：{key} {spawn}");
            byte[] mask;
            try { mask = Convert.FromBase64String(maskBase64?.Trim() ?? ""); }
            catch (FormatException error) { throw new InvalidOperationException($"当前地图导航不是有效 Base64：{key}", error); }
            int bits = modern ? source.walkmaskColumns * source.walkmaskRows : CurrentMapCatalog.MaskResolution * CurrentMapCatalog.MaskResolution;
            if (mask.Length != (bits + 7) / 8)
                throw new InvalidOperationException($"当前地图导航长度错误：{key}，要求 {(bits + 7) / 8} 字节。");
            if (bits % 8 != 0 && (mask[mask.Length - 1] & ((1 << (8 - bits % 8)) - 1)) != 0)
                throw new InvalidOperationException($"当前地图导航填充位错误：{key}");
            var data = new CurrentMapData(source, mask);
            if (!data.IsWalkable(spawn))
                throw new InvalidOperationException($"当前地图出生点不可行走：{key} {spawn}");
            return data;
        }

        public bool IsWalkable(Vector3 world)
        {
            if (!Finite(world.x) || !Finite(world.z) || world.x < EffectiveWorldRect.xMin || world.x >= EffectiveWorldRect.xMax ||
                world.z < EffectiveWorldRect.yMin || world.z >= EffectiveWorldRect.yMax) return false;
            int x = Mathf.FloorToInt((world.x - WorldRect.xMin) / CellSize);
            // Navigation cells include their west/south edge, like the controller
            // and server grids. Only storage order is reversed north-to-south.
            int row = Rows - 1 - Mathf.FloorToInt((world.z - WorldRect.yMin) / CellSize);
            if (x < 0 || x >= Columns || row < 0 || row >= Rows) return false;
            int index = row * Columns + x;
            return (_walkMask[index >> 3] & (0x80 >> (index & 7))) != 0;
        }

        public bool SharesNavigationWith(CurrentMapData other)
        {
            if (other == null || WorldRect != other.WorldRect || EffectiveWorldRect != other.EffectiveWorldRect ||
                Columns != other.Columns || Rows != other.Rows || CellSize != other.CellSize || _walkMask.Length != other._walkMask.Length)
                return false;
            for (int i = 0; i < _walkMask.Length; i++) if (_walkMask[i] != other._walkMask[i]) return false;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
