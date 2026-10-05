using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace MmorpgClient.World
{
    /// <summary>Pet source clips have their own geometry and real directions; unavailable clips keep the idle.</summary>
    [DisallowMultipleComponent]
    public sealed class QdaoPetSpriteAnimator : MonoBehaviour
    {
        private SpriteRenderer _renderer;
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
            if (_renderer == null) _renderer = gameObject.GetComponent<SpriteRenderer>() ?? gameObject.AddComponent<SpriteRenderer>();
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
                transform.rotation = camera.transform.rotation;
                _renderer.sortingOrder = QdaoBoySpriteAnimator.WorldSortingOrder(transform.position, camera);
            }
        }

        public void Clear()
        {
            if (_renderer != null) _renderer.sprite = null;
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
