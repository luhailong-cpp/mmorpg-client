using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.TrialJournal;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class TrialJournalClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 9101;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;

            public readonly List<(uint msgId, IMessage req)> Calls = new();
            private readonly Queue<Action> _pending = new();
            public Func<uint, IMessage, (IMessage resp, string err)> Responder;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add((messageId, request));
                var (resp, err) = Responder(messageId, request);
                if (err != null) _pending.Enqueue(() => onError(err));
                else _pending.Enqueue(() => onResponse((TResp)resp));
            }

            public void SendOneWay(uint messageId, IMessage request) { }

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
        public void Client_EnforcesMonotonicStateRevisionAndRejectsOldRoleResponse()
        {
            var net = new FakeTransport();
            object connection = new object();
            ulong revToReturn = 5;
            net.Responder = (_, _) => (new GetTrialJournalResponse
            {
                Snapshot = new TrialJournalSnapshot { Available = true, CanOperate = true, StateRevision = revToReturn }
            }, null);

            float now = 10f;
            using var client = new TrialJournalClient(net, () => connection, () => now);
            client.RequestSnapshot(force: true);
            net.Flush();
            Assert.That(client.Snapshot?.StateRevision, Is.EqualTo(5ul));

            // 乱序旧 revision 不得回退
            revToReturn = 3;
            now = 20f;
            client.RequestSnapshot(force: true);
            net.Flush();
            Assert.That(client.Snapshot?.StateRevision, Is.EqualTo(5ul));

            // 切角后旧回包丢弃
            revToReturn = 9;
            now = 30f;
            client.RequestSnapshot(force: true);
            net.PlayerId = 99999;
            net.Flush();
            Assert.That(client.Snapshot, Is.Null);
        }

        [Test]
        public void Client_ClaimsMilestoneAndRedeemsVoucherWithEvents()
        {
            var net = new FakeTransport();
            object connection = new object();
            net.Responder = (msgId, _) =>
            {
                if (msgId == TrialJournalMessageIds.Generated.ClaimMilestone)
                {
                    return (new ClaimTrialJournalMilestoneResponse
                    {
                        GrantedSandCount = 2,
                        GrantedVoucherCount = 1,
                        GrantedPetCosmeticId = 3,
                        GrantedSkillVfxId = 3,
                        Snapshot = new TrialJournalSnapshot
                        {
                            Available = true,
                            CanOperate = true,
                            StateRevision = 2,
                            BoundSandBalance = 2,
                            VoucherBalance = 1,
                        },
                    }, null);
                }
                return (new RedeemBoundArtifactVoucherResponse
                {
                    CreatedItemId = 9500001,
                    ArtifactItemConfigId = 3301,
                    Snapshot = new TrialJournalSnapshot
                    {
                        Available = true,
                        CanOperate = true,
                        StateRevision = 3,
                        BoundSandBalance = 2,
                        VoucherBalance = 0,
                    },
                }, null);
            };

            float now = 10f;
            using var client = new TrialJournalClient(net, () => connection, () => now);
            TrialMilestoneClaimedEvent? claimed = null;
            TrialArtifactRedeemedEvent? redeemed = null;
            client.MilestoneClaimed += ev => claimed = ev;
            client.ArtifactRedeemed += ev => redeemed = ev;

            Assert.That(client.ClaimMilestone(102), Is.True);
            net.Flush();
            Assert.That(claimed.HasValue, Is.True);
            Assert.That(claimed.Value.GrantedVoucherCount, Is.EqualTo(1u));
            Assert.That(claimed.Value.GrantedPetCosmeticId, Is.EqualTo(3u));
            Assert.That(claimed.Value.GrantedSkillVfxId, Is.EqualTo(3u));
            Assert.That(client.Snapshot?.VoucherBalance, Is.EqualTo(1ul));

            now = 12f;
            Assert.That(client.RedeemArtifactVoucher(3301), Is.True);
            net.Flush();
            Assert.That(redeemed.HasValue, Is.True);
            Assert.That(redeemed.Value.CreatedItemId, Is.EqualTo(9500001ul));
            Assert.That(client.Snapshot?.VoucherBalance, Is.EqualTo(0ul));
        }

        [Test]
        public void Models_ComputeSummariesClaimableCountAndUnlockedCommentaries()
        {
            var snap = new TrialJournalSnapshot { Available = true, CanOperate = true, VoucherBalance = 1 };
            snap.Milestones.Add(new TrialJournalMilestoneView
            {
                MilestoneId = 101,
                ScopeChapterId = 1,
                Claimable = true,
            });
            snap.Milestones.Add(new TrialJournalMilestoneView
            {
                MilestoneId = 104,
                ScopeChapterId = 1,
                Claimed = true,
                CommentaryKey = "commentary_tingyun_trial_v1",
                CommentaryTitle = "祝停云·破阵跋",
                CommentaryBody = "枪穗上的旧船绳又沾了墨。",
            });

            Assert.That(TrialJournalModels.CountClaimableMilestones(snap, 1), Is.EqualTo(1));
            Assert.That(TrialJournalModels.SealSlotLabel(3), Is.EqualTo("巧"));
            Assert.That(TrialJournalModels.DescribeTip((uint)trial_journal_error.KTrialJournalBagFull), Does.Contain("背包"));

            var commentaries = TrialJournalModels.CollectUnlockedCommentaries(snap);
            Assert.That(commentaries.Count, Is.EqualTo(1));
            Assert.That(commentaries[0].Title, Is.EqualTo("祝停云·破阵跋"));

            var loadout = new TrialJournalLoadoutSnapshot { PetTableId = 1001, SeasonSealId = 101 };
            loadout.ArtifactEffectIds.Add(1);
            loadout.ArtifactEffectIds.Add(8);
            string summary = TrialJournalModels.FormatSoloLoadoutSummary(
                loadout,
                id => id == 1001 ? "青鸾" : "",
                id => id == 1 ? "破障锥" : (id == 8 ? "清心符" : ""),
                id => id == 101 ? "破障印" : "");
            Assert.That(summary, Does.Contain("灵宠·青鸾"));
            Assert.That(summary, Does.Contain("破障锥 / 清心符"));
            Assert.That(summary, Does.Contain("赛季印·破障印"));
        }

        [Test]
        public void Models_ParseAllElevenJumpTargetsAndEntryMonstersAndBattleSummary()
        {
            var expected = new (string raw, TrialJumpKind kind, uint dungeonId, uint difficulty)[]
            {
                ("boss_trial:normal:rejuvenation", TrialJumpKind.BossTrial, 101u, 1u),
                ("boss_trial:hard:rejuvenation", TrialJumpKind.BossTrial, 102u, 2u),
                ("boss_trial:normal:bulwark", TrialJumpKind.BossTrial, 103u, 1u),
                ("boss_trial:hard:bulwark", TrialJumpKind.BossTrial, 104u, 2u),
                ("boss_trial:normal:barrier", TrialJumpKind.BossTrial, 105u, 1u),
                ("boss_trial:hard:barrier", TrialJumpKind.BossTrial, 106u, 2u),
                ("boss_trial:normal:venom", TrialJumpKind.BossTrial, 107u, 1u),
                ("boss_trial:hard:venom", TrialJumpKind.BossTrial, 108u, 2u),
                ("boss_trial:hard:dual_guard", TrialJumpKind.BossTrial, 112u, 2u),
                ("boss_trial:normal:rescue", TrialJumpKind.BossTrial, 109u, 1u),
                ("polarity_5v5:110", TrialJumpKind.Polarity5v5, 110u, 0u),
                ("ranked_pvp:5", TrialJumpKind.RankedPvp, 5u, 0u),
            };
            foreach (var item in expected)
            {
                Assert.That(TrialJournalModels.TryParseJumpTarget(item.raw, out var target), Is.True, item.raw);
                Assert.That(target.IsValid, Is.True, item.raw);
                Assert.That(target.Kind, Is.EqualTo(item.kind), item.raw);
                Assert.That(target.DungeonConfigId, Is.EqualTo(item.dungeonId), item.raw);
                Assert.That(target.Difficulty, Is.EqualTo(item.difficulty), item.raw);
            }
            Assert.That(TrialJournalModels.TryParseJumpTarget("", out _), Is.False);
            Assert.That(TrialJournalModels.TryParseJumpTarget("unknown:0", out _), Is.False);

            Assert.That(TrialJournalModels.TryGetEntryMonsterIds(101u, out uint b1, out uint g1), Is.True);
            Assert.That((b1, g1), Is.EqualTo((101u, 102u)));
            Assert.That(TrialJournalModels.TryGetEntryMonsterIds(102u, out uint b2, out uint g2), Is.True);
            Assert.That((b2, g2), Is.EqualTo((101u, 103u)));
            Assert.That(TrialJournalModels.TryGetEntryMonsterIds(105u, out uint bDual, out uint gDual), Is.True);
            Assert.That((bDual, gDual), Is.EqualTo((101u, 102u)));
            Assert.That(TrialJournalModels.GetEntryGuardMonsterIds(105u), Is.EqualTo(new[] { 102u, 103u }));
            Assert.That(TrialJournalModels.TryGetEntryMonsterIds(201u, out uint b3, out uint g3), Is.True);
            Assert.That((b3, g3), Is.EqualTo((104u, 105u)));
            Assert.That(TrialJournalModels.TryGetEntryMonsterIds(204u, out uint b4, out uint g4), Is.True);
            Assert.That((b4, g4), Is.EqualTo((106u, 107u)));
            Assert.That(TrialJournalModels.TryGetEntryMonsterIds(301u, out uint b5, out uint g5), Is.True);
            Assert.That((b5, g5), Is.EqualTo((101u, 102u)));
            Assert.That(TrialJournalModels.TryGetEntryMonsterIds(302u, out _, out _), Is.False);
            Assert.That(TrialJournalModels.TryGetEntryMonsterIds(303u, out _, out _), Is.False);

            var snapshot = new TrialJournalSnapshot { Available = true, CanOperate = true };
            var ch2 = new TrialJournalChapterView { ChapterId = 2 };
            var entry201 = new TrialJournalEntryView { EntryId = 201 };
            entry201.Seals.Add(new TrialJournalSealView { SealId = 2011, Title = "灵障初破" });
            entry201.Seals.Add(new TrialJournalSealView { SealId = 2013, Title = "碎障之锋" });
            ch2.Entries.Add(entry201);
            snapshot.Chapters.Add(ch2);
            snapshot.Milestones.Add(new TrialJournalMilestoneView
            {
                MilestoneId = 901,
                ScopeChapterId = 0,
                RequiredSeals = 18,
                GrantAppearanceId = "20_star_formation_master_girl",
            });

            var gm = TrialJournalModels.FindGrandmasterMilestone(snapshot);
            Assert.That(gm?.MilestoneId, Is.EqualTo(901u));
            Assert.That(gm?.GrantAppearanceId, Is.EqualTo("20_star_formation_master_girl"));

            var battleResult = new PlayerBattleResult
            {
                TrialJournalEntryId = 201,
                TrialNewSoloBest = true,
                TotalRounds = 5,
            };
            battleResult.TrialNewlyLitSealIds.Add(2011);
            battleResult.TrialNewlyLitSealIds.Add(2013);
            string formatted = TrialJournalModels.FormatBattleResultSummary(battleResult, snapshot);
            Assert.That(formatted, Does.Contain("灵障初破、碎障之锋"));
            Assert.That(formatted, Does.Contain("刷新单人破阵纪录（5回合）"));

            Assert.That(BattleReportClient.IsPvpMode(Match.MatchMode.Polarity5V5), Is.True);
            Assert.That(BattleReportClient.ModeName(Match.MatchMode.Polarity5V5), Is.EqualTo("物法易势 5v5"));
        }

        [Test]
        public void V3_Vol4WildRealmsJumpsVowsChallengeCardsAndPveReplayPolicy()
        {
            // 1. 卷四三境明雷跳转、地图绑定与怪物立绘映射
            var wildCases = new (string raw, uint dungeonId, uint sceneId, uint entryId, uint bossId, uint guardId, string sceneName)[]
            {
                ("boss_trial:wild:wind", 113u, 2u, 401u, 111u, 112u, "八景岛"),
                ("boss_trial:wild:bell", 114u, 5u, 402u, 113u, 114u, "碧渊水宫"),
                ("boss_trial:wild:tide", 115u, 3u, 403u, 115u, 116u, "月汐湾"),
            };
            foreach (var c in wildCases)
            {
                Assert.That(TrialJournalModels.TryParseJumpTarget(c.raw, out var target), Is.True, c.raw);
                Assert.That(target.Kind, Is.EqualTo(TrialJumpKind.BossTrial));
                Assert.That(target.DungeonConfigId, Is.EqualTo(c.dungeonId));
                Assert.That(target.Difficulty, Is.EqualTo(1u));
                Assert.That(target.RequiredSceneConfigId, Is.EqualTo(c.sceneId));
                Assert.That(TrialJournalModels.WildSceneName(c.sceneId), Is.EqualTo(c.sceneName));

                Assert.That(TrialJournalModels.TryGetWildSceneBossInfo(c.sceneId, out uint eId, out uint dId, out string bossTitle), Is.True);
                Assert.That((eId, dId), Is.EqualTo((c.entryId, c.dungeonId)));
                Assert.That(bossTitle, Is.Not.Empty);

                Assert.That(TrialJournalModels.TryGetEntryMonsterIds(c.entryId, out uint bId, out uint gId), Is.True);
                Assert.That((bId, gId), Is.EqualTo((c.bossId, c.guardId)));
            }
            Assert.That(TrialJournalModels.TryGetWildSceneBossInfo(1u, out _, out _, out _), Is.False);

            // 2. 试炼誓约掩码校验、汇总与回合紧缩
            Assert.That(TrialJournalModels.IsValidVowMask(0u), Is.True);
            Assert.That(TrialJournalModels.IsValidVowMask(TrialJournalModels.VowMaskAll), Is.True);
            Assert.That(TrialJournalModels.IsValidVowMask(0x10u), Is.False);
            uint combo = TrialJournalModels.VowBanControl | TrialJournalModels.VowNoPet | TrialJournalModels.VowTightRounds;
            Assert.That(TrialJournalModels.CountVows(combo), Is.EqualTo(3u));
            Assert.That(TrialJournalModels.FormatVowSummary(combo), Is.EqualTo("禁控硬破+无宠孤剑+紧缩回合"));
            Assert.That(TrialJournalModels.TightenRoundLimit(12u), Is.EqualTo(9u));
            Assert.That(TrialJournalModels.TightenRoundLimit(7u), Is.EqualTo(6u));

            // 3. 结算摘要带誓约刷新 + 卷四导航页签过滤与 36 印总计
            var snap = new TrialJournalSnapshot { Available = true, CanOperate = true, TotalPermanentSeals = 36, TotalPermanentLitSeals = 10 };
            var ch4 = new TrialJournalChapterView { ChapterId = TrialJournalModels.ChapterWildRealms, ChapterName = "卷四 · 三境明雷", TotalSealCount = 9 };
            ch4.Entries.Add(new TrialJournalEntryView { EntryId = 401u, DisplayName = "八景岛 · 风眼引风", VowSupported = true, CanEnter = true });
            snap.Chapters.Add(ch4);
            Assert.That(TrialJournalUiState.ResolveTabForEntry(snap, 401u), Is.EqualTo(TrialJournalNavTab.WildRealms));
            Assert.That(TrialJournalUiState.FilterEntries(snap, TrialJournalNavTab.WildRealms).Count, Is.EqualTo(1));

            var vowResult = new PlayerBattleResult
            {
                BattleId = 88001ul,
                MatchMode = (uint)Match.MatchMode.PveSolo,
                TrialJournalEntryId = 401u,
                TrialVowMask = TrialJournalModels.VowBanControl | TrialJournalModels.VowSingleArtifact,
                TrialNewVowBest = true,
                TotalRounds = 7u,
            };
            string vowFormatted = TrialJournalModels.FormatBattleResultSummary(vowResult, snap);
            Assert.That(vowFormatted, Does.Contain("刷新誓约纪录（禁控硬破+单宝轻装）"));

            // 4. 受控 PVE 回放判定：仅试炼志 PVE 放行，常规 PVE 仍禁
            Assert.That(BattleReportClient.CanWatchReplayFromRecovered(vowResult), Is.True);
            var regularPve = new PlayerBattleResult
            {
                BattleId = 88002ul,
                MatchMode = (uint)Match.MatchMode.PveSolo,
                TrialJournalEntryId = 0u,
            };
            Assert.That(BattleReportClient.CanWatchReplayFromRecovered(regularPve), Is.False);

            // 5. #TJC:<card_id># 聊天协议标记格式化、解析与剥离
            var card = new Match.TrialChallengeCardView
            {
                CardId = 900123456789ul,
                BattleId = 88001ul,
                JournalEntryId = 401u,
                VowMask = TrialJournalModels.VowBanControl | TrialJournalModels.VowNoPet,
                TotalRounds = 7u,
                PartySize = 1u,
            };
            string draft = TrialJournalModels.FormatChallengeCardChatDraft(card, snap);
            Assert.That(draft.Length, Is.LessThanOrEqualTo(120));
            Assert.That(TrialJournalModels.TryParseChallengeCardId(draft, out ulong parsedId), Is.True);
            Assert.That(parsedId, Is.EqualTo(900123456789ul));
            string stripped = TrialJournalModels.StripChallengeCardTag(draft);
            Assert.That(stripped, Does.Not.Contain("#TJC:"));
            Assert.That(stripped, Does.Contain("八景岛 · 风眼引风"));
            Assert.That(TrialJournalModels.TryParseChallengeCardId("普通闲聊 #TJC:abc#", out _), Is.False);
            Assert.That(TrialJournalModels.TryParseChallengeCardId("普通闲聊 #TJC:0#", out _), Is.False);
        }

        [Test]
        public void BattleReportClient_TrialChallengeCardCreateGetAndSharedReplayFlow()
        {
            var net = new FakeTransport();
            object connection = new object();
            var sampleCard = new Match.TrialChallengeCardView
            {
                CardId = 777001ul,
                OwnerPlayerId = 9101ul,
                BattleId = 66001ul,
                JournalEntryId = 401u,
                VowMask = TrialJournalModels.VowBanControl,
                TotalRounds = 8u,
                PartySize = 1u,
            };
            int getCalls = 0;
            net.Responder = (msgId, _) =>
            {
                if (msgId == MatchServiceRpc.CreateTrialChallengeCardMessageId)
                {
                    return (new Match.CreateTrialChallengeCardResponse { Card = sampleCard.Clone() }, null);
                }
                if (msgId == MatchServiceRpc.GetTrialChallengeCardMessageId)
                {
                    getCalls++;
                    return (new Match.GetTrialChallengeCardResponse { Card = sampleCard.Clone() }, null);
                }
                if (msgId == MatchServiceRpc.GetSharedTrialReplayMessageId)
                {
                    return (new Match.GetSharedTrialReplayResponse
                    {
                        ErrorMessage = new TipInfoMessage { Id = (uint)match_error.KMatchBattleReportNotFound },
                    }, null);
                }
                return (null, "unexpected");
            };

            using var reports = new BattleReportClient(net, () => connection);
            Match.TrialChallengeCardView created = null;
            Assert.That(reports.CreateTrialChallengeCard(66001ul, c => created = c, _ => Assert.Fail("unexpected error")), Is.True);
            net.Flush();
            Assert.That(created?.CardId, Is.EqualTo(777001ul));
            Assert.That(reports.TryGetCachedChallengeCard(777001ul, out var cached), Is.True);
            Assert.That(cached.VowMask, Is.EqualTo(TrialJournalModels.VowBanControl));

            // 命中缓存不应重复发 GetTrialChallengeCard RPC
            Match.TrialChallengeCardView loaded = null;
            Assert.That(reports.GetTrialChallengeCard(777001ul, c => loaded = c, _ => Assert.Fail("unexpected error")), Is.True);
            Assert.That(loaded?.CardId, Is.EqualTo(777001ul));
            Assert.That(getCalls, Is.EqualTo(0));

            // 未缓存 card_id 触发远端拉取
            Assert.That(reports.GetTrialChallengeCard(777002ul, c => loaded = c, _ => Assert.Fail("unexpected error")), Is.True);
            net.Flush();
            Assert.That(getCalls, Is.EqualTo(1));

            // 共享回放缺失时触发友好错误回调而非静默吞错
            string replayErr = null;
            reports.OnError += e => replayErr = e;
            Assert.That(reports.FetchSharedTrialReplay(777001ul), Is.True);
            net.Flush();
            Assert.That(replayErr, Does.Contain("过期"));
        }

        [Test]
        public void V3_InBattleHudVol4TelegraphQuickParryAndVowControlSkillBlock()
        {
            // 1. 卷四风眼强袭（IntentKind=10）触发一键化劲防御与专属战报标题
            Assert.That(CoreGameplayLoopModels.TelegraphIntentTitle(CoreGameplayLoopModels.TelegraphKindWildWindGust), Is.EqualTo("风眼强袭"));
            var windState = new BattleStateS2C
            {
                RoundIndex = 3u,
                Outcome = eBattleOutcome.BattleOutcomeOngoing,
                BossTrialKind = CoreGameplayLoopModels.BossTrialKindWildWind,
                TrialVowMask = TrialJournalModels.VowBanControl | TrialJournalModels.VowNoPet,
            };
            windState.Actors.Add(new BattleActorState { ActorId = 9101ul, ActorType = eBattleActorType.BattleActorTypePlayer });
            windState.PendingActorIds.Add(9101ul);
            windState.Telegraphs.Add(new BattleTelegraphEntry
            {
                SourceActorId = 5001ul,
                TargetActorId = 9101ul,
                ExecuteRound = 3u,
                IntentKind = CoreGameplayLoopModels.TelegraphKindWildWindGust,
                Status = 2u,
            });
            var windHud = CoreGameplayLoopModels.BuildBattleCoreHud(windState);
            Assert.That(windHud.IsBossTrial, Is.True);
            Assert.That(windHud.BossPhaseBannerText, Does.Contain("风眼引风"));
            var parryActors = CoreGameplayLoopModels.CollectQuickParryDefendActorIds(9101ul, windState);
            Assert.That(parryActors, Is.EqualTo(new[] { 9101ul }));

            // 2. 碧渊水宫阳钟/阴鼓与月汐湾涨潮/退潮回合相位与服务端完全一致
            Assert.That(CoreGameplayLoopModels.IsWildBellYangRound(1u), Is.True);
            Assert.That(CoreGameplayLoopModels.IsWildBellYangRound(3u), Is.False);
            Assert.That(CoreGameplayLoopModels.IsWildTideEbbRound(2u), Is.False);
            Assert.That(CoreGameplayLoopModels.IsWildTideEbbRound(3u), Is.True);
            var tideHud = CoreGameplayLoopModels.BuildBattleCoreHud(new BattleStateS2C
            {
                RoundIndex = 3u,
                BossTrialKind = CoreGameplayLoopModels.BossTrialKindWildTide,
            });
            Assert.That(tideHud.BossPhaseBannerText, Does.Contain("退潮窗口"));

            // 3. 试炼誓约「禁控硬破」客户端技能栏前置拦截（与 C++ TurnBattleEngine::IsControlSkill 同口径）
            var ctrlSkill = new SkillTable { Id = 10010u };
            ctrlSkill.Effect.Add(9001u);
            var dmgSkill = new SkillTable { Id = 10011u };
            dmgSkill.Effect.Add(9002u);
            var ctrlBuff = new BuffTable { Id = 9001u, BattleOp = 13u, StateKind = 1u };
            var dmgBuff = new BuffTable { Id = 9002u, BattleOp = 1u, StateKind = 0u };
            Func<uint, SkillTable> sLookup = id => id == 10010u ? ctrlSkill : (id == 10011u ? dmgSkill : null);
            Func<uint, BuffTable> bLookup = id => id == 9001u ? ctrlBuff : (id == 9002u ? dmgBuff : null);

            Assert.That(BattleSkillBanModels.IsSkillBlockedByTrialVow(windState, 10010u, sLookup, bLookup), Is.True);
            Assert.That(BattleSkillBanModels.IsSkillBlockedByTrialVow(windState, 10011u, sLookup, bLookup), Is.False);
            var noVowState = new BattleStateS2C { TrialVowMask = 0u };
            Assert.That(BattleSkillBanModels.IsSkillBlockedByTrialVow(noVowState, 10010u, sLookup, bLookup), Is.False);
        }

        [Test]
        public void Client_QueuesForcedRefreshDuringPendingSnapshotAndEmitsGrandmasterAppearance()
        {
            var net = new FakeTransport();
            object connection = new object();
            ulong rev = 1;
            net.Responder = (msgId, _) =>
            {
                if (msgId == TrialJournalMessageIds.Generated.ClaimMilestone)
                {
                    return (new ClaimTrialJournalMilestoneResponse
                    {
                        GrantedAppearanceId = "20_star_formation_master_girl",
                        Snapshot = new TrialJournalSnapshot { Available = true, CanOperate = true, StateRevision = ++rev },
                    }, null);
                }
                return (new GetTrialJournalResponse
                {
                    Snapshot = new TrialJournalSnapshot { Available = true, CanOperate = true, StateRevision = rev },
                }, null);
            };

            float now = 10f;
            using var client = new TrialJournalClient(net, () => connection, () => now);
            client.RequestSnapshot(force: true);
            Assert.That(client.SnapshotPending, Is.True);
            // 战斗结算触发的强制刷新遇到在途拉取时排队，完成后由 Tick 补发一次。
            rev = 2;
            client.HandleBattleSettled();
            net.Flush();
            Assert.That(client.Snapshot?.StateRevision, Is.EqualTo(1ul));
            client.Tick();
            net.Flush();
            Assert.That(client.Snapshot?.StateRevision, Is.EqualTo(2ul));

            TrialMilestoneClaimedEvent? claimed = null;
            client.MilestoneClaimed += ev => claimed = ev;
            now = 20f;
            Assert.That(client.ClaimMilestone(901), Is.True);
            net.Flush();
            Assert.That(claimed.HasValue, Is.True);
            Assert.That(claimed.Value.GrantedAppearanceId, Is.EqualTo("20_star_formation_master_girl"));
        }

        [Test]
        public void UiState_EvaluatesAllNineAcceptancePhasesAndEntryRouting()
        {
            // 1. FirstLoad（无快照同步中，不假显示 0/24）
            var loading = TrialJournalUiState.Evaluate(null, true, false, true, false, "", false,
                TrialJournalNavTab.Overview, 0u);
            Assert.That(loading.Phase, Is.EqualTo(TrialJournalVisualPhase.FirstLoad));
            Assert.That(loading.OnlineWriteEnabled, Is.False);
            Assert.That(loading.SyncPillText, Is.EqualTo("首次同步中"));

            // 构造覆盖三卷 + 双卫同阵（总永久 27 印，18/27 留 9 印容错）+ M901 + 8法宝的完整样例快照
            var snap = new TrialJournalSnapshot
            {
                Available = true,
                CanOperate = true,
                TotalPermanentLitSeals = 0,
                TotalPermanentSeals = 27,
                VoucherBalance = 0,
                BoundSandBalance = 0,
            };
            var ch1 = new TrialJournalChapterView { ChapterId = 1, ChapterName = "卷一 · 破阵试炼", TotalSealCount = 15 };
            var e101 = new TrialJournalEntryView { EntryId = 101, DisplayName = "普通·回春", RuleVersion = 1, RuleVersionName = "首测规则", CanEnter = true };
            ch1.Entries.Add(e101);
            var ch3 = new TrialJournalChapterView { ChapterId = 3, SeasonId = 1, ChapterName = "卷三 · 季游奇谈", TotalSealCount = 6 };
            ch3.Entries.Add(new TrialJournalEntryView { EntryId = 302, DisplayName = "物法易势 5V5", SourceKind = 3, RuleVersion = 1, RuleVersionName = "首测规则", CanEnter = true });
            snap.Chapters.Add(ch1);
            snap.Chapters.Add(ch3);
            var m901 = new TrialJournalMilestoneView
            {
                MilestoneId = 901,
                ScopeChapterId = 0,
                RequiredSeals = 18,
                GrantAppearanceId = "20_star_formation_master_girl",
                GrantAppearanceName = "星阵宗师",
            };
            snap.Milestones.Add(m901);
            snap.RedeemableArtifacts.Add(new TrialRedeemableArtifactOption { ItemConfigId = 3301, ArtifactEffectId = 1, Name = "破阵刃" });

            // 2. Empty（真空档）
            var empty = TrialJournalUiState.Evaluate(snap, false, false, true, false, "", false,
                TrialJournalNavTab.Overview, 101u);
            Assert.That(empty.Phase, Is.EqualTo(TrialJournalVisualPhase.Empty));
            Assert.That(empty.RedeemButtonEnabled, Is.False);

            // 3. InProgress（进行中）+ 4. Refreshing（刷新保留旧图）
            snap.TotalPermanentLitSeals = 7;
            snap.VoucherBalance = 1;
            var active = TrialJournalUiState.Evaluate(snap, false, false, true, false, "", false,
                TrialJournalNavTab.Overview, 101u);
            Assert.That(active.Phase, Is.EqualTo(TrialJournalVisualPhase.InProgress));
            Assert.That(active.RedeemButtonEnabled, Is.True);
            Assert.That(active.GrandmasterDesc, Does.Contain("7/27"));
            Assert.That(active.GrandmasterDesc, Does.Contain("留 9 印容错"));

            var refreshing = TrialJournalUiState.Evaluate(snap, true, false, true, false, "", false,
                TrialJournalNavTab.Overview, 101u);
            Assert.That(refreshing.Phase, Is.EqualTo(TrialJournalVisualPhase.Refreshing));
            Assert.That(refreshing.PermanentLitSeals, Is.EqualTo(7u));

            // 5. GrandmasterReached（18 印宗师）
            snap.TotalPermanentLitSeals = 18;
            m901.CurrentSeals = 18;
            m901.Reached = true;
            m901.Claimable = true;
            var gm = TrialJournalUiState.Evaluate(snap, false, false, true, false, "", false,
                TrialJournalNavTab.Overview, 101u);
            Assert.That(gm.Phase, Is.EqualTo(TrialJournalVisualPhase.GrandmasterReached));
            Assert.That(gm.GrandmasterClaimable, Is.True);
            Assert.That(gm.GrandmasterButtonText, Is.EqualTo("领取宗师里程碑"));

            // 6. SeasonalIsolated（卷三季游隔离）
            var seasonal = TrialJournalUiState.Evaluate(snap, false, false, true, false, "", false,
                TrialJournalNavTab.Seasonal, 302u);
            Assert.That(seasonal.Phase, Is.EqualTo(TrialJournalVisualPhase.SeasonalIsolated));
            Assert.That(seasonal.AlertText, Does.Contain("永久卷 27 印"));
            Assert.That(TrialJournalUiState.ResolveTabForEntry(snap, 302u), Is.EqualTo(TrialJournalNavTab.Seasonal));
            Assert.That(TrialJournalUiState.FilterEntries(snap, TrialJournalNavTab.Seasonal).Count, Is.EqualTo(1));

            // 7. RuleRevision（规则换代旧版留档）
            e101.HasLegacyRecordOnly = true;
            var ruleRev = TrialJournalUiState.Evaluate(snap, false, false, true, false, "", false,
                TrialJournalNavTab.Breakthrough, 101u);
            Assert.That(ruleRev.Phase, Is.EqualTo(TrialJournalVisualPhase.RuleRevision));

            // 8. BagFullError（背包满保留凭券）
            e101.HasLegacyRecordOnly = false;
            var bagFull = TrialJournalUiState.Evaluate(snap, false, false, true, false,
                TrialJournalModels.DescribeTip((uint)trial_journal_error.KTrialJournalBagFull), true,
                TrialJournalNavTab.Overview, 101u);
            Assert.That(bagFull.Phase, Is.EqualTo(TrialJournalVisualPhase.BagFullError));
            Assert.That(bagFull.VoucherBalance, Is.EqualTo(1ul));
            Assert.That(bagFull.RedeemButtonText, Is.EqualTo("整理背包后重试"));

            // 9. Disconnected（断线保留旧图且禁用写入）
            var disconn = TrialJournalUiState.Evaluate(snap, false, false, false, true,
                TrialJournalClient.RecoveryMessage, true, TrialJournalNavTab.Overview, 101u);
            Assert.That(disconn.Phase, Is.EqualTo(TrialJournalVisualPhase.Disconnected));
            Assert.That(disconn.OnlineWriteEnabled, Is.False);
            Assert.That(disconn.RedeemButtonEnabled, Is.False);
            Assert.That(disconn.PermanentLitSeals, Is.EqualTo(18u));
        }

        [Test]
        public void TrialJournalV4_SeasonS2ToS10LabelsAndHonorBadgeFallbacks()
        {
            var expectedSeasonNames = new (uint sid, string name, string honorPath)[]
            {
                (1u, "破阵之季", "UI/SeasonHonors/honor_s1_pozhen"),
                (2u, "玄脉之季", "UI/SeasonHonors/honor_s2_xuanmai"),
                (3u, "离火之季", "UI/SeasonHonors/honor_s3_lihuo"),
                (4u, "青木之季", "UI/SeasonHonors/honor_s4_qingmu"),
                (5u, "玄水之季", "UI/SeasonHonors/honor_s5_xuanshui"),
                (6u, "厚土之季", "UI/SeasonHonors/honor_s6_houtu"),
                (7u, "庚金之季", "UI/SeasonHonors/honor_s7_gengjin"),
                (8u, "太阴之季", "UI/SeasonHonors/honor_s8_taiyin"),
                (9u, "天雷之季", "UI/SeasonHonors/honor_s9_tianlei"),
                (10u, "混元之季", "UI/SeasonHonors/honor_s10_hunyuan"),
            };

            foreach (var (sid, name, honorPath) in expectedSeasonNames)
            {
                Assert.That(TrialJournalModels.SeasonDisplayName(sid), Is.EqualTo(name));
                string key = $"badge_season_s{sid}_trial";
                Assert.That(TrialJournalModels.TryGetSeasonHonorBadgeFallback(key, out string path), Is.True);
                Assert.That(path, Is.EqualTo(honorPath));

                var snap = new TrialJournalSnapshot
                {
                    Available = true,
                    CanOperate = true,
                    TotalPermanentSeals = 36u,
                };
                snap.Chapters.Add(new TrialJournalChapterView
                {
                    ChapterId = TrialJournalModels.ChapterSeasonal,
                    SeasonId = sid,
                    ChapterName = "卷三 · 季游奇谈",
                    TotalSealCount = 6u,
                });
                var view = TrialJournalUiState.Evaluate(snap, false, false, true, false, "", false, TrialJournalNavTab.Seasonal, 0u);
                Assert.That(view.SeasonLabel, Is.EqualTo($"{name} · 卷三 · 季游奇谈"));
                Assert.That(view.Phase, Is.EqualTo(TrialJournalVisualPhase.SeasonalIsolated));
                uint rescueEntryId = sid == 1u ? 301u : 3000u + sid * 10u + 1u;
                Assert.That(TrialJournalModels.TryGetEntryMonsterIds(rescueEntryId, out uint bossId, out uint guardId), Is.True);
                Assert.That((bossId, guardId), Is.EqualTo((101u, 102u)));
            }
            Assert.That(TrialJournalModels.TryGetSeasonHonorBadgeFallback("badge_trial_sword", out _), Is.False);
        }

        [Test]
        public void TrialJournalV2AndEquipLoadoutV1Sprites_AllLoadCleanly()
        {
            var tjExpected = new (string key, UnityEngine.Vector4 border)[]
            {
                ("main_frame", new UnityEngine.Vector4(180f, 160f, 180f, 150f)),
                ("title_plate", UnityEngine.Vector4.zero),
                ("close_btn", UnityEngine.Vector4.zero),
                ("rail_tab_idle", new UnityEngine.Vector4(18f, 18f, 18f, 18f)),
                ("rail_tab_selected", new UnityEngine.Vector4(18f, 18f, 18f, 18f)),
                ("metric_card", new UnityEngine.Vector4(20f, 20f, 20f, 20f)),
                ("section_plate", new UnityEngine.Vector4(64f, 0f, 64f, 0f)),
                ("entry_row_idle", new UnityEngine.Vector4(22f, 22f, 22f, 22f)),
                ("entry_row_selected", new UnityEngine.Vector4(24f, 24f, 24f, 24f)),
                ("portrait_frame", UnityEngine.Vector4.zero),
                ("seal_medallion_dim", UnityEngine.Vector4.zero),
                ("seal_medallion_lit", UnityEngine.Vector4.zero),
                ("artifact_tray", new UnityEngine.Vector4(20f, 20f, 20f, 20f)),
                ("artifact_slot_idle", new UnityEngine.Vector4(14f, 14f, 14f, 14f)),
                ("artifact_slot_selected", new UnityEngine.Vector4(14f, 14f, 14f, 14f)),
                ("grandmaster_pedestal", UnityEngine.Vector4.zero),
                ("btn_primary", new UnityEngine.Vector4(44f, 16f, 44f, 16f)),
                ("btn_secondary", new UnityEngine.Vector4(40f, 16f, 40f, 16f)),
                ("modal_panel", new UnityEngine.Vector4(28f, 28f, 28f, 28f)),
                ("battle_result_hint_bar", new UnityEngine.Vector4(22f, 22f, 22f, 22f)),
            };
            foreach (var (key, border) in tjExpected)
            {
                var sp = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Ugui/TrialJournalV2/" + key);
                Assert.That(sp, Is.Not.Null, "missing TrialJournalV2 sprite: " + key);
                Assert.That(sp.border, Is.EqualTo(border), "unexpected border for TrialJournalV2/" + key);
            }

            var elExpected = new (string key, UnityEngine.Vector4 border)[]
            {
                ("panel_equip_loadout_main", UnityEngine.Vector4.zero),
                ("panel_equip_loadout_unlock", UnityEngine.Vector4.zero),
                ("icon_loadout_expand_talisman", UnityEngine.Vector4.zero),
                ("tab_loadout_active", new UnityEngine.Vector4(32f, 0f, 32f, 0f)),
                ("tab_loadout_unlocked", new UnityEngine.Vector4(32f, 0f, 32f, 0f)),
                ("tab_loadout_locked", new UnityEngine.Vector4(32f, 0f, 32f, 0f)),
                ("badge_loadout_in_use", new UnityEngine.Vector4(16f, 0f, 16f, 0f)),
                ("badge_loadout_slot_missing", UnityEngine.Vector4.zero),
            };
            foreach (var (key, border) in elExpected)
            {
                var sp = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Ugui/EquipLoadoutV1/" + key);
                Assert.That(sp, Is.Not.Null, "missing EquipLoadoutV1 sprite: " + key);
                Assert.That(sp.border, Is.EqualTo(border), "unexpected border for EquipLoadoutV1/" + key);
            }

            var tjV3Expected = new (string path, UnityEngine.Vector4 border)[]
            {
                ("UI/Ugui/TrialJournalV3/wild_boss_btn", new UnityEngine.Vector4(24f, 12f, 24f, 12f)),
                ("UI/Ugui/TrialJournalV3/vow_chip_idle", new UnityEngine.Vector4(16f, 12f, 16f, 12f)),
                ("UI/Ugui/TrialJournalV3/vow_chip_active", new UnityEngine.Vector4(16f, 12f, 16f, 12f)),
                ("UI/Ugui/TrialJournalV3/vow_icon_ban_control", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournalV3/vow_icon_single_artifact", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournalV3/vow_icon_no_pet", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournalV3/vow_icon_tight_rounds", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournalV3/challenge_card_panel", new UnityEngine.Vector4(24f, 20f, 24f, 20f)),
                ("UI/Ugui/TrialJournal/Badges/badge_trial_three_realms", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s2_trial", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s3_trial", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s4_trial", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s5_trial", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s6_trial", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s7_trial", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s8_trial", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s9_trial", UnityEngine.Vector4.zero),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s10_trial", UnityEngine.Vector4.zero),
            };
            foreach (var (path, border) in tjV3Expected)
            {
                var sp = UnityEngine.Resources.Load<UnityEngine.Sprite>(path);
                Assert.That(sp, Is.Not.Null, "missing TrialJournalV3 sprite: " + path);
                Assert.That(sp.border, Is.EqualTo(border), "unexpected border for " + path);
            }
        }
    }
}
