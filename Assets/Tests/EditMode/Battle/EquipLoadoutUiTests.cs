using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class EquipLoadoutUiTests
    {
        private const ulong Player = 9201UL;
        private string _directory;
        private DurableRequestFileStore _unlockStore;
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private object _connection;
        private long _now;
        private readonly List<SwitchEquipLoadoutResponse> _switched = new();
        private readonly List<UnlockEquipLoadoutResponse> _unlocked = new();

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "equip-loadout-test-" + Guid.NewGuid().ToString("N"));
            _unlockStore = new DurableRequestFileStore(Path.Combine(_directory, "unlock"));
            _net = new FakeBattleTransport { IsReady = true, PlayerId = Player };
            _connection = new object();
            _now = 10000L;
            _switched.Clear();
            _unlocked.Clear();
            CreateClient();
        }

        private void CreateClient()
        {
            _client = new PlayerFeaturesClient(_net, () => _connection)
            {
                EquipLoadoutUnlockRecovery = _unlockStore,
                NowMs = () => _now,
            };
            _client.OnEquipLoadoutSwitched += _switched.Add;
            _client.OnEquipLoadoutUnlocked += _unlocked.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private static EquipLoadoutSnapshot MakeSnapshot(uint unlocked = 1u, uint active = 1u, ulong version = 3UL,
            ulong diamond = 100UL, ulong bindDiamond = 60UL, ulong talismans = 0UL)
        {
            var snap = new EquipLoadoutSnapshot
            {
                UnlockedSchemeCount = unlocked,
                MaxSchemeCount = 5u,
                ActiveSchemeId = active,
                StateVersion = version,
                NextUnlockCostDiamond = unlocked < 5u ? unlocked * 60UL : 0UL,
                NextUnlockAllowBindDiamond = unlocked == 1u,
                UnlockItemConfigId = EquipLoadoutModel.ExpandTalismanConfigId,
                UnlockItemOwned = talismans,
                DiamondBalance = diamond,
                BindDiamondBalance = bindDiamond,
            };
            for (uint id = 1u; id <= 5u; ++id)
            {
                var scheme = new EquipLoadoutSchemeInfo
                {
                    SchemeId = id,
                    Unlocked = id <= unlocked,
                    Name = id == 1u ? "PVE暴伤" : string.Empty,
                    RoleTag = id == 1u ? 1u : 0u,
                    LinkedSkillSchemeId = id == 1u ? 2u : 0u,
                    LinkedAttributeSchemeId = id == 1u ? 1u : 0u,
                };
                if (id == 1u)
                {
                    scheme.Slots.Add(new EquipLoadoutSlotView
                    {
                        Slot = 3u,
                        SlotName = "武器",
                        EquipKind = 11u,
                        ItemGuid = 5001UL,
                        Status = EquipLoadoutSlotStatus.EquipLoadoutSlotEquipped,
                    });
                }
                if (id == 2u && unlocked >= 2u)
                {
                    scheme.Slots.Add(new EquipLoadoutSlotView
                    {
                        Slot = 3u,
                        SlotName = "武器",
                        EquipKind = 11u,
                        ItemGuid = 5001UL,
                        Status = EquipLoadoutSlotStatus.EquipLoadoutSlotEquipped,
                    });
                }
                snap.Schemes.Add(scheme);
            }
            return snap;
        }

        private static BagInfo MakeBag(uint bagType, ulong itemId)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = bagType, Capacity = 30, CanSort = bagType == 0u } };
            bag.Items.Add(new BagItemInfo { ItemId = itemId, ConfigId = 2001u, Count = 1u });
            return bag;
        }

        [Test]
        public void ModelReferenceMapAndUnlockAffordabilityMatchRules()
        {
            var snap = MakeSnapshot(unlocked: 2u, active: 1u, diamond: 50UL, bindDiamond: 200UL, talismans: 0UL);
            Assert.That(EquipLoadoutModel.FormatItemLoadoutBadge(snap, 5001UL), Is.EqualTo("方案1/2"));
            Assert.That(EquipLoadoutModel.FormatItemLoadoutBadge(snap, 9999UL), Is.EqualTo(string.Empty));
            Assert.That(EquipLoadoutModel.SanitizeSchemeName("  一二三四五六七八九十甲乙丙丁  "), Is.EqualTo("一二三四五六七八九十甲乙"));

            // 第 3 套不可用绑定灵石，仅非绑 50 < 120 -> 不可解锁
            Assert.That(EquipLoadoutModel.CanAffordNextUnlock(snap, useBindDiamondFirst: true), Is.False);
            // 持有 1 枚扩容符 3413 -> 优先消耗扩容符可解锁
            snap.UnlockItemOwned = 1UL;
            Assert.That(EquipLoadoutModel.CanAffordNextUnlock(snap, useBindDiamondFirst: false), Is.True);
            Assert.That(EquipLoadoutModel.FormatUnlockCostSummary(snap, false), Does.Contain("装备方案扩容符 ×1"));

            // 第 2 套允许优先扣绑定灵石 + 非绑补足
            var slot2 = MakeSnapshot(unlocked: 1u, active: 1u, diamond: 20UL, bindDiamond: 40UL, talismans: 0UL);
            Assert.That(EquipLoadoutModel.CanAffordNextUnlock(slot2, useBindDiamondFirst: true), Is.True);
            Assert.That(EquipLoadoutModel.CanAffordNextUnlock(slot2, useBindDiamondFirst: false), Is.False);
        }

        [Test]
        public void SwitchLoadoutUpdatesBothBagsSnapshotAndBuildsNotice()
        {
            _client.RequestEquipLoadouts();
            _net.Calls.Last().Respond(new GetEquipLoadoutsResponse { Snapshot = MakeSnapshot(unlocked: 2u, active: 1u, version: 5UL) });
            Assert.That(_client.EquipLoadouts, Is.Not.Null);

            _client.SwitchEquipLoadout(2u, applyLinkedSchemes: true);
            var call = _net.CallsOf(SceneBagClientPlayerRpc.SwitchEquipLoadoutMessageId).Last();
            var req = (SwitchEquipLoadoutRequest)call.Request;
            Assert.That(req.SchemeId, Is.EqualTo(2u));
            Assert.That(req.ApplyLinkedSchemes, Is.True);
            Assert.That(req.StateVersion, Is.EqualTo(5UL));
            Assert.That(req.RequestId.Length, Is.GreaterThanOrEqualTo(16));

            var resp = new SwitchEquipLoadoutResponse
            {
                Bag = MakeBag(PlayerFeaturesClient.CharacterBagType, 7001UL),
                Equipment = MakeBag(PlayerFeaturesClient.EquipmentBagType, 7002UL),
                Snapshot = MakeSnapshot(unlocked: 2u, active: 2u, version: 6UL),
                RequestId = req.RequestId,
            };
            resp.MissingSlots.Add(8u);
            resp.LinkedNotices.Add("属性加点方案处于冷却中，本次仅切换了装备");
            call.Respond(resp);

            Assert.That(_client.CharacterBag.Items[0].ItemId, Is.EqualTo(7001UL));
            Assert.That(_client.Equipment.Items[0].ItemId, Is.EqualTo(7002UL));
            Assert.That(_client.EquipLoadouts.ActiveSchemeId, Is.EqualTo(2u));
            Assert.That(_switched.Count, Is.EqualTo(1));
            string notice = EquipLoadoutModel.BuildSwitchResultNotice(_switched[0]);
            Assert.That(notice, Does.Contain("方案二"));
            Assert.That(notice, Does.Contain("1 个槽位装备不在背包"));
            Assert.That(notice, Does.Contain("冷却中"));
        }

        [Test]
        public void UnlockLoadoutRetainsDurableKeyUntilConfirmedAndReplaysSameKey()
        {
            _client.RequestEquipLoadouts();
            _net.Calls.Last().Respond(new GetEquipLoadoutsResponse { Snapshot = MakeSnapshot(unlocked: 1u, active: 1u, diamond: 100UL, bindDiamond: 60UL) });

            _client.UnlockEquipLoadout(useBindDiamondFirst: true);
            var firstCall = _net.CallsOf(SceneBagClientPlayerRpc.UnlockEquipLoadoutMessageId).Last();
            var firstReq = (UnlockEquipLoadoutRequest)firstCall.Request;
            Assert.That(firstReq.ClientRequestId, Is.Not.EqualTo(0UL));
            Assert.That(firstReq.ExpectedUnlockedCount, Is.EqualTo(1u));
            Assert.That(firstReq.UseBindDiamondFirst, Is.True);
            Assert.That(_client.HasPendingEquipLoadoutUnlock, Is.True);

            // 模拟首包超时：本地持久记录必须保留，重连后按原幂等键恢复
            firstCall.Fail("rpc timeout");
            Assert.That(_client.HasPendingEquipLoadoutUnlock, Is.True);
            Assert.That(_unlocked.Count, Is.EqualTo(0));

            _client.Dispose();
            _connection = new object();
            _now += 5000L;
            CreateClient();
            _client.TickEquipLoadoutUnlockRecovery();

            var retryCall = _net.CallsOf(SceneBagClientPlayerRpc.UnlockEquipLoadoutMessageId).Last();
            var retryReq = (UnlockEquipLoadoutRequest)retryCall.Request;
            Assert.That(retryReq.ClientRequestId, Is.EqualTo(firstReq.ClientRequestId));
            Assert.That(retryReq.ExpectedUnlockedCount, Is.EqualTo(1u));

            retryCall.Respond(new UnlockEquipLoadoutResponse
            {
                ClientRequestId = retryReq.ClientRequestId,
                Outcome = UnlockEquipLoadoutResponse.Types.Outcome.Applied,
                Durable = true,
                Replayed = true,
                ResultUnlockedCount = 2u,
                CurrentUnlockedCount = 2u,
                SpentBindDiamond = 60UL,
                Bag = MakeBag(PlayerFeaturesClient.CharacterBagType, 8001UL),
                Snapshot = MakeSnapshot(unlocked: 2u, active: 1u, version: 4UL, diamond: 100UL, bindDiamond: 0UL),
            });

            Assert.That(_client.HasPendingEquipLoadoutUnlock, Is.False);
            Assert.That(_client.EquipLoadouts.UnlockedSchemeCount, Is.EqualTo(2u));
            Assert.That(_unlocked.Count, Is.EqualTo(1));
        }
    }
}
