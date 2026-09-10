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
    /// with a dark outline and a translucent, text-sized backdrop so the name
    /// stays readable over the painted town.
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

        // A dark 72% scrim keeps the pale name colors readable even over the
        // brightest paving, while still letting the painting show through.
        public static readonly Color BackdropColor = new Color(0.035f, 0.047f, 0.043f, 0.72f);
        public const float BackdropPaddingX = 0.28f;
        public const float BackdropPaddingY = 0.14f;

        // SimKai has fine strokes. A wide SDF outline consumes their bright
        // interior at the town camera's scale; use a thin edge and gently expand
        // the face instead so the identity tint stays readable on the scrim.
        public const float OutlineWidth = 0.10f;
        public const float FaceDilate = 0.14f;
        public static readonly Color OutlineColor = new Color(0.015f, 0.025f, 0.020f, 0.8f);

        /// <summary>Local player: mint white with a restrained green identity tint.</summary>
        public static readonly Color LocalPlayerColor = new Color(0.82f, 1f, 0.90f);
        /// <summary>Other players: warm off-white.</summary>
        public static readonly Color RemotePlayerColor = new Color(1f, 0.98f, 0.88f);
        /// <summary>NPCs: pale orange.</summary>
        public static readonly Color NpcColor = new Color(1f, 0.91f, 0.72f);

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
            go.AddComponent<WorldNameplateBackdrop>().Initialize(label);
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
                    ApplyFaceMaterial(mat);
                }
            }
            catch (System.Exception)
            {
                // Font without an SDF material: outline unavailable, keep plain text.
            }
        }

        private static void ApplyFaceMaterial(Material material)
        {
            // Only mutate the nameplate's derived/instance material, never the
            // shared font asset. TMP vertex color supplies the identity tint once.
            if (material.HasProperty(ShaderUtilities.ID_FaceColor))
                material.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            if (material.HasProperty(ShaderUtilities.ID_FaceDilate))
                material.SetFloat(ShaderUtilities.ID_FaceDilate, FaceDilate);
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
            ApplyFaceMaterial(mat);
            _sharedOutlineMaterial = mat;
            _sharedOutlineFont = font;
            return mat;
        }
    }

    /// <summary>
    /// Fits a shared white sprite to TMP's actual glyph bounds, including CJK
    /// fallback glyphs. It inherits the label billboard's transform and sorting
    /// band, so the whole nameplate stays above ground feedback. This is actor
    /// UI: it deliberately follows WorldLabelBillboard's always-readable name
    /// convention, not the occlusion of the actor's body.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)] // WorldLabelBillboard sets text order in LateUpdate.
    internal sealed class WorldNameplateBackdrop : MonoBehaviour
    {
        private static Sprite _sharedSprite;
        private TextMeshPro _label;
        private MeshRenderer _textRenderer;
        private SpriteRenderer _backdrop;

        public void Initialize(TextMeshPro label)
        {
            _label = label;
            _textRenderer = label.GetComponent<MeshRenderer>();
            var go = new GameObject("NameplateBackdrop");
            go.transform.SetParent(label.transform, false);
            _backdrop = go.AddComponent<SpriteRenderer>();
            _backdrop.sprite = ResolveSprite();
            _backdrop.color = WorldNameplate.BackdropColor;
            _backdrop.shadowCastingMode = ShadowCastingMode.Off;
            _backdrop.receiveShadows = false;
            _backdrop.lightProbeUsage = LightProbeUsage.Off;
            _backdrop.reflectionProbeUsage = ReflectionProbeUsage.Off;
            label.ForceMeshUpdate();
            Refresh();
        }

        private static Sprite ResolveSprite()
        {
            if (_sharedSprite != null) return _sharedSprite;
            var texture = Texture2D.whiteTexture; // Unity-owned; never destroy it.
            _sharedSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), texture.width);
            _sharedSprite.name = "Nameplate Backdrop (Shared)";
            // Shared for all actors, without DontUnloadUnusedAsset. Old domain
            // instances can be reclaimed with other unused runtime assets.
            _sharedSprite.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable
                                    | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            return _sharedSprite;
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (_label == null || _backdrop == null) return;
            bool visible = _label.isActiveAndEnabled && _textRenderer != null
                && _textRenderer.enabled && !string.IsNullOrWhiteSpace(_label.text);
            _backdrop.enabled = visible;
            if (!visible) return;

            // Names can change after spawn. Force only when TMP is dirty; normal
            // movement updates the transform/order without rebuilding the mesh.
            if (_label.havePropertiesChanged) _label.ForceMeshUpdate();
            var bounds = _label.textBounds;
            _backdrop.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, 0.01f);
            _backdrop.transform.localScale = new Vector3(
                bounds.size.x + WorldNameplate.BackdropPaddingX * 2f,
                Mathf.Max(bounds.size.y, WorldNameplate.WorldEmHeight) + WorldNameplate.BackdropPaddingY * 2f,
                1f);
            _backdrop.sortingLayerID = _textRenderer.sortingLayerID;
            _backdrop.sortingOrder = _label.sortingOrder - 1;
        }

        private void OnDisable()
        {
            if (_backdrop != null) _backdrop.enabled = false;
        }

        private void OnDestroy()
        {
            // Also clean up if only this behaviour is removed from a live label.
            if (_backdrop != null) Destroy(_backdrop.gameObject);
        }
    }

}
