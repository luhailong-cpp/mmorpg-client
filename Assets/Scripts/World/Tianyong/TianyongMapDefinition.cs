using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>Four Spring-Festival art directions sharing one authoritative town layout.</summary>
    public enum TianyongTheme
    {
        City = 0,
        Market = 1,
        Snow = 2,
        Lantern = 3,
    }

    public enum TianyongBuildingKind
    {
        Residence,
        Shop,
        Inn,
        Bank,
        Government,
        Temple,
        Pagoda,
    }

    [Serializable]
    public readonly struct TianyongBuildingSpec
    {
        public TianyongBuildingSpec(
            string name,
            TianyongBuildingKind kind,
            float x,
            float z,
            float width,
            float depth,
            float height,
            float yaw = 0f)
        {
            Name = name;
            Kind = kind;
            Position = new Vector3(x, 0f, z);
            Size = new Vector3(width, height, depth);
            Yaw = yaw;
        }

        public string Name { get; }
        public TianyongBuildingKind Kind { get; }
        public Vector3 Position { get; }
        public Vector3 Size { get; }
        public float Yaw { get; }

        public Rect Footprint(float padding = 0f)
            => new Rect(
                Position.x - Size.x * 0.5f - padding,
                Position.z - Size.z * 0.5f - padding,
                Size.x + padding * 2f,
                Size.z + padding * 2f);
    }

    /// <summary>
    /// Authoritative client-side Tianyong layout.  The 400 x 300 coordinate
    /// range mirrors the scale of the classic town map (NPC coordinates reach
    /// roughly x=390 / y=280), rather than the dimensions of any screen image.
    /// The layout is authored in Unity X/Z. Network conversion is centralized
    /// in WorldCoordinateConverter: server X maps to Unity Z and server Y maps
    /// to Unity X, so the corresponding server extents are Depth/Width.
    /// </summary>
    public static class TianyongMapDefinition
    {
        public const float Width = 400f;
        public const float Depth = 300f;
        public const float ChunkSize = 50f;
        public const float NavigationCellSize = 4f;
        public const uint DefaultSceneConfigId = 1;

        public const float ServerXExtent = Depth;
        public const float ServerYExtent = Width;

        // Kept on the central avenue, well clear of the plaza fountain.
        public static readonly Vector3 DefaultSpawn = new(200f, 0f, 180f);

        public static readonly Rect[] Roads =
        {
            new(188f, 0f, 24f, 292f),
            new(0f, 138f, 400f, 24f),
            new(73f, 8f, 16f, 284f),
            new(311f, 8f, 16f, 284f),
            new(8f, 67f, 384f, 15f),
            new(8f, 221f, 384f, 15f),
            new(154f, 107f, 92f, 86f),
        };

        public static readonly Rect Canal = new(8f, 264f, 384f, 22f);

        // Includes comfortable actor clearance around the visible fountain.
        public static readonly Rect FestivalFountainFootprint = new(195f, 145f, 10f, 10f);

        public static readonly Rect[] Bridges =
        {
            new(70f, 261f, 24f, 28f),
            new(188f, 261f, 24f, 28f),
            new(307f, 261f, 24f, 28f),
        };

        public static readonly IReadOnlyList<TianyongBuildingSpec> Buildings =
            new TianyongBuildingSpec[]
            {
                // North-west: yamen, prison and old Taoist quarter.
                new("NorthWestGateHouse", TianyongBuildingKind.Government, 33f, 24f, 34f, 24f, 12f),
                new("GovernorYamen", TianyongBuildingKind.Government, 118f, 35f, 46f, 30f, 14f),
                new("CityPrison", TianyongBuildingKind.Government, 35f, 88f, 30f, 24f, 10f),
                new("WuyunTemple", TianyongBuildingKind.Temple, 143f, 35f, 34f, 25f, 13f),
                new("NorthWestResidence", TianyongBuildingKind.Residence, 112f, 94f, 28f, 20f, 9f),
                new("NorthWestShop", TianyongBuildingKind.Shop, 151f, 91f, 27f, 20f, 9f),

                // North-east: wealthy commercial district and the tower landmark.
                new("NorthResidence", TianyongBuildingKind.Residence, 247f, 28f, 28f, 20f, 9f),
                new("NorthTeaHouse", TianyongBuildingKind.Shop, 286f, 34f, 31f, 22f, 10f),
                new("TongtianTower", TianyongBuildingKind.Pagoda, 340f, 40f, 27f, 27f, 25f),
                new("NorthEastTemple", TianyongBuildingKind.Temple, 374f, 35f, 30f, 23f, 12f),
                new("FujiaBank", TianyongBuildingKind.Bank, 257f, 94f, 44f, 31f, 15f),
                new("XilaiInnNorth", TianyongBuildingKind.Inn, 356f, 94f, 47f, 30f, 14f),

                // Around the central plaza; keep the 90 x 86 plaza itself open.
                new("WestWeaponShop", TianyongBuildingKind.Shop, 123f, 127f, 29f, 22f, 10f),
                new("WestMedicineShop", TianyongBuildingKind.Shop, 123f, 176f, 29f, 22f, 10f),
                new("EastClothShop", TianyongBuildingKind.Shop, 274f, 126f, 30f, 22f, 10f),
                new("EastCraftShop", TianyongBuildingKind.Shop, 274f, 178f, 30f, 22f, 10f),

                // South-west market and courier quarter.
                new("EscortAgency", TianyongBuildingKind.Government, 31f, 192f, 35f, 25f, 11f),
                new("XilaiInnSouth", TianyongBuildingKind.Inn, 119f, 198f, 47f, 31f, 14f),
                new("SouthWestShop", TianyongBuildingKind.Shop, 34f, 243f, 28f, 19f, 9f),
                new("SouthMarketHall", TianyongBuildingKind.Shop, 126f, 246f, 38f, 23f, 11f),

                // South-east residence and guild quarter.
                new("GuildHall", TianyongBuildingKind.Government, 268f, 202f, 44f, 30f, 14f),
                new("SouthEastTemple", TianyongBuildingKind.Temple, 366f, 197f, 34f, 25f, 13f),
                new("SouthEastShop", TianyongBuildingKind.Shop, 269f, 245f, 31f, 21f, 10f),
                new("SouthEastResidence", TianyongBuildingKind.Residence, 365f, 245f, 31f, 22f, 10f),
            };

        public static bool ContainsXZ(Vector3 world, float margin = 0f)
            => world.x >= margin && world.x <= Width - margin &&
               world.z >= margin && world.z <= Depth - margin;

        public static Vector3 ClampXZ(Vector3 world, float margin = 2f)
        {
            world.x = Mathf.Clamp(world.x, margin, Width - margin);
            world.z = Mathf.Clamp(world.z, margin, Depth - margin);
            return world;
        }
    }
}
