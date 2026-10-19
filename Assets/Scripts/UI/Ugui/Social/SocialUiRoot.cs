using System;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.Social;
using MmorpgClient.Game.Sync;
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
        public FriendClient Friends{get;private set;}
        public FriendRequestsWindow FriendRequests{get;private set;}
        public event Action Opened;
        private GameClient _game;private ulong _player;private object _connection;private RectTransform _hud;private bool _available;
        // 落地恢复的好友申请接线(LandingSyncBindings.BindFriendRequests):随 Friends 一起建、在它销毁之前解除;
        // Toggle / HidePanel / OnDisable / 会话复位都不碰它(FriendClient.Reset 不清钩子)。
        private IDisposable _friendSync;
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
            Window.TeamInviteRequested += id => { if (_available) Team.TeamUiRoot.Instance?.OpenInviteForPlayer(id); };
            FriendRequests = new FriendRequestsWindow(design);
            Window.FriendRequestsRequested += OpenFriendRequests;
        }
        private void Update()
        {
            var game=AppBootstrap.Instance?.GameClient;bool ready=game!=null&&game.InGame&&game.IsGateReady;ulong player=ready?game.PlayerId:0;object connection=game?.GateConnectionIdentity;
            bool differentGame=!ReferenceEquals(_game,game),differentSession=_player!=player||!ReferenceEquals(_connection,connection);
            if(differentGame)
            {
                _friendSync?.Dispose();_friendSync=null;Client?.Dispose();Client=null;Friends?.Dispose();Friends=null;_game=game;
                if(game!=null)
                {
                    Client=new SocialClient(new GameClientBattleTransport(game),State,()=>game.GateConnectionIdentity);
                    Friends=new FriendClient(new GameClientBattleTransport(game),()=>game.GateConnectionIdentity,
                        ()=>game.InGame&&game.IsGateReady);
                    if(game.LandingSync!=null)_friendSync=LandingSyncBindings.BindFriendRequests(game.LandingSync,Friends);
                }
                FriendRequests.Bind(Friends);
            }
            // Commit all observed identity fields together. Never reset one field inside the comparison of another.
            if(differentGame||differentSession){Client?.ObserveConnection();Client?.Reset();Friends?.ObserveConnection();Friends?.Reset();Window.ResetSession();FriendRequests.ResetSession();_player=player;_connection=connection;}
            Friends?.ObserveConnection();
            Client?.ObserveConnection();_available=ready&&!(BattleUiRoot.Instance?.IsBattleLayerVisible??false);_hud.gameObject.SetActive(_available);
            if(!_available){HidePanel();return;}Window.Tick(Time.unscaledTime);
            if(!Window.IsVisible&&!FriendRequests.IsVisible&&GameplayInputGate.IsKeyboardBlocked)return;
#if ENABLE_INPUT_SYSTEM
            var keys=Keyboard.current;if(keys==null)return;
            if(keys.escapeKey.wasPressedThisFrame){if(FriendRequests.IsVisible)FriendRequests.Hide();else Window.Back();}else if(!IsTyping()&&keys.cKey.wasPressedThisFrame)Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if(Input.GetKeyDown(KeyCode.Escape)){if(FriendRequests.IsVisible)FriendRequests.Hide();else Window.Back();}else if(!IsTyping()&&Input.GetKeyDown(KeyCode.C))Toggle();
#endif
        }
        public void Toggle()
        {
            if(Window.IsVisible||FriendRequests.IsVisible){HidePanel();return;}
            if(!_available)return;
            OpenPanel(State.Page);
        }
        /// <summary>从装备卡进入世界聊天，只填写草稿，发送仍须玩家确认。</summary>
        public bool TryComposeEquipment(BagItemInfo item)
        {
            var game=AppBootstrap.Instance?.GameClient;
            // 按当前连接再核对一次，防止点击发生在 Update 观察到换号或断线之前。
            if(!_available||Window==null||Client==null||State==null||game==null||!game.InGame||!game.IsGateReady
                ||game.PlayerId==0||!ReferenceEquals(_game,game)||_player!=game.PlayerId
                ||!ReferenceEquals(_connection,game.GateConnectionIdentity)||State.PlayerId!=game.PlayerId
                ||(BattleUiRoot.Instance?.IsBattleLayerVisible??false))
            {State?.Notify("请进入可用角色会话后分享装备。");return false;}
            if(State.IsPreview||!State.CanSend(SocialChannel.World)||Client.RequiresReconnect)
            {State.Notify(State.RequiresReconnect?SocialClient.RecoveryMessage:"聊天正在同步，请稍后再试。");return false;}
            if(!EquipShareText.TryCreateDraft(item,State.Draft(SocialChannel.World),out var draft))
            {State.Notify(string.IsNullOrEmpty(State.Draft(SocialChannel.World))?"这件物品暂无可分享的装备信息。":"世界频道已有草稿，请先处理原草稿后再分享装备。");return false;}

            State.SetDraft(SocialChannel.World,draft);
            State.SelectChannel(SocialChannel.World);
            // 此入口不刷新历史、不发消息；玩家可先编辑，再主动点击发送。
            OpenPanel(SocialPage.World,false);
            State.Notify("装备文字已填入世界频道草稿，尚未发送；可编辑后点击发送。");
            Window.FocusComposer();
            return true;
        }
        private void OpenPanel(SocialPage page,bool refreshHistory=true)
        {
            MysteryMerchantUiRoot.Instance?.HidePanel();
            FriendRequests.Hide();
            Mail.MailUiRoot.Instance?.HidePanel();
            Team.TeamUiRoot.Instance?.HidePanel();
            Guild.GuildUiRoot.Instance?.HidePanel();
            MmorpgClient.UI.Ugui.Rank.RankUiRoot.Instance?.HidePanel();
            Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            Attribute.AttributeUiRoot.Instance?.HidePanel();
            Skills.SkillUiRoot.Instance?.HidePanel();
            Pet.PetUiRoot.Instance?.HidePanel();
            Window.Show(page,refreshHistory);Opened?.Invoke();
        }
        private void OpenFriendRequests()
        {
            if(!_available||Friends==null)return;
            MysteryMerchantUiRoot.Instance?.HidePanel();
            Window.Hide();FriendRequests.Show();Opened?.Invoke();
        }
        public void HidePanel(){Window?.Hide();FriendRequests?.Hide();}
        public static bool IsTyping()
        {var go=EventSystem.current?.currentSelectedGameObject;return go!=null&&(go.GetComponentInParent<TMP_InputField>()?.isFocused==true||go.GetComponentInParent<InputField>()?.isFocused==true);}
        private void OnDisable()=>HidePanel();
        private void OnDestroy(){_friendSync?.Dispose();_friendSync=null;Client?.Dispose();Friends?.Dispose();FriendRequests?.Dispose();Window?.Dispose();if(Instance==this)Instance=null;}
    }
}
