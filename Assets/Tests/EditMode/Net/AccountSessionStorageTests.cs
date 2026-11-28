using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MmorpgClient.Core;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    public sealed class AccountSessionStorageTests
    {
        private sealed class MemorySecureStore : ISecureStringStore
        {
            public bool IsAvailable { get; set; } = true;
            public bool FailSave { get; set; }
            public readonly Dictionary<string, string> Data = new(StringComparer.Ordinal);

            public bool TrySave(string key, string secret)
            {
                if (!IsAvailable || FailSave || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(secret))
                    return false;
                Data[key] = secret;
                return true;
            }

            public bool TryLoad(string key, out string secret)
            {
                secret = string.Empty;
                if (!IsAvailable || string.IsNullOrEmpty(key)) return false;
                return Data.TryGetValue(key, out secret) && !string.IsNullOrEmpty(secret);
            }

            public void Delete(string key)
            {
                if (!string.IsNullOrEmpty(key)) Data.Remove(key);
            }
        }

        [Test]
        public void AccountHistoryMaskLabel_MasksEmailsPhonesUsernamesAndChannels()
        {
            Assert.That(AccountHistoryStore.MaskLabel("lhlcpp", LoginAuthTypes.Passport), Is.EqualTo("lh***p"));
            Assert.That(AccountHistoryStore.MaskLabel("alice@example.com", LoginAuthTypes.Passport), Is.EqualTo("al***@example.com"));
            Assert.That(AccountHistoryStore.MaskLabel("13812345678", LoginAuthTypes.Passport), Is.EqualTo("138****5678"));
            Assert.That(AccountHistoryStore.MaskLabel("ab", LoginAuthTypes.Passport), Is.EqualTo("a***"));
            Assert.That(AccountHistoryStore.MaskLabel("", LoginAuthTypes.Passport), Is.EqualTo("官方通行证"));
            Assert.That(AccountHistoryStore.MaskLabel("sub-123", LoginAuthTypes.Apple), Is.EqualTo("Apple 账号"));
            Assert.That(AccountHistoryStore.MaskLabel("sub-456", LoginAuthTypes.Google), Is.EqualTo("Google 账号"));
        }

        [Test]
        public void AccountHistoryStore_DedupsCapsRemovesToleratesCorruptionAndNeverLeaksPassportRawName()
        {
            string raw = string.Empty;
            bool consented = false;
            var store = new AccountHistoryStore(() => raw, v => raw = v, () => consented);

            // 未同意隐私协议前不落盘
            store.Record(LoginAuthTypes.Passport, "lhlcpp@example.com", 1, 100);
            Assert.That(raw, Is.Empty);
            Assert.That(store.GetEntries(), Is.Empty);

            consented = true;
            var first = store.Record(LoginAuthTypes.Passport, "lhlcpp@example.com", 1, 100);
            Assert.That(raw, Does.Not.Contain("lhlcpp@example.com"));
            Assert.That(raw, Does.Not.Contain("lhlcpp"));
            Assert.That(first.MaskedLabel, Is.EqualTo("lh***@example.com"));
            Assert.That(first.PrefillAccount, Is.Empty);

            // 写入超过上限 5 条并验证去重与置顶
            for (int i = 1; i <= 6; i++)
                store.Record(LoginAuthTypes.Password, $"dev_user{i}", (uint)i, 200 + i);
            var list = store.GetEntries();
            Assert.That(list.Count, Is.EqualTo(AccountHistoryStore.MaxEntries));
            Assert.That(list[0].PrefillAccount, Is.EqualTo("dev_user6"));

            // TouchZone 置顶更新区服而不二次脱敏
            string oldestKey = list[list.Count - 1].EntryKey;
            string oldestMask = list[list.Count - 1].MaskedLabel;
            Assert.That(store.TouchZone(oldestKey, 9, 999), Is.True);
            list = store.GetEntries();
            Assert.That(list[0].EntryKey, Is.EqualTo(oldestKey));
            Assert.That(list[0].MaskedLabel, Is.EqualTo(oldestMask));
            Assert.That(list[0].LastZoneId, Is.EqualTo(9u));

            // 单条删除
            Assert.That(store.Remove(oldestKey), Is.True);
            Assert.That(store.GetEntries().Count, Is.EqualTo(AccountHistoryStore.MaxEntries - 1));

            // 损坏行容错
            raw = "corrupt-line\nv1\tbad-base64!!\n" + raw;
            Assert.That(store.GetEntries().Count, Is.EqualTo(AccountHistoryStore.MaxEntries - 1));
        }

        [Test]
        public void RememberedSessionStore_KeepsTokenOutOfMetadataRotatesClearsOnOptOutAndFailsClosed()
        {
            var secure = new MemorySecureStore();
            bool keep = true;
            string meta = string.Empty;
            var store = new RememberedSessionStore(secure, () => keep, v => keep = v, () => meta, v => meta = v);

            const string token1 = "arun.secret_refresh_token_1";
            const string token2 = "arun.secret_refresh_token_2";
            Assert.That(store.Save(token1, "passport:abc", LoginAuthTypes.Passport, "lh***p", 1, 1000), Is.True);
            Assert.That(meta, Does.Not.Contain(token1));
            Assert.That(store.TryGetSavedSession(out var info, out string loaded), Is.True);
            Assert.That(loaded, Is.EqualTo(token1));
            Assert.That(info.MaskedLabel, Is.EqualTo("lh***p"));

            // 轮换覆盖旧令牌
            Assert.That(store.UpdateRotatedRefreshToken(token2, 1100), Is.True);
            Assert.That(store.TryGetSavedSession(out _, out loaded), Is.True);
            Assert.That(loaded, Is.EqualTo(token2));

            // 轮换写密文失败时清空,绝不留已失效旧令牌触发重用吊销
            secure.FailSave = true;
            Assert.That(store.UpdateRotatedRefreshToken("arun.token3", 1200), Is.False);
            secure.FailSave = false;
            Assert.That(store.HasSavedSession, Is.False);
            Assert.That(secure.Data, Is.Empty);

            // 关闭保持登录立刻清密文与元数据
            Assert.That(store.Save(token1, "passport:abc", LoginAuthTypes.Passport, "lh***p", 1, 1300), Is.True);
            store.KeepSignedIn = false;
            Assert.That(store.HasSavedSession, Is.False);
            Assert.That(secure.Data, Is.Empty);
            Assert.That(meta, Is.Empty);

            // 平台安全存储不可用时 fail-closed
            secure.IsAvailable = false;
            store.KeepSignedIn = true;
            Assert.That(store.KeepSignedIn, Is.False);
            Assert.That(store.Save(token1, "passport:abc", LoginAuthTypes.Passport, "lh***p", 1, 1400), Is.False);
        }

        [Test]
        public void PassportTryExtractDisplayName_ReadsPayloadWithoutLoggingOrThrowing()
        {
            string jwt = BuildUnsignedJwt("{\"sub\":\"uuid-1\",\"name\":\"lhlcpp\",\"email\":\"lhl@example.com\"}");
            Assert.That(PassportOAuthProtocol.TryExtractDisplayName(jwt, out string name), Is.True);
            Assert.That(name, Is.EqualTo("lhlcpp"));

            string emailOnly = BuildUnsignedJwt("{\"sub\":\"uuid-2\",\"email\":\"player@example.com\"}");
            Assert.That(PassportOAuthProtocol.TryExtractDisplayName(emailOnly, out string email), Is.True);
            Assert.That(email, Is.EqualTo("player@example.com"));

            foreach (var bad in new[] { "", null, "not-a-jwt", "a.!!!.c", BuildUnsignedJwt("{\"sub\":\"only-sub\"}") })
                Assert.That(PassportOAuthProtocol.TryExtractDisplayName(bad, out _), Is.False, bad ?? "<null>");
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [Test]
        public void Win32DpapiSecureStore_RoundTripsRejectsTamperingCrossKeySwapAndInvalidKeys()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mmorpg_dpapi_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new Win32DpapiSecureStore(dir);
                Assert.That(store.IsAvailable, Is.True);
                Assert.That(store.TrySave("../escape", "tok"), Is.False);

                const string secret = "arun.real_refresh_token_payload_12345";
                Assert.That(store.TrySave("refresh_token_v1", secret), Is.True);

                string blobPath = Path.Combine(dir, ".mmorpg_sec_refresh_token_v1.dpapi");
                Assert.That(File.Exists(blobPath), Is.True);
                string rawText = Encoding.UTF8.GetString(File.ReadAllBytes(blobPath));
                Assert.That(rawText, Does.Not.Contain(secret));

                Assert.That(store.TryLoad("refresh_token_v1", out string loaded), Is.True);
                Assert.That(loaded, Is.EqualTo(secret));

                // 跨 key 替换密文文件应因独立熵值不同而解密失败
                File.Copy(blobPath, Path.Combine(dir, ".mmorpg_sec_other_key.dpapi"));
                Assert.That(store.TryLoad("other_key", out _), Is.False);

                // 篡改密文字节应解密失败
                byte[] tampered = File.ReadAllBytes(blobPath);
                tampered[tampered.Length - 1] ^= 0xFF;
                File.WriteAllBytes(blobPath, tampered);
                Assert.That(store.TryLoad("refresh_token_v1", out _), Is.False);

                store.Delete("refresh_token_v1");
                Assert.That(File.Exists(blobPath), Is.False);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
#endif

        private static string BuildUnsignedJwt(string payloadJson)
        {
            string header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\"}"));
            string payload = Base64Url(Encoding.UTF8.GetBytes(payloadJson));
            return $"{header}.{payload}.sig";
        }

        private static string Base64Url(byte[] bytes)
            => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
