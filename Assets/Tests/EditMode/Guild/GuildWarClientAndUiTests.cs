using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Guild;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Guild
{
    public sealed class GuildWarClientAndUiTests
    {
        private WarFakeTransport _net;
        private long _monoMs;
        private GuildWarClient _client;

        [SetUp]
        public void SetUp()
        {
            _monoMs = 10_000;
            _net = new WarFakeTransport { PlayerId = 2001UL, IsReady = true };
            _client = new GuildWarClient(_net, monotonicMs: () => _monoMs);
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
        }

        [Test]
        public void GuildWarV2DeliveredAssets_LoadAll31SpritesWithExpectedThreeSliceBorders()
        {
            Assert.That(GuildWarUiArt.AllAssetKeys.Length, Is.EqualTo(31));
            foreach (string key in GuildWarUiArt.AllAssetKeys)
            {
                var sprite = GuildWarUiArt.Load(key);
                Assert.That(sprite, Is.Not.Null, "Missing GuildWarV2 sprite: " + key);
                Assert.That(sprite.texture, Is.Not.Null, "Missing texture for: " + key);
            }

            AssertHorizontalBorder("bar_banner_hp_track", 18f);
            AssertHorizontalBorder("bar_banner_hp_fill_ally", 18f);
            AssertHorizontalBorder("bar_banner_hp_fill_enemy", 18f);
            AssertHorizontalBorder("alert_banner_siege", 110f);
            AssertHorizontalBorder("alert_banner_bounty", 110f);
            AssertHorizontalBorder("btn_danger_crimson", 35f);
        }

        [Test]
        public void OverviewStanceGarrisonMilestonesDividendBountyAndConflictSpectateFlow()
        {
            _client.RefreshOverview();
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.GetGuildWarOverviewMessageId));
            _net.Reply(OverviewFixture());

            Assert.That(_client.Overview, Is.Not.Null);
            Assert.That(_client.Overview.Leylines.Count, Is.EqualTo(3));
            Assert.That(_client.Overview.Milestones.Count, Is.EqualTo(4));
            Assert.That(_client.Overview.HasClaimableReward, Is.True);
            Assert.That(_client.EstimatedServerNowMs, Is.EqualTo(1_795_430_400_000UL));
            _monoMs += 5_000;
            Assert.That(_client.EstimatedServerNowMs, Is.EqualTo(1_795_430_405_000UL));

            // 切和平免战与挂牌迎战
            _client.SetPeaceMode(true);
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.SetGuildWarStanceMessageId));
            _net.Reply(new SetGuildWarStanceResponse
            {
                ServerTimeMs = 1_795_430_406_000UL,
                MyStance = new GuildWarStanceView { PeaceMode = true, OpenChallenge = false, MyStreak = 3, MyWeeklyMerit = 145 },
            });
            Assert.That(_client.Overview.MyStance.PeaceMode, Is.True);

            // 登记守旗阵
            _client.RegisterMyGarrison();
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.RegisterGuildWarGarrisonMessageId));
            var garrisonResp = new RegisterGuildWarGarrisonResponse();
            garrisonResp.GarrisonSlots.Add(new GuildWarGarrisonSlotView { SlotIndex = 1, PlayerId = 2001UL, PlayerName = "云中客", Level = 60, PetName = "九尾灵狐" });
            _net.Reply(garrisonResp);
            Assert.That(_client.Overview.GarrisonSlots.Count, Is.EqualTo(1));

            // 领取周战功箱与灵脉分红
            _client.ClaimMilestone(2);
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.ClaimGuildWarMilestoneMessageId));
            _net.Reply(new ClaimGuildWarMilestoneResponse
            {
                Milestone = new GuildWarMilestoneView { MilestoneId = 2, RequiredMerit = 1200, GuildMerit = 1420, Claimed = true, Claimable = false },
                AssetOrderStatus = GuildAssetOrderStatus.Applied,
            });
            Assert.That(_client.Overview.Milestones.First(x => x.MilestoneId == 2).Claimed, Is.True);

            _client.ClaimDividend();
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.ClaimGuildWarDividendMessageId));
            _net.Reply(new ClaimGuildWarDividendResponse
            {
                ClaimedCount = 3,
                Dividend = new GuildWarDividendView { ClaimableCount = 0, ClaimedThisWeek = 5, WeeklyCap = 12, GuildProducedThisWeek = 9, GuildWeeklyCap = 24 },
                AssetOrderStatus = GuildAssetOrderStatus.Applied,
            });
            Assert.That(_client.Overview.Dividend.ClaimableCount, Is.EqualTo(0u));

            // 并发抢点返回 kGuildWarTargetInBattle 触发观战事件
            ulong conflictBattle = 0;
            _client.TargetAlreadyInBattle += id => conflictBattle = id;
            _client.StartLivePvpBattle(20011UL, 1u);
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.StartGuildWarBattleMessageId));
            _net.Reply(new StartGuildWarBattleResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildWarTargetInBattle },
                OffenderPlayerId = 20011UL,
                OffenderBattleId = 900777UL,
            });
            Assert.That(conflictBattle, Is.EqualTo(900777UL));
            Assert.That(_client.LastConflictBattleId, Is.EqualTo(900777UL));

            // 发布与追踪追杀令
            _client.PostBounty(20011UL, GuildWarBountyTier.NormalSilver, true);
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.PostGuildWarBountyMessageId));
            _net.Reply(new PostGuildWarBountyResponse
            {
                Bounty = new GuildWarBountyView
                {
                    BountyId = 70002UL,
                    TargetPlayerId = 20011UL,
                    TargetName = "剑尘",
                    TargetGuildName = "青云阁",
                    IssuerGuildId = 10002UL,
                    IssuerGuildName = "沧澜盟",
                    Tier = GuildWarBountyTier.NormalSilver,
                    RewardSilver = 4500,
                    PublicBoard = true,
                    Status = GuildWarBountyStatus.Active,
                    CounterKillsToTear = 2,
                },
            });
            Assert.That(_client.Overview.Bounties.Any(x => x.BountyId == 70002UL), Is.True);

            _client.TrackBounty(70002UL);
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.TrackGuildWarBountyMessageId));
            _net.Reply(new TrackGuildWarBountyResponse
            {
                TargetOnline = true,
                InSafeZone = false,
                SceneConfigId = 5,
                ChannelNo = 1,
                NextTrackAtMs = 1_795_430_425_000UL,
            });
            var tracked = _client.Overview.Bounties.First(x => x.BountyId == 70002UL);
            Assert.That(tracked.LastSceneConfigId, Is.EqualTo(5u));
            Assert.That(tracked.LastChannelNo, Is.EqualTo(1u));
        }

        [Test]
        public void SettlementPollingBackoffAlertPushFieldInspectAndFeatureUnavailableDegradeCleanly()
        {
            GuildWarBattleSettlementSnapshot latest = null;
            _client.SettlementOpened += s => latest = s;
            _client.QueryBattleResult(900188UL);
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.GetGuildWarBattleResultMessageId));
            _net.Reply(new GetGuildWarBattleResultResponse { BattleId = 900188UL, Settled = false, WarBattleKind = GuildWarBattleKind.LeylinePvp, Outcome = 1, Rounds = 8 });
            Assert.That(_client.SettlementPolling, Is.True);
            Assert.That(latest, Is.Not.Null);
            Assert.That(latest.Settled, Is.False);

            // 未到 2s 退避窗口不重复发包
            _monoMs += 1_000;
            _client.Tick();
            Assert.That(_net.CallCount, Is.EqualTo(1));

            // 满 2s 退避后自动重查并收口
            _monoMs += 1_500;
            _client.Tick();
            Assert.That(_net.CallCount, Is.EqualTo(2));
            _net.Reply(new GetGuildWarBattleResultResponse
            {
                BattleId = 900188UL,
                Settled = true,
                WarBattleKind = GuildWarBattleKind.LeylinePvp,
                Outcome = 1,
                Rounds = 8,
                MaterialUnitsAdded = 15,
                MaterialTradableAdded = 1,
                ContributionGain = 80,
                SilverGain = 3000,
                BountyOutcomeTag = 1,
                BountyJackpotContribution = 200,
                BountyJackpotSilver = 6000,
                DiminishPercent = 100,
                LeylineId = 1,
                BannerHpBefore = 820,
                BannerHpAfter = 720,
            });
            Assert.That(_client.SettlementPolling, Is.False);
            Assert.That(latest.Settled, Is.True);
            Assert.That(latest.MaterialUnitsAdded, Is.EqualTo(15u));

            // 预警推送
            GuildWarAlertSnapshot receivedAlert = null;
            _client.AlertReceived += a => receivedAlert = a;
            _net.Push(GuildServiceRpc.NotifyGuildWarAlertMessageId, new GuildWarAlertS2C
            {
                Kind = GuildWarAlertKind.LeylineUnderSiege,
                LeylineId = 2,
                AttackerGuildName = "青云阁",
                BannerHpPercent = 62,
            });
            Assert.That(receivedAlert, Is.Not.Null);
            Assert.That(receivedAlert.Kind, Is.EqualTo(GuildWarAlertKind.LeylineUnderSiege));

            // 野外巡检
            _client.InspectFieldWarTargets(new[] { 3001UL, 3002UL });
            Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.InspectFieldWarTargetsMessageId));
            var fieldResp = new InspectFieldWarTargetsResponse();
            fieldResp.Targets.Add(new FieldWarTargetBrief { PlayerId = 3001UL, PlayerName = "敌帮剑客", GuildId = 10001UL, GuildName = "青云阁", CanAttackNow = true, ActiveBountyId = 70001UL, ActiveBountyRewardSilver = 4500 });
            _net.Reply(fieldResp);
            Assert.That(_client.FieldTargets.Count, Is.EqualTo(1));

            // 服务端未开启时优雅降级
            _client.RefreshOverview();
            _net.Reply(new GetGuildWarOverviewResponse { ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KFeatureUnavailable } });
            Assert.That(_client.FeatureUnavailable, Is.True);
            Assert.That(_client.LastError, Does.Contain("尚未开启"));
        }

        [Test]
        public void GuildWarWindowRendersAllPagesPostBountyModalConflictSpectateAndAlertBanner()
        {
            var host = new GameObject("GuildWarWindowTestHost", typeof(RectTransform), typeof(Canvas));
            try
            {
                var window = new GuildWarWindow(host.transform);
                window.SetContext(_client, 10002UL, true);
                window.Show(GuildWarPage.Main);
                _net.Reply(OverviewFixture());

                Assert.That(window.IsVisible, Is.True);
                Assert.That(FindButton(window.Root, "QuickBattleBtn"), Is.Not.Null);
                Assert.That(FindButton(window.Root, "AttackLeyline_1"), Is.Not.Null);
                Assert.That(FindButton(window.Root, "MilestoneChest_2").interactable, Is.True);

                // 切守旗阵页
                window.ShowPage(GuildWarPage.Garrison);
                Assert.That(FindButton(window.Root, "RegisterMyGarrisonBtn"), Is.Not.Null);

                // 切追杀榜 -> 仇敌录 -> 打开固定 3 档发单弹框并提交
                window.ShowPage(GuildWarPage.Bounty);
                FindButton(window.Root, "BountySubTab_2").onClick.Invoke();
                FindButton(window.Root, "PostBountyForEnemy_0").onClick.Invoke();
                Assert.That(window.ModalVisible, Is.True);
                Assert.That(FindButton(window.Root, "BountyTier_NormalSilver"), Is.Not.Null);
                Assert.That(FindButton(window.Root, "BountyTier_HeavySilver"), Is.Not.Null);
                Assert.That(FindButton(window.Root, "BountyTier_GuildPublic").interactable, Is.True);
                FindButton(window.Root, "ConfirmPostBounty").onClick.Invoke();
                Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.PostGuildWarBountyMessageId));
                _net.Reply(new PostGuildWarBountyResponse());

                // 并发抢点弹窗触发实时观战
                ulong watched = 0;
                window.WatchBattleRequested += id => watched = id;
                window.ShowConflictSpectateModal(900888UL);
                Assert.That(window.ModalVisible, Is.True);
                FindButton(window.Root, "ConflictWatchNow").onClick.Invoke();
                Assert.That(watched, Is.EqualTo(900888UL));
                Assert.That(window.ModalVisible, Is.False);

                // 大事记 / 结算 / 野外现场页渲染无异常
                window.ShowPage(GuildWarPage.Logs);
                window.ShowPage(GuildWarPage.FieldHud);
                _client.QueryBattleResult(900188UL);
                _net.Reply(new GetGuildWarBattleResultResponse
                {
                    BattleId = 900188UL, Settled = true, Outcome = 1, Rounds = 6,
                    WarBattleKind = GuildWarBattleKind.PanelPvp, MaterialUnitsAdded = 15,
                    ContributionGain = 80, SilverGain = 3000, DiminishPercent = 100,
                });
                Assert.That(window.Page, Is.EqualTo(GuildWarPage.Settlement));
                Assert.That(FindButton(window.Root, "SettlementContinueWar"), Is.Not.Null);

                // 非阻断预警横幅展示与忽略
                _net.Push(GuildServiceRpc.NotifyGuildWarAlertMessageId, new GuildWarAlertS2C
                {
                    Kind = GuildWarAlertKind.PursuerEnteredMap,
                    AttackerGuildName = "青云阁",
                    SceneConfigId = 5,
                    ChannelNo = 1,
                });
                Assert.That(window.AlertRoot.gameObject.activeSelf, Is.True);
                FindButton(window.AlertRoot, "AlertDismiss").onClick.Invoke();
                Assert.That(window.AlertRoot.gameObject.activeSelf, Is.False);

                window.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void FieldHudDisplaysWildEnemiesAndTravelEventsForwardSceneIds()
        {
            _client.RefreshOverview();
            _net.Reply(OverviewFixture());
            _client.InspectFieldWarTargets(new[] { 20011UL });
            var inspectResp = new InspectFieldWarTargetsResponse();
            inspectResp.Targets.Add(new FieldWarTargetBrief
            {
                PlayerId = 20011UL,
                PlayerName = "剑尘",
                GuildId = 10001UL,
                GuildName = "青云阁",
                CanAttackNow = true,
                ActiveBountyId = 70002UL,
                ActiveBountyRewardSilver = 4500UL,
                IsRivalGuild = true,
            });
            _net.Reply(inspectResp);

            var host = new GameObject("FieldHudTestHost", typeof(RectTransform));
            try
            {
                var hud = new GuildWarFieldHud(host.transform);
                hud.SetContext(_client, 10002UL, 5u);
                Assert.That(hud.Root.gameObject.activeSelf, Is.True);
                Assert.That(FindButton(hud.Root, "NearbyAttack_0"), Is.Not.Null);

                hud.SelectFieldTarget(20011UL);
                Assert.That(hud.SelectedTargetPlayerId, Is.EqualTo(20011UL));
                FindButton(hud.Root, "PickedAttackBtn").onClick.Invoke();
                Assert.That(_net.LastMessageId, Is.EqualTo(GuildServiceRpc.StartGuildWarBattleMessageId));
                _net.Reply(new StartGuildWarBattleResponse { BattleId = 901234UL, WarBattleKind = GuildWarBattleKind.BountyHunt });

                uint traveledSceneConf = 0;
                ulong traveledSceneId = 0;
                var win = new GuildWarWindow(host.transform);
                win.TravelToSceneRequested += (conf, sid) => { traveledSceneConf = conf; traveledSceneId = sid; };
                win.SetContext(_client, 10002UL, true);
                win.Show(GuildWarPage.Main);
                FindButton(win.Root, "TravelLeyline_1").onClick.Invoke();
                Assert.That(traveledSceneConf, Is.EqualTo(2u));
                Assert.That(traveledSceneId, Is.EqualTo(0UL));

                hud.Dispose();
                win.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void AssertHorizontalBorder(string key, float expectedX)
        {
            var sprite = GuildWarUiArt.Load(key);
            Assert.That(sprite.border.x, Is.EqualTo(expectedX), key + " left border");
            Assert.That(sprite.border.z, Is.EqualTo(expectedX), key + " right border");
            Assert.That(sprite.border.y, Is.EqualTo(0f), key + " bottom border");
            Assert.That(sprite.border.w, Is.EqualTo(0f), key + " top border");
        }

        private static Button FindButton(UnityEngine.Transform root, string name) =>
            root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        private static GetGuildWarOverviewResponse OverviewFixture()
        {
            var resp = new GetGuildWarOverviewResponse
            {
                ServerTimeMs = 1_795_430_400_000UL,
                WeekKey = 202648,
                NextWeeklyResetMs = 1_795_986_000_000UL,
                GoldLeylineId = 1,
                ContributionBalance = 1280,
                PublicBountyUsedToday = 0,
                PublicBountyDailyLimit = 2,
                MyStance = new GuildWarStanceView { PeaceMode = false, OpenChallenge = true, MyStreak = 3, MyWeeklyMerit = 145, MyBountyPayoutsToday = 1, MyBountyPayoutLimit = 5 },
                BannerHonor = new GuildWarBannerHonorView { OccupiedLeylineId = 2 },
                Dividend = new GuildWarDividendView { ClaimableCount = 3, ClaimedThisWeek = 2, WeeklyCap = 12, GuildProducedThisWeek = 9, GuildWeeklyCap = 24, MinPersonalMerit = 30 },
            };
            resp.Leylines.Add(new GuildWarLeylineView { LeylineId = 1, Name = "八景岛 · 灵脉", SceneConfigId = 2, OwnerGuildId = 10001, OwnerGuildName = "青云阁", BannerHp = 820, BannerMaxHp = 1000, ActiveBattleCount = 2, FeaturedWatchBattleId = 900101, IsGoldLeyline = true, GarrisonCount = 4 });
            resp.Leylines.Add(new GuildWarLeylineView { LeylineId = 2, Name = "碧渊水宫 · 灵脉", SceneConfigId = 5, OwnerGuildId = 10002, OwnerGuildName = "沧澜盟", BannerHp = 380, BannerMaxHp = 1000, UnderSiege = true, ActiveBattleCount = 1, FeaturedWatchBattleId = 900102, GarrisonCount = 5 });
            resp.Leylines.Add(new GuildWarLeylineView { LeylineId = 3, Name = "月汐湾 · 灵脉", SceneConfigId = 3, OwnerGuildId = 10003, OwnerGuildName = "归墟堂", BannerHp = 600, BannerMaxHp = 1000, ShieldUntilMs = 1_795_431_150_000UL, GarrisonCount = 3 });
            resp.Milestones.Add(new GuildWarMilestoneView { MilestoneId = 1, RequiredMerit = 300, GuildMerit = 1420, Claimed = true, Claimable = false });
            resp.Milestones.Add(new GuildWarMilestoneView { MilestoneId = 2, RequiredMerit = 1200, GuildMerit = 1420, Claimed = false, Claimable = true });
            resp.Milestones.Add(new GuildWarMilestoneView { MilestoneId = 3, RequiredMerit = 3000, GuildMerit = 1420, Claimed = false, Claimable = false });
            resp.Milestones.Add(new GuildWarMilestoneView { MilestoneId = 4, RequiredMerit = 6000, GuildMerit = 1420, Claimed = false, Claimable = false });
            resp.StreakBounties.Add(new GuildWarStreakBountyView { PlayerId = 20011, PlayerName = "剑尘", GuildId = 10001, GuildName = "青云阁", Streak = 5, BonusContribution = 200, BonusSilver = 6000 });
            resp.LiveTargets.Add(new GuildWarLiveTargetView { PlayerId = 20011, PlayerName = "剑尘", GuildId = 10001, GuildName = "青云阁", Level = 60, PetName = "青鸾", PartySize = 1, Streak = 5, LeylineId = 1 });
            resp.Enemies.Add(new GuildWarEnemyView { PlayerId = 20011, PlayerName = "剑尘", GuildId = 10001, GuildName = "青云阁", VictimName = "松风道童", BattleKind = GuildWarBattleKind.LeylinePvp, CanPostBounty = true });
            resp.RecentLogs.Add(new GuildWarRecentLogView { BattleId = 900100, WarBattleKind = GuildWarBattleKind.LeylinePvp, AttackerGuildName = "沧澜盟", DefenderGuildName = "青云阁", AttackerSummary = "云中客", DefenderSummary = "剑尘", Rounds = 7, BannerHpDelta = -100, SelfParticipated = true });
            return resp;
        }

        private sealed class WarFakeTransport : IBattleTransport
        {
            private readonly Dictionary<uint, Action<MessageContent>> _notifies = new Dictionary<uint, Action<MessageContent>>();
            private Action<IMessage> _pendingReply;

            public ulong PlayerId { get; set; }
            public bool IsReady { get; set; }
            public uint LastMessageId { get; private set; }
            public int CallCount { get; private set; }
            public event Action Disconnected;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) => _notifies[messageId] = handler;

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                LastMessageId = messageId;
                CallCount++;
                _pendingReply = msg => onResponse((TResp)msg);
            }

            public void SendOneWay(uint messageId, IMessage request) { LastMessageId = messageId; }

            public void Reply(IMessage msg)
            {
                var cb = _pendingReply;
                _pendingReply = null;
                Assert.That(cb, Is.Not.Null, "No pending RPC callback.");
                cb(msg);
            }

            public void Push(uint messageId, IMessage msg)
            {
                Assert.That(_notifies.TryGetValue(messageId, out var handler), Is.True, "Notify handler not registered: " + messageId);
                handler(new MessageContent { MessageId = messageId, SerializedMessage = msg.ToByteString() });
            }

            public void TriggerDisconnect()
            {
                IsReady = false;
                Disconnected?.Invoke();
            }
        }
    }
}
