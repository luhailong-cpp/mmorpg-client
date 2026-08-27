using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// The glowing ring that classic 2.5D towns drop where the player taps to
    /// walk: it lands on the ground at the destination, expands a little and
    /// fades out. The ring texture is drawn procedurally so no art is needed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongClickMarker : MonoBehaviour
    {
        private const float Lifetime = 0.55f;
        private const float StartDiameter = 1.6f;
        private const float EndDiameter = 3.0f;
        private const int TexturePixels = 96;
        private static readonly Color RingColor = new(0.45f, 0.92f, 1f, 1f);

        private static Sprite _ringSprite;

        private SpriteRenderer _renderer;
        private float _age;

        /// <summary>Drops a marker at a feet/ground point; it destroys itself.</summary>
        public static TianyongClickMarker Spawn(Vector3 groundPoint, Camera worldCamera)
        {
            var go = new GameObject("[ClickMarker]");
            var marker = go.AddComponent<TianyongClickMarker>();
            marker._renderer = go.AddComponent<SpriteRenderer>();
            marker._renderer.sprite = GetRingSprite();
            marker._renderer.color = RingColor;
            // Markers lie on the ground and draw beneath every actor.
            marker._renderer.sortingOrder = QdaoBoySpriteAnimator.WorldSortingOrder(groundPoint, worldCamera) - 5;

            go.transform.position = groundPoint + Vector3.up * 0.06f;
            // Flat on the ground plane regardless of the camera angle.
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            marker.ApplyAge();
            return marker;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }
            ApplyAge();
        }

        private void ApplyAge()
        {
            var t = Mathf.Clamp01(_age / Lifetime);
            var diameter = Mathf.Lerp(StartDiameter, EndDiameter, 1f - (1f - t) * (1f - t));
            transform.localScale = Vector3.one * diameter;
            var color = RingColor;
            color.a = 1f - t * t;
            _renderer.color = color;
        }

        private static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            var texture = new Texture2D(TexturePixels, TexturePixels, TextureFormat.RGBA32, false)
            {
                name = "TianyongClickRing",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[TexturePixels * TexturePixels];
            var center = (TexturePixels - 1) * 0.5f;
            var outer = TexturePixels * 0.49f;
            var inner = TexturePixels * 0.34f;
            for (var y = 0; y < TexturePixels; y++)
            for (var x = 0; x < TexturePixels; x++)
            {
                var d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                // Soft ring with a brighter core and a faint inner glow.
                var ring = 1f - Mathf.Clamp01(Mathf.Abs(d - (outer + inner) * 0.5f) / ((outer - inner) * 0.5f));
                var glow = d < inner ? 0.25f * (d / inner) : 0f;
                var alpha = Mathf.Clamp01(Mathf.Pow(ring, 1.5f) + glow);
                pixels[y * TexturePixels + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);

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
    }
}
