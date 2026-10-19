using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    public sealed partial class QdaoServerSelectView
    {
        private const string MasterVolumePreference = "Qdao.MasterVolume";
        private const string FullscreenPreference = "Qdao.Fullscreen";
        [SerializeField] private Button _landingAnnouncement, _landingSettings;
        [SerializeField] private RectTransform _announcementPanel, _settingsPanel;
        [SerializeField] private TMP_Text _announcementBody, _settingsVolumeValue;
        [SerializeField] private ScrollRect _announcementScroll;
        [SerializeField] private Button _announcementClose, _settingsClose;
        [SerializeField] private Slider _settingsVolumeSlider;
        [SerializeField] private Toggle _settingsFullscreen;

        private bool LoginUtilityOpen => (_announcementPanel != null && _announcementPanel.gameObject.activeSelf)
            || (_settingsPanel != null && _settingsPanel.gameObject.activeSelf);

        private RectTransform UtilityPanel(string name, string title)
        {
            var root = QdaoUguiFactory.CreateRect(name, _contentRoot, 0, 0, 2560, 1080);
            var blocker = QdaoUguiFactory.CreateImage(name + "Blocker", root, 0, 0, 2560, 1080, null, true);
            blocker.color = new Color(.025f, .07f, .06f, .72f);
            var frame = QdaoCoreFidelityArt.Panel(name + "Frame", root, 690, 235, 1180, 650, "main_frame");
            frame.raycastTarget = true;
            Label(name + "Title", root, 820, 282, 920, 78, title, 48, false, TextAlignmentOptions.Center);
            return root;
        }

        private void BuildLoginUtilities()
        {
            _announcementPanel = UtilityPanel("AnnouncementPanel", "游戏公告");
            var viewport = QdaoUguiFactory.CreateRect("AnnouncementViewport", _announcementPanel, 825, 391, 910, 328);
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            _announcementBody = Label("AnnouncementBody", viewport, 0, 0, 910, 328, "", 31);
            _announcementBody.alignment = TextAlignmentOptions.TopLeft;
            _announcementBody.textWrappingMode = TextWrappingModes.Normal;
            _announcementBody.overflowMode = TextOverflowModes.Overflow;
            _announcementScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _announcementScroll.viewport = viewport;
            _announcementScroll.content = _announcementBody.rectTransform;
            _announcementScroll.horizontal = false;
            _announcementScroll.vertical = true;
            _announcementScroll.movementType = ScrollRect.MovementType.Clamped;
            _announcementClose = ArtButton("AnnouncementClose", _announcementPanel, 1120, 767, 320, 80,
                "关闭", "clean_tab", out _);
            _announcementPanel.gameObject.SetActive(false);

            _settingsPanel = UtilityPanel("SettingsPanel", "游戏设置");
            Label("SettingsVolumeLabel", _settingsPanel, 845, 421, 180, 65, "声音音量", 34);
            var sliderRoot = QdaoUguiFactory.CreateRect("SettingsVolumeSlider", _settingsPanel, 1050, 430, 520, 50);
            var track = QdaoCoreFidelityArt.Panel("VolumeTrack", sliderRoot, 0, 0, 520, 50, "clean_search");
            track.raycastTarget = true;
            var fillArea = QdaoUguiFactory.CreateRect("VolumeFillArea", sliderRoot, 25, 17, 470, 16);
            var fill = QdaoUguiFactory.CreateImage("VolumeFill", fillArea, 0, 0, 470, 16, null);
            fill.color = QdaoUguiTheme.Html("#419B76");
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = QdaoUguiFactory.CreateRect("VolumeHandleArea", sliderRoot, 25, 4, 470, 42);
            var handle = QdaoUguiFactory.CreateImage("VolumeHandle", handleArea, 0, 4, 42, 42, _statusDotSprite, true);
            handle.color = QdaoUguiTheme.Html("#E7C96E");
            handle.rectTransform.pivot = new Vector2(.5f, .5f);
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = new Vector2(0, .5f);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            // Slider stretches the perpendicular anchors on every refresh.
            // The area's height supplies the handle height; keep its extra height zero.
            handle.rectTransform.sizeDelta = new Vector2(42, 0);
            _settingsVolumeSlider = sliderRoot.gameObject.AddComponent<Slider>();
            _settingsVolumeSlider.fillRect = fill.rectTransform;
            _settingsVolumeSlider.handleRect = handle.rectTransform;
            _settingsVolumeSlider.targetGraphic = handle;
            _settingsVolumeSlider.minValue = 0; _settingsVolumeSlider.maxValue = 1;
            _settingsVolumeValue = Label("SettingsVolumeValue", _settingsPanel, 1610, 420, 130, 65, "", 30);

            var toggleRoot = QdaoUguiFactory.CreateRect("SettingsFullscreen", _settingsPanel, 1050, 548, 480, 64);
            var box = QdaoCoreFidelityArt.Panel("FullscreenBox", toggleRoot, 0, 0, 64, 64, "clean_tab");
            box.raycastTarget = true;
            var check = QdaoUguiFactory.CreateImage("FullscreenCheck", toggleRoot, 18, 16, 30, 30,
                QdaoCoreFidelityArt.Load("check"));
            Label("FullscreenLabel", toggleRoot, 92, 0, 310, 64, "全屏显示", 34);
            _settingsFullscreen = toggleRoot.gameObject.AddComponent<Toggle>();
            _settingsFullscreen.targetGraphic = box; _settingsFullscreen.graphic = check;
            _settingsClose = ArtButton("SettingsClose", _settingsPanel, 1120, 767, 320, 80,
                "完成", "clean_tab", out _);
            _settingsPanel.gameObject.SetActive(false);
        }

        private void BindLoginUtilityEvents()
        {
            _landingAnnouncement.onClick.AddListener(OnLandingAnnouncement);
            _landingSettings.onClick.AddListener(OnLandingSettings);
            _announcementClose.onClick.AddListener(CloseLoginUtilities);
            _settingsClose.onClick.AddListener(CloseLoginUtilities);
            _settingsVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            _settingsFullscreen.onValueChanged.AddListener(OnFullscreenChanged);
            if (Application.isPlaying)
            {
                AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePreference, AudioListener.volume));
                if (PlayerPrefs.HasKey(FullscreenPreference))
                    Screen.fullScreen = PlayerPrefs.GetInt(FullscreenPreference) != 0;
            }
        }

        private void UnbindLoginUtilityEvents()
        {
            _landingAnnouncement.onClick.RemoveListener(OnLandingAnnouncement);
            _landingSettings.onClick.RemoveListener(OnLandingSettings);
            _announcementClose.onClick.RemoveListener(CloseLoginUtilities);
            _settingsClose.onClick.RemoveListener(CloseLoginUtilities);
            _settingsVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
            _settingsFullscreen.onValueChanged.RemoveListener(OnFullscreenChanged);
        }

        private void OnLandingAnnouncement()
        {
            if (_busy || LoginUtilityOpen || (_credentialPanel != null && _credentialPanel.gameObject.activeSelf)) return;
            var text = new StringBuilder();
            var announcements = _app?.Session?.Announcements;
            if (announcements != null)
                foreach (var item in announcements)
                {
                    if (item == null) continue;
                    if (text.Length > 0) text.Append("\n\n");
                    if (!string.IsNullOrWhiteSpace(item.title)) text.AppendLine(item.title);
                    text.Append(item.content ?? string.Empty);
                }
            _announcementBody.text = text.Length == 0 ? "暂无公告" : text.ToString();
            _announcementBody.richText = false;
            float height = _announcementBody.GetPreferredValues(_announcementBody.text, 910, Mathf.Infinity).y;
            _announcementBody.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(328, height + 8));
            _announcementBody.rectTransform.anchoredPosition = Vector2.zero;
            _announcementPanel.gameObject.SetActive(true);
            _announcementScroll.verticalNormalizedPosition = 1;
            RefreshVisualState(); FocusUtility(_announcementClose);
        }

        private void OnLandingSettings()
        {
            if (_busy || LoginUtilityOpen || (_credentialPanel != null && _credentialPanel.gameObject.activeSelf)) return;
            float volume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePreference, AudioListener.volume));
            _settingsVolumeSlider.SetValueWithoutNotify(volume);
            _settingsVolumeValue.text = Mathf.RoundToInt(volume * 100) + "%";
            _settingsFullscreen.SetIsOnWithoutNotify(Application.isPlaying ? Screen.fullScreen
                : PlayerPrefs.GetInt(FullscreenPreference, Screen.fullScreen ? 1 : 0) != 0);
            _settingsPanel.gameObject.SetActive(true);
            RefreshVisualState(); FocusUtility(_settingsVolumeSlider);
        }

        private void OnMasterVolumeChanged(float value)
        {
            if (_busy) return;
            value = Mathf.Clamp01(value);
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(MasterVolumePreference, value);
            _settingsVolumeValue.text = Mathf.RoundToInt(value * 100) + "%";
        }

        private void OnFullscreenChanged(bool value)
        {
            if (_busy) return;
            PlayerPrefs.SetInt(FullscreenPreference, value ? 1 : 0);
            if (Application.isPlaying) Screen.fullScreen = value;
        }

        private void CloseLoginUtilities()
        {
            if (_busy) return;
            bool settings = _settingsPanel.gameObject.activeSelf;
            if (settings) PlayerPrefs.Save();
            _announcementPanel.gameObject.SetActive(false);
            _settingsPanel.gameObject.SetActive(false);
            RefreshVisualState(); FocusUtility(settings ? _landingSettings : _landingAnnouncement);
        }

        private static void FocusUtility(Selectable target)
        {
            if (EventSystem.current != null && target != null)
                EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        private void RefreshLoginUtilityState()
        {
            bool blocked = LoginUtilityOpen || (_credentialPanel != null && _credentialPanel.gameObject.activeSelf);
            if (blocked)
            {
                _landingServers.interactable = false;
                _landingEnter.interactable = false;
                _landingAccount.interactable = false;
            }
            _landingAnnouncement.interactable = !_busy && !blocked;
            _landingSettings.interactable = !_busy && !blocked;
            _announcementClose.interactable = !_busy;
            _settingsClose.interactable = !_busy;
            _settingsVolumeSlider.interactable = !_busy;
            _settingsFullscreen.interactable = !_busy;
        }
    }
}
