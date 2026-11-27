using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class BattleSpectateChatClientTests
    {
        [Test]
        public void RefreshAndToggleSetting_PersistsPreferenceAndEffectiveAllow()
        {
            var net = new FakeBattleTransport { PlayerId = 1001UL, IsReady = true };
            double now = 10.0;
            using var client = new BattleSpectateChatClient(net, () => "conn-1", () => now);

            Assert.IsTrue(client.RefreshSetting());
            var getCalls = net.CallsOf(SceneSceneClientPlayerRpc.GetSpectateChatSettingMessageId);
            Assert.AreEqual(1, getCalls.Count);
            getCalls[0].Respond(new GetSpectateChatSettingResponse
            {
                Preference = eSpectateChatPreference.SpectateChatPrefUnspecified,
                EffectiveAllow = true,
            });
            Assert.IsTrue(client.SettingLoaded);
            Assert.IsTrue(client.EffectiveAllowSpectateChat);
            Assert.AreEqual(eSpectateChatPreference.SpectateChatPrefUnspecified, client.Preference);

            Assert.IsTrue(client.SetAllowSpectateChat(false));
            var setCalls = net.CallsOf(SceneSceneClientPlayerRpc.SetSpectateChatSettingMessageId);
            Assert.AreEqual(1, setCalls.Count);
            var req = (SetSpectateChatSettingRequest)setCalls[0].Request;
            Assert.AreEqual(eSpectateChatPreference.SpectateChatPrefForbid, req.Preference);
            setCalls[0].Respond(new SetSpectateChatSettingResponse
            {
                Preference = eSpectateChatPreference.SpectateChatPrefForbid,
                EffectiveAllow = false,
            });
            Assert.IsFalse(client.EffectiveAllowSpectateChat);
            Assert.AreEqual(eSpectateChatPreference.SpectateChatPrefForbid, client.Preference);
            StringAssert.Contains("仍可正常观战", client.SettingStatus);
        }

        [Test]
        public void DisabledBattle_NeverPollsAndRejectsSendLocally()
        {
            var net = new FakeBattleTransport { PlayerId = 2002UL, IsReady = true };
            double now = 1.0;
            using var client = new BattleSpectateChatClient(net, () => "conn-1", () => now);

            client.SyncBattleContext(8801UL, chatEnabled: false, pollingActive: true);
            client.Tick(now + 10.0);

            Assert.AreEqual(0, net.CallsOf(MatchServiceRpc.PullBattleSpectateChatMessageId).Count);
            Assert.IsFalse(client.Send(8801UL, "加油"));
            Assert.AreEqual(0, net.CallsOf(MatchServiceRpc.SendBattleSpectateChatMessageId).Count);
            StringAssert.Contains("关闭观战交流", client.LastError);
        }

        [Test]
        public void EnabledBattle_SendPullDedupCooldownAndBattleEndedStopPolling()
        {
            var net = new FakeBattleTransport { PlayerId = 3003UL, IsReady = true };
            double now = 100.0;
            using var client = new BattleSpectateChatClient(net, () => "conn-1", () => now);

            client.SyncBattleContext(9901UL, chatEnabled: true, pollingActive: true);
            var initialPulls = net.CallsOf(MatchServiceRpc.PullBattleSpectateChatMessageId);
            Assert.AreEqual(1, initialPulls.Count);
            initialPulls[0].Respond(new PullBattleSpectateChatResponse
            {
                Enabled = true,
                LatestSeq = 1UL,
                ServerTimeMs = 100000UL,
                Messages =
                {
                    new BattleSpectateChatMessage
                    {
                        Seq = 1UL,
                        SenderPlayerId = 4004UL,
                        SenderName = "局外看客",
                        Role = BattleSpectateChatRole.Spectator,
                        Content = "好险！",
                        CreatedMs = 99900UL,
                    }
                },
            });
            Assert.AreEqual(1, client.Messages.Count);
            Assert.AreEqual("观战", client.Messages[0].RoleTag);

            Assert.IsTrue(client.Send(9901UL, "下回合控主"));
            var sendCalls = net.CallsOf(MatchServiceRpc.SendBattleSpectateChatMessageId);
            Assert.AreEqual(1, sendCalls.Count);
            sendCalls[0].Respond(new SendBattleSpectateChatResponse
            {
                Enabled = true,
                ServerTimeMs = 101000UL,
                CooldownUntilMs = 105000UL,
                Message = new BattleSpectateChatMessage
                {
                    Seq = 2UL,
                    SenderPlayerId = 3003UL,
                    SenderName = "我方弟子",
                    Role = BattleSpectateChatRole.Participant,
                    Content = "下回合控主",
                    CreatedMs = 101000UL,
                },
            });
            Assert.AreEqual(2, client.Messages.Count);
            Assert.AreEqual("参战", client.Messages[1].RoleTag);
            Assert.Greater(client.CooldownRemainingSeconds(), 3.5);

            // 增量轮询：重复 seq 应被去重，BattleEnded 后停止继续轮询
            now = 103.0;
            client.Tick(now);
            var pulls = net.CallsOf(MatchServiceRpc.PullBattleSpectateChatMessageId);
            Assert.AreEqual(2, pulls.Count);
            Assert.AreEqual(2UL, ((PullBattleSpectateChatRequest)pulls[1].Request).AfterSeq);
            pulls[1].Respond(new PullBattleSpectateChatResponse
            {
                Enabled = true,
                BattleEnded = true,
                LatestSeq = 2UL,
                ServerTimeMs = 103000UL,
                Messages =
                {
                    new BattleSpectateChatMessage
                    {
                        Seq = 2UL,
                        SenderPlayerId = 3003UL,
                        SenderName = "我方弟子",
                        Role = BattleSpectateChatRole.Participant,
                        Content = "下回合控主",
                        CreatedMs = 101000UL,
                    }
                },
            });
            Assert.AreEqual(2, client.Messages.Count);
            Assert.IsTrue(client.BattleEnded);

            now = 120.0;
            client.Tick(now);
            Assert.AreEqual(2, net.CallsOf(MatchServiceRpc.PullBattleSpectateChatMessageId).Count);
        }

        [Test]
        public void BattleSwitchAndDisconnect_ResetMessagesAndIgnoreStaleCallback()
        {
            var net = new FakeBattleTransport { PlayerId = 5005UL, IsReady = true };
            double now = 50.0;
            using var client = new BattleSpectateChatClient(net, () => "conn-1", () => now);

            client.SyncBattleContext(7001UL, chatEnabled: true, pollingActive: true);
            var firstPull = net.CallsOf(MatchServiceRpc.PullBattleSpectateChatMessageId)[0];

            // 切场后旧场回包到达，必须丢弃
            client.SyncBattleContext(7002UL, chatEnabled: true, pollingActive: true);
            firstPull.Respond(new PullBattleSpectateChatResponse
            {
                Enabled = true,
                LatestSeq = 1UL,
                Messages =
                {
                    new BattleSpectateChatMessage
                    {
                        Seq = 1UL,
                        SenderPlayerId = 9999UL,
                        SenderName = "旧场消息",
                        Role = BattleSpectateChatRole.Spectator,
                        Content = "不应污染新场",
                    }
                },
            });
            Assert.AreEqual(0, client.Messages.Count);
            Assert.AreEqual(7002UL, client.ActiveBattleId);

            // 断线复位
            net.RaiseDisconnected();
            Assert.AreEqual(0UL, client.ActiveBattleId);
            Assert.IsFalse(client.SettingLoaded);
            Assert.IsFalse(client.ChatEnabled);
        }

        [Test]
        public void LocalMute_PausesTickPollingAndResumesCleanly()
        {
            var net = new FakeBattleTransport { PlayerId = 6006UL, IsReady = true };
            double now = 200.0;
            using var client = new BattleSpectateChatClient(net, () => "conn-1", () => now);

            client.SyncBattleContext(8101UL, chatEnabled: true, pollingActive: true);
            var pulls = net.CallsOf(MatchServiceRpc.PullBattleSpectateChatMessageId);
            Assert.AreEqual(1, pulls.Count);
            pulls[0].Respond(new PullBattleSpectateChatResponse { Enabled = true, LatestSeq = 1UL, ServerTimeMs = 200000UL });

            // 本地静音：关闭轮询后多次 Tick 均不发拉取请求
            client.SetPollingActive(false);
            now = 210.0;
            client.Tick(now);
            now = 225.0;
            client.Tick(now);
            Assert.AreEqual(1, net.CallsOf(MatchServiceRpc.PullBattleSpectateChatMessageId).Count);

            // 取消静音：立即恢复一次拉取
            client.SetPollingActive(true);
            Assert.AreEqual(2, net.CallsOf(MatchServiceRpc.PullBattleSpectateChatMessageId).Count);
        }
    }
}
