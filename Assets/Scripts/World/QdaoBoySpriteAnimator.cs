using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Camera-facing eight-direction characters. Each appearance owns its frame count,
    /// cadence and idle policy; the original eight-frame boy remains the fallback.
    /// The actor root stays the authoritative feet point. Only the sprite changes:
    /// movement, collision, camera sorting, nameplates and the contact shadow share
    /// the existing world coordinate convention.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QdaoBoySpriteAnimator : MonoBehaviour
    {
        /// <summary>Readable locomotion state (Settling is the legacy stop handoff; V12 stops directly on its authored idle).</summary>
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

        private sealed class FrameSet
        {
            public string Id;
            public Sprite[][] Walk;
            public Sprite[] Idle;
            public int Count;
            public float Fps;
            public bool DedicatedIdle;
            public bool Legacy;
            public int Contact;
            public int Version;
            public QdaoCharacterCatalog.Appearance Appearance;
            public System.Func<string, Texture2D> LoadTexture;
            public System.Action<Texture2D> ReleaseTexture;
            public System.Func<QdaoCharacterCatalog.Appearance, QdaoCharacterCatalog.Appearance> Fallback;
            public bool ShareHd;
            public readonly HashSet<int> MissingHdDirections = new();
            public float FramesPerUnit => Fps / ReferenceRunSpeed;
            public int ContactFrame(int direction) => Legacy ? IdleFrame(direction) : Contact;
        }

        private static readonly Dictionary<string, FrameSet> SharedFrames = new();
        private FrameSet _frames;
        private QdaoHdResources.Lease _hdActive;
        private QdaoHdResources.Lease _hdObservation;
        public int ResidentHdDirections => (_hdActive != null ? 1 : 0) + (_hdObservation != null ? 1 : 0);
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
        private bool _explicitOriginalRequest;

        /// <summary>Current locomotion state (readable for tests and debugging).</summary>
        public LocomotionState State { get; private set; } = LocomotionState.Idle;

        /// <summary>Current strip index (0 = N ... 7 = NW), kept while standing.</summary>
        public int Direction => _lastDirection;
        public string CharacterId => _frames?.Id ?? QdaoCharacterCatalog.LegacyId;
        public int FrameCount => _frames?.Count ?? FramesPerDirection;
        public int ArtworkVersion => _frames?.Version ?? 0;

        /// <summary>
        /// Adds the animator to an actor and hides its placeholder mesh.
        /// Returns false (leaving the primitive visible) when the frame strips
        /// are not present in Resources.
        /// </summary>
        public static bool TryAttach(GameObject actor, string characterId = null)
        {
            if (actor == null) return false;
            var frames = LoadFrames(characterId);
            if (frames == null) return false;
            var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
            if (animator == null) animator = actor.AddComponent<QdaoBoySpriteAnimator>();
            animator._explicitOriginalRequest = QdaoCharacterCatalog.Find(characterId)?.IsOriginalRoster == true;
            if (!animator.ApplyFrames(frames)) return false;
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

        /// <summary>Change artwork in place without moving the actor or adding another shadow.</summary>
        public bool SetAppearance(string characterId)
        {
            _explicitOriginalRequest = QdaoCharacterCatalog.Find(characterId)?.IsOriginalRoster == true;
            var frames = LoadFrames(characterId);
            if (frames == null) return false;
            return ApplyFrames(frames);
        }

        private bool ApplyFrames(FrameSet frames)
        {
            InitializeVisuals();
            if (_frames == frames) return true;
            ReleaseObservedDirection();
            QdaoHdResources.Lease next = null;
            if (frames.Appearance?.IsHd == true)
            {
                next = AcquireHd(frames, _lastDirection);
                if (next == null)
                {
                    var fallback = MissingAppearanceFrame(frames.Appearance, "direction " + DirectionNames[_lastDirection],
                        frames.LoadTexture, frames.Fallback, frames.ShareHd);
                    return fallback != null && ApplyFrames(fallback);
                }
                SetHdRow(frames, next);
            }
            var previous = _frames;
            var oldActive = _hdActive;
            var oldObservation = _hdObservation;
            _frames = frames;
            _hdActive = next;
            _hdObservation = null;
            _lastPosition = transform.position;
            _animationClock = frames.ContactFrame(_lastDirection);
            _settleBudget = 0f;
            State = LocomotionState.Idle;
            _renderer.sprite = frames.Idle[_lastDirection];
            ClearHdRows(previous);
            oldObservation?.Dispose();
            oldActive?.Dispose();
            enabled = true;
            return true;
        }

        private static FrameSet CreateHdFrameSet(QdaoCharacterCatalog.Appearance appearance,
            System.Func<string, Texture2D> load, System.Action<Texture2D> release,
            System.Func<QdaoCharacterCatalog.Appearance, QdaoCharacterCatalog.Appearance> fallback, bool shared)
            => new FrameSet { Id = appearance.Id, Version = appearance.Version, Count = appearance.FrameCount,
                Fps = appearance.FramesPerSecond, Contact = appearance.ContactFrame, DedicatedIdle = true,
                Appearance = appearance, LoadTexture = load, ReleaseTexture = release, Fallback = fallback,
                ShareHd = shared, Walk = new Sprite[DirectionNames.Length][], Idle = new Sprite[DirectionNames.Length] };

        private static QdaoHdResources.Lease AcquireHd(FrameSet frames, int direction)
            => QdaoHdResources.AcquireWithResources(frames.Appearance, direction,
                frames.LoadTexture, frames.ReleaseTexture, frames.ShareHd);

        private static void SetHdRow(FrameSet frames, QdaoHdResources.Lease lease)
        {
            frames.Walk[lease.Direction] = lease.Walk;
            frames.Idle[lease.Direction] = lease.Idle;
        }
        private static void ClearHdRows(FrameSet frames)
        {
            if (frames?.Appearance?.IsHd != true) return;
            System.Array.Clear(frames.Walk, 0, frames.Walk.Length);
            System.Array.Clear(frames.Idle, 0, frames.Idle.Length);
        }

        /// <summary>Inspect one extra HD direction without changing the renderer, facing or current lease.</summary>
        public bool EnsureDirectionFrames(int direction)
        {
            if (_frames == null || direction < 0 || direction >= DirectionNames.Length) return false;
            if (_frames.Appearance?.IsHd != true) return _frames.Walk[direction] != null;
            if (_hdActive?.Direction == direction) return _hdActive.IsValid;
            if (_hdObservation?.Direction == direction && _hdObservation.IsValid) return true;
            ReleaseObservedDirection();
            var next = AcquireHd(_frames, direction);
            if (next == null) { FallbackFromHd(direction); return false; }
            _hdObservation = next;
            SetHdRow(_frames, next);
            return true;
        }

        public void ReleaseObservedDirection()
        {
            if (_hdObservation == null) return;
            var previous = _hdObservation; _hdObservation = null;
            if (_frames?.Appearance?.IsHd == true)
            {
                _frames.Walk[previous.Direction] = null;
                _frames.Idle[previous.Direction] = null;
            }
            previous.Dispose();
        }

        private bool FallbackFromHd(int direction)
        {
            var frames = _frames;
            if (frames?.Appearance?.IsHd != true) return true;
            if (!frames.MissingHdDirections.Add(direction)) return false;
            var fallback = MissingAppearanceFrame(frames.Appearance, "direction " + DirectionNames[direction],
                frames.LoadTexture, frames.Fallback, frames.ShareHd);
            return fallback != null && ApplyFrames(fallback);
        }

        private bool SelectRenderedDirection(int direction)
        {
            if (_frames.Appearance?.IsHd != true) return true;
            if (_hdActive?.Direction == direction && _hdActive.IsValid) return true;
            // The visible old direction stays leased until its replacement is attached to the renderer.
            QdaoHdResources.Lease next;
            if (_hdObservation?.Direction == direction && _hdObservation.IsValid)
            { next = _hdObservation; _hdObservation = null; }
            else
            {
                ReleaseObservedDirection();
                if (_frames.MissingHdDirections.Contains(direction)) return false;
                next = AcquireHd(_frames, direction);
            }
            if (next == null) return FallbackFromHd(direction);
            var previous = _hdActive;
            SetHdRow(_frames, next);
            _hdActive = next;
            _renderer.sprite = State == LocomotionState.Idle ? next.Idle : next.Walk[Mathf.Clamp((int)_animationClock, 0, next.Walk.Length - 1)];
            if (previous != null)
            {
                if (previous.Direction != next.Direction)
                {
                    _frames.Walk[previous.Direction] = null;
                    _frames.Idle[previous.Direction] = null;
                }
                previous.Dispose();
            }
            return true;
        }

        private void OnDestroy()
        {
            if (_renderer != null) _renderer.sprite = null;
            ReleaseObservedDirection();
            ClearHdRows(_frames);
            _hdActive?.Dispose();
            _hdActive = null;
        }

        private static FrameSet LoadFrames(string characterId)
        {
            var definition = QdaoCharacterCatalog.Find(characterId);
            var appearance = definition?.ResolveAppearance();
            // Known original identities cannot borrow another character's body
            // while their authored set is incomplete or awaiting approval.
            if (definition != null && definition.IsOriginalRoster && appearance == null) return null;
            return LoadFrameSet(appearance);
        }

        private static FrameSet LoadFrameSet(QdaoCharacterCatalog.Appearance appearance)
            => appearance?.IsHd == true
                ? CreateHdFrameSet(appearance, Resources.Load<Texture2D>, texture => Resources.UnloadAsset(texture),
                    QdaoCharacterCatalog.ResolveFallbackAppearance, true)
                : LoadFrameSetWithResources(appearance, Resources.Load<Texture2D>,
                    QdaoCharacterCatalog.ResolveFallbackAppearance, true);

        // The injected providers let tests simulate a resource disappearing after
        // catalog validation without modifying any approved PNG or activation file.
        private static FrameSet LoadFrameSetWithResources(QdaoCharacterCatalog.Appearance appearance,
            System.Func<string, Texture2D> loadTexture,
            System.Func<QdaoCharacterCatalog.Appearance, QdaoCharacterCatalog.Appearance> fallback,
            bool useCache)
        {
            if (appearance?.IsHd == true) return CreateHdFrameSet(appearance, loadTexture, null, fallback, false);
            var id = appearance?.Id ?? QdaoCharacterCatalog.LegacyId;
            var dedicatedIdle = appearance?.HasDedicatedIdle ?? true;
            // A V11 character gains dedicated idle textures without a version
            // bump (see Appearance.HasDedicatedIdle), so the frame-set cache
            // must not hand a walk-frame-01 "idle" to an appearance that now
            // has real standing poses.
            var key = (appearance?.CacheKey ?? QdaoCharacterCatalog.LegacyId) + (dedicatedIdle ? ":idle" : "");

            var folder = appearance?.ResourceFolder ?? ResourceFolder;
            var count = appearance?.FrameCount ?? FramesPerDirection;
            if (appearance != null)
            {
                var portraitPath = folder + "/portrait";
                var portrait = loadTexture(portraitPath);
                if (portrait == null || portrait.width != 1024 || portrait.height != 1024)
                    return MissingAppearanceFrame(appearance, portraitPath, loadTexture, fallback, useCache);
            }
            var strips = new Texture2D[DirectionNames.Length];
            var idles = new Texture2D[DirectionNames.Length];
            var separateFrames = new Texture2D[DirectionNames.Length][];
            // Resolve one complete immutable version before creating sprites. An
            // incomplete upgrade retries this identity's next approved version.
            for (var d = 0; d < DirectionNames.Length; d++)
            {
                if (appearance != null)
                {
                    separateFrames[d] = new Texture2D[count];
                    for (var f = 0; f < count; f++)
                    {
                        var path = appearance.FrameResourcePath(DirectionNames[d], f);
                        var texture = loadTexture(path);
                        if (!IsFrame(texture)) return MissingAppearanceFrame(appearance, path, loadTexture, fallback, useCache);
                        separateFrames[d][f] = texture;
                    }
                    if (dedicatedIdle)
                    {
                        var path = appearance.IdleResourcePath(DirectionNames[d]);
                        idles[d] = loadTexture(path);
                        if (!IsFrame(idles[d])) return MissingAppearanceFrame(appearance, path, loadTexture, fallback, useCache);
                    }
                    continue;
                }
                var strip = loadTexture($"{folder}/walk_{DirectionNames[d]}");
                var idle = loadTexture($"{folder}/idle_{DirectionNames[d]}");
                if (strip == null || strip.width != count * FramePixelHeight ||
                    strip.height != FramePixelHeight || !IsFrame(idle))
                {
                    Debug.LogWarning($"[QdaoBoySpriteAnimator] Incomplete original artwork: {folder}, direction {DirectionNames[d]}.");
                    return null;
                }
                strips[d] = strip;
                idles[d] = idle;
            }

            // Recheck completeness before returning cached sprites: a texture can
            // disappear during an editor import after catalog selection succeeded.
            if (useCache && SharedFrames.TryGetValue(key, out var cached) &&
                CachedFramesMatch(cached, separateFrames, strips, idles)) return cached;
            var result = new FrameSet
            {
                Id = id, Count = count, DedicatedIdle = dedicatedIdle,
                Legacy = appearance == null, Contact = appearance?.ContactFrame ?? 0,
                Version = appearance?.Version ?? 0,
                Fps = appearance?.FramesPerSecond ?? RunFramesPerSecond,
                Walk = new Sprite[DirectionNames.Length][], Idle = new Sprite[DirectionNames.Length],
            };
            var prefix = appearance == null ? "qdao" : id;
            for (var d = 0; d < DirectionNames.Length; d++)
            {
                result.Walk[d] = new Sprite[count];
                for (var f = 0; f < count; f++)
                {
                    // Every V11/V12/V13 pose owns a separate 512px texture. Only
                    // the historical headband-boy strips are sliced at runtime.
                    var texture = appearance == null ? strips[d] : separateFrames[d][f];
                    var frameX = appearance == null ? f * FramePixelHeight : 0f;
                    var sprite = Sprite.Create(texture,
                        new Rect(frameX, 0f, FramePixelHeight, FramePixelHeight),
                        new Vector2(0.5f, FeetPivotY), PixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = $"{prefix}_run_{DirectionNames[d]}_{f:00}";
                    result.Walk[d][f] = sprite;
                }
                if (dedicatedIdle)
                {
                    var sprite = Sprite.Create(idles[d],
                        new Rect(0f, 0f, FramePixelHeight, FramePixelHeight),
                        new Vector2(0.5f, FeetPivotY), PixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = $"{prefix}_idle_{DirectionNames[d]}_00";
                    result.Idle[d] = sprite;
                }
                else
                {
                    result.Idle[d] = result.Walk[d][result.Contact];
                }
            }
            if (useCache) SharedFrames[key] = result;
            return result;
        }

        private static bool IsFrame(Texture2D texture)
            => texture != null && texture.width == FramePixelHeight && texture.height == FramePixelHeight;

        private static bool CachedFramesMatch(FrameSet frames, Texture2D[][] separateFrames,
            Texture2D[] strips, Texture2D[] idles)
        {
            for (var direction = 0; direction < DirectionNames.Length; direction++)
            {
                var expectedIdle = frames.DedicatedIdle ? idles[direction] : separateFrames[direction][frames.Contact];
                if (frames.Idle[direction] == null || frames.Idle[direction].texture != expectedIdle) return false;
                for (var frame = 0; frame < frames.Count; frame++)
                {
                    var expected = frames.Legacy ? strips[direction] : separateFrames[direction][frame];
                    if (frames.Walk[direction][frame] == null || frames.Walk[direction][frame].texture != expected) return false;
                }
            }
            return true;
        }

        private static FrameSet MissingAppearanceFrame(QdaoCharacterCatalog.Appearance appearance, string path,
            System.Func<string, Texture2D> loadTexture,
            System.Func<QdaoCharacterCatalog.Appearance, QdaoCharacterCatalog.Appearance> fallback,
            bool useCache)
        {
            var next = fallback(appearance);
            if (next != null && next.Id == appearance.Id && next.Version < appearance.Version)
            {
                Debug.LogWarning($"[QdaoBoySpriteAnimator] Incomplete V{appearance.Version} artwork: {path}. Retaining {appearance.Id} V{next.Version}.");
                return LoadFrameSetWithResources(next, loadTexture, fallback, useCache);
            }
            Debug.LogWarning($"[QdaoBoySpriteAnimator] Missing artwork for {appearance.Id}: {path}. Keeping the actor's current visual.");
            return null;
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

        private void Awake() => InitializeVisuals();

        private void Start()
        {
            if (_frames == null && (_explicitOriginalRequest || !SetAppearance(null))) enabled = false;
        }

        private void InitializeVisuals()
        {
            if (_renderer != null) return;
            var go = new GameObject("sprite");
            _billboard = go.transform;
            _billboard.SetParent(transform, false);
            _renderer = go.AddComponent<SpriteRenderer>();

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
            _animationClock = _frames != null ? _frames.ContactFrame(_lastDirection) : IdleFrame(_lastDirection);
            State = LocomotionState.Idle;
        }

        private void LateUpdate()
        {
            if (_frames == null || _renderer == null) return;
            if (QdaoCharacterCatalog.IsRejectedHdAppearance(_frames.Appearance)) FallbackFromHd(_lastDirection);

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
                // A reversal switches the strip at once and keeps its frame phase.
                // TODO: unify anatomical left/right phases across the artwork
                // before mapping phases between directions; frame indices alone
                // do not guarantee that the same leg remains planted.
                var movementYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                var relativeYaw = Mathf.Repeat(movementYaw - cameraYaw, 360f);
                var nextDirection = SelectDirection(relativeYaw, _lastDirection);
                if (SelectRenderedDirection(nextDirection)) _lastDirection = nextDirection;

                if (State == LocomotionState.Idle)
                    _animationClock = _frames != null ? _frames.ContactFrame(_lastDirection) : IdleFrame(_lastDirection); // first step leaves the standing pose
                State = LocomotionState.Run;
                _animationClock = Mathf.Repeat(_animationClock + distance * _frames.FramesPerUnit, _frames.Count);
            }
            else
            {
                // V12 and V13 have an authored standing pose for every direction. Once
                // travel stops, continuing the walk in place would slide the
                // feet for up to a whole cycle before that pose appears.
                if (_frames.Version >= 12 && _frames.DedicatedIdle && speed < WalkSpeedThreshold)
                {
                    State = LocomotionState.Idle;
                    _settleBudget = 0f;
                }
                else if (State == LocomotionState.Run)
                {
                    State = LocomotionState.Settling;
                    _settleBudget = _frames.Count; // at most one lap
                }
                if (State == LocomotionState.Settling)
                    Settle(Time.deltaTime);
                if (State == LocomotionState.Idle)
                    _animationClock = _frames != null ? _frames.ContactFrame(_lastDirection) : IdleFrame(_lastDirection);
            }

            SelectRenderedDirection(_lastDirection);
            var frame = Mathf.Clamp((int)_animationClock, 0, _frames.Count - 1);
            _renderer.sprite = State == LocomotionState.Idle
                ? _frames.Idle[_lastDirection]
                : _frames.Walk[_lastDirection][frame];

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
        /// direction's contact phase, then holds it (V11) or shows its authored
        /// idle (legacy or V11 with standing textures). V12 uses its idle immediately when travel stops.
        /// </summary>
        private void Settle(float deltaTime)
        {
            var idle = _frames.ContactFrame(_lastDirection);
            if ((int)_animationClock == idle)
            {
                _animationClock = idle;
                State = LocomotionState.Idle;
                return;
            }

            var step = _frames.Fps * Mathf.Max(deltaTime, 0f);
            var remaining = Mathf.Repeat(idle - _animationClock, _frames.Count);
            if (remaining <= step || _settleBudget <= step)
            {
                _animationClock = idle;
                State = LocomotionState.Idle;
                return;
            }

            _settleBudget -= step;
            _animationClock = Mathf.Repeat(_animationClock + step, _frames.Count);
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
