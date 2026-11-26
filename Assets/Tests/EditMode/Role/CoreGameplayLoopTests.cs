using System;
using System.Collections.Generic;
using Google.Protobuf;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Game.Season;
using MmorpgClient.Game.Team;
using MmorpgClient.Game.TrialJournal;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class CoreGameplayLoopTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public sealed class SentCall
            {
                public uint MessageId;
                public IMessage Request;
                public Action<IMessage> Reply;
                public Action<string> Fail;
            }

            public ulong PlayerId { get; set; } = 3001UL;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;
            public readonly List<SentCall> Calls = new();
            private readonly Dictionary<uint, Action<MessageContent>> _notifies = new();

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) => _notifies[messageId] = handler;
            public void SendOneWay(uint messageId, IMessage request) { }

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add(new SentCall
                {
                    MessageId = messageId,
                    Request = request,
                    Reply = msg => onResponse((TResp)msg),
                    Fail = onError,
                });
            }

            public void PushNotify(uint messageId, IMessage payload)
            {
                if (_notifies.TryGetValue(messageId, out var handler))
                {
                    handler(new MessageContent
                    {
                        MessageId = messageId,
                        SerializedMessage = payload.ToByteString(),
                    });
                }
            }

            public void RaiseDisconnected() => Disconnected?.Invoke();
        }

        [Test]
        public void Models_BuildBossTrials_PvpScales_Rotations_BattleHud_Settlement_And_Hub()
        {
            // 1. 首领试炼：3 只首领 4 组普通/困难变体，困难锁严格读 HardBossTrialUnlocked
            var lockedCards = CoreGameplayLoopModels.BuildBossTrialCards(new TrialJournalSnapshot { HardBossTrialUnlocked = false });
            Assert.That(lockedCards.Count, Is.EqualTo(4));
            Assert.That(lockedCards[0].NormalRewardUnits, Is.EqualTo(10u));
            Assert.That(lockedCards[0].HardRewardUnits, Is.EqualTo(15u));
            Assert.That(lockedCards[0].HardUnlocked, Is.False);
            var unlockedCards = CoreGameplayLoopModels.BuildBossTrialCards(new TrialJournalSnapshot { HardBossTrialUnlocked = true });
            Assert.That(unlockedCards[3].HardUnlocked, Is.True);
            Assert.That(unlockedCards[3].HardDungeonId, Is.EqualTo(108u));

            // 2. 正式人宠会武 1V1..5V5：冷启动 [1,3] 开放，5V5 即将开放，2V2/4V4 特殊玩法
            var seasonInfo = new RankedSeasonInfo { SeasonId = 1, UsesOwnProgression = true };
            seasonInfo.AllowedTeamSizes.Add(new[] { 1u, 3u });
            seasonInfo.PrimaryTeamSizes.Add(new[] { 1u, 3u, 5u });
            seasonInfo.SpecialTeamSizes.Add(new[] { 2u, 4u });
            var profile = RankedProfileSnapshot.FromResponse(new GetRankedProfileResponse { CurrentSeason = seasonInfo });
            Assert.That(profile.UsesOwnProgression, Is.True);
            var scales = CoreGameplayLoopModels.BuildFormalPvpScales(profile, currentPartySize: 1u);
            Assert.That(scales.Count, Is.EqualTo(5));
            Assert.That(scales[0].Status, Is.EqualTo(PvpScaleSlotStatus.Open));              // 1V1
            Assert.That(scales[1].Status, Is.EqualTo(PvpScaleSlotStatus.SpecialModeClosed)); // 2V2
            Assert.That(scales[2].Status, Is.EqualTo(PvpScaleSlotStatus.Open));              // 3V3
            Assert.That(scales[3].Status, Is.EqualTo(PvpScaleSlotStatus.SpecialModeClosed)); // 4V4
            Assert.That(scales[4].Status, Is.EqualTo(PvpScaleSlotStatus.PrimaryUpcoming));   // 5V5

            // 3. 轮换娱乐场：护卫救援 (109) + 物法易势仅 5V5 (110) + 权威周次推导
            Assert.That(CoreGameplayLoopModels.TryResolveAuthoritativeSeasonWeek(
                new SeasonStateSnapshot { Available = true, StartsAtMs = 0ul, ServerNowMs = 1790000000000ul }, out _), Is.False);
            const ulong startMs = 1790000000000ul;
            const ulong weekMs = 7ul * 24ul * 3600ul * 1000ul;
            Assert.That(CoreGameplayLoopModels.TryResolveAuthoritativeSeasonWeek(
                new SeasonStateSnapshot { Available = true, StartsAtMs = startMs, ServerNowMs = startMs + 4ul * weekMs + 1000ul }, out uint resolvedWeek), Is.True);
            Assert.That(resolvedWeek, Is.EqualTo(5u));
            var rotations = CoreGameplayLoopModels.BuildRotationCards(activeSeasonWeek: resolvedWeek);
            Assert.That(rotations.Count, Is.EqualTo(2));
            Assert.That(rotations[0].DungeonConfigId, Is.EqualTo(109u));
            Assert.That(rotations[0].UsesFreePreset, Is.True);
            Assert.That(rotations[1].DungeonConfigId, Is.EqualTo(110u));
            Assert.That(rotations[1].FixedTeamSize, Is.EqualTo(5u));
            Assert.That(rotations[1].ActiveNow, Is.True);

            // 4. 战斗 HUD：预告 + 物法易势 + 护卫保全
            var state = new BattleStateS2C
            {
                RoundIndex = 3,
                ActivePolarity = 1,
                NextPolaritySwitchRound = 5,
                ProtectedGuardActorId = 9002UL,
            };
            state.Telegraphs.Add(new BattleTelegraphEntry
            {
                SourceActorId = 9001UL,
                TargetActorId = 3001UL,
                ExecuteRound = 4,
                IntentKind = 1,
                Status = 1,
            });
            var hud = CoreGameplayLoopModels.BuildBattleCoreHud(state);
            Assert.That(hud.HasTelegraphs, Is.True);
            Assert.That(hud.Telegraphs[0].IntentTitle, Is.EqualTo("慢动作重劈"));
            Assert.That(CoreGameplayLoopModels.TelegraphIntentTitle(7u), Is.EqualTo("灵障法术重击"));
            Assert.That(CoreGameplayLoopModels.TelegraphIntentTitle(8u), Is.EqualTo("玄毒群体冲击"));
            Assert.That(hud.HasPolarity, Is.True);
            Assert.That(hud.PolarityBannerText, Does.Contain("物势"));
            Assert.That(hud.HasProtectedGuard, Is.True);
            Assert.That(hud.ProtectedGuardBannerText, Does.Contain("友方护卫"));
            Assert.That(hud.ProtectedGuardBannerText, Does.Not.Contain("敌方"));

            // 5. 结算视图：胜局 15 份（1 可交易 + 14 自用）与 PVP 反挂机 0 份
            var winView = CoreGameplayLoopModels.BuildSettlementRewardView(
                new BattleEndS2C { MaterialCreditUnitsAdded = 15u, MaterialTradableUnitsAdded = 1u },
                new EquipmentMaterialRewardSnapshot { LifetimeEarned = 15u, LifetimeLimit = 660u, AvailableUnits = 15ul });
            Assert.That(winView.BaseUnitsAdded, Is.EqualTo(10u));
            Assert.That(winView.BonusUnitsAdded, Is.EqualTo(5u));
            Assert.That(winView.TradableUnitsAdded, Is.EqualTo(1u));
            Assert.That(winView.BoundUnitsAdded, Is.EqualTo(14u));

            var afkLossView = CoreGameplayLoopModels.BuildSettlementRewardView(
                new BattleEndS2C { MaterialCreditUnitsAdded = 0u, MaterialPvpLossIneligible = true },
                new EquipmentMaterialRewardSnapshot { LifetimeEarned = 15u, LifetimeLimit = 660u });
            Assert.That(afkLossView.PvpLossIneligible, Is.True);
            Assert.That(afkLossView.Headline, Does.Contain("+0"));
            Assert.That(afkLossView.DetailNote, Does.Contain("3 回合"));

            // 6. 主城总览：3 数字 + 4 个零卖数值预留位（含阶段二周上限 150 兜底）
            var hub = CoreGameplayLoopModels.BuildMainCityHub(
                new EquipmentMaterialRewardSnapshot { LifetimeEarned = 120u, LifetimeLimit = 660u, AvailableUnits = 20ul },
                profile,
                new SeasonStateSnapshot { Available = true, ProgressPoints = 30, MaxPoints = 100 },
                new TrialJournalSnapshot(),
                paymentEnabled: false);
            Assert.That(hub.MaterialProgressLabel, Is.EqualTo("永久首套材料 · 账号可交易"));
            Assert.That(hub.MaterialProgressValue, Is.EqualTo("120/660（待领 20）"));
            Assert.That(hub.SmallSpendSlots.Count, Is.EqualTo(4));
            foreach (var slot in hub.SmallSpendSlots)
                Assert.That(slot.ZeroStatGuarantee, Is.True);

            var weeklyHub = CoreGameplayLoopModels.BuildMainCityHub(
                new EquipmentMaterialRewardSnapshot { WeeklyPhaseActive = true, EarnedThisWeek = 45u, AvailableUnits = 15ul },
                profile,
                new SeasonStateSnapshot { Available = true, ProgressPoints = 30, MaxPoints = 100 },
                new TrialJournalSnapshot(),
                paymentEnabled: false);
            Assert.That(weeklyHub.MaterialProgressLabel, Is.EqualTo("周精进材料"));
            Assert.That(weeklyHub.MaterialProgressValue, Is.EqualTo("45/150（待领 15）"));
            var weeklyCappedView = CoreGameplayLoopModels.BuildSettlementRewardView(
                new BattleEndS2C { MaterialCreditUnitsAdded = 0u },
                new EquipmentMaterialRewardSnapshot { WeeklyPhaseActive = true, EarnedThisWeek = 150u });
            Assert.That(weeklyCappedView.WeeklyLimit, Is.EqualTo(150u));
            Assert.That(weeklyCappedView.Headline, Is.EqualTo("本周精进材料额度已达上限（150/150）"));
        }

        [Test]
        public void WeeklySettlementUsesTheActualTradableAndBoundAmounts()
        {
            var ledger = new EquipmentMaterialRewardSnapshot { WeeklyPhaseActive = true, EarnedThisWeek = 45u };
            var view = CoreGameplayLoopModels.BuildSettlementRewardView(
                new BattleEndS2C { MaterialCreditUnitsAdded = 15u, MaterialTradableUnitsAdded = 1u }, ledger);

            Assert.That(view.WeeklyPhaseActive, Is.True);
            Assert.That(view.TradableUnitsAdded, Is.EqualTo(1u));
            Assert.That(view.BoundUnitsAdded, Is.EqualTo(14u));
            Assert.That(view.DetailNote, Does.Contain("1 份可交易、14 份仅自用"));

            var noAward = CoreGameplayLoopModels.BuildSettlementRewardView(new BattleEndS2C(), ledger);
            Assert.That(noAward.Headline, Is.EqualTo("本局无材料额度新增"));
        }

        [Test]
        public void CoreClient_EnforcesOwnProgressionPvp_FreePresetRotations_And_120sCancelFallback()
        {
            var teamNet = new FakeTransport();
            var battleNet = new FakeTransport();
            var rankedNet = new FakeTransport();
            float t = 100f;

            var team = new TeamClient(teamNet, () => teamNet, clock: () => t);
            var battle = new BattleClient(battleNet);
            var ranked = new RankedClient(rankedNet, () => rankedNet, () => t);
            var entry = new BattleMatchEntryClient(team, battle, 101u, 5u, () => t);
            using var core = new CoreGameplayLoopClient(battle, entry, ranked);

            // 初始化无队状态与论剑档案（开放 1V1 / 3V3）
            team.DrainQueued(false);
            teamNet.Calls[0].Reply(new TeamResponse { Team = new TeamView { TeamId = 0 } });
            ranked.RequestProfile(false);
            var season = new RankedSeasonInfo { SeasonId = 1, RuleVersion = 1, BattleConfigId = 5, UsesOwnProgression = true };
            season.AllowedTeamSizes.Add(new[] { 1u, 3u });
            season.PrimaryTeamSizes.Add(new[] { 1u, 3u, 5u });
            season.SpecialTeamSizes.Add(new[] { 2u, 4u });
            rankedNet.Calls[0].Reply(new GetRankedProfileResponse { CurrentSeason = season });
            t += 1f;

            // 未开放的特殊玩法 2V2 被本地清晰拦截
            Assert.That(core.EnterFormalPvp(2u), Is.False);
            Assert.That(core.Status, Does.Contain("特殊玩法"));

            // 正式人宠会武 1V1：使用本人真实养成，不传免费预设 ID
            Assert.That(core.EnterFormalPvp(1u), Is.True);
            var pvpJoin = (JoinQueueRequest)battleNet.Calls[0].Request;
            Assert.That(pvpJoin.Mode, Is.EqualTo(MatchMode.PvpTeam));
            Assert.That(pvpJoin.TeamSize, Is.EqualTo(1u));
            Assert.That(pvpJoin.CharacterPresetId, Is.EqualTo(0u));
            Assert.That(pvpJoin.PetPresetId, Is.EqualTo(0u));
            battleNet.Calls[0].Reply(new JoinQueueResponse { QueueTicket = "t-pvp-1" });

            // 120s 等待取消并转单人首领试炼：CancelQueue 成功后自动发 PveSolo 101
            t += 2f;
            Assert.That(core.CancelQueueAndSwitchToBossTrial(101u), Is.True);
            Assert.That(core.FallbackSwitchState, Is.EqualTo(QueueFallbackSwitchState.Cancelling));
            Assert.That(battleNet.Calls[1].MessageId, Is.EqualTo(MessageIds.CancelQueue));
            battleNet.Calls[1].Reply(new Empty());

            Assert.That(core.FallbackSwitchState, Is.EqualTo(QueueFallbackSwitchState.SwitchedToBossTrial));
            Assert.That(battleNet.Calls.Count, Is.EqualTo(3));
            var bossJoin = (JoinQueueRequest)battleNet.Calls[2].Request;
            Assert.That(bossJoin.Mode, Is.EqualTo(MatchMode.PveSolo));
            Assert.That(bossJoin.BattleConfigId, Is.EqualTo(101u));

            // 若取消中途匹配已成局：收到 NotifyBattleStart 自动回到原局，不再切试炼
            t += 2f;
            Assert.That(core.CancelQueueAndSwitchToBossTrial(103u), Is.True);
            Assert.That(core.FallbackSwitchState, Is.EqualTo(QueueFallbackSwitchState.Cancelling));
            battleNet.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C
            {
                BattleId = 777UL,
                State = new BattleStateS2C { BattleId = 777UL, RoundIndex = 1, Outcome = eBattleOutcome.BattleOutcomeOngoing },
            });
            Assert.That(core.FallbackSwitchState, Is.EqualTo(QueueFallbackSwitchState.ReturnedToMatchedBattle));
            Assert.That(battleNet.Calls.Count, Is.EqualTo(4)); // 未补发 103 试炼

            // 终局后 LastBattleEnd 留存，娱乐物法易势 5V5 透传免费 3×3 预设与 TeamSize=5
            battleNet.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = 777UL,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                MaterialCreditUnitsAdded = 15u,
                MaterialTradableUnitsAdded = 1u,
            });
            Assert.That(battle.LastBattleEnd, Is.Not.Null);
            Assert.That(core.BuildLastSettlementReward().TotalUnitsAdded, Is.EqualTo(15u));

            t += 2f;
            Assert.That(core.EnterPolarity5v5(102u, 201u), Is.True);
            var polarityJoin = (JoinQueueRequest)battleNet.Calls[4].Request;
            Assert.That(polarityJoin.Mode, Is.EqualTo(MatchMode.Polarity5V5));
            Assert.That(polarityJoin.BattleConfigId, Is.EqualTo(110u));
            Assert.That(polarityJoin.TeamSize, Is.EqualTo(5u));
            Assert.That(polarityJoin.CharacterPresetId, Is.EqualTo(102u));
            Assert.That(polarityJoin.PetPresetId, Is.EqualTo(201u));

            // 整队正式会武不再要求队友先确认免费预设
            battleNet.RaiseDisconnected();
            var duoView = new TeamView
            {
                TeamId = 88, LeaderId = 3001UL, Capacity = 5, Version = 2, MembershipEpoch = 1,
                PvpMatchSupported = true, MatchFillSupported = true, RankedPresetsAllConfirmed = false, RankedRuleVersion = 1,
            };
            duoView.Members.Add(new TeamMemberView { PlayerId = 3001UL, IsOnline = true });
            duoView.Members.Add(new TeamMemberView { PlayerId = 3002UL, IsOnline = true });
            team.Refresh();
            teamNet.Calls[1].Reply(new TeamResponse { Team = duoView });
            t += 2f;
            Assert.That(core.EnterFormalPvp(3u), Is.True);

            ranked.Dispose();
            team.Dispose();
        }

        [Test]
        public void BattleClient_RequestState_FiresOnStateUpdatedEvenWhenPhaseUnchanged()
        {
            var battleNet = new FakeTransport { PlayerId = 3001UL };
            var battle = new BattleClient(battleNet);
            int phaseChanges = 0;
            BattleStateS2C updatedState = null;
            battle.OnPhaseChanged += _ => ++phaseChanges;
            battle.OnStateUpdated += s => updatedState = s;

            var initialState = new BattleStateS2C
            {
                BattleId = 888UL,
                RoundIndex = 2,
                Outcome = eBattleOutcome.BattleOutcomeOngoing,
            };
            initialState.PendingActorIds.Add(3001UL);
            battleNet.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C
            {
                BattleId = 888UL,
                State = initialState,
            });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.WaitingAction));

            phaseChanges = 0;
            battle.RequestState();
            Assert.That(battleNet.Calls.Count, Is.EqualTo(1));
            Assert.That(battleNet.Calls[0].MessageId, Is.EqualTo(MessageIds.GetBattleState));

            var pulledState = new BattleStateS2C
            {
                BattleId = 888UL,
                RoundIndex = 3,
                Outcome = eBattleOutcome.BattleOutcomeOngoing,
                ActivePolarity = 2,
                NextPolaritySwitchRound = 5,
                ProtectedGuardActorId = 9002UL,
            };
            pulledState.PendingActorIds.Add(3001UL);
            pulledState.Telegraphs.Add(new BattleTelegraphEntry
            {
                SourceActorId = 9001UL,
                TargetActorId = 3001UL,
                ExecuteRound = 4,
                IntentKind = 7,
                Status = 1,
            });
            battleNet.Calls[0].Reply(pulledState);

            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.WaitingAction));
            Assert.That(phaseChanges, Is.EqualTo(0), "相位未变时不重发 OnPhaseChanged");
            Assert.That(updatedState, Is.SameAs(pulledState), "补拉状态仍必须触发 OnStateUpdated 供战斗叠加 HUD 刷新");
            var hud = CoreGameplayLoopModels.BuildBattleCoreHud(battle.State);
            Assert.That(hud.ActivePolarity, Is.EqualTo(2u));
            Assert.That(hud.Telegraphs[0].IntentTitle, Is.EqualTo("灵障法术重击"));
        }

        [Test]
        public void BossTrialV2_DualGuardCard_QuickParry_Splash_MechanicBanners_And_Highlights()
        {
            // 1. 5 卡列表包含第 5 张仅困难双卫同阵（Dungeon 112，boss_trial_kind = 8）且纳入困难解锁门禁
            var v2Locked = CoreGameplayLoopModels.BuildBossTrialCardsV2(new TrialJournalSnapshot { HardBossTrialUnlocked = false });
            Assert.That(v2Locked.Count, Is.EqualTo(5));
            Assert.That(v2Locked[4].BossTrialKind, Is.EqualTo(8u));
            Assert.That(v2Locked[4].NormalDungeonId, Is.EqualTo(0u));
            Assert.That(v2Locked[4].HardDungeonId, Is.EqualTo(112u));
            Assert.That(v2Locked[4].IsHardOnly, Is.True);
            Assert.That(v2Locked[4].HardRewardUnits, Is.EqualTo(15u));
            Assert.That(CoreGameplayLoopModels.IsHardBossTrialDungeon(112u), Is.True);

            // 2. 战斗 HUD + 3–5 人裂地双劈 + 一键化劲防御（仅本人/本人存活宠）+ 技能记忆
            var battleNet = new FakeTransport { PlayerId = 3001UL };
            var battle = new BattleClient(battleNet);
            var state = new BattleStateS2C
            {
                BattleId = 9901UL,
                RoundIndex = 5,
                Outcome = eBattleOutcome.BattleOutcomeOngoing,
                BossTrialKind = 8u,
                BattleDifficulty = 2u,
                BossPhase = 2u,
                BossMomentumStacks = 2u,
                BossStanceType = 1u,
                BossStanceRemainRounds = 2u,
                BossExposedPct = 35u,
                BossExposedUntilRound = 6u,
            };
            state.Actors.Add(new BattleActorState { ActorId = 3001UL, ActorType = eBattleActorType.BattleActorTypePlayer, TeamIndex = 0 });
            state.Actors.Add(new BattleActorState { ActorId = 300101UL, ActorType = eBattleActorType.BattleActorTypePet, OwnerPlayerId = 3001UL, TeamIndex = 0 });
            state.Actors.Add(new BattleActorState { ActorId = 3002UL, ActorType = eBattleActorType.BattleActorTypePlayer, TeamIndex = 0 });
            state.PendingActorIds.Add(new[] { 3001UL, 300101UL, 3002UL });
            var dualCleave = new BattleTelegraphEntry
            {
                SourceActorId = 9001UL,
                TargetActorId = 3001UL,
                ExecuteRound = 5u,
                IntentKind = 1u,
                Status = 2u,
            };
            dualCleave.TargetActorIds.Add(new[] { 3001UL, 3002UL });
            state.Telegraphs.Add(dualCleave);

            battleNet.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = 9901UL, State = state });
            var hud = CoreGameplayLoopModels.BuildBattleCoreHud(state);
            Assert.That(hud.IsBossTrial, Is.True);
            Assert.That(hud.IsBossPhaseTwo, Is.True);
            Assert.That(hud.IsBossExposed, Is.True);
            Assert.That(hud.HasBossStance, Is.True);
            Assert.That(hud.BossPhaseBannerText, Does.Contain("二阶段"));
            Assert.That(hud.BossStatusSummaryText, Does.Contain("破绽 +35%"));
            Assert.That(hud.BossStatusSummaryText, Does.Contain("蓄势 2/3"));
            Assert.That(hud.BossStatusSummaryText, Does.Contain("物防联防阵"));
            Assert.That(hud.Telegraphs[0].IsDualTarget, Is.True);
            Assert.That(CoreGameplayLoopModels.IsSplashFocusApplicable(state, 5u), Is.True);
            Assert.That(CoreGameplayLoopModels.IsSplashFocusApplicable(state, 1u), Is.False);

            // 一键化劲防御列表只含本人 3001（不含队友 3002 与未被点名的宝宝 300101）
            var quickParryIds = CoreGameplayLoopModels.CollectQuickParryDefendActorIds(3001UL, state);
            Assert.That(quickParryIds, Is.EqualTo(new[] { 3001UL }));
            // 试图替队友 3002 下达一键防御被本地拦截（V8 防线）
            Assert.That(battle.SubmitQuickParryDefend(3002UL), Is.False);
            Assert.That(battleNet.Calls.Count, Is.EqualTo(0));
            // 为本人提交一键防御复用 SubmitBattleAction
            Assert.That(battle.SubmitQuickParryDefend(3001UL), Is.True);
            var parryReq = (SubmitBattleActionRequest)battleNet.Calls[0].Request;
            Assert.That(parryReq.ActorId, Is.EqualTo(3001UL));
            Assert.That(parryReq.Action.ActionType, Is.EqualTo(eBattleActionType.BattleActionDefend));
            battleNet.Calls[0].Reply(new SubmitBattleActionResponse());

            // 记忆上回合成功提交的技能 ID
            Assert.That(battle.SubmitAction(new BattleAction
            {
                ActionType = eBattleActionType.BattleActionSkill,
                SkillTableId = 10034u,
                TargetId = 9001UL,
            }, 3001UL), Is.True);
            battleNet.Calls[1].Reply(new SubmitBattleActionResponse());
            Assert.That(battle.GetLastUsedSkillId(3001UL), Is.EqualTo(10034u));

            // 幽毒首领群体玄毒（IntentKind=8）当回合也为本人与存活出战宠提供一键化劲防御（不含队友 3002）
            var venomState = state.Clone();
            venomState.BossTrialKind = 4u;
            venomState.Telegraphs.Clear();
            venomState.Telegraphs.Add(new BattleTelegraphEntry
            {
                SourceActorId = 9001UL,
                TargetActorId = 0UL,
                ExecuteRound = 5u,
                IntentKind = 8u,
                Status = 2u,
            });
            var venomParryIds = CoreGameplayLoopModels.CollectQuickParryDefendActorIds(3001UL, venomState);
            Assert.That(venomParryIds, Is.EqualTo(new[] { 3001UL, 300101UL }));

            // 3. 机制横幅播报与结算战法高光徽章
            var banner = CoreGameplayLoopModels.DescribeBossMechanicEvent(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventBossMechanic,
                MechanicKind = 3u,
                Value = 35ul,
            });
            Assert.That(banner, Is.Not.Null);
            Assert.That(banner.Title, Does.Contain("崩势破盾"));
            Assert.That(banner.Title, Does.Contain("+35%"));
            Assert.That(banner.IsPositiveForPlayer, Is.True);

            var interruptBanner = CoreGameplayLoopModels.DescribeBossMechanicEvent(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventBossMechanic,
                MechanicKind = 3u,
                Value = 20ul,
            });
            Assert.That(interruptBanner, Is.Not.Null);
            Assert.That(interruptBanner.Title, Does.Contain("+20%"));
            Assert.That(interruptBanner.IsPositiveForPlayer, Is.True);

            var highlights = CoreGameplayLoopModels.BuildBossTrialHighlightBadges(new BattleSettlementData
            {
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                PlayerTeamIndex = 0u,
                TrialFacts = new BattleTrialJournalFacts
                {
                    InitialHumanCount = 1u,
                    CleaveParryCount = 2u,
                    HealInterruptBacklashCount = 1u,
                    StanceBreakCount = 1u,
                    AllSelfCleavesParried = true,
                },
            });
            Assert.That(highlights.Count, Is.EqualTo(5));
            Assert.That(highlights[0].Title, Is.EqualTo("化劲破招 ×2"));
            Assert.That(highlights[3].Title, Is.EqualTo("化劲宗师"));
            Assert.That(highlights[4].Title, Is.EqualTo("单骑破阵"));
        }

        [Test]
        public void BuildTacticalReview_CoversBossDefeatCauses_BossVictory_GuardRescue_Polarity_And_PvpSurrender()
        {
            // 1. 首领试炼失败：回春回血未打断 -> 指向聚火与断疗反噬
            var bossHealLoss = CoreGameplayLoopModels.BuildTacticalReview(new BattleEndS2C
            {
                Settlement = new BattleSettlementData
                {
                    Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                    PlayerTeamIndex = 0u,
                    TotalRounds = 9u,
                    BossTrialKind = 1u,
                    BattleDifficulty = 2u,
                    TrialFacts = new BattleTrialJournalFacts
                    {
                        BossGuardHealTotal = 4200ul,
                        HealInterruptBacklashCount = 0u,
                    },
                },
            });
            Assert.That(bossHealLoss, Is.Not.Null);
            Assert.That(bossHealLoss.IsVictory, Is.False);
            Assert.That(bossHealLoss.ModeTag, Is.EqualTo("首领试炼 · 困难"));
            Assert.That(bossHealLoss.ReasonHeadline, Does.Contain("4200"));
            Assert.That(bossHealLoss.NextMoveTip, Does.Contain("反噬"));

            // 2. 首领试炼失败：双卫同阵护势未破 -> 指向驱散 A34
            var dualStanceLoss = CoreGameplayLoopModels.BuildTacticalReview(new BattleEndS2C
            {
                Settlement = new BattleSettlementData
                {
                    Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                    PlayerTeamIndex = 0u,
                    TotalRounds = 7u,
                    BossTrialKind = 8u,
                    BattleDifficulty = 2u,
                    TrialFacts = new BattleTrialJournalFacts
                    {
                        GuardStanceDispelled = false,
                        StanceBreakCount = 0u,
                    },
                },
            });
            Assert.That(dualStanceLoss, Is.Not.Null);
            Assert.That(dualStanceLoss.ReasonHeadline, Does.Contain("护势联防阵"));
            Assert.That(dualStanceLoss.NextMoveTip, Does.Contain("A34"));

            // 3. 普通首领胜利（全程化劲破招）-> 引导进阶困难难度
            var bossNormalWin = CoreGameplayLoopModels.BuildTacticalReview(new BattleEndS2C
            {
                Settlement = new BattleSettlementData
                {
                    Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                    PlayerTeamIndex = 0u,
                    TotalRounds = 6u,
                    BossTrialKind = 1u,
                    BattleDifficulty = 1u,
                    TrialFacts = new BattleTrialJournalFacts
                    {
                        AllSelfCleavesParried = true,
                        CleaveParryCount = 2u,
                    },
                },
            });
            Assert.That(bossNormalWin, Is.Not.Null);
            Assert.That(bossNormalWin.IsVictory, Is.True);
            Assert.That(bossNormalWin.ReasonHeadline, Does.Contain("化劲破招 2 次"));
            Assert.That(bossNormalWin.NextMoveTip, Does.Contain("困难难度"));

            // 4. 护卫救援失败：护卫阵亡 -> 提示保护护卫
            var guardLoss = CoreGameplayLoopModels.BuildTacticalReview(new BattleEndS2C
            {
                Settlement = new BattleSettlementData
                {
                    Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                    PlayerTeamIndex = 0u,
                    TotalRounds = 5u,
                    BossTrialKind = 5u,
                    TrialFacts = new BattleTrialJournalFacts { GuardSurvived = false },
                },
            });
            Assert.That(guardLoss, Is.Not.Null);
            Assert.That(guardLoss.ModeTag, Does.Contain("护卫救援"));
            Assert.That(guardLoss.ReasonHeadline, Does.Contain("未能保全"));

            // 5. 物法易势 5V5 失败：单一伤害类型 -> 提示覆盖物法双伤
            var polarityLoss = CoreGameplayLoopModels.BuildTacticalReview(new BattleEndS2C
            {
                Settlement = new BattleSettlementData
                {
                    Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                    PlayerTeamIndex = 0u,
                    TotalRounds = 8u,
                    BossTrialKind = 6u,
                    MatchMode = (uint)MatchMode.Polarity5V5,
                    TrialFacts = new BattleTrialJournalFacts
                    {
                        TeamDealtPhysicalDirectDamage = true,
                        TeamDealtMagicDirectDamage = false,
                    },
                },
            });
            Assert.That(polarityLoss, Is.Not.Null);
            Assert.That(polarityLoss.ReasonHeadline, Does.Contain("伤害类型较单一"));

            // 6. 正式人宠会武体面认输 & 反挂机门槛
            var pvpSurrender = CoreGameplayLoopModels.BuildTacticalReview(new BattleEndS2C
            {
                Settlement = new BattleSettlementData
                {
                    Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                    PlayerTeamIndex = 0u,
                    TotalRounds = 5u,
                    MatchMode = (uint)MatchMode.PvpTeam,
                    Surrendered = true,
                },
            });
            Assert.That(pvpSurrender, Is.Not.Null);
            Assert.That(pvpSurrender.ModeTag, Is.EqualTo("正式人宠会武"));
            Assert.That(pvpSurrender.ReasonHeadline, Does.Contain("体面认输"));

            var pvpIneligible = CoreGameplayLoopModels.BuildTacticalReview(new BattleEndS2C
            {
                MaterialPvpLossIneligible = true,
                Settlement = new BattleSettlementData
                {
                    Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                    PlayerTeamIndex = 0u,
                    TotalRounds = 2u,
                    MatchMode = (uint)MatchMode.PvpTeam,
                },
            });
            Assert.That(pvpIneligible, Is.Not.Null);
            Assert.That(pvpIneligible.ReasonHeadline, Does.Contain("不扣 660"));
            Assert.That(pvpIneligible.NextMoveTip, Does.Contain("3 回合"));

            // 7. 非核心循环普通战斗返回 null，避免干扰
            var regularMob = CoreGameplayLoopModels.BuildTacticalReview(new BattleEndS2C
            {
                Settlement = new BattleSettlementData
                {
                    Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                    PlayerTeamIndex = 0u,
                    TotalRounds = 3u,
                    MatchMode = (uint)MatchMode.PveSolo,
                    BossTrialKind = 0u,
                },
            });
            Assert.That(regularMob, Is.Null);
        }

        [Test]
        public void BossTrialWeeklyAffix_RotationCatalogAndHudWiring()
        {
            Assert.That(BossTrialWeeklyAffixCatalog.AffixIdForTimestamp(0UL), Is.EqualTo(0u));
            for (ulong w = 0; w < 6; w++)
            {
                ulong ts = BossTrialWeeklyAffixCatalog.FirstMonday05Utc8Ms + w * BossTrialWeeklyAffixCatalog.WeekMs + 12345UL;
                uint expected = (uint)((w % 5UL) + 1UL);
                Assert.That(BossTrialWeeklyAffixCatalog.AffixIdForTimestamp(ts), Is.EqualTo(expected));
                var view = BossTrialWeeklyAffixCatalog.ForTimestamp(ts);
                Assert.That(view, Is.Not.Null);
                Assert.That(view.AffixId, Is.EqualTo(expected));
                Assert.That(view.Title, Is.Not.Empty);
                Assert.That(view.BoonText, Is.Not.Empty);
                Assert.That(view.ChallengeText, Is.Not.Empty);
                Assert.That(view.RecommendedLoadoutHint, Is.Not.Empty);
                Assert.That(view.ShortChipText, Is.Not.Empty);
                Assert.That(view.ResetCountdownText, Is.Not.Empty);
            }

            var hud = CoreGameplayLoopModels.BuildBattleCoreHud(new BattleStateS2C
            {
                BattleId = 9001UL,
                RoundIndex = 2u,
                BossTrialKind = 2u,
                BossPhase = 1u,
                BossWeeklyAffixId = 2u,
            });
            Assert.That(hud.IsBossTrial, Is.True);
            Assert.That(hud.HasBossWeeklyAffix, Is.True);
            Assert.That(hud.BossWeeklyAffixId, Is.EqualTo(2u));
            Assert.That(hud.BossWeeklyAffix.Title, Does.Contain("木"));
            Assert.That(hud.BossWeeklyAffix.BoonText, Does.Contain("驱散"));

            var noAffixHud = CoreGameplayLoopModels.BuildBattleCoreHud(new BattleStateS2C
            {
                BattleId = 9002UL,
                RoundIndex = 1u,
                BossTrialKind = 5u,
                BossWeeklyAffixId = 0u,
            });
            Assert.That(noAffixHud.HasBossWeeklyAffix, Is.False);

            var banner = CoreGameplayLoopModels.DescribeBossMechanicEvent(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventBossMechanic,
                MechanicKind = 10u,
                Value = 120UL,
            });
            Assert.That(banner, Is.Not.Null);
            Assert.That(banner.Title, Does.Contain("周常五行"));
        }
    }
}
