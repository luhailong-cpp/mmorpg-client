using MmorpgClient.Game.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Attribute
{
    /// <summary>
    /// 属性加点 UI 层:自有 Canvas(sortingOrder 160,压在选服 100 / 角色流 150 之上、
    /// 战斗层 200 之下 —— 战斗一开就该盖住属性窗)。
    ///
    /// 生命周期与绑定方式照 <see cref="BattleUiRoot"/>:`[RuntimeInitializeOnLoadMethod]`
    /// 自举单例 + `DontDestroyOnLoad`,每帧 `EnsureBound` 懒绑 <see cref="AttributeClient"/>
    /// (NET 路可能晚于 UI 路初始化)。
    /// </summary>
    public sealed class AttributeUiRoot : MonoBehaviour
    {
        public static AttributeUiRoot Instance { get; private set; }

        private AppBootstrap _app;
        private GameObject _canvasGo;
        private RectTransform _hudRoot;

        private AttributeClient _client;
        private bool _clientBound;

        private UiTextButton _entryButton;
        private AttributePanel _panel;
        private TMP_Text _toastText;
        private Coroutine _toastCo;

        /// <summary>供子面板取 AttributeClient(可能为 null:NET 路尚未初始化)。</summary>
        public AttributeClient Client => _client;

        // ── 生命周期 ────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn() => EnsureSpawned();

        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var go = new GameObject("[AttributeUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<AttributeUiRoot>();
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
                var client = AttributeClient.Instance;
                if (client != null)
                {
                    _client = client;
                    _client.OnPanel += HandlePanel;
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
            _client.OnPanel -= HandlePanel;
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
                "[AttributeUgui]",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);

            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 160; // 选服 100 < 角色流 150 < 本层 < 战斗 200
            canvas.pixelPerfect = true;

            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            // Expand 与 BattleUiRoot / QdaoUguiRuntime 一致:本层有贴右缘的 HUD 入口(x=2350),
            // MatchWidthOrHeight 0.5 在 16:9 / 16:10 会把设计面两侧裁掉 ~170/230 像素,入口直接出屏
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;

            _hudRoot = CreateDesignRoot("HudRoot", _canvasGo.transform);
            var windowRoot = CreateDesignRoot("WindowRoot", _canvasGo.transform);
            var toastRoot = CreateDesignRoot("ToastRoot", _canvasGo.transform);

            _entryButton = BattleUiWidgets.CreateTextButton("AttributeEntry", _hudRoot,
                AttributeUiStyle.EntryX, AttributeUiStyle.EntryY, 150f, 70f, "角色", 26f,
                BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _entryButton.Button.onClick.AddListener(OnEntryClicked);
            _entryButton.SetVisible(false);

            _panel = new AttributePanel(this, windowRoot);

            _toastText = QdaoUguiFactory.CreateText("Toast", toastRoot, 680f, 210f, 1200f, 56f,
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

        private void HandlePanel(AttributePanelInfo panel) => _panel?.ApplyPanel(panel);

        private void HandleAutoSuggestion(uint poolId, IReadOnlyDictionary<uint, uint> suggested)
            => _panel?.ApplyAutoSuggestion(poolId, suggested);

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
            // 战斗屏/观战屏亮着时不显示入口(战斗中服务端也拒绝改属性)
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
