using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>
    /// 按帮会首要标准原图 approved-guild-reference.png 实测笔画厚度分档：
    /// Body(正文 2px) / Heading(栏标题与深色页签 3~4px) / Primary(绿底主钮与选中页签 3~4px+玉影) / Title(顶牌 4~6px+玉影)。
    /// </summary>
    public enum QdaoUiTypographyRole { Body, Heading, Primary, Title }

    /// <summary>
    /// Shared visual constants and resource loading for the native uGUI screen.
    /// One design unit maps to one pixel at the 2560x1080 reference resolution.
    /// </summary>
    public static class QdaoUguiTheme
    {
        public const float DesignWidth = 2560f;
        public const float DesignHeight = 1080f;

        // The screen artwork contains only immutable decoration. Interactive
        // controls, localized text and semantic status lights remain separate
        // uGUI objects in the baked prefab.
        public const string ScreenArtSpritePath = "UI/Ugui/Native/screen_art_headband";
        public const string StatusDotSpritePath = "UI/Ugui/Native/status_dot_mask";
        public const string CredentialSpritePath = "UI/Ugui/Native/credential_panel_v2";
        public const string CredentialCancelSpritePath = "UI/Ugui/Native/credential_btn_cancel";
        public const string CredentialSubmitSpritePath = "UI/Ugui/Native/credential_btn_submit";

        private const string SimKaiSdfPath = "Fonts/SimKai SDF";
        private const string SimKaiFontPath = "Fonts/SimKai";

        private static readonly Dictionary<string, Sprite> Sprites = new();
        private static TMP_FontAsset _font;
        private static TMP_FontAsset _symbolFallback;
        private static Material _matBase;
        private static Material _matBody;
        private static Material _matHeading;
        private static Material _matPrimary;
        private static Material _matTitle;

        public static readonly Color DarkBrown = Html("#3D2914");
        public static readonly Color Brown = Html("#5A4025");
        public static readonly Color MutedBrown = Html("#7C6043");
        public static readonly Color SelectedRed = Html("#9C2B1A");
        public static readonly Color Cream = Html("#FFF5D4");
        public static readonly Color StatusCream = Html("#FFF0BD");
        public static readonly Color PanelPaper = Html("#F1E6D8");
        public static readonly Color Letterbox = Html("#07181D");

        // 帮会标准原图 approved-guild-reference.png 实测墨色与鎏金玉白
        public static readonly Color GuildRichInk = new Color32(18, 13, 8, 255);
        public static readonly Color GuildInk = new Color32(31, 29, 27, 255);
        public static readonly Color GuildMuted = new Color32(84, 68, 48, 255);
        public static readonly Color GuildDeepJade = new Color32(0, 41, 19, 255);
        public static readonly Color GuildSelectedInk = new Color32(6, 34, 17, 255);
        public static readonly Color GuildTitleCream = new Color32(254, 250, 225, 255);
        public static readonly Color GuildTabCream = new Color32(255, 252, 202, 255);

        public static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;

            if (Sprites.TryGetValue(resourcePath, out var cached) && cached != null)
                return cached;

            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null)
                Debug.LogError($"[QdaoUgui] Missing sprite: Resources/{resourcePath}.png");
            else
                Sprites[resourcePath] = sprite;
            return sprite;
        }

        public static Sprite RequireSprite(string resourcePath)
        {
            var sprite = LoadSprite(resourcePath);
            if (sprite == null)
                throw new MissingReferenceException($"Required qdao uGUI sprite is missing: Resources/{resourcePath}.png");
            return sprite;
        }

        public static TMP_FontAsset ResolveFont()
        {
            if (_font != null)
                return _font;

            _font = Resources.Load<TMP_FontAsset>(SimKaiSdfPath);
            if (_font != null)
            {
                _font.isMultiAtlasTexturesEnabled = true;
                AttachSymbolFallback(_font);
                return _font;
            }

            // Runtime safety net. The editor builder creates a persistent SDF
            // asset, but a clean checkout remains usable before that menu item
            // has been run.
            var sourceFont = Resources.Load<Font>(SimKaiFontPath);
            if (sourceFont == null)
            {
                Debug.LogError($"[QdaoUgui] Missing font: Resources/{SimKaiFontPath}.ttf");
                return null;
            }

            _font = TMP_FontAsset.CreateFontAsset(
                sourceFont, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, true);
            _font.name = "SimKai SDF (Runtime)";
            _font.isMultiAtlasTexturesEnabled = true;
            AttachSymbolFallback(_font);
            return _font;
        }

        /// <summary>
        /// 为 SimKai 挂接极少缺字（✓/•/▶/▸/›/① 等 UI 符号）的运行期兜底字库，
        /// 保证汉字全部走 SimKai 楷体，同时彻底消除缺字豆腐块。
        /// </summary>
        private static void AttachSymbolFallback(TMP_FontAsset primary)
        {
            if (primary == null) return;
            if (_symbolFallback == null)
            {
                _symbolFallback = Resources.Load<TMP_FontAsset>(QdaoUguiTypography.BodyFontResourcePath);
                if (_symbolFallback != null) _symbolFallback.isMultiAtlasTexturesEnabled = true;
            }
            if (_symbolFallback == null || ReferenceEquals(_symbolFallback, primary)) return;
            if (primary.fallbackFontAssetTable == null)
                primary.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!primary.fallbackFontAssetTable.Contains(_symbolFallback))
                primary.fallbackFontAssetTable.Add(_symbolFallback);
        }

        private static void DestroyMaterial(ref Material mat)
        {
            if (mat == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mat);
            else UnityEngine.Object.DestroyImmediate(mat);
            mat = null;
        }

        public static Material ResolveTypographyMaterial(TMP_FontAsset font, QdaoUiTypographyRole role)
        {
            if (font == null || font.material == null) return null;
            if (!ReferenceEquals(_matBase, font.material) || _matBody == null || _matHeading == null || _matPrimary == null || _matTitle == null)
            {
                DestroyMaterial(ref _matBody);
                DestroyMaterial(ref _matHeading);
                DestroyMaterial(ref _matPrimary);
                DestroyMaterial(ref _matTitle);
                _matBase = font.material;

                _matBody = new Material(_matBase) { name = "QdaoSimKaiBody", hideFlags = HideFlags.DontSave };
                _matBody.SetFloat("_FaceDilate", 0.11f);
                _matBody.SetFloat("_OutlineSoftness", 0.02f);

                _matHeading = new Material(_matBase) { name = "QdaoSimKaiHeading", hideFlags = HideFlags.DontSave };
                _matHeading.SetFloat("_FaceDilate", 0.21f);
                _matHeading.SetFloat("_OutlineSoftness", 0.02f);

                _matPrimary = new Material(_matBase) { name = "QdaoSimKaiPrimary", hideFlags = HideFlags.DontSave };
                _matPrimary.SetFloat("_FaceDilate", 0.23f);
                _matPrimary.EnableKeyword("UNDERLAY_ON");
                _matPrimary.SetColor("_UnderlayColor", new Color(0.01f, 0.09f, 0.045f, 0.86f));
                _matPrimary.SetFloat("_UnderlayOffsetX", 0f);
                _matPrimary.SetFloat("_UnderlayOffsetY", -0.45f);
                _matPrimary.SetFloat("_UnderlayDilate", 0.20f);
                _matPrimary.SetFloat("_UnderlaySoftness", 0.22f);

                _matTitle = new Material(_matBase) { name = "QdaoSimKaiTitle", hideFlags = HideFlags.DontSave };
                _matTitle.SetFloat("_FaceDilate", 0.29f);
                _matTitle.EnableKeyword("UNDERLAY_ON");
                _matTitle.SetColor("_UnderlayColor", new Color(0.01f, 0.08f, 0.04f, 0.88f));
                _matTitle.SetFloat("_UnderlayOffsetX", 0f);
                _matTitle.SetFloat("_UnderlayOffsetY", -0.52f);
                _matTitle.SetFloat("_UnderlayDilate", 0.26f);
                _matTitle.SetFloat("_UnderlaySoftness", 0.24f);
            }

            return role switch
            {
                QdaoUiTypographyRole.Title => _matTitle,
                QdaoUiTypographyRole.Primary => _matPrimary,
                QdaoUiTypographyRole.Heading => _matHeading,
                _ => _matBody,
            };
        }

        public static void ApplyTypography(TMP_Text text, QdaoUiTypographyRole role)
        {
            if (text == null) return;
            var font = ResolveFont();
            if (font == null) return;
            text.font = font;
            var mat = ResolveTypographyMaterial(font, role);
            if (mat != null) text.fontSharedMaterial = mat;
        }

        /// <summary>按字号与色相亮度自动匹配四档厚楷材质，让全量 uGUI 文本零改动即获帮会原图笔意。</summary>
        public static void ApplyTypographyAuto(TMP_Text text)
        {
            if (text == null) return;
            float luma = 0.299f * text.color.r + 0.587f * text.color.g + 0.114f * text.color.b;
            bool lightText = luma >= 0.72f && text.color.a > 0.2f;
            QdaoUiTypographyRole role = lightText
                ? (text.fontSize >= 38f ? QdaoUiTypographyRole.Title : QdaoUiTypographyRole.Primary)
                : (text.fontSize >= 27f ? QdaoUiTypographyRole.Heading : QdaoUiTypographyRole.Body);
            ApplyTypography(text, role);
        }

        public static void ResetRuntimeCaches()
        {
            Sprites.Clear();
            _font = null;
            _symbolFallback = null;
            _matBase = null;
            DestroyMaterial(ref _matBody);
            DestroyMaterial(ref _matHeading);
            DestroyMaterial(ref _matPrimary);
            DestroyMaterial(ref _matTitle);
            QdaoUguiTypography.ResetRuntimeCache();
        }

        public static Color Html(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out var color) ? color : Color.white;
        }
    }

    /// <summary>
    /// 主城互斥全屏面板注册表与统一输入焦点判定。各 *UiRoot 在 Awake 注册、OnDestroy 注销，
    /// 打开前调 <see cref="HideOthers"/> 统一收起其余面板，避免新增面板时各处手工列表漂移漏关。
    /// </summary>
    public static class CityPanelRegistry
    {
        private static readonly List<(object Owner, Action Hide)> Panels = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => Panels.Clear();

        public static void Register(object owner, Action hide)
        {
            if (owner == null || hide == null) return;
            for (int i = 0; i < Panels.Count; i++)
            {
                if (ReferenceEquals(Panels[i].Owner, owner))
                {
                    Panels[i] = (owner, hide);
                    return;
                }
            }
            Panels.Add((owner, hide));
        }

        public static void Unregister(object owner)
        {
            if (owner == null) return;
            for (int i = Panels.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Panels[i].Owner, owner)) Panels.RemoveAt(i);
        }

        public static void HideOthers(object except)
        {
            if (Panels.Count == 0) return;
            var snapshot = Panels.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                var (owner, hide) = snapshot[i];
                if (ReferenceEquals(owner, except)) continue;
                if (owner is UnityEngine.Object unityObject && unityObject == null)
                {
                    Unregister(owner);
                    continue;
                }
                hide();
            }
        }

        public static bool IsTyping()
        {
            var current = EventSystem.current;
            if (current == null) return false;
            var selected = current.currentSelectedGameObject;
            // Unity 已销毁对象需用其重载的空值判断，?. 仍会访问失效的原生对象。
            if (selected == null) return false;
            return selected.GetComponentInParent<TMP_InputField>()?.isFocused == true
                || selected.GetComponentInParent<InputField>()?.isFocused == true;
        }
    }
}
