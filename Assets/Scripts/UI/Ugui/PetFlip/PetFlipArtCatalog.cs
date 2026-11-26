using System;
using System.Collections.Generic;
using MmorpgClient.Game.PetFlip;
using MmorpgClient.World;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.PetFlip
{
    /// <summary>单张灵宠牌面在运行期解析出的权威显示名与头像精灵。</summary>
    public sealed class PetFlipResolvedFace
    {
        public SpiritPetCardPattern Pattern { get; }
        public string PetCatalogId { get; }
        public string DisplayName { get; }
        public Sprite Portrait { get; }

        public PetFlipResolvedFace(SpiritPetCardPattern pattern, string petCatalogId, string displayName, Sprite portrait)
        {
            Pattern = pattern;
            PetCatalogId = petCatalogId;
            DisplayName = displayName;
            Portrait = portrait;
        }
    }

    /// <summary>一次完整校验通过后的不可变灵宠翻牌美术资源包。</summary>
    public sealed class PetFlipResolvedAssets
    {
        public Sprite WindowFrameLandscape { get; }
        public Sprite WindowFramePortrait { get; }
        public Sprite CardBack { get; }
        public Sprite CardFront { get; }
        public Sprite MatchedMark { get; }
        public Sprite ButtonPrimary { get; }
        public Sprite ButtonSecondary { get; }
        public IReadOnlyList<PetFlipResolvedFace> Faces { get; }

        public PetFlipResolvedAssets(
            Sprite windowFrameLandscape,
            Sprite windowFramePortrait,
            Sprite cardBack,
            Sprite cardFront,
            Sprite matchedMark,
            Sprite buttonPrimary,
            Sprite buttonSecondary,
            IReadOnlyList<PetFlipResolvedFace> faces)
        {
            WindowFrameLandscape = windowFrameLandscape;
            WindowFramePortrait = windowFramePortrait;
            CardBack = cardBack;
            CardFront = cardFront;
            MatchedMark = matchedMark;
            ButtonPrimary = buttonPrimary;
            ButtonSecondary = buttonSecondary;
            Faces = faces ?? Array.Empty<PetFlipResolvedFace>();
        }

        public PetFlipResolvedFace GetFace(SpiritPetCardPattern pattern)
        {
            int index = (int)pattern;
            return index >= 0 && index < Faces.Count ? Faces[index] : null;
        }
    }

    /// <summary>
    /// 灵宠翻牌 UI 资源目录（<c>Assets/Resources/UI/Ugui/PetFlipV1/</c>）与 <see cref="QdaoPetCatalog"/> 头像解析器。
    /// 仅加载无字组件与目录权威头像，严格禁止缺图或同图占位冒充开局。
    /// </summary>
    public static class PetFlipArtCatalog
    {
        public const string ResourcePrefix = "UI/Ugui/PetFlipV1/";

        // 对齐 designs/pet-memory-ui-v1/source/build.ps1 的道家 Q 版色彩定调。
        public static readonly Color JadeText = new Color32(47, 81, 62, 255);
        public static readonly Color GoldText = new Color32(145, 102, 45, 255);
        public static readonly Color PaperText = new Color32(253, 246, 221, 255);
        public static readonly Color WarnText = new Color32(154, 68, 45, 255);
        public static readonly Color CardLabelText = new Color32(48, 77, 60, 255);
        public static readonly Color NoteFill = new Color32(251, 246, 232, 222);
        public static readonly Color NoteBorder = new Color32(178, 144, 78, 175);
        public static readonly Color NamePlateFill = new Color32(255, 248, 228, 244);
        public static readonly Color NamePlateBorder = new Color32(188, 143, 77, 215);
        public static readonly Color CircleGoldBorder = new Color32(192, 150, 72, 255);
        public static readonly Color CirclePaperFill = new Color32(251, 240, 216, 255);
        public static readonly Color PillSelectedFill = new Color32(43, 102, 74, 245);
        public static readonly Color PillSelectedBorder = new Color32(214, 174, 96, 255);
        public static readonly Color SpiritEyePeekTint = new Color32(232, 252, 238, 255);

        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static Sprite _circleMaskSprite;
        private static Sprite _circleRingSprite;
        private static Sprite _roundedNoteSprite;
        private static Sprite _roundedLabelSprite;
        private static Sprite _selectedPillSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            SpriteCache.Clear();
            _circleMaskSprite = null;
            _circleRingSprite = null;
            _roundedNoteSprite = null;
            _roundedLabelSprite = null;
            _selectedPillSprite = null;
        }

        public static Sprite LoadUiSprite(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            if (SpriteCache.TryGetValue(key, out Sprite cached) && cached != null)
                return cached;

            string path = ResourcePrefix + key;
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                Texture2D texture = Resources.Load<Texture2D>(path);
                if (texture != null)
                {
                    sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = key;
                }
            }

            if (sprite != null)
                SpriteCache[key] = sprite;

            return sprite;
        }

        /// <summary>
        /// 尝试加载并完整校验翻牌所需的全部核心框体、牌背/牌框、已配标记与 8 只灵宠权威头像及名称（轻松用 A..F，进阶用 A..H）。
        /// </summary>
        public static bool TryLoadAndValidate(out PetFlipResolvedAssets resolved, out string failureReason)
        {
            resolved = null;

            Sprite frameLandscape = LoadUiSprite("window-frame");
            Sprite framePortrait = LoadUiSprite("window-frame-portrait");
            Sprite cardBack = LoadUiSprite("card-back");
            Sprite cardFront = LoadUiSprite("card-front");
            Sprite matchedMark = LoadUiSprite("matched-mark");
            Sprite buttonPrimary = LoadUiSprite("button-primary");
            Sprite buttonSecondary = LoadUiSprite("button-secondary");

            bool hasCoreUi = frameLandscape != null
                && framePortrait != null
                && cardBack != null
                && cardFront != null
                && matchedMark != null;

            var descriptors = new PetFlipFaceDescriptor[PetFlipConstants.MaxPairCount];
            var faces = new PetFlipResolvedFace[PetFlipConstants.MaxPairCount];

            for (int i = 0; i < PetFlipConstants.MaxPairCount; i++)
            {
                var pattern = (SpiritPetCardPattern)i;
                string petId = PetFlipUiPresentation.GetPetCatalogId(pattern);
                QdaoPetCatalog.Entry entry = QdaoPetCatalog.Find(petId);
                Sprite portrait = QdaoPetCatalog.LoadPortrait(petId);
                string displayName = entry?.displayName;
                object portraitToken = portrait != null ? (object)(portrait.texture != null ? (UnityEngine.Object)portrait.texture : portrait) : null;

                descriptors[i] = new PetFlipFaceDescriptor(pattern, petId, displayName, portraitToken);
                faces[i] = new PetFlipResolvedFace(pattern, petId, displayName, portrait);
            }

            if (!PetFlipUiPresentation.ValidateAssets(hasCoreUi, descriptors, PetFlipConstants.MaxPairCount, out failureReason))
                return false;

            resolved = new PetFlipResolvedAssets(
                frameLandscape,
                framePortrait,
                cardBack,
                cardFront,
                matchedMark,
                buttonPrimary,
                buttonSecondary,
                Array.AsReadOnly(faces));
            return true;
        }

        /// <summary>平滑抗锯齿圆遮罩精灵，用于牌面与终局灵宠头像椭圆/圆形裁切。</summary>
        public static Sprite GetCircleMaskSprite()
        {
            if (_circleMaskSprite != null) return _circleMaskSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float half = (size - 1) * 0.5f;
            float radius = half - 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float dy = y - half;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _circleMaskSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _circleMaskSprite.name = "PetFlip_CircleMask";
            return _circleMaskSprite;
        }

        /// <summary>完成页灵宠圆形头像的暖金细环精灵。</summary>
        public static Sprite GetCircleRingSprite()
        {
            if (_circleRingSprite != null) return _circleRingSprite;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float half = (size - 1) * 0.5f;
            float outer = half - 1f;
            float inner = outer - 4.5f;
            var pixels = new Color32[size * size];
            Color32 ringColor = CircleGoldBorder;
            for (int y = 0; y < size; y++)
            {
                float dy = y - half;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float outerCov = Mathf.Clamp01(outer - dist + 0.5f);
                    float innerCov = Mathf.Clamp01(dist - inner + 0.5f);
                    float alpha = outerCov * innerCov;
                    pixels[y * size + x] = new Color32(ringColor.r, ringColor.g, ringColor.b, (byte)Mathf.RoundToInt(alpha * ringColor.a));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _circleRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _circleRingSprite.name = "PetFlip_CircleRing";
            return _circleRingSprite;
        }

        /// <summary>说明便签纸（圆角象牙底 + 暖金细边，9-slice 不拉伸变形）。</summary>
        public static Sprite GetRoundedNoteSprite()
        {
            if (_roundedNoteSprite != null) return _roundedNoteSprite;
            _roundedNoteSprite = CreateRoundedBoxSprite("PetFlip_RoundedNote", 64, 20f, 2f, NoteFill, NoteBorder);
            return _roundedNoteSprite;
        }

        /// <summary>卡牌底部灵宠名称铭牌（小圆角暖白底 + 金细边，9-slice）。</summary>
        public static Sprite GetRoundedLabelSprite()
        {
            if (_roundedLabelSprite != null) return _roundedLabelSprite;
            _roundedLabelSprite = CreateRoundedBoxSprite("PetFlip_RoundedLabel", 48, 8f, 1.5f, NamePlateFill, NamePlateBorder);
            return _roundedLabelSprite;
        }

        /// <summary>难度/玩法选中胶囊底图（玉绿漆底 + 暖金边，9-slice；待 Codex V2 视觉定稿可无感替换）。</summary>
        public static Sprite GetSelectedPillSprite()
        {
            if (_selectedPillSprite != null) return _selectedPillSprite;
            _selectedPillSprite = CreateRoundedBoxSprite("PetFlip_SelectedPill", 48, 12f, 2f, PillSelectedFill, PillSelectedBorder);
            return _selectedPillSprite;
        }

        private static Sprite CreateRoundedBoxSprite(
            string name,
            int size,
            float radius,
            float borderWidth,
            Color fillColor,
            Color borderColor)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float innerHalf = half - radius;

            for (int y = 0; y < size; y++)
            {
                float py = Mathf.Abs(y - half) - innerHalf;
                for (int x = 0; x < size; x++)
                {
                    float px = Mathf.Abs(x - half) - innerHalf;
                    float dx = Mathf.Max(px, 0f);
                    float dy = Mathf.Max(py, 0f);
                    float sdf = Mathf.Sqrt(dx * dx + dy * dy) + Mathf.Min(Mathf.Max(px, py), 0f) - radius;

                    float outsideAlpha = Mathf.Clamp01(-sdf + 0.5f);
                    if (outsideAlpha <= 0f)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    float borderFactor = Mathf.Clamp01((sdf + borderWidth) + 0.5f);
                    Color c = Color.Lerp(fillColor, borderColor, borderFactor);
                    c.a *= outsideAlpha;
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            int pad = Mathf.CeilToInt(radius) + 2;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(pad, pad, pad, pad));
            sprite.name = name;
            return sprite;
        }
    }
}
