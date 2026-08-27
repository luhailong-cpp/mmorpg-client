using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    public sealed class TianyongMapInstance : IDisposable
    {
        internal TianyongMapInstance(GameObject root, TianyongTheme theme, TianyongNavigationGrid navigation)
        {
            Root = root;
            Theme = theme;
            Navigation = navigation;
        }

        public GameObject Root { get; }
        public TianyongTheme Theme { get; }
        public TianyongNavigationGrid Navigation { get; internal set; }
        public IReadOnlyDictionary<Vector2Int, GameObject> Chunks => _chunks;

        internal readonly Dictionary<Vector2Int, GameObject> _chunks = new();
        internal readonly List<Material> _materials = new();

        public void UpdateVisibleChunks(Vector3 focus, int radius = 3)
        {
            var center = new Vector2Int(
                Mathf.FloorToInt(focus.x / TianyongMapDefinition.ChunkSize),
                Mathf.FloorToInt(focus.z / TianyongMapDefinition.ChunkSize));
            foreach (var pair in _chunks)
            {
                var visible = Mathf.Abs(pair.Key.x - center.x) <= radius &&
                              Mathf.Abs(pair.Key.y - center.y) <= radius;
                if (pair.Value.activeSelf != visible)
                    pair.Value.SetActive(visible);
            }
        }

        public void Dispose()
        {
            DestroyObject(Root);
            foreach (var material in _materials)
                DestroyObject(material);
            _materials.Clear();
            _chunks.Clear();
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }

    /// <summary>
    /// Builds a 400 x 300 world-space, chunked, collider-backed Tianyong town.
    /// The output is geometry rather than a screen-sized backdrop, so camera
    /// zoom and player movement do not depend on texture resolution.
    /// </summary>
    public static class TianyongMapBuilder
    {
        private const string TextureRoot = "World/Tianyong/Textures/";
        /// <summary>Name of each chunk's walkable floor box (top at y = -0.05).</summary>
        public const string GroundColliderName = "Ground";

        private sealed class Palette
        {
            public Material Grass;
            public Material Stone;
            public Material Water;
            public Material Timber;
            public Material Roof;
            public Material Plaster;
            public Material Gold;
            public Material Red;
            public Material DarkStone;
            public Material Blossom;
            public Material Snow;
            public Material Lantern;
        }

        public static TianyongMapInstance Build(Transform parent, TianyongTheme theme)
            => Build(parent, theme, TianyongMapConfig.LoadDefault());

        public static TianyongMapInstance Build(
            Transform parent,
            TianyongTheme theme,
            TianyongMapConfig config)
        {
            var root = new GameObject($"[TianyongCity:{theme}]");
            root.transform.SetParent(parent, false);

            // The painted city walks on a mask derived from the artwork; the
            // procedural town's building footprints only apply to 3D themes.
            var painted = TianyongPaintedCity.IsEnabledFor(theme, config);
            var navigation = painted
                ? TianyongPaintedCity.CreateNavigation()
                : new TianyongNavigationGrid();
            var instance = new TianyongMapInstance(root, theme, navigation);
            var palette = CreatePalette(instance, theme, config);

            CreateChunks(instance, palette);
            CreateRoads(instance, palette);
            CreateCanalAndBridges(instance, palette);
            CreateCityWalls(instance, palette, theme);
            CreateCentralPlaza(instance, palette, theme);

            foreach (var spec in TianyongMapDefinition.Buildings)
                CreateBuilding(instance, palette, spec, theme);

            CreateMartialArena(instance, palette, theme);
            CreateMarket(instance, palette, theme);
            CreateTrees(instance, palette, theme);
            CreateLanternRows(instance, palette, theme);

            // Painted main city: the 3D town stays authoritative for physics
            // and navigation but hands its visuals to the city artwork.
            // If the artwork is missing the 3D town stays visible, so its
            // building footprints must drive navigation again.
            if (painted && !TianyongPaintedCity.Apply(instance))
                instance.Navigation = new TianyongNavigationGrid();

            instance.UpdateVisibleChunks(
                TianyongMapDefinition.DefaultSpawn,
                config?.VisibleChunkRadius ?? 3);
            return instance;
        }

        private static Palette CreatePalette(
            TianyongMapInstance instance,
            TianyongTheme theme,
            TianyongMapConfig config)
        {
            var night = theme == TianyongTheme.Lantern;
            var snow = theme == TianyongTheme.Snow;
            var palette = new Palette
            {
                Grass = Material(instance, "Grass", config?.GetBaseMaterial(TianyongMaterialSlot.Grass), LoadTexture("grass_ground"),
                    snow ? new Color(0.78f, 0.86f, 0.83f) : new Color(0.72f, 0.90f, 0.62f), 0.05f),
                Stone = Material(instance, "Stone", config?.GetBaseMaterial(TianyongMaterialSlot.Stone), LoadTexture("stone_paving"),
                    night ? new Color(0.56f, 0.62f, 0.68f) : Color.white, 0.08f),
                Water = Material(instance, "Water", config?.GetBaseMaterial(TianyongMaterialSlot.Water), LoadTexture("jade_water"),
                    night ? new Color(0.18f, 0.52f, 0.58f) : new Color(0.52f, 0.95f, 0.94f), 0.72f),
                Timber = Material(instance, "Timber", config?.GetBaseMaterial(TianyongMaterialSlot.Timber), LoadTexture("red_timber_wall"),
                    night ? new Color(0.62f, 0.35f, 0.26f) : new Color(0.92f, 0.70f, 0.54f), 0.18f),
                Roof = Material(instance, "Roof", config?.GetBaseMaterial(TianyongMaterialSlot.Roof), LoadTexture("roof_tiles_teal"),
                    night ? new Color(0.28f, 0.47f, 0.55f) : new Color(0.58f, 0.86f, 0.91f), 0.36f),
                Plaster = Material(instance, "Plaster", config?.GetBaseMaterial(TianyongMaterialSlot.Neutral), null,
                    night ? new Color(0.65f, 0.61f, 0.52f) : new Color(0.96f, 0.91f, 0.77f), 0.05f),
                Gold = Material(instance, "Gold", config?.GetBaseMaterial(TianyongMaterialSlot.Neutral), null, new Color(0.95f, 0.65f, 0.16f), 0.62f, 0.48f),
                Red = Material(instance, "FestivalRed", config?.GetBaseMaterial(TianyongMaterialSlot.Neutral), null, new Color(0.84f, 0.12f, 0.06f), 0.25f),
                DarkStone = Material(instance, "DarkStone", config?.GetBaseMaterial(TianyongMaterialSlot.Neutral), null, new Color(0.28f, 0.31f, 0.34f), 0.12f),
                Blossom = Material(instance, "Blossom", config?.GetBaseMaterial(TianyongMaterialSlot.Neutral), null, new Color(1f, 0.42f, 0.56f), 0.12f),
                Snow = Material(instance, "Snow", config?.GetBaseMaterial(TianyongMaterialSlot.Neutral), null, new Color(0.91f, 0.97f, 1f), 0.18f),
                Lantern = Material(instance, "Lantern", config?.GetBaseMaterial(TianyongMaterialSlot.Neutral), null, new Color(1f, 0.29f, 0.05f), 0.3f,
                    emission: night ? new Color(1f, 0.18f, 0.02f) * 2.2f : new Color(0.5f, 0.03f, 0f)),
            };
            return palette;
        }

        private static void CreateChunks(TianyongMapInstance instance, Palette palette)
        {
            var countX = Mathf.CeilToInt(TianyongMapDefinition.Width / TianyongMapDefinition.ChunkSize);
            var countZ = Mathf.CeilToInt(TianyongMapDefinition.Depth / TianyongMapDefinition.ChunkSize);
            for (var x = 0; x < countX; x++)
            for (var z = 0; z < countZ; z++)
            {
                var key = new Vector2Int(x, z);
                var chunk = new GameObject($"Chunk_{x}_{z}");
                chunk.transform.SetParent(instance.Root.transform, false);
                instance._chunks[key] = chunk;

                var width = Mathf.Min(TianyongMapDefinition.ChunkSize,
                    TianyongMapDefinition.Width - x * TianyongMapDefinition.ChunkSize);
                var depth = Mathf.Min(TianyongMapDefinition.ChunkSize,
                    TianyongMapDefinition.Depth - z * TianyongMapDefinition.ChunkSize);
                var center = new Vector3(
                    x * TianyongMapDefinition.ChunkSize + width * 0.5f,
                    -0.55f,
                    z * TianyongMapDefinition.ChunkSize + depth * 0.5f);
                CreateBox(GroundColliderName, chunk.transform, center, new Vector3(width, 1f, depth), palette.Grass);
            }
        }

        private static void CreateRoads(TianyongMapInstance instance, Palette palette)
        {
            foreach (var road in TianyongMapDefinition.Roads)
                CreateRectPatches(instance, road, 0.04f, 0.12f, palette.Stone, "Road");
        }

        private static void CreateCanalAndBridges(TianyongMapInstance instance, Palette palette)
        {
            CreateRectPatches(instance, TianyongMapDefinition.Canal, 0.06f, 0.10f, palette.Water, "Canal", false);
            foreach (var bridge in TianyongMapDefinition.Bridges)
            {
                var parent = ChunkFor(instance, bridge.center);
                var deck = CreateBox("StoneBridge", parent,
                    new Vector3(bridge.center.x, 0.18f, bridge.center.y),
                    new Vector3(bridge.width, 0.28f, bridge.height), palette.Stone);
                deck.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                CreateBox("BridgeRailL", parent,
                    new Vector3(bridge.xMin + 0.6f, 0.75f, bridge.center.y),
                    new Vector3(0.7f, 1.1f, bridge.height), palette.Plaster);
                CreateBox("BridgeRailR", parent,
                    new Vector3(bridge.xMax - 0.6f, 0.75f, bridge.center.y),
                    new Vector3(0.7f, 1.1f, bridge.height), palette.Plaster);
            }
        }

        private static void CreateCityWalls(TianyongMapInstance instance, Palette palette, TianyongTheme theme)
        {
            CreateWallSegment(instance, palette, new Rect(4f, 2f, 166f, 5f));
            CreateWallSegment(instance, palette, new Rect(230f, 2f, 166f, 5f));
            CreateWallSegment(instance, palette, new Rect(4f, 293f, 166f, 5f));
            CreateWallSegment(instance, palette, new Rect(230f, 293f, 166f, 5f));
            CreateWallSegment(instance, palette, new Rect(2f, 4f, 5f, 121f));
            CreateWallSegment(instance, palette, new Rect(2f, 175f, 5f, 121f));
            CreateWallSegment(instance, palette, new Rect(393f, 4f, 5f, 121f));
            CreateWallSegment(instance, palette, new Rect(393f, 175f, 5f, 121f));

            CreateGate(instance, palette, new Vector3(200f, 0f, 4f), 0f, "NorthGate", theme);
            CreateGate(instance, palette, new Vector3(200f, 0f, 296f), 180f, "SouthGate", theme);
            CreateGate(instance, palette, new Vector3(4f, 0f, 150f), 90f, "WestGate", theme);
            CreateGate(instance, palette, new Vector3(396f, 0f, 150f), -90f, "EastGate", theme);
        }

        private static void CreateWallSegment(TianyongMapInstance instance, Palette palette, Rect rect)
        {
            CreateRectPatches(instance, rect, 4f, 8f, palette.DarkStone, "CityWall");
        }

        private static void CreateGate(
            TianyongMapInstance instance,
            Palette palette,
            Vector3 position,
            float yaw,
            string name,
            TianyongTheme theme)
        {
            var parent = ChunkFor(instance, new Vector2(position.x, position.z));
            var gate = new GameObject(name);
            gate.transform.SetParent(parent, false);
            gate.transform.localPosition = position;
            gate.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            CreateBox("PostL", gate.transform, new Vector3(-13f, 5f, 0f), new Vector3(6f, 10f, 7f), palette.Timber);
            CreateBox("PostR", gate.transform, new Vector3(13f, 5f, 0f), new Vector3(6f, 10f, 7f), palette.Timber);
            CreateBox("GateBeam", gate.transform, new Vector3(0f, 10.5f, 0f), new Vector3(32f, 4f, 8f), palette.Plaster);
            CreateRoof(gate.transform, palette, 34f, 10f, 13f, theme == TianyongTheme.Snow);
            CreateLantern(gate.transform, palette, new Vector3(-8f, 8f, -4.5f), theme, true);
            CreateLantern(gate.transform, palette, new Vector3(8f, 8f, -4.5f), theme, true);
        }

        private static void CreateCentralPlaza(TianyongMapInstance instance, Palette palette, TianyongTheme theme)
        {
            var parent = ChunkFor(instance, new Vector2(200f, 150f));
            var plaza = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plaza.name = "CentralPlaza";
            plaza.transform.SetParent(parent, false);
            plaza.transform.localPosition = new Vector3(200f, 0.12f, 150f);
            plaza.transform.localScale = new Vector3(29f, 0.12f, 29f);
            plaza.GetComponent<Renderer>().sharedMaterial = palette.Stone;

            var fountain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fountain.name = "FestivalFountain";
            fountain.transform.SetParent(parent, false);
            fountain.transform.localPosition = new Vector3(200f, 0.6f, 150f);
            fountain.transform.localScale = new Vector3(5f, 0.6f, 5f);
            fountain.GetComponent<Renderer>().sharedMaterial = palette.Gold;
            RemoveCollider(fountain);

            if (theme == TianyongTheme.Snow)
                CreateBox("PlazaSnow", parent, new Vector3(200f, 0.27f, 150f), new Vector3(18f, 0.08f, 4f), palette.Snow, false);
        }

        private static void CreateBuilding(
            TianyongMapInstance instance,
            Palette palette,
            TianyongBuildingSpec spec,
            TianyongTheme theme)
        {
            var parent = ChunkFor(instance, new Vector2(spec.Position.x, spec.Position.z));
            if (spec.Kind == TianyongBuildingKind.Pagoda)
            {
                CreatePagoda(parent, palette, spec, theme);
                return;
            }

            var root = new GameObject(spec.Name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = spec.Position;
            root.transform.localRotation = Quaternion.Euler(0f, spec.Yaw, 0f);

            CreateBox("Foundation", root.transform, new Vector3(0f, 0.45f, 0f),
                new Vector3(spec.Size.x + 2f, 0.9f, spec.Size.z + 2f), palette.DarkStone);
            CreateBox("Body", root.transform, new Vector3(0f, spec.Size.y * 0.5f, 0f),
                new Vector3(spec.Size.x, spec.Size.y, spec.Size.z), palette.Timber);

            var upper = spec.Kind is TianyongBuildingKind.Inn or TianyongBuildingKind.Bank or TianyongBuildingKind.Government;
            if (upper)
            {
                CreateBox("UpperBody", root.transform, new Vector3(0f, spec.Size.y + 3.2f, 0f),
                    new Vector3(spec.Size.x * 0.82f, 6f, spec.Size.z * 0.82f), palette.Plaster);
                CreateRoof(root.transform, palette, spec.Size.x * 0.9f, spec.Size.z * 0.92f,
                    spec.Size.y + 6.3f, theme == TianyongTheme.Snow);
            }
            else
            {
                CreateRoof(root.transform, palette, spec.Size.x + 2f, spec.Size.z + 2f,
                    spec.Size.y + 0.5f, theme == TianyongTheme.Snow);
            }

            AddBuildingColumns(root.transform, palette, spec.Size.x, spec.Size.z, spec.Size.y);
            CreateLantern(root.transform, palette, new Vector3(-spec.Size.x * 0.28f, spec.Size.y * 0.65f, -spec.Size.z * 0.52f), theme, false);
            CreateLantern(root.transform, palette, new Vector3(spec.Size.x * 0.28f, spec.Size.y * 0.65f, -spec.Size.z * 0.52f), theme, false);
        }

        private static void CreatePagoda(
            Transform parent,
            Palette palette,
            TianyongBuildingSpec spec,
            TianyongTheme theme)
        {
            var root = new GameObject(spec.Name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = spec.Position;
            for (var tier = 0; tier < 4; tier++)
            {
                var scale = 1f - tier * 0.14f;
                var y = 1f + tier * 6f;
                CreateBox($"TierBody{tier}", root.transform, new Vector3(0f, y + 2.5f, 0f),
                    new Vector3(spec.Size.x * scale, 5f, spec.Size.z * scale), palette.Timber);
                CreateRoof(root.transform, palette, spec.Size.x * scale + 4f, spec.Size.z * scale + 4f,
                    y + 5.2f, theme == TianyongTheme.Snow, $"TierRoof{tier}");
            }
            CreateBox("PagodaSpire", root.transform, new Vector3(0f, 28f, 0f), new Vector3(1f, 8f, 1f), palette.Gold, false);
        }

        private static void CreateRoof(
            Transform parent,
            Palette palette,
            float width,
            float depth,
            float y,
            bool snow,
            string name = "Roof")
        {
            var roof = new GameObject(name);
            roof.transform.SetParent(parent, false);
            roof.transform.localPosition = new Vector3(0f, y, 0f);

            var panelWidth = width * 0.58f;
            var rise = width * 0.13f;
            var left = CreateBox("RoofL", roof.transform,
                new Vector3(-width * 0.22f, rise * 0.5f, 0f),
                new Vector3(panelWidth, 0.7f, depth), palette.Roof);
            left.transform.localRotation = Quaternion.Euler(0f, 0f, -24f);
            var right = CreateBox("RoofR", roof.transform,
                new Vector3(width * 0.22f, rise * 0.5f, 0f),
                new Vector3(panelWidth, 0.7f, depth), palette.Roof);
            right.transform.localRotation = Quaternion.Euler(0f, 0f, 24f);

            if (!snow) return;
            var snowL = CreateBox("SnowL", roof.transform,
                new Vector3(-width * 0.22f, rise * 0.5f + 0.42f, 0f),
                new Vector3(panelWidth * 0.92f, 0.12f, depth * 0.92f), palette.Snow, false);
            snowL.transform.localRotation = Quaternion.Euler(0f, 0f, -24f);
            var snowR = CreateBox("SnowR", roof.transform,
                new Vector3(width * 0.22f, rise * 0.5f + 0.42f, 0f),
                new Vector3(panelWidth * 0.92f, 0.12f, depth * 0.92f), palette.Snow, false);
            snowR.transform.localRotation = Quaternion.Euler(0f, 0f, 24f);
        }

        private static void AddBuildingColumns(Transform parent, Palette palette, float width, float depth, float height)
        {
            var x = width * 0.43f;
            var z = depth * 0.47f;
            foreach (var px in new[] { -x, x })
            foreach (var pz in new[] { -z, z })
                CreateBox("Column", parent, new Vector3(px, height * 0.46f, pz),
                    new Vector3(0.9f, height * 0.9f, 0.9f), palette.Red, false);
        }

        private static void CreateMartialArena(TianyongMapInstance instance, Palette palette, TianyongTheme theme)
        {
            var parent = ChunkFor(instance, new Vector2(341f, 147f));
            CreateBox("MartialArena", parent, new Vector3(341f, 1.2f, 147f),
                new Vector3(52f, 2.4f, 45f), palette.Red);
            CreateBox("ArenaInset", parent, new Vector3(341f, 2.43f, 147f),
                new Vector3(45f, 0.08f, 38f), palette.Gold, false);
            for (var x = 318f; x <= 364f; x += 15.5f)
            {
                CreateLantern(parent, palette, new Vector3(x, 5f, 123f), theme, true);
                CreateLantern(parent, palette, new Vector3(x, 5f, 171f), theme, true);
            }
        }

        private static void CreateMarket(TianyongMapInstance instance, Palette palette, TianyongTheme theme)
        {
            var count = theme == TianyongTheme.Market ? 18 : 8;
            var random = new System.Random(112233 + (int)theme);
            for (var i = 0; i < count; i++)
            {
                var side = i % 2 == 0 ? -1f : 1f;
                var x = 96f + (float)random.NextDouble() * 65f;
                var z = 146f + side * (19f + (float)random.NextDouble() * 18f);
                CreateStall(ChunkFor(instance, new Vector2(x, z)), palette, new Vector3(x, 0f, z), i);
            }
        }

        private static void CreateStall(Transform parent, Palette palette, Vector3 position, int index)
        {
            var root = new GameObject($"MarketStall{index:00}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            CreateBox("Counter", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(5f, 1.8f, 2.5f), palette.Timber, false);
            CreateBox("Canopy", root.transform, new Vector3(0f, 3.4f, 0f), new Vector3(6f, 0.35f, 3.5f),
                index % 2 == 0 ? palette.Red : palette.Gold, false);
            CreateBox("PoleL", root.transform, new Vector3(-2.3f, 2f, 0f), new Vector3(0.25f, 4f, 0.25f), palette.DarkStone, false);
            CreateBox("PoleR", root.transform, new Vector3(2.3f, 2f, 0f), new Vector3(0.25f, 4f, 0.25f), palette.DarkStone, false);
        }

        private static void CreateTrees(TianyongMapInstance instance, Palette palette, TianyongTheme theme)
        {
            var positions = new[]
            {
                new Vector2(18f, 130f), new Vector2(52f, 118f), new Vector2(105f, 58f),
                new Vector2(156f, 57f), new Vector2(238f, 55f), new Vector2(300f, 110f),
                new Vector2(382f, 116f), new Vector2(382f, 184f), new Vector2(298f, 196f),
                new Vector2(237f, 243f), new Vector2(164f, 242f), new Vector2(54f, 254f),
                new Vector2(22f, 166f), new Vector2(146f, 200f), new Vector2(250f, 116f),
            };
            for (var i = 0; i < positions.Length; i++)
                CreateTree(ChunkFor(instance, positions[i]), palette,
                    new Vector3(positions[i].x, 0f, positions[i].y), theme, i % 3 == 0);
        }

        private static void CreateTree(Transform parent, Palette palette, Vector3 position, TianyongTheme theme, bool blossom)
        {
            var root = new GameObject(blossom ? "PlumTree" : "WillowTree");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            trunk.transform.localScale = new Vector3(0.7f, 2.5f, 0.7f);
            trunk.GetComponent<Renderer>().sharedMaterial = palette.Timber;
            RemoveCollider(trunk);

            var crownMaterial = blossom ? palette.Blossom : palette.Grass;
            foreach (var offset in new[]
                     {
                         new Vector3(0f, 6.5f, 0f), new Vector3(2f, 6f, 0.8f),
                         new Vector3(-1.7f, 5.8f, -0.8f), new Vector3(0.4f, 7.5f, -1.2f),
                     })
            {
                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Crown";
                crown.transform.SetParent(root.transform, false);
                crown.transform.localPosition = offset;
                crown.transform.localScale = new Vector3(4.2f, 3.1f, 4.2f);
                crown.GetComponent<Renderer>().sharedMaterial = crownMaterial;
                RemoveCollider(crown);
            }

            if (theme == TianyongTheme.Snow)
                CreateBox("TreeSnow", root.transform, new Vector3(0f, 8.1f, 0f), new Vector3(5f, 0.25f, 4f), palette.Snow, false);
        }

        private static void CreateLanternRows(TianyongMapInstance instance, Palette palette, TianyongTheme theme)
        {
            for (var z = 26f; z < 258f; z += 24f)
            {
                CreateLantern(ChunkFor(instance, new Vector2(181f, z)), palette, new Vector3(181f, 5f, z), theme, true);
                CreateLantern(ChunkFor(instance, new Vector2(219f, z)), palette, new Vector3(219f, 5f, z), theme, true);
            }
            for (var x = 23f; x < 389f; x += 31f)
            {
                CreateLantern(ChunkFor(instance, new Vector2(x, 132f)), palette, new Vector3(x, 5f, 132f), theme, true);
                CreateLantern(ChunkFor(instance, new Vector2(x, 168f)), palette, new Vector3(x, 5f, 168f), theme, true);
            }
        }

        private static void CreateLantern(Transform parent, Palette palette, Vector3 position, TianyongTheme theme, bool pole)
        {
            var root = new GameObject("FestivalLantern");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            if (pole)
                CreateBox("Pole", root.transform, new Vector3(0f, -2.3f, 0f), new Vector3(0.3f, 5f, 0.3f), palette.DarkStone, false);

            var lantern = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lantern.name = "Lantern";
            lantern.transform.SetParent(root.transform, false);
            lantern.transform.localScale = new Vector3(1.1f, 1.35f, 1.1f);
            lantern.GetComponent<Renderer>().sharedMaterial = palette.Lantern;
            RemoveCollider(lantern);

            if (theme != TianyongTheme.Lantern || !pole) return;
            var lightHash = Mathf.Abs(Mathf.RoundToInt(position.x * 3f + position.z * 5f));
            if (lightHash % 4 != 0) return;
            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.32f, 0.08f);
            light.range = 12f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
        }

        private static void CreateRectPatches(
            TianyongMapInstance instance,
            Rect rect,
            float y,
            float thickness,
            Material material,
            string name,
            bool collider = true)
        {
            var minChunkX = Mathf.FloorToInt(rect.xMin / TianyongMapDefinition.ChunkSize);
            var maxChunkX = Mathf.FloorToInt((rect.xMax - 0.001f) / TianyongMapDefinition.ChunkSize);
            var minChunkZ = Mathf.FloorToInt(rect.yMin / TianyongMapDefinition.ChunkSize);
            var maxChunkZ = Mathf.FloorToInt((rect.yMax - 0.001f) / TianyongMapDefinition.ChunkSize);
            for (var x = minChunkX; x <= maxChunkX; x++)
            for (var z = minChunkZ; z <= maxChunkZ; z++)
            {
                var chunkRect = new Rect(
                    x * TianyongMapDefinition.ChunkSize,
                    z * TianyongMapDefinition.ChunkSize,
                    TianyongMapDefinition.ChunkSize,
                    TianyongMapDefinition.ChunkSize);
                var overlap = Intersect(rect, chunkRect);
                if (overlap.width <= 0f || overlap.height <= 0f) continue;
                var parent = instance._chunks[new Vector2Int(x, z)].transform;
                CreateBox(name, parent,
                    new Vector3(overlap.center.x, y, overlap.center.y),
                    new Vector3(overlap.width, thickness, overlap.height), material, collider);
            }
        }

        private static Transform ChunkFor(TianyongMapInstance instance, Vector2 point)
        {
            var maxX = Mathf.CeilToInt(TianyongMapDefinition.Width / TianyongMapDefinition.ChunkSize) - 1;
            var maxZ = Mathf.CeilToInt(TianyongMapDefinition.Depth / TianyongMapDefinition.ChunkSize) - 1;
            var key = new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(point.x / TianyongMapDefinition.ChunkSize), 0, maxX),
                Mathf.Clamp(Mathf.FloorToInt(point.y / TianyongMapDefinition.ChunkSize), 0, maxZ));
            return instance._chunks[key].transform;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            var xMin = Mathf.Max(a.xMin, b.xMin);
            var yMin = Mathf.Max(a.yMin, b.yMin);
            var xMax = Mathf.Min(a.xMax, b.xMax);
            var yMax = Mathf.Min(a.yMax, b.yMax);
            return xMax <= xMin || yMax <= yMin ? new Rect() : Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) RemoveCollider(go);
            return go;
        }

        private static Texture2D LoadTexture(string name) => Resources.Load<Texture2D>(TextureRoot + name);

        private static Material Material(
            TianyongMapInstance instance,
            string name,
            Material baseMaterial,
            Texture texture,
            Color color,
            float smoothness,
            float metallic = 0f,
            Color? emission = null)
        {
            var shader = baseMaterial != null
                ? baseMaterial.shader
                : Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Texture");
            var material = baseMaterial != null ? new Material(baseMaterial) : new Material(shader);
            material.name = "Tianyong_" + name;
            material.color = color;
            if (texture != null) material.mainTexture = texture;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (texture != null) material.mainTextureScale = new Vector2(3f, 3f);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emission.Value);
            }
            instance._materials.Add(material);
            return material;
        }

        private static void RemoveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
            else UnityEngine.Object.DestroyImmediate(collider);
        }
    }
}
