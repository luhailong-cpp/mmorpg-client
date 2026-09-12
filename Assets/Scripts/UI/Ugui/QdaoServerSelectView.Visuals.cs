using TMPro;
using MmorpgClient.Net;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    public sealed partial class QdaoServerSelectView
    {
        [SerializeField] private RectTransform _landingRoot;
        [SerializeField] private RectTransform _serverRoot;
        [SerializeField] private Button _landingServers;
        [SerializeField] private Button _landingEnter;
        [SerializeField] private Button _landingAccount;
        [SerializeField] private TMP_Text _landingServerText;
        [SerializeField] private TMP_Text _landingStatusText;
        [SerializeField] private TMP_Text _emptyListText;
        [SerializeField] private Image[] _serverBadges;
        [SerializeField] private Image[] _serverChecks;
        [SerializeField] private Image[] _serverRecommendations;
        [SerializeField] private TMP_Text _landingAvailability;
        [SerializeField] private Image _landingServerDot;

        private TMP_Text Label(string name, UnityEngine.Transform parent, float x, float y,
            float w, float h, string value, float size = 30f, bool light = false,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
            => QdaoUguiFactory.CreateText(name, parent, x, y, w, h, value, size,
                light ? QdaoRefreshArt.Ivory : QdaoRefreshArt.Ink, alignment);

        private Button ArtButton(string name, UnityEngine.Transform parent, float x, float y,
            float w, float h, string label, string asset, out TMP_Text text)
        {
            var button = QdaoRefreshArt.Button(name, parent, x, y, w, h, asset, out _);
            float sourceHeight = QdaoRefreshArt.Load(asset).rect.height;
            float padding = Mathf.Min(78f * h / sourceHeight, w * 0.18f);
            text = Label(name + "Text", button.transform, padding, 0f, w - padding * 2f, h, label,
                30f, asset.StartsWith("primary"), TextAlignmentOptions.Center);
            return button;
        }

        private void BuildRefreshVisualTree()
        {
            _screenArtSprite = Resources.Load<Sprite>(QdaoRefreshArt.Root + "login_background");
            if (_screenArtSprite == null) _screenArtSprite = QdaoRefreshArt.Load("sanctuary_background");
            _statusDotSprite = QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath);
            _credentialSprite = QdaoRefreshArt.Load("main_frame");
            _credentialCancelSprite = QdaoRefreshArt.Load("tab_normal");
            _credentialSubmitSprite = QdaoRefreshArt.Load("primary_button_normal");
            var backdrop = QdaoUguiFactory.CreateStretch("LetterboxBackdrop", transform, Vector4.zero);
            _backdropImage = backdrop.gameObject.AddComponent<Image>();
            _backdropImage.color = QdaoUguiTheme.Letterbox;
            _backdropImage.raycastTarget = false;
            _contentRoot = QdaoUguiFactory.CreateCenteredRect("ContentRoot", transform, 2560f, 1080f);
            _screenArtImage = QdaoUguiFactory.CreateImage("ScreenArtwork", _contentRoot, 0f, 0f, 2560f, 1080f, _screenArtSprite);
            _serverRoot = QdaoUguiFactory.CreateRect("ServerPage", _contentRoot, 0f, 0f, 2560f, 1080f);
            BuildServerPage();
            _landingRoot = QdaoUguiFactory.CreateRect("LoginPage", _contentRoot, 0f, 0f, 2560f, 1080f);
            BuildLandingPage();
            BuildRefreshCredentialPanel();
            ShowLanding(true);
        }

        private void BuildLandingPage()
        {
            var title = Resources.Load<Sprite>(QdaoRefreshArt.Root + "title_logo");
            if (title != null)
                QdaoUguiFactory.CreateImage("GameTitleArtwork", _landingRoot, 150f, 62f, 960f, 485f, title).preserveAspect = true;
            else
            {
                QdaoRefreshArt.Panel("GameTitlePlaque", _landingRoot, 160f, 240f, 880f, 210f, "primary_button_normal");
                Label("GameTitle", _landingRoot, 200f, 255f, 800f, 170f, "五行奇谈", 140f, true, TextAlignmentOptions.Center);
            }
            QdaoUguiFactory.CreateImage("Hero", _landingRoot, 1250f, 90f, 860f, 910f,
                QdaoRefreshArt.Load("hero")).preserveAspect = true;
            _landingServers = ArtButton("SelectServer", _landingRoot, 230f, 548f, 810f, 100f,
                "选择服务器", "list_row_normal", out _landingServerText);
            _landingServerText.rectTransform.anchoredPosition = new Vector2(62f, 0f);
            _landingServerText.rectTransform.sizeDelta = new Vector2(300f, 100f);
            _landingServerText.alignment = TextAlignmentOptions.MidlineLeft;
            _landingServerText.fontSize = 34f;
            _landingServerDot = QdaoUguiFactory.CreateImage("CurrentServerDot", _landingServers.transform,
                367f, 40f, 20f, 20f, _statusDotSprite);
            _landingAvailability = Label("CurrentServerAvailability", _landingServers.transform,
                403f, 0f, 150f, 100f, string.Empty, 30f);
            var changePlate = QdaoRefreshArt.Panel("ChooseServerAction", _landingServers.transform,
                573f, 13f, 211f, 74f, "primary_button_normal");
            Label("ChooseServerActionText", changePlate.transform, 20f, 0f, 171f, 74f,
                "选择服务器", 25f, true, TextAlignmentOptions.Center);
            _landingEnter = ArtButton("EnterGame", _landingRoot, 270f, 668f, 740f, 166f,
                "进入游戏", "primary_button_normal", out var enterLabel);
            enterLabel.fontSize = 76f;
            _landingAccount = ArtButton("SwitchAccount", _landingRoot, 467f, 856f, 340f, 80f,
                "切换账号", "tab_normal", out var accountLabel);
            accountLabel.fontSize = 35f;
            _landingStatusText = Label("LoginStatus", _landingRoot, 185f, 965f, 860f, 48f,
                string.Empty, 27f, false, TextAlignmentOptions.Center);
        }

        private void BuildServerPage()
        {
            QdaoRefreshArt.Panel("ServerFrame", _serverRoot, 380f, 126f, 1860f, 920f, "main_frame");
            QdaoRefreshArt.Panel("ServerTitlePlate", _serverRoot, 958f, 82f, 702f, 142f, "primary_button_normal");
            QdaoUguiFactory.CreateImage("ServerTitleEmblem", _serverRoot, 1252f, 8f, 108f, 108f,
                QdaoRefreshArt.Load("round_badge_taiji")).preserveAspect = true;
            Label("ServerTitle", _serverRoot, 1030f, 105f, 555f, 94f, "选择服务器", 65f, true, TextAlignmentOptions.Center);

            _searchInput = QdaoUguiFactory.CreateInputField("SearchInput", _serverRoot,
                1762f, 149f, 384f, 74f, "搜索服务器", 64, QdaoRefreshArt.Load("search_normal"));
            QdaoRefreshArt.Skin(_searchInput.GetComponent<Image>(), "search_normal");
            _searchInput.textComponent.fontSize = 25f;
            ((TMP_Text)_searchInput.placeholder).fontSize = 25f;
            SetInputInsets(_searchInput, 70f);

            _topButtons = new Button[3]; _topImages = new Image[3];
            _topLabels = new TMP_Text[3]; _topSelectionMarks = new Image[3];
            for (var i = 0; i < 3; i++)
            {
                float x = 840f + i * 302f;
                _topButtons[i] = QdaoRefreshArt.Button("TopTab_" + i, _serverRoot, x, 190f,
                    292f, 82f, "tab_normal", out _topImages[i]);
                _topLabels[i] = Label("TopTabText_" + i, _topButtons[i].transform, 18f, 0f,
                    256f, 82f, TopTabLabels[i], 29f, false, TextAlignmentOptions.Center);
                _topSelectionMarks[i] = QdaoUguiFactory.CreateImage("TopTabSelection_" + i,
                    _topButtons[i].transform, 29f, 35f, 15f, 15f, QdaoRefreshArt.Load("check"));
                _topSelectionMarks[i].enabled = false;
                // These legacy shortcuts are the same filters as the left rail.
                // Keep their serialized controls for compatibility; expose each filter once.
                _topButtons[i].gameObject.SetActive(false);
            }
            _topDefaultDimmer = QdaoUguiFactory.CreateImage("TopDefaultDimmer", _serverRoot, 0f, 0f, 1f, 1f, null);
            _topDefaultDimmer.enabled = false;
            _categoryButtons = new Button[6]; _categoryImages = new Image[6];
            _categoryTexts = new TMP_Text[6]; _categorySelectionMarks = new Image[6];
            for (var i = 0; i < 6; i++)
            {
                bool auxiliary = i >= 4;
                float width = auxiliary ? 152f : 320f;
                float height = auxiliary ? 76f : 112f;
                float x = auxiliary ? 490f + (i - 4) * 164f : 484f;
                float y = auxiliary ? 816f : 246f + i * 138f;
                string[] labels = { "最近登录", "推荐服务器", "全部区服", "新服推荐", "流畅", "维护" };
                _categoryButtons[i] = QdaoRefreshArt.Button("CategoryArt_" + i, _serverRoot,
                    x, y, width, height, "list_row_normal", out _categoryImages[i]);
                _categoryTexts[i] = Label("CategoryText_" + i, _categoryButtons[i].transform,
                    28f, 0f, width - 56f, height, labels[i], auxiliary ? 25f : 34f,
                    false, TextAlignmentOptions.Center);
                _categorySelectionMarks[i] = QdaoUguiFactory.CreateImage("CategorySelection_" + i,
                    _categoryButtons[i].transform, 12f, height * .5f - 8f, 16f, 16f, QdaoRefreshArt.Load("check"));
                _categorySelectionMarks[i].enabled = false;
            }
            _categoryDefaultDimmer = QdaoUguiFactory.CreateImage("CategoryDefaultDimmer", _serverRoot, 0f, 0f, 1f, 1f, null);
            _categoryDefaultDimmer.enabled = false;
            _serverButtons = new Button[PageSize]; _serverCardImages = new Image[PageSize];
            _serverNames = new TMP_Text[PageSize]; _serverSubtitles = new TMP_Text[PageSize];
            _serverDots = new Image[PageSize]; _serverEmptyCovers = new Image[PageSize];
            _serverBadges = new Image[PageSize];
            _serverChecks = new Image[PageSize];
            _serverRecommendations = new Image[PageSize];
            string[] emblems = { "pagoda", "pagoda", "mountain", "pagoda", "lotus", "water", "peach_spirit", "lotus" };
            for (var i = 0; i < PageSize; i++)
            {
                float x = 830f + (i % 2) * 678f, y = 244f + (i / 2) * 159f;
                _serverButtons[i] = QdaoRefreshArt.Button("ServerCardArt_" + i, _serverRoot,
                    x, y, 650f, 148f, "server_card_wide_normal", out _serverCardImages[i]);
                var parent = _serverButtons[i].transform;
                _serverBadges[i] = QdaoUguiFactory.CreateImage("ServerEmblem_" + i, parent,
                    38f, 24f, 102f, 102f, QdaoRefreshArt.Load("round_badge_" + emblems[i]));
                _serverNames[i] = Label("ServerName_" + i, parent, 164f, 24f, 277f, 74f, "", 40f);
                _serverSubtitles[i] = Label("ServerStatus_" + i, parent, 477f, 32f, 108f, 60f, "", 29f);
                _serverDots[i] = QdaoUguiFactory.CreateImage("ServerDot_" + i, parent, 443f, 54f, 23f, 23f, _statusDotSprite);
                _serverChecks[i] = QdaoUguiFactory.CreateImage("ServerSelected_" + i, parent,
                    588f, 51f, 38f, 38f, QdaoRefreshArt.Load("check"));
                _serverRecommendations[i] = QdaoUguiFactory.CreateImage("ServerRecommended_" + i, parent,
                    8f, 4f, 97f, 46f, QdaoRefreshArt.Load("recommend_badge"));
                _serverEmptyCovers[i] = QdaoUguiFactory.CreateImage("ServerEmptyCover_" + i, parent, 0f, 0f, 1f, 1f, null);
                _serverEmptyCovers[i].enabled = false;
            }
            _emptyListText = Label("EmptyServers", _serverRoot, 880f, 440f, 1220f, 200f,
                "没有匹配的服务器\n请修改搜索或切换分类", 35f, false, TextAlignmentOptions.Center);
            _emptyListText.textWrappingMode = TextWrappingModes.Normal;
            _prevPageButton = ArtButton("PrevPage", _serverRoot, 830f, 881f, 104f, 62f,
                "〈", "tab_normal", out _prevPageText);
            _nextPageButton = ArtButton("NextPage", _serverRoot, 944f, 881f, 104f, 62f,
                "〉", "tab_normal", out _nextPageText);
            _pageText = Label("PageText", _serverRoot, 1052f, 888f, 118f, 40f, "", 26f, false, TextAlignmentOptions.Center);
            _backButton = ArtButton("BackButton", _serverRoot, 515f, 908f, 300f, 103f,
                "返回", "tab_normal", out _);
            _lastLoginText = Label("LastLoginText", _serverRoot, 850f, 985f, 570f, 35f, "", 24f);
            _selectedText = Label("SelectedServerText", _serverRoot, 1005f, 936f, 625f, 54f, "", 35f);
            _statusText = Label("StatusText", _serverRoot, 855f, 1025f, 1260f, 35f, "", 23f);
            _refreshButton = ArtButton("RefreshButton", _serverRoot, 1984f, 875f, 170f, 64f,
                "刷新", "tab_normal", out _refreshText);
            _enterButton = ArtButton("EnterButton", _serverRoot, 1670f, 927f, 495f, 115f,
                "进入选角", "primary_button_normal", out _enterText);
            _enterText.fontSize = 48f;
        }

        private void BuildRefreshCredentialPanel()
        {
            _credentialPanel = QdaoUguiFactory.CreateRect("CredentialPanel", _contentRoot, 0f, 0f, 2560f, 1080f);
            _credentialBlockerImage = QdaoUguiFactory.CreateImage("CredentialBlocker", _credentialPanel, 0f, 0f, 2560f, 1080f, null, true);
            _credentialBlockerImage.color = new Color(0.025f, 0.07f, 0.06f, 0.78f);
            _credentialImage = QdaoRefreshArt.Panel("CredentialArt", _credentialPanel, 690f, 235f, 1180f, 650f, "main_frame");
            _credentialImage.raycastTarget = true;
            _credentialTitleText = Label("CredentialTitle", _credentialPanel, 950f, 283f, 660f, 78f,
                "账号登录", 50f, false, TextAlignmentOptions.Center);
            Label("AccountLabel", _credentialPanel, 835f, 405f, 150f, 78f, "账号", 32f);
            Label("PasswordLabel", _credentialPanel, 835f, 518f, 150f, 78f, "密码", 32f);
            _accountInput = QdaoUguiFactory.CreateInputField("AccountInput", _credentialPanel, 990f, 405f,
                700f, 78f, "请输入账号", 191, QdaoRefreshArt.Load("search_normal"));
            _passwordInput = QdaoUguiFactory.CreateInputField("PasswordInput", _credentialPanel, 990f, 518f,
                700f, 78f, "请输入密码", 1024, QdaoRefreshArt.Load("search_normal"));
            foreach (var input in new[] { _accountInput, _passwordInput })
            {
                QdaoRefreshArt.Skin(input.GetComponent<Image>(), "search_normal");
                input.textComponent.fontSize = 30f;
                ((TMP_Text)input.placeholder).fontSize = 28f;
                SetInputInsets(input, 78f);
            }
            _passwordInput.contentType = TMP_InputField.ContentType.Password;
            _credentialCancelButton = ArtButton("CredentialCancelButton", _credentialPanel, 930f, 660f,
                310f, 96f, "取消", "tab_normal", out _credentialCancelText);
            _credentialSubmitButton = ArtButton("CredentialSubmitButton", _credentialPanel, 1320f, 660f,
                310f, 96f, "登录", "primary_button_normal", out _credentialSubmitText);
            _credentialStatusText = Label("CredentialStatus", _credentialPanel, 790f, 778f, 980f, 56f,
                "", 27f, false, TextAlignmentOptions.Center);
            _credentialPanel.gameObject.SetActive(false);
        }

        private static string CompactAvailability(ServerListZone zone)
        {
            if (zone == null) return string.Empty;
            if (zone.status == "MAINTENANCE") return "维护";
            if (zone.status == "CLOSED") return "关闭";
            if (zone.status == "PREVIEW") return "未开放";
            return zone.load_level == "FULL" ? "爆满" : zone.load_level == "BUSY" ? "繁忙"
                : zone.load_level == "SMOOTH" ? "流畅" : "在线";
        }

        private void RefreshLandingServer(ServerListZone zone)
        {
            _landingServerText.text = zone == null ? "选择服务器" : CardDisplayName(zone);
            if (_landingAvailability != null)
                _landingAvailability.text = CompactAvailability(zone);
            if (_landingServerDot != null)
            {
                _landingServerDot.enabled = zone != null;
                if (zone != null) _landingServerDot.color = StatusDotColor(zone);
            }
        }

        private void RefreshCardMarkers(int slot, ServerListZone zone, bool selected, bool closed)
        {
            if (_serverChecks != null && slot < _serverChecks.Length && _serverChecks[slot] != null)
                _serverChecks[slot].enabled = selected && !closed;
            if (_serverRecommendations != null && slot < _serverRecommendations.Length && _serverRecommendations[slot] != null)
                _serverRecommendations[slot].enabled = zone.recommended;
            // Availability has its own reserved column; selection and recommendation
            // are independent shapes rather than appending text into the status label.
            _serverSubtitles[slot].text = CompactAvailability(zone);
            if (closed)
            {
                _serverBadges[slot].sprite = QdaoRefreshArt.Load("lock");
                _serverBadges[slot].enabled = true;
            }
            else
            {
                string[] emblems = { "pagoda", "pagoda", "mountain", "pagoda", "lotus", "water", "peach_spirit", "lotus" };
                _serverBadges[slot].sprite = QdaoRefreshArt.Load("round_badge_" + emblems[slot % emblems.Length]);
            }
        }

        private static void SetInputInsets(TMP_InputField input, float height)
        {
            float scale = height / 84f;
            input.textViewport.offsetMin = new Vector2(70f * scale, 12f * scale);
            input.textViewport.offsetMax = new Vector2(-64f * scale, -12f * scale);
        }

        public void ShowLanding(bool show)
        {
            if (_landingRoot != null) _landingRoot.gameObject.SetActive(show);
            if (_serverRoot != null) _serverRoot.gameObject.SetActive(!show);
        }

        private void BindRefreshEvents()
        {
            _landingServers.onClick.AddListener(OnLandingServers);
            _landingEnter.onClick.AddListener(OnLandingEnter);
            _landingAccount.onClick.AddListener(OnLandingAccount);
        }

        private void UnbindRefreshEvents()
        {
            _landingServers.onClick.RemoveListener(OnLandingServers);
            _landingEnter.onClick.RemoveListener(OnLandingEnter);
            _landingAccount.onClick.RemoveListener(OnLandingAccount);
        }

        private void OnLandingServers() { if (!_busy) ShowLanding(false); }
        private void OnLandingAccount() { if (!_busy) { ShowCredentialPanel(true, true); SetStatus("请输入账号和密码"); } }
        private void OnLandingEnter()
        {
            if (_busy) return;
            if (FindRenderedZone(_selectedZoneId) == null) { ShowLanding(false); SetStatus("请先选择区服"); return; }
            OnEnterClicked();
        }
    }
}
