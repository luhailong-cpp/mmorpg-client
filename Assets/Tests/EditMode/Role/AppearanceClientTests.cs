using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class AppearanceClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 9001;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;

            public readonly List<(uint msgId, IMessage req)> Calls = new();
            private readonly Dictionary<uint, Action<MessageContent>> _notifies = new();
            private readonly Queue<Action> _pending = new();
            public Func<uint, IMessage, (IMessage resp, string err)> Responder;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) => _notifies[messageId] = handler;

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add((messageId, request));
                var (resp, err) = Responder(messageId, request);
                if (err != null) _pending.Enqueue(() => onError(err));
                else _pending.Enqueue(() => onResponse((TResp)resp));
            }

            public void SendOneWay(uint messageId, IMessage request) { }

            public void Flush()
            {
                while (_pending.Count > 0) _pending.Dequeue().Invoke();
            }

            public void EmitNotify(uint messageId, IMessage payload)
            {
                if (_notifies.TryGetValue(messageId, out var handler))
                    handler(new MessageContent { MessageId = messageId, SerializedMessage = payload.ToByteString() });
            }

            public void TriggerDisconnect()
            {
                IsReady = false;
                Disconnected?.Invoke();
            }
        }

        private static readonly AppearanceMessageIds Ids = new AppearanceMessageIds(442, 443, 444, 445, 447);

        [Test]
        public void Models_BitStoragePriceCountdownAndTipsMatchServerContract()
        {
            // bit 0 (byte 0 bit 0) 与 bit 10 (byte 1 bit 2)
            byte[] bits = { 0x01, 0x04 };
            Assert.That(AppearanceModels.IsBitSet(bits, 0), Is.True);
            Assert.That(AppearanceModels.IsBitSet(bits, 1), Is.False);
            Assert.That(AppearanceModels.IsBitSet(bits, 10), Is.True);
            Assert.That(AppearanceModels.IsBitSet(bits, 11), Is.False);
            Assert.That(AppearanceModels.IsBitSet(bits, 99), Is.False);
            Assert.That(AppearanceModels.IsBitSet(null, 0), Is.False);

            Assert.That(AppearanceModels.FormatPriceYuan(1000), Is.EqualTo("10元"));
            Assert.That(AppearanceModels.FormatPriceYuan(1050), Is.EqualTo("10.50元"));
            Assert.That(AppearanceModels.FormatTrialCountdown(3600), Is.EqualTo("1小时00分00秒"));
            Assert.That(AppearanceModels.FormatTrialCountdown(3572), Is.EqualTo("59分32秒"));
            Assert.That(AppearanceModels.FormatTrialCountdown(0), Is.EqualTo("已到期"));

            Assert.That(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceNoCard), Does.Contain("10元"));
            Assert.That(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceTrialAlreadyUsed), Does.Contain("限试用 1 次"));
            Assert.That(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceInvalid), Does.Contain("不存在"));
        }

        [Test]
        public void Client_SwitchRetriesOnceWithSameIdempotencyKeyOnTimeout()
        {
            var net = new FakeTransport();
            float now = 10f;
            var conn = new object();
            int switchAttempts = 0;
            net.Responder = (msgId, req) =>
            {
                if (msgId == Ids.Switch)
                {
                    switchAttempts++;
                    if (switchAttempts == 1) return (null, "timeout");
                    return (new SwitchAppearanceResponse
                    {
                        ErrorMessage = new TipInfoMessage { Id = 0 },
                        Replayed = true,
                        Info = new PlayerAppearanceInfo
                        {
                            PermanentAppearanceId = "01_ice_sword_girl",
                            ActiveAppearanceId = "01_ice_sword_girl",
                            OwnedCardCount = 0,
                            ServerNow = 1000,
                        }
                    }, null);
                }
                return (new GetAppearanceInfoResponse(), null);
            };

            using var client = new AppearanceClient(net, () => conn, () => now, Ids);
            Assert.That(client.SwitchAppearance("01_ice_sword_girl"), Is.True);
            net.Flush();

            Assert.That(switchAttempts, Is.EqualTo(2));
            var firstReq = (SwitchAppearanceRequest)net.Calls[0].req;
            var secondReq = (SwitchAppearanceRequest)net.Calls[1].req;
            Assert.That(firstReq.ClientRequestId, Is.Not.EqualTo(0ul));
            Assert.That(secondReq.ClientRequestId, Is.EqualTo(firstReq.ClientRequestId), "超时重发必须携带同一幂等键");
            Assert.That(client.Info.PermanentAppearanceId, Is.EqualTo("01_ice_sword_girl"));
            Assert.That(client.RequiresReconnect, Is.False);
        }

        [Test]
        public void Client_TrialCountdownAndExpiredServerPushRaiseNotice()
        {
            var net = new FakeTransport();
            float now = 100f;
            var conn = new object();
            net.Responder = (msgId, req) =>
            {
                if (msgId == Ids.StartTrial)
                {
                    return (new StartAppearanceTrialResponse
                    {
                        ErrorMessage = new TipInfoMessage { Id = 0 },
                        Info = new PlayerAppearanceInfo
                        {
                            PermanentAppearanceId = "00_reference_topright_boy",
                            ActiveAppearanceId = "08_alchemy_prodigy_boy",
                            TrialAppearanceId = "08_alchemy_prodigy_boy",
                            TrialExpiresAt = 4600,
                            ServerNow = 1000,
                            TrialUsedBits = ByteString.CopyFrom(new byte[] { 0x01 }),
                        }
                    }, null);
                }
                return (new GetAppearanceInfoResponse(), null);
            };

            string notice = null;
            using var client = new AppearanceClient(net, () => conn, () => now, Ids);
            client.Notice += msg => notice = msg;

            Assert.That(client.StartTrial("08_alchemy_prodigy_boy"), Is.True);
            net.Flush();
            Assert.That(client.HasActiveTrial, Is.True);
            Assert.That(client.TrialRemainingSeconds, Is.EqualTo(3600ul));
            now += 120f;
            Assert.That(client.TrialRemainingSeconds, Is.EqualTo(3480ul));

            net.EmitNotify(Ids.NotifyState, new AppearanceStateNotify
            {
                TrialExpired = true,
                Info = new PlayerAppearanceInfo
                {
                    PermanentAppearanceId = "00_reference_topright_boy",
                    ActiveAppearanceId = "00_reference_topright_boy",
                    ServerNow = 4600,
                    TrialUsedBits = ByteString.CopyFrom(new byte[] { 0x01 }),
                }
            });
            Assert.That(client.HasActiveTrial, Is.False);
            Assert.That(notice, Is.EqualTo(AppearanceModels.TrialExpiredText));
        }
    }
}
