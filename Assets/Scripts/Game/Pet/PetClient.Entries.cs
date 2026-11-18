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
        private ulong _entriesPlayerId;
        public bool HasPendingEntries(ulong petId) => _pendingEntries?.PetId == petId && _entriesPlayerId == MyPlayerId;

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
            var request = new PreviewPetEntriesRequest { PetId = petId, StateVersion = stateVersion, ConfigVersion = configVersion };
            foreach (var pair in target) request.Target[pair.Key] = pair.Value;
            _net.Call(MessageIds.PreviewPetEntries, request, PreviewPetEntriesResponse.Parser,
                response => {
                    if (generation != _entriesGeneration || owner != MyPlayerId) return;
                    completed?.Invoke(!HasTip(response.ErrorMessage) && response.Entries?.PetId == petId ? response.Entries : null,
                        HasTip(response.ErrorMessage) ? "条目规则或状态已更新，请刷新后重新分配。" : null);
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
                if (target == null || petId == 0 || stateVersion == 0 || configVersion == 0) { completed?.Invoke(false, null, "条目规则尚未同步。"); return; }
                _pendingEntries = new SavePetEntriesRequest { PetId = petId, StateVersion = stateVersion, ConfigVersion = configVersion, RequestId = Guid.NewGuid().ToString("N") };
                foreach (var pair in target) _pendingEntries.Target[pair.Key] = pair.Value;
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

        private void PublishEntries(PetEntriesInfo info, ulong petId)
        {
            if (info?.PetId != petId || info.ConfigVersion == 0 || info.StateVersion == 0 || info.Entries.Count == 0 || Pets == null) return;
            var pet = Pets.Pets.FirstOrDefault(p => p.PetId == petId);
            if (pet == null || (pet.Entries != null && (pet.Entries.StateVersion > info.StateVersion || pet.Entries.ConfigVersion > info.ConfigVersion))) return;
            pet.Entries = info.Clone(); OnList?.Invoke(Pets);
        }
        private void InvalidateEntries() { ++_entriesGeneration; }
    }
}
