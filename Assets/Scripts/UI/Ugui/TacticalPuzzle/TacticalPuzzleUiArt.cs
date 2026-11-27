using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.TacticalPuzzle
{
    /// <summary>
    /// 万象残局坊 V1 美术资源加载与高对比度 UGUI 控件工厂。
    /// 全部 10 张专属手绘透明 PNG-32 切片位于 Assets/Resources/UI/Ugui/TacticalPuzzleV1/，
    /// 严格遵守：浅羊皮纸底图（puzzle_card_bg / puzzle_editor_panel_bg / puzzle_chat_card_bg / badge_draft_tag）
    /// 一律叠加深墨色（Ink / Jade / GoldInk）正文与标签，且所有玩家自填文本强制 richText = false。
    /// </summary>
    internal static class TacticalPuzzleUiArt
    {
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;
        public const float MiniGameCapsuleWidth = 176f;

        public const string SliceRoot = "UI/Ugui/TacticalPuzzleV1/";
        private const string SharedWindowRoot = "UI/Ugui/TrialJournalV2/";
        private const string ChronicleRoot = "UI/Ugui/AppearanceChronicleV2/";

        public static readonly string[] RequiredSliceKeys =
        {
            "hud_entry_puzzle",
            "puzzle_card_bg",
            "puzzle_editor_panel_bg",
            "puzzle_chat_card_bg",
            "badge_verified_seal",
            "badge_cleared_stamp",
            "badge_draft_tag",
            "icon_puzzle_like",
            "icon_puzzle_solution",
            "honor_puzzle_master",
        };

        public static readonly Color Jade = QdaoUguiTheme.Html("#194133");
        public static readonly Color JadeDeep = QdaoUguiTheme.Html("#123328");
        public static readonly Color Ink = QdaoUguiTheme.Html("#23352E");
        public static readonly Color Sub = QdaoUguiTheme.Html("#625340");
        public static readonly Color GoldInk = QdaoUguiTheme.Html("#7A5218");
        public static readonly Color Paper = QdaoUguiTheme.Html("#FAF4E6");
        public static readonly Color PaperSoft = QdaoUguiTheme.Html("#F2EBD9");
        public static readonly Color BorderGold = QdaoUguiTheme.Html("#C8AE7B");
        public static readonly Color Cream = QdaoUguiTheme.Html("#FFF8DF");
        public static readonly Color DisabledInk = QdaoUguiTheme.Html("#7D796E");
        public static readonly Color ErrorInk = QdaoUguiTheme.Html("#9A3224");
        public static readonly Color TagSelectedBg = QdaoUguiTheme.Html("#1E5A44");
        public static readonly Color TagIdleBg = QdaoUguiTheme.Html("#EFE7D4");
        public static readonly Color BackdropShade = new Color(0.03f, 0.08f, 0.06f, 0.62f);

        private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.Ordinal);

        public static Sprite LoadSlice(string key)
            => string.IsNullOrEmpty(key) ? null : LoadCached(SliceRoot + key);

        public static Sprite LoadWindowSkin(string primaryKey, string chronicleFallback = null)
        {
            if (!string.IsNullOrEmpty(primaryKey))
            {
                var sp = LoadCached(SharedWindowRoot + primaryKey);
                if (sp != null) return sp;
            }
            return string.IsNullOrEmpty(chronicleFallback) ? null : LoadCached(ChronicleRoot + chronicleFallback);
        }

        public static Image SlicedOrTint(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float w,
            float h,
            Sprite sprite,
            Color fallback,
            bool raycast = false)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite, raycast);
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

        public static Image FixedIcon(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float w,
            float h,
            Sprite sprite,
            bool raycast = false)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite, raycast);
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = Color.white;
            img.enabled = sprite != null;
            return img;
        }

        public static TextMeshProUGUI Label(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float w,
            float h,
            string text,
            float fontSize,
            Color color,
            bool bold = false,
            bool wrap = false,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var lbl = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, text ?? "", fontSize, color, alignment);
            lbl.richText = false;
            if (bold) lbl.fontStyle = FontStyles.Bold;
            lbl.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            lbl.overflowMode = TextOverflowModes.Ellipsis;
            return lbl;
        }

        public static Button ActionButton(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float w,
            float h,
            string text,
            float fontSize,
            bool primary,
            bool interactable,
            Action onClick,
            Sprite leadingIcon = null)
        {
            var sprite = LoadWindowSkin(primary ? "btn_primary" : "btn_secondary",
                primary ? "button_primary" : "button_secondary");
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            if (sprite != null)
            {
                img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = interactable ? Color.white : new Color(0.86f, 0.85f, 0.81f, 0.74f);
            }
            else
            {
                img.color = interactable ? (primary ? Jade : PaperSoft) : new Color(0.84f, 0.82f, 0.78f, 1f);
            }
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            float textLeft = 10f;
            if (leadingIcon != null)
            {
                float iconSize = Mathf.Min(28f, h - 14f);
                var icon = FixedIcon("Icon", btn.transform, 10f, (h - iconSize) * 0.5f, iconSize, iconSize, leadingIcon);
                if (!interactable) icon.color = new Color(0.75f, 0.75f, 0.75f, 0.7f);
                textLeft = 12f + iconSize + 4f;
            }
            var lbl = Label("Label", btn.transform, textLeft, 0f, Mathf.Max(20f, w - textLeft - 8f), h,
                text, fontSize, !interactable ? DisabledInk : (primary ? Cream : Ink),
                bold: primary, wrap: false, alignment: TextAlignmentOptions.Center);
            lbl.enableAutoSizing = true;
            lbl.fontSizeMin = Mathf.Max(13f, fontSize - 5f);
            lbl.fontSizeMax = fontSize;
            return btn;
        }

        public static Button ChipButton(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float w,
            float h,
            string text,
            bool selected,
            Action onClick,
            float fontSize = 18f)
        {
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, null, out var img);
            img.color = selected ? TagSelectedBg : TagIdleBg;
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = selected ? JadeDeep : BorderGold;
            outline.effectDistance = new Vector2(1.2f, -1.2f);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            Label("Label", btn.transform, 6f, 0f, Mathf.Max(16f, w - 12f), h, text, fontSize,
                selected ? Cream : Ink, bold: selected, wrap: false, alignment: TextAlignmentOptions.Center);
            return btn;
        }

        public static TMP_InputField InputBox(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float w,
            float h,
            string placeholder,
            string value,
            int characterLimit)
        {
            var input = QdaoUguiFactory.CreateInputField(name, parent, x, y, w, h, placeholder, characterLimit);
            input.lineType = TMP_InputField.LineType.SingleLine;
            var bg = input.GetComponent<Image>();
            if (bg != null)
            {
                bg.color = Color.white;
                var outline = bg.gameObject.AddComponent<Outline>();
                outline.effectColor = BorderGold;
                outline.effectDistance = new Vector2(1.4f, -1.4f);
            }
            input.textComponent.font = QdaoUguiTheme.ResolveFont();
            input.textComponent.fontSize = 20f;
            input.textComponent.color = Ink;
            input.textComponent.richText = false;
            if (input.placeholder is TMP_Text ph)
            {
                ph.font = input.textComponent.font;
                ph.fontSize = 19f;
                ph.color = Sub;
                ph.richText = false;
            }
            input.SetTextWithoutNotify(value ?? "");
            return input;
        }

        public static void ClearChildren(UnityEngine.Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        private static Sprite LoadCached(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Cache.TryGetValue(path, out var sp)) return sp;
            sp = Resources.Load<Sprite>(path);
            Cache[path] = sp;
            return sp;
        }
    }
}
