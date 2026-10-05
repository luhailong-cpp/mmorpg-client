using System.Collections;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>
    /// Lightweight visual cue for skill release / hit:
    ///   - expanding wireframe ring at the caster
    ///   - line from caster to each target
    ///   - target gets a brief flash
    /// All spawned objects auto-destroy after their lifetime.
    /// </summary>
    public static class SkillFx
    {
        public static void PlayCast(GameObject caster, float radius = 2f, float ttl = 0.6f)
        {
            if (caster == null) return;
            caster.GetComponent<QdaoBoySpriteAnimator>()?.PlayAction("cast");
            var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(ring.GetComponent<Collider>());
            ring.name = "CastRing";
            ring.transform.SetParent(caster.transform, false);
            ring.transform.localPosition = UnityEngine.Vector3.zero;
            var r = ring.GetComponent<Renderer>();
            r.material = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.95f, 0.3f, 0.5f) };
            var driver = ring.AddComponent<RingDriver>();
            driver.maxRadius = radius;
            driver.ttl = ttl;
        }

        public static void PlayHit(GameObject target, Color color, float ttl = 0.4f)
        {
            if (target == null) return;
            target.GetComponent<QdaoBoySpriteAnimator>()?.PlayAction("hit");
            var rend = target.GetComponent<Renderer>();
            if (rend == null) return;
            target.AddComponent<Flasher>().Init(rend, color, ttl);
        }

        public static void PlayBeam(UnityEngine.Vector3 from, UnityEngine.Vector3 to, Color color, float ttl = 0.3f)
        {
            var go = new GameObject("Beam");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startWidth = lr.endWidth = 0.08f;
            lr.material = new Material(Shader.Find("Sprites/Default")) { color = color };
            Object.Destroy(go, ttl);
        }

        private sealed class RingDriver : MonoBehaviour
        {
            public float maxRadius = 2f;
            public float ttl = 0.6f;
            private float _t;
            private void Update()
            {
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(_t / ttl);
                transform.localScale = UnityEngine.Vector3.one * Mathf.Lerp(0.2f, maxRadius, k);
                var r = GetComponent<Renderer>();
                if (r != null)
                {
                    var c = r.material.color;
                    c.a = 0.5f * (1f - k);
                    r.material.color = c;
                }
                if (_t >= ttl) Destroy(gameObject);
            }
        }

        private sealed class Flasher : MonoBehaviour
        {
            private Renderer _rend;
            private Color _orig;
            private Color _flash;
            private float _ttl;
            private float _t;

            public void Init(Renderer r, Color flash, float ttl)
            {
                _rend = r;
                _orig = r.material.color;
                _flash = flash;
                _ttl = ttl;
            }

            private void Update()
            {
                if (_rend == null) { Destroy(this); return; }
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(_t / _ttl);
                _rend.material.color = Color.Lerp(_flash, _orig, k);
                if (_t >= _ttl) { _rend.material.color = _orig; Destroy(this); }
            }
        }
    }
}
