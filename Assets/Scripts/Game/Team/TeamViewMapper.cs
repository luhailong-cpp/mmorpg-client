using System;
using System.Collections.Generic;
using MmorpgClient.World;
using Teampb;

namespace MmorpgClient.Game.Team
{
    /// <summary>
    /// 服务端组队视图 → 客户端模型的纯函数映射、排序与显示名。不持有状态,
    /// TeamClient(活路径)与 TeamUiState(离线预览 / 令牌路径)共用同一份排序规则。
    /// </summary>
    public static class TeamViewMapper
    {
        public const int DefaultCapacity = 5;
        // 服务端邀请默认 60 秒有效;回包 / 推送缺时钟信息时按它估算本地截止。
        public const float DefaultInviteTtlSeconds = 60f;

        /// <summary>
        /// 整份映射。view 为 null 或 TeamId 为 0 时得到"无队"快照:三张名单全空,
        /// 只保留 epoch / version / server time 这些排序与时钟标量——被踢后的空视图要靠 epoch 挡住迟到的旧队视图。
        /// </summary>
        public static TeamSnapshot ToSnapshot(TeamView view, ulong localPlayerId)
        {
            var snapshot = new TeamSnapshot { LocalPlayerId = localPlayerId, Capacity = DefaultCapacity };
            if (view == null) return snapshot;

            snapshot.TeamId = view.TeamId;
            snapshot.Capacity = view.Capacity > 0 ? (int)Math.Min(view.Capacity, (uint)int.MaxValue) : DefaultCapacity;
            snapshot.Version = view.Version;
            snapshot.MembershipEpoch = view.MembershipEpoch;
            snapshot.ServerTimeMs = view.ServerTimeMs;
            if (snapshot.TeamId == 0) return snapshot;

            snapshot.LeaderId = view.LeaderId;
            snapshot.ZoneId = view.ZoneId;
            snapshot.MatchStarting = view.MatchState == TeamMatchState.Starting;
            snapshot.ApplicationCount = view.ApplicationCount;

            // 成员按 JoinSeq 升序。插入排序是稳定的:JoinSeq 相同时保持服务端原顺序
            // (List.Sort 不稳定);队伍最多 5 人,开销可以忽略。
            var members = new List<TeamMemberView>(view.Members.Count);
            foreach (var member in view.Members)
                if (member != null) members.Add(member);
            for (int i = 1; i < members.Count; i++)
            {
                var item = members[i];
                int j = i - 1;
                while (j >= 0 && members[j].JoinSeq > item.JoinSeq)
                {
                    members[j + 1] = members[j];
                    j--;
                }
                members[j + 1] = item;
            }
            foreach (var member in members)
            {
                var role = ToRole(member);
                // 队长以 LeaderId 为准,不信成员行上的 IsLeader 标志。
                role.IsLeader = member.PlayerId == view.LeaderId;
                snapshot.Members.Add(role);
            }

            // 申请与已发出邀请只下发给队长;保持服务端顺序(已按申请 / 邀请时间排好)。
            foreach (var application in view.Applications)
                if (application?.Player != null) snapshot.Applications.Add(ToRole(application.Player, application.ExpireAtMs));
            foreach (var invite in view.PendingInvites)
                if (invite?.Invitee != null) snapshot.PendingInvites.Add(ToRole(invite.Invitee, invite.ExpireAtMs));
            return snapshot;
        }

        /// <summary>单名玩家的映射;member 为 null 时返回 null。</summary>
        public static TeamRole ToRole(TeamMemberView member, ulong expireAtMs = 0)
        {
            if (member == null) return null;
            return new TeamRole
            {
                PlayerId = member.PlayerId,
                // 服务端 name 目前恒为 "",界面用 DisplayName 兜底。
                Name = member.Name,
                Level = member.Level,
                ClassId = member.ClassId,
                Gender = member.Gender,
                SchoolName = null,
                CharacterId = ResolveCharacterId(member.ClassId, member.Gender, member.AppearanceId),
                IsLeader = member.IsLeader,
                // 类默认值是 true,这里必须用服务端的值覆盖。
                IsOnline = member.IsOnline,
                InBattle = member.InBattle,
                ZoneId = member.ZoneId,
                JoinSeq = member.JoinSeq,
                ExpireAtMs = expireAtMs
            };
        }

        /// <summary>
        /// 收到的邀请 → 本地模型。serverTimeMs 与 ExpireAtMs 都有值时按服务端剩余时长换算成本地截止秒数,
        /// 避免依赖客户端与服务端的墙钟一致;缺任一个时按默认 60 秒估算。
        /// </summary>
        public static TeamInvite ToInvite(TeamIncomingInviteView invite, ulong serverTimeMs, float now)
        {
            if (invite == null || invite.TeamId == 0) return null;
            float expiresAt;
            if (serverTimeMs != 0 && invite.ExpireAtMs != 0)
                expiresAt = now + (invite.ExpireAtMs > serverTimeMs ? (invite.ExpireAtMs - serverTimeMs) / 1000f : 0f);
            else
                expiresAt = now + DefaultInviteTtlSeconds;
            return new TeamInvite
            {
                TeamId = invite.TeamId,
                LeaderId = invite.LeaderId,
                MemberCount = invite.MemberCount,
                ZoneId = invite.ZoneId,
                Inviter = ToRole(invite.Inviter) ?? new TeamRole { PlayerId = invite.LeaderId },
                ExpiresAt = expiresAt
            };
        }

        /// <summary>
        /// 立绘 id。存档外观 id 与世界、战斗同一口径(PersistedAppearanceIdentityTests 钉住),非空原样返回;
        /// 缺外观时按四职业推导,gender 0 的旧存档按男性处理;都推不出返回 null,界面显示"待同步",
        /// 不拿默认立绘冒充。
        /// </summary>
        public static string ResolveCharacterId(uint classId, uint gender, string appearanceId)
        {
            if (!string.IsNullOrEmpty(appearanceId)) return appearanceId;
            if (classId >= 1 && classId <= 4) return QdaoCharacterCatalog.ResolveRole(classId, gender);
            return null;
        }

        /// <summary>
        /// 视图先后(服务端 §H.3):先比 MembershipEpoch,大者新;epoch 相等时 TeamId 不同是冲突
        /// (丢弃并重拉),同队再比 Version,相等也接受(在线态刷新不涨 version)。
        /// 与 TeamAppearanceTransport.IsAtLeastAsRecent 不同,两者并存、互不替换。
        /// </summary>
        public static TeamSnapshotOrder Compare(TeamSnapshot current, TeamSnapshot incoming)
        {
            if (incoming == null) return TeamSnapshotOrder.Stale;
            if (current == null) return TeamSnapshotOrder.Accept;                 // Reset 后的第一份必定接受
            if (incoming.MembershipEpoch != current.MembershipEpoch)
                return incoming.MembershipEpoch > current.MembershipEpoch ? TeamSnapshotOrder.Accept : TeamSnapshotOrder.Stale;
            if (incoming.TeamId != current.TeamId) return TeamSnapshotOrder.Conflict; // epoch 相等但换了队:丢弃并重拉
            return incoming.Version >= current.Version ? TeamSnapshotOrder.Accept : TeamSnapshotOrder.Stale; // 相等也接受(在线态刷新)
        }

        /// <summary>显示名:服务端 name 尚未接线,空名时用玩家编号兜底,否则队长无法区分队员。</summary>
        public static string DisplayName(TeamRole role)
        {
            if (role == null) return "无名道友";
            if (!string.IsNullOrWhiteSpace(role.Name)) return role.Name;
            if (role.PlayerId != 0) return $"道友 {role.PlayerId}";
            return "无名道友";
        }
    }
}
