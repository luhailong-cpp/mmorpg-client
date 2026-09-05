using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 阵型舞台:10 个槽位(每方 5:前排 0-4、后排 5-9)在 2560×1080 设计坐标(y 向下)上的位置表。
    ///
    /// 布局(turn-battle-presentation.md §1/§3;按问道录像 f_003/f_008 逐帧校准,2026-09-04):
    ///  - 两阵各是一条沿"右上→左下"对角线的斜带(排方向 <see cref="RowStep"/>,约 17°),每条斜带两排交错;
    ///  - 敌阵在画面左上、我方在右下:先以中线(过 <see cref="Center"/>、方向 RowStep)镜像,
    ///    再各自沿排方向反向平移 <see cref="TeamRowShift"/>(敌方往左下、我方往右上),
    ///    两队中心横向错开约 2×TeamRowShift×cos17° ≈ 500px(≈20% 屏宽),两条斜带之间留出一条对角空隙;
    ///  - 后排相对前排向外(远离中线)推 <see cref="BackRowOffset"/>−<see cref="FrontRowOffset"/>,
    ///    并沿排方向错开半格(交错),整排缩放 <see cref="BackRowScale"/>;
    ///  - 近大远小:脚底 y 越大(越靠屏幕下方)越大,按 <see cref="DepthScalePerPixel"/> 线性;
    ///    敌方整体再乘 <see cref="EnemyTeamScale"/>(远端);
    ///  - 绘制顺序按脚底 y 升序(下方的后画,盖住上方的)。
    /// 整个战场(含立绘边缘)约占屏宽 60%(x≈530..2080),上下都在 HUD 带之外。
    /// 纯计算,不建对象;EditMode 直接测。
    /// </summary>
    public static class BattleStage
    {
        public const int SlotsPerTeam = 10;
        public const int FrontRowCount = 5;

        /// <summary>
        /// 顶部 HUD 带(设计坐标 y):行动预告条(12..132)+ 边距。所有槽位的名牌顶
        /// (脚底 − BattleUnitView.OverheadReach × 缩放,含头顶 HP/MP 条与 buff 行)必须落在其下,
        /// 否则敌方后排的血条会被头像瓦片盖住(BattleStageTests 断言)。
        /// </summary>
        public const float HudTopBand = 140f;

        /// <summary>
        /// 底部 HUD 带(设计坐标 y):目标提示/确认/取消条从这里开始。所有槽位脚下名字
        /// (脚底 + 40)必须落在其上,选目标时按钮才不会压在我方后排身上。
        /// </summary>
        public const float HudBottomBand = 900f;

        /// <summary>战场中心(设计坐标,y 向下;略低于屏幕中心,让上下 HUD 带外的舞台区居中)。</summary>
        public static readonly Vector2 Center = new Vector2(1250f, 600f);

        /// <summary>
        /// 沿排方向的相邻槽位间距:槽 0 在左下,槽 4 在右上(斜率约 17°,录像约 30°;本机 1080 高 +
        /// 上下 HUD 带只剩 760px 给四排,再陡就压 HUD)。间距 ≥170 让 5 人一排铺开而不叠。
        /// </summary>
        public static readonly Vector2 RowStep = new Vector2(170f, -52f);

        /// <summary>前排/后排到中线的距离(沿垂直于排方向,指向各自阵营外侧)。</summary>
        public const float FrontRowOffset = 140f;
        public const float BackRowOffset = 225f;

        /// <summary>
        /// 两队沿排方向的反向平移:敌方整体往排的左下端、我方往右上端各移这么多,
        /// 形成录像里"敌阵左上、我方右下"的对角错位(而不是上下叠成一个菱形团块)。
        /// </summary>
        public const float TeamRowShift = 260f;

        /// <summary>后排整体缩放(spec §3:后排 0.85 ~ 前排 1.0)。</summary>
        public const float BackRowScale = 0.85f;

        /// <summary>敌方(远端)整体再缩 5%。</summary>
        public const float EnemyTeamScale = 0.95f;

        /// <summary>
        /// 近大远小:每偏离中心 y 一像素的缩放增量(向下放大)。
        /// 与排/阵营缩放合起来,全场缩放落在约 0.74~1.05。
        /// </summary>
        public const float DepthScalePerPixel = 1f / 3200f;

        /// <summary>单位表现的名义尺寸(设计像素;近似屏高 20%)。</summary>
        public const float UnitWidth = 220f;
        public const float UnitHeight = 230f;

        /// <summary>沿排方向(右上)的单位向量。</summary>
        public static Vector2 RowDirection => RowStep.normalized;

        /// <summary>垂直于排方向、指向敌方外侧(左上)的单位向量。</summary>
        public static Vector2 EnemyOutward
        {
            get
            {
                var perp = new Vector2(RowStep.y, -RowStep.x); // (−52, −170):左上
                return perp.normalized;
            }
        }

        public static bool IsBackRow(int slot) => NormalizeSlot(slot) >= FrontRowCount;

        /// <summary>槽位在排内的列(0-4,左→右)。</summary>
        public static int Column(int slot) => NormalizeSlot(slot) % FrontRowCount;

        /// <summary>槽位脚底点(设计坐标,y 向下)。</summary>
        public static Vector2 SlotPosition(bool teamIsMine, int slot)
        {
            slot = NormalizeSlot(slot);
            bool back = slot >= FrontRowCount;
            int col = slot % FrontRowCount;

            float side = teamIsMine ? -1f : 1f;
            var pos = Center + EnemyOutward * (side * (back ? BackRowOffset : FrontRowOffset));
            pos += RowDirection * (-side * TeamRowShift); // 敌方往左下、我方往右上:对角错开
            pos += RowStep * (col - (FrontRowCount - 1) * 0.5f);
            if (back) pos += RowStep * 0.5f; // 后排交错半格
            return pos;
        }

        /// <summary>槽位缩放:排缩放 × 近大远小 × 阵营(敌方远端 0.95)。</summary>
        public static float SlotScale(bool teamIsMine, int slot)
        {
            var pos = SlotPosition(teamIsMine, slot);
            float depth = 1f + (pos.y - Center.y) * DepthScalePerPixel;
            float row = IsBackRow(slot) ? BackRowScale : 1f;
            float team = teamIsMine ? 1f : EnemyTeamScale;
            return row * depth * team;
        }

        /// <summary>绘制排序键:脚底 y(越大越后画)。</summary>
        public static float SortKey(bool teamIsMine, int slot) => SlotPosition(teamIsMine, slot).y;

        /// <summary>按脚底 y 升序(a 在 b 上方 → 负)。</summary>
        public static int CompareDepth(Vector2 footA, Vector2 footB) => footA.y.CompareTo(footB.y);

        /// <summary>
        /// 该队伍的 actors → 槽位分配:优先 BattleActorState.formation_slot(D4),
        /// 越界/冲突按 actors 顺序取首个空槽;超过 10 人时循环复用(重叠,不崩)。
        /// 旧服务端不填 formation_slot 时全员为 0:首个占 0 号,其余自然回退到按序分配。
        /// </summary>
        public static Dictionary<ulong, int> AssignSlots(IEnumerable<BattleActorState> actors, uint teamIndex)
        {
            var result = new Dictionary<ulong, int>();
            if (actors == null) return result;

            var used = new bool[SlotsPerTeam];
            var pending = new List<BattleActorState>();
            foreach (var actor in actors)
            {
                if (actor == null || actor.TeamIndex != teamIndex) continue;
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
        /// 一次算出全部 actors 的槽位(两队),队伍归属按 myTeam 判定;
        /// 返回 actorId → (teamIsMine, slot)。
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
            return result;
        }

        /// <summary>formation_slot(0-9);越界返回 -1(按序回退)。</summary>
        public static int PreferredSlot(BattleActorState actor)
        {
            if (actor == null) return -1;
            return actor.FormationSlot < (uint)SlotsPerTeam ? (int)actor.FormationSlot : -1;
        }

        private static int NormalizeSlot(int slot)
        {
            if (slot < 0) return 0;
            if (slot >= SlotsPerTeam) return slot % SlotsPerTeam;
            return slot;
        }
    }
}
