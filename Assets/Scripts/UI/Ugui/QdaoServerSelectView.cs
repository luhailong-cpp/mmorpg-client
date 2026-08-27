using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using MmorpgClient.Core;
using MmorpgClient.Game;
using MmorpgClient.Net;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>
    /// Combined login + server-select screen rebuilt with native uGUI controls.
    /// Immutable decoration is rendered as one optimized screen-art layer;
    /// inputs, buttons, localized text, status lights and credential UI remain
    /// independent semantic objects in the baked production prefab.
    ///
    /// Data flow (all multi-node aware, nothing pins a fixed server):
    ///   /api/server-list  -> zone cards (status light + load level)
    ///   进入              -> GameClient.EnterZone: /api/login (zone-pinned)
    ///                        -> /api/assign-gate (+queue polling)
    ///                        -> gate TCP + token verify + session login
    ///                        -> CreatePlayer? -> EnterGame -> NotifyEnterScene
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QdaoServerSelectView : MonoBehaviour
    {
        private static readonly string[] TopTabLabels =
        {
            "最近登录", "推荐服务器", "全部区服"
        };

        private static readonly string[] CategoryLabels =
        {
            "近期", "推荐", "全部", "新服", "流畅", "维护"
        };

        /// <summary>Zone cards shown per page (matches the 4x2 grid in the art).</summary>
        private const int PageSize = 8;

        // Category indices (must match CategoryLabels order).
        private const int CatRecent = 0, CatRecommended = 1, CatAll = 2,
                          CatNew = 3, CatSmooth = 4, CatMaintenance = 5;

        [Header("Native art sprites")]
        [SerializeField] private Sprite _screenArtSprite;
        [SerializeField] private Sprite _statusDotSprite;
        [SerializeField] private Sprite _credentialSprite;
        [SerializeField] private Sprite _credentialCancelSprite;
        [SerializeField] private Sprite _credentialSubmitSprite;

        [Header("Native visual hierarchy")]
        [SerializeField] private RectTransform _contentRoot;
        [SerializeField] private Image _backdropImage;
        [SerializeField] private Image _screenArtImage;
        [SerializeField] private RectTransform _credentialPanel;
        [SerializeField] private Image _credentialBlockerImage;
        [SerializeField] private Image _credentialImage;
        [SerializeField] private Button _credentialCancelButton;
        [SerializeField] private Button _credentialSubmitButton;
        [SerializeField] private TMP_Text _credentialCancelText;
        [SerializeField] private TMP_Text _credentialSubmitText;
        [SerializeField] private TMP_Text _credentialTitleText;
        [SerializeField] private TMP_Text _credentialStatusText;
        [SerializeField] private TMP_InputField _accountInput;
        [SerializeField] private TMP_InputField _passwordInput;
        [SerializeField] private TMP_InputField _searchInput;
        [SerializeField] private Button[] _topButtons;
        [SerializeField] private Image[] _topImages;
        [SerializeField] private Image[] _topSelectionMarks;
        [SerializeField] private TMP_Text[] _topLabels;
        [SerializeField] private Image _topDefaultDimmer;
        [SerializeField] private Button[] _categoryButtons;
        [SerializeField] private Image[] _categoryImages;
        [SerializeField] private Image[] _categorySelectionMarks;
        [SerializeField] private TMP_Text[] _categoryTexts;
        [SerializeField] private Image _categoryDefaultDimmer;
        [SerializeField] private Button[] _serverButtons;
        [SerializeField] private Image[] _serverCardImages;
        [SerializeField] private TMP_Text[] _serverNames;
        [SerializeField] private TMP_Text[] _serverSubtitles;
        [SerializeField] private Image[] _serverDots;
        [SerializeField] private Image[] _serverEmptyCovers;
        [SerializeField] private Button _prevPageButton;
        [SerializeField] private Button _nextPageButton;
        [SerializeField] private TMP_Text _prevPageText;
        [SerializeField] private TMP_Text _nextPageText;
        [SerializeField] private TMP_Text _pageText;
        [SerializeField] private TMP_Text _lastLoginText;
        [SerializeField] private TMP_Text _selectedText;
        [SerializeField] private TMP_Text _refreshText;
        [SerializeField] private TMP_Text _enterText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _refreshButton;
        [SerializeField] private Button _enterButton;

        private AppBootstrap _app;
        private int _selectedTopTab;
        private int _selectedCategory;
        private uint _selectedZoneId;
        private int _page;
        private string _searchQuery = string.Empty;
        private bool _busy;
        private bool _serverListLoading;
        private bool _serverListFailed;
        private int _serverListRequestGen;
        private bool _previewMode;
        // 每次点「进入」+1;断线/重进也会 +1 作废旧回调,防止迟到的
        // onSuccess/onError 闭包作用到新一轮状态上。
        private int _enterRunId;
        private bool _eventsBound;
        private bool _clientEventsBound;
        private GameClient _boundGameClient;
        private Action<AssignGateResult> _queueUpdateHandler;
        private Action _disconnectedHandler;

        private readonly List<ServerListZone> _filtered = new();

        // Status-light palette (kept local: these are semantic, not theme colors).
        private static readonly Color DotSmooth      = QdaoUguiTheme.Html("#3F9B4E");
        private static readonly Color DotBusy        = QdaoUguiTheme.Html("#D98E1B");
        private static readonly Color DotFull        = QdaoUguiTheme.Html("#C23B22");
        private static readonly Color DotMaintenance = QdaoUguiTheme.Html("#8A8578");
        private static readonly Color DotPreview     = QdaoUguiTheme.Html("#3B6FA8");
        private static readonly Color DotClosed      = QdaoUguiTheme.Html("#5A2A24");

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only source of truth used by <c>QdaoUguiBuilder</c> to bake
        /// the production prefab. Player builds must instantiate that prefab
        /// and never manufacture a replacement hierarchy at runtime.
        /// </summary>
        public static QdaoServerSelectView CreateTemplate(UnityEngine.Transform parent)
        {
            var go = new GameObject("QdaoServerSelectUgui", typeof(RectTransform));
            go.SetActive(false);
            var rect = (RectTransform)go.transform;
            if (parent != null)
                rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = UnityEngine.Vector3.one;

            var view = go.AddComponent<QdaoServerSelectView>();
            view.BuildVisualTree();
            go.SetActive(true);
            return view;
        }
#endif

        private void Awake()
        {
            // Runtime instances must come from the baked prefab. Building the
            // hierarchy here would hide broken serialization and ship an
            // editor-only Resources lookup path into production.
        }

        public void Initialize(AppBootstrap app)
        {
            _app = app;
            _previewMode = false;
            ValidatePrefabReferences();
            // Normalize baked prefabs created before the credential limits
            // were aligned with the login service contract.
            _accountInput.characterLimit = 191;
            _passwordInput.characterLimit = 1024;
            if (QdaoUguiTheme.ResolveFont() == null)
                throw new MissingReferenceException("The qdao uGUI TMP font asset is missing.");
            BindEvents();
            BindClientEvents();

            _selectedTopTab = 0;
            _selectedCategory = CatRecent;
            _page = 0;
            _selectedZoneId = 0;

            var session = _app?.Session;
            if (session != null)
            {
                _accountInput.SetTextWithoutNotify(session.Account ?? string.Empty);
                _passwordInput.SetTextWithoutNotify(session.Password ?? string.Empty);
            }
            ShowCredentialPanel(false);

            RefreshFontReferences();
            RebuildFilteredList();
            RefreshVisualState();
            SetStatus("正在获取区服列表…");
            _app?.Run(LoadServerList());
            _app?.Run(LoadAnnouncements());
        }

        /// <summary>Editor capture helper: opens the login dialog with its idle prompt.</summary>
        public void ShowCredentialForPreview()
        {
            ShowCredentialPanel(true);
            SetStatus("请输入账号和密码");
        }

        public void PrepareForPreview()
        {
            _previewMode = true;
            ValidatePrefabReferences();
            RefreshFontReferences();
            ShowCredentialPanel(false);
            if (_app?.Session == null)
                BuildPreviewList();
            else
                RebuildFilteredList();
            RefreshVisualState();
            SetStatus(string.Empty);
        }

        private void BuildPreviewList()
        {
            _filtered.Clear();
            var names = new[] { "云海宗", "清风谷", "碧落渊", "紫霄峰", "沧浪洲", "昆仑墟", "蓬莱岛", "瀛洲海" };
            for (var i = 0; i < PageSize; i++)
            {
                _filtered.Add(new ServerListZone
                {
                    zone_id = (uint)(i + 1),
                    name = names[i],
                    status = "OPEN",
                    load_level = i == 2 ? "BUSY" : i == 5 ? "FULL" : "SMOOTH",
                    recommended = i < 2,
                    is_new = i == 3,
                });
            }
            _selectedZoneId = 1;
            _page = 0;
        }

        public void Tick(float deltaTime)
        {
            _ = deltaTime;
        }

        // ── data loading ─────────────────────────────────────────

        private IEnumerator LoadServerList()
        {
            if (_app?.Gateway == null)
            {
                _serverListFailed = true;
                RefreshVisualState();
                yield break;
            }

            var requestGen = ++_serverListRequestGen;
            _serverListLoading = true;
            _serverListFailed = false;
            RefreshVisualState();
            yield return _app.Gateway.GetServerList(
                resp =>
                {
                    if (this == null || requestGen != _serverListRequestGen) return;
                    _serverListLoading = false;
                    _serverListFailed = false;
                    var session = _app.Session;
                    session.Zones.Clear();
                    if (resp?.zones != null)
                        session.Zones.AddRange(resp.zones);
                    EnsureDefaultSelection();
                    RebuildFilteredList();
                    RefreshVisualState();
                    SetStatus(session.Zones.Count == 0 ? "暂无可用区服" : string.Empty);
                },
                err =>
                {
                    if (this == null || requestGen != _serverListRequestGen) return;
                    _serverListLoading = false;
                    _serverListFailed = true;
                    RefreshVisualState();
                    SetStatus("获取区服列表失败,请点刷新重试");
                    Debug.LogWarning($"[QdaoUgui] server-list: {err}");
                });
        }

        private IEnumerator LoadAnnouncements()
        {
            if (_app?.Gateway == null) yield break;
            yield return _app.Gateway.GetAnnouncements(
                resp =>
                {
                    var session = _app.Session;
                    session.Announcements.Clear();
                    if (resp?.items != null)
                        session.Announcements.AddRange(resp.items);
                },
                err => Debug.LogWarning($"[QdaoUgui] announcement: {err}"));
        }

        private void EnsureDefaultSelection()
        {
            var session = _app?.Session;
            if (session == null || session.Zones.Count == 0) return;
            if (_selectedZoneId != 0 && FindZone(_selectedZoneId) != null) return;

            ServerListZone pick = null;
            foreach (var recent in session.RecentZoneIds)
            {
                pick = FindZone(recent);
                if (pick != null) break;
            }
            if (pick == null)
                foreach (var z in session.Zones)
                    if (z.recommended) { pick = z; break; }
            if (pick == null)
                pick = session.Zones[0];
            SelectZone(pick.zone_id, silent: true);
        }

        private ServerListZone FindZone(uint zoneId)
        {
            var session = _app?.Session;
            if (session == null) return null;
            foreach (var z in session.Zones)
                if (z.zone_id == zoneId) return z;
            return null;
        }

        private ServerListZone FindRenderedZone(uint zoneId)
        {
            var zone = FindZone(zoneId);
            if (zone != null) return zone;
            foreach (var visible in _filtered)
                if (visible.zone_id == zoneId) return visible;
            return null;
        }

        // ── filtering / paging ───────────────────────────────────

        private void RebuildFilteredList()
        {
            _filtered.Clear();
            var session = _app?.Session;
            if (session == null) return;

            IEnumerable<ServerListZone> source = session.Zones;
            if (_selectedCategory == CatRecent)
            {
                var recent = new List<ServerListZone>();
                foreach (var zid in session.RecentZoneIds)
                {
                    var z = FindZone(zid);
                    if (z != null) recent.Add(z);
                }
                // A fresh install has no recent zones yet. Keep the reference
                // composition populated instead of presenting an empty first tab.
                if (recent.Count > 0)
                    source = recent;
            }

            foreach (var z in source)
            {
                if (!MatchesCategory(z)) continue;
                if (!MatchesSearch(z)) continue;
                _filtered.Add(z);
            }

            int pageCount = PageCount();
            if (_page >= pageCount) _page = Mathf.Max(0, pageCount - 1);
        }

        private bool MatchesCategory(ServerListZone z)
        {
            switch (_selectedCategory)
            {
                case CatRecommended: return z.recommended;
                case CatNew:         return z.is_new;
                case CatSmooth:      return z.status == "OPEN" && (z.load_level == "SMOOTH" || string.IsNullOrEmpty(z.load_level));
                case CatMaintenance: return z.status == "MAINTENANCE";
                default:             return true; // CatAll / CatRecent(已按列表来源过滤)
            }
        }

        private bool MatchesSearch(ServerListZone z)
        {
            if (string.IsNullOrEmpty(_searchQuery)) return true;
            if (!string.IsNullOrEmpty(z.name) && z.name.Contains(_searchQuery)) return true;
            return z.zone_id.ToString().Contains(_searchQuery);
        }

        private int PageCount()
            => Mathf.Max(1, (_filtered.Count + PageSize - 1) / PageSize);

        // ── visual tree ──────────────────────────────────────────

        private void BuildVisualTree()
        {
            ResolveArtSprites();

            // The 2560x1080 artwork is always shown at its authored aspect.
            // Narrower displays receive deterministic solid letterboxing; a
            // second, differently scaled scenery image would create seams.
            var backdropRect = QdaoUguiFactory.CreateStretch(
                "LetterboxBackdrop", transform, Vector4.zero);
            _backdropImage = backdropRect.gameObject.AddComponent<Image>();
            _backdropImage.color = QdaoUguiTheme.Letterbox;
            _backdropImage.raycastTarget = false;
            _contentRoot = QdaoUguiFactory.CreateCenteredRect(
                "ContentRoot", transform,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);

            // This full-design sprite is decoration only (raycastTarget=false).
            // Real uGUI controls below own all interaction and dynamic state.
            _screenArtImage = QdaoUguiFactory.CreateImage(
                "ScreenArtwork", _contentRoot, 0f, 0f,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight, _screenArtSprite);
            _searchInput = QdaoUguiFactory.CreateInputField(
                "SearchInput", _contentRoot, 692f, 307f, 188f, 44f, "搜索服务器");

            _topImages = new Image[TopTabLabels.Length];
            _topButtons = new Button[TopTabLabels.Length];
            _topSelectionMarks = new Image[TopTabLabels.Length];
            _topLabels = new TMP_Text[TopTabLabels.Length];
            var topRects = new[]
            {
                new Vector4(709f, 191f, 260f, 76f),
                new Vector4(963f, 192f, 300f, 66f),
                new Vector4(1269f, 192f, 344f, 66f),
            };
            for (var i = 0; i < TopTabLabels.Length; i++)
            {
                var rect = topRects[i];
                _topButtons[i] = QdaoUguiFactory.CreateHitButton(
                    $"TopTab_{i}", _contentRoot, rect.x, rect.y, rect.z, rect.w);
                _topImages[i] = _topButtons[i].GetComponent<Image>();
                _topSelectionMarks[i] = QdaoUguiFactory.CreateImage(
                    $"TopTabSelection_{i}", _contentRoot,
                    rect.x + rect.z * 0.30f, rect.y + rect.w - 8f,
                    rect.z * 0.40f, 3f, null);
                _topSelectionMarks[i].color = QdaoUguiTheme.SelectedRed;
                _topLabels[i] = QdaoUguiFactory.CreateText(
                    $"TopTabText_{i}",
                    _contentRoot,
                    rect.x,
                    rect.y + 10f,
                    rect.z,
                    rect.w - 18f,
                    TopTabLabels[i],
                    22f,
                    QdaoUguiTheme.Brown,
                    TextAlignmentOptions.Center);
            }
            _topDefaultDimmer = QdaoUguiFactory.CreateImage(
                "TopDefaultDimmer", _contentRoot, 724f, 207f, 178f, 39f, null);
            _topDefaultDimmer.color = new Color(0.16f, 0.18f, 0.16f, 0.34f);

            _categoryImages = new Image[CategoryLabels.Length];
            _categoryButtons = new Button[CategoryLabels.Length];
            _categorySelectionMarks = new Image[CategoryLabels.Length];
            _categoryTexts = new TMP_Text[CategoryLabels.Length];
            for (var i = 0; i < CategoryLabels.Length; i++)
            {
                var y = 365f + i * 75f;
                _categoryButtons[i] = QdaoUguiFactory.CreateHitButton(
                    $"CategoryArt_{i}", _contentRoot, 647f, y, 240f, 65f);
                _categoryImages[i] = _categoryButtons[i].GetComponent<Image>();
                _categorySelectionMarks[i] = QdaoUguiFactory.CreateImage(
                    $"CategorySelection_{i}", _contentRoot, 872f, y + 17f, 4f, 31f, null);
                _categorySelectionMarks[i].color = QdaoUguiTheme.SelectedRed;
                _categoryTexts[i] = QdaoUguiFactory.CreateText(
                    $"CategoryText_{i}",
                    _contentRoot,
                    704f,
                    y + 14f,
                    126f,
                    30f,
                    CategoryLabels[i],
                    21f,
                    QdaoUguiTheme.Brown);
            }
            _categoryDefaultDimmer = QdaoUguiFactory.CreateImage(
                "CategoryDefaultDimmer", _contentRoot, 653f, 371f, 228f, 53f, null);
            _categoryDefaultDimmer.color = new Color(0.12f, 0.14f, 0.12f, 0.28f);

            _serverButtons = new Button[PageSize];
            _serverCardImages = new Image[PageSize];
            _serverNames = new TMP_Text[PageSize];
            _serverSubtitles = new TMP_Text[PageSize];
            _serverDots = new Image[PageSize];
            _serverEmptyCovers = new Image[PageSize];
            var rowY = new[] { 310f, 429f, 548f, 666f };
            for (var i = 0; i < PageSize; i++)
            {
                var column = i % 2;
                var row = i / 2;
                var x = column == 0 ? 962f : 1459f;
                var y = rowY[row];

                _serverEmptyCovers[i] = QdaoUguiFactory.CreateImage(
                    $"ServerEmptyCover_{i}", _contentRoot,
                    x + 28f, y + 35f, 24f, 24f, _statusDotSprite);
                _serverEmptyCovers[i].color = DotMaintenance;
                _serverEmptyCovers[i].enabled = false;
                _serverButtons[i] = QdaoUguiFactory.CreateHitButton(
                    $"ServerCardArt_{i}", _contentRoot, x, y, 473f, 100f);
                _serverCardImages[i] = _serverButtons[i].GetComponent<Image>();
                _serverDots[i] = QdaoUguiFactory.CreateImage(
                    $"ServerDot_{i}", _contentRoot, x + 28f, y + 35f, 24f, 24f, _statusDotSprite);
                _serverDots[i].color = Color.white;
                _serverNames[i] = QdaoUguiFactory.CreateText(
                    $"ServerName_{i}",
                    _contentRoot,
                    x + 69f,
                    y + 16f,
                    246f,
                    30f,
                    string.Empty,
                    22f,
                    QdaoUguiTheme.DarkBrown);
                _serverSubtitles[i] = QdaoUguiFactory.CreateText(
                    $"ServerStatus_{i}",
                    _contentRoot,
                    x + 69f,
                    y + 50f,
                    238f,
                    27f,
                    string.Empty,
                    18f,
                    QdaoUguiTheme.Brown);
            }

            // Pager: arrows flanking the grid + "page/total" under the grid.
            _prevPageButton = QdaoUguiFactory.CreateHitButton(
                "PrevPage", _contentRoot, 908f, 500f, 48f, 120f);
            _nextPageButton = QdaoUguiFactory.CreateHitButton(
                "NextPage", _contentRoot, 1938f, 500f, 48f, 120f);
            _prevPageText = QdaoUguiFactory.CreateText(
                "PrevPageText", _contentRoot, 912f, 538f, 40f, 44f, "〈", 30f,
                QdaoUguiTheme.SelectedRed, TextAlignmentOptions.Center);
            _nextPageText = QdaoUguiFactory.CreateText(
                "NextPageText", _contentRoot, 1942f, 538f, 40f, 44f, "〉", 30f,
                QdaoUguiTheme.SelectedRed, TextAlignmentOptions.Center);
            _pageText = QdaoUguiFactory.CreateText(
                "PageText", _contentRoot, 1388f, 772f, 120f, 30f, "1/1", 20f,
                QdaoUguiTheme.Brown, TextAlignmentOptions.Center);

            _lastLoginText = QdaoUguiFactory.CreateText(
                "LastLoginText",
                _contentRoot,
                1212f,
                856f,
                270f,
                32f,
                "最近登录：-",
                22f,
                QdaoUguiTheme.Brown);
            _selectedText = QdaoUguiFactory.CreateText(
                "SelectedServerText",
                _contentRoot,
                1484f,
                856f,
                220f,
                32f,
                "当前选择",
                22f,
                QdaoUguiTheme.Brown);
            _statusText = QdaoUguiFactory.CreateText(
                "StatusText",
                _contentRoot,
                1212f,
                892f,
                540f,
                28f,
                string.Empty,
                20f,
                QdaoUguiTheme.SelectedRed);

            // The screen artwork owns the carved chrome. Interaction targets
            // stay independent, transparent, and accessible to EventSystem.
            _backButton = QdaoUguiFactory.CreateHitButton(
                "BackButton", _contentRoot, 535f, 796f, 150f, 210f);

            _refreshButton = QdaoUguiFactory.CreateHitButton(
                "RefreshButton", _contentRoot, 1910f, 850f, 104f, 70f);
            _refreshText = QdaoUguiFactory.CreateText(
                "RefreshText", _contentRoot, 1914f, 869f, 96f, 34f, "刷新", 20f,
                QdaoUguiTheme.SelectedRed, TextAlignmentOptions.Center);
            _refreshText.gameObject.SetActive(false);

            _enterButton = QdaoUguiFactory.CreateHitButton(
                "EnterButton", _contentRoot, 1728f, 850f, 176f, 82f);
            _enterText = QdaoUguiFactory.CreateText(
                "EnterText", _contentRoot, 1732f, 869f, 168f, 36f, "进入", 26f,
                QdaoUguiTheme.Brown, TextAlignmentOptions.Center);

            BuildCredentialPanel();
        }

        private void BuildCredentialPanel()
        {
            // The jade strip is painted in the top band of its texture
            // (opaque rows 225..510 of 756 -> local 130..294 at 1200x436).
            // This offset puts the strip's visual center at design y ≈ 500,
            // leaving room for the title above and the buttons below.
            const float PanelY = 288f;

            _credentialPanel = QdaoUguiFactory.CreateRect(
                "CredentialPanel", _contentRoot, 0f, 0f,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);

            _credentialBlockerImage = QdaoUguiFactory.CreateImage(
                "CredentialBlocker", _credentialPanel, 0f, 0f,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight,
                null, raycastTarget: true);
            // Alpha chosen for Unity's linear-space blend: 0.68 lands at roughly
            // half perceived brightness behind the dialog.
            _credentialBlockerImage.color = new Color(0f, 0f, 0f, 0.68f);

            _credentialImage = QdaoUguiFactory.CreateImage(
                "CredentialArt", _credentialPanel, 680f, PanelY, 1200f, 436f, _credentialSprite);
            _credentialTitleText = QdaoUguiFactory.CreateText(
                "CredentialTitle", _credentialPanel, 1030f, PanelY + 58f, 500f, 60f,
                "账号登录", 46f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);
            _accountInput = QdaoUguiFactory.CreateInputField(
                "AccountInput", _credentialPanel, 875f, PanelY + 205f, 355f, 64f, "账号", 191);
            _passwordInput = QdaoUguiFactory.CreateInputField(
                "PasswordInput", _credentialPanel, 1325f, PanelY + 205f, 355f, 64f, "密码", 1024);
            _passwordInput.contentType = TMP_InputField.ContentType.Password;

            _credentialCancelButton = QdaoUguiFactory.CreateArtButton(
                "CredentialCancelButton", _credentialPanel, 1060f, PanelY + 345f, 190f, 56f,
                _credentialCancelSprite, out _);
            _credentialCancelText = QdaoUguiFactory.CreateText(
                "CredentialCancelText", _credentialPanel, 1060f, PanelY + 356f, 190f, 34f,
                "取消", 24f, QdaoUguiTheme.Brown, TextAlignmentOptions.Center);

            _credentialSubmitButton = QdaoUguiFactory.CreateArtButton(
                "CredentialSubmitButton", _credentialPanel, 1310f, PanelY + 345f, 190f, 56f,
                _credentialSubmitSprite, out _);
            _credentialSubmitText = QdaoUguiFactory.CreateText(
                "CredentialSubmitText", _credentialPanel, 1310f, PanelY + 356f, 190f, 34f,
                "登录", 24f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);

            _credentialStatusText = QdaoUguiFactory.CreateText(
                "CredentialStatus", _credentialPanel, 980f, PanelY + 424f, 600f, 36f,
                string.Empty, 22f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);

            // The server-selection reference contains no credentials. Keep this
            // generated jade strip as an on-demand layer instead of changing the
            // normal composition.
            _credentialPanel.gameObject.SetActive(false);
        }

        private void ResolveArtSprites()
        {
            // Unity's destroyed-object sentinel is not a CLR null, so `??=`
            // would retain a missing prefab reference. Use Unity's overloaded
            // null check for every serialized Object reference.
            if (_screenArtSprite == null)
                _screenArtSprite = QdaoUguiTheme.RequireSprite(QdaoUguiTheme.ScreenArtSpritePath);
            if (_statusDotSprite == null)
                _statusDotSprite = QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath);
            if (_credentialSprite == null)
                _credentialSprite = QdaoUguiTheme.RequireSprite(QdaoUguiTheme.CredentialSpritePath);
            if (_credentialCancelSprite == null)
                _credentialCancelSprite = QdaoUguiTheme.RequireSprite(QdaoUguiTheme.CredentialCancelSpritePath);
            if (_credentialSubmitSprite == null)
                _credentialSubmitSprite = QdaoUguiTheme.RequireSprite(QdaoUguiTheme.CredentialSubmitSpritePath);
        }

        private void ShowCredentialPanel(bool visible, bool focusAccount = false)
        {
            if (_credentialPanel == null)
                return;

            _credentialPanel.gameObject.SetActive(visible);
            if (visible && focusAccount && _accountInput != null)
            {
                _accountInput.Select();
                _accountInput.ActivateInputField();
            }
        }

        private void BindEvents()
        {
            if (_eventsBound)
                UnbindEvents();

            for (var i = 0; i < _topButtons.Length; i++)
            {
                var captured = i;
                _topButtons[i].onClick.AddListener(() => SelectTopTab(captured));
            }
            for (var i = 0; i < _categoryButtons.Length; i++)
            {
                var captured = i;
                _categoryButtons[i].onClick.AddListener(() => SelectCategory(captured));
            }
            for (var i = 0; i < _serverButtons.Length; i++)
            {
                var captured = i;
                _serverButtons[i].onClick.AddListener(() => SelectServerSlot(captured));
            }

            _accountInput.onSubmit.AddListener(OnAccountSubmitted);
            _passwordInput.onSubmit.AddListener(OnPasswordSubmitted);
            _searchInput.onValueChanged.AddListener(OnSearchChanged);
            _prevPageButton.onClick.AddListener(() => TurnPage(-1));
            _nextPageButton.onClick.AddListener(() => TurnPage(1));
            _backButton.onClick.AddListener(OnBackClicked);
            _refreshButton.onClick.AddListener(OnRefreshClicked);
            _enterButton.onClick.AddListener(OnEnterClicked);
            _credentialCancelButton.onClick.AddListener(OnCredentialCancelClicked);
            _credentialSubmitButton.onClick.AddListener(OnCredentialSubmitClicked);
            _eventsBound = true;
        }

        private void UnbindEvents()
        {
            foreach (var button in _topButtons)
                button.onClick.RemoveAllListeners();
            foreach (var button in _categoryButtons)
                button.onClick.RemoveAllListeners();
            foreach (var button in _serverButtons)
                button.onClick.RemoveAllListeners();

            _accountInput.onValueChanged.RemoveAllListeners();
            _accountInput.onSubmit.RemoveAllListeners();
            _passwordInput.onValueChanged.RemoveAllListeners();
            _passwordInput.onSubmit.RemoveAllListeners();
            _searchInput.onValueChanged.RemoveAllListeners();
            _prevPageButton.onClick.RemoveAllListeners();
            _nextPageButton.onClick.RemoveAllListeners();
            _backButton.onClick.RemoveAllListeners();
            _refreshButton.onClick.RemoveAllListeners();
            _enterButton.onClick.RemoveAllListeners();
            _credentialCancelButton.onClick.RemoveAllListeners();
            _credentialSubmitButton.onClick.RemoveAllListeners();
            _eventsBound = false;
        }

        private void BindClientEvents()
        {
            if (_clientEventsBound || _app?.GameClient == null) return;
            _boundGameClient = _app.GameClient;
            _queueUpdateHandler = r =>
            {
                if (r.queue_source == "login")
                    SetStatus($"排队中… 第 {r.queue_rank + 1} 位 / 共 {Math.Max(r.queue_total, r.queue_rank + 1)} 人");
            };
            _disconnectedHandler = () =>
            {
                _enterRunId++;      // 作废在途的进入流程回调
                _busy = false;
                if (!gameObject.activeSelf)
                    gameObject.SetActive(true);
                RefreshVisualState();
                SetStatus("与服务器的连接已断开");
            };

            _clientEventsBound = true;
            _boundGameClient.OnFlowStatus += SetStatus;
            _boundGameClient.OnQueueUpdate += _queueUpdateHandler;
            _boundGameClient.OnDisconnected += _disconnectedHandler;
        }

        private void UnbindClientEvents()
        {
            if (!_clientEventsBound) return;

            if (_boundGameClient != null)
            {
                _boundGameClient.OnFlowStatus -= SetStatus;
                _boundGameClient.OnQueueUpdate -= _queueUpdateHandler;
                _boundGameClient.OnDisconnected -= _disconnectedHandler;
            }

            _boundGameClient = null;
            _queueUpdateHandler = null;
            _disconnectedHandler = null;
            _clientEventsBound = false;
        }

        private void OnDestroy()
        {
            _serverListRequestGen++;
            if (_eventsBound)
                UnbindEvents();
            UnbindClientEvents();
        }

        // ── interactions ─────────────────────────────────────────

        private void SelectTopTab(int index)
        {
            if (_busy) return;
            _selectedTopTab = Mathf.Clamp(index, 0, TopTabLabels.Length - 1);
            // Top tabs are shortcuts onto the category dimension.
            _selectedCategory = _selectedTopTab switch
            {
                0 => CatRecent,
                1 => CatRecommended,
                _ => CatAll,
            };
            _page = 0;
            RebuildFilteredList();
            RefreshVisualState();
        }

        private void SelectCategory(int index)
        {
            if (_busy) return;
            _selectedCategory = Mathf.Clamp(index, 0, CategoryLabels.Length - 1);
            _selectedTopTab = _selectedCategory switch
            {
                CatRecent => 0,
                CatRecommended => 1,
                _ => 2,
            };
            _page = 0;
            RebuildFilteredList();
            RefreshVisualState();
        }

        private void SelectServerSlot(int slot)
        {
            if (_busy) return;
            int idx = _page * PageSize + slot;
            if (idx < 0 || idx >= _filtered.Count) return;
            SelectZone(_filtered[idx].zone_id, silent: false);
        }

        private void SelectZone(uint zoneId, bool silent)
        {
            _selectedZoneId = zoneId;
            var session = _app?.Session;
            if (session != null)
            {
                session.SelectedZoneId = zoneId;
                session.SelectedZoneIndex = session.Zones.FindIndex(z => z.zone_id == zoneId);
            }
            RefreshVisualState();
            if (!silent)
            {
                var z = FindZone(zoneId);
                SetStatus(z == null ? string.Empty : $"已选择：{ZoneDisplayName(z)}");
            }
        }

        private void TurnPage(int delta)
        {
            if (_busy) return;
            int pageCount = PageCount();
            int next = Mathf.Clamp(_page + delta, 0, pageCount - 1);
            if (next == _page) return;
            _page = next;
            RefreshVisualState();
        }

        private void OnSearchChanged(string value)
        {
            if (_busy) return;
            _searchQuery = (value ?? string.Empty).Trim();
            _page = 0;
            RebuildFilteredList();
            RefreshVisualState();
        }

        private void OnBackClicked()
        {
            if (_busy) return;
            if (_credentialPanel != null && _credentialPanel.gameObject.activeSelf)
            {
                ShowCredentialPanel(false);
                SetStatus(string.Empty);
                return;
            }
            ShowCredentialPanel(true, true);
            SetStatus("请输入账号和密码");
        }

        private void OnCredentialCancelClicked()
        {
            if (_busy) return;
            ShowCredentialPanel(false);
            SetStatus(string.Empty);
        }

        private void OnCredentialSubmitClicked()
        {
            if (_busy) return;
            OnEnterClicked();
        }

        private void OnAccountSubmitted(string _)
        {
            if (_busy || _passwordInput == null) return;
            _passwordInput.Select();
            _passwordInput.ActivateInputField();
        }

        private void OnPasswordSubmitted(string _)
        {
            OnCredentialSubmitClicked();
        }

        private void OnRefreshClicked()
        {
            if (_busy || _serverListLoading) return;
            SetStatus("正在刷新区服列表…");
            _app?.Run(LoadServerList());
        }

        private void OnEnterClicked()
        {
            if (_busy) { SetStatus("正在进入中,请稍候…"); return; }
            var session = _app?.Session;
            if (session == null) return;

            var account = (_accountInput.text ?? string.Empty).Trim();
            var password = _passwordInput.text ?? string.Empty;
            if (account.Length == 0 || password.Length == 0)
            {
                ShowCredentialPanel(true, account.Length == 0);
                if (account.Length == 0)
                {
                    _accountInput.Select();
                    _accountInput.ActivateInputField();
                    SetStatus("请输入账号");
                }
                else
                {
                    _passwordInput.Select();
                    _passwordInput.ActivateInputField();
                    SetStatus("请输入密码");
                }
                return;
            }
            ShowCredentialPanel(false);

            var zone = FindZone(_selectedZoneId);
            if (zone == null) { SetStatus("请先选择区服"); return; }
            if (zone.status == "MAINTENANCE") { SetStatus(FallbackMsg(zone.maintenance_msg, "该区服正在维护中")); return; }
            if (zone.status == "CLOSED") { SetStatus(FallbackMsg(zone.maintenance_msg, "该区服已关闭")); return; }
            if (zone.status == "PREVIEW") { SetStatus($"该区服尚未开放{OpenTimeSuffix(zone)}"); return; }

            session.Account = account;
            session.Password = password;

            _busy = true;
            int runId = ++_enterRunId;
            RefreshVisualState();
            _app.Run(_app.GameClient.EnterZone(
                zone.zone_id, account, password, session.DeviceId,
                onSuccess: () =>
                {
                    if (runId != _enterRunId) return;
                    _busy = false;
                    // Persist the immutable inputs that actually completed,
                    // not any UI/session value changed by a later callback.
                    session.Account = account;
                    session.Password = password;
                    session.SelectedZoneId = zone.zone_id;
                    session.SelectedZoneIndex = session.Zones.FindIndex(
                        candidate => candidate.zone_id == zone.zone_id);
                    session.SetTokensFromClient(_app.GameClient);
                    session.RecordZoneEntered(zone.zone_id);
                    SetStatus(string.Empty);
                    gameObject.SetActive(false);
                },
                onError: err =>
                {
                    if (runId != _enterRunId) return;
                    _busy = false;
                    RefreshVisualState();
                    SetStatus(err);
                }));
        }

        // ── rendering ────────────────────────────────────────────

        private void RefreshVisualState()
        {
            var canInteract = !_busy;
            for (var i = 0; i < _topLabels.Length; i++)
            {
                _topButtons[i].interactable = canInteract;
                _topSelectionMarks[i].enabled = i == _selectedTopTab && i != 0;
                _topLabels[i].color = i == _selectedTopTab
                    ? (i == 0 ? QdaoUguiTheme.Cream : QdaoUguiTheme.SelectedRed)
                    : QdaoUguiTheme.Brown;
            }
            _topDefaultDimmer.enabled = _selectedTopTab != 0;

            for (var i = 0; i < _categoryTexts.Length; i++)
            {
                _categoryButtons[i].interactable = canInteract;
                _categoryImages[i].enabled = true;
                _categorySelectionMarks[i].enabled = i == _selectedCategory && i != 0;
                _categoryTexts[i].color = i == _selectedCategory
                    ? (i == 0 ? QdaoUguiTheme.Cream : QdaoUguiTheme.SelectedRed)
                    : QdaoUguiTheme.Brown;
            }
            _categoryDefaultDimmer.enabled = _selectedCategory != 0;

            int pageCount = PageCount();
            _pageText.text = pageCount > 1 ? $"{_page + 1}/{pageCount}" : string.Empty;
            _prevPageButton.gameObject.SetActive(pageCount > 1);
            _nextPageButton.gameObject.SetActive(pageCount > 1);
            _prevPageText.gameObject.SetActive(pageCount > 1);
            _nextPageText.gameObject.SetActive(pageCount > 1);
            _prevPageButton.interactable = canInteract && _page > 0;
            _nextPageButton.interactable = canInteract && _page < pageCount - 1;

            for (var slot = 0; slot < PageSize; slot++)
            {
                int idx = _page * PageSize + slot;
                if (idx < _filtered.Count)
                {
                    var z = _filtered[idx];
                    var isSelected = z.zone_id == _selectedZoneId;
                    _serverButtons[slot].interactable = canInteract;
                    _serverCardImages[slot].enabled = true;
                    _serverEmptyCovers[slot].enabled = false;
                    _serverDots[slot].enabled = true;
                    _serverDots[slot].color = StatusDotColor(z);
                    _serverNames[slot].text = CardDisplayName(z);
                    _serverNames[slot].color = isSelected ? QdaoUguiTheme.SelectedRed : QdaoUguiTheme.DarkBrown;
                    _serverNames[slot].alpha = 1f;
                    _serverSubtitles[slot].text = z.status == "OPEN" ? string.Empty : ZoneSubtitle(z);
                    _serverSubtitles[slot].color = QdaoUguiTheme.Brown;
                    _serverSubtitles[slot].alpha = 1f;
                }
                else
                {
                    _serverButtons[slot].interactable = false;
                    _serverCardImages[slot].enabled = true;
                    _serverEmptyCovers[slot].enabled = true;
                    _serverDots[slot].enabled = false;
                    _serverNames[slot].text = string.Empty;
                    _serverSubtitles[slot].text = string.Empty;
                }
            }

            var selected = FindRenderedZone(_selectedZoneId);
            _selectedText.text = selected == null ? "当前选择：-" : $"当前选择：{CardDisplayName(selected)}";

            var session = _app?.Session;
            ServerListZone lastZone = null;
            if (session != null && session.RecentZoneIds.Count > 0)
                lastZone = FindZone(session.RecentZoneIds[0]);
            if (_previewMode && lastZone == null)
                lastZone = selected;
            _lastLoginText.text = lastZone == null ? "最近登录：-" : $"最近登录：{CardDisplayName(lastZone)}";

            _enterText.text = _busy ? "进入中…" : "进入";
            _searchInput.interactable = canInteract;
            _accountInput.interactable = canInteract;
            _passwordInput.interactable = canInteract;
            _backButton.interactable = canInteract;
            _refreshButton.interactable = canInteract && !_serverListLoading;
            _refreshText.gameObject.SetActive(_serverListFailed && !_serverListLoading);
            _enterButton.interactable = canInteract;
            _credentialCancelButton.interactable = canInteract;
            _credentialSubmitButton.interactable = canInteract;
        }

        private static string ZoneDisplayName(ServerListZone z)
            => string.IsNullOrEmpty(z.name) ? $"{z.zone_id}区" : $"{z.zone_id}区·{z.name}";

        private static string CardDisplayName(ServerListZone z)
            => string.IsNullOrEmpty(z.name) ? $"{z.zone_id}区" : z.name;

        private static string FallbackMsg(string preferred, string fallback)
            => string.IsNullOrEmpty(preferred) ? fallback : preferred;

        private static string OpenTimeSuffix(ServerListZone z)
        {
            if (z.open_time <= 0) return string.Empty;
            var t = DateTimeOffset.FromUnixTimeSeconds(z.open_time).ToLocalTime();
            return $",{t:MM-dd HH:mm} 开服";
        }

        private static Color StatusDotColor(ServerListZone z) => z.status switch
        {
            "MAINTENANCE" => DotMaintenance,
            "CLOSED"      => DotClosed,
            "PREVIEW"     => DotPreview,
            _ => z.load_level switch
            {
                "FULL" => DotFull,
                "BUSY" => DotBusy,
                _      => DotSmooth,
            },
        };

        private static string ZoneSubtitle(ServerListZone z)
        {
            switch (z.status)
            {
                case "MAINTENANCE": return FallbackMsg(z.maintenance_msg, "维护中");
                case "CLOSED":      return FallbackMsg(z.maintenance_msg, "已关闭");
                case "PREVIEW":     return $"未开放{OpenTimeSuffix(z)}";
            }
            string load = z.load_level switch
            {
                "FULL"  => "爆满",
                "BUSY"  => "繁忙",
                "SMOOTH"=> "流畅",
                _       => "在线",
            };
            var tags = new List<string> { load };
            if (z.recommended) tags.Add("推荐");
            if (z.is_new) tags.Add("新服");
            return string.Join(" · ", tags);
        }

        private void RefreshFontReferences()
        {
            var font = QdaoUguiTheme.ResolveFont();
            if (font == null)
                return;

            foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                text.font = font;
        }

        private void SetStatus(string value)
        {
            if (_statusText != null)
                _statusText.text = value ?? string.Empty;
            // The blocker dims the bottom-bar status line, so credential
            // feedback is mirrored into the dialog itself.
            if (_credentialStatusText != null)
                _credentialStatusText.text = value ?? string.Empty;
        }

        private void ValidatePrefabReferences()
        {
            if (!ReferencesValid())
                throw new MissingReferenceException(
                    "QdaoServerSelectView prefab is incomplete. Rebuild it with " +
                    "MMORPG/UI/Build qdao uGUI vertical slice before entering Play Mode.");
        }

        private bool ReferencesValid()
        {
            var invalid = _screenArtSprite == null ||
                          _statusDotSprite == null || _credentialSprite == null ||
                          _credentialCancelSprite == null || _credentialSubmitSprite == null ||
                          _credentialTitleText == null || _credentialStatusText == null ||
                          _contentRoot == null ||
                          _backdropImage == null || _backdropImage.raycastTarget ||
                          _screenArtImage == null || _screenArtImage.sprite == null ||
                          _credentialPanel == null ||
                          _credentialBlockerImage == null || !_credentialBlockerImage.raycastTarget ||
                          _credentialImage == null || _credentialImage.sprite == null ||
                          _credentialCancelButton == null || _credentialSubmitButton == null ||
                          _credentialCancelText == null || _credentialSubmitText == null ||
                          _accountInput == null || _passwordInput == null ||
                          _searchInput == null ||
                          _topImages == null || _topImages.Length != TopTabLabels.Length ||
                          _topSelectionMarks == null || _topSelectionMarks.Length != TopTabLabels.Length ||
                          _topButtons == null || _topButtons.Length != TopTabLabels.Length ||
                          _topLabels == null || _topLabels.Length != TopTabLabels.Length ||
                          _topDefaultDimmer == null ||
                          _categoryImages == null || _categoryImages.Length != CategoryLabels.Length ||
                          _categorySelectionMarks == null || _categorySelectionMarks.Length != CategoryLabels.Length ||
                          _categoryButtons == null || _categoryButtons.Length != CategoryLabels.Length ||
                          _categoryTexts == null || _categoryTexts.Length != CategoryLabels.Length ||
                          _categoryDefaultDimmer == null ||
                          _serverCardImages == null || _serverCardImages.Length != PageSize ||
                          _serverButtons == null || _serverButtons.Length != PageSize ||
                          _serverNames == null || _serverNames.Length != PageSize ||
                          _serverSubtitles == null || _serverSubtitles.Length != PageSize ||
                          _serverDots == null || _serverDots.Length != PageSize ||
                          _serverEmptyCovers == null || _serverEmptyCovers.Length != PageSize ||
                          _prevPageButton == null || _nextPageButton == null ||
                          _prevPageText == null || _nextPageText == null || _pageText == null ||
                          _lastLoginText == null || _selectedText == null || _refreshText == null ||
                          _enterText == null || _statusText == null || _backButton == null ||
                          _refreshButton == null || _enterButton == null;

            if (!invalid)
            {
                invalid = Array.Exists(_topImages, value => value == null) ||
                          Array.Exists(_topSelectionMarks, value => value == null) ||
                          Array.Exists(_topButtons, value => value == null) ||
                          Array.Exists(_topLabels, value => value == null) ||
                          Array.Exists(_categoryImages, value => value == null) ||
                          Array.Exists(_categorySelectionMarks, value => value == null) ||
                          Array.Exists(_categoryButtons, value => value == null) ||
                          Array.Exists(_categoryTexts, value => value == null) ||
                          Array.Exists(_serverCardImages, value => value == null) ||
                          Array.Exists(_serverButtons, value => value == null) ||
                          Array.Exists(_serverNames, value => value == null) ||
                          Array.Exists(_serverSubtitles, value => value == null) ||
                          Array.Exists(_serverDots, value => value == null || value.sprite == null) ||
                          Array.Exists(_serverEmptyCovers, value => value == null) ||
                          !ButtonsTargetImages(_topButtons, _topImages) ||
                          !ButtonsTargetImages(_categoryButtons, _categoryImages) ||
                          !ButtonsTargetImages(_serverButtons, _serverCardImages) ||
                          !HasRaycastTarget(_backButton) ||
                          !HasRaycastTarget(_prevPageButton) ||
                          !HasRaycastTarget(_nextPageButton) ||
                          !HasRaycastTarget(_refreshButton) ||
                          !HasRaycastTarget(_enterButton) ||
                          !HasRaycastTarget(_credentialCancelButton) ||
                          !HasRaycastTarget(_credentialSubmitButton);
            }

            return !invalid;
        }

        private static bool ButtonsTargetImages(Button[] buttons, Image[] images)
        {
            if (buttons == null || images == null || buttons.Length != images.Length)
                return false;
            for (var i = 0; i < buttons.Length; i++)
                if (buttons[i] == null || images[i] == null ||
                    buttons[i].targetGraphic != images[i] || !images[i].raycastTarget)
                    return false;
            return true;
        }

        private static bool HasRaycastTarget(Button button)
            => button != null && button.targetGraphic != null && button.targetGraphic.raycastTarget;
    }
}
