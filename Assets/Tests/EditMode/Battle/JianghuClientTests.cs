using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Jianghu;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class JianghuClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 8001;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;

            public readonly List<(uint msgId, IMessage req)> Calls = new();
            private readonly Queue<Action> _pending = new();
            private readonly Dictionary<uint, Action<MessageContent>> _notifies = new();
            public Func<uint, IMessage, (IMessage resp, string err)> Responder;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler)
                => _notifies[messageId] = handler;

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

            public void Push(uint messageId, IMessage payload)
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

            public void TriggerDisconnect()
            {
                IsReady = false;
                Disconnected?.Invoke();
            }
        }

        [Test]
        public void ProfileAndCompliantWording_MonotonicCountdownAndZeroBannedTerms()
        {
            var net = new FakeTransport();
            float now = 10f;
            object conn = new object();
            using var client = new JianghuClient(net, () => conn, () => now);

            net.Responder = (msgId, _) =>
            {
                if (msgId == SceneJianghuClientPlayerRpc.GetJianghuProfileMessageId)
                {
                    return (new global::GetJianghuProfileResponse
                    {
                        Profile = new global::JianghuProfileView
                        {
                            ServerTimeMs = 1790000000000UL,
                            HasActiveDefeatMark = true,
                            DefeatMarkExpiresAtMs = 1790000120000UL, // +120s
                            DefeatMarkWinnerId = 9001,
                            DefeatMarkWinnerName = "青云剑修",
                            SilverBalance = 2500,
                            GoldWritItemCount = 2,
                            ClearCharmItemCount = 1,
                            SilverWritCost = 500,
                            SilverClearCost = 1000,
                        },
                    }, null);
                }
                throw new InvalidOperationException("unexpected msg " + msgId);
            };

            client.RequestProfile(true);
            net.Flush();

            Assert.That(client.Profile, Is.Not.Null);
            Assert.That(JianghuModels.FormatDefeatMark(client.Profile), Is.EqualTo("惜败于 青云剑修"));
            Assert.That(JianghuModels.RemainingSeconds(client.Profile.DefeatMarkExpiresAtMs, client.EstimatedServerNowMs), Is.EqualTo(120u));

            now += 45f;
            Assert.That(JianghuModels.RemainingSeconds(client.Profile.DefeatMarkExpiresAtMs, client.EstimatedServerNowMs), Is.EqualTo(75u));
            Assert.That(JianghuModels.FormatMinutesSeconds(75u), Is.EqualTo("01:15"));

            // Ensure all localized messages adhere strictly to soft wuxia compliance.
            string[] banned = { "杀人", "强杀", "监狱", "认怂" };
            for (uint tip = 40000; tip <= 40013; tip++)
            {
                string text = JianghuModels.DescribeTip(tip);
                foreach (string word in banned)
                    Assert.That(text.Contains(word), Is.False, $"Tip {tip} contained banned term {word}");
            }
            foreach (string code in new[] { "matched", "declined", "expired", "member_declined", "team_changed" })
            {
                string text = JianghuModels.FormatStatusReason(code);
                foreach (string word in banned)
                    Assert.That(text.Contains(word), Is.False, $"Reason {code} contained banned term {word}");
            }
        }

        [Test]
        public void ClearDefeatMark_RetriesOnceWithSameKeyOnTimeout_ThenAppliesProfileAndBag()
        {
            var net = new FakeTransport();
            float now = 100f;
            object conn = new object();
            using var client = new JianghuClient(net, () => conn, () => now);

            int clearAttempts = 0;
            global::BagInfo receivedBag = null;
            client.BagReceived += b => receivedBag = b;

            net.Responder = (msgId, req) =>
            {
                if (msgId == SceneJianghuClientPlayerRpc.GetJianghuProfileMessageId)
                {
                    return (new global::GetJianghuProfileResponse
                    {
                        Profile = new global::JianghuProfileView
                        {
                            ServerTimeMs = 1790000000000UL,
                            HasActiveDefeatMark = true,
                            DefeatMarkExpiresAtMs = 1790001800000UL,
                            ClearCharmItemCount = 1,
                        },
                    }, null);
                }
                if (msgId == SceneJianghuClientPlayerRpc.ClearJianghuDefeatMarkMessageId)
                {
                    clearAttempts++;
                    if (clearAttempts == 1)
                        return (null, OutstandingCalls.TimeoutDetail);
                    return (new global::ClearJianghuDefeatMarkResponse
                    {
                        Profile = new global::JianghuProfileView
                        {
                            ServerTimeMs = 1790000005000UL,
                            HasActiveDefeatMark = false,
                            HasActiveClearBadge = true,
                            ClearBadgeExpiresAtMs = 1790001805000UL,
                            ClearCharmItemCount = 0,
                        },
                        Bag = new global::BagInfo(),
                        Replayed = false,
                    }, null);
                }
                throw new InvalidOperationException("unexpected msg " + msgId);
            };

            client.RequestProfile(true);
            net.Flush();

            bool? ok = null;
            Assert.That(client.ClearDefeatMark(global::JianghuClearMarkMode.ItemCharm, r => ok = r), Is.True);
            net.Flush();

            Assert.That(clearAttempts, Is.EqualTo(2));
            var req1 = (global::ClearJianghuDefeatMarkRequest)net.Calls[1].req;
            var req2 = (global::ClearJianghuDefeatMarkRequest)net.Calls[2].req;
            Assert.That(req1.ClientRequestId, Is.Not.EqualTo(0UL));
            Assert.That(req2.ClientRequestId, Is.EqualTo(req1.ClientRequestId));
            Assert.That(ok, Is.True);
            Assert.That(client.Profile.HasActiveDefeatMark, Is.False);
            Assert.That(client.Profile.HasActiveClearBadge, Is.True);
            Assert.That(receivedBag, Is.Not.Null);
            Assert.That(client.RequiresReconnect, Is.False);
        }

        [Test]
        public void ClearDefeatMark_SecondTimeout_SetsRequiresReconnectFence()
        {
            var net = new FakeTransport();
            float now = 100f;
            using var client = new JianghuClient(net, () => net, () => now);

            net.Responder = (msgId, _) => (null, OutstandingCalls.TimeoutDetail);
            bool? ok = null;
            Assert.That(client.ClearDefeatMark(global::JianghuClearMarkMode.Silver, r => ok = r), Is.True);
            net.Flush();

            Assert.That(ok, Is.False);
            Assert.That(client.RequiresReconnect, Is.True);
            Assert.That(client.StatusIsError, Is.True);
            // Subsequent clear attempt blocked until reconnect.
            now += 2f;
            Assert.That(client.ClearDefeatMark(global::JianghuClearMarkMode.Silver), Is.False);
        }

        [Test]
        public void IncomingPushInvite_SquadConfirm_AndStatusMatchedFlow()
        {
            var net = new FakeTransport { PlayerId = 8002 };
            float now = 50f;
            using var client = new JianghuClient(net, () => net, () => now);

            global::Match.JianghuDuelInviteView popped = null;
            ulong matchedBattle = 0;
            client.InvitePopupArrived += inv => popped = inv;
            client.DuelMatched += id => matchedBattle = id;

            var invite = new global::Match.JianghuDuelInviteView
            {
                DuelInviteId = 55001,
                WritTier = global::Match.JianghuWritTier.GoldItem,
                TeamSize = 3,
                State = global::Match.JianghuDuelInviteState.AwaitingTargetTeam,
                ChallengerCaptainId = 7001,
                ChallengerCaptainName = "赤焰堂主",
                TargetCaptainId = 8001,
                TargetCaptainName = "碧落道人",
                ServerTimeMs = 1790000000000UL,
                ConfirmDeadlineMs = 1790000030000UL,
            };
            invite.TargetMembers.Add(new global::Match.JianghuDuelMemberView { PlayerId = 8001, Name = "碧落道人", Confirmed = true });
            invite.TargetMembers.Add(new global::Match.JianghuDuelMemberView { PlayerId = 8002, Name = "清风客", Confirmed = false });
            invite.TargetMembers.Add(new global::Match.JianghuDuelMemberView { PlayerId = 8003, Name = "沧浪子", Confirmed = false });

            net.Push(MatchServiceRpc.NotifyJianghuDuelInviteMessageId, new global::Match.JianghuDuelInviteS2C { Invite = invite });

            Assert.That(popped, Is.Not.Null);
            Assert.That(client.PendingActionInvite, Is.Not.Null);
            Assert.That(JianghuModels.InviteNeedsMyAction(client.PendingActionInvite, 8002),
                Is.EqualTo(JianghuInviteActionKind.MemberConfirm));

            // Confirm membership
            net.Responder = (msgId, req) =>
            {
                if (msgId == MatchServiceRpc.ConfirmJianghuDuelMemberMessageId)
                {
                    var confirmed = invite.Clone();
                    confirmed.TargetMembers[1].Confirmed = true;
                    return (new global::Match.ConfirmJianghuDuelMemberResponse { Invite = confirmed }, null);
                }
                if (msgId == SceneJianghuClientPlayerRpc.GetJianghuProfileMessageId)
                    return (new global::GetJianghuProfileResponse { Profile = new global::JianghuProfileView() }, null);
                throw new InvalidOperationException("unexpected msg " + msgId);
            };

            Assert.That(client.ConfirmTeamMember(55001, true), Is.True);
            net.Flush();
            Assert.That(client.PendingActionInvite, Is.Null);

            // Receive matched status push
            var matched = invite.Clone();
            matched.State = global::Match.JianghuDuelInviteState.Matched;
            matched.BattleId = 998877;
            net.Push(MatchServiceRpc.NotifyJianghuDuelStatusMessageId, new global::Match.JianghuDuelStatusS2C
            {
                Invite = matched,
                ReasonCode = "matched",
            });
            net.Flush();

            Assert.That(matchedBattle, Is.EqualTo(998877UL));
            Assert.That(client.ActiveInvites.Count, Is.EqualTo(0));
        }

        [Test]
        public void GrudgeListStreakBadge_DismissAndGuildWall_WorkAndFenceOnDisconnect()
        {
            var net = new FakeTransport { PlayerId = 8001 };
            float now = 10f;
            object conn = new object();
            using var client = new JianghuClient(net, () => conn, () => now);

            net.Responder = (msgId, req) =>
            {
                if (msgId == MatchServiceRpc.ListJianghuGrudgesMessageId)
                {
                    var resp = new global::Match.ListJianghuGrudgesResponse { ServerTimeMs = 1790000000000UL };
                    resp.Grudges.Add(new global::Match.JianghuGrudgeEntry
                    {
                        PeerPlayerId = 9001,
                        PeerName = "青云剑修",
                        Wins = 4,
                        Losses = 1,
                        WinStreak = 3,
                        PeerOnline = true,
                    });
                    resp.Grudges.Add(new global::Match.JianghuGrudgeEntry
                    {
                        PeerPlayerId = 9002,
                        PeerName = "紫电真人",
                        Wins = 0,
                        Losses = 2,
                        LoseStreak = 2,
                        PeerOnline = false,
                    });
                    return (resp, null);
                }
                if (msgId == MatchServiceRpc.DismissJianghuGrudgeMessageId)
                    return (new global::Match.DismissJianghuGrudgeResponse(), null);
                if (msgId == GuildServiceRpc.GetGuildJianghuGrudgeWallMessageId)
                {
                    var wall = new global::Guildpb.GetGuildJianghuGrudgeWallResponse { GuildId = 601 };
                    wall.Rivals.Add(new global::Guildpb.GuildJianghuRivalSummaryView
                    {
                        RivalGuildId = 602,
                        RivalGuildName = "苍穹宗",
                        Wins = 2,
                        Losses = 1,
                    });
                    return (wall, null);
                }
                throw new InvalidOperationException("unexpected msg " + msgId);
            };

            client.RequestGrudges(true);
            client.RequestGuildWall(true);
            net.Flush();

            Assert.That(client.Grudges.Count, Is.EqualTo(2));
            Assert.That(JianghuModels.FormatStreakBadge(client.Grudges[0].WinStreak, client.Grudges[0].LoseStreak), Is.EqualTo("宿敌连胜 ×3"));
            Assert.That(JianghuModels.FormatStreakBadge(client.Grudges[1].WinStreak, client.Grudges[1].LoseStreak), Is.EqualTo("待雪耻"));
            Assert.That(client.GuildWall.Rivals.Count, Is.EqualTo(1));

            Assert.That(client.DismissGrudge(9001), Is.True);
            net.Flush();
            Assert.That(client.Grudges.Count, Is.EqualTo(1));
            Assert.That(client.Grudges[0].PeerPlayerId, Is.EqualTo(9002UL));

            // In-flight request dropped when disconnected before flush.
            now += 2f;
            client.RequestGrudges(true);
            net.TriggerDisconnect();
            net.Flush();
            Assert.That(client.Grudges.Count, Is.EqualTo(0));
            Assert.That(client.GuildWall, Is.Null);
        }
    }
}
