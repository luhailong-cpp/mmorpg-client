using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// Battle stage in the same 2560x1080 design space as the arena background.
    /// Each team retains front slots 0-4 and back slots 5-9, positioned by the
    /// shared BattleFormationLayout asset in design coordinates. Player-only
    /// teams of up to five use the back row for players and the front row for pets.
    /// Back-row pets share the corresponding front-row position;
    /// reserved front-row pets stand a short distance toward the opposing team.
    /// </summary>
    public static class BattleStage
    {
        public const int SlotsPerTeam = 10;
        public const int FrontRowCount = 5;
        // 阵位按同队入战顺序给出；双方阵型资源的列均从屏幕左侧起算。
        private static readonly int[] DisplayColumns = { 2, 1, 3, 0, 4 };

        /// <summary>Top action-order strip including its margin.</summary>
        public const float HudTopBand = 140f;

        /// <summary>
        /// 底部 HUD 带(设计坐标 y):目标提示、确认和取消条从这里开始。
        /// </summary>
        public const float HudBottomBand = 992f;

        /// <summary>单位表现的名义尺寸(设计像素；完整名牌避让另按 BattleUnitView.OverheadReach)。</summary>
        public const float UnitWidth = 220f;
        public const float UnitHeight = 200f;

        /// <summary>深度缩放基准:设计坐标 y=920 时缩放为 1.0。</summary>
        public const float DepthScaleReferenceY = 920f;
        /// <summary>每往上 1 设计像素缩 0.01%(视频里可检出的深度缩放 ≤6%)。</summary>
        public const float DepthScalePerPixel = 1f / 10000f;

        /// <summary>
        /// 宝宝归属判定入口:返回该单位主人的 actorId,0 = 不是宝宝。
        /// 2026-09-09 起服务端真的下发归属了(宝宝系统:BattleActorState.owner_player_id,
        /// 见 docs/design/player-pet.md §5),所以**默认实现直接读那个字段** —— 正式战斗路径
        /// 不需要任何额外接线,宝宝就会站到主人身边、不占槽位。
        /// 演出台(PresentationShowcase)用合成数据时覆盖它。
        /// </summary>
        public static Func<BattleActorState, ulong> PetOwnerResolver =
            actor => actor != null ? actor.OwnerPlayerId : 0UL;

        private static BattleFormationLayout s_layout;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLayoutResource() => s_layout = null;

        public static bool IsBackRow(int slot) => NormalizeSlot(slot) >= FrontRowCount;

        /// <summary>槽位在排内的逻辑列号(0-4)。</summary>
        public static int Column(int slot) => NormalizeSlot(slot) % FrontRowCount;

        /// <summary>读取配置中的脚底点，坐标以画布左上为原点。</summary>
        public static Vector2 SlotPosition(bool teamIsMine, int slot) => GetPlacement(teamIsMine, slot).position;

        private static (Vector2 position, float depthYPixels) GetPlacement(bool teamIsMine, int slot)
        {
            slot = NormalizeSlot(slot);
            if (s_layout == null)
                s_layout = Resources.Load<BattleFormationLayout>(BattleFormationLayout.ResourcePath);
            if (s_layout != null) return s_layout.GetPlacement(teamIsMine, slot);
            var fallback = BattleFormationLayout.GetDefaultPosition(teamIsMine, slot);
            return (fallback, fallback.y);
        }

        /// <summary>按整队平移前的槽位深度缩放，保留角色与宠物的显示尺寸。</summary>
        public static float SlotScale(bool teamIsMine, int slot) => DepthScale(GetPlacement(teamIsMine, slot).depthYPixels);

        /// <summary>按脚底 y 算深度缩放:基准 <see cref="DepthScaleReferenceY"/> 为 1.0,每往上 1px 缩 <see cref="DepthScalePerPixel"/>。</summary>
        public static float DepthScale(float footY) => 1f - (DepthScaleReferenceY - footY) * DepthScalePerPixel;

        /// <summary>后排主人的宝宝站同列前排，前排主人的预留宝宝位略靠近对方。</summary>
        public static Vector2 PetSlotPosition(bool teamIsMine, int ownerSlot)
        {
            if (IsBackRow(ownerSlot)) return SlotPosition(teamIsMine, Column(ownerSlot));
            var offset = teamIsMine ? new Vector2(-30f, -25f) : new Vector2(35f, 8f);
            return SlotPosition(teamIsMine, ownerSlot) + offset;
        }

        /// <summary>宝宝与主人使用相同的战斗显示缩放。</summary>
        public static float PetSlotScale(bool teamIsMine, int ownerSlot) => SlotScale(teamIsMine, ownerSlot);

        /// <summary>绘制排序键:脚底 y(越大越后画)。</summary>
        public static float SortKey(bool teamIsMine, int slot) => SlotPosition(teamIsMine, slot).y;

        /// <summary>按脚底 y 升序(a 在 b 上方 → 负)。</summary>
        public static int CompareDepth(Vector2 footA, Vector2 footB) => footA.y.CompareTo(footB.y);

        /// <summary>该单位的主人 actorId(经 <see cref="PetOwnerResolver"/>);不是宝宝或解析异常返回 0。</summary>
        public static ulong PetOwnerOf(BattleActorState actor)
        {
            if (actor == null) return 0UL;
            var resolver = PetOwnerResolver;
            if (resolver == null) return 0UL;
            try
            {
                ulong owner = resolver(actor);
                return owner == actor.ActorId ? 0UL : owner;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BattleStage] PetOwnerResolver 异常 actor={actor.ActorId}:{e.Message}");
                return 0UL;
            }
        }

        /// <summary>
        /// 该队伍的 actors → 槽位分配:优先 BattleActorState.formation_slot(D4),
        /// 越界/冲突按 actors 顺序取首个空槽;超过 10 人时循环复用(重叠,不崩)。
        /// 旧服务端不填 formation_slot 时全员为 0:首个占 0 号,其余自然回退到按序分配。
        /// 宝宝(<see cref="PetOwnerOf"/> ≠ 0 且主人在 actors 里)不占槽位、不出现在结果里 —— 它们跟着主人站
        /// (<see cref="PetSlotPosition"/>);主人不在场的"孤儿宝宝"退化为普通单位占槽。
        /// </summary>
        public static Dictionary<ulong, int> AssignSlots(IEnumerable<BattleActorState> actors, uint teamIndex)
        {
            var result = new Dictionary<ulong, int>();
            if (actors == null) return result;

            var list = new List<BattleActorState>(actors);
            var ids = new HashSet<ulong>();
            foreach (var actor in list)
            {
                if (actor != null) ids.Add(actor.ActorId);
            }

            var used = new bool[SlotsPerTeam];
            var pending = new List<BattleActorState>();
            foreach (var actor in list)
            {
                if (actor == null || actor.TeamIndex != teamIndex) continue;
                if (IsPlacedPet(actor, ids)) continue;
                int preferred = PreferredSlot(actor);
                if (preferred >= 0 && !used[preferred])
                {
                    used[preferred] = true;
                    result[actor.ActorId] = preferred;
                }
                else
                {
                    pending.Add(actor);
                }
            }

            int cursor = 0;
            foreach (var actor in pending)
            {
                int slot = -1;
                for (int i = 0; i < SlotsPerTeam; i++)
                {
                    if (!used[i]) { slot = i; break; }
                }
                if (slot < 0) slot = cursor++ % SlotsPerTeam; // 满员:循环复用
                else used[slot] = true;
                result[actor.ActorId] = slot;
            }
            return result;
        }

        /// <summary>
        /// 一次算出全部 actors 的显示槽位。至多五人的玩家队在后排布阵，宝宝在同列前排。
        /// 队伍归属按 myTeam 判定。slot 选取画布位置，depthSlot 保留换列前的显示深度。
        /// 宝宝沿用主人的三个字段；调用方用 <see cref="PetOwnerOf"/> 区分后改用
        /// <see cref="PetSlotPosition"/>/<see cref="PetSlotScale"/> 摆放。
        /// </summary>
        public static Dictionary<ulong, (bool teamIsMine, int slot, int depthSlot)> AssignAll(IEnumerable<BattleActorState> actors, uint myTeam)
        {
            var result = new Dictionary<ulong, (bool, int, int)>();
            if (actors == null) return result;
            var list = new List<BattleActorState>(actors);

            void AssignTeam(uint team)
            {
                var slots = AssignDisplaySlots(list, team);
                foreach (var pair in slots)
                {
                    int depthSlot = pair.Value;
                    int displaySlot = (IsBackRow(depthSlot) ? FrontRowCount : 0) + DisplayColumns[Column(depthSlot)];
                    result[pair.Key] = (team == myTeam, displaySlot, depthSlot);
                }
            }
            AssignTeam(myTeam);

            // 敌方可能不止一个 team_index(多方混战预留):按出现顺序各自分配,同槽位重叠
            var enemyTeams = new List<uint>();
            foreach (var actor in list)
            {
                if (actor == null || actor.TeamIndex == myTeam) continue;
                if (!enemyTeams.Contains(actor.TeamIndex)) enemyTeams.Add(actor.TeamIndex);
            }
            foreach (uint team in enemyTeams) AssignTeam(team);

            // 宝宝跟主人:沿用主人的 (teamIsMine, slot)
            foreach (var actor in list)
            {
                if (actor == null || result.ContainsKey(actor.ActorId)) continue;
                ulong owner = PetOwnerOf(actor);
                if (owner != 0UL && result.TryGetValue(owner, out var ownerPlacement)) result[actor.ActorId] = ownerPlacement;
            }
            return result;
        }

        private static Dictionary<ulong, int> AssignDisplaySlots(List<BattleActorState> actors, uint teamIndex)
        {
            var slots = AssignSlots(actors, teamIndex);
            if (slots.Count == 0 || slots.Count > FrontRowCount) return slots;

            foreach (var actor in actors)
            {
                if (actor != null && actor.TeamIndex == teamIndex && slots.ContainsKey(actor.ActorId)
                    && actor.ActorType != eBattleActorType.BattleActorTypePlayer)
                    return slots;
            }

            // 保留服务端已经指定的后排位置，再把前排槽号转换为同列后排显示位。
            var occupied = new bool[SlotsPerTeam];
            foreach (int slot in slots.Values) occupied[slot] = true;

            var displaced = new List<ulong>();
            foreach (var actor in actors)
            {
                if (actor == null || actor.TeamIndex != teamIndex
                    || !slots.TryGetValue(actor.ActorId, out int slot) || IsBackRow(slot))
                    continue;

                int backSlot = slot + FrontRowCount;
                if (occupied[backSlot])
                {
                    displaced.Add(actor.ActorId);
                    continue;
                }
                occupied[backSlot] = true;
                slots[actor.ActorId] = backSlot;
            }
            // 同列已有角色时，等其他角色落位后再补到空列。
            foreach (ulong actorId in displaced)
            {
                int backSlot = Array.IndexOf(occupied, false, FrontRowCount);
                occupied[backSlot] = true;
                slots[actorId] = backSlot;
            }
            return slots;
        }

        /// <summary>
        /// 该单位是否按"宝宝"摆放:主人已在 placement 里(即主人占了槽位)。孤儿宝宝返回 false(按普通单位处理)。
        /// </summary>
        public static bool IsPetPlacement(BattleActorState actor, IReadOnlyDictionary<ulong, (bool teamIsMine, int slot, int depthSlot)> placement)
        {
            if (actor == null || placement == null) return false;
            ulong owner = PetOwnerOf(actor);
            return owner != 0UL && placement.ContainsKey(owner);
        }

        /// <summary>formation_slot(0-9);越界返回 -1(按序回退)。</summary>
        public static int PreferredSlot(BattleActorState actor)
        {
            if (actor == null) return -1;
            return actor.FormationSlot < (uint)SlotsPerTeam ? (int)actor.FormationSlot : -1;
        }

        private static bool IsPlacedPet(BattleActorState actor, HashSet<ulong> presentIds)
        {
            ulong owner = PetOwnerOf(actor);
            return owner != 0UL && presentIds.Contains(owner);
        }

        private static int NormalizeSlot(int slot)
        {
            if (slot < 0) return 0;
            if (slot >= SlotsPerTeam) return slot % SlotsPerTeam;
            return slot;
        }
    }
}
