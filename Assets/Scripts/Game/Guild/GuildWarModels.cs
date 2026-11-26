using System;
using System.Collections.Generic;
using System.Globalization;
using Guildpb;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Guild
{
    /// <summary>帮会全天帮战 V2 不可变快照与文案格式化（纯 C#，不依赖 UnityEngine，WebGL2/IL2CPP 安全）。</summary>
    public sealed class GuildWarStanceSnapshot
    {
        public bool PeaceMode { get; }
        public ulong PeaceReadyAtMs { get; }
        public bool OpenChallenge { get; }
        public ulong ProtectUntilMs { get; }
        public uint MyStreak { get; }
        public ulong MyWeeklyMerit { get; }
        public uint MyBountyPayoutsToday { get; }
        public uint MyBountyPayoutLimit { get; }
        public ulong BountyProtectUntilMs { get; }
        public ulong NextGarrisonRefreshMs { get; }
        public ulong NextTrackAtMs { get; }

        public GuildWarStanceSnapshot(GuildWarStanceView src)
        {
            if (src == null) return;
            PeaceMode = src.PeaceMode;
            PeaceReadyAtMs = src.PeaceReadyAtMs;
            OpenChallenge = src.OpenChallenge;
            ProtectUntilMs = src.ProtectUntilMs;
            MyStreak = src.MyStreak;
            MyWeeklyMerit = src.MyWeeklyMerit;
            MyBountyPayoutsToday = src.MyBountyPayoutsToday;
            MyBountyPayoutLimit = src.MyBountyPayoutLimit;
            BountyProtectUntilMs = src.BountyProtectUntilMs;
            NextGarrisonRefreshMs = src.NextGarrisonRefreshMs;
            NextTrackAtMs = src.NextTrackAtMs;
        }
    }

    public sealed class GuildWarBannerHonorSnapshot
    {
        public uint OccupiedLeylineId { get; }
        public IReadOnlyList<string> WeeklyMvps { get; }
        public IReadOnlyList<string> FirstWinNames { get; }
        public ulong FirstWinAtMs { get; }
        public IReadOnlyList<ulong> RivalGuildIds { get; }
        public IReadOnlyList<string> RivalGuildNames { get; }

        public GuildWarBannerHonorSnapshot(GuildWarBannerHonorView src)
        {
            if (src == null)
            {
                WeeklyMvps = Array.Empty<string>();
                FirstWinNames = Array.Empty<string>();
                RivalGuildIds = Array.Empty<ulong>();
                RivalGuildNames = Array.Empty<string>();
                return;
            }
            OccupiedLeylineId = src.OccupiedLeylineId;
            WeeklyMvps = new List<string>(src.WeeklyMvps).AsReadOnly();
            FirstWinNames = new List<string>(src.FirstWinNames).AsReadOnly();
            FirstWinAtMs = src.FirstWinAtMs;
            RivalGuildIds = new List<ulong>(src.RivalGuildIds).AsReadOnly();
            RivalGuildNames = new List<string>(src.RivalGuildNames).AsReadOnly();
        }
    }

    public sealed class GuildWarLeylineSnapshot
    {
        public uint LeylineId { get; }
        public string Name { get; }
        public uint SceneConfigId { get; }
        public ulong OwnerGuildId { get; }
        public string OwnerGuildName { get; }
        public uint BannerHp { get; }
        public uint BannerMaxHp { get; }
        public ulong ShieldUntilMs { get; }
        public uint ActiveBattleCount { get; }
        public bool UnderSiege { get; }
        public ulong FeaturedWatchBattleId { get; }
        public bool IsGoldLeyline { get; }
        public uint GarrisonCount { get; }
        public ulong RecommendedSceneId { get; }
        public uint RecommendedChannelNo { get; }

        public bool IsUnclaimed => OwnerGuildId == 0;
        public float HpRatio => BannerMaxHp == 0 ? 0f : Math.Min(1f, (float)BannerHp / BannerMaxHp);

        public GuildWarLeylineSnapshot(GuildWarLeylineView src)
        {
            if (src == null) { Name = string.Empty; OwnerGuildName = string.Empty; return; }
            LeylineId = src.LeylineId;
            Name = src.Name ?? string.Empty;
            SceneConfigId = src.SceneConfigId;
            OwnerGuildId = src.OwnerGuildId;
            OwnerGuildName = src.OwnerGuildName ?? string.Empty;
            BannerHp = src.BannerHp;
            BannerMaxHp = src.BannerMaxHp;
            ShieldUntilMs = src.ShieldUntilMs;
            ActiveBattleCount = src.ActiveBattleCount;
            UnderSiege = src.UnderSiege;
            FeaturedWatchBattleId = src.FeaturedWatchBattleId;
            IsGoldLeyline = src.IsGoldLeyline;
            GarrisonCount = src.GarrisonCount;
            RecommendedSceneId = src.RecommendedSceneId;
            RecommendedChannelNo = src.RecommendedChannelNo;
        }
    }

    public sealed class GuildWarMilestoneSnapshot
    {
        public uint MilestoneId { get; }
        public ulong RequiredMerit { get; }
        public ulong GuildMerit { get; }
        public bool Claimed { get; }
        public bool Claimable { get; }
        public string RewardPreview { get; }
        public ulong RewardSilver { get; }
        public ulong RewardContribution { get; }

        public GuildWarMilestoneSnapshot(GuildWarMilestoneView src)
        {
            if (src == null) { RewardPreview = string.Empty; return; }
            MilestoneId = src.MilestoneId;
            RequiredMerit = src.RequiredMerit;
            GuildMerit = src.GuildMerit;
            Claimed = src.Claimed;
            Claimable = src.Claimable;
            RewardPreview = src.RewardPreview ?? string.Empty;
            RewardSilver = src.RewardSilver;
            RewardContribution = src.RewardContribution;
        }
    }

    public sealed class GuildWarDividendSnapshot
    {
        public uint ClaimableCount { get; }
        public uint ClaimedThisWeek { get; }
        public uint WeeklyCap { get; }
        public uint GuildProducedThisWeek { get; }
        public uint GuildWeeklyCap { get; }
        public ulong MinPersonalMerit { get; }

        public GuildWarDividendSnapshot(GuildWarDividendView src)
        {
            if (src == null) return;
            ClaimableCount = src.ClaimableCount;
            ClaimedThisWeek = src.ClaimedThisWeek;
            WeeklyCap = src.WeeklyCap;
            GuildProducedThisWeek = src.GuildProducedThisWeek;
            GuildWeeklyCap = src.GuildWeeklyCap;
            MinPersonalMerit = src.MinPersonalMerit;
        }
    }

    public sealed class GuildWarStreakBountySnapshot
    {
        public ulong PlayerId { get; }
        public string PlayerName { get; }
        public ulong GuildId { get; }
        public string GuildName { get; }
        public uint Streak { get; }
        public ulong BonusContribution { get; }
        public ulong BonusSilver { get; }
        public bool InBattle { get; }
        public ulong BattleId { get; }

        public GuildWarStreakBountySnapshot(GuildWarStreakBountyView src)
        {
            if (src == null) { PlayerName = string.Empty; GuildName = string.Empty; return; }
            PlayerId = src.PlayerId;
            PlayerName = src.PlayerName ?? string.Empty;
            GuildId = src.GuildId;
            GuildName = src.GuildName ?? string.Empty;
            Streak = src.Streak;
            BonusContribution = src.BonusContribution;
            BonusSilver = src.BonusSilver;
            InBattle = src.InBattle;
            BattleId = src.BattleId;
        }
    }

    public sealed class GuildWarLiveTargetSnapshot
    {
        public ulong PlayerId { get; }
        public string PlayerName { get; }
        public ulong GuildId { get; }
        public string GuildName { get; }
        public uint Level { get; }
        public string PetName { get; }
        public uint PartySize { get; }
        public uint Streak { get; }
        public bool InBattle { get; }
        public ulong BattleId { get; }
        public uint LeylineId { get; }
        public bool IsRivalGuild { get; }
        public ulong ActiveBountyId { get; }

        public GuildWarLiveTargetSnapshot(GuildWarLiveTargetView src)
        {
            if (src == null) { PlayerName = string.Empty; GuildName = string.Empty; PetName = string.Empty; return; }
            PlayerId = src.PlayerId;
            PlayerName = src.PlayerName ?? string.Empty;
            GuildId = src.GuildId;
            GuildName = src.GuildName ?? string.Empty;
            Level = src.Level;
            PetName = src.PetName ?? string.Empty;
            PartySize = src.PartySize;
            Streak = src.Streak;
            InBattle = src.InBattle;
            BattleId = src.BattleId;
            LeylineId = src.LeylineId;
            IsRivalGuild = src.IsRivalGuild;
            ActiveBountyId = src.ActiveBountyId;
        }
    }

    public sealed class GuildWarGarrisonSlotSnapshot
    {
        public uint SlotIndex { get; }
        public ulong PlayerId { get; }
        public string PlayerName { get; }
        public uint Level { get; }
        public string AppearanceId { get; }
        public string PetName { get; }
        public uint PetTableId { get; }
        public ulong UpdatedMs { get; }

        public GuildWarGarrisonSlotSnapshot(GuildWarGarrisonSlotView src)
        {
            if (src == null) { PlayerName = string.Empty; AppearanceId = string.Empty; PetName = string.Empty; return; }
            SlotIndex = src.SlotIndex;
            PlayerId = src.PlayerId;
            PlayerName = src.PlayerName ?? string.Empty;
            Level = src.Level;
            AppearanceId = src.AppearanceId ?? string.Empty;
            PetName = src.PetName ?? string.Empty;
            PetTableId = src.PetTableId;
            UpdatedMs = src.UpdatedMs;
        }
    }

    public sealed class GuildWarEnemySnapshot
    {
        public ulong PlayerId { get; }
        public string PlayerName { get; }
        public ulong GuildId { get; }
        public string GuildName { get; }
        public string VictimName { get; }
        public GuildWarBattleKind BattleKind { get; }
        public ulong OccurredAtMs { get; }
        public bool CanPostBounty { get; }
        public uint BlockedTipId { get; }

        public GuildWarEnemySnapshot(GuildWarEnemyView src)
        {
            if (src == null) { PlayerName = string.Empty; GuildName = string.Empty; VictimName = string.Empty; return; }
            PlayerId = src.PlayerId;
            PlayerName = src.PlayerName ?? string.Empty;
            GuildId = src.GuildId;
            GuildName = src.GuildName ?? string.Empty;
            VictimName = src.VictimName ?? string.Empty;
            BattleKind = src.BattleKind;
            OccurredAtMs = src.OccurredAtMs;
            CanPostBounty = src.CanPostBounty;
            BlockedTipId = src.BlockedTipId;
        }
    }

    public sealed class GuildWarBountySnapshot
    {
        public ulong BountyId { get; }
        public ulong TargetPlayerId { get; }
        public string TargetName { get; }
        public ulong TargetGuildId { get; }
        public string TargetGuildName { get; }
        public ulong IssuerGuildId { get; }
        public string IssuerGuildName { get; }
        public GuildWarBountyTier Tier { get; }
        public ulong RewardSilver { get; }
        public bool PublicBoard { get; }
        public GuildWarBountyStatus Status { get; }
        public uint CounterKills { get; }
        public uint CounterKillsToTear { get; }
        public ulong ExpiresAtMs { get; }
        public uint LastSceneConfigId { get; }
        public uint LastChannelNo { get; }
        public ulong LastSceneId { get; }
        public bool TargetInSafeZone { get; }
        public bool TargetOnline { get; }
        public ulong NextTrackAtMs { get; }
        public bool CanDirectAttack { get; }

        public GuildWarBountySnapshot(GuildWarBountyView src, ulong lastSceneId = 0)
        {
            if (src == null) { TargetName = string.Empty; TargetGuildName = string.Empty; IssuerGuildName = string.Empty; return; }
            BountyId = src.BountyId;
            TargetPlayerId = src.TargetPlayerId;
            TargetName = src.TargetName ?? string.Empty;
            TargetGuildId = src.TargetGuildId;
            TargetGuildName = src.TargetGuildName ?? string.Empty;
            IssuerGuildId = src.IssuerGuildId;
            IssuerGuildName = src.IssuerGuildName ?? string.Empty;
            Tier = src.Tier;
            RewardSilver = src.RewardSilver;
            PublicBoard = src.PublicBoard;
            Status = src.Status;
            CounterKills = src.CounterKills;
            CounterKillsToTear = src.CounterKillsToTear;
            ExpiresAtMs = src.ExpiresAtMs;
            LastSceneConfigId = src.LastSceneConfigId;
            LastChannelNo = src.LastChannelNo;
            LastSceneId = lastSceneId;
            TargetInSafeZone = src.TargetInSafeZone;
            TargetOnline = src.TargetOnline;
            NextTrackAtMs = src.NextTrackAtMs;
            CanDirectAttack = src.CanDirectAttack;
        }

        public GuildWarBountySnapshot WithTrackResult(TrackGuildWarBountyResponse track)
        {
            if (track == null) return this;
            var view = new GuildWarBountyView
            {
                BountyId = BountyId,
                TargetPlayerId = TargetPlayerId,
                TargetName = TargetName,
                TargetGuildId = TargetGuildId,
                TargetGuildName = TargetGuildName,
                IssuerGuildId = IssuerGuildId,
                IssuerGuildName = IssuerGuildName,
                Tier = Tier,
                RewardSilver = RewardSilver,
                PublicBoard = PublicBoard,
                Status = Status,
                CounterKills = CounterKills,
                CounterKillsToTear = CounterKillsToTear,
                ExpiresAtMs = ExpiresAtMs,
                LastSceneConfigId = track.SceneConfigId,
                LastChannelNo = track.ChannelNo,
                TargetInSafeZone = track.InSafeZone,
                TargetOnline = track.TargetOnline,
                NextTrackAtMs = track.NextTrackAtMs,
                CanDirectAttack = CanDirectAttack,
            };
            return new GuildWarBountySnapshot(view, track.SceneId);
        }
    }

    public sealed class GuildWarRecentLogSnapshot
    {
        public ulong BattleId { get; }
        public GuildWarBattleKind WarBattleKind { get; }
        public uint LeylineId { get; }
        public string AttackerGuildName { get; }
        public string DefenderGuildName { get; }
        public string AttackerSummary { get; }
        public string DefenderSummary { get; }
        public uint Outcome { get; }
        public uint Rounds { get; }
        public int BannerHpDelta { get; }
        public uint BountyOutcomeTag { get; }
        public bool SelfParticipated { get; }
        public ulong FinishedAtMs { get; }

        public GuildWarRecentLogSnapshot(GuildWarRecentLogView src)
        {
            if (src == null)
            {
                AttackerGuildName = string.Empty;
                DefenderGuildName = string.Empty;
                AttackerSummary = string.Empty;
                DefenderSummary = string.Empty;
                return;
            }
            BattleId = src.BattleId;
            WarBattleKind = src.WarBattleKind;
            LeylineId = src.LeylineId;
            AttackerGuildName = src.AttackerGuildName ?? string.Empty;
            DefenderGuildName = src.DefenderGuildName ?? string.Empty;
            AttackerSummary = src.AttackerSummary ?? string.Empty;
            DefenderSummary = src.DefenderSummary ?? string.Empty;
            Outcome = src.Outcome;
            Rounds = src.Rounds;
            BannerHpDelta = src.BannerHpDelta;
            BountyOutcomeTag = src.BountyOutcomeTag;
            SelfParticipated = src.SelfParticipated;
            FinishedAtMs = src.FinishedAtMs;
        }
    }

    public sealed class GuildWarFeudSnapshot
    {
        public ulong FeudId { get; }
        public ulong IssuerGuildId { get; }
        public string IssuerGuildName { get; }
        public ulong TargetGuildId { get; }
        public string TargetGuildName { get; }
        public GuildWarFeudStatus Status { get; }
        public uint IssuerWins { get; }
        public uint TargetWins { get; }
        public ulong IssuerMerit { get; }
        public ulong TargetMerit { get; }
        public ulong CreatedAtMs { get; }
        public ulong AcceptDeadlineMs { get; }
        public ulong StartedAtMs { get; }
        public ulong EndsAtMs { get; }
        public ulong WinnerGuildId { get; }

        public bool IsPending => Status == GuildWarFeudStatus.Pending;
        public bool IsActive => Status == GuildWarFeudStatus.Active;
        public bool IsIssuer(ulong myGuildId) => myGuildId != 0 && IssuerGuildId == myGuildId;
        public ulong EnemyGuildId(ulong myGuildId) => IsIssuer(myGuildId) ? TargetGuildId : IssuerGuildId;
        public string EnemyGuildName(ulong myGuildId) => IsIssuer(myGuildId) ? TargetGuildName : IssuerGuildName;
        public uint MyWins(ulong myGuildId) => IsIssuer(myGuildId) ? IssuerWins : TargetWins;
        public uint EnemyWins(ulong myGuildId) => IsIssuer(myGuildId) ? TargetWins : IssuerWins;

        public GuildWarFeudSnapshot(GuildWarFeudView src)
        {
            if (src == null) { IssuerGuildName = string.Empty; TargetGuildName = string.Empty; return; }
            FeudId = src.FeudId;
            IssuerGuildId = src.IssuerGuildId;
            IssuerGuildName = src.IssuerGuildName ?? string.Empty;
            TargetGuildId = src.TargetGuildId;
            TargetGuildName = src.TargetGuildName ?? string.Empty;
            Status = src.Status;
            IssuerWins = src.IssuerWins;
            TargetWins = src.TargetWins;
            IssuerMerit = src.IssuerMerit;
            TargetMerit = src.TargetMerit;
            CreatedAtMs = src.CreatedAtMs;
            AcceptDeadlineMs = src.AcceptDeadlineMs;
            StartedAtMs = src.StartedAtMs;
            EndsAtMs = src.EndsAtMs;
            WinnerGuildId = src.WinnerGuildId;
        }
    }

    public sealed class GuildWarOverviewSnapshot
    {
        public ulong ServerTimeMs { get; }
        public uint WeekKey { get; }
        public ulong NextWeeklyResetMs { get; }
        public uint GoldLeylineId { get; }
        public ulong GoldLeylineEndsAtMs { get; }
        public GuildWarStanceSnapshot MyStance { get; }
        public GuildWarBannerHonorSnapshot BannerHonor { get; }
        public IReadOnlyList<GuildWarLeylineSnapshot> Leylines { get; }
        public IReadOnlyList<GuildWarMilestoneSnapshot> Milestones { get; }
        public GuildWarDividendSnapshot Dividend { get; }
        public IReadOnlyList<GuildWarStreakBountySnapshot> StreakBounties { get; }
        public IReadOnlyList<GuildWarLiveTargetSnapshot> LiveTargets { get; }
        public IReadOnlyList<GuildWarGarrisonSlotSnapshot> GarrisonSlots { get; }
        public IReadOnlyList<GuildWarEnemySnapshot> Enemies { get; }
        public IReadOnlyList<GuildWarBountySnapshot> Bounties { get; }
        public IReadOnlyList<GuildWarRecentLogSnapshot> RecentLogs { get; }
        public ulong ActiveBattleId { get; }
        public ulong ContributionBalance { get; }
        public uint PublicBountyUsedToday { get; }
        public uint PublicBountyDailyLimit { get; }
        public GuildWarFeudSnapshot ActiveFeud { get; }
        public string LastFeudSummary { get; }
        public ulong NextFeudIssueAtMs { get; }

        public bool HasClaimableReward
        {
            get
            {
                if (Dividend != null && Dividend.ClaimableCount > 0) return true;
                for (int i = 0; i < Milestones.Count; i++)
                    if (Milestones[i].Claimable && !Milestones[i].Claimed) return true;
                return false;
            }
        }

        public GuildWarOverviewSnapshot(GetGuildWarOverviewResponse resp)
        {
            if (resp == null) throw new ArgumentNullException(nameof(resp));
            ServerTimeMs = resp.ServerTimeMs;
            WeekKey = resp.WeekKey;
            NextWeeklyResetMs = resp.NextWeeklyResetMs;
            GoldLeylineId = resp.GoldLeylineId;
            GoldLeylineEndsAtMs = resp.GoldLeylineEndsAtMs;
            MyStance = new GuildWarStanceSnapshot(resp.MyStance);
            BannerHonor = new GuildWarBannerHonorSnapshot(resp.BannerHonor);
            Leylines = MapList(resp.Leylines, x => new GuildWarLeylineSnapshot(x));
            Milestones = MapList(resp.Milestones, x => new GuildWarMilestoneSnapshot(x));
            Dividend = new GuildWarDividendSnapshot(resp.Dividend);
            StreakBounties = MapList(resp.StreakBounties, x => new GuildWarStreakBountySnapshot(x));
            LiveTargets = MapList(resp.LiveTargets, x => new GuildWarLiveTargetSnapshot(x));
            GarrisonSlots = MapList(resp.GarrisonSlots, x => new GuildWarGarrisonSlotSnapshot(x));
            Enemies = MapList(resp.Enemies, x => new GuildWarEnemySnapshot(x));
            Bounties = MapList(resp.Bounties, x => new GuildWarBountySnapshot(x));
            RecentLogs = MapList(resp.RecentLogs, x => new GuildWarRecentLogSnapshot(x));
            ActiveBattleId = resp.ActiveBattleId;
            ContributionBalance = resp.ContributionBalance;
            PublicBountyUsedToday = resp.PublicBountyUsedToday;
            PublicBountyDailyLimit = resp.PublicBountyDailyLimit;
            ActiveFeud = resp.ActiveFeud != null && resp.ActiveFeud.FeudId != 0
                ? new GuildWarFeudSnapshot(resp.ActiveFeud) : null;
            LastFeudSummary = resp.LastFeudSummary ?? string.Empty;
            NextFeudIssueAtMs = resp.NextFeudIssueAtMs;
        }

        private GuildWarOverviewSnapshot(
            GuildWarOverviewSnapshot src,
            GuildWarStanceSnapshot stance = null,
            IReadOnlyList<GuildWarGarrisonSlotSnapshot> garrisons = null,
            IReadOnlyList<GuildWarMilestoneSnapshot> milestones = null,
            GuildWarDividendSnapshot dividend = null,
            IReadOnlyList<GuildWarBountySnapshot> bounties = null,
            bool replaceFeud = false,
            GuildWarFeudSnapshot feud = null,
            ulong? nextFeudIssueAtMs = null)
        {
            ServerTimeMs = src.ServerTimeMs;
            WeekKey = src.WeekKey;
            NextWeeklyResetMs = src.NextWeeklyResetMs;
            GoldLeylineId = src.GoldLeylineId;
            GoldLeylineEndsAtMs = src.GoldLeylineEndsAtMs;
            MyStance = stance ?? src.MyStance;
            BannerHonor = src.BannerHonor;
            Leylines = src.Leylines;
            Milestones = milestones ?? src.Milestones;
            Dividend = dividend ?? src.Dividend;
            StreakBounties = src.StreakBounties;
            LiveTargets = src.LiveTargets;
            GarrisonSlots = garrisons ?? src.GarrisonSlots;
            Enemies = src.Enemies;
            Bounties = bounties ?? src.Bounties;
            RecentLogs = src.RecentLogs;
            ActiveBattleId = src.ActiveBattleId;
            ContributionBalance = src.ContributionBalance;
            PublicBountyUsedToday = src.PublicBountyUsedToday;
            PublicBountyDailyLimit = src.PublicBountyDailyLimit;
            ActiveFeud = replaceFeud ? feud : src.ActiveFeud;
            LastFeudSummary = src.LastFeudSummary;
            NextFeudIssueAtMs = nextFeudIssueAtMs ?? src.NextFeudIssueAtMs;
        }

        public GuildWarOverviewSnapshot WithFeud(GuildWarFeudView feud, ulong nextIssueAtMs = 0)
        {
            GuildWarFeudSnapshot snap = feud != null && feud.FeudId != 0
                && (feud.Status == GuildWarFeudStatus.Pending || feud.Status == GuildWarFeudStatus.Active)
                ? new GuildWarFeudSnapshot(feud)
                : null;
            return new GuildWarOverviewSnapshot(this, replaceFeud: true, feud: snap,
                nextFeudIssueAtMs: nextIssueAtMs != 0 ? nextIssueAtMs : (ulong?)null);
        }

        public GuildWarOverviewSnapshot WithStance(GuildWarStanceView stance) =>
            new GuildWarOverviewSnapshot(this, stance: new GuildWarStanceSnapshot(stance));

        public GuildWarOverviewSnapshot WithGarrisons(IEnumerable<GuildWarGarrisonSlotView> slots) =>
            new GuildWarOverviewSnapshot(this, garrisons: MapList(slots, x => new GuildWarGarrisonSlotSnapshot(x)));

        public GuildWarOverviewSnapshot WithMilestone(GuildWarMilestoneView updated)
        {
            if (updated == null) return this;
            var list = new List<GuildWarMilestoneSnapshot>(Milestones.Count);
            bool replaced = false;
            for (int i = 0; i < Milestones.Count; i++)
            {
                if (Milestones[i].MilestoneId == updated.MilestoneId)
                {
                    list.Add(new GuildWarMilestoneSnapshot(updated));
                    replaced = true;
                }
                else list.Add(Milestones[i]);
            }
            if (!replaced) list.Add(new GuildWarMilestoneSnapshot(updated));
            return new GuildWarOverviewSnapshot(this, milestones: list.AsReadOnly());
        }

        public GuildWarOverviewSnapshot WithDividend(GuildWarDividendView updated) =>
            updated == null ? this : new GuildWarOverviewSnapshot(this, dividend: new GuildWarDividendSnapshot(updated));

        public GuildWarOverviewSnapshot WithUpsertedBounty(GuildWarBountySnapshot updated)
        {
            if (updated == null) return this;
            var list = new List<GuildWarBountySnapshot>(Bounties.Count + 1);
            bool replaced = false;
            for (int i = 0; i < Bounties.Count; i++)
            {
                if (Bounties[i].BountyId == updated.BountyId)
                {
                    list.Add(updated);
                    replaced = true;
                }
                else list.Add(Bounties[i]);
            }
            if (!replaced) list.Insert(0, updated);
            return new GuildWarOverviewSnapshot(this, bounties: list.AsReadOnly());
        }

        private static IReadOnlyList<TOut> MapList<TIn, TOut>(IEnumerable<TIn> src, Func<TIn, TOut> map)
        {
            var list = new List<TOut>();
            if (src != null)
            {
                foreach (var item in src) list.Add(map(item));
            }
            return list.AsReadOnly();
        }
    }

    public sealed class GuildWarBattleSettlementSnapshot
    {
        public bool Settled { get; }
        public ulong BattleId { get; }
        public GuildWarBattleKind WarBattleKind { get; }
        public uint Outcome { get; }
        public uint Rounds { get; }
        public uint MaterialUnitsAdded { get; }
        public uint MaterialTradableAdded { get; }
        public bool MaterialPvpLossIneligible { get; }
        public ulong ContributionGain { get; }
        public ulong SilverGain { get; }
        public ulong GuildFundsGain { get; }
        public ulong MeritGain { get; }
        public uint StreakAfter { get; }
        public ulong BountyJackpotContribution { get; }
        public ulong BountyJackpotSilver { get; }
        public uint BountyOutcomeTag { get; }
        public uint DiminishPercent { get; }
        public uint LeylineId { get; }
        public uint BannerHpBefore { get; }
        public uint BannerHpAfter { get; }
        public bool BannerShielded { get; }
        public bool CanPostBounty { get; }
        public ulong BountyCandidatePlayerId { get; }
        public string BountyCandidateName { get; }
        public bool GoldLeylineBonus { get; }
        public bool RivalryBonus { get; }
        public bool FeudBonus { get; }

        public GuildWarBattleSettlementSnapshot(GetGuildWarBattleResultResponse resp)
        {
            if (resp == null) { BountyCandidateName = string.Empty; return; }
            Settled = resp.Settled;
            BattleId = resp.BattleId;
            WarBattleKind = resp.WarBattleKind;
            Outcome = resp.Outcome;
            Rounds = resp.Rounds;
            MaterialUnitsAdded = resp.MaterialUnitsAdded;
            MaterialTradableAdded = resp.MaterialTradableAdded;
            MaterialPvpLossIneligible = resp.MaterialPvpLossIneligible;
            ContributionGain = resp.ContributionGain;
            SilverGain = resp.SilverGain;
            GuildFundsGain = resp.GuildFundsGain;
            MeritGain = resp.MeritGain;
            StreakAfter = resp.StreakAfter;
            BountyJackpotContribution = resp.BountyJackpotContribution;
            BountyJackpotSilver = resp.BountyJackpotSilver;
            BountyOutcomeTag = resp.BountyOutcomeTag;
            DiminishPercent = resp.DiminishPercent;
            LeylineId = resp.LeylineId;
            BannerHpBefore = resp.BannerHpBefore;
            BannerHpAfter = resp.BannerHpAfter;
            BannerShielded = resp.BannerShielded;
            CanPostBounty = resp.CanPostBounty;
            BountyCandidatePlayerId = resp.BountyCandidatePlayerId;
            BountyCandidateName = resp.BountyCandidateName ?? string.Empty;
            GoldLeylineBonus = resp.GoldLeylineBonus;
            RivalryBonus = resp.RivalryBonus;
            FeudBonus = resp.FeudBonus;
        }
    }

    public sealed class GuildWarAlertSnapshot
    {
        public GuildWarAlertKind Kind { get; }
        public uint LeylineId { get; }
        public uint SceneConfigId { get; }
        public uint ChannelNo { get; }
        public string AttackerGuildName { get; }
        public string DefenderGuildName { get; }
        public string ActorName { get; }
        public string TargetName { get; }
        public uint BannerHpPercent { get; }
        public ulong BountyId { get; }
        public ulong BattleId { get; }
        public ulong RewardSilver { get; }
        public uint StreakCount { get; }
        public ulong FeudId { get; }
        public uint IssuerWins { get; }
        public uint TargetWins { get; }

        public GuildWarAlertSnapshot(GuildWarAlertS2C msg)
        {
            if (msg == null)
            {
                AttackerGuildName = string.Empty;
                DefenderGuildName = string.Empty;
                ActorName = string.Empty;
                TargetName = string.Empty;
                return;
            }
            Kind = msg.Kind;
            LeylineId = msg.LeylineId;
            SceneConfigId = msg.SceneConfigId;
            ChannelNo = msg.ChannelNo;
            AttackerGuildName = msg.AttackerGuildName ?? string.Empty;
            DefenderGuildName = msg.DefenderGuildName ?? string.Empty;
            ActorName = msg.ActorName ?? string.Empty;
            TargetName = msg.TargetName ?? string.Empty;
            BannerHpPercent = msg.BannerHpPercent;
            BountyId = msg.BountyId;
            BattleId = msg.BattleId;
            RewardSilver = msg.RewardSilver;
            StreakCount = msg.StreakCount;
            FeudId = msg.FeudId;
            IssuerWins = msg.IssuerWins;
            TargetWins = msg.TargetWins;
        }
    }

    public sealed class GuildWarFieldTargetSnapshot
    {
        public ulong PlayerId { get; }
        public string PlayerName { get; }
        public ulong GuildId { get; }
        public string GuildName { get; }
        public bool PeaceMode { get; }
        public ulong ProtectUntilMs { get; }
        public uint Streak { get; }
        public bool InBattle { get; }
        public ulong BattleId { get; }
        public bool IsRivalGuild { get; }
        public ulong ActiveBountyId { get; }
        public ulong ActiveBountyRewardSilver { get; }
        public GuildWarBountyTier ActiveBountyTier { get; }
        public bool CanAttackNow { get; }

        public GuildWarFieldTargetSnapshot(FieldWarTargetBrief src)
        {
            if (src == null) { PlayerName = string.Empty; GuildName = string.Empty; return; }
            PlayerId = src.PlayerId;
            PlayerName = src.PlayerName ?? string.Empty;
            GuildId = src.GuildId;
            GuildName = src.GuildName ?? string.Empty;
            PeaceMode = src.PeaceMode;
            ProtectUntilMs = src.ProtectUntilMs;
            Streak = src.Streak;
            InBattle = src.InBattle;
            BattleId = src.BattleId;
            IsRivalGuild = src.IsRivalGuild;
            ActiveBountyId = src.ActiveBountyId;
            ActiveBountyRewardSilver = src.ActiveBountyRewardSilver;
            ActiveBountyTier = src.ActiveBountyTier;
            CanAttackNow = src.CanAttackNow;
        }
    }

    /// <summary>帮战 V2 权威 Tip 码、灵脉/野外地图名、追杀档位与倒计时文本辅助。</summary>
    public static class GuildWarText
    {
        public static string LeylineName(uint leylineId) => leylineId switch
        {
            1 => "八景岛 · 灵脉",
            2 => "碧渊水宫 · 灵脉",
            3 => "月汐湾 · 灵脉",
            _ => "未占灵脉",
        };

        public static string SceneName(uint sceneConfigId) => sceneConfigId switch
        {
            1 => "归元城（安全区）",
            2 => "八景岛",
            3 => "月汐湾",
            5 => "碧渊水宫",
            _ => sceneConfigId == 0 ? "行踪不明" : "野外区域#" + sceneConfigId.ToString(CultureInfo.InvariantCulture),
        };

        public static string BattleKindLabel(GuildWarBattleKind kind) => kind switch
        {
            GuildWarBattleKind.PanelPvp => "真人直打",
            GuildWarBattleKind.GarrisonSiege => "守旗踢馆",
            GuildWarBattleKind.FieldRoad => "野外路遇",
            GuildWarBattleKind.LeylinePvp => "灵脉攻防",
            GuildWarBattleKind.BountyHunt => "追杀结单",
            _ => "帮会争锋",
        };

        public static string BountyTierLabel(GuildWarBountyTier tier) => tier switch
        {
            GuildWarBountyTier.NormalSilver => "普通追杀令（5,000 银两 · 10%税）",
            GuildWarBountyTier.HeavySilver => "重金追杀令（15,000 银两 · 10%税）",
            GuildWarBountyTier.GuildPublic => "帮会公费追杀令（2,000 帮会资金）",
            _ => "追杀令",
        };

        public static string BountyStatusLabel(GuildWarBountyStatus status) => status switch
        {
            GuildWarBountyStatus.EscrowPending => "扣款确认中",
            GuildWarBountyStatus.Active => "追杀中",
            GuildWarBountyStatus.Completed => "已销单",
            GuildWarBountyStatus.TornByTarget => "已被反杀撕单",
            GuildWarBountyStatus.Expired => "已过期退本",
            _ => "未知状态",
        };

        public static string FormatCountdown(ulong targetMs, ulong nowMs)
        {
            if (targetMs <= nowMs) return "00:00";
            ulong sec = (targetMs - nowMs + 999UL) / 1000UL;
            ulong hours = sec / 3600UL;
            ulong mins = (sec % 3600UL) / 60UL;
            ulong rem = sec % 60UL;
            if (hours > 0)
                return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", hours, mins, rem);
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", mins, rem);
        }

        public static string FormatAlert(GuildWarAlertSnapshot alert)
        {
            if (alert == null) return string.Empty;
            return alert.Kind switch
            {
                GuildWarAlertKind.LeylineUnderSiege =>
                    $"本帮【{LeylineName(alert.LeylineId)}】正被【{alert.AttackerGuildName}】踢馆（剩余旗势 {alert.BannerHpPercent}%）",
                GuildWarAlertKind.LeylineCaptured =>
                    $"【{alert.AttackerGuildName}】攻占了【{LeylineName(alert.LeylineId)}】！",
                GuildWarAlertKind.PursuerEnteredMap =>
                    $"【{alert.AttackerGuildName}】的追兵已踏入【{SceneName(alert.SceneConfigId)} · {alert.ChannelNo}线】，请小心迎战或暂避！",
                GuildWarAlertKind.BountyResolved =>
                    $"江湖追杀令已结案：{alert.ActorName} 与 {alert.TargetName} 交锋分出胜负。",
                GuildWarAlertKind.StreakBounty => alert.RewardSilver > 0
                    ? $"终结连胜！【{alert.AttackerGuildName}·{alert.ActorName}】斩落【{alert.DefenderGuildName}·{alert.TargetName}】{alert.StreakCount}连胜，喜提悬赏 {alert.RewardSilver} 银两！"
                    : $"连胜封神！【{alert.AttackerGuildName}·{alert.ActorName}】已达 {alert.StreakCount} 连胜，击破享高额悬赏！",
                GuildWarAlertKind.FeudChallenge =>
                    $"宿敌战书！【{alert.AttackerGuildName}】向本帮下发 2 小时帮会约战战书，帮主/长老 15 分钟内可接战应战！",
                GuildWarAlertKind.FeudStarted =>
                    $"宿敌约战打响！【{alert.AttackerGuildName}】VS【{alert.DefenderGuildName}】2 小时约战开启（收益 +25% · 前 3 场免递减）！",
                GuildWarAlertKind.FeudEnded =>
                    $"宿敌约战收官：【{alert.AttackerGuildName}】{alert.IssuerWins}:{alert.TargetWins}【{alert.DefenderGuildName}】！",
                _ => "帮会争锋战况已更新。",
            };
        }

        public static string DescribeError(TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return string.Empty;
            if (tip.Id == (uint)common_error.KFeatureUnavailable)
                return "帮会全天帮战服务当前尚未开启，请稍后关注帮会公告。";
            if (tip.Id == (uint)common_error.KServiceUnavailable)
                return "帮战服务暂不可用，请稍后重试。";
            if (tip.Id == (uint)common_error.KInvalidParameter)
                return "请求参数无效，请刷新页面后重试。";
            return (guild_error)tip.Id switch
            {
                guild_error.KGuildNotInGuild => "请先加入帮会后再参与帮会争锋。",
                guild_error.KGuildNoPermission => "权限不足，仅帮主或长老可执行此操作。",
                guild_error.KGuildWarNotOpen => "当前暂未开启帮会全天帮战。",
                guild_error.KGuildWarLevelTooLow => "角色等级尚未达到帮战参与门槛。",
                guild_error.KGuildWarSameGuild => "不能对本帮同道发起帮战或追杀令。",
                guild_error.KGuildWarTargetPeaceMode => "对方当前处于和平免战模式，无法发起帮战。",
                guild_error.KGuildWarSelfPeaceMode => "你当前处于和平免战模式，请先切换为战意模式。",
                guild_error.KGuildWarPeaceCooldown => "脱战满 60 秒后方可切换和平免战（或返回归元城立即生效）。",
                guild_error.KGuildWarTargetProtected => "对方处于战后 10 秒保护期，请稍后再试。",
                guild_error.KGuildWarTargetInBattle => "目标刚刚进入激战，你可以选择实时观战或换个目标。",
                guild_error.KGuildWarTargetOffline => "目标当前离线，暂无法迎战。",
                guild_error.KGuildWarSafeZone => "归元城主城为永久安全区，不可强制点战。",
                guild_error.KGuildWarTeamInvalid => "队伍成员状态不符合帮战要求（需全员同帮、达级且未开免战）。",
                guild_error.KGuildWarTeamSizeExceedsRoadLimit => "野外普通路段人数差受限，请前往灵脉战区集结或调整队伍人数。",
                guild_error.KGuildWarLeylineNotFound => "指定灵脉不存在，请刷新战况。",
                guild_error.KGuildWarGarrisonFull => "本帮 5 个守旗阵槽位已满，可由帮主或长老调整槽位。",
                guild_error.KGuildWarGarrisonCooldown => "守旗阵快照刷新冷却 60 秒中，请稍后再试。",
                guild_error.KGuildWarMilestoneLocked => "未达本周战功档位或个人战功门槛，暂不可领取。",
                guild_error.KGuildWarMilestoneClaimed => "本周已领取该档位战功宝箱。",
                guild_error.KGuildWarDividendEmpty => "当前暂无可领取的灵脉分红（或已达本周个人领取上限）。",
                guild_error.KGuildWarBountyInvalidTarget => "该目标不符合追杀令发布条件。",
                guild_error.KGuildWarBountyAlreadyActive => "该目标当前已有生效中的追杀令（同目标同时限 1 单）。",
                guild_error.KGuildWarBountyLimit => "追杀令发布次数已达上限，请明日再试。",
                guild_error.KGuildWarBountyNotFound => "未找到对应追杀令，可能已结单或过期。",
                guild_error.KGuildWarTrackCooldown => "追踪行踪冷却 15 秒中，请稍后再试。",
                guild_error.KGuildWarServiceBusy => "帮战服务繁忙，本次未扣费，请稍后重试。",
                guild_error.KGuildWarFeudCooldown => "本帮宿敌约战战书仍在 24 小时冷却中，请稍后再发。",
                guild_error.KGuildWarFeudAlreadyActive => "本帮或对方帮会已有进行中或待应答的约战战书。",
                guild_error.KGuildWarFeudNotFound => "约战战书不存在、已过期或已处理。",
                guild_error.KGuildWarFeudInvalidTarget => "不能对本帮或不存在的帮会下发约战战书。",
                _ => TipFaults.IsFault(tip.Id)
                    ? $"帮战服务繁忙（错误码 {tip.Id}），请稍后重试。"
                    : $"帮战操作未完成（错误码 {tip.Id}）。",
            };
        }
    }
}
