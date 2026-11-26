using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Gameplay;
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
                UnlockItemConfigId = EquipLoadoutModel.DefaultUnlockItemConfigId,
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
                if (id == 1u || (id == 2u && unlocked >= 2u))
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
            var refs = EquipLoadoutModel.ReferencedSchemesOf(snap, 5001UL);
            Assert.That(EquipLoadoutModel.FormatInUseBadgeText(refs), Is.EqualTo("方案一+1"));
            Assert.That(EquipLoadoutModel.FormatItemProtectionWarning(refs, "解封剥离"), Does.Contain("方案一、方案二"));
            Assert.That(EquipLoadoutModel.ReferencedSchemesOf(snap, 9999UL).Count, Is.EqualTo(0));
            Assert.That(EquipLoadoutModel.ValidateNameDraft("一二三四五六七八九十甲乙丙", out _), Is.False);
            Assert.That(EquipLoadoutModel.ValidateNameDraft("PVP高速控场", out _), Is.True);

            // 第 3 套不可用绑定灵石，仅非绑 50 < 120 -> 不可解锁
            Assert.That(EquipLoadoutModel.CanAffordNextUnlock(snap, useBindDiamondFirst: true), Is.False);
            // 持有 1 枚扩容符 3413 -> 优先消耗扩容符可解锁
            snap.UnlockItemOwned = 1UL;
            Assert.That(EquipLoadoutModel.CanAffordNextUnlock(snap, useBindDiamondFirst: false), Is.True);
            Assert.That(EquipLoadoutModel.FormatUnlockTalismanLine(snap), Does.Contain("持有 1"));

            // 第 2 套允许优先扣绑定灵石 + 非绑补足
            var slot2 = MakeSnapshot(unlocked: 1u, active: 1u, diamond: 20UL, bindDiamond: 40UL, talismans: 0UL);
            Assert.That(EquipLoadoutModel.CanAffordNextUnlock(slot2, useBindDiamondFirst: true), Is.True);
            Assert.That(EquipLoadoutModel.CanAffordNextUnlock(slot2, useBindDiamondFirst: false), Is.False);
        }

        [Test]
        public void SwitchLoadoutUpdatesBothBagsSnapshotAndMissingNotices()
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
            string missingNotice = EquipLoadoutModel.FormatMissingSlotsNotice(_client.LastEquipLoadoutMissingSlots, null);
            Assert.That(missingNotice, Does.Contain("首饰二"));
            Assert.That(_client.LastEquipLoadoutLinkedNotices[0], Does.Contain("冷却中"));
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
            firstCall.FailWith("rpc timeout");
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

        [Test]
        public void IconKeyMapsTalisman3413AndSaveDropsStaleConnectionCallbacks()
        {
            var talisman = new BagItemInfo { ConfigId = 3413u, EquipKind = 0u };
            Assert.That(EquipmentScreenModel.IconKey(talisman), Is.EqualTo(EquipLoadoutModel.ExpandTalismanIconKey));

            _client.RequestEquipLoadouts();
            _net.Calls.Last().Respond(new GetEquipLoadoutsResponse { Snapshot = MakeSnapshot(unlocked: 2u, active: 1u, version: 9UL) });

            _client.SaveEquipLoadout(2u, "PVP控场套", 2u, captureCurrentEquipped: false,
                explicitSlots: new[] { new EquipLoadoutSlotBinding { Slot = 3u, ItemGuid = 5001UL } }, linkedSkillSchemeId: 2u, linkedAttributeSchemeId: 1u);
            var saveCall = _net.CallsOf(SceneBagClientPlayerRpc.SaveEquipLoadoutMessageId).Last();
            var saveReq = (SaveEquipLoadoutRequest)saveCall.Request;
            Assert.That(saveReq.SchemeId, Is.EqualTo(2u));
            Assert.That(saveReq.Name, Is.EqualTo("PVP控场套"));
            Assert.That(saveReq.RoleTag, Is.EqualTo(2u));
            Assert.That(saveReq.CaptureCurrentEquipped, Is.False);
            Assert.That(saveReq.Slots.Count, Is.EqualTo(1));
            Assert.That(saveReq.StateVersion, Is.EqualTo(9UL));

            // 模拟切连接代次后才收到旧回包：必须丢弃，绝不覆盖新会话状态
            _connection = new object();
            _client.RequestEquipLoadouts();
            saveCall.Respond(new SaveEquipLoadoutResponse
            {
                Snapshot = MakeSnapshot(unlocked: 2u, active: 2u, version: 10UL),
                RequestId = saveReq.RequestId,
            });
            Assert.That(_client.EquipLoadouts, Is.Null);
        }

        [Test]
        public void PreBattleBarStateEnforcesPhaseGateMissingSlotsAndLockedHints()
        {
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.None), Is.True);
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.Queued), Is.True);
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.Preparing), Is.False);
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.WaitingAction), Is.False);
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.Resolving), Is.False);

            // 未同步且非加载中：可触发刷新
            var unsynced = EquipLoadoutModel.BuildPreBattleBarState(
                null, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.None, string.Empty);
            Assert.That(unsynced.Synced, Is.False);
            Assert.That(unsynced.Slots.Count, Is.EqualTo(5));
            Assert.That(unsynced.Slots[0].ClickKind, Is.EqualTo(PreBattleLoadoutClickKind.Refresh));
            Assert.That(unsynced.Slots[0].CanClick, Is.True);

            var snap = MakeSnapshot(unlocked: 2u, active: 1u, diamond: 100UL, bindDiamond: 60UL, talismans: 0UL);
            snap.Schemes[1].Name = "PVP控场";
            snap.Schemes[1].MissingCount = 2u;

            // 排队等待期（Queued）仍未入房，允许一键切换未激活已解锁方案
            var queued = EquipLoadoutModel.BuildPreBattleBarState(
                snap, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.Queued, string.Empty);
            Assert.That(queued.Synced, Is.True);
            Assert.That(queued.BlockedByBattleState, Is.False);
            Assert.That(queued.Slots[0].Active, Is.True);
            Assert.That(queued.Slots[0].CanClick, Is.False);
            Assert.That(queued.Slots[0].SpriteKey, Is.EqualTo(EquipLoadoutModel.TabActiveKey));
            Assert.That(queued.Slots[1].CanClick, Is.True);
            Assert.That(queued.Slots[1].ClickKind, Is.EqualTo(PreBattleLoadoutClickKind.Switch));
            Assert.That(queued.Slots[1].SpriteKey, Is.EqualTo(EquipLoadoutModel.TabUnlockedKey));
            Assert.That(queued.Slots[1].Label, Does.Contain("缺2"));
            Assert.That(queued.Slots[2].Unlocked, Is.False);
            Assert.That(queued.Slots[2].CanClick, Is.True);
            Assert.That(queued.Slots[2].ClickKind, Is.EqualTo(PreBattleLoadoutClickKind.UnlockHint));
            Assert.That(queued.Slots[2].SpriteKey, Is.EqualTo(EquipLoadoutModel.TabLockedKey));

            // 匹配成局进入 Preparing 后全部置灰锁死
            var preparing = EquipLoadoutModel.BuildPreBattleBarState(
                snap, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.Preparing, string.Empty);
            Assert.That(preparing.BlockedByBattleState, Is.True);
            Assert.That(preparing.Slots.All(s => !s.CanClick), Is.True);
            Assert.That(preparing.StatusText, Does.Contain("锁定"));

            // 待确认解锁时禁止切换方案
            var pendingUnlock = EquipLoadoutModel.BuildPreBattleBarState(
                snap, loading: false, switching: false, hasPendingUnlock: true, BattlePhase.None, string.Empty);
            Assert.That(pendingUnlock.Slots[1].CanClick, Is.False);
            Assert.That(pendingUnlock.StatusIsError, Is.True);

            // 未解锁轻提示与签名变更校验
            string lockHint = EquipLoadoutModel.FormatLockedPreBattleHint(3u, snap);
            Assert.That(lockHint, Does.Contain("方案三"));
            Assert.That(lockHint, Does.Contain("120 灵石"));
            int sigQueued = EquipLoadoutModel.ComputePreBattleBarSignature(queued);
            int sigQueuedAgain = EquipLoadoutModel.ComputePreBattleBarSignature(
                EquipLoadoutModel.BuildPreBattleBarState(
                    snap, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.Queued, string.Empty));
            int sigPrep = EquipLoadoutModel.ComputePreBattleBarSignature(preparing);
            Assert.That(sigQueuedAgain, Is.EqualTo(sigQueued));
            Assert.That(sigPrep, Is.Not.EqualTo(sigQueued));
        }

        [Test]
        public void PreBattleQuickBarHelperRequestsSnapshotSwitchesAndFormatsToast()
        {
            EquipLoadoutModel.EnsureEquipLoadoutsRequested(_client);
            Assert.That(_net.CallsOf(SceneBagClientPlayerRpc.GetEquipLoadoutsMessageId).Count(), Is.EqualTo(1));
            // 加载中重复调用不得重发
            EquipLoadoutModel.EnsureEquipLoadoutsRequested(_client);
            Assert.That(_net.CallsOf(SceneBagClientPlayerRpc.GetEquipLoadoutsMessageId).Count(), Is.EqualTo(1));

            _net.CallsOf(SceneBagClientPlayerRpc.GetEquipLoadoutsMessageId).Last()
                .Respond(new GetEquipLoadoutsResponse { Snapshot = MakeSnapshot(unlocked: 3u, active: 1u, version: 7UL) });

            var before = EquipLoadoutModel.BuildPreBattleBarState(_client, BattlePhase.None);
            Assert.That(before.ActiveSchemeId, Is.EqualTo(1u));
            Assert.That(before.Slots[1].CanClick, Is.True);

            _client.SwitchEquipLoadout(2u, applyLinkedSchemes: true);
            var inFlight = EquipLoadoutModel.BuildPreBattleBarState(_client, BattlePhase.None);
            Assert.That(inFlight.Switching, Is.True);
            Assert.That(inFlight.Slots[1].CanClick, Is.False);

            var call = _net.CallsOf(SceneBagClientPlayerRpc.SwitchEquipLoadoutMessageId).Last();
            var req = (SwitchEquipLoadoutRequest)call.Request;
            var resp = new SwitchEquipLoadoutResponse
            {
                Bag = MakeBag(PlayerFeaturesClient.CharacterBagType, 7101UL),
                Equipment = MakeBag(PlayerFeaturesClient.EquipmentBagType, 7102UL),
                Snapshot = MakeSnapshot(unlocked: 3u, active: 2u, version: 8UL),
                RequestId = req.RequestId,
            };
            resp.MissingSlots.Add(9u);
            resp.LinkedNotices.Add("已同步切换至战法方案 2");
            call.Respond(resp);

            var after = EquipLoadoutModel.BuildPreBattleBarState(_client, BattlePhase.None);
            Assert.That(after.Switching, Is.False);
            Assert.That(after.ActiveSchemeId, Is.EqualTo(2u));
            Assert.That(after.Slots[1].Active, Is.True);
            Assert.That(after.Slots[1].CanClick, Is.False);
            Assert.That(after.Slots[0].CanClick, Is.True);

            string toast = EquipLoadoutModel.BuildSwitchToast(resp);
            Assert.That(toast, Does.Contain("方案二"));
            Assert.That(toast, Does.Contain("法宝一"));
            Assert.That(toast, Does.Contain("战法方案 2"));
        }
    }
}
