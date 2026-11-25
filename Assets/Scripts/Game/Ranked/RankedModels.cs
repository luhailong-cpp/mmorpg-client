using System;
using System.Collections.Generic;
using System.Globalization;
using Match;

namespace MmorpgClient.Game.Ranked
{
    /// <summary>论剑排位首发 V1 纯 C# 只读展示与状态模型（不引用 UnityEngine）。</summary>
    public static class RankedFormatting
    {
        public const uint DefaultBattleConfigId = 5;
        public const uint DefaultCharacterPresetId = 101;
        public const uint DefaultPetPresetId = 203;
        public const uint DefaultTeamSize = 1;
        public const uint DefaultWaitPromptSeconds = 120;
        public const uint MilestoneAvatarMatches = 4;
        public const uint MilestoneCardMatches = 8;
        public const uint MilestoneTalismanMatches = 12;

        /// <summary>
        /// 服务端段位枚举转中文名。客户端绝不自行按分数推算段位，始终展示服务端下发的段位枚举。
        /// </summary>
        public static string TierDisplayName(RankedTier tier, bool qualified = true)
        {
            if (!qualified || tier == RankedTier.Unspecified) return "未亮段";
            return tier switch
            {
                RankedTier.Chushi => "初试",
                RankedTier.Qingfeng => "青锋",
                RankedTier.Yunzhang => "云章",
                RankedTier.Tianshu => "天枢",
                RankedTier.Wushuang => "无双",
                _ => "未亮段",
            };
        }

        /// <summary>段位徽章资源键（对应 Assets/Resources/UI/Ugui/LunjianV1/rank-1..5）。</summary>
        public static string TierBadgeKey(RankedTier tier) => tier switch
        {
            RankedTier.Chushi => "rank-1",
            RankedTier.Qingfeng => "rank-2",
            RankedTier.Yunzhang => "rank-3",
            RankedTier.Tianshu => "rank-4",
            RankedTier.Wushuang => "rank-5",
            _ => "rank-1",
        };

        /// <summary>百分制积分转整数展示（154000 -> "1540"）。</summary>
        public static string FormatRating(long ratingCenti)
            => (ratingCenti / 100L).ToString(CultureInfo.InvariantCulture);

        /// <summary>百分制积分变化转带符号整数（+1600 -> "+16"，-1400 -> "-14"，0 -> "0"）。</summary>
        public static string FormatRatingDelta(long deltaCenti)
        {
            long points = deltaCenti / 100L;
            if (points > 0) return "+" + points.ToString(CultureInfo.InvariantCulture);
            return points.ToString(CultureInfo.InvariantCulture);
        }

        public static string RewardKindName(RankedRewardKind kind) => kind switch
        {
            RankedRewardKind.RankedRewardAvatarPattern => "头像纹样",
            RankedRewardKind.RankedRewardCardBackground => "名片底纹",
            RankedRewardKind.RankedRewardTalisman => "青云玉符",
            RankedRewardKind.RankedRewardTierBadge => "段位徽章",
            RankedRewardKind.RankedRewardTierTitle => "赛季称号",
            RankedRewardKind.RankedRewardChampionInscription => "榜首题名",
            _ => "赛季外观",
        };

        public static string OutcomeBannerText(eBattleOutcome outcome, uint playerTeamIndex, bool isVoid)
        {
            if (isVoid || outcome == eBattleOutcome.BattleOutcomeVoid) return "本局作废";
            if (outcome == eBattleOutcome.BattleOutcomeDraw) return "平局";
            bool won = (playerTeamIndex == 0 && outcome == eBattleOutcome.BattleOutcomeSideAWin)
                || (playerTeamIndex == 1 && outcome == eBattleOutcome.BattleOutcomeSideBWin);
            return won ? "论剑得胜" : "论剑惜败";
        }

        public static string FormatElapsedClock(uint seconds)
        {
            uint mm = seconds / 60u;
            uint ss = seconds % 60u;
            return mm.ToString("00", CultureInfo.InvariantCulture) + ":" + ss.ToString("00", CultureInfo.InvariantCulture);
        }

        public static string NextMilestoneHint(uint validMatches)
        {
            if (validMatches < MilestoneAvatarMatches)
                return $"还差 {MilestoneAvatarMatches - validMatches} 场 · 头像纹样";
            if (validMatches < MilestoneCardMatches)
                return $"还差 {MilestoneCardMatches - validMatches} 场 · 名片底纹";
            if (validMatches < MilestoneTalismanMatches)
                return $"还差 {MilestoneTalismanMatches - validMatches} 场 · 青云玉符";
            return "本季三档展示外观已全部取得";
        }

        public static string CharacterPresetTitle(uint presetId, string serverName) =>
            !string.IsNullOrEmpty(serverName) ? serverName : presetId switch
            {
                101 => "破阵",
                102 => "控场",
                103 => "守心",
                _ => $"人物预设 {presetId}",
            };

        public static string PetPresetTitle(uint presetId, string serverName) =>
            !string.IsNullOrEmpty(serverName) ? serverName : presetId switch
            {
                201 => "破甲",
                202 => "灵咒",
                203 => "护主",
                _ => $"宝宝预设 {presetId}",
            };

        public static string PetDefaultSpeciesName(uint presetId) => presetId switch
        {
            201 => "金猊",
            202 => "灵狐",
            203 => "石灵",
            _ => "竞技灵宠",
        };
    }

    /// <summary>RPC 448 GetRankedProfile 的只读客户端快照，所有对象均为独立副本。</summary>
    public sealed class RankedProfileSnapshot
    {
        public RankedSeasonInfo CurrentSeason { get; }
        public RankedPlayerSeasonSummary CurrentProgress { get; }
        public RankedPresetSelection SelectedPresets { get; }
        public RankedCosmeticLoadout CosmeticLoadout { get; }
        public IReadOnlyList<RankedPresetView> CharacterPresets { get; }
        public IReadOnlyList<RankedPresetView> PetPresets { get; }
        public IReadOnlyList<RankedRewardEntitlement> Entitlements { get; }
        public IReadOnlyList<RankedPlayerSeasonSummary> History { get; }
        public IReadOnlyList<RankedSeasonInfo> PastSeasons { get; }
        public ulong ServerTimeMs { get; }

        public bool HasSeason => CurrentSeason != null && CurrentSeason.SeasonId != 0;
        public bool IsSeasonSettling => CurrentSeason != null && CurrentSeason.State == RankedSeasonState.Settling;
        public bool AnyPastSeasonSettling
        {
            get
            {
                if (IsSeasonSettling) return true;
                for (int i = 0; i < PastSeasons.Count; i++)
                {
                    if (PastSeasons[i].State == RankedSeasonState.Settling) return true;
                }
                return false;
            }
        }
        public uint RuleVersion => CurrentSeason != null && CurrentSeason.RuleVersion != 0 ? CurrentSeason.RuleVersion : 1u;
        public uint BattleConfigId => CurrentSeason != null && CurrentSeason.BattleConfigId != 0
            ? CurrentSeason.BattleConfigId : RankedFormatting.DefaultBattleConfigId;
        public uint WaitPromptSeconds => CurrentSeason != null && CurrentSeason.WaitPromptSeconds != 0
            ? CurrentSeason.WaitPromptSeconds : RankedFormatting.DefaultWaitPromptSeconds;

        private static readonly uint[] s_defaultAllowedSizes = { 1u, 3u };
        private static readonly uint[] s_defaultPrimarySizes = { 1u, 3u, 5u };
        private static readonly uint[] s_defaultSpecialSizes = { 2u, 4u };

        /// <summary>服务端权威开放中的正式会武人数档（冷启动默认 1V1 / 3V3）。</summary>
        public IReadOnlyList<uint> AllowedTeamSizes =>
            CurrentSeason != null && CurrentSeason.AllowedTeamSizes.Count > 0
                ? (IReadOnlyList<uint>)CurrentSeason.AllowedTeamSizes
                : s_defaultAllowedSizes;

        /// <summary>常驻主力人数档（1V1 / 3V3 / 5V5）。</summary>
        public IReadOnlyList<uint> PrimaryTeamSizes =>
            CurrentSeason != null && CurrentSeason.PrimaryTeamSizes.Count > 0
                ? (IReadOnlyList<uint>)CurrentSeason.PrimaryTeamSizes
                : s_defaultPrimarySizes;

        /// <summary>特殊玩法人数档（2V2 / 4V4，仅活动/帮会开关开启时开放）。</summary>
        public IReadOnlyList<uint> SpecialTeamSizes =>
            CurrentSeason != null && CurrentSeason.SpecialTeamSizes.Count > 0
                ? (IReadOnlyList<uint>)CurrentSeason.SpecialTeamSizes
                : s_defaultSpecialSizes;

        /// <summary>正式人宠会武使用本人真实养成，免费人宠仅在娱乐轮换（护卫救援 / 物法易势 5V5）启用。</summary>
        public bool UsesOwnProgression => CurrentSeason == null || CurrentSeason.UsesOwnProgression || CurrentSeason.SeasonId != 0;

        public uint DefaultOpenTeamSize => AllowedTeamSizes.Count > 0 ? AllowedTeamSizes[0] : RankedFormatting.DefaultTeamSize;

        public bool IsTeamSizeOpen(uint teamSize) => ContainsSize(AllowedTeamSizes, teamSize);
        public bool IsSpecialTeamSize(uint teamSize) => ContainsSize(SpecialTeamSizes, teamSize);

        private static bool ContainsSize(IReadOnlyList<uint> list, uint value)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }

        public RankedProfileSnapshot(
            RankedSeasonInfo currentSeason,
            RankedPlayerSeasonSummary currentProgress,
            RankedPresetSelection selectedPresets,
            RankedCosmeticLoadout cosmeticLoadout,
            IReadOnlyList<RankedPresetView> characterPresets,
            IReadOnlyList<RankedPresetView> petPresets,
            IReadOnlyList<RankedRewardEntitlement> entitlements,
            IReadOnlyList<RankedPlayerSeasonSummary> history,
            IReadOnlyList<RankedSeasonInfo> pastSeasons,
            ulong serverTimeMs)
        {
            CurrentSeason = currentSeason;
            CurrentProgress = currentProgress;
            SelectedPresets = selectedPresets ?? new RankedPresetSelection
            {
                CharacterPresetId = RankedFormatting.DefaultCharacterPresetId,
                PetPresetId = RankedFormatting.DefaultPetPresetId,
            };
            CosmeticLoadout = cosmeticLoadout ?? new RankedCosmeticLoadout();
            CharacterPresets = characterPresets;
            PetPresets = petPresets;
            Entitlements = entitlements;
            History = history;
            PastSeasons = pastSeasons;
            ServerTimeMs = serverTimeMs;
        }

        public static RankedProfileSnapshot FromResponse(GetRankedProfileResponse resp)
        {
            if (resp == null) return null;
            var chars = new List<RankedPresetView>();
            var pets = new List<RankedPresetView>();
            foreach (var preset in resp.Presets)
            {
                if (preset == null) continue;
                var clone = preset.Clone();
                if (clone.RoleKind == 1) chars.Add(clone);
                else if (clone.RoleKind == 2) pets.Add(clone);
            }
            chars.Sort((a, b) => a.Sort != b.Sort ? a.Sort.CompareTo(b.Sort) : a.PresetId.CompareTo(b.PresetId));
            pets.Sort((a, b) => a.Sort != b.Sort ? a.Sort.CompareTo(b.Sort) : a.PresetId.CompareTo(b.PresetId));

            var entitlements = new List<RankedRewardEntitlement>(resp.Entitlements.Count);
            foreach (var e in resp.Entitlements) if (e != null) entitlements.Add(e.Clone());

            var history = new List<RankedPlayerSeasonSummary>(resp.History.Count);
            foreach (var h in resp.History) if (h != null) history.Add(h.Clone());

            var past = new List<RankedSeasonInfo>(resp.PastSeasons.Count);
            foreach (var s in resp.PastSeasons) if (s != null) past.Add(s.Clone());

            return new RankedProfileSnapshot(
                resp.CurrentSeason?.Clone(),
                resp.CurrentProgress?.Clone(),
                resp.SelectedPresets?.Clone(),
                resp.CosmeticLoadout?.Clone(),
                chars,
                pets,
                entitlements,
                history,
                past,
                resp.ServerTimeMs);
        }

        public RankedProfileSnapshot WithPresets(RankedPresetSelection selection)
            => new RankedProfileSnapshot(
                CurrentSeason?.Clone(),
                CurrentProgress?.Clone(),
                selection?.Clone(),
                CosmeticLoadout?.Clone(),
                CharacterPresets,
                PetPresets,
                Entitlements,
                History,
                PastSeasons,
                ServerTimeMs);

        public RankedProfileSnapshot WithCosmetic(RankedCosmeticLoadout loadout)
            => new RankedProfileSnapshot(
                CurrentSeason?.Clone(),
                CurrentProgress?.Clone(),
                SelectedPresets?.Clone(),
                loadout?.Clone(),
                CharacterPresets,
                PetPresets,
                Entitlements,
                History,
                PastSeasons,
                ServerTimeMs);

        public RankedRewardEntitlement FindEntitlement(RankedRewardKind kind)
        {
            for (int i = 0; i < Entitlements.Count; i++)
            {
                if (Entitlements[i].Kind == kind) return Entitlements[i];
            }
            return null;
        }

        public bool HasEntitlement(RankedRewardKind kind) => FindEntitlement(kind) != null;
    }

    /// <summary>
    /// 论剑单局结算视图：清晰拆分两条权威真源：
    /// 1) scene 终局/补查带来的「日常材料额度增量」（入材料账，领取后才入包）；
    /// 2) match RPC 451 带来的「积分、段位、有效参与、季场次与展示外观权益」。
    /// 任一条未确认前严格显示「待确认」，绝不推测或本地算分。
    /// </summary>
    public sealed class RankedBattleSettlementState
    {
        public ulong BattleId { get; set; }
        public uint SeasonId { get; set; }
        public uint TeamSize { get; set; }
        public eBattleOutcome Outcome { get; set; }
        public uint PlayerTeamIndex { get; set; }
        public uint TotalRounds { get; set; }
        public bool IsVoid { get; set; }
        public bool FromRecovery { get; set; }

        // Scene 权威材料额度增量（仅记入可自选额度账，玩家需走既有领取才入包）
        public bool MaterialCreditConfirmed { get; set; }
        public uint MaterialCreditUnitsAdded { get; set; }
        public uint MaterialTradableUnitsAdded { get; set; }
        public bool MaterialPvpLossIneligible { get; set; }

        // Match RPC 451 权威结算结果
        public RankedBattleSettlementView AuthoritativeView { get; set; }
        public bool RatingLoading { get; set; }
        public string RatingError { get; set; } = string.Empty;

        public RankedMatchRatingStatus RatingStatus =>
            IsVoid ? RankedMatchRatingStatus.RankedMatchRatingVoid
            : AuthoritativeView != null ? AuthoritativeView.Status
            : RankedMatchRatingStatus.RankedMatchRatingPending;

        public bool IsRatingConfirmed => RatingStatus == RankedMatchRatingStatus.RankedMatchRatingConfirmed;
        public bool IsRatingPending => !IsVoid && RatingStatus == RankedMatchRatingStatus.RankedMatchRatingPending;
        public bool IsZeroRound => RatingStatus == RankedMatchRatingStatus.RankedMatchRatingZeroRound;

        public eBattleOutcome EffectiveOutcome =>
            AuthoritativeView != null && AuthoritativeView.Outcome != eBattleOutcome.BattleOutcomeOngoing
                ? AuthoritativeView.Outcome
                : Outcome;

        public uint EffectiveTeamSize =>
            AuthoritativeView != null && AuthoritativeView.TeamSize != 0
                ? AuthoritativeView.TeamSize
                : (TeamSize != 0 ? TeamSize : RankedFormatting.DefaultTeamSize);
    }
}
