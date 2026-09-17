using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace MmorpgClient.UI.Ugui.Mail
{
    /// <summary>Explicit offline-only host, never auto-spawned by gameplay.</summary>
    public sealed class MailPreviewHost : MonoBehaviour
    {
        public MailWindow Window { get; private set; }
        private Button _open;
        private void Awake()=>Initialize();
        public void Initialize()
        {
            if(Window!=null)return;
            var go=new GameObject("OfflineMailPreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            go.transform.SetParent(transform,false);var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=360;
            MailUiRoot.ConfigureScaler(go.GetComponent<CanvasScaler>());
            var design=QdaoUguiFactory.CreateCenteredRect("DesignRoot",go.transform,2560,1080);
            MailUiArt.Art(design,"main-city",0,0,2560,1080);
            _open=MailUiArt.Button(design,"OpenOfflineMail","打开离线仙笺",1020,490,520,100,()=>Window.Show(),true,37);
            Window=new MailWindow(design);Window.State.LoadDemo();
            MailUiArt.Label(design,"OfflineMailBadge","离线邮件样例 · 不连接真实账号或背包",26,11,1180,48,24,MailUiArt.Cream);
            if(EventSystem.current==null){var events=new GameObject("MailPreviewEventSystem",typeof(EventSystem));events.transform.SetParent(transform,false);
#if ENABLE_INPUT_SYSTEM
                events.AddComponent<InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }
            Window.Show();
        }
        public void ResetDemo(){Initialize();Window.State.LoadDemo();Window.Show();}
        private void Update()
        {
            if(Window==null)return;Window.Tick();_open.interactable=!Window.IsVisible;
            if(MailUiRoot.IsTyping())return;
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;if(keyboard==null)return;
            if(keyboard.escapeKey.wasPressedThisFrame)Window.Back();
            else if(keyboard.nKey.wasPressedThisFrame){if(Window.IsVisible)Window.Hide();else Window.Show();}
#elif ENABLE_LEGACY_INPUT_MANAGER
            if(Input.GetKeyDown(KeyCode.Escape))Window.Back();
            else if(Input.GetKeyDown(KeyCode.N)){if(Window.IsVisible)Window.Hide();else Window.Show();}
#endif
        }
        private void OnDestroy(){Window?.Dispose();Window=null;}
    }
}
