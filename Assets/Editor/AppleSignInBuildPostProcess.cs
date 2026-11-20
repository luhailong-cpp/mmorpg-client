#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace MmorpgClient.Editor
{
    /// <summary>
    /// 给导出的 Xcode 工程主 target 加 Sign in with Apple 能力(服务端 docs/design/account-deletion.md §3.1)。
    /// 没有这项权利,ASAuthorizationAppleIDProvider 的请求在真机上一律失败;原生桥是 Assets/Plugins/iOS/MmorpgAppleSignIn.mm。
    /// </summary>
    public static class AppleSignInBuildPostProcess
    {
        private const string DefaultEntitlementsPath = "Unity-iPhone/mmorpg.entitlements";

        [PostProcessBuild(100)]
        public static void AddSignInWithApple(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;

            string projectPath = PBXProject.GetPBXProjectPath(buildPath);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string mainTarget = project.GetUnityMainTargetGuid();
            // 主 target 已经配了权利文件(别的后处理或手工配置)就沿用,ProjectCapabilityManager 在原文件上追加,不另起一份把它顶掉。
            string entitlements = project.GetBuildPropertyForAnyConfig(mainTarget, "CODE_SIGN_ENTITLEMENTS");
            if (string.IsNullOrEmpty(entitlements)) entitlements = DefaultEntitlementsPath;

            var capabilities = new ProjectCapabilityManager(projectPath, entitlements, null, mainTarget);
            capabilities.AddSignInWithApple();
            capabilities.WriteToFile();
            Debug.Log($"[AppleSignInBuildPostProcess] Added Sign in with Apple capability ({entitlements})");
        }
    }
}
#endif
