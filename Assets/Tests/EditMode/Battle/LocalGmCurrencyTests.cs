#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.App;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class LocalGmCurrencyTests
    {
        internal sealed class Channel : IRpcChannel
        {
            public readonly List<GmAddCurrencyRequest> Requests = new();
            public uint MessageId;
            public Action<RpcStatus, GmAddCurrencyResponse> Complete;
            public void CallMethod<T>(uint id, IMessage request, MessageParser<T> parser, RpcDone<T> done) where T : IMessage<T>
            {
                MessageId = id;
                Requests.Add((GmAddCurrencyRequest)request);
                Complete = (status, response) => done(status, response == null ? default : parser.ParseFrom(response.ToByteArray()));
            }
            public void SendOneWay(uint id, IMessage request) => Assert.Fail("不应发送单向加钱消息");
            public void RegisterNotify(uint id, Action<MessageContent> handler) { }
        }

        [TestCase("http://127.0.0.1:8081", "127.0.0.1:20000", true)]
        [TestCase("http://localhost:8081", "localhost:20000", true)]
        [TestCase("http://[::1]:8081", "[::1]:20000", true)]
        [TestCase("https://localhost:8081", "127.0.0.1:20000", true)]
        [TestCase("http://game.example:8081", "127.0.0.1:20000", false)]
        [TestCase("http://localhost:8081", "game.example:20000", false)]
        [TestCase("http://localhost:8081", "192.168.1.2:20000", false)]
        [TestCase("http://localhost.evil:8081", "127.0.0.1:20000", false)]
        [TestCase("http://localhost@evil:8081", "127.0.0.1:20000", false)]
        [TestCase("http://127.0.0.1:8081", "localhost@evil:20000", false)]
        [TestCase("http://127.0.0.1:8081/proxy", "127.0.0.1:20000", false)]
        [TestCase("http://127.0.0.1:8081", "127.0.0.1:20000/proxy", false)]
        [TestCase("file://localhost/test", "127.0.0.1:20000", false)]
        [TestCase("http://localhost:8081", null, false)]
        public void OnlyLoopbackGatewayAndGateAreAllowed(string gateway, string gate, bool allowed)
            => Assert.That(LocalGmCurrencyClient.MatchesEndpoints(gateway, gate), Is.EqualTo(allowed));

        [Test]
        public void SingleClickUsesGeneratedRpcAndAuthoritativeBalanceThenRefreshes()
        {
            var channel = new Channel();
            var session = Local(channel);
            int refreshes = 0;
            var client = new LocalGmCurrencyClient(() => session, _ => ++refreshes);
            client.AddSilver(); client.AddSilver();
            Assert.That(channel.Requests.Count, Is.EqualTo(1));
            Assert.That(channel.MessageId, Is.EqualTo(SceneCurrencyClientPlayerRpc.GmAddCurrencyMessageId));
            Assert.That(channel.Requests[0].CurrencyType, Is.Zero);
            Assert.That(channel.Requests[0].Amount, Is.EqualTo(100000));
            Assert.That(client.CanSend, Is.False);
            channel.Complete(RpcStatus.Ok, new GmAddCurrencyResponse { BalanceAfter = 123456 });
            Assert.That(client.Notice, Does.Contain(123456.ToString("N0")));
            Assert.That(refreshes, Is.EqualTo(1));
            Assert.That(client.CanSend, Is.True);
        }

        [Test]
        public void RemoteOrNotEnteredSessionCannotSendEvenWhenInvokedDirectly()
        {
            var channel = new Channel();
            var session = Local(channel);
            var client = new LocalGmCurrencyClient(() => session, _ => Assert.Fail("拒绝请求不刷新"));
            session = new LocalGmCurrencyClient.Session(channel, new object(), 1, true, true, "http://localhost:8081", "remote:20000");
            client.AddSilver();
            session = new LocalGmCurrencyClient.Session(channel, new object(), 1, false, true, "http://localhost:8081", "localhost:20000");
            client.AddSilver();
            Assert.That(channel.Requests, Is.Empty);
        }

        [Test]
        public void TimeoutIsUnknownAndNeverAutomaticallyResent()
        {
            var channel = new Channel();
            var session = Local(channel);
            var client = new LocalGmCurrencyClient(() => session, _ => Assert.Fail("未知结果不冒充成功刷新"));
            client.AddSilver();
            channel.Complete(RpcStatus.Timeout("timeout"), null);
            Assert.That(client.OutcomeUnknown, Is.True);
            Assert.That(client.Notice, Does.Contain("结果未知"));
            Assert.That(channel.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void SwitchingCharactersClearsThePreviousCharactersDisplayedBalance()
        {
            var channel = new Channel();
            var session = Local(channel);
            var client = new LocalGmCurrencyClient(() => session, null);
            client.AddSilver();
            channel.Complete(RpcStatus.Ok, new GmAddCurrencyResponse { BalanceAfter = 987654 });
            session = Local(channel);
            client.SynchronizeSession();
            Assert.That(client.Notice, Does.Not.Contain("987"));
            Assert.That(channel.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void PlayerChangeOnSameConnectionCannotApplyOldBalance()
        {
            var channel = new Channel();
            var connection = new object();
            var session = new LocalGmCurrencyClient.Session(channel, connection, 1, true, true, "http://localhost:8081", "localhost:20000");
            var client = new LocalGmCurrencyClient(() => session, _ => Assert.Fail("不能刷新另一个角色"));
            client.AddSilver();
            session = new LocalGmCurrencyClient.Session(channel, connection, 2, true, true, "http://localhost:8081", "localhost:20000");
            channel.Complete(RpcStatus.Ok, new GmAddCurrencyResponse { BalanceAfter = 987654 });
            Assert.That(client.OutcomeUnknown, Is.True);
            Assert.That(client.Notice, Does.Not.Contain("987"));
            Assert.That(channel.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConnectionChangeCannotApplyOldBalanceToNewSession()
        {
            var channel = new Channel();
            var session = Local(channel);
            var client = new LocalGmCurrencyClient(() => session, _ => Assert.Fail("不能刷新另一个连接"));
            client.AddSilver();
            session = Local(channel);
            channel.Complete(RpcStatus.Ok, new GmAddCurrencyResponse { BalanceAfter = 987654 });
            Assert.That(client.OutcomeUnknown, Is.True);
            Assert.That(client.Notice, Does.Not.Contain("987"));
        }

        private static LocalGmCurrencyClient.Session Local(Channel channel)
            => new(channel, new object(), 1, true, true, "http://127.0.0.1:8081", "127.0.0.1:20000");
    }

    public sealed class LocalGmCurrencyUiTests
    {
        [Test]
        public void ActualButtonBindingRechecksEnvironmentAndBlocksDuplicateClicks()
        {
            var root = new UnityEngine.GameObject("LocalGmUiTest");
            try
            {
                var view = root.AddComponent<LocalGmCurrencyDebugUi>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var field = typeof(LocalGmCurrencyDebugUi).GetField("_client", flags);
                if (field.GetValue(view) == null) typeof(LocalGmCurrencyDebugUi).GetMethod("Awake", flags).Invoke(view, null);
                var channel = new LocalGmCurrencyTests.Channel();
                var session = new LocalGmCurrencyClient.Session(channel, new object(), 1, true, true, "http://remote:8081", "localhost:20000");
                var client = new LocalGmCurrencyClient(() => session, null);
                field.SetValue(view, client);
                var buttons = root.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                Assert.That(buttons.Length, Is.EqualTo(1), "测试面板只保留一个加银两入口");
                var button = buttons[0];
                Assert.That(button.gameObject.activeInHierarchy, Is.False, "未进入本地角色前不显示测试按钮");
                button.onClick.Invoke();
                Assert.That(channel.Requests, Is.Empty, "绕过按钮显隐直接调用事件也不能向外部服发包");
                session = new LocalGmCurrencyClient.Session(channel, new object(), 1, true, true, "http://localhost:8081", "localhost:20000");
                button.onClick.Invoke(); button.onClick.Invoke();
                Assert.That(channel.Requests.Count, Is.EqualTo(1));
                Assert.That(channel.Requests[0].Amount, Is.EqualTo(100000));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
#endif
