using MmorpgClient.Game.Social;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif
namespace MmorpgClient.UI.Ugui.Social
{
    /// <summary>Explicit offline-only host. Production root never mounts this component.</summary>
    public sealed class SocialPreviewHost:MonoBehaviour
    {
        public SocialState State{get;private set;}
        public SocialWindow Window{get;private set;}
        private void Awake()
        {
            var go=new GameObject("SocialPreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(transform,false);
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=190;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(2560,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var design=QdaoUguiFactory.CreateCenteredRect("DesignRoot",go.transform,2560,1080);SocialUiArt.Art(design,"main-city",0,0,2560,1080);
            State=new SocialState(true);Window=new SocialWindow(design,State);
            if(EventSystem.current==null){var events=new GameObject("SocialPreviewEventSystem",typeof(EventSystem));events.transform.SetParent(transform,false);
#if ENABLE_INPUT_SYSTEM
                events.AddComponent<InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }
            SocialUiArt.Button(design,"SocialPreviewOpen","离线预览 · 仙友会 [C]",921,450,718,107,()=>Window.Show(State.Page),true,size:37);
            Window.Root.SetAsLastSibling();Window.Show();
        }
        private void Update()
        {
            Window.Tick(Time.unscaledTime);
#if ENABLE_INPUT_SYSTEM
            var keys=Keyboard.current;if(keys==null)return;if(keys.escapeKey.wasPressedThisFrame)Window.Back();else if(!SocialUiRoot.IsTyping()&&keys.cKey.wasPressedThisFrame){if(Window.IsVisible)Window.Hide();else Window.Show(State.Page);}
#elif ENABLE_LEGACY_INPUT_MANAGER
            if(Input.GetKeyDown(KeyCode.Escape))Window.Back();else if(!SocialUiRoot.IsTyping()&&Input.GetKeyDown(KeyCode.C)){if(Window.IsVisible)Window.Hide();else Window.Show(State.Page);}
#endif
        }
        public void ResetPreview(){Window.Hide();State.LoadDemo();Window.Show();}
        public void SimulateIncoming()=>State.PreviewIncoming();
        private void OnDestroy()=>Window?.Dispose();
    }
}
