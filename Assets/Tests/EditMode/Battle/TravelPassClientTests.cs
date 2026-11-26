using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.TravelPass;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class TravelPassClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 9101;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;

            public readonly List<(uint msgId, IMessage req)> Calls = new();
            private readonly Queue<Action> _pending = new();
            public Func<uint, IMessage, (IMessage resp, string err)> Responder;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
            public void SendOneWay(uint messageId, IMessage request) { }

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add((messageId, request));
                var (resp, err) = Responder(messageId, request);
                if (err != null) _pending.Enqueue(() => onError(err));
                else _pending.Enqueue(() => onResponse((TResp)resp));
            }

            public void Flush()
            {
                while (_pending.Count > 0) _pending.Dequeue().Invoke();
            }

            public void TriggerDisconnect()
            {
                IsReady = false;
                Disconnected?.Invoke();
            }
        }

        private static global::TravelPassSnapshot SampleSnapshot(
            uint points = 90,
            bool unlocked = false,
            ulong diamond = 200,
            ulong bindDiamond = 9999,
            bool free1Claimed = false,
            bool prem1Claimed = false)
        {
            var snap = new global::TravelPassSnapshot
            {
                Available = true,
                ServerNowMs = 1790000000000UL,
                PassRevision = 1,
                PassId = 202601,
                ConfigRevision = 1,
                Name = "青云游历手记 · 第一期",
                OpenForProgress = true,
                OpenForUnlock = true,
                ProgressPoints = points,
                MaxPoints = 300,
                PveWinPoints = 20,
                HardOrPvpWinPoints = 30,
                PvpQualifiedOtherPoints = 15,
                PremiumUnlocked = unlocked,
                PremiumCurrencyType = TravelPassModels.CurrencyDiamond,
                PremiumPrice = 180,
                DiamondBalance = diamond,
                BindDiamondBalance = bindDiamond,
                ClaimableFreeCount = (!free1Claimed && points >= 30) ? 1u : 0u,
                ClaimablePremiumCount = (unlocked && !prem1Claimed && points >= 30) ? 1u : 0u,
                RetroactivePremiumCount = (!unlocked && points >= 30) ? 3u : 0u,
            };
            snap.Tiers.Add(new global::TravelPassTierView
            {
                TierId = 20260101,
                TierIndex = 1,
                RequiredPoints = 30,
                Reached = points >= 30,
                FreeGold = 3000,
                FreeClaimed = free1Claimed,
                FreeCanClaim = points >= 30 && !free1Claimed,
                PremiumItemId = 3405,
                PremiumItemCount = 1,
                PremiumItemName = "定星保底符",
                PremiumCosmeticKey = "frame_travel_qingyun_s1",
                PremiumCosmeticName = "青云游历头像框",
                PremiumClaimed = prem1Claimed,
                PremiumCanClaim = unlocked && points >= 30 && !prem1Claimed,
            });
            if (prem1Claimed) snap.UnlockedCosmeticKeys.Add("frame_travel_qingyun_s1");
            return snap;
        }

        [Test]
        public void RequestState_CachesDualBalances_AndDoesNotCountLockedPremiumAsClaimable()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 10f;
            net.Responder = (msgId, _) =>
            {
                Assert.AreEqual(SceneActivityClientPlayerRpc.GetTravelPassStateMessageId, msgId);
                return (new global::GetTravelPassStateResponse
                {
                    Snapshot = SampleSnapshot(points: 90, unlocked: false, diamond: 120, bindDiamond: 8888, free1Claimed: true)
                }, null);
            };

            using var client = new TravelPassClient(net, () => conn, () => now);
            client.RequestState(force: true);
            net.Flush();

            Assert.IsTrue(client.HasState);
            Assert.AreEqual(120UL, client.DiamondBalance);
            Assert.AreEqual(8888UL, client.BindDiamondBalance);
            // 免费轨已领完且典藏未解锁时，绝不制造假红点
            Assert.IsFalse(client.HasClaimableReward);
            Assert.AreEqual(0u, client.ClaimableCount);
            Assert.AreEqual(3u, client.State.RetroactivePremiumCount);
        }

        [Test]
        public void UnlockPremium_SendsQuoteAndIdempotencyKey_AndResendsOnceOnTimeout()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 10f;
            int unlockCalls = 0;
            ulong firstKey = 0;
            net.Responder = (msgId, req) =>
            {
                if (msgId == SceneActivityClientPlayerRpc.GetTravelPassStateMessageId)
                    return (new global::GetTravelPassStateResponse { Snapshot = SampleSnapshot(points: 90, unlocked: false, diamond: 300) }, null);

                Assert.AreEqual(SceneActivityClientPlayerRpc.UnlockTravelPassPremiumMessageId, msgId);
                var unlockReq = (global::UnlockTravelPassPremiumRequest)req;
                Assert.AreNotEqual(0UL, unlockReq.ClientRequestId);
                Assert.AreEqual(202601u, unlockReq.PassId);
                Assert.AreEqual(180UL, unlockReq.ExpectedPrice);
                Assert.AreEqual(1u, unlockReq.ExpectedConfigRevision);
                unlockCalls++;
                if (unlockCalls == 1)
                {
                    firstKey = unlockReq.ClientRequestId;
                    return (null, OutstandingCalls.TimeoutDetail);
                }
                Assert.AreEqual(firstKey, unlockReq.ClientRequestId);
                return (new global::UnlockTravelPassPremiumResponse
                {
                    Snapshot = SampleSnapshot(points: 90, unlocked: true, diamond: 120)
                }, null);
            };

            using var client = new TravelPassClient(net, () => conn, () => now);
            client.RequestState(force: true);
            net.Flush();

            bool unlockedEvent = false;
            bool callbackResult = false;
            client.PremiumUnlocked += () => unlockedEvent = true;

            var quote = client.CurrentUnlockQuote();
            Assert.IsTrue(quote.IsValid);
            Assert.IsTrue(client.UnlockPremium(quote, ok => callbackResult = ok));
            net.Flush();

            Assert.AreEqual(2, unlockCalls);
            Assert.IsTrue(unlockedEvent);
            Assert.IsTrue(callbackResult);
            Assert.IsFalse(client.RequiresReconnect);
            Assert.IsTrue(client.State.PremiumUnlocked);
            Assert.AreEqual(120UL, client.DiamondBalance);
        }

        [Test]
        public void UnlockPremium_DoubleTimeout_LocksUntilReconnect_WhileCircuitOpenDoesNotLock()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 10f;
            bool returnCircuitOpen = true;
            net.Responder = (msgId, _) =>
            {
                if (msgId == SceneActivityClientPlayerRpc.GetTravelPassStateMessageId)
                    return (new global::GetTravelPassStateResponse { Snapshot = SampleSnapshot() }, null);
                return returnCircuitOpen ? (null, RpcGuard.CircuitOpenError) : (null, OutstandingCalls.TimeoutDetail);
            };

            using var client = new TravelPassClient(net, () => conn, () => now);
            client.RequestState(force: true);
            net.Flush();

            // 熔断属于确定未执行，绝不锁死面板
            Assert.IsTrue(client.UnlockPremium(client.CurrentUnlockQuote()));
            net.Flush();
            Assert.IsFalse(client.RequiresReconnect);

            // 双次超时结果不明，必须锁死直到断线重连
            now += 1.0f;
            returnCircuitOpen = false;
            Assert.IsTrue(client.UnlockPremium(client.CurrentUnlockQuote()));
            net.Flush();
            Assert.IsTrue(client.RequiresReconnect);

            now += 1.0f;
            Assert.IsFalse(client.UnlockPremium(client.CurrentUnlockQuote()));

            // 断线后恢复
            net.TriggerDisconnect();
            Assert.IsFalse(client.RequiresReconnect);
        }

        [Test]
        public void ClaimReward_ForwardsBagAndUpdatesSnapshot_AndEquipCosmeticToggles()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 10f;
            net.Responder = (msgId, req) =>
            {
                if (msgId == SceneActivityClientPlayerRpc.GetTravelPassStateMessageId)
                    return (new global::GetTravelPassStateResponse { Snapshot = SampleSnapshot(points: 90, unlocked: true) }, null);
                if (msgId == SceneActivityClientPlayerRpc.ClaimTravelPassRewardMessageId)
                {
                    var claimReq = (global::ClaimTravelPassRewardRequest)req;
                    Assert.AreNotEqual(0UL, claimReq.ClientRequestId);
                    Assert.AreEqual(0u, claimReq.TierIndex);
                    return (new global::ClaimTravelPassRewardResponse
                    {
                        Snapshot = SampleSnapshot(points: 90, unlocked: true, free1Claimed: true, prem1Claimed: true),
                        Bag = new global::BagInfo(),
                        ClaimedCount = 2,
                    }, null);
                }
                if (msgId == SceneActivityClientPlayerRpc.EquipTravelPassCosmeticMessageId)
                {
                    var eqReq = (global::EquipTravelPassCosmeticRequest)req;
                    var updated = SampleSnapshot(points: 90, unlocked: true, free1Claimed: true, prem1Claimed: true);
                    updated.EquippedFrameKey = eqReq.CosmeticKey;
                    return (new global::EquipTravelPassCosmeticResponse { Snapshot = updated }, null);
                }
                throw new InvalidOperationException("unexpected msgId " + msgId);
            };

            using var client = new TravelPassClient(net, () => conn, () => now);
            client.RequestState(force: true);
            net.Flush();

            global::BagInfo receivedBag = null;
            uint claimedCount = 0;
            client.BagReceived += b => receivedBag = b;
            client.RewardClaimed += c => claimedCount = c;

            Assert.IsTrue(client.ClaimReward(0u, false));
            net.Flush();
            Assert.IsNotNull(receivedBag);
            Assert.AreEqual(2u, claimedCount);
            Assert.IsFalse(client.HasClaimableReward);

            now += 1.0f;
            Assert.IsTrue(client.EquipCosmetic(TravelPassModels.CosmeticSlotFrame, "frame_travel_qingyun_s1"));
            net.Flush();
            Assert.AreEqual("frame_travel_qingyun_s1", client.State.EquippedFrameKey);

            // 战斗期间禁止领取/佩戴
            client.InBattle = true;
            now += 1.0f;
            Assert.IsFalse(client.EquipCosmetic(TravelPassModels.CosmeticSlotFrame, ""));
            Assert.IsTrue(client.StatusIsError);
        }

        [Test]
        public void DescribeTip_CoversAllTravelPassErrors_AndFormatsBoundRewardText()
        {
            foreach (travel_pass_error code in Enum.GetValues(typeof(travel_pass_error)))
            {
                if (code == travel_pass_error.KTravelPassErrorOk) continue;
                string text = TravelPassModels.DescribeTip((uint)code);
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"Missing text for {code}");
                Assert.IsFalse(text.Contains("错误码"), $"Fallback text used for {code}");
            }

            string formatted = TravelPassModels.FormatTrackReward(3405, 1, "定星保底符", 3000, "frame_travel_qingyun_s1", "青云游历头像框");
            StringAssert.Contains("定星保底符×1(绑定)", formatted);
            StringAssert.Contains("银两×3000", formatted);
            StringAssert.Contains("头像框·青云游历头像框(零战力)", formatted);
        }
    }
}
