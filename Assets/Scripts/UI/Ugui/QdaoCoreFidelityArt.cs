using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace MmorpgClient.UI.Ugui
{
    internal static class QdaoCoreFidelityArt
    {
        public const string Root = "UI/Ugui/CoreFidelity20261016/";
        public static readonly Color Ink = QdaoUguiTheme.Html("#244C3E"), Ivory = QdaoUguiTheme.Html("#FFF3D3");
        public static Sprite Load(string key)
        {
            // Existing approved transparent controls prevent a cropped scenery rectangle
            // around new utility actions that live over the sky or inside a dialog.
            if (key == "clean_tab") return QdaoRefreshArt.Load("tab_normal");
            if (key == "clean_search") return QdaoRefreshArt.Load("search_normal");
            if (key == "clean_primary") return QdaoRefreshArt.Load("primary_button_normal");
            string alias = key switch {
                "server_card_wide_normal" => "server_card_normal", "server_card_wide_selected" => "server_card_selected",
                "server_card_wide_disabled" => "server_card_disabled", "list_row_normal" => "server_category_normal",
                "list_row_selected" => "server_category_selected", "list_row_disabled" => "server_category_normal",
                "search_normal" => "server_search", "tab_normal" => "server_back", "tab_selected" => "server_category_selected",
                "primary_button_normal" => "server_enter", "primary_button_selected" => "server_enter", _ => key };
            return Resources.Load<Sprite>(Root + alias) ?? QdaoRefreshArt.Load(key);
        }
        public static void Skin(Image image, string key)
        {
            if (image == null) return;
            image.sprite = Load(key); image.color = Color.white;
            image.type = image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = false;
            image.pixelsPerUnitMultiplier = Mathf.Max(.01f, image.sprite.rect.height / Mathf.Max(1, image.rectTransform.rect.height));
        }
        public static Image Panel(string name, UnityEngine.Transform parent, float x, float y, float w, float h, string key = "content_panel")
        { var image = QdaoUguiFactory.CreateImage(name,parent,x,y,w,h,Load(key)); Skin(image,key); return image; }
        public static Button Button(string name, UnityEngine.Transform parent,float x,float y,float w,float h,string key,out Image image)
        {
            var button=QdaoUguiFactory.CreateArtButton(name,parent,x,y,w,h,Load(key),out image);Skin(image,key);
            button.navigation=new Navigation { mode=Navigation.Mode.Automatic };
            var colors=button.colors;colors.highlightedColor=new Color(1,.97f,.84f);colors.selectedColor=colors.highlightedColor;button.colors=colors;
            return button;
        }
        public static void ApplyText(TMP_Text text)
        {
            if (text == null) return;
            text.richText = false;
            bool server = false;
            for (var parent = text.transform.parent; parent != null; parent = parent.parent)
                if (parent.name == "ServerPage") { server = true; break; }
            if (server)
            {
                QdaoUguiTypography.ApplyBody(text);
                if (text.name.StartsWith("ServerName_") || text.name.StartsWith("CategoryText_") ||
                    text.name.StartsWith("TopTabText_") || text.name == "ServerTitle" ||
                    text.name.EndsWith("ButtonText") || text.name == "ServerRecommendedText")
                { text.fontStyle = FontStyles.Bold; text.fontWeight = FontWeight.Bold; }
                if (text.name.StartsWith("ServerName_") || text.name.StartsWith("ServerStatus_") ||
                    text.name == "SelectedServerText")
                {
                    // The body font's line metrics are taller than SimKai's. Empty
                    // data-bound labels still need a real line box before values arrive.
                    float preferred = text.GetPreferredValues("国", Mathf.Infinity, Mathf.Infinity).y;
                    if (preferred > text.rectTransform.rect.height)
                        text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                            Mathf.Ceil(preferred + 2f));
                }
                return;
            }
            text.font = QdaoUguiTheme.ResolveFont();
            string action = text.transform.parent != null ? text.transform.parent.name : string.Empty;
            if (text.name == "Title" || text.name == "EnterGameText" ||
                action == "EnterSanctuary" || action == "ConfirmCreate")
            {
                text.fontStyle = FontStyles.Bold; text.fontWeight = FontWeight.Bold;
                text.color = QdaoUguiTheme.Html("#E5CC80");
                ApplyGoldOutline(text);
            }
            else if (text.name == "Name" || text.name == "PreviewTitle")
            { text.fontStyle = FontStyles.Bold; text.fontWeight = FontWeight.Bold; }
        }
        private static void ApplyGoldOutline(TMP_Text text)
        {
            // Inactive freshly baked labels may not have TMP's m_sharedMaterial yet.
            // Begin from the font asset's valid material instead of outline setters,
            // which clone m_sharedMaterial without checking that it exists.
            var source = text.font != null ? text.font.material : null;
            if (source == null) return;
            const string resource = "UI/Ugui/CoreFidelity20261016/CoreGoldText";
            Material outline = Resources.Load<Material>(resource);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                const string path = "Assets/Resources/" + resource + ".mat";
                outline = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                if (outline == null)
                {
                    outline = new Material(source) { name = "CoreGoldText" };
                    outline.EnableKeyword("OUTLINE_ON");
                    outline.SetFloat("_OutlineWidth", .10f);
                    outline.SetColor("_OutlineColor", QdaoUguiTheme.Html("#28412B"));
                    UnityEditor.AssetDatabase.CreateAsset(outline, path);
                }
                // A persistent dedicated preset survives SaveAsPrefabAsset + reload.
                // Never serialize a temporary material or edit the font's own material.
                text.fontSharedMaterial = outline;
                return;
            }
#endif
            if (outline != null)
            {
                text.fontSharedMaterial = outline;
                return;
            }
            text.fontSharedMaterial = source;
            var instance = text.fontMaterial; // TMP owns and disposes this runtime clone.
            if (instance == null) return;
            instance.EnableKeyword("OUTLINE_ON");
            instance.SetFloat("_OutlineWidth", .10f);
            instance.SetColor("_OutlineColor", QdaoUguiTheme.Html("#28412B"));
            text.UpdateMeshPadding();
        }

        public static Sprite LoadCharacterPortrait(string id)
        {
            // Same-identity standalone portrait only. Unknown ids still remain unknown;
            // this UI path does not approve or activate any animation package.
            return id == "04_mountain_guardian_boy" ? Resources.Load<Sprite>(Root + "portraits/04_mountain_guardian_boy") : null;
        }
    }
    internal static class RoleFidelityArt
    {
        private static string Key(string key) => key switch {
            "server_card_medium_normal" => "role_card_normal", "server_card_medium_selected" => "role_card_selected",
            "list_row_normal" => "role_card_normal", "list_row_selected" => "role_card_selected",
            "primary_button_normal" => "role_enter", "primary_button_selected" => "role_enter",
            "tab_normal" => "role_back", _ => key };
        public static Sprite Load(string key) => QdaoCoreFidelityArt.Load(Key(key));
        public static void Skin(Image image,string key) => QdaoCoreFidelityArt.Skin(image,Key(key));
        public static Image Panel(string name,UnityEngine.Transform parent,float x,float y,float w,float h,string key="content_panel")
            => QdaoCoreFidelityArt.Panel(name,parent,x,y,w,h,Key(key));
        public static Button Button(string name,UnityEngine.Transform parent,float x,float y,float w,float h,string key,out Image image)
            => QdaoCoreFidelityArt.Button(name,parent,x,y,w,h,Key(key),out image);
    }
}
