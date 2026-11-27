using System;
using System.Collections.Generic;
using Match;
using MmorpgClient.Table;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Tournament
{
    using Transform = UnityEngine.Transform;

    /// <summary>
    /// 《群仙问道赛》道家 Q 版 UGUI 美术目录与复用控件工厂：
    /// - 严格对照 D:/work/image/designs/season-team-tournament-v1 色板与切片：
    ///   复用已验收导入的 MailV1 / TeamV2 窗框、标题牌、按钮、流苏、物品图标及新增 TournamentV1/hud-entry-base；
    /// - 玩家头像严禁借用演示肖像，仅从 QdaoCharacterCatalog 加载已获批真实形象，未取得时呈现中性玉青太极占位；
    /// - 物品名称通过 ItemTableManager.Instance.FindById 现查现用，绝不长期缓存表行引用。
    /// </summary>
    internal static class TournamentArtCatalog
    {
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;
        public const float WindowWidth = 1640f;
        public const float WindowHeight = 900f;
        public const float CapsuleReserveWidth = 148f;

        // 对齐 designs/season-team-tournament-v1/index.html :root 色板
        public static readonly Color Jade = QdaoUguiTheme.Html("#205B46");
        public static readonly Color JadeDeep = QdaoUguiTheme.Html("#174433");
        public static readonly Color JadeSoft = QdaoUguiTheme.Html("#DCE9DA");
        public static readonly Color JadeCard = QdaoUguiTheme.Html("#E6EEDF");
        public static readonly Color Paper = QdaoUguiTheme.Html("#F7EFDF");
        public static readonly Color Ivory = QdaoUguiTheme.Html("#FFF9ED");
        public static readonly Color CardBg = QdaoUguiTheme.Html("#FBF4E5");
        public static readonly Color CardAltBg = QdaoUguiTheme.Html("#F4ECDB");
        public static readonly Color Gold = QdaoUguiTheme.Html("#AD8B51");
        public static readonly Color GoldSoft = QdaoUguiTheme.Html("#D8BF86");
        public static readonly Color GoldTagBg = QdaoUguiTheme.Html("#F7E9C8");
        public static readonly Color GoldTagText = QdaoUguiTheme.Html("#846028");
        public static readonly Color Ink = QdaoUguiTheme.Html("#273D32");
        public static readonly Color Muted = QdaoUguiTheme.Html("#716952");
        public static readonly Color Line = QdaoUguiTheme.Html("#CBB994");
        public static readonly Color WarnRed = QdaoUguiTheme.Html("#A54838");
        public static readonly Color ToastErrorBg = QdaoUguiTheme.Html("#8E4738");
        public static readonly Color PrimaryBtnText = QdaoUguiTheme.Html("#FFF0CC");
        public static readonly Color SecondaryBtnText = QdaoUguiTheme.Html("#594A31");
        public static readonly Color TagGreenBg = QdaoUguiTheme.Html("#E7EEDC");
        public static readonly Color TagGreenText = QdaoUguiTheme.Html("#35644A");
        public static readonly Color TagRedBg = QdaoUguiTheme.Html("#F7E1DA");
        public static readonly Color TagGrayBg = QdaoUguiTheme.Html("#EEE9DC");
        public static readonly Color CutLineBg = QdaoUguiTheme.Html("#F4E8CA");
        public static readonly Color HeaderRowBg = QdaoUguiTheme.Html("#E1EADB");
        public static readonly Color BoundBadgeBg = QdaoUguiTheme.Html("#247052");

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            string path = key switch
            {
                "hud-entry-base" => "UI/Ugui/TournamentV1/hud-entry-base",
                "window-frame" => "UI/Ugui/MailV1/window_frame",
                "title-plate" => "UI/Ugui/MailV1/title_plate",
                "button-primary" => "UI/Ugui/MailV1/button_primary",
                "button-secondary" => "UI/Ugui/MailV1/button_secondary",
                "close-button" => "UI/Ugui/MailV1/close_button",
                "close-tassel" => "UI/Ugui/MailV1/close_tassel",
                "notice-icon" => "UI/Ugui/MailV1/notice_icon",
                "taiji-badge" => "UI/Ugui/MailV1/round_badge_taiji",
                "portrait-frame" => "UI/Ugui/TeamV2/portrait_frame",
                "status-online" => "UI/Ugui/TeamV2/status_online",
                "status-offline" => "UI/Ugui/TeamV2/status_offline",
                "icon-chest" => "UI/Ugui/MailV1/icon-chest",
                "icon-pill" => "UI/Ugui/MailV1/icon-pill",
                "icon-scroll" => "UI/Ugui/MailV1/icon-scroll",
                "icon-talisman" => "UI/Ugui/MailV1/icon-talisman",
                // V2 Module A（BO3 对阵、五行灵脉、局间换阵）
                "v2-a-element-metal" => "UI/Ugui/TournamentV2/a/element-metal",
                "v2-a-element-wood" => "UI/Ugui/TournamentV2/a/element-wood",
                "v2-a-element-water" => "UI/Ugui/TournamentV2/a/element-water",
                "v2-a-element-fire" => "UI/Ugui/TournamentV2/a/element-fire",
                "v2-a-element-earth" => "UI/Ugui/TournamentV2/a/element-earth",
                "v2-a-intermission-panel" => "UI/Ugui/TournamentV2/a/intermission-panel",
                "v2-a-score-bo3" => "UI/Ugui/TournamentV2/a/score-bo3",
                "v2-a-button-primary" => "UI/Ugui/TournamentV2/a/button-primary",
                // V2 Module B（冠军基座、牌匾）
                "v2-b-champion-pedestal" => "UI/Ugui/TournamentV2/b/champion-pedestal",
                "v2-b-champion-plaque" => "UI/Ugui/TournamentV2/b/champion-plaque",
                // V2 Module C（观战底部喝彩条、6 预设完整短语图、对推条与飘字）
                "v2-c-cheer-bar" => "UI/Ugui/TournamentV2/c/cheer-bar",
                "v2-c-cheer-float" => "UI/Ugui/TournamentV2/c/cheer-float",
                "v2-c-cheer-pill-disabled" => "UI/Ugui/TournamentV2/c/cheer-pill-disabled",
                "v2-c-cheer-pill-normal" => "UI/Ugui/TournamentV2/c/cheer-pill-normal",
                "v2-c-cheer-pill-selected" => "UI/Ugui/TournamentV2/c/cheer-pill-selected",
                "v2-c-phrase-01" => "UI/Ugui/TournamentV2/c/phrase-01",
                "v2-c-phrase-02" => "UI/Ugui/TournamentV2/c/phrase-02",
                "v2-c-phrase-03" => "UI/Ugui/TournamentV2/c/phrase-03",
                "v2-c-phrase-04" => "UI/Ugui/TournamentV2/c/phrase-04",
                "v2-c-phrase-05" => "UI/Ugui/TournamentV2/c/phrase-05",
                "v2-c-phrase-06" => "UI/Ugui/TournamentV2/c/phrase-06",
                "v2-c-popularity-coral" => "UI/Ugui/TournamentV2/c/popularity-coral",
                "v2-c-popularity-jade" => "UI/Ugui/TournamentV2/c/popularity-jade",
                "v2-c-popularity-track" => "UI/Ugui/TournamentV2/c/popularity-track",
                // V2 原生毛笔题字（严禁叠字）
                "v2-title-intermission" => "UI/Ugui/TournamentV2/titles/a-intermission-title",
                "v2-title-cheer" => "UI/Ugui/TournamentV2/titles/c-cheer-title",
                // V3 Module D（赛前零积分热身约战）
                "v3-d-warmup-btn" => "UI/Ugui/TournamentV3/d/warmup-btn",
                "v3-d-warmup-badge" => "UI/Ugui/TournamentV3/d/warmup-badge",
                // V3 Module E（淘汰赛神机问道免费竞猜）
                "v3-e-predict-panel" => "UI/Ugui/TournamentV3/e/predict-panel",
                "v3-e-predict-pick" => "UI/Ugui/TournamentV3/e/predict-pick",
                "v3-e-predict-hit" => "UI/Ugui/TournamentV3/e/predict-hit",
                "v3-e-predict-miss" => "UI/Ugui/TournamentV3/e/predict-miss",
                // V3 Module F（淘汰赛问鼎之星 MVP 与高光战报卡）
                "v3-f-mvp-seal" => "UI/Ugui/TournamentV3/f/mvp-seal",
                "v3-f-mvp-card-bg" => "UI/Ugui/TournamentV3/f/mvp-card-bg",
                // V3 Module G（季军争夺战牌匾 + 8 枚官方预设道家战旗纹章）
                "v3-g-bronze-banner" => "UI/Ugui/TournamentV3/g/bronze-banner",
                "v3-g-emblem-01" => "UI/Ugui/TournamentV3/g/emblem-01-sword",
                "v3-g-emblem-02" => "UI/Ugui/TournamentV3/g/emblem-02-taiji",
                "v3-g-emblem-03" => "UI/Ugui/TournamentV3/g/emblem-03-furnace",
                "v3-g-emblem-04" => "UI/Ugui/TournamentV3/g/emblem-04-lotus",
                "v3-g-emblem-05" => "UI/Ugui/TournamentV3/g/emblem-05-crane",
                "v3-g-emblem-06" => "UI/Ugui/TournamentV3/g/emblem-06-thunder",
                "v3-g-emblem-07" => "UI/Ugui/TournamentV3/g/emblem-07-bagua",
                "v3-g-emblem-08" => "UI/Ugui/TournamentV3/g/emblem-08-cauldron",
                // V3 无字标题底板（320×64，按固定比例置入 300×44 区域并由运行时动态叠字）
                "v3-title-warmup" => "UI/Ugui/TournamentV3/titles/d-warmup-title",
                "v3-title-predict" => "UI/Ugui/TournamentV3/titles/e-predict-title",
                "v3-title-emblem" => "UI/Ugui/TournamentV3/titles/g-emblem-title",
                // V4 Module H/I/J（BO3 灵脉禁选、本季名局录、6 枚赛季荣耀铭牌）
                "v4-h-ban-seal" => "UI/Ugui/TournamentV3/h/ban-seal",
                "v4-i-highlights-panel" => "UI/Ugui/TournamentV3/i/highlights-panel",
                "v4-title-highlights" => "UI/Ugui/TournamentV3/titles/i-highlights-title",
                "v4-j-honor-01" => "UI/Ugui/TournamentV3/j/honor-01-debut",
                "v4-j-honor-02" => "UI/Ugui/TournamentV3/j/honor-02-streak",
                "v4-j-honor-03" => "UI/Ugui/TournamentV3/j/honor-03-unbeaten",
                "v4-j-honor-04" => "UI/Ugui/TournamentV3/j/honor-04-comeback",
                "v4-j-honor-05" => "UI/Ugui/TournamentV3/j/honor-05-sweep",
                "v4-j-honor-06" => "UI/Ugui/TournamentV3/j/honor-06-champion",
                _ => key,
            };
            var sprite = Resources.Load<Sprite>(path);
            if (sprite != null) Cache[key] = sprite;
            return sprite;
        }

        /// <summary>从玩家真实外观目录解析获批立绘；未知或跨服未同步时返回 null（显示中性占位）。</summary>
        public static Sprite ResolvePlayerPortrait(Func<ulong, string> resolveCharacterId, ulong playerId)
        {
            if (resolveCharacterId == null || playerId == 0UL) return null;
            string charId = resolveCharacterId(playerId);
            if (string.IsNullOrEmpty(charId)) return null;
            if (QdaoCharacterCatalog.Find(charId) == null) return null;
            return QdaoCharacterCatalog.LoadPortrait(charId);
        }

        public static Sprite ResolveCharacterPortrait(string charId, Func<ulong, string> resolveCharacterId, ulong playerId)
        {
            if (!string.IsNullOrEmpty(charId) && QdaoCharacterCatalog.Find(charId) != null)
                return QdaoCharacterCatalog.LoadPortrait(charId);
            return ResolvePlayerPortrait(resolveCharacterId, playerId);
        }

        /// <summary>V2：优先按成员权威外观快照（AppearanceId / ClassId+Gender）解析真实获批立绘；缺数据时严格返回 null（中性太极占位，绝不用演示图冒充）。</summary>
        public static Sprite ResolveMemberPortrait(TournamentMemberView member, Func<ulong, string> resolveCharacterId)
        {
            if (member == null) return null;
            if (!string.IsNullOrEmpty(member.AppearanceId))
            {
                string vis = QdaoCharacterCatalog.ResolveVisualId(member.AppearanceId);
                if (!string.IsNullOrEmpty(vis) && QdaoCharacterCatalog.Find(vis) != null)
                    return QdaoCharacterCatalog.LoadPortrait(vis);
            }
            if (member.ClassId >= 1u && member.ClassId <= 4u)
            {
                string vis = QdaoCharacterCatalog.ResolveRole(member.ClassId, member.Gender);
                if (!string.IsNullOrEmpty(vis) && QdaoCharacterCatalog.Find(vis) != null)
                    return QdaoCharacterCatalog.LoadPortrait(vis);
            }
            return ResolvePlayerPortrait(resolveCharacterId, member.PlayerId);
        }

        /// <summary>V2 主操作按钮（使用 v2-a-button-primary 184×44 原图皮肤 + 独立楷体字）。</summary>
        public static Button V2PrimaryButton(string name, Transform parent, float x, float y, float w, float h,
            string labelText, Action onClick, bool interactable = true, float fontSize = 20f)
        {
            var sprite = Load("v2-a-button-primary") ?? Load("button-primary");
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = false;
            if (sprite == null) img.color = Jade;
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var txt = Label(name + "_Label", btn.transform, 10f, 0f, w - 20f, h, labelText,
                fontSize, PrimaryBtnText, TextAlignmentOptions.Center, bold: true);
            txt.enableAutoSizing = true;
            txt.fontSizeMin = Mathf.Max(13f, fontSize - 6f);
            txt.fontSizeMax = fontSize;
            return btn;
        }

        /// <summary>现查 ItemTable 获取物品中文名，不缓存行引用，防热更失效。</summary>
        public static string ResolveItemName(uint itemId)
        {
            if (itemId == 0u) return "物品";
            var row = ItemTableManager.Instance.FindById(itemId);
            if (row != null && !string.IsNullOrEmpty(row.Name)) return row.Name;
            return itemId switch
            {
                3403u => "重铸砂",
                3406u => "悟道残页",
                3407u => "绝学残卷",
                20u => "灵兽口粮",
                _ => $"道具 #{itemId}",
            };
        }

        public static Sprite ResolveItemIcon(uint itemId) => itemId switch
        {
            3403u => QdaoUguiTheme.LoadSprite("UI/Icons/item_reforge_sand") ?? Load("icon-pill"),
            3406u => QdaoUguiTheme.LoadSprite("UI/Ugui/CraftQualitySkill/Icons/item_enlightenment_page") ?? Load("icon-scroll"),
            3407u => QdaoUguiTheme.LoadSprite("UI/Ugui/CraftQualitySkill/Icons/item_rare_scroll_fragment") ?? Load("icon-scroll"),
            20u => Load("icon-talisman"),
            _ => Load("icon-chest"),
        };

        public static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; --i)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }

        public static Image Box(string name, Transform parent, float x, float y, float w, float h,
            Color color, bool raycast = false)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null, raycast);
            img.color = color;
            return img;
        }

        public static RectTransform BorderedCard(string name, Transform parent, float x, float y, float w, float h,
            Color fill, Color border, float borderPx = 1.5f)
        {
            var outer = Box(name, parent, x, y, w, h, border, false).rectTransform;
            var inner = QdaoUguiFactory.CreateStretch("Inner", outer,
                new Vector4(borderPx, borderPx, borderPx, borderPx));
            var innerImg = inner.gameObject.AddComponent<Image>();
            innerImg.color = fill;
            innerImg.raycastTarget = false;
            return outer;
        }

        public static Image SpriteBox(string name, Transform parent, float x, float y, float w, float h,
            string spriteKey, bool sliced = false, bool preserveAspect = false, bool raycast = false)
        {
            var sprite = Load(spriteKey);
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite, raycast);
            img.type = sliced && sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = preserveAspect;
            if (sprite == null) img.color = Paper;
            return img;
        }

        public static TMP_Text Label(string name, Transform parent, float x, float y, float w, float h,
            string text, float fontSize, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
            bool bold = false, bool wrap = false)
        {
            var tmp = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, text ?? string.Empty, fontSize, color, alignment);
            if (bold) tmp.fontStyle = FontStyles.Bold;
            if (wrap)
            {
                tmp.textWrappingMode = TextWrappingModes.Normal;
                tmp.overflowMode = TextOverflowModes.Truncate;
            }
            return tmp;
        }

        public static Button ArtButton(string name, Transform parent, float x, float y, float w, float h,
            string labelText, Action onClick, bool primary = true, bool interactable = true, float fontSize = 22f)
        {
            var sprite = Load(primary ? "button-primary" : "button-secondary");
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            img.type = Image.Type.Simple;
            if (sprite == null) img.color = primary ? Jade : CardAltBg;
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var txt = Label(name + "_Label", btn.transform, 10f, 0f, w - 20f, h, labelText,
                fontSize, primary ? PrimaryBtnText : SecondaryBtnText, TextAlignmentOptions.Center, bold: true);
            txt.enableAutoSizing = true;
            txt.fontSizeMin = Mathf.Max(13f, fontSize - 7f);
            txt.fontSizeMax = fontSize;
            return btn;
        }

        public static Button FlatButton(string name, Transform parent, float x, float y, float w, float h,
            string labelText, Color bg, Color border, Color textColor, Action onClick,
            bool interactable = true, float fontSize = 18f, bool bold = true)
        {
            var outer = Box(name, parent, x, y, w, h, border, true);
            var inner = QdaoUguiFactory.CreateStretch("Bg", outer.rectTransform, new Vector4(1.5f, 1.5f, 1.5f, 1.5f));
            var innerImg = inner.gameObject.AddComponent<Image>();
            innerImg.color = bg;
            innerImg.raycastTarget = false;

            var btn = outer.gameObject.AddComponent<Button>();
            btn.targetGraphic = outer;
            btn.interactable = interactable;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.97f, 0.90f, 1f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.disabledColor = new Color(0.72f, 0.72f, 0.72f, 0.58f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var txt = Label("Label", outer.rectTransform, 6f, 0f, w - 12f, h, labelText,
                fontSize, textColor, TextAlignmentOptions.Center, bold: bold);
            txt.enableAutoSizing = true;
            txt.fontSizeMin = Mathf.Max(12f, fontSize - 5f);
            txt.fontSizeMax = fontSize;
            return btn;
        }

        public static void Tag(string name, Transform parent, float x, float y, float w, float h,
            string text, Color bg, Color border, Color textColor, float fontSize = 14f)
        {
            var card = BorderedCard(name, parent, x, y, w, h, bg, border, 1.2f);
            var lbl = Label("Text", card, 4f, 0f, w - 8f, h, text, fontSize, textColor,
                TextAlignmentOptions.Center, bold: true);
            lbl.enableAutoSizing = true;
            lbl.fontSizeMin = 11f;
            lbl.fontSizeMax = fontSize;
        }

        /// <summary>绘制成员头像圆章：已知真实形象加载立绘，未知或空位绘制玉青太极中性占位。</summary>
        public static void AvatarMedallion(string name, Transform parent, float x, float y, float size,
            Sprite portrait, bool isEmptySlot = false)
        {
            var root = QdaoUguiFactory.CreateRect(name, parent, x, y, size, size);
            var bg = QdaoUguiFactory.CreateStretch("Bg", root, new Vector4(4f, 4f, 4f, 4f)).gameObject.AddComponent<Image>();
            bg.color = isEmptySlot ? CardAltBg : JadeSoft;
            bg.raycastTarget = false;

            if (isEmptySlot)
            {
                Label("Plus", root, 0f, 0f, size, size, "+", size * 0.48f, Gold, TextAlignmentOptions.Center, bold: true);
            }
            else if (portrait != null)
            {
                var pic = QdaoUguiFactory.CreateImage("Portrait", root, 6f, 6f, size - 12f, size - 12f, portrait, false);
                pic.preserveAspect = true;
            }
            else
            {
                var taiji = Load("taiji-badge");
                if (taiji != null)
                {
                    var badge = QdaoUguiFactory.CreateImage("NeutralTaiji", root, size * 0.16f, size * 0.16f,
                        size * 0.68f, size * 0.68f, taiji, false);
                    badge.preserveAspect = true;
                    badge.color = new Color(1f, 1f, 1f, 0.85f);
                }
                else
                {
                    Label("NeutralGlyph", root, 0f, 0f, size, size, "道", size * 0.4f, Jade,
                        TextAlignmentOptions.Center, bold: true);
                }
            }

            var frame = Load("portrait-frame");
            if (frame != null)
            {
                var f = QdaoUguiFactory.CreateImage("Frame", root, 0f, 0f, size, size, frame, false);
                f.preserveAspect = true;
            }
        }

        /// <summary>V3 模块 G：绘制官方预设道家战队纹章（已导图时直显透明 PNG；缺图时优雅降级为道家玉金字章）。</summary>
        public static RectTransform EmblemBadge(string name, Transform parent, float x, float y, float size, uint emblemId)
        {
            var info = MmorpgClient.Game.Tournament.TournamentFormatting.GetEmblemInfo(emblemId);
            var sprite = Load(info.SpriteKey);
            if (sprite != null)
            {
                var img = QdaoUguiFactory.CreateImage(name, parent, x, y, size, size, sprite, false);
                img.preserveAspect = true;
                return img.rectTransform;
            }
            var card = BorderedCard(name, parent, x, y, size, size, JadeSoft, Gold, Mathf.Max(1.2f, size * 0.04f));
            Label("Glyph", card, 0f, 0f, size, size, info.Glyph, Mathf.Max(12f, size * 0.52f),
                JadeDeep, TextAlignmentOptions.Center, bold: true);
            return card;
        }

        /// <summary>V4 模块 J：绘制赛季纯荣誉铭牌徽章（已导图时直显 PNG，未解锁态半透明灰化，缺图时降级为玉金字章）。</summary>
        public static RectTransform HonorBadge(string name, Transform parent, float x, float y, float size,
            MmorpgClient.Game.Tournament.TournamentFormatting.TournamentHonorInfo info, bool unlocked)
        {
            var sprite = Load(info.SpriteKey);
            if (sprite != null)
            {
                var img = QdaoUguiFactory.CreateImage(name, parent, x, y, size, size, sprite, false);
                img.preserveAspect = true;
                img.color = unlocked ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.42f);
                return img.rectTransform;
            }
            var card = BorderedCard(name, parent, x, y, size, size,
                unlocked ? GoldTagBg : TagGrayBg,
                unlocked ? Gold : Line,
                Mathf.Max(1.2f, size * 0.04f));
            Label("Glyph", card, 0f, 0f, size, size, info.ShortGlyph, Mathf.Max(12f, size * 0.48f),
                unlocked ? GoldTagText : Muted, TextAlignmentOptions.Center, bold: unlocked);
            return card;
        }

        /// <summary>V3 皮肤按钮（加载到深玉金边切片时固定配暖金高亮字并留出左右雕饰安全区，缺图时回退道家标准样式）。</summary>
        public static Button V3SkinnedButton(string name, Transform parent, float x, float y, float w, float h,
            string spriteKey, string labelText, Action onClick, bool primaryFallback = false,
            bool interactable = true, float fontSize = 18f)
        {
            var sprite = Load(spriteKey);
            if (sprite == null)
                return ArtButton(name, parent, x, y, w, h, labelText, onClick, primary: primaryFallback, interactable: interactable, fontSize: fontSize);
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = false;
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            float padX = Mathf.Min(18f, w * 0.12f);
            var txt = Label(name + "_Label", btn.transform, padX, 0f, w - padX * 2f, h, labelText,
                fontSize, PrimaryBtnText, TextAlignmentOptions.Center, bold: true);
            txt.enableAutoSizing = true;
            txt.fontSizeMin = Mathf.Max(11f, fontSize - 6f);
            txt.fontSizeMax = fontSize;
            return btn;
        }

        /// <summary>V3/V4 无字标题条幅（320×64 固定比例居中呈现于原 300×44 标题区，不拉伸变形，居中动态叠字）。</summary>
        public static Image ModalTitleBanner(string name, Transform parent, float x, float y, float w, float h,
            string spriteKey, string titleText)
        {
            var sprite = Load(spriteKey);
            if (sprite == null)
            {
                Label(name + "Fallback", parent, x, y + 2f, w, h - 4f, titleText,
                    28f, JadeDeep, TextAlignmentOptions.Center, bold: true);
                return null;
            }
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite, false);
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : (w / Mathf.Max(1f, h));
            float drawW = Mathf.Min(w, h * aspect);
            float drawH = Mathf.Min(h, drawW / Mathf.Max(0.01f, aspect));
            float insetX = (w - drawW) * 0.5f + Mathf.Max(12f, drawW * 0.08f);
            float insetY = (h - drawH) * 0.5f + 4f;
            var txt = Label(name + "_Text", img.transform, insetX, insetY,
                Mathf.Max(20f, w - insetX * 2f), Mathf.Max(16f, h - insetY * 2f),
                titleText, 20f, PrimaryBtnText, TextAlignmentOptions.Center, bold: true);
            txt.enableAutoSizing = true;
            txt.fontSizeMin = 13f;
            txt.fontSizeMax = 21f;
            txt.raycastTarget = false;
            BattleUiWidgets.ApplyOutline(txt, 0.16f, new Color32(14, 38, 28, 220));
            return img;
        }

        /// <summary>在无字手绘印章/徽章中央叠加高对比动态文字（带深色描边确保小尺寸清晰可读）。</summary>
        public static TMP_Text SealOverlayLabel(Transform sealParent, float x, float y, float w, float h,
            string text, float fontSize, float fontSizeMin, Color32 outlineColor)
        {
            var lbl = Label("SealTxt", sealParent, x, y, w, h, text, fontSize, PrimaryBtnText,
                TextAlignmentOptions.Center, bold: true);
            lbl.enableAutoSizing = true;
            lbl.fontSizeMin = fontSizeMin;
            lbl.fontSizeMax = fontSize;
            lbl.raycastTarget = false;
            BattleUiWidgets.ApplyOutline(lbl, 0.28f, outlineColor);
            return lbl;
        }

        public static ScrollRect CreateScrollView(string name, Transform parent, float x, float y, float w, float h,
            bool horizontal, bool vertical, out RectTransform content)
        {
            var viewport = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            content = QdaoUguiFactory.CreateRect("Content", viewport, 0f, 0f, w, h);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = horizontal;
            scroll.vertical = vertical;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            return scroll;
        }
    }
}
