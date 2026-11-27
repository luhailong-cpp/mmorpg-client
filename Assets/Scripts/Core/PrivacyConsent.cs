using UnityEngine;

namespace MmorpgClient.Core
{
    /// <summary>
    /// 首次启动的《用户协议》《隐私政策》同意记录(服务端 docs/design/official-passport-registration.md §3.9)。
    ///
    /// 国内应用商店与 App Store 都要求:首次运行先弹出协议与隐私政策、由玩家主动点同意,同意之前不收集个人信息
    /// (个人信息保护法第十三、十四、十七条)。协议或隐私政策改了就把 <see cref="CurrentVersion"/> 加一,所有玩家下次启动重新同意。
    /// 记的只是同意过的版本号,不是个人信息,放在 PlayerPrefs 即可。
    /// </summary>
    public static class PrivacyConsent
    {
        /// <summary>当前协议与隐私政策的版本。只增不减。</summary>
        public const int CurrentVersion = 1;

        private const string AcceptedVersionKey = "mmorpg.privacy.accepted_version";

        /// <summary>本机是否已同意当前版本。</summary>
        public static bool Accepted => IsCurrent(PlayerPrefs.GetInt(AcceptedVersionKey, 0));

        /// <summary>同意过的版本是否覆盖当前版本(纯函数,单测用)。</summary>
        public static bool IsCurrent(int acceptedVersion) => acceptedVersion >= CurrentVersion;

        public static void Accept()
        {
            PlayerPrefs.SetInt(AcceptedVersionKey, CurrentVersion);
            PlayerPrefs.Save();
        }
    }
}
