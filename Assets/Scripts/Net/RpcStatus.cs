using System;
using Google.Protobuf;

namespace MmorpgClient.Net
{
    /// <summary>一次请求-响应调用的结局。语义对齐服务端节点间 RPC 的 RpcStatus(docs/design/client-rpc-callback.md §2)。</summary>
    public enum RpcStatusCode : byte
    {
        /// <summary>收到回包且没有信封错误;响应体已解析。</summary>
        Ok = 0,
        /// <summary>本地就没发出(未连接 / 未就绪 / 发送异常):确定未执行,重试安全。</summary>
        NotSent,
        /// <summary>发出后连接断开或被新连接接管,没等到回包:结果未知。</summary>
        Disconnected,
        /// <summary>期限内没等到回包:结果未知。</summary>
        Timeout,
        /// <summary>服务端用信封错误(MessageContent.error_message)回绝,码在 <see cref="RpcStatus.TipId"/>。</summary>
        ServerTip,
        /// <summary>回包到了但响应体解析失败。</summary>
        ParseError,
    }

    /// <summary>
    /// 结构化的调用结局。新代码按 <see cref="Code"/> / <see cref="TipId"/> 判断,不解析字符串;
    /// 旧的字符串错误契约只经 <see cref="ToLegacyError"/> 一处翻译。
    /// </summary>
    public readonly struct RpcStatus
    {
        public RpcStatusCode Code { get; }
        /// <summary>仅 <see cref="RpcStatusCode.ServerTip"/> 有值。</summary>
        public uint TipId { get; }
        /// <summary>给日志看的细节(断线原因、异常文本);不承载判定。</summary>
        public string Detail { get; }

        private RpcStatus(RpcStatusCode code, uint tipId, string detail)
        {
            Code = code;
            TipId = tipId;
            Detail = detail;
        }

        public bool IsOk => Code == RpcStatusCode.Ok;

        /// <summary>
        /// 传输层失败且请求可能已被服务端执行(超时 / 断线)。有副作用的请求按「结果未知」处理:
        /// 复用同一个业务幂等键重发,或先查询再决定。gate 级 1003 信封(下游调用失败)同样是结果未知,
        /// 但它属于 <see cref="RpcStatusCode.ServerTip"/>,由业务按 tip 表的 fault 列判断。
        /// </summary>
        public bool OutcomeUnknown => Code == RpcStatusCode.Timeout || Code == RpcStatusCode.Disconnected;

        public static RpcStatus Ok => default;
        public static RpcStatus NotSent(string detail) => new(RpcStatusCode.NotSent, 0, detail);
        public static RpcStatus Disconnected(string detail) => new(RpcStatusCode.Disconnected, 0, detail);
        public static RpcStatus Timeout(string detail) => new(RpcStatusCode.Timeout, 0, detail);
        public static RpcStatus ServerTip(uint tipId) => new(RpcStatusCode.ServerTip, tipId, null);
        public static RpcStatus ParseError(string detail) => new(RpcStatusCode.ParseError, 0, detail);

        /// <summary>
        /// 旧字符串错误契约(IBattleTransport.Call 等的 onError)的唯一翻译点。
        /// 传输层失败 = transportPrefix + Detail(大厅前缀为空,战斗直连为 "link: ");
        /// 服务端拒绝 = "server tip=N";解析失败 = "parse response: …"。业务模块按这些前缀判定,改文案前先改它们。
        /// </summary>
        public string ToLegacyError(string transportPrefix = "")
        {
            switch (Code)
            {
                case RpcStatusCode.Ok: return null;
                case RpcStatusCode.ServerTip: return $"server tip={TipId}";
                case RpcStatusCode.ParseError: return $"parse response: {Detail}";
                default: return transportPrefix + Detail;
            }
        }

        public override string ToString()
            => Code switch
            {
                RpcStatusCode.Ok => "ok",
                RpcStatusCode.ServerTip => $"server tip={TipId}",
                _ => $"{Code}: {Detail}",
            };
    }

    /// <summary>每次调用自带的完成回调。status 非 Ok 时 response 为 null。</summary>
    public delegate void RpcDone<in TResp>(RpcStatus status, TResp response);

    /// <summary>
    /// 客户端到一个服务端连接(gate 大厅 / 战斗直连)的调用面:请求-响应、单向发送、推送注册三件事。
    /// 生成的类型化调用桩只依赖本接口(docs/design/client-rpc-callback.md §5)。
    ///
    /// 契约(实现必须满足):
    /// <list type="bullet">
    /// <item>CallMethod 给请求分配非 0 的 ClientRequest.id 并登记 done;回包按 MessageContent.id 认领,
    ///   不再按 message_id 猜。done 恰好调用一次:回包 / 信封错误 / 超时 / 断线 / 本地未发出。</item>
    /// <item>本地未发出(未连接 / 未就绪 / 发送异常)时 done 在 CallMethod 返回前同步以 NotSent 调用;
    ///   其余结局都在之后的收包派发或 Tick 中、在主线程上调用。</item>
    /// <item>SendOneWay 发 ClientRequest.id = 0,不登记回调。服务端对它的错误信封同样带 id = 0,
    ///   与推送一样按 message_id 交给 RegisterNotify 的处理器。</item>
    /// <item>id ≠ 0 却认领不到的回包(调用已超时 / 已断线失败)是迟到回包:丢弃并记日志,不交给推送处理器。</item>
    /// <item>RegisterNotify 同一 message_id 只保留最后一次注册(覆盖语义)。</item>
    /// </list>
    /// </summary>
    public interface IRpcChannel
    {
        void CallMethod<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser, RpcDone<TResp> done)
            where TResp : IMessage<TResp>;

        void SendOneWay(uint messageId, IMessage request);

        void RegisterNotify(uint messageId, Action<MessageContent> handler);
    }
}
