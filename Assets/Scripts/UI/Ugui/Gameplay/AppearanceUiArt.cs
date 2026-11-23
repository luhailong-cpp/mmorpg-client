using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public enum AppearanceBadgeKind
    {
        CurrentPermanent = 0,
        Trialing = 1,
        TrialFree = 2,
        TrialUsed = 3,
    }

    /// <summary>
    /// 易容界面美术资源与控件装配辅助。
    /// 严格遵循 designs/appearance-ui-v1/ui/CONTROL-USAGE.md：
    /// 立绘展示台与形象卡槽按 fixed_size_only 等比显示；6 张横条/徽章按 horizontal_only_verified 横向三段拉伸。
    /// </summary>
    public static class AppearanceUiArt
    {
        public const float FrameWidth = 2160f;
        public const float FrameHeight = 1000f;
        public const float MainBodyTopOffset = 54f;
        public const float MainBodyHeight = 940f;

        public const string AppearanceRoot = "UI/Ugui/AppearanceV1/";
        public const string CardIconPath = "UI/Icons/item_appearance_card";
        public const string MailRoot = "UI/Ugui/MailV1/";

        public static readonly Color JadeInk = QdaoUguiTheme.Html("#294F41");
        public static readonly Color DeepJade = QdaoUguiTheme.Html("#1F5946");
        public static readonly Color MutedInk = QdaoUguiTheme.Html("#6D5E47");
        public static readonly Color WarmCream = QdaoUguiTheme.Html("#FFF4D8");
        public static readonly Color CrimsonInk = QdaoUguiTheme.Html("#A13A2A");
        public static readonly Color TrialGreen = QdaoUguiTheme.Html("#2A6C4F");
        public static readonly Color ParchmentCard = QdaoUguiTheme.Html("#F7F0E1");
        public static readonly Color ParchmentBorder = QdaoUguiTheme.Html("#C9AB70");
        public static readonly Color ParchmentInner = QdaoUguiTheme.Html("#FBF6EA");
        public static readonly Color PortraitPlaceholderBg = QdaoUguiTheme.Html("#EBE1CB");
        public static readonly Color OverlayShade = new Color(0.025f, 0.095f, 0.075f, 0.72f);

        public static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath) || resourcePath.Contains("..") || resourcePath.Contains(":"))
                return null;
            return Resources.Load<Sprite>(resourcePath);
        }

        public static Sprite LoadAppearance(string key) => LoadSprite(AppearanceRoot + key);
        public static Sprite LoadMail(string key) => LoadSprite(MailRoot + key);
        public static Sprite LoadGameplay(string key) => LoadSprite(GameplayUiArt.Root + key);
        public static Sprite LoadCardIcon() => LoadSprite(CardIconPath) ?? LoadGameplay("icon_scroll");

        public static Image Frame(UnityEngine.Transform parent, string name, float x, float y, float w, float h,
            string gameplayKey = "main_frame", Color? fallback = null)
        {
            var sprite = LoadGameplay(gameplayKey);
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            if (sprite != null)
            {
                img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = Color.white;
            }
            else
            {
                img.color = fallback ?? ParchmentCard;
            }
            return img;
        }

        public static Image FixedArt(UnityEngine.Transform parent, string name, Sprite sprite,
            float x, float y, float w, float h, Color fallback, bool preserveAspect = true)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            img.type = Image.Type.Simple;
            img.preserveAspect = preserveAspect;
            img.color = sprite != null ? Color.white : fallback;
            return img;
        }

        /// <summary>横向三段拉伸条（保持左右端原始宽高比例，仅拉伸中段）。</summary>
        public static Image HorizontalBar(UnityEngine.Transform parent, string name, Sprite sprite,
            float x, float y, float w, float h, Color fallback)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            ApplyHorizontalSlice(img, sprite, h, fallback);
            return img;
        }

        public static void ApplyHorizontalSlice(Image img, Sprite sprite, float targetHeight, Color fallback)
        {
            if (img == null) return;
            img.sprite = sprite;
            if (sprite != null)
            {
                img.color = Color.white;
                img.preserveAspect = false;
                if (sprite.border.sqrMagnitude > 0f)
                {
                    img.type = Image.Type.Sliced;
                    float srcH = sprite.rect.height > 0f ? sprite.rect.height : targetHeight;
                    img.pixelsPerUnitMultiplier = targetHeight > 0f ? srcH / targetHeight : 1f;
                }
                else
                {
                    img.type = Image.Type.Simple;
                    img.pixelsPerUnitMultiplier = 1f;
                }
            }
            else
            {
                img.type = Image.Type.Simple;
                img.color = fallback;
            }
        }

        public static Image BorderedParchmentCard(UnityEngine.Transform parent, string name,
            float x, float y, float w, float h, Color? fill = null, Color? border = null)
        {
            var outer = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null);
            outer.color = border ?? ParchmentBorder;
            var inner = QdaoUguiFactory.CreateImage("InnerPaper", outer.transform, 2f, 2f, w - 4f, h - 4f, null);
            inner.color = fill ?? ParchmentCard;
            return outer;
        }

        public static Button ActionButton(UnityEngine.Transform parent, string name, string label,
            float x, float y, float w, float h, Action onClick, bool primary = false,
            float fontSize = 26f, string spriteKey = null)
        {
            var sprite = LoadGameplay(spriteKey ?? (primary ? "button_primary" : "button_secondary"));
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            if (sprite != null)
            {
                img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = Color.white;
            }
            else
            {
                img.color = primary ? DeepJade : ParchmentInner;
            }
            ConfigureButtonColors(btn);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var text = QdaoUguiFactory.CreateText("Label", btn.transform, 16f, 0f, w - 32f, h,
                label, fontSize, primary ? WarmCream : JadeInk, TextAlignmentOptions.Center);
            text.richText = false;
            text.fontStyle = primary ? FontStyles.Bold : FontStyles.Normal;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Max(16f, fontSize - 8f);
            text.fontSizeMax = fontSize;
            return btn;
        }

        public static Button GoldMallButton(UnityEngine.Transform parent, string name, string label,
            float x, float y, float w, float h, Action onClick, float fontSize = 23f)
        {
            var sprite = LoadAppearance("appearance_btn_mall_gold");
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            ApplyHorizontalSlice(img, sprite, h, QdaoUguiTheme.Html("#E5C47B"));
            ConfigureButtonColors(btn);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var text = QdaoUguiFactory.CreateText("Label", btn.transform, 14f, 0f, w - 28f, h,
                label, fontSize, DeepJade, TextAlignmentOptions.Center);
            text.richText = false;
            text.fontStyle = FontStyles.Bold;
            text.enableAutoSizing = true;
            text.fontSizeMin = 16f;
            text.fontSizeMax = fontSize;
            return btn;
        }

        public static Button CloseButton(UnityEngine.Transform parent, string name, float x, float y, float size, Action onClick)
        {
            var sprite = LoadMail("close_button") ?? LoadGameplay("close");
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, size, size, sprite, out var img);
            img.preserveAspect = true;
            if (sprite == null)
            {
                img.color = DeepJade;
                var cross = QdaoUguiFactory.CreateText("Cross", btn.transform, 0, 0, size, size,
                    "×", size * 0.6f, WarmCream, TextAlignmentOptions.Center);
                cross.richText = false;
            }
            ConfigureButtonColors(btn);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static void ApplyBadge(Image bg, TMP_Text label, AppearanceBadgeKind kind, float targetHeight)
        {
            if (bg == null || label == null) return;
            string key;
            string text;
            Color fallbackBg;
            Color ink;
            switch (kind)
            {
                case AppearanceBadgeKind.CurrentPermanent:
                    key = "appearance_badge_current";
                    text = "当前形象";
                    fallbackBg = DeepJade;
                    ink = WarmCream;
                    break;
                case AppearanceBadgeKind.Trialing:
                    key = "appearance_badge_trialing";
                    text = "试用中";
                    fallbackBg = CrimsonInk;
                    ink = WarmCream;
                    break;
                case AppearanceBadgeKind.TrialFree:
                    key = "appearance_badge_trial_free";
                    text = "可试用1小时";
                    fallbackBg = QdaoUguiTheme.Html("#C8D8B9");
                    ink = JadeInk;
                    break;
                default:
                    key = "appearance_badge_trial_used";
                    text = "已试用";
                    fallbackBg = QdaoUguiTheme.Html("#DDD2BE");
                    ink = MutedInk;
                    break;
            }
            ApplyHorizontalSlice(bg, LoadAppearance(key), targetHeight, fallbackBg);
            label.text = text;
            label.color = ink;
        }

        public static void AddVerticalScrollbar(ScrollRect scroll, UnityEngine.Transform parent,
            string name, float x, float y, float width, float height)
        {
            var rail = QdaoUguiFactory.CreateRect(name, parent, x, y, width, height);
            var railImg = rail.gameObject.AddComponent<Image>();
            railImg.color = QdaoUguiTheme.Html("#E5D6B7");
            var handle = QdaoUguiFactory.CreateStretch("Handle", rail, Vector4.zero);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.color = QdaoUguiTheme.Html("#5D8E76");
            var bar = rail.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle;
            bar.targetGraphic = handleImg;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private static void ConfigureButtonColors(Button btn)
        {
            btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.95f, 0.82f, 1f);
            colors.selectedColor = new Color(1f, 0.92f, 0.72f, 1f);
            colors.pressedColor = new Color(0.84f, 0.89f, 0.82f, 1f);
            colors.disabledColor = new Color(0.68f, 0.68f, 0.64f, 0.72f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
        }
    }

    /// <summary>
    /// 易容窗口自适应安全区缩放：兼容 2560×1080、1920×1080、窄横屏、刘海屏、底部手势区与小游戏右上角胶囊避让。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AppearanceWindowFit : MonoBehaviour
    {
        public Vector2 referenceSize = new(AppearanceUiArt.FrameWidth, AppearanceUiArt.FrameHeight);
        public float outerMarginPx = 18f;
        public Vector4 extraSafeInsetsPx; // x=left, y=bottom, z=right, w=top

        private RectTransform _rect;
        private bool _fitting;

        private void Awake() => Fit();
        private void OnEnable() => Fit();
        private void OnRectTransformDimensionsChange() => Fit();
        private void LateUpdate() => Fit();

        public void Fit()
        {
            if (_fitting) return;
            if (_rect == null) _rect = transform as RectTransform;
            if (_rect == null || !(_rect.parent is RectTransform parent)) return;
            if (referenceSize.x <= 0f || referenceSize.y <= 0f) return;

            _fitting = true;
            try
            {
                var available = AvailableRect(parent);
                if (available.width <= 1f || available.height <= 1f) return;

                float scale = Mathf.Min(available.width / referenceSize.x, available.height / referenceSize.y);
                var center = new Vector2(0.5f, 0.5f);
                if (_rect.anchorMin != center) _rect.anchorMin = center;
                if (_rect.anchorMax != center) _rect.anchorMax = center;
                if (_rect.pivot != center) _rect.pivot = center;
                if (_rect.sizeDelta != referenceSize) _rect.sizeDelta = referenceSize;

                var offset = available.center - parent.rect.center;
                var pos = new UnityEngine.Vector3(offset.x, offset.y, 0f);
                if (_rect.anchoredPosition3D != pos) _rect.anchoredPosition3D = pos;

                var uniform = new UnityEngine.Vector3(scale, scale, 1f);
                if (_rect.localScale != uniform) _rect.localScale = uniform;
                if (_rect.localRotation != Quaternion.identity) _rect.localRotation = Quaternion.identity;
            }
            finally
            {
                _fitting = false;
            }
        }

        private Rect AvailableRect(RectTransform parent)
        {
            var available = parent.rect;
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas = canvas.rootCanvas;
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var pixels = canvas.pixelRect;
                if (pixels.width > 0f && pixels.height > 0f)
                {
                    // 主屏幕 Overlay / 主相机与系统 Screen.safeArea（刘海、灵动岛、底部手势区）求交。
                    if (canvas.targetDisplay == 0 && (camera == null || camera.targetTexture == null) &&
                        Screen.safeArea.width > 0f && Screen.safeArea.height > 0f)
                    {
                        pixels = Intersect(pixels, Screen.safeArea);
                    }

                    // 微信 / 抖音小游戏右上角原生胶囊按钮与通用四周留白避让。
                    float left = outerMarginPx + Mathf.Max(0f, extraSafeInsetsPx.x);
                    float bottom = outerMarginPx + Mathf.Max(0f, extraSafeInsetsPx.y);
                    float right = outerMarginPx + Mathf.Max(0f, extraSafeInsetsPx.z);
                    float top = outerMarginPx + Mathf.Max(0f, extraSafeInsetsPx.w);
#if UNITY_WEBGL && !UNITY_EDITOR
                    top = Mathf.Max(top, 76f);
                    right = Mathf.Max(right, 24f);
#endif
                    pixels = Rect.MinMaxRect(
                        Mathf.Min(pixels.xMax, pixels.xMin + left),
                        Mathf.Min(pixels.yMax, pixels.yMin + bottom),
                        Mathf.Max(pixels.xMin, pixels.xMax - right),
                        Mathf.Max(pixels.yMin, pixels.yMax - top));

                    if (pixels.width > 0f && pixels.height > 0f &&
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixels.min, camera, out var min) &&
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixels.max, camera, out var max))
                    {
                        var safeLocal = Rect.MinMaxRect(
                            Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                            Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
                        available = Intersect(available, safeLocal);
                    }
                }
            }
            return available;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float x = Mathf.Max(a.xMin, b.xMin);
            float y = Mathf.Max(a.yMin, b.yMin);
            return new Rect(x, y,
                Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - x),
                Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - y));
        }
    }
}
