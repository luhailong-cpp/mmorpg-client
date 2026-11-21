using System;
using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    public sealed class RpcGuardTests
    {
        private sealed class FakeChannel : IRpcChannel
        {
            public readonly List<uint> SentIds = new();
            public Action<RpcStatus> Pending;

            public void CallMethod<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser, RpcDone<TResp> done)
                where TResp : IMessage<TResp>
            {
                SentIds.Add(messageId);
                Pending = status => done(status, status.IsOk ? parser.ParseFrom(Array.Empty<byte>()) : default);
            }

            public void SendOneWay(uint messageId, IMessage request) => SentIds.Add(messageId);
            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
        }

        [Test]
        public void GuardedChannel_EnforcesPerMessageRateLimit_AndExemptsSessionControlMessages()
        {
            float now = 0f;
            var inner = new FakeChannel();
            var guard = new RpcGuard(() => now);
            var channel = new GuardedRpcChannel(inner, guard);

            // 默认写消息 3 次 / 2.5s：第 4 次在本地直接返回 1008，不发往底层通道。
            for (int i = 0; i < 3; i++)
            {
                RpcStatus got = default;
                channel.CallMethod(MessageIds.MailDeleteMails, new Empty(), Empty.Parser, (s, _) => got = s);
                inner.Pending(RpcStatus.Ok);
                Assert.That(got.IsOk, Is.True);
                now += 0.2f;
            }
            Assert.That(guard.CanSend(MessageIds.MailDeleteMails), Is.False);
            Assert.That(guard.IsRateLimited(MessageIds.MailDeleteMails), Is.True);

            RpcStatus rejected = default;
            channel.CallMethod(MessageIds.MailDeleteMails, new Empty(), Empty.Parser, (s, _) => rejected = s);
            Assert.That(rejected.Code, Is.EqualTo(RpcStatusCode.ServerTip));
            Assert.That(rejected.TipId, Is.EqualTo(RpcGuard.RateLimitTipId));
            Assert.That(inner.SentIds.Count, Is.EqualTo(3), "超限请求不得发到 gate");

            // 其它消息号配额独立，不受影响。
            Assert.That(guard.CanSend(MessageIds.MailListMails), Is.True);

            // 会话控制号豁免（如 LeaveGame / Login / EnterGame）。
            for (int i = 0; i < 6; i++)
            {
                RpcStatus got = default;
                channel.CallMethod(MessageIds.LeaveGame, new Empty(), Empty.Parser, (s, _) => got = s);
                inner.Pending(RpcStatus.Ok);
                Assert.That(got.IsOk, Is.True);
            }
            Assert.That(inner.SentIds.FindAll(id => id == MessageIds.LeaveGame).Count, Is.EqualTo(6));
        }

        [Test]
        public void GuardedChannel_CircuitBreakerIsolatesByService_Ignores1008_AndDoesNotBurnQuotaWhileOpen()
        {
            float now = 0f;
            var inner = new FakeChannel();
            var guard = new RpcGuard(() => now, circuitFailureThreshold: 3, circuitOpenSeconds: 15f);
            var channel = new GuardedRpcChannel(inner, guard);

            // 连续 3 次 1008 限流绝不触发熔断。
            for (int i = 0; i < 3; i++)
            {
                channel.CallMethod(MessageIds.MailListMails, new Empty(), Empty.Parser, (_, _) => { });
                inner.Pending(RpcStatus.ServerTip(RpcGuard.RateLimitTipId));
                now += 3f;
            }
            Assert.That(guard.GetCircuitState(MessageIds.MailListMails), Is.EqualTo(CircuitState.Closed));

            // 邮件服务连续 3 次超时/1003 触发 "mail" 域熔断；同域其它消息也熔断，但组队/匹配域不受影响。
            for (int i = 0; i < 3; i++)
            {
                channel.CallMethod(MessageIds.MailListMails, new Empty(), Empty.Parser, (_, _) => { });
                inner.Pending(i == 0 ? RpcStatus.Timeout("rpc timeout") : RpcStatus.ServerTip((uint)common_error.KServiceUnavailable));
                now += 3f;
            }
            Assert.That(guard.GetCircuitState(MessageIds.MailListMails), Is.EqualTo(CircuitState.Open));
            Assert.That(guard.IsCircuitOpen(MessageIds.MailDeleteMails), Is.True, "同属 mail 服务域共享熔断");
            Assert.That(guard.CanSend(MessageIds.StartTeamMatch), Is.True, "team 域不受 mail 域熔断影响");
            Assert.That(guard.CanSend(MessageIds.JoinQueue), Is.True, "match 域不受 mail 域熔断影响");

            // 熔断期快速返回 NotSent("circuit open")，且不消耗限流配额。
            int sentBefore = inner.SentIds.Count;
            for (int i = 0; i < 5; i++)
            {
                RpcStatus s = default;
                channel.CallMethod(MessageIds.MailDeleteMails, new Empty(), Empty.Parser, (st, _) => s = st);
                Assert.That(s.Code, Is.EqualTo(RpcStatusCode.NotSent));
                Assert.That(s.Detail, Is.EqualTo(RpcGuard.CircuitOpenError));
            }
            Assert.That(inner.SentIds.Count, Is.EqualTo(sentBefore));
            Assert.That(guard.IsRateLimited(MessageIds.MailDeleteMails), Is.False, "熔断拦截不得误扣限流配额");

            // 15 秒后进入 HalfOpen：只放行 1 个探测请求，并发第 2 个返回 circuit open。
            now += 15f;
            Assert.That(guard.GetCircuitState(MessageIds.MailListMails), Is.EqualTo(CircuitState.HalfOpen));
            channel.CallMethod(MessageIds.MailListMails, new Empty(), Empty.Parser, (_, _) => { });
            Assert.That(inner.SentIds.Count, Is.EqualTo(sentBefore + 1));

            RpcStatus concurrent = default;
            channel.CallMethod(MessageIds.MailDeleteMails, new Empty(), Empty.Parser, (st, _) => concurrent = st);
            Assert.That(concurrent.Code, Is.EqualTo(RpcStatusCode.NotSent));
            Assert.That(concurrent.Detail, Is.EqualTo(RpcGuard.CircuitOpenError));

            // HalfOpen 探测遇 1008：释放槽位，仍保持 HalfOpen，不重开 15 秒。
            inner.Pending(RpcStatus.ServerTip(RpcGuard.RateLimitTipId));
            Assert.That(guard.GetCircuitState(MessageIds.MailListMails), Is.EqualTo(CircuitState.HalfOpen));

            now += 3f;
            channel.CallMethod(MessageIds.MailListMails, new Empty(), Empty.Parser, (_, _) => { });
            inner.Pending(RpcStatus.Ok);
            Assert.That(guard.GetCircuitState(MessageIds.MailListMails), Is.EqualTo(CircuitState.Closed));
        }

        [Test]
        public void SharedGuard_OuterOkFollowedByInnerServiceFault_AccumulatesToOpenCircuit()
        {
            float now = 0f;
            var inner = new FakeChannel();
            var guard = new RpcGuard(() => now, circuitFailureThreshold: 3, circuitOpenSeconds: 15f);
            var channel = new GuardedRpcChannel(inner, guard);

            // 模拟外层 RpcStatus.Ok、但业务回包缺视图（Team == null）连续 3 次：必须累计并打开熔断。
            for (int i = 0; i < 3; i++)
            {
                channel.CallMethod(MessageIds.GetMyTeam, new Empty(), Empty.Parser, (st, _) =>
                {
                    Assert.That(st.IsOk, Is.True);
                    guard.RecordServiceFault(MessageIds.GetMyTeam);
                });
                inner.Pending(RpcStatus.Ok);
                now += 3f;
            }
            Assert.That(guard.GetCircuitState(MessageIds.GetMyTeam), Is.EqualTo(CircuitState.Open));

            // 冷却后 HalfOpen 探测再次出现内层故障：立即重开熔断 15 秒。
            now += 15f;
            channel.CallMethod(MessageIds.GetMyTeam, new Empty(), Empty.Parser, (_, _) => guard.RecordServiceFault(MessageIds.GetMyTeam));
            inner.Pending(RpcStatus.Ok);
            Assert.That(guard.GetCircuitState(MessageIds.GetMyTeam), Is.EqualTo(CircuitState.Open));

            // 换连接 Reset 一处清零全部限流与熔断。
            guard.Reset();
            Assert.That(guard.GetCircuitState(MessageIds.GetMyTeam), Is.EqualTo(CircuitState.Closed));
            Assert.That(guard.CanSend(MessageIds.GetMyTeam), Is.True);
        }

        [Test]
        public void RpcGuard_HotReloadsLimits_AndCoversBattleDomain()
        {
            float now = 0f;
            int configuredMax = 2;
            var guard = new RpcGuard(() => now,
                limitResolver: id => configuredMax == 0 ? ((int, float)?)null : (configuredMax, 1.5f));

            Assert.That(guard.TryBeginCall(MessageIds.SubmitBattleAction, out _), Is.True);
            Assert.That(guard.TryBeginCall(MessageIds.SubmitBattleAction, out _), Is.True);
            Assert.That(guard.TryBeginCall(MessageIds.SubmitBattleAction, out var limited), Is.False);
            Assert.That(limited.TipId, Is.EqualTo(RpcGuard.RateLimitTipId));

            // 模拟配置表热更放宽配额：立即按新参数重建限流器，无需断线重连。
            configuredMax = 5;
            Assert.That(guard.CanSend(MessageIds.SubmitBattleAction), Is.True);
            Assert.That(guard.TryBeginCall(MessageIds.SubmitBattleAction, out _), Is.True);

            // 战斗直连域内部共享 "battle" 熔断桶，但与大厅 "team" 域隔离。
            for (int i = 0; i < 3; i++)
            {
                guard.CompleteCall(MessageIds.SubmitBattleAction, RpcStatus.Timeout("rpc timeout"));
                now += 2f;
            }
            Assert.That(guard.IsCircuitOpen(MessageIds.GetBattleState), Is.True);
            Assert.That(guard.IsCircuitOpen(MessageIds.StartTeamMatch), Is.False);

            // 支付域(396..399)共享独立的 "payment" 熔断桶,与聚宝斋 "jubaozhai" 隔离。
            Assert.That(RpcGuard.DefaultResolveService(ClientPlayerPaymentRpc.ListPayChannelsMessageId), Is.EqualTo("payment"));
            Assert.That(RpcGuard.DefaultResolveService(ClientPlayerPaymentRpc.CreatePayIntentMessageId), Is.EqualTo("payment"));
            Assert.That(RpcGuard.DefaultResolveService(ClientPlayerPaymentRpc.GetPayIntentMessageId), Is.EqualTo("payment"));
            Assert.That(RpcGuard.DefaultResolveService(ClientPlayerPaymentRpc.SubmitPurchaseMessageId), Is.EqualTo("payment"));
            for (int i = 0; i < 3; i++)
            {
                guard.CompleteCall(ClientPlayerPaymentRpc.CreatePayIntentMessageId, RpcStatus.Timeout("rpc timeout"));
                now += 2f;
            }
            Assert.That(guard.IsCircuitOpen(ClientPlayerPaymentRpc.GetPayIntentMessageId), Is.True);
            Assert.That(guard.IsCircuitOpen(MessageIds.JubaozhaiListOfficialSkus), Is.False);
        }
    }
}
