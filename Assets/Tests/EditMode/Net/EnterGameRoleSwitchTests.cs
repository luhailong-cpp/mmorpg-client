using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 同一条连接上进场与换角的判定(服务端 docs/design/client-request-ordering.md §6.2「login 会话栅栏」):
    /// 换角先 LeaveGame;已绑定角色时换角收到 2005 按「先 LeaveGame」处理、不当成登录会话不存在;
    /// LeaveGame 清掉登录态后的 2028 才重新 Login;同一角色结果不明时照常重发一次。
    /// </summary>
    public sealed class EnterGameRoleSwitchTests
    {
        private const ulong RoleA = 7001;
        private const ulong RoleB = 7002;
        private static readonly string TimedOut = RpcStatus.Timeout(OutstandingCalls.TimeoutDetail).ToLegacyError();
        private static readonly string Unknown1003 = RpcStatus.ServerTip((uint)common_error.KServiceUnavailable).ToLegacyError();
        private static readonly uint InProgress = (uint)login_error.KLoginInProgress;
        private static readonly uint SessionNotFound = (uint)login_error.KLoginSessionNotFound;

        private static GameClient.EnterGameStep Classify(string error, uint tip, ulong boundBefore, ulong target,
            bool resent = false, bool left = false, bool relogged = false)
            => GameClient.ClassifyEnterGameReply(error, tip, boundBefore, target, resent, left, relogged);

        [Test]
        public void LeaveGameIsNeededOnlyWhenAnotherRoleMayBeBound()
        {
            Assert.That(GameClient.NeedsLeaveGameBeforeEnter(0, RoleA), Is.False, "新连接上没有绑着角色");
            Assert.That(GameClient.NeedsLeaveGameBeforeEnter(RoleA, RoleA), Is.False, "同一角色重进不先离开");
            Assert.That(GameClient.NeedsLeaveGameBeforeEnter(RoleA, RoleB), Is.True, "进场失败之后换角:先 LeaveGame");
        }

        [Test]
        public void AcceptedAndPlainRejectionsKeepTheirMeaning()
        {
            Assert.That(Classify(null, 0, 0, RoleA), Is.EqualTo(GameClient.EnterGameStep.Accepted));
            Assert.That(Classify(null, (uint)login_error.KLoginEnterGameGuid, 0, RoleA), Is.EqualTo(GameClient.EnterGameStep.Rejected));
            Assert.That(Classify(RpcStatus.ServerTip((uint)common_error.KRateLimitExceeded).ToLegacyError(), 0, 0, RoleA),
                Is.EqualTo(GameClient.EnterGameStep.Failed), "信封拒绝照旧收掉管线");
            Assert.That(Classify(RpcStatus.Disconnected("disconnected").ToLegacyError(), 0, RoleA, RoleA),
                Is.EqualTo(GameClient.EnterGameStep.Failed), "断线不重发");
        }

        [Test]
        public void SameRoleResendsOnceAfterTimeoutOr1003WithoutLeavingFirst()
        {
            Assert.That(Classify(TimedOut, 0, RoleA, RoleA), Is.EqualTo(GameClient.EnterGameStep.Resend));
            Assert.That(Classify(Unknown1003, 0, RoleA, RoleA), Is.EqualTo(GameClient.EnterGameStep.Resend));
            Assert.That(Classify(TimedOut, 0, RoleA, RoleA, resent: true), Is.EqualTo(GameClient.EnterGameStep.Failed), "只重发一次");
            // 重发撞上前一次的进场链(玩家锁被占):前一次多半已受理,接着等进场结果,不报失败、也不 LeaveGame。
            Assert.That(Classify(null, InProgress, RoleA, RoleA, resent: true), Is.EqualTo(GameClient.EnterGameStep.InProgress));
            Assert.That(Classify(null, InProgress, 0, RoleA), Is.EqualTo(GameClient.EnterGameStep.Rejected),
                "第一次就是 2005(别处的进场还占着锁):照旧按拒绝收尾,不当成登录会话不存在去重新登录");
        }

        [Test]
        public void RoleSwitchRejectedWithLoginInProgressLeavesFirstInsteadOfLoggingInAgain()
        {
            // login 栅栏回响应体,gate 换角闸回信封:同一个码、同一个处理。
            Assert.That(Classify(null, InProgress, RoleA, RoleB), Is.EqualTo(GameClient.EnterGameStep.LeaveThenEnter));
            Assert.That(Classify(RpcStatus.ServerTip(InProgress).ToLegacyError(), 0, RoleA, RoleB),
                Is.EqualTo(GameClient.EnterGameStep.LeaveThenEnter));
            Assert.That(Classify(null, InProgress, RoleA, RoleB, left: true), Is.EqualTo(GameClient.EnterGameStep.Rejected),
                "已经离开过还被拒:不再循环");
        }

        [Test]
        public void SessionNotFoundAfterOurOwnLeaveGameLogsInAgainOnce()
        {
            Assert.That(Classify(null, SessionNotFound, 0, RoleB, left: true), Is.EqualTo(GameClient.EnterGameStep.LoginThenEnter),
                "C++ 版 LeaveGame 清掉本连接的登录态:重新 Login 一次再进");
            Assert.That(Classify(null, SessionNotFound, 0, RoleB, left: true, relogged: true),
                Is.EqualTo(GameClient.EnterGameStep.Rejected), "重新 Login 过还是没有:不再循环");
            Assert.That(Classify(null, SessionNotFound, 0, RoleA), Is.EqualTo(GameClient.EnterGameStep.Rejected),
                "没离开过的 2028 不是我们清的,照旧按拒绝收尾");
        }
    }
}
