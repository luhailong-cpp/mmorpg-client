using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class EquipmentCraftClientTests
    {
        private const ulong Player = 8102;
        private const ulong ItemId = 9007199254740993UL;
        private string _directory;
        private DurableRequestFileStore _store;
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private object _connection;
        private long _now;
        private readonly List<CommitEquipmentCraftResponse> _applied = new();

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "equipment-craft-test-" + Guid.NewGuid().ToString("N"));
            _store = new DurableRequestFileStore(_directory);
            _net = new FakeBattleTransport { PlayerId = Player, IsReady = true };
            _connection = new object();
            _now = 10000;
            _applied.Clear();
            CreateClient();
        }

        private void CreateClient()
        {
            _client = new PlayerFeaturesClient(_net, () => _connection)
            { EquipmentCraftRecovery = _store, NowMs = () => _now };
            _client.OnEquipmentCraftApplied += _applied.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
            if (!Directory.Exists(_directory)) return;
            Assert.That(Path.GetDirectoryName(Path.GetFullPath(_directory)),
                Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
            Assert.That(Path.GetFileName(_directory), Does.StartWith("equipment-craft-test-"));
            Directory.Delete(_directory, true);
        }

        private FakeBattleTransport.RecordedCall Last => _net.Calls.Last();
        private List<FakeBattleTransport.RecordedCall> Writes => _net.CallsOf(SceneBagClientPlayerRpc.CommitEquipmentCraftMessageId);
        private CommitEquipmentCraftRequest LastWrite => (CommitEquipmentCraftRequest)Writes.Last().Request;
        private static EquipmentCraftChoice Choice(uint config = 99001) => new()
        { Operation = EquipmentCraftChoice.Types.Operation.CreateItem, ConfigId = config };
        private static EquipmentCraftPreview Preview(EquipmentCraftChoice choice = null, bool available = true) => new()
        {
            Choice = choice ?? Choice(), RulesFingerprint = ByteString.CopyFromUtf8("server-rules-v1"),
            ExpectedForgeRevision = 0, Available = available,
            Result = new BagItemInfo { ConfigId = 99001, EquipKind = 11, EquipProfile = 1, Count = 1, MaxStack = 1 },
            Material = new EquipmentCraftMaterial { ConfigId = 99002, Count = 20, Owned = 51, Available = 31 },
            UnavailableTip = available ? 0u : (uint)equip_error.KEquipItemNotFound,
        };
        private static BagInfo Bag(uint type, uint count = 51)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = type, Capacity = 20, CanSort = type == 0 } };
            if (type == 0)
            {
                bag.Items.Add(new BagItemInfo { ItemId = 2001, ConfigId = 99002, Count = count, MaxStack = 999 });
                bag.Layout.Slots.Add(new BagSlotInfo { Slot = 0, ItemId = 2001, Width = 1, Height = 1 });
            }
            return bag;
        }
        private void LoadBag()
        {
            _client.RequestBag();
            Last.Respond(new GetBagResponse { Bag = Bag(0) });
            _client.RequestEquipment();
            Last.Respond(new GetBagResponse { Bag = Bag(2) });
        }
        private EquipmentCraftPreview LoadPreview(EquipmentCraftChoice choice = null, bool available = true)
        {
            choice ??= Choice();
            _client.PreviewEquipmentCraft(choice);
            Assert.That(Last.Request, Is.TypeOf<PreviewEquipmentCraftRequest>());
            Last.Respond(new PreviewEquipmentCraftResponse { Preview = Preview(choice, available) });
            return _client.CraftPreview;
        }
        private void Commit() => _client.CommitEquipmentCraft(LoadPreview());
        private CommitEquipmentCraftResponse Receipt(bool durable = true) => new()
        {
            ClientRequestId = LastWrite.ClientRequestId, Outcome = CommitEquipmentCraftResponse.Types.Outcome.Applied,
            Durable = durable, ItemId = ItemId, ResultForgeRevision = LastWrite.ExpectedForgeRevision + 1,
            CurrentForgeRevision = LastWrite.ExpectedForgeRevision + 1, Bag = Bag(0, 31), Equipment = Bag(2),
        };
        private void Later() => _now += 20000;

        [Test]
        public void RulesPreserveServerRecipesAffixesAndUnitsWithoutLocalInterpretation()
        {
            _client.RequestEquipmentCraftRules();
            var response = new GetEquipmentCraftRulesResponse { RulesFingerprint = ByteString.CopyFromUtf8("rule"), ForgeRevision = 8 };
            response.Recipes.Add(new EquipmentCraftRecipe { Product = Preview().Result, MaterialConfigId = 9, MaterialCount = 40 });
            response.Affixes.Add(new EquipFixedAffixInfo { AffixId = 6, Pool = 3, ValueBps = 123, Unit = 2, SourceCapBps = 987 });
            Last.Respond(response);
            Assert.That(_client.EquipmentCraftRules, Is.EqualTo(response));
            _client.EquipmentCraftRules.Affixes[0].ValueBps = 9999;
            Assert.That(_client.EquipmentCraftRules.Affixes[0].ValueBps, Is.EqualTo(123));
        }

        [Test]
        public void LatestPreviewWinsAndUnavailableChoiceCannotCommit()
        {
            _client.PreviewEquipmentCraft(Choice(1));
            var old = Last;
            _client.PreviewEquipmentCraft(Choice(2));
            Last.Respond(new PreviewEquipmentCraftResponse { Preview = Preview(Choice(2), false) });
            old.Respond(new PreviewEquipmentCraftResponse { Preview = Preview(Choice(1)) });
            Assert.That(_client.CraftPreview.Choice.ConfigId, Is.EqualTo(2));
            _client.CommitEquipmentCraft(_client.CraftPreview);
            Assert.That(Writes, Is.Empty);
        }

        [Test]
        public void CallerCannotForgePreviewOrReuseOlderSelection()
        {
            var previous = LoadPreview();
            var altered = previous.Clone();
            altered.ExpectedForgeRevision = 99;
            _client.CommitEquipmentCraft(altered);
            Assert.That(Writes, Is.Empty);
            LoadPreview(Choice(99003));
            _client.CommitEquipmentCraft(previous);
            Assert.That(Writes, Is.Empty);
        }

        [Test]
        public void WrongChoiceInPreviewResponseIsRejected()
        {
            _client.PreviewEquipmentCraft(Choice(1));
            Last.Respond(new PreviewEquipmentCraftResponse { Preview = Preview(Choice(2)) });
            Assert.That(_client.CraftPreview, Is.Null);
            Assert.That(_client.EquipmentCraftError, Is.Not.Empty);
        }

        [Test]
        public void RequestIsPersistedBeforeSendAndInventoryIsNotDeductedLocally()
        {
            LoadBag();
            var before = _client.CharacterBag.ToByteArray();
            var preview = LoadPreview();
            bool observed = false;
            _client.OnChanged += () =>
            {
                if (!_client.BusyEquipmentCraft) return;
                observed = true;
                Assert.That(_store.Load(Player), Is.Not.Empty);
                Assert.That(Writes, Is.Empty);
            };
            _client.CommitEquipmentCraft(preview);
            Assert.That(observed, Is.True);
            Assert.That(LastWrite.ClientRequestId, Is.Not.Zero);
            Assert.That(LastWrite.ExpectedForgeRevision, Is.Zero);
            Assert.That(LastWrite.RulesFingerprint, Is.EqualTo(preview.RulesFingerprint));
            Assert.That(LastWrite.Choice, Is.EqualTo(preview.Choice));
            Assert.That(CommitEquipmentCraftRequest.Parser.ParseFrom(Convert.FromBase64String(_store.Load(Player))), Is.EqualTo(LastWrite));
            Assert.That(_client.CharacterBag.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void InstallUsesExplicitSlotAndExactGuid()
        {
            _client.PreviewEquipAffix(ItemId, 0, 36);
            var selection = ((PreviewEquipmentCraftRequest)Last.Request).Choice;
            Assert.That(selection.Operation, Is.EqualTo(EquipmentCraftChoice.Types.Operation.InstallAffix));
            Last.Respond(new PreviewEquipmentCraftResponse { Preview = Preview(selection) });
            _client.CommitEquipmentCraft(_client.CraftPreview);
            Assert.That(LastWrite.Choice.ItemId, Is.EqualTo(ItemId));
            Assert.That(LastWrite.Choice.ConfigId, Is.Zero);
            Assert.That(LastWrite.Choice.AffixSlot, Is.Zero);
            Assert.That(LastWrite.Choice.AffixId, Is.EqualTo(36));
        }

        [Test]
        public void MissingOrUnwritableStoreDoesNotSend()
        {
            var preview = LoadPreview();
            _client.EquipmentCraftRecovery = null;
            _client.CommitEquipmentCraft(preview);
            Assert.That(Writes, Is.Empty);
            Directory.CreateDirectory(_directory);
            string blocked = Path.Combine(_directory, "file-not-directory");
            File.WriteAllText(blocked, "fixture");
            _client.EquipmentCraftRecovery = new DurableRequestFileStore(blocked);
            _client.CommitEquipmentCraft(preview);
            Assert.That(Writes, Is.Empty);
        }

        [Test]
        public void CorruptRecordIsRetainedAndBlocksOldAssetActionsBeforeTick()
        {
            _store.Save(Player, "!invalid");
            _client.SortBag();
            _client.EquipItem(ItemId);
            _client.RetrieveAllTemporaryItems();
            Assert.That(_client.HasPendingEquipmentCraft, Is.True);
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_store.Load(Player), Is.EqualTo("!invalid"));
        }

        [Test]
        public void DisconnectAndRecreationReplaySameBytesAndIgnoreOldCallback()
        {
            Commit();
            var original = Writes.Last();
            string record = _store.Load(Player);
            var response = Receipt();
            _net.RaiseDisconnected();
            _client.Dispose();
            CreateClient();
            _client.TickEquipmentCraftRecovery();
            Assert.That(Writes.Count, Is.EqualTo(2));
            Assert.That(LastWrite.ToByteArray(), Is.EqualTo(original.Request.ToByteArray()));
            original.Respond(response);
            Assert.That(_applied, Is.Empty);
            Assert.That(_store.Load(Player), Is.EqualTo(record));
            Writes.Last().Respond(response);
            Assert.That(_applied.Count, Is.EqualTo(1));
        }

        [Test]
        public void UnknownExpiredRevisionDoesNotClearOrGenerateAnotherKey()
        {
            Commit();
            var request = LastWrite.Clone();
            string record = _store.Load(Player);
            Writes.Last().Respond(new CommitEquipmentCraftResponse
            {
                ClientRequestId = request.ClientRequestId, Outcome = CommitEquipmentCraftResponse.Types.Outcome.Unknown,
                CurrentForgeRevision = 99, ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipItemNotFound },
            });
            Assert.That(_client.HasPendingEquipmentCraft, Is.True);
            _client.CommitEquipmentCraft(Preview());
            _client.PreviewEquipmentCraft(99002);
            Assert.That(Writes.Count, Is.EqualTo(1));
            Later();
            _client.RecoverEquipmentCraft();
            Assert.That(LastWrite, Is.EqualTo(request));
            Assert.That(_store.Load(Player), Is.EqualTo(record));
        }

        [Test]
        public void AppliedWithoutDurabilityUpdatesSnapshotButKeepsOriginalIntent()
        {
            LoadBag();
            Commit();
            string record = _store.Load(Player);
            Writes.Last().Respond(Receipt(false));
            Assert.That(_client.CharacterBag.Items[0].Count, Is.EqualTo(31));
            Assert.That(_client.HasPendingEquipmentCraft, Is.True);
            Assert.That(_applied, Is.Empty);
            Assert.That(_store.Load(Player), Is.EqualTo(record));
            Later();
            _client.RecoverEquipmentCraft();
            var response = Receipt();
            response.Replayed = true;
            Writes.Last().Respond(response);
            Assert.That(_applied.Single().Replayed, Is.True);
            Assert.That(_client.HasPendingEquipmentCraft, Is.False);
            Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void ExplicitRejectionClearsIntentWithoutDeducting()
        {
            LoadBag();
            byte[] before = _client.CharacterBag.ToByteArray();
            Commit();
            Writes.Last().Respond(new CommitEquipmentCraftResponse
            {
                ClientRequestId = LastWrite.ClientRequestId, Outcome = CommitEquipmentCraftResponse.Types.Outcome.Rejected,
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipItemNotFound },
            });
            Assert.That(_client.CharacterBag.ToByteArray(), Is.EqualTo(before));
            Assert.That(_client.HasPendingEquipmentCraft, Is.False);
            Assert.That(_store.Load(Player), Is.Empty);
            Assert.That(_applied, Is.Empty);
        }

        [TestCase("identity")]
        [TestCase("revision")]
        [TestCase("snapshot")]
        [TestCase("tip")]
        public void MalformedAppliedReceiptNeverClearsIntent(string defect)
        {
            Commit();
            var response = Receipt();
            if (defect == "identity") ++response.ClientRequestId;
            if (defect == "revision") response.ResultForgeRevision = 100;
            if (defect == "snapshot") response.Equipment = null;
            if (defect == "tip") response.ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipItemNotFound };
            Writes.Last().Respond(response);
            Assert.That(_store.Load(Player), Is.Not.Empty);
            Assert.That(_client.HasPendingEquipmentCraft, Is.True);
            Assert.That(_applied, Is.Empty);
        }

        [Test]
        public void WrongInstalledItemReceiptNeverClearsIntent()
        {
            _client.CommitEquipmentCraft(LoadPreview(new EquipmentCraftChoice
            { Operation = EquipmentCraftChoice.Types.Operation.InstallAffix, ItemId = ItemId, AffixSlot = 1, AffixId = 3 }));
            var response = Receipt();
            ++response.ItemId;
            Writes.Last().Respond(response);
            Assert.That(_client.HasPendingEquipmentCraft, Is.True);
            Assert.That(_applied, Is.Empty);
        }

        [Test]
        public void DurableSuccessReplacesSnapshotsAndIgnoresStaleReadsAndDuplicateReply()
        {
            LoadBag();
            _client.RequestBag();
            var oldBag = Last;
            _client.RequestEquipment();
            var oldEquipment = Last;
            Commit();
            var call = Writes.Last();
            var response = Receipt();
            call.Respond(response);
            oldBag.Respond(new GetBagResponse { Bag = Bag(0, 999) });
            oldEquipment.Respond(new GetBagResponse { Bag = Bag(2, 999) });
            call.Respond(response);
            Assert.That(_client.CharacterBag, Is.EqualTo(response.Bag));
            Assert.That(_client.Equipment, Is.EqualTo(response.Equipment));
            Assert.That(_applied.Count, Is.EqualTo(1));
            Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void ReplacedDiskRecordCannotBeDeletedByOldReceipt()
        {
            Commit();
            string record = _store.Load(Player);
            var replacement = LastWrite.Clone();
            ++replacement.ClientRequestId;
            string changed = Convert.ToBase64String(replacement.ToByteArray());
            _store.Transition(Player, record, changed);
            Writes.Last().Respond(Receipt());
            Assert.That(_store.Load(Player), Is.EqualTo(changed));
            Assert.That(_client.HasPendingEquipmentCraft, Is.True);
            Assert.That(_applied, Is.Empty);
        }

        [Test]
        public void AutomaticRecoveryIsBoundedAndExplicitRecoveryKeepsOriginalKey()
        {
            Commit();
            var original = LastWrite.Clone();
            Writes.Last().FailWith("timeout");
            _client.TickEquipmentCraftRecovery();
            Assert.That(Writes.Count, Is.EqualTo(1));
            for (int i = 0; i < 3; ++i)
            {
                Later();
                _client.TickEquipmentCraftRecovery();
                Assert.That(LastWrite, Is.EqualTo(original));
                Writes.Last().FailWith("timeout");
            }
            Later();
            _client.TickEquipmentCraftRecovery();
            Assert.That(Writes.Count, Is.EqualTo(4));
            _client.RecoverEquipmentCraft();
            Assert.That(Writes.Count, Is.EqualTo(5));
            Assert.That(LastWrite, Is.EqualTo(original));
        }

        [Test]
        public void PendingCraftBlocksEveryExistingAssetActionIncludingAfterTimeout()
        {
            LoadBag();
            Commit();
            Writes.Last().FailWith("timeout");
            int calls = _net.Calls.Count;
            _client.EquipItem(ItemId);
            _client.EquipItem(ItemId, 3);
            _client.UnequipItem(3);
            _client.EnhanceEquip(ItemId);
            _client.InlayGem(ItemId, 0, 1);
            _client.RemoveGem(ItemId, 0);
            _client.SortBag();
            _client.RetrieveAllTemporaryItems();
            _client.AcceptMission(1, 1);
            _client.ClaimMissionReward(1, 1);
            _client.ClaimActivityVitalityReward(1, 1, 1, 1);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
        }

        [Test]
        public void OtherAssetWriteInFlightBlocksCraftCommit()
        {
            LoadBag();
            var preview = LoadPreview();
            _client.SortBag();
            Assert.That(_client.BusySort, Is.True);
            _client.CommitEquipmentCraft(preview);
            Assert.That(Writes, Is.Empty);
            Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void CharacterAndConnectionSwitchCannotApplyOldReceipt()
        {
            Commit();
            var call = Writes.Last();
            var receipt = Receipt();
            _net.PlayerId = Player + 1;
            _connection = new object();
            _client.TickEquipmentCraftRecovery();
            call.Respond(receipt);
            Assert.That(_client.CharacterBag, Is.Null);
            Assert.That(_applied, Is.Empty);
            Assert.That(_store.Load(Player), Is.Not.Empty);
            Assert.That(_store.Load(Player + 1), Is.Empty);
            Assert.That(_client.HasPendingEquipmentCraft, Is.False);
            Assert.That(_client.BusyEquipmentCraft, Is.False);
        }

        [Test]
        public void NewConnectionWithoutDisconnectEventRecoversBusyOriginalRequest()
        {
            Commit();
            var original = LastWrite.Clone();
            var stale = Writes.Last();
            var receipt = Receipt();
            _connection = new object();
            Later();
            _client.TickEquipmentCraftRecovery();
            Assert.That(Writes.Count, Is.EqualTo(2));
            Assert.That(LastWrite, Is.EqualTo(original));
            stale.Respond(receipt);
            Assert.That(_client.BusyEquipmentCraft, Is.True);
            Assert.That(_applied, Is.Empty);
            Writes.Last().Respond(receipt);
            Assert.That(_applied.Count, Is.EqualTo(1));
        }

        [Test]
        public void RulesReadDuringWriteCannotRestoreOldRevisionAfterReceipt()
        {
            Commit();
            var write = Writes.Last();
            var receipt = Receipt();
            _client.RequestEquipmentCraftRules();
            var read = Last;
            write.Respond(receipt);
            read.Respond(new GetEquipmentCraftRulesResponse { RulesFingerprint = ByteString.CopyFromUtf8("old"), ForgeRevision = 0 });
            Assert.That(_client.EquipmentCraftRules, Is.Null);
            Assert.That(_client.EquipmentCraftRulesLoading, Is.False);
        }

        [Test]
        public void ReentrantRoleChangeAfterSuccessSuppressesOldSuccessEvent()
        {
            Commit();
            bool change = true;
            _client.OnChanged += () =>
            {
                if (!change || _client.HasPendingEquipmentCraft) return;
                change = false;
                _net.PlayerId = Player + 1;
                _client.RequestBag();
            };
            Writes.Last().Respond(Receipt());
            Assert.That(_applied, Is.Empty);
            Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void PolishAffixForwardsExpectedPityCharmBoundAndProxyAndRejectsWrongItemReceipt()
        {
            _client.PreviewPolishEquipAffix(ItemId, 1, useGuaranteeCharm: true, allowBoundMaterials: true,
                proxyMasterPlayerId: 5001UL, expectedPolishPityCount: 7u);
            var choice = ((PreviewEquipmentCraftRequest)Last.Request).Choice;
            Assert.That(choice.Operation, Is.EqualTo(EquipmentCraftChoice.Types.Operation.PolishAffix));
            Assert.That(choice.ItemId, Is.EqualTo(ItemId));
            Assert.That(choice.AffixSlot, Is.EqualTo(1u));
            Assert.That(choice.AffixId, Is.Zero);
            Assert.That(choice.UseGuaranteeCharm, Is.True);
            Assert.That(choice.AllowBoundMaterials, Is.True);
            Assert.That(choice.ProxyMasterPlayerId, Is.EqualTo(5001UL));
            Assert.That(choice.HasExpectedPolishPityCount, Is.True);
            Assert.That(choice.ExpectedPolishPityCount, Is.EqualTo(7u));

            Last.Respond(new PreviewEquipmentCraftResponse { Preview = Preview(choice) });
            _client.CommitEquipmentCraft(_client.CraftPreview);
            Assert.That(LastWrite.Choice, Is.EqualTo(choice));

            // 返回错误的 item_id 不得误清未决打磨意图。
            var wrong = Receipt();
            wrong.ItemId = ItemId + 1;
            Writes.Last().Respond(wrong);
            Assert.That(_client.HasPendingEquipmentCraft, Is.True);
            Assert.That(_applied, Is.Empty);

            Later();
            _client.RecoverEquipmentCraft();
            var ok = Receipt();
            ok.ResultQualityTier = 5;
            ok.ResultPolishPityCount = 0;
            ok.GuaranteedByPity = true;
            Writes.Last().Respond(ok);
            Assert.That(_client.HasPendingEquipmentCraft, Is.False);
            Assert.That(_applied.Single().ResultQualityTier, Is.EqualTo(5u));
            Assert.That(_applied.Single().GuaranteedByPity, Is.True);
        }

        [Test]
        public void ReforgeAffixValidatesParametersAndSurfacesChineseTips()
        {
            // 非法组合（重铸不允许带保底符或代打宗师）在发包前直接拦截。
            _client.PreviewEquipmentCraft(new EquipmentCraftChoice
            {
                Operation = EquipmentCraftChoice.Types.Operation.ReforgeAffix,
                ItemId = ItemId, AffixSlot = 1, AffixId = 102, UseGuaranteeCharm = true,
            });
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.EquipmentCraftError, Is.Not.Empty);

            _client.PreviewReforgeEquipAffix(ItemId, 1, 102, allowBoundMaterials: true);
            var choice = ((PreviewEquipmentCraftRequest)Last.Request).Choice;
            Assert.That(choice.Operation, Is.EqualTo(EquipmentCraftChoice.Types.Operation.ReforgeAffix));
            Assert.That(choice.AllowBoundMaterials, Is.True);
            Last.Respond(new PreviewEquipmentCraftResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipBindConfirmRequired },
            });
            Assert.That(_client.EquipmentCraftError, Does.Contain("二次确认"));
            Assert.That(PlayerFeaturesClient.DescribeEquipTip("打磨",
                new TipInfoMessage { Id = (uint)equip_error.KEquipAffixPolishExempt }), Does.Contain("固定满值"));
            Assert.That(PlayerFeaturesClient.DescribeEquipTip("打磨",
                new TipInfoMessage { Id = (uint)equip_error.KEquipProxyMasterUnavailable }), Does.Contain("代打摊位"));
        }
    }
}
