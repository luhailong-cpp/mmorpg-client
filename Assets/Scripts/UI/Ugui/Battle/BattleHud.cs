using System;
using System.Collections.Generic;
using System.Text;
using FairyGUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>左上"第 N 回合"金框:回合切换时翻数(旧数上滑淡出、新数下方顶入)。</summary>
    public sealed class BattleRoundCounter
    {
        public const float Width = 300f;
        public const float Height = 70f;

        private readonly RectTransform _root;
        private readonly TMP_Text _a;
        private readonly TMP_Text _b;
        private TMP_Text _front;
        private uint _round;

        public uint Round => _round;

        public BattleRoundCounter(UnityEngine.Transform parent, float x, float y)
        {
            var plate = BattleUiWidgets.CreatePanel("RoundFrame", parent, x, y, Width, Height, BattleUiStyle.PanelBg, false);
            ApplyNineSlice(plate, "panel_9slice");
            _root = plate.rectTransform;
            var frame = BattleUiWidgets.CreatePanel("Gold", _root, 0f, 0f, Width, Height, new Color(0.93f, 0.76f, 0.32f, 0.9f), false);
            var inner = BattleUiWidgets.CreatePanel("Inner", _root, 3f, 3f, Width - 6f, Height - 6f, BattleUiStyle.PanelBg, false);
            inner.transform.SetAsLastSibling();
            var clip = QdaoUguiFactory.CreateRect("Clip", _root, 0f, 0f, Width, Height);
            clip.gameObject.AddComponent<RectMask2D>();
            _a = QdaoUguiFactory.CreateText("RoundA", clip, 0f, 0f, Width, Height, string.Empty, 32f, BattleUiStyle.WarnText, TextAlignmentOptions.Center);
            _b = QdaoUguiFactory.CreateText("RoundB", clip, 0f, Height, Width, Height, string.Empty, 32f, BattleUiStyle.WarnText, TextAlignmentOptions.Center);
            _a.fontStyle = FontStyles.Bold;
            _b.fontStyle = FontStyles.Bold;
            _front = _a;
            frame.transform.SetAsFirstSibling();
        }

        public void SetRound(uint round, bool animate, string suffix = null)
        {
            string text = $"第 {round} 回合{suffix ?? string.Empty}";
            if (!animate || _front.text == text || _round == round && string.IsNullOrEmpty(suffix))
            {
                _round = round;
                _front.text = text;
                return;
            }
            _round = round;
            var back = _front == _a ? _b : _a;
            var outgoing = _front;
            _front = back;
            back.text = text;
            GTween.Kill(_root);
            var outRect = outgoing.rectTransform;
            var inRect = back.rectTransform;
            inRect.anchoredPosition = new Vector2(0f, -Height);
            outRect.anchoredPosition = Vector2.zero;
            GTween.To(0f, 1f, 0.32f).SetEase(EaseType.CubicOut).SetIgnoreEngineTimeScale(true).SetTarget(_root)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (_root == null) return;
                    float k = t.value.x;
                    outRect.anchoredPosition = new Vector2(0f, Height * k);
                    inRect.anchoredPosition = new Vector2(0f, -Height * (1f - k));
                    var c = outgoing.color; c.a = 1f - k; outgoing.color = c;
                    var c2 = back.color; c2.a = k; back.color = c2;
                }))
                .OnComplete((GTweenCallback)(() =>
                {
                    if (_root == null) return;
                    outRect.anchoredPosition = new Vector2(0f, -Height);
                    inRect.anchoredPosition = Vector2.zero;
                }));
        }

        internal static void ApplyNineSlice(Image image, string uiName)
        {
            var sprite = BattleArtCatalog.LoadUiSprite(uiName);
            if (sprite == null) return;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }
    }

    /// <summary>"战斗记录"按钮 + 记录面板(最近 N 行,新行在下)。</summary>
    public sealed class BattleLogPanel
    {
        public const int KeepLines = 80;
        public const int VisibleLines = 18;

        private readonly UiTextButton _button;
        private readonly RectTransform _panel;
        private readonly TMP_Text _text;
        private readonly List<string> _lines = new List<string>();

        public bool IsVisible => _panel != null && _panel.gameObject.activeSelf;

        public BattleLogPanel(UnityEngine.Transform parent, float x, float y)
        {
            _button = BattleUiWidgets.CreateTextButton("BattleLog", parent, x, y, 170f, 54f,
                "战斗记录", 22f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_button.Plate, "button_9slice");
            _button.Button.onClick.AddListener(Toggle);

            var bg = BattleUiWidgets.CreatePanel("BattleLogPanel", parent, 40f, 110f, 720f, 520f, BattleUiStyle.PanelBg);
            BattleRoundCounter.ApplyNineSlice(bg, "panel_9slice");
            _panel = bg.rectTransform;
            QdaoUguiFactory.CreateText("Title", _panel, 24f, 12f, 400f, 36f, "战斗记录", 24f, BattleUiStyle.WarnText);
            var close = BattleUiWidgets.CreateTextButton("Close", _panel, 664f, 10f, 44f, 44f,
                "×", 26f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            close.Button.onClick.AddListener(() => SetVisible(false));
            _text = BattleUiWidgets.CreateWrappedText("Lines", _panel, 24f, 56f, 672f, 450f, string.Empty, 20f, QdaoUguiTheme.StatusCream);
            _panel.gameObject.SetActive(false);
        }

        public void Append(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            _lines.Add(line);
            if (_lines.Count > KeepLines) _lines.RemoveRange(0, _lines.Count - KeepLines);
            RefreshText();
        }

        public void Clear()
        {
            _lines.Clear();
            RefreshText();
        }

        public void Toggle() => SetVisible(!IsVisible);

        public void SetVisible(bool visible)
        {
            if (_panel != null && _panel.gameObject.activeSelf != visible)
            {
                _panel.gameObject.SetActive(visible);
                if (visible) _panel.SetAsLastSibling();
            }
        }

        public void SetButtonVisible(bool visible) => _button.SetVisible(visible);

        private void RefreshText()
        {
            if (_text == null) return;
            var sb = new StringBuilder();
            int start = Mathf.Max(0, _lines.Count - VisibleLines);
            for (int i = start; i < _lines.Count; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(_lines[i]);
            }
            _text.text = sb.ToString();
        }
    }

    /// <summary>头像瓦片数据(行动预告条 / 角色卡共用)。</summary>
    public struct BattleTileInfo
    {
        public string Name;
        public Sprite Portrait;
        public bool IsMine;
        public bool IsSelf;
    }

    /// <summary>
    /// 顶部行动预告条:按出手序横排头像,当前出手者高亮,已出手者变暗。
    /// 20 瓦片满排宽 1476(x 542..2018),避开左上战斗记录键(≤526)与右上计时环(≥2080);
    /// 底边 Top+TileHeight+名字 ≈ 132,舞台按 BattleStage.HudTopBand 让位。
    /// </summary>
    public sealed class BattleActionOrderBar
    {
        public const float TileWidth = 70f;
        public const float TileHeight = 98f;
        public const float Gap = 4f;
        public const float Top = 12f;
        public const int MaxTiles = 20;

        /// <summary>满排(MaxTiles)时的总宽(设计像素)。</summary>
        public static float FullWidth => MaxTiles * TileWidth + (MaxTiles - 1) * Gap;

        private sealed class Tile
        {
            public ulong ActorId;
            public RectTransform Root;
            public Image Frame;
            public CanvasGroup Group;
        }

        private readonly RectTransform _root;
        private readonly List<Tile> _tiles = new List<Tile>();
        private int _current = -1;

        public int Count => _tiles.Count;
        public int CurrentIndex => _current;

        public BattleActionOrderBar(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateRect("ActionOrder", parent, 0f, Top, QdaoUguiTheme.DesignWidth, TileHeight + 8f);
        }

        public void SetOrder(IReadOnlyList<ulong> order, Func<ulong, BattleTileInfo> resolve)
        {
            Clear();
            if (order == null || order.Count == 0) return;
            int n = Mathf.Min(order.Count, MaxTiles);
            float total = n * TileWidth + (n - 1) * Gap;
            float x0 = (QdaoUguiTheme.DesignWidth - total) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                ulong id = order[i];
                var info = resolve != null ? resolve(id) : default;
                var tile = new Tile { ActorId = id };
                tile.Root = QdaoUguiFactory.CreateRect($"Tile_{id}", _root, x0 + i * (TileWidth + Gap), 0f, TileWidth, TileHeight);
                tile.Root.pivot = new Vector2(0.5f, 0.5f);
                tile.Root.anchoredPosition += new Vector2(TileWidth * 0.5f, -TileHeight * 0.5f);
                tile.Group = tile.Root.gameObject.AddComponent<CanvasGroup>();
                tile.Group.blocksRaycasts = false;
                var frameColor = info.IsSelf ? new Color(0.95f, 0.8f, 0.35f, 0.95f)
                    : info.IsMine ? new Color(0.35f, 0.6f, 0.95f, 0.9f) : new Color(0.9f, 0.4f, 0.32f, 0.9f);
                tile.Frame = BattleUiWidgets.CreatePanel("Frame", tile.Root, 0f, 0f, TileWidth, TileWidth, frameColor, false);
                var portrait = QdaoUguiFactory.CreateImage("Portrait", tile.Root, 3f, 3f, TileWidth - 6f, TileWidth - 6f, info.Portrait);
                if (info.Portrait == null) portrait.color = new Color(0.2f, 0.16f, 0.28f, 1f);
                var name = QdaoUguiFactory.CreateText("Name", tile.Root, -10f, TileWidth + 2f, TileWidth + 20f, 20f,
                    info.Name ?? id.ToString(), 14f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
                name.overflowMode = TextOverflowModes.Ellipsis;
                _tiles.Add(tile);
            }
        }

        /// <summary>高亮第一个匹配 actorId 且尚未出手的瓦片(同一单位一回合多次出手时依次推进)。</summary>
        public void Highlight(ulong actorId)
        {
            int found = -1;
            for (int i = _current + 1; i < _tiles.Count; i++)
            {
                if (_tiles[i].ActorId == actorId) { found = i; break; }
            }
            if (found < 0)
            {
                for (int i = 0; i < _tiles.Count; i++)
                {
                    if (_tiles[i].ActorId == actorId) { found = i; break; }
                }
            }
            if (found < 0) return;
            _current = found;
            for (int i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                bool active = i == found;
                tile.Group.alpha = i < found ? 0.45f : 1f;
                tile.Root.localScale = active ? new Vector3(1.15f, 1.15f, 1f) : Vector3.one;
                if (active) tile.Root.SetAsLastSibling();
            }
        }

        /// <summary>回合结束:全部复位为未出手态。</summary>
        public void ResetHighlight()
        {
            _current = -1;
            foreach (var tile in _tiles)
            {
                tile.Group.alpha = 1f;
                tile.Root.localScale = Vector3.one;
            }
        }

        public void Clear()
        {
            foreach (var tile in _tiles)
            {
                if (tile.Root != null) UnityEngine.Object.Destroy(tile.Root.gameObject);
            }
            _tiles.Clear();
            _current = -1;
        }

        public void SetVisible(bool visible)
        {
            if (_root != null) _root.gameObject.SetActive(visible);
        }
    }

    /// <summary>右上角色卡(自己 + 队友):头像 + 名字 + 等级 + 红蓝条,播放中随事件同步。</summary>
    public sealed class BattlePartyCards
    {
        public const float CardWidth = 330f;
        public const float CardHeight = 92f;
        public const float Gap = 8f;
        public const int MaxCards = 4;
        public const float RightMargin = 40f;
        public const float Top = 20f;

        private sealed class Card
        {
            public ulong ActorId;
            public RectTransform Root;
            public RectTransform HpFill;
            public RectTransform MpFill;
            public TMP_Text HpText;
            public ulong MaxHp;
            public ulong MaxMp;
        }

        private const float BarWidth = 216f;

        private readonly RectTransform _root;
        private readonly List<Card> _cards = new List<Card>();

        public int Count => _cards.Count;

        public BattlePartyCards(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateRect("PartyCards", parent, 0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
        }

        /// <summary>第 index 张角色卡的矩形(设计坐标,y 向下)。</summary>
        public static Rect CardRect(int index)
            => new Rect(QdaoUguiTheme.DesignWidth - RightMargin - CardWidth, Top + index * (CardHeight + Gap), CardWidth, CardHeight);

        public void Refresh(IReadOnlyList<BattleActorState> members, Func<ulong, BattleTileInfo> resolve)
        {
            Clear();
            if (members == null) return;
            for (int i = 0; i < members.Count && i < MaxCards; i++)
            {
                var actor = members[i];
                if (actor == null) continue;
                var info = resolve != null ? resolve(actor.ActorId) : default;
                var bg = BattleUiWidgets.CreatePanel($"Card_{actor.ActorId}", _root, CardRect(i).x, CardRect(i).y, CardWidth, CardHeight, BattleUiStyle.PanelBg, false);
                BattleRoundCounter.ApplyNineSlice(bg, "panel_9slice");
                var card = new Card { ActorId = actor.ActorId, Root = bg.rectTransform, MaxHp = actor.MaxHealth, MaxMp = actor.MaxMana };

                var frameColor = info.IsSelf ? new Color(0.95f, 0.8f, 0.35f, 0.95f) : new Color(0.35f, 0.6f, 0.95f, 0.9f);
                BattleUiWidgets.CreatePanel("PortraitFrame", card.Root, 8f, 8f, 76f, 76f, frameColor, false);
                var portrait = QdaoUguiFactory.CreateImage("Portrait", card.Root, 11f, 11f, 70f, 70f, info.Portrait);
                if (info.Portrait == null) portrait.color = new Color(0.2f, 0.16f, 0.28f, 1f);

                QdaoUguiFactory.CreateText("Name", card.Root, 94f, 8f, 150f, 26f, info.Name ?? actor.ActorId.ToString(), 20f,
                    info.IsSelf ? QdaoUguiTheme.Html("#8FE3FF") : QdaoUguiTheme.Cream);
                QdaoUguiFactory.CreateText("Level", card.Root, 246f, 8f, 76f, 26f, $"Lv{actor.Level}", 20f, BattleUiStyle.WarnText, TextAlignmentOptions.MidlineRight);

                var hpBg = BattleUiWidgets.CreatePanel("HpBg", card.Root, 94f, 42f, BarWidth + 4f, 16f, BattleUiStyle.BarBg, false);
                card.HpFill = BattleUiWidgets.CreatePanel("HpFill", hpBg.transform, 2f, 2f, BarWidth, 12f, BattleUiStyle.HpFill, false).rectTransform;
                card.HpText = QdaoUguiFactory.CreateText("HpText", hpBg.transform, 0f, 0f, BarWidth + 4f, 16f, string.Empty, 12f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
                var mpBg = BattleUiWidgets.CreatePanel("MpBg", card.Root, 94f, 64f, BarWidth + 4f, 12f, BattleUiStyle.BarBg, false);
                card.MpFill = BattleUiWidgets.CreatePanel("MpFill", mpBg.transform, 2f, 2f, BarWidth, 8f, BattleUiStyle.MpFill, false).rectTransform;

                _cards.Add(card);
                SetHealth(actor.ActorId, actor.Attributes?.Health ?? 0);
                SetMana(actor.ActorId, actor.Attributes?.Mana ?? 0);
                if (actor.IsDead || actor.Fled)
                {
                    var group = card.Root.gameObject.AddComponent<CanvasGroup>();
                    group.alpha = 0.45f;
                }
            }
        }

        public void SetHealth(ulong actorId, ulong hp)
        {
            var card = Find(actorId);
            if (card == null) return;
            float ratio = card.MaxHp == 0 ? 0f : Mathf.Clamp01((float)((double)hp / card.MaxHp));
            SetWidth(card.HpFill, BarWidth * ratio);
            if (card.HpText != null) card.HpText.text = $"{hp}/{card.MaxHp}";
        }

        public void SetMana(ulong actorId, ulong mp)
        {
            var card = Find(actorId);
            if (card == null) return;
            float ratio = card.MaxMp == 0 ? 0f : Mathf.Clamp01((float)((double)mp / card.MaxMp));
            SetWidth(card.MpFill, BarWidth * ratio);
        }

        public void Clear()
        {
            foreach (var card in _cards)
            {
                if (card.Root != null) UnityEngine.Object.Destroy(card.Root.gameObject);
            }
            _cards.Clear();
        }

        public void SetVisible(bool visible)
        {
            if (_root != null) _root.gameObject.SetActive(visible);
        }

        private Card Find(ulong actorId)
        {
            foreach (var card in _cards)
            {
                if (card.ActorId == actorId) return card;
            }
            return null;
        }

        private static void SetWidth(RectTransform rect, float width)
        {
            if (rect == null) return;
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), rect.sizeDelta.y);
        }
    }
}
