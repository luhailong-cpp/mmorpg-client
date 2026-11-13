using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MmorpgClient.World.Tianyong;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 临时格面板:列出临时格里的物品,逐件「取回」与「全部取回」(服务端 docs/design/bag-rule-policy-layering.md §6.6)。
    /// 只发意图、等权威回包:列表、占用、在途与失败原因都由 <see cref="Render"/> 按数据层状态整份重画,不做本地假成功。
    /// 战斗中服务器照样放行取回,这里不按战斗状态置灰。
    /// 没有对应的效果图:沿用装备弹窗的底图与按钮皮肤,布局只保证功能可用,视觉验收另算
    /// (客户端 AGENTS.md「UI 原图还原验收」)。
    /// </summary>
    public sealed class TemporaryBagDialog
    {
        public const int RowsPerPage = 4;
        private const float RowHeight = 72;
        private static readonly Color Alert = QdaoUguiTheme.Html("#9A442D");

        /// <summary>参数是临时格里物品的实例 id。</summary>
        public event Action<ulong> RetrieveRequested;
        public event Action RetrieveAllRequested;
        public event Action RefreshRequested;

        private readonly RectTransform _root, _list;
        private readonly TMP_Text _occupancy, _explanation, _status, _pageText;
        private readonly Button _retrieveAll, _refresh, _close, _previous, _next;
        private readonly EquipWishDialogInput _input;
        private Action _closed;
        private GameObject _returnFocus;
        private int _page;
        // 最近一次 Render 的输入:翻页时按它重画。
        private BagInfo _temporary;
        private bool _loading, _busy;
        private string _error = string.Empty;

        public bool IsVisible => _root.gameObject.activeSelf;

        public TemporaryBagDialog(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("TemporaryBagDialog", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .76f); shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            _input = _root.gameObject.AddComponent<EquipWishDialogInput>();
            _input.CancelRequested = Close;
            var frame = QdaoUguiFactory.CreateCenteredRect("TemporaryBagPanel", _root, 1000, 780);
            Art(frame, "main_frame", 0, 0, 1000, 780);
            Art(frame, "title_plate", 260, -20, 480, 114);
            Text(frame, "临时格", 300, -5, 400, 84, 42, Cream, alignment: TextAlignmentOptions.Center);
            _occupancy = Text(frame, "", 70, 108, 860, 46, 32, Ink, alignment: TextAlignmentOptions.Center);
            _occupancy.name = "TemporaryOccupancy";
            _explanation = Text(frame, "", 70, 160, 860, 82, 24, Muted, true);
            _explanation.name = "TemporaryExplanation";
            _list = QdaoUguiFactory.CreateRect("TemporaryRows", frame, 70, 250, 860, RowsPerPage * RowHeight);
            _previous = Button(frame, "上一页", 560, 546, 120, 50, () => { --_page; Redraw(); }, fontSize: 24);
            _previous.name = "TemporaryPreviousPage";
            _pageText = Text(frame, "", 690, 546, 110, 50, 24, Muted, alignment: TextAlignmentOptions.Center);
            _next = Button(frame, "下一页", 810, 546, 120, 50, () => { ++_page; Redraw(); }, fontSize: 24);
            _next.name = "TemporaryNextPage";
            _status = Text(frame, "", 70, 604, 860, 72, 24, Muted, true);
            _status.name = "TemporaryStatus";
            _retrieveAll = Button(frame, "全部取回", 70, 690, 300, 70, () => RetrieveAllRequested?.Invoke(), true);
            _retrieveAll.name = "TemporaryRetrieveAll";
            _refresh = Button(frame, "刷新", 390, 690, 240, 70, () => RefreshRequested?.Invoke());
            _refresh.name = "TemporaryRefresh";
            _close = Button(frame, "关闭", 650, 690, 280, 70, Close);
            _close.name = "TemporaryClose";
        }

        /// <summary>打开面板,回到第一页,按最近一次 <see cref="Render"/> 的状态画出来。</summary>
        /// <param name="closed">面板关掉之后调用一次(宿主据此恢复背包页的输入)。</param>
        public void Show(Action closed)
        {
            _returnFocus = EventSystem.current?.currentSelectedGameObject;
            _closed = closed;
            _page = 0;
            Redraw();
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            _input.FocusFirst();
        }

        public void Close()
        {
            if (!IsVisible) return;
            var callback = _closed;
            _closed = null;
            _root.gameObject.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(
                _returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null;
            callback?.Invoke();
        }

        /// <summary>按数据层此刻的状态整份重画。temporary 为 null = 还没拉到(或断线后已作废)。</summary>
        public void Render(BagInfo temporary, bool loading, string error, bool busy)
        {
            _temporary = temporary;
            _loading = loading;
            _error = error ?? string.Empty;
            _busy = busy;
            Redraw();
        }

        private void Redraw()
        {
            var items = TemporaryBagModel.Items(_temporary);
            int pages = Math.Max(1, (items.Count + RowsPerPage - 1) / RowsPerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            bool canAct = !_busy && !_loading && _temporary?.Layout != null;

            _occupancy.text = TemporaryBagModel.OccupancyText(_temporary);
            _explanation.text = TemporaryBagModel.Explain(_temporary);
            _explanation.color = TemporaryBagModel.IsNearlyFull(_temporary) ? Alert : Muted;

            Clear(_list);
            var controls = new List<Selectable>();
            for (int row = 0; row < RowsPerPage; ++row)
            {
                int index = _page * RowsPerPage + row;
                if (index >= items.Count) break;
                var item = items[index];
                float y = row * RowHeight;
                ItemIcon(_list, item.IconKey, 0, y + 4, 60);
                Text(_list, TemporaryBagModel.RowText(item), 76, y, 600, 68, 28, Ink).name = "TemporaryItem_" + item.ItemId;
                ulong id = item.ItemId;
                var take = Button(_list, "取回", 690, y + 6, 170, 56, () => RetrieveRequested?.Invoke(id),
                    enabled: canAct, fontSize: 26);
                take.name = "TemporaryRetrieve_" + id;
                controls.Add(take);
            }
            if (_temporary?.Layout == null)
                Text(_list, _loading ? "正在读取临时格…" : "临时格尚未同步，请点击刷新", 0, 110, 860, 60, 28, Muted,
                    alignment: TextAlignmentOptions.Center);
            else if (items.Count == 0)
                Text(_list, "临时格是空的", 0, 110, 860, 60, 28, Muted, alignment: TextAlignmentOptions.Center);

            bool paged = pages > 1;
            _previous.gameObject.SetActive(paged);
            _next.gameObject.SetActive(paged);
            _pageText.gameObject.SetActive(paged);
            _previous.interactable = _page > 0;
            _next.interactable = _page + 1 < pages;
            _pageText.text = $"{_page + 1}/{pages}";
            if (paged) { controls.Add(_previous); controls.Add(_next); }

            _retrieveAll.interactable = canAct && items.Count > 0;
            _refresh.interactable = !_busy && !_loading;
            bool failed = !_busy && _error.Length != 0;
            _status.text = _busy ? "正在取回…" : failed ? _error : _loading ? "正在读取临时格…" : string.Empty;
            _status.color = failed ? Alert : Muted;

            controls.Add(_retrieveAll);
            controls.Add(_refresh);
            controls.Add(_close);
            _input.Controls = controls;
        }
    }
}
