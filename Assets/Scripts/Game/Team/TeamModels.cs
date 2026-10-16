using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.Team
{
    /// <summary>
    /// 队伍里的一名玩家(成员 / 申请人 / 被邀请人 / 邀请人)。由 TeamViewMapper 从服务端视图映射而来,
    /// 界面与离线预览只读它,不直接碰 Teampb 生成物。
    /// </summary>
    [Serializable]
    public sealed class TeamRole
    {
        public ulong PlayerId;
        public string Name;
        public uint Level;
        public uint ClassId;
        public uint Gender;
        public string SchoolName;
        public string CharacterId;
        public bool IsLeader;
        public bool IsOnline = true;
        // Chat history identifies a sender but does not prove current presence.
        public bool OnlineStatusKnown = true;
        public bool InBattle;
        public uint ZoneId;
        public uint JoinSeq;
        // 申请 / 已发邀请的服务端截止毫秒;成员恒为 0。
        public ulong ExpireAtMs;

        public TeamRole Clone() => (TeamRole)MemberwiseClone();
    }

    /// <summary>组队服务下发的权威快照(已映射)。</summary>
    [Serializable]
    public sealed class TeamSnapshot
    {
        public ulong TeamId;
        public ulong LeaderId;
        public ulong LocalPlayerId;
        public int Capacity = 5;
        public uint ZoneId;
        // 排序键:先比 MembershipEpoch,再比 Version(见 TeamViewMapper.Compare)。
        public ulong Version;
        public ulong MembershipEpoch;
        public bool MatchStarting;
        // 队伍补位匹配:我正随本队在人数档队列里等补位(服务端按我自己的排队票据现算,不带版本号)。
        // 后三项只在 MatchQueued 为真时有值:目标人数、进队列的服务端毫秒时刻、排的副本。
        public bool MatchQueued;
        public uint MatchTargetSize;
        public ulong MatchQueuedAtMs;
        public uint MatchBattleConfigId;
        // 服务端声明支持队伍补位匹配(且开关没关)。为真才给出「目标人数」选项:
        // 不认识目标人数的服务端(旧版本 / 另一版服务端)会把它静默当成直接开战。
        public bool MatchFillSupported;
        public uint ApplicationCount;
        public ulong ServerTimeMs;
        public List<TeamRole> Members = new List<TeamRole>();
        public List<TeamRole> Applications = new List<TeamRole>();
        public List<TeamRole> PendingInvites = new List<TeamRole>();
        public List<TeamRole> Approved = new List<TeamRole>();

        public TeamSnapshot Clone()
        {
            return new TeamSnapshot
            {
                TeamId = TeamId, LeaderId = LeaderId, LocalPlayerId = LocalPlayerId,
                Capacity = Capacity > 0 ? Capacity : 5,
                ZoneId = ZoneId, Version = Version, MembershipEpoch = MembershipEpoch,
                MatchStarting = MatchStarting, ApplicationCount = ApplicationCount, ServerTimeMs = ServerTimeMs,
                MatchQueued = MatchQueued, MatchTargetSize = MatchTargetSize, MatchQueuedAtMs = MatchQueuedAtMs,
                MatchBattleConfigId = MatchBattleConfigId, MatchFillSupported = MatchFillSupported,
                Members = CloneRoles(Members), Applications = CloneRoles(Applications),
                PendingInvites = CloneRoles(PendingInvites), Approved = CloneRoles(Approved)
            };
        }

        /// <summary>null 列表变空列表;null 元素跳过。</summary>
        public static List<TeamRole> CloneRoles(List<TeamRole> source)
        {
            var roles = new List<TeamRole>(source?.Count ?? 0);
            if (source == null) return roles;
            foreach (var role in source) if (role != null) roles.Add(role.Clone());
            return roles;
        }
    }

    /// <summary>收到的组队邀请。ExpiresAt 是 TeamClient 时钟下的本地截止秒数。</summary>
    [Serializable]
    public sealed class TeamInvite
    {
        public ulong TeamId;
        public ulong LeaderId;
        public uint MemberCount;
        public uint ZoneId;
        public TeamRole Inviter;
        public float ExpiresAt;

        public TeamInvite Clone()
        {
            var copy = (TeamInvite)MemberwiseClone();
            copy.Inviter = Inviter?.Clone();
            return copy;
        }
    }

    /// <summary>TeamClient 在途请求的种类;None 表示没有在途请求。</summary>
    public enum TeamAction
    {
        None, Refresh, ListInvites, Create, Apply, Decide, Invite, RespondInvite,
        Leave, Kick, Transfer, Disband, StartMatch
    }

    /// <summary>新视图相对当前视图的先后:Accept 应用;Stale 丢弃;Conflict 同 epoch 换了队,丢弃并重拉。</summary>
    public enum TeamSnapshotOrder { Accept, Stale, Conflict }
}
