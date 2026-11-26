using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Guild
{
    /// <summary>
    /// 帮会全天帮战 V2 独立皮肤资源目录与控件工厂（只读 UI/Ugui/GuildWarV2/，绝不改动其他窗口共享主题）：
    /// - 严格对照 designs/guild-war-ui-v2/HANDOFF.md 与 MANIFEST.md 的 31 张无字正式切图及色板；
    /// - 标题深墨绿 #002D18、正文近黑 #20201E、标签暖棕 #643A10、玉绿/朱砂钮淡黄白 #FFFFDF；
    /// - 整屏底板（panel_01..08）固定 1932x814 等比缩放，仅 bar_banner_hp_* / alert_banner_* / btn_danger_crimson 启用横向三段拉伸。
    /// </summary>
    public static class GuildWarUiArt
    {
        public const string Root = "UI/Ugui/GuildWarV2/";
        public const float PanelWidth = 1932f;
        public const float PanelHeight = 814f;

        public static readonly Color TitleJade = QdaoUguiTheme.Html("#002D18");
        public static readonly Color BodyInk = QdaoUguiTheme.Html("#20201E");
        public static readonly Color LabelBrown = QdaoUguiTheme.Html("#643A10");
        public static readonly Color LightText = QdaoUguiTheme.Html("#FFFFDF");
        public static readonly Color PaperFill = QdaoUguiTheme.Html("#F1E6D8");
        public static readonly Color PaperSoft = new Color(0.95f, 0.91f, 0.85f, 0.94f);
        public static readonly Color GoldBorder = QdaoUguiTheme.Html("#AA803E");
        public static readonly Color JadeFill = QdaoUguiTheme.Html("#294F41");
        public static readonly Color CrimsonText = QdaoUguiTheme.Html("#9C2B1A");
        public static readonly Color DeepBackdrop = new Color(0.027f, 0.094f, 0.114f, 0.72f);

        /// <summary>全部 33 张正式运行切图名（8 张无字底板 + 25 张独立无字透明切图）。</summary>
        public static readonly string[] AllAssetKeys =
        {
            "panel_01_guild_home",
            "panel_02_guild_war_main",
            "panel_03_garrison_and_conflict",
            "panel_04_bounty_hunt",
            "panel_05_bounty_post_and_logs",
            "panel_06_war_settlement",
            "panel_07_guild_shop",
            "panel_08_field_hud",
            "banner_guild_flag",
            "leyline_bg_baxian",
            "leyline_bg_biyuan",
            "leyline_bg_yuexi",
            "leyline_shield_overlay",
            "bar_banner_hp_track",
            "bar_banner_hp_fill_ally",
            "bar_banner_hp_fill_enemy",
            "badge_streak_3",
            "badge_streak_5",
            "badge_streak_8",
            "badge_bounty_target",
            "badge_spectate_swords",
            "badge_stance_war",
            "badge_stance_peace",
            "badge_frenzy_round12",
            "badge_gold_leyline",
            "badge_rival_guild",
            "chest_merit_locked",
            "chest_merit_ready",
            "chest_merit_claimed",
            "alert_banner_siege",
            "alert_banner_bounty",
            "tag_exclusive_bg",
            "btn_danger_crimson",
        };

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var sprite = QdaoUguiTheme.RequireSprite(Root + key);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>加载 V2.5 可选增补切片；未落盘前自动优雅降级到既有正式切图，落盘后零改代码自动生效。</summary>
        public static Sprite LoadOrFallback(string key, string fallbackKey)
        {
            if (!string.IsNullOrEmpty(key))
            {
                if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
                var loaded = Resources.Load<Sprite>(Root + key);
                if (loaded != null)
                {
                    Cache[key] = loaded;
                    return loaded;
                }
            }
            return Load(fallbackKey);
        }

        public static Image ArtOrFallback(UnityEngine.Transform parent, string name, string key, string fallbackKey,
            float x, float y, float w, float h, bool preserveAspect = false)
        {
            var sprite = LoadOrFallback(key, fallbackKey);
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            img.type = !preserveAspect && sprite != null && sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced
                : Image.Type.Simple;
            img.preserveAspect = preserveAspect;
            return img;
        }

        public static string LeylineBackgroundKey(uint leylineId) => leylineId switch
        {
            1 => "leyline_bg_baxian",
            2 => "leyline_bg_biyuan",
            3 => "leyline_bg_yuexi",
            _ => "leyline_bg_baxian",
        };

        public static string StreakBadgeKey(uint streak) =>
            streak >= 8 ? "badge_streak_8" : streak >= 5 ? "badge_streak_5" : "badge_streak_3";

        public static string ChestKey(bool claimed, bool claimable) =>
            claimed ? "chest_merit_claimed" : claimable ? "chest_merit_ready" : "chest_merit_locked";

        public static Image Art(UnityEngine.Transform parent, string name, string key, float x, float y, float w, float h, bool preserveAspect = false)
        {
            var sprite = Load(key);
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            img.type = !preserveAspect && sprite != null && sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced
                : Image.Type.Simple;
            img.preserveAspect = preserveAspect;
            return img;
        }

        public static Image Card(UnityEngine.Transform parent, string name, float x, float y, float w, float h, Color? bg = null)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null);
            img.color = bg ?? PaperSoft;
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(GoldBorder.r, GoldBorder.g, GoldBorder.b, 0.55f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return img;
        }

        public static TextMeshProUGUI Label(UnityEngine.Transform parent, string name, string text,
            float x, float y, float w, float h, float size, Color color,
            bool wrap = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var tmp = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, text, size, color, align);
            tmp.font = QdaoUguiTheme.ResolveFont();
            tmp.richText = false;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            if (wrap) tmp.alignment = TextAlignmentOptions.TopLeft;
            return tmp;
        }

        public static Button ActionButton(UnityEngine.Transform parent, string name, string caption,
            float x, float y, float w, float h, Action onClick,
            bool danger = false, bool primary = false, bool enabled = true, float fontSize = 24f)
        {
            Sprite sprite = danger
                ? Load("btn_danger_crimson")
                : GuildUiArt.Load(primary ? "button_primary" : "button_secondary");
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var image);
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            btn.interactable = enabled;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            Color textColor = danger || primary ? LightText : BodyInk;
            Label(btn.transform, "Caption", caption, 10f, 0f, w - 20f, h, fontSize, textColor, align: TextAlignmentOptions.Center);
            return btn;
        }
    }
}
