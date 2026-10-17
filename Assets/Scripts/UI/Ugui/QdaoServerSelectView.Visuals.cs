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
        {
            var label = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, value, size,
                light ? QdaoCoreFidelityArt.Ivory : QdaoCoreFidelityArt.Ink, alignment);
            QdaoCoreFidelityArt.ApplyText(label);
            return label;
        }

        private Button ArtButton(string name, UnityEngine.Transform parent, float x, float y,
            float w, float h, string label, string asset, out TMP_Text text)
        {
            var button = QdaoCoreFidelityArt.Button(name, parent, x, y, w, h, asset, out _);
            float sourceHeight = QdaoCoreFidelityArt.Load(asset).rect.height;
            float padding = Mathf.Min(78f * h / sourceHeight, w * 0.18f);
            text = Label(name + "Text", button.transform, padding, 0f, w - padding * 2f, h, label,
                32f, asset.StartsWith("primary"), TextAlignmentOptions.Center);
            QdaoCoreFidelityArt.ApplyText(text);
            return button;
        }

        private void BuildRefreshVisualTree()
        {
            _screenArtSprite = QdaoCoreFidelityArt.Load("login_shell");
            _statusDotSprite = QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath);
            _credentialSprite = QdaoCoreFidelityArt.Load("main_frame");
            _credentialCancelSprite = QdaoCoreFidelityArt.Load("tab_normal");
            _credentialSubmitSprite = QdaoCoreFidelityArt.Load("primary_button_normal");
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
            // Immutable original logo, boy and landscape are in the cleaned shell;
            // real server data and every action remain separate native controls.
            _landingServers = ArtButton("SelectServer", _landingRoot, 230f, 548f, 810f, 100f,
                "选择服务器", "login_server_bar", out _landingServerText);
            _landingServerText.rectTransform.anchoredPosition = new Vector2(62f,0);
            _landingServerText.rectTransform.sizeDelta = new Vector2(300,100);
            _landingServerText.alignment=TextAlignmentOptions.MidlineLeft;_landingServerText.fontSize=38;
            _landingServerDot=QdaoUguiFactory.CreateImage("CurrentServerDot",_landingServers.transform,367,40,20,20,_statusDotSprite);
            _landingAvailability=Label("CurrentServerAvailability",_landingServers.transform,403,0,150,100,"",34);
            var choose=QdaoCoreFidelityArt.Panel("ChooseServerAction",_landingServers.transform,573,13,211,74,"login_choose");
            Label("ChooseServerActionText",choose.transform,17,0,177,74,"选择服务器",28,true,TextAlignmentOptions.Center);
            _landingEnter=ArtButton("EnterGame",_landingRoot,272,676,733,167,"进入游戏","login_enter",out var enterLabel);
            enterLabel.fontSize=80;QdaoCoreFidelityArt.ApplyText(enterLabel);
            _landingAccount=QdaoUguiFactory.CreateHitButton("SwitchAccount",_landingRoot,467,856,340,80);
            _landingAccount.navigation=new Navigation{mode=Navigation.Mode.Automatic};
            Label("SwitchAccountText",_landingAccount.transform,0,0,340,80,"切换账号",39,false,TextAlignmentOptions.Center);
            _landingStatusText=Label("LoginStatus",_landingRoot,185,965,860,48,"",30,false,TextAlignmentOptions.Center);
        }
        private void BuildServerPage()
        {
            var serverTitle = Label("ServerTitle", _serverRoot, 397,236,1040,65,"选择服务器",44,true);
            _searchInput = QdaoUguiFactory.CreateInputField("SearchInput", _serverRoot,
                368.36f, 348.94f, 337.89f, 66.34f, "搜索服务器", 64, QdaoCoreFidelityArt.Load("search_normal"));
            QdaoCoreFidelityArt.Skin(_searchInput.GetComponent<Image>(), "search_normal");
            _searchInput.textComponent.fontSize = 25f;
            ((TMP_Text)_searchInput.placeholder).fontSize = 25f;
            QdaoCoreFidelityArt.ApplyText(_searchInput.textComponent);
            QdaoCoreFidelityArt.ApplyText((TMP_Text)_searchInput.placeholder);
            SetInputInsets(_searchInput, 70f);

            _topButtons = new Button[3]; _topImages = new Image[3];
            _topLabels = new TMP_Text[3]; _topSelectionMarks = new Image[3];
            for (var i = 0; i < 3; i++)
            {
                float x = 840f + i * 302f;
                _topButtons[i] = QdaoCoreFidelityArt.Button("TopTab_" + i, _serverRoot, x, 190f,
                    292f, 82f, "tab_normal", out _topImages[i]);
                _topLabels[i] = Label("TopTabText_" + i, _topButtons[i].transform, 18f, 0f,
                    256f, 82f, TopTabLabels[i], 29f, false, TextAlignmentOptions.Center);
                QdaoCoreFidelityArt.ApplyText(_topLabels[i]);
                _topSelectionMarks[i] = QdaoUguiFactory.CreateImage("TopTabSelection_" + i,
                    _topButtons[i].transform, 29f, 35f, 15f, 15f, QdaoCoreFidelityArt.Load("check"));
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
                float width = auxiliary ? 160f : 337.89f;
                float height = auxiliary ? 50f : 70f;
                float x = auxiliary ? 368.36f + (i - 4) * 174f : 368.36f;
                float y = auxiliary ? 842f : new float[]{339,418,495,574}[i] * 1080f / 814f;
                string[] labels = { "最近登录", "推荐服务器", "全部区服", "新服推荐", "流畅", "维护" };
                _categoryButtons[i] = QdaoCoreFidelityArt.Button("CategoryArt_" + i, _serverRoot,
                    x, y, width, height, "list_row_normal", out _categoryImages[i]);
                _categoryTexts[i] = Label("CategoryText_" + i, _categoryButtons[i].transform,
                    28f, 0f, width - 56f, height, labels[i], auxiliary ? 23f : 31f,
                    false, TextAlignmentOptions.Center);
                QdaoCoreFidelityArt.ApplyText(_categoryTexts[i]);
                _categorySelectionMarks[i] = QdaoUguiFactory.CreateImage("CategorySelection_" + i,
                    _categoryButtons[i].transform, 12f, height * .5f - 8f, 16f, 16f, QdaoCoreFidelityArt.Load("check"));
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
                float x = (i % 2 == 0 ? 576f : 1112f) * 2560f / 1932f, y = (265f + (i / 2) * 98f) * 1080f / 814f;
                _serverButtons[i] = QdaoCoreFidelityArt.Button("ServerCardArt_" + i, _serverRoot,
                    x, y, (i % 2 == 0 ? 487f : 485f) * 2560f / 1932f, 80f * 1080f / 814f, "server_card_wide_normal", out _serverCardImages[i]);
                var parent = _serverButtons[i].transform;
                _serverBadges[i] = QdaoUguiFactory.CreateImage("ServerEmblem_" + i, parent,
                    532f, 6f, 94f, 94f, QdaoCoreFidelityArt.Load("round_badge_" + emblems[i]));
                _serverNames[i] = Label("ServerName_" + i, parent, 56f, 7f, 405f, 51f, "", 38f);
                _serverSubtitles[i] = Label("ServerStatus_" + i, parent, 84f, 61f, 160f, 33f, "", 26f);
                _serverDots[i] = QdaoUguiFactory.CreateImage("ServerDot_" + i, parent, 57f, 70f, 17f, 17f, _statusDotSprite);
                _serverChecks[i] = QdaoUguiFactory.CreateImage("ServerSelected_" + i, parent,
                    12f, 46f, 26f, 26f, QdaoCoreFidelityArt.Load("check"));
                _serverRecommendations[i] = QdaoUguiFactory.CreateImage("ServerRecommended_" + i, parent,
                    439f, 6f, 86f, 34f, QdaoCoreFidelityArt.Load("recommend_badge"));
                Label("ServerRecommendedText", _serverRecommendations[i].transform, 0f, 0f, 86f, 34f,
                    "推荐", 18f, false, TextAlignmentOptions.Center).color = QdaoUguiTheme.Html("#795725");
                _serverEmptyCovers[i] = QdaoUguiFactory.CreateImage("ServerEmptyCover_" + i, parent, 0f, 0f, 1f, 1f, null);
                _serverEmptyCovers[i].enabled = false;
            }
            _emptyListText = Label("EmptyServers", _serverRoot, 830f, 460f, 1220f, 200f,
                "没有匹配的服务器\n请修改搜索或切换分类", 35f, false, TextAlignmentOptions.Center);
            _emptyListText.textWrappingMode = TextWrappingModes.Normal;
            _prevPageButton = ArtButton("PrevPage", _serverRoot, 768f, 850f, 72f, 40f,
                "〈", "tab_normal", out _prevPageText);
            _nextPageButton = ArtButton("NextPage", _serverRoot, 848f, 850f, 72f, 40f,
                "〉", "tab_normal", out _nextPageText);
            foreach (var text in new[] { _prevPageText, _nextPageText })
            {
                text.fontSize = 24f;
                text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 40f);
            }
            _pageText = Label("PageText", _serverRoot, 930f, 850f, 160f, 40f, "", 26f, false, TextAlignmentOptions.Center);
            _backButton = ArtButton("BackButton", _serverRoot, 1630f, 900f, 195f, 69f,
                "返回", "tab_normal", out _);
            _lastLoginText = Label("LastLoginText", _serverRoot, 1110f, 854f, 570f, 35f, "", 24f);
            _selectedText = Label("SelectedServerText", _serverRoot, 395f, 911f, 1100f, 45f, "", 35f);
            _statusText = Label("StatusText", _serverRoot, 395f, 1002f, 1750f, 35f, "", 23f);
            _refreshButton = ArtButton("RefreshButton", _serverRoot, 1980f, 850f, 170f, 42f,
                "刷新", "tab_normal", out _refreshText);
            _refreshText.fontSize = 24f;
            _refreshText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 42f);
            _enterButton = ArtButton("EnterButton", _serverRoot, 1850f, 897f, 313f, 74f,
                "进入选角", "primary_button_normal", out _enterText);
            _enterText.fontSize = 38f;
        }

        private void BuildRefreshCredentialPanel()
        {
            _credentialPanel = QdaoUguiFactory.CreateRect("CredentialPanel", _contentRoot, 0f, 0f, 2560f, 1080f);
            _credentialBlockerImage = QdaoUguiFactory.CreateImage("CredentialBlocker", _credentialPanel, 0f, 0f, 2560f, 1080f, null, true);
            _credentialBlockerImage.color = new Color(0.025f, 0.07f, 0.06f, 0.78f);
            _credentialImage = QdaoCoreFidelityArt.Panel("CredentialArt", _credentialPanel, 690f, 235f, 1180f, 650f, "main_frame");
            _credentialImage.raycastTarget = true;
            _credentialTitleText = Label("CredentialTitle", _credentialPanel, 950f, 283f, 660f, 78f,
                "账号登录", 50f, false, TextAlignmentOptions.Center);
            QdaoCoreFidelityArt.ApplyText(_credentialTitleText);
            Label("AccountLabel", _credentialPanel, 835f, 405f, 150f, 78f, "账号", 34f);
            Label("PasswordLabel", _credentialPanel, 835f, 518f, 150f, 78f, "密码", 34f);
            _accountInput = QdaoUguiFactory.CreateInputField("AccountInput", _credentialPanel, 990f, 405f,
                700f, 78f, "请输入账号", 191, QdaoCoreFidelityArt.Load("clean_search"));
            _passwordInput = QdaoUguiFactory.CreateInputField("PasswordInput", _credentialPanel, 990f, 518f,
                700f, 78f, "请输入密码", 1024, QdaoCoreFidelityArt.Load("clean_search"));
            foreach (var input in new[] { _accountInput, _passwordInput })
            {
                QdaoCoreFidelityArt.Skin(input.GetComponent<Image>(), "clean_search");
                input.textComponent.fontSize = 32f;
                ((TMP_Text)input.placeholder).fontSize = 32f;
                QdaoCoreFidelityArt.ApplyText(input.textComponent);
                QdaoCoreFidelityArt.ApplyText((TMP_Text)input.placeholder);
                SetInputInsets(input, 78f);
            }
            _passwordInput.contentType = TMP_InputField.ContentType.Password;
            _credentialCancelButton = ArtButton("CredentialCancelButton", _credentialPanel, 930f, 660f,
                310f, 96f, "取消", "clean_tab", out _credentialCancelText);
            _credentialSubmitButton = ArtButton("CredentialSubmitButton", _credentialPanel, 1320f, 660f,
                310f, 96f, "登录", "clean_primary", out _credentialSubmitText);
            _credentialCancelText.fontSize = 34f;
            _credentialSubmitText.fontSize = 34f;
            _credentialSubmitText.color = QdaoCoreFidelityArt.Ivory;
            _credentialStatusText = Label("CredentialStatus", _credentialPanel, 790f, 778f, 980f, 56f,
                "", 30f, false, TextAlignmentOptions.Center);
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
            {
                _serverRecommendations[slot].enabled = zone.recommended;
                var caption = _serverRecommendations[slot].GetComponentInChildren<TMP_Text>(true);
                if (caption != null) caption.gameObject.SetActive(zone.recommended);
            }
            // Availability has its own reserved column; selection and recommendation
            // are independent shapes rather than appending text into the status label.
            _serverSubtitles[slot].text = CompactAvailability(zone);
            if (closed)
            {
                _serverBadges[slot].sprite = QdaoCoreFidelityArt.Load("lock");
                _serverBadges[slot].enabled = true;
            }
            else
            {
                string[] emblems = { "pagoda", "pagoda", "mountain", "pagoda", "lotus", "water", "peach_spirit", "lotus" };
                _serverBadges[slot].sprite = QdaoCoreFidelityArt.Load("round_badge_" + emblems[slot % emblems.Length]);
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
            if (_screenArtImage != null) _screenArtImage.sprite = QdaoCoreFidelityArt.Load(show ? "login_shell" : "server_shell");
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
