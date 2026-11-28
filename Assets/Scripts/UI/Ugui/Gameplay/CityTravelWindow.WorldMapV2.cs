using System;
using System.Collections.Generic;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.World.CurrentMaps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public sealed partial class CityTravelWindow
    {
        private const string WorldMapResourceRoot = "UI/Ugui/WorldMapV2/";
        private static readonly Color QuietInk = QdaoUguiTheme.Html("#425B55");
        private static readonly Color RestingSealTint = new(0.77f, 0.86f, 0.79f);

        private Image _tideBanner, _tideBannerSeal;
        private TMP_Text _tideHeadline, _tideCountdown, _tideAction;
        private Button _tideQuickTravel;
        private float _overviewAgeSeconds;
        private MapPin[] _mapPins;

        private sealed class MapPin
        {
            public uint SceneConfigId;
            public Button Button;
            public TMP_Text Name, HeatLabel;
            public Image Heat, Tide, Hotspot, Merchant, Current;
        }

        private void BuildWorldMapOverlay(RectTransform frame)
        {
            // 原图坐标以左上角为原点。横幅从牌匾下边缘开始，底图其他地名保持纯装饰。
            _tideBanner = QdaoUguiFactory.CreateImage("WorldMapTideBanner", frame,
                820, 132, 920, 72, WorldMapSprite("tide_banner_active"));
            _tideBanner.type = Image.Type.Sliced;
            _tideBannerSeal = WorldMapImage("WorldMapTideSeal", _tideBanner.transform, 31, 12, 48, 48, "seal_tide");
            _tideHeadline = Text(_tideBanner.transform, "", 95, 10, 385, 52, 32, Cream);
            _tideHeadline.name = "WorldMapTideHeadline";
            _tideHeadline.overflowMode = TextOverflowModes.Ellipsis;
            _tideCountdown = Text(_tideBanner.transform, "", 514, 10, 140, 52, 25, Cream,
                alignment: TextAlignmentOptions.Center);
            _tideCountdown.name = "WorldMapTideCountdown";
            _tideQuickTravel = WorldMapButton("WorldMapTideQuickTravel", "", _tideBanner.transform,
                660, 8, 220, 56, "tide_quick_travel_btn", QdaoUguiTheme.DarkBrown, 28, () =>
                {
                    if (_tideQuickTravel.interactable) TideQuickTravelRequested?.Invoke();
                });
            _tideAction = _tideQuickTravel.GetComponentInChildren<TMP_Text>();
            _tideBanner.gameObject.SetActive(false);

            _mapPins = new[]
            {
                CreateMapPin(frame, 1, 1265, 545 + 30), // 下移牌面以盖住原城名，地标锚点仍是上方的金色珠饰。
                CreateMapPin(frame, 2, 1375, 248),
                CreateMapPin(frame, 5, 925, 252),
                CreateMapPin(frame, 3, 1775, 245)
            };
        }

        private MapPin CreateMapPin(RectTransform frame, uint sceneConfigId, float x, float y)
        {
            var button = QdaoUguiFactory.CreateArtButton("WorldMapPin_" + sceneConfigId,
                frame, x - 100, y - 36, 200, 72, WorldMapSprite("map_pin_base_normal"), out var plate);
            plate.preserveAspect = true;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(() =>
            {
                if (!button.interactable || CurrentMapCatalog.FindForOnline(sceneConfigId)?.CanTravelOnline != true) return;
                var destination = FindDestination(_onlineDestinations, sceneConfigId);
                if (destination == null) return;
                Select(destination);
                OpenOptions();
            });

            var name = Text(button.transform, "", 32, 12, 136, 42, 26, Ink,
                alignment: TextAlignmentOptions.Center);
            name.name = "WorldMapPinName";
            var heat = WorldMapImage("WorldMapPinHeat", button.transform, 8, 67, 92, 34, "heat_pill_quiet");
            var heatLabel = Text(heat.transform, "", 9, 0, 74, 34, 20, QuietInk,
                alignment: TextAlignmentOptions.Center);
            var tide = WorldMapImage("WorldMapPinTide", button.transform, 8, 67, 34, 34, "seal_tide");
            var hotspot = WorldMapImage("WorldMapPinHotspot", button.transform, 8, 67, 34, 34, "seal_hotspot");
            var merchant = WorldMapImage("WorldMapPinMerchant", button.transform, 8, 67, 34, 34, "seal_merchant");
            var current = WorldMapImage("WorldMapPinCurrent", button.transform, 8, 67, 108, 34, "heat_pill_quiet");
            Text(current.transform, "当前所在", 9, 0, 90, 34, 18, QuietInk,
                alignment: TextAlignmentOptions.Center);
            button.gameObject.SetActive(false);
            return new MapPin
            {
                SceneConfigId = sceneConfigId, Button = button, Name = name, Heat = heat, HeatLabel = heatLabel,
                Tide = tide, Hotspot = hotspot, Merchant = merchant, Current = current
            };
        }

        public void RefreshTideBanner(float overviewAgeSeconds)
        {
            _overviewAgeSeconds = overviewAgeSeconds;
            var tide = _overview?.Tide;
            bool canTravel = tide != null && CurrentMapCatalog.FindForOnline(tide.SceneConfigId)?.CanTravelOnline == true;
            var view = WorldMapOverviewModels.BuildTideBanner(_overview, _currentScene, ResolveWorldMapName,
                WorldMapOverviewModels.EstimateServerNowMs(_overview, overviewAgeSeconds), canTravel, _busy);
            _tideBanner.gameObject.SetActive(view.Visible);
            _tideQuickTravel.gameObject.SetActive(view.Visible && !string.IsNullOrEmpty(view.ActionLabel));
            _tideQuickTravel.interactable = view.CanQuickTravel;
            if (!view.Visible) return;

            _tideBanner.sprite = WorldMapSprite(view.Active ? "tide_banner_active" : "tide_banner_resting");
            _tideBannerSeal.color = view.Active ? Color.white : RestingSealTint;
            _tideHeadline.color = view.Active ? Cream : Ink;
            _tideCountdown.color = view.Active ? Cream : Ink;
            _tideHeadline.text = view.Headline;
            _tideCountdown.text = view.CountdownText;
            _tideHeadline.rectTransform.anchoredPosition = new Vector2(95, view.Active ? -7 : -10);
            _tideCountdown.rectTransform.anchoredPosition = new Vector2(view.Active ? 514 : 462, view.Active ? -7 : -10);
            _tideCountdown.rectTransform.sizeDelta = new Vector2(view.Active ? 140 : 332, 52);
            _tideAction.text = view.ActionLabel;
        }

        private void RefreshWorldMapPins()
        {
            bool showWorldStatus = _overview?.Tide != null;
            foreach (var pin in _mapPins)
            {
                uint id = pin.SceneConfigId;
                bool online = FindDestination(_onlineDestinations, id) != null &&
                    CurrentMapCatalog.FindForOnline(id)?.CanTravelOnline == true;
                pin.Button.gameObject.SetActive(online);
                pin.Button.interactable = online && !_busy;
                if (!online) continue;

                var view = WorldMapOverviewModels.BuildPinView(_overview, id, _currentScene, online);
                bool tide = showWorldStatus && view.IsTideMap;
                bool selected = _selected?.SceneConfigId == id;
                pin.Button.image.sprite = WorldMapSprite(tide ? "map_pin_base_tide"
                    : selected ? "map_pin_base_selected" : "map_pin_base_normal");
                pin.Name.text = ResolveWorldMapName(id);
                pin.Name.color = tide || selected ? Cream : Ink;

                bool showHeat = showWorldStatus && view.HasOverviewEntry && !string.IsNullOrEmpty(view.HeatLabel);
                pin.Heat.gameObject.SetActive(showHeat);
                if (showHeat)
                {
                    string heatSprite = view.HeatLevel switch
                    {
                        global::WorldMapHeatLevel.Crowded => "heat_pill_crowded",
                        global::WorldMapHeatLevel.Active => "heat_pill_active",
                        _ => "heat_pill_quiet"
                    };
                    pin.Heat.sprite = WorldMapSprite(heatSprite);
                    pin.HeatLabel.text = view.HeatLabel;
                    pin.HeatLabel.color = view.HeatLevel == global::WorldMapHeatLevel.Quiet ? QuietInk : Cream;
                }

                float badgeX = showHeat ? 104 : 8;
                badgeX = PositionPinBadge(pin.Tide, tide, badgeX);
                badgeX = PositionPinBadge(pin.Hotspot, showWorldStatus && view.IsDailyHotspot, badgeX);
                badgeX = PositionPinBadge(pin.Merchant, showWorldStatus && view.HasMysteryMerchant, badgeX);
                PositionPinBadge(pin.Current, view.IsCurrentScene, badgeX);
            }
        }

        private static float PositionPinBadge(Image image, bool visible, float x)
        {
            image.gameObject.SetActive(visible);
            if (!visible) return x;
            image.rectTransform.anchoredPosition = new Vector2(x, -67);
            return x + image.rectTransform.sizeDelta.x + 2;
        }

        private void SelectDestinationPage(bool gallery)
        {
            if (_busy || gallery == _showGallery) return;
            var page = gallery ? _galleryDestinations : _onlineDestinations;
            if (page.Count > 0) Select(page[0]);
            else
            {
                ReflowDestinationButtons(gallery);
                _selected = null;
                Refresh();
            }
        }

        private void ReflowDestinationButtons(bool gallery)
        {
            if (_showGallery != gallery) _zoneIndex = -1;
            _showGallery = gallery;
            var page = gallery ? _galleryDestinations : _onlineDestinations;
            foreach (var button in _destinationButtons.Values) button.gameObject.SetActive(false);
            for (int i = 0; i < page.Count; ++i)
            {
                var button = _destinationButtons[page[i]];
                ((RectTransform)button.transform).anchoredPosition = new Vector2(i * DestinationSpacing, 0);
                button.gameObject.SetActive(true);
            }
            _destinationContent.sizeDelta = new Vector2(Mathf.Max(DestinationStripWidth, page.Count * DestinationSpacing), 58);
            _destinationContent.anchoredPosition = Vector2.zero;
            _destinationScroll.StopMovement();
            SetDestinationPageArt(_onlineTab, !gallery);
            SetDestinationPageArt(_galleryTab, gallery);
        }

        private static void SetDestinationPageArt(Button tab, bool active)
        {
            tab.image.sprite = WorldMapSprite(active ? "drawer_tab_active" : "drawer_tab_normal");
            tab.GetComponentInChildren<TMP_Text>(true).color = active ? Cream : Ink;
        }

        private string ResolveWorldMapName(uint sceneConfigId)
        {
            // 地图标牌用地标正式名，目录中的临时底图名与皮肤名仍随原有资料保存。
            return sceneConfigId switch
            {
                1 => "归元城",
                2 => "八景岛",
                3 => "月汐湾",
                4 => "青岚镇",
                5 => "碧渊水宫",
                6 => "沧澜港",
                7 => "灵禾渡",
                18000 => "山顶雪区",
                20000 => "水车庭院",
                _ => FindDestination(_destinations, sceneConfigId)?.Name ?? ""
            };
        }

        private static CityTravelDestination FindDestination(IReadOnlyList<CityTravelDestination> destinations, uint sceneConfigId)
        {
            if (destinations == null) return null;
            foreach (var destination in destinations)
                if (destination?.SceneConfigId == sceneConfigId) return destination;
            return null;
        }

        private static Sprite WorldMapSprite(string name) => QdaoUguiTheme.RequireSprite(WorldMapResourceRoot + name);

        private static Image WorldMapImage(string name, UnityEngine.Transform parent, float x, float y,
            float width, float height, string asset)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, width, height, WorldMapSprite(asset));
            image.type = image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            return image;
        }

        private static Button WorldMapButton(string name, string value, UnityEngine.Transform parent, float x, float y,
            float width, float height, string asset, Color ink, float fontSize, Action click)
        {
            var button = QdaoUguiFactory.CreateArtButton(name, parent, x, y, width, height,
                WorldMapSprite(asset), out var image);
            image.type = Image.Type.Sliced;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(() => click());
            Text(button.transform, value, 30, 0, width - 60, height, fontSize, ink,
                alignment: TextAlignmentOptions.Center);
            return button;
        }
    }
}
