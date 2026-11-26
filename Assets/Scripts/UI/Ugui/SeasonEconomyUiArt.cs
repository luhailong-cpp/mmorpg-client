using System;
using System.Collections.Generic;
using TMPro;
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
        public const string SeasonFrame9SliceControlKey = "window_frame_season_9slice";
        public const string SeasonTitlePlaqueControlKey = "season_title_plaque";
        public const string Plus11SlotHaloIconKey = "plus11_slot_halo";
        public const string CraftQualityRoot = "UI/Ugui/CraftQualitySkill/";
        public const string CraftIconsRoot = "UI/Ugui/CraftQualitySkill/Icons/";
        public const string EconomyTradeRoot = "UI/Ugui/EconomyTradeV1/";
        public const string EconomyTradeV2Root = "UI/Ugui/EconomyTradeV2/";
        public static readonly string[] EconomyTradeV2AssetKeys =
        {
            "badge_designated_only",
            "badge_notice_48h",
            "badge_notice_fast",
            "icon_inherit_talisman",
            "icon_master_seal",
            "icon_wishlist_dove",
            "panel_08_zhenbaoge_rmb",
            "panel_09_equip_inherit",
        };
        public const string PaymentCashierRoot = "UI/Ugui/PaymentCashierV1/";
        public static readonly string[] PaymentCashierAssetKeys =
        {
            "cashier_card_bg",
            "lightcard_hero_frame",
            "sku_row_card",
            "sku_row_card_highlight",
            "pay_action_btn_primary",
            "pay_action_btn_secondary",
            "pay_countdown_track",
            "pay_countdown_fill",
            "badge_pay_waiting",
            "badge_pay_settled",
            "badge_pay_revoked",
            "icon_gift_chest_gold",
        };
        public const string BossTrialV2IconsRoot = "UI/Ugui/Battle/BossTrialV2/";
        public const string NewPlayReviewControlsRoot = "UI/Ugui/NewPlayReview/Controls/";
        public const string SkillScrollIconsRoot = "UI/Ugui/Skills/Scrolls/";
        public const string CanonicalSeasonEconomyIconsRoot = "UI/Ugui/SeasonEconomy/Icons/";
        public const string TrialJournalBadgesRoot = "UI/Ugui/TrialJournal/Badges/";
        public const string BattleArenaV2V3AssetsRoot = "UI/Ugui/BattleArenaV2V3/assets/";
        public const string BattleArenaV2V3ControlsRoot = "UI/Ugui/BattleArenaV2V3/controls/";
        public const string RankedV3GuildFidelityUiRoot = "UI/Ugui/RankedV3GuildFidelity/ui/";
        public const string RankedPlayV2ControlsRoot = "UI/Ugui/RankedPlayV2/";
        public const string RankedPlayV4Root = "UI/Ugui/RankedPlayV4/";
        public const string RankedV4TacticalUiRoot = "UI/Ugui/RankedV4Tactical/ui/";
        public const string GuildFidelityRoot = "UI/Ugui/GuildFidelity20261016/";

        // 论剑 V2+V3 统一道家配色（对应 BattleArenaV2V3 layout-contract.json）
        public static readonly Color BattleArenaInk = new Color32(0x26, 0x3F, 0x34, 255);
        public static readonly Color BattleArenaJade = new Color32(0x28, 0x6D, 0x59, 255);
        public static readonly Color BattleArenaJadeLight = new Color32(0xD9, 0xE7, 0xD6, 255);
        public static readonly Color BattleArenaPaper = new Color32(0xF4, 0xEC, 0xD8, 255);
        public static readonly Color BattleArenaWarmGold = new Color32(0xB7, 0x95, 0x52, 255);
        public static readonly Color BattleArenaVermilion = new Color32(0xA9, 0x47, 0x35, 255);
        public static readonly Color BattleArenaAmber = new Color32(0xA3, 0x6A, 0x2D, 255);
        public static readonly Color BattleArenaDisabled = new Color32(0x7B, 0x81, 0x75, 255);
        public static readonly Color SkillPhysicalColor = new Color32(0xA6, 0x78, 0x3B, 255);
        public static readonly Color SkillMagicColor = new Color32(0x4A, 0x82, 0x91, 255);
        public static readonly Color SkillControlColor = new Color32(0x7B, 0x6B, 0x90, 255);
        public static readonly Color SkillSupportColor = new Color32(0x4F, 0x82, 0x62, 255);

        // CONTROL-USAGE.md 实测：pity_bar_bg 为 535x88，视觉内槽为 [39, 26, 497, 62]（458x36）。
        private const float PityLeftRatio = 39f / 535f;
        private const float PityTopRatio = 26f / 88f;
        private const float PityInnerWidthRatio = 458f / 535f;
        private const float PityInnerHeightRatio = 36f / 88f;

        public static readonly Color GoldHighlight = new Color32(0xC4, 0x8A, 0x2C, 255);
        public static readonly Color GoldText = new Color32(0xFF, 0xF4, 0xD2, 255);
        /// <summary>浅古金按钮（btn_primary_gold）上的深棕墨高对比字色。</summary>
        public static readonly Color GoldButtonInk = new Color32(0x3F, 0x26, 0x0B, 255);
        public static readonly Color SoftWarnText = new Color32(0x8E, 0x4B, 0x28, 255);

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Cache.Clear();

        public static Sprite LoadIcon(string name) => LoadCached(IconsRoot + name);

        public static Sprite LoadControl(string name) => LoadCached(ControlsRoot + name);

        public static Sprite LoadCraftControl(string name) => LoadCached(CraftQualityRoot + name);

        public static Sprite LoadCraftIcon(string name) => LoadCached(CraftIconsRoot + name);

        public static Sprite LoadTradeSlice(string name) => LoadCached(EconomyTradeRoot + name);

        public static Sprite LoadTradeV2Slice(string name) => LoadCached(EconomyTradeV2Root + name);

        public static Sprite LoadPaymentCashierSlice(string name) => LoadCached(PaymentCashierRoot + name);

        /// <summary>使用 PaymentCashierV1 9-slice 按钮切图创建收银台/官方商城操作按钮，缺图回退通用按钮。</summary>
        public static Button CreateCashierButton(
            Transform parent,
            string label,
            float x,
            float y,
            float width,
            float height,
            Action click,
            bool primary,
            float fontSize = 24f)
        {
            var sprite = LoadPaymentCashierSlice(primary ? "pay_action_btn_primary" : "pay_action_btn_secondary");
            if (sprite == null)
            {
                return Gameplay.GameplayUiArt.Button(parent, label, x, y, width, height, click, primary: primary, fontSize: fontSize);
            }
            var button = QdaoUguiFactory.CreateArtButton(label, parent, x, y, width, height, sprite, out var art);
            art.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            art.pixelsPerUnitMultiplier = 1f;
            art.color = Color.white;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.96f, 0.84f, 1f);
            colors.selectedColor = new Color(1f, 0.94f, 0.78f, 1f);
            button.colors = colors;
            if (click != null) button.onClick.AddListener(() => click());
            var text = QdaoUguiFactory.CreateText("Label", button.transform, 18f, 0f, width - 36f, height,
                label, fontSize, primary ? Gameplay.GameplayUiArt.Cream : Gameplay.GameplayUiArt.Ink, TextAlignmentOptions.Center);
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return button;
        }

        public static Sprite LoadBossTrialV2Icon(string name) => LoadCached(BossTrialV2IconsRoot + name);

        public static Sprite LoadNewPlayReviewControl(string name) => LoadCached(NewPlayReviewControlsRoot + name);

        public static Sprite LoadSkillScrollIcon(uint scrollId)
            => scrollId == 0u ? null : LoadCached($"{SkillScrollIconsRoot}scroll_{scrollId}");

        public static Sprite LoadCanonicalBossAvatar(string name) => LoadCached(CanonicalSeasonEconomyIconsRoot + name);

        public static Sprite LoadTrialJournalBadge(string name) => LoadCached(TrialJournalBadgesRoot + name);

        public static Sprite LoadBattleArenaAsset(string name) => LoadCached(BattleArenaV2V3AssetsRoot + name);

        public static Sprite LoadBattleArenaControl(string name) => LoadCached(BattleArenaV2V3ControlsRoot + name);

        /// <summary>加载 d129182 帮会原图返修版论剑 V3 战前变阵专属切片（RankedV3GuildFidelity/ui/），缺图回退 BattleArenaV2V3/controls/。</summary>
        public static Sprite LoadRankedV3PrepControl(string name)
            => LoadCached(RankedV3GuildFidelityUiRoot + name) ?? LoadBattleArenaControl(name);

        /// <summary>加载 Ranked Play V2 独立透明无字控件（escalation-badge-base / fangmang-progress-track / four-skill-detail-card-base）。</summary>
        public static Sprite LoadRankedPlayV2Control(string name) => LoadCached(RankedPlayV2ControlsRoot + name);

        /// <summary>加载 Ranked Play V4 独立透明无字控件与图标（12 张透明 PNG 来自 image@51c33ee740a，缺图返回 null 安全回退）。</summary>
        public static Sprite LoadRankedPlayV4Asset(string name) => LoadCached(RankedPlayV4Root + name);

        /// <summary>加载论剑 V5 临阵唤灵无字透明控件（来自 Codex 交付 1471b83 ranked-play-v4-tactical-ui-20261125/ui/）。</summary>
        public static Sprite LoadRankedV4TacticalControl(string name) => LoadCached(RankedV4TacticalUiRoot + name);

        /// <summary>加载已验收的帮会原图同源道家 Q 版通用控件（与 ranked-play-v2-ui-20261125/assets 逐字节一致）。</summary>
        public static Sprite LoadGuildFidelityAsset(string name) => LoadCached(GuildFidelityRoot + name);

        /// <summary>仅映射已确认 S1 赛季印（101~104）的专属图标，未知 ID 返回 null 以便展示中性玉符占位。</summary>
        public static Sprite LoadBattleArenaSealIcon(uint sealId) => sealId switch
        {
            101u => LoadBattleArenaAsset("seal_101"),
            102u => LoadBattleArenaAsset("seal_102"),
            103u => LoadBattleArenaAsset("seal_103"),
            104u => LoadBattleArenaAsset("seal_104"),
            _ => null,
        };

        public static Sprite LoadBattleArenaSkillCategoryIcon(string categoryKey) => categoryKey switch
        {
            "physical" => LoadBattleArenaAsset("skill_physical"),
            "magic" => LoadBattleArenaAsset("skill_magic"),
            "control" => LoadBattleArenaAsset("skill_control"),
            "support" => LoadBattleArenaAsset("skill_support"),
            _ => null,
        };

        /// <summary>论剑 V2+V3 固定比例无九宫格控件：严格按 Image.Type.Simple 绘制，缺图回退纯色底。</summary>
        public static void ApplyFixedControlSprite(Image image, Sprite sprite, Color fallbackColor)
        {
            if (image == null) return;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = Color.white;
            }
            else
            {
                image.sprite = null;
                image.color = fallbackColor;
            }
        }

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

        /// <summary>将首领试炼种类映射为 SeasonEconomyV1 专属 256×256 无字首领头像键。</summary>
        public static string BossAvatarIconKey(uint bossTrialKind) => bossTrialKind switch
        {
            1u => "boss_break_avatar",
            2u => "boss_break_avatar",
            3u => "boss_barrier_avatar",
            4u => "boss_venom_avatar",
            8u => "boss_avatar_dual_guard",
            _ => "boss_break_avatar",
        };

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
                case 8u:
                    primaryIcon = "intent_guard_heal";
                    secondaryIcon = "intent_stance_phys";
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

        /// <summary>判定装备是否已达满配强化上限（例如 +11/+11），用于叠加外圈柔和玉金光环与满配皇冠。</summary>
        public static bool IsMaxEnhanced(uint enhanceLevel, uint enhanceMaxLevel)
            => enhanceMaxLevel > 0u && enhanceLevel >= enhanceMaxLevel;

        /// <summary>计算保底或材料总账的归一化进度比例（0..1）。</summary>
        public static float ComputeProgressRatio(ulong current, ulong limit, bool forceFull = false)
        {
            if (forceFull) return 1f;
            if (limit == 0UL) return 0f;
            if (current >= limit) return 1f;
            return (float)current / limit;
        }

        /// <summary>
        /// 应用已通过离线横向三段检验的控件精灵：横向三段（top=bottom=0）按目标高度自动换算 pixelsPerUnitMultiplier，
        /// 保证任意高度下左右云纹/圆角端帽严格保持原图比例、不挤压不重叠。
        /// </summary>
        public static void ApplyHorizontalSlice(Image img, Sprite sprite, float targetHeight)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.color = Color.white;
            if (sprite.border.sqrMagnitude > 0f)
            {
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = sprite.border.y == 0f && sprite.border.w == 0f
                    && targetHeight > 0f && sprite.rect.height > 0f
                    ? sprite.rect.height / targetHeight
                    : 1f;
            }
            else
            {
                img.type = Image.Type.Simple;
                img.pixelsPerUnitMultiplier = 1f;
            }
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
                ApplyHorizontalSlice(img, sprite, height);
            }
            else
            {
                img.color = fallback;
            }
            return img;
        }

        /// <summary>
        /// 在赛季玩法大厅顶栏左侧空白区（x=285..981, y=172..238，避开居中独立标题牌左红穗 x=1016 与下方说明条）绘制统一三页签切换条。
        /// </summary>
        public static void DrawSeasonLobbyTabs(
            Transform parent,
            int activeIndex,
            Action onBossTrials,
            Action onRotations,
            Action onFormalPvp)
        {
            var labels = new[] { "首领试炼", "轮换娱乐", "人宠会武" };
            var actions = new[] { onBossTrials, onRotations, onFormalPvp };
            const float startX = 285f;
            const float y = 172f;
            const float tabW = 224f;
            const float tabH = 66f;
            const float gap = 12f;
            for (int i = 0; i < 3; ++i)
            {
                bool active = i == activeIndex;
                var sprite = LoadControl(active ? "tab_active" : "tab_normal");
                var button = QdaoUguiFactory.CreateArtButton("SeasonTab_" + i, parent,
                    startX + i * (tabW + gap), y, tabW, tabH, sprite, out var art);
                if (sprite != null)
                    ApplyHorizontalSlice(art, sprite, tabH);
                else
                    art.color = active ? QdaoUguiTheme.Html("#285D4D") : QdaoUguiTheme.Html("#F7F1E2");
                var action = actions[i];
                if (!active && action != null) button.onClick.AddListener(() => action());
                var text = QdaoUguiFactory.CreateText("SeasonTabLabel_" + i, button.transform,
                    24f, 0f, tabW - 48f, tabH, labels[i], 24f,
                    active ? QdaoUguiTheme.Html("#F7F1E2") : QdaoUguiTheme.Html("#27483C"),
                    TextAlignmentOptions.Center);
                text.fontStyle = FontStyles.Bold;
                text.enableAutoSizing = true;
                text.fontSizeMin = 16f;
                text.fontSizeMax = 24f;
            }
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
