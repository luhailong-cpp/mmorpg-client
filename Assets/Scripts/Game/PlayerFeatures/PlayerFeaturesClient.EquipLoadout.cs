using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using EquipLoadoutModel = MmorpgClient.UI.Ugui.Gameplay.EquipLoadoutModel;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>
    /// 装备流派方案预设 V1 客户端协议协调器：
    /// - 只读读取、乐观锁保存、原子一键换装（同步更新人物背包与装备栏双全量快照）；
    /// - 付费/扩容符解锁走 DurableRequestFileStore 持久化幂等键防连点双扣，确认 durable=true 后才清理。
    /// </summary>
    public sealed partial class PlayerFeaturesClient
    {
        public const string EquipLoadoutUnlockPendingText = "装备方案解锁结果尚未确认，请先恢复原请求";
        private const int EquipLoadoutUnlockRecoveryLimit = 3;
        private const long EquipLoadoutMinSendIntervalMs = 800;

        private EquipLoadoutSnapshot _equipLoadouts;
        private readonly List<uint> _lastLoadoutMissingSlots = new List<uint>();
        private readonly List<string> _lastLoadoutLinkedNotices = new List<string>();
        private UnlockEquipLoadoutRequest _pendingEquipLoadoutUnlock;
        private string _pendingEquipLoadoutUnlockRecord;
        private ulong _equipLoadoutRecoveryPlayer;
        private bool _equipLoadoutRecoveryBlocked;
        private int _equipLoadoutReadRequest, _equipLoadoutWriteRequest, _equipLoadoutRecoveryAttempts;
        private long _equipLoadoutRecoveryAtMs;
        private long? _equipLoadoutLastSendMs;

        public DurableRequestFileStore EquipLoadoutUnlockRecovery { get; set; }
        public EquipLoadoutSnapshot EquipLoadouts => _equipLoadouts?.Clone();
        public bool EquipLoadoutsLoading { get; private set; }
        public bool BusyEquipLoadout { get; private set; }
        public bool BusyEquipLoadoutUnlock { get; private set; }
        public bool HasPendingEquipLoadoutUnlock => _pendingEquipLoadoutUnlock != null || _equipLoadoutRecoveryBlocked;
        public string EquipLoadoutError { get; private set; } = string.Empty;
        public IReadOnlyList<uint> LastEquipLoadoutMissingSlots => _lastLoadoutMissingSlots;
        public IReadOnlyList<string> LastEquipLoadoutLinkedNotices => _lastLoadoutLinkedNotices;

        public event Action<SwitchEquipLoadoutResponse> OnEquipLoadoutSwitched;
        public event Action<UnlockEquipLoadoutResponse> OnEquipLoadoutUnlocked;

        public void RequestEquipLoadouts()
        {
            if (!PrepareRequest(out var playerId)) return;
            RestoreEquipLoadoutUnlock(playerId);
            int epoch = _epoch, generation = ++_equipLoadoutReadRequest;
            EquipLoadoutsLoading = true;
            if (!HasPendingEquipLoadoutUnlock) EquipLoadoutError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutReadRequest) return;
            _net.Call(SceneBagClientPlayerRpc.GetEquipLoadoutsMessageId,
                new GetEquipLoadoutsRequest(), GetEquipLoadoutsResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutReadRequest) return;
                    ++_equipLoadoutReadRequest;
                    EquipLoadoutsLoading = false;
                    if (HasTip(response.ErrorMessage) || response.Snapshot == null)
                    {
                        FailEquipLoadout(HasTip(response.ErrorMessage)
                            ? DescribeEquipTip("读取装备方案失败", response.ErrorMessage)
                            : "装备方案暂未同步，请刷新");
                        return;
                    }
                    _equipLoadouts = response.Snapshot.Clone();
                    if (!HasPendingEquipLoadoutUnlock) EquipLoadoutError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutReadRequest) return;
                    ++_equipLoadoutReadRequest;
                    EquipLoadoutsLoading = false;
                    FailEquipLoadout(TryParseEnvelopeTip(error, out uint tipId)
                        ? DescribeEquipTip("读取装备方案失败", new TipInfoMessage { Id = tipId })
                        : "装备方案暂未同步，请刷新");
                });
        }

        public void SaveEquipLoadout(
            uint schemeId,
            string name,
            uint roleTag,
            bool captureCurrentEquipped,
            IEnumerable<EquipLoadoutSlotBinding> explicitSlots = null,
            uint linkedSkillSchemeId = 0,
            uint linkedAttributeSchemeId = 0)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyEquipLoadout || BusyEquipLoadoutUnlock) return;
            if (HasPendingEquipLoadoutUnlock) { FailEquipLoadout(EquipLoadoutUnlockPendingText); return; }
            if (_equipLoadouts == null) { FailEquipLoadout("装备方案尚未同步，请先刷新"); return; }
            if (schemeId == 0u || schemeId > _equipLoadouts.UnlockedSchemeCount)
            { FailEquipLoadout("目标方案位尚未解锁"); return; }
            if (!CanSendEquipLoadout()) { FailEquipLoadout("操作过于频繁，请稍候重试"); return; }

            string sanitized = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            if (!EquipLoadoutModel.ValidateNameDraft(sanitized, out string nameErr))
            { FailEquipLoadout(nameErr); return; }

            var request = new SaveEquipLoadoutRequest
            {
                SchemeId = schemeId,
                Name = sanitized,
                RoleTag = roleTag,
                CaptureCurrentEquipped = captureCurrentEquipped,
                LinkedSkillSchemeId = linkedSkillSchemeId,
                LinkedAttributeSchemeId = linkedAttributeSchemeId,
                StateVersion = _equipLoadouts.StateVersion,
                RequestId = Guid.NewGuid().ToString("N"),
            };
            if (!captureCurrentEquipped && explicitSlots != null)
            {
                foreach (var binding in explicitSlots)
                    if (binding != null) request.Slots.Add(binding.Clone());
            }

            int epoch = _epoch, generation = ++_equipLoadoutWriteRequest;
            BusyEquipLoadout = true;
            EquipLoadoutError = string.Empty;
            _equipLoadoutLastSendMs = _nowMs();
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutWriteRequest) return;

            _net.Call(SceneBagClientPlayerRpc.SaveEquipLoadoutMessageId, request, SaveEquipLoadoutResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutWriteRequest) return;
                    ++_equipLoadoutWriteRequest;
                    BusyEquipLoadout = false;
                    if (HasTip(response.ErrorMessage))
                    {
                        if (response.Snapshot != null) _equipLoadouts = response.Snapshot.Clone();
                        FailEquipLoadout(DescribeEquipTip("保存方案失败", response.ErrorMessage));
                        return;
                    }
                    if (response.Snapshot != null) _equipLoadouts = response.Snapshot.Clone();
                    EquipLoadoutError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutWriteRequest) return;
                    ++_equipLoadoutWriteRequest;
                    BusyEquipLoadout = false;
                    FailEquipLoadout(TryParseEnvelopeTip(error, out uint tipId)
                        ? DescribeEquipTip("保存方案失败", new TipInfoMessage { Id = tipId })
                        : $"保存方案失败:{NetworkErrorText}");
                    RequestEquipLoadouts();
                });
        }

        public void SwitchEquipLoadout(uint schemeId, bool applyLinkedSchemes = true)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyEquipLoadout || BusyEquipLoadoutUnlock || BusyEquip) return;
            if (HasPendingEquipLoadoutUnlock) { FailEquipLoadout(EquipLoadoutUnlockPendingText); return; }
            if (_equipLoadouts == null) { FailEquipLoadout("装备方案尚未同步，请先刷新"); return; }
            if (schemeId == 0u || schemeId > _equipLoadouts.UnlockedSchemeCount)
            { FailEquipLoadout("目标方案位尚未解锁"); return; }
            if (!CanSendEquipLoadout()) { FailEquipLoadout("操作过于频繁，请稍候重试"); return; }

            var request = new SwitchEquipLoadoutRequest
            {
                SchemeId = schemeId,
                ApplyLinkedSchemes = applyLinkedSchemes,
                StateVersion = _equipLoadouts.StateVersion,
                RequestId = Guid.NewGuid().ToString("N"),
            };

            int epoch = _epoch, generation = ++_equipLoadoutWriteRequest;
            BusyEquipLoadout = true;
            EquipLoadoutError = string.Empty;
            _lastLoadoutMissingSlots.Clear();
            _lastLoadoutLinkedNotices.Clear();
            _equipLoadoutLastSendMs = _nowMs();
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutWriteRequest) return;

            _net.Call(SceneBagClientPlayerRpc.SwitchEquipLoadoutMessageId, request, SwitchEquipLoadoutResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutWriteRequest) return;
                    ++_equipLoadoutWriteRequest;
                    BusyEquipLoadout = false;
                    if (HasTip(response.ErrorMessage))
                    {
                        if (response.Snapshot != null) _equipLoadouts = response.Snapshot.Clone();
                        FailEquipLoadout(DescribeEquipTip("切换方案失败", response.ErrorMessage));
                        return;
                    }
                    if (ValidBag(response.Bag, CharacterBagType))
                    {
                        _bags[CharacterBagType] = response.Bag;
                        if (RequestedBagType == CharacterBagType) { ++_bagRequest; BagLoading = false; BagError = string.Empty; }
                    }
                    if (ValidBag(response.Equipment, EquipmentBagType))
                    {
                        _bags[EquipmentBagType] = response.Equipment;
                        ++_equipmentRequest;
                        EquipmentLoading = false;
                        _equipmentReadError = _equipActionError = string.Empty;
                    }
                    if (response.Snapshot != null) _equipLoadouts = response.Snapshot.Clone();
                    _lastLoadoutMissingSlots.Clear();
                    _lastLoadoutMissingSlots.AddRange(response.MissingSlots);
                    _lastLoadoutLinkedNotices.Clear();
                    _lastLoadoutLinkedNotices.AddRange(response.LinkedNotices);
                    EquipLoadoutError = string.Empty;
                    OnChanged?.Invoke();
                    if (IsCurrent(epoch, playerId)) OnEquipLoadoutSwitched?.Invoke(response);
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutWriteRequest) return;
                    ++_equipLoadoutWriteRequest;
                    BusyEquipLoadout = false;
                    FailEquipLoadout(TryParseEnvelopeTip(error, out uint tipId)
                        ? DescribeEquipTip("切换方案失败", new TipInfoMessage { Id = tipId })
                        : $"切换方案失败:{NetworkErrorText}");
                    RequestBag(CharacterBagType);
                    RequestEquipment();
                    RequestEquipLoadouts();
                });
        }

        public void UnlockEquipLoadout(bool useBindDiamondFirst = false)
        {
            // 方案 A：装备流派方案已并入 7 套三合一共享流派席位（2永久免费 + 85级赠1 + 轻享卡+2 + RentSkillLoadoutSlots按套租用），
            // 旧单独买断入口统一 fail-closed 拒绝，不再发起独立扣费；遗留待恢复记录仍通过 TickEquipLoadoutUnlockRecovery 清理。
            if (!PrepareRequest(out var playerId)) return;
            RestoreEquipLoadoutUnlock(playerId);
            if (HasPendingEquipLoadoutUnlock) { FailEquipLoadout(EquipLoadoutUnlockPendingText); return; }
            FailEquipLoadout("装备方案已并入 7 套三合一共享流派席位，请通过 85 级免费解锁、仙途轻享卡或「管理席位」按套扩展");
        }

        public void RecoverEquipLoadoutUnlock()
        {
            if (PrepareRequest(out var playerId) && CanRecoverEquipLoadoutUnlock())
                SendEquipLoadoutUnlock(playerId);
        }

        public void RetryPendingEquipLoadoutUnlock() => RecoverEquipLoadoutUnlock();

        public void TickEquipLoadoutUnlockRecovery()
        {
            if (_disposed || !_net.IsReady || _net.PlayerId == 0UL) return;
            if (!PrepareRequest(out var playerId)) return;
            RestoreEquipLoadoutUnlock(playerId);
            if (_equipLoadoutRecoveryAttempts >= EquipLoadoutUnlockRecoveryLimit || !CanRecoverEquipLoadoutUnlock()) return;
            ++_equipLoadoutRecoveryAttempts;
            SendEquipLoadoutUnlock(playerId);
        }

        private bool CanSendEquipLoadout()
        {
            if (!_equipLoadoutLastSendMs.HasValue) return true;
            long elapsed = _nowMs() - _equipLoadoutLastSendMs.Value;
            return elapsed < 0 || elapsed >= EquipLoadoutMinSendIntervalMs;
        }

        private bool CanRecoverEquipLoadoutUnlock()
            => _pendingEquipLoadoutUnlock != null && !_equipLoadoutRecoveryBlocked
            && !BusyEquipLoadout && !BusyEquipLoadoutUnlock && _nowMs() >= _equipLoadoutRecoveryAtMs && CanSendEquipLoadout();

        private void RestoreEquipLoadoutUnlock(ulong playerId)
        {
            if (_equipLoadoutRecoveryPlayer == playerId || EquipLoadoutUnlockRecovery == null) return;
            _equipLoadoutRecoveryPlayer = playerId;
            try
            {
                string record = EquipLoadoutUnlockRecovery.Load(playerId);
                if (record.Length == 0) return;
                var request = UnlockEquipLoadoutRequest.Parser.ParseFrom(Convert.FromBase64String(record));
                if (request.ClientRequestId == 0UL || request.ExpectedUnlockedCount == 0u)
                    throw new InvalidOperationException();
                _pendingEquipLoadoutUnlock = request;
                _pendingEquipLoadoutUnlockRecord = record;
                EquipLoadoutError = EquipLoadoutUnlockPendingText;
            }
            catch (Exception)
            {
                _equipLoadoutRecoveryBlocked = true;
                EquipLoadoutError = "装备方案解锁记录损坏或无法读取，请保留记录并联系支持";
            }
        }

        private void SendEquipLoadoutUnlock(ulong playerId)
        {
            int epoch = _epoch, generation = ++_equipLoadoutWriteRequest;
            var request = _pendingEquipLoadoutUnlock.Clone();
            string record = _pendingEquipLoadoutUnlockRecord;
            BusyEquipLoadoutUnlock = true;
            EquipLoadoutError = string.Empty;
            _equipLoadoutLastSendMs = _nowMs();
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutWriteRequest) return;

            _net.Call(SceneBagClientPlayerRpc.UnlockEquipLoadoutMessageId, request, UnlockEquipLoadoutResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutWriteRequest) return;
                    ++_equipLoadoutWriteRequest;
                    BusyEquipLoadoutUnlock = false;
                    if (response.ClientRequestId != request.ClientRequestId)
                    { DeferEquipLoadoutRecovery("方案解锁回执身份不符，原请求已保留"); return; }

                    if (response.Outcome == UnlockEquipLoadoutResponse.Types.Outcome.Rejected)
                    {
                        if (!ClearEquipLoadoutRecord(playerId, record)) return;
                        if (response.Snapshot != null) _equipLoadouts = response.Snapshot.Clone();
                        FailEquipLoadout(HasTip(response.ErrorMessage)
                            ? DescribeEquipTip("解锁方案位失败", response.ErrorMessage)
                            : "解锁方案位被拒绝");
                        return;
                    }
                    bool applied = response.Outcome == UnlockEquipLoadoutResponse.Types.Outcome.Applied
                        && response.Durable && !HasTip(response.ErrorMessage)
                        && response.ResultUnlockedCount > request.ExpectedUnlockedCount;
                    if (!applied)
                    {
                        DeferEquipLoadoutRecovery(EquipLoadoutUnlockPendingText);
                        return;
                    }
                    if (!ClearEquipLoadoutRecord(playerId, record)) return;
                    if (ValidBag(response.Bag, CharacterBagType))
                    {
                        _bags[CharacterBagType] = response.Bag;
                        if (RequestedBagType == CharacterBagType) { ++_bagRequest; BagLoading = false; BagError = string.Empty; }
                    }
                    if (response.Snapshot != null) _equipLoadouts = response.Snapshot.Clone();
                    EquipLoadoutError = string.Empty;
                    OnChanged?.Invoke();
                    if (IsCurrent(epoch, playerId)) OnEquipLoadoutUnlocked?.Invoke(response);
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _equipLoadoutWriteRequest) return;
                    ++_equipLoadoutWriteRequest;
                    BusyEquipLoadoutUnlock = false;
                    DeferEquipLoadoutRecovery(EquipLoadoutUnlockPendingText);
                });
        }

        private void DeferEquipLoadoutRecovery(string message)
        {
            _equipLoadoutRecoveryAtMs = _nowMs() + 2000;
            FailEquipLoadout(message);
        }

        private bool ClearEquipLoadoutRecord(ulong playerId, string expectedRecord)
        {
            try
            {
                EquipLoadoutUnlockRecovery?.Clear(playerId, expectedRecord);
                _pendingEquipLoadoutUnlock = null;
                _pendingEquipLoadoutUnlockRecord = null;
                _equipLoadoutRecoveryBlocked = false;
                _equipLoadoutRecoveryAttempts = 0;
                return true;
            }
            catch (Exception)
            {
                _equipLoadoutRecoveryBlocked = true;
                FailEquipLoadout("方案解锁已确认，但清理本地记录失败，请检查存储后重试");
                return false;
            }
        }

        private void FailEquipLoadout(string message)
        {
            EquipLoadoutError = string.IsNullOrWhiteSpace(message) ? "装备方案请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(EquipLoadoutError);
        }

        private void ResetEquipLoadoutSession()
        {
            ++_equipLoadoutReadRequest;
            ++_equipLoadoutWriteRequest;
            _equipLoadouts = null;
            _lastLoadoutMissingSlots.Clear();
            _lastLoadoutLinkedNotices.Clear();
            _pendingEquipLoadoutUnlock = null;
            _pendingEquipLoadoutUnlockRecord = null;
            _equipLoadoutRecoveryPlayer = 0;
            _equipLoadoutRecoveryBlocked = false;
            _equipLoadoutRecoveryAttempts = 0;
            _equipLoadoutRecoveryAtMs = 0;
            EquipLoadoutsLoading = false;
            BusyEquipLoadout = false;
            BusyEquipLoadoutUnlock = false;
            EquipLoadoutError = string.Empty;
        }
    }
}
