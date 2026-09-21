using Google.Protobuf;
using MmorpgClient.Game;
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

        // ── 以下是 GameClient 上与行程相关的纯静态判据（不需要网络）。本任务不新增测试文件，就近放在这里。──

        [Test]
        public void EnterFailureTip_OnlyRecognisesContractCode()
        {
            // 契约：EnterGame 受理之后进场没成，服务端统一推 kEnterSceneFailed；别的码一律不下结论，交给超时兜底。
            Assert.That(GameClient.IsEnterFailureTip((uint)scene_error.KEnterSceneFailed), Is.True);
            Assert.That(GameClient.IsEnterFailureTip((uint)scene_error.KZoneTravelTargetBusy), Is.False);
            Assert.That(GameClient.IsEnterFailureTip((uint)scene_error.KEnterSceneSceneNotFound), Is.False);
            Assert.That(GameClient.IsEnterFailureTip(0), Is.False);
        }

        [Test]
        public void EnterFailureTip_IsAlsoATravelFailureTipWithText()
        {
            // 同一个码在游戏内换图失败时也会出现，两个判据都认它；文案必须是人话而不是裸编号。
            uint id = (uint)scene_error.KEnterSceneFailed;
            Assert.That(GameClient.IsTravelFailureTip(id), Is.True);
            Assert.That(GameClient.DescribeTravelTip(id), Does.Not.Contain("tip="));
        }

        [Test]
        public void DescribeTravelTip_SyncRejectCodesHaveText_UnknownFallsBackToNumber()
        {
            Assert.That(GameClient.DescribeTravelTip((uint)scene_error.KEnterSceneSceneNotFound),
                Does.Not.Contain("tip="));
            Assert.That(GameClient.DescribeTravelTip((uint)scene_error.KEnterSceneChangingScene),
                Does.Not.Contain("tip="));
            // 同步拒绝码有文案，但不属于“受理后失败”，不能让在途的行程因此收场。
            Assert.That(GameClient.IsTravelFailureTip((uint)scene_error.KEnterSceneSceneNotFound), Is.False);
            Assert.That(GameClient.IsTravelFailureTip((uint)scene_error.KEnterSceneChangingScene), Is.False);
            Assert.That(GameClient.DescribeTravelTip(uint.MaxValue), Does.Contain("tip=" + uint.MaxValue));
        }

        [Test]
        public void ParseTicketZoneId_PrefersTargetZoneThenZone()
        {
            var travel = new GateTokenPayload { ZoneId = 1, TargetZoneId = 2 }.ToByteString();
            var plain = new GateTokenPayload { ZoneId = 3 }.ToByteString();
            Assert.That(GameClient.ParseTicketZoneId(travel), Is.EqualTo(2u));
            Assert.That(GameClient.ParseTicketZoneId(plain), Is.EqualTo(3u));
        }

        [Test]
        public void ParseTicketZoneId_UnusableTicketYieldsZeroAndNeverThrows()
        {
            // 解析只为展示：解不出来返回零，由调用方保留旧值，不得让换服因此失败。
            Assert.That(GameClient.ParseTicketZoneId(null), Is.Zero);
            Assert.That(GameClient.ParseTicketZoneId(ByteString.Empty), Is.Zero);
            Assert.That(GameClient.ParseTicketZoneId(new GateTokenPayload().ToByteString()), Is.Zero);
            // 单字节 0xFF 是一个没写完的变长整数（续位为 1 却没有后续字节），解析器必抛格式异常。
            Assert.That(GameClient.ParseTicketZoneId(ByteString.CopyFrom(0xFF)), Is.Zero);
        }
    }
}
