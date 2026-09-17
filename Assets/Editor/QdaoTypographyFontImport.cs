using System.IO;
using MmorpgClient.UI.Ugui;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.UI.EditorTools
{
    public static class QdaoTypographyFontImport
    {
        public const string BodyAssetPath = "Assets/Resources/Fonts/QdaoBody SDF.asset";

        [MenuItem("MMORPG/UI/Ensure readable Chinese body font")]
        public static void EnsureBodyFontAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyAssetPath) != null) return;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/TeamNotoSansSC.otf");
            if (source == null) throw new FileNotFoundException("Bundled Chinese font is missing.");
            var font = QdaoUguiTypography.CreateBodyFont(source);
            // Prewarm common controls; player names and server content use the dynamic atlas.
            font.TryAddCharacters("账号密码登录取消确认返回创建角色帮会成员排行商店活动任务等级职业性别服务器请输入道友天地五行金木水火土男女0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz，。！？：、（）·+-/%");
            AssetDatabase.CreateAsset(font, BodyAssetPath);
            font.material.name = "QdaoBody SDF Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures)
                if (atlas != null && !AssetDatabase.Contains(atlas))
                {
                    atlas.name = "QdaoBody SDF Atlas";
                    AssetDatabase.AddObjectToAsset(atlas, font);
                }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            QdaoUguiTypography.ResetRuntimeCache();
        }
    }
}
