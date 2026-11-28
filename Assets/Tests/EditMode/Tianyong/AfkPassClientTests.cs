using System.Collections.Generic;
using MmorpgClient.Game.AfkPass;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// 挂机月卡网络层的状态机(服务端 docs/design/afk-monthly-card.md §7)。时间全部由注入的时钟推进(本地秒),
    /// 服务器时刻按应答里的 server_now 推算。传输层复用切线测试的 <see cref="SceneChannelFakeTransport"/>(同一个测试程序集)。
    /// </summary>
    public sealed class AfkPassClientTests
    {
        private const ulong Now = 1_800_000_000;
        private const ulong Day = 86400;
        // 只用于内存传输测试;不代表线上服务器的协议分配。
        private static readonly AfkPassMessageIds TestMessageIds = new AfkPassMessageIds(65011, 65012, 65013, 65014);

        private float _now;
        private SceneChannelFakeTransport _net;
        private AfkPassClient _client;
        private readonly List<string> _reminders = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _now = 0f;
            _reminders.Clear();
            _net = new SceneChannelFakeTransport();
            _client = new AfkPassClient(_net, () => _net.Identity, () => _now, TestMessageIds);
            _client.Reminder += text => _reminders.Add(text);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private SceneChannelFakeTransport.SentCall LastCall => _net.Calls[_net.Calls.Count - 1];

        private static global::AfkPassInfo Info(ulong expiresAt, ulong price = 300, uint remindHours = 72) =>
            new global::AfkPassInfo
            {
                ExpiresAt = expiresAt, ServerNow = Now, PriceCurrencyType = 1, Price = price, PeriodDays = 30,
                MaxRemainingDays = 180, RemindBeforeHours = remindHours, Balance = 900,
            };

        private void ReplyInfo(global::AfkPassInfo info) => LastCall.Reply(new global::GetAfkPassResponse { Info = info });

        private void EnterWith(global::AfkPassInfo info)
        {
            _client.HandleSceneEntered();
            ReplyInfo(info);
        }

        // ── 展示规则 ────────────────────────────────────────────────────────

        [Test]
        public void FormatRemaining_CoversEveryShape()
        {
            Assert.That(AfkPassModels.FormatRemaining(0, 0), Is.EqualTo(AfkPassModels.NotOpenedText));
            Assert.That(AfkPassModels.FormatRemaining(Now, 0), Is.EqualTo(AfkPassModels.ExpiredText));
            Assert.That(AfkPassModels.FormatRemaining(Now, 30 * Day), Is.EqualTo("剩余 30 天 0 小时"));
            Assert.That(AfkPassModels.FormatRemaining(Now, 2 * 3600 + 59 * 60 + 59), Is.EqualTo("剩余 2 小时 59 分"), "只取整、不进位");
            Assert.That(AfkPassModels.FormatRemaining(Now, 61), Is.EqualTo("剩余 1 分钟"));
            Assert.That(AfkPassModels.FormatRemaining(Now, 59), Is.EqualTo("剩余不到 1 分钟"));
            Assert.That(AfkPassModels.ReminderText(59), Is.EqualTo("挂机月卡剩余不到 1 分钟，到期后断线将不再离线托管。"));
        }

        [Test]
        public void ReminderWindow_IsInclusiveAndZeroMeansNever()
        {
            Assert.That(AfkPassModels.InReminderWindow(72 * 3600, 72), Is.True);
            Assert.That(AfkPassModels.InReminderWindow(72 * 3600 + 1, 72), Is.False);
            Assert.That(AfkPassModels.InReminderWindow(0, 72), Is.False, "已到期不算快到期");
            Assert.That(AfkPassModels.InReminderWindow(60, 0), Is.False, "0 = 不提醒");
        }

        // ── 查询与倒计时 ────────────────────────────────────────────────────

        [Test]
        public void SceneEntered_AsksOnce_AndRemainingCountsDownWithTheLocalClock()
        {
            _client.HandleSceneEntered();
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(LastCall.Id, Is.EqualTo(TestMessageIds.Get));
            Assert.That(LastCall.Request, Is.InstanceOf<global::GetAfkPassRequest>());

            ReplyInfo(Info(Now + 10 * Day));
            Assert.That(_client.RemainingSeconds, Is.EqualTo(10 * Day));
            _now = 100f;
            Assert.That(_client.RemainingSeconds, Is.EqualTo(10 * Day - 100), "按服务器时间 + 本地走过的秒数推算,不信本机墙钟");
            Assert.That(_client.RemainingText, Is.EqualTo("剩余 9 天 23 小时"));
            Assert.That(_client.Active, Is.True);

            _client.HandleSceneEntered();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "已有结论:换场景、切线不再问");
        }

        [Test]
        public void UnsupportedServer_SendsNothing()
        {
            var client = new AfkPassClient(_net, () => _net.Identity, () => _now, new AfkPassMessageIds(0, 0));
            client.HandleSceneEntered();
            client.RequestInfo();
            Assert.That(client.Buy(), Is.False);
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(client.Status, Is.EqualTo(AfkPassClient.UnsupportedMessage));
            client.Dispose();
        }

        // ── 提醒 ────────────────────────────────────────────────────────────

        [Test]
        public void Reminder_FiresOnceInTheWindow_AndOnceMoreWhenItExpiresWhileOnline()
        {
            EnterWith(Info(Now + 3 * Day));
            Assert.That(_reminders.Count, Is.EqualTo(1), "剩余恰好 72 小时:进入提醒窗口");
            Assert.That(_reminders[0], Does.Contain("剩余 3 天"));
            _client.Tick();
            Assert.That(_reminders.Count, Is.EqualTo(1), "一次连接只提醒一次");

            _now = 3 * Day;
            _client.Tick();
            Assert.That(_reminders.Count, Is.EqualTo(2));
            Assert.That(_reminders[1], Is.EqualTo(AfkPassModels.ExpiredReminderText));
            _client.Tick();
            Assert.That(_reminders.Count, Is.EqualTo(2));
        }

        [Test]
        public void Reminder_StaysQuietOutsideTheWindowAndForAPassThatExpiredBeforeLogin()
        {
            EnterWith(Info(Now + 10 * Day));
            _client.Tick();
            Assert.That(_reminders, Is.Empty);

            _net.Disconnect();
            _net.Reconnect();
            _now = AfkPassClient.MinInfoIntervalSeconds; // 查询的最小间隔跨连接保留:等它过去,重连后的查询才会真的发出
            _client.Tick();
            EnterWith(Info(Now - 10));
            Assert.That(_client.Info, Is.Not.Null, "重连之后的那次查询确实拿到了结论");
            _client.Tick();
            Assert.That(_reminders, Is.Empty, "登录时就已到期:不是「刚到期」,不弹");
        }

        // ── 开通 ────────────────────────────────────────────────────────────

        [Test]
        public void Buy_SendsTheShownQuoteWithAKey_AndAppliesTheAuthoritativeResult()
        {
            EnterWith(Info(0));
            Assert.That(_client.Buy(), Is.True);
            Assert.That(_client.BuyPending, Is.True);
            Assert.That(_client.Status, Is.EqualTo(AfkPassClient.BuyPendingMessage));
            var call = LastCall;
            Assert.That(call.Id, Is.EqualTo(TestMessageIds.Buy));
            var request = (global::BuyAfkPassRequest)call.Request;
            Assert.That(request.ClientRequestId, Is.Not.Zero);
            Assert.That(request.ExpectedCurrencyType, Is.EqualTo(1));
            Assert.That(request.ExpectedPrice, Is.EqualTo(300));
            int sent = _net.Calls.Count;
            Assert.That(_client.Buy(), Is.False, "在途:不发第二个");
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));

            call.Reply(new global::BuyAfkPassResponse { Info = Info(Now + 30 * Day) });
            Assert.That(_client.BuyPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(AfkPassClient.BoughtMessage));
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_client.RemainingSeconds, Is.EqualTo(30 * Day));
        }

        [Test]
        public void Buy_UnknownOutcome_ResendsOnceWithTheSameKey_ThenWaitsForReconnect()
        {
            EnterWith(Info(0));
            _client.Buy();
            var first = LastCall;
            first.Fail(OutstandingCalls.TimeoutDetail);
            var resend = LastCall;
            Assert.That(resend, Is.Not.SameAs(first));
            Assert.That(resend.Request, Is.SameAs(first.Request), "重发逐字节相同:同一个键、同一份报价");
            Assert.That(_client.BuyPending, Is.True);

            resend.Fail(OutstandingCalls.TimeoutDetail);
            Assert.That(_client.RequiresReconnect, Is.True, "结果未知:可能已经扣了钱,核实前不再发");
            Assert.That(_client.Status, Is.EqualTo(AfkPassClient.RecoveryMessage));
            _now = 5f;
            int sent = _net.Calls.Count;
            Assert.That(_client.Buy(), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));

            _client.RequestInfo();
            ReplyInfo(Info(Now + 30 * Day));
            Assert.That(_client.Status, Is.EqualTo(AfkPassClient.RecoveryMessage), "查询结果不能把「结果未知」盖掉");

            _net.Disconnect();
            _net.Reconnect();
            _client.Tick();
            Assert.That(_client.RequiresReconnect, Is.False, "真实断线重连之后解除");
            Assert.That(_client.Info, Is.Null, "重连进场后重新问");
        }

        [Test]
        public void Buy_RejectedByTheGateOnTheFirstAttempt_IsADefiniteFailure()
        {
            EnterWith(Info(0));
            _client.Buy();
            LastCall.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            Assert.That(_client.RequiresReconnect, Is.False, "gate 带码拒绝:请求没到 scene,结果是确定的");
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_client.BuyPending, Is.False);
        }

        [Test]
        public void Buy_PriceChanged_ShowsTheReason_AndAsksForTheNewQuote()
        {
            EnterWith(Info(0));
            _client.Buy();
            LastCall.Reply(new global::BuyAfkPassResponse
            {
                ErrorMessage = new global::TipInfoMessage { Id = (uint)afk_pass_error.KAfkPassPriceChanged },
            });
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_client.Status, Does.Contain("价格"));

            int sent = _net.Calls.Count;
            _now = AfkPassClient.MinInfoIntervalSeconds;
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 1), "最小间隔一过就补问一次");
            Assert.That(LastCall.Id, Is.EqualTo(TestMessageIds.Get));
        }

        [Test]
        public void Buy_WithoutAQuoteOrWhenNotForSale_IsRejectedLocally()
        {
            Assert.That(_client.Buy(), Is.False, "还没有报价");
            Assert.That(_client.Status, Is.EqualTo(AfkPassClient.InfoUnavailableMessage));
            Assert.That(LastCall.Id, Is.EqualTo(TestMessageIds.Get), "顺手问一次");

            ReplyInfo(Info(0, price: 0));
            Assert.That(_client.ForSale, Is.False);
            int sent = _net.Calls.Count;
            Assert.That(_client.Buy(), Is.False);
            Assert.That(_client.Status, Is.EqualTo(AfkPassModels.NotForSaleText));
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
        }

        [Test]
        public void SilentConnectionSwap_ResetsStateAndNotifiesChanged()
        {
            object identity = new object();
            using var client = new AfkPassClient(_net, () => identity, () => _now, TestMessageIds);
            int changedCount = 0;
            client.Changed += () => changedCount++;

            client.HandleSceneEntered();
            ReplyInfo(Info(Now + 10 * Day));
            Assert.That(client.Info, Is.Not.Null);

            // 模拟底层连接被静默替换(未发 Disconnected 事件):下一帧 Tick 复位状态并通知 UI 重绘。
            int beforeSwap = changedCount;
            identity = new object();
            client.Tick();
            Assert.That(client.Info, Is.Null);
            Assert.That(client.RequiresReconnect, Is.False);
            Assert.That(changedCount, Is.EqualTo(beforeSwap + 1), "连接静默切换后必须通知界面清空旧状态");
        }

        [Test]
        public void DescribeTip_UsesConfiguredCurrencyNameForNonDiamond()
        {
            uint insufficient = (uint)afk_pass_error.KAfkPassCurrencyNotEnough;
            Assert.That(AfkPassModels.DescribeTip(insufficient), Does.Contain("灵石").And.Contain("万象阁"));
            Assert.That(AfkPassModels.DescribeTip(insufficient, 0), Does.Contain("银两").And.Not.Contain("灵石"));
            Assert.That(AfkPassModels.DescribeTip(insufficient, 2), Does.Contain("绑定灵石").And.Not.Contain("万象阁"));
        }

        [Test]
        public void SetAutoReturnCity_AndHostedRecapAckFlowWork()
        {
            var info = Info(Now + 10 * Day);
            info.AutoReturnCityOnTeamEnd = false;
            info.HostedRecap = new global::HostedRecapSummary
            {
                StartedAt = Now - 1800,
                EndedAt = Now,
                BattleCount = 4,
                WinCount = 3,
                ExpGained = 5200,
                SilverGained = 9000,
                ItemCount = 6,
                PendingAck = true,
            };

            _client.HandleSceneEntered();
            ReplyInfo(info);
            Assert.That(_client.AutoReturnCityOnTeamEnd, Is.False);
            Assert.That(_client.PendingRecap, Is.Not.Null);
            Assert.That(_client.HasPromptableRecap, Is.True);

            // 开启离队自动回主城
            Assert.That(_client.SetAutoReturnCity(true), Is.True);
            Assert.That(LastCall.Id, Is.EqualTo(TestMessageIds.SetSettings));
            var updated = info.Clone();
            updated.AutoReturnCityOnTeamEnd = true;
            LastCall.Reply(new global::SetAfkPassSettingsResponse { Info = updated });
            Assert.That(_client.AutoReturnCityOnTeamEnd, Is.True);

            // 确认收获简报
            Assert.That(_client.AckHostedRecap(info.HostedRecap.StartedAt), Is.True);
            Assert.That(LastCall.Id, Is.EqualTo(TestMessageIds.AckRecap));
            var cleared = updated.Clone();
            cleared.HostedRecap = null;
            LastCall.Reply(new global::AckHostedRecapResponse { Info = cleared });
            Assert.That(_client.PendingRecap, Is.Null);
            Assert.That(_client.HasPromptableRecap, Is.False);
        }

        [Test]
        public void ShortZeroBattleRecap_IsAutoAckedSilentlyWithoutPrompt()
        {
            var info = Info(Now + 10 * Day);
            info.HostedRecap = new global::HostedRecapSummary
            {
                StartedAt = Now - 15,
                EndedAt = Now,
                BattleCount = 0,
                PendingAck = true,
            };
            _client.HandleSceneEntered();
            ReplyInfo(info);

            Assert.That(_client.HasPromptableRecap, Is.False);
            Assert.That(LastCall.Id, Is.EqualTo(TestMessageIds.AckRecap), "不足 60 秒且 0 场战斗的瞬时断线应静默确认");
        }

        [Test]
        public void LightCardBenefitsSummary_ReflectsSlotsAndStage2TradableAndBuyFiresPurchased()
        {
            var inactive = Info(0);
            inactive.UnlockedLoadoutSlots = 2;
            inactive.Stage2TradableEnabled = false;
            string inactiveText = AfkPassModels.LightCardBenefitsSummary(inactive);
            Assert.That(inactiveText, Does.Contain("+2 套").And.Contain("上限 7 套").And.Contain("零战力"));

            var active = Info(Now + 30 * Day);
            active.UnlockedLoadoutSlots = 4;
            active.Stage2TradableEnabled = true;
            string activeText = AfkPassModels.LightCardBenefitsSummary(active);
            Assert.That(activeText, Does.Contain("4 套").And.Contain("上限 7 套").And.Contain("已激活").And.Contain("零战力"));

            int purchasedCount = 0;
            _client.Purchased += () => purchasedCount++;
            _client.HandleSceneEntered();
            ReplyInfo(inactive);
            Assert.That(_client.Buy(), Is.True);
            LastCall.Reply(new global::BuyAfkPassResponse { Info = active });
            Assert.That(purchasedCount, Is.EqualTo(1));
            Assert.That(_client.Info.UnlockedLoadoutSlots, Is.EqualTo(4u));
            Assert.That(_client.Info.Stage2TradableEnabled, Is.True);
        }

        [Test]
        public void AfkPassTeamArtSlices_All25SpritesLoadWithExpectedBordersAndSizes()
        {
            var entry = Resources.Load<Sprite>("UI/Ugui/WorldHud20261022/afkpass");
            Assert.That(entry, Is.Not.Null, "missing WorldHud20261022/afkpass");
            Assert.That((int)entry.rect.width, Is.EqualTo(128));
            Assert.That((int)entry.rect.height, Is.EqualTo(128));

            var sliced = new (string key, int w, int h)[]
            {
                ("afk_pass_panel_bg", 1080, 760),
                ("afk_pass_info_card", 920, 250),
                ("afk_pass_script_bar", 920, 52),
                ("afk_pass_toggle_off", 460, 62),
                ("afk_pass_toggle_on", 460, 62),
                ("afk_pass_btn_secondary", 260, 62),
                ("afk_pass_btn_primary", 400, 80),
                ("hosted_recap_bg", 920, 620),
                ("hosted_recap_row_bg", 728, 52),
                ("hosted_recap_btn_secondary", 210, 72),
                ("hosted_recap_btn_confirm", 302, 72),
                ("status_hosted_badge", 138, 46),
                ("team_recall_banner_bg", 660, 84),
                ("team_recall_btn_accept", 140, 52),
                ("team_recall_btn_dismiss", 94, 52),
            };
            foreach (var (key, w, h) in sliced)
            {
                var sp = GameplayUiArt.TryLoadAfkTeamArt(key);
                Assert.That(sp, Is.Not.Null, $"missing sliced sprite {key}");
                Assert.That((int)sp.rect.width, Is.EqualTo(w), key);
                Assert.That((int)sp.rect.height, Is.EqualTo(h), key);
                Assert.That(sp.border.sqrMagnitude, Is.GreaterThan(0f), $"missing 9-slice border on {key}");
            }

            var icons = new (string key, int size)[]
            {
                ("icon_recap_battle", 64),
                ("icon_recap_exp", 64),
                ("icon_recap_silver", 64),
                ("icon_recap_item", 64),
                ("icon_team_hosted", 48),
                ("icon_team_recall", 48),
                ("icon_formation_column", 48),
                ("icon_formation_echelon", 48),
                ("icon_formation_wuxing", 48),
            };
            foreach (var (key, size) in icons)
            {
                var sp = GameplayUiArt.TryLoadAfkTeamArt(key);
                Assert.That(sp, Is.Not.Null, $"missing icon sprite {key}");
                Assert.That((int)sp.rect.width, Is.EqualTo(size), key);
                Assert.That((int)sp.rect.height, Is.EqualTo(size), key);
            }
        }

        [Test]
        public void BuySeasonPlan90d_SendsPlanIdEmitsBagAndFormatsInstantPerks()
        {
            var info = Info(0);
            info.MarketListingLimit = 20;
            info.StallDurationHours = 96;
            info.WeeklyFreeTrialQuota = 3;
            info.Plans.Add(new global::AfkPassPlanView
            {
                PlanId = 1, PlanName = "仙途轻享卡（30天）", PeriodDays = 30,
                PriceCurrencyType = 1, Price = 300, BonusItemId = 3405, BonusItemCount = 1,
                BonusFrameKey = "frame_lightcard_s1",
            });
            info.Plans.Add(new global::AfkPassPlanView
            {
                PlanId = 2, PlanName = "仙途季享卡（90天）", PeriodDays = 90,
                PriceCurrencyType = 1, Price = 780, BonusItemId = 3405, BonusItemCount = 3,
                BonusFrameKey = "frame_lightcard_season_s1", IconKey = "icon_season_pass_90d",
            });
            EnterWith(info);

            Assert.That(AfkPassModels.FormatPlanBonus(info.Plans[1]), Does.Contain("定星保底符×3").And.Contain("头像框"));
            Assert.That(AfkPassModels.LightCardBenefitsSummary(info), Does.Contain("20").And.Contain("96h").And.Contain("3 次"));

            global::BagInfo receivedBag = null;
            _client.BagReceived += bag => receivedBag = bag;

            Assert.That(_client.Buy(2u), Is.True);
            var req = LastCall.Request as global::BuyAfkPassRequest;
            Assert.That(req, Is.Not.Null);
            Assert.That(req.PlanId, Is.EqualTo(2u));
            Assert.That(req.ExpectedPrice, Is.EqualTo(780UL));
            Assert.That(req.ExpectedPeriodDays, Is.EqualTo(90u));

            var updated = Info(Now + 90 * Day);
            LastCall.Reply(new global::BuyAfkPassResponse
            {
                Info = updated,
                Bag = new global::BagInfo(),
            });
            Assert.That(_client.Active, Is.True);
            Assert.That(receivedBag, Is.Not.Null);
            Assert.That(AfkPassModels.DescribeTip((uint)afk_pass_error.KAfkPassBagFull), Does.Contain("背包"));
        }
    }
}
