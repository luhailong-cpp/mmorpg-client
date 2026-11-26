using System;
using System.Collections.Generic;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Jianghu
{
    /// <summary>
    /// 江湖恩怨与天墉生死状一期客户端状态与网络适配层（RPC 547–557，走大厅 gate 路由）。
    ///
    /// 设计约束：
    /// - 零 UnityEngine 依赖，100% WebGL2 单线程与 IL2CPP AOT 安全；
    /// - 所有异步回调与 S2C 推送均受 (connectionIdentity, playerId, generation) 三重围栏保护；
    /// - 清除败绩印记使用非 0 幂等键 ClientRequestKeys，仅在超时/1003 时原样重发至多 1 次；
    /// - 断线或换角自动复位状态并丢弃所有在途回调。
    /// </summary>
    public sealed class JianghuClient : IDisposable
    {
        public static JianghuClient Instance { get; private set; }

        public const float MinReadIntervalSeconds = 1.0f;
        public const float MinWriteIntervalSeconds = 0.5f;
        public const float RateLimitBackoffSeconds = 1.5f;

        public const string UnsupportedMessage = "当前服务器尚未开放天墉生死擂。";
        public const string RecoveryMessage = "网络异常，洗尘操作结果未知。请重新连接或重登后核对余额与背包。";
        public const string TooFastMessage = "操作太快了，请稍候再试。";
        private const string ServerTipPrefix = "server tip=";

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private readonly JianghuMessageIds _ids;

        private readonly List<global::Match.JianghuGrudgeEntry> _grudges = new();
        private readonly List<global::Match.JianghuDuelInviteView> _activeInvites = new();

        private object _observedConnection;
        private ulong _observedPlayerId;
        private int _generation;
        private bool _disposed;
        private bool _inBattle;

        private ulong _anchorServerMs;
        private float _anchorClockSec;
        private float _nextProfileAt = float.NegativeInfinity;
        private float _nextGrudgesAt = float.NegativeInfinity;
        private float _nextGuildWallAt = float.NegativeInfinity;
        private float _nextWriteAt = float.NegativeInfinity;
        private bool _profileQueued;
        private bool _grudgesQueued;
        private bool _guildWallLoadedOnce;
        private ulong _dismissedPopupInviteId;

        public event Action Changed;
        public event Action<global::BagInfo> BagReceived;
        public event Action<global::Match.JianghuDuelInviteView> InvitePopupArrived;
        public event Action<ulong> DuelMatched;

        public ulong PlayerId => _net.PlayerId;
        public global::JianghuProfileView Profile { get; private set; }
        public IReadOnlyList<global::Match.JianghuGrudgeEntry> Grudges => _grudges;
        public IReadOnlyList<global::Match.JianghuDuelInviteView> ActiveInvites => _activeInvites;
        public global::Guildpb.GetGuildJianghuGrudgeWallResponse GuildWall { get; private set; }
        public string GuildWallHint { get; private set; } = string.Empty;

        public bool ProfilePending { get; private set; }
        public bool GrudgesPending { get; private set; }
        public bool GuildWallPending { get; private set; }
        public bool WritePending { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public bool StatusIsError { get; private set; }
        public uint LastErrorTipId { get; private set; }

        public bool IsSupported => _ids.IsSupported;
        public bool Ready => IsSupported && !_disposed && _net.IsReady && _net.PlayerId != 0;

        public bool InBattle
        {
            get => _inBattle;
            set
            {
                if (_inBattle == value) return;
                _inBattle = value;
                Changed?.Invoke();
            }
        }

        public ulong EstimatedServerNowMs => JianghuModels.EstimateServerNowMs(_anchorServerMs, _anchorClockSec, _clock());

        public global::Match.JianghuDuelInviteView PendingActionInvite
        {
            get
            {
                ulong self = _net.PlayerId;
                if (self == 0) return null;
                ulong nowMs = EstimatedServerNowMs;
                for (int i = 0; i < _activeInvites.Count; i++)
                {
                    var inv = _activeInvites[i];
                    if (inv == null) continue;
                    if (inv.DuelInviteId == _dismissedPopupInviteId) continue;
                    if (JianghuModels.InviteNeedsMyAction(inv, self) == JianghuInviteActionKind.None) continue;
                    ulong deadline = JianghuModels.InviteDeadlineMs(inv);
                    if (nowMs > 0 && deadline > 0 && nowMs >= deadline) continue;
                    return inv;
                }
                return null;
            }
        }

        public JianghuClient(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<float> clock,
            JianghuMessageIds? messageIds = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ids = messageIds ?? JianghuMessageIds.Generated;
            _observedConnection = _connectionIdentity();
            _observedPlayerId = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;

            if (_ids.NotifyInvite != 0)
                _net.RegisterNotify(_ids.NotifyInvite, HandleNotifyInviteRaw);
            if (_ids.NotifyStatus != 0)
                _net.RegisterNotify(_ids.NotifyStatus, HandleNotifyStatusRaw);

            if (!IsSupported) SetStatus(UnsupportedMessage, false, 0);
        }

        public static JianghuClient Attach(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<float> clock,
            JianghuMessageIds? messageIds = null)
        {
            Instance?.Dispose();
            Instance = new JianghuClient(transport, connectionIdentity, clock, messageIds);
            return Instance;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_generation;
            ProfilePending = GrudgesPending = GuildWallPending = WritePending = false;
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        public void SuppressPopupFor(ulong duelInviteId)
        {
            _dismissedPopupInviteId = duelInviteId;
            Changed?.Invoke();
        }

        public void RefreshAll(bool force = false)
        {
            RequestProfile(force);
            RequestGrudges(force);
            RequestGuildWall(force);
        }

        public void RequestProfile(bool force = false)
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            if (!Ready || ProfilePending) return;
            float now = _clock();
            if (!force && now < _nextProfileAt)
            {
                _profileQueued = true;
                return;
            }
            SendGetProfile(now);
        }

        public void RequestGrudges(bool force = false)
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            if (!Ready || GrudgesPending) return;
            float now = _clock();
            if (!force && now < _nextGrudgesAt)
            {
                _grudgesQueued = true;
                return;
            }
            SendListGrudges(now);
        }

        public void RequestGuildWall(bool force = false)
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            if (!Ready || GuildWallPending) return;
            float now = _clock();
            if (!force && now < _nextGuildWallAt) return;

            GuildWallPending = true;
            _nextGuildWallAt = now + MinReadIntervalSeconds;
            int generation = _generation;

            _net.Call(_ids.GetGuildGrudgeWall, new global::Guildpb.GetGuildJianghuGrudgeWallRequest(),
                global::Guildpb.GetGuildJianghuGrudgeWallResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    GuildWallPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        GuildWall = null;
                        GuildWallHint = "加入帮派后可查看本帮与其他帮派的生死状恩怨墙。";
                        Changed?.Invoke();
                        return;
                    }
                    GuildWall = resp;
                    _guildWallLoadedOnce = true;
                    GuildWallHint = resp == null || resp.GuildId == 0
                        ? "当前尚未加入帮派，暂无帮派恩怨墙记录。"
                        : string.Empty;
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    GuildWallPending = false;
                    GuildWallHint = "帮派恩怨墙同步失败，请稍后刷新。";
                    Changed?.Invoke();
                });
        }

        public bool SetRefuseStrangers(bool refuse, Action<bool> completed = null)
        {
            if (!PrepareSimpleWrite(completed)) return false;

            WritePending = true;
            _nextWriteAt = _clock() + MinWriteIntervalSeconds;
            int generation = _generation;
            SetStatus(refuse ? "正在开启闭关免战…" : "正在关闭闭关免战…", false, 0);
            Changed?.Invoke();

            _net.Call(_ids.SetRefuseStrangers,
                new global::SetJianghuRefuseStrangersRequest { RefuseStrangers = refuse },
                global::SetJianghuRefuseStrangersResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleTipFailure(tipId);
                        Changed?.Invoke();
                        completed?.Invoke(false);
                        return;
                    }
                    if (resp?.Profile != null) ApplyProfile(resp.Profile);
                    SetStatus(refuse
                        ? "已开启闭关免战，陌生人无法向你递送江湖战书。"
                        : "已关闭闭关免战，恢复正常江湖约战状态。", false, 0);
                    Changed?.Invoke();
                    completed?.Invoke(true);
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    HandleSimpleTransportError(error, "切换闭关免战失败，请稍后重试。");
                    Changed?.Invoke();
                    completed?.Invoke(false);
                });
            return true;
        }

        public bool ClearDefeatMark(global::JianghuClearMarkMode mode, Action<bool> completed = null)
        {
            if (!PrepareSimpleWrite(completed, checkReconnect: true)) return false;
            if (mode != global::JianghuClearMarkMode.Silver && mode != global::JianghuClearMarkMode.ItemCharm)
            {
                RejectLocally(JianghuModels.DescribeTip((uint)global::common_error.KInvalidParameter),
                    (uint)global::common_error.KInvalidParameter);
                completed?.Invoke(false);
                return false;
            }
            if (Profile != null && !Profile.HasActiveDefeatMark)
            {
                RejectLocally(JianghuModels.DescribeTip((uint)global::jianghu_error.KJianghuNoActiveDefeatMark),
                    (uint)global::jianghu_error.KJianghuNoActiveDefeatMark);
                completed?.Invoke(false);
                return false;
            }
            if (!ClientRequestKeys.TryCreate(out ulong requestKey))
            {
                RejectLocally(ClientRequestKeys.UnavailableText, 0);
                completed?.Invoke(false);
                return false;
            }

            WritePending = true;
            _nextWriteAt = _clock() + MinWriteIntervalSeconds;
            int generation = _generation;
            SetStatus(mode == global::JianghuClearMarkMode.ItemCharm
                ? "正在使用清风洗尘符洗去印记…"
                : "正在消耗银两洗去印记…", false, 0);
            Changed?.Invoke();

            var request = new global::ClearJianghuDefeatMarkRequest
            {
                ClientRequestId = requestKey,
                Mode = mode,
            };

            int attempt = 0;
            bool resent = false;

            void Send()
            {
                int mine = ++attempt;
                _net.Call(_ids.ClearDefeatMark, request, global::ClearJianghuDefeatMarkResponse.Parser,
                    resp =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        WritePending = false;
                        uint tipId = resp?.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            if (tipId == (uint)global::common_error.KInvalidParameter)
                                ClientRequestKeys.ReportKeyReused("ClearJianghuDefeatMark");
                            HandleTipFailure(tipId);
                            Changed?.Invoke();
                            completed?.Invoke(false);
                            return;
                        }
                        if (resp?.Profile != null) ApplyProfile(resp.Profile);
                        SetStatus(mode == global::JianghuClearMarkMode.ItemCharm
                            ? "清风拂尘，已抹去败绩印记并获得 30 分钟洒脱徽记。"
                            : "已抹去当前败绩印记，重整旗鼓再战江湖。", false, 0);
                        if (resp?.Bag != null) BagReceived?.Invoke(resp.Bag);
                        Changed?.Invoke();
                        completed?.Invoke(true);
                    },
                    error =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        if (!resent && ClientRequestKeys.ShouldResendOnce(error))
                        {
                            resent = true;
                            Send();
                            return;
                        }
                        WritePending = false;
                        uint tipId = ParseEnvelopeTip(error);
                        if (!resent && string.Equals(error, RpcGuard.CircuitOpenError, StringComparison.Ordinal))
                            SetStatus("洗尘服务暂时繁忙，请稍后重试。", true, 0);
                        else if (!resent && tipId != 0 && tipId != (uint)global::common_error.KServiceUnavailable)
                            HandleTipFailure(tipId);
                        else
                        {
                            RequiresReconnect = true;
                            SetStatus(RecoveryMessage, true, tipId);
                        }
                        Changed?.Invoke();
                        completed?.Invoke(false);
                    });
            }

            Send();
            return true;
        }

        public bool CreateDuelInvite(
            ulong targetPlayerId,
            global::Match.JianghuWritTier tier,
            IReadOnlyList<ulong> challengerMemberIds = null,
            IReadOnlyList<ulong> targetMemberIds = null,
            Action<bool> completed = null)
        {
            if (!PrepareSimpleWrite(completed)) return false;
            if (targetPlayerId == 0 || targetPlayerId == _net.PlayerId)
            {
                RejectLocally("请输入有效的对方玩家 ID，且不可向自己递送战书。",
                    (uint)global::common_error.KInvalidParameter);
                completed?.Invoke(false);
                return false;
            }
            if (tier != global::Match.JianghuWritTier.Silver && tier != global::Match.JianghuWritTier.GoldItem)
            {
                RejectLocally(JianghuModels.DescribeTip((uint)global::common_error.KInvalidParameter),
                    (uint)global::common_error.KInvalidParameter);
                completed?.Invoke(false);
                return false;
            }

            var req = new global::Match.CreateJianghuDuelInviteRequest
            {
                TargetPlayerId = targetPlayerId,
                WritTier = tier,
            };
            if (challengerMemberIds != null)
            {
                for (int i = 0; i < challengerMemberIds.Count; i++)
                {
                    if (challengerMemberIds[i] != 0) req.ChallengerMemberIds.Add(challengerMemberIds[i]);
                }
            }
            if (targetMemberIds != null)
            {
                for (int i = 0; i < targetMemberIds.Count; i++)
                {
                    if (targetMemberIds[i] != 0) req.TargetMemberIds.Add(targetMemberIds[i]);
                }
            }

            WritePending = true;
            _nextWriteAt = _clock() + MinWriteIntervalSeconds;
            int generation = _generation;
            SetStatus("正在递送江湖战书…", false, 0);
            Changed?.Invoke();

            _net.Call(_ids.CreateDuelInvite, req, global::Match.CreateJianghuDuelInviteResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleTipFailure(tipId);
                        Changed?.Invoke();
                        completed?.Invoke(false);
                        return;
                    }
                    if (resp?.Invite != null)
                    {
                        UpsertInvite(resp.Invite);
                        SetStatus(resp.Invite.State == global::Match.JianghuDuelInviteState.AwaitingChallengerTeam
                            ? "小队战书已起草，正等待同队道友 30 秒内确认参战。"
                            : "江湖战书已送达对方，待其应战后方正式成局扣费。", false, 0);
                    }
                    else
                    {
                        SetStatus("江湖战书已递出。", false, 0);
                    }
                    Changed?.Invoke();
                    completed?.Invoke(true);
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    HandleSimpleTransportError(error, "递送江湖战书失败，请稍后重试。");
                    Changed?.Invoke();
                    completed?.Invoke(false);
                });
            return true;
        }

        public bool ConfirmTeamMember(ulong duelInviteId, bool agree, Action<bool> completed = null)
        {
            if (!PrepareSimpleWrite(completed)) return false;
            if (duelInviteId == 0)
            {
                RejectLocally(JianghuModels.DescribeTip((uint)global::jianghu_error.KJianghuInvitePendingOrExpired),
                    (uint)global::jianghu_error.KJianghuInvitePendingOrExpired);
                completed?.Invoke(false);
                return false;
            }

            WritePending = true;
            _nextWriteAt = _clock() + MinWriteIntervalSeconds;
            int generation = _generation;
            SetStatus(agree ? "正在确认小队参战…" : "正在退出本封战书…", false, 0);
            Changed?.Invoke();

            _net.Call(_ids.ConfirmDuelMember,
                new global::Match.ConfirmJianghuDuelMemberRequest { DuelInviteId = duelInviteId, Agree = agree },
                global::Match.ConfirmJianghuDuelMemberResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleTipFailure(tipId);
                        if (tipId == (uint)global::jianghu_error.KJianghuInvitePendingOrExpired)
                            RemoveInvite(duelInviteId);
                        Changed?.Invoke();
                        completed?.Invoke(false);
                        return;
                    }
                    if (resp?.Invite != null)
                    {
                        ApplyInviteUpdate(resp.Invite, null);
                    }
                    else if (!agree)
                    {
                        RemoveInvite(duelInviteId);
                    }
                    SetStatus(agree ? "已确认参战，等待双方集结成局。" : "已退出本封战书，整单作废且无扣费。", false, 0);
                    Changed?.Invoke();
                    completed?.Invoke(true);
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    HandleSimpleTransportError(error, "提交确认失败，请稍后重试。");
                    Changed?.Invoke();
                    completed?.Invoke(false);
                });
            return true;
        }

        public bool RespondDuelInvite(ulong duelInviteId, bool accept, Action<bool> completed = null)
        {
            if (!PrepareSimpleWrite(completed)) return false;
            if (duelInviteId == 0)
            {
                RejectLocally(JianghuModels.DescribeTip((uint)global::jianghu_error.KJianghuInvitePendingOrExpired),
                    (uint)global::jianghu_error.KJianghuInvitePendingOrExpired);
                completed?.Invoke(false);
                return false;
            }

            WritePending = true;
            _nextWriteAt = _clock() + MinWriteIntervalSeconds;
            int generation = _generation;
            SetStatus(accept ? "正在应战生死状…" : "正在婉拒战书…", false, 0);
            Changed?.Invoke();

            _net.Call(_ids.RespondDuelInvite,
                new global::Match.RespondJianghuDuelInviteRequest { DuelInviteId = duelInviteId, Accept = accept },
                global::Match.RespondJianghuDuelInviteResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleTipFailure(tipId);
                        if (tipId == (uint)global::jianghu_error.KJianghuInvitePendingOrExpired)
                            RemoveInvite(duelInviteId);
                        Changed?.Invoke();
                        completed?.Invoke(false);
                        return;
                    }
                    if (resp?.Invite != null)
                        ApplyInviteUpdate(resp.Invite, null);
                    else if (!accept)
                        RemoveInvite(duelInviteId);
                    SetStatus(accept ? "已应战，正在进入天墉生死擂！" : "已婉拒本次战书，本单无任何消耗。", false, 0);
                    Changed?.Invoke();
                    completed?.Invoke(true);
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    HandleSimpleTransportError(error, "回复战书失败，请稍后重试。");
                    Changed?.Invoke();
                    completed?.Invoke(false);
                });
            return true;
        }

        public bool DismissGrudge(ulong peerPlayerId, Action<bool> completed = null)
        {
            if (!PrepareSimpleWrite(completed)) return false;
            if (peerPlayerId == 0)
            {
                RejectLocally(JianghuModels.DescribeTip((uint)global::jianghu_error.KJianghuGrudgeNotFound),
                    (uint)global::jianghu_error.KJianghuGrudgeNotFound);
                completed?.Invoke(false);
                return false;
            }

            WritePending = true;
            _nextWriteAt = _clock() + MinWriteIntervalSeconds;
            int generation = _generation;
            SetStatus("正在一笔勾销恩怨记录…", false, 0);
            Changed?.Invoke();

            _net.Call(_ids.DismissGrudge,
                new global::Match.DismissJianghuGrudgeRequest { PeerPlayerId = peerPlayerId },
                global::Match.DismissJianghuGrudgeResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleTipFailure(tipId);
                        Changed?.Invoke();
                        completed?.Invoke(false);
                        return;
                    }
                    for (int i = _grudges.Count - 1; i >= 0; i--)
                    {
                        if (_grudges[i] != null && _grudges[i].PeerPlayerId == peerPlayerId)
                            _grudges.RemoveAt(i);
                    }
                    SetStatus("已单向抹去该条恩怨记录，过往恩怨一笔勾销。", false, 0);
                    Changed?.Invoke();
                    completed?.Invoke(true);
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    WritePending = false;
                    HandleSimpleTransportError(error, "移除恩怨记录失败，请稍后重试。");
                    Changed?.Invoke();
                    completed?.Invoke(false);
                });
            return true;
        }

        public void HandleSceneEntered()
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            if (!Ready) return;
            RequestProfile(false);
            RequestGrudges(false);
        }

        public void HandleBattleSettled()
        {
            if (_disposed || !IsSupported || !Ready) return;
            RequestProfile(true);
            RequestGrudges(true);
            if (_guildWallLoadedOnce) RequestGuildWall(true);
        }

        public void HandleDisconnected()
        {
            if (_disposed) return;
            ResetSession();
            Changed?.Invoke();
        }

        public void Tick()
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            float now = _clock();
            if (!ProfilePending && _profileQueued && Ready && now >= _nextProfileAt)
                SendGetProfile(now);
            if (!GrudgesPending && _grudgesQueued && Ready && now >= _nextGrudgesAt)
                SendListGrudges(now);
        }

        private void HandleNotifyInviteRaw(MessageContent mc)
        {
            if (_disposed || mc == null) return;
            ObserveSessionBoundary();
            if (!Ready) return;
            global::Match.JianghuDuelInviteS2C push;
            try { push = global::Match.JianghuDuelInviteS2C.Parser.ParseFrom(mc.SerializedMessage); }
            catch (InvalidProtocolBufferException) { return; }
            if (push?.Invite == null || push.Invite.DuelInviteId == 0) return;

            if (_dismissedPopupInviteId == push.Invite.DuelInviteId)
                _dismissedPopupInviteId = 0;

            UpsertInvite(push.Invite);
            if (JianghuModels.InviteNeedsMyAction(push.Invite, _net.PlayerId) != JianghuInviteActionKind.None)
                InvitePopupArrived?.Invoke(push.Invite);
            Changed?.Invoke();
        }

        private void HandleNotifyStatusRaw(MessageContent mc)
        {
            if (_disposed || mc == null) return;
            ObserveSessionBoundary();
            if (!Ready) return;
            global::Match.JianghuDuelStatusS2C push;
            try { push = global::Match.JianghuDuelStatusS2C.Parser.ParseFrom(mc.SerializedMessage); }
            catch (InvalidProtocolBufferException) { return; }
            if (push?.Invite == null || push.Invite.DuelInviteId == 0) return;

            ApplyInviteUpdate(push.Invite, push.ReasonCode);
            Changed?.Invoke();
        }

        private void SendGetProfile(float now)
        {
            _profileQueued = false;
            ProfilePending = true;
            _nextProfileAt = now + MinReadIntervalSeconds;
            int generation = _generation;

            _net.Call(_ids.GetProfile, new global::GetJianghuProfileRequest(),
                global::GetJianghuProfileResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    ProfilePending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleTipFailure(tipId);
                        Changed?.Invoke();
                        return;
                    }
                    if (resp?.Profile != null) ApplyProfile(resp.Profile);
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    ProfilePending = false;
                    HandleSimpleTransportError(error, "读取江湖档案失败，请稍后重试。");
                    Changed?.Invoke();
                });
        }

        private void SendListGrudges(float now)
        {
            _grudgesQueued = false;
            GrudgesPending = true;
            _nextGrudgesAt = now + MinReadIntervalSeconds;
            int generation = _generation;

            _net.Call(_ids.ListGrudges, new global::Match.ListJianghuGrudgesRequest(),
                global::Match.ListJianghuGrudgesResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    GrudgesPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleTipFailure(tipId);
                        Changed?.Invoke();
                        return;
                    }
                    if (resp != null)
                    {
                        UpdateServerClock(resp.ServerTimeMs);
                        _grudges.Clear();
                        for (int i = 0; i < resp.Grudges.Count; i++)
                        {
                            if (resp.Grudges[i] != null) _grudges.Add(resp.Grudges[i]);
                        }
                        _activeInvites.Clear();
                        for (int i = 0; i < resp.ActiveInvites.Count; i++)
                        {
                            var inv = resp.ActiveInvites[i];
                            if (inv != null && !JianghuModels.IsInviteTerminal(inv.State))
                            {
                                UpdateServerClock(inv.ServerTimeMs);
                                _activeInvites.Add(inv);
                            }
                        }
                    }
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    GrudgesPending = false;
                    HandleSimpleTransportError(error, "读取江湖恩怨录失败，请稍后重试。");
                    Changed?.Invoke();
                });
        }

        private void ApplyProfile(global::JianghuProfileView profile)
        {
            Profile = profile;
            UpdateServerClock(profile.ServerTimeMs);
        }

        private void ApplyInviteUpdate(global::Match.JianghuDuelInviteView invite, string reasonCode)
        {
            UpdateServerClock(invite.ServerTimeMs);
            if (JianghuModels.IsInviteTerminal(invite.State))
            {
                RemoveInvite(invite.DuelInviteId);
                if (!string.IsNullOrEmpty(reasonCode))
                    SetStatus(JianghuModels.FormatStatusReason(reasonCode), false, 0);
                if (invite.State == global::Match.JianghuDuelInviteState.Matched)
                {
                    RequestProfile(true);
                    if (invite.BattleId != 0) DuelMatched?.Invoke(invite.BattleId);
                }
            }
            else
            {
                UpsertInvite(invite);
                if (!string.IsNullOrEmpty(reasonCode))
                    SetStatus(JianghuModels.FormatStatusReason(reasonCode), false, 0);
            }
        }

        private void UpsertInvite(global::Match.JianghuDuelInviteView invite)
        {
            UpdateServerClock(invite.ServerTimeMs);
            for (int i = 0; i < _activeInvites.Count; i++)
            {
                if (_activeInvites[i].DuelInviteId == invite.DuelInviteId)
                {
                    _activeInvites[i] = invite;
                    return;
                }
            }
            _activeInvites.Insert(0, invite);
        }

        private void RemoveInvite(ulong duelInviteId)
        {
            for (int i = _activeInvites.Count - 1; i >= 0; i--)
            {
                if (_activeInvites[i].DuelInviteId == duelInviteId)
                    _activeInvites.RemoveAt(i);
            }
            if (_dismissedPopupInviteId == duelInviteId)
                _dismissedPopupInviteId = 0;
        }

        private void UpdateServerClock(ulong serverTimeMs)
        {
            if (serverTimeMs == 0) return;
            _anchorServerMs = serverTimeMs;
            _anchorClockSec = _clock();
        }

        private bool PrepareSimpleWrite(Action<bool> completed, bool checkReconnect = false)
        {
            if (_disposed || !IsSupported)
            {
                completed?.Invoke(false);
                return false;
            }
            ObserveSessionBoundary();
            if (WritePending)
            {
                completed?.Invoke(false);
                return false;
            }
            if (checkReconnect && RequiresReconnect)
            {
                RejectLocally(RecoveryMessage, 0);
                completed?.Invoke(false);
                return false;
            }
            if (!Ready)
            {
                RejectLocally("尚未连接到游戏。", 0);
                completed?.Invoke(false);
                return false;
            }
            if (_inBattle)
            {
                RejectLocally("战斗或观战期间无法进行江湖擂台操作。", 0);
                completed?.Invoke(false);
                return false;
            }
            if (_clock() < _nextWriteAt)
            {
                RejectLocally(TooFastMessage, 0);
                completed?.Invoke(false);
                return false;
            }
            return true;
        }

        private void HandleTipFailure(uint tipId)
        {
            if (tipId == (uint)global::common_error.KRateLimitExceeded)
            {
                float backoff = _clock() + RateLimitBackoffSeconds;
                if (backoff > _nextWriteAt) _nextWriteAt = backoff;
            }
            SetStatus(JianghuModels.DescribeTip(tipId), true, tipId);
        }

        private void HandleSimpleTransportError(string error, string fallbackMessage)
        {
            uint tipId = ParseEnvelopeTip(error);
            if (string.Equals(error, RpcGuard.CircuitOpenError, StringComparison.Ordinal))
                SetStatus("江湖擂台服务暂时繁忙，请稍后重试。", true, 0);
            else if (tipId != 0)
                HandleTipFailure(tipId);
            else
                SetStatus(fallbackMessage, true, 0);
        }

        private void RejectLocally(string message, uint tipId)
        {
            SetStatus(message, true, tipId);
            Changed?.Invoke();
        }

        private void ObserveSessionBoundary()
        {
            object currentConn = _connectionIdentity();
            ulong currentPlayer = _net.PlayerId;
            if (ReferenceEquals(_observedConnection, currentConn)
                && (_observedPlayerId == 0 || currentPlayer == 0 || _observedPlayerId == currentPlayer))
            {
                if (currentPlayer != 0) _observedPlayerId = currentPlayer;
                return;
            }
            _observedConnection = currentConn;
            _observedPlayerId = currentPlayer;
            ResetSession();
            Changed?.Invoke();
        }

        private void ResetSession()
        {
            ++_generation;
            Profile = null;
            _grudges.Clear();
            _activeInvites.Clear();
            GuildWall = null;
            GuildWallHint = string.Empty;
            _guildWallLoadedOnce = false;
            _dismissedPopupInviteId = 0;
            _anchorServerMs = 0;
            _anchorClockSec = 0f;
            ProfilePending = GrudgesPending = GuildWallPending = WritePending = false;
            _profileQueued = _grudgesQueued = false;
            RequiresReconnect = false;
            _nextProfileAt = _nextGrudgesAt = _nextGuildWallAt = _nextWriteAt = float.NegativeInfinity;
            SetStatus(IsSupported ? string.Empty : UnsupportedMessage, false, 0);
        }

        private bool IsCurrent(int generation) => !_disposed && generation == _generation;

        private void SetStatus(string message, bool isError, uint tipId)
        {
            Status = message ?? string.Empty;
            StatusIsError = isError;
            LastErrorTipId = isError ? tipId : 0;
        }

        private static uint ParseEnvelopeTip(string error)
        {
            if (error == null || !error.StartsWith(ServerTipPrefix, StringComparison.Ordinal)) return 0;
            return uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out uint tipId) ? tipId : 0;
        }
    }
}
