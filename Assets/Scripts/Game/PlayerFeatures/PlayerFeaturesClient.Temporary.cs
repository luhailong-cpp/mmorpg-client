using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>
    /// 临时格(bag_type = 3):快照与取回(服务端 docs/design/bag-rule-policy-layering.md §6.6)。
    /// 临时格只装「人物背包放不下的战斗掉落」,满了不再挤掉旧物;玩家用 RetrieveTemporaryItems 把东西取回人物背包,
    /// 成功的回包带人物背包与临时格两份全量,本地整体覆盖。
    /// <para>
    /// 读取走自己的序号,不改 <see cref="RequestedBagType"/>:背包页照旧看人物背包,临时格另开面板。
    /// 取回单飞(<see cref="BusyTemporary"/>),与整理、穿脱 / 强化 / 镶嵌 / 取下互斥:它们都整体覆盖人物背包,
    /// 整理还与读取共用 _bagRequest,同时在途会让一方的回包被当成过期丢掉。任务领奖、活跃奖励与取回同时在途不冲突
    /// (各自的回包只带或只读自己那份数据,人物背包以后到的全量为准),那两处不拦取回。
    /// </para>
    /// 取回不带幂等键,服务端按设计天然防重:按物品重发时这件已不在临时格(kBagTemporaryItemNotFound,两个包都没动),
    /// 全部取回的重发是空操作。所以结果不明(超时 / 1003)不自动重发,只重拉两份快照。
    /// </summary>
    public sealed partial class PlayerFeaturesClient
    {
        public const uint TemporaryBagType = 3;
        /// <summary>
        /// 两次取回发包的最小间隔(毫秒)。RetrieveTemporaryItems 没有单独进限流表,吃默认档 3
        /// (相邻两整秒合计 3 个,持续连点要 ≥ 667 毫秒),推导同 <see cref="ForgeMinIntervalMs"/>。
        /// </summary>
        public const long TemporaryRetrieveMinIntervalMs = 700;
        /// <summary>取回在途时别的背包写操作给的提示(整理、穿脱、强化等被挡下时)。</summary>
        public const string TemporaryBusyText = "正在从临时格取回，请稍候";

        // 下面三句以服务端 data/tip/Tip.xlsx 的 bag_error 组为底(表改了改这里),后半句是给玩家的下一步。
        private const string TemporaryItemNotFoundText = "临时格里没有这件物品,请刷新后再试";
        private const string TemporaryBagFullText = "背包空间不足,无法从临时格取回";
        private const string TemporaryRetrieveBusyText = "交接或传送途中,暂时不能从临时格取回";
        private const string TemporaryOperation = "取回失败";

        private int _temporaryRequest;
        private int _temporaryRetrieveRequest;
        // 取回在途期间被挡下的临时格刷新,取回收口后补发。
        private bool _temporaryReadDeferred;
        // 上一次取回的发包时刻(_nowMs 的读数);null = 还没发过。只管发包节奏,断线 / 换角色不清。
        private long? _lastTemporaryRetrieveMs;
        private string _temporaryReadError = string.Empty;
        private string _temporaryActionError = string.Empty;

        /// <summary>临时格快照;还没拉到、断线或换角色之后为 null。占用看 Layout.Slots 条数 / Layout.Capacity。</summary>
        public BagInfo Temporary => _bags[TemporaryBagType];
        public bool HasTemporary => Temporary != null;
        public bool TemporaryLoading { get; private set; }
        /// <summary>取回在途(界面据此置灰「取回」与「全部取回」)。</summary>
        public bool BusyTemporary { get; private set; }
        /// <summary>临时格面板状态行显示的错误:上一次取回的失败原因优先,其次是临时格读取失败。</summary>
        public string TemporaryError => _temporaryActionError.Length != 0 ? _temporaryActionError : _temporaryReadError;

        /// <summary>拉取临时格(= GetBag(bag_type = 3))。不改 RequestedBagType,不动人物背包与装备栏的快照与在途请求。</summary>
        public void RequestTemporary()
        {
            if (!PrepareRequest(out var playerId)) return;
            // 取回的回包自带临时格全量:不发、也不报错,记下这一次刷新,取回收口后补发。
            if (BusyTemporary) { _temporaryReadDeferred = true; return; }
            _temporaryActionError = string.Empty; // 玩家主动刷新:上一次取回的失败原因一并收起
            ReadTemporary(playerId);
        }

        /// <summary>取回临时格里的一件(一堆)。人物背包放不下由服务器整批拒绝,两个包都不动。</summary>
        public void RetrieveTemporaryItem(ulong itemId) => RetrieveTemporary(itemId, false);

        /// <summary>取回临时格里的全部。全或无:人物背包差一格就整批拒绝,玩家可以改成逐件取回。</summary>
        public void RetrieveAllTemporaryItems() => RetrieveTemporary(0, true);

        private void ReadTemporary(ulong playerId)
        {
            int epoch = _epoch;
            int request = ++_temporaryRequest;
            TemporaryLoading = true;
            _temporaryReadError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || request != _temporaryRequest) return;
            _net.Call(SceneBagClientPlayerRpc.GetBagMessageId,
                new GetBagRequest { BagType = TemporaryBagType }, GetBagResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _temporaryRequest) return;
                    TemporaryLoading = false;
                    if (HasTip(response.ErrorMessage)) { FailTemporaryRead(DescribeTip("读取临时格失败", response.ErrorMessage)); return; }
                    if (!ValidBag(response.Bag, TemporaryBagType)) { FailTemporaryRead("服务器未返回有效的临时格数据"); return; }
                    _bags[TemporaryBagType] = response.Bag;
                    _temporaryReadError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _temporaryRequest) return;
                    TemporaryLoading = false;
                    // 这条原因会显示在面板状态行:传输层的内部英文串不上屏。
                    FailTemporaryRead(TryParseEnvelopeTip(error, out uint tipId)
                        ? DescribeTip("读取临时格失败", new TipInfoMessage { Id = tipId })
                        : $"读取临时格失败:{NetworkErrorText}");
                });
        }

        private void RetrieveTemporary(ulong itemId, bool takeAll)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyTemporary) return; // 连点:上一次还没回包,不发第二个包也不报错
            if (BusyEquip) { FailTemporaryAction("装备正在更换，请稍候"); return; }
            if (BusySort) { FailTemporaryAction("背包正在整理，请稍候"); return; }
            if (!takeAll && itemId == 0) { FailTemporaryAction("请先选择要取回的物品"); return; }
            long now = _nowMs();
            // 差值为负 = 时钟被换过:放行(宁可多发一包,不能把人永久锁在间隔里)。
            if (_lastTemporaryRetrieveMs.HasValue && now >= _lastTemporaryRetrieveMs.Value
                && now - _lastTemporaryRetrieveMs.Value < TemporaryRetrieveMinIntervalMs)
            { FailTemporaryAction("操作太快，请稍候再试"); return; }

            int epoch = _epoch;
            int request = ++_temporaryRetrieveRequest;
            BusyTemporary = true;
            _temporaryActionError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || request != _temporaryRetrieveRequest) return;
            bool Current() => IsCurrent(epoch, playerId) && request == _temporaryRetrieveRequest && BusyTemporary;
            _lastTemporaryRetrieveMs = now;
            // item_id 与 take_all 二选一:两者都给或都不给服务器回 kInvalidParameter。
            _net.Call(SceneBagClientPlayerRpc.RetrieveTemporaryItemsMessageId,
                new RetrieveTemporaryItemsRequest { ItemId = takeAll ? 0 : itemId, TakeAll = takeAll },
                RetrieveTemporaryItemsResponse.Parser,
                response =>
                {
                    if (!Current()) return;
                    BusyTemporary = false;
                    if (HasTip(response.ErrorMessage))
                    {
                        RejectTemporary(response.ErrorMessage.Id, takeAll, epoch, playerId);
                        return;
                    }
                    // 没有 tip 却缺快照:取回已经生效,只是没带回来,本地两份都过期了。
                    if (!ValidBag(response.Bag, CharacterBagType) || !ValidBag(response.Temporary, TemporaryBagType))
                    {
                        FailTemporaryAndResync("服务器未返回最新的背包与临时格", epoch, playerId);
                        return;
                    }
                    // 整体覆盖人物背包与临时格:这两类此前发出、还没回来的读取到这里作废,免得更旧的读回包盖掉写回包。
                    if (RequestedBagType == CharacterBagType || RequestedBagType == TemporaryBagType)
                    { ++_bagRequest; BagLoading = false; BagError = string.Empty; }
                    ++_temporaryRequest;
                    TemporaryLoading = false;
                    _bags[CharacterBagType] = response.Bag;
                    _bags[TemporaryBagType] = response.Temporary;
                    _temporaryActionError = _temporaryReadError = string.Empty;
                    OnChanged?.Invoke();
                    SendTemporaryDeferredReads(epoch, playerId);
                },
                error =>
                {
                    if (!Current()) return;
                    BusyTemporary = false;
                    // 信封上的码与响应体里的码同一套处理;其余是传输层失败(超时 / 断线 / 解析失败):
                    // 服务器可能已经搬了,不自动重发,只重拉两份快照(§6.6 (5))。
                    if (TryParseEnvelopeTip(error, out uint tipId)) RejectTemporary(tipId, takeAll, epoch, playerId);
                    else FailTemporaryAndResync($"{TemporaryOperation}:{NetworkErrorText}", epoch, playerId);
                });
        }

        /// <summary>
        /// 取回被拒的文案与「要不要重拉」。物品不在临时格(含上一次其实已取回的重发)说明本地快照过期;1003 与别的码
        /// 不知道服务器停在哪一步:两份都重拉。背包放不下、交接中、限流是状态没变的拒绝:不拉,免得把连点放大成限流。
        /// 战斗中服务器放行取回(整笔延后的那一局正要靠取回腾位),客户端不在本地拦。
        /// </summary>
        private void RejectTemporary(uint tipId, bool takeAll, int epoch, ulong playerId)
        {
            switch (tipId)
            {
                case (uint)bag_error.KBagTemporaryItemNotFound:
                    FailTemporaryAndResync(TemporaryItemNotFoundText, epoch, playerId);
                    return;
                case (uint)bag_error.KBagTemporaryRetrieveBagFull:
                    FailTemporaryAction(TemporaryBagFullText + (takeAll
                        ? ";请先腾出人物背包空间,或改为逐件取回" : ";请先腾出人物背包空间"));
                    return;
                case (uint)bag_error.KBagTemporaryRetrieveBusy:
                    FailTemporaryAction(TemporaryRetrieveBusyText + ",请稍后再试");
                    return;
                case (uint)common_error.KRateLimitExceeded:
                    FailTemporaryAction("操作太快，请稍候再试");
                    return;
                default:
                    FailTemporaryAndResync(DescribeTip(TemporaryOperation, new TipInfoMessage { Id = tipId }), epoch, playerId);
                    return;
            }
        }

        /// <summary>取回失败且状态不明:原因留着,人物背包(正在看它时)与临时格各重拉一次。</summary>
        private void FailTemporaryAndResync(string message, int epoch, ulong playerId)
        {
            _temporaryReadDeferred = true;
            if (RequestedBagType == CharacterBagType) _bagReadDeferred = true;
            FailTemporaryAction(message);
            SendTemporaryDeferredReads(epoch, playerId);
        }

        /// <summary>
        /// 取回收口后补发攒下的读取。每发一个之前都重新确认:前面的通知里可能又点了一次取回(留给它的收口),
        /// 或已经换了角色(标记已清)。人物背包的补读与穿脱共用 _bagReadDeferred,两者互斥,不会同时在途。
        /// </summary>
        private void SendTemporaryDeferredReads(int epoch, ulong playerId)
        {
            if (_bagReadDeferred && !BusyEquip && !BusyTemporary && IsCurrent(epoch, playerId))
            { _bagReadDeferred = false; RequestBag(RequestedBagType); }
            if (_temporaryReadDeferred && !BusyTemporary && IsCurrent(epoch, playerId))
            { _temporaryReadDeferred = false; ReadTemporary(playerId); }
        }

        private void FailTemporaryRead(string message)
        {
            _temporaryReadError = string.IsNullOrWhiteSpace(message) ? "临时格请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(_temporaryReadError);
        }

        /// <summary>取回失败,含发包前的本地拒绝。</summary>
        private void FailTemporaryAction(string message)
        {
            _temporaryActionError = string.IsNullOrWhiteSpace(message) ? "取回失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(_temporaryActionError);
        }

        /// <summary>断线 / 换角色:由 ClearSnapshots 调(_epoch 已换,在途回调都会被丢弃)。</summary>
        private void ResetTemporarySession()
        {
            BusyTemporary = TemporaryLoading = false;
            _temporaryReadDeferred = false;
            _temporaryReadError = _temporaryActionError = string.Empty;
        }
    }
}
