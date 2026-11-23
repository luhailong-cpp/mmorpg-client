using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;

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
                            ActiveAppearanceId = "08_alchemy_prodigy_boy",
                            TrialAppearanceId = "08_alchemy_prodigy_boy",
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

            Assert.That(client.StartTrial("08_alchemy_prodigy_boy"), Is.True);
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
        public void BiographyCatalog_MatchesExactFourRawIdsAndRejectsVisualAliasesAndOthers()
        {
            Assert.That(AppearanceBiographyCatalog.All.Count, Is.EqualTo(4));

            Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId("00_reference_topright_boy", out var ahe), Is.True);
            Assert.That(ahe.ArchiveName, Is.EqualTo("陆照禾"));
            Assert.That(ahe.Nickname, Is.EqualTo("阿禾"));
            Assert.That(ahe.DisplayArchiveName, Is.EqualTo("陆照禾（阿禾）"));
            Assert.That(ahe.StoryTitle, Is.EqualTo("第四只碗"));
            Assert.That(ahe.BackgroundSummary, Does.Contain("留灯院"));
            Assert.That(ahe.StoryBody, Does.Contain("回来带葱"));

            Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId("10_crimson_spear_girl", out var tingyun), Is.True);
            Assert.That(tingyun.ArchiveName, Is.EqualTo("祝停云"));
            Assert.That(tingyun.StoryTitle, Is.EqualTo("纸也赢过一回"));
            Assert.That(tingyun.BackgroundSummary, Does.Contain("败局册"));
            Assert.That(tingyun.StoryBody, Does.Contain("今天纸赢了"));

            Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId("17_ghost_script_calligrapher_boy", out var yansheng), Is.True);
            Assert.That(yansheng.ArchiveName, Is.EqualTo("闻砚生"));
            Assert.That(yansheng.StoryTitle, Is.EqualTo("借还栏"));
            Assert.That(yansheng.BackgroundSummary, Does.Contain("奇谈集"));
            Assert.That(yansheng.StoryBody, Does.Contain("名字，你来写"));

            Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId("09_bamboo_archer_girl", out var tingyu), Is.True);
            Assert.That(tingyu.ArchiveName, Is.EqualTo("桑听雨"));
            Assert.That(tingyu.StoryTitle, Is.EqualTo("没有画的那一页"));
            Assert.That(tingyu.BackgroundSummary, Does.Contain("潮汐鸟谱"));
            Assert.That(tingyu.StoryBody, Does.Contain("替我空一页吧"));

            // 其余 7 个现行形象不得命中四人传记
            string[] otherActiveIds =
            {
                "08_alchemy_prodigy_boy", "01_ice_sword_girl", "02_fire_talisman_boy",
                "03_lotus_healer_girl", "04_mountain_guardian_boy", "05_celestial_musician_girl",
                "15_water_dragon_scholar_boy"
            };
            foreach (string id in otherActiveIds)
                Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId(id, out _), Is.False, $"无档案形象不应命中：{id}");

            // 历史视觉替代 ID 严禁获得这四人的传记
            string[] retiredVisualAliases =
            {
                "QdaoHeadbandBoy", "12_iron_saber_flat_top_boy", "18_desert_sun_monk_girl",
                "22_lidazui_hair_waiter_saber_boy", "25_lion_drum_guard", "26_osmanthus_healer"
            };
            foreach (string alias in retiredVisualAliases)
                Assert.That(AppearanceBiographyCatalog.TryGetByRawAppearanceId(alias, out _), Is.False, $"历史视觉替代 ID 不得命中传记：{alias}");
        }

        [Test]
        public void PanelState_SkipFinishAndRereadPreserveSelectionAndListScroll()
        {
            var state = new AppearancePanelState();
            string[] ids =
            {
                "08_alchemy_prodigy_boy", "00_reference_topright_boy", "01_ice_sword_girl",
                "09_bamboo_archer_girl", "10_crimson_spear_girl", "17_ghost_script_calligrapher_boy"
            };
            state.SyncAvailableAppearances(ids, "08_alchemy_prodigy_boy", "08_alchemy_prodigy_boy");
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
            string[] ids = { "08_alchemy_prodigy_boy", "00_reference_topright_boy" };
            state.SyncAvailableAppearances(ids, "08_alchemy_prodigy_boy", "08_alchemy_prodigy_boy");
            state.SelectAppearance("00_reference_topright_boy", 0.72f);

            // 1. 先点旧“切换”一次（同时检验弹窗确认态与遗留武装态）
            state.ArmLegacySwitchConfirmation("00_reference_topright_boy", 10f);
            var firstIntent = state.RequestSwitch("00_reference_topright_boy", "08_alchemy_prodigy_boy", 2, 1);
            Assert.That(firstIntent, Is.EqualTo(AppearanceSwitchIntentOutcome.OpenedSwitchConfirm));
            Assert.That(state.HasPendingSwitchConfirmation, Is.True);
            Assert.That(net.Calls.Count, Is.EqualTo(0), "首次点击切换绝不能直接扣卡发包");

            // 2. 打开故事阅读层，必须清除未提交的旧扣卡确认
            Assert.That(state.OpenStory(0.72f), Is.True);
            Assert.That(state.IsStoryOpen, Is.True);
            Assert.That(state.HasPendingSwitchConfirmation, Is.False);
            Assert.That(state.TryConsumePendingSwitchConfirm(out _), Is.False);
            // 阅读中尝试触发切换必须被隔离忽略
            Assert.That(state.RequestSwitch("00_reference_topright_boy", "08_alchemy_prodigy_boy", 2, 1),
                Is.EqualTo(AppearanceSwitchIntentOutcome.Ignored));

            // 3. 返回详情后，首次再点“切换”必须重新进入确认，绝不直接扣卡
            Assert.That(state.CloseStory(AppearanceStoryCloseReason.Back), Is.True);
            Assert.That(state.HasPendingSwitchConfirmation, Is.False);
            Assert.That(state.SelectedAppearanceId, Is.EqualTo("00_reference_topright_boy"));
            Assert.That(state.SavedListScrollNormalized, Is.EqualTo(0.72f).Within(0.0001f));

            var secondIntent = state.RequestSwitch("00_reference_topright_boy", "08_alchemy_prodigy_boy", 2, 1);
            Assert.That(secondIntent, Is.EqualTo(AppearanceSwitchIntentOutcome.OpenedSwitchConfirm));
            Assert.That(net.Calls.Count, Is.EqualTo(0), "阅读与返回后再次点切换只重进确认，零资产/试用/换形象请求");
        }

        [Test]
        public void PanelState_NoBiographyLeavesNoResidualAndInvalidatedEntryOrDisconnectClosesStory()
        {
            var state = new AppearancePanelState();
            string[] ids = { "08_alchemy_prodigy_boy", "09_bamboo_archer_girl", "01_ice_sword_girl" };
            state.SyncAvailableAppearances(ids, "08_alchemy_prodigy_boy", "08_alchemy_prodigy_boy");

            // 先选有档案的桑听雨，再切到无档案的冰剑少女：不得残留旧正文
            state.SelectAppearance("09_bamboo_archer_girl", 0.5f);
            Assert.That(state.TryGetSelectedBiography(out var bio), Is.True);
            Assert.That(bio.ArchiveName, Is.EqualTo("桑听雨"));

            state.SelectAppearance("01_ice_sword_girl", 0.5f);
            Assert.That(state.TryGetSelectedBiography(out _), Is.False);
            Assert.That(state.OpenStory(0.5f), Is.False);
            Assert.That(state.IsStoryOpen, Is.False);

            // 打开桑听雨故事后若条目从列表失效，立刻关闭阅读层
            state.SelectAppearance("09_bamboo_archer_girl", 0.5f);
            Assert.That(state.OpenStory(0.5f), Is.True);
            state.SyncAvailableAppearances(new[] { "08_alchemy_prodigy_boy", "01_ice_sword_girl" },
                "08_alchemy_prodigy_boy", "08_alchemy_prodigy_boy");
            Assert.That(state.IsStoryOpen, Is.False);
            Assert.That(state.TryGetReadingBiography(out _), Is.False);

            // 背包 0 张卡点击切换 -> 仅弹缺卡商城引导，不触发确认提交
            var noCardOutcome = state.RequestSwitch("01_ice_sword_girl", "08_alchemy_prodigy_boy", 0, 1);
            Assert.That(noCardOutcome, Is.EqualTo(AppearanceSwitchIntentOutcome.OpenedNoCardGuide));
            Assert.That(state.IsNoCardGuideOpen, Is.True);
            Assert.That(state.TryConsumePendingSwitchConfirm(out _), Is.False);

            // 断线/关闭重置清空全部弹层
            state.ResetForCloseOrDisconnect();
            Assert.That(state.HasOverlay, Is.False);
            Assert.That(state.HasPendingSwitchConfirmation, Is.False);
        }
    }
}
