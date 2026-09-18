using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>Retains an HD direction while a UI Image/afterimage still displays one of its sprites.</summary>
    [DisallowMultipleComponent]
    public sealed class QdaoHdSpriteLeaseOwner : MonoBehaviour
    {
        private QdaoHdResources.Lease _lease;
        private QdaoHdResources.Lease _pending;
        private bool _rejectionNotified;
        public System.Action OnAppearanceRejected { get; set; }
        /// <summary>Keep a newly returned sprite alive without retiring the image that is still displayed.</summary>
        public void PrepareSprite(Sprite sprite)
        {
            var next = QdaoHdResources.RetainSprite(sprite);
            var previous = _pending; _pending = next; previous?.Dispose();
        }
        public void BindSprite(Sprite sprite)
        {
            var next = QdaoHdResources.RetainSprite(sprite);
            var previous = _lease;
            _lease = next;
            _rejectionNotified = false;
            previous?.Dispose();
            var pending = _pending; _pending = null; pending?.Dispose();
        }
        // Call only after clearing/replacing all images this owner protects.
        public void Clear()
        {
            var previous = _lease; _lease = null;
            var pending = _pending; _pending = null;
            previous?.Dispose(); pending?.Dispose();
        }
        private void Update()
        {
            if (_rejectionNotified || _lease == null || OnAppearanceRejected == null ||
                !QdaoCharacterCatalog.IsRejectedHdAppearance(_lease.Appearance)) return;
            _rejectionNotified = true;
            OnAppearanceRejected();
        }
        private void OnDestroy() { OnAppearanceRejected = null; Clear(); }
    }
}
