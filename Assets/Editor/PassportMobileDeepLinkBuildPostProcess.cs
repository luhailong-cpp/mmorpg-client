using System;
using System.IO;
using System.Xml;
using MmorpgClient.Core;
using UnityEngine;
#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif
#if UNITY_ANDROID
using UnityEditor.Android;
#endif

namespace MmorpgClient.Editor
{
    /// <summary>
    /// 移动端官方通行证(Casdoor OIDC)回调深度链接自动登记(服务端 docs/design/official-passport-registration.md):
    ///   - iOS: 导出 Xcode 工程时向 Info.plist 的 CFBundleURLTypes 幂等追加 xuanming scheme;
    ///   - Android: 导出 Gradle 工程时向主 LAUNCHER Activity 的 AndroidManifest.xml 幂等追加
    ///     VIEW/DEFAULT/BROWSABLE intent-filter(xuanming://passport/callback),使 Application.deepLinkActivated 生效。
    /// 协议与路径单一事实源取自 ClientSettings.DefaultMobilePassportRedirectUri。
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

#if UNITY_IOS
        [PostProcessBuild(110)]
        public static void AddIosUrlScheme(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;
            if (!TryParseMobileRedirect(out var scheme, out _, out _))
            {
                Debug.LogWarning("[PassportMobileDeepLink] Invalid DefaultMobilePassportRedirectUri; skipping iOS URL scheme");
                return;
            }

            string plistPath = Path.Combine(buildPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"[PassportMobileDeepLink] Info.plist not found at {plistPath}");
                return;
            }

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementDict root = plist.root;

            PlistElementArray urlTypes = root["CFBundleURLTypes"] as PlistElementArray
                ?? root.CreateArray("CFBundleURLTypes");

            foreach (PlistElement item in urlTypes.values)
            {
                if (item is not PlistElementDict dict) continue;
                if (dict["CFBundleURLSchemes"] is not PlistElementArray schemes) continue;
                foreach (PlistElement s in schemes.values)
                {
                    if (string.Equals(s.AsString(), scheme, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log($"[PassportMobileDeepLink] iOS URL scheme '{scheme}' already present in Info.plist");
                        return;
                    }
                }
            }

            PlistElementDict entry = urlTypes.AddDict();
            entry.SetString("CFBundleTypeRole", "Editor");
            entry.SetString("CFBundleURLName", "com.mmorpg.passport");
            PlistElementArray newSchemes = entry.CreateArray("CFBundleURLSchemes");
            newSchemes.AddString(scheme);

            plist.WriteToFile(plistPath);
            Debug.Log($"[PassportMobileDeepLink] Registered iOS URL scheme '{scheme}' in Info.plist");
        }
#endif
    }

#if UNITY_ANDROID
    public sealed class PassportAndroidDeepLinkPostProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 110;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            if (!PassportMobileDeepLinkBuildPostProcess.TryParseMobileRedirect(out var scheme, out var host, out var path))
            {
                Debug.LogWarning("[PassportMobileDeepLink] Invalid DefaultMobilePassportRedirectUri; skipping Android manifest update");
                return;
            }

            string manifestPath = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[PassportMobileDeepLink] AndroidManifest.xml not found at {manifestPath}");
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            if (!PassportMobileDeepLinkBuildPostProcess.EnsureAndroidDeepLinkFilter(doc, scheme, host, path))
            {
                Debug.Log($"[PassportMobileDeepLink] Android deep link '{scheme}://{host}{path}' unchanged in {manifestPath}");
                return;
            }

            doc.Save(manifestPath);
            Debug.Log($"[PassportMobileDeepLink] Registered Android deep link '{scheme}://{host}{path}' in {manifestPath}");
        }
    }
#endif
}
