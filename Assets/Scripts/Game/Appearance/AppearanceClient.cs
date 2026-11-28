using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

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
        public uint BuySeriesBundle { get; }
        public uint EquipShowcaseCosmetic { get; }
        public uint CastCityCelebration { get; }
        public uint ClaimCityCelebrationGift { get; }
        public uint NotifyCityCelebration { get; }
        public uint SendCosmeticGift { get; }

        public bool IsSupported => Get != 0 && Switch != 0 && StartTrial != 0 && CancelTrial != 0;
        public bool IsCosmeticsSupported => IsSupported && EquipPetCosmetic != 0 && EquipSkillVfx != 0;
        public bool IsSeriesBundleSupported => IsSupported && BuySeriesBundle != 0;
        public bool IsShowcaseSupported => IsSupported && EquipShowcaseCosmetic != 0;
        public bool IsCelebrationSupported => IsSupported && CastCityCelebration != 0 && ClaimCityCelebrationGift != 0;
        public bool IsGiftSupported => IsSupported && SendCosmeticGift != 0;

        public AppearanceMessageIds(uint get, uint @switch, uint startTrial, uint cancelTrial, uint notifyState,
            uint equipPetCosmetic = 0, uint equipSkillVfx = 0, uint buySeriesBundle = 0,
            uint equipShowcaseCosmetic = 0, uint castCityCelebration = 0, uint claimCityCelebrationGift = 0,
            uint notifyCityCelebration = 0, uint sendCosmeticGift = 0)
        {
            Get = get;
            Switch = @switch;
            StartTrial = startTrial;
            CancelTrial = cancelTrial;
            NotifyState = notifyState;
            EquipPetCosmetic = equipPetCosmetic;
            EquipSkillVfx = equipSkillVfx;
            BuySeriesBundle = buySeriesBundle;
            EquipShowcaseCosmetic = equipShowcaseCosmetic;
            CastCityCelebration = castCityCelebration;
            ClaimCityCelebrationGift = claimCityCelebrationGift;
            NotifyCityCelebration = notifyCityCelebration;
            SendCosmeticGift = sendCosmeticGift;
        }

        public static AppearanceMessageIds Generated => new AppearanceMessageIds(
            MessageIds.GetAppearanceInfo,
            MessageIds.SwitchAppearance,
            MessageIds.StartAppearanceTrial,
            MessageIds.CancelAppearanceTrial,
            MessageIds.NotifyAppearanceState,
            MessageIds.EquipPetCosmetic,
            MessageIds.EquipSkillVfx,
            SceneAppearanceClientPlayerRpc.BuyAppearanceSeriesBundleMessageId,
            SceneAppearanceClientPlayerRpc.EquipShowcaseCosmeticMessageId,
            SceneAppearanceClientPlayerRpc.CastCityCelebrationMessageId,
            SceneAppearanceClientPlayerRpc.ClaimCityCelebrationGiftMessageId,
            SceneAppearanceClientPlayerRpc.NotifyCityCelebrationMessageId,
            SceneAppearanceClientPlayerRpc.SendCosmeticGiftMessageId);
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
            (uint)appearance_error.KAppearanceSeriesBundleUnavailable => "该收藏系列暂未开放补齐优惠。",
            (uint)appearance_error.KAppearanceSeriesAlreadyCompleted => "该收藏系列已集齐，无需再次补齐。",
            (uint)appearance_error.KAppearanceSeriesBundlePriceChanged => "系列缺失成员或补齐价格已变动，已为您刷新报价。",
            (uint)appearance_error.KAppearanceSeriesBundleCurrencyNotEnough => "真实充值灵石不足，无法补齐（绑定灵石不可抵扣系列补齐包）。",
            (uint)appearance_error.KCityCelebrationNotSafeCity => "仅可在天墉城或归元城安全区内施放全城同乐庆典。",
            (uint)appearance_error.KCityCelebrationChannelFull => "当前分线同时进行的庆典已达 5 场上限，请稍后再放。",
            (uint)appearance_error.KCityCelebrationNoItem => "背包缺少对应的全城同乐庆典道具，请先前往百宝阁选购。",
            (uint)appearance_error.KCityCelebrationNotFound => "该全城庆典已结束或不存在。",
            (uint)appearance_error.KCityCelebrationAlreadyClaimed => "您已领取过该场庆典的绑银红包。",
            (uint)appearance_error.KCityCelebrationLevelTooLow => "角色需达到 10 级方可领取全城同乐绑银红包。",
            (uint)appearance_error.KCityCelebrationWeeklyCapReached => "本周领取全城同乐绑银已达 3000 上限。",
            (uint)appearance_error.KCosmeticGiftNotFriend7Days => "需成为好友满 7 天且未互拉黑，方可互赠纯外观或游历手记。",
            (uint)appearance_error.KCosmeticGiftTargetAlreadyOwned => "对方已拥有该外观或该期游历手记，无需重复赠送。",
            (uint)appearance_error.KCosmeticGiftWeeklyLimitReached => "本周外观赠礼次数已达 5 次上限。",
            (uint)appearance_error.KCosmeticGiftInvalidTarget => "受赠目标无效或该外观暂不支持赠礼。",
            (uint)appearance_error.KCosmeticGiftCurrencyNotEnough => "真实充值灵石不足（绑定灵石不可用于好友赠礼）。",
            (uint)appearance_error.KCosmeticGiftPriceChanged => "赠礼报价已更新，已为您刷新价格。",
            (uint)appearance_error.KCosmeticGiftServiceUnavailable => "好友赠礼服务暂时不可用，请稍后再试。",
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
        public event Action<ulong, uint> PetCosmeticEquipped;
        public event Action<global::CityCelebrationNotify> CityCelebrationReceived;

        private readonly List<global::CityCelebrationView> _activeCelebrations = new();

        public global::PlayerAppearanceInfo Info { get; private set; }
        public IReadOnlyList<global::CityCelebrationView> ActiveCityCelebrations => _activeCelebrations;
        public bool InfoPending { get; private set; }
        public bool ActionPending { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "";
        public bool StatusIsError { get; private set; }

        public bool IsSupported => _messageIds.IsSupported;
        public bool IsCosmeticsSupported => _messageIds.IsCosmeticsSupported;
        public bool IsSeriesBundleSupported => _messageIds.IsSeriesBundleSupported;
        public bool IsShowcaseSupported => _messageIds.IsShowcaseSupported;
        public bool IsCelebrationSupported => _messageIds.IsCelebrationSupported;
        public bool IsGiftSupported => _messageIds.IsGiftSupported;
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
            if (_messageIds.NotifyCityCelebration != 0)
                _net.RegisterNotify(_messageIds.NotifyCityCelebration, HandleCityCelebrationNotify);
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

        /// <summary>为指定灵宠实例穿戴、耗券解锁并穿戴或恢复原形（cosmeticId = 0 表示恢复原形；超时自动重发一次同幂等键并重查当前穿戴）。</summary>
        public bool EquipPetCosmetic(ulong petId, uint cosmeticId, bool expectFreeEquip = true)
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
            SetStatus(cosmeticId == 0u ? "正在恢复灵宠原形…" : (expectFreeEquip ? "正在穿戴灵宠幻化…" : "正在解锁并穿戴灵宠幻化…"), false);
            Changed?.Invoke();

            var request = new global::EquipPetCosmeticRequest
            {
                ClientRequestId = requestKey,
                PetId = petId,
                CosmeticId = cosmeticId,
                ExpectFreeEquip = expectFreeEquip,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.EquipPetCosmetic, request, global::EquipPetCosmeticResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("EquipPetCosmetic", tipId);
                            return;
                        }
                        if (response.Info != null) ApplyInfo(response.Info);
                        SetStatus(response.EquippedCosmeticId == 0u ? "已恢复灵宠原形。" : (expectFreeEquip ? "已穿戴灵宠幻化。" : "已永久解锁并穿戴灵宠幻化。"), false);
                        PetCosmeticEquipped?.Invoke(response.PetId != 0ul ? response.PetId : petId, response.EquippedCosmeticId);
                        Changed?.Invoke();
                    },
                    error =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        if (!expectFreeEquip && !resent && ClientRequestKeys.ShouldResendOnce(error))
                        {
                            resent = true;
                            Send();
                            return;
                        }
                        ActionPending = false;
                        uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
                        SetStatus(tipId != 0 ? AppearanceModels.DescribeTip(tipId) : "请求超时，正在重新确认当前穿戴状态…", true);
                        RequestInfo(force: true);
                        Changed?.Invoke();
                    });
            }
            Send();
            return true;
        }

        /// <summary>为指定技能启用、耗券解锁并启用或恢复默认特效（cosmeticId = 0 表示恢复默认；超时自动重发一次同幂等键并重查当前状态）。</summary>
        public bool EquipSkillVfx(uint skillTableId, uint cosmeticId, bool expectFreeEquip = true)
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
            SetStatus(cosmeticId == 0u ? "正在恢复默认特效…" : (expectFreeEquip ? "正在启用技能特效…" : "正在解锁并启用技能特效…"), false);
            Changed?.Invoke();

            var request = new global::EquipSkillVfxRequest
            {
                ClientRequestId = requestKey,
                SkillTableId = skillTableId,
                CosmeticId = cosmeticId,
                ExpectFreeEquip = expectFreeEquip,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.EquipSkillVfx, request, global::EquipSkillVfxResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("EquipSkillVfx", tipId);
                            return;
                        }
                        if (response.Info != null) ApplyInfo(response.Info);
                        SetStatus(response.EquippedCosmeticId == 0u ? "已恢复技能默认特效。" : (expectFreeEquip ? "已启用技能特效。" : "已永久解锁并启用技能特效。"), false);
                        Changed?.Invoke();
                    },
                    error =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        if (!expectFreeEquip && !resent && ClientRequestKeys.ShouldResendOnce(error))
                        {
                            resent = true;
                            Send();
                            return;
                        }
                        ActionPending = false;
                        uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
                        SetStatus(tipId != 0 ? AppearanceModels.DescribeTip(tipId) : "请求超时，正在重新确认当前生效状态…", true);
                        RequestInfo(force: true);
                        Changed?.Invoke();
                    });
            }
            Send();
            return true;
        }

        /// <summary>Steam 式按缺口 8.5 折一键补齐收藏系列缺失角色（仅扣真实充值灵石，强制幂等键与报价防呆）。</summary>
        public bool BuySeriesBundle(uint seriesId)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsSeriesBundleSupported) return RejectLocally("当前服务器暂未开放收藏系列补齐优惠。");
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            var series = AppearanceSeriesModels.FindSeries(Info, seriesId);
            if (!AppearanceSeriesModels.CanBuySeriesBundle(series))
                return RejectLocally(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceSeriesBundleUnavailable));
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out ulong requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);

            var expectedIds = new List<string>(series.MissingAppearanceIds.Count);
            for (int i = 0; i < series.MissingAppearanceIds.Count; i++)
            {
                if (!string.IsNullOrEmpty(series.MissingAppearanceIds[i]))
                    expectedIds.Add(series.MissingAppearanceIds[i]);
            }
            expectedIds.Sort(StringComparer.Ordinal);
            if (expectedIds.Count == 0)
                return RejectLocally(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceSeriesBundleUnavailable));

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus($"正在补齐系列【{series.Name}】…", false);
            Changed?.Invoke();

            var request = new global::BuyAppearanceSeriesBundleRequest
            {
                ClientRequestId = requestKey,
                SeriesId = seriesId,
                ExpectedPriceDiamond = series.BundleDiscountDiamond,
            };
            request.ExpectedMissingAppearanceIds.AddRange(expectedIds);

            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.BuySeriesBundle, request, global::BuyAppearanceSeriesBundleResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("BuyAppearanceSeriesBundle", tipId);
                            return;
                        }
                        if (response.Info != null) ApplyInfo(response.Info);
                        string msg = response.UnlockedCount > 0
                            ? $"已补齐 {response.UnlockedCount} 位角色（消耗 {response.SpentDiamond} 灵石），系列印章与合篇已解锁。"
                            : "已完成系列补齐。";
                        SetStatus(msg, false);
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

        /// <summary>穿戴、耗排面装饰券解锁并穿戴或恢复默认排面装饰（slotKind=1阵纹/2入场胜利/3气泡/4战报卡，cosmeticId=0恢复默认）。</summary>
        public bool EquipShowcaseCosmetic(uint slotKind, uint cosmeticId, bool expectFreeEquip = true)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsShowcaseSupported) return RejectLocally("当前服务器暂未开放排面装饰。");
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            if (slotKind < 1u || slotKind > 4u) return RejectLocally("请选择有效的排面槽位。");
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out ulong requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(cosmeticId == 0u ? "正在恢复默认排面…" : (expectFreeEquip ? "正在穿戴排面装饰…" : "正在解锁并穿戴排面装饰…"), false);
            Changed?.Invoke();

            var request = new global::EquipShowcaseCosmeticRequest
            {
                ClientRequestId = requestKey,
                SlotKind = slotKind,
                CosmeticId = cosmeticId,
                ExpectFreeEquip = expectFreeEquip,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.EquipShowcaseCosmetic, request, global::EquipShowcaseCosmeticResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("EquipShowcaseCosmetic", tipId);
                            return;
                        }
                        if (response.Info != null) ApplyInfo(response.Info);
                        SetStatus(response.EquippedCosmeticId == 0u ? "已恢复默认排面。" : (expectFreeEquip ? "已启用排面装饰。" : "已永久解锁并启用排面装饰。"), false);
                        Changed?.Invoke();
                    },
                    error =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        if (!expectFreeEquip && !resent && ClientRequestKeys.ShouldResendOnce(error))
                        {
                            resent = true;
                            Send();
                            return;
                        }
                        ActionPending = false;
                        uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
                        SetStatus(tipId != 0 ? AppearanceModels.DescribeTip(tipId) : "请求超时，正在重新确认当前生效状态…", true);
                        RequestInfo(force: true);
                        Changed?.Invoke();
                    });
            }
            Send();
            return true;
        }

        /// <summary>在安全区主城施放全城同乐氛围耗材（天墉灵灯/庆功烟花/仙茶同乐席）。</summary>
        public bool CastCityCelebration(uint itemConfigId)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsCelebrationSupported) return RejectLocally("当前服务器暂未开放全城同乐庆典。");
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            if (itemConfigId == 0u) return RejectLocally("请选择有效的庆典道具。");
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out ulong requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus("正在施放全城同乐庆典…", false);
            Changed?.Invoke();

            var request = new global::CastCityCelebrationRequest
            {
                ClientRequestId = requestKey,
                ItemConfigId = itemConfigId,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.CastCityCelebration, request, global::CastCityCelebrationResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("CastCityCelebration", tipId);
                            return;
                        }
                        if (response.Celebration != null) UpsertCelebration(response.Celebration);
                        SetStatus("全城同乐庆典已绽放！城中道友均可沾喜领绑银。", false);
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

        /// <summary>领取当前主城活跃庆典的绑银红包（≥10级，每人每场限 1 次，每周 3000 绑银上限）。</summary>
        public bool ClaimCityCelebrationGift(ulong celebrationId)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsCelebrationSupported) return RejectLocally("当前服务器暂未开放全城同乐庆典。");
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            if (celebrationId == 0ul) return RejectLocally("该庆典已结束。");
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out ulong requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus("正在领取全城同乐绑银…", false);
            Changed?.Invoke();

            var request = new global::ClaimCityCelebrationGiftRequest
            {
                ClientRequestId = requestKey,
                CelebrationId = celebrationId,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.ClaimCityCelebrationGift, request, global::ClaimCityCelebrationGiftResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("ClaimCityCelebrationGift", tipId);
                            return;
                        }
                        MarkCelebrationClaimed(response.CelebrationId != 0ul ? response.CelebrationId : celebrationId);
                        if (Info != null)
                        {
                            Info.CelebrationBindGoldClaimed = response.WeeklyClaimedTotal;
                            if (response.WeeklyCap > 0u) Info.CelebrationBindGoldWeeklyCap = response.WeeklyCap;
                        }
                        SetStatus($"已领取 {response.GrantedBindGold} 绑定银两（本周 {response.WeeklyClaimedTotal}/{response.WeeklyCap}）。", false);
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

        /// <summary>向满 7 天好友赠送绑定纯外观或游历手记（仅扣真实充值灵石，强制幂等键与报价校验）。</summary>
        public bool SendCosmeticGift(
            ulong targetPlayerId,
            global::CosmeticGiftTargetKind targetKind,
            uint targetIdU32,
            string targetIdStr,
            ulong expectedPriceDiamond)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsGiftSupported) return RejectLocally("当前服务器暂未开放好友外观赠礼。");
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            if (targetPlayerId == 0ul || targetPlayerId == _net.PlayerId)
                return RejectLocally(AppearanceModels.DescribeTip((uint)appearance_error.KCosmeticGiftInvalidTarget));
            if (targetKind == global::CosmeticGiftTargetKind.CosmeticGiftTargetUnspecified || expectedPriceDiamond == 0ul)
                return RejectLocally(AppearanceModels.DescribeTip((uint)appearance_error.KCosmeticGiftInvalidTarget));
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out ulong requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus("正在发送好友外观赠礼…", false);
            Changed?.Invoke();

            var request = new global::SendCosmeticGiftRequest
            {
                ClientRequestId = requestKey,
                TargetPlayerId = targetPlayerId,
                TargetKind = targetKind,
                TargetIdU32 = targetIdU32,
                TargetIdStr = targetIdStr ?? string.Empty,
                ExpectedPriceDiamond = expectedPriceDiamond,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.SendCosmeticGift, request, global::SendCosmeticGiftResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("SendCosmeticGift", tipId);
                            return;
                        }
                        if (Info != null)
                        {
                            Info.DiamondBalance = response.DiamondBalance;
                            Info.CosmeticGiftSentThisWeek = response.SentThisWeek;
                            if (response.WeeklyLimit > 0u) Info.CosmeticGiftWeeklyLimit = response.WeeklyLimit;
                        }
                        SetStatus($"赠礼已发出（消耗 {response.SpentDiamond} 灵石，本周 {response.SentThisWeek}/{response.WeeklyLimit}）。", false);
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

        private void HandleCityCelebrationNotify(MessageContent mc)
        {
            if (_disposed || mc == null) return;
            ObserveConnection();
            if (!Ready) return;
            global::CityCelebrationNotify notify;
            try { notify = global::CityCelebrationNotify.Parser.ParseFrom(mc.SerializedMessage); }
            catch (InvalidProtocolBufferException) { return; }
            if (notify.Celebration != null)
            {
                UpsertCelebration(notify.Celebration);
                string caster = string.IsNullOrWhiteSpace(notify.Celebration.CasterName)
                    ? $"道友 {notify.Celebration.CasterPlayerId}" : notify.Celebration.CasterName;
                Notice?.Invoke($"{caster} 正在主城施放全城同乐庆典，可沾喜领绑银！");
            }
            CityCelebrationReceived?.Invoke(notify);
            Changed?.Invoke();
        }

        private void UpsertCelebration(global::CityCelebrationView view)
        {
            if (view == null || view.CelebrationId == 0ul) return;
            ulong nowSec = ServerNowSec;
            for (int i = _activeCelebrations.Count - 1; i >= 0; i--)
            {
                if (_activeCelebrations[i].CelebrationId == view.CelebrationId
                    || (nowSec > 0ul && _activeCelebrations[i].ExpiresAtSec <= nowSec))
                {
                    _activeCelebrations.RemoveAt(i);
                }
            }
            _activeCelebrations.Add(view);
        }

        private void MarkCelebrationClaimed(ulong celebrationId)
        {
            for (int i = 0; i < _activeCelebrations.Count; i++)
            {
                if (_activeCelebrations[i].CelebrationId == celebrationId)
                {
                    _activeCelebrations[i].ClaimedByMe = true;
                    break;
                }
            }
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
                || tipId == (uint)appearance_error.KAppearanceSeriesBundleUnavailable
                || tipId == (uint)appearance_error.KAppearanceSeriesAlreadyCompleted
                || tipId == (uint)appearance_error.KAppearanceSeriesBundlePriceChanged
                || tipId == (uint)appearance_error.KCityCelebrationNoItem
                || tipId == (uint)appearance_error.KCityCelebrationWeeklyCapReached
                || tipId == (uint)appearance_error.KCosmeticGiftPriceChanged
                || tipId == (uint)appearance_error.KCosmeticGiftWeeklyLimitReached
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
            _activeCelebrations.Clear();
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
