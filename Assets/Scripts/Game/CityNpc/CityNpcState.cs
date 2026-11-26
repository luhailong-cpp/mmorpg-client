using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MmorpgClient.Game.CityNpc
{
    public enum CityNpcErrandStatus
    {
        Available = 0,
        Active = 1,
        ReadyToArchive = 2,
        Archived = 3
    }

    /// <summary>可选的本地轻量存档缝（纯剧情见闻/木桩纪录/同行预设短语，零敏感信息、脱离 UnityEngine 便于单测）。</summary>
    public interface ICityNpcStateStorage
    {
        string Load(ulong playerId);
        void Save(ulong playerId, string payload);
    }

    /// <summary>归元城原创 NPC 玩法纯 C# 会话状态机（脱离 UnityEngine，可独立单测；按 PlayerId 隔离，断线/换号即重置或换档）。</summary>
    public sealed class CityNpcState
    {
        private readonly ICityNpcStateStorage _storage;
        private ulong _playerId;
        private readonly HashSet<uint> _clearedPuzzles = new();
        private readonly HashSet<uint> _clearedTales = new();
        private readonly Dictionary<string, CityNpcErrandStatus> _errandStatus = new(StringComparer.Ordinal);
        private readonly Dictionary<uint, PracticeDummyRecord> _dummyRecords = new();
        private WuxingFortuneResult? _lastFortune;
        private double _fortuneOverheadExpiresAtSec;
        private int _selectedCompanionOathIndex;
        private string _lastBannerNotice = "";

        public CityNpcState(ICityNpcStateStorage storage = null)
        {
            _storage = storage;
        }

        public ulong PlayerId => _playerId;
        public WuxingFortuneResult? LastFortune => _lastFortune;
        public int SelectedCompanionOathIndex => _selectedCompanionOathIndex;
        public string SelectedCompanionOathPhrase =>
            CityNpcCatalog.CompanionOathPresets.Count == 0
                ? ""
                : CityNpcCatalog.CompanionOathPresets[_selectedCompanionOathIndex % CityNpcCatalog.CompanionOathPresets.Count];
        public string LastBannerNotice => _lastBannerNotice;

        public int ClearedPuzzleCount => _clearedPuzzles.Count;
        public int ClearedTaleCount => _clearedTales.Count;

        public int ArchivedErrandCount
        {
            get
            {
                int count = 0;
                foreach (var kv in _errandStatus)
                    if (kv.Value == CityNpcErrandStatus.Archived) ++count;
                return count;
            }
        }

        public event Action Changed;
        public event Action<string> ToastRequested;

        public void BindPlayer(ulong playerId)
        {
            if (_playerId == playerId) return;
            _playerId = playerId;
            _clearedPuzzles.Clear();
            _clearedTales.Clear();
            _errandStatus.Clear();
            _dummyRecords.Clear();
            _lastFortune = null;
            _fortuneOverheadExpiresAtSec = 0d;
            _selectedCompanionOathIndex = 0;
            _lastBannerNotice = "";
            if (_playerId != 0ul && _storage != null)
            {
                Deserialize(_storage.Load(_playerId));
            }
            Changed?.Invoke();
        }

        public void Reset() => BindPlayer(0ul);

        public bool IsPuzzleCleared(uint dungeonConfigId) => _clearedPuzzles.Contains(dungeonConfigId);
        public bool IsTaleCleared(uint dungeonConfigId) => _clearedTales.Contains(dungeonConfigId);

        public bool TryGetDummyRecord(uint dungeonConfigId, out PracticeDummyRecord record) =>
            _dummyRecords.TryGetValue(dungeonConfigId, out record);

        public CityNpcErrandStatus GetErrandStatus(string errandId)
        {
            if (string.IsNullOrEmpty(errandId)) return CityNpcErrandStatus.Available;
            return _errandStatus.TryGetValue(errandId, out var status) ? status : CityNpcErrandStatus.Available;
        }

        public bool AcceptErrand(string errandId)
        {
            var def = CityNpcCatalog.FindErrand(errandId);
            if (def == null) return false;
            var current = GetErrandStatus(errandId);
            if (current != CityNpcErrandStatus.Available) return false;
            _errandStatus[errandId] = CityNpcErrandStatus.Active;
            _lastBannerNotice = $"已接取顺路捎物「{def.Title}」，下次前往{def.TargetSceneName}自动送达。";
            Persist();
            ToastRequested?.Invoke(_lastBannerNotice);
            Changed?.Invoke();
            return true;
        }

        /// <summary>监听服务端权威入场场景：已接取的顺路捎物抵达目标野外图后自动记为「已送达」。</summary>
        public bool ObserveAuthoritativeScene(uint sceneConfigId)
        {
            if (sceneConfigId <= 1u) return false;
            bool updated = false;
            for (int i = 0; i < CityNpcCatalog.Errands.Count; ++i)
            {
                var def = CityNpcCatalog.Errands[i];
                if (def.TargetSceneConfigId == sceneConfigId && GetErrandStatus(def.Id) == CityNpcErrandStatus.Active)
                {
                    _errandStatus[def.Id] = CityNpcErrandStatus.ReadyToArchive;
                    _lastBannerNotice = $"顺路捎物「{def.Title}」已送达{def.TargetSceneName}！回归元城找周行舟即可归档见闻。";
                    ToastRequested?.Invoke(_lastBannerNotice);
                    updated = true;
                }
            }
            if (updated)
            {
                Persist();
                Changed?.Invoke();
            }
            return updated;
        }

        public bool ArchiveErrand(string errandId)
        {
            var def = CityNpcCatalog.FindErrand(errandId);
            if (def == null || GetErrandStatus(errandId) != CityNpcErrandStatus.ReadyToArchive) return false;
            _errandStatus[errandId] = CityNpcErrandStatus.Archived;
            _lastBannerNotice = $"见闻归档：{def.CompletionNote}";
            Persist();
            ToastRequested?.Invoke(_lastBannerNotice);
            Changed?.Invoke();
            return true;
        }

        /// <summary>监听服务端权威战果 BattleEndS2C：记录演武木桩实战对比回执，或点亮残局首通与人物奇谈隐藏续篇。</summary>
        public bool ObserveAuthoritativeBattleEnd(global::BattleEndS2C end)
        {
            var s = end?.Settlement;
            if (s == null || s.TotalRounds == 0u) return false;

            var def = CityNpcCatalog.FindEncounter(s.BattleConfigId);
            if (def == null) return false;

            // 演武试招木桩（301 / 302）：不限胜负（8 回合打满或提前撤出均产生有效验招回执），对比上次均伤并更新历史最佳
            if (def.Category == CityNpcEncounterCategory.PracticeDummy)
            {
                ulong avg = s.TotalDamageDealt / s.TotalRounds;
                ulong support = s.TotalHealingDone + s.TotalShieldGranted;
                uint ctrl = s.TotalControlsLanded + s.TotalDispelsLanded;
                bool hasPrev = _dummyRecords.TryGetValue(def.DungeonConfigId, out var prev);
                ulong bestTotal = hasPrev ? Math.Max(prev.BestTotalDamage, s.TotalDamageDealt) : s.TotalDamageDealt;
                ulong bestAvg = hasPrev ? Math.Max(prev.BestAvgDamagePerRound, avg) : avg;
                int deltaPct = 0;
                bool showDelta = hasPrev && prev.AvgDamagePerRound > 0ul;
                if (showDelta)
                {
                    double diff = ((double)avg - prev.AvgDamagePerRound) * 100.0 / prev.AvgDamagePerRound;
                    deltaPct = (int)Math.Round(diff);
                }
                var rec = new PracticeDummyRecord(
                    def.DungeonConfigId,
                    s.TotalRounds,
                    s.TotalDamageDealt,
                    avg,
                    support,
                    ctrl,
                    s.DamageTaken,
                    bestTotal,
                    bestAvg,
                    showDelta,
                    deltaPct);
                _dummyRecords[def.DungeonConfigId] = rec;
                _lastBannerNotice = $"演武回执（{def.Title}）：{rec.FormatCompactSummary()}";
                Persist();
                ToastRequested?.Invoke(_lastBannerNotice);
                Changed?.Invoke();
                return true;
            }

            if (s.Fled) return false;
            bool won = (end.Outcome == global::eBattleOutcome.BattleOutcomeSideAWin && s.PlayerTeamIndex == 0u) ||
                       (end.Outcome == global::eBattleOutcome.BattleOutcomeSideBWin && s.PlayerTeamIndex == 1u);
            if (!won) return false;

            if (def.Category == CityNpcEncounterCategory.TacticalPuzzle && _clearedPuzzles.Add(def.DungeonConfigId))
            {
                _lastBannerNotice = $"残局破阵成功！点亮印记「{def.ClearBadgeName}」。";
                Persist();
                ToastRequested?.Invoke(_lastBannerNotice);
                Changed?.Invoke();
                return true;
            }
            if (def.Category == CityNpcEncounterCategory.CharacterTale && _clearedTales.Add(def.DungeonConfigId))
            {
                _lastBannerNotice = $"奇谈见闻达成！已解锁隐藏续篇「{def.HiddenEpilogueTitle}」。";
                Persist();
                ToastRequested?.Invoke(_lastBannerNotice);
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        public WuxingFortuneResult DrawFortune(ulong serverNowMs, uint tideSceneConfigId, double localMonotonicNowSec)
        {
            var fortune = CityNpcCatalog.DrawWuxingFortune(_playerId, serverNowMs, tideSceneConfigId);
            _lastFortune = fortune;
            _fortuneOverheadExpiresAtSec = localMonotonicNowSec + 1800d; // 30 分钟纯视觉吉签气泡
            _lastBannerNotice = $"求得「{fortune.ElementName} · {fortune.SignGrade}」：{fortune.Verse}";
            ToastRequested?.Invoke(_lastBannerNotice);
            Changed?.Invoke();
            return fortune;
        }

        public string ActiveFortuneOverheadTag(double localMonotonicNowSec)
        {
            if (!_lastFortune.HasValue || localMonotonicNowSec > _fortuneOverheadExpiresAtSec) return "";
            return _lastFortune.Value.OverheadTag;
        }

        public void CycleCompanionOath()
        {
            if (CityNpcCatalog.CompanionOathPresets.Count == 0) return;
            _selectedCompanionOathIndex = (_selectedCompanionOathIndex + 1) % CityNpcCatalog.CompanionOathPresets.Count;
            _lastBannerNotice = $"同行之约已更新：「{SelectedCompanionOathPhrase}」";
            Persist();
            Changed?.Invoke();
        }

        private void Persist()
        {
            if (_storage == null || _playerId == 0ul) return;
            try { _storage.Save(_playerId, Serialize()); }
            catch { /* 本地见闻存档失败绝不阻断主流程 */ }
        }

        internal string Serialize()
        {
            var sb = new StringBuilder(256);
            sb.Append("v=1\n");
            sb.Append("oath=").Append(_selectedCompanionOathIndex.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (_clearedPuzzles.Count > 0)
            {
                sb.Append("puzzles=");
                bool first = true;
                foreach (uint id in _clearedPuzzles)
                {
                    if (!first) sb.Append(',');
                    sb.Append(id.ToString(CultureInfo.InvariantCulture));
                    first = false;
                }
                sb.Append('\n');
            }
            if (_clearedTales.Count > 0)
            {
                sb.Append("tales=");
                bool first = true;
                foreach (uint id in _clearedTales)
                {
                    if (!first) sb.Append(',');
                    sb.Append(id.ToString(CultureInfo.InvariantCulture));
                    first = false;
                }
                sb.Append('\n');
            }
            foreach (var kv in _errandStatus)
            {
                if (kv.Value != CityNpcErrandStatus.Available)
                    sb.Append("errand=").Append(kv.Key).Append(':').Append(((int)kv.Value).ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            foreach (var kv in _dummyRecords)
            {
                var r = kv.Value;
                sb.Append("dummy=")
                  .Append(r.DungeonConfigId.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.Rounds.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.TotalDamage.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.AvgDamagePerRound.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.SupportValue.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.ControlAndDispelCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.DamageTaken.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.BestTotalDamage.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.BestAvgDamagePerRound.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            return sb.ToString();
        }

        internal void Deserialize(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return;
            var lines = payload.Split('\n');
            for (int i = 0; i < lines.Length; ++i)
            {
                string line = lines[i].Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq);
                string val = line.Substring(eq + 1);
                switch (key)
                {
                    case "oath":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oathIdx) && oathIdx >= 0)
                            _selectedCompanionOathIndex = oathIdx;
                        break;
                    case "puzzles":
                        ParseUintSet(val, _clearedPuzzles);
                        break;
                    case "tales":
                        ParseUintSet(val, _clearedTales);
                        break;
                    case "errand":
                        int colon = val.IndexOf(':');
                        if (colon > 0 && int.TryParse(val.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int stInt)
                            && Enum.IsDefined(typeof(CityNpcErrandStatus), stInt))
                        {
                            string eid = val.Substring(0, colon);
                            if (CityNpcCatalog.FindErrand(eid) != null)
                                _errandStatus[eid] = (CityNpcErrandStatus)stInt;
                        }
                        break;
                    case "dummy":
                        var parts = val.Split(',');
                        if (parts.Length >= 9
                            && uint.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint did)
                            && uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint rounds)
                            && ulong.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong totalDmg)
                            && ulong.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong avgDmg)
                            && ulong.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong support)
                            && uint.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint ctrl)
                            && ulong.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong taken)
                            && ulong.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong bestTotal)
                            && ulong.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong bestAvg))
                        {
                            _dummyRecords[did] = new PracticeDummyRecord(did, rounds, totalDmg, avgDmg, support, ctrl, taken, bestTotal, bestAvg, false, 0);
                        }
                        break;
                }
            }
        }

        private static void ParseUintSet(string csv, HashSet<uint> target)
        {
            if (string.IsNullOrEmpty(csv)) return;
            var parts = csv.Split(',');
            for (int i = 0; i < parts.Length; ++i)
            {
                if (uint.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) && id > 0u)
                    target.Add(id);
            }
        }
    }
}
