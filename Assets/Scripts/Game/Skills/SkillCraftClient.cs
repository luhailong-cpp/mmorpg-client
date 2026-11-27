using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using TradePb = Trade;

namespace MmorpgClient.Game.Skills
{
    /// <summary>
    /// 率土式分卷宗技能制书、未学书架研习/拆页与主城名匠代打摊位的客户端业务适配器。
    /// 严格遵守服务端权威原则：材料余额、卷宗池、书架、熟练度与摊位列表全部以服务端快照为准；
    /// 写请求携带幂等键与 expected_revision，弱网结果未知时保留原请求字节重放，不换键重扣。
    /// </summary>
    public sealed class SkillCraftClient : IDisposable
    {
        public static SkillCraftClient Instance { get; private set; }

        public const string NotInGameMessage = "尚未进入游戏，典籍制书信息未同步";
        public const string LoadingMessage = "正在同步典籍制书信息…";
        public const string DisconnectedMessage = "连接已断开，典籍制书信息已过期，重连后请刷新";
        public const string PendingWriteText = "上次制书/书架操作结果尚未确认，请先重试原请求";
        public const string BattleBlockedText = "战斗中或观战中不可进行制书、研习、拆页或修改摊位。";
        private const int MaxAutoRecoveryAttempts = 3;
        private const long MinWriteIntervalMs = 800;
        private const uint WantedListPageSize = 50;          // trade 一页上限 50
        private const long WantedFollowUpIntervalMs = 2000;
        private const int MaxWantedFollowUpPolls = 15;        // 约 30 秒仍没结论就停,提示稍后刷新

        private enum PendingKind
        {
            None = 0, Craft = 1, Learn = 2, Dismantle = 3, Seal = 4, Unseal = 5,
            ClaimMilestone = 6, BatchDismantle = 7, PostWanted = 8, CancelWanted = 9, FulfillWanted = 10,
        }

        private sealed class PendingRequest
        {
            public PendingKind Kind;
            public CraftSkillBookRequest Craft;
            public LearnSkillBookRequest Learn;
            public DismantleSkillBookRequest Dismantle;
            public SealSkillBookRequest Seal;
            public UnsealSkillBookRequest Unseal;
            public ClaimScrollCollectionMilestoneRequest ClaimMilestone;
            public BatchDismantleSkillBooksRequest BatchDismantle;
            // 悬赏求书三种走 trade 求购单(kind 7):发榜、揭榜的请求号是字符串;撤单只带订单号,CancelCommerceOrder 按订单幂等。
            public TradePb.CreateWantedSkillBookOrderRequest PostWanted;
            public TradePb.CancelCommerceOrderRequest CancelWanted;
            public TradePb.FulfillWantedSkillBookOrderRequest FulfillWanted;
            public string EncodedRecord;
            public ulong ClientRequestId => Kind switch
            {
                PendingKind.Craft => Craft?.ClientRequestId ?? 0,
                PendingKind.Learn => Learn?.ClientRequestId ?? 0,
                PendingKind.Dismantle => Dismantle?.ClientRequestId ?? 0,
                PendingKind.Seal => Seal?.ClientRequestId ?? 0,
                PendingKind.Unseal => Unseal?.ClientRequestId ?? 0,
                PendingKind.ClaimMilestone => ClaimMilestone?.ClientRequestId ?? 0,
                PendingKind.BatchDismantle => BatchDismantle?.ClientRequestId ?? 0,
                _ => 0,
            };
            // 恢复记录按种类核对身份字段齐全;trade 的三种没有 64 位键。
            public bool HasIdentity => Kind switch
            {
                PendingKind.PostWanted => !string.IsNullOrEmpty(PostWanted?.ClientRequestId),
                PendingKind.CancelWanted => !string.IsNullOrEmpty(CancelWanted?.OrderId),
                PendingKind.FulfillWanted => !string.IsNullOrEmpty(FulfillWanted?.ClientRequestId)
                    && !string.IsNullOrEmpty(FulfillWanted.OrderId) && FulfillWanted.SealedItemId != 0,
                _ => ClientRequestId != 0,
            };
            public string Label => Kind switch
            {
                PendingKind.Craft => "拓印制书",
                PendingKind.Learn => "研习技能书",
                PendingKind.Dismantle => "拆解技能书",
                PendingKind.Seal => "技能书封卷入包",
                PendingKind.Unseal => "封卷道籍开卷",
                PendingKind.ClaimMilestone => "领取卷宗里程碑",
                PendingKind.BatchDismantle => "批量拆解技能书",
                PendingKind.PostWanted => "发布求书悬赏",
                PendingKind.CancelWanted => "撤销求书悬赏",
                PendingKind.FulfillWanted => "揭榜交书",
                _ => "制书操作",
            };
        }

        // 发榜之后等托管有结论、揭榜之后等交书有结论:按同一请求号重发原请求查(trade 对同号重发回当前结果,不会再扣一次)。
        // 只在内存里跟,不占待确认写槽:交付可能要等发榜人腾出背包,不能因此锁住制书写操作;
        // 客户端重启后不再跟,服务端照常办完,刷新悬赏榜与背包即可看到结果。
        private sealed class WantedFollowUp
        {
            public TradePb.CreateWantedSkillBookOrderRequest Post;
            public TradePb.FulfillWantedSkillBookOrderRequest Fulfill;
            public int Polls;
            public long NextPollAtMs;
        }

        // 书架书揭榜 = 先在 scene 免费封卷,封卷回包带回道具号后立刻揭榜。
        private sealed class SealThenFulfill
        {
            public ulong SealRequestId;
            public string OrderId;
        }

        private readonly struct RequestStamp
        {
            public readonly long Epoch, Id;
            public readonly ulong Player;
            public readonly object Connection;
            public RequestStamp(long epoch, long id, ulong player, object connection)
            { Epoch = epoch; Id = id; Player = player; Connection = connection; }
        }

        private static readonly System.Diagnostics.Stopwatch MonotonicClock = System.Diagnostics.Stopwatch.StartNew();

        private readonly IBattleTransport _net;
        private readonly IRpcChannel _rpc;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<bool> _writesBlocked;
        private Func<long> _nowMs = () => MonotonicClock.ElapsedMilliseconds;
        private object _connection;
        private ulong _player;
        private long _epoch, _serial, _activeRead, _activeWrite, _activeStallRead, _activeWantedRead, _activeMyWantedRead, _activeFollowUp;
        private bool _disposed;

        private SkillCraftingStateSnapshot _state;
        private readonly List<CraftProxyStallInfo> _proxyStalls = new();
        private readonly List<TradePb.WantedSkillBookOrderView> _wantedOrders = new();
        private readonly List<TradePb.WantedSkillBookOrderView> _myWantedOrders = new();
        private string _wantedOrdersNextCursor = string.Empty;
        private WantedFollowUp _followUp;
        private SealThenFulfill _sealThenFulfill;
        private PendingRequest _pending;
        private ulong _recoveryPlayer;
        private bool _recoveryBlocked;
        private int _autoRecoveryAttempts;
        private long _nextAutoRecoveryAtMs;
        private long? _lastWriteSendMs;

        public DurableRequestFileStore RecoveryStore { get; set; }
        public Func<long> NowMs
        {
            get => _nowMs;
            set => _nowMs = value ?? (() => MonotonicClock.ElapsedMilliseconds);
        }

        public SkillCraftingStateSnapshot State => _state?.Clone();
        public IReadOnlyList<CraftProxyStallInfo> ProxyStalls
        {
            get
            {
                var copy = new List<CraftProxyStallInfo>(_proxyStalls.Count);
                foreach (var stall in _proxyStalls) copy.Add(stall.Clone());
                return copy;
            }
        }
        /// <summary>公开悬赏榜(trade 全服,只列挂榜中的单,悬赏额从高到低)。</summary>
        public IReadOnlyList<TradePb.WantedSkillBookOrderView> WantedOrders => CloneOrders(_wantedOrders);
        /// <summary>本人还没结案的悬赏单(含托管中、交易中、交付中)。</summary>
        public IReadOnlyList<TradePb.WantedSkillBookOrderView> MyWantedOrders => CloneOrders(_myWantedOrders);
        public bool HasMoreWantedOrders => !string.IsNullOrEmpty(_wantedOrdersNextCursor);
        /// <summary>发榜或揭榜之后还在等服务端给出结论。</summary>
        public bool WantedFollowUpActive => _followUp != null || _sealThenFulfill != null;

        public bool IsConnected => !_disposed && _net.IsReady && _net.PlayerId != 0;
        public ulong SelfPlayerId => IsConnected ? _net.PlayerId : 0;
        public bool IsLoading => _activeRead != 0;
        public bool IsBusy => _activeWrite != 0;
        public bool ProxyStallsLoading => _activeStallRead != 0;
        public bool WantedOrdersLoading => _activeWantedRead != 0 || _activeMyWantedRead != 0;
        public bool HasPendingWrite => _pending != null || _recoveryBlocked;
        public bool HasUnreadProxyDigest => _state?.UnreadProxyDigest != null &&
            (_state.UnreadProxyDigest.OrdersCompleted > 0
             || _state.UnreadProxyDigest.NetGoldEarned > 0
             || _state.UnreadProxyDigest.NewlyUnlockedTitleMask != 0);
        public string PendingWriteLabel => _pending?.Label ?? string.Empty;
        public string Message { get; private set; } = NotInGameMessage;
        public string Error { get; private set; } = string.Empty;

        public event Action Changed;
        public event Action<CraftSkillBookResponse> BookCrafted;
        public event Action<LearnSkillBookResponse> BookLearned;
        public event Action<DismantleSkillBookResponse> BookDismantled;
        public event Action<SealSkillBookResponse> BookSealed;
        public event Action<UnsealSkillBookResponse> BookUnsealed;
        public event Action<ClaimScrollCollectionMilestoneResponse> ScrollMilestoneClaimed;
        public event Action<BatchDismantleSkillBooksResponse> BooksBatchDismantled;
        public event Action<TradePb.WantedSkillBookOrderView> WantedOrderPosted;
        public event Action<TradePb.CommerceOrderView> WantedOrderCanceled;
        /// <summary>这一次揭榜有了结论(成交、书不符已退回、书没扣到):背包与银两以服务端为准,订阅方重拉。</summary>
        public event Action WantedFulfillConcluded;
        public event Action<AckCraftProxyDigestResponse> ProxyDigestAcked;

        public SkillCraftClient(IBattleTransport transport, IRpcChannel rpc,
            Func<object> connectionIdentity = null, Func<bool> writesBlocked = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _connectionIdentity = connectionIdentity ?? (() => transport);
            _writesBlocked = writesBlocked;
            _connection = _connectionIdentity();
            _player = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        public static SkillCraftClient Attach(IBattleTransport transport, IRpcChannel rpc,
            Func<object> connectionIdentity = null, Func<bool> writesBlocked = null)
        {
            Instance?.Dispose();
            var client = new SkillCraftClient(transport, rpc, connectionIdentity, writesBlocked);
            Instance = client;
            return client;
        }

        public void RefreshState()
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            RestorePending(_net.PlayerId);
            if (_activeRead != 0 || _activeWrite != 0) return;

            var stamp = Capture();
            _activeRead = stamp.Id;
            Error = string.Empty;
            Message = LoadingMessage;
            Changed?.Invoke();

            SceneSkillClientPlayerRpc.GetSkillCraftingState(_rpc, new GetSkillCraftingStateRequest(),
                (status, response) =>
                {
                    if (!CurrentRead(stamp)) return;
                    _activeRead = 0;
                    if (!status.IsOk)
                    {
                        Fail("同步典籍制书信息失败：" + DescribeFailure(status));
                        return;
                    }
                    if (response == null)
                    {
                        Fail("服务器未返回典籍制书状态，请刷新");
                        return;
                    }
                    if (HasTip(response.ErrorMessage))
                    {
                        Fail(SkillBookMapper.DescribeTip("同步典籍制书信息失败", response.ErrorMessage));
                        return;
                    }
                    if (response.State == null)
                    {
                        Fail("服务器未返回有效的典籍制书快照，请刷新");
                        return;
                    }
                    _state = response.State.Clone();
                    Error = string.Empty;
                    Message = HasPendingWrite ? PendingWriteText : string.Empty;
                    Changed?.Invoke();
                });
        }

        public void RefreshProxyStalls()
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            if (_activeStallRead != 0) return;

            var stamp = Capture();
            _activeStallRead = stamp.Id;
            Changed?.Invoke();

            SceneSkillClientPlayerRpc.ListCraftProxyStalls(_rpc, new ListCraftProxyStallsRequest(),
                (status, response) =>
                {
                    if (!CurrentStallRead(stamp)) return;
                    _activeStallRead = 0;
                    if (!status.IsOk)
                    {
                        Fail("读取名匠代打摊位失败：" + DescribeFailure(status));
                        return;
                    }
                    if (response == null)
                    {
                        Fail("服务器未返回名匠摊位列表，请刷新");
                        return;
                    }
                    if (HasTip(response.ErrorMessage))
                    {
                        Fail(SkillBookMapper.DescribeTip("读取名匠代打摊位失败", response.ErrorMessage));
                        return;
                    }
                    _proxyStalls.Clear();
                    foreach (var stall in response.Stalls)
                        if (stall != null && stall.MasterPlayerId != 0) _proxyStalls.Add(stall.Clone());
                    Error = string.Empty;
                    Changed?.Invoke();
                });
        }

        public void CraftRegularRandom(uint scrollId, ulong proxyMasterPlayerId = 0)
            => SubmitCraft(CraftSkillBookRequest.Types.CraftMode.RandomRegular, scrollId, 0, proxyMasterPlayerId);

        public void CraftRegularTargeted(uint scrollId, uint targetSkillTableId, ulong proxyMasterPlayerId = 0)
            => SubmitCraft(CraftSkillBookRequest.Types.CraftMode.TargetedRegular, scrollId, targetSkillTableId, proxyMasterPlayerId);

        public void CraftRareTargeted(uint scrollId, uint targetSkillTableId, ulong proxyMasterPlayerId = 0)
            => SubmitCraft(CraftSkillBookRequest.Types.CraftMode.TargetedRare, scrollId, targetSkillTableId, proxyMasterPlayerId);

        public void LearnBook(ulong bookId, ulong petId = 0)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (bookId == 0) { Fail("请选择有效的未学技能书"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new LearnSkillBookRequest
            {
                BookId = bookId,
                PetId = petId,
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.Learn, Learn = request });
        }

        public void DismantleBook(ulong bookId)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (bookId == 0) { Fail("请选择有效的常规技能书"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new DismantleSkillBookRequest
            {
                BookId = bookId,
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.Dismantle, Dismantle = request });
        }

        public void SealBook(ulong bookId) => SubmitSeal(bookId, null);

        private void SubmitSeal(ulong bookId, string fulfillOrderId)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (bookId == 0) { Fail("请选择有效的可交易常规技能书"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new SealSkillBookRequest
            {
                BookId = bookId,
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
            };
            // 先记下「封卷后揭榜」再发送:回包可能在发送调用里同步回来。
            _sealThenFulfill = fulfillOrderId == null ? null : new SealThenFulfill { SealRequestId = key, OrderId = fulfillOrderId };
            if (!StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.Seal, Seal = request }))
                _sealThenFulfill = null;
        }

        public void UnsealBook(ulong itemId)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (itemId == 0) { Fail("请选择有效的封卷道籍"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new UnsealSkillBookRequest
            {
                ItemId = itemId,
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.Unseal, Unseal = request });
        }

        public void SetProxyStall(bool open, ulong forgeFeeGold, ulong scrollFeeGold,
            uint masterShowcaseTag = 0, uint equippedCraftTitleId = 0, bool teamFreeProxy = false)
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            if (_writesBlocked?.Invoke() == true) { Notice(BattleBlockedText); return; }
            if (_activeWrite != 0 || _activeRead != 0) return;

            var stamp = Capture();
            _activeWrite = stamp.Id;
            Error = string.Empty;
            Message = open ? "正在更新名匠代打摊位…" : "正在保存名匠设置…";
            Changed?.Invoke();

            var request = new SetCraftProxyStallRequest
            {
                Open = open,
                ProxyForgeFeeGold = open ? forgeFeeGold : 0,
                ProxyScrollFeeGold = open ? scrollFeeGold : 0,
                MasterShowcaseTag = masterShowcaseTag,
                EquippedCraftTitleId = equippedCraftTitleId,
                TeamFreeProxy = teamFreeProxy,
            };
            SceneSkillClientPlayerRpc.SetCraftProxyStall(_rpc, request,
                (status, response) =>
                {
                    if (!CurrentWrite(stamp)) return;
                    _activeWrite = 0;
                    if (!status.IsOk)
                    {
                        Fail("更新名匠摊位失败：" + DescribeFailure(status));
                        return;
                    }
                    if (response == null)
                    {
                        Fail("服务器未返回摊位设置结果，请刷新");
                        return;
                    }
                    if (response.State != null) _state = response.State.Clone();
                    if (HasTip(response.ErrorMessage))
                    {
                        Fail(SkillBookMapper.DescribeTip("设置代打摊位失败", response.ErrorMessage));
                        return;
                    }
                    Error = string.Empty;
                    Message = open
                        ? "名匠代打摊位已开启（支持跨线/离线24小时接单，95% 净银两自动经邮件入账）"
                        : "名匠代打摊位已关闭（雅号与情义设置已保存）";
                    Changed?.Invoke();
                });
        }

        public void AckProxyDigest()
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            if (_writesBlocked?.Invoke() == true) { Notice(BattleBlockedText); return; }
            if (_activeWrite != 0 || _activeRead != 0) return;

            var stamp = Capture();
            _activeWrite = stamp.Id;
            Error = string.Empty;
            Message = "正在确认离线营业战报…";
            Changed?.Invoke();

            SceneSkillClientPlayerRpc.AckCraftProxyDigest(_rpc, new AckCraftProxyDigestRequest(),
                (status, response) =>
                {
                    if (!CurrentWrite(stamp)) return;
                    _activeWrite = 0;
                    if (!status.IsOk)
                    {
                        Fail("确认营业战报失败：" + DescribeFailure(status));
                        return;
                    }
                    if (response == null)
                    {
                        Fail("服务器未返回营业战报确认结果，请刷新");
                        return;
                    }
                    if (response.State != null) _state = response.State.Clone();
                    if (HasTip(response.ErrorMessage))
                    {
                        Fail(SkillBookMapper.DescribeTip("确认营业战报失败", response.ErrorMessage));
                        return;
                    }
                    Error = string.Empty;
                    Message = "已收下名匠营业战报，净银两收益可在邮箱领取";
                    Changed?.Invoke();
                    if (Current(stamp)) ProxyDigestAcked?.Invoke(response.Clone());
                });
        }

        public void ClaimScrollMilestone(uint scrollId, uint milestoneTier)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (scrollId == 0 || (milestoneTier != 1 && milestoneTier != 2))
            { Fail("请选择有效的卷宗里程碑"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new ClaimScrollCollectionMilestoneRequest
            {
                ScrollId = scrollId,
                MilestoneTier = milestoneTier,
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.ClaimMilestone, ClaimMilestone = request });
        }

        public void BatchDismantleBooks(IEnumerable<ulong> bookIds)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            var ids = new List<ulong>();
            if (bookIds != null)
            {
                var seen = new HashSet<ulong>();
                foreach (ulong id in bookIds)
                    if (id != 0 && seen.Add(id)) ids.Add(id);
            }
            if (ids.Count == 0 || ids.Count > 20)
            { Fail("请选择 1 至 20 本可拆常规技能书"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new BatchDismantleSkillBooksRequest
            {
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
            };
            request.BookIds.AddRange(ids);
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.BatchDismantle, BatchDismantle = request });
        }

        /// <summary>刷新悬赏榜:公开榜从第一页取(nextPage = true 按游标续取下一页);取第一页时一并刷新本人的悬赏单。</summary>
        public void RefreshWantedOrders(bool nextPage = false)
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            if (!nextPage) RefreshMyWantedOrders();
            if (_activeWantedRead != 0 || (nextPage && !HasMoreWantedOrders)) return;

            var stamp = Capture();
            _activeWantedRead = stamp.Id;
            Changed?.Invoke();
            // 技能筛选是服务端页内过滤、一页可能不满;这里不筛,按悬赏额从高到低整页取,空游标表示没有下一页。
            var request = new TradePb.ListWantedSkillBookOrdersRequest
            {
                Cursor = nextPage ? _wantedOrdersNextCursor : string.Empty,
                Limit = WantedListPageSize,
            };
            ClientPlayerJubaozhaiRpc.ListWantedSkillBookOrders(_rpc, request, (status, response) =>
            {
                if (!CurrentWantedRead(stamp)) return;
                _activeWantedRead = 0;
                if (!ReadWantedList("读取悬赏求书榜失败", status, response)) return;
                if (!nextPage) _wantedOrders.Clear();
                AppendOrders(_wantedOrders, response.Orders);
                _wantedOrdersNextCursor = response.NextCursor ?? string.Empty;
                Error = string.Empty;
                Changed?.Invoke();
            });
        }

        public void RefreshMyWantedOrders()
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            if (_activeMyWantedRead != 0) return;

            var stamp = Capture();
            _activeMyWantedRead = stamp.Id;
            Changed?.Invoke();
            // 每人同时至多 3 张没结案的单,一页取完。
            var request = new TradePb.ListWantedSkillBookOrdersRequest { OnlyMine = true, Limit = WantedListPageSize };
            ClientPlayerJubaozhaiRpc.ListWantedSkillBookOrders(_rpc, request, (status, response) =>
            {
                if (!Current(stamp) || stamp.Id != _activeMyWantedRead) return;
                _activeMyWantedRead = 0;
                if (!ReadWantedList("读取我的悬赏失败", status, response)) return;
                _myWantedOrders.Clear();
                AppendOrders(_myWantedOrders, response.Orders);
                Error = string.Empty;
                Changed?.Invoke();
            });
        }

        /// <summary>发榜:trade 建单后从流通银两里托管悬赏;回包通常是「托管中」,之后按同一请求号跟到挂榜或撤单结案。</summary>
        public void PostWantedOrder(uint skillTableId, ulong rewardGold, bool requireMasterSignature = false)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (skillTableId == 0) { Fail("请选择要求购的常规技能"); return; }
            if (rewardGold < 100 || rewardGold > 50000) { Fail("悬赏银两须在 100 至 50000 之间"); return; }

            var request = new TradePb.CreateWantedSkillBookOrderRequest
            {
                ClientRequestId = NewTradeRequestId(),
                SkillTableId = skillTableId,
                RequireMasterSignature = requireMasterSignature,
                RewardGold = rewardGold,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.PostWanted, PostWanted = request });
        }

        /// <summary>撤单:只有发榜人、只在托管中与挂榜中可撤(有人交书时回「交易中」),托管的悬赏银两全额退回。</summary>
        public void CancelWantedOrder(string orderId)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (string.IsNullOrEmpty(orderId)) { Fail("请选择要撤销的悬赏求书单"); return; }
            var request = new TradePb.CancelCommerceOrderRequest { OrderId = orderId };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.CancelWanted, CancelWanted = request });
        }

        public void FulfillWantedOrderWithSealedItem(string orderId, ulong sealedItemId)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (string.IsNullOrEmpty(orderId) || sealedItemId == 0) { Fail("揭榜交书参数无效"); return; }
            StartFulfillWanted(playerId, orderId, sealedItemId);
        }

        /// <summary>书架书揭榜:先免费封卷,拿到道具号立刻揭榜(界面上一键)。揭榜没成交时书以封卷道籍留在背包,可再开卷,不丢。</summary>
        public void FulfillWantedOrderWithBookshelfBook(string orderId, ulong bookshelfBookId)
        {
            if (string.IsNullOrEmpty(orderId) || bookshelfBookId == 0) { Fail("揭榜交书参数无效"); return; }
            SubmitSeal(bookshelfBookId, orderId);
        }

        private void StartFulfillWanted(ulong playerId, string orderId, ulong sealedItemId)
        {
            var request = new TradePb.FulfillWantedSkillBookOrderRequest
            {
                OrderId = orderId,
                ClientRequestId = NewTradeRequestId(),
                SealedItemId = sealedItemId,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.FulfillWanted, FulfillWanted = request });
        }

        // trade 的请求号 8..80 字符;与交易行(CommerceClient)同一写法。
        private static string NewTradeRequestId() => Guid.NewGuid().ToString("N");

        public void RetryPendingWrite()
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            RestorePending(_net.PlayerId);
            if (_pending == null || _recoveryBlocked || _activeWrite != 0) return;
            if (_writesBlocked?.Invoke() == true) { Notice(BattleBlockedText); return; }
            if (!CanSendWrite()) { Notice("操作太快，请稍候重试"); return; }
            SendPending(_net.PlayerId);
        }

        public void TickRecovery()
        {
            if (_disposed || !IsConnected) return;
            ObserveSession();
            TickWantedFollowUp();
            RestorePending(_net.PlayerId);
            if (_pending == null || _recoveryBlocked || _activeWrite != 0) return;
            if (_autoRecoveryAttempts >= MaxAutoRecoveryAttempts) return;
            if (_writesBlocked?.Invoke() == true) return;
            if (_nowMs() < _nextAutoRecoveryAtMs || !CanSendWrite()) return;
            ++_autoRecoveryAttempts;
            SendPending(_net.PlayerId);
        }

        private void SubmitCraft(CraftSkillBookRequest.Types.CraftMode mode, uint scrollId,
            uint targetSkillTableId, ulong proxyMasterPlayerId)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (scrollId == 0) { Fail("请选择有效的典籍卷宗"); return; }
            if (mode == CraftSkillBookRequest.Types.CraftMode.RandomRegular && targetSkillTableId != 0)
            { Fail("随机拓印不需要指定目标技能"); return; }
            if (mode != CraftSkillBookRequest.Types.CraftMode.RandomRegular && targetSkillTableId == 0)
            { Fail("请先选择要定向拓印的目标技能"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new CraftSkillBookRequest
            {
                Mode = mode,
                ScrollId = scrollId,
                TargetSkillTableId = targetSkillTableId,
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
                ProxyMasterPlayerId = proxyMasterPlayerId,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.Craft, Craft = request });
        }

        private bool PrepareWrite(out ulong playerId)
        {
            playerId = 0;
            if (_disposed) return false;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return false; }
            playerId = _net.PlayerId;
            RestorePending(playerId);
            if (HasPendingWrite) { Fail(PendingWriteText); return false; }
            if (_writesBlocked?.Invoke() == true) { Notice(BattleBlockedText); return false; }
            if (_activeWrite != 0 || _activeRead != 0) return false;
            if (_state == null) { Fail("请先同步典籍制书状态后再操作"); return false; }
            if (!CanSendWrite()) { Notice("操作太快，请稍候重试"); return false; }
            return true;
        }

        private bool StartIdempotentWrite(ulong playerId, PendingRequest pending)
        {
            string encoded = EncodePending(pending);
            if (RecoveryStore != null)
            {
                try { RecoveryStore.Save(playerId, encoded); }
                catch (Exception)
                {
                    _recoveryPlayer = 0;
                    RestorePending(playerId);
                    Fail("无法保存本机制书恢复记录，本次尚未提交");
                    return false;
                }
            }
            pending.EncodedRecord = encoded;
            _pending = pending;
            _autoRecoveryAttempts = 0;
            _nextAutoRecoveryAtMs = 0;
            SendPending(playerId);
            return true;
        }

        private void SendPending(ulong playerId)
        {
            var pending = _pending;
            if (pending == null) return;
            var stamp = Capture();
            _activeWrite = stamp.Id;
            _activeRead = 0;
            _lastWriteSendMs = _nowMs();
            Error = string.Empty;
            Message = $"正在处理{pending.Label}…";
            Changed?.Invoke();

            switch (pending.Kind)
            {
                case PendingKind.Craft:
                    SceneSkillClientPlayerRpc.CraftSkillBook(_rpc, pending.Craft.Clone(),
                        (status, response) => HandleCraftReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.Learn:
                    SceneSkillClientPlayerRpc.LearnSkillBook(_rpc, pending.Learn.Clone(),
                        (status, response) => HandleLearnReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.Dismantle:
                    SceneSkillClientPlayerRpc.DismantleSkillBook(_rpc, pending.Dismantle.Clone(),
                        (status, response) => HandleDismantleReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.Seal:
                    SceneSkillClientPlayerRpc.SealSkillBook(_rpc, pending.Seal.Clone(),
                        (status, response) => HandleSealReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.Unseal:
                    SceneSkillClientPlayerRpc.UnsealSkillBook(_rpc, pending.Unseal.Clone(),
                        (status, response) => HandleUnsealReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.ClaimMilestone:
                    SceneSkillClientPlayerRpc.ClaimScrollCollectionMilestone(_rpc, pending.ClaimMilestone.Clone(),
                        (status, response) => HandleClaimMilestoneReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.BatchDismantle:
                    SceneSkillClientPlayerRpc.BatchDismantleSkillBooks(_rpc, pending.BatchDismantle.Clone(),
                        (status, response) => HandleBatchDismantleReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.PostWanted:
                    ClientPlayerJubaozhaiRpc.CreateWantedSkillBookOrder(_rpc, pending.PostWanted.Clone(),
                        (status, response) => HandlePostWantedReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.CancelWanted:
                    ClientPlayerJubaozhaiRpc.CancelCommerceOrder(_rpc, pending.CancelWanted.Clone(),
                        (status, response) => HandleCancelWantedReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.FulfillWanted:
                    ClientPlayerJubaozhaiRpc.FulfillWantedSkillBookOrder(_rpc, pending.FulfillWanted.Clone(),
                        (status, response) => HandleFulfillWantedReply(stamp, playerId, pending, status, response));
                    break;
            }
        }

        private void HandleCraftReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, CraftSkillBookResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip("制书未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            string bonus = response.BonusFragmentsGranted > 0
                ? $"，巧匠/宗师额外返还 {response.BonusFragmentsGranted} 张悟道残页" : string.Empty;
            Message = response.Replayed ? $"已恢复上次制书结果{bonus}" : $"技能书制成成功{bonus}";
            Changed?.Invoke();
            if (Current(stamp)) BookCrafted?.Invoke(response.Clone());
        }

        private void HandleLearnReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, LearnSkillBookResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip("研习未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            Message = response.Replayed ? "已恢复上次研习结果，技能已永久解锁" : "技能书研习成功，已永久习得";
            Changed?.Invoke();
            if (Current(stamp)) BookLearned?.Invoke(response.Clone());
        }

        private void HandleDismantleReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, DismantleSkillBookResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip("拆页未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            Message = $"拆页完成，返还 {response.RefundedFragments} 张通用悟道残页";
            Changed?.Invoke();
            if (Current(stamp)) BookDismantled?.Invoke(response.Clone());
        }

        private void HandleSealReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, SealSkillBookResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            var chained = TakeSealThenFulfill(pending.Seal.ClientRequestId);
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip(chained != null ? "封卷未成功，未揭榜" : "封卷未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            Message = response.Replayed ? "已恢复上次封卷结果，封卷道籍已存入背包" : "封卷完成，已存入背包（可挂集市或面对面交易）";
            Changed?.Invoke();
            if (Current(stamp)) BookSealed?.Invoke(response.Clone());
            if (chained != null && response.ItemId != 0 && Current(stamp))
                StartFulfillWanted(playerId, chained.OrderId, response.ItemId);
        }

        private SealThenFulfill TakeSealThenFulfill(ulong sealRequestId)
        {
            var chained = _sealThenFulfill;
            if (chained == null || chained.SealRequestId != sealRequestId) return null;
            _sealThenFulfill = null;
            return chained;
        }

        private void HandleUnsealReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, UnsealSkillBookResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip("开卷未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            Message = response.Replayed ? "已恢复上次开卷结果，技能书已回到书架" : "开卷完成，技能书已放入未学书架";
            Changed?.Invoke();
            if (Current(stamp)) BookUnsealed?.Invoke(response.Clone());
        }

        private void HandleClaimMilestoneReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, ClaimScrollCollectionMilestoneResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip("领取里程碑未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            Message = response.Replayed
                ? $"已恢复里程碑领奖，获得 {response.RewardedFragments} 张绑定悟道残页"
                : $"藏经阁里程碑已领取，获得 {response.RewardedFragments} 张绑定悟道残页";
            Changed?.Invoke();
            if (Current(stamp)) ScrollMilestoneClaimed?.Invoke(response.Clone());
        }

        private void HandleBatchDismantleReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, BatchDismantleSkillBooksResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip("批量拆页未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            Message = $"批量拆解 {response.DismantledCount} 本常规书完成，共返还 {response.RefundedFragments} 张绑定悟道残页";
            Changed?.Invoke();
            if (Current(stamp)) BooksBatchDismantled?.Invoke(response.Clone());
        }

        // trade 回包与交易行(CommerceClient)同口径:故障码、缺少订单的空成功都证明不了没提交,保留原请求按同一请求号重发。
        private static bool TradeReplyUncertain(uint tip, bool hasOrder) => TipFaults.IsFault(tip) || (tip == 0 && !hasOrder);

        private void HandlePostWantedReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, TradePb.WantedSkillBookOrderResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            uint tip = response?.ErrorMessage?.Id ?? 0;
            if (response == null || TradeReplyUncertain(tip, response.Order != null))
            { DeferPending($"{pending.Label}结果尚未确认，原请求已保留"); return; }
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            ObservePost(pending.PostWanted, tip, response.Order);
            if (tip == 0 && Current(stamp)) WantedOrderPosted?.Invoke(response.Order.Clone());
        }

        private void HandleCancelWantedReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, TradePb.CommerceOrderResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            uint tip = response?.ErrorMessage?.Id ?? 0;
            if (response == null || TradeReplyUncertain(tip, response.Order != null)
                || (response.Order != null && response.Order.OrderId != pending.CancelWanted.OrderId))
            { DeferPending($"{pending.Label}结果尚未确认，原请求已保留"); return; }
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            RefreshMyWantedOrders();
            if (tip != 0)
            {
                Notice("撤单未成功：" + DescribeWantedTip(tip));
                return;
            }
            Notice(response.Order.State == TradePb.CommerceOrderState.Cancelled
                ? "已撤销求书悬赏，托管的悬赏银两已全额退回"
                : "撤单已受理，托管的悬赏银两随后全额退回");
            if (Current(stamp)) WantedOrderCanceled?.Invoke(response.Order.Clone());
        }

        private void HandleFulfillWantedReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, TradePb.WantedSkillBookOrderResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            uint tip = response?.ErrorMessage?.Id ?? 0;
            if (response == null || TradeReplyUncertain(tip, response.Order != null))
            { DeferPending($"{pending.Label}结果尚未确认，原请求已保留"); return; }
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            ObserveFulfill(pending.FulfillWanted, tip, response.Order);
        }

        // 发榜回包与按同一请求号重发的查询回包同一套判读。
        private void ObservePost(TradePb.CreateWantedSkillBookOrderRequest request, uint tip, TradePb.WantedSkillBookOrderView order)
        {
            if (tip != 0)
            {
                _followUp = null;
                Notice("发布悬赏未成功：" + DescribeWantedTip(tip));
                return;
            }
            if (order.Phase == TradePb.WantedSkillBookOrderPhase.Escrowing)
            {
                Notice(ScheduleFollowUp(request, null)
                    ? $"悬赏已提交，正在托管 {order.RewardGold} 银两…"
                    : "悬赏银两仍在托管中，请稍后刷新我的悬赏查看");
                return;
            }
            _followUp = null;
            Notice(order.Phase switch
            {
                TradePb.WantedSkillBookOrderPhase.Cancelled => "这张悬赏单已撤销：悬赏银两没能托管（流通银两不足），或已撤单退回",
                TradePb.WantedSkillBookOrderPhase.Manual => "这张悬赏单转人工核对，请保留记录",
                _ => $"悬赏已挂榜，全服可见（托管 {order.RewardGold} 银两；挂榜中随时可撤单，全额退回）",
            });
            RefreshMyWantedOrders();
        }

        // 揭榜回包与按同一请求号重发的查询回包同一套判读:交易中、交付中回订单;书没扣到、书不符已退回回 tip。
        private void ObserveFulfill(TradePb.FulfillWantedSkillBookOrderRequest request, uint tip, TradePb.WantedSkillBookOrderView order)
        {
            if (tip == 0 && (order.Phase == TradePb.WantedSkillBookOrderPhase.Fulfilling
                             || order.Phase == TradePb.WantedSkillBookOrderPhase.Delivering))
            {
                if (ScheduleFollowUp(null, request))
                {
                    Notice(order.Phase == TradePb.WantedSkillBookOrderPhase.Delivering
                        ? $"书正交给发榜人（对方背包满时等对方腾出一格），之后到账 {order.SellerNetGold} 银两"
                        : "交书中：正在核对封卷道籍…");
                    return;
                }
                Notice($"交书仍在进行（成交后到账 {order.SellerNetGold} 银两），请稍后刷新背包查看");
            }
            else
            {
                _followUp = null;
                if (tip != 0) Notice("揭榜未成交：" + DescribeWantedTip(tip));
                else if (order.Phase == TradePb.WantedSkillBookOrderPhase.Settled)
                    Notice($"揭榜成交，扣 5% 集市税后实得 {order.SellerNetGold} 银两");
                else Notice("揭榜未成交：这张悬赏单状态已变化，请刷新悬赏榜");
            }
            RefreshWantedOrders();
            WantedFulfillConcluded?.Invoke();
        }

        // 继续跟同一请求;跟满次数返回 false 并停。换了请求就从头数。
        private bool ScheduleFollowUp(TradePb.CreateWantedSkillBookOrderRequest post, TradePb.FulfillWantedSkillBookOrderRequest fulfill)
        {
            bool same = _followUp != null && (post != null
                ? _followUp.Post?.ClientRequestId == post.ClientRequestId
                : _followUp.Fulfill?.ClientRequestId == fulfill.ClientRequestId);
            if (!same) _followUp = new WantedFollowUp { Post = post?.Clone(), Fulfill = fulfill?.Clone() };
            if (++_followUp.Polls > MaxWantedFollowUpPolls)
            {
                _followUp = null;
                return false;
            }
            _followUp.NextPollAtMs = _nowMs() + WantedFollowUpIntervalMs;
            return true;
        }

        private void TickWantedFollowUp()
        {
            var followUp = _followUp;
            if (followUp == null || _activeFollowUp != 0 || _nowMs() < followUp.NextPollAtMs) return;
            var stamp = Capture();
            _activeFollowUp = stamp.Id;
            if (followUp.Post != null)
            {
                ClientPlayerJubaozhaiRpc.CreateWantedSkillBookOrder(_rpc, followUp.Post.Clone(), (status, response) =>
                {
                    if (!CurrentFollowUp(stamp, followUp)) return;
                    _activeFollowUp = 0;
                    uint tip = response?.ErrorMessage?.Id ?? 0;
                    if (!status.IsOk || response == null || TradeReplyUncertain(tip, response.Order != null))
                    { RetryFollowUp(followUp); return; }
                    ObservePost(followUp.Post, tip, response.Order);
                });
                return;
            }
            ClientPlayerJubaozhaiRpc.FulfillWantedSkillBookOrder(_rpc, followUp.Fulfill.Clone(), (status, response) =>
            {
                if (!CurrentFollowUp(stamp, followUp)) return;
                _activeFollowUp = 0;
                uint tip = response?.ErrorMessage?.Id ?? 0;
                if (!status.IsOk || response == null || TradeReplyUncertain(tip, response.Order != null))
                { RetryFollowUp(followUp); return; }
                ObserveFulfill(followUp.Fulfill, tip, response.Order);
            });
        }

        // 查询本身没结论(断线、故障码)只算一次跟进,满次数就停,不改原结论。
        private void RetryFollowUp(WantedFollowUp followUp)
        {
            if (++followUp.Polls > MaxWantedFollowUpPolls)
            {
                _followUp = null;
                Notice("暂时查不到结果，服务端会照常办完，请稍后刷新悬赏榜与背包查看");
                return;
            }
            followUp.NextPollAtMs = _nowMs() + WantedFollowUpIntervalMs;
        }

        private bool CurrentFollowUp(RequestStamp stamp, WantedFollowUp followUp)
            => Current(stamp) && stamp.Id == _activeFollowUp && ReferenceEquals(followUp, _followUp);

        private bool ReadWantedList(string what, RpcStatus status, TradePb.ListWantedSkillBookOrdersResponse response)
        {
            if (!status.IsOk) { Fail($"{what}：" + DescribeFailure(status)); return false; }
            if (response == null) { Fail($"{what}：服务器未返回列表，请刷新"); return false; }
            if (HasTip(response.ErrorMessage)) { Fail($"{what}：" + DescribeWantedTip(response.ErrorMessage.Id)); return false; }
            return true;
        }

        private static void AppendOrders(List<TradePb.WantedSkillBookOrderView> target,
            IEnumerable<TradePb.WantedSkillBookOrderView> orders)
        {
            foreach (var order in orders)
                if (order != null && !string.IsNullOrEmpty(order.OrderId)) target.Add(order.Clone());
        }

        private static IReadOnlyList<TradePb.WantedSkillBookOrderView> CloneOrders(List<TradePb.WantedSkillBookOrderView> source)
        {
            var copy = new List<TradePb.WantedSkillBookOrderView>(source.Count);
            foreach (var order in source) copy.Add(order.Clone());
            return copy;
        }

        private static string DescribeWantedTip(uint tip) => tip switch
        {
            (uint)trade_error.KTradeWantedBookMismatch => "交上的书不符合这张悬赏单的要求，已原样退回背包",
            (uint)trade_error.KTradeWantedOrderLimitReached => "同时最多挂 3 张悬赏求书单，请先撤掉一张再发榜",
            (uint)trade_error.KTradeAssetNotTradable => "封卷道籍没能扣下（已绑定、正被战斗占用或已不在背包），书仍在背包",
            (uint)trade_error.KTradeOrderStateConflict => "有人正在交书，或你对这张单已试满 3 次，请稍后刷新再试",
            _ => JubaozhaiClient.DescribeTip(tip),
        };

        private void HandleTransportFailure(ulong playerId, PendingRequest pending, RpcStatus status)
        {
            // 明确未发到服务端（断线/未就绪/网关明确信封拒绝）才安全清除原请求；超时与未知故障必须原样保留幂等键。
            if (status.Code == RpcStatusCode.NotSent || status.Code == RpcStatusCode.ServerTip)
            {
                if (!ClearPending(playerId, pending.EncodedRecord)) return;
                if (pending.Kind == PendingKind.Seal) TakeSealThenFulfill(pending.Seal.ClientRequestId);
                string prefix = status.Code == RpcStatusCode.NotSent ? $"{pending.Label}未发送：" : $"{pending.Label}被拒绝：";
                Fail(prefix + DescribeFailure(status));
                return;
            }
            DeferPending($"{pending.Label}结果尚未确认（{DescribeFailure(status)}），原请求已保留，请重试原请求");
        }

        private void DeferPending(string error)
        {
            _nextAutoRecoveryAtMs = _nowMs() + 2000 * (1L << Math.Min(_autoRecoveryAttempts, 2));
            Fail(error);
        }

        private bool ClearPending(ulong playerId, string record)
        {
            if (RecoveryStore != null && !string.IsNullOrEmpty(record))
            {
                try { RecoveryStore.Clear(playerId, record); }
                catch (Exception)
                {
                    DeferPending("操作回执已收到，本机恢复记录尚未清理，请重试原请求");
                    return false;
                }
            }
            _pending = null;
            _nextAutoRecoveryAtMs = 0;
            return true;
        }

        private void RestorePending(ulong playerId)
        {
            if (_recoveryPlayer == playerId || RecoveryStore == null || playerId == 0) return;
            _recoveryPlayer = playerId;
            try
            {
                string record = RecoveryStore.Load(playerId);
                if (string.IsNullOrEmpty(record)) return;
                var parsed = DecodePending(record);
                if (parsed == null || !parsed.HasIdentity) throw new InvalidOperationException();
                parsed.EncodedRecord = record;
                _pending = parsed;
                Error = PendingWriteText;
            }
            catch (Exception)
            {
                _recoveryBlocked = true;
                Error = "制书恢复记录损坏或无法读取，请保留记录并联系支持";
            }
        }

        private static string EncodePending(PendingRequest pending) => pending.Kind switch
        {
            PendingKind.Craft => "craft:" + Convert.ToBase64String(pending.Craft.ToByteArray()),
            PendingKind.Learn => "learn:" + Convert.ToBase64String(pending.Learn.ToByteArray()),
            PendingKind.Dismantle => "dismantle:" + Convert.ToBase64String(pending.Dismantle.ToByteArray()),
            PendingKind.Seal => "seal:" + Convert.ToBase64String(pending.Seal.ToByteArray()),
            PendingKind.Unseal => "unseal:" + Convert.ToBase64String(pending.Unseal.ToByteArray()),
            PendingKind.ClaimMilestone => "milestone:" + Convert.ToBase64String(pending.ClaimMilestone.ToByteArray()),
            PendingKind.BatchDismantle => "batchdismantle:" + Convert.ToBase64String(pending.BatchDismantle.ToByteArray()),
            // 悬赏三种 2026-11-27 改走 trade,换了前缀:旧 scene 格式的记录读不出,按损坏处理(上线前清档,不读旧格式)。
            PendingKind.PostWanted => "tradewantedpost:" + Convert.ToBase64String(pending.PostWanted.ToByteArray()),
            PendingKind.CancelWanted => "tradewantedcancel:" + Convert.ToBase64String(pending.CancelWanted.ToByteArray()),
            PendingKind.FulfillWanted => "tradewantedfulfill:" + Convert.ToBase64String(pending.FulfillWanted.ToByteArray()),
            _ => string.Empty,
        };

        private static PendingRequest DecodePending(string record)
        {
            int sep = record.IndexOf(':');
            if (sep <= 0 || sep + 1 >= record.Length) return null;
            string kind = record.Substring(0, sep);
            byte[] bytes = Convert.FromBase64String(record.Substring(sep + 1));
            return kind switch
            {
                "craft" => new PendingRequest { Kind = PendingKind.Craft, Craft = CraftSkillBookRequest.Parser.ParseFrom(bytes) },
                "learn" => new PendingRequest { Kind = PendingKind.Learn, Learn = LearnSkillBookRequest.Parser.ParseFrom(bytes) },
                "dismantle" => new PendingRequest { Kind = PendingKind.Dismantle, Dismantle = DismantleSkillBookRequest.Parser.ParseFrom(bytes) },
                "seal" => new PendingRequest { Kind = PendingKind.Seal, Seal = SealSkillBookRequest.Parser.ParseFrom(bytes) },
                "unseal" => new PendingRequest { Kind = PendingKind.Unseal, Unseal = UnsealSkillBookRequest.Parser.ParseFrom(bytes) },
                "milestone" => new PendingRequest { Kind = PendingKind.ClaimMilestone, ClaimMilestone = ClaimScrollCollectionMilestoneRequest.Parser.ParseFrom(bytes) },
                "batchdismantle" => new PendingRequest { Kind = PendingKind.BatchDismantle, BatchDismantle = BatchDismantleSkillBooksRequest.Parser.ParseFrom(bytes) },
                "tradewantedpost" => new PendingRequest { Kind = PendingKind.PostWanted, PostWanted = TradePb.CreateWantedSkillBookOrderRequest.Parser.ParseFrom(bytes) },
                "tradewantedcancel" => new PendingRequest { Kind = PendingKind.CancelWanted, CancelWanted = TradePb.CancelCommerceOrderRequest.Parser.ParseFrom(bytes) },
                "tradewantedfulfill" => new PendingRequest { Kind = PendingKind.FulfillWanted, FulfillWanted = TradePb.FulfillWantedSkillBookOrderRequest.Parser.ParseFrom(bytes) },
                _ => null,
            };
        }

        private bool CanSendWrite() => !_lastWriteSendMs.HasValue || _nowMs() < _lastWriteSendMs.Value
            || _nowMs() - _lastWriteSendMs.Value >= MinWriteIntervalMs;

        private void Notice(string text)
        {
            Error = string.Empty;
            Message = text ?? string.Empty;
            Changed?.Invoke();
        }

        private void Fail(string error)
        {
            Error = string.IsNullOrWhiteSpace(error) ? "典籍制书请求失败，请重试" : error;
            Changed?.Invoke();
        }

        private void ObserveSession()
        {
            var connection = _connectionIdentity();
            ulong player = _net.PlayerId;
            if (ReferenceEquals(connection, _connection) && player == _player) return;
            _connection = connection;
            _player = player;
            ResetSession(NotInGameMessage);
        }

        private void HandleDisconnected()
        {
            if (_disposed) return;
            _connection = _connectionIdentity();
            _player = _net.PlayerId;
            ResetSession(DisconnectedMessage);
        }

        private void ResetSession(string message)
        {
            ++_epoch;
            _activeRead = _activeWrite = _activeStallRead = _activeWantedRead = _activeMyWantedRead = _activeFollowUp = 0;
            _state = null;
            _proxyStalls.Clear();
            ClearWantedState();
            _pending = null;
            _recoveryPlayer = 0;
            _recoveryBlocked = false;
            _autoRecoveryAttempts = 0;
            _nextAutoRecoveryAtMs = 0;
            Error = string.Empty;
            Message = message;
            Changed?.Invoke();
        }

        private void ClearWantedState()
        {
            _wantedOrders.Clear();
            _myWantedOrders.Clear();
            _wantedOrdersNextCursor = string.Empty;
            _followUp = null;
            _sealThenFulfill = null;
        }

        private RequestStamp Capture() => new(_epoch, ++_serial, _net.PlayerId, _connectionIdentity());
        private bool Current(RequestStamp stamp) => !_disposed && stamp.Epoch == _epoch && IsConnected
            && stamp.Player == _net.PlayerId && ReferenceEquals(stamp.Connection, _connectionIdentity());
        private bool CurrentRead(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeRead;
        private bool CurrentWrite(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeWrite;
        private bool CurrentStallRead(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeStallRead;
        private bool CurrentWantedRead(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeWantedRead;

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeFailure(RpcStatus status) => status.Code switch
        {
            RpcStatusCode.NotSent => "连接尚未就绪",
            RpcStatusCode.Timeout => "请求超时",
            RpcStatusCode.Disconnected => "连接已断开",
            RpcStatusCode.ParseError => "服务器响应无法读取",
            RpcStatusCode.ServerTip => SkillBookMapper.DescribeTip(status.TipId),
            _ => string.IsNullOrEmpty(status.Detail) ? "网络异常" : status.Detail,
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _net.Disconnected -= HandleDisconnected;
            ++_epoch;
            _activeRead = _activeWrite = _activeStallRead = _activeWantedRead = _activeMyWantedRead = _activeFollowUp = 0;
            _state = null;
            _proxyStalls.Clear();
            ClearWantedState();
            _pending = null;
            if (ReferenceEquals(Instance, this)) Instance = null;
            Changed = null;
            BookCrafted = null;
            BookLearned = null;
            BookDismantled = null;
            BookSealed = null;
            BookUnsealed = null;
            ScrollMilestoneClaimed = null;
            BooksBatchDismantled = null;
            WantedOrderPosted = null;
            WantedOrderCanceled = null;
            WantedFulfillConcluded = null;
            ProxyDigestAcked = null;
        }
    }
}
