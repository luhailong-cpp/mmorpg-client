using System;
using System.Globalization;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;

namespace MmorpgClient.Game.Team
{
    /// <summary>界面要显示的跟随状态。</summary>
    public enum TeamFollowStatus
    {
        /// <summary>不需要跟随:没有队伍、自己是队长,或眼下不允许跟随(未进游戏 / 战斗中 / 队伍视图还没拿到)。</summary>
        None,
        /// <summary>服务器正带着自己跟在队长身后,本地输入不驱动角色。</summary>
        Following,
        /// <summary>玩家选择了暂离:仍在队里,但自己走。</summary>
        Away,
        /// <summary>在队里也没暂离,但服务器眼下没在带着走(队长不在这个场景 / 刚换完图还没接上):暂时自己走。</summary>
        LeaderOutOfSight,
    }

    /// <summary>
    /// 组队跟随的客户端一侧(服务端 docs/design/team-follow-server-trail.md §5):只决定要不要跟、显示服务器给的状态。
    /// 走位全在服务器 —— 声明跟随之后,服务器把自己接到队长身后,沿队长走过的路带着走,同动作、同间隔;
    /// 发给自己的 ActorMoveS2C 由 ActorWorld 按远端角色同一套插值显示,本地控制器不按输入走、不上报移动。
    /// 这些切换由 GameClient 收到 TeamFollowStateS2C 时完成(<see cref="GameClient.TeamFollowDriven"/>),本类不碰角色位置。
    ///
    /// 要不要跟:在队里、不是队长、没暂离就跟(与改版前"入队即跟随"一致);何时发声明由 <see cref="TeamFollowDeclaration"/> 定,
    /// 声明经 <see cref="GameClient.SendTeamFollow"/> 发出。暂离是玩家的选择,只记在本地,换队 / 离队时复位;
    /// 断线重连、跨区重定向期间队伍视图暂时拿不到,不算换队。服务器自己开的跟随(挂机月卡托管开始时)照单全收:
    /// 状态显示为跟随中,玩家想自己走就点「暂离」。
    /// 只在主线程使用;丢弃前调一次 <see cref="Release"/>。
    /// </summary>
    public sealed class TeamFollowDriver
    {
        private readonly TeamFollowDeclaration _declaration = new TeamFollowDeclaration();
        private TianyongPlayerController _controller;
        private ulong _teamId;

        public TeamFollowStatus Status { get; private set; }

        /// <summary>
        /// 诊断输出(联机验收排障用)。非 null 时:状态变化与每次发出的声明各写一行。为 null(缺省)时不拼任何字符串。
        /// 宿主决定接到哪里(见 TeamFollowUiRoot)。
        /// </summary>
        public Action<string> Trace;
        private string _traceKey;

        /// <summary>玩家是否选择了暂离(只在有队且非队长时有意义)。</summary>
        public bool Away { get; private set; }

        /// <summary>被服务器带着走时玩家试图自己走(点地面 / 按方向键)。可能每帧触发,订阅方自己限频。</summary>
        public event Action InputRejected;

        public void SetAway(bool away)
        {
            if (Away == away) return;
            Away = away;
            _declaration.AwayChanged(away);
        }

        /// <summary>每帧调用一次。</summary>
        /// <param name="game">当前会话;null 等同于不允许跟随。</param>
        /// <param name="snapshot">
        /// 已经应用过权威视图的队伍快照;还没有权威视图(刚连上、重连中)时传 null,表示"不知道":
        /// 不发声明,也不把它当成离队(暂离选择保留)。
        /// </param>
        /// <param name="allowed">此刻是否允许跟随(已进游戏、不在战斗画面)。false 时不发声明,保留暂离选择。</param>
        /// <param name="now">单调时钟秒数(Time.realtimeSinceStartup)。</param>
        public void Tick(GameClient game, TeamSnapshot snapshot, bool allowed, float now)
        {
            BindInputHint(game?.World);
            if (snapshot == null || game == null)
            {
                Status = TeamFollowStatus.None;
                TraceState(now, "no trusted team view", false);
                return;
            }
            if (snapshot.TeamId != _teamId)
            {
                // 确认换队 / 离队:暂离与声明记账都不带过去。服务器在成员关系变了时自己停止驱动。
                _teamId = snapshot.TeamId;
                Away = false;
                _declaration.Reset();
            }

            bool driven = game.TeamFollowDriven;
            var wish = !allowed || !TeamFollowDeclaration.IsFollower(snapshot, game.PlayerId) ? TeamFollowWish.None
                : Away ? TeamFollowWish.Away
                : TeamFollowWish.Follow;
            var declare = _declaration.Decide(wish, driven, LeaderOnline(snapshot), game.SceneEntryEpoch, now);
            if (declare.HasValue && game.SendTeamFollow(declare.Value))
            {
                _declaration.Sent(declare.Value, game.SceneEntryEpoch, now);
                if (Trace != null)
                    Trace(string.Format(CultureInfo.InvariantCulture, "t={0:F2} declared follow={1} entry={2} driven={3}",
                        now, declare.Value, game.SceneEntryEpoch, driven));
            }

            Status = wish switch
            {
                TeamFollowWish.Follow => driven ? TeamFollowStatus.Following : TeamFollowStatus.LeaderOutOfSight,
                TeamFollowWish.Away => TeamFollowStatus.Away,
                _ => TeamFollowStatus.None,
            };
            TraceState(now, wish.ToString(), driven);
        }

        /// <summary>解开对本地控制器的订阅(幂等)。不清暂离。</summary>
        public void Release()
        {
            var controller = _controller;
            _controller = null;
            // 控制器所在的物体可能已被销毁(Unity 的 == 此时为 true):事件挂在托管对象上,照样能摘。
            if ((object)controller != null) controller.FollowInputRejected -= HandleInputRejected;
        }

        /// <summary>跟着本地角色换控制器(换图 / 本地角色重建),把"跟随中自己走不了"的提示接上。</summary>
        private void BindInputHint(ActorWorld world)
        {
            var controller = ResolveLocalController(world);
            if (ReferenceEquals(_controller, controller)) return;
            Release();
            if (controller == null) return;
            _controller = controller;
            controller.FollowInputRejected += HandleInputRejected;
        }

        private void HandleInputRejected() => InputRejected?.Invoke();

        private void TraceState(float now, string wish, bool driven)
        {
            if (Trace == null) return;
            string key = Status + "/" + wish + "/" + driven;
            if (key == _traceKey) return;
            _traceKey = key;
            Trace(string.Format(CultureInfo.InvariantCulture, "t={0:F2} status={1} wish={2} driven={3}", now, Status, wish, driven));
        }

        /// <summary>队伍快照里队长在线;找不到队长那一行(视图未知 / 不在队里)按不在线。</summary>
        private static bool LeaderOnline(TeamSnapshot snapshot)
        {
            if (snapshot == null || snapshot.LeaderId == 0) return false;
            foreach (var member in snapshot.Members)
                if (member != null && member.PlayerId == snapshot.LeaderId) return member.IsOnline;
            return false;
        }

        private static TianyongPlayerController ResolveLocalController(ActorWorld world)
        {
            if (world == null || !world.HasLocalPlayer) return null;
            if (!world.TryGetActor(world.LocalEntity, out var local) || local.Go == null) return null;
            var controller = local.Go.GetComponent<TianyongPlayerController>();
            return controller != null ? controller : null;
        }
    }
}
