using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Game.PlayerFeatures;
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
        public const float EntryX = 68, EntryY = 600;
        private GameClient _game;
        private GuildClient _client;
        private GuildWindow _window;
        private RectTransform _hud;
        private ulong _player;
        private bool _available;
        // 上一帧战斗层是否显示;由显示变为不显示的那一帧 = 一场战斗刚结束。
        private bool _inBattle;

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
            _window = new GuildWindow(design);
            _window.RefreshRequested += () => { if (_available) _client?.Refresh(); };
            _window.RankRequested += page => { if (_available) _client?.Browse(page, ZoneId); };
            _window.CreateRequested += name => { if (_available) _client?.Create(name, ZoneId); };
            // 申请制:排行页不再直接入帮,改为提交 / 撤回申请,由帮主或长老审批。
            _window.ApplyRequested += id => { if (_available) _client?.ApplyToJoin(id); };
            _window.CancelApplicationRequested += id => { if (_available) _client?.CancelApplication(id); };
            _window.RoleRequested += (id, role) => { if (_available) _client?.SetMemberRole(id, role); };
            _window.KickRequested += id => { if (_available) _client?.Kick(id); };
            _window.TransferRequested += id => { if (_available) _client?.TransferLeader(id); };
            _window.ReviewRequested += (id, approve) => { if (_available) _client?.Review(id, approve); };
            _window.ApplicationsRequested += () => { if (_available) _client?.LoadApplications(); };
            _window.AnnouncementRequested += text => { if (_available) _client?.SaveAnnouncement(text); };
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
            _hud.gameObject.SetActive(false);
        }
        private uint ZoneId => AppBootstrap.Instance?.Session?.SelectedZoneId ?? 0;
        private void Update()
        {
            var game = AppBootstrap.Instance?.GameClient;
            if (_game != game)
            {
                if (_client != null) { _client.Changed -= Changed; _client.AssetsChanged -= OnAssetsChanged; _client.Dispose(); }
                _game = game;
                _client = game == null ? null : new GuildClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity);
                if (_client != null) { _client.Changed += Changed; _client.AssetsChanged += OnAssetsChanged; }
                _window.ResetSession(); _window.SetClient(_client);
            }
            _client?.ObserveConnection();
            bool inGame = game != null && game.InGame && game.IsGateReady;
            ulong player = inGame ? game.PlayerId : 0;
            if (_player != player)
            { _player = player; _window.ResetSession(); _client?.Reset(); _window.SetClient(_client); }
            bool inBattle = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            // 战斗层刚收起 = 一场战斗刚结束。打的若是同道历练,活动快照里还留着"历练进行中 / 已开启":结算推送可能丢、
            // 也可能晚到,让客户端把这份快照标成过时,下次进活动页自动重拉(没有在途历练时它什么也不做)。
            if (_inBattle && !inBattle) _client?.NoteBattleEnded();
            _inBattle = inBattle;
            _available = inGame && !inBattle;
            _hud.gameObject.SetActive(_available);
            // 开战(收到 NotifyBattleStart 后战斗层显示)由战斗界面接管:本窗口连同邀请框 / 选人框在这里关掉。
            // 战斗中到达的历练邀请不弹,标志留在 GuildClient 里(有时限,见 TrialInvitePending)。
            if (!_available) { HidePanel(); return; }
            OpenPendingTrialInvite();
            // NotifyGuildChanged 只置排队标志;真正的拉取在这里按帧消费,一帧最多发一个请求,
            // 不让每条推送都触发一次全量 Refresh。窗口关着不拉,Toggle() 打开时已有 Refresh()。
            // Tick 排在后面:停在捐献 / 商店 / 活动页跨过日 / 周切点没有任何事件,由它补判快照过时;DrainQueued 刚发了请求时它什么也不做。
            // 活动视图只在活动页可见时重拉(ShowingActivities):不可见时排队标志留着,进页那一帧再发。
            if (_window.IsVisible) { _client?.DrainQueued(_window.ShowingApplications, _window.ShowingActivities); _window.Tick(); }
            bool typing = IsTyping();
            if (!_window.IsVisible && GameplayInputGate.IsKeyboardBlocked) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) _window.Back();
            else if (!typing && keys.gKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) _window.Back();
            else if (!typing && Input.GetKeyDown(KeyCode.G)) Toggle();
#endif
        }
        public void Toggle()
        {
            if (_window.IsVisible) { HidePanel(); return; }
            if (!_available) return;
            HideOtherPanels();
            _window.Show();
            _client?.Refresh();
        }
        /// <summary>主城的几个大面板互斥:打开帮会窗口前收起别的。</summary>
        private static void HideOtherPanels()
        {
            Team.TeamUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
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
            if (!wasVisible) HideOtherPanels();
            if (!_window.ShowingActivities) _window.Show(GuildPage.Activities);
            // 窗口是这一下才打开的:同 Toggle,先拉一次帮会快照(这次登录可能还没拉过,活动视图要等它)。
            // 活动视图只排队:有快照时下一个空闲帧发出;还没有快照时这一下不生效,快照落定后由窗口进页的自动拉取补上。
            if (!wasVisible) _client.Refresh();
            _client.QueueActivities();
        }
        private static bool IsTyping()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected?.GetComponentInParent<TMP_InputField>()?.isFocused == true;
        }
        public void HidePanel() => _window?.Hide();
        private void Changed() => _window?.SetClient(_client);
        /// <summary>
        /// 捐献扣了货币 / 兑换发了物品:scene 没有余额推送,重拉一次背包(沿用玩家当前看的那个背包类型)。
        /// 背包客户端不在(未进场)就什么也不做,下次打开背包自然会拉。
        /// </summary>
        private static void OnAssetsChanged()
        {
            var features = PlayerFeaturesClient.Instance;
            features?.RequestBag(features.RequestedBagType);
        }
        private void OnDestroy()
        {
            if (_client != null) { _client.Changed -= Changed; _client.AssetsChanged -= OnAssetsChanged; _client.Dispose(); }
            _window?.Hide();
            if (Instance == this) Instance = null;
        }
    }
}
