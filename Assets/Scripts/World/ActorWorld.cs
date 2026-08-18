using System.Collections.Generic;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>
    /// Per-actor visual state. Cheap GameObjects (cubes for players,
    /// cylinders for NPCs) parented under the world container. The local
    /// player is highlighted in green.
    /// </summary>
    public sealed class ActorView
    {
        public ulong Entity;
        public ActorKind Kind;
        public ulong ConfigId;
        public GameObject Go;
        public TextMesh Label;

        // Interpolation state. We snap on first sample, then linearly
        // interpolate from current transform toward _target* over
        // InterpDuration seconds. Server send rate is sparse (start / stop /
        // direction-change), so we extrapolate using _velocity in between.
        public UnityEngine.Vector3 TargetPos;
        public UnityEngine.Vector3 TargetEuler;
        public UnityEngine.Vector3 Velocity;     // m/s in Unity space
        public float   InterpStart;  // realtimeSinceStartup
        public float   InterpDuration;
        public UnityEngine.Vector3 InterpFromPos;
        public UnityEngine.Vector3 InterpFromEuler;
        public bool    HasTarget;
    }

    public enum ActorKind { Unknown = 0, Player = 1, Npc = 2 }

    /// <summary>
    /// Holds the live actor cache and renders one primitive per entity.
    /// Drives the visual layer reactively: <see cref="OnActorCreate"/>,
    /// <see cref="OnActorDestroy"/>, etc. are called from the dispatcher.
    /// </summary>
    public sealed class ActorWorld
    {
        private readonly Dictionary<ulong, ActorView> _actors = new();
        private readonly string _rootName;
        private UnityEngine.Transform _root;
        private ulong _localEntity;
        private readonly MaterialPropertyBlock _colorProperties = new();
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        public ActorWorld(string rootName = "[ActorWorld]", UnityEngine.Transform rootParent = null)
        {
            _rootName = rootName;
            var go = GameObject.Find(rootName) ?? new GameObject(rootName);
            _root = go.transform;
            // A pre-existing root may already live under the persistent
            // AppRoot. A null argument means "leave ownership unchanged",
            // not "detach it back into the active scene".
            if (rootParent != null)
                SetRootParent(rootParent);
        }

        public IReadOnlyDictionary<ulong, ActorView> Actors => _actors;
        public ulong LocalEntity => _localEntity;
        public UnityEngine.Transform Root => _root;

        public event System.Action<ActorView> OnActorSpawned;
        public event System.Action<ulong> OnActorDespawned;
        public event System.Action<ActorView> OnLocalPlayerChanged;

        /// <summary>
        /// Places the actor container under the persistent application root.
        /// Network positions remain actor-local coordinates, so the default
        /// path normalizes the root transform after reparenting.
        /// </summary>
        public void SetRootParent(UnityEngine.Transform parent, bool worldPositionStays = false)
        {
            if (_root == null)
                _root = new GameObject(_rootName).transform;

            _root.SetParent(parent, worldPositionStays);
            if (worldPositionStays) return;
            _root.localPosition = UnityEngine.Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = UnityEngine.Vector3.one;
        }

        public void SetLocalPlayer(ulong entity)
        {
            var previousLocal = _localEntity;
            _localEntity = entity;

            if (previousLocal != 0 && previousLocal != entity &&
                _actors.TryGetValue(previousLocal, out var previous))
                Recolor(previous);

            if (_actors.TryGetValue(entity, out var v))
            {
                v.HasTarget = false;
                v.Velocity = UnityEngine.Vector3.zero;
                Recolor(v);
                OnLocalPlayerChanged?.Invoke(v);
            }
            else if (entity == 0)
            {
                OnLocalPlayerChanged?.Invoke(null);
            }
        }

        public void SpawnActor(ulong entity, ActorKind kind, ulong configId,
                       UnityEngine.Vector3 position, UnityEngine.Vector3 eulerDeg)
        {
            if (_actors.ContainsKey(entity)) return; // dedupe
            var prim = kind == ActorKind.Player
                ? GameObject.CreatePrimitive(PrimitiveType.Cube)
                : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            prim.name = $"{kind}#{entity}";
            prim.transform.SetParent(_root, false);
            prim.transform.localPosition = position;
            prim.transform.localEulerAngles = eulerDeg;

            // Floating label
            var labelGo = new GameObject("label");
            labelGo.transform.SetParent(prim.transform, false);
            labelGo.transform.localPosition = new UnityEngine.Vector3(0, 1.2f, 0);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = $"{kind}#{entity}";
            tm.characterSize = 0.08f;
            tm.fontSize = 32;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;

            var view = new ActorView
            {
                Entity = entity,
                Kind = kind,
                ConfigId = configId,
                Go = prim,
                Label = tm,
            };
            _actors[entity] = view;
            Recolor(view);
            OnActorSpawned?.Invoke(view);
        }

        public void DespawnActor(ulong entity)
        {
            if (!_actors.TryGetValue(entity, out var view)) return;
            DisableLocalMovement(view);
            DestroyActorObject(view.Go);
            _actors.Remove(entity);
            OnActorDespawned?.Invoke(entity);
            if (_localEntity == entity)
            {
                _localEntity = 0;
                OnLocalPlayerChanged?.Invoke(null);
            }
        }

        public void Clear()
        {
            if (_localEntity != 0 && _actors.TryGetValue(_localEntity, out var local))
                DisableLocalMovement(local);

            foreach (var v in _actors.Values)
                DestroyActorObject(v.Go);
            _actors.Clear();
            _localEntity = 0;
            OnLocalPlayerChanged?.Invoke(null);
        }

        public bool TryGetActor(ulong entity, out ActorView view)
            => _actors.TryGetValue(entity, out view);

        /// <summary>
        /// Apply a server <c>ActorMoveS2C</c>: snap target, start a short
        /// interpolation from the current transform so movement looks smooth
        /// even if the server's broadcast rate is sparse.
        /// </summary>
        public void ApplyMove(ulong entity, UnityEngine.Vector3 targetPos, UnityEngine.Vector3 targetEuler,
                      UnityEngine.Vector3 velocity, float interpDuration = 0.15f)
        {
            if (!_actors.TryGetValue(entity, out var v) || v.Go == null) return;
            // The local actor is driven by CharacterController prediction.
            // Authoritative corrections arrive through Teleport/MoveAck; using
            // the remote interpolation path here would race the local motor.
            if (entity == _localEntity) return;
            v.InterpFromPos   = v.Go.transform.localPosition;
            v.InterpFromEuler = v.Go.transform.localEulerAngles;
            v.TargetPos       = targetPos;
            v.TargetEuler     = targetEuler;
            v.Velocity        = velocity;
            v.InterpStart     = Time.realtimeSinceStartup;
            v.InterpDuration  = Mathf.Max(0.001f, interpDuration);
            v.HasTarget       = true;
        }

        /// <summary>
        /// Server forced snap (teleport / anti-cheat correction). Skips
        /// interpolation entirely.
        /// </summary>
        public void Teleport(ulong entity, UnityEngine.Vector3 pos, UnityEngine.Vector3 euler)
        {
            if (!_actors.TryGetValue(entity, out var v) || v.Go == null) return;
            var localMovement = entity == _localEntity
                ? v.Go.GetComponent<TianyongPlayerController>()
                : null;
            if (localMovement != null)
            {
                // Protocol positions are feet coordinates; the Tianyong actor
                // transform is the capsule centre. WarpTo preserves that pivot
                // distinction and safely toggles its CharacterController.
                var worldFeet = _root != null ? _root.TransformPoint(pos) : pos;
                localMovement.WarpTo(worldFeet);
                v.Go.transform.localEulerAngles = euler;
            }
            else
            {
                var motor = v.Go.GetComponent<CharacterController>();
                var restoreMotor = motor != null && motor.enabled;
                if (restoreMotor) motor.enabled = false;
                try
                {
                    v.Go.transform.localPosition = pos;
                    v.Go.transform.localEulerAngles = euler;
                }
                finally
                {
                    if (restoreMotor && motor != null) motor.enabled = true;
                }
            }
            v.TargetPos = pos;
            v.TargetEuler = euler;
            v.Velocity = UnityEngine.Vector3.zero;
            v.HasTarget = false;
        }

        /// <summary>
        /// Drive interpolation + extrapolation. Call from a MonoBehaviour
        /// Update once per frame. Frame-rate independent.
        /// </summary>
        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            float dt  = Time.deltaTime;
            foreach (var v in _actors.Values)
            {
                if (v.Entity == _localEntity) continue;
                if (!v.HasTarget || v.Go == null) continue;
                float t = (now - v.InterpStart) / v.InterpDuration;
                if (t < 1f)
                {
                    v.Go.transform.localPosition = UnityEngine.Vector3.Lerp(v.InterpFromPos, v.TargetPos, t);
                    v.Go.transform.localEulerAngles = LerpEuler(v.InterpFromEuler, v.TargetEuler, t);
                }
                else
                {
                    // Past the interp window: dead-reckon with last velocity.
                    v.TargetPos += v.Velocity * dt;
                    v.Go.transform.localPosition    = v.TargetPos;
                    v.Go.transform.localEulerAngles = v.TargetEuler;
                }
            }
        }

        private static UnityEngine.Vector3 LerpEuler(UnityEngine.Vector3 a, UnityEngine.Vector3 b, float t)
            => new(Mathf.LerpAngle(a.x, b.x, t),
                   Mathf.LerpAngle(a.y, b.y, t),
                   Mathf.LerpAngle(a.z, b.z, t));

        private void Recolor(ActorView v)
        {
            if (v.Go == null) return;
            var rend = v.Go.GetComponent<Renderer>();
            if (rend == null) return;
            Color c;
            if (v.Entity == _localEntity)            c = new Color(0.2f, 0.9f, 0.3f);
            else if (v.Kind == ActorKind.Player)     c = new Color(0.3f, 0.5f, 0.95f);
            else                                     c = new Color(0.85f, 0.55f, 0.2f);

            // Per-renderer overrides preserve the shared primitive material and
            // avoid allocating a new Material on every recolor/respawn.
            rend.GetPropertyBlock(_colorProperties);
            _colorProperties.SetColor(ColorProperty, c);
            _colorProperties.SetColor(BaseColorProperty, c);
            rend.SetPropertyBlock(_colorProperties);
            _colorProperties.Clear();
        }

        private void DisableLocalMovement(ActorView view)
        {
            if (view == null || view.Entity != _localEntity || view.Go == null) return;
            var movement = view.Go.GetComponent<TianyongPlayerController>();
            if (movement != null && movement.enabled)
                movement.enabled = false;
        }

        private static void DestroyActorObject(GameObject actor)
        {
            if (actor == null) return;
            if (Application.isPlaying) Object.Destroy(actor);
            else Object.DestroyImmediate(actor);
        }
    }
}
