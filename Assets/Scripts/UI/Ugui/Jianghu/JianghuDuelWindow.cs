using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game.Jianghu;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Jianghu
{
    public enum JianghuWindowTab
    {
        Challenge = 0,
        Grudges = 1,
        GuildWall = 2,
    }

    /// <summary>
    /// 天墉生死擂与江湖恩怨录可交互 uGUI 窗口（零强制约战、软惩罚、双方式洗尘、1V1~5V5 小队确认、宿敌高光连胜与帮派恩怨墙）。
    /// 优先加载 Assets/Resources/UI/Ugui/JianghuDuelV1/ 专属皮肤切片，缺图时无缝回落至 GameplayUiArt。
    /// </summary>
    public sealed class JianghuDuelWindow : IDisposable
    {
        public const string SkinRoot = "UI/Ugui/JianghuDuelV1/";

        private const float FrameWidth = 1860f;
        private const float FrameHeight = 960f;
        private const float ContentX = 56f;
        private const float ContentWidth = FrameWidth - 112f; // 1748
        private const float BannerY = 136f;
        private const float BannerHeight = 104f;
        private const float TabsY = 252f;
        private const float TabsHeight = 64f;
        private const float ContentY = 328f;
        private const float ContentHeight = 566f;

        private static readonly Color DangerInk = QdaoUguiTheme.Html("#A94735");
        private static readonly Color JadeAccent = QdaoUguiTheme.Html("#1F6B52");
        private static readonly Color GoldAccent = QdaoUguiTheme.Html("#9A6B22");
        private static readonly Color CardTint = new Color(0.95f, 0.92f, 0.84f, 0.95f);
        private static readonly Color GoldCardTint = new Color(0.99f, 0.94f, 0.80f, 0.98f);

        private readonly RectTransform _root, _frame, _contentHost, _clearOverlay;
        private RectTransform _activeInvitesHost;
        private float _activeInvitesWidth;
        private float _activeInvitesHeight;
        private readonly TMP_Text _balanceText, _bannerText, _bannerSubText, _statusText;
        private readonly Button _peaceToggleBtn, _clearBannerBtn;
        private readonly TMP_Text _peaceToggleLabel;
        private readonly Button[] _tabButtons = new Button[3];
        private readonly TMP_Text[] _tabLabels = new TMP_Text[3];
        private readonly Image[] _tabImages = new Image[3];

        // Clear-mark modal widgets
        private readonly TMP_Text _clearBodyText, _clearNoticeText;
        private readonly Button _clearSilverBtn, _clearCharmBtn, _clearBuyCharmBtn;

        private JianghuClient _client;
        private Action<uint> _openSpiritShopAction;
        private Action<ulong> _playReplayAction;

        private JianghuWindowTab _activeTab = JianghuWindowTab.Challenge;
        private string _draftTargetPlayerId = string.Empty;
        private string _draftTargetMembers = string.Empty;
        private global::Match.JianghuWritTier _selectedWritTier = global::Match.JianghuWritTier.Silver;
        private float _lastCountdownRenderTime;
        private bool _disposed;

        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public event Action Closed;

        public JianghuDuelWindow(
            UnityEngine.Transform parent,
            Action<uint> openSpiritShopAction = null,
            Action<ulong> playReplayAction = null)
        {
            _openSpiritShopAction = openSpiritShopAction;
            _playReplayAction = playReplayAction;

            _root = QdaoUguiFactory.CreateStretch("JianghuDuelWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);

            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(0.025f, 0.09f, 0.075f, 0.72f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();

            _frame = QdaoUguiFactory.CreateCenteredRect("JianghuDuelFrame", _root, FrameWidth, FrameHeight);
            CreateSkinOrFallbackArt(_frame, "JianghuFrameBg", "window_frame", "main_frame", 0f, 0f, FrameWidth, FrameHeight);

            bool hasTitleSkin = LoadOptionalSkin("title_plate") != null;
            var plaque = CreateSkinOrFallbackArt(_frame, "JianghuTitlePlaque", "title_plate", "title_plate", 76f, 18f, 520f, 110f);
            var title = Text(plaque.transform, "天墉生死擂",
                76f, hasTitleSkin ? 24f : 14f, 368f, hasTitleSkin ? 58f : 68f,
                34f, Cream, alignment: TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.raycastTarget = false;

            _balanceText = Text(_frame, "银两：0   ·   金帖生死状：0   ·   清风洗尘符：0", 620f, 44f, 650f, 44f, 24f, Ink);
            _balanceText.name = "JianghuBalanceStrip";
            _balanceText.raycastTarget = false;

            _peaceToggleBtn = SkinButton(_frame, "JianghuPeaceToggle", "闭关免战：关", 1296f, 38f, 244f, 64f,
                HandlePeaceToggleClicked, "btn_secondary", "button_secondary", false, 23f);
            _peaceToggleLabel = _peaceToggleBtn.GetComponentInChildren<TMP_Text>(true);

            var refreshBtn = SkinButton(_frame, "JianghuRefresh", "刷新", 1554f, 38f, 118f, 64f,
                () => _client?.RefreshAll(force: true), "btn_secondary", "button_secondary", false, 24f);
            refreshBtn.name = "JianghuRefresh";

            var closeBtn = SkinButton(_frame, "JianghuClose", "关闭", 1686f, 38f, 118f, 64f,
                Hide, "btn_secondary", "button_secondary", false, 24f);
            closeBtn.name = "JianghuClose";

            // Top status banner (Defeat mark / Clear badge / Calm notice)
            var bannerBg = QdaoUguiFactory.CreateImage("JianghuStatusBanner", _frame, ContentX, BannerY, ContentWidth, BannerHeight,
                LoadOptionalSkin("status_banner"));
            bannerBg.type = bannerBg.sprite != null && bannerBg.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            bannerBg.pixelsPerUnitMultiplier = 1f;
            if (bannerBg.sprite == null) bannerBg.color = CardTint;
            bannerBg.raycastTarget = false;

            _bannerText = Text(bannerBg.transform, "当前清净无败绩印记 · 双方自愿方可立状开战",
                28f, 16f, 1340f, 38f, 25f, JadeAccent);
            _bannerText.fontStyle = FontStyles.Bold;
            _bannerText.raycastTarget = false;

            _bannerSubText = Text(bannerBg.transform,
                "零强制接战 · 零战力加成 · 30 回合封顶防恶意拖局 · 拒绝/超时/变动全额零扣费",
                28f, 58f, 1340f, 32f, 20f, Muted);
            _bannerSubText.raycastTarget = false;

            _clearBannerBtn = SkinButton(bannerBg.rectTransform, "JianghuOpenClearModal", "清风洗尘", 1456f, 18f, 260f, 68f,
                OpenClearModal, "btn_primary", "button_primary", true, 25f);
            _clearBannerBtn.gameObject.SetActive(false);

            // 3 Tabs
            string[] tabTitles = { "下达战书", "江湖恩怨录", "帮派恩怨墙" };
            for (int i = 0; i < 3; i++)
            {
                var tab = (JianghuWindowTab)i;
                var btn = SkinButton(_frame, "JianghuTab_" + i, tabTitles[i],
                    ContentX + i * 256f, TabsY, 240f, TabsHeight,
                    () => SelectTab(tab), "tab_normal", "tab_normal", false, 26f);
                _tabButtons[i] = btn;
                _tabLabels[i] = btn.GetComponentInChildren<TMP_Text>(true);
                _tabImages[i] = btn.GetComponent<Image>();
            }

            CreateSkinOrFallbackArt(_frame, "JianghuContentBg", "content_panel", "content_panel",
                ContentX, ContentY, ContentWidth, ContentHeight);
            _contentHost = QdaoUguiFactory.CreateRect("JianghuContentHost", _frame,
                ContentX + 24f, ContentY + 18f, ContentWidth - 48f, ContentHeight - 36f);

            _statusText = Text(_frame, string.Empty, 60f, 904f, 1740f, 38f, 22f, Muted);
            _statusText.name = "JianghuStatus";
            _statusText.raycastTarget = false;

            // Clear defeat mark modal
            _clearOverlay = QdaoUguiFactory.CreateStretch("JianghuClearMarkOverlay", _root, Vector4.zero);
            _clearOverlay.gameObject.SetActive(false);
            var modalDim = _clearOverlay.gameObject.AddComponent<Image>();
            modalDim.color = new Color(0.02f, 0.06f, 0.05f, 0.72f);
            modalDim.raycastTarget = true;

            bool hasPopupSkin = LoadOptionalSkin("popup_frame") != null;
            var modalFrame = QdaoUguiFactory.CreateCenteredRect("JianghuClearModalFrame", _clearOverlay, 860f, 480f);
            CreateSkinOrFallbackArt(modalFrame, "JianghuClearModalBg", "popup_frame", "main_frame", 0f, 0f, 860f, 480f);

            var modalTitle = Text(modalFrame, "清风洗尘 · 提前抹去败绩印记", 76f, 33f, 708f, 56f, 32f,
                hasPopupSkin ? Cream : Ink, alignment: TextAlignmentOptions.Center);
            modalTitle.fontStyle = FontStyles.Bold;
            modalTitle.raycastTarget = false;

            _clearBodyText = Text(modalFrame, string.Empty, 56f, 112f, 748f, 156f, 24f, Ink, wrap: true);
            _clearBodyText.raycastTarget = false;

            _clearNoticeText = Text(modalFrame,
                "提示：败绩印记 30 分钟后也会自动自然消散，仅作江湖趣闻，不影响任何战力与收益。",
                56f, 276f, 748f, 70f, 21f, Muted, wrap: true);
            _clearNoticeText.raycastTarget = false;

            _clearSilverBtn = SkinButton(modalFrame, "JianghuClearSilver", "消耗 1000 银两洗尘",
                56f, 376f, 256f, 68f,
                () => ExecuteClearMark(global::JianghuClearMarkMode.Silver), "btn_secondary", "button_secondary", false, 22f);

            _clearCharmBtn = SkinButton(modalFrame, "JianghuClearCharm", "清风洗尘符 ×1（赠徽记）",
                326f, 376f, 286f, 68f,
                () => ExecuteClearMark(global::JianghuClearMarkMode.ItemCharm), "btn_primary", "button_primary", true, 22f);

            _clearBuyCharmBtn = SkinButton(modalFrame, "JianghuBuyCharm", "百宝阁购符",
                326f, 376f, 286f, 68f,
                () => JumpToSpiritShop(JianghuModels.SpiritShopGoodsClearCharm), "btn_primary", "button_primary", true, 22f);
            _clearBuyCharmBtn.gameObject.SetActive(false);

            SkinButton(modalFrame, "JianghuClearCancel", "先留着",
                626f, 376f, 178f, 68f,
                CloseClearModal, "btn_secondary", "button_secondary", false, 23f);
        }

        public void Bind(JianghuClient client)
        {
            if (_client == client) return;
            if (_client != null) _client.Changed -= HandleClientChanged;
            _client = client;
            if (_client != null) _client.Changed += HandleClientChanged;
            if (IsVisible) Render();
        }

        public void Show(JianghuWindowTab initialTab = JianghuWindowTab.Challenge, ulong prefillTargetPlayerId = 0)
        {
            if (_root == null) return;
            ApplySafeArea();
            CloseClearModal();
            _activeTab = initialTab;
            if (prefillTargetPlayerId != 0)
                _draftTargetPlayerId = prefillTargetPlayerId.ToString(CultureInfo.InvariantCulture);
            _root.gameObject.SetActive(true);
            _client?.RefreshAll(force: true);
            Render();
        }

        public void Hide()
        {
            if (_root == null) return;
            bool wasVisible = _root.gameObject.activeSelf;
            CloseClearModal();
            _root.gameObject.SetActive(false);
            if (wasVisible)
            {
                EventSystem.current?.SetSelectedGameObject(null);
                Closed?.Invoke();
            }
        }

        public void TickCountdown(float unscaledTime)
        {
            if (!IsVisible || _client == null) return;
            if (unscaledTime - _lastCountdownRenderTime < 1.0f) return;
            _lastCountdownRenderTime = unscaledTime;
            RefreshHeaderAndBanner();
            if (_activeTab == JianghuWindowTab.Challenge && _activeInvitesHost != null && _client.ActiveInvites.Count > 0)
                RebuildActiveInvitesList();
        }

        public void Render()
        {
            if (_disposed || _root == null || !_root.gameObject.activeSelf) return;
            RefreshHeaderAndBanner();
            RefreshTabStyles();
            RebuildActiveTab();
            RefreshClearModal();
            UpdateStatusLine();
        }

        private void HandleClientChanged()
        {
            if (_disposed || !IsVisible) return;
            Render();
        }

        private void SelectTab(JianghuWindowTab tab)
        {
            if (_activeTab == tab) return;
            _activeTab = tab;
            if (tab == JianghuWindowTab.Grudges) _client?.RequestGrudges(false);
            else if (tab == JianghuWindowTab.GuildWall) _client?.RequestGuildWall(false);
            Render();
        }

        private void RefreshHeaderAndBanner()
        {
            var profile = _client?.Profile;
            ulong silver = profile?.SilverBalance ?? 0;
            uint goldWrits = profile?.GoldWritItemCount ?? 0;
            uint clearCharms = profile?.ClearCharmItemCount ?? 0;
            _balanceText.text =
                $"银两：{silver.ToString(CultureInfo.InvariantCulture)}   ·   " +
                $"金帖生死状：{goldWrits.ToString(CultureInfo.InvariantCulture)}   ·   " +
                $"清风洗尘符：{clearCharms.ToString(CultureInfo.InvariantCulture)}";

            bool refuse = profile != null && profile.DuelRefuseStrangers;
            if (_peaceToggleLabel != null)
                _peaceToggleLabel.text = refuse ? "闭关免战：已开启" : "闭关免战：已关闭";
            _peaceToggleBtn.interactable = _client != null && _client.Ready && !_client.WritePending && !_client.InBattle;

            ulong nowMs = _client?.EstimatedServerNowMs ?? 0;
            uint markRem = profile != null && profile.HasActiveDefeatMark
                ? JianghuModels.RemainingSeconds(profile.DefeatMarkExpiresAtMs, nowMs)
                : 0;
            uint clearRem = profile != null && profile.HasActiveClearBadge
                ? JianghuModels.RemainingSeconds(profile.ClearBadgeExpiresAtMs, nowMs)
                : 0;

            if (profile != null && profile.HasActiveDefeatMark && markRem > 0)
            {
                _bannerText.text = $"{JianghuModels.FormatDefeatMark(profile)}   ·   剩余 {JianghuModels.FormatMinutesSeconds(markRem)}";
                _bannerText.color = DangerInk;
                _bannerSubText.text = "本印记仅持续 30 分钟后自动消散，零战力影响；也可消耗银两或清风洗尘符立即洗去。";
                _clearBannerBtn.gameObject.SetActive(true);
                _clearBannerBtn.interactable = _client != null && _client.Ready && !_client.WritePending && !_client.InBattle;
            }
            else if (profile != null && profile.HasActiveClearBadge && clearRem > 0)
            {
                _bannerText.text = $"{JianghuModels.FormatClearBadge()}（洒脱徽记生效中）   ·   剩余 {JianghuModels.FormatMinutesSeconds(clearRem)}";
                _bannerText.color = GoldAccent;
                _bannerSubText.text = "清风拂尘，过往胜负已随风散；双方自愿立状方可再入天墉生死擂。";
                _clearBannerBtn.gameObject.SetActive(false);
            }
            else
            {
                _bannerText.text = "当前清净无败绩印记 · 双方自愿方可立状开战";
                _bannerText.color = JadeAccent;
                _bannerSubText.text = "零强制接战 · 零战力加成 · 30 回合封顶防恶意拖局 · 拒绝/超时/队员变动全额零扣费";
                _clearBannerBtn.gameObject.SetActive(false);
            }
        }

        private void RefreshTabStyles()
        {
            for (int i = 0; i < 3; i++)
            {
                bool selected = (int)_activeTab == i;
                string key = selected ? "tab_selected" : "tab_normal";
                Sprite sprite = LoadOptionalSkin(key) ?? Load(key);
                if (_tabImages[i] != null && sprite != null)
                {
                    _tabImages[i].sprite = sprite;
                    _tabImages[i].type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                }
                if (_tabLabels[i] != null)
                    _tabLabels[i].color = selected ? Cream : Ink;
            }
        }

        private void RebuildActiveTab()
        {
            _activeInvitesHost = null;
            Clear(_contentHost);
            if (_client == null || !_client.IsSupported)
            {
                Text(_contentHost, JianghuClient.UnsupportedMessage, 40f, 160f, _contentHost.rect.width - 80f, 80f,
                    30f, Muted, alignment: TextAlignmentOptions.Center).raycastTarget = false;
                return;
            }

            switch (_activeTab)
            {
                case JianghuWindowTab.Challenge:
                    BuildChallengeTab();
                    break;
                case JianghuWindowTab.Grudges:
                    BuildGrudgesTab();
                    break;
                case JianghuWindowTab.GuildWall:
                    BuildGuildWallTab();
                    break;
            }
        }

        private void BuildChallengeTab()
        {
            float hostW = ContentWidth - 48f; // 1700
            float hostH = ContentHeight - 36f; // 530
            float leftW = 816f;
            float rightX = 844f;
            float rightW = hostW - rightX;

            // Left panel: issue duel invite
            var leftCard = QdaoUguiFactory.CreateRect("ChallengeFormCard", _contentHost, 0f, 0f, leftW, hostH);
            var leftBg = leftCard.gameObject.AddComponent<Image>();
            leftBg.color = CardTint;
            leftBg.raycastTarget = false;

            var heading = Text(leftCard, "递送江湖生死状（1V1 单挑 / 2V2~5V5 小队等人数对决）", 24f, 16f, leftW - 48f, 38f, 25f, Ink);
            heading.fontStyle = FontStyles.Bold;
            heading.raycastTarget = false;

            Text(leftCard, "对方玩家 ID（小队约战填对方队长 ID）：", 24f, 64f, 440f, 32f, 22f, Muted).raycastTarget = false;
            var targetInput = QdaoUguiFactory.CreateInputField("TargetPlayerIdInput", leftCard, 24f, 100f, 430f, 52f,
                "输入对方角色 ID（如 10002）", 20);
            var inputBg = targetInput.GetComponent<Image>();
            inputBg.color = new Color(1f, 0.99f, 0.95f, 1f);
            targetInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            targetInput.text = _draftTargetPlayerId;
            targetInput.onValueChanged.AddListener(v => _draftTargetPlayerId = v ?? string.Empty);

            // Team status hint & optional target squad member IDs
            var teamClient = TeamUiRoot.Instance?.Client;
            bool inSquad = teamClient != null && teamClient.HasTeam && teamClient.Snapshot.Members.Count >= 2;
            if (inSquad)
            {
                int count = teamClient.Snapshot.Members.Count;
                Text(leftCard, $"当前已组 {count} 人小队：对方须同为 {count} 人，下方可补填对方队员 ID（逗号分隔）：",
                    24f, 160f, leftW - 48f, 30f, 20f, JadeAccent).raycastTarget = false;
                var membersInput = QdaoUguiFactory.CreateInputField("TargetSquadMembersInput", leftCard, 24f, 192f, leftW - 48f, 46f,
                    "可选：对方其余队员 ID（如 10003,10004）", 80);
                membersInput.GetComponent<Image>().color = new Color(1f, 0.99f, 0.95f, 1f);
                membersInput.text = _draftTargetMembers;
                membersInput.onValueChanged.AddListener(v => _draftTargetMembers = v ?? string.Empty);
            }
            else
            {
                Text(leftCard, "当前为单人模式（1V1 单挑）；若已组建 2~5 人小队，将自动按小队等人数约战并触发 30 秒全员确认。",
                    24f, 168f, leftW - 48f, 56f, 20f, Muted, wrap: true).raycastTarget = false;
            }

            // Writ tier cards
            var profile = _client.Profile;
            ulong silverCost = profile != null && profile.SilverWritCost > 0 ? profile.SilverWritCost : JianghuModels.DefaultSilverWritCost;
            uint goldCount = profile?.GoldWritItemCount ?? 0;

            BuildWritTierOption(leftCard, 24f, 250f, 374f, 152f,
                global::Match.JianghuWritTier.Silver,
                "普通战书",
                $"消耗 {silverCost.ToString(CultureInfo.InvariantCulture)} 银两（仅成局扣费）\n全服系统播报 · 完整计入恩怨录与战报",
                "writ_card_silver", false);

            BuildWritTierOption(leftCard, 418f, 250f, 374f, 152f,
                global::Match.JianghuWritTier.GoldItem,
                $"金帖生死状（持有 {goldCount.ToString(CultureInfo.InvariantCulture)}）",
                "消耗金帖 ×1（仅成局扣费）\n全服金框开战播报 + 战报金框纪念",
                "writ_card_gold", true);

            bool busy = _client.WritePending || _client.InBattle || !_client.Ready;
            var sendBtn = SkinButton(leftCard, "JianghuSendInviteBtn", "下达战书", 24f, 430f, 320f, 70f,
                HandleSendInviteClicked, "btn_primary", "button_primary", true, 28f);
            sendBtn.interactable = !busy;

            var buyGoldBtn = SkinButton(leftCard, "JianghuBuyGoldWritBtn", "百宝阁购金帖（20 灵石）", 364f, 430f, 320f, 70f,
                () => JumpToSpiritShop(JianghuModels.SpiritShopGoodsGoldWrit), "btn_secondary", "button_secondary", false, 23f);
            buyGoldBtn.interactable = _openSpiritShopAction != null;

            // Right panel: Active invites list (hosted independently so 1s countdown never tears down left input fields)
            var rightCard = QdaoUguiFactory.CreateRect("ActiveInvitesCard", _contentHost, rightX, 0f, rightW, hostH);
            var rightBg = rightCard.gameObject.AddComponent<Image>();
            rightBg.color = CardTint;
            rightBg.raycastTarget = false;

            _activeInvitesHost = rightCard;
            _activeInvitesWidth = rightW;
            _activeInvitesHeight = hostH;
            RebuildActiveInvitesList();
        }

        private void RebuildActiveInvitesList()
        {
            if (_activeInvitesHost == null || _client == null) return;
            for (int i = _activeInvitesHost.childCount - 1; i >= 0; --i)
            {
                var child = _activeInvitesHost.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
            float rightW = _activeInvitesWidth;
            float hostH = _activeInvitesHeight;
            bool busy = _client.WritePending || _client.InBattle || !_client.Ready;

            var rightHeading = Text(_activeInvitesHost, $"在途生死状（{_client.ActiveInvites.Count.ToString(CultureInfo.InvariantCulture)}）",
                22f, 16f, rightW - 44f, 38f, 25f, Ink);
            rightHeading.fontStyle = FontStyles.Bold;
            rightHeading.raycastTarget = false;

            if (_client.ActiveInvites.Count == 0)
            {
                Text(_activeInvitesHost, "当前暂无待处理的江湖生死状。\n收到他人战书或小队参战确认时，将即时弹窗提醒并列于此处。",
                    28f, 170f, rightW - 56f, 110f, 22f, Muted, wrap: true, alignment: TextAlignmentOptions.Center).raycastTarget = false;
                return;
            }

            var scrollContent = CreateVerticalScroll(_activeInvitesHost, "ActiveInvitesScroll", 16f, 62f, rightW - 32f, hostH - 76f,
                _client.ActiveInvites.Count * 178f);
            ulong nowMs = _client.EstimatedServerNowMs;
            ulong myId = _client.PlayerId;

            for (int i = 0; i < _client.ActiveInvites.Count; i++)
            {
                var inv = _client.ActiveInvites[i];
                var row = QdaoUguiFactory.CreateRect("ActiveInvite_" + inv.DuelInviteId, scrollContent, 0f, i * 178f, rightW - 32f, 168f);
                var rowBg = QdaoUguiFactory.CreateImage("RowBg", row, 0f, 0f, rightW - 32f, 168f, LoadOptionalSkin("invite_row_bg"));
                rowBg.type = rowBg.sprite != null && rowBg.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                if (rowBg.sprite == null) rowBg.color = inv.WritTier == global::Match.JianghuWritTier.GoldItem ? GoldCardTint : Color.white;
                rowBg.raycastTarget = false;

                string tierTag = JianghuModels.FormatWritShort(inv.WritTier);
                string vsLine = $"[{tierTag} · {inv.TeamSize}V{inv.TeamSize}] {SafeName(inv.ChallengerCaptainName, inv.ChallengerCaptainId)}  对阵  {SafeName(inv.TargetCaptainName, inv.TargetCaptainId)}";
                Text(row, vsLine, 36f, 12f, rightW - 198f, 32f, 23f, Ink).raycastTarget = false;

                uint remSec = JianghuModels.RemainingSeconds(JianghuModels.InviteDeadlineMs(inv), nowMs);
                Text(row, remSec > 0 ? $"剩余 {remSec}秒" : "即将超时", rightW - 166f, 12f, 126f, 32f, 22f, DangerInk,
                    alignment: TextAlignmentOptions.MidlineRight).raycastTarget = false;

                Text(row, JianghuModels.FormatInviteState(inv.State), 36f, 48f, rightW - 86f, 28f, 20f, JadeAccent).raycastTarget = false;

                if (inv.TeamSize > 1)
                {
                    string squadProgress = FormatSquadConfirmSummary(inv);
                    Text(row, squadProgress, 36f, 78f, rightW - 86f, 28f, 19f, Muted).raycastTarget = false;
                }

                var actionKind = JianghuModels.InviteNeedsMyAction(inv, myId);
                ulong inviteId = inv.DuelInviteId;
                if (actionKind == JianghuInviteActionKind.CaptainRespond)
                {
                    var acceptBtn = SkinButton(row, "AcceptInvite_" + inviteId, "应战开打", 36f, 108f, 186f, 48f,
                        () => _client?.RespondDuelInvite(inviteId, true), "btn_primary", "button_primary", true, 22f);
                    acceptBtn.interactable = !busy;
                    var declineBtn = SkinButton(row, "DeclineInvite_" + inviteId, "婉拒战书", 236f, 108f, 186f, 48f,
                        () => _client?.RespondDuelInvite(inviteId, false), "btn_secondary", "button_secondary", false, 22f);
                    declineBtn.interactable = !busy;
                }
                else if (actionKind == JianghuInviteActionKind.MemberConfirm)
                {
                    var agreeBtn = SkinButton(row, "ConfirmMember_" + inviteId, "同意参战", 36f, 108f, 186f, 48f,
                        () => _client?.ConfirmTeamMember(inviteId, true), "btn_primary", "button_primary", true, 22f);
                    agreeBtn.interactable = !busy;
                    var abortBtn = SkinButton(row, "AbortMember_" + inviteId, "退出作废", 236f, 108f, 186f, 48f,
                        () => _client?.ConfirmTeamMember(inviteId, false), "btn_secondary", "button_secondary", false, 22f);
                    abortBtn.interactable = !busy;
                }
                else
                {
                    Text(row, "等待对方或队友响应中（未成局不扣任何费用）", 36f, 114f, rightW - 86f, 34f, 19f, Muted).raycastTarget = false;
                }
            }
        }

        private void BuildWritTierOption(
            RectTransform parent,
            float x,
            float y,
            float w,
            float h,
            global::Match.JianghuWritTier tier,
            string title,
            string desc,
            string skinName,
            bool isGold)
        {
            bool selected = _selectedWritTier == tier;
            var rect = QdaoUguiFactory.CreateRect("WritTier_" + tier, parent, x, y, w, h);
            var img = rect.gameObject.AddComponent<Image>();
            var skin = LoadOptionalSkin(skinName);
            if (skin != null)
            {
                img.sprite = skin;
                img.type = skin.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.color = selected ? Color.white : new Color(0.88f, 0.88f, 0.88f, 0.85f);
            }
            else
            {
                img.color = selected
                    ? (isGold ? GoldCardTint : new Color(0.90f, 0.96f, 0.91f, 1f))
                    : new Color(0.92f, 0.90f, 0.85f, 0.78f);
            }
            img.raycastTarget = true;
            var btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                _selectedWritTier = tier;
                RebuildActiveTab();
            });

            string prefix = selected ? "√ " : "";
            var titleTxt = Text(rect, prefix + title, 18f, 14f, w - 36f, 34f, 24f, isGold ? GoldAccent : Ink);
            titleTxt.fontStyle = FontStyles.Bold;
            titleTxt.raycastTarget = false;

            var descTxt = Text(rect, desc, 18f, 54f, w - 36f, 84f, 19f, Muted, wrap: true);
            descTxt.raycastTarget = false;
        }

        private void BuildGrudgesTab()
        {
            float hostW = ContentWidth - 48f; // 1700
            float hostH = ContentHeight - 36f; // 530

            if (_client.Grudges.Count == 0)
            {
                Text(_contentHost,
                    _client.GrudgesPending
                        ? "正在同步江湖恩怨录…"
                        : "江湖尚且风平浪静，暂无宿敌交手记录。\n完成天墉生死擂对决后，将自动载入双方恩怨录并支持一键再递战书。",
                    40f, 170f, hostW - 80f, 120f, 24f, Muted, wrap: true, alignment: TextAlignmentOptions.Center).raycastTarget = false;
                return;
            }

            float rowH = 132f;
            var scrollContent = CreateVerticalScroll(_contentHost, "GrudgesScroll", 0f, 0f, hostW, hostH,
                _client.Grudges.Count * rowH);
            bool busy = _client.WritePending || _client.InBattle || !_client.Ready;

            for (int i = 0; i < _client.Grudges.Count; i++)
            {
                var g = _client.Grudges[i];
                var row = QdaoUguiFactory.CreateRect("GrudgeRow_" + g.PeerPlayerId, scrollContent, 0f, i * rowH, hostW - 12f, 122f);
                var bg = QdaoUguiFactory.CreateImage("RowBg", row, 0f, 0f, hostW - 12f, 122f, LoadOptionalSkin("grudge_row_bg"));
                bg.type = bg.sprite != null && bg.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                if (bg.sprite == null) bg.color = CardTint;
                bg.raycastTarget = false;

                string onlineTag = g.PeerOnline ? "● 在线" : "○ 离线";
                Text(row, $"{SafeName(g.PeerName, g.PeerPlayerId)}  (ID:{g.PeerPlayerId.ToString(CultureInfo.InvariantCulture)})",
                    38f, 14f, 444f, 36f, 25f, Ink).raycastTarget = false;
                Text(row, onlineTag, 490f, 16f, 120f, 32f, 21f, g.PeerOnline ? JadeAccent : Muted).raycastTarget = false;

                string recordLine = $"历史交手：{g.Wins} 胜  {g.Losses} 负  {g.Draws} 平";
                Text(row, recordLine, 626f, 16f, 310f, 32f, 22f, Ink).raycastTarget = false;

                string streakBadge = JianghuModels.FormatStreakBadge(g.WinStreak, g.LoseStreak);
                if (!string.IsNullOrEmpty(streakBadge))
                {
                    bool isHot = g.WinStreak >= JianghuModels.StreakHighlightThreshold;
                    var badgeImg = QdaoUguiFactory.CreateImage("StreakBadge", row, 948f, 14f, 176f, 36f,
                        LoadOptionalSkin(isHot ? "badge_streak_hot" : "badge_revenge_ready"));
                    badgeImg.type = badgeImg.sprite != null && badgeImg.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                    badgeImg.pixelsPerUnitMultiplier = 1f;
                    if (badgeImg.sprite == null)
                        badgeImg.color = isHot ? new Color(0.86f, 0.65f, 0.24f, 0.92f) : new Color(0.75f, 0.36f, 0.28f, 0.92f);
                    badgeImg.raycastTarget = false;
                    var badgeTxt = Text(badgeImg.transform, streakBadge, 8f, 0f, 160f, 36f, 19f, Cream,
                        alignment: TextAlignmentOptions.Center);
                    badgeTxt.fontStyle = FontStyles.Bold;
                    badgeTxt.raycastTarget = false;
                }

                // Latest battle log summary + replay shortcut
                ulong latestBattleId = 0;
                if (g.RecentLogs.Count > 0 && g.RecentLogs[0] != null)
                {
                    var latest = g.RecentLogs[0];
                    latestBattleId = latest.BattleId;
                    string summary = $"最近一战：{JianghuModels.FormatOutcome(latest.OutcomeForSelf)} · {JianghuModels.FormatWritShort(latest.WritTier)} {latest.TeamSize}V{latest.TeamSize} · {latest.TotalRounds} 回合";
                    Text(row, summary, 38f, 66f, 884f, 34f, 20f, Muted).raycastTarget = false;
                }
                else
                {
                    Text(row, "仅展示在线状态，绝不泄露和平道友位置。", 38f, 66f, 884f, 34f, 20f, Muted).raycastTarget = false;
                }

                ulong peerId = g.PeerPlayerId;
                if (latestBattleId != 0 && _playReplayAction != null)
                {
                    var replayBtn = SkinButton(row, "GrudgeReplay_" + latestBattleId, "回放", 1150f, 30f, 126f, 60f,
                        () => _playReplayAction(latestBattleId), "btn_secondary", "button_secondary", false, 22f);
                    replayBtn.interactable = !_client.InBattle;
                }

                var rematchBtn = SkinButton(row, "GrudgeRematch_" + peerId, "再递战书", 1292f, 30f, 186f, 60f,
                    () =>
                    {
                        _draftTargetPlayerId = peerId.ToString(CultureInfo.InvariantCulture);
                        _activeTab = JianghuWindowTab.Challenge;
                        Render();
                    }, "btn_primary", "button_primary", true, 23f);
                rematchBtn.interactable = !busy;

                var dismissBtn = SkinButton(row, "GrudgeDismiss_" + peerId, "一笔勾销", 1492f, 30f, 174f, 60f,
                    () => _client?.DismissGrudge(peerId), "btn_secondary", "button_secondary", false, 22f);
                dismissBtn.interactable = !busy;
            }
        }

        private void BuildGuildWallTab()
        {
            float hostW = ContentWidth - 48f; // 1700
            float hostH = ContentHeight - 36f; // 530

            var wall = _client.GuildWall;
            if (wall == null || wall.GuildId == 0)
            {
                string hint = !string.IsNullOrEmpty(_client.GuildWallHint)
                    ? _client.GuildWallHint
                    : (_client.GuildWallPending ? "正在同步帮派恩怨墙…" : "加入帮派后可查看本帮纯帮派生死状战绩与恩怨墙。");
                Text(_contentHost, hint, 40f, 170f, hostW - 80f, 100f, 25f, Muted, wrap: true,
                    alignment: TextAlignmentOptions.Center).raycastTarget = false;
                return;
            }

            // Left column: Rival guild summaries (560px)
            float leftW = 556f;
            var leftPanel = QdaoUguiFactory.CreateRect("GuildRivalsPanel", _contentHost, 0f, 0f, leftW, hostH);
            var leftBg = leftPanel.gameObject.AddComponent<Image>();
            leftBg.color = CardTint;
            leftBg.raycastTarget = false;

            var leftTitle = Text(leftPanel, $"宿敌帮派榜（{wall.Rivals.Count.ToString(CultureInfo.InvariantCulture)}）",
                20f, 14f, leftW - 40f, 36f, 24f, Ink);
            leftTitle.fontStyle = FontStyles.Bold;
            leftTitle.raycastTarget = false;

            if (wall.Rivals.Count == 0)
            {
                Text(leftPanel, "本帮暂无与其他帮派的纯帮派生死状交锋。", 20f, 140f, leftW - 40f, 80f, 21f, Muted,
                    wrap: true, alignment: TextAlignmentOptions.Center).raycastTarget = false;
            }
            else
            {
                var rScroll = CreateVerticalScroll(leftPanel, "GuildRivalsScroll", 12f, 58f, leftW - 24f, hostH - 70f,
                    wall.Rivals.Count * 112f);
                for (int i = 0; i < wall.Rivals.Count; i++)
                {
                    var r = wall.Rivals[i];
                    var card = QdaoUguiFactory.CreateRect("GuildRival_" + r.RivalGuildId, rScroll, 0f, i * 112f, leftW - 24f, 102f);
                    var img = QdaoUguiFactory.CreateImage("CardBg", card, 0f, 0f, leftW - 24f, 102f, LoadOptionalSkin("guild_rival_card"));
                    img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                    img.pixelsPerUnitMultiplier = 1f;
                    if (img.sprite == null) img.color = Color.white;
                    img.raycastTarget = false;
                    Text(card, $"【{SafeName(r.RivalGuildName, r.RivalGuildId)}】", 36f, 14f, leftW - 78f, 34f, 24f, Ink).raycastTarget = false;
                    Text(card, $"总交锋：{r.Wins} 胜  {r.Losses} 负  {r.Draws} 平", 36f, 56f, leftW - 78f, 30f, 21f, JadeAccent).raycastTarget = false;
                }
            }

            // Right column: Recent 20 guild duel records
            float rightX = 576f;
            float rightW = hostW - rightX;
            var rightPanel = QdaoUguiFactory.CreateRect("GuildRecordsPanel", _contentHost, rightX, 0f, rightW, hostH);
            var rightBg = rightPanel.gameObject.AddComponent<Image>();
            rightBg.color = CardTint;
            rightBg.raycastTarget = false;

            var rightTitle = Text(rightPanel, $"近 20 场纯帮派生死状战绩（{wall.RecentRecords.Count.ToString(CultureInfo.InvariantCulture)}）",
                20f, 14f, rightW - 40f, 36f, 24f, Ink);
            rightTitle.fontStyle = FontStyles.Bold;
            rightTitle.raycastTarget = false;

            if (wall.RecentRecords.Count == 0)
            {
                Text(rightPanel, "暂无纯帮派生死状记录。双方成员全部隶属各自帮派的生死状对决将自动上墙留名。",
                    24f, 140f, rightW - 48f, 90f, 21f, Muted, wrap: true, alignment: TextAlignmentOptions.Center).raycastTarget = false;
            }
            else
            {
                var lScroll = CreateVerticalScroll(rightPanel, "GuildRecordsScroll", 12f, 58f, rightW - 24f, hostH - 70f,
                    wall.RecentRecords.Count * 100f);
                for (int i = 0; i < wall.RecentRecords.Count; i++)
                {
                    var rec = wall.RecentRecords[i];
                    var row = QdaoUguiFactory.CreateRect("GuildRec_" + rec.BattleId, lScroll, 0f, i * 100f, rightW - 24f, 92f);
                    var img = QdaoUguiFactory.CreateImage("RowBg", row, 0f, 0f, rightW - 24f, 92f, LoadOptionalSkin("guild_record_row"));
                    img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                    img.pixelsPerUnitMultiplier = 1f;
                    if (img.sprite == null) img.color = rec.WritTier == (uint)global::Match.JianghuWritTier.GoldItem ? GoldCardTint : Color.white;
                    img.raycastTarget = false;

                    string outcome = JianghuModels.FormatOutcome(rec.OutcomeForGuild);
                    Color outcomeColor = rec.OutcomeForGuild == 1 ? JadeAccent : (rec.OutcomeForGuild == 2 ? DangerInk : Muted);
                    Text(row, $"[{outcome}] 对阵【{SafeName(rec.RivalGuildName, rec.RivalGuildId)}】 · {JianghuModels.FormatWritShort(rec.WritTier)} {rec.TeamSize}V{rec.TeamSize}",
                        18f, 12f, rightW - 200f, 34f, 22f, outcomeColor).raycastTarget = false;
                    Text(row, $"领衔：{rec.OurCaptainName} vs {rec.RivalCaptainName} · {rec.TotalRounds} 回合",
                        18f, 50f, rightW - 200f, 28f, 19f, Muted).raycastTarget = false;

                    ulong battleId = rec.BattleId;
                    if (battleId != 0 && _playReplayAction != null)
                    {
                        var btn = SkinButton(row, "GuildReplay_" + battleId, "回放", rightW - 166f, 18f, 122f, 54f,
                            () => _playReplayAction(battleId), "btn_secondary", "button_secondary", false, 21f);
                        btn.interactable = !_client.InBattle;
                    }
                }
            }
        }

        private void HandlePeaceToggleClicked()
        {
            if (_client == null) return;
            bool next = !(_client.Profile?.DuelRefuseStrangers ?? false);
            _client.SetRefuseStrangers(next);
        }

        private void HandleSendInviteClicked()
        {
            if (_client == null) return;
            string raw = (_draftTargetPlayerId ?? string.Empty).Trim();
            if (!ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out ulong targetId) || targetId == 0)
            {
                _statusText.text = "请输入有效的对方玩家 ID（纯数字）。";
                _statusText.color = DangerInk;
                return;
            }

            List<ulong> challengerIds = null;
            List<ulong> targetIds = null;
            var teamClient = TeamUiRoot.Instance?.Client;
            if (teamClient != null && teamClient.HasTeam && teamClient.Snapshot.Members.Count >= 2)
            {
                challengerIds = new List<ulong>(teamClient.Snapshot.Members.Count);
                for (int i = 0; i < teamClient.Snapshot.Members.Count; i++)
                {
                    var m = teamClient.Snapshot.Members[i];
                    if (m != null && m.PlayerId != 0) challengerIds.Add(m.PlayerId);
                }
                targetIds = new List<ulong> { targetId };
                if (!string.IsNullOrWhiteSpace(_draftTargetMembers))
                {
                    var parts = _draftTargetMembers.Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (ulong.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong mid)
                            && mid != 0 && !targetIds.Contains(mid))
                        {
                            targetIds.Add(mid);
                        }
                    }
                }
            }

            _client.CreateDuelInvite(targetId, _selectedWritTier, challengerIds, targetIds);
        }

        private void OpenClearModal()
        {
            if (_clearOverlay == null || _client?.Profile == null || !_client.Profile.HasActiveDefeatMark) return;
            _clearOverlay.gameObject.SetActive(true);
            RefreshClearModal();
        }

        private void CloseClearModal()
        {
            if (_clearOverlay != null) _clearOverlay.gameObject.SetActive(false);
        }

        private void RefreshClearModal()
        {
            if (_clearOverlay == null || !_clearOverlay.gameObject.activeSelf) return;
            var profile = _client?.Profile;
            if (profile == null || !profile.HasActiveDefeatMark)
            {
                CloseClearModal();
                return;
            }
            ulong silverCost = profile.SilverClearCost > 0 ? profile.SilverClearCost : JianghuModels.DefaultSilverClearCost;
            uint charms = profile.ClearCharmItemCount;
            _clearBodyText.text =
                $"当前印记：{JianghuModels.FormatDefeatMark(profile)}\n\n" +
                $"方式一（普通洗尘）：消耗 {silverCost.ToString(CultureInfo.InvariantCulture)} 可流通银两（当前持有 {profile.SilverBalance.ToString(CultureInfo.InvariantCulture)}），立即清除败绩印记。\n" +
                $"方式二（清风洗尘符）：消耗绑定道具「清风洗尘符」×1（当前持有 {charms.ToString(CultureInfo.InvariantCulture)}），清印并附赠 30 分钟「清风拂尘」洒脱徽记。";

            bool busy = _client.WritePending || _client.InBattle;
            _clearSilverBtn.interactable = !busy && profile.SilverBalance >= silverCost;
            bool hasCharm = charms > 0;
            _clearCharmBtn.gameObject.SetActive(hasCharm);
            _clearCharmBtn.interactable = !busy && hasCharm;
            _clearBuyCharmBtn.gameObject.SetActive(!hasCharm && _openSpiritShopAction != null);
        }

        private void ExecuteClearMark(global::JianghuClearMarkMode mode)
        {
            CloseClearModal();
            _client?.ClearDefeatMark(mode);
        }

        private void JumpToSpiritShop(uint goodsId)
        {
            Hide();
            _openSpiritShopAction?.Invoke(goodsId);
        }

        private void UpdateStatusLine()
        {
            if (_statusText == null) return;
            string msg = _client?.Status ?? string.Empty;
            bool isErr = _client?.StatusIsError ?? false;
            if (string.IsNullOrEmpty(msg) && _client != null && _client.InBattle)
                msg = "战斗或观战期间仅可浏览江湖恩怨录，结束后即可发起或应战。";
            _statusText.text = msg;
            _statusText.color = isErr ? DangerInk : Muted;
        }

        private static string FormatSquadConfirmSummary(global::Match.JianghuDuelInviteView inv)
        {
            var members = inv.State == global::Match.JianghuDuelInviteState.AwaitingTargetTeam
                ? inv.TargetMembers
                : inv.ChallengerMembers;
            int confirmed = 0;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] != null && members[i].Confirmed) confirmed++;
            }
            return $"小队全员确认进度：{confirmed}/{members.Count}（全员同意后方扣费开战）";
        }

        private static string SafeName(string name, ulong id)
            => !string.IsNullOrWhiteSpace(name) ? name.Trim() : $"道友 {id.ToString(CultureInfo.InvariantCulture)}";

        private static RectTransform CreateVerticalScroll(
            UnityEngine.Transform parent,
            string name,
            float x,
            float y,
            float w,
            float h,
            float contentHeight)
        {
            var viewport = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var maskImg = viewport.gameObject.AddComponent<Image>();
            maskImg.color = new Color(1f, 1f, 1f, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = QdaoUguiFactory.CreateRect(name + "Content", viewport, 0f, 0f, w, Mathf.Max(h, contentHeight));
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            return content;
        }

        internal static Sprite LoadOptionalSkin(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            return Resources.Load<Sprite>(SkinRoot + relativePath.TrimStart('/'));
        }

        internal static Image CreateSkinOrFallbackArt(RectTransform parent, string name, string skinRelPath,
            string fallbackGameplayKey, float x, float y, float w, float h)
        {
            Sprite sprite = LoadOptionalSkin(skinRelPath) ?? Load(fallbackGameplayKey);
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = 1f;
            img.raycastTarget = false;
            return img;
        }

        internal static Button SkinButton(RectTransform parent, string name, string label,
            float x, float y, float w, float h, Action onClick,
            string skinRelPath, string fallbackKey, bool primary, float fontSize)
        {
            Sprite sprite = LoadOptionalSkin(skinRelPath) ?? Load(fallbackKey);
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 0.95f, 0.80f);
            colors.selectedColor = new Color(1f, 0.94f, 0.77f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var txt = Text(btn.transform, label, 12f, 0f, w - 24f, h, fontSize,
                primary ? Cream : Ink, alignment: TextAlignmentOptions.Center);
            txt.raycastTarget = false;
            return btn;
        }

        private void ApplySafeArea()
        {
            if (_frame == null || Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            float topPad = Mathf.Clamp((Screen.height - (safe.y + safe.height)) / Screen.height * 1080f, 0f, 60f);
            _frame.anchoredPosition = new Vector2(0f, -topPad * 0.5f);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Bind(null);
            Closed = null;
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
                else UnityEngine.Object.DestroyImmediate(_root.gameObject);
            }
        }
    }
}
