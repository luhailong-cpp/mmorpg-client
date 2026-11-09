namespace MmorpgClient.World
{
    public static class QdaoRunVariants
    {
        public const string CurrentRoot = "World/Characters/QdaoRunVariants20261105/current";

        /// <summary>只允许道童和本次保留的十一名角色，历史别名须由现有角色目录解析。</summary>
        public static bool AllowsCharacter(string id)
        {
            switch (id)
            {
                case "00_reference_topright_boy":
                case "01_ice_sword_girl":
                case "02_fire_talisman_boy":
                case "03_lotus_healer_girl":
                case "04_mountain_guardian_boy":
                case "05_celestial_musician_girl":
                case "06_thunder_caster_boy":
                case "08_alchemy_prodigy_boy":
                case "09_bamboo_archer_girl":
                case "10_crimson_spear_girl":
                case "15_water_dragon_scholar_boy":
                case "17_ghost_script_calligrapher_boy":
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
