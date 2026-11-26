using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Jubaozhai;
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

namespace MmorpgClient.UI.Ugui.Jubaozhai
{
    /// <summary>
    /// Real-session city entry. 正式商品只经 JubaozhaiClient(服务端分页,聚宝斋 P1)写入窗口 State;
    /// 本根节点从不装载演示数据。
    /// </summary>
    public sealed class JubaozhaiUiRoot : MonoBehaviour
    {
        public static JubaozhaiUiRoot Instance { get; private set; }
        public JubaozhaiWindow Window => _window;
        public JubaozhaiState State => _window?.State;
        public JubaozhaiClient Client => _client;
        public const float EntryX = 68, EntryY = 704;
        private GameClient _game;
        private JubaozhaiClient _client;
        private JubaozhaiWindow _window;
        private CommerceClient _commerce;
        private PaymentClient _payment;
        private CommerceWindow _commerceWindow;
        private RectTransform _hud;
        private ulong _player;
        private bool _available;
        private bool HasView => _hud != null && _window?.Root != null && _commerceWindow?.Root != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var root = new GameObject("[JubaozhaiUi]");
            DontDestroyOnLoad(root);
            root.AddComponent<JubaozhaiUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);
            var canvasObject = new GameObject("JubaozhaiCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 188;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            var entry = GameplayUiArt.Button(_hud, "聚宝斋 [U]", EntryX, EntryY,
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 32);
            entry.name = "JubaozhaiEntry";
            WorldHudNavigation.Register(WorldHudEntry.Trade, (RectTransform)entry.transform, entry, entry.GetComponentInChildren<TMP_Text>());
            _window = new JubaozhaiWindow(design);
            _commerceWindow = new CommerceWindow(design, () => _game);
            _window.CommerceRequested += ShowCommerce;
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!HasView) { UnbindGame(); return; }
            var game = AppBootstrap.Instance?.GameClient;
            if (_game != game)
            {
                if (_game != null) _game.OnDisconnected -= ResetSession;
                DisposeClient();
                ResetSession();
                _game = game;
                if (_game != null)
                {
                    _game.OnDisconnected += ResetSession;
                    // 服务端分页适配器与窗口共用同一个 State;连接身份取 Gate 对象,静默换 Gate 时解除隔离。
                    _client = new JubaozhaiClient(new GameClientBattleTransport(game), _window.State,
                        () => game.GateConnectionIdentity);
                    _commerce = new CommerceClient(new GameClientBattleTransport(game), new CommerceFileRecoveryStore(
                        System.IO.Path.Combine(Application.persistentDataPath, "commerce-recovery-v1"), AppBootstrap.Instance.Session.GatewayBaseUrl,
                        () => game.OwnerRevision),
                        () => game.GateConnectionIdentity);
                    _commerce.AssetRefreshRequired += RefreshAssets;
                    _payment = new PaymentClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity);
                    _payment.Settled += OnPaymentSettled;
                    _commerceWindow.Bind(_commerce, _payment);
                }
            }
            _client?.ObserveConnection();
            _commerce?.ObserveConnection();
            _payment?.ObserveConnection();
            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            ulong player = inGame ? _game.PlayerId : 0;
            if (_player != player) { ResetSession(); _client?.Reset(); _payment?.Reset(); _player = player; }
            if (inGame && player != 0) _payment?.Tick(Time.unscaledTimeAsDouble); // 仅进场就绪后按间隔查服务端或补交商店凭证
            // 战斗层显示时只清窗口不 Reset client:在途请求被 Reset 会隔离到下次真实断线,
            // 而 State.Reset 已退出服务端模式,迟到回包会按版本丢弃。
            bool available = inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            if (_available && !available) _window.ResetSession();
            _available = available;
            _hud.gameObject.SetActive(available);
            if (!available) { HidePanel(); return; }
            _window.Tick(Time.unscaledTime);
            if (IsTyping() || (!_window.IsVisible && !_commerceWindow.IsVisible && GameplayInputGate.IsKeyboardBlocked)) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) { if (_commerceWindow.IsVisible) _commerceWindow.Back(); else _window.Back(); }
            else if (keys.uKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) { if (_commerceWindow.IsVisible) _commerceWindow.Back(); else _window.Back(); }
            else if (Input.GetKeyDown(KeyCode.U)) Toggle();
#endif
        }

        public void Toggle()
        {
            if (!HasView) return;
            if (_commerceWindow.IsVisible) { HidePanel(); return; }
            if (_window.IsVisible) { HidePanel(); return; }
            if (!_available) return;
            CityPanelRegistry.HideOthers(this);
            _window.Show();
            _client?.Open();
        }

        private void ShowCommerce()
        {
            if (!HasView || !_available) return;
            _window.Hide();
            _commerceWindow.Show();
        }

        /// <summary>快捷跳转到聚宝斋官方商城页签（供角色形象切换面板购买 10 元角色形象卡使用）。</summary>
        public void OpenOfficialMall()
        {
            OpenOfficialMallSubTab(OfficialShopTab.LightCard);
        }

        /// <summary>快捷跳转到聚宝斋官方商城指定子页签（如衣柜灵宠/特效直跳「永久衣柜」）。</summary>
        public void OpenOfficialMallSubTab(OfficialShopTab subTab)
        {
            if (!HasView || !_available) return;
            CityPanelRegistry.HideOthers(this);
            _window.Hide();
            _commerceWindow.ShowOfficialShopSubTab((int)subTab);
        }

        /// <summary>快捷打开指定卖家的店铺信用档案（如主城宗师代工摊位点击查看大匠信用）。</summary>
        public void OpenSellerCredit(ulong sellerPlayerId, System.Action onBack = null)
        {
            if (!HasView || !_available || sellerPlayerId == 0UL) return;
            CityPanelRegistry.HideOthers(this);
            _window.Hide();
            _commerceWindow.ShowSellerCredit(sellerPlayerId, onBack);
        }

        /// <summary>查询服务端官方商城是否已返回该 SKU 且当前有可售库存。</summary>
        public bool HasSellableOfficialSku(ulong skuId) => _commerce != null && _commerce.HasSellableOfficialSku(skuId);

        /// <summary>在衣柜打开时按需同步官方商城在售 SKU 列表（不弹出商城窗）。</summary>
        public void RequestOfficialSkusRefresh()
        {
            if (!_available || _commerce == null || _commerce.Busy || _commerce.RequiresReconnect) return;
            _commerce.ListSkus(0ul);
        }
        private void OnPaymentSettled(string _) => RefreshAssets();
        private void RefreshAssets()
        {
            if (_game == null || !_game.InGame || !_game.IsGateReady) return;
            _game.Features.RequestBag();
            _game.Pets.RequestList();
            _game.SkillCraft?.RefreshState();
            MmorpgClient.UI.Ugui.Gameplay.AppearanceUiRoot.Instance?.Client?.RequestInfo(force: true);
        }
        public void HidePanel() { _window?.Hide(); _commerceWindow?.Hide(); }
        private void DisposeClient()
        {
            _client?.Dispose();
            _client = null;
            if (_commerce != null) _commerce.AssetRefreshRequired -= RefreshAssets;
            if (_payment != null) _payment.Settled -= OnPaymentSettled;
            _commerceWindow?.Bind(null);
            _commerce?.Dispose(); _commerce = null;
            _payment?.Dispose(); _payment = null;
        }
        private void ResetSession()
        {
            _player = 0;
            _available = false;
            _window?.ResetSession();
            _commerceWindow?.Hide();
            if (_hud != null) _hud.gameObject.SetActive(false);
        }
        public static bool IsTyping() => CityPanelRegistry.IsTyping();
        private void UnbindGame()
        {
            if (_game != null) _game.OnDisconnected -= ResetSession;
            _game = null;
            _available = false;
            DisposeClient();
        }
        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            UnbindGame();
            _window?.Dispose();
            _commerceWindow?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}

