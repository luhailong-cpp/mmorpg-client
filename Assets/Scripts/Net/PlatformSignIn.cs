using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Threading;
using MmorpgClient.Core;
#endif

namespace MmorpgClient.Net
{
    /// <summary>
    /// 渠道 SDK 交回的一次结果。令牌与授权码只在内存里用这一次:服务端一次性消费身份令牌,
    /// 失败后要重新向渠道取,不能重发(服务端 docs/design/account-deletion.md §3.4);不落盘、不进 SessionModel、不进日志。
    /// </summary>
    public readonly struct PlatformSignInResult
    {
        public bool Ok { get; }
        public string IdentityToken { get; }
        /// <summary>只有 Apple 有:注销时随申请送上,login 换票后立刻撤销(§8);登录不用它。</summary>
        public string AuthorizationCode { get; }
        /// <summary>失败原因,取 <see cref="PlatformSignIn"/> 的 Error* 常量之一;成功为空串。</summary>
        public string Error { get; }

        private PlatformSignInResult(bool ok, string identityToken, string authorizationCode, string error)
        {
            Ok = ok;
            IdentityToken = identityToken;
            AuthorizationCode = authorizationCode;
            Error = error;
        }

        public static PlatformSignInResult Success(string identityToken, string authorizationCode)
            => new PlatformSignInResult(true, identityToken, authorizationCode ?? string.Empty, string.Empty);

        public static PlatformSignInResult Failure(string error)
            => new PlatformSignInResult(false, string.Empty, string.Empty, error);
    }

    /// <summary>
    /// Apple / Google 登录的 C# 桥(服务端 docs/design/account-deletion.md §3.3)。
    /// iOS 走 Assets/Plugins/iOS/MmorpgAppleSignIn.mm(DllImport "__Internal"),结果经 UnitySendMessage 回到本类的常驻实例;
    /// 安卓走 Assets/Plugins/Android/MmorpgGoogleSignIn.androidlib(AndroidJavaProxy 回调,在 Java 线程上到达,切回主线程再交出);
    /// 其它平台(含编辑器)立即回 <see cref="ErrorUnsupported"/>。回调一律在 Unity 主线程上调用,每次请求恰好一次。
    /// 它是 MonoBehaviour 只为给 UnitySendMessage 当有名字的接收对象,调用方只用静态成员。
    /// </summary>
    public sealed class PlatformSignIn : MonoBehaviour
    {
        public const string ErrorCanceled = "canceled";
        public const string ErrorNoAccount = "no_account";
        public const string ErrorFailed = "failed";
        public const string ErrorUnsupported = "unsupported";

        /// <summary>这个安装包能发起 Apple 登录(只有 iOS 真机包)。</summary>
        public static bool AppleAvailable =>
#if UNITY_IOS && !UNITY_EDITOR
            true;
#else
            false;
#endif

        /// <summary>这个安装包能发起 Google 登录:安卓真机包,且构建时填了 <c>ClientSettings.GoogleServerClientId</c>。</summary>
        public static bool GoogleAvailable =>
#if UNITY_ANDROID && !UNITY_EDITOR
            !string.IsNullOrEmpty(ClientSettings.GoogleServerClientId);
#else
            false;
#endif

        /// <summary>拉起 Sign in with Apple(不要邮箱和姓名)。成功带 identity token 与 authorization code。</summary>
        public static void RequestApple(Action<PlatformSignInResult> onDone)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (_receiver == null)
            {
                var host = new GameObject(ReceiverName);
                DontDestroyOnLoad(host);
                _receiver = host.AddComponent<PlatformSignIn>();
            }
            // 上一次授权还没回:不并发拉起,也不让迟到的结果落到这一次的回调上。
            if (_receiver._pendingApple != null) { onDone?.Invoke(PlatformSignInResult.Failure(ErrorFailed)); return; }
            _receiver._pendingApple = onDone ?? (_ => { });
            MmorpgAppleSignIn_Request(ReceiverName, nameof(OnAppleSignInResult));
#else
            onDone?.Invoke(PlatformSignInResult.Failure(ErrorUnsupported));
#endif
        }

        /// <summary>拉起 Sign in with Google(Credential Manager)。成功带 ID token。</summary>
        public static void RequestGoogle(Action<PlatformSignInResult> onDone)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var main = SynchronizationContext.Current; // Unity 主线程的上下文;Java 回调在别的线程上到
            if (!GoogleAvailable || main == null) { onDone?.Invoke(PlatformSignInResult.Failure(ErrorUnsupported)); return; }
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var bridge = new AndroidJavaClass(GoogleBridgeClass);
                bridge.CallStatic("request", activity, ClientSettings.GoogleServerClientId,
                    new GoogleCallback(json => main.Post(_ => onDone?.Invoke(Parse(json)), null)));
            }
            catch (AndroidJavaException) { onDone?.Invoke(PlatformSignInResult.Failure(ErrorUnsupported)); } // 安装包没带 MmorpgGoogleSignIn 模块
#else
            onDone?.Invoke(PlatformSignInResult.Failure(ErrorUnsupported));
#endif
        }

        /// <summary>注销申请成功后清掉 Google 凭据状态,免得下次自动选中刚注销的账号(§3.2)。尽力而为,只记日志。</summary>
        public static void ClearGoogleCredentialState()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var bridge = new AndroidJavaClass(GoogleBridgeClass);
                bridge.CallStatic("clearCredentialState", activity,
                    new GoogleCallback(json => Debug.Log($"[PlatformSignIn] Google clear credential state result: {json}")));
            }
            catch (AndroidJavaException ex) { Debug.LogWarning($"[PlatformSignIn] Google clear credential state failed: {ex.Message}"); }
#endif
        }

        /// <summary>
        /// 原生侧回的 JSON → 结果。格式不对、ok 却没有令牌都按 <see cref="ErrorFailed"/>(拿不准就当没登录上);
        /// 认不出的错误串也归成 failed。
        /// </summary>
        public static PlatformSignInResult Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return PlatformSignInResult.Failure(ErrorFailed);
            NativePayload payload;
            try { payload = JsonUtility.FromJson<NativePayload>(json); }
            catch (Exception) { return PlatformSignInResult.Failure(ErrorFailed); }
            if (payload == null) return PlatformSignInResult.Failure(ErrorFailed);
            if (payload.ok)
                return string.IsNullOrEmpty(payload.identity_token)
                    ? PlatformSignInResult.Failure(ErrorFailed)
                    : PlatformSignInResult.Success(payload.identity_token, payload.authorization_code);
            return PlatformSignInResult.Failure(payload.error is ErrorCanceled or ErrorNoAccount or ErrorUnsupported
                ? payload.error
                : ErrorFailed);
        }

        /// <summary>失败原因 → 给玩家看的文案。</summary>
        public static string DescribeFailure(string error) => error switch
        {
            ErrorCanceled => "已取消授权",
            ErrorNoAccount => "这台设备上没有可用的 Google 账号，请先在系统设置里登录 Google 账号",
            ErrorUnsupported => "当前设备不支持这种登录方式",
            _ => "授权失败，请稍后重试",
        };

        [Serializable]
        private sealed class NativePayload
        {
            public bool ok;
            public string identity_token;
            public string authorization_code;
            public string error;
        }

#if UNITY_IOS && !UNITY_EDITOR
        private const string ReceiverName = "[PlatformSignIn]";
        private static PlatformSignIn _receiver;
        private Action<PlatformSignInResult> _pendingApple;

        [DllImport("__Internal")]
        private static extern void MmorpgAppleSignIn_Request(string gameObject, string method);

        /// <summary>原生侧 UnitySendMessage 调这里,Unity 在主线程上投递。</summary>
        public void OnAppleSignInResult(string json)
        {
            var done = _pendingApple;
            _pendingApple = null;
            done?.Invoke(Parse(json));
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string GoogleBridgeClass = "com.mmorpg.signin.GoogleSignInBridge";

        /// <summary>Java 侧 GoogleSignInBridge.Callback 的实现;Java 在自己的线程上调,这里只转交。</summary>
        private sealed class GoogleCallback : AndroidJavaProxy
        {
            private readonly Action<string> _onJson;

            public GoogleCallback(Action<string> onJson) : base(GoogleBridgeClass + "$Callback") => _onJson = onJson;

            // 名字与 Java 接口方法一致,AndroidJavaProxy 按名字分派。
            public void onResult(string json) => _onJson(json);
        }
#endif
    }
}
