using System;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || (UNITY_IOS && !UNITY_EDITOR)
using System.Runtime.InteropServices;
#endif

namespace MmorpgClient.Core
{
    /// <summary>
    /// 平台安全凭据存储抽象(纯接口,单测可注入内存替身)。
    /// 只用于持久化可吊销的 RefreshToken;严禁用于存玩家明文密码,且任何不可用平台一律 fail-closed,绝不回落到明文 PlayerPrefs。
    /// </summary>
    public interface ISecureStringStore
    {
        /// <summary>当前平台是否具备操作系统级加密凭据存储。</summary>
        bool IsAvailable { get; }

        /// <summary>加密写入一条凭据;空串或平台不支持时返回 false。</summary>
        bool TrySave(string key, string secret);

        /// <summary>读取并解密一条凭据;不存在、损坏或跨设备拷入时返回 false。</summary>
        bool TryLoad(string key, out string secret);

        /// <summary>删除指定凭据(幂等)。</summary>
        void Delete(string key);
    }

    /// <summary>
    /// 客户端系统级安全凭据存储入口:
    ///   - Windows / Windows 编辑器:Win32 DPAPI(CurrentUser + 独立熵值,拷到别的用户或电脑解不开);
    ///   - iOS 真机:Keychain(kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,不随 iCloud 备份);
    ///   - Android 真机:AndroidKeyStore AES-256-GCM + 私有 SharedPreferences;
    ///   - 微信/抖音小游戏(WebGL)与未适配平台:关闭(IsAvailable = false),绝不回落明文。
    /// </summary>
    public static class SecureStore
    {
        private static ISecureStringStore _default;

        public static ISecureStringStore Default => _default ??= CreatePlatformStore();

        private static ISecureStringStore CreatePlatformStore()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return UnavailableSecureStore.Instance;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return new Win32DpapiSecureStore(Application.persistentDataPath);
#elif UNITY_IOS && !UNITY_EDITOR
            return new IosKeychainSecureStore();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidKeystoreSecureStore();
#else
            // shortcut: macOS/Linux desktop builds return UnavailableSecureStore; add Keychain/libsecret before shipping standalone macOS/Linux
            return UnavailableSecureStore.Instance;
#endif
        }

        internal static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 64) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        private sealed class UnavailableSecureStore : ISecureStringStore
        {
            public static readonly UnavailableSecureStore Instance = new();
            public bool IsAvailable => false;
            public bool TrySave(string key, string secret) => false;
            public bool TryLoad(string key, out string secret) { secret = string.Empty; return false; }
            public void Delete(string key) { }
        }
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    /// <summary>
    /// Windows DPAPI(crypt32.dll CryptProtectData/CryptUnprotectData,CurrentUser 作用域)实现。
    /// 密文与当前 Windows 登录用户及机器凭据绑定,并混入按 key 派生的熵值,防跨键替换与跨机拷贝。
    /// </summary>
    public sealed class Win32DpapiSecureStore : ISecureStringStore
    {
        private const uint CryptProtectUiForbidden = 0x1;
        private readonly string _directoryPath;

        public Win32DpapiSecureStore(string directoryPath)
        {
            _directoryPath = string.IsNullOrWhiteSpace(directoryPath) ? string.Empty : directoryPath;
        }

        public bool IsAvailable => !string.IsNullOrEmpty(_directoryPath);

        public bool TrySave(string key, string secret)
        {
            if (!IsAvailable || !SecureStore.IsValidKey(key) || string.IsNullOrEmpty(secret))
                return false;

            byte[] plain = Encoding.UTF8.GetBytes(secret);
            byte[] entropy = BuildEntropy(key);
            try
            {
                if (!Protect(plain, entropy, out byte[] cipher))
                    return false;

                Directory.CreateDirectory(_directoryPath);
                string target = FilePathFor(key);
                string temp = target + ".tmp." + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllBytes(temp, cipher);
                    if (File.Exists(target))
                        File.Replace(temp, target, null);
                    else
                        File.Move(temp, target);
                    return true;
                }
                finally
                {
                    if (File.Exists(temp))
                    {
                        try { File.Delete(temp); } catch (Exception) { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[secure-store] dpapi save failed: {ex.GetType().Name}");
                return false;
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public bool TryLoad(string key, out string secret)
        {
            secret = string.Empty;
            if (!IsAvailable || !SecureStore.IsValidKey(key))
                return false;

            string path = FilePathFor(key);
            if (!File.Exists(path))
                return false;

            byte[] plain = null;
            try
            {
                byte[] cipher = File.ReadAllBytes(path);
                if (cipher.Length == 0 || cipher.Length > 65536)
                    return false;
                if (!Unprotect(cipher, BuildEntropy(key), out plain) || plain == null || plain.Length == 0)
                    return false;
                secret = Encoding.UTF8.GetString(plain);
                return !string.IsNullOrEmpty(secret);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[secure-store] dpapi load failed: {ex.GetType().Name}");
                return false;
            }
            finally
            {
                if (plain != null) Array.Clear(plain, 0, plain.Length);
            }
        }

        public void Delete(string key)
        {
            if (!IsAvailable || !SecureStore.IsValidKey(key))
                return;
            try
            {
                string path = FilePathFor(key);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[secure-store] dpapi delete failed: {ex.GetType().Name}");
            }
        }

        private string FilePathFor(string key) => Path.Combine(_directoryPath, ".mmorpg_sec_" + key + ".dpapi");

        private static byte[] BuildEntropy(string key) => Encoding.UTF8.GetBytes("mmorpg.client.dpapi.v1:" + key);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob pDataIn,
            string szDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            uint dwFlags,
            out DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob pDataIn,
            IntPtr ppszDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            uint dwFlags,
            out DataBlob pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static bool Protect(byte[] plain, byte[] entropy, out byte[] cipher)
        {
            cipher = null;
            var inBlob = AllocBlob(plain);
            var entropyBlob = AllocBlob(entropy);
            var outBlob = default(DataBlob);
            try
            {
                if (!CryptProtectData(ref inBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                        CryptProtectUiForbidden, out outBlob))
                    return false;
                if (outBlob.cbData <= 0 || outBlob.pbData == IntPtr.Zero)
                    return false;
                cipher = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, cipher, 0, outBlob.cbData);
                return true;
            }
            finally
            {
                FreeHGlobalBlob(ref inBlob, zeroMemory: true);
                FreeHGlobalBlob(ref entropyBlob, zeroMemory: false);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        private static bool Unprotect(byte[] cipher, byte[] entropy, out byte[] plain)
        {
            plain = null;
            var inBlob = AllocBlob(cipher);
            var entropyBlob = AllocBlob(entropy);
            var outBlob = default(DataBlob);
            try
            {
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                        CryptProtectUiForbidden, out outBlob))
                    return false;
                if (outBlob.cbData <= 0 || outBlob.pbData == IntPtr.Zero)
                    return false;
                plain = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, plain, 0, outBlob.cbData);
                return true;
            }
            finally
            {
                FreeHGlobalBlob(ref inBlob, zeroMemory: false);
                FreeHGlobalBlob(ref entropyBlob, zeroMemory: false);
                if (outBlob.pbData != IntPtr.Zero)
                {
                    ZeroUnmanaged(outBlob.pbData, outBlob.cbData);
                    LocalFree(outBlob.pbData);
                }
            }
        }

        private static DataBlob AllocBlob(byte[] bytes)
        {
            var blob = new DataBlob { cbData = bytes.Length };
            if (bytes.Length > 0)
            {
                blob.pbData = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
            }
            return blob;
        }

        private static void FreeHGlobalBlob(ref DataBlob blob, bool zeroMemory)
        {
            if (blob.pbData == IntPtr.Zero) return;
            if (zeroMemory && blob.cbData > 0) ZeroUnmanaged(blob.pbData, blob.cbData);
            Marshal.FreeHGlobal(blob.pbData);
            blob.pbData = IntPtr.Zero;
            blob.cbData = 0;
        }

        private static void ZeroUnmanaged(IntPtr ptr, int length)
        {
            for (int i = 0; i < length; i++) Marshal.WriteByte(ptr, i, 0);
        }
    }
#endif

#if UNITY_IOS && !UNITY_EDITOR
    /// <summary>iOS Keychain 凭据存储(Assets/Plugins/iOS/MmorpgKeychainStore.mm)。</summary>
    internal sealed class IosKeychainSecureStore : ISecureStringStore
    {
        private const string ServiceName = "com.mmorpg.client.auth";

        [DllImport("__Internal")]
        private static extern int MmorpgKeychain_Save(string service, string account, string secret);

        [DllImport("__Internal")]
        private static extern IntPtr MmorpgKeychain_Load(string service, string account);

        [DllImport("__Internal")]
        private static extern void MmorpgKeychain_Delete(string service, string account);

        [DllImport("__Internal")]
        private static extern void MmorpgKeychain_Free(IntPtr ptr);

        public bool IsAvailable => true;

        public bool TrySave(string key, string secret)
        {
            if (!SecureStore.IsValidKey(key) || string.IsNullOrEmpty(secret)) return false;
            return MmorpgKeychain_Save(ServiceName, key, secret) != 0;
        }

        public bool TryLoad(string key, out string secret)
        {
            secret = string.Empty;
            if (!SecureStore.IsValidKey(key)) return false;
            IntPtr ptr = MmorpgKeychain_Load(ServiceName, key);
            if (ptr == IntPtr.Zero) return false;
            try
            {
                secret = Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
                return !string.IsNullOrEmpty(secret);
            }
            finally
            {
                MmorpgKeychain_Free(ptr);
            }
        }

        public void Delete(string key)
        {
            if (!SecureStore.IsValidKey(key)) return;
            MmorpgKeychain_Delete(ServiceName, key);
        }
    }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>Android KeyStore AES-256-GCM 凭据存储(Assets/Plugins/Android/MmorpgSecureStore.androidlib)。</summary>
    internal sealed class AndroidKeystoreSecureStore : ISecureStringStore
    {
        private const string BridgeClass = "com.mmorpg.secure.SecureStoreBridge";

        public bool IsAvailable => true;

        public bool TrySave(string key, string secret)
        {
            if (!SecureStore.IsValidKey(key) || string.IsNullOrEmpty(secret)) return false;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var bridge = new AndroidJavaClass(BridgeClass);
                return bridge.CallStatic<bool>("save", activity, key, secret);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[secure-store] android keystore save failed: {ex.GetType().Name}");
                return false;
            }
        }

        public bool TryLoad(string key, out string secret)
        {
            secret = string.Empty;
            if (!SecureStore.IsValidKey(key)) return false;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var bridge = new AndroidJavaClass(BridgeClass);
                string loaded = bridge.CallStatic<string>("load", activity, key);
                if (string.IsNullOrEmpty(loaded)) return false;
                secret = loaded;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[secure-store] android keystore load failed: {ex.GetType().Name}");
                return false;
            }
        }

        public void Delete(string key)
        {
            if (!SecureStore.IsValidKey(key)) return;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var bridge = new AndroidJavaClass(BridgeClass);
                bridge.CallStatic("delete", activity, key);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[secure-store] android keystore delete failed: {ex.GetType().Name}");
            }
        }
    }
#endif
}
