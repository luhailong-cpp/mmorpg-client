using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MmorpgClient.World.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>One independently movable, static town character; coordinates are Unity X/Z feet points.</summary>
    [Serializable]
    public sealed class TianyongNpcPlacement
    {
        public string id;
        public string displayName;
        public string spriteResource;
        public float x;
        public float z;
        public float visibleHeight;
        public float pivotX;
        public float pivotY;
        public float visibleHeightFraction;
        public string location;

        public Vector3 FeetPosition => new(x, 0f, z);
    }

    [Serializable]
    public sealed class TianyongNpcPlacementManifest
    {
        public int schemaVersion;
        public TianyongNpcPlacement[] entries;

        public bool Validate(out string error)
        {
            error = null;
            if (schemaVersion != 1 || entries == null || entries.Length == 0)
            {
                error = "Expected schemaVersion 1 and at least one NPC placement.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id) ||
                    string.IsNullOrWhiteSpace(entry.displayName))
                    error = "Every NPC needs a unique id and a display name.";
                else if (string.IsNullOrWhiteSpace(entry.spriteResource) ||
                         !entry.spriteResource.StartsWith(TianyongCityNpcs.SpriteResourceRoot, StringComparison.Ordinal) ||
                         entry.spriteResource.Contains("..") || entry.spriteResource.Contains("\\") ||
                         entry.spriteResource.Contains("."))
                    error = $"NPC {entry.id} must reference an extensionless sprite resource under {TianyongCityNpcs.SpriteResourceRoot}.";
                else if (!Finite(entry.x) || !Finite(entry.z) ||
                         !TianyongPaintedCity.PaintingWorldRect.Contains(new Vector2(entry.x, entry.z)))
                    error = $"NPC {entry.id} must stand inside the painted city.";
                else if (!Finite(entry.visibleHeight) || entry.visibleHeight <= 0f || entry.visibleHeight > 40f ||
                         !Finite(entry.visibleHeightFraction) || entry.visibleHeightFraction <= 0f || entry.visibleHeightFraction > 1f ||
                         !Finite(entry.pivotX) || entry.pivotX < 0f || entry.pivotX > 1f ||
                         !Finite(entry.pivotY) || entry.pivotY < 0f || entry.pivotY > 1f)
                    error = $"NPC {entry.id} has invalid sprite size or feet pivot.";
                if (error != null) return false;
            }
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// A separately owned visual layer for the painted Tianyong city. The original map,
    /// navigation and network actors are unaffected. These static characters have no
    /// colliders or fabricated dialogue/shop actions; placement data can move with later art.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongCityNpcs : MonoBehaviour
    {
        public const string RootName = "[TianyongCityNpcs]";
        public const string PlacementsResourcePath = "World/Tianyong/Npcs/placements";
        public const string SpriteResourceRoot = "World/Tianyong/Npcs/Sprites/";
        private const float SpriteLift = 0.1f;
        private const int ShadowTexturePixels = 64;

        private sealed class Visual
        {
            public Transform Feet;
            public Transform Body;
            public SpriteRenderer Renderer;
            public Transform Shadow;
            public SpriteRenderer ShadowRenderer;
        }

        private readonly List<Visual> _visuals = new();
        private readonly List<Sprite> _ownedSprites = new();
        private Sprite _shadowSprite;
        private Texture2D _shadowTexture;

        public int Count => _visuals.Count;

        /// <summary>Call after the painting has hidden procedural renderers, and only for the painted main city.</summary>
        public static TianyongCityNpcs Build(TianyongMapInstance map)
        {
            if (map?.Root == null || map.Theme != TianyongTheme.City ||
                map.Root.transform.Find(TianyongPaintedCity.RootName) == null) return null;
            var existing = map.Root.GetComponentInChildren<TianyongCityNpcs>(true);
            if (existing != null) return existing;

            var asset = Resources.Load<TextAsset>(PlacementsResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[TianyongCityNpcs] Missing Resources/{PlacementsResourcePath}; no static town NPCs loaded.");
                return null;
            }

            TianyongNpcPlacementManifest manifest;
            try { manifest = JsonUtility.FromJson<TianyongNpcPlacementManifest>(asset.text); }
            catch (Exception error)
            {
                Debug.LogError($"[TianyongCityNpcs] Invalid placement JSON: {error.Message}");
                return null;
            }
            if (manifest == null)
            {
                Debug.LogError("[TianyongCityNpcs] Placement JSON is empty.");
                return null;
            }
            if (!manifest.Validate(out var validationError))
            {
                Debug.LogError($"[TianyongCityNpcs] {validationError}");
                return null;
            }

            // Resolve the complete roster before creating any objects, so a broken
            // resource path cannot quietly leave half of the town's cast missing.
            var textures = new Texture2D[manifest.entries.Length];
            for (var i = 0; i < textures.Length; i++)
            {
                textures[i] = Resources.Load<Texture2D>(manifest.entries[i].spriteResource);
                if (textures[i] != null) continue;
                Debug.LogError($"[TianyongCityNpcs] Missing sprite for {manifest.entries[i].id}: {manifest.entries[i].spriteResource}");
                return null;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(map.Root.transform, false);
            var layer = root.AddComponent<TianyongCityNpcs>();
            try
            {
                layer.CreateShadow();
                for (var i = 0; i < textures.Length; i++)
                    layer.CreateVisual(manifest.entries[i], textures[i]);
                layer.RefreshForCamera(Camera.main);
                return layer;
            }
            catch (Exception error)
            {
                root.SetActive(false);
                DestroyOwned(root);
                Debug.LogException(error);
                return null;
            }
        }

        private void CreateVisual(TianyongNpcPlacement entry, Texture2D texture)
        {
            var feet = new GameObject(entry.id).transform;
            feet.SetParent(transform, false);
            feet.localPosition = entry.FeetPosition;
            var body = new GameObject("sprite").transform;
            body.SetParent(feet, false);
            float ppu = texture.height * entry.visibleHeightFraction / entry.visibleHeight;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(entry.pivotX, entry.pivotY), ppu, 0, SpriteMeshType.FullRect);
            sprite.name = entry.id + "Sprite";
            _ownedSprites.Add(sprite);
            var renderer = body.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var shadow = new GameObject(QdaoBoySpriteAnimator.ShadowObjectName).transform;
            shadow.SetParent(feet, false);
            float shadowWidth = QdaoBoySpriteAnimator.ShadowWidth * Mathf.Clamp(entry.visibleHeight / 8f, 0.7f, 1.2f);
            float shadowAspect = TianyongMapConfig.ResolveGroundDiscAspect();
            shadow.localScale = new Vector3(shadowWidth, shadowWidth * shadowAspect, 1f);
            var shadowRenderer = shadow.gameObject.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = _shadowSprite;
            shadowRenderer.color = new Color(0.16f, 0.12f, 0.08f, 0.62f);
            shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;

            var label = WorldNameplate.Create(feet, entry.displayName, WorldNameplate.NpcColor);
            var labelOffset = QdaoBoySpriteAnimator.ShadowScreenDownOffset + shadowWidth * shadowAspect * 0.5f
                              + WorldNameplate.WorldEmHeight * 0.5f + WorldLabelBillboard.DefaultGapBelowShadow;
            WorldLabelBillboard.Attach(label.gameObject, labelOffset);
            _visuals.Add(new Visual
            {
                Feet = feet, Body = body, Renderer = renderer,
                Shadow = shadow, ShadowRenderer = shadowRenderer,
            });
        }

        private void CreateShadow()
        {
            _shadowTexture = new Texture2D(ShadowTexturePixels, ShadowTexturePixels, TextureFormat.RGBA32, false)
            {
                name = "TianyongNpcContactShadow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[ShadowTexturePixels * ShadowTexturePixels];
            float centre = (ShadowTexturePixels - 1) * 0.5f;
            for (var y = 0; y < ShadowTexturePixels; y++)
            for (var x = 0; x < ShadowTexturePixels; x++)
            {
                float radius = Vector2.Distance(new Vector2(x, y), new Vector2(centre, centre)) / (ShadowTexturePixels * 0.5f);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - radius * radius), 2f);
                pixels[y * ShadowTexturePixels + x] = new Color(1f, 1f, 1f, alpha);
            }
            _shadowTexture.SetPixels(pixels);
            _shadowTexture.Apply(false, true);
            _shadowSprite = Sprite.Create(_shadowTexture,
                new Rect(0f, 0f, ShadowTexturePixels, ShadowTexturePixels), new Vector2(0.5f, 0.5f),
                ShadowTexturePixels, 0, SpriteMeshType.FullRect);
            _shadowSprite.name = "TianyongNpcContactShadow";
        }

        private void LateUpdate() => RefreshForCamera(Camera.main);

        /// <summary>Shares the player's camera-facing and feet-depth rules, including painted prop occlusion.</summary>
        public void RefreshForCamera(Camera worldCamera)
        {
            var rotation = worldCamera != null ? worldCamera.transform.rotation : Quaternion.Euler(90f, 0f, 0f);
            float yaw = QdaoBoySpriteAnimator.CameraYaw(worldCamera) * Mathf.Deg2Rad;
            var screenDown = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
            foreach (var visual in _visuals)
            {
                var position = visual.Feet.position;
                int order = QdaoBoySpriteAnimator.WorldSortingOrder(position, worldCamera);
                visual.Body.rotation = rotation;
                visual.Body.position = position + Vector3.up * SpriteLift;
                visual.Renderer.sortingOrder = order;
                visual.Shadow.rotation = Quaternion.Euler(90f, 0f, 0f);
                visual.Shadow.position = position + screenDown * QdaoBoySpriteAnimator.ShadowScreenDownOffset
                                         + Vector3.up * QdaoBoySpriteAnimator.ShadowLift;
                visual.ShadowRenderer.sortingOrder = order + QdaoBoySpriteAnimator.ShadowSortingOffset;
            }
        }

        private void OnDestroy()
        {
            foreach (var sprite in _ownedSprites) DestroyOwned(sprite);
            _ownedSprites.Clear();
            _visuals.Clear();
            DestroyOwned(_shadowSprite);
            DestroyOwned(_shadowTexture);
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
