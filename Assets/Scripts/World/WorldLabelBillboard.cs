using UnityEngine;

namespace MmorpgClient.World
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Keeps an actor's floating name label readable from the world camera
    /// and, as in classic 2.5D towns, parked just below the actor's feet.
    /// The label is a child of the actor root, which is the feet point and
    /// rotates with the facing; this component overrides the child's world
    /// rotation/position every frame so neither matters.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldLabelBillboard : MonoBehaviour
    {
        /// <summary>Screen-down offset from the feet, in world units.</summary>
        public float OffsetBelowFeet = 0.45f;

        private Renderer _renderer;

        public static WorldLabelBillboard Attach(GameObject label, float offsetBelowFeet = 0.45f)
        {
            if (label == null) return null;
            var billboard = label.GetComponent<WorldLabelBillboard>();
            if (billboard == null) billboard = label.AddComponent<WorldLabelBillboard>();
            billboard.OffsetBelowFeet = offsetBelowFeet;
            return billboard;
        }

        private void Awake() => _renderer = GetComponent<Renderer>();

        private void LateUpdate()
        {
            var worldCamera = Camera.main;
            if (worldCamera == null || transform.parent == null) return;

            var cameraTransform = worldCamera.transform;
            transform.rotation = cameraTransform.rotation;
            transform.position = transform.parent.position
                                 - cameraTransform.up * OffsetBelowFeet
                                 - cameraTransform.forward * 0.2f; // keep clear of the ground/sprite

            if (_renderer != null)
                _renderer.sortingOrder =
                    QdaoBoySpriteAnimator.WorldSortingOrder(transform.parent.position, worldCamera) + 1;
        }
    }
}
