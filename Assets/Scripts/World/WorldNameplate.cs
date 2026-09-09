using MmorpgClient.UI.Ugui;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MmorpgClient.World
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// World-space actor nameplate: a 3D TextMeshPro label (not the uGUI
    /// TextMeshProUGUI) parked under the actor's feet by
    /// <see cref="WorldLabelBillboard"/>. Uses the project's SimKai SDF font
    /// with a dark outline so the name stays readable over the painted town.
    ///
    /// Outline material: TMP_Text.outlineWidth alone writes _OutlineWidth on
    /// an instance material, but the SimKai SDF asset uses the Mobile/Distance
    /// Field shader whose outline pass is gated by the OUTLINE_ON keyword (see
    /// BattleUiWidgets.ApplyOutline). Every nameplate would otherwise clone
    /// its own material, so one static material is derived from the font's
    /// material with the keyword and outline properties set, then assigned as
    /// fontSharedMaterial. Dozens of actors thus share a single draw material.
    /// </summary>
    public static class WorldNameplate
    {
        public const string ObjectName = "label";

        /// <summary>
        /// 3D TMP (isOrthographic = false, its default) maps 10 pt to one world
        /// unit, so 12.5 pt gives an em height of roughly 1.25 u, about 13% of
        /// the 9.8 u character billboard.
        /// </summary>
        public const float FontSize = 12.5f;

        /// <summary>Em height of the nameplate in world units (10 pt = 1 u).</summary>
        public const float WorldEmHeight = FontSize * 0.1f;

        public const float OutlineWidth = 0.25f;
        public static readonly Color OutlineColor = new Color(0f, 0f, 0f, 0.9f);

        /// <summary>Local player: pale green, as in the reference footage.</summary>
        public static readonly Color LocalPlayerColor = new Color(0.62f, 1f, 0.62f);
        /// <summary>Other players: warm off-white.</summary>
        public static readonly Color RemotePlayerColor = new Color(1f, 0.96f, 0.80f);
        /// <summary>NPCs: pale orange.</summary>
        public static readonly Color NpcColor = new Color(1f, 0.85f, 0.55f);

        private static Material _sharedOutlineMaterial;
        private static TMP_FontAsset _sharedOutlineFont;

        /// <summary>
        /// Creates a nameplate as a child of <paramref name="parent"/>. The
        /// caller attaches <see cref="WorldLabelBillboard"/> to place it; this
        /// only builds the text object.
        /// </summary>
        public static TMP_Text Create(Transform parent, string text, Color color)
        {
            var go = new GameObject(ObjectName);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var label = go.AddComponent<TextMeshPro>();
            var font = ResolveFont() ?? label.font;
            if (font != null) label.font = font;

            label.text = text ?? string.Empty;
            label.color = color;
            label.fontSize = FontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = false;
            // isOrthographic is deliberately left at the 3D TMP default (false):
            // TMP's glyph scale is fontSize / pointSize * (isOrthographic ? 1 : 0.1),
            // so flipping it on would make 12.5 pt span ~12.5 world units. The
            // orthographic town camera needs no perspective correction anyway.
            // Generous rect so long names never touch the bounds; the billboard
            // positions the pivot (centre) and alignment centres the glyphs.
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(24f, 4f);

            ApplyOutline(label);

            var renderer = label.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            return label;
        }

        private static TMP_FontAsset ResolveFont()
        {
            var font = QdaoUguiTheme.ResolveFont();
            if (font != null) return font;
            try
            {
                return TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            }
            catch (System.Exception)
            {
                return null; // TMP essentials not imported: leave the component's own default
            }
        }

        private static void ApplyOutline(TMP_Text label)
        {
            var font = label.font;
            if (font == null || font.material == null) return;

            var shared = ResolveSharedOutlineMaterial(font);
            if (shared != null)
            {
                label.fontSharedMaterial = shared;
                return;
            }

            // Fallback: per-instance material (same path as BattleUiWidgets.ApplyOutline).
            try
            {
                label.outlineWidth = OutlineWidth;
                label.outlineColor = OutlineColor;
                var mat = label.fontMaterial;
                if (mat != null)
                {
                    mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
                    mat.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidth);
                    mat.SetColor(ShaderUtilities.ID_OutlineColor, OutlineColor);
                }
            }
            catch (System.Exception)
            {
                // Font without an SDF material: outline unavailable, keep plain text.
            }
        }

        private static Material ResolveSharedOutlineMaterial(TMP_FontAsset font)
        {
            if (_sharedOutlineMaterial != null && _sharedOutlineFont == font)
                return _sharedOutlineMaterial;

            var source = font.material;
            if (source == null) return null;
            // Not HideAndDontSave: that flag set includes DontUnloadUnusedAsset,
            // which would keep a superseded material alive forever after a
            // font swap (QdaoUguiTheme.ResetRuntimeCaches) or an editor domain
            // reload. Without it the current material stays alive through this
            // static reference and the live labels' fontSharedMaterial, and an
            // orphaned one is reclaimed by the next Resources.UnloadUnusedAssets
            // (scene load / play-mode entry). It is never destroyed explicitly
            // because labels created before the swap may still render with it.
            var mat = new Material(source)
            {
                name = $"{source.name} (Nameplate Outline)",
                hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable
                            | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild,
            };
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidth);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, OutlineColor);
            _sharedOutlineMaterial = mat;
            _sharedOutlineFont = font;
            return mat;
        }
    }
}
