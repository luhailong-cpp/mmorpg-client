using System;
using System.Globalization;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>
    /// Player-owned bag, mission and activity snapshots over the existing gate transport.
    /// Lists are server authoritative. A request is bound to its player and connection
    /// epoch so late callbacks cannot repopulate another character's windows.
    /// 背包快照按 bag_type 各存各的(0 人物背包 / 1 仓库 / 2 装备栏 / 3 临时格),互不清空;
    /// 穿上 / 卸下的回包带人物背包与装备栏两份全量,本地整体覆盖,不做增量推算
    /// (服务端 docs/design/equipment-attributes.md §3.2 / §6)。
    /// 穿脱失败不作废在途的读取;失败后服务器状态未知(超时、回包缺快照等)时自动重拉这两份快照。
    /// </summary>
    public sealed class PlayerFeaturesClient : IDisposable
    {
        public static PlayerFeaturesClient Instance { get; private set; }

        /// <summary>bag_type 取值,见服务端 proto/scene/player_bag.proto 的 BagLayoutInfo.bag_type。</summary>
        public const uint CharacterBagType = 0;
        public const uint EquipmentBagType = 2;
        private const uint MaxBagType = 3;
        // 与 GameClient.Call 把信封错误码折算成 onError 串的前缀逐字一致;两边改动必须同步。
        private const string EnvelopeTipPrefix = "server tip=";
        private const string NetworkErrorText = "网络异常,请稍后重试";

        private readonly IBattleTransport _net;
        private readonly BagInfo[] _bags = new BagInfo[MaxBagType + 1];
        private ulong _snapshotPlayerId;
        private int _epoch;
        private int _bagRequest;
        private int _equipmentRequest;
        // 穿上 / 卸下自己的序号:只用来认出上一次穿脱的迟到回调,不作废任何读取。
        private int _equipActionRequest;
        private int _missionRequest;
        private int _activityRequest;
        // 穿脱在途期间被挡下的刷新(人物背包 / 装备栏),穿脱收口后补发。
        private bool _bagReadDeferred;
        private bool _equipmentReadDeferred;
        // 装备栏读取失败与穿脱失败分开存:穿脱失败后可能自动重拉两份快照,重拉成功不能把失败原因冲掉。
        private string _equipmentReadError = string.Empty;
        private string _equipActionError = string.Empty;
        private bool _disposed;

        /// <summary>当前查看的那一类背包(<see cref="RequestedBagType"/>)的快照;切到另一类且还没拉到过时为 null。</summary>
        public BagInfo Bag => _bags[RequestedBagType];
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
        /// <summary>穿上 / 卸下在途(UI 据此禁用按钮);同一时刻只允许一个。</summary>
        public bool BusyEquip { get; private set; }
        public bool BusyMissionAction { get; private set; }
        public uint RequestedBagType { get; private set; }
        public string BagError { get; private set; } = string.Empty;
        /// <summary>背包页状态行显示的装备侧错误:上一次穿上 / 卸下的失败原因优先,其次是装备栏读取失败。</summary>
        public string EquipmentError => _equipActionError.Length != 0 ? _equipActionError : _equipmentReadError;
        public string MissionsError { get; private set; } = string.Empty;
        public string ActivitiesError { get; private set; } = string.Empty;

        public event Action OnChanged;
        public event Action<string> OnError;

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
            _net.Call(SceneBagClientPlayerGetBagHandler.MessageId,
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
            _net.Call(SceneBagClientPlayerGetBagHandler.MessageId,
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
        public void EquipItem(ulong itemId) => PerformEquipAction(itemId, true);

        /// <summary>卸下装备栏里的一件装备,回人物背包(背包满由服务器拒绝)。</summary>
        public void UnequipItem(ulong itemId) => PerformEquipAction(itemId, false);

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

        private void PerformEquipAction(ulong itemId, bool equip)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyEquip) return; // 连点:上一次还没回包,不发第二个包也不报错
            if (BusyMissionAction) { FailEquipAction("任务正在处理中，请稍候"); return; }
            if (BusySort) { FailEquipAction("背包正在整理，请稍候"); return; }
            if (itemId == 0) { FailEquipAction(equip ? "请先选择要装备的物品" : "请先选择要卸下的装备"); return; }

            int epoch = _epoch;
            // 在途的读取此刻不作废:穿脱被拒时服务器状态没变,它们照常落地;成功时才作废(见 Complete)。
            int request = ++_equipActionRequest;
            BusyEquip = true;
            _equipActionError = _equipmentReadError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || request != _equipActionRequest) return;

            string operation = equip ? "装备失败" : "卸下失败";
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
            void Complete(TipInfoMessage tip, BagInfo bag, BagInfo equipment)
            {
                if (!IsCurrent(epoch, playerId) || request != _equipActionRequest) return;
                // 业务拒绝码在响应体里(信封只承载 gate 级错误):有 tip 就保留旧快照。
                if (HasTip(tip)) { Reject(DescribeEquipTip(operation, tip), !IsRuleRejection(tip.Id)); return; }
                // 没有 tip 却缺快照:穿脱已经生效,只是没带回来,本地两份都过期了。
                if (!ValidBag(bag, CharacterBagType) || !ValidBag(equipment, EquipmentBagType))
                { Reject("服务器未返回更换后的背包与装备栏", true); return; }
                BusyEquip = false;
                // 回包整体覆盖人物背包与装备栏:这两类此前发出、还没回来的读取到这里才作废,免得更旧的
                // 读回包盖掉写回包。正在看仓库 / 临时格时,那一类的在途读取与穿脱无关,不动它。
                ++_equipmentRequest;
                EquipmentLoading = false;
                if (ViewingEquipAffectedBag) { ++_bagRequest; BagLoading = false; BagError = string.Empty; }
                _bags[CharacterBagType] = bag;
                _bags[EquipmentBagType] = equipment;
                _equipActionError = _equipmentReadError = string.Empty;
                OnChanged?.Invoke();
                SendDeferredReads(epoch, playerId);
            }
            void Fail(string error)
            {
                if (!IsCurrent(epoch, playerId) || request != _equipActionRequest) return;
                // 信封上的码(gate / 路由层)与响应体里的码走同一套文案、同一个判定;其余是传输层失败
                // (超时 / 断线 / 解析失败):内部英文串不上屏,而且服务器可能已经执行,要重新同步。
                if (TryParseEnvelopeTip(error, out uint tipId))
                    Reject(DescribeEquipTip(operation, new TipInfoMessage { Id = tipId }), !IsRuleRejection(tipId));
                else Reject($"{operation}:{NetworkErrorText}", true);
            }
            if (equip)
                _net.Call(MessageIds.EquipItem, new EquipItemRequest { ItemId = itemId }, EquipItemResponse.Parser,
                    response => Complete(response.ErrorMessage, response.Bag, response.Equipment), Fail);
            else
                _net.Call(MessageIds.UnequipItem, new UnequipItemRequest { ItemId = itemId }, UnequipItemResponse.Parser,
                    response => Complete(response.ErrorMessage, response.Bag, response.Equipment), Fail);
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
            _net.Call(SceneBagClientPlayerSortBagHandler.MessageId,
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
            _net.Call(SceneMissionClientPlayerGetMissionListHandler.MessageId,
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
            _net.Call(SceneActivityClientPlayerGetActivityListHandler.MessageId,
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
            _net.Call(claim ? SceneMissionClientPlayerClaimMissionRewardHandler.MessageId :
                    SceneMissionClientPlayerAcceptMissionHandler.MessageId,
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

        /// <summary>
        /// 装备域 tip 文案:逐字镜像服务端 data/tip/Tip.xlsx 的 //equip_error 组
        /// (docs/design/equipment-attributes.md §2.7),表改了要同步这里。
        /// 码只写生成枚举名、不抄数字(号由导表器发,下次导表可能变)。
        /// 未收录的码(别的域透传过来的,或表里新加的)退回本类既有的「操作（错误码 N）」口径。
        /// </summary>
        public static string DescribeEquipTip(string operation, TipInfoMessage tip)
        {
            if (!HasTip(tip)) return operation;
            string text = tip.Id switch
            {
                (uint)equip_error.KEquipItemNotFound => "物品不存在",
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
                _ => null,
            };
            return text == null ? DescribeTip(operation, tip) : $"{operation}:{text}";
        }

        /// <summary>
        /// 这个拒绝码是否只说明「规则不允许」:服务器没有动过状态,本地快照也没有被证明过期,不必重新拉取。
        /// gate 限流同理(包没有到 scene)。其余的码一律按「需要重新同步」处理:物品不存在 / 未穿戴说明本地
        /// 快照已经过期;内部故障与别的域透传过来的码不知道服务器停在哪一步 —— 服务端 player_bag_handler.cpp
        /// 的 ReplyInventoryAndEquipment 在「穿脱已生效但快照建不出来」时就是回一个码,并约定客户端重新 GetBag。
        /// </summary>
        private static bool IsRuleRejection(uint tipId) => tipId switch
        {
            (uint)equip_error.KEquipNotEquipment => true,
            (uint)equip_error.KEquipLevelNotEnough => true,
            (uint)equip_error.KEquipClassMismatch => true,
            (uint)equip_error.KEquipNoSlot => true,
            (uint)equip_error.KEquipBagFull => true,
            (uint)equip_error.KEquipInBattle => true,
            (uint)equip_error.KEquipFrozen => true,
            (uint)common_error.KRateLimitExceeded => true,
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

        /// <summary>穿上 / 卸下失败(含发包前的本地拒绝)。</summary>
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
        }
    }
}
