using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace MmorpgClient.Table
{
    /// <summary>
    /// 客户端配置表启动加载入口（统一驱动 <see cref="TableLoader"/> 加载二进制 <c>.pb</c> 表快照）。
    /// 桌面/Editor 走本地 <see cref="Application.streamingAssetsPath"/> 同步加载；
    /// WebGL 与 Android 的 StreamingAssets 不可走 <c>System.IO</c>，按单线程协程经 <see cref="UnityWebRequest"/> 分批拉取字节后一次性加载。
    /// </summary>
    public static class ClientTableBootstrap
    {
        public const string StreamingTablesFolder = "Tables";
        private const int MaxConcurrentWebRequests = 8;

        private static bool _loading;

        public static bool IsLoaded { get; private set; }
        public static string LastError { get; private set; }
        public static event Action OnLoaded;

        public static string DefaultStreamingTablesDir
            => Path.Combine(Application.streamingAssetsPath, StreamingTablesFolder);

        /// <summary>
        /// 幂等触发配置表加载。已加载或加载中时直接返回。
        /// 桌面/Editor 下在调用当帧同步完成；WebGL/Android 下在 <paramref name="coroutineHost"/> 上启动协程异步完成。
        /// </summary>
        public static void EnsureLoaded(MonoBehaviour coroutineHost)
        {
            if (IsLoaded || _loading) return;

#if (UNITY_WEBGL || UNITY_ANDROID) && !UNITY_EDITOR
            if (coroutineHost == null)
            {
                LastError = "ClientTableBootstrap requires a MonoBehaviour host on WebGL/Android.";
                Debug.LogWarning("[ClientTableBootstrap] " + LastError);
                return;
            }
            string baseUrl = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + StreamingTablesFolder;
            coroutineHost.StartCoroutine(LoadFromUrlPrefixCoroutine(baseUrl));
#else
            TryLoadFromDirectory(DefaultStreamingTablesDir);
#endif
        }

        /// <summary>从本地目录同步加载全部二进制配置表（失败只记日志与 <see cref="LastError"/>，不中断启动）。</summary>
        public static bool TryLoadFromDirectory(string configDir)
        {
            if (string.IsNullOrEmpty(configDir) || !Directory.Exists(configDir))
            {
                LastError = $"Config table directory not found: {configDir}";
                Debug.LogWarning("[ClientTableBootstrap] " + LastError);
                return false;
            }

            try
            {
                TableLoader.LoadTables(configDir, useBinary: true);
                IsLoaded = true;
                LastError = null;
                Debug.Log($"[ClientTableBootstrap] Loaded {TableLoader.TableCount} binary tables from {configDir}");
                OnLoaded?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.LogError($"[ClientTableBootstrap] Failed to load tables from {configDir}: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 通过 <see cref="UnityWebRequest"/> 从 URL 前缀分批拉取全部 <c>.pb</c> 表并在主线程一次性提交给 <see cref="TableLoader"/>。
        /// 兼容 WebGL（无线程/无本地 IO）、Android APK StreamingAssets 以及远端热更 CDN。
        /// </summary>
        public static IEnumerator LoadFromUrlPrefixCoroutine(string baseUrl)
        {
            if (_loading) yield break;
            _loading = true;
            string prefix = (baseUrl ?? string.Empty).TrimEnd('/', '\\');
            string[] fileNames = TableLoader.TableFileNames(useBinary: true);
            var bytesByName = new Dictionary<string, byte[]>(fileNames.Length, StringComparer.OrdinalIgnoreCase);
            var inFlight = new List<(string Name, UnityWebRequest Request)>(MaxConcurrentWebRequests);
            string fetchError = null;

            try
            {
                int index = 0;
                while (index < fileNames.Length && fetchError == null)
                {
                    inFlight.Clear();
                    int batchEnd = Math.Min(index + MaxConcurrentWebRequests, fileNames.Length);
                    for (; index < batchEnd; index++)
                    {
                        string name = fileNames[index];
                        var req = UnityWebRequest.Get(prefix + "/" + name);
                        req.SendWebRequest();
                        inFlight.Add((name, req));
                    }

                    for (int i = 0; i < inFlight.Count; i++)
                    {
                        var item = inFlight[i];
                        while (!item.Request.isDone) yield return null;

                        if (item.Request.result != UnityWebRequest.Result.Success)
                        {
                            fetchError = $"{item.Name}: {item.Request.error} (HTTP {item.Request.responseCode})";
                        }
                        else if (fetchError == null)
                        {
                            bytesByName[item.Name] = item.Request.downloadHandler.data ?? Array.Empty<byte>();
                        }
                    }

                    for (int i = 0; i < inFlight.Count; i++) inFlight[i].Request.Dispose();
                    inFlight.Clear();
                }

                if (fetchError != null)
                {
                    LastError = fetchError;
                    Debug.LogError("[ClientTableBootstrap] Failed to fetch tables: " + fetchError);
                    yield break;
                }

                try
                {
                    TableLoader.LoadTables(name => bytesByName.TryGetValue(name, out var data) ? data : null, useBinary: true);
                    IsLoaded = true;
                    LastError = null;
                    Debug.Log($"[ClientTableBootstrap] Loaded {TableLoader.TableCount} binary tables from {prefix}");
                    OnLoaded?.Invoke();
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Debug.LogError($"[ClientTableBootstrap] Failed to parse fetched tables: {ex}");
                }
            }
            finally
            {
                for (int i = 0; i < inFlight.Count; i++) inFlight[i].Request.Dispose();
                inFlight.Clear();
                _loading = false;
            }
        }

        /// <summary>复位静态加载状态与全部表管理器快照，供 EditMode 单测隔离清理。</summary>
        public static void ResetForTests()
        {
            _loading = false;
            IsLoaded = false;
            LastError = null;
            TableLoader.LoadTables(_ => Array.Empty<byte>(), useBinary: true);
        }
    }
}
