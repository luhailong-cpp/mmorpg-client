using System.Collections.Generic;
using MmorpgClient.Game;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Battle;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Jubaozhai;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 角色形象切换（商城 10 元角色形象卡）与 1 小时免费试用（bit 存储）的功能占位面板。
    /// 网络与协议真相全在 <see cref="AppearanceClient"/>；待 GPT 完成道家 Q 版美术原图与切图后，可直接替换本面板的皮肤纹理。
    /// </summary>
    public sealed class AppearanceUiRoot : MonoBehaviour
    {
        public static AppearanceUiRoot Instance { get; private set; }
        public AppearanceClient Client => _client;
        public bool IsPanelVisible => _window != null && _window.gameObject.activeSelf;

        public const int CanvasSortingOrder = 184;
        public const string EntryName = "AppearanceEntry";
        public const string EntryText = "易容形象";
        public const float ConfirmWindowSeconds = 5f;
        public const float ConfirmMinIntervalSeconds = 0.4f;

        private const float FrameWidth = 1760f, FrameHeight = 860f;
        private static readonly Color ErrorInk = QdaoUguiTheme.Html("#9A442D");
        private static readonly Color TrialGreen = QdaoUguiTheme.Html("#2E6B4F");

        private GameClient _game;
        private AppearanceClient _client;
        private RectTransform _hud;
        private RectTransform _window;
        private RectTransform _listContent;
        private Button _entry;
        private Button _switchBtn, _trialBtn, _cancelTrialBtn, _buyMallBtn;
        private TMP_Text _switchLabel, _trialLabel, _buyMallLabel;
        private TMP_Text _previewName, _previewBadge, _cardInfo, _trialBanner, _status;
        private Image _previewPortrait;
        private readonly List<GameObject> _rowObjects = new();

        private bool _available;
        private string _selectedAppearanceId = "";
        private string _armedAppearanceId = "";
        private float _armedAt = float.NegativeInfinity;
        private bool _confirmShown;
        private string _lastCountdownShown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[AppearanceUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<AppearanceUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);

            var canvasObject = new GameObject("AppearanceCanvas", typeof(RectTransform), typeof(Canvas),
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
            _entry = GameplayUiArt.Button(_hud, EntryText, BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(9),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, TogglePanel, true, fontSize: 32);
            _entry.name = EntryName;
            WorldHudNavigation.Register(WorldHudEntry.Appearance, (RectTransform)_entry.transform, _entry,
                _entry.GetComponentInChildren<TMP_Text>(true));
            BuildWindow(design);
            _hud.gameObject.SetActive(false);
        }

        private void BuildWindow(RectTransform design)
        {
            _window = QdaoUguiFactory.CreateStretch("AppearanceWindow", design, Vector4.zero);
            var shade = _window.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f);
            shade.raycastTarget = true;
            _window.gameObject.AddComponent<GameplayInputBlocker>();

            var frame = QdaoUguiFactory.CreateCenteredRect("AppearanceFrame", _window, FrameWidth, FrameHeight);
            GameplayUiArt.Art(frame, "main_frame", 0, 0, FrameWidth, FrameHeight);
            GameplayUiArt.Text(frame, "道门易容 · 角色形象切换", 64, 34, 800, 56, 40);
            GameplayUiArt.Text(frame, "角色仅持有 1 个永久形象，切换需消耗 1 张商城角色形象卡（10元）；每个形象可免费试用 1 小时。",
                64, 92, 1380, 36, 22, GameplayUiArt.Muted);
            var close = GameplayUiArt.Button(frame, "关闭", FrameWidth - 210, 34, 150, 58, HidePanel);
            close.name = "AppearanceClose";

            // 左侧可滚动形象清单
            var viewport = QdaoUguiFactory.CreateRect("AppearanceListViewport", frame, 58, 146, 660, 650);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _listContent = QdaoUguiFactory.CreateRect("AppearanceListContent", viewport, 0, 0, 660, 650);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _listContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;

            // 右侧大图预览与操作区
            _previewPortrait = QdaoUguiFactory.CreateImage("AppearancePreviewPortrait", frame, 760, 152, 420, 480, null);
            _previewPortrait.preserveAspect = true;
            _previewPortrait.enabled = false;

            _previewName = GameplayUiArt.Text(frame, "请选择角色形象", 1210, 160, 490, 56, 38);
            _previewBadge = GameplayUiArt.Text(frame, "", 1210, 224, 490, 42, 26, TrialGreen);
            _trialBanner = GameplayUiArt.Text(frame, "", 1210, 280, 490, 74, 24, TrialGreen, wrap: true);
            _cancelTrialBtn = GameplayUiArt.Button(frame, "提前恢复原形象", 1210, 366, 280, 58, HandleCancelTrialClicked);
            _cancelTrialBtn.name = "AppearanceCancelTrial";

            _cardInfo = GameplayUiArt.Text(frame, "角色形象卡：—", 1210, 456, 490, 48, 28);
            _buyMallBtn = GameplayUiArt.Button(frame, "去商城购买（10元）", 1210, 514, 340, 62, HandleOpenMallClicked);
            _buyMallBtn.name = "AppearanceOpenMall";
            _buyMallLabel = _buyMallBtn.GetComponentInChildren<TMP_Text>(true);

            _trialBtn = GameplayUiArt.Button(frame, "免费试用 1 小时", 760, 666, 420, 76, HandleStartTrialClicked);
            _trialBtn.name = "AppearanceStartTrial";
            _trialLabel = _trialBtn.GetComponentInChildren<TMP_Text>(true);

            _switchBtn = GameplayUiArt.Button(frame, "切换永久形象（消耗1张卡）", 1210, 666, 490, 76, HandleSwitchClicked, true);
            _switchBtn.name = "AppearanceConfirmSwitch";
            _switchLabel = _switchBtn.GetComponentInChildren<TMP_Text>(true);

            _status = GameplayUiArt.Text(frame, "", 760, 764, 940, 52, 24, GameplayUiArt.Muted, wrap: true);
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
            UpdateTrialCountdownText();
            if (_confirmShown && Time.unscaledTime - _armedAt > ConfirmWindowSeconds) Render();
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            Unbind();
            if (Instance == this) Instance = null;
        }

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
            _armedAppearanceId = "";
            _window.gameObject.SetActive(true);
            _client?.RequestInfo(force: true);
            Render();
        }

        public void HidePanel()
        {
            _armedAt = float.NegativeInfinity;
            _armedAppearanceId = "";
            if (_window != null) _window.gameObject.SetActive(false);
        }

        private void HandleOpenMallClicked()
        {
            HidePanel();
            JubaozhaiUiRoot.Instance?.OpenOfficialMall();
        }

        private void HandleStartTrialClicked()
        {
            if (_client == null || string.IsNullOrEmpty(_selectedAppearanceId)) return;
            _armedAt = float.NegativeInfinity;
            _client.StartTrial(_selectedAppearanceId);
            Render();
        }

        private void HandleCancelTrialClicked()
        {
            if (_client == null) return;
            _armedAt = float.NegativeInfinity;
            _client.CancelTrial();
            Render();
        }

        private void HandleSwitchClicked()
        {
            if (_client == null || string.IsNullOrEmpty(_selectedAppearanceId)) return;
            float now = Time.unscaledTime;
            if (now - _armedAt > ConfirmWindowSeconds || _armedAppearanceId != _selectedAppearanceId)
            {
                _armedAt = now;
                _armedAppearanceId = _selectedAppearanceId;
                Render();
                return;
            }
            if (now - _armedAt < ConfirmMinIntervalSeconds) return;
            _armedAt = float.NegativeInfinity;
            _armedAppearanceId = "";
            _client.SwitchAppearance(_selectedAppearanceId);
            Render();
        }

        private void Render()
        {
            if (_window == null) return;
            var info = _client?.Info;
            RebuildList(info);

            if (info == null)
            {
                _previewName.text = _client?.InfoPending == true ? "正在加载形象清单…" : "暂未获取形象信息";
                _previewBadge.text = "";
                _previewPortrait.sprite = null;
                _previewPortrait.enabled = false;
                _cardInfo.text = "角色形象卡：—";
                _trialBanner.text = "";
                _cancelTrialBtn.gameObject.SetActive(false);
                _trialBtn.interactable = false;
                _switchBtn.interactable = false;
                UpdateStatusText();
                return;
            }

            EnsureValidSelection(info);
            CharacterAppearanceEntry selected = FindSelected(info);
            if (selected != null)
            {
                _previewName.text = selected.Name;
                var sprite = QdaoCharacterCatalog.LoadPortrait(selected.AppearanceId);
                _previewPortrait.sprite = sprite;
                _previewPortrait.enabled = sprite != null;
                bool isPermanent = selected.AppearanceId == info.PermanentAppearanceId;
                bool isTrialing = _client.HasActiveTrial && selected.AppearanceId == info.TrialAppearanceId;
                _previewBadge.text = isTrialing ? "状态：当前正在免费试用中"
                    : isPermanent ? "状态：当前唯一永久形象"
                    : selected.TrialUsed ? "状态：已使用过免费试用（可消耗形象卡永久切换）"
                    : "状态：可免费试用 1 小时 / 可消耗形象卡永久切换";
            }
            else
            {
                _previewName.text = "暂无可选形象";
                _previewBadge.text = "";
                _previewPortrait.sprite = null;
                _previewPortrait.enabled = false;
            }

            string priceText = AppearanceModels.FormatPriceYuan(info.MallPriceFen > 0 ? info.MallPriceFen : 1000ul);
            _cardInfo.text = $"背包持有角色形象卡：{info.OwnedCardCount} 张（每次切换消耗 {info.CardItemCount} 张）";
            _buyMallLabel.text = $"商城购买形象卡（{priceText}）";
            _cancelTrialBtn.gameObject.SetActive(_client.HasActiveTrial);
            _cancelTrialBtn.interactable = _client.Ready && !_client.ActionPending;

            _lastCountdownShown = null;
            UpdateTrialCountdownText();

            bool busy = _client.ActionPending || _client.RequiresReconnect || !_client.Ready;
            bool isTargetPermanent = selected != null && selected.AppearanceId == info.PermanentAppearanceId;
            bool isTargetTrialUsed = selected != null && selected.TrialUsed;

            _trialBtn.interactable = !busy && selected != null && !isTargetPermanent && !isTargetTrialUsed;
            _trialLabel.text = isTargetPermanent ? "已是当前永久形象"
                : isTargetTrialUsed ? "该形象已试用过"
                : "免费试用 1 小时";

            bool canSwitch = !busy && selected != null && !isTargetPermanent;
            if (!canSwitch || _armedAppearanceId != _selectedAppearanceId)
                _armedAt = float.NegativeInfinity;
            _confirmShown = canSwitch && Time.unscaledTime - _armedAt <= ConfirmWindowSeconds;
            _switchBtn.interactable = canSwitch;
            _switchLabel.text = _client.ActionPending ? "处理中…"
                : isTargetPermanent ? "已是当前永久形象"
                : _confirmShown ? $"再次点击确认切换（耗 {info.CardItemCount} 张卡）"
                : $"切换永久形象（消耗 {info.CardItemCount} 张卡）";

            UpdateStatusText();
        }

        private void RebuildList(PlayerAppearanceInfo info)
        {
            for (int i = 0; i < _rowObjects.Count; i++)
                if (_rowObjects[i] != null) Destroy(_rowObjects[i]);
            _rowObjects.Clear();
            if (info == null || _listContent == null) return;

            EnsureValidSelection(info);
            const float rowHeight = 96f;
            const float rowGap = 10f;
            float y = 6f;
            for (int i = 0; i < info.Appearances.Count; i++)
            {
                var entry = info.Appearances[i];
                string id = entry.AppearanceId;
                bool selected = id == _selectedAppearanceId;
                bool isPermanent = id == info.PermanentAppearanceId;
                bool isTrial = _client.HasActiveTrial && id == info.TrialAppearanceId;
                string tag = isTrial ? "【试用中】"
                    : isPermanent ? "【当前形象】"
                    : entry.TrialUsed ? "【已试用】"
                    : "【可试用1小时】";
                string label = $"{entry.Name}  {tag}";
                var btn = GameplayUiArt.Button(_listContent, label, 8f, y, 636f, rowHeight, () =>
                {
                    _selectedAppearanceId = id;
                    _armedAt = float.NegativeInfinity;
                    _armedAppearanceId = "";
                    Render();
                }, primary: selected, fontSize: 26);
                btn.name = "AppearanceOption_" + id;
                _rowObjects.Add(btn.gameObject);
                y += rowHeight + rowGap;
            }
            _listContent.sizeDelta = new Vector2(660f, Mathf.Max(650f, y + 12f));
        }

        private void EnsureValidSelection(PlayerAppearanceInfo info)
        {
            if (info == null || info.Appearances.Count == 0) { _selectedAppearanceId = ""; return; }
            if (!string.IsNullOrEmpty(_selectedAppearanceId))
            {
                for (int i = 0; i < info.Appearances.Count; i++)
                    if (info.Appearances[i].AppearanceId == _selectedAppearanceId) return;
            }
            _selectedAppearanceId = !string.IsNullOrEmpty(info.ActiveAppearanceId)
                ? info.ActiveAppearanceId
                : info.Appearances[0].AppearanceId;
        }

        private CharacterAppearanceEntry FindSelected(PlayerAppearanceInfo info)
        {
            for (int i = 0; i < info.Appearances.Count; i++)
                if (info.Appearances[i].AppearanceId == _selectedAppearanceId) return info.Appearances[i];
            return info.Appearances.Count > 0 ? info.Appearances[0] : null;
        }

        private void UpdateTrialCountdownText()
        {
            if (_trialBanner == null) return;
            string text = _client != null && _client.HasActiveTrial
                ? $"当前正在试用形象，剩余时间：{AppearanceModels.FormatTrialCountdown(_client.TrialRemainingSeconds)}"
                : "当前展示为角色唯一永久形象（未在试用中）。";
            if (text == _lastCountdownShown) return;
            _lastCountdownShown = text;
            _trialBanner.text = text;
        }

        private void UpdateStatusText()
        {
            if (_status == null) return;
            _status.text = _client?.Status ?? "";
            _status.color = (_client?.StatusIsError ?? false) ? ErrorInk : GameplayUiArt.Muted;
        }

        private void Bind(GameClient game)
        {
            if (ReferenceEquals(_game, game)) return;
            Unbind();
            _game = game;
            if (game != null)
            {
                _client = new AppearanceClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                    () => Time.realtimeSinceStartup);
                _client.Changed += Render;
                _client.Notice += HandleNotice;
                game.OnSceneEntered += HandleSceneEntered;
                game.OnDisconnected += HandleDisconnected;
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
                _client.Notice -= HandleNotice;
                _client.Dispose();
                _client = null;
            }
            HidePanel();
        }

        private void HandleSceneEntered(SceneInfoComp _) => _client?.HandleSceneEntered();

        private void HandleDisconnected()
        {
            _client?.HandleDisconnected();
            HidePanel();
        }

        private static void HandleNotice(string text) => BattleUiRoot.Instance?.ShowToast(text);
    }
}
