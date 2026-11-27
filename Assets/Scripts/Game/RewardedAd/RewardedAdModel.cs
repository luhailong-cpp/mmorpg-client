using System;
using System.Globalization;
using MmorpgClient.Game.Common;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.RewardedAd
{
    public readonly struct RewardedAdMessageIds
    {
        public uint GetInfo { get; }
        public uint BeginSession { get; }
        public uint Claim { get; }

        public bool IsSupported => GetInfo != 0 && BeginSession != 0 && Claim != 0
            && GetInfo != BeginSession && GetInfo != Claim && BeginSession != Claim;

        public RewardedAdMessageIds(uint getInfo, uint beginSession, uint claim)
        {
            GetInfo = getInfo;
            BeginSession = beginSession;
            Claim = claim;
        }

        public static RewardedAdMessageIds Generated => new RewardedAdMessageIds(
            SceneRewardedAdClientPlayerRpc.GetRewardedAdInfoMessageId,
            SceneRewardedAdClientPlayerRpc.BeginRewardedAdSessionMessageId,
            SceneRewardedAdClientPlayerRpc.ClaimRewardedAdMessageId);
    }

    /// <summary>
    /// 观看完成后待核销的领奖凭据（按 PlayerId 绑定，抗 30 秒全屏广告暂停心跳引发的断线重连）。
    /// </summary>
    public readonly struct RewardedAdPendingClaim
    {
        public ulong PlayerId { get; }
        public uint SlotId { get; }
        public ulong AdSessionId { get; }
        public ulong ClientRequestId { get; }
        public string SsvProof { get; }

        public bool IsValid => PlayerId != 0 && SlotId != 0 && AdSessionId != 0 && ClientRequestId != 0;

        public RewardedAdPendingClaim(ulong playerId, uint slotId, ulong adSessionId, ulong clientRequestId, string ssvProof)
        {
            PlayerId = playerId;
            SlotId = slotId;
            AdSessionId = adSessionId;
            ClientRequestId = clientRequestId;
            SsvProof = ssvProof ?? string.Empty;
        }
    }

    /// <summary>
    /// 激励视频入口与二次确认窗只读展示快照（跨模块不可变传递，零 UnityEngine 依赖）。
    /// </summary>
    public readonly struct RewardedAdViewState
    {
        public uint SlotId { get; }
        public bool ServerEnabled { get; }
        public bool BridgeSupported { get; }
        public bool BridgeReady { get; }
        public bool HasAdUnitId { get; }
        public bool InBattle { get; }
        public bool Busy { get; }
        public uint DailyLimit { get; }
        public uint ClaimedToday { get; }
        public uint RemainingToday => DailyLimit > ClaimedToday ? DailyLimit - ClaimedToday : 0u;
        public ulong CooldownRemainingSec { get; }
        public bool InCooldown => CooldownRemainingSec > 0;
        public uint CurrencyType { get; }
        public ulong RewardAmount { get; }
        public ulong BindGoldBalance { get; }
        public ulong GoldBalance { get; }

        /// <summary>
        /// 零拒审硬门禁（防 App Store 2.1 空按钮与首审暴露）：
        /// 仅在服务端总开关开启、当前宿主支持且广告素材已加载就绪、配置了广告位 ID、今日尚有余量且非战斗态时展示入口。
        /// </summary>
        public bool ShouldShowEntry =>
            ServerEnabled && BridgeSupported && BridgeReady && HasAdUnitId && RemainingToday > 0 && !InBattle;

        public bool CanClickWatch => ShouldShowEntry && !InCooldown && !Busy;

        public RewardedAdViewState(
            uint slotId,
            bool serverEnabled,
            bool bridgeSupported,
            bool bridgeReady,
            bool hasAdUnitId,
            bool inBattle,
            bool busy,
            uint dailyLimit,
            uint claimedToday,
            ulong cooldownRemainingSec,
            uint currencyType,
            ulong rewardAmount,
            ulong bindGoldBalance,
            ulong goldBalance)
        {
            SlotId = slotId;
            ServerEnabled = serverEnabled;
            BridgeSupported = bridgeSupported;
            BridgeReady = bridgeReady;
            HasAdUnitId = hasAdUnitId;
            InBattle = inBattle;
            Busy = busy;
            DailyLimit = dailyLimit;
            ClaimedToday = claimedToday;
            CooldownRemainingSec = cooldownRemainingSec;
            CurrencyType = currencyType;
            RewardAmount = rewardAmount;
            BindGoldBalance = bindGoldBalance;
            GoldBalance = goldBalance;
        }
    }

    /// <summary>
    /// 零拒审激励视频纯规则与合规文案模型（脱离 UnityEngine，全量纳入 EditMode 单测与禁用词门禁）。
    /// </summary>
    public static class RewardedAdModel
    {
        public const uint DefaultSlotId = 1u;
        public const string VideoIconResourcePath = "UI/Icons/icon_rewarded_ad_video";

        // 严格中立合规文案：仅客观描述广告奖励规则，与抖音/微信/AdMob 合规细则对齐。
        public const string VideoBadgeText = "[广告]";
        public const string EntryButtonText = "观看广告领绑定银两";
        public const string DialogTitleText = "广告奖励";
        public const string ConfirmWatchButtonText = "观看广告领取";
        public const string CancelButtonText = "关闭";
        public const string WatchingStatusText = "正在播放广告视频…";
        public const string ClaimingStatusText = "正在核验并发放奖励…";
        public const string DismissedEarlyStatusText = "未完整观看广告视频，未发放奖励。";
        public const string UnavailableStatusText = "暂无可播放的广告视频，请稍后再试。";
        public const string ReconnectPendingStatusText = "网络波动，恢复连接后将自动为您补发奖励。";

        public static string BuildDialogBody(ulong rewardAmount, uint currencyType, uint remainingToday, uint dailyLimit)
        {
            string reward = CurrencyLabels.Format(rewardAmount, currencyType);
            return $"观看一段完整广告视频后，可领取 {reward}（本周剩余 {remainingToday.ToString(CultureInfo.InvariantCulture)}/{dailyLimit.ToString(CultureInfo.InvariantCulture)} 次）。绑定银两仅用于角色个人养成消耗，不可交易。";
        }

        public static string BuildBalanceLine(ulong bindGoldBalance, ulong goldBalance)
            => $"绑定银两：{bindGoldBalance.ToString("#,0", CultureInfo.InvariantCulture)}   流通银两：{goldBalance.ToString("#,0", CultureInfo.InvariantCulture)}";

        public static string BuildClaimedToast(ulong rewardAmount, uint currencyType)
            => $"已领取 {CurrencyLabels.Format(rewardAmount, currencyType)}。";

        public static string BuildCooldownButtonText(ulong cooldownRemainingSec)
            => cooldownRemainingSec > 0
                ? $"冷却中（{cooldownRemainingSec.ToString(CultureInfo.InvariantCulture)}秒）"
                : ConfirmWatchButtonText;

        public static RewardedAdViewState Evaluate(
            global::GetRewardedAdInfoResponse info,
            uint slotId,
            bool bridgeSupported,
            bool bridgeReady,
            string adUnitId,
            bool inBattle,
            bool busy,
            ulong estimatedServerNowMs)
        {
            global::RewardedAdSlotView slot = FindSlot(info, slotId);
            if (slot == null)
            {
                return new RewardedAdViewState(
                    slotId,
                    serverEnabled: false,
                    bridgeSupported: bridgeSupported,
                    bridgeReady: bridgeReady,
                    hasAdUnitId: !string.IsNullOrWhiteSpace(adUnitId),
                    inBattle: inBattle,
                    busy: busy,
                    dailyLimit: 0,
                    claimedToday: 0,
                    cooldownRemainingSec: 0,
                    currencyType: CurrencyLabels.BindGold,
                    rewardAmount: 0,
                    bindGoldBalance: info?.BindGoldBalance ?? 0,
                    goldBalance: info?.GoldBalance ?? 0);
            }

            ulong cooldownRemSec = 0;
            if (slot.CooldownUntilMs > estimatedServerNowMs)
            {
                cooldownRemSec = (slot.CooldownUntilMs - estimatedServerNowMs + 999UL) / 1000UL;
            }

            return new RewardedAdViewState(
                slot.SlotId,
                serverEnabled: slot.Enabled,
                bridgeSupported: bridgeSupported,
                bridgeReady: bridgeReady,
                hasAdUnitId: !string.IsNullOrWhiteSpace(adUnitId),
                inBattle: inBattle,
                busy: busy,
                dailyLimit: slot.DailyLimit,
                claimedToday: slot.ClaimedToday,
                cooldownRemainingSec: cooldownRemSec,
                currencyType: slot.CurrencyType,
                rewardAmount: slot.RewardAmount,
                bindGoldBalance: info?.BindGoldBalance ?? 0,
                goldBalance: info?.GoldBalance ?? 0);
        }

        public static global::RewardedAdSlotView FindSlot(global::GetRewardedAdInfoResponse info, uint slotId)
        {
            if (info?.Slots == null) return null;
            for (int i = 0; i < info.Slots.Count; i++)
            {
                var slot = info.Slots[i];
                if (slot != null && slot.SlotId == slotId) return slot;
            }
            return null;
        }

        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)rewarded_ad_error.KRewardedAdDisabled => "当前未开放广告奖励。",
            (uint)rewarded_ad_error.KRewardedAdPlatformUnsupported => "当前渠道暂不支持广告奖励。",
            (uint)rewarded_ad_error.KRewardedAdLevelNotEnough => "角色等级不足，暂不可参与。",
            (uint)rewarded_ad_error.KRewardedAdDailyLimitReached => "本周广告奖励次数已领完，下周一 05:00 重置。",
            (uint)rewarded_ad_error.KRewardedAdCooldown => "领取过于频繁，请冷却结束后再试。",
            (uint)rewarded_ad_error.KRewardedAdSessionInvalid => "本次观看凭据无效，请重新观看。",
            (uint)rewarded_ad_error.KRewardedAdSessionExpired => "本次观看凭据已过期，请重新观看。",
            (uint)rewarded_ad_error.KRewardedAdWatchTooShort => "请完整观看广告视频后再领取。",
            (uint)rewarded_ad_error.KRewardedAdCurrencyBlocked => "当前绑定银两发放受限，请稍后再试。",
            (uint)rewarded_ad_error.KRewardedAdInBattle => "战斗中不可观看广告，请脱战后再试。",
            (uint)rewarded_ad_error.KRewardedAdFrozen => "角色状态切换中，请稍后再试。",
            (uint)rewarded_ad_error.KRewardedAdSsvRequired => "缺少广告核验凭据，请稍后再试。",
            (uint)rewarded_ad_error.KRewardedAdUnavailable => "广告奖励服务暂不可用，请稍后再试。",
            (uint)common_error.KInvalidParameter => "请求参数异常，请刷新后重试。",
            (uint)common_error.KServiceUnavailable => "服务暂时繁忙，请稍后再试。",
            _ => "领取未完成，请稍后再试。",
        };
    }
}
