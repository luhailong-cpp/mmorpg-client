using System;
using System.Collections.Generic;
using MmorpgClient.Table;
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
                20u => "灵兽口粮",
                _ => $"道具 #{itemId}",
            };
        }

        public static Sprite ResolveItemIcon(uint itemId) => itemId switch
        {
            3403u => Load("icon-pill"),
            3406u => Load("icon-scroll"),
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
                UnityEngine.Object.Destroy(child);
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
