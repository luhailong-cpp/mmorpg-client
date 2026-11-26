using System;
using System.Collections.Generic;
using System.Globalization;
using Match;
using MmorpgClient.Game.Tournament;
using MmorpgClient.Table;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Tournament
{
    using Transform = UnityEngine.Transform;

    /// <summary>
    /// 《群仙问道赛》主窗口 uGUI 视图（1640×900 嵌入 1920×1080 安全区容器）：
    /// - 严格单向数据流：只读取 TournamentOverviewSnapshot / TournamentStandingsSnapshot 与 TournamentUiModel，
    ///   所有点击经事件回抛给 TournamentUiRoot 调 TournamentClient / SpectateClient / BattleReportClient / MailUiRoot；
    /// - 兼容手机横屏 safeArea 与微信/抖音小游戏右上角 140px 胶囊避让（关闭钮避开右上角）；
    /// - 弹窗含 TMP_InputField 时仅在弹窗切换/报错变更时重建，避免刷新打断玩家输入。
    /// </summary>
    public sealed class TournamentWindow
    {
        private const float WinW = TournamentArtCatalog.WindowWidth;
        private const float WinH = TournamentArtCatalog.WindowHeight;
        private const float PadX = 68f;
        private const float InnerW = WinW - PadX * 2f; // 1504

        public RectTransform Root { get; }
        public bool IsVisible => Root != null && Root.gameObject.activeSelf;

        public event Action CloseRequested;
        public event Action RefreshOverviewRequested;
        public event Action<uint, uint> RequestStandingsRequested;
        public event Action<string> CreateSquadSubmitted;
        public event Action<ulong> InviteMemberSubmitted;
        public event Action<ulong, bool> RespondInviteClicked;
        public event Action<ulong> LeaveOrKickClicked;
        public event Action<IReadOnlyList<ulong>> UpdateLineupSubmitted;
        public event Action RegisterSquadConfirmed;
        public event Action<ulong, IReadOnlyList<ulong>> StartMatchSubmitted;
        public event Action<ulong> CheerSquadClicked;
        public event Action<uint> ClaimRewardClicked;
        public event Action<uint> SaluteChampionConfirmed;
        public event Action<ulong> WatchBattleClicked;
        public event Action<ulong> WatchReplayClicked;
        public event Action OpenMailClicked;
        public event Action<TournamentInviteQuickSource> InviteSourceRequested;

        private readonly TournamentUiModel _model;
        private readonly Func<ulong, string> _resolveCharacterId;

        private readonly RectTransform _windowPanel;
        private readonly TMP_Text _seasonTopLabel;
        private readonly RectTransform _stageRailRoot;
        private readonly RectTransform _tabBarRoot;
        private readonly RectTransform _pageRoot;
        private readonly TMP_Text _footerStatus;
        private readonly RectTransform _modalBackdrop;
        private readonly RectTransform _modalContent;
        private readonly RectTransform _toastBox;
        private readonly Image _toastBg;
        private readonly TMP_Text _toastText;

        private TMP_Text _activeStageSubLabel;
        private TMP_Text _heroStageTagLabel;
        private TMP_Text _intermissionCountdownLabel;
        private TournamentLeylineElement _renderedInspectLeyline = TournamentLeylineElement.TournamentLeylineUnspecified;
        private bool _renderedSalutedToday;
        private ulong _renderedSaluteCount;
        private float _toastHideAt = -1f;

        private TournamentModalKind _renderedModal = TournamentModalKind.None;
        private string _renderedModalError = string.Empty;
        private int _renderedLineupSig = -1;
        private int _renderedInviteSig = -1;
        private TMP_InputField _createInput;
        private TMP_Text _createCounter;
        private TMP_InputField _inviteInput;
        private RectTransform _inviteCandidatesHost;

        private TournamentOverviewSnapshot _lastOverview;
        private TournamentStandingsSnapshot _lastStandings;
        private ulong _lastPlayerId;
        private bool _lastBusy;
        private string _lastStatus = string.Empty;
        private IReadOnlyList<TournamentInviteCandidate> _lastInviteCandidates = Array.Empty<TournamentInviteCandidate>();
        private string _lastInviteSourceStatus = string.Empty;

        public TournamentWindow(Transform fittedParent, TournamentUiModel model, Func<ulong, string> resolveCharacterId)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _resolveCharacterId = resolveCharacterId;

            Root = QdaoUguiFactory.CreateStretch("TournamentWindowRoot", fittedParent, Vector4.zero);

            // 窗口本体：1640×900 居中，外留左右各 140px 与上下各 90px，天然满足小游戏右上角胶囊避让
            _windowPanel = QdaoUguiFactory.CreateCenteredRect("TournamentMainFrame", Root, WinW, WinH);
            TournamentArtCatalog.SpriteBox("FrameBg", _windowPanel, 0f, 0f, WinW, WinH, "window-frame", sliced: true, raycast: true);

            // 顶部牌匾：群仙问道赛
            var plaque = TournamentArtCatalog.SpriteBox("TitlePlaque", _windowPanel, (WinW - 480f) * 0.5f, -40f, 480f, 82f, "title-plate");
            var title = TournamentArtCatalog.Label("TitleText", plaque.transform, 20f, 4f, 440f, 74f,
                "群仙问道赛", 42f, TournamentArtCatalog.PrimaryBtnText, TextAlignmentOptions.Center, bold: true);
            title.characterSpacing = 12f;

            // 顶栏信息（右侧留出避让区放置关闭钮与流苏）
            TournamentArtCatalog.SpriteBox("GameTaiji", _windowPanel, PadX, 58f, 24f, 24f, "taiji-badge", preserveAspect: true);
            TournamentArtCatalog.Label("GameSubtitle", _windowPanel, PadX + 32f, 54f, 480f, 32f,
                "五行奇谈 · 全服赛季战队锦标赛", 18f, TournamentArtCatalog.GoldTagText, bold: true);
            _seasonTopLabel = TournamentArtCatalog.Label("SeasonTopLabel", _windowPanel, WinW - 580f, 54f, 380f, 32f,
                "正在同步赛季信息…", 16f, TournamentArtCatalog.GoldTagText, TextAlignmentOptions.MidlineRight);

            // 关闭钮与红穗（位于窗内右上角，距离画布最右边缘 >= 195px，完整避开小游戏 140px 胶囊区）
            var closeBtn = QdaoUguiFactory.CreateArtButton("CloseButton", _windowPanel, WinW - 108f, 42f, 52f, 52f,
                TournamentArtCatalog.Load("close-button"), out var closeImg);
            closeImg.preserveAspect = true;
            closeBtn.onClick.AddListener(() => CloseRequested?.Invoke());
            TournamentArtCatalog.SpriteBox("CloseTassel", _windowPanel, WinW - 92f, 90f, 22f, 58f, "close-tassel");

            // 四阶段时间轴
            _stageRailRoot = QdaoUguiFactory.CreateRect("StageRail", _windowPanel, PadX, 92f, InnerW - 60f, 70f);
            TournamentArtCatalog.Box("StageRailDivider", _windowPanel, PadX, 166f, InnerW, 1.5f, TournamentArtCatalog.Line);

            // 五个主分页栏
            _tabBarRoot = QdaoUguiFactory.CreateRect("TabBar", _windowPanel, PadX, 174f, InnerW, 52f);
            TournamentArtCatalog.Box("TabBarDivider", _windowPanel, PadX, 226f, InnerW, 2f, TournamentArtCatalog.Line);

            // 主内容区
            _pageRoot = QdaoUguiFactory.CreateRect("PageBody", _windowPanel, PadX, 236f, InnerW, 600f);

            // 底栏状态提示
            TournamentArtCatalog.Box("FooterDivider", _windowPanel, PadX, WinH - 54f, InnerW, 1.5f, TournamentArtCatalog.Line);
            TournamentArtCatalog.SpriteBox("NoticeIcon", _windowPanel, PadX, WinH - 42f, 20f, 20f, "notice-icon", preserveAspect: true);
            _footerStatus = TournamentArtCatalog.Label("FooterStatus", _windowPanel, PadX + 28f, WinH - 46f, 880f, 28f,
                "全服跨区自由组队 · 免费报名 · 绑定自用奖励", 15f, TournamentArtCatalog.Muted);
            TournamentArtCatalog.Label("FooterRightNote", _windowPanel, WinW - PadX - 460f, WinH - 46f, 460f, 28f,
                "每季 1 票免费助威 · 零付费报名", 14f, TournamentArtCatalog.Muted, TextAlignmentOptions.MidlineRight);

            // 弹窗层
            _modalBackdrop = QdaoUguiFactory.CreateStretch("ModalBackdrop", Root, Vector4.zero);
            var modalDim = _modalBackdrop.gameObject.AddComponent<Image>();
            modalDim.color = new Color(0.08f, 0.16f, 0.12f, 0.62f);
            modalDim.raycastTarget = true;
            _modalContent = QdaoUguiFactory.CreateCenteredRect("ModalHost", _modalBackdrop, 580f, 520f);
            _modalBackdrop.gameObject.SetActive(false);

            // 顶部 Toast（独立悬浮在最上层）
            _toastBox = QdaoUguiFactory.CreateRect("ToastBanner", Root, (TournamentArtCatalog.DesignWidth - 680f) * 0.5f, 18f, 680f, 48f);
            var toastBorder = _toastBox.gameObject.AddComponent<Image>();
            toastBorder.color = TournamentArtCatalog.GoldSoft;
            toastBorder.raycastTarget = false;
            var toastInner = QdaoUguiFactory.CreateStretch("Bg", _toastBox, new Vector4(1.5f, 1.5f, 1.5f, 1.5f));
            _toastBg = toastInner.gameObject.AddComponent<Image>();
            _toastBg.color = TournamentArtCatalog.Jade;
            _toastBg.raycastTarget = false;
            _toastText = TournamentArtCatalog.Label("ToastText", _toastBox, 16f, 4f, 648f, 40f,
                string.Empty, 18f, TournamentArtCatalog.PrimaryBtnText, TextAlignmentOptions.Center, bold: true);
            _toastBox.gameObject.SetActive(false);

            Root.gameObject.SetActive(false);
        }

        public void Show() => Root.gameObject.SetActive(true);

        public void Hide()
        {
            _model.CloseModal();
            ForceCloseModalVisual();
            Root.gameObject.SetActive(false);
        }

        public void ShowToast(string message, bool isError, float nowRealtimeSeconds)
        {
            if (string.IsNullOrEmpty(message)) return;
            _toastBg.color = isError ? TournamentArtCatalog.ToastErrorBg : TournamentArtCatalog.Jade;
            _toastText.text = message;
            _toastBox.gameObject.SetActive(true);
            _toastHideAt = nowRealtimeSeconds + 3.2f;
        }

        public void Tick(float nowRealtimeSeconds)
        {
            if (_toastHideAt > 0f && nowRealtimeSeconds >= _toastHideAt)
            {
                _toastHideAt = -1f;
                if (_toastBox != null) _toastBox.gameObject.SetActive(false);
            }
            if (!IsVisible || _lastOverview == null || !_lastOverview.HasSeason) return;
            ulong rem = _model.ComputeStageRemainingMs(_lastOverview, nowRealtimeSeconds);
            string cd = rem > 0 ? $"剩余 {TournamentFormatting.FormatCountdown(rem)}" : "阶段即将切换";
            if (_activeStageSubLabel != null) _activeStageSubLabel.text = cd;
            if (_heroStageTagLabel != null && _lastOverview.Season.Stage == TournamentStage.Registration)
                _heroStageTagLabel.text = $"报名中 · {cd}";
            if (_intermissionCountdownLabel != null && _model.ActiveModal == TournamentModalKind.IntermissionSwap)
            {
                var active = _lastOverview.MyActiveMatch;
                if (active != null && active.State == TournamentMatchState.Intermission)
                {
                    ulong interRem = _model.ComputeIntermissionRemainingMs(_lastOverview, active, nowRealtimeSeconds);
                    _intermissionCountdownLabel.text = interRem > 0UL
                        ? $"局间换阵倒计时  {TournamentFormatting.FormatCountdown(interRem)}"
                        : "倒计时已归零 · 等待服务端自动开战…";
                }
                else
                {
                    _intermissionCountdownLabel.text = "本局准备状态已更新";
                }
            }
        }

        public void Render(
            TournamentOverviewSnapshot overview,
            TournamentStandingsSnapshot standings,
            ulong playerId,
            bool isBusy,
            string status,
            float nowRealtimeSeconds,
            IReadOnlyList<TournamentInviteCandidate> inviteCandidates = null,
            string inviteSourceStatus = null)
        {
            _lastOverview = overview;
            _lastStandings = standings;
            _lastPlayerId = playerId;
            _lastBusy = isBusy;
            _lastStatus = status ?? string.Empty;
            _lastInviteCandidates = inviteCandidates ?? Array.Empty<TournamentInviteCandidate>();
            _lastInviteSourceStatus = inviteSourceStatus ?? string.Empty;

            if (overview != null) _model.ObserveOverview(overview, nowRealtimeSeconds);

            RenderTopLine(overview);
            RenderStageRail(overview, nowRealtimeSeconds);
            RenderTabBar(overview);
            RenderActivePage(overview, standings, playerId, isBusy, nowRealtimeSeconds);
            _footerStatus.text = !string.IsNullOrEmpty(_lastStatus)
                ? _lastStatus
                : "全服跨区自由组队 · 免费报名 · 绑定自用奖励";

            SyncModalVisual(overview, isBusy);
        }

        private void RenderTopLine(TournamentOverviewSnapshot overview)
        {
            if (overview == null || !overview.HasSeason)
            {
                _seasonTopLabel.text = _model.ShowNotOpenState ? "赛事暂未开启" : "正在同步赛季快照…";
                return;
            }
            string sName = !string.IsNullOrEmpty(overview.Season.SeasonName)
                ? overview.Season.SeasonName
                : $"第 {overview.Season.SeasonId} 赛季";
            _seasonTopLabel.text = $"{sName}  {TournamentFormatting.StageDisplayName(overview.Season.Stage)}";
        }

        private void RenderStageRail(TournamentOverviewSnapshot overview, float nowRealtimeSeconds)
        {
            TournamentArtCatalog.ClearChildren(_stageRailRoot);
            _activeStageSubLabel = null;

            TournamentStage curStage = overview?.Season?.Stage ?? TournamentStage.Unspecified;
            int activeIdx = curStage switch
            {
                TournamentStage.Registration => 0,
                TournamentStage.Qualifier => 1,
                TournamentStage.Knockout => 2,
                TournamentStage.Settled => 3,
                _ => -1,
            };

            string[] titles = { "赛季报名", "宽窗海选", "全服淘汰赛", "封榜发奖" };
            float colW = (_stageRailRoot.sizeDelta.x) / 4f;
            ulong remMs = overview != null ? _model.ComputeStageRemainingMs(overview, nowRealtimeSeconds) : 0UL;
            string activeCd = remMs > 0 ? $"剩余 {TournamentFormatting.FormatCountdown(remMs)}" : "进行中";

            for (int i = 0; i < 4; i++)
            {
                float x = i * colW;
                bool done = activeIdx >= 0 && i < activeIdx;
                bool active = i == activeIdx;

                if (i < 3)
                    TournamentArtCatalog.Box($"StageLine_{i}", _stageRailRoot, x + 210f, 34f, colW - 224f, 1.5f, TournamentArtCatalog.Line);

                Color circleBg = active ? TournamentArtCatalog.Jade : (done ? TournamentArtCatalog.JadeSoft : TournamentArtCatalog.CardAltBg);
                Color circleBorder = active ? TournamentArtCatalog.GoldSoft : (done ? TournamentArtCatalog.Jade : TournamentArtCatalog.Line);
                Color circleText = active ? TournamentArtCatalog.PrimaryBtnText : (done ? TournamentArtCatalog.JadeDeep : TournamentArtCatalog.Muted);

                var medal = TournamentArtCatalog.BorderedCard($"StageMedal_{i}", _stageRailRoot, x, 14f, 42f, 42f, circleBg, circleBorder, 2f);
                TournamentArtCatalog.Label("Num", medal, 0f, 0f, 42f, 42f, done ? "✓" : (i + 1).ToString(CultureInfo.InvariantCulture),
                    20f, circleText, TextAlignmentOptions.Center, bold: true);

                TournamentArtCatalog.Label($"StageTitle_{i}", _stageRailRoot, x + 52f, 10f, 160f, 28f,
                    titles[i], 20f, active ? TournamentArtCatalog.JadeDeep : TournamentArtCatalog.Muted, bold: true);
                string sub = active ? activeCd : (done ? "已完成" : "即将开启");
                var subLbl = TournamentArtCatalog.Label($"StageSub_{i}", _stageRailRoot, x + 52f, 38f, 175f, 22f,
                    sub, 14f, active ? TournamentArtCatalog.GoldTagText : TournamentArtCatalog.Muted);
                if (active) _activeStageSubLabel = subLbl;
            }
        }

        private void RenderTabBar(TournamentOverviewSnapshot overview)
        {
            TournamentArtCatalog.ClearChildren(_tabBarRoot);
            (TournamentTab tab, string label)[] tabs =
            {
                (TournamentTab.Overview, "赛事总览"),
                (TournamentTab.Squad, "我的战队"),
                (TournamentTab.Standings, "全服榜与对阵"),
                (TournamentTab.Rewards, "赛季奖励"),
                (TournamentTab.Hall, "冠军殿堂"),
            };

            float x = 0f;
            int pendingInvites = overview != null && !overview.HasSquad ? overview.PendingInvites.Count : 0;
            for (int i = 0; i < tabs.Length; i++)
            {
                var entry = tabs[i];
                bool selected = _model.ActiveTab == entry.tab;
                float w = entry.tab == TournamentTab.Standings ? 190f : 156f;
                string caption = entry.tab == TournamentTab.Squad && pendingInvites > 0
                    ? $"{entry.label} ({pendingInvites})"
                    : entry.label;
                var tabKey = entry.tab;
                TournamentArtCatalog.FlatButton($"Tab_{entry.tab}", _tabBarRoot, x, 4f, w, 46f,
                    caption,
                    selected ? TournamentArtCatalog.Jade : TournamentArtCatalog.CardAltBg,
                    selected ? TournamentArtCatalog.Gold : TournamentArtCatalog.Line,
                    selected ? TournamentArtCatalog.PrimaryBtnText : TournamentArtCatalog.SecondaryBtnText,
                    () => SwitchTab(tabKey),
                    interactable: true,
                    fontSize: 20f,
                    bold: true);
                x += w + 8f;
            }

            // 右侧手动刷新按钮
            TournamentArtCatalog.FlatButton("RefreshBtn", _tabBarRoot, InnerW - 110f, 8f, 110f, 36f,
                _lastBusy ? "同步中…" : "刷新",
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                () => RefreshOverviewRequested?.Invoke(),
                interactable: !_lastBusy, fontSize: 16f);
        }

        private void SwitchTab(TournamentTab tab)
        {
            if (_model.ActiveTab == tab) return;
            _model.ActiveTab = tab;
            if (tab == TournamentTab.Standings && _lastStandings == null)
                RequestStandingsRequested?.Invoke(_model.SelectedStandingsSeasonId, _model.StandingsPage);
            Render(_lastOverview, _lastStandings, _lastPlayerId, _lastBusy, _lastStatus, Time.realtimeSinceStartup);
        }

        private void RenderActivePage(
            TournamentOverviewSnapshot overview,
            TournamentStandingsSnapshot standings,
            ulong playerId,
            bool isBusy,
            float nowRealtimeSeconds)
        {
            TournamentArtCatalog.ClearChildren(_pageRoot);
            _heroStageTagLabel = null;

            if (_model.ShowNotOpenState || (overview != null && overview.HasSeason && !overview.IsEnabled))
            {
                RenderEmptyState("群仙问道赛 · 敬请期待",
                    "本赛季暂未开启，开放时间以全服赛季公告为准。");
                return;
            }

            if (overview == null)
            {
                RenderEmptyState("正在同步群仙问道赛…",
                    "请稍候，正在拉取最新赛季赛程、战队名册与全服荣誉。");
                return;
            }

            switch (_model.ActiveTab)
            {
                case TournamentTab.Overview:
                    RenderOverviewTab(overview, standings, playerId, isBusy, nowRealtimeSeconds);
                    break;
                case TournamentTab.Squad:
                    RenderSquadTab(overview, playerId, isBusy);
                    break;
                case TournamentTab.Standings:
                    RenderStandingsTab(overview, standings, isBusy);
                    break;
                case TournamentTab.Rewards:
                    RenderRewardsTab(overview, isBusy);
                    break;
                case TournamentTab.Hall:
                    RenderHallTab(overview);
                    break;
            }
        }

        private void RenderEmptyState(string heading, string body)
        {
            var card = TournamentArtCatalog.BorderedCard("EmptyCard", _pageRoot, 120f, 80f, InnerW - 240f, 390f,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
            TournamentArtCatalog.SpriteBox("EmptyTaiji", card, (card.sizeDelta.x - 92f) * 0.5f, 52f, 92f, 92f,
                "taiji-badge", preserveAspect: true);
            TournamentArtCatalog.Label("Heading", card, 40f, 168f, card.sizeDelta.x - 80f, 44f,
                heading, 30f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
            TournamentArtCatalog.Label("Body", card, 80f, 224f, card.sizeDelta.x - 160f, 90f,
                body, 19f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center, wrap: true);
        }

        // ==================== 分页 1：赛事总览 ====================

        private void RenderOverviewTab(
            TournamentOverviewSnapshot overview,
            TournamentStandingsSnapshot standings,
            ulong playerId,
            bool isBusy,
            float nowRealtimeSeconds)
        {
            float leftW = 886f;
            float rightX = leftW + 22f;
            float rightW = InnerW - rightX;

            var season = overview.Season;
            uint teamSize = season.BattleTeamSize == 0 ? 3u : season.BattleTeamSize;
            uint subSlots = season.RosterMaxSize > teamSize ? season.RosterMaxSize - teamSize : 1u;
            uint maxQual = season.QualifierMaxMatches == 0 ? 6u : season.QualifierMaxMatches;
            uint minPart = season.ParticipationMinMatches == 0 ? 2u : season.ParticipationMinMatches;
            ulong remMs = _model.ComputeStageRemainingMs(overview, nowRealtimeSeconds);
            string cd = remMs > 0 ? $"剩余 {TournamentFormatting.FormatCountdown(remMs)}" : "进行中";

            // 左上主视觉卡（310px 高）
            var hero = TournamentArtCatalog.BorderedCard("HeroCard", _pageRoot, 0f, 0f, leftW, 310f,
                TournamentArtCatalog.Paper, TournamentArtCatalog.Gold, 2f);
            string kicker = !string.IsNullOrEmpty(season.SeasonName) ? season.SeasonName : $"第 {season.SeasonId} 赛季 · 群仙问道赛";
            string heroTitle, heroCopy, stageTag, sealCaption;
            switch (season.Stage)
            {
                case TournamentStage.Qualifier:
                    heroTitle = "七日海选，随心应战";
                    heroCopy = $"宽时间窗内任意组队完成最多 {maxQual} 场积分赛，使用本人真实装备、技能与出战灵兽上阵。";
                    stageTag = overview.HasSquad
                        ? $"海选积分赛 · 已打 {overview.MySquad.QualifierPlayed}/{maxQual} 场"
                        : $"宽窗海选 · {cd}";
                    sealCaption = "问道开战";
                    break;
                case TournamentStage.Knockout:
                    heroTitle = "全服争锋，冠军将见";
                    heroCopy = "晋级战队周末单败角逐，焦点赛事全服 L2 公开观战，精彩战报完整留存。";
                    stageTag = season.KnockoutTeamCount > 0 ? $"{season.KnockoutTeamCount} 强淘汰赛 · {cd}" : $"全服淘汰赛 · {cd}";
                    sealCaption = "全服围观";
                    break;
                case TournamentStage.Settled:
                    heroTitle = "本季总冠军，荣耀留名";
                    heroCopy = "冠军战队永久铭刻冠军殿堂；所有完赛与免费助威命中道友均可领取绑定赛季奖励。";
                    stageTag = $"封榜发奖 · {cd}";
                    sealCaption = "荣耀封榜";
                    break;
                default:
                    heroTitle = "召集同道，共赴问道之约";
                    heroCopy = $"{teamSize} 人首发 + {subSlots} 名替补，全服跨区自由组队。海选 7 天宽窗，打满 {minPart} 场即得参赛保底。";
                    stageTag = $"报名中 · {cd}";
                    sealCaption = "同道集结";
                    break;
            }

            TournamentArtCatalog.Label("HeroKicker", hero, 30f, 24f, 540f, 26f, kicker, 16f, TournamentArtCatalog.GoldTagText, bold: true);
            TournamentArtCatalog.Label("HeroTitle", hero, 30f, 58f, 570f, 54f, heroTitle, 38f, TournamentArtCatalog.JadeDeep, bold: true);
            TournamentArtCatalog.Label("HeroCopy", hero, 30f, 126f, 550f, 84f, heroCopy, 18f, TournamentArtCatalog.Ink, wrap: true);

            var tagCard = TournamentArtCatalog.BorderedCard("HeroStageTag", hero, 30f, 232f, 260f, 34f,
                TournamentArtCatalog.GoldTagBg, TournamentArtCatalog.Gold, 1.2f);
            _heroStageTagLabel = TournamentArtCatalog.Label("Text", tagCard, 8f, 0f, 244f, 34f,
                stageTag, 15f, TournamentArtCatalog.GoldTagText, TextAlignmentOptions.Center, bold: true);
            TournamentArtCatalog.Tag("HeroFreeTag", hero, 302f, 232f, 112f, 34f,
                "免费报名", TournamentArtCatalog.TagGreenBg, TournamentArtCatalog.Jade, TournamentArtCatalog.TagGreenText, 15f);

            // 右侧太极玉章装饰
            var seal = TournamentArtCatalog.BorderedCard("JadeSeal", hero, leftW - 244f, 36f, 212f, 212f,
                TournamentArtCatalog.JadeSoft, TournamentArtCatalog.Gold, 3f);
            TournamentArtCatalog.SpriteBox("SealTaiji", seal, 28f, 18f, 156f, 156f, "taiji-badge", preserveAspect: true);
            TournamentArtCatalog.Tag("SealCaption", hero, leftW - 224f, 224f, 172f, 34f,
                sealCaption, TournamentArtCatalog.Jade, TournamentArtCatalog.GoldSoft, TournamentArtCatalog.PrimaryBtnText, 17f);

            // 左下两张阶段自适应卡（高 264px）
            float subY = 328f;
            float subW = (leftW - 18f) * 0.5f;
            float subH = 264f;
            var subLeft = TournamentArtCatalog.BorderedCard("SubLeft", _pageRoot, 0f, subY, subW, subH,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
            var subRight = TournamentArtCatalog.BorderedCard("SubRight", _pageRoot, subW + 18f, subY, subW, subH,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);

            if (season.Stage == TournamentStage.Registration)
            {
                TournamentArtCatalog.Label("LTitle", subLeft, 22f, 18f, subW - 44f, 34f, "轻松参赛", 23f, TournamentArtCatalog.JadeDeep, bold: true);
                TournamentArtCatalog.Label("LBody", subLeft, 22f, 64f, subW - 44f, 176f,
                    $"{teamSize}V{teamSize} 真实养成 · {subSlots} 名替补随时顶上\n"
                    + $"7 天宽窗海选，自由完赛至多 {maxQual} 场\n"
                    + $"完赛 {minPart} 场可得绑定重铸砂与悟道残页\n"
                    + $"当前全服已锁定报名：{season.RegisteredSquadCount} 支战队",
                    17f, TournamentArtCatalog.Ink, wrap: true);

                TournamentArtCatalog.Label("RTitle", subRight, 22f, 18f, subW - 44f, 34f, "上届冠军", 23f, TournamentArtCatalog.JadeDeep, bold: true);
                var prev = overview.HallOfFame.Count > 0 ? overview.HallOfFame[0] : null;
                if (prev != null && !string.IsNullOrEmpty(prev.ChampionSquadName))
                {
                    TournamentArtCatalog.Label("ChampName", subRight, 22f, 68f, subW - 44f, 48f,
                        prev.ChampionSquadName, 32f, TournamentArtCatalog.Jade, bold: true);
                    TournamentArtCatalog.Label("ChampMembers", subRight, 22f, 126f, subW - 44f, 62f,
                        !string.IsNullOrEmpty(prev.ChampionMemberSummary) ? prev.ChampionMemberSummary : "以同道之名，永久留名冠军殿堂。",
                        16f, TournamentArtCatalog.Muted, wrap: true);
                }
                else
                {
                    TournamentArtCatalog.Label("ChampEmpty", subRight, 22f, 76f, subW - 44f, 92f,
                        "首届全服总冠军虚位以待，快召集同伴留名青史！", 18f, TournamentArtCatalog.Muted, wrap: true);
                }
                TournamentArtCatalog.FlatButton("OpenHallBtn", subRight, 22f, subH - 58f, 168f, 40f,
                    "查看冠军殿堂", TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                    () => SwitchTab(TournamentTab.Hall), fontSize: 16f);
            }
            else if (season.Stage == TournamentStage.Qualifier)
            {
                TournamentArtCatalog.Label("LTitle", subLeft, 22f, 16f, subW - 44f, 34f, "当前海选前 5 强", 22f, TournamentArtCatalog.JadeDeep, bold: true);
                if (standings == null)
                {
                    TournamentArtCatalog.Label("LoadingTop", subLeft, 22f, 74f, subW - 44f, 40f,
                        "点击刷新拉取当前海选积分前五强", 16f, TournamentArtCatalog.Muted);
                    TournamentArtCatalog.FlatButton("LoadStandingsBtn", subLeft, 22f, 130f, 164f, 38f,
                        "拉取海选榜", TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                        () => RequestStandingsRequested?.Invoke(0u, 1u), interactable: !isBusy, fontSize: 16f);
                }
                else if (standings.Standings.Count == 0)
                {
                    TournamentArtCatalog.Label("EmptyTop", subLeft, 22f, 76f, subW - 44f, 40f,
                        "暂未产生海选战绩，快去打响首战！", 16f, TournamentArtCatalog.Muted);
                }
                else
                {
                    int limit = Math.Min(5, standings.Standings.Count);
                    for (int i = 0; i < limit; i++)
                    {
                        var sq = standings.Standings[i];
                        float rowY = 58f + i * 38f;
                        TournamentArtCatalog.Label($"TopName_{i}", subLeft, 22f, rowY, subW - 126f, 32f,
                            $"{i + 1}  {sq.Name}", 16f, TournamentArtCatalog.Ink, bold: i < 3);
                        TournamentArtCatalog.Label($"TopPts_{i}", subLeft, subW - 104f, rowY, 82f, 32f,
                            $"{sq.QualifierPoints} 分", 16f, TournamentArtCatalog.Jade, TextAlignmentOptions.MidlineRight, bold: true);
                    }
                }
                RenderFeaturedSpectateCard(subRight, subW, subH, overview, "精选对局");
            }
            else if (season.Stage == TournamentStage.Knockout)
            {
                RenderFeaturedSpectateCard(subLeft, subW, subH, overview, "焦点激战中");

                TournamentArtCatalog.Label("RTitle", subRight, 22f, 18f, subW - 44f, 34f, "淘汰赛动态对阵", 22f, TournamentArtCatalog.JadeDeep, bold: true);
                string bracketInfo = season.KnockoutTeamCount >= 2
                    ? $"本季共 {season.KnockoutTeamCount} 强晋级单败淘汰，所有焦点局均开放全服 L2 观战。"
                    : "本季有效报名战队不足 2 支，未开启淘汰赛；完赛战队将按保底档发奖。";
                TournamentArtCatalog.Label("RBody", subRight, 22f, 64f, subW - 44f, 110f, bracketInfo, 17f, TournamentArtCatalog.Ink, wrap: true);
                TournamentArtCatalog.ArtButton("OpenBracketBtn", subRight, 22f, subH - 64f, 200f, 44f,
                    "查看淘汰对阵树", () =>
                    {
                        _model.BoardMode = TournamentBoardMode.Bracket;
                        SwitchTab(TournamentTab.Standings);
                    }, primary: true, fontSize: 18f);
            }
            else
            {
                TournamentArtCatalog.Label("LTitle", subLeft, 22f, 18f, subW - 44f, 34f, "本季全服总冠军", 22f, TournamentArtCatalog.JadeDeep, bold: true);
                string cName = !string.IsNullOrEmpty(season.ChampionSquadName) ? season.ChampionSquadName : "本季无冠军产生";
                TournamentArtCatalog.Label("ChampName", subLeft, 22f, 68f, subW - 44f, 48f, cName, 30f, TournamentArtCatalog.Jade, bold: true);
                TournamentArtCatalog.Label("ChampMem", subLeft, 22f, 128f, subW - 44f, 90f,
                    !string.IsNullOrEmpty(season.ChampionMemberSummary) ? season.ChampionMemberSummary : "已收录至历届冠军殿堂。",
                    16f, TournamentArtCatalog.Muted, wrap: true);

                TournamentArtCatalog.Label("RTitle", subRight, 22f, 18f, subW - 44f, 34f, "我的本季荣誉", 22f, TournamentArtCatalog.JadeDeep, bold: true);
                string myHonor = overview.HasSquad && overview.MySquad.FinalRank > 0
                    ? $"最终名次：第 {overview.MySquad.FinalRank} 名"
                    : (overview.MyRewards.Count > 0 ? "已获得本季赛季/助威奖励" : "本季未上榜，期待下季再战");
                TournamentArtCatalog.Label("HonorSummary", subRight, 22f, 70f, subW - 44f, 44f, myHonor, 22f, TournamentArtCatalog.Jade, bold: true);
                TournamentArtCatalog.ArtButton("ViewRewardsBtn", subRight, 22f, subH - 64f, 180f, 44f,
                    "查看赛季奖励", () => SwitchTab(TournamentTab.Rewards), primary: true, fontSize: 18f);
            }

            // 右侧我的战队 / 行动卡（全高 592px）
            RenderOverviewActionCard(overview, rightX, rightW, 592f, playerId, isBusy);
        }

        private void RenderFeaturedSpectateCard(RectTransform card, float w, float h, TournamentOverviewSnapshot overview, string title)
        {
            TournamentArtCatalog.Label("FTitle", card, 22f, 18f, w - 44f, 34f, title, 22f, TournamentArtCatalog.JadeDeep, bold: true);
            TournamentMatchView live = null;
            for (int i = 0; i < overview.FeaturedMatches.Count; i++)
            {
                var m = overview.FeaturedMatches[i];
                if (m != null && m.State == TournamentMatchState.InBattle && m.BattleId != 0UL)
                {
                    live = m;
                    break;
                }
            }
            if (live == null)
            {
                TournamentArtCatalog.Label("NoLive", card, 22f, 78f, w - 44f, 80f,
                    "当前暂无进行中的全服精选对局，开战后将在此实时显示并支持一键观战。", 16f, TournamentArtCatalog.Muted, wrap: true);
                return;
            }
            string aName = !string.IsNullOrEmpty(live.SquadAName) ? live.SquadAName : $"战队 #{live.SquadAId}";
            string bName = !string.IsNullOrEmpty(live.SquadBName) ? live.SquadBName : $"战队 #{live.SquadBId}";
            string roundDesc = live.Phase == TournamentMatchPhase.Knockout
                ? $"{TournamentFormatting.KnockoutRoundTitle(overview.Season.KnockoutTeamCount, live.RoundIndex)} · 全服 L2 公开观战"
                : "海选赛 · 全服公开观战";
            TournamentArtCatalog.Label("MatchVs", card, 22f, 68f, w - 44f, 42f,
                $"{aName}  对阵  {bName}", 20f, TournamentArtCatalog.Ink, bold: true);
            TournamentArtCatalog.Label("MatchDesc", card, 22f, 114f, w - 44f, 32f, roundDesc, 15f, TournamentArtCatalog.Muted);
            ulong bid = live.BattleId;
            TournamentArtCatalog.ArtButton("WatchLiveBtn", card, 22f, h - 64f, 178f, 44f,
                "立即围观", () => WatchBattleClicked?.Invoke(bid), primary: true, fontSize: 18f);
        }

        private void RenderOverviewActionCard(TournamentOverviewSnapshot overview, float x, float w, float h, ulong playerId, bool isBusy)
        {
            var card = TournamentArtCatalog.BorderedCard("ActionCard", _pageRoot, x, 0f, w, h,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
            var cta = TournamentUiModel.DerivePrimaryCta(overview, playerId, isBusy);

            if (!overview.HasSquad)
            {
                TournamentArtCatalog.Label("Title", card, 24f, 20f, 280f, 40f, "我的赛事状态", 28f, TournamentArtCatalog.JadeDeep, bold: true);
                TournamentArtCatalog.Tag("StateTag", card, w - 122f, 24f, 98f, 30f, "尚未入队",
                    TournamentArtCatalog.GoldTagBg, TournamentArtCatalog.Gold, TournamentArtCatalog.GoldTagText, 14f);
                TournamentArtCatalog.Label("Sub", card, 24f, 70f, w - 48f, 32f,
                    "创建属于你的固定战队，或同意来自道友的跨区邀请。", 16f, TournamentArtCatalog.Muted);

                if (overview.PendingInvites.Count > 0)
                {
                    TournamentArtCatalog.Label("InvTitle", card, 24f, 112f, w - 48f, 28f,
                        $"收到的战队邀请（{overview.PendingInvites.Count} 条）", 17f, TournamentArtCatalog.JadeDeep, bold: true);
                    int show = Math.Min(3, overview.PendingInvites.Count);
                    for (int i = 0; i < show; i++)
                    {
                        var inv = overview.PendingInvites[i];
                        float iy = 148f + i * 84f;
                        var row = TournamentArtCatalog.BorderedCard($"Inv_{i}", card, 24f, iy, w - 48f, 74f,
                            TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, 1.2f);
                        TournamentArtCatalog.Label("Name", row, 14f, 10f, w - 230f, 28f, inv.SquadName, 18f, TournamentArtCatalog.JadeDeep, bold: true);
                        string inviter = !string.IsNullOrEmpty(inv.InviterName) ? inv.InviterName : $"队长 #{inv.InviterPlayerId}";
                        TournamentArtCatalog.Label("Meta", row, 14f, 40f, w - 230f, 24f,
                            $"{inviter} · 当前 {inv.CurrentMemberCount}/{inv.RosterMaxSize} 人", 14f, TournamentArtCatalog.Muted);
                        ulong sid = inv.SquadId;
                        TournamentArtCatalog.ArtButton("Accept", row, w - 216f, 17f, 74f, 38f, "同意",
                            () => RespondInviteClicked?.Invoke(sid, true), primary: true, interactable: !isBusy, fontSize: 16f);
                        TournamentArtCatalog.ArtButton("Decline", row, w - 134f, 17f, 74f, 38f, "拒绝",
                            () => RespondInviteClicked?.Invoke(sid, false), primary: false, interactable: !isBusy, fontSize: 16f);
                    }
                }
                else
                {
                    TournamentArtCatalog.Label("NoInv", card, 24f, 160f, w - 48f, 40f,
                        "暂未收到战队邀请。", 16f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center);
                }

                RenderPrimaryCtaFooter(card, w, h, cta, overview);
                return;
            }

            var sq = overview.MySquad;
            TournamentArtCatalog.Label("Title", card, 24f, 20f, 240f, 40f, "我的战队", 28f, TournamentArtCatalog.JadeDeep, bold: true);
            TournamentArtCatalog.Tag("StateTag", card, w - 140f, 24f, 116f, 30f,
                TournamentFormatting.SquadStateDisplayName(sq.State),
                TournamentArtCatalog.GoldTagBg, TournamentArtCatalog.Gold, TournamentArtCatalog.GoldTagText, 14f);

            var summary = TournamentArtCatalog.BorderedCard("Summary", card, 24f, 72f, w - 48f, 76f,
                TournamentArtCatalog.JadeCard, TournamentArtCatalog.Jade, 1.2f);
            TournamentArtCatalog.Label("SqName", summary, 16f, 10f, w - 180f, 30f, sq.Name, 21f, TournamentArtCatalog.JadeDeep, bold: true);
            TournamentArtCatalog.Label("SqId", summary, 16f, 42f, w - 180f, 24f,
                $"战队编号 {sq.SquadId} · 全服跨区", 14f, TournamentArtCatalog.Muted);
            TournamentArtCatalog.Tag("RoleBadge", summary, w - 160f, 22f, 96f, 30f,
                overview.IsCaptain(playerId) ? "你是队长" : "你是队员",
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Jade, TournamentArtCatalog.JadeDeep, 14f);

            // 成员头像排（自适应 4 人，兼容未来更多成员截断展示）
            uint maxRoster = overview.Season.RosterMaxSize == 0 ? 4u : overview.Season.RosterMaxSize;
            int slots = (int)Math.Min(maxRoster, 5u);
            float cellW = (w - 48f) / slots;
            for (int i = 0; i < slots; i++)
            {
                float cx = 24f + i * cellW;
                var m = i < sq.Members.Count ? sq.Members[i] : null;
                var portrait = m != null ? TournamentArtCatalog.ResolvePlayerPortrait(_resolveCharacterId, m.PlayerId) : null;
                TournamentArtCatalog.AvatarMedallion($"Av_{i}", card, cx + (cellW - 72f) * 0.5f, 174f, 72f, portrait, isEmptySlot: m == null);
                string caption = m == null
                    ? "空位"
                    : $"{TournamentFormatting.MemberRoleDisplayName(m.Role)} · {DisplayMemberName(m)}";
                TournamentArtCatalog.Label($"AvCap_{i}", card, cx + 2f, 252f, cellW - 4f, 24f,
                    caption, 13f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center);
            }

            // 战绩行
            TournamentArtCatalog.Box("RecTop", card, 24f, 294f, w - 48f, 1.2f, TournamentArtCatalog.Line);
            TournamentArtCatalog.Label("RecTxt", card, 24f, 302f, w - 48f, 36f,
                TournamentFormatting.FormatRecord(sq), 21f, TournamentArtCatalog.Jade, TextAlignmentOptions.Center, bold: true);
            TournamentArtCatalog.Box("RecBot", card, 24f, 344f, w - 48f, 1.2f, TournamentArtCatalog.Line);

            string rankHint = sq.FinalRank > 0
                ? $"本季最终名次：第 {sq.FinalRank} 名 · 助威人气 {sq.CheerCount}"
                : (sq.SeedRank > 0
                    ? $"海选定档：#{sq.SeedRank} · 助威人气 {sq.CheerCount}"
                    : $"本季进度与战绩按服务端快照刷新 · 助威人气 {sq.CheerCount}");
            TournamentArtCatalog.Label("RankHint", card, 24f, 360f, w - 48f, 52f,
                rankHint, 15f, TournamentArtCatalog.Muted, wrap: true);

            RenderPrimaryCtaFooter(card, w, h, cta, overview);
        }

        private void RenderPrimaryCtaFooter(RectTransform card, float w, float h, TournamentPrimaryCta cta, TournamentOverviewSnapshot overview)
        {
            TournamentArtCatalog.ArtButton("PrimaryCtaBtn", card, 32f, h - 108f, w - 64f, 54f,
                cta.Label, () => ExecutePrimaryCta(cta, overview), primary: true, interactable: cta.Interactable, fontSize: 22f);
            TournamentArtCatalog.Label("PrimaryCtaNote", card, 24f, h - 48f, w - 48f, 32f,
                cta.Note, 14f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center);
        }

        private void ExecutePrimaryCta(TournamentPrimaryCta cta, TournamentOverviewSnapshot overview)
        {
            switch (cta.Kind)
            {
                case TournamentPrimaryCtaKind.CreateSquad:
                    _model.OpenCreateModal();
                    SyncModalVisual(overview, _lastBusy);
                    break;
                case TournamentPrimaryCtaKind.RegisterSquad:
                    _model.OpenConfirmLockModal();
                    SyncModalVisual(overview, _lastBusy);
                    break;
                case TournamentPrimaryCtaKind.StartQualifierMatch:
                case TournamentPrimaryCtaKind.StartKnockoutMatch:
                    _model.OpenLineupModal(overview.MySquad, overview.Season.BattleTeamSize, cta.MatchId, forMatchStart: true);
                    SyncModalVisual(overview, _lastBusy);
                    break;
                case TournamentPrimaryCtaKind.IntermissionSwap:
                    _model.OpenIntermissionModal(overview.MySquad, overview.Season.BattleTeamSize, overview.MyActiveMatch);
                    SyncModalVisual(overview, _lastBusy);
                    break;
                case TournamentPrimaryCtaKind.ClaimReward:
                    ClaimRewardClicked?.Invoke(0u);
                    break;
                case TournamentPrimaryCtaKind.PreviewSquad:
                    SwitchTab(TournamentTab.Squad);
                    break;
            }
        }

        // ==================== 分页 2：我的战队 ====================

        private void RenderSquadTab(TournamentOverviewSnapshot overview, ulong playerId, bool isBusy)
        {
            if (!overview.HasSquad)
            {
                var card = TournamentArtCatalog.BorderedCard("NoSquadCard", _pageRoot, 140f, 60f, InnerW - 280f, 420f,
                    TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
                TournamentArtCatalog.SpriteBox("Taiji", card, (card.sizeDelta.x - 84f) * 0.5f, 44f, 84f, 84f, "taiji-badge", preserveAspect: true);
                TournamentArtCatalog.Label("H", card, 40f, 148f, card.sizeDelta.x - 80f, 44f,
                    "还没有加入战队", 30f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
                TournamentArtCatalog.Label("P", card, 80f, 202f, card.sizeDelta.x - 160f, 64f,
                    "全服跨区自由组队。创建自己的固定战队，或在「赛事总览」接受道友发来的邀请。", 18f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center, wrap: true);
                if (overview.Season.Stage == TournamentStage.Registration)
                {
                    TournamentArtCatalog.ArtButton("CreateFromSquadTab", card, (card.sizeDelta.x - 220f) * 0.5f, 296f, 220f, 52f,
                        "创建战队", () =>
                        {
                            _model.OpenCreateModal();
                            SyncModalVisual(overview, _lastBusy);
                        }, primary: true, interactable: !isBusy, fontSize: 22f);
                }
                return;
            }

            var sq = overview.MySquad;
            var season = overview.Season;
            bool isCaptain = overview.IsCaptain(playerId);
            bool unlockedDraft = season.Stage == TournamentStage.Registration && sq.State == TournamentSquadState.Draft;
            uint rosterMax = season.RosterMaxSize == 0 ? 4u : season.RosterMaxSize;
            uint teamSize = season.BattleTeamSize == 0 ? 3u : season.BattleTeamSize;

            // 头部信息卡
            var head = TournamentArtCatalog.BorderedCard("SquadHeader", _pageRoot, 0f, 0f, InnerW, 92f,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
            TournamentArtCatalog.Label("SqTitle", head, 24f, 12f, 520f, 40f, sq.Name, 30f, TournamentArtCatalog.JadeDeep, bold: true);
            TournamentArtCatalog.Label("SqMeta", head, 24f, 54f, 460f, 26f,
                $"战队编号 {sq.SquadId}  ·  全服跨区 · {TournamentFormatting.SquadStateDisplayName(sq.State)}",
                15f, TournamentArtCatalog.Muted);
            TournamentArtCatalog.FlatButton("CopyIdBtn", head, 488f, 52f, 72f, 28f, "复制",
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                () =>
                {
                    GUIUtility.systemCopyBuffer = sq.SquadId.ToString(CultureInfo.InvariantCulture);
                    ShowToast($"已复制战队编号 {sq.SquadId}", false, Time.realtimeSinceStartup);
                }, fontSize: 14f);

            string rightStats = $"海选战绩 {sq.QualifierWins}胜 {sq.QualifierDraws}平 {sq.QualifierLosses}负   积分 {sq.QualifierPoints}"
                + (sq.FinalRank > 0 ? $"   最终第 {sq.FinalRank} 名" : (sq.SeedRank > 0 ? $"   海选第 {sq.SeedRank} 名" : string.Empty));
            TournamentArtCatalog.Label("SqStats", head, InnerW - 620f, 28f, 596f, 36f,
                rightStats, 18f, TournamentArtCatalog.Jade, TextAlignmentOptions.MidlineRight, bold: true);

            // 成员卡片区（自适应 RosterMaxSize，默认横排 4 格，超过 4 格自动等比缩宽）
            int slotCount = (int)Math.Max(rosterMax, (uint)sq.Members.Count);
            float gap = 14f;
            float cardW = (InnerW - gap * (slotCount - 1)) / slotCount;
            float cardY = 108f;
            float cardH = 266f;
            for (int i = 0; i < slotCount; i++)
            {
                float cx = i * (cardW + gap);
                if (i < sq.Members.Count)
                {
                    var m = sq.Members[i];
                    var mc = TournamentArtCatalog.BorderedCard($"Mem_{i}", _pageRoot, cx, cardY, cardW, cardH,
                        TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, 1.5f);
                    var portrait = TournamentArtCatalog.ResolveMemberPortrait(m, _resolveCharacterId);
                    TournamentArtCatalog.AvatarMedallion("Av", mc, (cardW - 88f) * 0.5f, 18f, 88f, portrait);
                    TournamentArtCatalog.Label("Name", mc, 12f, 114f, cardW - 24f, 30f,
                        DisplayMemberName(m), 20f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
                    string zoneLabel = m.ZoneId > 0 ? $"区服 S{m.ZoneId}" : "全服跨区";
                    TournamentArtCatalog.Label("Zone", mc, 12f, 146f, cardW - 24f, 24f,
                        $"{zoneLabel} · ID {m.PlayerId}", 14f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center);

                    string roleTxt = m.Role == TournamentMemberRole.Captain
                        ? (m.StarterSlot > 0 ? "队长 · 首发" : "队长 · 替补")
                        : TournamentFormatting.MemberRoleDisplayName(m.Role);
                    bool goldTag = m.Role == TournamentMemberRole.Captain;
                    TournamentArtCatalog.Tag("Role", mc, (cardW - 116f) * 0.5f, 176f, 116f, 28f, roleTxt,
                        goldTag ? TournamentArtCatalog.GoldTagBg : TournamentArtCatalog.TagGreenBg,
                        goldTag ? TournamentArtCatalog.Gold : TournamentArtCatalog.Jade,
                        goldTag ? TournamentArtCatalog.GoldTagText : TournamentArtCatalog.TagGreenText, 14f);

                    if (isCaptain && unlockedDraft && m.PlayerId != playerId)
                    {
                        ulong targetId = m.PlayerId;
                        TournamentArtCatalog.FlatButton("KickBtn", mc, (cardW - 104f) * 0.5f, 218f, 104f, 32f,
                            "移出战队", TournamentArtCatalog.TagRedBg, TournamentArtCatalog.WarnRed, TournamentArtCatalog.WarnRed,
                            () => LeaveOrKickClicked?.Invoke(targetId), interactable: !isBusy, fontSize: 15f);
                    }
                }
                else
                {
                    var emptyCard = TournamentArtCatalog.BorderedCard($"EmptySlot_{i}", _pageRoot, cx, cardY, cardW, cardH,
                        TournamentArtCatalog.CardAltBg, TournamentArtCatalog.Line, 1.5f);
                    TournamentArtCatalog.AvatarMedallion("PlusAv", emptyCard, (cardW - 84f) * 0.5f, 44f, 84f, null, isEmptySlot: true);
                    TournamentArtCatalog.Label("EmptyLbl", emptyCard, 12f, 144f, cardW - 24f, 30f,
                        i < teamSize ? "首发空位" : "替补空位", 18f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center, bold: true);
                    if (isCaptain && unlockedDraft)
                    {
                        TournamentArtCatalog.FlatButton("InviteSlotBtn", emptyCard, (cardW - 128f) * 0.5f, 192f, 128f, 38f,
                            "邀请队员", TournamentArtCatalog.Ivory, TournamentArtCatalog.Gold, TournamentArtCatalog.JadeDeep,
                            () =>
                            {
                                _model.OpenInviteModal();
                                SyncModalVisual(overview, _lastBusy);
                                InviteSourceRequested?.Invoke(_model.InviteSource);
                            }, interactable: !isBusy, fontSize: 16f);
                    }
                }
            }

            // 战队规则说明卡
            var rule = TournamentArtCatalog.BorderedCard("SquadRules", _pageRoot, 0f, 390f, InnerW, 126f,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
            TournamentArtCatalog.Label("RHead", rule, 24f, 14f, 240f, 32f, "战队规则", 22f, TournamentArtCatalog.JadeDeep, bold: true);
            TournamentArtCatalog.Label("RText", rule, 24f, 50f, InnerW - 48f, 64f,
                $"全服跨区可组队；每赛季每人仅限加入 1 支战队。锁定报名后不可更换队员，替补可在任意场次顶替上场。\n"
                + $"当前战队 {sq.MemberCount}/{rosterMax} 人 · 单场出战 {teamSize} 人 · 真实装备、技能与出战宝宝入场。",
                16f, TournamentArtCatalog.Ink, wrap: true);

            // 底部动作条
            float btnY = 534f;
            float rx = InnerW;
            if (isCaptain && unlockedDraft)
            {
                rx -= 200f;
                TournamentArtCatalog.ArtButton("LockRegBtn", _pageRoot, rx, btnY, 200f, 50f, "确认锁定报名",
                    () =>
                    {
                        _model.OpenConfirmLockModal();
                        SyncModalVisual(overview, _lastBusy);
                    }, primary: true, interactable: !isBusy && sq.MemberCount >= teamSize, fontSize: 20f);
                rx -= 166f;
                TournamentArtCatalog.ArtButton("EditLineupBtn", _pageRoot, rx, btnY, 152f, 50f, "调整首发",
                    () =>
                    {
                        _model.OpenLineupModal(sq, teamSize, 0UL, forMatchStart: false);
                        SyncModalVisual(overview, _lastBusy);
                    }, primary: false, interactable: !isBusy && sq.MemberCount >= teamSize, fontSize: 20f);
                if (sq.MemberCount < rosterMax)
                {
                    rx -= 166f;
                    TournamentArtCatalog.ArtButton("InviteBtn", _pageRoot, rx, btnY, 152f, 50f, "邀请队员",
                        () =>
                        {
                            _model.OpenInviteModal();
                            SyncModalVisual(overview, _lastBusy);
                            InviteSourceRequested?.Invoke(_model.InviteSource);
                        }, primary: false, interactable: !isBusy, fontSize: 20f);
                }
            }
            else if (isCaptain && season.Stage != TournamentStage.Settled)
            {
                rx -= 170f;
                TournamentArtCatalog.ArtButton("EditLineupLockedBtn", _pageRoot, rx, btnY, 170f, 50f, "调整默认首发",
                    () =>
                    {
                        _model.OpenLineupModal(sq, teamSize, 0UL, forMatchStart: false);
                        SyncModalVisual(overview, _lastBusy);
                    }, primary: false, interactable: !isBusy && sq.MemberCount >= teamSize, fontSize: 19f);
            }

            if (unlockedDraft)
            {
                TournamentArtCatalog.FlatButton("LeaveSquadBtn", _pageRoot, 0f, btnY + 5f, 148f, 40f,
                    sq.MemberCount <= 1 ? "解散战队" : "离开战队",
                    TournamentArtCatalog.TagRedBg, TournamentArtCatalog.WarnRed, TournamentArtCatalog.WarnRed,
                    () => LeaveOrKickClicked?.Invoke(0UL), interactable: !isBusy, fontSize: 17f);
            }
        }

        // ==================== 分页 3：全服榜与淘汰赛对阵树 ====================

        private void RenderStandingsTab(TournamentOverviewSnapshot overview, TournamentStandingsSnapshot standings, bool isBusy)
        {
            // 双子页签 + 赛季切换
            bool isRanking = _model.BoardMode == TournamentBoardMode.Ranking;
            TournamentArtCatalog.FlatButton("SubRanking", _pageRoot, 0f, 0f, 156f, 42f, "海选积分榜",
                isRanking ? TournamentArtCatalog.Jade : TournamentArtCatalog.CardAltBg,
                isRanking ? TournamentArtCatalog.Gold : TournamentArtCatalog.Line,
                isRanking ? TournamentArtCatalog.PrimaryBtnText : TournamentArtCatalog.SecondaryBtnText,
                () =>
                {
                    _model.BoardMode = TournamentBoardMode.Ranking;
                    Render(_lastOverview, _lastStandings, _lastPlayerId, _lastBusy, _lastStatus, Time.realtimeSinceStartup);
                }, fontSize: 18f);

            TournamentArtCatalog.FlatButton("SubBracket", _pageRoot, 166f, 0f, 170f, 42f, "淘汰赛对阵树",
                !isRanking ? TournamentArtCatalog.Jade : TournamentArtCatalog.CardAltBg,
                !isRanking ? TournamentArtCatalog.Gold : TournamentArtCatalog.Line,
                !isRanking ? TournamentArtCatalog.PrimaryBtnText : TournamentArtCatalog.SecondaryBtnText,
                () =>
                {
                    _model.BoardMode = TournamentBoardMode.Bracket;
                    Render(_lastOverview, _lastStandings, _lastPlayerId, _lastBusy, _lastStatus, Time.realtimeSinceStartup);
                }, fontSize: 18f);

            // 赛季切换按钮（循环切当前赛季 / 历届只读回看）
            var choices = TournamentUiModel.BuildSeasonChoices(overview);
            int curIdx = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].SeasonId == _model.SelectedStandingsSeasonId) { curIdx = i; break; }
            }
            var currentChoice = choices[curIdx];
            TournamentArtCatalog.Label("SeasonFilterLbl", _pageRoot, InnerW - 350f, 6f, 70f, 30f,
                "赛季：", 16f, TournamentArtCatalog.Muted, TextAlignmentOptions.MidlineRight);
            TournamentArtCatalog.FlatButton("SeasonCycleBtn", _pageRoot, InnerW - 274f, 2f, 274f, 38f,
                choices.Count > 1 ? $"{currentChoice.Label}  ▾" : currentChoice.Label,
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                () =>
                {
                    if (choices.Count <= 1) return;
                    int next = (curIdx + 1) % choices.Count;
                    _model.SelectedStandingsSeasonId = choices[next].SeasonId;
                    _model.StandingsPage = 1u;
                    RequestStandingsRequested?.Invoke(_model.SelectedStandingsSeasonId, _model.StandingsPage);
                }, interactable: !isBusy, fontSize: 16f);

            if (standings == null)
            {
                RenderEmptyState("正在拉取全服榜与淘汰赛对阵…", "如未自动加载，请点击下方按钮重试。");
                TournamentArtCatalog.ArtButton("RetryStandingsBtn", _pageRoot, (InnerW - 180f) * 0.5f, 390f, 180f, 46f,
                    "加载全服榜", () => RequestStandingsRequested?.Invoke(_model.SelectedStandingsSeasonId, _model.StandingsPage),
                    primary: true, interactable: !isBusy, fontSize: 18f);
                return;
            }

            if (isRanking) RenderRankingList(overview, standings, isBusy, currentChoice.IsCurrent);
            else RenderKnockoutBracket(overview, standings);
        }

        private void RenderRankingList(TournamentOverviewSnapshot overview, TournamentStandingsSnapshot standings, bool isBusy, bool isCurrentSeason)
        {
            TournamentArtCatalog.Label("BoardHeading", _pageRoot, 0f, 54f, 460f, 36f,
                "全服海选积分榜", 24f, TournamentArtCatalog.JadeDeep, bold: true);
            uint maxQual = standings.Season?.QualifierMaxMatches > 0 ? standings.Season.QualifierMaxMatches : 6u;
            TournamentArtCatalog.Label("BoardRuleHint", _pageRoot, InnerW - 520f, 58f, 520f, 28f,
                $"共 {standings.TotalSquads} 支参赛战队 · 每队最多 {maxQual} 场 · 胜 3 分 / 平 1 分",
                15f, TournamentArtCatalog.Muted, TextAlignmentOptions.MidlineRight);

            var box = TournamentArtCatalog.BorderedCard("StandingsBox", _pageRoot, 0f, 96f, InnerW, 396f,
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, 1.5f);

            // 表头
            var th = TournamentArtCatalog.Box("TableHead", box, 2f, 2f, InnerW - 4f, 42f, TournamentArtCatalog.HeaderRowBg).rectTransform;
            RenderRankingRowColumns(th, "名次", "战队名称", "人数", "胜/平/负", "积分", "助威人气", "操作", isHeader: true);

            // 滚动体
            TournamentArtCatalog.CreateScrollView("StandingsScroll", box, 2f, 46f, InnerW - 4f, 346f,
                horizontal: false, vertical: true, out var content);

            bool canCheerSeason = isCurrentSeason
                && overview.Season.Stage != TournamentStage.Settled
                && !overview.HasCheered;
            uint cut = standings.Season?.KnockoutTeamCount ?? 0u;
            uint baseRank = (standings.Page - 1u) * standings.PageSize;
            float y = 0f;

            if (standings.Standings.Count == 0)
            {
                TournamentArtCatalog.Label("NoSquads", content, 0f, 60f, InnerW - 4f, 40f,
                    "当前暂无已锁定报名的战队。", 18f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center);
                y = 140f;
            }
            else
            {
                for (int i = 0; i < standings.Standings.Count; i++)
                {
                    var sq = standings.Standings[i];
                    uint rank = sq.FinalRank > 0 ? sq.FinalRank : (baseRank + (uint)i + 1u);
                    var rowBg = TournamentArtCatalog.Box($"Row_{i}", content, 0f, y, InnerW - 4f, 50f,
                        (i % 2 == 0) ? TournamentArtCatalog.CardBg : TournamentArtCatalog.Ivory).rectTransform;
                    TournamentArtCatalog.Box("Sep", rowBg, 0f, 49f, InnerW - 4f, 1f, TournamentArtCatalog.Line);

                    bool isMine = overview.HasSquad && overview.MySquad.SquadId == sq.SquadId;
                    string sqLabel = isMine ? $"{sq.Name}（我的战队）" : sq.Name;
                    RenderRankingRowColumns(rowBg,
                        rank.ToString(CultureInfo.InvariantCulture),
                        sqLabel,
                        $"{sq.MemberCount} 人",
                        $"{sq.QualifierWins}/{sq.QualifierDraws}/{sq.QualifierLosses}",
                        sq.QualifierPoints.ToString(CultureInfo.InvariantCulture),
                        sq.CheerCount.ToString(CultureInfo.InvariantCulture),
                        string.Empty,
                        isHeader: false);

                    bool cheeredThis = isCurrentSeason && overview.MyCheeredSquadId == sq.SquadId;
                    if (cheeredThis)
                    {
                        TournamentArtCatalog.Tag("Cheered", rowBg, InnerW - 144f, 10f, 114f, 30f, "已助威",
                            TournamentArtCatalog.GoldTagBg, TournamentArtCatalog.Gold, TournamentArtCatalog.GoldTagText, 14f);
                    }
                    else if (canCheerSeason)
                    {
                        ulong sid = sq.SquadId;
                        TournamentArtCatalog.ArtButton("CheerBtn", rowBg, InnerW - 148f, 8f, 120f, 34f, "免费助威",
                            () => CheerSquadClicked?.Invoke(sid), primary: false, interactable: !isBusy, fontSize: 15f);
                    }
                    else
                    {
                        TournamentArtCatalog.Label("ReadOnly", rowBg, InnerW - 144f, 10f, 114f, 30f,
                            isCurrentSeason ? (overview.HasCheered ? "已用本季票" : "已封榜") : "历史只读",
                            14f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center);
                    }
                    y += 50f;

                    // 动态晋级线：精确画在 KnockoutTeamCount 名之后
                    if (cut >= 2 && rank == cut)
                    {
                        var cutBar = TournamentArtCatalog.Box("CutLine", content, 0f, y, InnerW - 4f, 32f, TournamentArtCatalog.CutLineBg).rectTransform;
                        TournamentArtCatalog.Label("CutTxt", cutBar, 0f, 0f, InnerW - 4f, 32f,
                            $"────────── 以上前 {cut} 名晋级全服淘汰赛 ──────────",
                            14f, TournamentArtCatalog.GoldTagText, TextAlignmentOptions.Center, bold: true);
                        y += 32f;
                    }
                }
            }
            content.sizeDelta = new Vector2(InnerW - 4f, Mathf.Max(346f, y + 6f));

            // 底部钉住「我的战队」一行 + 分页控制
            if (standings.MySquad != null)
            {
                var mine = TournamentArtCatalog.BorderedCard("PinnedMine", _pageRoot, 0f, 502f, InnerW - 248f, 48f,
                    TournamentArtCatalog.JadeCard, TournamentArtCatalog.Jade, 1.5f);
                var ms = standings.MySquad;
                string rankStr = ms.FinalRank > 0 ? $"第 {ms.FinalRank} 名" : (ms.SeedRank > 0 ? $"#{ms.SeedRank}" : "我的战队");
                TournamentArtCatalog.Label("MineTxt", mine, 18f, 8f, mine.sizeDelta.x - 36f, 32f,
                    $"{rankStr}  ·  {ms.Name}  ·  {ms.QualifierWins}胜 {ms.QualifierDraws}平 {ms.QualifierLosses}负  ·  {ms.QualifierPoints} 分  ·  人气 {ms.CheerCount}",
                    17f, TournamentArtCatalog.JadeDeep, bold: true);
            }

            uint totalPages = Math.Max(1u, (standings.TotalSquads + standings.PageSize - 1u) / standings.PageSize);
            TournamentArtCatalog.FlatButton("PrevPage", _pageRoot, InnerW - 232f, 506f, 76f, 38f, "上一页",
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                () => RequestStandingsRequested?.Invoke(_model.SelectedStandingsSeasonId, Math.Max(1u, standings.Page - 1u)),
                interactable: !isBusy && standings.Page > 1u, fontSize: 15f);
            TournamentArtCatalog.Label("PageNum", _pageRoot, InnerW - 152f, 506f, 72f, 38f,
                $"{standings.Page}/{totalPages}", 16f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center, bold: true);
            TournamentArtCatalog.FlatButton("NextPage", _pageRoot, InnerW - 76f, 506f, 76f, 38f, "下一页",
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                () => RequestStandingsRequested?.Invoke(_model.SelectedStandingsSeasonId, Math.Min(totalPages, standings.Page + 1u)),
                interactable: !isBusy && standings.Page < totalPages, fontSize: 15f);
        }

        private static void RenderRankingRowColumns(RectTransform row, string cRank, string cName, string cMem,
            string cWdl, string cPts, string cCheer, string cOp, bool isHeader)
        {
            Color col = isHeader ? TournamentArtCatalog.JadeDeep : TournamentArtCatalog.Ink;
            float fs = isHeader ? 15f : 16f;
            TournamentArtCatalog.Label("ColRank", row, 16f, 0f, 76f, row.sizeDelta.y, cRank,
                isHeader ? 15f : 20f, isHeader ? col : TournamentArtCatalog.Gold, TextAlignmentOptions.MidlineLeft, bold: true);
            TournamentArtCatalog.Label("ColName", row, 100f, 0f, 440f, row.sizeDelta.y, cName, fs,
                isHeader ? col : TournamentArtCatalog.JadeDeep, bold: true);
            TournamentArtCatalog.Label("ColMem", row, 550f, 0f, 140f, row.sizeDelta.y, cMem, fs,
                isHeader ? col : TournamentArtCatalog.Muted);
            TournamentArtCatalog.Label("ColWdl", row, 710f, 0f, 170f, row.sizeDelta.y, cWdl, fs, col);
            TournamentArtCatalog.Label("ColPts", row, 900f, 0f, 120f, row.sizeDelta.y, cPts, fs,
                isHeader ? col : TournamentArtCatalog.Jade, bold: true);
            TournamentArtCatalog.Label("ColCheer", row, 1050f, 0f, 140f, row.sizeDelta.y, cCheer, fs, col);
            if (!string.IsNullOrEmpty(cOp))
                TournamentArtCatalog.Label("ColOp", row, InnerW - 148f, 0f, 120f, row.sizeDelta.y, cOp, fs, col, TextAlignmentOptions.Center, bold: isHeader);
        }

        private void RenderKnockoutBracket(TournamentOverviewSnapshot overview, TournamentStandingsSnapshot standings)
        {
            uint teamCount = standings.Season?.KnockoutTeamCount ?? 0u;
            var columns = standings.GroupKnockoutByRound();

            TournamentArtCatalog.Label("BracketTitle", _pageRoot, 0f, 54f, 600f, 36f,
                teamCount >= 2 ? $"淘汰赛对阵树 · {teamCount} 强" : "淘汰赛对阵树",
                24f, TournamentArtCatalog.JadeDeep, bold: true);
            TournamentArtCatalog.Label("BracketHint", _pageRoot, InnerW - 460f, 58f, 460f, 28f,
                "支持拖拽/滚轮浏览完整赛程 · 激战中一键围观", 15f, TournamentArtCatalog.Muted, TextAlignmentOptions.MidlineRight);

            if (teamCount < 2 || columns.Count == 0)
            {
                var empty = TournamentArtCatalog.BorderedCard("EmptyBracket", _pageRoot, 80f, 112f, InnerW - 160f, 400f,
                    TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
                TournamentArtCatalog.SpriteBox("Taiji", empty, (empty.sizeDelta.x - 84f) * 0.5f, 58f, 84f, 84f, "taiji-badge", preserveAspect: true);
                string heading = standings.Season?.Stage == TournamentStage.Registration || standings.Season?.Stage == TournamentStage.Qualifier
                    ? "淘汰赛尚未开启"
                    : "本季未开启淘汰赛";
                string desc = standings.Season?.Stage == TournamentStage.Registration || standings.Season?.Stage == TournamentStage.Qualifier
                    ? "海选积分赛结束后将按实际有效报名队数自动生成 2 / 4 / 8 / 16 强单败淘汰签表。"
                    : "本赛季有效报名战队不足 2 支，未开启淘汰赛；已完赛战队将直接按保底档发放赛季奖励。";
                TournamentArtCatalog.Label("H", empty, 40f, 162f, empty.sizeDelta.x - 80f, 42f, heading, 28f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
                TournamentArtCatalog.Label("P", empty, 80f, 214f, empty.sizeDelta.x - 160f, 86f, desc, 18f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center, wrap: true);
                return;
            }

            TournamentArtCatalog.CreateScrollView("BracketScroll", _pageRoot, 0f, 98f, InnerW, 488f,
                horizontal: true, vertical: true, out var content);

            float colW = 310f;
            float colGap = 44f;
            float cardH = 114f;
            int firstColMatches = columns[0].Count;
            float contentH = Mathf.Max(480f, 56f + firstColMatches * (cardH + 18f));
            float totalW = Mathf.Max(InnerW, columns.Count * colW + (columns.Count - 1) * colGap + 40f);
            float startX = columns.Count <= 2 ? Mathf.Max(20f, (InnerW - (columns.Count * colW + (columns.Count - 1) * colGap)) * 0.5f) : 12f;

            for (int c = 0; c < columns.Count; c++)
            {
                var roundMatches = columns[c];
                float cx = startX + c * (colW + colGap);
                uint roundIdx = roundMatches.Count > 0 ? roundMatches[0].RoundIndex : (uint)(c + 1);
                string roundTitle = TournamentFormatting.KnockoutRoundTitle(teamCount, roundIdx);
                TournamentArtCatalog.Label($"RoundHead_{c}", content, cx, 6f, colW, 34f,
                    roundTitle, 20f, TournamentArtCatalog.GoldTagText, TextAlignmentOptions.Center, bold: true);

                float usableH = contentH - 52f;
                float slotStep = usableH / Math.Max(1, roundMatches.Count);
                bool isFinalCol = c == columns.Count - 1;

                for (int mIdx = 0; mIdx < roundMatches.Count; mIdx++)
                {
                    var m = roundMatches[mIdx];
                    float cy = 48f + mIdx * slotStep + (slotStep - cardH) * 0.5f;
                    var mCard = TournamentArtCatalog.BorderedCard($"Match_{c}_{mIdx}", content, cx, cy, colW, cardH,
                        isFinalCol ? TournamentArtCatalog.GoldTagBg : TournamentArtCatalog.Ivory,
                        isFinalCol ? TournamentArtCatalog.Gold : TournamentArtCatalog.Line,
                        isFinalCol ? 2f : 1.5f);

                    if (!isFinalCol)
                        TournamentArtCatalog.Box($"Link_{c}_{mIdx}", content, cx + colW, cy + cardH * 0.5f, colGap * 0.6f, 1.5f, TournamentArtCatalog.Gold);

                    string metaLeft = $"{roundTitle} · 第 {m.SlotIndex + 1} 场";
                    string metaRight = TournamentFormatting.MatchStateDisplayName(m.State);
                    Color metaColor = m.State == TournamentMatchState.InBattle
                        ? TournamentArtCatalog.WarnRed
                        : (m.State == TournamentMatchState.Intermission ? TournamentArtCatalog.Jade : TournamentArtCatalog.Muted);
                    TournamentArtCatalog.Label("MetaL", mCard, 12f, 6f, 176f, 20f, metaLeft, 13f, TournamentArtCatalog.Muted);
                    TournamentArtCatalog.Label("MetaR", mCard, colW - 116f, 6f, 104f, 20f,
                        m.State == TournamentMatchState.InBattle ? "● 激战中" : metaRight,
                        13f, metaColor, TextAlignmentOptions.MidlineRight,
                        bold: m.State == TournamentMatchState.InBattle || m.State == TournamentMatchState.Intermission);

                    RenderBracketTeamLine(mCard, 28f, colW, m.SquadAId, m.SquadAName, m.SquadASeed, m.ReadyA,
                        m.WinnerSquadId, m.State, isByeSlot: m.SquadAId == 0UL && m.State == TournamentMatchState.Bye);
                    RenderBracketTeamLine(mCard, 54f, colW, m.SquadBId, m.SquadBName, m.SquadBSeed, m.ReadyB,
                        m.WinnerSquadId, m.State, isByeSlot: m.SquadBId == 0UL && m.State == TournamentMatchState.Bye);

                    // V2 模块 A：BO3 局分胶囊（score-bo3.png 156×40 等比缩至 102×26）+ 五行灵脉圆章点击看说明
                    float bottomLeftX = 10f;
                    if (m.BestOf >= 3u)
                    {
                        var boPill = TournamentArtCatalog.SpriteBox("Bo3Pill", mCard, bottomLeftX, 80f, 102f, 26f,
                            "v2-a-score-bo3", preserveAspect: true);
                        TournamentArtCatalog.Label("Bo3Score", boPill.transform, 4f, 0f, 94f, 26f,
                            TournamentFormatting.FormatBo3Score(m), 13f, TournamentArtCatalog.PrimaryBtnText,
                            TextAlignmentOptions.Center, bold: true);
                        bottomLeftX += 108f;
                    }
                    if (m.CurrentLeylineElement != TournamentLeylineElement.TournamentLeylineUnspecified)
                    {
                        var leyInfo = TournamentFormatting.GetLeylineInfo(m.CurrentLeylineElement);
                        var leyBtn = QdaoUguiFactory.CreateArtButton("LeylineBadge", mCard, bottomLeftX, 80f, 26f, 26f,
                            TournamentArtCatalog.Load(leyInfo.SpriteKey), out var lImg);
                        lImg.preserveAspect = true;
                        string tipMsg = $"{leyInfo.FullName}：{leyInfo.EffectSummary}";
                        leyBtn.onClick.AddListener(() => ShowToast(tipMsg, isError: false, Time.realtimeSinceStartup));
                        bottomLeftX += 30f;
                    }

                    // 节点动作按钮：激战中 -> 围观；本队局间换阵 -> 换阵弹层；淘汰赛公开战报 / 本队已结束局 -> 战报回放
                    if (m.State == TournamentMatchState.InBattle && m.BattleId != 0UL)
                    {
                        ulong bid = m.BattleId;
                        TournamentArtCatalog.ArtButton("WatchBtn", mCard, colW - 92f, 80f, 80f, 26f, "围观",
                            () => WatchBattleClicked?.Invoke(bid), primary: true, fontSize: 14f);
                    }
                    else if (m.State == TournamentMatchState.Intermission
                        && overview.HasSquad && (m.SquadAId == overview.MySquad.SquadId || m.SquadBId == overview.MySquad.SquadId))
                    {
                        var capturedMatch = m;
                        TournamentArtCatalog.ArtButton("IntermissionBtn", mCard, colW - 96f, 80f, 84f, 26f, "局间换阵",
                            () =>
                            {
                                _model.OpenIntermissionModal(overview.MySquad, overview.Season.BattleTeamSize, capturedMatch);
                                SyncModalVisual(_lastOverview, _lastBusy);
                            }, primary: true, fontSize: 13f);
                    }
                    else if (TournamentUiModel.CanWatchReplay(m, overview.MySquad))
                    {
                        if (m.GameBattleIds.Count > 1)
                        {
                            // BO3 多局支持按 G1/G2/G3 任选回放
                            int count = Math.Min(3, m.GameBattleIds.Count);
                            float bx = colW - 10f - count * 42f;
                            for (int g = 0; g < count; g++)
                            {
                                ulong gBid = m.GameBattleIds[g];
                                if (gBid == 0UL) continue;
                                TournamentArtCatalog.FlatButton($"ReplayG{g + 1}", mCard, bx + g * 42f, 80f, 38f, 26f,
                                    $"G{g + 1}", TournamentArtCatalog.Ivory, TournamentArtCatalog.Jade, TournamentArtCatalog.JadeDeep,
                                    () => WatchReplayClicked?.Invoke(gBid), fontSize: 12f);
                            }
                        }
                        else
                        {
                            ulong bid = TournamentUiModel.ResolveLatestReplayBattleId(m);
                            TournamentArtCatalog.FlatButton("ReplayBtn", mCard, colW - 88f, 80f, 76f, 26f, "回放",
                                TournamentArtCatalog.Ivory, TournamentArtCatalog.Jade, TournamentArtCatalog.JadeDeep,
                                () => WatchReplayClicked?.Invoke(bid), fontSize: 13f);
                        }
                    }
                    else if (m.State == TournamentMatchState.Bye)
                    {
                        TournamentArtCatalog.Label("ByeNote", mCard, bottomLeftX, 82f, colW - bottomLeftX - 12f, 22f,
                            "轮空晋级", 13f, TournamentArtCatalog.GoldTagText, TextAlignmentOptions.MidlineRight);
                    }
                    else if (m.State == TournamentMatchState.WaitingOpponent || m.State == TournamentMatchState.Ready)
                    {
                        TournamentArtCatalog.Label("WaitNote", mCard, bottomLeftX, 82f, colW - bottomLeftX - 12f, 22f,
                            "双方就绪后开战", 13f, TournamentArtCatalog.Muted, TextAlignmentOptions.MidlineRight);
                    }
                }
            }
            content.sizeDelta = new Vector2(totalW, contentH);
        }

        private static void RenderBracketTeamLine(RectTransform card, float y, float cardW, ulong squadId, string name,
            uint seed, bool ready, ulong winnerId, TournamentMatchState state, bool isByeSlot)
        {
            string seedTxt = seed > 0 ? $"#{seed} " : string.Empty;
            string display = squadId == 0UL
                ? (isByeSlot ? "（轮空）" : "待定")
                : (!string.IsNullOrEmpty(name) ? name : $"战队 #{squadId}");
            bool isWinner = winnerId != 0UL && winnerId == squadId;
            bool isLoser = winnerId != 0UL && squadId != 0UL && winnerId != squadId;
            Color txtCol = isWinner ? TournamentArtCatalog.Jade : (isLoser ? TournamentArtCatalog.Muted : TournamentArtCatalog.Ink);

            TournamentArtCatalog.Label("Team", card, 12f, y, cardW - 64f, 24f, seedTxt + display, 15f, txtCol, bold: isWinner);
            string suffix = isWinner ? "✓" : ((state == TournamentMatchState.WaitingOpponent || state == TournamentMatchState.Ready) && ready ? "就绪" : string.Empty);
            if (!string.IsNullOrEmpty(suffix))
                TournamentArtCatalog.Label("Flag", card, cardW - 56f, y, 44f, 24f, suffix, 14f, TournamentArtCatalog.Jade, TextAlignmentOptions.MidlineRight, bold: true);
        }

        // ==================== 分页 4：赛季奖励与免费助威 ====================

        private void RenderRewardsTab(TournamentOverviewSnapshot overview, bool isBusy)
        {
            float leftW = 600f;
            float rightX = leftW + 20f;
            float rightW = InnerW - rightX;

            // 左上：我的本季奖励账单
            var myCard = TournamentArtCatalog.BorderedCard("MyRewardCard", _pageRoot, 0f, 0f, leftW, 430f,
                TournamentArtCatalog.JadeCard, TournamentArtCatalog.Jade, 1.5f);
            TournamentArtCatalog.Label("MyRewHead", myCard, 24f, 18f, leftW - 48f, 34f, "❖ 我的本季奖励", 24f, TournamentArtCatalog.JadeDeep, bold: true);

            if (overview.MyRewards.Count == 0)
            {
                TournamentArtCatalog.Label("NoRew", myCard, 24f, 96f, leftW - 48f, 110f,
                    overview.Season.Stage == TournamentStage.Settled
                        ? "本季暂无可领取奖励（完赛至少 2 场或免费助威命中四强/冠军即可领奖）。"
                        : "赛季封榜结算后，将按战队最终名次、参赛保底及免费助威结果生成绑定奖励账单。",
                    17f, TournamentArtCatalog.Ink, wrap: true);
            }
            else
            {
                TournamentArtCatalog.CreateScrollView("MyRewScroll", myCard, 18f, 60f, leftW - 36f, 286f,
                    horizontal: false, vertical: true, out var myContent);
                float ry = 0f;
                for (int i = 0; i < overview.MyRewards.Count; i++)
                {
                    var r = overview.MyRewards[i];
                    var rc = TournamentArtCatalog.BorderedCard($"MyRew_{i}", myContent, 0f, ry, leftW - 40f, 124f,
                        TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, 1.2f);
                    string tierName = TournamentFormatting.RewardTierDisplayName(r.TierKind);
                    string stateTxt = TournamentFormatting.RewardDeliveryDisplayName(r.DeliveryState);
                    TournamentArtCatalog.Label("Tier", rc, 14f, 10f, 320f, 30f, $"S{r.SeasonId} · {tierName}", 20f, TournamentArtCatalog.JadeDeep, bold: true);
                    TournamentArtCatalog.Tag("State", rc, rc.sizeDelta.x - 118f, 10f, 104f, 28f, stateTxt,
                        r.DeliveryState == TournamentRewardDeliveryState.TournamentRewardDeliveryClaimable
                            ? TournamentArtCatalog.GoldTagBg : TournamentArtCatalog.TagGreenBg,
                        TournamentArtCatalog.Gold,
                        r.DeliveryState == TournamentRewardDeliveryState.TournamentRewardDeliveryClaimable
                            ? TournamentArtCatalog.GoldTagText : TournamentArtCatalog.TagGreenText, 13f);

                    string itemsSummary = FormatRewardItems(r.Items, r.GoldAmount);
                    TournamentArtCatalog.Label("Items", rc, 14f, 46f, rc.sizeDelta.x - 28f, 40f,
                        "【绑定】" + itemsSummary, 15f, TournamentArtCatalog.Ink, wrap: true);
                    string honor = FormatHonorLine(r.TitleCode, r.BadgeCode);
                    if (!string.IsNullOrEmpty(honor))
                        TournamentArtCatalog.Label("Honor", rc, 14f, 90f, rc.sizeDelta.x - 28f, 24f, honor, 14f, TournamentArtCatalog.GoldTagText);
                    ry += 134f;
                }
                myContent.sizeDelta = new Vector2(leftW - 40f, Mathf.Max(286f, ry));
            }

            // 底部领奖 / 前往邮箱按钮
            if (overview.HasClaimableReward)
            {
                TournamentArtCatalog.Label("ClaimHint", myCard, 24f, 366f, 310f, 36f,
                    "可领取 · 全部为绑定自用道具与银两", 15f, TournamentArtCatalog.Muted);
                TournamentArtCatalog.ArtButton("ClaimBtn", myCard, leftW - 216f, 358f, 192f, 48f, "领取赛季奖励",
                    () => ClaimRewardClicked?.Invoke(0u), primary: true, interactable: !isBusy, fontSize: 19f);
            }
            else
            {
                TournamentArtCatalog.Label("MailHint", myCard, 24f, 366f, 350f, 36f,
                    "已领奖励自动发往仙笺邮箱", 15f, TournamentArtCatalog.Muted);
                TournamentArtCatalog.FlatButton("OpenMailBtn", myCard, leftW - 154f, 362f, 130f, 40f, "前往邮箱",
                    TournamentArtCatalog.Ivory, TournamentArtCatalog.Jade, TournamentArtCatalog.JadeDeep,
                    () => OpenMailClicked?.Invoke(), fontSize: 16f);
            }

            // 右上：全档位奖励总览（真源为 Overview.RewardTiers，零硬编码数值）
            var tierCard = TournamentArtCatalog.BorderedCard("TierTableCard", _pageRoot, rightX, 0f, rightW, 430f,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
            TournamentArtCatalog.Label("TierHead", tierCard, 24f, 18f, rightW - 48f, 34f,
                "全档位奖励总览 · 全部绑定自用 + 荣耀排面", 22f, TournamentArtCatalog.JadeDeep, bold: true);

            TournamentArtCatalog.CreateScrollView("TierScroll", tierCard, 18f, 60f, rightW - 36f, 354f,
                horizontal: false, vertical: true, out var tierContent);
            float ty = 0f;
            if (overview.RewardTiers.Count > 0)
            {
                for (int i = 0; i < overview.RewardTiers.Count; i++)
                {
                    var t = overview.RewardTiers[i];
                    RenderRewardTierRow(tierContent, ty, rightW - 40f,
                        TournamentFormatting.RewardTierDisplayName(t.TierKind),
                        FormatRewardItems(t.Items, t.GoldAmount),
                        FormatHonorLine(t.TitleCode, t.BadgeCode));
                    ty += 52f;
                }
            }
            else
            {
                // 兜底直读客户端同步的 TournamentRewardTableManager（绝不手写样例数字）
                var rows = TournamentRewardTableManager.Instance.FindAll();
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var items = new List<TournamentRewardItem>();
                    for (int k = 0; k < row.ItemIds.Count && k < row.ItemCounts.Count; k++)
                        items.Add(new TournamentRewardItem { ItemId = row.ItemIds[k], Count = row.ItemCounts[k] });
                    RenderRewardTierRow(tierContent, ty, rightW - 40f,
                        TournamentFormatting.RewardTierDisplayName((TournamentRewardTierKind)row.TierKind),
                        FormatRewardItems(items, row.GoldAmount),
                        FormatHonorLine(row.TitleCode, row.BadgeCode));
                    ty += 52f;
                }
            }
            tierContent.sizeDelta = new Vector2(rightW - 40f, Mathf.Max(354f, ty));

            // 底部：免费助威专区（零消耗明示）
            var cheer = TournamentArtCatalog.BorderedCard("CheerStrip", _pageRoot, 0f, 448f, InnerW, 132f,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
            var stamp = TournamentArtCatalog.BorderedCard("CheerStamp", cheer, 24f, 32f, 66f, 66f,
                TournamentArtCatalog.JadeSoft, TournamentArtCatalog.GoldSoft, 2f);
            TournamentArtCatalog.Label("Glyph", stamp, 0f, 0f, 66f, 66f, "助", 32f, TournamentArtCatalog.Jade, TextAlignmentOptions.Center, bold: true);
            TournamentArtCatalog.Label("CHead", cheer, 108f, 24f, 760f, 34f,
                "免费助威 · 每季 1 票 · 零消耗", 22f, TournamentArtCatalog.JadeDeep, bold: true);
            string cheerDesc = overview.HasCheered
                ? $"本季已免费助威战队：{(!string.IsNullOrEmpty(overview.MyCheeredSquadName) ? overview.MyCheeredSquadName : "#" + overview.MyCheeredSquadId)}（每季 1 票，投后不可更改）。"
                : "免费为看好的晋级战队打 Call，零消耗；所选战队进入四强或夺冠即可领取观赛纪念礼。";
            TournamentArtCatalog.Label("CDesc", cheer, 108f, 64f, InnerW - 360f, 48f, cheerDesc, 16f, TournamentArtCatalog.Muted, wrap: true);
            TournamentArtCatalog.ArtButton("GoCheerBtn", cheer, InnerW - 210f, 40f, 184f, 48f,
                overview.HasCheered ? "查看榜单" : "去选队助威",
                () =>
                {
                    _model.BoardMode = TournamentBoardMode.Ranking;
                    _model.SelectedStandingsSeasonId = 0u;
                    SwitchTab(TournamentTab.Standings);
                }, primary: !overview.HasCheered, fontSize: 19f);
        }

        private static void RenderRewardTierRow(RectTransform parent, float y, float w, string tierName, string items, string honor)
        {
            TournamentArtCatalog.Label("TierName", parent, 6f, y + 8f, 128f, 34f, tierName, 17f, TournamentArtCatalog.GoldTagText, bold: true);
            TournamentArtCatalog.Label("TierItems", parent, 140f, y + 8f, w - 340f, 34f, items, 15f, TournamentArtCatalog.JadeDeep);
            TournamentArtCatalog.Label("TierHonor", parent, w - 194f, y + 8f, 188f, 34f,
                string.IsNullOrEmpty(honor) ? "绑定自用" : honor, 14f, TournamentArtCatalog.Muted, TextAlignmentOptions.MidlineRight);
            TournamentArtCatalog.Box("Sep", parent, 0f, y + 50f, w, 1f, TournamentArtCatalog.Line);
        }

        // ==================== 分页 5：冠军殿堂（V2 玉石基座 + 牌匾 + 观道致敬） ====================

        private void RenderHallTab(TournamentOverviewSnapshot overview)
        {
            var latest = overview.FindLatestChampionSeason();
            if (latest == null)
            {
                RenderEmptyState("冠军殿堂 · 虚位以待",
                    "首届全服总冠军席位虚位以待，快召集同伴报名留名青史！");
                return;
            }

            float leftW = 780f;
            float rightX = leftW + 22f;
            float rightW = InnerW - rightX;

            var spot = TournamentArtCatalog.BorderedCard("LatestChampion", _pageRoot, 0f, 0f, leftW, 574f,
                TournamentArtCatalog.Paper, TournamentArtCatalog.Gold, 2f);
            string sTitle = !string.IsNullOrEmpty(latest.SeasonName) ? latest.SeasonName : $"第 {latest.SeasonId} 赛季 · 群仙问道赛";
            string champName = !string.IsNullOrEmpty(latest.ChampionSquadName) ? latest.ChampionSquadName : $"战队 #{latest.ChampionSquadId}";
            TournamentArtCatalog.Label("Kicker", spot, 28f, 18f, 520f, 26f, sTitle, 16f, TournamentArtCatalog.GoldTagText, bold: true);

            // V2 模块 B：牌匾（420×96 等比）+ 冠军队名独立楷体字
            var plaque = TournamentArtCatalog.SpriteBox("ChampPlaque", spot, (leftW - 420f) * 0.5f, 46f, 420f, 96f,
                "v2-b-champion-plaque", preserveAspect: true);
            TournamentArtCatalog.Label("PlaqueChampName", plaque.transform, 36f, 16f, 348f, 64f,
                champName, 30f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);

            // V2 模块 B：右侧 80×80 可点击观道致敬气泡
            uint targetSeasonId = latest.SeasonId;
            var bubbleBtn = QdaoUguiFactory.CreateArtButton("HallSaluteBubble", spot, leftW - 108f, 24f, 80f, 80f,
                TournamentArtCatalog.Load("v2-b-salute-bubble"), out var bImg);
            bImg.preserveAspect = true;
            bubbleBtn.onClick.AddListener(() =>
            {
                _model.OpenSaluteModal(targetSeasonId);
                SyncModalVisual(_lastOverview, _lastBusy);
            });

            // V2 模块 B：玉石基座（480×320 等比居中）+ 真实冠军成员头像（无数据时中性太极占位，严禁借演示图冒充）
            TournamentArtCatalog.SpriteBox("ChampPedestal", spot, (leftW - 420f) * 0.5f, 178f, 420f, 280f,
                "v2-b-champion-pedestal", preserveAspect: true);

            if (latest.ChampionMembers.Count > 0)
            {
                int mCount = Math.Min(4, latest.ChampionMembers.Count);
                float avSize = 86f;
                float avGap = 24f;
                float rowW = mCount * avSize + (mCount - 1) * avGap;
                float startAvX = (leftW - rowW) * 0.5f;
                for (int i = 0; i < mCount; i++)
                {
                    var cm = latest.ChampionMembers[i];
                    float ax = startAvX + i * (avSize + avGap);
                    var portrait = TournamentArtCatalog.ResolveMemberPortrait(cm, _resolveCharacterId);
                    TournamentArtCatalog.AvatarMedallion($"ChampAv_{i}", spot, ax, 152f, avSize, portrait);
                    TournamentArtCatalog.Label($"ChampMemName_{i}", spot, ax - 14f, 244f, avSize + 28f, 24f,
                        DisplayMemberName(cm), 14f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
                }
            }
            else
            {
                TournamentArtCatalog.Label("MembersFallback", spot, 60f, 180f, leftW - 120f, 70f,
                    !string.IsNullOrEmpty(latest.ChampionMemberSummary) ? latest.ChampionMemberSummary : "冠军战队成员名册已永久封存。",
                    18f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, wrap: true);
            }

            // 底部：全服累计观道次数 + 每日免费观道致敬按钮
            string dateText = latest.EndMs > 0 ? $"封榜日期：{FormatUtc8Date(latest.EndMs)}" : $"第 {latest.SeasonId} 赛季总冠军";
            TournamentArtCatalog.Label("SaluteSummary", spot, 28f, 470f, 460f, 28f,
                $"{dateText}  ·  全服已观道致敬 {latest.ChampionSaluteCount} 次",
                16f, TournamentArtCatalog.GoldTagText, bold: true);
            TournamentArtCatalog.Label("SaluteSub", spot, 28f, 502f, 460f, 26f,
                latest.SalutedToday ? "今日已完成观道致敬，明日 0 点后可再次免费致敬。" : "每日可免费向总冠军观道致敬一次，立得绑定薄礼。",
                14f, TournamentArtCatalog.Muted);

            TournamentArtCatalog.V2PrimaryButton("OpenSaluteModalBtn", spot, leftW - 220f, 480f, 190f, 46f,
                latest.SalutedToday ? "今日已观道" : "观道致敬",
                () =>
                {
                    _model.OpenSaluteModal(targetSeasonId);
                    SyncModalVisual(_lastOverview, _lastBusy);
                }, interactable: !_lastBusy, fontSize: 19f);

            // 右侧历届列表
            var histBox = TournamentArtCatalog.BorderedCard("HistoryListBox", _pageRoot, rightX, 0f, rightW, 574f,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
            TournamentArtCatalog.Label("HHead", histBox, 24f, 20f, rightW - 48f, 34f, "往届荣誉 · 每一季都值得铭记", 22f, TournamentArtCatalog.JadeDeep, bold: true);
            TournamentArtCatalog.CreateScrollView("HallScroll", histBox, 18f, 66f, rightW - 36f, 490f,
                horizontal: false, vertical: true, out var content);

            float hy = 0f;
            for (int i = 0; i < overview.HallOfFame.Count; i++)
            {
                var s = overview.HallOfFame[i];
                var item = TournamentArtCatalog.BorderedCard($"Hof_{i}", content, 0f, hy, rightW - 40f, 108f,
                    TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, 1.2f);
                TournamentArtCatalog.Label("Title", item, 18f, 12f, item.sizeDelta.x - 160f, 32f,
                    $"第 {s.SeasonId} 赛季 · {s.ChampionSquadName}", 20f, TournamentArtCatalog.JadeDeep, bold: true);
                TournamentArtCatalog.Label("Members", item, 18f, 50f, item.sizeDelta.x - 36f, 44f,
                    !string.IsNullOrEmpty(s.ChampionMemberSummary) ? s.ChampionMemberSummary : "全服总冠军",
                    15f, TournamentArtCatalog.Muted, wrap: true);
                uint sid = s.SeasonId;
                TournamentArtCatalog.FlatButton("ViewPast", item, item.sizeDelta.x - 130f, 12f, 114f, 32f, "回看榜单",
                    TournamentArtCatalog.CardAltBg, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                    () =>
                    {
                        _model.SelectedStandingsSeasonId = sid;
                        _model.StandingsPage = 1u;
                        _model.ActiveTab = TournamentTab.Standings;
                        RequestStandingsRequested?.Invoke(sid, 1u);
                    }, fontSize: 14f);
                hy += 118f;
            }
            content.sizeDelta = new Vector2(rightW - 40f, Mathf.Max(490f, hy));
        }

        // ==================== 4 个轻量弹窗 ====================

        private void SyncModalVisual(TournamentOverviewSnapshot overview, bool isBusy)
        {
            if (_model.ActiveModal == TournamentModalKind.None)
            {
                ForceCloseModalVisual();
                return;
            }

            int lineupSig = ComputeLineupSig();
            int inviteSig = ComputeInviteSig();
            var saluteTarget = overview?.FindChampionSeason(_model.SaluteTargetSeasonId);
            bool salutedNow = saluteTarget?.SalutedToday ?? false;
            ulong saluteCountNow = saluteTarget?.ChampionSaluteCount ?? 0UL;
            bool needRebuild = _renderedModal != _model.ActiveModal
                || !string.Equals(_renderedModalError, _model.ModalInlineError, StringComparison.Ordinal)
                || ((_model.ActiveModal == TournamentModalKind.ConfirmLineup || _model.ActiveModal == TournamentModalKind.IntermissionSwap) && lineupSig != _renderedLineupSig)
                || (_model.ActiveModal == TournamentModalKind.IntermissionSwap && _renderedInspectLeyline != _model.SelectedLeylineInspect)
                || (_model.ActiveModal == TournamentModalKind.SaluteChampion && (_renderedSalutedToday != salutedNow || _renderedSaluteCount != saluteCountNow));

            if (!needRebuild && _modalBackdrop.gameObject.activeSelf)
            {
                if (_model.ActiveModal == TournamentModalKind.InviteMember && inviteSig != _renderedInviteSig)
                {
                    _renderedInviteSig = inviteSig;
                    RenderInviteCandidatesSection(isBusy);
                }
                return;
            }

            _renderedModal = _model.ActiveModal;
            _renderedModalError = _model.ModalInlineError ?? string.Empty;
            _renderedLineupSig = lineupSig;
            _renderedInviteSig = inviteSig;
            _renderedInspectLeyline = _model.SelectedLeylineInspect;
            _renderedSalutedToday = salutedNow;
            _renderedSaluteCount = saluteCountNow;
            _intermissionCountdownLabel = null;
            _createInput = null;
            _createCounter = null;
            _inviteInput = null;
            _inviteCandidatesHost = null;

            TournamentArtCatalog.ClearChildren(_modalContent);
            _modalBackdrop.gameObject.SetActive(true);

            switch (_model.ActiveModal)
            {
                case TournamentModalKind.CreateSquad:
                    BuildCreateSquadModal(isBusy);
                    break;
                case TournamentModalKind.InviteMember:
                    BuildInviteMemberModal(isBusy);
                    break;
                case TournamentModalKind.ConfirmLineup:
                    BuildConfirmLineupModal(overview, isBusy);
                    break;
                case TournamentModalKind.ConfirmLock:
                    BuildConfirmLockModal(overview, isBusy);
                    break;
                case TournamentModalKind.IntermissionSwap:
                    BuildIntermissionModal(overview, isBusy);
                    break;
                case TournamentModalKind.SaluteChampion:
                    BuildSaluteModal(overview, isBusy);
                    break;
            }
        }

        private void ForceCloseModalVisual()
        {
            _renderedModal = TournamentModalKind.None;
            _renderedModalError = string.Empty;
            _renderedLineupSig = -1;
            _renderedInviteSig = -1;
            _intermissionCountdownLabel = null;
            _createInput = null;
            _createCounter = null;
            _inviteInput = null;
            _inviteCandidatesHost = null;
            if (_modalContent != null) TournamentArtCatalog.ClearChildren(_modalContent);
            if (_modalBackdrop != null) _modalBackdrop.gameObject.SetActive(false);
        }

        private RectTransform BuildModalFrame(string title, float w, float h)
        {
            _modalContent.sizeDelta = new Vector2(w, h);
            TournamentArtCatalog.SpriteBox("ModalFrame", _modalContent, 0f, 0f, w, h, "window-frame", sliced: true, raycast: true);
            TournamentArtCatalog.Label("ModalTitle", _modalContent, 48f, 34f, w - 96f, 44f,
                title, 30f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
            var close = QdaoUguiFactory.CreateArtButton("ModalClose", _modalContent, w - 76f, 30f, 42f, 42f,
                TournamentArtCatalog.Load("close-button"), out var ci);
            ci.preserveAspect = true;
            close.onClick.AddListener(() =>
            {
                _model.CloseModal();
                ForceCloseModalVisual();
            });
            return _modalContent;
        }

        private void BuildCreateSquadModal(bool isBusy)
        {
            var host = BuildModalFrame("创建战队", 560f, 340f);
            TournamentArtCatalog.Label("Lbl", host, 52f, 94f, 456f, 28f, "战队名称（2–12 字）", 17f, TournamentArtCatalog.SecondaryBtnText, bold: true);

            var fieldBorder = TournamentArtCatalog.BorderedCard("InputBorder", host, 52f, 128f, 456f, 48f,
                TournamentArtCatalog.Ivory,
                string.IsNullOrEmpty(_model.ModalInlineError) ? TournamentArtCatalog.Gold : TournamentArtCatalog.WarnRed, 2f);
            _createInput = QdaoUguiFactory.CreateInputField("SquadNameInput", fieldBorder, 4f, 4f, 448f, 40f,
                "输入 2–12 字战队名称", TournamentUiModel.MaxSquadNameChars);
            _createInput.text = _model.CreateNameDraft;
            _createCounter = TournamentArtCatalog.Label("Counter", host, 396f, 182f, 112f, 22f,
                $"{TournamentUiModel.CountUnicodeCodePoints(_model.CreateNameDraft)}/12 字", 14f, TournamentArtCatalog.Muted, TextAlignmentOptions.MidlineRight);
            _createInput.onValueChanged.AddListener(val =>
            {
                _model.CreateNameDraft = val ?? string.Empty;
                if (_createCounter != null)
                    _createCounter.text = $"{TournamentUiModel.CountUnicodeCodePoints(_model.CreateNameDraft)}/12 字";
            });

            string help = string.IsNullOrEmpty(_model.ModalInlineError)
                ? "队名由服务端内容安全审核；同一赛季不可重名。"
                : _model.ModalInlineError;
            TournamentArtCatalog.Label("Help", host, 52f, 184f, 344f, 46f, help, 14f,
                string.IsNullOrEmpty(_model.ModalInlineError) ? TournamentArtCatalog.Muted : TournamentArtCatalog.WarnRed, wrap: true);

            TournamentArtCatalog.ArtButton("CancelBtn", host, 134f, 250f, 136f, 48f, "取消", () =>
            {
                _model.CloseModal();
                ForceCloseModalVisual();
            }, primary: false, fontSize: 19f);
            TournamentArtCatalog.ArtButton("ConfirmBtn", host, 290f, 250f, 148f, 48f, "创建战队", () =>
            {
                string name = _createInput != null ? _createInput.text : _model.CreateNameDraft;
                _model.CreateNameDraft = name ?? string.Empty;
                if (!TournamentUiModel.ValidateSquadNameLength(_model.CreateNameDraft, out _))
                {
                    _model.ModalInlineError = "请输入 2 至 12 字有效战队名称。";
                    SyncModalVisual(_lastOverview, _lastBusy);
                    return;
                }
                CreateSquadSubmitted?.Invoke(_model.CreateNameDraft.Trim());
            }, primary: true, interactable: !isBusy, fontSize: 19f);
        }

        private void BuildInviteMemberModal(bool isBusy)
        {
            const float modalW = 660f;
            const float modalH = 584f;
            var host = BuildModalFrame("邀请队员", modalW, modalH);

            TournamentArtCatalog.Label("Copy", host, 48f, 84f, modalW - 96f, 24f,
                "支持从好友 / 同帮 / 附近道友一键邀请，或输入精确角色 ID 跨区邀请。", 15f, TournamentArtCatalog.Muted);

            // 顶部：精确角色 ID 输入行（保留全服跨区直邀能力）
            var fieldBorder = TournamentArtCatalog.BorderedCard("InputBorder", host, 48f, 114f, modalW - 232f, 44f,
                TournamentArtCatalog.Ivory,
                string.IsNullOrEmpty(_model.ModalInlineError) ? TournamentArtCatalog.Gold : TournamentArtCatalog.WarnRed, 2f);
            _inviteInput = QdaoUguiFactory.CreateInputField("InviteIdInput", fieldBorder, 4f, 4f, modalW - 240f, 36f,
                "输入目标道友角色 ID（支持跨区）", 20);
            _inviteInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            _inviteInput.text = _model.InvitePlayerIdDraft;
            _inviteInput.onValueChanged.AddListener(v => _model.InvitePlayerIdDraft = v ?? string.Empty);

            TournamentArtCatalog.ArtButton("SendInviteBtn", host, modalW - 174f, 112f, 126f, 48f, "发起邀请", () =>
            {
                string raw = _inviteInput != null ? _inviteInput.text : _model.InvitePlayerIdDraft;
                _model.InvitePlayerIdDraft = raw ?? string.Empty;
                if (!TournamentUiModel.TryParsePlayerId(_model.InvitePlayerIdDraft, out ulong pid))
                {
                    _model.ModalInlineError = "请输入有效的正整数角色 ID。";
                    SyncModalVisual(_lastOverview, _lastBusy);
                    return;
                }
                InviteMemberSubmitted?.Invoke(pid);
            }, primary: true, interactable: !isBusy, fontSize: 18f);

            if (!string.IsNullOrEmpty(_model.ModalInlineError))
            {
                TournamentArtCatalog.Label("Err", host, 48f, 162f, modalW - 96f, 22f,
                    _model.ModalInlineError, 14f, TournamentArtCatalog.WarnRed);
            }

            // 中部：好友 / 同帮 / 附近快捷名单容器（异步刷新只更新此区域，不打断上方输入）
            _inviteCandidatesHost = QdaoUguiFactory.CreateRect("InviteQuickHost", host, 48f, 188f, modalW - 96f, 326f);
            RenderInviteCandidatesSection(isBusy);

            TournamentArtCatalog.ArtButton("CancelBtn", host, (modalW - 144f) * 0.5f, modalH - 64f, 144f, 44f, "关闭", () =>
            {
                _model.CloseModal();
                ForceCloseModalVisual();
            }, primary: false, fontSize: 18f);
        }

        private void RenderInviteCandidatesSection(bool isBusy)
        {
            if (_inviteCandidatesHost == null) return;
            TournamentArtCatalog.ClearChildren(_inviteCandidatesHost);
            float w = _inviteCandidatesHost.sizeDelta.x;

            // 来源切换栏 + 刷新
            (TournamentInviteQuickSource src, string label)[] tabs =
            {
                (TournamentInviteQuickSource.Friends, "好友"),
                (TournamentInviteQuickSource.Guild, "同帮"),
                (TournamentInviteQuickSource.Nearby, "附近"),
            };
            float tx = 0f;
            for (int i = 0; i < tabs.Length; i++)
            {
                var (src, label) = tabs[i];
                bool active = _model.InviteSource == src;
                TournamentArtCatalog.FlatButton($"SrcTab_{i}", _inviteCandidatesHost, tx, 0f, 102f, 34f, label,
                    active ? TournamentArtCatalog.Jade : TournamentArtCatalog.Ivory,
                    active ? TournamentArtCatalog.GoldSoft : TournamentArtCatalog.Line,
                    active ? TournamentArtCatalog.PrimaryBtnText : TournamentArtCatalog.JadeDeep,
                    () =>
                    {
                        _model.InviteSource = src;
                        _renderedInviteSig = -1;
                        RenderInviteCandidatesSection(_lastBusy);
                        InviteSourceRequested?.Invoke(src);
                    }, fontSize: 15f);
                tx += 110f;
            }
            TournamentArtCatalog.FlatButton("SrcRefresh", _inviteCandidatesHost, w - 82f, 0f, 82f, 34f, "刷新",
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                () => InviteSourceRequested?.Invoke(_model.InviteSource), fontSize: 14f);

            string note = !string.IsNullOrEmpty(_lastInviteSourceStatus)
                ? _lastInviteSourceStatus
                : "邀请发出后将在对方「赛事总览」与「我的战队」页签提示。";
            TournamentArtCatalog.Label("SrcStatus", _inviteCandidatesHost, 0f, 38f, w, 22f, note, 13f, TournamentArtCatalog.Muted);

            var listCard = TournamentArtCatalog.BorderedCard("CandBox", _inviteCandidatesHost, 0f, 64f, w, 258f,
                TournamentArtCatalog.CardBg, TournamentArtCatalog.Line, 1.5f);
            TournamentArtCatalog.CreateScrollView("CandScroll", listCard, 2f, 2f, w - 4f, 254f,
                horizontal: false, vertical: true, out var content);

            float y = 4f;
            if (_lastInviteCandidates == null || _lastInviteCandidates.Count == 0)
            {
                TournamentArtCatalog.Label("Empty", content, 16f, 90f, w - 36f, 44f,
                    "当前名单暂无可邀请道友，可切换页签或直接在上方输入角色 ID。",
                    15f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center, wrap: true);
                y = 200f;
            }
            else
            {
                for (int i = 0; i < _lastInviteCandidates.Count; i++)
                {
                    var c = _lastInviteCandidates[i];
                    var row = TournamentArtCatalog.Box($"Cand_{i}", content, 6f, y, w - 16f, 52f,
                        (i % 2 == 0) ? TournamentArtCatalog.Ivory : TournamentArtCatalog.CardAltBg).rectTransform;
                    TournamentArtCatalog.Box("Sep", row, 0f, 51f, w - 16f, 1f, TournamentArtCatalog.Line);

                    var portrait = TournamentArtCatalog.ResolveCharacterPortrait(c.CharacterId, _resolveCharacterId, c.PlayerId);
                    TournamentArtCatalog.AvatarMedallion("Av", row, 10f, 6f, 40f, portrait);
                    string disp = !string.IsNullOrEmpty(c.Name) ? c.Name : $"道友 #{c.PlayerId}";
                    TournamentArtCatalog.Label("Name", row, 60f, 4f, 250f, 24f, disp, 16f, TournamentArtCatalog.JadeDeep, bold: true);
                    string stateText = c.OnlineStatusKnown ? (c.IsOnline ? "在线" : "离线") : c.SourceTag;
                    Color stateColor = c.OnlineStatusKnown && c.IsOnline ? TournamentArtCatalog.Jade : TournamentArtCatalog.Muted;
                    TournamentArtCatalog.Label("Sub", row, 60f, 28f, 280f, 20f,
                        $"ID {c.PlayerId} · {c.SourceTag} · {stateText}", 13f, stateColor);

                    ulong pid = c.PlayerId;
                    TournamentArtCatalog.ArtButton("QuickInv", row, row.sizeDelta.x - 104f, 9f, 92f, 34f, "邀请",
                        () => InviteMemberSubmitted?.Invoke(pid),
                        primary: false, interactable: !isBusy, fontSize: 15f);
                    y += 54f;
                }
            }
            content.sizeDelta = new Vector2(w - 4f, Mathf.Max(254f, y + 4f));
        }

        private void BuildConfirmLineupModal(TournamentOverviewSnapshot overview, bool isBusy)
        {
            uint teamSize = overview?.Season?.BattleTeamSize > 0 ? overview.Season.BattleTeamSize : 3u;
            var members = overview?.MySquad?.Members;
            int count = members?.Count ?? 0;
            float modalH = Mathf.Clamp(290f + count * 62f, 420f, 620f);
            var host = BuildModalFrame(_model.LineupModalIsForMatchStart ? "确认出战阵容" : "调整默认首发", 580f, modalH);

            string desc = _model.LineupModalIsForMatchStart
                ? $"请选择恰好 {teamSize} 名出战成员。确认后将全员拉入跨服战斗房间，请确保已整备好本人真实装备、技能与出战宝宝。"
                : $"请选择恰好 {teamSize} 名默认首发成员，其余自动列为替补。";
            TournamentArtCatalog.Label("Desc", host, 48f, 86f, 484f, 48f, desc, 15f, TournamentArtCatalog.Muted, wrap: true);

            float y = 142f;
            if (members != null)
            {
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    bool selected = _model.IsMemberSelectedInDraft(m.PlayerId);
                    ulong pid = m.PlayerId;
                    var rowBtn = TournamentArtCatalog.FlatButton($"Pick_{i}", host, 48f, y, 484f, 52f, string.Empty,
                        selected ? TournamentArtCatalog.JadeCard : TournamentArtCatalog.Ivory,
                        selected ? TournamentArtCatalog.Jade : TournamentArtCatalog.Line,
                        TournamentArtCatalog.Ink,
                        () =>
                        {
                            _model.ToggleLineupMember(pid, teamSize);
                            SyncModalVisual(_lastOverview, _lastBusy);
                        }, interactable: !isBusy, fontSize: 16f);

                    var rowRect = (RectTransform)rowBtn.transform;
                    TournamentArtCatalog.Tag("Chk", rowRect, 12f, 11f, 30f, 30f, selected ? "✓" : string.Empty,
                        selected ? TournamentArtCatalog.Jade : TournamentArtCatalog.CardBg,
                        selected ? TournamentArtCatalog.JadeDeep : TournamentArtCatalog.Line,
                        TournamentArtCatalog.PrimaryBtnText, 18f);
                    var portrait = TournamentArtCatalog.ResolvePlayerPortrait(_resolveCharacterId, m.PlayerId);
                    TournamentArtCatalog.AvatarMedallion("Av", rowRect, 52f, 6f, 40f, portrait);
                    TournamentArtCatalog.Label("Name", rowRect, 102f, 10f, 220f, 32f, DisplayMemberName(m), 17f, TournamentArtCatalog.JadeDeep, bold: true);
                    TournamentArtCatalog.Label("Role", rowRect, 326f, 10f, 146f, 32f,
                        TournamentFormatting.MemberRoleDisplayName(m.Role), 15f, TournamentArtCatalog.Muted, TextAlignmentOptions.MidlineRight);
                    y += 60f;
                }
            }

            string statusTxt = !string.IsNullOrEmpty(_model.ModalInlineError)
                ? _model.ModalInlineError
                : $"已选择 {_model.LineupDraft.Count} / {teamSize} 名首发";
            TournamentArtCatalog.Label("CountLbl", host, 48f, modalH - 118f, 484f, 28f, statusTxt, 15f,
                !string.IsNullOrEmpty(_model.ModalInlineError) ? TournamentArtCatalog.WarnRed : TournamentArtCatalog.JadeDeep, bold: true);

            TournamentArtCatalog.ArtButton("CancelBtn", host, 126f, modalH - 76f, 140f, 48f, "取消", () =>
            {
                _model.CloseModal();
                ForceCloseModalVisual();
            }, primary: false, fontSize: 19f);

            bool complete = _model.IsLineupDraftComplete(teamSize);
            TournamentArtCatalog.ArtButton("SubmitLineupBtn", host, 286f, modalH - 76f, 188f, 48f,
                _model.LineupModalIsForMatchStart ? "确认阵容并开战" : "保存首发阵容",
                () =>
                {
                    if (!_model.IsLineupDraftComplete(teamSize))
                    {
                        _model.ModalInlineError = $"请勾选恰好 {teamSize} 名本队成员。";
                        SyncModalVisual(_lastOverview, _lastBusy);
                        return;
                    }
                    var copy = new List<ulong>(_model.LineupDraft);
                    if (_model.LineupModalIsForMatchStart)
                        StartMatchSubmitted?.Invoke(_model.PendingMatchId, copy);
                    else
                        UpdateLineupSubmitted?.Invoke(copy);
                }, primary: true, interactable: !isBusy && complete, fontSize: 18f);
        }

        private void BuildConfirmLockModal(TournamentOverviewSnapshot overview, bool isBusy)
        {
            var host = BuildModalFrame("确认锁定报名", 560f, 300f);
            uint teamSize = overview?.Season?.BattleTeamSize > 0 ? overview.Season.BattleTeamSize : 3u;
            uint memCount = overview?.MySquad?.MemberCount ?? 0u;
            TournamentArtCatalog.Label("Warn", host, 52f, 96f, 456f, 88f,
                $"锁定后本赛季不可更换队员，确认代表本战队报名《群仙问道赛》？\n当前名册 {memCount} 人（单场首发 {teamSize} 人），替补可在赛前任意切换上场。",
                17f, TournamentArtCatalog.Ink, TextAlignmentOptions.Center, wrap: true);

            TournamentArtCatalog.ArtButton("BackBtn", host, 118f, 206f, 150f, 48f, "返回调整", () =>
            {
                _model.CloseModal();
                ForceCloseModalVisual();
            }, primary: false, fontSize: 19f);
            TournamentArtCatalog.ArtButton("LockBtn", host, 288f, 206f, 168f, 48f, "确认锁定报名",
                () => RegisterSquadConfirmed?.Invoke(), primary: true, interactable: !isBusy, fontSize: 19f);
        }

        /// <summary>V2 模块 A：BO3 局间换阵与五行灵脉弹层（760×540 原图面板 + a-intermission-title 原生毛笔字稿，严禁叠字）。</summary>
        private void BuildIntermissionModal(TournamentOverviewSnapshot overview, bool isBusy)
        {
            const float w = 760f;
            const float h = 540f;
            _modalContent.sizeDelta = new Vector2(w, h);
            TournamentArtCatalog.SpriteBox("IntermissionBg", _modalContent, 0f, 0f, w, h,
                "v2-a-intermission-panel", sliced: false, preserveAspect: false, raycast: true);

            // 原生毛笔标题图（340×40，严禁重复叠字）
            var titleImg = TournamentArtCatalog.SpriteBox("IntermissionTitleArt", _modalContent, (w - 340f) * 0.5f, 24f, 340f, 40f,
                "v2-title-intermission", preserveAspect: true);
            if (titleImg.sprite == null)
                TournamentArtCatalog.Label("FallbackTitle", _modalContent, 60f, 24f, w - 120f, 40f, "局间灵脉换阵", 28f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);

            var close = QdaoUguiFactory.CreateArtButton("ModalClose", _modalContent, w - 66f, 22f, 40f, 40f,
                TournamentArtCatalog.Load("close-button"), out var ci);
            ci.preserveAspect = true;
            close.onClick.AddListener(() =>
            {
                _model.CloseModal();
                ForceCloseModalVisual();
            });

            var match = overview?.MyActiveMatch;
            var squad = overview?.MySquad;
            uint teamSize = overview?.Season?.BattleTeamSize > 0 ? overview.Season.BattleTeamSize : 3u;
            bool isCaptain = overview != null && overview.IsCaptain(_lastPlayerId);
            bool myReady = match != null && squad != null
                && ((match.SquadAId == squad.SquadId && match.ReadyA) || (match.SquadBId == squad.SquadId && match.ReadyB));

            // 局分与校准倒计时行
            string boScore = match != null ? TournamentFormatting.FormatBo3Score(match) : "BO3";
            uint gameIdx = match != null && match.CurrentGameIndex > 0 ? match.CurrentGameIndex : 2u;
            string aName = !string.IsNullOrEmpty(match?.SquadAName) ? match.SquadAName : "甲队";
            string bName = !string.IsNullOrEmpty(match?.SquadBName) ? match.SquadBName : "乙队";
            TournamentArtCatalog.Label("SeriesInfo", _modalContent, 48f, 74f, 370f, 30f,
                $"{aName} vs {bName} · {boScore} · 第 {gameIdx} 局", 16f, TournamentArtCatalog.JadeDeep, bold: true);

            ulong remMs = match != null
                ? _model.ComputeIntermissionRemainingMs(overview, match, Time.realtimeSinceStartup)
                : 0UL;
            _intermissionCountdownLabel = TournamentArtCatalog.Label("IntermissionCd", _modalContent, w - 340f, 74f, 292f, 30f,
                remMs > 0UL ? $"局间换阵倒计时  {TournamentFormatting.FormatCountdown(remMs)}" : "倒计时已归零 · 等待服务端自动开战…",
                16f, TournamentArtCatalog.WarnRed, TextAlignmentOptions.MidlineRight, bold: true);

            // 五行灵脉圆章排（128×128 等比缩至 56×56；点击任意圆章切文字说明，不依赖 Hover）
            var activeLeyline = match != null ? match.CurrentLeylineElement : TournamentLeylineElement.TournamentLeylineUnspecified;
            var inspectLeyline = _model.SelectedLeylineInspect != TournamentLeylineElement.TournamentLeylineUnspecified
                ? _model.SelectedLeylineInspect
                : (activeLeyline != TournamentLeylineElement.TournamentLeylineUnspecified
                    ? activeLeyline
                    : TournamentLeylineElement.TournamentLeylineMetal);

            float medalStartX = 76f;
            float medalStep = 128f;
            for (int i = 0; i < TournamentFormatting.AllLeylines.Length; i++)
            {
                var info = TournamentFormatting.AllLeylines[i];
                float mx = medalStartX + i * medalStep;
                bool isCurrent = info.Element == activeLeyline;
                bool isInspected = info.Element == inspectLeyline;

                if (isCurrent || isInspected)
                {
                    TournamentArtCatalog.Box($"MedalRing_{i}", _modalContent, mx - 3f, 111f, 62f, 62f,
                        isCurrent ? TournamentArtCatalog.Gold : TournamentArtCatalog.JadeSoft, false);
                }
                var elem = info.Element;
                var mBtn = QdaoUguiFactory.CreateArtButton($"Leyline_{i}", _modalContent, mx, 114f, 56f, 56f,
                    TournamentArtCatalog.Load(info.SpriteKey), out var mImg);
                mImg.preserveAspect = true;
                mBtn.onClick.AddListener(() =>
                {
                    _model.SelectedLeylineInspect = elem;
                    SyncModalVisual(_lastOverview, _lastBusy);
                });
                TournamentArtCatalog.Label($"LeyLbl_{i}", _modalContent, mx - 18f, 172f, 92f, 22f,
                    isCurrent ? $"★{info.ShortName}" : info.ShortName, 13f,
                    isCurrent ? TournamentArtCatalog.GoldTagText : TournamentArtCatalog.Muted,
                    TextAlignmentOptions.Center, bold: isCurrent);
            }

            var inspectInfo = TournamentFormatting.GetLeylineInfo(inspectLeyline);
            string leyTag = inspectInfo.Element == activeLeyline ? "【本局生效】" : "【灵脉预览】";
            var leyCard = TournamentArtCatalog.BorderedCard("LeylineDescCard", _modalContent, 56f, 198f, w - 112f, 44f,
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Gold, 1.2f);
            TournamentArtCatalog.Label("LeyDesc", leyCard, 14f, 4f, w - 140f, 36f,
                $"{leyTag}{inspectInfo.FullName}：{inspectInfo.EffectSummary}",
                15f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);

            // 首发 / 替补换阵区
            float y = 252f;
            var members = squad?.Members;
            if (members != null)
            {
                int show = Math.Min(4, members.Count);
                float rowW = (w - 124f) * 0.5f;
                for (int i = 0; i < show; i++)
                {
                    var m = members[i];
                    int col = i % 2;
                    int row = i / 2;
                    float rx = 56f + col * (rowW + 12f);
                    float ry = y + row * 64f;
                    bool selected = _model.IsMemberSelectedInDraft(m.PlayerId);
                    ulong pid = m.PlayerId;
                    var rowBtn = TournamentArtCatalog.FlatButton($"InterPick_{i}", _modalContent, rx, ry, rowW, 54f, string.Empty,
                        selected ? TournamentArtCatalog.JadeCard : TournamentArtCatalog.Ivory,
                        selected ? TournamentArtCatalog.Jade : TournamentArtCatalog.Line,
                        TournamentArtCatalog.Ink,
                        () =>
                        {
                            if (!isCaptain || myReady || isBusy) return;
                            _model.ToggleLineupMember(pid, teamSize);
                            SyncModalVisual(_lastOverview, _lastBusy);
                        }, interactable: isCaptain && !myReady && !isBusy, fontSize: 15f);

                    var rRect = (RectTransform)rowBtn.transform;
                    TournamentArtCatalog.Tag("Chk", rRect, 10f, 12f, 28f, 28f, selected ? "✓" : string.Empty,
                        selected ? TournamentArtCatalog.Jade : TournamentArtCatalog.CardBg,
                        selected ? TournamentArtCatalog.JadeDeep : TournamentArtCatalog.Line,
                        TournamentArtCatalog.PrimaryBtnText, 16f);
                    var portrait = TournamentArtCatalog.ResolveMemberPortrait(m, _resolveCharacterId);
                    TournamentArtCatalog.AvatarMedallion("Av", rRect, 46f, 7f, 40f, portrait);
                    TournamentArtCatalog.Label("Name", rRect, 94f, 6f, rowW - 104f, 24f, DisplayMemberName(m), 15f, TournamentArtCatalog.JadeDeep, bold: true);
                    TournamentArtCatalog.Label("Role", rRect, 94f, 30f, rowW - 104f, 20f,
                        selected ? "本局首发" : "本局替补", 12f, selected ? TournamentArtCatalog.Jade : TournamentArtCatalog.Muted);
                }
            }

            string errOrNote = !string.IsNullOrEmpty(_model.ModalInlineError)
                ? _model.ModalInlineError
                : (myReady
                    ? "本队已锁定本局阵容，关闭弹窗不影响服务端倒计时。"
                    : (isCaptain
                        ? $"已选 {_model.LineupDraft.Count}/{teamSize} 名出战；超时将沿用上局合法阵容自动开战。"
                        : "仅队长可提交本局换阵，关闭弹窗不影响服务端倒计时。"));
            TournamentArtCatalog.Label("InterNote", _modalContent, 56f, 392f, w - 112f, 36f, errOrNote, 14f,
                !string.IsNullOrEmpty(_model.ModalInlineError) ? TournamentArtCatalog.WarnRed : TournamentArtCatalog.Muted,
                TextAlignmentOptions.Center, wrap: true);

            TournamentArtCatalog.ArtButton("InterCloseBtn", _modalContent, 182f, h - 78f, 160f, 44f, "稍后再看", () =>
            {
                _model.CloseModal();
                ForceCloseModalVisual();
            }, primary: false, fontSize: 18f);

            bool canSubmit = isCaptain && !myReady && !isBusy && _model.IsLineupDraftComplete(teamSize) && match != null;
            TournamentArtCatalog.V2PrimaryButton("InterSubmitBtn", _modalContent, 366f, h - 78f, 212f, 44f,
                myReady ? "已确认备战" : "确认阵容并开战",
                () =>
                {
                    if (!canSubmit) return;
                    var copy = new List<ulong>(_model.LineupDraft);
                    StartMatchSubmitted?.Invoke(match.MatchId, copy);
                }, interactable: canSubmit, fontSize: 18f);
        }

        /// <summary>V2 模块 B：观道致敬轻弹窗（640×440 原图面板 + b-salute-title 原生「观道致敬」字稿，严禁叠字）。</summary>
        private void BuildSaluteModal(TournamentOverviewSnapshot overview, bool isBusy)
        {
            const float w = 640f;
            const float h = 440f;
            _modalContent.sizeDelta = new Vector2(w, h);
            TournamentArtCatalog.SpriteBox("SaluteBg", _modalContent, 0f, 0f, w, h,
                "v2-b-salute-panel", sliced: false, preserveAspect: false, raycast: true);

            var titleImg = TournamentArtCatalog.SpriteBox("SaluteTitleArt", _modalContent, (w - 260f) * 0.5f, 20f, 260f, 56f,
                "v2-title-salute", preserveAspect: true);
            if (titleImg.sprite == null)
                TournamentArtCatalog.Label("FallbackTitle", _modalContent, 60f, 28f, w - 120f, 40f, "观道致敬", 28f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);

            var close = QdaoUguiFactory.CreateArtButton("ModalClose", _modalContent, w - 64f, 22f, 40f, 40f,
                TournamentArtCatalog.Load("close-button"), out var ci);
            ci.preserveAspect = true;
            close.onClick.AddListener(() =>
            {
                _model.CloseModal();
                ForceCloseModalVisual();
            });

            var champ = overview?.FindChampionSeason(_model.SaluteTargetSeasonId);
            if (champ == null || champ.ChampionSquadId == 0UL)
            {
                TournamentArtCatalog.Label("NoChamp", _modalContent, 56f, 160f, w - 112f, 80f,
                    "当前暂无已诞生的全服总冠军，待赛季封榜后即可每日免费观道致敬。",
                    18f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center, wrap: true);
                TournamentArtCatalog.ArtButton("OkBtn", _modalContent, (w - 156f) * 0.5f, h - 82f, 156f, 44f, "知道了", () =>
                {
                    _model.CloseModal();
                    ForceCloseModalVisual();
                }, primary: false, fontSize: 18f);
                return;
            }

            string sqName = !string.IsNullOrEmpty(champ.ChampionSquadName) ? champ.ChampionSquadName : $"战队 #{champ.ChampionSquadId}";
            TournamentArtCatalog.Label("ChampLine", _modalContent, 48f, 84f, w - 96f, 34f,
                $"第 {champ.SeasonId} 赛季总冠军 · {sqName}", 22f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);

            // 真实冠军成员头像排（数据缺失时中性太极占位）
            if (champ.ChampionMembers.Count > 0)
            {
                int mCount = Math.Min(4, champ.ChampionMembers.Count);
                float avSize = 68f;
                float avGap = 20f;
                float rowW = mCount * avSize + (mCount - 1) * avGap;
                float sx = (w - rowW) * 0.5f;
                for (int i = 0; i < mCount; i++)
                {
                    var cm = champ.ChampionMembers[i];
                    var portrait = TournamentArtCatalog.ResolveMemberPortrait(cm, _resolveCharacterId);
                    TournamentArtCatalog.AvatarMedallion($"SalAv_{i}", _modalContent, sx + i * (avSize + avGap), 126f, avSize, portrait);
                    TournamentArtCatalog.Label($"SalNm_{i}", _modalContent, sx + i * (avSize + avGap) - 10f, 198f, avSize + 20f, 22f,
                        DisplayMemberName(cm), 13f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center);
                }
            }
            else
            {
                TournamentArtCatalog.Label("ChampMemSummary", _modalContent, 56f, 140f, w - 112f, 64f,
                    !string.IsNullOrEmpty(champ.ChampionMemberSummary) ? champ.ChampionMemberSummary : "全服总冠军战队",
                    16f, TournamentArtCatalog.Ink, TextAlignmentOptions.Center, wrap: true);
            }

            // 每日免费绑定薄礼预览（现查配表，零硬编码漂移）
            string rewardPreview = BuildSaluteRewardPreview();
            var rewardBox = TournamentArtCatalog.BorderedCard("SaluteRewardBox", _modalContent, 64f, 232f, w - 128f, 96f,
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, 1.2f);
            TournamentArtCatalog.Label("SalCount", rewardBox, 16f, 10f, w - 160f, 26f,
                $"全服已累计观道致敬：{champ.ChampionSaluteCount} 次", 16f, TournamentArtCatalog.GoldTagText, TextAlignmentOptions.Center, bold: true);
            TournamentArtCatalog.Label("SalReward", rewardBox, 16f, 40f, w - 160f, 44f,
                $"每日免费绑定薄礼：{rewardPreview}（自动发至邮箱）", 15f, TournamentArtCatalog.Ink, TextAlignmentOptions.Center, wrap: true);

            uint targetSeason = champ.SeasonId;
            bool canSalute = !champ.SalutedToday && !isBusy;
            TournamentArtCatalog.ArtButton("SalCancelBtn", _modalContent, 140f, h - 78f, 148f, 44f, "关闭", () =>
            {
                _model.CloseModal();
                ForceCloseModalVisual();
            }, primary: false, fontSize: 18f);
            TournamentArtCatalog.V2PrimaryButton("SalConfirmBtn", _modalContent, 312f, h - 78f, 188f, 44f,
                champ.SalutedToday ? "今日已观道" : "观道致敬",
                () => SaluteChampionConfirmed?.Invoke(targetSeason),
                interactable: canSalute, fontSize: 19f);
        }

        private static string BuildSaluteRewardPreview()
        {
            var rules = TournamentRuleTableManager.Instance.FindAll();
            uint gold = 5000u;
            uint itemId = 20u;
            uint itemCount = 3u;
            if (rules != null && rules.Count > 0 && rules[0] != null)
            {
                if (rules[0].SaluteGoldAmount > 0) gold = rules[0].SaluteGoldAmount;
                if (rules[0].SaluteItemId > 0) itemId = rules[0].SaluteItemId;
                if (rules[0].SaluteItemCount > 0) itemCount = rules[0].SaluteItemCount;
            }
            return $"绑定银两 ×{gold.ToString("N0", CultureInfo.InvariantCulture)} · {TournamentArtCatalog.ResolveItemName(itemId)} ×{itemCount}";
        }

        // ==================== 格式化辅助 ====================

        private int ComputeLineupSig()
        {
            int hash = _model.LineupDraft.Count * 397;
            for (int i = 0; i < _model.LineupDraft.Count; i++)
                hash = unchecked(hash * 31 + _model.LineupDraft[i].GetHashCode());
            return hash;
        }

        private int ComputeInviteSig()
        {
            int hash = unchecked(((int)_model.InviteSource * 397) ^ (_lastInviteSourceStatus?.GetHashCode() ?? 0));
            if (_lastInviteCandidates != null)
            {
                hash = unchecked(hash * 31 + _lastInviteCandidates.Count);
                for (int i = 0; i < _lastInviteCandidates.Count; i++)
                {
                    var c = _lastInviteCandidates[i];
                    hash = unchecked(hash * 31 + c.PlayerId.GetHashCode());
                    hash = unchecked(hash * 31 + (c.IsOnline ? 1 : 0));
                }
            }
            return hash;
        }

        private static string DisplayMemberName(TournamentMemberView m) =>
            m == null ? string.Empty : (!string.IsNullOrEmpty(m.PlayerName) ? m.PlayerName : $"道友 #{m.PlayerId}");

        private static string FormatRewardItems(IList<TournamentRewardItem> items, ulong gold)
        {
            var parts = new List<string>();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it != null && it.ItemId != 0 && it.Count > 0)
                        parts.Add($"{TournamentArtCatalog.ResolveItemName(it.ItemId)} ×{it.Count}");
                }
            }
            if (gold > 0UL) parts.Add($"绑定银两 ×{gold.ToString("N0", CultureInfo.InvariantCulture)}");
            return parts.Count > 0 ? string.Join(" · ", parts) : "荣耀纪念礼";
        }

        private static string FormatHonorLine(string titleCode, string badgeCode)
        {
            bool hasTitle = !string.IsNullOrEmpty(titleCode);
            bool hasBadge = !string.IsNullOrEmpty(badgeCode);
            if (hasTitle && hasBadge) return $"称号 [{titleCode}] · 徽章 [{badgeCode}]";
            if (hasTitle) return $"称号 [{titleCode}]";
            if (hasBadge) return $"徽章 [{badgeCode}]";
            return string.Empty;
        }

        private static string FormatUtc8Date(ulong unixMs)
        {
            try
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)unixMs).ToOffset(TimeSpan.FromHours(8));
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
