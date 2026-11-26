using System;
using System.Collections.Generic;
using MmorpgClient.Game;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Attribute;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CityNpc;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.CoreLoop;
using MmorpgClient.UI.Ugui.Guild;
using MmorpgClient.UI.Ugui.Jubaozhai;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.UI.Ugui.Skills;
using MmorpgClient.UI.Ugui.SpiritShop;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.UI.Ugui.TravelPass;
using MmorpgClient.UI.Ugui.TrialJournal;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Gameplay
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 归元城 8 位原创核心 NPC 场景表现与人物联动玩法宿主。
    /// 严格遵守 SRP 与连接生命周期规范：
    ///  - 仅在归元城（Scene 1）生成 8 位原创 NPC 实体与屏幕点击热区，进战斗/离城/断线自动清理；
    ///  - 订阅 BattleClient.OnBattleEnd / GameClient.OnSceneEntered，OnDisable/OnDestroy 对称退订；
    ///  - 兼容 WebGL2 单线程与 IL2CPP，零多线程与阻塞 I/O。
    /// </summary>
    public sealed class CityNpcUiRoot : MonoBehaviour
    {
        public static CityNpcUiRoot Instance { get; private set; }
        public CityNpcState State => _state;
        public CityNpcWindow Window => _window;

        public const int CanvasSortingOrder = 182;
        public const float ApproachStandOff = 3.2f;
        public const float ApproachTimeoutSeconds = 25f;
        private const float CoopBubbleIntervalSeconds = 22f;

        private readonly CityNpcState _state = new();
        private readonly List<GuiyuanCityNpcActor> _actors = new();
        private readonly List<Button> _hotspots = new();

        private GameClient _game;
        private RectTransform _hud;
        private CityNpcWindow _window;
        private Button _rosterEntry;
        private bool _available;
        private bool _spawnedForCity;
        private CityNpcDefinition _approachingNpc;
        private float _approachDeadline;
        private Vector3 _approachTarget;
        private float _nextCoopBubbleAt;
        private int _coopBubbleSeed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[GuiyuanCityNpcUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<CityNpcUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);

            var canvasObject = new GameObject("CityNpcCanvas", typeof(RectTransform), typeof(Canvas),
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

            for (int i = 0; i < CityNpcCatalog.Npcs.Count; ++i)
            {
                var npc = CityNpcCatalog.Npcs[i];
                var hs = QdaoUguiFactory.CreateHitButton("GuiyuanNpcHotspot_" + npc.Id, _hud, 0, 0, 10, 10);
                hs.onClick.AddListener(() => HandleNpcClicked(npc));
                hs.gameObject.SetActive(false);
                _hotspots.Add(hs);
            }

            // 归元城常驻快捷「归元八友」入口按钮（位于屏幕右下安全区内，不遮挡主 HUD 与小游戏胶囊）
            _rosterEntry = GameplayUiArt.Button(_hud, "归元八友", 2292, 976, 224, 62,
                () => OpenNpcDialog(CityNpcCatalog.Npcs[0]), false, true, fontSize: 24);
            _rosterEntry.name = "GuiyuanNpcRosterEntry";

            _window = new CityNpcWindow(design);
            _window.SelectNpcRequested += npc => OpenNpcDialog(npc);
            _window.WalkToNpcRequested += BeginWalkToNpc;
            _window.ShortcutRequested += DispatchShortcut;
            _window.StartEncounterRequested += HandleStartEncounter;
            _window.AcceptErrandRequested += id => { _state.AcceptErrand(id); RefreshWindowIfVisible(); };
            _window.ArchiveErrandRequested += id => { _state.ArchiveErrand(id); RefreshWindowIfVisible(); };
            _window.DrawFortuneRequested += HandleDrawFortune;
            _window.CycleCompanionOathRequested += () => { _state.CycleCompanionOath(); RefreshWindowIfVisible(); };
            _window.CopyCallingCardRequested += HandleCopyCallingCard;

            _state.Changed += RefreshWindowIfVisible;
            _state.ToastRequested += ShowToast;
            _hud.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            UnbindGame();
            ClearWorldActors();
            _state.Changed -= RefreshWindowIfVisible;
            _state.ToastRequested -= ShowToast;
            if (Instance == this) Instance = null;
        }

        public void HidePanel()
        {
            CancelApproach();
            _window?.Hide();
        }

        public void OpenNpcDialog(CityNpcDefinition npc)
        {
            if (!_available || _window == null) return;
            CityPanelRegistry.HideOthers(this);
            var target = npc ?? CityNpcCatalog.Npcs[0];
            var activeId = ResolveActiveAppearanceId();
            var archiveName = ResolveArchiveName(activeId);
            var card = BuildCurrentCallingCard(activeId, archiveName);
            _window.Show(target, activeId, archiveName, _state, card, ResolveActiveTideSceneId());

            var actor = FindActor(target.Id);
            if (actor != null && target.TryGetCharacterGreeting(activeId, out var line))
            {
                actor.ShowBubble(line, 3.6f);
            }
        }

        private void Update()
        {
            if (_window == null || _hud == null) return;
            BindGame(AppBootstrap.Instance?.GameClient);

            bool inGame = _game != null && _game.InGame && _game.IsGateReady && _game.PlayerId != 0;
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = inGame && !battleLayer;
            _hud.gameObject.SetActive(_available);

            if (!_available)
            {
                HidePanel();
                ClearWorldActors();
                return;
            }

            _state.BindPlayer(_game.PlayerId);
            bool inGuiyuanCity = _game.CurrentSceneConfigId == CityNpcCatalog.GuiyuanCitySceneConfigId;
            if (_rosterEntry != null) _rosterEntry.gameObject.SetActive(inGuiyuanCity);

            if (inGuiyuanCity)
            {
                EnsureWorldActors();
                SyncHotspots();
                TickApproach();
                TickSeriesCoopBubble();
            }
            else
            {
                ClearWorldActors();
                CancelApproach();
            }

            if (_window.IsVisible && !CityPanelRegistry.IsTyping())
            {
#if ENABLE_INPUT_SYSTEM
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) HidePanel();
#elif ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
#endif
            }
        }

        private void BindGame(GameClient next)
        {
            if (_game == next) return;
            UnbindGame();
            _game = next;
            if (_game == null) return;
            _game.OnSceneEntered += HandleSceneEntered;
            _game.OnDisconnected += HandleDisconnected;
            if (_game.Battle != null) _game.Battle.OnBattleEnd += HandleBattleEnd;
        }

        private void UnbindGame()
        {
            if (_game == null) return;
            _game.OnSceneEntered -= HandleSceneEntered;
            _game.OnDisconnected -= HandleDisconnected;
            if (_game.Battle != null) _game.Battle.OnBattleEnd -= HandleBattleEnd;
            _game = null;
        }

        private void HandleDisconnected()
        {
            HidePanel();
            ClearWorldActors();
            _state.Reset();
        }

        private void HandleSceneEntered(SceneInfoComp sceneInfo)
        {
            if (sceneInfo == null) return;
            _state.ObserveAuthoritativeScene(sceneInfo.SceneConfigId);
            if (sceneInfo.SceneConfigId != CityNpcCatalog.GuiyuanCitySceneConfigId)
            {
                ClearWorldActors();
                CancelApproach();
            }
        }

        private void HandleBattleEnd(BattleEndS2C end)
        {
            _state.ObserveAuthoritativeBattleEnd(end);
        }

        private void EnsureWorldActors()
        {
            if (_spawnedForCity && _actors.Count == CityNpcCatalog.Npcs.Count && _actors[0] != null) return;
            ClearWorldActors();
            var mapRuntime = UnityEngine.Object.FindFirstObjectByType<TianyongMapRuntime>();
            UnityEngine.Transform parent = mapRuntime?.Map?.Root != null ? mapRuntime.Map.Root.transform : null;
            for (int i = 0; i < CityNpcCatalog.Npcs.Count; ++i)
            {
                _actors.Add(GuiyuanCityNpcActor.Create(parent, CityNpcCatalog.Npcs[i]));
            }
            _spawnedForCity = true;
        }

        private void ClearWorldActors()
        {
            for (int i = 0; i < _actors.Count; ++i)
            {
                if (_actors[i] != null) Destroy(_actors[i].gameObject);
            }
            _actors.Clear();
            for (int i = 0; i < _hotspots.Count; ++i)
            {
                if (_hotspots[i] != null) _hotspots[i].gameObject.SetActive(false);
            }
            _spawnedForCity = false;
        }

        private void SyncHotspots()
        {
            var cam = Camera.main;
            bool allowHotspot = cam != null && !_window.IsVisible;
            for (int i = 0; i < _actors.Count && i < _hotspots.Count; ++i)
            {
                var actor = _actors[i];
                var hs = _hotspots[i];
                if (!allowHotspot || actor == null || !actor.TryGetScreenHitBox(cam, out var feet, out var size))
                {
                    if (hs.gameObject.activeSelf) hs.gameObject.SetActive(false);
                    continue;
                }
                if (!hs.gameObject.activeSelf) hs.gameObject.SetActive(true);
                var rect = (RectTransform)hs.transform;
                var canvasRect = (RectTransform)_hud.parent;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, feet, null, out var localFeet))
                {
                    float scaleY = canvasRect.rect.height / Mathf.Max(1f, Screen.height);
                    float w = Mathf.Clamp(size.x * scaleY, 64f, 180f);
                    float h = Mathf.Clamp(size.y * scaleY, 96f, 240f);
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0f);
                    rect.anchoredPosition = localFeet;
                    rect.sizeDelta = new Vector2(w, h);
                }
            }
        }

        private void HandleNpcClicked(CityNpcDefinition npc)
        {
            if (!_available || npc == null) return;
            var controller = FindLocalController();
            if (controller != null)
            {
                float dist = MysteryMerchantNpc.HorizontalDistance(controller.FeetPosition, new Vector3(npc.WorldX, 0f, npc.WorldZ));
                if (dist > CityNpcCatalog.DefaultInteractRange)
                {
                    BeginWalkToNpc(npc);
                    return;
                }
            }
            OpenNpcDialog(npc);
        }

        private void BeginWalkToNpc(CityNpcDefinition npc)
        {
            if (npc == null) return;
            _window?.Hide();
            var controller = FindLocalController();
            if (controller == null)
            {
                OpenNpcDialog(npc);
                return;
            }
            if (controller.ServerDriven)
            {
                ShowToast("当前处于组队跟随中，请暂离后再自行前往。");
                return;
            }
            var npcFeet = new Vector3(npc.WorldX, 0f, npc.WorldZ);
            var target = MysteryMerchantNpc.ApproachPoint(npcFeet, controller.FeetPosition, ApproachStandOff);
            if (!controller.SetDestination(target))
            {
                OpenNpcDialog(npc);
                return;
            }
            _approachingNpc = npc;
            _approachTarget = target;
            _approachDeadline = Time.unscaledTime + ApproachTimeoutSeconds;
            ShowToast($"正在前往归元城 · {npc.LandmarkLabel}拜访「{npc.Name}」…");
        }

        private void TickApproach()
        {
            if (_approachingNpc == null) return;
            var controller = FindLocalController();
            if (controller == null || Time.unscaledTime > _approachDeadline)
            {
                CancelApproach();
                return;
            }
            float dist = MysteryMerchantNpc.HorizontalDistance(controller.FeetPosition,
                new Vector3(_approachingNpc.WorldX, 0f, _approachingNpc.WorldZ));
            if (dist <= CityNpcCatalog.DefaultInteractRange)
            {
                var reached = _approachingNpc;
                CancelApproach();
                OpenNpcDialog(reached);
                return;
            }
            if (!controller.IsMoving &&
                MysteryMerchantNpc.HorizontalDistance(controller.FeetPosition, _approachTarget) > 2f)
            {
                CancelApproach();
            }
        }

        private void CancelApproach()
        {
            _approachingNpc = null;
            _approachDeadline = 0f;
        }

        private void TickSeriesCoopBubble()
        {
            if (Time.unscaledTime < _nextCoopBubbleAt || _game?.World == null) return;
            _nextCoopBubbleAt = Time.unscaledTime + CoopBubbleIntervalSeconds;

            string localAppearance = ResolveActiveAppearanceId();
            var companionIds = new List<string>();
            foreach (var kv in _game.World.Actors)
            {
                var actor = kv.Value;
                if (actor == null || actor.PlayerId == 0 || actor.PlayerId == _game.PlayerId) continue;
                string otherId = _game.ResolveCharacterId(actor.PlayerId);
                if (!string.IsNullOrEmpty(otherId)) companionIds.Add(otherId);
            }
            if (CityNpcCatalog.TryMatchSeriesCoopBubble(localAppearance, companionIds, ++_coopBubbleSeed, out var matched))
            {
                ShowToast($"[{matched.SeriesName} · 同游彩蛋] {matched.Line}");
                var storyteller = FindActor("guiyuan_storyteller");
                storyteller?.ShowBubble(matched.Line, 3.8f);
                return;
            }
            string fortuneTag = _state.ActiveFortuneOverheadTag(Time.unscaledTimeAsDouble);
            if (!string.IsNullOrEmpty(fortuneTag))
            {
                FindActor("guiyuan_storyteller")?.ShowBubble(fortuneTag, 3.0f);
            }
        }

        private void HandleStartEncounter(CityNpcEncounterDefinition enc, bool useFreePreset)
        {
            if (enc == null) return;
            var matchEntry = TeamUiRoot.Instance?.MatchEntry ?? BattleUiRoot.Instance?.MatchEntry;
            if (matchEntry == null)
            {
                ShowToast("战斗匹配模块尚未就绪，请稍候重试。");
                return;
            }
            uint charPreset = enc.AllowsFreePreset && useFreePreset ? enc.RecommendedCharacterPresetId : 0u;
            uint petPreset = enc.AllowsFreePreset && useFreePreset ? enc.RecommendedPetPresetId : 0u;
            if (matchEntry.StartNpcEncounter(enc.DungeonConfigId, charPreset, petPreset))
            {
                HidePanel();
            }
            else if (!string.IsNullOrEmpty(matchEntry.Status))
            {
                ShowToast(matchEntry.Status);
            }
        }

        private void HandleDrawFortune()
        {
            ulong serverNowMs = WorldMapOverviewModels.EstimateServerNowMs(
                CityTravelUiRoot.Instance?.CurrentOverview,
                CityTravelUiRoot.Instance?.OverviewAgeSeconds ?? 0f);
            if (serverNowMs == 0ul)
                serverNowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var fortune = _state.DrawFortune(serverNowMs, ResolveActiveTideSceneId(), Time.unscaledTimeAsDouble);
            FindActor("guiyuan_storyteller")?.ShowBubble(fortune.OverheadTag, 4.0f);
        }

        private void HandleCopyCallingCard()
        {
            var activeId = ResolveActiveAppearanceId();
            var card = BuildCurrentCallingCard(activeId, ResolveArchiveName(activeId));
            string text = card.FormatShareText();
            GUIUtility.systemCopyBuffer = text;
            ShowToast("已复制人物「江湖名帖」到剪贴板，可直接发给道友！");
        }

        private void DispatchShortcut(CityNpcShortcutKind kind)
        {
            HidePanel();
            switch (kind)
            {
                case CityNpcShortcutKind.EquipmentForge:
                    CoreGameplayUiRoot.Instance?.OpenEquipment();
                    break;
                case CityNpcShortcutKind.Bag:
                    GameplayUiRoot.Instance?.OpenBag();
                    break;
                case CityNpcShortcutKind.MarketTrade:
                    JubaozhaiUiRoot.Instance?.Toggle();
                    break;
                case CityNpcShortcutKind.SpiritShop:
                    SpiritShopUiRoot.Instance?.ShowPanel();
                    break;
                case CityNpcShortcutKind.TravelPass:
                    TravelPassUiRoot.EnsureSpawned();
                    TravelPassUiRoot.Instance?.OpenWindow();
                    break;
                case CityNpcShortcutKind.FormalPvp:
                    CoreGameplayUiRoot.Instance?.OpenFormalPvp();
                    break;
                case CityNpcShortcutKind.CustomDuel:
                    CoreGameplayUiRoot.Instance?.OpenCustomDuel();
                    break;
                case CityNpcShortcutKind.BossTrial:
                    CoreGameplayUiRoot.Instance?.OpenBossTrials();
                    break;
                case CityNpcShortcutKind.WuxingTower:
                    CoreGameplayUiRoot.Instance?.OpenWuxingTower();
                    break;
                case CityNpcShortcutKind.TrialJournal:
                    TrialJournalUiRoot.Instance?.Open();
                    break;
                case CityNpcShortcutKind.GuildWar:
                    GuildUiRoot.Instance?.Toggle();
                    break;
                case CityNpcShortcutKind.Pets:
                    PetUiRoot.Instance?.OpenPanel();
                    break;
                case CityNpcShortcutKind.Skills:
                    SkillUiRoot.Open();
                    break;
                case CityNpcShortcutKind.WorldMap:
                    CityTravelUiRoot.Instance?.Toggle();
                    break;
                case CityNpcShortcutKind.Appearance:
                    AppearanceUiRoot.Instance?.ShowPanel();
                    break;
                case CityNpcShortcutKind.Attributes:
                    AttributeUiRoot.Instance?.OpenPanel();
                    break;
            }
        }

        private void RefreshWindowIfVisible()
        {
            if (_window == null || !_window.IsVisible) return;
            var activeId = ResolveActiveAppearanceId();
            var archiveName = ResolveArchiveName(activeId);
            var card = BuildCurrentCallingCard(activeId, archiveName);
            _window.Refresh(activeId, archiveName, _state, card, ResolveActiveTideSceneId());
        }

        private CharacterCallingCardView BuildCurrentCallingCard(string activeAppearanceId, string archiveName)
        {
            var panel = AttributeClient.Instance?.Panel;
            var appInfo = AppearanceUiRoot.Instance?.Client?.Info;
            var seriesBadges = new List<string>();
            int ownedCount = 0;
            int totalCount = 0;
            if (appInfo != null)
            {
                totalCount = appInfo.Appearances.Count;
                for (int i = 0; i < appInfo.Appearances.Count; ++i)
                    if (appInfo.Appearances[i].Owned) ++ownedCount;
                for (int i = 0; i < appInfo.SeriesProgress.Count; ++i)
                {
                    var sp = appInfo.SeriesProgress[i];
                    if (sp.Completed && !string.IsNullOrEmpty(sp.CompletionBadgeName))
                        seriesBadges.Add(sp.CompletionBadgeName);
                }
            }
            var seasonHonors = new List<string>();
            var seasonSnap = _game?.Season?.Snapshot;
            if (seasonSnap != null)
            {
                for (int i = 0; i < seasonSnap.Honors.Count; ++i)
                {
                    var h = seasonSnap.Honors[i];
                    if (h != null && h.Unlocked && !string.IsNullOrEmpty(h.HonorTitleName))
                        seasonHonors.Add(h.HonorTitleName);
                }
            }
            int sealCount = (int)(_game?.TrialJournal?.Snapshot?.TotalPermanentLitSeals ?? 0u);
            string activePetName = "";
            var petList = PetUiRoot.Instance?.Client?.Pets;
            if (petList != null)
            {
                for (int i = 0; i < petList.Pets.Count; ++i)
                {
                    var p = petList.Pets[i];
                    if (p.PetId == petList.ActivePetId || p.IsActive)
                    {
                        activePetName = !string.IsNullOrWhiteSpace(p.Name) ? p.Name : $"灵宠 #{p.PetTableId}";
                        break;
                    }
                }
            }
            string roleName = _game != null ? _game.ResolveRoleName(_game.PlayerId) : "";
            return new CharacterCallingCardView(
                roleName,
                panel?.Level ?? 0u,
                archiveName,
                activeAppearanceId,
                CityNpcCatalog.DeriveBuildStyleTag(panel),
                _state.SelectedCompanionOathPhrase,
                ownedCount,
                totalCount,
                seriesBadges,
                seasonHonors,
                sealCount,
                activePetName,
                _state.ClearedPuzzleCount,
                _state.ClearedTaleCount,
                _state.ArchivedErrandCount);
        }

        private string ResolveActiveAppearanceId()
        {
            var info = AppearanceUiRoot.Instance?.Client?.Info;
            if (info != null && !string.IsNullOrEmpty(info.ActiveAppearanceId))
                return info.ActiveAppearanceId;
            return _game != null ? _game.ResolveCharacterId(_game.PlayerId) : QdaoCharacterCatalog.DefaultId;
        }

        private static string ResolveArchiveName(string rawAppearanceId)
        {
            if (AppearanceBiographyCatalog.TryGetByRawAppearanceId(rawAppearanceId, out var bio) && bio != null)
                return string.IsNullOrEmpty(bio.Nickname) ? bio.ArchiveName : $"{bio.ArchiveName}（{bio.Nickname}）";
            return QdaoCharacterCatalog.Find(rawAppearanceId)?.Name ?? "归元行者";
        }

        private static uint ResolveActiveTideSceneId()
        {
            var tide = CityTravelUiRoot.Instance?.CurrentOverview?.Tide;
            return tide != null && tide.Active ? tide.SceneConfigId : 0u;
        }

        private GuiyuanCityNpcActor FindActor(string npcId)
        {
            for (int i = 0; i < _actors.Count; ++i)
                if (_actors[i] != null && string.Equals(_actors[i].NpcId, npcId, StringComparison.Ordinal))
                    return _actors[i];
            return null;
        }

        private TianyongPlayerController FindLocalController()
        {
            if (_game?.World != null && _game.World.HasLocalPlayer &&
                _game.World.TryGetActor(_game.World.LocalEntity, out var local) && local?.Go != null)
            {
                return local.Go.GetComponent<TianyongPlayerController>();
            }
            return null;
        }

        private static void ShowToast(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            if (CoreGameplayUiRoot.Instance != null) CoreGameplayUiRoot.Instance.ShowToast(msg);
            else BattleUiRoot.Instance?.ShowToast(msg);
        }
    }
}
