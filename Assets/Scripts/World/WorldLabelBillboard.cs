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
        /// <summary>Screen gap between the contact shadow's lower edge and the nameplate's em box.</summary>
        public const float DefaultGapBelowShadow = 0.1f;

        /// <summary>
        /// Default screen-down offset from the feet to the nameplate centre, in
        /// world units, derived from the sprite walker's contact shadow so the
        /// two stay in sync: the shadow ellipse is centred
        /// <see cref="QdaoBoySpriteAnimator.ShadowScreenDownOffset"/> below the
        /// feet and is <see cref="QdaoBoySpriteAnimator.ShadowWidth"/> x
        /// <see cref="Tianyong.TianyongMapConfig.DefaultGroundDiscAspect"/> tall,
        /// so its lower edge is 0.15 + 3.2 * 0.66 / 2 = ~1.21 u under the feet;
        /// add half the nameplate em height (~0.63 u) and a small gap = ~1.93 u.
        /// A map asset that overrides groundDiscAspect shifts the shadow edge by
        /// a few hundredths of a unit; pass an explicit offset to Attach if that
        /// ever matters.
        /// </summary>
        public const float DefaultOffsetBelowFeet =
            QdaoBoySpriteAnimator.ShadowScreenDownOffset
            + QdaoBoySpriteAnimator.ShadowWidth * Tianyong.TianyongMapConfig.DefaultGroundDiscAspect * 0.5f
            + WorldNameplate.WorldEmHeight * 0.5f
            + DefaultGapBelowShadow;

        /// <summary>Screen-down offset from the feet, in world units.</summary>
        public float OffsetBelowFeet = DefaultOffsetBelowFeet;

        /// <summary>
        /// Constant added to a label's depth order so every nameplate draws
        /// above every ground object, whatever their relative depth.
        /// <para>
        /// A label hangs below its owner's feet on screen but is sorted by the
        /// feet, so anything sorted by its own (smaller) depth at that spot
        /// outranks it. A click ring dropped just south of the actor did
        /// exactly that and painted over the middle character of the name
        /// (2026-09-08, frame 38_04_click_rapid_a). Depth ordering is the
        /// right rule for things lying on the ground and the wrong rule for
        /// text about an actor, so labels get their own band instead.
        /// </para>
        /// <para>
        /// 8000 clears the whole painting: <see cref="QdaoBoySpriteAnimator.WorldSortingOrder"/>
        /// is round(-depth * 10) and the map spans 300 u, so world orders stay
        /// inside +/-3000. Labels therefore also draw over actor sprites that
        /// stand in front of them, which is the usual MMO convention (a name
        /// is never worth hiding) and the deliberate trade for never losing a
        /// name under ground feedback. Labels keep their mutual depth order.
        /// </para>
        /// </summary>
        public const int LabelSortingBoost = 8000;

        private Renderer _renderer;
        // TMP labels must be sorted through TextMeshPro.sortingOrder: TMP spawns
        // child sub-mesh renderers for fallback-font glyphs / sprites and only
        // copies the parent's order when they are created, so writing the
        // MeshRenderer directly would leave those pieces at a stale depth.
        private TMPro.TextMeshPro _tmp;
        private Vector3 _normalScale = Vector3.one;

        public static WorldLabelBillboard Attach(GameObject label, float offsetBelowFeet = DefaultOffsetBelowFeet)
        {
            if (label == null) return null;
            var billboard = label.GetComponent<WorldLabelBillboard>();
            if (billboard == null) billboard = label.AddComponent<WorldLabelBillboard>();
            billboard.OffsetBelowFeet = offsetBelowFeet;
            return billboard;
        }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _tmp = GetComponent<TMPro.TextMeshPro>();
            _normalScale = transform.localScale;
        }

        private void LateUpdate()
            => RefreshForCamera(Camera.main);

        internal void RefreshForCamera(Camera worldCamera)
        {
            if (worldCamera == null || transform.parent == null) return;

            var cameraTransform = worldCamera.transform;
            float weight = 0f;
            float closeScale = 1f;
            float closeOffset = OffsetBelowFeet;
            var body = transform.parent.Find("sprite")?.GetComponent<SpriteRenderer>();
            if (body?.sprite != null && worldCamera.orthographic)
            {
                float frameHeight = body.sprite.bounds.size.y * Mathf.Abs(body.transform.lossyScale.y);
                weight = Tianyong.TianyongCameraController.CloseUpFramingWeight(worldCamera.orthographicSize, frameHeight);
                // At nearest zoom keep a readable ~40 px name instead of a 150 px world-space label.
                // Its small backdrop fits the authored transparent space below the unchanged feet pivot.
                closeScale = Mathf.Min(.3f, 40f * 2f * worldCamera.orthographicSize /
                    (Mathf.Max(1, worldCamera.pixelHeight) * WorldNameplate.WorldEmHeight));
                float halfBox = (WorldNameplate.WorldEmHeight * .5f + WorldNameplate.BackdropPaddingY) * closeScale;
                float labelBottom = -halfBox, labelTop = halfBox;
                var backdrop = transform.Find("NameplateBackdrop")?.GetComponent<SpriteRenderer>();
                if (backdrop?.sprite != null)
                {
                    var bounds = backdrop.sprite.bounds;
                    labelBottom = (backdrop.transform.localPosition.y + bounds.min.y * backdrop.transform.localScale.y) * closeScale;
                    labelTop = (backdrop.transform.localPosition.y + bounds.max.y * backdrop.transform.localScale.y) * closeScale;
                }
                closeOffset = Mathf.Max(labelTop + .08f,
                    -body.sprite.bounds.min.y * Mathf.Abs(body.transform.lossyScale.y) + labelBottom - .12f);
            }
            transform.localScale = _normalScale * Mathf.Lerp(1f, closeScale, weight);
            transform.rotation = cameraTransform.rotation;
            transform.position = transform.parent.position
                                 - cameraTransform.up * Mathf.Lerp(OffsetBelowFeet, closeOffset, weight)
                                 - cameraTransform.forward * 0.2f; // keep clear of the ground/sprite

            var order = QdaoBoySpriteAnimator.WorldSortingOrder(transform.parent.position, worldCamera)
                        + LabelSortingBoost;
            if (_tmp != null)
                _tmp.sortingOrder = order; // also propagates to TMP sub-mesh renderers
            else if (_renderer != null)
                _renderer.sortingOrder = order; // non-TMP label (legacy TextMesh etc.)
        }
    }
}
