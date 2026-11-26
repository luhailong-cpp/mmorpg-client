using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Battle.Presentation;
using MmorpgClient.Game.Season;
using MmorpgClient.Game.Skills;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Skills;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class SeasonClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 9201;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;

            public readonly List<(uint msgId, IMessage req)> Calls = new();
            private readonly Queue<Action> _pending = new();
            public Func<uint, IMessage, (IMessage resp, string err)> Responder;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
            public void SendOneWay(uint messageId, IMessage request) { }

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add((messageId, request));
                var (resp, err) = Responder(messageId, request);
                if (err != null) _pending.Enqueue(() => onError(err));
                else _pending.Enqueue(() => onResponse((TResp)resp));
            }

            public void Flush()
            {
                while (_pending.Count > 0) _pending.Dequeue().Invoke();
            }

            public void TriggerDisconnect()
            {
                IsReady = false;
                Disconnected?.Invoke();
            }
        }

        [Test]
        public void Client_EnforcesMonotonicRevision_And_EquipsSeal()
        {
            var net = new FakeTransport();
            object conn = new object();
            ulong rev = 4;
            uint equipped = 1;
            net.Responder = (msgId, req) =>
            {
                if (msgId == SceneSeasonClientPlayerRpc.GetSeasonStateMessageId)
                {
                    var snap = new SeasonStateSnapshot
                    {
                        Available = true,
                        SeasonId = 1,
                        SeasonName = "首测赛季",
                        EquippedSealId = equipped,
                        ProgressPoints = 40,
                        MaxPoints = 100,
                        Revision = rev,
                        CanSwitchSeal = true,
                        CanCatchup = true,
                        CatchupCurrencyType = 0,
                        CatchupPricePerPoint = 10,
                    };
                    snap.Seals.Add(new SeasonSealOption { SealId = 1, Name = "破军印", Equipped = equipped == 1 });
                    snap.Seals.Add(new SeasonSealOption { SealId = 2, Name = "玄甲印", Equipped = equipped == 2 });
                    return (new GetSeasonStateResponse { State = snap }, null);
                }
                if (msgId == SceneSeasonClientPlayerRpc.EquipSeasonSealMessageId)
                {
                    var equipReq = (EquipSeasonSealRequest)req;
                    equipped = equipReq.SealId;
                    rev = 5;
                    var snap = new SeasonStateSnapshot
                    {
                        Available = true,
                        SeasonId = equipReq.SeasonId,
                        EquippedSealId = equipped,
                        ProgressPoints = 40,
                        MaxPoints = 100,
                        Revision = rev,
                        CanSwitchSeal = true,
                    };
                    snap.Seals.Add(new SeasonSealOption { SealId = 1, Name = "破军印", Equipped = equipped == 1 });
                    snap.Seals.Add(new SeasonSealOption { SealId = 2, Name = "玄甲印", Equipped = equipped == 2 });
                    return (new EquipSeasonSealResponse { State = snap }, null);
                }
                return (null, "unexpected");
            };

            float now = 10f;
            using var client = new SeasonClient(net, () => conn, () => now);
            client.RequestState(force: true);
            net.Flush();
            Assert.That(client.Snapshot?.EquippedSealId, Is.EqualTo(1u));
            Assert.That(SeasonModels.FindEquippedSeal(client.Snapshot)?.Name, Is.EqualTo("破军印"));

            // 乱序旧 revision 不回退
            rev = 2;
            now = 20f;
            client.RequestState(force: true);
            net.Flush();
            Assert.That(client.Snapshot?.Revision, Is.EqualTo(4ul));

            // 切换赛季印
            now = 25f;
            Assert.That(client.EquipSeal(2u), Is.True);
            net.Flush();
            Assert.That(client.Snapshot?.EquippedSealId, Is.EqualTo(2u));
            Assert.That(SeasonModels.FindEquippedSeal(client.Snapshot)?.Name, Is.EqualTo("玄甲印"));
        }

        [Test]
        public void CatchupSeal_ResendsOnceWithSameKey_And_EmitsEvent()
        {
            var net = new FakeTransport();
            object conn = new object();
            int catchupAttempts = 0;
            net.Responder = (msgId, req) =>
            {
                if (msgId == SceneSeasonClientPlayerRpc.GetSeasonStateMessageId)
                {
                    return (new GetSeasonStateResponse
                    {
                        State = new SeasonStateSnapshot
                        {
                            Available = true,
                            SeasonId = 1,
                            ProgressPoints = 60,
                            MaxPoints = 100,
                            CanCatchup = true,
                            CatchupCurrencyType = 0,
                            CatchupPricePerPoint = 50,
                            CatchupRemainingTotalPrice = 2000,
                            Revision = 1,
                        },
                    }, null);
                }
                if (msgId == SceneSeasonClientPlayerRpc.CatchupSeasonSealMessageId)
                {
                    catchupAttempts++;
                    if (catchupAttempts == 1)
                        return (null, OutstandingCalls.TimeoutDetail);
                    return (new CatchupSeasonSealResponse
                    {
                        AddedPoints = 20,
                        SpentCurrency = 1000,
                        State = new SeasonStateSnapshot
                        {
                            Available = true,
                            SeasonId = 1,
                            ProgressPoints = 80,
                            MaxPoints = 100,
                            CanCatchup = true,
                            CatchupCurrencyType = 0,
                            CatchupPricePerPoint = 50,
                            Revision = 2,
                        },
                    }, null);
                }
                return (null, "unexpected");
            };

            float now = 10f;
            using var client = new SeasonClient(net, () => conn, () => now);
            SeasonSealCatchupEvent? received = null;
            client.SealCatchupCompleted += ev => received = ev;

            client.RequestState(force: true);
            net.Flush();

            now = 15f;
            Assert.That(client.CatchupSeal(20u), Is.True);
            net.Flush();

            Assert.That(catchupAttempts, Is.EqualTo(2));
            var firstReq = (CatchupSeasonSealRequest)net.Calls[1].req;
            var secondReq = (CatchupSeasonSealRequest)net.Calls[2].req;
            Assert.That(firstReq.ClientRequestId, Is.Not.EqualTo(0ul));
            Assert.That(secondReq.ClientRequestId, Is.EqualTo(firstReq.ClientRequestId));
            Assert.That(firstReq.ExpectedTotalPrice, Is.EqualTo(1000ul));
            Assert.That(received.HasValue, Is.True);
            Assert.That(received.Value.AddedPoints, Is.EqualTo(20u));
            Assert.That(client.Snapshot?.ProgressPoints, Is.EqualTo(80u));
            Assert.That(SeasonModels.FormatBpsPercent(600u), Is.EqualTo("6%"));
            Assert.That(SeasonModels.FormatBpsPercent(450u), Is.EqualTo("4.5%"));
            Assert.That(SeasonModels.DescribeTip((uint)season_error.KSeasonInBattle), Does.Contain("战斗中"));
        }

        [Test]
        public void SeasonModels_FormatsTimeBreakdownRulesIconsAndCatchupQuotes()
        {
            Assert.That(SeasonModels.FormatBpsOneDecimal(0u), Is.EqualTo("0.0%"));
            Assert.That(SeasonModels.FormatBpsOneDecimal(150u), Is.EqualTo("1.5%"));
            Assert.That(SeasonModels.FormatBpsOneDecimal(300u), Is.EqualTo("3.0%"));

            Assert.That(SeasonModels.FormatSeasonTimeLabel(0UL, 1_000_000UL), Is.EqualTo("首测常开"));
            Assert.That(SeasonModels.FormatSeasonTimeLabel(1_000UL, 2_000UL), Is.EqualTo("本季已结束"));
            Assert.That(SeasonModels.FormatSeasonTimeLabel(1_000UL + (2UL * 86400UL + 3UL * 3600UL) * 1000UL, 1_000UL),
                Is.EqualTo("剩余 2天3时"));

            Assert.That(SeasonModels.NormalizeIconResource("Assets/Resources/UI/SeasonSeals/101.png", "UI/SeasonSeals/", 101u),
                Is.EqualTo("UI/SeasonSeals/101"));
            Assert.That(SeasonModels.NormalizeIconResource("../evil/path", "UI/SeasonSeals/", 102u),
                Is.EqualTo("UI/SeasonSeals/102"));

            var snap = new SeasonStateSnapshot
            {
                Available = true,
                SeasonId = 1u,
                ProgressPoints = 6u,
                MaxPoints = 12u,
                BattlePoints = 4u,
                CatchupPoints = 2u,
                PvpWinPoints = 2u,
                PvpOtherPoints = 1u,
                PveWinPoints = 1u,
                CanCatchup = true,
                CatchupCurrencyType = 1u,
                CatchupPricePerPoint = 10UL,
                CatchupRemainingTotalPrice = 60UL,
            };
            Assert.That(SeasonModels.DescribeProgressBreakdown(snap), Is.EqualTo("战斗 4 点 + 追赶 2 点"));
            Assert.That(SeasonModels.DescribeSourceRuleLine(snap), Is.EqualTo("正规排位胜+2/完赛+1 · 首领试炼胜+1 · 无每日上限"));

            Assert.That(SeasonModels.TryBuildCatchupQuote(snap, 1u, out var oneQuote), Is.True);
            Assert.That(oneQuote.PointsToBuy, Is.EqualTo(1u));
            Assert.That(oneQuote.RequestPoints, Is.EqualTo(1u));
            Assert.That(oneQuote.TotalPrice, Is.EqualTo(10UL));
            Assert.That(oneQuote.ProgressAfter, Is.EqualTo(7u));
            Assert.That(oneQuote.IsFullFill, Is.False);

            Assert.That(SeasonModels.TryBuildCatchupQuote(snap, 0u, out var fullQuote), Is.True);
            Assert.That(fullQuote.PointsToBuy, Is.EqualTo(6u));
            Assert.That(fullQuote.RequestPoints, Is.EqualTo(0u));
            Assert.That(fullQuote.TotalPrice, Is.EqualTo(60UL));
            Assert.That(fullQuote.ProgressAfter, Is.EqualTo(12u));
            Assert.That(fullQuote.IsFullFill, Is.True);

            snap.ProgressPoints = 12u;
            Assert.That(SeasonModels.TryBuildCatchupQuote(snap, 1u, out _), Is.False);

            Assert.That(SeasonModels.FormatSeasonSealTriggerText(101u, 1u, 120L, _ => "破阵锐印"),
                Is.EqualTo("破阵锐印 · 破盾 -120"));
            Assert.That(SeasonModels.FormatSeasonSealTriggerText(103u, 6u, 80L, _ => "固阵守印"),
                Is.EqualTo("固阵守印 · 减伤 -80"));
            Assert.That(SeasonModels.FormatSeasonSealTriggerText(104u, 7u, 45L, _ => "聚息援印"),
                Is.EqualTo("聚息援印 · 回蓝 +45"));
        }

        [Test]
        public void TurnPlan_CarriesSeasonSealId_And_SkillBookBlocksSeasonalUpgrade()
        {
            var protoEv = new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventArtifact,
                SourceId = 1001UL,
                TargetId = 2001UL,
                ArtifactItemId = 0UL,
                ArtifactEffectId = 1u,
                SeasonSealId = 101u,
                Value = 150UL,
            };
            var turnResult = new TurnResultS2C();
            turnResult.Events.Add(protoEv);
            var plan = TurnPlan.Build(turnResult, 0u);
            Assert.That(plan.ArtifactEvents.Count, Is.EqualTo(1));
            Assert.That(plan.ArtifactEvents[0].Input.SeasonSealId, Is.EqualTo(101u));
            Assert.That(plan.ArtifactEvents[0].Input.ArtifactItemId, Is.EqualTo(0UL));

            var skillState = new SkillBookViewState
            {
                IsConnected = true,
                IsReadOnly = false,
                Snapshot = new SkillBookSnapshot { CurrencyBalance = 999999UL },
            };
            var seasonalEntry = new SkillEntryState
            {
                Id = 15001u,
                SeasonId = 1u,
                Rank = 4u,
                RankLimit = 10u,
                NextRankCost = 0UL,
                Definition = new SkillDefinition { id = 15001u, maxRank = 10u, name = "破阵裂锋" },
            };
            Assert.That(seasonalEntry.IsSeasonal, Is.True);
            Assert.That(SkillBookViewState.CanUpgrade(skillState, seasonalEntry), Is.False);
            Assert.That(SkillBookMapper.DescribeTip((uint)season_error.KSeasonSkillNoUpgrade), Does.Contain("免费"));
        }

        [Test]
        public void CatchupSeal_ReplayedResponseUpdatesSnapshotCleanly()
        {
            var net = new FakeTransport();
            object conn = new object();
            net.Responder = (msgId, req) =>
            {
                if (msgId == SceneSeasonClientPlayerRpc.GetSeasonStateMessageId)
                {
                    return (new GetSeasonStateResponse
                    {
                        State = new SeasonStateSnapshot
                        {
                            Available = true,
                            SeasonId = 1u,
                            ProgressPoints = 11u,
                            MaxPoints = 12u,
                            CanCatchup = true,
                            CatchupCurrencyType = 1u,
                            CatchupPricePerPoint = 10UL,
                            CatchupRemainingTotalPrice = 10UL,
                            Revision = 3UL,
                        },
                    }, null);
                }
                return (new CatchupSeasonSealResponse
                {
                    AddedPoints = 1u,
                    SpentCurrency = 10UL,
                    Replayed = true,
                    State = new SeasonStateSnapshot
                    {
                        Available = true,
                        SeasonId = 1u,
                        ProgressPoints = 12u,
                        MaxPoints = 12u,
                        CanCatchup = false,
                        CatchupCurrencyType = 1u,
                        CatchupPricePerPoint = 10UL,
                        CatchupRemainingTotalPrice = 0UL,
                        Revision = 4UL,
                    },
                }, null);
            };

            float now = 10f;
            using var client = new SeasonClient(net, () => conn, () => now);
            SeasonSealCatchupEvent? ev = null;
            client.SealCatchupCompleted += e => ev = e;
            client.RequestState(force: true);
            net.Flush();

            now = 20f;
            Assert.That(client.CatchupSeal(1u), Is.True);
            net.Flush();
            Assert.That(ev.HasValue, Is.True);
            Assert.That(ev.Value.Replayed, Is.True);
            Assert.That(client.Snapshot?.ProgressPoints, Is.EqualTo(12u));
            Assert.That(client.StatusIsError, Is.False);
        }

        [Test]
        public void SeasonV2Models_BuildSettlementDeltaPresetsWeeklyAffixAndHonors()
        {
            var snap = new SeasonStateSnapshot
            {
                Available = true,
                SeasonId = 1u,
                SeasonName = "破阵之季",
                EquippedSealId = 101u,
                ProgressPoints = 12u,
                MaxPoints = 12u,
                WeeklyAffix = new SeasonWeeklyAffixView
                {
                    WeekIndex = 1u,
                    AffixId = 1u,
                    Name = "护盾过载周",
                    Description = "全员对称：双方技能护盾 +15%",
                },
            };
            snap.WeeklyAffix.RecommendedSealIds.Add(101u);
            snap.WeeklyAffix.RecommendedSealIds.Add(102u);
            snap.Seals.Add(new SeasonSealOption { SealId = 101u, Name = "破阵锐印", EffectId = 1u, Equipped = true });
            snap.Seals.Add(new SeasonSealOption { SealId = 103u, Name = "固阵守印", EffectId = 6u });
            snap.Seals.Add(new SeasonSealOption { SealId = 104u, Name = "聚息援印", EffectId = 7u });
            snap.SeasonSkills.Add(new SeasonSkillSummary { SkillTableId = 15001u, Name = "破阵裂锋", TargetTeam = 1u });
            snap.SeasonSkills.Add(new SeasonSkillSummary { SkillTableId = 15002u, Name = "破阵灵雷", TargetTeam = 1u });
            snap.SeasonSkills.Add(new SeasonSkillSummary { SkillTableId = 15003u, Name = "固阵灵障", TargetTeam = 2u });
            snap.SeasonSkills.Add(new SeasonSkillSummary { SkillTableId = 15004u, Name = "破阵蓄势", TargetTeam = 2u });
            snap.Honors.Add(new SeasonHonorView
            {
                SeasonId = 1u,
                SeasonName = "破阵之季",
                HonorTitleName = "破阵宗师",
                HonorBadgeIconKey = "UI/SeasonHonors/honor_s1_pozhen",
                ProgressPoints = 12u,
                MaxPoints = 12u,
                Unlocked = true,
                IsCurrentSeason = true,
            });

            // 1. 结算进度增量与首次解锁永久零数值纪念称谓
            var battleEnd = new BattleEndS2C
            {
                SeasonId = 1u,
                SeasonSealPointsAdded = 2u,
                SeasonSealProgressAfter = 12u,
                SeasonSealMaxPoints = 12u,
                SeasonHonorNewlyUnlocked = true,
            };
            var settle = SeasonModels.BuildSettlementProgressView(battleEnd, snap);
            Assert.That(settle, Is.Not.Null);
            Assert.That(settle.SeasonName, Is.EqualTo("破阵之季"));
            Assert.That(settle.PointsAdded, Is.EqualTo(2u));
            Assert.That(settle.ProgressAfter, Is.EqualTo(12u));
            Assert.That(settle.MaxPoints, Is.EqualTo(12u));
            Assert.That(settle.IsMaxed, Is.True);
            Assert.That(settle.HonorNewlyUnlocked, Is.True);
            Assert.That(settle.HonorTitleName, Is.EqualTo("破阵宗师"));
            Assert.That(SeasonModels.BuildSettlementProgressView(new BattleEndS2C(), snap), Is.Null);

            // 2. 动态一键推荐流派（含本周推荐高亮与当前佩戴标记）
            var presets = SeasonModels.BuildRecommendedLoadouts(snap);
            Assert.That(presets.Count, Is.EqualTo(3));
            Assert.That(presets[0].SealId, Is.EqualTo(101u));
            Assert.That(presets[0].RecommendedThisWeek, Is.True);
            Assert.That(presets[0].IsCurrentlyEquipped, Is.True);
            Assert.That(presets[0].PairedSkillsSummary, Is.EqualTo("破阵裂锋 + 破阵灵雷"));
            Assert.That(presets[1].SealId, Is.EqualTo(103u));
            Assert.That(presets[1].RecommendedThisWeek, Is.False);
            Assert.That(presets[2].SealId, Is.EqualTo(104u));

            // 3. 战斗周词缀文案与荣誉统计
            Assert.That(SeasonModels.DescribeWeeklyAffixShort(1u), Is.EqualTo("护盾过载周"));
            Assert.That(SeasonModels.DescribeWeeklyAffixShort(2u), Is.EqualTo("灵息充盈周"));
            Assert.That(SeasonModels.DescribeWeeklyAffixShort(3u), Is.EqualTo("先机守势周"));
            Assert.That(SeasonModels.DescribeWeeklyAffixShort(4u), Is.EqualTo("锋芒竞速周"));
            Assert.That(SeasonModels.DescribeWeeklyAffixShort(0u), Is.Empty);
            Assert.That(SeasonModels.CountUnlockedHonors(snap), Is.EqualTo(1));
            Assert.That(SeasonModels.FindCurrentHonor(snap)?.HonorTitleName, Is.EqualTo("破阵宗师"));
        }
    }
}
