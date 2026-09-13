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
