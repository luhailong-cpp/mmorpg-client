using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>Navigation queries borrowed from the current map's owner.</summary>
    public interface IMapNavigation
    {
        bool IsAuthoredFlat { get; }
        bool IsWalkable(Vector3 world);
        bool TryFindNearestWalkable(Vector3 world, out Vector3 result);
        Vector3 ClampXZ(Vector3 world, float margin = 2f);
        List<Vector3> FindPath(Vector3 start, Vector3 target);
        /// <summary>
        /// 贴近寻路：目标在阻挡、越界或孤岛时，在起点连通域内寻找离目标欧氏距离最近的可走点并规划合法路径。
        /// </summary>
        List<Vector3> FindApproachPath(Vector3 start, Vector3 target);
        Vector3 ResolveFallbackSpawn(Vector3 initialFeet, Vector3? requested);
        bool IsSegmentWalkable(Vector3 from, Vector3 to);
    }
}
