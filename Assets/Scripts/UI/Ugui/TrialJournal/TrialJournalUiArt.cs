using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.TrialJournal
{
    /// <summary>
    /// 五行试炼志道家 Q 版皮肤与控件工具（严格复用 D:\work\image\designs\trial-journal-ui-v1 锁定的已导入素材与色板）。
    /// 不复制大图，只加载已有无字 9-slice 切图与法宝图标。
    /// </summary>
    internal static class TrialJournalUiArt
    {
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;

        // 微信 / 抖音小游戏右上角胶囊保守避让边界（基于 1920 基准，结合 Screen.safeArea 共同生效）
        public const float MiniGameCapsuleWidth = 176f;

        private const string ChronicleRoot = "UI/Ugui/AppearanceChronicleV2/";
        private const string EquipmentIconsRoot = "UI/Ugui/Equipment20261117/Icons/";
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static Sprite s_circleSprite;

        // 严格对齐 designs/trial-journal-ui-v1/style.css 权威色板
        public static readonly Color Jade = QdaoUguiTheme.Html("#205B46");
        public static readonly Color JadeDeep = QdaoUguiTheme.Html("#173F33");
        public static readonly Color Ink = QdaoUguiTheme.Html("#304736");
        public static readonly Color Sub = QdaoUguiTheme.Html("#70634E");
        public static readonly Color MutedLabel = QdaoUguiTheme.Html("#8E7B59");
        public static readonly Color Gold = QdaoUguiTheme.Html("#C7B68E");
        public static readonly Color GoldSoft = QdaoUguiTheme.Html("#D6C69D");
        public static readonly Color Paper = QdaoUguiTheme.Html("#FBF6E9");
        public static readonly Color CardBg = QdaoUguiTheme.Html("#FAF4E6");
        public static readonly Color SelectedBg = QdaoUguiTheme.Html("#D4E8CF");
        public static readonly Color SelectedLine = QdaoUguiTheme.Html("#3B8266");
        public static readonly Color SealGold = QdaoUguiTheme.Html("#C8922B");
        public static readonly Color SealGoldFill = QdaoUguiTheme.Html("#F6DA93");
        public static readonly Color SealDimFill = QdaoUguiTheme.Html("#EEE8D9");
        public static readonly Color SealDimInk = QdaoUguiTheme.Html("#9A8E79");
        public static readonly Color ButtonCream = QdaoUguiTheme.Html("#FFF3D6");
        public static readonly Color InfoBg = QdaoUguiTheme.Html("#E6F0E2");
        public static readonly Color InfoBorder = QdaoUguiTheme.Html("#92B79E");
        public static readonly Color WarnBg = QdaoUguiTheme.Html("#FFF0D2");
        public static readonly Color WarnInk = QdaoUguiTheme.Html("#9B6328");
        public static readonly Color ErrorBg = QdaoUguiTheme.Html("#FBEAE1");
        public static readonly Color ErrorInk = QdaoUguiTheme.Html("#9B4D39");
        public static readonly Color SkeletonBg = QdaoUguiTheme.Html("#F5EFE2");
        public static readonly Color SkeletonBar = QdaoUguiTheme.Html("#E1DACB");
        public static readonly Color BackdropShade = new Color(0.03f, 0.08f, 0.06f, 0.58f);

        public static Sprite LoadChronicle(string key) => LoadCached(ChronicleRoot + key);

        public static Sprite LoadArtifactIcon(uint artifactEffectId)
        {
            string key = artifactEffectId switch
            {
                1u => "artifact_pozhen",
                2u => "artifact_zhaoying",
                3u => "artifact_huixiang",
                4u => "artifact_jisheng",
                5u => "artifact_guyuan",
                6u => "artifact_huxin",
                7u => "artifact_guiyuan",
                8u => "artifact_qingxin",
                _ => "reward_choice",
            };
            return LoadCached(EquipmentIconsRoot + key);
        }

        public static Sprite LoadRewardIcon(bool isVoucher)
            => LoadCached(EquipmentIconsRoot + (isVoucher ? "reward_choice" : "material_enhancement"));

        /// <summary>生成并缓存抗锯齿圆形 Sprite（用于 18 枚宗师道印圆徽与战法印徽章）。</summary>
        public static Sprite CircleSprite()
        {
            if (s_circleSprite != null) return s_circleSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "trial_journal_circle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            float half = (size - 1) * 0.5f;
            float radius = half - 1.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float dy = y - half;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - dist + 1f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            s_circleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            s_circleSprite.name = "trial_journal_circle_sprite";
            return s_circleSprite;
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

        public static Image FixedArt(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float w,
            float h,
            Sprite sprite,
            Color fallback,
            bool preserveAspect = true,
            bool raycast = false)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite, raycast);
            img.type = Image.Type.Simple;
            img.preserveAspect = preserveAspect;
            img.color = sprite != null ? Color.white : fallback;
            return img;
        }

        public static Image BorderedBox(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float w,
            float h,
            Color fill,
            Color borderColor,
            float borderPx = 1.5f,
            bool raycast = false)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null, raycast);
            img.color = fill;
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(borderPx, -borderPx);
            outline.useGraphicAlpha = true;
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
            var label = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, text ?? "", fontSize, color, alignment);
            label.richText = false;
            if (bold) label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = wrap ? TextOverflowModes.Ellipsis : TextOverflowModes.Ellipsis;
            return label;
        }

        public static Button SpriteButton(
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
            Action onClick)
        {
            var sprite = LoadChronicle(primary ? "button_primary" : "button_secondary");
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            if (sprite != null)
            {
                img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = interactable ? Color.white : new Color(0.82f, 0.80f, 0.74f, 0.75f);
            }
            else
            {
                img.color = interactable
                    ? (primary ? Jade : Paper)
                    : new Color(0.83f, 0.81f, 0.75f, 1f);
            }
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var label = Label("Label", btn.transform, 10f, 0f, Mathf.Max(20f, w - 20f), h,
                text, fontSize,
                interactable ? (primary ? ButtonCream : Jade) : Sub,
                bold: true, wrap: false, alignment: TextAlignmentOptions.Center);
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(13f, fontSize - 6f);
            label.fontSizeMax = fontSize;
            return btn;
        }

        public static Button FlatActionButton(
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
            Action onClick)
        {
            Color fill = !interactable
                ? QdaoUguiTheme.Html("#D4D0C0")
                : primary ? Jade : QdaoUguiTheme.Html("#F8F1DB");
            Color border = !interactable ? QdaoUguiTheme.Html("#B9B09E") : QdaoUguiTheme.Html("#B59D6A");
            var plate = BorderedBox(name, parent, x, y, w, h, fill, border, 1.5f, raycast: true);
            var btn = plate.gameObject.AddComponent<Button>();
            btn.targetGraphic = plate;
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var label = Label("Label", btn.transform, 6f, 0f, Mathf.Max(16f, w - 12f), h,
                text, fontSize,
                !interactable ? Sub : (primary ? ButtonCream : Jade),
                bold: true, wrap: false, alignment: TextAlignmentOptions.Center);
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(12f, fontSize - 5f);
            label.fontSizeMax = fontSize;
            return btn;
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
            if (SpriteCache.TryGetValue(path, out var cached)) return cached;
            var loaded = Resources.Load<Sprite>(path);
            SpriteCache[path] = loaded;
            return loaded;
        }
    }
}
