using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>Native coordinates from the approved designs, with live protocol data and real scrolling controls.</summary>
    public sealed partial class GameplayWindow
    {
        private const float NativeWidth = 1932, NativeHeight = 814;
        private const string NativeRoot = "UI/Ugui/GameplayFidelity20261016/";
        private readonly Image _nativeFrame, _nativeTitle;
        private float _bagScroll = 1, _missionScroll = 1, _activityScroll = 1;
        private bool _missionFilterMenu, _bagTools, _activityTools;
        private uint _activityDetails;
        private const int ExactBagPageSize = 20;
        // The approved fourth row is compact (80px); occupied icons fit inside it.
        private static readonly float[] ExactBagY = { 174, 320, 464, 608 };
        private static readonly float[] ExactBagH = { 138, 136, 135, 80 };

        private static Sprite NativeSprite(string key) => QdaoUguiTheme.RequireSprite(NativeRoot + key);
        private static Image NativeArt(UnityEngine.Transform parent, string key, float x, float y, float w, float h)
            => QdaoUguiFactory.CreateImage(key, parent, x, y, w, h, NativeSprite(key));

        private static TextMeshProUGUI NativeText(UnityEngine.Transform parent, string value,
            float x, float y, float w, float h, float size = 28, Color? color = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            var t = Text(parent, value, x, y, w, h, size, color, wrap, alignment);
            t.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            ApplyOriginalLettering(parent,t,value,x,y,w,h,size,color ?? Ink,alignment);
            return t;
        }

        private static Button NativeHit(UnityEngine.Transform parent, string name, string label,
            float x, float y, float w, float h, Action click, bool enabled = true, float font = 29, Color? color = null)
        {
            var b = QdaoUguiFactory.CreateHitButton(name, parent, x, y, w, h);
            b.navigation = new Navigation { mode = Navigation.Mode.Automatic }; b.interactable = enabled;
            if (click != null) b.onClick.AddListener(() => click());
            if (!string.IsNullOrEmpty(label)) NativeText(b.transform, label, 4, 0, w - 8, h, font, color, TextAlignmentOptions.Center);
            return b;
        }

        private static Button NativeButton(UnityEngine.Transform parent, string name, string label, string skin,
            float x, float y, float w, float h, Action click, bool enabled = true, float font = 29, bool jade = false)
        {
            var b = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, NativeSprite(skin), out var image);
            if (skin == "bag-slot-selected") InventorySelectedLightControl.Apply(image);
            b.navigation = new Navigation { mode = Navigation.Mode.Automatic }; b.interactable = enabled;
            var c = b.colors; c.selectedColor = Color.white; c.highlightedColor = new Color(1, .97f, .90f); c.disabledColor = Color.white; b.colors = c;
            if (click != null) b.onClick.AddListener(() => click());
            if (!string.IsNullOrEmpty(label))
            {
                var text = NativeText(b.transform, label, 10, 0, w - 20, h, font, jade ? Cream : Ink, TextAlignmentOptions.Center);
                if (!enabled) text.color = jade ? new Color(Cream.r, Cream.g, Cream.b, .65f) : Muted;
            }
            return b;
        }

        private static RectTransform NativeScroll(UnityEngine.Transform parent, string name, float x, float y,
            float width, float height, float contentHeight, float position, Action<float> changed,
            bool horizontal = false, float contentWidth = 0)
        {
            var view = QdaoUguiFactory.CreateRect(name, parent, x, y, width, height);
            var hit = view.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, .001f); hit.raycastTarget = true;
            view.gameObject.AddComponent<RectMask2D>();
            var content = QdaoUguiFactory.CreateRect(name + "Content", view, 0, 0,
                horizontal ? Math.Max(width, contentWidth) : width, Math.Max(height, contentHeight));
            var scroll = view.gameObject.AddComponent<ScrollRect>(); scroll.viewport = view; scroll.content = content;
            scroll.horizontal = horizontal; scroll.vertical = !horizontal; scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = horizontal ? 120 : 50;
            if (horizontal) scroll.horizontalNormalizedPosition = 1 - position; else scroll.verticalNormalizedPosition = position;
            scroll.onValueChanged.AddListener(v => changed(horizontal ? 1 - v.x : v.y));
            return content;
        }

        private static void NativeParagraph(UnityEngine.Transform parent, string value, float x, float y,
            float width, float height, float font = 26, Color? color = null)
        {
            var measure = NativeText(parent, value, 0, 0, width - 8, height, font, color, wrap: true);
            float actual = Math.Max(height, measure.GetPreferredValues(value, width - 8, float.PositiveInfinity).y + 8);
            float oneLine = measure.GetPreferredValues("道", width - 8, float.PositiveInfinity).y;
            float advance = Math.Max(1, measure.GetPreferredValues("道\n道", width - 8, float.PositiveInfinity).y - oneLine);
            int visibleLines = Math.Max(1, Mathf.FloorToInt((height - oneLine) / advance) + 1);
            float viewportHeight = Math.Min(height, oneLine + (visibleLines - 1) * advance);
            measure.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(measure.gameObject); else UnityEngine.Object.DestroyImmediate(measure.gameObject);
            var content = NativeScroll(parent, "DescriptionScroll", x, y, width, viewportHeight, actual, 1, _ => { });
            NativeText(content, value, 0, 0, width - 8, actual, font, color, wrap: true);
        }

        private void NativeStatus(string value, float x, float y, float width, bool error = false)
            => NativeText(_body, value, x, y, width, 35, 23, error ? QdaoUguiTheme.Html("#9A442D") : Muted);

        private void RenderBag()
        {
            // Keep approved primary categories. Protocol has no potion/material taxonomy.
            string[] filters = { "全部", "装备", "丹药", "材料" };
            for (int i = 0; i < filters.Length; ++i)
            {
                int f = i; bool selected = i < 2 && _bagFilter == i;
                NativeButton(_body, "BagCategory_" + i, filters[i], selected ? "bag-tab-selected" : "bag-tab-normal",
                    new float[]{190,444,684,924}[i], 93, new float[]{254,240,240,237}[i], 67,
                    () => { _bagFilter = f; _bagPage = 0; _bagScroll = 1; Render(); }, i < 2, 34, selected);
            }
            for (int i = 0; i < 2; ++i)
            {
                string amount = _bag?.Currency != null && i < _bag.Currency.Values.Count ? _bag.Currency.Values[i].ToString("N0") : "—";
                NativeText(_body, amount, i == 0 ? 261 : 562, 708, i == 0 ? 132 : 85, 43, 28);
            }
            NativeButton(_body, "SortBag", _sorting ? "整理中" : "整理", "bag-sort", 1005, 700, 158, 54,
                () => SortRequested?.Invoke(), !_bagLoading && !_sorting && !_equipBusy && _bag?.Layout != null && _bag.Layout.CanSort && _bag.Layout.BagType == 0, 30);
            var worn = EquipBagModel.Slots(_equipment);
            var items = _bag?.Items.GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<ulong, BagItemInfo>();
            var slots = _bag?.Layout?.Slots.OrderBy(s => s.Slot).Where(s => items.ContainsKey(s.ItemId)).ToList() ?? new List<BagSlotInfo>();
            bool filtered = _bagFilter != 0 || !string.IsNullOrWhiteSpace(_query);
            Func<BagItemInfo, bool> matches = item => string.IsNullOrWhiteSpace(_query) || ItemName(item).IndexOf(_query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 || item.ConfigId.ToString().Contains(_query.Trim());
            var visible = slots.Where(s => matches(items[s.ItemId]) && (_bagFilter == 0 || (_bagFilter == 1 ? items[s.ItemId].EquipKind != 0 : _bagFilter == 2 && items[s.ItemId].EquipKind == 0))).ToList();
            var wornVisible = worn.Where(v => v.Item == null ? string.IsNullOrWhiteSpace(_query) : matches(v.Item)).ToList();
            int count = _bagFilter == 3 ? wornVisible.Count : filtered ? visible.Count : (int)Math.Min(_bag?.Layout?.Capacity ?? 0, int.MaxValue);
            _bagPage = Mathf.Clamp(_bagPage, 0, Math.Max(0, (count - 1) / ExactBagPageSize));
            ulong selectedId = EquipBagModel.ResolveSelection(_selectedItem, worn, visible.Select(s => s.ItemId), out bool selectedWorn);
            if (_bagFilter == 3 && !wornVisible.Any(v => v.Item != null && v.Item.ItemId == selectedId))
            { selectedId = wornVisible.Where(v => v.Item != null).Select(v => v.Item.ItemId).FirstOrDefault(); selectedWorn = true; }
            var bySlot = slots.GroupBy(s => s.Slot).ToDictionary(g => g.Key, g => g.First());
            // Page controls keep every server slot reachable without cutting a populated row.
            var content = NativeScroll(_body, "BagScroll", 178, 174, 981, 514, 514, 1, _ => { });
            content.GetComponentInParent<ScrollRect>().vertical = false;
            int first = _bagPage * ExactBagPageSize;
            for (int cell = 0; cell < Math.Min(ExactBagPageSize, count - first); ++cell)
            {
                int index = first + cell, row = cell / 5;
                BagSlotInfo slot = _bagFilter == 3 ? null : filtered ? visible[index] : bySlot.TryGetValue((uint)index, out var found) ? found : null;
                BagItemInfo item = _bagFilter == 3 ? wornVisible[index].Item : slot != null ? items[slot.ItemId] : null;
                ulong id = item?.ItemId ?? 0; bool selected = id != 0 && selectedId == id;
                float h = ExactBagH[row];
                var button = NativeButton(content, _bagFilter == 3 ? "EquipSlot_" + wornVisible[index].Slot : "BagSlot_" + (slot?.Slot ?? (uint)index), "",
                    selected ? "bag-slot-selected" : "bag-slot", cell % 5 * 196, ExactBagY[row] - 174, 190, h,
                    () => { if (id != 0) { _selectedItem = id; Render(); } });
                if (item == null)
                {
                    button.interactable = false; var colors = button.colors; colors.disabledColor = Color.white; button.colors = colors;
                    if (_bagFilter == 3) NativeText(button.transform, wornVisible[index].Name, 12, 8, 166, h - 16, 26, Muted, TextAlignmentOptions.Center);
                    continue;
                }
                float icon = row == 3 ? 68 : 126;
                ItemIcon(button.transform, item.IconKey, (190 - icon) / 2, 4, icon);
                NativeText(button.transform, item.Count > 1 ? item.Count.ToString() : "", 108, h - 37, 66, 34, 25, Ink, TextAlignmentOptions.MidlineRight);
            }
            NativeText(_body, _bagFilter == 3 ? "已穿戴 " + wornVisible.Count(v => v.Item != null) : $"背包 {slots.Count}/{_bag?.Layout?.Capacity.ToString() ?? "—"}", 832, 706, 166, 44, 26, Ink);
            string state = !string.IsNullOrEmpty(_bagError) ? "暂时未能同步，请点击刷新重试" : _bagLoading ? "正在打开行囊…" :
                !string.IsNullOrEmpty(_equipError) ? _equipError : _equipBusy ? "正在处理装备…" : _equipmentNotice;
            if (string.IsNullOrEmpty(state))
            {
                if (_bagFilter == 3 && _equipment?.Layout == null) NativeStatus(_equipLoading ? "读取中…" : "装备栏尚未同步", 434, 400, 640);
                else if (_bag?.Layout == null && _bagFilter != 3) NativeStatus("行囊尚未开启", 434, 400, 640);
                else if (filtered && count == 0) NativeStatus("没有找到符合条件的物品", 400, 400, 640);
                else if (slots.Count == 0 && _bagFilter != 3) NativeStatus("行囊空空，沿途拾得的灵物会收在这里", 310, 400, 780);
            }
            BagItemInfo chosen = selectedWorn ? EquipBagModel.FindWorn(worn, selectedId) : items.TryGetValue(selectedId, out var held) ? held : null;
            if (!_bagTools) RenderExactBagTools(count);
            RenderNativeItem(chosen, selectedWorn);
            if (!string.IsNullOrEmpty(state)) NativeStatus(state, 1260, 622, 474, !string.IsNullOrEmpty(_bagError) || !string.IsNullOrEmpty(_equipError));
            if (_bagTools) RenderExactBagTools(count);
        }

        private void RenderExactBagTools(int count)
        {
            NativeHit(_body, "BagTools", "工具", 1694, 208, 66, 34, () => { _bagTools = !_bagTools; Render(); }, font: 22);
            int pages = Math.Max(1, (count + ExactBagPageSize - 1) / ExactBagPageSize);
            if (pages > 1)
            {
                NativeHit(_body, "BagPreviousPage", "‹", 742, 711, 31, 35, () => { --_bagPage; Render(); }, _bagPage > 0, 28);
                NativeText(_body, $"{_bagPage + 1}/{pages}", 773, 711, 55, 35, 20, Muted, TextAlignmentOptions.Center);
                NativeHit(_body, "BagNextPage", "›", 1159, 711, 31, 35, () => { ++_bagPage; Render(); }, _bagPage + 1 < pages, 28);
            }
            _search.gameObject.SetActive(_bagTools);
            if (!_bagTools) return;
            Art(_body, "content_panel", 1238, 208, 451, 363);
            NativeText(_body, "行囊工具", 1260, 218, 405, 36, 28);
            NativeHit(_body, "RefreshBag", "刷新", 1260, 302, 187, 47, Refresh, !_bagLoading && !_sorting && !_equipBusy, 26);
            NativeHit(_body, "WornEquipment", "已穿戴", 1460, 302, 187, 47, () => { _bagFilter = 3; _bagPage = 0; _bagTools = false; Render(); }, font: 26);
            NativeHit(_body, "OtherItems", "其他物品", 1260, 355, 187, 47, () => { _bagFilter = 2; _bagPage = 0; _bagTools = false; Render(); }, font: 26);
            NativeText(_body, "丹药、材料分类及出售尚未开放", 1260, 416, 405, 38, 21, Muted);
            NativeHit(_body, "CloseBagTools", "收起", 1460, 480, 187, 47, () => { _bagTools = false; Render(); }, font: 26);
        }

        private void RenderNativeItem(BagItemInfo item, bool worn)
        {
            if (item == null)
            {
                NativeText(_body, "选一件灵物，查看详情", 1260, 495, 480, 65, 28, Muted, TextAlignmentOptions.Center);
                NativeButton(_body, "SellItem", "出售", "bag-button-normal", 1235, 678, 210, 68, null, false, 32);
                NativeButton(_body, "EquipActionEmpty", "装备", "bag-button-primary", 1459, 673, 308, 81, null, false, 37, true);
                return;
            }
            bool equipment = worn || item.EquipKind != 0;
            var panel = QdaoUguiFactory.CreateRect(equipment ? "GameplayEquipmentDetails" : "GameplayItemDetails", _body, 0, 0, NativeWidth, NativeHeight);
            var itemName = NativeText(panel, EquipDisplay.NameWithEnhance(ItemName(item), item.EnhanceLevel), 1256, 90, 508, 64, 43, Ink, TextAlignmentOptions.Center);
            itemName.name = "TooltipName"; itemName.enableAutoSizing = true; itemName.fontSizeMin = 28; itemName.fontSizeMax = 43;
            NativeArt(panel, "bag-item-badge", 1423, 160, 159, 40);
            NativeText(panel, equipment ? worn ? "已穿戴" : "未穿戴" : "物品", 1423, 160, 159, 40, 27, Cream, TextAlignmentOptions.Center).name = "TooltipState";
            ItemIcon(panel, item.IconKey, 1380, 216, 250);
            var lines = new List<EquipDetailLine>();
            if (equipment)
            {
                foreach (var line in EquipDisplay.LinesOf(item)) lines.Add(new EquipDetailLine(EquipDisplay.FormatLine(line), EquipUiStyle.TierColor(EquipDisplay.TierOf(line.Tier))));
                foreach (var socket in EquipDisplay.SocketsOf(item)) lines.Add(new EquipDetailLine(EquipDisplay.FormatSocketLine(socket), socket.GemConfigId != 0 && socket.Attr != null ? EquipUiSkin.Green : Muted));
            }
            else { lines.Add(new EquipDetailLine("持有数量  " + item.Count, Ink)); lines.Add(new EquipDetailLine("叠放上限  " + item.MaxStack, Ink)); }
            bool hasNotice = !string.IsNullOrEmpty(_bagError) || _bagLoading || !string.IsNullOrEmpty(_equipError) || _equipBusy || !string.IsNullOrEmpty(_equipmentNotice);
            float detailsHeight = hasNotice ? 132 : 178;
            var details = NativeScroll(panel, "ItemDetailsScroll", 1276, 482, 467, detailsHeight, detailsHeight, 1, _ => { });
            float y = 0;
            if (equipment)
            {
                NativeText(details, EquipBagModel.LevelRequirement(item), 12, y, 439, 39, 28,
                    EquipBagModel.LevelUnmet(item, _characterLevel) ? EquipUiStyle.Unmet : EquipUiStyle.TooltipText).name = "TooltipLevel";
                y += 39;
                NativeText(details, EquipBagModel.ClassRequirement(item), 12, y, 439, 39, 28, EquipUiStyle.TooltipText).name = "TooltipClass";
                y += 39;
            }
            for (int i = 0; i < lines.Count; ++i)
            {
                var line = NativeText(details, lines[i].Text, 12, y, 439, 39, 28, lines[i].Color, wrap: true);
                line.name = "TooltipLine_" + i;
                float height = Math.Max(39, line.GetPreferredValues(line.text, 439, float.PositiveInfinity).y + 5);
                line.rectTransform.sizeDelta = new Vector2(439, height); y += height;
            }
            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                y += 10;
                var description = NativeText(details, item.Description, 12, y, 439, EquipUiStyle.TooltipDescMinH, 24, EquipUiStyle.TooltipText, wrap: true);
                description.name = "TooltipDescription";
                float height = Math.Max(EquipUiStyle.TooltipDescMinH, description.GetPreferredValues(description.text, 439, float.PositiveInfinity).y + 5);
                description.rectTransform.sizeDelta = new Vector2(439, height); y += height;
            }
            details.sizeDelta = new Vector2(467, Math.Max(detailsHeight, y + 12));
            if (equipment)
            {
                ulong id = item.ItemId;
                NativeButton(panel, "SellItem", "出售", "bag-button-normal", 1235, 678, 210, 68, null, false, 32);
                NativeHit(panel, "EquipMore", "更多", 1694, 248, 66, 34, () => OpenEquipmentActions(id), font: 22);
                NativeButton(panel, "EquipAction", worn ? "卸下" : "装备", "bag-button-primary", 1459, 673, 308, 81,
                    () => { _equipmentNotice = null; if (worn) UnequipRequested?.Invoke(id); else EquipRequested?.Invoke(id); }, !_equipBusy && !_sorting, 37, true);
                NativeHit(panel, "EquipShare", "分享", 1666, 166, 80, 35, () => { var current = FindEquipment(id); if (current != null) EquipmentShareRequested?.Invoke(current.Clone()); }, font: 24);
            }
            else
            {
                NativeButton(panel, "SellItem", "出售", "bag-button-normal", 1235, 678, 210, 68, null, false, 32);
                NativeButton(panel, "ItemActionUnavailable", "使用", "bag-button-primary", 1459, 673, 308, 81, null, false, 32, true);
            }
        }

        private void RenderMissions()
        {
            bool available = _missionFilter == 3;
            NativeButton(_body, "AcceptedMissions", "已接任务", available ? "mission-tab" : "mission-tab-selected", 191, 93, 267, 68,
                () => { _missionFilter = 0; _missionScroll = 1; Render(); }, font: 34, jade: !available);
            NativeButton(_body, "AvailableMissions", "可接任务", available ? "mission-tab-selected" : "mission-tab", 457, 93, 264, 68,
                () => { _missionFilter = 3; _missionScroll = 1; Render(); }, font: 34, jade: available);
            NativeHit(_body, "RefreshMissions", "刷新任务", 1608, 113, 132, 36, Refresh, !_missionLoading && !_missionActionBusy, 22);
            var list = _missions?.Missions.Where(m => (_missionFilter == 0 && (int)m.Status != 0) || (_missionFilter == 1 && (int)m.Status == 1) ||
                (_missionFilter == 2 && ((int)m.Status == 2 || (int)m.Status == 3)) || (_missionFilter == 3 && (int)m.Status == 0)).OrderBy(m => m.MissionId).ToList() ?? new List<PlayerMissionInfo>();
            if (!list.Any(m => MissionKey(m) == _selectedMission)) _selectedMission = list.Count == 0 ? 0 : MissionKey(list[0]);
            var rows = NativeScroll(_body, "MissionListScroll", 166, 171, 603, 494, list.Count * 101, _missionScroll, p => _missionScroll = p);
            for (int i = 0; i < list.Count; ++i)
            {
                var m = list[i]; bool selected = MissionKey(m) == _selectedMission;
                var row = NativeButton(rows, "Mission_" + m.Scope + "_" + m.MissionId, "", selected ? "mission-row-selected" : "mission-row", 0, i * 101, 603, 94,
                    () => { _selectedMission = MissionKey(m); _missionObjectiveScroll = 1; Render(); });
                NativeText(row.transform, "任务", 23, 23, 103, 49, 29, Ink, TextAlignmentOptions.Center);
                NativeText(row.transform, MissionName(m), 149, 14, 283, 61, 33, selected ? Cream : Ink);
                NativeText(row.transform, _missionTracking.IsTracked(MissionKey(m)) ? "追踪中" : MissionStatus(m.Status), 422, 26, selected ? 91 : 157, 41, 26, selected ? Cream : Ink, TextAlignmentOptions.MidlineRight);
                if (selected) NativeArt(row.transform, "mission-selected-circle", 521, 18, 64, 62).name = "MissionSelectionMark";
            }
            NativeText(_body, "任务数量 " + list.Count, 207, 688, 233, 51, 28);
            string[] statuses = { "全部任务", "进行中", "已达成", "待开启" };
            NativeButton(_body, "MissionFilterMenu", "筛选·" + statuses[_missionFilter], "mission-tab", 1467, 113, 133, 36,
                () => { _missionFilterMenu = !_missionFilterMenu; Render(); }, font: 20);
            if (_missionFilterMenu)
                for (int i = 0; i < statuses.Length; ++i)
                {
                    int filter = i;
                    NativeButton(_body, "MissionStatusFilter_" + i, statuses[i], _missionFilter == i ? "mission-tab-selected" : "mission-tab",
                        1467, 155 + i * 53, 270, 53, () => { _missionFilter = filter; _missionFilterMenu = false; _missionScroll = 1; Render(); }, font: 26, jade: _missionFilter == i);
                }
            if (list.Count == 0) NativeStatus(_missionLoading ? "正在展开手札…" : !string.IsNullOrEmpty(_missionError) ? "任务暂未同步，请刷新重试" : "这页手札还没有记录", 207, 377, 540);
            var detail = list.FirstOrDefault(m => MissionKey(m) == _selectedMission);
            if (detail == null) return;
            var missionName = NativeText(_body, MissionName(detail), 841, 111, 740, 76, 46);
            missionName.enableAutoSizing = true; missionName.fontSizeMin = 30; missionName.fontSizeMax = 46;
            float badgeX = Math.Max(1106, Math.Min(1618, 841 + missionName.GetPreferredValues(MissionName(detail)).x + 24));
            NativeArt(_body, "mission-badge", badgeX, 127, 126, 52);
            var badgeText = NativeText(_body, MissionStatus(detail.Status), badgeX + 15, 131, 96, 45, 20, Ink, TextAlignmentOptions.Center);
            badgeText.enableAutoSizing = true; badgeText.fontSizeMin = 16; badgeText.fontSizeMax = 20;
            NativeParagraph(_body, string.IsNullOrWhiteSpace(detail.Description) ? "暂无任务说明。" : detail.Description, 852, 202, 872, 85, 28, Ink);
            NativeText(_body, "任务目标", 861, 286, 301, 45, 33);
            var objectives = NativeScroll(_body, "MissionObjectivesScroll", 854, 337, 872, 108, 108, _missionObjectiveScroll, p => _missionObjectiveScroll = p);
            float goalY = 0;
            for (int i = 0; i < detail.Objectives.Count; ++i)
            {
                var goal = detail.Objectives[i];
                var stateArt = EquipUiSkin.Art(objectives, goal.Completed ? "checkbox_on" : "checkbox_off", 12, goalY + 7, 32, 32, Gold);
                stateArt.name = "MissionObjectiveState_" + goal.ObjectiveIndex;
                var goalText = NativeText(objectives, string.IsNullOrWhiteSpace(goal.Description) ? $"目标 {(ulong)goal.ObjectiveIndex + 1}" : goal.Description, 70, goalY, 610, 54, 29, wrap: true);
                float goalHeight = Math.Max(54, goalText.GetPreferredValues(goalText.text, 610, float.PositiveInfinity).y + 6);
                goalText.rectTransform.sizeDelta = new Vector2(610, goalHeight);
                NativeText(objectives, goal.Target > 0 ? $"{goal.Progress}/{goal.Target}" : goal.Completed ? "已达成" : "数量待确认", 691, goalY, 166, 50, 28, Ink, TextAlignmentOptions.MidlineRight);
                goalY += goalHeight;
            }
            objectives.sizeDelta = new Vector2(872, Math.Max(108, goalY));
            objectives.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = _missionObjectiveScroll;
            if (detail.Objectives.Count == 0) NativeText(objectives, "暂无目标记录", 20, 0, 820, 55, 27, Muted);
            NativeText(_body, "任务奖励", 862, 455, 300, 44, 33);
            NativeText(_body, detail.RewardId == 0 ? "本任务未配置奖励" : "完成后由服务器发放奖励", 871, 516, 603, 47, 28, Muted);
            string reason = !string.IsNullOrWhiteSpace(_missionError) ? _missionError : _missionActionBusy ? "正在处理任务，请稍候…" :
                !string.IsNullOrWhiteSpace(detail.UnavailableReason) ? detail.UnavailableReason : detail.CanClaim ? "目标已达成，可以领取奖励" : detail.CanAccept ? "准备妥当，便可开始此行" : "目标进度以当前手札为准";
            NativeParagraph(_body, reason, 873, 573, 603, 79, 25, !string.IsNullOrWhiteSpace(_missionError) ? QdaoUguiTheme.Html("#9A442D") : Muted);
            bool tracked = _missionTracking.IsTracked(MissionKey(detail));
            var tracking = NativeHit(_body, "TrackMission", "", 858, 687, 210, 50,
                () => { _missionTracking.Toggle(detail); TrackingChanged?.Invoke(tracked ? null : detail); Render(); }, (int)detail.Status == 1 || (int)detail.Status == 3 || tracked);
            EquipUiSkin.Art(tracking.transform, tracked ? "checkbox_on" : "checkbox_off", 0, 0, 44, 44, Gold).name = "AutoTrackState";
            NativeText(tracking.transform, tracked ? "取消追踪" : "追踪任务", 60, 0, 150, 44, 31);
            NativeButton(_body, "MissionMap", "查看地图", "mission-button-normal", 1190, 678, 264, 71, null, false, 32);
            bool claim = detail.CanClaim || (int)detail.Status == 3;
            bool action = claim || detail.CanAccept || (int)detail.Status == 0;
            NativeButton(_body, "MissionAction", _missionActionBusy ? "处理中…" : claim ? "领取奖励" : action ? "接取任务" : "前往任务", "mission-button-primary", 1462, 672, 316, 82,
                () => { if (claim) MissionClaimRequested?.Invoke(detail.Scope, detail.MissionId); else MissionAcceptRequested?.Invoke(detail.Scope, detail.MissionId); },
                action && !_missionActionBusy && !_missionLoading && (claim ? detail.CanClaim : detail.CanAccept), 36, true);
        }

    }
}
