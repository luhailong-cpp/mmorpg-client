using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.EquipUiStyle;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public enum GameplayPage { Bag, Missions, Activities }

    /// <summary>Production uGUI views; snapshots also allow isolated editor verification.</summary>
    public sealed class GameplayWindow
    {
        public event Action<uint> BagRequested;
        public event Action SortRequested;
        /// <summary>打开 / 刷新背包页时与 <see cref="BagRequested"/> 一起发:装备栏是另一份快照(bag_type = 2)。</summary>
        public event Action EquipmentRequested;
        /// <summary>参数是物品实例 id:穿上人物背包里的装备 / 卸下装备栏里的装备。</summary>
        public event Action<ulong> EquipRequested;
        public event Action<ulong> UnequipRequested;
        public event Action MissionsRequested;
        public event Action ActivitiesRequested;
        public event Action<uint, uint> MissionAcceptRequested;
        public event Action<uint, uint> MissionClaimRequested;
        public event Action<PlayerMissionInfo> TrackingChanged;
        public event Action Closed;
        public bool IsVisible => _root.gameObject.activeSelf;
        public GameplayPage Page { get; private set; }
        private readonly RectTransform _root, _body;
        private readonly TextMeshProUGUI _title, _subtitle;
        private readonly Button[] _tabs = new Button[3];
        private readonly TMP_InputField _search;
        private readonly EquipTooltip _tooltip;
        private BagInfo _bag, _equipment;
        private GetMissionListResponse _missions;
        private GetActivityListResponse _activities;
        private bool _bagLoading, _missionLoading, _activityLoading, _sorting, _missionActionBusy;
        private bool _equipLoading, _equipBusy;
        private string _bagError, _missionError, _activityError, _equipError, _query = "";
        private int _objectivePage;
        private int _bagFilter, _missionFilter, _activityFilter, _bagPage, _missionPage, _activityPage, _equipPage;
        private uint _bagType, _selectedActivity, _characterLevel;
        private ulong _selectedMission, _trackedMission;
        // 玩家点过的物品实例 id(人物背包或装备栏里的都算);它此刻在哪个包里每次重画现查。
        private ulong _selectedItem;

        public GameplayWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("PlayerFeaturesWindow", parent, Vector4.zero);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f); shade.raycastTarget = true;
            var frame = QdaoUguiFactory.CreateCenteredRect("JadePaperWindow", _root, 2160, 924);
            Art(frame, "main_frame", 0, 0, 2160, 924);
            Art(frame, "title_plate", 62, -21, 480, 114);
            _title = Text(frame, "行囊", 100, -7, 400, 84, 49, Cream, alignment: TextAlignmentOptions.Center);
            _subtitle = Text(frame, "一囊灵物，伴你云游", 72, 99, 330, 46, 28, Muted);
            string[] tabs = { "背包", "任务", "活动" };
            for (int i = 0; i < 3; ++i)
            {
                var page = (GameplayPage)i;
                _tabs[i] = Button(frame, tabs[i], 1130 + i * 266, 40, 240, 80, () => Show(page), key: "tab_normal", fontSize: 36);
            }
            var close = Button(frame, "", 2036, 28, 76, 76, Hide, key: "close");
            close.name = "CloseWindow";
            Art(frame, "tassel", 2079, 114, 57, 168, true);
            _search = QdaoUguiFactory.CreateInputField("BagSearch", frame, 442, 99, 595, 57,
                "搜索物品名称或编号", 80, Load("content_panel"));
            _search.textComponent.color = Ink; _search.textComponent.fontSize = 27;
            ((TMP_Text)_search.placeholder).color = Muted;
            _search.onValueChanged.AddListener(value => { _query = value; _bagPage = 0; if (Page == GameplayPage.Bag) Render(); });
            _body = QdaoUguiFactory.CreateRect("PageContent", frame, 60, 174, 2040, 700);
            // 卡片是 _body 的兄弟节点:Render 每次清空 _body,卡片不跟着销毁;位置对齐详情栏(_body 在 frame 的 60,174)。
            _tooltip = new EquipTooltip(frame, 60 + DetailX + TooltipInsetX, 174 + TooltipInsetY);
            Hide();
        }

        public void Show(GameplayPage page)
        {
            Page = page; _root.gameObject.SetActive(true);
            for (int i = 0; i < 3; ++i)
            {
                ((Image)_tabs[i].targetGraphic).sprite = Load(i == (int)page ? "tab_selected" : "tab_normal");
                _tabs[i].GetComponentInChildren<TMP_Text>().color = i == (int)page ? Cream : Ink;
            }
            _title.text = page == GameplayPage.Bag ? "随身行囊" : page == GameplayPage.Missions ? "仙途手札" : "四时雅集";
            _subtitle.text = page == GameplayPage.Bag ? "收纳灵物 · 随心云游" : page == GameplayPage.Missions ? "记下此行，一步一程" : "灯火照归途 · 月满人团圆";
            _search.gameObject.SetActive(page == GameplayPage.Bag);
            _subtitle.rectTransform.sizeDelta = new Vector2(page == GameplayPage.Bag ? 330 : 900, 46);
            Render();
            Refresh();
        }

        public void Hide() { _tooltip.Hide(); _root.gameObject.SetActive(false); Closed?.Invoke(); }
        public void Refresh()
        {
            if (Page == GameplayPage.Bag) { BagRequested?.Invoke(_bagType); EquipmentRequested?.Invoke(); }
            else if (Page == GameplayPage.Missions) MissionsRequested?.Invoke();
            else { ActivitiesRequested?.Invoke(); MissionsRequested?.Invoke(); }
        }
        public void SetBag(BagInfo bag, bool loading, string error, bool sorting)
        { _bag = bag; _bagLoading = loading; _bagError = error; _sorting = sorting; if (IsVisible && Page == GameplayPage.Bag) Render(); }
        /// <summary>装备栏快照(bag_type = 2)。busy = 穿上 / 卸下在途;error 原样显示在背包页的状态行。</summary>
        public void SetEquipment(BagInfo equipment, bool loading, string error, bool busy)
        { _equipment = equipment; _equipLoading = loading; _equipError = error; _equipBusy = busy; if (IsVisible && Page == GameplayPage.Bag) Render(); }
        /// <summary>
        /// 人物背包与装备栏一起换,只重画一次。数据层每次通知都同时带着两份(见 GameplayUiRoot.Changed):
        /// 先后各调一次 <see cref="SetEquipment"/> / <see cref="SetBag"/> 会把整页重建两遍。
        /// </summary>
        public void SetBagPage(BagInfo bag, bool bagLoading, string bagError, bool sorting,
            BagInfo equipment, bool equipLoading, string equipError, bool equipBusy)
        {
            _equipment = equipment; _equipLoading = equipLoading; _equipError = equipError; _equipBusy = equipBusy;
            SetBag(bag, bagLoading, bagError, sorting);
        }
        /// <summary>角色等级(0 = 未知),只用来给 tooltip 的等级要求标红;能不能穿由服务器裁决。</summary>
        public void SetCharacterLevel(uint level)
        { if (_characterLevel == level) return; _characterLevel = level; if (IsVisible && Page == GameplayPage.Bag) Render(); }
        public void SetMissions(GetMissionListResponse missions, bool loading, string error, bool actionBusy = false)
        {
            _missions = missions;
            if (_trackedMission != 0 && missions != null && !missions.Missions.Any(m => MissionKey(m) == _trackedMission))
            { _trackedMission = 0; TrackingChanged?.Invoke(null); }
            _missionLoading = loading; _missionError = error; _missionActionBusy = actionBusy;
            if (IsVisible && (Page == GameplayPage.Missions || Page == GameplayPage.Activities)) Render();
        }
        public void SetActivities(GetActivityListResponse activities, bool loading, string error)
        { _activities = activities; _activityLoading = loading; _activityError = error; if (IsVisible && Page == GameplayPage.Activities) Render(); }
        public void ResetSession()
        {
            _bag = null; _equipment = null; _missions = null; _activities = null; _trackedMission = 0;
            _selectedItem = 0; _selectedMission = 0; _selectedActivity = 0;
            _bagPage = _missionPage = _activityPage = _objectivePage = _equipPage = 0;
            _bagFilter = _missionFilter = _activityFilter = 0; _query = string.Empty;
            _search.SetTextWithoutNotify(string.Empty); _search.DeactivateInputField();
            _bagLoading = _missionLoading = _activityLoading = _sorting = _missionActionBusy = false;
            _equipLoading = _equipBusy = false; _characterLevel = 0;
            _bagError = _missionError = _activityError = _equipError = string.Empty;
            TrackingChanged?.Invoke(null); Hide();
        }

        private void Render()
        {
            Clear(_body);
            if (Page != GameplayPage.Bag) _tooltip.Hide(); // 卡片不在 _body 里,离开背包页要显式收起
            if (Page == GameplayPage.Bag) RenderBag();
            else if (Page == GameplayPage.Missions) RenderMissions();
            else RenderActivities();
        }
        private void Notice(string value, float x = 420, float y = 244, float width = 1500)
        {
            Art(_body, "flower", x + width / 2 - 58, y - 102, 116, 85, true);
            Text(_body, value, x, y, width, 110, 34, Muted, alignment: TextAlignmentOptions.Center);
        }
        private void StateLine(string error, bool loading, float x, float y, float width)
        {
            string value = !string.IsNullOrEmpty(error) ? "暂时未能同步，请点击刷新重试" : loading ? "正在同步…" : "";
            Text(_body, value, x, y, width, 36, 25, !string.IsNullOrEmpty(error) ? QdaoUguiTheme.Html("#9A442D") : Muted);
        }
        private void Pager(int page, int count, int size, float x, float y, Action<int> change)
        {
            int pages = Math.Max(1, (count + size - 1) / size);
            Button(_body, "上一页", x, y, 164, 56, () => change(page - 1), enabled: page > 0, fontSize: 26);
            Text(_body, $"{page + 1} / {pages}", x + 176, y, 114, 56, 27, Muted, alignment: TextAlignmentOptions.Center);
            Button(_body, "下一页", x + 302, y, 164, 56, () => change(page + 1), enabled: page + 1 < pages, fontSize: 26);
        }

        private void RenderBag()
        {
            Art(_body, "content_panel", 0, 0, 338, 614);
            Art(_body, "section_header", 25, 15, 288, 62);
            Text(_body, "物品分类", 48, 16, 242, 60, 32, Ink, alignment: TextAlignmentOptions.Center);
            string[] filters = { "全部物品", "装备", "其他物品" };
            for (int i = 0; i < filters.Length; ++i)
            {
                int filter = i;
                Button(_body, filters[i], 27, 102 + i * 86, 284, 72,
                    () => { _bagFilter = filter; _bagPage = 0; Render(); },
                    primary: _bagFilter == i, key: _bagFilter == i ? "tab_selected" : "tab_normal");
            }
            Text(_body, "随身资产", 38, 356, 260, 40, 28, Gold);
            // 契约 §0-1:银两 = kCurrencyGold(0),灵石 = kCurrencyDiamond(1),下标即货币类型。
            string[] currencies = { "银两", "灵石", "绑定灵石" };
            for (int i = 0; i < 3; ++i)
            {
                Text(_body, currencies[i], 38, 406 + i * 40, 150, 38, 26, Muted);
                string amount = _bag?.Currency != null && i < _bag.Currency.Values.Count ? _bag.Currency.Values[i].ToString("N0") : "—";
                Text(_body, amount, 166, 406 + i * 40, 130, 38, 27, Ink, alignment: TextAlignmentOptions.MidlineRight);
            }
            Button(_body, "刷新", 0, 638, 152, 62, Refresh, enabled: !_bagLoading && !_sorting && !_equipBusy);
            Button(_body, _sorting ? "整理中" : "整理背包", 171, 638, 168, 62, () => SortRequested?.Invoke(), true,
                !_bagLoading && !_sorting && !_equipBusy && _bag?.Layout != null && _bag.Layout.CanSort && _bag.Layout.BagType == 0);
            Art(_body, "content_panel", DetailX, 0, DetailW, PanelH);
            var worn = EquipBagModel.Slots(_equipment);
            if (_bag?.Layout == null)
            {
                Notice(_bagLoading ? "正在打开行囊…" : !string.IsNullOrEmpty(_bagError) ? "行囊暂未同步，请刷新重试" : "行囊尚未开启", GridX, 250, GridW);
                BagStateLine(GridX + 22, 650, GridW - 30);
                // 两份快照各读各的:行囊还没到时,身上的装备照样可以查看 / 卸下。
                ulong wornId = EquipBagModel.ResolveSelection(_selectedItem, worn, null, out _);
                RenderEquipColumn(worn, wornId);
                if (wornId != 0) ShowTooltip(EquipBagModel.FindWorn(worn, wornId), true);
                else
                {
                    _tooltip.Hide();
                    Text(_body, "物品详情", DetailX + 24, 26, DetailW - 48, 55, 34, Ink, alignment: TextAlignmentOptions.Center);
                }
                return;
            }
            var items = _bag.Items.GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First());
            var slots = _bag.Layout.Slots.OrderBy(s => s.Slot).Where(s => items.ContainsKey(s.ItemId)).ToList();
            bool filtered = _bagFilter != 0 || !string.IsNullOrWhiteSpace(_query);
            var visible = slots.Where(s =>
            {
                var item = items[s.ItemId];
                return (_bagFilter == 0 || (_bagFilter == 1 ? item.EquipKind != 0 : item.EquipKind == 0)) &&
                    (string.IsNullOrWhiteSpace(_query) || ItemName(item).IndexOf(_query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 || item.ConfigId.ToString().Contains(_query.Trim()));
            }).ToList();
            // Fixed-slot bags are flattened for presentation; the server slot remains authoritative.
            int count = filtered ? visible.Count : (int)Math.Min(_bag.Layout.Capacity, int.MaxValue);
            _bagPage = Math.Min(_bagPage, Math.Max(0, (count - 1) / 28));
            var bySlot = slots.GroupBy(s => s.Slot).ToDictionary(g => g.Key, g => g.First());
            // 穿上 / 卸下后同一件装备换了包,选中按 item_id 跟过去;哪边都找不到时退回第一件可见物品,
            // 且不改 _selectedItem(两份快照先后到达,中间那次重画不能把记忆冲掉)。
            ulong selectedId = EquipBagModel.ResolveSelection(_selectedItem, worn, visible.Select(s => s.ItemId), out bool selectedWorn);
            RenderEquipColumn(worn, selectedWorn ? selectedId : 0);
            for (int i = 0; i < 28 && _bagPage * 28 + i < count; ++i)
            {
                int index = _bagPage * 28 + i;
                BagSlotInfo slot = filtered ? visible[index] : bySlot.TryGetValue((uint)index, out var found) ? found : null;
                float x = GridX + i % 7 * GridPitchX, y = i / 7 * 151;
                Art(_body, "paper_tile", x + 7, y + 7, 134, 126);
                var button = Button(_body, "", x, y, 148, 140, () => { _selectedItem = slot.ItemId; Render(); },
                    enabled: slot != null, key: "portrait_frame");
                button.name = "BagSlot_" + (slot?.Slot ?? (uint)index);
                if (slot != null)
                {
                    var item = items[slot.ItemId];
                    ItemIcon(button.transform, item.IconKey, 22, 12, 101);
                    Text(button.transform, item.Count.ToString(), 55, 98, 70, 32, 25, Ink, alignment: TextAlignmentOptions.MidlineRight);
                    if (!selectedWorn && selectedId == slot.ItemId) Art(button.transform, "check", 114, 8, 29, 29, true);
                }
                else Text(button.transform, (index + 1).ToString(), 15, 103, 55, 26, 20, Muted);
            }
            bool stated = BagStateLine(GridX, 606, GridW);
            if (filtered && count == 0) Notice("没有找到符合条件的物品", GridX, 254, GridW);
            else if (slots.Count == 0 && !stated) Text(_body, "行囊空空，沿途拾得的灵物会收在这里", GridX + 20, 612, GridW - 20, 36, 27, Muted);
            Text(_body, $"容量  {slots.Count} / {_bag.Layout.Capacity}", GridX + 6, 650, 480, 45, 28, Muted);
            Pager(_bagPage, count, 28, GridX + GridW - 466, 642, p => { _bagPage = p; Render(); });
            BagItemInfo selected = selectedWorn ? EquipBagModel.FindWorn(worn, selectedId) :
                selectedId != 0 && items.TryGetValue(selectedId, out var held) ? held : null;
            // 装备(以及装备栏里的任何东西)用 tooltip 卡片,盖在详情栏上;其余物品沿用详情栏。
            if (selected != null && (selectedWorn || selected.EquipKind != 0)) { ShowTooltip(selected, selectedWorn); return; }
            _tooltip.Hide();
            float center = DetailX + DetailW / 2;
            if (selected != null)
            {
                Art(_body, "portrait_frame", center - 77, 40, 154, 144);
                ItemIcon(_body, selected.IconKey, center - 53, 54, 105);
                Text(_body, ItemName(selected), DetailX + 20, 202, DetailW - 40, 61, 40, Ink, alignment: TextAlignmentOptions.Center);
                Text(_body, "物品", DetailX + 20, 269, DetailW - 40, 38, 27, Gold, alignment: TextAlignmentOptions.Center);
                Text(_body, string.IsNullOrWhiteSpace(selected.Description) ? "暂无物品说明。" : selected.Description,
                    DetailX + 28, 333, DetailW - 56, 125, 30, Muted, true);
                Text(_body, $"持有数量  {selected.Count}\n叠放上限  {selected.MaxStack}", DetailX + 60, 472, DetailW - 110, 66, 27, Ink, true);
            }
            else
            {
                Art(_body, "icon_bag", center - 89, 111, 177, 177, true);
                Text(_body, "选一件灵物\n查看它的详情", DetailX + 52, 352, DetailW - 104, 120, 33, Muted, true);
            }
        }

        /// <summary>
        /// 背包页的状态行。行囊自身的读取状态优先(沿用通用提示);其次是穿脱 / 装备栏的失败文案,
        /// 原样显示数据层给的原因(与任务页显示领奖失败原因同一做法)。返回是否写了提示。
        /// </summary>
        private bool BagStateLine(float x, float y, float width)
        {
            if (!string.IsNullOrEmpty(_bagError) || _bagLoading) { StateLine(_bagError, _bagLoading, x, y, width); return true; }
            if (!string.IsNullOrEmpty(_equipError)) { Text(_body, _equipError, x, y, width, 36, 25, QdaoUguiTheme.Html("#9A442D")); return true; }
            if (_equipBusy) { Text(_body, "正在更换装备…", x, y, width, 36, 25, Muted); return true; }
            return false;
        }

        /// <summary>
        /// 左侧装备栏:按服务器下发的槽位定义画出全部格子。空槽写部位名,穿着的画图标,点它即选中。
        /// 格高按槽位数均分栏高;超过一页(<see cref="EquipSlotsPerPage"/> 格)才出现翻页。
        /// </summary>
        private void RenderEquipColumn(List<EquipSlotView> worn, ulong selectedWorn)
        {
            float x = EquipColumnX, width = EquipColumnW, cell = width - 20;
            Art(_body, "paper_tile", x + 6, 6, width - 12, PanelH - 12);
            Art(_body, "portrait_frame", x, 0, width, PanelH);
            Text(_body, "装备栏", x, 12, width, 44, 28, Ink, alignment: TextAlignmentOptions.Center);
            if (_equipment?.Layout == null || worn.Count == 0)
            {
                string state = _equipment?.Layout != null ? "暂无栏位" : _equipLoading ? "读取中…" :
                    !string.IsNullOrEmpty(_equipError) ? "未同步" : "未读取";
                Text(_body, state, x, 250, width, 40, 24, Muted, alignment: TextAlignmentOptions.Center);
                return;
            }
            int pages = (worn.Count + EquipSlotsPerPage - 1) / EquipSlotsPerPage;
            _equipPage = Math.Max(0, Math.Min(_equipPage, pages - 1));
            float room = PanelH - EquipSlotsTop - EquipSlotsBottomPad - (pages > 1 ? EquipPagerH : 0);
            // 多页时每页都按满页算格高,翻页时格子不跳。
            float pitch = Math.Min(EquipSlotMaxPitch, room / (pages > 1 ? EquipSlotsPerPage : worn.Count));
            float height = pitch - EquipSlotGap;
            for (int i = _equipPage * EquipSlotsPerPage; i < worn.Count && i < (_equipPage + 1) * EquipSlotsPerPage; ++i)
            {
                var view = worn[i];
                ulong itemId = view.Item?.ItemId ?? 0;
                float y = EquipSlotsTop + (i - _equipPage * EquipSlotsPerPage) * pitch;
                var button = Button(_body, "", x + 10, y, cell, height, () => { _selectedItem = itemId; Render(); },
                    enabled: view.Item != null, key: "portrait_frame");
                button.name = "EquipSlot_" + view.Slot;
                if (view.Item == null)
                {
                    Text(button.transform, view.Name, 4, 0, cell - 8, height, 24, Muted, alignment: TextAlignmentOptions.Center);
                    continue;
                }
                float icon = Math.Min(cell, height) - 14;
                ItemIcon(button.transform, view.Item.IconKey, (cell - icon) / 2, (height - icon) / 2, icon);
                if (selectedWorn == itemId) Art(button.transform, "check", cell - 30, 4, 26, 26, true);
            }
            if (pages <= 1) return;
            // 栏太窄,放不下带 20 边距文字的通用按钮:与槽位格一样用细框 + 自己铺满的文字。
            float pagerY = PanelH - EquipSlotsBottomPad - EquipPagerH + 4, pagerH = EquipPagerH - 6;
            var up = Button(_body, "", x + 10, pagerY, 54, pagerH, () => { --_equipPage; Render(); }, enabled: _equipPage > 0, key: "portrait_frame");
            Text(up.transform, "上", 0, 0, 54, pagerH, 22, Ink, alignment: TextAlignmentOptions.Center);
            var down = Button(_body, "", x + width - 64, pagerY, 54, pagerH, () => { ++_equipPage; Render(); }, enabled: _equipPage + 1 < pages, key: "portrait_frame");
            Text(down.transform, "下", 0, 0, 54, pagerH, 22, Ink, alignment: TextAlignmentOptions.Center);
        }

        private void ShowTooltip(BagItemInfo item, bool worn)
        {
            ulong itemId = item.ItemId;
            // 穿脱与整理在数据层互斥:对方在途时按钮置灰,不让玩家点出一条「请稍候」。
            _tooltip.Show(item, worn, !_equipBusy && !_sorting, _characterLevel,
                () => { if (worn) UnequipRequested?.Invoke(itemId); else EquipRequested?.Invoke(itemId); });
        }

        private void RenderMissions()
        {
            Art(_body, "content_panel", 0, 0, 310, 614);
            Art(_body, "section_header", 22, 16, 266, 62);
            Text(_body, "仙途行记", 35, 17, 240, 58, 31, Ink, alignment: TextAlignmentOptions.Center);
            string[] filters = { "全部任务", "进行中", "已达成", "待开启" };
            for (int i = 0; i < filters.Length; ++i)
            {
                int f = i;
                Button(_body, filters[i], 22, 99 + i * 84, 266, 70, () => { _missionFilter = f; _missionPage = 0; Render(); },
                    key: _missionFilter == i ? "tab_selected" : "tab_normal");
            }
            Art(_body, "icon_scroll", 101, 466, 108, 108, true);
            Button(_body, "刷新任务", 22, 638, 266, 62, Refresh, enabled: !_missionLoading && !_missionActionBusy);
            Art(_body, "content_panel", 1056, 0, 984, 614);
            if (_missions == null)
            {
                Notice(_missionLoading ? "正在展开手札…" : !string.IsNullOrEmpty(_missionError) ? "任务暂未同步，请刷新重试" : "尚无任务记录", 348, 260, 654);
                StateLine(_missionError, _missionLoading, 350, 650, 650); return;
            }
            var list = _missions.Missions.Where(m => _missionFilter == 0 ||
                (_missionFilter == 1 && (int)m.Status == 1) ||
                (_missionFilter == 2 && ((int)m.Status == 2 || (int)m.Status == 3)) ||
                (_missionFilter == 3 && (int)m.Status == 0)).OrderBy(m => m.MissionId).ToList();
            _missionPage = Math.Min(_missionPage, Math.Max(0, (list.Count - 1) / 5));
            if (!list.Any(m => MissionKey(m) == _selectedMission)) _selectedMission = list.Count == 0 ? 0 : MissionKey(list[0]);
            for (int i = 0; i < 5 && _missionPage * 5 + i < list.Count; ++i)
            {
                var mission = list[_missionPage * 5 + i]; bool selected = MissionKey(mission) == _selectedMission;
                var row = Button(_body, "", 346, i * 122, 676, 112, () => { _selectedMission = MissionKey(mission); _objectivePage = 0; Render(); },
                    key: selected ? "pet_card_selected" : "pet_card_normal");
                Text(row.transform, MissionName(mission), 29, 13, 467, 46, 34, selected ? Cream : Ink);
                Text(row.transform, MissionStatus(mission.Status), 30, 62, 566, 34, 25, selected ? Cream : Muted);
                if (_trackedMission == MissionKey(mission)) Art(row.transform, "check", 604, 39, 38, 38, true);
            }
            if (list.Count == 0) Notice("这页手札还没有记录", 346, 256, 676);
            Pager(_missionPage, list.Count, 5, 450, 642, p => { _missionPage = p; _selectedMission = MissionKey(list[p * 5]); _objectivePage = 0; Render(); });
            StateLine(_missionError, _missionLoading, 350, 610, 650);
            var detail = list.FirstOrDefault(m => MissionKey(m) == _selectedMission);
            if (detail == null) { Text(_body, "选择一项任务，查看此行目标", 1110, 267, 858, 70, 34, Muted); return; }
            Text(_body, MissionName(detail), 1167, 58, 816, 60, 44);
            Text(_body, MissionStatus(detail.Status), 1167, 117, 800, 40, 27, Gold);
            Text(_body, string.IsNullOrWhiteSpace(detail.Description) ? "暂无任务说明。" : detail.Description,
                1108, 156, 840, 102, 30, Muted, true);
            Text(_body, "此行目标", 1108, 252, 620, 40, 31);
            int objectivePages = Math.Max(1, (detail.Objectives.Count + 2) / 3);
            _objectivePage = Math.Min(_objectivePage, objectivePages - 1);
            if (objectivePages > 1)
            {
                Text(_body, $"{_objectivePage + 1} / {objectivePages}", 1650, 252, 95, 40, 24, Muted);
                Button(_body, "上组", 1750, 252, 95, 40, () => { --_objectivePage; Render(); }, enabled: _objectivePage > 0, fontSize: 22);
                Button(_body, "下组", 1855, 252, 95, 40, () => { ++_objectivePage; Render(); }, enabled: _objectivePage + 1 < objectivePages, fontSize: 22);
            }
            var objectives = detail.Objectives.Skip(_objectivePage * 3).Take(3).ToList();
            for (int i = 0; i < objectives.Count; ++i)
            {
                var goal = objectives[i]; float y = 296 + i * 56;
                Text(_body, string.IsNullOrWhiteSpace(goal.Description) ? $"目标 {goal.ObjectiveIndex + 1}" : goal.Description,
                    1108, y, 629, 39, 27, Muted);
                Text(_body, $"{goal.Progress} / {goal.Target}", 1744, y, 210, 39, 27, Ink, alignment: TextAlignmentOptions.MidlineRight);
                Art(_body, "slider_bg", 1108, y + 40, 844, 23).color = new Color(.6f, .6f, .6f, .25f);
                float ratio = goal.Target == 0 ? (goal.Completed ? 1 : 0) : Mathf.Clamp01((float)goal.Progress / goal.Target);
                if (ratio > 0) Art(_body, "slider_fill", 1108, y + 40, Math.Max(14, 844 * ratio), 23);
            }
            if (objectives.Count == 0) Text(_body, "暂无目标记录", 1108, 344, 840, 52, 29, Muted);
            string reward = detail.RewardId == 0 ? "本任务未配置奖励" : "完成任务后可领取奖励";
            Text(_body, reward, 1170, 484, 724, 32, 26, Muted);
            string reason = !string.IsNullOrWhiteSpace(_missionError) ? _missionError : _missionActionBusy ? "正在处理任务，请稍候…" :
                !string.IsNullOrWhiteSpace(detail.UnavailableReason) ? detail.UnavailableReason :
                detail.CanClaim ? "目标已达成，可以领取奖励" : detail.CanAccept ? "准备妥当，便可开始此行" : "目标进度以当前手札为准";
            Text(_body, reason, 1170, 523, 724, 32, 25,
                !string.IsNullOrWhiteSpace(_missionError) ? QdaoUguiTheme.Html("#9A442D") : Muted);
            bool claim = detail.CanClaim || (int)detail.Status == 3;
            bool showAction = claim || detail.CanAccept || (int)detail.Status == 0;
            Button(_body, _trackedMission == MissionKey(detail) ? "取消追踪" : "追踪任务", showAction ? 1480 : 1763, 638, 255, 62,
                () => { _trackedMission = _trackedMission == MissionKey(detail) ? 0 : MissionKey(detail); TrackingChanged?.Invoke(_trackedMission == 0 ? null : detail); Render(); },
                !showAction, (int)detail.Status == 1 || _trackedMission == MissionKey(detail));
            if (showAction)
                Button(_body, _missionActionBusy ? "处理中…" : claim ? "领取奖励" : "接取任务", 1763, 638, 255, 62,
                    () => { if (claim) MissionClaimRequested?.Invoke(detail.Scope, detail.MissionId); else MissionAcceptRequested?.Invoke(detail.Scope, detail.MissionId); },
                    true, !_missionActionBusy && !_missionLoading && (claim ? detail.CanClaim : detail.CanAccept));
        }

        private void ShowMission(PlayerMissionInfo mission)
        {
            _missionFilter = 0; _objectivePage = 0; _selectedMission = MissionKey(mission);
            var ordered = _missions.Missions.OrderBy(m => m.MissionId).ToList();
            _missionPage = Math.Max(0, ordered.FindIndex(m => MissionKey(m) == _selectedMission)) / 5;
            Show(GameplayPage.Missions);
        }

        private static string ActivitySchedule(PlayerActivityInfo activity)
        {
            if ((int)activity.Status == 0) return "尚未排期";
            return "开始 " + ActivityTime(activity.StartsAtMs) + "\n结束 " + ActivityTime(activity.EndsAtMs) + " 北京时间（UTC+8）";
        }

        private static string ActivityTime(ulong milliseconds)
        {
            if (milliseconds == 0 || milliseconds > 253402300799999ul) return "待公布";
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds).ToOffset(TimeSpan.FromHours(8))
                    .ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException) { return "待公布"; }
        }
        private void RenderActivities()
        {
            Art(_body, "pet_card_normal", 0, 0, 2040, 128);
            Art(_body, "lantern", 32, 2, 124, 124, true);
            Text(_body, "好时节，与道友相聚", 181, 18, 1020, 49, 40);
            Text(_body, "四时有约，静候佳期", 184, 74, 940, 37, 28, Muted);
            Art(_body, "moon_rabbit", 1867, 8, 142, 114, true);
            string[] filters = { "全部活动", "进行中", "待开放" };
            for (int i = 0; i < 3; ++i)
            {
                int f = i;
                Button(_body, filters[i], i * 252, 148, 232, 62, () => { _activityFilter = f; _activityPage = 0; Render(); },
                    key: i == _activityFilter ? "tab_selected" : "tab_normal", fontSize: 28);
            }
            Button(_body, "刷新", 1862, 148, 176, 62, Refresh, enabled: !_activityLoading && !_missionActionBusy, fontSize: 28);
            if (_activities == null)
            {
                Notice(_activityLoading ? "正在查看雅集…" : !string.IsNullOrEmpty(_activityError) ? "活动暂未同步，请刷新重试" : "雅集尚未开放", 0, 398, 2040);
                StateLine(_activityError, _activityLoading, 0, 653, 1510); return;
            }
            var list = _activities.Activities.Where(a => _activityFilter == 0 || (_activityFilter == 1 ? (int)a.Status == 2 : (int)a.Status < 2)).OrderBy(a => a.ActivityId).ToList();
            _activityPage = Math.Min(_activityPage, Math.Max(0, (list.Count - 1) / 3));
            if (!list.Any(a => a.ActivityId == _selectedActivity)) _selectedActivity = list.FirstOrDefault()?.ActivityId ?? 0;
            for (int i = 0; i < 3 && _activityPage * 3 + i < list.Count; ++i)
            {
                var activity = list[_activityPage * 3 + i]; float x = i * 690;
                Art(_body, "content_panel", x, 237, 656, 360);
                string ornament = i == 0 ? "tassel" : i == 1 ? "lantern" : "moon_rabbit";
                Art(_body, ornament, x + 230, 253, 196, 122, true);
                Text(_body, ActivityName(activity), x + 36, 379, 584, 48, 37, Ink, alignment: TextAlignmentOptions.Center);
                Text(_body, ActivityStatus(activity.Status), x + 36, 432, 584, 30, 28, Gold, alignment: TextAlignmentOptions.Center);
                Text(_body, ActivitySchedule(activity), x + 36, 469, 584, 51, 22, Muted, false, TextAlignmentOptions.Center);
                Button(_body, _selectedActivity == activity.ActivityId ? "正在查看" : "查看详情", x + 172, 529, 312, 60,
                    () => { _selectedActivity = activity.ActivityId; Render(); }, _selectedActivity == activity.ActivityId, fontSize: 28);
            }
            if (list.Count == 0) Notice("此时暂无活动，静候下一场相聚", 0, 390, 2040);
            var detail = list.FirstOrDefault(a => a.ActivityId == _selectedActivity);
            if (detail != null)
            {
                string description = !string.IsNullOrWhiteSpace(detail.Description) ? detail.Description :
                    (int)detail.Status == 0 ? "活动尚未排期，敬请期待。" : "具体安排请留意活动公告。";
                Text(_body, description, 20, 608, 1225, 43, 26, Muted);
                var mission = _missions?.Missions.FirstOrDefault(m => m.Scope == 0 && m.MissionId == detail.MissionId && (int)m.Status != 0);
                string reason = !string.IsNullOrWhiteSpace(_missionError) ? _missionError : _missionActionBusy ? "正在处理任务，请稍候…" :
                    !string.IsNullOrWhiteSpace(detail.UnavailableReason) ? detail.UnavailableReason : mission != null ? "已加入活动，可查看任务进度" :
                    detail.CanParticipate ? "雅集已开启，欢迎道友参与" : "活动暂不可参与";
                Text(_body, reason, 20, 656, 1225, 38, 24, !string.IsNullOrWhiteSpace(_missionError) ? QdaoUguiTheme.Html("#9A442D") : Muted);
                Button(_body, _missionActionBusy ? "处理中…" : mission != null ? "查看任务" : "参与活动", 1300, 638, 250, 62,
                    () => { if (mission != null) ShowMission(mission); else MissionAcceptRequested?.Invoke(0, detail.MissionId); },
                    true, !_missionActionBusy && !_missionLoading && (mission != null || (detail.CanParticipate && detail.MissionId != 0)), fontSize: 28);
            }
            if (!string.IsNullOrEmpty(_activityError) || _activityLoading) StateLine(_activityError, _activityLoading, 20, 574, 1225);
            Pager(_activityPage, list.Count, 3, 1574, 641, p => { _activityPage = p; _selectedActivity = list[p * 3].ActivityId; Render(); });
        }
    }
}
