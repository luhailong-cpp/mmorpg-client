using System;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Social;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
namespace MmorpgClient.UI.Ugui.Social
{
    /// <summary>Production city entry. Never loads previews or creates local players/messages.</summary>
    public sealed class SocialUiRoot:MonoBehaviour
    {
        public static SocialUiRoot Instance{get;private set;}
        public const float EntryX=68,EntryY=912;
        public SocialState State{get;private set;}
        public SocialWindow Window{get;private set;}
        public SocialClient Client{get;private set;}
        public event Action Opened;
        private GameClient _game;private ulong _player;private object _connection;private RectTransform _hud;private bool _available;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void AutoSpawn()
        {if(Instance!=null)return;var go=new GameObject("[SocialUi]");DontDestroyOnLoad(go);go.AddComponent<SocialUiRoot>();}
        private void Awake()
        {
            if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;State=new SocialState();
            var go=new GameObject("SocialCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(transform,false);
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=190;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(2560,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var design=QdaoUguiFactory.CreateCenteredRect("DesignRoot",go.transform,2560,1080);_hud=QdaoUguiFactory.CreateStretch("HudRoot",design,Vector4.zero);QdaoUguiFactory.ConfigureHudCanvas(_hud);
            var entry=GameplayUiArt.Button(_hud,"仙友会 [C]",EntryX,EntryY,BattleUiStyle.HudEntryWidth,BattleUiStyle.HudEntryHeight,Toggle,true,fontSize:32);entry.name="SocialEntry";
            Window=new SocialWindow(design,State);Window.RefreshRequested+=channel=>{if(_available)Client?.Refresh(channel);};Window.SendRequested+=(channel,text)=>{if(_available)Client?.Send(channel,text);};_hud.gameObject.SetActive(false);
        }
        private void Update()
        {
            var game=AppBootstrap.Instance?.GameClient;bool ready=game!=null&&game.InGame&&game.IsGateReady;ulong player=ready?game.PlayerId:0;object connection=game?.GateConnectionIdentity;
            bool differentGame=!ReferenceEquals(_game,game),differentSession=_player!=player||!ReferenceEquals(_connection,connection);
            if(differentGame)
            {
                Client?.Dispose();Client=null;_game=game;
                if(game!=null)Client=new SocialClient(new GameClientBattleTransport(game),State,()=>game.GateConnectionIdentity);
            }
            // Commit all observed identity fields together. Never reset one field inside the comparison of another.
            if(differentGame||differentSession){Client?.ObserveConnection();Client?.Reset();Window.ResetSession();_player=player;_connection=connection;}
            Client?.ObserveConnection();_available=ready&&!(BattleUiRoot.Instance?.IsBattleLayerVisible??false);_hud.gameObject.SetActive(_available);
            if(!_available){HidePanel();return;}Window.Tick(Time.unscaledTime);
            if(!Window.IsVisible&&GameplayInputGate.IsKeyboardBlocked)return;
#if ENABLE_INPUT_SYSTEM
            var keys=Keyboard.current;if(keys==null)return;
            if(keys.escapeKey.wasPressedThisFrame)Window.Back();else if(!IsTyping()&&keys.cKey.wasPressedThisFrame)Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if(Input.GetKeyDown(KeyCode.Escape))Window.Back();else if(!IsTyping()&&Input.GetKeyDown(KeyCode.C))Toggle();
#endif
        }
        public void Toggle()
        {
            if(Window.IsVisible){HidePanel();return;}
            if(!_available)return;
            Mail.MailUiRoot.Instance?.HidePanel();
            Team.TeamUiRoot.Instance?.HidePanel();
            Guild.GuildUiRoot.Instance?.HidePanel();
            Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            Attribute.AttributeUiRoot.Instance?.HidePanel();
            Pet.PetUiRoot.Instance?.HidePanel();
            Window.Show(State.Page);Opened?.Invoke();
        }
        public void HidePanel()=>Window?.Hide();
        public static bool IsTyping()
        {var go=EventSystem.current?.currentSelectedGameObject;return go?.GetComponentInParent<TMP_InputField>()?.isFocused==true||go?.GetComponentInParent<InputField>()?.isFocused==true;}
        private void OnDisable()=>HidePanel();
        private void OnDestroy(){Client?.Dispose();Window?.Dispose();if(Instance==this)Instance=null;}
    }
}
