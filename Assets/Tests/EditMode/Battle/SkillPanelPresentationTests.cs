using System;
using System.Linq;
using MmorpgClient.UI.Ugui.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>Tests presentation actions only; no network, player data, or battle implementation.</summary>
    public sealed class SkillPanelPresentationTests
    {
        private GameObject _canvas;
        private SkillPanel _panel;
        private FixtureSource _source;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("SkillPanelTestCanvas", typeof(RectTransform), typeof(Canvas));
            _source = new FixtureSource { State = Ready() };
            _panel = new SkillPanel(_canvas.transform, () => _source.Refresh(_source.State.Context),
                (id, rank, count) => _source.Upgrade(_source.State.Context, id, rank, count));
            _source.Changed += Apply;
            Apply();
            _panel.Show();
        }

        [TearDown]
        public void TearDown()
        {
            if (_source != null) _source.Changed -= Apply;
            _panel?.Hide();
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
        }

        private void Apply() => _panel.Apply(_source.State);
        private Button Button(string name) => _canvas.GetComponentsInChildren<Button>(true).Single(item => item.name == name);
        private void Click(string name)
        {
            var button = Button(name);
            Assert.That(button.gameObject.activeInHierarchy && button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }

        [TestCase("UpgradePlate", 1u)]
        [TestCase("UpgradeTenPlate", 10u)]
        public void UpgradeForwardsDisplayedIdentityRankAndCountWithoutChangingSnapshot(string button, uint count)
        {
            var original = _source.State.Snapshot;
            Click(button);
            Assert.That(_source.UpgradeCalls, Is.EqualTo(1));
            Assert.That(_source.LastId, Is.EqualTo(1101));
            Assert.That(_source.LastRank, Is.EqualTo(3));
            Assert.That(_source.LastCount, Is.EqualTo(count));
            Assert.That(original.Find(1101).Rank, Is.EqualTo(3));
            Assert.That(original.CurrencyBalance, Is.EqualTo(265800));
        }

        [Test]
        public void BattleLockPreventsEvenDirectUpgradeButtonCallbacksWithoutErasingRealRank()
        {
            _panel.SetBattleLocked(true);
            Assert.That(Button("UpgradePlate").IsInteractable(), Is.False);
            Assert.That(Button("UpgradeTenPlate").IsInteractable(), Is.False);
            Button("UpgradePlate").onClick.Invoke();
            Assert.That(_source.UpgradeCalls, Is.Zero);
            Assert.That(_source.State.Snapshot.Find(1101).Rank, Is.EqualTo(3));
            Assert.That(_source.State.IsReadOnly, Is.False);
            Click("RefreshPlate");
            Assert.That(_source.RefreshCalls, Is.EqualTo(1));
            _panel.SetBattleLocked(false);
            Click("UpgradePlate");
            Assert.That(_source.UpgradeCalls, Is.EqualTo(1));
        }

        [Test]
        public void RefreshUsesTheCurrentlyDisplayedPetContext()
        {
            _source.State.Context = SkillBookContext.Pet(77, "灵宠");
            Apply();
            Click("RefreshPlate");
            Assert.That(_source.RefreshCalls, Is.EqualTo(1));
            Assert.That(_source.LastContext.IsPet, Is.True);
            Assert.That(_source.LastContext.PetId, Is.EqualTo(77));
        }

        [TestCase("loading")]
        [TestCase("busy")]
        [TestCase("error")]
        [TestCase("disconnected")]
        [TestCase("readonly")]
        [TestCase("empty")]
        [TestCase("insufficient")]
        [TestCase("maxrank")]
        public void UnavailableStatesPreventUpgradeInteraction(string state)
        {
            switch (state)
            {
                case "loading": _source.State.IsLoading = true; break;
                case "busy": _source.State.IsBusy = true; break;
                case "error": _source.State.Error = "同步失败"; break;
                case "disconnected": _source.State.IsConnected = false; break;
                case "readonly": _source.State.IsReadOnly = true; break;
                case "empty": _source.State.Snapshot.Entries.Clear(); break;
                case "insufficient": _source.State.Snapshot.CurrencyBalance = 0; break;
                case "maxrank": _source.State.Snapshot.Entries[0].Rank = 10; break;
            }
            Apply();
            foreach (var name in new[] { "UpgradePlate", "UpgradeTenPlate" })
            {
                var button = Button(name);
                Assert.That(button.gameObject.activeInHierarchy && button.IsInteractable(), Is.False, state + ": " + name);
            }
            Assert.That(_source.UpgradeCalls, Is.Zero);
        }

        [Test]
        public void ReturnedSnapshotChangesTheExpectedRankForTheNextClick()
        {
            var replacement = Ready();
            replacement.Snapshot.Entries[0].Rank = 6;
            _source.Replace(replacement);
            Click("UpgradePlate");
            Assert.That(_source.LastRank, Is.EqualTo(6));
        }

        // 存储重数可能高于本版本上限(scene 回滚窗口;服务端保留存储重数、按本版本上限生效,混版本持久字段保护设计 §12.2):
        // 列表按上限显示,不出现「12/10」;修习按钮不可用。
        [Test]
        public void StoredRankAboveTheCurrentMaxIsShownCappedAndCannotBeUpgraded()
        {
            _source.State.Snapshot.Entries[0].Rank = 12;
            Apply();
            var rankTexts = _canvas.GetComponentsInChildren<TMPro.TMP_Text>(true)
                .Where(text => text.name == "Rank").Select(text => text.text).ToList();
            Assert.That(rankTexts, Has.Member("10/10"));
            Assert.That(rankTexts, Has.None.EqualTo("12/10"));
            foreach (var name in new[] { "UpgradePlate", "UpgradeTenPlate" })
                Assert.That(Button(name).IsInteractable(), Is.False, name);
        }

        [Test]
        public void ReusedStateWithNewPetDoesNotKeepThePreviousEmptyFilter()
        {
            Click("Filter2Plate");
            Assert.That(Button("SkillRow0Plate").gameObject.activeInHierarchy, Is.False);
            _source.State.Context = SkillBookContext.Pet(77, "新选中的宝宝");
            Apply();
            Assert.That(Button("SkillRow0Plate").gameObject.activeInHierarchy, Is.True);
            Click("UpgradePlate");
            Assert.That(_source.LastContext.PetId, Is.EqualTo(77));
        }

        [Test]
        public void FilterChangingSelectedSkillStartsItsDescriptionAtTheTop()
        {
            var scroll = ScrollLongDescriptionToBottom();
            Click("Filter2Plate");

            Assert.That(SelectedName(), Is.EqualTo("被动长说明"));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0f).Within(.01f));
            Assert.That(scroll.velocity.sqrMagnitude, Is.EqualTo(0f).Within(.01f));
        }

        [Test]
        public void SnapshotRemovingSelectedSkillStartsReplacementDescriptionAtTheTop()
        {
            var scroll = ScrollLongDescriptionToBottom();
            var replacement = Ready();
            replacement.Snapshot.Entries.Clear();
            replacement.Snapshot.Entries.Add(_source.State.Snapshot.Entries[1]);
            _source.Replace(replacement);

            Assert.That(SelectedName(), Is.EqualTo("被动长说明"));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0f).Within(.01f));
            Assert.That(scroll.velocity.sqrMagnitude, Is.EqualTo(0f).Within(.01f));
        }

        [Test]
        public void SnapshotKeepingSelectedSkillPreservesDescriptionReadingPosition()
        {
            var scroll = ScrollLongDescriptionToBottom();
            float readingPosition = scroll.content.anchoredPosition.y;
            _source.State.Snapshot.Entries[0].Rank++;
            Apply();

            Assert.That(SelectedName(), Is.EqualTo("主动长说明"));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(readingPosition).Within(.01f));
        }

        private string SelectedName() => _canvas.GetComponentsInChildren<TMPro.TMP_Text>(true)
            .Single(item => item.name == "SelectedName").text;

        private ScrollRect ScrollLongDescriptionToBottom()
        {
            var active = _source.State.Snapshot.Entries[0];
            active.Definition.name = "主动长说明";
            active.Definition.description = string.Join("\n", Enumerable.Repeat("主动技能的长说明。", 20));
            _source.State.Snapshot.Entries.Add(new SkillEntryState
            {
                Id = 1102, Rank = 1, RankLimit = 10, NextRankCost = 1600,
                Definition = new SkillDefinition { id = 1102, classId = 1, name = "被动长说明", type = "passive",
                    maxRank = 10, unlockLevel = 1, targetTeam = "self",
                    description = string.Join("\n", Enumerable.Repeat("被动技能的长说明。", 20)) },
            });
            Apply();
            Canvas.ForceUpdateCanvases();
            var scroll = _canvas.GetComponentsInChildren<ScrollRect>(true)
                .Single(item => item.name == "DescriptionViewport");
            scroll.verticalNormalizedPosition = 0f;
            scroll.velocity = Vector2.up * 120f;
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0f), "The initial description must be scrolled.");
            return scroll;
        }

        [Test]
        public void ClosingPanelHidesTheActualWindow()
        {
            Assert.That(_panel.IsVisible, Is.True);
            _panel.Hide();
            Assert.That(_panel.IsVisible, Is.False);
        }

        [Test]
        public void PetContextIdentityDoesNotDependOnDisplayName()
        {
            Assert.That(SkillBookContext.Pet(11, "旧名").Matches(SkillBookContext.Pet(11, "新名")), Is.True);
            Assert.That(SkillBookContext.Pet(11, "同名").Matches(SkillBookContext.Pet(12, "同名")), Is.False);
            Assert.That(SkillBookContext.Character().Matches(SkillBookContext.Pet(11, "角色技能")), Is.False);
        }

        [Test]
        public void LoadoutTabHiddenForPetAndReplacesExclusiveGroupBeforeSaving()
        {
            var loadoutSource = new FixtureLoadoutSource();
            var canvas = new GameObject("LoadoutTestCanvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var state = Ready();
                state.Snapshot.Loadout = new SkillLoadoutView { CustomEnabled = true, ActiveSchemeId = 1, StateVersion = 2 };
                var scheme1 = new SkillLoadoutSchemeView { SchemeId = 1, RoleTag = 1 };
                scheme1.PassiveSkillIds.Add(12101u);
                state.Snapshot.Loadout.Schemes.Add(scheme1);
                state.Snapshot.Loadout.Schemes.Add(new SkillLoadoutSchemeView { SchemeId = 2, RoleTag = 2 });
                state.Snapshot.Loadout.Schemes.Add(new SkillLoadoutSchemeView { SchemeId = 3, RoleTag = 3 });
                state.Snapshot.Entries.Add(new SkillEntryState
                {
                    Id = 12101u, Rank = 1, RankLimit = 10, Equipped = true,
                    Definition = new SkillDefinition { id = 12101u, name = "兵锋专精", type = "passive", abilityKind = 2u, exclusiveGroup = 1u },
                });
                state.Snapshot.Entries.Add(new SkillEntryState
                {
                    Id = 12102u, Rank = 1, RankLimit = 10,
                    Definition = new SkillDefinition { id = 12102u, name = "玄法专精", type = "passive", abilityKind = 2u, exclusiveGroup = 1u },
                });

                var panel = new SkillPanel(canvas.transform, () => { }, (_, _, _) => { },
                    getLoadoutSource: () => loadoutSource);
                panel.Apply(state);
                panel.Show();

                var loadoutTab = canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutTabPlate");
                Assert.That(loadoutTab.gameObject.activeInHierarchy, Is.True, "角色技能书应展示战法装配页签");
                loadoutTab.onClick.Invoke();

                // 点击互斥组战法 12102 自动替换 12101 并保存提交
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutPool_12102").onClick.Invoke();
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutSaveBtnPlate").onClick.Invoke();
                Assert.That(loadoutSource.SaveCalls, Is.EqualTo(1));
                Assert.That(loadoutSource.LastSavedPassive, Is.EqualTo(new[] { 12102u }));

                // 战斗中锁定写入
                panel.SetBattleLocked(true);
                Assert.That(canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutSaveBtnPlate").IsInteractable(), Is.False);
                panel.SetBattleLocked(false);

                // 切宠物上下文自动隐藏战法装配页签
                state.Context = SkillBookContext.Pet(88, "小狐");
                panel.Apply(state);
                Assert.That(loadoutTab.gameObject.activeInHierarchy, Is.False, "宝宝技能页不得展示人物战法装配页签");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        [Test]
        public void LoadoutPanelSupportsSevenSchemesTemplatesResetConfirmationAndRentModal()
        {
            var loadoutSource = new FixtureLoadoutSource();
            var canvas = new GameObject("LoadoutSevenSlotCanvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var state = Ready();
                var loadout = new SkillLoadoutView
                {
                    CustomEnabled = true,
                    ActiveSchemeId = 1,
                    StateVersion = 5,
                    UnlockedSchemes = 2,
                    MaxSchemes = 7,
                    FreeSchemes = 2,
                    LightCardBonusSchemes = 0,
                    RentedExtraSlots = 0,
                    ServerNowSec = 100000UL,
                    DiamondBalance = 120UL,
                    AttributeResetCostGold = 5000UL,
                };
                var s1 = new SkillLoadoutSchemeView { SchemeId = 1, Name = "首发", RoleTag = 1, AttributeTotalPoints = 50, AttributeRemaining = 0 };
                s1.AttributeAllocated[SkillBuildTemplates.DimCon] = 50;
                loadout.Schemes.Add(s1);
                var s5 = new SkillLoadoutSchemeView { SchemeId = 5, Name = "旧封系", RoleTag = 2, AttributeTotalPoints = 50, AttributeRemaining = 0 };
                s5.AttributeAllocated[SkillBuildTemplates.DimAgi] = 50;
                loadout.Schemes.Add(s5);
                state.Snapshot.Loadout = loadout;

                var panel = new SkillPanel(canvas.transform, () => { }, (_, _, _) => { },
                    getLoadoutSource: () => loadoutSource);
                panel.Apply(state);
                panel.Show();

                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutTabPlate").onClick.Invoke();

                // 7 个方案槽位页签 + 5 个用途标签全部就绪
                for (uint i = 1u; i <= 7u; i++)
                    Assert.That(canvas.GetComponentsInChildren<Button>(true).Any(b => b.name == $"LoadoutScheme{i}Plate"), Is.True, $"Scheme {i}");
                for (uint r = 0u; r <= 4u; r++)
                    Assert.That(canvas.GetComponentsInChildren<Button>(true).Any(b => b.name == $"LoadoutRoleTag{r}Plate"), Is.True, $"RoleTag {r}");

                // 套用 4力1体 模板会减少原本全体的体质点数 -> 未勾选洗点确认前拦截保存
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutTemplate0Plate").onClick.Invoke();
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutSaveBtnPlate").onClick.Invoke();
                Assert.That(loadoutSource.SaveCalls, Is.Zero, "下调加点未勾选洗点确认前不得提交");

                // 勾选确认洗点后保存成功并携带一体化加点参数
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutResetAttrTogglePlate").onClick.Invoke();
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutSaveBtnPlate").onClick.Invoke();
                Assert.That(loadoutSource.SaveCalls, Is.EqualTo(1));
                Assert.That(loadoutSource.LastSaveArgs.UpdateAttributeAllocation, Is.True);
                Assert.That(loadoutSource.LastSaveArgs.ResetAttributeIfNeeded, Is.True);
                Assert.That(loadoutSource.LastSaveArgs.AttributeAllocated[SkillBuildTemplates.DimStr], Is.EqualTo(40u));
                Assert.That(loadoutSource.LastSaveArgs.AttributeAllocated[SkillBuildTemplates.DimCon], Is.EqualTo(10u));

                // 打开租用弹窗选择 +2 套、+30 天（60 灵钻）并确认提交
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutRentBtnPlate").onClick.Invoke();
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutRentTarget2Plate").onClick.Invoke();
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutRentPeriod1Plate").onClick.Invoke();
                canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "LoadoutRentConfirmBtnPlate").onClick.Invoke();
                Assert.That(loadoutSource.RentCalls, Is.EqualTo(1));
                Assert.That(loadoutSource.LastRentTargetSlots, Is.EqualTo(2u));
                Assert.That(loadoutSource.LastRentAddPeriods, Is.EqualTo(1u));
                Assert.That(loadoutSource.LastRentExpectedCost, Is.EqualTo(60UL));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        [Test]
        public void SkillBuildLoadoutV1SpritesLoadWithExpectedDimensionsAndSliceBorders()
        {
            var expected = new (string key, int w, int h, bool sliced)[]
            {
                (SkillLoadoutRentPlanner.ArtRentModalFrame, 1932, 814, false),
                (SkillLoadoutRentPlanner.ArtTabActive, 176, 56, true),
                (SkillLoadoutRentPlanner.ArtTabUnlocked, 176, 56, true),
                (SkillLoadoutRentPlanner.ArtTabRented, 176, 56, true),
                (SkillLoadoutRentPlanner.ArtTabLocked, 176, 56, true),
                (SkillLoadoutRentPlanner.ArtAttrBox, 640, 320, true),
                (SkillLoadoutRentPlanner.ArtTemplatePill, 148, 44, true),
                (SkillLoadoutRentPlanner.ArtSlotPassIcon, 512, 512, false),
            };
            foreach (var item in expected)
            {
                var sprite = Resources.Load<Sprite>(SkillLoadoutRentPlanner.ArtBasePath + item.key);
                Assert.That(sprite, Is.Not.Null, "缺失切图资源: " + item.key);
                Assert.That((int)sprite.rect.width, Is.EqualTo(item.w), item.key + " width");
                Assert.That((int)sprite.rect.height, Is.EqualTo(item.h), item.key + " height");
                if (item.sliced)
                    Assert.That(sprite.border.sqrMagnitude, Is.GreaterThan(0f), item.key + " 必须配置九宫格保护边距");
            }
        }

        private sealed class FixtureLoadoutSource : ISkillLoadoutSource
        {
            public int SaveCalls, SwitchCalls, RentCalls;
            public uint[] LastSavedPassive = Array.Empty<uint>();
            public SkillLoadoutSaveArgs LastSaveArgs;
            public uint LastRentTargetSlots, LastRentAddPeriods;
            public ulong LastRentExpectedCost;

            public void SaveLoadout(uint schemeId, uint roleTag, System.Collections.Generic.IReadOnlyList<uint> passiveSkillIds,
                System.Collections.Generic.IReadOnlyList<uint> pursuitSkillIds, System.Collections.Generic.IReadOnlyList<uint> openingSkillIds,
                Action<bool, string> completed = null)
            {
                SaveLoadout(new SkillLoadoutSaveArgs
                {
                    SchemeId = schemeId,
                    RoleTag = roleTag,
                    PassiveSkillIds = passiveSkillIds,
                    PursuitSkillIds = pursuitSkillIds,
                    OpeningSkillIds = openingSkillIds,
                }, completed);
            }

            public void SaveLoadout(SkillLoadoutSaveArgs args, Action<bool, string> completed = null)
            {
                SaveCalls++;
                LastSaveArgs = args;
                LastSavedPassive = args?.PassiveSkillIds != null ? args.PassiveSkillIds.ToArray() : Array.Empty<uint>();
                completed?.Invoke(true, "已保存");
            }

            public void SwitchLoadout(uint schemeId, Action<bool, string> completed = null)
            {
                SwitchLoadout(schemeId, false, completed);
            }

            public void SwitchLoadout(uint schemeId, bool skipAttributeSync, Action<bool, string> completed = null)
            {
                SwitchCalls++;
                completed?.Invoke(true, "已切换");
            }

            public void RentLoadoutSlots(uint targetRentedSlots, uint addPeriods, ulong expectedCostDiamond,
                Action<bool, string> completed = null)
            {
                RentCalls++;
                LastRentTargetSlots = targetRentedSlots;
                LastRentAddPeriods = addPeriods;
                LastRentExpectedCost = expectedCostDiamond;
                completed?.Invoke(true, "已租用");
            }
        }

        private static SkillBookViewState Ready()
        {
            var snapshot = new SkillBookSnapshot { ClassId = 1, PlayerLevel = 68, CurrencyBalance = 265800 };
            snapshot.Entries.Add(new SkillEntryState
            {
                Id = 1101, Rank = 3, RankLimit = 10, NextRankCost = 1600,
                Definition = new SkillDefinition { id = 1101, classId = 1, name = "测试技能", type = "active",
                    maxRank = 10, unlockLevel = 1, targetTeam = "enemy", maxTargets = 1, description = "界面接口测试。" },
            });
            return new SkillBookViewState { Context = SkillBookContext.Character(), Snapshot = snapshot, IsConnected = true };
        }

        private sealed class FixtureSource : ISkillBookSource
        {
            public event Action Changed;
            public SkillBookViewState State { get; set; }
            public int UpgradeCalls, RefreshCalls;
            public uint LastId, LastRank, LastCount;
            public SkillBookContext LastContext;
            public void Refresh(SkillBookContext context) { RefreshCalls++; LastContext = context; }
            public void Upgrade(SkillBookContext context, uint skillId, uint expectedRank, uint count)
            { UpgradeCalls++; LastContext = context; LastId = skillId; LastRank = expectedRank; LastCount = count; }
            public void Replace(SkillBookViewState state) { State = state; Changed?.Invoke(); }
        }
    }
}
