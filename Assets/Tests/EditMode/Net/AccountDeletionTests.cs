using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;
using UnityEngine;
using Step = MmorpgClient.Game.GameClient.AccountDeletionStep;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 账号注销的客户端契约(服务端 docs/design/account-deletion.md §4、§5.2):请求字段名、应答按 tip 再按 state 分支、
    /// 挂起原因的文案、/api/login 的注销字段。tip 只认导表器生成的 login_error 枚举,不写码值。
    /// </summary>
    public sealed class AccountDeletionTests
    {
        [TestCase(GatewayLoginResult.CodeOk, 0u, 1u, Step.Scheduled)]        // 申请受理(本来就在冷静期时原样回 1)
        [TestCase(GatewayLoginResult.CodeOk, 0u, 0u, Step.Cancelled)]        // 撤销受理
        [TestCase(GatewayLoginResult.CodeOk, 0u, 2u, Step.AlreadyExecuting)] // 已在执行
        [TestCase(GatewayLoginResult.CodeAuthRejected, (uint)login_error.KLoginAccountDeletionBlocked, 0u, Step.Blocked)]
        [TestCase(GatewayLoginResult.CodeAuthRejected, (uint)login_error.KLoginAccountDeletionReauthRequired, 0u, Step.Reauthorize)]
        [TestCase(GatewayLoginResult.CodeAuthRejected, (uint)login_error.KLoginAccountDeletionInProgress, 2u, Step.AlreadyExecuting)]
        [TestCase(GatewayLoginResult.CodeOk, (uint)login_error.KLoginAccountDeletionPending, 1u, Step.Scheduled)]
        [TestCase(GatewayLoginResult.CodeAuthRejected, (uint)login_error.KLoginAccountNotFound, 0u, Step.CredentialRejected)]
        [TestCase(GatewayLoginResult.CodeAuthRejected, 0u, 0u, Step.CredentialRejected)] // 撤销时 access token 失效
        [TestCase(GatewayLoginResult.CodeRateLimited, 0u, 0u, Step.RateLimited)]
        [TestCase(GatewayLoginResult.CodeInternal, (uint)login_error.KLoginAuthProviderUnavailable, 0u, Step.Retry)]
        [TestCase(GatewayLoginResult.CodeInternal, 0u, 0u, Step.Retry)]
        [TestCase(GatewayLoginResult.CodeOk, 999999u, 1u, Step.Retry)] // 认不出的码不能当成功
        public void Outcome_BranchesOnTipThenState(int code, uint tip, uint state, Step expected)
        {
            var r = new GatewayAccountDeletionResult { code = code, tip = tip, state = state };

            Assert.That(GameClient.ClassifyAccountDeletion(r), Is.EqualTo(expected));
        }

        [Test]
        public void MissingResponse_IsRetry()
        {
            Assert.That(GameClient.ClassifyAccountDeletion(null), Is.EqualTo(Step.Retry));
        }

        [TestCase("guild_leader", "请先转让帮主或解散帮会")]
        [TestCase("role_listed", "角色正在寄售")]
        [TestCase("team_membership", "请先退出队伍或处理组队邀请/申请")]
        [TestCase("team_invite_index", "请先退出队伍或处理组队邀请/申请")]
        [TestCase("team_application_index", "请先退出队伍或处理组队邀请/申请")]
        [TestCase("team_launch_role", "请先退出队伍或处理组队邀请/申请")]
        [TestCase("match_authority_ticket", "请先结束匹配、观战或切磋")]
        [TestCase("match_role_occupancy", "请先结束匹配、观战或切磋")]
        [TestCase("match_spectator", "请先结束匹配、观战或切磋")]
        [TestCase("match_challenge_invitation", "请先结束匹配、观战或切磋")]
        [TestCase("trade_commerce", "交易行还有未完成的订单或挂单")]
        [TestCase("trade_asset_op", "有物品或奖励还在处理，请稍后再试")]
        [TestCase("mail_asset_op", "有物品或奖励还在处理，请稍后再试")]
        [TestCase("guild_asset_op", "有物品或奖励还在处理，请稍后再试")]
        [TestCase("player_inbox_item", "有物品或奖励还在处理，请稍后再试")]
        [TestCase("guild_trial_reward", "有物品或奖励还在处理，请稍后再试")]
        [TestCase("some_future_reason", "有未完成的游戏内事务")]
        [TestCase("", "有未完成的游戏内事务")]
        [TestCase(null, "有未完成的游戏内事务")]
        public void BlockerReason_MapsToChineseText(string reason, string expected)
        {
            Assert.That(GameClient.DescribeAccountDeletionBlocker(reason), Is.EqualTo(expected));
        }

        [Test]
        public void BlockerList_ListsEachReasonOnceAcrossRoles()
        {
            string text = GameClient.DescribeAccountDeletionBlockers(new[]
            {
                new GatewayAccountDeletionBlocker { player_id = 1, reason = "team_membership" },
                new GatewayAccountDeletionBlocker { player_id = 2, reason = "team_invite_index" },
                new GatewayAccountDeletionBlocker { player_id = 2, reason = "guild_leader" },
            });

            Assert.That(text, Is.EqualTo("· 请先退出队伍或处理组队邀请/申请\n· 请先转让帮主或解散帮会"));
        }

        [Test]
        public void EmptyBlockerList_StillExplains()
        {
            Assert.That(GameClient.DescribeAccountDeletionBlockers(null), Is.EqualTo("· 有未完成的游戏内事务"));
            Assert.That(GameClient.DescribeAccountDeletionBlockers(new GatewayAccountDeletionBlocker[0]),
                Is.EqualTo("· 有未完成的游戏内事务"));
        }

        [Test]
        public void AppleDeletionRequest_CarriesFreshTokenAndAuthorizationCode()
        {
            var body = GameClient.BuildAccountDeletionRequest(7,
                LoginCredential.ForThirdParty(LoginAuthTypes.Apple, "apple-id-token"), "apple-auth-code");
            string json = JsonUtility.ToJson(body);

            Assert.That(json, Does.Contain("\"zone_id\":7"));
            Assert.That(json, Does.Contain("\"auth_type\":\"apple\""));
            Assert.That(json, Does.Contain("\"auth_token\":\"apple-id-token\""));
            Assert.That(json, Does.Contain("\"authorization_code\":\"apple-auth-code\""));
            Assert.That(body.account, Is.Empty, "账号由服务端从凭证算出");
            Assert.That(body.password, Is.Empty);
            Assert.That(json, Does.Not.Contain("access_token"), "注销要求重新认证,不收 access token");
        }

        [Test]
        public void PasswordDeletionRequest_SendsAccountAndPasswordOnly()
        {
            var body = GameClient.BuildAccountDeletionRequest(3, LoginCredential.ForPassword("robot_1", "pw"), null);

            Assert.That(body.zone_id, Is.EqualTo(3u));
            Assert.That(body.auth_type, Is.EqualTo(LoginAuthTypes.Password));
            Assert.That(body.account, Is.EqualTo("robot_1"));
            Assert.That(body.password, Is.EqualTo("pw"));
            Assert.That(body.auth_token, Is.Empty);
            Assert.That(body.authorization_code, Is.Empty);
        }

        [Test]
        public void CancelRequest_CarriesZoneAndAccessTokenOnly()
        {
            string json = JsonUtility.ToJson(new GatewayCancelAccountDeletionRequest { zone_id = 3, access_token = "cooling-off-token" });

            Assert.That(json, Is.EqualTo("{\"zone_id\":3,\"access_token\":\"cooling-off-token\"}"));
        }

        [Test]
        public void DeletionResponse_ParsesStateTimeAndBlockers()
        {
            var r = JsonUtility.FromJson<GatewayAccountDeletionResult>(
                "{\"code\":0,\"message\":\"\",\"tip\":0,\"state\":1,\"execute_after\":1795000000," +
                "\"blockers\":[{\"player_id\":4503599627370497,\"reason\":\"guild_leader\"},{\"player_id\":7,\"reason\":\"role_listed\"}]}");

            Assert.That(r.state, Is.EqualTo(1u));
            Assert.That(r.execute_after, Is.EqualTo(1795000000L));
            Assert.That(r.blockers, Has.Length.EqualTo(2));
            Assert.That(r.blockers[0].player_id, Is.EqualTo(4503599627370497UL));
            Assert.That(r.blockers[0].reason, Is.EqualTo("guild_leader"));
            Assert.That(r.blockers[1].reason, Is.EqualTo("role_listed"));
        }

        [Test]
        public void LoginResponse_ParsesDeletionFields()
        {
            var r = JsonUtility.FromJson<GatewayLoginResult>(
                "{\"code\":0,\"access_token\":\"a\",\"account_deletion_state\":1," +
                "\"account_deletion_execute_after\":1795000000,\"previous_account_deleted_at\":1790000000}");

            Assert.That(r.account_deletion_state, Is.EqualTo(1u));
            Assert.That(r.account_deletion_execute_after, Is.EqualTo(1795000000L));
            Assert.That(r.previous_account_deleted_at, Is.EqualTo(1790000000L));
        }

        [Test]
        public void LoginResponseWithoutDeletionFields_IsActiveAccount()
        {
            var r = JsonUtility.FromJson<GatewayLoginResult>("{\"code\":0,\"access_token\":\"a\"}");

            Assert.That(r.account_deletion_state, Is.Zero);
            Assert.That(r.account_deletion_execute_after, Is.Zero);
            Assert.That(r.previous_account_deleted_at, Is.Zero);
        }

        [Test]
        public void DeletionTime_IsLocalDateAndMinute()
        {
            // 1795000000 = 2026-11-18 11:06 UTC,任何时区下都还在 11 月 17~19 日
            Assert.That(GameClient.FormatAccountDeletionTime(1795000000), Does.Match(@"^2026-11-1[789] \d{2}:\d{2}$"));
        }

        [TestCase(0L)]
        [TestCase(-5L)]
        [TestCase(long.MaxValue)]
        public void MissingOrOutOfRangeTime_DoesNotThrow(long unixSeconds)
        {
            Assert.That(GameClient.FormatAccountDeletionTime(unixSeconds), Is.EqualTo("近期"));
        }
    }
}
