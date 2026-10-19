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
        private Vector3 _fallbackSpawn = TianyongMapDefinition.DefaultSpawn;
        private Camera _camera;
        private CharacterController _motor;
        private int _waypoint;
        private bool _moving;
        private float _nextSyncAt;
        // Heading and time of the last movement report, for the turn-triggered sync.
        private Vector3 _reportedDirection;
        private float _reportedAt = -100f;
        private const float SyncInterval = 0.25f;
        // A turn sharper than ~20 degrees is reported right away instead of waiting for the
        // next periodic sync: remote clients extrapolate along the last reported velocity, so
        // an unreported turn shows up there as up to SyncInterval * speed (2.25 units) of
        // running straight on, then a pull back. The minimum spacing keeps a continuously
        // curving walk at ~8 reports/s, inside the gate's MoveSync quota (20 per ~2 s window).
        private const float TurnSyncCosine = 0.94f;
        private const float TurnSyncMinInterval = 0.12f;
        private float _moveSpeed = DefaultMoveSpeed;
        private float _arrivalDistance = 0.35f;
        private Vector3? _networkTarget;
        private const float RecoveryBurstWindow = 2f;
        private const int RecoveryBurstLimit = 3;

        private Vector3 _debugDirection;
        private bool _debugIgnoreMask;
        private bool _ignoreLocalInput;
        private bool _pendingReport;
        private float _lastRecoveryAt = -100f;
        private int _recoveryBurst;
        private float _lastReplanAt = -100f;
        private int _replanBurst;
        private const int MaxReplansOnBlock = 3;
        private Vector3? _destination;
        private int _replanOnBlock;

        // ── 组队跟随(TeamFollowDriver 接管)────────────────────────────
        private const float FollowArrivalDistance = 0.03f;
        private const float MinFollowSpeedScale = 0.5f;
        private const float MaxFollowSpeedScale = 1.1f;
        private bool _followControlled;
        private Vector3? _followWaypoint;
        private float _followSpeedScale = 1f;
        private bool _followBlocked;

        public CharacterController Motor => _motor;
        public bool IsMoving => _moving;

        /// <summary>
        /// 组队跟随是否正在接管本地角色。为 true 时玩家自己的点击 / 键盘不再驱动角色(队员跟着队长走,
        /// 不能自由行动),移动改由 <see cref="FollowSteer"/> / <see cref="FollowRoute"/> /
        /// <see cref="FollowHold"/> 决定;走出去的每一步仍然过本地行走掩码、仍然照常上报服务器。
        /// 与验收用的 <see cref="SetScriptedInputOwner"/> 不同:跟随期间即使界面占着键盘(聊天打字、弹窗)
        /// 也继续走。脚本化方向(<see cref="SetDebugDirection"/>)优先级仍然最高。
        /// </summary>
        public bool FollowControlled => _followControlled;

        /// <summary>上一次 <see cref="FollowSteer"/> 的那一步是否被行走掩码拒绝(角色已停下)。</summary>
        public bool FollowBlocked => _followBlocked;

        /// <summary>跟随接管期间,玩家点了地面或按了方向键(每次点击 / 每帧按住各触发一次,订阅方自己限频)。</summary>
        public event System.Action FollowInputRejected;

        /// <summary>
        /// 交出或收回控制权。两个方向都会丢掉当前路径并在行走中时通知服务器停步:
        /// 接管时玩家自己点的目的地作废,归还时跟随走到一半的那一步作废。
        /// </summary>
        public void SetFollowControlled(bool controlled)
        {
            if (_followControlled == controlled) return;
            _followControlled = controlled;
            _followWaypoint = null;
            _followSpeedScale = 1f;
            _followBlocked = false;
            _destination = null;
            CancelPath(true);
        }

        /// <summary>
        /// 跟随:朝 feetWaypoint 直线走(不寻路)。调用方每帧给出轨迹上稍靠前的一点;走到该点即停,
        /// 不会冲过头。被行走掩码挡住时停下并置 <see cref="FollowBlocked"/>,由调用方改用
        /// <see cref="FollowRoute"/>。未接管时调用无效。
        /// </summary>
        public void FollowSteer(Vector3 feetWaypoint, float speedScale = 1f)
        {
            if (!_followControlled) return;
            // 从寻路切回直线跟随:丢掉路径但不发停步,人是连续在走的。
            _path.Clear();
            _waypoint = 0;
            _networkTarget = null;
            _destination = null;
            _followWaypoint = feetWaypoint;
            _followSpeedScale = Mathf.Clamp(speedScale, MinFollowSpeedScale, MaxFollowSpeedScale);
        }

        /// <summary>跟随:寻路去 feetDestination(离轨迹太远或直线被挡时用)。找不到路返回 false 并停下。</summary>
        public bool FollowRoute(Vector3 feetDestination)
        {
            if (!_followControlled) return false;
            _followWaypoint = null;
            _followBlocked = false;
            return SetDestination(feetDestination);
        }

        /// <summary>跟随:已到位,原地站定。</summary>
        public void FollowHold()
        {
            if (!_followControlled) return;
            _followWaypoint = null;
            _followBlocked = false;
            _destination = null;
            CancelPath(true);
        }

        /// <summary>
        /// Acceptance-only switch: when true, <see cref="MoveInDirection"/>
        /// skips the client walk-mask check so a scripted drive can push into
        /// a wall and prove that the *server* still blocks and corrects it
        /// (MoveAck). Never enable for real input.
        /// </summary>
        public void SetDebugIgnoreMask(bool ignore) => _debugIgnoreMask = ignore;

        /// <summary>
        /// Acceptance-only switch: when true, real local input (mouse clicks
        /// AND the keyboard/joystick movement axes) no longer moves the actor,
        /// so a scripted drive (TianyongSandboxAutoDrive) owns movement
        /// exclusively. SetDebugDirection and ClickAt keep working, so the
        /// drive still exercises the real motor and click-to-move paths.
        /// <para>
        /// Both halves are needed. A stray click while the player window takes
        /// focus walks the actor off its observation point; and a stray key is
        /// worse, because <see cref="ReadKeyboardDirection"/> outranks path
        /// following (it clears _path every frame it is non-zero), so a single
        /// held arrow key silently converts a click-to-move test into a
        /// free-walk test. That is not hypothetical: the 2026-09-08 acceptance
        /// run was captured while the operator was typing, the player window
        /// had focus, and every click-to-move frame in it showed the actor
        /// walking along Z while the ring sat correctly on the clicked point
        /// several units away. Gating only the mouse let that through.
        /// </para>
        /// </summary>
        public void SetScriptedInputOwner(bool owned) => _ignoreLocalInput = owned;

        /// <summary>The client-side walk mask this controller validates against (null before Initialize).</summary>
        public TianyongNavigationGrid Navigation => _navigation;

        /// <summary>
        /// End point of the path currently being walked, i.e. the destination
        /// the actor actually adopted, or null when it is not path-following.
        /// This is the exact point <see cref="ClickAt"/> drops the click ring
        /// on, so an acceptance run can assert "the ring never lies" straight
        /// from the log instead of measuring pixels.
        /// </summary>
        public Vector3? PathDestination => _waypoint < _path.Count ? _path[^1] : (Vector3?)null;

        /// <summary>
        /// Scripted movement input in world XZ (unit-length or zero). Non-zero
        /// overrides the keyboard exactly like a held WASD key and is not
        /// subject to the UI keyboard gate, so automated acceptance runs
        /// (DevAutoPilot -moveTest) can drive the real motor/network path
        /// without synthesising key presses. Zero restores keyboard control.
        /// </summary>
        public void SetDebugDirection(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            _debugDirection = worldDirection.sqrMagnitude > 0.0001f
                ? worldDirection.normalized
                : Vector3.zero;
        }

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
            TianyongMapConfig config,
            Vector3? fallbackSpawn = null)
        {
            // Root position is the public/server feet coordinate. Do not infer
            // it from a prefab's pre-existing CharacterController settings;
            // Initialize is also responsible for normalising those settings.
            var initialFeet = transform.position;
            StopMoving();

            _client = client;
            _navigation = navigation;
            _fallbackSpawn = ResolveFallbackSpawn(initialFeet, fallbackSpawn);
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
                initialFeet = _fallbackSpawn;
            WarpTo(initialFeet);
        }

        private Vector3 ResolveFallbackSpawn(Vector3 initialFeet, Vector3? requested)
        {
            var candidate = requested ?? (_navigation != null && _navigation.IsWalkable(initialFeet)
                ? initialFeet : TianyongMapDefinition.DefaultSpawn);
            candidate = TianyongMapDefinition.ClampXZ(candidate, 2f);
            if (_navigation == null || _navigation.IsWalkable(candidate)) return candidate;
            if (_navigation.TryFindNearestWalkable(candidate, out var nearest))
            {
                nearest = TianyongMapDefinition.ClampXZ(nearest, 2f);
                if (_navigation.IsWalkable(nearest)) return nearest;
            }
            throw new System.InvalidOperationException(
                $"[TianyongPlayerController] No walkable recovery spawn near {candidate}; supply this map's valid spawn.");
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

        /// <summary>
        /// Authoritative snap from the server (MoveAck reconcile / TeleportS2C).
        /// The server validates against the same walk mask, so the point is
        /// normally legal. If it is not (stale server navdata, a scene without
        /// navdata, or the map-edge clamp in <see cref="WarpTo"/>), the actor
        /// would otherwise be stranded where neither WASD nor click pathing can
        /// move it. Recover to the nearest walkable cell and report that point
        /// back with a MoveStop so the server adopts it (its movement handler
        /// snaps a reported point when its own position is off-mesh) instead of
        /// leaving the two sides disagreeing. Returns true when recovery ran.
        /// </summary>
        public bool WarpFromServer(Vector3 feetPosition)
        {
            // A correction while click-pathing must not silently drop the
            // player's destination (WarpTo clears the path): re-plan from the
            // corrected point once the warp is done.
            var destination = _waypoint < _path.Count ? _path[^1] : (Vector3?)null;
            WarpTo(feetPosition);
            var recovered = WarpFromServerCore();
            if (destination.HasValue)
            {
                // Corrections arriving faster than a few per second while
                // pathing mean client mask and server navmesh disagree about
                // this route; re-planning would just walk back into the
                // disagreement (2026-09-08: 4 corrections/s along a wall).
                // Drop the destination instead and let the player re-click.
                var now = Time.unscaledTime;
                _replanBurst = now - _lastReplanAt < RecoveryBurstWindow ? _replanBurst + 1 : 1;
                _lastReplanAt = now;
                if (_replanBurst <= RecoveryBurstLimit)
                    SetDestination(destination.Value);
                else if (_replanBurst == RecoveryBurstLimit + 1)
                    Debug.LogWarning(
                        $"[TianyongPlayerController] server keeps correcting the click path to {destination.Value}; dropping it");
            }
            return recovered;
        }

        private bool WarpFromServerCore()
        {
            if (_navigation == null || _navigation.IsWalkable(FeetPosition)) return false;

            var stranded = FeetPosition;
            if (!_navigation.TryFindNearestWalkable(stranded, out var recovered))
                recovered = _fallbackSpawn;
            recovered.y = stranded.y;
            WarpTo(recovered);

            // Loop breaker: if the server keeps answering our recovery report
            // with the same off-mask point (client mask and server navmesh
            // disagree about this spot), stop re-reporting after a few rounds
            // and stay on the client-legal point; the next real movement
            // report re-converges from there.
            var now = Time.unscaledTime;
            _recoveryBurst = now - _lastRecoveryAt < RecoveryBurstWindow ? _recoveryBurst + 1 : 1;
            _lastRecoveryAt = now;
            if (_recoveryBurst > RecoveryBurstLimit)
            {
                if (_recoveryBurst == RecoveryBurstLimit + 1)
                    Debug.LogError(
                        $"[TianyongPlayerController] server keeps snapping to non-walkable {stranded}; " +
                        $"giving up recovery reports (client mask and server navmesh disagree here)");
                return true;
            }
            Debug.LogWarning(
                $"[TianyongPlayerController] server snap {stranded} is not walkable; recovered to {FeetPosition}");
            ReportPositionToServer();
            return true;
        }

        /// <summary>
        /// Tells the server where the client actually placed the local actor
        /// (a MoveStop at the current feet point). Used after a client-side
        /// relocation so the authoritative position follows instead of the
        /// next movement report being judged from a stale server point.
        /// The self ActorCreate can be dispatched in the same poll as
        /// NotifyEnterScene, before GameClient flips InGame, so a report that
        /// cannot go out yet is deferred to the first Update where it can.
        /// </summary>
        public void ReportPositionToServer()
        {
            if (_client == null) return;
            _moving = false;
            if (!_client.InGame)
            {
                _pendingReport = true;
                return;
            }
            _pendingReport = false;
            _client.SendMoveStop(FeetPosition, transform.eulerAngles);
        }

        /// <summary>Routes to a feet/world destination. Returns false when no legal path exists.</summary>
        public bool SetDestination(Vector3 feetDestination)
        {
            _replanOnBlock = 0;
            return PlanPath(feetDestination);
        }

        private bool PlanPath(Vector3 feetDestination)
        {
            if (_navigation == null) return false;

            feetDestination = TianyongMapDefinition.ClampXZ(feetDestination, 2f);
            var feet = FeetPosition;
            feetDestination.y = feet.y;
            _destination = feetDestination;

            _path.Clear();
            _path.AddRange(_navigation.FindPath(feet, feetDestination));
            _waypoint = _path.Count > 1 ? 1 : _path.Count;
            _networkTarget = _path.Count > 0 ? _path[^1] : (Vector3?)null;
            if (_waypoint < _path.Count) return true;

            StopMoving();
            return false;
        }

        /// <summary>
        /// A path-following step was refused by the walk mask (the smoothed
        /// segment grazed a blocked cell and the capsule drifted into it).
        /// Re-plan from where the actor actually is instead of stalling in
        /// place; give up after a few attempts so a genuinely unreachable
        /// destination does not loop.
        /// </summary>
        private void HandleBlockedPathStep()
        {
            if (_destination.HasValue && _replanOnBlock < MaxReplansOnBlock)
            {
                _replanOnBlock++;
                if (PlanPath(_destination.Value)) return;
            }
            CancelPath(true);
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

            if (_pendingReport && _client != null && _client.InGame)
                ReportPositionToServer();

            var keyboardBlocked = GameplayInputGate.IsKeyboardBlocked;
            var pointerBlocked = GameplayInputGate.IsPointerBlocked;
            var scripted = _debugDirection.sqrMagnitude > 0.01f;
            // 组队跟随接管中:界面占着键盘也照走(边聊天边跟队),玩家自己的输入只触发提示。
            var following = _followControlled && !scripted;
            if (keyboardBlocked && !scripted && !following)
            {
                // Keep the authored path so movement may resume after an input
                // field/modal releases focus, but stop both motor and network
                // movement while UI owns the keyboard.
                StopMoving();
                return;
            }

            if (following) NoticeRejectedFollowInput(pointerBlocked, keyboardBlocked);
            else if (!pointerBlocked && !scripted && !_ignoreLocalInput) HandleClick();
            var keyboard = scripted ? _debugDirection
                : _ignoreLocalInput || following ? Vector3.zero
                : ReadKeyboardDirection();
            if (keyboard.sqrMagnitude > 0.01f)
            {
                _path.Clear();
                _waypoint = 0;
                _networkTarget = null;
                MoveInDirection(keyboard.normalized, Time.deltaTime);
                return;
            }

            if (following && _followWaypoint.HasValue)
            {
                FollowStep(Time.deltaTime);
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

                if (!MoveInDirection(delta.normalized, Time.deltaTime))
                    HandleBlockedPathStep();
                return;
            }

            StopMoving();
        }

        /// <summary>朝跟随转向点走一步;最后一步按剩余距离截短,正好停在点上。</summary>
        private void FollowStep(float deltaTime)
        {
            var current = FeetPosition;
            var target = _followWaypoint.Value;
            target.y = current.y;
            var delta = target - current;
            delta.y = 0f;
            var distance = delta.magnitude;
            if (distance < FollowArrivalDistance)
            {
                _followBlocked = false;
                StopMoving();
                return;
            }

            _followBlocked = !MoveInDirection(delta / distance, deltaTime, _followSpeedScale, distance);
        }

        private void NoticeRejectedFollowInput(bool pointerBlocked, bool keyboardBlocked)
        {
            if (_ignoreLocalInput || FollowInputRejected == null) return;
            var clicked = !pointerBlocked && _camera != null && Input.GetMouseButtonDown(0);
            var keyed = !keyboardBlocked && ReadKeyboardDirection().sqrMagnitude > 0.01f;
            if (clicked || keyed) FollowInputRejected.Invoke();
        }

        private void HandleClick()
        {
            if (_camera == null || !Input.GetMouseButtonDown(0)) return;

            var feet = FeetPosition;
            var ray = _camera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, feet.y, 0f));
            if (!plane.Raycast(ray, out var distance)) return;
            var point = ray.GetPoint(distance);
            ClickAt(point);
        }

        /// <summary>
        /// Click-to-move entry shared by the mouse and scripted drives
        /// (TianyongSandboxAutoDrive): routes to the ground point and drops
        /// the click marker on the destination actually adopted, i.e. the
        /// nearest legal cell when the click landed on a roof/canal, so the
        /// feedback never points somewhere the actor will not go.
        /// </summary>
        public bool ClickAt(Vector3 groundPoint)
        {
            if (!SetDestination(groundPoint)) return false;
            TianyongClickMarker.Spawn(_path[^1], _camera);
            return true;
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

        /// <summary>
        /// Returns false when the walk mask refused the step (the actor stopped).
        /// speedScale / maxDistance are only used by team follow: catch-up speed, and
        /// a last step shortened to land exactly on the waypoint. The defaults reproduce
        /// plain player movement.
        /// </summary>
        private bool MoveInDirection(Vector3 direction, float deltaTime, float speedScale = 1f,
            float maxDistance = float.PositiveInfinity)
        {
            if (direction.sqrMagnitude < 0.001f)
            {
                StopMoving();
                return false;
            }

            var dt = Mathf.Min(Mathf.Max(deltaTime, 0f), 0.05f);
            var velocity = direction.normalized * (_moveSpeed * speedScale);
            var step = velocity * dt;
            if (step.magnitude > maxDistance) step = direction.normalized * maxDistance;
            var candidateFeet = TianyongMapDefinition.ClampXZ(FeetPosition + step, 2f);
            if (!_debugIgnoreMask && !_navigation.IsWalkable(candidateFeet))
            {
                StopMoving();
                return false;
            }

            var desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                1f - Mathf.Exp(-TurnSpeed * dt));
            var before = FeetPosition;
            _motor.Move(step + Vector3.down * (Gravity * dt));
            // The CharacterController can slide a few centimetres off the
            // intended line (leftover collider edges, capsule skin). The mask
            // check above validated the intended step, not where the motor
            // actually ended up; if that is a cell the mask rejects, put the
            // feet back on the last legal point so a legal walk never reports
            // an illegal position (the server would correct it and the
            // correction would look like a random pull-back to the player).
            if (!_debugIgnoreMask && !_navigation.IsWalkable(FeetPosition))
            {
                var back = before;
                back.y = FeetPosition.y;
                PlaceFeet(back);
            }

            var reportNow = Time.unscaledTime;
            var heading = direction.normalized;
            if (!_moving)
            {
                _moving = true;
                if (_client != null && _client.InGame)
                    _client.SendMoveStart(FeetPosition, transform.eulerAngles, velocity, _networkTarget);
                _nextSyncAt = reportNow + SyncInterval;
                _reportedDirection = heading;
                _reportedAt = reportNow;
            }
            else if (_client != null && _client.InGame &&
                     (reportNow >= _nextSyncAt ||
                      (reportNow >= _reportedAt + TurnSyncMinInterval &&
                       Vector3.Dot(heading, _reportedDirection) < TurnSyncCosine)))
            {
                _client.SendMoveSync(FeetPosition, transform.eulerAngles, velocity);
                _nextSyncAt = reportNow + SyncInterval;
                _reportedDirection = heading;
                _reportedAt = reportNow;
            }
            return true;
        }

        /// <summary>Moves the feet without touching the current path or network state.</summary>
        private void PlaceFeet(Vector3 feetPosition)
        {
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

        private void OnDisable()
        {
            StopMoving();
            // 控制器被禁用(卸图 / 本地角色被替换)时跟随状态一并清掉:重新启用后由
            // TeamFollowDriver 重新接管,不带着上一张图的转向点继续走。
            _followControlled = false;
            _followWaypoint = null;
            _followBlocked = false;
        }
    }
}
