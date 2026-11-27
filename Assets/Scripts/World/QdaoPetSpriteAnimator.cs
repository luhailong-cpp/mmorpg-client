using MmorpgClient.World.Occlusion;
using UnityEngine;

namespace MmorpgClient.World
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>Pet source clips have their own geometry and real directions; unavailable clips keep the idle.</summary>
    [DisallowMultipleComponent]
    public sealed class QdaoPetSpriteAnimator : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Material _defaultSpriteMaterial;
        private readonly ActorTranslucencyModel _translucency = new();
        private QdaoActionResources.Lease _clip;
        private string _id, _action, _direction = "E";
        private float _elapsed;
        public string PetIdentity => _id;
        public string ActiveAction => _action;
        public Sprite CurrentSprite => _renderer != null ? _renderer.sprite : null;

        public void SetIdentity(string id)
        {
            if (_id == id && _clip != null) return;
            _id = id;
            Clear();
            Play("idle", _direction);
        }

        public bool Play(string action, string direction)
        {
            // Unity 缺失组件的假 null 不受 ?? 检查，必须用组件查询结果判断。
            if (_renderer == null && !gameObject.TryGetComponent(out _renderer))
                _renderer = gameObject.AddComponent<SpriteRenderer>();
            if (action == _action && direction == _direction && _clip != null) return true;
            var next = QdaoPetCatalog.Acquire(_id, action, direction);
            if (next == null)
            {
                // Switching direction must use its own idle, never mirror the opposite delivery.
                if (action != "idle") Play("idle", direction == "W" || direction == "NW" || direction == "SW" ? "W" : "E");
                else Clear();
                return false;
            }
            var previous = _clip;
            _clip = next;
            _action = action;
            _direction = direction;
            _elapsed = 0f;
            _renderer.sprite = next.Frames[0];
            previous?.Dispose();
            return true;
        }

        private void LateUpdate()
        {
            if (_clip == null) return;
            _elapsed += Time.deltaTime;
            if (_action != "idle" && _action != "run" && _elapsed >= _clip.DurationSeconds)
                Play("idle", _direction);
            if (_clip == null) return;
            _renderer.sprite = _clip.FrameAt(_elapsed, _action == "idle" || _action == "run");
            var camera = Camera.main;
            if (camera != null)
            {
                var pos = transform.position;
                MapOcclusionRenderer.SyncSpriteMaterial(_renderer, camera, ref _defaultSpriteMaterial);
                if (MapOcclusionRenderer.TryResolveTopDownDepthY(pos, camera, out float depthY))
                    transform.position = new Vector3(pos.x, depthY, pos.z);
                transform.rotation = camera.transform.rotation;
                _renderer.sortingOrder = QdaoBoySpriteAnimator.WorldSortingOrder(pos, camera);

                bool insideFade = MapOcclusionRenderer.EvaluateFadeZone(pos, out float fadeTarget, out float fadeDuration);
                float nextAlpha = _translucency.Step(insideFade, fadeTarget, fadeDuration, Time.unscaledDeltaTime);
                var tint = _renderer.color;
                if (Mathf.Abs(tint.a - nextAlpha) > 1e-4f)
                {
                    tint.a = nextAlpha;
                    _renderer.color = tint;
                }
            }
        }

        public void Clear()
        {
            if (_renderer != null)
            {
                _renderer.sprite = null;
                var tint = _renderer.color;
                if (tint.a < 1f) { tint.a = 1f; _renderer.color = tint; }
            }
            _translucency.Reset();
            var previous = _clip;
            _clip = null;
            _action = null;
            previous?.Dispose();
        }
        private void OnDisable() => Clear();
        private void OnEnable() { if (_id != null) Play("idle", _direction); }
        private void OnDestroy() => Clear();
    }
}
