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
