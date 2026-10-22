using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Rank;
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

namespace MmorpgClient.UI.Ugui.Rank
{
    /// <summary>
    /// 主城左栏论剑榜入口(服务端 docs/design/leaderboard-system.md §4.6)。由真实会话驱动:换连接重建 RankClient,
    /// 换角 / 退出 / 战斗中关闭窗口并作废快照。打开即读第 1 页;窗口开着时 RankClient.Changed 驱动重画。
    /// </summary>
    public sealed class RankUiRoot : MonoBehaviour
    {
        public static RankUiRoot Instance { get; private set; }
        // 左栏帮会入口右侧并排一格(与 TeamFollowUiRoot 贴在组队右侧同一写法,间距 16)。左栏 y 288–468 是任务追踪面板
        // (GameplayUiRoot 的 TrackedMission,追踪任务时显示),496–992 已排满组队 / 帮会 / 聚宝斋 / 仙笺 / 仙友会;
        // 帮会右侧这一格 x 414–744、y 600–680 没有别的控件。
        public const float EntryX = Guild.GuildUiRoot.EntryX + BattleUiStyle.HudEntryWidth + 16;
        public const float EntryY = Guild.GuildUiRoot.EntryY;
        public RankWindow Window => _window;
        public RankClient Client => _client;
        private GameClient _game;
        private RankClient _client;
        private RankWindow _window;
        private RectTransform _hud;
        private ulong _player;
        private bool _available;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[RankUi]");
            DontDestroyOnLoad(go); go.AddComponent<RankUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var go = new GameObject("RankCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 186;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", go.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            var entry = GameplayUiArt.Button(_hud, "论剑榜 [K]", EntryX, EntryY,
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 32);
            entry.name = "RankEntry";
            WorldHudNavigation.Register(WorldHudEntry.Rank, (RectTransform)entry.transform, entry, entry.GetComponentInChildren<TMP_Text>());
            _window = new RankWindow(design);
            _window.PageRequested += page => { if (_available) _client?.Load(page); };
            _window.RefreshRequested += () => { if (_available) _client?.Refresh(); };
            _window.AroundRequested += () =>
            {
                if (_available) _client?.ReadAround(_client.Page?.Board ?? _client.Around?.Board ?? RankClient.LatestBoard(), 2);
            };
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            var game = AppBootstrap.Instance?.GameClient;
            if (_game != game)
            {
                if (_client != null) { _client.Changed -= Changed; _client.Dispose(); }
                _game = game;
                _client = game == null ? null
                    : new RankClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity, () => Time.realtimeSinceStartup);
                if (_client != null) _client.Changed += Changed;
                _window.Hide();
            }
            _client?.ObserveConnection();
            bool inGame = game != null && game.InGame && game.IsGateReady;
            ulong player = inGame ? game.PlayerId : 0;
            if (_player != player) { _player = player; _client?.Reset(); _window.Hide(); }
            _available = inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            _hud.gameObject.SetActive(_available);
            if (!_available) { HidePanel(); return; }
            // 排队的翻页到点补发;窗口关着时不补(重新打开会从第 1 页读起)。
            if (_window.IsVisible) _client?.Tick();
            if (IsTyping() || (!_window.IsVisible && GameplayInputGate.IsKeyboardBlocked)) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) HidePanel();
            else if (keys.kKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
            else if (Input.GetKeyDown(KeyCode.K)) Toggle();
#endif
        }

        public void Toggle()
        {
            if (_window.IsVisible) { HidePanel(); return; }
            if (!_available || _client == null) return;
            HideOtherPanels();
            _window.Show(_client);
            _client.Refresh();
        }

        /// <summary>主城的几个大面板互斥:打开论剑榜前收起别的(列表同 MysteryMerchantUiRoot,含分线列表)。</summary>
        private static void HideOtherPanels()
        {
            MysteryMerchantUiRoot.Instance?.HidePanel();
            SceneChannelUiRoot.Instance?.HidePanel();
            Team.TeamUiRoot.Instance?.HidePanel();
            Guild.GuildUiRoot.Instance?.HidePanel();
            Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();
            Social.SocialUiRoot.Instance?.HidePanel();
            Mail.MailUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
            Skills.SkillUiRoot.Instance?.HidePanel();
        }

        public void HidePanel() => _window?.Hide();

        private void Changed()
        {
            if (_window != null && _window.IsVisible) _window.Render();
        }

        private static bool IsTyping()
        {
            var current = EventSystem.current;
            if (current == null) return false;
            var selected = current.currentSelectedGameObject;
            // Unity 已销毁对象需用其重载的空值判断,?. 仍会访问失效的原生对象。
            if (selected == null) return false;
            return selected?.GetComponentInParent<TMP_InputField>()?.isFocused == true
                || selected?.GetComponentInParent<InputField>()?.isFocused == true;
        }

        private void OnDisable() => HidePanel();

        private void OnDestroy()
        {
            if (_client != null) { _client.Changed -= Changed; _client.Dispose(); }
            _window?.Hide();
            if (Instance == this) Instance = null;
        }
    }
}
