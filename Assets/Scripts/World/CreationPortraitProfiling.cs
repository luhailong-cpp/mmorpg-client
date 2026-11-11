using Unity.Profiling;

namespace MmorpgClient.World
{
    /// <summary>
    /// 创角页的性能埋点，供 Profiler 和测速驱动（DevRoleCreatePerfProbe）按名字读取。
    /// 名字是测速驱动的读取契约，改名要同步改驱动；非开发版里这些调用几乎没有开销。
    /// </summary>
    public static class CreationPortraitProfiling
    {
        public const string OpenName = "RoleCreate.Open";
        public const string RefreshChoicesName = "RoleCreate.RefreshChoices";
        public const string RefreshSelectionName = "RoleCreate.RefreshSelection";
        public const string ResolveName = "RoleCreate.ResolveAppearance";
        public const string LoadTextureName = "RoleCreate.LoadPortraitTexture";
        public const string PreloadName = "RoleCreate.PreloadNeighbour";
        public const string ReleaseName = "RoleCreate.ReleasePortrait";

        /// <summary>进入创角页（含候选刷新和当前人物显示）。</summary>
        public static readonly ProfilerMarker Open = new(ProfilerCategory.Scripts, OpenName);
        /// <summary>重新解析全部可创建人物、重排候选环。</summary>
        public static readonly ProfilerMarker RefreshChoices = new(ProfilerCategory.Scripts, RefreshChoicesName);
        /// <summary>按当前选择刷新高亮、窗口和预览，包括当前人物头像的同步加载。</summary>
        public static readonly ProfilerMarker RefreshSelection = new(ProfilerCategory.Scripts, RefreshSelectionName);
        /// <summary>单个人物的创角外观解析（读元数据、核对索引，不加载纹理）。</summary>
        public static readonly ProfilerMarker Resolve = new(ProfilerCategory.Scripts, ResolveName);
        /// <summary>一次创角头像纹理的同步加载。</summary>
        public static readonly ProfilerMarker LoadTexture = new(ProfilerCategory.Scripts, LoadTextureName);
        /// <summary>后续帧补载一位左右邻居。</summary>
        public static readonly ProfilerMarker Preload = new(ProfilerCategory.Scripts, PreloadName);
        /// <summary>归还一份创角头像租约（可能销毁 Sprite 并卸载纹理）。</summary>
        public static readonly ProfilerMarker Release = new(ProfilerCategory.Scripts, ReleaseName);
    }
}
