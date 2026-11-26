using System;
using System.Collections.Generic;
using System.Globalization;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Ranked
{
    /// <summary>
    /// 论剑排位首发 V1 网络真源（纯 C#，不引用 UnityEngine）：
    /// - RPC 448 GetRankedProfile：大厅赛季、本人积分/段位/有效场次、6 预设、已获展示权益、历史与往季榜首；
    /// - RPC 449 SaveRankedPreset：服务端确认后才更新本地已选预设；
    /// - RPC 450 UpdateRankedCosmetic：服务端确认后才更新玉符/头像纹样/名片底纹/称号佩戴与隐藏开关；
    /// - RPC 451 GetRankedBattleResult：结算页权威积分/场次/新解锁展示权益，PENDING 时有界退避轮询直到确认或离场。
    /// 全部回调受 (connectionIdentity, playerId, generation) 保护，切号、断线、换 Gate 一律作废旧回调。
    /// </summary>
    public sealed class RankedClient : IDisposable
    {
        public static RankedClient Instance { get; private set; }

        public const string NotInGameMessage = "请进入角色后再使用论剑排位。";
        public const string LoadingMessage = "正在同步论剑档案…";
        public const string SavingPresetMessage = "正在保存战法预设…";
        public const string SavingCosmeticMessage = "正在更新外观展示…";
        public const string SettlingSeasonMessage = "上季结算中，请稍后再试。";
        public const string PendingRatingMessage = "积分待确认，正在查询权威落账…";
        public static readonly IReadOnlyList<float> ResultPollDelaysSeconds = new[] { 2f, 3f, 5f, 8f, 10f, 12f };

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private readonly RpcGuard _guard;
        private readonly bool _ownsGuard;

        private object _observedConnection;
        private ulong _observedPlayer;
        private int _generation;
        private bool _disposed;

        private ulong _pollBattleId;
        private int _pollAttempt;
        private float _nextPollAt = float.NegativeInfinity;
        private bool _resultRequestInFlight;

        public ulong PlayerId => _net.PlayerId;
        public RankedProfileSnapshot Profile { get; private set; }
        public RankedBattleSettlementState ActiveSettlement { get; private set; }

        public bool ProfileLoading { get; private set; }
        public bool PresetSaving { get; private set; }
        public bool CosmeticSaving { get; private set; }
        public bool ResultLoading => _resultRequestInFlight;
        public string Status { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;

        public uint SelectedTeamSize { get; set; } = RankedFormatting.DefaultTeamSize;

        public uint SelectedCharacterPresetId =>
            Profile?.SelectedPresets != null && Profile.SelectedPresets.CharacterPresetId != 0
                ? Profile.SelectedPresets.CharacterPresetId
                : RankedFormatting.DefaultCharacterPresetId;

        public uint SelectedPetPresetId =>
            Profile?.SelectedPresets != null && Profile.SelectedPresets.PetPresetId != 0
                ? Profile.SelectedPresets.PetPresetId
                : RankedFormatting.DefaultPetPresetId;

        public event Action Changed;
        public event Action<RankedBattleSettlementState> SettlementOpened;

        public RankedClient(
            IBattleTransport net,
            Func<object> connectionIdentity = null,
            Func<float> clock = null,
            RpcGuard rpcGuard = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            if (clock == null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _clock = () => (float)sw.Elapsed.TotalSeconds;
            }
            else
            {
                _clock = clock;
            }
            _ownsGuard = rpcGuard == null;
            _guard = rpcGuard ?? new RpcGuard(_clock);
            _observedConnection = _connectionIdentity();
            _observedPlayer = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        public static RankedClient Attach(
            IBattleTransport net,
            Func<object> connectionIdentity = null,
            Func<float> clock = null,
            RpcGuard rpcGuard = null)
        {
            Instance?.Dispose();
            var client = new RankedClient(net, connectionIdentity, clock, rpcGuard);
            Instance = client;
            return client;
        }

        public void ObserveConnection()
        {
            if (_disposed) return;
            object conn = _connectionIdentity();
            ulong player = _net.PlayerId;
            if (ReferenceEquals(conn, _observedConnection) && player == _observedPlayer) return;
            _observedConnection = conn;
            _observedPlayer = player;
            if (_ownsGuard) _guard.Reset();
            Reset();
        }

        public void Reset()
        {
            if (_disposed) return;
            ++_generation;
            Profile = null;
            ActiveSettlement = null;
            ProfileLoading = false;
            PresetSaving = false;
            CosmeticSaving = false;
            _resultRequestInFlight = false;
            _pollBattleId = 0;
            _pollAttempt = 0;
            _nextPollAt = float.NegativeInfinity;
            Status = string.Empty;
            LastError = string.Empty;
            SelectedTeamSize = RankedFormatting.DefaultTeamSize;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ++_generation;
            _net.Disconnected -= HandleDisconnected;
            if (Instance == this) Instance = null;
        }

        public void Tick()
        {
            if (_disposed) return;
            ObserveConnection();
            if (_pollBattleId == 0 || _resultRequestInFlight) return;
            if (!_net.IsReady || PlayerId == 0) return;
            if (_clock() < _nextPollAt) return;
            SendBattleResultQuery(_pollBattleId);
        }

        /// <summary>RPC 448：读取论剑档案（赛季、本人积分/段位/有效场次、6 预设、已获权益与历史）。</summary>
        public bool RequestProfile(bool includeHistory = true)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0)
            {
                FailLocal(NotInGameMessage);
                return false;
            }
            if (ProfileLoading) return false;
            if (!TryConsumeBudget(MatchServiceRpc.GetRankedProfileMessageId)) return false;

            int gen = _generation;
            ulong player = PlayerId;
            ProfileLoading = true;
            Status = LoadingMessage;
            LastError = string.Empty;
            Changed?.Invoke();

            _net.Call(MatchServiceRpc.GetRankedProfileMessageId,
                new GetRankedProfileRequest { IncludeHistory = includeHistory },
                GetRankedProfileResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    ProfileLoading = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        LastError = DescribeTip(resp.ErrorMessage);
                        Status = LastError;
                        Changed?.Invoke();
                        return;
                    }
                    Profile = RankedProfileSnapshot.FromResponse(resp);
                    if (Profile != null && !Profile.IsTeamSizeOpen(SelectedTeamSize))
                        SelectedTeamSize = Profile.DefaultOpenTeamSize;
                    LastError = string.Empty;
                    Status = Profile != null && Profile.IsSeasonSettling ? SettlingSeasonMessage : string.Empty;
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    ProfileLoading = false;
                    LastError = DescribeTransport("读取论剑档案失败", err);
                    Status = LastError;
                    Changed?.Invoke();
                });
            return true;
        }

        /// <summary>RPC 449：保存人物与宝宝战法预设；只在服务端确认后更新本地档案。</summary>
        public bool SavePreset(uint characterPresetId, uint petPresetId, uint ruleVersion = 0)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0)
            {
                FailLocal(NotInGameMessage);
                return false;
            }
            if (PresetSaving) return false;
            if (!TryConsumeBudget(MatchServiceRpc.SaveRankedPresetMessageId)) return false;

            uint effectiveRuleVersion = ruleVersion != 0 ? ruleVersion : (Profile?.RuleVersion ?? 2u);
            int gen = _generation;
            ulong player = PlayerId;
            PresetSaving = true;
            Status = SavingPresetMessage;
            LastError = string.Empty;
            Changed?.Invoke();

            _net.Call(MatchServiceRpc.SaveRankedPresetMessageId,
                new SaveRankedPresetRequest
                {
                    CharacterPresetId = characterPresetId,
                    PetPresetId = petPresetId,
                    RuleVersion = effectiveRuleVersion,
                },
                SaveRankedPresetResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    PresetSaving = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        LastError = DescribeTip(resp.ErrorMessage);
                        Status = LastError;
                        Changed?.Invoke();
                        return;
                    }
                    if (Profile != null && resp?.SelectedPresets != null)
                        Profile = Profile.WithPresets(resp.SelectedPresets);
                    LastError = string.Empty;
                    Status = "战法预设已保存。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    PresetSaving = false;
                    LastError = DescribeTransport("保存战法预设失败", err);
                    Status = LastError;
                    Changed?.Invoke();
                });
            return true;
        }

        /// <summary>RPC 450：更新论剑展示外观佩戴（仅展示，不影响任何数值）；服务端确认后更新。</summary>
        public bool UpdateCosmetic(RankedCosmeticLoadout loadout)
        {
            if (_disposed || loadout == null) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0)
            {
                FailLocal(NotInGameMessage);
                return false;
            }
            if (CosmeticSaving) return false;
            if (!TryConsumeBudget(MatchServiceRpc.UpdateRankedCosmeticMessageId)) return false;

            int gen = _generation;
            ulong player = PlayerId;
            CosmeticSaving = true;
            Status = SavingCosmeticMessage;
            LastError = string.Empty;
            Changed?.Invoke();

            _net.Call(MatchServiceRpc.UpdateRankedCosmeticMessageId,
                new UpdateRankedCosmeticRequest { Loadout = loadout.Clone() },
                UpdateRankedCosmeticResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    CosmeticSaving = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        LastError = DescribeTip(resp.ErrorMessage);
                        Status = LastError;
                        Changed?.Invoke();
                        return;
                    }
                    if (Profile != null && resp?.Loadout != null)
                        Profile = Profile.WithCosmetic(resp.Loadout);
                    LastError = string.Empty;
                    Status = "展示外观已更新。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    CosmeticSaving = false;
                    LastError = DescribeTransport("更新展示外观失败", err);
                    Status = LastError;
                    Changed?.Invoke();
                });
            return true;
        }

        /// <summary>由现场 BattleEndS2C 打开排位结算页，并立即查询 RPC 451 权威落账。</summary>
        public RankedBattleSettlementState BeginSettlementFromBattleEnd(
            BattleEndS2C end, uint playerTeamIndex, uint fallbackTeamSize = 0)
        {
            if (_disposed || end == null || end.BattleId == 0) return null;
            ObserveConnection();
            bool isVoid = BattleClient.IsVoidEnd(end);
            var outcome = end.Settlement != null ? end.Settlement.Outcome : end.Outcome;
            uint totalRounds = end.Settlement != null ? end.Settlement.TotalRounds : 0u;
            uint seasonId = end.SeasonId != 0 ? end.SeasonId : (Profile?.CurrentSeason?.SeasonId ?? 0u);
            var state = new RankedBattleSettlementState
            {
                BattleId = end.BattleId,
                SeasonId = seasonId,
                TeamSize = fallbackTeamSize != 0 ? fallbackTeamSize : SelectedTeamSize,
                Outcome = outcome,
                PlayerTeamIndex = playerTeamIndex,
                TotalRounds = totalRounds,
                IsVoid = isVoid,
                Surrendered = end.Settlement?.Surrendered == true,
                FromRecovery = false,
                MaterialCreditConfirmed = true,
                MaterialCreditUnitsAdded = end.MaterialCreditUnitsAdded,
                MaterialTradableUnitsAdded = end.MaterialTradableUnitsAdded,
                MaterialPvpLossIneligible = end.MaterialPvpLossIneligible,
                TotalDamageDealt = end.Settlement?.TotalDamageDealt ?? 0ul,
                PrimaryFocusDamageDealt = end.Settlement?.PrimaryFocusDamageDealt ?? 0ul,
                TotalSupportValue = end.Settlement?.TotalHealingDone ?? 0ul,
                ControlAndDispelCount = (end.Settlement?.TotalControlsLanded ?? 0u) + (end.Settlement?.TotalDispelsLanded ?? 0u),
                DamageTaken = end.Settlement?.DamageTaken ?? 0ul,
                HighlightKind = end.Settlement?.HighlightKind ?? BattleHighlightKind.BattleHighlightNone,
                EndedOpponentStreak = end.Settlement?.EndedOpponentStreak ?? 0u,
                RatingLoading = !isVoid,
            };
            ActiveSettlement = state;
            _pollBattleId = 0;
            _pollAttempt = 0;
            _nextPollAt = float.NegativeInfinity;
            Changed?.Invoke();
            SettlementOpened?.Invoke(state);
            RequestBattleResult(end.BattleId);
            return state;
        }

        /// <summary>由重登补查结果 PlayerBattleResult 打开排位结算页，并立即查询 RPC 451 权威落账。</summary>
        public RankedBattleSettlementState BeginSettlementFromRecovery(PlayerBattleResult result)
        {
            if (_disposed || result == null || result.BattleId == 0) return null;
            ObserveConnection();
            bool isVoid = BattleResultRecoveryClient.IsVoid(result);
            var state = new RankedBattleSettlementState
            {
                BattleId = result.BattleId,
                SeasonId = result.SeasonId != 0 ? result.SeasonId : (Profile?.CurrentSeason?.SeasonId ?? 0u),
                TeamSize = SelectedTeamSize,
                Outcome = (eBattleOutcome)result.Outcome,
                PlayerTeamIndex = result.TeamIndex,
                TotalRounds = result.TotalRounds,
                IsVoid = isVoid,
                Surrendered = result.Surrendered,
                FromRecovery = true,
                MaterialCreditConfirmed = true,
                MaterialCreditUnitsAdded = result.MaterialCreditUnitsAdded,
                MaterialTradableUnitsAdded = result.MaterialTradableUnitsAdded,
                MaterialPvpLossIneligible = result.MaterialPvpLossIneligible,
                RatingLoading = !isVoid,
            };
            ActiveSettlement = state;
            _pollBattleId = 0;
            _pollAttempt = 0;
            _nextPollAt = float.NegativeInfinity;
            Changed?.Invoke();
            SettlementOpened?.Invoke(state);
            RequestBattleResult(result.BattleId);
            return state;
        }

        /// <summary>RPC 451：查询指定对局的权威排位结算结果；PENDING 时启动有界轮询。</summary>
        public bool RequestBattleResult(ulong battleId)
        {
            if (_disposed || battleId == 0) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0)
            {
                if (ActiveSettlement != null && ActiveSettlement.BattleId == battleId)
                {
                    ActiveSettlement.RatingLoading = false;
                    ActiveSettlement.RatingError = NotInGameMessage;
                    Changed?.Invoke();
                }
                return false;
            }
            _pollBattleId = battleId;
            _pollAttempt = 0;
            _nextPollAt = float.NegativeInfinity;
            return SendBattleResultQuery(battleId);
        }

        public void DismissSettlement()
        {
            if (_disposed) return;
            ActiveSettlement = null;
            _pollBattleId = 0;
            _pollAttempt = 0;
            _nextPollAt = float.NegativeInfinity;
            Changed?.Invoke();
        }

        /// <summary>判断一条终局/补查结果是否属于论剑排位（PVP_TEAM 或携带排位 season_id）。</summary>
        public static bool IsRankedBattleEnd(BattleEndS2C end, uint battleConfigId = 0)
        {
            if (end == null) return false;
            if (end.SeasonId != 0) return true;
            if (end.Settlement != null && end.Settlement.CompetitiveRanked) return true;
            if (end.Settlement != null && end.Settlement.MatchMode == (uint)MatchMode.PvpTeam) return true;
            return battleConfigId == RankedFormatting.DefaultBattleConfigId
                && end.Settlement != null && end.Settlement.SeasonId != 0;
        }

        public static bool IsRankedRecoveredResult(PlayerBattleResult result)
        {
            if (result == null) return false;
            return result.MatchMode == (uint)MatchMode.PvpTeam || result.SeasonId != 0;
        }

        public static string DescribeTip(TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return string.Empty;
            switch (tip.Id)
            {
                case (uint)match_error.KMatchRankedSeasonSettling:
                    return SettlingSeasonMessage;
                case (uint)match_error.KMatchRankedPresetInvalid:
                    return "所选战法预设无效，请重新选择。";
                case (uint)match_error.KMatchRankedRuleVersionMismatch:
                    return "论剑规则版本已更新，请刷新档案后重试。";
                case (uint)match_error.KMatchBattleSurrenderNotAllowed:
                    return "当前战斗模式不支持投降。";
                case (uint)match_error.KMatchBattleSurrenderTooEarly:
                    return "需满第 5 回合后方可发起投降。";
                case (uint)match_error.KMatchBattleSurrenderCooldown:
                    return "投降表决冷却中，请稍后再试。";
                case (uint)match_error.KMatchBattleSurrenderAlreadyVoted:
                    return "你已参与本轮投降表决。";
                case (uint)common_error.KRateLimitExceeded:
                    return "操作过于频繁，请稍后再试。";
                case (uint)common_error.KServiceUnavailable:
                    return "论剑服务暂时繁忙，请稍后再试。";
                case (uint)common_error.KInvalidParameter:
                    return "请求参数无效，请刷新后重试。";
                default:
                    return $"论剑请求未完成（tip={tip.Id}），请稍后重试。";
            }
        }

        private bool SendBattleResultQuery(ulong battleId)
        {
            if (_resultRequestInFlight) return false;
            if (!TryConsumeBudget(MatchServiceRpc.GetRankedBattleResultMessageId))
            {
                ScheduleNextResultPoll(battleId);
                return false;
            }
            int gen = _generation;
            ulong player = PlayerId;
            _resultRequestInFlight = true;
            if (ActiveSettlement != null && ActiveSettlement.BattleId == battleId)
            {
                ActiveSettlement.RatingLoading = true;
                ActiveSettlement.RatingError = string.Empty;
            }
            Changed?.Invoke();

            _net.Call(MatchServiceRpc.GetRankedBattleResultMessageId,
                new GetRankedBattleResultRequest { BattleId = battleId },
                GetRankedBattleResultResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _resultRequestInFlight = false;
                    if (ActiveSettlement == null || ActiveSettlement.BattleId != battleId)
                    {
                        _pollBattleId = 0;
                        return;
                    }
                    ActiveSettlement.RatingLoading = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        ActiveSettlement.RatingError = DescribeTip(resp.ErrorMessage);
                        ScheduleNextResultPoll(battleId);
                        Changed?.Invoke();
                        return;
                    }
                    var view = resp?.Result?.Clone();
                    ActiveSettlement.AuthoritativeView = view;
                    ActiveSettlement.RatingError = string.Empty;
                    if (view != null && view.TeamSize != 0)
                    {
                        ActiveSettlement.TeamSize = view.TeamSize;
                        SelectedTeamSize = view.TeamSize;
                    }
                    if (view != null && view.SeasonId != 0)
                        ActiveSettlement.SeasonId = view.SeasonId;
                    if (view != null && view.Status == RankedMatchRatingStatus.RankedMatchRatingVoid)
                        ActiveSettlement.IsVoid = true;

                    bool stillPending = view == null || view.Status == RankedMatchRatingStatus.RankedMatchRatingPending;
                    if (stillPending && !ActiveSettlement.IsVoid)
                    {
                        ScheduleNextResultPoll(battleId);
                    }
                    else
                    {
                        _pollBattleId = 0;
                        _pollAttempt = 0;
                        _nextPollAt = float.NegativeInfinity;
                        // 积分/场次/展示权益落账确认后，同步刷新大厅档案，避免玩家点返回看到旧数字。
                        if (!ProfileLoading) RequestProfile(includeHistory: true);
                    }
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _resultRequestInFlight = false;
                    if (ActiveSettlement != null && ActiveSettlement.BattleId == battleId)
                    {
                        ActiveSettlement.RatingLoading = false;
                        ActiveSettlement.RatingError = DescribeTransport("查询排位结算失败", err);
                        ScheduleNextResultPoll(battleId);
                        Changed?.Invoke();
                    }
                });
            return true;
        }

        private void ScheduleNextResultPoll(ulong battleId)
        {
            if (_pollBattleId != battleId) return;
            if (_pollAttempt >= ResultPollDelaysSeconds.Count)
            {
                _pollBattleId = 0;
                _nextPollAt = float.NegativeInfinity;
                return;
            }
            float delay = ResultPollDelaysSeconds[_pollAttempt++];
            _nextPollAt = _clock() + delay;
        }

        private bool TryConsumeBudget(uint messageId)
        {
            if (_guard.IsCircuitOpen(messageId))
            {
                FailLocal("论剑服务暂时繁忙，请稍后再试。");
                return false;
            }
            if (_guard.IsRateLimited(messageId))
            {
                FailLocal("操作过于频繁，请稍后再试。");
                return false;
            }
            if (_ownsGuard && !_guard.TryBeginCall(messageId, out _))
            {
                FailLocal("操作过于频繁，请稍后再试。");
                return false;
            }
            return true;
        }

        private void HandleDisconnected()
        {
            _observedConnection = _connectionIdentity();
            _observedPlayer = _net.PlayerId;
            if (_ownsGuard) _guard.Reset();
            Reset();
        }

        private void FailLocal(string message)
        {
            LastError = message ?? string.Empty;
            Status = LastError;
            Changed?.Invoke();
        }

        private bool IsCurrent(int generation, ulong player) =>
            !_disposed && generation == _generation && _net.IsReady && PlayerId == player
            && ReferenceEquals(_observedConnection, _connectionIdentity());

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeTransport(string prefix, string err)
        {
            if (string.IsNullOrEmpty(err)) return prefix;
            if (err == RpcGuard.CircuitOpenError) return "论剑服务暂时繁忙，请稍后再试。";
            const string serverTip = "server tip=";
            if (err.StartsWith(serverTip, StringComparison.Ordinal)
                && uint.TryParse(err.Substring(serverTip.Length), NumberStyles.None, CultureInfo.InvariantCulture, out uint tipId))
            {
                return DescribeTip(new TipInfoMessage { Id = tipId });
            }
            return $"{prefix}：{err}";
        }
    }
}
