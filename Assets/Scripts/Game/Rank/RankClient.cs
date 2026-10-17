using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using Rankpb;

namespace MmorpgClient.Game.Rank
{
    /// <summary>
    /// 排行榜适配器(服务端 docs/design/leaderboard-system.md §4,proto/rank/rank.proto,service ClientPlayerRank)。
    /// 只有一个读方法 GetRankPage:一页 + 调用者自己的名次合成一次请求(客户端零编号 gRPC 回包按消息号先进先出匹配,
    /// 同一功能只能单请求在途,§4.2)。会话代次、玩家、连接对象三重校验照 GuildClient / JubaozhaiClient,
    /// 旧回包不能污染换角、重连或换 Gate 之后的会话。
    /// 1. 同时只发一个请求;在途时再翻页只记下想要的页码,回包后补发一次(最后一次点击为准)。
    /// 2. 本地按 gate 的限流窗口节流(两次至少隔 <see cref="MinRequestIntervalSeconds"/>,<see cref="MessageWindowSeconds"/> 内
    ///    最多 <see cref="SendQuota"/> 次):超出的翻页不丢,排队到点由 <see cref="Tick"/> 补发。
    /// 3. 仍被 gate 限频(信封错误 "server tip=1008")时丢掉排队的页码、退避 <see cref="RateLimitBackoffSeconds"/>,
    ///    退避期间的翻页只提示、不排队 —— §4.5「被限频时客户端不得自动重试」,否则 gate 的非法包计数会一路累加到断线。
    /// 4. 只有 onError 为 "rpc timeout" 时进入 RequiresReconnect 隔离(超时分支已删掉在途记录,迟到回包会被同连接下一个同号请求误领);
    ///    其余传输错误不串包,只提示文案。
    /// 请求刻意不带 player_id:"我是谁"只取 gate 注入的会话(D-9);自己未上榜时 self.rank = 0,**不是错误**。
    /// </summary>
    public sealed class RankClient : IDisposable
    {
        /// <summary>客户端单页 5 行(§4.6;服务端上限 50,超过按上限钳制)。</summary>
        public const uint PageSize = 5;
        /// <summary>两次发送的最小间隔(只防连点,与 TeamClient 同值)。gate 的限流另算,见 <see cref="MessageWindowSeconds"/>。</summary>
        public const float MinRequestIntervalSeconds = 0.4f;
        /// <summary>
        /// 本地发送窗口。gate 的 MessageLimiter 按 message_id 计数、时间戳取整秒,淘汰条件是 now − 最早 > 1,
        /// 所以"每秒 N 次"实际是"跨两个整秒、最长约 2 秒内只放 N 次";再留 0.5 秒给上行抖动(推导同 TeamClient.MessageWindowSeconds)。
        /// </summary>
        public const float MessageWindowSeconds = 2.5f;
        /// <summary>
        /// 窗口内最多发几次 = data/MessageLimiter.xlsx 里 GetRankPage 那一行的每秒次数(服务端 tools/scripts/rank_xlsx_patch.py 写入 5,§4.5)。
        /// 那一行没配时 gate 用默认 3 次:多出的请求被拒、走下面的退避,不会自动重试。
        /// </summary>
        public const int SendQuota = 5;
        /// <summary>被 gate 限频之后至少再等这么久才允许发(期间的翻页只提示,不排队);取一个完整窗口,让 gate 那边的计数先滚出去。</summary>
        public const float RateLimitBackoffSeconds = MessageWindowSeconds;
        public const string RecoveryMessage = "排行榜请求状态尚未确认，请重新登录角色后再试。";
        public const string LoadingMessage = "正在读取排行榜，请稍候…";
        public const string NotInGameMessage = "请进入角色后再打开排行榜。";
        public const string TransportFailureMessage = "排行榜请求未完成，请稍后重试。";
        public const string EmptyMessage = "榜上暂无记录，打一局论剑即可上榜。";
        // 与 GameClient.Call 超时分支 onError("rpc timeout") 的文字逐字一致;两边改动必须同步。
        public const string TimeoutError = "rpc timeout";
        // GameClient.Call 把 MessageContent 信封上的 TipInfoMessage 折成 onError("server tip=N")(gate 限频走这条路)。
        public const string ServerTipPrefix = "server tip=";

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private object _observedConnection;
        private int _generation;
        private bool _disposed;
        // 待发的页码(0 = 没有):在途时又要了另一页,或本地节流还没到点。回包后 / Tick 到点补发一次。
        private uint _pendingPage;
        // 最近 SendQuota 次发送的时刻(本地发送窗口)与最后一次发送时刻。
        private readonly Queue<float> _sentAt = new Queue<float>();
        private float _lastSendAt = float.NegativeInfinity;
        // 被 gate 限频后的退避截止;早于它的翻页只提示、不排队。
        private float _rateLimitedUntil = float.NegativeInfinity;

        public ulong PlayerId => _net.PlayerId;
        /// <summary>
        /// 最近一次成功的回包;null = 未加载或已作废(换角 / 重连 / 读失败)。
        /// 被 gate 限频时保留:那次请求没到服务端,手上这页仍是最近一次读到的结果。
        /// </summary>
        public GetRankPageResponse Page { get; private set; }
        /// <summary>有请求在途。</summary>
        public bool Busy { get; private set; }
        /// <summary>有请求在途,或有一页排着队等发(界面据此禁用翻页,免得连点只记住同一页)。</summary>
        public bool Waiting => Busy || _pendingPage != 0;
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "";
        public event Action Changed;

        /// <param name="clock">单调递增的秒数(界面传 Time.realtimeSinceStartup;测试注入假时钟)。</param>
        public RankClient(IBattleTransport net, Func<object> connectionIdentity, Func<float> clock)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _observedConnection = _connectionIdentity();
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>当前页码(未加载时为 1)。</summary>
        public uint CurrentPage => Page?.Page ?? 1;
        /// <summary>总页数(至少为 1,与服务端 page_count 一致)。</summary>
        public uint PageCount => Math.Max(1, Page?.PageCount ?? 1);

        /// <summary>读第 page 页(0 视为 1;越过末页由服务端钳到末页)。在途或离上次发送太近时只记下页码,稍后补发。</summary>
        public void Load(uint page)
        {
            if (_disposed) return;
            ObserveConnection();
            page = Math.Max(1, page);
            if (Busy) { _pendingPage = page; return; }
            if (RequiresReconnect) { Show(RecoveryMessage); return; }
            if (!_net.IsReady || PlayerId == 0) { Show(NotInGameMessage); return; }
            float now = _clock();
            if (now < _rateLimitedUntil)
            {
                // 退避期间不排队:到点自动补发就是 §4.5 禁止的自动重试。
                _pendingPage = 0;
                Show(DescribeTip((uint)common_error.KRateLimitExceeded));
                return;
            }
            if (!HasSendBudget(now))
            {
                _pendingPage = page;
                Show(LoadingMessage);
                return;
            }
            _pendingPage = 0;
            RecordSend(now);
            int generation = ++_generation;
            ulong player = PlayerId;
            Busy = true;
            Status = LoadingMessage;
            Changed?.Invoke();
            var request = new GetRankPageRequest { Board = RankBoard.MatchRating, Page = page, PageSize = PageSize };
            _net.Call(MessageIds.GetRankPage, request, GetRankPageResponse.Parser, response =>
            {
                if (!Current(generation, player)) return;
                Busy = false;
                Apply(response);
                FlushPending();
                Changed?.Invoke();
            }, error =>
            {
                if (!Current(generation, player)) return;
                Busy = false;
                UnityEngine.Debug.LogWarning($"[Rank] GetRankPage 失败: {error}");
                uint envelopeTip = ParseEnvelopeTip(error);
                if (error == TimeoutError)
                {
                    RequiresReconnect = true;
                    _pendingPage = 0;
                    Page = null;
                    Status = RecoveryMessage;
                }
                else if (envelopeTip == (uint)common_error.KRateLimitExceeded)
                {
                    _pendingPage = 0;
                    _rateLimitedUntil = Math.Max(_rateLimitedUntil, _clock() + RateLimitBackoffSeconds);
                    Status = DescribeTip(envelopeTip);
                }
                else
                {
                    // 与业务拒绝同一口径:旧页挂在"读失败"的提示下面会被当成最新名次。
                    Page = null;
                    Status = envelopeTip != 0 ? DescribeTip(envelopeTip) : TransportFailureMessage;
                    FlushPending();
                }
                Changed?.Invoke();
            });
        }

        /// <summary>重读当前页(刷新键 / 打开窗口)。</summary>
        public void Refresh() => Load(CurrentPage);

        /// <summary>每帧由界面调用:排队的那一页到点补发(限频退避期间不会有排队的页)。</summary>
        public void Tick()
        {
            if (_disposed || _pendingPage == 0 || Busy) return;
            float now = _clock();
            if (now < _rateLimitedUntil || !HasSendBudget(now)) return;
            FlushPending();
        }

        public void Reset()
        {
            // 在途请求被作废后,它之后的超时回调会被代次校验吞掉,迟到回包就可能被同连接下一个同号请求误领,
            // 所以在途时 Reset 必须隔离到真实断线或换 Gate(同 GuildClient / JubaozhaiClient)。
            if (Busy) RequiresReconnect = true;
            ++_generation;
            Busy = false;
            _pendingPage = 0;
            Page = null;
            Status = RequiresReconnect ? RecoveryMessage : "";
            Changed?.Invoke();
        }

        public void ObserveConnection()
        {
            object current = _connectionIdentity();
            if (ReferenceEquals(current, _observedConnection)) return;
            // GameClient 静默换 Gate 不发 Disconnected;真实连接对象变化才解除隔离。新连接的限频计数从零开始,退避一并清掉。
            _observedConnection = current;
            Busy = false;
            RequiresReconnect = false;
            ClearSendThrottle();
            Reset();
        }

        public void Dispose()
        {
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_generation;
            Busy = false;
            _pendingPage = 0;
        }

        /// <summary>分 × 100 的整数格式化成两位小数(1516.29);客户端不做浮点换算,避免 0.01 的显示误差。</summary>
        public static string FormatRating(uint ratingCenti) =>
            (ratingCenti / 100).ToString(CultureInfo.InvariantCulture) + "." + (ratingCenti % 100).ToString("D2", CultureInfo.InvariantCulture);

        /// <summary>
        /// 错误码 → 文案。回包体里 rank 服务只用 common 段的 kInvalidParameter / kServiceUnavailable(§4.5);
        /// kRateLimitExceeded 来自信封(gate 限频),其余码(信封上的路由 / 熔断类)给通用文案并带上码,方便对账。
        /// </summary>
        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)common_error.KInvalidParameter => "排行榜请求无效，请重新登录角色后再试。",
            (uint)common_error.KServiceUnavailable => "排行榜暂时不可用，请稍后再试。",
            (uint)common_error.KRateLimitExceeded => "翻页过快，请稍后再试。",
            _ => "排行榜读取失败(" + tipId.ToString(CultureInfo.InvariantCulture) + ")。",
        };

        /// <summary>传输层 onError 串里信封上的错误码;不是 "server tip=N" 形状时返回 0。</summary>
        public static uint ParseEnvelopeTip(string error) =>
            error != null && error.StartsWith(ServerTipPrefix, StringComparison.Ordinal)
            && uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
                out uint tipId)
                ? tipId
                : 0;

        private void Apply(GetRankPageResponse response)
        {
            uint tip = response.ErrorMessage?.Id ?? 0;
            if (tip != 0)
            {
                // 失败时清空当前页:旧页挂在"读失败"的提示下面,会被当成最新名次。
                Page = null;
                Status = DescribeTip(tip);
                return;
            }
            Page = response;
            Status = response.Entries.Count == 0 ? EmptyMessage : "";
        }

        private void FlushPending()
        {
            if (_pendingPage == 0 || Busy || RequiresReconnect) return;
            uint page = _pendingPage;
            _pendingPage = 0;
            Load(page);
        }

        /// <summary>本地节流是否放行:离上次够远,且窗口内还有配额(写成"最早一次 + 窗口"而不是减法,理由同 TeamClient.TooSoon)。</summary>
        private bool HasSendBudget(float now) =>
            now >= _lastSendAt + MinRequestIntervalSeconds
            && (_sentAt.Count < SendQuota || now >= _sentAt.Peek() + MessageWindowSeconds);

        private void RecordSend(float now)
        {
            _lastSendAt = now;
            _sentAt.Enqueue(now);
            while (_sentAt.Count > SendQuota) _sentAt.Dequeue();
        }

        private void ClearSendThrottle()
        {
            _sentAt.Clear();
            _lastSendAt = float.NegativeInfinity;
            _rateLimitedUntil = float.NegativeInfinity;
        }

        private void Show(string message)
        {
            Status = message;
            Changed?.Invoke();
        }

        private void HandleDisconnected()
        {
            // 仅真实连接断开时,旧连接回包才不可能再进入下一连接。
            _observedConnection = _connectionIdentity();
            Busy = false;
            RequiresReconnect = false;
            ClearSendThrottle();
            Reset();
        }

        private bool Current(int generation, ulong player) =>
            !_disposed && generation == _generation && _net.IsReady && PlayerId == player
            && ReferenceEquals(_observedConnection, _connectionIdentity());
    }
}
