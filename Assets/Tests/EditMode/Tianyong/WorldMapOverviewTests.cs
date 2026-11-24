using MmorpgClient.Game.MysteryMerchant;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.CurrentMaps;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class WorldMapOverviewTests
    {
        [Test]
        public void FromProtoAndFormatting_MapsHeatTideHotspotAndMerchant()
        {
            var proto = new global::WorldMapOverview
            {
                ZoneId = 1,
                UpdatedAtMs = 3_900_000,
                ServerTimeMs = 3_900_000,
                DailyHotspotSceneConfigId = 5,
                Tide = new global::WorldTideSnapshot
                {
                    Active = true,
                    RoundId = 3600,
                    SceneConfigId = 5,
                    RecommendedSceneId = 50001,
                    RecommendedChannelNo = 1,
                    StartsAtMs = 3_600_000,
                    EndsAtMs = 6_000_000,
                    NextStartsAtMs = 7_200_000,
                },
            };
            proto.Maps.Add(new global::WorldMapOverviewEntry
            {
                SceneConfigId = 1,
                TotalPlayerCount = 12,
                ChannelCount = 1,
                HeatLevel = global::WorldMapHeatLevel.Crowded,
                RecommendedSceneId = 10001,
                RecommendedChannelNo = 1,
            });
            proto.Maps.Add(new global::WorldMapOverviewEntry
            {
                SceneConfigId = 5,
                TotalPlayerCount = 3,
                ChannelCount = 1,
                HeatLevel = global::WorldMapHeatLevel.Active,
                RecommendedSceneId = 50001,
                RecommendedChannelNo = 1,
                IsTideMap = true,
                HasMysteryMerchant = true,
            });

            var snapshot = WorldMapOverviewModels.FromProto(proto);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.ZoneId, Is.EqualTo(1u));
            Assert.That(snapshot.DailyHotspotSceneConfigId, Is.EqualTo(5u));
            Assert.That(WorldMapOverviewModels.FormatDestinationBadges(snapshot, 1), Is.EqualTo("热闹"));
            Assert.That(WorldMapOverviewModels.FormatDestinationBadges(snapshot, 5), Is.EqualTo("有人 · 灵潮中 · 今日旺地 · 神秘商人"));
            Assert.That(WorldMapOverviewModels.FormatDestinationHint(snapshot, 5), Does.Contain("推荐落脚 1线"));
            Assert.That(WorldMapOverviewModels.FormatDestinationHint(snapshot, 5), Does.Contain("灵潮余约 35 分"));
            Assert.That(WorldMapOverviewModels.FormatDestinationHint(snapshot, 5), Does.Contain("神秘商人"));
        }

        [Test]
        public void SceneChannelClient_RequestOverviewSendsFlagAndCachesOverviewUntilDisconnect()
        {
            float now = 0f;
            ulong sceneId = 100;
            uint sceneConfId = 1;
            var net = new SceneChannelFakeTransport();
            using var client = new SceneChannelClient(net, () => net.Identity, () => sceneId, () => sceneConfId, () => now);

            Assert.That(client.RequestOverview(), Is.True);
            Assert.That(net.OneWays.Count, Is.EqualTo(1));
            Assert.That(net.OneWays[0].Id, Is.EqualTo(MessageIds.SceneInfoC2S));
            var req = (SceneInfoRequest)net.OneWays[0].Request;
            Assert.That(req.WithChannelDirectory, Is.True);
            Assert.That(req.WithWorldMapOverview, Is.True);

            var push = new SceneInfoS2C
            {
                WorldMapOverview = new global::WorldMapOverview
                {
                    ZoneId = 1,
                    DailyHotspotSceneConfigId = 2,
                },
            };
            push.SceneInfo.Add(new SceneInfoComp { SceneId = sceneId, SceneConfigId = sceneConfId });
            push.WorldMapOverview.Maps.Add(new global::WorldMapOverviewEntry
            {
                SceneConfigId = 2,
                TotalPlayerCount = 0,
                ChannelCount = 1,
                HeatLevel = global::WorldMapHeatLevel.Quiet,
                RecommendedSceneId = 200,
                RecommendedChannelNo = 1,
            });
            net.Push(MessageIds.NotifySceneInfo, push);

            Assert.That(client.HasOverview, Is.True);
            Assert.That(client.OverviewReceivedAt, Is.EqualTo(0f));
            Assert.That(client.Overview.FindMap(2)?.HeatLevel, Is.EqualTo(global::WorldMapHeatLevel.Quiet));

            // 后续普通仅目录请求不携带 WithWorldMapOverview，且不抹除已有总览缓存。
            now += SceneChannelClient.MinListIntervalSeconds + 0.1f;
            Assert.That(client.OverviewAgeSeconds, Is.EqualTo(now).Within(0.001f));
            Assert.That(client.RequestList(), Is.True);
            var listReq = (SceneInfoRequest)net.OneWays[1].Request;
            Assert.That(listReq.WithWorldMapOverview, Is.False);
            net.Push(MessageIds.NotifySceneInfo, new SceneInfoS2C());
            Assert.That(client.HasOverview, Is.True);

            // 断线复位清掉旧会话总览。
            net.Disconnect();
            Assert.That(client.HasOverview, Is.False);
            Assert.That(client.OverviewReceivedAt, Is.EqualTo(0f));
            Assert.That(client.OverviewAgeSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void TideBannerPinViewAndHudBadge_CoverActiveRestingDismissalAndCountdown()
        {
            var snapshot = new WorldMapOverviewSnapshot(
                1,
                3_900_000UL,
                3_900_000UL,
                new[]
                {
                    new WorldMapOverviewItem(1, 12, 1, global::WorldMapHeatLevel.Crowded, 10001, 1, false, false),
                    new WorldMapOverviewItem(5, 3, 1, global::WorldMapHeatLevel.Active, 50001, 1, true, true),
                },
                new WorldTideSnapshotModel(true, 3600UL, 5u, 50001UL, 1u, 3_600_000UL, 6_000_000UL, 7_200_000UL),
                5u);

            ulong nowMs = WorldMapOverviewModels.EstimateServerNowMs(snapshot, 10.5f);
            Assert.That(nowMs, Is.EqualTo(3_910_500UL));

            var banner = WorldMapOverviewModels.BuildTideBanner(snapshot, 1u, id => id == 5u ? "碧渊水宫" : "", nowMs, true);
            Assert.That(banner.Visible, Is.True);
            Assert.That(banner.Active, Is.True);
            Assert.That(banner.Headline, Is.EqualTo("灵潮涌动 · 碧渊水宫"));
            Assert.That(banner.CountdownText, Is.EqualTo("剩余 34:50"));
            Assert.That(banner.CanQuickTravel, Is.True);
            Assert.That(banner.ActionLabel, Is.EqualTo("立即前往"));

            var onTargetBanner = WorldMapOverviewModels.BuildTideBanner(snapshot, 5u, id => "碧渊水宫", nowMs, true);
            Assert.That(onTargetBanner.IsOnTargetMap, Is.True);
            Assert.That(onTargetBanner.CanQuickTravel, Is.False);
            Assert.That(onTargetBanner.ActionLabel, Is.EqualTo("已在此地"));

            var resting = new WorldMapOverviewSnapshot(
                1, 6_100_000UL, 6_100_000UL, snapshot.Maps,
                new WorldTideSnapshotModel(false, 3600UL, 5u, 0UL, 0u, 3_600_000UL, 6_000_000UL, 7_200_000UL), 5u);
            var restingBanner = WorldMapOverviewModels.BuildTideBanner(resting, 1u, id => "碧渊水宫", 6_100_000UL, true);
            Assert.That(restingBanner.Visible, Is.True);
            Assert.That(restingBanner.Active, Is.False);
            Assert.That(restingBanner.Headline, Is.EqualTo("灵潮休歇"));
            Assert.That(restingBanner.CountdownText, Is.EqualTo("下轮倒计时 18:20"));
            Assert.That(restingBanner.CanQuickTravel, Is.False);

            var hiddenBanner = WorldMapOverviewModels.BuildTideBanner(null, 1u, null, 0UL, true);
            Assert.That(hiddenBanner.Visible, Is.False);

            var pin = WorldMapOverviewModels.BuildPinView(snapshot, 5u, 1u, true);
            Assert.That(pin.HasOverviewEntry, Is.True);
            Assert.That(pin.HeatLevel, Is.EqualTo(global::WorldMapHeatLevel.Active));
            Assert.That(pin.HeatLabel, Is.EqualTo("有人"));
            Assert.That(pin.IsTideMap, Is.True);
            Assert.That(pin.IsDailyHotspot, Is.True);
            Assert.That(pin.HasMysteryMerchant, Is.True);
            Assert.That(pin.IsCurrentScene, Is.False);
            Assert.That(pin.RecommendedSceneId, Is.EqualTo(50001UL));

            // HUD 入口角标：在非灵潮图显示，本轮看过后消失，身处灵潮图不显示，换轮重亮。
            Assert.That(WorldMapOverviewModels.ShouldShowMapEntryTideBadge(snapshot, 1u, 0UL), Is.True);
            Assert.That(WorldMapOverviewModels.ShouldShowMapEntryTideBadge(snapshot, 1u, 3600UL), Is.False);
            Assert.That(WorldMapOverviewModels.ShouldShowMapEntryTideBadge(snapshot, 5u, 0UL), Is.False);
            Assert.That(WorldMapOverviewModels.ShouldShowMapEntryTideBadge(resting, 1u, 0UL), Is.False);

            // 游历 / 览景目的地分栏过滤不丢失正式图也不把 clientOnly 混入联机列表。
            CurrentMapCatalog.ReloadDeliveries();
            try
            {
                var all = CityTravelUiRoot.CreateDestinations();
                var online = CityTravelUiRoot.FilterOnlineDestinations(all);
                var gallery = CityTravelUiRoot.FilterGalleryDestinations(all);
                Assert.That(online.Length, Is.GreaterThanOrEqualTo(3));
                foreach (var d in online) Assert.That(d.OnlineAvailable && d.Available, Is.True);
                foreach (var d in gallery) Assert.That(!d.OnlineAvailable && d.Available, Is.True);
            }
            finally
            {
                CurrentMapCatalog.ReloadDeliveries();
            }
        }

        [Test]
        public void CityTravelWindowAndMerchantWindow_RenderOverviewBadgesAndCrossMapTravelLabel()
        {
            CurrentMapCatalog.ReloadDeliveries();
            var root = new GameObject("WorldMapOverviewUiTest", typeof(RectTransform));
            try
            {
                var design = QdaoUguiFactory.CreateCenteredRect("Design", root.transform, 2560, 1080);
                var travelWindow = new CityTravelWindow(design);
                travelWindow.SetDestinations(CityTravelUiRoot.CreateDestinations());
                travelWindow.Show(1, false);
                travelWindow.SetOverview(new WorldMapOverviewSnapshot(
                    1, 1000, 1000,
                    new[]
                    {
                        new WorldMapOverviewItem(1, 10, 1, global::WorldMapHeatLevel.Crowded, 101, 1, false, true),
                    },
                    null,
                    1));
                var nameText = travelWindow.Root.GetComponentsInChildren<TMP_Text>(true);
                string selectedName = "";
                foreach (var t in nameText)
                {
                    if (t.name == "SelectedDestinationName") { selectedName = t.text; break; }
                }
                Assert.That(selectedName, Does.Contain("热闹"));
                Assert.That(selectedName, Does.Contain("今日旺地"));
                Assert.That(selectedName, Does.Contain("神秘商人"));

                var merchantWindow = new MysteryMerchantWindow(design);
                var crossMapView = new MysteryMerchantShopView
                {
                    Presence = MysteryMerchantPresence.Elsewhere,
                    ChannelNo = 2,
                    TargetMapName = "碧渊水宫",
                    RemainingMs = 754_000,
                    CanTravel = true,
                    Ready = true,
                };
                merchantWindow.SetView(crossMapView);
                Assert.That(MysteryMerchantWindow.DescribeHeadline(crossMapView), Is.EqualTo("正在 碧渊水宫 · 2线 · 12:34 后离开"));
                Assert.That(MysteryMerchantWindow.DescribeEmpty(crossMapView), Does.Contain("碧渊水宫 · 2线"));
                var sameMapView = crossMapView;
                sameMapView.TargetMapName = "";
                Assert.That(MysteryMerchantWindow.DescribeHeadline(sameMapView), Is.EqualTo("正在 2线 · 12:34 后离开"));
                Assert.That(MysteryMerchantWindow.DescribeEmpty(sameMapView), Is.EqualTo("神秘商人正在 2线，到那条线才能看到货架。"));
                string travelText = "";
                foreach (var t in merchantWindow.Root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.transform.parent != null && t.transform.parent.name == MysteryMerchantWindow.TravelButtonName)
                    {
                        travelText = t.text;
                        break;
                    }
                }
                Assert.That(travelText, Is.EqualTo("前往 碧渊水宫 · 2线"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                CurrentMapCatalog.ReloadDeliveries();
            }
        }
    }
}
