using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MmorpgClient.World.Occlusion
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Owns a map's merged depth-only occluder mesh and compiled fade zones.
    /// Registered strictly while enabled so map switches, rollbacks and tests never leak state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapOcclusionRenderer : MonoBehaviour
    {
        public const string ChildObjectName = "[MapOcclusion]";
        public const string DepthMaskMaterialResourcePath = "World/Shaders/OcclusionDepthMask";
        public const string ActorSpriteMaterialResourcePath = "World/Shaders/ActorOcclusionSprite";
        public const string DepthMaskShaderName = "World/OcclusionDepthMask";
        public const string ActorSpriteShaderName = "World/ActorOcclusionSprite";

        private static readonly List<MapOcclusionRenderer> Registered = new();
        private static Material _fallbackActorMaterial;
        private static Material _fallbackDefaultSpriteMaterial;

        private Mesh _ownedMesh;
        private Material _ownedDepthMaterial;
        private MeshRenderer _meshRenderer;
        private Rect _worldRect;
        private CompiledFadeZone[] _fadeZones = Array.Empty<CompiledFadeZone>();

        public Rect WorldRect => _worldRect;
        public bool HasDepthOccluders => _ownedMesh != null;
        public bool HasFadeZones => _fadeZones.Length > 0;
        public IReadOnlyList<CompiledFadeZone> FadeZones => _fadeZones;
        public Mesh DepthMaskMesh => _ownedMesh;

        /// <summary>Most recently enabled live occlusion renderer, or null when the current map has none.</summary>
        public static MapOcclusionRenderer Active
        {
            get
            {
                for (int i = Registered.Count - 1; i >= 0; i--)
                {
                    var candidate = Registered[i];
                    if (candidate == null)
                    {
                        Registered.RemoveAt(i);
                        continue;
                    }
                    if (candidate.isActiveAndEnabled)
                        return candidate;
                }
                return null;
            }
        }

        /// <summary>True only for straight-down orthographic 2.5D map cameras where Y maps purely to depth.</summary>
        public static bool IsTopDownCamera(Camera worldCamera)
            => worldCamera != null && worldCamera.orthographic && Mathf.Abs(worldCamera.transform.forward.y) > 0.99f;

        /// <summary>Resolves top-down depth Y for an actor/shadow/marker when the active map has depth occluders.</summary>
        public static bool TryResolveTopDownDepthY(Vector3 worldFeet, Camera worldCamera, out float depthY)
        {
            var active = Active;
            if (active == null || !active.HasDepthOccluders || !IsTopDownCamera(worldCamera))
            {
                depthY = 0f;
                return false;
            }

            depthY = OcclusionDepthMapper.WorldZToDepthY(worldFeet.z, active._worldRect);
            return true;
        }

        /// <summary>Resolves forward clearance distance for nameplates/bubbles so they never clip behind occluders.</summary>
        public static bool TryResolveTopDownLabelClearance(Camera worldCamera, out float clearanceDistance)
        {
            var active = Active;
            if (active == null || !active.HasDepthOccluders || !IsTopDownCamera(worldCamera))
            {
                clearanceDistance = 0f;
                return false;
            }

            clearanceDistance = OcclusionDepthMapper.LabelClearanceY;
            return true;
        }

        /// <summary>
        /// Returns the shared dual-pass silhouette sprite material while top-down depth occlusion is active;
        /// returns null otherwise so sprites retain their default material.
        /// </summary>
        public static Material ResolveActorMaterial(Camera worldCamera)
        {
            var active = Active;
            if (active == null || !active.HasDepthOccluders || !IsTopDownCamera(worldCamera))
                return null;

            var resourceMaterial = Resources.Load<Material>(ActorSpriteMaterialResourcePath);
            if (resourceMaterial != null) return resourceMaterial;

            if (_fallbackActorMaterial == null)
            {
                var shader = Shader.Find(ActorSpriteShaderName);
                if (shader != null && shader.isSupported)
                    _fallbackActorMaterial = new Material(shader) { name = "ActorOcclusionSpriteFallback" };
            }
            return _fallbackActorMaterial;
        }

        /// <summary>Applies or restores the sprite material for an actor/pet/NPC without per-frame allocations.</summary>
        public static void SyncSpriteMaterial(SpriteRenderer spriteRenderer, Camera worldCamera, ref Material defaultMaterial)
        {
            if (spriteRenderer == null) return;
            if (defaultMaterial == null)
                defaultMaterial = spriteRenderer.sharedMaterial ?? ResolveFallbackDefaultSpriteMaterial();

            var occlusionMaterial = ResolveActorMaterial(worldCamera);
            var target = occlusionMaterial != null ? occlusionMaterial : defaultMaterial;
            if (target != null && spriteRenderer.sharedMaterial != target)
                spriteRenderer.sharedMaterial = target;
        }

        /// <summary>Evaluates active map fade zones at the actor's world feet position.</summary>
        public static bool EvaluateFadeZone(Vector3 worldFeet, out float targetAlpha, out float fadeDurationSec)
        {
            var active = Active;
            if (active == null || !active.HasFadeZones)
            {
                targetAlpha = 1f;
                fadeDurationSec = 0f;
                return false;
            }

            return OcclusionDepthMapper.EvaluateFadeZones(
                new Vector2(worldFeet.x, worldFeet.z),
                active._fadeZones,
                out targetAlpha,
                out fadeDurationSec);
        }

        /// <summary>
        /// Optionally loads <c>&lt;resourceFolder&gt;occlusion.json</c> and attaches a renderer to <paramref name="mapRoot"/>.
        /// Missing occlusion config is normal and returns null quietly; malformed config logs an error and skips occlusion.
        /// </summary>
        public static MapOcclusionRenderer TryAttachFromResources(GameObject mapRoot, string resourceFolder, Rect worldRect)
        {
            if (mapRoot == null || string.IsNullOrEmpty(resourceFolder)) return null;
            var asset = Resources.Load<TextAsset>(resourceFolder + MapOcclusionData.ResourceFileName);
            if (asset == null) return null;

            try
            {
                var data = MapOcclusionData.Parse(asset.text);
                return Attach(mapRoot, data, worldRect);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapOcclusion] Failed to load {resourceFolder}{MapOcclusionData.ResourceFileName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Attaches a depth-mask renderer and compiled fade zones built from <paramref name="data"/>.</summary>
        public static MapOcclusionRenderer Attach(GameObject mapRoot, MapOcclusionData data, Rect worldRect)
        {
            if (mapRoot == null || data == null || data.IsEmpty) return null;
            data.Validate();

            var go = new GameObject(ChildObjectName);
            go.transform.SetParent(mapRoot.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var component = go.AddComponent<MapOcclusionRenderer>();
            try
            {
                component.Configure(data, worldRect);
                return component;
            }
            catch
            {
                component.DisposeOwnedResources();
                DestroyOwned(go);
                throw;
            }
        }

        private void Configure(MapOcclusionData data, Rect worldRect)
        {
            _worldRect = worldRect;
            _fadeZones = OcclusionDepthMapper.CompileFadeZones(data, worldRect);
            RegisterAsTopmost();

            if (!data.HasOccluders) return;

            var mesh = MapOcclusionMeshBuilder.BuildMergedMesh(data, worldRect);
            if (mesh == null) return;

            var material = ResolveDepthMaskMaterial(out bool owned);
            if (material == null)
            {
                DestroyOwned(mesh);
                Debug.LogError($"[MapOcclusion] Missing {DepthMaskMaterialResourcePath} material and {DepthMaskShaderName} shader; depth occlusion disabled.");
                return;
            }

            _ownedMesh = mesh;
            if (owned) _ownedDepthMaterial = material;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _ownedMesh;

            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Material ResolveDepthMaskMaterial(out bool owned)
        {
            var resourceMaterial = Resources.Load<Material>(DepthMaskMaterialResourcePath);
            if (resourceMaterial != null)
            {
                owned = false;
                return resourceMaterial;
            }

            var shader = Shader.Find(DepthMaskShaderName);
            if (shader == null || !shader.isSupported)
            {
                owned = false;
                return null;
            }

            owned = true;
            return new Material(shader) { name = "MapOcclusionDepthMaskRuntime" };
        }

        private static Material ResolveFallbackDefaultSpriteMaterial()
        {
            if (_fallbackDefaultSpriteMaterial != null) return _fallbackDefaultSpriteMaterial;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                _fallbackDefaultSpriteMaterial = new Material(shader) { name = "SpritesDefaultFallback" };
            return _fallbackDefaultSpriteMaterial;
        }

        private void RegisterAsTopmost()
        {
            Registered.Remove(this);
            Registered.Add(this);
        }

        private void OnEnable() => RegisterAsTopmost();

        public void DisposeOwnedResources()
        {
            Registered.Remove(this);
            if (_meshRenderer != null) _meshRenderer.sharedMaterial = null;
            DestroyOwned(_ownedMesh);
            DestroyOwned(_ownedDepthMaterial);
            _ownedMesh = null;
            _ownedDepthMaterial = null;
            _fadeZones = Array.Empty<CompiledFadeZone>();
        }

        private void OnDestroy() => DisposeOwnedResources();

        public static void ResetSharedFallbackMaterialsForTests()
        {
            DestroyOwned(_fallbackActorMaterial);
            DestroyOwned(_fallbackDefaultSpriteMaterial);
            _fallbackActorMaterial = null;
            _fallbackDefaultSpriteMaterial = null;
        }

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
