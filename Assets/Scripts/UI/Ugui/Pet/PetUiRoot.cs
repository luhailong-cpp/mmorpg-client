using System.Collections;
using System.Collections.Generic;
using MmorpgClient.Game.Pet;
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
        private RectTransform _hudRoot;

        private PetClient _client;
        private bool _clientBound;

        private UiTextButton _entryButton;
        private PetPanel _panel;
        private TMP_Text _toastText;
        private Coroutine _toastCo;

        /// <summary>供子面板取 PetClient(可能为 null:NET 路尚未初始化)。</summary>
        public PetClient Client => _client;

        /// <summary>关掉宝宝窗(属性窗打开时调;理由见 AttributeUiRoot.HidePanel)。</summary>
        public void HidePanel() => _panel?.Hide();

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
        }

        private void OnDestroy()
        {
            UnbindClient();
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
            var toastRoot = CreateDesignRoot("ToastRoot", _canvasGo.transform);

            _entryButton = BattleUiWidgets.CreateFramedTextButton("PetEntry", _hudRoot,
                PetUiStyle.EntryX, PetUiStyle.EntryY,
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, "宝宝", BattleUiStyle.HudEntryFontSize,
                BattleUiStyle.HudEntryPlate, BattleUiStyle.HudEntryFrameColor, BattleUiStyle.HudEntryText,
                BattleUiStyle.HudEntryFrame);
            _entryButton.Button.onClick.AddListener(OnEntryClicked);
            _entryButton.SetVisible(false);

            _panel = new PetPanel(this, windowRoot);

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

        private void HandleAutoSuggestion(ulong petId, IReadOnlyDictionary<uint, uint> suggested)
            => _panel?.ApplyAutoSuggestion(petId, suggested);

        private void HandleBusyChanged(bool busy) => _panel?.ApplyBusy(busy);

        private void HandleError(string message)
        {
            _panel?.SetStatus(message, true);
            ShowToast(message, true);
        }

        // ── HUD ─────────────────────────────────────────────

        private void OnEntryClicked() => _panel?.Toggle();

        private void RefreshEntryVisibility()
        {
            if (_entryButton == null) return;
            bool inGame = _app != null && _app.GameClient != null && _app.GameClient.InGame;
            // 战斗屏/观战屏亮着时不显示入口(战斗中服务端也拒绝改宝宝)
            bool battleBusy = BattleUiRoot.Instance != null && BattleUiRoot.Instance.IsBattleLayerVisible;
            bool visible = _clientBound && inGame && !battleBusy;
            _entryButton.SetVisible(visible);
            if (!visible && _panel != null && _panel.IsVisible) _panel.Hide();
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
