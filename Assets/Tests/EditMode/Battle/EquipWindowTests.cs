using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 背包页的装备栏与装备 tooltip(服务端 docs/design/equipment-attributes.md §6)。
    /// 正式窗口 + 正式数据层(假传输),按 GameplayUiRoot 的接法连起来:从原生按钮点到出站请求,
    /// 再喂回包看界面。不建立账号或网络会话。
    /// </summary>
    public sealed class EquipWindowTests
    {
        private const ulong Herb = 9001, Sword = 5001, Boots = 7001;
        /// <summary>行囊自身读取失败时状态行的通用提示(GameplayWindow.StateLine)。</summary>
        private const string SyncFailedText = "暂时未能同步，请点击刷新重试";

        private GameObject _fixture;
        private GameplayWindow _window;
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;

        [SetUp]
        public void SetUp()
        {
            _fixture = new GameObject("EquipWindowTests", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_fixture.transform).sizeDelta = new Vector2(2560, 1080);
            _window = new GameplayWindow(_fixture.transform);
            _net = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            _client = new PlayerFeaturesClient(_net);
            // 与 GameplayUiRoot 同样的接法(那边是带单例与输入轮询的 MonoBehaviour,EditMode 里不实例化)。
            _window.BagRequested += type => _client.RequestBag(type);
            _window.EquipmentRequested += () => _client.RequestEquipment();
            _window.EquipRequested += id => _client.EquipItem(id);
            _window.UnequipRequested += id => _client.UnequipItem(id);
            _client.OnChanged += Push;
        }

        [TearDown]
        public void TearDown()
        {
            _client.OnChanged -= Push;
            _client.Dispose();
            if (_fixture != null) Object.DestroyImmediate(_fixture);
            _window = null;
        }

        private void Push()
            => _window.SetBagPage(_client.Bag, _client.BagLoading, _client.BagError, _client.BusySort,
                _client.Equipment, _client.EquipmentLoading, _client.EquipmentError, _client.BusyEquip);

        // ── 装备栏 ──────────────────────────────────────────

        [Test]
        public void OpeningBagPageReadsCharacterBagAndEquipmentSeparately()
        {
            _window.Show(GameplayPage.Bag);
            var reads = _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId);
            Assert.That(reads.Select(call => ((GetBagRequest)call.Request).BagType),
                Is.EqualTo(new[] { PlayerFeaturesClient.CharacterBagType, PlayerFeaturesClient.EquipmentBagType }));
            AssertText("读取中…");
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False);

            reads[1].Respond(new GetBagResponse { Bag = MakeEquipment(true) });
            Assert.That(HasText("读取中…"), Is.False);
            Assert.That(SlotNames(), Is.EqualTo(new[] { "EquipSlot_3", "EquipSlot_4", "EquipSlot_5", "EquipSlot_6" }),
                "行囊还没到,装备栏也照样画出来");
            AssertText("正在打开行囊…");
            SlotButton(6).onClick.Invoke();
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.True, "两份快照各读各的:身上的装备此时已经可以查看");
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷靴"));
            Assert.That(ActionLabel(), Is.EqualTo("卸下"));
        }

        [Test]
        public void EquipmentColumnRendersEveryServerSlotAndEmptySlotsShowPartName()
        {
            var equipment = MakeEquipment(true);
            // 表里的遗留槽位没有名字:照样画出来,显示「栏位 N」。
            equipment.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 0, EquipKind = 1 });
            Open(MakeBag(true), equipment);

            Assert.That(SlotNames(), Is.EqualTo(new[] { "EquipSlot_0", "EquipSlot_3", "EquipSlot_4", "EquipSlot_5", "EquipSlot_6" }),
                "全部槽位按槽号升序,含空槽");
            Assert.That(SlotTexts(0), Has.Member("栏位 0"));
            Assert.That(SlotTexts(3), Has.Member("武器"));
            Assert.That(SlotTexts(4), Has.Member("帽子"));
            Assert.That(SlotTexts(5), Has.Member("衣服"));
            foreach (uint empty in new uint[] { 0, 3, 4, 5 })
            {
                Assert.That(SlotButton(empty).interactable, Is.False, "空槽不可选");
                Assert.That(HasIcon(SlotButton(empty)), Is.False);
            }
            Assert.That(SlotButton(6).interactable, Is.True);
            Assert.That(HasIcon(SlotButton(6)), Is.True, "穿着的部位显示图标");
            Assert.That(SlotTexts(6), Has.None.EqualTo("鞋子"));
        }

        [Test]
        public void MoreSlotsThanOnePageAreReachableThroughColumnPager()
        {
            var equipment = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 10 } };
            for (uint slot = 0; slot < 10; ++slot)
                equipment.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = slot, EquipKind = 11, Name = "部位" + slot });
            Open(MakeBag(false), equipment);

            Assert.That(SlotNames().Length, Is.EqualTo(EquipUiStyle.EquipSlotsPerPage));
            Assert.That(SlotNames().First(), Is.EqualTo("EquipSlot_0"));
            Assert.That(FindButton("上").interactable, Is.False);
            Click("下");
            Assert.That(SlotNames(), Is.EqualTo(new[] { "EquipSlot_8", "EquipSlot_9" }));
            Assert.That(FindButton("下").interactable, Is.False);
            Click("上");
            Assert.That(SlotNames().Length, Is.EqualTo(EquipUiStyle.EquipSlotsPerPage));
            Assert.That(_net.Calls, Is.Empty, "翻页只是展示,不发请求");
        }

        // ── tooltip ─────────────────────────────────────────

        [Test]
        public void SelectingBagEquipmentShowsTooltipWithTierColouredLinesAndEquipActionButton()
        {
            Open(MakeBag(true), MakeEquipment(true));
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False, "默认选中的回春草不是装备");

            BagSlot(1).onClick.Invoke();

            Assert.That(TooltipRoot().gameObject.activeSelf, Is.True);
            Assert.That(TooltipRoot().GetSiblingIndex(), Is.EqualTo(TooltipRoot().parent.childCount - 1), "卡片压在页面内容之上");
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷剑"));
            Assert.That(TooltipText("TooltipState").text, Is.EqualTo("未穿戴"));
            Assert.That(TooltipText("TooltipLevel").text, Is.EqualTo("角色要求：等级 80"));
            Assert.That(TooltipText("TooltipClass").text, Is.EqualTo("职业要求：无"));
            // 回包里的属性行故意乱序:显示顺序只看 (tier, seq),基础属性在最前。
            Assert.That(TooltipLines().Select(line => line.text), Is.EqualTo(new[]
            {
                "伤害：1620", "所有技能上升 5/5", "物理必杀率 8%/10%", "力量 15/20", "伤害 900/1200", "准确 1200/1600",
            }));
            Assert.That(TooltipLines().Select(line => line.color), Is.EqualTo(new[]
            {
                EquipUiStyle.TooltipText, EquipUiStyle.TierBlue, EquipUiStyle.TierBlue, EquipUiStyle.TierBlue,
                EquipUiStyle.TierPink, EquipUiStyle.TierYellow,
            }));
            Assert.That(TooltipRoot().GetComponentsInChildren<TMP_Text>().All(text => !text.richText), Is.True,
                "服务器下发的字符串一律按纯文本显示");
            Assert.That(ActionLabel(), Is.EqualTo("装备"));
            Assert.That(ActionButton().interactable, Is.True);
            Assert.That(HasCheck(BagSlot(1)), Is.True);
            Assert.That(_net.Calls, Is.Empty, "选中与查看不发请求");

            ActionButton().onClick.Invoke();

            var call = _net.Calls.Single();
            Assert.That(call.MessageId, Is.EqualTo(MessageIds.EquipItem));
            Assert.That(((EquipItemRequest)call.Request).ItemId, Is.EqualTo(Sword));
        }

        [Test]
        public void WornItemShowsUnequipActionAndSendsUnequip()
        {
            Open(MakeBag(true), MakeEquipment(true));

            SlotButton(6).onClick.Invoke();

            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷靴"));
            Assert.That(TooltipText("TooltipState").text, Is.EqualTo("已穿戴"));
            Assert.That(TooltipLines().Select(line => line.text), Is.EqualTo(new[]
            {
                "防御：486", "速度：328", "敏捷 15/20", "所有属性 12/16", "抗物理 4%/5%",
            }));
            Assert.That(TooltipLines().Last().color, Is.EqualTo(EquipUiStyle.TierYellow));
            Assert.That(ActionLabel(), Is.EqualTo("卸下"));
            Assert.That(HasCheck(SlotButton(6)), Is.True);
            Assert.That(HasCheck(BagSlot(0)), Is.False, "选中在装备栏时,物品格不再打勾");

            ActionButton().onClick.Invoke();

            var call = _net.Calls.Single();
            Assert.That(call.MessageId, Is.EqualTo(MessageIds.UnequipItem));
            Assert.That(((UnequipItemRequest)call.Request).ItemId, Is.EqualTo(Boots));
        }

        [Test]
        public void NonEquipmentItemKeepsPlainDetailPanelWithoutTooltip()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.True);

            BagSlot(0).onClick.Invoke();

            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False);
            Assert.That(Buttons().Any(button => button.name == EquipTooltip.ActionName), Is.False);
            AssertText("回春草");
            AssertText("物品");
            AssertText("持有数量  3\n叠放上限  99");
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void LevelRequirementTurnsRedOnlyWhenCharacterLevelIsKnownAndTooLow()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            Assert.That(TooltipText("TooltipLevel").color, Is.EqualTo(EquipUiStyle.TooltipText), "不知道角色等级时不标红");

            _window.SetCharacterLevel(79);
            Assert.That(TooltipText("TooltipLevel").color, Is.EqualTo(EquipUiStyle.Unmet));
            Assert.That(ActionButton().interactable, Is.True, "标红只是提示,能不能穿由服务器裁决");
            _window.SetCharacterLevel(80);
            Assert.That(TooltipText("TooltipLevel").color, Is.EqualTo(EquipUiStyle.TooltipText));
        }

        [Test]
        public void TooltipShowsServerDescriptionBetweenAttributeLinesAndActionButton()
        {
            var bag = MakeBag(true);
            bag.Items[1].Description = "<b>雷泽</b>深处所铸，剑鸣如雷。";
            Open(bag, MakeEquipment(true));
            BagSlot(1).onClick.Invoke();

            var description = TooltipText(EquipTooltip.DescriptionName);
            Assert.That(description.text, Is.EqualTo("<b>雷泽</b>深处所铸，剑鸣如雷。"));
            Assert.That(description.richText, Is.False, "服务器下发的说明按纯文本显示");
            Assert.That(description.textWrappingMode, Is.EqualTo(TextWrappingModes.Normal));
            Assert.That(description.overflowMode, Is.EqualTo(TextOverflowModes.Overflow), "说明完整测量并换行,不截断文本");
            AssertStackedAboveAction(description);
            Assert.That(TooltipLines().Select(line => line.fontSize), Is.All.EqualTo(EquipUiStyle.TooltipLineFont),
                "六条属性加说明放得下,不压缩");

            // 没有说明的装备不画这一行,也不留空。
            SlotButton(6).onClick.Invoke();
            Assert.That(TooltipRoot().GetComponentsInChildren<TMP_Text>().Any(text => text.name == EquipTooltip.DescriptionName),
                Is.False);
        }

        [TestCase(8, false)]
        [TestCase(14, false)]
        [TestCase(50, true)]
        public void DescriptionKeepsItsRoomWhenAttributeLinesFillTheCard(int rowCount, bool expectScroll)
        {
            // 同名同值词条也必须完整保留;14条正常显示,50条长文本进入可滚动正文。
            var bag = MakeBag(true);
            var sword = bag.Items[1];
            sword.Description = "雷泽深处所铸，剑鸣如雷。";
            string duplicateName = expectScroll ? "重复属性名称很长时必须自动换行并保留完整词条内容" : "速度";
            for (int i = 6; i < rowCount; i++) sword.BaseAttrs.Add(Line(11, duplicateName, 0, 1, 328, 0));
            Open(bag, MakeEquipment(true));
            BagSlot(1).onClick.Invoke();

            var lines = TooltipLines();
            Assert.That(lines.Length, Is.EqualTo(rowCount));
            Assert.That(lines.Count(line => line.text == duplicateName + "：328"), Is.EqualTo(rowCount - 6), "同名同值属性不能合并或丢弃");
            Assert.That(lines.Select(line => line.fontSize), Is.All.GreaterThanOrEqualTo(EquipDetailCard.MinLineFont));
            var scroll = TooltipRoot().GetComponentsInChildren<ScrollRect>().SingleOrDefault();
            Assert.That(scroll != null, Is.EqualTo(expectScroll));
            if (expectScroll)
            {
                Assert.That(scroll.vertical, Is.True);
                Assert.That(scroll.horizontal, Is.False);
                Assert.That(scroll.viewport, Is.Not.Null);
                Assert.That(scroll.content, Is.Not.Null);
                Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null, "属性必须裁剪在视口内");
                Assert.That(scroll.content.parent, Is.EqualTo(scroll.viewport));
                Assert.That(scroll.viewport.sizeDelta.y, Is.GreaterThan(0));
                Assert.That(scroll.content.sizeDelta.y, Is.GreaterThan(scroll.viewport.sizeDelta.y));
                Assert.That(lines.All(line => line.transform.parent == scroll.content), Is.True);
            }
            else Assert.That(lines.All(line => line.transform.parent == TooltipRoot()), Is.True, "普通完整属性列表直接展示");
            var description = TooltipText(EquipTooltip.DescriptionName);
            AssertStackedAboveAction(description);
            Assert.That(TooltipRoot().sizeDelta.y, Is.LessThanOrEqualTo(EquipUiStyle.TooltipMaxH));
        }

        /// <summary>说明在属性可见区域之下、按钮之上,且至少有约定的最小高度。</summary>
        private void AssertStackedAboveAction(TMP_Text description)
        {
            var scroll = TooltipRoot().GetComponentInChildren<ScrollRect>();
            var lastLine = TooltipLines().Last().rectTransform;
            Assert.That(description.rectTransform.parent, Is.EqualTo(lastLine.parent));
            var action = (RectTransform)ActionButton().transform;
            float top = -description.rectTransform.anchoredPosition.y;
            float bottom = top + description.rectTransform.sizeDelta.y;
            Assert.That(top, Is.GreaterThanOrEqualTo(-lastLine.anchoredPosition.y + lastLine.sizeDelta.y - 0.01f));
            if (scroll != null)
            {
                Assert.That(description.rectTransform.parent, Is.EqualTo(scroll.content));
                Assert.That(scroll.content.sizeDelta.y, Is.GreaterThanOrEqualTo(bottom - 0.01f), "滚动范围包含全部属性和完整说明");
                float viewportBottom = -scroll.viewport.anchoredPosition.y + scroll.viewport.sizeDelta.y;
                Assert.That(viewportBottom, Is.LessThanOrEqualTo(-action.anchoredPosition.y + 0.01f), "滚动正文不覆盖固定操作按钮");
            }
            else Assert.That(bottom, Is.LessThanOrEqualTo(-action.anchoredPosition.y + 0.01f));
            Assert.That(description.rectTransform.sizeDelta.y, Is.GreaterThanOrEqualTo(EquipUiStyle.TooltipDescMinH - 0.01f));
        }

        // ── 穿上 / 卸下 ─────────────────────────────────────

        [Test]
        public void EquipSuccessMovesItemToEquipmentColumnAndTooltipFollowsIt()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            ActionButton().onClick.Invoke();

            // 在途:按钮置灰,刷新 / 整理也等回包;界面仍是旧快照。
            Assert.That(ActionButton().interactable, Is.False);
            Assert.That(FindButton("刷新").interactable, Is.False);
            Assert.That(FindButton("整理背包").interactable, Is.False);
            AssertText("正在更换装备…");
            Assert.That(OccupiedBagSlots(), Is.EqualTo(new[] { "BagSlot_0", "BagSlot_1" }));

            var worn = MakeEquipment(true);
            Wear(worn, 3, MakeSword());
            _net.Calls.Single().Respond(new EquipItemResponse { Bag = MakeBag(false), Equipment = worn });

            Assert.That(OccupiedBagSlots(), Is.EqualTo(new[] { "BagSlot_0" }), "穿上的装备从物品格消失");
            Assert.That(SlotButton(3).interactable, Is.True);
            Assert.That(HasIcon(SlotButton(3)), Is.True, "装备栏的武器位出现它");
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.True, "选中按 item_id 跟到装备栏");
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷剑"));
            Assert.That(TooltipText("TooltipState").text, Is.EqualTo("已穿戴"));
            Assert.That(ActionLabel(), Is.EqualTo("卸下"));
            Assert.That(ActionButton().interactable, Is.True);
            Assert.That(HasCheck(SlotButton(3)), Is.True);
            Assert.That(HasCheck(BagSlot(0)), Is.False);
            Assert.That(HasText("正在更换装备…"), Is.False);
            Assert.That(FindButton("刷新").interactable, Is.True);

            // 再卸下:同一件装备回到物品格,卡片跟回去。
            _net.Calls.Clear();
            ActionButton().onClick.Invoke();
            var call = _net.Calls.Single();
            Assert.That(call.MessageId, Is.EqualTo(MessageIds.UnequipItem));
            Assert.That(((UnequipItemRequest)call.Request).ItemId, Is.EqualTo(Sword));
            call.Respond(new UnequipItemResponse { Bag = MakeBag(true), Equipment = MakeEquipment(true) });

            Assert.That(OccupiedBagSlots(), Is.EqualTo(new[] { "BagSlot_0", "BagSlot_1" }));
            Assert.That(SlotButton(3).interactable, Is.False);
            Assert.That(SlotTexts(3), Has.Member("武器"), "卸下后空槽重新显示部位名");
            Assert.That(TooltipText("TooltipState").text, Is.EqualTo("未穿戴"));
            Assert.That(ActionLabel(), Is.EqualTo("装备"));
            Assert.That(HasCheck(BagSlot(1)), Is.True);
        }

        [Test]
        public void SelectionFollowsTheItemWhicheverSnapshotArrivesFirst()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            var worn = MakeEquipment(true);
            Wear(worn, 3, MakeSword());

            // 穿上:人物背包先到、装备栏后到(与 Push 的顺序相反),中间那次重画两边都找不到这件装备。
            _window.SetBag(MakeBag(false), false, null, false);
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False, "中间态不指向已经不在包里的装备");
            _window.SetEquipment(worn, false, null, false);
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷剑"), "另一份快照到了,选中跟过去");
            Assert.That(ActionLabel(), Is.EqualTo("卸下"));

            // 卸下:装备栏先到、人物背包后到。
            _window.SetEquipment(MakeEquipment(true), false, null, false);
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False);
            _window.SetBag(MakeBag(true), false, null, false);
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷剑"));
            Assert.That(ActionLabel(), Is.EqualTo("装备"));
            Assert.That(HasCheck(BagSlot(1)), Is.True);
        }

        [Test]
        public void RejectedEquipShowsServerReasonAndKeepsSelectionAndSnapshots()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            ActionButton().onClick.Invoke();

            _net.Calls.Single().Respond(new EquipItemResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipLevelNotEnough },
            });

            AssertText("装备失败:等级不足,无法装备");
            Assert.That(OccupiedBagSlots(), Is.EqualTo(new[] { "BagSlot_0", "BagSlot_1" }));
            Assert.That(SlotButton(3).interactable, Is.False);
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷剑"));
            Assert.That(ActionLabel(), Is.EqualTo("装备"));
            Assert.That(ActionButton().interactable, Is.True, "被拒后可以再点");
        }

        [Test]
        public void RefreshingWhileEquipIsInFlightDoesNotMaskTheRejectReason()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            ActionButton().onClick.Invoke();
            var equip = _net.Calls.Single();

            Click("背包"); // 回包前又点了一下顶部页签:窗口照常要求刷新两份快照

            Assert.That(_net.Calls.Count, Is.EqualTo(1), "回包自带两份全量:在途期间不发读取");
            Assert.That(HasText(SyncFailedText), Is.False, "也不能冒出一行「未能同步」");
            AssertText("正在更换装备…");

            equip.Respond(new EquipItemResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipLevelNotEnough },
            });
            var reads = _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId);
            Assert.That(reads.Select(call => ((GetBagRequest)call.Request).BagType), Is.EqualTo(new uint[] { 0, 2 }),
                "被挡下的那次刷新在穿脱收口后补发");
            reads[0].Respond(new GetBagResponse { Bag = MakeBag(true) });
            reads[1].Respond(new GetBagResponse { Bag = MakeEquipment(true) });

            AssertText("装备失败:等级不足,无法装备");
            Assert.That(HasText(SyncFailedText), Is.False);
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷剑"));
            Assert.That(ActionButton().interactable, Is.True);
        }

        [Test]
        public void RejectedUnequipBeforeTheBagArrivesStillLetsTheBagIn()
        {
            // 第一次打开:装备栏先到,行囊还在路上就点了「卸下」,被拒。
            _window.Show(GameplayPage.Bag);
            var reads = _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId);
            reads[1].Respond(new GetBagResponse { Bag = MakeEquipment(true) });
            SlotButton(6).onClick.Invoke();
            ActionButton().onClick.Invoke();
            _net.CallsOf(MessageIds.UnequipItem).Single().Respond(new UnequipItemResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipBagFull },
            });
            AssertText("正在打开行囊…");

            reads[0].Respond(new GetBagResponse { Bag = MakeBag(true) });

            Assert.That(HasText("行囊尚未开启"), Is.False, "行囊的读取没有被这次穿脱作废");
            Assert.That(OccupiedBagSlots(), Is.EqualTo(new[] { "BagSlot_0", "BagSlot_1" }));
            AssertText("卸下失败:背包已满,无法卸下");
            Assert.That(ActionLabel(), Is.EqualTo("卸下"));
            Assert.That(_net.Calls.Count, Is.EqualTo(3), "规则类拒绝不补拉");
        }

        [Test]
        public void TimedOutEquipResyncsBothSnapshotsAndShowsPlayerTextNotTheTransportString()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            ActionButton().onClick.Invoke();

            _net.Calls.Single().FailWith("rpc timeout");

            var reads = _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId);
            Assert.That(reads.Select(call => ((GetBagRequest)call.Request).BagType), Is.EqualTo(new uint[] { 0, 2 }),
                "超时不等于没执行:两份快照重新拉");
            Assert.That(OccupiedBagSlots(), Is.EqualTo(new[] { "BagSlot_0", "BagSlot_1" }), "重拉回来之前仍是旧快照");

            // 服务器其实已经穿上了。
            var worn = MakeEquipment(true);
            Wear(worn, 3, MakeSword());
            reads[0].Respond(new GetBagResponse { Bag = MakeBag(false) });
            reads[1].Respond(new GetBagResponse { Bag = worn });

            Assert.That(OccupiedBagSlots(), Is.EqualTo(new[] { "BagSlot_0" }));
            Assert.That(HasIcon(SlotButton(3)), Is.True);
            Assert.That(ActionLabel(), Is.EqualTo("卸下"), "不会再对着一件已经穿上的装备显示「装备」");
            AssertText("装备失败:网络异常,请稍后重试");
            Assert.That(HasText("rpc timeout"), Is.False, "传输层的内部串不上屏");
            Assert.That(HasText(SyncFailedText), Is.False);
        }

        [TestCase("tip", "读取装备栏失败（错误码 5）")]
        [TestCase("mistyped", "服务器未返回有效的装备栏数据")]
        [TestCase("transport", "读取装备栏失败:网络异常,请稍后重试")]
        public void EquipmentReadFailureLeavesBagPageUsableAndRefreshRetries(string kind, string reason)
        {
            _window.Show(GameplayPage.Bag);
            var reads = _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId);
            reads[0].Respond(new GetBagResponse { Bag = MakeBag(true) });
            if (kind == "tip") reads[1].Respond(new GetBagResponse { ErrorMessage = new TipInfoMessage { Id = 5 } });
            else if (kind == "mistyped") reads[1].Respond(new GetBagResponse { Bag = MakeBag(false) });
            else reads[1].FailWith("rpc timeout");

            AssertText("未同步");
            Assert.That(SlotNames(), Is.Empty);
            AssertText(reason);
            Assert.That(HasText(SyncFailedText), Is.False, "行囊自己没有出错");
            Assert.That(HasText("rpc timeout"), Is.False);
            // 物品格、普通物品的详情栏、背包里装备的卡片都照常。
            Assert.That(OccupiedBagSlots(), Is.EqualTo(new[] { "BagSlot_0", "BagSlot_1" }));
            AssertText("回春草");
            AssertText("持有数量  3\n叠放上限  99");
            BagSlot(1).onClick.Invoke();
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷剑"));
            Assert.That(ActionLabel(), Is.EqualTo("装备"));
            Assert.That(ActionButton().interactable, Is.True);

            _net.Calls.Clear();
            Click("刷新");

            Assert.That(_net.Calls.Select(call => ((GetBagRequest)call.Request).BagType), Is.EqualTo(new uint[] { 0, 2 }));
            AssertText("读取中…");
            Assert.That(HasText(reason), Is.False, "重试时收起上一次的错误");
            _net.Calls[0].Respond(new GetBagResponse { Bag = MakeBag(true) });
            _net.Calls[1].Respond(new GetBagResponse { Bag = MakeEquipment(true) });
            Assert.That(SlotNames().Length, Is.EqualTo(4));
            Assert.That(HasText("未同步") || HasText("读取中…") || HasText(reason), Is.False);
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷剑"), "选中没有因为重读丢掉");
        }

        [Test]
        public void SetBagPageSwapsBothSnapshotsTogether()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            var worn = MakeEquipment(true);
            Wear(worn, 3, MakeSword());

            // GameplayUiRoot.Changed 的口径:两份快照一次给齐,没有「新装备栏 + 旧背包」的中间态。
            _window.SetBagPage(MakeBag(false), false, null, false, worn, false, null, true);

            Assert.That(OccupiedBagSlots(), Is.EqualTo(new[] { "BagSlot_0" }));
            Assert.That(HasCheck(SlotButton(3)), Is.True);
            Assert.That(TooltipText("TooltipState").text, Is.EqualTo("已穿戴"));
            Assert.That(ActionLabel(), Is.EqualTo("卸下"));
            Assert.That(ActionButton().interactable, Is.False, "装备栏那一半的状态(穿脱在途)也一起生效");
            AssertText("正在更换装备…");
        }

        [Test]
        public void ArrangeInFlightDisablesEquipActionButton()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            _window.SortRequested += () => _client.SortBag();

            Click("整理背包");

            Assert.That(_client.BusySort, Is.True);
            Assert.That(ActionButton().interactable, Is.False, "整理与穿脱在数据层互斥,按钮直接置灰");
        }

        // ── 卡片的收起 ──────────────────────────────────────

        [Test]
        public void TooltipHidesWhenLeavingBagPageClosingOrLosingTheItem()
        {
            Open(MakeBag(true), MakeEquipment(true));
            BagSlot(1).onClick.Invoke();
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.True);

            _window.Show(GameplayPage.Missions);
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False, "切页收起");

            _window.Show(GameplayPage.Bag);
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.True, "回到背包页,记住的选中还在");
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("惊雷剑"));

            _window.Hide();
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False, "关窗收起");

            // 重新打开时的刷新回包里那件装备已经不在了:选中退回第一件物品(不是装备),卡片不再指向它。
            _window.Show(GameplayPage.Bag);
            _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId).Last(call => ((GetBagRequest)call.Request).BagType == 0)
                .Respond(new GetBagResponse { Bag = MakeBag(false) });
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False);
            AssertText("回春草");

            SlotButton(6).onClick.Invoke();
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.True);
            _window.ResetSession();
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False, "换角色 / 断线清场");
        }

        [Test]
        public void ResetSessionDropsEquipmentSnapshotAndSelectionBeforeAnotherCharacter()
        {
            Open(MakeBag(true), MakeEquipment(true));
            SlotButton(6).onClick.Invoke();
            Assert.That(ActionLabel(), Is.EqualTo("卸下"));
            // 下面只看窗口自己清没清:不让数据层把上一个角色的快照再推回来。
            _client.OnChanged -= Push;

            _window.ResetSession();
            _window.SetBag(MakeBag(true), false, null, false);
            _window.Show(GameplayPage.Bag);

            Assert.That(SlotNames(), Is.Empty, "上一个角色的装备栏不能留到下一个角色");
            AssertText("未读取");
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False);
            // 新角色的装备栏到达:即使里面有同一个 id,上一个会话的选中也不该还在。
            _window.SetEquipment(MakeEquipment(true), false, null, false);
            Assert.That(SlotButton(6).interactable, Is.True);
            Assert.That(HasCheck(SlotButton(6)), Is.False);
            Assert.That(TooltipRoot().gameObject.activeSelf, Is.False);
        }

        // ── 夹具与查询 ──────────────────────────────────────

        /// <summary>打开背包页并喂两份读回包(人物背包、装备栏),然后清掉出站记录。</summary>
        private void Open(BagInfo bag, BagInfo equipment)
        {
            _window.Show(GameplayPage.Bag);
            var reads = _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId);
            Assert.That(reads.Count, Is.EqualTo(2));
            reads[0].Respond(new GetBagResponse { Bag = bag });
            reads[1].Respond(new GetBagResponse { Bag = equipment });
            _net.Calls.Clear();
        }

        /// <summary>人物背包:0 号格回春草;withSword 时 1 号格是惊雷剑。</summary>
        private static BagInfo MakeBag(bool withSword)
        {
            var bag = new BagInfo
            {
                Layout = new BagLayoutInfo { BagType = 0, Capacity = 40, CanSort = true },
                Currency = new CurrencyComp(),
            };
            bag.Items.Add(new BagItemInfo { ItemId = Herb, ConfigId = 101, Name = "回春草", Count = 3, MaxStack = 99 });
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = 0, ItemId = Herb, Width = 1, Height = 1 });
            if (withSword)
            {
                bag.Items.Add(MakeSword());
                bag.Layout.Slots.Add(new BagSlotInfo { Slot = 1, ItemId = Sword, Width = 1, Height = 1 });
            }
            return bag;
        }

        /// <summary>装备栏:3 武器 / 4 帽子 / 5 衣服 / 6 鞋子四个槽位定义(乱序下发);withBoots 时鞋子位穿着惊雷靴。</summary>
        private static BagInfo MakeEquipment(bool withBoots)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 7 } };
            bag.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 5, EquipKind = 13, Name = "衣服" });
            bag.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 3, EquipKind = 11, Name = "武器" });
            bag.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 6, EquipKind = 14, Name = "鞋子" });
            bag.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 4, EquipKind = 12, Name = "帽子" });
            if (withBoots) Wear(bag, 6, MakeBoots());
            return bag;
        }

        private static void Wear(BagInfo equipment, uint slot, BagItemInfo item)
        {
            equipment.Items.Add(item);
            equipment.Layout.Slots.Add(new BagSlotInfo { Slot = slot, ItemId = item.ItemId, Width = 1, Height = 1 });
        }

        /// <summary>设计文档 §2.6 的 80 级武器样例;随机属性故意乱序放。</summary>
        private static BagItemInfo MakeSword()
        {
            var item = new BagItemInfo
            {
                ItemId = Sword, ConfigId = 1105, Name = "惊雷剑", Count = 1, MaxStack = 1, EquipKind = 11, EquipLevel = 80,
            };
            item.BaseAttrs.Add(Line(1, "伤害", 0, 0, 1620, 0));
            item.Affixes.Add(Line(2, "准确", 3, 0, 1200, 1600));
            item.Affixes.Add(Line(3, "力量", 1, 2, 15, 20));
            item.Affixes.Add(Line(1, "伤害", 2, 0, 900, 1200));
            item.Affixes.Add(Line(12, "物理必杀率", 1, 1, 8, 10, true));
            item.Affixes.Add(Line(17, "所有技能上升", 1, 0, 5, 5));
            return item;
        }

        private static BagItemInfo MakeBoots()
        {
            var item = new BagItemInfo
            {
                ItemId = Boots, ConfigId = 1405, Name = "惊雷靴", Count = 1, MaxStack = 1, EquipKind = 14, EquipLevel = 80,
            };
            item.BaseAttrs.Add(Line(8, "防御", 0, 0, 486, 0));
            item.BaseAttrs.Add(Line(11, "速度", 0, 1, 328, 0));
            item.Affixes.Add(Line(6, "敏捷", 1, 0, 15, 20));
            item.Affixes.Add(Line(7, "所有属性", 1, 1, 12, 16));
            item.Affixes.Add(Line(26, "抗物理", 3, 0, 4, 5, true));
            return item;
        }

        private static EquipAttrLineInfo Line(uint attrId, string name, uint tier, uint seq, ulong value, ulong cap,
            bool percent = false)
            => new EquipAttrLineInfo { AttrId = attrId, Name = name, Tier = tier, Seq = seq, Value = value, Cap = cap, Percent = percent };

        private Button[] Buttons() => _fixture.GetComponentsInChildren<Button>();

        private string[] SlotNames()
            => Buttons().Where(button => button.name.StartsWith("EquipSlot_")).Select(button => button.name).ToArray();

        private Button SlotButton(uint slot) => Buttons().Single(button => button.name == "EquipSlot_" + slot);

        private string[] SlotTexts(uint slot)
            => SlotButton(slot).GetComponentsInChildren<TMP_Text>().Select(text => text.text).ToArray();

        private Button BagSlot(uint slot) => Buttons().Single(button => button.name == "BagSlot_" + slot);

        private string[] OccupiedBagSlots()
            => Buttons().Where(button => button.name.StartsWith("BagSlot_") && button.interactable)
                .Select(button => button.name).ToArray();

        private static bool HasIcon(Button button)
            => button.GetComponentsInChildren<Image>().Any(image => image.name == "ItemIcon");

        private static bool HasCheck(Button button)
            => button.GetComponentsInChildren<Image>().Any(image => image.name == "check");

        private RectTransform TooltipRoot()
            => _fixture.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == EquipTooltip.RootName);

        private TMP_Text TooltipText(string name)
            => TooltipRoot().GetComponentsInChildren<TMP_Text>().Single(text => text.name == name);

        private TMP_Text[] TooltipLines()
            => TooltipRoot().GetComponentsInChildren<TMP_Text>()
                .Where(text => text.name.StartsWith(EquipTooltip.LinePrefix)).ToArray();

        private Button ActionButton() => Buttons().Single(button => button.name == EquipTooltip.ActionName);

        private string ActionLabel() => ActionButton().GetComponentInChildren<TMP_Text>().text;

        private bool HasText(string text)
            => _fixture.GetComponentsInChildren<TMP_Text>().Any(label => label.text == text);

        private void AssertText(string text) => Assert.That(HasText(text), Is.True, "当前页面应显示：" + text);

        private Button FindButton(string label)
        {
            var matches = Buttons().Where(button => button.GetComponentsInChildren<TMP_Text>().Any(text => text.text == label)).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), "当前页面应有唯一按钮：" + label);
            return matches[0];
        }

        private void Click(string label)
        {
            var button = FindButton(label);
            Assert.That(button.interactable, Is.True, "按钮应可点击：" + label);
            button.onClick.Invoke();
        }
    }
}
