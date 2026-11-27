using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.Skills;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Skills;
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

        private static EquipLoadoutSnapshot MakeSnapshot(
            uint free = 2u,
            uint cardBonus = 0u,
            uint rented = 0u,
            uint active = 1u,
            ulong version = 3UL,
            ulong diamond = 120UL,
            ulong bindDiamond = 60UL,
            ulong serverNowSec = 1000000UL,
            ulong rentExpiresAtSec = 0UL,
            bool savedInScheme5 = false)
        {
            uint activeRented = rentExpiresAtSec > serverNowSec ? rented : 0u;
            uint unlocked = Math.Min(EquipLoadoutModel.MaxSchemes, free + cardBonus + activeRented);
            var snap = new EquipLoadoutSnapshot
            {
                UnlockedSchemeCount = unlocked,
                MaxSchemeCount = EquipLoadoutModel.MaxSchemes,
                ActiveSchemeId = active,
                StateVersion = version,
                UnlockItemConfigId = EquipLoadoutModel.DefaultUnlockItemConfigId,
                DiamondBalance = diamond,
                BindDiamondBalance = bindDiamond,
                FreeSchemes = free,
                LightCardBonusSchemes = cardBonus,
                RentedExtraSlots = activeRented,
                RentExpiresAtSec = rentExpiresAtSec,
                ServerNowSec = serverNowSec,
                RentUnitPriceDiamond = SkillLoadoutRentPlanner.RentUnitPriceDiamond,
                RentPeriodDays = SkillLoadoutRentPlanner.RentPeriodDays,
                RentMaxRemainingDays = SkillLoadoutRentPlanner.RentMaxRemainingDays,
            };
            for (uint id = 1u; id <= EquipLoadoutModel.MaxSchemes; ++id)
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
                if (id == 1u || id == 2u || (id == 5u && savedInScheme5))
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
        public void ModelReferenceMapProtectsLockedSavedSchemesAndProjectsUnifiedRentView()
        {
            // 方案 5 已到期锁定但仍保留武器 5001 引用（到期只锁不删），防误卖必须覆盖
            var snap = MakeSnapshot(free: 2u, cardBonus: 2u, rented: 0u, active: 1u, diamond: 120UL,
                serverNowSec: 1000000UL, rentExpiresAtSec: 0UL, savedInScheme5: true);
            var refs = EquipLoadoutModel.ReferencedSchemesOf(snap, 5001UL);
            Assert.That(refs, Is.EqualTo(new[] { "方案一", "方案二", "方案五(锁)" }));
            Assert.That(EquipLoadoutModel.FormatInUseBadgeText(refs), Is.EqualTo("方案一+2"));
            Assert.That(EquipLoadoutModel.FormatItemProtectionWarning(refs, "解封剥离"),
                Does.Contain("方案一、方案二、方案五(锁)"));
            Assert.That(EquipLoadoutModel.ReferencedSchemesOf(snap, 9999UL).Count, Is.EqualTo(0));
            Assert.That(EquipLoadoutModel.ValidateNameDraft("一二三四五六七八九十甲乙丙", out _), Is.False);
            Assert.That(EquipLoadoutModel.ValidateNameDraft("PVP高速控场", out _), Is.True);

            // 席位来源标签：1..2 免、3..4 卡、5..7 锁（若有有效租期则租）
            Assert.That(EquipLoadoutModel.SchemeSeatTag(snap, 1u, true), Is.EqualTo("免"));
            Assert.That(EquipLoadoutModel.SchemeSeatTag(snap, 3u, true), Is.EqualTo("卡"));
            Assert.That(EquipLoadoutModel.SchemeSeatTag(snap, 5u, false), Is.EqualTo("锁"));
            Assert.That(EquipLoadoutModel.HasSavedBindings(snap.Schemes[4]), Is.True);
            Assert.That(EquipLoadoutModel.HasSavedBindings(snap.Schemes[5]), Is.False);

            // 投影至 SkillLoadoutRentPlanner 统一试算：2免 + 2卡 = 4套，最多再租 3 套封顶 7 套；+2套×30天 = 60 真实灵石
            var view = EquipLoadoutModel.ToUnifiedRentView(snap);
            Assert.That(view.UnlockedSchemes, Is.EqualTo(4u));
            Assert.That(view.MaxRentableExtraSlots, Is.EqualTo(3u));
            var quote = SkillLoadoutRentPlanner.Plan(view, targetRentedSlots: 2u, addPeriods: 1u);
            Assert.That(quote.Valid, Is.True);
            Assert.That(quote.TotalCost, Is.EqualTo(60UL));
            Assert.That(quote.NewUnlockedSchemes, Is.EqualTo(6u));
            var overCap = SkillLoadoutRentPlanner.Plan(view, targetRentedSlots: 4u, addPeriods: 1u);
            Assert.That(overCap.Valid, Is.False);
        }

        [Test]
        public void SwitchLoadoutUpdatesBothBagsSnapshotAndMissingNotices()
        {
            _client.RequestEquipLoadouts();
            _net.Calls.Last().Respond(new GetEquipLoadoutsResponse { Snapshot = MakeSnapshot(free: 2u, active: 1u, version: 5UL) });
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
                Snapshot = MakeSnapshot(free: 2u, active: 2u, version: 6UL),
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
        public void LegacyUnlockFailsClosedLocallyAndPendingRecoveryClearsOnServerRejection()
        {
            _client.RequestEquipLoadouts();
            _net.Calls.Last().Respond(new GetEquipLoadoutsResponse { Snapshot = MakeSnapshot(free: 2u, active: 1u) });

            // 方案 A：旧单独买断入口本地直接 fail-closed，不发扣费包也不写待恢复记录
            _client.UnlockEquipLoadout(useBindDiamondFirst: true);
            Assert.That(_net.CallsOf(SceneBagClientPlayerRpc.UnlockEquipLoadoutMessageId).Count(), Is.EqualTo(0));
            Assert.That(_client.HasPendingEquipLoadoutUnlock, Is.False);
            Assert.That(_client.EquipLoadoutError, Does.Contain("7 套三合一共享"));

            // 兼容老版本升级遗留的待恢复文件：恢复上报后服务端 Rejected 时自动安全清理本地记录
            var legacyReq = new UnlockEquipLoadoutRequest { ClientRequestId = 778899UL, ExpectedUnlockedCount = 1u };
            _unlockStore.Save(Player, Convert.ToBase64String(legacyReq.ToByteArray()));
            _client.Dispose();
            _connection = new object();
            _now += 5000L;
            CreateClient();
            _client.TickEquipLoadoutUnlockRecovery();
            Assert.That(_client.HasPendingEquipLoadoutUnlock, Is.True);

            var retryCall = _net.CallsOf(SceneBagClientPlayerRpc.UnlockEquipLoadoutMessageId).Last();
            var sent = (UnlockEquipLoadoutRequest)retryCall.Request;
            Assert.That(sent.ClientRequestId, Is.EqualTo(778899UL));
            retryCall.Respond(new UnlockEquipLoadoutResponse
            {
                ClientRequestId = sent.ClientRequestId,
                Outcome = UnlockEquipLoadoutResponse.Types.Outcome.Rejected,
                Durable = true,
                ErrorMessage = new TipInfoMessage { Id = 1005u },
                Snapshot = MakeSnapshot(free: 2u, active: 1u, version: 4UL),
            });
            Assert.That(_client.HasPendingEquipLoadoutUnlock, Is.False);
            Assert.That(_unlockStore.Load(Player), Is.EqualTo(string.Empty));
            Assert.That(_unlocked.Count, Is.EqualTo(0));
        }

        private sealed class FakeRpcChannelAdapter : IRpcChannel
        {
            private readonly FakeBattleTransport _transport;
            public FakeRpcChannelAdapter(FakeBattleTransport transport) => _transport = transport;
            public void SendOneWay(uint id, IMessage request) => _transport.SendOneWay(id, request);
            public void RegisterNotify(uint id, Action<MessageContent> action) => _transport.RegisterNotify(id, action);
            public void CallMethod<T>(uint id, IMessage request, MessageParser<T> parser, RpcDone<T> done)
                where T : IMessage<T>
                => _transport.Call(id, request, parser, resp => done(RpcStatus.Ok, resp), err => done(RpcStatus.Timeout(err), default));
        }

        [Test]
        public void SkillBookRentLoadoutSlotsWorksDirectlyFromEquipmentWindowAndFiresEvent()
        {
            using var skills = new SkillBookClient(_net, new FakeRpcChannelAdapter(_net), () => _connection, _ => null);
            RentSkillLoadoutSlotsResponse rentedEvent = null;
            skills.LoadoutSlotsRented += resp => rentedEvent = resp;

            bool? doneOk = null;
            string doneMsg = null;
            // 未先打开技能书加载 State.Snapshot，也必须允许直接从装备窗发起统一按套租用
            skills.RentLoadoutSlots(targetRentedSlots: 2u, addPeriods: 1u, expectedCostDiamond: 60UL, (ok, msg) =>
            {
                doneOk = ok;
                doneMsg = msg;
            });

            var call = _net.CallsOf(SceneSkillClientPlayerRpc.RentSkillLoadoutSlotsMessageId).Last();
            var req = (RentSkillLoadoutSlotsRequest)call.Request;
            Assert.That(req.TargetRentedSlots, Is.EqualTo(2u));
            Assert.That(req.AddPeriods, Is.EqualTo(1u));
            Assert.That(req.ExpectedCostDiamond, Is.EqualTo(60UL));

            call.Respond(new RentSkillLoadoutSlotsResponse
            {
                ClientRequestId = req.ClientRequestId,
                RentedExtraSlots = 2u,
                SpentDiamond = 60UL,
                RentExpiresAtSec = 1000000UL + 30UL * 86400UL,
            });

            Assert.That(doneOk, Is.True);
            Assert.That(doneMsg, Does.Contain("装备+战法+加点共享"));
            Assert.That(rentedEvent, Is.Not.Null);
            Assert.That(rentedEvent.RentedExtraSlots, Is.EqualTo(2u));
        }

        [Test]
        public void IconKeyMapsTalisman3413AndSaveDropsStaleConnectionCallbacks()
        {
            var talisman = new BagItemInfo { ConfigId = 3413u, EquipKind = 0u };
            Assert.That(EquipmentScreenModel.IconKey(talisman), Is.EqualTo(EquipLoadoutModel.ExpandTalismanIconKey));

            _client.RequestEquipLoadouts();
            _net.Calls.Last().Respond(new GetEquipLoadoutsResponse { Snapshot = MakeSnapshot(free: 2u, active: 1u, version: 9UL) });

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
                Snapshot = MakeSnapshot(free: 2u, active: 2u, version: 10UL),
                RequestId = saveReq.RequestId,
            });
            Assert.That(_client.EquipLoadouts, Is.Null);
        }

        [Test]
        public void PreBattleBarStateEnforcesPhaseGateSevenSlotsAndLockedHints()
        {
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.None), Is.True);
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.Queued), Is.True);
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.Preparing), Is.False);
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.WaitingAction), Is.False);
            Assert.That(EquipLoadoutModel.IsPreBattleSwitchAllowed(BattlePhase.Resolving), Is.False);

            // 未同步且非加载中：展示 7 槽占位并可触发刷新
            var unsynced = EquipLoadoutModel.BuildPreBattleBarState(
                null, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.None, string.Empty);
            Assert.That(unsynced.Synced, Is.False);
            Assert.That(unsynced.Slots.Count, Is.EqualTo(7));
            Assert.That(unsynced.Slots[0].ClickKind, Is.EqualTo(PreBattleLoadoutClickKind.Refresh));
            Assert.That(unsynced.Slots[0].CanClick, Is.True);

            // 2 永久免费 + 2 轻享卡 + 1 租用 = 5 套解锁，方案 6..7 锁定
            var snap = MakeSnapshot(free: 2u, cardBonus: 2u, rented: 1u, active: 1u,
                serverNowSec: 1000000UL, rentExpiresAtSec: 1000000UL + 10UL * 86400UL);
            snap.Schemes[1].Name = "PVP控场";
            snap.Schemes[1].MissingCount = 2u;

            // 排队等待期（Queued）仍未入房，允许一键切换未激活已解锁方案
            var queued = EquipLoadoutModel.BuildPreBattleBarState(
                snap, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.Queued, string.Empty);
            Assert.That(queued.Synced, Is.True);
            Assert.That(queued.Slots.Count, Is.EqualTo(7));
            Assert.That(queued.BlockedByBattleState, Is.False);
            Assert.That(queued.Slots[0].Active, Is.True);
            Assert.That(queued.Slots[0].CanClick, Is.False);
            Assert.That(queued.Slots[0].SpriteKey, Is.EqualTo(EquipLoadoutModel.TabActiveKey));
            Assert.That(queued.Slots[1].CanClick, Is.True);
            Assert.That(queued.Slots[1].ClickKind, Is.EqualTo(PreBattleLoadoutClickKind.Switch));
            Assert.That(queued.Slots[1].SpriteKey, Is.EqualTo(EquipLoadoutModel.TabUnlockedKey));
            Assert.That(queued.Slots[1].Label, Does.Contain("缺2"));
            Assert.That(queued.Slots[4].Unlocked, Is.True);
            Assert.That(queued.Slots[4].SpriteKey, Is.EqualTo(EquipLoadoutModel.TabRentedKey));
            Assert.That(queued.Slots[5].Unlocked, Is.False);
            Assert.That(queued.Slots[5].CanClick, Is.True);
            Assert.That(queued.Slots[5].ClickKind, Is.EqualTo(PreBattleLoadoutClickKind.UnlockHint));
            Assert.That(queued.Slots[5].SpriteKey, Is.EqualTo(EquipLoadoutModel.TabLockedKey));

            // 匹配成局进入 Preparing 后全部置灰锁死
            var preparing = EquipLoadoutModel.BuildPreBattleBarState(
                snap, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.Preparing, string.Empty);
            Assert.That(preparing.BlockedByBattleState, Is.True);
            Assert.That(preparing.Slots.All(s => !s.CanClick), Is.True);
            Assert.That(preparing.StatusText, Does.Contain("锁定"));

            // 到期只锁不删：正在穿戴的方案 5 到期后仍保持激活标示并提示可切回已解锁方案或续期
            var expiredActive = MakeSnapshot(free: 2u, cardBonus: 0u, rented: 0u, active: 5u, savedInScheme5: true);
            var expiredBar = EquipLoadoutModel.BuildPreBattleBarState(
                expiredActive, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.None, string.Empty);
            Assert.That(expiredBar.Slots[4].Active, Is.True);
            Assert.That(expiredBar.Slots[4].Label, Does.Contain("·锁"));
            Assert.That(expiredBar.StatusText, Does.Contain("只锁不删"));

            // 窄栏紧凑标签压缩：仅未解锁非激活槽位压缩为 "+方案N"，不截断已解锁或到期只锁不删激活槽
            Assert.That(EquipLoadoutModel.FormatCompactSlotLabel(queued.Slots[5], narrow: true), Is.EqualTo("+方案6"));
            Assert.That(EquipLoadoutModel.FormatCompactSlotLabel(queued.Slots[5], narrow: false), Is.EqualTo("+ 方案六"));
            Assert.That(EquipLoadoutModel.FormatCompactSlotLabel(queued.Slots[1], narrow: true), Is.EqualTo(queued.Slots[1].Label));
            Assert.That(EquipLoadoutModel.FormatCompactSlotLabel(expiredBar.Slots[4], narrow: true), Does.Contain("·锁"));

            // 未解锁轻提示与签名变更校验（含同解锁状态但租用/免费切片变化时的签名失效）
            string lockHint = EquipLoadoutModel.FormatLockedPreBattleHint(6u, snap);
            Assert.That(lockHint, Does.Contain("方案六"));
            Assert.That(lockHint, Does.Contain("30 灵石"));
            Assert.That(lockHint, Does.Contain("三合一共享"));
            int sigQueued = EquipLoadoutModel.ComputePreBattleBarSignature(queued);
            int sigQueuedAgain = EquipLoadoutModel.ComputePreBattleBarSignature(
                EquipLoadoutModel.BuildPreBattleBarState(
                    snap, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.Queued, string.Empty));
            int sigPrep = EquipLoadoutModel.ComputePreBattleBarSignature(preparing);
            var lv85Snap = MakeSnapshot(free: 3u, cardBonus: 2u, rented: 0u, active: 1u,
                serverNowSec: 1000000UL, rentExpiresAtSec: 0UL);
            lv85Snap.Schemes[1].Name = "PVP控场";
            lv85Snap.Schemes[1].MissingCount = 2u;
            var lv85Bar = EquipLoadoutModel.BuildPreBattleBarState(
                lv85Snap, loading: false, switching: false, hasPendingUnlock: false, BattlePhase.Queued, string.Empty);
            Assert.That(lv85Bar.Slots[2].SpriteKey, Is.EqualTo(EquipLoadoutModel.TabUnlockedKey));
            Assert.That(queued.Slots[2].SpriteKey, Is.EqualTo(EquipLoadoutModel.TabRentedKey));
            int sigLv85 = EquipLoadoutModel.ComputePreBattleBarSignature(lv85Bar);
            Assert.That(sigQueuedAgain, Is.EqualTo(sigQueued));
            Assert.That(sigPrep, Is.Not.EqualTo(sigQueued));
            Assert.That(sigLv85, Is.Not.EqualTo(sigQueued));
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
                .Respond(new GetEquipLoadoutsResponse { Snapshot = MakeSnapshot(free: 3u, active: 1u, version: 7UL) });

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
                Snapshot = MakeSnapshot(free: 3u, active: 2u, version: 8UL),
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
