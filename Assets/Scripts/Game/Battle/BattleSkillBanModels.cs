using System;
using System.Collections.Generic;
using MmorpgClient.Table;
using UnityEngine;

namespace MmorpgClient.Game.Battle
{
    /// <summary>
    /// 战前技能禁选（每方 3 Ban）纯 C# 领域辅助：分类分组、对称禁招查询、本地常用 3 禁偏好（赛季切换自动求交集过滤）与文案汇总。
    /// 不长持任何 *TableManager 行引用，按需通过 FindById 查询并显式判空。
    /// </summary>
    public static class BattleSkillBanModels
    {
        public const uint MaxBanSlotsPerTeam = 3u;
        public const uint MaxProtectSlotsPerTeam = 1u;
        public const int CategoryCount = 4;

        /// <summary>对应 SkillTable.ban_pool_category = 1..4 的 4 个固定页签文案。</summary>
        public static readonly string[] CategoryTabLabels = { "控场", "输出", "辅助", "灵宠" };

        private const string PrefKeyPrefix = "qdao_prep_skill_bans_v1_";
        private const string ProtectPrefKeyPrefix = "qdao_prep_skill_protect_v2_";

        /// <summary>判定技能在本局是否已被全局对称禁用。</summary>
        public static bool IsSkillBanned(BattleStateS2C state, uint skillId)
        {
            if (state == null || skillId == 0u || state.BannedSkillIds == null) return false;
            for (int i = 0; i < state.BannedSkillIds.Count; i++)
            {
                if (state.BannedSkillIds[i] == skillId) return true;
            }
            return false;
        }

        /// <summary>战前禁选 V2：判定技能是否被对方护招「破禁化解」而恢复可用。</summary>
        public static bool IsSkillCountered(BattleStateS2C state, uint skillId)
        {
            if (state == null || skillId == 0u || state.CounteredBanSkillIds == null) return false;
            for (int i = 0; i < state.CounteredBanSkillIds.Count; i++)
            {
                if (state.CounteredBanSkillIds[i] == skillId) return true;
            }
            return false;
        }

        /// <summary>战前禁选 V2：判定技能是否属于双方同时封禁且仍生效的「双边同禁」撞车。</summary>
        public static bool IsDoubleBanned(BattleStateS2C state, uint skillId)
        {
            if (state == null || skillId == 0u || !IsSkillBanned(state, skillId)) return false;
            return ContainsSkill(state.TeamABannedSkillIds, skillId)
                && ContainsSkill(state.TeamBBannedSkillIds, skillId);
        }

        /// <summary>战前禁选 V2：本局是否存在任何禁招或护招亮牌信息（用于控制顶条、开局亮牌与战报详情展示）。</summary>
        public static bool HasAnyBanOrProtectReveal(BattleStateS2C state)
        {
            if (state == null) return false;
            return state.BannedSkillIds.Count > 0
                || state.TeamABannedSkillIds.Count > 0
                || state.TeamBBannedSkillIds.Count > 0
                || state.TeamAProtectedSkillId != 0u
                || state.TeamBProtectedSkillId != 0u
                || state.CounteredBanSkillIds.Count > 0;
        }

        /// <summary>战前禁选 V2：判定某候选技能是否已被同队其他队友携带（仅读服务端脱敏下发的 TeammateEquippedSkillIds）。</summary>
        public static bool IsTeammateEquipped(BattlePrepState prep, uint skillId)
        {
            if (prep == null || skillId == 0u || prep.TeammateEquippedSkillIds == null) return false;
            return ContainsSkill(prep.TeammateEquippedSkillIds, skillId);
        }

        private static bool ContainsSkill(IReadOnlyList<uint> list, uint skillId)
        {
            if (list == null || skillId == 0u) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == skillId) return true;
            }
            return false;
        }

        /// <summary>
        /// 五行试炼志 V3：判定技能是否因当前生效的「禁控硬破」试炼誓约（bit0）被禁用。
        /// 判定口径与 C++ TurnBattleEngine::IsControlSkill 逐项一致（BattleOp=13 & StateKind 1..6，或 BuffType 30/31/52）。
        /// </summary>
        public static bool IsSkillBlockedByTrialVow(
            BattleStateS2C state,
            uint skillId,
            Func<uint, global::SkillTable> skillLookup = null,
            Func<uint, global::BuffTable> buffLookup = null)
        {
            if (state == null || skillId == 0u) return false;
            if ((state.TrialVowMask & MmorpgClient.Game.TrialJournal.TrialJournalModels.VowBanControl) == 0u)
                return false;
            var skillRow = skillLookup != null ? skillLookup(skillId) : SkillTableManager.Instance?.FindById(skillId);
            if (skillRow == null || skillRow.Effect == null || skillRow.Effect.Count == 0) return false;
            for (int i = 0; i < skillRow.Effect.Count; i++)
            {
                uint buffId = skillRow.Effect[i];
                if (buffId == 0u) continue;
                var buffRow = buffLookup != null ? buffLookup(buffId) : BuffTableManager.Instance?.FindById(buffId);
                if (buffRow == null) continue;
                if ((buffRow.BattleOp == 13u && buffRow.StateKind >= 1u && buffRow.StateKind <= 6u)
                    || buffRow.BuffType == 30u || buffRow.BuffType == 31u || buffRow.BuffType == 52u)
                    return true;
            }
            return false;
        }

        /// <summary>按 SkillTable.ban_pool_category（1..4）将服务端权威候选池分到 4 个页签桶，保序去重。</summary>
        public static List<uint>[] GroupCandidatesByCategory(
            IReadOnlyList<uint> candidateSkillIds,
            Func<uint, uint> categoryLookup = null)
        {
            var buckets = new List<uint>[CategoryCount];
            for (int i = 0; i < CategoryCount; i++) buckets[i] = new List<uint>();
            if (candidateSkillIds == null || candidateSkillIds.Count == 0) return buckets;

            Func<uint, uint> lookup = categoryLookup ?? ResolveSkillBanCategory;
            var seen = new HashSet<uint>();
            for (int i = 0; i < candidateSkillIds.Count; i++)
            {
                uint sid = candidateSkillIds[i];
                if (sid == 0u || !seen.Add(sid)) continue;
                uint cat = lookup(sid);
                if (cat >= 1u && cat <= (uint)CategoryCount)
                    buckets[(int)(cat - 1u)].Add(sid);
            }
            return buckets;
        }

        /// <summary>按需读取技能表 ban_pool_category（缺行返回 0，不长持表行）。</summary>
        public static uint ResolveSkillBanCategory(uint skillId)
        {
            if (skillId == 0u) return 0u;
            var row = SkillTableManager.Instance?.FindById(skillId);
            return row != null ? row.BanPoolCategory : 0u;
        }

        /// <summary>按需读取技能显示名（缺行回退“技能#id”）。</summary>
        public static string ResolveSkillName(uint skillId)
        {
            if (skillId == 0u) return string.Empty;
            var row = SkillTableManager.Instance?.FindById(skillId);
            if (row != null && !string.IsNullOrWhiteSpace(row.Name)) return row.Name;
            return $"技能{skillId}";
        }

        /// <summary>切换一条禁招提名（已选则取消，未选且未满槽则追加）；满槽再点返回 false。</summary>
        public static bool ToggleNomination(List<uint> nominations, uint skillId, uint maxSlots)
        {
            if (nominations == null || skillId == 0u || maxSlots == 0u) return false;
            int idx = nominations.IndexOf(skillId);
            if (idx >= 0)
            {
                nominations.RemoveAt(idx);
                return true;
            }
            if ((uint)nominations.Count >= maxSlots) return false;
            nominations.Add(skillId);
            return true;
        }

        /// <summary>战前禁选 V2：在护招模式下点击技能，再次点已护招技能则取消，点选本方已禁招技能则自动从禁招移除并转入护招。</summary>
        public static (uint nextProtect, bool bansModified) ToggleProtectNomination(
            List<uint> currentBans, uint currentProtect, uint clickedSkillId)
        {
            if (clickedSkillId == 0u) return (currentProtect, false);
            if (currentProtect == clickedSkillId) return (0u, false);
            bool bansModified = currentBans != null && currentBans.Remove(clickedSkillId);
            return (clickedSkillId, bansModified);
        }

        /// <summary>战前禁选 V2：在禁招模式下点击技能；若该技当前为本人护招，同时清空护招以免冲突。</summary>
        public static (bool accepted, uint nextProtect, bool protectModified) ToggleBanWithProtectClear(
            List<uint> currentBans, uint currentProtect, uint clickedSkillId, uint maxSlots)
        {
            if (!ToggleNomination(currentBans, clickedSkillId, maxSlots))
                return (false, currentProtect, false);
            if (currentProtect != 0u && currentProtect == clickedSkillId && currentBans.Contains(clickedSkillId))
                return (true, 0u, true);
            return (true, currentProtect, false);
        }

        /// <summary>统计同队各成员对某技能的禁招提名票数（只读己方脱敏后的 TeamStates）。</summary>
        public static int CountTeamVotes(BattlePrepState prep, uint skillId)
        {
            if (prep == null || skillId == 0u || prep.TeamStates == null) return 0;
            int votes = 0;
            for (int i = 0; i < prep.TeamStates.Count; i++)
            {
                var ps = prep.TeamStates[i];
                if (ps?.NominatedBanSkillIds == null) continue;
                for (int j = 0; j < ps.NominatedBanSkillIds.Count; j++)
                {
                    if (ps.NominatedBanSkillIds[j] == skillId)
                    {
                        votes++;
                        break;
                    }
                }
            }
            return votes;
        }

        /// <summary>战前禁选 V2：统计同队各成员对某技能的护招提名票数。</summary>
        public static int CountTeamProtectVotes(BattlePrepState prep, uint skillId)
        {
            if (prep == null || skillId == 0u || prep.TeamStates == null) return 0;
            int votes = 0;
            for (int i = 0; i < prep.TeamStates.Count; i++)
            {
                var ps = prep.TeamStates[i];
                if (ps != null && ps.NominatedProtectSkillId == skillId) votes++;
            }
            return votes;
        }

        /// <summary>格式化战报配装详情与顶部禁招浮条展开态的双方禁招摘要（V1 兼容重载）。</summary>
        public static string FormatTeamBansSummary(
            IReadOnlyList<uint> teamABans,
            IReadOnlyList<uint> teamBBans,
            Func<uint, string> nameLookup = null)
        {
            Func<uint, string> lookup = nameLookup ?? ResolveSkillName;
            string aText = FormatSkillList(teamABans, lookup);
            string bText = FormatSkillList(teamBBans, lookup);
            return $"A队禁：{aText}  |  B队禁：{bText}";
        }

        /// <summary>战前禁选 V2：格式化含双方禁招、双方护招、破禁化解与双边同禁的完整摘要。</summary>
        public static string FormatTeamBansSummary(BattleStateS2C state, Func<uint, string> nameLookup = null)
        {
            if (state == null) return string.Empty;
            Func<uint, string> lookup = nameLookup ?? ResolveSkillName;
            string aText = FormatSkillList(state.TeamABannedSkillIds, lookup);
            string bText = FormatSkillList(state.TeamBBannedSkillIds, lookup);
            string aProt = state.TeamAProtectedSkillId != 0u ? $" / 护：{lookup(state.TeamAProtectedSkillId)}" : string.Empty;
            string bProt = state.TeamBProtectedSkillId != 0u ? $" / 护：{lookup(state.TeamBProtectedSkillId)}" : string.Empty;
            var sb = new System.Text.StringBuilder();
            sb.Append($"A队禁：{aText}{aProt}  |  B队禁：{bText}{bProt}");
            if (state.CounteredBanSkillIds.Count > 0)
            {
                sb.Append($"  ·  破禁化解：{FormatSkillList(state.CounteredBanSkillIds, lookup)}");
            }
            var doubles = new List<uint>();
            for (int i = 0; i < state.TeamABannedSkillIds.Count; i++)
            {
                uint sid = state.TeamABannedSkillIds[i];
                if (IsDoubleBanned(state, sid) && !doubles.Contains(sid)) doubles.Add(sid);
            }
            if (doubles.Count > 0)
            {
                sb.Append($"  ·  双边同禁：{FormatSkillList(doubles, lookup)}");
            }
            return sb.ToString();
        }

        private static string FormatSkillList(IReadOnlyList<uint> ids, Func<uint, string> lookup)
        {
            if (ids == null || ids.Count == 0) return "无";
            var names = new List<string>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] != 0u) names.Add(lookup(ids[i]));
            }
            return names.Count > 0 ? string.Join("、", names) : "无";
        }

        /// <summary>
        /// 客户端本地“常用 3 禁”偏好持久化：仅在进入禁选且本人尚未提名时做一次预填（绝不自动锁定），
        /// 读取时强制与服务端本局下发的 ban_candidate_skill_ids 求交集，自动剔除已轮换退出的旧赛季技能。
        /// </summary>
        public static class PreferenceStore
        {
            public static string KeyFor(ulong playerId) => PrefKeyPrefix + playerId;
            public static string ProtectKeyFor(ulong playerId) => ProtectPrefKeyPrefix + playerId;

            public static List<uint> LoadFiltered(
                ulong playerId,
                IReadOnlyList<uint> candidateSkillIds,
                uint maxSlots,
                Func<string, string> getter = null)
            {
                var result = new List<uint>();
                if (playerId == 0ul || maxSlots == 0u || candidateSkillIds == null || candidateSkillIds.Count == 0)
                    return result;

                string raw = getter != null
                    ? getter(KeyFor(playerId))
                    : PlayerPrefs.GetString(KeyFor(playerId), string.Empty);
                if (string.IsNullOrWhiteSpace(raw)) return result;

                var allowed = new HashSet<uint>(candidateSkillIds);
                var seen = new HashSet<uint>();
                string[] parts = raw.Split(',');
                for (int i = 0; i < parts.Length && (uint)result.Count < maxSlots; i++)
                {
                    if (!uint.TryParse(parts[i].Trim(), out uint sid) || sid == 0u) continue;
                    if (allowed.Contains(sid) && seen.Add(sid))
                        result.Add(sid);
                }
                return result;
            }

            public static uint LoadProtectedFiltered(
                ulong playerId,
                IReadOnlyList<uint> candidateSkillIds,
                IReadOnlyList<uint> excludedBanSkillIds = null,
                Func<string, string> getter = null)
            {
                if (playerId == 0ul || candidateSkillIds == null || candidateSkillIds.Count == 0)
                    return 0u;
                string raw = getter != null
                    ? getter(ProtectKeyFor(playerId))
                    : PlayerPrefs.GetString(ProtectKeyFor(playerId), string.Empty);
                if (!uint.TryParse(raw?.Trim(), out uint sid) || sid == 0u) return 0u;
                if (!ContainsSkill(candidateSkillIds, sid)) return 0u;
                if (excludedBanSkillIds != null && ContainsSkill(excludedBanSkillIds, sid)) return 0u;
                return sid;
            }

            public static void SaveProtected(
                ulong playerId,
                uint protectedSkillId,
                Action<string, string> setter = null)
            {
                if (playerId == 0ul) return;
                string encoded = protectedSkillId != 0u ? protectedSkillId.ToString() : string.Empty;
                if (setter != null)
                {
                    setter(ProtectKeyFor(playerId), encoded);
                    return;
                }
                PlayerPrefs.SetString(ProtectKeyFor(playerId), encoded);
                PlayerPrefs.Save();
            }

            public static void Save(
                ulong playerId,
                IReadOnlyList<uint> banSkillIds,
                Action<string, string> setter = null)
            {
                if (playerId == 0ul) return;
                string encoded = string.Empty;
                if (banSkillIds != null && banSkillIds.Count > 0)
                {
                    var unique = new List<string>(banSkillIds.Count);
                    var seen = new HashSet<uint>();
                    for (int i = 0; i < banSkillIds.Count; i++)
                    {
                        uint sid = banSkillIds[i];
                        if (sid != 0u && seen.Add(sid)) unique.Add(sid.ToString());
                    }
                    encoded = string.Join(",", unique);
                }
                if (setter != null)
                {
                    setter(KeyFor(playerId), encoded);
                    return;
                }
                PlayerPrefs.SetString(KeyFor(playerId), encoded);
                PlayerPrefs.Save();
            }
        }
    }
}
