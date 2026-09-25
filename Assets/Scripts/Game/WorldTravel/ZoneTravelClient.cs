using System;
using System.Collections;
using MmorpgClient.Net;

namespace MmorpgClient.Game.WorldTravel
{
    /// <summary>
    /// 跨 zone 场景传送的客户端调用点(服务端设计:docs/design/cross-zone-scene-travel.md CZ-7)。
    ///
    /// 本文件引用的三个生成符号**已生成入库**:MessageIds.TravelToZone(Net/MessageIds.cs)、
    /// TravelToZoneRequest / TravelToZoneResponse(Proto/Generated/PlayerScene.cs)。
    /// 服务端协议再变时按下面两条重新生成,备查(&lt;服务端仓&gt; 换成本机的服务端仓库根目录;
    /// 脚本的默认 ProtoRoot 未必指向它,显式传最稳):
    ///   pwsh -File tools/gen_messageids.ps1 -ProtoRoot &lt;服务端仓&gt;   → MessageIds.TravelToZone
    ///   pwsh -File tools/gen_proto.ps1      -ProtoRoot &lt;服务端仓&gt;   → TravelToZoneRequest / TravelToZoneResponse
    /// 引用这三个符号的代码仍然只写在本文件(Assets/Scripts 只有一个 asmdef,一红全红):
    /// GameClient / DevAutoPilot / 地图窗只经 <see cref="TravelToZone"/> 这一个入口间接用到它们。
    ///
    /// 契约(proto/scene/player_scene.proto 的 TravelToZone 注释):
    ///  - 应答无错 = **已受理**(源 scene 已冻结玩家并开始存盘),不代表已到达;
    ///  - 成了   = 之后收到 msg 124 RedirectToGate,<see cref="GameClient"/> 的 RedirectFlow 连到目标 zone,
    ///             最终以 OnSceneEntered 为准;
    ///  - 没成   = 同步拒绝在响应体 error_message 里;受理之后才发生的失败只会以 msg 23 推送到达
    ///             (GameClient.OnServerTip),那时源 scene 已解冻,玩家留在原地。
    ///
    /// 在途状态(IsTravelPending)与四个收口点(msg 124 / msg 23 / 断线 / 超时)都在 GameClient 里,
    /// 本类无状态、无定时器,不需要 Tick。
    /// **不要在这里对 msg 23 / msg 124 调 OnNotify**:那是覆盖语义,会把 GameClient 的处理器静默盖掉,
    /// 重定向直接失效。要听 tip 一律订阅 GameClient.OnServerTip。
    /// </summary>
    public sealed class ZoneTravelClient
    {
        /// <summary>
        /// 等 msg 124 / 失败 tip 的客户端预算(秒)。必须大于服务端源 scene 交接的最坏时长,
        /// 否则客户端先报超时、服务端随后又把人搬走,或失败 tip 到达时已不在途、原因被吞掉。
        /// 服务端那一段是**两道**看门狗串行(存盘 + 等 scene_manager 应答,各 kTravelReplyBudgetSec=30s,
        /// cpp/libs/services/scene/player/system/player_lifecycle.cpp),最坏约 60s 才有结论 ——
        /// 只按一道看门狗取 60 的话,两段都踩满时客户端必然先到期(预算从发请求之前就开始计)。
        /// 与同区换图受理后的等待共用同一个数,只在 <see cref="CityTravelRequest"/> 维护一处。
        /// </summary>
        public const float TravelBudgetSec = (float)CityTravelRequest.AcceptedHandoffBudgetSeconds;

        private readonly GameClient _game;

        public ZoneTravelClient(GameClient game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }

        /// <summary>
        /// 请求传送到 <paramref name="targetZoneId"/> 的 <paramref name="sceneConfigId"/> 地图
        /// (0 = 由目标 zone 的 scene_manager 按世界频道表挑默认大世界)。回家也是同一个入口(CZ-9)。
        ///
        /// <paramref name="onAccepted"/> 只表示"服务端受理了"(或 msg 124 已经到了),**不是抵达**:
        /// 成功看之后的 GameClient.OnSceneEntered,失败看 OnServerTip / OnDisconnected / 调用方自己的超时。
        /// <paramref name="onError"/> 只报同步失败(前置条件不满足 / RPC 失败 / 响应体带拒绝码)。
        /// 两个回调恰好触发其一。
        /// </summary>
        public IEnumerator TravelToZone(uint targetZoneId, uint sceneConfigId,
                                        Action onAccepted, Action<string> onError)
        {
            if (targetZoneId == 0) { onError?.Invoke("目标区服无效"); yield break; }

            // 先记下当前连接再进在途:后面靠它判断"连接是不是已经被 RedirectFlow 换掉了"。
            object gateAtStart = _game.GateConnectionIdentity;
            string rejected = _game.BeginZoneTravel(TravelBudgetSec);
            if (rejected != null) { onError?.Invoke(rejected); yield break; }

            TravelToZoneResponse resp = null;
            string callErr = null;
            yield return _game.Call(MessageIds.TravelToZone,
                new TravelToZoneRequest { TargetZoneId = targetZoneId, SceneConfigId = sceneConfigId },
                TravelToZoneResponse.Parser, r => resp = r, e => callErr = e);

            // msg 124 可能抢在应答之前到:RedirectFlow 一接管就会摘掉老连接的消息派发,这条应答永远收不到,
            // Call 最终以 "disconnected" 收场。那是**传送成了**,不是失败 —— 不先判这一条,
            // UI 会在传送成功的同时弹"传送失败"。
            // 判据刻意不写成"连接变了就算成功":真断线时连接会变成 null,那种情况必须按失败报。
            object gateNow = _game.GateConnectionIdentity;
            bool takenOverByRedirect = _game.IsRedirecting ||
                                       (gateNow != null && !ReferenceEquals(gateNow, gateAtStart));
            if (takenOverByRedirect) { onAccepted?.Invoke(); yield break; }

            if (resp == null)
            {
                // RPC 层失败(发不出 / 15s 超时 / 信封错误 / 断线)。"rpc timeout" 时服务端其实可能已经受理,
                // 这里仍然收口:迟到的 msg 124 不依赖在途标志,照样会把人搬过去,不会卡死。
                _game.EndZoneTravel($"传送失败({callErr})");
                onError?.Invoke($"传送失败({callErr})");
                yield break;
            }
            // GameClient.Call 只折算信封里的 error_message;业务拒绝码(目标区不存在 / 战斗中 / 组队中 /
            // 目标区繁忙)在**响应体**里,必须自己看,否则所有同步拒绝都会被当成"已受理"然后干等超时。
            if (resp.ErrorMessage != null && resp.ErrorMessage.Id != 0)
            {
                string text = GameClient.DescribeTravelTip(resp.ErrorMessage.Id);
                _game.EndZoneTravel(text);
                onError?.Invoke(text);
                yield break;
            }

            onAccepted?.Invoke();
        }
    }
}
