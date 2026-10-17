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
            _root=QdaoUguiFactory.CreateStretch("CityTravelWindow",parent,Vector4.zero);_root.gameObject.SetActive(false);
            var shade=_root.gameObject.AddComponent<Image>();shade.color=new Color(.025f,.10f,.08f,.68f);shade.raycastTarget=true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame=QdaoUguiFactory.CreateCenteredRect("CityTravelFrame",_root,2560,1080);
            QdaoUguiFactory.CreateImage("ApprovedWorldMap",frame,0,0,2560,1080,QdaoUguiTheme.RequireSprite(FidelityResourceRoot+"world_skin"));
            var close=QdaoUguiFactory.CreateHitButton("CloseCityTravel",frame,2435,12,93,93);close.onClick.AddListener(Hide);
            _current=Text(frame,"",128,984,307,40,26,Ink);_current.name="CityTravelCurrent";
            _destinationList=QdaoUguiFactory.CreateRect("Destinations",frame,515,976,800,64);
            // The approved world map is the main artwork. Existing destination-preview data remains available to the view,
            // but its old square thumbnail is not a second painted map over the original composition.
            var previewRect=QdaoUguiFactory.CreateRect("SelectedMapPreview",frame,0,0,0,0);
            _preview=previewRect.gameObject.AddComponent<RawImage>();_preview.raycastTarget=false;_preview.color=Color.clear;
            _name=Text(frame,"",1336,984,292,43,25,Ink);_name.name="SelectedDestinationName";
            _description=Text(frame,"",515,925,1050,32,23,Cream);_description.name="SelectedDestinationDescription";
            _appearanceHint=Text(frame,"",515,893,1050,30,21,Cream);_appearanceHint.name="DestinationAppearanceHint";
            _status=Text(frame,"",515,859,1400,32,23,Cream);_status.name="CityTravelStatus";
            _day=QdaoUguiFactory.CreateHitButton("DayAppearance",frame,2007,970,80,67);_day.onClick.AddListener(()=>SelectFestival(false));
            Text(_day.transform,"日景",0,0,80,67,25,Cream,alignment:TextAlignmentOptions.Center);
            _festivalButton=QdaoUguiFactory.CreateHitButton("FestivalAppearance",frame,2087,970,116,67);_festivalButton.onClick.AddListener(()=>SelectFestival(true));
            _festivalLabel=Text(_festivalButton.transform,"节庆",0,0,116,67,25,Cream,alignment:TextAlignmentOptions.Center);
            _zoneButton=FidelityButton(frame,"本区",1648,976,236,64,CycleZone,25);_zoneButton.name="TravelZoneCycle";
            _zoneLabel=_zoneButton.GetComponentInChildren<TMP_Text>();_zoneLabel.overflowMode=TextOverflowModes.Ellipsis;_zoneButton.gameObject.SetActive(false);
            _travel=QdaoUguiFactory.CreateHitButton("TravelToSelectedCity",frame,2218,960,304,80);
            _travel.onClick.AddListener(()=>
            {
                if(_selected==null)return;var zone=SelectedZone;
                if(zone!=null)ZoneTravelRequested?.Invoke(zone.ZoneId,_selected.SceneConfigId,_festival);
                else TravelRequested?.Invoke(_selected.SceneConfigId,_festival);
            });
            _travelLabel=Text(_travel.transform,"前往",91,0,203,80,27,Cream,alignment:TextAlignmentOptions.Center);
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
                var button=FidelityButton(_destinationList,value.Name,row*195,0,190,64,()=>Select(value),26);
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

        private const string FidelityResourceRoot="UI/Ugui/WorldMapOriginal/Fidelity20261016/";
        private static Button FidelityButton(UnityEngine.Transform parent,string value,float x,float y,float w,float h,Action click,float size)
        {
            var button=QdaoUguiFactory.CreateArtButton(value,parent,x,y,w,h,QdaoUguiTheme.RequireSprite(FidelityResourceRoot+"destination_plate"),out var image);
            image.type=Image.Type.Simple;button.navigation=new Navigation{mode=Navigation.Mode.Automatic};button.onClick.AddListener(()=>click?.Invoke());
            Text(button.transform,value,12,0,w-24,h,size,Ink,alignment:TextAlignmentOptions.Center);return button;
        }

        private static void SetTab(Button button,bool selected)
        {
            var image=button.targetGraphic as Image;
            if(image!=null&&image.sprite!=null)image.color=selected?new Color(1f,.94f,.75f):Color.white;
            var label=button.GetComponentInChildren<TMP_Text>();if(label!=null)label.color=button.name=="DayAppearance"||button.name=="FestivalAppearance"?(selected?Cream:Gold):Ink;
        }
    }
}
