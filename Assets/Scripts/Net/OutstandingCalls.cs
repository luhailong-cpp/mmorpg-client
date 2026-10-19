using System;
using System.Collections.Generic;
using Google.Protobuf;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 一条连接上在途的请求-响应调用表(muduo RpcChannel 的 outstandings_ 在客户端的对应物)。
    /// 大厅 <c>GameClient</c> 与战斗直连 <c>BattleDirectLink</c> 共用,两条连接的完成语义因此逐条相同
    /// (docs/design/client-rpc-callback.md §2)。
    ///
    /// 只在主线程使用,不加锁。时间由所有者注入(秒,单调),本类不读时钟,便于 EditMode 测试。
    /// 每个登记项恰好完成一次:先从表里摘除再回调,所以回调里重入本表(再发请求、断线失败全部)是安全的;
    /// 回调抛出的异常交给 onCallbackException,不打断同一批里其余调用的完成。
    /// </summary>
    public sealed class OutstandingCalls
    {
        private sealed class Entry
        {
            public double Deadline;
            public Action<RpcStatus, MessageContent> Done;
        }

        private readonly Dictionary<ulong, Entry> _calls = new();
        private readonly Action<Exception> _onCallbackException;
        private ulong _lastId;

        public OutstandingCalls(Action<Exception> onCallbackException)
        {
            _onCallbackException = onCallbackException ?? throw new ArgumentNullException(nameof(onCallbackException));
        }

        public int Count => _calls.Count;

        /// <summary>认领不到的回包累计数(调用已超时 / 已断线失败后才到)。只增不减,供日志与测试观察。</summary>
        public ulong LateReplies { get; private set; }

        /// <summary>
        /// 登记一次调用,返回要写进 ClientRequest.id 的请求号(从 1 起,跳过 0:0 表示「不关联」)。
        /// 请求号在本表生命周期内单调,换连接也不复用,旧连接的迟到回包不可能认领到新调用。
        /// </summary>
        public ulong Add(double deadline, Action<RpcStatus, MessageContent> done)
        {
            if (done == null) throw new ArgumentNullException(nameof(done));
            ulong id = ++_lastId;
            if (id == 0) id = ++_lastId;
            _calls[id] = new Entry { Deadline = deadline, Done = done };
            return id;
        }

        /// <summary>
        /// 撤销登记且不回调(发送异常时调用方自己以 NotSent 完成)。返回该调用是否还在表里。
        /// </summary>
        public bool Remove(ulong id) => _calls.Remove(id);

        /// <summary>
        /// 按 MessageContent.id 认领回包。id 为 0(推送 / 单向消息的错误信封)返回 false,交给调用方按 message_id 派发;
        /// id ≠ 0 而认领不到 = 迟到回包,计数后返回 false,调用方只记日志、不得再当推送派发。
        /// </summary>
        public bool TryComplete(MessageContent reply)
        {
            if (reply == null || reply.Id == 0) return false;
            if (!_calls.Remove(reply.Id, out var entry))
            {
                LateReplies++;
                return false;
            }
            var tip = reply.ErrorMessage?.Id ?? 0;
            Invoke(entry, tip != 0 ? RpcStatus.ServerTip(tip) : RpcStatus.Ok, reply);
            return true;
        }

        /// <summary>该调用是否仍在等回包(测试与诊断用)。</summary>
        public bool IsOutstanding(ulong id) => _calls.ContainsKey(id);

        /// <summary>让期限已到(deadline ≤ now)的调用以 Timeout 完成。由所有者的 Tick 驱动。</summary>
        public void ExpireDue(double now, string detail = "rpc timeout")
        {
            if (_calls.Count == 0) return;
            List<Entry> expired = null;
            List<ulong> expiredIds = null;
            foreach (var kv in _calls)
            {
                if (kv.Value.Deadline > now) continue;
                (expiredIds ??= new List<ulong>()).Add(kv.Key);
            }
            if (expiredIds == null) return;
            foreach (var id in expiredIds)
            {
                if (_calls.Remove(id, out var entry)) (expired ??= new List<Entry>()).Add(entry);
            }
            if (expired == null) return;
            var status = RpcStatus.Timeout(detail);
            foreach (var entry in expired) Invoke(entry, status, null);
        }

        /// <summary>
        /// 让全部在途调用以 status 完成(断线 / 被新连接接管 / 关闭)。先整表摘除再逐个回调。
        /// </summary>
        public void FailAll(RpcStatus status)
        {
            if (_calls.Count == 0) return;
            var entries = new List<Entry>(_calls.Values);
            _calls.Clear();
            foreach (var entry in entries) Invoke(entry, status, null);
        }

        private void Invoke(Entry entry, RpcStatus status, MessageContent reply)
        {
            try
            {
                entry.Done(status, reply);
            }
            catch (Exception ex)
            {
                _onCallbackException(ex);
            }
        }

        /// <summary>
        /// 把认领到的回包与结局翻成类型化的 done:Ok 时解析响应体,解析失败降为 ParseError。
        /// 供 IRpcChannel 实现共用,保证两条连接的解析口径相同。
        /// </summary>
        public static Action<RpcStatus, MessageContent> Typed<TResp>(MessageParser<TResp> parser, RpcDone<TResp> done)
            where TResp : IMessage<TResp>
        {
            if (parser == null) throw new ArgumentNullException(nameof(parser));
            if (done == null) throw new ArgumentNullException(nameof(done));
            return (status, reply) =>
            {
                if (!status.IsOk)
                {
                    done(status, default);
                    return;
                }
                TResp response;
                try
                {
                    response = parser.ParseFrom(reply.SerializedMessage);
                }
                catch (Exception ex)
                {
                    done(RpcStatus.ParseError(ex.Message), default);
                    return;
                }
                done(RpcStatus.Ok, response);
            };
        }
    }
}
