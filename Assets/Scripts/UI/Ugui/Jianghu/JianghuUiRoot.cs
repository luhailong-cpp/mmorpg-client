using System.Globalization;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Jianghu;
using MmorpgClient.Game.SpiritShop;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.SpiritShop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Jianghu
{
    /// <summary>
    /// 天墉生死擂城内独立 UI 宿主：
    /// - 自动挂载并向 WorldHudNavigation 注册主城「生死擂」入口（归入「历练」分类）；
    /// - 管理主窗口（下达战书 / 江湖恩怨录 / 帮派恩怨墙）以及非阻塞式收到战书/小队确认浮层弹窗；
    /// - 对接灵石百宝阁商品直达（305 金帖生死状 / 306 清风洗尘符）与 PVP 战报回放。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JianghuUiRoot : MonoBehaviour
    {
        public static JianghuUiRoot Instance { get; private set; }

        public const int CanvasSortingOrder = 189;
        public const string EntryName = "JianghuDuelEntry";
        public const string EntryText = "生死擂";

        private GameClient _game;
        private JianghuClient _client;
        private RectTransform _hud;
        private Button _entryBtn;
        private TMP_Text _entryLabel;
        private JianghuDuelWindow _window;

        // Non-blocking incoming invite popup (visible even when main window is closed)
        private RectTransform _popupRoot;
        private TMP_Text _popupTitle;
        private TMP_Text _popupBody;
        private TMP_Text _popupTimer;
        private Button _popupPrimaryBtn;
        private Button _popupSecondaryBtn;
        private Image _popupSecondaryImage;
        private TMP_Text _popupPrimaryLabel;
        private TMP_Text _popupSecondaryLabel;
        private ulong _popupInviteId;
        private JianghuInviteActionKind _popupActionKind;
        private float _nextPopupTickAt;
        private bool _available;

        public JianghuDuelWindow Window => _window;
        public JianghuClient Client => _client;
        public bool IsPanelVisible => _window != null && _window.IsVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn() => EnsureSpawned();

        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var go = new GameObject("[JianghuUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<JianghuUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);

            var canvasObject = new GameObject("JianghuCanvas", typeof(RectTransform), typeof(Canvas),
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

            _entryBtn = GameplayUiArt.Button(_hud, EntryText, BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(11),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, TogglePanel, true, fontSize: 32);
            _entryBtn.name = EntryName;
            _entryLabel = _entryBtn.GetComponentInChildren<TMP_Text>(true);
            WorldHudNavigation.Register(WorldHudEntry.JianghuDuel, (RectTransform)_entryBtn.transform, _entryBtn, _entryLabel);
            _hud.gameObject.SetActive(false);

            _window = new JianghuDuelWindow(design, OpenSpiritShopGoods, PlayReplay);
            BuildIncomingPopup(design);
        }

        private void BuildIncomingPopup(RectTransform design)
        {
            _popupRoot = QdaoUguiFactory.CreateCenteredRect("JianghuIncomingPopup", design, 860f, 420f);
            _popupRoot.anchoredPosition = new Vector2(0f, 110f);
            _popupRoot.gameObject.SetActive(false);

            JianghuDuelWindow.CreateSkinOrFallbackArt(_popupRoot, "PopupFrameBg", "popup_frame", "main_frame", 0f, 0f, 860f, 420f);

            bool hasTitleSkin = JianghuDuelWindow.LoadOptionalSkin("title_plate") != null;
            var plaque = JianghuDuelWindow.CreateSkinOrFallbackArt(_popupRoot, "PopupTitlePlaque", "title_plate", "title_plate",
                190f, -34f, 480f, 96f);
            _popupTitle = GameplayUiArt.Text(plaque.transform, "天墉生死状送达",
                hasTitleSkin ? 104f : 48f, hasTitleSkin ? 20f : 12f,
                hasTitleSkin ? 272f : 384f, hasTitleSkin ? 52f : 64f,
                28f, GameplayUiArt.Cream, alignment: TextAlignmentOptions.Center);
            _popupTitle.fontStyle = FontStyles.Bold;
            _popupTitle.raycastTarget = false;

            _popupBody = GameplayUiArt.Text(_popupRoot, string.Empty, 82f, 92f, 696f, 134f, 23f, GameplayUiArt.Ink, wrap: true);
            _popupBody.raycastTarget = false;

            _popupTimer = GameplayUiArt.Text(_popupRoot, string.Empty, 82f, 234f, 696f, 34f, 21f, QdaoUguiTheme.Html("#A94735"),
                alignment: TextAlignmentOptions.Center);
            _popupTimer.raycastTarget = false;

            _popupPrimaryBtn = JianghuDuelWindow.SkinButton(_popupRoot, "PopupPrimaryBtn", "应战开打",
                74f, 294f, 230f, 66f, HandlePopupPrimary, "btn_primary", "button_primary", true, 23f);
            _popupPrimaryLabel = _popupPrimaryBtn.GetComponentInChildren<TMP_Text>(true);

            _popupSecondaryBtn = JianghuDuelWindow.SkinButton(_popupRoot, "PopupSecondaryBtn", "婉拒战书",
                318f, 294f, 224f, 66f, HandlePopupSecondary, "btn_secondary", "button_secondary", false, 23f);
            _popupSecondaryImage = _popupSecondaryBtn.GetComponent<Image>();
            _popupSecondaryLabel = _popupSecondaryBtn.GetComponentInChildren<TMP_Text>(true);

            JianghuDuelWindow.SkinButton(_popupRoot, "PopupLaterBtn", "稍后查看",
                556f, 294f, 230f, 66f, HandlePopupLater, "btn_secondary", "button_secondary", false, 23f);
        }

        private void Update()
        {
            if (_hud == null || _window == null) { Unbind(); return; }
            Bind(AppBootstrap.Instance?.GameClient);
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = _game != null && _game.InGame && _game.IsGateReady && !battleLayer && (_client?.IsSupported ?? false);
            _hud.gameObject.SetActive(_available);

            if (!_available)
            {
                if (_window.IsVisible) _window.Hide();
                if (_popupRoot != null && _popupRoot.gameObject.activeSelf) _popupRoot.gameObject.SetActive(false);
                return;
            }

            if (_window.IsVisible)
            {
                _window.TickCountdown(Time.unscaledTime);
                if (!CityPanelRegistry.IsTyping())
                {
                    bool esc = false;
#if ENABLE_INPUT_SYSTEM
                    esc = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
                    esc = Input.GetKeyDown(KeyCode.Escape);
#endif
                    if (esc) HidePanel();
                }
            }

            if (Time.unscaledTime >= _nextPopupTickAt)
            {
                _nextPopupTickAt = Time.unscaledTime + 0.5f;
                RefreshPopupAndBadge();
            }
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            Unbind();
            _window?.Dispose();
            _window = null;
            if (Instance == this) Instance = null;
        }

        public void TogglePanel()
        {
            if (IsPanelVisible) HidePanel();
            else OpenWindow();
        }

        public void OpenWindow(JianghuWindowTab tab = JianghuWindowTab.Challenge)
        {
            Bind(AppBootstrap.Instance?.GameClient);
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = _game != null && _game.InGame && _game.IsGateReady && !battleLayer && (_client?.IsSupported ?? false);
            if (!_available || _window == null) return;
            CityPanelRegistry.HideOthers(this);
            if (_popupRoot != null) _popupRoot.gameObject.SetActive(false);
            _window.Show(tab);
        }

        public void OpenChallengeFor(ulong targetPlayerId)
        {
            Bind(AppBootstrap.Instance?.GameClient);
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = _game != null && _game.InGame && _game.IsGateReady && !battleLayer && (_client?.IsSupported ?? false);
            if (!_available || _window == null) return;
            CityPanelRegistry.HideOthers(this);
            if (_popupRoot != null) _popupRoot.gameObject.SetActive(false);
            _window.Show(JianghuWindowTab.Challenge, targetPlayerId);
        }

        public void HidePanel() => _window?.Hide();

        private void OpenSpiritShopGoods(uint goodsId)
            => SpiritShopUiRoot.Instance?.OpenTab(SpiritShopModels.TabAppearanceAndConvenience, goodsId);

        private void PlayReplay(ulong battleId)
        {
            if (battleId == 0) return;
            BattleReportClient.Instance?.FetchReplay(battleId);
        }

        private void Bind(GameClient game)
        {
            if (_game == game) return;
            if (_client != null)
            {
                _client.Changed -= RefreshPopupAndBadge;
                _client.InvitePopupArrived -= HandleInvitePopupArrived;
            }
            _game = game;
            _client = game?.Jianghu;
            _window?.Bind(_client);
            if (_client != null)
            {
                _client.Changed += RefreshPopupAndBadge;
                _client.InvitePopupArrived += HandleInvitePopupArrived;
            }
            RefreshPopupAndBadge();
        }

        private void Unbind()
        {
            if (_client != null)
            {
                _client.Changed -= RefreshPopupAndBadge;
                _client.InvitePopupArrived -= HandleInvitePopupArrived;
            }
            _game = null;
            _client = null;
            _window?.Bind(null);
        }

        private void HandleInvitePopupArrived(global::Match.JianghuDuelInviteView invite)
        {
            RefreshPopupAndBadge();
        }

        private void RefreshPopupAndBadge()
        {
            var pending = _available ? _client?.PendingActionInvite : null;
            if (_entryLabel != null)
                _entryLabel.text = pending != null ? "生死擂 · 战书" : EntryText;

            if (_popupRoot == null) return;
            if (pending == null || IsPanelVisible || !_available)
            {
                _popupRoot.gameObject.SetActive(false);
                _popupInviteId = 0;
                _popupActionKind = JianghuInviteActionKind.None;
                return;
            }

            var kind = JianghuModels.InviteNeedsMyAction(pending, _client.PlayerId);
            if (kind == JianghuInviteActionKind.None)
            {
                _popupRoot.gameObject.SetActive(false);
                return;
            }

            _popupInviteId = pending.DuelInviteId;
            _popupActionKind = kind;

            string tierLabel = JianghuModels.FormatWritShort(pending.WritTier);
            string challenger = string.IsNullOrWhiteSpace(pending.ChallengerCaptainName)
                ? $"道友 {pending.ChallengerCaptainId.ToString(CultureInfo.InvariantCulture)}"
                : pending.ChallengerCaptainName.Trim();
            string target = string.IsNullOrWhiteSpace(pending.TargetCaptainName)
                ? $"道友 {pending.TargetCaptainId.ToString(CultureInfo.InvariantCulture)}"
                : pending.TargetCaptainName.Trim();

            if (kind == JianghuInviteActionKind.CaptainRespond)
            {
                _popupTitle.text = pending.WritTier == global::Match.JianghuWritTier.GoldItem
                    ? "金帖生死状送达"
                    : "收到江湖战书";
                _popupBody.text =
                    $"【{challenger}】向你递交了一封 [{tierLabel} · {pending.TeamSize}V{pending.TeamSize}] 天墉生死状。\n" +
                    "双方自愿方可成局开战；若暂不方便，可点击「婉拒战书」，对双方均不产生任何消耗或惩罚。";
                if (_popupPrimaryLabel != null) _popupPrimaryLabel.text = "应战开打";
                if (_popupSecondaryLabel != null) _popupSecondaryLabel.text = "婉拒战书";
                ApplyPopupSecondarySkin(isDanger: false);
            }
            else
            {
                _popupTitle.text = "小队全员确认";
                _popupBody.text =
                    $"本队参与 [{tierLabel} · {pending.TeamSize}V{pending.TeamSize}] 天墉生死擂（{challenger} 对阵 {target}）。\n" +
                    "须全队成员在 30 秒内确认同意方可成局开战；任意队员退出则整单自动作废且无任何扣费。";
                if (_popupPrimaryLabel != null) _popupPrimaryLabel.text = "同意参战";
                if (_popupSecondaryLabel != null) _popupSecondaryLabel.text = "退出作废";
                ApplyPopupSecondarySkin(isDanger: true);
            }

            uint remSec = JianghuModels.RemainingSeconds(JianghuModels.InviteDeadlineMs(pending), _client.EstimatedServerNowMs);
            _popupTimer.text = remSec > 0 ? $"剩余响应时间：{remSec.ToString(CultureInfo.InvariantCulture)} 秒" : "即将超时自动作废";
            bool busy = _client.WritePending || _client.InBattle;
            _popupPrimaryBtn.interactable = !busy;
            _popupSecondaryBtn.interactable = !busy;
            _popupRoot.gameObject.SetActive(true);
        }

        private void HandlePopupPrimary()
        {
            if (_client == null || _popupInviteId == 0) return;
            if (_popupActionKind == JianghuInviteActionKind.CaptainRespond)
                _client.RespondDuelInvite(_popupInviteId, true);
            else if (_popupActionKind == JianghuInviteActionKind.MemberConfirm)
                _client.ConfirmTeamMember(_popupInviteId, true);
            _popupRoot.gameObject.SetActive(false);
        }

        private void HandlePopupSecondary()
        {
            if (_client == null || _popupInviteId == 0) return;
            if (_popupActionKind == JianghuInviteActionKind.CaptainRespond)
                _client.RespondDuelInvite(_popupInviteId, false);
            else if (_popupActionKind == JianghuInviteActionKind.MemberConfirm)
                _client.ConfirmTeamMember(_popupInviteId, false);
            _popupRoot.gameObject.SetActive(false);
        }

        private void HandlePopupLater()
        {
            if (_popupInviteId != 0) _client?.SuppressPopupFor(_popupInviteId);
            _popupRoot.gameObject.SetActive(false);
        }

        private void ApplyPopupSecondarySkin(bool isDanger)
        {
            Sprite dangerSprite = isDanger ? JianghuDuelWindow.LoadOptionalSkin("btn_danger") : null;
            if (_popupSecondaryImage != null)
            {
                Sprite sprite = dangerSprite
                    ?? JianghuDuelWindow.LoadOptionalSkin("btn_secondary")
                    ?? GameplayUiArt.Load("button_secondary");
                if (sprite != null)
                {
                    _popupSecondaryImage.sprite = sprite;
                    _popupSecondaryImage.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                }
            }
            if (_popupSecondaryLabel != null)
                _popupSecondaryLabel.color = dangerSprite != null ? GameplayUiArt.Cream : GameplayUiArt.Ink;
        }
    }
}
