using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.TrialJournal
{
    /// <summary>
    /// 五行试炼志 V2 皮肤加载与控件工具。
    /// 优先加载 Assets/Resources/UI/Ugui/TrialJournalV2/ 专属无字 PNG-32 切片（缺图时回落至既有共享切片）。
    /// </summary>
    internal static class TrialJournalUiArt
    {
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;

        // 微信 / 抖音小游戏右上角胶囊保守避让边界（基于 1920 基准，结合 Screen.safeArea 共同生效）
        public const float MiniGameCapsuleWidth = 176f;

        public const string DedicatedSkinRoot = "UI/Ugui/TrialJournalV2/";
        public const string V3SkinRoot = "UI/Ugui/TrialJournalV3/";
        private const string ChronicleRoot = "UI/Ugui/AppearanceChronicleV2/";
        private const string EquipmentIconsRoot = "UI/Ugui/Equipment20261117/Icons/";
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static Sprite s_circleSprite;

        // 与已验收 TrialJournalV2 切片保持一致的高对比度文字与状态色
        public static readonly Color Jade = QdaoUguiTheme.Html("#194133");
        public static readonly Color JadeDeep = QdaoUguiTheme.Html("#14352A");
        public static readonly Color Ink = QdaoUguiTheme.Html("#23352E");
        public static readonly Color Sub = QdaoUguiTheme.Html("#695B49");
        public static readonly Color MutedLabel = QdaoUguiTheme.Html("#835628");
        public static readonly Color Gold = QdaoUguiTheme.Html("#CCAE78");
        public static readonly Color GoldSoft = QdaoUguiTheme.Html("#D6C29A");
        public static readonly Color Paper = QdaoUguiTheme.Html("#FBF6E9");
        public static readonly Color CardBg = QdaoUguiTheme.Html("#FAF4E6");
        public static readonly Color SelectedBg = QdaoUguiTheme.Html("#D4E8CF");
        public static readonly Color SelectedLine = QdaoUguiTheme.Html("#3B8266");
        public static readonly Color SealGold = QdaoUguiTheme.Html("#C8922B");
        public static readonly Color SealGoldFill = QdaoUguiTheme.Html("#1E5D47");
        public static readonly Color SealDimFill = QdaoUguiTheme.Html("#C7C4B8");
        public static readonly Color SealDimInk = QdaoUguiTheme.Html("#4B4E4A");
        public static readonly Color ButtonCream = QdaoUguiTheme.Html("#FFF8D7");
        public static readonly Color DisabledInk = QdaoUguiTheme.Html("#7F7B70");
        public static readonly Color BadgeRed = QdaoUguiTheme.Html("#AA3728");
        public static readonly Color InfoBg = QdaoUguiTheme.Html("#E6F0E2");
        public static readonly Color InfoBorder = QdaoUguiTheme.Html("#92B79E");
        public static readonly Color WarnBg = QdaoUguiTheme.Html("#FFF0D2");
        public static readonly Color WarnInk = QdaoUguiTheme.Html("#835628");
        public static readonly Color ErrorBg = QdaoUguiTheme.Html("#FBEAE1");
        public static readonly Color ErrorInk = QdaoUguiTheme.Html("#973B2B");
        public static readonly Color SkeletonBg = QdaoUguiTheme.Html("#F5EFE2");
        public static readonly Color SkeletonBar = QdaoUguiTheme.Html("#D6D0BF");
        public static readonly Color BackdropShade = new Color(0.03f, 0.08f, 0.06f, 0.58f);

        public static Sprite LoadChronicle(string key) => LoadCached(ChronicleRoot + key);

        /// <summary>优先加载 UI/Ugui/TrialJournalV2/ 专属切片，缺图时回落到既有共享切片。</summary>
        public static Sprite LoadSkin(string dedicatedKey, string chronicleFallbackKey = null)
        {
            if (!string.IsNullOrEmpty(dedicatedKey))
            {
                var dedicated = LoadCached(DedicatedSkinRoot + dedicatedKey);
                if (dedicated != null) return dedicated;
            }
            return !string.IsNullOrEmpty(chronicleFallbackKey) ? LoadChronicle(chronicleFallbackKey) : null;
        }

        public static Sprite LoadDedicatedOnly(string dedicatedKey)
            => string.IsNullOrEmpty(dedicatedKey) ? null : LoadCached(DedicatedSkinRoot + dedicatedKey);

        /// <summary>优先加载 UI/Ugui/TrialJournalV3/ 专属切片，缺图时回落到 TrialJournalV2 或共享切片。</summary>
        public static Sprite LoadV3Skin(string v3Key, string v2FallbackKey = null, string chronicleFallbackKey = null)
        {
            if (!string.IsNullOrEmpty(v3Key))
            {
                var v3 = LoadCached(V3SkinRoot + v3Key);
                if (v3 != null) return v3;
            }
            return LoadSkin(v2FallbackKey, chronicleFallbackKey);
        }

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

        /// <summary>生成并缓存抗锯齿圆形 Sprite（用于头像圆形遮罩、数字红点及缺图时的道印圆徽）。</summary>
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

        public static Image SlicedOrBordered(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float w,
            float h,
            string dedicatedKey,
            Color fallbackFill,
            Color fallbackBorder,
            float borderPx = 1.5f,
            bool raycast = false)
        {
            var sprite = LoadDedicatedOnly(dedicatedKey);
            if (sprite != null)
            {
                return SlicedOrTint(name, parent, x, y, w, h, sprite, fallbackFill, raycast);
            }
            return BorderedBox(name, parent, x, y, w, h, fallbackFill, fallbackBorder, borderPx, raycast);
        }

        /// <summary>圆形裁切头像 + portrait_frame 透明镂空外框（对齐验收图头像叠放规范）。</summary>
        public static void CircularPortrait(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float size,
            Sprite portraitSprite,
            bool lockedVeil = false)
        {
            float inset = Mathf.Max(2f, size * 0.05f);
            float inner = Mathf.Max(8f, size - inset * 2f);
            var maskImg = QdaoUguiFactory.CreateImage(name + "_Mask", parent,
                x + inset, y + inset, inner, inner, CircleSprite(), false);
            maskImg.color = QdaoUguiTheme.Html("#EAE5D5");
            var mask = maskImg.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            if (portraitSprite != null)
            {
                var pic = FixedArt(name + "_Pic", maskImg.transform, 0f, 0f, inner, inner,
                    portraitSprite, Color.clear, preserveAspect: true);
                if (lockedVeil) pic.color = new Color(0.86f, 0.85f, 0.80f, 0.72f);
            }

            var frame = LoadDedicatedOnly("portrait_frame");
            if (frame != null)
            {
                FixedArt(name + "_Frame", parent, x, y, size, size, frame, Color.clear, preserveAspect: true);
            }
        }

        /// <summary>单枚道印圆徽（支持亮/暗专属切片，居中叠放序号或单字，缺图回落至抗锯齿圆徽）。</summary>
        public static Image SealMedallion(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float size,
            bool lit,
            string labelText,
            float fontSize)
        {
            var dedicated = LoadDedicatedOnly(lit ? "seal_medallion_lit" : "seal_medallion_dim");
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, size, size, dedicated ?? CircleSprite());
            if (dedicated != null)
            {
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = Color.white;
            }
            else
            {
                img.color = lit ? SealGoldFill : SealDimFill;
                var outline = img.gameObject.AddComponent<Outline>();
                outline.effectColor = lit ? SealGold : Gold;
                outline.effectDistance = new Vector2(1.4f, -1.4f);
            }
            if (!string.IsNullOrEmpty(labelText))
            {
                Label("Mark", img.transform, 0f, 0f, size, size, labelText, fontSize,
                    lit ? ButtonCream : SealDimInk, bold: lit, wrap: false,
                    alignment: TextAlignmentOptions.Center);
            }
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
            label.overflowMode = TextOverflowModes.Ellipsis;
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
            var sprite = LoadSkin(primary ? "btn_primary" : "btn_secondary",
                primary ? "button_primary" : "button_secondary");
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            if (sprite != null)
            {
                img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = interactable ? Color.white : new Color(0.87f, 0.86f, 0.82f, 0.78f);
            }
            else
            {
                img.color = interactable
                    ? (primary ? Jade : Paper)
                    : new Color(0.84f, 0.82f, 0.77f, 1f);
            }
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var label = Label("Label", btn.transform, 12f, 0f, Mathf.Max(20f, w - 24f), h,
                text, fontSize,
                !interactable ? DisabledInk : (primary ? ButtonCream : Ink),
                bold: primary, wrap: false, alignment: TextAlignmentOptions.Center);
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
            return SpriteButton(name, parent, x, y, w, h, text, fontSize, primary, interactable, onClick);
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
