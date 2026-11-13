using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// HTTP 登录失败文案(GameClient.DescribeLoginCode)。契约见服务端
    /// docs/design/third-party-login-end-to-end-design.md §三 与 tip-code-axis.md §2.10:
    /// 网关 HTTP 状态恒为 200,客户端只按 JSON code 分支;500 也可能带 upstream_err=&lt;tip&gt;。
    /// </summary>
    public sealed class GatewayLoginErrorTextTests
    {
        private static GatewayLoginResult Result(int code, string message)
            => new GatewayLoginResult { code = code, message = message };

        [Test]
        public void UpstreamFaultTip_On500_SaysRetryLater_NotWrongPassword()
        {
            // 2035 只是示例数字:文案不按码值分支,只认 code=500。
            string text = GameClient.DescribeLoginCode(Result(GatewayLoginResult.CodeInternal, "upstream_err=2035"));
            Assert.That(text, Does.Contain("稍后重试"));
            Assert.That(text, Does.Contain("upstream_err=2035"));
            Assert.That(text, Does.Not.Contain("密码"));
        }

        [Test]
        public void LoginUnavailable_On500_SaysRetryLater()
        {
            string text = GameClient.DescribeLoginCode(Result(GatewayLoginResult.CodeInternal, "login_unavailable"));
            Assert.That(text, Does.Contain("稍后重试"));
            Assert.That(text, Does.Contain("login_unavailable"));
        }

        [Test]
        public void EmptyMessage_On500_FallsBackToCode()
        {
            string text = GameClient.DescribeLoginCode(Result(GatewayLoginResult.CodeInternal, null));
            Assert.That(text, Does.Contain("500"));
        }

        [Test]
        public void UpstreamRejection_On401_StaysAuthRejected()
        {
            string text = GameClient.DescribeLoginCode(Result(GatewayLoginResult.CodeAuthRejected, "upstream_err=2000"));
            Assert.That(text, Is.EqualTo("账号或密码错误"));
        }

        [Test]
        public void UnknownCode_KeepsGenericFailure()
        {
            string text = GameClient.DescribeLoginCode(Result(418, "teapot"));
            Assert.That(text, Is.EqualTo("登录失败(teapot)"));
        }
    }

    /// <summary>
    /// 排队之前的预判(GameClient.MustCreateRoleInClosedZone):所选区暂停创角、而账号在这个区没有角色时不去排队。
    /// 契约见服务端 docs/design/zone-full-handling.md §6:/api/login 的 players[].zone_id 是角色现在所在的区,
    /// 0 = 不知道(老网关没发,或加字段之前的存量角色)。只在确定没有角色时才拦,拦错的代价是老玩家进不了区。
    /// </summary>
    public sealed class ZoneCreateRoleClosedPrecheckTests
    {
        private const uint Zone = 101;

        private static GatewayPlayerInfo Role(ulong playerId, uint zoneId)
            => new GatewayPlayerInfo { player_id = playerId, zone_id = zoneId };

        [Test]
        public void OpenZone_NeverBlocks_EvenWithoutAnyRole()
        {
            Assert.That(GameClient.MustCreateRoleInClosedZone(false, null, Zone), Is.False);
            Assert.That(GameClient.MustCreateRoleInClosedZone(false, new GatewayPlayerInfo[0], Zone), Is.False);
        }

        [Test]
        public void ClosedZone_BlocksAccountWithNoRoleAtAll()
        {
            Assert.That(GameClient.MustCreateRoleInClosedZone(true, null, Zone), Is.True);
            Assert.That(GameClient.MustCreateRoleInClosedZone(true, new GatewayPlayerInfo[0], Zone), Is.True);
        }

        [Test]
        public void ClosedZone_BlocksAccountWhoseRolesAreAllInOtherZones()
        {
            var players = new[] { Role(1, 102), Role(2, 103) };
            Assert.That(GameClient.MustCreateRoleInClosedZone(true, players, Zone), Is.True);
        }

        [Test]
        public void ClosedZone_LetsInAccountWithARoleInThatZone()
        {
            var players = new[] { Role(1, 102), Role(2, Zone) };
            Assert.That(GameClient.MustCreateRoleInClosedZone(true, players, Zone), Is.False);
        }

        [Test]
        public void ClosedZone_UnknownRoleZone_IsNotBlocked()
        {
            // 老网关不发 zone_id(全是 0),或加字段之前建的存量角色:进区后的过滤把它们算作本区可见,这里同样不拦。
            var players = new[] { Role(1, 0) };
            Assert.That(GameClient.MustCreateRoleInClosedZone(true, players, Zone), Is.False);
        }

        [Test]
        public void BlockedMessage_IsTheSameTextAsTheServerRejection()
        {
            Assert.That(GameClient.ZoneCreateRoleClosedMessage, Is.EqualTo("该区服已暂停创建新角色，请选择其他区服"));
        }
    }
}
