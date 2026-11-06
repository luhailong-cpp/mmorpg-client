using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game
{
    // 组队跟随的收发(服务端 docs/design/team-follow-server-trail.md §5)。跟随中走位全在服务器:
    // 这里把 TeamFollowStateS2C 记成 TeamFollowDriven,并切换 ActorWorld 里本地角色的显示方式
    // (被驱动时按发给自己的 ActorMoveS2C 插值,本地控制器不走、不上报)。何时声明由 Team/TeamFollowDriver 决定。
    public sealed partial class GameClient
    {
        /// <summary>
        /// 服务器此刻是否在驱动本地角色跟随队长(最近一条 TeamFollowStateS2C)。入场通知与断线时复位为 false:
        /// 仍接在队长身后的,服务器在入场后会再推一次 driven=true。
        /// </summary>
        public bool TeamFollowDriven { get; private set; }

        /// <summary>收到入场通知的次数(本进程内只增不减,断线不清零)。跟随声明按"每次入场说一次"去重。</summary>
        public int SceneEntryEpoch { get; private set; }

        /// <summary>声明跟随(true)或暂离(false)。返回是否真的发出去了(没连着 gate 时为 false,调用方下一帧再试)。</summary>
        public bool SendTeamFollow(bool follow)
            => TrySendOneWay(SceneMovementClientPlayerRpc.TeamFollowMessageId, new TeamFollowC2S { Follow = follow });

        private void WireTeamFollowHandlers()
        {
            // 单向消息被拒时服务端回 id = 0 的错误信封,同号派发到这里,只记日志。
            RegisterMovementReply(SceneMovementClientPlayerRpc.TeamFollowMessageId, "TeamFollow");
            SceneMovementClientPlayerRpc.OnNotifyTeamFollowState(this, ApplyTeamFollowState,
                ex => LogError($"[team-follow] drop malformed follow state: {ex.Message}"));
        }

        private void ApplyTeamFollowState(TeamFollowStateS2C state)
        {
            TeamFollowDriven = state.Driven;
            // 被驱动期间自己不再上报移动;不清的话 MoveAck 的"停着就以服务器为准"会拿上一次自己走时的标记判。
            if (state.Driven) _isMoving = false;
            World.SetLocalServerDriven(state.Driven);
            Log($"[team-follow] driven={state.Driven} leader_entity={state.LeaderEntity} slot={state.Slot} reason={state.Reason}");
        }

        /// <summary>入场通知 / 断线:服务器驱动随之作废。调用前 World.Clear 已复位本地角色的显示方式,这里补齐自己的状态。</summary>
        private void ResetTeamFollowState()
        {
            TeamFollowDriven = false;
            World.SetLocalServerDriven(false);
        }
    }
}
