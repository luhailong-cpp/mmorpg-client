using MmorpgClient.Game;
using MmorpgClient.Game.AfkPass;
using MmorpgClient.Game.Battle;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 挂机月卡的宿主(服务端设计 docs/design/afk-monthly-card.md §7)。网络真相全在 <see cref="AfkPassClient"/>;这里只做三件事:
    ///  - 跟着 GameClient 实例创建 / 释放数据层,把入场通知、断线转发给它;快到期 / 已到期提醒用飘字显示;
    ///  - 城内入口按钮「挂机月卡」(经 <see cref="WorldHudNavigation"/> 归进「历练」分类);
    ///  - 面板:剩余时长、价格与叠加上限、持有灵石、开通 / 续费(两次点击确认,一次双击不算)。
    ///
    /// 视觉:这块界面没有效果图,只做功能版。按客户端 AGENTS.md「UI 原图还原验收」,视觉验收是未解决项,不宣称与原图一致。
    /// </summary>
    public sealed class AfkPassUiRoot : MonoBehaviour
    {
        public static AfkPassUiRoot Instance { get; private set; }
        public AfkPassClient Client => _client;
        public bool IsPanelVisible => _window != null && _window.gameObject.activeSelf;

        /// <summary>主画布排序:在背包任务活动(180)之上、神秘商人(183)之下。</summary>
        public const int CanvasSortingOrder = 182;
        // 测试与离线截图按名字找控件。
        public const string EntryName = "AfkPassEntry";
        public const string BuyButtonName = "AfkPassBuy";
        public const string CloseButtonName = "AfkPassClose";
        public const string EntryText = "挂机月卡";
        /// <summary>第一次点击之后多久内的第二次点击算确认;超时要重新点。</summary>
        public const float ConfirmWindowSeconds = 5f;
        /// <summary>第一次点击之后至少过这么久,第二次点击才算确认:一次双击不该扣钱。</summary>
        public const float ConfirmMinIntervalSeconds = 0.4f;

        private const float FrameWidth = 1080, FrameHeight = 640;
        private static readonly Color ErrorInk = QdaoUguiTheme.Html("#9A442D");

        private GameClient _game;
        private AfkPassClient _client;
        private RectTransform _hud;
        private RectTransform _window;
        private Button _entry, _buy;
        private TMP_Text _state, _price, _balance, _status, _buyLabel;
        private bool _available;
        private float _armedAt = float.NegativeInfinity;
        private uint _armedCurrency;
        private ulong _armedPrice;
        // 按钮此刻显示的是「确认开通」:时限到了要收回来。
        private bool _confirmShown;
        // 状态行上一次显示的文字:倒计时每帧在走,只有文字变了才重写。
        private string _stateShown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[AfkPassUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<AfkPassUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);
            var canvasObject = new GameObject("AfkPassCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            // 入口的位置由 WorldHudNavigation 接管(归进分类面板);这里给的坐标只是注册前的初值。
            _entry = GameplayUiArt.Button(_hud, EntryText, BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(8),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, TogglePanel, true, fontSize: 32);
            _entry.name = EntryName;
            WorldHudNavigation.Register(WorldHudEntry.AfkPass, (RectTransform)_entry.transform, _entry,
                _entry.GetComponentInChildren<TMP_Text>(true));
            BuildWindow(design);
            _hud.gameObject.SetActive(false);
        }

        private void BuildWindow(RectTransform design)
        {
            _window = QdaoUguiFactory.CreateStretch("AfkPassWindow", design, Vector4.zero);
            var shade = _window.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f);
            shade.raycastTarget = true;
            _window.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("AfkPassFrame", _window, FrameWidth, FrameHeight);
            GameplayUiArt.Art(frame, "main_frame", 0, 0, FrameWidth, FrameHeight);
            GameplayUiArt.Text(frame, EntryText, 80, 44, 600, 64, 40);
            _state = GameplayUiArt.Text(frame, "", 80, 136, 920, 60, 36);
            _price = GameplayUiArt.Text(frame, "", 80, 206, 920, 48, 28, GameplayUiArt.Muted);
            _balance = GameplayUiArt.Text(frame, "", 80, 260, 920, 48, 28, GameplayUiArt.Muted);
            GameplayUiArt.Text(frame, AfkPassModels.HostingDescription, 80, 320, 920, 120, 26, GameplayUiArt.Muted, wrap: true);
            _status = GameplayUiArt.Text(frame, "", 80, 452, 920, 48, 26, GameplayUiArt.Muted);
            var close = GameplayUiArt.Button(frame, "关闭", 80, 524, 240, 80, HidePanel);
            close.name = CloseButtonName;
            _buy = GameplayUiArt.Button(frame, "开通", 600, 524, 400, 80, HandleBuyClicked, true);
            _buy.name = BuyButtonName;
            _buyLabel = _buy.GetComponentInChildren<TMP_Text>(true);
            _window.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_window == null || _hud == null) { Unbind(); return; }
            Bind(AppBootstrap.Instance?.GameClient);
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = _game != null && _game.InGame && _game.IsGateReady && !battleLayer && (_client?.IsSupported ?? false);
            _client?.Tick();
            _hud.gameObject.SetActive(_available);
            if (!_available)
            {
                HidePanel();
                return;
            }
            if (!IsPanelVisible) return;
            UpdateStateText();
            if (_confirmShown && Time.unscaledTime - _armedAt > ConfirmWindowSeconds) Render();   // 确认时限到了:收回按钮文字
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            Unbind();
            if (Instance == this) Instance = null;
        }

        // ── 面板 ────────────────────────────────────────────────────────────

        public void TogglePanel()
        {
            if (IsPanelVisible) HidePanel();
            else ShowPanel();
        }

        public void ShowPanel()
        {
            if (!_available || _window == null) return;
            CityPanelRegistry.HideOthers(this);
            _armedAt = float.NegativeInfinity;
            _window.gameObject.SetActive(true);
            _client?.RequestInfo();   // 打开时问一次:余额与剩余时长以服务端此刻为准
            Render();
        }

        public void HidePanel()
        {
            _armedAt = float.NegativeInfinity;
            if (_window != null) _window.gameObject.SetActive(false);
        }

        private void HandleBuyClicked()
        {
            if (_client == null) return;
            var info = _client.Info;
            if (info == null || !_client.ForSale) return;
            float now = Time.unscaledTime;
            if (now - _armedAt > ConfirmWindowSeconds || info.PriceCurrencyType != _armedCurrency || info.Price != _armedPrice)
            {
                // 第一次点击:只亮出「确认开通(价格)」,不扣钱;记下报价,期间热更涨价则撤回确认。
                _armedAt = now;
                _armedCurrency = info.PriceCurrencyType;
                _armedPrice = info.Price;
                Render();
                return;
            }
            if (now - _armedAt < ConfirmMinIntervalSeconds) return;   // 双击的第二下不算确认
            _armedAt = float.NegativeInfinity;
            _client.Buy();
            Render();
        }

        private void Render()
        {
            if (_window == null) return;
            var info = _client?.Info;
            _stateShown = null;
            UpdateStateText();
            if (info == null)
            {
                _price.text = "";
                _balance.text = "";
            }
            else if (_client.ForSale)
            {
                string currency = AfkPassModels.CurrencyName(info.PriceCurrencyType);
                _price.text = $"{info.Price} {currency} / {info.PeriodDays} 天，可叠加，剩余最多 {info.MaxRemainingDays} 天";
                _balance.text = $"持有{currency}：{info.Balance}";
            }
            else
            {
                _price.text = AfkPassModels.NotForSaleText;
                _balance.text = "";
            }
            string status = _client?.Status ?? "";
            _status.text = status;
            _status.color = (_client?.StatusIsError ?? false) ? ErrorInk : GameplayUiArt.Muted;

            bool pending = _client != null && _client.BuyPending;
            bool canBuy = _client != null && _client.Ready && _client.ForSale && !pending && !_client.RequiresReconnect;
            if (info == null || info.PriceCurrencyType != _armedCurrency || info.Price != _armedPrice)
                _armedAt = float.NegativeInfinity;
            _buy.interactable = canBuy;
            _confirmShown = canBuy && info != null && Time.unscaledTime - _armedAt <= ConfirmWindowSeconds;
            _buyLabel.text = pending ? "开通中…"
                : _confirmShown ? $"确认开通（{info.Price} {AfkPassModels.CurrencyName(info.PriceCurrencyType)}）"
                : _client != null && _client.Active ? "续费" : "开通";
        }

        // 只写状态行的文字(倒计时),不碰别的控件。
        private void UpdateStateText()
        {
            if (_state == null) return;
            string text = _client?.Info == null
                ? (_client?.InfoPending ?? false ? "正在查询挂机月卡…" : "")
                : _client.RemainingText;
            if (text == _stateShown) return;
            _stateShown = text;
            _state.text = text;
        }

        // ── 绑定 ────────────────────────────────────────────────────────────

        private void Bind(GameClient game)
        {
            if (ReferenceEquals(_game, game)) return;
            Unbind();
            _game = game;
            if (game != null)
            {
                _client = new AfkPassClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                    () => Time.realtimeSinceStartup);
                _client.Changed += Render;
                _client.Reminder += HandleReminder;
                game.OnSceneEntered += HandleSceneEntered;
                game.OnDisconnected += HandleDisconnected;
                // 本组件晚于进场才出现(或 GameClient 中途换了实例)时,入场通知已经错过,自己问一次。
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
                _client.Reminder -= HandleReminder;
                _client.Dispose();
                _client = null;
            }
            HidePanel();
        }

        private void HandleSceneEntered(SceneInfoComp scene) => _client?.HandleSceneEntered();

        private void HandleDisconnected()
        {
            _client?.HandleDisconnected();
            HidePanel();
        }

        private static void HandleReminder(string text) => BattleUiRoot.Instance?.ShowToast(text);
    }
}
