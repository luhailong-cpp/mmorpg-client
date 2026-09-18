namespace MmorpgClient.Game.WorldTravel
{
    /// <summary>记录传送请求，只有服务端入场通知才能确认抵达。</summary>
    public sealed class CityTravelRequest
    {
        /// <summary>
        /// 请求发出后的默认等待上限。它只够盖住“请求还没被受理”的那一段（请求本身十几秒内必有应答或报错）；
        /// 受理之后要等多久由服务端的交接预算决定，见 <see cref="AcceptedHandoffBudgetSeconds"/>。
        /// </summary>
        public const double TimeoutSeconds = 30.0;
        /// <summary>
        /// 请求被受理之后，客户端至少要再等这么久才能宣布“没等到结果”。同区换图与跨区传送共用这一个数。
        /// 依据是服务端的交接预算：受理后源场景可能转入“冻结、存盘、重发进场请求”的归属交接
        /// （同区换图落到别的节点时、以及每一次跨区传送都走它），存盘与等应答各有一道 30 秒的看门狗，
        /// 前后串行（服务端 player_lifecycle.cpp 的 kTravelReplyBudgetSec 同时用于这两段），
        /// 最坏约 60 秒才有结论：要么入场通知到达，要么补推一条失败提示。
        /// 余下 15 秒留给网络往返和服务端核实归属的那次查询。
        /// 比它短的后果：客户端先收起遮罩报“未收到抵达消息”，玩家却还被服务端冻结着（走不动、再点被拒），
        /// 随后要么突然换图成功，要么失败提示到达时请求已不在途、原因被吞掉。
        /// 客户端分不清某次同区换图走没走交接（服务端不通知），只能一律按交接的预算等；
        /// 代价是普通换图的应答丢失时，遮罩也要等满这个数才收起。
        /// 盖不住的情形：服务端存储不可用时会反复重挂看门狗、一直冻结到恢复，任何有限上限都会先到期。
        /// 服务端改那个常量或再加一段看门狗时，必须同步改这里。
        /// </summary>
        public const double AcceptedHandoffBudgetSeconds = 75.0;
        /// <summary>
        /// 跨区传送的等待上限。跨区在源场景交接（最坏约 60 秒，见 <see cref="AcceptedHandoffBudgetSeconds"/>）
        /// 之后，还要换连接、验票、重新登录、进游戏、等入场通知，几段相加远超同区。
        /// 用同区的上限会在传送途中提前报“未收到抵达消息”，随后玩家却真的到了。
        /// </summary>
        public const double CrossZoneTimeoutSeconds = 120.0;
        public uint Destination { get; private set; }
        public bool Festival { get; private set; }
        public bool IsPending { get; private set; }
        public bool IsAccepted { get; private set; }
        public int Generation { get; private set; }
        private double _deadline;

        /// <summary>
        /// <paramref name="timeoutSeconds"/> 不传即 <see cref="TimeoutSeconds"/>；
        /// 做成参数而不是改常量，是因为现有测试依赖 30 秒。非正数按默认值处理。
        /// 同区换图受理后的等待由带时间参数的 <see cref="Accept(int, double, double)"/> 顺延，不靠这里。
        /// </summary>
        public int Begin(uint destination, bool festival, double now, double timeoutSeconds = TimeoutSeconds)
        {
            if (IsPending || destination == 0) return 0;
            ++Generation;
            Destination = destination;
            Festival = festival;
            IsPending = true;
            IsAccepted = false;
            _deadline = now + (timeoutSeconds > 0 ? timeoutSeconds : TimeoutSeconds);
            return Generation;
        }

        public bool Accept(int generation)
        {
            if (!IsCurrent(generation)) return false;
            IsAccepted = true;
            return true;
        }

        /// <summary>
        /// 受理，并保证从 <paramref name="now"/> 起至少还会再等 <paramref name="waitAtLeastSeconds"/> 秒。
        /// 只延不缩：跨区请求一开始就给了更长的上限，不能被这里改短。非正数等同于不带时间的受理。
        /// 为什么受理时才延、而不是一开始就给长上限：受理之前服务端还没开始干活，30 秒绰绰有余；
        /// 受理之后服务端才可能转入交接，等多久从这一刻起由服务端的预算决定。
        /// 入场通知抢在应答之前到达时请求已经结束，这里返回 false，不会把已结束的请求重新续上。
        /// </summary>
        public bool Accept(int generation, double now, double waitAtLeastSeconds)
        {
            if (!Accept(generation)) return false;
            if (waitAtLeastSeconds > 0 && now + waitAtLeastSeconds > _deadline) _deadline = now + waitAtLeastSeconds;
            return true;
        }

        public bool Fail(int generation)
        {
            if (!IsCurrent(generation)) return false;
            Reset();
            return true;
        }

        public bool ReceiveScene(uint sceneConfigId, out bool festival)
        {
            festival = false;
            if (!IsPending) return false;
            bool arrived = sceneConfigId == Destination;
            festival = arrived && Festival;
            Reset();
            return arrived;
        }

        public bool Tick(double now)
        {
            if (!IsPending || now < _deadline) return false;
            Reset();
            return true;
        }

        public void Reset()
        {
            ++Generation;
            Destination = 0;
            Festival = false;
            IsPending = false;
            IsAccepted = false;
            _deadline = 0;
        }

        private bool IsCurrent(int generation) => IsPending && generation == Generation;
    }
}
