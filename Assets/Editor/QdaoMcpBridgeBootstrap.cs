#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.UI.EditorTools
{
    /// <summary>
    /// Keeps the official Unity MCP bridge available after the UI builder triggers a domain reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class QdaoMcpBridgeBootstrap
    {
        static QdaoMcpBridgeBootstrap()
        {
            EditorApplication.delayCall += EnsureBridgeRunning;
        }

        private static void EnsureBridgeRunning()
        {
            // Reflection keeps this editor-only helper harmless on machines where
            // the optional official Unity AI/MCP package is not installed.
            var bridgeType = Type.GetType(
                "Unity.AI.MCP.Editor.UnityMCPBridge, Unity.AI.MCP.Editor",
                throwOnError: false);
            if (bridgeType == null)
                return;

            try
            {
                var flags = BindingFlags.Public | BindingFlags.Static;
                var isRunning = bridgeType.GetProperty("IsRunning", flags);
                if (isRunning?.GetValue(null) is true)
                    return;

                bridgeType.GetMethod("Start", flags)?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QdaoMcpBridgeBootstrap] Could not start Unity MCP: {ex.Message}");
            }
        }
    }
}
#endif
