using System;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Appearance
{
    /// <summary>角色形象切换与 1 小时免费试用的消息号契约（服务端 proto/scene/player_appearance.proto）。</summary>
    public readonly struct AppearanceMessageIds
    {
        public uint Get { get; }
        public uint Switch { get; }
        public uint StartTrial { get; }
        public uint CancelTrial { get; }
        public uint NotifyState { get; }
        public uint EquipPetCosmetic { get; }
        public uint EquipSkillVfx { get; }

        public bool IsSupported => Get != 0 && Switch != 0 && StartTrial != 0 && CancelTrial != 0;
        public bool IsCosmeticsSupported => IsSupported && EquipPetCosmetic != 0 && EquipSkillVfx != 0;

        public AppearanceMessageIds(uint get, uint @switch, uint startTrial, uint cancelTrial, uint notifyState,
            uint equipPetCosmetic = 0, uint equipSkillVfx = 0)
        {
            Get = get;
            Switch = @switch;
            StartTrial = startTrial;
            CancelTrial = cancelTrial;
            NotifyState = notifyState;
            EquipPetCosmetic = equipPetCosmetic;
            EquipSkillVfx = equipSkillVfx;
        }

        public static AppearanceMessageIds Generated => new AppearanceMessageIds(
            MessageIds.GetAppearanceInfo,
            MessageIds.SwitchAppearance,
            MessageIds.StartAppearanceTrial,
            MessageIds.CancelAppearanceTrial,
            MessageIds.NotifyAppearanceState,
            MessageIds.EquipPetCosmetic,
            MessageIds.EquipSkillVfx);
    }

    /// <summary>角色形象系统的纯规则与文案函数（不引用 UnityEngine，EditMode 测试直接调用）。</summary>
    public static class AppearanceModels
    {
        public const string TrialExpiredText = "形象试用已到期，已自动恢复为原永久形象。";

        /// <summary>按与服务端一致的低位在前紧凑位图读取第 bitIndex 位（越界返回 false）。</summary>
        public static bool IsBitSet(byte[] bits, int bitIndex)
        {
            if (bits == null || bitIndex < 0) return false;
            int byteIndex = bitIndex >> 3;
            return byteIndex < bits.Length && ((bits[byteIndex] & (1 << (bitIndex & 7))) != 0);
        }

        /// <summary>将人民币分格式化为中文价格（如 1000 -> "10元"，1050 -> "10.50元"）。</summary>
        public static string FormatPriceYuan(ulong fen)
        {
            ulong yuan = fen / 100;
            ulong rem = fen % 100;
            return rem == 0 ? $"{yuan}元" : $"{yuan}.{rem:D2}元";
        }

        /// <summary>计算试用剩余秒数（以服务器墙钟为准，不信本机墙钟）。</summary>
        public static ulong RemainingSeconds(ulong expiresAt, ulong serverNow)
            => expiresAt > serverNow ? expiresAt - serverNow : 0;

        /// <summary>格式化 1 小时试用倒计时（如 "59分32秒" / "45秒" / "已到期"）。</summary>
        public static string FormatTrialCountdown(ulong remainingSeconds)
        {
            if (remainingSeconds == 0) return "已到期";
            ulong hours = remainingSeconds / 3600;
            ulong minutes = (remainingSeconds % 3600) / 60;
            ulong seconds = remainingSeconds % 60;
            if (hours > 0) return $"{hours}小时{minutes:D2}分{seconds:D2}秒";
            if (minutes > 0) return $"{minutes}分{seconds:D2}秒";
            return $"{seconds}秒";
        }

        /// <summary>将服务端 appearance_error 与通用错误码转换为中文提示，禁止硬编码数字。</summary>
        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)appearance_error.KAppearanceInvalid => "目标形象不存在或暂未开放。",
            (uint)appearance_error.KAppearanceAlreadyCurrent => "该形象已是当前永久形象，无需切换。",
            (uint)appearance_error.KAppearanceNoCard => "角色形象卡不足，请先前往官方商城购买（10元/张）。",
            (uint)appearance_error.KAppearanceTrialAlreadyUsed => "该形象已免费试用过，每个形象每角色限试用 1 次。",
            (uint)appearance_error.KAppearanceNoActiveTrial => "当前没有正在进行中的试用形象。",
            (uint)appearance_error.KAppearanceFrozen => "角色当前状态暂不可变更形象，请稍后再试。",
            (uint)appearance_error.KAppearanceUnavailable => "角色形象服务暂时不可用，请稍后再试。",
            (uint)appearance_error.KAppearanceNotCardUnlockable => "该角色形象需通过专属玩法或活动解锁，暂不可使用形象卡兑换。",
            (uint)appearance_error.KAppearanceAlreadyOwned => "已拥有该角色形象，可直接免费切换，无需试用。",
            (uint)appearance_error.KAppearanceNotOwned => "尚未拥有该外观，请先获取后再穿戴。",
            (uint)pet_error.KPetNotFound => "目标灵宠不存在或已离队，请刷新后重试。",
            (uint)common_error.KRateLimitExceeded => "操作太快了，请稍候再试。",
            (uint)common_error.KServiceUnavailable => "服务暂时不可用，请稍后再试。",
            (uint)common_error.KInvalidParameter => "请求参数无效，请刷新页面后再试。",
            _ => $"操作失败（{tipId}）。",
        };
    }

    /// <summary>
    /// 角色形象切换与 1 小时免费试用的网络协调器（纯 C#，WebGL 单线程安全，EditMode 可直接实例化）。
    /// 遵循与 <see cref="AfkPass.AfkPassClient"/> 一致的连接世代隔离、幂等键防重、超时同键单次重发与断线复位契约。
    /// </summary>
    public sealed class AppearanceClient : IDisposable
    {
        public const float MinInfoIntervalSeconds = 2.0f;
        public const float MinActionIntervalSeconds = 0.8f;

        public const string UnsupportedMessage = "当前服务器未开放角色形象切换。";
        public const string NotConnectedMessage = "尚未连接到游戏。";
        public const string InfoUnavailableMessage = "暂时无法获取角色形象信息。";
        public const string SwitchPendingMessage = "正在切换角色形象…";
        public const string SwitchedMessage = "已永久解锁并切换角色形象。";
        public const string FreeSwitchedMessage = "已免费切换永久形象。";
        public const string TrialPendingMessage = "正在开启 1 小时免费试用…";
        public const string TrialStartedMessage = "已开启 1 小时免费试用，到期将自动恢复原形象。";
        public const string CancelPendingMessage = "正在恢复原形象…";
        public const string TrialCancelledMessage = "已恢复原永久形象。";
        public const string TooFastMessage = "操作太快了，请稍候再试。";
        public const string RecoveryMessage = "网络异常，操作结果未知。请重新登录后查看。";

        private readonly IBattleTransport _net;
        private readonly AppearanceMessageIds _messageIds;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;

        private object _observedConnection;
        private ulong _observedPlayerId;
        private bool _disposed;
        private int _generation;
        private float _infoReceivedAt;
        private float _nextInfoAt = float.NegativeInfinity;
        private float _nextActionAt = float.NegativeInfinity;
        private bool _infoQueued;

        public event Action Changed;
        public event Action<string> Notice;

        public global::PlayerAppearanceInfo Info { get; private set; }
        public bool InfoPending { get; private set; }
        public bool ActionPending { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "";
        public bool StatusIsError { get; private set; }

        public bool IsSupported => _messageIds.IsSupported;
        public bool IsCosmeticsSupported => _messageIds.IsCosmeticsSupported;
        public bool Ready => IsSupported && !_disposed && _net.IsReady && _net.PlayerId != 0;

        public ulong ServerNowSec
        {
            get
            {
                if (Info == null) return 0;
                float elapsed = _clock() - _infoReceivedAt;
                return elapsed > 0f ? Info.ServerNow + (ulong)elapsed : Info.ServerNow;
            }
        }

        public ulong TrialRemainingSeconds => Info == null || string.IsNullOrEmpty(Info.TrialAppearanceId)
            ? 0
            : AppearanceModels.RemainingSeconds(Info.TrialExpiresAt, ServerNowSec);

        public bool HasActiveTrial => TrialRemainingSeconds > 0;

        public AppearanceClient(IBattleTransport transport, Func<object> connectionIdentity, Func<float> clock,
            AppearanceMessageIds? messageIds = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _messageIds = messageIds ?? AppearanceMessageIds.Generated;
            _observedConnection = _connectionIdentity();
            _observedPlayerId = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
            if (_messageIds.NotifyState != 0)
                _net.RegisterNotify(_messageIds.NotifyState, HandleAppearanceStateNotify);
            if (!IsSupported) SetStatus(UnsupportedMessage, false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_generation;
            InfoPending = false;
            ActionPending = false;
        }

        public void RequestInfo(bool force = false)
        {
            if (_disposed) return;
            if (!IsSupported) { SetStatus(UnsupportedMessage, false); return; }
            ObserveConnection();
            if (!Ready || InfoPending) return;
            float now = _clock();
            if (!force && now < _nextInfoAt)
            {
                _infoQueued = true;
                return;
            }
            SendInfo(now);
        }

        private void SendInfo(float now)
        {
            _infoQueued = false;
            InfoPending = true;
            _nextInfoAt = now + MinInfoIntervalSeconds;
            int generation = _generation;
            Changed?.Invoke();
            _net.Call(_messageIds.Get, new global::GetAppearanceInfoRequest(), global::GetAppearanceInfoResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    InfoPending = false;
                    uint tip = response.ErrorMessage?.Id ?? 0;
                    if (tip != 0 || response.Info == null)
                        SetInfoStatus(tip != 0 ? AppearanceModels.DescribeTip(tip) : InfoUnavailableMessage, true);
                    else
                    {
                        ApplyInfo(response.Info);
                        if (Status == InfoUnavailableMessage) SetInfoStatus("", false);
                    }
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    InfoPending = false;
                    SetInfoStatus(InfoUnavailableMessage, true);
                    Changed?.Invoke();
                });
        }

        public bool SwitchAppearance(string targetAppearanceId, bool expectFreeSwitch = false)
        {
            if (!PrepareWriteAction(targetAppearanceId, out ulong requestKey, out float now)) return false;
            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(SwitchPendingMessage, false);
            Changed?.Invoke();

            var request = new global::SwitchAppearanceRequest
            {
                ClientRequestId = requestKey,
                AppearanceId = targetAppearanceId,
                ExpectFreeSwitch = expectFreeSwitch,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.Switch, request, global::SwitchAppearanceResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("SwitchAppearance", tipId);
                            return;
                        }
                        if (response.Info != null) ApplyInfo(response.Info);
                        SetStatus(expectFreeSwitch ? FreeSwitchedMessage : SwitchedMessage, false);
                        Changed?.Invoke();
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
                        HandleWriteTransportError(error, resent);
                    });
            }
            Send();
            return true;
        }

        public bool StartTrial(string targetAppearanceId)
        {
            if (!PrepareWriteAction(targetAppearanceId, out ulong requestKey, out float now)) return false;
            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(TrialPendingMessage, false);
            Changed?.Invoke();

            var request = new global::StartAppearanceTrialRequest
            {
                ClientRequestId = requestKey,
                AppearanceId = targetAppearanceId,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.StartTrial, request, global::StartAppearanceTrialResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("StartAppearanceTrial", tipId);
                            return;
                        }
                        if (response.Info != null) ApplyInfo(response.Info);
                        SetStatus(TrialStartedMessage, false);
                        Changed?.Invoke();
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
                        HandleWriteTransportError(error, resent);
                    });
            }
            Send();
            return true;
        }

        public bool CancelTrial()
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsSupported) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending) return false;
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(CancelPendingMessage, false);
            Changed?.Invoke();

            _net.Call(_messageIds.CancelTrial, new global::CancelAppearanceTrialRequest(),
                global::CancelAppearanceTrialResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        SetStatus(AppearanceModels.DescribeTip(tipId), true);
                        Changed?.Invoke();
                        return;
                    }
                    if (response.Info != null) ApplyInfo(response.Info);
                    SetStatus(TrialCancelledMessage, false);
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
                    SetStatus(tipId != 0 ? AppearanceModels.DescribeTip(tipId) : InfoUnavailableMessage, true);
                    Changed?.Invoke();
                });
            return true;
        }

        /// <summary>为指定灵宠实例穿戴或恢复原形（cosmeticId = 0 表示恢复原形；超时自动重查当前穿戴）。</summary>
        public bool EquipPetCosmetic(ulong petId, uint cosmeticId)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsCosmeticsSupported) return RejectLocally("当前服务器暂未开放灵宠幻化。");
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            if (petId == 0ul) return RejectLocally("请选择有效的灵宠。");
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out ulong requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(cosmeticId == 0u ? "正在恢复灵宠原形…" : "正在穿戴灵宠幻化…", false);
            Changed?.Invoke();

            var request = new global::EquipPetCosmeticRequest
            {
                ClientRequestId = requestKey,
                PetId = petId,
                CosmeticId = cosmeticId,
            };
            _net.Call(_messageIds.EquipPetCosmetic, request, global::EquipPetCosmeticResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleWriteTip("EquipPetCosmetic", tipId);
                        return;
                    }
                    if (response.Info != null) ApplyInfo(response.Info);
                    SetStatus(response.EquippedCosmeticId == 0u ? "已恢复灵宠原形。" : "已穿戴灵宠幻化。", false);
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
                    SetStatus(tipId != 0 ? AppearanceModels.DescribeTip(tipId) : "请求超时，正在重新确认当前穿戴状态…", true);
                    RequestInfo(force: true);
                    Changed?.Invoke();
                });
            return true;
        }

        /// <summary>为指定技能启用或恢复默认特效（cosmeticId = 0 表示恢复默认；超时自动重查当前穿戴）。</summary>
        public bool EquipSkillVfx(uint skillTableId, uint cosmeticId)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsCosmeticsSupported) return RejectLocally("当前服务器暂未开放技能特效。");
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            if (skillTableId == 0u) return RejectLocally("请选择有效的适用技能。");
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out ulong requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(cosmeticId == 0u ? "正在恢复默认特效…" : "正在启用技能特效…", false);
            Changed?.Invoke();

            var request = new global::EquipSkillVfxRequest
            {
                ClientRequestId = requestKey,
                SkillTableId = skillTableId,
                CosmeticId = cosmeticId,
            };
            _net.Call(_messageIds.EquipSkillVfx, request, global::EquipSkillVfxResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleWriteTip("EquipSkillVfx", tipId);
                        return;
                    }
                    if (response.Info != null) ApplyInfo(response.Info);
                    SetStatus(response.EquippedCosmeticId == 0u ? "已恢复技能默认特效。" : "已启用技能特效。", false);
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
                    SetStatus(tipId != 0 ? AppearanceModels.DescribeTip(tipId) : "请求超时，正在重新确认当前生效状态…", true);
                    RequestInfo(force: true);
                    Changed?.Invoke();
                });
            return true;
        }

        public void HandleSceneEntered()
        {
            if (_disposed) return;
            ObserveConnection();
            if (Info == null && !InfoPending) RequestInfo();
        }

        public void HandleDisconnected()
        {
            if (_disposed) return;
            Reset();
            Changed?.Invoke();
        }

        public void Tick()
        {
            if (_disposed || !IsSupported) return;
            ObserveConnection();
            float now = _clock();
            if (_infoQueued && Ready && !InfoPending && now >= _nextInfoAt) SendInfo(now);
        }

        private void HandleAppearanceStateNotify(MessageContent mc)
        {
            if (_disposed || mc == null) return;
            ObserveConnection();
            // 断线后排队的旧推送不得重新填回已清空的角色快照。
            if (!Ready) return;
            global::AppearanceStateNotify notify;
            try { notify = global::AppearanceStateNotify.Parser.ParseFrom(mc.SerializedMessage); }
            catch (InvalidProtocolBufferException) { return; }
            if (notify.Info != null) ApplyInfo(notify.Info);
            if (notify.TrialExpired)
            {
                SetInfoStatus(AppearanceModels.TrialExpiredText, false);
                Notice?.Invoke(AppearanceModels.TrialExpiredText);
            }
            Changed?.Invoke();
        }

        private bool PrepareWriteAction(string targetAppearanceId, out ulong requestKey, out float now)
        {
            requestKey = 0;
            now = 0;
            if (_disposed) return false;
            ObserveConnection();
            if (!IsSupported) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            if (string.IsNullOrEmpty(targetAppearanceId)) return RejectLocally(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceInvalid));
            now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);
            return true;
        }

        private void HandleWriteTip(string opName, uint tipId)
        {
            if (tipId == (uint)common_error.KInvalidParameter) ClientRequestKeys.ReportKeyReused(opName);
            if (tipId == (uint)common_error.KRateLimitExceeded) _nextActionAt = _clock() + MinInfoIntervalSeconds;
            SetStatus(AppearanceModels.DescribeTip(tipId), true);
            if (tipId == (uint)appearance_error.KAppearanceNoCard
                || tipId == (uint)appearance_error.KAppearanceTrialAlreadyUsed
                || tipId == (uint)appearance_error.KAppearanceNotOwned
                || tipId == (uint)appearance_error.KAppearanceNotCardUnlockable
                || tipId == (uint)appearance_error.KAppearanceAlreadyOwned
                || tipId == (uint)pet_error.KPetNotFound)
                RequestInfo(force: true);
            Changed?.Invoke();
        }

        private void HandleWriteTransportError(string error, bool resent)
        {
            ActionPending = false;
            uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
            if (!resent && tipId != 0 && tipId != (uint)common_error.KServiceUnavailable)
            {
                if (tipId == (uint)common_error.KRateLimitExceeded) _nextActionAt = _clock() + MinInfoIntervalSeconds;
                SetStatus(AppearanceModels.DescribeTip(tipId), true);
            }
            else
            {
                RequiresReconnect = true;
                SetStatus(RecoveryMessage, true);
            }
            Changed?.Invoke();
        }

        private void ApplyInfo(global::PlayerAppearanceInfo info)
        {
            Info = info;
            _infoReceivedAt = _clock();
        }

        private void ObserveConnection()
        {
            var current = _connectionIdentity();
            ulong currentPlayerId = _net.PlayerId;
            if (ReferenceEquals(current, _observedConnection) && currentPlayerId == _observedPlayerId) return;
            _observedConnection = current;
            _observedPlayerId = currentPlayerId;
            Reset();
        }

        private void Reset()
        {
            ++_generation;
            Info = null;
            InfoPending = false;
            ActionPending = false;
            RequiresReconnect = false;
            _infoQueued = false;
            SetStatus(IsSupported ? "" : UnsupportedMessage, false);
        }

        private bool IsCurrent(int generation)
        {
            if (_disposed) return false;
            // 回调可能先于下一帧 Tick 抵达；必须在写入状态前核对连接与当前角色。
            ObserveConnection();
            return generation == _generation;
        }

        private bool RejectLocally(string message)
        {
            SetStatus(message, true);
            Changed?.Invoke();
            return false;
        }

        private void SetInfoStatus(string message, bool isError)
        {
            if (RequiresReconnect) return;
            SetStatus(message, isError);
        }

        private void SetStatus(string message, bool isError)
        {
            Status = message ?? "";
            StatusIsError = isError && Status.Length != 0;
        }
    }
}
