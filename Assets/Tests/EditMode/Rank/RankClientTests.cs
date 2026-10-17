using System;
using Google.Protobuf;
using Rankpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Rank;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Rank
{
    public sealed class RankClientTests
    {
        private sealed class Transport:IBattleTransport
        {
            public ulong PlayerId{get;set;}=7;public bool IsReady{get;set;}=true;public event Action Disconnected;
            public int Calls;public IMessage Last;public Action<IMessage> Reply;public Action<string> Error;
            public void RegisterNotify(uint id,Action<MessageContent> handler){}public void SendOneWay(uint id,IMessage request){}
            public void Call<T>(uint id,IMessage request,MessageParser<T> parser,Action<T> response,Action<string> error)where T:IMessage<T>{Calls++;Last=request;Reply=value=>response((T)value);Error=error;}
            public void Disconnect(){IsReady=false;Disconnected?.Invoke();}
        }
        private static RankBoardKey Board()=>new(){BoardType=1,Scope=RankScope.Global,Period="season",Generation=7};
        [Test]public void PageAndSelfAreAuthoritativeAndSnapshotsAreDetached()
        {
            var net=new Transport();object connection=new();using var client=new RankClient(net,()=>connection);var board=Board();client.ReadPage(board,2,10);board.Generation=99;Assert.That(((GetRankPageRequest)net.Last).Board.Generation,Is.EqualTo(7));
            net.Reply(new GetRankPageResponse{Board=Board(),Page=2,PageCount=3,TotalCount=21,Self=new RankEntry{EntityId=7,Rank=21,Score=80},Entries={new RankEntry{EntityId=8,Rank=11,Score=100}}});Assert.That(client.Page.Self.Rank,Is.EqualTo(21));var outside=client.Page;outside.Entries[0].Score=999;Assert.That(client.Page.Entries[0].Score,Is.EqualTo(100));Assert.That(client.Busy,Is.False);
        }
        [Test]public void BusinessRejectionAllowsRetryButUnknownTransportNeedsReconnect()
        {
            var net=new Transport();object connection=new();using var client=new RankClient(net,()=>connection);client.ReadPage(Board());net.Reply(new GetRankPageResponse{ErrorMessage=new TipInfoMessage{Id=(uint)rank_error.KRankBoardNotFound}});client.ReadPage(Board());Assert.That(net.Calls,Is.EqualTo(2));net.Error("timeout");client.ReadPage(Board());Assert.That(net.Calls,Is.EqualTo(2));Assert.That(client.RequiresReconnect,Is.True);
        }
        [Test]public void SwitchedConnectionCannotAcceptPreviousSnapshot()
        {
            var net=new Transport();object connection=new();using var client=new RankClient(net,()=>connection);client.ReadPage(Board());connection=new object();net.Reply(new GetRankPageResponse{Board=Board(),Entries={new RankEntry{EntityId=8,Rank=1}}});Assert.That(client.Page,Is.Null);
        }
    }
}
