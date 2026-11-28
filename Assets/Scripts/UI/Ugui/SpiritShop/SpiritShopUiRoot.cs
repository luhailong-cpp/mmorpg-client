using MmorpgClient.Game;
using MmorpgClient.Game.SpiritShop;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Jubaozhai;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.SpiritShop
{
    /// <summary>
    /// 官方灵石百宝阁 V1 城内宿主（挂在独立 Overlay Canvas 上，SortingOrder = 185）。
    /// 负责：
    /// - 向 WorldHudNavigation 注册「百宝阁」入口（归入「交易」分类）；
    /// - 绑定 GameClient.SpiritShop 并转发官方商城充值与月卡详情跳转；
    /// - 提供从易容、打磨、制书、打造四大缺料场景直达指定分页/商品卡的公开入口 <see cref="OpenTab"/>。
    /// </summary>
    public sealed class SpiritShopUiRoot : MonoBehaviour
    {
        public static SpiritShopUiRoot Instance { get; private set; }

        public const int CanvasSortingOrder = 185;
        public const string EntryName = "SpiritShopEntry";
        public const string EntryText = "百宝阁";

        private GameClient _game;
        private SpiritShopClient _client;
        private RectTransform _hud;
        private SpiritShopWindow _window;
        private bool _available;

        public SpiritShopWindow Window => _window;
        public SpiritShopClient Client => _client;
        public bool IsPanelVisible => _window != null && _window.IsVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[SpiritShopUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<SpiritShopUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);

            var canvasObject = new GameObject("SpiritShopCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);

            var entry = GameplayUiArt.Button(_hud, EntryText, BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(9),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, TogglePanel, true, fontSize: 32);
            entry.name = EntryName;
            WorldHudNavigation.Register(WorldHudEntry.SpiritShop, (RectTransform)entry.transform, entry,
                entry.GetComponentInChildren<TMP_Text>(true));

            _window = new SpiritShopWindow(design, OpenRechargeMall, OpenAfkPassPanel);
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_hud == null || _window == null) { Unbind(); return; }
            Bind(AppBootstrap.Instance?.GameClient);
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = _game != null && _game.InGame && _game.IsGateReady && !battleLayer
                && (_client?.IsSupported ?? false)
                && MmorpgClient.Game.RewardedAd.ClientDistributionPolicy.Current.IapEnabled;
            _hud.gameObject.SetActive(_available);
            if (!_available && _window.IsVisible) _window.Hide();
            else if (_window.IsVisible) _window.TickCountdown(Time.unscaledTime);
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            Unbind();
            _window?.Dispose();
            _window = null;
            if (Instance == this) Instance = null;
        }

        public void TogglePanel()
        {
            if (IsPanelVisible) HidePanel();
            else ShowPanel();
        }

        public void ShowPanel() => OpenTab(SpiritShopModels.TabStarterPacks, 0);

        /// <summary>直达灵石百宝阁指定分页与推荐商品（供易容/打磨/制书/打造缺料快捷入口调用）。</summary>
        public void OpenTab(uint tabId, uint focusGoodsId = 0)
        {
            if (!_available || _window == null) return;
            CityPanelRegistry.HideOthers(this);
            _window.Show(tabId, focusGoodsId);
        }

        public void HidePanel() => _window?.Hide();

        private void OpenRechargeMall() => JubaozhaiUiRoot.Instance?.OpenOfficialMall();

        private void OpenAfkPassPanel() => AfkPassUiRoot.Instance?.ShowPanel();

        private void Bind(GameClient game)
        {
            if (_game == game) return;
            Unbind();
            _game = game;
            _client = game?.SpiritShop;
            if (_client != null) _client.Purchased += OnSpiritShopPurchased;
            _window?.Bind(_client);
        }

        private void Unbind()
        {
            if (_client != null) _client.Purchased -= OnSpiritShopPurchased;
            _game = null;
            _client = null;
            _window?.Bind(null);
        }

        private void OnSpiritShopPurchased(global::SpiritShopGoodsView goods, uint count)
        {
            if (goods != null
                && (goods.TabId == SpiritShopModels.TabAppearanceAndConvenience || goods.TabId == SpiritShopModels.TabFestival))
            {
                AppearanceUiRoot.Instance?.Client?.RequestInfo(force: true);
            }
        }
    }
}
