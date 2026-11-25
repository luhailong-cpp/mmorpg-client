using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Season;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class SeasonClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 9201;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;

            public readonly List<(uint msgId, IMessage req)> Calls = new();
            private readonly Queue<Action> _pending = new();
            public Func<uint, IMessage, (IMessage resp, string err)> Responder;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
            public void SendOneWay(uint messageId, IMessage request) { }

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add((messageId, request));
                var (resp, err) = Responder(messageId, request);
                if (err != null) _pending.Enqueue(() => onError(err));
                else _pending.Enqueue(() => onResponse((TResp)resp));
            }

            public void Flush()
            {
                while (_pending.Count > 0) _pending.Dequeue().Invoke();
            }

            public void TriggerDisconnect()
            {
                IsReady = false;
                Disconnected?.Invoke();
            }
        }

        [Test]
        public void Client_EnforcesMonotonicRevision_And_EquipsSeal()
        {
            var net = new FakeTransport();
            object conn = new object();
            ulong rev = 4;
            uint equipped = 1;
            net.Responder = (msgId, req) =>
            {
                if (msgId == SceneSeasonClientPlayerRpc.GetSeasonStateMessageId)
                {
                    var snap = new SeasonStateSnapshot
                    {
                        Available = true,
                        SeasonId = 1,
                        SeasonName = "首测赛季",
                        EquippedSealId = equipped,
                        ProgressPoints = 40,
                        MaxPoints = 100,
                        Revision = rev,
                        CanSwitchSeal = true,
                        CanCatchup = true,
                        CatchupCurrencyType = 0,
                        CatchupPricePerPoint = 10,
                    };
                    snap.Seals.Add(new SeasonSealOption { SealId = 1, Name = "破军印", Equipped = equipped == 1 });
                    snap.Seals.Add(new SeasonSealOption { SealId = 2, Name = "玄甲印", Equipped = equipped == 2 });
                    return (new GetSeasonStateResponse { State = snap }, null);
                }
                if (msgId == SceneSeasonClientPlayerRpc.EquipSeasonSealMessageId)
                {
                    var equipReq = (EquipSeasonSealRequest)req;
                    equipped = equipReq.SealId;
                    rev = 5;
                    var snap = new SeasonStateSnapshot
                    {
                        Available = true,
                        SeasonId = equipReq.SeasonId,
                        EquippedSealId = equipped,
                        ProgressPoints = 40,
                        MaxPoints = 100,
                        Revision = rev,
                        CanSwitchSeal = true,
                    };
                    snap.Seals.Add(new SeasonSealOption { SealId = 1, Name = "破军印", Equipped = equipped == 1 });
                    snap.Seals.Add(new SeasonSealOption { SealId = 2, Name = "玄甲印", Equipped = equipped == 2 });
                    return (new EquipSeasonSealResponse { State = snap }, null);
                }
                return (null, "unexpected");
            };

            float now = 10f;
            using var client = new SeasonClient(net, () => conn, () => now);
            client.RequestState(force: true);
            net.Flush();
            Assert.That(client.Snapshot?.EquippedSealId, Is.EqualTo(1u));
            Assert.That(SeasonModels.FindEquippedSeal(client.Snapshot)?.Name, Is.EqualTo("破军印"));

            // 乱序旧 revision 不回退
            rev = 2;
            now = 20f;
            client.RequestState(force: true);
            net.Flush();
            Assert.That(client.Snapshot?.Revision, Is.EqualTo(4ul));

            // 切换赛季印
            now = 25f;
            Assert.That(client.EquipSeal(2u), Is.True);
            net.Flush();
            Assert.That(client.Snapshot?.EquippedSealId, Is.EqualTo(2u));
            Assert.That(SeasonModels.FindEquippedSeal(client.Snapshot)?.Name, Is.EqualTo("玄甲印"));
        }

        [Test]
        public void CatchupSeal_ResendsOnceWithSameKey_And_EmitsEvent()
        {
            var net = new FakeTransport();
            object conn = new object();
            int catchupAttempts = 0;
            net.Responder = (msgId, req) =>
            {
                if (msgId == SceneSeasonClientPlayerRpc.GetSeasonStateMessageId)
                {
                    return (new GetSeasonStateResponse
                    {
                        State = new SeasonStateSnapshot
                        {
                            Available = true,
                            SeasonId = 1,
                            ProgressPoints = 60,
                            MaxPoints = 100,
                            CanCatchup = true,
                            CatchupCurrencyType = 0,
                            CatchupPricePerPoint = 50,
                            CatchupRemainingTotalPrice = 2000,
                            Revision = 1,
                        },
                    }, null);
                }
                if (msgId == SceneSeasonClientPlayerRpc.CatchupSeasonSealMessageId)
                {
                    catchupAttempts++;
                    if (catchupAttempts == 1)
                        return (null, OutstandingCalls.TimeoutDetail);
                    return (new CatchupSeasonSealResponse
                    {
                        AddedPoints = 20,
                        SpentCurrency = 1000,
                        State = new SeasonStateSnapshot
                        {
                            Available = true,
                            SeasonId = 1,
                            ProgressPoints = 80,
                            MaxPoints = 100,
                            CanCatchup = true,
                            CatchupCurrencyType = 0,
                            CatchupPricePerPoint = 50,
                            Revision = 2,
                        },
                    }, null);
                }
                return (null, "unexpected");
            };

            float now = 10f;
            using var client = new SeasonClient(net, () => conn, () => now);
            SeasonSealCatchupEvent? received = null;
            client.SealCatchupCompleted += ev => received = ev;

            client.RequestState(force: true);
            net.Flush();

            now = 15f;
            Assert.That(client.CatchupSeal(20u), Is.True);
            net.Flush();

            Assert.That(catchupAttempts, Is.EqualTo(2));
            var firstReq = (CatchupSeasonSealRequest)net.Calls[1].req;
            var secondReq = (CatchupSeasonSealRequest)net.Calls[2].req;
            Assert.That(firstReq.ClientRequestId, Is.Not.EqualTo(0ul));
            Assert.That(secondReq.ClientRequestId, Is.EqualTo(firstReq.ClientRequestId));
            Assert.That(firstReq.ExpectedTotalPrice, Is.EqualTo(1000ul));
            Assert.That(received.HasValue, Is.True);
            Assert.That(received.Value.AddedPoints, Is.EqualTo(20u));
            Assert.That(client.Snapshot?.ProgressPoints, Is.EqualTo(80u));
            Assert.That(SeasonModels.FormatBpsPercent(600u), Is.EqualTo("6%"));
            Assert.That(SeasonModels.FormatBpsPercent(450u), Is.EqualTo("4.5%"));
            Assert.That(SeasonModels.DescribeTip((uint)season_error.KSeasonInBattle), Does.Contain("战斗中"));
        }
    }
}
