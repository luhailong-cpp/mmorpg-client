using System;
using Google.Protobuf;

namespace MmorpgClient.Net
{
    /// <summary>
    /// <see cref="IRpcChannel"/> 的限流与熔断装饰器：可包在任意通道外层，统一执行 <see cref="RpcGuard"/> 门禁与收口。
    /// </summary>
    public sealed class GuardedRpcChannel : IRpcChannel
    {
        private readonly IRpcChannel _inner;
        private readonly RpcGuard _guard;

        public GuardedRpcChannel(IRpcChannel inner, RpcGuard guard)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        }

        public RpcGuard Guard => _guard;

        public void CallMethod<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser, RpcDone<TResp> done)
            where TResp : IMessage<TResp>
        {
            if (!_guard.TryBeginCall(messageId, out var rejection))
            {
                done?.Invoke(rejection, default);
                return;
            }
            _inner.CallMethod(messageId, request, parser, (status, response) =>
            {
                _guard.CompleteCall(messageId, status);
                done?.Invoke(status, response);
            });
        }

        public void SendOneWay(uint messageId, IMessage request) => _inner.SendOneWay(messageId, request);

        public void RegisterNotify(uint messageId, Action<MessageContent> handler) => _inner.RegisterNotify(messageId, handler);
    }
}
