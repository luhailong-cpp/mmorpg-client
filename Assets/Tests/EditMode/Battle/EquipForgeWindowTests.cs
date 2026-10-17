using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>正式窗口 + 按钮状态 + 正式数据层：从原生控件点击到出站，再用回包验证重画与弹窗生命周期。</summary>
    public sealed class EquipForgeWindowTests
    {
        private const ulong Sword = 5001, WornSword = 7001;
        private const uint Stone = 2001, AttackGem = 2101;
        private GameObject _fixture;
        private GameplayWindow _window;
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private EquipForgeUiState _forge;
        private long _now;

        [SetUp]
        public void SetUp()
        {
            _fixture = new GameObject("EquipForgeWindowTests", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_fixture.transform).sizeDelta = new Vector2(2560, 1080);
            _net = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            _client = new PlayerFeaturesClient(_net);
            _now = 100000;
            _client.NowMs = () => _now;
            _forge = new EquipForgeUiState();
            _window = new GameplayWindow(_fixture.transform, _forge);
            _window.BagRequested += type => _client.RequestBag(type);
            _window.EquipmentRequested += () => _client.RequestEquipment();
            _window.EquipRequested += id => _client.EquipItem(id);
            _window.UnequipRequested += id => _client.UnequipItem(id);
            _client.OnChanged += Push;
            // GameplayUiRoot 先订阅窗口推送，再 Bind forge；保持同样的重入顺序。
            _forge.Bind(_client);
        }

        [TearDown]
        public void TearDown()
        {
            _client.OnChanged -= Push;
            _window.Dispose();
            _forge.Dispose();
            _client.Dispose();
            if (_fixture != null) Object.DestroyImmediate(_fixture);
        }

        [Test]
        public void MoreOpensForgeWithoutRequestsAndRendersTheCurrentEquipment()
        {
            Open();
            Click("BagSlot_0");
            Click("EquipMore");
            Click("EquipOpenForge");

            Assert.That(ForgeRoot().gameObject.activeInHierarchy, Is.True);
            Assert.That(_forge.TargetItemId, Is.EqualTo(Sword));
            Assert.That(_window.HasEquipmentModal, Is.True);
            Assert.That(_fixture.GetComponentsInChildren<CanvasGroup>().Single().interactable, Is.False);
            Assert.That(Text("EquipForgeTitle").text, Is.EqualTo("青铜剑"));
            Assert.That(HasText("强化等级 +0 / +12"), Is.True);
            Assert.That(HasText("成功率 100%"), Is.True);
            Assert.That(Text("EquipForgeCost").text, Is.EqualTo("消耗 银两 1000、强化石 ×1"));
            Assert.That(Text("EquipForgeOwned").text, Is.EqualTo("持有 银两 50000、强化石 ×5"));
            Assert.That(HasText("伤害：40 → 42"), Is.True);
            Assert.That(Button("EquipForgeEnhance").interactable, Is.True);
            Assert.That(Button("EquipForgeSocket_0").GetComponentInChildren<TMP_Text>().text, Is.EqualTo("镶嵌"));
            Assert.That(_net.Calls, Is.Empty, "卡片、更多与打开面板只展示已同步的快照");
        }

        [Test]
        public void EnhanceStaysOpenDuringTheRequestAndRedrawsTheResultAndCooldown()
        {
            OpenFromMore();
            Click("EquipForgeEnhance");
            var call = _net.Calls.Single();
            Assert.That(call.MessageId, Is.EqualTo(MessageIds.EnhanceEquip));
            Assert.That(((EnhanceEquipRequest)call.Request).ItemId, Is.EqualTo(Sword));
            Assert.That(ForgeRoot().gameObject.activeInHierarchy, Is.True,
                "更多先关闭再进强化：目标 id 必须保留，发包的 OnChanged 不能把面板关掉");
            Assert.That(_window.HasEquipmentModal, Is.True);
            Assert.That(Text("EquipForgeStatus").text, Is.EqualTo("正在强化…"));
            Assert.That(Button("EquipForgeEnhance").interactable, Is.False);
            Assert.That(Button("EquipForgeRefresh").interactable, Is.False);

            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, sword: Weapon(Sword, 1)),
                Equipment = StandardEquipment(), Success = true, EnhanceLevel = 1,
            });

            Assert.That(ForgeRoot().gameObject.activeInHierarchy, Is.True);
            Assert.That(Text("EquipForgeTitle").text, Is.EqualTo("青铜剑 +1"));
            Assert.That(TooltipText("TooltipName").text, Is.EqualTo("青铜剑 +1"));
            Assert.That(Text("EquipForgeOwned").text, Is.EqualTo("持有 银两 49000、强化石 ×4"));
            Assert.That(Text("EquipForgeStatus").text, Is.EqualTo("强化成功：青铜剑 +1"));
            Assert.That(Button("EquipForgeEnhance").interactable, Is.False);
            Assert.That(Text("EquipForgeEnhanceHint").text, Is.EqualTo("操作太快，请稍候"));
            Assert.That(_net.CallsOf(MessageIds.EnhanceEquip).Count, Is.EqualTo(1), "重画不自动发出下一次强化");

            FinishCooldown();
            Assert.That(Button("EquipForgeEnhance").interactable, Is.True);
            Assert.That(Text("EquipForgeStatus").text, Is.EqualTo("强化成功：青铜剑 +1"));
        }

        [Test]
        public void InlaySelectionAndRemoveSendTheSocketAndRedrawItsNewState()
        {
            OpenFromMore();
            Click("EquipForgeSocket_0");
            Assert.That(_forge.View.Picker, Is.Not.Null);
            Assert.That(HasText("选择要镶入「攻击孔」的宝石"), Is.True);
            Assert.That(_net.Calls, Is.Empty, "选孔还没有写请求");
            Click("EquipForgeGem_2101");
            var inlay = _net.Calls.Single();
            Assert.That(inlay.MessageId, Is.EqualTo(MessageIds.InlayGem));
            var request = (InlayGemRequest)inlay.Request;
            Assert.That(request.ItemId, Is.EqualTo(Sword));
            Assert.That(request.Socket, Is.EqualTo(0u));
            Assert.That(request.GemConfigId, Is.EqualTo(AttackGem));
            Assert.That(_forge.View.Picker, Is.Null);
            Assert.That(Text("EquipForgeStatus").text, Is.EqualTo("正在镶嵌…"));
            inlay.Respond(new InlayGemResponse
            {
                Bag = StandardBag(attackGems: 1, sword: WithGem(Weapon(Sword), 0)), Equipment = StandardEquipment(),
            });
            Assert.That(HasText("攻击孔：攻击宝石·一级（伤害 +30）"), Is.True);
            Assert.That(TooltipRoot().GetComponentsInChildren<TMP_Text>()
                .Any(text => text.text.Contains("攻击宝石·一级")), Is.True);
            Assert.That(Button("EquipForgeSocket_0").GetComponentInChildren<TMP_Text>().text, Is.EqualTo("取下"));

            FinishCooldown();
            Click("EquipForgeSocket_0");
            var remove = _net.Calls.Last();
            Assert.That(remove.MessageId, Is.EqualTo(MessageIds.RemoveGem));
            Assert.That(((RemoveGemRequest)remove.Request).ItemId, Is.EqualTo(Sword));
            Assert.That(((RemoveGemRequest)remove.Request).Socket, Is.EqualTo(0u));
            Assert.That(Text("EquipForgeStatus").text, Is.EqualTo("正在取下宝石…"));
            remove.Respond(new RemoveGemResponse { Bag = StandardBag(), Equipment = StandardEquipment() });
            Assert.That(HasText("攻击孔：未镶嵌"), Is.True);
            Assert.That(Button("EquipForgeSocket_0").GetComponentInChildren<TMP_Text>().text, Is.EqualTo("镶嵌"));
            Assert.That(ForgeRoot().gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public void DegradingEnhanceCancelsWithoutSendingAndOnlyConfirmSends()
        {
            OpenFromMore(StandardBag(sword: Weapon(Sword, level: 5, rate: 7500, failLoss: 1)));
            Click("EquipForgeEnhance");
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(HasText("成功率 75%，消耗 银两 1000、强化石 ×1。\n失败会降 1 级（+5 → +4），消耗不退还。"), Is.True);
            Assert.That(Button("EquipForgeConfirmEnhance").interactable, Is.True);
            Click("EquipForgeCancelConfirm");
            Assert.That(_forge.View.Confirm, Is.Null);
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_window.HasEquipmentModal, Is.True);

            Click("EquipForgeEnhance");
            Click("EquipForgeConfirmEnhance");
            Assert.That(_net.Calls.Single().MessageId, Is.EqualTo(MessageIds.EnhanceEquip));
            Assert.That(((EnhanceEquipRequest)_net.Calls.Single().Request).ItemId, Is.EqualTo(Sword));
            Assert.That(_forge.View.Confirm, Is.Null);
            Assert.That(ForgeRoot().gameObject.activeInHierarchy, Is.True);
        }

        [TestCase("picker")]
        [TestCase("confirm")]
        public void EscapeCancelsTheInnerStepBeforeClosingForgeAndKeepsTheBagOpen(string step)
        {
            OpenFromMore(StandardBag(sword: Weapon(Sword, level: 5, failLoss: 1)));
            Click(step == "picker" ? "EquipForgeSocket_0" : "EquipForgeEnhance");
            Assert.That(_forge.View.Picker != null || _forge.View.Confirm != null, Is.True);

            Escape();

            Assert.That(_forge.View.Picker, Is.Null);
            Assert.That(_forge.View.Confirm, Is.Null);
            Assert.That(_forge.IsOpen && _window.HasEquipmentModal && _window.IsVisible, Is.True);
            Assert.That(_net.Calls, Is.Empty);
            Escape();
            Assert.That(_forge.IsOpen || _window.HasEquipmentModal, Is.False);
            Assert.That(_window.IsVisible, Is.True);
            Assert.That(_fixture.GetComponentsInChildren<CanvasGroup>().Single().interactable, Is.True);
            Assert.That(_window.EquipmentModalOwnsKeyboard, Is.True, "本帧 Esc 已由面板处理，不能穿透关背包");
        }

        [Test]
        public void MissingTargetClosesForgeAndRestoresTheBagControls()
        {
            OpenFromMore();
            Click("EquipForgeSocket_0");
            _client.RequestBag();
            _net.Calls.Last().Respond(new GetBagResponse { Bag = CharacterBag(50000, Material(5)) });

            Assert.That(_forge.IsOpen || _window.HasEquipmentModal, Is.False);
            Assert.That(ForgeRoot().gameObject.activeSelf, Is.False);
            Assert.That(_window.IsVisible, Is.True);
            Assert.That(_fixture.GetComponentsInChildren<CanvasGroup>().Single().interactable, Is.True);
            Assert.That(_net.CallsOf(MessageIds.InlayGem), Is.Empty);
        }

        [Test]
        public void ClosingTheWindowDuringEnhanceDoesNotReopenItWhenTheResponseArrives()
        {
            OpenFromMore();
            Click("EquipForgeEnhance");
            var call = _net.Calls.Single();
            _window.Hide();
            Assert.That(_forge.IsOpen || _window.IsVisible || _window.HasEquipmentModal, Is.False);
            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, sword: Weapon(Sword, 1)),
                Equipment = StandardEquipment(), Success = true, EnhanceLevel = 1,
            });
            Assert.That(_forge.IsOpen || _window.IsVisible || _window.HasEquipmentModal, Is.False);
            Assert.That(ForgeRoot().gameObject.activeSelf, Is.False);
        }

        [Test]
        public void MaterialShortageAndServerRejectionAreShownWithTheDisabledButtonHint()
        {
            OpenFromMore(StandardBag(stones: 0));
            Assert.That(Button("EquipForgeEnhance").interactable, Is.False);
            Assert.That(Text("EquipForgeEnhanceHint").text, Is.EqualTo("强化材料不足"));
            Assert.That(Text("EquipForgeOwned").text, Does.Contain("强化石 ×0"));
            Button("EquipForgeEnhance").onClick.Invoke();
            Assert.That(_net.Calls, Is.Empty, "即使直接触发禁用控件的回调也不能发包");

            _client.RequestBag();
            _net.Calls.Last().Respond(new GetBagResponse { Bag = StandardBag() });
            Assert.That(Button("EquipForgeEnhance").interactable, Is.True);
            Click("EquipForgeEnhance");
            _net.Calls.Last().Respond(new EnhanceEquipResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipEnhanceGoldNotEnough },
            });
            Assert.That(Text("EquipForgeStatus").text, Is.EqualTo("无法强化:银两不足,无法强化"));
            Assert.That(Text("EquipForgeStatus").color, Is.EqualTo(QdaoUguiTheme.Html("#9A442D")));
            Assert.That(Button("EquipForgeEnhance").interactable, Is.False);
            Assert.That(Text("EquipForgeEnhanceHint").text, Is.EqualTo("操作太快，请稍候"));
            Assert.That(_window.HasEquipmentModal, Is.True);
        }

        [Test]
        public void WornEquipmentCanOpenWhileBagIsMissingAndRefreshAfterReadFailure()
        {
            _window.Show(GameplayPage.Bag);
            var reads = _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId);
            reads[0].FailWith("timeout");
            reads[1].Respond(new GetBagResponse { Bag = StandardEquipment() });
            _net.Calls.Clear();
            _window.OpenEquipmentForge(WornSword);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "只补拉尚未同步的行囊");
            Assert.That(Text("EquipForgeStage").text, Is.EqualTo("正在同步行囊与装备栏…"));
            Assert.That(Button("EquipForgeEnhance").interactable, Is.False);
            Assert.That(Button("EquipForgeRefresh").interactable, Is.False);
            _net.Calls.Single().FailWith("timeout");
            Assert.That(Text("EquipForgeStage").text, Is.EqualTo("行囊或装备栏暂未同步，请点击刷新重试"));
            Assert.That(Button("EquipForgeRefresh").interactable, Is.True);

            Click("EquipForgeRefresh");
            reads = _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId);
            reads[1].Respond(new GetBagResponse { Bag = StandardBag() });
            reads[2].Respond(new GetBagResponse { Bag = StandardEquipment() });
            Assert.That(_forge.View.Stage, Is.EqualTo(EquipForgeStage.Ready));
            Assert.That(Text("EquipForgeTitle").text, Is.EqualTo("青铜剑 +3"));
            Assert.That(_window.HasEquipmentModal, Is.True);
        }

        [Test]
        public void ServerMarkupIsLiteralInTheMainPanelAndBothInnerSteps()
        {
            var sword = Weapon(Sword, level: 5, failLoss: 1);
            sword.Name = "<b>青铜剑</b>";
            sword.BaseAttrs[0].Name = "<color=red>伤害</color>";
            var bag = StandardBag(sword: sword);
            bag.Items.Single(item => item.ConfigId == AttackGem).Name = "<i>攻击宝石</i>";
            OpenFromMore(bag);
            Assert.That(Text("EquipForgeTitle").text, Is.EqualTo("<b>青铜剑</b> +5"));
            AssertPlainText();
            Click("EquipForgeSocket_0");
            Assert.That(HasText("<i>攻击宝石</i>"), Is.True);
            AssertPlainText();
            Click("EquipForgeCancelInlay");
            Click("EquipForgeEnhance");
            Assert.That(_forge.View.Confirm, Is.Not.Null);
            AssertPlainText();
        }

        [Test]
        public void SocketButtonsPassWheelAndDragToTheirScrollListAndKeyboardFocusStaysVisible()
        {
            var sword = Weapon(Sword);
            for (uint socket = 1; socket < 6; ++socket)
                sword.GemSockets.Add(new EquipGemSocketInfo { Socket = socket, GemType = 1, Name = "攻击孔" + socket });
            var events = _fixture.AddComponent<EventSystem>();
            OpenFromMore(StandardBag(sword: sword));
            Canvas.ForceUpdateCanvases();
            var list = ForgeRoot().GetComponentsInChildren<ScrollRect>().Single();
            list.Rebuild(CanvasUpdate.PostLayout);
            list.verticalNormalizedPosition = 1;
            var button = Button("EquipForgeSocket_0").gameObject;

            var pointer = new PointerEventData(events) { scrollDelta = new Vector2(0, -1) };
            ExecuteEvents.ExecuteHierarchy(button, pointer, ExecuteEvents.scrollHandler);
            Assert.That(list.content.anchoredPosition.y, Is.GreaterThan(0),
                "光标在镶嵌按钮上滚轮也必须滚动孔位列表");

            list.verticalNormalizedPosition = 1;
            pointer = new PointerEventData(events)
            {
                button = PointerEventData.InputButton.Left, position = Vector2.zero,
            };
            ExecuteEvents.ExecuteHierarchy(button, pointer, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.ExecuteHierarchy(button, pointer, ExecuteEvents.beginDragHandler);
            pointer.position = new Vector2(0, 64);
            ExecuteEvents.ExecuteHierarchy(button, pointer, ExecuteEvents.dragHandler);
            ExecuteEvents.ExecuteHierarchy(button, pointer, ExecuteEvents.endDragHandler);
            Assert.That(list.content.anchoredPosition.y, Is.GreaterThan(0),
                "从镶嵌按钮上拖动也必须滚动孔位列表");

            list.verticalNormalizedPosition = 1;
            var last = Button("EquipForgeSocket_5");
            events.SetSelectedGameObject(last.gameObject);
            var visible = new Bounds(list.viewport.rect.center, list.viewport.rect.size);
            var focused = RectTransformUtility.CalculateRelativeRectTransformBounds(list.viewport, last.transform);
            Assert.That(focused.min.y, Is.GreaterThanOrEqualTo(visible.min.y - .1f));
            Assert.That(focused.max.y, Is.LessThanOrEqualTo(visible.max.y + .1f));
            Assert.That(_net.Calls, Is.Empty, "滚动与焦点移动都没有装备操作");
        }

        private void Push()
            => _window.SetBagPage(_client.Bag, _client.BagLoading, _client.BagError, _client.BusySort,
                _client.Equipment, _client.EquipmentLoading, _client.EquipmentError, _client.BusyEquip);

        private void Open(BagInfo bag = null)
        {
            _window.Show(GameplayPage.Bag);
            var reads = _net.CallsOf(SceneBagClientPlayerGetBagHandler.MessageId);
            Assert.That(reads.Count, Is.EqualTo(2));
            reads[0].Respond(new GetBagResponse { Bag = bag ?? StandardBag() });
            reads[1].Respond(new GetBagResponse { Bag = StandardEquipment() });
            _net.Calls.Clear();
        }

        private void OpenFromMore(BagInfo bag = null)
        {
            Open(bag);
            Click("BagSlot_0");
            Click("EquipMore");
            Click("EquipOpenForge");
        }

        private void FinishCooldown()
        {
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            _forge.Tick();
        }

        private RectTransform ForgeRoot()
            => _fixture.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == "EquipForgeDialog");

        private Button Button(string name)
            => _fixture.GetComponentsInChildren<Button>(true).Where(button => button.gameObject.activeInHierarchy).Single(button => button.name == name);

        private TMP_Text Text(string name)
            => ForgeRoot().GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == name);

        private TMP_Text TooltipText(string name)
            => TooltipRoot().GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == name);

        private RectTransform TooltipRoot()
            => _fixture.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == "GameplayEquipmentDetails" && rect.gameObject.activeInHierarchy);

        private bool HasText(string value)
            => ForgeRoot().GetComponentsInChildren<TMP_Text>().Any(text => text.text == value);

        private void Click(string name)
        {
            var button = Button(name);
            Assert.That(button.interactable, Is.True, "按钮应可点击：" + name);
            button.onClick.Invoke();
        }

        private void Escape() => ForgeRoot().GetComponent<EquipWishDialogInput>().CancelRequested.Invoke();

        private void AssertPlainText()
            => Assert.That(ForgeRoot().GetComponentsInChildren<TMP_Text>().All(text => !text.richText), Is.True,
                "包括宝石选择与降级确认，服务器字符串都必须按纯文本显示");

        private static BagItemInfo Weapon(ulong id, uint level = 0, uint rate = 10000, uint failLoss = 0)
        {
            var item = new BagItemInfo
            {
                ItemId = id, ConfigId = 1101, Count = 1, MaxStack = 1, EquipKind = 11,
                Name = "青铜剑", IconKey = "sword_1101", EnhanceLevel = level, EnhanceMaxLevel = 12,
                EnhanceNext = new EquipEnhancePreview
                {
                    NextLevel = level + 1, SuccessRate = rate, CostGold = 1000, FailLevelLoss = failLoss,
                    MaterialConfigId = Stone, MaterialName = "强化石", MaterialCount = 1,
                },
            };
            item.BaseAttrs.Add(new EquipAttrLineInfo
            {
                AttrId = 1, Name = "伤害", Value = 40 + level * 2, EnhanceNextValue = 42 + level * 2,
            });
            item.GemSockets.Add(new EquipGemSocketInfo { Socket = 0, GemType = 1, Name = "攻击孔" });
            return item;
        }

        private static BagItemInfo WithGem(BagItemInfo weapon, uint socket)
        {
            var hole = weapon.GemSockets.Single(entry => entry.Socket == socket);
            hole.GemConfigId = AttackGem; hole.GemName = "攻击宝石·一级"; hole.GemIconKey = "gem_2101";
            hole.Attr = new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 30 };
            return weapon;
        }

        private static BagItemInfo Material(uint count)
            => new BagItemInfo { ItemId = 6001, ConfigId = Stone, Count = count, MaxStack = 999, Name = "强化石" };

        private static BagInfo StandardBag(ulong gold = 50000, uint stones = 5, uint attackGems = 2, BagItemInfo sword = null)
        {
            var items = new List<BagItemInfo> { sword ?? Weapon(Sword) };
            if (stones != 0) items.Add(Material(stones));
            if (attackGems != 0) items.Add(new BagItemInfo
            {
                ItemId = 6002, ConfigId = AttackGem, Count = attackGems, MaxStack = 999, GemType = 1,
                Name = "攻击宝石·一级", IconKey = "gem_2101",
                GemAttr = new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 30 },
            });
            return CharacterBag(gold, items.ToArray());
        }

        private static BagInfo CharacterBag(ulong gold, params BagItemInfo[] items)
        {
            var bag = new BagInfo
            {
                Layout = new BagLayoutInfo { BagType = 0, Capacity = 40, CanSort = true }, Currency = new CurrencyComp(),
            };
            bag.Currency.Values.Add(gold);
            uint slot = 0;
            foreach (var item in items)
            {
                bag.Items.Add(item);
                bag.Layout.Slots.Add(new BagSlotInfo { Slot = slot++, ItemId = item.ItemId, Width = 1, Height = 1 });
            }
            return bag;
        }

        private static BagInfo StandardEquipment()
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 7 } };
            bag.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 3, EquipKind = 11, Name = "武器" });
            bag.Items.Add(Weapon(WornSword, 3));
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = 3, ItemId = WornSword, Width = 1, Height = 1 });
            return bag;
        }
    }
}
