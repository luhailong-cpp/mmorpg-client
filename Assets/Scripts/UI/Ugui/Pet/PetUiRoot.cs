using System.Collections;
using System.Collections.Generic;
using MmorpgClient.Game.Pet;
using MmorpgClient.Game.Skills;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    /// <summary>
    /// 宝宝 UI 层:自有 Canvas(sortingOrder 170,紧挨属性窗 160 之上、战斗层 200 之下 ——
    /// 两个窗不会同时开,谁后点谁在上,战斗一开都盖住)。
    ///
    /// 生命周期与绑定方式照 <see cref="Attribute.AttributeUiRoot"/>:`[RuntimeInitializeOnLoadMethod]`
    /// 自举单例 + `DontDestroyOnLoad`,每帧 `EnsureBound` 懒绑 <see cref="PetClient"/>
    /// (NET 路可能晚于 UI 路初始化)。
    /// </summary>
    public sealed class PetUiRoot : MonoBehaviour
    {
        public static PetUiRoot Instance { get; private set; }

        private AppBootstrap _app;
        private GameObject _canvasGo;
        private GameObject _cultivationCanvasGo;
        private RectTransform _hudRoot;

        private PetClient _client;
        private bool _clientBound;

        private UiTextButton _entryButton;
        private PetPanel _panel;
        private PetCultivationPanel _cultivation;
        private TMP_Text _toastText;
        private Coroutine _toastCo;

        /// <summary>供子面板取 PetClient(可能为 null:NET 路尚未初始化)。</summary>
        public PetClient Client => _client;

        /// <summary>关掉宝宝窗(属性窗打开时调;理由见 AttributeUiRoot.HidePanel)。</summary>
        public void HidePanel() { _panel?.Hide(); _cultivation?.Hide(); }

        /// <summary>从技能页返回当前选中的宝宝。</summary>
        public void OpenPanel(bool allocation = true)
            => OpenPage(allocation ? PetCultivationPage.FourStats : PetCultivationPage.Attributes);

        /// <summary>正式宝宝培养导航，包含条目点、四维和当前宝宝的权威技能书。</summary>
        public void OpenPage(PetCultivationPage page = PetCultivationPage.EntryPoints)
        {
            EnsureBound();
            if (_clientBound && _app?.GameClient?.InGame == true &&
                _app.GameClient.IsGateReady)
            {
                _panel?.Hide();
                Attribute.AttributeUiRoot.Instance?.HidePanel();
                Skills.SkillUiRoot.Instance?.HidePanel();
                Guild.GuildUiRoot.Instance?.HidePanel();
                Rank.RankUiRoot.Instance?.HidePanel();
                Gameplay.GameplayUiRoot.Instance?.HidePanel();
                Gameplay.MysteryMerchantUiRoot.Instance?.HidePanel();
                BattleScripts.BattleScriptUiRoot.Instance?.HidePanel();
                _cultivation?.Show(page);
            }
        }

        // ── 生命周期 ────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn() => EnsureSpawned();

        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var go = new GameObject("[PetUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<PetUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            EnsureBound();
            RefreshEntryVisibility();
            _cultivation?.Tick();
        }

        private void OnDestroy()
        {
            UnbindClient();
            _cultivation?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void EnsureBound()
        {
            if (_app == null)
            {
                _app = FindAnyObjectByType<AppBootstrap>();
                if (_app == null) return;
            }
            if (_canvasGo == null) BuildCanvas();

            if (!_clientBound)
            {
                var client = PetClient.Instance;
                if (client != null)
                {
                    _client = client;
                    _client.OnList += HandleList;
                    _client.OnAutoSuggestion += HandleAutoSuggestion;
                    _client.OnBusyChanged += HandleBusyChanged;
                    _client.OnError += HandleError;
                    _client.OnShopChanged += HandleShopChanged;
                    _client.OnPurchased += HandlePurchased;
                    _clientBound = true;
                }
            }
        }

        private void UnbindClient()
        {
            if (!_clientBound || _client == null) return;
            _client.OnList -= HandleList;
            _client.OnAutoSuggestion -= HandleAutoSuggestion;
            _client.OnBusyChanged -= HandleBusyChanged;
            _client.OnError -= HandleError;
            _client.OnShopChanged -= HandleShopChanged;
            _client.OnPurchased -= HandlePurchased;
            _clientBound = false;
        }

        // ── Canvas ──────────────────────────────────────────

        private void BuildCanvas()
        {
            EnsureEventSystem();

            _canvasGo = new GameObject(
                "[PetUgui]",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);

            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 170; // 属性 160 < 本层 < 战斗 200
            canvas.pixelPerfect = true;

            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            // Expand:本层有贴右缘的 HUD 入口,MatchWidthOrHeight 0.5 在 16:9 会把入口裁出屏
            // (AttributeUiRoot 同一条注释背后的同一个坑)
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;

            _hudRoot = CreateDesignRoot("HudRoot", _canvasGo.transform);
            QdaoUguiFactory.ConfigureHudCanvas(_hudRoot);
            var windowRoot = CreateDesignRoot("WindowRoot", _canvasGo.transform);
            // The responsive cultivation page stays at native 1080-height pixels. The legacy
            // 2560-wide HUD canvas scales down at 1920, which would rasterize Text too small
            // before the page compensates its geometry with an inverse local scale.
            _cultivationCanvasGo = new GameObject("[PetCultivationUgui]",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _cultivationCanvasGo.transform.SetParent(transform, false);
            var cultivationCanvas = _cultivationCanvasGo.GetComponent<Canvas>();
            cultivationCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            cultivationCanvas.sortingOrder = 170;
            cultivationCanvas.pixelPerfect = true;
            var cultivationScaler = _cultivationCanvasGo.GetComponent<CanvasScaler>();
            cultivationScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cultivationScaler.referenceResolution = new Vector2(1920f, 1080f);
            cultivationScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            cultivationScaler.referencePixelsPerUnit = 100f;
            var cultivationRoot = CreateDesignRoot("CultivationRoot", _cultivationCanvasGo.transform);
            var toastRoot = CreateDesignRoot("ToastRoot", _cultivationCanvasGo.transform);

            _entryButton = BattleUiWidgets.CreateFramedTextButton("PetEntry", _hudRoot,
                PetUiStyle.EntryX, PetUiStyle.EntryY,
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, "宝宝", BattleUiStyle.HudEntryFontSize,
                BattleUiStyle.HudEntryPlate, BattleUiStyle.HudEntryFrameColor, BattleUiStyle.HudEntryText,
                BattleUiStyle.HudEntryFrame);
            _entryButton.Button.onClick.AddListener(OnEntryClicked);
            _entryButton.SetVisible(false);
            WorldHudNavigation.Register(WorldHudEntry.Pet, _entryButton.Rect, _entryButton.Button, _entryButton.Label);

            _panel = new PetPanel(this, windowRoot);
            _cultivation = new PetCultivationPanel(cultivationRoot, () => _client, () => SkillBookClient.Instance,
                () => BattleUiRoot.Instance?.IsBattleLayerVisible == true,
                () => _panel.ShowForPet(_cultivation.SelectedPetId));

            _toastText = QdaoUguiFactory.CreateText("Toast", toastRoot, 680f, 270f, 1200f, 56f,
                string.Empty, 26f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
        }

        private static RectTransform CreateDesignRoot(string name, UnityEngine.Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            return rect;
        }

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("[EventSystem]", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
        }

        // ── 事件 ────────────────────────────────────────────

        private void HandleList(PetListInfo list) => _panel?.ApplyList(list);
        private void HandleShopChanged() => _panel?.RefreshShop();
        private void HandlePurchased(ulong petId) => _panel?.ApplyPurchased(petId);

        private void HandleAutoSuggestion(ulong petId, IReadOnlyDictionary<uint, uint> suggested)
            => _panel?.ApplyAutoSuggestion(petId, suggested);

        private void HandleBusyChanged(bool busy) => _panel?.ApplyBusy(busy);

        private void HandleError(string message)
        {
            _panel?.ApplyError(message);
            ShowToast(message, true);
        }

        // ── HUD ─────────────────────────────────────────────

        private void OnEntryClicked()
        {
            if (_cultivation?.IsVisible == true) _cultivation.RequestClose();
            else OpenPage();
        }

        private void RefreshEntryVisibility()
        {
            if (_entryButton == null) return;
            bool inGame = _app != null && _app.GameClient != null && _app.GameClient.InGame;
            // 主城入口交给世界 HUD；战斗页有正式的只读查看入口。
            bool battleBusy = BattleUiRoot.Instance != null && BattleUiRoot.Instance.IsBattleLayerVisible;
            bool visible = _clientBound && inGame && !battleBusy;
            if (_canvasGo != null) _canvasGo.GetComponent<Canvas>().sortingOrder = battleBusy ? 210 : 170;
            if (_cultivationCanvasGo != null) _cultivationCanvasGo.GetComponent<Canvas>().sortingOrder = battleBusy ? 210 : 170;
            _entryButton.SetVisible(visible);
            if (!visible && _panel != null && _panel.IsVisible) _panel.Hide();
            if ((!_clientBound || !inGame) && _cultivation?.IsVisible == true) _cultivation.Hide();
        }

        public void ShowToast(string message, bool isError = false)
        {
            if (_toastText == null) return;
            _toastText.text = message ?? string.Empty;
            var color = isError ? BattleUiStyle.DamageText : QdaoUguiTheme.Cream;
            color.a = 1f;
            _toastText.color = color;
            if (_toastCo != null) StopCoroutine(_toastCo);
            _toastCo = StartCoroutine(CoToast());
        }

        private IEnumerator CoToast()
        {
            yield return new WaitForSecondsRealtime(2.2f);
            const float fade = 0.5f;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < fade)
            {
                if (_toastText == null) yield break;
                var color = _toastText.color;
                color.a = 1f - (Time.realtimeSinceStartup - start) / fade;
                _toastText.color = color;
                yield return null;
            }
            if (_toastText != null) _toastText.text = string.Empty;
            _toastCo = null;
        }
    }
}
