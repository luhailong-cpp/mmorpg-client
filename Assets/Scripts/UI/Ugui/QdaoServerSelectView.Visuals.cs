using TMPro;
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
            float padding = 78f * h / sourceHeight;
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
                QdaoUguiFactory.CreateImage("GameTitleArtwork", _landingRoot, 110f, 70f, 1000f, 475f, title).preserveAspect = true;
            else
            {
                QdaoRefreshArt.Panel("GameTitlePlaque", _landingRoot, 160f, 240f, 880f, 210f, "primary_button_normal");
                Label("GameTitle", _landingRoot, 200f, 255f, 800f, 170f, "五行奇谈", 140f, true, TextAlignmentOptions.Center);
            }
            QdaoUguiFactory.CreateImage("Hero", _landingRoot, 1490f, 90f, 860f, 910f,
                QdaoRefreshArt.Load("hero")).preserveAspect = true;
            QdaoUguiFactory.CreateImage("NineTailedFox", _landingRoot, 1000f, 555f, 640f, 480f,
                QdaoRefreshArt.Load("companion_fox")).preserveAspect = true;
            _landingServers = ArtButton("SelectServer", _landingRoot, 235f, 555f, 730f, 96f,
                "选择服务器", "list_row_normal", out _landingServerText);
            _landingEnter = ArtButton("EnterGame", _landingRoot, 235f, 686f, 730f, 132f,
                "进入游戏", "primary_button_normal", out var enterLabel);
            enterLabel.fontSize = 55f;
            _landingAccount = ArtButton("SwitchAccount", _landingRoot, 380f, 846f, 430f, 86f,
                "切换账号", "tab_normal", out _);
            _landingStatusText = Label("LoginStatus", _landingRoot, 185f, 965f, 860f, 48f,
                string.Empty, 27f, false, TextAlignmentOptions.Center);
        }

        private void BuildServerPage()
        {
            QdaoRefreshArt.Panel("ServerFrame", _serverRoot, 325f, 80f, 1950f, 970f, "main_frame");
            QdaoRefreshArt.Panel("ServerTitlePlate", _serverRoot, 920f, 72f, 780f, 126f, "primary_button_normal");
            Label("ServerTitle", _serverRoot, 1010f, 83f, 600f, 94f, "选择服务器", 62f, true, TextAlignmentOptions.Center);
            QdaoUguiFactory.CreateImage("ServerFox", _serverRoot, -20f, 590f, 535f, 465f,
                QdaoRefreshArt.Load("companion_fox")).preserveAspect = true;

            _searchInput = QdaoUguiFactory.CreateInputField("SearchInput", _serverRoot,
                1770f, 191f, 380f, 70f, "搜索服务器", 64, QdaoRefreshArt.Load("search_normal"));
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
            }
            _topDefaultDimmer = QdaoUguiFactory.CreateImage("TopDefaultDimmer", _serverRoot, 0f, 0f, 1f, 1f, null);
            _topDefaultDimmer.enabled = false;
            _categoryButtons = new Button[6]; _categoryImages = new Image[6];
            _categoryTexts = new TMP_Text[6]; _categorySelectionMarks = new Image[6];
            for (var i = 0; i < 6; i++)
            {
                _categoryButtons[i] = QdaoRefreshArt.Button("CategoryArt_" + i, _serverRoot,
                    445f, 292f + i * 91f, 325f, 83f, "list_row_normal", out _categoryImages[i]);
                _categoryTexts[i] = Label("CategoryText_" + i, _categoryButtons[i].transform,
                    26f, 0f, 270f, 83f, CategoryLabels[i], 31f, false, TextAlignmentOptions.Center);
                _categorySelectionMarks[i] = QdaoUguiFactory.CreateImage("CategorySelection_" + i,
                    _categoryButtons[i].transform, 33f, 34f, 17f, 17f, QdaoRefreshArt.Load("check"));
                _categorySelectionMarks[i].enabled = false;
            }
            _categoryDefaultDimmer = QdaoUguiFactory.CreateImage("CategoryDefaultDimmer", _serverRoot, 0f, 0f, 1f, 1f, null);
            _categoryDefaultDimmer.enabled = false;
            _serverButtons = new Button[PageSize]; _serverCardImages = new Image[PageSize];
            _serverNames = new TMP_Text[PageSize]; _serverSubtitles = new TMP_Text[PageSize];
            _serverDots = new Image[PageSize]; _serverEmptyCovers = new Image[PageSize];
            _serverBadges = new Image[PageSize];
            string[] emblems = { "pagoda", "pagoda", "mountain", "pagoda", "lotus", "water", "peach_spirit", "lotus" };
            for (var i = 0; i < PageSize; i++)
            {
                float x = 840f + (i % 2) * 665f, y = 288f + (i / 2) * 143f;
                _serverButtons[i] = QdaoRefreshArt.Button("ServerCardArt_" + i, _serverRoot,
                    x, y, 650f, 130f, "server_card_wide_normal", out _serverCardImages[i]);
                var parent = _serverButtons[i].transform;
                _serverBadges[i] = QdaoUguiFactory.CreateImage("ServerEmblem_" + i, parent,
                    25f, 27f, 74f, 74f, QdaoRefreshArt.Load("round_badge_" + emblems[i]));
                _serverNames[i] = Label("ServerName_" + i, parent, 116f, 19f, 330f, 52f, "", 38f);
                _serverSubtitles[i] = Label("ServerStatus_" + i, parent, 116f, 73f, 370f, 35f, "", 24f);
                _serverDots[i] = QdaoUguiFactory.CreateImage("ServerDot_" + i, parent, 494f, 52f, 23f, 23f, _statusDotSprite);
                _serverEmptyCovers[i] = QdaoUguiFactory.CreateImage("ServerEmptyCover_" + i, parent, 0f, 0f, 1f, 1f, null);
                _serverEmptyCovers[i].enabled = false;
            }
            _emptyListText = Label("EmptyServers", _serverRoot, 880f, 440f, 1220f, 200f,
                "没有匹配的服务器\n请修改搜索或切换分类", 35f, false, TextAlignmentOptions.Center);
            _emptyListText.textWrappingMode = TextWrappingModes.Normal;
            _prevPageButton = ArtButton("PrevPage", _serverRoot, 810f, 862f, 130f, 69f,
                "〈", "tab_normal", out _prevPageText);
            _nextPageButton = ArtButton("NextPage", _serverRoot, 2025f, 862f, 130f, 69f,
                "〉", "tab_normal", out _nextPageText);
            _pageText = Label("PageText", _serverRoot, 1400f, 855f, 150f, 48f, "", 26f, false, TextAlignmentOptions.Center);
            _backButton = ArtButton("BackButton", _serverRoot, 450f, 883f, 310f, 93f,
                "返回", "tab_normal", out _);
            _lastLoginText = Label("LastLoginText", _serverRoot, 855f, 937f, 490f, 37f, "", 24f);
            _selectedText = Label("SelectedServerText", _serverRoot, 855f, 890f, 720f, 44f, "", 31f);
            _statusText = Label("StatusText", _serverRoot, 855f, 984f, 1260f, 35f, "", 23f);
            _refreshButton = ArtButton("RefreshButton", _serverRoot, 1535f, 900f, 190f, 79f,
                "刷新", "tab_normal", out _refreshText);
            _enterButton = ArtButton("EnterButton", _serverRoot, 1755f, 885f, 410f, 108f,
                "进入选角", "primary_button_normal", out _enterText);
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
