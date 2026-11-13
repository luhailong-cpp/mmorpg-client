using System.Collections.Generic;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 业务幂等键的生成与「结果不明、可以原样重发」的分类(服务端 docs/design/client-request-idempotency.md §2.1)。
    /// 分类读的是旧字符串契约(RpcStatus.ToLegacyError 的产物),所以这里同时钉住它的反解。
    /// </summary>
    public sealed class ClientRequestKeysTests
    {
        [Test]
        public void TryCreateYieldsNonZeroDistinctKeys()
        {
            var seen = new HashSet<ulong>();
            for (int i = 0; i < 1000; ++i)
            {
                Assert.That(ClientRequestKeys.TryCreate(out ulong key), Is.True);
                Assert.That(key, Is.Not.Zero, "0 表示旧客户端、不去重,不得生成");
                Assert.That(seen.Add(key), Is.True, "随机 64 位键在保留期内不会重复");
            }
        }

        [Test]
        public void ShouldResendOnceOnlyForUnknownOutcomes()
        {
            Assert.That(ClientRequestKeys.ShouldResendOnce(RpcStatus.Timeout(OutstandingCalls.TimeoutDetail).ToLegacyError()), Is.True,
                "超时:结果不明");
            Assert.That(ClientRequestKeys.ShouldResendOnce(RpcStatus.ServerTip((uint)common_error.KServiceUnavailable).ToLegacyError()),
                Is.True, "信封 1003:gate 等下游超时或下游失败,结果不明");
            Assert.That(ClientRequestKeys.ShouldResendOnce(RpcStatus.ServerTip((uint)common_error.KRateLimitExceeded).ToLegacyError()),
                Is.False, "1008 限流:包没到 scene,确定没执行");
            Assert.That(ClientRequestKeys.ShouldResendOnce(RpcStatus.Disconnected("disconnected").ToLegacyError()), Is.False,
                "断线:连接已经换了,第一版不跨连接重发");
            Assert.That(ClientRequestKeys.ShouldResendOnce(RpcStatus.NotSent("not connected").ToLegacyError()), Is.False);
            Assert.That(ClientRequestKeys.ShouldResendOnce(RpcStatus.ParseError("x").ToLegacyError()), Is.False);
            Assert.That(ClientRequestKeys.ShouldResendOnce(null), Is.False);
            Assert.That(ClientRequestKeys.ShouldResendOnce("server tip="), Is.False);
            Assert.That(ClientRequestKeys.ShouldResendOnce("server tip=abc"), Is.False);
        }

        [Test]
        public void LegacyServerTipRoundTrips()
        {
            uint tip = (uint)common_error.KServiceUnavailable;
            Assert.That(RpcStatus.TryParseLegacyServerTip(RpcStatus.ServerTip(tip).ToLegacyError(), out uint parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(tip));
            Assert.That(RpcStatus.TryParseLegacyServerTip(OutstandingCalls.TimeoutDetail, out _), Is.False);
            Assert.That(RpcStatus.TryParseLegacyServerTip("server tip=0", out _), Is.False, "0 不是码");
            Assert.That(RpcStatus.TryParseLegacyServerTip(null, out _), Is.False);
        }
    }
}
