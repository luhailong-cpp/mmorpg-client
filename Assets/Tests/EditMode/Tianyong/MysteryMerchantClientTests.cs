using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.MysteryMerchant;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// 神秘商人网络层的状态机。时间全部由注入的时钟推进(本地秒)与服务端时刻(毫秒)推算;
    /// 默认场景:商人在 3 线(scene 300),整点出现、停留 30 分钟、每小时一轮,玩家在 1 线(scene 100)。
    /// 传输层复用切线测试的 <see cref="SceneChannelFakeTransport"/>(同一个测试程序集)。
    /// </summary>
    public sealed class MysteryMerchantClientTests
    {
        private const ulong PlayerScene = 100;
        private const ulong MerchantScene = 300;
        private const ulong Appear = 10_000_000;
        private const ulong Stay = 1_800_000;
        private const ulong Hour = 3_600_000;
        private const ulong Round = 42;

        private float _now;
        private ulong _sceneId;
        private SceneChannelFakeTransport _net;
        private MysteryMerchantClient _client;
        private int _changes;
        private readonly List<(uint ChannelNo, bool Here, long RemainingMs)> _appeared = new List<(uint, bool, long)>();
        private readonly List<(uint GoodsId, uint Count)> _purchased = new List<(uint, uint)>();

        [SetUp]
        public void SetUp()
        {
            _now = 0f;
            _sceneId = PlayerScene;
            _changes = 0;
            _appeared.Clear();
            _purchased.Clear();
            _net = new SceneChannelFakeTransport();
            _client = new MysteryMerchantClient(_net, () => _net.Identity, () => _sceneId, () => _now);
            _client.Changed += () => _changes++;
            _client.Appeared += (channelNo, here, remainingMs) => _appeared.Add((channelNo, here, remainingMs));
            _client.Purchased += (goods, count) => _purchased.Add((goods.GoodsId, count));
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        // ── 样例数据 ────────────────────────────────────────────────────────

        private static global::MysteryMerchantGoodsInfo Goods(uint id, ulong price, uint limit, uint bought = 0) =>
            new global::MysteryMerchantGoodsInfo
            {
                GoodsId = id, ItemConfigId = 10 + id, ItemCount = 10, Name = "商品" + id, CurrencyType = 0,
                OriginalPrice = price * 2, Price = price, DiscountPercent = 50, LimitCount = limit, BoughtCount = bought,
            };

        /// <summary>服务端在 <paramref name="serverNowMs"/> 给出的结论:商人在场;<paramref name="here"/> 时带两件货。</summary>
        private static global::MysteryMerchantInfo ActiveInfo(ulong serverNowMs, bool here, ulong round = Round,
            ulong sceneId = MerchantScene, uint channelNo = 3)
        {
            var info = new global::MysteryMerchantInfo
            {
                Active = true, RoundId = round, SceneConfigId = 1, SceneId = sceneId, ChannelNo = channelNo,
                AppearAtMs = Appear, ExpireAtMs = Appear + Stay, NextAppearAtMs = Appear + Hour,
                ServerTimeMs = serverNowMs, Here = here, InteractRange = 8,
                Position = new global::Vector3 { X = 176, Y = 206, Z = 0 },
            };
            if (here)
            {
                info.Goods.Add(Goods(1, 240, 5));
                info.Goods.Add(Goods(2, 3000, 1));
            }
            return info;
        }

        private static global::MysteryMerchantInfo AwayInfo(ulong serverNowMs, ulong nextAppearMs) =>
            new global::MysteryMerchantInfo { ServerTimeMs = serverNowMs, NextAppearAtMs = nextAppearMs };

        private SceneChannelFakeTransport.SentCall LastCall => _net.Calls[_net.Calls.Count - 1];

        private void ReplyInfo(global::MysteryMerchantInfo info) =>
            LastCall.Reply(new global::GetMysteryMerchantInfoResponse { Info = info });

        /// <summary>让玩家站到商人那条线上,并让数据层拿到带货架的结论。</summary>
        private void ArriveOnMerchantLine(ulong serverNowMs = Appear + 60_000)
        {
            _sceneId = MerchantScene;
            _client.HandleSceneEntered();
            ReplyInfo(ActiveInfo(serverNowMs, here: true));
        }

        private void Advance(float seconds)
        {
            _now += seconds;
            _client.Tick();
        }

        /// <summary>按此刻货架上的报价买(等价于玩家看着当前货架点了购买并确认)。</summary>
        private bool BuyShown(uint goodsId, uint count) => _client.Buy(_client.QuoteFor(goodsId), count);

        private void PushNotify(global::MysteryMerchantInfo info) =>
            _net.Push(MessageIds.NotifyMysteryMerchant, new global::MysteryMerchantNotify { Info = info });

        // ── 构造与查询 ──────────────────────────────────────────────────────

        [Test]
        public void Construct_SendsNothing_AndKnowsNothing()
        {
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.HasInfo, Is.False);
            Assert.That(_client.Presence, Is.EqualTo(MysteryMerchantPresence.Unknown));
            Assert.That(_net.Notifies.ContainsKey(MessageIds.NotifyMysteryMerchant), Is.True, "公告处理器在构造时注册");
        }

        [Test]
        public void RequestInfo_ElsewhereReply_GivesLineAndCountdownButNoShelf()
        {
            _client.RequestInfo();
            Assert.That(_client.InfoPending, Is.True);
            Assert.That(LastCall.Id, Is.EqualTo(MessageIds.GetMysteryMerchantInfo));

            ReplyInfo(ActiveInfo(Appear + 60_000, here: false));
            Assert.That(_client.InfoPending, Is.False);
            Assert.That(_client.Presence, Is.EqualTo(MysteryMerchantPresence.Elsewhere));
            Assert.That(_client.Info.ChannelNo, Is.EqualTo(3u));
            Assert.That(_client.Info.Goods, Is.Empty);
            Assert.That(_client.RemainingMs, Is.EqualTo((long)(Stay - 60_000)));
        }

        [Test]
        public void ServerClock_AdvancesWithTheLocalClock()
        {
            _client.RequestInfo();
            ReplyInfo(ActiveInfo(Appear + 60_000, here: false));
            _now += 10f;
            Assert.That(_client.ServerNowMs, Is.EqualTo(Appear + 70_000));
            Assert.That(_client.RemainingMs, Is.EqualTo((long)(Stay - 70_000)));
        }

        [Test]
        public void RequestInfo_WhileOnePending_DoesNotSendAgain()
        {
            _client.RequestInfo();
            _client.RequestInfo();
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public void RequestInfo_WhileOnePending_AsksOnceMoreAfterTheReply()
        {
            _client.RequestInfo();
            _client.RequestInfo();   // 在途的那一次可能问得太早(公告刚到、刚被告知货架过时):回来之后再问一次
            ReplyInfo(ActiveInfo(Appear + 60_000, here: false));
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "还没到最小间隔:先排着");
            Assert.That(_client.InfoQueued, Is.True);

            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            ReplyInfo(ActiveInfo(Appear + 62_500, here: false));
            Advance(MysteryMerchantClient.MinInfoIntervalSeconds * 4);
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "只多问一次,不会没完没了");
        }

        [Test]
        public void RequestInfo_Failure_RetriesByItself_WithGrowingIntervals()
        {
            _client.RequestInfo();
            LastCall.Fail("rpc timeout");

            // 第一次失败:5 秒后自己再问。
            Advance(MysteryMerchantModels.InfoRetryDelaySeconds(1) - 0.1f);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Advance(0.2f);
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "进场那一次查询丢了,不能等到下一次换场景才看得到商人");

            // 连续第二次失败:间隔翻倍。
            LastCall.Fail("rpc timeout");
            Advance(MysteryMerchantModels.InfoRetryDelaySeconds(2) - 0.1f);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Advance(0.2f);
            Assert.That(_net.Calls.Count, Is.EqualTo(3));

            // 成功之后不再重试。
            ReplyInfo(ActiveInfo(Appear + 60_000, here: false));
            Advance(120f);
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
            Assert.That(_client.Status, Is.EqualTo(""));
        }

        [Test]
        public void RequestInfo_TooSoon_IsQueuedAndSentWhenTheIntervalElapses()
        {
            _client.RequestInfo();
            ReplyInfo(ActiveInfo(Appear + 60_000, here: false));
            Advance(1f);
            _client.RequestInfo();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "距上一次不足最小间隔:不发");
            Assert.That(_client.InfoQueued, Is.True);

            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "到点由 Tick 补发,不丢");
            Assert.That(_client.InfoQueued, Is.False);
        }

        [Test]
        public void RequestInfo_TransportError_ReportsUnavailable_AndAllowsRetry()
        {
            _client.RequestInfo();
            LastCall.Fail("rpc timeout");
            Assert.That(_client.InfoPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantClient.InfoUnavailableMessage));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_client.RequiresReconnect, Is.False, "查询是幂等的只读请求,失败不需要等重连");

            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            _client.RequestInfo();
            ReplyInfo(ActiveInfo(Appear + 60_000, here: false));
            Assert.That(_client.Status, Is.EqualTo(""), "查询成功后收掉「打听不到」");
        }

        [Test]
        public void RequestInfo_RateLimited_BacksOffAFullWindow()
        {
            _client.RequestInfo();
            LastCall.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            Advance(1f);
            _client.RequestInfo();
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Advance(MysteryMerchantClient.RateLimitBackoffSeconds);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void OlderReply_NeverOverwritesANewerConclusion()
        {
            _client.RequestInfo();
            ReplyInfo(ActiveInfo(Appear + 120_000, here: false, channelNo: 5));
            // 一份更早的结论迟到(比如公告与查询应答走了两条路)。
            _net.Push(MessageIds.NotifyMysteryMerchant,
                new global::MysteryMerchantNotify { Info = ActiveInfo(Appear + 5_000, here: false, channelNo: 3) });
            Assert.That(_client.Info.ChannelNo, Is.EqualTo(5u));
            Assert.That(_appeared, Is.Empty, "迟到的旧公告不采纳,也不再弹一次「出现了」");
        }

        // ── 公告 ────────────────────────────────────────────────────────────

        [Test]
        public void Notify_OnAnotherLine_RaisesAppeared_WithoutQueryingTheShelf()
        {
            _net.Push(MessageIds.NotifyMysteryMerchant,
                new global::MysteryMerchantNotify { Info = ActiveInfo(Appear + 4_000, here: false) });

            Assert.That(_client.Presence, Is.EqualTo(MysteryMerchantPresence.Elsewhere));
            Assert.That(_appeared, Is.EqualTo(new[] { (3u, false, (long)(Stay - 4_000)) }));
            Assert.That(_net.Calls, Is.Empty, "不在本线:看不到货架,不必问");
        }

        [Test]
        public void Notify_OnMyLine_RaisesAppearedHere_AndFetchesTheShelf()
        {
            _sceneId = MerchantScene;
            _net.Push(MessageIds.NotifyMysteryMerchant,
                new global::MysteryMerchantNotify { Info = ActiveInfo(Appear + 4_000, here: false) });

            Assert.That(_client.Presence, Is.EqualTo(MysteryMerchantPresence.Here), "公告里 here 恒为 false,在不在本线按 scene_id 自己比");
            Assert.That(_appeared.Count, Is.EqualTo(1));
            Assert.That(_appeared[0].Here, Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "就在本线:马上把货架问回来");
            Assert.That(LastCall.Id, Is.EqualTo(MessageIds.GetMysteryMerchantInfo));
        }

        [Test]
        public void Notify_SameRoundSameLine_DoesNotWipeTheShelfAlreadyOnHand()
        {
            ArriveOnMerchantLine();
            Assert.That(_client.Info.Goods.Count, Is.EqualTo(2));

            // 同一轮、同一条线的公告又来了一遍(不带货架)。
            _net.Push(MessageIds.NotifyMysteryMerchant,
                new global::MysteryMerchantNotify { Info = ActiveInfo(Appear + 90_000, here: false) });
            Assert.That(_client.Info.Goods.Count, Is.EqualTo(2), "公告不带货架,不该把手里的清空");
            Assert.That(_client.Info.ServerTimeMs, Is.EqualTo(Appear + 90_000));
        }

        [Test]
        public void Notify_SameLandingAnnouncedTwice_RaisesAppearedOnce()
        {
            PushNotify(ActiveInfo(Appear + 4_000, here: false));
            // 跨节点换场景时,另一个节点也发过一条说同一件事的公告(同一轮、同一条线)。
            PushNotify(ActiveInfo(Appear + 9_000, here: false));
            Assert.That(_appeared.Count, Is.EqualTo(1), "同一次落位只报一次");
            Assert.That(_client.Info.ServerTimeMs, Is.EqualTo(Appear + 9_000), "结论照常采纳,只是不再弹提示");

            // 同一轮内商人换了线:这是一次新的落位。
            PushNotify(ActiveInfo(Appear + 20_000, here: false, sceneId: 500, channelNo: 5));
            Assert.That(_appeared.Count, Is.EqualTo(2));
            Assert.That(_appeared[1].ChannelNo, Is.EqualTo(5u));

            // 下一轮回到原来那条线:又是一次新的落位。
            PushNotify(ActiveInfo(Appear + 30_000, here: false, round: Round + 3600));
            Assert.That(_appeared.Count, Is.EqualTo(3));
        }

        [Test]
        public void Notify_Malformed_OrInactive_IsIgnored()
        {
            _net.PushRaw(MessageIds.NotifyMysteryMerchant, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
            _net.Push(MessageIds.NotifyMysteryMerchant, new global::MysteryMerchantNotify());
            _net.Push(MessageIds.NotifyMysteryMerchant,
                new global::MysteryMerchantNotify { Info = AwayInfo(Appear + Stay + 1, Appear + Hour) });
            Assert.That(_client.HasInfo, Is.False);
            Assert.That(_appeared, Is.Empty);
        }

        // ── 到点 ────────────────────────────────────────────────────────────

        [Test]
        public void Tick_MerchantLeavesAtExpiry_RaisesChangedOnce()
        {
            _client.RequestInfo();
            ReplyInfo(ActiveInfo(Appear + Stay - 5_000, here: false));
            _changes = 0;

            Advance(4f);
            Assert.That(_client.Presence, Is.EqualTo(MysteryMerchantPresence.Elsewhere));
            Assert.That(_changes, Is.EqualTo(0), "倒计时走动不触发变化通知");

            Advance(2f);
            Assert.That(_client.Presence, Is.EqualTo(MysteryMerchantPresence.Away));
            Assert.That(_changes, Is.EqualTo(1), "到点离开:一次变化通知");
            Advance(1f);
            Assert.That(_changes, Is.EqualTo(1));
        }

        [Test]
        public void Tick_AfterTheNextAppearanceTime_AsksAgain_UntilTheServerNamesTheRealNextRound()
        {
            float first = MysteryMerchantModels.AutoRefreshDelayMs(0, _net.PlayerId) / 1000f;
            float second = MysteryMerchantModels.AutoRefreshDelayMs(1, _net.PlayerId) / 1000f;
            _client.RequestInfo();
            ReplyInfo(AwayInfo(Appear + Hour - 3_000, Appear + Hour));
            int sent = _net.Calls.Count;

            Advance(3f + first - 1f);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent), "到点之后还没等够:公告通常会先到");
            Assert.That(_client.UntilNextMs, Is.EqualTo(0L), "到点了:角标显示「即将出现」");
            Advance(1.5f);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 1), "公告没来:自己问一次");

            // 服务端还在整点后的宽限期里:它答的「下一次」仍是刚过去的这个整点。
            ReplyInfo(AwayInfo(Appear + Hour + (ulong)(first * 1000f) + 500, Appear + Hour));
            Advance(second - first - 1f);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 1), "第二次要等到宽限期之后");
            Advance(1f);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 2), "不能就此停在「即将出现」:过了宽限期再问一次");

            // 这一次服务端给出了真正的下一轮:角标回到倒计时,不再追问。
            ReplyInfo(AwayInfo(Appear + Hour + (ulong)(second * 1000f) + 600, Appear + 2 * Hour));
            Assert.That(_client.UntilNextMs, Is.GreaterThan(0L));
            Advance(300f);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 2));
        }

        [Test]
        public void Tick_AutoRefresh_GivesUpAfterAFewAttemptsForTheSameAppearance()
        {
            _client.RequestInfo();
            ReplyInfo(AwayInfo(Appear + Hour - 3_000, Appear + Hour));

            // 调度停摆:每次问回来「下一次出现时刻」都没变。
            for (int attempt = 0; attempt < MysteryMerchantModels.MaxAutoRefreshAttempts; attempt++)
            {
                int before = _net.Calls.Count;
                for (int second = 0; second < 300 && _net.Calls.Count == before; second++) Advance(1f);
                Assert.That(_net.Calls.Count, Is.EqualTo(before + 1), "第 " + attempt + " 次自动查询");
                ReplyInfo(AwayInfo(_client.ServerNowMs, Appear + Hour));
            }
            int final = _net.Calls.Count;
            Advance(600f);
            Assert.That(_net.Calls.Count, Is.EqualTo(final), "同一个时刻问够了就不再问");
        }

        // ── 进场与断线 ──────────────────────────────────────────────────────

        [Test]
        public void SceneEntered_DropsTheShelfOfThePreviousLine_AndAsksAgain()
        {
            ArriveOnMerchantLine();
            Assert.That(_client.Presence, Is.EqualTo(MysteryMerchantPresence.Here));
            int sent = _net.Calls.Count;

            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            _sceneId = PlayerScene;   // 切回 1 线
            _client.HandleSceneEntered();
            Assert.That(_client.Presence, Is.EqualTo(MysteryMerchantPresence.Elsewhere), "行踪与倒计时仍然有效");
            Assert.That(_client.Info.Goods, Is.Empty, "货架是上一条线的,清掉");
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 1));
        }

        [Test]
        public void SceneEntered_WhileAQueryIsInFlight_DropsItsReply_AndAsksAgain()
        {
            _client.RequestInfo();   // 在 1 线问的,还没回来
            var stale = LastCall;
            _sceneId = MerchantScene;   // 这时切到了商人那条线
            _client.HandleSceneEntered();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "有请求在途:不重复发");

            // 上一条线问出来的应答:here == false、没有货架 —— 说的是已经离开的那条线。
            stale.Reply(new global::GetMysteryMerchantInfoResponse { Info = ActiveInfo(Appear + 60_000, here: false) });
            Assert.That(_client.HasInfo, Is.False, "整包丢弃,不能让它把「本线没有货架」写进来");
            Assert.That(_client.InfoQueued, Is.True);

            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "到点重问");
            ReplyInfo(ActiveInfo(Appear + 62_500, here: true));
            Assert.That(_client.Presence, Is.EqualTo(MysteryMerchantPresence.Here));
            Assert.That(_client.Info.Goods.Count, Is.EqualTo(2));
        }

        [Test]
        public void SceneEntered_TheNewNodeMayHaveASlowerClock_ItsConclusionsAreStillAdopted()
        {
            _client.RequestInfo();
            ReplyInfo(ActiveInfo(Appear + 120_000, here: false));   // 旧节点的时钟
            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            _sceneId = MerchantScene;
            _client.HandleSceneEntered();

            // 新节点的时钟比旧节点慢 20 秒:它的公告不能被当成「更旧的」丢掉。
            PushNotify(ActiveInfo(Appear + 100_000, here: false));
            Assert.That(_client.Info.ServerTimeMs, Is.EqualTo(Appear + 100_000), "换过场景之后,两个节点的时钟不可比");
            Assert.That(_appeared.Count, Is.EqualTo(1));

            // 当前场景里问出来的应答是所在节点此刻的结论:一律采纳。
            ReplyInfo(ActiveInfo(Appear + 99_000, here: true));
            Assert.That(_client.Info.ServerTimeMs, Is.EqualTo(Appear + 99_000));
            Assert.That(_client.Info.Goods.Count, Is.EqualTo(2));
        }

        [Test]
        public void Disconnect_ResetsEverything_AndLateRepliesAreDropped()
        {
            _client.RequestInfo();
            var stale = LastCall;
            _net.Disconnect();
            Assert.That(_client.HasInfo, Is.False);
            Assert.That(_client.InfoPending, Is.False);

            _net.Reconnect();
            stale.Reply(new global::GetMysteryMerchantInfoResponse { Info = ActiveInfo(Appear + 60_000, here: false) });
            Assert.That(_client.HasInfo, Is.False, "旧连接上的迟到应答按世代丢弃");
        }

        [Test]
        public void SilentGateSwap_IsTreatedLikeADisconnect()
        {
            _client.RequestInfo();
            ReplyInfo(ActiveInfo(Appear + 60_000, here: false));
            _net.Identity = new object();   // 换了连接对象而没有断线事件
            _client.Tick();
            Assert.That(_client.HasInfo, Is.False);
        }

        // ── 购买 ────────────────────────────────────────────────────────────

        [Test]
        public void Buy_SendsTheWholeQuoteThePlayerSaw()
        {
            ArriveOnMerchantLine();
            Assert.That(BuyShown(1, 2), Is.True);
            Assert.That(_client.BuyPending, Is.True);
            Assert.That(_client.BuyingGoodsId, Is.EqualTo(1u));
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantClient.BuyPendingMessage));

            Assert.That(LastCall.Id, Is.EqualTo(MessageIds.BuyMysteryMerchantGoods));
            var request = (global::BuyMysteryMerchantGoodsRequest)LastCall.Request;
            Assert.That(request.RoundId, Is.EqualTo(Round));
            Assert.That(request.GoodsId, Is.EqualTo(1u));
            Assert.That(request.Count, Is.EqualTo(2u));
            // 报价的每一项原样回传:服务端发现任何一项对不上就拒绝。
            Assert.That(request.ExpectedPrice, Is.EqualTo(240ul));
            Assert.That(request.ExpectedCurrencyType, Is.EqualTo(0u));
            Assert.That(request.ExpectedItemConfigId, Is.EqualTo(11u));
            Assert.That(request.ExpectedItemCount, Is.EqualTo(10u));
        }

        [Test]
        public void Buy_WithAQuoteThatNoLongerMatchesTheShelf_IsRejectedLocally()
        {
            ArriveOnMerchantLine();
            var shown = _client.QuoteFor(1);
            Assert.That(shown.IsValid, Is.True);
            int sent = _net.Calls.Count;

            // 玩家点第一下时看到的是另一轮 / 另一个价格 / 另一件物品 / 另一个数量 / 另一种货币。
            var stale = new[]
            {
                new MysteryMerchantQuote(shown.RoundId + 3600, shown.GoodsId, shown.Price, shown.CurrencyType, shown.ItemConfigId, shown.ItemCount),
                new MysteryMerchantQuote(shown.RoundId, shown.GoodsId, shown.Price + 1, shown.CurrencyType, shown.ItemConfigId, shown.ItemCount),
                new MysteryMerchantQuote(shown.RoundId, shown.GoodsId, shown.Price, shown.CurrencyType + 1, shown.ItemConfigId, shown.ItemCount),
                new MysteryMerchantQuote(shown.RoundId, shown.GoodsId, shown.Price, shown.CurrencyType, shown.ItemConfigId + 1, shown.ItemCount),
                new MysteryMerchantQuote(shown.RoundId, shown.GoodsId, shown.Price, shown.CurrencyType, shown.ItemConfigId, shown.ItemCount + 1),
                default(MysteryMerchantQuote),
            };
            foreach (var quote in stale)
            {
                Assert.That(_client.Buy(quote, 1), Is.False);
                Assert.That(_client.Status, Is.EqualTo(MysteryMerchantModels.ShelfChangedText));
            }
            Assert.That(_net.Calls.Count, Is.EqualTo(sent), "界面上已经是新的一份:让玩家看清楚再点,不发请求");

            Assert.That(_client.Buy(shown, 1), Is.True, "报价对得上才发");
        }

        [Test]
        public void Buy_Success_AdoptsTheNewShelfAndBalance_AndRaisesPurchased()
        {
            ArriveOnMerchantLine();
            BuyShown(1, 2);

            var updated = ActiveInfo(Appear + 61_000, here: true);
            updated.Goods[0].BoughtCount = 2;
            var bag = new global::BagInfo { Currency = new global::CurrencyComp() };
            bag.Currency.Values.Add(9_520);
            LastCall.Reply(new global::BuyMysteryMerchantGoodsResponse { Info = updated, Bag = bag });

            Assert.That(_client.BuyPending, Is.False);
            Assert.That(_client.BuyingGoodsId, Is.EqualTo(0u));
            Assert.That(_client.FindGoods(1).BoughtCount, Is.EqualTo(2u), "已购数只来自服务端");
            Assert.That(_client.Currency.Values, Is.EqualTo(new[] { 9_520ul }), "购买成功那一刻的余额(只用于显示)");
            Assert.That(_client.Status, Is.EqualTo("购买成功：商品1 ×20"));
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_purchased, Is.EqualTo(new[] { (1u, 2u) }));
        }

        [Test]
        public void Buy_RejectedByServer_ShowsTheReason_AndRefreshesWhenTheShelfIsStale()
        {
            ArriveOnMerchantLine();
            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            int sent = _net.Calls.Count;
            BuyShown(1, 1);
            LastCall.Reply(new global::BuyMysteryMerchantGoodsResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)mystery_merchant_error.KMysteryMerchantShelfChanged },
            });

            Assert.That(_client.BuyPending, Is.False);
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantModels.DescribeTip((uint)mystery_merchant_error.KMysteryMerchantShelfChanged)));
            Assert.That(_purchased, Is.Empty);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 2), "货架过时:购买那一次 + 立刻重新问的一次");
            Assert.That(LastCall.Id, Is.EqualTo(MessageIds.GetMysteryMerchantInfo));
        }

        [Test]
        public void Buy_TooFar_ShowsTheReason_WithoutRefreshing()
        {
            ArriveOnMerchantLine();
            int sent = _net.Calls.Count;
            BuyShown(1, 1);
            LastCall.Reply(new global::BuyMysteryMerchantGoodsResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)mystery_merchant_error.KMysteryMerchantTooFar },
            });
            Assert.That(_client.Status, Does.Contain("太远"));
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 1), "离得远不代表货架过时:不重新问");
        }

        [Test]
        public void Buy_TransportTimeout_BlocksFurtherBuysUntilReconnect()
        {
            ArriveOnMerchantLine();
            BuyShown(1, 1);
            LastCall.Fail("rpc timeout");

            Assert.That(_client.RequiresReconnect, Is.True, "结果未知:之后的应答无法与请求对应");
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantClient.RecoveryMessage));
            int sent = _net.Calls.Count;
            Advance(5f);
            Assert.That(BuyShown(1, 1), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));

            _net.Disconnect();
            _net.Reconnect();
            _client.Tick();
            Assert.That(_client.RequiresReconnect, Is.False, "真实断线重连之后解除");
        }

        [Test]
        public void Buy_TransportTimeout_TheWarningSurvivesLaterQueries()
        {
            ArriveOnMerchantLine();
            BuyShown(1, 1);
            LastCall.Fail("rpc timeout");
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantClient.RecoveryMessage));

            // 之后的查询失败、成功,以及本地拒绝,都不能把「购买结果未知,请重新登录」盖掉。
            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            _client.RequestInfo();
            LastCall.Fail("rpc timeout");
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantClient.RecoveryMessage));

            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            _client.RequestInfo();
            ReplyInfo(ActiveInfo(Appear + 70_000, here: true));
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantClient.RecoveryMessage));
            Assert.That(_client.StatusIsError, Is.True);
        }

        [Test]
        public void Buy_RateLimitedAtTheGate_IsADefiniteFailure_NotAnUnknownOutcome()
        {
            ArriveOnMerchantLine();
            BuyShown(1, 1);
            LastCall.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);

            Assert.That(_client.RequiresReconnect, Is.False, "gate 带码拒绝:请求没到 scene,结果是确定的");
            Assert.That(_client.Status, Does.Contain("太快"));
            int sent = _net.Calls.Count;
            Advance(MysteryMerchantClient.MinBuyIntervalSeconds);
            Assert.That(BuyShown(1, 1), Is.False, "限流后要让出一个完整的窗口");
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Advance(MysteryMerchantClient.RateLimitBackoffSeconds);
            Assert.That(BuyShown(1, 1), Is.True);
        }

        [Test]
        public void Buy_LocalRejections_SendNothing()
        {
            // 还没有任何结论。
            Assert.That(BuyShown(1, 1), Is.False);
            Assert.That(_net.Calls, Is.Empty);

            // 商人在别的线。
            _client.RequestInfo();
            ReplyInfo(ActiveInfo(Appear + 60_000, here: false));
            int sent = _net.Calls.Count;
            Assert.That(BuyShown(1, 1), Is.False);
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantModels.NotHereText));

            // 到了本线:不在架上的商品、超过限购的份数、0 份。
            Advance(MysteryMerchantClient.MinInfoIntervalSeconds);
            ArriveOnMerchantLine();
            sent = _net.Calls.Count;
            Assert.That(BuyShown(99, 1), Is.False);
            Assert.That(BuyShown(2, 2), Is.False, "商品 2 限购 1");
            Assert.That(BuyShown(1, 0), Is.False);
            Assert.That(BuyShown(1, 6), Is.False, "商品 1 限购 5");

            // 战斗中。
            _client.InBattle = true;
            Assert.That(BuyShown(1, 1), Is.False);
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantModels.InBattleText));
            _client.InBattle = false;

            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
        }

        [Test]
        public void Buy_WhileOnePending_OrTooSoon_IsRejectedLocally()
        {
            ArriveOnMerchantLine();
            Assert.That(BuyShown(1, 1), Is.True);
            int sent = _net.Calls.Count;
            Assert.That(BuyShown(1, 1), Is.False, "上一次还在途");
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantClient.BuyPendingMessage), "在途时不改状态行");

            LastCall.Reply(new global::BuyMysteryMerchantGoodsResponse { Info = ActiveInfo(Appear + 61_000, here: true) });
            Assert.That(BuyShown(1, 1), Is.False, "距上一次不足最小间隔");
            Assert.That(_client.Status, Is.EqualTo(MysteryMerchantClient.TooFastMessage));
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));

            Advance(MysteryMerchantClient.MinBuyIntervalSeconds);
            Assert.That(BuyShown(1, 1), Is.True);
        }

        [Test]
        public void Buy_IsNeverBlockedByALocalBalanceSnapshot()
        {
            ArriveOnMerchantLine();
            BuyShown(1, 1);
            var bag = new global::BagInfo { Currency = new global::CurrencyComp() };
            bag.Currency.Values.Add(100);   // 买完那一刻只剩 100,不够再买一份 240 的
            LastCall.Reply(new global::BuyMysteryMerchantGoodsResponse { Info = ActiveInfo(Appear + 61_000, here: true), Bag = bag });

            // 那是一份快照:之后玩家在别处赚了钱,客户端并不知道(scene 不推送余额)。
            // 按它拦下来的话,玩家攒够了钱也点不了,而且没有办法自己解开 —— 所以照发,由服务端裁决。
            Advance(MysteryMerchantClient.MinBuyIntervalSeconds);
            int sent = _net.Calls.Count;
            Assert.That(BuyShown(1, 1), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 1));

            // 真的不够时,服务端会说。
            LastCall.Reply(new global::BuyMysteryMerchantGoodsResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)mystery_merchant_error.KMysteryMerchantCurrencyNotEnough },
            });
            Assert.That(_client.Status, Does.Contain("货币不足"));
            Assert.That(_client.StatusIsError, Is.True);
        }

        [Test]
        public void Buy_AfterTheMerchantLeft_IsRejectedLocally_EvenWithAStaleShelf()
        {
            ArriveOnMerchantLine(Appear + Stay - 2_000);
            Advance(3f);   // 过了离开时刻
            int sent = _net.Calls.Count;
            Assert.That(BuyShown(1, 1), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
        }

        [Test]
        public void Dispose_StopsReactingToPushesAndCallbacks()
        {
            _client.RequestInfo();
            var call = LastCall;
            _client.Dispose();
            call.Reply(new global::GetMysteryMerchantInfoResponse { Info = ActiveInfo(Appear + 60_000, here: false) });
            _net.Push(MessageIds.NotifyMysteryMerchant,
                new global::MysteryMerchantNotify { Info = ActiveInfo(Appear + 60_000, here: false) });
            Assert.That(_client.HasInfo, Is.False);
            Assert.That(_appeared, Is.Empty);
            Assert.That(_net.DisconnectedSubscriberCount, Is.EqualTo(0));
        }
    }
}
