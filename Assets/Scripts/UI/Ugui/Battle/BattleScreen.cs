using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Battle.Presentation;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Table;
using MmorpgClient.UI.Ugui.CoreLoop;
using MmorpgClient.UI.Ugui.Tweening;
using MmorpgClient.World;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 全屏回合战斗屏(问道式,纯 UGUI,turn-battle-presentation.md §3/§4):
    ///   - 舞台:BattleStage 槽位(上敌下我、两排交错、近大远小)+ BattleUnitView 图片单位;
    ///   - 演出:TurnResult 到达 → TurnPlan.Build → BattlePresenter/BattleSequencer 逐拍播放
    ///     (冲刺/施法/受击/飘字/特效/暴击顿帧震屏/死亡),播完全量应用 state(Ack 由 BattleUiRoot 调);
    ///   - HUD:左上回合数(翻数)+ 战斗记录;顶部行动预告条;右上自己与队友卡片;
    ///     右下命令环(攻击/法术/防御/道具/捕捉/逃跑/自动;自动战斗中换成 角色/宠物/取消自动);
    ///   - 目标选择:点单位(BattleUnitView 点击区)高亮,确认后 SubmitAction;
    ///   - 回合倒计时:环形 + 数字(action_deadline_ms);已提交显示「等待其他玩家…」;
    ///   - 观战只读模式(OpenSpectate):隐藏命令环,顶部显示观众数;回合播放复用 CoPlayTurn
    ///     (观战流无 AckTurnPlayed 契约,Ack 与否由 BattleUiRoot 按模式区分);
    ///   - 观战抢占/断线:AbortPlayback → BattlePresenter.Abort 复位所有 tween/飘字/特效;
    ///   - 战斗直连(turn-based §22 D74,直连是战斗唯一通路):出手按钮按 BattleClient.IsBattleChannelReady 置灰,
    ///     顶部横幅显示「正在连接战斗服务器…」/「无法连接战斗服务器」+「重新连接」;状态每帧轮询
    ///     BattleClient(唯一事实源),不在本类另存一份连接状态。
    /// 对 BattleUiRoot 的公开接口与旧版保持一致。
    /// </summary>
    public sealed class BattleScreen
    {
        private enum PendingKind { None, Attack, Skill, Item, Capture, RecallPet }

        // ── HUD 固定控件矩形(设计坐标,y 向下;BattleUiLayout 用它们断言任何分辨率下都在屏内) ──
        public const float RoundCounterX = 40f;
        public const float RoundCounterY = 20f;
        public static readonly Rect LogButtonRect = new Rect(356f, 28f, 170f, 54f);
        public static readonly Rect TimerRect = new Rect((QdaoUguiTheme.DesignWidth - 90f) * 0.5f, 24f, 90f, 90f);
        public static readonly Rect StopWatchRect = new Rect(2300f, 980f, 220f, 64f);
        // 战报回放控制条(只在 _replay 模式可见,贴在 HudBottomBand 之下,与目标确认条同带且互斥)
        public static readonly Rect ReplayPrevRect = new Rect(660f, BattleStage.HudBottomBand, 136f, 56f);
        public static readonly Rect ReplayPauseRect = new Rect(808f, BattleStage.HudBottomBand, 120f, 56f);
        public static readonly Rect ReplayNextRect = new Rect(940f, BattleStage.HudBottomBand, 136f, 56f);
        public static readonly Rect ReplaySpeedRect = new Rect(1088f, BattleStage.HudBottomBand, 120f, 56f);
        public static readonly Rect ReplaySkipRect = new Rect(1220f, BattleStage.HudBottomBand, 136f, 56f);
        public static readonly Rect ReplayJumpInputRect = new Rect(1380f, BattleStage.HudBottomBand, 110f, 56f);
        public static readonly Rect ReplayJumpRect = new Rect(1500f, BattleStage.HudBottomBand, 110f, 56f);
        public static readonly Rect ReplayStatsRect = new Rect(1628f, BattleStage.HudBottomBand, 160f, 56f);
        /// <summary>
        /// 目标提示/确认/取消条:贴在 BattleStage.HudBottomBand(992,按视频我方玩家排 slot0 脚底 948.6 + 名字 40 定)之下,
        /// 不压我方后排;高 56 → 底边 1048,再往下 1048..1080 是底部提示文字(HintRect)。
        /// </summary>
        public static readonly Rect TargetHintRect = new Rect(724f, BattleStage.HudBottomBand + 4f, 560f, 48f);
        public static readonly Rect ConfirmRect = new Rect(1310f, BattleStage.HudBottomBand, 240f, 56f);
        public static readonly Rect CancelRect = new Rect(1570f, BattleStage.HudBottomBand, 240f, 56f);
        /// <summary>底部提示文字(确认/取消条底边 1048 之下,贴屏底)。</summary>
        public static readonly Rect HintRect = new Rect(680f, 1048f, 1200f, 32f);
        /// <summary>左列「未行动」文字(相位文字之下)。</summary>
        public static readonly Rect PendingTextRect = new Rect(40f, 136f, 700f, 30f);
        /// <summary>
        /// 战斗直连提示横幅:放在全局 toast 带(<see cref="BattleUiRoot.ToastRect"/>,y 150..206)之下,
        /// 左端让开左列(战斗记录面板 / 未行动,x ≤ 760)——toast 层在战斗层之上,两者同位会叠字,
        /// 恰在最需要看清连接状态时两段都看不清。失败文案约 40 个汉字,22 号字放得下。
        /// 会压住敌方后排头顶一小段,只在连不上 / 久连不上时出现,提示优先于舞台。
        /// </summary>
        public static readonly Rect ChannelBannerRect = new Rect(780f, 214f, 1120f, 56f);
        /// <summary>
        /// 「重新连接」:横幅右侧,同样在 toast 带之下;在计时环(y ≤ 114)之下,在右上角色卡(x ≥ 2190)之左。
        /// </summary>
        public static readonly Rect ReconnectRect = new Rect(1916f, 210f, 200f, 64f);

        /// <summary>
        /// 直连判定连不上时横幅的文案(客户端本地字符串,服务端没有对应消息)。客户端分不清是本机网络的问题还是战斗服故障,
        /// 两种结局都要说到(同 <see cref="BattleClient.BattleChannelFailedText"/>):前者由服务端代为出手,后者本局会被作废。
        /// 横幅宽 1088、字号 22、超出按省略号截断:文案不得比 46 个全角字更长(现 36 字)。
        /// </summary>
        public const string ChannelFailedBannerText = "无法连接战斗服务器:网络受阻时本局由系统代为出手,服务器故障时本局会作废";
        /// <summary>直连已判定连不上、轮到本人出手时底部提示的文案。</summary>
        public const string ChannelFailedHintText = "无法连接战斗服务器,战斗若仍在进行,本回合到时由系统代为出手";
        /// <summary>直连握手中 / 断线恢复中超过宽限期时横幅的文案。</summary>
        public const string ChannelConnectingBannerText = "正在连接战斗服务器…";
        /// <summary>最后一回合已播完、已出胜负,等 scene 结算包期间的相位文案。</summary>
        public const string BattleSettlingPhaseText = "战斗结算中…";
        /// <summary>直连已判定连不上时左上相位文字(链路不会自己恢复,不能再说「等待」)。</summary>
        public const string ChannelFailedPhaseText = "无法连接战斗服务器";

        private readonly BattleUiRoot _owner;
        private readonly RectTransform _root;

        // 层(兄弟顺序即绘制顺序)
        private readonly RectTransform _world;        // 舞台/特效/名牌/数字的共同父节点(震屏整体位移)
        private readonly RectTransform _stageRoot;    // 单位
        private readonly RectTransform _fxLayer;      // 特效
        private readonly RectTransform _plateLayer;   // 名牌(名字/血蓝条/buff 行):立绘与特效之上,不被遮
        private readonly RectTransform _numberLayer;  // 伤害数字
        private readonly RectTransform _overlayLayer; // 入场云层 / 出生光环
        private readonly RectTransform _hudRoot;      // HUD

        // HUD
        private readonly BattleRoundCounter _roundCounter;
        private readonly RectTransform _guildWarFrenzyRoot;
        private readonly TMP_Text _guildWarFrenzyText;
        private readonly BattleLogPanel _logPanel;
        private readonly BattleActionOrderBar _orderBar;
        private readonly BattlePartyCards _partyCards;
        private readonly BattleCommandRing _commandRing;
        private readonly TMP_Text _phaseText;
        private readonly TMP_Text _pendingText;
        private readonly RectTransform _timerRoot;
        private readonly Image _timerRing;
        private readonly TMP_Text _timerText;
        private float _safeTopOffsetDesign = -1f;
        public static readonly Rect SurrenderButtonRect = new Rect(40f, 576f, 220f, 56f);
        public static readonly Rect SummonButtonRect = new Rect(40f, 652f, 220f, 64f);
        public static readonly Rect SummonDrawerRect = new Rect(760f, 690f, 1040f, 270f);
        public static readonly Rect PrepPanelRect = new Rect(300f, 680f, 1960f, 370f);
        private readonly UiTextButton _surrenderButton;
        private readonly UiTextButton _summonButton;
        private readonly RectTransform _summonDrawer;
        private readonly UiTextButton _summonConfirmButton;
        private readonly PrepPetSlotView[] _summonPetSlots = new PrepPetSlotView[5];
        private readonly ulong[] _summonPetIds = new ulong[5];
        private ulong _summonSelectedPetId;
        private ulong _summonTrackedBattleId;
        private bool _summonEverAvailable;
        private bool _summonRequestPending;
        private readonly RectTransform _surrenderConfirmStrip;
        private readonly TMP_Text _surrenderConfirmText;
        private readonly RectTransform _surrenderVoteStrip;
        private readonly TMP_Text _surrenderVoteText;
        private readonly UiTextButton _surrenderAgreeButton;
        private readonly UiTextButton _surrenderRejectButton;
        private readonly UiTextButton _quickParryButton;
        private bool _surrenderRequestPending;

        // 观战 / 回放只读模式
        private readonly TMP_Text _spectateText;
        private readonly UiTextButton _stopWatchButton;
        private readonly UiTextButton _replayPrevButton;
        private readonly UiTextButton _replayPauseButton;
        private readonly UiTextButton _replayNextButton;
        private readonly UiTextButton _replaySpeedButton;
        private readonly UiTextButton _replaySkipButton;
        private readonly RectTransform _replayJumpInputPlate;
        private readonly TMP_InputField _replayJumpInput;
        private readonly UiTextButton _replayJumpButton;
        private readonly UiTextButton _replayStatsButton;
        private readonly BattleReplayOverlay _replayOverlay;

        // 目标选择 / 确认
        private readonly TMP_Text _targetHintText;
        private readonly UiTextButton _confirmButton;
        private readonly UiTextButton _cancelButton;

        // 技能子面板
        private readonly RectTransform _skillPanel;
        private readonly RectTransform _skillViewport;
        private readonly RectTransform _skillContent;
        private readonly UnityEngine.UI.ScrollRect _skillScroll;
        private readonly TMP_Text _skillEmptyText;
        private readonly UiTextButton[] _petSkillTabs = new UiTextButton[3];
        private readonly TMP_Text _petSkillLimitText;
        private int _petSkillTab;
        private readonly List<UiTextButton> _skillButtons = new();

        // 道具子面板(道具列表未随 BattleStateS2C 下发,先用输入 id 的极简方式,见 open_issues)
        private readonly RectTransform _itemPanel;
        private readonly TMP_InputField _itemInput;

        // 底部提示
        private readonly TMP_Text _hintText;

        // 论剑 V3 战前 15 秒明牌变阵浮层（精确对齐 d129182 帮会原图返修版 [300, 680, 1960, 370] 与 RankedV3GuildFidelity 切片）
        private sealed class PrepSealSlotView
        {
            public UiTextButton Button;
            public Image Icon;
            public Image FallbackPlaque;
            public TMP_Text FallbackGlyph;
            public TMP_Text NameText;
            public TMP_Text TagText;
            public TMP_Text SelectedStamp;
        }

        private sealed class PrepPetSlotView
        {
            public UiTextButton Button;
            public Image Portrait;
            public Image FallbackPlaque;
            public TMP_Text FallbackGlyph;
            public TMP_Text NameText;
            public TMP_Text LevelText;
            public TMP_Text StatsText;
            public TMP_Text SelectedCheck;
        }

        private readonly RectTransform _prepPanel;
        private readonly CanvasGroup _prepCanvasGroup;
        private readonly TMP_Text _prepTitleText;
        private readonly TMP_Text _prepCountdownText;
        private readonly Image _prepCountdownFillImg;
        private readonly RectTransform _prepCountdownFill;
        private readonly TMP_Text _prepTeamReadyText;
        private readonly Image[] _prepReadyDots = new Image[5];
        private readonly TMP_Text _prepEnemySummaryText;
        private readonly RectTransform _prepTeamContent;
        private readonly UnityEngine.UI.ScrollRect _prepTeamScroll;
        private readonly List<GameObject> _prepTeamChips = new();
        private readonly HashSet<ulong> _expandedPrepTeamIds = new();
        private readonly TMP_Text _prepLockHintText;
        private readonly TMP_Text _prepSealEmptyText;
        private readonly TMP_Text _prepPetEmptyText;
        private readonly PrepSealSlotView[] _prepSealSlots = new PrepSealSlotView[4];
        private readonly uint[] _prepSealIds = new uint[4];
        private readonly PrepPetSlotView[] _prepPetSlots = new PrepPetSlotView[6];
        private readonly ulong[] _prepPetIds = new ulong[6];
        private readonly UiTextButton _prepLockButton;
        private readonly RectTransform _spectatorPrepBanner;
        private readonly TMP_Text _spectatorPrepText;
        private BattlePrepState _prepState;
        private bool _prepRequestPending;
        private bool _prepPanelWasVisible;
        private bool _prepExpiredShown;
        private float _prepPulseOriginTime;

        // 单位状态详情小卡（触控/点击单位打开，消费 BattleUnitStatusInspector 只读投影）
        private readonly RectTransform _statusPopup;
        private readonly TMP_Text _statusPopupTitle;
        private readonly TMP_Text _statusHealth;
        private readonly TMP_Text _statusMana;
        private readonly TMP_Text _statusShield;
        private readonly TMP_Text _statusCharge;
        private readonly RectTransform _statusBuffContent;
        private readonly ScrollRect _statusScroll;
        private readonly List<(RectTransform Root, TMP_Text Source, TMP_Text Rounds, TMP_Text Effect)> _statusBuffRows = new();
        private ulong _inspectedActorId;

        // 战斗直连提示(turn-based §22 D74)
        private readonly Image _channelBanner;
        private readonly TMP_Text _channelBannerText;
        private readonly UiTextButton _reconnectButton;
        private BattleChannelBanner _bannerShown = BattleChannelBanner.Hidden;
        private bool _channelReadyShown = true;   // 命令环上次刷新时看到的直连就绪值(变化即重刷)
        private bool _channelFailedShown;         // 同上,「已判定连不上」(底部提示文案随之切换)
        private float _notReadySince = -1f;       // 本次连续未就绪的起点(realtimeSinceStartup);-1 = 就绪

        // 单位
        private readonly List<BattleUnitView> _views = new();
        private readonly Dictionary<ulong, BattleUnitView> _viewById = new();
        private readonly HashSet<ulong> _coreTelegraphTargetIds = new();
        private ulong _coreProtectedGuardActorId;
        private ulong _festivalTelegraphTargetId;
        private string _festivalTelegraphLabel;
        private readonly BattlePresenter _presenter;

        private BattleStateS2C _state;
        private ulong _myId;
        private ulong _actingId;
        private readonly HashSet<ulong> _submittedActors = new();
        private uint _myTeam;
        private uint _lastRound;
        private ulong _deadlineMs;
        private long _deadlineStartMs;
        private BattlePhase _phase = BattlePhase.None;
        private PendingKind _pending = PendingKind.None;
        private uint _pendingSkillId;
        private uint _pendingItemId;
        private ulong _selectedTargetId;
        private bool _submitted => IsSubmitted(_actingId);
        private bool _playing;
        private bool _playDone = true;
        private bool _spectate;
        private bool _replay;
        private uint _observerCount;
        private eBattleOutcome _shownOutcome = eBattleOutcome.BattleOutcomeOngoing;
        // 论剑 V4 队内快捷战术标点浮条与连胜势旗标（多人同队对局可见，缺图时安全回退程序底色）
        private RectTransform _pingBarRoot;
        private bool _hasPingBarArt;
        private readonly UiTextButton[] _pingKindButtons = new UiTextButton[4];
        private UiTextButton _pingMuteButton;
        private TMP_Text _pingArmedHint;
        private BattlePingKind _armedPingKind = BattlePingKind.BattlePingUnspecified;
        private RectTransform _winStreakBanner;
        private TMP_Text _winStreakBannerText;

        private bool IsReadOnly => _spectate || _replay;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public uint MyTeamIndex => _myTeam;
        /// <summary>
        /// 屏上正显示的权威状态(最近一次 <see cref="ApplyState"/> 的入参对象)。BattleUiRoot 按引用
        /// 与 BattleClient.State 比较,发现直连就绪补拉带来的新状态(turn-based §22 D74)。
        /// </summary>
        internal BattleStateS2C CurrentState => _state;
        /// <summary>演出层(BattleUiRoot 结算/测试可用)。</summary>
        public BattlePresenter Presenter => _presenter;

        // Read the existing rendered objects; acceptance must never manufacture a second view.
        internal bool TryGetAppearanceView(ulong actorId, out BattleUnitView view, out Image portrait)
        {
            portrait = null;
            view = null;
            if (!IsOpen || !_viewById.TryGetValue(actorId, out view)) return false;
            var card = _hudRoot.Find("PartyCards/Card_" + actorId + "/Portrait");
            if (card != null) portrait = card.GetComponent<Image>();
            return view.Root != null && view.Root.gameObject.activeInHierarchy && portrait != null &&
                portrait.isActiveAndEnabled;
        }

        public BattleScreen(BattleUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;
            _root = QdaoUguiFactory.CreateRect("BattleScreen", parent,
                0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);

            // 舞台/特效/数字层各自嵌套 Canvas:单位呼吸、冲刺、飘字只重建各自子画布,HUD 静态批次不动;
            // 舞台层自带 GraphicRaycaster(嵌套 Canvas 的图形只由本画布的 raycaster 命中,单位点击区在此)
            _world = CreateLayer("World", nestedCanvas: false, raycaster: false);
            _stageRoot = CreateLayer("Stage", nestedCanvas: true, raycaster: true, parent: _world);
            _fxLayer = CreateLayer("Fx", nestedCanvas: true, raycaster: false, parent: _world);
            _plateLayer = CreateLayer("Plates", nestedCanvas: true, raycaster: true, parent: _world);
            _numberLayer = CreateLayer("Numbers", nestedCanvas: true, raycaster: false, parent: _world);
            _overlayLayer = CreateLayer("Overlay", nestedCanvas: false, raycaster: false);
            _hudRoot = CreateLayer("Hud", nestedCanvas: false, raycaster: false);

            _presenter = new BattlePresenter(owner, _stageRoot, _fxLayer, _numberLayer, _overlayLayer,
                id => _viewById.TryGetValue(id, out var v) ? v : null, () => _views, ResolveName, _world);
            _presenter.OnFinished += () => _playDone = true;
            _presenter.OnAborted += () => _playDone = true;
            _presenter.OnBeatStarted += (beat, index) => _orderBar.Highlight(beat.ActorId);
            _presenter.OnLog += line => _logPanel.Append(line);
            _presenter.OnHealthChanged += (id, hp) => _partyCards.SetHealth(id, hp);
            _presenter.OnManaChanged += (id, mp) => _partyCards.SetMana(id, mp);

            // ── 左上:回合 + 战斗记录 + 相位/未行动(左列,避开顶部预告条与敌方后排的头顶条) ──
            _roundCounter = new BattleRoundCounter(_hudRoot, RoundCounterX, RoundCounterY);
            _guildWarFrenzyRoot = QdaoUguiFactory.CreateRect("GuildWarFrenzyBadge", _hudRoot,
                RoundCounterX + BattleRoundCounter.Width + 14f, RoundCounterY + 4f, 340f, 62f);
            var frenzyBg = _guildWarFrenzyRoot.gameObject.AddComponent<Image>();
            frenzyBg.color = new Color(0.38f, 0.09f, 0.07f, 0.88f);
            frenzyBg.raycastTarget = false;
            var frenzyIcon = QdaoUguiFactory.CreateImage("Icon", _guildWarFrenzyRoot, 6f, 5f, 52f, 52f,
                Guild.GuildWarUiArt.Load("badge_frenzy_round12"));
            frenzyIcon.preserveAspect = true;
            frenzyIcon.raycastTarget = false;
            _guildWarFrenzyText = QdaoUguiFactory.CreateText("Text", _guildWarFrenzyRoot, 64f, 0f, 270f, 62f,
                string.Empty, 20f, Guild.GuildWarUiArt.LightText, TextAlignmentOptions.MidlineLeft);
            _guildWarFrenzyText.raycastTarget = false;
            _guildWarFrenzyRoot.gameObject.SetActive(false);
            _logPanel = new BattleLogPanel(_hudRoot, LogButtonRect.x, LogButtonRect.y);
            _phaseText = QdaoUguiFactory.CreateText("Phase", _hudRoot, 40f, 98f, 620f, 36f,
                string.Empty, 24f, BattleUiStyle.WarnText, TextAlignmentOptions.MidlineLeft);
            _pendingText = QdaoUguiFactory.CreateText("Pending", _hudRoot,
                PendingTextRect.x, PendingTextRect.y, PendingTextRect.width, PendingTextRect.height,
                string.Empty, 18f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.MidlineLeft);
            _pendingText.overflowMode = TextOverflowModes.Ellipsis;

            // ── 顶部:行动预告条,从中央倒计时两侧依次排开 ──
            _orderBar = new BattleActionOrderBar(_hudRoot);

            // ── 顶部中央:回合剩余秒数 + 计时环,共用服务端行动截止时间 ──
            _timerRoot = QdaoUguiFactory.CreateRect("ActionTimer", _hudRoot, TimerRect.x, TimerRect.y, TimerRect.width, TimerRect.height);
            var timerBg = BattleUiWidgets.CreatePanel("TimerBg", _timerRoot, 0f, 0f, TimerRect.width, TimerRect.height, BattleUiStyle.PanelBg, false);
            BattleRoundCounter.ApplyNineSlice(timerBg, "panel_9slice");
            _timerRing = QdaoUguiFactory.CreateImage("TimerRing", _timerRoot, 0f, 0f, TimerRect.width, TimerRect.height, BattleArtCatalog.RingSprite);
            _timerRing.type = Image.Type.Filled;
            _timerRing.fillMethod = Image.FillMethod.Radial360;
            _timerRing.fillOrigin = (int)Image.Origin360.Top;
            _timerRing.fillClockwise = false;
            _timerRing.color = BattleUiStyle.WarnText;
            _timerRing.raycastTarget = false;
            _timerText = QdaoUguiFactory.CreateText("TimerText", _timerRoot, 0f, 10f, TimerRect.width, 48f,
                string.Empty, 36f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            _timerText.fontStyle = FontStyles.Bold;
            QdaoUguiFactory.CreateText("SecondsUnit", _timerRoot, 0f, 54f, TimerRect.width, 24f,
                "秒", 18f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            _timerRoot.gameObject.SetActive(false);

            // ── 右上:角色卡 ──
            _partyCards = new BattlePartyCards(_hudRoot);

            // ── 右下:命令环 ──
            _commandRing = new BattleCommandRing(_hudRoot, OnCommand, OnAutoCommand);

            // ── 目标选择 / 确认条(HudBottomBand 之下,我方后排脚下名字之外) ──
            _targetHintText = QdaoUguiFactory.CreateText("TargetHint", _hudRoot, TargetHintRect.x, TargetHintRect.y, TargetHintRect.width, TargetHintRect.height,
                string.Empty, 22f, BattleUiStyle.WarnText);
            _confirmButton = BattleUiWidgets.CreateTextButton("ConfirmAction", _hudRoot, ConfirmRect.x, ConfirmRect.y, ConfirmRect.width, ConfirmRect.height,
                "确认行动", 24f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_confirmButton.Plate, "button_9slice");
            _confirmButton.Button.onClick.AddListener(OnConfirmClicked);
            _cancelButton = BattleUiWidgets.CreateTextButton("CancelAction", _hudRoot, CancelRect.x, CancelRect.y, CancelRect.width, CancelRect.height,
                "取消", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_cancelButton.Plate, "button_9slice");
            _cancelButton.Button.onClick.AddListener(ExitTargetMode);

            // ── 技能子面板 ──
            var skillBg = BattleUiWidgets.CreatePanel("SkillPanel", _hudRoot,
                830f, 380f, 900f, 400f, BattleUiStyle.PanelBgLight);
            BattleRoundCounter.ApplyNineSlice(skillBg, "panel_9slice");
            _skillPanel = (RectTransform)skillBg.transform;
            QdaoUguiFactory.CreateText("SkillTitle", _skillPanel, 24f, 12f, 400f, 40f,
                "选择技能", 28f, QdaoUguiTheme.Cream);
            var skillClose = BattleUiWidgets.CreateTextButton("SkillClose", _skillPanel, 826f, 8f, 58f, 58f,
                "×", 26f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            skillClose.Button.onClick.AddListener(() => _skillPanel.gameObject.SetActive(false));
            _skillEmptyText = QdaoUguiFactory.CreateText("SkillEmpty", _skillPanel, 24f, 70f, 852f, 40f,
                "暂无可施放技能", 24f, QdaoUguiTheme.Cream);
            string[] petTabNames = { "攻击", "障碍", "辅助" };
            for (int i = 0; i < _petSkillTabs.Length; i++)
            {
                int tabIndex = i;
                var tab = BattleUiWidgets.CreateTextButton("PetSkillTab" + i, _skillPanel,
                    24f + i * 286f, 69f, 274f, 48f, petTabNames[i], 23f,
                    BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
                BattleRoundCounter.ApplyNineSlice(tab.Plate, "button_9slice");
                tab.Button.onClick.AddListener(() => { _petSkillTab = tabIndex; PopulateSkillPanel(); });
                _petSkillTabs[i] = tab;
                tab.SetVisible(false);
            }
            _petSkillLimitText = QdaoUguiFactory.CreateText("PetSkillLimit", _skillPanel,
                24f, 122f, 852f, 29f, "攻击／障碍／辅助三类合计施放 1 招", 19f,
                QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);
            _petSkillLimitText.gameObject.SetActive(false);
            _skillViewport = QdaoUguiFactory.CreateRect("SkillViewport", _skillPanel, 24f, 76f, 852f, 300f);
            var skillViewportImage = _skillViewport.gameObject.AddComponent<Image>();
            skillViewportImage.color = new Color(0f, 0f, 0f, 0.001f);
            skillViewportImage.raycastTarget = true;
            _skillViewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            _skillContent = QdaoUguiFactory.CreateRect("SkillContent", _skillViewport, 0f, 0f, 852f, 300f);
            _skillScroll = _skillViewport.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            _skillScroll.viewport = _skillViewport;
            _skillScroll.content = _skillContent;
            _skillScroll.horizontal = false;
            _skillScroll.vertical = true;
            _skillScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            _skillScroll.scrollSensitivity = 60f;

            // ── 道具子面板 ──
            var itemBg = BattleUiWidgets.CreatePanel("ItemPanel", _hudRoot,
                830f, 500f, 900f, 220f, BattleUiStyle.PanelBgLight);
            BattleRoundCounter.ApplyNineSlice(itemBg, "panel_9slice");
            _itemPanel = (RectTransform)itemBg.transform;
            QdaoUguiFactory.CreateText("ItemTitle", _itemPanel, 24f, 12f, 400f, 40f,
                "使用道具", 24f, QdaoUguiTheme.Cream);
            var itemClose = BattleUiWidgets.CreateTextButton("ItemClose", _itemPanel, 846f, 10f, 44f, 44f,
                "×", 26f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            itemClose.Button.onClick.AddListener(() => _itemPanel.gameObject.SetActive(false));
            QdaoUguiFactory.CreateText("ItemHint", _itemPanel, 24f, 62f, 852f, 34f,
                "战斗道具列表暂未接入,请输入道具表ID:", 20f, QdaoUguiTheme.StatusCream);
            var itemInputPlate = BattleUiWidgets.CreatePanel("ItemInputPlate", _itemPanel,
                24f, 106f, 300f, 56f, BattleUiStyle.PanelBg);
            _itemInput = QdaoUguiFactory.CreateInputField("ItemInput", itemInputPlate.transform,
                0f, 0f, 300f, 56f, "道具表ID", 10);
            _itemInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            var itemUse = BattleUiWidgets.CreateTextButton("ItemUse", _itemPanel, 340f, 106f, 160f, 56f,
                "选定", 22f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            itemUse.Button.onClick.AddListener(OnItemPicked);

            _hintText = QdaoUguiFactory.CreateText("Hint", _hudRoot, HintRect.x, HintRect.y, HintRect.width, HintRect.height,
                string.Empty, 22f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);

            // ── 观战 / 回放头部与退出键(左列第三行 + 右下退出) ──
            _spectateText = QdaoUguiFactory.CreateText("SpectateInfo", _hudRoot, 40f, 170f, 620f, 40f,
                string.Empty, 24f, QdaoUguiTheme.Cream);
            _stopWatchButton = BattleUiWidgets.CreateTextButton("StopWatch", _hudRoot, StopWatchRect.x, StopWatchRect.y, StopWatchRect.width, StopWatchRect.height,
                "退出观战", 24f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_stopWatchButton.Plate, "button_9slice");
            _stopWatchButton.Button.onClick.AddListener(() =>
            {
                if (_replay) _owner?.RequestExitReplay();
                else _owner?.RequestStopSpectate();
            });
            _spectateText.gameObject.SetActive(false);
            _stopWatchButton.SetVisible(false);

            // ── 战报回放控制条(仅 _replay 模式可见) ──
            _replayPrevButton = CreateReplayButton("ReplayPrev", ReplayPrevRect, "上一回合", BattleUiStyle.ButtonPlate, () => _owner?.RequestReplayPrevRound());
            _replayPauseButton = CreateReplayButton("ReplayPause", ReplayPauseRect, "暂停", BattleUiStyle.ButtonPlateAccent, () => _owner?.RequestReplayTogglePause());
            _replayNextButton = CreateReplayButton("ReplayNext", ReplayNextRect, "下一回合", BattleUiStyle.ButtonPlate, () => _owner?.RequestReplayNextRound());
            _replaySpeedButton = CreateReplayButton("ReplaySpeed", ReplaySpeedRect, "1x", BattleUiStyle.ButtonPlate, () => _owner?.RequestReplayCycleSpeed());
            _replaySkipButton = CreateReplayButton("ReplaySkip", ReplaySkipRect, "跳过终态", BattleUiStyle.ButtonPlate, () => _owner?.RequestReplaySkip());
            var jumpPlate = BattleUiWidgets.CreatePanel("ReplayJumpPlate", _hudRoot,
                ReplayJumpInputRect.x, ReplayJumpInputRect.y, ReplayJumpInputRect.width, ReplayJumpInputRect.height, BattleUiStyle.PanelBgLight);
            _replayJumpInputPlate = (RectTransform)jumpPlate.transform;
            _replayJumpInput = QdaoUguiFactory.CreateInputField("ReplayJumpInput", _replayJumpInputPlate,
                0f, 0f, ReplayJumpInputRect.width, ReplayJumpInputRect.height, "回合", 4);
            _replayJumpInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            _replayJumpButton = CreateReplayButton("ReplayJump", ReplayJumpRect, "跳转", BattleUiStyle.ButtonPlate, OnReplayJumpClicked);
            _replayStatsButton = CreateReplayButton("ReplayStats", ReplayStatsRect, "数据复盘", BattleUiStyle.ButtonPlateAccent, () => _replayOverlay?.ToggleStats());
            var statsIconSprite = BattleReportUiArt.TryLoad(BattleReportUiArt.IconStatsChart);
            if (statsIconSprite != null)
            {
                var statsIcon = QdaoUguiFactory.CreateImage("StatsIcon", _replayStatsButton.Rect, 10f, 12f, 32f, 32f, statsIconSprite, raycastTarget: false);
                statsIcon.preserveAspect = true;
                _replayStatsButton.Label.rectTransform.offsetMin = new Vector2(36f, 0f);
            }
            _replayOverlay = new BattleReplayOverlay(_hudRoot, round => _owner?.RequestReplayJumpRound(round));
            SetReplayControlsVisible(false);

            // ── 战斗直连提示横幅 + 重新连接(顶部预告条之下;默认隐藏,Tick 按 BattleClient 状态切档) ──
            _channelBanner = BattleUiWidgets.CreatePanel("ChannelBanner", _hudRoot,
                ChannelBannerRect.x, ChannelBannerRect.y, ChannelBannerRect.width, ChannelBannerRect.height,
                BattleUiStyle.PanelBg, raycastTarget: false);
            _channelBannerText = QdaoUguiFactory.CreateText("ChannelBannerText", _channelBanner.transform,
                16f, 0f, ChannelBannerRect.width - 32f, ChannelBannerRect.height,
                string.Empty, 22f, BattleUiStyle.WarnText, TextAlignmentOptions.Center);
            _channelBannerText.overflowMode = TextOverflowModes.Ellipsis;
            _reconnectButton = BattleUiWidgets.CreateTextButton("ChannelReconnect", _hudRoot,
                ReconnectRect.x, ReconnectRect.y, ReconnectRect.width, ReconnectRect.height,
                "重新连接", 24f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_reconnectButton.Plate, "button_9slice");
            _reconnectButton.Button.onClick.AddListener(OnReconnectClicked);
            _channelBanner.gameObject.SetActive(false);
            _reconnectButton.SetVisible(false);

            // ── PVP 延迟投降入口与确认/表决浮条（ranked-play-v2-ui-20261125 宣纸金框切片，左侧布局远离技能环与胶囊） + 首领试炼 V2 一键化劲防御 ──
            _surrenderButton = BattleUiWidgets.CreateTextButton("SurrenderBtn", _hudRoot,
                SurrenderButtonRect.x, SurrenderButtonRect.y, SurrenderButtonRect.width, SurrenderButtonRect.height,
                "认输 / 投降", 21f, BattleUiStyle.ButtonPlate, SeasonEconomyUiArt.BattleArenaInk);
            var surrenderBtnSprite = SeasonEconomyUiArt.LoadBattleArenaControl("surrender-button-base");
            if (surrenderBtnSprite != null)
            {
                SeasonEconomyUiArt.ApplyFixedControlSprite(_surrenderButton.Plate, surrenderBtnSprite, SeasonEconomyUiArt.BattleArenaPaper);
                _surrenderButton.Label.color = SeasonEconomyUiArt.BattleArenaInk;
                _surrenderButton.Label.fontStyle = FontStyles.Bold;
            }
            else
            {
                BattleRoundCounter.ApplyNineSlice(_surrenderButton.Plate, "button_9slice");
                _surrenderButton.Label.color = BattleUiStyle.ButtonText;
            }
            _surrenderButton.Button.onClick.AddListener(OnSurrenderClicked);
            _surrenderButton.SetVisible(false);

            var voteStripSprite = SeasonEconomyUiArt.LoadBattleArenaControl("surrender-vote-strip-base");
            var primaryBtnSprite = SeasonEconomyUiArt.LoadBattleArenaAsset("guild_button_primary");
            var secondaryBtnSprite = SeasonEconomyUiArt.LoadBattleArenaAsset("guild_button_secondary");

            var confirmBg = BattleUiWidgets.CreatePanel("SurrenderConfirmStrip", _hudRoot, 280f, 480f, 880f, 92f,
                SeasonEconomyUiArt.BattleArenaPaper, raycastTarget: false);
            if (voteStripSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(confirmBg, voteStripSprite, SeasonEconomyUiArt.BattleArenaPaper);
            else BattleRoundCounter.ApplyNineSlice(confirmBg, "panel_9slice");
            _surrenderConfirmStrip = confirmBg.rectTransform;
            _surrenderConfirmText = QdaoUguiFactory.CreateText("ConfirmText", _surrenderConfirmStrip, 34f, 16f, 520f, 60f,
                string.Empty, 20f, voteStripSprite != null ? SeasonEconomyUiArt.BattleArenaInk : QdaoUguiTheme.Cream, TextAlignmentOptions.MidlineLeft);
            _surrenderConfirmText.fontStyle = FontStyles.Bold;
            _surrenderConfirmText.textWrappingMode = TextWrappingModes.Normal;
            _surrenderConfirmText.enableAutoSizing = true;
            _surrenderConfirmText.fontSizeMin = 15f;
            _surrenderConfirmText.fontSizeMax = 20f;
            _surrenderConfirmText.raycastTarget = false;
            var confirmYes = BattleUiWidgets.CreateTextButton("SurrenderConfirmYes", _surrenderConfirmStrip, 566f, 18f, 140f, 56f,
                "确认认输", 20f, BattleUiStyle.ButtonPlateAccent, SeasonEconomyUiArt.GoldText);
            if (primaryBtnSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(confirmYes.Plate, primaryBtnSprite, SeasonEconomyUiArt.BattleArenaJade);
            else BattleRoundCounter.ApplyNineSlice(confirmYes.Plate, "button_9slice");
            confirmYes.Button.onClick.AddListener(OnConfirmSurrenderClicked);
            var confirmNo = BattleUiWidgets.CreateTextButton("SurrenderConfirmNo", _surrenderConfirmStrip, 718f, 18f, 134f, 56f,
                "取消", 20f, BattleUiStyle.ButtonPlate, secondaryBtnSprite != null ? SeasonEconomyUiArt.BattleArenaInk : BattleUiStyle.ButtonText);
            if (secondaryBtnSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(confirmNo.Plate, secondaryBtnSprite, SeasonEconomyUiArt.BattleArenaPaper);
            else BattleRoundCounter.ApplyNineSlice(confirmNo.Plate, "button_9slice");
            confirmNo.Button.onClick.AddListener(() => _surrenderConfirmStrip.gameObject.SetActive(false));
            _surrenderConfirmStrip.gameObject.SetActive(false);

            var voteBg = BattleUiWidgets.CreatePanel("SurrenderVoteStrip", _hudRoot, 280f, 480f, 940f, 92f,
                SeasonEconomyUiArt.BattleArenaPaper, raycastTarget: false);
            if (voteStripSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(voteBg, voteStripSprite, SeasonEconomyUiArt.BattleArenaPaper);
            else BattleRoundCounter.ApplyNineSlice(voteBg, "panel_9slice");
            _surrenderVoteStrip = voteBg.rectTransform;
            _surrenderVoteText = QdaoUguiFactory.CreateText("VoteText", _surrenderVoteStrip, 34f, 14f, 568f, 64f,
                string.Empty, 20f, voteStripSprite != null ? SeasonEconomyUiArt.BattleArenaInk : QdaoUguiTheme.Cream, TextAlignmentOptions.MidlineLeft);
            _surrenderVoteText.fontStyle = FontStyles.Bold;
            _surrenderVoteText.textWrappingMode = TextWrappingModes.Normal;
            _surrenderVoteText.enableAutoSizing = true;
            _surrenderVoteText.fontSizeMin = 14f;
            _surrenderVoteText.fontSizeMax = 20f;
            _surrenderVoteText.raycastTarget = false;
            _surrenderAgreeButton = BattleUiWidgets.CreateTextButton("SurrenderAgree", _surrenderVoteStrip, 614f, 18f, 146f, 56f,
                "同意投降", 20f, BattleUiStyle.ButtonPlateAccent, SeasonEconomyUiArt.GoldText);
            if (primaryBtnSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(_surrenderAgreeButton.Plate, primaryBtnSprite, SeasonEconomyUiArt.BattleArenaJade);
            else BattleRoundCounter.ApplyNineSlice(_surrenderAgreeButton.Plate, "button_9slice");
            _surrenderAgreeButton.Button.onClick.AddListener(() => SendSurrenderVote(true));
            _surrenderRejectButton = BattleUiWidgets.CreateTextButton("SurrenderReject", _surrenderVoteStrip, 770f, 18f, 142f, 56f,
                "继续战斗", 20f, BattleUiStyle.ButtonPlate, secondaryBtnSprite != null ? SeasonEconomyUiArt.BattleArenaInk : BattleUiStyle.ButtonText);
            if (secondaryBtnSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(_surrenderRejectButton.Plate, secondaryBtnSprite, SeasonEconomyUiArt.BattleArenaPaper);
            else BattleRoundCounter.ApplyNineSlice(_surrenderRejectButton.Plate, "button_9slice");
            _surrenderRejectButton.Button.onClick.AddListener(() => SendSurrenderVote(false));
            _surrenderVoteStrip.gameObject.SetActive(false);

            _quickParryButton = BattleUiWidgets.CreateTextButton("QuickParryBtn", _hudRoot, 1660f, 926f, 320f, 54f,
                "一键化劲防御", 22f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_quickParryButton.Plate, "button_9slice");
            var quickParrySprite = CoreLoopUiArt.LoadV2("badge_quick_parry");
            if (quickParrySprite != null)
            {
                var icon = QdaoUguiFactory.CreateImage("QuickParryBadge", _quickParryButton.Rect,
                    14f, 13f, 28f, 28f, quickParrySprite, false);
                icon.preserveAspect = true;
                var label = _quickParryButton.Label;
                label.rectTransform.anchoredPosition = new Vector2(50f, 0f);
                label.rectTransform.sizeDelta = new Vector2(258f, 54f);
                label.enableAutoSizing = true;
                label.fontSizeMin = 16f;
                label.fontSizeMax = 22f;
            }
            _quickParryButton.Button.onClick.AddListener(OnQuickParryClicked);
            _quickParryButton.SetVisible(false);

            // ── 论剑 V3 战前 15 秒明牌变阵浮层（d129182 帮会原图风格返修版：[300, 680, 1960, 370]，太极青玉标题牌 + 深玉底米白字选中态 + 三态帮会锁定键）──
            var prepShellSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("prep-shell");
            var prepBg = BattleUiWidgets.CreatePanel("TacticalPrepPanel", _hudRoot,
                PrepPanelRect.x, PrepPanelRect.y, PrepPanelRect.width, PrepPanelRect.height,
                SeasonEconomyUiArt.BattleArenaPaper, raycastTarget: false);
            if (prepShellSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(prepBg, prepShellSprite, SeasonEconomyUiArt.BattleArenaPaper);
            else BattleRoundCounter.ApplyNineSlice(prepBg, "panel_9slice");
            _prepPanel = prepBg.rectTransform;
            _prepCanvasGroup = _prepPanel.gameObject.AddComponent<CanvasGroup>();
            var kaiFont = QdaoUguiTheme.ResolveFont();
            Color prepInk = QdaoUguiTheme.Html("#142F21");
            Color prepSubInk = QdaoUguiTheme.Html("#363B2C");
            Color prepNoteInk = QdaoUguiTheme.Html("#71583B");
            Color prepJade = QdaoUguiTheme.Html("#235E42");

            // 顶中太极青玉标题牌 [680, 0, 600, 92]
            var plaqueSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("prep-title-plaque");
            var titlePlaque = QdaoUguiFactory.CreateImage("PrepTitlePlaque", _prepPanel, 680f, 0f, 600f, 92f, plaqueSprite, false);
            if (plaqueSprite != null)
            {
                titlePlaque.type = Image.Type.Simple;
                titlePlaque.color = Color.white;
            }
            else
            {
                titlePlaque.color = SeasonEconomyUiArt.BattleArenaJade;
            }
            _prepTitleText = QdaoUguiFactory.CreateText("PrepTitle", _prepPanel, 680f, 26f, 600f, 58f,
                "战前明牌变阵", 42f, QdaoUguiTheme.Html("#FFF4D6"), TextAlignmentOptions.Center);
            _prepTitleText.font = kaiFont;
            _prepTitleText.fontStyle = FontStyles.Bold;
            _prepTitleText.fontWeight = FontWeight.Bold;
            _prepTitleText.characterSpacing = 3f;
            _prepTitleText.raycastTarget = false;

            // 左上双行提示 [34, 54, 610, 48]
            var prepHintMain = QdaoUguiFactory.CreateText("PrepSubtitle", _prepPanel, 34f, 52f, 610f, 28f,
                "观敌初阵，临场易印换宠", 24f, prepSubInk, TextAlignmentOptions.MidlineLeft);
            prepHintMain.font = kaiFont;
            prepHintMain.raycastTarget = false;
            var prepHintSub = QdaoUguiFactory.CreateText("PrepSubtitleNote", _prepPanel, 34f, 80f, 610f, 24f,
                "（仅本场生效，不改城内默认配置）", 18f, prepNoteInk, TextAlignmentOptions.MidlineLeft);
            prepHintSub.raycastTarget = false;

            // 右上沙漏倒计时 [1450, 46, 180, 50]
            var clockRing = QdaoUguiFactory.CreateImage("PrepClockRing", _prepPanel, 1450f, 50f, 40f, 40f, null, false);
            clockRing.color = QdaoUguiTheme.Html("#BD9D60");
            var clockInner = QdaoUguiFactory.CreateImage("PrepClockInner", clockRing.transform, 2f, 2f, 36f, 36f, null, false);
            clockInner.color = QdaoUguiTheme.Html("#FFF8E7");
            var sandglassSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("countdown-sandglass");
            var sandglassImg = QdaoUguiFactory.CreateImage("PrepSandglass", clockRing.transform, 7.5f, 5.5f, 25f, 29f, sandglassSprite, false);
            sandglassImg.preserveAspect = true;
            sandglassImg.gameObject.SetActive(sandglassSprite != null);

            _prepCountdownText = QdaoUguiFactory.CreateText("PrepCountdown", _prepPanel, 1494f, 46f, 136f, 44f,
                "15 秒", 34f, prepJade, TextAlignmentOptions.Center);
            _prepCountdownText.fontStyle = FontStyles.Bold;
            _prepCountdownText.raycastTarget = false;
            var prepTrack = QdaoUguiFactory.CreateImage("PrepCountdownTrack", _prepPanel, 1498f, 92f, 126f, 4f, null, false);
            prepTrack.color = QdaoUguiTheme.Html("#D7CCB4");
            _prepCountdownFillImg = QdaoUguiFactory.CreateImage("PrepCountdownFill", prepTrack.transform, 0f, 0f, 126f, 4f, null, false);
            _prepCountdownFillImg.color = QdaoUguiTheme.Html("#528567");
            _prepCountdownFill = _prepCountdownFillImg.rectTransform;

            // 右上分隔线 + 本队已锁定 X/N + 5 枚竖丸状态点 [1650, 52, 274, 44]
            var readyDivider = QdaoUguiFactory.CreateImage("PrepReadyDivider", _prepPanel, 1646f, 54f, 1f, 38f, null, false);
            readyDivider.color = QdaoUguiTheme.Html("#C9B58A");
            _prepTeamReadyText = QdaoUguiFactory.CreateText("PrepTeamReadyCount", _prepPanel, 1656f, 52f, 192f, 42f,
                "本队已锁定 0/1", 23f, QdaoUguiTheme.Html("#364E32"), TextAlignmentOptions.MidlineRight);
            _prepTeamReadyText.font = kaiFont;
            _prepTeamReadyText.fontStyle = FontStyles.Bold;
            _prepTeamReadyText.raycastTarget = false;
            for (int i = 0; i < _prepReadyDots.Length; i++)
            {
                var dot = QdaoUguiFactory.CreateImage($"PrepReadyDot_{i}", _prepPanel, 1854f + i * 15f, 65f, 11f, 16f, null, false);
                dot.color = QdaoUguiTheme.Html("#E3D6B7");
                dot.gameObject.SetActive(false);
                _prepReadyDots[i] = dot;
            }

            // 第一排：临场赛季印（y=110, h=64）
            var sealLbl = QdaoUguiFactory.CreateText("PrepSealLabel", _prepPanel, 30f, 112f, 126f, 34f,
                "临场赛季印", 27f, prepInk, TextAlignmentOptions.Center);
            sealLbl.font = kaiFont;
            sealLbl.fontStyle = FontStyles.Bold;
            sealLbl.fontWeight = FontWeight.Bold;
            sealLbl.raycastTarget = false;
            var sealSubLbl = QdaoUguiFactory.CreateText("PrepSealSubLabel", _prepPanel, 30f, 146f, 126f, 24f,
                "点选即提交", 19f, QdaoUguiTheme.Html("#77613F"), TextAlignmentOptions.Center);
            sealSubLbl.raycastTarget = false;

            _prepSealEmptyText = QdaoUguiFactory.CreateText("PrepSealEmpty", _prepPanel, 168f, 110f, 1752f, 64f,
                "当前无可调整的赛季印，可直接锁定出战", 24f, QdaoUguiTheme.Html("#6D5735"), TextAlignmentOptions.Center);
            _prepSealEmptyText.raycastTarget = false;
            _prepSealEmptyText.gameObject.SetActive(false);
            var sealNormalSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("prep-seal-normal");
            for (int i = 0; i < _prepSealSlots.Length; i++)
            {
                int slotIdx = i;
                var btn = BattleUiWidgets.CreateTextButton($"PrepSeal_{i}", _prepPanel,
                    168f + i * 438f, 110f, 426f, 64f, string.Empty, 22f, SeasonEconomyUiArt.BattleArenaPaper, prepInk);
                btn.Label.gameObject.SetActive(false);
                SeasonEconomyUiArt.ApplyFixedControlSprite(btn.Plate, sealNormalSprite, SeasonEconomyUiArt.BattleArenaPaper);
                var iconRing = QdaoUguiFactory.CreateImage("SealIconRing", btn.Rect, 15f, 7f, 50f, 50f, null, false);
                iconRing.color = QdaoUguiTheme.Html("#FFF7E4");
                var icon = QdaoUguiFactory.CreateImage("SealIcon", iconRing.transform, 1f, 1f, 48f, 48f, null, false);
                icon.preserveAspect = true;
                var plaque = QdaoUguiFactory.CreateImage("SealPlaque", btn.Rect, 15f, 7f, 50f, 50f, null, false);
                plaque.color = QdaoUguiTheme.Html("#F4ECD9");
                var glyph = QdaoUguiFactory.CreateText("SealGlyph", plaque.transform, 0f, 0f, 50f, 50f,
                    "印", 24f, QdaoUguiTheme.Html("#8C764B"), TextAlignmentOptions.Center);
                glyph.font = kaiFont;
                glyph.fontStyle = FontStyles.Bold;
                glyph.raycastTarget = false;
                var nameTxt = QdaoUguiFactory.CreateText("SealName", btn.Rect, 76f, 14f, 160f, 36f,
                    string.Empty, 29f, QdaoUguiTheme.Html("#172D20"), TextAlignmentOptions.MidlineLeft);
                nameTxt.font = kaiFont;
                nameTxt.fontStyle = FontStyles.Bold;
                nameTxt.fontWeight = FontWeight.Bold;
                nameTxt.overflowMode = TextOverflowModes.Ellipsis;
                nameTxt.raycastTarget = false;
                var tagTxt = QdaoUguiFactory.CreateText("SealTag", btn.Rect, 240f, 19f, 112f, 26f,
                    string.Empty, 20f, QdaoUguiTheme.Html("#6E5134"), TextAlignmentOptions.MidlineRight);
                tagTxt.overflowMode = TextOverflowModes.Ellipsis;
                tagTxt.raycastTarget = false;
                var stampTxt = QdaoUguiFactory.CreateText("SealSelectedStamp", btn.Rect, 358f, 17f, 54f, 28f,
                    "已选", 18f, QdaoUguiTheme.Html("#FFF3D9"), TextAlignmentOptions.MidlineRight);
                stampTxt.font = kaiFont;
                stampTxt.fontStyle = FontStyles.Bold;
                stampTxt.raycastTarget = false;
                stampTxt.gameObject.SetActive(false);
                btn.Button.onClick.AddListener(() => OnPrepSealClicked(slotIdx));
                btn.SetVisible(false);
                _prepSealSlots[i] = new PrepSealSlotView
                {
                    Button = btn, Icon = icon, FallbackPlaque = plaque, FallbackGlyph = glyph,
                    NameText = nameTxt, TagText = tagTxt, SelectedStamp = stampTxt,
                };
            }

            // 第二排：临场首发宠（y=184, h=86）
            var petLbl = QdaoUguiFactory.CreateText("PrepPetLabel", _prepPanel, 30f, 192f, 126f, 34f,
                "临场首发宠", 27f, prepInk, TextAlignmentOptions.Center);
            petLbl.font = kaiFont;
            petLbl.fontStyle = FontStyles.Bold;
            petLbl.fontWeight = FontWeight.Bold;
            petLbl.raycastTarget = false;
            var petSubLbl = QdaoUguiFactory.CreateText("PrepPetSubLabel", _prepPanel, 30f, 228f, 126f, 24f,
                "速 / 血优先", 19f, QdaoUguiTheme.Html("#77613F"), TextAlignmentOptions.Center);
            petSubLbl.raycastTarget = false;

            _prepPetEmptyText = QdaoUguiFactory.CreateText("PrepPetEmpty", _prepPanel, 168f, 184f, 1752f, 86f,
                "当前无可替换备选灵宠，可直接调整赛季印并锁定", 25f, QdaoUguiTheme.Html("#6F5938"), TextAlignmentOptions.MidlineLeft);
            _prepPetEmptyText.raycastTarget = false;
            var petNormalSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("prep-pet-normal");
            for (int i = 0; i < _prepPetSlots.Length; i++)
            {
                int slotIdx = i;
                var btn = BattleUiWidgets.CreateTextButton($"PrepPet_{i}", _prepPanel,
                    168f + i * 294f, 184f, 282f, 86f, string.Empty, 20f, SeasonEconomyUiArt.BattleArenaPaper, prepInk);
                btn.Label.gameObject.SetActive(false);
                SeasonEconomyUiArt.ApplyFixedControlSprite(btn.Plate, petNormalSprite, SeasonEconomyUiArt.BattleArenaPaper);
                var portraitFrame = QdaoUguiFactory.CreateImage("PetPortraitFrame", btn.Rect, 13f, 10f, 66f, 66f, null, false);
                portraitFrame.color = QdaoUguiTheme.Html("#C5A25B");
                var portraitInner = QdaoUguiFactory.CreateImage("PetPortraitInner", portraitFrame.transform, 2f, 2f, 62f, 62f, null, false);
                portraitInner.color = QdaoUguiTheme.Html("#F6EDDA");
                var portrait = QdaoUguiFactory.CreateImage("PetPortrait", portraitInner.transform, 0f, 0f, 62f, 62f, null, false);
                portrait.preserveAspect = true;
                var plaque = QdaoUguiFactory.CreateImage("PetPlaque", portraitInner.transform, 0f, 0f, 62f, 62f, null, false);
                plaque.color = QdaoUguiTheme.Html("#F4ECD9");
                var glyph = QdaoUguiFactory.CreateText("PetGlyph", plaque.transform, 0f, 0f, 62f, 62f,
                    "灵", 28f, QdaoUguiTheme.Html("#8C764B"), TextAlignmentOptions.Center);
                glyph.font = kaiFont;
                glyph.fontStyle = FontStyles.Bold;
                glyph.raycastTarget = false;
                var nameTxt = QdaoUguiFactory.CreateText("PetName", btn.Rect, 90f, 8f, 158f, 32f,
                    string.Empty, 27f, QdaoUguiTheme.Html("#173522"), TextAlignmentOptions.MidlineLeft);
                nameTxt.font = kaiFont;
                nameTxt.fontStyle = FontStyles.Bold;
                nameTxt.fontWeight = FontWeight.Bold;
                nameTxt.overflowMode = TextOverflowModes.Ellipsis;
                nameTxt.raycastTarget = false;
                var lvlTxt = QdaoUguiFactory.CreateText("PetLevel", btn.Rect, 91f, 37f, 150f, 20f,
                    string.Empty, 17f, QdaoUguiTheme.Html("#765C3E"), TextAlignmentOptions.MidlineLeft);
                lvlTxt.raycastTarget = false;
                var statsTxt = QdaoUguiFactory.CreateText("PetStats", btn.Rect, 90f, 57f, 182f, 24f,
                    string.Empty, 21f, QdaoUguiTheme.Html("#182C20"), TextAlignmentOptions.MidlineLeft);
                statsTxt.overflowMode = TextOverflowModes.Ellipsis;
                statsTxt.raycastTarget = false;
                var checkTxt = QdaoUguiFactory.CreateText("PetSelectedCheck", btn.Rect, 250f, 8f, 22f, 22f,
                    "✓", 19f, QdaoUguiTheme.Html("#FFF4D4"), TextAlignmentOptions.Center);
                checkTxt.fontStyle = FontStyles.Bold;
                checkTxt.raycastTarget = false;
                checkTxt.gameObject.SetActive(false);
                btn.Button.onClick.AddListener(() => OnPrepPetClicked(slotIdx));
                btn.SetVisible(false);
                _prepPetSlots[i] = new PrepPetSlotView
                {
                    Button = btn, Portrait = portrait, FallbackPlaque = plaque, FallbackGlyph = glyph,
                    NameText = nameTxt, LevelText = lvlTxt, StatsText = statsTxt, SelectedCheck = checkTxt,
                };
            }

            // 底部分隔金线 + 敌方初阵 + 己方协同横向条（紧贴内金边之上）
            var coordRule = QdaoUguiFactory.CreateImage("PrepCoordDivider", _prepPanel, 31f, 274f, 1518f, 1f, null, false);
            coordRule.color = QdaoUguiTheme.Html("#CBB68C");

            var enemyLbl = QdaoUguiFactory.CreateText("PrepEnemyLabel", _prepPanel, 31f, 275f, 114f, 26f,
                "敌方初阵", 24f, QdaoUguiTheme.Html("#163C27"), TextAlignmentOptions.Center);
            enemyLbl.font = kaiFont;
            enemyLbl.fontStyle = FontStyles.Bold;
            enemyLbl.fontWeight = FontWeight.Bold;
            enemyLbl.raycastTarget = false;
            _prepEnemySummaryText = QdaoUguiFactory.CreateText("PrepEnemySummary", _prepPanel, 168f, 275f, 1380f, 26f,
                string.Empty, 22f, QdaoUguiTheme.Html("#3E412C"), TextAlignmentOptions.MidlineLeft);
            _prepEnemySummaryText.overflowMode = TextOverflowModes.Ellipsis;
            _prepEnemySummaryText.raycastTarget = false;

            var teamLbl = QdaoUguiFactory.CreateText("PrepTeamLabel", _prepPanel, 31f, 304f, 114f, 44f,
                "己方协同", 24f, QdaoUguiTheme.Html("#163C27"), TextAlignmentOptions.Center);
            teamLbl.font = kaiFont;
            teamLbl.fontStyle = FontStyles.Bold;
            teamLbl.fontWeight = FontWeight.Bold;
            teamLbl.raycastTarget = false;
            var teamViewport = QdaoUguiFactory.CreateRect("PrepTeamScroll", _prepPanel, 168f, 300f, 1380f, 52f);
            var teamViewportImg = teamViewport.gameObject.AddComponent<Image>();
            teamViewportImg.color = new Color(0f, 0f, 0f, 0.001f);
            teamViewportImg.raycastTarget = true;
            teamViewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            _prepTeamContent = QdaoUguiFactory.CreateRect("PrepTeamContent", teamViewport, 0f, 0f, 1380f, 52f);
            _prepTeamScroll = teamViewport.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            _prepTeamScroll.viewport = teamViewport;
            _prepTeamScroll.content = _prepTeamContent;
            _prepTeamScroll.horizontal = true;
            _prepTeamScroll.vertical = false;
            _prepTeamScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;

            // 右下锁定提示 + 三态帮会锁定主按钮 [1580, 294, 352, 62]
            _prepLockHintText = QdaoUguiFactory.CreateText("PrepLockHint", _prepPanel, 1580f, 271f, 352f, 23f,
                "一点易印换宠，锁定后不可更改", 19f, QdaoUguiTheme.Html("#735A36"), TextAlignmentOptions.Center);
            _prepLockHintText.raycastTarget = false;

            var lockNormalSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("lock-button-normal") ?? primaryBtnSprite;
            _prepLockButton = BattleUiWidgets.CreateTextButton("PrepLockBtn", _prepPanel, 1580f, 294f, 352f, 62f,
                "锁定出战", 32f, SeasonEconomyUiArt.BattleArenaJade, QdaoUguiTheme.Html("#FFF3D4"));
            if (lockNormalSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(_prepLockButton.Plate, lockNormalSprite, SeasonEconomyUiArt.BattleArenaJade);
            else BattleRoundCounter.ApplyNineSlice(_prepLockButton.Plate, "button_9slice");
            _prepLockButton.Label.font = kaiFont;
            _prepLockButton.Label.fontStyle = FontStyles.Bold;
            _prepLockButton.Label.fontWeight = FontWeight.Bold;
            _prepLockButton.Button.onClick.AddListener(OnPrepLockClicked);
            _prepPanel.gameObject.SetActive(false);

            // 观战者战前明牌变阵顶部专属只读提示（帮会页签风，零按钮、零 raycast、绝不透出任何私密选择）
            var specBannerSprite = SeasonEconomyUiArt.LoadBattleArenaAsset("guild_tab_normal")
                ?? SeasonEconomyUiArt.LoadBattleArenaControl("round-counter-base");
            var specBanner = BattleUiWidgets.CreatePanel("SpectatorPrepBanner", _hudRoot, 920f, 122f, 720f, 66f,
                SeasonEconomyUiArt.BattleArenaPaper, raycastTarget: false);
            if (specBannerSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(specBanner, specBannerSprite, SeasonEconomyUiArt.BattleArenaPaper);
            else BattleRoundCounter.ApplyNineSlice(specBanner, "panel_9slice");
            _spectatorPrepBanner = specBanner.rectTransform;
            _spectatorPrepText = QdaoUguiFactory.CreateText("SpectatorPrepText", _spectatorPrepBanner, 20f, 0f, 680f, 66f,
                "战前明牌变阵中…", 30f, QdaoUguiTheme.Html("#173F28"), TextAlignmentOptions.Center);
            _spectatorPrepText.font = kaiFont;
            _spectatorPrepText.fontStyle = FontStyles.Bold;
            _spectatorPrepText.fontWeight = FontWeight.Bold;
            _spectatorPrepText.raycastTarget = false;
            _spectatorPrepBanner.gameObject.SetActive(false);

            // 论剑 V4 队内快捷战术标点浮条（[40, 868, 420, 84]，原尺寸 420×84 碧玉浮条 + 4 枚 96×96 无字图标，缺图安全回退）
            var pingBarBg = BattleUiWidgets.CreatePanel("TacticalPingBar", _hudRoot, 40f, 868f, 420f, 84f,
                SeasonEconomyUiArt.BattleArenaPaper, raycastTarget: true);
            var pingBarSprite = SeasonEconomyUiArt.LoadRankedPlayV4Asset("ping_bar_base");
            _hasPingBarArt = pingBarSprite != null;
            if (_hasPingBarArt) SeasonEconomyUiArt.ApplyFixedControlSprite(pingBarBg, pingBarSprite, SeasonEconomyUiArt.BattleArenaPaper);
            else BattleRoundCounter.ApplyNineSlice(pingBarBg, "panel_9slice");
            _pingBarRoot = pingBarBg.rectTransform;
            var pingKinds = new[]
            {
                BattlePingKind.BattlePingFocus,
                BattlePingKind.BattlePingControl,
                BattlePingKind.BattlePingProtect,
                BattlePingKind.BattlePingDispel,
            };
            for (int i = 0; i < pingKinds.Length; i++)
            {
                var capturedKind = pingKinds[i];
                float btnX = _hasPingBarArt ? (32f + i * 72f) : (12f + i * 78f);
                float btnY = _hasPingBarArt ? 10f : 12f;
                float btnW = _hasPingBarArt ? 68f : 72f;
                float btnH = _hasPingBarArt ? 64f : 60f;
                var btn = BattleUiWidgets.CreateTextButton($"PingBtn_{i}", _pingBarRoot,
                    btnX, btnY, btnW, btnH, RankedFormatting.PingDisplayName(capturedKind), 20f,
                    _hasPingBarArt ? Color.clear : SeasonEconomyUiArt.BattleArenaJade, QdaoUguiTheme.Cream);
                var iconSprite = SeasonEconomyUiArt.LoadRankedPlayV4Asset(RankedFormatting.PingAssetKey(capturedKind));
                if (iconSprite != null)
                {
                    var iconImg = QdaoUguiFactory.CreateImage("Icon", btn.Rect, (btnW - 42f) * 0.5f, 2f, 42f, 42f, iconSprite, false);
                    iconImg.preserveAspect = true;
                    iconImg.color = Color.white;
                    btn.Label.rectTransform.anchoredPosition = new Vector2(0f, -44f);
                    btn.Label.rectTransform.sizeDelta = new Vector2(btnW, 20f);
                    btn.Label.fontSize = 14f;
                    btn.Label.fontStyle = FontStyles.Bold;
                }
                btn.Button.onClick.AddListener(() => OnPingButtonClicked(capturedKind));
                _pingKindButtons[i] = btn;
            }
            _pingMuteButton = BattleUiWidgets.CreateTextButton("PingMuteBtn", _pingBarRoot,
                _hasPingBarArt ? 320f : 332f, _hasPingBarArt ? 14f : 12f,
                _hasPingBarArt ? 66f : 76f, _hasPingBarArt ? 56f : 60f,
                "屏蔽", _hasPingBarArt ? 16f : 18f,
                _hasPingBarArt ? SeasonEconomyUiArt.BattleArenaPaper : SeasonEconomyUiArt.BattleArenaDisabled,
                _hasPingBarArt ? SeasonEconomyUiArt.BattleArenaInk : QdaoUguiTheme.Cream);
            _pingMuteButton.Label.fontStyle = FontStyles.Bold;
            _pingMuteButton.Button.onClick.AddListener(OnPingMuteClicked);
            _pingArmedHint = QdaoUguiFactory.CreateText("PingArmedHint", _pingBarRoot, 0f, -26f, 420f, 24f,
                string.Empty, 17f, SeasonEconomyUiArt.BattleArenaAmber, TextAlignmentOptions.MidlineLeft);
            _pingArmedHint.fontStyle = FontStyles.Bold;
            _pingArmedHint.raycastTarget = false;
            _pingArmedHint.gameObject.SetActive(false);
            _pingBarRoot.gameObject.SetActive(false);

            // 论剑 V4 连胜势旗标（原尺寸 320×64，置于记录键右侧、倒计时环左侧，避让顶栏与胶囊安全区）
            var streakBg = BattleUiWidgets.CreatePanel("WinStreakBanner", _hudRoot, 548f, 24f, 320f, 64f,
                SeasonEconomyUiArt.BattleArenaAmber, raycastTarget: false);
            var streakSprite = SeasonEconomyUiArt.LoadRankedPlayV4Asset("streak_banner_base");
            if (streakSprite != null) SeasonEconomyUiArt.ApplyFixedControlSprite(streakBg, streakSprite, SeasonEconomyUiArt.BattleArenaAmber);
            else BattleRoundCounter.ApplyNineSlice(streakBg, "panel_9slice");
            _winStreakBanner = streakBg.rectTransform;
            _winStreakBannerText = QdaoUguiFactory.CreateText("WinStreakBannerText", _winStreakBanner,
                16f, 0f, 288f, 64f, string.Empty, 22f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            _winStreakBannerText.fontStyle = FontStyles.Bold;
            _winStreakBannerText.enableAutoSizing = true;
            _winStreakBannerText.fontSizeMin = 17f;
            _winStreakBannerText.fontSizeMax = 22f;
            _winStreakBannerText.raycastTarget = false;
            _winStreakBanner.gameObject.SetActive(false);

            BattleFidelityArt.ApplyHud(_hudRoot);
            _statusPopup = QdaoUguiFactory.CreateStretch("UnitStatusPopup", _hudRoot, Vector4.zero);
            var statusDismiss = QdaoUguiFactory.CreateHitButton("DismissStatus", _statusPopup,
                0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            statusDismiss.onClick.AddListener(HideUnitStatusPopup);
            const float cardWidth = 760f;
            const float cardHeight = 540f;
            var statusCard = BattleUiWidgets.CreatePanel("StatusCard", _statusPopup,
                (QdaoUguiTheme.DesignWidth - cardWidth) / 2f, (QdaoUguiTheme.DesignHeight - cardHeight) / 2f,
                cardWidth, cardHeight, SeasonEconomyUiArt.BattleArenaPaper, true);
            SeasonEconomyUiArt.ApplyFixedControlSprite(statusCard,
                Resources.Load<Sprite>("UI/Ugui/NewPlayReview/Controls/card"), SeasonEconomyUiArt.BattleArenaPaper);
            if (statusCard.sprite != null && statusCard.sprite.border != Vector4.zero)
                statusCard.type = Image.Type.Sliced;
            var statusHeading = BattleUiWidgets.CreatePanel("StatusHeading", statusCard.transform,
                180f, -7f, 400f, 76f, SeasonEconomyUiArt.BattleArenaJade, false);
            SeasonEconomyUiArt.ApplyFixedControlSprite(statusHeading,
                SeasonEconomyUiArt.LoadGuildFidelityAsset("guild_button_primary"), SeasonEconomyUiArt.BattleArenaJade);
            _statusPopupTitle = QdaoUguiFactory.CreateText("UnitStatusTitle", statusHeading.transform,
                42f, 2f, 316f, 70f, string.Empty, 34f, SeasonEconomyUiArt.GoldText, TextAlignmentOptions.Center);
            _statusPopupTitle.richText = false;
            _statusPopupTitle.fontStyle = FontStyles.Bold;
            _statusPopupTitle.enableAutoSizing = true;
            _statusPopupTitle.fontSizeMin = 23f;
            _statusPopupTitle.fontSizeMax = 34f;
            var statusClose = BattleUiWidgets.CreateTextButton("UnitStatusClose", statusCard.transform,
                684f, 9f, 64f, 64f, "×", 32f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            var closeArt = SeasonEconomyUiArt.LoadGuildFidelityAsset("guild_close");
            if (closeArt != null)
            {
                SeasonEconomyUiArt.ApplyFixedControlSprite(statusClose.Plate, closeArt, BattleUiStyle.ButtonPlate);
                statusClose.Label.gameObject.SetActive(false);
            }
            statusClose.Button.onClick.AddListener(HideUnitStatusPopup);

            _statusHealth = CreateStatusField(statusCard.transform, "气血", 80f);
            _statusMana = CreateStatusField(statusCard.transform, "法力", 118f);
            _statusShield = CreateStatusField(statusCard.transform, "持久护盾", 156f);
            _statusCharge = CreateStatusField(statusCard.transform, "蓄力", 194f);
            var statusTableHead = BattleUiWidgets.CreatePanel("StatusTableHead", statusCard.transform,
                24f, 244f, 712f, 44f, QdaoUguiTheme.Html("#E5D7BD"), false);
            SeasonEconomyUiArt.ApplyFixedControlSprite(statusTableHead,
                SeasonEconomyUiArt.LoadGuildFidelityAsset("guild_table_header"), QdaoUguiTheme.Html("#E5D7BD"));
            QdaoUguiFactory.CreateText("SourceHeading", statusTableHead.transform,
                19f, 0f, 231f, 44f, "状态来源", 28f, QdaoUguiTheme.Html("#5A3C26")).fontStyle = FontStyles.Bold;
            QdaoUguiFactory.CreateText("RoundsHeading", statusTableHead.transform,
                260f, 0f, 224f, 44f, "剩余回合", 28f, QdaoUguiTheme.Html("#5A3C26"), TextAlignmentOptions.Center)
                .fontStyle = FontStyles.Bold;
            QdaoUguiFactory.CreateText("EffectHeading", statusTableHead.transform,
                500f, 0f, 202f, 44f, "护盾与余次", 28f, QdaoUguiTheme.Html("#5A3C26")).fontStyle = FontStyles.Bold;
            var viewport = BattleUiWidgets.CreatePanel("StatusViewport", statusCard.transform,
                24f, 288f, 712f, 227f, Color.clear, true).rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            _statusBuffContent = QdaoUguiFactory.CreateRect("UnitStatusBody", viewport, 0f, 0f, 712f, 227f);
            _statusScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _statusScroll.viewport = viewport;
            _statusScroll.content = _statusBuffContent;
            _statusScroll.horizontal = false;
            _statusScroll.movementType = ScrollRect.MovementType.Clamped;
            _statusScroll.scrollSensitivity = 48f;
            _statusPopup.gameObject.SetActive(false);

            _skillPanel.gameObject.SetActive(false);
            _itemPanel.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }

        private static TMP_Text CreateStatusField(UnityEngine.Transform parent, string label, float y)
        {
            Color ink = QdaoUguiTheme.Html("#17382A");
            QdaoUguiFactory.CreateText(label, parent, 43f, y, 135f, 38f, label, 27f, QdaoUguiTheme.Html("#614328"));
            BattleUiWidgets.CreatePanel("StatusDivider", parent, 43f, y + 36f, 671f, 1f,
                QdaoUguiTheme.Html("#CCB794"), false);
            var value = QdaoUguiFactory.CreateText("StatusValue", parent, 195f, y, 519f, 38f, string.Empty, 28f, ink);
            value.richText = false;
            value.fontStyle = FontStyles.Bold;
            value.enableAutoSizing = true;
            value.fontSizeMin = 20f;
            value.fontSizeMax = 28f;
            return value;
        }

        /// <summary>
        /// 建一层(兄弟顺序即绘制顺序)。nestedCanvas=true 时加嵌套 Canvas(不覆盖排序,随父画布层级绘制),
        /// 该层内任何 transform 变化只触发本子画布 BuildBatch;raycaster=true 再加 GraphicRaycaster
        /// (嵌套 Canvas 下的 Graphic 只登记在自己的画布上,父画布的 raycaster 命中不到)。
        /// </summary>
        private RectTransform CreateLayer(string name, bool nestedCanvas, bool raycaster, RectTransform parent = null)
        {
            var layer = QdaoUguiFactory.CreateRect(name, parent != null ? parent : _root, 0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            if (nestedCanvas)
            {
                var canvas = layer.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = false;
                canvas.pixelPerfect = false;
                if (raycaster) layer.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            return layer;
        }

        // ── 开关 ─────────────────────────────────────────────

        public void Open(BattleStateS2C state, ulong myPlayerId)
            => OpenInternal(state, myPlayerId, spectate: false, replay: false, observerCount: 0, viewerTeamIndex: 0);

        /// <summary>
        /// 观战只读模式开屏(§10 D8 观众零写权):无本人 actor(myId=0,
        /// FindMyActor 恒空),命令环整体隐藏;_myTeam 保持 0,即 team 0
        /// 固定显示在下方(我方阵位)。回合播放复用 CoPlayTurn,Ack 与否由 BattleUiRoot 区分。
        /// </summary>
        public void OpenSpectate(BattleStateS2C state, uint observerCount)
            => OpenInternal(state, 0, spectate: true, replay: false, observerCount: observerCount, viewerTeamIndex: 0);

        /// <summary>
        /// 战报回放只读模式开屏(docs/handoff/battle-report-replay-client.md §5):
        /// 只播不发,隐藏命令环、计时环与直连横幅;展示底部回放控制条。
        /// </summary>
        public void OpenReplay(BattleStateS2C state, ulong myPlayerId, uint viewerTeamIndex, string headerInfo, string noticeText)
            => OpenReplay(state, myPlayerId, viewerTeamIndex, headerInfo, noticeText, null);

        public void OpenReplay(BattleStateS2C state, ulong myPlayerId, uint viewerTeamIndex, string headerInfo, string noticeText, BattleReplayAnalysis analysis)
        {
            _replayOverlay?.Bind(analysis);
            OpenInternal(state, myPlayerId, spectate: false, replay: true, observerCount: 0, viewerTeamIndex: viewerTeamIndex);
            if (_spectateText != null) _spectateText.text = headerInfo ?? "战报回放";
            if (!string.IsNullOrEmpty(noticeText)) SetHint(noticeText);
            _replayStatsButton?.SetInteractable(analysis != null && analysis.Actors.Count > 0);
            SetTimer(-1, 0f);
        }

        private void OpenInternal(BattleStateS2C state, ulong myPlayerId, bool spectate, bool replay, uint observerCount, uint viewerTeamIndex)
        {
            _spectate = spectate;
            _replay = replay;
            _observerCount = observerCount;
            if (spectate)
            {
                _phase = BattlePhase.None;
                _myTeam = 0;
            }
            else if (replay)
            {
                _phase = BattlePhase.None;
                _myTeam = viewerTeamIndex;
            }
            _myId = myPlayerId;
            _actingId = myPlayerId;
            _submittedActors.Clear();
            _playing = false;
            _playDone = true;
            _lastRound = 0;
            _deadlineMs = 0;
            _deadlineStartMs = 0;
            _pending = PendingKind.None;
            _selectedTargetId = 0;
            _coreTelegraphTargetIds.Clear();
            _coreProtectedGuardActorId = 0;
            _shownOutcome = eBattleOutcome.BattleOutcomeOngoing;
            _notReadySince = -1f;   // 宽限期从本局第一次观察到未就绪起算
            ApplyChannelBanner(BattleChannelBanner.Hidden);
            _root.gameObject.SetActive(true);
            ApplyTopSafety();
            _logPanel.Clear();
            _logPanel.SetVisible(false);
            _orderBar.Clear();
            ApplyState(state);
            _roundCounter.SetRound(state?.RoundIndex ?? 0, false, roundLimit: CurrentRoundLimit());
            ExitTargetMode();
            RefreshSpectateHeader();
            SetReplayControlsVisible(replay);
            _presenter.PlayEntrance(_views);
            _logPanel.Append(replay ? "回放开始" : "战斗开始");
        }

        public void Close()
        {
            AbortPlayback();
            _presenter.SpeedScale = 1f;
            _presenter.ClearTransient();
            foreach (var view in _views) view.Destroy();
            _views.Clear();
            _viewById.Clear();
            _coreTelegraphTargetIds.Clear();
            _coreProtectedGuardActorId = 0;
            _surrenderRequestPending = false;
            _surrenderButton?.SetVisible(false);
            if (_guildWarFrenzyRoot != null) _guildWarFrenzyRoot.gameObject.SetActive(false);
            if (_surrenderConfirmStrip != null) _surrenderConfirmStrip.gameObject.SetActive(false);
            if (_surrenderVoteStrip != null) _surrenderVoteStrip.gameObject.SetActive(false);
            _quickParryButton?.SetVisible(false);
            _prepState = null;
            _prepRequestPending = false;
            _prepPanelWasVisible = false;
            _prepExpiredShown = false;
            _expandedPrepTeamIds.Clear();
            ClearPrepTeamChips();
            if (_prepPanel != null)
            {
                RealtimeTween.Kill(_prepPanel.gameObject);
                _prepPanel.anchoredPosition = new Vector2(PrepPanelRect.x, -PrepPanelRect.y);
                if (_prepCanvasGroup != null) _prepCanvasGroup.alpha = 1f;
                _prepPanel.gameObject.SetActive(false);
            }
            if (_spectatorPrepBanner != null) _spectatorPrepBanner.gameObject.SetActive(false);
            HideUnitStatusPopup();
            _orderBar.Clear();
            _partyCards.Clear();
            _state = null;
            _spectate = false;
            _replay = false;
            _observerCount = 0;
            _notReadySince = -1f;
            ApplyChannelBanner(BattleChannelBanner.Hidden);
            RefreshSpectateHeader();
            _replayOverlay?.Clear();
            SetReplayControlsVisible(false);
            _root.gameObject.SetActive(false);
            BattleArtCatalog.ReleaseTransientBattleCaches();
        }

        /// <summary>Scene teardown may destroy the canvas without the normal CloseBattle path.</summary>
        public void Dispose()
        {
            _presenter.Dispose();
            foreach (var view in _views) view.Destroy();
            _views.Clear();
            _viewById.Clear();
        }

        /// <summary>观战补帧(NotifySpectateState 可重发)刷新观众数。</summary>
        public void SetObserverCount(uint count)
        {
            _observerCount = count;
            RefreshSpectateHeader();
        }

        /// <summary>只把权威预告目标和护卫 ID 映射到可见单位名牌，不参与战斗选择或判定。</summary>
        public void SetCoreLoopActorMarkers(ulong telegraphTargetId, ulong protectedGuardActorId) =>
            SetCoreLoopActorMarkers(telegraphTargetId, 0, protectedGuardActorId);

        public void SetCoreLoopActorMarkers(ulong firstTargetId, ulong secondTargetId,
            ulong protectedGuardActorId)
        {
            _coreTelegraphTargetIds.Clear();
            if (firstTargetId != 0UL) _coreTelegraphTargetIds.Add(firstTargetId);
            if (secondTargetId != 0UL) _coreTelegraphTargetIds.Add(secondTargetId);
            _coreProtectedGuardActorId = protectedGuardActorId;
            RefreshActorMarkers();
        }

        public void SetCoreLoopActorMarkers(IReadOnlyList<ulong> telegraphTargetIds, ulong protectedGuardActorId)
        {
            _coreTelegraphTargetIds.Clear();
            if (telegraphTargetIds != null)
            {
                for (int i = 0; i < telegraphTargetIds.Count; i++)
                {
                    if (telegraphTargetIds[i] != 0UL) _coreTelegraphTargetIds.Add(telegraphTargetIds[i]);
                }
            }
            _coreProtectedGuardActorId = protectedGuardActorId;
            RefreshActorMarkers();
        }

        public void SetFestivalBeastTelegraphMarker(ulong targetActorId, string label)
        {
            _festivalTelegraphTargetId = targetActorId;
            _festivalTelegraphLabel = string.IsNullOrEmpty(label) ? "重击目标" : label;
            RefreshActorMarkers();
        }

        private void RefreshActorMarkers()
        {
            foreach (var pair in _viewById)
            {
                bool festivalMarked = pair.Key != 0UL && pair.Key == _festivalTelegraphTargetId;
                bool coreMarked = pair.Key != 0UL && _coreTelegraphTargetIds.Contains(pair.Key);
                pair.Value.SetCoreMarkers(
                    festivalMarked || coreMarked,
                    pair.Key == _coreProtectedGuardActorId && _coreProtectedGuardActorId != 0UL,
                    festivalMarked ? _festivalTelegraphLabel : "重击目标");
            }
        }

        private uint CurrentRoundLimit()
        {
            if (_state != null && _state.RoundLimit > 0u) return _state.RoundLimit;
            if (_state != null && (Guild.GuildUiRoot.Instance?.IsActiveGuildWarBattle(_state.BattleId) ?? false)) return 20u;
            if (!(FestivalBeast.FestivalBeastUiRoot.Instance?.IsActiveFestivalBeastBattle ?? false)) return 0u;
            uint configured = Game.FestivalBeast.FestivalBeastClient.Instance?.Detail?.RoundLimit ?? 0u;
            return configured == 0u ? 12u : configured;
        }

        private void UpdateGuildWarFrenzyBadge(BattleStateS2C state)
        {
            if (_guildWarFrenzyRoot == null) return;
            bool isWar = state != null && (Guild.GuildUiRoot.Instance?.IsActiveGuildWarBattle(state.BattleId) ?? false);
            if (!isWar || state.RoundIndex < 12u)
            {
                _guildWarFrenzyRoot.gameObject.SetActive(false);
                return;
            }
            uint stacks = state.RoundIndex - 11u;
            uint pct = stacks * 15u;
            _guildWarFrenzyText.text = $"决胜煞气 ×{stacks} (+{pct}%伤/-{pct}%疗)";
            _guildWarFrenzyRoot.gameObject.SetActive(true);
        }

        public void OnPhaseChanged(BattlePhase phase)
        {
            _phase = phase;
            RefreshActionBar();
        }

        /// <summary>战斗屏被强制关闭(断线)/观战抢占时中止播放态:复位所有 tween、飘字、特效。</summary>
        public void AbortPlayback()
        {
            _presenter.Abort();
            _playing = false;
            _playDone = true;
            _orderBar.ResetHighlight();
        }

        // ── 权威状态应用 ─────────────────────────────────────

        public void ApplyState(BattleStateS2C state)
        {
            if (state == null) return;
            _state = state;
            if (state.ActionDeadlineMs != _deadlineMs)
            {
                _deadlineMs = state.ActionDeadlineMs;
                _deadlineStartMs = BattleUiWidgets.NowUnixMs();
            }

            // 我方 team:按本人 actor 定位;找不到时保持上次值
            foreach (var actor in state.Actors)
            {
                if (actor.ActorId == _myId) { _myTeam = actor.TeamIndex; break; }
            }

            bool structural = _views.Count != state.Actors.Count;
            if (!structural)
            {
                foreach (var actor in state.Actors)
                {
                    if (!_viewById.TryGetValue(actor.ActorId, out var existing))
                    {
                        structural = true;
                        break;
                    }
                    // 论剑 V3 战前换首发宠原地复用 actor_id，pet_id 或 pet_table_id 变化时须重建单位立绘与名牌。
                    if (actor.ActorType == eBattleActorType.BattleActorTypePet && existing.LastState != null &&
                        (existing.LastState.PetId != actor.PetId || existing.LastState.PetTableId != actor.PetTableId))
                    {
                        structural = true;
                        break;
                    }
                }
            }
            if (structural) RebuildUnits(state);
            else
            {
                foreach (var actor in state.Actors)
                {
                    if (_viewById.TryGetValue(actor.ActorId, out var view)) view.Apply(actor);
                }
            }

            _roundCounter.SetRound(state.RoundIndex, state.RoundIndex != _roundCounter.Round && _roundCounter.Round != 0,
                roundLimit: CurrentRoundLimit());
            UpdateGuildWarFrenzyBadge(state);
            _partyCards.Refresh(BattleHudLogic.PartyCardOrder(state.Actors, _myId, _myTeam, BattlePartyCards.MaxCards), ResolveTile);

            // 新回合开始:重置本地已提交标记。目标选择只对选的那一回合有效:直连断线期间服务端可能已替本人
            // 默认出手并进入下一回合,补拉到新回合时收起旧的选择,免得「确认」落到玩家没看过的回合
            if (state.RoundIndex != _lastRound)
            {
                _lastRound = state.RoundIndex;
                _submittedActors.Clear();
                ExitTargetMode();
            }
            SelectNextActorIfNeeded();
            EnsureSkillCatalog();

            // 终局:胜方存活单位做胜利姿势(一次);回放往回跳到进行中的回合时重置标记以便再次播到终局仍可触发
            if (state.Outcome == eBattleOutcome.BattleOutcomeOngoing)
            {
                _shownOutcome = eBattleOutcome.BattleOutcomeOngoing;
            }
            else if (state.Outcome != _shownOutcome)
            {
                _shownOutcome = state.Outcome;
                if (state.Outcome != eBattleOutcome.BattleOutcomeDraw)
                {
                    uint winnerTeam = state.Outcome == eBattleOutcome.BattleOutcomeSideAWin ? 0u : 1u;
                    _presenter.PlayVictory(_views, winnerTeam == _myTeam);
                }
            }

            _prepState = state.PrepState != null && state.PrepState.Active ? state.PrepState.Clone() : null;
            RefreshPendingText();
            RefreshActionBar();
            RefreshPrepUi();
            RefreshTacticalPingBar();
            RefreshWinStreakBanner(state);
            RefreshUnitTacticalPings(_owner?.Client?.CurrentTeamPings ?? state.TeamPings);
            RefreshUnitStatusPopup();
        }

        private void RebuildUnits(BattleStateS2C state)
        {
            foreach (var view in _views) view.Destroy();
            _views.Clear();
            _viewById.Clear();

            // 宝宝跟随主人的物理列和原槽位深度，换列时保留每个单位的显示尺寸。
            var placement = BattleStage.AssignAll(state.Actors, _myTeam);
            foreach (var actor in state.Actors)
            {
                if (actor == null) continue;
                bool mine = actor.TeamIndex == _myTeam;
                int slot = 0;
                int depthSlot = 0;
                if (placement.TryGetValue(actor.ActorId, out var p))
                {
                    mine = p.teamIsMine;
                    slot = p.slot;
                    depthSlot = p.depthSlot;
                }
                bool pet = BattleStage.IsPetPlacement(actor, placement);
                var view = new BattleUnitView(_owner, _stageRoot, actor.ActorId, actor.ActorId == _myId && _myId != 0,
                    mine, slot, OnUnitClicked, _plateLayer, OnUnitStatusClicked, ToggleUnitStatusPopup)
                {
                    Fx = _presenter.Fx,
                    Numbers = _presenter.Numbers,
                    Ghosts = _presenter.Ghosts,
                };
                if (pet)
                    view.SetPlacement(BattleStage.PetSlotPosition(mine, slot), BattleStage.PetSlotScale(mine, depthSlot), slot);
                else
                    view.SetPlacement(BattleStage.SlotPosition(mine, slot), BattleStage.SlotScale(mine, depthSlot), slot);
                view.Apply(actor);
                _views.Add(view);
                _viewById[actor.ActorId] = view;
            }
            RefreshActorMarkers();

            // 绘制顺序:脚底 y 升序(下方的后画,盖住上方的);宝宝脚底在主人左上方,自然被主人盖住(与视频一致)
            _views.Sort((a, b) => BattleStage.CompareDepth(a.FootPosition, b.FootPosition));
            for (int i = 0; i < _views.Count; i++) _views[i].SetSiblingIndex(i);
        }

        // ── 每帧驱动(倒计时) ────────────────────────────────

        public void Tick()
        {
            if (!IsOpen) return;
            float pulseAlpha = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5f);
            foreach (var view in _views) view.UpdateChargeBadge(pulseAlpha);
            ApplyTopSafety();
            // 直连状态没有「掉线恢复中」事件(IBattleChannel 只报就绪 / 终结),每帧轮询才能及时置灰;
            // 放在播放分支之前:回合播放中直连断开 / 恢复,横幅照样更新
            RefreshChannelState();
            if (_replay)
            {
                // 回放没有实时行动截止与倒计时(handoff §5)
                SetTimer(-1, 0f);
                return;
            }
            if (_playing)
            {
                // 播放中行动窗口已在倒数(服务端广播前就起算):计时环照常显示,玩家能看到留给自己的时间
                _phaseText.text = "回合播放中…";
                bool ongoing = _state == null || _state.Outcome == eBattleOutcome.BattleOutcomeOngoing;
                long remainMs = _deadlineMs != 0 && ongoing ? (long)_deadlineMs - BattleUiWidgets.NowUnixMs() : -1;
                SetTimer(_deadlineMs != 0 && ongoing ? System.Math.Max(0, remainMs) : -1, TimerRatio(remainMs));
                return;
            }
            if (_spectate)
            {
                // 观众没有本地相位,顶部信息完全由权威状态推导
                if (_prepState != null && _prepState.Active)
                {
                    long prepRemainMs = (long)_prepState.DeadlineMs - BattleUiWidgets.NowUnixMs();
                    float totalMs = System.Math.Max(1000f, (_prepState.DurationSeconds > 0 ? _prepState.DurationSeconds : 15u) * 1000f);
                    string specCaption = prepRemainMs > 0 ? "战前明牌变阵中…" : "等待开局…";
                    _phaseText.text = specCaption;
                    if (_spectatorPrepText != null) _spectatorPrepText.text = specCaption;
                    SetTimer(_prepState.DeadlineMs != 0 ? System.Math.Max(0, prepRemainMs) : -1,
                        prepRemainMs > 0 ? Mathf.Clamp01(prepRemainMs / totalMs) : 0f);
                }
                else if (_state != null && _state.Outcome == eBattleOutcome.BattleOutcomeOngoing)
                {
                    long remainMs = (long)_deadlineMs - BattleUiWidgets.NowUnixMs();
                    _phaseText.text = remainMs > 0
                        ? "等待玩家行动"
                        : "等待回合结算…";
                    SetTimer(_deadlineMs != 0 ? System.Math.Max(0, remainMs) : -1, TimerRatio(remainMs));
                }
                else
                {
                    _phaseText.text = "战斗已结束";
                    SetTimer(-1, 0f);
                }
                return;
            }
            if (_prepState != null && _prepState.Active)
            {
                long prepRemainMs = (long)_prepState.DeadlineMs - BattleUiWidgets.NowUnixMs();
                float totalMs = System.Math.Max(1000f, (_prepState.DurationSeconds > 0 ? _prepState.DurationSeconds : 15u) * 1000f);
                int sec = prepRemainMs > 0 ? (int)((prepRemainMs + 999L) / 1000L) : 0;
                float ratio = prepRemainMs > 0 ? Mathf.Clamp01(prepRemainMs / totalMs) : 0f;
                bool expired = _prepState.DeadlineMs != 0 && prepRemainMs <= 0;
                if (expired != _prepExpiredShown)
                {
                    _prepExpiredShown = expired;
                    RefreshPrepUi();
                }
                _phaseText.text = IsChannelFailed() ? ChannelFailedPhaseText
                    : !IsChannelReady() ? "等待战斗连接…"
                    : !expired ? "战前明牌变阵" : "等待开局…";
                SetTimer(_prepState.DeadlineMs != 0 ? System.Math.Max(0, prepRemainMs) : -1, ratio);
                UpdatePrepCountdownLabel(sec, ratio);
                return;
            }
            switch (_phase)
            {
                case BattlePhase.WaitingAction:
                {
                    long remainMs = (long)_deadlineMs - BattleUiWidgets.NowUnixMs();
                    // 已判定连不上:链路不会自己恢复,回合结果也到不了,「等待…」类文案都与横幅矛盾,排最前;
                    // 窗口已过(服务端正按默认行动结算,回合结果在路上):不再提示「请选择行动」;
                    // 直连未就绪时出手按钮是灰的,已提交的回合结果也要等直连就绪补拉才到,排在「已提交」之前
                    _phaseText.text = IsChannelFailed() ? ChannelFailedPhaseText
                        : remainMs < 0 && _deadlineMs != 0 ? "等待回合结算…"
                        : !IsChannelReady() ? "等待战斗连接…"
                        : _submitted ? "等待其他玩家…" : "请选择行动";
                    SetTimer(_deadlineMs != 0 ? System.Math.Max(0, remainMs) : -1, TimerRatio(remainMs));
                    break;
                }
                case BattlePhase.Resolving:
                {
                    // 已提交 / 已阵亡、等回合结果:连不上时结果到不了,同样不说「结算中」;
                    // 最后一回合已播完、只等结算包时说在结算战斗
                    var battle = _owner?.Client;
                    _phaseText.text = IsChannelFailed() ? ChannelFailedPhaseText
                        : battle != null && battle.IsBattleDecided ? BattleSettlingPhaseText
                        : "回合结算中…";
                    long remainMs = (long)_deadlineMs - BattleUiWidgets.NowUnixMs();
                    SetTimer(_deadlineMs != 0 ? System.Math.Max(0, remainMs) : -1, TimerRatio(remainMs));
                    break;
                }
                case BattlePhase.Ended:
                    _phaseText.text = "战斗结束";
                    SetTimer(-1, 0f);
                    break;
                default:
                    _phaseText.text = string.Empty;
                    SetTimer(-1, 0f);
                    break;
            }
        }

        /// <summary>只移动战斗顶部控件，避让设备安全区与小游戏右上胶囊；舞台和底部指令不位移。</summary>
        private void ApplyTopSafety()
        {
            var canvas = _hudRoot.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            var pixels = canvas.pixelRect;
            if (pixels.width <= 0f || pixels.height <= 0f) return;
            float insetPixels = 0f;
            if (canvas.targetDisplay == 0 && Screen.safeArea.width > 0f && Screen.safeArea.height > 0f)
                insetPixels = Mathf.Max(0f, pixels.yMax - Mathf.Min(pixels.yMax, Screen.safeArea.yMax));
#if UNITY_WEBGL && !UNITY_EDITOR
            // 微信/抖音胶囊不一定计入 Screen.safeArea；保守预留宿主原生按钮上带。
            insetPixels = Mathf.Max(insetPixels, 76f);
#endif
            float scale = Mathf.Max(0.001f, canvas.scaleFactor);
            float offset = insetPixels / scale;
            if (Mathf.Abs(offset - _safeTopOffsetDesign) < 0.5f) return;
            _safeTopOffsetDesign = offset;
            _roundCounter.Root.anchoredPosition = new Vector2(RoundCounterX, -(RoundCounterY + offset));
            _logPanel.ButtonRoot.anchoredPosition = new Vector2(LogButtonRect.x, -(LogButtonRect.y + offset));
            _orderBar.Root.anchoredPosition = new Vector2(0f, -(BattleActionOrderBar.Top + offset));
            _timerRoot.anchoredPosition = new Vector2(TimerRect.x, -(TimerRect.y + offset));
            _phaseText.rectTransform.anchoredPosition = new Vector2(40f, -(98f + offset));
            if (_spectatorPrepBanner != null)
                _spectatorPrepBanner.anchoredPosition = new Vector2(920f, -(122f + offset));
            if (_winStreakBanner != null)
                _winStreakBanner.anchoredPosition = new Vector2(548f, -(24f + offset));
            _pendingText.rectTransform.anchoredPosition = new Vector2(PendingTextRect.x,
                -(PendingTextRect.y + offset));
            float partyOffset = offset;
#if UNITY_WEBGL && !UNITY_EDITOR
            // 角色卡占右上角整列；其首卡须落在宿主胶囊下方。
            partyOffset = Mathf.Max(partyOffset, 155f);
#endif
            _partyCards.SetTopOffset(partyOffset);
        }

        private float TimerRatio(long remainMs)
        {
            long total = (long)_deadlineMs - _deadlineStartMs;
            if (total <= 0 || remainMs <= 0) return 0f;
            return Mathf.Clamp01((float)remainMs / total);
        }

        private void SetTimer(long remainMs, float ratio)
        {
            bool show = remainMs >= 0;
            _timerRoot.gameObject.SetActive(show);
            if (_timerRing != null)
            {
                _timerRing.gameObject.SetActive(show);
                _timerRing.fillAmount = ratio;
                _timerRing.color = ratio < 0.25f ? BattleUiStyle.DamageText : BattleUiStyle.WarnText;
            }
            if (_timerText != null)
            {
                _timerText.gameObject.SetActive(show);
                if (show) _timerText.text = BattleHudLogic.CountdownSeconds(remainMs).ToString();
            }
        }

        // ── 命令环交互 ──────────────────────────────────────

        private void OnCommand(BattleCommand command)
        {
            switch (command)
            {
                case BattleCommand.Attack:
                    if (!CanAct()) return;
                    ExitTargetMode();
                    _pending = PendingKind.Attack;
                    EnterTargetMode("请选择攻击目标(敌方)");
                    break;
                case BattleCommand.Spell:
                    if (!CanAct()) return;
                    if (_skillPanel.gameObject.activeSelf) { _skillPanel.gameObject.SetActive(false); return; }
                    ExitTargetMode();
                    PopulateSkillPanel();
                    _skillPanel.gameObject.SetActive(true);
                    _skillPanel.SetAsLastSibling();
                    break;
                case BattleCommand.Defend:
                    if (!CanAct()) return;
                    CloseSubPanels();
                    Submit(new BattleAction { ActionType = eBattleActionType.BattleActionDefend });
                    break;
                case BattleCommand.Item:
                    if (!CanAct()) return;
                    if (FindMyActor()?.ActorType == eBattleActorType.BattleActorTypePet) { SetHint("宝宝只能攻击、施放技能或防御"); return; }
                    if (_itemPanel.gameObject.activeSelf) { _itemPanel.gameObject.SetActive(false); return; }
                    ExitTargetMode();
                    _itemPanel.gameObject.SetActive(true);
                    _itemPanel.SetAsLastSibling();
                    break;
                case BattleCommand.Capture:
                    if (!CanAct()) return;
                    var recallPet = FindRecallablePet();
                    if (recallPet != null)
                    {
                        ExitTargetMode();
                        _pending = PendingKind.RecallPet;
                        EnterTargetMode($"召回 {ResolveName(recallPet.ActorId)} 将消耗人物本回合行动");
                        _selectedTargetId = recallPet.ActorId;
                        RefreshTargetSelection();
                        break;
                    }
                    if (!HasCaptureTarget()) return;
                    ExitTargetMode();
                    _pending = PendingKind.Capture;
                    EnterTargetMode("请选择可捕捉野怪，气血越低越容易成功");
                    break;
                case BattleCommand.Flee:
                    if (!CanAct()) return;
                    if (FindMyActor()?.ActorType == eBattleActorType.BattleActorTypePet) { SetHint("宝宝只能攻击、施放技能或防御"); return; }
                    if (!IsPveBattle()) { SetHint("PVP 战斗不可逃跑"); return; }
                    CloseSubPanels();
                    if (CoreGameplayLoopModels.IsBossTrialKind(_state?.BossTrialKind ?? 0u))
                        SetHint("确认退出试炼（100% 确定离场，不消耗任何场外资产）");
                    Submit(new BattleAction { ActionType = eBattleActionType.BattleActionFlee });
                    break;
                case BattleCommand.Auto:
                    OnAutoClicked();
                    break;
            }
        }

        private void OnAutoCommand(AutoBattleCommand command)
        {
            switch (command)
            {
                case AutoBattleCommand.Character:
                    SelectActingActor(_myId);
                    break;
                case AutoBattleCommand.Pet:
                    if (_state != null)
                        foreach (var actor in _state.Actors)
                            if (actor.ActorType == eBattleActorType.BattleActorTypePet && IsControlled(actor))
                            { SelectActingActor(actor.ActorId); break; }
                    break;
                case AutoBattleCommand.CancelAuto:
                {
                    var client = _owner?.Client;
                    if (client == null) { SetHint("战斗模块未就绪"); return; }
                    client.SetAutoBattle(false);
                    SetHint(AutoToggleHint(false, client));
                    break;
                }
            }
        }

        private void OnSkillPicked(uint skillTableId)
        {
            if (!CanAct()) return;
            var definition = Skills.SkillUiRoot.FindPresentation(skillTableId);
            bool isMirrorCopy = skillTableId == TurnPlan.MirrorCopySkillId;
            if (!isMirrorCopy && definition == null && MmorpgClient.Game.Skills.SkillBookClient.Instance != null)
            { EnsureSkillCatalog(); SetHint("技能信息正在同步，请稍后再选择"); return; }
            _skillPanel.gameObject.SetActive(false);
            _pending = PendingKind.Skill;
            _pendingSkillId = skillTableId;
            if (isMirrorCopy)
            {
                EnterTargetMode("请为【玄镜映法】选择敌方目标（首领不可复制）");
                return;
            }
            bool self = string.Equals(definition?.targetTeam, "self", System.StringComparison.OrdinalIgnoreCase);
            var name = definition?.name ?? $"技能{skillTableId}";
            EnterTargetMode(self ? $"【{name}】作用于自身，请确认行动" : $"请为【{name}】选择目标");
            // 自身技能只预选本人，复用现有确认按钮；选择技能本身不发送行动。
            if (self) OnUnitClicked(_actingId);
        }

        private void OnItemPicked()
        {
            if (!CanAct()) return;
            string raw = (_itemInput != null ? _itemInput.text : string.Empty)?.Trim();
            if (string.IsNullOrEmpty(raw) || !uint.TryParse(raw, out uint itemId) || itemId == 0)
            {
                SetHint("请输入有效的道具表ID");
                return;
            }
            _itemPanel.gameObject.SetActive(false);
            _pending = PendingKind.Item;
            _pendingItemId = itemId;
            EnterTargetMode($"请为 道具{itemId} 选择目标");
        }

        private void OnAutoClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetHint("战斗模块未就绪"); return; }
            // 切换基准取本地意愿(AutoBattleLatched)而非权威值:服务器未回执前
            // 再点一次也能撤销;按钮文案只跟 is_auto 权威回执翻转(§11.2)
            bool enable = !client.AutoBattleLatched;
            client.SetAutoBattle(enable);
            SetHint(AutoToggleHint(enable, client));
        }

        /// <summary>
        /// 自动战斗开关后的提示。直连未就绪时 BattleClient 只记意愿、不发请求也不报错
        /// (直连就绪后按记忆补发,turn-based §22 D74),提示由 UI 在这里给,免得玩家以为点了没反应。
        /// </summary>
        private static string AutoToggleHint(bool enable, BattleClient client)
        {
            if (client != null && !client.IsBattleChannelReady)
                return enable ? "已记下开启自动战斗,战斗连接建立后生效" : "已记下关闭自动战斗,战斗连接建立后生效";
            return enable ? "已请求开启自动战斗…" : "已请求关闭自动战斗…";
        }

        /// <summary>BattleClient.OnAutoStateChanged(is_auto 权威变化)驱动:命令环换模式与置灰。</summary>
        public void OnAutoStateChanged(bool isAuto)
        {
            RefreshActionBar();
        }

        // ── 目标选择 ────────────────────────────────────────

        private void OnUnitClicked(ulong actorId)
        {
            if (_armedPingKind != BattlePingKind.BattlePingUnspecified && !IsReadOnly && _owner?.Client != null)
            {
                var pingKind = _armedPingKind;
                _armedPingKind = BattlePingKind.BattlePingUnspecified;
                RefreshTacticalPingBar();
                _owner.Client.SendBattlePing(actorId, pingKind);
                return;
            }
            if (_pending == PendingKind.None)
            {
                var clickedActor = FindActor(actorId);
                bool canSwitchActing = !IsReadOnly && !_playing && _phase == BattlePhase.WaitingAction &&
                    IsControlled(clickedActor) && _actingId != actorId;
                if (canSwitchActing) SelectActingActor(actorId);
                ToggleUnitStatusPopup(actorId);
                return;
            }
            if (!CanAct()) return;
            if (!_viewById.TryGetValue(actorId, out var view)) return;
            if (!IsTargetable(view)) return;

            _selectedTargetId = actorId;
            RefreshTargetSelection();
        }

        private void ToggleUnitStatusPopup(ulong actorId)
        {
            if (_statusPopup == null || actorId == 0) return;
            if (_statusPopup.gameObject.activeSelf && _inspectedActorId == actorId)
            {
                HideUnitStatusPopup();
                return;
            }
            _inspectedActorId = actorId;
            _statusPopup.gameObject.SetActive(true);
            _statusPopup.transform.SetAsLastSibling();
            RefreshUnitStatusPopup();
            _statusScroll.verticalNormalizedPosition = 1f;
        }

        private void OnUnitStatusClicked(ulong actorId)
        {
            if (_pending == PendingKind.None) ToggleUnitStatusPopup(actorId);
            else OnUnitClicked(actorId);
        }

        private void HideUnitStatusPopup()
        {
            _inspectedActorId = 0;
            if (_statusPopup != null) _statusPopup.gameObject.SetActive(false);
        }

        private void RefreshUnitStatusPopup()
        {
            if (_statusPopup == null || !_statusPopup.gameObject.activeSelf) return;
            var actor = FindActor(_inspectedActorId);
            if (actor == null) { HideUnitStatusPopup(); return; }
            var summary = BattleUnitStatusInspector.Inspect(actor,
                skillId => Skills.SkillUiRoot.FindPresentation(skillId)?.name);
            string actorName = ResolveName(_inspectedActorId);
            _statusPopupTitle.text = $"{actorName} · 状态详情";
            _statusHealth.text = $"{actor.Attributes?.Health ?? 0UL:N0} / {actor.MaxHealth:N0}";
            _statusMana.text = $"{actor.Attributes?.Mana ?? 0UL:N0} / {actor.MaxMana:N0}";
            _statusShield.text = summary.TotalShieldCapacity.ToString("N0");
            _statusCharge.text = summary.IsCharging ? $"蓄力中 · {summary.ChargingSkillName}" : "未蓄力";
            RefreshStatusRows(summary);
        }

        private void RefreshStatusRows(BattleUnitStatusSummary summary)
        {
            const float rowHeight = 58f;
            int rowCount = Mathf.Max(summary.Buffs.Count, 1);
            while (_statusBuffRows.Count < rowCount)
            {
                int index = _statusBuffRows.Count;
                var row = BattleUiWidgets.CreatePanel("StatusBuffRow", _statusBuffContent, 0f, index * rowHeight,
                    712f, rowHeight, SeasonEconomyUiArt.BattleArenaPaper, false);
                SeasonEconomyUiArt.ApplyFixedControlSprite(row,
                    SeasonEconomyUiArt.LoadGuildFidelityAsset("guild_row_normal"), SeasonEconomyUiArt.BattleArenaPaper);
                Color ink = QdaoUguiTheme.Html("#17382A");
                var source = QdaoUguiFactory.CreateText("BuffSource", row.transform,
                    19f, 0f, 229f, rowHeight, string.Empty, 29f, ink);
                source.richText = false;
                source.fontStyle = FontStyles.Bold;
                source.enableAutoSizing = true;
                source.fontSizeMin = 21f;
                source.fontSizeMax = 29f;
                var rounds = QdaoUguiFactory.CreateText("BuffRounds", row.transform,
                    260f, 0f, 224f, rowHeight, string.Empty, 27f, ink, TextAlignmentOptions.Center);
                var effect = BattleUiWidgets.CreateWrappedText("BuffEffect", row.transform,
                    500f, 3f, 198f, rowHeight - 6f, string.Empty, 23f, ink);
                effect.alignment = TextAlignmentOptions.MidlineLeft;
                effect.enableAutoSizing = true;
                effect.fontSizeMin = 17f;
                effect.fontSizeMax = 23f;
                _statusBuffRows.Add((row.rectTransform, source, rounds, effect));
            }
            float tableHeight = 0f;
            for (int index = 0; index < _statusBuffRows.Count; index++)
            {
                var row = _statusBuffRows[index];
                row.Root.gameObject.SetActive(index < rowCount);
                if (index >= rowCount) continue;
                var buff = summary.Buffs.Count == 0 ? null : summary.Buffs[index];
                row.Source.text = buff?.SourceName ?? "当前无生效状态";
                row.Rounds.text = buff == null ? string.Empty : FormatBuffDuration(buff);
                row.Effect.text = buff == null ? string.Empty : FormatBuffEffects(buff);
                float height = Mathf.Max(rowHeight, Mathf.Ceil(row.Effect.GetPreferredValues(row.Effect.text, 198f, 0f).y) + 6f);
                row.Root.anchoredPosition = new Vector2(0f, -tableHeight);
                row.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                row.Source.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                row.Rounds.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                row.Effect.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height - 6f);
                tableHeight += height;
            }
            _statusBuffContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(_statusScroll.viewport.rect.height, tableHeight));
        }

        private const uint BattleOpGrowingAura = 25u;

        private static string GrowingModifierLabel(uint modifierStat) => modifierStat switch
        {
            5u => "伤害",
            17u => "施盾容量",
            18u => "物理直伤",
            19u => "法术直伤",
            20u => "防御",
            25u => "直接伤害",
            26u => "无视防御",
            27u => "持续伤害",
            28u => "全控抵抗",
            _ => string.Empty,
        };

        private static string FormatBuffDuration(BattleUnitBuffDetail buff, System.Func<uint, BuffTable> resolveBuff = null)
        {
            if (buff.RemainRounds > 0) return $"{buff.RemainRounds} 回合";
            var row = (resolveBuff ?? BuffTableManager.Instance.FindById)(buff.BuffTableId);
            return row != null && row.InfiniteDuration != 0 ? "至战斗结束" : "—";
        }

        private static string FormatBuffEffects(BattleUnitBuffDetail buff, System.Func<uint, BuffTable> resolveBuff = null)
        {
            var fields = new List<string>(4);
            var row = (resolveBuff ?? BuffTableManager.Instance.FindById)(buff.BuffTableId);
            uint stackLimit = row?.StackLimit ?? 0u;
            if (buff.IsShield) fields.Add($"护盾余量 {buff.RemainingCapacity:N0}");
            if (buff.RemainingHits > 0) fields.Add($"抵挡余{buff.RemainingHits}次");
            if (buff.RemainingTicks > 0)
            {
                bool isReviveMark = row != null && row.Tag.TryGetValue("Revive", out bool revive) && revive;
                fields.Add(isReviveMark ? $"回生印余{buff.RemainingTicks}次" : $"持续效果余{buff.RemainingTicks}次");
            }
            if (row != null && row.BattleOp == BattleOpGrowingAura)
            {
                // 成长光环严格使用权威 stacks（未触发为 0 层，绝不把 layer=1 误显为 1 层）。
                uint stacks = buff.RawStacks;
                if (stacks == 0u)
                {
                    fields.Add(stackLimit > 1 ? $"0/{stackLimit}层（待触发）" : "0层（待触发）");
                }
                else
                {
                    fields.Add(stackLimit > 1 ? $"×{stacks}/{stackLimit}层" : $"×{stacks}层");
                    int pct = (int)System.Math.Round(row.Ratio * stacks * 100.0, System.MidpointRounding.AwayFromZero);
                    string statLabel = GrowingModifierLabel(row.ModifierStat);
                    if (pct > 0 && !string.IsNullOrEmpty(statLabel))
                        fields.Add($"{statLabel}+{pct}%");
                }
            }
            else if (buff.Stacks > 0)
            {
                fields.Add(stackLimit > 1 ? $"×{buff.Stacks}/{stackLimit}层" : $"×{buff.Stacks}层");
            }
            return string.Join(" · ", fields);
        }

        public static string FormatUnitStatusBody(BattleActorState actor, BattleUnitStatusSummary summary,
            System.Func<uint, BuffTable> resolveBuff = null)
        {
            if (actor == null || summary == null) return string.Empty;
            var sb = new System.Text.StringBuilder(192);
            ulong hp = actor.Attributes?.Health ?? 0UL;
            ulong mp = actor.Attributes?.Mana ?? 0UL;
            sb.Append($"气血 {hp}/{actor.MaxHealth}   法力 {mp}/{actor.MaxMana}");
            sb.Append($"\n持久护盾：{summary.TotalShieldCapacity}");
            sb.Append(summary.IsCharging ? $"\n蓄力中：{summary.ChargingSkillName}" : "\n蓄力：未蓄力");
            if (summary.Buffs.Count == 0)
            {
                sb.Append("\n\n当前无生效状态");
                return sb.ToString();
            }
            sb.Append('\n');
            for (int i = 0; i < summary.Buffs.Count; i++)
            {
                var b = summary.Buffs[i];
                sb.Append($"\n• {b.SourceName}");
                string effects = FormatBuffEffects(b, resolveBuff);
                if (!string.IsNullOrEmpty(effects)) sb.Append($"  {effects}");
                if (b.RemainRounds > 0)
                {
                    sb.Append($"  剩{b.RemainRounds}回合");
                }
                else
                {
                    var row = (resolveBuff ?? BuffTableManager.Instance.FindById)(b.BuffTableId);
                    if (row != null && row.InfiniteDuration != 0) sb.Append("  至战斗结束");
                }
            }
            return sb.ToString();
        }

        private bool IsSelectingMirrorCopy => _pending == PendingKind.Skill && _pendingSkillId == TurnPlan.MirrorCopySkillId;

        private bool IsTargetable(BattleUnitView view)
        {
            if (view == null || _pending == PendingKind.None) return false;
            if (_pending == PendingKind.RecallPet) return CanRecallPet(FindActor(view.ActorId));
            if (_pending == PendingKind.Capture) return CanCapture(FindActor(view.ActorId));
            if (IsSelectingMirrorCopy && view.LastState?.IsBoss == true) return false;
            // 这里只过滤可见目标；最终施放资格和战斗规则仍由业务与服务端校验。
            var targetTeam = _pending == PendingKind.Attack || IsSelectingMirrorCopy ? "enemy" :
                _pending == PendingKind.Skill ? Skills.SkillUiRoot.FindPresentation(_pendingSkillId)?.targetTeam : null;
            bool allowDead = _pending == PendingKind.Skill &&
                Skills.SkillUiRoot.FindPresentation(_pendingSkillId)?.allowDeadTarget == true;
            return BattleTargetPresentation.AllowsTarget(targetTeam, _actingId, _myTeam,
                view.ActorId, view.TeamIndex, view.IsDead, view.Fled, allowDead);
        }

        private void RefreshTargetSelection()
        {
            if (!CanChooseAction())
            {
                ExitTargetMode();
                return;
            }
            // 同回合快照可能移除目标、使其阵亡或逃跑；旧选择不能继续确认。
            if (!_viewById.TryGetValue(_selectedTargetId, out var selected) || !IsTargetable(selected))
                _selectedTargetId = 0;
            // 临时断线只收起高亮与确认，保留本回合的选择，连接恢复后可以继续确认。
            bool ready = IsChannelReady();
            var pendingDef = _pending == PendingKind.Skill ? Skills.SkillUiRoot.FindPresentation(_pendingSkillId) : null;
            bool splashFocus = _pending == PendingKind.Skill && ready &&
                CoreGameplayLoopModels.IsSplashFocusApplicable(_state, pendingDef?.maxTargets ?? 0u);
            foreach (var view in _views)
            {
                bool targetable = IsTargetable(view);
                view.SetHighlight(!ready || !targetable ? SlotHighlight.None :
                    view.ActorId == _selectedTargetId ? SlotHighlight.Selected : SlotHighlight.Targetable);
                if (ready && IsSelectingMirrorCopy && view.TeamIndex != _myTeam && view.LastState?.IsBoss == true)
                {
                    view.SetTargetFocusTag("首领不可复制", false, showFocusIcon: false);
                }
                else if (splashFocus && targetable && view.TeamIndex != _myTeam)
                {
                    if (view.ActorId == _selectedTargetId)
                        view.SetTargetFocusTag("聚火 100%", true);
                    else if (_selectedTargetId != 0UL)
                        view.SetTargetFocusTag("溅射 60%", false);
                    else
                        view.SetTargetFocusTag(null, false);
                }
                else
                {
                    view.SetTargetFocusTag(null, false);
                }
            }
            _confirmButton.SetVisible(ready && _selectedTargetId != 0);
        }

        private void EnterTargetMode(string hint)
        {
            HideUnitStatusPopup();
            _selectedTargetId = 0;
            _targetHintText.text = hint ?? string.Empty;
            RefreshTargetSelection();
            _cancelButton.SetVisible(true);
        }

        private void ExitTargetMode()
        {
            _pending = PendingKind.None;
            _pendingSkillId = 0;
            _pendingItemId = 0;
            _selectedTargetId = 0;
            _targetHintText.text = string.Empty;
            foreach (var view in _views)
            {
                view.SetHighlight(SlotHighlight.None);
                view.SetTargetFocusTag(null, false);
            }
            _confirmButton.SetVisible(false);
            _cancelButton.SetVisible(false);
            CloseSubPanels();
        }

        private void OnConfirmClicked()
        {
            if (_pending == PendingKind.None) return;
            RefreshTargetSelection();
            if (!CanAct() || _selectedTargetId == 0) return;
            BattleAction action;
            switch (_pending)
            {
                case PendingKind.RecallPet:
                    action = new BattleAction { ActionType = eBattleActionType.BattleActionRecallPet, TargetId = _selectedTargetId };
                    break;
                case PendingKind.Capture:
                    action = new BattleAction
                    {
                        ActionType = eBattleActionType.BattleActionCapture,
                        TargetId = _selectedTargetId,
                    };
                    break;
                case PendingKind.Attack:
                    action = new BattleAction
                    {
                        ActionType = eBattleActionType.BattleActionAttack,
                        TargetId = _selectedTargetId,
                    };
                    break;
                case PendingKind.Skill:
                    action = new BattleAction
                    {
                        ActionType = eBattleActionType.BattleActionSkill,
                        SkillTableId = _pendingSkillId,
                        TargetId = _selectedTargetId,
                    };
                    break;
                case PendingKind.Item:
                    action = new BattleAction
                    {
                        ActionType = eBattleActionType.BattleActionItem,
                        ItemTableId = _pendingItemId,
                        TargetId = _selectedTargetId,
                    };
                    break;
                default:
                    return;
            }
            Submit(action);
        }

        private void Submit(BattleAction action)
        {
            var client = _owner?.Client;
            if (client == null) { SetHint("战斗模块未就绪"); return; }
            // 只有请求真的发出才算本回合已提交。本地拒绝(直连未就绪 / 相位已变)时原因已由
            // BattleClient.OnError 弹出:不置 _submitted、保留目标选择,直连就绪后可直接再点确认
            // (置了就会一直显示「等待其他玩家」、按钮全灰,玩家再也出不了手,turn-based §22 D74)
            ulong actorId = _actingId;
            uint round = _lastRound;
            ulong battleId = _state?.BattleId ?? 0;
            _submittedActors.Add(actorId);
            if (!client.SubmitAction(action, actorId, success =>
            {
                if (_state?.BattleId != battleId || _lastRound != round) return;
                if (!success) _submittedActors.Remove(actorId);
                SelectNextActorIfNeeded();
                RefreshActionBar();
            }, round))
            {
                _submittedActors.Remove(actorId);
                RefreshActionBar();
                return;
            }
            ExitTargetMode();
            SelectNextActorIfNeeded();
            RefreshActionBar();
        }

        private bool CanAct() => CanChooseAction() && IsChannelReady();

        // 回合/本人状态失效应丢弃选择；连接暂不可用不改变玩家的本回合选择意图。
        private bool CanChooseAction()
        {
            if (IsReadOnly) return false;
            // 自动战斗中禁止手动出招(is_auto 单位不进 pending,提交会被服务端拒)
            var me = FindMyActor();
            return _phase == BattlePhase.WaitingAction && !_playing && !_submitted
                   && IsControlled(me) && !me.IsAuto && !me.IsDead && !me.Fled;
        }

        /// <summary>
        /// 本局战斗直连就绪(可以出手)。没有 BattleClient(QA 截图 / 演出台直接建屏)视为就绪,
        /// 保持这些离线路径的旧行为;BattleClient 无直连通道时自身也恒为就绪。
        /// </summary>
        private bool IsChannelReady()
        {
            var client = _owner?.Client;
            return client == null || client.IsBattleChannelReady;
        }

        /// <summary>本局直连已判定连不上(恢复预算用完,只有「重新连接」能再试)。没有 BattleClient 时恒为 false。</summary>
        private bool IsChannelFailed()
        {
            var client = _owner?.Client;
            return client != null && client.IsBattleChannelFailed;
        }

        /// <summary>
        /// 每帧同步直连提示:横幅档位按 <see cref="BattleHudLogic.DecideChannelBanner"/>;
        /// 就绪值与命令环上次刷新时不同则重刷命令环(置灰 / 解灰 + 底部提示)。观战不显示
        /// (观众的直连失败由 SpectateClient 以 OnError + 回 None 体现,观战屏随之关闭)。
        /// </summary>
        private void RefreshChannelState()
        {
            if (IsReadOnly)
            {
                ApplyChannelBanner(BattleChannelBanner.Hidden);
                return;
            }
            var client = _owner?.Client;
            bool ready = client == null || client.IsBattleChannelReady;
            bool failed = client != null && client.IsBattleChannelFailed;
            float now = Time.realtimeSinceStartup;
            if (ready) _notReadySince = -1f;
            else if (_notReadySince < 0f) _notReadySince = now;

            // 已出胜负:battle 在最后一回合帧之后正常关掉直连,不再需要它,不算「回合内」——不亮连接横幅。
            bool decided = client != null && client.IsBattleDecided;
            bool inBattle = !decided && (_phase == BattlePhase.WaitingAction || _phase == BattlePhase.Resolving);
            ApplyChannelBanner(BattleHudLogic.DecideChannelBanner(inBattle, ready, failed,
                ready ? 0f : now - _notReadySince));
            if (ready != _channelReadyShown || failed != _channelFailedShown) RefreshActionBar();
        }

        private void ApplyChannelBanner(BattleChannelBanner banner)
        {
            if (_channelBanner == null || banner == _bannerShown) return;
            _bannerShown = banner;
            bool visible = banner != BattleChannelBanner.Hidden;
            _channelBanner.gameObject.SetActive(visible);
            if (visible)
            {
                bool failed = banner == BattleChannelBanner.Failed;
                _channelBannerText.text = failed ? ChannelFailedBannerText : ChannelConnectingBannerText;
                _channelBannerText.color = failed ? BattleUiStyle.DamageText : BattleUiStyle.WarnText;
            }
            _reconnectButton.SetVisible(banner == BattleChannelBanner.Failed);
        }

        private void OnReconnectClicked()
        {
            var client = _owner?.Client;
            if (client == null) return;
            client.RetryBattleChannel();
            // 手动重连是玩家刚点的:连接中横幅立即出现(不走开局宽限期),给出「点了有反应」的反馈;
            // 结果由 BattleClient 回来 —— 就绪则横幅收起,再次失败则 IsBattleChannelFailed 置回、横幅回失败档。
            // 不另弹 toast:横幅已写「正在连接战斗服务器…」,同一句话出现两处只是噪音
            _notReadySince = Time.realtimeSinceStartup - BattleHudLogic.ChannelConnectingBannerDelaySeconds;
            RefreshChannelState();
        }

        // ── 回合播放 ────────────────────────────────────────

        /// <summary>
        /// 按 TurnPlan 播放回合表现(BattlePresenter 驱动),播完全量应用 state。
        /// 由 BattleUiRoot 以协程驱动;协程被 StopCoroutine 抢占后需再调 AbortPlayback 复位表现。
        ///
        /// 演出预算:服务端在广播本回合结果前已把下一回合 action_deadline_ms 挂上(手动 25s / 全员自动 2s 起),
        /// 不等客户端 Ack。按 <see cref="PlaybackBudget"/> 用 deadline − now − 输入余量 决定倍率:
        /// 够就原速(自动 1.5x),不够就压缩到上限 6x,仍塞不下则 Skip 只落终态 —— 保证命令环在
        /// 窗口内至少留出 2.5s 给玩家手动出招,自动/观战也不再每回合被下一回合抢占。
        /// </summary>
        public IEnumerator CoPlayTurn(TurnResultS2C result)
            => CoPlayTurnInternal(result, fixedReplaySpeed: null);

        /// <summary>战报回放专用回合播放:不走实时行动截止预算压缩,严格按玩家所选倍速播放。</summary>
        public IEnumerator CoPlayReplayTurn(TurnResultS2C result, float speedScale)
            => CoPlayTurnInternal(result, fixedReplaySpeed: speedScale);

        private IEnumerator CoPlayTurnInternal(TurnResultS2C result, float? fixedReplaySpeed)
        {
            if (result == null) yield break;
            // 抢占:上一回合若还在播,先复位
            if (_presenter.IsPlaying) _presenter.Abort();

            _playing = true;
            _playDone = false;
            ExitTargetMode();
            RefreshActionBar();
            _roundCounter.SetRound(result.RoundIndex, true, fixedReplaySpeed.HasValue ? " · 回放" : " · 结算", CurrentRoundLimit());

            // 下一回合的截止时间随本回合结果一起到达:计时环从现在起算,而不是等播完 ApplyState 才起算
            if (!fixedReplaySpeed.HasValue && result.State != null && result.State.ActionDeadlineMs != _deadlineMs)
            {
                _deadlineMs = result.State.ActionDeadlineMs;
                _deadlineStartMs = BattleUiWidgets.NowUnixMs();
            }

            var plan = TurnPlan.Build(result, _myTeam);
            _presenter.PreparePlan(plan);
            var order = BattleHudLogic.ResolveActionOrder(_state?.Actors, plan.ActionOrder);
            _orderBar.SetOrder(order, ResolveTile);
            _logPanel.Append($"—— 第 {result.RoundIndex} 回合 ——");
            for (int i = 0; i < result.Events.Count; i++)
            {
                var ev = result.Events[i];
                if (ev.EventType != eBattleEventType.BattleEventSummonPet) continue;
                string summonPetName = null;
                if (result.State != null)
                {
                    for (int a = 0; a < result.State.Actors.Count; a++)
                    {
                        if (result.State.Actors[a].ActorId == ev.TargetId && !string.IsNullOrEmpty(result.State.Actors[a].Name))
                        {
                            summonPetName = result.State.Actors[a].Name;
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(summonPetName))
                {
                    summonPetName = "备选灵宠";
                }
                _logPanel.Append($"{ResolveName(ev.SourceId)} 临阵唤灵，唤出 {summonPetName}！");
            }

            if (fixedReplaySpeed.HasValue)
            {
                _presenter.SpeedScale = fixedReplaySpeed.Value;
                _presenter.Play(plan);
            }
            else
            {
                bool myAuto = !_spectate && _owner?.Client != null && _owner.Client.IsMyActorAuto;
                bool ended = result.State != null && result.State.Outcome != eBattleOutcome.BattleOutcomeOngoing;
                var budget = PlaybackBudget.Decide(plan.TotalSeconds, result.State?.ActionDeadlineMs ?? 0UL,
                    BattleUiWidgets.NowUnixMs(), passive: _spectate || myAuto,
                    baseSpeed: myAuto ? BattlePresenter.AutoBattleSpeed : 1f, unbounded: ended);
                _presenter.SpeedScale = budget.Speed;
                if (!budget.Unbounded && budget.Speed > (myAuto ? BattlePresenter.AutoBattleSpeed : 1f) + 0.01f)
                    _logPanel.Append($"(行动窗口剩 {budget.BudgetSeconds:0.0}s,演出 {plan.TotalSeconds:0.0}s → {budget.Speed:0.0}x)");
                _presenter.Play(plan);
                if (budget.Skip && _presenter.IsPlaying)
                {
                    _logPanel.Append("(行动窗口不足,跳过本回合演出,直接落终态)");
                    _presenter.Skip();
                }
            }

            while (!_playDone) yield return null;

            _orderBar.ResetHighlight();
            if (result.State != null) ApplyState(result.State);
            _roundCounter.SetRound(result.State?.RoundIndex ?? result.RoundIndex, false, roundLimit: CurrentRoundLimit());
            _playing = false;
            RefreshActionBar();
        }

        /// <summary>回放中即时切换播放倍速。</summary>
        public void SetReplaySpeed(float speed)
        {
            if (!_replay) return;
            _presenter.SpeedScale = speed;
            _replaySpeedButton?.SetText($"{speed:0.#}x");
        }

        /// <summary>同步回放控制条文案与按钮可用态。</summary>
        public void UpdateReplayHud(string phaseText, string headerInfo, string hintText, float speed,
            bool paused, int currentRound, int recordedRounds, bool finished)
        {
            if (!_replay) return;
            if (_phaseText != null) _phaseText.text = phaseText ?? string.Empty;
            if (_spectateText != null && headerInfo != null) _spectateText.text = headerInfo;
            if (hintText != null) SetHint(hintText);
            _replayPauseButton?.SetText(paused ? "继续" : "暂停");
            _replaySpeedButton?.SetText($"{speed:0.#}x");
            bool hasRounds = recordedRounds > 0;
            _replayPrevButton?.SetInteractable(hasRounds && (currentRound > 1 || finished));
            _replayPauseButton?.SetInteractable(hasRounds && !finished);
            _replayNextButton?.SetInteractable(hasRounds && currentRound < recordedRounds && !finished);
            _replaySkipButton?.SetInteractable(!finished);
            _replayJumpButton?.SetInteractable(hasRounds);
        }

        // ── 内部刷新 ────────────────────────────────────────

        private BattleTileInfo ResolveTile(ulong actorId)
        {
            var info = new BattleTileInfo { Name = ResolveName(actorId), IsSelf = actorId == _myId && _myId != 0 };
            var actor = FindActor(actorId);
            if (actor != null)
            {
                info.IsMine = actor.TeamIndex == _myTeam;
                bool monster = actor.ActorType == eBattleActorType.BattleActorTypeMonster && actor.CapturePetTableId == 0;
                info.Portrait = monster
                    ? BattleArtCatalog.LoadMonsterPortrait(actor.MonsterTableId)
                    : BattleArtCatalog.LoadPlayerPortrait(actor, playerId => AppBootstrap.Instance?.GameClient?.ResolveCharacterId(playerId));
            }
            else
            {
                info.Portrait = BattleArtCatalog.LoadPlayerPortrait(actorId);
            }
            return info;
        }

        private string ResolveName(ulong actorId)
        {
            var actor = FindActor(actorId);
            if (actor == null) return $"单位{actorId}";
            if (actor.ActorType == eBattleActorType.BattleActorTypeMonster && actor.CapturePetTableId == 0 &&
                (string.IsNullOrEmpty(actor.Name) || actor.Name == "野怪"))
            {
                var trialName = BattleArtCatalog.ResolveMonsterDisplayName(actor.MonsterTableId);
                if (!string.IsNullOrEmpty(trialName)) return trialName;
            }
            if (!string.IsNullOrEmpty(actor.Name)) return actor.Name;
            return actor.ActorType == eBattleActorType.BattleActorTypeMonster
                ? $"怪物{actor.MonsterTableId}"
                : $"玩家{actor.ActorId}";
        }

        private BattleActorState FindActor(ulong actorId)
        {
            if (_state == null) return null;
            foreach (var actor in _state.Actors)
            {
                if (actor.ActorId == actorId) return actor;
            }
            return null;
        }

        private void PopulateSkillPanel()
        {
            foreach (var button in _skillButtons)
            {
                if (button.Rect != null)
                {
                    button.Rect.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(button.Rect.gameObject);
                }
            }
            _skillButtons.Clear();

            var me = FindMyActor();
            bool isPet = me?.ActorType == eBattleActorType.BattleActorTypePet;
            string[] petTabNames = { "攻击", "障碍", "辅助" };
            for (int i = 0; i < _petSkillTabs.Length; i++)
            {
                var tab = _petSkillTabs[i];
                tab.SetVisible(isPet);
                if (!isPet) continue;
                tab.Plate.color = i == _petSkillTab
                    ? BattleUiStyle.PveSizeSelectedPlate : BattleUiStyle.ButtonPlate;
                tab.SetText(i == _petSkillTab ? "√" + petTabNames[i] : petTabNames[i]);
            }
            _petSkillLimitText.gameObject.SetActive(isPet);
            _skillViewport.anchoredPosition = new Vector2(24f, isPet ? -156f : -76f);
            _skillViewport.sizeDelta = new Vector2(852f, isPet ? 220f : 300f);
            _skillEmptyText.rectTransform.anchoredPosition = new Vector2(24f, isPet ? -178f : -70f);
            _skillEmptyText.text = isPet
                ? _petSkillTab == 0 ? "暂无可施放的攻击技能"
                    : _petSkillTab == 1 ? "暂无可施放的障碍技能" : "暂无可施放的辅助技能"
                : "暂无可施放技能";
            int index = 0;
            uint lastUsedSkillId = _owner?.Client?.GetLastUsedSkillId(_actingId) ?? 0u;
            if (me != null)
            {
                var skillIds = new List<uint>(me.SkillTableIds);
                skillIds.Sort((a, b) =>
                {
                    var defA = Skills.SkillUiRoot.FindPresentation(a);
                    var defB = Skills.SkillUiRoot.FindPresentation(b);
                    int passA = defA?.IsPassive == true ? 1 : 0;
                    int passB = defB?.IsPassive == true ? 1 : 0;
                    if (passA != passB) return passA.CompareTo(passB);
                    int group = SkillCategory(a).CompareTo(SkillCategory(b));
                    return group != 0 ? group : a.CompareTo(b);
                });
                foreach (var skillId in skillIds)
                {
                    // 列表归属仍来自战斗快照；这里只补显示元数据，不授予技能或计算战斗规则。
                    var definition = Skills.SkillUiRoot.FindPresentation(skillId);
                    bool isPassive = definition?.IsPassive == true;
                    if (isPet && !isPassive && SkillCategory(skillId) != _petSkillTab) continue;
                    me.SkillCooldownRounds.TryGetValue(skillId, out uint cooldown);
                    string blockReason = ResolveSkillBlockReason(me, cooldown, definition);
                    bool canUse = string.IsNullOrEmpty(blockReason);
                    bool isLastUsed = !isPassive && lastUsedSkillId != 0u && skillId == lastUsedSkillId;

                    int row = index / 3;
                    int col = index % 3;
                    uint captured = skillId;
                    var plateColor = isLastUsed ? BattleUiStyle.PveSizeSelectedPlate : BattleUiStyle.ButtonPlate;
                    var button = BattleUiWidgets.CreateTextButton($"Skill_{skillId}", _skillContent,
                        col * 284f, row * 100f, 270f, 88f,
                        string.Empty, 24f, plateColor, BattleUiStyle.ButtonText);
                    button.Label.gameObject.SetActive(false);
                    BattleRoundCounter.ApplyNineSlice(button.Plate, "button_9slice");
                    var iconSprite = CharacterPetArt.Skill(skillId, 64) ??
                        (definition == null ? null : QdaoUguiTheme.LoadSprite(definition.IconResource)) ??
                        SeasonV1UiArt.LoadSkillIcon(skillId);
                    var icon = QdaoUguiFactory.CreateImage("Icon", button.Rect, 10f, 14f, 60f, 60f,
                        iconSprite != null ? iconSprite : QdaoRefreshArt.Load("round_badge_taiji"));
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    if (MmorpgClient.Game.Season.SeasonModels.IsSeasonalSkill(skillId))
                        SeasonV1UiArt.CreateSeasonCornerBadge(icon.transform, "SeasonCornerBadge", 38f, 2f, 20f);
                    var skillName = QdaoUguiFactory.CreateText("Name", button.Rect, 80f, 7f, 182f, 36f,
                        definition?.name ?? $"技能{skillId}", 24f, QdaoUguiTheme.Cream);
                    skillName.raycastTarget = false;
                    skillName.textWrappingMode = TextWrappingModes.NoWrap;
                    skillName.overflowMode = TextOverflowModes.Ellipsis;
                    if (isLastUsed)
                    {
                        var lastTag = QdaoUguiFactory.CreateText("LastUsed", button.Rect, 198f, 4f, 66f, 20f,
                            "上回合", 13f, new Color(1f, 0.88f, 0.42f, 1f), TextAlignmentOptions.MidlineRight);
                        lastTag.fontStyle = FontStyles.Bold;
                        lastTag.raycastTarget = false;
                    }
                    var detail = !canUse ? blockReason :
                        definition == null ? "选择目标后施放" : $"{definition.KindLabel} · {definition.TargetLabel} · {definition.manaPercent * 100:0.#}%法力";
                    var caption = QdaoUguiFactory.CreateText("Detail", button.Rect, 80f, 45f, 182f, 30f,
                        detail, 18f, !canUse ? BattleUiStyle.WarnText : QdaoUguiTheme.Cream);
                    caption.raycastTarget = false;
                    button.SetInteractable(canUse);
                    button.Button.onClick.AddListener(() => OnSkillPicked(captured));
                    BattleFidelityArt.Button(button.Button);
                    _skillButtons.Add(button);
                    index++;
                }
            }
            _skillContent.sizeDelta = new Vector2(852f,
                Mathf.Max(isPet ? 220f : 300f, ((index + 2) / 3) * 100f));
            _skillScroll.verticalNormalizedPosition = 1f;
            _skillEmptyText.gameObject.SetActive(index == 0);
        }

        private static string ResolveSkillBlockReason(BattleActorState me, uint cooldown, Skills.SkillDefinition definition)
        {
            if (definition?.IsPassive == true) return "被动 · 条件触发";
            if (cooldown > 0u) return $"冷却 {cooldown} 回合";
            if (me == null) return string.Empty;
            if (definition != null && definition.manaPercent > 0d && me.MaxMana > 0ul)
            {
                ulong cost = (ulong)System.Math.Ceiling(me.MaxMana * definition.manaPercent);
                if ((me.Attributes?.Mana ?? 0ul) < cost) return "法力不足";
            }
            if (me.Buffs != null)
            {
                var mgr = BuffTableManager.Instance;
                for (int i = 0; i < me.Buffs.Count; i++)
                {
                    var row = mgr?.FindById(me.Buffs[i].BuffTableId);
                    if (row == null) continue;
                    switch (row.StateKind)
                    {
                        case 4u:
                        case 5u:
                        case 6u: return "受控不可施放";
                        case 1u: return "被封灵不可施放";
                        case 2u when definition != null && (definition.actionTags & 2u) != 0u: return "被封法不可施放";
                        case 3u when definition != null && (definition.actionTags & 1u) != 0u: return "被封物不可施放";
                    }
                }
            }
            return string.Empty;
        }

        private static int SkillCategory(uint skillId)
        {
            var presentation = Skills.SkillUiRoot.FindPresentation(skillId);
            return presentation?.IsObstacle == true ? 1 : presentation?.IsSupport == true ? 2 : 0;
        }

        private void CloseSubPanels()
        {
            _skillPanel.gameObject.SetActive(false);
            _itemPanel.gameObject.SetActive(false);
        }

        private void RefreshPendingText()
        {
            if (_replay)
            {
                _pendingText.text = string.Empty;
                return;
            }
            // 观战没有本地相位,只要权威 pending 列表非空就展示
            bool show = _state != null && _state.PendingActorIds.Count > 0
                        && (_spectate || _phase == BattlePhase.WaitingAction);
            if (!show)
            {
                _pendingText.text = string.Empty;
                return;
            }
            var names = new List<string>();
            foreach (var actorId in _state.PendingActorIds) names.Add(ResolveName(actorId));
            _pendingText.text = $"未行动:{string.Join("、", names)}";
        }

        private void RefreshActionBar()
        {
            if (_pending != PendingKind.None) RefreshTargetSelection();
            bool inPrep = _prepState != null && _prepState.Active;
            // 观战 / 回放只读或战前变阵期:命令环整体隐藏
            _commandRing.SetVisible(!IsReadOnly && !inPrep);
            if (IsReadOnly || inPrep)
            {
                CloseSubPanels();
                _quickParryButton?.SetVisible(false);
                _surrenderButton?.SetVisible(false);
                if (_surrenderConfirmStrip != null) _surrenderConfirmStrip.gameObject.SetActive(false);
                if (_surrenderVoteStrip != null) _surrenderVoteStrip.gameObject.SetActive(false);
                if (inPrep)
                {
                    _channelReadyShown = IsChannelReady();
                    _channelFailedShown = IsChannelFailed();
                    RefreshPrepUi();
                }
                if (_spectate || inPrep) SetHint(string.Empty);
                return;
            }

            var me = FindMyActor();
            bool myAuto = me?.IsAuto == true;
            bool alive = me != null && !me.IsDead && !me.Fled;
            // 直连未就绪时出手按钮置灰(turn-based §22 D74);记下这次看到的值,Tick 据此判断要不要重刷
            bool channelReady = IsChannelReady();
            bool channelFailed = IsChannelFailed();
            _channelReadyShown = channelReady;
            _channelFailedShown = channelFailed;
            bool canAct = CanAct();
            // 一期规则:PVP 不可逃跑(客户端以「敌方含玩家」近似判定,见 open_issues)
            bool petCommand = me?.ActorType == eBattleActorType.BattleActorTypePet;
            bool canFlee = IsPveBattle() && !petCommand;
            // 「自动」不受已提交/播放中限制:战斗内活着随时可切(Resolving 切换下回合生效)
            bool inRound = _phase == BattlePhase.WaitingAction || _phase == BattlePhase.Resolving;
            bool autoInteractable = alive && inRound;

            _commandRing.SetAutoMode(myAuto);
            _commandRing.SetState(canAct, canFlee, autoInteractable, myAuto, HasCaptureTarget(), !petCommand, FindRecallablePet() != null);
            _commandRing.SetAutoKeysInteractable(true);
            RefreshQuickParryUi(channelReady);
            RefreshSurrenderUi();
            RefreshSummonUi();

            if (me == null) SetHint(string.Empty);
            else if (me.IsDead) SetHint("你已阵亡,等待战斗结束…");
            else if (me.Fled) SetHint("你已逃离战斗…");
            else if (myAuto) SetHint("自动战斗中,点「取消自动」可关闭");
            else if (_playing) SetHint("回合播放中…");
            // 排在「已提交」之前:已提交但直连断了,回合结果也要等重连后补拉才到,连接状态更要紧
            else if (!channelReady && _phase == BattlePhase.WaitingAction)
                SetHint(channelFailed ? ChannelFailedHintText : BattleClient.BattleChannelConnectingText);
            else if (_submitted && _phase == BattlePhase.WaitingAction) SetHint("已提交行动,等待其他玩家…");
            else if (_phase == BattlePhase.WaitingAction)
                SetHint($"正在为 {ResolveName(_actingId)} 选择行动 · 点击自己的角色或宝宝切换" +
                    (petCommand ? " · 宝宝可攻击、技能、防御" : RecallWaitHint() + (canFlee ? "" : " · PVP 不可逃跑")));
            else SetHint(string.Empty);
        }

        public void OnSurrenderVoteChanged(BattleSurrenderVoteState vote)
        {
            _surrenderRequestPending = false;
            RefreshSurrenderUi();
        }

        public void OnBattlePrepChanged(BattlePrepState prep)
        {
            _prepState = prep != null && prep.Active ? prep.Clone() : null;
            if (_prepState == null) _prepRequestPending = false;
            RefreshActionBar();
            RefreshPrepUi();
            RefreshTacticalPingBar();
        }

        public void OnTeamPingsChanged(IReadOnlyList<BattleTacticalPing> pings)
        {
            RefreshUnitTacticalPings(pings);
        }

        private void OnPingButtonClicked(BattlePingKind kind)
        {
            if (IsReadOnly || _owner?.Client == null) return;
            _armedPingKind = _armedPingKind == kind ? BattlePingKind.BattlePingUnspecified : kind;
            RefreshTacticalPingBar();
        }

        private void OnPingMuteClicked()
        {
            if (_owner?.Client == null) return;
            _owner.Client.TeamPingsMuted = !_owner.Client.TeamPingsMuted;
            RefreshTacticalPingBar();
            RefreshUnitTacticalPings(_owner.Client.CurrentTeamPings);
        }

        private void RefreshTacticalPingBar()
        {
            if (_pingBarRoot == null) return;
            int myTeamPlayers = 0;
            if (_state != null)
            {
                for (int i = 0; i < _state.Actors.Count; i++)
                {
                    var a = _state.Actors[i];
                    if (a != null && a.ActorType == eBattleActorType.BattleActorTypePlayer && a.TeamIndex == _myTeam)
                        myTeamPlayers++;
                }
            }
            bool prepBlocking = _prepState != null && _prepState.Active;
            bool show = !IsReadOnly && !prepBlocking && _state != null && _state.Outcome == eBattleOutcome.BattleOutcomeOngoing && myTeamPlayers >= 2;
            _pingBarRoot.gameObject.SetActive(show);
            if (!show)
            {
                _armedPingKind = BattlePingKind.BattlePingUnspecified;
                return;
            }
            var kinds = new[]
            {
                BattlePingKind.BattlePingFocus,
                BattlePingKind.BattlePingControl,
                BattlePingKind.BattlePingProtect,
                BattlePingKind.BattlePingDispel,
            };
            for (int i = 0; i < _pingKindButtons.Length; i++)
            {
                if (_pingKindButtons[i] == null) continue;
                bool armed = _armedPingKind == kinds[i];
                _pingKindButtons[i].Plate.color = _hasPingBarArt
                    ? (armed ? new Color(0.85f, 0.56f, 0.16f, 0.45f) : Color.clear)
                    : (armed ? SeasonEconomyUiArt.BattleArenaVermilion : SeasonEconomyUiArt.BattleArenaJade);
            }
            bool muted = _owner?.Client?.TeamPingsMuted == true;
            if (_pingMuteButton != null)
            {
                _pingMuteButton.Label.text = muted ? "已静音" : "屏蔽";
                _pingMuteButton.Label.color = (_hasPingBarArt && !muted)
                    ? SeasonEconomyUiArt.BattleArenaInk
                    : QdaoUguiTheme.Cream;
                _pingMuteButton.Plate.color = muted
                    ? SeasonEconomyUiArt.BattleArenaAmber
                    : (_hasPingBarArt ? SeasonEconomyUiArt.BattleArenaPaper : SeasonEconomyUiArt.BattleArenaDisabled);
            }
            if (_pingArmedHint != null)
            {
                bool hasArmed = _armedPingKind != BattlePingKind.BattlePingUnspecified;
                _pingArmedHint.gameObject.SetActive(hasArmed);
                if (hasArmed)
                    _pingArmedHint.text = $"点击目标发送「{RankedFormatting.PingDisplayName(_armedPingKind)}」队内标点";
            }
        }

        private void RefreshUnitTacticalPings(IReadOnlyList<BattleTacticalPing> pings)
        {
            for (int i = 0; i < _views.Count; i++)
                _views[i].SetTacticalPing(BattlePingKind.BattlePingUnspecified);
            if (IsReadOnly || _owner?.Client?.TeamPingsMuted == true || pings == null) return;
            for (int i = 0; i < pings.Count; i++)
            {
                var p = pings[i];
                if (p == null || p.TargetActorId == 0) continue;
                if (_viewById.TryGetValue(p.TargetActorId, out var view))
                    view.SetTacticalPing(p.Kind);
            }
        }

        private void RefreshWinStreakBanner(BattleStateS2C state)
        {
            if (_winStreakBanner == null || _winStreakBannerText == null) return;
            bool inIntro = state != null && state.Outcome == eBattleOutcome.BattleOutcomeOngoing
                && ((state.PrepState != null && state.PrepState.Active) || state.RoundIndex <= 1u);
            if (!inIntro)
            {
                _winStreakBanner.gameObject.SetActive(false);
                return;
            }
            uint bestAlly = 0u;
            uint bestEnemy = 0u;
            for (int i = 0; i < state.Actors.Count; i++)
            {
                var a = state.Actors[i];
                if (a == null || a.ActorType != eBattleActorType.BattleActorTypePlayer || a.WinStreak < 3u) continue;
                if (a.TeamIndex == _myTeam)
                {
                    if (a.WinStreak > bestAlly) bestAlly = a.WinStreak;
                }
                else
                {
                    if (a.WinStreak > bestEnemy) bestEnemy = a.WinStreak;
                }
            }
            if (bestEnemy >= 3u)
            {
                _winStreakBannerText.text = $"敌方{RankedFormatting.WinStreakBannerText(bestEnemy)}";
                _winStreakBanner.gameObject.SetActive(true);
            }
            else if (bestAlly >= 3u)
            {
                _winStreakBannerText.text = $"己方{RankedFormatting.WinStreakBannerText(bestAlly)}";
                _winStreakBanner.gameObject.SetActive(true);
            }
            else
            {
                _winStreakBanner.gameObject.SetActive(false);
            }
        }

        private void RefreshPrepUi()
        {
            if (_prepPanel == null) return;
            bool prepActive = _prepState != null && _prepState.Active;
            if (_spectatorPrepBanner != null)
                _spectatorPrepBanner.gameObject.SetActive(_spectate && prepActive);

            bool show = !IsReadOnly && prepActive;
            if (!show)
            {
                _prepExpiredShown = false;
                if (_prepPanelWasVisible && _prepPanel.gameObject.activeSelf)
                {
                    _prepPanelWasVisible = false;
                    StartPrepExitAnimation();
                }
                else
                {
                    _prepPanelWasVisible = false;
                    _prepPanel.gameObject.SetActive(false);
                }
                return;
            }

            RealtimeTween.Kill(_prepPanel.gameObject);
            _prepPanel.anchoredPosition = new Vector2(PrepPanelRect.x, -PrepPanelRect.y);
            if (_prepCanvasGroup != null)
            {
                _prepCanvasGroup.alpha = 1f;
                _prepCanvasGroup.interactable = true;
                _prepCanvasGroup.blocksRaycasts = true;
            }
            if (!_prepPanelWasVisible)
            {
                _prepPanelWasVisible = true;
                _prepPulseOriginTime = Time.unscaledTime;
            }
            _prepPanel.gameObject.SetActive(true);
            _prepPanel.SetAsLastSibling();

            var selfState = FindPrepPlayerState(_prepState, _myId);
            uint selectedSeal = selfState?.SelectedSealId ?? 0u;
            ulong selectedPet = selfState?.SelectedPetId ?? 0ul;
            bool locked = selfState?.Ready == true;
            bool channelReady = IsChannelReady();
            long remainMs = (long)_prepState.DeadlineMs - BattleUiWidgets.NowUnixMs();
            float totalMs = System.Math.Max(1000f, (_prepState.DurationSeconds > 0 ? _prepState.DurationSeconds : 15u) * 1000f);
            int sec = remainMs > 0 ? (int)((remainMs + 999L) / 1000L) : 0;
            float ratio = remainMs > 0 ? Mathf.Clamp01(remainMs / totalMs) : 0f;
            bool expired = _prepState.DeadlineMs != 0 && remainMs <= 0;
            _prepExpiredShown = expired;
            bool canEdit = channelReady && !locked && !expired && !_prepRequestPending;

            // 1. 赛季印临场切换（至多 4 枚，d129182 深玉底米白字选中态 + 象牙纸面常态）
            var sealNormalSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("prep-seal-normal");
            var sealSelectedSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("prep-seal-selected");
            Color selectedCream = QdaoUguiTheme.Html("#FFF3D9");
            Color selectedTagGold = QdaoUguiTheme.Html("#F1E5BD");
            Color sealNormalInk = QdaoUguiTheme.Html("#172D20");
            int sealCount = _prepState.SelfAvailableSeals.Count;
            if (_prepSealEmptyText != null) _prepSealEmptyText.gameObject.SetActive(sealCount == 0);
            for (int i = 0; i < _prepSealSlots.Length; i++)
            {
                var slot = _prepSealSlots[i];
                if (i < sealCount)
                {
                    var opt = _prepState.SelfAvailableSeals[i];
                    _prepSealIds[i] = opt.SealId;
                    bool active = opt.SealId == selectedSeal;
                    SeasonEconomyUiArt.ApplyFixedControlSprite(slot.Button.Plate,
                        active ? sealSelectedSprite : sealNormalSprite,
                        active ? SeasonEconomyUiArt.BattleArenaJade : SeasonEconomyUiArt.BattleArenaPaper);
                    if (!canEdit)
                        slot.Button.Plate.color = locked ? new Color(0.88f, 0.90f, 0.87f, 0.90f) : new Color(0.82f, 0.84f, 0.82f, 0.78f);
                    var sealIcon = SeasonEconomyUiArt.LoadBattleArenaSealIcon(opt.SealId);
                    slot.Icon.sprite = sealIcon;
                    slot.Icon.gameObject.SetActive(sealIcon != null);
                    slot.FallbackPlaque.gameObject.SetActive(sealIcon == null);
                    slot.NameText.text = opt.Name;
                    slot.NameText.color = active ? selectedCream
                        : !canEdit ? SeasonEconomyUiArt.BattleArenaDisabled : sealNormalInk;
                    slot.TagText.text = PrepSealTag(opt.EffectId);
                    slot.TagText.rectTransform.anchoredPosition = new Vector2(active ? 236f : 272f, -19f);
                    slot.TagText.rectTransform.sizeDelta = new Vector2(active ? 116f : 136f, 26f);
                    slot.TagText.color = active ? selectedTagGold
                        : !canEdit ? SeasonEconomyUiArt.BattleArenaDisabled : PrepSealTagColor(opt.EffectId);
                    if (slot.SelectedStamp != null) slot.SelectedStamp.gameObject.SetActive(active);
                    slot.Button.SetInteractable(canEdit && !active);
                    slot.Button.SetVisible(true);
                }
                else
                {
                    _prepSealIds[i] = 0u;
                    slot.Button.SetVisible(false);
                }
            }

            // 2. 首发灵宠临场切换（至多 6 只，d129182 深玉底米白字选中态；缺头像显示中性「灵」玉符，严禁借别宠肖像）
            var petNormalSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("prep-pet-normal");
            var petSelectedSprite = SeasonEconomyUiArt.LoadRankedV3PrepControl("prep-pet-selected");
            Color petSelectedName = QdaoUguiTheme.Html("#FFF4DC");
            Color petSelectedLvl = QdaoUguiTheme.Html("#EADFBE");
            Color petSelectedStats = QdaoUguiTheme.Html("#FFF1CD");
            Color petNormalName = QdaoUguiTheme.Html("#173522");
            Color petNormalLvl = QdaoUguiTheme.Html("#765C3E");
            Color petNormalStats = QdaoUguiTheme.Html("#182C20");
            int petCount = _prepState.SelfAvailablePets.Count;
            bool showPetButtons = petCount > 1 || (petCount == 1 && selectedPet == 0ul);
            if (_prepPetEmptyText != null) _prepPetEmptyText.gameObject.SetActive(!showPetButtons);
            for (int i = 0; i < _prepPetSlots.Length; i++)
            {
                var slot = _prepPetSlots[i];
                if (showPetButtons && i < petCount)
                {
                    var opt = _prepState.SelfAvailablePets[i];
                    _prepPetIds[i] = opt.PetId;
                    bool active = opt.PetId == selectedPet;
                    SeasonEconomyUiArt.ApplyFixedControlSprite(slot.Button.Plate,
                        active ? petSelectedSprite : petNormalSprite,
                        active ? SeasonEconomyUiArt.BattleArenaJade : SeasonEconomyUiArt.BattleArenaPaper);
                    if (!canEdit)
                        slot.Button.Plate.color = locked ? new Color(0.88f, 0.90f, 0.87f, 0.90f) : new Color(0.82f, 0.84f, 0.82f, 0.78f);
                    var petEntry = QdaoPetCatalog.Resolve(opt.PetTableId);
                    var portrait = petEntry != null ? QdaoPetCatalog.LoadPortrait(petEntry.id) : null;
                    slot.Portrait.sprite = portrait;
                    slot.Portrait.gameObject.SetActive(portrait != null);
                    slot.FallbackPlaque.gameObject.SetActive(portrait == null);
                    slot.NameText.text = opt.PetName;
                    slot.NameText.color = active ? petSelectedName
                        : !canEdit ? SeasonEconomyUiArt.BattleArenaDisabled : petNormalName;
                    slot.LevelText.text = $"Lv.{opt.Level}";
                    slot.LevelText.color = active ? petSelectedLvl : petNormalLvl;
                    slot.StatsText.text = $"速 {opt.Speed}  血 {opt.MaxHealth}";
                    slot.StatsText.color = active ? petSelectedStats
                        : !canEdit ? SeasonEconomyUiArt.BattleArenaDisabled : petNormalStats;
                    if (slot.SelectedCheck != null) slot.SelectedCheck.gameObject.SetActive(active);
                    slot.Button.SetInteractable(canEdit && !active);
                    slot.Button.SetVisible(true);
                }
                else
                {
                    _prepPetIds[i] = 0ul;
                    slot.Button.SetVisible(false);
                }
            }

            // 3. 敌方公开初阵速览（仅读公开初阵单位，绝不推测或读取敌方变阵选择）
            if (_prepEnemySummaryText != null)
            {
                var enemies = new List<string>();
                if (_state != null)
                {
                    foreach (var actor in _state.Actors)
                    {
                        if (actor == null || actor.TeamIndex == _myTeam) continue;
                        string roleTag = actor.ActorType == eBattleActorType.BattleActorTypePet ? "[宠]" : "[人]";
                        enemies.Add($"{roleTag}{actor.Name} Lv.{actor.Level}");
                    }
                }
                _prepEnemySummaryText.text = enemies.Count > 0 ? string.Join("  ·  ", enemies) : "初阵信息待同步";
            }

            // 4. 己方同队横向协同条 + 锁定统计竖丸（仅统计 TeamStates）
            RebuildPrepTeamChips();

            // 5. 状态提示、三态帮会锁定主按钮与倒计时
            if (_prepLockHintText != null)
            {
                if (!channelReady)
                {
                    _prepLockHintText.text = "等待战斗连接…";
                    _prepLockHintText.color = QdaoUguiTheme.Html("#A73E2B");
                }
                else if (locked)
                {
                    _prepLockHintText.text = "已锁定，本场选择不可再改";
                    _prepLockHintText.color = QdaoUguiTheme.Html("#3B7045");
                }
                else if (expired)
                {
                    _prepLockHintText.text = "变阵已截止，等待开局…";
                    _prepLockHintText.color = SeasonEconomyUiArt.BattleArenaAmber;
                }
                else if (_prepRequestPending)
                {
                    _prepLockHintText.text = "选择同步中…";
                    _prepLockHintText.color = QdaoUguiTheme.Html("#235E42");
                }
                else
                {
                    _prepLockHintText.text = "一点易印换宠，锁定后不可更改";
                    _prepLockHintText.color = QdaoUguiTheme.Html("#735A36");
                }
            }
            if (_prepLockButton != null)
            {
                var lockSprite = locked
                    ? SeasonEconomyUiArt.LoadRankedV3PrepControl("lock-button-locked")
                    : canEdit
                        ? SeasonEconomyUiArt.LoadRankedV3PrepControl("lock-button-normal")
                        : SeasonEconomyUiArt.LoadRankedV3PrepControl("lock-button-disabled");
                if (lockSprite != null)
                    SeasonEconomyUiArt.ApplyFixedControlSprite(_prepLockButton.Plate, lockSprite, SeasonEconomyUiArt.BattleArenaJade);
                _prepLockButton.SetText(locked ? "已锁定 · 等待对手…" : _prepRequestPending ? "正在锁定…" : "锁定出战");
                _prepLockButton.Label.fontSize = locked ? 24f : 32f;
                _prepLockButton.Label.color = locked || canEdit ? QdaoUguiTheme.Html("#FFF3D4") : QdaoUguiTheme.Html("#F2EFE6");
                _prepLockButton.SetInteractable(canEdit);
            }
            UpdatePrepCountdownLabel(sec, ratio);
        }

        private void StartPrepExitAnimation()
        {
            if (_prepPanel == null) return;
            RealtimeTween.Kill(_prepPanel.gameObject);
            if (_prepCanvasGroup != null)
            {
                _prepCanvasGroup.interactable = false;
                _prepCanvasGroup.blocksRaycasts = false;
            }
            float startY = -PrepPanelRect.y;
            RealtimeTween.To(0f, 1f, 0.2f).SetEase(RealtimeEase.QuadIn)
                .SetIgnoreEngineTimeScale(true).SetTarget(_prepPanel.gameObject)
                .OnUpdate((RealtimeTweenCallback1)(t =>
                {
                    if (_prepPanel == null) return;
                    float k = t.Value.x;
                    _prepPanel.anchoredPosition = new Vector2(PrepPanelRect.x, startY - 28f * k);
                    if (_prepCanvasGroup != null) _prepCanvasGroup.alpha = 1f - k;
                }))
                .OnComplete((RealtimeTweenCallback)(() =>
                {
                    if (_prepPanel == null) return;
                    _prepPanel.anchoredPosition = new Vector2(PrepPanelRect.x, startY);
                    if (_prepCanvasGroup != null) _prepCanvasGroup.alpha = 1f;
                    _prepPanel.gameObject.SetActive(false);
                }));
        }

        private void ClearPrepTeamChips()
        {
            for (int i = 0; i < _prepTeamChips.Count; i++)
            {
                if (_prepTeamChips[i] != null) UnityEngine.Object.Destroy(_prepTeamChips[i]);
            }
            _prepTeamChips.Clear();
        }

        private void RebuildPrepTeamChips()
        {
            ClearPrepTeamChips();
            if (_prepState == null || _prepTeamContent == null) return;

            var kaiFont = QdaoUguiTheme.ResolveFont();
            Color readyDotColor = QdaoUguiTheme.Html("#47895B");
            Color idleDotColor = QdaoUguiTheme.Html("#E3D6B7");
            Color chipReadyDot = QdaoUguiTheme.Html("#438D55");
            Color chipAdjustDot = QdaoUguiTheme.Html("#B18A45");
            Color dividerColor = QdaoUguiTheme.Html("#D3C19B");

            int readyCount = 0;
            int total = _prepState.TeamStates.Count;
            float cursorX = 0f;
            for (int i = 0; i < total; i++)
            {
                var ps = _prepState.TeamStates[i];
                if (ps.Ready) readyCount++;
                if (i < _prepReadyDots.Length && _prepReadyDots[i] != null)
                {
                    _prepReadyDots[i].gameObject.SetActive(true);
                    _prepReadyDots[i].color = ps.Ready ? readyDotColor : idleDotColor;
                }

                ulong pid = ps.PlayerId;
                bool expanded = _expandedPrepTeamIds.Contains(pid);
                float chipW = expanded ? 430f : 264f;
                var chipBg = BattleUiWidgets.CreatePanel($"PrepTeamChip_{i}", _prepTeamContent, cursorX, 2f, chipW, 48f,
                    expanded ? new Color(0.90f, 0.92f, 0.84f, 0.55f) : new Color(0f, 0f, 0f, 0.001f), raycastTarget: true);
                var chipBtn = chipBg.gameObject.AddComponent<UnityEngine.UI.Button>();
                chipBtn.transition = UnityEngine.UI.Selectable.Transition.None;
                chipBtn.onClick.AddListener(() =>
                {
                    if (!_expandedPrepTeamIds.Add(pid)) _expandedPrepTeamIds.Remove(pid);
                    RebuildPrepTeamChips();
                });

                if (i > 0)
                {
                    var leftBar = QdaoUguiFactory.CreateImage("Div", chipBg.transform, 0f, 4f, 1f, 40f, null, false);
                    leftBar.color = dividerColor;
                }
                float padLeft = i > 0 ? 12f : 2f;
                var dot = QdaoUguiFactory.CreateImage("Dot", chipBg.transform, padLeft, 8f, 10f, 10f, null, false);
                dot.color = ps.Ready ? chipReadyDot : chipAdjustDot;
                string who = pid == _myId ? "自己" : ResolveName(pid);
                string sealName = ResolvePrepSealName(_prepState, ps.SelectedSealId);
                string petName = string.IsNullOrEmpty(ps.SelectedPetName) ? "未出战灵宠" : ps.SelectedPetName;
                string stateLabel = ps.Ready ? "已锁" : "调整中";

                var topLine = QdaoUguiFactory.CreateText("Top", chipBg.transform, padLeft + 16f, 0f, chipW - padLeft - 88f, 24f,
                    who, 22f, QdaoUguiTheme.Html("#142F21"), TextAlignmentOptions.MidlineLeft);
                topLine.font = kaiFont;
                topLine.fontStyle = FontStyles.Bold;
                topLine.fontWeight = FontWeight.Bold;
                topLine.overflowMode = expanded ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
                topLine.raycastTarget = false;

                var stateTxt = QdaoUguiFactory.CreateText("State", chipBg.transform, chipW - 72f, 0f, 66f, 24f,
                    stateLabel, 20f, ps.Ready ? QdaoUguiTheme.Html("#3B7045") : QdaoUguiTheme.Html("#8B672C"), TextAlignmentOptions.MidlineRight);
                stateTxt.font = kaiFont;
                stateTxt.raycastTarget = false;

                var buildTxt = QdaoUguiFactory.CreateText("Build", chipBg.transform, padLeft, 24f, chipW - padLeft - 6f, 22f,
                    $"{sealName} · {petName}", 20f, QdaoUguiTheme.Html("#6C593A"), TextAlignmentOptions.MidlineLeft);
                buildTxt.overflowMode = expanded ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
                buildTxt.raycastTarget = false;

                _prepTeamChips.Add(chipBg.gameObject);
                cursorX += chipW + 12f;
            }
            for (int i = total; i < _prepReadyDots.Length; i++)
            {
                if (_prepReadyDots[i] != null) _prepReadyDots[i].gameObject.SetActive(false);
            }
            _prepTeamContent.sizeDelta = new Vector2(Mathf.Max(1380f, cursorX), 52f);
            if (_prepTeamReadyText != null)
                _prepTeamReadyText.text = total > 0 ? $"本队已锁定 {readyCount}/{total}" : "队伍信息待同步";
        }

        private void UpdatePrepCountdownLabel(int remainSeconds, float ratio)
        {
            if (_prepCountdownText == null || _prepState == null) return;
            _prepCountdownText.text = $"{remainSeconds} 秒";
            bool warn = remainSeconds > 0 && remainSeconds <= 5;
            _prepCountdownText.color = warn ? QdaoUguiTheme.Html("#A73E2B")
                : remainSeconds == 0 ? SeasonEconomyUiArt.BattleArenaAmber
                : QdaoUguiTheme.Html("#235E42");
            if (warn)
            {
                float wave = 0.5f - 0.5f * Mathf.Cos((Time.unscaledTime - _prepPulseOriginTime) * (2f * Mathf.PI / 0.8f));
                float scale = 1f + 0.035f * wave;
                _prepCountdownText.rectTransform.localScale = new UnityEngine.Vector3(scale, scale, 1f);
            }
            else
            {
                _prepCountdownText.rectTransform.localScale = UnityEngine.Vector3.one;
            }
            if (_prepCountdownFill != null)
            {
                _prepCountdownFill.sizeDelta = new Vector2(126f * Mathf.Clamp01(ratio), _prepCountdownFill.sizeDelta.y);
                if (_prepCountdownFillImg != null)
                    _prepCountdownFillImg.color = warn ? QdaoUguiTheme.Html("#B45636") : QdaoUguiTheme.Html("#528567");
            }
        }

        private void OnPrepSealClicked(int slotIdx)
        {
            if (slotIdx < 0 || slotIdx >= _prepSealIds.Length) return;
            uint sealId = _prepSealIds[slotIdx];
            if (sealId == 0u) return;
            SendPrepUpdate(sealId, 0ul, false);
        }

        private void OnPrepPetClicked(int slotIdx)
        {
            if (slotIdx < 0 || slotIdx >= _prepPetIds.Length) return;
            ulong petId = _prepPetIds[slotIdx];
            if (petId == 0ul) return;
            SendPrepUpdate(0u, petId, false);
        }

        private void OnPrepLockClicked() => SendPrepUpdate(0u, 0ul, true);

        private void SendPrepUpdate(uint sealId, ulong petId, bool ready)
        {
            if (_prepRequestPending || _owner?.Client == null) return;
            ulong battleId = _state?.BattleId ?? 0ul;
            _prepRequestPending = true;
            RefreshPrepUi();
            _owner.Client.UpdateBattlePrep(sealId, petId, ready, _ =>
            {
                if (_state?.BattleId != battleId) return;
                _prepRequestPending = false;
                RefreshPrepUi();
            });
        }

        private static BattlePrepPlayerState FindPrepPlayerState(BattlePrepState prep, ulong playerId)
        {
            if (prep == null || playerId == 0ul) return null;
            for (int i = 0; i < prep.TeamStates.Count; i++)
            {
                if (prep.TeamStates[i].PlayerId == playerId) return prep.TeamStates[i];
            }
            return null;
        }

        private static string ResolvePrepSealName(BattlePrepState prep, uint sealId)
        {
            if (sealId == 0u) return "无印";
            if (prep != null)
            {
                for (int i = 0; i < prep.SelfAvailableSeals.Count; i++)
                {
                    if (prep.SelfAvailableSeals[i].SealId == sealId) return prep.SelfAvailableSeals[i].Name;
                }
            }
            return sealId switch
            {
                101u => "破阵锐印",
                102u => "破阵玄印",
                103u => "固阵守印",
                104u => "聚息援印",
                _ => $"印#{sealId}",
            };
        }

        private static string PrepSealTag(uint effectId) => effectId switch
        {
            1u => "物攻破盾",
            2u => "法攻破盾",
            6u => "防开局秒",
            7u => "防御回蓝",
            _ => string.Empty,
        };

        private static Color PrepSealTagColor(uint effectId) => effectId switch
        {
            1u => QdaoUguiTheme.Html("#8B5036"),
            2u => QdaoUguiTheme.Html("#565581"),
            6u => QdaoUguiTheme.Html("#436C45"),
            7u => QdaoUguiTheme.Html("#376F68"),
            _ => QdaoUguiTheme.Html("#6E5134"),
        };

        private void RefreshQuickParryUi(bool channelReady)
        {
            if (_quickParryButton == null) return;
            if (IsReadOnly || _playing || _phase != BattlePhase.WaitingAction || !channelReady || _state == null)
            {
                _quickParryButton.SetVisible(false);
                return;
            }
            var rawIds = CoreGameplayLoopModels.CollectQuickParryDefendActorIds(_myId, _state);
            if (rawIds == null || rawIds.Count == 0)
            {
                _quickParryButton.SetVisible(false);
                return;
            }
            int pendingParry = 0;
            for (int i = 0; i < rawIds.Count; i++)
            {
                if (!IsSubmitted(rawIds[i])) pendingParry++;
            }
            if (pendingParry == 0)
            {
                _quickParryButton.SetVisible(false);
                return;
            }
            _quickParryButton.SetVisible(true);
            _quickParryButton.SetInteractable(true);
            _quickParryButton.SetText(pendingParry > 1 ? $"⚠ 点名预警 · 一键化劲({pendingParry})" : "⚠ 点名预警 · 一键化劲防御");
        }

        private void OnQuickParryClicked()
        {
            if (IsReadOnly || _playing || _phase != BattlePhase.WaitingAction || _state == null) return;
            var client = _owner?.Client;
            var rawIds = CoreGameplayLoopModels.CollectQuickParryDefendActorIds(_myId, _state);
            if (client == null || rawIds == null || rawIds.Count == 0)
            {
                RefreshQuickParryUi(IsChannelReady());
                return;
            }
            ulong battleId = _state.BattleId;
            uint round = _state.RoundIndex;
            bool dispatched = false;
            for (int i = 0; i < rawIds.Count; i++)
            {
                ulong actorId = rawIds[i];
                if (actorId == 0 || IsSubmitted(actorId)) continue;
                _submittedActors.Add(actorId);
                if (!client.SubmitQuickParryDefend(actorId, success =>
                {
                    if (_state?.BattleId != battleId || _lastRound != round) return;
                    if (!success) _submittedActors.Remove(actorId);
                    SelectNextActorIfNeeded();
                    RefreshActionBar();
                }))
                {
                    _submittedActors.Remove(actorId);
                }
                else
                {
                    dispatched = true;
                }
            }
            if (dispatched)
            {
                ExitTargetMode();
                SelectNextActorIfNeeded();
            }
            RefreshActionBar();
        }

        private void RefreshSurrenderUi()
        {
            if (_surrenderButton == null) return;
            if (IsReadOnly || _state == null || _state.SurrenderUnlockRound == 0u ||
                _phase == BattlePhase.Ended || _phase == BattlePhase.None)
            {
                _surrenderButton.SetVisible(false);
                if (_surrenderConfirmStrip != null) _surrenderConfirmStrip.gameObject.SetActive(false);
                if (_surrenderVoteStrip != null) _surrenderVoteStrip.gameObject.SetActive(false);
                return;
            }

            _surrenderButton.SetVisible(true);
            var client = _owner?.Client;
            var vote = client?.CurrentSurrenderVote;
            if (vote != null && vote.Active)
            {
                _surrenderButton.SetText("投降 · 表决中");
                _surrenderButton.SetInteractable(false);
                _surrenderButton.Label.color = SeasonEconomyUiArt.BattleArenaDisabled;
                if (_surrenderConfirmStrip != null) _surrenderConfirmStrip.gameObject.SetActive(false);
                if (_surrenderVoteStrip != null)
                {
                    _surrenderVoteStrip.gameObject.SetActive(true);
                    bool alreadyAgreed = _myId != 0 && vote.AgreedPlayerIds.Contains(_myId);
                    if (_surrenderVoteText != null)
                        _surrenderVoteText.text = alreadyAgreed
                            ? $"已同意投降（{vote.AgreedPlayerIds.Count}/{vote.RequiredAgrees}），等待队友表决…"
                            : $"队友发起投降表决 · 已同意 {vote.AgreedPlayerIds.Count}/{vote.RequiredAgrees}\n连续 2 回合未手动队友不计入票数 · 本轮仍可下指令";
                    _surrenderAgreeButton?.SetInteractable(!alreadyAgreed && !_surrenderRequestPending);
                    _surrenderRejectButton?.SetInteractable(!alreadyAgreed && !_surrenderRequestPending);
                }
                return;
            }

            if (_surrenderVoteStrip != null) _surrenderVoteStrip.gameObject.SetActive(false);
            uint round = _state.RoundIndex;
            uint unlock = _state.SurrenderUnlockRound;
            uint cooldownRound = vote != null ? vote.NextAllowedRound : 0u;
            bool canSurrender = client != null && client.CanSurrenderNow && !_surrenderRequestPending;

            if (round < unlock)
            {
                _surrenderButton.SetText($"R{unlock} 解锁投降");
                _surrenderButton.SetInteractable(false);
                _surrenderButton.Label.color = SeasonEconomyUiArt.BattleArenaDisabled;
            }
            else if (cooldownRound > round)
            {
                _surrenderButton.SetText($"冷却中(R{cooldownRound})");
                _surrenderButton.SetInteractable(false);
                _surrenderButton.Label.color = SeasonEconomyUiArt.BattleArenaDisabled;
            }
            else
            {
                _surrenderButton.SetText("认输 / 投降");
                _surrenderButton.SetInteractable(canSurrender);
                _surrenderButton.Label.color = canSurrender ? SeasonEconomyUiArt.BattleArenaInk : SeasonEconomyUiArt.BattleArenaDisabled;
            }
            if (!canSurrender && _surrenderConfirmStrip != null)
                _surrenderConfirmStrip.gameObject.SetActive(false);
        }

        private void RefreshSummonUi()
        {
            if (_summonButton == null) return;
            ulong battleId = _state?.BattleId ?? 0UL;
            if (battleId != _summonTrackedBattleId)
            {
                _summonTrackedBattleId = battleId;
                _summonEverAvailable = false;
                _summonSelectedPetId = 0UL;
                _summonRequestPending = false;
                if (_summonDrawer != null) _summonDrawer.gameObject.SetActive(false);
            }

            bool inPrep = _prepState != null && _prepState.Active;
            if (IsReadOnly || _state == null || _state.SummonPetUnlockRound == 0u || inPrep
                || _phase == BattlePhase.Ended || _phase == BattlePhase.None)
            {
                _summonButton.SetVisible(false);
                if (_summonDrawer != null) _summonDrawer.gameObject.SetActive(false);
                return;
            }

            if (_state.SelfSummonPetRemaining > 0u && _state.SelfStandbyPets.Count > 0)
                _summonEverAvailable = true;

            if (!_summonEverAvailable)
            {
                _summonButton.SetVisible(false);
                if (_summonDrawer != null) _summonDrawer.gameObject.SetActive(false);
                return;
            }

            var client = _owner?.Client;
            var myHero = FindActor(_myId);
            bool myHeroOperable = myHero != null && !myHero.IsDead && !myHero.Fled && !myHero.IsAuto
                && !_submittedActors.Contains(_myId) && !_playing && _phase == BattlePhase.WaitingAction;
            bool remaining = _state.SelfSummonPetRemaining > 0u && _state.SelfStandbyPets.Count > 0;
            bool unlocked = _state.RoundIndex >= _state.SummonPetUnlockRound;
            bool canSummon = client != null && client.CanSummonStandbyPetNow && myHeroOperable && !_summonRequestPending;

            _summonButton.SetVisible(true);
            var btnSprite = SeasonEconomyUiArt.LoadRankedV4TacticalControl(
                canSummon ? "summon-btn-normal" : "summon-btn-disabled");
            if (btnSprite != null)
                SeasonEconomyUiArt.ApplyFixedControlSprite(_summonButton.Plate, btnSprite,
                    canSummon ? SeasonEconomyUiArt.BattleArenaPaper : SeasonEconomyUiArt.BattleArenaDisabled);

            if (!remaining)
                _summonButton.SetText("唤灵已用 0/1");
            else if (!unlocked)
                _summonButton.SetText($"R{_state.SummonPetUnlockRound} 解锁唤灵");
            else if (_summonRequestPending)
                _summonButton.SetText("正在唤灵…");
            else
                _summonButton.SetText("临阵唤灵 1/1");

            _summonButton.Label.color = canSummon
                ? SeasonEconomyUiArt.BattleArenaInk
                : QdaoUguiTheme.Html("#F2EFE6");
            _summonButton.SetInteractable(canSummon);

            if (!canSummon && _summonDrawer != null)
                _summonDrawer.gameObject.SetActive(false);
            else if (_summonDrawer != null && _summonDrawer.gameObject.activeSelf)
                RefreshSummonDrawerCards();
        }

        private void OnSummonButtonClicked()
        {
            if (_summonDrawer == null || _state == null || _owner?.Client == null || !_owner.Client.CanSummonStandbyPetNow)
                return;
            bool show = !_summonDrawer.gameObject.activeSelf;
            if (show)
            {
                _skillPanel.gameObject.SetActive(false);
                _itemPanel.gameObject.SetActive(false);
                if (_surrenderConfirmStrip != null) _surrenderConfirmStrip.gameObject.SetActive(false);
                if (_summonSelectedPetId == 0UL && _state.SelfStandbyPets.Count > 0)
                    _summonSelectedPetId = _state.SelfStandbyPets[0].PetId;
                RefreshSummonDrawerCards();
            }
            _summonDrawer.gameObject.SetActive(show);
        }

        private void OnSummonPetCardClicked(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _summonPetIds.Length) return;
            ulong petId = _summonPetIds[slotIndex];
            if (petId == 0UL) return;
            _summonSelectedPetId = petId;
            RefreshSummonDrawerCards();
        }

        private void RefreshSummonDrawerCards()
        {
            if (_state == null) return;
            var normalSprite = SeasonEconomyUiArt.LoadRankedV4TacticalControl("summon-pet-card-normal");
            var selectedSprite = SeasonEconomyUiArt.LoadRankedV4TacticalControl("summon-pet-card-selected");
            Color selName = QdaoUguiTheme.Html("#FFF4DC");
            Color selStats = QdaoUguiTheme.Html("#FFF1CD");
            Color normName = QdaoUguiTheme.Html("#173522");
            Color normStats = QdaoUguiTheme.Html("#765C3E");

            bool selectedStillValid = false;
            int count = Mathf.Min(_summonPetSlots.Length, _state.SelfStandbyPets.Count);
            for (int i = 0; i < count; i++)
            {
                if (_state.SelfStandbyPets[i].PetId == _summonSelectedPetId)
                {
                    selectedStillValid = true;
                    break;
                }
            }
            if (!selectedStillValid)
                _summonSelectedPetId = count > 0 ? _state.SelfStandbyPets[0].PetId : 0UL;

            for (int i = 0; i < _summonPetSlots.Length; i++)
            {
                var slot = _summonPetSlots[i];
                if (i < count)
                {
                    var opt = _state.SelfStandbyPets[i];
                    _summonPetIds[i] = opt.PetId;
                    bool selected = opt.PetId == _summonSelectedPetId;
                    SeasonEconomyUiArt.ApplyFixedControlSprite(slot.Button.Plate,
                        selected ? selectedSprite : normalSprite,
                        selected ? SeasonEconomyUiArt.BattleArenaJade : SeasonEconomyUiArt.BattleArenaPaper);
                    var petEntry = QdaoPetCatalog.Resolve(opt.PetTableId);
                    var portrait = petEntry != null ? QdaoPetCatalog.LoadPortrait(petEntry.id) : null;
                    slot.Portrait.sprite = portrait;
                    slot.Portrait.gameObject.SetActive(portrait != null);
                    slot.FallbackPlaque.gameObject.SetActive(portrait == null);
                    slot.NameText.text = string.IsNullOrEmpty(opt.PetName) ? $"灵宠 #{opt.PetTableId}" : opt.PetName;
                    slot.NameText.color = selected ? selName : normName;
                    slot.StatsText.text = $"Lv.{opt.Level} · 速{opt.Speed}";
                    slot.StatsText.color = selected ? selStats : normStats;
                    slot.Button.SetInteractable(!_summonRequestPending);
                    slot.Button.SetVisible(true);
                }
                else
                {
                    _summonPetIds[i] = 0UL;
                    slot.Button.SetVisible(false);
                }
            }
            if (_summonConfirmButton != null)
                _summonConfirmButton.SetInteractable(_summonSelectedPetId != 0UL && !_summonRequestPending);
        }

        private void OnConfirmSummonPetClicked()
        {
            var client = _owner?.Client;
            if (client == null || _state == null || _summonSelectedPetId == 0UL || _summonRequestPending)
                return;
            ulong battleId = _state.BattleId;
            uint round = _lastRound;
            ulong heroId = _myId;
            ulong chosenPetId = _summonSelectedPetId;
            _summonRequestPending = true;
            _submittedActors.Add(heroId);
            if (_summonDrawer != null) _summonDrawer.gameObject.SetActive(false);

            if (!client.SubmitSummonPet(chosenPetId, success =>
            {
                if (_state?.BattleId != battleId || _lastRound != round) return;
                _summonRequestPending = false;
                if (!success) _submittedActors.Remove(heroId);
                SelectNextActorIfNeeded();
                RefreshActionBar();
            }))
            {
                _summonRequestPending = false;
                _submittedActors.Remove(heroId);
                RefreshActionBar();
                return;
            }
            ExitTargetMode();
            SelectNextActorIfNeeded();
            RefreshActionBar();
        }

        private void OnSurrenderClicked()
        {
            if (_owner?.Client == null || !_owner.Client.CanSurrenderNow || _surrenderConfirmStrip == null || _state == null)
                return;
            int allyPlayers = 0;
            for (int i = 0; i < _state.Actors.Count; i++)
            {
                var actor = _state.Actors[i];
                if (actor != null && actor.ActorType == eBattleActorType.BattleActorTypePlayer && actor.TeamIndex == _myTeam)
                    allyPlayers++;
            }
            if (_surrenderConfirmText != null)
            {
                _surrenderConfirmText.text = allyPlayers <= 1
                    ? "确认体面认输？本局按正常战败结算，不计逃跑惩罚"
                    : "发起投降表决？需全部活跃队友同意（连续 2 回合未手动不计入）";
            }
            _surrenderConfirmStrip.gameObject.SetActive(!_surrenderConfirmStrip.gameObject.activeSelf);
        }

        private void OnConfirmSurrenderClicked()
        {
            if (_surrenderConfirmStrip != null) _surrenderConfirmStrip.gameObject.SetActive(false);
            SendSurrenderVote(true);
        }

        private void SendSurrenderVote(bool agree)
        {
            if (_surrenderRequestPending || _owner?.Client == null) return;
            _surrenderRequestPending = true;
            RefreshSurrenderUi();
            _owner.Client.SurrenderBattle(agree, _ =>
            {
                _surrenderRequestPending = false;
                RefreshSurrenderUi();
            });
        }

        private void RefreshSpectateHeader()
        {
            if (_spectateText == null) return;
            bool readOnly = IsReadOnly;
            _spectateText.gameObject.SetActive(readOnly);
            _stopWatchButton.SetVisible(readOnly);
            if (_spectate)
            {
                _spectateText.text = $"观战中 · 观众 {_observerCount} 人";
                _stopWatchButton.SetText("退出观战");
            }
            else if (_replay)
            {
                _stopWatchButton.SetText("退出回放");
            }
        }

        private UiTextButton CreateReplayButton(string name, Rect rect, string label, Color plate, UnityEngine.Events.UnityAction onClick)
        {
            var button = BattleUiWidgets.CreateTextButton(name, _hudRoot, rect.x, rect.y, rect.width, rect.height,
                label, 22f, plate, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(button.Plate, "button_9slice");
            button.Button.onClick.AddListener(onClick);
            return button;
        }

        private void SetReplayControlsVisible(bool visible)
        {
            _replayPrevButton?.SetVisible(visible);
            _replayPauseButton?.SetVisible(visible);
            _replayNextButton?.SetVisible(visible);
            _replaySpeedButton?.SetVisible(visible);
            _replaySkipButton?.SetVisible(visible);
            if (_replayJumpInputPlate != null) _replayJumpInputPlate.gameObject.SetActive(visible);
            _replayJumpButton?.SetVisible(visible);
            _replayStatsButton?.SetVisible(visible);
            _replayOverlay?.SetVisible(visible);
        }

        private void OnReplayJumpClicked()
        {
            if (!_replay) return;
            string raw = (_replayJumpInput != null ? _replayJumpInput.text : string.Empty)?.Trim();
            if (int.TryParse(raw, out int round) && round >= 1)
            {
                _owner?.RequestReplayJumpRound(round);
            }
            else
            {
                _owner?.ShowToast("请输入有效回合号(从 1 开始)", true);
            }
        }

        private bool IsPveBattle()
        {
            if (_state == null) return true;
            return !BattleHudLogic.IsPvp(_state.Actors, _myTeam);
        }

        private bool HasCaptureTarget()
        {
            if (_state == null || !IsPveBattle()) return false;
            foreach (var actor in _state.Actors)
                if (CanCapture(actor)) return true;
            return false;
        }

        private bool CanRecallPet(BattleActorState pet) => FindMyActor()?.ActorId == _myId &&
            pet != null && pet.ActorType == eBattleActorType.BattleActorTypePet && pet.OwnerPlayerId == _myId &&
            pet.Fled && !pet.IsDead && pet.WithdrawUntilRound > 0 && _state != null && _state.RoundIndex >= pet.WithdrawUntilRound;

        private BattleActorState FindRecallablePet()
        {
            if (_state != null) foreach (var actor in _state.Actors) if (CanRecallPet(actor)) return actor;
            return null;
        }

        private string RecallWaitHint()
        {
            if (_state != null) foreach (var actor in _state.Actors)
                if (actor.ActorType == eBattleActorType.BattleActorTypePet && actor.OwnerPlayerId == _myId &&
                    actor.Fled && !actor.IsDead && actor.WithdrawUntilRound > _state.RoundIndex)
                    return $" · 宝宝 {actor.WithdrawUntilRound - _state.RoundIndex} 回合后可召回";
            return string.Empty;
        }

        private bool CanCapture(BattleActorState target)
        {
            var owner = FindMyActor();
            return IsPveBattle() && owner != null && target != null &&
                owner.ActorType == eBattleActorType.BattleActorTypePlayer &&
                target.ActorType == eBattleActorType.BattleActorTypeMonster && target.TeamIndex != _myTeam &&
                target.CapturePetTableId != 0 && target.CapturedByPlayerId == 0 &&
                !target.IsDead && !target.Fled && owner.Level >= target.CaptureMinLevel;
        }

        private BattleActorState FindMyActor()
        {
            if (_state == null || _actingId == 0) return null;
            return FindActor(_actingId);
        }

        private bool IsControlled(BattleActorState actor) => actor != null && _myId != 0 &&
            (actor.ActorId == _myId || actor.ActorType == eBattleActorType.BattleActorTypePet && actor.OwnerPlayerId == _myId);

        private bool IsSubmitted(ulong id) => _submittedActors.Contains(id) ||
            _state != null && _state.PendingActorIds.Count > 0 && !_state.PendingActorIds.Contains(id);

        private void SelectNextActorIfNeeded()
        {
            bool Available(BattleActorState a) => IsControlled(a) && !a.IsDead && !a.Fled && !a.IsAuto && !IsSubmitted(a.ActorId);
            if (Available(FindActor(_actingId)) || _state == null) return;
            foreach (var actor in _state.Actors)
                if (Available(actor)) { _actingId = actor.ActorId; ExitTargetMode(); EnsureSkillCatalog(); return; }
        }

        private void SelectActingActor(ulong id)
        {
            if (IsReadOnly || _playing || _phase != BattlePhase.WaitingAction || !IsControlled(FindActor(id))) return;
            _actingId = id;
            ExitTargetMode();
            EnsureSkillCatalog();
            RefreshActionBar();
        }

        private void EnsureSkillCatalog()
        {
            var actor = FindMyActor();
            var source = MmorpgClient.Game.Skills.SkillBookClient.Instance;
            if (IsReadOnly || source == null || !IsControlled(actor)) return;
            ulong actorId = actor.ActorId;
            source.EnsureBattleCatalog(actor.ActorType == eBattleActorType.BattleActorTypePet ? actor.PetId : 0, success =>
            {
                if (!success || _actingId != actorId) return;
                if (_skillPanel.gameObject.activeSelf) PopulateSkillPanel();
            });
        }

        private void SetHint(string value)
        {
            if (_hintText != null) _hintText.text = value ?? string.Empty;
        }
    }

    /// <summary>技能元数据对应的目标显示范围，不计算伤害、消耗或施放资格。</summary>
    public static class BattleTargetPresentation
    {
        public static bool AllowsTarget(string targetTeam, ulong actingId, uint actingTeam,
            ulong targetId, uint targetTeamIndex, bool isDead, bool fled, bool allowDeadTarget = false)
        {
            if (fled || isDead && !allowDeadTarget) return false;
            if (string.Equals(targetTeam, "enemy", System.StringComparison.OrdinalIgnoreCase))
                return targetTeamIndex != actingTeam;
            if (string.Equals(targetTeam, "ally", System.StringComparison.OrdinalIgnoreCase))
                return targetTeamIndex == actingTeam;
            if (string.Equals(targetTeam, "self", System.StringComparison.OrdinalIgnoreCase))
                return actingId != 0 && targetId == actingId;
            // 旧技能和缺少元数据的道具保留既有可选范围，避免因展示目录未同步而不可操作。
            return true;
        }
    }
}
