namespace MmorpgClient.Game.Appearance
{
    /// <summary>
    /// 批次 B3 无焦虑扩容便利与帮会定制徽记资源索引（纯 C#，零 UnityEngine 依赖，WebGL2/IL2CPP 安全）。
    /// 所有纹理均已携 .meta 导入 Assets/Resources/UI/Ugui/{UtilityExpandV1,GuildEmblemsV1}/。
    /// </summary>
    public static class UtilityExpandAndGuildEmblemCatalog
    {
        public const string WarehouseExpandIconPath = "UI/Ugui/UtilityExpandV1/icon_warehouse_expand";
        public const string PetSquadPresetIconPath = "UI/Ugui/UtilityExpandV1/icon_pet_squad_preset";
        public const string ReplayBookmarkPinIconPath = "UI/Ugui/UtilityExpandV1/icon_replay_bookmark_pin";

        public readonly struct GuildEmblemEntry
        {
            public readonly uint EmblemId;
            public readonly string Name;
            public readonly ulong PriceDiamond;
            public readonly string ResourcePath;

            public GuildEmblemEntry(uint emblemId, string name, ulong priceDiamond, string resourcePath)
            {
                EmblemId = emblemId;
                Name = name;
                PriceDiamond = priceDiamond;
                ResourcePath = resourcePath;
            }
        }

        public static readonly GuildEmblemEntry[] GuildEmblems =
        {
            new GuildEmblemEntry(1u, "玄铁战盾", 180ul, "UI/Ugui/GuildEmblemsV1/guild_emblem_01"),
            new GuildEmblemEntry(2u, "青莲雅印", 180ul, "UI/Ugui/GuildEmblemsV1/guild_emblem_02"),
            new GuildEmblemEntry(3u, "星宿罗盘", 180ul, "UI/Ugui/GuildEmblemsV1/guild_emblem_03"),
            new GuildEmblemEntry(4u, "朱雀焚羽", 300ul, "UI/Ugui/GuildEmblemsV1/guild_emblem_04"),
            new GuildEmblemEntry(5u, "金龙盘柱", 300ul, "UI/Ugui/GuildEmblemsV1/guild_emblem_05"),
            new GuildEmblemEntry(6u, "太极阴阳", 300ul, "UI/Ugui/GuildEmblemsV1/guild_emblem_06"),
        };

        public static bool IsEmblemUnlocked(ulong unlockedBits, uint emblemId)
        {
            if (emblemId == 0u) return true;
            if (emblemId > 6u) return false;
            return (unlockedBits & (1ul << (int)(emblemId - 1u))) != 0ul;
        }

        public static string GetEmblemResourcePath(uint emblemId)
        {
            if (emblemId >= 1u && emblemId <= (uint)GuildEmblems.Length)
                return GuildEmblems[emblemId - 1u].ResourcePath;
            return string.Empty;
        }
    }
}
