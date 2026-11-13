using System;
using System.Security.Cryptography;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 业务幂等键(服务端 docs/design/client-request-idempotency.md §2.1)。与信封的 ClientRequest.id 是两回事:
    /// 信封 id 每次发送都换号、重连后从头数;幂等键属于「玩家点的这一次操作」,这次操作拿到明确结果
    /// (受理,或带 tip 的拒绝)之前的所有重发都带同一个,拿到明确结果之后作废。
    /// 服务端按 (玩家, 键) 去重,键随玩家存档落盘:同键同参数的重发不再执行,回首次结果并标 replayed;
    /// 同键换了参数回 kInvalidParameter(客户端把旧键用在了新操作上,见 <see cref="ReportKeyReused"/>)。0 = 不去重。
    /// </summary>
    public static class ClientRequestKeys
    {
        /// <summary>拿不到安全随机数时给玩家看的文案(调用方原样显示,不发包)。</summary>
        public const string UnavailableText = "无法安全生成请求编号，本次操作未发送";

        /// <summary>
        /// 生成一个非 0 的随机 64 位键。用密码学随机源:自增号在重装或换设备后会撞号。
        /// 拿不到安全随机数时返回 false、key 为 0:调用方不发包、提示玩家,不得退回别的随机源。
        /// </summary>
        public static bool TryCreate(out ulong key)
        {
            key = 0;
            try
            {
                using var random = RandomNumberGenerator.Create();
                var bytes = new byte[8];
                do
                {
                    random.GetBytes(bytes);
                    key = BitConverter.ToUInt64(bytes, 0);
                } while (key == 0);
                return true;
            }
            catch (CryptographicException)
            {
                key = 0;
                return false;
            }
        }

        /// <summary>
        /// 旧字符串错误契约(<see cref="RpcStatus.ToLegacyError"/>)下,这次失败是否「结果不明、可以原样重发」:
        /// 超时(<see cref="OutstandingCalls.TimeoutDetail"/>)与信封 1003("server tip=1003",gate 等下游超时或下游失败)为真。
        /// 断线("disconnected",连接已换)、别的信封码(1008 限流等,确定没执行)、本地没发出、回包解析失败、不认识的串一律为假。
        /// 只有带了幂等键(或协议自带请求号)的请求才能据此重发,不带键的重发会被服务端当成新的一次操作;
        /// 重发几次由调用方定(业务写请求一律只重发一次)。
        /// </summary>
        public static bool ShouldResendOnce(string legacyError)
        {
            if (legacyError == OutstandingCalls.TimeoutDetail) return true;
            return RpcStatus.TryParseLegacyServerTip(legacyError, out uint tipId)
                && tipId == (uint)common_error.KServiceUnavailable;
        }

        /// <summary>
        /// 带键的请求在响应体里收到 kInvalidParameter:服务端认出这个键已经用于另一次参数不同的操作,
        /// 说明客户端把旧键用在了新操作上(客户端缺陷)。调用方照常按拒绝收尾,这里只留下一条显眼的错误日志。
        /// </summary>
        public static void ReportKeyReused(string method)
            => UnityEngine.Debug.LogError("[client-request] key reused for another operation: " + method);
    }
}
