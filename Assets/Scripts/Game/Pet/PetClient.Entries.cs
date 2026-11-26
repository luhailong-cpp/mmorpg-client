using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Pet
{
    public sealed partial class PetClient
    {
        private int _entriesGeneration;
        private SavePetEntriesRequest _pendingEntries;
        private SwitchPetEntryPresetRequest _pendingPresetSwitch;
        private ulong _entriesPlayerId;
        public const uint KeepPresetTag = 0xFFFFFFFFu;
        public bool HasPendingEntries(ulong petId) => _pendingEntries?.PetId == petId && _entriesPlayerId == MyPlayerId;
        public bool HasPendingPresetSwitch(ulong petId) => _pendingPresetSwitch?.PetId == petId && _entriesPlayerId == MyPlayerId;

        public static string DescribePresetTag(uint tag) => tag switch
        {
            1 => "输出",
            2 => "控场",
            3 => "辅助",
            _ => "默认",
        };

        public void RequestEntries(ulong petId)
        {
            if (!IsReady || petId == 0) return;
            int generation = _entriesGeneration; ulong owner = MyPlayerId;
            _net.Call(MessageIds.GetPetEntries, new GetPetEntriesRequest { PetId = petId }, GetPetEntriesResponse.Parser,
                response => {
                    if (generation != _entriesGeneration || owner != MyPlayerId) return;
                    if (HasTip(response.ErrorMessage)) { OnError?.Invoke(DescribeTip("刷新条目失败", response.ErrorMessage)); return; }
                    PublishEntries(response.Entries, petId);
                }, error => { if (generation == _entriesGeneration && owner == MyPlayerId) OnError?.Invoke(error); });
        }

        public void PreviewEntries(ulong petId, ulong stateVersion, uint configVersion, IReadOnlyDictionary<uint, uint> target,
            Action<PetEntriesInfo, string> completed)
        {
            if (!IsReady) { completed?.Invoke(null, "连接未就绪"); return; }
            int generation = _entriesGeneration; ulong owner = MyPlayerId;
            // 仅发送非零项保持紧凑载荷，省略的已知方向由服务端 ReplaceKnown 按 0 覆盖。
            var request = new PreviewPetEntriesRequest { PetId = petId, StateVersion = stateVersion, ConfigVersion = configVersion };
            if (target != null) foreach (var pair in target) if (pair.Value > 0) request.Target[pair.Key] = pair.Value;
            _net.Call(MessageIds.PreviewPetEntries, request, PreviewPetEntriesResponse.Parser,
                response => {
                    if (generation != _entriesGeneration || owner != MyPlayerId) return;
                    bool rejected = HasTip(response.ErrorMessage);
                    if (rejected) PublishEntries(response.Entries, petId);
                    completed?.Invoke(!rejected && response.Entries?.PetId == petId ? response.Entries : null,
                        rejected ? "条目规则或状态已更新，请恢复当前分配后重试。 " + DescribeTip("", response.ErrorMessage) : null);
                }, error => { if (generation == _entriesGeneration && owner == MyPlayerId) completed?.Invoke(null, error); });
        }

        public void SaveEntries(ulong petId, ulong stateVersion, uint configVersion, IReadOnlyDictionary<uint, uint> target,
            Action<bool, PetEntriesInfo, string> completed)
        {
            if (_pendingEntries != null && _entriesPlayerId != MyPlayerId) _pendingEntries = null;
            if (_pendingEntries != null && _pendingEntries.PetId != petId) { completed?.Invoke(false, null, "请先确认另一只宝宝的上次条目保存结果。"); return; }
            if (_pendingEntries == null && (target == null || petId == 0 || stateVersion == 0 || configVersion == 0)) { completed?.Invoke(false, null, "条目规则尚未同步。"); return; }
            if (!BeginWrite(null)) { completed?.Invoke(false, null, CultivationWritesBlocked ? CultivationLockedMessage : "当前无法保存条目。"); return; }
            if (_pendingEntries == null)
            {
                _pendingEntries = new SavePetEntriesRequest { PetId = petId, StateVersion = stateVersion, ConfigVersion = configVersion, RequestId = Guid.NewGuid().ToString("N") };
                foreach (var pair in target) if (pair.Value > 0) _pendingEntries.Target[pair.Key] = pair.Value;
                _entriesPlayerId = MyPlayerId;
            }
            // 超时只能原样重试；不能把改过的草稿套用旧 request_id。
            ++_listRequestVersion; ListRequestPending = false;
            var sent = _pendingEntries.Clone(); int generation = ++_entriesGeneration; ulong owner = MyPlayerId;
            _net.Call(MessageIds.SavePetEntries, sent, SavePetEntriesResponse.Parser,
                response => {
                    if (generation != _entriesGeneration || owner != MyPlayerId) return;
                    bool rejected = HasTip(response.ErrorMessage);
                    bool exact = !rejected && response.RequestId == sent.RequestId && response.Entries?.PetId == petId &&
                        response.Entries.StateVersion == sent.StateVersion + 1 && response.Entries.LastRequestId == sent.RequestId &&
                        response.Entries.Entries.Count > 0 &&
                        response.Entries.Entries.All(e => e.Allocated == (sent.Target.TryGetValue(e.Id, out var v) ? v : 0)) &&
                        sent.Target.All(pair => pair.Value == 0 || response.Entries.Entries.Any(e => e.Id == pair.Key && e.Allocated == pair.Value));
                    if (rejected || exact) _pendingEntries = null;
                    SetBusy(false);
                    string error = exact ? null : rejected ? "保存被拒绝：状态或规则已变化，请恢复当前分配后重试。 " + DescribeTip("", response.ErrorMessage) : "保存结果尚未确认，请重试同一请求。";
                    // 只有本请求的明确成功可提交草稿；随后再广播权威刷新。
                    completed?.Invoke(exact, response.Entries, error);
                    PublishEntries(response.Entries, petId);
                }, error => {
                    if (generation != _entriesGeneration || owner != MyPlayerId) return;
                    SetBusy(false); completed?.Invoke(false, null, "保存结果未知，可重试确认。 " + error);
                });
        }

        /// <summary>切换宝宝条目预设方案（1..3）或更新其用途标签（0默认/1输出/2控场/3辅助，KeepPresetTag 保持原标签）。</summary>
        public void SwitchEntryPreset(ulong petId, uint slotId, uint tag, ulong stateVersion, uint configVersion,
            Action<bool, PetEntriesInfo, string> completed)
        {
            if (_pendingPresetSwitch != null && _entriesPlayerId != MyPlayerId) _pendingPresetSwitch = null;
            if (_pendingEntries != null && _entriesPlayerId == MyPlayerId)
            {
                completed?.Invoke(false, null, "请先确认未完成的条目保存结果。");
                return;
            }
            if (_pendingPresetSwitch != null && _pendingPresetSwitch.PetId != petId)
            {
                completed?.Invoke(false, null, "请先确认另一只宝宝的方案切换结果。");
                return;
            }
            if (_pendingPresetSwitch == null && (petId == 0 || slotId < 1 || slotId > 3 || stateVersion == 0 || configVersion == 0))
            {
                completed?.Invoke(false, null, "无效的宝宝条目方案或规则尚未同步。");
                return;
            }
            if (!BeginWrite(null))
            {
                completed?.Invoke(false, null, CultivationWritesBlocked ? CultivationLockedMessage : "当前无法切换条目方案。");
                return;
            }
            if (_pendingPresetSwitch == null)
            {
                _pendingPresetSwitch = new SwitchPetEntryPresetRequest
                {
                    PetId = petId,
                    SlotId = slotId,
                    Tag = tag,
                    StateVersion = stateVersion,
                    ConfigVersion = configVersion,
                    RequestId = Guid.NewGuid().ToString("N"),
                };
                _entriesPlayerId = MyPlayerId;
            }
            ++_listRequestVersion; ListRequestPending = false;
            var sent = _pendingPresetSwitch.Clone(); int generation = ++_entriesGeneration; ulong owner = MyPlayerId;
            _net.Call(MessageIds.SwitchPetEntryPreset, sent, SwitchPetEntryPresetResponse.Parser,
                response =>
                {
                    if (generation != _entriesGeneration || owner != MyPlayerId) return;
                    bool rejected = HasTip(response.ErrorMessage);
                    bool exact = !rejected && response.RequestId == sent.RequestId && response.Entries?.PetId == petId &&
                        response.Entries.StateVersion == sent.StateVersion + 1 &&
                        response.Entries.ActivePresetSlot == sent.SlotId &&
                        response.Entries.LastRequestId == sent.RequestId;
                    if (rejected || exact) _pendingPresetSwitch = null;
                    SetBusy(false);
                    string error = exact ? null : rejected
                        ? DescribeTip("切换条目方案失败", response.ErrorMessage)
                        : "切换方案结果尚未确认，请重试同一请求。";
                    completed?.Invoke(exact, response.Entries, error);
                    PublishEntries(response.Entries, petId);
                },
                error =>
                {
                    if (generation != _entriesGeneration || owner != MyPlayerId) return;
                    SetBusy(false);
                    completed?.Invoke(false, null, "切换方案结果未知，可重试确认。" + error);
                });
        }

        private void PublishEntries(PetEntriesInfo info, ulong petId)
        {
            if (info?.PetId != petId || info.ConfigVersion == 0 || info.StateVersion == 0 || info.Entries.Count == 0 || Pets == null) return;
            var pet = Pets.Pets.FirstOrDefault(p => p.PetId == petId);
            if (pet == null || (pet.Entries != null && (pet.Entries.StateVersion > info.StateVersion || pet.Entries.ConfigVersion > info.ConfigVersion))) return;
            pet.Entries = info.Clone(); OnList?.Invoke(Pets);
        }
        private void InvalidateEntries() { ++_entriesGeneration; _pendingPresetSwitch = null; }
    }
}
