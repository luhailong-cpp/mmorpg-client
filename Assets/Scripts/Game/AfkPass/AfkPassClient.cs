using System;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.AfkPass
{
    /// <summary>
    /// 挂机月卡的服务端契约(服务端 docs/design/afk-monthly-card.md §6)。0 表示服务端尚未分配该方法;两个方法都有独立的有效消息号才启用。
    /// GM 设定(GmSetAfkPass)只给联调与 robot 用,客户端不发。
    /// </summary>
    public readonly struct AfkPassMessageIds
    {
        public uint Get { get; }
        public uint Buy { get; }
        public bool IsSupported => Get != 0 && Buy != 0 && Get != Buy;

        public AfkPassMessageIds(uint get, uint buy)
        {
            Get = get;
            Buy = buy;
        }

        public static AfkPassMessageIds Generated => new AfkPassMessageIds(MessageIds.GetAfkPass, MessageIds.BuyAfkPass);
    }

    /// <summary>挂机月卡的展示规则。纯函数,不碰网络与 Unity,EditMode 测试直接调。</summary>
    public static class AfkPassModels
    {
        public const string NotOpenedText = "未开通";
        public const string ExpiredText = "已到期";
        public const string NotForSaleText = "挂机月卡暂不出售。";
        public const string ExpiredReminderText = "挂机月卡已到期，断线后角色不再离线托管。";
        public const string HostingDescription =
            "开通后，断线时角色不会在 25 秒后下线：留在原地离线托管，组队时照常跟着队长走，直到重新登录或月卡到期。";

        public static string CurrencyName(uint currencyType) => currencyType switch
        {
            0 => "银两",
            1 => "灵石",
            2 => "绑定灵石",
            _ => "货币",
        };

        public static ulong RemainingSeconds(ulong expiresAt, ulong serverNow) => expiresAt > serverNow ? expiresAt - serverNow : 0;

        /// <summary>面板上的状态行:未开通 / 剩余 …(见 <see cref="FormatDuration"/>)/ 已到期。</summary>
        public static string FormatRemaining(ulong expiresAt, ulong remainingSeconds)
        {
            if (expiresAt == 0) return NotOpenedText;
            return remainingSeconds == 0 ? ExpiredText : FormatRemainingPhrase(remainingSeconds);
        }

        // 不到 1 分钟以汉字开头，不插空格；其余以数字开头，插一个空格。
        private static string FormatRemainingPhrase(ulong remainingSeconds) =>
            (remainingSeconds < 60 ? "剩余" : "剩余 ") + FormatDuration(remainingSeconds);

        /// <summary>X 天 Y 小时 / X 小时 Y 分 / X 分钟 / 不到 1 分钟。只取整、不进位:显示的永远不比实际多。</summary>
        public static string FormatDuration(ulong seconds)
        {
            ulong days = seconds / 86400;
            ulong hours = seconds % 86400 / 3600;
            ulong minutes = seconds % 3600 / 60;
            if (days > 0) return $"{days} 天 {hours} 小时";
            if (hours > 0) return $"{hours} 小时 {minutes} 分";
            return minutes > 0 ? $"{minutes} 分钟" : "不到 1 分钟";
        }

        /// <summary>还有效,而且剩余不超过 <paramref name="remindBeforeHours"/> 小时(0 = 不提醒)。</summary>
        public static bool InReminderWindow(ulong remainingSeconds, uint remindBeforeHours) =>
            remainingSeconds > 0 && remindBeforeHours > 0 && remainingSeconds <= (ulong)remindBeforeHours * 3600;

        public static string ReminderText(ulong remainingSeconds) =>
            $"挂机月卡{FormatRemainingPhrase(remainingSeconds)}，到期后断线将不再离线托管。";

        /// <summary>开通被拒的原因。服务端 tip 码在 data/tip/Tip.xlsx 的 //afk_pass_error 段。</summary>
        public static string DescribeTip(uint tipId) => DescribeTip(tipId, 1);

        /// <summary>按实际售价币种描述开通被拒原因:灵石保留充值引导,其它币种不误报「灵石不足」。</summary>
        public static string DescribeTip(uint tipId, uint currencyType) => tipId switch
        {
            (uint)afk_pass_error.KAfkPassPriceChanged => "挂机月卡价格已变化，请刷新后再试。",
            (uint)afk_pass_error.KAfkPassCurrencyNotEnough => currencyType == 1
                ? "灵石不足，无法开通挂机月卡。可在聚宝斋充值灵石。"
                : $"{CurrencyName(currencyType)}不足，无法开通挂机月卡。",
            (uint)afk_pass_error.KAfkPassStackLimit => "挂机月卡剩余时长已达上限，暂不能续费。",
            (uint)afk_pass_error.KAfkPassFrozen => "角色当前状态不能开通挂机月卡，请稍后再试。",
            (uint)afk_pass_error.KAfkPassUnavailable => "挂机月卡暂时无法开通。",
            (uint)common_error.KRateLimitExceeded => "操作太快了，请稍候再试。",
            (uint)common_error.KServiceUnavailable => "服务暂时不可用，请稍后再试。",
            _ => $"开通失败（{tipId}）。",
        };

        /// <summary>这个拒绝说明手里的报价已经过时:要重新查一次。</summary>
        public static bool TipMeansStale(uint tipId) =>
            tipId == (uint)afk_pass_error.KAfkPassPriceChanged || tipId == (uint)afk_pass_error.KAfkPassStackLimit;
    }

    /// <summary>
    /// 挂机月卡的网络真相(服务端 docs/design/afk-monthly-card.md §7)。
    ///
    /// 契约:
    ///  - 查询:GetAfkPass。应答是服务端此刻的结论(到期时刻、服务器时间、售价、余额);剩余时长按「服务器时间 + 收到应答后
    ///    本地单调时钟走过的秒数」推算,不信本机墙钟;
    ///  - 开通 / 续费:BuyAfkPass。请求带幂等键与玩家看到的报价;只发意图、等权威回包,不做本地假成功,
    ///    买不买得起由服务端裁决(本地余额只是某次快照,不拿它拦);
    ///  - 结果不明(超时 / 信封 1003)、连接没变、还没重发过:原样重发一次(同一个键);重发也不明就进入「结果未知」,
    ///    真实断线重连之前不再发开通,避免重复扣费(同 MysteryMerchantClient);
    ///  - 提醒:登录后剩余进入 remind_before_hours 时提醒一次;在线期间到期时提醒一次。只发事件,宿主决定怎么显示;
    ///  - 断线或连接身份变化时整体复位并换世代,旧连接上的迟到回调按世代丢弃。
    ///
    /// 用法:宿主每帧调 <see cref="Tick"/>,界面订阅 <see cref="Changed"/> 后整份读取;倒计时由界面每帧读 <see cref="RemainingSeconds"/>。
    /// 本类不引用 GameClient 与 UnityEngine,EditMode 测试直接 new。
    /// </summary>
    public sealed class AfkPassClient : IDisposable
    {
        /// <summary>两次查询之间的最小间隔(gate 默认配额是跨两个整秒 3 次,推导同 MysteryMerchantClient)。太近的查询排队,到点补发。</summary>
        public const float MinInfoIntervalSeconds = 2.5f;
        /// <summary>两次开通之间的最小间隔(连点保护)。</summary>
        public const float MinBuyIntervalSeconds = 1f;

        public const string UnsupportedMessage = "当前服务器未开放挂机月卡。";
        public const string NotConnectedMessage = "尚未连接到游戏。";
        public const string InfoUnavailableMessage = "挂机月卡状态暂时查不到。";
        public const string BuyPendingMessage = "正在开通…";
        public const string BoughtMessage = "挂机月卡已开通。";
        public const string TooFastMessage = "操作太快了，请稍候再试。";
        public const string RecoveryMessage = "网络异常，开通结果未知。请重新登录后查看。";

        private readonly IBattleTransport _net;
        private readonly AfkPassMessageIds _messageIds;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;

        private object _observedConnection;
        private bool _disposed;
        // 会话世代:断线 / 连接身份变化 / Dispose 时 +1。回调带着发出时的世代,对不上就整包丢弃。
        private int _generation;
        private float _infoReceivedAt;
        private float _nextInfoAt = float.NegativeInfinity;
        private float _nextBuyAt = float.NegativeInfinity;
        private bool _infoQueued;
        // 本次连接里的提醒:见过有效的月卡、已提醒「快到期」、已提醒「已到期」。
        private bool _sawActive;
        private bool _remindedSoon;
        private bool _remindedExpired;

        /// <summary>状态变化(界面整份重读)。倒计时走动不触发。</summary>
        public event Action Changed;
        /// <summary>快到期 / 已到期提醒的文案。宿主用飘字显示。</summary>
        public event Action<string> Reminder;

        /// <summary>最近一份服务端结论;null = 还没问到(或断线后已复位)。只读:不要改它的字段。</summary>
        public global::AfkPassInfo Info { get; private set; }
        public bool InfoPending { get; private set; }
        public bool BuyPending { get; private set; }
        /// <summary>一次开通的结果不明之后为 true:请求可能已经成交,真实断线重连、重新取得权威状态之前不再发开通。</summary>
        public bool RequiresReconnect { get; private set; }
        /// <summary>状态行:开通进度 / 结论、查询失败、本地拒绝。"" = 没有要说的。</summary>
        public string Status { get; private set; } = "";
        public bool StatusIsError { get; private set; }

        public bool IsSupported => _messageIds.IsSupported;
        public bool Ready => IsSupported && !_disposed && _net.IsReady && _net.PlayerId != 0;

        /// <summary>推算的服务器当前时刻(Unix 秒);没有任何结论时为 0。</summary>
        public ulong ServerNowSec
        {
            get
            {
                if (Info == null) return 0;
                float elapsed = _clock() - _infoReceivedAt;
                return elapsed > 0f ? Info.ServerNow + (ulong)elapsed : Info.ServerNow;   // 负数 / 非数:按刚收到算
            }
        }

        public ulong RemainingSeconds => Info == null ? 0 : AfkPassModels.RemainingSeconds(Info.ExpiresAt, ServerNowSec);
        public bool Active => RemainingSeconds > 0;
        public bool ForSale => Info != null && Info.Price != 0;
        public string RemainingText => Info == null ? "" : AfkPassModels.FormatRemaining(Info.ExpiresAt, RemainingSeconds);

        public AfkPassClient(IBattleTransport transport, Func<object> connectionIdentity, Func<float> clock,
            AfkPassMessageIds? messageIds = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _messageIds = messageIds ?? AfkPassMessageIds.Generated;
            _observedConnection = _connectionIdentity();
            _net.Disconnected += HandleDisconnected;
            if (!IsSupported) SetStatus(UnsupportedMessage, false);
            // 构造不发请求:第一次查询由进场(HandleSceneEntered)或界面触发。
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_generation;
            InfoPending = false;
            BuyPending = false;
        }

        // ── 查询 ────────────────────────────────────────────────────────────

        /// <summary>问一次服务端。太近的请求不丢:排队,到点由 <see cref="Tick"/> 补发;已有查询在途时不重复发。</summary>
        public void RequestInfo()
        {
            if (_disposed) return;
            if (!IsSupported) { SetStatus(UnsupportedMessage, false); return; }
            bool reset = ObserveConnection();
            if (!Ready || InfoPending)
            {
                if (reset) Changed?.Invoke();
                return;
            }
            float now = _clock();
            if (now < _nextInfoAt)
            {
                _infoQueued = true;
                if (reset) Changed?.Invoke();
                return;
            }
            SendInfo(now);
        }

        private void SendInfo(float now)
        {
            _infoQueued = false;
            InfoPending = true;
            _nextInfoAt = now + MinInfoIntervalSeconds;
            int generation = _generation;
            Changed?.Invoke();
            _net.Call(_messageIds.Get, new global::GetAfkPassRequest(), global::GetAfkPassResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    InfoPending = false;
                    if ((response.ErrorMessage?.Id ?? 0) != 0 || response.Info == null) SetInfoStatus(InfoUnavailableMessage, true);
                    else
                    {
                        ApplyInfo(response.Info);
                        if (Status == InfoUnavailableMessage) SetInfoStatus("", false);
                    }
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    // 查询是幂等的只读请求:不需要等重连,玩家再打开面板时再问。
                    InfoPending = false;
                    SetInfoStatus(InfoUnavailableMessage, true);
                    Changed?.Invoke();
                });
        }

        // ── 开通 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 按此刻手里的报价(<see cref="Info"/> 的价格与币种)开通 / 续费一次。返回 false = 本地拒绝(原因在 <see cref="Status"/>),
        /// 没有发出请求;返回 true 只表示请求已发出,结论看随后的 <see cref="Changed"/>。
        /// </summary>
        public bool Buy()
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsSupported) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (BuyPending) return false;   // 在途:不改状态行,等那一次的结论
            if (Info == null)
            {
                RequestInfo();
                return RejectLocally(InfoUnavailableMessage);
            }
            if (!ForSale) return RejectLocally(AfkPassModels.NotForSaleText);
            float now = _clock();
            if (now < _nextBuyAt) return RejectLocally(TooFastMessage);
            // 这一次开通的幂等键(服务端 client-request-idempotency.md §2.1):本地检查都过了才生成,
            // 拿到明确结果之前的重发都带它,服务端按 (玩家, 键) 去重,不会扣两次钱。
            if (!ClientRequestKeys.TryCreate(out ulong requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);

            _nextBuyAt = now + MinBuyIntervalSeconds;
            BuyPending = true;
            int generation = _generation;
            SetStatus(BuyPendingMessage, false);
            Changed?.Invoke();
            // 只构造一次:结果不明时的重发必须逐字节相同(同一个键、同一份报价),服务端按参数指纹认重发。
            var request = new global::BuyAfkPassRequest
            {
                ClientRequestId = requestKey,
                ExpectedCurrencyType = Info.PriceCurrencyType,
                ExpectedPrice = Info.Price,
            };
            // 回调只认最近一次发出的那一个:重发之后前一次的迟到回调不能收掉这一次。
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.Buy, request, global::BuyAfkPassResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        BuyPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            if (tipId == (uint)common_error.KInvalidParameter) ClientRequestKeys.ReportKeyReused("BuyAfkPass");
                            if (tipId == (uint)common_error.KRateLimitExceeded) _nextBuyAt = _clock() + MinInfoIntervalSeconds;
                            SetStatus(AfkPassModels.DescribeTip(tipId, request.ExpectedCurrencyType), true);
                            if (AfkPassModels.TipMeansStale(tipId)) RequestInfo();
                            Changed?.Invoke();
                            return;
                        }
                        // replayed = true 是首次那一次的结果(重发命中了已成交的那一次,没有再扣钱):与普通成功走同一条路。
                        if (response.Info != null) ApplyInfo(response.Info);
                        SetStatus(BoughtMessage, false);
                        Changed?.Invoke();
                    },
                    error =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        // 结果不明(超时 / 信封 1003)、连接没变、还没重发过:保持在途,原样重发一次。
                        if (!resent && ClientRequestKeys.ShouldResendOnce(error))
                        {
                            resent = true;
                            Send();
                            return;
                        }
                        BuyPending = false;
                        uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
                        // 重发过就说明首发结果不明:重发被 gate 带码拒绝只说明重发没到 scene,首发可能已经成交。
                        if (!resent && tipId != 0 && tipId != (uint)common_error.KServiceUnavailable)
                        {
                            // gate / 路由层带码拒绝(限流等):请求没有到达 scene,结果是确定的「没开通」。
                            if (tipId == (uint)common_error.KRateLimitExceeded) _nextBuyAt = _clock() + MinInfoIntervalSeconds;
                            SetStatus(AfkPassModels.DescribeTip(tipId, request.ExpectedCurrencyType), true);
                        }
                        else
                        {
                            RequiresReconnect = true;
                            SetStatus(RecoveryMessage, true);
                        }
                        Changed?.Invoke();
                    });
            }
            Send();
            return true;
        }

        // ── 宿主转发 ────────────────────────────────────────────────────────

        /// <summary>进了一个场景:登录(或重连)之后第一次进场时问一次,用于剩余天数与快到期提醒。之后换场景不再问。</summary>
        public void HandleSceneEntered()
        {
            if (_disposed) return;
            bool reset = ObserveConnection();
            if (Info == null && !InfoPending)
            {
                RequestInfo();
                // RequestInfo 命中限频排队或尚未 Ready 时不会触发 Changed,连接复位仍要通知界面清旧状态。
                if (reset && !InfoPending) Changed?.Invoke();
                return;
            }
            if (reset) Changed?.Invoke();
        }

        /// <summary>断线:整体复位。重连进场后由 <see cref="HandleSceneEntered"/> 重新问。</summary>
        public void HandleDisconnected()
        {
            if (_disposed) return;
            Reset();
            Changed?.Invoke();
        }

        /// <summary>每帧调一次:补发排队的查询;在线期间进入提醒窗口或到期时发提醒。</summary>
        public void Tick()
        {
            if (_disposed || !IsSupported) return;
            if (ObserveConnection()) Changed?.Invoke();
            float now = _clock();
            if (_infoQueued && Ready && !InfoPending && now >= _nextInfoAt) SendInfo(now);
            CheckReminders();
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        private void ApplyInfo(global::AfkPassInfo info)
        {
            Info = info;
            _infoReceivedAt = _clock();
            CheckReminders();
        }

        private void CheckReminders()
        {
            if (Info == null) return;
            ulong remaining = RemainingSeconds;
            if (remaining > 0)
            {
                _sawActive = true;
                _remindedExpired = false;
                bool inWindow = AfkPassModels.InReminderWindow(remaining, Info.RemindBeforeHours);
                if (inWindow && !_remindedSoon)
                {
                    _remindedSoon = true;
                    Reminder?.Invoke(AfkPassModels.ReminderText(remaining));
                }
                else if (!inWindow) _remindedSoon = false;   // 续费到窗口之外:之后再进窗口时再提醒
                return;
            }
            // 在线期间到期:只在这次连接里见过它有效时提醒(登录时就已到期的不算「刚到期」)。
            if (_sawActive && !_remindedExpired)
            {
                _remindedExpired = true;
                Reminder?.Invoke(AfkPassModels.ExpiredReminderText);
            }
        }

        private bool ObserveConnection()
        {
            var current = _connectionIdentity();
            if (ReferenceEquals(current, _observedConnection)) return false;
            _observedConnection = current;
            Reset();
            return true;
        }

        private void Reset()
        {
            ++_generation;
            Info = null;
            InfoPending = false;
            BuyPending = false;
            RequiresReconnect = false;
            _infoQueued = false;
            _sawActive = false;
            _remindedSoon = false;
            _remindedExpired = false;
            SetStatus(IsSupported ? "" : UnsupportedMessage, false);
        }

        private bool IsCurrent(int generation) => !_disposed && generation == _generation;

        private bool RejectLocally(string message)
        {
            SetStatus(message, true);
            Changed?.Invoke();
            return false;
        }

        // 「结果未知」的警告要一直留到重连:查询的成败不能把它盖掉。
        private void SetInfoStatus(string message, bool isError)
        {
            if (RequiresReconnect) return;
            SetStatus(message, isError);
        }

        private void SetStatus(string message, bool isError)
        {
            Status = message ?? "";
            StatusIsError = isError && Status.Length != 0;
        }
    }
}
