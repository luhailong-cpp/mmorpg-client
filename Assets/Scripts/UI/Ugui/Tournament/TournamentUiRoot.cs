using System.Collections.Generic;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Game.Team;
using MmorpgClient.Game.Tournament;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Guild;
using MmorpgClient.UI.Ugui.Mail;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Tournament
{
    /// <summary>
    /// 全服赛季战队锦标赛《群仙问道赛》V1 主 UI 根：
    /// - 自动挂载并向 WorldHudNavigation 注册主城「群仙问道赛」入口（归属历练页签，紧邻论剑）；
    /// - 管理 1920×1080 安全区自适应画布（EquipmentUiFit + 顶部胶囊避让），承载 5 分页与 4 轻弹窗；
    /// - 严格对称订阅/退订 TournamentClient.Changed 与 MatchBattleLaunched，断线/切号/重新 Attach 自动清理旧态；
    /// - 精选观战直连 SpectateClient.Instance.WatchBattle，本队战报回放直连 BattleReportClient.Instance.FetchReplay，
    ///   领奖后「前往邮箱」直连 MailUiRoot.Instance.Toggle。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TournamentUiRoot : MonoBehaviour
    {
        public static TournamentUiRoot Instance { get; private set; }
        public const int CanvasSortingOrder = 187;

        public bool IsWindowVisible => _window != null && _window.IsVisible;
        public TournamentClient Client => _client;
        public TournamentUiModel Model => _model;

        private readonly TournamentUiModel _model = new TournamentUiModel();

        private GameClient _game;
        private TournamentClient _client;
        private TeamInvitationDirectory _inviteDirectory;
        private GuildClient _guildObserved;
        private ulong _playerId;
        private object _gateIdentity;
        private bool _available;

        private bool _awaitingMutationResult;
        private bool _autoRefreshArmed;
        private float _nextCountdownTickAt;
        private string _lastHudLabel = string.Empty;

        private Canvas _canvas;
        private RectTransform _hud;
        private RectTransform _backdrop;
        private RectTransform _fittedRoot;
        private Button _entryButton;
        private TMP_Text _entryLabel;
        private TournamentWindow _window;
        private TournamentSpectateCheerOverlay _spectateCheerOverlay;
        private TournamentChampionStageController _championStage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[TournamentUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<TournamentUiRoot>();
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
            CityPanelRegistry.Register(this, Hide);
            Build();
        }

        private void OnEnable()
        {
            BindClient(TournamentClient.Instance);
        }

        private void OnDisable()
        {
            Hide();
            _spectateCheerOverlay?.ResetAndHide();
            _championStage?.Dispose();
            UnbindClient();
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            _spectateCheerOverlay?.UnbindClient();
            _spectateCheerOverlay?.ResetAndHide();
            _championStage?.Dispose();
            UnbindClient();
            UnbindInviteSources();
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            var canvasGo = new GameObject("TournamentCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(TournamentArtCatalog.DesignWidth, TournamentArtCatalog.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var hudDesign = QdaoUguiFactory.CreateCenteredRect("TournamentHudDesign", canvasGo.transform, 2560f, 1080f);
            _hud = QdaoUguiFactory.CreateStretch("TournamentHudRoot", hudDesign, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            _entryButton = GameplayUiArt.Button(_hud, "群仙问道赛", BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(11),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 30);
            _entryButton.name = "TournamentEntry";
            _entryLabel = _entryButton.GetComponentInChildren<TMP_Text>(true);
            WorldHudNavigation.Register(WorldHudEntry.Tournament, (RectTransform)_entryButton.transform, _entryButton, _entryLabel);
            _hud.gameObject.SetActive(false);

            _backdrop = QdaoUguiFactory.CreateStretch("TournamentWindowBackdrop", canvasGo.transform, Vector4.zero);
            var dim = _backdrop.gameObject.AddComponent<Image>();
            dim.color = new Color(0.03f, 0.07f, 0.05f, 0.64f);
            dim.raycastTarget = true;
            _backdrop.gameObject.AddComponent<GameplayInputBlocker>();

            _fittedRoot = QdaoUguiFactory.CreateCenteredRect("TournamentFittedRoot", _backdrop,
                TournamentArtCatalog.DesignWidth, TournamentArtCatalog.DesignHeight);
            var fit = _fittedRoot.gameObject.AddComponent<EquipmentUiFit>();
            fit.referenceSize = new Vector2(TournamentArtCatalog.DesignWidth, TournamentArtCatalog.DesignHeight);

            _window = new TournamentWindow(_fittedRoot, _model, ResolveCharacterId);
            WireWindowEvents();
            _backdrop.gameObject.SetActive(false);

            _spectateCheerOverlay = new TournamentSpectateCheerOverlay(transform);
            _championStage = new TournamentChampionStageController(_hud, OpenSaluteModalFromCity);
        }

        private void WireWindowEvents()
        {
            _window.CloseRequested += Hide;
            _window.RefreshOverviewRequested += () =>
            {
                _autoRefreshArmed = false;
                _client?.RequestOverview(includeHistory: true);
                if (_model.ActiveTab == TournamentTab.Standings)
                    _client?.RequestStandings(_model.SelectedStandingsSeasonId, _model.StandingsPage, TournamentUiModel.DefaultPageSize);
            };
            _window.RequestStandingsRequested += (seasonId, page) =>
            {
                _model.SelectedStandingsSeasonId = seasonId;
                _model.StandingsPage = page;
                _client?.RequestStandings(seasonId, page, TournamentUiModel.DefaultPageSize);
            };
            _window.CreateSquadSubmitted += (name, emblemId) => SubmitMutation(() => _client?.CreateSquad(name, emblemId) == true);
            _window.UpdateEmblemSubmitted += emblemId => SubmitMutation(() => _client?.UpdateSquadEmblem(emblemId) == true);
            _window.StartWarmupSubmitted += (targetSq, leyline, lineup) => SubmitMutation(() => _client?.StartWarmup(targetSq, leyline, lineup) == true);
            _window.PredictMatchClicked += (matchId, squadId) => SubmitMutation(() => _client?.PredictMatch(matchId, squadId) == true);
            _window.RequestPredictionBoardRequested += seasonId => _client?.RequestPredictionBoard(seasonId);
            _window.BanLeylineSubmitted += (matchId, leyline) => SubmitMutation(() => _client?.BanLeyline(matchId, leyline) == true);
            _window.RequestHighlightsRequested += seasonId => _client?.RequestHighlights(seasonId);
            _window.InviteMemberSubmitted += pid => SubmitMutation(() => _client?.InviteMember(pid) == true);
            _window.RespondInviteClicked += (sid, accept) => SubmitMutation(() => _client?.RespondInvite(sid, accept) == true);
            _window.LeaveOrKickClicked += pid => SubmitMutation(() => _client?.LeaveOrKick(pid) == true);
            _window.UpdateLineupSubmitted += starters => SubmitMutation(() => _client?.UpdateLineup(starters) == true);
            _window.RegisterSquadConfirmed += () => SubmitMutation(() => _client?.RegisterSquad() == true);
            _window.StartMatchSubmitted += (matchId, lineup) => SubmitMutation(() => _client?.StartMatch(matchId, lineup) == true);
            _window.CheerSquadClicked += sid => SubmitMutation(() => _client?.CheerSquad(sid) == true);
            _window.ClaimRewardClicked += sid => SubmitMutation(() => _client?.ClaimReward(sid) == true);
            _window.SaluteChampionConfirmed += sid => SubmitMutation(() => _client?.SaluteChampion(sid) == true);
            _window.WatchBattleClicked += HandleWatchBattle;
            _window.WatchReplayClicked += HandleWatchReplay;
            _window.OpenMailClicked += HandleOpenMail;
            _window.InviteSourceRequested += HandleInviteSourceRequested;
        }

        private void SubmitMutation(System.Func<bool> action)
        {
            if (_client == null) return;
            _autoRefreshArmed = true;
            if (action())
            {
                _awaitingMutationResult = true;
            }
        }

        private void Update()
        {
            var game = AppBootstrap.Instance?.GameClient;
            if (!ReferenceEquals(_game, game))
            {
                _game = game;
                RebindInviteDirectory(game);
                BindClient(game?.Tournament ?? TournamentClient.Instance);
            }
            else if (!ReferenceEquals(_client, game?.Tournament ?? TournamentClient.Instance))
            {
                BindClient(game?.Tournament ?? TournamentClient.Instance);
            }
            SyncGuildObserver();
            if (IsWindowVisible && _model.ActiveModal == TournamentModalKind.InviteMember)
            {
                _inviteDirectory?.ObserveConnection();
            }

            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            ulong player = inGame ? _game.PlayerId : 0UL;
            object gate = inGame ? _game.GateConnectionIdentity : null;
            if (_playerId != player || !ReferenceEquals(_gateIdentity, gate))
            {
                _playerId = player;
                _gateIdentity = gate;
                _awaitingMutationResult = false;
                _autoRefreshArmed = false;
                _model.ResetSession();
                _spectateCheerOverlay?.ResetAndHide();
                Hide();
            }

            bool battleVisible = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = inGame && !battleVisible;
            if (_hud != null) _hud.gameObject.SetActive(_available);
            float nowTime = Time.realtimeSinceStartup;
            _spectateCheerOverlay?.Tick(nowTime, inGame);
            _championStage?.Tick(_client?.Overview, _available && !IsWindowVisible);
            if (!inGame)
            {
                Hide();
                return;
            }
            if (battleVisible && IsWindowVisible)
            {
                Hide();
                return;
            }

            UpdateHudEntryLabel();

            if (IsWindowVisible)
            {
                if (nowTime >= _nextCountdownTickAt)
                {
                    _nextCountdownTickAt = nowTime + 0.5f;
                    _window.Tick(nowTime);
                }
                HandleEscapeKey();
            }
        }

        public void OpenSaluteModalFromCity(uint seasonId = 0u)
        {
            if (!_available || _window == null) return;
            BindClient(_game?.Tournament ?? TournamentClient.Instance);
            CityPanelRegistry.HideOthers(this);
            _model.ActiveTab = TournamentTab.Hall;
            _model.OpenSaluteModal(seasonId);
            _backdrop.gameObject.SetActive(true);
            _window.Show();
            RenderWindow();
            _client?.RequestOverview(includeHistory: true);
        }

        public void Toggle()
        {
            if (IsWindowVisible) Hide();
            else Open(TournamentTab.Overview);
        }

        public void Open(TournamentTab tab = TournamentTab.Overview)
        {
            if (!_available || _window == null) return;
            BindClient(_game?.Tournament ?? TournamentClient.Instance);
            CityPanelRegistry.HideOthers(this);
            _model.ActiveTab = tab;
            _model.CloseModal();
            _backdrop.gameObject.SetActive(true);
            _window.Show();
            RenderWindow();
            _client?.RequestOverview(includeHistory: true);
            if (tab == TournamentTab.Standings)
                _client?.RequestStandings(_model.SelectedStandingsSeasonId, _model.StandingsPage, TournamentUiModel.DefaultPageSize);
        }

        public void Hide()
        {
            _awaitingMutationResult = false;
            _window?.Hide();
            if (_backdrop != null) _backdrop.gameObject.SetActive(false);
        }

        private void BindClient(TournamentClient next)
        {
            if (ReferenceEquals(_client, next)) return;
            UnbindClient();
            _client = next;
            _spectateCheerOverlay?.BindClient(_client);
            if (_client != null)
            {
                _client.Changed += HandleClientChanged;
                _client.MatchBattleLaunched += HandleMatchBattleLaunched;
            }
        }

        private void UnbindClient()
        {
            _spectateCheerOverlay?.UnbindClient();
            if (_client == null) return;
            _client.Changed -= HandleClientChanged;
            _client.MatchBattleLaunched -= HandleMatchBattleLaunched;
            _client = null;
        }

        private void HandleClientChanged()
        {
            if (_client == null) return;
            float now = Time.realtimeSinceStartup;

            // 断线或切号清空快照时立即复位弹窗，绝不残留上一账号数据
            if (_client.Overview == null && !_client.IsLoading)
            {
                _awaitingMutationResult = false;
                _model.CloseModal();
            }

            if (_awaitingMutationResult && !_client.IsMutating)
            {
                _awaitingMutationResult = false;
                if (string.IsNullOrEmpty(_client.LastError))
                {
                    _autoRefreshArmed = false;
                    // BO3 灵脉禁选成功后留在窗内实时刷新「禁」印与重算后的 3 局序列，便于队长核对或开战前改禁
                    if (_model.ActiveModal != TournamentModalKind.BanLeyline)
                        _model.CloseModal();
                    if (!string.IsNullOrEmpty(_client.Status))
                        _window?.ShowToast(_client.Status, isError: false, now);
                }
                else
                {
                    var recovery = TournamentUiModel.ClassifyTipRecovery(_client.LastErrorTipId);
                    if (recovery.ShowNotOpenEmptyState)
                    {
                        _model.ShowNotOpenState = true;
                        _model.CloseModal();
                        _window?.ShowToast(_client.LastError, isError: true, now);
                    }
                    else if (recovery.KeepCreateModalInline
                        && (_model.ActiveModal == TournamentModalKind.CreateSquad || _model.ActiveModal == TournamentModalKind.SelectEmblem))
                    {
                        _model.ModalInlineError = _client.LastError;
                    }
                    else if (recovery.KeepLineupModalInline
                        && (_model.ActiveModal == TournamentModalKind.ConfirmLineup
                            || _model.ActiveModal == TournamentModalKind.IntermissionSwap
                            || _model.ActiveModal == TournamentModalKind.WarmupSpar))
                    {
                        _model.ModalInlineError = _client.LastError;
                    }
                    else
                    {
                        _model.CloseModal();
                        _window?.ShowToast(_client.LastError, isError: true, now);
                        if (recovery.AutoRefreshOverviewOnce && _autoRefreshArmed)
                        {
                            _autoRefreshArmed = false;
                            _client.RequestOverview(includeHistory: true);
                        }
                    }
                }
            }
            else if (!string.IsNullOrEmpty(_client.LastError) && !_client.IsBusy)
            {
                var recovery = TournamentUiModel.ClassifyTipRecovery(_client.LastErrorTipId);
                if (recovery.ShowNotOpenEmptyState) _model.ShowNotOpenState = true;
                _window?.ShowToast(_client.LastError, isError: true, now);
            }

            // 海选总览需要前 5 强：若切入总览且处于海选期尚无榜单快照，自动补拉一次
            if (IsWindowVisible && _model.ActiveTab == TournamentTab.Overview
                && _client.Overview?.Season?.Stage == Match.TournamentStage.Qualifier
                && _client.Standings == null && !_client.IsLoading)
            {
                _client.RequestStandings(0u, 1u, TournamentUiModel.DefaultPageSize);
            }

            RenderWindow();
        }

        private void HandleMatchBattleLaunched(ulong battleId)
        {
            if (battleId == 0UL) return;
            _window?.ShowToast("对手已就绪，正在进入全服赛场…", isError: false, Time.realtimeSinceStartup);
            Hide();
        }

        private void HandleWatchBattle(ulong battleId)
        {
            if (battleId == 0UL) return;
            var spectate = SpectateClient.Instance;
            if (spectate == null)
            {
                _window?.ShowToast("观战通道尚未就绪，请稍后再试。", isError: true, Time.realtimeSinceStartup);
                return;
            }
            Hide();
            spectate.WatchBattle(battleId);
        }

        private void HandleWatchReplay(ulong battleId)
        {
            if (battleId == 0UL) return;
            var reports = BattleReportClient.Instance;
            if (reports == null)
            {
                _window?.ShowToast("战报回放通道尚未就绪，请稍后再试。", isError: true, Time.realtimeSinceStartup);
                return;
            }
            Hide();
            reports.FetchReplay(battleId);
        }

        private void HandleOpenMail()
        {
            Hide();
            MailUiRoot.Instance?.Toggle();
        }

        private void RenderWindow()
        {
            if (!IsWindowVisible || _window == null) return;
            IReadOnlyList<TournamentInviteCandidate> inviteCandidates = null;
            string inviteStatus = null;
            if (_model.ActiveModal == TournamentModalKind.InviteMember)
            {
                BuildInviteCandidatesSnapshot(out inviteCandidates, out inviteStatus);
            }
            _window.Render(
                _client?.Overview,
                _client?.Standings,
                _client?.PlayerId ?? _playerId,
                _client?.IsBusy == true,
                _client?.Status,
                Time.realtimeSinceStartup,
                inviteCandidates,
                inviteStatus,
                _client?.PredictionBoard,
                _client?.Highlights);
        }

        private void RebindInviteDirectory(GameClient game)
        {
            if (_inviteDirectory != null)
            {
                _inviteDirectory.Changed -= HandleInviteCandidatesChanged;
                _inviteDirectory.Dispose();
                _inviteDirectory = null;
            }
            if (game != null)
            {
                _inviteDirectory = new TeamInvitationDirectory(game, null);
                _inviteDirectory.Changed += HandleInviteCandidatesChanged;
            }
        }

        private void SyncGuildObserver()
        {
            var next = GuildUiRoot.Instance?.Client;
            if (ReferenceEquals(_guildObserved, next)) return;
            if (_guildObserved != null) _guildObserved.Changed -= HandleInviteCandidatesChanged;
            _guildObserved = next;
            if (_guildObserved != null) _guildObserved.Changed += HandleInviteCandidatesChanged;
        }

        private void UnbindInviteSources()
        {
            if (_inviteDirectory != null)
            {
                _inviteDirectory.Changed -= HandleInviteCandidatesChanged;
                _inviteDirectory.Dispose();
                _inviteDirectory = null;
            }
            if (_guildObserved != null)
            {
                _guildObserved.Changed -= HandleInviteCandidatesChanged;
                _guildObserved = null;
            }
        }

        private void HandleInviteSourceRequested(TournamentInviteQuickSource source)
        {
            _model.InviteSource = source;
            if (source == TournamentInviteQuickSource.Guild)
            {
                SyncGuildObserver();
                if (_guildObserved != null && !_guildObserved.Busy && !_guildObserved.HasLoaded)
                    _guildObserved.Refresh();
                RenderWindow();
                return;
            }
            if (_inviteDirectory == null && _game != null) RebindInviteDirectory(_game);
            if (_inviteDirectory != null)
            {
                var teamSource = source == TournamentInviteQuickSource.Nearby
                    ? TeamInvitationSource.Nearby
                    : TeamInvitationSource.Friends;
                _inviteDirectory.Refresh(teamSource);
            }
            RenderWindow();
        }

        private void HandleInviteCandidatesChanged()
        {
            if (IsWindowVisible && _model.ActiveModal == TournamentModalKind.InviteMember)
                RenderWindow();
        }

        private void BuildInviteCandidatesSnapshot(
            out IReadOnlyList<TournamentInviteCandidate> candidates,
            out string status)
        {
            var raw = new List<TournamentInviteCandidate>();
            if (_model.InviteSource == TournamentInviteQuickSource.Guild)
            {
                SyncGuildObserver();
                var info = _guildObserved?.Info;
                if (info == null)
                {
                    status = _guildObserved == null
                        ? "帮会服务未就绪，可直接输入角色 ID 邀请。"
                        : (_guildObserved.Busy ? "正在同步同帮成员名单…" : (_guildObserved.HasLoaded ? "你尚未加入帮会。" : "点击「刷新」读取同帮成员。"));
                }
                else
                {
                    for (int i = 0; i < info.Members.Count; i++)
                    {
                        var m = info.Members[i];
                        if (m == null) continue;
                        raw.Add(new TournamentInviteCandidate(
                            m.PlayerId,
                            GuildClient.DisplayName(m.PlayerId, m.Name),
                            "同帮",
                            m.Online,
                            onlineStatusKnown: true));
                    }
                    status = raw.Count > 0 ? $"帮会「{info.Name}」成员名单，点击即可发起跨区邀请。" : "当前帮会暂无其他可邀请成员。";
                }
            }
            else
            {
                string tag = _model.InviteSource == TournamentInviteQuickSource.Nearby ? "附近" : "好友";
                if (_inviteDirectory != null)
                {
                    for (int i = 0; i < _inviteDirectory.Candidates.Count; i++)
                    {
                        var role = _inviteDirectory.Candidates[i];
                        if (role == null) continue;
                        raw.Add(new TournamentInviteCandidate(
                            role.PlayerId,
                            role.Name,
                            tag,
                            role.IsOnline,
                            role.OnlineStatusKnown,
                            role.CharacterId));
                    }
                    status = _inviteDirectory.Status;
                }
                else
                {
                    status = "名单服务尚未就绪，可直接在上方输入角色 ID 邀请。";
                }
            }
            ulong selfId = _client?.PlayerId ?? _playerId;
            candidates = TournamentUiModel.FilterInviteCandidates(raw, selfId, _client?.Overview?.MySquad);
        }

        private void UpdateHudEntryLabel()
        {
            if (_entryLabel == null) return;
            var ov = _client?.Overview;
            string text = "群仙问道赛";
            if (ov != null && ov.HasSeason && ov.IsEnabled)
            {
                if (ov.HasClaimableReward) text = "群仙问道赛 · 领奖";
                else if (!ov.HasSquad && ov.HasPendingInvites) text = "群仙问道赛 · 邀";
                else if (ov.Season.Stage == Match.TournamentStage.Knockout && ov.FeaturedMatches.Count > 0) text = "群仙问道赛 · 直播";
            }
            if (!string.Equals(_lastHudLabel, text, System.StringComparison.Ordinal))
            {
                _lastHudLabel = text;
                _entryLabel.text = text;
            }
        }

        private string ResolveCharacterId(ulong playerId) => _game?.ResolveCharacterId(playerId);

        private void HandleEscapeKey()
        {
            if (CityPanelRegistry.IsTyping()) return;
            bool esc = false;
#if ENABLE_INPUT_SYSTEM
            esc = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            esc = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (!esc) return;
            if (_model.ActiveModal != TournamentModalKind.None)
            {
                _model.CloseModal();
                RenderWindow();
            }
            else
            {
                Hide();
            }
        }
    }
}
