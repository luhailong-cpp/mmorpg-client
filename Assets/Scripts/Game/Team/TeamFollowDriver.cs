using System;
using System.Collections.Generic;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.Game.Team
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>界面要显示的跟随状态。</summary>
    public enum TeamFollowStatus
    {
        /// <summary>不需要跟随:没有队伍、自己是队长,或眼下不允许跟随(未进游戏 / 战斗中 / 队伍视图还没拿到)。</summary>
        None,
        /// <summary>正在跟随队长,本地输入被接管。</summary>
        Following,
        /// <summary>玩家选择了暂离:仍在队里,但自己走。</summary>
        Away,
        /// <summary>在队里也没暂离,但眼下看不到队长(不在同一张图 / 同一条线,或离得太远):暂时自己走。</summary>
        LeaderOutOfSight,
    }

    /// <summary>
    /// 组队跟随的本地驱动:队员的客户端自己把角色走到"队长身后第 N 个"的位置上,照常上报移动,
    /// 服务端照常裁决。每个客户端只驱动自己这一个角色;别的队员在本机上的位置来自他们各自上报的移动。
    ///
    /// 每帧:从队伍快照算出自己的槽位 → 在场景里找到队长的角色 → 把队长的新位置记进轨迹 →
    /// <see cref="TeamFollowPlanner"/> 给出这一帧的指令 → 交给本地 <see cref="TianyongPlayerController"/>。
    /// 找不到队长、暂离、被要求停手(战斗中 / 未进游戏)时把控制权还给玩家。
    ///
    /// 暂离是纯本地状态,服务端不知道:换图时照样会被队长带走(服务端按队伍拉人),只是到了之后不排队。
    /// 只有确认换了队或离了队才复位;断线重连、跨区重定向期间队伍视图暂时拿不到,不算换队。
    /// 只在主线程使用;不持有任何需要释放的资源,丢弃前调一次 <see cref="Release"/> 即可。
    /// </summary>
    public sealed class TeamFollowDriver
    {
        /// <summary>
        /// 轨迹保留长度。整队 4 个槽位只占 5.6,余量留给追赶:队员卡住或切到后台时队长照走,
        /// 只要还没走出这么远,回来后沿原路追上,不必寻路。
        /// </summary>
        public const float TrailLength = 80f;
        /// <summary>队长相邻两帧位置差超过它视为瞬移(被服务端带走 / 纠偏),轨迹作废重起。</summary>
        public const float LeaderJumpDistance = 6f;
        /// <summary>队长的速度平方小于它视为站定(远端角色停步后速度被置为精确的 0)。</summary>
        public const float LeaderStandingSpeedSquared = 0.0001f;
        /// <summary>寻路模式下,人还在走时两次重新规划的最小间隔。</summary>
        public const float RouteReplanSeconds = 0.5f;
        /// <summary>寻路失败(找不到路)后再试的间隔。失败的 A* 会扫完整个连通区,不能半秒一次。</summary>
        public const float RouteFailedBackoffSeconds = 2f;
        /// <summary>寻路目标(自己的槽位)挪动超过它才值得重新规划;队长站着不动时不重算。</summary>
        public const float RouteRetargetDistance = 0.5f;
        /// <summary>直线跟随被掩码挡住后,改用寻路的持续时间。</summary>
        public const float BlockedRouteSeconds = 0.75f;

        private readonly TeamFollowTrail _trail = new TeamFollowTrail(TrailLength);
        private readonly List<Vector3> _seedTail = new List<Vector3>();
        private TianyongPlayerController _controller;
        private ulong _teamId;
        private ulong _sceneId;
        private ulong _leaderId;
        private ulong _leaderEntity;
        private bool _hasLeaderEntity;
        // 有一段时间没有在记轨迹(暂离、战斗、视图未知):期间队长可能绕过了墙角,
        // 旧队首到他现在的位置之间不能直接连一条线。
        private bool _trailStale;
        private bool _routing;
        private bool _routeFound;
        private Vector3 _routeTarget;
        private float _nextRouteAt;
        private float _forceRouteUntil;

        public TeamFollowStatus Status { get; private set; }

        /// <summary>玩家是否选择了暂离(只在有队且非队长时有意义)。</summary>
        public bool Away { get; private set; }

        /// <summary>跟随中玩家试图自己走(点地面 / 按方向键)。可能每帧触发,订阅方自己限频。</summary>
        public event Action InputRejected;

        public void SetAway(bool away)
        {
            if (Away == away) return;
            Away = away;
            if (away) Suspend();
        }

        /// <summary>
        /// 每帧调用一次。
        /// </summary>
        /// <param name="game">当前会话;null 等同于不允许跟随。</param>
        /// <param name="snapshot">
        /// 已经应用过权威视图的队伍快照;还没有权威视图(刚连上、重连中)时传 null,表示"不知道",
        /// 此时只放手,不把它当成离队(暂离选择保留)。
        /// </param>
        /// <param name="allowed">此刻是否允许跟随移动(已进游戏、不在战斗画面)。false 时归还控制权但保留暂离选择。</param>
        /// <param name="now">单调时钟秒数(Time.realtimeSinceStartup)。</param>
        public void Tick(GameClient game, TeamSnapshot snapshot, bool allowed, float now)
        {
            if (snapshot == null || game == null)
            {
                Suspend();
                Status = TeamFollowStatus.None;
                return;
            }

            if (snapshot.TeamId != _teamId)
            {
                // 确认换队 / 离队:暂离与上一支队伍的轨迹都不带过去。
                _teamId = snapshot.TeamId;
                Away = false;
                ForgetLeader();
            }
            if (game.CurrentSceneId != _sceneId)
            {
                // 换了场景实例:坐标系变了,旧轨迹不能接着用(实体号在新场景里可能恰好没变)。
                _sceneId = game.CurrentSceneId;
                ForgetLeader();
            }

            int slot = allowed ? TeamFollowPlanner.SlotOf(snapshot, game.PlayerId) : 0;
            if (slot == 0)
            {
                Suspend();
                Status = TeamFollowStatus.None;
                return;
            }
            if (Away)
            {
                Suspend();
                Status = TeamFollowStatus.Away;
                return;
            }

            var world = game.World;
            var controller = ResolveLocalController(world);
            var leader = controller != null ? ResolveLeader(world, snapshot.LeaderId) : null;
            if (leader == null)
            {
                // 看不到队长:没有可跟的目标,让玩家自己走;轨迹作废,再见到队长时重起。
                Release();
                ForgetLeader();
                Status = TeamFollowStatus.LeaderOutOfSight;
                return;
            }

            var navigation = controller.Navigation;
            Vector3 leaderPosition = leader.Go.transform.position;
            float moved = _trail.HasHead ? Horizontal(leaderPosition, _trail.Head) : float.PositiveInfinity;
            if (moved > LeaderJumpDistance || (_trailStale && moved > TeamFollowTrail.SampleSpacing))
            {
                SeedTrail(leaderPosition, leader.Go.transform.forward, navigation);
            }
            else
            {
                // 只有队长站定时才允许把轨迹末端收回来:那是停步后显示位置被拉回停点。
                // 队长带着速度往回走是真的掉头,轨迹要原样留着(折成发夹),队员才会跟在他后面而不是被赶到前面。
                _trail.Push(leaderPosition, leader.Velocity.sqrMagnitude < LeaderStandingSpeedSquared);
            }
            _trailStale = false;

            Bind(controller);
            Apply(controller, Plan(controller, slot, now), now);
            Status = TeamFollowStatus.Following;
        }

        /// <summary>把控制权还给玩家(幂等)。不清暂离,也不清轨迹。</summary>
        public void Release()
        {
            _routing = false;
            _forceRouteUntil = 0f;
            if (_controller == null) return;
            // Unity 重载的 == 在对象已销毁时为 true,上面那句已经拦住;这里一定是活对象。
            _controller.FollowInputRejected -= HandleInputRejected;
            _controller.SetFollowControlled(false);
            _controller = null;
        }

        /// <summary>放手,并记下"这段时间没有在记轨迹"。</summary>
        private void Suspend()
        {
            Release();
            _trailStale = true;
        }

        private TeamFollowCommand Plan(TianyongPlayerController controller, int slot, float now)
        {
            var navigation = controller.Navigation;
            Vector3 self = controller.FeetPosition;
            bool moving = controller.IsMoving;
            float slotDistance = Mathf.Min(slot, TeamFollowPlanner.MaxFollowers) * TeamFollowPlanner.SlotSpacing;
            Vector3 slotPoint = _trail.PointBehindHead(slotDistance);

            // 槽位落在不可走的格子里(铺的尾巴被墙截断之外的情况:队长的显示位置在拐角处外推过头、
            // 暂离回来接上的轨迹擦过建筑):直线跟随会在格子边界被拒、寻路又会把目标改成最近的可走格,
            // 两边目的地不一致,人就在边界和格心之间来回踱步。统一成"最近可走点",只做到没到 / 寻路过去。
            if (!navigation.IsWalkable(slotPoint))
            {
                return navigation.TryFindNearestWalkable(slotPoint, out var reachable)
                    ? TeamFollowPlanner.PlanToPoint(reachable, self, moving)
                    : new TeamFollowCommand(TeamFollowCommandKind.Hold);
            }

            var command = TeamFollowPlanner.Plan(_trail, slot, self, moving);
            // 上一步直线跟随被掩码挡住(轨迹是按远端角色的显示位置记的,拐角处可能擦到不可走格):
            // 接下来一小段时间改用寻路去槽位,绕过去之后再回到直线跟随。
            if (controller.FollowBlocked) _forceRouteUntil = now + BlockedRouteSeconds;
            if (now < _forceRouteUntil && command.Kind == TeamFollowCommandKind.Steer)
                command = new TeamFollowCommand(TeamFollowCommandKind.Route, slotPoint);
            return command;
        }

        private void Apply(TianyongPlayerController controller, TeamFollowCommand command, float now)
        {
            switch (command.Kind)
            {
                case TeamFollowCommandKind.Steer:
                    _routing = false;
                    controller.FollowSteer(command.Point, command.SpeedScale);
                    break;
                case TeamFollowCommandKind.Route:
                    // 寻路是整张格子图上的 A*(每次要分配并清空整张图大小的数组),不能每帧算。
                    //   第一次:立刻算。
                    //   上一条路已经走完、目标又挪了(队长还在走):立刻接着算,不然会走走停停。
                    //   其余(人还在走而目标挪了 / 人停着而目标没动 / 上次没找到路):等节流到期。
                    bool idle = !controller.IsMoving;
                    bool targetMoved = Horizontal(command.Point, _routeTarget) > RouteRetargetDistance;
                    if (!_routing ||
                        (idle && targetMoved && _routeFound) ||
                        (now >= _nextRouteAt && (idle || targetMoved)))
                    {
                        _routeFound = controller.FollowRoute(command.Point);
                        _routing = true;
                        _routeTarget = command.Point;
                        _nextRouteAt = now + (_routeFound ? RouteReplanSeconds : RouteFailedBackoffSeconds);
                    }
                    break;
                default:
                    _routing = false;
                    controller.FollowHold();
                    break;
            }
        }

        private void Bind(TianyongPlayerController controller)
        {
            if (!ReferenceEquals(_controller, controller))
            {
                Release();
                _controller = controller;
                controller.FollowInputRejected += HandleInputRejected;
            }
            // 每帧重申:控制器被禁用再启用(重载地图)时会自己清掉接管标记。已接管时是空操作。
            controller.SetFollowControlled(true);
        }

        private void HandleInputRejected() => InputRejected?.Invoke();

        private static TianyongPlayerController ResolveLocalController(ActorWorld world)
        {
            if (world == null || !world.HasLocalPlayer) return null;
            if (!world.TryGetActor(world.LocalEntity, out var local) || local.Go == null) return null;
            var controller = local.Go.GetComponent<TianyongPlayerController>();
            // 没有导航 = 地图还没装好(或这张图客户端不认识):不接管。
            return controller != null && controller.enabled && controller.Navigation != null ? controller : null;
        }

        private ActorView ResolveLeader(ActorWorld world, ulong leaderId)
        {
            if (leaderId == 0) return null;
            if (_leaderId != leaderId) ForgetLeader();
            _leaderId = leaderId;

            if (_hasLeaderEntity && world.TryGetActor(_leaderEntity, out var cached) &&
                cached.PlayerId == leaderId && cached.Go != null)
                return cached;

            // 实体号变了(队长换图后重新进视野)或第一次找:旧轨迹属于上一个实体,作废。
            _hasLeaderEntity = false;
            _trail.Clear();
            foreach (var view in world.Actors.Values)
            {
                if (view == null || view.Kind != ActorKind.Player || view.PlayerId != leaderId || view.Go == null) continue;
                _leaderEntity = view.Entity;
                _hasLeaderEntity = true;
                return view;
            }
            return null;
        }

        private void ForgetLeader()
        {
            _leaderId = 0;
            _hasLeaderEntity = false;
            _trail.Clear();
        }

        /// <summary>
        /// 以队长当前位置起一条新轨迹,并在他背后铺一段直线尾巴,让队员直接排到身后。
        /// 尾巴一格一格往后探,碰到不可走的格子就停:队长背靠墙时后排的人叠在最后一个可走点上,
        /// 队长一走出去就自然拉开。
        /// </summary>
        private void SeedTrail(Vector3 leaderPosition, Vector3 leaderForward, TianyongNavigationGrid navigation)
        {
            _seedTail.Clear();
            leaderForward.y = 0f;
            if (leaderForward.sqrMagnitude > 0.0001f)
            {
                Vector3 back = -leaderForward.normalized;
                float needed = TeamFollowPlanner.MaxFollowers * TeamFollowPlanner.SlotSpacing;
                for (float distance = TeamFollowTrail.SampleSpacing; distance <= needed + 0.001f;
                     distance += TeamFollowTrail.SampleSpacing)
                {
                    Vector3 point = leaderPosition + back * distance;
                    if (!navigation.IsWalkable(point)) break;
                    _seedTail.Add(point);
                }
            }
            _trail.Reset(leaderPosition, _seedTail);
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
