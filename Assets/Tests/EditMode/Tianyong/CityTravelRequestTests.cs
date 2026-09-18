using MmorpgClient.Game.WorldTravel;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class CityTravelRequestTests
    {
        [Test]
        public void RpcAcceptance_DoesNotCompleteTravel()
        {
            var request = new CityTravelRequest();
            int token = request.Begin(2, true, 0);
            Assert.That(request.Accept(token), Is.True);
            Assert.That(request.IsPending, Is.True);
            Assert.That(request.ReceiveScene(2, out bool festival), Is.True);
            Assert.That(festival, Is.True);
            Assert.That(request.IsPending, Is.False);
        }

        [Test]
        public void NotifyBeforeRpc_LateErrorCannotUndoArrival()
        {
            var request = new CityTravelRequest();
            int token = request.Begin(3, false, 0);
            Assert.That(request.ReceiveScene(3, out bool festival), Is.True);
            Assert.That(festival, Is.False);
            Assert.That(request.Fail(token), Is.False);
            Assert.That(request.Accept(token), Is.False);
        }

        [Test]
        public void OtherSceneNotification_DoesNotApplyRequestedAppearance()
        {
            var request = new CityTravelRequest();
            request.Begin(2, true, 0);
            Assert.That(request.ReceiveScene(4, out bool festival), Is.False);
            Assert.That(festival, Is.False);
            Assert.That(request.IsPending, Is.False);
        }

        [Test]
        public void Timeout_RejectsStaleCallbacksAndAllowsRetry()
        {
            var request = new CityTravelRequest();
            int oldToken = request.Begin(2, true, 100);
            Assert.That(request.Tick(129), Is.False);
            Assert.That(request.Tick(130), Is.True);
            int newToken = request.Begin(4, false, 131);
            Assert.That(request.Accept(oldToken), Is.False);
            Assert.That(request.Fail(oldToken), Is.False);
            Assert.That(request.Accept(newToken), Is.True);
            Assert.That(request.Destination, Is.EqualTo(4));
        }

        [Test]
        public void AcceptWithBudget_KeepsWaitingPastDefaultTimeout()
        {
            // 受理后服务端可能转入跨节点交接，最坏一分钟左右才有结论；三十秒到期会先报“未收到抵达消息”。
            var request = new CityTravelRequest();
            int token = request.Begin(2, false, 100);
            Assert.That(request.Accept(token, 101, CityTravelRequest.AcceptedHandoffBudgetSeconds), Is.True);
            Assert.That(request.IsAccepted, Is.True);
            Assert.That(request.Tick(130), Is.False);
            Assert.That(request.Tick(175), Is.False);
            Assert.That(request.IsPending, Is.True);
            Assert.That(request.Tick(176), Is.True);
            Assert.That(request.IsPending, Is.False);
        }

        [Test]
        public void AcceptWithBudget_NeverShortensLongerDeadline()
        {
            var request = new CityTravelRequest();
            int token = request.Begin(2, false, 0, CityTravelRequest.CrossZoneTimeoutSeconds);
            Assert.That(request.Accept(token, 1, CityTravelRequest.AcceptedHandoffBudgetSeconds), Is.True);
            Assert.That(request.Tick(119), Is.False);
            Assert.That(request.Tick(120), Is.True);
        }

        [Test]
        public void AcceptWithBudget_StaleOrFinishedRequestIsNotExtended()
        {
            var request = new CityTravelRequest();
            int oldToken = request.Begin(2, false, 0);
            Assert.That(request.ReceiveScene(2, out _), Is.True);
            Assert.That(request.Accept(oldToken, 1, CityTravelRequest.AcceptedHandoffBudgetSeconds), Is.False);
            request.Begin(3, false, 10);
            Assert.That(request.Accept(oldToken, 11, CityTravelRequest.AcceptedHandoffBudgetSeconds), Is.False);
            Assert.That(request.IsAccepted, Is.False);
            Assert.That(request.Tick(39), Is.False);
            Assert.That(request.Tick(40), Is.True);
        }

        [Test]
        public void AcceptedBudget_CoversServerHandoffWatchdogs()
        {
            // 服务端存盘与等应答两道看门狗各 30 秒、前后串行；客户端受理后的等待必须严格更长。
            Assert.That(CityTravelRequest.AcceptedHandoffBudgetSeconds, Is.GreaterThan(60.0));
            Assert.That(CityTravelRequest.CrossZoneTimeoutSeconds,
                Is.GreaterThan(CityTravelRequest.AcceptedHandoffBudgetSeconds));
        }

        [Test]
        public void DuplicateClick_DoesNotReplaceInFlightDestination()
        {
            var request = new CityTravelRequest();
            request.Begin(2, true, 0);
            Assert.That(request.Begin(3, false, 1), Is.Zero);
            Assert.That(request.Destination, Is.EqualTo(2));
            Assert.That(request.Festival, Is.True);
        }

        [Test]
        public void DisconnectReset_RejectsLateNotifications()
        {
            var request = new CityTravelRequest();
            int token = request.Begin(4, true, 0);
            request.Reset();
            Assert.That(request.ReceiveScene(4, out _), Is.False);
            Assert.That(request.Accept(token), Is.False);
            Assert.That(request.IsPending, Is.False);
        }
    }
}
