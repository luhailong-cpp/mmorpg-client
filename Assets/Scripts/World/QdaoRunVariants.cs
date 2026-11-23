namespace MmorpgClient.World
{
    public static class QdaoRunVariants
    {
        public const string CurrentRoot = "World/Characters/QdaoRunVariants20261105/current";

        // 与 current8 逐方向跑步校准对齐的静止显示倍率：不改 V13/V14 原图与 SHA 契约，由脚底锚点等比放大站立表现。
        public const float IdleDisplayScale = 1.15f;

        /// <summary>返回角色静止时的显示倍率；现役 8 名角色对齐当前跑步尺寸，其余角色保持 1。</summary>
        public static float ResolveIdleDisplayScale(string id) => AllowsCharacter(id) ? IdleDisplayScale : 1f;

        /// <summary>只允许道童和本次保留的七名角色，历史别名须由现有角色目录解析。</summary>
        public static bool AllowsCharacter(string id)
        {
            switch (id)
            {
                case "00_reference_topright_boy":
                case "01_ice_sword_girl":
                case "02_fire_talisman_boy":
                case "03_lotus_healer_girl":
                case "05_celestial_musician_girl":
                case "09_bamboo_archer_girl":
                case "10_crimson_spear_girl":
                case "15_water_dragon_scholar_boy":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>以完整周期比例切换帧数和时长；无效输入从零开始。此算法不依赖 Unity。</summary>
        public static float RemapSeconds(float elapsed, float oldDuration, float newDuration)
        {
            if (!Finite(elapsed) || elapsed < 0f || !Finite(oldDuration) || oldDuration <= 0f ||
                !Finite(newDuration) || newDuration <= 0f) return 0f;
            // 先取余再归一化，避免极大累计时间除以极小周期时溢出。
            var phase = ((double)elapsed % oldDuration) / oldDuration;
            var mapped = (float)(phase * newDuration);
            // 单精度舍入可能把接近末尾的相位舍入到完整周期，按闭环约定归零。
            return mapped >= newDuration ? 0f : mapped;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
