using System;
using Google.Protobuf;

namespace MmorpgClient.Game.Battle
{
    /// <summary>
    /// <see cref="IBattleTransport"/> 的生产实现:把 BattleClient 的网络依赖
    /// 原样接到 GameClient 现有管线上 ——
    /// 请求-响应直接走 GameClient.CallMethod(超时与派发由主循环 GameClient.Tick 驱动),
    /// 单向消息走 GameClient.SendOneWay,S2C 推送走 GameClient.OnNotify。
    /// 不新增任何网络栈/线程/协程,回调全部落在 Unity 主线程。
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
            // 直接转发 CallMethod,避免协程首帧同步重入 GameClient.Tick() 以及每笔在途 RPC 的每帧冗余 Tick。
            _game.CallMethod(messageId, request, parser, (status, response) =>
            {
                if (status.IsOk) onResponse?.Invoke(response);
                else onError?.Invoke(status.ToLegacyError());
            });
        }

        public void SendOneWay(uint messageId, IMessage request)
            => _game.SendOneWay(messageId, request);
    }
}
