using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 在途调用表的契约(服务端仓 docs/design/client-rpc-callback.md §2):只按 id 认领、恰好一次、
    /// 超时与断线让调用失败、迟到回包不认领、回调重入安全、回调异常不打断同批其余调用。
    /// </summary>
    public sealed class OutstandingCallsTests
    {
        private readonly List<Exception> _callbackErrors = new();
        private OutstandingCalls _calls;

        [SetUp]
        public void SetUp()
        {
            _callbackErrors.Clear();
            _calls = new OutstandingCalls(_callbackErrors.Add);
        }

        private static MessageContent Reply(ulong id, uint messageId, IMessage body = null, uint tip = 0)
        {
            var mc = new MessageContent { Id = id, MessageId = messageId };
            if (body != null) mc.SerializedMessage = body.ToByteString();
            if (tip != 0) mc.ErrorMessage = new TipInfoMessage { Id = tip };
            return mc;
        }

        [Test]
        public void Ids_StartAtOne_AreUnique()
        {
            var a = _calls.Add(10, (_, _) => { });
            var b = _calls.Add(10, (_, _) => { });
            Assert.AreEqual(1UL, a);
            Assert.AreEqual(2UL, b);
            Assert.AreEqual(2, _calls.Count);
        }

        [Test]
        public void Reply_MatchesOnlyByIdAndCompletesExactlyOnce()
        {
            var results = new List<(ulong call, RpcStatus status)>();
            var first = _calls.Add(10, (s, _) => results.Add((1, s)));
            var second = _calls.Add(10, (s, _) => results.Add((2, s)));

            // 同一 message_id,回包顺序与发出顺序相反:按 id 认领,不按 message_id 先进先出。
            Assert.IsTrue(_calls.TryComplete(Reply(second, 7)));
            Assert.IsTrue(_calls.TryComplete(Reply(first, 7)));
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(2UL, results[0].call);
            Assert.AreEqual(1UL, results[1].call);
            Assert.IsTrue(results[0].status.IsOk);

            // 同一 id 的第二个回包是迟到回包。
            Assert.IsFalse(_calls.TryComplete(Reply(first, 7)));
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(1UL, _calls.LateReplies);
        }

        [Test]
        public void ZeroId_IsNeverClaimed_AndNotCountedLate()
        {
            _calls.Add(10, (_, _) => Assert.Fail("push must not complete a call"));
            Assert.IsFalse(_calls.TryComplete(Reply(0, 7)));
            Assert.AreEqual(0UL, _calls.LateReplies);
            Assert.AreEqual(1, _calls.Count);
        }

        [Test]
        public void EnvelopeError_CompletesWithServerTip()
        {
            RpcStatus got = default;
            var id = _calls.Add(10, (s, _) => got = s);
            Assert.IsTrue(_calls.TryComplete(Reply(id, 7, tip: 1003)));
            Assert.AreEqual(RpcStatusCode.ServerTip, got.Code);
            Assert.AreEqual(1003u, got.TipId);
            Assert.AreEqual("server tip=1003", got.ToLegacyError());
        }

        [Test]
        public void Timeout_FiresOnlyAfterDeadline_ThenReplyIsLate()
        {
            RpcStatus got = default;
            int calls = 0;
            var id = _calls.Add(5.0, (s, _) => { got = s; calls++; });

            _calls.ExpireDue(4.9);
            Assert.AreEqual(0, calls);
            _calls.ExpireDue(5.0);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(RpcStatusCode.Timeout, got.Code);
            Assert.IsTrue(got.OutcomeUnknown);
            Assert.AreEqual("rpc timeout", got.ToLegacyError());
            Assert.AreEqual("link: rpc timeout", got.ToLegacyError("link: "));

            Assert.IsFalse(_calls.TryComplete(Reply(id, 7)));
            Assert.AreEqual(1, calls);
            Assert.AreEqual(1UL, _calls.LateReplies);
        }

        [Test]
        public void FailAll_CompletesEveryCallOnce_AndAllowsReentrantAdd()
        {
            var statuses = new List<RpcStatus>();
            ulong retried = 0;
            _calls.Add(10, (s, _) =>
            {
                statuses.Add(s);
                // 回调里重发:新调用登记进同一张表,不被这一轮 FailAll 吞掉。
                retried = _calls.Add(20, (_, _) => { });
            });
            _calls.Add(10, (s, _) => statuses.Add(s));

            _calls.FailAll(RpcStatus.Disconnected("disconnected"));
            Assert.AreEqual(2, statuses.Count);
            Assert.IsTrue(statuses.TrueForAll(s => s.Code == RpcStatusCode.Disconnected));
            Assert.AreEqual("disconnected", statuses[0].ToLegacyError());
            Assert.AreNotEqual(0UL, retried);
            Assert.IsTrue(_calls.IsOutstanding(retried));
            Assert.AreEqual(1, _calls.Count);
        }

        [Test]
        public void CallbackException_IsReported_AndDoesNotSkipOthers()
        {
            int completed = 0;
            _calls.Add(10, (_, _) => throw new InvalidOperationException("boom"));
            _calls.Add(10, (_, _) => completed++);
            _calls.FailAll(RpcStatus.Disconnected("closed"));
            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, _callbackErrors.Count);
            Assert.AreEqual(0, _calls.Count);
        }

        [Test]
        public void Remove_WithdrawsWithoutCallback()
        {
            var id = _calls.Add(10, (_, _) => Assert.Fail("removed call must not complete"));
            Assert.IsTrue(_calls.Remove(id));
            Assert.IsFalse(_calls.Remove(id));
            _calls.FailAll(RpcStatus.Disconnected("disconnected"));
            Assert.AreEqual(0, _calls.Count);
        }

        [Test]
        public void Typed_ParsesOkBody_AndDowngradesBadBodyToParseError()
        {
            TipInfoMessage parsed = null;
            RpcStatus status = default;
            var typed = OutstandingCalls.Typed(TipInfoMessage.Parser, (s, r) => { status = s; parsed = r; });

            typed(RpcStatus.Ok, Reply(1, 7, new TipInfoMessage { Id = 9 }));
            Assert.IsTrue(status.IsOk);
            Assert.AreEqual(9u, parsed.Id);

            typed(RpcStatus.Ok, new MessageContent { Id = 2, SerializedMessage = ByteString.CopyFrom(0xFF, 0xFF, 0xFF) });
            Assert.AreEqual(RpcStatusCode.ParseError, status.Code);
            Assert.IsNull(parsed);
            StringAssert.StartsWith("parse response: ", status.ToLegacyError());

            typed(RpcStatus.Timeout("rpc timeout"), null);
            Assert.AreEqual(RpcStatusCode.Timeout, status.Code);
            Assert.IsNull(parsed);
        }
    }
}
