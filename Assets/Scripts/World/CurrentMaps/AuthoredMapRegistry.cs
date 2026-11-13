using System;
using UnityEngine;
using UnityEngine.AI;

namespace MmorpgClient.World.CurrentMaps
{
    /// <summary>显式启用的本地手绘地图；不写入正式地图交付目录。</summary>
    public static class AuthoredMapRegistry
    {
        public const string Argument = "-authoredFestivalLocalTest";
        internal const string ResourcePath = "World/AuthoredFestival20261112/registry";
        public static bool IsEnabled => (Application.isEditor || Debug.isDebugBuild) &&
            Array.Exists(Environment.GetCommandLineArgs(), arg => string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase));

        internal static CurrentMapDefinition Find(uint id)
        {
            if (!IsEnabled || (id != 26000 && id != 26008)) return null;
            var registry = Resources.Load<AuthoredFestivalAssets>(ResourcePath);
            if (registry == null) throw new InvalidOperationException("手绘地图运行资源未生成，请先运行编辑器交付生成器。");
            foreach (var item in registry.maps ?? Array.Empty<AuthoredFestivalAssets.Map>())
                if (item != null && item.sceneConfigId == id)
                {
                    item.Validate();
                    return new CurrentMapDefinition(item);
                }
            throw new InvalidOperationException("手绘地图运行资源缺少场景：" + id);
        }
    }

}
