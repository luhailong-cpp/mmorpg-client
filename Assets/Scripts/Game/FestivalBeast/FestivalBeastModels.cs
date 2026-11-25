using System;
using System.Collections.Generic;
using System.Globalization;

namespace MmorpgClient.Game.FestivalBeast
{
    /// <summary>瑞兽迎春主面板展示优先级（严格按服务端权威状态推导，不自行算资格或发奖）。</summary>
    public enum FestivalBeastDisplayPriority
    {
        None = 0,
        ClaimConfirming = 1,
        Claimable = 2,
        InBattleOrLaunchInFlight = 3,
        OpenChallengeable = 4,
        OpenBlocked = 5,
        ClaimShowcaseClaimed = 6,
        Upcoming = 7,
        HistoryOrUnscheduled = 8,
    }

    /// <summary>首领重击预告与兑现/取消状态快照（单目标、确定性预告，重连不重抽）。</summary>
    public sealed class FestivalBeastTelegraphSnapshot
    {
        public ulong SourceActorId { get; }
        public ulong TargetActorId { get; }
        public uint TelegraphRound { get; }
        public uint ExecuteRound { get; }
        public uint IntentKind { get; }
        public uint Status { get; }
        public ulong StateVersion { get; }

        public string SourceActorIdText => SourceActorId.ToString(CultureInfo.InvariantCulture);
        public string TargetActorIdText => TargetActorId.ToString(CultureInfo.InvariantCulture);

        public bool IsUpcoming => Status == FestivalBeastModels.TelegraphStatusUpcoming;
        public bool IsExecuting => Status == FestivalBeastModels.TelegraphStatusExecuting;
        public bool IsExecuted => Status == FestivalBeastModels.TelegraphStatusExecuted;
        public bool IsCancelledByControl => Status == FestivalBeastModels.TelegraphStatusCancelledByControl;
        public bool IsCancelledTargetInvalid => Status == FestivalBeastModels.TelegraphStatusCancelledTargetInvalid;
        public bool IsCancelled => IsCancelledByControl || IsCancelledTargetInvalid;

        public string StatusLabel => Status switch
        {
            FestivalBeastModels.TelegraphStatusUpcoming => "下回合重击预告",
            FestivalBeastModels.TelegraphStatusExecuting => "本回合蓄力重击（后置）",
            FestivalBeastModels.TelegraphStatusExecuted => "重击已兑现",
            FestivalBeastModels.TelegraphStatusCancelledByControl => "重击已因控制取消",
            FestivalBeastModels.TelegraphStatusCancelledTargetInvalid => "目标已离场，重击取消",
            _ => string.Empty,
        };

        public FestivalBeastTelegraphSnapshot(
            ulong sourceActorId,
            ulong targetActorId,
            uint telegraphRound,
            uint executeRound,
            uint intentKind,
            uint status,
            ulong stateVersion)
        {
            SourceActorId = sourceActorId;
            TargetActorId = targetActorId;
            TelegraphRound = telegraphRound;
            ExecuteRound = executeRound;
            IntentKind = intentKind;
            Status = status;
            StateVersion = stateVersion;
        }
    }

    /// <summary>瑞兽迎春单局结算摘要（支持现场 BattleEndS2C 与断线补查 PlayerBattleResult 双源统一）。</summary>
    public sealed class FestivalBeastSettlementSnapshot
    {
        public ulong BattleId { get; }
        public global::eBattleOutcome Outcome { get; }
        public uint ActivityInstanceId { get; }
        public string RewardIdentity { get; }
        public uint WinsBefore { get; }
        public uint WinsAfter { get; }
        public uint RequiredWins { get; }
        public bool NewlyEligible { get; }
        public uint MaterialUnitsAdded { get; }
        public uint MaterialTradableUnitsAdded { get; }
        public ulong MaterialAvailableUnitsAfter { get; }
        public uint MaterialEarnableRemainingAfter { get; }

        public string BattleIdText => BattleId.ToString(CultureInfo.InvariantCulture);
        public bool IsVictory => Outcome == global::eBattleOutcome.BattleOutcomeSideAWin;
        public bool IsVoid => Outcome == global::eBattleOutcome.BattleOutcomeVoid;

        public FestivalBeastSettlementSnapshot(
            ulong battleId,
            global::eBattleOutcome outcome,
            uint activityInstanceId,
            string rewardIdentity,
            uint winsBefore,
            uint winsAfter,
            uint requiredWins,
            bool newlyEligible,
            uint materialUnitsAdded,
            uint materialTradableUnitsAdded,
            ulong materialAvailableUnitsAfter,
            uint materialEarnableRemainingAfter)
        {
            BattleId = battleId;
            Outcome = outcome;
            ActivityInstanceId = activityInstanceId;
            RewardIdentity = rewardIdentity ?? string.Empty;
            WinsBefore = winsBefore;
            WinsAfter = winsAfter;
            RequiredWins = requiredWins;
            NewlyEligible = newlyEligible;
            MaterialUnitsAdded = materialUnitsAdded;
            MaterialTradableUnitsAdded = materialTradableUnitsAdded;
            MaterialAvailableUnitsAfter = materialAvailableUnitsAfter;
            MaterialEarnableRemainingAfter = materialEarnableRemainingAfter;
        }
    }

    /// <summary>在途开局恢复快照（跨设备/重连直接由服务端返回，不依赖旧设备 UUID）。</summary>
    public sealed class FestivalBeastPendingLaunchSnapshot
    {
        public string ClientRequestId { get; }
        public uint ActivityInstanceId { get; }
        public uint ConfigRevision { get; }
        public ulong LockedPetId { get; }
        public uint MatchedTierId { get; }
        public global::FestivalBeastLaunchState LaunchState { get; }
        public ulong BattleId { get; }
        public ulong RequestedAtMs { get; }
        public ulong AdmittedAtMs { get; }
        public uint RejectTipId { get; }
        public string RejectReason { get; }

        public string LockedPetIdText => LockedPetId.ToString(CultureInfo.InvariantCulture);
        public string BattleIdText => BattleId.ToString(CultureInfo.InvariantCulture);
        public bool HasInFlight =>
            LaunchState == global::FestivalBeastLaunchState.FestivalBeastLaunchLaunching
            || LaunchState == global::FestivalBeastLaunchState.FestivalBeastLaunchAdmitted
            || LaunchState == global::FestivalBeastLaunchState.FestivalBeastLaunchUnknown;

        public FestivalBeastPendingLaunchSnapshot(global::FestivalBeastPendingLaunchInfo proto)
        {
            ClientRequestId = proto?.ClientRequestId ?? string.Empty;
            ActivityInstanceId = proto?.ActivityInstanceId ?? 0u;
            ConfigRevision = proto?.ConfigRevision ?? 0u;
            LockedPetId = proto?.LockedPetId ?? 0UL;
            MatchedTierId = proto?.MatchedTierId ?? 0u;
            LaunchState = proto?.LaunchState ?? global::FestivalBeastLaunchState.FestivalBeastLaunchUnspecified;
            BattleId = proto?.BattleId ?? 0UL;
            RequestedAtMs = proto?.RequestedAtMs ?? 0UL;
            AdmittedAtMs = proto?.AdmittedAtMs ?? 0UL;
            RejectTipId = proto?.RejectTipId ?? 0u;
            RejectReason = proto?.RejectReason ?? string.Empty;
        }
    }

    /// <summary>共享装备材料总账只读摘要（直接透传服务端口径，UI 严禁硬编码上限）。</summary>
    public sealed class FestivalBeastMaterialSummarySnapshot
    {
        public global::FestivalBeastMaterialQuotaMode QuotaMode { get; }
        public uint ExpectedWinUnits { get; }
        public uint EarnableRemainingUnits { get; }
        public ulong AvailableUnits { get; }
        public ulong AvailableTradableUnits { get; }
        public uint EarnedToday { get; }
        public uint DailyLimit { get; }
        public ulong DayStartsAtMs { get; }
        public ulong NextDayResetAtMs { get; }
        public bool WeeklyPhaseActive { get; }
        public uint LifetimeEarned { get; }
        public uint LifetimeLimit { get; }
        public uint EarnedThisWeek { get; }
        public uint WeeklyLimit { get; }
        public ulong WeekStartsAtMs { get; }
        public ulong NextWeekResetAtMs { get; }

        public FestivalBeastMaterialSummarySnapshot(global::FestivalBeastMaterialSummary proto)
        {
            QuotaMode = proto?.QuotaMode ?? global::FestivalBeastMaterialQuotaMode.FestivalMaterialQuotaUnspecified;
            ExpectedWinUnits = proto?.ExpectedWinUnits ?? 0u;
            EarnableRemainingUnits = proto?.EarnableRemainingUnits ?? 0u;
            AvailableUnits = proto?.AvailableUnits ?? 0UL;
            AvailableTradableUnits = proto?.AvailableTradableUnits ?? 0UL;
            EarnedToday = proto?.EarnedToday ?? 0u;
            DailyLimit = proto?.DailyLimit ?? 0u;
            DayStartsAtMs = proto?.DayStartsAtMs ?? 0UL;
            NextDayResetAtMs = proto?.NextDayResetAtMs ?? 0UL;
            WeeklyPhaseActive = proto?.WeeklyPhaseActive ?? false;
            LifetimeEarned = proto?.LifetimeEarned ?? 0u;
            LifetimeLimit = proto?.LifetimeLimit ?? 0u;
            EarnedThisWeek = proto?.EarnedThisWeek ?? 0u;
            WeeklyLimit = proto?.WeeklyLimit ?? 0u;
            WeekStartsAtMs = proto?.WeekStartsAtMs ?? 0UL;
            NextWeekResetAtMs = proto?.NextWeekResetAtMs ?? 0UL;
        }
    }

    /// <summary>瑞兽迎春详情权威快照（只读不可变，完整交付第六节所需字段）。</summary>
    public sealed class FestivalBeastDetailSnapshot
    {
        public ulong ServerNowMs { get; }
        public long CapturedAtLocalMs { get; }
        public global::FestivalBeastPhase Phase { get; }
        public uint ActivityInstanceId { get; }
        public uint ConfigRevision { get; }
        public string ActivityName { get; }
        public string BossName { get; }
        public string BannerAssetKey { get; }
        public string IconKey { get; }
        public ulong EffectiveStartAtMs { get; }
        public ulong EffectiveEndAtMs { get; }
        public ulong ClaimShowcaseEndAtMs { get; }
        public uint CurrentWins { get; }
        public uint RequiredWins { get; }
        public string RewardIdentity { get; }
        public uint RewardDefinitionVersion { get; }
        public uint RewardPetTableId { get; }
        public uint RewardPetModelId { get; }
        public string RewardPetName { get; }
        public bool HasPermanentEligibility { get; }
        public uint EligibilitySourceInstanceId { get; }
        public ulong EligibilityEarnedAtMs { get; }
        public bool RewardClaimed { get; }
        public bool RewardClaimDurable { get; }
        public ulong ClaimedPetId { get; }
        public ulong ClaimedAtMs { get; }
        public string ClaimRequestId { get; }
        public bool CanStartChallenge { get; }
        public global::FestivalBeastActionBlockCode StartBlockCode { get; }
        public string StartBlockReason { get; }
        public bool CanClaimReward { get; }
        public global::FestivalBeastActionBlockCode ClaimBlockCode { get; }
        public string ClaimBlockReason { get; }
        public FestivalBeastPendingLaunchSnapshot PendingLaunch { get; }
        public FestivalBeastMaterialSummarySnapshot MaterialSummary { get; }
        public uint MatchedTierId { get; }
        public uint RoundLimit { get; }
        public uint HeavyDamagePercent { get; }
        public ulong StateRevision { get; }

        public string ClaimedPetIdText => ClaimedPetId.ToString(CultureInfo.InvariantCulture);
        public bool IsClaimConfirming => RewardClaimed && !RewardClaimDurable;
        public FestivalBeastDisplayPriority DisplayPriority => FestivalBeastModels.ResolveDisplayPriority(this);

        public FestivalBeastDetailSnapshot(global::GetFestivalBeastDetailResponse proto, long capturedAtLocalMs)
        {
            if (proto == null) throw new ArgumentNullException(nameof(proto));
            ServerNowMs = proto.ServerNowMs;
            CapturedAtLocalMs = capturedAtLocalMs;
            Phase = proto.Phase;
            ActivityInstanceId = proto.ActivityInstanceId;
            ConfigRevision = proto.ConfigRevision;
            ActivityName = proto.ActivityName ?? string.Empty;
            BossName = proto.BossName ?? string.Empty;
            BannerAssetKey = proto.BannerAssetKey ?? string.Empty;
            IconKey = proto.IconKey ?? string.Empty;
            EffectiveStartAtMs = proto.EffectiveStartAtMs;
            EffectiveEndAtMs = proto.EffectiveEndAtMs;
            ClaimShowcaseEndAtMs = proto.ClaimShowcaseEndAtMs;
            CurrentWins = proto.CurrentWins;
            RequiredWins = proto.RequiredWins;
            RewardIdentity = proto.RewardIdentity ?? string.Empty;
            RewardDefinitionVersion = proto.RewardDefinitionVersion;
            RewardPetTableId = proto.RewardPetTableId;
            RewardPetModelId = proto.RewardPetModelId;
            RewardPetName = proto.RewardPetName ?? string.Empty;
            HasPermanentEligibility = proto.HasPermanentEligibility;
            EligibilitySourceInstanceId = proto.EligibilitySourceInstanceId;
            EligibilityEarnedAtMs = proto.EligibilityEarnedAtMs;
            RewardClaimed = proto.RewardClaimed;
            RewardClaimDurable = proto.RewardClaimDurable;
            ClaimedPetId = proto.ClaimedPetId;
            ClaimedAtMs = proto.ClaimedAtMs;
            ClaimRequestId = proto.ClaimRequestId ?? string.Empty;
            CanStartChallenge = proto.CanStartChallenge;
            StartBlockCode = proto.StartBlockCode;
            StartBlockReason = proto.StartBlockReason ?? string.Empty;
            CanClaimReward = proto.CanClaimReward;
            ClaimBlockCode = proto.ClaimBlockCode;
            ClaimBlockReason = proto.ClaimBlockReason ?? string.Empty;
            PendingLaunch = proto.PendingLaunch != null
                ? new FestivalBeastPendingLaunchSnapshot(proto.PendingLaunch)
                : null;
            MaterialSummary = new FestivalBeastMaterialSummarySnapshot(proto.MaterialSummary);
            MatchedTierId = proto.MatchedTierId;
            RoundLimit = proto.RoundLimit;
            HeavyDamagePercent = proto.HeavyDamagePercent;
            StateRevision = proto.StateRevision;
        }
    }

    /// <summary>
    /// 瑞兽迎春纯 C# 契约辅助与文案工具（零 UnityEngine 依赖，WebGL2 / IL2CPP 安全）。
    /// 所有资格、胜场、重击预告与材料额度严格读取服务端权威字段。
    /// </summary>
    public static class FestivalBeastModels
    {
        public const uint TelegraphKindFestivalHeavyStrike = 9u;
        public const uint TelegraphStatusUpcoming = 1u;
        public const uint TelegraphStatusExecuting = 2u;
        public const uint TelegraphStatusExecuted = 3u;
        public const uint TelegraphStatusCancelledByControl = 4u;
        public const uint TelegraphStatusCancelledTargetInvalid = 5u;

        private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);

        /// <summary>将服务端 UTC 毫秒时间戳固定格式化为北京时间（UTC+8），不依赖本机系统时区。</summary>
        public static string FormatBeijingDateTime(ulong utcMs)
        {
            if (utcMs == 0UL || utcMs > (ulong)long.MaxValue) return string.Empty;
            var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)utcMs).ToOffset(BeijingOffset);
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>格式化左闭右开活动时间窗（北京时间 UTC+8）。</summary>
        public static string FormatBeijingRange(ulong startUtcMs, ulong endUtcMs)
        {
            if (startUtcMs == 0UL || endUtcMs == 0UL || endUtcMs <= startUtcMs) return "未排期";
            return $"{FormatBeijingDateTime(startUtcMs)} - {FormatBeijingDateTime(endUtcMs)} (UTC+8)";
        }

        /// <summary>基于服务端锚点时间戳与本地单调流逝计算剩余毫秒（不信任本机墙钟，不小于 0）。</summary>
        public static ulong ComputeRemainingMs(ulong targetUtcMs, ulong anchoredServerNowMs, long elapsedLocalMs)
        {
            if (targetUtcMs == 0UL || anchoredServerNowMs == 0UL) return 0UL;
            ulong effectiveElapsed = elapsedLocalMs > 0 ? (ulong)elapsedLocalMs : 0UL;
            ulong estimatedNow = anchoredServerNowMs > ulong.MaxValue - effectiveElapsed
                ? ulong.MaxValue
                : anchoredServerNowMs + effectiveElapsed;
            return targetUtcMs > estimatedNow ? targetUtcMs - estimatedNow : 0UL;
        }

        /// <summary>按固定界面优先级收敛当前核心展示态：领奖确认中 &gt; 待领奖 &gt; 在途/战斗中 &gt; 开放可挑战 &gt; 受阻/展示/预告/历史。</summary>
        public static FestivalBeastDisplayPriority ResolveDisplayPriority(FestivalBeastDetailSnapshot detail)
        {
            if (detail == null) return FestivalBeastDisplayPriority.None;
            if (detail.IsClaimConfirming
                || detail.ClaimBlockCode == global::FestivalBeastActionBlockCode.FestivalBeastBlockClaimConfirming)
            {
                return FestivalBeastDisplayPriority.ClaimConfirming;
            }
            if (detail.CanClaimReward)
            {
                return FestivalBeastDisplayPriority.Claimable;
            }
            if ((detail.PendingLaunch != null && detail.PendingLaunch.HasInFlight)
                || detail.StartBlockCode == global::FestivalBeastActionBlockCode.FestivalBeastBlockInBattle
                || detail.StartBlockCode == global::FestivalBeastActionBlockCode.FestivalBeastBlockLaunchInFlight)
            {
                return FestivalBeastDisplayPriority.InBattleOrLaunchInFlight;
            }
            if (detail.Phase == global::FestivalBeastPhase.Open)
            {
                return detail.CanStartChallenge
                    ? FestivalBeastDisplayPriority.OpenChallengeable
                    : FestivalBeastDisplayPriority.OpenBlocked;
            }
            if (detail.Phase == global::FestivalBeastPhase.ClaimShowcase)
            {
                return FestivalBeastDisplayPriority.ClaimShowcaseClaimed;
            }
            if (detail.Phase == global::FestivalBeastPhase.Upcoming)
            {
                return FestivalBeastDisplayPriority.Upcoming;
            }
            return FestivalBeastDisplayPriority.HistoryOrUnscheduled;
        }

        /// <summary>提取战斗状态快照中的活动首领重击预告与最近一次兑现/取消状态。</summary>
        public static void ExtractTelegraphs(
            global::BattleStateS2C state,
            global::TurnResultS2C turnResult,
            out FestivalBeastTelegraphSnapshot activeTelegraph,
            out FestivalBeastTelegraphSnapshot lastResolvedTelegraph)
        {
            activeTelegraph = null;
            lastResolvedTelegraph = null;

            if (state?.Telegraphs != null)
            {
                for (int i = 0; i < state.Telegraphs.Count; i++)
                {
                    var entry = state.Telegraphs[i];
                    if (entry != null && entry.IntentKind == TelegraphKindFestivalHeavyStrike)
                    {
                        activeTelegraph = ToTelegraphSnapshot(entry);
                        break;
                    }
                }
            }

            IList<global::BattleTelegraphEntry> resolvedList = null;
            if (turnResult?.ResolvedTelegraphs != null && turnResult.ResolvedTelegraphs.Count > 0)
            {
                resolvedList = turnResult.ResolvedTelegraphs;
            }
            else if (state?.LastResolvedTelegraphs != null && state.LastResolvedTelegraphs.Count > 0)
            {
                resolvedList = state.LastResolvedTelegraphs;
            }

            if (resolvedList != null)
            {
                for (int i = resolvedList.Count - 1; i >= 0; i--)
                {
                    var entry = resolvedList[i];
                    if (entry != null && entry.IntentKind == TelegraphKindFestivalHeavyStrike)
                    {
                        lastResolvedTelegraph = ToTelegraphSnapshot(entry);
                        break;
                    }
                }
            }
        }

        /// <summary>从现场 BattleEndS2C 提取瑞兽迎春结算摘要；非瑞兽迎春战局返回 null。</summary>
        public static FestivalBeastSettlementSnapshot ExtractSettlement(global::BattleEndS2C battleEnd)
        {
            var summary = battleEnd?.FestivalBeastSettlement;
            if (summary == null || summary.ActivityInstanceId == 0u) return null;
            var outcome = battleEnd.Settlement != null && battleEnd.Settlement.Outcome != global::eBattleOutcome.BattleOutcomeOngoing
                ? battleEnd.Settlement.Outcome
                : battleEnd.Outcome;
            return new FestivalBeastSettlementSnapshot(
                battleEnd.BattleId,
                outcome,
                summary.ActivityInstanceId,
                summary.RewardIdentity,
                summary.WinsBefore,
                summary.WinsAfter,
                summary.RequiredWins,
                summary.NewlyEligible,
                summary.MaterialUnitsAdded,
                summary.MaterialTradableUnitsAdded,
                summary.MaterialAvailableUnitsAfter,
                summary.MaterialEarnableRemainingAfter);
        }

        /// <summary>从断线补查记录 PlayerBattleResult 提取瑞兽迎春结算摘要；非瑞兽迎春战局返回 null。</summary>
        public static FestivalBeastSettlementSnapshot ExtractSettlement(global::PlayerBattleResult result)
        {
            if (result == null || result.FestivalActivityInstanceId == 0u) return null;
            return new FestivalBeastSettlementSnapshot(
                result.BattleId,
                (global::eBattleOutcome)result.Outcome,
                result.FestivalActivityInstanceId,
                result.FestivalRewardIdentity,
                result.FestivalWinsBefore,
                result.FestivalWinsAfter,
                result.FestivalRequiredWins,
                result.FestivalNewlyEligible,
                result.MaterialCreditUnitsAdded,
                0u,
                result.MaterialAvailableUnitsAfter,
                result.MaterialEarnableRemainingAfter);
        }

        /// <summary>将服务端 festival_beast_error / pet_error / 通用提示码转为中文提示（禁止手写数字）。</summary>
        public static string DescribeTip(uint tipId, string fallbackReason = null)
        {
            if (!string.IsNullOrEmpty(fallbackReason)) return fallbackReason;
            return tipId switch
            {
                (uint)global::festival_beast_error.KFestivalBeastNotOpen => "瑞兽迎春当前未开放挑战。",
                (uint)global::festival_beast_error.KFestivalBeastConfigInvalid => "瑞兽迎春活动配置暂未就绪，请稍后再试。",
                (uint)global::festival_beast_error.KFestivalBeastConfigRevisionMismatch => "活动配置已更新，请刷新活动界面后重试。",
                (uint)global::festival_beast_error.KFestivalBeastPetRequired => "请先携一只存活的出战宝宝后再挑战。",
                (uint)global::festival_beast_error.KFestivalBeastPetDead => "当前出战宝宝已倒地，请先恢复状态或更换出战宝宝。",
                (uint)global::festival_beast_error.KFestivalBeastPetChanged => "出战宝宝已变更，请重新发起挑战。",
                (uint)global::festival_beast_error.KFestivalBeastLaunchInFlight => "上一笔挑战开局请求仍在处理中，正在为你恢复原请求。",
                (uint)global::festival_beast_error.KFestivalBeastInBattle => "当前已在战斗中，请先完成当前战局。",
                (uint)global::festival_beast_error.KFestivalBeastFrozen => "当前角色状态暂不可操作，请稍后再试。",
                (uint)global::festival_beast_error.KFestivalBeastNotEligible => "尚未达成累计 3 胜领奖资格。",
                (uint)global::festival_beast_error.KFestivalBeastAlreadyClaimed => "该神兽奖励已领取，不可重复领取。",
                (uint)global::festival_beast_error.KFestivalBeastExclusiveSlotOccupied => "该神兽专属赠送位已占用，无法重复发放。",
                (uint)global::festival_beast_error.KFestivalBeastRequestConflict => "请求身份冲突，请刷新活动状态后重试。",
                (uint)global::festival_beast_error.KFestivalBeastServiceUnavailable => "瑞兽迎春服务暂不可用，请稍后重试。",
                (uint)global::pet_error.KPetNotTransferable => "绑定神兽不可交易、赠送、寄售或用于分解合成。",
                (uint)global::common_error.KRateLimitExceeded => "操作太快，请稍候再试。",
                (uint)global::common_error.KServiceUnavailable => "服务暂时不可用，请稍后再试。",
                (uint)global::common_error.KInvalidParameter => "请求参数无效，请刷新页面后重试。",
                _ => $"操作失败（{tipId}）。",
            };
        }

        private static FestivalBeastTelegraphSnapshot ToTelegraphSnapshot(global::BattleTelegraphEntry entry)
            => new FestivalBeastTelegraphSnapshot(
                entry.SourceActorId,
                entry.TargetActorId,
                entry.TelegraphRound,
                entry.ExecuteRound,
                entry.IntentKind,
                entry.Status,
                entry.StateVersion);
    }
}
