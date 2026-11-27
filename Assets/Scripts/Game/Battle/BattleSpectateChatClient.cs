using System;
using System.Collections.Generic;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.Net.Generated.Rpc;

namespace MmorpgClient.Game.Battle
{
    /// <summary>观战公开交流室单条不可变消息快照（纯 C#，不依赖 UnityEngine，供单测与 UGUI 共用）。</summary>
    public sealed class BattleSpectateChatEntry
    {
        public ulong Seq { get; }
        public ulong SenderPlayerId { get; }
        public string SenderName { get; }
        public BattleSpectateChatRole Role { get; }
        public string Content { get; }
        public ulong CreatedMs { get; }

        public BattleSpectateChatEntry(
            ulong seq,
            ulong senderPlayerId,
            string senderName,
            BattleSpectateChatRole role,
            string content,
            ulong createdMs)
        {
            Seq = seq;
            SenderPlayerId = senderPlayerId;
            SenderName = string.IsNullOrWhiteSpace(senderName)
                ? (role == BattleSpectateChatRole.Participant ? "参战道友" : "观战道友")
                : senderName.Trim();
            Role = role;
            Content = content ?? string.Empty;
            CreatedMs = createdMs;
        }

        public string RoleTag => Role == BattleSpectateChatRole.Participant ? "参战" : "观战";

        public static BattleSpectateChatEntry FromProto(BattleSpectateChatMessage proto)
        {
            if (proto == null || proto.Seq == 0UL) return null;
            return new BattleSpectateChatEntry(
                proto.Seq,
                proto.SenderPlayerId,
                proto.SenderName,
                proto.Role,
                proto.Content,
                proto.CreatedMs);
        }
    }

    /// <summary>
    /// 《五行奇谈》局外人观战交流与个人禁交开关客户端状态机（纯 C#，零线程、WebGL2/IL2CPP 安全）：
    /// - 个人设置（589/590）：读写「允许局外人观战交流」开关（关闭仅禁止观战期间交流，绝不禁止观战；下次进战斗生效）；
    /// - 战斗观战交流室（587/588）：仅当 BattleStateS2C.SpectateChatEnabled == true 且非离线回放时拉取与发言；
    /// - 所有发言经服务端 ContentGuard 审核与限流，客户端以权威响应为准，绝不本地伪造消息。
    /// </summary>
    public sealed class BattleSpectateChatClient : IDisposable
    {
        public static BattleSpectateChatClient Instance { get; private set; }

        public const int MaxRetainedMessages = 60;
        public const int DefaultPullLimit = 30;
        public const int MaxContentBytes = 180;
        public const double PollIntervalSeconds = 2.0;

        private const string NotInGameMessage = "当前未进入游戏，无法同步观战交流状态。";

        private readonly IBattleTransport _net;
        private readonly Func<string> _connectionIdentity;
        private readonly Func<double> _clock;
        private readonly RpcGuard _guard;
        private readonly List<BattleSpectateChatEntry> _messages = new List<BattleSpectateChatEntry>(MaxRetainedMessages);
        private readonly HashSet<ulong> _seenSeqs = new HashSet<ulong>();

        private string _lastConnectionIdentity;
        private int _generation;
        private bool _disposed;

        private bool _settingLoading;
        private bool _settingSaving;
        private bool _sending;
        private bool _pulling;
        private bool _pollingActive;
        private double _nextPollAt;
        private double _cooldownUntilLocal;

        // ── 个人开关状态（Scene 589/590）────────────────────

        public eSpectateChatPreference Preference { get; private set; } = eSpectateChatPreference.SpectateChatPrefUnspecified;
        /// <summary>服务端解析后的生效值：true=允许局外人观战交流，false=禁止观战期间交流（仍可观战）。</summary>
        public bool EffectiveAllowSpectateChat { get; private set; } = true;
        public bool SettingLoaded { get; private set; }
        public bool SettingSaving => _settingSaving;
        public string SettingStatus { get; private set; } = string.Empty;

        // ── 当前战斗观战交流室状态（Match 587/588）──────────

        public ulong ActiveBattleId { get; private set; }
        public bool ChatEnabled { get; private set; }
        public bool BattleEnded { get; private set; }
        public ulong LatestSeq { get; private set; }
        public bool Sending => _sending;
        public string LastError { get; private set; } = string.Empty;
        public uint LastErrorTipId { get; private set; }
        public IReadOnlyList<BattleSpectateChatEntry> Messages => _messages;

        public ulong PlayerId => _net?.PlayerId ?? 0UL;

        public event Action SettingChanged;
        public event Action ChatChanged;

        public BattleSpectateChatClient(
            IBattleTransport transport,
            Func<string> connectionIdentity = null,
            Func<double> clock = null,
            RpcGuard guard = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? (() => string.Empty);
            _clock = clock ?? (() => 0.0);
            _guard = guard;
            _lastConnectionIdentity = _connectionIdentity() ?? string.Empty;
            _net.Disconnected += HandleDisconnected;
        }

        public static BattleSpectateChatClient Attach(
            IBattleTransport transport,
            Func<string> connectionIdentity = null,
            Func<double> clock = null,
            RpcGuard guard = null)
        {
            var client = new BattleSpectateChatClient(transport, connectionIdentity, clock, guard);
            Instance = client;
            return client;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _net.Disconnected -= HandleDisconnected;
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        public double CooldownRemainingSeconds()
        {
            double remain = _cooldownUntilLocal - _clock();
            return remain > 0.0 ? remain : 0.0;
        }

        public void Tick(double nowSeconds)
        {
            if (_disposed) return;
            ObserveConnection();
            if (!_pollingActive || ActiveBattleId == 0UL || !ChatEnabled || BattleEnded) return;
            if (!_net.IsReady || PlayerId == 0UL || _pulling) return;
            if (nowSeconds >= _nextPollAt)
            {
                _nextPollAt = nowSeconds + PollIntervalSeconds;
                Pull(ActiveBattleId);
            }
        }

        /// <summary>切换当前战斗/观战交流上下文；切场或关闭时清空旧场消息与轮询态。</summary>
        public void SyncBattleContext(ulong battleId, bool chatEnabled, bool pollingActive)
        {
            if (_disposed) return;
            ObserveConnection();

            bool battleChanged = ActiveBattleId != battleId;
            bool enabledChanged = ChatEnabled != chatEnabled;
            _pollingActive = pollingActive && battleId != 0UL && chatEnabled;

            if (battleChanged)
            {
                ActiveBattleId = battleId;
                ChatEnabled = chatEnabled;
                BattleEnded = false;
                LatestSeq = 0UL;
                _cooldownUntilLocal = 0.0;
                _sending = false;
                _pulling = false;
                LastError = string.Empty;
                LastErrorTipId = 0u;
                _messages.Clear();
                _seenSeqs.Clear();
                _nextPollAt = 0.0;
                ChatChanged?.Invoke();
                if (_pollingActive && _net.IsReady && PlayerId != 0UL)
                {
                    _nextPollAt = _clock() + PollIntervalSeconds;
                    Pull(battleId);
                }
                return;
            }

            if (enabledChanged)
            {
                ChatEnabled = chatEnabled;
                if (!chatEnabled)
                {
                    _pollingActive = false;
                    LastError = string.Empty;
                    LastErrorTipId = 0u;
                }
                ChatChanged?.Invoke();
            }
            if (_pollingActive && !_pulling && _nextPollAt <= 0.0 && _net.IsReady && PlayerId != 0UL)
            {
                _nextPollAt = _clock() + PollIntervalSeconds;
                Pull(battleId);
            }
        }

        public void ClearBattleContext() => SyncBattleContext(0UL, false, false);

        public void SetPollingActive(bool active)
        {
            if (_disposed) return;
            _pollingActive = active && ActiveBattleId != 0UL && ChatEnabled && !BattleEnded;
            if (_pollingActive && !_pulling && _net.IsReady && PlayerId != 0UL && _clock() >= _nextPollAt)
            {
                _nextPollAt = _clock() + PollIntervalSeconds;
                Pull(ActiveBattleId);
            }
        }

        // ── 个人设置（589/590）──────────────────────────────

        public bool RefreshSetting()
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0UL) return false;
            if (_settingLoading) return false;
            if (!TryConsumeBudget(SceneSceneClientPlayerRpc.GetSpectateChatSettingMessageId)) return false;

            int gen = _generation;
            ulong player = PlayerId;
            _settingLoading = true;

            _net.Call(SceneSceneClientPlayerRpc.GetSpectateChatSettingMessageId,
                new GetSpectateChatSettingRequest(),
                GetSpectateChatSettingResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _settingLoading = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        SettingStatus = DescribeTip(resp.ErrorMessage);
                        SettingChanged?.Invoke();
                        return;
                    }
                    if (resp != null)
                    {
                        Preference = resp.Preference;
                        EffectiveAllowSpectateChat = resp.EffectiveAllow;
                        SettingLoaded = true;
                        SettingStatus = string.Empty;
                        SettingChanged?.Invoke();
                    }
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _settingLoading = false;
                    SettingStatus = DescribeTransport("读取观战交流设置失败", err);
                    SettingChanged?.Invoke();
                });
            return true;
        }

        public bool SetAllowSpectateChat(bool allow)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0UL)
            {
                SettingStatus = NotInGameMessage;
                SettingChanged?.Invoke();
                return false;
            }
            if (_settingSaving) return false;
            if (!TryConsumeBudget(SceneSceneClientPlayerRpc.SetSpectateChatSettingMessageId)) return false;

            int gen = _generation;
            ulong player = PlayerId;
            _settingSaving = true;
            SettingStatus = "设置保存中…";
            SettingChanged?.Invoke();

            var pref = allow
                ? eSpectateChatPreference.SpectateChatPrefAllow
                : eSpectateChatPreference.SpectateChatPrefForbid;

            _net.Call(SceneSceneClientPlayerRpc.SetSpectateChatSettingMessageId,
                new SetSpectateChatSettingRequest { Preference = pref },
                SetSpectateChatSettingResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _settingSaving = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        SettingStatus = DescribeTip(resp.ErrorMessage);
                        SettingChanged?.Invoke();
                        return;
                    }
                    if (resp != null)
                    {
                        Preference = resp.Preference;
                        EffectiveAllowSpectateChat = resp.EffectiveAllow;
                        SettingLoaded = true;
                        SettingStatus = EffectiveAllowSpectateChat
                            ? "已开启：局外人可在观战时交流（下次进战斗生效）"
                            : "已关闭：仍可正常观战，但禁止观战期间局外人交流（下次进战斗生效）";
                        SettingChanged?.Invoke();
                    }
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _settingSaving = false;
                    SettingStatus = DescribeTransport("保存观战交流设置失败", err);
                    SettingChanged?.Invoke();
                });
            return true;
        }

        // ── 战斗观战交流室（587/588）────────────────────────

        public bool Send(ulong battleId, string rawContent)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0UL)
            {
                FailChatLocal(NotInGameMessage);
                return false;
            }
            if (battleId == 0UL)
            {
                FailChatLocal("当前不在实时战斗或观战中。");
                return false;
            }
            if (battleId == ActiveBattleId && !ChatEnabled)
            {
                FailChatLocal("本场战斗已关闭观战交流（可正常观战）。");
                return false;
            }
            if (battleId == ActiveBattleId && BattleEnded)
            {
                FailChatLocal("本场战斗已结束，观战交流已关闭。");
                return false;
            }
            string trimmed = (rawContent ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                FailChatLocal("请输入交流内容。");
                return false;
            }
            if (System.Text.Encoding.UTF8.GetByteCount(trimmed) > MaxContentBytes)
            {
                FailChatLocal("发言内容过长，请控制在 60 字以内。");
                return false;
            }
            if (_sending) return false;
            if (!TryConsumeBudget(MatchServiceRpc.SendBattleSpectateChatMessageId)) return false;

            int gen = _generation;
            ulong player = PlayerId;
            _sending = true;
            LastError = string.Empty;
            LastErrorTipId = 0u;
            ChatChanged?.Invoke();

            _net.Call(MatchServiceRpc.SendBattleSpectateChatMessageId,
                new SendBattleSpectateChatRequest { BattleId = battleId, Content = trimmed },
                SendBattleSpectateChatResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _sending = false;
                    if (resp != null && (ActiveBattleId == 0UL || battleId == ActiveBattleId))
                    {
                        if (ActiveBattleId == 0UL) ActiveBattleId = battleId;
                        UpdateCooldown(resp.CooldownUntilMs, resp.ServerTimeMs);
                        if (resp.BattleEnded)
                        {
                            BattleEnded = true;
                            _pollingActive = false;
                        }
                        if (!resp.Enabled && HasTip(resp.ErrorMessage) &&
                            resp.ErrorMessage.Id == (uint)match_error.KMatchSpectateChatDisabled)
                        {
                            ChatEnabled = false;
                            _pollingActive = false;
                        }
                        if (resp.Message != null)
                        {
                            AppendMessage(BattleSpectateChatEntry.FromProto(resp.Message));
                        }
                    }
                    if (HasTip(resp?.ErrorMessage))
                    {
                        LastErrorTipId = resp.ErrorMessage.Id;
                        LastError = DescribeTip(resp.ErrorMessage);
                        ChatChanged?.Invoke();
                        return;
                    }
                    LastError = string.Empty;
                    LastErrorTipId = 0u;
                    ChatChanged?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _sending = false;
                    LastErrorTipId = 0u;
                    LastError = DescribeTransport("发送观战交流失败", err);
                    ChatChanged?.Invoke();
                });
            return true;
        }

        public bool Pull(ulong battleId, ulong afterSeq = 0UL)
        {
            if (_disposed || battleId == 0UL) return false;
            ObserveConnection();
            if (!_net.IsReady || PlayerId == 0UL || _pulling) return false;
            if (!TryConsumeBudget(MatchServiceRpc.PullBattleSpectateChatMessageId)) return false;

            int gen = _generation;
            ulong player = PlayerId;
            _pulling = true;
            ulong effAfter = afterSeq != 0UL ? afterSeq : (battleId == ActiveBattleId ? LatestSeq : 0UL);

            _net.Call(MatchServiceRpc.PullBattleSpectateChatMessageId,
                new PullBattleSpectateChatRequest
                {
                    BattleId = battleId,
                    AfterSeq = effAfter,
                    Limit = DefaultPullLimit,
                },
                PullBattleSpectateChatResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _pulling = false;
                    if (battleId != ActiveBattleId) return;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        if (resp.ErrorMessage.Id == (uint)match_error.KMatchBattleNotWatchable)
                        {
                            BattleEnded = true;
                            _pollingActive = false;
                            ChatChanged?.Invoke();
                        }
                        return;
                    }
                    if (resp == null) return;
                    ChatEnabled = resp.Enabled;
                    if (resp.BattleEnded)
                    {
                        BattleEnded = true;
                        _pollingActive = false;
                    }
                    if (!resp.Enabled)
                    {
                        _pollingActive = false;
                    }
                    UpdateCooldown(resp.CooldownUntilMs, resp.ServerTimeMs);
                    if (resp.LatestSeq > LatestSeq) LatestSeq = resp.LatestSeq;
                    if (resp.Messages != null)
                    {
                        for (int i = 0; i < resp.Messages.Count; i++)
                        {
                            AppendMessage(BattleSpectateChatEntry.FromProto(resp.Messages[i]));
                        }
                    }
                    ChatChanged?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(gen, player)) return;
                    _pulling = false;
                });
            return true;
        }

        // ── 内部工具 ────────────────────────────────────────

        private void AppendMessage(BattleSpectateChatEntry entry)
        {
            if (entry == null || !_seenSeqs.Add(entry.Seq)) return;
            _messages.Add(entry);
            if (entry.Seq > LatestSeq) LatestSeq = entry.Seq;
            if (_messages.Count > MaxRetainedMessages)
            {
                int remove = _messages.Count - MaxRetainedMessages;
                for (int i = 0; i < remove; i++) _seenSeqs.Remove(_messages[i].Seq);
                _messages.RemoveRange(0, remove);
            }
        }

        private void UpdateCooldown(ulong cooldownUntilMs, ulong serverTimeMs)
        {
            if (cooldownUntilMs == 0UL || serverTimeMs == 0UL || cooldownUntilMs <= serverTimeMs) return;
            double deltaSec = (cooldownUntilMs - serverTimeMs) / 1000.0;
            double target = _clock() + deltaSec;
            if (target > _cooldownUntilLocal) _cooldownUntilLocal = target;
        }

        private void FailChatLocal(string message)
        {
            LastError = message ?? string.Empty;
            LastErrorTipId = 0u;
            ChatChanged?.Invoke();
        }

        private bool TryConsumeBudget(uint messageId)
        {
            if (_guard == null) return true;
            return _guard.TryBeginCall(messageId, out _);
        }

        private void ObserveConnection()
        {
            string current = _connectionIdentity() ?? string.Empty;
            if (!string.Equals(current, _lastConnectionIdentity, StringComparison.Ordinal))
            {
                _lastConnectionIdentity = current;
                ResetSessionState();
            }
        }

        private void HandleDisconnected()
        {
            ResetSessionState();
        }

        private void ResetSessionState()
        {
            _generation++;
            _settingLoading = false;
            _settingSaving = false;
            _sending = false;
            _pulling = false;
            _pollingActive = false;
            _nextPollAt = 0.0;
            _cooldownUntilLocal = 0.0;
            SettingLoaded = false;
            ActiveBattleId = 0UL;
            ChatEnabled = false;
            BattleEnded = false;
            LatestSeq = 0UL;
            LastError = string.Empty;
            LastErrorTipId = 0u;
            _messages.Clear();
            _seenSeqs.Clear();
            SettingChanged?.Invoke();
            ChatChanged?.Invoke();
        }

        private bool IsCurrent(int generation, ulong playerId)
        {
            if (_disposed) return false;
            ObserveConnection();
            return _generation == generation && PlayerId == playerId && playerId != 0UL;
        }

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0u;

        public static string DescribeTip(TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0u) return string.Empty;
            switch (tip.Id)
            {
                case (uint)match_error.KMatchSpectateChatDisabled:
                    return "本场战斗已关闭观战交流（可正常观战）。";
                case (uint)match_error.KMatchSpectateChatEnded:
                    return "本场战斗已结束，观战交流已关闭。";
                case (uint)match_error.KMatchSpectateChatCooldown:
                    return "发言过于频繁，请稍后再试。";
                case (uint)match_error.KMatchSpectateChatRoomBusy:
                    return "当前观战讨论过于火爆，请稍候再试。";
                case (uint)match_error.KMatchSpectateChatContentRejected:
                    return "发言内容包含不适宜信息，请修改后重试。";
                case (uint)match_error.KMatchSpectateChatCheckUnavailable:
                    return "内容安全审核服务暂不可用，请稍后重试。";
                case (uint)match_error.KMatchBattleNotWatchable:
                    return "该场战斗已结束或不可观战。";
                default:
                    return $"操作未成功（错误码 {tip.Id}）。";
            }
        }

        private static string DescribeTransport(string prefix, string err)
        {
            return string.IsNullOrWhiteSpace(err) ? prefix : $"{prefix}：{err}";
        }
    }
}
