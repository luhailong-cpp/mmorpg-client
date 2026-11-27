using System;
using System.IO;
using System.Xml;
using MmorpgClient.Core;
using UnityEngine;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif

namespace MmorpgClient.Editor
{
    /// <summary>
    /// 移动端官方通行证(Casdoor OIDC)回调深度链接自动登记(服务端 docs/design/official-passport-registration.md §3.8):
    ///   - Android: 导出 Gradle 工程时向主 LAUNCHER Activity 的 AndroidManifest.xml 幂等追加 VIEW/DEFAULT/BROWSABLE intent-filter,
    ///     使 Application.deepLinkActivated 生效。登记的是自定义协议头 xuanming://passport/callback(开发包、正式包一样)。
    ///     没用 App Links:国内多数手机核对不了 assetlinks.json、浏览器没有点击的跳转也不交给 App;别的 App 抢注同名协议头时
    ///     截到的授权码没有 PKCE 校验串换不了票,静默登录参数由通行证公网入口拦掉(服务端设计稿 §3.8、§3.10)。
    ///   - iOS 不登记:走 ASWebAuthenticationSession(Assets/Plugins/iOS/MmorpgPassportWebAuth.mm),回调由系统直接交给发起会话的 App,
    ///     不需要 Info.plist 的 URL scheme;登记了反而让别的 App / 网页能用这个 scheme 把游戏拉起来。
    /// 协议头、主机与路径取自 ClientSettings.DefaultMobilePassportRedirectUri。
    /// </summary>
    public static class PassportMobileDeepLinkBuildPostProcess
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        internal static bool TryParseMobileRedirect(out string scheme, out string host, out string path)
        {
            scheme = string.Empty;
            host = string.Empty;
            path = string.Empty;
            if (!Uri.TryCreate(ClientSettings.DefaultMobilePassportRedirectUri, UriKind.Absolute, out var uri))
                return false;
            if (string.IsNullOrEmpty(uri.Scheme) || string.IsNullOrEmpty(uri.Host))
                return false;
            scheme = uri.Scheme;
            host = uri.Host;
            path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
            return true;
        }

        /// <summary>
        /// 向 AndroidManifest.xml 的主 LAUNCHER Activity 幂等注入深度链接 intent-filter。
        /// 拆成纯 XML 方法便于脱离 Android Build Support 单测验证。
        /// </summary>
        internal static bool EnsureAndroidDeepLinkFilter(XmlDocument doc, string scheme, string host, string pathPrefix)
        {
            if (doc == null || string.IsNullOrEmpty(scheme) || string.IsNullOrEmpty(host))
                return false;

            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("android", AndroidNamespace);

            XmlElement mainActivity = FindMainLauncherActivity(doc, nsMgr);
            if (mainActivity == null)
                return false;

            // 幂等检查:已有同 scheme + host 的 <data> 则不重复追加。
            var existingDataNodes = mainActivity.SelectNodes("intent-filter/data");
            if (existingDataNodes != null)
            {
                foreach (XmlNode node in existingDataNodes)
                {
                    if (node is not XmlElement el) continue;
                    string s = el.GetAttribute("scheme", AndroidNamespace);
                    string h = el.GetAttribute("host", AndroidNamespace);
                    if (string.Equals(s, scheme, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(h, host, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            XmlElement filter = doc.CreateElement("intent-filter");
            XmlElement action = doc.CreateElement("action");
            action.SetAttribute("name", AndroidNamespace, "android.intent.action.VIEW");
            filter.AppendChild(action);

            XmlElement catDefault = doc.CreateElement("category");
            catDefault.SetAttribute("name", AndroidNamespace, "android.intent.category.DEFAULT");
            filter.AppendChild(catDefault);

            XmlElement catBrowsable = doc.CreateElement("category");
            catBrowsable.SetAttribute("name", AndroidNamespace, "android.intent.category.BROWSABLE");
            filter.AppendChild(catBrowsable);

            XmlElement data = doc.CreateElement("data");
            data.SetAttribute("scheme", AndroidNamespace, scheme);
            data.SetAttribute("host", AndroidNamespace, host);
            if (!string.IsNullOrEmpty(pathPrefix) && pathPrefix != "/")
                data.SetAttribute("pathPrefix", AndroidNamespace, pathPrefix);
            filter.AppendChild(data);

            mainActivity.AppendChild(filter);
            return true;
        }

        private static XmlElement FindMainLauncherActivity(XmlDocument doc, XmlNamespaceManager nsMgr)
        {
            var activities = doc.SelectNodes("/manifest/application/activity");
            if (activities == null) return null;
            foreach (XmlNode node in activities)
            {
                if (node is not XmlElement activity) continue;
                var mainAction = activity.SelectSingleNode(
                    "intent-filter/action[@android:name='android.intent.action.MAIN']", nsMgr);
                var launcherCat = activity.SelectSingleNode(
                    "intent-filter/category[@android:name='android.intent.category.LAUNCHER']", nsMgr);
                if (mainAction != null && launcherCat != null)
                    return activity;
            }
            return null;
        }

    }

#if UNITY_ANDROID
    public sealed class PassportAndroidDeepLinkPostProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 110;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string manifestPath = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[PassportMobileDeepLink] AndroidManifest.xml not found at {manifestPath}");
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            bool changed = false;
            if (PassportMobileDeepLinkBuildPostProcess.TryParseMobileRedirect(out var scheme, out var host, out var path))
            {
                changed = PassportMobileDeepLinkBuildPostProcess.EnsureAndroidDeepLinkFilter(doc, scheme, host, path);
                Debug.Log($"[PassportMobileDeepLink] custom scheme {scheme}://{host}{path}");
            }
            if (!changed)
            {
                Debug.Log($"[PassportMobileDeepLink] Android manifest unchanged: {manifestPath}");
                return;
            }

            doc.Save(manifestPath);
            Debug.Log($"[PassportMobileDeepLink] Updated Android passport deep links in {manifestPath}");
        }
    }
#endif
}
