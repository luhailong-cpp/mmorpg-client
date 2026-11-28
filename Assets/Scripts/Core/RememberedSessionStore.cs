using System;
using System.Text;

namespace MmorpgClient.Core
{
    /// <summary>本机当前保持登录的账号展示元数据(不含任何令牌或密码)。</summary>
    public readonly struct RememberedSessionInfo
    {
        public string EntryKey { get; }
        public string AuthType { get; }
        public string MaskedLabel { get; }
        public uint ZoneId { get; }
        public long SavedAtUnixSec { get; }

        public RememberedSessionInfo(string entryKey, string authType, string maskedLabel, uint zoneId, long savedAtUnixSec)
        {
            EntryKey = entryKey ?? string.Empty;
            AuthType = authType ?? string.Empty;
            MaskedLabel = maskedLabel ?? string.Empty;
            ZoneId = zoneId;
            SavedAtUnixSec = savedAtUnixSec;
        }

        public bool IsValid => !string.IsNullOrEmpty(EntryKey) && !string.IsNullOrEmpty(AuthType);
    }

    /// <summary>
    /// 防盗号「保持登录」纯 C# 存储模型(零 UnityEngine 依赖,可单测):
    ///   - 同一设备最多只保存当前 1 个活跃账号的可吊销 RefreshToken,且只写入 <see cref="ISecureStringStore"/>(系统加密库),绝不落盘密码或明文令牌;
    ///   - 每次令牌轮换必须同步覆盖新 RefreshToken(旧令牌已被服务端一次性消费,保留旧值会触发家族重用吊销);
    ///   - 关闭「保持登录」、主动退出登录、切号失败或服务端 401/改密踢线时立即清空本地密文与元数据。
    /// </summary>
    public sealed class RememberedSessionStore
    {
        public const string RefreshTokenSecureKey = "refresh_token_v1";
        private const string WireVersion = "v1";

        private readonly ISecureStringStore _secureStore;
        private readonly Func<bool> _readKeepSignedIn;
        private readonly Action<bool> _writeKeepSignedIn;
        private readonly Func<string> _readMetadata;
        private readonly Action<string> _writeMetadata;
        private readonly Func<bool> _canPersist;

        public RememberedSessionStore(
            ISecureStringStore secureStore,
            Func<bool> readKeepSignedIn,
            Action<bool> writeKeepSignedIn,
            Func<string> readMetadata,
            Action<string> writeMetadata,
            Func<bool> canPersist = null)
        {
            _secureStore = secureStore ?? throw new ArgumentNullException(nameof(secureStore));
            _readKeepSignedIn = readKeepSignedIn ?? (() => false);
            _writeKeepSignedIn = writeKeepSignedIn ?? (_ => { });
            _readMetadata = readMetadata ?? (() => string.Empty);
            _writeMetadata = writeMetadata ?? (_ => { });
            _canPersist = canPersist ?? (() => true);
        }

        public bool IsSecureStorageAvailable => _secureStore.IsAvailable;

        public bool KeepSignedIn
        {
            get => _secureStore.IsAvailable && _readKeepSignedIn();
            set
            {
                bool enabled = value && _secureStore.IsAvailable;
                if (_canPersist()) _writeKeepSignedIn(enabled);
                if (!enabled) Clear();
            }
        }

        public bool HasSavedSession => TryGetSavedSession(out _, out _);

        public bool TryGetSavedMetadata(out RememberedSessionInfo info)
            => TryGetSavedSession(out info, out _);

        public bool TryGetSavedSession(out RememberedSessionInfo info, out string refreshToken)
        {
            info = default;
            refreshToken = string.Empty;
            if (!KeepSignedIn) return false;

            if (!TryDeserializeMetadata(_readMetadata(), out var meta) || !meta.IsValid)
            {
                Clear();
                return false;
            }
            if (!_secureStore.TryLoad(RefreshTokenSecureKey, out string token) || string.IsNullOrEmpty(token))
            {
                Clear();
                return false;
            }

            info = meta;
            refreshToken = token;
            return true;
        }

        /// <summary>登录成功后写入当前账号的 RefreshToken(仅在开启「保持登录」且平台安全存储可用时落盘)。</summary>
        public bool Save(string refreshToken, string entryKey, string authType, string maskedLabel, uint zoneId, long nowUnixSec)
        {
            if (!_canPersist() || !KeepSignedIn || string.IsNullOrEmpty(refreshToken) ||
                string.IsNullOrEmpty(entryKey) || string.IsNullOrEmpty(authType))
            {
                Clear();
                return false;
            }

            if (!_secureStore.TrySave(RefreshTokenSecureKey, refreshToken))
            {
                Clear();
                return false;
            }

            var info = new RememberedSessionInfo(entryKey, authType, maskedLabel, zoneId, nowUnixSec);
            _writeMetadata(SerializeMetadata(info));
            return true;
        }

        /// <summary>
        /// 服务端轮换 RefreshToken 后立即同步密文。若写入失败则清空本地会话,防止残留已失效旧令牌导致下次触发重用吊销。
        /// </summary>
        public bool UpdateRotatedRefreshToken(string newRefreshToken, long nowUnixSec)
        {
            if (string.IsNullOrEmpty(newRefreshToken))
            {
                Clear();
                return false;
            }
            if (!_canPersist() || !KeepSignedIn) return false;
            if (!TryDeserializeMetadata(_readMetadata(), out var meta) || !meta.IsValid) return false;

            if (!_secureStore.TrySave(RefreshTokenSecureKey, newRefreshToken))
            {
                Clear();
                return false;
            }
            var updated = new RememberedSessionInfo(meta.EntryKey, meta.AuthType, meta.MaskedLabel, meta.ZoneId, nowUnixSec);
            _writeMetadata(SerializeMetadata(updated));
            return true;
        }

        /// <summary>退出登录 / 401 失效 / 改密被踢 / 关闭保持登录时清空本地密文与元数据。</summary>
        public void Clear()
        {
            // shortcut: logout clears local SecureStore only without server-side family revoke RPC; add /api/logout when gateway exposes token revocation
            _secureStore.Delete(RefreshTokenSecureKey);
            _writeMetadata(string.Empty);
        }

        private static string SerializeMetadata(RememberedSessionInfo info)
        {
            return new StringBuilder()
                .Append(WireVersion).Append('\t')
                .Append(AccountHistoryStore.EncodeField(info.EntryKey)).Append('\t')
                .Append(AccountHistoryStore.EncodeField(info.AuthType)).Append('\t')
                .Append(AccountHistoryStore.EncodeField(info.MaskedLabel)).Append('\t')
                .Append(info.ZoneId).Append('\t')
                .Append(info.SavedAtUnixSec)
                .ToString();
        }

        private static bool TryDeserializeMetadata(string raw, out RememberedSessionInfo info)
        {
            info = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string[] parts = raw.Trim().Split('\t');
            if (parts.Length != 6 || parts[0] != WireVersion) return false;
            if (!AccountHistoryStore.TryDecodeField(parts[1], out string entryKey) || string.IsNullOrEmpty(entryKey)) return false;
            if (!AccountHistoryStore.TryDecodeField(parts[2], out string authType) || string.IsNullOrEmpty(authType)) return false;
            if (!AccountHistoryStore.TryDecodeField(parts[3], out string maskedLabel)) return false;
            if (!uint.TryParse(parts[4], out uint zoneId)) return false;
            if (!long.TryParse(parts[5], out long savedAt)) return false;
            info = new RememberedSessionInfo(entryKey, authType, maskedLabel, zoneId, savedAt);
            return info.IsValid;
        }
    }
}
