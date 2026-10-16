using System;
using System.Collections.Generic;
using System.Linq;
using Friendpb;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
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
            Assert.That(_sync.Domains, Has.No.Member(SyncDomain.Unspecified));
            Assert.That(_sync.Domains, Has.Member(SyncDomain.FriendRequest));
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
            Assert.That(PlayerPrefsSyncDigestStore.KeyFor(42), Is.EqualTo("mmorpg.syncdigest.42"));
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
            Calls.Add(new CallRecord { Id = id, Request = request, Success = response => success((T)response), Error = error });
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
