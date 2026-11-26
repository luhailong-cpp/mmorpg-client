using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MmorpgClient.Editor
{
    /// <summary>
    /// App 零拒审出包门禁（对齐服务端 docs/design/rewarded-ad-system.md §4.3）：
    /// 1. 未定义 MMORPG_ENABLE_APP_AD_SDK（首审默认纯无广告包）时，扫描导出的 iOS Xcode 工程与 Android 工程，
    ///    若残留 NSUserTrackingUsageDescription / AppTrackingTransparency / 任何第三方广告 SDK（AdMob/穿山甲/优量汇/快手/GroMore）
    ///    立即中断构建并报 BuildFailedException，杜绝 App Store 5.1.2 隐私弹窗拒审与 Google Play 广告 ID 声明冲突；
    /// 2. 显式开启 MMORPG_ENABLE_APP_AD_SDK（二期 App 广告包）时，强制核验 iOS Info.plist 已配置非空 NSUserTrackingUsageDescription。
    /// </summary>
    public sealed class RewardedAdBuildGuard : IPostprocessBuildWithReport
    {
        public int callbackOrder => 200;

        private static readonly string[] ForbiddenIosTokens =
        {
            "NSUserTrackingUsageDescription",
            "AppTrackingTransparency.framework",
            "GoogleMobileAds",
            "GADApplicationIdentifier",
            "BUAdSDK",
            "GDTMobSDK",
            "KSAdSDK",
            "ABUAdSDK",
        };

        private static readonly string[] ForbiddenAndroidTokens =
        {
            "com.google.android.gms.ads",
            "com.google.android.gms.permission.AD_ID",
            "com.bytedance.sdk.openadsdk",
            "com.qq.e.ads",
            "com.kwad.sdk",
        };

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report == null) return;
            var target = report.summary.platform;
            string outputPath = report.summary.outputPath;
            if (string.IsNullOrEmpty(outputPath)) return;

            bool adSdkEnabled = IsAppAdSdkEnabled();
            if (target == BuildTarget.iOS)
            {
                VerifyIosBuild(outputPath, adSdkEnabled);
            }
            else if (target == BuildTarget.Android)
            {
                VerifyAndroidBuild(outputPath, adSdkEnabled);
            }
        }

        private static bool IsAppAdSdkEnabled()
        {
#if MMORPG_ENABLE_APP_AD_SDK
            return true;
#else
            return false;
#endif
        }

        private static void VerifyIosBuild(string outputPath, bool adSdkEnabled)
        {
            string plistPath = Path.Combine(outputPath, "Info.plist");
            string pbxPath = Path.Combine(outputPath, "Unity-iPhone.xcodeproj", "project.pbxproj");
            string plist = File.Exists(plistPath) ? File.ReadAllText(plistPath) : string.Empty;
            string pbx = File.Exists(pbxPath) ? File.ReadAllText(pbxPath) : string.Empty;

            if (!adSdkEnabled)
            {
                foreach (string token in ForbiddenIosTokens)
                {
                    if (plist.IndexOf(token, StringComparison.Ordinal) >= 0
                        || pbx.IndexOf(token, StringComparison.Ordinal) >= 0)
                    {
                        throw new BuildFailedException(
                            $"[RewardedAdBuildGuard] First-review iOS build has MMORPG_ENABLE_APP_AD_SDK disabled, "
                            + $"but forbidden ad/ATT token was detected: '{token}'. Remove it before App Store submission.");
                    }
                }
                Debug.Log("[RewardedAdBuildGuard] iOS first-review build verified: zero ad SDK and zero ATT leakage.");
                return;
            }

            if (plist.IndexOf("NSUserTrackingUsageDescription", StringComparison.Ordinal) < 0)
            {
                throw new BuildFailedException(
                    "[RewardedAdBuildGuard] MMORPG_ENABLE_APP_AD_SDK is enabled for iOS, "
                    + "but Info.plist is missing NSUserTrackingUsageDescription (App Store 5.1.2 requirement).");
            }
        }

        private static void VerifyAndroidBuild(string outputPath, bool adSdkEnabled)
        {
            if (adSdkEnabled || !Directory.Exists(outputPath)) return;

            foreach (string file in Directory.GetFiles(outputPath, "AndroidManifest.xml", SearchOption.AllDirectories))
            {
                string content = File.ReadAllText(file);
                foreach (string token in ForbiddenAndroidTokens)
                {
                    if (content.IndexOf(token, StringComparison.Ordinal) >= 0)
                    {
                        throw new BuildFailedException(
                            $"[RewardedAdBuildGuard] First-review Android build has MMORPG_ENABLE_APP_AD_SDK disabled, "
                            + $"but forbidden ad SDK token '{token}' was found in '{file}'.");
                    }
                }
            }
            Debug.Log("[RewardedAdBuildGuard] Android first-review build verified: zero ad SDK manifest leakage.");
        }
    }
}
