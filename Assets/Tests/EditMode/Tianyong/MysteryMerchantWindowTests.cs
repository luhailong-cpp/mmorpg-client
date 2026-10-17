using System.Collections.Generic;
using MmorpgClient.Game.MysteryMerchant;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector2 = UnityEngine.Vector2;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 神秘商人商店窗口:只读视图进、意图事件出。默认样例是商人就在本线、货架三件:
    /// 商品 1(限购 5、买过 1)、商品 2(限购 1、已买满)、商品 3(限购 1、3000 银两),余额 1000 银两。
    /// 商品 2 买满了不能点;商品 3 比余额贵,但余额只用于显示,照样能点(买不买得起由服务端裁决)。
    /// 两次点击的间隔由注入的假时钟推进。
    /// </summary>
    public sealed class MysteryMerchantWindowTests
    {
        private GameObject _root;
        private MysteryMerchantWindow _window;
        private const ulong Round = 1_757_001_600;
        private readonly List<MysteryMerchantQuote> _buys = new List<MysteryMerchantQuote>();
        private int _travels;
        private int _closes;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _buys.Clear();
            _travels = 0;
            _closes = 0;
            _now = 100f;
            _root = new GameObject("MysteryMerchantWindowTest", typeof(RectTransform));
            var design = QdaoUguiFactory.CreateCenteredRect("Design", _root.transform, 2560, 1080);
            _window = new MysteryMerchantWindow(design, () => _now);
            _window.BuyRequested += quote => _buys.Add(quote);
            _window.TravelRequested += () => _travels++;
            _window.Closed += () => _closes++;
        }

        [TearDown]
        public void TearDown()
        {
            _window?.Hide();
            if (_root != null) Object.DestroyImmediate(_root);
        }

        // ── 样例 ────────────────────────────────────────────────────────────

        private static global::MysteryMerchantGoodsInfo Goods(uint id, ulong price, uint limit, uint bought = 0, uint percent = 60) =>
            new global::MysteryMerchantGoodsInfo
            {
                GoodsId = id, ItemConfigId = 10 + id, ItemCount = 10, Name = "商品" + id, CurrencyType = 0,
                OriginalPrice = price * 100 / percent, Price = price, DiscountPercent = percent,
                LimitCount = limit, BoughtCount = bought,
            };

        private static List<global::MysteryMerchantGoodsInfo> ThreeGoods() => new List<global::MysteryMerchantGoodsInfo>
        {
            Goods(1, 240, 5, bought: 1),
            Goods(2, 300, 1, bought: 1, percent: 50),
            Goods(3, 3000, 1, percent: 40),
        };

        private static MysteryMerchantShopView Here(IReadOnlyList<global::MysteryMerchantGoodsInfo> goods = null) =>
            new MysteryMerchantShopView
            {
                Presence = MysteryMerchantPresence.Here, RoundId = Round, ChannelNo = 3, Goods = goods ?? ThreeGoods(), Ready = true,
                RemainingMs = 754_000, UntilNextMs = 2_554_000, Balances = new List<ulong> { 1000 },
            };

        /// <summary>点一下,等够确认间隔,再点一下:一次正常的「购买 → 确认购买」。</summary>
        private void ClickTwice(uint goodsId)
        {
            BuyButton(goodsId).onClick.Invoke();
            _now += MysteryMerchantWindow.ConfirmMinIntervalSeconds;
            BuyButton(goodsId).onClick.Invoke();
        }

        private Button BuyButton(uint goodsId) => Find<Button>(MysteryMerchantWindow.BuyButtonPrefix + goodsId);

        private T Find<T>(string name) where T : Component
        {
            foreach (var component in _root.GetComponentsInChildren<T>(true))
                if (component.name == name) return component;
            return null;
        }

        private static string Label(Button button) => button.GetComponentInChildren<TMP_Text>(true).text;

        private List<string> CardTexts(uint goodsId)
        {
            var texts = new List<string>();
            var card = Find<RectTransform>(MysteryMerchantWindow.CardPrefix + goodsId);
            Assert.That(card, Is.Not.Null, "card " + goodsId);
            foreach (var label in card.GetComponentsInChildren<TMP_Text>(true)) texts.Add(label.text);
            return texts;
        }

        // ── 货架 ────────────────────────────────────────────────────────────

        [Test]
        public void Show_BuildsOneCardPerGoods_WithDiscountPricesAndLimit()
        {
            _window.SetView(Here());
            _window.Show();

            var texts = CardTexts(1);
            Assert.That(texts, Does.Contain("商品1"));
            Assert.That(texts, Does.Contain("6折"));
            Assert.That(texts, Does.Contain("每份 ×10"));
            Assert.That(texts, Does.Contain("原价 400"));
            Assert.That(texts, Does.Contain("240 银两"));
            Assert.That(texts, Does.Contain("限购 1/5"));
            Assert.That(CardTexts(2), Does.Contain("5折"));
            Assert.That(CardTexts(3), Does.Contain("4折"));
            Assert.That(CardTexts(3), Does.Contain("3,000 银两"));
        }

        [Test]
        public void BuyButtons_ReflectWhyAGoodsCannotBeBought()
        {
            _window.SetView(Here());
            _window.Show();

            Assert.That(BuyButton(1).interactable, Is.True);
            Assert.That(Label(BuyButton(1)), Is.EqualTo(MysteryMerchantWindow.BuyText));
            Assert.That(BuyButton(2).interactable, Is.False);
            Assert.That(Label(BuyButton(2)), Is.EqualTo(MysteryMerchantWindow.SoldOutText));
            Assert.That(BuyButton(3).interactable, Is.True,
                "余额 1000、售价 3000 也不置灰:余额是某次快照,按它置灰的话玩家攒够了钱也点不了");
            Assert.That(Label(BuyButton(3)), Is.EqualTo(MysteryMerchantWindow.BuyText));
        }

        [Test]
        public void UnknownBalance_ShowsNoBalanceLine()
        {
            var view = Here();
            view.Balances = null;
            _window.SetView(view);
            _window.Show();
            Assert.That(BuyButton(3).interactable, Is.True);
            Assert.That(MysteryMerchantWindow.DescribeBalance(view), Is.EqualTo(""));
        }

        [Test]
        public void Buy_TakesTwoClicks_FirstArmsSecondConfirms_AndCarriesTheQuote()
        {
            _window.SetView(Here());
            _window.Show();

            BuyButton(1).onClick.Invoke();
            Assert.That(_buys, Is.Empty, "第一次点击只是把按钮变成「确认购买」");
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(1u));
            Assert.That(Label(BuyButton(1)), Is.EqualTo(MysteryMerchantWindow.ConfirmText));

            _now += MysteryMerchantWindow.ConfirmMinIntervalSeconds;
            BuyButton(1).onClick.Invoke();
            Assert.That(_buys.Count, Is.EqualTo(1));
            Assert.That(_buys[0], Is.EqualTo(new MysteryMerchantQuote(Round, 1, 240, 0, 11, 10)),
                "发出去的是第一次点击时看到的那份报价:轮次、价格、货币、物品、每份数量");
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(0u));
        }

        [Test]
        public void Buy_ADoubleClick_DoesNotConfirm()
        {
            _window.SetView(Here());
            _window.Show();

            BuyButton(1).onClick.Invoke();
            _now += MysteryMerchantWindow.ConfirmMinIntervalSeconds * 0.5f;
            BuyButton(1).onClick.Invoke();
            Assert.That(_buys, Is.Empty, "两次点击挨得太近:多半是双击,不算确认");
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(1u), "仍然待确认,再点一次即可");

            _now += MysteryMerchantWindow.ConfirmMinIntervalSeconds;
            BuyButton(1).onClick.Invoke();
            Assert.That(_buys.Count, Is.EqualTo(1));
        }

        [Test]
        public void ArmedQuote_IsVoided_WhenThePriceOrTheRoundChangesBetweenTheTwoClicks()
        {
            _window.SetView(Here());
            _window.Show();
            BuyButton(1).onClick.Invoke();
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(1u));

            // 两次点击之间配表热更:同一个商品 id,价格从 240 变成 300。
            var repriced = ThreeGoods();
            repriced[0].Price = 300;
            _window.SetView(Here(repriced));
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(0u), "确认绑定的是报价,不是商品 id");
            _now += MysteryMerchantWindow.ConfirmMinIntervalSeconds;
            BuyButton(1).onClick.Invoke();
            Assert.That(_buys, Is.Empty, "这一下只是对着新价格重新「购买」,不是确认");
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(1u));

            // 两次点击之间换了一轮:货架碰巧还有同一个 id、同一个价格,也不算同一份报价。
            var nextRound = Here(repriced);
            nextRound.RoundId = Round + 3600;
            _window.SetView(nextRound);
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(0u));
            ClickTwice(1);
            Assert.That(_buys.Count, Is.EqualTo(1));
            Assert.That(_buys[0], Is.EqualTo(new MysteryMerchantQuote(Round + 3600, 1, 300, 0, 11, 10)));
        }

        [Test]
        public void ArmedGoods_IsDisarmed_WhenItCanNoLongerBeBought_OrThePageChanges()
        {
            _window.SetView(Here());
            _window.Show();
            BuyButton(1).onClick.Invoke();
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(1u));

            // 新视图里商品 1 已买满:撤销待确认,下一次点击不能直接成交。
            var soldOut = ThreeGoods();
            soldOut[0].BoughtCount = 5;
            _window.SetView(Here(soldOut));
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(0u));
            BuyButton(1).onClick.Invoke();
            Assert.That(_buys, Is.Empty);

            // 关掉再开:待确认不跨越窗口的开关。
            _window.SetView(Here());
            BuyButton(1).onClick.Invoke();
            _window.Hide();
            _window.Show();
            Assert.That(_window.ArmedGoodsId, Is.EqualTo(0u));
        }

        [Test]
        public void WhileABuyIsPending_NoButtonIsClickable()
        {
            var view = Here();
            view.BuyingGoodsId = 1;
            _window.SetView(view);
            _window.Show();

            Assert.That(BuyButton(1).interactable, Is.False);
            Assert.That(Label(BuyButton(1)), Is.EqualTo(MysteryMerchantWindow.BuyingText));
            BuyButton(1).onClick.Invoke();
            Assert.That(_buys, Is.Empty);
        }

        [Test]
        public void InBattle_NothingCanBeBought_AndTheStatusSaysWhy()
        {
            var view = Here();
            view.InBattle = true;
            _window.SetView(view);
            _window.Show();
            Assert.That(BuyButton(1).interactable, Is.False);
            Assert.That(_window.StatusText, Is.EqualTo(MysteryMerchantModels.InBattleText));
        }

        [Test]
        public void Shelf_PaginatesBeyondSixGoods()
        {
            var many = new List<global::MysteryMerchantGoodsInfo>();
            for (uint id = 1; id <= 8; id++) many.Add(Goods(id, 100, 5));
            _window.SetView(Here(many));
            _window.Show();

            Assert.That(_window.PageCount, Is.EqualTo(2));
            Assert.That(BuyButton(6), Is.Not.Null);
            Assert.That(BuyButton(7), Is.Null, "第 7 件在下一页");
            Find<Button>(MysteryMerchantWindow.NextPageButtonName).onClick.Invoke();
            Assert.That(_window.PageIndex, Is.EqualTo(1));
            Assert.That(BuyButton(7), Is.Not.Null);
            Assert.That(BuyButton(1), Is.Null);
        }

        [Test]
        public void SameView_DoesNotRebuildTheCards()
        {
            _window.SetView(Here());
            _window.Show();
            var before = BuyButton(1);
            _window.SetView(Here());   // 内容相同的一份新视图(数据层每次变化都会整份重给)
            Assert.That(BuyButton(1), Is.SameAs(before), "卡片若被重建,按下与抬起之间撞上的那次点击就丢了");
        }

        // ── 不在本线 / 不在场 ──────────────────────────────────────────────

        [Test]
        public void Elsewhere_ShowsWhereHeIs_AndATravelButton()
        {
            var view = new MysteryMerchantShopView
            {
                Presence = MysteryMerchantPresence.Elsewhere, ChannelNo = 3, Ready = true, RemainingMs = 754_000, CanTravel = true,
            };
            _window.SetView(view);
            _window.Show();

            Assert.That(_window.HeadlineText, Is.EqualTo("正在 3线 · 12:34 后离开"));
            Assert.That(Find<TMP_Text>(MysteryMerchantWindow.EmptyLabelName).text, Does.Contain("3线"));
            var travel = Find<Button>(MysteryMerchantWindow.TravelButtonName);
            Assert.That(travel.gameObject.activeSelf, Is.True);
            Assert.That(Label(travel), Is.EqualTo("前往 3线"));
            travel.onClick.Invoke();
            Assert.That(_travels, Is.EqualTo(1));
        }

        [Test]
        public void Elsewhere_TravelButtonIsDisabled_WhenTheLineCannotBeEntered_AndTheStatusSaysWhy()
        {
            var view = new MysteryMerchantShopView
            {
                Presence = MysteryMerchantPresence.Elsewhere, ChannelNo = 3, Ready = true,
                TravelBlockedText = "该线路已满，请选择其他线路。",
            };
            _window.SetView(view);
            _window.Show();
            var travel = Find<Button>(MysteryMerchantWindow.TravelButtonName);
            Assert.That(travel.interactable, Is.False);
            travel.onClick.Invoke();
            Assert.That(_travels, Is.EqualTo(0));
            Assert.That(_window.StatusText, Is.EqualTo("该线路已满，请选择其他线路。"), "按钮灰着却不说为什么,玩家只能干等");

            // 能前往时不显示那句原因;数据层的失败提示始终排在它前面。
            view.CanTravel = true;
            Assert.That(MysteryMerchantWindow.DescribeStatus(view, out _), Is.EqualTo(""));
            view.CanTravel = false;
            view.Status = "神秘商人的行踪暂时打听不到。";
            view.StatusIsError = true;
            Assert.That(MysteryMerchantWindow.DescribeStatus(view, out bool isError), Is.EqualTo("神秘商人的行踪暂时打听不到。"));
            Assert.That(isError, Is.True);
        }

        [Test]
        public void Away_ShowsTheCountdownToTheNextAppearance_AndNoTravelButton()
        {
            var view = new MysteryMerchantShopView { Presence = MysteryMerchantPresence.Away, UntilNextMs = 2_710_000 };
            _window.SetView(view);
            _window.Show();
            Assert.That(_window.HeadlineText, Is.EqualTo("已经离开 · 45:10 后再次出现"));
            Assert.That(Find<Button>(MysteryMerchantWindow.TravelButtonName).gameObject.activeSelf, Is.False);

            view.UntilNextMs = 0;
            Assert.That(MysteryMerchantWindow.DescribeHeadline(view), Is.EqualTo("已经离开 · 即将出现"),
                "下一次的时刻已经到了、新一轮还没排出来:不显示「00:00 后再次出现」");

            view.UntilNextMs = -1;
            Assert.That(MysteryMerchantWindow.DescribeHeadline(view), Is.EqualTo(MysteryMerchantModels.AwayText));
            Assert.That(MysteryMerchantWindow.DescribeEmpty(view), Is.EqualTo(MysteryMerchantModels.UnknownNextText));
        }

        [Test]
        public void SetCountdown_UpdatesTheHeadline_WithoutASetView()
        {
            _window.SetView(Here());
            _window.Show();
            Assert.That(_window.HeadlineText, Is.EqualTo("就在本线 · 12:34 后离开"));
            _window.SetCountdown(753_000, 0);
            Assert.That(_window.HeadlineText, Is.EqualTo("就在本线 · 12:33 后离开"));
        }

        // ── 状态行与余额 ────────────────────────────────────────────────────

        [Test]
        public void DescribeStatus_ErrorFirst_ThenProgress_ThenHint()
        {
            var view = Here();
            Assert.That(MysteryMerchantWindow.DescribeStatus(view, out bool isError), Is.EqualTo(MysteryMerchantWindow.PickHint));
            Assert.That(isError, Is.False);

            view.Status = "购买成功：商品1 ×10";
            Assert.That(MysteryMerchantWindow.DescribeStatus(view, out isError), Is.EqualTo("购买成功：商品1 ×10"));
            Assert.That(isError, Is.False);

            view.Status = "背包已满，无法购买。";
            view.StatusIsError = true;
            Assert.That(MysteryMerchantWindow.DescribeStatus(view, out isError), Is.EqualTo("背包已满，无法购买。"));
            Assert.That(isError, Is.True);
        }

        [Test]
        public void DescribeBalance_ListsEachCurrencyOnTheShelfOnce()
        {
            var goods = ThreeGoods();
            goods[2].CurrencyType = 1;
            var view = Here(goods);
            view.Balances = new List<ulong> { 12_345, 67 };
            Assert.That(MysteryMerchantWindow.DescribeBalance(view), Is.EqualTo("银两 12,345   灵石 67"));
        }

        [Test]
        public void Hide_RaisesClosedOnce()
        {
            _window.Show();
            _window.Hide();
            _window.Hide();
            Assert.That(_closes, Is.EqualTo(1));
        }

        // ── 世界里的形象:几何 ──────────────────────────────────────────────

        [Test]
        public void OpeningSkillBook_ClosesMerchantInsteadOfLeavingItAboveTheSkillPanel()
        {
            var merchantObject = new GameObject("MerchantHostTest");
            merchantObject.transform.SetParent(_root.transform, false);
            var merchant = merchantObject.AddComponent<MysteryMerchantUiRoot>();
            // EditMode 下组件不会自动跑生命周期;SendMessage 会让 Unity 打出 'ShouldRunBehaviour()' 断言日志、
            // 被测试框架判为未处理的错误日志。与 TeamInvitationWindowTests 一样用反射直接调私有 Awake。
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            if (merchant.Window == null) typeof(MysteryMerchantUiRoot).GetMethod("Awake", flags).Invoke(merchant, null);
            merchant.Window.SetView(Here());
            merchant.Window.Show();
            var skillObject = new GameObject("SkillHostTest");
            skillObject.transform.SetParent(_root.transform, false);
            var skills = skillObject.AddComponent<MmorpgClient.UI.Ugui.Skills.SkillUiRoot>();
            if (MmorpgClient.UI.Ugui.Skills.SkillUiRoot.Instance == null)
                typeof(MmorpgClient.UI.Ugui.Skills.SkillUiRoot).GetMethod("Awake", flags).Invoke(skills, null);

            MmorpgClient.UI.Ugui.Skills.SkillUiRoot.Open();

            Assert.That(skills.IsVisible, Is.True);
            Assert.That(merchant.Window.IsVisible, Is.False,
                "商人画布在技能面板之上，打开技能必须收起商店，才能看到并操作技能面板。");
        }

        [Test]
        public void ApproachPoint_StopsShortOfTheMerchant_OnTheLineTowardsThePlayer()
        {
            var merchant = new Vector3(206, 0, 176);
            var player = new Vector3(216, 0.5f, 176);
            var point = MysteryMerchantNpc.ApproachPoint(merchant, player, 3f);
            Assert.That(point.x, Is.EqualTo(209f).Within(0.001f));
            Assert.That(point.z, Is.EqualTo(176f).Within(0.001f));
            Assert.That(point.y, Is.EqualTo(0.5f).Within(0.001f), "落脚点用玩家的高度");

            // 已经比停靠距离更近:不必再走。
            var near = new Vector3(207, 0, 176);
            Assert.That(MysteryMerchantNpc.ApproachPoint(merchant, near, 3f), Is.EqualTo(near));
        }

        [Test]
        public void OpenDistance_LeavesAMarginInsideTheServerRange()
        {
            Assert.That(MysteryMerchantNpc.OpenDistance(8), Is.EqualTo(6f));
            Assert.That(MysteryMerchantNpc.OpenDistance(2), Is.EqualTo(1f), "不小于 1");
            Assert.That(MysteryMerchantNpc.HorizontalDistance(new Vector3(0, 9, 0), new Vector3(3, 0, 4)), Is.EqualTo(5f).Within(0.001f),
                "只算水平距离,与服务端判定口径一致");
        }

        [Test]
        public void StandOff_AlwaysEndsInsideTheOpenDistance()
        {
            Assert.That(MysteryMerchantNpc.StandOff(8), Is.EqualTo(3f), "购买距离够大:停在 3 个单位外,不踩到商人身上");
            // 购买距离配小之后,停靠点必须跟着收近 —— 否则玩家停在开店距离之外,走到了店却永远不开。
            for (uint range = 0; range <= 12; range++)
                Assert.That(MysteryMerchantNpc.StandOff(range), Is.LessThan(MysteryMerchantNpc.OpenDistance(range)), "range=" + range);
            Assert.That(MysteryMerchantNpc.StandOff(5), Is.EqualTo(2.5f));
            Assert.That(MysteryMerchantNpc.StandOff(1), Is.EqualTo(0.5f), "不小于 0.5");
        }

        [Test]
        public void DisplayCurrency_UsesWhicheverSnapshotIsNewer()
        {
            var purchase = new global::CurrencyComp();
            purchase.Values.Add(100);
            var bagBefore = new global::BagInfo { Currency = new global::CurrencyComp() };
            bagBefore.Currency.Values.Add(340);
            var bagAfter = new global::BagInfo { Currency = new global::CurrencyComp() };
            bagAfter.Currency.Values.Add(5_000);

            Assert.That(MysteryMerchantUiRoot.DisplayCurrency(null, null, bagBefore), Is.SameAs(bagBefore.Currency), "没买过:用背包快照");
            Assert.That(MysteryMerchantUiRoot.DisplayCurrency(purchase, bagBefore, bagBefore), Is.SameAs(purchase),
                "刚买完、背包还没重拉回来:购买带回的余额更新");
            Assert.That(MysteryMerchantUiRoot.DisplayCurrency(purchase, bagBefore, bagAfter), Is.SameAs(bagAfter.Currency),
                "背包快照换过了:它更新(之后在别处赚的钱只有它知道)");
            Assert.That(MysteryMerchantUiRoot.DisplayCurrency(purchase, null, null), Is.SameAs(purchase));
            Assert.That(MysteryMerchantUiRoot.DisplayCurrency(purchase, bagBefore, null), Is.SameAs(purchase), "背包正在重拉:先用手里的");
            Assert.That(MysteryMerchantUiRoot.DisplayCurrency(null, null, null), Is.Null);
        }

        [Test]
        public void HotspotRect_ConvertsCentreOriginToTopLeftOrigin()
        {
            // HUD 2560×1080;脚底在中心偏右 100、偏下 200;头顶比脚底高 300。
            var rect = MysteryMerchantUiRoot.HotspotRect(new Vector2(100, -200), new Vector2(100, 100), new Vector2(2560, 1080), 0.5f);
            Assert.That(rect.height, Is.EqualTo(300f).Within(0.001f));
            Assert.That(rect.width, Is.EqualTo(150f).Within(0.001f));
            Assert.That(rect.x, Is.EqualTo(1280f + 100f - 75f).Within(0.001f), "水平方向以脚底为中心");
            Assert.That(rect.y, Is.EqualTo(540f - 100f).Within(0.001f), "顶边 = 头顶,换成从上往下量");
        }
    }
}
