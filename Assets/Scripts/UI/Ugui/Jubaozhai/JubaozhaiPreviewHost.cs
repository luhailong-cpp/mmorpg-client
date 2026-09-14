using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace MmorpgClient.UI.Ugui.Jubaozhai
{
    /// <summary>Explicit offline preview host. Never auto-spawned and never mounted by the production root.</summary>
    public sealed class JubaozhaiPreviewHost : MonoBehaviour
    {
        public JubaozhaiWindow Window { get; private set; }
        private Button _entry;

        private void Awake()
        {
            var canvasObject = new GameObject("JubaozhaiPreviewCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 188;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            var background = design.gameObject.AddComponent<Image>();
            background.color = new Color(.08f, .20f, .16f);
            background.raycastTarget = false;
            _entry = JubaozhaiArt.Button(design, "JubaoPreviewOpen", "离线演示 · 聚宝斋 [U]", 916, 464, 728, 120,
                () => Window.Show(), true, fontSize: 38);
            Window = new JubaozhaiWindow(design);
            Window.State.LoadDemo();
            if (EventSystem.current == null)
            {
                var events = new GameObject("JubaozhaiPreviewEventSystem", typeof(EventSystem));
                events.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
                events.AddComponent<InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }
            Window.Show();
        }

        private void Update()
        {
            _entry.interactable = !Window.IsVisible;
            Window.Tick(Time.unscaledTime);
            if (JubaozhaiUiRoot.IsTyping()) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) Window.Back();
            else if (keys.uKey.wasPressedThisFrame) { if (Window.IsVisible) Window.Hide(); else Window.Show(); }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) Window.Back();
            else if (Input.GetKeyDown(KeyCode.U)) { if (Window.IsVisible) Window.Hide(); else Window.Show(); }
#endif
        }
        private void OnDestroy() => Window?.Dispose();
    }
}



