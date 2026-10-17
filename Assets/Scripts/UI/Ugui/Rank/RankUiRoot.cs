using System;
using System.Text;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Rank;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.Tianyong;
using Rankpb;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Rank
{
    /// <summary>正式通用榜入口：分页、本人及附近名次，换角/连接后清空旧视图。</summary>
    public sealed class RankUiRoot:MonoBehaviour
    {
        public static RankUiRoot Instance{get;private set;}
        private GameClient _game;
        private RankClient _client;
        private object _connection;
        private ulong _player;
        private RectTransform _hud,_window;
        private TMP_Text _rows,_status,_self,_heading;
        private Button _previous,_next,_refresh,_around;
        private bool _available;
        private readonly RankBoardKey _latest=new(){BoardType=1,Scope=RankScope.Global};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Spawn(){if(Instance!=null)return;var go=new GameObject("[RankUi]");DontDestroyOnLoad(go);go.AddComponent<RankUiRoot>();}
        private void Awake()
        {
            if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;
            var canvasObject=new GameObject("RankCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvasObject.transform.SetParent(transform,false);var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=190;
            var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(2560,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var design=QdaoUguiFactory.CreateCenteredRect("DesignRoot",canvasObject.transform,2560,1080);_hud=QdaoUguiFactory.CreateStretch("RankHud",design,Vector4.zero);QdaoUguiFactory.ConfigureHudCanvas(_hud);
            GameplayUiArt.Button(_hud,"排行",68,877,BattleUiStyle.HudEntryWidth,BattleUiStyle.HudEntryHeight,Toggle,true,fontSize:32);
            _window=QdaoUguiFactory.CreateCenteredRect("RankWindow",design,1200,900);var background=_window.gameObject.AddComponent<Image>();background.color=new Color(.96f,.94f,.83f,.99f);_window.gameObject.AddComponent<GameplayInputBlocker>();
            _heading=GameplayUiArt.Text(_window,"全服排行榜",50,25,950,60,36);GameplayUiArt.Button(_window,"关闭",1020,25,130,55,HidePanel);
            _rows=GameplayUiArt.Text(_window,"",65,115,1070,585,28,wrap:true);_self=GameplayUiArt.Text(_window,"",65,715,1060,50,26);_status=GameplayUiArt.Text(_window,"",65,775,1060,45,24);
            _previous=GameplayUiArt.Button(_window,"上一页",65,830,175,55,()=>Load((_client?.Page?.Page??1)-1));_next=GameplayUiArt.Button(_window,"下一页",265,830,175,55,()=>Load((_client?.Page?.Page??1)+1));
            _refresh=GameplayUiArt.Button(_window,"最新榜",560,830,180,55,()=>{_client?.ReadPage(_latest,1,10);});_around=GameplayUiArt.Button(_window,"我的附近",800,830,230,55,()=>_client?.ReadAround(_client.Page?.Board??_latest,5));HidePanel();_hud.gameObject.SetActive(false);
        }
        private void Update()
        {
            var game=AppBootstrap.Instance?.GameClient;bool ready=game!=null&&game.InGame&&game.IsGateReady;ulong player=ready?game.PlayerId:0;object connection=game?.GateConnectionIdentity;
            if(_game!=game||_player!=player||!ReferenceEquals(connection,_connection)){Reset();_game=game;_player=player;_connection=connection;if(player!=0){_client=new RankClient(new GameClientBattleTransport(game),()=>game.GateConnectionIdentity);_client.Changed+=Render;}}
            _available=ready&&!(BattleUiRoot.Instance?.IsBattleLayerVisible??false);_hud.gameObject.SetActive(_available);if(!_available)HidePanel();
        }
        private void Load(uint page){if(page==0)page=1;_client?.ReadPage(_client.Page?.Board??_latest,page,10);}
        public void Toggle(){if(_window.gameObject.activeSelf){HidePanel();return;}if(!_available)return;Mail.MailUiRoot.Instance?.HidePanel();Guild.GuildUiRoot.Instance?.HidePanel();Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();Social.SocialUiRoot.Instance?.HidePanel();Team.TeamUiRoot.Instance?.HidePanel();GameplayUiRoot.Instance?.HidePanel();_window.gameObject.SetActive(true);Render();_client?.ReadPage(_latest,1,10);}
        public void HidePanel(){if(_window!=null)_window.gameObject.SetActive(false);}
        private void Render()
        {
            if(_client==null)return;var page=_client.Page;var around=_client.Around;var entries=around?.Entries??page?.Entries;var own=around?.Self??page?.Self;var text=new StringBuilder();if(entries!=null)foreach(var entry in entries)text.Append(entry.Rank).Append("    ").Append(string.IsNullOrEmpty(entry.Name)?"道友 "+entry.EntityId:entry.Name).Append("    ").Append(entry.Score).Append('\n');_rows.text=text.Length==0?"本期尚无上榜记录。":text.ToString();_self.text=own==null||own.Rank==0?"本人：尚未上榜":"本人：第 "+own.Rank+" 名    "+own.Score+" 分";_status.text=_client.Status;_heading.text=page==null?"全服排行榜":"全服排行榜  "+page.Page+" / "+page.PageCount+(page.State==RankGenerationState.RankGenerationSealed?" · 已结算":"");bool enabled=!_client.Busy&&!_client.RequiresReconnect;_previous.interactable=enabled&&page!=null&&page.Page>1;_next.interactable=enabled&&page!=null&&page.Page<page.PageCount;_refresh.interactable=enabled;_around.interactable=enabled;
        }
        private void Reset(){if(_client!=null){_client.Changed-=Render;_client.Dispose();_client=null;}HidePanel();if(_rows!=null)_rows.text="";if(_self!=null)_self.text="";if(_status!=null)_status.text="";}
        private void OnDisable()=>HidePanel();
        private void OnDestroy(){Reset();if(Instance==this)Instance=null;}
    }
}
