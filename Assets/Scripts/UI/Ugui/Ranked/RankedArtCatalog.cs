using System.Collections.Generic;
using Match;
using MmorpgClient.Game.Ranked;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Ranked
{
    using Transform = UnityEngine.Transform;
    /// <summary>
    /// 论剑排位 UGUI 美术资源目录与无 9-slice 控件工厂。
    /// 2026-11-25 起按 designs/lunjian-ui-v1/RETIRED_STYLE.md 与 ranked-play-v2-ui-20261125：
    /// - 严禁复用已退场的旧论剑主窗框（ui-window / ui-window-clean）与错误人物素材（avatar-pozheng/kongchang/shouxin、role-*-full）；
    /// - 主窗框、页签、按钮、表头与列表行统一复用已验收同源帮会控件（UI/Ugui/GuildFidelity20261016/）；
    /// - 人物头像由客户端绑定真实角色（QdaoCharacterCatalog），缺图时显示中性道家占位；动态中文统一走项目内置字体。
    /// </summary>
    public static class RankedArtCatalog
    {
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;
        public const string ResourcePrefix = "UI/Ugui/LunjianV1/";
        public const string GuildApprovedPrefix = "UI/Ugui/GuildFidelity20261016/";

        public static readonly Color GoldCream = QdaoUguiTheme.Html("#FFEFC2");
        public static readonly Color GoldSoft = QdaoUguiTheme.Html("#F5E5B4");
        public static readonly Color ButtonGoldText = QdaoUguiTheme.Html("#FFF4D6");
        public static readonly Color JadeDark = QdaoUguiTheme.Html("#244D42");
        public static readonly Color JadeHeading = QdaoUguiTheme.Html("#2C5546");
        public static readonly Color JadeSelected = QdaoUguiTheme.Html("#214E3D");
        public static readonly Color JadeAccent = QdaoUguiTheme.Html("#2E765C");
        public static readonly Color SageLight = QdaoUguiTheme.Html("#CADDCA");
        public static readonly Color InkMuted = QdaoUguiTheme.Html("#6E7764");
        public static readonly Color InkSecondary = QdaoUguiTheme.Html("#455447");
        public static readonly Color BronzeHint = QdaoUguiTheme.Html("#825C27");
        public static readonly Color WarnRed = QdaoUguiTheme.Html("#9B3B2E");

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var sprite = Resources.Load<Sprite>(ResourcePrefix + key);
            if (sprite != null) Cache[key] = sprite;
            return sprite;
        }

        /// <summary>加载已验收同源帮会道家 Q 版控件（与 ranked-play-v2-ui-20261125/assets 逐字节一致）。</summary>
        public static Sprite LoadGuildApproved(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string cacheKey = "guild:" + key;
            if (Cache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;
            var sprite = Resources.Load<Sprite>(GuildApprovedPrefix + key);
            if (sprite != null) Cache[cacheKey] = sprite;
            return sprite;
        }

        public static Sprite RankBadge(RankedTier tier) => Load(RankedFormatting.TierBadgeKey(tier));

        /// <summary>旧论剑角色头像已按 RETIRED_STYLE.md 禁用；真实玩家头像须经 QdaoCharacterCatalog.LoadPortrait 绑定。</summary>
        public static Sprite CharacterAvatar(uint presetId) => null;

        /// <summary>旧论剑角色全身图已按 RETIRED_STYLE.md 禁用；真实角色立绘由客户端动态绑定。</summary>
        public static Sprite CharacterFull(uint presetId) => null;

        public static Sprite PetPortrait(uint presetId) => presetId switch
        {
            201 => Load("pet-jinni"),
            202 => Load("pet-linghu"),
            203 => Load("pet-shiling"),
            _ => Load("pet-shiling"),
        };

        public static Image CreateGuildArt(string name, Transform parent, float x, float y, float w, float h,
            string guildKey, bool preserveAspect = false, bool raycastTarget = false)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, LoadGuildApproved(guildKey), raycastTarget);
            img.type = Image.Type.Simple;
            img.preserveAspect = preserveAspect;
            if (img.sprite == null) img.color = SeasonEconomyUiArt.BattleArenaPaper;
            return img;
        }

        public static Button CreateGuildButton(string name, Transform parent, float x, float y, float w, float h,
            string guildKey, string labelText, float fontSize, Color labelColor,
            out Image image, out TMP_Text label, bool preserveAspect = false)
        {
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, LoadGuildApproved(guildKey), out image);
            image.type = Image.Type.Simple;
            image.preserveAspect = preserveAspect;
            if (image.sprite == null) image.color = SeasonEconomyUiArt.BattleArenaPaper;
            label = QdaoUguiFactory.CreateText(name + "_Label", btn.transform, 10f, 0f, w - 20f, h,
                labelText, fontSize, labelColor, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(14f, fontSize - 8f);
            label.fontSizeMax = fontSize;
            return btn;
        }

        public static Image CreateArt(string name, Transform parent, float x, float y, float w, float h,
            string spriteKey, bool preserveAspect = false, bool raycastTarget = false)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, Load(spriteKey), raycastTarget);
            img.type = Image.Type.Simple;
            img.preserveAspect = preserveAspect;
            if (img.sprite == null) img.color = new Color(0.92f, 0.90f, 0.84f, 0.92f);
            return img;
        }

        public static Button CreateArtButton(string name, Transform parent, float x, float y, float w, float h,
            string spriteKey, string labelText, float fontSize, Color labelColor,
            out Image image, out TMP_Text label, bool preserveAspect = false)
        {
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, Load(spriteKey), out image);
            image.type = Image.Type.Simple;
            image.preserveAspect = preserveAspect;
            if (image.sprite == null) image.color = new Color(0.24f, 0.48f, 0.40f, 0.95f);
            label = QdaoUguiFactory.CreateText(name + "_Label", btn.transform, 8f, 0f, w - 16f, h,
                labelText, fontSize, labelColor, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(14f, fontSize - 8f);
            label.fontSizeMax = fontSize;
            return btn;
        }

        public static TMP_Text CreateLabel(string name, Transform parent, float x, float y, float w, float h,
            string text, float fontSize, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
            bool bold = false, bool wrap = false)
        {
            var tmp = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, text, fontSize, color, alignment);
            if (bold) tmp.fontStyle = FontStyles.Bold;
            if (wrap)
            {
                tmp.textWrappingMode = TextWrappingModes.Normal;
                tmp.overflowMode = TextOverflowModes.Truncate;
            }
            return tmp;
        }
    }
}
