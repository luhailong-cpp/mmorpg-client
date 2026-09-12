using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public enum GameplayPage { Bag, Missions, Activities }

    /// <summary>Production uGUI views; snapshots also allow isolated editor verification.</summary>
    public sealed class GameplayWindow
    {
        public event Action<uint> BagRequested;
        public event Action SortRequested;
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
        private BagInfo _bag;
        private GetMissionListResponse _missions;
        private GetActivityListResponse _activities;
        private bool _bagLoading, _missionLoading, _activityLoading, _sorting, _missionActionBusy;
        private string _bagError, _missionError, _activityError, _query = "";
        private int _objectivePage;
        private int _bagFilter, _missionFilter, _activityFilter, _bagPage, _missionPage, _activityPage;
        private uint _bagType, _selectedActivity;
        private ulong _selectedMission, _trackedMission;
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

        public void Hide() { _root.gameObject.SetActive(false); Closed?.Invoke(); }
        public void Refresh()
        {
            if (Page == GameplayPage.Bag) BagRequested?.Invoke(_bagType);
            else if (Page == GameplayPage.Missions) MissionsRequested?.Invoke();
            else { ActivitiesRequested?.Invoke(); MissionsRequested?.Invoke(); }
        }
        public void SetBag(BagInfo bag, bool loading, string error, bool sorting)
        { _bag = bag; _bagLoading = loading; _bagError = error; _sorting = sorting; if (IsVisible && Page == GameplayPage.Bag) Render(); }
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
            _bag = null; _missions = null; _activities = null; _trackedMission = 0;
            _selectedItem = 0; _selectedMission = 0; _selectedActivity = 0;
            _bagPage = _missionPage = _activityPage = _objectivePage = 0;
            _bagFilter = _missionFilter = _activityFilter = 0; _query = string.Empty;
            _search.SetTextWithoutNotify(string.Empty); _search.DeactivateInputField();
            _bagLoading = _missionLoading = _activityLoading = _sorting = _missionActionBusy = false;
            _bagError = _missionError = _activityError = string.Empty;
            TrackingChanged?.Invoke(null); Hide();
        }

        private void Render()
        {
            Clear(_body);
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
            string[] currencies = { "金币", "钻石", "绑定钻石" };
            for (int i = 0; i < 3; ++i)
            {
                Text(_body, currencies[i], 38, 406 + i * 40, 150, 38, 26, Muted);
                string amount = _bag?.Currency != null && i < _bag.Currency.Values.Count ? _bag.Currency.Values[i].ToString("N0") : "—";
                Text(_body, amount, 166, 406 + i * 40, 130, 38, 27, Ink, alignment: TextAlignmentOptions.MidlineRight);
            }
            Button(_body, "刷新", 0, 638, 152, 62, Refresh, enabled: !_bagLoading && !_sorting);
            Button(_body, _sorting ? "整理中" : "整理背包", 171, 638, 168, 62, () => SortRequested?.Invoke(), true,
                !_bagLoading && !_sorting && _bag?.Layout != null && _bag.Layout.CanSort && _bag.Layout.BagType == 0);
            Art(_body, "content_panel", 1518, 0, 522, 614);
            if (_bag?.Layout == null)
            {
                Notice(_bagLoading ? "正在打开行囊…" : !string.IsNullOrEmpty(_bagError) ? "行囊暂未同步，请刷新重试" : "行囊尚未开启", 368, 250, 1110);
                StateLine(_bagError, _bagLoading, 396, 650, 1080);
                Text(_body, "物品详情", 1560, 26, 438, 55, 34, Ink, alignment: TextAlignmentOptions.Center);
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
            if (_selectedItem == 0 || !visible.Any(s => s.ItemId == _selectedItem)) _selectedItem = visible.FirstOrDefault()?.ItemId ?? 0;
            for (int i = 0; i < 28 && _bagPage * 28 + i < count; ++i)
            {
                int index = _bagPage * 28 + i;
                BagSlotInfo slot = filtered ? visible[index] : bySlot.TryGetValue((uint)index, out var found) ? found : null;
                float x = 374 + i % 7 * 158, y = i / 7 * 151;
                Art(_body, "paper_tile", x + 7, y + 7, 134, 126);
                var button = Button(_body, "", x, y, 148, 140, () => { _selectedItem = slot.ItemId; Render(); },
                    enabled: slot != null, key: "portrait_frame");
                button.name = "BagSlot_" + (slot?.Slot ?? (uint)index);
                if (slot != null)
                {
                    var item = items[slot.ItemId];
                    ItemIcon(button.transform, item.IconKey, 22, 12, 101);
                    Text(button.transform, item.Count.ToString(), 55, 98, 70, 32, 25, Ink, alignment: TextAlignmentOptions.MidlineRight);
                    if (_selectedItem == slot.ItemId) Art(button.transform, "check", 114, 8, 29, 29, true);
                }
                else Text(button.transform, (index + 1).ToString(), 15, 103, 55, 26, 20, Muted);
            }
            if (filtered && count == 0) Notice("没有找到符合条件的物品", 374, 254, 1090);
            else if (slots.Count == 0 && !_bagLoading && string.IsNullOrEmpty(_bagError)) Text(_body, "行囊空空，沿途拾得的灵物会收在这里", 394, 612, 1080, 36, 27, Muted);
            StateLine(_bagError, _bagLoading, 374, 606, 1110);
            Text(_body, $"容量  {slots.Count} / {_bag.Layout.Capacity}", 380, 650, 480, 45, 28, Muted);
            Pager(_bagPage, count, 28, 987, 642, p => { _bagPage = p; Render(); });
            if (_selectedItem != 0 && items.TryGetValue(_selectedItem, out var selected))
            {
                Art(_body, "portrait_frame", 1702, 40, 154, 144);
                ItemIcon(_body, selected.IconKey, 1726, 54, 105);
                Text(_body, ItemName(selected), 1554, 202, 450, 61, 40, Ink, alignment: TextAlignmentOptions.Center);
                Text(_body, selected.EquipKind != 0 ? "装备" : "物品", 1554, 269, 450, 38, 27, Gold, alignment: TextAlignmentOptions.Center);
                Text(_body, string.IsNullOrWhiteSpace(selected.Description) ? "暂无物品说明。" : selected.Description,
                    1563, 333, 428, 125, 30, Muted, true);
                Text(_body, $"持有数量  {selected.Count}\n叠放上限  {selected.MaxStack}", 1605, 472, 365, 66, 27, Ink, true);
            }
            else
            {
                Art(_body, "icon_bag", 1690, 111, 177, 177, true);
                Text(_body, "选一件灵物\n查看它的详情", 1570, 352, 412, 120, 33, Muted, true);
            }
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
