using System.Collections.Generic;
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
    /// <summary>正常游戏地图入口；服务端确认地点，本地选择该地点的节庆景色。</summary>
    public sealed class CityTravelUiRoot : MonoBehaviour
    {
        public static CityTravelUiRoot Instance { get; private set; }
        public CityTravelWindow Window => _window;
        private readonly CityTravelRequest _request = new();
        private AppBootstrap _app;
        private GameClient _game;
        private RectTransform _hud;
        private Button _entry;
        private CityTravelWindow _window;
        private string _status = "";
        private bool _wasAvailable;
        // 跨区传送：在途的目标区服、发起时的连接标识（抵达时据此判断是否真的换了连接），
        // 以及“此刻人在哪个区”。服务端的重定向通知不带区服编号，客户端只能自己记：
        // 零表示没跨过区，按选区时的区服算。登录时被服务端送回归属区的情况这里无从得知，
        // 记错的后果只是列表里多列或少列一个区，能不能去始终由服务端裁决。
        private uint _pendingZoneId;
        private object _gateAtRequest;
        private uint _visitingZoneId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[CityTravelUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<CityTravelUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var canvasObject = new GameObject("CityTravelCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 185;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            _entry = GameplayUiArt.Button(_hud, "地图", BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(7),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 32);
            _entry.name = "CityTravelEntry";
            _window = new CityTravelWindow(design);
            _window.SetDestinations(CreateDestinations());
            _window.TravelRequested += RequestTravel;
            _window.ZoneTravelRequested += RequestZoneTravel;
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            _app = AppBootstrap.Instance;
            Bind(_app?.GameClient);
            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            bool available = inGame && CanTravelNow();
            _hud.gameObject.SetActive(inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false));
            _entry.interactable = available && !_request.IsPending;
            if (_request.Tick(Time.realtimeSinceStartup))
            {
                _pendingZoneId = 0;
                _gateAtRequest = null;
                _status = "暂未收到抵达消息，请稍后重试。";
                Refresh();
            }
            // 跨区请求已被受理，但底层已经不再等待（在它的等待预算内既没等到换服通知，也没等到失败提示），
            // 连接也还是原来那条：这趟行程不会再有下文。底层只把“传送超时”写进登录流程的状态栏，
            // 游戏内没有任何界面显示它；不在这里收场，窗口会顶着“正在传送”一直空等到跨区的等待上限。
            // 不会误伤正常流程：失败提示到达时请求已在同一调用栈里被重置；换服通知到达后“正在换连接”
            // 立刻为真，且入场通知先于换连接结束到达，那时在途的区服编号已经清零。
            if (_pendingZoneId != 0 && _request.IsPending && _request.IsAccepted && _game != null &&
                !_game.IsTravelPending && !_game.IsRedirecting &&
                ReferenceEquals(_game.GateConnectionIdentity, _gateAtRequest))
            {
                _request.Reset();
                _pendingZoneId = 0;
                _gateAtRequest = null;
                _status = "传送超时，请稍后重试。";
                Refresh();
            }
            if (!available)
            {
                // 换区途中客户端会先清掉“已进入游戏”的状态再去连新服务器，此时 available 必然为假；
                // 照旧收起窗口，就等于把“正在传送”的遮罩连同输入拦截一起撤掉，玩家会看到空场景还能乱点。
                // 跨区在途或正在换连接时保持原样（也不动 _wasAvailable，收场后仍不可用时下一帧照常收起），
                // 收场交给入场通知、服务端提示、断线和超时。
                // 判据只看底层的这两个状态，不看“有请求在途”：同区换图不换连接，它在途时变得不可用
                // 只可能是战斗或观战开始了，那时必须照旧收起，否则遮罩会盖在战斗界面上直到请求超时（受理后可长达一分多钟）。
                // 跨区的整段路程（发出请求、等换服通知、换连接、重新进场）都被这两个状态盖住，中间没有空窗。
                bool travelling = _game != null && (_game.IsRedirecting || _game.IsTravelPending);
                if (travelling) return;
                if (_wasAvailable) HidePanel();
                _wasAvailable = false;
                return;
            }
            _wasAvailable = true;
            // Our own open window owns a blocker but must still accept Escape/M.
            // A different modal keeps the map shortcut from opening through it.
            if (IsTyping() || (!_window.IsVisible && GameplayInputGate.IsKeyboardBlocked)) return;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) HidePanel();
            else if (keyboard.mKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
            else if (Input.GetKeyDown(KeyCode.M)) Toggle();
#endif
        }

        public void HidePanel() => _window?.Hide();

        public void Toggle()
        {
            if (_window.IsVisible) { HidePanel(); return; }
            if (_game == null || !_game.InGame || !_game.IsGateReady || !CanTravelNow()) return;
            Guild.GuildUiRoot.Instance?.HidePanel();
            Team.TeamUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
            _window.SetZones(BuildZones());
            _window.Show(CurrentScene, _app.WorldMap != null && _app.WorldMap.FestivalAppearance);
            Refresh();
        }

        public void RequestTravel(uint sceneConfigId, bool festival)
        {
            if (_game == null || !_game.InGame || !_game.IsGateReady || !CanTravelNow() || _request.IsPending) return;
            if (sceneConfigId < 1 || sceneConfigId > 4 || _app.WorldMap == null) return;
            if (sceneConfigId == CurrentScene)
            {
                _app.WorldMap.SetFestivalAppearance(festival);
                _status = "景色已更新，愿此行尽兴。";
                Refresh();
                return;
            }
            int generation = _request.Begin(sceneConfigId, festival, Time.realtimeSinceStartup);
            if (generation == 0) return;
            _status = "正在启程，请稍候…";
            Refresh();
            // 编号为零的线路由服务端分配，客户端不修改当前场景和角色位置。
            StartCoroutine(_game.EnterScene(sceneConfigId, 0,
                () =>
                {
                    // 受理不等于马上换图：目的地落在别的节点时，服务端要先冻结玩家、存盘、再重发进场请求，
                    // 最坏一分钟左右才有结论（换图成功，或补推一条失败提示）。客户端分不清这次走没走这条路，
                    // 所以受理后一律按服务端的交接预算顺延截止时间；仍用三十秒的话会先报“未收到抵达消息”，
                    // 玩家却还被冻结着，随后又真的换了图，或失败提示到达时请求已不在途、原因看不到。
                    if (!_request.Accept(generation, Time.realtimeSinceStartup,
                            CityTravelRequest.AcceptedHandoffBudgetSeconds)) return;
                    _status = "行程已安排，正在进入目的地…";
                    Refresh();
                },
                error =>
                {
                    if (!_request.Fail(generation)) return;
                    Debug.LogWarning("[CityTravel] 传送请求失败：" + error);
                    _status = "暂时无法前往，请稍后重试。";
                    Refresh();
                }));
        }

        /// <summary>
        /// 跨区传送。与同区换图共用同一个请求状态机和“正在传送”遮罩，区别只有三点：
        /// 走另一条请求；等待上限放宽；目的地与当前地图相同也照常前往（那是别的区服的同名地图）。
        /// 请求被受理不算抵达，抵达仍然只认入场通知。
        /// </summary>
        public void RequestZoneTravel(uint zoneId, uint sceneConfigId, bool festival)
        {
            if (_game == null || !_game.InGame || !_game.IsGateReady || !CanTravelNow() || _request.IsPending) return;
            if (zoneId == 0 || sceneConfigId < 1 || sceneConfigId > 4 || _app.WorldMap == null) return;
            int generation = _request.Begin(sceneConfigId, festival, Time.realtimeSinceStartup,
                CityTravelRequest.CrossZoneTimeoutSeconds);
            if (generation == 0) return;
            _pendingZoneId = zoneId;
            _gateAtRequest = _game.GateConnectionIdentity;
            _status = "正在启程，请稍候…";
            Refresh();
            StartCoroutine(_game.ZoneTravel.TravelToZone(zoneId, sceneConfigId,
                () =>
                {
                    if (!_request.Accept(generation)) return;
                    _status = "行程已安排，正在前往目标区服…";
                    Refresh();
                },
                error =>
                {
                    if (!_request.Fail(generation)) return;
                    _pendingZoneId = 0;
                    _gateAtRequest = null;
                    Debug.LogWarning("[CityTravel] 跨区传送请求失败：" + error);
                    // 错误文本此刻只有编号兜底（提示码的文案表客户端还没有），直接给玩家看，方便反馈问题。
                    _status = "暂时无法前往，请稍后重试。（" + error + "）";
                    Refresh();
                }));
        }

        private uint CurrentZoneId => _visitingZoneId != 0 ? _visitingZoneId : (_app?.Session?.SelectedZoneId ?? 0u);

        /// <summary>
        /// 可前往的其他区服：取选区时网关下发的列表，去掉当前所在区和不可进入的区
        /// （口径与选区界面一致：维护、关闭、未开放不可进）。列表只在选区时刷新，可能过时，
        /// 所以这里只管展示，目标区是否存在、是否繁忙由服务端在传送请求里裁决并回提示。
        /// </summary>
        private List<CityTravelZone> BuildZones()
        {
            var result = new List<CityTravelZone>();
            var zones = _app?.Session?.Zones;
            if (zones == null) return result;
            uint here = CurrentZoneId;
            foreach (var zone in zones)
            {
                if (zone == null || zone.zone_id == 0 || zone.zone_id == here) continue;
                if (zone.status == "MAINTENANCE" || zone.status == "CLOSED" || zone.status == "PREVIEW") continue;
                result.Add(new CityTravelZone
                {
                    ZoneId = zone.zone_id,
                    Name = string.IsNullOrWhiteSpace(zone.name) ? zone.zone_id + "区" : zone.name
                });
            }
            return result;
        }

        private uint CurrentScene => _game?.CurrentSceneConfigId is > 0 ? _game.CurrentSceneConfigId : 1;

        private bool CanTravelNow()
            => (_game?.Battle == null || _game.Battle.Phase == BattlePhase.None) &&
               (_game?.Spectate == null || _game.Spectate.Phase == SpectatePhase.None) &&
               !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);

        private void Bind(GameClient game)
        {
            if (_game == game) return;
            if (_game != null)
            {
                _game.OnSceneEntered -= HandleSceneEntered;
                _game.OnDisconnected -= HandleDisconnected;
                _game.OnServerTip -= HandleServerTip;
            }
            _request.Reset();
            _pendingZoneId = 0;
            _gateAtRequest = null;
            _visitingZoneId = 0;
            _status = "";
            HidePanel();
            _game = game;
            if (_game != null)
            {
                _game.OnSceneEntered += HandleSceneEntered;
                _game.OnDisconnected += HandleDisconnected;
                _game.OnServerTip += HandleServerTip;
            }
        }

        private void HandleSceneEntered(SceneInfoComp scene)
        {
            // 跨区请求在途、且入场通知来自另一条连接，说明人已经在目标区了（哪怕落到的不是所选地图）。
            // 连接没换就收到入场通知，只是一次普通换图，不能据此改“所在区”。
            if (_pendingZoneId != 0 && _game != null && !ReferenceEquals(_game.GateConnectionIdentity, _gateAtRequest))
                _visitingZoneId = _pendingZoneId;
            _pendingZoneId = 0;
            _gateAtRequest = null;
            if (_request.ReceiveScene(scene?.SceneConfigId ?? 0, out bool festival))
            {
                _app.WorldMap.SetFestivalAppearance(festival);
                _status = "已抵达，沿着街巷自在游历吧。";
                HidePanel();
            }
            else _status = "";
            Refresh();
        }

        /// <summary>
        /// 服务端提示到达时，如果有传送请求在途，就当作这次行程没成、立刻收场。
        /// 受理之后才发生的失败（同区换图被换手门拒绝、跨区在冻结存盘之后被拒）不会出现在请求的应答里，
        /// 服务端只能补推一条提示；不接它，窗口就只能干等到超时。
        /// 判得宽是刻意的：请求在途时窗口挡着输入，几乎不会有别的提示；提示码的枚举客户端还没有，无法按码收窄。
        /// 即使误判，后果也只是遮罩提前收起，随后到达的入场通知照常生效。
        /// </summary>
        private void HandleServerTip(TipInfoMessage tip)
        {
            if (!_request.IsPending) return;
            // 连接已经在换了，说明服务端早已放行；这时的提示来自目标区的登录流程，与行程成败无关。
            if (_game != null && _game.IsRedirecting) return;
            _request.Reset();
            _pendingZoneId = 0;
            _gateAtRequest = null;
            _status = "暂时无法前往，请稍后重试。（" + (tip?.Id ?? 0) + "）";
            Refresh();
        }

        private void HandleDisconnected()
        {
            _request.Reset();
            _pendingZoneId = 0;
            _gateAtRequest = null;
            _visitingZoneId = 0;
            _status = "";
            HidePanel();
            Refresh();
        }

        private void Refresh()
            => _window?.SetState(CurrentScene, _app?.WorldMap != null && _app.WorldMap.FestivalAppearance,
                _request.IsPending, _status);

        private static bool IsTyping()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected != null && (selected.GetComponentInParent<TMP_InputField>()?.isFocused == true ||
                                        selected.GetComponentInParent<InputField>()?.isFocused == true);
        }

        private void OnDestroy()
        {
            Bind(null);
            if (Instance == this) Instance = null;
        }

        public static CityTravelDestination[] CreateDestinations() => new[]
        {
            new CityTravelDestination
            {
                SceneConfigId = 1, Name = "天墉城", Description = "太极广场连着热闹长街，道观、灯市与庭院环绕城中。",
                DayResourcePath = "World/FestivalRegions/tianyong/preview"
            },
            new CityTravelDestination
            {
                SceneConfigId = 2, Name = "蓬莱岛", Description = "云海托起仙岛，石桥串联道观与庭园，沿岸静赏海天。",
                DayResourcePath = "World/FestivalRegions/penglai/day",
                FestivalResourcePath = "World/FestivalRegions/penglai/festival", FestivalName = "中秋月夜"
            },
            new CityTravelDestination
            {
                SceneConfigId = 3, Name = "东海渔村", Description = "码头停舟，渔舍临海，绕过村中小桥与摊市，走向潮声深处。",
                DayResourcePath = "World/FestivalRegions/donghai/day",
                FestivalResourcePath = "World/FestivalRegions/donghai/festival", FestivalName = "元宵灯会"
            },
            new CityTravelDestination
            {
                SceneConfigId = 4, Name = "揽仙镇", Description = "山间古镇炊烟轻起，宽阔街巷通向牌坊、集市与道家院落。",
                DayResourcePath = "World/FestivalRegions/lanxian/day",
                FestivalResourcePath = "World/FestivalRegions/lanxian/festival", FestivalName = "春节迎新"
            }
        };
    }
}
