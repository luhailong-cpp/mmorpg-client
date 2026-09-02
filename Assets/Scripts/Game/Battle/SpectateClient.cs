using System;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Battle
{
    /// <summary>观战客户端相位(SpectateClient API 契约,UI 路只读)。</summary>
    public enum SpectatePhase
    {
        /// <summary>空闲:未观战。</summary>
        None,
        /// <summary>WatchBattle 已发出,等 NotifySpectateState 首帧(15s 超时)。</summary>
        Requesting,
        /// <summary>观战中:逐回合收 NotifySpectateTurnResult。</summary>
        Watching,
        /// <summary>收到 NotifySpectateEnd,等 UI 收尾后 AckEnd() 回 None。</summary>
        Ended,
    }

    /// <summary>
    /// 观战网络层状态机(只读:观众对战斗状态零写权,设计文档 §10 D8)。
    ///
    /// 与 <see cref="BattleClient"/> 是两套互不干扰的状态机(消息号不同),
    /// 结构与惯例照搬 BattleClient:网络依赖收敛在 <see cref="IBattleTransport"/>,
    /// 本类不引用 UnityEngine,时钟由宿主经 <see cref="Tick"/> 注入。
    ///
    /// 边界情况(均有 EditMode 测试覆盖):
    ///  - 随机观战(battle_id=0):首帧可能先于 WatchBattle 响应到达(Kafka 与 gRPC
    ///    两条链路无序),Requesting 期间未定 battle_id 时以先到者为准;
    ///  - 迟到/错发消息(battle_id 不匹配当前观战)丢弃;
    ///  - 首帧 15s 未到:收敛回 None 并报错(照 BattleClient 的 Preparing 超时惯例);
    ///  - 断线:本地观战态整体作废回 None(观战无重连恢复,重进走 WatchBattle)。
    /// </summary>
    public sealed class SpectateClient
    {
        /// <summary>单例挂接:与 BattleClient 模式一致(实例由 GameClient 持有,静态 Instance 供 UI 路解析;测试直接 new)。</summary>
        public static SpectateClient Instance { get; private set; }

        /// <summary>Requesting 相位超时(秒):WatchBattle 后首帧无下文时收敛回 None。</summary>
        public const double FirstFrameTimeoutSeconds = 15.0;

        private readonly IBattleTransport _net;

        private double _now;                  // 宿主注入的时钟(秒)
        private double _firstFrameDeadline;   // 首帧超时时刻(0 = 未挂)
        private ulong _battleId;              // 当前观战战斗 id;0 = 无(随机模式响应/首帧回填)

        // 随机观战响应未回(_battleId==0)时用户退出:battle_id 未知发不了有效
        // StopWatchBattle,置此标记等响应/首帧带回 battle_id 后补发退出。
        // 注意 ClearWatchContext 不得重置它:本地相位先收敛 None,标记要活到回填到达。
        private bool _stopPending;

        // ── 契约属性 ────────────────────────────────────────

        public SpectatePhase Phase { get; private set; } = SpectatePhase.None;

        /// <summary>最新权威战斗状态(首帧/回合结算更新;断线/退出清空)。</summary>
        public BattleStateS2C State { get; private set; }

        /// <summary>当前观众数(含本人,随 NotifySpectateState 更新)。</summary>
        public uint ObserverCount { get; private set; }

        /// <summary>当前观战的战斗 id(0 = 无/随机模式尚未回填)。</summary>
        public ulong WatchedBattleId => _battleId;

        public ulong MyPlayerId => _net.PlayerId;

        // ── 契约事件 ────────────────────────────────────────

        public event Action<SpectatePhase> OnPhaseChanged;
        public event Action<SpectateStateS2C> OnSpectateState;
        public event Action<TurnResultS2C> OnSpectateTurn;
        public event Action<SpectateEndS2C> OnSpectateEnd;
        public event Action<Match.ListWatchableBattlesResponse> OnListUpdated;
        public event Action<string> OnError;

        // ── 构造/挂接 ───────────────────────────────────────

        public SpectateClient(IBattleTransport transport)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            RegisterNotifies();
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>生产入口:创建实例并登记为单例(GameClient 构造时调用)。</summary>
        public static SpectateClient Attach(IBattleTransport transport)
        {
            var client = new SpectateClient(transport);
            Instance = client;
            return client;
        }

        /// <summary>宿主每帧驱动(GameClient.Tick),只负责首帧超时。</summary>
        public void Tick(double nowSeconds)
        {
            _now = nowSeconds;

            if (Phase == SpectatePhase.Requesting && _firstFrameDeadline > 0 && _now >= _firstFrameDeadline)
            {
                _firstFrameDeadline = 0;
                OnError?.Invoke("观战首帧超时,已退出观战");
                ClearWatchContext();
                SetPhase(SpectatePhase.None);
            }
        }

        // ── 契约方法 ────────────────────────────────────────

        /// <summary>随机观战(battle_id=0,由 match 从活跃索引挑一场)。</summary>
        public void WatchRandom() => WatchBattle(0);

        /// <summary>
        /// 观战指定战斗(battleId=0 为随机)。成功进 Requesting,收到
        /// NotifySpectateState 首帧才算进入观战(设计文档 §10.2);
        /// 服务端会拒绝排队/战斗中的玩家(D11 互斥),错误统一走 OnError。
        /// </summary>
        public void WatchBattle(ulong battleId)
        {
            if (Phase != SpectatePhase.None)
            {
                OnError?.Invoke("当前状态不能发起观战(需先退出当前观战)");
                return;
            }

            _stopPending = false; // 新观战作废旧的待补退出
            _battleId = battleId;
            SetPhase(SpectatePhase.Requesting);
            _firstFrameDeadline = _now + FirstFrameTimeoutSeconds; // SetPhase 会清,故在其后挂

            var req = new Match.WatchBattleRequest
            {
                PlayerId = MyPlayerId,
                BattleId = battleId,
            };
            _net.Call(MessageIds.WatchBattle, req, Match.WatchBattleResponse.Parser,
                resp =>
                {
                    // 用户已在响应回来前退出(随机模式待补退出):拿到 battle_id 立即补发
                    if (_stopPending)
                    {
                        _stopPending = false;
                        if (!HasTip(resp.ErrorMessage) && resp.BattleId != 0)
                            SendStopWatch(resp.BattleId);
                        return;
                    }
                    // 首帧已先到(Watching)或已超时/退出:迟到响应不改状态
                    if (Phase != SpectatePhase.Requesting) return;
                    if (HasTip(resp.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeTip("观战请求失败", resp.ErrorMessage));
                        ClearWatchContext();
                        SetPhase(SpectatePhase.None);
                        return;
                    }
                    // 随机模式:响应回填实际战斗 id(若首帧未抢先回填)
                    if (_battleId == 0 && resp.BattleId != 0) _battleId = resp.BattleId;
                },
                err =>
                {
                    if (Phase != SpectatePhase.Requesting) return;
                    OnError?.Invoke($"观战请求失败:{err}");
                    ClearWatchContext();
                    SetPhase(SpectatePhase.None);
                });
        }

        /// <summary>
        /// 主动退出观战:本地立即收敛回 None(UI 响应优先),服务端移除是
        /// 尽力而为(StopWatchBattle 经 gate 按绑定路由,失败只报错不改相位)。
        /// </summary>
        public void StopWatch()
        {
            if (Phase != SpectatePhase.Requesting && Phase != SpectatePhase.Watching)
            {
                OnError?.Invoke("当前不在观战中");
                return;
            }

            // Requesting 且 battle_id 未知(随机观战响应未回):此刻发出去只能是
            // battle_id=0 的无效请求,改挂待补标记,等响应/首帧回填后补发退出。
            if (Phase == SpectatePhase.Requesting && _battleId == 0)
                _stopPending = true;
            else
                SendStopWatch(_battleId);

            ClearWatchContext();
            State = null;
            ObserverCount = 0;
            SetPhase(SpectatePhase.None);
        }

        /// <summary>拉取可观战列表(limit=0 用服务端默认条数),结果经 OnListUpdated 透传,不改相位。</summary>
        public void RefreshList(uint limit)
        {
            var req = new Match.ListWatchableBattlesRequest
            {
                PlayerId = MyPlayerId,
                Limit = limit,
            };
            _net.Call(MessageIds.ListWatchableBattles, req, Match.ListWatchableBattlesResponse.Parser,
                resp => OnListUpdated?.Invoke(resp),
                err => OnError?.Invoke($"拉取观战列表失败:{err}"));
        }

        /// <summary>UI 收尾(结束面板收起)后调用(契约):Ended → None。迟到 Ack 丢弃。</summary>
        public void AckEnd()
        {
            if (Phase != SpectatePhase.Ended) return;
            ClearWatchContext();
            State = null;
            ObserverCount = 0;
            SetPhase(SpectatePhase.None);
        }

        // ── S2C 推送处理 ────────────────────────────────────

        private void RegisterNotifies()
        {
            _net.RegisterNotify(MessageIds.NotifySpectateState,
                mc => HandleSpectateState(SpectateStateS2C.Parser.ParseFrom(mc.SerializedMessage)));
            _net.RegisterNotify(MessageIds.NotifySpectateTurnResult,
                mc => HandleSpectateTurnResult(TurnResultS2C.Parser.ParseFrom(mc.SerializedMessage)));
            _net.RegisterNotify(MessageIds.NotifySpectateEnd,
                mc => HandleSpectateEnd(SpectateEndS2C.Parser.ParseFrom(mc.SerializedMessage)));
        }

        private void HandleSpectateState(SpectateStateS2C ev)
        {
            if (ev?.State == null) return;
            // 待补退出:首帧抢先于 WatchBattle 响应带回 battle_id 时同样补发
            //(须在相位守卫前:StopWatch 已把相位收敛 None)
            if (_stopPending && ev.State.BattleId != 0)
            {
                _stopPending = false;
                SendStopWatch(ev.State.BattleId);
                return;
            }
            if (Phase != SpectatePhase.Requesting && Phase != SpectatePhase.Watching) return;

            ulong incoming = ev.State.BattleId;
            // 迟到/错发的首帧丢弃;Requesting 且 battle_id 未定(随机模式响应未回)时以首帧为准
            if (_battleId != 0 && incoming != 0 && incoming != _battleId) return;
            if (_battleId == 0 && incoming != 0) _battleId = incoming;

            State = ev.State;
            ObserverCount = ev.ObserverCount;

            // 事件先于终相位(照 BattleClient 惯例):UI 先拿首帧开屏,再收相位刷新
            OnSpectateState?.Invoke(ev);
            if (Phase == SpectatePhase.Requesting) SetPhase(SpectatePhase.Watching);
        }

        private void HandleSpectateTurnResult(TurnResultS2C ev)
        {
            if (ev == null) return;
            if (Phase != SpectatePhase.Watching) return;
            if (_battleId != 0 && ev.BattleId != 0 && ev.BattleId != _battleId) return;

            if (ev.State != null) State = ev.State;
            OnSpectateTurn?.Invoke(ev); // 观众无 Resolving 相位:只读流,UI 按自己的节奏播
        }

        private void HandleSpectateEnd(SpectateEndS2C ev)
        {
            if (ev == null) return;
            // Requesting 也接受:AddObserver 与战斗收尾可竞速(首帧未到战斗先结束)
            if (Phase != SpectatePhase.Requesting && Phase != SpectatePhase.Watching) return;
            if (_battleId != 0 && ev.BattleId != 0 && ev.BattleId != _battleId) return;

            SetPhase(SpectatePhase.Ended);
            OnSpectateEnd?.Invoke(ev); // UI 展示结束原因后调 AckEnd() 回 None
        }

        private void HandleDisconnected()
        {
            // 断线:观战态整体作废(服务端绑定随 session 失效,重进走 WatchBattle);
            // 待补退出一并作废(session 失效服务端自会清绑定,无需再补发)
            _stopPending = false;
            ClearWatchContext();
            State = null;
            ObserverCount = 0;
            SetPhase(SpectatePhase.None);
        }

        // ── 内部工具 ────────────────────────────────────────

        /// <summary>发退出观战请求(尽力而为:失败只报错不改相位;主动退出与待补退出共用)。</summary>
        private void SendStopWatch(ulong battleId)
        {
            var req = new StopWatchBattleRequest { BattleId = battleId };
            _net.Call(MessageIds.StopWatchBattle, req, StopWatchBattleResponse.Parser,
                resp =>
                {
                    if (HasTip(resp.ErrorMessage))
                        OnError?.Invoke(DescribeTip("退出观战失败", resp.ErrorMessage));
                },
                err => OnError?.Invoke($"退出观战失败:{err}"));
        }

        private void ClearWatchContext()
        {
            _battleId = 0;
            _firstFrameDeadline = 0;
        }

        private void SetPhase(SpectatePhase phase)
        {
            if (Phase == phase) return;
            if (phase != SpectatePhase.Requesting) _firstFrameDeadline = 0;
            Phase = phase;
            OnPhaseChanged?.Invoke(phase);
        }

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeTip(string what, TipInfoMessage tip)
            => tip != null && tip.Id != 0 ? $"{what}(tip={tip.Id})" : what;
    }
}
