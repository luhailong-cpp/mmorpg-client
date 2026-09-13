using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// Forest-bridge battle stage in the same 2560x1080 design space as its painting.
    /// Each team retains front slots 0-4 and back slots 5-9, ordered lower-left to
    /// upper-right. The positions follow the two stone platforms rather than the
    /// unrelated historical video coordinates. Slot assignment and protocol IDs
    /// are unchanged. Back-row pets share the corresponding front-row position;
    /// reserved front-row pets stand a short distance toward the opposing team.
    /// Ground/shadow bounds were traced independently from the accepted painting
    /// in qdao_festival_scenes_20260910/runtime/battle-platform-ground.json.
    /// </summary>
    public static class BattleStage
    {
        public const int SlotsPerTeam = 10;
        public const int FrontRowCount = 5;

        /// <summary>Top action-order strip including its margin.</summary>
        public const float HudTopBand = 140f;

        /// <summary>
        /// 底部 HUD 带(设计坐标 y):目标提示/确认/取消条从这里开始。所有槽位脚下名字(脚底 + 40)必须落在其上。
        /// 校准后的最低脚点为 920；保留既有 992 的确认条布局合同。
        /// </summary>
        public const float HudBottomBand = 992f;

        /// <summary>单位表现的名义尺寸(设计像素；完整名牌避让另按 BattleUnitView.OverheadReach)。</summary>
        public const float UnitWidth = 220f;
        public const float UnitHeight = 200f;

        /// <summary>深度缩放基准:我方玩家排 slot0 脚底 y(全场最低点)= 缩放 1.0。</summary>
        public const float DepthScaleReferenceY = 920f;
        /// <summary>每往上 1 设计像素缩 0.01%(视频里可检出的深度缩放 ≤6%)。</summary>
        public const float DepthScalePerPixel = 1f / 10000f;

        /// <summary>宝宝相对主人的缩放(视频里酷酷龙 53 视频 px vs 主人 100~110;人形幻化宠与主人等大,取折中)。</summary>
        public const float PetRelativeScale = 0.65f;

        /// <summary>
        /// 宝宝归属判定入口:返回该单位主人的 actorId,0 = 不是宝宝。
        /// 2026-09-09 起服务端真的下发归属了(宝宝系统:BattleActorState.owner_player_id,
        /// 见 docs/design/player-pet.md §5),所以**默认实现直接读那个字段** —— 正式战斗路径
        /// 不需要任何额外接线,宝宝就会站到主人身边、不占槽位。
        /// 演出台(PresentationShowcase)用合成数据时覆盖它。
        /// </summary>
        public static Func<BattleActorState, ulong> PetOwnerResolver =
            actor => actor != null ? actor.OwnerPlayerId : 0UL;

        /// <summary>一个槽位的美术适配坐标(设计坐标,y 向下):脚底点 + 宝宝脚底点。</summary>
        private readonly struct SlotEntry
        {
            public readonly float X;
            public readonly float Y;
            public readonly float PetX;
            public readonly float PetY;

            public SlotEntry(float x, float y, float petX, float petY)
            {
                X = x; Y = y; PetX = petX; PetY = petY;
            }

            public Vector2 Foot => new Vector2(X, Y);
            public Vector2 PetFoot => new Vector2(PetX, PetY);
        }

        private static readonly SlotEntry[] EnemyFront =
        {
            new SlotEntry(520f, 515f, 555f, 522f),
            new SlotEntry(615f, 505f, 650f, 512f),
            new SlotEntry(710f, 495f, 745f, 502f),
            new SlotEntry(805f, 475f, 840f, 482f),
            new SlotEntry(950f, 405f, 980f, 415f),
        };

        private static readonly SlotEntry[] EnemyBack =
        {
            new SlotEntry(450f, 440f, 520f, 515f),
            new SlotEntry(550f, 430f, 615f, 505f),
            new SlotEntry(650f, 420f, 710f, 495f),
            new SlotEntry(750f, 400f, 805f, 475f),
            new SlotEntry(846f, 393f, 950f, 405f),
        };

        private static readonly SlotEntry[] AllyFront =
        {
            new SlotEntry(1470f, 820f, 1450f, 795f),
            new SlotEntry(1570f, 780f, 1540f, 750f),
            new SlotEntry(1670f, 740f, 1640f, 710f),
            new SlotEntry(1770f, 700f, 1740f, 670f),
            new SlotEntry(1870f, 660f, 1840f, 630f),
        };

        private static readonly SlotEntry[] AllyBack =
        {
            new SlotEntry(1555f, 920f, 1470f, 820f),
            new SlotEntry(1650f, 880f, 1570f, 780f),
            new SlotEntry(1745f, 840f, 1670f, 740f),
            new SlotEntry(1840f, 800f, 1770f, 700f),
            new SlotEntry(1935f, 760f, 1870f, 660f),
        };

        public static bool IsBackRow(int slot) => NormalizeSlot(slot) >= FrontRowCount;

        /// <summary>槽位在排内的列(0-4,左下→右上)。</summary>
        public static int Column(int slot) => NormalizeSlot(slot) % FrontRowCount;

        private static SlotEntry Entry(bool teamIsMine, int slot)
        {
            slot = NormalizeSlot(slot);
            bool back = slot >= FrontRowCount;
            int col = slot % FrontRowCount;
            if (teamIsMine) return back ? AllyBack[col] : AllyFront[col];
            return back ? EnemyBack[col] : EnemyFront[col];
        }

        /// <summary>槽位脚底点(设计坐标,y 向下)—— 直接查表。</summary>
        public static Vector2 SlotPosition(bool teamIsMine, int slot) => Entry(teamIsMine, slot).Foot;

        /// <summary>槽位缩放:极弱线性深度模型(脚底越低越大),全场约 0.947~1.0。</summary>
        public static float SlotScale(bool teamIsMine, int slot) => DepthScale(SlotPosition(teamIsMine, slot).y);

        /// <summary>按脚底 y 算深度缩放:基准 <see cref="DepthScaleReferenceY"/> 为 1.0,每往上 1px 缩 <see cref="DepthScalePerPixel"/>。</summary>
        public static float DepthScale(float footY) => 1f - (DepthScaleReferenceY - footY) * DepthScalePerPixel;

        /// <summary>主人在 ownerSlot 时,其宝宝的脚底点(设计坐标)—— 直接查表(后排主人的宝宝位 = 同列前排位置)。</summary>
        public static Vector2 PetSlotPosition(bool teamIsMine, int ownerSlot) => Entry(teamIsMine, ownerSlot).PetFoot;

        /// <summary>宝宝缩放:主人缩放 × <see cref="PetRelativeScale"/>。</summary>
        public static float PetSlotScale(bool teamIsMine, int ownerSlot) => SlotScale(teamIsMine, ownerSlot) * PetRelativeScale;

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
        /// 一次算出全部 actors 的槽位(两队),队伍归属按 myTeam 判定;返回 actorId → (teamIsMine, slot)。
        /// 宝宝的条目是 (主人的 teamIsMine, 主人的 slot):调用方用 <see cref="PetOwnerOf"/> 区分后改用
        /// <see cref="PetSlotPosition"/>/<see cref="PetSlotScale"/> 摆放。
        /// </summary>
        public static Dictionary<ulong, (bool teamIsMine, int slot)> AssignAll(IEnumerable<BattleActorState> actors, uint myTeam)
        {
            var result = new Dictionary<ulong, (bool, int)>();
            if (actors == null) return result;
            var list = new List<BattleActorState>(actors);

            var mine = AssignSlots(list, myTeam);
            foreach (var kv in mine) result[kv.Key] = (true, kv.Value);

            // 敌方可能不止一个 team_index(多方混战预留):按出现顺序各自分配,同槽位重叠
            var enemyTeams = new List<uint>();
            foreach (var actor in list)
            {
                if (actor == null || actor.TeamIndex == myTeam) continue;
                if (!enemyTeams.Contains(actor.TeamIndex)) enemyTeams.Add(actor.TeamIndex);
            }
            foreach (uint team in enemyTeams)
            {
                foreach (var kv in AssignSlots(list, team)) result[kv.Key] = (false, kv.Value);
            }

            // 宝宝跟主人:沿用主人的 (teamIsMine, slot)
            foreach (var actor in list)
            {
                if (actor == null || result.ContainsKey(actor.ActorId)) continue;
                ulong owner = PetOwnerOf(actor);
                if (owner != 0UL && result.TryGetValue(owner, out var ownerPlacement)) result[actor.ActorId] = ownerPlacement;
            }
            return result;
        }

        /// <summary>
        /// 该单位是否按"宝宝"摆放:主人已在 placement 里(即主人占了槽位)。孤儿宝宝返回 false(按普通单位处理)。
        /// </summary>
        public static bool IsPetPlacement(BattleActorState actor, IReadOnlyDictionary<ulong, (bool teamIsMine, int slot)> placement)
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
