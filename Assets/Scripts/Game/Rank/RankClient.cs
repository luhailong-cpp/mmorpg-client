using System;
using Google.Protobuf;
using Rankpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Rank
{
    /// <summary>通用榜真实读取；分数和名次仅来自服务器，分页固定已返回的代次。</summary>
    public sealed class RankClient:IDisposable
    {
        private readonly IBattleTransport _net;
        private readonly Func<object> _identity;
        private readonly object _connection;
        private readonly ulong _player;
        private int _generation;
        private bool _disposed;
        private GetRankPageResponse _page;
        private GetRankAroundResponse _around;
        public bool Busy{get;private set;}
        public bool RequiresReconnect{get;private set;}
        public string Status{get;private set;}="";
        public GetRankPageResponse Page=>_page?.Clone();
        public GetRankAroundResponse Around=>_around?.Clone();
        public event Action Changed;
        public RankClient(IBattleTransport net,Func<object> identity){_net=net??throw new ArgumentNullException(nameof(net));_identity=identity??throw new ArgumentNullException(nameof(identity));_connection=identity();_player=net.PlayerId;_net.Disconnected+=Disconnected;}
        public void ReadPage(RankBoardKey board,uint page=1,uint size=20)
        {
            if(board==null||board.BoardType==0||board.Scope!=RankScope.Global){Status="排行榜尚未配置。";Changed?.Invoke();return;}
            Call(MessageIds.RankGetRankPage,new GetRankPageRequest{Board=board.Clone(),Page=page,PageSize=Math.Min(size==0?20:size,50)},GetRankPageResponse.Parser,response=>
            {Status=DescribeTip(response.ErrorMessage);if(Status.Length==0){_page=response.Clone();_around=null;}});
        }
        public void ReadAround(RankBoardKey board,uint radius=10)
        {
            if(board==null||board.BoardType==0||board.Scope!=RankScope.Global)return;
            Call(MessageIds.RankGetRankAround,new GetRankAroundRequest{Board=board.Clone(),Radius=Math.Min(radius,25)},GetRankAroundResponse.Parser,response=>{Status=DescribeTip(response.ErrorMessage);if(Status.Length==0)_around=response.Clone();});
        }
        private bool Current(int generation)=>!_disposed&&generation==_generation&&_net.IsReady&&_player!=0&&_net.PlayerId==_player&&ReferenceEquals(_connection,_identity());
        private void Call<T>(uint id,IMessage request,MessageParser<T> parser,Action<T> receive)where T:IMessage<T>
        {
            if(_disposed||Busy||RequiresReconnect)return;if(!_net.IsReady||_player==0||_net.PlayerId!=_player||!ReferenceEquals(_connection,_identity()))return;
            int generation=++_generation;Busy=true;Status="正在读取排行榜…";Changed?.Invoke();_net.Call(id,request,parser,response=>{if(!Current(generation))return;Busy=false;receive(response);Changed?.Invoke();},_=>{if(!Current(generation))return;Busy=false;RequiresReconnect=true;Status="排行榜连接尚未确认，请重连后再试。";Changed?.Invoke();});
        }
        public static string DescribeTip(TipInfoMessage tip)=>tip==null||tip.Id==0?"":tip.Id switch{(uint)rank_error.KRankBoardNotFound=>"本期排行榜尚未开放。",(uint)rank_error.KRankGenerationClosed=>"本期排行榜已结算，请刷新查看。",(uint)rank_error.KRankRequestConflict=>"排行榜代次已变化，请刷新。",(uint)rank_error.KRankSettlementPending=>"排行榜正在结算，请稍后再试。",_=>"排行榜暂时不可用，请稍后重试。"};
        private void Disconnected(){if(_disposed)return;++_generation;Busy=false;_page=null;_around=null;Status="请重新进入角色后查看排行榜。";Changed?.Invoke();}
        public void Dispose(){if(_disposed)return;_disposed=true;++_generation;_net.Disconnected-=Disconnected;}
    }
}
