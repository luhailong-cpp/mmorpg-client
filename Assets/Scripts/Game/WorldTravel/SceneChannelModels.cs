using System;
using System.Collections.Generic;
using System.Globalization;

namespace MmorpgClient.Game.WorldTravel
{
    /// <summary>
    /// 一条分线对玩家可见的拥挤程度。镜像 proto 的 SceneChannelState,界面只读它、不直接碰生成物;
    /// 服务端以后加的、本客户端不认识的状态一律落到 <see cref="Unknown"/>(不可选,fail-closed)。
    /// </summary>
    public enum SceneChannelLoad
    {
        Unknown = 0,
        /// <summary>流畅。</summary>
        Smooth,
        /// <summary>繁忙(仍可进入)。</summary>
        Busy,
        /// <summary>爆满:人数已到每线上限,不可主动选入。</summary>
        Full,
        /// <summary>回收中:等玩家走空后销毁,不可进入。</summary>
        Closing,
        /// <summary>所在节点暂不可用。</summary>
        Unavailable,
    }

    /// <summary>
    /// 一条线此刻为什么不能点。<see cref="None"/> = 可以切。
    /// 判定规则与优先级见 <see cref="SceneChannelModels.Evaluate"/>;
    /// 这里只是客户端预判(置灰 + 文案),放行权威在服务端。
    /// </summary>
    public enum SceneChannelBlockReason
    {
        /// <summary>可以切。</summary>
        None = 0,

        // ── 全局原因:与点的是哪条线无关,整张面板一起置灰 ──
        /// <summary>未连接,或线路目录尚未就绪(未拉到 / 副本镜像里没有目录)。</summary>
        NotReady,
        /// <summary>战斗或观战中,或正在排队匹配。</summary>
        InBattle,
        /// <summary>正在传送 / 换图(地图窗的请求在途,或正在跨区换连接)。</summary>
        Travelling,
        /// <summary>上一次切线还没有结论。</summary>
        Switching,
        /// <summary>在队伍里且不是队长:v1 只有队长能切线(服务端设计 §6)。</summary>
        TeamMember,
        /// <summary>服务端关闭了玩家主动切线(目录 switch_enabled = false)。</summary>
        SwitchDisabled,
        /// <summary>本地切线冷却未过。</summary>
        CoolingDown,

        // ── 单线原因:只影响这一条线 ──
        /// <summary>这条线已不在当前目录里。</summary>
        LineMissing,
        /// <summary>就是玩家现在所在的线。</summary>
        CurrentLine,
        /// <summary>爆满。</summary>
        Full,
        /// <summary>回收中。</summary>
        Closing,
        /// <summary>所在节点暂不可用。</summary>
        Unavailable,
        /// <summary>状态未知(含本客户端不认识的新状态)。</summary>
        UnknownState,
    }

    /// <summary>
    /// 一条线的只读视图。由 <see cref="SceneChannelModels.BuildLines"/> 从服务端目录映射而来,
    /// 构造后不可变;目录每刷新一次就整批换新对象。
    /// </summary>
    public sealed class SceneChannelLine
    {
        /// <summary>这条线的场景实例 id;切线 = EnterScene(当前地图, 这个 scene_id)。</summary>
        public ulong SceneId { get; }
        /// <summary>线号,从 1 起;同一条线在其生命周期内不变。</summary>
        public uint ChannelNo { get; }
        /// <summary>服务端记的人数,秒级滞后,只用于展示。</summary>
        public uint PlayerCount { get; }
        public SceneChannelLoad Load { get; }
        /// <summary>构建列表那一刻,玩家是否就在这条线上。</summary>
        public bool IsCurrent { get; }

        public SceneChannelLine(ulong sceneId, uint channelNo, uint playerCount, SceneChannelLoad load, bool isCurrent)
        {
            SceneId = sceneId;
            ChannelNo = channelNo;
            PlayerCount = playerCount;
            Load = load;
            IsCurrent = isCurrent;
        }
    }

    /// <summary>
    /// 判定「能不能切线」所需的全部外部状态。做成一个带名字的结构而不是一串布尔参数,
    /// 调用处读得出每个值的含义。默认值(全 false / 0)判定为 <see cref="SceneChannelBlockReason.NotReady"/>。
    /// </summary>
    public struct SceneChannelSwitchContext
    {
        /// <summary>gate 连接已建立且验票通过。</summary>
        public bool Connected;
        /// <summary>当前地图的线路目录已就绪。</summary>
        public bool HasDirectory;
        /// <summary>战斗或观战中,或正在排队匹配(宿主提供)。</summary>
        public bool InBattle;
        /// <summary>正在传送 / 换图(宿主提供)。</summary>
        public bool Travelling;
        /// <summary>有切线请求在途。</summary>
        public bool SwitchPending;
        /// <summary>在队伍里且不是队长(宿主提供)。</summary>
        public bool TeamFollower;
        /// <summary>目录里的 switch_enabled。</summary>
        public bool SwitchEnabled;
        /// <summary>本地冷却剩余秒数;≤ 0 = 不在冷却。</summary>
        public float CooldownRemainingSeconds;
    }

    /// <summary>
    /// 分线的纯函数:proto 目录 → 视图、展示文案、「这条线现在能不能点 / 为什么不能」。
    /// 不引用 UnityEngine,不持有状态;网络与状态机在 <see cref="SceneChannelClient"/>。
    /// </summary>
    public static class SceneChannelModels
    {
        /// <summary>空线路列表(共享的只读实例)。</summary>
        public static readonly IReadOnlyList<SceneChannelLine> NoLines = Array.Empty<SceneChannelLine>();

        // ── 映射 ────────────────────────────────────────────────────────────

        /// <summary>proto 状态 → 客户端枚举。未知值(含将来新增的)→ Unknown。</summary>
        public static SceneChannelLoad ToLoad(SceneChannelState state)
        {
            switch (state)
            {
                case SceneChannelState.Smooth: return SceneChannelLoad.Smooth;
                case SceneChannelState.Busy: return SceneChannelLoad.Busy;
                case SceneChannelState.Full: return SceneChannelLoad.Full;
                case SceneChannelState.Closing: return SceneChannelLoad.Closing;
                case SceneChannelState.Unavailable: return SceneChannelLoad.Unavailable;
                default: return SceneChannelLoad.Unknown;
            }
        }

        /// <summary>
        /// 由服务端目录构建线路列表,按线号升序。<paramref name="directory"/> 为 null 返回空列表。
        /// 脏数据不抛:scene_id == 0 或 channel_no == 0 的条目丢弃(前者切不过去,后者显示不出线名);
        /// scene_id 或线号与**已保留**的条目重复时保留先出现的那条(按目录里的顺序)。
        /// <paramref name="currentSceneId"/> 为 0(还没进场)时没有任何一条被标为当前线。
        /// </summary>
        public static IReadOnlyList<SceneChannelLine> BuildLines(SceneChannelDirectory directory, ulong currentSceneId)
        {
            if (directory == null || directory.Channels.Count == 0) return NoLines;
            var lines = new List<SceneChannelLine>(directory.Channels.Count);
            var sceneIds = new HashSet<ulong>();
            var channelNos = new HashSet<uint>();
            foreach (var info in directory.Channels)
            {
                if (info == null || info.SceneId == 0 || info.ChannelNo == 0) continue;
                if (sceneIds.Contains(info.SceneId) || channelNos.Contains(info.ChannelNo)) continue;
                sceneIds.Add(info.SceneId);
                channelNos.Add(info.ChannelNo);
                lines.Add(new SceneChannelLine(info.SceneId, info.ChannelNo, info.PlayerCount, ToLoad(info.State),
                    currentSceneId != 0 && info.SceneId == currentSceneId));
            }
            // 线号已去重,排序结果唯一,不依赖排序算法是否稳定。
            lines.Sort((a, b) => a.ChannelNo.CompareTo(b.ChannelNo));
            return lines.AsReadOnly();
        }

        /// <summary>当前所在线的线号;列表为 null、或里面没有当前线时为 0。</summary>
        public static uint CurrentChannelNo(IReadOnlyList<SceneChannelLine> lines)
        {
            if (lines == null) return 0;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i] != null && lines[i].IsCurrent) return lines[i].ChannelNo;
            return 0;
        }

        /// <summary>按 scene_id 找线;找不到(或 sceneId == 0、列表为 null)返回 null。</summary>
        public static SceneChannelLine FindLine(IReadOnlyList<SceneChannelLine> lines, ulong sceneId)
        {
            if (lines == null || sceneId == 0) return null;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i] != null && lines[i].SceneId == sceneId) return lines[i];
            return null;
        }

        // ── 展示文案 ────────────────────────────────────────────────────────

        /// <summary>线名「N线」;线号为 0(目录里找不到当前线)时退回「线路」。</summary>
        public static string LineName(uint channelNo) =>
            channelNo == 0 ? "线路" : channelNo.ToString(CultureInfo.InvariantCulture) + "线";

        /// <summary>状态文案。用词与选服界面一致:繁忙,不是拥挤。</summary>
        public static string LoadText(SceneChannelLoad load)
        {
            switch (load)
            {
                case SceneChannelLoad.Smooth: return "流畅";
                case SceneChannelLoad.Busy: return "繁忙";
                case SceneChannelLoad.Full: return "爆满";
                case SceneChannelLoad.Closing: return "回收中";
                case SceneChannelLoad.Unavailable: return "暂不可用";
                default: return "未知";
            }
        }

        /// <summary>冷却剩余秒数向上取整(2.1 秒显示 3 秒,到 0 才消失);≤ 0 或非数为 0。</summary>
        public static int CooldownSecondsCeil(float remainingSeconds)
        {
            if (!(remainingSeconds > 0f)) return 0;   // 写成取反是为了把 NaN 也挡在这里
            if (remainingSeconds >= int.MaxValue) return int.MaxValue;
            return (int)Math.Ceiling(remainingSeconds);
        }

        /// <summary>
        /// 把不可切的原因翻成一句给玩家看的话;<see cref="SceneChannelBlockReason.None"/> 返回 ""。
        /// <paramref name="cooldownRemainingSeconds"/> 只有冷却原因会用到(向上取整显示)。
        /// </summary>
        public static string DescribeBlock(SceneChannelBlockReason reason, float cooldownRemainingSeconds = 0f)
        {
            switch (reason)
            {
                case SceneChannelBlockReason.None: return "";
                case SceneChannelBlockReason.NotReady: return "线路信息尚未就绪，请稍候。";
                // 宿主把「排队匹配中」也算进 InBattle(口径同地图窗能否传送),所以文案两种情况都要说得通。
                case SceneChannelBlockReason.InBattle: return "战斗或匹配中无法切线。";
                case SceneChannelBlockReason.Travelling: return "正在传送，请稍候。";
                case SceneChannelBlockReason.Switching: return "正在切线，请稍候。";
                case SceneChannelBlockReason.TeamMember: return "队伍中由队长切线，或先离队。";
                case SceneChannelBlockReason.SwitchDisabled: return "服务器暂未开放切线。";
                case SceneChannelBlockReason.CoolingDown:
                {
                    int seconds = CooldownSecondsCeil(cooldownRemainingSeconds);
                    return seconds > 0
                        ? "切线冷却中，" + seconds.ToString(CultureInfo.InvariantCulture) + "秒后可再次切线。"
                        : "切线冷却中，请稍候。";
                }
                case SceneChannelBlockReason.LineMissing: return "该线路已不存在，请重新选择。";
                case SceneChannelBlockReason.CurrentLine: return "已在该线路。";
                case SceneChannelBlockReason.Full: return "该线路已满，请选择其他线路。";
                case SceneChannelBlockReason.Closing: return "该线路正在回收，无法进入。";
                case SceneChannelBlockReason.Unavailable: return "该线路暂不可用。";
                default: return "该线路状态未知，暂时无法进入。";
            }
        }

        // ── 能不能切 ────────────────────────────────────────────────────────

        /// <summary>
        /// 全局原因(与点哪条线无关)。自上而下第一条命中:
        /// 未连接 → 战斗中 → 正在传送 / 换图 → 正在切线 → 队伍成员 → 目录未就绪 → 服务端关闭切线 → 冷却中。
        /// 顺序的理由:越靠前越是「玩家现在做不了任何切线相关的事」,文案也越贴近他眼前的处境;
        /// 「目录未就绪」排在宿主才知道的几条之后,是因为战斗 / 传送 / 队伍的提示比「线路信息尚未就绪」更具体;
        /// 冷却排最后,因为它是唯一一个等几秒自己会消失的。
        /// </summary>
        public static SceneChannelBlockReason EvaluateGlobal(SceneChannelSwitchContext context)
        {
            if (!context.Connected) return SceneChannelBlockReason.NotReady;
            if (context.InBattle) return SceneChannelBlockReason.InBattle;
            if (context.Travelling) return SceneChannelBlockReason.Travelling;
            if (context.SwitchPending) return SceneChannelBlockReason.Switching;
            if (context.TeamFollower) return SceneChannelBlockReason.TeamMember;
            if (!context.HasDirectory) return SceneChannelBlockReason.NotReady;
            if (!context.SwitchEnabled) return SceneChannelBlockReason.SwitchDisabled;
            if (context.CooldownRemainingSeconds > 0f) return SceneChannelBlockReason.CoolingDown;
            return SceneChannelBlockReason.None;
        }

        /// <summary>
        /// 单线原因(只看这条线自己)。自上而下第一条命中:
        /// 线不存在 → 当前线 → 回收中 → 暂不可用 → 爆满 → 状态未知;只有流畅 / 繁忙可选。
        /// 「当前线」排在各状态之前:玩家所在的线即使爆满或回收中,要告诉他的也是「你就在这条线」。
        /// </summary>
        public static SceneChannelBlockReason EvaluateLine(SceneChannelLine line)
        {
            if (line == null) return SceneChannelBlockReason.LineMissing;
            if (line.IsCurrent) return SceneChannelBlockReason.CurrentLine;
            switch (line.Load)
            {
                case SceneChannelLoad.Smooth:
                case SceneChannelLoad.Busy:
                    return SceneChannelBlockReason.None;
                case SceneChannelLoad.Closing: return SceneChannelBlockReason.Closing;
                case SceneChannelLoad.Unavailable: return SceneChannelBlockReason.Unavailable;
                case SceneChannelLoad.Full: return SceneChannelBlockReason.Full;
                default: return SceneChannelBlockReason.UnknownState;
            }
        }

        /// <summary>
        /// 一条线现在能不能点。全局原因(<see cref="EvaluateGlobal"/>)优先于单线原因(<see cref="EvaluateLine"/>):
        /// 战斗中 / 冷却中时整张面板都点不了,逐行显示「爆满」反而会让人以为换一条线就行。
        /// </summary>
        public static SceneChannelBlockReason Evaluate(SceneChannelLine line, SceneChannelSwitchContext context)
        {
            var global = EvaluateGlobal(context);
            return global != SceneChannelBlockReason.None ? global : EvaluateLine(line);
        }
    }
}
