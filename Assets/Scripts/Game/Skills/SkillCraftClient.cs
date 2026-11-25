using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Skills
{
    /// <summary>
    /// 率土式分卷宗技能制书、未学书架研习/拆页与主城名匠代打摊位的客户端业务适配器。
    /// 严格遵守服务端权威原则：材料余额、卷宗池、书架、熟练度与摊位列表全部以服务端快照为准；
    /// 写请求携带幂等键与 expected_revision，弱网结果未知时保留原请求字节重放，不换键重扣。
    /// </summary>
    public sealed class SkillCraftClient : IDisposable
    {
        public static SkillCraftClient Instance { get; private set; }

        public const string NotInGameMessage = "尚未进入游戏，典籍制书信息未同步";
        public const string LoadingMessage = "正在同步典籍制书信息…";
        public const string DisconnectedMessage = "连接已断开，典籍制书信息已过期，重连后请刷新";
        public const string PendingWriteText = "上次制书/书架操作结果尚未确认，请先重试原请求";
        public const string BattleBlockedText = "战斗中或观战中不可进行制书、研习、拆页或修改摊位。";
        private const int MaxAutoRecoveryAttempts = 3;
        private const long MinWriteIntervalMs = 800;

        private enum PendingKind { None = 0, Craft = 1, Learn = 2, Dismantle = 3 }

        private sealed class PendingRequest
        {
            public PendingKind Kind;
            public CraftSkillBookRequest Craft;
            public LearnSkillBookRequest Learn;
            public DismantleSkillBookRequest Dismantle;
            public string EncodedRecord;
            public ulong ClientRequestId => Kind switch
            {
                PendingKind.Craft => Craft?.ClientRequestId ?? 0,
                PendingKind.Learn => Learn?.ClientRequestId ?? 0,
                PendingKind.Dismantle => Dismantle?.ClientRequestId ?? 0,
                _ => 0,
            };
            public string Label => Kind switch
            {
                PendingKind.Craft => "拓印制书",
                PendingKind.Learn => "研习技能书",
                PendingKind.Dismantle => "拆解技能书",
                _ => "制书操作",
            };
        }

        private readonly struct RequestStamp
        {
            public readonly long Epoch, Id;
            public readonly ulong Player;
            public readonly object Connection;
            public RequestStamp(long epoch, long id, ulong player, object connection)
            { Epoch = epoch; Id = id; Player = player; Connection = connection; }
        }

        private static readonly System.Diagnostics.Stopwatch MonotonicClock = System.Diagnostics.Stopwatch.StartNew();

        private readonly IBattleTransport _net;
        private readonly IRpcChannel _rpc;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<bool> _writesBlocked;
        private Func<long> _nowMs = () => MonotonicClock.ElapsedMilliseconds;
        private object _connection;
        private ulong _player;
        private long _epoch, _serial, _activeRead, _activeWrite, _activeStallRead;
        private bool _disposed;

        private SkillCraftingStateSnapshot _state;
        private readonly List<CraftProxyStallInfo> _proxyStalls = new();
        private PendingRequest _pending;
        private ulong _recoveryPlayer;
        private bool _recoveryBlocked;
        private int _autoRecoveryAttempts;
        private long _nextAutoRecoveryAtMs;
        private long? _lastWriteSendMs;

        public DurableRequestFileStore RecoveryStore { get; set; }
        public Func<long> NowMs
        {
            get => _nowMs;
            set => _nowMs = value ?? (() => MonotonicClock.ElapsedMilliseconds);
        }

        public SkillCraftingStateSnapshot State => _state?.Clone();
        public IReadOnlyList<CraftProxyStallInfo> ProxyStalls
        {
            get
            {
                var copy = new List<CraftProxyStallInfo>(_proxyStalls.Count);
                foreach (var stall in _proxyStalls) copy.Add(stall.Clone());
                return copy;
            }
        }

        public bool IsConnected => !_disposed && _net.IsReady && _net.PlayerId != 0;
        public bool IsLoading => _activeRead != 0;
        public bool IsBusy => _activeWrite != 0;
        public bool ProxyStallsLoading => _activeStallRead != 0;
        public bool HasPendingWrite => _pending != null || _recoveryBlocked;
        public string PendingWriteLabel => _pending?.Label ?? string.Empty;
        public string Message { get; private set; } = NotInGameMessage;
        public string Error { get; private set; } = string.Empty;

        public event Action Changed;
        public event Action<CraftSkillBookResponse> BookCrafted;
        public event Action<LearnSkillBookResponse> BookLearned;
        public event Action<DismantleSkillBookResponse> BookDismantled;

        public SkillCraftClient(IBattleTransport transport, IRpcChannel rpc,
            Func<object> connectionIdentity = null, Func<bool> writesBlocked = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _connectionIdentity = connectionIdentity ?? (() => transport);
            _writesBlocked = writesBlocked;
            _connection = _connectionIdentity();
            _player = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        public static SkillCraftClient Attach(IBattleTransport transport, IRpcChannel rpc,
            Func<object> connectionIdentity = null, Func<bool> writesBlocked = null)
        {
            Instance?.Dispose();
            var client = new SkillCraftClient(transport, rpc, connectionIdentity, writesBlocked);
            Instance = client;
            return client;
        }

        public void RefreshState()
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            RestorePending(_net.PlayerId);
            if (_activeRead != 0 || _activeWrite != 0) return;

            var stamp = Capture();
            _activeRead = stamp.Id;
            Error = string.Empty;
            Message = LoadingMessage;
            Changed?.Invoke();

            SceneSkillClientPlayerRpc.GetSkillCraftingState(_rpc, new GetSkillCraftingStateRequest(),
                (status, response) =>
                {
                    if (!CurrentRead(stamp)) return;
                    _activeRead = 0;
                    if (!status.IsOk)
                    {
                        Fail("同步典籍制书信息失败：" + DescribeFailure(status));
                        return;
                    }
                    if (response == null)
                    {
                        Fail("服务器未返回典籍制书状态，请刷新");
                        return;
                    }
                    if (HasTip(response.ErrorMessage))
                    {
                        Fail(SkillBookMapper.DescribeTip("同步典籍制书信息失败", response.ErrorMessage));
                        return;
                    }
                    if (response.State == null)
                    {
                        Fail("服务器未返回有效的典籍制书快照，请刷新");
                        return;
                    }
                    _state = response.State.Clone();
                    Error = string.Empty;
                    Message = HasPendingWrite ? PendingWriteText : string.Empty;
                    Changed?.Invoke();
                });
        }

        public void RefreshProxyStalls()
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            if (_activeStallRead != 0) return;

            var stamp = Capture();
            _activeStallRead = stamp.Id;
            Changed?.Invoke();

            SceneSkillClientPlayerRpc.ListCraftProxyStalls(_rpc, new ListCraftProxyStallsRequest(),
                (status, response) =>
                {
                    if (!CurrentStallRead(stamp)) return;
                    _activeStallRead = 0;
                    if (!status.IsOk)
                    {
                        Fail("读取名匠代打摊位失败：" + DescribeFailure(status));
                        return;
                    }
                    if (response == null)
                    {
                        Fail("服务器未返回名匠摊位列表，请刷新");
                        return;
                    }
                    if (HasTip(response.ErrorMessage))
                    {
                        Fail(SkillBookMapper.DescribeTip("读取名匠代打摊位失败", response.ErrorMessage));
                        return;
                    }
                    _proxyStalls.Clear();
                    foreach (var stall in response.Stalls)
                        if (stall != null && stall.MasterPlayerId != 0) _proxyStalls.Add(stall.Clone());
                    Error = string.Empty;
                    Changed?.Invoke();
                });
        }

        public void CraftRegularRandom(uint scrollId, ulong proxyMasterPlayerId = 0)
            => SubmitCraft(CraftSkillBookRequest.Types.CraftMode.RandomRegular, scrollId, 0, proxyMasterPlayerId);

        public void CraftRegularTargeted(uint scrollId, uint targetSkillTableId, ulong proxyMasterPlayerId = 0)
            => SubmitCraft(CraftSkillBookRequest.Types.CraftMode.TargetedRegular, scrollId, targetSkillTableId, proxyMasterPlayerId);

        public void CraftRareTargeted(uint scrollId, uint targetSkillTableId, ulong proxyMasterPlayerId = 0)
            => SubmitCraft(CraftSkillBookRequest.Types.CraftMode.TargetedRare, scrollId, targetSkillTableId, proxyMasterPlayerId);

        public void LearnBook(ulong bookId, ulong petId = 0)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (bookId == 0) { Fail("请选择有效的未学技能书"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new LearnSkillBookRequest
            {
                BookId = bookId,
                PetId = petId,
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.Learn, Learn = request });
        }

        public void DismantleBook(ulong bookId)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (bookId == 0) { Fail("请选择有效的常规技能书"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new DismantleSkillBookRequest
            {
                BookId = bookId,
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.Dismantle, Dismantle = request });
        }

        public void SetProxyStall(bool open, ulong forgeFeeGold, ulong scrollFeeGold)
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            if (_writesBlocked?.Invoke() == true) { Notice(BattleBlockedText); return; }
            if (_activeWrite != 0 || _activeRead != 0) return;

            var stamp = Capture();
            _activeWrite = stamp.Id;
            Error = string.Empty;
            Message = open ? "正在更新名匠代打摊位…" : "正在关闭名匠代打摊位…";
            Changed?.Invoke();

            var request = new SetCraftProxyStallRequest
            {
                Open = open,
                ProxyForgeFeeGold = open ? forgeFeeGold : 0,
                ProxyScrollFeeGold = open ? scrollFeeGold : 0,
            };
            SceneSkillClientPlayerRpc.SetCraftProxyStall(_rpc, request,
                (status, response) =>
                {
                    if (!CurrentWrite(stamp)) return;
                    _activeWrite = 0;
                    if (!status.IsOk)
                    {
                        Fail("更新名匠摊位失败：" + DescribeFailure(status));
                        return;
                    }
                    if (response == null)
                    {
                        Fail("服务器未返回摊位设置结果，请刷新");
                        return;
                    }
                    if (response.State != null) _state = response.State.Clone();
                    if (HasTip(response.ErrorMessage))
                    {
                        Fail(SkillBookMapper.DescribeTip("设置代打摊位失败", response.ErrorMessage));
                        return;
                    }
                    Error = string.Empty;
                    Message = open ? "名匠代打摊位已开启" : "名匠代打摊位已关闭";
                    Changed?.Invoke();
                });
        }

        public void RetryPendingWrite()
        {
            if (_disposed) return;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return; }
            RestorePending(_net.PlayerId);
            if (_pending == null || _recoveryBlocked || _activeWrite != 0) return;
            if (_writesBlocked?.Invoke() == true) { Notice(BattleBlockedText); return; }
            if (!CanSendWrite()) { Notice("操作太快，请稍候重试"); return; }
            SendPending(_net.PlayerId);
        }

        public void TickRecovery()
        {
            if (_disposed || !IsConnected) return;
            ObserveSession();
            RestorePending(_net.PlayerId);
            if (_pending == null || _recoveryBlocked || _activeWrite != 0) return;
            if (_autoRecoveryAttempts >= MaxAutoRecoveryAttempts) return;
            if (_writesBlocked?.Invoke() == true) return;
            if (_nowMs() < _nextAutoRecoveryAtMs || !CanSendWrite()) return;
            ++_autoRecoveryAttempts;
            SendPending(_net.PlayerId);
        }

        private void SubmitCraft(CraftSkillBookRequest.Types.CraftMode mode, uint scrollId,
            uint targetSkillTableId, ulong proxyMasterPlayerId)
        {
            if (!PrepareWrite(out ulong playerId)) return;
            if (scrollId == 0) { Fail("请选择有效的典籍卷宗"); return; }
            if (mode == CraftSkillBookRequest.Types.CraftMode.RandomRegular && targetSkillTableId != 0)
            { Fail("随机拓印不需要指定目标技能"); return; }
            if (mode != CraftSkillBookRequest.Types.CraftMode.RandomRegular && targetSkillTableId == 0)
            { Fail("请先选择要定向拓印的目标技能"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { Fail(ClientRequestKeys.UnavailableText); return; }

            var request = new CraftSkillBookRequest
            {
                Mode = mode,
                ScrollId = scrollId,
                TargetSkillTableId = targetSkillTableId,
                ExpectedRevision = _state.ScrollCraftRevision,
                ClientRequestId = key,
                ProxyMasterPlayerId = proxyMasterPlayerId,
            };
            StartIdempotentWrite(playerId, new PendingRequest { Kind = PendingKind.Craft, Craft = request });
        }

        private bool PrepareWrite(out ulong playerId)
        {
            playerId = 0;
            if (_disposed) return false;
            ObserveSession();
            if (!IsConnected) { Fail(NotInGameMessage); return false; }
            playerId = _net.PlayerId;
            RestorePending(playerId);
            if (HasPendingWrite) { Fail(PendingWriteText); return false; }
            if (_writesBlocked?.Invoke() == true) { Notice(BattleBlockedText); return false; }
            if (_activeWrite != 0 || _activeRead != 0) return false;
            if (_state == null) { Fail("请先同步典籍制书状态后再操作"); return false; }
            if (!CanSendWrite()) { Notice("操作太快，请稍候重试"); return false; }
            return true;
        }

        private void StartIdempotentWrite(ulong playerId, PendingRequest pending)
        {
            string encoded = EncodePending(pending);
            if (RecoveryStore != null)
            {
                try { RecoveryStore.Save(playerId, encoded); }
                catch (Exception)
                {
                    _recoveryPlayer = 0;
                    RestorePending(playerId);
                    Fail("无法保存本机制书恢复记录，本次尚未提交");
                    return;
                }
            }
            pending.EncodedRecord = encoded;
            _pending = pending;
            _autoRecoveryAttempts = 0;
            _nextAutoRecoveryAtMs = 0;
            SendPending(playerId);
        }

        private void SendPending(ulong playerId)
        {
            var pending = _pending;
            if (pending == null) return;
            var stamp = Capture();
            _activeWrite = stamp.Id;
            _activeRead = 0;
            _lastWriteSendMs = _nowMs();
            Error = string.Empty;
            Message = $"正在处理{pending.Label}…";
            Changed?.Invoke();

            switch (pending.Kind)
            {
                case PendingKind.Craft:
                    SceneSkillClientPlayerRpc.CraftSkillBook(_rpc, pending.Craft.Clone(),
                        (status, response) => HandleCraftReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.Learn:
                    SceneSkillClientPlayerRpc.LearnSkillBook(_rpc, pending.Learn.Clone(),
                        (status, response) => HandleLearnReply(stamp, playerId, pending, status, response));
                    break;
                case PendingKind.Dismantle:
                    SceneSkillClientPlayerRpc.DismantleSkillBook(_rpc, pending.Dismantle.Clone(),
                        (status, response) => HandleDismantleReply(stamp, playerId, pending, status, response));
                    break;
            }
        }

        private void HandleCraftReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, CraftSkillBookResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip("制书未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            string bonus = response.BonusFragmentsGranted > 0
                ? $"，巧匠/宗师额外返还 {response.BonusFragmentsGranted} 张悟道残页" : string.Empty;
            Message = response.Replayed ? $"已恢复上次制书结果{bonus}" : $"技能书制成成功{bonus}";
            Changed?.Invoke();
            if (Current(stamp)) BookCrafted?.Invoke(response.Clone());
        }

        private void HandleLearnReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, LearnSkillBookResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip("研习未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            Message = response.Replayed ? "已恢复上次研习结果，技能已永久解锁" : "技能书研习成功，已永久习得";
            Changed?.Invoke();
            if (Current(stamp)) BookLearned?.Invoke(response.Clone());
        }

        private void HandleDismantleReply(RequestStamp stamp, ulong playerId, PendingRequest pending,
            RpcStatus status, DismantleSkillBookResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            if (!status.IsOk) { HandleTransportFailure(playerId, pending, status); return; }
            if (response == null || response.ClientRequestId != pending.ClientRequestId)
            { DeferPending($"{pending.Label}回执身份不符，原请求已保留"); return; }
            if (response.State != null) _state = response.State.Clone();
            if (!ClearPending(playerId, pending.EncodedRecord)) return;
            if (HasTip(response.ErrorMessage))
            {
                Notice(SkillBookMapper.DescribeTip("拆页未成功", response.ErrorMessage));
                return;
            }
            Error = string.Empty;
            Message = $"拆页完成，返还 {response.RefundedFragments} 张通用悟道残页";
            Changed?.Invoke();
            if (Current(stamp)) BookDismantled?.Invoke(response.Clone());
        }

        private void HandleTransportFailure(ulong playerId, PendingRequest pending, RpcStatus status)
        {
            // 明确未发到服务端（断线/未就绪/网关明确信封拒绝）才安全清除原请求；超时与未知故障必须原样保留幂等键。
            if (status.Code == RpcStatusCode.NotSent || status.Code == RpcStatusCode.ServerTip)
            {
                if (!ClearPending(playerId, pending.EncodedRecord)) return;
                string prefix = status.Code == RpcStatusCode.NotSent ? $"{pending.Label}未发送：" : $"{pending.Label}被拒绝：";
                Fail(prefix + DescribeFailure(status));
                return;
            }
            DeferPending($"{pending.Label}结果尚未确认（{DescribeFailure(status)}），原请求已保留，请重试原请求");
        }

        private void DeferPending(string error)
        {
            _nextAutoRecoveryAtMs = _nowMs() + 2000 * (1L << Math.Min(_autoRecoveryAttempts, 2));
            Fail(error);
        }

        private bool ClearPending(ulong playerId, string record)
        {
            if (RecoveryStore != null && !string.IsNullOrEmpty(record))
            {
                try { RecoveryStore.Clear(playerId, record); }
                catch (Exception)
                {
                    DeferPending("操作回执已收到，本机恢复记录尚未清理，请重试原请求");
                    return false;
                }
            }
            _pending = null;
            _nextAutoRecoveryAtMs = 0;
            return true;
        }

        private void RestorePending(ulong playerId)
        {
            if (_recoveryPlayer == playerId || RecoveryStore == null || playerId == 0) return;
            _recoveryPlayer = playerId;
            try
            {
                string record = RecoveryStore.Load(playerId);
                if (string.IsNullOrEmpty(record)) return;
                var parsed = DecodePending(record);
                if (parsed == null || parsed.ClientRequestId == 0) throw new InvalidOperationException();
                parsed.EncodedRecord = record;
                _pending = parsed;
                Error = PendingWriteText;
            }
            catch (Exception)
            {
                _recoveryBlocked = true;
                Error = "制书恢复记录损坏或无法读取，请保留记录并联系支持";
            }
        }

        private static string EncodePending(PendingRequest pending) => pending.Kind switch
        {
            PendingKind.Craft => "craft:" + Convert.ToBase64String(pending.Craft.ToByteArray()),
            PendingKind.Learn => "learn:" + Convert.ToBase64String(pending.Learn.ToByteArray()),
            PendingKind.Dismantle => "dismantle:" + Convert.ToBase64String(pending.Dismantle.ToByteArray()),
            _ => string.Empty,
        };

        private static PendingRequest DecodePending(string record)
        {
            int sep = record.IndexOf(':');
            if (sep <= 0 || sep + 1 >= record.Length) return null;
            string kind = record.Substring(0, sep);
            byte[] bytes = Convert.FromBase64String(record.Substring(sep + 1));
            return kind switch
            {
                "craft" => new PendingRequest { Kind = PendingKind.Craft, Craft = CraftSkillBookRequest.Parser.ParseFrom(bytes) },
                "learn" => new PendingRequest { Kind = PendingKind.Learn, Learn = LearnSkillBookRequest.Parser.ParseFrom(bytes) },
                "dismantle" => new PendingRequest { Kind = PendingKind.Dismantle, Dismantle = DismantleSkillBookRequest.Parser.ParseFrom(bytes) },
                _ => null,
            };
        }

        private bool CanSendWrite() => !_lastWriteSendMs.HasValue || _nowMs() < _lastWriteSendMs.Value
            || _nowMs() - _lastWriteSendMs.Value >= MinWriteIntervalMs;

        private void Notice(string text)
        {
            Error = string.Empty;
            Message = text ?? string.Empty;
            Changed?.Invoke();
        }

        private void Fail(string error)
        {
            Error = string.IsNullOrWhiteSpace(error) ? "典籍制书请求失败，请重试" : error;
            Changed?.Invoke();
        }

        private void ObserveSession()
        {
            var connection = _connectionIdentity();
            ulong player = _net.PlayerId;
            if (ReferenceEquals(connection, _connection) && player == _player) return;
            _connection = connection;
            _player = player;
            ResetSession(NotInGameMessage);
        }

        private void HandleDisconnected()
        {
            if (_disposed) return;
            _connection = _connectionIdentity();
            _player = _net.PlayerId;
            ResetSession(DisconnectedMessage);
        }

        private void ResetSession(string message)
        {
            ++_epoch;
            _activeRead = _activeWrite = _activeStallRead = 0;
            _state = null;
            _proxyStalls.Clear();
            _pending = null;
            _recoveryPlayer = 0;
            _recoveryBlocked = false;
            _autoRecoveryAttempts = 0;
            _nextAutoRecoveryAtMs = 0;
            Error = string.Empty;
            Message = message;
            Changed?.Invoke();
        }

        private RequestStamp Capture() => new(_epoch, ++_serial, _net.PlayerId, _connectionIdentity());
        private bool Current(RequestStamp stamp) => !_disposed && stamp.Epoch == _epoch && IsConnected
            && stamp.Player == _net.PlayerId && ReferenceEquals(stamp.Connection, _connectionIdentity());
        private bool CurrentRead(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeRead;
        private bool CurrentWrite(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeWrite;
        private bool CurrentStallRead(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeStallRead;

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeFailure(RpcStatus status) => status.Code switch
        {
            RpcStatusCode.NotSent => "连接尚未就绪",
            RpcStatusCode.Timeout => "请求超时",
            RpcStatusCode.Disconnected => "连接已断开",
            RpcStatusCode.ParseError => "服务器响应无法读取",
            RpcStatusCode.ServerTip => SkillBookMapper.DescribeTip(status.TipId),
            _ => string.IsNullOrEmpty(status.Detail) ? "网络异常" : status.Detail,
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _net.Disconnected -= HandleDisconnected;
            ++_epoch;
            _activeRead = _activeWrite = _activeStallRead = 0;
            _state = null;
            _proxyStalls.Clear();
            _pending = null;
            if (ReferenceEquals(Instance, this)) Instance = null;
            Changed = null;
            BookCrafted = null;
            BookLearned = null;
            BookDismantled = null;
        }
    }
}
