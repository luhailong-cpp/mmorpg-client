using System;
using System.Globalization;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Jianghu
{
    /// <summary>
    /// 江湖恩怨与天墉生死状一期服务端 RPC 消息号契约（全部引用自动生成的 Rpc 常量，零硬编码）。
    /// </summary>
    public readonly struct JianghuMessageIds
    {
        public uint GetProfile { get; }
        public uint SetRefuseStrangers { get; }
        public uint ClearDefeatMark { get; }
        public uint CreateDuelInvite { get; }
        public uint ConfirmDuelMember { get; }
        public uint RespondDuelInvite { get; }
        public uint ListGrudges { get; }
        public uint DismissGrudge { get; }
        public uint NotifyInvite { get; }
        public uint NotifyStatus { get; }
        public uint GetGuildGrudgeWall { get; }

        public bool IsSupported =>
            GetProfile != 0 && SetRefuseStrangers != 0 && ClearDefeatMark != 0
            && CreateDuelInvite != 0 && ConfirmDuelMember != 0 && RespondDuelInvite != 0
            && ListGrudges != 0 && DismissGrudge != 0
            && NotifyInvite != 0 && NotifyStatus != 0
            && GetGuildGrudgeWall != 0;

        public JianghuMessageIds(
            uint getProfile,
            uint setRefuseStrangers,
            uint clearDefeatMark,
            uint createDuelInvite,
            uint confirmDuelMember,
            uint respondDuelInvite,
            uint listGrudges,
            uint dismissGrudge,
            uint notifyInvite,
            uint notifyStatus,
            uint getGuildGrudgeWall)
        {
            GetProfile = getProfile;
            SetRefuseStrangers = setRefuseStrangers;
            ClearDefeatMark = clearDefeatMark;
            CreateDuelInvite = createDuelInvite;
            ConfirmDuelMember = confirmDuelMember;
            RespondDuelInvite = respondDuelInvite;
            ListGrudges = listGrudges;
            DismissGrudge = dismissGrudge;
            NotifyInvite = notifyInvite;
            NotifyStatus = notifyStatus;
            GetGuildGrudgeWall = getGuildGrudgeWall;
        }

        public static JianghuMessageIds Generated => new JianghuMessageIds(
            SceneJianghuClientPlayerRpc.GetJianghuProfileMessageId,
            SceneJianghuClientPlayerRpc.SetJianghuRefuseStrangersMessageId,
            SceneJianghuClientPlayerRpc.ClearJianghuDefeatMarkMessageId,
            MatchServiceRpc.CreateJianghuDuelInviteMessageId,
            MatchServiceRpc.ConfirmJianghuDuelMemberMessageId,
            MatchServiceRpc.RespondJianghuDuelInviteMessageId,
            MatchServiceRpc.ListJianghuGrudgesMessageId,
            MatchServiceRpc.DismissJianghuGrudgeMessageId,
            MatchServiceRpc.NotifyJianghuDuelInviteMessageId,
            MatchServiceRpc.NotifyJianghuDuelStatusMessageId,
            GuildServiceRpc.GetGuildJianghuGrudgeWallMessageId);
    }

    public enum JianghuInviteActionKind : byte
    {
        None = 0,
        CaptainRespond = 1,
        MemberConfirm = 2,
    }

    /// <summary>
    /// 江湖恩怨与天墉生死状纯 C# 文案、倒计时与状态判定辅助（零 UnityEngine 依赖，WebGL2/IL2CPP 安全）。
    /// 所有展示文案严守仙侠江湖合规措辞，无强制约战或敏感违禁词。
    /// </summary>
    public static class JianghuModels
    {
        public const uint ItemGoldWrit = 3415;
        public const uint ItemClearCharm = 3416;
        public const uint SpiritShopGoodsGoldWrit = 305;
        public const uint SpiritShopGoodsClearCharm = 306;
        public const ulong DefaultSilverWritCost = 500;
        public const ulong DefaultSilverClearCost = 1000;
        public const uint StreakHighlightThreshold = 3;

        public static ulong EstimateServerNowMs(ulong anchorServerMs, float anchorClockSec, float nowClockSec)
        {
            if (anchorServerMs == 0) return 0;
            float elapsed = nowClockSec - anchorClockSec;
            if (elapsed <= 0f || float.IsNaN(elapsed) || float.IsInfinity(elapsed)) return anchorServerMs;
            return anchorServerMs + (ulong)(elapsed * 1000.0);
        }

        public static uint RemainingSeconds(ulong expiresAtMs, ulong estimatedServerNowMs)
        {
            if (expiresAtMs == 0 || estimatedServerNowMs == 0 || estimatedServerNowMs >= expiresAtMs) return 0;
            ulong diffMs = expiresAtMs - estimatedServerNowMs;
            return (uint)((diffMs + 999UL) / 1000UL);
        }

        public static string FormatMinutesSeconds(uint totalSeconds)
        {
            uint minutes = totalSeconds / 60u;
            uint seconds = totalSeconds % 60u;
            return $"{minutes.ToString("00", CultureInfo.InvariantCulture)}:{seconds.ToString("00", CultureInfo.InvariantCulture)}";
        }

        public static string FormatDefeatMark(global::JianghuProfileView profile)
        {
            if (profile == null || !profile.HasActiveDefeatMark) return string.Empty;
            string winner = string.IsNullOrWhiteSpace(profile.DefeatMarkWinnerName)
                ? "无名道友"
                : profile.DefeatMarkWinnerName.Trim();
            return profile.DefeatMarkIsGuildName
                ? $"惜败于【{winner}】"
                : $"惜败于 {winner}";
        }

        public static string FormatClearBadge() => "清风拂尘";

        public static string FormatWritTier(global::Match.JianghuWritTier tier, ulong silverWritCost = DefaultSilverWritCost)
        {
            ulong cost = silverWritCost > 0 ? silverWritCost : DefaultSilverWritCost;
            return tier == global::Match.JianghuWritTier.GoldItem
                ? "金帖生死状（道具 ×1）"
                : $"普通战书（{cost.ToString(CultureInfo.InvariantCulture)} 银两）";
        }

        public static string FormatWritShort(global::Match.JianghuWritTier tier)
            => tier == global::Match.JianghuWritTier.GoldItem ? "金帖" : "普通";

        public static string FormatWritShort(uint rawTier)
            => rawTier == (uint)global::Match.JianghuWritTier.GoldItem ? "金帖" : "普通";

        public static string FormatInviteState(global::Match.JianghuDuelInviteState state) => state switch
        {
            global::Match.JianghuDuelInviteState.AwaitingChallengerTeam => "等待发起小队全员确认",
            global::Match.JianghuDuelInviteState.PendingTarget => "已送达，等待对方应战",
            global::Match.JianghuDuelInviteState.AwaitingTargetTeam => "对方已应战，等待全员确认",
            global::Match.JianghuDuelInviteState.Matched => "生死状已立，正在步入擂台",
            global::Match.JianghuDuelInviteState.Declined => "对方暂未应战",
            global::Match.JianghuDuelInviteState.Aborted => "本封战书已作废（无消耗）",
            _ => "状态同步中",
        };

        public static string FormatStatusReason(string reasonCode) => reasonCode switch
        {
            "matched" => "双方已立状成局，即将进入天墉生死擂。",
            "declined" => "对方未接受本次江湖战书，本单未扣除银两或道具。",
            "expired" => "江湖战书已超时作废，未产生任何消耗。",
            "member_declined" => "有同队道友未确认参战，本封战书已自动作废（零扣费）。",
            "team_changed" => "队伍成员变动，本封战书已自动作废（零扣费）。",
            _ => "江湖战书状态已更新。",
        };

        public static bool IsInviteTerminal(global::Match.JianghuDuelInviteState state)
            => state == global::Match.JianghuDuelInviteState.Matched
                || state == global::Match.JianghuDuelInviteState.Declined
                || state == global::Match.JianghuDuelInviteState.Aborted;

        public static ulong InviteDeadlineMs(global::Match.JianghuDuelInviteView invite)
        {
            if (invite == null) return 0;
            if (invite.State == global::Match.JianghuDuelInviteState.AwaitingChallengerTeam
                || invite.State == global::Match.JianghuDuelInviteState.AwaitingTargetTeam)
            {
                return invite.ConfirmDeadlineMs > 0 ? invite.ConfirmDeadlineMs : invite.ExpiresAtMs;
            }
            return invite.ExpiresAtMs;
        }

        public static JianghuInviteActionKind InviteNeedsMyAction(global::Match.JianghuDuelInviteView invite, ulong myPlayerId)
        {
            if (invite == null || myPlayerId == 0 || IsInviteTerminal(invite.State))
                return JianghuInviteActionKind.None;

            if (invite.State == global::Match.JianghuDuelInviteState.PendingTarget)
                return invite.TargetCaptainId == myPlayerId
                    ? JianghuInviteActionKind.CaptainRespond
                    : JianghuInviteActionKind.None;

            if (invite.State == global::Match.JianghuDuelInviteState.AwaitingChallengerTeam)
            {
                for (int i = 0; i < invite.ChallengerMembers.Count; i++)
                {
                    var m = invite.ChallengerMembers[i];
                    if (m != null && m.PlayerId == myPlayerId && !m.Confirmed)
                        return JianghuInviteActionKind.MemberConfirm;
                }
            }
            else if (invite.State == global::Match.JianghuDuelInviteState.AwaitingTargetTeam)
            {
                for (int i = 0; i < invite.TargetMembers.Count; i++)
                {
                    var m = invite.TargetMembers[i];
                    if (m != null && m.PlayerId == myPlayerId && !m.Confirmed)
                        return JianghuInviteActionKind.MemberConfirm;
                }
            }

            return JianghuInviteActionKind.None;
        }

        public static string FormatStreakBadge(uint winStreak, uint loseStreak)
        {
            if (winStreak >= StreakHighlightThreshold)
                return $"宿敌连胜 ×{winStreak.ToString(CultureInfo.InvariantCulture)}";
            if (loseStreak > 0)
                return "待雪耻";
            return string.Empty;
        }

        public static string FormatOutcome(uint outcome) => outcome switch
        {
            1 => "胜",
            2 => "负",
            3 => "平",
            _ => "—",
        };

        public static string DescribeTip(uint tipId)
        {
            if (tipId == 0) return string.Empty;
            if (tipId == (uint)global::jianghu_error.KJianghuDuelDisabled)
                return "天墉生死擂暂未开启。";
            if (tipId == (uint)global::jianghu_error.KJianghuBlockedByPeer)
                return "对方暂不接收你的江湖战书。";
            if (tipId == (uint)global::jianghu_error.KJianghuTargetRefusesStrangers)
                return "对方当前处于闭关免战状态，暂不接陌生人战书。";
            if (tipId == (uint)global::jianghu_error.KJianghuSelfRefusesStrangers)
                return "你当前已开启闭关免战，需先关闭后方可向陌生人下达战书。";
            if (tipId == (uint)global::jianghu_error.KJianghuSameTargetCooldown)
                return "刚向该道友递过战书，请稍候冷却结束后再试。";
            if (tipId == (uint)global::jianghu_error.KJianghuDailyInviteLimitReached)
                return "今日向陌生人递送江湖战书的次数已达上限。";
            if (tipId == (uint)global::jianghu_error.KJianghuTeamSizeMismatch)
                return "双方小队人数需保持一致（支持 1V1 至 5V5 等人数对决）。";
            if (tipId == (uint)global::jianghu_error.KJianghuTeamChangedAborted)
                return "队伍成员发生变动，本封战书已自动作废（零扣费）。";
            if (tipId == (uint)global::jianghu_error.KJianghuInsufficientWritOrSilver)
                return "银两或对应道具不足，可调整方式或前往百宝阁置办。";
            if (tipId == (uint)global::jianghu_error.KJianghuInvitePendingOrExpired)
                return "该封战书已处理或已过期。";
            if (tipId == (uint)global::jianghu_error.KJianghuNotInviteParticipant)
                return "你不在本封生死状的参战名单中。";
            if (tipId == (uint)global::jianghu_error.KJianghuNoActiveDefeatMark)
                return "当前无生效中的败绩印记，无需洗尘。";
            if (tipId == (uint)global::jianghu_error.KJianghuGrudgeNotFound)
                return "该条江湖恩怨记录已不在列表中。";
            if (tipId == (uint)global::jianghu_error.KJianghuServiceBusy)
                return "天墉擂台司当前繁忙，请稍后再试。";
            if (tipId == (uint)global::common_error.KRateLimitExceeded)
                return "操作过于频繁，请稍后再试。";
            if (tipId == (uint)global::common_error.KServiceUnavailable)
                return "服务暂时不可用，请稍后重试。";
            if (tipId == (uint)global::common_error.KInvalidParameter)
                return "请求参数无效，请核对目标与人数后重试。";
            return $"操作未完成（提示码 {tipId.ToString(CultureInfo.InvariantCulture)}）。";
        }
    }
}
