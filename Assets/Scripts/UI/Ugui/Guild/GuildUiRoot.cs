using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.Sync;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Guild
{
    /// <summary>主城左栏帮会入口。由真实会话驱动，战斗、退出和换角均关闭并清理状态。</summary>
    public sealed class GuildUiRoot : MonoBehaviour
    {
        public static GuildUiRoot Instance { get; private set; }
        public GuildWindow Window => _window;
        public GuildClient Client => _client;
        public GuildWarWindow WarWindow => _warWindow;
        public GuildWarClient WarClient => _warClient;
        public GuildWarFieldHud FieldHud => _fieldHud;
        public GuildWarLeylineWorldBanner LeylineBanner => _leylineBanner;
        public bool IsActiveGuildWarBattle(ulong battleId) =>
            battleId != 0 && _warClient != null &&
            (_warClient.LastLaunchedBattleId == battleId || _warClient.Overview?.ActiveBattleId == battleId);
        public const float EntryX = 68, EntryY = 600;
        private const float FieldInspectIntervalSec = 3f;
        private GameClient _game;
        private GuildClient _client;
        private GuildWarClient _warClient;
        // 落地恢复的接线(LandingSyncBindings.BindGuild):随 _client 一起建、在它销毁之前解除;
        // Toggle / HidePanel / 换角复位都不碰它(GuildClient.Reset 不清钩子)。
        private System.IDisposable _guildSync;
        private GuildWindow _window;
        private GuildWarWindow _warWindow;
        private GuildWarFieldHud _fieldHud;
        private GuildWarLeylineWorldBanner _leylineBanner;
        private RectTransform _hud;
        private ulong _player;
        private bool _available;
        // 上一帧战斗层是否显示;由显示变为不显示的那一帧 = 一场战斗刚结束。
        private bool _inBattle;
        private float _nextFieldInspectSec;
        private uint _lastObservedSceneConfigId;
        private bool _destroyed;
        private bool HasView => !_destroyed && _window?.Root != null && _hud != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[GuildUi]");
            DontDestroyOnLoad(go); go.AddComponent<GuildUiRoot>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);
            var go = new GameObject("GuildCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 187;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", go.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            var entry = GameplayUiArt.Button(_hud, "帮会 [G]", EntryX, EntryY,
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 32);
            entry.name = "GuildEntry";
            WorldHudNavigation.Register(WorldHudEntry.Guild, (RectTransform)entry.transform, entry, entry.GetComponentInChildren<TMP_Text>());
            _window = new GuildWindow(design);
            _window.RefreshRequested += () => { if (_available) _client?.Refresh(); };
            _window.EconomyRecoveryRequested += () => { if (_available) _client?.RecoverEconomyRequest(); };
            // 榜单与建帮所在区都由服务端按玩家归属区决定，界面不再传区。
            _window.RankRequested += page => { if (_available) _client?.Browse(page); };
            _window.CreateRequested += name => { if (_available) _client?.Create(name); };
            // 申请制:排行页不再直接入帮,改为提交 / 撤回申请,由帮主或长老审批。
            _window.ApplyRequested += id => { if (_available) _client?.ApplyToJoin(id); };
            _window.CancelApplicationRequested += id => { if (_available) _client?.CancelApplication(id); };
            _window.RoleRequested += (id, role) => { if (_available) _client?.SetMemberRole(id, role); };
            _window.KickRequested += id => { if (_available) _client?.Kick(id); };
            _window.TransferRequested += id => { if (_available) _client?.TransferLeader(id); };
            _window.ReviewRequested += (id, approve) => { if (_available) _client?.Review(id, approve); };
            _window.ApplicationsRequested += () => { if (_available) _client?.LoadApplications(); };
            _window.AnnouncementRequested += text => { if (_available) _client?.SaveAnnouncement(text); };
            _window.RenameRequested += name => { if (_available) _client?.Rename(name); };
            _window.LeaveRequested += () => { if (_available) _client?.Leave(); };
            _window.DisbandRequested += () => { if (_available) _client?.Disband(); };
            // 经济(B5):捐献页 / 商店页 / 总览升级。
            _window.DonationsRequested += () => { if (_available) _client?.RefreshDonations(); };
            _window.DonateRequested += id => { if (_available) _client?.Donate(id); };
            // 带确认框打开时的等级:确认前别的长老先升了,客户端就拒发,不连升两级。
            _window.UpgradeRequested += level => { if (_available) _client?.Upgrade(level); };
            _window.ShopRequested += () => { if (_available) _client?.RefreshShop(); };
            _window.ShopBuyRequested += id => { if (_available) _client?.Buy(id); };
            // 活动(B6a):读取只排队(打开窗口那一发 GetPlayerGuild 多半还在路上),由下面 Update 里的 DrainQueued 发出;
            // 点灯 / 领团圆礼是写操作。
            _window.ActivitiesRequested += () => { if (_available) _client?.QueueActivities(); };
            _window.LanternRequested += id => { if (_available) _client?.LightLantern(id); };
            _window.ReunionRequested += id => { if (_available) _client?.ClaimReunion(id); };
            // 同道历练(B6b):选人框发出邀请(建房),邀请框 / 卡片按钮应答(同意、婉拒、发起人取消)。
            _window.TrialRequested += (id, members) => { if (_available) _client?.StartTrial(id, members); };
            _window.TrialInviteResponded += (lobby, accept) => { if (_available) _client?.RespondTrialInvite(lobby, accept); };
            _warWindow = new GuildWarWindow(design);
            _window.GuildWarRequested += OpenGuildWar;
            _warWindow.BackToGuildRequested += () => { if (_available) { _window.Show(); RefreshOrQueue(); } };
            _warWindow.OpenGuildShopRequested += () => { if (_available) { _window.Show(GuildPage.Shop); RefreshOrQueue(); } };
            _warWindow.WatchBattleRequested += WatchBattle;
            _warWindow.TravelToSceneRequested += TravelToScene;
            _warWindow.ReplayBattleRequested += battleId =>
            {
                if (battleId == 0) return;
                BattleReportClient.Instance?.FetchReplay(battleId);
            };
            _fieldHud = new GuildWarFieldHud(_hud);
            _fieldHud.OpenWarWindowRequested += OpenGuildWar;
            _fieldHud.WatchBattleRequested += WatchBattle;
            _leylineBanner = new GuildWarLeylineWorldBanner();
            _hud.gameObject.SetActive(false);
        }
        private void Update()
        {
            if (!HasView) { UnbindClient(); return; }
            var game = AppBootstrap.Instance?.GameClient;
            if (_game != game)
            {
                _guildSync?.Dispose(); _guildSync = null;
                if (_client != null) { _client.Changed -= Changed; _client.AssetsChanged -= OnAssetsChanged; _client.WarUpdated -= OnWarUpdated; _client.Dispose(); }
                if (_warClient != null) { _warClient.Changed -= OnWarClientChanged; _warClient.Dispose(); _warClient = null; }
                _game = game;
                _client = game == null ? null : new GuildClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                    economyRecovery: DurableRequestFileStore.ForOrigin(
                        System.IO.Path.Combine(Application.persistentDataPath, "guild-economy-intents-v1"),
                        AppBootstrap.Instance.Session.GatewayBaseUrl, () => game.OwnerRevision));
                _warClient = game == null ? null : new GuildWarClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity);
                if (_client != null) {
                    _client.PrepareMatchNetwork=(ready,failed)=>game.Battle.RegionProbe.Prepare(_=>ready(),failed);
                    _client.Changed += Changed; _client.AssetsChanged += OnAssetsChanged; _client.WarUpdated += OnWarUpdated;
                    if (game.LandingSync != null) _guildSync = LandingSyncBindings.BindGuild(game.LandingSync, _client);
                }
                if (_warClient != null) _warClient.Changed += OnWarClientChanged;
                _window.ResetSession(); _window.SetClient(_client);
                _warWindow?.SetContext(_warClient, _client?.Info?.GuildId ?? 0UL, _client?.IsOfficerOrLeader ?? false);
                _fieldHud?.SetContext(_warClient, _client?.Info?.GuildId ?? 0UL, game?.CurrentSceneConfigId ?? 0u);
            }
            _client?.ObserveConnection();
            _warClient?.ObserveConnection();
            bool inGame = game != null && game.InGame && game.IsGateReady;
            ulong player = inGame ? game.PlayerId : 0;
            if (_player != player)
            {
                _player = player; _window.ResetSession(); _client?.Reset(); _window.SetClient(_client);
                _warClient?.Reset(); _warWindow?.Hide(); _warWindow?.DismissAlert();
                _warWindow?.SetContext(_warClient, 0UL, false);
                _fieldHud?.SetContext(_warClient, 0UL, 0u);
                _leylineBanner?.Hide();
                _nextFieldInspectSec = 0f; _lastObservedSceneConfigId = 0u;
            }
            bool inBattle = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            // 战斗层刚收起 = 一场战斗刚结束。打的若是同道历练,活动快照里还留着"历练进行中 / 已开启":结算推送可能丢、
            // 也可能晚到,让客户端把这份快照标成过时,下次进活动页自动重拉(没有在途历练时它什么也不做)。
            if (_inBattle && !inBattle)
            {
                _client?.NoteBattleEnded();
                if (_warClient != null && _warClient.LastLaunchedBattleId != 0)
                    _warClient.QueryBattleResult(_warClient.LastLaunchedBattleId);
            }
            _inBattle = inBattle;
            _available = inGame && !inBattle;
            _hud.gameObject.SetActive(_available);
            // 开战(收到 NotifyBattleStart 后战斗层显示)由战斗界面接管:本窗口连同邀请框 / 选人框在这里关掉。
            // 战斗中到达的历练邀请不弹,标志留在 GuildClient 里(有时限,见 TrialInvitePending)。
            if (!_available) { HidePanel(); _leylineBanner?.Hide(); return; }
            _warClient?.Tick();
            _warWindow?.Tick(Time.unscaledTime);
            UpdateFieldWarPresence(game);
            _leylineBanner?.Tick(Time.unscaledTime);
            OpenPendingTrialInvite();
            // NotifyGuildChanged 只置排队标志;真正的拉取在这里按帧消费,一帧最多发一个请求,
            // 不让每条推送都触发一次全量 Refresh。窗口关着不拉推送排的重拉,Toggle() 打开时已有 Refresh()。
            // Tick 排在后面:停在捐献 / 商店 / 活动页跨过日 / 周切点没有任何事件,由它补判快照过时;DrainQueued 刚发了请求时它什么也不做。
            // 活动视图只在活动页可见时重拉(ShowingActivities):不可见时排队标志留着,进页那一帧再发。
            // 窗口关着时只消费落地恢复排的读取(DrainSyncQueued,帮会快照 / 本人申请);战斗中走不到这里,位留着。
            if (_window.IsVisible) { _client?.DrainQueued(_window.ShowingApplications, _window.ShowingActivities); _window.Tick(); }
            else _client?.DrainSyncQueued();
            bool typing = IsTyping();
            if (!_window.IsVisible && (_warWindow == null || !_warWindow.IsVisible) && GameplayInputGate.IsKeyboardBlocked) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) { if (_warWindow != null && _warWindow.IsVisible) _warWindow.Hide(); else _window.Back(); }
            else if (!typing && keys.gKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) { if (_warWindow != null && _warWindow.IsVisible) _warWindow.Hide(); else _window.Back(); }
            else if (!typing && Input.GetKeyDown(KeyCode.G)) Toggle();
#endif
        }
        public void OpenGuildWar()
        {
            if (!HasView || !_available || _warWindow == null) return;
            _window.Hide();
            _warWindow.SetContext(_warClient, _client?.Info?.GuildId ?? 0UL, _client?.IsOfficerOrLeader ?? false);
            _warWindow.Show(GuildWarPage.Main);
        }
        public void Toggle()
        {
            if (!HasView) return;
            if (_warWindow != null && _warWindow.IsVisible) { HidePanel(); return; }
            if (_window.IsVisible) { HidePanel(); return; }
            if (!_available) return;
            CityPanelRegistry.HideOthers(this);
            _window.Show();
            RefreshOrQueue();
        }
        /// <summary>
        /// 打开窗口时拉一次帮会快照。后台的落地恢复读取可能正在途(Busy),这时直接 Refresh 会被静默丢掉,
        /// 窗口停在旧状态;改为排队,由窗口可见后的 DrainQueued 在空闲的那一帧发出。
        /// </summary>
        private void RefreshOrQueue()
        {
            if (_client == null) return;
            if (_client.Busy) _client.QueueRefresh();
            else _client.Refresh();
        }
        /// <summary>
        /// 收到同道历练邀请(GuildClient.TrialInvitePending)时把帮会窗口带到活动页,邀请框由窗口在拿到视图后自己弹出。
        /// 邀请只有几十秒有效,所以主动打开;但不打断玩家手上的事 ——
        ///   · 战斗中:走不到这里(Update 在 _available 为假时已返回),标志留着;
        ///   · 帮会窗口开着且有弹窗(正在确认解散、写公告、选历练同道……),或正在成员查找框里打字:等它结束;
        ///   · 帮会窗口关着,而别的全屏界面开着或玩家正在输入框里打字(GameplayInputGate.IsKeyboardBlocked):等它结束。
        /// 等太久邀请就过期了:标志自己失效(TrialInvitePending 有时限),不会在几分钟后凭空弹出一个空页面。
        /// 帮会窗口开着、手上没事时直接切到活动页:IsKeyboardBlocked 此时恒为真(窗口自己就挂着输入拦截),不能拿它判。
        /// </summary>
        private void OpenPendingTrialInvite()
        {
            // 已隔离(帮会请求待重新登录)时应答发不出去,不为它开窗;重连会把标志连同会话一起清掉。
            if (_client == null || !_client.TrialInvitePending || _client.RequiresReconnect) return;
            bool wasVisible = _window.IsVisible;
            if (wasVisible ? _window.ModalVisible || IsTyping() : GameplayInputGate.IsKeyboardBlocked) return;
            _client.ConsumeTrialInvite();
            if (!wasVisible) CityPanelRegistry.HideOthers(this);
            if (!_window.ShowingActivities) _window.Show(GuildPage.Activities);
            // 窗口是这一下才打开的:同 Toggle,先拉一次帮会快照(这次登录可能还没拉过,活动视图要等它)。
            // 活动视图只排队:有快照时下一个空闲帧发出;还没有快照时这一下不生效,快照落定后由窗口进页的自动拉取补上。
            if (!wasVisible) RefreshOrQueue();
            _client.QueueActivities();
        }
        private static bool IsTyping() => CityPanelRegistry.IsTyping();
        public void HidePanel()
        {
            if (_window?.Root != null) _window.Hide();
            if (_warWindow?.Root != null) _warWindow.Hide();
        }
        private void Changed()
        {
            if (HasView)
            {
                _window.SetClient(_client);
                ulong gid = _client?.Info?.GuildId ?? 0UL;
                _warWindow?.SetContext(_warClient, gid, _client?.IsOfficerOrLeader ?? false);
                _fieldHud?.UpdateScene(_game?.CurrentSceneConfigId ?? 0u, gid);
                if (gid != 0 && _warClient != null && _warClient.Overview == null && !_warClient.OverviewLoading)
                    _warClient.RefreshOverview();
            }
            else UnbindClient();
        }
        private void OnWarUpdated()
        {
            if (_warClient == null || _warClient.OverviewLoading) return;
            if ((_warWindow != null && _warWindow.IsVisible) || GuildWarFieldHud.IsWildWarScene(_game?.CurrentSceneConfigId ?? 0u))
                _warClient.RefreshOverview();
        }
        private void OnWarClientChanged()
        {
            _window?.SetWarOverview(_warClient?.Overview);
        }
        private void WatchBattle(ulong battleId)
        {
            if (battleId == 0) return;
            (_game?.Spectate ?? SpectateClient.Instance)?.WatchBattle(battleId);
        }
        private void TravelToScene(uint sceneConfigId, ulong sceneId)
        {
            if (_game == null || !_available || sceneConfigId == 0) return;
            _warWindow?.Hide();
            StartCoroutine(_game.EnterScene(sceneConfigId, sceneId,
                () => { _nextFieldInspectSec = 0f; },
                _ => { }));
        }
        private void UpdateFieldWarPresence(GameClient game)
        {
            if (game == null || _warClient == null) return;
            uint sceneConf = game.CurrentSceneConfigId;
            ulong gid = _client?.Info?.GuildId ?? 0UL;
            if (sceneConf != _lastObservedSceneConfigId)
            {
                _lastObservedSceneConfigId = sceneConf;
                _nextFieldInspectSec = 0f;
            }
            _fieldHud?.UpdateScene(sceneConf, gid);
            if (gid == 0 || !GuildWarFieldHud.IsWildWarScene(sceneConf))
            {
                _leylineBanner?.Hide();
                return;
            }

            UnityEngine.Vector3? localPos = null;
            if (game.World != null && game.World.HasLocalPlayer &&
                game.World.TryGetActor(game.World.LocalEntity, out var localActor) &&
                localActor != null && localActor.Go != null)
            {
                localPos = localActor.Go.transform.position;
            }
            SyncWildLeylineBanner(sceneConf, gid, localPos);

            float now = Time.unscaledTime;
            if (now >= _nextFieldInspectSec && game.World != null)
            {
                _nextFieldInspectSec = now + FieldInspectIntervalSec;
                var ids = new System.Collections.Generic.List<ulong>(GuildWarClient.MaxFieldInspectPlayers);
                foreach (var kv in game.World.Actors)
                {
                    var actor = kv.Value;
                    if (actor == null || actor.Kind != MmorpgClient.World.ActorKind.Player) continue;
                    if (actor.PlayerId == 0 || actor.PlayerId == _player) continue;
                    ids.Add(actor.PlayerId);
                    if (ids.Count >= GuildWarClient.MaxFieldInspectPlayers) break;
                }
                if (ids.Count > 0) _warClient.InspectFieldWarTargets(ids);
            }
            TryPickWildActor(game);
        }
        private void SyncWildLeylineBanner(uint sceneConf, ulong myGuildId, UnityEngine.Vector3? localPos)
        {
            if (_leylineBanner == null) return;
            uint lid = GuildWarLeylineWorldBanner.LeylineIdForScene(sceneConf);
            GuildWarLeylineSnapshot matched = null;
            var ov = _warClient?.Overview;
            if (ov != null && lid != 0)
            {
                for (int i = 0; i < ov.Leylines.Count; i++)
                {
                    if (ov.Leylines[i].LeylineId == lid) { matched = ov.Leylines[i]; break; }
                }
            }
            bool isGold = matched != null && (matched.IsGoldLeyline || (ov != null && ov.GoldLeylineId == lid));
            _leylineBanner.Sync(sceneConf, matched, myGuildId, _warClient?.EstimatedServerNowMs ?? 0UL, isGold, localPos);
            if (localPos.HasValue)
                _fieldHud?.UpdateBannerDistance(_leylineBanner.DistanceTo(localPos.Value));
        }
        private void TryPickWildActor(GameClient game)
        {
            if (_fieldHud == null || game?.World == null) return;
            if ((_window != null && _window.IsVisible) || (_warWindow != null && _warWindow.IsVisible)) return;
            bool clicked = false;
            Vector2 pointerPos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                clicked = true;
                pointerPos = mouse.position.ReadValue();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                clicked = true;
                pointerPos = Input.mousePosition;
            }
#endif
            if (!clicked) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ScreenPointToRay(pointerPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 120f)) return;
            var hitTr = hit.transform;
            if (_leylineBanner != null && _leylineBanner.MatchesHit(hitTr))
            {
                _fieldHud.SelectLeylineBanner(_leylineBanner.ActiveLeylineId);
                return;
            }
            foreach (var kv in game.World.Actors)
            {
                var actor = kv.Value;
                if (actor == null || actor.Kind != MmorpgClient.World.ActorKind.Player || actor.Go == null) continue;
                if (actor.PlayerId == 0 || actor.PlayerId == _player) continue;
                if (hitTr == actor.Go.transform || hitTr.IsChildOf(actor.Go.transform))
                {
                    _fieldHud.SelectFieldTarget(actor.PlayerId);
                    return;
                }
            }
        }
        /// <summary>
        /// 捐献扣了货币 / 兑换发了物品:scene 没有余额推送,重拉一次背包(沿用玩家当前看的那个背包类型)。
        /// 背包客户端不在(未进场)就什么也不做,下次打开背包自然会拉。
        /// </summary>
        private static void OnAssetsChanged()
        {
            var features = PlayerFeaturesClient.Instance;
            features?.RequestBag(features.RequestedBagType);
        }
        private void UnbindClient()
        {
            _guildSync?.Dispose(); _guildSync = null;
            if (_client != null) { _client.Changed -= Changed; _client.AssetsChanged -= OnAssetsChanged; _client.WarUpdated -= OnWarUpdated; _client.Dispose(); }
            if (_warClient != null) { _warClient.Changed -= OnWarClientChanged; _warClient.Dispose(); _warClient = null; }
            _warWindow?.SetContext(null, 0UL, false);
            _fieldHud?.SetContext(null, 0UL, 0u);
            _leylineBanner?.Hide();
            _client = null; _game = null; _player = 0; _available = _inBattle = false;
        }
        private void OnDisable() { UnbindClient(); HidePanel(); }
        private void OnDestroy()
        {
            _destroyed = true;
            CityPanelRegistry.Unregister(this);
            UnbindClient(); HidePanel();
            _warWindow?.Dispose(); _warWindow = null;
            _fieldHud?.Dispose(); _fieldHud = null;
            _leylineBanner?.Dispose(); _leylineBanner = null;
            _window = null;
            if (Instance == this) Instance = null;
        }
    }
}
