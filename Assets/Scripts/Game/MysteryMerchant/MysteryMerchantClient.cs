using System;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.MysteryMerchant
{
    /// <summary>
    /// 神秘商人的网络真相(服务端设计:docs/design/mystery-merchant.md §6)。
    ///
    /// 契约:
    ///  - 查询:GetMysteryMerchantInfo(请求-应答)。应答里是服务端此刻的结论:在不在、在几线、站在哪、
    ///    什么时候走、下一次什么时候来;商人就在本线时才带货架;
    ///  - 购买:BuyMysteryMerchantGoods(请求-应答)。请求带上玩家看到的那份报价(轮次、单价、货币、物品、每份数量),
    ///    服务端发现任何一项对不上就拒绝,保证不会以玩家没看到的内容成交;成功的应答带最新货架与背包全量;
    ///  - 公告:NotifyMysteryMerchant(S2C 推送),新的一轮开始或商人换线时到达。它对所有人是同一份
    ///    (不带货架、here 恒为 false),在不在本线由本类按 scene_id 自己比;
    ///  - 只发意图、等权威回包,不做本地假成功:已购数、余额只来自服务端;买不买得起也由服务端裁决
    ///    (本地余额只是某次快照,不拿它拦购买);
    ///  - 入场通知 / 断线由宿主转发进来,本类不引用 GameClient 与 UnityEngine,EditMode 测试直接 new;
    ///  - 旧连接隔离:断线或连接身份变化时整体复位并换世代,旧连接上的迟到回调按世代丢弃;
    ///  - 旧场景隔离:换了场景(含切线)之后,上一个场景里发出的查询的应答整包丢弃并重问 ——
    ///    它的「在不在本线」与货架说的是已经离开的那条线。
    ///
    /// 用法:宿主每帧调 <see cref="Tick"/>,界面订阅 <see cref="Changed"/> 后整份读取本对象的只读状态;
    /// 倒计时由界面每帧读 <see cref="RemainingMs"/> / <see cref="UntilNextMs"/>,本类不会每帧触发 Changed。
    /// **不要再对 NotifyMysteryMerchant 调 RegisterNotify**:那是覆盖语义,会把这里的处理器静默顶掉。
    /// </summary>
    public sealed class MysteryMerchantClient : IDisposable
    {
        /// <summary>
        /// 两次查询之间的最小间隔。gate 的限流按消息号计数,新消息不在限流表里,走默认配额
        /// (跨两个整秒内 3 次);2.5 秒一发与配额隔着一倍以上的余量(推导同 SceneChannelClient.MinListIntervalSeconds)。
        /// 太近的查询不丢:排队,到点补发一次。
        /// </summary>
        public const float MinInfoIntervalSeconds = 2.5f;
        /// <summary>两次购买之间的最小间隔(连点保护,口径同 SceneChannelClient.MinSwitchIntervalSeconds)。</summary>
        public const float MinBuyIntervalSeconds = 1f;
        /// <summary>被 gate 限流(1008)之后至少再等这么久才发。</summary>
        public const float RateLimitBackoffSeconds = MinInfoIntervalSeconds;

        public const string InfoUnavailableMessage = "神秘商人的行踪暂时打听不到。";
        public const string BuyPendingMessage = "正在购买…";
        public const string TooFastMessage = "操作太快了，请稍候再试。";
        public const string RecoveryMessage = "网络异常，购买结果未知。请重新登录后查看背包。";
        public const string NotConnectedMessage = "尚未连接到游戏。";

        private const string ServerTipPrefix = "server tip=";

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<ulong> _currentSceneId;
        private readonly Func<float> _clock;

        private object _observedConnection;
        private bool _disposed;
        // 会话世代:断线 / 连接身份变化 / Dispose 时 +1。回调带着发出时的世代,对不上就整包丢弃。
        private int _generation;
        // 场景世代:每进一次场景(含切线)+1。查询带着发出时的场景世代,对不上说明它问的是上一条线。
        private int _sceneEpoch;
        // 手里那份结论是在哪个场景世代里采纳的。新旧比较(server_time_ms)只在同一个场景世代内有意义:
        // 跨节点换场景之后,两个节点的时钟不可比。
        private int _infoEpoch;

        // 最近一份服务端结论,以及收到它时的本地时钟:两者合起来推算「服务端现在几点」。
        private float _infoReceivedAt;
        // 下一次允许发查询 / 购买的时刻(存「下次可发时刻」而不是「上次发送时刻」,理由同 SceneChannelClient)。
        private float _nextInfoAt = float.NegativeInfinity;
        private float _nextBuyAt = float.NegativeInfinity;
        // 查询在途期间又有人要查:应答回来后再问一次(在途的那一次可能问得太早)。
        private bool _requeryAfterPending;
        // 查询连续失败的次数,以及下一次自动重试的时刻(没有待重试时为正无穷)。
        private int _infoFailures;
        private float _infoRetryAt = float.PositiveInfinity;
        // 到点自动查询:针对哪一个「下一次出现时刻」、已经问过几次(MysteryMerchantModels.AutoRefreshDelayMs)。
        private ulong _autoRefreshNextAppearMs;
        private int _autoRefreshAttempt;
        // 已经报过「出现了」的那一次落位(轮次 + 场景实例):同一次落位不重复报。
        private ulong _appearedRoundId;
        private ulong _appearedSceneId;
        private MysteryMerchantPresence _lastPresence = MysteryMerchantPresence.Unknown;
        private bool _inBattle;

        /// <summary>状态变化(界面整份重读)。倒计时走动不触发。</summary>
        public event Action Changed;
        /// <summary>
        /// 商人出现了(新的一轮,或同一轮内换了线)。参数:线号、是否就在本线、还会停留多少毫秒。
        /// 只由服务端公告触发;自己主动查询查到商人在场不算「出现」。同一次落位(同一轮、同一条线)只报一次:
        /// 跨节点换场景时两个节点各发过一条公告,玩家不该看到两次。
        /// </summary>
        public event Action<uint, bool, long> Appeared;
        /// <summary>一次购买成功。参数:买的那件商品(购买前界面上看到的那一份)、份数。</summary>
        public event Action<global::MysteryMerchantGoodsInfo, uint> Purchased;

        /// <summary>最近一份服务端结论;null = 还没问到(或断线后已复位)。只读:不要改它的字段。</summary>
        public global::MysteryMerchantInfo Info { get; private set; }
        public bool HasInfo => Info != null;
        /// <summary>查询已发出、应答未到。</summary>
        public bool InfoPending { get; private set; }
        /// <summary>有一次查询因最小间隔排着队,到点会自动补发。</summary>
        public bool InfoQueued { get; private set; }
        /// <summary>购买已发出、应答未到。</summary>
        public bool BuyPending { get; private set; }
        /// <summary>在途购买的商品 id;不在途为 0。</summary>
        public uint BuyingGoodsId { get; private set; }
        /// <summary>
        /// 一次购买的应答超时 / 传输失败之后为 true:GameClient 按消息号先进先出匹配应答,
        /// 迟到的旧应答会被当成下一次购买的结果,所以在真实断线重连之前不再发购买(做法同 GuildClient)。
        /// </summary>
        public bool RequiresReconnect { get; private set; }
        /// <summary>状态行:购买进度 / 结论、查询失败、本地拒绝。"" = 没有要说的。</summary>
        public string Status { get; private set; } = "";
        public bool StatusIsError { get; private set; }
        /// <summary>
        /// 最近一次购买成功时服务端给的余额(按货币类型下标);null = 还没有成功购买过。
        /// **只用于显示,而且只在那一刻准**:之后别处赚了钱 / 花了钱它不会跟着变。宿主拿到更新的背包快照后应当改用那一份。
        /// </summary>
        public global::CurrencyComp Currency { get; private set; }

        /// <summary>宿主才知道的状态:战斗 / 观战中。写入值有变化时发变化通知。</summary>
        public bool InBattle
        {
            get => _inBattle;
            set { if (_inBattle == value) return; _inBattle = value; Changed?.Invoke(); }
        }

        /// <summary>能不能发请求:没释放、传输就绪、有角色。</summary>
        public bool Ready => !_disposed && _net.IsReady && _net.PlayerId != 0;

        /// <summary>推算的服务端当前时刻(毫秒);没有任何结论时为 0。</summary>
        public ulong ServerNowMs
        {
            get
            {
                if (Info == null) return 0;
                float elapsed = _clock() - _infoReceivedAt;
                if (!(elapsed > 0f)) return Info.ServerTimeMs;   // 负数 / 非数:按刚收到算
                return Info.ServerTimeMs + (ulong)(elapsed * 1000.0);
            }
        }

        public MysteryMerchantPresence Presence => MysteryMerchantModels.Evaluate(Info, _currentSceneId(), ServerNowMs);
        public long RemainingMs => MysteryMerchantModels.RemainingMs(Info, ServerNowMs);
        public long UntilNextMs => MysteryMerchantModels.UntilNextMs(Info, ServerNowMs);

        public MysteryMerchantClient(IBattleTransport transport, Func<object> connectionIdentity,
            Func<ulong> currentSceneId, Func<float> clock)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _currentSceneId = currentSceneId ?? throw new ArgumentNullException(nameof(currentSceneId));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _observedConnection = _connectionIdentity();
            _net.Disconnected += HandleDisconnected;
            _net.RegisterNotify(MessageIds.NotifyMysteryMerchant, HandleNotify);
            // 构造不发请求:第一次查询由进场(HandleSceneEntered)或界面触发。
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            // IBattleTransport 没有注销推送的接口;处理器开头的 _disposed 判断保证 Dispose 之后的推送不再改动状态。
            _disposed = true;
            ++_generation;
            InfoPending = false;
            InfoQueued = false;
            BuyPending = false;
            BuyingGoodsId = 0;
        }

        /// <summary>按 id 在当前货架上找商品;不在架上(或没有货架)返回 null。</summary>
        public global::MysteryMerchantGoodsInfo FindGoods(uint goodsId)
        {
            if (Info == null) return null;
            foreach (var goods in Info.Goods)
                if (goods.GoodsId == goodsId) return goods;
            return null;
        }

        /// <summary>此刻货架上这件商品的报价;不在架上返回 default(IsValid == false)。</summary>
        public MysteryMerchantQuote QuoteFor(uint goodsId) =>
            MysteryMerchantQuote.Of(Info?.RoundId ?? 0, FindGoods(goodsId));

        // ── 查询 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 问一次服务端。太近的请求不丢:排队,到点由 <see cref="Tick"/> 补发。
        /// 已有请求在途时不重复发,但记下「回来之后再问一次」:在途的那一次可能问得太早
        /// (公告刚到、刚被告知货架过时、刚换了场景)。
        /// </summary>
        public void RequestInfo()
        {
            if (_disposed) return;
            ObserveConnection();
            if (!Ready) return;
            if (InfoPending)
            {
                _requeryAfterPending = true;
                return;
            }
            float now = _clock();
            if (now < _nextInfoAt)
            {
                if (!InfoQueued) { InfoQueued = true; Changed?.Invoke(); }
                return;
            }
            SendInfo(now);
        }

        private void SendInfo(float now)
        {
            InfoQueued = false;
            InfoPending = true;
            _requeryAfterPending = false;
            _infoRetryAt = float.PositiveInfinity;   // 这一发就是重试;再失败由 FailInfo 重新安排
            _nextInfoAt = now + MinInfoIntervalSeconds;
            int generation = _generation;
            int sceneEpoch = _sceneEpoch;
            Changed?.Invoke();
            _net.Call(MessageIds.GetMysteryMerchantInfo, new global::GetMysteryMerchantInfoRequest(),
                global::GetMysteryMerchantInfoResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    InfoPending = false;
                    bool askAgain = _requeryAfterPending;
                    _requeryAfterPending = false;
                    if (sceneEpoch != _sceneEpoch)
                    {
                        // 这是上一个场景里问的:它说的「在不在本线」与货架都属于已经离开的那条线。丢弃并重问。
                        askAgain = true;
                    }
                    else if (response.ErrorMessage != null && response.ErrorMessage.Id != 0)
                    {
                        // 查询没有业务拒绝码;带码回来只可能是 gate 层的事(限流等)。
                        FailInfo(response.ErrorMessage.Id);
                    }
                    else if (response.Info == null)
                    {
                        FailInfo(0);
                    }
                    else
                    {
                        _infoFailures = 0;
                        ApplyInfo(response.Info, true);
                        // 查询成功:收掉上一条「打听不到」;购买的结论(成功 / 被拒)留着给玩家看。
                        if (Status == InfoUnavailableMessage) SetStatus("", false);
                    }
                    if (askAgain) RequestInfo();
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    InfoPending = false;
                    bool askAgain = _requeryAfterPending || sceneEpoch != _sceneEpoch;
                    _requeryAfterPending = false;
                    // 查询是幂等的只读请求:失败了自己退避重试,不需要等重连。
                    FailInfo(ParseEnvelopeTip(error));
                    if (askAgain) RequestInfo();
                    Changed?.Invoke();
                });
        }

        /// <summary>一次查询失败:写状态行,并安排自动重试(间隔逐次拉长,见 MysteryMerchantModels.InfoRetryDelaySeconds)。</summary>
        private void FailInfo(uint tipId)
        {
            if (_infoFailures < int.MaxValue) ++_infoFailures;
            float now = _clock();
            _infoRetryAt = now + MysteryMerchantModels.InfoRetryDelaySeconds(_infoFailures);
            if (tipId == (uint)common_error.KRateLimitExceeded)
            {
                float backoff = now + RateLimitBackoffSeconds;
                if (backoff > _nextInfoAt) _nextInfoAt = backoff;
            }
            SetStatus(InfoUnavailableMessage, true);
        }

        // ── 购买 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 按 <paramref name="quote"/>(玩家点下「购买」时看到的那份报价)买 <paramref name="count"/> 份。
        /// 返回 false = 本地拒绝(原因在 <see cref="Status"/>),没有发出请求。
        /// 返回 true 只表示请求已发出;结果看随后的 <see cref="Purchased"/> / <see cref="Status"/>。
        ///
        /// 报价与此刻的货架对不上(换了一轮、这件下架了、价格 / 内容变了)时本地就拒绝:界面上已经是新的一份,
        /// 让玩家看清楚再点。发出去的请求带着报价的每一项,服务端会再核一遍(两边之间还隔着一次网络往返)。
        /// </summary>
        public bool Buy(MysteryMerchantQuote quote, uint count)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (BuyPending) return false;   // 在途:不改状态行,等那一次的结论
            var presence = Presence;
            if (presence != MysteryMerchantPresence.Here) return RejectLocally(MysteryMerchantModels.NotHereText);
            var goods = FindGoods(quote.GoodsId);
            if (goods == null || !quote.IsValid || quote != MysteryMerchantQuote.Of(Info.RoundId, goods))
                return RejectLocally(MysteryMerchantModels.ShelfChangedText);
            var block = MysteryMerchantModels.EvaluateBuy(goods, presence, true, _inBattle);
            if (block != MysteryMerchantBuyBlock.None) return RejectLocally(MysteryMerchantModels.DescribeBlock(block));
            if (count == 0 || count > MysteryMerchantModels.Remaining(goods) || count > MysteryMerchantModels.MaxBuyCountPerRequest)
                return RejectLocally(MysteryMerchantModels.LimitReachedText);
            float now = _clock();
            if (now < _nextBuyAt) return RejectLocally(TooFastMessage);

            _nextBuyAt = now + MinBuyIntervalSeconds;
            BuyPending = true;
            BuyingGoodsId = quote.GoodsId;
            int generation = _generation;
            // 回调里要用的是「购买前玩家看到的那一份」:应答回来时货架对象已经被新的一份替换。
            var seen = goods.Clone();
            SetStatus(BuyPendingMessage, false);
            Changed?.Invoke();
            _net.Call(MessageIds.BuyMysteryMerchantGoods,
                new global::BuyMysteryMerchantGoodsRequest
                {
                    RoundId = quote.RoundId,
                    GoodsId = quote.GoodsId,
                    Count = count,
                    ExpectedPrice = quote.Price,
                    ExpectedCurrencyType = quote.CurrencyType,
                    ExpectedItemConfigId = quote.ItemConfigId,
                    ExpectedItemCount = quote.ItemCount,
                },
                global::BuyMysteryMerchantGoodsResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    EndBuy();
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        FailBuy(tipId);
                        Changed?.Invoke();
                        return;
                    }
                    if (response.Info != null) ApplyInfo(response.Info, true);
                    if (response.Bag?.Currency != null) Currency = response.Bag.Currency;
                    SetStatus(MysteryMerchantModels.PurchasedText(seen, count), false);
                    Changed?.Invoke();
                    Purchased?.Invoke(seen, count);
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    EndBuy();
                    uint tipId = ParseEnvelopeTip(error);
                    if (tipId != 0)
                    {
                        // gate 层带码拒绝(限流等):请求没有到达 scene,结果是确定的「没买成」。
                        FailBuy(tipId);
                    }
                    else
                    {
                        // 超时 / 传输失败:结果未知。之后的应答无法与请求对应,等重连。
                        RequiresReconnect = true;
                        SetStatus(RecoveryMessage, true);
                    }
                    Changed?.Invoke();
                });
            return true;
        }

        private void EndBuy()
        {
            BuyPending = false;
            BuyingGoodsId = 0;
        }

        private void FailBuy(uint tipId)
        {
            if (tipId == (uint)common_error.KRateLimitExceeded)
            {
                float backoff = _clock() + RateLimitBackoffSeconds;
                if (backoff > _nextBuyAt) _nextBuyAt = backoff;
            }
            SetStatus(MysteryMerchantModels.DescribeTip(tipId), true);
            // 「不在了 / 不在本线 / 货架换了 / 名额用完」说明本地手里的那份已经过时:立刻重新问一次。
            if (MysteryMerchantModels.TipMeansStale(tipId)) RequestInfo();
        }

        private bool RejectLocally(string message)
        {
            SetStatus(message, true);
            Changed?.Invoke();
            return false;
        }

        // ── 宿主转发 ────────────────────────────────────────────────────────

        /// <summary>
        /// 进了一个场景(含同图切线):「在不在本线」与货架都要重新问。
        /// 手里的结论先留着(行踪、倒计时仍然有效),只把货架清掉 —— 那是上一条线的。
        /// 上一个场景里还在途的查询,应答回来时会被丢弃并重问(见 SendInfo)。
        /// </summary>
        public void HandleSceneEntered()
        {
            if (_disposed) return;
            ObserveConnection();
            ++_sceneEpoch;
            if (Info != null && Info.Goods.Count != 0)
            {
                var kept = Info.Clone();
                kept.Goods.Clear();
                kept.Here = false;
                Info = kept;
            }
            RequestInfo();
            NotePresence();
            Changed?.Invoke();
        }

        /// <summary>断线:整体复位。重连进场后由 <see cref="HandleSceneEntered"/> 重新问。</summary>
        public void HandleDisconnected()
        {
            if (_disposed) return;
            Reset();
            Changed?.Invoke();
        }

        /// <summary>
        /// 每帧调一次:补发排队的查询与失败后的重试;商人到点离开时发一次变化通知;
        /// 到了下一次出现的时刻而公告没来时自动问(次数与节奏见 MysteryMerchantModels.AutoRefreshDelayMs)。
        /// </summary>
        public void Tick()
        {
            if (_disposed) return;
            ObserveConnection();
            float now = _clock();
            bool retryDue = _infoFailures > 0 && now >= _infoRetryAt;
            if ((InfoQueued || retryDue) && Ready && !InfoPending && now >= _nextInfoAt) SendInfo(now);

            // 在场 → 离开(到了离开时刻)等转变没有网络事件驱动,靠这里发现。
            if (NotePresence()) Changed?.Invoke();

            if (Info == null || !Ready || InfoPending || InfoQueued) return;
            ulong next = Info.NextAppearAtMs;
            if (next == 0 || Presence != MysteryMerchantPresence.Away) return;
            if (next != _autoRefreshNextAppearMs)
            {
                _autoRefreshNextAppearMs = next;
                _autoRefreshAttempt = 0;
            }
            long delayMs = MysteryMerchantModels.AutoRefreshDelayMs(_autoRefreshAttempt, _net.PlayerId);
            if (delayMs < 0 || ServerNowMs < next + (ulong)delayMs) return;
            ++_autoRefreshAttempt;
            RequestInfo();
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        private void HandleNotify(MessageContent content)
        {
            if (_disposed) return;
            ObserveConnection();
            global::MysteryMerchantNotify notify = null;
            if (content != null)
            {
                // 坏包直接丢:推送是至多一次的,下一次查询会补上。
                try { notify = global::MysteryMerchantNotify.Parser.ParseFrom(content.SerializedMessage); }
                catch (InvalidProtocolBufferException) { notify = null; }
            }
            if (notify?.Info == null || !notify.Info.Active) return;

            // 比手里那份还旧的公告(迟到)不采纳,也不再弹一次「出现了」。
            if (!ApplyInfo(notify.Info, false)) return;
            bool here = Presence == MysteryMerchantPresence.Here;
            // 公告不带货架。商人就在本线时马上把货架问回来,玩家点开商店就有东西看。
            if (here) RequestInfo();
            Changed?.Invoke();
            // 同一次落位只报一次:跨节点换场景时,旧节点与新节点各自发过一条说同一件事的公告。
            if (Info.RoundId == _appearedRoundId && Info.SceneId == _appearedSceneId) return;
            _appearedRoundId = Info.RoundId;
            _appearedSceneId = Info.SceneId;
            Appeared?.Invoke(Info.ChannelNo, here, RemainingMs);
        }

        /// <summary>
        /// 采纳一份服务端结论;返回 false = 没有采纳。
        ///
        /// <paramref name="authoritative"/> = 这是本连接上、当前场景里问出来的应答(查询 / 购买):它就是所在节点此刻的结论,
        /// 一律采纳。公告(false)则要比新旧 —— 比手里那份更旧(server_time_ms 更小)的丢弃;但这个比较只在
        /// 「手里那份也是在当前场景里拿到的」时才做:换过场景之后,新旧两个节点的时钟不可比。
        /// </summary>
        private bool ApplyInfo(global::MysteryMerchantInfo incoming, bool authoritative)
        {
            if (incoming == null) return false;
            if (!authoritative && Info != null && _infoEpoch == _sceneEpoch && incoming.ServerTimeMs < Info.ServerTimeMs)
                return false;
            // 公告不带货架:同一轮、同一条线上已经有货架时,别让一条公告把它清空。
            if (Info != null && incoming.Goods.Count == 0 && Info.Goods.Count != 0 && incoming.Active
                && incoming.RoundId == Info.RoundId && incoming.SceneId == Info.SceneId
                && incoming.SceneId == _currentSceneId())
            {
                var merged = incoming.Clone();
                merged.Goods.Add(Info.Goods.Clone());
                merged.Here = true;
                incoming = merged;
            }
            Info = incoming;
            _infoEpoch = _sceneEpoch;
            _infoReceivedAt = _clock();
            NotePresence();
            return true;
        }

        /// <summary>记下此刻的在场状态;与上次不同返回 true。</summary>
        private bool NotePresence()
        {
            var presence = Presence;
            if (presence == _lastPresence) return false;
            _lastPresence = presence;
            return true;
        }

        private void SetStatus(string text, bool isError)
        {
            // 「购买结果未知,请重新登录」必须留到重连:别让随后一次查询的成败、一次本地拒绝把它盖掉。
            if (RequiresReconnect && text != RecoveryMessage) return;
            Status = text ?? "";
            StatusIsError = isError && Status.Length != 0;
        }

        private bool IsCurrent(int generation) =>
            !_disposed && generation == _generation && ReferenceEquals(_observedConnection, _connectionIdentity());

        /// <summary>连接身份变了(换服、重连换了连接对象)而没有收到断线事件时,同样整体复位。</summary>
        private void ObserveConnection()
        {
            var identity = _connectionIdentity();
            if (ReferenceEquals(identity, _observedConnection)) return;
            _observedConnection = identity;
            Reset();
            Changed?.Invoke();
        }

        private void Reset()
        {
            ++_generation;
            ++_sceneEpoch;
            Info = null;
            Currency = null;
            InfoPending = false;
            InfoQueued = false;
            BuyPending = false;
            BuyingGoodsId = 0;
            RequiresReconnect = false;
            _requeryAfterPending = false;
            _infoFailures = 0;
            _infoRetryAt = float.PositiveInfinity;
            _nextInfoAt = float.NegativeInfinity;
            _nextBuyAt = float.NegativeInfinity;
            _autoRefreshNextAppearMs = 0;
            _autoRefreshAttempt = 0;
            _appearedRoundId = 0;
            _appearedSceneId = 0;
            _lastPresence = MysteryMerchantPresence.Unknown;
            SetStatus("", false);
        }

        private static uint ParseEnvelopeTip(string error) =>
            error != null && error.StartsWith(ServerTipPrefix, StringComparison.Ordinal)
            && uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
                out uint tipId)
                ? tipId
                : 0;
    }
}
