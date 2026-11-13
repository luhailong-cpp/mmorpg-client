using MmorpgClient.World.NavigationAuthoring;
using UnityEditor;

namespace MmorpgClient.Editor.NavigationAuthoring
{
    /// <summary>Prepared image-only authoring canvases. These entries never manufacture navigation.</summary>
    public static class FestivalNavMeshDraftPresets
    {
        public const string Root = "Assets/MapNavigationDrafts/Festival20261111/";
        public static readonly string[] Keys = { "26000-mid-autumn", "26008-spring", "26008-mid-autumn" };
        public static readonly string[] Labels = { "26000 中秋", "26008 春节", "26008 中秋" };

        public static NavMeshAuthoringDraft Load(int index)
            => index < 0 || index >= Keys.Length ? null : AssetDatabase.LoadAssetAtPath<NavMeshAuthoringDraft>(Root + Keys[index] + ".asset");

        public static NavMeshAuthoringDraft FirstReady()
        {
            for (int i = 0; i < Keys.Length; i++) { var draft = Load(i); if (draft != null) return draft; }
            return null;
        }
    }
}
