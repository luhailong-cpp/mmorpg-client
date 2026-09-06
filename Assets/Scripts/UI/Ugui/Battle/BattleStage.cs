using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 阵型舞台:10 个槽位(每方 5:前排 0-4、后排 5-9)在 2560×1080 设计坐标(y 向下)上的**逐槽位位置表**,
    /// 外加每个槽位的"宝宝位"(宠物贴着主人站的位置)。纯计算、不建对象;EditMode 直接测。
    ///
    /// 【数据来源】用户录制的问道 5v5 回合战斗视频 a58f3434….mp4(1280×592,抽帧 f_001~f_036 每秒一帧、
    /// d_001~d_145 每 0.25s 一帧),2026-09-05 由三份独立量测按单位逐个取中位数合并(位置分歧 ≤13px)。
    /// 三帧(第 1 回合 f_005/f_017、第 2 回合 f_027)及另抽的 f_001/002/003/015/016/029 里站位一致(≤5px),
    /// 即整场站位不变,本表就是站位表。"位置"以脚底点为准(名字文字上方、立绘最底部像素)。
    ///
    /// 【坐标换算】视频像素 (vx, vy) → 设计坐标 (dx, dy):按高度等比缩放并水平居中,
    ///   s = 1080 / 592 = 1.8243;dx = (vx − 640) × s + 1280;dy = vy × s。
    /// (视频 2.16:1、设计面 2.37:1,等比缩放后左右各留 ≈112px 空白,几何关系与视频完全一致。)
    ///
    /// 【行列定义】row=back 是各方远离中线的一排(视频里我方 = 5 名玩家,敌方 = 5 只怪含 3 只飘渺),
    /// 对应槽位 5-9;row=front 是靠中线的一排(视频里我方 = 5 只宝宝,敌方 = 4 只爪牙 + 1 个外推空槽),
    /// 对应槽位 0-4。列号 0..4 沿斜带从左下到右上。敌方前排第 5 槽视频里没有怪,按前排 0..3 的
    /// 线性步长 (74.1, −46.5) 视频像素外推;其余 19 个槽位全部是实测值。
    ///
    /// 【几何(设计像素)】排方向(左下→右上)视频步长 ≈ (74, −46.5) → 设计 (134.5, −85),方向角 32.2°,
    /// 槽间距 159;排间垂直距离 敌方 146 / 我方 173;前后排不是半格交错,而是"同列近乎垂直平移"再沿排方向
    /// 错 0.22~0.25 槽(敌前排朝右上错 39.5px、我方宝宝排朝左下错 35px)。两阵后排中心 敌 (850,501)、我 (1673,778)。
    /// 整场脚底范围 x 581..1946、y 330..949。
    ///
    /// 【宝宝位】视频里宝宝固定站在主人的"左上方"(朝敌方的前方偏左):我方 5 对实测 宝宝脚底 − 主人脚底
    /// 均值 (−79.6, −62.2) 视频像素 → 设计 (−145, −113);后排(玩家)槽位的宝宝位 = 同列前排的实测位置,
    /// 也就是视频里的宝宝排。敌方无宝宝,敌方后排"宝宝位"取同列前排实测位(前排相对后排 (+76, +47.2) 视频像素
    /// → 设计 (+139, +86))。前排槽位的宝宝位是把同一偏移再套一次(向中线再推一排),视频里无实例,仅作预留。
    ///
    /// 【缩放】视频里几乎没有近大远小(同物种在不同深度高度差 ≤5~8%,在测量误差内),故用极弱线性深度模型:
    /// unitScale = 1 − (948.6 − designY) / 10000,以我方玩家排 slot0(全场最低点、立绘 110 视频 px = 200 设计 px)为 1.0,
    /// 每往上 100 设计像素缩 1%,全场 0.938~1.0;不做后排整排缩放、不做敌方整队缩放。物种体型差由美术资源本身体现。
    ///
    /// 【历史口径】2026-09-04 之前的版本是参数化几何:Center(1250,600) + RowStep(170,−52)(约 17°、槽距 178)+
    /// FrontRowOffset 140 / BackRowOffset 225 + TeamRowShift 260 + 后排半格交错 + BackRowScale 0.85 × EnemyTeamScale 0.95 ×
    /// 深度 1/3200。与视频差别最大的是排方向角(17° vs 32.2°)、排间距/交错方式与缩放,故整体替换为本表。
    /// </summary>
    public static class BattleStage
    {
        public const int SlotsPerTeam = 10;
        public const int FrontRowCount = 5;

        /// <summary>视频 → 设计坐标的等比缩放系数(1080 / 592)。</summary>
        public const float VideoToDesignScale = 1080f / 592f;
        /// <summary>等比缩放后视频画面左边在设计面上的 x(左右各留白 ≈112px)。</summary>
        public const float VideoOffsetX = 1280f - 640f * VideoToDesignScale;

        /// <summary>
        /// 顶部 HUD 带(设计坐标 y):行动预告条(12..132)+ 边距。所有槽位与宝宝位的脚底都在其下;
        /// 全场最高的敌方后排 slot4(脚底 330)的头顶 HP 条约在 y≈150,恰好贴着预告条之下。
        /// </summary>
        public const float HudTopBand = 140f;

        /// <summary>
        /// 底部 HUD 带(设计坐标 y):目标提示/确认/取消条从这里开始。所有槽位脚下名字(脚底 + 40)必须落在其上。
        /// 视频里我方玩家排 slot0 脚底 948.6(全场最低点),名字底 ≈989,故底部带从 992 起。
        /// </summary>
        public const float HudBottomBand = 992f;

        /// <summary>单位表现的名义尺寸(设计像素;视频玩家立绘 100~110 视频 px ≈ 182~200 设计 px)。</summary>
        public const float UnitWidth = 220f;
        public const float UnitHeight = 200f;

        /// <summary>深度缩放基准:我方玩家排 slot0 脚底 y(全场最低点)= 缩放 1.0。</summary>
        public const float DepthScaleReferenceY = 948.6f;
        /// <summary>每往上 1 设计像素缩 0.01%(视频里可检出的深度缩放 ≤6%)。</summary>
        public const float DepthScalePerPixel = 1f / 10000f;

        /// <summary>宝宝相对主人的缩放(视频里酷酷龙 53 视频 px vs 主人 100~110;人形幻化宠与主人等大,取折中)。</summary>
        public const float PetRelativeScale = 0.65f;

        /// <summary>
        /// 宝宝归属判定入口:返回该单位主人的 actorId,0 = 不是宝宝。默认恒 0。
        /// 服务端目前没有宠物实体(BattleActorState 无 owner 字段),先由客户端表现层按数据决定是否出现:
        /// 演出台(PresentationShowcase)用它给合成战斗标记宝宝;将来服务端补 owner_actor_id 后在这里接上即可。
        /// </summary>
        public static Func<BattleActorState, ulong> PetOwnerResolver = _ => 0UL;

        /// <summary>一个槽位的实测数据(设计坐标,y 向下):脚底点 + 宝宝脚底点。</summary>
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

        // ── 敌方(画面左上斜带)──
        // 前排 0-4(靠中线;视频里的爪牙排,slot4 外推);宝宝位 = 再往中线推一排(预留)
        private static readonly SlotEntry[] EnemyFront =
        {
            new SlotEntry(718.1f, 757.1f, 856.7f, 843.2f),   // 邪仙爪牙(绿皮兽人;视频 (332,415))
            new SlotEntry(854.9f, 671.4f, 993.5f, 757.5f),   // 邪仙爪牙(灰色人形;视频 (407,368))
            new SlotEntry(986.3f, 589.3f, 1124.9f, 675.4f),  // 邪仙爪牙(蓝灰小怪;视频 (479,323))
            new SlotEntry(1124.9f, 501.7f, 1263.6f, 587.8f), // 邪仙爪牙(蓝灰瘦怪;视频 (555,275))
            new SlotEntry(1259f, 417.8f, 1397.6f, 503.9f),   // 外推空槽(视频 (628.5,229))
        };

        // 后排 5-9(远离中线;视频里的飘渺/爪牙排);宝宝位 = 同列前排实测位
        private static readonly SlotEntry[] EnemyBack =
        {
            new SlotEntry(581.3f, 669.5f, 718.1f, 757.1f),   // 邪仙爪牙(视频 (257,367))
            new SlotEntry(714.5f, 587.4f, 854.9f, 671.4f),   // 火地邪仙飘渺(视频 (330,322))
            new SlotEntry(854.9f, 501.7f, 986.3f, 589.3f),   // 火地邪仙飘渺(视频 (407,275))
            new SlotEntry(979f, 415.9f, 1124.9f, 501.7f),    // 火地邪仙飘渺(视频 (475,228))
            new SlotEntry(1119.5f, 330.2f, 1259f, 417.8f),   // 邪仙爪牙(全场最高点;视频 (552,181))
        };

        // ── 我方(画面右下斜带)──
        // 前排 0-4(靠中线;视频里的宝宝排);宝宝位 = 再往中线推一排(预留)
        private static readonly SlotEntry[] AllyFront =
        {
            new SlotEntry(1258.1f, 833.7f, 1112.9f, 720.2f), // 宝宝 雪女(视频 (628,457))
            new SlotEntry(1394.9f, 748f, 1249.7f, 634.5f),   // 宝宝 酷酷龙(黄身小龙;视频 (703,410))
            new SlotEntry(1526.3f, 660.4f, 1381.1f, 546.9f), // 宝宝 酷酷龙(蓝发人形幻化;视频 (775,362))
            new SlotEntry(1663.1f, 582f, 1517.9f, 468.5f),   // 宝宝 酷酷龙(蓝发人形幻化;视频 (850,319))
            new SlotEntry(1794.5f, 499.9f, 1649.3f, 386.4f), // 宝宝 水神(视频 (922,274))
        };

        // 后排 5-9(远离中线;视频里的 5 名玩家);宝宝位 = 同列前排实测位(即视频里各自的宝宝)
        private static readonly SlotEntry[] AllyBack =
        {
            new SlotEntry(1398.6f, 948.6f, 1258.1f, 833.7f), // 玩家 梦醒时夜续う(全场最低点;视频 (705,520))
            new SlotEntry(1546.4f, 866.6f, 1394.9f, 748f),   // 玩家 逆天丶哀浪(视频 (786,475))
            new SlotEntry(1668.6f, 773.5f, 1526.3f, 660.4f), // 玩家 陶の金帝(视频 (853,424))
            new SlotEntry(1803.6f, 696.9f, 1663.1f, 582f),   // 玩家 jay一晴天(视频 (927,382))
            new SlotEntry(1945.9f, 605.7f, 1794.5f, 499.9f), // 玩家 时光巷陌っ(全场最右点;视频 (1005,332))
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

        /// <summary>槽位缩放:极弱线性深度模型(脚底越低越大),全场 0.938~1.0。</summary>
        public static float SlotScale(bool teamIsMine, int slot) => DepthScale(SlotPosition(teamIsMine, slot).y);

        /// <summary>按脚底 y 算深度缩放:基准 <see cref="DepthScaleReferenceY"/> 为 1.0,每往上 1px 缩 <see cref="DepthScalePerPixel"/>。</summary>
        public static float DepthScale(float footY) => 1f - (DepthScaleReferenceY - footY) * DepthScalePerPixel;

        /// <summary>主人在 ownerSlot 时,其宝宝的脚底点(设计坐标)—— 直接查表(后排主人的宝宝位 = 同列前排实测位)。</summary>
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
