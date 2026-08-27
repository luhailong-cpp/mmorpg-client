using System.Collections.Generic;
using MmorpgClient.Game;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Local-player movement for Tianyong. Public destinations and network
    /// messages use feet/world coordinates. The actor root is the feet point;
    /// the CharacterController centre is offset upward by half its height.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongPlayerController : MonoBehaviour
    {
        private const float DefaultMoveSpeed = 9f;
        private const float DefaultHeight = 1.8f;
        private const float DefaultRadius = 0.38f;
        private const float DefaultStepOffset = 0.35f;
        private const float TurnSpeed = 14f;
        private const float Gravity = 20f;
        private const float FallLimit = -5f;

        private readonly List<Vector3> _path = new();
        private GameClient _client;
        private TianyongNavigationGrid _navigation;
        private Camera _camera;
        private CharacterController _motor;
        private int _waypoint;
        private bool _moving;
        private float _nextSyncAt;
        private float _moveSpeed = DefaultMoveSpeed;
        private float _arrivalDistance = 0.35f;
        private Vector3? _networkTarget;

        public CharacterController Motor => _motor;
        public bool IsMoving => _moving;

        /// <summary>The actor's authoritative ground/feet position in world space.</summary>
        public Vector3 FeetPosition
        {
            get
            {
                if (_motor == null)
                    return transform.position;
                return transform.TransformPoint(
                    _motor.center + Vector3.down * (_motor.height * 0.5f));
            }
        }

        public void Initialize(
            GameClient client,
            TianyongNavigationGrid navigation,
            Camera worldCamera)
            => Initialize(client, navigation, worldCamera, TianyongMapConfig.LoadDefault());

        public void Initialize(
            GameClient client,
            TianyongNavigationGrid navigation,
            Camera worldCamera,
            TianyongMapConfig config)
        {
            // Root position is the public/server feet coordinate. Do not infer
            // it from a prefab's pre-existing CharacterController settings;
            // Initialize is also responsible for normalising those settings.
            var initialFeet = transform.position;
            StopMoving();

            _client = client;
            _navigation = navigation;
            _camera = worldCamera;
            _moveSpeed = config != null ? config.MoveSpeed : DefaultMoveSpeed;

            foreach (var collider in GetComponents<Collider>())
            {
                if (collider is CharacterController) continue;
                collider.enabled = false;
                Destroy(collider);
            }

            _motor = GetComponent<CharacterController>();
            if (_motor == null) _motor = gameObject.AddComponent<CharacterController>();

            var height = config != null ? config.PlayerHeight : DefaultHeight;
            var radius = config != null ? config.PlayerRadius : DefaultRadius;
            var stepOffset = config != null ? config.PlayerStepOffset : DefaultStepOffset;
            _motor.height = Mathf.Max(0.5f, height);
            _motor.radius = Mathf.Clamp(radius, 0.1f, _motor.height * 0.49f);
            _motor.center = Vector3.up * (_motor.height * 0.5f);
            _motor.stepOffset = Mathf.Clamp(stepOffset, 0f, _motor.height * 0.49f);
            _motor.slopeLimit = 45f;
            _motor.skinWidth = Mathf.Clamp(_motor.radius * 0.15f, 0.02f, 0.1f);
            _motor.minMoveDistance = 0f;
            _arrivalDistance = Mathf.Max(0.25f, _motor.radius * 0.9f);

            if (_navigation != null && !_navigation.IsWalkable(initialFeet))
                initialFeet = TianyongMapDefinition.DefaultSpawn;
            WarpTo(initialFeet);
        }

        /// <summary>Immediately places the capsule so its feet land at the supplied world point.</summary>
        public void WarpTo(Vector3 feetPosition)
        {
            CancelPath(false);
            feetPosition = TianyongMapDefinition.ClampXZ(feetPosition, 2f);

            if (_motor == null)
            {
                transform.position = feetPosition;
                return;
            }

            var wasEnabled = _motor.enabled;
            if (wasEnabled) _motor.enabled = false;
            transform.position += feetPosition - FeetPosition;
            if (wasEnabled) _motor.enabled = true;
        }

        /// <summary>Routes to a feet/world destination. Returns false when no legal path exists.</summary>
        public bool SetDestination(Vector3 feetDestination)
        {
            if (_navigation == null) return false;

            feetDestination = TianyongMapDefinition.ClampXZ(feetDestination, 2f);
            var feet = FeetPosition;
            feetDestination.y = feet.y;

            _path.Clear();
            _path.AddRange(_navigation.FindPath(feet, feetDestination));
            _waypoint = _path.Count > 1 ? 1 : _path.Count;
            _networkTarget = _path.Count > 0 ? _path[^1] : (Vector3?)null;
            if (_waypoint < _path.Count) return true;

            StopMoving();
            return false;
        }

        private void Update()
        {
            if (_navigation == null || _motor == null) return;

            // Safety net: should the floor ever be missing under the motor,
            // put the actor back on the ground plane instead of falling away.
            if (FeetPosition.y < FallLimit)
            {
                var feet = FeetPosition;
                feet.y = 0f;
                WarpTo(feet);
            }

            var keyboardBlocked = GameplayInputGate.IsKeyboardBlocked;
            var pointerBlocked = GameplayInputGate.IsPointerBlocked;
            if (keyboardBlocked)
            {
                // Keep the authored path so movement may resume after an input
                // field/modal releases focus, but stop both motor and network
                // movement while UI owns the keyboard.
                StopMoving();
                return;
            }

            if (!pointerBlocked) HandleClick();
            var keyboard = ReadKeyboardDirection();
            if (keyboard.sqrMagnitude > 0.01f)
            {
                _path.Clear();
                _waypoint = 0;
                _networkTarget = null;
                MoveInDirection(keyboard.normalized, Time.deltaTime);
                return;
            }

            if (_waypoint < _path.Count)
            {
                var current = FeetPosition;
                var target = _path[_waypoint];
                target.y = current.y;
                var delta = target - current;
                delta.y = 0f;
                if (delta.sqrMagnitude < _arrivalDistance * _arrivalDistance)
                {
                    _waypoint++;
                    if (_waypoint >= _path.Count)
                    {
                        StopMoving();
                        return;
                    }

                    target = _path[_waypoint];
                    target.y = current.y;
                    delta = target - current;
                    delta.y = 0f;
                }

                MoveInDirection(delta.normalized, Time.deltaTime);
                return;
            }

            StopMoving();
        }

        private void HandleClick()
        {
            if (_camera == null || !Input.GetMouseButtonDown(0)) return;

            var feet = FeetPosition;
            var ray = _camera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, feet.y, 0f));
            if (!plane.Raycast(ray, out var distance)) return;
            var point = ray.GetPoint(distance);
            if (SetDestination(point))
                TianyongClickMarker.Spawn(_path[^1], _camera);
        }

        private Vector3 ReadKeyboardDirection()
        {
            if (_camera == null) return Vector3.zero;
            var horizontal = Input.GetAxisRaw("Horizontal");
            var vertical = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(horizontal) + Mathf.Abs(vertical) < 0.01f) return Vector3.zero;

            // "Screen up" on the ground: camera forward for the tilted camera,
            // camera up for the painted city's straight-down camera.
            var forward = _camera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = _camera.transform.up;
                forward.y = 0f;
            }
            var right = _camera.transform.right;
            right.y = 0f;
            if (forward.sqrMagnitude < 0.0001f || right.sqrMagnitude < 0.0001f) return Vector3.zero;
            return forward.normalized * vertical + right.normalized * horizontal;
        }

        private void MoveInDirection(Vector3 direction, float deltaTime)
        {
            if (direction.sqrMagnitude < 0.001f)
            {
                StopMoving();
                return;
            }

            var dt = Mathf.Min(Mathf.Max(deltaTime, 0f), 0.05f);
            var velocity = direction.normalized * _moveSpeed;
            var candidateFeet = TianyongMapDefinition.ClampXZ(FeetPosition + velocity * dt, 2f);
            if (!_navigation.IsWalkable(candidateFeet))
            {
                StopMoving();
                return;
            }

            var desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                1f - Mathf.Exp(-TurnSpeed * dt));
            _motor.Move(velocity * dt + Vector3.down * (Gravity * dt));

            if (!_moving)
            {
                _moving = true;
                if (_client != null && _client.InGame)
                    _client.SendMoveStart(FeetPosition, transform.eulerAngles, velocity, _networkTarget);
                _nextSyncAt = Time.unscaledTime + 0.25f;
            }
            else if (_client != null && _client.InGame && Time.unscaledTime >= _nextSyncAt)
            {
                _client.SendMoveSync(FeetPosition, transform.eulerAngles, velocity);
                _nextSyncAt = Time.unscaledTime + 0.25f;
            }
        }

        private void CancelPath(bool notifyServer)
        {
            _path.Clear();
            _waypoint = 0;
            _networkTarget = null;
            if (notifyServer) StopMoving();
            else _moving = false;
        }

        private void StopMoving()
        {
            if (!_moving) return;
            _moving = false;
            if (_client != null && _client.InGame)
                _client.SendMoveStop(FeetPosition, transform.eulerAngles);
        }

        private void OnDisable() => StopMoving();
    }
}
