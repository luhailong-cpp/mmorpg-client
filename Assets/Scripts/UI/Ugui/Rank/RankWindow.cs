using System;
using System.Collections.Generic;
using MmorpgClient.Game.Rank;
using MmorpgClient.World.Tianyong;
using Rankpb;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Rank
{
    /// <summary>
    /// 论剑榜窗口(服务端 docs/design/leaderboard-system.md §4.6):一页 5 行 + 固定在列表下方的"我的名次" + 翻页。
    /// 窗口只渲染 <see cref="RankClient"/> 的快照、发出翻页 / 刷新意图;请求、代次与隔离都在 RankClient 里。
    /// 名字补全失败时服务端名字留空(fail-open,§4.3),这里回落成"道友 · 编号",与帮会成员列表同一口径。
    /// 名次只精确到回包的 rank_limit 名(2026-11-11):排在其后的本人 / 附近条目显示「{rank_limit}+」,翻页也只到这一名。
    /// 按模式分榜(2026-11-12,服务端 global-match-hierarchical-pools.md §8 R5):标题右侧三个页签切换三张评分榜,
    /// 选中哪张由 RankClient.SelectedBoardType 决定,窗口只发 <see cref="BoardSelected"/>。
    /// 页签的形态、位置、文字与选中态没有设计稿,沿用本窗口已有的 tab_normal / tab_selected 素材,待设计确认后按稿还原。
    /// </summary>
    public sealed class RankWindow
    {
        public const int RowCount = (int)RankClient.PageSize;
        /// <summary>"我的附近"取本人前后各几名:正好排满一页。</summary>
        public const uint AroundRadius = (RankClient.PageSize - 1) / 2;
        /// <summary>
        /// 模式页签,从左到右。默认榜(PVP_TEAM,客户端「匹配 PVP」入口排的就是它)放最前;
        /// 1V1 / 5V5 是另外两条评分轨道,客户端暂无排队入口。
        /// </summary>
        public static readonly IReadOnlyList<(uint BoardType, string Label)> BoardTabs = new[]
        {
            ((uint)RankBoardType.MatchPvpTeam, "PVP 匹配"),
            ((uint)RankBoardType.Match1V1, "PVP 1v1"),
            ((uint)RankBoardType.Match5V5, "PVP 5v5"),
        };
        private const float TabX = 566, TabY = 34, TabWidth = 176, TabHeight = 66, TabGap = 12;

        public event Action<uint> PageRequested;
        public event Action RefreshRequested;
        public event Action AroundRequested;
        /// <summary>点了某个模式页签(参数是 board_type);是否切换、切换后读什么由 RankClient 决定。</summary>
        public event Action<uint> BoardSelected;
        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        private readonly RectTransform _root, _list;
        private readonly TMP_Text _status, _pageLabel, _selfRank, _selfName, _selfRating, _heading;
        private readonly Button _previous, _next, _refresh, _around;
        private readonly Button[] _boardTabs = new Button[BoardTabs.Count];
        private RankClient _client;

        public RankWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("RankWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("RankFrame", _root, 1640, 924);
            Art(frame, "main_frame", 0, 0, 1640, 924);
            Art(frame, "title_plate", 62, -21, 480, 114);
            _heading = Text(frame, "论剑榜", 100, -7, 400, 84, 49, Cream, alignment: TextAlignmentOptions.Center);
            for (int i = 0; i < BoardTabs.Count; i++)
            {
                uint boardType = BoardTabs[i].BoardType;
                var tab = Button(frame, BoardTabs[i].Label, TabX + i * (TabWidth + TabGap), TabY, TabWidth, TabHeight,
                    () => BoardSelected?.Invoke(boardType), key: "tab_normal", fontSize: 28);
                tab.name = TabName(boardType);
                _boardTabs[i] = tab;
            }
            // 原先占满标题右侧的副标题缩到页签右边;页签已写明模式,「论剑评分」在列头。
            Text(frame, "全服排行 · 同分先达到者在前", 1136, 40, 370, 58, 26, Muted);
            var close = Button(frame, "", 1516, 28, 76, 76, Hide, key: "close");
            close.name = "CloseRank";

            Art(frame, "content_panel", 56, 140, 1528, 560);
            Text(frame, "名次", 96, 156, 140, 46, 27, Gold);
            Text(frame, "道号", 260, 156, 560, 46, 27, Gold);
            Text(frame, "论剑评分", 1120, 156, 340, 46, 27, Gold);
            _list = QdaoUguiFactory.CreateRect("RankRows", frame, 76, 210, 1488, 470);

            Art(frame, "content_panel", 56, 716, 1528, 96);
            _selfRank = Text(frame, "", 96, 736, 160, 56, 30, Gold);
            _selfName = Text(frame, "", 260, 736, 560, 56, 30);
            _selfRating = Text(frame, "", 1120, 736, 340, 56, 30, Muted);

            _status = Text(frame, "", 76, 832, 470, 64, 25, Muted);
            _refresh = Button(frame, "最新榜", 560, 828, 190, 70, () => RefreshRequested?.Invoke(), fontSize: 28);
            _refresh.name = "RankRefresh";
            _around = Button(frame, "我的附近", 760, 828, 190, 70, () => AroundRequested?.Invoke(), fontSize: 28);
            _around.name = "RankAround";
            _previous = Button(frame, "上一页", 970, 828, 190, 70, () => RequestPage(-1), fontSize: 28);
            _previous.name = "RankPrevious";
            _pageLabel = Text(frame, "1 / 1", 1170, 828, 170, 70, 29, Muted, alignment: TextAlignmentOptions.Center);
            _next = Button(frame, "下一页", 1350, 828, 190, 70, () => RequestPage(1), fontSize: 28);
            _next.name = "RankNext";
        }

        public void Show(RankClient client)
        {
            _client = client;
            _root.gameObject.SetActive(true);
            Render();
        }

        public void Hide() => _root.gameObject.SetActive(false);

        /// <summary>按 RankClient 的当前快照重画(由 RankUiRoot 在 Changed 时调用)。</summary>
        public void Render()
        {
            Clear(_list);
            var page = _client?.Page;
            var around = _client?.Around;
            var entries = around?.Entries ?? page?.Entries;
            bool busy = _client?.Waiting == true || _client?.RequiresReconnect == true;
            ulong me = _client?.PlayerId ?? 0;
            ulong rankLimit = _client?.RankLimit ?? 0;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count && i < RowCount; i++)
                {
                    var entry = entries[i];
                    float y = i * 94;
                    bool mine = entry.EntityId == me && me != 0;
                    var row = Art(_list, mine ? "tab_selected" : "tab_normal", 0, y, 1488, 84);
                    // 上限之后的条目名次都是 0,按编号起名,不撞名。
                    row.name = entry.BeyondRankLimit ? "RankRow_Beyond_" + entry.EntityId : "RankRow_" + entry.Rank;
                    var ink = mine ? Cream : Ink;
                    Text(_list, RankClient.FormatRowRank(entry, rankLimit), 20, y + 14, 160, 56, 31, mine ? Cream : Gold);
                    Text(_list, DisplayName(entry), 184, y + 14, 560, 56, 31, ink);
                    Text(_list, RankClient.FormatRating(entry.Score), 1044, y + 14, 340, 56, 30, ink);
                }
            }
            RenderSelf(around?.Self ?? page?.Self, rankLimit);
            uint current = _client?.CurrentPage ?? 1;
            ulong pages = _client?.PageCount ?? 1UL;
            _pageLabel.text = around != null ? "我的附近" : current + " / " + pages;
            _heading.text = page?.State == RankGenerationState.RankGenerationSealed ? "论剑榜 · 已结算" : "论剑榜";
            _previous.interactable = !busy && page != null && current > 1;
            _next.interactable = !busy && page != null && current < pages && current < uint.MaxValue;
            _refresh.interactable = !busy;
            _around.interactable = !busy;
            // 请求在途时也能切榜(只保留最后一次切换);结果未知、等重连时切了也读不了,一并禁用。
            RenderTabs(_client?.SelectedBoardType ?? RankClient.DefaultBoardType, _client != null && !_client.RequiresReconnect);
            _status.text = _client?.Status ?? "";
        }

        /// <summary>页签按钮的对象名,测试与界面验收按它找按钮。</summary>
        public static string TabName(uint boardType) => "RankBoard_" + boardType;

        private void RenderTabs(uint selected, bool enabled)
        {
            for (int i = 0; i < _boardTabs.Length; i++)
            {
                bool active = BoardTabs[i].BoardType == selected;
                var tab = _boardTabs[i];
                tab.image.sprite = Load(active ? "tab_selected" : "tab_normal");
                var label = tab.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.color = active ? Cream : Ink;
                tab.interactable = enabled;
            }
        }

        /// <summary>服务端名字留空(补全失败 / 未登记)时回落成"道友 · 编号",不显示空行。</summary>
        public static string DisplayName(RankEntry entry) =>
            string.IsNullOrWhiteSpace(entry.Name) ? "道友 · " + entry.EntityId : entry.Name;

        private void RenderSelf(RankEntry self, ulong rankLimit)
        {
            // 不在榜上:没有 self,或名次为 0 而又不是「排在精确名次上限之后」。
            if (self == null || (self.Rank == 0 && !self.BeyondRankLimit))
            {
                _selfRank.text = "我的名次";
                _selfName.text = _client?.Page == null && _client?.Around == null ? "" : "暂未上榜";
                _selfRating.text = "";
                return;
            }
            _selfRank.text = RankClient.FormatSelfRank(self, rankLimit);
            _selfName.text = DisplayName(self);
            _selfRating.text = RankClient.FormatRating(self.Score);
        }

        private void RequestPage(int delta)
        {
            if (_client == null || _client.Waiting) return;
            long target = (long)_client.CurrentPage + delta;
            if (target < 1 || (ulong)target > _client.PageCount || target > uint.MaxValue) return;
            PageRequested?.Invoke((uint)target);
        }
    }
}
