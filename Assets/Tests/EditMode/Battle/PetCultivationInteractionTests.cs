using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Pet;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.UI.Ugui.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class PetCultivationInteractionTests
    {
        private GameObject _root;
        private FakeBattleTransport _net;
        private PetClient _client;
        private PetCultivationPanel _panel;
        private SkillSource _skills;
        private PetListInfo _list;
        private bool _inBattle;

        private sealed class SkillSource : ISkillBookSource
        {
            public event Action Changed;
            public SkillBookViewState State { get; private set; }
            public readonly List<SkillBookContext> Reads = new();
            public readonly List<(SkillBookContext context, uint id, uint rank, uint count)> Writes = new();
            public void Refresh(SkillBookContext context) { Reads.Add(context); Publish(new SkillBookViewState { Context = context, IsLoading = true, IsConnected = true }); }
            public void Upgrade(SkillBookContext context, uint id, uint rank, uint count)
            {
                Writes.Add((context, id, rank, count));
                Publish(new SkillBookViewState { Context = context, Snapshot = State.Snapshot, IsBusy = true, IsConnected = true });
            }
            public void Publish(SkillBookViewState state) { State = state; Changed?.Invoke(); }
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PetCultivationTest", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_root.transform).sizeDelta = new Vector2(2560, 1080);
            _net = new FakeBattleTransport(); _client = new PetClient(_net, () => _inBattle); _skills = new SkillSource();
            _inBattle = false;
            _panel = new PetCultivationPanel(_root.transform, () => _client, () => _skills, () => _inBattle);
            _panel.Show(); _list = MakeList();
            _net.CallsOf(MessageIds.GetPetList).Single().Respond(new GetPetListResponse { Pets = _list });
            _net.Calls.Clear();
        }

        [TearDown]
        public void TearDown() { _panel?.Dispose(); if (_root != null) UnityEngine.Object.DestroyImmediate(_root); }

        [Test]
        public void EntryPointsHaveNoInventedBudgetAndCannotSendAllocation()
        {
            Assert.That(Text("EntryPointsBudget").text, Is.EqualTo("总点数 —       已用 —       剩余 —"));
            Assert.That(Button("EntrySave").interactable, Is.False);
            Assert.That(Button("EntryPlus0").interactable, Is.False);
            Button("EntrySave").onClick.Invoke(); Button("EntryPlus0").onClick.Invoke();
            Click("EntryFilter3");
            Assert.That(Text("EntryCurrent4").text, Is.EqualTo("—"));
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.Pets.Pets[0].RemainingPoints, Is.EqualTo(3));
        }

        [Test]
        public void FourStatsShareOnlyServerAttributeBudgetAndCannotDecreaseCommittedAllocation()
        {
            Click("Tab加点");
            Assert.That(Text("FourStatCurrent0").text, Is.EqualTo("2"), "显示投入，不把含成长的 Value 当成已分配点数");
            Assert.That(Button("Row0Minus").interactable, Is.False);
            Click("Row0Plus");
            Assert.That(Button("Row0Plus").interactable, Is.False, "第一维已到服务器 cap 3");
            Click("Row1Plus"); Click("Row1Plus");
            Assert.That(Text("FourStatsRemaining").text, Is.EqualTo("剩余属性点 0"));
            Assert.That(Button("Row2Plus").interactable, Is.False);
            Assert.That(Text("FourStatAfter0").text, Is.EqualTo("3"));
            Click("Row0Minus");
            Assert.That(Text("FourStatAfter0").text, Is.EqualTo("2"));
            Assert.That(Button("Row0Minus").interactable, Is.False);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void SaveUsesPetInstanceAndTargetAllocationAndWaitsForAuthority()
        {
            Click("Tab加点"); Click("Row0Plus"); Click("Confirm");
            var call = _net.CallsOf(MessageIds.AllocatePetPoints).Single();
            var request = (AllocatePetPointsRequest)call.Request;
            Assert.That(request.PetId, Is.EqualTo(900001));
            Assert.That(request.Allocated.Count, Is.EqualTo(1)); Assert.That(request.Allocated[401], Is.EqualTo(3));
            Assert.That(_client.Pets.Pets[0].Dimensions[0].Allocated, Is.EqualTo(2));
            Assert.That(Button("Confirm").interactable, Is.False);
            Button("Confirm").onClick.Invoke(); Assert.That(_net.Calls, Has.Count.EqualTo(1));
            var response = _list.Clone(); response.Pets[0].Dimensions[0].Allocated = 3; response.Pets[0].RemainingPoints = 2;
            call.Respond(new AllocatePetPointsResponse { Pets = response });
            Assert.That(_panel.HasDraft, Is.False);
            Assert.That(Text("FourStatCurrent0").text, Is.EqualTo("3"));
            Assert.That(Text("FourStatsRemaining").text, Is.EqualTo("剩余属性点 2"));
        }

        [Test]
        public void RejectedSaveKeepsDraftAndRestoreNeverSendsWrite()
        {
            Click("Tab加点"); Click("Row1Plus"); Click("Confirm");
            _net.CallsOf(MessageIds.AllocatePetPoints).Single().Respond(new AllocatePetPointsResponse { ErrorMessage = new TipInfoMessage { Id = 26009 } });
            Assert.That(_panel.HasDraft, Is.True); Assert.That(Button("Confirm").interactable, Is.True);
            Assert.That(Text("PetCultivationStatus").text, Does.Contain("不足"));
            int calls = _net.Calls.Count; Click("Discard");
            Assert.That(_net.Calls.Count, Is.EqualTo(calls)); Assert.That(_panel.HasDraft, Is.False);
            Assert.That(Text("FourStatIncrement1").text, Is.EqualTo("0"));
        }

        [Test]
        public void UnrelatedListsCannotAcknowledgeAnAllocationAndRejectionLeavesItRetryable()
        {
            Click("Tab加点"); Click("Row1Plus"); Click("Confirm");
            var write = _net.CallsOf(MessageIds.AllocatePetPoints).Single();
            _net.PushNotify(MessageIds.NotifyPetListChanged, new PetListChangedS2C { Pets = _list });
            Assert.That(_panel.HasDraft, Is.True, "无关推送不是本次加点的成功回包");
            Assert.That(Text("PetCultivationStatus").text, Does.Not.Contain("已保存"));
            _client.RequestList();
            _net.CallsOf(MessageIds.GetPetList).Single().Respond(new GetPetListResponse { Pets = _list });
            Assert.That(_panel.HasDraft, Is.True, "并发拉取的旧快照不能清除草稿");
            Assert.That(Button("Confirm").interactable, Is.False);
            write.Respond(new AllocatePetPointsResponse { ErrorMessage = new TipInfoMessage { Id = 26009 } });
            Assert.That(_panel.HasDraft, Is.True); Assert.That(Button("Confirm").interactable, Is.True);
            Click("Confirm");
            var accepted = _list.Clone(); accepted.Pets[0].Dimensions[1].Allocated = 3; accepted.Pets[0].RemainingPoints = 2;
            _net.CallsOf(MessageIds.AllocatePetPoints).Last().Respond(new AllocatePetPointsResponse { Pets = accepted });
            Assert.That(_panel.HasDraft, Is.False); Assert.That(Text("FourStatCurrent1").text, Is.EqualTo("3"));
        }

        [Test]
        public void SuccessfulAllocationResponseMustReachTheRequestedPetAndDimension()
        {
            Click("Tab加点"); Click("Row1Plus"); Click("Confirm");
            var wrongPet = _list.Clone(); wrongPet.Pets[1].Dimensions[1].Allocated = 3;
            _net.CallsOf(MessageIds.AllocatePetPoints).Single().Respond(new AllocatePetPointsResponse { Pets = wrongPet });
            Assert.That(_panel.HasDraft, Is.True, "另一只宝宝达到目标不能确认本次保存");
            Assert.That(Button("Confirm").interactable, Is.True);
            Assert.That(Text("PetCultivationStatus").text, Does.Not.Contain("加点已保存"));
            Click("Confirm");
            var accepted = _list.Clone(); accepted.Pets[0].Dimensions[1].Allocated = 3; accepted.Pets[0].RemainingPoints = 2;
            _net.CallsOf(MessageIds.AllocatePetPoints).Last().Respond(new AllocatePetPointsResponse { Pets = accepted });
            Assert.That(_panel.HasDraft, Is.False);
        }

        [Test]
        public void LateAllocationResponseCannotRestoreAuthorityAfterDisconnect()
        {
            Click("Tab加点"); Click("Row1Plus"); Click("Confirm");
            var write = _net.CallsOf(MessageIds.AllocatePetPoints).Single();
            _net.IsReady = false; _net.RaiseDisconnected(); _panel.Tick();
            var accepted = _list.Clone(); accepted.Pets[0].Dimensions[1].Allocated = 3;
            write.Respond(new AllocatePetPointsResponse { Pets = accepted });
            Assert.That(_client.HasList, Is.False, "断线前的迟到写回包必须失效");
            Assert.That(Text("FourStatsRemaining").text, Is.EqualTo("剩余属性点 —"));
            Assert.That(Button("Confirm").interactable, Is.False);
        }

        [Test]
        public void LeavingOrSwitchingPetRequiresExplicitDraftDecision()
        {
            Click("Tab加点"); Click("Row1Plus"); Click("PetItem1");
            Assert.That(_panel.SelectedPetId, Is.EqualTo(900001));
            Click("KeepDraft"); Assert.That(_panel.HasDraft, Is.True);
            Click("CultivationClose"); Click("KeepDraft"); Assert.That(_panel.IsVisible, Is.True);
            Click("PetItem1"); Click("DiscardAndLeave");
            Assert.That(_panel.SelectedPetId, Is.EqualTo(900002)); Assert.That(_panel.HasDraft, Is.False);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void BattleLocksBothVisibleControlsAndQueuedClicksWithoutDiscardingDraft()
        {
            Click("Tab加点"); Click("Row1Plus"); _inBattle = true; _panel.Tick();
            Assert.That(Button("Row1Plus").interactable, Is.False); Assert.That(Button("Confirm").interactable, Is.False);
            Button("Row1Plus").onClick.Invoke(); Button("Confirm").onClick.Invoke();
            Assert.That(Text("FourStatIncrement1").text, Is.EqualTo("+1")); Assert.That(_net.Calls, Is.Empty);
            Assert.That(Text("PetCultivationStatus").text, Does.Contain("战斗中"));
            Click("Tab技能"); Assert.That(_panel.Page, Is.EqualTo(PetCultivationPage.Skills));
            Assert.That(_panel.HasDraft, Is.True, "战斗只读翻页不丢弃四维草稿");
            Click("CultivationClose"); Assert.That(_panel.IsVisible, Is.False, "只读窗口可直接关闭返回战斗");
            _inBattle = false; _panel.Show(PetCultivationPage.FourStats);
            Assert.That(_panel.HasDraft, Is.True);
            Assert.That(Button("Confirm").interactable, Is.True, "战斗结束后同一份草稿恢复可保存");
        }

        [Test]
        public void BattleGuardAlsoRejectsDirectPetWritesWhileAllowingAuthoritativeReads()
        {
            _inBattle = true;
            _client.Allocate(900001, new Dictionary<uint, uint> { [401] = 3 });
            _client.ResetPoints(900001); _client.Summon(900001); _client.Recall();
            _client.Rename(900001, "新名字"); _client.GmGrantPet(1); _client.AutoAllocate(900001);
            Assert.That(_net.Calls, Is.Empty); Assert.That(_client.Busy, Is.False);
            Assert.That(_client.PurchaseBlockedReason(1), Is.EqualTo(PetClient.CultivationLockedMessage));
            _client.RequestList(); Assert.That(_net.CallsOf(MessageIds.GetPetList), Has.Count.EqualTo(1));
            _net.CallsOf(MessageIds.GetPetList).Single().Respond(new GetPetListResponse { Pets = _list });
            _inBattle = false; _client.Allocate(900001, new Dictionary<uint, uint> { [401] = 3 });
            Assert.That(_net.CallsOf(MessageIds.AllocatePetPoints), Has.Count.EqualTo(1));
        }

        [Test]
        public void SkillBookUsesSelectedPetAndOnlyItsServerCatalog()
        {
            Click("Tab技能"); Assert.That(_skills.Reads.Last().PetId, Is.EqualTo(900001));
            PublishSkills(900001, 3);
            Click("PetSkillFilter2");
            Assert.That(Text("PetSelectedSkillName").text, Is.EqualTo("真实治疗技能"));
            Click("PetSkillUpgrade");
            Assert.That(_skills.Writes.Single().context.PetId, Is.EqualTo(900001));
            Assert.That(_skills.Writes.Single().id, Is.EqualTo(10010)); Assert.That(_skills.Writes.Single().rank, Is.EqualTo(3));
            Assert.That(_skills.Writes.Single().count, Is.EqualTo(1));
            Button("PetSkillUpgrade").onClick.Invoke(); Assert.That(_skills.Writes, Has.Count.EqualTo(1));
            Assert.That(Text("PetSkillParameters").text, Does.Contain("3/8"), "未在本地模拟升级");
            PublishSkills(900001, 4); Click("PetItem1");
            Assert.That(_skills.Reads.Last().PetId, Is.EqualTo(900002));
            Assert.That(_root.GetComponentsInChildren<Text>(true).Any(t => t.text == "真实治疗技能"), Is.False);
            PublishSkills(900001, 7);
            Assert.That(_root.GetComponentsInChildren<Text>(true).Any(t => t.text == "真实治疗技能"), Is.False, "旧宝宝回包不能盖住新宝宝");
        }

        [Test]
        public void PassiveCatalogAddsItsOwnTabWithoutMixingActiveAttacksOrChangingLegacyTabs()
        {
            Click("Tab技能");
            Assert.That(_root.GetComponentsInChildren<Button>().Any(b => b.name == "PetSkillFilter3Plate"), Is.False);
            var snapshot = new SkillBookSnapshot();
            snapshot.Entries.Add(new SkillEntryState { Id = 10002, Definition = new SkillDefinition { id = 10002, name = "横戈裂岳", type = "active", actionTags = 1 } });
            snapshot.Entries.Add(new SkillEntryState { Id = 12004, Definition = new SkillDefinition { id = 12004, name = "百战归元", type = "passive", actionTags = 8 } });
            snapshot.Entries.Add(new SkillEntryState { Id = 13001, Definition = new SkillDefinition { id = 13001, name = "追击", type = "passive", originLabel = "追击", actionTags = 8 } });
            snapshot.Entries.Add(new SkillEntryState { Id = 14003, Definition = new SkillDefinition { id = 14003, name = "开场", type = "passive", originLabel = "开场", actionTags = 8 } });
            var state = new SkillBookViewState { Context = SkillBookContext.Pet(900001, "测试宝宝"), Snapshot = snapshot, IsConnected = true };
            _skills.Publish(state); _panel.SetViewport(1920, 1080);
            Assert.That(Text("PetSkillFilter0Text").text, Is.EqualTo("主动攻击"));
            Assert.That(Text("PetSkillFilter1Text").text, Is.EqualTo("障碍"));
            Assert.That(Text("PetSkillFilter2Text").text, Is.EqualTo("辅助"));
            Assert.That(Text("PetSkillFilter3Text").text, Is.EqualTo("被动"));
            Assert.That(_root.GetComponentsInChildren<Text>().Where(t => t.name.StartsWith("PetSkillName")).Select(t => t.name), Is.EquivalentTo(new[] { "PetSkillName10002" }));
            Click("PetSkillFilter3");
            Assert.That(_root.GetComponentsInChildren<Text>().Where(t => t.name.StartsWith("PetSkillName")).Select(t => t.name),
                Is.EquivalentTo(new[] { "PetSkillName12004", "PetSkillName13001", "PetSkillName14003" }));
            Assert.That(Text("PetSkillSummary13001").text, Does.StartWith("追击"));
            Assert.That(Text("PetSkillSummary14003").text, Does.StartWith("开场"));
            snapshot.Entries.RemoveAll(e => e.Definition.IsPassive); _skills.Publish(state);
            Assert.That(_root.GetComponentsInChildren<Button>().Any(b => b.name == "PetSkillFilter3Plate"), Is.False);
            Assert.That(Text("PetSelectedSkillName").text, Is.EqualTo("横戈裂岳"));
        }

        [TestCase("disconnected")]
        [TestCase("readonly")]
        public void MissingSkillAuthorityDoesNotDisplayPreviewRanksOrDefaultParameters(string unavailable)
        {
            Click("Tab技能"); PublishSkills(900001, 3); Click("PetSkillFilter2");
            var state = _skills.State;
            if (unavailable == "disconnected") state.IsConnected = false;
            else state.IsReadOnly = true;
            _skills.Publish(state);
            Assert.That(Text("PetSkillEmpty").text, Does.Contain("尚未同步"));
            Assert.That(Text("PetSkillDetailEmpty").text, Does.Contain("—"));
            Assert.That(_root.GetComponentsInChildren<Text>().Any(t => t.name == "PetSkillParameters"), Is.False);
            Assert.That(_skills.Writes, Is.Empty);
            PublishSkills(900001, 3);
            Assert.That(Text("PetSkillParameters").text, Does.Contain("3/8"));
            Assert.That(Text("PetSkillParameters").text, Does.Contain("持续：—"));
        }

        [Test]
        public void SkillFailureAndBattleLockBlockUpgradeAndRefreshRecovers()
        {
            Click("Tab技能"); PublishSkills(900001, 3); Click("PetSkillFilter2");
            var state = _skills.State; state.Error = "请求超时，请刷新"; _skills.Publish(state);
            Assert.That(Button("PetSkillUpgrade").interactable, Is.False);
            Button("PetSkillUpgrade").onClick.Invoke(); Assert.That(_skills.Writes, Is.Empty);
            Click("PetSkillsRefresh"); PublishSkills(900001, 3);
            _inBattle = true; _panel.Tick(); Assert.That(Button("PetSkillUpgrade").interactable, Is.False);
            _inBattle = false; _panel.Tick(); Click("PetSkillUpgradeTen");
            Assert.That(_skills.Writes.Single().count, Is.EqualTo(10));
        }

        [Test]
        public void DisconnectClearsAuthorityAndDisablesAllAllocation()
        {
            Click("Tab加点"); Click("Row1Plus"); _net.IsReady = false; _net.RaiseDisconnected(); _panel.Tick();
            Assert.That(_panel.HasDraft, Is.False); Assert.That(Text("FourStatsRemaining").text, Is.EqualTo("剩余属性点 —"));
            Assert.That(Button("Confirm").interactable, Is.False);
            Assert.That(_client.HasList, Is.False);
        }

        [Test]
        public void NewAuthoritativeBudgetInvalidatesOversizedDraftWithExplanation()
        {
            Click("Tab加点"); Click("Row1Plus"); Click("Row1Plus");
            var changed = _list.Clone(); changed.Pets[0].RemainingPoints = 1;
            _net.PushNotify(MessageIds.NotifyPetListChanged, new PetListChangedS2C { Pets = changed });
            Assert.That(_panel.HasDraft, Is.False);
            Assert.That(Text("PetCultivationStatus").text, Does.Contain("草稿已撤回"));
            Assert.That(Button("Confirm").interactable, Is.False);
            Assert.That(Text("FourStatsRemaining").text, Is.EqualTo("剩余属性点 1"));
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void NarrowLayoutReflowsWithSameNativeHeightAndReachableTabs()
        {
            _panel.SetViewport(1920, 1080);
            Assert.That(_panel.Root.rect.width, Is.EqualTo(1920)); Assert.That(_panel.Root.rect.height, Is.EqualTo(1080));
            var tab = (RectTransform)Button("Tab条目点").transform;
            Assert.That(tab.anchoredPosition.x + tab.rect.width, Is.LessThan(1920 - 70));
            var state = Text("PetState0").rectTransform;
            var card = (RectTransform)state.parent;
            Assert.That(card.rect.width - state.anchoredPosition.x - state.rect.width, Is.EqualTo(18),
                "宝宝状态须保留美术参考的18px右内距，不能被卡片边框裁字");
            Click("Tab加点"); Click("Row1Plus");
            Assert.That(Text("FourStatIncrement1").text, Is.EqualTo("+1"));
        }

        [Test]
        public void BadgePreviewDoesNotInterpretQualityAsFiveTierRankOrMutatePet()
        {
            Click("PetBadgeExplain");
            Assert.That(Text("PetBadgeCurrent").text, Does.Contain("当前阶：—"));
            Assert.That(Text("PetBadgeExplanation").text, Does.Contain("独立身份"));
            for (int i = 0; i < 5; i++) Click("PreviewBadge" + i);
            Assert.That(Text("PetBadgeCurrent").text, Does.Contain("当前预览：圣兽"));
            Assert.That(_client.Pets.Pets[0].Quality, Is.EqualTo(0));
            Assert.That(_net.Calls, Is.Empty);
            Click("PetBadgeClose"); Assert.That(_panel.Page, Is.EqualTo(PetCultivationPage.EntryPoints));
        }

        private void PublishSkills(ulong petId, uint rank)
        {
            var snapshot = new SkillBookSnapshot { CurrencyBalance = 500 };
            snapshot.Entries.Add(new SkillEntryState { Id = 10010, Rank = rank, RankLimit = 8, NextRankCost = 20,
                Definition = new SkillDefinition { id = 10010, name = "真实治疗技能", type = "active", category = "辅助", maxRank = 10,
                    targetTeam = "ally", maxTargets = 1, manaPercent = .1, description = "恢复气血" } });
            _skills.Publish(new SkillBookViewState { Context = SkillBookContext.Pet(petId, "测试宝宝"), Snapshot = snapshot, IsConnected = true });
        }
        private Button Button(string name) => _root.GetComponentsInChildren<Button>(true).Single(b => b.name == name + "Plate");
        private Text Text(string name) => _root.GetComponentsInChildren<Text>(true).Single(t => t.name == name);
        private void Click(string name) { var button = Button(name); Assert.That(button.interactable, Is.True, name); button.onClick.Invoke(); }
        private static PetListInfo MakeList()
        {
            var list = new PetListInfo { MaxPets = 8 };
            for (uint i = 0; i < 2; i++)
            {
                var pet = new PetInfo { PetId = 900001 + i, PetTableId = 1, ModelId = 1001, Name = "测试宝宝" + i, Level = 12, RemainingPoints = 3 };
                string[] names = { "体质", "灵力", "力量", "敏捷" };
                for (uint d = 0; d < 4; d++) pet.Dimensions.Add(new PetDimensionInfo { DimensionId = 401 + d,
                    Name = names[d], Allocated = 2, Value = 35, Cap = d == 0 ? 3u : 100u });
                list.Pets.Add(pet);
            }
            return list;
        }

        private void PublishEntries(uint allocated = 2, ulong version = 1)
        {
            var entry = new PetEntriesInfo { PetId = _list.Pets[0].PetId, StateVersion = version, ConfigVersion = 1,
                TotalPoints = 20, RemainingPoints = 20 - allocated, Effective = true };
            entry.Entries.Add(new PetEntryInfo { Id = 503, Name = "治疗效果", Category = 3, Channel = 3,
                Cap = 20, PerPointBps = 50, Allocated = allocated, EffectiveBps = allocated * 50, Icon = "healing" });
            _list.Pets[0].Entries = entry;
            _list.Pets[0].CultivationTier = 4; _list.Pets[0].CultivationTierName = "神兽"; _list.Pets[0].CultivationArtKey = "divine";
            _net.PushNotify(MessageIds.NotifyPetListChanged, new PetListChangedS2C { Pets = _list });
        }

        [Test]
        public void EntriesUseSeparateBudgetAllowCommittedDecreaseAndRequireServerPreview()
        {
            PublishEntries();
            Assert.That(Text("EntryPointsBudget").text, Does.Contain("总点数 20"));
            Assert.That(Button("EntryMinus503").interactable, Is.True);
            Click("EntryMinus503");
            Assert.That(Text("EntryValue503").text, Is.EqualTo("1"));
            Assert.That(Button("EntrySave").interactable, Is.False);
            var call = _net.CallsOf(MessageIds.PreviewPetEntries).Last();
            var preview = _list.Pets[0].Entries.Clone(); preview.Entries[0].Allocated = 1; preview.Entries[0].EffectiveBps = 50; preview.RemainingPoints = 19;
            call.Respond(new PreviewPetEntriesResponse { Entries = preview });
            Assert.That(Button("EntrySave").interactable, Is.True);
            Click("EntrySave");
            var save = _net.CallsOf(MessageIds.SavePetEntries).Last(); var request = (SavePetEntriesRequest)save.Request;
            Assert.That(request.Target[503], Is.EqualTo(1));
            Assert.That(_net.CallsOf(MessageIds.AllocatePetPoints), Is.Empty);
            var authority = preview.Clone(); authority.StateVersion = 2; authority.LastRequestId = request.RequestId;
            save.Respond(new SavePetEntriesResponse { RequestId = request.RequestId, Entries = authority });
            Assert.That(_panel.HasDraft, Is.False); Assert.That(Text("EntryCurrent503").text, Is.EqualTo("1"));
        }

        [Test]
        public void EntryBattleLockKeepsDraftAndConcurrentRefreshCannotOverwriteIt()
        {
            PublishEntries(); Click("EntryPlus503");
            _inBattle = true; _panel.Tick();
            Assert.That(_panel.HasDraft, Is.True); Assert.That(Button("EntryMinus503").interactable, Is.False);
            Assert.That(Button("EntrySave").interactable, Is.False);
            _inBattle = false; PublishEntries(5, 2); _panel.Tick();
            Assert.That(_panel.HasDraft, Is.True); Assert.That(Button("EntrySave").interactable, Is.False);
            Assert.That(Text("EntryValue503").text, Is.EqualTo("3"));
            Click("EntryRestore");
            Assert.That(_panel.HasDraft, Is.False); Assert.That(Text("EntryValue503").text, Is.EqualTo("5"));
        }

        [Test]
        public void EntryPresetsClearAllAndFiveTierAscensionDialogUseServerAuthority()
        {
            PublishEntries(2, 1);
            var entries = _list.Pets[0].Entries;
            entries.ActivePresetSlot = 1;
            entries.Presets.Add(new PetEntryPresetSummary { SlotId = 1, Tag = 1, UsedPoints = 2 });
            entries.Presets.Add(new PetEntryPresetSummary { SlotId = 2, Tag = 2, UsedPoints = 0 });
            entries.Presets.Add(new PetEntryPresetSummary { SlotId = 3, Tag = 3, UsedPoints = 0 });
            _list.Pets[0].NextTier = new PetTierAscensionInfo
            {
                NextTier = 5, NextTierName = "圣兽", MinPetLevel = 10,
                CostItemId = 3406, CostItemCount = 5, OwnedItemCount = 8,
                CostGold = 5000, BonusEntryPoints = 8, CanAscend = true,
            };
            _net.PushNotify(MessageIds.NotifyPetListChanged, new PetListChangedS2C { Pets = _list });

            // UI-2: 切换至方案 2
            Click("EntryPreset2");
            var switchCall = _net.CallsOf(MessageIds.SwitchPetEntryPreset).Last();
            var switchReq = (SwitchPetEntryPresetRequest)switchCall.Request;
            Assert.That(switchReq.SlotId, Is.EqualTo(2u));
            Assert.That(switchReq.Tag, Is.EqualTo(PetClient.KeepPresetTag));
            var switched = entries.Clone(); switched.StateVersion = 2; switched.ActivePresetSlot = 2; switched.LastRequestId = switchReq.RequestId;
            switchCall.Respond(new SwitchPetEntryPresetResponse { RequestId = switchReq.RequestId, Entries = switched });

            // UI-2: 一键清零提交空 Target
            Click("EntryClearAll");
            var clearCall = _net.CallsOf(MessageIds.SavePetEntries).Last();
            var clearReq = (SavePetEntriesRequest)clearCall.Request;
            Assert.That(clearReq.Target, Is.Empty);
            var cleared = switched.Clone(); cleared.StateVersion = 3; cleared.Entries[0].Allocated = 0; cleared.LastRequestId = clearReq.RequestId;
            clearCall.Respond(new SavePetEntriesResponse { RequestId = clearReq.RequestId, Entries = cleared });

            // UI-3: 打开灵阶突破确认窗并提交
            Click("PetAscendTier");
            Assert.That(Text("PetAscendNextName").text, Does.Contain("圣兽"));
            Click("PetAscendConfirm");
            var ascendCall = _net.CallsOf(MessageIds.AscendPetTier).Last();
            var ascendReq = (AscendPetTierRequest)ascendCall.Request;
            Assert.That(ascendReq.ExpectedTier, Is.EqualTo(4u));
            var upgradedList = _list.Clone();
            upgradedList.Pets[0].CultivationTier = 5; upgradedList.Pets[0].CultivationTierName = "圣兽"; upgradedList.Pets[0].NextTier = null;
            ascendCall.Respond(new AscendPetTierResponse { ClientRequestId = ascendReq.ClientRequestId, NewTier = 5, Pets = upgradedList });
            Assert.That(_client.Pets.Pets[0].CultivationTier, Is.EqualTo(5u));
        }
    }
}
