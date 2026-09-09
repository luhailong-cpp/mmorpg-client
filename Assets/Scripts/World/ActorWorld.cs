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
        /// <summary>World-space nameplate (3D TextMeshPro) built by <see cref="WorldNameplate"/>.</summary>
        public TMPro.TMP_Text Label;

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
        private bool _hasLocal;
        // SetLocalPlayer was called for an entity that has not spawned yet;
        // SpawnActor promotes it to _hasLocal when that entity arrives.
        private bool _pendingLocal;
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
        /// Resolves the readable name shown on an actor's nameplate. Returning
        /// null/empty falls back to a kind label ("玩家" / "NPC" / "?"). Queried
        /// on spawn, on every recolor and again when the local player binding
        /// changes (the local entity is usually bound after its spawn).
        /// </summary>
        public System.Func<ActorView, string> DisplayNameProvider;

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

        /// <summary>
        /// True while a spawned actor is bound as the local player. Entity ids
        /// are raw entt handles from the server and 0 is a perfectly valid one
        /// (the first actor created on a fresh scene process), so "no local
        /// player" is tracked explicitly instead of as LocalEntity == 0.
        /// </summary>
        public bool HasLocalPlayer => _hasLocal;

        private bool IsLocal(ulong entity) => _hasLocal && entity == _localEntity;

        /// <summary>
        /// Binds <paramref name="entity"/> as the local player. GameClient
        /// spawns the actor first and binds afterwards; binding before the
        /// spawn is also allowed: the binding stays pending (HasLocalPlayer
        /// false, LocalEntity set) and SpawnActor promotes it, colouring the
        /// actor as local and raising <see cref="OnLocalPlayerChanged"/> then.
        /// </summary>
        public void SetLocalPlayer(ulong entity)
        {
            var hadLocal = _hasLocal;
            var previousLocal = _localEntity;
            _localEntity = entity;
            _hasLocal = _actors.ContainsKey(entity);
            _pendingLocal = !_hasLocal;

            if (hadLocal && previousLocal != entity &&
                _actors.TryGetValue(previousLocal, out var previous))
                Recolor(previous);

            if (_actors.TryGetValue(entity, out var v))
            {
                v.HasTarget = false;
                v.Velocity = UnityEngine.Vector3.zero;
                Recolor(v);
                OnLocalPlayerChanged?.Invoke(v);
            }
            else if (hadLocal)
            {
                OnLocalPlayerChanged?.Invoke(null);
            }
        }

        public void SpawnActor(ulong entity, ActorKind kind, ulong configId,
                       UnityEngine.Vector3 position, UnityEngine.Vector3 eulerDeg)
        {
            if (_actors.ContainsKey(entity)) return; // dedupe

            // A SetLocalPlayer that ran before this spawn lands now, so the
            // nameplate/primitive below are built with the local colours.
            var promotedToLocal = _pendingLocal && entity == _localEntity;
            if (promotedToLocal)
            {
                _pendingLocal = false;
                _hasLocal = true;
            }

            var prim = kind == ActorKind.Player
                ? GameObject.CreatePrimitive(PrimitiveType.Cube)
                : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            prim.name = $"{kind}#{entity}";
            prim.transform.SetParent(_root, false);
            prim.transform.localPosition = position;
            prim.transform.localEulerAngles = eulerDeg;

            var view = new ActorView
            {
                Entity = entity,
                Kind = kind,
                ConfigId = configId,
                Go = prim,
            };

            // Nameplate: readable name (never the dev "Player#123" text), parked
            // under the feet/contact shadow and always facing the world camera.
            view.Label = WorldNameplate.Create(prim.transform, ResolveDisplayName(view), NameplateColor(view));
            WorldLabelBillboard.Attach(view.Label.gameObject);

            // Players get the qdao sprite walker when its Resources are
            // present; the cube stays as a fallback (and in edit-mode tests,
            // which assert against the primitive's renderer).
            if (kind == ActorKind.Player && Application.isPlaying)
                QdaoBoySpriteAnimator.TryAttach(prim);

            _actors[entity] = view;
            Recolor(view);
            OnActorSpawned?.Invoke(view);
            if (promotedToLocal)
                OnLocalPlayerChanged?.Invoke(view);
        }

        /// <summary>
        /// Re-queries <see cref="DisplayNameProvider"/> for every actor, e.g.
        /// after the session's player list arrives later than the spawns.
        /// </summary>
        public void RefreshDisplayNames()
        {
            foreach (var v in _actors.Values)
                RefreshNameplate(v);
        }

        private string ResolveDisplayName(ActorView v)
        {
            string name = null;
            try
            {
                name = DisplayNameProvider?.Invoke(v);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
            if (!string.IsNullOrEmpty(name)) return name;
            return v.Kind switch
            {
                ActorKind.Player => "玩家",
                ActorKind.Npc => "NPC",
                _ => "?",
            };
        }

        private Color NameplateColor(ActorView v)
        {
            if (IsLocal(v.Entity)) return WorldNameplate.LocalPlayerColor;
            return v.Kind == ActorKind.Player ? WorldNameplate.RemotePlayerColor : WorldNameplate.NpcColor;
        }

        private void RefreshNameplate(ActorView v)
        {
            if (v == null || v.Label == null) return;
            v.Label.text = ResolveDisplayName(v);
            v.Label.color = NameplateColor(v);
        }

        public void DespawnActor(ulong entity)
        {
            if (!_actors.TryGetValue(entity, out var view)) return;
            DisableLocalMovement(view);
            DestroyActorObject(view.Go);
            _actors.Remove(entity);
            OnActorDespawned?.Invoke(entity);
            if (IsLocal(entity))
            {
                _hasLocal = false;
                _localEntity = 0;
                OnLocalPlayerChanged?.Invoke(null);
            }
        }

        public void Clear()
        {
            if (_hasLocal && _actors.TryGetValue(_localEntity, out var local))
                DisableLocalMovement(local);

            foreach (var v in _actors.Values)
                DestroyActorObject(v.Go);
            _actors.Clear();
            _hasLocal = false;
            _pendingLocal = false;
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
            if (IsLocal(entity)) return;
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
            var localMovement = IsLocal(entity)
                ? v.Go.GetComponent<TianyongPlayerController>()
                : null;
            if (localMovement != null)
            {
                // Protocol positions are feet coordinates; the Tianyong actor
                // transform is the capsule centre. WarpFromServer preserves that
                // pivot distinction, safely toggles its CharacterController and
                // recovers to the nearest walkable cell (reporting it back) if
                // the authoritative point is off the client walk mask, so a
                // correction can never strand the local actor.
                var worldFeet = _root != null ? _root.TransformPoint(pos) : pos;
                localMovement.WarpFromServer(worldFeet);
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
                if (IsLocal(v.Entity)) continue;
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
            // Nameplate text + colour follow the local/remote/NPC role too; the
            // local binding usually lands after SpawnActor, so this re-queries
            // DisplayNameProvider as well.
            RefreshNameplate(v);
            var rend = v.Go.GetComponent<Renderer>();
            if (rend == null) return;
            Color c;
            if (IsLocal(v.Entity))                   c = new Color(0.2f, 0.9f, 0.3f);
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
            if (view == null || !IsLocal(view.Entity) || view.Go == null) return;
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
