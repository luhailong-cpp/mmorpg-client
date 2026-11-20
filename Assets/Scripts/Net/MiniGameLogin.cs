using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace MmorpgClient.Net
{
    /// <summary>
    /// 微信 / 抖音小游戏的登录:向宿主要一次性的登录 code(wx.login / tt.login),交给 /api/login 换账号
    /// (服务端 docs/design/third-party-login-end-to-end-design.md §十一;JS 侧在 Assets/Plugins/WebGL/MmorpgMiniGame.jslib)。
    /// code 一次性、几分钟内有效:登录失败后重新要,不重发。结果沿用渠道登录的 <see cref="PlatformSignInResult"/>,code 放在 IdentityToken。
    /// </summary>
    public static class MiniGameLogin
    {
        /// <summary>
        /// 这个安装包所在宿主对应的登录方式(<see cref="LoginAuthTypes.WeChatMiniGame"/> / <see cref="LoginAuthTypes.DouyinMiniGame"/>);
        /// 不在小游戏里(编辑器、原生包、普通浏览器)为 null。宿主在进程存续期间不变。
        /// </summary>
        public static string AuthType { get; } = DetectAuthType();

        /// <summary>要一个登录 code。回调在主线程上、每次请求恰好一次;不在小游戏里立即回 unsupported。</summary>
        public static void Request(Action<PlatformSignInResult> onDone)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // 上一次还没回:不并发拉起,也不让迟到的结果落到这一次的回调上。
            if (AuthType == null) { onDone?.Invoke(PlatformSignInResult.Failure(PlatformSignIn.ErrorUnsupported)); return; }
            if (_pending != null) { onDone?.Invoke(PlatformSignInResult.Failure(PlatformSignIn.ErrorFailed)); return; }
            _pending = onDone ?? (_ => { });
            MmorpgMiniGameLogin(OnCode);
#else
            onDone?.Invoke(PlatformSignInResult.Failure(PlatformSignIn.ErrorUnsupported));
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int MmorpgMiniGameHost();
        // 回调用专门声明的委托类型,不用 Action<>(理由同 WebSocketDrivers.cs)。
        private delegate void CodeCallback(string code);
        [DllImport("__Internal")] private static extern void MmorpgMiniGameLogin(CodeCallback callback);

        private static Action<PlatformSignInResult> _pending;

        private static string DetectAuthType() => MmorpgMiniGameHost() switch
        {
            1 => LoginAuthTypes.WeChatMiniGame,
            2 => LoginAuthTypes.DouyinMiniGame,
            _ => null,
        };

        [MonoPInvokeCallback(typeof(CodeCallback))]
        private static void OnCode(string code)
        {
            var done = _pending;
            _pending = null;
            done?.Invoke(string.IsNullOrEmpty(code)
                ? PlatformSignInResult.Failure(PlatformSignIn.ErrorFailed)
                : PlatformSignInResult.Success(code, null));
        }
#else
        private static string DetectAuthType() => null;
#endif
    }
}
