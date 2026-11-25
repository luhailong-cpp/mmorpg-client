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

            // 3. 轮换娱乐场：护卫救援 (109) + 物法易势仅 5V5 (110)
            var rotations = CoreGameplayLoopModels.BuildRotationCards(activeSeasonWeek: 5u);
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
            Assert.That(hud.HasPolarity, Is.True);
            Assert.That(hud.PolarityBannerText, Does.Contain("物势"));
            Assert.That(hud.HasProtectedGuard, Is.True);

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

            // 6. 主城总览：3 数字 + 4 个零卖数值预留位
            var hub = CoreGameplayLoopModels.BuildMainCityHub(
                new EquipmentMaterialRewardSnapshot { LifetimeEarned = 120u, LifetimeLimit = 660u, AvailableUnits = 20ul },
                profile,
                new SeasonStateSnapshot { Available = true, ProgressPoints = 30, MaxPoints = 100 },
                new TrialJournalSnapshot(),
                paymentEnabled: false);
            Assert.That(hub.MaterialProgressValue, Is.EqualTo("120/660（待领 20）"));
            Assert.That(hub.SmallSpendSlots.Count, Is.EqualTo(4));
            foreach (var slot in hub.SmallSpendSlots)
                Assert.That(slot.ZeroStatGuarantee, Is.True);
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
    }
}
