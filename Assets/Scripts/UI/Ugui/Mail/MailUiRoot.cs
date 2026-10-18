using MmorpgClient.Game;
using MmorpgClient.Game.Mail;
using MmorpgClient.Game.Battle;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Mail
{
    /// <summary>正式邮件入口，经现有gate传输读取权威邮件，不加载离线样例。</summary>
    public sealed class MailUiRoot : MonoBehaviour
    {
        public static MailUiRoot Instance { get; private set; }
        public const float EntryX=68,EntryY=808;
        public MailWindow Window=>_window;
        public MailUiState State=>_window?.State;
        private MailWindow _window;
        private RectTransform _hud;
        private GameClient _game;
        private object _gate;
        private ulong _player;
        private bool _available;
        private MailClient _client;
        private TMP_Text _entryText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if(Instance!=null)return;var go=new GameObject("[MailUi]");DontDestroyOnLoad(go);go.AddComponent<MailUiRoot>();
        }
        private void Awake()
        {
            if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;
            var go=new GameObject("MailCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            go.transform.SetParent(transform,false);var canvas=go.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=189;
            ConfigureScaler(go.GetComponent<CanvasScaler>());
            var design=QdaoUguiFactory.CreateCenteredRect("DesignRoot",go.transform,2560,1080);
            _hud=QdaoUguiFactory.CreateStretch("HudRoot",design,Vector4.zero);QdaoUguiFactory.ConfigureHudCanvas(_hud);
            var entry=GameplayUiArt.Button(_hud,"仙笺 [N]",EntryX,EntryY,BattleUiStyle.HudEntryWidth,BattleUiStyle.HudEntryHeight,Toggle,true,fontSize:32);
            entry.name="MailEntry";_window=new MailWindow(design);_hud.gameObject.SetActive(false);
            _entryText=entry.GetComponentInChildren<TMP_Text>();
        }
        public static void ConfigureScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(2560,1080);
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        }
        private void Update()
        {
            var game=AppBootstrap.Instance?.GameClient;
            if(_game!=game){if(_game!=null)_game.OnDisconnected-=ResetSession;ResetSession();_game=game;
                if(_game!=null)_game.OnDisconnected+=ResetSession;}
            object gate=game?.GateConnectionIdentity;
            if(!ReferenceEquals(_gate,gate)){ResetSession();_gate=gate;}
            bool inGame=game!=null&&game.InGame&&game.IsGateReady;
            ulong player=inGame?game.PlayerId:0;
            if(_player!=player){ResetSession();_player=player;_gate=gate;State.Reset(player);
                if(player!=0){_client=new MailClient(new GameClientBattleTransport(game),()=>game.GateConnectionIdentity,State);_client.AssetRefreshRequired+=RefreshAssets;State.Refresh();}}
            _available=inGame&&!(BattleUiRoot.Instance?.IsBattleLayerVisible??false);
            _hud.gameObject.SetActive(_available);_window.Tick();_client?.Tick(_window.IsVisible);
            if(_entryText!=null)_entryText.text=_client?.HasUnseenNotice==true?"仙笺 [N] · 新":State.UnreadCount>0?$"仙笺 [N] · {State.UnreadCount}":"仙笺 [N]";
            if(!_available){HidePanel();return;}
            if(IsTyping()||(!_window.IsVisible&&GameplayInputGate.IsKeyboardBlocked))return;
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;if(keyboard==null)return;
            if(keyboard.escapeKey.wasPressedThisFrame)_window.Back();else if(keyboard.nKey.wasPressedThisFrame)Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if(Input.GetKeyDown(KeyCode.Escape))_window.Back();else if(Input.GetKeyDown(KeyCode.N))Toggle();
#endif
        }
        public void Toggle()
        {
            if(_window.IsVisible){HidePanel();return;}if(!_available)return;
            Team.TeamUiRoot.Instance?.HidePanel();Guild.GuildUiRoot.Instance?.HidePanel();Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();Social.SocialUiRoot.Instance?.HidePanel();
            Rank.RankUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();CityTravelUiRoot.Instance?.HidePanel();AttributeUiRoot.Instance?.HidePanel();PetUiRoot.Instance?.HidePanel();
            Skills.SkillUiRoot.Instance?.HidePanel();
            _window.Show();
            if(!State.IsBusy)State.Refresh();
        }
        public void HidePanel()=>_window?.Hide();
        private void RefreshAssets()
        {
            if(_game==null||!_game.InGame||!_game.IsGateReady||_game.PlayerId!=_player||!ReferenceEquals(_gate,_game.GateConnectionIdentity))return;
            _game.Features.RequestBag();
        }
        private void DisposeClient(){if(_client==null)return;_client.AssetRefreshRequired-=RefreshAssets;_client.Dispose();_client=null;}
        private void ResetSession(){DisposeClient();_player=0;_gate=null;_available=false;_window?.ResetSession();if(_hud!=null)_hud.gameObject.SetActive(false);}
        public static bool IsTyping()
        {
            var go=EventSystem.current?.currentSelectedGameObject;
            return go!=null&&(go.GetComponentInParent<TMP_InputField>()?.isFocused==true||go.GetComponentInParent<InputField>()?.isFocused==true);
        }
        private void OnDisable()=>HidePanel();
        private void OnDestroy()
        {
            if(_game!=null)_game.OnDisconnected-=ResetSession;DisposeClient();_window?.Dispose();if(Instance==this)Instance=null;
        }
    }
}
