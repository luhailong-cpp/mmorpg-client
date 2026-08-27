using System;
using Google.Protobuf;

namespace MmorpgClient.Game.Battle
{
    /// <summary>
    /// <see cref="IBattleTransport"/> 的生产实现:把 BattleClient 的网络依赖
    /// 原样接到 GameClient 现有管线上 ——
    /// 请求-响应走 GameClient.Call(协程,经 CoroutineRunner 宿主启动),
    /// 单向消息走 GameClient.SendOneWay,S2C 推送走 GameClient.OnNotify。
    /// 不新增任何网络栈/线程,回调全部落在 Unity 主线程(由 GameClient.Tick 驱动)。
    /// </summary>
    public sealed class GameClientBattleTransport : IBattleTransport
    {
        private readonly GameClient _game;

        public GameClientBattleTransport(GameClient game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }

        public ulong PlayerId => _game.PlayerId;

        public bool IsReady => _game.IsGateReady;

        /// <summary>直接转发 GameClient.OnDisconnected(事件访问器转发,不额外存状态)。</summary>
        public event Action Disconnected
        {
            add => _game.OnDisconnected += value;
            remove => _game.OnDisconnected -= value;
        }

        public void RegisterNotify(uint messageId, Action<MessageContent> handler)
            => _game.OnNotify(messageId, handler);

        public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                                Action<TResp> onResponse, Action<string> onError)
            where TResp : IMessage<TResp>
        {
            // GameClient.Call 是协程(内部带 15s 超时 + Tick 驱动),需要宿主启动。
            // CoroutineRunner 由 AppBootstrap 在 Awake 时接线(与重定向流程共用)。
            var runner = _game.CoroutineRunner;
            if (runner == null)
            {
                onError?.Invoke("协程宿主未就绪(CoroutineRunner 未接线)");
                return;
            }
            runner(_game.Call(messageId, request, parser, onResponse, onError));
        }

        public void SendOneWay(uint messageId, IMessage request)
            => _game.SendOneWay(messageId, request);
    }
}
