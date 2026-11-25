using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>通过正式业务客户端和假传输验证窗口选择、权威预览与持久回执的接缝。</summary>
    public sealed class EquipmentCraftUiStateTests
    {
        private const ulong Player = 8102;
        private const ulong ItemId = 9007199254740993UL;
        private string _directory;
        private DurableRequestFileStore _store;
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private EquipmentCraftUiState _ui;
        private readonly List<ulong> _applied = new();
        private long _now;
        private int _changes;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "equipment-craft-ui-test-" + Guid.NewGuid().ToString("N"));
            _store = new DurableRequestFileStore(_directory);
            _net = new FakeBattleTransport { PlayerId = Player, IsReady = true };
            _now = 10000;
            _client = new PlayerFeaturesClient(_net) { EquipmentCraftRecovery = _store, NowMs = () => _now };
            _ui = new EquipmentCraftUiState();
            _ui.Bind(_client);
            _applied.Clear();
            _changes = 0;
            _ui.Applied += _applied.Add;
            _ui.Changed += () => ++_changes;
        }

        [TearDown]
        public void TearDown()
        {
            _ui.Dispose();
            _client.Dispose();
            if (!Directory.Exists(_directory)) return;
            Assert.That(Path.GetDirectoryName(Path.GetFullPath(_directory)),
                Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
            Assert.That(Path.GetFileName(_directory), Does.StartWith("equipment-craft-ui-test-"));
            Directory.Delete(_directory, true);
        }

        private FakeBattleTransport.RecordedCall Last => _net.Calls.Last();
        private List<FakeBattleTransport.RecordedCall> Previews => _net.CallsOf(SceneBagClientPlayerRpc.PreviewEquipmentCraftMessageId);
        private List<FakeBattleTransport.RecordedCall> Writes => _net.CallsOf(SceneBagClientPlayerRpc.CommitEquipmentCraftMessageId);
        private CommitEquipmentCraftRequest Write => (CommitEquipmentCraftRequest)Writes.Last().Request;
        private static EquipmentCraftChoice Recipe(uint configId = 99001) => new()
        { Operation = EquipmentCraftChoice.Types.Operation.CreateItem, ConfigId = configId };
        private static EquipmentCraftChoice Affix() => new()
        { Operation = EquipmentCraftChoice.Types.Operation.InstallAffix, ItemId = ItemId, AffixSlot = 2, AffixId = 36 };
        private static EquipmentCraftPreview PreviewFor(EquipmentCraftChoice choice, bool available = true) => new()
        {
            Choice = choice.Clone(), RulesFingerprint = ByteString.CopyFromUtf8("server-rules-v1"),
            ExpectedForgeRevision = 7, Available = available,
            Result = new BagItemInfo { ItemId = choice.ItemId, ConfigId = 99001, EquipKind = 11, EquipProfile = 1, Count = 1, MaxStack = 1 },
            Material = new EquipmentCraftMaterial { ConfigId = 99002, Count = 20, Owned = 51, Available = 31 },
            UnavailableTip = available ? 0u : (uint)equip_error.KEquipItemNotFound,
        };

        private void Select(EquipmentCraftChoice choice)
        {
            if (choice.Operation == EquipmentCraftChoice.Types.Operation.InstallAffix)
                _ui.SelectAffix(choice.ItemId, choice.AffixSlot, choice.AffixId);
            else _ui.SelectRecipe(choice.ConfigId);
        }

        private EquipmentCraftPreview LoadPreview(EquipmentCraftChoice choice = null, bool available = true)
        {
            choice ??= Recipe();
            Select(choice);
            var preview = PreviewFor(choice, available);
            Last.Respond(new PreviewEquipmentCraftResponse { Preview = preview });
            return preview;
        }

        private static BagInfo Bag(uint type) => new()
        { Layout = new BagLayoutInfo { BagType = type, Capacity = 20, CanSort = type == 0 } };

        private CommitEquipmentCraftResponse Receipt(bool durable = true) => new()
        {
            ClientRequestId = Write.ClientRequestId, Outcome = CommitEquipmentCraftResponse.Types.Outcome.Applied,
            ItemId = ItemId, Durable = durable, ResultForgeRevision = Write.ExpectedForgeRevision + 1,
            CurrentForgeRevision = Write.ExpectedForgeRevision + 1, Bag = Bag(0), Equipment = Bag(2),
        };

        private void Commit()
        {
            LoadPreview();
            _ui.Commit();
        }

        [Test]
        public void UnboundStateHasNoSelectionAndAllCommandsAreSafe()
        {
            _ui.Bind(null);
            _ui.RefreshRules();
            _ui.SelectRecipe(99001);
            _ui.SelectAffix(ItemId, 2, 36);
            _ui.Commit();
            _ui.Recover();
            _ui.ClearSelection();
            Assert.That(_ui.Choice, Is.Null);
            Assert.That(_ui.Preview, Is.Null);
            Assert.That(_ui.Rules, Is.Null);
            Assert.That(_ui.CanCommit || _ui.Busy || _ui.Pending || _ui.LoadingRules || _ui.LoadingPreview, Is.False);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void RequestBeforeEnteringRoleShowsClientRejection()
        {
            _net.IsReady = false;
            _ui.RefreshRules();
            Assert.That(_ui.Error, Is.Not.Empty);
            Assert.That(_ui.LoadingRules || _ui.CanCommit, Is.False);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void RulesRefreshExposesServerDataAndDefensiveCopies()
        {
            _ui.RefreshRules();
            _ui.RefreshRules();
            Assert.That(_net.CallsOf(SceneBagClientPlayerRpc.GetEquipmentCraftRulesMessageId).Count, Is.EqualTo(1));
            Assert.That(_ui.LoadingRules, Is.True);
            var response = new GetEquipmentCraftRulesResponse { RulesFingerprint = ByteString.CopyFromUtf8("rules"), ForgeRevision = 7 };
            response.Recipes.Add(new EquipmentCraftRecipe { Product = PreviewFor(Recipe()).Result, MaterialConfigId = 9, MaterialCount = 40 });
            response.Affixes.Add(new EquipFixedAffixInfo { AffixId = 36, Pool = 3, ValueBps = 123, Unit = 2, SourceCapBps = 987 });
            Last.Respond(response);
            Assert.That(_ui.LoadingRules, Is.False);
            Assert.That(_ui.Rules, Is.EqualTo(response));
            _ui.Rules.Affixes[0].ValueBps = 9999;
            Assert.That(_ui.Rules.Affixes[0].ValueBps, Is.EqualTo(123));
            Assert.That(_changes, Is.GreaterThan(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SelectionAndCommitForwardExactAuthoritativeChoice(bool installAffix)
        {
            var choice = installAffix ? Affix() : Recipe();
            var preview = LoadPreview(choice);
            Assert.That(_ui.Choice, Is.EqualTo(choice));
            Assert.That(_ui.Preview, Is.EqualTo(preview));
            Assert.That(_ui.CanCommit, Is.True);
            _ui.Choice.ConfigId = 5;
            _ui.Preview.ExpectedForgeRevision = 999;
            _ui.Preview.Material.Count = 1;
            _ui.Commit();
            Assert.That(Write.Choice, Is.EqualTo(choice));
            Assert.That(Write.ExpectedForgeRevision, Is.EqualTo(7));
            Assert.That(Write.RulesFingerprint, Is.EqualTo(preview.RulesFingerprint));
            Assert.That(_ui.Busy && _ui.Pending, Is.True);
            Assert.That(_ui.CanCommit, Is.False);
            Assert.That(_store.Load(Player), Is.Not.Empty);
        }

        [Test]
        public void RepeatedSelectionKeepsInFlightAndAcceptedPreview()
        {
            _ui.SelectRecipe(99001);
            _ui.SelectRecipe(99001);
            Assert.That(_ui.LoadingPreview, Is.True);
            Assert.That(Previews.Count, Is.EqualTo(1));
            var preview = PreviewFor(Recipe());
            Last.Respond(new PreviewEquipmentCraftResponse { Preview = preview });
            _ui.SelectRecipe(99001);
            Assert.That(Previews.Count, Is.EqualTo(1));
            Assert.That(_ui.Preview, Is.EqualTo(preview));
            Assert.That(_ui.CanCommit, Is.True);
        }

        [Test]
        public void FailedPreviewCanRetrySameSelection()
        {
            _ui.SelectRecipe(99001);
            Last.FailWith("timeout");
            Assert.That(_ui.Error, Is.Not.Empty);
            Assert.That(_ui.LoadingPreview || _ui.CanCommit, Is.False);
            _ui.SelectRecipe(99001);
            Assert.That(Previews.Count, Is.EqualTo(2));
            Assert.That(_ui.Error, Is.Empty);
            Assert.That(_ui.LoadingPreview, Is.True);
        }

        [Test]
        public void UnavailablePreviewRetainsServerReasonButCannotCommit()
        {
            var preview = LoadPreview(available: false);
            Assert.That(_ui.Preview.UnavailableTip, Is.EqualTo(preview.UnavailableTip));
            Assert.That(_ui.Preview.Material.Available, Is.EqualTo(31));
            Assert.That(_ui.CanCommit, Is.False);
            _ui.Commit();
            Assert.That(Writes, Is.Empty);
        }

        [Test]
        public void LatePreviewCannotReplaceNewSelection()
        {
            _ui.SelectRecipe(1);
            var old = Last;
            var expected = LoadPreview(Recipe(2));
            old.Respond(new PreviewEquipmentCraftResponse { Preview = PreviewFor(Recipe(1)) });
            Assert.That(_ui.Choice.ConfigId, Is.EqualTo(2));
            Assert.That(_ui.Preview, Is.EqualTo(expected));
        }

        [Test]
        public void AnotherClientChoiceIsNeverExposedOrCommittedByThisWindow()
        {
            LoadPreview();
            _client.PreviewEquipmentCraft(Recipe(2));
            Last.Respond(new PreviewEquipmentCraftResponse { Preview = PreviewFor(Recipe(2)) });
            Assert.That(_ui.Choice.ConfigId, Is.EqualTo(99001));
            Assert.That(_ui.Preview, Is.Null);
            Assert.That(_ui.CanCommit, Is.False);
            _ui.Commit();
            Assert.That(Writes, Is.Empty);
        }

        [Test]
        public void ClearSelectionHidesLatePreviewAndReselectRequestsFreshPreview()
        {
            _ui.SelectRecipe(99001);
            var old = Last;
            _ui.ClearSelection();
            old.Respond(new PreviewEquipmentCraftResponse { Preview = PreviewFor(Recipe()) });
            Assert.That(_ui.Choice, Is.Null);
            Assert.That(_ui.Preview, Is.Null);
            Assert.That(_ui.LoadingPreview || _ui.CanCommit, Is.False);
            _ui.SelectRecipe(99001);
            Assert.That(Previews.Count, Is.EqualTo(2));
            Assert.That(_ui.LoadingPreview, Is.True);
            Assert.That(_ui.Preview, Is.Null);
        }

        [Test]
        public void OtherAssetWriteBlocksCommitAndChangingSelection()
        {
            LoadPreview();
            _client.EquipItem(ItemId);
            Assert.That(_client.BusyEquip, Is.True);
            Assert.That(_ui.Busy, Is.True);
            Assert.That(_ui.CanCommit, Is.False);
            _ui.SelectRecipe(2);
            _ui.Commit();
            Assert.That(_ui.Choice.ConfigId, Is.EqualTo(99001));
            Assert.That(Previews.Count, Is.EqualTo(1));
            Assert.That(Writes, Is.Empty);
        }

        [Test]
        public void PendingCommitCannotBeReplacedOrClearedByClosingSelection()
        {
            Commit();
            string record = _store.Load(Player);
            _ui.ClearSelection();
            _ui.SelectRecipe(2);
            _ui.SelectAffix(ItemId, 2, 36);
            _ui.Commit();
            Assert.That(_ui.Choice, Is.Null);
            Assert.That(_ui.Pending && _ui.Busy, Is.True);
            Assert.That(Writes.Count, Is.EqualTo(1));
            Assert.That(Previews.Count, Is.EqualTo(1));
            Assert.That(_store.Load(Player), Is.EqualTo(record));
        }

        [Test]
        public void AppliedWithoutDurabilityOnlyRecoversOriginalKeyThenPublishesOnce()
        {
            Commit();
            var request = Write.Clone();
            string record = _store.Load(Player);
            Writes.Last().Respond(Receipt(false));
            Assert.That(_ui.Pending, Is.True);
            Assert.That(_ui.Busy || _ui.CanCommit, Is.False);
            Assert.That(_ui.Error, Is.Not.Empty);
            Assert.That(_applied, Is.Empty);
            Assert.That(_store.Load(Player), Is.EqualTo(record));
            _now += 20000;
            _ui.Recover();
            Assert.That(Write, Is.EqualTo(request));
            var recovery = Writes.Last();
            var receipt = Receipt();
            recovery.Respond(receipt);
            recovery.Respond(receipt);
            Assert.That(_applied, Is.EqualTo(new[] { ItemId }));
            Assert.That(_ui.Choice, Is.Null);
            Assert.That(_ui.Preview, Is.Null);
            Assert.That(_ui.Pending || _ui.CanCommit, Is.False);
            Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void UnknownOutcomeRemainsPendingAndRecoverKeepsOriginalRequest()
        {
            Commit();
            var request = Write.Clone();
            Writes.Last().Respond(new CommitEquipmentCraftResponse
            {
                ClientRequestId = request.ClientRequestId,
                Outcome = CommitEquipmentCraftResponse.Types.Outcome.Unknown,
                CurrentForgeRevision = 100,
            });
            _ui.ClearSelection();
            _ui.SelectRecipe(2);
            _ui.Commit();
            Assert.That(_ui.Pending, Is.True);
            Assert.That(_ui.Choice, Is.Null);
            Assert.That(_applied, Is.Empty);
            Assert.That(Writes.Count, Is.EqualTo(1));
            _now += 20000;
            _ui.Recover();
            Assert.That(Write, Is.EqualTo(request));
            Assert.That(Writes.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitRejectionAllowsFreshPreviewWithoutPublishingSuccess()
        {
            Commit();
            Writes.Last().Respond(new CommitEquipmentCraftResponse
            { ClientRequestId = Write.ClientRequestId, Outcome = CommitEquipmentCraftResponse.Types.Outcome.Rejected });
            Assert.That(_ui.Pending, Is.False);
            Assert.That(_ui.Error, Is.Not.Empty);
            Assert.That(_applied, Is.Empty);
            Assert.That(_ui.CanCommit, Is.False);
            _ui.SelectRecipe(99001);
            Assert.That(Previews.Count, Is.EqualTo(2));
            Assert.That(_ui.LoadingPreview, Is.True);
        }

        [Test]
        public void UnbindingPreservesPendingAndStopsOldClientNotifications()
        {
            Commit();
            string record = _store.Load(Player);
            _ui.Bind(null);
            int changes = _changes;
            Writes.Last().FailWith("timeout");
            Assert.That(_changes, Is.EqualTo(changes));
            Assert.That(_store.Load(Player), Is.EqualTo(record));
            _ui.Bind(_client);
            Assert.That(_ui.Pending, Is.True);
            Assert.That(_ui.Choice, Is.Null);
            _now += 20000;
            _ui.Recover();
            Assert.That(Writes.Count, Is.EqualTo(2));
        }

        [Test]
        public void DisconnectClearsSelectionButKeepsRecoverableRequest()
        {
            Commit();
            var original = Write.Clone();
            string record = _store.Load(Player);
            _net.RaiseDisconnected();
            Assert.That(_ui.Choice, Is.Null);
            Assert.That(_ui.Preview, Is.Null);
            Assert.That(_ui.CanCommit, Is.False);
            Assert.That(_store.Load(Player), Is.EqualTo(record));
            _now += 20000;
            _ui.Recover();
            Assert.That(Write, Is.EqualTo(original));
            Assert.That(Writes.Count, Is.EqualTo(2));
            Assert.That(_ui.Pending, Is.True);
        }

        [Test]
        public void SwitchingCharacterDropsOldSelectionAndIgnoresOldReply()
        {
            _ui.SelectRecipe(99001);
            var old = Last;
            _net.PlayerId = Player + 1;
            _ui.RefreshRules();
            old.Respond(new PreviewEquipmentCraftResponse { Preview = PreviewFor(Recipe()) });
            Assert.That(_ui.Choice, Is.Null);
            Assert.That(_ui.Preview, Is.Null);
            Assert.That(_ui.CanCommit, Is.False);
            Assert.That(_applied, Is.Empty);
        }

        [Test]
        public void AppliedNotificationDoesNotLeakAcrossReentrantUnbind()
        {
            Commit();
            // 成功先通知快照变化；窗口可在下一次选择清理通知中被导航关闭。
            _ui.Changed += () =>
            {
                if (!_ui.Pending && _ui.Choice == null) _ui.Bind(null);
            };
            Writes.Last().Respond(Receipt());
            Assert.That(_applied, Is.Empty);
            Assert.That(_ui.Rules, Is.Null);
            Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void DisposingDoesNotClearPendingOrContinueReceivingChanges()
        {
            Commit();
            string record = _store.Load(Player);
            _ui.Dispose();
            int changes = _changes;
            Writes.Last().FailWith("timeout");
            _ui.Bind(_client);
            _ui.RefreshRules();
            _ui.SelectRecipe(2);
            _ui.Commit();
            _ui.Recover();
            Assert.That(_changes, Is.EqualTo(changes));
            Assert.That(_store.Load(Player), Is.EqualTo(record));
            Assert.That(Writes.Count, Is.EqualTo(1));
            Assert.That(_applied, Is.Empty);
            Assert.That(_ui.CanCommit || _ui.Pending || _ui.Busy, Is.False);
        }

        [Test]
        public void PolishBindConfirmationAndAppliedReceiptExposeServerState()
        {
            CommitEquipmentCraftResponse received = null;
            _ui.AppliedReceipt += r => received = r;

            _ui.SelectPolishAffix(ItemId, 1, useGuaranteeCharm: false, allowBoundMaterials: false,
                proxyMasterPlayerId: 0, expectedPolishPityCount: 6u);
            var blockedChoice = ((PreviewEquipmentCraftRequest)Last.Request).Choice;
            var blockedPreview = PreviewFor(blockedChoice, available: false);
            blockedPreview.UnavailableTip = (uint)equip_error.KEquipBindConfirmRequired;
            blockedPreview.WillBindItem = true;
            Last.Respond(new PreviewEquipmentCraftResponse { Preview = blockedPreview });

            Assert.That(_ui.RequiresBindConfirmation, Is.True);
            Assert.That(_ui.CanCommit, Is.False);

            _ui.SelectPolishAffix(ItemId, 1, useGuaranteeCharm: false, allowBoundMaterials: true,
                proxyMasterPlayerId: 0, expectedPolishPityCount: 6u);
            var allowedChoice = ((PreviewEquipmentCraftRequest)Last.Request).Choice;
            var allowedPreview = PreviewFor(allowedChoice, available: true);
            allowedPreview.WillBindItem = true;
            Last.Respond(new PreviewEquipmentCraftResponse { Preview = allowedPreview });

            Assert.That(_ui.RequiresBindConfirmation, Is.False);
            Assert.That(_ui.CanCommit, Is.True);

            _ui.Commit();
            var receipt = Receipt();
            receipt.ResultQualityTier = 5;
            receipt.GuaranteedByPity = true;
            Writes.Last().Respond(receipt);

            Assert.That(received, Is.Not.Null);
            Assert.That(received.ResultQualityTier, Is.EqualTo(5u));
            Assert.That(_ui.LastApplied.GuaranteedByPity, Is.True);
        }
    }
}
