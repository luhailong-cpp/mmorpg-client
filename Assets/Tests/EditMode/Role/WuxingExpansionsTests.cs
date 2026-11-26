using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Season;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class WuxingExpansionsTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public bool IsReady { get; set; } = true;
            public ulong PlayerId { get; set; } = 70001ul;
            public event Action Disconnected;

            public readonly List<(uint MsgId, IMessage Request)> Sent = new List<(uint, IMessage)>();
            public readonly Dictionary<uint, Func<IMessage, IMessage>> Handlers = new Dictionary<uint, Func<IMessage, IMessage>>();

            public void Call<TResponse>(uint messageId, IMessage request, MessageParser<TResponse> parser,
                Action<TResponse> onOk, Action<string> onError) where TResponse : IMessage<TResponse>
            {
                Sent.Add((messageId, request));
                if (Handlers.TryGetValue(messageId, out var handler))
                {
                    var resp = handler(request);
                    onOk?.Invoke((TResponse)resp);
                }
            }

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
            public void SendOneWay(uint messageId, IMessage request) => Sent.Add((messageId, request));
            public void TriggerDisconnect() => Disconnected?.Invoke();
        }

        [Test]
        public void FormationCounterAndHumanPetWuxingSynergyRulesMatchServer()
        {
            Assert.AreEqual(5, WuxingExpansionModels.Formations.Length);
            Assert.AreEqual(15, WuxingExpansionModels.TowerSigils.Length);

            // 五阵循环互克
            Assert.AreEqual("克制敌阵 · 直伤 +5%", WuxingExpansionModels.FormationCounterLabel(1u, 2u));
            Assert.AreEqual("被敌阵克制", WuxingExpansionModels.FormationCounterLabel(2u, 1u));
            Assert.AreEqual("克制敌阵 · 直伤 +5%", WuxingExpansionModels.FormationCounterLabel(5u, 1u));
            Assert.AreEqual(string.Empty, WuxingExpansionModels.FormationCounterLabel(1u, 3u));

            // 五行相生链：金(1)->水(3)->木(2)->火(4)->土(5)->金(1)
            Assert.IsTrue(WuxingExpansionModels.AreElementsMutuallyGenerating(1u, 3u));
            Assert.IsTrue(WuxingExpansionModels.AreElementsMutuallyGenerating(3u, 1u));
            Assert.IsTrue(WuxingExpansionModels.AreElementsMutuallyGenerating(2u, 4u));
            Assert.IsFalse(WuxingExpansionModels.AreElementsMutuallyGenerating(1u, 2u));

            // 10 层位图统计
            Assert.AreEqual(3, WuxingExpansionModels.CountClearedFloors(0b0000010101u));
            Assert.IsTrue(WuxingExpansionModels.IsFloorCleared(0b0000010101u, 5u));
            Assert.IsFalse(WuxingExpansionModels.IsFloorCleared(0b0000010101u, 2u));
        }

        [Test]
        public void CustomDuelRulesSummaryAndBuildSummaryFormatCleanly()
        {
            var rules = new global::CustomDuelRules
            {
                UseFreePreset = true,
                EnablePolarityRotation = true,
                RoundLimitOverride = 8u,
                BanControlSkills = true,
                AllowPublicSpectate = true,
            };
            string summary = WuxingExpansionModels.FormatCustomDuelRulesSummary(rules);
            StringAssert.Contains("公平 3×3 模板", summary);
            StringAssert.Contains("物法易势", summary);
            StringAssert.Contains("限 8 回合", summary);
            StringAssert.Contains("禁控直伤", summary);
            StringAssert.Contains("公开观战", summary);

            var build = new global::BattleActorBuildSummary
            {
                PlayerId = 1001ul,
                Name = "青云道友",
                FormationId = 1u,
                WuxingElement = 1u,
                SeasonSealId = 101u,
                ActivePetTableId = 3u,
                ActivePetName = "灵狐",
                ActivePetElement = 3u,
                UsedFreePreset = true,
            };
            string text = WuxingExpansionModels.FormatBuildSummary(build);
            StringAssert.Contains("天覆阵", text);
            StringAssert.Contains("★相生", text);
            StringAssert.Contains("公平3×3", text);
        }

        [Test]
        public void SeasonClientDrivesFormationTowerAndBuildBookmarkRpcs()
        {
            var net = new FakeTransport();
            float clock = 10f;
            var client = new SeasonClient(net, () => net, () => clock, SeasonMessageIds.Generated);

            net.Handlers[SceneSeasonClientPlayerRpc.EquipWuxingFormationMessageId] = req =>
            {
                var r = (global::EquipWuxingFormationRequest)req;
                return new global::EquipWuxingFormationResponse
                {
                    EquippedFormationId = r.FormationId,
                    State = new global::SeasonStateSnapshot { EquippedFormationId = r.FormationId, Revision = 1ul },
                };
            };
            Assert.IsTrue(client.EquipFormation(3u));
            Assert.AreEqual(3u, client.EquippedFormationId);

            net.Handlers[SceneSeasonClientPlayerRpc.StartWuxingTowerRunMessageId] = _ =>
                new global::StartWuxingTowerRunResponse
                {
                    State = new global::WuxingTowerStateSnapshot
                    {
                        WeekKey = 202648u,
                        RunActive = true,
                        RunId = 1ul,
                        CurrentFloor = 1u,
                        CurrentFloorDungeonId = 101u,
                        Revision = 1ul,
                    },
                };
            Assert.IsTrue(client.StartWuxingTowerRun());
            Assert.IsNotNull(client.TowerSnapshot);
            Assert.IsTrue(client.TowerSnapshot.RunActive);
            Assert.AreEqual(1u, client.TowerSnapshot.CurrentFloor);

            net.Handlers[SceneSeasonClientPlayerRpc.PickWuxingTowerSigilMessageId] = req =>
            {
                var r = (global::PickWuxingTowerSigilRequest)req;
                var state = new global::WuxingTowerStateSnapshot
                {
                    WeekKey = 202648u,
                    RunActive = true,
                    RunId = r.ExpectedRunId,
                    CurrentFloor = 2u,
                    Revision = 2ul,
                };
                state.ActiveSigilIds.Add(r.SigilId);
                return new global::PickWuxingTowerSigilResponse { State = state };
            };
            Assert.IsTrue(client.PickWuxingTowerSigil(101u));
            Assert.AreEqual(2u, client.TowerSnapshot.CurrentFloor);
            Assert.AreEqual(1, client.TowerSnapshot.ActiveSigilIds.Count);

            net.Handlers[SceneSeasonClientPlayerRpc.SaveBattleBuildBookmarkMessageId] = req =>
            {
                var r = (global::SaveBattleBuildBookmarkRequest)req;
                var saved = r.Bookmark.Clone();
                saved.BookmarkId = 9ul;
                var resp = new global::SaveBattleBuildBookmarkResponse { Saved = saved };
                resp.AllBookmarks.Add(saved);
                return resp;
            };
            Assert.IsTrue(client.SaveBattleBuildBookmark(new global::SavedBattleBuildBookmarkView { Title = "测试构筑", FormationId = 3u }));
            Assert.AreEqual(1, client.SavedBuilds.Count);
            Assert.AreEqual(9ul, client.SavedBuilds[0].BookmarkId);

            client.Dispose();
        }

        [Test]
        public void BattleClientForwardsWuxingTowerAndCustomDuelPayloads()
        {
            var net = new FakeTransport();
            var battle = new BattleClient(net, null);

            battle.JoinQueue(Match.MatchMode.PveSolo, 101u, isWuxingTower: true);
            Assert.AreEqual(1, net.Sent.Count);
            var joinReq = (Match.JoinQueueRequest)net.Sent[0].Request;
            Assert.IsTrue(joinReq.IsWuxingTower);
            Assert.AreEqual(101u, joinReq.BattleConfigId);

            var rules = new global::CustomDuelRules { UseFreePreset = true, RoundLimitOverride = 8u, BanControlSkills = true };
            battle.ChallengePlayer(88888ul, rules);
            var challengeReq = (Match.ChallengePlayerRequest)net.Sent[1].Request;
            Assert.AreEqual(88888ul, challengeReq.TargetPlayerId);
            Assert.IsNotNull(challengeReq.CustomRules);
            Assert.IsTrue(challengeReq.CustomRules.BanControlSkills);

            battle.RespondChallenge(555ul, true, 102u, 201u);
            var respondReq = (Match.RespondChallengeRequest)net.Sent[2].Request;
            Assert.AreEqual(102u, respondReq.ResponderCharacterPresetId);
            Assert.AreEqual(201u, respondReq.ResponderPetPresetId);
        }
    }
}
