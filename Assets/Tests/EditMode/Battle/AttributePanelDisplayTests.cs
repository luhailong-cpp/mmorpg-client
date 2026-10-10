using System.Linq;
using MmorpgClient.UI.Ugui.Attribute;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 属性窗的装备相关展示(服务端 docs/design/equipment-attributes.md §3.2 / §6):
    /// 一级属性的「值(+装备加成)」与「战斗」页签。只喂面板数据看正式控件,宿主没有客户端,不发请求。
    /// </summary>
    public sealed class AttributePanelDisplayTests
    {
        private GameObject _uiRoot;
        private AttributePanel _panel;

        [SetUp]
        public void SetUp()
        {
            _uiRoot = new GameObject("AttributePanelDisplayTests", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_uiRoot.transform).sizeDelta = new Vector2(2560f, 1080f);

            // 非激活宿主不会执行单例 Awake/Update,也没有 AttributeClient:面板只读它的 Client(null)。
            var ownerGo = new GameObject("AttributeOwner");
            ownerGo.SetActive(false);
            ownerGo.transform.SetParent(_uiRoot.transform, false);
            var owner = ownerGo.AddComponent<AttributeUiRoot>();
            owner.enabled = false;
            _panel = new AttributePanel(owner, _uiRoot.transform);
            // 面板构造完是收起的;这里只看控件状态,不走 Show()(它会去关别的窗、拉面板)。
            Find<RectTransform>("AttributeWindow").gameObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_uiRoot != null) Object.DestroyImmediate(_uiRoot);
            _panel = null;
        }

        // ── 一级属性的装备加成 ──────────────────────────────

        [Test]
        public void PrimaryRowShowsBonusInParenthesesOnlyWhenPositive()
        {
            _panel.ApplyPanel(MakePanel());

            Assert.That(Find<TMP_Text>("Row0Value").text, Is.EqualTo("170(+15)"), "加成已含在 170 里,括号只标注来源");
            Assert.That(Find<TMP_Text>("Row1Value").text, Is.EqualTo("135"), "没有外部加成不显示括号");
        }

        [Test]
        public void PendingPointsKeepBonusAnnotationAndServerRefreshUpdatesIt()
        {
            _panel.ApplyPanel(MakePanel());

            Find<Button>("Row0PlusPlate").onClick.Invoke();
            Assert.That(Find<TMP_Text>("Row0Value").text, Is.EqualTo("170<color=#7FD98A>+1</color>(+15)"));
            Find<Button>("Row1PlusPlate").onClick.Invoke();
            Assert.That(Find<TMP_Text>("Row1Value").text, Is.EqualTo("135<color=#7FD98A>+1</color>"));

            // 卸下装备后服务器推来新面板:加成归零,括号消失,未确认的增量按既有规则作废。
            var plain = MakePanel();
            plain.Dimensions[0].Value = 155;
            plain.Dimensions[0].Bonus = 0;
            _panel.ApplyPanel(plain);
            Assert.That(Find<TMP_Text>("Row0Value").text, Is.EqualTo("155"));
        }

        // ── 战斗属性页签 ────────────────────────────────────

        [Test]
        public void CombatAttributesAppearOnTheirOwnTabInServerSortOrder()
        {
            var panel = MakePanel();
            // 故意乱序:显示顺序只看 sort,不看下标。
            panel.Combat.Add(new CombatAttributeInfo { CombatId = 15, Name = "抗物理", Value = 4, Percent = true, Sort = 260 });
            panel.Combat.Add(new CombatAttributeInfo { CombatId = 1, Name = "物理必杀率", Value = 8, Percent = true, Sort = 120 });
            panel.Combat.Add(new CombatAttributeInfo { CombatId = 6, Name = "所有技能上升", Value = 5, Sort = 170 });
            panel.Combat.Add(new CombatAttributeInfo { CombatId = 3, Name = "", Value = 0, Percent = true, Sort = 140 });
            _panel.ApplyPanel(panel);

            Assert.That(Find<Button>("CombatTabPlate").gameObject.activeSelf, Is.True, "服务器下发了战斗属性才有这个页签");
            Assert.That(Find<RectTransform>("CombatPage").gameObject.activeSelf, Is.False, "默认仍停在加点页");
            Assert.That(CombatTexts("CombatName_").Select(text => text.text),
                Is.EqualTo(new[] { "物理必杀率", "战斗属性 · 3", "所有技能上升", "抗物理" }));
            Assert.That(CombatTexts("CombatValue_").Select(text => text.text), Is.EqualTo(new[] { "8%", "0%", "5", "4%" }));
            Assert.That(CombatTexts("Combat").All(text => !text.richText), Is.True, "服务器下发的名称按纯文本显示");

            Find<Button>("CombatTabPlate").onClick.Invoke();
            Assert.That(Find<RectTransform>("CombatPage").gameObject.activeSelf, Is.True);
            Assert.That(Find<RectTransform>("AllocationPage").gameObject.activeSelf, Is.False);
            Assert.That(Find<RectTransform>("OverviewPage").gameObject.activeSelf, Is.False);

            Find<Button>("AllocationTabPlate").onClick.Invoke();
            Assert.That(Find<RectTransform>("CombatPage").gameObject.activeSelf, Is.False);
            Assert.That(Find<RectTransform>("AllocationPage").gameObject.activeSelf, Is.True);
        }

        [Test]
        public void EmptyCombatListHidesTheTabAndLeavesNoRowsBehind()
        {
            // 老服务器不下发 combat:页签不出现,原有三页照旧。
            _panel.ApplyPanel(MakePanel());
            Assert.That(Find<Button>("CombatTabPlate").gameObject.activeSelf, Is.False);
            Assert.That(Find<RectTransform>("CombatPage").gameObject.activeSelf, Is.False);
            Assert.That(CombatTexts("Combat"), Is.Empty);
            Assert.That(Find<RectTransform>("AllocationPage").gameObject.activeSelf, Is.True);

            var withCombat = MakePanel();
            withCombat.Combat.Add(new CombatAttributeInfo { CombatId = 1, Name = "物理必杀率", Value = 8, Percent = true, Sort = 120 });
            _panel.ApplyPanel(withCombat);
            Find<Button>("CombatTabPlate").onClick.Invoke();
            Assert.That(CombatTexts("CombatName_").Length, Is.EqualTo(1));

            // 正看着战斗页时列表变空:页签收起,退回加点页,旧行不残留。
            _panel.ApplyPanel(MakePanel());
            Assert.That(Find<Button>("CombatTabPlate").gameObject.activeSelf, Is.False);
            Assert.That(Find<RectTransform>("CombatPage").gameObject.activeSelf, Is.False);
            Assert.That(Find<RectTransform>("AllocationPage").gameObject.activeSelf, Is.True);
            Assert.That(CombatTexts("Combat"), Is.Empty);
        }

        [Test]
        public void LongCombatListKeepsAllRowsInScrollableContentAndLiteralNames()
        {
            var panel = MakePanel();
            for (uint i = 0; i < 24; i++)
                panel.Combat.Add(new CombatAttributeInfo
                {
                    CombatId = i + 1, Name = $"<b>属性{i}</b>", Value = i, Sort = 1,
                });
            _panel.ApplyPanel(panel);
            Find<Button>("CombatTabPlate").onClick.Invoke();
            var scroll = Find<ScrollRect>("CombatViewport");
            Assert.That(CombatTexts("CombatName_").Length, Is.EqualTo(24));
            Assert.That(CombatTexts("CombatName_").Select(text => text.text),
                Is.EqualTo(Enumerable.Range(0, 24).Select(i => $"<b>属性{i}</b>")));
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(CombatTexts("CombatName_").All(text => !text.richText), Is.True);
        }

        [Test]
        public void ResetSessionRemovesPreviousEquipmentBonusCombatRowsAndPendingPoints()
        {
            var panel = MakePanel();
            panel.Combat.Add(new CombatAttributeInfo { CombatId = 1, Name = "物理必杀率", Value = 8, Percent = true });
            _panel.ApplyPanel(panel);
            Find<Button>("Row0PlusPlate").onClick.Invoke();
            Find<Button>("CombatTabPlate").onClick.Invoke();

            _panel.ResetSession();

            Assert.That(_panel.IsVisible, Is.False);
            Assert.That(Find<TMP_Text>("Row0Value").text, Is.EqualTo("0"));
            Assert.That(Find<Button>("Row0PlusPlate").interactable, Is.False);
            Assert.That(Find<TMP_Text>("Stat0Value").text, Is.EqualTo("—"));
            Assert.That(Find<Button>("CombatTabPlate").gameObject.activeSelf, Is.False);
            Assert.That(CombatTexts("Combat"), Is.Empty);
            Assert.That(Find<Button>("ConfirmPlate").interactable, Is.False);

            var next = MakePanel();
            next.Dimensions[0].Bonus = 0;
            next.Dimensions[0].Value = 155;
            _panel.ApplyPanel(next);
            Assert.That(Find<TMP_Text>("Row0Value").text, Is.EqualTo("155"));
            Assert.That(Find<RectTransform>("AllocationPage").gameObject.activeSelf, Is.True);
        }

        [Test]
        public void EquipmentEntryRemainsAvailableAcrossAllCharacterPages()
        {
            var panel = MakePanel();
            panel.Combat.Add(new CombatAttributeInfo { CombatId = 1, Name = "物理必杀率", Value = 8 });
            _panel.ApplyPanel(panel);
            var equipment = Find<Button>("EquipmentEntryPlate");
            foreach (var tab in new[] { "AttributeTabPlate", "AllocationTabPlate", "CombatTabPlate" })
            {
                Find<Button>(tab).onClick.Invoke();
                Assert.That(equipment.gameObject.activeInHierarchy, Is.True);
                Assert.That(equipment.interactable, Is.True);
            }
        }

        [Test]
        public void BackClosesSchemePickerBeforeClosingTheCharacterWindow()
        {
            _panel.ApplyPanel(MakePanel());
            Find<Button>("SchemeButtonPlate").onClick.Invoke();
            Assert.That(Find<RectTransform>("SchemeDropdown").gameObject.activeSelf, Is.True);

            _panel.Back();

            Assert.That(_panel.IsVisible, Is.True);
            Assert.That(Find<RectTransform>("SchemeDropdown").gameObject.activeSelf, Is.False);
            Assert.That(Find<RectTransform>("AttributeWindow")
                .GetComponent<MmorpgClient.World.Tianyong.GameplayInputBlocker>().isActiveAndEnabled, Is.True);

            _panel.Back();
            Assert.That(_panel.IsVisible, Is.False);
            Assert.That(Find<RectTransform>("AttributeWindow")
                .GetComponent<MmorpgClient.World.Tianyong.GameplayInputBlocker>().isActiveAndEnabled, Is.False);
        }

        // ── 夹具与查询 ──────────────────────────────────────

        /// <summary>体质 170(加点 60,其中 15 点来自装备)、灵力 135(无外部加成);属性点池还剩 10 点。</summary>
        private static AttributePanelInfo MakePanel()
        {
            var panel = new AttributePanelInfo { ActiveSchemeId = 1, MaxSchemes = 3, Level = 80 };
            panel.Schemes.Add(new AttributeSchemeInfo { SchemeId = 1, Name = "方案一" });
            panel.Pools.Add(new AttributePoolInfo
            {
                PoolId = 1, Name = "属性点", Total = 320, Remaining = 10, Unlocked = true, DimensionCap = 300,
            });
            panel.Dimensions.Add(new AttributeDimensionInfo
            {
                DimensionId = 101, PoolId = 1, Name = "体质", Allocated = 60, Value = 170, Cap = 300, Sort = 0, Bonus = 15,
            });
            panel.Dimensions.Add(new AttributeDimensionInfo
            {
                DimensionId = 102, PoolId = 1, Name = "灵力", Allocated = 60, Value = 135, Cap = 300, Sort = 1,
            });
            panel.Derived = new DerivedAttributeInfo
            {
                Health = 5800, MaxHealth = 5800, Mana = 2300, MaxMana = 2300,
                PhysicalAttack = 1380, MagicAttack = 2180, Speed = 860, Defense = 960,
            };
            return panel;
        }

        private T Find<T>(string name) where T : Component
            => _uiRoot.GetComponentsInChildren<T>(true).Single(item => item.name == name);

        /// <summary>战斗页里名字以 prefix 开头的文本,按显示顺序(含未激活页里的)。</summary>
        private TMP_Text[] CombatTexts(string prefix)
            => Find<RectTransform>("CombatRows").GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text.name.StartsWith(prefix)).ToArray();
    }
}
