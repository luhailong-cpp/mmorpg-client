using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    using Transform = UnityEngine.Transform;

    /// <summary>
    /// 赛季玩法、首领试炼、战斗 HUD 与 +11 强化/两阶段材料总账的无字美术资源加载与纯 C# 映射工具。
    /// 严格对应 Assets/Resources/UI/Ugui/SeasonEconomyV1/（来源 D:\work\image\designs\season-economy-ui-20261124）：
    /// 只加载独立图标与无字控件，禁止运行时整图遮罩；拉伸边界严格遵循 CONTROL-USAGE.md。
    /// </summary>
    public static class SeasonEconomyUiArt
    {
        public const string IconsRoot = "UI/Ugui/SeasonEconomyV1/Icons/";
        public const string ControlsRoot = "UI/Ugui/SeasonEconomyV1/Controls/";
        public const string CraftQualityRoot = "UI/Ugui/CraftQualitySkill/";
        public const string CraftIconsRoot = "UI/Ugui/CraftQualitySkill/Icons/";
        public const string EconomyTradeRoot = "UI/Ugui/EconomyTradeV1/";

        // CONTROL-USAGE.md 实测：pity_bar_bg 为 535x88，视觉内槽为 [39, 26, 497, 62]（458x36）。
        private const float PityLeftRatio = 39f / 535f;
        private const float PityTopRatio = 26f / 88f;
        private const float PityInnerWidthRatio = 458f / 535f;
        private const float PityInnerHeightRatio = 36f / 88f;

        public static readonly Color GoldHighlight = new Color32(0xC4, 0x8A, 0x2C, 255);
        public static readonly Color GoldText = new Color32(0xFF, 0xF4, 0xD2, 255);
        public static readonly Color SoftWarnText = new Color32(0x8E, 0x4B, 0x28, 255);

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Cache.Clear();

        public static Sprite LoadIcon(string name) => LoadCached(IconsRoot + name);

        public static Sprite LoadControl(string name) => LoadCached(ControlsRoot + name);

        public static Sprite LoadCraftControl(string name) => LoadCached(CraftQualityRoot + name);

        public static Sprite LoadCraftIcon(string name) => LoadCached(CraftIconsRoot + name);

        public static Sprite LoadTradeSlice(string name) => LoadCached(EconomyTradeRoot + name);

        /// <summary>新增 3 本兵法成长战法（12020/12021/12022）的 512×512 专属技能图标键。</summary>
        public static string GrowthSkillIconKey(uint skillId) => skillId switch
        {
            12020u => "skill_yu_zhan_yu_yong",
            12021u => "skill_shen_mou_yuan_lv",
            12022u => "skill_bu_bu_wei_ying",
            _ => string.Empty,
        };

        public static Sprite LoadGrowthSkillIcon(uint skillId)
        {
            string key = GrowthSkillIconKey(skillId);
            return string.IsNullOrEmpty(key) ? null : LoadCraftIcon(key);
        }

        /// <summary>将装备词条 5 档品相映射为 CraftQualitySkill 无字品相底板键。</summary>
        public static string QualityTagKey(uint qualityTier) => qualityTier switch
        {
            1u => "quality_tag_1_common",
            2u => "quality_tag_2_good",
            3u => "quality_tag_3_fine",
            4u => "quality_tag_4_precious",
            >= 5u => "quality_tag_5_supreme",
            _ => string.Empty,
        };

        /// <summary>将铸器/制卷三档手艺映射为固定比例徽章键。</summary>
        public static string MasteryBadgeKey(uint masteryTier) => masteryTier switch
        {
            >= 3u => "mastery_badge_grandmaster",
            2u => "mastery_badge_artisan",
            _ => "mastery_badge_apprentice",
        };

        private static Sprite LoadCached(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var sprite = Resources.Load<Sprite>(path);
            if (sprite != null) Cache[path] = sprite;
            return sprite;
        }

        /// <summary>将战斗公开预告 IntentKind 映射为专属 256×256 无字意图图标键。</summary>
        public static string IntentIconKey(uint intentKind) => intentKind switch
        {
            1u => "intent_slow_cleave",
            2u => "intent_guard_heal",
            3u => "intent_stance_phys",
            4u => "intent_stance_magic",
            5u => "intent_guard_shield",
            6u => "intent_guard_bind",
            7u => "intent_magic_strike",
            8u => "intent_venom_aoe",
            _ => "intent_slow_cleave",
        };

        /// <summary>为四张首领试炼备战卡提供对应的双机制图标键（覆盖全部 8 件意图图标）。</summary>
        public static void BossCardIntentIcons(uint bossTrialKind, out string primaryIcon, out string secondaryIcon)
        {
            switch (bossTrialKind)
            {
                case 1u:
                    primaryIcon = "intent_slow_cleave";
                    secondaryIcon = "intent_guard_heal";
                    break;
                case 2u:
                    primaryIcon = "intent_stance_phys";
                    secondaryIcon = "intent_stance_magic";
                    break;
                case 3u:
                    primaryIcon = "intent_guard_shield";
                    secondaryIcon = "intent_magic_strike";
                    break;
                case 4u:
                    primaryIcon = "intent_guard_bind";
                    secondaryIcon = "intent_venom_aoe";
                    break;
                default:
                    primaryIcon = "intent_slow_cleave";
                    secondaryIcon = "intent_guard_heal";
                    break;
            }
        }

        /// <summary>从权威 BattleStateS2C 中读取护卫救援目标的真实名称与当前/最大气血。</summary>
        public static bool TryFindProtectedGuardHp(
            global::BattleStateS2C state,
            ulong guardActorId,
            out string guardName,
            out ulong health,
            out ulong maxHealth)
        {
            guardName = string.Empty;
            health = 0UL;
            maxHealth = 0UL;
            if (state == null || guardActorId == 0UL || state.Actors == null) return false;
            for (int i = 0; i < state.Actors.Count; ++i)
            {
                var actor = state.Actors[i];
                if (actor == null || actor.ActorId != guardActorId) continue;
                guardName = string.IsNullOrWhiteSpace(actor.Name) ? "回春护卫" : actor.Name;
                health = actor.Attributes?.Health ?? 0UL;
                maxHealth = actor.MaxHealth > 0UL ? actor.MaxHealth : (health > 0UL ? health : 1UL);
                return true;
            }
            return false;
        }

        /// <summary>格式化阶段二周精进池的剩余刷新时间（严格由服务端权威时间戳计算）。</summary>
        public static string FormatWeeklyResetRemaining(ulong nextResetAtMs, ulong nowUtcMs)
        {
            if (nextResetAtMs == 0UL || nextResetAtMs <= nowUtcMs) return string.Empty;
            ulong remainingSec = (nextResetAtMs - nowUtcMs) / 1000UL;
            ulong days = remainingSec / 86400UL;
            ulong hours = (remainingSec % 86400UL) / 3600UL;
            ulong minutes = (remainingSec % 3600UL) / 60UL;
            if (days > 0UL) return $"剩余 {days}天{hours}时";
            if (hours > 0UL) return $"剩余 {hours}小时{minutes}分";
            return $"剩余 {Math.Max(1UL, minutes)}分";
        }

        /// <summary>计算保底或材料总账的归一化进度比例（0..1）。</summary>
        public static float ComputeProgressRatio(ulong current, ulong limit, bool forceFull = false)
        {
            if (forceFull) return 1f;
            if (limit == 0UL) return 0f;
            if (current >= limit) return 1f;
            return (float)current / limit;
        }

        /// <summary>
        /// 应用已通过离线横向三段检验的控件精灵（仅设置 Sliced 当 sprite.border 非零）。
        /// </summary>
        public static Image CreateSlicedOrSimple(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            Sprite sprite,
            Color fallback,
            bool raycast = false)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, width, height, sprite, raycast);
            if (sprite != null)
            {
                img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = Color.white;
            }
            else
            {
                img.color = fallback;
            }
            return img;
        }

        /// <summary>
        /// 绘制 +11 保底 / 两阶段材料进度条：使用 RectMask2D 按比例裁切内槽填充，避免圆角端帽被压扁。
        /// </summary>
        public static RectTransform DrawPityBar(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            ulong current,
            ulong limit,
            bool guaranteed)
        {
            var container = QdaoUguiFactory.CreateRect(name, parent, x, y, width, height);
            var bgSprite = LoadControl("pity_bar_bg");
            var bg = QdaoUguiFactory.CreateImage("Bg", container, 0f, 0f, width, height, bgSprite, false);
            bg.type = Image.Type.Simple;
            bg.color = bgSprite != null ? Color.white : new Color(0.84f, 0.81f, 0.72f, 0.95f);

            float ratio = ComputeProgressRatio(current, limit, guaranteed);
            if (ratio <= 0f) return container;

            float innerX = width * PityLeftRatio;
            float innerY = height * PityTopRatio;
            float innerW = width * PityInnerWidthRatio;
            float innerH = height * PityInnerHeightRatio;
            float clipW = Mathf.Max(2f, innerW * ratio);

            var clip = QdaoUguiFactory.CreateRect("FillClip", container, innerX, innerY, clipW, innerH);
            clip.gameObject.AddComponent<RectMask2D>();

            var fillSprite = LoadControl(guaranteed ? "pity_bar_fill_guaranteed" : "pity_bar_fill_normal");
            var fill = QdaoUguiFactory.CreateImage("Fill", clip, 0f, 0f, innerW, innerH, fillSprite, false);
            fill.type = Image.Type.Simple;
            fill.color = fillSprite != null ? Color.white : (guaranteed ? GoldHighlight : QdaoUguiTheme.Html("#2E7D60"));
            return container;
        }

        /// <summary>
        /// 绘制装备词条 8 次打磨保底条（使用 craft-quality-skill-ui-20261124 横向三段已验证切片）。
        /// </summary>
        public static RectTransform DrawPolishPityBar(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            uint current,
            uint limit)
        {
            var container = QdaoUguiFactory.CreateRect(name, parent, x, y, width, height);
            var bgSprite = LoadCraftControl("pity_bar_bg");
            CreateSlicedOrSimple(container, "Bg", 0f, 0f, width, height,
                bgSprite, new Color(0.84f, 0.81f, 0.72f, 0.95f), raycast: false);

            bool ready = limit > 0u && current + 1u >= limit;
            float ratio = ComputeProgressRatio(current, limit, forceFull: ready);
            if (ratio <= 0f) return container;

            float padX = width * (10f / 560f);
            float padY = height * (6f / 44f);
            float innerW = Mathf.Max(4f, width - padX * 2f);
            float innerH = Mathf.Max(4f, height - padY * 2f);
            float clipW = Mathf.Max(2f, innerW * ratio);

            var clip = QdaoUguiFactory.CreateRect("FillClip", container, padX, padY, clipW, innerH);
            clip.gameObject.AddComponent<RectMask2D>();
            var fillSprite = LoadCraftControl(ready ? "pity_bar_fill_ready" : "pity_bar_fill_normal");
            CreateSlicedOrSimple(clip, "Fill", 0f, 0f, innerW, innerH,
                fillSprite, ready ? GoldHighlight : QdaoUguiTheme.Html("#2E7D60"), raycast: false);
            return container;
        }
    }
}
