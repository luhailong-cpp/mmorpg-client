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
        Owned = 4,
        ExclusiveLocked = 5,
    }

    /// <summary>
    /// 易容与人物志共用的组队原稿同源皮肤。装饰 Sprite 与真实 TMP 文本分层；
    /// 人物、立绘台与道具卡槽保持原比例，读取与资产操作不属于本类。
    /// </summary>
    public static class AppearanceUiArt
    {
        public const float FrameWidth = 2160f;
        public const float FrameHeight = 1000f;
        public const float MainBodyTopOffset = 54f;
        public const float MainBodyHeight = 940f;

        public const string StyleRoot = "UI/Ugui/AppearanceChronicleV2/";
        public const string SeriesStyleRoot = "UI/Ugui/AppearanceSeriesV1/";
        public const string WardrobeCosmeticsRoot = "UI/Ugui/WardrobeCosmetics/";
        public const string ShowcaseCosmeticsRoot = "UI/Ugui/ShowcaseCosmeticsV1/";
        public const string CelebrationPropsRoot = "World/CelebrationProps/";

        public static readonly Color JadeInk = QdaoUguiTheme.Html("#304736");
        public static readonly Color DeepJade = QdaoUguiTheme.Html("#205B46");
        public static readonly Color MutedInk = QdaoUguiTheme.Html("#70634E");
        public static readonly Color WarmCream = QdaoUguiTheme.Html("#FFF3D6");
        public static readonly Color CrimsonInk = QdaoUguiTheme.Html("#A13A2A");
        public static readonly Color TrialGreen = QdaoUguiTheme.Html("#2A6C4F");
        public static readonly Color ParchmentCard = QdaoUguiTheme.Html("#F7F0E1");
        public static readonly Color ParchmentBorder = QdaoUguiTheme.Html("#C9AB70");
        public static readonly Color ParchmentInner = QdaoUguiTheme.Html("#FBF6EA");
        public static readonly Color PortraitPlaceholderBg = QdaoUguiTheme.Html("#EBE1CB");
        public static readonly Color SeriesGoldInk = QdaoUguiTheme.Html("#8E651F");
        public static readonly Color SeriesDimBg = QdaoUguiTheme.Html("#E6DEC9");
        public static readonly Color SeriesLitBg = QdaoUguiTheme.Html("#2B6A50");
        // Modal hit surfaces still intercept input; outside the actual window there is no painted scenery.
        public static readonly Color OverlayShade = new Color(0.025f, 0.095f, 0.075f, 0.30f);

        public static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath) || resourcePath.Contains("..") || resourcePath.Contains(":"))
                return null;
            return Resources.Load<Sprite>(resourcePath);
        }

        public static Sprite LoadSeries(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return LoadSprite(SeriesStyleRoot + key) ?? LoadSprite(StyleRoot + key);
        }

        /// <summary>加载衣柜灵宠幻化与技能特效交付的无字 RGBA 切片（Assets/Resources/UI/Ugui/WardrobeCosmetics/）。</summary>
        public static Sprite LoadWardrobeCosmetic(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return LoadSprite(WardrobeCosmeticsRoot + key);
        }

        /// <summary>加载排面装饰与赠礼图标切片（Assets/Resources/UI/Ugui/ShowcaseCosmeticsV1/）。</summary>
        public static Sprite LoadShowcaseCosmetic(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return LoadSprite(key) ?? LoadSprite(ShowcaseCosmeticsRoot + key) ?? LoadSprite(CelebrationPropsRoot + key);
        }

        /// <summary>按服务端权威 CosmeticShowcase ID 解析战斗阵纹/入场横幅/聊天气泡/战报卡底纹切片。</summary>
        public static Sprite LoadShowcaseById(uint cosmeticId)
        {
            if (cosmeticId == 0u) return null;
            if (MmorpgClient.Table.CosmeticShowcaseTableManager.Instance.KvData.TryGetValue(cosmeticId, out var row)
                && !string.IsNullOrWhiteSpace(row.AssetKey))
            {
                var fromTable = LoadShowcaseCosmetic(row.AssetKey);
                if (fromTable != null) return fromTable;
            }
            string fallback = cosmeticId switch
            {
                1u => "formation_skin_metal",
                2u => "formation_skin_wood",
                3u => "formation_skin_water",
                4u => "formation_skin_fire",
                5u => "formation_skin_earth",
                6u => "battle_banner_entrance_01",
                7u => "battle_banner_entrance_02",
                8u => "battle_banner_entrance_03",
                9u => "chat_bubble_skin_01",
                10u => "chat_bubble_skin_02",
                11u => "share_card_skin_01",
                12u => "share_card_skin_02",
                13u => "formation_skin_starlight",
                _ => null,
            };
            var sprite = LoadShowcaseCosmetic(fallback);
            if (sprite == null && cosmeticId == 13u) sprite = LoadShowcaseCosmetic("formation_skin_metal");
            return sprite;
        }

        public static Sprite LoadTravelPassBanner(uint passId, string explicitKey)
        {
            if (!string.IsNullOrWhiteSpace(explicitKey))
            {
                var direct = LoadSprite(explicitKey);
                if (direct != null) return direct;
            }
            string key = passId switch
            {
                2u => "UI/Ugui/TravelPassV2/banner_travel_pass_s2",
                3u => "UI/Ugui/TravelPassV2/banner_travel_pass_s3",
                4u => "UI/Ugui/TravelPassV2/banner_travel_pass_s4",
                5u => "UI/Ugui/TravelPassV2/banner_travel_pass_s5",
                6u => "UI/Ugui/TravelPassV2/banner_travel_pass_s6",
                _ => null,
            };
            return LoadSprite(key);
        }

        public static Sprite LoadCelebrationIcon(uint itemConfigId) => itemConfigId switch
        {
            3418u => LoadShowcaseCosmetic("icon_city_lantern"),
            3419u => LoadShowcaseCosmetic("icon_city_firework"),
            3420u => LoadShowcaseCosmetic("icon_city_tea_table"),
            _ => null,
        };

        public static Sprite LoadCelebrationWorldProp(uint itemConfigId) => itemConfigId switch
        {
            3418u => LoadSprite(CelebrationPropsRoot + "prop_city_lantern_world") ?? LoadCelebrationIcon(itemConfigId),
            3420u => LoadSprite(CelebrationPropsRoot + "prop_city_tea_table_world") ?? LoadCelebrationIcon(itemConfigId),
            _ => LoadCelebrationIcon(itemConfigId),
        };

        public static Sprite LoadFriendGiftIcon() => LoadShowcaseCosmetic("icon_friend_cosmetic_gift");

        public static Sprite LoadShowcaseVoucherIcon() => LoadShowcaseCosmetic("icon_showcase_voucher");

        public static Sprite LoadFounderSupporterBanner() => LoadShowcaseCosmetic("banner_founder_supporter_pack");

        public static Sprite LoadGuildEmblem(uint emblemId) => emblemId switch
        {
            1u => LoadSprite("UI/Ugui/GuildEmblemsV1/guild_emblem_01"),
            2u => LoadSprite("UI/Ugui/GuildEmblemsV1/guild_emblem_02"),
            3u => LoadSprite("UI/Ugui/GuildEmblemsV1/guild_emblem_03"),
            4u => LoadSprite("UI/Ugui/GuildEmblemsV1/guild_emblem_04"),
            5u => LoadSprite("UI/Ugui/GuildEmblemsV1/guild_emblem_05"),
            6u => LoadSprite("UI/Ugui/GuildEmblemsV1/guild_emblem_06"),
            _ => null,
        };

        public static Sprite LoadWarehouseExpandIcon() => LoadSprite("UI/Ugui/UtilityExpandV1/icon_warehouse_expand");
        public static Sprite LoadPetSquadPresetIcon() => LoadSprite("UI/Ugui/UtilityExpandV1/icon_pet_squad_preset");
        public static Sprite LoadReplayBookmarkExpandIcon() => LoadSprite("UI/Ugui/UtilityExpandV1/icon_replay_bookmark_expand");
        public static Sprite LoadRewardedVideoBadge() => LoadSprite("UI/Ugui/RewardedAdV1/icon_rewarded_video_badge");

        /// <summary>加载系列印章图：优先专属系列印章，缺省回退通用点亮/灰态印章。</summary>
        public static Sprite LoadSeriesSeal(string seriesKey, bool completed)
        {
            string suffix = completed ? "_lit" : "_dim";
            if (!string.IsNullOrWhiteSpace(seriesKey))
            {
                var specific = LoadSeries("series_seal_" + seriesKey + suffix);
                if (specific != null) return specific;
            }
            return LoadSeries("series_seal" + suffix);
        }

        public static Sprite LoadAppearance(string key)
        {
            switch (key)
            {
                case "appearance_preview_pedestal": return LoadTeam("portrait_pedestal");
                case "appearance_card_slot": return LoadTeam("appearance_card_slot");
                case "appearance_trial_banner": return LoadTeam("section_plate");
                case "appearance_btn_mall_gold": return LoadTeam("button_secondary");
                case "appearance_badge_current": case "appearance_badge_trialing": return LoadTeam("badge_self");
                case "appearance_badge_trial_free": case "appearance_badge_trial_used": return LoadTeam("badge_leader");
                default: return null;
            }
        }
        public static Sprite LoadTeam(string key) => LoadSprite(StyleRoot + key);

        /// <summary>Compatibility for the existing window builder; every panel key maps to this skin.</summary>
        public static Sprite LoadGameplay(string key)
        {
            switch (key)
            {
                case "main_frame": case "title_plate": case "button_primary": case "button_secondary":
                case "portrait_frame": case "close":
                    return LoadTeam(key);
                case "section_header": return LoadTeam("section_plate");
                case "button_disabled": return LoadTeam("button_secondary");
                case "pet_card_normal": case "pet_card_selected": return LoadTeam("member_row");
                default: return LoadSprite(GameplayUiArt.Root + key);
            }
        }
        public static Sprite LoadCardIcon() => LoadSprite("UI/Ugui/AppearanceV1/item_appearance_card") ?? LoadTeam("item_appearance_card");

        public static Image Frame(UnityEngine.Transform parent, string name, float x, float y, float w, float h,
            string gameplayKey = "main_frame", Color? fallback = null)
        {
            var sprite = LoadTeam("main_frame");
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            if (sprite != null)
            {
                RefreshFrameScale(img, w, h);
                img.color = Color.white;
            }
            else
            {
                img.color = fallback ?? ParchmentCard;
            }
            return img;
        }

        public static void RefreshFrameScale(Image img, float w, float h)
        {
            if (img == null || img.sprite == null) return;
            img.type = img.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            // 按宽/高较紧一边等比缩放四角与边框，窄横屏缩窗时不挤压内部纸面。
            float scaleX = img.sprite.rect.width / Mathf.Max(1f, w);
            float scaleY = img.sprite.rect.height / Mathf.Max(1f, h);
            img.pixelsPerUnitMultiplier = Mathf.Max(.01f, Mathf.Max(scaleX, scaleY));
        }

        public static Image TitlePlate(UnityEngine.Transform parent, string name,
            float x, float y, float w, float h)
        {
            // Use the whole cloud silhouette, at its actual aspect ratio. No baked title text.
            return FixedArt(parent, name, LoadTeam("title_plate"), x, y, w, h, Color.clear, true);
        }

        public static Image SectionHeader(UnityEngine.Transform parent, string name,
            float x, float y, float w, float h)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, LoadTeam("section_plate"));
            ApplyHorizontalSlice(image, image.sprite, h, DeepJade);
            return image;
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
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, LoadTeam("application_card"));
            if (image.sprite != null)
            {
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f;
                image.color = Color.white;
            }
            else image.color = fill ?? ParchmentCard;
            return image;
        }

        public static void ApplyRowSelection(Image image, bool selected)
        {
            if (image == null) return;
            image.sprite = LoadTeam("member_row");
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = selected ? QdaoUguiTheme.Html("#D4E8CF") : Color.white;
            var outline = image.GetComponent<Outline>();
            if (outline == null && selected) outline = image.gameObject.AddComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = QdaoUguiTheme.Html("#3B8266");
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;
                outline.enabled = selected;
            }
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
            StretchButtonLabel(text.rectTransform, 16f);
            text.richText = false;
            text.font = QdaoUguiTheme.ResolveFont();
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
            StretchButtonLabel(text.rectTransform, 14f);
            text.richText = false;
            text.fontStyle = FontStyles.Bold;
            text.enableAutoSizing = true;
            text.fontSizeMin = 16f;
            text.fontSizeMax = fontSize;
            return btn;
        }

        private static void StretchButtonLabel(RectTransform rect, float horizontalPadding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(horizontalPadding, 0f);
            rect.offsetMax = new Vector2(-horizontalPadding, 0f);
        }

        public static Button CloseButton(UnityEngine.Transform parent, string name, float x, float y, float size, Action onClick)
        {
            var sprite = LoadTeam("close");
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
            string text;
            Color fallbackBg;
            Color ink;
            switch (kind)
            {
                case AppearanceBadgeKind.CurrentPermanent:
                    text = "使用中";
                    fallbackBg = DeepJade;
                    ink = WarmCream;
                    break;
                case AppearanceBadgeKind.Trialing:
                    text = "试用中";
                    fallbackBg = CrimsonInk;
                    ink = WarmCream;
                    break;
                case AppearanceBadgeKind.Owned:
                    text = "已获得";
                    fallbackBg = TrialGreen;
                    ink = WarmCream;
                    break;
                case AppearanceBadgeKind.ExclusiveLocked:
                    text = "玩法解锁";
                    fallbackBg = QdaoUguiTheme.Html("#DDD2BE");
                    ink = MutedInk;
                    break;
                case AppearanceBadgeKind.TrialFree:
                    text = "可试用1小时";
                    fallbackBg = QdaoUguiTheme.Html("#C8D8B9");
                    ink = JadeInk;
                    break;
                default:
                    text = "未获得";
                    fallbackBg = QdaoUguiTheme.Html("#DDD2BE");
                    ink = MutedInk;
                    break;
            }
            // Team-derived jade/paper badges carry only live text; reading never changes this status.
            var sprite = LoadTeam(kind == AppearanceBadgeKind.CurrentPermanent
                || kind == AppearanceBadgeKind.Trialing
                || kind == AppearanceBadgeKind.Owned
                ? "badge_self" : "badge_leader");
            ApplyHorizontalSlice(bg, sprite, targetHeight, fallbackBg);
            label.text = text;
            label.color = ink;
        }

        public static void ApplySeriesTab(Image bg, TMP_Text label, bool selected, bool completed, float targetHeight)
        {
            if (bg == null || label == null) return;
            var sprite = LoadSeries(selected ? "series_tab_selected" : "series_tab_normal")
                ?? LoadTeam(selected ? "badge_self" : "badge_leader");
            Color fallbackBg = selected ? DeepJade : (completed ? QdaoUguiTheme.Html("#DFECDA") : SeriesDimBg);
            ApplyHorizontalSlice(bg, sprite, targetHeight, fallbackBg);
            if (sprite != null && !selected && completed)
                bg.color = QdaoUguiTheme.Html("#E3F3E4");
            label.color = selected ? WarmCream : (completed ? DeepJade : JadeInk);
            label.fontStyle = (selected || completed) ? FontStyles.Bold : FontStyles.Normal;
        }

        public static void ApplySeriesBadge(Image bg, TMP_Text label, string badgeName, bool completed, float targetHeight)
        {
            if (bg == null || label == null) return;
            var sprite = LoadSeries(completed ? "series_badge_lit" : "series_badge_dim")
                ?? LoadTeam(completed ? "badge_self" : "badge_leader");
            ApplyHorizontalSlice(bg, sprite, targetHeight, completed ? SeriesLitBg : SeriesDimBg);
            string display = string.IsNullOrWhiteSpace(badgeName) ? "系列印章" : badgeName;
            label.text = display;
            label.color = completed ? WarmCream : MutedInk;
            label.fontStyle = completed ? FontStyles.Bold : FontStyles.Normal;
        }

        public static Image SeriesCard(UnityEngine.Transform parent, string name, string spriteKey,
            float x, float y, float w, float h, Color fallback)
        {
            var sprite = LoadSeries(spriteKey) ?? LoadTeam("application_card");
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            if (sprite != null)
            {
                image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                image.pixelsPerUnitMultiplier = 1f;
                image.color = Color.white;
            }
            else
            {
                image.color = fallback;
            }
            return image;
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
