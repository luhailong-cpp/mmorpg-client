using System;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.RewardedAd;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 激励视频广告四槽位统一 uGUI 宿主（Slot 1 绑银补给 / Slot 2 五行幻境奇策重随 / Slot 3 衣柜 1 小时试穿 / Slot 4 神秘商人换货）。
    ///
    /// 零拒审硬门禁：
    /// 1. 仅当 ClientDistributionPolicy.Current.RewardedAdAllowed 且服务端开关 + 宿主 + 素材 + 余量 + 非战斗全部满足时才显示入口，否则整块隐藏；
    /// 2. 二次确认弹窗严格复用 RewardedAdModel 中立合规文案及同等显眼的确认/关闭按钮，绝不夹带诱导或站外引流字样。
    /// </summary>
    public sealed class RewardedAdUiRoot : MonoBehaviour
    {
        public static RewardedAdUiRoot Instance { get; private set; }
        public RewardedAdClient Client => _client;

        public const int CanvasSortingOrder = 192;

        private GameClient _game;
        private RewardedAdClient _client;
        private IRewardedAdBridge _bridge = NullRewardedAdBridge.Instance;
        private string _adUnitId = string.Empty;

        private RectTransform _hud;
        private Button _slot1HudButton;
        private GameObject _confirmOverlay;
        private TMP_Text _confirmTitle;
        private TMP_Text _confirmBody;
        private TMP_Text _confirmBalance;
        private TMP_Text _confirmStatus;
        private Button _confirmWatchBtn;
        private TMP_Text _confirmWatchLabel;

        private uint _pendingSlotId;
        private uint _pendingParamU32;
        private string _pendingParamStr = string.Empty;
        private Action<bool> _pendingOnCompleted;
        private bool _available;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[RewardedAdUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<RewardedAdUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            var canvasObject = new GameObject("RewardedAdCanvas", typeof(RectTransform), typeof(Canvas),
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

            // Slot 1 独立主城悬浮入口（无版号小游戏百宝阁隐藏时依然可用；首审默认 enabled=0 时整块隐藏）
            _slot1HudButton = GameplayUiArt.Button(_hud,
                $"{RewardedAdModel.VideoBadgeText} {RewardedAdModel.EntryButtonText}",
                2010f, 134f, 300f, 54f,
                () => OpenConfirmForSlot(RewardedAdModel.DefaultSlotId),
                primary: false, fontSize: 20f);
            _slot1HudButton.name = "RewardedAdSlot1HudBtn";
            AttachVideoBadge(_slot1HudButton.transform, 28f);
            _slot1HudButton.gameObject.SetActive(false);

            BuildConfirmOverlay(design);
            _hud.gameObject.SetActive(false);
        }

        private void BuildConfirmOverlay(RectTransform design)
        {
            var overlay = QdaoUguiFactory.CreateStretch("RewardedAdConfirmOverlay", design, Vector4.zero);
            var shade = overlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0.025f, 0.10f, 0.08f, 0.72f);
            shade.raycastTarget = true;
            overlay.gameObject.AddComponent<GameplayInputBlocker>();

            var frame = QdaoUguiFactory.CreateCenteredRect("RewardedAdConfirmFrame", overlay, 880f, 500f);
            GameplayUiArt.Art(frame, "main_frame", 0f, 0f, 880f, 500f);

            var badgeSprite = AppearanceUiArt.LoadRewardedVideoBadge();
            if (badgeSprite != null)
            {
                var badgeImg = QdaoUguiFactory.CreateImage("RewardedAdDialogBadge", frame, 58f, 34f, 44f, 44f, badgeSprite);
                badgeImg.preserveAspect = true;
                badgeImg.raycastTarget = false;
            }

            _confirmTitle = GameplayUiArt.Text(frame, $"{RewardedAdModel.VideoBadgeText} {RewardedAdModel.DialogTitleText}",
                110f, 32f, 660f, 52f, 34f, GameplayUiArt.Ink, alignment: TextAlignmentOptions.Center);
            _confirmTitle.fontStyle = FontStyles.Bold;
            _confirmTitle.raycastTarget = false;

            _confirmBody = GameplayUiArt.Text(frame, string.Empty, 64f, 108f, 752f, 160f, 25f, GameplayUiArt.Ink, wrap: true);
            _confirmBody.raycastTarget = false;

            _confirmBalance = GameplayUiArt.Text(frame, string.Empty, 64f, 282f, 752f, 40f, 23f, GameplayUiArt.Muted);
            _confirmBalance.raycastTarget = false;

            _confirmStatus = GameplayUiArt.Text(frame, string.Empty, 64f, 328f, 752f, 40f, 22f, GameplayUiArt.Muted);
            _confirmStatus.raycastTarget = false;

            var cancelBtn = GameplayUiArt.Button(frame, RewardedAdModel.CancelButtonText,
                96f, 392f, 310f, 70f, CloseConfirmDialog, primary: false, fontSize: 27f);
            cancelBtn.name = "RewardedAdConfirmCancel";

            _confirmWatchBtn = GameplayUiArt.Button(frame, RewardedAdModel.ConfirmWatchButtonText,
                474f, 392f, 310f, 70f, HandleConfirmWatchClicked, primary: true, fontSize: 27f);
            _confirmWatchBtn.name = "RewardedAdConfirmWatch";
            _confirmWatchLabel = _confirmWatchBtn.GetComponentInChildren<TMP_Text>(true);

            _confirmOverlay = overlay.gameObject;
            _confirmOverlay.SetActive(false);
        }

        /// <summary>供宿主平台（微信/抖音/海外移动端）注入实际广告桥与广告位 ID。</summary>
        public void ConfigureRuntimeBridge(IRewardedAdBridge bridge, string adUnitId)
        {
            _bridge = bridge ?? NullRewardedAdBridge.Instance;
            _adUnitId = adUnitId ?? string.Empty;
            if (_game != null)
            {
                var currentGame = _game;
                Unbind();
                Bind(currentGame);
            }
        }

        public bool CanShowSlot(uint slotId, out RewardedAdViewState view)
        {
            if (_client == null || !_available || !ClientDistributionPolicy.Current.RewardedAdAllowed)
            {
                view = default;
                return false;
            }
            view = _client.Snapshot(slotId);
            return view.ShouldShowEntry && view.CanClickWatch;
        }

        public void OpenConfirmForSlot(uint slotId, uint targetParamU32 = 0u, string targetParamStr = "", Action<bool> onCompleted = null)
        {
            if (!CanShowSlot(slotId, out var view) || _confirmOverlay == null)
            {
                CloseConfirmDialog();
                return;
            }
            _pendingSlotId = slotId;
            _pendingParamU32 = targetParamU32;
            _pendingParamStr = targetParamStr ?? string.Empty;
            _pendingOnCompleted = onCompleted;
            _confirmOverlay.SetActive(true);
            RefreshConfirmDialog(view);
        }

        public void CloseConfirmDialog()
        {
            _pendingSlotId = 0u;
            _pendingParamU32 = 0u;
            _pendingParamStr = string.Empty;
            _pendingOnCompleted = null;
            if (_confirmOverlay != null) _confirmOverlay.SetActive(false);
        }

        public static Image AttachVideoBadge(UnityEngine.Transform buttonRoot, float size = 28f)
        {
            if (buttonRoot == null) return null;
            var sprite = AppearanceUiArt.LoadRewardedVideoBadge();
            if (sprite == null) return null;
            var img = QdaoUguiFactory.CreateImage("RewardedVideoBadgeIcon", buttonRoot, 10f, 10f, size, size, sprite);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(10f, 0f);
            rt.sizeDelta = new Vector2(size, size);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        private void HandleConfirmWatchClicked()
        {
            if (_client == null || _pendingSlotId == 0u) return;
            uint slotId = _pendingSlotId;
            uint paramU32 = _pendingParamU32;
            string paramStr = _pendingParamStr;
            var onCompleted = _pendingOnCompleted;
            CloseConfirmDialog();
            _client.StartWatch(slotId, paramU32, paramStr, ok =>
            {
                if (!string.IsNullOrEmpty(_client?.Status))
                    BattleUiRoot.Instance?.ShowToast(_client.Status, _client.StatusIsError);
                onCompleted?.Invoke(ok);
            });
        }

        private void RefreshConfirmDialog(RewardedAdViewState view)
        {
            if (_confirmOverlay == null || !_confirmOverlay.activeSelf) return;
            if (_confirmTitle != null)
                _confirmTitle.text = $"{RewardedAdModel.VideoBadgeText} {RewardedAdModel.DialogTitleText}";
            if (_confirmBody != null)
                _confirmBody.text = RewardedAdModel.BuildDialogBodyForKind(
                    view.RewardKind, view.RewardAmount, view.CurrencyType, view.RemainingToday, view.DailyLimit);
            if (_confirmBalance != null)
                _confirmBalance.text = view.RewardKind == RewardedAdModel.RewardKindBindGold
                    ? RewardedAdModel.BuildBalanceLine(view.BindGoldBalance, view.GoldBalance)
                    : string.Empty;
            if (_confirmStatus != null)
                _confirmStatus.text = _client?.Status ?? string.Empty;
            if (_confirmWatchBtn != null)
                _confirmWatchBtn.interactable = view.CanClickWatch;
            if (_confirmWatchLabel != null)
                _confirmWatchLabel.text = RewardedAdModel.BuildCooldownButtonText(view.CooldownRemainingSec);
        }

        private void Update()
        {
            if (_hud == null) { Unbind(); return; }
            Bind(AppBootstrap.Instance?.GameClient);
            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = inGame && !battleLayer && (_client?.IsSupported ?? false);
            if (_client != null)
            {
                _client.InBattle = battleLayer;
                _client.Tick();
            }
            bool showSlot1 = CanShowSlot(RewardedAdModel.DefaultSlotId, out _);
            _hud.gameObject.SetActive(showSlot1);
            if (_slot1HudButton != null) _slot1HudButton.gameObject.SetActive(showSlot1);
            if (!_available) CloseConfirmDialog();
        }

        private void Bind(GameClient game)
        {
            if (ReferenceEquals(_game, game)) return;
            Unbind();
            _game = game;
            if (game != null)
            {
                _client = new RewardedAdClient(
                    new GameClientBattleTransport(game),
                    () => game.GateConnectionIdentity,
                    () => Time.realtimeSinceStartup,
                    _bridge);
                if (!string.IsNullOrWhiteSpace(_adUnitId)) _client.ConfigureAdUnitId(_adUnitId);
                _client.Changed += HandleClientChanged;
                game.OnSceneEntered += HandleSceneEntered;
                game.OnDisconnected += HandleDisconnected;
                if (game.InGame && game.IsGateReady && game.CurrentSceneId != 0) _client.RequestInfo();
            }
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
                _client.Changed -= HandleClientChanged;
                _client.Dispose();
                _client = null;
            }
            CloseConfirmDialog();
        }

        private void HandleSceneEntered(SceneInfoComp _) => _client?.RequestInfo();

        private void HandleDisconnected() => CloseConfirmDialog();

        private void HandleClientChanged()
        {
            if (_confirmOverlay != null && _confirmOverlay.activeSelf && _pendingSlotId != 0u)
            {
                if (CanShowSlot(_pendingSlotId, out var view)) RefreshConfirmDialog(view);
                else CloseConfirmDialog();
            }
        }

        private void OnDestroy()
        {
            Unbind();
            if (Instance == this) Instance = null;
        }
    }
}
