using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Common;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.RewardedAd;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class RewardedAdModelAndComplianceTests
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

        private sealed class FakeAdBridge : IRewardedAdBridge
        {
            public uint ClientPlatform { get; set; } = (uint)global::AdClientPlatform.WechatMinigame;
            public bool IsSupported { get; set; } = true;
            public bool IsAdReady { get; set; } = true;
            public string LastPreloadedUnitId { get; private set; } = string.Empty;
            public Action<RewardedAdShowResult> PendingShow { get; private set; }

            public event Action ReadyChanged;

            public void Preload(string adUnitId) => LastPreloadedUnitId = adUnitId;

            public void Show(Action<RewardedAdShowResult> onFinished) => PendingShow = onFinished;

            public void CancelPending() => PendingShow = null;

            public void CompleteShow(RewardedAdShowResult result)
            {
                var cb = PendingShow;
                PendingShow = null;
                cb?.Invoke(result);
            }

            public void SetReady(bool ready)
            {
                IsAdReady = ready;
                ReadyChanged?.Invoke();
            }
        }

        private static global::GetRewardedAdInfoResponse SampleInfo(bool enabled, uint claimedToday = 0,
            uint dailyLimit = 2, ulong cooldownUntilMs = 0, ulong bindGold = 0, ulong gold = 500)
        {
            var resp = new global::GetRewardedAdInfoResponse
            {
                ErrorMessage = new global::TipInfoMessage { Id = 0 },
                ServerTimeMs = 1_760_000_000_000UL,
                BindGoldBalance = bindGold,
                GoldBalance = gold,
            };
            resp.Slots.Add(new global::RewardedAdSlotView
            {
                SlotId = RewardedAdModel.DefaultSlotId,
                Enabled = enabled,
                MinLevel = 10,
                DailyLimit = dailyLimit,
                ClaimedToday = claimedToday,
                NextResetMs = 1_760_086_400_000UL,
                CooldownUntilMs = cooldownUntilMs,
                CurrencyType = CurrencyLabels.BindGold,
                RewardAmount = 300,
            });
            return resp;
        }

        [Test]
        public void CurrencyLabelsAndEquipEnhanceDualSilverAntiLaundering()
        {
            Assert.That(CurrencyLabels.Name(0), Is.EqualTo("银两"));
            Assert.That(CurrencyLabels.Name(1), Is.EqualTo("灵石"));
            Assert.That(CurrencyLabels.Name(2), Is.EqualTo("绑定灵石"));
            Assert.That(CurrencyLabels.Name(3), Is.EqualTo("绑定银两"));
            Assert.That(CurrencyLabels.Name(99), Is.EqualTo("货币"));
            Assert.That(CurrencyLabels.Format(1200, CurrencyLabels.BindGold), Is.EqualTo("1,200 绑定银两"));

            // A/B 防洗白前置检验：绑定装允许 绑定银两 + 银两 合并支付；可交易装只认流通银两
            var bag = new global::BagInfo
            {
                Currency = new global::CurrencyComp(),
            };
            bag.Currency.Values.Add(100); // GOLD
            bag.Currency.Values.Add(0);   // DIAMOND
            bag.Currency.Values.Add(0);   // BIND_DIAMOND
            bag.Currency.Values.Add(300); // BIND_GOLD

            var boundEquip = new global::BagItemInfo
            {
                Bound = true,
                EnhanceLevel = 0,
                EnhanceMaxLevel = 10,
                EnhanceNext = new global::EquipEnhancePreview { CostGold = 350 },
            };
            var tradableEquip = boundEquip.Clone();
            tradableEquip.Bound = false;

            Assert.That(EquipBagModel.CheckEnhance(boundEquip, bag), Is.EqualTo(EquipEnhanceCheck.None));
            Assert.That(EquipBagModel.CheckEnhance(tradableEquip, bag), Is.EqualTo(EquipEnhanceCheck.GoldNotEnough));
        }

        [Test]
        public void EntryVisibilityEnforcesZeroRejectionFiveGates()
        {
            var enabledInfo = SampleInfo(enabled: true, claimedToday: 0, dailyLimit: 2);
            ulong now = enabledInfo.ServerTimeMs;

            // 全部 5 项条件满足 -> 展示入口
            var ok = RewardedAdModel.Evaluate(enabledInfo, 1, true, true, "adunit-1", false, false, now);
            Assert.That(ok.ShouldShowEntry, Is.True);
            Assert.That(ok.CanClickWatch, Is.True);

            // 任一条件不满足 -> 彻底隐藏入口（防 App Store 2.1 空按钮与首审暴露）
            Assert.That(RewardedAdModel.Evaluate(SampleInfo(enabled: false), 1, true, true, "adunit-1", false, false, now).ShouldShowEntry, Is.False);
            Assert.That(RewardedAdModel.Evaluate(enabledInfo, 1, false, true, "adunit-1", false, false, now).ShouldShowEntry, Is.False);
            Assert.That(RewardedAdModel.Evaluate(enabledInfo, 1, true, false, "adunit-1", false, false, now).ShouldShowEntry, Is.False);
            Assert.That(RewardedAdModel.Evaluate(enabledInfo, 1, true, true, "", false, false, now).ShouldShowEntry, Is.False);
            Assert.That(RewardedAdModel.Evaluate(SampleInfo(enabled: true, claimedToday: 2, dailyLimit: 2), 1, true, true, "adunit-1", false, false, now).ShouldShowEntry, Is.False);
            Assert.That(RewardedAdModel.Evaluate(enabledInfo, 1, true, true, "adunit-1", true, false, now).ShouldShowEntry, Is.False);

            // 冷却中：仍可看到提示但按钮不可点
            var cooling = RewardedAdModel.Evaluate(SampleInfo(enabled: true, cooldownUntilMs: now + 30_000UL), 1, true, true, "adunit-1", false, false, now);
            Assert.That(cooling.ShouldShowEntry, Is.True);
            Assert.That(cooling.InCooldown, Is.True);
            Assert.That(cooling.CanClickWatch, Is.False);
        }

        [Test]
        public void EarlyDismissDoesNotClaim_AndDisconnectDuringWatchAutoReclaimsIdempotently()
        {
            var net = new FakeTransport();
            var bridge = new FakeAdBridge();
            object conn = new object();
            float nowSec = 10f;
            var ids = new RewardedAdMessageIds(540, 541, 542);

            using var client = new RewardedAdClient(net, () => conn, () => nowSec, bridge, 1, ids);
            client.ConfigureAdUnitId("adunit-test-1");

            net.Responder = (msgId, req) =>
            {
                if (msgId == 540) return (SampleInfo(enabled: true), null);
                if (msgId == 541) return (new global::BeginRewardedAdSessionResponse
                {
                    ErrorMessage = new global::TipInfoMessage { Id = 0 },
                    AdSessionId = 777888UL,
                    ExpiresAtMs = 1_760_000_300_000UL,
                }, null);
                if (msgId == 542)
                {
                    return (new global::ClaimRewardedAdResponse
                    {
                        ErrorMessage = new global::TipInfoMessage { Id = 0 },
                        CurrencyType = CurrencyLabels.BindGold,
                        RewardAmount = 300,
                        CurrencyBalance = 300,
                        GoldBalance = 500,
                        Slot = SampleInfo(enabled: true, claimedToday: 1, bindGold: 300).Slots[0],
                    }, null);
                }
                return (null, "unexpected");
            };

            client.RequestInfo(force: true);
            net.Flush();
            Assert.That(client.Snapshot().ShouldShowEntry, Is.True);

            // 1. 中途关闭广告 -> 绝不发起 ClaimRewardedAd
            Assert.That(client.StartWatch(), Is.True);
            net.Flush();
            bridge.CompleteShow(new RewardedAdShowResult(RewardedAdShowStatus.DismissedEarly));
            Assert.That(net.Calls.Exists(c => c.msgId == 542), Is.False);
            Assert.That(client.HasPendingClaim, Is.False);

            // 2. 完整观看期间发生 30 秒切后台断连 -> 保留 PendingClaim，重连后自动携同幂等键补领
            Assert.That(client.StartWatch(), Is.True);
            net.Flush();
            net.TriggerDisconnect();
            bridge.CompleteShow(new RewardedAdShowResult(RewardedAdShowStatus.Completed));
            Assert.That(client.HasPendingClaim, Is.True);
            Assert.That(net.Calls.Exists(c => c.msgId == 542), Is.False);

            // 模拟重连换新连接对象，Tick() 自动补领
            conn = new object();
            net.IsReady = true;
            nowSec += 5f;
            client.Tick();
            net.Flush();

            Assert.That(client.HasPendingClaim, Is.False);
            Assert.That(net.Calls.FindAll(c => c.msgId == 542).Count, Is.EqualTo(1));
            Assert.That(client.Snapshot().BindGoldBalance, Is.EqualTo(300UL));
            Assert.That(client.Snapshot().ClaimedToday, Is.EqualTo(1u));
        }

        [Test]
        public void CompliancePolicyScanForbidsInducementAndDonationStrings()
        {
            string[] forbiddenWords =
            {
                "支持作者", "支持开发", "捐赠", "打赏", "点广告", "点一下", "帮忙点",
                "收款码", "微信收款", "支付宝收款", "爱发电", "赞助作者", "点击广告",
            };

            // 1. 校验运行时文案输出
            var runtimeStrings = new List<string>
            {
                RewardedAdModel.VideoBadgeText,
                RewardedAdModel.EntryButtonText,
                RewardedAdModel.DialogTitleText,
                RewardedAdModel.ConfirmWatchButtonText,
                RewardedAdModel.CancelButtonText,
                RewardedAdModel.WatchingStatusText,
                RewardedAdModel.ClaimingStatusText,
                RewardedAdModel.DismissedEarlyStatusText,
                RewardedAdModel.UnavailableStatusText,
                RewardedAdModel.ReconnectPendingStatusText,
                RewardedAdModel.BuildDialogBody(300, CurrencyLabels.BindGold, 2, 2),
                RewardedAdModel.BuildBalanceLine(300, 500),
                RewardedAdModel.BuildClaimedToast(300, CurrencyLabels.BindGold),
                RewardedAdModel.BuildCooldownButtonText(30),
            };
            foreach (uint code in new uint[]
            {
                (uint)rewarded_ad_error.KRewardedAdUnavailable,
                (uint)rewarded_ad_error.KRewardedAdDisabled,
                (uint)rewarded_ad_error.KRewardedAdPlatformUnsupported,
                (uint)rewarded_ad_error.KRewardedAdLevelNotEnough,
                (uint)rewarded_ad_error.KRewardedAdDailyLimitReached,
                (uint)rewarded_ad_error.KRewardedAdCooldown,
                (uint)rewarded_ad_error.KRewardedAdSessionInvalid,
                (uint)rewarded_ad_error.KRewardedAdSessionExpired,
                (uint)rewarded_ad_error.KRewardedAdWatchTooShort,
                (uint)rewarded_ad_error.KRewardedAdCurrencyBlocked,
                (uint)rewarded_ad_error.KRewardedAdInBattle,
                (uint)rewarded_ad_error.KRewardedAdFrozen,
                (uint)rewarded_ad_error.KRewardedAdSsvRequired,
            })
            {
                runtimeStrings.Add(RewardedAdModel.DescribeTip(code));
            }

            foreach (string text in runtimeStrings)
            {
                foreach (string word in forbiddenWords)
                {
                    Assert.That(text, Does.Not.Contain(word),
                        $"Forbidden compliance inducement word '{word}' detected in UI text: {text}");
                }
            }

            // 2. 扫描 RewardedAd 源码中全部字符串字面量，防止后续误加诱导/捐赠文案
            string adSourceDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts", "Game", "RewardedAd");
            if (Directory.Exists(adSourceDir))
            {
                var stringLiteralRegex = new Regex("\"([^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);
                foreach (string file in Directory.GetFiles(adSourceDir, "*.cs", SearchOption.AllDirectories))
                {
                    string src = File.ReadAllText(file);
                    foreach (System.Text.RegularExpressions.Match m in stringLiteralRegex.Matches(src))
                    {
                        foreach (string word in forbiddenWords)
                        {
                            Assert.That(m.Value, Does.Not.Contain(word),
                                $"Forbidden word '{word}' in string literal of {Path.GetFileName(file)}: {m.Value}");
                        }
                    }
                }
            }
        }
    }
}
