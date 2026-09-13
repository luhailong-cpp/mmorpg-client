namespace MmorpgClient.Game.WorldTravel
{
    /// <summary>记录传送请求，只有服务端入场通知才能确认抵达。</summary>
    public sealed class CityTravelRequest
    {
        public const double TimeoutSeconds = 30.0;
        public uint Destination { get; private set; }
        public bool Festival { get; private set; }
        public bool IsPending { get; private set; }
        public bool IsAccepted { get; private set; }
        public int Generation { get; private set; }
        private double _deadline;

        public int Begin(uint destination, bool festival, double now)
        {
            if (IsPending || destination == 0) return 0;
            ++Generation;
            Destination = destination;
            Festival = festival;
            IsPending = true;
            IsAccepted = false;
            _deadline = now + TimeoutSeconds;
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
