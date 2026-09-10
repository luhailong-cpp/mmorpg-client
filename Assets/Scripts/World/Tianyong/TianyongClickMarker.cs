using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// The ring that classic 2.5D towns drop where the player clicks to walk.
    /// <para>
    /// Shape: a flat disc lying on the painted ground. The painting draws
    /// ground circles as circles (straight-down view) while figures are seen
    /// from the side, so the disc is squashed along screen-up (world +Z) by
    /// <see cref="TianyongMapConfig.GroundDiscAspect"/> - the same ratio the
    /// actor's contact shadow uses - i.e. localScale = (d, d * aspect, 1) on a
    /// sprite rotated Euler(90, 0, 0). The aspect is read once per domain and
    /// cached; <see cref="ResetStaticCache"/> clears it for tests.
    /// </para>
    /// <para>
    /// Look: the texture is procedural (no art), a bright cyan main ring
    /// bracketed by a dark navy rim on both sides so it stays legible on pale
    /// flagstones and in shadowed alleys alike, plus a small solid dot at the
    /// centre that pins the exact landing point. Colours live in the texture;
    /// <see cref="SpriteRenderer.color"/> only carries the fade alpha. The
    /// 160 px texture is drawn at roughly 30-60 screen pixels (ortho 27 at
    /// 1080p, then squashed by the aspect), so it carries a mip chain and is
    /// sampled trilinearly; without mips the 1-2 px rims would sparkle and
    /// crawl during the scale animation.
    /// </para>
    /// <para>
    /// The texture and sprite are created once per domain and held only by a
    /// static field. That is enough to survive scene loads (assets are not
    /// scene objects and UnloadUnusedAssets honours managed static roots)
    /// while still letting the editor tear them down on Play Mode exit, so no
    /// DontSave flags are set and nothing accumulates across Play sessions.
    /// </para>
    /// <para>
    /// Motion (about 0.7 s): pops from 0.7x to 1x in the first 0.08 s, holds
    /// while drifting to 1.15x until 0.4 s, then spreads to 1.35x while fading
    /// out. Deliberately restrained - a landing cue, not a skill effect.
    /// </para>
    /// <para>
    /// Anchoring: the marker is a scene-root object whose position is set
    /// exactly once in <see cref="Spawn"/> and never touched again, so it
    /// stays glued to the world point while the camera pans and neither
    /// follows the actor nor the mouse.
    /// </para>
    /// <para>
    /// Contract with <see cref="TianyongPlayerController.ClickAt"/>: the
    /// controller spawns the marker on the destination it actually adopted
    /// (the last point of the planned path), not on the raw click. A click on
    /// a roof or canal therefore shows the ring on the nearest legal cell the
    /// actor will really walk to, so the feedback never lies.
    /// </para>
    /// <para>
    /// Residue: at most <see cref="MaxLive"/> markers exist at once; spawning
    /// a new one retires the oldest immediately, so click spam leaves no trail.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongClickMarker : MonoBehaviour
    {
        /// <summary>Total life of a marker in seconds.</summary>
        public const float Lifetime = 0.7f;
        /// <summary>Diameter (world units, along X) at 1x scale. Slightly smaller than the actor's ~3.2 u contact shadow.</summary>
        public const float BaseDiameter = 2.2f;
        /// <summary>Maximum number of markers alive at once.</summary>
        public const int MaxLive = 2;
        /// <summary>Lift above the ground point so the disc clears the painted quad (y = -0.045).</summary>
        public const float GroundLift = 0.06f;
        /// <summary>Sorting offset below the actor's own order (its contact shadow sits at -2).</summary>
        public const int SortingOffset = -5;

        // Timeline (seconds) and scale keys.
        private const float PopEnd = 0.08f;
        private const float HoldEnd = 0.4f;
        private const float PopStartScale = 0.7f;
        private const float PopEndScale = 1f;
        private const float HoldEndScale = 1.15f;
        private const float FadeEndScale = 1.35f;

        // Ring layout as fractions of the texture radius (outer to inner).
        private const int TexturePixels = 160;
        private const float OuterEdge = 0.98f;
        private const float RimWidth = 0.07f;
        private const float RingWidth = 0.24f;
        // Centre dot: radius as a fraction of the disc radius, i.e. its diameter
        // is 12% of the disc diameter (~0.26 u, ~5 px at 1x on a 1080p ortho-27
        // view). Half that would be ~2.6 px and vanish, defeating its purpose.
        private const float CoreRadius = 0.12f;
        private static readonly Color RimColor = new(0.05f, 0.15f, 0.30f, 0.85f);
        private static readonly Color RingColor = new(0.55f, 0.95f, 1f, 1f);
        private static readonly Color CoreColor = new(0.55f, 0.95f, 1f, 0.9f);

        private static readonly List<TianyongClickMarker> Live = new();
        private static Sprite _ringSprite;
        private static float _groundAspect = -1f; // < 0: not resolved yet

        private SpriteRenderer _renderer;
        private float _aspect = TianyongMapConfig.DefaultGroundDiscAspect;
        private float _age;

        /// <summary>Number of markers currently alive (destroyed ones are pruned on read).</summary>
        public static int LiveCount
        {
            get
            {
                Prune();
                return Live.Count;
            }
        }

        /// <summary>
        /// Test hook: forgets the cached ground aspect and the cached ring
        /// sprite and empties the live list. Destroy all markers first (the
        /// sprite they share is released here).
        /// </summary>
        public static void ResetStaticCache()
        {
            _groundAspect = -1f;
            Live.Clear();
            if (_ringSprite != null)
            {
                var texture = _ringSprite.texture;
                DestroyNow(_ringSprite);
                if (texture != null) DestroyNow(texture);
            }
            _ringSprite = null;
        }

        /// <summary>
        /// Drops a marker at a feet/ground point; it destroys itself after
        /// <see cref="Lifetime"/>. When <see cref="MaxLive"/> markers are
        /// already alive the oldest is retired first.
        /// </summary>
        public static TianyongClickMarker Spawn(Vector3 groundPoint, Camera worldCamera)
        {
            Prune();
            while (Live.Count >= MaxLive)
            {
                var oldest = Live[0];
                Live.RemoveAt(0);
                if (oldest != null) oldest.Retire();
            }

            if (_groundAspect < 0f) _groundAspect = TianyongMapConfig.ResolveGroundDiscAspect();

            var go = new GameObject("[ClickMarker]");
            var marker = go.AddComponent<TianyongClickMarker>();
            marker._aspect = _groundAspect;
            marker._renderer = go.AddComponent<SpriteRenderer>();
            marker._renderer.sprite = GetRingSprite();
            marker._renderer.color = Color.white;
            // Markers lie on the ground and draw beneath every actor and its shadow.
            marker._renderer.sortingOrder =
                QdaoBoySpriteAnimator.WorldSortingOrder(groundPoint, worldCamera) + SortingOffset;

            // World anchor: set once, never updated (see class remarks).
            go.transform.position = groundPoint + Vector3.up * GroundLift;
            // Flat on the ground plane; local +Y then points along world +Z (screen up).
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            marker.ApplyAge();

            Live.Add(marker);
            return marker;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Lifetime)
            {
                Retire();
                return;
            }
            ApplyAge();
        }

        private void OnDestroy()
        {
            Live.Remove(this);
        }

        /// <summary>Scale and fade for the current age; position is never touched.</summary>
        private void ApplyAge()
        {
            var t = Mathf.Clamp(_age, 0f, Lifetime);
            float scale;
            var alpha = 1f;
            if (t < PopEnd)
            {
                var u = t / PopEnd;
                var easeOut = 1f - (1f - u) * (1f - u);
                scale = Mathf.Lerp(PopStartScale, PopEndScale, easeOut);
            }
            else if (t < HoldEnd)
            {
                var u = (t - PopEnd) / (HoldEnd - PopEnd);
                scale = Mathf.Lerp(PopEndScale, HoldEndScale, u);
            }
            else
            {
                var u = (t - HoldEnd) / (Lifetime - HoldEnd);
                scale = Mathf.Lerp(HoldEndScale, FadeEndScale, u);
                alpha = 1f - u;
            }

            var diameter = BaseDiameter * scale;
            transform.localScale = new Vector3(diameter, diameter * _aspect, 1f);
            _renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        /// <summary>Removes the marker from the live list and destroys it (immediately in edit mode).</summary>
        private void Retire()
        {
            Live.Remove(this);
            DestroyNow(gameObject);
        }

        private static void Prune()
        {
            for (var i = Live.Count - 1; i >= 0; i--)
                if (Live[i] == null) Live.RemoveAt(i);
        }

        private static void DestroyNow(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            // mipChain: true - the disc is minified 3-8x on screen (see class remarks).
            var texture = new Texture2D(TexturePixels, TexturePixels, TextureFormat.RGBA32, true)
            {
                name = "TianyongClickRing",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            var pixels = new Color[TexturePixels * TexturePixels];
            var center = (TexturePixels - 1) * 0.5f;
            var radius = TexturePixels * 0.5f;
            var feather = 1f / radius; // one texel of anti-aliasing at each band edge

            // Bands from the outside in: dark rim, bright ring, dark rim, then
            // a clear gap and the centre dot.
            var outerRimInner = OuterEdge - RimWidth;
            var ringInner = outerRimInner - RingWidth;
            var innerRimInner = ringInner - RimWidth;

            for (var y = 0; y < TexturePixels; y++)
            for (var x = 0; x < TexturePixels; x++)
            {
                var d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / radius;
                var rgb = Vector3.zero;
                var alpha = 0f;
                Accumulate(ref rgb, ref alpha, RimColor, Band(d, outerRimInner, OuterEdge, feather));
                Accumulate(ref rgb, ref alpha, RingColor, Band(d, ringInner, outerRimInner, feather));
                Accumulate(ref rgb, ref alpha, RimColor, Band(d, innerRimInner, ringInner, feather));
                Accumulate(ref rgb, ref alpha, CoreColor, Band(d, -1f, CoreRadius, feather));
                // Clear texels take the rim colour: mip generation box-filters RGB
                // regardless of alpha, and the dark rims are what border the clear
                // areas, so this keeps the smaller mips from fringing toward cyan.
                pixels[y * TexturePixels + x] = alpha > 0f
                    ? new Color(rgb.x / alpha, rgb.y / alpha, rgb.z / alpha, Mathf.Clamp01(alpha))
                    : new Color(RimColor.r, RimColor.g, RimColor.b, 0f);
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true); // build the mip chain, then drop the CPU copy

            _ringSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, TexturePixels, TexturePixels),
                new Vector2(0.5f, 0.5f),
                TexturePixels, // one world unit across at scale 1
                0,
                SpriteMeshType.FullRect);
            _ringSprite.name = "TianyongClickRing";
            return _ringSprite;
        }

        /// <summary>Coverage of the annulus inner &lt; d &lt; outer with a one-texel soft edge.</summary>
        private static float Band(float d, float inner, float outer, float feather)
        {
            var insideOuter = Mathf.Clamp01((outer - d) / feather + 0.5f);
            var insideInner = Mathf.Clamp01((inner - d) / feather + 0.5f);
            return insideOuter * (1f - insideInner);
        }

        private static void Accumulate(ref Vector3 rgb, ref float alpha, Color color, float coverage)
        {
            var a = color.a * coverage;
            if (a <= 0f) return;
            rgb += new Vector3(color.r, color.g, color.b) * a;
            alpha += a;
        }
    }
}
