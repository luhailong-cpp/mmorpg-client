using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Table;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class AppearanceClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 9001;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;

            public readonly List<(uint msgId, IMessage req)> Calls = new();
            private readonly Dictionary<uint, Action<MessageContent>> _notifies = new();
            private readonly Queue<Action> _pending = new();
            public Func<uint, IMessage, (IMessage resp, string err)> Responder;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) => _notifies[messageId] = handler;

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add((messageId, request));
                var (resp, err) = Responder(messageId, request);
                if (err != null) _pending.Enqueue(() => onError(err));
                else _pending.Enqueue(() => onResponse((TResp)resp));
            }

            public void SendOneWay(uint messageId, IMessage request) { }

            public void Flush()
            {
                while (_pending.Count > 0) _pending.Dequeue().Invoke();
            }

            public void EmitNotify(uint messageId, IMessage payload)
            {
                if (_notifies.TryGetValue(messageId, out var handler))
                    handler(new MessageContent { MessageId = messageId, SerializedMessage = payload.ToByteString() });
            }

            public void TriggerDisconnect()
            {
                IsReady = false;
                Disconnected?.Invoke();
            }
        }

        private static readonly AppearanceMessageIds Ids = new AppearanceMessageIds(442, 443, 444, 445, 447);

        [TestCase(true)]
        [TestCase(false)]
        public void Client_RejectsOldRoleResponseBeforeTheNextTick(bool changeConnection)
        {
            var net = new FakeTransport();
            object connection = new object();
            net.Responder = (_, _) => (new GetAppearanceInfoResponse
            {
                Info = new PlayerAppearanceInfo { PermanentAppearanceId = "old-role", ActiveAppearanceId = "old-role" }
            }, null);
            using var client = new AppearanceClient(net, () => connection, () => 10f, Ids);
            client.RequestInfo(force: true);

            // 刻意不调用 Tick：收包回调本身必须先检查连接和角色代次。
            if (changeConnection) connection = new object();
            else net.PlayerId = ulong.MaxValue - 5;
            net.Flush();

            Assert.That(client.Info, Is.Null, "旧角色回包不得重新填入当前快照");
            Assert.That(client.InfoPending, Is.False);
        }

        [Test]
        public void Client_RejectsQueuedStateNotifyAfterDisconnect()
        {
            var net = new FakeTransport();
            using var client = new AppearanceClient(net, () => net, () => 10f, Ids);
            net.TriggerDisconnect();
            net.EmitNotify(Ids.NotifyState, new AppearanceStateNotify
            {
                Info = new PlayerAppearanceInfo { PermanentAppearanceId = "old-role", ActiveAppearanceId = "old-role" }
            });

            Assert.That(client.Info, Is.Null, "断线后迟到推送不得恢复旧角色快照");
            Assert.That(client.Ready, Is.False);
        }

        [Test]
        public void Models_BitStoragePriceCountdownAndTipsMatchServerContract()
        {
            // bit 0 (byte 0 bit 0) 与 bit 10 (byte 1 bit 2)
            byte[] bits = { 0x01, 0x04 };
            Assert.That(AppearanceModels.IsBitSet(bits, 0), Is.True);
            Assert.That(AppearanceModels.IsBitSet(bits, 1), Is.False);
            Assert.That(AppearanceModels.IsBitSet(bits, 10), Is.True);
            Assert.That(AppearanceModels.IsBitSet(bits, 11), Is.False);
            Assert.That(AppearanceModels.IsBitSet(bits, 99), Is.False);
            Assert.That(AppearanceModels.IsBitSet(null, 0), Is.False);

            Assert.That(AppearanceModels.FormatPriceYuan(1000), Is.EqualTo("10元"));
            Assert.That(AppearanceModels.FormatPriceYuan(1050), Is.EqualTo("10.50元"));
            Assert.That(AppearanceModels.FormatTrialCountdown(3600), Is.EqualTo("1小时00分00秒"));
            Assert.That(AppearanceModels.FormatTrialCountdown(3572), Is.EqualTo("59分32秒"));
            Assert.That(AppearanceModels.FormatTrialCountdown(0), Is.EqualTo("已到期"));

            Assert.That(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceNoCard), Does.Contain("10元"));
            Assert.That(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceTrialAlreadyUsed), Does.Contain("限试用 1 次"));
            Assert.That(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceInvalid), Does.Contain("不存在"));
            Assert.That(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceNotCardUnlockable), Does.Contain("专属玩法"));
            Assert.That(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceAlreadyOwned), Does.Contain("免费切换"));
            Assert.That(AppearanceModels.DescribeTip((uint)appearance_error.KAppearanceNotOwned), Does.Contain("尚未拥有"));
        }

        [Test]
        public void Client_FreeSwitchSetsExpectFreeSwitchFlagAndRefreshesOnNotOwnedTip()
        {
            var net = new FakeTransport();
            float now = 50f;
            var conn = new object();
            net.Responder = (msgId, req) =>
            {
                if (msgId == Ids.Switch)
                {
                    var sw = (SwitchAppearanceRequest)req;
                    if (sw.AppearanceId == "02_fire_talisman_boy")
                    {
                        return (new SwitchAppearanceResponse
                        {
                            ErrorMessage = new TipInfoMessage { Id = (uint)appearance_error.KAppearanceNotOwned }
                        }, null);
                    }
                    return (new SwitchAppearanceResponse
                    {
                        ErrorMessage = new TipInfoMessage { Id = 0 },
                        Info = new PlayerAppearanceInfo
                        {
                            PermanentAppearanceId = sw.AppearanceId,
                            ActiveAppearanceId = sw.AppearanceId,
                            OwnedCardCount = 0,
                            ServerNow = 1000,
                        }
                    }, null);
                }
                return (new GetAppearanceInfoResponse
                {
                    Info = new PlayerAppearanceInfo { PermanentAppearanceId = "00_reference_topright_boy", ActiveAppearanceId = "00_reference_topright_boy" }
                }, null);
            };

            using var client = new AppearanceClient(net, () => conn, () => now, Ids);
            Assert.That(client.SwitchAppearance("01_ice_sword_girl", expectFreeSwitch: true), Is.True);
            net.Flush();
            var freeReq = (SwitchAppearanceRequest)net.Calls[0].req;
            Assert.That(freeReq.ExpectFreeSwitch, Is.True);
            Assert.That(client.Status, Is.EqualTo(AppearanceClient.FreeSwitchedMessage));

            now += 2f;
            Assert.That(client.SwitchAppearance("02_fire_talisman_boy", expectFreeSwitch: true), Is.True);
            net.Flush();
            Assert.That(client.StatusIsError, Is.True);
            Assert.That(client.Status, Does.Contain("尚未拥有"));
            Assert.That(net.Calls.Count, Is.EqualTo(3), "收到 KAppearanceNotOwned 应强制刷新面板状态");
            Assert.That(net.Calls[2].msgId, Is.EqualTo(Ids.Get));
        }

        [Test]
        public void Client_SwitchRetriesOnceWithSameIdempotencyKeyOnTimeout()
        {
            var net = new FakeTransport();
            float now = 10f;
            var conn = new object();
            int switchAttempts = 0;
            net.Responder = (msgId, req) =>
            {
                if (msgId == Ids.Switch)
                {
                    switchAttempts++;
                    if (switchAttempts == 1) return (null, OutstandingCalls.TimeoutDetail);
                    return (new SwitchAppearanceResponse
                    {
                        ErrorMessage = new TipInfoMessage { Id = 0 },
                        Replayed = true,
                        Info = new PlayerAppearanceInfo
                        {
                            PermanentAppearanceId = "01_ice_sword_girl",
                            ActiveAppearanceId = "01_ice_sword_girl",
                            OwnedCardCount = 0,
                            ServerNow = 1000,
                        }
                    }, null);
                }
                return (new GetAppearanceInfoResponse(), null);
            };

            using var client = new AppearanceClient(net, () => conn, () => now, Ids);
            Assert.That(client.SwitchAppearance("01_ice_sword_girl"), Is.True);
            net.Flush();

            Assert.That(switchAttempts, Is.EqualTo(2));
            var firstReq = (SwitchAppearanceRequest)net.Calls[0].req;
            var secondReq = (SwitchAppearanceRequest)net.Calls[1].req;
            Assert.That(firstReq.ClientRequestId, Is.Not.EqualTo(0ul));
            Assert.That(secondReq.ClientRequestId, Is.EqualTo(firstReq.ClientRequestId), "超时重发必须携带同一幂等键");
            Assert.That(client.Info.PermanentAppearanceId, Is.EqualTo("01_ice_sword_girl"));
            Assert.That(client.RequiresReconnect, Is.False);
        }

        [Test]
        public void Client_TrialCountdownAndExpiredServerPushRaiseNotice()
        {
            var net = new FakeTransport();
            float now = 100f;
            var conn = new object();
            net.Responder = (msgId, req) =>
            {
                if (msgId == Ids.StartTrial)
                {
                    return (new StartAppearanceTrialResponse
                    {
                        ErrorMessage = new TipInfoMessage { Id = 0 },
                        Info = new PlayerAppearanceInfo
                        {
                            PermanentAppearanceId = "00_reference_topright_boy",
                            ActiveAppearanceId = "02_fire_talisman_boy",
                            TrialAppearanceId = "02_fire_talisman_boy",
                            TrialExpiresAt = 4600,
                            ServerNow = 1000,
                            TrialUsedBits = ByteString.CopyFrom(new byte[] { 0x01 }),
                        }
                    }, null);
                }
                return (new GetAppearanceInfoResponse(), null);
            };

            string notice = null;
            using var client = new AppearanceClient(net, () => conn, () => now, Ids);
            client.Notice += msg => notice = msg;

            Assert.That(client.StartTrial("02_fire_talisman_boy"), Is.True);
            net.Flush();
            Assert.That(client.HasActiveTrial, Is.True);
            Assert.That(client.TrialRemainingSeconds, Is.EqualTo(3600ul));
            now += 120f;
            Assert.That(client.TrialRemainingSeconds, Is.EqualTo(3480ul));

            net.EmitNotify(Ids.NotifyState, new AppearanceStateNotify
            {
                TrialExpired = true,
                Info = new PlayerAppearanceInfo
                {
                    PermanentAppearanceId = "00_reference_topright_boy",
                    ActiveAppearanceId = "00_reference_topright_boy",
                    ServerNow = 4600,
                    TrialUsedBits = ByteString.CopyFrom(new byte[] { 0x01 }),
                }
            });
            Assert.That(client.HasActiveTrial, Is.False);
            Assert.That(notice, Is.EqualTo(AppearanceModels.TrialExpiredText));
        }

        [Test]
        public void BiographyCatalog_MatchesAllNineActiveRawIdsHotReloadSafeAndRejectsAliases()
        {
            byte[] prevBytes = CharacterAppearanceTableManager.Instance.Container.ToByteArray();
            try
            {
                string pbPath = Path.Combine(Application.streamingAssetsPath, "Tables",
                    CharacterAppearanceTableManager.BinaryFileName);
                Assert.That(File.Exists(pbPath), Is.True, "正式导出的 characterappearance.pb 应存在");
                byte[] realBytes = File.ReadAllBytes(pbPath);
                CharacterAppearanceTableManager.Instance.LoadFromBytes(realBytes);

                Assert.That(AppearanceBiographyCatalog.All.Count, Is.EqualTo(9));

                var expectedNine = new[]
                {
                    ("00_reference_topright_boy", "陆照禾", "阿禾", "第四只碗", "回来带葱"),
                    ("01_ice_sword_girl", "霜见雪", "", "霜阶扫雪", "别把青石烤裂"),
                    ("02_fire_talisman_boy", "许炽", "小炽", "焦边朱砂", "糖栗子"),
                    ("03_lotus_healer_girl", "苏青莲", "", "药包双结", "心就先安了一半"),
                    ("05_celestial_musician_girl", "林知音", "", "廊下试弦", "灰雀"),
                    ("09_bamboo_archer_girl", "桑听雨", "", "没有画的那一页", "替我空一页吧"),
                    ("10_crimson_spear_girl", "祝停云", "", "纸也赢过一回", "今天纸赢了"),
                    ("15_water_dragon_scholar_boy", "沈澄", "", "半盏未凉", "茶凉了"),
                    ("20_star_formation_master_girl", "星璇", "", "十八星印", "十八枚战法印"),
                };
                foreach (var (rawId, archiveName, nickname, storyTitle, bodySnippet) in expectedNine)
                {
                    Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId(rawId, out var bio), Is.True, $"应命中：{rawId}");
                    Assert.That(bio.ArchiveName, Is.EqualTo(archiveName));
                    Assert.That(bio.Nickname, Is.EqualTo(nickname));
                    Assert.That(bio.StoryTitle, Is.EqualTo(storyTitle));
                    Assert.That(bio.StoryBody, Does.Contain(bodySnippet));
                    Assert.That(bio.BackgroundSummary, Does.Not.Contain("闻砚生"));
                    Assert.That(bio.StoryBody, Does.Not.Contain("闻砚生"));
                }
                Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId("00_reference_topright_boy", out var ahe), Is.True);
                Assert.That(ahe.DisplayArchiveName, Is.EqualTo("陆照禾（阿禾）"));
                Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId("10_crimson_spear_girl", out var tingyun), Is.True);
                Assert.That(tingyun.StoryBody, Does.Contain("沈澄"));

                // 已删除与未配置形象不得命中人物志
                string[] otherIds =
                {
                    "99_no_biography_probe", "30_han_xiangzi",
                    "04_mountain_guardian_boy", "08_alchemy_prodigy_boy", "17_ghost_script_calligrapher_boy"
                };
                foreach (string id in otherIds)
                    Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId(id, out _), Is.False, $"无档案或已删除形象不应命中：{id}");

                // 历史视觉替代 ID 严禁获得任何人物志
                string[] retiredVisualAliases =
                {
                    "QdaoHeadbandBoy", "12_iron_saber_flat_top_boy", "18_desert_sun_monk_girl",
                    "22_lidazui_hair_waiter_saber_boy", "25_lion_drum_guard", "26_osmanthus_healer"
                };
                foreach (string alias in retiredVisualAliases)
                    Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId(alias, out _), Is.False, $"历史视觉替代 ID 不得命中传记：{alias}");

                // 热更契约：加载空人物志的新表快照时，立刻以表快照为准，不回退到旧硬编码文案
                var reloaded = new CharacterAppearanceTableData();
                reloaded.Data.Add(new CharacterAppearanceTable
                {
                    Id = 2,
                    AppearanceId = "00_reference_topright_boy",
                    Name = "金发带Q道童",
                    Enabled = 1,
                });
                CharacterAppearanceTableManager.Instance.LoadFromBytes(reloaded.ToByteArray());
                Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId("00_reference_topright_boy", out _), Is.False);
                Assert.That(AppearanceBiographyCatalog.All.Count, Is.EqualTo(0));
            }
            finally
            {
                CharacterAppearanceTableManager.Instance.LoadFromBytes(prevBytes);
            }
        }

        [Test]
        public void PanelState_SkipFinishAndRereadPreserveSelectionAndListScroll()
        {
            var state = new AppearancePanelState();
            string[] ids =
            {
                "05_celestial_musician_girl", "00_reference_topright_boy", "01_ice_sword_girl",
                "09_bamboo_archer_girl", "10_crimson_spear_girl", "15_water_dragon_scholar_boy"
            };
            state.SyncAvailableAppearances(ids, "05_celestial_musician_girl", "05_celestial_musician_girl");
            state.SelectAppearance("10_crimson_spear_girl", 0.37f);

            Assert.That(state.OpenStory(0.37f), Is.True);
            Assert.That(state.IsStoryOpen, Is.True);
            Assert.That(state.TryGetReadingBiography(out var bio), Is.True);
            Assert.That(bio.StoryTitle, Is.EqualTo("纸也赢过一回"));

            // 跳过后仍选中原人物，列表位置不变
            Assert.That(state.CloseStory(AppearanceStoryCloseReason.Skip), Is.True);
            Assert.That(state.IsStoryOpen, Is.False);
            Assert.That(state.SelectedAppearanceId, Is.EqualTo("10_crimson_spear_girl"));
            Assert.That(state.SavedListScrollNormalized, Is.EqualTo(0.37f).Within(0.0001f));

            // 可再次打开重读，读完返回仍保留原选中与列表位置
            Assert.That(state.OpenStory(0.37f), Is.True);
            Assert.That(state.StoryOpenCount, Is.EqualTo(2));
            Assert.That(state.CloseStory(AppearanceStoryCloseReason.Finished), Is.True);
            Assert.That(state.SelectedAppearanceId, Is.EqualTo("10_crimson_spear_girl"));
            Assert.That(state.SavedListScrollNormalized, Is.EqualTo(0.37f).Within(0.0001f));
        }

        [Test]
        public void PanelState_SwitchClickThenStoryThenReturnThenSwitchNeverMischargesOrCallsRpc()
        {
            var net = new FakeTransport();
            float now = 10f;
            var conn = new object();
            net.Responder = (_, _) => (new GetAppearanceInfoResponse(), null);
            using var client = new AppearanceClient(net, () => conn, () => now, Ids);

            var state = new AppearancePanelState();
            string[] ids = { "05_celestial_musician_girl", "00_reference_topright_boy" };
            state.SyncAvailableAppearances(ids, "05_celestial_musician_girl", "05_celestial_musician_girl");
            state.SelectAppearance("00_reference_topright_boy", 0.72f);

            // 1. 先点旧“切换”一次（同时检验弹窗确认态与遗留武装态）
            state.ArmLegacySwitchConfirmation("00_reference_topright_boy", 10f);
            var firstIntent = state.RequestSwitch("00_reference_topright_boy", "05_celestial_musician_girl", 2, 1);
            Assert.That(firstIntent, Is.EqualTo(AppearanceSwitchIntentOutcome.OpenedSwitchConfirm));
            Assert.That(state.HasPendingSwitchConfirmation, Is.True);
            Assert.That(net.Calls.Count, Is.EqualTo(0), "首次点击切换绝不能直接扣卡发包");

            // 2. 打开故事阅读层，必须清除未提交的旧扣卡确认
            Assert.That(state.OpenStory(0.72f), Is.True);
            Assert.That(state.IsStoryOpen, Is.True);
            Assert.That(state.HasPendingSwitchConfirmation, Is.False);
            Assert.That(state.TryConsumePendingSwitchConfirm(out _), Is.False);
            // 阅读中尝试触发切换必须被隔离忽略
            Assert.That(state.RequestSwitch("00_reference_topright_boy", "05_celestial_musician_girl", 2, 1),
                Is.EqualTo(AppearanceSwitchIntentOutcome.Ignored));

            // 3. 返回详情后，首次再点“切换”必须重新进入确认，绝不直接扣卡
            Assert.That(state.CloseStory(AppearanceStoryCloseReason.Back), Is.True);
            Assert.That(state.HasPendingSwitchConfirmation, Is.False);
            Assert.That(state.SelectedAppearanceId, Is.EqualTo("00_reference_topright_boy"));
            Assert.That(state.SavedListScrollNormalized, Is.EqualTo(0.72f).Within(0.0001f));

            var secondIntent = state.RequestSwitch("00_reference_topright_boy", "05_celestial_musician_girl", 2, 1);
            Assert.That(secondIntent, Is.EqualTo(AppearanceSwitchIntentOutcome.OpenedSwitchConfirm));
            Assert.That(net.Calls.Count, Is.EqualTo(0), "阅读与返回后再次点切换只重进确认，零资产/试用/换形象请求");
        }

        [Test]
        public void PanelState_NoBiographyLeavesNoResidualAndInvalidatedEntryOrDisconnectClosesStory()
        {
            var state = new AppearancePanelState();
            const string noBioId = "99_no_biography_probe";
            string[] ids = { "05_celestial_musician_girl", "09_bamboo_archer_girl", noBioId };
            state.SyncAvailableAppearances(ids, "05_celestial_musician_girl", "05_celestial_musician_girl");

            // 先选有档案的桑听雨，再切到无档案探针条目：不得残留旧正文
            state.SelectAppearance("09_bamboo_archer_girl", 0.5f);
            Assert.That(state.TryGetSelectedBiography(out var bio), Is.True);
            Assert.That(bio.ArchiveName, Is.EqualTo("桑听雨"));

            state.SelectAppearance(noBioId, 0.5f);
            Assert.That(state.TryGetSelectedBiography(out _), Is.False);
            Assert.That(state.OpenStory(0.5f), Is.False);
            Assert.That(state.IsStoryOpen, Is.False);

            // 打开桑听雨故事后若条目从列表失效，立刻关闭阅读层
            state.SelectAppearance("09_bamboo_archer_girl", 0.5f);
            Assert.That(state.OpenStory(0.5f), Is.True);
            state.SyncAvailableAppearances(new[] { "05_celestial_musician_girl", noBioId },
                "05_celestial_musician_girl", "05_celestial_musician_girl");
            Assert.That(state.IsStoryOpen, Is.False);
            Assert.That(state.TryGetReadingBiography(out _), Is.False);

            // 背包 0 张卡点击未拥有可兑换形象 -> 仅弹缺卡商城引导，不触发确认提交
            var noCardOutcome = state.RequestSwitch(noBioId, "05_celestial_musician_girl", 0, 1, targetOwned: false, targetCardUnlockable: true);
            Assert.That(noCardOutcome, Is.EqualTo(AppearanceSwitchIntentOutcome.OpenedNoCardGuide));
            Assert.That(state.IsNoCardGuideOpen, Is.True);
            Assert.That(state.TryConsumePendingSwitchConfirm(out _), Is.False);

            // 已拥有非当前形象 -> 0 卡直接免费切换，不弹确认/缺卡框
            var freeOutcome = state.RequestSwitch(noBioId, "05_celestial_musician_girl", 0, 1, targetOwned: true, targetCardUnlockable: false);
            Assert.That(freeOutcome, Is.EqualTo(AppearanceSwitchIntentOutcome.ReadyForFreeSwitch));
            Assert.That(state.HasOverlay, Is.False);

            // 未拥有且非卡兑换（玩法专属）-> 拒绝扣卡，不弹确认/缺卡框
            var lockedOutcome = state.RequestSwitch(noBioId, "05_celestial_musician_girl", 5, 1, targetOwned: false, targetCardUnlockable: false);
            Assert.That(lockedOutcome, Is.EqualTo(AppearanceSwitchIntentOutcome.LockedExclusive));
            Assert.That(state.HasOverlay, Is.False);

            // 断线/关闭重置清空全部弹层
            state.ResetForCloseOrDisconnect();
            Assert.That(state.HasOverlay, Is.False);
            Assert.That(state.HasPendingSwitchConfirmation, Is.False);
        }

        [Test]
        public void SeriesModelsAndPanelState_FilterAndGateEpilogueByAuthoritativeCompletion()
        {
            var info = new PlayerAppearanceInfo
            {
                PermanentAppearanceId = "00_reference_topright_boy",
                ActiveAppearanceId = "00_reference_topright_boy",
                Appearances =
                {
                    new CharacterAppearanceEntry { ConfigId = 2, AppearanceId = "00_reference_topright_boy", Owned = true, SeriesId = 1 },
                    new CharacterAppearanceEntry { ConfigId = 8, AppearanceId = "09_bamboo_archer_girl", Owned = true, SeriesId = 1 },
                    new CharacterAppearanceEntry { ConfigId = 9, AppearanceId = "10_crimson_spear_girl", Owned = true, SeriesId = 1 },
                    new CharacterAppearanceEntry { ConfigId = 3, AppearanceId = "01_ice_sword_girl", Owned = false, SeriesId = 2 },
                },
                SeriesProgress =
                {
                    new CharacterAppearanceSeriesProgress
                    {
                        SeriesId = 1, SeriesKey = "lantern_courtyard", Name = "留灯院旧友",
                        OwnedCount = 3, TotalCount = 3, Completed = true,
                        CompletionBadgeName = "留灯同袍", EpilogueTitle = "灯下三碗面"
                    },
                    new CharacterAppearanceSeriesProgress
                    {
                        SeriesId = 2, SeriesKey = "elemental_blades", Name = "水火冰锋",
                        OwnedCount = 0, TotalCount = 3, Completed = false,
                        CompletionBadgeName = "三灵同契", EpilogueTitle = "霜火沧澜试"
                    }
                }
            };

            Assert.That(AppearanceSeriesModels.FilterBySeries(info, 0).Count, Is.EqualTo(4));
            Assert.That(AppearanceSeriesModels.FilterBySeries(info, 1).Count, Is.EqualTo(3));
            Assert.That(AppearanceSeriesModels.CountCompletedSeries(info), Is.EqualTo(1));
            Assert.That(AppearanceSeriesModels.TryGetUnlockedEpilogue(info, 2, _ => "正文", out _), Is.False);
            Assert.That(AppearanceSeriesModels.TryGetUnlockedEpilogue(info, 1, _ => "灯下三碗面正文", out var epilogue), Is.True);
            Assert.That(epilogue.BadgeName, Is.EqualTo("留灯同袍"));
            Assert.That(epilogue.EpilogueBody, Is.EqualTo("灯下三碗面正文"));

            var state = new AppearancePanelState();
            state.SyncAvailableAppearances(new[] { "00_reference_topright_boy", "10_crimson_spear_girl" },
                "00_reference_topright_boy", "00_reference_topright_boy");
            state.SelectSeries(1, 0.4f);
            Assert.That(state.SelectedSeriesId, Is.EqualTo(1u));
            state.RequestSwitch("10_crimson_spear_girl", "00_reference_topright_boy", 2, 1);
            Assert.That(state.OpenSeriesEpilogue(1, true, epilogue.EpilogueBody, 0.4f), Is.True);
            Assert.That(state.IsSeriesEpilogueOpen, Is.True);
            Assert.That(state.HasPendingSwitchConfirmation, Is.False);
            Assert.That(state.RequestSwitch("10_crimson_spear_girl", "00_reference_topright_boy", 2, 1),
                Is.EqualTo(AppearanceSwitchIntentOutcome.Ignored));
            Assert.That(state.CloseSeriesEpilogue(), Is.True);
            Assert.That(state.IsSeriesEpilogueOpen, Is.False);

            // 单行四页签宽度分配：3 个现役系列 + 全部 刚好填满 612px 视口；扩展到 5 个系列时自动保留舒适宽度交由横向滚动
            var threeSeries = new List<CharacterAppearanceSeriesProgress>
            {
                new CharacterAppearanceSeriesProgress { SeriesId = 1, Name = "留灯院旧友" },
                new CharacterAppearanceSeriesProgress { SeriesId = 2, Name = "水火冰锋" },
                new CharacterAppearanceSeriesProgress { SeriesId = 3, Name = "清音济世" },
            };
            float[] singleRowWidths = AppearanceSeriesModels.ComputeSeriesTabWidths(threeSeries);
            Assert.That(singleRowWidths.Length, Is.EqualTo(4));
            float singleRowTotal = singleRowWidths[0] + singleRowWidths[1] + singleRowWidths[2] + singleRowWidths[3]
                + AppearanceSeriesModels.DefaultTabGap * 3f;
            Assert.That(singleRowTotal, Is.EqualTo(AppearanceSeriesModels.DefaultTabViewportWidth).Within(0.01f));
            for (int i = 0; i < singleRowWidths.Length; i++)
                Assert.That(singleRowWidths[i], Is.GreaterThanOrEqualTo(76f));

            var fiveSeries = new List<CharacterAppearanceSeriesProgress>(threeSeries)
            {
                new CharacterAppearanceSeriesProgress { SeriesId = 4, Name = "星河问道" },
                new CharacterAppearanceSeriesProgress { SeriesId = 5, Name = "碧渊游侠" },
            };
            float[] overflowWidths = AppearanceSeriesModels.ComputeSeriesTabWidths(fiveSeries);
            float overflowTotal = AppearanceSeriesModels.DefaultTabGap * 5f;
            for (int i = 0; i < overflowWidths.Length; i++) overflowTotal += overflowWidths[i];
            Assert.That(overflowTotal, Is.GreaterThan(AppearanceSeriesModels.DefaultTabViewportWidth));
        }

        [Test]
        public void PanelState_BiographyToStoryToPortraitPreviewReturnsLayerByLayerAndKeepsStoryScroll()
        {
            var state = new AppearancePanelState();
            state.SyncAvailableAppearances(new[] { "00_reference_topright_boy", "10_crimson_spear_girl" },
                "00_reference_topright_boy", "00_reference_topright_boy");
            state.SelectAppearance("10_crimson_spear_girl", 0.41f);

            Assert.That(state.OpenBiography(0.41f), Is.True);
            Assert.That(state.OpenStory(0.41f), Is.True);
            state.SaveStoryPosition(0.28f);

            Assert.That(state.OpenPortraitPreview(0.41f), Is.True);
            Assert.That(state.IsPortraitPreviewOpen, Is.True);
            Assert.That(state.DismissActiveOverlay(), Is.True);
            Assert.That(state.IsStoryOpen, Is.True, "大图返回应回到往事正文层");
            Assert.That(state.SavedStoryScrollNormalized, Is.EqualTo(0.28f).Within(0.0001f));

            Assert.That(state.DismissActiveOverlay(), Is.True);
            Assert.That(state.IsBiographyOpen, Is.True, "往事普通返回应回到人物小传层");
            Assert.That(state.DismissActiveOverlay(), Is.True);
            Assert.That(state.HasOverlay, Is.False);
            Assert.That(state.SavedListScrollNormalized, Is.EqualTo(0.41f).Within(0.0001f));
        }
    }
}
