using MmorpgClient.World;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>A pooled afterimage keeps its archived frame alive independently of its actor.</summary>
    public sealed class BattleActionSpriteLeaseOwner : MonoBehaviour
    {
        private QdaoActionResources.Lease _lease;

        public void BindSprite(Sprite sprite)
        {
            var next = QdaoActionResources.Retain(sprite);
            var previous = _lease;
            _lease = next;
            previous?.Dispose();
        }

        public void Clear()
        {
            var previous = _lease;
            _lease = null;
            previous?.Dispose();
        }

        private void OnDestroy() => Clear();
    }
}
