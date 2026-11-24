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
    }
}
