namespace MmorpgClient.Game.WorldTravel
{
    /// <summary>记录传送请求，只有服务端入场通知才能确认抵达。</summary>
    public sealed class CityTravelRequest
    {
        public const double TimeoutSeconds = 30.0;
        /// <summary>
        /// 跨区传送的等待上限。同区换图只有一次场景切换，30 秒足够；跨区要先等源场景冻结并存盘
        /// （服务端预算 30 秒），再换连接、验票、重新登录、进游戏、等入场通知，几段预算相加远超 30 秒。
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
        /// <paramref name="timeoutSeconds"/> 不传即同区换图的 <see cref="TimeoutSeconds"/>；
        /// 做成参数而不是改常量，是因为现有测试与同区体验都依赖 30 秒。非正数按默认值处理。
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
