using System;
using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Game.Season;
using MmorpgClient.Game.TrialJournal;

namespace MmorpgClient.Game.CoreLoop
{
    /// <summary>120 秒等待兜底：取消当前排队并自动切往单人首领试炼的状态机。</summary>
    public enum QueueFallbackSwitchState
    {
        Idle = 0,
        Cancelling = 1,
        SwitchedToBossTrial = 2,
        ReturnedToMatchedBattle = 3,
    }

    /// <summary>
    /// 《五行奇谈》核心玩法 V1 客户端纯 C# 协调门面（零 UnityEngine 依赖，WebGL2/IL2CPP 安全）。
    /// 面向 UI 层统一收口：首领试炼（3 只首领×普通/困难）、正式自养人宠会武（动态开放规模）、
    /// 首季轮换娱乐场（护卫救援 109 / 物法易势 5V5 110 免费预设）、120 秒等待取消转单人试炼状态机，
    /// 以及主城/战斗/结算只读视图生成。
    /// </summary>
    public sealed class CoreGameplayLoopClient : IDisposable
    {
        private readonly BattleClient _battle;
        private readonly BattleMatchEntryClient _matchEntry;
        private readonly RankedClient _ranked;
        private readonly TrialJournalClient _trialJournal;
        private readonly SeasonClient _season;
        private readonly PlayerFeaturesClient _features;

        private bool _disposed;
        private uint _pendingFallbackDungeonId;

        public QueueFallbackSwitchState FallbackSwitchState { get; private set; } = QueueFallbackSwitchState.Idle;
        public string Status { get; private set; } = "";

        public event Action Changed;
        public event Action<string> Notice;

        public CoreGameplayLoopClient(
            BattleClient battle,
            BattleMatchEntryClient matchEntry,
            RankedClient ranked = null,
            TrialJournalClient trialJournal = null,
            SeasonClient season = null,
            PlayerFeaturesClient features = null)
        {
            _battle = battle ?? throw new ArgumentNullException(nameof(battle));
            _matchEntry = matchEntry ?? throw new ArgumentNullException(nameof(matchEntry));
            _ranked = ranked;
            _trialJournal = trialJournal;
            _season = season;
            _features = features;
            _battle.OnPhaseChanged += HandleBattlePhaseChanged;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _battle.OnPhaseChanged -= HandleBattlePhaseChanged;
            _pendingFallbackDungeonId = 0u;
            FallbackSwitchState = QueueFallbackSwitchState.Idle;
        }

        /// <summary>发起普通/困难首领试炼（Dungeon 101–108）；困难难度先做本地解锁提示，服务端始终最终裁决。</summary>
        public bool EnterBossTrial(uint dungeonConfigId)
        {
            if (_disposed) return false;
            ClearFallbackState();
            if (CoreGameplayLoopModels.IsHardBossTrialDungeon(dungeonConfigId)
                && _trialJournal?.Snapshot != null
                && !_trialJournal.Snapshot.HardBossTrialUnlocked)
            {
                PublishStatus(CoreGameplayLoopModels.HardLockDefaultHint);
                return false;
            }
            bool ok = _matchEntry.StartBossTrial(dungeonConfigId);
            if (!string.IsNullOrEmpty(_matchEntry.Status)) PublishStatus(_matchEntry.Status);
            return ok;
        }

        /// <summary>发起正式人宠会武（使用本人真实养成，不走免费人宠预设，人数档以服务端开放开关为准）。</summary>
        public bool EnterFormalPvp(uint targetSize)
        {
            if (_disposed) return false;
            ClearFallbackState();
            var profile = _ranked?.Profile;
            IReadOnlyList<uint> openSizes = profile?.AllowedTeamSizes ?? new[] { 1u, 3u };
            uint ruleVersion = profile?.RuleVersion ?? 1u;
            uint configId = profile?.BattleConfigId ?? RankedFormatting.DefaultBattleConfigId;
            bool ok = _matchEntry.StartRankedPvp(
                targetSize,
                characterPresetId: 0u,
                petPresetId: 0u,
                rankedRuleVersion: ruleVersion,
                battleConfigId: configId,
                openTeamSizes: openSizes,
                requireTeamPresetsConfirmed: false);
            if (ok && _ranked != null) _ranked.SelectedTeamSize = targetSize;
            if (!string.IsNullOrEmpty(_matchEntry.Status)) PublishStatus(_matchEntry.Status);
            return ok;
        }

        /// <summary>发起娱乐轮换「护卫救援」（Dungeon 109，免费 3×3 人宠，支持 1–5 人，不计正式评分）。</summary>
        public bool EnterGuardRescue(uint characterPresetId = 0u, uint petPresetId = 0u)
        {
            if (_disposed) return false;
            ClearFallbackState();
            uint charPreset = characterPresetId != 0 ? characterPresetId : (_ranked?.SelectedCharacterPresetId ?? RankedFormatting.DefaultCharacterPresetId);
            uint petPreset = petPresetId != 0 ? petPresetId : (_ranked?.SelectedPetPresetId ?? RankedFormatting.DefaultPetPresetId);
            bool ok = _matchEntry.StartBossTrial(CoreGameplayLoopModels.GuardRescueDungeonId, charPreset, petPreset);
            if (!string.IsNullOrEmpty(_matchEntry.Status)) PublishStatus(_matchEntry.Status);
            return ok;
        }

        /// <summary>发起娱乐轮换「物法易势 5V5」（Dungeon 110，仅 5V5，免费 3×3 人宠，不计正式评分）。</summary>
        public bool EnterPolarity5v5(uint characterPresetId = 0u, uint petPresetId = 0u)
        {
            if (_disposed) return false;
            ClearFallbackState();
            uint charPreset = characterPresetId != 0 ? characterPresetId : (_ranked?.SelectedCharacterPresetId ?? RankedFormatting.DefaultCharacterPresetId);
            uint petPreset = petPresetId != 0 ? petPresetId : (_ranked?.SelectedPetPresetId ?? RankedFormatting.DefaultPetPresetId);
            bool ok = _matchEntry.StartPolarity5v5(CoreGameplayLoopModels.Polarity5v5DungeonId, charPreset, petPreset);
            if (!string.IsNullOrEmpty(_matchEntry.Status)) PublishStatus(_matchEntry.Status);
            return ok;
        }

        /// <summary>
        /// 120 秒等待兜底流：正在取消 -> 已取消后自动进单人首领试炼；
        /// 若取消中途已经成局进入 Preparing/WaitingAction，则立即取消切换并回到原局。
        /// </summary>
        public bool CancelQueueAndSwitchToBossTrial(uint fallbackDungeonConfigId = CoreGameplayLoopModels.Boss1RejuvenateNormalDungeonId)
        {
            if (_disposed) return false;
            if (fallbackDungeonConfigId == 0) fallbackDungeonConfigId = CoreGameplayLoopModels.Boss1RejuvenateNormalDungeonId;

            if (_battle.Phase == BattlePhase.None)
            {
                bool started = EnterBossTrial(fallbackDungeonConfigId);
                if (started)
                {
                    FallbackSwitchState = QueueFallbackSwitchState.SwitchedToBossTrial;
                    Changed?.Invoke();
                }
                return started;
            }

            if (_battle.Phase != BattlePhase.Queued)
            {
                FallbackSwitchState = QueueFallbackSwitchState.ReturnedToMatchedBattle;
                PublishStatus("匹配已成局，正在进入原定对局…");
                return false;
            }

            _pendingFallbackDungeonId = fallbackDungeonConfigId;
            FallbackSwitchState = QueueFallbackSwitchState.Cancelling;
            PublishStatus("正在取消当前匹配，确认后将自动进入首领试炼…");
            if (!_matchEntry.Cancel())
            {
                _pendingFallbackDungeonId = 0u;
                FallbackSwitchState = QueueFallbackSwitchState.Idle;
                PublishStatus("取消操作冷却中，请稍候再试。");
                return false;
            }
            // 若本机立即退出到 None（例如尚未发出 JoinQueue），HandleBattlePhaseChanged 已同步完成切换。
            return true;
        }

        public IReadOnlyList<BossTrialCardView> BuildBossTrials() =>
            CoreGameplayLoopModels.BuildBossTrialCards(_trialJournal?.Snapshot);

        public IReadOnlyList<BossTrialCardView> BuildBossTrialsV2() =>
            CoreGameplayLoopModels.BuildBossTrialCardsV2(_trialJournal?.Snapshot);

        public IReadOnlyList<ulong> CollectMyQuickParryActorIds() =>
            CoreGameplayLoopModels.CollectQuickParryDefendActorIds(_battle.MyPlayerId, _battle.State);

        public IReadOnlyList<BossTrialHighlightBadge> BuildLastBossTrialHighlights() =>
            CoreGameplayLoopModels.BuildBossTrialHighlightBadges(_battle.LastBattleEnd?.Settlement);

        public BattleTacticalReviewView BuildLastTacticalReview() =>
            CoreGameplayLoopModels.BuildTacticalReview(_battle.LastBattleEnd);

        public IReadOnlyList<PvpScaleSlotView> BuildFormalPvpScales() =>
            CoreGameplayLoopModels.BuildFormalPvpScales(_ranked?.Profile, _matchEntry.CurrentSize);

        public IReadOnlyList<RotationCardView> BuildRotations(uint activeSeasonWeek = 1u) =>
            CoreGameplayLoopModels.BuildRotationCards(activeSeasonWeek);

        public BattleCoreHudView BuildBattleHud() =>
            CoreGameplayLoopModels.BuildBattleCoreHud(_battle.State);

        /// <summary>返回本周首领试炼周常五行词缀预览（优先锚定服务端材料周历 WeekStartsAtMs，战中以权威 BattleStateS2C 为准）。</summary>
        public BossTrialWeeklyAffixView BuildCurrentBossTrialWeeklyAffix(ulong? nowMsOverride = null)
        {
            ulong nowMs = nowMsOverride ?? (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var reward = _features?.EquipmentMaterialReward;
            ulong weekStart = reward != null ? reward.WeekStartsAtMs : 0ul;
            ulong nextReset = reward != null ? reward.NextWeekResetAtMs : 0ul;
            return BossTrialWeeklyAffixCatalog.ForTimestamp(nowMs, weekStart, nextReset);
        }

        public CoreSettlementRewardView BuildLastSettlementReward() =>
            CoreGameplayLoopModels.BuildSettlementRewardView(_battle.LastBattleEnd, _features?.EquipmentMaterialReward);

        public SettlementProgressionView BuildLastSettlementProgression() =>
            PlayerProgressionModels.BuildSettlementView(_battle.LastBattleEnd);

        public MainCityHubView BuildMainCityHub(
            bool paymentEnabled = false,
            bool hasAfkPass = false,
            uint unlockedLoadoutSlots = 2u) =>
            CoreGameplayLoopModels.BuildMainCityHub(
                _features?.EquipmentMaterialReward,
                _ranked?.Profile,
                _season?.Snapshot,
                _trialJournal?.Snapshot,
                paymentEnabled,
                hasAfkPass,
                unlockedLoadoutSlots);

        private void HandleBattlePhaseChanged(BattlePhase phase)
        {
            if (_disposed || _pendingFallbackDungeonId == 0u) return;

            if (phase == BattlePhase.None)
            {
                uint targetDungeon = _pendingFallbackDungeonId;
                _pendingFallbackDungeonId = 0u;
                if (_matchEntry.StartBossTrial(targetDungeon))
                {
                    FallbackSwitchState = QueueFallbackSwitchState.SwitchedToBossTrial;
                    PublishStatus("已取消排队，正在进入首领试炼…");
                }
                else
                {
                    FallbackSwitchState = QueueFallbackSwitchState.Idle;
                    PublishStatus(string.IsNullOrEmpty(_matchEntry.Status) ? "已取消排队。" : _matchEntry.Status);
                }
                return;
            }

            if (phase == BattlePhase.Preparing || phase == BattlePhase.WaitingAction || phase == BattlePhase.Resolving)
            {
                _pendingFallbackDungeonId = 0u;
                FallbackSwitchState = QueueFallbackSwitchState.ReturnedToMatchedBattle;
                PublishStatus("匹配已成局，正在返回原定对局…");
            }
        }

        private void ClearFallbackState()
        {
            _pendingFallbackDungeonId = 0u;
            if (FallbackSwitchState != QueueFallbackSwitchState.Idle)
            {
                FallbackSwitchState = QueueFallbackSwitchState.Idle;
                Changed?.Invoke();
            }
        }

        private void PublishStatus(string message)
        {
            Status = message ?? "";
            if (Status.Length != 0) Notice?.Invoke(Status);
            Changed?.Invoke();
        }
    }
}
