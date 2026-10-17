using System;
using System.Collections.Generic;
using System.Linq;
using Friendpb;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Game.Social;
using MmorpgClient.Game.Sync;
using MmorpgClient.Game.Team;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 落地恢复客户端(服务端 docs/design/landing-recovery-sync-digest.md §4.3 第 6 条)。
    /// 假传输照 TeamFakeTransport 的写法,缓存用内存实现(经 SyncDigestCodec 编解码,顺带覆盖序列化)。
    /// </summary>
    public sealed class LandingSyncClientTests
    {
        private const ulong Player = 10;
        private float _now;
        private bool _inGame;
        private SyncFakeTransport _net;
        private MemorySyncDigestStore _store;
        private LandingSyncClient _sync;

        [SetUp]
        public void SetUp()
        {
            _now = 0f;
            _inGame = true;
            _net = new SyncFakeTransport { PlayerId = Player };
            _store = new MemorySyncDigestStore();
            _sync = new LandingSyncClient(_net, () => _net.Identity, () => _inGame, () => _now, _store);
        }

        [TearDown]
        public void TearDown() => _sync.Dispose();

        // ── 接线 ────────────────────────────────────────────────────────────

        [Test]
        public void Construct_RegistersBothMessageIdsAndSendsNothing()
        {
            Assert.That(_net.Notifies.Keys,
                Is.EquivalentTo(new[] { MessageIds.NotifySyncDigest, MessageIds.GetSyncDigest }));
            Assert.That(_net.Sent, Is.Empty, "构造不发请求");
            Assert.That(_sync.Domains, Has.None.EqualTo(SyncDomain.Unspecified));
            Assert.That(_sync.Domains, Does.Contain(SyncDomain.FriendRequest));
        }

        [Test]
        public void NotInGame_SendsNothing()
        {
            _inGame = false;
            _sync.Tick();
            _net.IsReady = false;
            _inGame = true;
            _sync.Tick();
            Assert.That(DigestRequests, Is.EqualTo(0));
        }

        // ── 首登 / 代次 / 逐域比对 ──────────────────────────────────────────

        [Test]
        public void FirstLanding_WithoutCache_SendsOnceAndMarksEveryDomainDirty()
        {
            _sync.Tick();
            Assert.That(DigestRequests, Is.EqualTo(1));
            Assert.That(_sync.RequestInFlight, Is.True);
            Assert.That(DirtyDomains(), Is.Empty, "摘要到达前不标脏");

            PushDigest(100, (SyncDomain.Team, 3));

            Assert.That(_sync.HasDigest, Is.True);
            Assert.That(_sync.RequestInFlight, Is.False);
            Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains), "没有缓存:全部域拉一次");
            var saved = _store.Peek(Player);
            Assert.That(saved.Generation, Is.EqualTo(100UL));
            Assert.That(saved.Counts, Is.Empty, "计数要等消费者拉取成功后才写");

            _sync.Tick();
            Assert.That(DigestRequests, Is.EqualTo(1), "本次落地拿到摘要后不再发");
        }

        [Test]
        public void GenerationChanged_MarksEveryDomainDirtyAndClearsCachedCounts()
        {
            SeedCache(100, 5);
            _sync.Tick();
            PushDigest(200, _sync.Domains.Select(domain => (domain, 5UL)).ToArray());

            Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains), "g 变了:计数相等也要全量拉");
            var saved = _store.Peek(Player);
            Assert.That(saved.Generation, Is.EqualTo(200UL), "g 改记新值");
            Assert.That(saved.Counts, Is.Empty, "旧计数作废");
        }

        [Test]
        public void SameGeneration_MarksOnlyChangedOrUnrecordedDomains()
        {
            // 缓存:除 GUILD_ACTIVITY 之外每个域都有记录;FRIEND_REQUEST=2,TEAM=7,其余 0。
            SeedCache(100, 0, (SyncDomain.FriendRequest, 2UL), (SyncDomain.Team, 7UL));
            _store.RemoveCount(Player, SyncDomain.GuildActivity);
            var notified = new List<SyncPullTicket>();
            using (_sync.RegisterConsumer(SyncDomain.FriendRequest, notified.Add))
            using (_sync.RegisterConsumer(SyncDomain.Team, notified.Add))
            {
                _sync.Tick();
                // TEAM 未变;其余域缺席按 0,与缓存的 0 相等。
                PushDigest(100, (SyncDomain.FriendRequest, 3UL), (SyncDomain.Team, 7UL));

                Assert.That(DirtyDomains(),
                    Is.EquivalentTo(new[] { SyncDomain.FriendRequest, SyncDomain.GuildActivity }),
                    "只拉计数不等的域与本地没有记录的域");

                _sync.Tick();
                Assert.That(notified.Count, Is.EqualTo(1), "未变的 TEAM 不通知");
                Assert.That(notified[0].Domain, Is.EqualTo(SyncDomain.FriendRequest));
                Assert.That(notified[0].VersionReadAtLanding, Is.EqualTo(3UL));
                Assert.That(notified[0].HasVersion, Is.True);

                _sync.Tick();
                Assert.That(notified.Count, Is.EqualTo(1), "同一批标脏只通知一次");
            }
        }

        [Test]
        public void ConsumerRegisteredAfterDigest_IsNotifiedOnNextTick()
        {
            _sync.Tick();
            PushDigest(1);
            int calls = 0;
            using (_sync.RegisterConsumer(SyncDomain.FriendRequest, _ => calls++))
            {
                Assert.That(calls, Is.EqualTo(0), "注册时不同步回调");
                _sync.Tick();
                Assert.That(calls, Is.EqualTo(1));
            }
        }

        // ── 拉取与推进(L-2)───────────────────────────────────────────────

        [Test]
        public void FriendPull_FailureKeepsDirtyAndOnlyPullStartedAfterDigestAdvancesCache()
        {
            SeedCache(100, 0, (SyncDomain.FriendRequest, 1UL));
            using var friends = new FriendClient(_net, () => _net.Identity, () => _inGame);
            using var binding = LandingSyncBindings.BindFriendRequests(_sync, friends);

            // 摘要到达之前就发出的拉取:它读到的数据可能早于摘要,不能拿来确认。
            friends.RefreshPending();
            Assert.That(_net.Calls.Count, Is.EqualTo(1));

            _sync.Tick();
            PushDigest(100, (SyncDomain.FriendRequest, 2UL));
            Assert.That(_sync.IsDirty(SyncDomain.FriendRequest), Is.True);
            _sync.Tick(); // 投递标脏:好友客户端在途,排到当前请求之后
            Assert.That(_net.Calls.Count, Is.EqualTo(1));

            _net.Reply(0, PendingRequests());
            Assert.That(_sync.IsDirty(SyncDomain.FriendRequest), Is.True, "摘要之前发出的拉取不确认");
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "排队的刷新紧接着发出");
            Assert.That(_net.Calls[1].Id, Is.EqualTo(ClientPlayerFriendGetPendingRequestsHandler.MessageId));

            _net.Fail(1, "rpc timeout");
            Assert.That(_sync.IsDirty(SyncDomain.FriendRequest), Is.True, "拉取失败保留脏标");
            Assert.That(CachedCount(SyncDomain.FriendRequest), Is.EqualTo(1UL), "拉取失败不推进版本");

            friends.RefreshPending(); // 玩家打开申请窗口
            _net.Reply(2, PendingRequests(501));
            Assert.That(_sync.IsDirty(SyncDomain.FriendRequest), Is.False);
            Assert.That(CachedCount(SyncDomain.FriendRequest), Is.EqualTo(2UL), "成功后才写成摘要里的值");
            Assert.That(friends.Pending.Count, Is.EqualTo(1), "红点只来自拉回的内容");
        }

        [Test]
        public void TeamProbeBeforeDigest_DoesNotAckButInvitesReadAfterDigestDoes()
        {
            float teamNow = 0f;
            using var team = new TeamClient(_net, () => _net.Identity, null, null, () => teamNow);
            using var binding = LandingSyncBindings.BindTeam(_sync, team);

            team.DrainQueued(false); // 落地探针 GetMyTeam,早于摘要
            Assert.That(_net.Calls.Single().Id, Is.EqualTo(MessageIds.GetMyTeam));

            _sync.Tick();
            PushDigest(100, (SyncDomain.Team, 2UL), (SyncDomain.TeamInvite, 5UL));
            _sync.Tick();

            _net.Reply(0, new TeamResponse { Team = new TeamView() });
            Assert.That(_sync.IsDirty(SyncDomain.Team), Is.True, "探针发出早于摘要,不能推进 TEAM");

            teamNow = 1f;
            team.DrainQueued(false); // 无队玩家随后拉邀请,发出晚于摘要
            Assert.That(_net.Calls[1].Id, Is.EqualTo(MessageIds.ListMyInvites));
            _net.Reply(1, new ListMyInvitesResponse());

            Assert.That(_sync.IsDirty(SyncDomain.TeamInvite), Is.False);
            Assert.That(CachedCount(SyncDomain.TeamInvite), Is.EqualTo(5UL));
            Assert.That(_sync.IsDirty(SyncDomain.Team), Is.True);
        }

        // ── 迟到推送 ────────────────────────────────────────────────────────

        [Test]
        public void ConsecutiveLandings_DropLatePushFromPreviousLanding()
        {
            _sync.Tick();
            Assert.That(DigestRequests, Is.EqualTo(1));

            _net.Identity = new object(); // 重连:新连接,本帧 Tick 之前旧请求的推送先到
            PushDigest(100, (SyncDomain.Team, 9UL));

            Assert.That(_sync.HasDigest, Is.False, "旧落地的迟到推送丢弃");
            Assert.That(DirtyDomains(), Is.Empty);
            Assert.That(_store.Peek(Player), Is.Null, "旧推送不写缓存");

            _sync.Tick();
            Assert.That(DigestRequests, Is.EqualTo(2), "新落地重新取摘要");
            PushDigest(200);
            Assert.That(_sync.HasDigest, Is.True);
            Assert.That(_store.Peek(Player).Generation, Is.EqualTo(200UL));
        }

        [Test]
        public void LandingBegunWhileRequestUnanswered_TreatsDigestAsFullPull()
        {
            SeedCache(100, 0);
            _sync.Tick();
            _net.Identity = new object();
            _sync.Tick(); // 新落地:上一条请求还没回
            Assert.That(DigestRequests, Is.EqualTo(2));

            // 先到的这条可能是旧请求的回包(读于断线窗口之前),即使与缓存完全一致也要全量拉。
            PushDigest(100);
            Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains));

            PushDigest(100); // 本次请求自己的回包:已有摘要,丢弃
            Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains));
        }

        // ── 失败与退避 ──────────────────────────────────────────────────────

        [Test]
        public void DigestTimeout_BacksOff15_30_60AndFallsBackToFullPullOnce()
        {
            int notified = 0;
            using var registration = _sync.RegisterConsumer(SyncDomain.FriendRequest, _ => notified++);

            TickAt(0f);
            Assert.That(DigestRequests, Is.EqualTo(1));
            TickAt(14.9f);
            Assert.That(DigestRequests, Is.EqualTo(1));

            TickAt(15f); // 超时 → 先全量拉,15 秒后重试
            Assert.That(DigestRequests, Is.EqualTo(1));
            Assert.That(_sync.HasDigest, Is.False);
            Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains), "取不到摘要时全量拉");
            Assert.That(notified, Is.EqualTo(1));
            Assert.That(_sync.TryBeginPull(SyncDomain.FriendRequest, out var fallback), Is.True);
            Assert.That(fallback.HasVersion, Is.False);

            TickAt(29.9f); Assert.That(DigestRequests, Is.EqualTo(1));
            TickAt(30f);   Assert.That(DigestRequests, Is.EqualTo(2));
            TickAt(45f);   Assert.That(DigestRequests, Is.EqualTo(2)); // 第 2 次超时 → 30 秒
            TickAt(74.9f); Assert.That(DigestRequests, Is.EqualTo(2));
            TickAt(75f);   Assert.That(DigestRequests, Is.EqualTo(3));
            TickAt(90f);   Assert.That(DigestRequests, Is.EqualTo(3)); // 第 3 次超时 → 60 秒
            TickAt(149.9f); Assert.That(DigestRequests, Is.EqualTo(3));
            TickAt(150f);  Assert.That(DigestRequests, Is.EqualTo(4));
            TickAt(165f);  Assert.That(DigestRequests, Is.EqualTo(4)); // 第 4 次超时 → 封顶 60 秒
            TickAt(224.9f); Assert.That(DigestRequests, Is.EqualTo(4));
            TickAt(225f);  Assert.That(DigestRequests, Is.EqualTo(5));

            Assert.That(notified, Is.EqualTo(1), "重试再失败不重复通知");
        }

        [Test]
        public void GateRejection_CountsAsFailureAndBacksOff()
        {
            TickAt(0f);
            _net.Reject(MessageIds.GetSyncDigest, (uint)common_error.KRateLimitExceeded);
            Assert.That(_sync.RequestInFlight, Is.False);
            TickAt(14.9f); Assert.That(DigestRequests, Is.EqualTo(1));
            TickAt(15f);   Assert.That(DigestRequests, Is.EqualTo(2));
        }

        [Test]
        public void LatePushAfterTimeoutInSameLanding_IsAccepted()
        {
            SeedCache(100, 0);
            TickAt(0f);
            TickAt(15f); // 超时,全量拉兜底
            Assert.That(_sync.TryBeginPull(SyncDomain.Team, out var fallback), Is.True);

            PushDigest(100, (SyncDomain.Team, 4UL)); // 同一次落地的请求晚到:读于落地之后,照收
            Assert.That(_sync.HasDigest, Is.True);
            Assert.That(DirtyDomains(), Is.EquivalentTo(new[] { SyncDomain.Team }));
            Assert.That(_sync.Ack(fallback), Is.False, "摘要重算后,兜底批次的票作废");
            TickAt(30f);
            Assert.That(DigestRequests, Is.EqualTo(1), "拿到摘要后不再重试");
        }

        [Test]
        public void Unavailable_MarksEveryDomainDirtyClearsCacheAndAckWritesNothing()
        {
            SeedCache(100, 4);
            _sync.Tick();
            _net.Push(MessageIds.NotifySyncDigest, new SyncDigestS2C { Status = SyncDigestStatus.Unavailable });

            Assert.That(_sync.HasDigest, Is.True, "UNAVAILABLE 也结束等待");
            Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains));
            Assert.That(_store.Peek(Player), Is.Null, "该玩家缓存整份作废");

            Assert.That(_sync.TryBeginPull(SyncDomain.Team, out var ticket), Is.True);
            Assert.That(ticket.HasVersion, Is.False);
            Assert.That(_sync.Ack(ticket), Is.True);
            Assert.That(_sync.IsDirty(SyncDomain.Team), Is.False);
            Assert.That(_store.Peek(Player), Is.Null, "不知道服务端计数,确认不写缓存");
        }

        [Test]
        public void MalformedPush_IsTreatedAsUnavailable()
        {
            SeedCache(100, 0);
            _sync.Tick();
            _net.PushRaw(MessageIds.NotifySyncDigest, new byte[] { 0xFF, 0xFF, 0xFF });
            Assert.That(_sync.HasDigest, Is.True);
            Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains));
        }

        // ── 身份变化 ────────────────────────────────────────────────────────

        [Test]
        public void IdentityChange_ResetsDirtyAndInvalidatesTickets()
        {
            _sync.Tick();
            PushDigest(100, (SyncDomain.Team, 1UL));
            Assert.That(_sync.TryBeginPull(SyncDomain.Team, out var stale), Is.True);

            _net.Identity = new object();
            _sync.Tick();
            Assert.That(_sync.HasDigest, Is.False);
            Assert.That(DirtyDomains(), Is.Empty);
            Assert.That(DigestRequests, Is.EqualTo(2));
            Assert.That(_sync.Ack(stale), Is.False, "上一次落地的票作废");

            PushDigest(100, (SyncDomain.Team, 1UL));
            Assert.That(_sync.IsDirty(SyncDomain.Team), Is.True);
            Assert.That(_sync.Ack(stale), Is.False);
            Assert.That(CachedCount(SyncDomain.Team), Is.Null);
        }

        [Test]
        public void PlayerChange_UsesThatPlayersCache()
        {
            SeedCache(100, 0);
            _sync.Tick();
            PushDigest(100);
            Assert.That(DirtyDomains(), Is.Empty, "本玩家缓存与摘要一致");

            _net.PlayerId = 11; // 换角,同一条连接
            _sync.Tick();
            Assert.That(DigestRequests, Is.EqualTo(2));
            PushDigest(100);
            Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains), "新角色没有缓存");
            Assert.That(_store.Peek(11).Generation, Is.EqualTo(100UL));
        }

        [Test]
        public void LeavingGame_ClearsLandingState()
        {
            _sync.Tick();
            PushDigest(1);
            Assert.That(DirtyDomains(), Is.Not.Empty);
            _inGame = false;
            _sync.Tick();
            Assert.That(_sync.HasDigest, Is.False);
            Assert.That(DirtyDomains(), Is.Empty);
        }

        // ── 重入 ────────────────────────────────────────────────────────────

        [Test]
        public void ReentrantTick_DoesNotSendTwiceOrNotifyTwice()
        {
            // GameClient.Tick 会在 Call 的等待循环里重入:发送时同步回到 Tick。
            _net.OnSend = () => { _sync.Tick(); _sync.Tick(); };
            _sync.Tick();
            Assert.That(DigestRequests, Is.EqualTo(1));

            int calls = 0;
            using (_sync.RegisterConsumer(SyncDomain.Team, _ => { calls++; _sync.Tick(); }))
            {
                PushDigest(1);
                _sync.Tick();
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(DigestRequests, Is.EqualTo(1));
            }
        }

        // ── 缓存编码 ────────────────────────────────────────────────────────

        [Test]
        public void Codec_RoundTripsAndRejectsCorruptText()
        {
            var record = new SyncDigestRecord(1234567890123UL);
            record.SetCount((int)SyncDomain.Team, 7);
            record.SetCount((int)SyncDomain.FriendRequest, ulong.MaxValue);
            var decoded = SyncDigestCodec.Decode(SyncDigestCodec.Encode(record));
            Assert.That(decoded.Generation, Is.EqualTo(1234567890123UL));
            Assert.That(decoded.Counts, Is.EquivalentTo(record.Counts));

            Assert.That(SyncDigestCodec.Decode(""), Is.Null);
            Assert.That(SyncDigestCodec.Decode("2|1|"), Is.Null, "未知格式版本");
            Assert.That(SyncDigestCodec.Decode("1|x|"), Is.Null);
            Assert.That(SyncDigestCodec.Decode("1|1|4:"), Is.Null);
            Assert.That(SyncDigestCodec.Decode("1|1|0:3"), Is.Null);
            Assert.That(SyncDigestCodec.Decode("1|1|-4:3"), Is.Null);
            Assert.That(SyncDigestCodec.Decode("1|5|").Counts, Is.Empty);
            Assert.That(new PlayerPrefsSyncDigestStore("abc123").KeyFor(42), Is.EqualTo("mmorpg.syncdigest.abc123.42"));
        }

        [Test]
        public void StoreKeys_AreNamespacedPerGateway()
        {
            // 只验证纯函数部分(键与命名空间),不碰真实的 PlayerPrefs。
            string local = PlayerPrefsSyncDigestStore.NamespaceForGateway("http://127.0.0.1:8080");
            Assert.That(local, Does.Match("^[0-9a-f]{12}$"));
            Assert.That(PlayerPrefsSyncDigestStore.NamespaceForGateway("http://127.0.0.1:8080/"), Is.EqualTo(local),
                "末尾的 '/' 不改变环境");
            Assert.That(PlayerPrefsSyncDigestStore.NamespaceForGateway("http://test.example:8080"), Is.Not.EqualTo(local),
                "不同 gateway 不共用缓存");
            Assert.That(new PlayerPrefsSyncDigestStore(local).KeyFor(42), Is.Not.EqualTo("mmorpg.syncdigest.42"),
                "旧版不带命名空间的键不再被读取");
            Assert.Throws<ArgumentException>(() => new PlayerPrefsSyncDigestStore(""));
            Assert.Throws<ArgumentException>(() => new PlayerPrefsSyncDigestStore("a.b"));
        }

        [Test]
        public void StoreFailures_DoNotBreakTheFlow()
        {
            _store.ThrowOnAccess = true;
            _sync.Tick();
            PushDigest(100);
            Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains), "读失败按无缓存处理");
            Assert.That(_sync.TryBeginPull(SyncDomain.Team, out var ticket), Is.True);
            Assert.That(_sync.Ack(ticket), Is.True, "写失败不影响消脏标");
        }

        // ── 迟到推送的时限与拒绝回包计数 ───────────────────────────────────

        [TestCase(119f, true)]
        [TestCase(120f, false)]
        public void StalePushHorizon_OldUnansweredRequestStopsForcingFullPullAfter120s(float landingAt, bool fullPull)
        {
            SeedCache(100, 0);
            TickAt(0f);      // 这一条请求始终没有推送
            _inGame = false;
            TickAt(1f);      // 离开游戏:本次落地结束
            _inGame = true;
            _net.Identity = new object();
            TickAt(landingAt);
            Assert.That(DigestRequests, Is.EqualTo(2));

            PushDigest(100); // 与缓存完全一致
            if (fullPull) Assert.That(DirtyDomains(), Is.EquivalentTo(_sync.Domains), "旧请求的推送可能还在路上,本次全量拉");
            else Assert.That(DirtyDomains(), Is.Empty, "超过时限的旧请求不再等,按摘要逐域比对");
        }

        [Test]
        public void GateRejection_IsCountedSoNextLandingIsNotForcedToFullPull()
        {
            SeedCache(100, 0);
            TickAt(0f);
            _net.Reject(MessageIds.GetSyncDigest, (uint)common_error.KRateLimitExceeded);
            _net.Identity = new object();
            TickAt(1f);
            Assert.That(DigestRequests, Is.EqualTo(2));

            PushDigest(100);
            Assert.That(DirtyDomains(), Is.Empty, "被拒的请求不会再有推送,不该让下一次落地全量拉");
        }

        // ── 投递的隔离 ──────────────────────────────────────────────────────

        [Test]
        public void ConsumerThrows_OtherDomainsStillNotified()
        {
            int friendCalls = 0;
            using (_sync.RegisterConsumer(SyncDomain.Team, _ => throw new InvalidOperationException("boom")))
            using (_sync.RegisterConsumer(SyncDomain.FriendRequest, _ => friendCalls++))
            {
                _sync.Tick();
                PushDigest(1);
                Assert.DoesNotThrow(() => _sync.Tick());
                Assert.That(friendCalls, Is.EqualTo(1), "一个消费者出错不影响别的域");
                Assert.That(_sync.IsDirty(SyncDomain.Team), Is.True, "出错的域保留脏标");
            }
        }

        [Test]
        public void ConsumerBeginningNewLanding_StopsDeliveryOfTheOldBatch()
        {
            int friendCalls = 0;
            using (_sync.RegisterConsumer(SyncDomain.Team, _ => { _net.Identity = new object(); _sync.Tick(); }))
            using (_sync.RegisterConsumer(SyncDomain.FriendRequest, _ => friendCalls++))
            {
                _sync.Tick();
                PushDigest(1);
                _sync.Tick();
                Assert.That(friendCalls, Is.EqualTo(0), "回调里换了落地:旧批次不再投递");
                Assert.That(DirtyDomains(), Is.Empty, "新落地还没有摘要");
            }
        }

        // ── 绑定的解除 ──────────────────────────────────────────────────────

        [Test]
        public void BindingDispose_UnhooksTeamReadHook()
        {
            using var team = new TeamClient(_net, () => _net.Identity, null, null, () => 0f);
            var binding = LandingSyncBindings.BindTeam(_sync, team);
            Assert.That(team.SyncReadStarted, Is.Not.Null);
            binding.Dispose();
            Assert.That(team.SyncReadStarted, Is.Null);

            // 钩子已被别的绑定换掉时,旧绑定的 Dispose 不动它;重复 Dispose 无副作用。
            var again = LandingSyncBindings.BindTeam(_sync, team);
            Func<TeamAction, Action> other = _ => null;
            team.SyncReadStarted = other;
            again.Dispose();
            again.Dispose();
            Assert.That(team.SyncReadStarted, Is.SameAs(other));
        }

        [Test]
        public void BindingDispose_UnhooksFriendReadHookAndConsumer()
        {
            using var friends = new FriendClient(_net, () => _net.Identity, () => _inGame);
            var binding = LandingSyncBindings.BindFriendRequests(_sync, friends);
            Assert.That(friends.SyncReadStarted, Is.Not.Null);
            binding.Dispose();
            Assert.That(friends.SyncReadStarted, Is.Null);

            _sync.Tick();
            PushDigest(1);
            _sync.Tick();
            Assert.That(_net.Calls, Is.Empty, "消费者已注销:标脏不再触发拉取");
            Assert.That(_sync.IsDirty(SyncDomain.FriendRequest), Is.True);
        }

        [Test]
        public void BindingDispose_UnhooksGuildReadHookAndConsumers()
        {
            using var guild = NewGuild();
            var binding = LandingSyncBindings.BindGuild(_sync, guild);
            Assert.That(guild.SyncReadStarted, Is.Not.Null);
            binding.Dispose();
            Assert.That(guild.SyncReadStarted, Is.Null);

            _sync.Tick();
            PushDigest(1);
            _sync.Tick();
            guild.DrainSyncQueued();
            Assert.That(_net.Calls, Is.Empty, "消费者已注销:标脏不再排队");
        }

        // ── 帮会(BindGuild)───────────────────────────────────────────────

        [Test]
        public void Guild_DirtyQueuesHiddenPull_AppliedSnapshotAcks()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            _sync.Tick();
            PushDigest(100, (SyncDomain.Guild, 3UL));
            int changed = 0;
            guild.Changed += () => changed++;
            _sync.Tick(); // 投递标脏
            Assert.That(_net.Calls, Is.Empty, "消费者只排队,不在投递的调用栈上发请求");
            Assert.That(changed, Is.EqualTo(0), "排队不触发界面刷新");
            Assert.That(guild.Status, Is.EqualTo("请刷新帮会信息"), "排队不写提示(被动方隐私)");

            guild.DrainSyncQueued(); // 帮会窗口关着
            Assert.That(_net.Calls.Single().Id, Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(0, new GetPlayerGuildResponse { Guild = GuildFixture() });
            Assert.That(_sync.IsDirty(SyncDomain.Guild), Is.False);
            Assert.That(CachedCount(SyncDomain.Guild), Is.EqualTo(3UL));
        }

        [Test]
        public void Guild_NotInGuildReplyAcksGuild()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            _sync.Tick();
            PushDigest(100, (SyncDomain.Guild, 2UL));
            _sync.Tick();
            guild.DrainSyncQueued();
            _net.Reply(0, NotInGuild());
            Assert.That(_sync.IsDirty(SyncDomain.Guild), Is.False, "「不在帮」也是落定的帮会状态");
            Assert.That(CachedCount(SyncDomain.Guild), Is.EqualTo(2UL));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Guild_ApplicationPulledOnlyWhenNotInGuild(bool inGuild)
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            _sync.Tick();
            PushDigest(100, (SyncDomain.GuildApplication, 4UL));
            _sync.Tick();
            guild.DrainSyncQueued();
            Assert.That(_net.Calls[0].Id, Is.EqualTo(MessageIds.GetPlayerGuild), "本人申请以帮会状态为前提,先拉快照");
            _net.Reply(0, inGuild ? new GetPlayerGuildResponse { Guild = GuildFixture() } : NotInGuild());
            Assert.That(_sync.IsDirty(SyncDomain.GuildApplication), Is.EqualTo(!inGuild),
                inGuild ? "在帮快照同时落定了「本人申请为空」,由这次 GetPlayerGuild 一并确认"
                        : "不在帮:快照不确认本人申请,要另拉");

            guild.DrainSyncQueued();
            if (inGuild)
            {
                Assert.That(_net.Calls.Count, Is.EqualTo(1), "已入帮不拉本人申请");
                Assert.That(CachedCount(SyncDomain.GuildApplication), Is.EqualTo(4UL));
                return;
            }
            Assert.That(_net.Calls[1].Id, Is.EqualTo(MessageIds.ListMyGuildApplications));
            _net.Reply(1, new ListMyGuildApplicationsResponse());
            Assert.That(_sync.IsDirty(SyncDomain.GuildApplication), Is.False);
            Assert.That(CachedCount(SyncDomain.GuildApplication), Is.EqualTo(4UL));
            guild.DrainSyncQueued();
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "排的读取都已发出");
        }

        [Test]
        public void Guild_InGuildSecondLandingWithUnchangedDigest_SendsNoGuildRequest()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            var entries = new[] { (SyncDomain.Guild, 3UL), (SyncDomain.GuildApplication, 4UL) };
            LoadGuildAfterDigest(guild, entries); // 第一次落地:没有缓存,后台拉到在帮快照
            Assert.That(CachedCount(SyncDomain.Guild), Is.EqualTo(3UL));
            Assert.That(CachedCount(SyncDomain.GuildApplication), Is.EqualTo(4UL), "在帮快照顺带推进本人申请的缓存");

            // 第二次落地:重连换了连接,GuildUiRoot 随之复位帮会客户端(快照清空,HasLoaded 为假),摘要与缓存一致。
            // 若 GUILD_APPLICATION 仍是脏的,它的消费者会因快照没拉过补排 GetPlayerGuild —— 在帮玩家每次落地都多拉一次。
            _net.Identity = new object();
            guild.Reset();
            _sync.Tick();
            PushDigest(100, entries);
            _sync.Tick();
            guild.DrainSyncQueued();
            Assert.That(_sync.IsDirty(SyncDomain.Guild), Is.False);
            Assert.That(_sync.IsDirty(SyncDomain.GuildApplication), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "两个帮会域都没变:不发任何帮会请求");
        }

        [Test]
        public void Guild_EconomyAcksOnlyAfterBothDonationsAndShop()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            LoadGuildAfterDigest(guild, (SyncDomain.GuildEconomy, 7UL));
            guild.DrainSyncQueued();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "经济只被动确认,不后台拉");

            guild.RefreshDonations(); // 玩家进捐献页时的自动拉取
            Assert.That(_net.Calls[1].Id, Is.EqualTo(MessageIds.GetGuildDonateOptions));
            _net.Reply(1, new GetGuildDonateOptionsResponse());
            Assert.That(_sync.IsDirty(SyncDomain.GuildEconomy), Is.True, "只拉了捐献页:商店页可能还停在摘要之前");

            guild.RefreshShop();
            _net.Reply(2, new GetGuildShopResponse());
            Assert.That(_sync.IsDirty(SyncDomain.GuildEconomy), Is.False);
            Assert.That(CachedCount(SyncDomain.GuildEconomy), Is.EqualTo(7UL));
        }

        [Test]
        public void Guild_EconomyAckDoesNotPairReadsFromDifferentBatches()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            TickAt(0f);
            TickAt(15f); // 摘要超时:兜底全量拉,批次 E(没有版本)
            guild.DrainSyncQueued();
            Assert.That(_net.Calls.Single().Id, Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(0, new GetPlayerGuildResponse { Guild = GuildFixture() });

            guild.RefreshDonations(); // 捐献页在批次 E 发出并完成
            _net.Reply(1, new GetGuildDonateOptionsResponse());
            Assert.That(_sync.IsDirty(SyncDomain.GuildEconomy), Is.True);
            // 批次 E 里另一次商店读取:发出时取票,它的完成留到批次 E+1 之后才到。
            var lateShopOfBatchE = guild.SyncReadStarted(GuildSyncRead.Shop);
            Assert.That(lateShopOfBatchE, Is.Not.Null);

            PushDigest(100, (SyncDomain.GuildEconomy, 7UL)); // 同一次落地的摘要晚到:按摘要重算,批次 E+1
            guild.RefreshShop(); // 商店页在批次 E+1 发出并完成
            _net.Reply(2, new GetGuildShopResponse());
            Assert.That(_sync.IsDirty(SyncDomain.GuildEconomy), Is.True,
                "捐献页属于旧批次,可能早于新摘要:不能与新批次的商店页凑成一对");

            lateShopOfBatchE(); // 旧批次的完成迟到:忽略,不得冲掉已记下的 E+1 商店页
            Assert.That(_sync.IsDirty(SyncDomain.GuildEconomy), Is.True);

            guild.RefreshDonations(); // 捐献页在批次 E+1 再拉一次
            _net.Reply(3, new GetGuildDonateOptionsResponse());
            Assert.That(_sync.IsDirty(SyncDomain.GuildEconomy), Is.False, "E+1 的两页都完成才确认");
            Assert.That(CachedCount(SyncDomain.GuildEconomy), Is.EqualTo(7UL));
        }

        [Test]
        public void Guild_EconomyPageReadSentBeforeDigestDoesNotCount()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            guild.Refresh(); // 登录后立刻开窗、进捐献页:快照与捐献页都早于摘要发出
            _net.Reply(0, new GetPlayerGuildResponse { Guild = GuildFixture() });
            guild.RefreshDonations();
            _net.Reply(1, new GetGuildDonateOptionsResponse());

            _sync.Tick();
            PushDigest(100, (SyncDomain.GuildEconomy, 7UL));
            guild.RefreshShop(); // 商店页晚于摘要
            _net.Reply(2, new GetGuildShopResponse());
            Assert.That(_sync.IsDirty(SyncDomain.GuildEconomy), Is.True, "捐献页发出早于摘要,可能停在摘要之前的状态");

            guild.RefreshDonations(); // 再进一次捐献页,这次晚于摘要
            _net.Reply(3, new GetGuildDonateOptionsResponse());
            Assert.That(_sync.IsDirty(SyncDomain.GuildEconomy), Is.False);
            Assert.That(CachedCount(SyncDomain.GuildEconomy), Is.EqualTo(7UL));
        }

        [Test]
        public void Guild_NestedSynchronousReplyFiresInnerAndOuterAckOnceEach()
        {
            using var guild = NewGuild();
            var started = new List<GuildSyncRead>();
            var acked = new List<GuildSyncRead>();
            guild.SyncReadStarted = read =>
            {
                started.Add(read);
                return () =>
                {
                    acked.Add(read);
                    // 外层读取的成功回调里(经它的确认委托)再发一个带种类的读取;同步回包让它整个跑在外层的栈上。
                    if (read == GuildSyncRead.Guild) guild.RefreshDonations();
                };
            };
            // 假传输在 Call 内同步回包(同 GuildUiVerification 的同步替身)。
            _net.SyncReply = id =>
                id == MessageIds.GetPlayerGuild ? new GetPlayerGuildResponse { Guild = GuildFixture() }
                : id == MessageIds.GetGuildDonateOptions ? new GetGuildDonateOptionsResponse()
                : (IMessage)null;

            guild.Refresh();

            Assert.That(_net.Calls.Select(call => call.Id),
                Is.EqualTo(new[] { MessageIds.GetPlayerGuild, MessageIds.GetGuildDonateOptions }));
            Assert.That(started, Is.EqualTo(new[] { GuildSyncRead.Guild, GuildSyncRead.Donations }));
            Assert.That(acked, Is.EqualTo(new[] { GuildSyncRead.Guild, GuildSyncRead.Donations }), "内外两个确认各只触发一次");
            Assert.That(guild.Busy, Is.False);
            Assert.That(guild.Donations, Is.Not.Null, "内层读取已落定");
        }

        [Test]
        public void Guild_ActivityAckedOnlyByPageRead()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            LoadGuildAfterDigest(guild, (SyncDomain.GuildActivity, 9UL));
            guild.DrainSyncQueued();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "活动只被动确认,不后台拉");
            Assert.That(_sync.IsDirty(SyncDomain.GuildActivity), Is.True);

            guild.RefreshActivities(); // 玩家进活动页
            _net.Reply(1, new GetGuildActivitiesResponse());
            Assert.That(_sync.IsDirty(SyncDomain.GuildActivity), Is.False);
            Assert.That(CachedCount(SyncDomain.GuildActivity), Is.EqualTo(9UL));
        }

        [Test]
        public void Guild_ReadSentBeforeDigestDoesNotAck()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            guild.Refresh(); // 登录后立刻打开帮会窗口:发出早于摘要
            _sync.Tick();
            PushDigest(100, (SyncDomain.Guild, 3UL));
            _net.Reply(0, new GetPlayerGuildResponse { Guild = GuildFixture() });
            Assert.That(_sync.IsDirty(SyncDomain.Guild), Is.True, "发出早于摘要的读取不能推进缓存");

            _sync.Tick();
            guild.DrainSyncQueued();
            Assert.That(_net.Calls[1].Id, Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(1, new GetPlayerGuildResponse { Guild = GuildFixture() });
            Assert.That(_sync.IsDirty(SyncDomain.Guild), Is.False);
        }

        [Test]
        public void Guild_LaterReadOfSameKindSatisfiesQueuedPull()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            _sync.Tick();
            PushDigest(100, (SyncDomain.Guild, 3UL));
            _sync.Tick(); // GUILD 排上
            guild.Refresh(); // 玩家此刻打开窗口
            _net.Reply(0, new GetPlayerGuildResponse { Guild = GuildFixture() });
            Assert.That(_sync.IsDirty(SyncDomain.Guild), Is.False);
            guild.DrainSyncQueued();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "已发出的同种读取满足排队,不重复拉");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Guild_ErrorReplyKeepsDirty(bool transportFailure)
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            _sync.Tick();
            PushDigest(100, (SyncDomain.Guild, 3UL), (SyncDomain.GuildApplication, 4UL));
            _sync.Tick();
            guild.DrainSyncQueued();
            if (transportFailure) _net.Fail(0, "rpc timeout");
            else _net.Reply(0, new GetPlayerGuildResponse
                { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildBusyRetry } });

            Assert.That(_sync.IsDirty(SyncDomain.Guild), Is.True, "失败保留脏标");
            Assert.That(_sync.IsDirty(SyncDomain.GuildApplication), Is.True);
            Assert.That(CachedCount(SyncDomain.Guild), Is.Null);
            guild.DrainSyncQueued();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "快照没拉成:其余读取本次落地作罢,不每帧重试");
        }

        [Test]
        public void Guild_QueuedSyncReadIsNotSentWhileBusyAndIsSentOnceIdle()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            _sync.Tick();
            PushDigest(100);
            _sync.Tick(); // GUILD 排上
            guild.Browse(); // 别的读取(排行页)在途
            guild.DrainSyncQueued();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "在途时不发");

            _net.Reply(0, new GetGuildRankResponse());
            guild.DrainSyncQueued();
            Assert.That(_net.Calls[1].Id, Is.EqualTo(MessageIds.GetPlayerGuild), "排的读取没丢,空闲后发出");
        }

        [Test]
        public void Guild_ResetClearsSyncReads()
        {
            using var guild = NewGuild();
            using var binding = LandingSyncBindings.BindGuild(_sync, guild);
            _sync.Tick();
            PushDigest(100);
            _sync.Tick(); // GUILD / GUILD_APPLICATION 排上
            guild.Reset(); // 换角 / 重连
            guild.DrainSyncQueued();
            Assert.That(_net.Calls, Is.Empty, "排的读取属于上一次落地");
            Assert.That(guild.SyncReadStarted, Is.Not.Null, "复位不清钩子,不必重绑");
        }

        // ── 辅助 ────────────────────────────────────────────────────────────

        private int DigestRequests => _net.Sent.Count(sent => sent.Id == MessageIds.GetSyncDigest);

        private SyncDomain[] DirtyDomains() => _sync.Domains.Where(_sync.IsDirty).ToArray();

        private void TickAt(float now)
        {
            _now = now;
            _sync.Tick();
        }

        private void PushDigest(ulong generation, params (SyncDomain Domain, ulong Version)[] entries)
        {
            var digest = new SyncDigestS2C { Status = SyncDigestStatus.Ok, Generation = generation };
            foreach (var (domain, version) in entries)
                digest.Entries.Add(new SyncDigestEntry { Domain = domain, Version = version });
            _net.Push(MessageIds.NotifySyncDigest, digest);
        }

        /// <summary>给本玩家写一份缓存:每个已知域都有记录,默认 <paramref name="count"/>,再按 overrides 覆盖。</summary>
        private void SeedCache(ulong generation, ulong count, params (SyncDomain Domain, ulong Count)[] overrides)
        {
            var record = new SyncDigestRecord(generation);
            foreach (var domain in _sync.Domains) record.SetCount((int)domain, count);
            foreach (var (domain, value) in overrides) record.SetCount((int)domain, value);
            _store.Save(Player, record);
        }

        private ulong? CachedCount(SyncDomain domain)
        {
            var record = _store.Peek(Player);
            return record != null && record.TryGetCount((int)domain, out ulong count) ? count : (ulong?)null;
        }

        private static GetPendingRequestsResponse PendingRequests(params ulong[] requestIds)
        {
            var response = new GetPendingRequestsResponse();
            foreach (ulong id in requestIds)
                response.Requests.Add(new FriendRequest { RequestId = id, FromPlayerId = 20 + id, ToPlayerId = Player });
            return response;
        }

        private GuildClient NewGuild() => new GuildClient(_net, () => _net.Identity);

        /// <summary>本玩家所在的帮会快照(帮主 = 本人);GuildClient 只认含本人的快照。</summary>
        private static GuildInfo GuildFixture()
        {
            var info = new GuildInfo { GuildId = 555, Name = "清风明月", LeaderId = Player, Level = 1, MaxMembers = 50 };
            info.Members.Add(new GuildMember { PlayerId = Player, Role = GuildRoles.Leader });
            return info;
        }

        private static GetPlayerGuildResponse NotInGuild() =>
            new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } };

        /// <summary>首次落地(无缓存,全部域标脏)→ 后台拉帮会快照并应用(在帮)。之后 _net.Calls 里只有这一发。</summary>
        private void LoadGuildAfterDigest(GuildClient guild, params (SyncDomain Domain, ulong Version)[] entries)
        {
            _sync.Tick();
            PushDigest(100, entries);
            _sync.Tick();
            guild.DrainSyncQueued();
            Assert.That(_net.Calls.Single().Id, Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(0, new GetPlayerGuildResponse { Guild = GuildFixture() });
            Assert.That(guild.Info, Is.Not.Null);
        }
    }

    /// <summary>落地恢复测试用的传输替身:单向发送留记录,请求-响应按下标手动回包,推送按消息号投递。</summary>
    internal sealed class SyncFakeTransport : IBattleTransport
    {
        internal sealed class CallRecord
        {
            public uint Id;
            public IMessage Request;
            public Action<IMessage> Success;
            public Action<string> Error;
        }

        public ulong PlayerId { get; set; } = 10;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        /// <summary>连接身份;换成新对象即模拟重连 / 重定向换了 gate。</summary>
        public object Identity = new object();
        public readonly Dictionary<uint, Action<MessageContent>> Notifies = new Dictionary<uint, Action<MessageContent>>();
        public readonly List<(uint Id, IMessage Request)> Sent = new List<(uint Id, IMessage Request)>();
        public readonly List<CallRecord> Calls = new List<CallRecord>();
        /// <summary>单向发送之后同步调用(模拟 GameClient.Tick 在等待循环里重入)。</summary>
        public Action OnSend;
        /// <summary>
        /// 非 null 时 Call 在返回前按消息号同步回包(模拟同步回调的替身);返回 null 的消息号照常留给 <see cref="Reply"/>。
        /// </summary>
        public Func<uint, IMessage> SyncReply;

        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
        public void RegisterNotify(uint id, Action<MessageContent> handler) => Notifies[id] = handler;

        public void SendOneWay(uint id, IMessage request)
        {
            Sent.Add((id, request));
            OnSend?.Invoke();
        }

        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success, Action<string> error)
            where T : IMessage<T>
        {
            var record = new CallRecord { Id = id, Request = request, Success = response => success((T)response), Error = error };
            Calls.Add(record);
            var immediate = SyncReply?.Invoke(id);
            if (immediate != null) record.Success(immediate);
        }

        public void Reply(int index, IMessage response) => Calls[index].Success(response);
        public void Fail(int index, string error) => Calls[index].Error(error);

        /// <summary>模拟服务端推送;未注册的 id 会直接抛,测试里那就是接线漏了。</summary>
        public void Push(uint id, IMessage message) => PushRaw(id, message.ToByteArray());

        public void PushRaw(uint id, byte[] bytes) =>
            Notifies[id](new MessageContent { MessageId = id, SerializedMessage = ByteString.CopyFrom(bytes) });

        /// <summary>模拟 gate 拒绝(限流等):带错误码的信封回到该消息号自己的处理器。</summary>
        public void Reject(uint id, uint tipId) =>
            Notifies[id](new MessageContent { MessageId = id, ErrorMessage = new TipInfoMessage { Id = tipId } });
    }

    /// <summary>内存缓存:经 SyncDigestCodec 编解码存成字符串,与 PlayerPrefs 实现同一条序列化路径。</summary>
    internal sealed class MemorySyncDigestStore : ISyncDigestStore
    {
        private readonly Dictionary<ulong, string> _rows = new Dictionary<ulong, string>();
        /// <summary>为真时读写都抛异常(模拟 PlayerPrefs 不可用)。</summary>
        public bool ThrowOnAccess;

        public SyncDigestRecord Load(ulong playerId)
        {
            if (ThrowOnAccess) throw new InvalidOperationException("store unavailable");
            return Peek(playerId);
        }

        public void Save(ulong playerId, SyncDigestRecord record)
        {
            if (ThrowOnAccess) throw new InvalidOperationException("store unavailable");
            _rows[playerId] = SyncDigestCodec.Encode(record);
        }

        public void Delete(ulong playerId)
        {
            if (ThrowOnAccess) throw new InvalidOperationException("store unavailable");
            _rows.Remove(playerId);
        }

        public SyncDigestRecord Peek(ulong playerId) =>
            _rows.TryGetValue(playerId, out string raw) ? SyncDigestCodec.Decode(raw) : null;

        /// <summary>删掉某个域的记录(构造「本地没有记录」的场景)。</summary>
        public void RemoveCount(ulong playerId, SyncDomain domain)
        {
            var record = Peek(playerId);
            if (record == null) return;
            var copy = new SyncDigestRecord(record.Generation);
            foreach (var pair in record.Counts)
                if (pair.Key != (int)domain) copy.SetCount(pair.Key, pair.Value);
            _rows[playerId] = SyncDigestCodec.Encode(copy);
        }
    }
}
