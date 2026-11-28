using UnityEngine;

namespace MmorpgClient.Core
{
    /// <summary>
    /// 客户端「账号历史记录」与「保持登录(SecureStore RefreshToken)」运行期装配入口。
    /// PlayerPrefs 仅保存非敏感的脱敏展示元数据与开关偏好;RefreshToken 严格只进 <see cref="SecureStore.Default"/>。
    /// </summary>
    public static class ClientAuthStorage
    {
        private const string AccountHistoryPrefsKey = "mmorpg.auth.history.v1";
        private const string KeepSignedInPrefsKey = "mmorpg.auth.keep_signed_in.v1";
        private const string SavedSessionMetaPrefsKey = "mmorpg.auth.saved_session_meta.v1";

        private static AccountHistoryStore _history;
        private static RememberedSessionStore _remembered;

        public static AccountHistoryStore History => _history ??= new AccountHistoryStore(
            () => PlayerPrefs.GetString(AccountHistoryPrefsKey, string.Empty),
            raw =>
            {
                if (string.IsNullOrEmpty(raw)) PlayerPrefs.DeleteKey(AccountHistoryPrefsKey);
                else PlayerPrefs.SetString(AccountHistoryPrefsKey, raw);
                PlayerPrefs.Save();
            },
            () => PrivacyConsent.Accepted);

        public static RememberedSessionStore RememberedSession => _remembered ??= new RememberedSessionStore(
            SecureStore.Default,
            // 桌面与移动端默认勾选「保持登录」,公用电脑可手动取消勾选(取消后立即清空本地加密令牌)。
            () => PlayerPrefs.GetInt(KeepSignedInPrefsKey, 1) != 0,
            enabled =>
            {
                PlayerPrefs.SetInt(KeepSignedInPrefsKey, enabled ? 1 : 0);
                PlayerPrefs.Save();
            },
            () => PlayerPrefs.GetString(SavedSessionMetaPrefsKey, string.Empty),
            raw =>
            {
                if (string.IsNullOrEmpty(raw)) PlayerPrefs.DeleteKey(SavedSessionMetaPrefsKey);
                else PlayerPrefs.SetString(SavedSessionMetaPrefsKey, raw);
                PlayerPrefs.Save();
            },
            () => PrivacyConsent.Accepted);
    }
}
