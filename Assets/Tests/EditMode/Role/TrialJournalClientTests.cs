using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.TrialJournal;
using MmorpgClient.Net;
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
    }
}
