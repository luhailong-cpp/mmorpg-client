using System;
using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 实名与防沉迷的客户端半边(服务端 docs/design/real-name-anti-addiction.md §5.2~§5.7):网关应答的解析、
    /// 实名结论与踢线原因的文案、实名之后凭 access token 继续进区的凭据。
    /// </summary>
    public class AntiAddictionTests
    {
        [Test]
        public void LoginResultWithoutAntiAddictionIsNotRestricted()
        {
            var r = JsonUtility.FromJson<GatewayLoginResult>("{\"code\":0}");
            Assert.IsFalse(r.anti_addiction != null && (r.anti_addiction.real_name_required || r.anti_addiction.minor),
                "部署没开防沉迷时网关不发这一段,客户端不能当成要实名");
        }

        [Test]
        public void LoginResultParsesAntiAddiction()
        {
            var r = JsonUtility.FromJson<GatewayLoginResult>(
                "{\"code\":0,\"anti_addiction\":{\"real_name_required\":false,\"minor\":true,\"play_deadline_ms\":1763989200000}}");
            Assert.IsFalse(r.anti_addiction.real_name_required);
            Assert.IsTrue(r.anti_addiction.minor);
            Assert.AreEqual(1763989200000L, r.anti_addiction.play_deadline_ms);
        }

        [Test]
        public void RealNameResultTexts()
        {
            Assert.IsNull(GameClient.DescribeRealNameResult(new GatewayRealNameResult { code = GatewayRealNameResult.CodeOk }));
            Assert.AreEqual("姓名或身份证号有误，请核对后重试", GameClient.DescribeRealNameResult(new GatewayRealNameResult
            {
                code = GatewayRealNameResult.CodeRejected, tip = (uint)login_error.KLoginRealNameInvalid,
            }));
            Assert.AreEqual("实名认证中，请稍后重新登录", GameClient.DescribeRealNameResult(new GatewayRealNameResult
            {
                code = GatewayRealNameResult.CodeRejected, tip = (uint)login_error.KLoginRealNamePending,
            }));
            Assert.AreEqual(GameClient.ChannelCredentialRejectedMessage,
                GameClient.DescribeRealNameResult(new GatewayRealNameResult { code = GatewayRealNameResult.CodeAuthRejected }));
            Assert.IsNotNull(GameClient.DescribeRealNameResult(new GatewayRealNameResult { code = 500 }));
        }

        [Test]
        public void GateLoginAndKickReasonsHaveAntiAddictionTexts()
        {
            Assert.AreEqual(GameClient.RealNameRequiredMessage, GameClient.DescribeAntiAddictionTip((uint)login_error.KLoginRealNameRequired));
            Assert.AreEqual(GameClient.MinorNotInPlayTimeMessage, GameClient.DescribeAntiAddictionTip((uint)login_error.KLoginMinorNotInPlayTime));
            Assert.IsNull(GameClient.DescribeAntiAddictionTip((uint)login_error.KLoginAccountNotFound));
            Assert.AreEqual(GameClient.MinorPlayTimeOverMessage, GameClient.DescribeKickReason((uint)login_error.KLoginMinorPlayTimeOver));
        }

        [Test]
        public void AccountDeletionKickReasonsHaveTexts()
        {
            // 服务端申请注销成功后带冷静期码踢线、到期执行时带执行中码踢线:都要有文案,客户端才不会当成普通断线去自动重连。
            Assert.AreEqual(GameClient.AccountDeletionPendingMessage, GameClient.DescribeKickReason((uint)login_error.KLoginAccountDeletionPending));
            Assert.AreEqual(GameClient.AccountDeletionExecutingMessage, GameClient.DescribeKickReason((uint)login_error.KLoginAccountDeletionInProgress));
            Assert.IsNull(GameClient.DescribeKickReason((uint)login_error.KLoginAccountDeletionBlocked));
        }

        [Test]
        public void PassportCredentialChangedHasTexts()
        {
            // 官方通行证改密 / 停用之后(服务端 official-passport-registration.md §3.6):踢线要有文案,客户端才不会去自动重连;
            // 用改密之前的授权登录被拒(401,message = upstream_err=<tip>)要说清原因,其余渠道 401 仍是「登录授权已失效」。
            Assert.AreEqual(GameClient.PassportCredentialChangedMessage,
                GameClient.DescribeKickReason((uint)login_error.KLoginPassportCredentialChanged));
            var passportRejected = new GatewayLoginResult
            {
                code = GatewayLoginResult.CodeAuthRejected,
                message = "upstream_err=" + (uint)login_error.KLoginPassportCredentialChanged,
            };
            Assert.AreEqual(GameClient.PassportCredentialChangedMessage, GameClient.DescribeChannelCredentialRejected(passportRejected));
            var otherRejected = new GatewayLoginResult
            {
                code = GatewayLoginResult.CodeAuthRejected,
                message = "upstream_err=" + (uint)login_error.KLoginAccountNotFound,
            };
            Assert.AreEqual(GameClient.ChannelCredentialRejectedMessage, GameClient.DescribeChannelCredentialRejected(otherRejected));
        }

        [Test]
        public void AccessTokenCredentialCarriesTheToken()
        {
            var credential = LoginCredential.ForAccessToken("tok");
            Assert.AreEqual(LoginAuthTypes.AccessToken, credential.AuthType);
            Assert.AreEqual("tok", credential.AuthToken);
            Assert.IsFalse(credential.IsPassword);
            Assert.Throws<ArgumentException>(() => LoginCredential.ForAccessToken(string.Empty));
        }
    }
}
