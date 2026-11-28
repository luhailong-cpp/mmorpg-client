using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MmorpgClient.Net;

namespace MmorpgClient.Core
{
    /// <summary>
    /// 本地账号历史记录的一条非敏感展示项(零 UnityEngine 依赖,可纯 C# 单测)。
    /// 绝不保存密码、id_token 或 RefreshToken;通行证 / Apple / Google 条目只存脱敏展示名与哈希键,
    /// 不在 PlayerPrefs 留下邮箱/手机号/用户名原文。
    /// </summary>
    public sealed class AccountHistoryEntry
    {
        public string EntryKey { get; }
        public string AuthType { get; }
        public string MaskedLabel { get; }
        /// <summary>仅口令账号(password)有值,用于点选历史条目时回填账号输入框(绝不回填密码);第三方渠道一律空串。</summary>
        public string PrefillAccount { get; }
        public uint LastZoneId { get; }
        public long LastUsedUnixSec { get; }

        public AccountHistoryEntry(string entryKey, string authType, string maskedLabel,
            string prefillAccount, uint lastZoneId, long lastUsedUnixSec)
        {
            EntryKey = entryKey ?? string.Empty;
            AuthType = authType ?? string.Empty;
            MaskedLabel = maskedLabel ?? string.Empty;
            PrefillAccount = AuthType == LoginAuthTypes.Password ? (prefillAccount ?? string.Empty) : string.Empty;
            LastZoneId = lastZoneId;
            LastUsedUnixSec = lastUsedUnixSec;
        }
    }

    /// <summary>
    /// 纯 C# 账号历史列表模型:最多保留 5 条、按最近使用倒序去重、支持单条删除与清空。
    /// </summary>
    public sealed class AccountHistoryStore
    {
        public const int MaxEntries = 5;
        private const string WireVersion = "v1";

        private readonly Func<string> _readRaw;
        private readonly Action<string> _writeRaw;
        private readonly Func<bool> _canPersist;

        public AccountHistoryStore(Func<string> readRaw, Action<string> writeRaw, Func<bool> canPersist = null)
        {
            _readRaw = readRaw ?? (() => string.Empty);
            _writeRaw = writeRaw ?? (_ => { });
            _canPersist = canPersist ?? (() => true);
        }

        public static string BuildEntryKey(string authType, string rawLabel)
        {
            string normalizedType = NormalizeAuthType(authType);
            if (normalizedType == LoginAuthTypes.Apple || normalizedType == LoginAuthTypes.Google ||
                normalizedType == LoginAuthTypes.WeChatMiniGame || normalizedType == LoginAuthTypes.DouyinMiniGame)
            {
                return normalizedType + ":default";
            }
            string trimmed = (rawLabel ?? string.Empty).Trim().ToLowerInvariant();
            if (trimmed.Length == 0) return normalizedType + ":anon";
            return normalizedType + ":" + Sha256Hex16(trimmed);
        }

        public static string MaskLabel(string rawLabel, string authType)
        {
            string normalizedType = NormalizeAuthType(authType);
            if (normalizedType == LoginAuthTypes.Apple) return "Apple 账号";
            if (normalizedType == LoginAuthTypes.Google) return "Google 账号";
            if (normalizedType == LoginAuthTypes.WeChatMiniGame) return "微信账号";
            if (normalizedType == LoginAuthTypes.DouyinMiniGame) return "抖音账号";

            string trimmed = (rawLabel ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return normalizedType == LoginAuthTypes.Passport ? "官方通行证" : "本地账号";

            int at = trimmed.IndexOf('@');
            if (at > 0 && at < trimmed.Length - 1)
            {
                string local = trimmed.Substring(0, at);
                string domain = trimmed.Substring(at + 1);
                string maskedLocal = local.Length <= 1 ? "*"
                    : local.Length == 2 ? local[0] + "*"
                    : local.Substring(0, 2) + "***";
                return maskedLocal + "@" + domain;
            }

            if (IsElevenAsciiDigits(trimmed))
                return trimmed.Substring(0, 3) + "****" + trimmed.Substring(7);

            if (trimmed.Length == 1) return "*";
            if (trimmed.Length <= 4) return trimmed[0] + "***";
            return trimmed.Substring(0, 2) + "***" + trimmed[trimmed.Length - 1];
        }

        public static string AuthTypeDisplayName(string authType) => NormalizeAuthType(authType) switch
        {
            LoginAuthTypes.Passport => "通行证",
            LoginAuthTypes.Apple => "Apple",
            LoginAuthTypes.Google => "Google",
            LoginAuthTypes.WeChatMiniGame => "微信",
            LoginAuthTypes.DouyinMiniGame => "抖音",
            _ => "账号",
        };

        public List<AccountHistoryEntry> GetEntries()
        {
            return Deserialize(_readRaw());
        }

        /// <summary>记录一次成功登录;未同意隐私协议时不落盘,仍返回本次构建好的条目供当次使用。</summary>
        public AccountHistoryEntry Record(string authType, string rawLabel, uint zoneId, long nowUnixSec)
        {
            string normalizedType = NormalizeAuthType(authType);
            string key = BuildEntryKey(normalizedType, rawLabel);
            string masked = MaskLabel(rawLabel, normalizedType);
            string prefill = normalizedType == LoginAuthTypes.Password ? (rawLabel ?? string.Empty).Trim() : string.Empty;
            var entry = new AccountHistoryEntry(key, normalizedType, masked, prefill, zoneId, nowUnixSec);

            if (!_canPersist()) return entry;

            var list = GetEntries();
            list.RemoveAll(e => string.Equals(e.EntryKey, key, StringComparison.Ordinal));
            list.Insert(0, entry);
            if (list.Count > MaxEntries)
                list.RemoveRange(MaxEntries, list.Count - MaxEntries);
            _writeRaw(Serialize(list));
            return entry;
        }

        /// <summary>免登续期成功后仅刷新该历史条目的区服与时间并置顶,避免把已脱敏标签二次脱敏或改变哈希键。</summary>
        public bool TouchZone(string entryKey, uint zoneId, long nowUnixSec)
        {
            if (!_canPersist() || string.IsNullOrEmpty(entryKey)) return false;
            var list = GetEntries();
            int idx = list.FindIndex(e => string.Equals(e.EntryKey, entryKey, StringComparison.Ordinal));
            if (idx < 0) return false;
            var old = list[idx];
            list.RemoveAt(idx);
            list.Insert(0, new AccountHistoryEntry(old.EntryKey, old.AuthType, old.MaskedLabel, old.PrefillAccount, zoneId, nowUnixSec));
            _writeRaw(Serialize(list));
            return true;
        }

        public bool Remove(string entryKey)
        {
            if (string.IsNullOrEmpty(entryKey)) return false;
            var list = GetEntries();
            int removed = list.RemoveAll(e => string.Equals(e.EntryKey, entryKey, StringComparison.Ordinal));
            if (removed == 0) return false;
            _writeRaw(list.Count == 0 ? string.Empty : Serialize(list));
            return true;
        }

        public void Clear() => _writeRaw(string.Empty);

        private static string NormalizeAuthType(string authType)
        {
            string trimmed = (authType ?? string.Empty).Trim();
            return trimmed.Length == 0 ? LoginAuthTypes.Password : trimmed;
        }

        private static bool IsElevenAsciiDigits(string value)
        {
            if (value.Length != 11) return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] < '0' || value[i] > '9') return false;
            return true;
        }

        private static string Sha256Hex16(string input)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private static string Serialize(IReadOnlyList<AccountHistoryEntry> entries)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (i > 0) sb.Append('\n');
                sb.Append(WireVersion).Append('\t')
                  .Append(EncodeField(e.EntryKey)).Append('\t')
                  .Append(EncodeField(e.AuthType)).Append('\t')
                  .Append(EncodeField(e.MaskedLabel)).Append('\t')
                  .Append(EncodeField(e.PrefillAccount)).Append('\t')
                  .Append(e.LastZoneId).Append('\t')
                  .Append(e.LastUsedUnixSec);
            }
            return sb.ToString();
        }

        private static List<AccountHistoryEntry> Deserialize(string raw)
        {
            var result = new List<AccountHistoryEntry>();
            if (string.IsNullOrWhiteSpace(raw)) return result;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in raw.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length != 7 || parts[0] != WireVersion) continue;
                if (!TryDecodeField(parts[1], out string key) || string.IsNullOrEmpty(key)) continue;
                if (!TryDecodeField(parts[2], out string authType) || string.IsNullOrEmpty(authType)) continue;
                if (!TryDecodeField(parts[3], out string masked)) continue;
                if (!TryDecodeField(parts[4], out string prefill)) continue;
                if (!uint.TryParse(parts[5], out uint zoneId)) continue;
                if (!long.TryParse(parts[6], out long lastUsed)) continue;
                if (!seen.Add(key)) continue;
                result.Add(new AccountHistoryEntry(key, authType, masked, prefill, zoneId, lastUsed));
                if (result.Count >= MaxEntries) break;
            }
            return result;
        }

        internal static string EncodeField(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        internal static bool TryDecodeField(string encoded, out string value)
        {
            value = string.Empty;
            if (encoded == null) return false;
            if (encoded.Length == 0) return true;
            try
            {
                value = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
