using System;
using System.Collections.Generic;
using System.Globalization;
using Match;

namespace MmorpgClient.Game.Tournament
{
    public enum TournamentTab
    {
        Overview = 0,
        Squad = 1,
        Standings = 2,
        Rewards = 3,
        Hall = 4,
    }

    public enum TournamentBoardMode
    {
        Ranking = 0,
        Bracket = 1,
    }

    public enum TournamentModalKind
    {
        None = 0,
        CreateSquad = 1,
        InviteMember = 2,
        ConfirmLineup = 3,
        ConfirmLock = 4,
    }

    public enum TournamentPrimaryCtaKind
    {
        None = 0,
        CreateSquad = 1,
        RegisterSquad = 2,
        StartQualifierMatch = 3,
        StartKnockoutMatch = 4,
        ClaimReward = 5,
        PreviewSquad = 6,
    }

    public readonly struct TournamentPrimaryCta
    {
        public TournamentPrimaryCtaKind Kind { get; }
        public string Label { get; }
        public string Note { get; }
        public bool Interactable { get; }
        public ulong MatchId { get; }

        public TournamentPrimaryCta(
            TournamentPrimaryCtaKind kind,
            string label,
            string note,
            bool interactable,
            ulong matchId = 0UL)
        {
            Kind = kind;
            Label = label ?? string.Empty;
            Note = note ?? string.Empty;
            Interactable = interactable;
            MatchId = matchId;
        }
    }

    public readonly struct TournamentTipRecovery
    {
        public bool KeepCreateModalInline { get; }
        public bool KeepLineupModalInline { get; }
        public bool AutoRefreshOverviewOnce { get; }
        public bool ShowNotOpenEmptyState { get; }

        public TournamentTipRecovery(
            bool keepCreateModalInline,
            bool keepLineupModalInline,
            bool autoRefreshOverviewOnce,
            bool showNotOpenEmptyState)
        {
            KeepCreateModalInline = keepCreateModalInline;
            KeepLineupModalInline = keepLineupModalInline;
            AutoRefreshOverviewOnce = autoRefreshOverviewOnce;
            ShowNotOpenEmptyState = showNotOpenEmptyState;
        }
    }

    public readonly struct TournamentSeasonChoice
    {
        public uint SeasonId { get; }
        public string Label { get; }
        public bool IsCurrent { get; }

        public TournamentSeasonChoice(uint seasonId, string label, bool isCurrent)
        {
            SeasonId = seasonId;
            Label = label ?? string.Empty;
            IsCurrent = isCurrent;
        }
    }

    /// <summary>
    /// 《群仙问道赛》纯 C# UI 状态推导模型（不依赖 UnityEngine，便于 EditMode / NUnit 离线全覆盖单测）：
    /// - 维护页签、榜单分页/历史赛季选择、四个轻弹窗与出战阵容草稿；
    /// - 使用独立单调时钟差校准服务端 ServerTimeMs 倒计时（严禁依赖本机系统时间或直接传 Time.realtimeSinceStartup）；
    /// - 按服务端快照推导阶段主 CTA、主城 HUD 轻提示与 15 个赛事 Tip 的恢复行为。
    /// </summary>
    public sealed class TournamentUiModel
    {
        public const int MinSquadNameChars = 2;
        public const int MaxSquadNameChars = 12;
        public const uint DefaultPageSize = 20u;

        public TournamentTab ActiveTab { get; set; } = TournamentTab.Overview;
        public TournamentBoardMode BoardMode { get; set; } = TournamentBoardMode.Ranking;
        public uint SelectedStandingsSeasonId { get; set; } = 0u;
        public uint StandingsPage { get; set; } = 1u;

        public TournamentModalKind ActiveModal { get; private set; } = TournamentModalKind.None;
        public ulong PendingMatchId { get; private set; }
        public bool LineupModalIsForMatchStart { get; private set; }

        public string CreateNameDraft { get; set; } = string.Empty;
        public string InvitePlayerIdDraft { get; set; } = string.Empty;
        public string ModalInlineError { get; set; } = string.Empty;
        public bool ShowNotOpenState { get; set; }

        private readonly List<ulong> _lineupDraft = new List<ulong>();
        private TournamentOverviewSnapshot _observedOverview;
        private float _overviewObservedRealtime;

        public IReadOnlyList<ulong> LineupDraft => _lineupDraft;

        public void ResetSession()
        {
            ActiveTab = TournamentTab.Overview;
            BoardMode = TournamentBoardMode.Ranking;
            SelectedStandingsSeasonId = 0u;
            StandingsPage = 1u;
            CloseModal();
            ShowNotOpenState = false;
            _observedOverview = null;
            _overviewObservedRealtime = 0f;
        }

        /// <summary>收到新 Overview 引用时记录对应单调秒表时刻，用于后续无漂移倒计时。</summary>
        public void ObserveOverview(TournamentOverviewSnapshot overview, float nowRealtimeSeconds)
        {
            if (ReferenceEquals(_observedOverview, overview)) return;
            _observedOverview = overview;
            _overviewObservedRealtime = nowRealtimeSeconds;
            if (overview != null && overview.HasSeason)
            {
                ShowNotOpenState = !overview.IsEnabled;
            }
        }

        /// <summary>按 UNITY-HANDOFF §11 校准计算当前阶段剩余毫秒数。</summary>
        public ulong ComputeStageRemainingMs(TournamentOverviewSnapshot overview, float nowRealtimeSeconds)
        {
            if (overview == null) return 0UL;
            ObserveOverview(overview, nowRealtimeSeconds);
            float elapsed = Math.Max(0f, nowRealtimeSeconds - _overviewObservedRealtime);
            return overview.CurrentStageRemainingMs(overview.CapturedAtLocalSeconds + elapsed);
        }

        public void OpenCreateModal()
        {
            ActiveModal = TournamentModalKind.CreateSquad;
            ModalInlineError = string.Empty;
        }

        public void OpenInviteModal()
        {
            ActiveModal = TournamentModalKind.InviteMember;
            InvitePlayerIdDraft = string.Empty;
            ModalInlineError = string.Empty;
        }

        public void OpenConfirmLockModal()
        {
            ActiveModal = TournamentModalKind.ConfirmLock;
            ModalInlineError = string.Empty;
        }

        public void OpenLineupModal(TournamentSquadView squad, uint battleTeamSize, ulong matchId, bool forMatchStart)
        {
            ActiveModal = TournamentModalKind.ConfirmLineup;
            PendingMatchId = matchId;
            LineupModalIsForMatchStart = forMatchStart;
            ModalInlineError = string.Empty;
            SeedLineupDraft(squad, battleTeamSize);
        }

        public void SeedLineupDraft(TournamentSquadView squad, uint battleTeamSize)
        {
            _lineupDraft.Clear();
            if (squad == null || squad.Members.Count == 0) return;
            uint cap = battleTeamSize == 0 ? 3u : battleTeamSize;
            var starters = new List<TournamentMemberView>();
            for (int i = 0; i < squad.Members.Count; i++)
            {
                var m = squad.Members[i];
                if (m != null && m.StarterSlot > 0) starters.Add(m);
            }
            starters.Sort((a, b) => a.StarterSlot.CompareTo(b.StarterSlot));
            for (int i = 0; i < starters.Count && _lineupDraft.Count < cap; i++)
            {
                if (!_lineupDraft.Contains(starters[i].PlayerId))
                    _lineupDraft.Add(starters[i].PlayerId);
            }
            for (int i = 0; i < squad.Members.Count && _lineupDraft.Count < cap; i++)
            {
                var m = squad.Members[i];
                if (m != null && !_lineupDraft.Contains(m.PlayerId))
                    _lineupDraft.Add(m.PlayerId);
            }
        }

        public bool ToggleLineupMember(ulong playerId, uint requiredSize)
        {
            if (playerId == 0UL) return false;
            int idx = _lineupDraft.IndexOf(playerId);
            if (idx >= 0)
            {
                _lineupDraft.RemoveAt(idx);
                ModalInlineError = string.Empty;
                return true;
            }
            uint limit = requiredSize == 0 ? 3u : requiredSize;
            if (_lineupDraft.Count >= limit)
            {
                ModalInlineError = $"最多选择 {limit} 名首发成员，请先取消一位再替换。";
                return false;
            }
            _lineupDraft.Add(playerId);
            ModalInlineError = string.Empty;
            return true;
        }

        public bool IsMemberSelectedInDraft(ulong playerId) => _lineupDraft.Contains(playerId);

        public bool IsLineupDraftComplete(uint requiredSize)
        {
            uint need = requiredSize == 0 ? 3u : requiredSize;
            return _lineupDraft.Count == need;
        }

        public void CloseModal()
        {
            ActiveModal = TournamentModalKind.None;
            PendingMatchId = 0UL;
            LineupModalIsForMatchStart = false;
            ModalInlineError = string.Empty;
        }

        public static bool ValidateSquadNameLength(string raw, out int charCount)
        {
            string trimmed = (raw ?? string.Empty).Trim();
            charCount = CountUnicodeCodePoints(trimmed);
            return charCount >= MinSquadNameChars && charCount <= MaxSquadNameChars;
        }

        public static int CountUnicodeCodePoints(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    i++;
                count++;
            }
            return count;
        }

        public static bool TryParsePlayerId(string raw, out ulong playerId)
        {
            playerId = 0UL;
            string trimmed = (raw ?? string.Empty).Trim();
            return ulong.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out playerId) && playerId != 0UL;
        }

        /// <summary>按服务端快照推导右侧行动卡唯一阶段主 CTA，绝不伪造资格或本地改状态。</summary>
        public static TournamentPrimaryCta DerivePrimaryCta(
            TournamentOverviewSnapshot overview, ulong playerId, bool isBusy)
        {
            if (overview == null || !overview.HasSeason || !overview.IsEnabled)
                return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None, "敬请期待", "赛事尚未开启", false);

            var season = overview.Season;
            var squad = overview.MySquad;
            bool isCaptain = overview.IsCaptain(playerId);
            uint teamSize = season.BattleTeamSize == 0 ? 3u : season.BattleTeamSize;

            if (!overview.HasSquad)
            {
                if (season.Stage == TournamentStage.Registration)
                    return new TournamentPrimaryCta(TournamentPrimaryCtaKind.CreateSquad, "创建战队",
                        "每人每赛季仅限加入 1 支战队", !isBusy);
                if (season.Stage == TournamentStage.Settled && overview.HasClaimableReward)
                    return new TournamentPrimaryCta(TournamentPrimaryCtaKind.ClaimReward, "领取赛季奖励",
                        "免费助威或赛季奖励待领取", !isBusy);
                return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None, "本季报名已截止",
                    "可在全服榜与对阵树免费助威并围观赛事", false);
            }

            switch (season.Stage)
            {
                case TournamentStage.Registration:
                    if (squad.State == TournamentSquadState.Registered)
                        return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None, "报名已锁定",
                            "锁定后本赛季不可更换队员，静待海选开赛", false);
                    if (!isCaptain)
                        return new TournamentPrimaryCta(TournamentPrimaryCtaKind.PreviewSquad, "等待队长确认报名",
                            "可进入「我的战队」查看名册与首发阵容", true);
                    if (squad.MemberCount < teamSize)
                    {
                        uint missing = teamSize - squad.MemberCount;
                        return new TournamentPrimaryCta(TournamentPrimaryCtaKind.RegisterSquad,
                            $"还差 {missing} 名队员", $"需满 {teamSize} 名队员方可锁定报名", false);
                    }
                    return new TournamentPrimaryCta(TournamentPrimaryCtaKind.RegisterSquad, "确认锁定报名",
                        "名册达标，锁定后代表战队出战本季群仙问道赛", !isBusy);

                case TournamentStage.Qualifier:
                {
                    if (squad.State != TournamentSquadState.Registered)
                        return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None, "未完成报名锁定",
                            "本季未能进入海选，可继续观战与免费助威", false);
                    uint maxMatches = season.QualifierMaxMatches == 0 ? 6u : season.QualifierMaxMatches;
                    uint remaining = maxMatches > squad.QualifierPlayed ? maxMatches - squad.QualifierPlayed : 0u;
                    if (remaining == 0u)
                        return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None,
                            $"本季海选 {maxMatches}/{maxMatches} 已打满", "静待全服淘汰赛开启", false);
                    var activeMatch = overview.MyActiveMatch;
                    if (activeMatch != null && activeMatch.Phase == TournamentMatchPhase.Qualifier
                        && activeMatch.State == TournamentMatchState.WaitingOpponent)
                        return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None, "战队等待对手就绪",
                            "已进入海选配对队列，对手就绪后自动拉入跨服房间", false);
                    if (!isCaptain)
                        return new TournamentPrimaryCta(TournamentPrimaryCtaKind.PreviewSquad, "当前阵容预览",
                            $"海选剩余 {remaining} 场，由队长发起海选匹配", true);
                    return new TournamentPrimaryCta(TournamentPrimaryCtaKind.StartQualifierMatch,
                        $"发起海选匹配（剩 {remaining} 场）", "7 天宽窗，随时约齐队友开战", !isBusy, 0UL);
                }

                case TournamentStage.Knockout:
                {
                    var active = overview.MyActiveMatch;
                    if (active != null && active.Phase == TournamentMatchPhase.Knockout)
                    {
                        if (active.State == TournamentMatchState.InBattle)
                            return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None, "淘汰赛激战中",
                                "本队对阵正在进行中", false, active.MatchId);
                        if (active.State == TournamentMatchState.WaitingOpponent || active.State == TournamentMatchState.Ready)
                        {
                            bool myReady = (active.SquadAId == squad.SquadId && active.ReadyA)
                                || (active.SquadBId == squad.SquadId && active.ReadyB);
                            if (myReady)
                                return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None, "战队等待对手就绪",
                                    "本队已完成备战，对手就绪后即刻开战", false, active.MatchId);
                            if (!isCaptain)
                                return new TournamentPrimaryCta(TournamentPrimaryCtaKind.PreviewSquad, "等待队长备战",
                                    "淘汰赛由队长确认出战阵容并开战", true, active.MatchId);
                            return new TournamentPrimaryCta(TournamentPrimaryCtaKind.StartKnockoutMatch, "进入淘汰赛备战",
                                "确认本场出战阵容，双方就绪立即拉入房间", !isBusy, active.MatchId);
                        }
                    }
                    return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None,
                        squad.State == TournamentSquadState.Champion ? "全服总冠军" : "当前无待开场次",
                        "可前往对阵树围观全服淘汰赛", false);
                }

                case TournamentStage.Settled:
                    if (overview.HasClaimableReward)
                        return new TournamentPrimaryCta(TournamentPrimaryCtaKind.ClaimReward, "领取赛季奖励",
                            "奖励将幂等发往邮箱并解锁荣誉称号/徽章", !isBusy);
                    bool anyClaimed = false;
                    for (int i = 0; i < overview.MyRewards.Count; i++)
                    {
                        if (overview.MyRewards[i].DeliveryState == TournamentRewardDeliveryState.TournamentRewardDeliveryClaimed)
                        {
                            anyClaimed = true;
                            break;
                        }
                    }
                    return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None,
                        anyClaimed ? "奖励已发至邮箱" : "本季已封榜结算",
                        anyClaimed ? "请前往仙笺邮箱查收绑定道具与银两" : "可在冠军殿堂查看本季荣耀", false);

                default:
                    return new TournamentPrimaryCta(TournamentPrimaryCtaKind.None, "敬请期待", string.Empty, false);
            }
        }

        /// <summary>主城 HUD 入口动态轻状态文案（无焦虑红点）。</summary>
        public string DeriveHudSubtitle(TournamentOverviewSnapshot overview, float nowRealtimeSeconds)
        {
            if (overview == null || !overview.HasSeason) return "全服战队赛";
            if (!overview.IsEnabled) return "敬请期待";
            var season = overview.Season;
            switch (season.Stage)
            {
                case TournamentStage.Registration:
                {
                    if (overview.HasPendingInvites) return $"收到 {overview.PendingInvites.Count} 条邀请";
                    ulong rem = ComputeStageRemainingMs(overview, nowRealtimeSeconds);
                    return rem > 0 ? $"报名中 · 剩 {TournamentFormatting.FormatCountdown(rem)}" : "报名进行中";
                }
                case TournamentStage.Qualifier:
                {
                    if (overview.HasSquad && overview.MySquad.State == TournamentSquadState.Registered)
                    {
                        uint max = season.QualifierMaxMatches == 0 ? 6u : season.QualifierMaxMatches;
                        return $"海选 {overview.MySquad.QualifierPlayed}/{max} 场";
                    }
                    return "海选积分赛 · 随时开战";
                }
                case TournamentStage.Knockout:
                    return overview.FeaturedMatches.Count > 0 ? "淘汰赛 · 直播围观中" : "全服淘汰赛";
                case TournamentStage.Settled:
                    return overview.HasClaimableReward ? "本季封榜 · 可领奖" : "本季总冠军诞生";
                default:
                    return "全服战队赛";
            }
        }

        /// <summary>按结构化服务端 Tip ID 决定 UI 恢复路径（严格对齐 UNITY-HANDOFF 15 个赛事 Tip 表）。</summary>
        public static TournamentTipRecovery ClassifyTipRecovery(uint tipId)
        {
            switch (tipId)
            {
                case (uint)match_error.KMatchTournamentNotOpen:
                    return new TournamentTipRecovery(false, false, false, true);
                case (uint)match_error.KMatchTournamentSquadNameInvalid:
                    return new TournamentTipRecovery(true, false, false, false);
                case (uint)match_error.KMatchTournamentLineupInvalid:
                    return new TournamentTipRecovery(false, true, false, false);
                case (uint)match_error.KMatchTournamentStageClosed:
                case (uint)match_error.KMatchTournamentAlreadyInSquad:
                case (uint)match_error.KMatchTournamentSquadNotFound:
                case (uint)match_error.KMatchTournamentNotCaptain:
                case (uint)match_error.KMatchTournamentSquadFull:
                case (uint)match_error.KMatchTournamentSquadLocked:
                case (uint)match_error.KMatchTournamentInviteNotFound:
                case (uint)match_error.KMatchTournamentQualifierLimit:
                case (uint)match_error.KMatchTournamentMatchNotReady:
                case (uint)match_error.KMatchTournamentAlreadyCheered:
                case (uint)match_error.KMatchTournamentRewardUnavailable:
                    return new TournamentTipRecovery(false, false, true, false);
                default:
                    return new TournamentTipRecovery(false, false, false, false);
            }
        }

        /// <summary>仅当玩家所属战队参与了该已结束场次且 battle_id 非 0 时允许触发战报回放。</summary>
        public static bool CanWatchReplay(TournamentMatchView match, TournamentSquadView mySquad)
        {
            if (match == null || mySquad == null) return false;
            if (match.State != TournamentMatchState.Finished || match.BattleId == 0UL) return false;
            return match.SquadAId == mySquad.SquadId || match.SquadBId == mySquad.SquadId;
        }

        /// <summary>生成榜单赛季切换选项（首项恒为当前赛季，其余来自历届冠军殿堂）。</summary>
        public static List<TournamentSeasonChoice> BuildSeasonChoices(TournamentOverviewSnapshot overview)
        {
            var list = new List<TournamentSeasonChoice>
            {
                new TournamentSeasonChoice(0u, "当前赛季", true),
            };
            if (overview == null) return list;
            uint currentId = overview.Season?.SeasonId ?? 0u;
            for (int i = 0; i < overview.HallOfFame.Count; i++)
            {
                var past = overview.HallOfFame[i];
                if (past == null || past.SeasonId == 0u || past.SeasonId == currentId) continue;
                string label = !string.IsNullOrEmpty(past.SeasonName)
                    ? $"S{past.SeasonId} · {past.SeasonName}"
                    : $"第 {past.SeasonId} 赛季";
                list.Add(new TournamentSeasonChoice(past.SeasonId, label, false));
            }
            return list;
        }
    }
}
