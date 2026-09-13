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
                _status = "暂未收到抵达消息，请稍后重试。";
                Refresh();
            }
            if (!available)
            {
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
            GameplayUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
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
                    if (!_request.Accept(generation)) return;
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
            }
            _request.Reset();
            _status = "";
            HidePanel();
            _game = game;
            if (_game != null)
            {
                _game.OnSceneEntered += HandleSceneEntered;
                _game.OnDisconnected += HandleDisconnected;
            }
        }

        private void HandleSceneEntered(SceneInfoComp scene)
        {
            if (_request.ReceiveScene(scene?.SceneConfigId ?? 0, out bool festival))
            {
                _app.WorldMap.SetFestivalAppearance(festival);
                _status = "已抵达，沿着街巷自在游历吧。";
                HidePanel();
            }
            else _status = "";
            Refresh();
        }

        private void HandleDisconnected()
        {
            _request.Reset();
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
