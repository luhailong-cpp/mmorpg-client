using System.Collections.Generic;
using MmorpgClient.Game.Attribute;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// <see cref="AttributeClient"/> 状态机用例(不经 Attach,避免污染单例)。
    ///
    /// 覆盖的是"客户端不能自作主张"这条契约:面板只由服务器给,写请求单飞,
    /// 断线作废,自动加点只回建议不改面板。
    /// </summary>
    public sealed class AttributeClientTests
    {
        private FakeBattleTransport _net;
        private AttributeClient _client;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport();
            _client = new AttributeClient(_net);
        }

        private static AttributePanelInfo MakePanel(uint remaining = 5, uint allocated = 0)
        {
            var panel = new AttributePanelInfo { ActiveSchemeId = 1, MaxSchemes = 3, Level = 10 };
            panel.Pools.Add(new AttributePoolInfo
            {
                PoolId = 1, Name = "属性点", Total = remaining + allocated, Remaining = remaining, Unlocked = true,
            });
            panel.Dimensions.Add(new AttributeDimensionInfo
            {
                DimensionId = 101, PoolId = 1, Name = "体质", Desc = "体质:提高气血上限和防御。",
                Allocated = allocated, Value = 10 + allocated,
            });
            panel.Schemes.Add(new AttributeSchemeInfo { SchemeId = 1, Name = "方案一" });
            panel.Derived = new DerivedAttributeInfo { MaxHealth = 500, Health = 500, Defense = 20 };
            return panel;
        }

        [Test]
        public void RequestPanelAppliesServerPanel()
        {
            AttributePanelInfo received = null;
            _client.OnPanel += p => received = p;

            _client.RequestPanel();
            var call = _net.CallsOf(MessageIds.GetAttributePanel);
            Assert.That(call, Has.Count.EqualTo(1));

            call[0].Respond(new GetAttributePanelResponse { Panel = MakePanel() });

            Assert.That(received, Is.Not.Null);
            Assert.That(_client.HasPanel, Is.True);
            Assert.That(_client.Panel.Pools[0].Remaining, Is.EqualTo(5u));
        }

        [Test]
        public void AllocateSendsTargetValuesAndAppliesReturnedPanel()
        {
            _client.Allocate(1, new Dictionary<uint, uint> { { 101, 3 } });

            var call = _net.CallsOf(MessageIds.AllocateAttributePoints);
            Assert.That(call, Has.Count.EqualTo(1));
            var request = (AllocateAttributePointsRequest)call[0].Request;
            Assert.That(request.PoolId, Is.EqualTo(1u));
            // 目标值语义:发的是"最终应为 3",不是"+3"
            Assert.That(request.Allocated[101], Is.EqualTo(3u));
            Assert.That(_client.Busy, Is.True);

            call[0].Respond(new AllocateAttributePointsResponse { Panel = MakePanel(2, 3) });

            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.Panel.Dimensions[0].Allocated, Is.EqualTo(3u));
        }

        [Test]
        public void SecondWriteIsRejectedWhileFirstIsInFlight()
        {
            string error = null;
            _client.OnError += e => error = e;

            _client.Allocate(1, new Dictionary<uint, uint> { { 101, 1 } });
            _client.ResetPool(1);

            Assert.That(_net.CallsOf(MessageIds.ResetAttributePoints), Is.Empty);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void FailedWriteClearsBusyAndSurfacesError()
        {
            string error = null;
            _client.OnError += e => error = e;

            _client.Allocate(1, new Dictionary<uint, uint> { { 101, 1 } });
            _net.CallsOf(MessageIds.AllocateAttributePoints)[0].FailWith("server tip=133");

            Assert.That(_client.Busy, Is.False);
            Assert.That(error, Is.EqualTo("server tip=133"));
            // 失败不得动本地面板(此处本来就没有,断言不会被凭空造出来)
            Assert.That(_client.HasPanel, Is.False);
        }

        [Test]
        public void EmptyAllocationIsRejectedLocallyWithoutNetworkCall()
        {
            _client.Allocate(1, new Dictionary<uint, uint>());

            Assert.That(_net.CallsOf(MessageIds.AllocateAttributePoints), Is.Empty);
            Assert.That(_client.Busy, Is.False);
        }

        [Test]
        public void AutoAllocateReturnsSuggestionWithoutTouchingPanelOrBusy()
        {
            _client.RequestPanel();
            _net.CallsOf(MessageIds.GetAttributePanel)[0].Respond(
                new GetAttributePanelResponse { Panel = MakePanel() });

            uint suggestedPool = 0;
            IReadOnlyDictionary<uint, uint> suggestion = null;
            _client.OnAutoSuggestion += (pool, map) => { suggestedPool = pool; suggestion = map; };

            _client.AutoAllocate(1);
            Assert.That(_client.Busy, Is.False, "自动加点只算不落,不该占用写请求名额");

            var response = new AutoAllocateAttributePointsResponse { PoolId = 1 };
            response.Suggested[101] = 5;
            _net.CallsOf(MessageIds.AutoAllocateAttributePoints)[0].Respond(response);

            Assert.That(suggestedPool, Is.EqualTo(1u));
            Assert.That(suggestion[101], Is.EqualTo(5u));
            // 建议不是权威:面板仍是服务器上次给的那份
            Assert.That(_client.Panel.Dimensions[0].Allocated, Is.EqualTo(0u));
        }

        [Test]
        public void ServerPushReplacesPanel()
        {
            _client.RequestPanel();
            _net.CallsOf(MessageIds.GetAttributePanel)[0].Respond(
                new GetAttributePanelResponse { Panel = MakePanel() });

            _net.PushNotify(MessageIds.NotifyAttributePanelChanged,
                new AttributePanelChangedS2C { Panel = MakePanel(9, 1) });

            Assert.That(_client.Panel.Pools[0].Remaining, Is.EqualTo(9u));
            Assert.That(_client.Panel.Dimensions[0].Allocated, Is.EqualTo(1u));
        }

        [Test]
        public void DisconnectDropsPanelAndInFlightWrite()
        {
            _client.RequestPanel();
            _net.CallsOf(MessageIds.GetAttributePanel)[0].Respond(
                new GetAttributePanelResponse { Panel = MakePanel() });
            _client.Allocate(1, new Dictionary<uint, uint> { { 101, 1 } });
            Assert.That(_client.Busy, Is.True);

            _net.RaiseDisconnected();

            Assert.That(_client.HasPanel, Is.False);
            Assert.That(_client.Busy, Is.False);
        }

        [Test]
        public void TipInResponseBodyIsRejectionKeepsPanelAndClearsBusy()
        {
            // 服务端拒绝码放在响应体 error_message(信封 error_message 为空 → 走 onResponse 而不是 onError)
            _client.RequestPanel();
            _net.CallsOf(MessageIds.GetAttributePanel)[0].Respond(
                new GetAttributePanelResponse { Panel = MakePanel() });
            string error = null;
            _client.OnError += e => error = e;

            _client.Allocate(1, new Dictionary<uint, uint> { { 101, 3 } });
            _net.CallsOf(MessageIds.AllocateAttributePoints)[0].Respond(
                new AllocateAttributePointsResponse { ErrorMessage = new TipInfoMessage { Id = 134 } });

            Assert.That(_client.Busy, Is.False);
            Assert.That(error, Does.Contain("不能减少"));
            // 旧面板保留,没有被 null 面板冲掉
            Assert.That(_client.HasPanel, Is.True);
            Assert.That(_client.Panel.Dimensions[0].Allocated, Is.EqualTo(0u));
        }

        [Test]
        public void AutoAllocateTipInBodyRaisesErrorWithoutSuggestion()
        {
            string error = null;
            IReadOnlyDictionary<uint, uint> suggestion = null;
            _client.OnError += e => error = e;
            _client.OnAutoSuggestion += (_, map) => suggestion = map;

            _client.AutoAllocate(3);
            _net.CallsOf(MessageIds.AutoAllocateAttributePoints)[0].Respond(
                new AutoAllocateAttributePointsResponse { ErrorMessage = new TipInfoMessage { Id = 131 } });

            Assert.That(suggestion, Is.Null);
            Assert.That(error, Does.Contain("尚未解锁"));
        }

        [Test]
        public void NotReadyTransportRejectsBeforeSending()
        {
            _net.IsReady = false;
            string error = null;
            _client.OnError += e => error = e;

            _client.RequestPanel();
            _client.Allocate(1, new Dictionary<uint, uint> { { 101, 1 } });

            Assert.That(_net.Calls, Is.Empty);
            Assert.That(error, Is.Not.Null);
        }
    }
}
