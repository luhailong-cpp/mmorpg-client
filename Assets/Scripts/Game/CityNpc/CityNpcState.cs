using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.CityNpc
{
    public enum CityNpcErrandStatus
    {
        Available = 0,
        Active = 1,
        ReadyToArchive = 2,
        Archived = 3
    }

    /// <summary>归元城原创 NPC 玩法纯 C# 会话状态机（脱离 UnityEngine，可独立单测；按 PlayerId 隔离，断线/换号即重置）。</summary>
    public sealed class CityNpcState
    {
        private ulong _playerId;
        private readonly HashSet<uint> _clearedPuzzles = new();
        private readonly HashSet<uint> _clearedTales = new();
        private readonly Dictionary<string, CityNpcErrandStatus> _errandStatus = new(StringComparer.Ordinal);
        private WuxingFortuneResult? _lastFortune;
        private double _fortuneOverheadExpiresAtSec;
        private int _selectedCompanionOathIndex;
        private string _lastBannerNotice = "";

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
            _lastFortune = null;
            _fortuneOverheadExpiresAtSec = 0d;
            _selectedCompanionOathIndex = 0;
            _lastBannerNotice = "";
            Changed?.Invoke();
        }

        public void Reset() => BindPlayer(0ul);

        public bool IsPuzzleCleared(uint dungeonConfigId) => _clearedPuzzles.Contains(dungeonConfigId);
        public bool IsTaleCleared(uint dungeonConfigId) => _clearedTales.Contains(dungeonConfigId);

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
            if (updated) Changed?.Invoke();
            return updated;
        }

        public bool ArchiveErrand(string errandId)
        {
            var def = CityNpcCatalog.FindErrand(errandId);
            if (def == null || GetErrandStatus(errandId) != CityNpcErrandStatus.ReadyToArchive) return false;
            _errandStatus[errandId] = CityNpcErrandStatus.Archived;
            _lastBannerNotice = $"见闻归档：{def.CompletionNote}";
            ToastRequested?.Invoke(_lastBannerNotice);
            Changed?.Invoke();
            return true;
        }

        /// <summary>监听服务端权威战果 BattleEndS2C：点亮残局首通或解锁人物奇谈隐藏续篇。</summary>
        public bool ObserveAuthoritativeBattleEnd(global::BattleEndS2C end)
        {
            var s = end?.Settlement;
            if (s == null || s.Fled || s.TotalRounds == 0u) return false;
            bool won = (end.Outcome == global::eBattleOutcome.BattleOutcomeSideAWin && s.PlayerTeamIndex == 0u) ||
                       (end.Outcome == global::eBattleOutcome.BattleOutcomeSideBWin && s.PlayerTeamIndex == 1u);
            if (!won) return false;

            var def = CityNpcCatalog.FindEncounter(s.BattleConfigId);
            if (def == null) return false;

            if (def.Category == CityNpcEncounterCategory.TacticalPuzzle && _clearedPuzzles.Add(def.DungeonConfigId))
            {
                _lastBannerNotice = $"残局破阵成功！点亮印记「{def.ClearBadgeName}」。";
                ToastRequested?.Invoke(_lastBannerNotice);
                Changed?.Invoke();
                return true;
            }
            if (def.Category == CityNpcEncounterCategory.CharacterTale && _clearedTales.Add(def.DungeonConfigId))
            {
                _lastBannerNotice = $"奇谈见闻达成！已解锁隐藏续篇「{def.HiddenEpilogueTitle}」。";
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
            Changed?.Invoke();
        }
    }
}
