#if UNITY_EDITOR || DEVELOPMENT_BUILD
using MmorpgClient.Game;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Pet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.App
{
    /// <summary>开发包内的本地 GM 小面板；普通发布包完全不包含入口或发送逻辑。</summary>
    public sealed class LocalGmCurrencyDebugUi : MonoBehaviour
    {
        private static LocalGmCurrencyDebugUi _instance;
        private LocalGmCurrencyClient _client;
        private GameObject _canvasObject;
        private UiTextButton _add;
        private TMP_Text _notice;
        private LocalGmCurrencyClient.Session _refreshSession;
        private LocalGmCurrencyClient.Session _panelSession;
        private bool _refreshBag, _refreshShop;
        private bool _panelVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (_instance != null) return;
            var root = new GameObject("[LocalGmCurrencyDebugUi]");
            DontDestroyOnLoad(root);
            root.AddComponent<LocalGmCurrencyDebugUi>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _client = new LocalGmCurrencyClient(CurrentSession, session =>
            { _refreshSession = session; _refreshBag = _refreshShop = true; });
            _client.Changed += RefreshText;
            _canvasObject = new GameObject("LocalGmCurrencyCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasObject.transform.SetParent(transform, false);
            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var panel = QdaoUguiFactory.CreateRect("LocalGmCurrencyPanel", _canvasObject.transform, 0, 0, 760, 150);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0, -12);
            panel.gameObject.AddComponent<Image>().color = PetUiStyle.WindowPaper;
            _add = BattleUiWidgets.CreateTextButton("LocalGmAddSilver", panel, 16, 12, 728, 58,
                "本地 GM：增加 10 万银两", 28, PetUiStyle.ActionPlate, PetUiStyle.ActionText);
            _add.Button.onClick.AddListener(() => _client.AddSilver());
            _notice = BattleUiWidgets.CreateWrappedText("LocalGmCurrencyNotice", panel, 16, 78, 728, 62,
                _client.Notice, 23, PetUiStyle.HintText);
            _canvasObject.SetActive(false);
        }

        private static LocalGmCurrencyClient.Session CurrentSession()
        {
            var game = AppBootstrap.Instance?.GameClient;
            return game == null ? default : new LocalGmCurrencyClient.Session(game, game.GateConnectionIdentity,
                game.PlayerId, game.InGame, game.IsGateReady, game.GatewayBaseUrl, game.AssignedGate);
        }

        private void Update()
        {
            _client.SynchronizeSession();
            var current = CurrentSession();
            // 面板只在当前会话中按需打开；断线、退出或切换角色后恢复隐藏。
            if (!current.Allowed || !_panelSession.IsSame(current)) _panelVisible = false;
            _panelSession = current;
            if (current.Allowed && TogglePressed()) _panelVisible = !_panelVisible;
            _canvasObject.SetActive(current.Allowed && _panelVisible);
            if (!current.Allowed) return;
            _add.SetInteractable(_client.CanSend);
            // 只延后只读刷新，绝不延后或重发加钱请求。
            if (!_refreshSession.IsSame(current)) { _refreshBag = _refreshShop = false; return; }
            var game = current.Channel as GameClient;
            if (_refreshBag && game?.Features != null && !game.Features.BusySort && !game.Features.BusyEquip)
            { _refreshBag = false; game.Features.RequestBag(game.Features.RequestedBagType); }
            if (_refreshShop && game?.Pets != null && !game.Pets.Busy && !game.Pets.ShopRequestPending)
            { _refreshShop = false; game.Pets.RequestShop(); }
        }

        private static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current?.f10Key.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F10);
#else
            return false;
#endif
        }

        private void RefreshText()
        {
            if (_notice == null) return;
            _notice.text = _client.Notice;
            _notice.color = _client.Warning ? PetUiStyle.WarnText : PetUiStyle.HintText;
            _add.SetText(_client.Pending ? "等待服务器确认…" : _client.OutcomeUnknown ? "新加 10 万（上次结果未知）" : "本地 GM：增加 10 万银两");
            _add.SetInteractable(_client.CanSend);
        }

        private void OnDestroy()
        {
            if (_client != null) _client.Changed -= RefreshText;
            if (_instance == this) _instance = null;
        }
    }
}
#endif
