using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// <see cref="IBattleTransport"/> 假实现:记录全部出站调用,允许测试
    /// 手动喂响应(Respond/FailWith)与 S2C 推送(PushNotify),
    /// 让 BattleClient 状态机在无网络/无 Unity 运行时的 EditMode 下可穷打。
    /// </summary>
    internal sealed class FakeBattleTransport : IBattleTransport
    {
        /// <summary>一次被记录的 Call:测试用 Respond 喂响应,或 FailWith 喂错误。</summary>
        public sealed class RecordedCall
        {
            public uint MessageId;
            public IMessage Request;
            public Action<IMessage> Respond;   // 序列化 round-trip 后回调 onResponse
            public Action<string> FailWith;    // 直通 onError
        }

        private readonly Dictionary<uint, Action<MessageContent>> _notify = new();

        public readonly List<RecordedCall> Calls = new();
        public readonly List<(uint MessageId, IMessage Request)> OneWays = new();

        public ulong PlayerId { get; set; } = 1001;
        public bool IsReady { get; set; } = true;

        public event Action Disconnected;

        public void RegisterNotify(uint messageId, Action<MessageContent> handler)
            => _notify[messageId] = handler;

        public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                                Action<TResp> onResponse, Action<string> onError)
            where TResp : IMessage<TResp>
        {
            Calls.Add(new RecordedCall
            {
                MessageId = messageId,
                Request = request,
                // 经字节 round-trip 解析,和真实链路(SerializedMessage → Parser)同构
                Respond = msg => onResponse(parser.ParseFrom(msg.ToByteString())),
                FailWith = onError,
            });
        }

        public void SendOneWay(uint messageId, IMessage request)
            => OneWays.Add((messageId, request));

        /// <summary>模拟服务端 S2C 推送(payload 按真实链路序列化进 MessageContent)。</summary>
        public void PushNotify(uint messageId, IMessage payload)
        {
            if (!_notify.TryGetValue(messageId, out var handler))
                throw new InvalidOperationException($"没有注册 message_id={messageId} 的推送处理器");
            handler(new MessageContent
            {
                MessageId = messageId,
                SerializedMessage = payload.ToByteString(),
            });
        }

        /// <summary>模拟 gate 断线。</summary>
        public void RaiseDisconnected() => Disconnected?.Invoke();

        /// <summary>取指定 message_id 的全部出站 Call(便于断言轮询次数)。</summary>
        public List<RecordedCall> CallsOf(uint messageId)
        {
            var result = new List<RecordedCall>();
            foreach (var c in Calls)
                if (c.MessageId == messageId) result.Add(c);
            return result;
        }
    }
}
