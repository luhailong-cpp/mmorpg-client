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
    /// 论剑排位 V1 UGUI 美术资源目录与无 9-slice 控件工厂。
    /// 仅加载交接包内已验收导入的 47 张无字 PNG（Assets/Resources/UI/Ugui/LunjianV1/），
    /// 严格禁用 ui-window / button-primary-cloud 与任何九宫格拉伸，动态中文统一走项目内置字体。
    /// </summary>
    public static class RankedArtCatalog
    {
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;
        public const string ResourcePrefix = "UI/Ugui/LunjianV1/";

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

        public static Sprite RankBadge(RankedTier tier) => Load(RankedFormatting.TierBadgeKey(tier));

        public static Sprite CharacterAvatar(uint presetId) => presetId switch
        {
            101 => Load("avatar-pozheng"),
            102 => Load("avatar-kongchang"),
            103 => Load("avatar-shouxin"),
            _ => Load("avatar-pozheng"),
        };

        public static Sprite CharacterFull(uint presetId) => presetId switch
        {
            101 => Load("role-pozheng-full"),
            102 => Load("role-kongchang-full"),
            103 => Load("role-shouxin-full"),
            _ => Load("role-pozheng-full"),
        };

        public static Sprite PetPortrait(uint presetId) => presetId switch
        {
            201 => Load("pet-jinni"),
            202 => Load("pet-linghu"),
            203 => Load("pet-shiling"),
            _ => Load("pet-shiling"),
        };

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
