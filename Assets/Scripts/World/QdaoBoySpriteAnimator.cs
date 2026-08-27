using UnityEngine;

namespace MmorpgClient.World
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Billboarded eight-direction walk animation for player actors, fed by the
    /// qdao headband-boy frame strips under
    /// Resources/World/Characters/QdaoHeadbandBoy (one 8-frame 4096x512 strip per
    /// direction, N/NE/E/SE/S/SW/W/NW, frames left to right).
    /// The actor root transform stays the authoritative feet/yaw source
    /// (TianyongPlayerController locally, ActorWorld interpolation remotely);
    /// this component only reads it, so it needs no network wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QdaoBoySpriteAnimator : MonoBehaviour
    {
        private const string ResourceFolder = "World/Characters/QdaoHeadbandBoy";
        private const int FramesPerDirection = 8;
        private const float FramesPerSecond = 12f;
        // Procedural gait layered over the frame strips: the AI-painted
        // front/back strips barely move the legs, so a two-steps-per-cycle
        // bob, an alternating lean and a contact squash carry the walk read.
        private const float BobAmplitude = 0.5f;   // world units (~10 px at 32 ppu)
        private const float SwayDegrees = 3.5f;    // lean around the camera axis, sign flips per step
        private const float SquashAmount = 0.035f; // stretch at bob top, squash at foot contact
        private const float GaitBlendSpeed = 8f;   // per-second fade of the gait on start/stop
        // 512 px HD frames (the drawn figure stands roughly 400-445 px). At
        // 32 ppu the full billboard is 16 world units tall and renders at
        // about 320 px at the painted city's default 1080p camera. This keeps
        // the source downsampled rather than magnified while restoring the
        // readable character size lost when the city camera was pulled back.
        // The actor root and capsule stay at their physics size; only the
        // rendered picture is enlarged.
        public const float PixelsPerUnit = 32f;
        // The feet sit a few pixels above the strip's bottom edge.
        private const float FeetPivotY = 0.03f;
        private const float WalkSpeedThreshold = 0.5f; // metres per second, planar
        // Lift above the painted ground so the flat sprite wins the depth test.
        private const float SpriteLift = 0.1f;

        // Sheet order; index = clockwise 45° steps of the actor yaw relative to
        // the camera yaw. Index 0 (N) is "facing away from the camera".
        private static readonly string[] DirectionNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        private const int FacingCameraIndex = 4; // S

        private static Sprite[][] _sharedFrames;
        private static bool _loadAttempted;

        private SpriteRenderer _renderer;
        private Transform _billboard;
        private Vector3 _lastPosition;
        private float _animationClock;
        private float _gaitWeight;

        /// <summary>
        /// Adds the animator to an actor and hides its placeholder mesh.
        /// Returns false (leaving the primitive visible) when the frame strips
        /// are not present in Resources.
        /// </summary>
        public static bool TryAttach(GameObject actor)
        {
            if (actor == null || LoadSharedFrames() == null) return false;
            if (actor.GetComponent<QdaoBoySpriteAnimator>() == null)
                actor.AddComponent<QdaoBoySpriteAnimator>();
            // Hide the placeholder mesh (root primitive or a prefab's child
            // visual) but keep TextMesh labels, which also use MeshRenderer.
            foreach (var placeholder in actor.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (placeholder.GetComponent<TextMesh>() != null) continue;
                placeholder.enabled = false;
            }
            return true;
        }

        private static Sprite[][] LoadSharedFrames()
        {
            if (_loadAttempted) return _sharedFrames;
            _loadAttempted = true;

            var frames = new Sprite[DirectionNames.Length][];
            for (var d = 0; d < DirectionNames.Length; d++)
            {
                var strip = Resources.Load<Texture2D>($"{ResourceFolder}/walk_{DirectionNames[d]}");
                if (strip == null)
                {
                    Debug.LogWarning(
                        $"[QdaoBoySpriteAnimator] Missing {ResourceFolder}/walk_{DirectionNames[d]}; " +
                        "players keep placeholder cubes. Run sync_qdao_walk_to_resources.ps1.");
                    return null;
                }

                var frameWidth = strip.width / FramesPerDirection;
                frames[d] = new Sprite[FramesPerDirection];
                for (var f = 0; f < FramesPerDirection; f++)
                {
                    // FullRect: tight meshes would need CPU-readable pixels,
                    // which the import settings deliberately do not keep.
                    var sprite = Sprite.Create(
                        strip,
                        new Rect(f * frameWidth, 0f, frameWidth, strip.height),
                        new Vector2(0.5f, FeetPivotY),
                        PixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect);
                    sprite.name = $"qdao_walk_{DirectionNames[d]}_{f:00}";
                    frames[d][f] = sprite;
                }
            }

            _sharedFrames = frames;
            return _sharedFrames;
        }

        private void Awake()
        {
            var frames = LoadSharedFrames();
            if (frames == null)
            {
                enabled = false;
                return;
            }

            var go = new GameObject("sprite");
            _billboard = go.transform;
            _billboard.SetParent(transform, false);
            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sprite = frames[FacingCameraIndex][0];
            _lastPosition = transform.position;
        }

        private void LateUpdate()
        {
            if (_sharedFrames == null || _renderer == null) return;

            var worldCamera = Camera.main;
            var cameraRotation = worldCamera != null ? worldCamera.transform.rotation : Quaternion.Euler(0f, 45f, 0f);

            var position = transform.position;
            var delta = position - _lastPosition;
            _lastPosition = position;
            delta.y = 0f;
            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            var walking = delta.magnitude / dt >= WalkSpeedThreshold;

            var cameraYaw = CameraYaw(worldCamera);
            var relativeYaw = Mathf.Repeat(transform.eulerAngles.y - cameraYaw, 360f);
            var direction = Mathf.RoundToInt(relativeYaw / 45f) % DirectionNames.Length;

            _animationClock = walking
                ? Mathf.Repeat(_animationClock + dt * FramesPerSecond, FramesPerDirection)
                : 0f;
            _renderer.sprite = _sharedFrames[direction][(int)_animationClock];

            // Gait phase over one 8-frame cycle; the wave crosses zero at the
            // contact frames (0 and 4) and its sign picks the leaning side.
            _gaitWeight = Mathf.MoveTowards(_gaitWeight, walking ? 1f : 0f, dt * GaitBlendSpeed);
            var stepWave = Mathf.Sin(_animationClock / FramesPerDirection * Mathf.PI * 2f);
            var bob = Mathf.Abs(stepWave) * BobAmplitude * _gaitWeight;
            var sway = stepWave * SwayDegrees * _gaitWeight;
            var squash = (Mathf.Abs(stepWave) - 0.5f) * 2f * SquashAmount * _gaitWeight;

            // The root rotates with the actor facing, but the sprite must face
            // the camera: under the isometric camera it stands up, under the
            // painted city's straight-down camera it lies flat on the art.
            // Gait sway tilts it around the view axis, bob lifts it along
            // screen-up, squash scales around the feet pivot.
            _billboard.rotation = cameraRotation * Quaternion.Euler(0f, 0f, sway);
            _billboard.position = position + new Vector3(0f, SpriteLift, 0f)
                + cameraRotation * new Vector3(0f, bob, 0f);
            _billboard.localScale = new Vector3(1f - squash * 0.6f, 1f + squash, 1f);
            _renderer.sortingOrder = WorldSortingOrder(position, worldCamera);
        }

        /// <summary>
        /// Yaw of "screen up" on the ground plane. For a tilted camera that is
        /// its forward vector; for the straight-down painted-city camera the
        /// forward vector has no planar part, so its up vector is used.
        /// </summary>
        public static float CameraYaw(Camera worldCamera)
        {
            if (worldCamera == null) return 45f;
            var planar = worldCamera.transform.forward;
            planar.y = 0f;
            if (planar.sqrMagnitude < 0.01f)
            {
                planar = worldCamera.transform.up;
                planar.y = 0f;
            }
            if (planar.sqrMagnitude < 0.0001f) return 0f;
            return Mathf.Atan2(planar.x, planar.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Sorting order for things standing on the map: whatever is nearer
        /// the bottom of the screen draws in front. Under the painted city's
        /// top-down camera that is simply smaller Z; under the isometric
        /// camera it is the position along the camera's planar forward.
        /// Labels and effects use the same rule to line up with their actor.
        /// </summary>
        public static int WorldSortingOrder(Vector3 world, Camera worldCamera)
        {
            var yaw = CameraYaw(worldCamera) * Mathf.Deg2Rad;
            var depth = world.x * Mathf.Sin(yaw) + world.z * Mathf.Cos(yaw);
            return Mathf.RoundToInt(-depth * 10f);
        }
    }
}
