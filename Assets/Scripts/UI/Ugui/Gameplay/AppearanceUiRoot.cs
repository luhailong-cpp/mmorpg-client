using System;
using System.Collections.Generic;
using MmorpgClient.Game;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Battle;
using MmorpgClient.Table;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Jubaozhai;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 角色形象切换（商城 10 元角色形象卡）、1 小时免费试用（bit 存储）与人物背景故事阅读窗口。
    /// 网络与资产状态唯一真相在 <see cref="AppearanceClient"/>；候选人物档案与交互状态机在 <see cref="AppearanceBiographyCatalog"/> / <see cref="AppearancePanelState"/>。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed partial class AppearanceUiRoot : MonoBehaviour
    {
        public static AppearanceUiRoot Instance { get; private set; }
        public AppearanceClient Client => _client;
        public bool IsPanelVisible => _window != null && _window.gameObject.activeSelf;

        public const int CanvasSortingOrder = 184;
        public const string EntryName = "AppearanceEntry";
        public const string EntryText = "易容形象";

        private const float BadgeHeight = 52f;
        private const float StoryViewportWidth = 1144f;
        private const float StoryViewportHeight = 500f;

        private sealed class RowView
        {
            public string AppearanceId;
            public GameObject Root;
            public Button Button;
            public Image CardBackground;
            public Image AvatarImage;
            public TMP_Text AvatarMissingText;
            public TMP_Text NameText;
            public TMP_Text SubText;
            public Image BadgeBackground;
            public TMP_Text BadgeLabel;
            public string LoadedAvatarId;
        }

        private GameClient _game;
        private AppearanceClient _client;
        private bool _externalVerificationClient;
        private Func<string, Sprite> _portraitLoaderOverride;

        private readonly AppearancePanelState _state = new();
        private readonly List<RowView> _rows = new();
        private readonly List<string> _validIdsBuffer = new();
        private string _listSignature = "";

        private Canvas _canvas;
        private RectTransform _hud;
        private RectTransform _window;
        private RectTransform _fittedRoot;
        private AppearanceWindowFit _windowFit;
        private CanvasGroup _mainPanelGroup;
        private Button _entry;

        // 左侧形象列表
        private TMP_Text _listCountLabel;
        private ScrollRect _listScroll;
        private RectTransform _listContent;

        // 中部立绘台与试用倒计时横幅
        private Image _previewPortrait;
        private GameObject _previewMissingBox;
        private GameObject _trialBannerRoot;
        private TMP_Text _trialCountdownText;
        private Button _cancelTrialBtn;
        private string _loadedPreviewPortraitId = "";
        private Sprite _loadedPreviewPortraitSprite;

        // 右侧人物详情、背景与操作
        private TMP_Text _previewName;
        private TMP_Text _previewArchiveTag;
        private TMP_Text _previewDesc;
        private TMP_Text _previewStatus;
        private GameObject _biographySection;
        private TMP_Text _biographyIdentity;
        private TMP_Text _biographyBody;
        private Button _readStoryBtn;
        private TMP_Text _cardInfoLine1;
        private TMP_Text _cardInfoLine2;
        private Button _buyMallBtn;
        private TMP_Text _buyMallLabel;
        private Button _trialBtn;
        private Image _trialBtnImage;
        private TMP_Text _trialLabel;
        private Button _switchBtn;
        private TMP_Text _switchLabel;
        private TMP_Text _status;

        // 弹窗 1：消耗角色形象卡双头像二次确认（03）
        private GameObject _confirmOverlay;
        private Image _confirmOldPortrait;
        private GameObject _confirmOldMissing;
        private TMP_Text _confirmOldName;
        private Image _confirmNewPortrait;
        private GameObject _confirmNewMissing;
        private TMP_Text _confirmNewName;
        private TMP_Text _confirmBodyText;
        private Button _confirmCancelBtn;
        private Button _confirmSubmitBtn;
        private Button _confirmCloseBtn;
        private Selectable[] _confirmControls;

        // 弹窗 2：背包形象卡不足跳转官方商城（04）
        private GameObject _noCardOverlay;
        private TMP_Text _noCardPriceBadgeLabel;
        private TMP_Text _noCardOwnedLabel;
        private Button _noCardCancelBtn;
        private Button _noCardMallBtn;
        private Button _noCardCloseBtn;
        private Selectable[] _noCardControls;

        // 弹层 3：人物故事阅读层
        private GameObject _storyOverlay;
        private TMP_Text _storyTitleText;
        private TMP_Text _storyCharacterText;
        private ScrollRect _storyScroll;
        private RectTransform _storyContent;
        private TMP_Text _storyRecapLabel;
        private RectTransform _storyDivider;
        private TMP_Text _storyBodyText;
        private Button _storyBackBtn;
        private Button _storySkipBtn;
        private Button _storyFinishBtn;
        private Selectable[] _storyControls;

        private bool _available;
        private ulong _lastCountdownSeconds = ulong.MaxValue;
        private bool _lastCountdownActive;
        private int _lastEscapeHandledFrame = -1;
        private float _lastStoryBodyFontSize;
        private float _lastStoryViewportWidth = -1f, _lastStoryViewportHeight = -1f;
        private string _lastStoryRecap, _lastStoryBody;

#if UNITY_EDITOR
        // 供 Editor 离线可视化验收与单测直接核验真实控件树，不伪造业务状态，正式包零编译残留。
        public AppearancePanelState PanelState => _state;
        public Canvas RootCanvas => _canvas;
        public RectTransform WindowRoot => _window;
        public RectTransform FittedRoot => _fittedRoot;
        public ScrollRect ListScroll => _listScroll;
        public ScrollRect StoryScroll => _storyScroll;
        public TMP_Text StoryBodyText => _storyBodyText;
        public GameObject BiographySection => _biographySection;
        public GameObject StoryOverlay => _storyOverlay;
        public GameObject ConfirmOverlay => _confirmOverlay;
        public GameObject NoCardOverlay => _noCardOverlay;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[AppearanceUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<AppearanceUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);
            EnsureBuilt();
        }

        private void EnsureBuilt()
        {
            if (_canvas != null) return;
            var canvasObject = new GameObject("AppearanceCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            _canvas = canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var hudDesign = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", hudDesign, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            _entry = AppearanceUiArt.ActionButton(_hud, EntryName, EntryText, BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(9),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, TogglePanel, true, fontSize: 32);
            _entry.name = EntryName;
            WorldHudNavigation.Register(WorldHudEntry.Appearance, (RectTransform)_entry.transform, _entry,
                _entry.GetComponentInChildren<TMP_Text>(true));

            BuildWindow(canvasObject.transform);
            _hud.gameObject.SetActive(false);
        }

#if UNITY_EDITOR
        public void AttachVerificationClient(AppearanceClient client, Func<string, Sprite> portraitLoaderOverride = null)
        {
            EnsureBuilt();
            Unbind();
            _externalVerificationClient = true;
            _portraitLoaderOverride = portraitLoaderOverride;
            _client = client;
            if (_client != null)
            {
                _client.Changed += Render;
                _client.Notice += HandleNotice;
            }
            _available = _client != null && _client.IsSupported;
            _loadedPreviewPortraitId = "";
            _loadedPreviewPortraitSprite = null;
            _listSignature = "";
            Render();
        }

        public void SetExtraSafeInsetsForVerification(Vector4 insetsPx)
        {
            if (_windowFit == null) return;
            _windowFit.extraSafeInsetsPx = insetsPx;
            _windowFit.Fit();
        }

        public void RefreshStoryLayoutForVerification()
        {
            RefreshStoryScrollLayout();
        }
#endif

        private void BuildWindow(UnityEngine.Transform canvasRoot)
        {
            _window = QdaoUguiFactory.CreateStretch("AppearanceWindow", canvasRoot, Vector4.zero);
            var shade = _window.gameObject.AddComponent<Image>();
            shade.color = Color.clear;
            shade.raycastTarget = true;
            _window.gameObject.AddComponent<GameplayInputBlocker>();

            _fittedRoot = QdaoUguiFactory.CreateCenteredRect("AppearanceFittedRoot", _window,
                AppearanceUiArt.FrameWidth, AppearanceUiArt.FrameHeight);
            _windowFit = _fittedRoot.gameObject.AddComponent<AppearanceWindowFit>();
            _windowFit.referenceSize = new Vector2(AppearanceUiArt.FrameWidth, AppearanceUiArt.FrameHeight);

            BuildMainPanel(_fittedRoot);
            BuildConfirmOverlay(_fittedRoot);
            BuildNoCardOverlay(_fittedRoot);
            BuildStoryOverlay(_fittedRoot);
            BuildSeriesUi((RectTransform)_mainPanelGroup.transform, _fittedRoot);
            BuildReadingOverlays(_fittedRoot);

            _window.gameObject.SetActive(false);
        }

        private void BuildMainPanel(RectTransform parent)
        {
            var mainRoot = QdaoUguiFactory.CreateRect("AppearanceMainPanel", parent,
                0f, AppearanceUiArt.MainBodyTopOffset, AppearanceUiArt.FrameWidth, AppearanceUiArt.MainBodyHeight);
            _mainPanelGroup = mainRoot.gameObject.AddComponent<CanvasGroup>();

            AppearanceUiArt.Frame(mainRoot, "AppearanceMainFrame", 0f, 0f,
                AppearanceUiArt.FrameWidth, AppearanceUiArt.MainBodyHeight);

            // 顶部标题牌 + 太极徽记 + 关闭按钮 + 规则说明
            var titlePlate = AppearanceUiArt.TitlePlate(mainRoot, "AppearanceTitlePlate",
                722f, -60f, 716f, 145f);
            var mainTitle = QdaoUguiFactory.CreateText("TitleMain", titlePlate.transform,
                68f, 26f, 580f, 50f, "易 容", 42f, AppearanceUiArt.WarmCream, TextAlignmentOptions.Center);
            mainTitle.richText = false;
            mainTitle.fontStyle = FontStyles.Bold;
            var subTitle = QdaoUguiFactory.CreateText("TitleSub", titlePlate.transform,
                68f, 82f, 580f, 28f, "形象与人物志", 20f, AppearanceUiArt.WarmCream, TextAlignmentOptions.Center);
            subTitle.richText = false;

            var close = AppearanceUiArt.CloseButton(mainRoot, "AppearanceClose",
                AppearanceUiArt.FrameWidth - 96f, 18f, 76f, HidePanel);
            close.navigation = new Navigation { mode = Navigation.Mode.Automatic };

            var ruleBanner = QdaoUguiFactory.CreateText("AppearanceRuleBanner", mainRoot,
                180f, 92f, 1800f, 34f,
                "已拥有的形象可随时免费切换；未拥有且支持兑换的形象消耗 1 张角色形象卡永久解锁（保留原形象）；未拥有可免费试用 1 小时",
                22f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            ruleBanner.richText = false;

            // 左栏：可选形象滚动列表
            AppearanceUiArt.BorderedParchmentCard(mainRoot, "AppearanceListPanel", 56f, 140f, 638f, 742f,
                AppearanceUiArt.ParchmentInner, AppearanceUiArt.ParchmentBorder);
            var listHeader = AppearanceUiArt.SectionHeader(mainRoot, "AppearanceListHeader",
                68f, 148f, 340f, 52f);
            _listCountLabel = QdaoUguiFactory.CreateText("AppearanceListCount", listHeader.transform,
                38f, 0f, 264f, 52f, "可选形象", 22f, AppearanceUiArt.WarmCream, TextAlignmentOptions.Center);
            _listCountLabel.richText = false;
            _listCountLabel.fontStyle = FontStyles.Bold;
            _listCountLabel.enableAutoSizing = true;
            _listCountLabel.fontSizeMin = 15f;
            _listCountLabel.fontSizeMax = 22f;

            var listViewport = QdaoUguiFactory.CreateRect("AppearanceListViewport", mainRoot, 68f, 210f, 594f, 660f);
            var listHit = listViewport.gameObject.AddComponent<Image>();
            listHit.color = new Color(1f, 1f, 1f, 0.001f);
            listHit.raycastTarget = true;
            listViewport.gameObject.AddComponent<RectMask2D>();
            _listContent = QdaoUguiFactory.CreateRect("AppearanceListContent", listViewport, 0f, 0f, 594f, 660f);
            _listScroll = listViewport.gameObject.AddComponent<ScrollRect>();
            _listScroll.viewport = listViewport;
            _listScroll.content = _listContent;
            _listScroll.horizontal = false;
            _listScroll.vertical = true;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            _listScroll.scrollSensitivity = 48f;
            AppearanceUiArt.AddVerticalScrollbar(_listScroll, mainRoot, "AppearanceListScrollbar", 668f, 210f, 12f, 660f);

            // 中栏：独立立绘展示台（fixed_size_only 640×720 等比）+ 实时角色立绘 + 试用倒计时条
            AppearanceUiArt.FixedArt(mainRoot, "AppearancePreviewPedestal",
                AppearanceUiArt.LoadAppearance("appearance_preview_pedestal"),
                719f, 184f, 586f, 660f, AppearanceUiArt.ParchmentCard, preserveAspect: true);

            _previewPortrait = QdaoUguiFactory.CreateImage("AppearancePreviewPortrait", mainRoot,
                774f, 224f, 476f, 536f, null);
            _previewPortrait.preserveAspect = true;
            _previewPortrait.enabled = false;

            var missingCard = AppearanceUiArt.BorderedParchmentCard(mainRoot, "AppearancePortraitMissing",
                832f, 386f, 360f, 144f, AppearanceUiArt.PortraitPlaceholderBg, AppearanceUiArt.ParchmentBorder);
            var missingText = QdaoUguiFactory.CreateText("MissingLabel", missingCard.transform,
                20f, 18f, 320f, 108f, "暂无该角色立绘\n背景与故事仍可正常浏览", 24f,
                AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            missingText.richText = false;
            missingText.textWrappingMode = TextWrappingModes.Normal;
            _previewMissingBox = missingCard.gameObject;
            _previewMissingBox.SetActive(false);

            var trialBar = AppearanceUiArt.HorizontalBar(mainRoot, "AppearanceTrialBanner",
                AppearanceUiArt.LoadAppearance("appearance_trial_banner"),
                698f, 138f, 626f, 76f, AppearanceUiArt.DeepJade);
            _trialCountdownText = QdaoUguiFactory.CreateText("TrialCountdownLabel", trialBar.transform,
                112f, 12f, 264f, 52f, "", 20f, AppearanceUiArt.WarmCream, TextAlignmentOptions.MidlineLeft);
            _trialCountdownText.richText = false;
            _trialCountdownText.fontStyle = FontStyles.Bold;
            _trialCountdownText.enableAutoSizing = true;
            _trialCountdownText.fontSizeMin = 15f;
            _trialCountdownText.fontSizeMax = 20f;
            _cancelTrialBtn = AppearanceUiArt.ActionButton(trialBar.transform, "AppearanceCancelTrial",
                "恢复原形象", 382f, 16f, 130f, 44f, HandleCancelTrialClicked, primary: false, fontSize: 18f);
            _trialBannerRoot = trialBar.gameObject;
            _trialBannerRoot.SetActive(false);

            // 右栏：人物标题、描述、状态、新增「人物背景 + 读故事」、角色形象卡槽、底部双按钮
            _previewName = QdaoUguiFactory.CreateText("AppearancePreviewName", mainRoot,
                1340f, 144f, 444f, 54f, "请选择角色形象", 40f, AppearanceUiArt.DeepJade, TextAlignmentOptions.MidlineLeft);
            _previewName.richText = false;
            _previewName.fontStyle = FontStyles.Bold;
            _previewName.textWrappingMode = TextWrappingModes.Normal;
            _previewName.enableAutoSizing = true;
            _previewName.fontSizeMin = 22f;
            _previewName.fontSizeMax = 40f;

            _previewArchiveTag = QdaoUguiFactory.CreateText("AppearanceArchiveTag", mainRoot,
                1788f, 156f, 304f, 38f, "", 23f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineRight);
            _previewArchiveTag.richText = false;

            _previewDesc = QdaoUguiFactory.CreateText("AppearancePreviewDesc", mainRoot,
                1340f, 202f, 752f, 32f, "", 23f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
            _previewDesc.richText = false;

            _previewStatus = QdaoUguiFactory.CreateText("AppearancePreviewStatus", mainRoot,
                1340f, 238f, 752f, 36f, "", 23f, AppearanceUiArt.TrialGreen, TextAlignmentOptions.MidlineLeft);
            _previewStatus.richText = false;

            // 新增：人物背景摘要区（无档案形象时完整收起）
            var bioCard = AppearanceUiArt.BorderedParchmentCard(mainRoot, "AppearanceBiographySection",
                1338f, 282f, 756f, 228f, AppearanceUiArt.ParchmentInner, AppearanceUiArt.ParchmentBorder);
            var bioTitle = QdaoUguiFactory.CreateText("BiographyHeading", bioCard.transform,
                22f, 14f, 140f, 34f, "人物背景", 26f, AppearanceUiArt.DeepJade, TextAlignmentOptions.MidlineLeft);
            bioTitle.richText = false;
            bioTitle.fontStyle = FontStyles.Bold;

            _biographyIdentity = QdaoUguiFactory.CreateText("BiographyIdentity", bioCard.transform,
                164f, 18f, 392f, 28f, "", 21f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
            _biographyIdentity.richText = false;

            _readStoryBtn = AppearanceUiArt.ActionButton(bioCard.transform, "AppearanceReadStory",
                "读故事", 572f, 10f, 164f, 46f, HandleReadStoryClicked, primary: false, fontSize: 23f);

            var bioDivider = QdaoUguiFactory.CreateImage("BiographyDivider", bioCard.transform,
                22f, 58f, 712f, 2f, null);
            bioDivider.color = new Color(AppearanceUiArt.ParchmentBorder.r, AppearanceUiArt.ParchmentBorder.g,
                AppearanceUiArt.ParchmentBorder.b, 0.55f);

            _biographyBody = QdaoUguiFactory.CreateText("BiographyBody", bioCard.transform,
                22f, 66f, 712f, 150f, "", 22f, AppearanceUiArt.JadeInk, TextAlignmentOptions.TopLeft);
            _biographyBody.richText = false;
            _biographyBody.textWrappingMode = TextWrappingModes.Normal;
            _biographyBody.overflowMode = TextOverflowModes.Ellipsis;
            _biographyBody.lineSpacing = 6f;

            _biographySection = bioCard.gameObject;
            _biographySection.SetActive(false);

            // 角色形象卡持有槽（fixed_size_only 760×136 等比）
            var cardSlot = AppearanceUiArt.FixedArt(mainRoot, "AppearanceCardSlot",
                AppearanceUiArt.LoadAppearance("appearance_card_slot"),
                1338f, 522f, 756f, 136f, AppearanceUiArt.ParchmentCard, preserveAspect: true);

            AppearanceUiArt.FixedArt(cardSlot.transform, "AppearanceCardIcon",
                AppearanceUiArt.LoadCardIcon(), 22f, 20f, 96f, 96f, Color.white, preserveAspect: true);

            _cardInfoLine1 = QdaoUguiFactory.CreateText("CardOwnedLine", cardSlot.transform,
                136f, 24f, 386f, 40f, "角色形象卡  背包持有：—", 25f, AppearanceUiArt.JadeInk, TextAlignmentOptions.MidlineLeft);
            _cardInfoLine1.richText = false;
            _cardInfoLine1.fontStyle = FontStyles.Bold;

            _cardInfoLine2 = QdaoUguiFactory.CreateText("CardRuleLine", cardSlot.transform,
                136f, 68f, 388f, 46f, "消耗 1 张永久解锁新形象（保留原形象，已拥有免费切换）", 19f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
            _cardInfoLine2.richText = false;
            _cardInfoLine2.textWrappingMode = TextWrappingModes.Normal;
            _cardInfoLine2.enableAutoSizing = true;
            _cardInfoLine2.fontSizeMin = 14f;
            _cardInfoLine2.fontSizeMax = 19f;

            _buyMallBtn = AppearanceUiArt.GoldMallButton(cardSlot.transform, "AppearanceOpenMall",
                "商城购买 ¥10", 534f, 34f, 202f, 68f, HandleOpenMallClicked, fontSize: 23f);
            _buyMallLabel = _buyMallBtn.GetComponentInChildren<TMP_Text>(true);

            // 底部双主操作按钮（固定位置，绝不被人物背景挤压）
            _trialBtn = AppearanceUiArt.ActionButton(mainRoot, "AppearanceStartTrial",
                "免费试用 1 小时", 1338f, 676f, 314f, 78f, HandleStartTrialClicked, primary: false, fontSize: 26f);
            _trialBtnImage = _trialBtn.GetComponent<Image>();
            _trialLabel = _trialBtn.GetComponentInChildren<TMP_Text>(true);

            _switchBtn = AppearanceUiArt.ActionButton(mainRoot, "AppearanceConfirmSwitch",
                "切换永久形象（耗1张卡）", 1670f, 676f, 424f, 78f, HandleSwitchClicked, primary: true, fontSize: 26f);
            _switchLabel = _switchBtn.GetComponentInChildren<TMP_Text>(true);

            _status = QdaoUguiFactory.CreateText("AppearanceStatus", mainRoot,
                1340f, 772f, 752f, 74f, "", 22f, AppearanceUiArt.MutedInk, TextAlignmentOptions.TopLeft);
            _status.richText = false;
            _status.textWrappingMode = TextWrappingModes.Normal;
        }

        private static RectTransform CreateOverlayBackdrop(RectTransform parent, string name)
        {
            var root = QdaoUguiFactory.CreateRect(name, parent,
                0f, 0f, AppearanceUiArt.FrameWidth, AppearanceUiArt.FrameHeight);
            var blocker = root.gameObject.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.001f);
            blocker.raycastTarget = true;
            var dim = QdaoUguiFactory.CreateImage("WindowDim", root,
                16f, AppearanceUiArt.MainBodyTopOffset + 12f,
                AppearanceUiArt.FrameWidth - 32f, AppearanceUiArt.MainBodyHeight - 24f, null);
            dim.color = AppearanceUiArt.OverlayShade;
            dim.raycastTarget = true;
            return root;
        }

        private void BuildConfirmOverlay(RectTransform parent)
        {
            var root = CreateOverlayBackdrop(parent, "AppearanceConfirmOverlay");

            const float w = 980f, h = 560f;
            var box = QdaoUguiFactory.CreateCenteredRect("ConfirmDialogFrame", root, w, h);
            AppearanceUiArt.Frame(box, "ConfirmFrameBg", 0f, 0f, w, h);

            var titlePlate = AppearanceUiArt.FixedArt(box, "ConfirmTitlePlate",
                AppearanceUiArt.LoadGameplay("title_plate"), (w - 420f) * 0.5f, -24f, 420f, 84f,
                AppearanceUiArt.DeepJade, preserveAspect: false);
            var title = QdaoUguiFactory.CreateText("ConfirmTitle", titlePlate.transform,
                20f, 16f, 380f, 50f, "确认易容", 34f, AppearanceUiArt.WarmCream, TextAlignmentOptions.Center);
            title.richText = false;
            title.fontStyle = FontStyles.Bold;
            _confirmCloseBtn = AppearanceUiArt.CloseButton(box, "AppearanceConfirmClose", w - 80f, 14f, 64f, HandleDismissOverlay);

            // 左右双头像对比
            var oldFrame = AppearanceUiArt.BorderedParchmentCard(box, "OldPortraitCard", 148f, 88f, 190f, 190f,
                AppearanceUiArt.ParchmentInner, AppearanceUiArt.ParchmentBorder);
            _confirmOldPortrait = QdaoUguiFactory.CreateImage("OldPortrait", oldFrame.transform, 10f, 10f, 170f, 170f, null);
            _confirmOldPortrait.preserveAspect = true;
            var oldMiss = QdaoUguiFactory.CreateText("OldMissing", oldFrame.transform, 10f, 68f, 170f, 54f,
                "暂无立绘", 22f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            oldMiss.richText = false;
            _confirmOldMissing = oldMiss.gameObject;
            _confirmOldName = QdaoUguiFactory.CreateText("OldName", box, 98f, 286f, 290f, 36f,
                "", 23f, AppearanceUiArt.JadeInk, TextAlignmentOptions.Center);
            _confirmOldName.richText = false;

            var arrow = QdaoUguiFactory.CreateText("SwitchArrow", box, 420f, 148f, 140f, 70f,
                "→", 56f, QdaoUguiTheme.Html("#B28436"), TextAlignmentOptions.Center);
            arrow.richText = false;
            arrow.fontStyle = FontStyles.Bold;

            var newFrame = AppearanceUiArt.BorderedParchmentCard(box, "NewPortraitCard", 642f, 88f, 190f, 190f,
                AppearanceUiArt.ParchmentInner, AppearanceUiArt.ParchmentBorder);
            _confirmNewPortrait = QdaoUguiFactory.CreateImage("NewPortrait", newFrame.transform, 10f, 10f, 170f, 170f, null);
            _confirmNewPortrait.preserveAspect = true;
            var newMiss = QdaoUguiFactory.CreateText("NewMissing", newFrame.transform, 10f, 68f, 170f, 54f,
                "暂无立绘", 22f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            newMiss.richText = false;
            _confirmNewMissing = newMiss.gameObject;
            _confirmNewName = QdaoUguiFactory.CreateText("NewName", box, 592f, 286f, 290f, 36f,
                "", 23f, AppearanceUiArt.DeepJade, TextAlignmentOptions.Center);
            _confirmNewName.richText = false;

            _confirmBodyText = QdaoUguiFactory.CreateText("ConfirmBody", box, 88f, 336f, w - 176f, 100f,
                "", 24f, AppearanceUiArt.JadeInk, TextAlignmentOptions.Center);
            _confirmBodyText.richText = false;
            _confirmBodyText.textWrappingMode = TextWrappingModes.Normal;
            _confirmBodyText.lineSpacing = 8f;

            _confirmCancelBtn = AppearanceUiArt.ActionButton(box, "AppearanceConfirmCancel",
                "再想想", 170f, 454f, 280f, 68f, HandleDismissOverlay, primary: false, fontSize: 26f);
            _confirmSubmitBtn = AppearanceUiArt.ActionButton(box, "AppearanceConfirmSubmit",
                "确认切换", 530f, 454f, 280f, 68f, HandleConfirmSwitchSubmit, primary: true, fontSize: 26f);

            _confirmControls = new Selectable[] { _confirmCancelBtn, _confirmSubmitBtn, _confirmCloseBtn };
            _confirmOverlay = root.gameObject;
            _confirmOverlay.SetActive(false);
        }

        private void BuildNoCardOverlay(RectTransform parent)
        {
            var root = CreateOverlayBackdrop(parent, "AppearanceNoCardOverlay");

            const float w = 920f, h = 520f;
            var box = QdaoUguiFactory.CreateCenteredRect("NoCardDialogFrame", root, w, h);
            AppearanceUiArt.Frame(box, "NoCardFrameBg", 0f, 0f, w, h);

            var titlePlate = AppearanceUiArt.FixedArt(box, "NoCardTitlePlate",
                AppearanceUiArt.LoadGameplay("title_plate"), (w - 420f) * 0.5f, -24f, 420f, 84f,
                AppearanceUiArt.DeepJade, preserveAspect: false);
            var title = QdaoUguiFactory.CreateText("NoCardTitle", titlePlate.transform,
                20f, 16f, 380f, 50f, "形象卡不足", 34f, AppearanceUiArt.WarmCream, TextAlignmentOptions.Center);
            title.richText = false;
            title.fontStyle = FontStyles.Bold;

            _noCardCloseBtn = AppearanceUiArt.CloseButton(box, "AppearanceNoCardClose", w - 80f, 14f, 64f, HandleDismissOverlay);

            var showcase = AppearanceUiArt.BorderedParchmentCard(box, "NoCardShowcase", 120f, 94f, 680f, 196f,
                AppearanceUiArt.ParchmentInner, AppearanceUiArt.ParchmentBorder);
            AppearanceUiArt.FixedArt(showcase.transform, "NoCardIcon",
                AppearanceUiArt.LoadCardIcon(), 44f, 32f, 132f, 132f, Color.white, preserveAspect: true);

            var itemName = QdaoUguiFactory.CreateText("NoCardItemName", showcase.transform,
                208f, 24f, 420f, 44f, "角色形象卡", 32f, AppearanceUiArt.DeepJade, TextAlignmentOptions.MidlineLeft);
            itemName.richText = false;
            itemName.fontStyle = FontStyles.Bold;

            var priceTag = AppearanceUiArt.HorizontalBar(showcase.transform, "NoCardPriceBadge",
                AppearanceUiArt.LoadAppearance("appearance_badge_trialing"),
                208f, 78f, 300f, 48f, AppearanceUiArt.CrimsonInk);
            _noCardPriceBadgeLabel = QdaoUguiFactory.CreateText("NoCardPriceLabel", priceTag.transform,
                16f, 0f, 268f, 48f, "官方商城 · 10元 / 张", 22f, AppearanceUiArt.WarmCream, TextAlignmentOptions.Center);
            _noCardPriceBadgeLabel.richText = false;

            _noCardOwnedLabel = QdaoUguiFactory.CreateText("NoCardOwned", showcase.transform,
                208f, 136f, 420f, 36f, "背包当前持有：0 张", 23f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
            _noCardOwnedLabel.richText = false;

            var note = QdaoUguiFactory.CreateText("NoCardNote", box, 90f, 314f, w - 180f, 76f,
                "背包中暂无角色形象卡，前往官方商城购买后即可永久解锁并切换角色形象。",
                24f, AppearanceUiArt.JadeInk, TextAlignmentOptions.Center);
            note.richText = false;
            note.textWrappingMode = TextWrappingModes.Normal;

            _noCardCancelBtn = AppearanceUiArt.ActionButton(box, "AppearanceNoCardCancel",
                "取消", 150f, 412f, 260f, 68f, HandleDismissOverlay, primary: false, fontSize: 26f);
            _noCardMallBtn = AppearanceUiArt.GoldMallButton(box, "AppearanceNoCardGoMall",
                "前往官方商城", 480f, 412f, 300f, 68f, HandleOpenMallClicked, fontSize: 26f);

            _noCardControls = new Selectable[] { _noCardCancelBtn, _noCardMallBtn, _noCardCloseBtn };
            _noCardOverlay = root.gameObject;
            _noCardOverlay.SetActive(false);
        }

        private void BuildStoryOverlay(RectTransform parent)
        {
            var root = CreateOverlayBackdrop(parent, "AppearanceStoryOverlay");

            const float w = 1360f, h = 820f;
            var box = QdaoUguiFactory.CreateCenteredRect("StoryReaderFrame", root, w, h);
            AppearanceUiArt.Frame(box, "StoryFrameBg", 0f, 0f, w, h);

            // 顶部：返回、人物名与故事篇名、独立跳过按钮
            _storyBackBtn = AppearanceUiArt.ActionButton(box, "AppearanceStoryBack",
                "返回", 76f, 46f, 160f, 60f, () => HandleCloseStory(AppearanceStoryCloseReason.Back),
                primary: false, fontSize: 25f);

            _storyTitleText = QdaoUguiFactory.CreateText("StoryTitle", box,
                256f, 32f, 848f, 52f, "", 38f, AppearanceUiArt.DeepJade, TextAlignmentOptions.Center);
            _storyTitleText.richText = false;
            _storyTitleText.fontStyle = FontStyles.Bold;

            _storyCharacterText = QdaoUguiFactory.CreateText("StoryCharacter", box,
                256f, 88f, 848f, 34f, "", 23f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            _storyCharacterText.richText = false;

            _storySkipBtn = AppearanceUiArt.ActionButton(box, "AppearanceStorySkip",
                "跳过", w - 236f, 46f, 160f, 60f, () => HandleCloseStory(AppearanceStoryCloseReason.Skip),
                primary: false, fontSize: 25f);

            // 中间：可纵向滚动故事正文（支持字体放大与触屏/滚轮滚动）
            AppearanceUiArt.BorderedParchmentCard(box, "StoryPaperCard", 84f, 154f, 1192f, 528f,
                AppearanceUiArt.ParchmentInner, AppearanceUiArt.ParchmentBorder);

            var viewport = QdaoUguiFactory.CreateRect("StoryViewport", box, 98f, 168f, StoryViewportWidth, StoryViewportHeight);
            var viewHit = viewport.gameObject.AddComponent<Image>();
            viewHit.color = new Color(1f, 1f, 1f, 0.001f);
            viewHit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            _storyContent = QdaoUguiFactory.CreateRect("StoryContent", viewport, 0f, 0f, StoryViewportWidth, StoryViewportHeight + 24f);
            _storyScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _storyScroll.viewport = viewport;
            _storyScroll.content = _storyContent;
            _storyScroll.horizontal = false;
            _storyScroll.vertical = true;
            _storyScroll.movementType = ScrollRect.MovementType.Clamped;
            _storyScroll.scrollSensitivity = 48f;
            AppearanceUiArt.AddVerticalScrollbar(_storyScroll, box, "StoryScrollbar", 1250f, 168f, 14f, StoryViewportHeight);

            _storyRecapLabel = QdaoUguiFactory.CreateText("StoryBackgroundRecap", _storyContent,
                36f, 24f, StoryViewportWidth - 72f, 96f, "", 23f, AppearanceUiArt.MutedInk, TextAlignmentOptions.TopLeft);
            _storyRecapLabel.richText = false;
            _storyRecapLabel.textWrappingMode = TextWrappingModes.Normal;
            _storyRecapLabel.overflowMode = TextOverflowModes.Overflow;
            _storyRecapLabel.lineSpacing = 8f;

            var dividerImg = QdaoUguiFactory.CreateImage("StoryDivider", _storyContent,
                36f, 130f, StoryViewportWidth - 72f, 2f, null);
            dividerImg.color = new Color(AppearanceUiArt.ParchmentBorder.r, AppearanceUiArt.ParchmentBorder.g,
                AppearanceUiArt.ParchmentBorder.b, 0.6f);
            _storyDivider = dividerImg.rectTransform;

            _storyBodyText = QdaoUguiFactory.CreateText("StoryBody", _storyContent,
                36f, 154f, StoryViewportWidth - 72f, 340f, "", 30f, AppearanceUiArt.JadeInk, TextAlignmentOptions.TopLeft);
            _storyBodyText.richText = false;
            _storyBodyText.textWrappingMode = TextWrappingModes.Normal;
            _storyBodyText.overflowMode = TextOverflowModes.Overflow;
            _storyBodyText.lineSpacing = 14f;
            _storyBodyText.paragraphSpacing = 18f;
            _lastStoryBodyFontSize = _storyBodyText.fontSize;

            // 底部：读完返回
            _storyFinishBtn = AppearanceUiArt.ActionButton(box, "AppearanceStoryFinish",
                "读完返回", (w - 320f) * 0.5f, 706f, 320f, 68f,
                () => HandleCloseStory(AppearanceStoryCloseReason.Finished), primary: true, fontSize: 27f);

            _storyControls = new Selectable[] { _storyBackBtn, _storyFinishBtn, _storySkipBtn };
            _storyOverlay = root.gameObject;
            _storyOverlay.SetActive(false);
        }

        private void Update()
        {
            if (_window == null || _hud == null) { Unbind(); return; }
            if (!_externalVerificationClient)
            {
                Bind(AppBootstrap.Instance?.GameClient);
                ObserveReadingSession();
                bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
                _available = _game != null && _game.InGame && _game.IsGateReady && !battleLayer && (_client?.IsSupported ?? false);
                _client?.Tick();
                _hud.gameObject.SetActive(_available);
                if (!_available)
                {
                    HidePanel();
                    return;
                }
            }
            if (!IsPanelVisible) return;

            UpdateTrialCountdownText();
            if (_state.IsStoryOpen && _storyBodyText != null &&
                Mathf.Abs(_storyBodyText.fontSize - _lastStoryBodyFontSize) > 0.01f)
            {
                RefreshStoryScrollLayout();
            }
            if (_state.IsSeriesEpilogueOpen && _seriesEpilogueBodyText != null &&
                Mathf.Abs(_seriesEpilogueBodyText.fontSize - _lastEpilogueBodyFontSize) > 0.01f)
            {
                RefreshSeriesEpilogueScrollLayout();
            }
            HandleOverlayInputAndFocus();
            ApplyReadingLayout();
            ConfigureModalNavigation();
        }

        private void OnDisable()
        {
            Unbind();
            _state.ResetForRoleChange();
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            Unbind();
            if (Instance == this) Instance = null;
        }

        public void TogglePanel()
        {
            if (IsPanelVisible) HidePanel();
            else ShowPanel();
        }

        public void ShowPanel()
        {
            if (!_available || _window == null) return;
            CityPanelRegistry.HideOthers(this);
            _state.ResetForCloseOrDisconnect();
            _window.gameObject.SetActive(true);
            _windowFit?.Fit();
            if (!_externalVerificationClient) _client?.RequestInfo(force: true);
            Render();
        }

        public void HidePanel()
        {
            bool wasVisible = IsPanelVisible;
            SaveReadingPosition();
            if (wasVisible && !_state.HasOverlay && _listScroll != null && _listScroll.gameObject.activeInHierarchy)
                _state.SelectAppearance(_state.SelectedAppearanceId, _listScroll.verticalNormalizedPosition);
            _state.ResetForCloseOrDisconnect();
            SyncOverlayVisibility();
            if (_window != null) _window.gameObject.SetActive(false);
            ReleaseReadingPortraits();
            if (wasVisible) EventSystem.current?.SetSelectedGameObject(null);
        }

        private void HandleOpenMallClicked()
        {
            if (_state.IsReadOnlyOverlay || _client?.Info == null || !_client.Ready ||
                _client.Info.MallSkuId == 0 || _client.Info.MallPriceFen == 0) return;
            HidePanel();
            JubaozhaiUiRoot.Instance?.OpenOfficialMall();
        }

        private void HandleStartTrialClicked()
        {
            if (_state.HasOverlay || _client == null || string.IsNullOrEmpty(_state.SelectedAppearanceId)) return;
            _state.ClearPendingSwitchConfirmation();
            _client.StartTrial(_state.SelectedAppearanceId);
            Render();
        }

        private void HandleCancelTrialClicked()
        {
            if (_state.HasOverlay || _client == null) return;
            _state.ClearPendingSwitchConfirmation();
            _client.CancelTrial();
            Render();
        }

        private void HandleSwitchClicked()
        {
            if (_state.HasOverlay || _client == null || _client.Info == null) return;
            var info = _client.Info;
            var selected = FindEntry(info, _state.SelectedAppearanceId);
            if (selected == null) return;
            bool isOwned = selected.Owned || selected.AppearanceId == info.PermanentAppearanceId;
            _state.SelectAppearance(_state.SelectedAppearanceId, CurrentListPosition());
            var outcome = _state.RequestSwitch(
                _state.SelectedAppearanceId,
                info.PermanentAppearanceId,
                info.OwnedCardCount,
                info.CardItemCount,
                targetOwned: isOwned,
                targetCardUnlockable: selected.CardUnlockable);
            if (outcome == AppearanceSwitchIntentOutcome.ReadyForFreeSwitch)
            {
                _client.SwitchAppearance(_state.SelectedAppearanceId, expectFreeSwitch: true);
                Render();
                return;
            }
            if (outcome == AppearanceSwitchIntentOutcome.Ignored ||
                outcome == AppearanceSwitchIntentOutcome.LockedExclusive) return;
            Render();
            FocusFirstControlOfActiveOverlay();
        }

        private void HandleConfirmSwitchSubmit()
        {
            if (_client == null || !_state.TryConsumePendingSwitchConfirm(out string targetId)) return;
            _client.SwitchAppearance(targetId, expectFreeSwitch: false);
            Render();
        }

        private void HandleReadStoryClicked()
        {
            if (_state.IsBiographyOpen) return;
            SaveReadingPosition();
            float scrollPos = _state.HasOverlay ? _state.SavedListScrollNormalized : CurrentListPosition();
            bool opened = _state.OpenBiography(scrollPos);
            Render();
            if (!opened) return;
            FocusFirstControlOfActiveOverlay();
        }

        private void HandleCloseStory(AppearanceStoryCloseReason reason)
        {
            SaveReadingPosition();
            if (!_state.CloseStory(reason)) return;
            Render();
            RestoreListScrollPosition();
            if (_state.HasOverlay) { FocusFirstControlOfActiveOverlay(); return; }
            if (_readStoryBtn != null && _readStoryBtn.gameObject.activeInHierarchy && _readStoryBtn.interactable &&
                EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_readStoryBtn.gameObject);
            }
            else if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void HandleDismissOverlay()
        {
            bool wasReading = _state.IsStoryOpen || _state.IsBiographyOpen || _state.IsPortraitPreviewOpen;
            bool wasSeriesEpilogue = _state.IsSeriesEpilogueOpen;
            SaveReadingPosition();
            if (!_state.DismissActiveOverlay()) return;
            Render();
            if (_state.IsStoryOpen && _storyScroll != null)
                _storyScroll.verticalNormalizedPosition = _state.SavedStoryScrollNormalized;
            if (_state.HasOverlay) { FocusFirstControlOfActiveOverlay(); return; }
            if (wasReading || wasSeriesEpilogue) RestoreListScrollPosition();
            if (EventSystem.current != null)
            {
                if (wasReading && _readStoryBtn != null && _readStoryBtn.gameObject.activeInHierarchy && _readStoryBtn.interactable)
                    EventSystem.current.SetSelectedGameObject(_readStoryBtn.gameObject);
                else if (wasSeriesEpilogue && _seriesEpilogueBtn != null && _seriesEpilogueBtn.gameObject.activeInHierarchy && _seriesEpilogueBtn.interactable)
                    EventSystem.current.SetSelectedGameObject(_seriesEpilogueBtn.gameObject);
                else
                    EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void HandleOverlayInputAndFocus()
        {
            bool escape = false, tab = false, reverse = false;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys != null)
            {
                escape = keys.escapeKey.wasPressedThisFrame;
                tab = keys.tabKey.wasPressedThisFrame;
                reverse = keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            escape = Input.GetKeyDown(KeyCode.Escape);
            tab = Input.GetKeyDown(KeyCode.Tab);
            reverse = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            if (escape && _lastEscapeHandledFrame != Time.frameCount)
            {
                _lastEscapeHandledFrame = Time.frameCount;
                if (_state.HasOverlay) HandleDismissOverlay();
                else if (_compactLayout && _compactDetail)
                {
                    _compactDetail = false;
                    ApplyReadingLayout();
                    Canvas.ForceUpdateCanvases();
                    RestoreListScrollPosition();
                }
                else HidePanel();
                return;
            }

            if (!_state.HasOverlay) return;
            var controls = ActiveOverlayControls();
            var system = EventSystem.current;
            if (system == null || controls == null || controls.Length == 0) return;

            var selected = system.currentSelectedGameObject;
            int index = -1;
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] != null && controls[i].gameObject == selected && controls[i].interactable)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                FocusFirstControlOfActiveOverlay();
                return;
            }
            if (tab)
            {
                int next = (index + (reverse ? controls.Length - 1 : 1)) % controls.Length;
                if (controls[next] != null && controls[next].interactable)
                    system.SetSelectedGameObject(controls[next].gameObject);
            }
        }

        private Selectable[] ActiveOverlayControls() => _state.ActiveOverlay switch
        {
            AppearanceOverlayKind.StoryReader => _storyControls,
            AppearanceOverlayKind.SwitchConfirm => _confirmControls,
            AppearanceOverlayKind.NoCardGuide => _noCardControls,
            AppearanceOverlayKind.Biography => _biographyControls,
            AppearanceOverlayKind.PortraitPreview => _portraitControls,
            AppearanceOverlayKind.SeriesEpilogue => _seriesEpilogueControls,
            _ => null,
        };

        private void FocusFirstControlOfActiveOverlay()
        {
            var system = EventSystem.current;
            var controls = ActiveOverlayControls();
            if (system == null || controls == null) return;
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] != null && controls[i].gameObject.activeInHierarchy && controls[i].interactable)
                {
                    system.SetSelectedGameObject(controls[i].gameObject);
                    return;
                }
            }
        }

        private void Render()
        {
            if (_window == null || !IsPanelVisible) return;
            var info = _client?.Info;

            _validIdsBuffer.Clear();
            if (info != null)
            {
                for (int i = 0; i < info.Appearances.Count; i++)
                    _validIdsBuffer.Add(info.Appearances[i].AppearanceId);
            }
            _state.SyncAvailableAppearances(_validIdsBuffer, info?.ActiveAppearanceId, info?.PermanentAppearanceId);
            var visibleEntries = SyncSeriesStateAndFilter(info);

            SyncListRows(info, visibleEntries);

            if (info == null)
            {
                _listCountLabel.text = "可选形象";
                _previewName.text = _client?.InfoPending == true ? "正在读取形象" : "读取形象失败";
                _previewArchiveTag.text = "";
                _previewDesc.text = "";
                _previewStatus.text = "";
                ClearPreviewPortrait();
                _previewMissingBox.SetActive(false);
                _trialBannerRoot.SetActive(false);
                HideBiographySection();
                _cardInfoLine1.text = "角色形象卡  背包持有：—";
                _cardInfoLine2.text = "消耗 1 张永久解锁新形象（保留原形象，已拥有免费切换）";
                _trialBtn.interactable = false;
                _switchBtn.interactable = false;
                _buyMallBtn.interactable = false;
                _previewButton.interactable = false;
                _readOnlyRetryButton.gameObject.SetActive(_client?.InfoPending != true);
                _readOnlyRetryButton.interactable = _client?.Ready == true;
                RenderSeriesUi(null);
                UpdateStatusText();
                SyncOverlayVisibility();
                return;
            }

            int ownedTotal = 0;
            for (int i = 0; i < info.Appearances.Count; i++)
            {
                var e = info.Appearances[i];
                if (e.Owned || e.AppearanceId == info.PermanentAppearanceId) ownedTotal++;
            }
            _listCountLabel.text = $"角色衣柜 · 已收集 {ownedTotal}/{info.Appearances.Count}";
            _readOnlyRetryButton.gameObject.SetActive(info.Appearances.Count == 0);
            _readOnlyRetryButton.interactable = _client.Ready && !_client.InfoPending;
            CharacterAppearanceEntry selected = FindEntry(info, _state.SelectedAppearanceId);
            _previewButton.interactable = selected != null;

            if (selected != null)
            {
                _previewName.text = AppearanceBiographyCatalog.TryGetByRawAppearanceId(selected.AppearanceId, out var archive)
                    ? archive.DisplayArchiveName : selected.Name;
                string tableDesc = LookupTableDescription(selected.ConfigId);
                _previewDesc.text = tableDesc;
                UpdatePreviewPortrait(selected.AppearanceId);

                bool isPermanent = selected.AppearanceId == info.PermanentAppearanceId;
                bool isOwned = isPermanent || selected.Owned;
                bool isTrialing = _client.HasActiveTrial && selected.AppearanceId == info.TrialAppearanceId;
                if (isTrialing)
                {
                    string permanentName = ResolveEntryName(info, info.PermanentAppearanceId);
                    _previewStatus.text = $"当前正在免费试用该形象，到期将自动恢复为【{permanentName}】";
                    _previewStatus.color = AppearanceUiArt.CrimsonInk;
                }
                else if (isPermanent)
                {
                    _previewStatus.text = "状态：使用中（永久已拥有）";
                    _previewStatus.color = AppearanceUiArt.TrialGreen;
                }
                else if (isOwned)
                {
                    _previewStatus.text = "状态：已永久拥有（可随时免费切换，0 消耗）";
                    _previewStatus.color = AppearanceUiArt.TrialGreen;
                }
                else if (!selected.CardUnlockable)
                {
                    _previewStatus.text = selected.TrialUsed
                        ? "状态：未获得（专属玩法/活动解锁，已试用过）"
                        : "状态：未获得（专属玩法/活动解锁，可免费试用 1 小时）";
                    _previewStatus.color = AppearanceUiArt.MutedInk;
                }
                else if (selected.TrialUsed)
                {
                    _previewStatus.text = "状态：未获得（已免费试用过，消耗形象卡可永久解锁）";
                    _previewStatus.color = AppearanceUiArt.MutedInk;
                }
                else
                {
                    _previewStatus.text = "状态：未获得（可免费试用 1 小时 / 消耗形象卡永久解锁）";
                    _previewStatus.color = AppearanceUiArt.TrialGreen;
                }

                // 按原始稳定 appearance_id 精确匹配人物背景（绝不借用视觉替代 ID）
                if (_state.TryGetSelectedBiography(out var bio))
                {
                    _previewArchiveTag.text = selected.Name;
                    _biographyIdentity.text = $"{bio.DisplayArchiveName} · {bio.OriginalTitle}";
                    _biographyBody.text = bio.BackgroundSummary;
                    _biographySection.SetActive(true);
                    _readStoryBtn.interactable = true;
                }
                else
                {
                    HideBiographySection();
                    _previewArchiveTag.text = "暂无人物志";
                }
            }
            else
            {
                _previewName.text = "暂无可展示形象";
                _previewDesc.text = "";
                _previewStatus.text = "";
                ClearPreviewPortrait();
                _previewMissingBox.SetActive(false);
                HideBiographySection();
            }

            uint requiredCards = info.CardItemCount > 0 ? info.CardItemCount : 1u;
            string priceText = info.MallPriceFen > 0 ? AppearanceModels.FormatPriceYuan(info.MallPriceFen) : "价格待同步";
            _cardInfoLine1.text = $"角色形象卡  背包持有：{info.OwnedCardCount} 张";
            _cardInfoLine2.text = $"消耗 {requiredCards} 张永久解锁新形象（保留原形象，已拥有免费切换）";
            _buyMallLabel.text = $"商城购买 {priceText}";
            _buyMallBtn.interactable = _client.Ready && !_client.RequiresReconnect &&
                info.MallSkuId != 0 && info.MallPriceFen > 0;

            _cancelTrialBtn.interactable = _client.Ready && !_client.ActionPending && !_state.HasOverlay;
            _lastCountdownSeconds = ulong.MaxValue;
            UpdateTrialCountdownText();

            bool busy = _client.ActionPending || _client.RequiresReconnect || !_client.Ready;
            bool isTargetPermanent = selected != null && selected.AppearanceId == info.PermanentAppearanceId;
            bool isTargetOwned = selected != null && (isTargetPermanent || selected.Owned);
            bool isTargetCardUnlockable = selected != null && selected.CardUnlockable;
            bool isTargetTrialUsed = selected != null && selected.TrialUsed;
            bool isTargetTrialing = selected != null && _client.HasActiveTrial && selected.AppearanceId == info.TrialAppearanceId;

            bool trialEnabled = !busy && selected != null && !isTargetPermanent && !isTargetOwned && !isTargetTrialUsed;
            _trialBtn.interactable = trialEnabled;
            if (_trialBtnImage != null)
            {
                var trialSprite = AppearanceUiArt.LoadGameplay(trialEnabled ? "button_secondary" : "button_disabled");
                if (trialSprite != null) _trialBtnImage.sprite = trialSprite;
            }
            _trialLabel.text = isTargetPermanent ? "当前使用中"
                : isTargetOwned ? "已拥有（免费切换）"
                : isTargetTrialing ? "正在免费试用中"
                : isTargetTrialUsed ? "该形象已试用过"
                : "免费试用 1 小时";
            _trialLabel.color = trialEnabled ? AppearanceUiArt.JadeInk : AppearanceUiArt.MutedInk;

            bool canSwitch = !busy && selected != null && !isTargetPermanent && (isTargetOwned || isTargetCardUnlockable);
            _switchBtn.interactable = canSwitch;
            _switchLabel.text = _client.ActionPending ? "处理中…"
                : isTargetPermanent ? "当前使用中"
                : isTargetOwned ? "免费切换"
                : !isTargetCardUnlockable ? "专属玩法解锁"
                : $"解锁并切换（耗{requiredCards}张卡）";

            UpdateStatusText();
            RenderSeriesUi(info);
            RenderOverlays(info, requiredCards, priceText);
            SyncOverlayVisibility();
        }

        private void RenderOverlays(PlayerAppearanceInfo info, uint requiredCards, string priceText)
        {
            if (_state.IsSwitchConfirmOpen && info != null)
            {
                string oldId = info.PermanentAppearanceId;
                string newId = _state.PendingSwitchTargetId;
                string oldName = ResolveEntryName(info, oldId);
                string newName = ResolveEntryName(info, newId);

                ApplyOverlayPortrait(_confirmOldPortrait, _confirmOldMissing, oldId);
                ApplyOverlayPortrait(_confirmNewPortrait, _confirmNewMissing, newId);
                _confirmOldName.text = $"原形象 · {oldName}";
                _confirmNewName.text = $"新形象 · {newName}";
                _confirmBodyText.text =
                    $"将消耗 {requiredCards} 张【角色形象卡】，永久解锁并切换为【{newName}】。\n" +
                    $"原形象【{oldName}】将保留在你的角色衣柜中，之后可随时免费切换。";
                _confirmSubmitBtn.interactable = _client != null && _client.Ready && !_client.ActionPending;
            }

            if (_state.IsNoCardGuideOpen && info != null)
            {
                _noCardPriceBadgeLabel.text = $"官方商城 · {priceText} / 张";
                _noCardOwnedLabel.text = $"背包当前持有：{info.OwnedCardCount} 张";
                _noCardMallBtn.interactable = _client.Ready && !_client.RequiresReconnect &&
                    info.MallSkuId != 0 && info.MallPriceFen > 0;
            }

            if (_state.TryGetReadingBiography(out var bio))
            {
                _storyTitleText.text = bio.StoryTitle;
                _storyCharacterText.text = $"{bio.DisplayArchiveName} · {bio.OriginalTitle}";
                _storyRecapLabel.text = $"【人物背景】{bio.BackgroundSummary}";
                _storyBodyText.text = bio.StoryBody;
                RefreshStoryScrollLayout();
            }
            else if (_storyTitleText != null)
            {
                _storyTitleText.text = "";
                _storyCharacterText.text = "";
                _storyRecapLabel.text = "";
                _storyBodyText.text = "";
            }
        }

        private void RefreshStoryScrollLayout()
        {
            if (_storyContent == null || _storyRecapLabel == null || _storyBodyText == null) return;
            float viewportWidth = _storyScroll != null ? _storyScroll.viewport.rect.width : StoryViewportWidth;
            float viewportHeight = _storyScroll != null ? _storyScroll.viewport.rect.height : StoryViewportHeight;
            if (Mathf.Approximately(viewportWidth, _lastStoryViewportWidth) &&
                Mathf.Approximately(viewportHeight, _lastStoryViewportHeight) &&
                Mathf.Approximately(_storyBodyText.fontSize, _lastStoryBodyFontSize) &&
                _lastStoryRecap == _storyRecapLabel.text && _lastStoryBody == _storyBodyText.text) return;
            _lastStoryViewportWidth = viewportWidth; _lastStoryViewportHeight = viewportHeight;
            _lastStoryBodyFontSize = _storyBodyText.fontSize;
            _lastStoryRecap = _storyRecapLabel.text; _lastStoryBody = _storyBodyText.text;
            float textWidth = viewportWidth - 72f;
            _storyRecapLabel.ForceMeshUpdate(true, true);
            float recapH = Mathf.Ceil(_storyRecapLabel.GetPreferredValues(_storyRecapLabel.text, textWidth, 0f).y);
            recapH = Mathf.Max(40f, recapH);
            PlaceTopLeft(_storyRecapLabel.rectTransform, 36f, 24f, textWidth, recapH);

            float dividerY = 24f + recapH + 18f;
            if (_storyDivider != null) PlaceTopLeft(_storyDivider, 36f, dividerY, textWidth, 2f);

            float bodyY = dividerY + 24f;
            _storyBodyText.ForceMeshUpdate(true, true);
            float bodyH = Mathf.Ceil(_storyBodyText.GetPreferredValues(_storyBodyText.text, textWidth, 0f).y);
            bodyH = Mathf.Max(220f, bodyH);
            PlaceTopLeft(_storyBodyText.rectTransform, 36f, bodyY, textWidth, bodyH);

            // 即使短文一屏放得下，也保留可滚动余量与末行底部留白，支持字体放大后继续滚动。
            float contentHeight = Mathf.Max(viewportHeight + 24f, bodyY + bodyH + 76f);
            _storyContent.sizeDelta = new Vector2(viewportWidth, contentHeight);
        }

        private void SyncOverlayVisibility()
        {
            if (_confirmOverlay != null) _confirmOverlay.SetActive(_state.IsSwitchConfirmOpen);
            if (_noCardOverlay != null) _noCardOverlay.SetActive(_state.IsNoCardGuideOpen);
            if (_storyOverlay != null) _storyOverlay.SetActive(_state.IsStoryOpen);
            if (_seriesEpilogueOverlay != null) _seriesEpilogueOverlay.SetActive(_state.IsSeriesEpilogueOpen);
            RenderReadingOverlays();
            if (_mainPanelGroup != null)
            {
                bool blocked = _state.HasOverlay;
                _mainPanelGroup.interactable = !blocked;
                _mainPanelGroup.blocksRaycasts = !blocked;
            }
            ApplyReadingLayout();
        }

        private void SyncListRows(PlayerAppearanceInfo info, IReadOnlyList<CharacterAppearanceEntry> visibleEntries)
        {
            if (_listContent == null) return;
            if (info == null || visibleEntries == null || visibleEntries.Count == 0)
            {
                ClearRows();
                return;
            }

            string signature = BuildListSignature(visibleEntries);
            if (signature != _listSignature)
            {
                float savedScroll = _listScroll != null && _listScroll.gameObject.activeInHierarchy
                    ? _listScroll.verticalNormalizedPosition : _state.SavedListScrollNormalized;
                RebuildRowObjects(visibleEntries);
                _listSignature = signature;
                if (_listScroll != null)
                {
                    _listScroll.velocity = Vector2.zero;
                    _listScroll.verticalNormalizedPosition = Mathf.Clamp01(savedScroll);
                }
            }

            for (int i = 0; i < _rows.Count && i < visibleEntries.Count; i++)
            {
                var row = _rows[i];
                var entry = visibleEntries[i];
                bool selected = entry.AppearanceId == _state.SelectedAppearanceId;
                bool isPermanent = entry.AppearanceId == info.PermanentAppearanceId;
                bool isTrial = _client != null && _client.HasActiveTrial && entry.AppearanceId == info.TrialAppearanceId;

                if (row.CardBackground != null)
                {
                    AppearanceUiArt.ApplyRowSelection(row.CardBackground, selected);
                }

                row.NameText.text = entry.Name;
                row.NameText.color = AppearanceUiArt.JadeInk;

                if (AppearanceBiographyCatalog.TryGetByRawAppearanceId(entry.AppearanceId, out var bio))
                {
                    row.SubText.text = $"人物背景 · {bio.ArchiveName}";
                    row.SubText.color = AppearanceUiArt.TrialGreen;
                }
                else
                {
                    var series = AppearanceSeriesModels.FindSeries(info, entry.SeriesId);
                    row.SubText.text = series != null ? $"系列 · {series.Name}" : "暂无人物志";
                    row.SubText.color = AppearanceUiArt.MutedInk;
                }

                if (row.LoadedAvatarId != entry.AppearanceId)
                {
                    row.LoadedAvatarId = entry.AppearanceId;
                    var sprite = LoadExactAppearancePortrait(entry.AppearanceId);
                    row.AvatarImage.sprite = sprite;
                    row.AvatarImage.enabled = sprite != null;
                    row.AvatarMissingText.gameObject.SetActive(sprite == null);
                }
                row.AvatarMissingText.color = AppearanceUiArt.MutedInk;

                bool isOwned = isPermanent || entry.Owned;
                AppearanceBadgeKind badgeKind = isTrial ? AppearanceBadgeKind.Trialing
                    : isPermanent ? AppearanceBadgeKind.CurrentPermanent
                    : isOwned ? AppearanceBadgeKind.Owned
                    : !entry.CardUnlockable ? AppearanceBadgeKind.ExclusiveLocked
                    : entry.TrialUsed ? AppearanceBadgeKind.TrialUsed
                    : AppearanceBadgeKind.TrialFree;
                AppearanceUiArt.ApplyBadge(row.BadgeBackground, row.BadgeLabel, badgeKind, BadgeHeight);
            }
        }

        private void RebuildRowObjects(IReadOnlyList<CharacterAppearanceEntry> entries)
        {
            ClearRows();
            const float rowWidth = 580f;
            const float rowHeight = 98f;
            const float rowGap = 10f;
            float y = 6f;

            var normalCard = AppearanceUiArt.LoadGameplay("pet_card_normal");
            var avatarFrameSprite = AppearanceUiArt.LoadGameplay("portrait_frame");

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                string id = entry.AppearanceId;

                var btn = QdaoUguiFactory.CreateArtButton("AppearanceOption_" + id, _listContent,
                    6f, y, rowWidth, rowHeight, normalCard, out var cardBg);
                if (normalCard != null && normalCard.border.sqrMagnitude > 0f)
                {
                    cardBg.type = Image.Type.Sliced;
                    cardBg.pixelsPerUnitMultiplier = 1f;
                }
                btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                btn.onClick.AddListener(() =>
                {
                    if (_state.HasOverlay) return;
                    float scrollPos = _listScroll != null ? _listScroll.verticalNormalizedPosition : 1f;
                    _state.SelectAppearance(id, scrollPos);
                    _compactDetail = true;
                    Render();
                });

                var avatarFrame = AppearanceUiArt.FixedArt(btn.transform, "AvatarFrame", avatarFrameSprite,
                    14f, 10f, 78f, 78f, AppearanceUiArt.PortraitPlaceholderBg, preserveAspect: true);
                var avatarImg = QdaoUguiFactory.CreateImage("Avatar", avatarFrame.transform, 6f, 6f, 66f, 66f, null);
                avatarImg.preserveAspect = true;
                avatarImg.enabled = false;
                var avatarMiss = QdaoUguiFactory.CreateText("AvatarMissing", avatarFrame.transform, 4f, 22f, 70f, 34f,
                    "缺图", 18f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
                avatarMiss.richText = false;

                var nameLabel = QdaoUguiFactory.CreateText("Name", btn.transform,
                    104f, 14f, 248f, 38f, entry.Name, 27f, AppearanceUiArt.JadeInk, TextAlignmentOptions.MidlineLeft);
                nameLabel.richText = false;
                nameLabel.fontStyle = FontStyles.Bold;

                var subLabel = QdaoUguiFactory.CreateText("Sub", btn.transform,
                    104f, 54f, 248f, 28f, "", 19f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
                subLabel.richText = false;

                var badgeBg = AppearanceUiArt.HorizontalBar(btn.transform, "Badge", null,
                    362f, 23f, 206f, BadgeHeight, AppearanceUiArt.ParchmentCard);
                var badgeLabel = QdaoUguiFactory.CreateText("BadgeLabel", badgeBg.transform,
                    10f, 0f, 186f, BadgeHeight, "", 20f, AppearanceUiArt.JadeInk, TextAlignmentOptions.Center);
                badgeLabel.richText = false;
                badgeLabel.fontStyle = FontStyles.Bold;

                _rows.Add(new RowView
                {
                    AppearanceId = id,
                    Root = btn.gameObject,
                    Button = btn,
                    CardBackground = cardBg,
                    AvatarImage = avatarImg,
                    AvatarMissingText = avatarMiss,
                    NameText = nameLabel,
                    SubText = subLabel,
                    BadgeBackground = badgeBg,
                    BadgeLabel = badgeLabel,
                });

                y += rowHeight + rowGap;
            }
            _listContent.sizeDelta = new Vector2(594f, Mathf.Max(660f, y + 12f));
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Root == null) continue;
                if (Application.isPlaying) Destroy(_rows[i].Root);
                else DestroyImmediate(_rows[i].Root);
            }
            _rows.Clear();
            _listSignature = "";
        }

        private void RestoreListScrollPosition()
        {
            if (_listScroll == null) return;
            _listScroll.velocity = Vector2.zero;
            _listScroll.verticalNormalizedPosition = _state.SavedListScrollNormalized;
        }

        private void HideBiographySection()
        {
            if (_previewArchiveTag != null) _previewArchiveTag.text = "";
            if (_biographyIdentity != null) _biographyIdentity.text = "";
            if (_biographyBody != null) _biographyBody.text = "";
            if (_biographySection != null) _biographySection.SetActive(false);
        }

        private void UpdatePreviewPortrait(string rawAppearanceId)
        {
            // 同人物立绘缓存复用，阅读故事与刷新倒计时绝不重复加载角色立绘/动作。
            if (_loadedPreviewPortraitId != rawAppearanceId)
            {
                _loadedPreviewPortraitId = rawAppearanceId;
                _loadedPreviewPortraitSprite = LoadExactAppearancePortrait(rawAppearanceId);
            }
            _previewPortrait.sprite = _loadedPreviewPortraitSprite;
            _previewPortrait.enabled = _loadedPreviewPortraitSprite != null;
            _previewMissingBox.SetActive(_loadedPreviewPortraitSprite == null);
            if (_loadedPreviewPortraitSprite == null)
            {
                var label = _previewMissingBox.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = _previewName.text + "\n形象资源暂不可用";
            }
        }

        private void ClearPreviewPortrait()
        {
            _loadedPreviewPortraitId = "";
            _loadedPreviewPortraitSprite = null;
            if (_previewPortrait != null)
            {
                _previewPortrait.sprite = null;
                _previewPortrait.enabled = false;
            }
        }

        private void ApplyOverlayPortrait(Image image, GameObject missing, string rawAppearanceId)
        {
            var sprite = LoadExactAppearancePortrait(rawAppearanceId);
            image.sprite = sprite;
            image.enabled = sprite != null;
            missing.SetActive(sprite == null);
        }

        private Sprite LoadExactAppearancePortrait(string rawAppearanceId)
        {
            if (string.IsNullOrEmpty(rawAppearanceId)) return null;
            if (_portraitLeases.TryGetValue(rawAppearanceId, out var cached)) return cached?.Sprite;
            if (_portraitLoaderOverride != null)
            {
                var borrowed = QdaoCharacterCatalog.CreationPortraitLease.Borrow(_portraitLoaderOverride(rawAppearanceId));
                _portraitLeases[rawAppearanceId] = borrowed;
                return borrowed?.Sprite;
            }
            if (QdaoCharacterCatalog.Find(rawAppearanceId) == null) return null;
            // 仅图像允许使用正式资源替代；档案与选中身份始终保留原始 ID。
            string visualId = QdaoCharacterCatalog.ResolveVisualId(rawAppearanceId);
            var lease = QdaoCharacterCatalog.AcquireCreationPortrait(visualId);
            _portraitLeases[rawAppearanceId] = lease;
            return lease?.Sprite;
        }

        private void UpdateTrialCountdownText()
        {
            if (_trialBannerRoot == null || _trialCountdownText == null) return;
            bool active = _client != null && _client.HasActiveTrial;
            ulong remaining = active ? _client.TrialRemainingSeconds : 0ul;
            if (active == _lastCountdownActive && remaining == _lastCountdownSeconds) return;
            _lastCountdownActive = active;
            _lastCountdownSeconds = remaining;
            _trialBannerRoot.SetActive(active);
            if (active)
                _trialCountdownText.text = $"试用中 · 剩余 {AppearanceModels.FormatTrialCountdown(remaining)}";
        }

        private void UpdateStatusText()
        {
            if (_status == null) return;
            _status.text = _client?.Status ?? "";
            _status.color = (_client?.StatusIsError ?? false) ? AppearanceUiArt.CrimsonInk : AppearanceUiArt.MutedInk;
        }

        private void Bind(GameClient game)
        {
            if (ReferenceEquals(_game, game)) return;
            Unbind();
            _game = game;
            if (game != null)
            {
                _client = new AppearanceClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                    () => Time.realtimeSinceStartup);
                _client.Changed += Render;
                _client.Notice += HandleNotice;
                game.OnSceneEntered += HandleSceneEntered;
                game.OnDisconnected += HandleDisconnected;
                if (game.InGame && game.IsGateReady && game.CurrentSceneId != 0) _client.RequestInfo();
            }
            Render();
        }

        private void Unbind()
        {
            if (_game != null)
            {
                _game.OnSceneEntered -= HandleSceneEntered;
                _game.OnDisconnected -= HandleDisconnected;
                _game = null;
            }
            if (_client != null)
            {
                _client.Changed -= Render;
                _client.Notice -= HandleNotice;
                if (!_externalVerificationClient) _client.Dispose();
                _client = null;
            }
            _externalVerificationClient = false;
            _portraitLoaderOverride = null;
            _seriesEpilogueBodyLookupOverride = null;
            HidePanel();
        }

        private void HandleSceneEntered(SceneInfoComp _) => _client?.HandleSceneEntered();

        private void HandleDisconnected()
        {
            _client?.HandleDisconnected();
            HidePanel();
            _state.ResetForRoleChange();
        }

        private static void HandleNotice(string text) => BattleUiRoot.Instance?.ShowToast(text);

        private static CharacterAppearanceEntry FindEntry(PlayerAppearanceInfo info, string rawId)
        {
            if (info == null || string.IsNullOrEmpty(rawId)) return null;
            for (int i = 0; i < info.Appearances.Count; i++)
                if (info.Appearances[i].AppearanceId == rawId) return info.Appearances[i];
            return null;
        }

        private static string ResolveEntryName(PlayerAppearanceInfo info, string rawId)
        {
            var entry = FindEntry(info, rawId);
            if (entry != null && !string.IsNullOrEmpty(entry.Name)) return entry.Name;
            var def = QdaoCharacterCatalog.Find(rawId);
            return def != null ? def.Name : rawId ?? "";
        }

        private static string LookupTableDescription(uint configId)
        {
            if (configId == 0) return "";
            var row = CharacterAppearanceTableManager.Instance.FindById(configId);
            return row?.Desc ?? "";
        }

        private static string BuildListSignature(IReadOnlyList<CharacterAppearanceEntry> entries)
        {
            var sb = new System.Text.StringBuilder(entries.Count * 28);
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(entries[i].AppearanceId);
            }
            return sb.ToString();
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
