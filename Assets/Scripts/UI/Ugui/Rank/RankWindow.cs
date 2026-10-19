using System;
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
    /// </summary>
    public sealed class RankWindow
    {
        public const int RowCount = (int)RankClient.PageSize;
        public event Action<uint> PageRequested;
        public event Action RefreshRequested;
        public event Action AroundRequested;
        public bool IsVisible => _root.gameObject.activeSelf;

        private readonly RectTransform _root, _list;
        private readonly TMP_Text _status, _pageLabel, _selfRank, _selfName, _selfRating, _heading;
        private readonly Button _previous, _next, _refresh, _around;
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
            Text(frame, "全服论剑评分排行 · 同分先达到者在前", 590, 40, 860, 58, 28, Muted);
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
            if (entries != null)
            {
                for (int i = 0; i < entries.Count && i < RowCount; i++)
                {
                    var entry = entries[i];
                    float y = i * 94;
                    bool mine = entry.EntityId == me && me != 0;
                    var row = Art(_list, mine ? "tab_selected" : "tab_normal", 0, y, 1488, 84);
                    row.name = "RankRow_" + entry.Rank;
                    var ink = mine ? Cream : Ink;
                    Text(_list, entry.Rank.ToString("D2"), 20, y + 14, 160, 56, 31, mine ? Cream : Gold);
                    Text(_list, DisplayName(entry), 184, y + 14, 560, 56, 31, ink);
                    Text(_list, RankClient.FormatRating(entry.Score), 1044, y + 14, 340, 56, 30, ink);
                }
            }
            RenderSelf(around?.Self ?? page?.Self);
            uint current = _client?.CurrentPage ?? 1;
            ulong pages = _client?.PageCount ?? 1UL;
            _pageLabel.text = around != null ? "我的附近" : current + " / " + pages;
            _heading.text = page?.State == RankGenerationState.RankGenerationSealed ? "论剑榜 · 已结算" : "论剑榜";
            _previous.interactable = !busy && page != null && current > 1;
            _next.interactable = !busy && page != null && current < pages && current < uint.MaxValue;
            _refresh.interactable = !busy;
            _around.interactable = !busy;
            _status.text = _client?.Status ?? "";
        }

        /// <summary>服务端名字留空(补全失败 / 未登记)时回落成"道友 · 编号",不显示空行。</summary>
        public static string DisplayName(RankEntry entry) =>
            string.IsNullOrWhiteSpace(entry.Name) ? "道友 · " + entry.EntityId : entry.Name;

        private void RenderSelf(RankEntry self)
        {
            if (self == null || self.Rank == 0)
            {
                _selfRank.text = "我的名次";
                _selfName.text = _client?.Page == null && _client?.Around == null ? "" : "暂未上榜";
                _selfRating.text = "";
                return;
            }
            _selfRank.text = "第 " + self.Rank + " 名";
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
