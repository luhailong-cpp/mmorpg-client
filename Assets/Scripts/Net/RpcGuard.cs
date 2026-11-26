using System;
using System.Collections.Generic;
using System.Diagnostics;
using MmorpgClient.Net.Generated;
using MmorpgClient.Table;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 客户端统一 RPC 限流与按服务域熔断守卫，挂接在 <see cref="IRpcChannel"/> / GameClient 统一出口。
    /// - 限流：按 messageId 查 <see cref="MessageLimiterTableManager"/>（缺省 3 次 / 2.5s），超限时本地返回 1008，不消耗 gate 非法包计数。
    /// - 熔断：按服务域分桶（不全局一把锁），仅统计 Timeout 与 <see cref="TipFaults.IsFault"/>；1008 仅释放 HalfOpen 探测槽位，绝不触发熔断。
    /// </summary>
    public sealed class RpcGuard
    {
        public const string CircuitOpenError = "circuit open";
        public const uint RateLimitTipId = (uint)common_error.KRateLimitExceeded;
        public const int DefaultMaxRequests = 3;
        public const float DefaultTimeWindowSeconds = 1f;
        // 服务端 C++ MessageLimiter 与 Java MessageRateLimiter 均已统一为精确单调毫秒/纳秒滑动窗口(>=)，
        // 客户端只需在服务端窗口外留 0.5 秒吸收公网上行抖动（1 秒窗口 → 1.5 秒）。
        public const float GateWindowMarginSeconds = 0.5f;
        public const float DefaultMessageWindowSeconds = DefaultTimeWindowSeconds + GateWindowMarginSeconds;
        public const int DefaultCircuitFailureThreshold = 3;
        public const float DefaultCircuitOpenSeconds = 15f;

        private readonly Func<float> _clock;
        private readonly Func<uint, (int maxRequests, float windowSeconds)?> _limitResolver;
        private readonly Func<uint, string> _serviceResolver;
        private readonly Func<uint, bool> _isExempt;
        private readonly int _circuitFailureThreshold;
        private readonly float _circuitOpenSeconds;
        private readonly Dictionary<uint, SlidingWindowLimiter> _limiters = new();
        private readonly Dictionary<string, SimpleCircuitBreaker> _breakers = new();

        public RpcGuard(
            Func<float> clock = null,
            Func<uint, (int maxRequests, float windowSeconds)?> limitResolver = null,
            Func<uint, string> serviceResolver = null,
            Func<uint, bool> isExempt = null,
            int circuitFailureThreshold = DefaultCircuitFailureThreshold,
            float circuitOpenSeconds = DefaultCircuitOpenSeconds)
        {
            if (circuitFailureThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(circuitFailureThreshold));
            if (circuitOpenSeconds <= 0f || float.IsNaN(circuitOpenSeconds) || float.IsInfinity(circuitOpenSeconds))
                throw new ArgumentOutOfRangeException(nameof(circuitOpenSeconds));
            if (clock == null)
            {
                var sw = Stopwatch.StartNew();
                _clock = () => (float)sw.Elapsed.TotalSeconds;
            }
            else
            {
                _clock = clock;
            }
            _limitResolver = limitResolver ?? DefaultResolveLimit;
            _serviceResolver = serviceResolver ?? DefaultResolveService;
            _isExempt = isExempt ?? DefaultIsExempt;
            _circuitFailureThreshold = circuitFailureThreshold;
            _circuitOpenSeconds = circuitOpenSeconds;
        }

        /// <summary>只读检查：当前消息号是否既未被熔断、也还有限流配额（供 UI 置灰按钮用）。</summary>
        public bool CanSend(uint messageId) => !IsCircuitOpen(messageId) && !IsRateLimited(messageId);

        public bool IsRateLimited(uint messageId)
        {
            if (_isExempt(messageId)) return false;
            var limiter = LimiterFor(messageId);
            return limiter != null && !limiter.CanAcquire(_clock());
        }

        public bool IsCircuitOpen(uint messageId)
        {
            if (_isExempt(messageId)) return false;
            string service = _serviceResolver(messageId);
            if (string.IsNullOrEmpty(service)) return false;
            return _breakers.TryGetValue(service, out var breaker) && !breaker.CanAcquire(_clock());
        }

        public CircuitState GetCircuitState(uint messageId)
        {
            if (_isExempt(messageId)) return CircuitState.Closed;
            string service = _serviceResolver(messageId);
            if (string.IsNullOrEmpty(service)) return CircuitState.Closed;
            return _breakers.TryGetValue(service, out var breaker) ? breaker.GetState(_clock()) : CircuitState.Closed;
        }

        /// <summary>
        /// 统一发包前门禁。先判熔断（避免熔断期误扣限流配额），再扣限流配额，最后锁定 HalfOpen 探测槽位。
        /// </summary>
        public bool TryBeginCall(uint messageId, out RpcStatus rejection)
        {
            if (_isExempt(messageId))
            {
                rejection = default;
                return true;
            }
            float now = _clock();
            SimpleCircuitBreaker breaker = BreakerFor(messageId);
            if (breaker != null && !breaker.CanAcquire(now))
            {
                rejection = RpcStatus.NotSent(CircuitOpenError);
                return false;
            }
            SlidingWindowLimiter limiter = LimiterFor(messageId);
            if (limiter != null && !limiter.TryAcquire(now))
            {
                rejection = RpcStatus.ServerTip(RateLimitTipId);
                return false;
            }
            breaker?.TryAcquire(now);
            rejection = default;
            return true;
        }

        /// <summary>统一收口：按传输层结局更新服务域熔断状态。</summary>
        public void CompleteCall(uint messageId, RpcStatus status)
        {
            if (_isExempt(messageId)) return;
            SimpleCircuitBreaker breaker = BreakerFor(messageId);
            if (breaker == null) return;
            switch (status.Code)
            {
                case RpcStatusCode.Ok:
                    breaker.RecordSuccess();
                    break;
                case RpcStatusCode.Timeout:
                    breaker.RecordFailure(_clock());
                    break;
                case RpcStatusCode.ServerTip:
                    if (status.TipId != RateLimitTipId && TipFaults.IsFault(status.TipId))
                        breaker.RecordFailure(_clock());
                    else
                        breaker.ReleaseProbe();
                    break;
                default:
                    breaker.ReleaseProbe();
                    break;
            }
        }

        /// <summary>供上层业务补充上报协议体内部的下游故障（例如外层 Ok 但回包缺视图或内层 tip 属于 TipFaults）。</summary>
        public void RecordServiceFault(uint messageId)
        {
            if (_isExempt(messageId)) return;
            BreakerFor(messageId)?.RecordInnerFault(_clock());
        }

        public void RecordServiceSuccess(uint messageId)
        {
            if (_isExempt(messageId)) return;
            BreakerFor(messageId)?.Reset();
        }

        public void ReleaseServiceProbe(uint messageId)
        {
            if (_isExempt(messageId)) return;
            BreakerFor(messageId)?.ReleaseProbe();
        }

        /// <summary>换连接或断线时统一清零全部限流窗口与熔断状态。</summary>
        public void Reset()
        {
            _limiters.Clear();
            _breakers.Clear();
        }

        private SlidingWindowLimiter LimiterFor(uint messageId)
        {
            var spec = _limitResolver(messageId);
            if (!spec.HasValue || spec.Value.maxRequests <= 0)
            {
                _limiters.Remove(messageId);
                return null;
            }
            if (_limiters.TryGetValue(messageId, out var existing)
                && existing.MaxCount == spec.Value.maxRequests
                && existing.WindowSeconds == spec.Value.windowSeconds)
            {
                return existing;
            }
            // 配置表热更导致额度或窗口变化时自动按新参数重建，无需断线重连。
            var created = new SlidingWindowLimiter(spec.Value.maxRequests, spec.Value.windowSeconds);
            _limiters[messageId] = created;
            return created;
        }

        private SimpleCircuitBreaker BreakerFor(uint messageId)
        {
            string service = _serviceResolver(messageId);
            if (string.IsNullOrEmpty(service)) return null;
            if (!_breakers.TryGetValue(service, out var breaker))
                _breakers[service] = breaker = new SimpleCircuitBreaker(_circuitFailureThreshold, _circuitOpenSeconds);
            return breaker;
        }

        /// <summary>会话生命周期与单向移动控制号豁免：由连接/重连状态机自身管理，不得被业务限流或熔断阻断。</summary>
        public static bool DefaultIsExempt(uint messageId)
        {
            switch (messageId)
            {
                case MessageIds.Login:
                case MessageIds.CreatePlayer:
                case MessageIds.EnterGame:
                case MessageIds.LeaveGame:
                case MessageIds.Disconnect:
                case MessageIds.RefreshToken:
                case MessageIds.EnterScene:
                case MessageIds.GetSyncDigest:
                case MessageIds.MoveStart:
                case MessageIds.MoveSync:
                case MessageIds.MoveStop:
                    return true;
                default:
                    return false;
            }
        }

        public static (int maxRequests, float windowSeconds)? DefaultResolveLimit(uint messageId)
        {
            if (MessageLimiterTableManager.Instance.TryFindById(messageId, out var row) && row != null)
            {
                if (row.MaxRequests == 0) return null;
                float window = row.TimeWindow > 0 ? row.TimeWindow : DefaultTimeWindowSeconds;
                return ((int)row.MaxRequests, window + GateWindowMarginSeconds);
            }
            // 未加载配置表（如纯逻辑单测）时，对已知 5 次/秒的只读消息给出一致回退，其余走 gate 默认 3 次/秒。
            int fallbackMax = (messageId == MessageIds.GetMyTeam
                || messageId == MessageIds.ListMyInvites
                || messageId == MessageIds.RankGetRankPage
                || messageId == MessageIds.RankGetRankAround) ? 5 : DefaultMaxRequests;
            return (fallbackMax, DefaultTimeWindowSeconds + GateWindowMarginSeconds);
        }

        public static string DefaultResolveService(uint messageId)
        {
            switch (messageId)
            {
                case MessageIds.GetMyTeam:
                case MessageIds.ListMyInvites:
                case MessageIds.CreateTeam:
                case MessageIds.ApplyJoinTeam:
                case MessageIds.HandleApplication:
                case MessageIds.InviteToTeam:
                case MessageIds.RespondInvite:
                case MessageIds.LeaveTeam:
                case MessageIds.KickMember:
                case MessageIds.TransferLeader:
                case MessageIds.DisbandTeam:
                case MessageIds.StartTeamMatch:
                    return "team";

                case MessageIds.JoinQueue:
                case MessageIds.CancelQueue:
                case MessageIds.GetQueueStatus:
                case MessageIds.ChallengePlayer:
                case MessageIds.RespondChallenge:
                case MessageIds.WatchBattle:
                case MessageIds.ListWatchableBattles:
                case MessageIds.RequestBattleTicket:
                case MessageIds.GetNetworkProbePlan:
                case MessageIds.CommitNetworkProbe:
                    return "match";

                case MessageIds.SubmitBattleAction:
                case MessageIds.GetBattleState:
                case MessageIds.StopWatchBattle:
                case MessageIds.SetAutoBattle:
                case MessageIds.SurrenderBattle:
                case MessageIds.UpdateBattlePrep:
                    return "battle";

                case MessageIds.RankGetRankPage:
                case MessageIds.RankGetRankAround:
                    return "rank";

                case MessageIds.CreateGuild:
                case MessageIds.GetGuild:
                case MessageIds.GetPlayerGuild:
                case MessageIds.LeaveGuild:
                case MessageIds.DisbandGuild:
                case MessageIds.SetGuildAnnouncement:
                case MessageIds.RenameGuild:
                case MessageIds.SetGuildMemberRole:
                case MessageIds.DonateToGuild:
                case MessageIds.GetGuildDonateOptions:
                case MessageIds.UpgradeGuild:
                case MessageIds.GetGuildRank:
                case MessageIds.GetGuildRankByGuild:
                case MessageIds.TransferGuildLeader:
                case MessageIds.KickGuildMember:
                case MessageIds.ApplyJoinGuild:
                case MessageIds.CancelGuildApplication:
                case MessageIds.ListGuildApplications:
                case MessageIds.ListMyGuildApplications:
                case MessageIds.ReviewGuildApplication:
                case MessageIds.GetGuildShop:
                case MessageIds.BuyGuildShopGoods:
                case MessageIds.LightGuildLantern:
                case MessageIds.ClaimGuildReunion:
                case MessageIds.GetGuildActivities:
                case MessageIds.StartGuildTrial:
                case MessageIds.RespondGuildTrialInvite:
                    return "guild";

                case MessageIds.MailListMails:
                case MessageIds.MailReadMail:
                case MessageIds.MailClaimMailAttachments:
                case MessageIds.MailDeleteMails:
                case MessageIds.MailClearReadMails:
                    return "mail";

                case MessageIds.JubaozhaiBrowseListings:
                case MessageIds.JubaozhaiGetListingDetail:
                case MessageIds.JubaozhaiSetFavorite:
                case MessageIds.JubaozhaiGetMyShelf:
                case MessageIds.JubaozhaiListOfficialSkus:
                case MessageIds.JubaozhaiCreateOfficialOrder:
                case MessageIds.JubaozhaiGetCommerceOrder:
                case MessageIds.JubaozhaiCancelCommerceOrder:
                case MessageIds.JubaozhaiListCommerceOrders:
                case MessageIds.JubaozhaiCreatePlayerTrade:
                case MessageIds.JubaozhaiConfirmPlayerTrade:
                case MessageIds.JubaozhaiCreateConsignment:
                case MessageIds.JubaozhaiBuyConsignment:
                case MessageIds.JubaozhaiCancelConsignment:
                case MessageIds.JubaozhaiPlaceMarketOrder:
                case MessageIds.JubaozhaiCancelMarketOrder:
                case MessageIds.JubaozhaiListMarketOrders:
                case MessageIds.JubaozhaiGetMarketDepth:
                case MessageIds.JubaozhaiCreateAuctionLot:
                case MessageIds.JubaozhaiGetAuctionLot:
                case MessageIds.JubaozhaiPlaceAuctionBid:
                case MessageIds.JubaozhaiCancelAuctionLot:
                case MessageIds.JubaozhaiBrowseCommerceOffers:
                case MessageIds.JubaozhaiListTradeMarkets:
                    return "jubaozhai";

                case MessageIds.GetAttributePanel:
                case MessageIds.AllocateAttributePoints:
                case MessageIds.RenameAttributeScheme:
                case MessageIds.SwitchAttributeScheme:
                case MessageIds.ResetAttributePoints:
                case MessageIds.AutoAllocateAttributePoints:
                case MessageIds.CreateAttributeScheme:
                    return "attribute";

                case MessageIds.GetPetList:
                case MessageIds.ResetPetPoints:
                case MessageIds.SummonPet:
                case MessageIds.RecallPet:
                case MessageIds.AllocatePetPoints:
                case MessageIds.AutoAllocatePetPoints:
                case MessageIds.RenamePet:
                case MessageIds.GetPetShop:
                case MessageIds.BuyPet:
                case MessageIds.GetPetEntries:
                case MessageIds.PreviewPetEntries:
                case MessageIds.SavePetEntries:
                    return "pet";

                case MessageIds.GetMysteryMerchantInfo:
                case MessageIds.BuyMysteryMerchantGoods:
                    return "mystery_merchant";

                case MessageIds.GetAfkPass:
                case MessageIds.BuyAfkPass:
                    return "afk_pass";

                case MessageIds.GetAppearanceInfo:
                case MessageIds.SwitchAppearance:
                case MessageIds.StartAppearanceTrial:
                case MessageIds.CancelAppearanceTrial:
                    return "appearance";

                case MessageIds.GetBag:
                case MessageIds.SortBag:
                case MessageIds.UnequipItem:
                case MessageIds.EquipItem:
                case MessageIds.InlayGem:
                case MessageIds.EnhanceEquip:
                case MessageIds.RemoveGem:
                    return "scene_bag";

                case MessageIds.ListSkills:
                case MessageIds.ReleaseSkill:
                case MessageIds.UpgradeSkill:
                    return "scene_skill";

                case MessageIds.GetActivityList:
                case MessageIds.GetMissionList:
                    return "scene_activity";

                case MessageIds.SceneInfoC2S:
                case MessageIds.TeleportRequest:
                case MessageIds.TravelToZone:
                    return "scene_travel";

                case SceneClientPlayerCommonRpc.RenamePlayerMessageId:
                    return "scene_common";

                case ClientPlayerChatRpc.SendChatMessageId:
                case ClientPlayerChatRpc.PullChatHistoryMessageId:
                case ClientPlayerChatRpc.ReportChatMessageId:
                    return "chat";

                case ClientPlayerPaymentRpc.ListPayChannelsMessageId:
                case ClientPlayerPaymentRpc.CreatePayIntentMessageId:
                case ClientPlayerPaymentRpc.GetPayIntentMessageId:
                case ClientPlayerPaymentRpc.SubmitPurchaseMessageId:
                    return "payment";

                default:
                    // 未归类的新消息号按号隔离，避免互相连累。
                    return "msg:" + messageId;
            }
        }
    }
}
