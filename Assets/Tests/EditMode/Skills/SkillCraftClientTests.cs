using System;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.Skills;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Skills;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Skills
{
    /// <summary>
    /// SkillCraftClient 与 SkillCraftModels 的无头单测：覆盖分卷宗制书、未学书架研习/拆页、
    /// 主城名匠代打、幂等重放恢复、战斗写锁与纯资格推导。
    /// </summary>
    public sealed class SkillCraftClientTests
    {
        private const ulong Player = 9201;
        private string _directory;
        private DurableRequestFileStore _store;
        private SkillFakeTransport _net;
        private object _connection;
        private long _now;
        private bool _battleLocked;
        private SkillCraftClient _client;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "skill-craft-test-" + Guid.NewGuid().ToString("N"));
            _store = new DurableRequestFileStore(_directory);
            _net = new SkillFakeTransport { PlayerId = Player, IsReady = true };
            _connection = new object();
            _now = 10000;
            _battleLocked = false;
            CreateClient();
        }

        private void CreateClient()
        {
            _client = new SkillCraftClient(_net, _net, () => _connection, () => _battleLocked)
            {
                RecoveryStore = _store,
                NowMs = () => _now,
            };
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private static SkillCraftingStateSnapshot MakeState(ulong revision = 1, uint scrollTier = 2, uint forgeTier = 3,
            ulong fragments = 60, ulong rareScroll = 1, ulong rareInk = 1, ulong gold = 500)
        {
            var state = new SkillCraftingStateSnapshot
            {
                ScrollCraftRevision = revision,
                ForgeMastery = new SkillCraftMasterySnapshot { Tier = forgeTier, TierName = forgeTier >= 3 ? "宗师" : "学徒", BonusRateBps = forgeTier >= 3 ? 1000u : 500u },
                ScrollMastery = new SkillCraftMasterySnapshot { Tier = scrollTier, TierName = scrollTier >= 3 ? "宗师" : "巧匠", Exp = 20, NextTierExp = 50, BonusRateBps = scrollTier >= 3 ? 1000u : 500u },
                GoldBalance = gold,
                FragmentOwned = fragments,
                RareScrollOwned = rareScroll,
                RareInkOwned = rareInk,
            };
            var regular = new SkillScrollCatalogInfo
            {
                ScrollId = 3, Name = "兵法成长卷", RarityTier = 0,
                FragmentItemId = 3406, RandomFragmentCount = 20, RandomGoldCost = 50,
                TargetedFragmentCount = 40, TargetedGoldCost = 100, DismantleFragmentCount = 16,
            };
            regular.SkillTableIds.Add(12020);
            regular.SkillTableIds.Add(12021);
            regular.SkillTableIds.Add(12022);
            var rare = new SkillScrollCatalogInfo
            {
                ScrollId = 2, Name = "首发绝学卷", RarityTier = 1,
                RareScrollItemId = 3407, RareScrollCount = 1,
                RareInkItemId = 3408, RareInkCount = 1, RareGoldCost = 100,
            };
            rare.SkillTableIds.Add(10041);
            state.Scrolls.Add(regular);
            state.Scrolls.Add(rare);
            return state;
        }

        private void LoadState(SkillCraftingStateSnapshot state = null)
        {
            _client.RefreshState();
            Assert.That(_net.Calls[^1], Is.EqualTo(SceneSkillClientPlayerRpc.GetSkillCraftingStateMessageId));
            _net.Reply(new GetSkillCraftingStateResponse { State = state ?? MakeState() });
        }

        [Test]
        public void RefreshStateAndProxyStallsStoreClonedSnapshots()
        {
            LoadState();
            var snap = _client.State;
            Assert.That(snap.Scrolls.Count, Is.EqualTo(2));
            snap.GoldBalance = 999999;
            Assert.That(_client.State.GoldBalance, Is.EqualTo(500UL), "修改副本不得污染内部状态");

            _client.RefreshProxyStalls();
            Assert.That(_net.Calls[^1], Is.EqualTo(SceneSkillClientPlayerRpc.ListCraftProxyStallsMessageId));
            var stallsResp = new ListCraftProxyStallsResponse();
            stallsResp.Stalls.Add(new CraftProxyStallInfo
            {
                MasterPlayerId = 777, MasterName = "清风道人",
                ForgeMasteryTier = 3, ScrollMasteryTier = 3,
                TopQualityRateBps = 1000, ScrollBonusRateBps = 1000,
                ProxyForgeFeeGold = 80, ProxyScrollFeeGold = 60,
            });
            _net.Reply(stallsResp);
            Assert.That(_client.ProxyStalls.Count, Is.EqualTo(1));
            Assert.That(SkillCraftModels.DescribeProxyStall(_client.ProxyStalls[0]),
                Does.Contain("清风道人").And.Contain("80 银两").And.Contain("60 银两"));
        }

        [Test]
        public void CraftLearnAndDismantleFlowUsesRevisionAndUpdatesState()
        {
            LoadState(MakeState(revision: 5));
            CraftSkillBookResponse crafted = null;
            LearnSkillBookResponse learned = null;
            DismantleSkillBookResponse dismantled = null;
            _client.BookCrafted += r => crafted = r;
            _client.BookLearned += r => learned = r;
            _client.BookDismantled += r => dismantled = r;

            // 1. 常规卷定向拓印
            _client.CraftRegularTargeted(3, 12020, proxyMasterPlayerId: 777);
            var craftReq = (CraftSkillBookRequest)_net.LastRequest;
            Assert.That(craftReq.Mode, Is.EqualTo(CraftSkillBookRequest.Types.CraftMode.TargetedRegular));
            Assert.That(craftReq.ScrollId, Is.EqualTo(3u));
            Assert.That(craftReq.TargetSkillTableId, Is.EqualTo(12020u));
            Assert.That(craftReq.ExpectedRevision, Is.EqualTo(5UL));
            Assert.That(craftReq.ProxyMasterPlayerId, Is.EqualTo(777UL));
            Assert.That(craftReq.ClientRequestId, Is.Not.Zero);

            var nextState = MakeState(revision: 6, fragments: 22, gold: 340);
            nextState.SkillBooks.Add(new CraftedSkillBookEntry
            {
                BookId = 101, SkillTableId = 12020, ScrollId = 3, RarityTier = 0, Bound = false, CrafterName = "清风道人",
            });
            _net.Reply(new CraftSkillBookResponse
            {
                ClientRequestId = craftReq.ClientRequestId,
                CraftedBook = nextState.SkillBooks[0],
                BonusFragmentsGranted = 2,
                State = nextState,
            });
            Assert.That(crafted.BonusFragmentsGranted, Is.EqualTo(2u));
            Assert.That(_client.Message, Does.Contain("返还 2 张"));
            Assert.That(_client.HasPendingWrite, Is.False);

            // 2. 拆页返还 16 残页
            _now += 5000;
            _client.DismantleBook(101);
            var disReq = (DismantleSkillBookRequest)_net.LastRequest;
            Assert.That(disReq.BookId, Is.EqualTo(101UL));
            Assert.That(disReq.ExpectedRevision, Is.EqualTo(6UL));
            var afterDismantle = MakeState(revision: 7, fragments: 38, gold: 340);
            afterDismantle.SkillBooks.Add(new CraftedSkillBookEntry
            {
                BookId = 102, SkillTableId = 10041, ScrollId = 2, RarityTier = 1, Bound = true,
            });
            _net.Reply(new DismantleSkillBookResponse
            {
                ClientRequestId = disReq.ClientRequestId,
                RefundedFragments = 16,
                State = afterDismantle,
            });
            Assert.That(dismantled.RefundedFragments, Is.EqualTo(16u));
            Assert.That(_client.Message, Does.Contain("16"));

            // 3. 研习稀世书给人物
            _now += 5000;
            _client.LearnBook(102, petId: 0);
            var learnReq = (LearnSkillBookRequest)_net.LastRequest;
            Assert.That(learnReq.BookId, Is.EqualTo(102UL));
            Assert.That(learnReq.ExpectedRevision, Is.EqualTo(7UL));
            var afterLearn = MakeState(revision: 8, fragments: 38, gold: 340);
            afterLearn.BookUnlockedSkillIds.Add(10041);
            _net.Reply(new LearnSkillBookResponse
            {
                ClientRequestId = learnReq.ClientRequestId,
                LearnedSkillTableId = 10041,
                State = afterLearn,
            });
            Assert.That(learned.LearnedSkillTableId, Is.EqualTo(10041u));
            Assert.That(_client.State.BookUnlockedSkillIds, Does.Contain(10041u));
        }

        [Test]
        public void TimeoutPersistsAndReplaysExactClientRequestIdAcrossRecreate()
        {
            LoadState(MakeState(revision: 9));
            _client.CraftRareTargeted(2, 10041);
            var firstReq = ((CraftSkillBookRequest)_net.LastRequest).Clone();
            _net.Fail(RpcStatus.Timeout("rpc timeout"));

            Assert.That(_client.HasPendingWrite, Is.True);
            Assert.That(_store.Load(Player), Is.Not.Empty);

            // 新写操作在原意图未确认前必须被拦截。
            int callsBefore = _net.Calls.Count;
            _client.CraftRegularRandom(3);
            Assert.That(_net.Calls.Count, Is.EqualTo(callsBefore));

            // 模拟断线重建客户端并重放原字节。
            _net.Disconnect();
            _client.Dispose();
            _net.IsReady = true;
            CreateClient();
            _now += 10000;
            _client.TickRecovery();

            Assert.That(_net.Calls[^1], Is.EqualTo(SceneSkillClientPlayerRpc.CraftSkillBookMessageId));
            var replayedReq = (CraftSkillBookRequest)_net.LastRequest;
            Assert.That(replayedReq.ClientRequestId, Is.EqualTo(firstReq.ClientRequestId));
            Assert.That(replayedReq.ExpectedRevision, Is.EqualTo(9UL));
            Assert.That(replayedReq.Mode, Is.EqualTo(CraftSkillBookRequest.Types.CraftMode.TargetedRare));

            _net.Reply(new CraftSkillBookResponse
            {
                ClientRequestId = firstReq.ClientRequestId,
                Replayed = true,
                State = MakeState(revision: 10, rareScroll: 0, rareInk: 0, gold: 400),
            });
            Assert.That(_client.HasPendingWrite, Is.False);
            Assert.That(_store.Load(Player), Is.Empty);
            Assert.That(_client.State.ScrollCraftRevision, Is.EqualTo(10UL));
        }

        [Test]
        public void BattleLockBlocksWritesAndModelsValidateScrollAndProxyRules()
        {
            var state = MakeState(revision: 2, scrollTier: 2, forgeTier: 3, fragments: 25, rareScroll: 1, rareInk: 0, gold: 120);
            LoadState(state);

            _battleLocked = true;
            int before = _net.Calls.Count;
            _client.CraftRegularRandom(3);
            _client.SetProxyStall(true, 80, 0);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Message, Does.Contain("战斗中"));

            _battleLocked = false;
            _client.SetProxyStall(true, 80, 0);
            Assert.That(_net.Calls[^1], Is.EqualTo(SceneSkillClientPlayerRpc.SetCraftProxyStallMessageId));
            var stallReq = (SetCraftProxyStallRequest)_net.LastRequest;
            Assert.That(stallReq.Open, Is.True);
            Assert.That(stallReq.ProxyForgeFeeGold, Is.EqualTo(80UL));
            _net.Reply(new SetCraftProxyStallResponse { State = state });
            Assert.That(_client.Message, Does.Contain("已开启"));

            // 纯资格模型断言
            var regular = state.Scrolls[0];
            var rare = state.Scrolls[1];
            Assert.That(SkillCraftModels.CanCraftRegularRandom(state, regular, 0, out _), Is.True);
            Assert.That(SkillCraftModels.CanCraftRegularTargeted(state, regular, 12020, 0, out string shortFrag), Is.False);
            Assert.That(shortFrag, Does.Contain("悟道残页不足"));
            Assert.That(SkillCraftModels.CanCraftRareTargeted(state, rare, 10041, 0, out string shortInk), Is.False);
            Assert.That(shortInk, Does.Contain("天工秘墨不足"));
            Assert.That(SkillCraftModels.CanOpenProxyStall(state), Is.True);
            Assert.That(SkillCraftModels.IsValidProxyFee(49), Is.False);
            Assert.That(SkillCraftModels.IsValidProxyFee(50), Is.True);
            Assert.That(SkillCraftModels.IsValidProxyFee(100), Is.True);
            Assert.That(SkillCraftModels.IsValidProxyFee(101), Is.False);
            Assert.That(SkillCraftModels.CanDismantleBook(new CraftedSkillBookEntry { RarityTier = 0 }), Is.True);
            Assert.That(SkillCraftModels.CanDismantleBook(new CraftedSkillBookEntry { RarityTier = 1 }), Is.False);
            Assert.That(SkillCraftModels.DescribeBook(
                new CraftedSkillBookEntry { BookId = 1, SkillTableId = 10041, ScrollId = 2, RarityTier = 1, Bound = true, CrafterName = "玄机子" },
                state, _ => "玄镜映法"),
                Is.EqualTo("玄镜映法（首发绝学卷 · 稀世绝学 · 仅自用 · 宗师·玄机子 亲手撰）"));
        }

        [Test]
        public void SealAndUnsealBookFlowUsesRevisionIdempotencyAndGuardsBoundBooks()
        {
            Assert.That(SkillCraftModels.CanSealBook(new CraftedSkillBookEntry { RarityTier = 0, Bound = false }), Is.True);
            Assert.That(SkillCraftModels.CanSealBook(new CraftedSkillBookEntry { RarityTier = 0, Bound = true }), Is.False);
            Assert.That(SkillCraftModels.CanSealBook(new CraftedSkillBookEntry { RarityTier = 1, Bound = false }), Is.False);

            var init = MakeState(revision: 3);
            init.SkillBooks.Add(new CraftedSkillBookEntry
            {
                BookId = 55, SkillTableId = 12020, ScrollId = 3, RarityTier = 0, Bound = false, CrafterName = "欧冶子",
            });
            LoadState(init);

            SealSkillBookResponse sealedResp = null;
            UnsealSkillBookResponse unsealedResp = null;
            _client.BookSealed += r => sealedResp = r;
            _client.BookUnsealed += r => unsealedResp = r;

            // 1. 封卷入包：超时断线后保留原幂等键恢复重放。
            _client.SealBook(55);
            Assert.That(_net.Calls[^1], Is.EqualTo(SceneSkillClientPlayerRpc.SealSkillBookMessageId));
            var sealReq = (SealSkillBookRequest)_net.LastRequest;
            Assert.That(sealReq.BookId, Is.EqualTo(55UL));
            Assert.That(sealReq.ExpectedRevision, Is.EqualTo(3UL));
            Assert.That(sealReq.ClientRequestId, Is.Not.Zero);

            _net.Fail(RpcStatus.Timeout("timeout"));
            Assert.That(_client.HasPendingWrite, Is.True);
            Assert.That(_store.Load(Player), Does.StartWith("seal:"));

            _client.Dispose();
            CreateClient();
            _client.BookSealed += r => sealedResp = r;
            _client.BookUnsealed += r => unsealedResp = r;
            _now += 5000;
            _client.RetryPendingWrite();
            var replayedSeal = (SealSkillBookRequest)_net.LastRequest;
            Assert.That(replayedSeal.ClientRequestId, Is.EqualTo(sealReq.ClientRequestId));
            _net.Reply(new SealSkillBookResponse
            {
                ClientRequestId = sealReq.ClientRequestId,
                Replayed = true,
                ItemId = 888001,
                SkillTableId = 12020,
                State = MakeState(revision: 4),
            });
            Assert.That(sealedResp.ItemId, Is.EqualTo(888001UL));
            Assert.That(_client.HasPendingWrite, Is.False);

            // 2. 背包封卷道籍开卷回书架。
            _now += 5000;
            _client.UnsealBook(888001);
            Assert.That(_net.Calls[^1], Is.EqualTo(SceneSkillClientPlayerRpc.UnsealSkillBookMessageId));
            var unsealReq = (UnsealSkillBookRequest)_net.LastRequest;
            Assert.That(unsealReq.ItemId, Is.EqualTo(888001UL));
            Assert.That(unsealReq.ExpectedRevision, Is.EqualTo(4UL));
            var restoredState = MakeState(revision: 5);
            restoredState.SkillBooks.Add(new CraftedSkillBookEntry
            {
                BookId = 56, SkillTableId = 12020, ScrollId = 3, RarityTier = 0, Bound = false, CrafterName = "欧冶子",
            });
            _net.Reply(new UnsealSkillBookResponse
            {
                ClientRequestId = unsealReq.ClientRequestId,
                UnsealedBook = restoredState.SkillBooks[0],
                State = restoredState,
            });
            Assert.That(unsealedResp.UnsealedBook.BookId, Is.EqualTo(56UL));
            Assert.That(_client.State.SkillBooks.Count, Is.EqualTo(1));
            Assert.That(SkillCraftModels.DescribeSealedBook(new BagItemInfo
            {
                ItemId = 888001, ConfigId = 3414, Bound = false,
                SealedSkillBook = new SealedSkillBookInfo { SkillTableId = 12020, SkillName = "愈战愈勇", ScrollId = 3, ScrollName = "兵法成长卷", CrafterName = "欧冶子" },
            }), Is.EqualTo("【背包封卷】愈战愈勇（兵法成长卷 · 可交易 · 宗师·欧冶子 亲手撰）"));
        }

        [Test]
        public void V2ToV5ProxySortMilestonesBatchDismantleAdvisorAndWantedBoardWorkEndToEnd()
        {
            // ── V2: 西市名匠口碑排序、筛选与雅号招牌设置 ──
            var stalls = new[]
            {
                new CraftProxyStallInfo { MasterPlayerId = 10, MasterName = "甲", ForgeMasteryTier = 3, ProxyForgeFeeGold = 90, ProxyOrdersCompleted = 12, SupremeForgedCount = 2, MasterShowcaseTag = 1, EquippedCraftTitleId = 1 },
                new CraftProxyStallInfo { MasterPlayerId = 20, MasterName = "乙", ForgeMasteryTier = 3, ScrollMasteryTier = 3, ProxyForgeFeeGold = 80, ProxyScrollFeeGold = 70, ProxyOrdersCompleted = 30, SupremeForgedCount = 15, MasterShowcaseTag = 4, EquippedCraftTitleId = 6, TeamFreeProxy = true, ViewerGetsFreeProxy = true },
            };
            var byRep = SkillCraftModels.SortAndFilterProxyStalls(stalls, ProxyStallSortMode.Reputation, ProxyStallFilterMode.All);
            Assert.That(byRep[0].MasterPlayerId, Is.EqualTo(20UL));
            var onlyTeamFree = SkillCraftModels.SortAndFilterProxyStalls(stalls, ProxyStallSortMode.LowestFee, ProxyStallFilterMode.TeamFree);
            Assert.That(onlyTeamFree.Count, Is.EqualTo(1));
            Assert.That(SkillCraftModels.DescribeProxyStall(byRep[0]), Does.Contain("双绝天工·乙"));
            Assert.That(SkillCraftModels.DescribeProxyStall(byRep[0]), Does.Contain("同队0费情义代打"));

            // ── V4: 藏经阁里程碑位图、批量安全拆页与六大流派体检 ──
            var state = MakeState(revision: 10, fragments: 50, gold: 2000);
            state.BookUnlockedSkillIds.Add(12020);
            state.BookUnlockedSkillIds.Add(12021); // 卷 3 共 3 技 -> 已解锁 2 >= ceil(3/2)=2
            var status = SkillCraftModels.GetScrollMilestoneStatus(state, state.Scrolls[0]);
            Assert.That(status.HalfClaimable, Is.True);
            Assert.That(status.FullClaimable, Is.False);

            // 书架：已学常规书(1)、重复未学常规书(2,3)、稀世书(4) -> 安全批量拆页仅选 1 与 3（留孤本 2 与稀世 4）
            state.SkillBooks.Add(new CraftedSkillBookEntry { BookId = 1, SkillTableId = 12020, ScrollId = 3, RarityTier = 0 });
            state.SkillBooks.Add(new CraftedSkillBookEntry { BookId = 2, SkillTableId = 12022, ScrollId = 3, RarityTier = 0 });
            state.SkillBooks.Add(new CraftedSkillBookEntry { BookId = 3, SkillTableId = 12022, ScrollId = 3, RarityTier = 0 });
            state.SkillBooks.Add(new CraftedSkillBookEntry { BookId = 4, SkillTableId = 10041, ScrollId = 2, RarityTier = 1 });
            var safeBatch = SkillCraftModels.SelectSafeBatchDismantleBookIds(state);
            Assert.That(safeBatch, Is.EqualTo(new[] { 1UL, 3UL }));

            var advisor = SkillCraftModels.EvaluateBuildBookAdvisor(state);
            Assert.That(advisor.Count, Is.EqualTo(6));
            Assert.That(advisor[0].UnlockedCount, Is.EqualTo(1)); // 物理破阵流含 12020

            LoadState(state);
            ClaimScrollCollectionMilestoneResponse milestoneResp = null;
            BatchDismantleSkillBooksResponse batchResp = null;
            PostSkillBookWantedOrderResponse postResp = null;
            CancelSkillBookWantedOrderResponse cancelResp = null;
            FulfillSkillBookWantedOrderResponse fulfillResp = null;
            _client.ScrollMilestoneClaimed += r => milestoneResp = r;
            _client.BooksBatchDismantled += r => batchResp = r;
            _client.WantedOrderPosted += r => postResp = r;
            _client.WantedOrderCanceled += r => cancelResp = r;
            _client.WantedOrderFulfilled += r => fulfillResp = r;

            _client.ClaimScrollMilestone(3, 1);
            var mReq = (ClaimScrollCollectionMilestoneRequest)_net.LastRequest;
            Assert.That(mReq.ScrollId, Is.EqualTo(3u));
            Assert.That(mReq.MilestoneTier, Is.EqualTo(1u));
            _net.Reply(new ClaimScrollCollectionMilestoneResponse
            {
                ClientRequestId = mReq.ClientRequestId,
                RewardedFragments = 20,
                State = MakeState(revision: 11, fragments: 70, gold: 2000),
            });
            Assert.That(milestoneResp.RewardedFragments, Is.EqualTo(20u));

            _now += 5000;
            _client.BatchDismantleBooks(safeBatch);
            var bReq = (BatchDismantleSkillBooksRequest)_net.LastRequest;
            Assert.That(bReq.BookIds, Is.EqualTo(safeBatch));
            _net.Reply(new BatchDismantleSkillBooksResponse
            {
                ClientRequestId = bReq.ClientRequestId,
                DismantledCount = 2,
                RefundedFragments = 32,
                State = MakeState(revision: 12, fragments: 102, gold: 2000),
            });
            Assert.That(batchResp.RefundedFragments, Is.EqualTo(32u));

            // ── V5: 西市悬赏求书板发榜 / 撤单 / 列表 / 匹配揭榜交卷 ──
            _now += 5000;
            _client.PostWantedOrder(12020, 1000, requireMasterSignature: true);
            var pReq = (PostSkillBookWantedOrderRequest)_net.LastRequest;
            Assert.That(pReq.RewardGold, Is.EqualTo(1000UL));
            Assert.That(pReq.RequireMasterSignature, Is.True);
            var orderInfo = new SkillBookWantedOrderInfo
            {
                OrderId = 7, PosterPlayerId = 888, PosterName = "买书道友",
                SkillTableId = 12020, SkillName = "愈战愈勇", ScrollId = 3,
                RequireMasterSignature = true, RewardGold = 1000, SellerNetGold = 950,
            };
            _net.Reply(new PostSkillBookWantedOrderResponse
            {
                ClientRequestId = pReq.ClientRequestId,
                Order = orderInfo,
                State = MakeState(revision: 13, fragments: 102, gold: 1000),
            });
            Assert.That(postResp.Order.OrderId, Is.EqualTo(7UL));

            _now += 5000;
            _client.CancelWantedOrder(7);
            var cReq = (CancelSkillBookWantedOrderRequest)_net.LastRequest;
            _net.Reply(new CancelSkillBookWantedOrderResponse
            {
                ClientRequestId = cReq.ClientRequestId,
                RefundedGold = 1000,
                State = MakeState(revision: 14, fragments: 102, gold: 2000),
            });
            Assert.That(cancelResp.RefundedGold, Is.EqualTo(1000UL));

            _client.RefreshWantedOrders();
            _net.Reply(new ListSkillBookWantedOrdersResponse { Orders = { orderInfo } });
            Assert.That(_client.WantedOrders.Count, Is.EqualTo(1));

            // 揭榜匹配：仅接受未绑定且满足宗师署名要求的 3414 封卷道籍
            var bag = new BagInfo();
            bag.Items.Add(new BagItemInfo
            {
                ItemId = 9002, ConfigId = 3414, Count = 1, Bound = false,
                SealedSkillBook = new SealedSkillBookInfo { SkillTableId = 12020, RarityTier = 0, CrafterName = "双绝天工·欧冶子", CrafterPlayerId = 20 },
            });
            var match = SkillCraftModels.FindMatchingFulfillSource(orderInfo, _client.State, bag);
            Assert.That(match.SealedItemId, Is.EqualTo(9002UL));

            _now += 5000;
            _client.FulfillWantedOrderWithSealedItem(888, 7, 9002);
            var fReq = (FulfillSkillBookWantedOrderRequest)_net.LastRequest;
            Assert.That(fReq.SealedItemId, Is.EqualTo(9002UL));
            _net.Reply(new FulfillSkillBookWantedOrderResponse
            {
                ClientRequestId = fReq.ClientRequestId,
                OrderId = 7,
                SellerNetGold = 950,
                State = MakeState(revision: 15, fragments: 102, gold: 2950),
            });
            Assert.That(fulfillResp.SellerNetGold, Is.EqualTo(950UL));
        }

        [Test]
        public void ProxyDigestAckHotRankSortTopHotAndTitleMaskDecodeWorkEndToEnd()
        {
            var stalls = new[]
            {
                new CraftProxyStallInfo { MasterPlayerId = 10, MasterName = "甲", ForgeMasteryTier = 3, ProxyForgeFeeGold = 90, ProxyOrdersCompleted = 20, SupremeForgedCount = 2, Recent24HOrders = 1, Recent24HSupreme = 0, HotRank = 3 },
                new CraftProxyStallInfo { MasterPlayerId = 20, MasterName = "乙", ForgeMasteryTier = 3, ProxyForgeFeeGold = 80, ProxyOrdersCompleted = 5, SupremeForgedCount = 5, Recent24HOrders = 2, Recent24HSupreme = 2, HotRank = 1, EquippedCraftTitleId = 2 },
                new CraftProxyStallInfo { MasterPlayerId = 30, MasterName = "丙", ScrollMasteryTier = 3, ProxyScrollFeeGold = 70, ProxyOrdersCompleted = 10, SupremeForgedCount = 3, Recent24HOrders = 3, Recent24HSupreme = 1, HotRank = 2 },
                new CraftProxyStallInfo { MasterPlayerId = 40, MasterName = "丁", ForgeMasteryTier = 3, ProxyForgeFeeGold = 60, ProxyOrdersCompleted = 0, SupremeForgedCount = 0, Recent24HOrders = 0, Recent24HSupreme = 0, HotRank = 0 },
            };

            var byHot = SkillCraftModels.SortAndFilterProxyStalls(stalls, ProxyStallSortMode.HotRank, ProxyStallFilterMode.All);
            Assert.That(byHot.Count, Is.EqualTo(4));
            Assert.That(byHot[0].MasterPlayerId, Is.EqualTo(20UL));
            Assert.That(byHot[1].MasterPlayerId, Is.EqualTo(30UL));
            Assert.That(byHot[2].MasterPlayerId, Is.EqualTo(10UL));
            Assert.That(byHot[3].MasterPlayerId, Is.EqualTo(40UL));

            var topHot = SkillCraftModels.TopHotStalls(stalls);
            Assert.That(topHot.Count, Is.EqualTo(3));
            Assert.That((topHot[0].MasterPlayerId, topHot[0].HotRank), Is.EqualTo((20UL, 1u)));
            Assert.That((topHot[1].MasterPlayerId, topHot[1].HotRank), Is.EqualTo((30UL, 2u)));
            Assert.That((topHot[2].MasterPlayerId, topHot[2].HotRank), Is.EqualTo((10UL, 3u)));
            Assert.That(SkillCraftModels.DescribeProxyStall(topHot[0]), Does.Contain("【热榜榜首】"));
            Assert.That(SkillCraftModels.DescribeProxyStall(topHot[0]), Does.Contain("近24h 2单/2极品"));

            var digest = new CraftProxyDigestSummary
            {
                OrdersCompleted = 3,
                SupremeForgedCount = 2,
                RareBooksCraftedCount = 1,
                NetGoldEarned = 266,
                LastBuyerName = "青云侠士",
                NewlyUnlockedTitleMask = (1u << 1) | (1u << 5), // 百炼宗师(2) + 双绝天工(6)
            };
            Assert.That(SkillCraftModels.DecodeUnlockedTitleNames(digest.NewlyUnlockedTitleMask),
                Is.EqualTo(new[] { "百炼宗师", "双绝天工" }));
            string lines = SkillCraftModels.FormatDigestLines(digest);
            Assert.That(lines, Does.Contain("3 单"));
            Assert.That(lines, Does.Contain("266 银两"));
            Assert.That(lines, Does.Contain("青云侠士"));
            Assert.That(lines, Does.Contain("百炼宗师、双绝天工"));

            var stateWithDigest = MakeState(revision: 20, fragments: 50, gold: 2000);
            stateWithDigest.UnreadProxyDigest = digest;
            LoadState(stateWithDigest);
            Assert.That(_client.HasUnreadProxyDigest, Is.True);

            AckCraftProxyDigestResponse acked = null;
            _client.ProxyDigestAcked += r => acked = r;
            _client.AckProxyDigest();
            Assert.That(_net.LastRequest, Is.InstanceOf<AckCraftProxyDigestRequest>());
            _net.Reply(new AckCraftProxyDigestResponse { State = MakeState(revision: 20, fragments: 50, gold: 2000) });
            Assert.That(acked, Is.Not.Null);
            Assert.That(_client.HasUnreadProxyDigest, Is.False);
            Assert.That(_client.Error, Is.Empty);
        }

        [Test]
        public void CraftProxyDigestSpriteResourcesAndSliceMetaContractsAreComplete()
        {
            string projectRoot = Directory.Exists(Path.Combine("Assets", "Resources", "UI", "Ugui", "CraftProxyDigest"))
                ? "."
                : "mmorpg-client";
            string assetsDir = Path.Combine(projectRoot, "Assets", "Resources", "UI", "Ugui", "CraftProxyDigest");
            Assert.That(Directory.Exists(assetsDir), Is.True, $"Missing CraftProxyDigest directory: {assetsDir}");
            Assert.That(File.Exists(assetsDir + ".meta"), Is.True, "Missing CraftProxyDigest.meta");

            string artCatalogSource = File.ReadAllText(Path.Combine(projectRoot, "Assets", "Scripts", "UI", "Ugui", "SeasonEconomyUiArt.cs"));
            Assert.That(artCatalogSource, Does.Contain("CraftProxyDigestRoot = \"UI/Ugui/CraftProxyDigest/\";"));

            var expectedSpecs = new Dictionary<string, (int W, int H, string Border)>
            {
                ["panel_proxy_digest"] = (1200, 820, "{x: 64, y: 64, z: 64, w: 64}"),
                ["banner_hot_masters"] = (1400, 96, "{x: 120, y: 0, z: 120, w: 0}"),
                ["card_hot_master"] = (440, 168, "{x: 32, y: 32, z: 32, w: 32}"),
                ["badge_hot_rank_1"] = (128, 128, "{x: 0, y: 0, z: 0, w: 0}"),
                ["badge_hot_rank_2"] = (128, 128, "{x: 0, y: 0, z: 0, w: 0}"),
                ["badge_hot_rank_3"] = (128, 128, "{x: 0, y: 0, z: 0, w: 0}"),
                ["badge_title_unlocked"] = (480, 76, "{x: 72, y: 0, z: 72, w: 0}"),
                ["icon_digest_scroll"] = (256, 256, "{x: 0, y: 0, z: 0, w: 0}"),
            };

            foreach (var kv in expectedSpecs)
            {
                string key = kv.Key;
                var spec = kv.Value;
                Assert.That(artCatalogSource, Does.Contain($"\"{key}\""), $"SeasonEconomyUiArt.CraftProxyDigestAssetKeys missing {key}");

                string pngPath = Path.Combine(assetsDir, key + ".png");
                string metaPath = pngPath + ".meta";
                Assert.That(File.Exists(pngPath), Is.True, $"Missing sprite PNG: {pngPath}");
                Assert.That(File.Exists(metaPath), Is.True, $"Missing sprite meta: {metaPath}");

                byte[] header = new byte[24];
                using (var fs = File.OpenRead(pngPath))
                {
                    Assert.That(fs.Read(header, 0, header.Length), Is.EqualTo(24));
                }
                Assert.That(header[0], Is.EqualTo(0x89), $"{key} is not a PNG binary");
                Assert.That(header[1], Is.EqualTo(0x50), $"{key} is not a PNG binary");
                int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                Assert.That((width, height), Is.EqualTo((spec.W, spec.H)), $"Dimension mismatch for {key}");

                string meta = File.ReadAllText(metaPath);
                Assert.That(meta, Does.Contain("textureType: 8"), $"{key} meta must import as Sprite");
                Assert.That(meta, Does.Contain("alphaIsTransparency: 1"), $"{key} meta must keep alpha transparency");
                Assert.That(meta, Does.Contain("enableMipMap: 0"), $"{key} meta must disable mipmaps");
                Assert.That(meta, Does.Contain("spriteBorder: " + spec.Border), $"{key} meta border mismatch");
            }
        }
    }
}
