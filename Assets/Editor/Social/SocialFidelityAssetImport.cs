#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class SocialFidelityAssetImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if(!assetPath.StartsWith("Assets/Resources/UI/Ugui/SocialV1/Fidelity20261016/"))return;
        var importer=(TextureImporter)assetImporter;
        importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Single;
        importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;
        importer.spritePixelsPerUnit=100;
        var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
        settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);
        string key=System.IO.Path.GetFileNameWithoutExtension(assetPath);
        importer.spriteBorder=key switch
        {
            "button_normal" or "button_selected" => new Vector4(20,10,20,10),
            "send_button" => new Vector4(30,20,30,20),
            "input_field" => new Vector4(32,20,32,20),
            "message_bubble" => new Vector4(17,9,17,9),
            "group_row_normal" or "group_row_selected" or "group_row_lotus" or "rumor_row" or "rumor_row_ended" => new Vector4(12,12,12,12),
            _ => Vector4.zero
        };
    }
}
#endif
