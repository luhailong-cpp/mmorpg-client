using System;
using System.Collections;
using System.Text;
using MmorpgClient.Core;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_STANDALONE || UNITY_EDITOR
using System.Net;
#endif

namespace MmorpgClient.Net
{
    /// <summary>
    /// 官方通行证(Casdoor OIDC)客户端授权桥(服务端 docs/design/official-passport-registration.md)。
    ///
    /// 平台边界(严格遵守客户端 AGENTS.md 双形态约束):
    ///   - 微信/抖音小游戏(UNITY_WEBGL &amp;&amp; !UNITY_EDITOR):禁用,不编译任何 HttpListener / Socket / 线程代码;
    ///   - 桌面/编辑器(UNITY_STANDALONE || UNITY_EDITOR):仅在 127.0.0.1:&lt;port&gt; 启单次 HttpListener,
    ///     收到回调后于主线程 Update 排空并经 UnityWebRequest.Post(PKCE code_verifier,无 client_secret)换取 id_token;
    ///   - iOS / Android 真机包:走 Application.deepLinkActivated 接自定义 scheme 回调。
    ///
    /// id_token 仅在内存里交给这一次登录或注销,不落盘、不写 PlayerPrefs、不打日志。
    /// </summary>
    public sealed class PassportSignIn : MonoBehaviour
    {
        private const string HostName = "__MmorpgPassportSignIn";
        private const string CallbackHtml =
            "<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
            + "<title>官方通行证</title><style>"
            + "body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;"
            + "background:#244C3E;color:#FFF3D3;font-family:-apple-system,BlinkMacSystemFont,'PingFang SC','Microsoft YaHei',sans-serif;}"
            + ".card{padding:32px 40px;border:2px solid #E5CC80;border-radius:14px;background:#FFF3D3;color:#244C3E;text-align:center;box-shadow:0 12px 32px rgba(0,0,0,.3);}"
            + "h1{margin:0 0 12px;font-size:22px;}p{margin:0;font-size:15px;opacity:.88;}"
            + "</style></head><body><div class=\"card\"><h1>通行证授权已完成</h1>"
            + "<p>请切回游戏继续，本页面可直接关闭。</p></div></body></html>";
        private const string StaleCallbackHtml =
            "<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>官方通行证</title></head>"
            + "<body style=\"font-family:sans-serif;text-align:center;padding-top:20vh\">"
            + "<h1>该授权页已过期</h1><p>请切回游戏，按游戏内提示重新打开授权页。</p></body></html>";

        private static PassportSignIn _instance;

        private Action<PlatformSignInResult> _pendingCallback;
        private PassportOAuthConfig _activeConfig;
        private PassportPkceSession _activePkce;
        private string _activeAuthorizeUrl;
        private float _deadlineRealtimeSec;
        private bool _exchanging;
        private Coroutine _exchangeCoroutine;
        private UnityWebRequest _exchangeRequest;

#if UNITY_STANDALONE || UNITY_EDITOR
        private readonly object _callbackLock = new object();
        private HttpListener _listener;
        private string _expectedState;
        private Uri _receivedLoopbackUri;
        private bool _loopbackFailed;
#endif

        /// <summary>当前安装包与运行环境是否开放官方通行证入口。</summary>
        public static bool Available
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                if (MiniGameLogin.AuthType != null) return false;
                var config = CurrentConfig();
                if (!config.IsValid) return false;
#if UNITY_STANDALONE || UNITY_EDITOR
                return PassportOAuthProtocol.TryGetLoopbackListenerPrefix(config.RedirectUri, out _, out _);
#else
                return true;
#endif
#endif
            }
        }

        /// <summary>是否正在等待浏览器回调或换取 id_token。</summary>
        public static bool IsWaiting => _instance != null && _instance._pendingCallback != null;

        public static PassportOAuthConfig CurrentConfig()
            => new PassportOAuthConfig(
                ClientSettings.PassportOrigin,
                ClientSettings.PassportClientId,
                ClientSettings.PassportRedirectUri);

        /// <summary>拉起系统浏览器进入 Casdoor 官方通行证注册/登录页。回调保证在 Unity 主线程执行且至多一次。</summary>
        public static void Request(Action<PlatformSignInResult> onDone)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            onDone?.Invoke(PlatformSignInResult.Failure(PlatformSignIn.ErrorUnsupported));
            return;
#else
            var config = CurrentConfig();
            if (!Available || !config.IsValid)
            {
                onDone?.Invoke(PlatformSignInResult.Failure(PlatformSignIn.ErrorUnsupported));
                return;
            }
            var host = EnsureHost();
            host.BeginRequest(config, onDone);
#endif
        }

        /// <summary>若玩家误关浏览器,在等待授权期间再次打开同一授权页。</summary>
        public static bool ReopenBrowser()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            if (_instance == null || _instance._pendingCallback == null ||
                _instance._exchanging || string.IsNullOrEmpty(_instance._activeAuthorizeUrl))
                return false;
            Application.OpenURL(_instance._activeAuthorizeUrl);
            return true;
#endif
        }

        /// <summary>主动取消等待中的授权,立即释放本地回调监听并触发一次 ErrorCanceled 回调。</summary>
        public static void Cancel()
        {
            if (_instance == null) return;
            _instance.Complete(PlatformSignInResult.Failure(PlatformSignIn.ErrorCanceled));
        }

        /// <summary>面向玩家的官方通行证失败文案。</summary>
        public static string DescribeFailure(string error) => PassportSignInErrors.Describe(error);

#if !UNITY_WEBGL || UNITY_EDITOR
        private static PassportSignIn EnsureHost()
        {
            if (_instance != null) return _instance;
            var go = new GameObject(HostName);
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PassportSignIn>();
            return _instance;
        }

        private void BeginRequest(PassportOAuthConfig config, Action<PlatformSignInResult> onDone)
        {
            if (_pendingCallback != null)
            {
                onDone?.Invoke(PlatformSignInResult.Failure(PlatformSignIn.ErrorFailed));
                return;
            }

            PassportPkceSession pkce = PassportOAuthProtocol.CreatePkceSession();
            string authorizeUrl = PassportOAuthProtocol.BuildAuthorizeUrl(config, pkce);

            _pendingCallback = onDone ?? (_ => { });
            _activeConfig = config;
            _activePkce = pkce;
            _activeAuthorizeUrl = authorizeUrl;
            _deadlineRealtimeSec = Time.realtimeSinceStartup + config.TimeoutSeconds;
            _exchanging = false;

#if UNITY_STANDALONE || UNITY_EDITOR
            if (!TryStartLoopbackListener(config.RedirectUri, pkce.State))
            {
                Complete(PlatformSignInResult.Failure(PassportSignInErrors.PortInUse));
                return;
            }
#elif UNITY_IOS || UNITY_ANDROID
            Application.deepLinkActivated -= OnDeepLinkActivated;
            Application.deepLinkActivated += OnDeepLinkActivated;
#endif
            Application.OpenURL(authorizeUrl);
        }

        private void Update()
        {
            if (_pendingCallback == null) return;

#if UNITY_STANDALONE || UNITY_EDITOR
            Uri loopbackUri = null;
            bool loopbackFailed = false;
            lock (_callbackLock)
            {
                if (_receivedLoopbackUri != null)
                {
                    loopbackUri = _receivedLoopbackUri;
                    _receivedLoopbackUri = null;
                }
                if (_loopbackFailed)
                {
                    loopbackFailed = true;
                    _loopbackFailed = false;
                }
            }
            if (loopbackUri != null)
            {
                HandleCallbackUri(loopbackUri);
                return;
            }
            if (loopbackFailed && !_exchanging)
            {
                Complete(PlatformSignInResult.Failure(PlatformSignIn.ErrorFailed));
                return;
            }
#endif
            if (!_exchanging && Time.realtimeSinceStartup >= _deadlineRealtimeSec)
            {
                Debug.LogWarning("[passport] browser authorization timed out");
                Complete(PlatformSignInResult.Failure(PassportSignInErrors.Timeout));
            }
        }

#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        private void OnDeepLinkActivated(string url)
        {
            if (_pendingCallback == null || _exchanging || string.IsNullOrEmpty(url)) return;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
            if (!PassportOAuthProtocol.MatchesRedirectTarget(uri, _activeConfig.RedirectUri)) return;
            if (!PassportOAuthProtocol.CallbackStateMatches(uri, _activePkce.State))
            {
                Debug.LogWarning("[passport] ignoring deep link callback with stale state");
                return;
            }
            HandleCallbackUri(uri);
        }
#endif

        private void HandleCallbackUri(Uri callbackUri)
        {
            if (_pendingCallback == null || _exchanging) return;
            StopCallbackWaiters();

            if (!PassportOAuthProtocol.TryParseCallbackUri(callbackUri, _activePkce.State, out var code, out var err))
            {
                Debug.LogWarning($"[passport] callback rejected: reason={err}");
                Complete(PlatformSignInResult.Failure(err));
                return;
            }

            _exchanging = true;
            _exchangeCoroutine = StartCoroutine(ExchangeCodeForIdToken(_activeConfig, code, _activePkce.CodeVerifier));
        }

        private IEnumerator ExchangeCodeForIdToken(PassportOAuthConfig config, string code, string codeVerifier)
        {
            string url = PassportOAuthProtocol.BuildTokenExchangeUrl(config);
            var form = PassportOAuthProtocol.BuildTokenExchangeForm(config, code, codeVerifier);
            using var req = UnityWebRequest.Post(url, form);
            req.timeout = PassportOAuthConfig.DefaultTokenExchangeTimeoutSeconds;
            _exchangeRequest = req;
            yield return req.SendWebRequest();
            _exchangeRequest = null;

            if (this == null || _pendingCallback == null) yield break;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[passport] token exchange failed: http={req.responseCode} err={req.error}");
                Complete(PlatformSignInResult.Failure(PlatformSignIn.ErrorFailed));
                yield break;
            }

            string body = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
            if (!PassportOAuthProtocol.TryExtractIdToken(body, out var idToken))
            {
                Debug.LogWarning("[passport] token exchange response did not contain a valid id_token");
                Complete(PlatformSignInResult.Failure(PlatformSignIn.ErrorFailed));
                yield break;
            }

            Complete(PlatformSignInResult.Success(idToken, null));
        }

#if UNITY_STANDALONE || UNITY_EDITOR
        private bool TryStartLoopbackListener(string redirectUri, string expectedState)
        {
            if (!PassportOAuthProtocol.TryGetLoopbackListenerPrefix(redirectUri, out var prefix, out _))
                return false;

            StopLoopbackListener();
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add(prefix);
                listener.Start();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[passport] loopback listener failed to bind {prefix}: {ex.Message}");
                SafeCloseListener(listener);
                return false;
            }

            lock (_callbackLock)
            {
                _listener = listener;
                _expectedState = expectedState;
                _receivedLoopbackUri = null;
                _loopbackFailed = false;
            }
            BeginAcceptNext(listener);
            return true;
        }

        private void BeginAcceptNext(HttpListener listener)
        {
            try
            {
                listener.BeginGetContext(OnHttpContext, listener);
            }
            catch (ObjectDisposedException) { }
            catch (HttpListenerException) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[passport] loopback BeginGetContext error: {ex.Message}");
                lock (_callbackLock) { _loopbackFailed = true; }
            }
        }

        private void OnHttpContext(IAsyncResult ar)
        {
            var listener = (HttpListener)ar.AsyncState;
            HttpListenerContext ctx;
            try
            {
                ctx = listener.EndGetContext(ar);
            }
            catch (ObjectDisposedException) { return; }
            catch (HttpListenerException) { return; }
            catch (InvalidOperationException) { return; }

            Uri requestUrl = ctx.Request?.Url;
            string expectedState;
            lock (_callbackLock)
            {
                expectedState = ReferenceEquals(_listener, listener) ? _expectedState : null;
            }
            // 旧标签页或本机其它页面打来的回调:回过期页并继续监听,不让本次授权失败。
            bool accepted = expectedState != null && PassportOAuthProtocol.CallbackStateMatches(requestUrl, expectedState);
            try
            {
                byte[] htmlBytes = Encoding.UTF8.GetBytes(accepted ? CallbackHtml : StaleCallbackHtml);
                ctx.Response.StatusCode = accepted ? 200 : 400;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = htmlBytes.Length;
                ctx.Response.OutputStream.Write(htmlBytes, 0, htmlBytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception) { }

            if (!accepted)
            {
                if (expectedState != null) BeginAcceptNext(listener);
                return;
            }

            lock (_callbackLock)
            {
                if (ReferenceEquals(_listener, listener) && _receivedLoopbackUri == null)
                    _receivedLoopbackUri = requestUrl;
            }
            SafeCloseListener(listener);
        }

        private void StopLoopbackListener()
        {
            HttpListener listener;
            lock (_callbackLock)
            {
                listener = _listener;
                _listener = null;
                _expectedState = null;
                _receivedLoopbackUri = null;
                _loopbackFailed = false;
            }
            SafeCloseListener(listener);
        }

        private static void SafeCloseListener(HttpListener listener)
        {
            if (listener == null) return;
            try { listener.Stop(); } catch (Exception) { }
            try { listener.Close(); } catch (Exception) { }
        }
#endif

        private void StopCallbackWaiters()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            StopLoopbackListener();
#elif UNITY_IOS || UNITY_ANDROID
            Application.deepLinkActivated -= OnDeepLinkActivated;
#endif
        }

        private void Complete(PlatformSignInResult result)
        {
            var callback = _pendingCallback;
            _pendingCallback = null;
            _activePkce = default;
            _activeConfig = default;
            _activeAuthorizeUrl = null;
            _exchanging = false;
            var inflight = _exchangeRequest;
            _exchangeRequest = null;
            if (_exchangeCoroutine != null)
            {
                StopCoroutine(_exchangeCoroutine);
                _exchangeCoroutine = null;
            }
            inflight?.Dispose(); // 换票途中取消:StopCoroutine 不会走 using,手动中止并释放原生请求
            StopCallbackWaiters();
            callback?.Invoke(result);
        }

        private void OnDestroy()
        {
            Complete(PlatformSignInResult.Failure(PlatformSignIn.ErrorCanceled));
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
#else
        private void Complete(PlatformSignInResult _) { }
#endif
    }
}
