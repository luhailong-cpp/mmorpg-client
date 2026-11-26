using System;
using System.Collections.Generic;
using System.Globalization;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Tournament
{
    /// <summary>
    /// 全服赛季战队锦标赛《群仙问道赛》V1 非 UI 网络与状态真源（纯 C#，不引用 UnityEngine，WebGL2/IL2CPP 安全）：
    /// - 覆盖 RPC 465–475 全部 11 条 MatchService 锦标赛协议；
    /// - 仅在服务端回包确认后更新本地快照，绝不在本地臆造名次、积分或奖励；
    /// - 全部异步回调受 (connectionIdentity, playerId, generation) 三重守卫，断线/切号/换 Gate 自动作废旧回调；
    /// - UI 层按 docs/design/season-team-tournament-v1-ui-handoff.md 订阅 Changed 即可。
    /// </summary>
    public sealed class TournamentClient : IDisposable
    {
        public static TournamentClient Instance { get; private set; }

        public const string NotInGameMessage = "请进入角色后再参与群仙问道赛。";
        public const string LoadingOverviewMessage = "正在同步群仙问道赛信息…";
        public const string LoadingStandingsMessage = "正在拉取全服积分榜与对阵树…";

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private readonly RpcGuard _guard;
        private readonly bool _ownsGuard;

        private object _observedConnection;
        private ulong _observedPlayer;
        private int _generation;
        private bool _disposed;

        private bool _overviewLoading;
        private bool _standingsLoading;
        private bool _mutating;
        private bool _cheerLoading;
        private bool _cheerSending;

        public ulong PlayerId => _net.PlayerId;
        public TournamentOverviewSnapshot Overview { get; private set; }
        public TournamentStandingsSnapshot Standings { get; private set; }
        public TournamentSpectateCheerSnapshot SpectateCheer { get; private set; }
        public ulong LastLaunchedBattleId { get; private set; }

        public bool IsLoading => _overviewLoading || _standingsLoading;
        public bool IsMutating => _mutating;
        public bool IsBusy => IsLoading || IsMutating;
        public bool IsSpectateCheerSending => _cheerSending;
        public string Status { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        /// <summary>最近一次服务端业务错误对应的结构化 Tip ID（0 表示无服务端 Tip 错误），供 UI 精准决策恢复路径。</summary>
        public uint LastErrorTipId { get; private set; }

        public event Action Changed;
        public event Action<ulong> MatchBattleLaunched;
        public event Action<TournamentSaluteOutcome> ChampionSaluted;
        public event Action SpectateCheerChanged;

        public TournamentClient(
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

        public static TournamentClient Attach(
            IBattleTransport net,
            Func<object> connectionIdentity = null,
            Func<float> clock = null,
            RpcGuard rpcGuard = null)
        {
            Instance?.Dispose();
            var client = new TournamentClient(net, connectionIdentity, clock, rpcGuard);
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
            Overview = null;
            Standings = null;
            SpectateCheer = null;
            LastLaunchedBattleId = 0UL;
            _overviewLoading = false;
            _standingsLoading = false;
            _mutating = false;
            _cheerLoading = false;
            _cheerSending = false;
            Status = string.Empty;
            LastError = string.Empty;
            LastErrorTipId = 0u;
            Changed?.Invoke();
            SpectateCheerChanged?.Invoke();
        }

        public void ClearSpectateCheer()
        {
            if (_disposed) return;
            if (SpectateCheer == null && !_cheerLoading && !_cheerSending) return;
            SpectateCheer = null;
            _cheerLoading = false;
            _cheerSending = false;
            SpectateCheerChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ++_generation;
            _net.Disconnected -= HandleDisconnected;
            if (Instance == this) Instance = null;
        }

        /// <summary>RPC 465：拉取赛事总览（赛季进度、我的战队、待处理邀请、精选观战、我的助威、奖励账单与冠军殿堂）。</summary>
        public bool RequestOverview(bool includeHistory = true)
        {
            if (!BeginQuery(MatchServiceRpc.GetTournamentOverviewMessageId, ref _overviewLoading, LoadingOverviewMessage,
                    out int gen, out ulong player))
                return false;

            _net.Call(MatchServiceRpc.GetTournamentOverviewMessageId,
                new GetTournamentOverviewRequest { IncludeHistory = includeHistory },
                GetTournamentOverviewResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _overviewLoading = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    Overview = TournamentOverviewSnapshot.FromResponse(resp, _clock());
                    LastError = string.Empty;
                    Status = string.Empty;
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _overviewLoading = false;
                    FailTransport("读取赛事总览失败", err);
                });
            return true;
        }

        /// <summary>RPC 466：创建本赛季固定战队（2–12 字符，由服务端审校并同季去重）。</summary>
        public bool CreateSquad(string squadName)
        {
            string trimmed = (squadName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                FailLocal("请输入 2 至 12 字战队名称。");
                return false;
            }
            if (!BeginMutation(MatchServiceRpc.CreateTournamentSquadMessageId, "正在创建战队…", out int gen, out ulong player))
                return false;

            _net.Call(MatchServiceRpc.CreateTournamentSquadMessageId,
                new CreateTournamentSquadRequest { SquadName = trimmed },
                CreateTournamentSquadResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    if (Overview != null && resp?.Squad != null)
                        Overview = Overview.WithSquad(resp.Squad);
                    LastError = string.Empty;
                    Status = "战队创建成功，快去邀请同伴吧。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport("创建战队失败", err);
                });
            return true;
        }

        /// <summary>RPC 467：队长邀请目标角色加入战队（支持全服跨区）。</summary>
        public bool InviteMember(ulong targetPlayerId)
        {
            if (targetPlayerId == 0UL || targetPlayerId == PlayerId)
            {
                FailLocal("请输入有效的受邀玩家编号。");
                return false;
            }
            if (!BeginMutation(MatchServiceRpc.InviteTournamentMemberMessageId, "正在发送战队邀请…", out int gen, out ulong player))
                return false;

            _net.Call(MatchServiceRpc.InviteTournamentMemberMessageId,
                new InviteTournamentMemberRequest { TargetPlayerId = targetPlayerId },
                InviteTournamentMemberResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    LastError = string.Empty;
                    Status = "战队邀请已发出。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport("发送邀请失败", err);
                });
            return true;
        }

        /// <summary>RPC 468：受邀玩家同意或拒绝战队邀请。</summary>
        public bool RespondInvite(ulong squadId, bool accept)
        {
            if (squadId == 0UL)
            {
                FailLocal("无效的战队邀请。");
                return false;
            }
            if (!BeginMutation(MatchServiceRpc.RespondTournamentInviteMessageId,
                    accept ? "正在加入战队…" : "正在处理邀请…", out int gen, out ulong player))
                return false;

            _net.Call(MatchServiceRpc.RespondTournamentInviteMessageId,
                new RespondTournamentInviteRequest { SquadId = squadId, Accept = accept },
                RespondTournamentInviteResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    if (Overview != null)
                        Overview = Overview.WithSquad(accept ? resp?.Squad : Overview.MySquad, removeInviteSquadId: squadId);
                    LastError = string.Empty;
                    Status = accept ? "已加入战队。" : "已拒绝邀请。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport("处理邀请失败", err);
                });
            return true;
        }

        /// <summary>RPC 469：退队/解散（targetPlayerId=0 或等于自己）或队长移出队员。</summary>
        public bool LeaveOrKick(ulong targetPlayerId = 0UL)
        {
            bool isKick = targetPlayerId != 0UL && targetPlayerId != PlayerId;
            if (!BeginMutation(MatchServiceRpc.LeaveTournamentSquadMessageId,
                    isKick ? "正在移出队员…" : "正在离开战队…", out int gen, out ulong player))
                return false;

            _net.Call(MatchServiceRpc.LeaveTournamentSquadMessageId,
                new LeaveTournamentSquadRequest { TargetPlayerId = targetPlayerId },
                LeaveTournamentSquadResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    if (Overview != null)
                    {
                        bool leftSelf = !isKick || (resp != null && resp.SquadDisbanded);
                        Overview = Overview.WithSquad(leftSelf ? null : resp?.Squad);
                    }
                    LastError = string.Empty;
                    Status = resp != null && resp.SquadDisbanded
                        ? "战队已解散。"
                        : (isKick ? "已移出该队员。" : "已离开战队。");
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport(isKick ? "移出队员失败" : "退队失败", err);
                });
            return true;
        }

        /// <summary>RPC 470：队长调整首发与替补名单（传入恰好 battle_team_size 名首发 ID）。</summary>
        public bool UpdateLineup(IReadOnlyList<ulong> starterPlayerIds)
        {
            if (starterPlayerIds == null || starterPlayerIds.Count == 0)
            {
                FailLocal("请勾选完整首发阵容。");
                return false;
            }
            if (!BeginMutation(MatchServiceRpc.UpdateTournamentLineupMessageId, "正在保存首发阵容…", out int gen, out ulong player))
                return false;

            var req = new UpdateTournamentLineupRequest();
            for (int i = 0; i < starterPlayerIds.Count; i++) req.StarterPlayerIds.Add(starterPlayerIds[i]);

            _net.Call(MatchServiceRpc.UpdateTournamentLineupMessageId,
                req,
                UpdateTournamentLineupResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    if (Overview != null && resp?.Squad != null)
                        Overview = Overview.WithSquad(resp.Squad);
                    LastError = string.Empty;
                    Status = "首发阵容已更新。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport("更新阵容失败", err);
                });
            return true;
        }

        /// <summary>RPC 471：队长确认锁定本赛季报名（锁后本季不可更换名册成员）。</summary>
        public bool RegisterSquad()
        {
            if (!BeginMutation(MatchServiceRpc.RegisterTournamentSquadMessageId, "正在锁定赛季报名…", out int gen, out ulong player))
                return false;

            _net.Call(MatchServiceRpc.RegisterTournamentSquadMessageId,
                new RegisterTournamentSquadRequest(),
                RegisterTournamentSquadResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    if (Overview != null && resp?.Squad != null)
                        Overview = Overview.WithSquad(resp.Squad);
                    LastError = string.Empty;
                    Status = "战队已锁定报名，预祝旗开得胜。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport("锁定报名失败", err);
                });
            return true;
        }

        /// <summary>RPC 472：拉取全服积分榜与动态淘汰赛对阵树（seasonId=0 表示当前赛季）。</summary>
        public bool RequestStandings(uint seasonId = 0, uint page = 1, uint pageSize = 20)
        {
            if (!BeginQuery(MatchServiceRpc.ListTournamentStandingsMessageId, ref _standingsLoading, LoadingStandingsMessage,
                    out int gen, out ulong player))
                return false;

            uint effPage = page == 0 ? 1u : page;
            uint effSize = pageSize == 0 ? 20u : Math.Min(pageSize, 50u);
            _net.Call(MatchServiceRpc.ListTournamentStandingsMessageId,
                new ListTournamentStandingsRequest { SeasonId = seasonId, Page = effPage, PageSize = effSize },
                ListTournamentStandingsResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _standingsLoading = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    Standings = TournamentStandingsSnapshot.FromResponse(resp, effPage, effSize);
                    LastError = string.Empty;
                    Status = string.Empty;
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _standingsLoading = false;
                    FailTransport("拉取全服榜失败", err);
                });
            return true;
        }

        /// <summary>RPC 473：发起海选匹配（matchId=0）或淘汰赛备战开打（传本场 matchId）。</summary>
        public bool StartMatch(ulong matchId = 0UL, IReadOnlyList<ulong> lineupPlayerIds = null)
        {
            if (!BeginMutation(MatchServiceRpc.StartTournamentMatchMessageId, "正在提交赛前备战…", out int gen, out ulong player))
                return false;

            var req = new StartTournamentMatchRequest { MatchId = matchId };
            if (lineupPlayerIds != null)
            {
                for (int i = 0; i < lineupPlayerIds.Count; i++) req.LineupPlayerIds.Add(lineupPlayerIds[i]);
            }

            _net.Call(MatchServiceRpc.StartTournamentMatchMessageId,
                req,
                StartTournamentMatchResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    if (Overview != null && resp?.Match != null)
                        Overview = Overview.WithActiveMatch(resp.Match);
                    LastError = string.Empty;
                    if (resp != null && resp.BattleId != 0UL)
                    {
                        LastLaunchedBattleId = resp.BattleId;
                        Status = "对手已就绪，正在进入全服赛场…";
                        Changed?.Invoke();
                        MatchBattleLaunched?.Invoke(resp.BattleId);
                        return;
                    }
                    Status = "备战已就绪，正在等待对手入场…";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport("发起锦标赛对局失败", err);
                });
            return true;
        }

        /// <summary>RPC 474：免费助威（每人每季 1 票、零消耗）。</summary>
        public bool CheerSquad(ulong squadId)
        {
            if (squadId == 0UL)
            {
                FailLocal("请选择一支已报名战队。");
                return false;
            }
            if (!BeginMutation(MatchServiceRpc.CheerTournamentSquadMessageId, "正在送出免费助威…", out int gen, out ulong player))
                return false;

            _net.Call(MatchServiceRpc.CheerTournamentSquadMessageId,
                new CheerTournamentSquadRequest { SquadId = squadId },
                CheerTournamentSquadResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    string targetName = FindSquadName(squadId);
                    if (Overview != null) Overview = Overview.WithCheered(squadId, targetName);
                    if (Standings != null && resp != null) Standings = Standings.WithCheerCount(squadId, resp.SquadCheerCount);
                    LastError = string.Empty;
                    Status = "免费助威成功。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport("助威失败", err);
                });
            return true;
        }

        /// <summary>RPC 475：领取/核对赛季绑定奖励与荣耀称号（自动幂等投递邮箱）。</summary>
        public bool ClaimReward(uint seasonId = 0u)
        {
            if (!BeginMutation(MatchServiceRpc.ClaimTournamentRewardMessageId, "正在领取赛季奖励…", out int gen, out ulong player))
                return false;

            _net.Call(MatchServiceRpc.ClaimTournamentRewardMessageId,
                new ClaimTournamentRewardRequest { SeasonId = seasonId },
                ClaimTournamentRewardResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    if (Overview != null && resp != null && resp.ClaimedRewards.Count > 0)
                        Overview = Overview.WithClaimedRewards(resp.ClaimedRewards);
                    LastError = string.Empty;
                    Status = "赛季奖励已发至邮箱，请前往查收。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport("领取赛季奖励失败", err);
                });
            return true;
        }

        /// <summary>V2 RPC 506：每日免费向总冠军观道致敬（每人每天 UTC+8 1 次，绑定银两与灵兽口粮发至邮箱）。</summary>
        public bool SaluteChampion(uint seasonId = 0u)
        {
            if (!BeginMutation(MatchServiceRpc.SaluteTournamentChampionMessageId, "正在向总冠军观道致敬…", out int gen, out ulong player))
                return false;

            _net.Call(MatchServiceRpc.SaluteTournamentChampionMessageId,
                new SaluteTournamentChampionRequest { SeasonId = seasonId },
                SaluteTournamentChampionResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        FailServer(resp.ErrorMessage);
                        return;
                    }
                    uint effSeason = resp?.SeasonId ?? seasonId;
                    ulong saluteCount = resp?.ChampionSaluteCount ?? 0UL;
                    if (Overview != null)
                        Overview = Overview.WithSaluted(effSeason, saluteCount);
                    var items = new List<TournamentRewardItem>();
                    if (resp != null)
                    {
                        for (int i = 0; i < resp.Items.Count; i++)
                            if (resp.Items[i] != null) items.Add(resp.Items[i].Clone());
                    }
                    var outcome = new TournamentSaluteOutcome(effSeason, saluteCount, resp?.GoldAmount ?? 0UL, items, resp?.MailId ?? 0UL);
                    LastError = string.Empty;
                    Status = "观道致敬成功，绑定薄礼已发至仙笺邮箱。";
                    Changed?.Invoke();
                    ChampionSaluted?.Invoke(outcome);
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _mutating = false;
                    FailTransport("观道致敬失败", err);
                });
            return true;
        }

        /// <summary>V2 RPC 507：观战底部预设喝彩（严格白名单 phraseId 1..6、阵营 0/1，5 秒冷却，绝不本地臆造人气）。</summary>
        public bool SendSpectateCheer(ulong battleId, uint phraseId, uint side)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0UL)
            {
                FailLocal(NotInGameMessage);
                return false;
            }
            if (battleId == 0UL || phraseId < 1u || phraseId > 6u || side > 1u)
            {
                FailLocal("无效的喝彩选项。");
                return false;
            }
            if (_cheerSending) return false;
            if (!TryConsumeBudget(MatchServiceRpc.SendTournamentSpectateCheerMessageId)) return false;

            int gen = _generation;
            ulong player = PlayerId;
            _cheerSending = true;
            LastError = string.Empty;
            LastErrorTipId = 0u;
            SpectateCheerChanged?.Invoke();

            _net.Call(MatchServiceRpc.SendTournamentSpectateCheerMessageId,
                new SendTournamentSpectateCheerRequest { BattleId = battleId, PhraseId = phraseId, Side = side },
                SendTournamentSpectateCheerResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _cheerSending = false;
                    if (resp != null && resp.BattleId != 0UL)
                    {
                        SpectateCheer = TournamentSpectateCheerSnapshot.Merge(
                            SpectateCheer, resp.BattleId, resp.CountA, resp.CountB,
                            resp.CooldownUntilMs, resp.RecentCheers, resp.ServerTimeMs, _clock());
                    }
                    if (HasTip(resp?.ErrorMessage))
                    {
                        LastErrorTipId = resp.ErrorMessage.Id;
                        LastError = DescribeTip(resp.ErrorMessage);
                        Status = LastError;
                        SpectateCheerChanged?.Invoke();
                        return;
                    }
                    LastError = string.Empty;
                    LastErrorTipId = 0u;
                    SpectateCheerChanged?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _cheerSending = false;
                    LastErrorTipId = TryExtractServerTipId(err);
                    LastError = DescribeTransport("发送观战喝彩失败", err);
                    Status = LastError;
                    SpectateCheerChanged?.Invoke();
                });
            return true;
        }

        /// <summary>V2 RPC 508：增量拉取观战双方人气与近期预设喝彩（按 seq 增量拉取）。</summary>
        public bool RequestSpectateCheer(ulong battleId, ulong afterSeq = 0UL)
        {
            if (_disposed || battleId == 0UL) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0UL) return false;
            if (_cheerLoading) return false;
            if (!TryConsumeBudget(MatchServiceRpc.GetTournamentSpectateCheerMessageId)) return false;

            int gen = _generation;
            ulong player = PlayerId;
            _cheerLoading = true;
            ulong effAfter = afterSeq;
            if (effAfter == 0UL && SpectateCheer != null && SpectateCheer.BattleId == battleId)
                effAfter = SpectateCheer.LatestSeq;

            _net.Call(MatchServiceRpc.GetTournamentSpectateCheerMessageId,
                new GetTournamentSpectateCheerRequest { BattleId = battleId, AfterSeq = effAfter },
                GetTournamentSpectateCheerResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _cheerLoading = false;
                    if (HasTip(resp?.ErrorMessage)) return;
                    if (resp != null)
                    {
                        ulong bid = resp.BattleId != 0UL ? resp.BattleId : battleId;
                        SpectateCheer = TournamentSpectateCheerSnapshot.Merge(
                            SpectateCheer, bid, resp.CountA, resp.CountB,
                            resp.CooldownUntilMs, resp.RecentCheers, resp.ServerTimeMs, _clock());
                        SpectateCheerChanged?.Invoke();
                    }
                },
                _ =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _cheerLoading = false;
                });
            return true;
        }

        public static string DescribeTip(TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return string.Empty;
            switch (tip.Id)
            {
                case (uint)match_error.KMatchTournamentNotOpen:
                    return "群仙问道赛暂未开启，敬请期待。";
                case (uint)match_error.KMatchTournamentStageClosed:
                    return "当前赛季阶段不支持此操作，请刷新赛事状态。";
                case (uint)match_error.KMatchTournamentSquadNameInvalid:
                    return "战队名称不合规或本赛季已被占用，请更换 2–12 字名称。";
                case (uint)match_error.KMatchTournamentAlreadyInSquad:
                    return "本赛季每人仅限加入一支战队。";
                case (uint)match_error.KMatchTournamentSquadNotFound:
                    return "未找到目标战队或已解散。";
                case (uint)match_error.KMatchTournamentNotCaptain:
                    return "只有战队队长可执行此操作。";
                case (uint)match_error.KMatchTournamentSquadFull:
                    return "战队名册人数已满。";
                case (uint)match_error.KMatchTournamentSquadLocked:
                    return "战队已锁定本赛季报名，不可变更名册成员。";
                case (uint)match_error.KMatchTournamentInviteNotFound:
                    return "该战队邀请不存在或已过期。";
                case (uint)match_error.KMatchTournamentMemberNotEnough:
                    return "战队人数不足首发要求，请先邀请同伴加入。";
                case (uint)match_error.KMatchTournamentLineupInvalid:
                    return "出战首发阵容人数或成员不合法，请重新选择。";
                case (uint)match_error.KMatchTournamentQualifierLimit:
                    return "本季海选场次已打满，静待全服淘汰赛开启。";
                case (uint)match_error.KMatchTournamentMatchNotReady:
                    return "当前暂无可开战的赛程或对手尚未就绪。";
                case (uint)match_error.KMatchTournamentAlreadyCheered:
                    return "本赛季免费助威机会已使用（每人每季 1 票）。";
                case (uint)match_error.KMatchTournamentRewardUnavailable:
                    return "暂无可领取的赛季奖励或奖励已发放。";
                case (uint)match_error.KMatchTournamentAlreadySalutedToday:
                    return "今日已向总冠军观道致敬，明日 0 点后可再次致敬。";
                case (uint)match_error.KMatchTournamentNoChampionToSalute:
                    return "当前暂无已诞生的总冠军可供观道致敬。";
                case (uint)match_error.KMatchTournamentSpectateCheerCooldown:
                    return "喝彩过于频繁，请稍候片刻再为道友加油。";
                case (uint)match_error.KMatchTournamentSpectateCheerInvalid:
                    return "当前对局不支持该喝彩选项，请稍后重试。";
                case (uint)common_error.KRateLimitExceeded:
                    return "操作过于频繁，请稍后再试。";
                case (uint)common_error.KServiceUnavailable:
                    return "赛事服务暂时繁忙，请稍后再试。";
                case (uint)common_error.KInvalidParameter:
                    return "请求参数无效，请刷新后重试。";
                default:
                    return $"锦标赛请求未完成（tip={tip.Id}），请稍后重试。";
            }
        }

        private bool BeginQuery(uint messageId, ref bool flag, string statusMessage, out int gen, out ulong player)
        {
            gen = 0;
            player = 0UL;
            if (_disposed) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0UL)
            {
                FailLocal(NotInGameMessage);
                return false;
            }
            if (flag) return false;
            if (!TryConsumeBudget(messageId)) return false;

            gen = _generation;
            player = PlayerId;
            flag = true;
            Status = statusMessage;
            LastError = string.Empty;
            LastErrorTipId = 0u;
            Changed?.Invoke();
            return true;
        }

        private bool BeginMutation(uint messageId, string statusMessage, out int gen, out ulong player)
        {
            gen = 0;
            player = 0UL;
            if (_disposed) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0UL)
            {
                FailLocal(NotInGameMessage);
                return false;
            }
            if (_mutating) return false;
            if (!TryConsumeBudget(messageId)) return false;

            gen = _generation;
            player = PlayerId;
            _mutating = true;
            Status = statusMessage;
            LastError = string.Empty;
            LastErrorTipId = 0u;
            Changed?.Invoke();
            return true;
        }

        private bool TryConsumeBudget(uint messageId)
        {
            if (_guard.IsCircuitOpen(messageId))
            {
                FailLocal("赛事服务暂时繁忙，请稍后再试。");
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

        private string FindSquadName(ulong squadId)
        {
            if (Standings != null)
            {
                for (int i = 0; i < Standings.Standings.Count; i++)
                {
                    if (Standings.Standings[i].SquadId == squadId) return Standings.Standings[i].Name;
                }
                if (Standings.MySquad != null && Standings.MySquad.SquadId == squadId) return Standings.MySquad.Name;
            }
            if (Overview?.MySquad != null && Overview.MySquad.SquadId == squadId) return Overview.MySquad.Name;
            return string.Empty;
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
            LastErrorTipId = 0u;
            LastError = message ?? string.Empty;
            Status = LastError;
            Changed?.Invoke();
        }

        private void FailServer(TipInfoMessage tip)
        {
            LastErrorTipId = tip?.Id ?? 0u;
            LastError = DescribeTip(tip);
            Status = LastError;
            Changed?.Invoke();
        }

        private void FailTransport(string prefix, string err)
        {
            LastErrorTipId = TryExtractServerTipId(err);
            LastError = DescribeTransport(prefix, err);
            Status = LastError;
            Changed?.Invoke();
        }

        private static uint TryExtractServerTipId(string err)
        {
            const string serverTip = "server tip=";
            if (!string.IsNullOrEmpty(err)
                && err.StartsWith(serverTip, StringComparison.Ordinal)
                && uint.TryParse(err.Substring(serverTip.Length), NumberStyles.None, CultureInfo.InvariantCulture, out uint tipId))
                return tipId;
            return 0u;
        }

        private bool IsCurrent(int generation, ulong player) =>
            !_disposed && generation == _generation && _net.IsReady && PlayerId == player
            && ReferenceEquals(_observedConnection, _connectionIdentity());

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeTransport(string prefix, string err)
        {
            if (string.IsNullOrEmpty(err)) return prefix;
            if (err == RpcGuard.CircuitOpenError) return "赛事服务暂时繁忙，请稍后再试。";
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
