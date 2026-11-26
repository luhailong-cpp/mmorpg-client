using System;
using System.Collections.Generic;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Guild
{
    /// <summary>
    /// 帮会全天帮战 V2 纯 C# 客户端状态与 RPC 协调层（零 UnityEngine 依赖，WebGL2/IL2CPP 安全）：
    /// - 覆盖 GuildService 485–494、499 全部 10 条 RPC 与 1 条推送；
    /// - 全部异步回调按 (connectionIdentity, playerId, generation) 三重围栏校验；
    /// - 所有倒计时统一由 EstimatedServerNowMs（服务端时间戳 + 本机单调时钟增量）锚定，绝不信任本机墙钟；
    /// - 并发抢点返回 kGuildWarTargetInBattle + offender_battle_id != 0 时触发 TargetAlreadyInBattle 供一键观战；
    /// - 结算未落账（settled=false）时按 [2, 3, 5, 8, 10, 12]s 有界退避轮询，通过 Tick() 在主线程推进。
    /// </summary>
    public sealed class GuildWarClient : IDisposable
    {
        public static GuildWarClient Instance { get; private set; }

        public static readonly double[] SettlementBackoffSeconds = { 2d, 3d, 5d, 8d, 10d, 12d };
        public const int MaxFieldInspectPlayers = 30;

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<long> _monotonicMs;

        private object _observedConnection;
        private ulong _observedPlayer;
        private int _generation;
        private bool _disposed;
        private ulong _nextRequestSeq = 1;

        private ulong _serverAnchorMs;
        private long _serverAnchorMonoMs;

        private ulong _pollingBattleId;
        private int _pollAttemptIndex;
        private long _nextPollMonoMs;
        private bool _pollRequestInFlight;

        public ulong PlayerId => _net.PlayerId;
        public GuildWarOverviewSnapshot Overview { get; private set; }
        public GuildWarBattleSettlementSnapshot ActiveSettlement { get; private set; }
        public IReadOnlyList<GuildWarFieldTargetSnapshot> FieldTargets { get; private set; } = Array.Empty<GuildWarFieldTargetSnapshot>();
        public ulong LastConflictBattleId { get; private set; }
        public ulong LastLaunchedBattleId { get; private set; }
        public bool OverviewLoading { get; private set; }
        public bool ActionInFlight { get; private set; }
        public bool SettlementPolling => _pollingBattleId != 0;
        public bool FeatureUnavailable { get; private set; }
        public string Status { get; private set; } = "点击刷新获取帮会争锋战况。";
        public string LastError { get; private set; } = string.Empty;
        public uint LastErrorTipId { get; private set; }

        public ulong EstimatedServerNowMs
        {
            get
            {
                if (_serverAnchorMs == 0) return 0;
                long delta = _monotonicMs() - _serverAnchorMonoMs;
                return delta > 0 ? _serverAnchorMs + (ulong)delta : _serverAnchorMs;
            }
        }

        public event Action Changed;
        public event Action<GuildWarAlertSnapshot> AlertReceived;
        public event Action<GuildWarBattleSettlementSnapshot> SettlementOpened;
        public event Action<ulong> TargetAlreadyInBattle;
        public event Action<ulong, GuildWarBattleKind> BattleLaunched;

        public GuildWarClient(
            IBattleTransport net,
            Func<object> connectionIdentity = null,
            Func<long> monotonicMs = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            if (monotonicMs == null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _monotonicMs = () => sw.ElapsedMilliseconds;
            }
            else
            {
                _monotonicMs = monotonicMs;
            }
            _observedConnection = _connectionIdentity();
            _observedPlayer = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
            _net.RegisterNotify(GuildServiceRpc.NotifyGuildWarAlertMessageId, HandleAlertMessage);
            Instance = this;
        }

        public void ObserveConnection()
        {
            if (_disposed) return;
            object conn = _connectionIdentity();
            ulong player = _net.PlayerId;
            if (!ReferenceEquals(conn, _observedConnection) || player != _observedPlayer)
            {
                _observedConnection = conn;
                _observedPlayer = player;
                Reset();
            }
        }

        public void Reset()
        {
            unchecked { _generation++; }
            Overview = null;
            ActiveSettlement = null;
            FieldTargets = Array.Empty<GuildWarFieldTargetSnapshot>();
            LastConflictBattleId = 0;
            LastLaunchedBattleId = 0;
            OverviewLoading = false;
            ActionInFlight = false;
            FeatureUnavailable = false;
            StopSettlementPolling();
            _serverAnchorMs = 0;
            _serverAnchorMonoMs = 0;
            Status = "点击刷新获取帮会争锋战况。";
            LastError = string.Empty;
            LastErrorTipId = 0;
            Changed?.Invoke();
        }

        public void ClearSettlement()
        {
            StopSettlementPolling();
            ActiveSettlement = null;
            Changed?.Invoke();
        }

        public void RefreshOverview()
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再查看帮会争锋。")) return;
            if (OverviewLoading) return;
            OverviewLoading = true;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Status = "正在同步帮会争锋战况…";
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            _net.Call(GuildServiceRpc.GetGuildWarOverviewMessageId,
                new GetGuildWarOverviewRequest(),
                GetGuildWarOverviewResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    OverviewLoading = false;
                    if (HasError(resp?.ErrorMessage))
                    {
                        ApplyError(resp.ErrorMessage);
                        return;
                    }
                    FeatureUnavailable = false;
                    AnchorServerTime(resp.ServerTimeMs);
                    Overview = new GuildWarOverviewSnapshot(resp);
                    Status = "帮会争锋战况已同步。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    OverviewLoading = false;
                    ApplyTransportError(err);
                });
        }

        public void SetPeaceMode(bool enabled)
        {
            var req = new SetGuildWarStanceRequest { SetPeaceMode = enabled };
            SendStance(req, enabled ? "已切换为和平免战模式。" : "已切换为战意模式，全天直打已就绪。");
        }

        public void SetOpenChallenge(bool enabled)
        {
            var req = new SetGuildWarStanceRequest { SetOpenChallenge = enabled };
            SendStance(req, enabled ? "已开启挂牌迎战。" : "已关闭挂牌迎战。");
        }

        private void SendStance(SetGuildWarStanceRequest req, string successText)
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再切换战意状态。")) return;
            if (ActionInFlight) return;
            ActionInFlight = true;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            _net.Call(GuildServiceRpc.SetGuildWarStanceMessageId, req, SetGuildWarStanceResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    if (HasError(resp?.ErrorMessage)) { ApplyError(resp.ErrorMessage); return; }
                    AnchorServerTime(resp.ServerTimeMs);
                    if (Overview != null && resp.MyStance != null)
                        Overview = Overview.WithStance(resp.MyStance);
                    Status = successText;
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    ApplyTransportError(err);
                });
        }

        public void RegisterMyGarrison() => SendGarrison(1u, 0UL, "守旗阵快照已登记更新（独立托管，不占用本人战斗）。");
        public void RemoveGarrison(ulong targetPlayerId = 0) => SendGarrison(2u, targetPlayerId, "已撤下守旗阵槽位。");
        public void PromoteGarrison(ulong targetPlayerId) => SendGarrison(3u, targetPlayerId, "已置顶该守旗阵槽位。");

        private void SendGarrison(uint action, ulong targetPlayerId, string successText)
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再管理守旗阵。")) return;
            if (ActionInFlight) return;
            ActionInFlight = true;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            var req = new RegisterGuildWarGarrisonRequest { Action = action, TargetPlayerId = targetPlayerId };
            _net.Call(GuildServiceRpc.RegisterGuildWarGarrisonMessageId, req, RegisterGuildWarGarrisonResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    if (HasError(resp?.ErrorMessage)) { ApplyError(resp.ErrorMessage); return; }
                    if (Overview != null) Overview = Overview.WithGarrisons(resp.GarrisonSlots);
                    Status = successText;
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    ApplyTransportError(err);
                });
        }

        public void StartQuickBattle(uint preferredLeylineId = 0) =>
            StartBattle(new StartGuildWarBattleRequest
            {
                TargetKind = GuildWarTargetKind.QuickAuto,
                LeylineId = preferredLeylineId,
                ClientRequestId = NextClientRequestId(),
            });

        public void StartLivePvpBattle(ulong targetPlayerId, uint leylineId = 0) =>
            StartBattle(new StartGuildWarBattleRequest
            {
                TargetKind = GuildWarTargetKind.LivePlayer,
                TargetPlayerId = targetPlayerId,
                LeylineId = leylineId,
                ClientRequestId = NextClientRequestId(),
            });

        public void StartLeylineSiege(uint leylineId) =>
            StartBattle(new StartGuildWarBattleRequest
            {
                TargetKind = GuildWarTargetKind.LeylineGarrison,
                LeylineId = leylineId,
                ClientRequestId = NextClientRequestId(),
            });

        public void StartBountyBattle(ulong bountyId, ulong targetPlayerId) =>
            StartBattle(new StartGuildWarBattleRequest
            {
                TargetKind = GuildWarTargetKind.Bounty,
                BountyId = bountyId,
                TargetPlayerId = targetPlayerId,
                ClientRequestId = NextClientRequestId(),
            });

        public void AttackFieldPlayer(ulong targetPlayerId, ulong bountyId = 0) =>
            StartBattle(new StartGuildWarBattleRequest
            {
                TargetKind = GuildWarTargetKind.FieldPlayer,
                TargetPlayerId = targetPlayerId,
                BountyId = bountyId,
                ClientRequestId = NextClientRequestId(),
            });

        private void StartBattle(StartGuildWarBattleRequest req)
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再发起帮战。")) return;
            if (ActionInFlight) return;
            ActionInFlight = true;
            LastConflictBattleId = 0;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Status = "正在直连帮战对局…";
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            _net.Call(GuildServiceRpc.StartGuildWarBattleMessageId, req, StartGuildWarBattleResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    if (HasError(resp?.ErrorMessage))
                    {
                        if (resp.ErrorMessage.Id == (uint)guild_error.KGuildWarTargetInBattle && resp.OffenderBattleId != 0)
                        {
                            LastConflictBattleId = resp.OffenderBattleId;
                            ApplyError(resp.ErrorMessage);
                            TargetAlreadyInBattle?.Invoke(resp.OffenderBattleId);
                            return;
                        }
                        ApplyError(resp.ErrorMessage);
                        return;
                    }
                    LastLaunchedBattleId = resp.BattleId;
                    Status = "已开启" + GuildWarText.BattleKindLabel(resp.WarBattleKind) + "！";
                    Changed?.Invoke();
                    if (resp.BattleId != 0) BattleLaunched?.Invoke(resp.BattleId, resp.WarBattleKind);
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    ApplyTransportError(err);
                });
        }

        public void ClaimMilestone(uint milestoneId)
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再领取战功宝箱。")) return;
            if (ActionInFlight || milestoneId == 0) return;
            ActionInFlight = true;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            var req = new ClaimGuildWarMilestoneRequest { MilestoneId = milestoneId, ClientRequestId = NextClientRequestId() };
            _net.Call(GuildServiceRpc.ClaimGuildWarMilestoneMessageId, req, ClaimGuildWarMilestoneResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    if (HasError(resp?.ErrorMessage)) { ApplyError(resp.ErrorMessage); return; }
                    if (Overview != null && resp.Milestone != null) Overview = Overview.WithMilestone(resp.Milestone);
                    Status = resp.AssetOrderStatus == GuildAssetOrderStatus.Pending
                        ? "战功宝箱已登记发放，若背包已满请腾出空位后自动到账。"
                        : "已领取本周战功宝箱！";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    ApplyTransportError(err);
                });
        }

        public void ClaimDividend()
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再领取灵脉分红。")) return;
            if (ActionInFlight) return;
            ActionInFlight = true;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            var req = new ClaimGuildWarDividendRequest { ClientRequestId = NextClientRequestId() };
            _net.Call(GuildServiceRpc.ClaimGuildWarDividendMessageId, req, ClaimGuildWarDividendResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    if (HasError(resp?.ErrorMessage)) { ApplyError(resp.ErrorMessage); return; }
                    if (Overview != null && resp.Dividend != null) Overview = Overview.WithDividend(resp.Dividend);
                    Status = resp.AssetOrderStatus == GuildAssetOrderStatus.Pending
                        ? $"已登记领取 {resp.ClaimedCount} 份灵脉分红，正在确认入账。"
                        : $"已领取 {resp.ClaimedCount} 份灵脉分红！";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    ApplyTransportError(err);
                });
        }

        public void PostBounty(ulong targetPlayerId, GuildWarBountyTier tier, bool publicBoard)
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再发布追杀令。")) return;
            if (ActionInFlight || targetPlayerId == 0 || tier == GuildWarBountyTier.Unspecified) return;
            ActionInFlight = true;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            var req = new PostGuildWarBountyRequest
            {
                TargetPlayerId = targetPlayerId,
                Tier = tier,
                PublicBoard = publicBoard,
                ClientRequestId = NextClientRequestId(),
            };
            _net.Call(GuildServiceRpc.PostGuildWarBountyMessageId, req, PostGuildWarBountyResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    if (HasError(resp?.ErrorMessage)) { ApplyError(resp.ErrorMessage); return; }
                    if (Overview != null && resp.Bounty != null)
                        Overview = Overview.WithUpsertedBounty(new GuildWarBountySnapshot(resp.Bounty));
                    Status = resp.Bounty != null && resp.Bounty.Status == GuildWarBountyStatus.EscrowPending
                        ? "追杀令已提交，赏金托管扣款确认中…"
                        : "江湖追杀令已发布！";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    ApplyTransportError(err);
                });
        }

        public void TrackBounty(ulong bountyId)
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再追踪追杀目标。")) return;
            if (ActionInFlight || bountyId == 0) return;
            ActionInFlight = true;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            var req = new TrackGuildWarBountyRequest { BountyId = bountyId };
            _net.Call(GuildServiceRpc.TrackGuildWarBountyMessageId, req, TrackGuildWarBountyResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    if (HasError(resp?.ErrorMessage)) { ApplyError(resp.ErrorMessage); return; }
                    if (Overview != null)
                    {
                        for (int i = 0; i < Overview.Bounties.Count; i++)
                        {
                            if (Overview.Bounties[i].BountyId == bountyId)
                            {
                                Overview = Overview.WithUpsertedBounty(Overview.Bounties[i].WithTrackResult(resp));
                                break;
                            }
                        }
                    }
                    Status = !resp.TargetOnline
                        ? "目标当前暂未露面（离线）。"
                        : resp.InSafeZone
                            ? "目标正藏身归元城安全区。"
                            : $"目标正在【{GuildWarText.SceneName(resp.SceneConfigId)} · {resp.ChannelNo}线】出没！";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    ApplyTransportError(err);
                });
        }

        public void QueryBattleResult(ulong battleId)
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再查询帮战结算。")) return;
            if (battleId == 0) return;
            _pollingBattleId = battleId;
            _pollAttemptIndex = 0;
            _nextPollMonoMs = _monotonicMs();
            _pollRequestInFlight = false;
            SendBattleResultQuery(battleId);
        }

        public void InspectFieldWarTargets(IReadOnlyList<ulong> playerIds)
        {
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0 || playerIds == null || playerIds.Count == 0) return;
            var req = new InspectFieldWarTargetsRequest();
            int count = Math.Min(MaxFieldInspectPlayers, playerIds.Count);
            for (int i = 0; i < count; i++)
                if (playerIds[i] != 0) req.PlayerIds.Add(playerIds[i]);
            if (req.PlayerIds.Count == 0) return;

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            _net.Call(GuildServiceRpc.InspectFieldWarTargetsMessageId, req, InspectFieldWarTargetsResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    if (HasError(resp?.ErrorMessage)) return;
                    var list = new List<GuildWarFieldTargetSnapshot>(resp.Targets.Count);
                    for (int i = 0; i < resp.Targets.Count; i++)
                        list.Add(new GuildWarFieldTargetSnapshot(resp.Targets[i]));
                    FieldTargets = list.AsReadOnly();
                    Changed?.Invoke();
                },
                _ => { });
        }

        public void IssueFeud(ulong targetGuildId)
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再下发宿敌约战战书。")) return;
            if (ActionInFlight || targetGuildId == 0) return;
            ActionInFlight = true;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            var req = new IssueGuildWarFeudRequest { TargetGuildId = targetGuildId, ClientRequestId = NextClientRequestId() };
            _net.Call(GuildServiceRpc.IssueGuildWarFeudMessageId, req, IssueGuildWarFeudResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    if (HasError(resp?.ErrorMessage)) { ApplyError(resp.ErrorMessage); return; }
                    if (Overview != null) Overview = Overview.WithFeud(resp.Feud, resp.NextIssueAtMs);
                    Status = "已向敌帮下发 2 小时宿敌约战战书，等待对方管理 15 分钟内应战！";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    ApplyTransportError(err);
                });
        }

        public void RespondFeud(ulong feudId, bool accept)
        {
            ObserveConnection();
            if (!EnsureReady("请进入角色后再应答宿敌约战战书。")) return;
            if (ActionInFlight || feudId == 0) return;
            ActionInFlight = true;
            LastError = string.Empty;
            LastErrorTipId = 0;
            Changed?.Invoke();

            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            var req = new RespondGuildWarFeudRequest { FeudId = feudId, Accept = accept };
            _net.Call(GuildServiceRpc.RespondGuildWarFeudMessageId, req, RespondGuildWarFeudResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    if (HasError(resp?.ErrorMessage)) { ApplyError(resp.ErrorMessage); return; }
                    if (Overview != null) Overview = Overview.WithFeud(resp.Feud);
                    Status = accept ? "已接战！2 小时宿敌约战正式打响（收益 +25% · 前 3 场免递减）！" : "已婉拒/撤回约战战书。";
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    ActionInFlight = false;
                    ApplyTransportError(err);
                });
        }

        public void Tick()
        {
            if (_disposed) return;
            ObserveConnection();
            if (_pollingBattleId == 0 || _pollRequestInFlight) return;
            if (!_net.IsReady || PlayerId == 0) return;
            if (_monotonicMs() < _nextPollMonoMs) return;
            SendBattleResultQuery(_pollingBattleId);
        }

        private void SendBattleResultQuery(ulong battleId)
        {
            if (_pollRequestInFlight) return;
            _pollRequestInFlight = true;
            int gen = _generation;
            ulong player = PlayerId;
            object conn = _connectionIdentity();
            _net.Call(GuildServiceRpc.GetGuildWarBattleResultMessageId,
                new GetGuildWarBattleResultRequest { BattleId = battleId },
                GetGuildWarBattleResultResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player, conn) || _pollingBattleId != battleId) return;
                    _pollRequestInFlight = false;
                    if (HasError(resp?.ErrorMessage))
                    {
                        StopSettlementPolling();
                        ApplyError(resp.ErrorMessage);
                        return;
                    }
                    var snap = new GuildWarBattleSettlementSnapshot(resp);
                    ActiveSettlement = snap;
                    if (snap.Settled)
                    {
                        StopSettlementPolling();
                        Status = "帮战结算已确认入账。";
                        Changed?.Invoke();
                        SettlementOpened?.Invoke(snap);
                    }
                    else
                    {
                        ScheduleNextSettlementPoll();
                        Status = "部分帮战奖励确认中，正在自动同步…";
                        Changed?.Invoke();
                        if (_pollAttemptIndex == 1) SettlementOpened?.Invoke(snap);
                    }
                },
                err =>
                {
                    if (!IsCurrent(gen, player, conn)) return;
                    _pollRequestInFlight = false;
                    ScheduleNextSettlementPoll();
                    ApplyTransportError(err);
                });
        }

        private void ScheduleNextSettlementPoll()
        {
            if (_pollAttemptIndex >= SettlementBackoffSeconds.Length)
            {
                StopSettlementPolling();
                Status = "结算同步超时，请稍后点刷新查看。";
                return;
            }
            long delayMs = (long)(SettlementBackoffSeconds[_pollAttemptIndex] * 1000d);
            _pollAttemptIndex++;
            _nextPollMonoMs = _monotonicMs() + delayMs;
        }

        private void StopSettlementPolling()
        {
            _pollingBattleId = 0;
            _pollAttemptIndex = 0;
            _nextPollMonoMs = 0;
            _pollRequestInFlight = false;
        }

        private void HandleAlertMessage(MessageContent content)
        {
            if (_disposed || content == null || content.SerializedMessage == null) return;
            GuildWarAlertS2C msg;
            try { msg = GuildWarAlertS2C.Parser.ParseFrom(content.SerializedMessage); }
            catch (InvalidProtocolBufferException) { return; }
            var snap = new GuildWarAlertSnapshot(msg);
            Status = GuildWarText.FormatAlert(snap);
            Changed?.Invoke();
            AlertReceived?.Invoke(snap);
        }

        private void HandleDisconnected()
        {
            if (_disposed) return;
            Reset();
        }

        private void AnchorServerTime(ulong serverTimeMs)
        {
            if (serverTimeMs == 0) return;
            _serverAnchorMs = serverTimeMs;
            _serverAnchorMonoMs = _monotonicMs();
        }

        private ulong NextClientRequestId()
        {
            ulong seq = _nextRequestSeq++;
            ulong now = (ulong)Math.Max(1L, _monotonicMs());
            ulong id = (now << 16) ^ (seq & 0xFFFFUL);
            return id == 0 ? 1UL : id;
        }

        private bool EnsureReady(string notReadyMessage)
        {
            if (_disposed) return false;
            if (!_net.IsReady || PlayerId == 0)
            {
                LastError = notReadyMessage;
                Status = notReadyMessage;
                Changed?.Invoke();
                return false;
            }
            return true;
        }

        private bool IsCurrent(int generation, ulong player, object connection) =>
            !_disposed
            && generation == _generation
            && _net.IsReady
            && PlayerId == player
            && ReferenceEquals(connection, _connectionIdentity());

        private static bool HasError(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private void ApplyError(TipInfoMessage tip)
        {
            LastErrorTipId = tip.Id;
            FeatureUnavailable = tip.Id == (uint)common_error.KFeatureUnavailable
                || tip.Id == (uint)guild_error.KGuildWarNotOpen;
            LastError = GuildWarText.DescribeError(tip);
            Status = LastError;
            Changed?.Invoke();
        }

        private void ApplyTransportError(string error)
        {
            LastErrorTipId = 0;
            LastError = string.IsNullOrWhiteSpace(error) ? "网络通信异常，请稍后重试。" : "网络通信异常：" + error;
            Status = LastError;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopSettlementPolling();
            _net.Disconnected -= HandleDisconnected;
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
