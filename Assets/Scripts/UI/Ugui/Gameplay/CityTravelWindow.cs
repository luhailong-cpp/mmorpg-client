using System;
using System.Collections.Generic;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.World.Tianyong;
using MmorpgClient.World.CurrentMaps;
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
        public bool Available = true;
        public bool OnlineAvailable = true;
        public string DefaultAppearance = "normal";
        public CurrentMapDefinition Map;
        public bool HasFestival => !string.IsNullOrEmpty(FestivalResourcePath);
        public string TexturePath(string appearance) => Map != null
            ? Map.ForAppearance(appearance)?.TexturePath
            : appearance == "normal" ? DayResourcePath : appearance == "spring" ? FestivalResourcePath : null;
    }

    /// <summary>可前往的其他区服；只是展示数据，能不能去由服务端在传送请求里裁决。</summary>
    public sealed class CityTravelZone
    {
        public uint ZoneId;
        public string Name;
    }

    /// <summary>沿用游戏现有纸面窗口，展示整张地图并发出传送或换景意图。</summary>
    public sealed partial class CityTravelWindow
    {
        public event Action<uint, bool> TravelRequested;
        public event Action<uint, string> AppearanceTravelRequested;
        public event Action<uint, uint, string> AppearanceZoneTravelRequested;
        /// <summary>
        /// 选了其他区服时，“前往”发出的是这个事件（区服编号、场景配置编号、节庆景色），不再发
        /// <see cref="TravelRequested"/>：跨区要换连接并重新登录，走的是另一条请求，两者不能混用。
        /// </summary>
        public event Action<uint, uint, bool> ZoneTravelRequested;
        public event Action TideQuickTravelRequested;
        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public uint SelectedSceneConfigId => _selected?.SceneConfigId ?? 0;
        public bool SelectedFestival => _festival;
        public string SelectedAppearance => _appearance;
        public bool TravelEnabled => _travel.interactable;
        /// <summary>零表示留在本区（默认）。</summary>
        public uint SelectedZoneId => SelectedZone?.ZoneId ?? 0;

        private readonly RectTransform _root, _destinationList, _destinationContent, _travelPanel;
        private readonly ScrollRect _destinationScroll;
        private readonly RawImage _preview;
        private readonly AspectRatioFitter _previewAspect;
        private readonly TMP_Text _name, _description, _current, _status, _appearanceHint, _travelLabel;
        private readonly Button _day, _festivalButton, _travel, _zoneButton;
        private readonly Button _onlineTab, _galleryTab;
        private readonly TMP_Text _festivalLabel, _zoneLabel;
        private readonly List<Button> _appearanceButtons = new();
        private static readonly string[] AppearanceIds = { "normal", "spring", "lantern", "mid-autumn" };
        private string _appearance = "normal", _currentAppearance = "normal";
        private IReadOnlyList<CityTravelZone> _zones;
        private int _zoneIndex = -1; // 负一表示本区；其余是 _zones 的下标
        private const float DestinationStripWidth = 922f;
        private const float DestinationSpacing = 172f;
        private readonly Dictionary<CityTravelDestination, Button> _destinationButtons = new();
        private IReadOnlyList<CityTravelDestination> _destinations;
        private IReadOnlyList<CityTravelDestination> _onlineDestinations = Array.Empty<CityTravelDestination>();
        private IReadOnlyList<CityTravelDestination> _galleryDestinations = Array.Empty<CityTravelDestination>();
        private CityTravelDestination _selected;
        private WorldMapOverviewSnapshot _overview;
        private uint _currentScene;
        private bool _festival, _busy, _showGallery;

        public CityTravelWindow(UnityEngine.Transform parent)
        {
            _root=QdaoUguiFactory.CreateStretch("CityTravelWindow",parent,Vector4.zero);_root.gameObject.SetActive(false);
            var shade=_root.gameObject.AddComponent<Image>();shade.color=new Color(.025f,.10f,.08f,.68f);shade.raycastTarget=true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame=QdaoUguiFactory.CreateCenteredRect("CityTravelFrame",_root,2560,1080);
            QdaoUguiFactory.CreateImage("ApprovedWorldMap",frame,0,0,2560,1080,QdaoUguiTheme.RequireSprite(FidelityResourceRoot+"world_skin"));
            var close=QdaoUguiFactory.CreateHitButton("CloseCityTravel",frame,2435,12,93,93);close.onClick.AddListener(Hide);
            _current=Text(frame,"",278,984,170,40,26,Ink);_current.name="CityTravelCurrent";
            var travelEntry=QdaoUguiFactory.CreateHitButton("OpenCityTravelOptions",frame,60,970,420,76);
            travelEntry.onClick.AddListener(()=>OpenOptions());
            var trials=QdaoUguiFactory.CreateHitButton("WorldMapTrials",frame,1895,960,305,80);
            var realms=QdaoUguiFactory.CreateHitButton("WorldMapSecretRealms",frame,2218,960,304,80);
            trials.onClick.AddListener(()=>OpenOptions("修行试炼尚未开放，可先选择游历目的地。"));
            realms.onClick.AddListener(()=>OpenOptions("云外秘境尚未开放，可先选择游历目的地。"));
            BuildWorldMapOverlay(frame);

            _travelPanel = QdaoUguiFactory.CreateRect("WorldTravelOptions", frame, 478, 710, 1400, 330);
            WorldMapImage("TravelOptionsPaper", _travelPanel, 0, 0, 1400, 330,
                "world_map_paper_panel").raycastTarget = true;
            _onlineTab = WorldMapButton("WorldMapTravelTab", "游历", _travelPanel, 32, 12, 190, 54,
                "drawer_tab_active", Cream, 29, () => SelectDestinationPage(false));
            _galleryTab = WorldMapButton("WorldMapGalleryTab", "览景", _travelPanel, 232, 12, 190, 54,
                "drawer_tab_normal", Ink, 29, () => SelectDestinationPage(true));
            var dismiss = FidelityButton(_travelPanel, "收起", 1215, 14, 154, 50,
                () => _travelPanel.gameObject.SetActive(false), 24);
            dismiss.name = "CloseCityTravelOptions";
            _destinationList = QdaoUguiFactory.CreateRect("Destinations", _travelPanel, 446, 72, DestinationStripWidth, 58);
            _destinationList.gameObject.AddComponent<RectMask2D>();
            _destinationContent = QdaoUguiFactory.CreateRect("DestinationItems", _destinationList, 0, 0, DestinationStripWidth, 58);
            _destinationScroll = _destinationList.gameObject.AddComponent<ScrollRect>();
            _destinationScroll.viewport = _destinationList;
            _destinationScroll.content = _destinationContent;
            _destinationScroll.horizontal = true;
            _destinationScroll.vertical = false;
            _destinationScroll.movementType = ScrollRect.MovementType.Clamped;
            _destinationScroll.inertia = false;
            _destinationScroll.scrollSensitivity = 35;

            QdaoRefreshArt.Panel("MapPreviewPaper", _travelPanel, 32, 80, 390, 216);
            var previewArea = QdaoUguiFactory.CreateRect("MapPreviewArea", _travelPanel, 40, 88, 374, 200);
            var previewRect = QdaoUguiFactory.CreateStretch("SelectedMapPreview", previewArea, Vector4.zero);
            _preview = previewRect.gameObject.AddComponent<RawImage>();
            _preview.raycastTarget = false;
            _previewAspect = previewRect.gameObject.AddComponent<AspectRatioFitter>();
            _previewAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _name=Text(_travelPanel,"",446,142,922,38,28,Ink);_name.name="SelectedDestinationName";
            _description=Text(_travelPanel,"",446,181,922,30,23,Ink);_description.name="SelectedDestinationDescription";_description.overflowMode=TextOverflowModes.Ellipsis;
            _appearanceHint=Text(_travelPanel,"",446,216,922,28,21,Muted);_appearanceHint.name="DestinationAppearanceHint";
            _status=Text(_travelPanel,"",446,302,922,24,21,Ink);_status.name="CityTravelStatus";
            _day=FidelityButton(_travelPanel,"日景",446,251,94,46,()=>SelectFestival(false),25);_day.name="DayAppearance";
            _festivalButton=FidelityButton(_travelPanel,"节庆",548,251,94,46,()=>SelectFestival(true),25);_festivalButton.name="FestivalAppearance";
            _festivalLabel=_festivalButton.GetComponentInChildren<TMP_Text>();
            _appearanceButtons.Add(_day);_appearanceButtons.Add(_festivalButton);
            _appearanceButtons.Add(FidelityButton(_travelPanel,"元宵",650,251,94,46,()=>SelectAppearance("lantern"),25));
            _appearanceButtons.Add(FidelityButton(_travelPanel,"中秋",752,251,94,46,()=>SelectAppearance("mid-autumn"),25));
            _zoneButton=FidelityButton(_travelPanel,"本区",854,251,150,46,CycleZone,25);_zoneButton.name="TravelZoneCycle";
            _zoneLabel=_zoneButton.GetComponentInChildren<TMP_Text>();_zoneLabel.overflowMode=TextOverflowModes.Ellipsis;_zoneButton.gameObject.SetActive(false);
            _travel=WorldMapButton("TravelToSelectedCity", "前往", _travelPanel, 1018, 247, 350, 54,
                "drawer_tab_active", Cream, 27, () =>
            {
                if(_selected==null||!_selected.Available||!_travel.interactable)return;var zone=SelectedZone;
                if(zone!=null)
                {
                    if(AppearanceZoneTravelRequested!=null)AppearanceZoneTravelRequested(zone.ZoneId,_selected.SceneConfigId,_appearance);
                    else if(_appearance=="normal"||_appearance=="spring")ZoneTravelRequested?.Invoke(zone.ZoneId,_selected.SceneConfigId,_festival);
                }
                else if(AppearanceTravelRequested!=null)AppearanceTravelRequested(_selected.SceneConfigId,_appearance);
                else if(_appearance=="normal"||_appearance=="spring")TravelRequested?.Invoke(_selected.SceneConfigId,_festival);
            });
            _travelLabel=_travel.GetComponentInChildren<TMP_Text>();
            _travelPanel.gameObject.SetActive(false);
        }

        private void OpenOptions(string message=null)
        {
            _travelPanel.gameObject.SetActive(true);
            if(!string.IsNullOrEmpty(message))_status.text=message;
        }

        public void SetDestinations(IReadOnlyList<CityTravelDestination> destinations)
        {
            _destinations = destinations;
            _onlineDestinations = CityTravelUiRoot.FilterOnlineDestinations(destinations);
            _galleryDestinations = CityTravelUiRoot.FilterGalleryDestinations(destinations);
            _destinationButtons.Clear();
            Clear(_destinationContent);
            if (destinations != null)
            {
                foreach (var destination in destinations)
                {
                    if (destination == null) continue;
                    var button = FidelityButton(_destinationContent, ResolveWorldMapName(destination.SceneConfigId),
                        0, 0, 166, 58, () => Select(destination), 26);
                    button.name = "CityDestination_" + destination.SceneConfigId;
                    _destinationButtons.Add(destination, button);
                }
            }

            _selected = null;
            ReflowDestinationButtons(false);
            if (_onlineDestinations.Count > 0) Select(_onlineDestinations[0]);
            else Refresh();
        }

        public void SetOverview(WorldMapOverviewSnapshot overview, float overviewAgeSeconds = 0f)
        {
            _overview = overview;
            _overviewAgeSeconds = overviewAgeSeconds;
            Refresh();
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
            => Show(currentScene,currentFestival ? "spring" : "normal");

        public void Show(uint currentScene, string currentAppearance)
        {
            SetState(currentScene, currentAppearance, _busy, "");
            if (!_busy)
            {
                ReflowDestinationButtons(false);
                var destination = FindDestination(_onlineDestinations, currentScene);
                if (destination != null) Select(destination);
                else if (_onlineDestinations.Count > 0) Select(_onlineDestinations[0]);
                else
                {
                    _selected = null;
                    Refresh();
                }
            }
            _travelPanel.gameObject.SetActive(false);
            _root.gameObject.SetActive(true);
        }

        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        public void SetState(uint currentScene, bool currentFestival, bool busy, string status)
            => SetState(currentScene,currentFestival ? "spring" : "normal",busy,status);

        public void SetState(uint currentScene, string currentAppearance, bool busy, string status)
        {
            _currentScene = currentScene;
            _currentAppearance = currentAppearance;
            _busy = busy;
            _status.text = status ?? "";
            var current = FindDestination(_destinations, currentScene);
            _current.text = current != null ? ResolveWorldMapName(currentScene) : "其他地图";
            if(busy || !string.IsNullOrEmpty(status))_travelPanel.gameObject.SetActive(true);
            Refresh();
        }

        private void Select(CityTravelDestination destination)
        {
            if (_busy || destination == null || !destination.Available) return;
            if (_showGallery == destination.OnlineAvailable) ReflowDestinationButtons(!destination.OnlineAvailable);
            _selected = destination;
            _appearance = destination.SceneConfigId == _currentScene && destination.TexturePath(_currentAppearance) != null
                ? _currentAppearance : destination.DefaultAppearance;
            _festival = _appearance != "normal";
            Refresh();
        }

        private void SelectFestival(bool festival)
            => SelectAppearance(festival ? "spring" : "normal");

        private void SelectAppearance(string appearance)
        {
            if (_busy || _selected == null || string.IsNullOrEmpty(_selected.TexturePath(appearance))) return;
            _appearance = appearance;
            _festival = appearance != "normal";
            Refresh();
        }

        private void Refresh()
        {
            RefreshTideBanner(_overviewAgeSeconds);
            RefreshWorldMapPins();
            _onlineTab.interactable = !_busy;
            _galleryTab.interactable = !_busy;
            foreach (var entry in _destinationButtons)
            {
                SetTab(entry.Value, entry.Key == _selected);
                entry.Value.interactable = !_busy && entry.Key.Available;
            }
            if (_selected == null)
            {
                _name.text = _showGallery ? "暂无可览景色" : "暂无开放地图";
                _description.text = "";
                _appearanceHint.text = "";
                _preview.texture = null;
                _preview.gameObject.SetActive(false);
                foreach (var button in _appearanceButtons) button.gameObject.SetActive(false);
                _zoneButton.gameObject.SetActive(false);
                _travel.interactable = false;
                _travelLabel.text = "尚未开放传送";
                return;
            }

            string selectedName = ResolveWorldMapName(_selected.SceneConfigId);
            string badges = WorldMapOverviewModels.FormatDestinationBadges(_overview, _selected.SceneConfigId);
            _name.text = string.IsNullOrEmpty(badges) ? selectedName : selectedName + "  [" + badges + "]";
            string habitatSummary = _selected.OnlineAvailable
                ? WorldMapOverviewModels.FormatDrawerHabitatSummary(
                    _overview, _selected.SceneConfigId, WorldMapOverviewModels.EstimateServerNowMs(_overview, _overviewAgeSeconds))
                : "";
            _description.text = string.IsNullOrEmpty(habitatSummary)
                ? _selected.Description
                : string.IsNullOrEmpty(_selected.Description)
                    ? habitatSummary
                    : _selected.Description + " · " + habitatSummary;
            string path = _selected.TexturePath(_appearance);
            _preview.texture = string.IsNullOrEmpty(path) ? null : Resources.Load<Texture2D>(path);
            _preview.gameObject.SetActive(_preview.texture != null);
            if (_preview.texture != null) _previewAspect.aspectRatio = (float)_preview.texture.width / _preview.texture.height;
            for(int i=0;i<AppearanceIds.Length;i++)
            {
                bool delivered=!string.IsNullOrEmpty(_selected.TexturePath(AppearanceIds[i]));
                _appearanceButtons[i].gameObject.SetActive(delivered);
                _appearanceButtons[i].interactable=!_busy;
                SetTab(_appearanceButtons[i],_appearance==AppearanceIds[i]);
            }
            _festivalLabel.text = _selected.FestivalName ?? "春节";
            bool sameScene = _selected.SceneConfigId == _currentScene;
            // 跨区时“同一张地图”不再等于“原地换景”：别的区服的同名地图是另一处地方，必须能前往。
            var zone = SelectedZone;
            bool crossZone = zone != null;
            _zoneLabel.text = crossZone ? zone.Name : "本区";
            SetTab(_zoneButton, crossZone);
            _zoneButton.interactable = !_busy && _selected.OnlineAvailable;
            _zoneButton.gameObject.SetActive(_selected.OnlineAvailable && _zones != null && _zones.Count > 0);
            string overviewHint = _selected.OnlineAvailable && !crossZone
                ? WorldMapOverviewModels.FormatDestinationHint(_overview, _selected.SceneConfigId) : "";
            string baseHint = !_selected.OnlineAvailable ? "景色已收录；此地图尚未开放传送。" : crossZone
                ? "将前往其他区服，途中会重新连接，请稍候片刻。"
                : _selected.HasFestival
                    ? sameScene ? "更换此地景色，继续在原处游历。" : "选好景色，启程前往这片山海。"
                    : "沿着石路自在游历。";
            _appearanceHint.text = string.IsNullOrEmpty(overviewHint) ? baseHint : overviewHint;
            _travelLabel.text = !_selected.OnlineAvailable ? "尚未开放传送" : _busy ? "正在传送…"
                : crossZone ? "前往" + zone.Name + " · " + selectedName
                : sameScene ? (_selected.HasFestival ? "应用景色" : "已在此地") : "前往" + selectedName;
            _travel.interactable = !_busy && _selected.Available && _selected.OnlineAvailable &&
                (crossZone || !sameScene || !string.IsNullOrEmpty(_selected.TexturePath(_currentAppearance)) && _appearance != _currentAppearance);
        }

        private const string FidelityResourceRoot="UI/Ugui/WorldMapOriginal/Fidelity20261016/";
        private static Button FidelityButton(UnityEngine.Transform parent,string value,float x,float y,float w,float h,Action click,float size)
        {
            var button=QdaoRefreshArt.Button(value,parent,x,y,w,h,"tab_normal",out var image);
            button.navigation=new Navigation{mode=Navigation.Mode.Automatic};button.onClick.AddListener(()=>click?.Invoke());
            Text(button.transform,value,12,0,w-24,h,size,Ink,alignment:TextAlignmentOptions.Center);return button;
        }

        private static void SetTab(Button button,bool selected)
        {
            var image=button.targetGraphic as Image;
            if(image!=null&&image.sprite!=null)image.color=selected?new Color(1f,.94f,.75f):Color.white;
            var label=button.GetComponentInChildren<TMP_Text>();if(label!=null)label.color=button.name=="DayAppearance"||button.name=="FestivalAppearance"?(selected?Ink:Muted):Ink;
        }
    }
}
