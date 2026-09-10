using UnityEngine;

namespace MmorpgClient.World
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Billboarded eight-direction run and dedicated standing poses for player actors, fed by the
    /// qdao headband-boy frame strips under
    /// Resources/World/Characters/QdaoHeadbandBoy (one 8-frame 4096x512 strip per
    /// direction, N/NE/E/SE/S/SW/W/NW, frames left to right), eight idle_*
    /// textures with the same frame size and feet anchor, plus a soft
    /// contact shadow on the ground.
    /// The actor root transform stays the authoritative feet/yaw source
    /// (TianyongPlayerController locally, ActorWorld interpolation remotely);
    /// this component only reads it, so it needs no network wiring.
    ///
    /// Feet-point convention (脚点约定) shared by everything drawn for an actor:
    ///  - the actor root position IS the feet point (TianyongPlayerController
    ///    and ActorWorld both move the root, never a visual child);
    ///  - the run sprite's pivot is the feet (FeetPivotY on the opaque foot
    ///    line), placed at root + up * SpriteLift, so the drawn feet touch the
    ///    root point;
    ///  - the contact shadow is centred on the feet, nudged a little
    ///    screen-down (ShadowScreenDownOffset) and lifted ShadowLift above
    ///    the ground painting, never following the gait or facing;
    ///  - the name label hangs below the feet on screen (WorldLabelBillboard);
    ///  - the click marker lands on the path end point the actor will reach.
    /// Draw order around one actor (WorldSortingOrder of the feet point):
    /// click ring -5, shadow -2, sprite +0, label +1.
    ///
    /// Motion: the run cycle is advanced by actual travel distance (foot
    /// cadence survives frame hitches and remote interpolation). Stopping does
    /// not freeze mid-stride: the cycle keeps playing at run cadence until it
    /// reaches the direction's grounded run phase (IdleFrames), at most one
    /// lap, then switches to its dedicated neutral idle texture. Starting
    /// resumes the run from that grounded phase. Idle textures are authored
    /// standing poses with relaxed arms and planted feet, not held run frames.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QdaoBoySpriteAnimator : MonoBehaviour
    {
        /// <summary>Readable locomotion state (Settling = stopped, finishing the run before the dedicated idle).</summary>
        public enum LocomotionState
        {
            Idle,
            Run,
            Settling,
        }

        private const string ResourceFolder = "World/Characters/QdaoHeadbandBoy";
        public const int FramesPerDirection = 8;
        private const float ReferenceRunSpeed = 9f;

        // 512 px HD frames (the drawn figure stands roughly 418 px of the 512).
        // Scale calibration against the reference video: there the character
        // stands about 15-16 % of the screen height. At 64 ppu (8 u frame,
        // ortho 27 at 1080p) the figure was ~131 px = 12 %; at 52 ppu the
        // frame is 512/52 = 9.846 u -> 196.9 px, figure ~161 px = 14.9 %.
        // Only the character grows: the painted ground keeps its native texel
        // density (camera zoom stays 27) so the map does not blur.
        public const float PixelsPerUnit = 52f;
        public const float FramePixelHeight = 512f;
        public const float FrameWorldHeight = FramePixelHeight / PixelsPerUnit; // ~9.846 u

        // Stride calibration. At 64 ppu one 8-frame cycle covered 4.5 u,
        // i.e. 0.5625 x the 8 u frame height. Keeping the same ratio when the
        // frame grows means the feet still plant where the art draws them:
        //   cycle distance = 0.5625 x 9.846 = 5.538 u
        //   frames per unit = 8 / 5.538 = 1.444
        // and at the 9 u/s run speed the cycle plays at ~13 fps.
        public const float CycleDistancePerFrameHeight = 0.5625f;
        public const float CycleWorldDistance = FrameWorldHeight * CycleDistancePerFrameHeight; // ~5.54 u
        public const float FramesPerWorldUnit = FramesPerDirection / CycleWorldDistance; // ~1.444
        // Time cadence of the cycle at run speed; also the pace at which a
        // stopping actor finishes its cycle into the standing pose.
        public const float RunFramesPerSecond = ReferenceRunSpeed * FramesPerWorldUnit; // ~13 fps

        // Packed strips put the opaque foot line at rows 471-472 of 512
        // (40-41 px above the bottom edge) in every frame of every direction,
        // so a single pivot keeps the feet planted across the whole set.
        private const float FeetPivotY = 0.08f;
        private const float WalkSpeedThreshold = 0.5f; // metres per second, planar
        // Above this planar speed a frame is a warp/snap (server correction,
        // spawn placement), not travel: keep pose and facing instead of
        // spinning the cycle or picking a direction from the jump.
        private const float TeleportSpeedThreshold = 40f;
        // Lift above the painted ground so the flat sprite wins the depth test.
        private const float SpriteLift = 0.1f;

        // Direction sectors are 45 deg wide; a movement yaw must leave the
        // current sector's half width by this much before the strip changes,
        // so a heading that hovers on a boundary (path smoothing, remote
        // interpolation) does not flicker between two strips.
        public const float DirectionSectorDegrees = 45f;
        public const float DirectionHysteresisDegrees = 5f;

        // Contact shadow: a radial soft ellipse under the feet. Warm dark
        // tint and a low peak alpha so it reads as shade on the painting
        // rather than a black disc; the aspect follows the map's ground-disc
        // ratio (TianyongMapConfig.groundDiscAspect) so it lies on the ground
        // the same way the click ring does.
        public const string ShadowObjectName = "shadow";
        public const float ShadowWidth = 2.9f;
        public const float ShadowLift = 0.05f; // above the ground quad (-0.045), below SpriteLift
        public const float ShadowScreenDownOffset = 0.15f;
        public const int ShadowSortingOffset = -2;
        private const int ShadowTexturePixels = 128;
        // Peak alpha measured against the painted paving: 0.45 only darkened
        // the ground under the feet by ~35/255 (14 %), too weak to read as
        // contact next to the artwork's own pillar shadows; 0.62 lands at
        // ~48/255 (19 %), close to those without becoming a hard black disc.
        private static readonly Color ShadowColor = new(0.16f, 0.12f, 0.08f, 0.62f);

        // Sheet order; index = clockwise 45 deg steps of travel relative to
        // the camera yaw. Index 0 (N) is "moving away from the camera".
        private static readonly string[] DirectionNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        private const int FacingCameraIndex = 4; // S

        // Grounded phase per run direction for the stop/start handoff. These
        // indices describe run-cycle phases only; LocomotionState.Idle always
        // displays the independent idle_* texture. Keep the established phase
        // table so stopping continues the current gait before changing pose.
        private static readonly int[] IdleFrames = { 2, 1, 1, 6, 2, 5, 2, 5 };

        private static Sprite[][] _sharedFrames;
        private static Sprite[] _sharedIdleFrames;
        private static bool _loadAttempted;
        private static Sprite _shadowSprite;

        private SpriteRenderer _renderer;
        private Transform _billboard;
        private SpriteRenderer _shadowRenderer;
        private Transform _shadow;
        // Flat on the ground plane regardless of camera or actor yaw
        // (Euler 90,0,0: sprite local +Y maps to world +Z / screen up).
        private Quaternion _groundRotation;
        private Vector3 _lastPosition;
        private float _animationClock;
        private float _settleBudget;
        private int _lastDirection = FacingCameraIndex;

        /// <summary>Current locomotion state (readable for tests and debugging).</summary>
        public LocomotionState State { get; private set; } = LocomotionState.Idle;

        /// <summary>Current strip index (0 = N ... 7 = NW), kept while standing.</summary>
        public int Direction => _lastDirection;

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
            // visual) but keep name labels (TextMesh / 3D TextMeshPro), which
            // also render through a MeshRenderer. The sprite and shadow are
            // SpriteRenderers, so they are unaffected.
            foreach (var placeholder in actor.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (placeholder.GetComponent<TextMesh>() != null) continue;
                if (placeholder.GetComponent<TMPro.TMP_Text>() != null) continue;
                placeholder.enabled = false;
            }
            return true;
        }

        /// <summary>Grounded run-cycle handoff index for a direction (not an idle texture index).</summary>
        public static int IdleFrame(int direction)
        {
            if (direction < 0 || direction >= IdleFrames.Length) return IdleFrames[FacingCameraIndex];
            return IdleFrames[direction];
        }

        /// <summary>Copy of the run-cycle handoff table, indexed N, NE, E, SE, S, SW, W, NW.</summary>
        public static int[] IdleFrameTable() => (int[])IdleFrames.Clone();

        /// <summary>
        /// Nearest 45 deg sector for a camera-relative movement yaw, without
        /// hysteresis: a yaw hovering on a boundary (22.4 / 22.6) alternates
        /// between two strips every frame.
        /// </summary>
        public static int SelectDirectionRaw(float relativeYawDegrees)
        {
            var yaw = Mathf.Repeat(relativeYawDegrees, 360f);
            return Mathf.RoundToInt(yaw / DirectionSectorDegrees) % DirectionNames.Length;
        }

        /// <summary>
        /// Sector selection with hysteresis: the current strip is kept while
        /// the yaw stays within half a sector plus DirectionHysteresisDegrees
        /// of its centre (measured across the 0/360 wrap); beyond that the
        /// nearest sector wins immediately, so a real turn switches on the
        /// first frame while boundary jitter does not.
        /// </summary>
        public static int SelectDirection(float relativeYawDegrees, int currentDirection)
        {
            if (currentDirection < 0 || currentDirection >= DirectionNames.Length)
                return SelectDirectionRaw(relativeYawDegrees);

            var yaw = Mathf.Repeat(relativeYawDegrees, 360f);
            var centre = currentDirection * DirectionSectorDegrees;
            var offset = Mathf.DeltaAngle(centre, yaw);
            var holdHalfWidth = DirectionSectorDegrees * 0.5f + DirectionHysteresisDegrees;
            if (Mathf.Abs(offset) <= holdHalfWidth) return currentDirection;
            return SelectDirectionRaw(yaw);
        }

        private static Sprite[][] LoadSharedFrames()
        {
            if (_loadAttempted) return _sharedFrames;
            _loadAttempted = true;

            var frames = new Sprite[DirectionNames.Length][];
            var idleFrames = new Sprite[DirectionNames.Length];
            for (var d = 0; d < DirectionNames.Length; d++)
            {
                // Keep the established walk_* resource names so existing
                // scenes and import metadata remain stable; their contents are
                // authored run cycles.
                var strip = Resources.Load<Texture2D>($"{ResourceFolder}/walk_{DirectionNames[d]}");
                if (strip == null)
                {
                    Debug.LogWarning(
                        $"[QdaoBoySpriteAnimator] Missing {ResourceFolder}/walk_{DirectionNames[d]}; " +
                        "players keep placeholder cubes. Run sync_qdao_walk_to_resources.ps1.");
                    return null;
                }

                var frameWidth = strip.width / FramesPerDirection;
                var idle = Resources.Load<Texture2D>($"{ResourceFolder}/idle_{DirectionNames[d]}");
                if (idle == null || idle.width != frameWidth || idle.height != strip.height)
                {
                    Debug.LogWarning(
                        $"[QdaoBoySpriteAnimator] Missing or incompatible {ResourceFolder}/idle_{DirectionNames[d]}; " +
                        "dedicated idle textures must match one run frame's dimensions.");
                    return null;
                }
                idleFrames[d] = Sprite.Create(
                    idle,
                    new Rect(0f, 0f, idle.width, idle.height),
                    new Vector2(0.5f, FeetPivotY),
                    PixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);
                idleFrames[d].name = $"qdao_idle_{DirectionNames[d]}_00";

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
                    sprite.name = $"qdao_run_{DirectionNames[d]}_{f:00}";
                    frames[d][f] = sprite;
                }
            }

            _sharedIdleFrames = idleFrames;
            _sharedFrames = frames;
            return _sharedFrames;
        }

        /// <summary>
        /// Procedural 128x128 radial soft disc, white with alpha falling
        /// smoothly from 1 at the centre to 0 at the edge; tint and peak
        /// alpha come from the renderer colour, the ellipse from the scale.
        /// One world unit across at scale 1.
        /// </summary>
        private static Sprite GetShadowSprite()
        {
            if (_shadowSprite != null) return _shadowSprite;

            var texture = new Texture2D(ShadowTexturePixels, ShadowTexturePixels, TextureFormat.RGBA32, false)
            {
                name = "QdaoContactShadow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[ShadowTexturePixels * ShadowTexturePixels];
            var centre = (ShadowTexturePixels - 1) * 0.5f;
            var radius = ShadowTexturePixels * 0.5f;
            for (var y = 0; y < ShadowTexturePixels; y++)
            for (var x = 0; x < ShadowTexturePixels; x++)
            {
                var r = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre)) / radius;
                // Smooth falloff, zero before the texture edge so the clamp
                // border never shows a hard rim.
                var alpha = r >= 1f ? 0f : Mathf.SmoothStep(1f, 0f, r);
                pixels[y * ShadowTexturePixels + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);

            _shadowSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, ShadowTexturePixels, ShadowTexturePixels),
                new Vector2(0.5f, 0.5f),
                ShadowTexturePixels,
                0,
                SpriteMeshType.FullRect);
            _shadowSprite.name = "QdaoContactShadow";
            return _shadowSprite;
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
            _renderer.sprite = _sharedIdleFrames[FacingCameraIndex];

            // Shadow: flat on the ground, sized once; only position and
            // sorting change per frame (LateUpdate).
            var shadowGo = new GameObject(ShadowObjectName);
            _shadow = shadowGo.transform;
            _shadow.SetParent(transform, false);
            var aspect = Tianyong.TianyongMapConfig.ResolveGroundDiscAspect();
            _shadow.localScale = new Vector3(ShadowWidth, ShadowWidth * aspect, 1f);
            _groundRotation = Quaternion.Euler(90f, 0f, 0f);
            _shadow.rotation = _groundRotation;
            _shadowRenderer = shadowGo.AddComponent<SpriteRenderer>();
            _shadowRenderer.sprite = GetShadowSprite();
            _shadowRenderer.color = ShadowColor;

            _lastPosition = transform.position;
            _animationClock = IdleFrame(_lastDirection);
            State = LocomotionState.Idle;
        }

        private void LateUpdate()
        {
            if (_sharedFrames == null || _renderer == null) return;

            var worldCamera = Camera.main;
            var cameraRotation = worldCamera != null ? worldCamera.transform.rotation : Quaternion.Euler(0f, 45f, 0f);
            var cameraYaw = CameraYaw(worldCamera);

            var position = transform.position;
            var delta = position - _lastPosition;
            _lastPosition = position;
            delta.y = 0f;
            var distance = delta.magnitude;
            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            var speed = distance / dt;
            var running = speed >= WalkSpeedThreshold && speed < TeleportSpeedThreshold;

            if (running)
            {
                // Use actual travel, not the controller's smoothed root yaw.
                // The latter can lag a direction change by several frames and
                // briefly select a strip whose feet disagree with the motion.
                // A reversal switches the strip at once but keeps the cycle
                // phase, so the feet carry on instead of restarting.
                var movementYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                var relativeYaw = Mathf.Repeat(movementYaw - cameraYaw, 360f);
                _lastDirection = SelectDirection(relativeYaw, _lastDirection);

                if (State == LocomotionState.Idle)
                    _animationClock = IdleFrame(_lastDirection); // first step leaves the standing pose
                State = LocomotionState.Run;
                _animationClock = Mathf.Repeat(_animationClock + distance * FramesPerWorldUnit, FramesPerDirection);
            }
            else
            {
                if (State == LocomotionState.Run)
                {
                    State = LocomotionState.Settling;
                    _settleBudget = FramesPerDirection; // at most one lap
                }
                if (State == LocomotionState.Settling)
                    Settle(Time.deltaTime);
                if (State == LocomotionState.Idle)
                    _animationClock = IdleFrame(_lastDirection);
            }

            var frame = Mathf.Clamp((int)_animationClock, 0, FramesPerDirection - 1);
            _renderer.sprite = State == LocomotionState.Idle
                ? _sharedIdleFrames[_lastDirection]
                : _sharedFrames[_lastDirection][frame];

            // The root rotates with the actor facing, but the sprite must face
            // the camera: under the isometric camera it stands up, under the
            // painted city's straight-down camera it lies flat on the art. The
            // authored run frames contain all gait motion; keeping this child
            // rigid prevents the whole character card (and nearby scenery by
            // visual association) from bobbing, leaning or breathing.
            _billboard.rotation = cameraRotation;
            _billboard.position = position + new Vector3(0f, SpriteLift, 0f);
            _billboard.localScale = Vector3.one;
            var order = WorldSortingOrder(position, worldCamera);
            _renderer.sortingOrder = order;

            if (_shadow != null)
            {
                // Follows the feet point only: no gait, no facing, no scale.
                // World rotation is re-pinned because the root yaws under it.
                var yawRad = cameraYaw * Mathf.Deg2Rad;
                var screenDown = new Vector3(-Mathf.Sin(yawRad), 0f, -Mathf.Cos(yawRad));
                _shadow.rotation = _groundRotation;
                _shadow.position = position + screenDown * ShadowScreenDownOffset + new Vector3(0f, ShadowLift, 0f);
                _shadowRenderer.sortingOrder = order + ShadowSortingOffset;
            }
        }

        /// <summary>
        /// Plays the cycle on at run cadence until the displayed frame is the
        /// direction's grounded handoff phase, then shows its dedicated idle.
        /// Bounded to one lap so a
        /// stop always lands within ~0.6 s (8 frames at ~13 fps).
        /// </summary>
        private void Settle(float deltaTime)
        {
            var idle = IdleFrame(_lastDirection);
            if ((int)_animationClock == idle)
            {
                _animationClock = idle;
                State = LocomotionState.Idle;
                return;
            }

            var step = RunFramesPerSecond * Mathf.Max(deltaTime, 0f);
            var remaining = Mathf.Repeat(idle - _animationClock, FramesPerDirection);
            if (remaining <= step || _settleBudget <= step)
            {
                _animationClock = idle;
                State = LocomotionState.Idle;
                return;
            }

            _settleBudget -= step;
            _animationClock = Mathf.Repeat(_animationClock + step, FramesPerDirection);
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
