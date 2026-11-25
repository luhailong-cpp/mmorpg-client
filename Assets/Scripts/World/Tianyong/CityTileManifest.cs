using System;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    /// <summary>Explicit JSON fields; Unity Rect uses engine-private field names in JsonUtility.</summary>
    [Serializable]
    public struct CityTileWorldRect
    {
        public float x;
        public float y;
        public float width;
        public float height;
        public static implicit operator Rect(CityTileWorldRect value)
            => new(value.x, value.y, value.width, value.height);
        public static implicit operator CityTileWorldRect(Rect value)
            => new() { x = value.x, y = value.y, width = value.width, height = value.height };
    }
}
