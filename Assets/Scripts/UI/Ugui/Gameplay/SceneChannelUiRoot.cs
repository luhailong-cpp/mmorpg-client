using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 切线入口:右上角「N线」角标 + 线路面板的宿主(服务端设计 docs/design/world-channel-switch.md §7)。
    /// 网络真相全在 <see cref="SceneChannelClient"/>;这里只做三件事:
    ///  - 跟着 GameClient 实例创建 / 释放数据层,并把入场通知、服务端提示、断线转发给它
    ///    (只订阅 GameClient 已暴露的事件,不对 23 / 79 / 124 再注册处理器:那是覆盖语义);
    ///  - 把宿主才知道的状态喂给数据层:战斗或观战中、跨区传送在途、在队伍里且不是队长;
    ///  - 角标、面板、快捷键 L。面板只发意图,能不能切由数据层按同一条规则裁决。
    /// </summary>
    public sealed class SceneChannelUiRoot : MonoBehaviour
    {
        public static SceneChannelUiRoot Instance { get; private set; }
        public SceneChannelWindow Window => _window;
        public SceneChannelClient Client => _client;

        /// <summary>主画布排序:在背包任务活动(180)之上、地图窗(185)之下。</summary>
        public const int CanvasSortingOrder = 184;
        public const string EntryName = "SceneChannelEntry";
        public const string SwitchingEntryText = "切换中…";
        /// <summary>角标线名后面的快捷键提示,写法与其它 HUD 入口一致(「组队 [T]」「帮会 [G]」)。</summary>
        public const string EntryHotkeyHint = " [L]";
        /// <summary>
        /// 面板关着、而角标还显示不出线号(没有目录,或目录里找不到所在线)时,第一次补问前等这么久。
        /// 进场时只拉一次目录;那一刻目录恰好没发布(scene_manager 换主、Redis 抖动)或新线还没进目录的话,
        /// 不补问,角标会一直缺到下次换场景,而角标是这个功能唯一看得见的入口。
        /// 15 秒一发,离 43 号消息的限流配额很远(数据层另有 2.5 秒最小间隔兜底)。
        /// </summary>
        public const float BackgroundRetryIntervalSeconds = 15f;
        /// <summary>
        /// 每次进场后最多补问几次,间隔逐次加倍(15 / 30 / 60 秒,见 <see cref="BackgroundRetryDelay"/>);
        /// 用完就停,等下次进场或玩家打开面板再拉。不设上限的话,目录长期拿不到(运维关掉了目录发布、
        /// 这张图没有任何线)时每个在线客户端都会一直每 15 秒发一条 43:白费服务端一次读,还会让它的
        /// 挂机判定(30 秒没有客户端消息)对站着不动的玩家永远不成立。
        /// 副本 / 镜像本来就没有分线(数据层的 DirectoryApplicable 为 false),一次也不问。
        /// </summary>
        public const int BackgroundRetryLimit = 3;

        private GameClient _game;
        private SceneChannelClient _client;
        private RectTransform _hud;
        private Button _entry;
        private TMP_Text _entryLabel;
        private SceneChannelWindow _window;
        private bool _available;
        private bool _wasSwitchPending;
        // 自己发起的切线抵达的线号,只在「已抵达、这个场景的目录还没到」时用来顶住角标(列线因最小间隔排队时最长约 2.5 秒)。
        // 它来自入场通知确认过的那次切线,不是本地猜的;再次进场、断线、换绑时清零。
        private uint _arrivedChannelNo;
        // 面板打开期间下一次刷新目录的时刻;面板关着时下一次补问的时刻(补问次数用完后是正无穷)。
        // 时钟都是 Time.realtimeSinceStartup。
        private float _nextPanelRefreshAt;
        private float _nextBackgroundListAt;
        // 这次进场(或上次面板自己拉目录)以来已经补问了几次。
        private int _backgroundRetries;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[SceneChannelUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<SceneChannelUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var canvasObject = new GameObject("SceneChannelCanvas", typeof(RectTransform), typeof(Canvas),
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
            _entry = CreateEntry(_hud, Toggle);
            _entryLabel = _entry.GetComponentInChildren<TMP_Text>(true);
            _entry.gameObject.SetActive(false);
            _window = new SceneChannelWindow(design);
            _window.SwitchRequested += sceneId => { if (_available) _client?.RequestSwitch(sceneId); };
            _window.RefreshRequested += RefreshNow;
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_window == null) return;   // 重复实例:Awake 里已安排销毁
            Bind(AppBootstrap.Instance?.GameClient);
            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = inGame && !battleLayer;
            if (_client != null)
            {
                // 宿主才知道的三件事。写入值有变化时数据层会发变化通知,面板随之重画。
                _client.InBattle = battleLayer || InBattleOrSpectating(_game);
                _client.Travelling = _game.IsTravelPending || _game.IsRedirecting;
                _client.TeamFollower = IsTeamFollower();
                _client.Tick();
            }
            _hud.gameObject.SetActive(_available);
            if (!_available)
            {
                // 进战斗、断线、换服途中都收起面板;在途的切线不受影响,结论仍由数据层给出。
                HidePanel();
                return;
            }
            if (_client != null) Poll(Time.realtimeSinceStartup);
            // 自己的面板开着时带着输入拦截,但仍要响应 Esc / L;别的模态窗开着时不让 L 穿透打开。
            if (IsTyping() || (!_window.IsVisible && GameplayInputGate.IsKeyboardBlocked)) return;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) HidePanel();
            else if (keyboard.lKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
            else if (Input.GetKeyDown(KeyCode.L)) Toggle();
#endif
        }

        public void HidePanel() => _window?.Hide();

        public void Toggle()
        {
            if (_window.IsVisible) { HidePanel(); return; }
            // 跨区传送在途时不开:地图窗正挡在屏上当遮罩,下面那串收起会把它一起撤掉。
            if (!_available || _client == null || _client.Travelling) return;
            Guild.GuildUiRoot.Instance?.HidePanel();
            Team.TeamUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
            Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();
            Mail.MailUiRoot.Instance?.HidePanel();
            Social.SocialUiRoot.Instance?.HidePanel();
            _window.SetView(BuildView(_client));
            _window.Show();
            // 打开即刷新一次(数据层自己管最小间隔:太近就排队,到点补发)。
            RefreshNow();
        }

        // ── 纯函数(离线截图与测试直接用)──────────────────────────────────

        /// <summary>
        /// 角标文案;null = 不显示角标。切线在途 →「切换中…」(不带快捷键提示);有目录 →「N线 [L]」
        /// (目录里找不到所在线时是「线路 [L]」);没有目录(副本 / 镜像 / 尚未取到)→ 不显示。
        /// 唯一的例外是 <paramref name="arrivedChannelNo"/>:自己发起的切线刚抵达、新场景的目录还没到的那一小段,
        /// 先显示抵达的线号(非 0 时),免得角标闪没;目录一到就以目录为准。
        /// </summary>
        public static string EntryLabel(bool hasDirectory, uint currentChannelNo, bool switchPending,
            uint arrivedChannelNo = 0)
        {
            if (switchPending) return SwitchingEntryText;
            if (hasDirectory) return SceneChannelModels.LineName(currentChannelNo) + EntryHotkeyHint;
            return arrivedChannelNo != 0 ? SceneChannelModels.LineName(arrivedChannelNo) + EntryHotkeyHint : null;
        }

        /// <summary>
        /// 第 <paramref name="attempt"/> 次补问(从 0 起)之前要等多久:15、30、60 秒;
        /// 次数用完(或传了负数)返回正无穷,即不再补问。
        /// </summary>
        public static float BackgroundRetryDelay(int attempt) =>
            attempt < 0 || attempt >= BackgroundRetryLimit
                ? float.PositiveInfinity
                : BackgroundRetryIntervalSeconds * (1 << attempt);

        /// <summary>把数据层此刻的状态整份拍成面板视图。</summary>
        public static SceneChannelPanelView BuildView(SceneChannelClient client)
        {
            if (client == null) return default;
            return new SceneChannelPanelView
            {
                Lines = client.Lines,
                Context = client.SwitchContext,
                Loading = client.ListPending,
                SwitchTargetSceneId = client.SwitchTargetSceneId,
                Status = client.Status,
                StatusIsError = client.StatusIsError,
            };
        }

        /// <summary>
        /// 建角标按钮:右侧入口列顶上的空位(列里第一个入口「战斗」在 176,往上一格是 72),
        /// 尺寸、底图与「地图」入口同款。
        /// </summary>
        public static Button CreateEntry(UnityEngine.Transform hud, System.Action click)
        {
            var entry = GameplayUiArt.Button(hud, SceneChannelModels.LineName(0) + EntryHotkeyHint, BattleUiStyle.HudEntryX,
                BattleUiStyle.HudEntryY(-1), BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, click, true,
                fontSize: 32);
            entry.name = EntryName;
            return entry;
        }

        // ── 绑定 ────────────────────────────────────────────────────────────

        private void Bind(GameClient game)
        {
            if (ReferenceEquals(_game, game)) return;
            Unbind();
            _game = game;
            if (game != null)
            {
                _client = new SceneChannelClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                    () => game.CurrentSceneId, () => game.CurrentSceneConfigId, () => Time.realtimeSinceStartup);
                _client.Changed += Render;
                _client.Switched += HandleSwitched;
                game.OnSceneEntered += HandleSceneEntered;
                game.OnServerTip += HandleServerTip;
                game.OnDisconnected += HandleDisconnected;
                RestartBackgroundRetry(Time.realtimeSinceStartup);
                // 本组件晚于进场才出现(或 GameClient 中途换了实例)时,入场通知已经错过,自己拉一次。
                if (game.InGame && game.IsGateReady && game.CurrentSceneId != 0) _client.RequestList();
            }
            Render();
        }

        private void Unbind()
        {
            if (_game != null)
            {
                _game.OnSceneEntered -= HandleSceneEntered;
                _game.OnServerTip -= HandleServerTip;
                _game.OnDisconnected -= HandleDisconnected;
                _game = null;
            }
            if (_client != null)
            {
                _client.Changed -= Render;
                _client.Switched -= HandleSwitched;
                _client.Dispose();
                _client = null;
            }
            _wasSwitchPending = false;
            _arrivedChannelNo = 0;
            HidePanel();
        }

        private void HandleSceneEntered(SceneInfoComp scene)
        {
            // 数据层在这里清掉旧目录并拉新场景的;补问从这一刻重新计时、重新计数。
            RestartBackgroundRetry(Time.realtimeSinceStartup);
            // 换了场景,上一次抵达的线号作废;这次若是自己切线抵达,数据层随后会再报一次(HandleSwitched)。
            _arrivedChannelNo = 0;
            _client?.HandleSceneEntered(scene);
        }

        private void HandleServerTip(TipInfoMessage tip) => _client?.HandleServerTip(tip);

        private void HandleDisconnected()
        {
            _arrivedChannelNo = 0;
            _client?.HandleDisconnected();
            RefreshEntry();
            HidePanel();
        }

        // ── 驱动 ────────────────────────────────────────────────────────────

        private void Poll(float now)
        {
            if (_window.IsVisible)
            {
                // 倒计时走动不触发变化通知,状态行的秒数每帧喂给面板。
                _window.SetCooldown(_client.CooldownRemainingSeconds);
                if (now >= _nextPanelRefreshAt) RefreshNow();
                return;
            }
            // 补问次数用完后这里是正无穷,一直返回,直到下次进场或面板自己拉目录时重新计数。
            if (now < _nextBackgroundListAt) return;
            bool badgeIncomplete = !_client.HasDirectory || _client.CurrentChannelNo == 0;
            if (!badgeIncomplete || !_client.DirectoryApplicable || _client.ListPending || _client.SwitchPending
                || _game.CurrentSceneId == 0)
            {
                // 此刻没有可补的(角标完整 / 这里没有分线 / 已有请求在途 / 还没进场):不占次数,隔一个基本间隔再看。
                _nextBackgroundListAt = now + BackgroundRetryIntervalSeconds;
                return;
            }
            _client.RequestList();
            _backgroundRetries++;
            _nextBackgroundListAt = now + BackgroundRetryDelay(_backgroundRetries);
        }

        /// <summary>补问重新计时、重新计数:进场、换绑、面板自己拉过目录之后。</summary>
        private void RestartBackgroundRetry(float now)
        {
            _backgroundRetries = 0;
            _nextBackgroundListAt = now + BackgroundRetryDelay(0);
        }

        /// <summary>面板要一份新目录:打开时、每个刷新周期、玩家点「刷新」。</summary>
        private void RefreshNow()
        {
            if (!_available || _client == null) return;
            float now = Time.realtimeSinceStartup;
            _nextPanelRefreshAt = now + SceneChannelClient.PanelRefreshIntervalSeconds;
            RestartBackgroundRetry(now);
            // 切线在途时不发:玩家正在两个场景之间,这时的列线回包服务端会按「已换场景」丢弃。
            // 抵达后数据层自己会拉新场景的目录;没切成的话,面板最迟在下一个刷新周期补上。
            if (!_client.SwitchPending) _client.RequestList();
        }

        /// <summary>数据层的变化通知:角标与面板整份重读。</summary>
        private void Render()
        {
            if (_window == null) return;
            RefreshEntry();
            _window.SetView(BuildView(_client));
            bool pending = _client != null && _client.SwitchPending;
            // 面板不在屏上时切线失败(玩家手动关了面板,或中途进了战斗):状态行没人看得到,补一条提示。
            if (_wasSwitchPending && !pending && _client != null && _client.StatusIsError && !_window.IsVisible)
                BattleUiRoot.Instance?.ShowToast(_client.Status, true);
            _wasSwitchPending = pending;
        }

        private void RefreshEntry()
        {
            if (_entry == null) return;
            string label = _client == null ? null
                : EntryLabel(_client.HasDirectory, _client.CurrentChannelNo, _client.SwitchPending, _arrivedChannelNo);
            _entry.gameObject.SetActive(label != null);
            if (label != null && _entryLabel.text != label) _entryLabel.text = label;
        }

        /// <summary>
        /// 自己发起的切线已抵达:收起面板(切线期间它一直留在屏上当遮罩),提示一句。
        /// 数据层先发变化通知、后报抵达,所以这里记下线号后要自己再刷一次角标。
        /// </summary>
        private void HandleSwitched(uint channelNo)
        {
            _arrivedChannelNo = channelNo;
            RefreshEntry();
            HidePanel();
            BattleUiRoot.Instance?.ShowToast(SceneChannelClient.SwitchedText(channelNo));
        }

        private static bool InBattleOrSpectating(GameClient game) =>
            (game.Battle != null && game.Battle.Phase != BattlePhase.None) ||
            (game.Spectate != null && game.Spectate.Phase != SpectatePhase.None);

        /// <summary>在队伍里且不是队长(v1 只有队长能切线);组队界面还没同步到队伍时按不在队伍算。</summary>
        private static bool IsTeamFollower()
        {
            var team = Team.TeamUiRoot.Instance?.State;
            return team != null && team.HasTeam && !team.IsLeader;
        }

        private static bool IsTyping()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected != null && (selected.GetComponentInParent<TMP_InputField>()?.isFocused == true ||
                                        selected.GetComponentInParent<InputField>()?.isFocused == true);
        }

        private void OnDisable() => HidePanel();

        private void OnDestroy()
        {
            Unbind();
            if (Instance == this) Instance = null;
        }
    }
}
