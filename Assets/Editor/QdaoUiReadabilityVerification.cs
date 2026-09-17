using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MmorpgClient.UI.Ugui;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MmorpgClient.UI.EditorTools
{
    /// <summary>Offline checks and real Unity renders of the shared typography change.</summary>
    public static class QdaoUiReadabilityVerification
    {
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Readability verification requires Edit Mode.");
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../output/unity-ui-readability-01a0ad5a"));
            Directory.CreateDirectory(output);
            QdaoUiTextureQuality.ApplyAll();
            QdaoTypographyFontImport.EnsureBodyFontAsset();
            QdaoUguiTheme.ResetRuntimeCaches();
            var body = QdaoUguiTypography.ResolveBodyFont();
            if (!AssetDatabase.Contains(body) || !body.isMultiAtlasTexturesEnabled)
                throw new InvalidOperationException("Body font must be persistent and support dynamic Chinese glyphs.");
            string missing;
            if (!body.TryAddCharacters("创建角色账号密码登录取消帮会成员修行天地五行请选择0123456789", out missing))
                throw new InvalidOperationException("Missing UI glyphs: " + missing);

            // Rebuild only the UI prefab; leave Bootstrap and the artwork import settings intact.
            typeof(QdaoUguiBuilder).GetMethod("BuildPrefab", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            AssetDatabase.SaveAssets();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/Ugui/Prefabs/QdaoServerSelect.prefab");
            var preview = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var view = preview.GetComponent<QdaoServerSelectView>();
                view.PrepareForPreview();
                foreach (var input in preview.GetComponentsInChildren<TMP_InputField>(true))
                    if (input.textComponent.font != body || ((TMP_Text)input.placeholder).font != body)
                        throw new InvalidOperationException("Input font changed during refresh: " + input.name);
                var chooser = preview.GetComponentsInChildren<TMP_Text>(true).Single(x => x.name == "ChooseServerActionText");
                if (chooser.font != body || (chooser.fontStyle & FontStyles.Bold) == 0)
                    throw new InvalidOperationException("Nested server selector lost its button typography.");
            }
            finally { UnityEngine.Object.DestroyImmediate(preview); }

            if (Application.isBatchMode)
                EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            ClientUiRefreshVerification.OutputDirectory = Path.Combine(output, "role-login" );
            var guildVerification = Type.GetType("GuildUiVerification, MmorpgClient.Guild.Editor", true);
            guildVerification.GetField("OutputDirectory", BindingFlags.Static | BindingFlags.Public).SetValue(null, Path.Combine(output, "guild"));
            var guildCapture = guildVerification.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (int width in new[] { 1280, 1920, 2560, 3840 })
            {
                int height = width == 1280 ? 720 : width == 3840 ? 2160 : 1080;
                ClientUiRefreshVerification.Capture(width, height);
                guildCapture.Invoke(null, new object[] { width, height });
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(output, "verification.txt"),
                "PASS: persistent Chinese font and glyph coverage; input and nested button styling survives prefab refresh.\n" +
                "Rendered production UI offline at 1280x720, 1920x1080, 2560x1080 and 3840x2160.\n" +
                "PNG files: " + Directory.GetFiles(output, "*.png", SearchOption.AllDirectories).Length + "\n");
            Debug.Log("UI_READABILITY_VERIFIED|" + output);
        }
    }
}
