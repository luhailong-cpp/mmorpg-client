using System;
using System.Collections.Generic;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>地图窗口使用的展示数据；传送目标仍由场景配置编号决定。</summary>
    public sealed class CityTravelDestination
    {
        public uint SceneConfigId;
        public string Name;
        public string Description;
        public string DayResourcePath;
        public string FestivalResourcePath;
        public string FestivalName;
        public bool HasFestival => !string.IsNullOrEmpty(FestivalResourcePath);
    }

    /// <summary>可前往的其他区服；只是展示数据，能不能去由服务端在传送请求里裁决。</summary>
    public sealed class CityTravelZone
    {
        public uint ZoneId;
        public string Name;
    }

    /// <summary>沿用游戏现有纸面窗口，展示整张地图并发出传送或换景意图。</summary>
    public sealed class CityTravelWindow
    {
        public event Action<uint, bool> TravelRequested;
        /// <summary>
        /// 选了其他区服时，“前往”发出的是这个事件（区服编号、场景配置编号、节庆景色），不再发
        /// <see cref="TravelRequested"/>：跨区要换连接并重新登录，走的是另一条请求，两者不能混用。
        /// </summary>
        public event Action<uint, uint, bool> ZoneTravelRequested;
        public bool IsVisible => _root.gameObject.activeSelf;
        public uint SelectedSceneConfigId => _selected?.SceneConfigId ?? 0;
        public bool SelectedFestival => _festival;
        public bool TravelEnabled => _travel.interactable;
        /// <summary>零表示留在本区（默认）。</summary>
        public uint SelectedZoneId => SelectedZone?.ZoneId ?? 0;

        private readonly RectTransform _root, _destinationList;
        private readonly RawImage _preview;
        private readonly TMP_Text _name, _description, _current, _status, _appearanceHint, _travelLabel;
        private readonly Button _day, _festivalButton, _travel, _zoneButton;
        private readonly TMP_Text _festivalLabel, _zoneLabel;
        private IReadOnlyList<CityTravelZone> _zones;
        private int _zoneIndex = -1; // 负一表示本区；其余是 _zones 的下标
        private readonly List<Button> _destinationButtons = new();
        private IReadOnlyList<CityTravelDestination> _destinations;
        private CityTravelDestination _selected;
        private uint _currentScene;
        private bool _currentFestival, _festival, _busy;

        public CityTravelWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("CityTravelWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("CityTravelFrame", _root, 2160, 924);
            Art(frame, "main_frame", 0, 0, 2160, 924);
            Art(frame, "title_plate", 62, -21, 480, 114);
            Text(frame, "山海行图", 100, -7, 400, 84, 49, Cream, alignment: TextAlignmentOptions.Center);
            _current = Text(frame, "", 604, 40, 1270, 58, 30, Muted);
            var close = Button(frame, "", 2036, 28, 76, 76, Hide, key: "close");
            close.name = "CloseCityTravel";

            Art(frame, "content_panel", 56, 156, 336, 694);
            _destinationList = QdaoUguiFactory.CreateRect("Destinations", frame, 76, 176, 296, 640);
            Art(frame, "content_panel", 420, 145, 716, 716);
            var previewRect = QdaoUguiFactory.CreateRect("SelectedMapPreview", frame, 431, 156, 694, 694);
            _preview = previewRect.gameObject.AddComponent<RawImage>();
            _preview.raycastTarget = false;
            _preview.color = Color.white;

            _name = Text(frame, "", 1190, 170, 850, 74, 52);
            _description = Text(frame, "", 1194, 264, 806, 118, 31, Muted, true);
            Text(frame, "四时景色", 1194, 424, 800, 45, 32, Gold);
            _day = Button(frame, "日景", 1190, 490, 358, 80,
                () => SelectFestival(false), key: "tab_selected", fontSize: 32);
            _day.name = "DayAppearance";
            _festivalButton = Button(frame, "节庆", 1584, 490, 426, 80,
                () => SelectFestival(true), key: "tab_normal", fontSize: 32);
            _festivalButton.name = "FestivalAppearance";
            _festivalLabel = _festivalButton.GetComponentInChildren<TMP_Text>();
            _appearanceHint = Text(frame, "", 1194, 598, 806, 70, 28, Muted, true);
            _status = Text(frame, "", 1194, 680, 806, 70, 28, Muted, true);
            // 区服切换放在“前往”左侧的空位。默认隐藏：没有可去的区服（单区部署、测试、截图验收）时
            // 窗口与原来完全一致，现有用例按名字找按钮也不受影响。
            _zoneButton = Button(frame, "本区", 1190, 770, 220, 82, CycleZone, key: "tab_normal", fontSize: 28);
            _zoneButton.name = "TravelZoneCycle";
            _zoneLabel = _zoneButton.GetComponentInChildren<TMP_Text>();
            _zoneLabel.overflowMode = TextOverflowModes.Ellipsis; // 区服名由运营配置，长名字不能撑破按钮
            _zoneButton.gameObject.SetActive(false);
            _travel = Button(frame, "前往", 1422, 770, 588, 82,
                () =>
                {
                    if (_selected == null) return;
                    var zone = SelectedZone;
                    if (zone != null) ZoneTravelRequested?.Invoke(zone.ZoneId, _selected.SceneConfigId, _festival);
                    else TravelRequested?.Invoke(_selected.SceneConfigId, _festival);
                },
                true, fontSize: 35);
            _travel.name = "TravelToSelectedCity";
            _travelLabel = _travel.GetComponentInChildren<TMP_Text>();
        }

        public void SetDestinations(IReadOnlyList<CityTravelDestination> destinations)
        {
            _destinations = destinations;
            _destinationButtons.Clear();
            Clear(_destinationList);
            if (destinations == null || destinations.Count == 0) return;
            foreach (var destination in destinations)
            {
                var value = destination;
                int row = _destinationButtons.Count;
                var button = Button(_destinationList, value.Name, 0, row * 144, 296, 116,
                    () => Select(value), key: "tab_normal", fontSize: 34);
                button.name = "CityDestination_" + value.SceneConfigId;
                _destinationButtons.Add(button);
            }
            Select(destinations[0]);
        }

        /// <summary>
        /// 设置可前往的其他区服（不含当前所在区）。每次设置都回到“本区”：
        /// 跨区要重新连接，不能让上一次的选择留到下一次打开，一次随手的“前往”就把人送去别的区。
        /// 传送途中忽略调用，避免按钮文案在“正在传送”时变来变去。
        /// </summary>
        public void SetZones(IReadOnlyList<CityTravelZone> zones)
        {
            if (_busy) return;
            _zones = zones;
            _zoneIndex = -1;
            _zoneButton.gameObject.SetActive(zones != null && zones.Count > 0);
            Refresh();
        }

        private CityTravelZone SelectedZone
            => _zones != null && _zoneIndex >= 0 && _zoneIndex < _zones.Count ? _zones[_zoneIndex] : null;

        private void CycleZone()
        {
            if (_busy || _zones == null || _zones.Count == 0) return;
            // 本区 → 各区服依次 → 回到本区
            _zoneIndex = _zoneIndex + 1 >= _zones.Count ? -1 : _zoneIndex + 1;
            Refresh();
        }

        public void Show(uint currentScene, bool currentFestival)
        {
            SetState(currentScene, currentFestival, _busy, "");
            if (_destinations != null)
                foreach (var destination in _destinations)
                    if (destination.SceneConfigId == currentScene) { Select(destination); break; }
            _root.gameObject.SetActive(true);
        }

        public void Hide() => _root.gameObject.SetActive(false);

        public void SetState(uint currentScene, bool currentFestival, bool busy, string status)
        {
            _currentScene = currentScene;
            _currentFestival = currentFestival;
            _busy = busy;
            _status.text = status ?? "";
            string currentName = "其他地图";
            if (_destinations != null)
                foreach (var destination in _destinations)
                    if (destination.SceneConfigId == currentScene) { currentName = destination.Name; break; }
            _current.text = "当前所在 · " + currentName;
            Refresh();
        }

        private void Select(CityTravelDestination destination)
        {
            if (_busy) return;
            _selected = destination;
            _festival = destination.SceneConfigId == _currentScene && _currentFestival && destination.HasFestival;
            Refresh();
        }

        private void SelectFestival(bool festival)
        {
            if (_busy || _selected == null || festival && !_selected.HasFestival) return;
            _festival = festival;
            Refresh();
        }

        private void Refresh()
        {
            if (_selected == null) { _travel.interactable = false; return; }
            for (int i = 0; i < _destinationButtons.Count; ++i)
            {
                bool selected = _destinations[i] == _selected;
                SetTab(_destinationButtons[i], selected);
                _destinationButtons[i].interactable = !_busy;
            }
            _name.text = _selected.Name;
            _description.text = _selected.Description;
            string path = _festival ? _selected.FestivalResourcePath : _selected.DayResourcePath;
            _preview.texture = string.IsNullOrEmpty(path) ? null : Resources.Load<Texture2D>(path);
            _preview.gameObject.SetActive(_preview.texture != null);
            _festivalButton.gameObject.SetActive(_selected.HasFestival);
            _festivalLabel.text = _selected.FestivalName ?? "节庆";
            _day.interactable = !_busy && _selected.HasFestival;
            _festivalButton.interactable = !_busy;
            SetTab(_day, !_festival);
            SetTab(_festivalButton, _festival);
            bool sameScene = _selected.SceneConfigId == _currentScene;
            // 跨区时“同一张地图”不再等于“原地换景”：别的区服的同名地图是另一处地方，必须能前往。
            var zone = SelectedZone;
            bool crossZone = zone != null;
            _zoneLabel.text = crossZone ? zone.Name : "本区";
            SetTab(_zoneButton, crossZone);
            _zoneButton.interactable = !_busy;
            _appearanceHint.text = crossZone
                ? "将前往其他区服，途中会重新连接，请稍候片刻。"
                : _selected.HasFestival
                    ? sameScene ? "更换此地景色，继续在原处游历。" : "选好景色，启程前往这片山海。"
                    : "灯火映长街，云游自此启程。";
            _travelLabel.text = _busy ? "正在传送…"
                : crossZone ? "前往" + zone.Name + " · " + _selected.Name
                : sameScene ? "应用景色" : "前往" + _selected.Name;
            _travel.interactable = !_busy &&
                (crossZone || !sameScene || _selected.HasFestival && _festival != _currentFestival);
        }

        private static void SetTab(Button button, bool selected)
        {
            ((Image)button.targetGraphic).sprite = Load(selected ? "tab_selected" : "tab_normal");
            button.GetComponentInChildren<TMP_Text>().color = selected ? Cream : Ink;
        }
    }
}
