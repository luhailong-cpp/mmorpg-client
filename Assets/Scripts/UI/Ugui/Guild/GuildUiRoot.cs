using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
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
            _hud.gameObject.SetActive(false);
        }
        private uint ZoneId => AppBootstrap.Instance?.Session?.SelectedZoneId ?? 0;
        private void Update()
        {
            var game = AppBootstrap.Instance?.GameClient;
            if (_game != game)
            {
                if (_client != null) { _client.Changed -= Changed; _client.Dispose(); }
                _game = game;
                _client = game == null ? null : new GuildClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity);
                if (_client != null) _client.Changed += Changed;
                _window.ResetSession(); _window.SetClient(_client);
            }
            _client?.ObserveConnection();
            bool inGame = game != null && game.InGame && game.IsGateReady;
            ulong player = inGame ? game.PlayerId : 0;
            if (_player != player)
            { _player = player; _window.ResetSession(); _client?.Reset(); _window.SetClient(_client); }
            _available = inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            _hud.gameObject.SetActive(_available);
            if (!_available) { HidePanel(); return; }
            // NotifyGuildChanged 只置排队标志;真正的拉取在这里按帧消费,一帧最多发一个请求,
            // 不让每条推送都触发一次全量 Refresh。窗口关着不拉,Toggle() 打开时已有 Refresh()。
            if (_window.IsVisible) _client?.DrainQueued(_window.ShowingApplications);
            var selected = EventSystem.current?.currentSelectedGameObject;
            bool typing = selected?.GetComponentInParent<TMP_InputField>()?.isFocused == true;
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
            Team.TeamUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
            _window.Show();
            _client?.Refresh();
        }
        public void HidePanel() => _window?.Hide();
        private void Changed() => _window?.SetClient(_client);
        private void OnDestroy()
        {
            if (_client != null) { _client.Changed -= Changed; _client.Dispose(); }
            _window?.Hide();
            if (Instance == this) Instance = null;
        }
    }
}
