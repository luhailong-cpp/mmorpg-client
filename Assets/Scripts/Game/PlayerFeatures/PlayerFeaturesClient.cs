using System;
using System.Globalization;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
// 强化前置检查、宝石数量汇总与孔位查找只有 EquipBagModel 这一份(界面置灰与这里的本地拒绝 / 重拉判定同口径)。
// 用别名只引这两个纯逻辑类型,不把整个界面命名空间带进数据层。
using EquipBagModel = MmorpgClient.UI.Ugui.Gameplay.EquipBagModel;
using EquipEnhanceCheck = MmorpgClient.UI.Ugui.Gameplay.EquipEnhanceCheck;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>
    /// 一次强化的结果。只有被服务器受理的强化才有(被拒绝的请求走 OnError);
    /// Success == false 是「掷骰没过」:金币与材料已扣,不是错误。
    /// </summary>
    public readonly struct EquipEnhanceOutcome
    {
        public ulong ItemId { get; }
        public bool Success { get; }
        /// <summary>操作后的强化等级(成功 = 原等级 + 1;没过时按表不变或回落)。</summary>
        public uint Level { get; }

        public EquipEnhanceOutcome(ulong itemId, bool success, uint level)
        {
            ItemId = itemId;
            Success = success;
            Level = level;
        }
    }

    /// <summary>
    /// Player-owned bag, mission and activity snapshots over the existing gate transport.
    /// Lists are server authoritative. A request is bound to its player and connection
    /// epoch so late callbacks cannot repopulate another character's windows.
    /// 背包快照按 bag_type 各存各的(0 人物背包 / 1 仓库 / 2 装备栏 / 3 临时格),互不清空;
    /// 穿上 / 卸下的回包带人物背包与装备栏两份全量,本地整体覆盖,不做增量推算
    /// (服务端 docs/design/equipment-attributes.md §3.2 / §6)。
    /// 穿脱失败不作废在途的读取;失败后服务器状态未知(超时、回包缺快照等)时自动重拉这两份快照。
    /// 强化 / 镶嵌 / 取下宝石与穿脱共用同一个在途标志和同一套回包处理
    /// (服务端 docs/design/equipment-enhance-inlay.md §6.1):回包同样带两份全量;强化掷骰没过不是失败
    /// (没有 tip,快照照常覆盖),结果经 <see cref="OnEnhanceResult"/> 单独通知。
    /// </summary>
    public sealed class PlayerFeaturesClient : IDisposable
    {
        public static PlayerFeaturesClient Instance { get; private set; }

        /// <summary>bag_type 取值,见服务端 proto/scene/player_bag.proto 的 BagLayoutInfo.bag_type。</summary>
        public const uint CharacterBagType = 0;
        public const uint EquipmentBagType = 2;
        /// <summary>
        /// 金币在 BagInfo.Currency.Values 里的下标;镜像服务端
        /// cpp/libs/modules/currency/constants/currency.h 的 kCurrencyGold。
        /// </summary>
        public const int GoldCurrencyIndex = 0;
        /// <summary>
        /// 强化 / 镶嵌 / 取下 两次发包的最小间隔(毫秒)。超限的包会被 gate 计成非法包,累计到阈值就断线,
        /// 所以数据层自己把连点压在限流档位之内。取 700 是按**默认档**留的:服务端限流器是「相邻两秒合计 N 个」,
        /// 这三个方法进限流表之前吃默认档 3(持续连点要 ≥ 667 毫秒才不超限);进表之后档位更宽
        /// (强化 10、镶嵌 / 取下 5),700 同样够用。
        /// </summary>
        public const long ForgeMinIntervalMs = 700;
        private const uint MaxBagType = 3;
        // 与 GameClient.Call 把信封错误码折算成 onError 串的前缀逐字一致;两边改动必须同步。
        private const string EnvelopeTipPrefix = "server tip=";
        private const string NetworkErrorText = "网络异常,请稍后重试";
        // 「{操作名}:{原因}」里的操作名。强化不叫「强化失败」:那会和掷骰没过(不是错误)混在一起。
        private const string EnhanceOperation = "无法强化";
        private const string InlayOperation = "镶嵌失败";
        private const string RemoveGemOperation = "取下失败";
        // 默认时钟:进程内单调,不受改系统时间影响。
        private static readonly System.Diagnostics.Stopwatch MonotonicClock = System.Diagnostics.Stopwatch.StartNew();

        private readonly IBattleTransport _net;
        private readonly BagInfo[] _bags = new BagInfo[MaxBagType + 1];
        private ulong _snapshotPlayerId;
        private int _epoch;
        private int _bagRequest;
        private int _equipmentRequest;
        // 装备类操作(穿上 / 卸下 / 强化 / 镶嵌 / 取下)共用的序号:只用来认出上一次操作的迟到回调,不作废任何读取。
        private int _equipActionRequest;
        private int _missionRequest;
        private int _activityRequest;
        // 装备类操作在途期间被挡下的刷新(人物背包 / 装备栏),操作收口后补发。
        private bool _bagReadDeferred;
        private bool _equipmentReadDeferred;
        // 装备栏读取失败与穿脱失败分开存:穿脱失败后可能自动重拉两份快照,重拉成功不能把失败原因冲掉。
        private string _equipmentReadError = string.Empty;
        private string _equipActionError = string.Empty;
        // 上一次强化 / 镶嵌 / 取下的发包时刻(_nowMs 的读数);null = 还没发过。
        // 断线 / 换角色不清:它只管发包节奏,与快照属于谁无关。
        private long? _lastForgeSendMs;
        private Func<long> _nowMs = DefaultNowMs;
        private bool _disposed;

        /// <summary>当前查看的那一类背包(<see cref="RequestedBagType"/>)的快照;切到另一类且还没拉到过时为 null。</summary>
        public BagInfo Bag => _bags[RequestedBagType];
        /// <summary>
        /// 人物背包(bag_type = 0)快照。<see cref="Bag"/> 是「当前查看的那一类」,可能是仓库;
        /// 强化的金币 / 材料与镶嵌的宝石只认这一份。
        /// </summary>
        public BagInfo CharacterBag => _bags[CharacterBagType];
        /// <summary>装备栏(bag_type = 2)快照:槽位定义在 Layout.EquipSlots(含空槽),占用关系在 Layout.Slots。</summary>
        public BagInfo Equipment => _bags[EquipmentBagType];
        public GetMissionListResponse Missions { get; private set; }
        public GetActivityListResponse Activities { get; private set; }
        public bool HasBag => Bag != null;
        public bool HasEquipment => Equipment != null;
        public bool HasMissions => Missions != null;
        public bool HasActivities => Activities != null;
        public bool BagLoading { get; private set; }
        public bool EquipmentLoading { get; private set; }
        public bool MissionsLoading { get; private set; }
        public bool ActivitiesLoading { get; private set; }
        public bool BusySort { get; private set; }
        /// <summary>穿上 / 卸下 / 强化 / 镶嵌 / 取下在途(UI 据此禁用按钮);这五种操作同一时刻只允许一个。</summary>
        public bool BusyEquip { get; private set; }
        public bool BusyMissionAction { get; private set; }
        public uint RequestedBagType { get; private set; }
        public string BagError { get; private set; } = string.Empty;
        /// <summary>
        /// 背包页状态行显示的装备侧错误:上一次装备类操作(穿上 / 卸下 / 强化 / 镶嵌 / 取下)的失败原因优先,
        /// 其次是装备栏读取失败。
        /// </summary>
        public string EquipmentError => _equipActionError.Length != 0 ? _equipActionError : _equipmentReadError;
        /// <summary>
        /// 装备栏读取本身的失败原因(不含装备类操作的失败);下一次读取开始、读取成功、或下一次装备类操作开始时清空。
        /// 强化 / 镶嵌面板用它区分「快照没同步上」与「操作被拒」。
        /// </summary>
        public string EquipmentReadError => _equipmentReadError;
        public string MissionsError { get; private set; } = string.Empty;
        public string ActivitiesError { get; private set; } = string.Empty;
        /// <summary>
        /// 最近一次强化的结果;没有则为 null。任何一次装备类操作开始(通过连点闸之后,无论随后是报错还是发包)、
        /// 断线、换角色时清空,所以它不会与装备类操作的失败原因同时存在。
        /// </summary>
        public EquipEnhanceOutcome? LastEnhanceOutcome { get; private set; }
        /// <summary>
        /// 距离下一次强化 / 镶嵌 / 取下可以发包还剩多少毫秒(0 = 现在就能发)。只给界面置灰用的读数:
        /// 闸本身仍在 PerformEquipAction 里,间隔内的点击照样静默丢弃。与闸同口径 —— 时钟读数倒退(时钟被换过)时为 0。
        /// 读数会随时间自己变小却不触发 <see cref="OnChanged"/>,界面要按帧(或定时)重新读它。
        /// </summary>
        public long ForgeCooldownRemainingMs
        {
            get
            {
                if (!_lastForgeSendMs.HasValue) return 0;
                long elapsed = _nowMs() - _lastForgeSendMs.Value;
                return elapsed < 0 || elapsed >= ForgeMinIntervalMs ? 0 : ForgeMinIntervalMs - elapsed;
            }
        }
        /// <summary>
        /// 发包间隔用的毫秒时钟,默认是进程内单调时钟;只给用例替换,传 null 恢复默认。
        /// 不走构造函数(GameClient 里那一处构造调用是别的会话的在途文件);
        /// 测试程序集没有 InternalsVisibleTo,所以只能是 public。
        /// </summary>
        public Func<long> NowMs
        {
            get => _nowMs;
            set => _nowMs = value ?? DefaultNowMs;
        }

        public event Action OnChanged;
        public event Action<string> OnError;
        /// <summary>
        /// 一次强化被服务器受理(成功或掷骰没过)。在两份快照覆盖完、<see cref="OnChanged"/> 之后触发,每次强化一次;
        /// 被拒绝、超时、回包缺快照都不触发(走 <see cref="OnError"/>)。
        /// </summary>
        public event Action<EquipEnhanceOutcome> OnEnhanceResult;

        public PlayerFeaturesClient(IBattleTransport transport)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _net.Disconnected += HandleDisconnected;
        }

        public static PlayerFeaturesClient Attach(IBattleTransport transport)
        {
            var client = new PlayerFeaturesClient(transport);
            Instance = client;
            return client;
        }

        public void RequestBag(uint bagType = 0)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (bagType > MaxBagType) { FailBag("背包类型不存在"); return; }
            if (BusySort) { FailBag("背包正在整理，请稍候"); return; }
            if (BusyEquip)
            {
                // 正在看的这一类会被穿脱回包整体覆盖:不发、也不报错(报了会盖住穿脱自己的失败原因),
                // 记下这一次刷新,穿脱收口后补发。切到别的类型仍然要等。
                if (bagType == RequestedBagType && ViewingEquipAffectedBag) { _bagReadDeferred = true; return; }
                FailBag("装备正在更换，请稍候");
                return;
            }

            int epoch = _epoch;
            int request = ++_bagRequest;
            RequestedBagType = bagType;
            BagLoading = true;
            BagError = string.Empty;
            OnChanged?.Invoke();
            _net.Call(SceneBagClientPlayerRpc.GetBagMessageId,
                new GetBagRequest { BagType = bagType }, GetBagResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _bagRequest) return;
                    BagLoading = false;
                    if (HasTip(response.ErrorMessage)) { FailBag(DescribeTip("读取背包失败", response.ErrorMessage)); return; }
                    if (!ValidBag(response.Bag, bagType)) { FailBag("服务器未返回有效的背包数据"); return; }
                    _bags[bagType] = response.Bag;
                    BagError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _bagRequest) return;
                    BagLoading = false;
                    FailBag(error);
                });
        }

        /// <summary>
        /// 拉取装备栏(= GetBag(bag_type = 2))。与 <see cref="RequestBag"/> 各走各的序号:
        /// 不改 <see cref="RequestedBagType"/>,不清人物背包快照,也不作废它的在途请求。
        /// </summary>
        public void RequestEquipment()
        {
            if (!PrepareRequest(out var playerId)) return;
            // 穿脱回包自带装备栏全量:不发、也不报错,记下这一次刷新,穿脱收口后补发。
            if (BusyEquip) { _equipmentReadDeferred = true; return; }
            _equipActionError = string.Empty; // 玩家主动刷新:上一次穿脱的失败原因一并收起
            ReadEquipment(playerId);
        }

        /// <summary>发出装备栏读取。不动穿脱的失败原因:穿脱失败后的自动重拉也走这里。</summary>
        private void ReadEquipment(ulong playerId)
        {
            int epoch = _epoch;
            int request = ++_equipmentRequest;
            EquipmentLoading = true;
            _equipmentReadError = string.Empty;
            OnChanged?.Invoke();
            _net.Call(SceneBagClientPlayerRpc.GetBagMessageId,
                new GetBagRequest { BagType = EquipmentBagType }, GetBagResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _equipmentRequest) return;
                    EquipmentLoading = false;
                    if (HasTip(response.ErrorMessage)) { FailEquipment(DescribeTip("读取装备栏失败", response.ErrorMessage)); return; }
                    if (!ValidBag(response.Bag, EquipmentBagType)) { FailEquipment("服务器未返回有效的装备栏数据"); return; }
                    _bags[EquipmentBagType] = response.Bag;
                    _equipmentReadError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _equipmentRequest) return;
                    EquipmentLoading = false;
                    // 这条原因会显示在背包页状态行:传输层的内部英文串不上屏。
                    FailEquipment(TryParseEnvelopeTip(error, out uint tipId)
                        ? DescribeTip("读取装备栏失败", new TipInfoMessage { Id = tipId })
                        : $"读取装备栏失败:{NetworkErrorText}");
                });
        }

        /// <summary>穿上人物背包里的一件装备(同部位已满时由服务器替换槽号最小的那件,旧装备回背包)。</summary>
        public void EquipItem(ulong itemId) => PerformEquipAction("装备失败", false,
            () => itemId == 0 ? "请先选择要装备的物品" : null,
            (complete, fail) => _net.Call(MessageIds.EquipItem, new EquipItemRequest { ItemId = itemId },
                EquipItemResponse.Parser,
                response => complete(response.ErrorMessage, response.Bag, response.Equipment), fail));

        /// <summary>卸下装备栏里的一件装备,回人物背包(背包满由服务器拒绝)。</summary>
        public void UnequipItem(ulong itemId) => PerformEquipAction("卸下失败", false,
            () => itemId == 0 ? "请先选择要卸下的装备" : null,
            (complete, fail) => _net.Call(MessageIds.UnequipItem, new UnequipItemRequest { ItemId = itemId },
                UnequipItemResponse.Parser,
                response => complete(response.ErrorMessage, response.Bag, response.Equipment), fail));

        /// <summary>
        /// 强化一件装备(人物背包里的或穿在身上的;仓库 / 临时格里的不行)。金币与材料成败都扣。
        /// 结果看 <see cref="OnEnhanceResult"/> / <see cref="LastEnhanceOutcome"/>:掷骰没过不是错误,不走 OnError。
        /// 金币 / 材料够不够不在这里拦(快照可能偏旧,拦了会把人锁死),由服务器裁决;
        /// 按钮置灰用 EquipBagModel.CheckEnhance。
        /// </summary>
        public void EnhanceEquip(ulong itemId)
        {
            // 发包那一刻本地快照对「金币 / 材料够不够」的判断。被这两个码拒绝时据此区分
            // 「本来就知道不够」(不重拉)与「本地以为够」(快照过期,重拉)。
            var knownAtSend = EquipEnhanceCheck.Unknown;
            string RejectLocally()
            {
                if (itemId == 0) return "请先选择要强化的装备";
                if (!TryFindForgeTarget(itemId, out var target)) return null;
                if (target == null) return $"{EnhanceOperation}:{ItemNotFoundText}";
                if (target.EnhanceMaxLevel == 0) return $"{EnhanceOperation}:{EnhanceUnavailableText}";
                if (target.EnhanceLevel >= target.EnhanceMaxLevel) return $"{EnhanceOperation}:{EnhanceMaxLevelText}";
                knownAtSend = EquipBagModel.CheckEnhance(target, CharacterBag);
                return null;
            }
            PerformEquipAction(EnhanceOperation, true, RejectLocally,
                (complete, fail) => _net.Call(MessageIds.EnhanceEquip, new EnhanceEquipRequest { ItemId = itemId },
                    EnhanceEquipResponse.Parser,
                    response => complete(response.ErrorMessage, response.Bag, response.Equipment,
                        () => LastEnhanceOutcome = new EquipEnhanceOutcome(itemId, response.Success, response.EnhanceLevel)),
                    fail),
                tipId => IsRuleRejection(tipId, knownAtSend));
        }

        /// <summary>
        /// 把人物背包里的一颗宝石嵌进装备的指定孔位(孔位号 0 起)。按宝石的 config 消耗 1 颗,不指定哪一堆;
        /// 孔里已有宝石要先取下。类型合不合由服务器裁决,候选列表用 EquipBagModel.GemChoices。
        /// </summary>
        public void InlayGem(ulong itemId, uint socket, uint gemConfigId)
        {
            string RejectLocally()
            {
                if (itemId == 0) return "请先选择要镶嵌的装备";
                if (gemConfigId == 0) return "请先选择要镶嵌的宝石";
                if (!TryFindForgeTarget(itemId, out var target)) return null;
                if (target == null) return $"{InlayOperation}:{ItemNotFoundText}";
                var hole = EquipBagModel.FindGemSocket(target, socket);
                if (hole == null) return $"{InlayOperation}:{GemSocketInvalidText}";
                if (hole.GemConfigId != 0) return $"{InlayOperation}:{GemSocketOccupiedText}";
                if (EquipBagModel.CountOf(CharacterBag, gemConfigId) == 0) return $"{InlayOperation}:{GemNotOwnedText}";
                return null;
            }
            PerformEquipAction(InlayOperation, true, RejectLocally,
                (complete, fail) => _net.Call(MessageIds.InlayGem,
                    new InlayGemRequest { ItemId = itemId, Socket = socket, GemConfigId = gemConfigId },
                    InlayGemResponse.Parser,
                    response => complete(response.ErrorMessage, response.Bag, response.Equipment), fail));
        }

        /// <summary>取下指定孔位的宝石,原样退回 1 颗到人物背包(背包放不下由服务器拒绝)。</summary>
        public void RemoveGem(ulong itemId, uint socket)
        {
            string RejectLocally()
            {
                if (itemId == 0) return "请先选择要取下宝石的装备";
                if (!TryFindForgeTarget(itemId, out var target)) return null;
                if (target == null) return $"{RemoveGemOperation}:{ItemNotFoundText}";
                // 快照里列着这个孔、而且是空的,才在本地拒绝。没列出来的孔不拦:策划删掉孔位定义之后,
                // 孔里的宝石仍然可以按孔位号取下(服务端设计文档 §8),有没有由服务器说了算。
                var hole = EquipBagModel.FindGemSocket(target, socket);
                if (hole != null && hole.GemConfigId == 0) return $"{RemoveGemOperation}:{GemSocketEmptyText}";
                return null;
            }
            PerformEquipAction(RemoveGemOperation, true, RejectLocally,
                (complete, fail) => _net.Call(MessageIds.RemoveGem,
                    new RemoveGemRequest { ItemId = itemId, Socket = socket }, RemoveGemResponse.Parser,
                    response => complete(response.ErrorMessage, response.Bag, response.Equipment), fail));
        }

        /// <summary>
        /// 这件物品是否穿在身上。「穿着」= 它在装备栏快照里,实例上没有已装备标记
        /// (服务端设计文档 §4.3);装备栏还没拉到时一律 false。
        /// </summary>
        public bool IsEquipped(ulong itemId)
        {
            if (itemId == 0 || Equipment == null) return false;
            foreach (var item in Equipment.Items)
                if (item.ItemId == itemId) return true;
            return false;
        }

        /// <summary>
        /// 强化 / 镶嵌 / 取下的目标:人物背包里的或身上穿着的(与服务端 LocateEquipment 同序:先背包后装备栏)。
        /// 返回 false = 两份快照有一份还没拉到,本地无从判断:不做本地拒绝,直接发包让服务器裁决。
        /// 返回 true 而 target 为 null = 两份快照里都没有这件物品。
        /// </summary>
        private bool TryFindForgeTarget(ulong itemId, out BagItemInfo target)
        {
            target = null;
            if (CharacterBag == null || Equipment == null) return false;
            foreach (var item in CharacterBag.Items)
                if (item.ItemId == itemId) { target = item; return true; }
            foreach (var item in Equipment.Items)
                if (item.ItemId == itemId) { target = item; return true; }
            return true;
        }

        /// <summary>
        /// 装备类操作的响应体出口:tip、人物背包、装备栏;applied 在两份快照覆盖之后、通知之前执行
        /// (强化在这里记下结果),被拒绝或缺快照时不执行。
        /// </summary>
        private delegate void EquipActionReply(TipInfoMessage tip, BagInfo bag, BagInfo equipment, Action applied = null);

        /// <summary>
        /// 穿上 / 卸下 / 强化 / 镶嵌 / 取下共用的执行骨架:同一个在途标志(BusyEquip)、同一个请求序号,
        /// 互斥、在途期间刷新的延后补发、旧连接 / 旧角色回包的拒绝都只有这一份。
        /// </summary>
        /// <param name="operation">「{操作名}:{原因}」里的操作名。</param>
        /// <param name="forge">强化 / 镶嵌 / 取下:受 <see cref="ForgeMinIntervalMs"/> 的发包间隔约束;穿脱不受。</param>
        /// <param name="rejectLocally">发包前的本地检查:返回拒绝文案,null = 放行。确认了角色与互斥之后才调用。</param>
        /// <param name="send">发包:响应体交给第一个回调,传输层 / 信封错误交给第二个。</param>
        /// <param name="isRuleRejection">这个拒绝码是否不必重拉;不传 = IsRuleRejection 的通用判定。</param>
        private void PerformEquipAction(string operation, bool forge, Func<string> rejectLocally,
            Action<EquipActionReply, Action<string>> send, Func<uint, bool> isRuleRejection = null)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyEquip) return; // 连点:上一次还没回包,不发第二个包也不报错
            // 连点的另一种形态:上一包已经回来,但离它发出还不到最小间隔。同样不发、不报错。
            // 差值为负 = 时钟被换过:放行(宁可多发一包,不能把人永久锁在间隔里)。
            long now = forge ? _nowMs() : 0;
            if (forge && _lastForgeSendMs.HasValue && now >= _lastForgeSendMs.Value
                && now - _lastForgeSendMs.Value < ForgeMinIntervalMs) return;
            // 这一次点击会有可见的结果(报错或进入在途):上一次强化的结果不再是「最近一次」。
            LastEnhanceOutcome = null;
            if (BusyMissionAction) { FailEquipAction("任务正在处理中，请稍候"); return; }
            if (BusySort) { FailEquipAction("背包正在整理，请稍候"); return; }
            string localError = rejectLocally();
            if (localError != null) { FailEquipAction(localError); return; }

            int epoch = _epoch;
            // 在途的读取此刻不作废:穿脱被拒时服务器状态没变,它们照常落地;成功时才作废(见 Complete)。
            int request = ++_equipActionRequest;
            BusyEquip = true;
            _equipActionError = _equipmentReadError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || request != _equipActionRequest) return;

            isRuleRejection ??= tipId => IsRuleRejection(tipId);
            // resync:服务器状态未知,或本地快照已被证明过期 —— 两份快照都重新拉一遍(与在途期间被挡下的
            // 刷新一起补发)。规则类拒绝不拉:状态没变,而且每次拒绝都补两个读会把连点放大成 gate 限流。
            void Reject(string message, bool resync)
            {
                BusyEquip = false;
                if (resync)
                {
                    _equipmentReadDeferred = true;
                    if (ViewingEquipAffectedBag) _bagReadDeferred = true;
                }
                FailEquipAction(message);
                SendDeferredReads(epoch, playerId);
            }
            void Complete(TipInfoMessage tip, BagInfo bag, BagInfo equipment, Action applied = null)
            {
                if (!IsCurrent(epoch, playerId) || request != _equipActionRequest) return;
                // 业务拒绝码在响应体里(信封只承载 gate 级错误):有 tip 就保留旧快照。
                if (HasTip(tip)) { Reject(DescribeEquipTip(operation, tip), !isRuleRejection(tip.Id)); return; }
                // 没有 tip 却缺快照:操作已经生效(强化连金币材料都扣了),只是没带回来,本地两份都过期了。
                if (!ValidBag(bag, CharacterBagType) || !ValidBag(equipment, EquipmentBagType))
                { Reject(forge ? "服务器未返回最新的背包与装备栏" : "服务器未返回更换后的背包与装备栏", true); return; }
                BusyEquip = false;
                // 回包整体覆盖人物背包与装备栏:这两类此前发出、还没回来的读取到这里才作废,免得更旧的
                // 读回包盖掉写回包。正在看仓库 / 临时格时,那一类的在途读取与穿脱无关,不动它。
                ++_equipmentRequest;
                EquipmentLoading = false;
                if (ViewingEquipAffectedBag) { ++_bagRequest; BagLoading = false; BagError = string.Empty; }
                _bags[CharacterBagType] = bag;
                _bags[EquipmentBagType] = equipment;
                _equipActionError = _equipmentReadError = string.Empty;
                applied?.Invoke();
                // 操作开始时已清空,此刻非空只可能是 applied 刚写的这一次强化。
                var enhanced = LastEnhanceOutcome;
                OnChanged?.Invoke();
                // 强化结果单独通知一次。OnChanged 的监听者可能已经换了角色,或又发起了一次装备类操作:先复核。
                if (enhanced.HasValue && IsCurrent(epoch, playerId) && request == _equipActionRequest)
                    OnEnhanceResult?.Invoke(enhanced.Value);
                SendDeferredReads(epoch, playerId);
            }
            void Fail(string error)
            {
                if (!IsCurrent(epoch, playerId) || request != _equipActionRequest) return;
                // 信封上的码(gate / 路由层)与响应体里的码走同一套文案、同一个判定;其余是传输层失败
                // (超时 / 断线 / 解析失败):内部英文串不上屏,而且服务器可能已经执行,要重新同步。
                // 绝不自动重发:强化不是幂等操作,再发一次就是再扣一次。
                if (TryParseEnvelopeTip(error, out uint tipId))
                    Reject(DescribeEquipTip(operation, new TipInfoMessage { Id = tipId }), !isRuleRejection(tipId));
                else Reject($"{operation}:{NetworkErrorText}", true);
            }
            if (forge) _lastForgeSendMs = now;
            send(Complete, Fail);
        }

        /// <summary>
        /// 穿脱收口后补发攒下的读取:在途期间被挡下的刷新,以及失败后的重新同步。
        /// 每发一个之前都重新确认 —— 前面的通知里可能又点了一次穿脱(留给它的收口),或已经换了角色(标记已清)。
        /// </summary>
        private void SendDeferredReads(int epoch, ulong playerId)
        {
            if (_bagReadDeferred && !BusyEquip && IsCurrent(epoch, playerId))
            { _bagReadDeferred = false; RequestBag(RequestedBagType); }
            if (_equipmentReadDeferred && !BusyEquip && IsCurrent(epoch, playerId))
            { _equipmentReadDeferred = false; ReadEquipment(playerId); }
        }

        /// <summary>Called only by the player's explicit arrange button, never on open.</summary>
        public void SortBag()
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyMissionAction) { FailBag("任务正在处理中，请稍候"); return; }
            if (BusyEquip) { FailBag("装备正在更换，请稍候"); return; }
            if (BusySort || BagLoading) { FailBag("背包正在处理中，请稍候"); return; }
            if (Bag?.Layout == null) { FailBag("请先读取背包"); return; }
            if (!Bag.Layout.CanSort || Bag.Layout.BagType > 1) { FailBag("此背包暂不支持整理"); return; }

            int epoch = _epoch;
            int request = ++_bagRequest;
            uint bagType = Bag.Layout.BagType;
            BusySort = true;
            BagError = string.Empty;
            OnChanged?.Invoke();
            _net.Call(SceneBagClientPlayerRpc.SortBagMessageId,
                new SortBagRequest { BagType = bagType }, SortBagResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _bagRequest) return;
                    BusySort = false;
                    if (HasTip(response.ErrorMessage)) { FailBag(DescribeTip("整理背包失败", response.ErrorMessage)); return; }
                    if (!ValidBag(response.Bag, bagType)) { FailBag("服务器未返回整理后的背包"); return; }
                    _bags[bagType] = response.Bag;
                    BagError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _bagRequest) return;
                    BusySort = false;
                    FailBag(error);
                });
        }

        public void RequestMissions()
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyMissionAction) return;
            int epoch = _epoch;
            int request = ++_missionRequest;
            MissionsLoading = true;
            MissionsError = string.Empty;
            OnChanged?.Invoke();
            _net.Call(SceneMissionClientPlayerRpc.GetMissionListMessageId,
                new GetMissionListRequest(), GetMissionListResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
                    MissionsLoading = false;
                    if (HasTip(response.ErrorMessage)) { FailMissions(DescribeTip("读取任务失败", response.ErrorMessage)); return; }
                    Missions = response;
                    MissionsError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
                    MissionsLoading = false;
                    FailMissions(error);
                });
        }

        public void RequestActivities()
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyMissionAction) return;
            int epoch = _epoch;
            int request = ++_activityRequest;
            ActivitiesLoading = true;
            ActivitiesError = string.Empty;
            OnChanged?.Invoke();
            _net.Call(SceneActivityClientPlayerRpc.GetActivityListMessageId,
                new GetActivityListRequest(), GetActivityListResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _activityRequest) return;
                    ActivitiesLoading = false;
                    if (HasTip(response.ErrorMessage)) { FailActivities(DescribeTip("读取活动失败", response.ErrorMessage)); return; }
                    Activities = response;
                    ActivitiesError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _activityRequest) return;
                    ActivitiesLoading = false;
                    FailActivities(error);
                });
        }

        public void AcceptMission(uint scope, uint missionId) => PerformMissionAction(scope, missionId, false);

        public void ClaimMissionReward(uint scope, uint missionId) => PerformMissionAction(scope, missionId, true);

        private void PerformMissionAction(uint scope, uint missionId, bool claim)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyMissionAction) return;
            if (BusySort) { FailMissions("背包正在整理，请稍候"); return; }
            if (BusyEquip) { FailMissions("装备正在更换，请稍候"); return; }
            PlayerMissionInfo mission = null;
            if (Missions != null)
                foreach (var entry in Missions.Missions)
                    if (entry.Scope == scope && entry.MissionId == missionId) { mission = entry; break; }
            bool allowed = mission != null && (claim ? mission.CanClaim : mission.CanAccept);
            if (!claim && mission == null && scope == 0 && Activities != null)
                foreach (var activity in Activities.Activities)
                    if (activity.MissionId == missionId && activity.CanParticipate) { allowed = true; break; }
            if (missionId == 0 || !allowed)
            {
                FailMissions(!string.IsNullOrWhiteSpace(mission?.UnavailableReason) ? mission.UnavailableReason :
                    claim ? "当前任务暂不可领奖，请刷新查看" : "当前任务暂不可接取，请刷新查看");
                return;
            }

            int epoch = _epoch;
            int request = ++_missionRequest;
            ++_activityRequest;
            MissionsLoading = ActivitiesLoading = false;
            BusyMissionAction = true;
            MissionsError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
            _net.Call(claim ? SceneMissionClientPlayerRpc.ClaimMissionRewardMessageId :
                    SceneMissionClientPlayerRpc.AcceptMissionMessageId,
                new MissionActionRequest { Scope = scope, MissionId = missionId }, GetMissionListResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
                    BusyMissionAction = false;
                    if (HasTip(response.ErrorMessage))
                    { FailMissions(DescribeTip(claim ? "领取奖励失败" : "接取任务失败", response.ErrorMessage)); return; }
                    Missions = response;
                    MissionsError = string.Empty;
                    OnChanged?.Invoke();
                    if (!IsCurrent(epoch, playerId)) return;
                    RequestActivities();
                    if (claim && IsCurrent(epoch, playerId)) RequestBag(RequestedBagType);
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
                    BusyMissionAction = false;
                    FailMissions(error);
                });
        }
        private bool PrepareRequest(out ulong playerId)
        {
            playerId = _net.PlayerId;
            if (_disposed) return false;
            if (_snapshotPlayerId != 0 && _snapshotPlayerId != playerId) ClearSnapshots();
            if (!_net.IsReady || playerId == 0)
            {
                const string error = "尚未进入游戏，请进入角色后再试";
                BagError = _equipmentReadError = MissionsError = ActivitiesError = error;
                _equipActionError = string.Empty;
                OnChanged?.Invoke();
                OnError?.Invoke(error);
                return false;
            }
            _snapshotPlayerId = playerId;
            return true;
        }

        private bool IsCurrent(int epoch, ulong playerId)
            => !_disposed && _epoch == epoch && _net.IsReady && _net.PlayerId == playerId;

        private static bool ValidBag(BagInfo bag, uint bagType)
            => bag?.Layout != null && bag.Layout.BagType == bagType;

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeTip(string operation, TipInfoMessage tip)
            => $"{operation}（错误码 {tip.Id}）";

        // 发包前的本地拒绝与服务器的拒绝说的是同一件事,用同一句话:下面这几句各只写一处,
        // DescribeEquipTip 的对应码与本地拒绝都引用它(逐字镜像 Tip.xlsx,表改了改这里)。
        private const string ItemNotFoundText = "物品不存在";
        private const string EnhanceUnavailableText = "该装备无法强化";
        private const string EnhanceMaxLevelText = "已强化到最高等级";
        private const string GemNotOwnedText = "背包里没有这颗宝石";
        private const string GemSocketInvalidText = "该装备没有这个宝石孔";
        private const string GemSocketOccupiedText = "该孔位已镶有宝石,请先取下";
        private const string GemSocketEmptyText = "该孔位没有宝石";

        /// <summary>
        /// 装备域 tip 文案:逐字镜像服务端 data/tip/Tip.xlsx 的 //equip_error 组
        /// (docs/design/equipment-attributes.md §2.7、equipment-enhance-inlay.md §2.5),表改了要同步这里。
        /// 码只写生成枚举名、不抄数字(号由导表器发,下次导表可能变)。
        /// 未收录的码(别的域透传过来的,或表里新加的)退回本类既有的「操作（错误码 N）」口径。
        /// </summary>
        public static string DescribeEquipTip(string operation, TipInfoMessage tip)
        {
            if (!HasTip(tip)) return operation;
            string text = tip.Id switch
            {
                (uint)equip_error.KEquipItemNotFound => ItemNotFoundText,
                (uint)equip_error.KEquipNotEquipment => "该物品不是装备",
                (uint)equip_error.KEquipLevelNotEnough => "等级不足,无法装备",
                (uint)equip_error.KEquipClassMismatch => "职业不符,无法装备",
                (uint)equip_error.KEquipNoSlot => "没有可用的装备栏位",
                (uint)equip_error.KEquipNotEquipped => "该装备未穿戴",
                (uint)equip_error.KEquipBagFull => "背包已满,无法卸下",
                (uint)equip_error.KEquipInBattle => "战斗中无法更换装备",
                (uint)equip_error.KEquipFrozen => "当前状态无法更换装备",
                (uint)equip_error.KEquipGrantInvalid => "发放参数无效",
                (uint)equip_error.KEquipInternalError => "装备操作失败,请稍后再试",
                (uint)equip_error.KEquipEnhanceUnavailable => EnhanceUnavailableText,
                (uint)equip_error.KEquipEnhanceMaxLevel => EnhanceMaxLevelText,
                (uint)equip_error.KEquipEnhanceMaterialNotEnough => "强化材料不足",
                // 契约 §0-1 把 kCurrencyGold 叫「银两」(背包页、属性、宠物同口径)。Tip.xlsx 这一行若仍写「金币」,客户端先改叫法。
                (uint)equip_error.KEquipEnhanceGoldNotEnough => "银两不足,无法强化",
                (uint)equip_error.KEquipGemNotGem => "该物品不是宝石",
                (uint)equip_error.KEquipGemNotOwned => GemNotOwnedText,
                (uint)equip_error.KEquipGemSocketInvalid => GemSocketInvalidText,
                (uint)equip_error.KEquipGemSocketOccupied => GemSocketOccupiedText,
                (uint)equip_error.KEquipGemSocketEmpty => GemSocketEmptyText,
                (uint)equip_error.KEquipGemTypeMismatch => "宝石类型与孔位不符",
                (uint)equip_error.KEquipGemBagFull => "背包已满,无法取下宝石",
                _ => null,
            };
            return text == null ? DescribeTip(operation, tip) : $"{operation}:{text}";
        }

        /// <summary>
        /// 这个拒绝码是否只说明「规则不允许」:服务器没有动过状态,本地快照也没有被证明过期,不必重新拉取。
        /// gate 限流同理(包没有到 scene)。其余的码一律按「需要重新同步」处理:物品不存在 / 未穿戴说明本地
        /// 快照已经过期;内部故障与别的域透传过来的码不知道服务器停在哪一步 —— 服务端 player_bag_handler.cpp
        /// 的 ReplyInventoryAndEquipment 在「穿脱已生效但快照建不出来」时就是回一个码,并约定客户端重新 GetBag。
        /// 强化 / 镶嵌 / 取下的码同一个判据:宝石类型不符、背包放不下取下的宝石,数据层没有在本地拦,
        /// 服务器这么说不代表快照过期,不拉;满级 / 强化未开放 / 孔位不存在 / 已占用 / 为空 / 没有这颗宝石
        /// 是发包前已经按本地快照拦过的情形(「不是宝石」则是候选列表本该排除的),服务器还这么说就是快照过期,要拉。
        /// </summary>
        /// <param name="knownAtSend">
        /// 强化发包那一刻,本地快照按 EquipBagModel.CheckEnhance 得出的判断(其它操作不传)。
        /// 金币 / 材料不足的两个码:服务器的说法与它一致 = 本来就知道不够,不拉;不一致 = 本地以为够
        /// (或以为缺的是另一样),快照已过期,拉。CheckEnhance 的检查顺序与服务器相同(先材料后金币),所以可以逐码比对。
        /// 「无法强化」同理:本地快照本来就没有下一级预览(实例未初始化,或下一级是必败的行 —— 这两种情形服务器
        /// 不给预览、也照样拒)= 与本地一致,不拉(否则每点一次都白拉两份一模一样的快照);本地带着预览却被这样拒 = 快照过期,拉。
        /// </param>
        private static bool IsRuleRejection(uint tipId, EquipEnhanceCheck knownAtSend = EquipEnhanceCheck.Unknown) => tipId switch
        {
            (uint)equip_error.KEquipNotEquipment => true,
            (uint)equip_error.KEquipLevelNotEnough => true,
            (uint)equip_error.KEquipClassMismatch => true,
            (uint)equip_error.KEquipNoSlot => true,
            (uint)equip_error.KEquipBagFull => true,
            (uint)equip_error.KEquipInBattle => true,
            (uint)equip_error.KEquipFrozen => true,
            (uint)common_error.KRateLimitExceeded => true,
            (uint)equip_error.KEquipGemTypeMismatch => true,
            (uint)equip_error.KEquipGemBagFull => true,
            (uint)equip_error.KEquipEnhanceMaterialNotEnough => knownAtSend == EquipEnhanceCheck.MaterialNotEnough,
            (uint)equip_error.KEquipEnhanceGoldNotEnough => knownAtSend == EquipEnhanceCheck.GoldNotEnough,
            (uint)equip_error.KEquipEnhanceUnavailable => knownAtSend == EquipEnhanceCheck.Unavailable,
            _ => false,
        };

        /// <summary>
        /// 传输层 onError 串里是不是信封上的错误码(gate / 路由层写的,GameClient.Call 折算成 "server tip=N")。
        /// 其余的串("rpc timeout" / "disconnected" / "send failed: …" / "parse response: …")是传输层失败。
        /// </summary>
        private static bool TryParseEnvelopeTip(string error, out uint tipId)
        {
            tipId = 0;
            return error != null && error.StartsWith(EnvelopeTipPrefix, StringComparison.Ordinal)
                && uint.TryParse(error.Substring(EnvelopeTipPrefix.Length), NumberStyles.None,
                    CultureInfo.InvariantCulture, out tipId)
                && tipId != 0;
        }

        /// <summary>当前查看的那一类背包会不会被穿脱回包整体覆盖(人物背包 / 装备栏)。</summary>
        private bool ViewingEquipAffectedBag
            => RequestedBagType == CharacterBagType || RequestedBagType == EquipmentBagType;

        private void FailBag(string message)
        {
            BagError = string.IsNullOrWhiteSpace(message) ? "背包请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(BagError);
        }

        /// <summary>装备栏读取失败。</summary>
        private void FailEquipment(string message)
        {
            _equipmentReadError = string.IsNullOrWhiteSpace(message) ? "装备请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(_equipmentReadError);
        }

        private static long DefaultNowMs() => MonotonicClock.ElapsedMilliseconds;

        /// <summary>装备类操作(穿上 / 卸下 / 强化 / 镶嵌 / 取下)失败,含发包前的本地拒绝。</summary>
        private void FailEquipAction(string message)
        {
            _equipActionError = string.IsNullOrWhiteSpace(message) ? "装备请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(_equipActionError);
        }

        private void FailMissions(string message)
        {
            MissionsError = string.IsNullOrWhiteSpace(message) ? "任务请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(MissionsError);
        }

        private void FailActivities(string message)
        {
            ActivitiesError = string.IsNullOrWhiteSpace(message) ? "活动请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(ActivitiesError);
        }

        private void HandleDisconnected()
        {
            if (_disposed) return;
            ClearSnapshots();
        }

        private void ClearSnapshots()
        {
            ++_epoch;
            _snapshotPlayerId = 0;
            Array.Clear(_bags, 0, _bags.Length);
            Missions = null;
            Activities = null;
            BagLoading = EquipmentLoading = MissionsLoading = ActivitiesLoading = false;
            BusySort = BusyEquip = BusyMissionAction = false;
            _bagReadDeferred = _equipmentReadDeferred = false;
            BagError = _equipmentReadError = _equipActionError = MissionsError = ActivitiesError = string.Empty;
            LastEnhanceOutcome = null;
            RequestedBagType = 0;
            OnChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _net.Disconnected -= HandleDisconnected;
            ClearSnapshots();
            if (ReferenceEquals(Instance, this)) Instance = null;
            OnChanged = null;
            OnError = null;
            OnEnhanceResult = null;
        }
    }
}
