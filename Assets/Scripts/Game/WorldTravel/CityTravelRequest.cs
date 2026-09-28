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
        /// （同区换图落到别的节点时、以及每一次跨区传送都走它）。正常路径是存盘与等应答两道 30 秒的看门狗
        /// 前后串行，最坏约 60 秒有结论：要么入场通知到达，要么补推一条失败提示。
        /// 核实不了归属的情形（冻结期间服务端存储不可用）由服务端的冻结硬上限兜底：
        /// travel_freeze_cap::kFreezeCap = 70 秒（单调时钟、1 秒扫描，最迟约 71 秒出结论；
        /// 服务端 docs/design/cross-zone-scene-travel.md §12.3“冻结上限”，另案落码）。到期时要么解冻并推失败提示，
        /// 要么推失败提示后紧跟踢线 34（原因就是同一个提示码，客户端显示原因后回选服）。
        /// 所以本常量必须 ≥ 该上限 + 扫描 + 请求与提示的单程投递余量：按 71 秒算只余 4 秒。
        /// 两条路的起算点不同，这 4 秒要盖的东西也不同：
        /// 跨区时本预算从发请求之前就开始计（<see cref="GameClient.BeginZoneTravel"/>），服务端在请求到达源场景的
        /// 那次处理里就冻结，余量盖的是请求与提示各一次单程；
        /// 同区换图从受理应答到达才起算（CityTravelUiRoot 在 EnterScene 的应答回调里 Accept），而受理应答在源场景
        /// 把请求转给 scene_manager 之后就回了，服务端冻结要等 scene_manager 回复“需要交接”之后才开始，
        /// 所以这 4 秒还要盖住那一次往返。超出余量的后果：解冻提示晚于本预算到达时文案被吞（人仍在原地）；
        /// 踢线与在途状态无关，照样断线并带原因。
        /// 上限落地之前，存储不可用时服务端冻结没有上界，本常量到期后由地图窗“底层已不再等待”分支收场。
        /// 比它短的后果：客户端先收起遮罩报“未收到抵达消息”，玩家却还被服务端冻结着（走不动、再点被拒），
        /// 随后要么突然换图成功，要么失败提示到达时请求已不在途、原因被吞掉。
        /// 客户端分不清某次同区换图走没走交接（服务端不通知），只能一律按交接的预算等；
        /// 代价是普通换图的应答丢失时，遮罩也要等满这个数才收起。
        /// 服务端 travel_freeze_cap.h 的 kClientAcceptedHandoffBudget 镜像本常量，并用 static_assert 守住
        /// “上限 + 扫描 &lt; 本常量”；服务端改看门狗、改冻结上限，或这里改值时，两边必须同一批同步改。
        /// 跨区总上限 <see cref="CrossZoneTimeoutSeconds"/> 由本常量推出，会自动跟随。
        /// </summary>
        public const double AcceptedHandoffBudgetSeconds = 75.0;
        /// <summary>
        /// 跨区总上限在两段有界最坏值之上再留的余量（秒）。盖的是：换连接时 OpenGate 的同步建连
        /// （阻塞主线程，但计时照走）、各等待循环按帧检查的粒度、msg 124 进入 Poll 到换连接流程启动之间的派发延迟。
        /// 盖不住的残余：探测通过之后目标 gate 才变黑洞时，同步建连约阻塞 21 秒。
        /// </summary>
        public const double CrossZoneSlackSeconds = 15.0;
        /// <summary>
        /// 跨区传送的等待上限，由两段客户端自己的上界推出（服务端 docs/design/cross-zone-scene-travel.md §12.5.4 CL-8）：
        /// ① 还连着老 gate、等 msg 124 或失败提示：≤ <see cref="AcceptedHandoffBudgetSeconds"/>（75 秒）。
        ///    超过它底层就不再等待，地图窗走“底层已不再等待”分支收场，不靠本上限；
        ///    所以地图窗在途时收到 msg 124，一定是在这 75 秒之内。
        /// ② 收到 msg 124 之后换连接：≤ <see cref="GameClient.RedirectFlowWorstCaseSec"/>
        ///    （探测 5 + 验票 10 + Login 15 + EnterGame 15 + 等入场 60 = 105 秒）。
        /// 二者相加 180 秒，再加 <see cref="CrossZoneSlackSeconds"/> 15 秒，共 195 秒。
        /// 比它短，会在换连接途中先报“未收到抵达消息”、随后玩家又真的到了。
        /// 这个值只是换连接阶段的兜底：常见失败（失败提示、踢线、断线、底层 75 秒到期）都会更早收场。
        /// 常量表达式在编译期内联，任何一段改值都会跟着变；不要改回字面量。
        /// </summary>
        public const double CrossZoneTimeoutSeconds =
            AcceptedHandoffBudgetSeconds + GameClient.RedirectFlowWorstCaseSec + CrossZoneSlackSeconds;
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
