using System;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 服务端认得的登录方式(POST /api/login 的 auth_type)。取值以服务端 go/login 注册的认证器名为准
    /// (docs/design/auth-provider-framework.md「Built-in Providers」),上线后不改名。
    /// </summary>
    public static class LoginAuthTypes
    {
        public const string Password = "password";
        public const string WeChat = "wechat";                  // 微信 App(移动应用 SDK 的 OAuth code)
        public const string QQ = "qq";                          // QQ 互联(SDK 拿到的 access_token)
        public const string WeChatMiniGame = "wechat_minigame"; // 微信小游戏 wx.login() 的 code
        public const string DouyinMiniGame = "douyin_minigame"; // 抖音小游戏 tt.login() 的 code
        public const string Apple = "apple";                    // Sign in with Apple 的 identity token
        public const string Google = "google";                  // Google 登录的 ID token

        /// <summary>只用于 gate 上的会话绑定,不是首次登录方式。</summary>
        public const string AccessToken = "access_token";
    }

    /// <summary>
    /// 一次 POST /api/login 用的凭据,二选一:
    /// <list type="bullet">
    ///   <item>口令登录:账号 + 口令;</item>
    ///   <item>第三方登录:登录方式 + 渠道凭证。账号由服务端从凭证算出(如 wx_&lt;unionid&gt;),客户端不知道也不需要知道。</item>
    /// </list>
    /// 渠道凭证多为一次性或短时有效:登录失败后要向渠道重新取,不能原样重发
    /// (服务端 docs/design/third-party-login-end-to-end-design.md §三)。
    /// 登录成功后的一切(分配 gate、gate 上绑定会话、重连、跨区)只用服务端签发的 access / refresh token,与登录方式无关。
    /// </summary>
    public readonly struct LoginCredential
    {
        public string AuthType { get; }
        public string Account { get; }
        public string Password { get; }
        public string AuthToken { get; }

        private LoginCredential(string authType, string account, string password, string authToken)
        {
            AuthType = authType;
            Account = account;
            Password = password;
            AuthToken = authToken;
        }

        public bool IsPassword => AuthType == LoginAuthTypes.Password;

        public static LoginCredential ForPassword(string account, string password)
            => new LoginCredential(LoginAuthTypes.Password, account ?? string.Empty, password ?? string.Empty, string.Empty);

        public static LoginCredential ForThirdParty(string authType, string authToken)
        {
            if (string.IsNullOrEmpty(authType) || authType == LoginAuthTypes.Password || authType == LoginAuthTypes.AccessToken)
                throw new ArgumentException("third-party login needs a provider auth_type", nameof(authType));
            if (string.IsNullOrEmpty(authToken))
                throw new ArgumentException("third-party login needs the credential issued by the channel", nameof(authToken));
            return new LoginCredential(authType, string.Empty, string.Empty, authToken);
        }
    }
}
