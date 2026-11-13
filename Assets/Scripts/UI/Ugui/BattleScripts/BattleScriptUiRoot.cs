using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.BattleScripts;
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

namespace MmorpgClient.UI.Ugui.BattleScripts
{
    /// <summary>
    /// 战斗剧本入口(服务端 docs/design/battle-script.md §7)。写法同 RankUiRoot:换连接重建 BattleScriptClient,
    /// 换角 / 退出 / 战斗中关闭窗口并作废快照;打开即刷新列表;窗口开着时 BattleScriptClient.Changed 驱动重画。
    /// 快捷键 Y(J 已给任务面板)。
    /// </summary>
    public sealed class BattleScriptUiRoot : MonoBehaviour
    {
        public static BattleScriptUiRoot Instance { get; private set; }
        // 论剑榜入口右侧并排一格(同 RankUiRoot 贴在帮会右侧的写法,间距 16)
        public const float EntryX = Rank.RankUiRoot.EntryX + BattleUiStyle.HudEntryWidth + 16;
        public const float EntryY = Rank.RankUiRoot.EntryY;
        public BattleScriptWindow Window => _window;
        public BattleScriptClient Client => _client;
        private GameClient _game;
        private BattleScriptClient _client;
        private BattleScriptWindow _window;
        private RectTransform _hud;
        private ulong _player;
        private bool _available;
        private bool _destroyed;
        private bool HasView => !_destroyed && _window?.Root != null && _hud != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[BattleScriptUi]");
            DontDestroyOnLoad(go); go.AddComponent<BattleScriptUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var go = new GameObject("BattleScriptCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            var entry = GameplayUiArt.Button(_hud, "战斗剧本 [Y]", EntryX, EntryY,
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 32);
            entry.name = "BattleScriptEntry";
            WorldHudNavigation.Register(WorldHudEntry.BattleScript, (RectTransform)entry.transform, entry, entry.GetComponentInChildren<TMP_Text>());
            _window = new BattleScriptWindow(design);
            _window.RefreshRequested += () => { if (_available) _client?.Refresh(); };
            _window.DeleteRequested += (script, revision) => { if (_available) _client?.Delete(script, revision); };
            _window.ChooseOwnRequested += (mode, script) => { if (_available) _client?.ChooseOwn(mode, 0, script); };
            _window.ChooseOfficialRequested += (mode, script) => { if (_available) _client?.ChooseOfficial(mode, 0, script); };
            _window.ClearChoiceRequested += mode => { if (_available) _client?.ClearChoice(mode, 0); };
            _window.SubmitRequested += script => { if (_available) _client?.Submit(0, 0, script); };
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!HasView) { UnbindClient(); return; }
            var game = AppBootstrap.Instance?.GameClient;
            if (_game != game)
            {
                if (_client != null) { _client.Changed -= Changed; _client.Dispose(); }
                _game = game;
                _client = game == null ? null : new BattleScriptClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity);
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
            if (IsTyping() || (!_window.IsVisible && GameplayInputGate.IsKeyboardBlocked)) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) HidePanel();
            else if (keys.yKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
            else if (Input.GetKeyDown(KeyCode.Y)) Toggle();
#endif
        }

        public void Toggle()
        {
            if (!HasView) return;
            if (_window.IsVisible) { HidePanel(); return; }
            if (!_available || _client == null) return;
            HideOtherPanels();
            _window.Show(_client);
            _client.Refresh();
        }

        /// <summary>主城的几个大面板互斥:打开战斗剧本前收起别的(列表同 RankUiRoot,再加论剑榜)。</summary>
        private static void HideOtherPanels()
        {
            Rank.RankUiRoot.Instance?.HidePanel();
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

        public void HidePanel() { if (_window?.Root != null) _window.Hide(); }

        private void Changed()
        {
            if (!HasView) { UnbindClient(); return; }
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

        private void UnbindClient()
        {
            if (_client != null) { _client.Changed -= Changed; _client.Dispose(); }
            _client = null; _game = null; _player = 0; _available = false;
        }
        private void OnDisable() { UnbindClient(); HidePanel(); }

        private void OnDestroy()
        {
            _destroyed = true;
            UnbindClient(); HidePanel(); _window = null;
            if (Instance == this) Instance = null;
        }
    }
}
