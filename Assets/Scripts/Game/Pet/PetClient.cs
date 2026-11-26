using System;
using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Pet
{
    /// <summary>
    /// 宝宝(宠物)网络层(服务端设计:docs/design/player-pet.md)。
    ///
    /// 契约要点(与 <see cref="Attribute.AttributeClient"/> 逐条同构,便于两边一起读):
    ///  - 网络依赖收敛在 <see cref="IBattleTransport"/>(纯测试缝,不另起网络栈),
    ///    本类不引用 UnityEngine,可在 EditMode 里用假实现穷打;
    ///  - **服务器是唯一真相**:每个写操作成功后服务器回全量列表,本地整体覆盖
    ///    <see cref="Pets"/>,绝不做本地增量推算(点数 / 上限 / 二级属性 / 资质全在服务器算);
    ///  - 列表未到达前 UI 不允许提交(<see cref="HasList"/> 为 false);
    ///  - 一次只允许一个在途写请求(<see cref="Busy"/>),防连点把同一批点数提交两次
    ///    —— 服务器侧 Allocate 是"目标值"幂等语义,重复提交也不会多扣点,这里只是省掉无谓往返;
    ///  - 断线:清空列表与在途状态,重连后由 UI 重新 RequestList。
    /// </summary>
    public sealed partial class PetClient
    {
        /// <summary>单例挂接(实例由 GameClient 持有,静态 Instance 供 UI 路解析;测试直接 new)。</summary>
        public static PetClient Instance { get; private set; }

        private readonly IBattleTransport _net;
        private readonly Func<bool> _cultivationWritesBlocked;
        public const string CultivationLockedMessage = "战斗中只读查看，无法修改宝宝培养。";
        public bool CultivationWritesBlocked => _cultivationWritesBlocked?.Invoke() == true;

        // ── 契约属性 ────────────────────────────────────────

        /// <summary>最新权威列表(null = 尚未拉取 / 已作废)。</summary>
        public PetListInfo Pets { get; private set; }

        public bool HasList => Pets != null;
        public bool ListRequestPending { get; private set; }
        private int _sessionVersion;
        private int _listRequestVersion;
        private int _grantRequestVersion;
        private int _allocationRequestVersion;
        private AscendPetTierRequest _pendingAscend;
        private ulong _ascendPlayerId;
        private ulong _ascendRequestCounter;
        public bool HasPendingTierAscension(ulong petId) => _pendingAscend?.PetId == petId && _ascendPlayerId == MyPlayerId;
        public bool IsReady => _net.IsReady;

        /// <summary>有写请求在途(UI 据此禁用提交按钮)。</summary>
        public bool Busy { get; private set; }

        public ulong MyPlayerId => _net.PlayerId;

        // ── 契约事件 ────────────────────────────────────────

        /// <summary>列表更新(拉取 / 写成功 / 服务器主动推送);参数即 <see cref="Pets"/>。</summary>
        public event Action<PetListInfo> OnList;

        /// <summary>自动加点建议(pet_id, dimension_id → 目标已分配点);只是预览,需玩家确认后再提交。</summary>
        public event Action<ulong, IReadOnlyDictionary<uint, uint>> OnAutoSuggestion;

        /// <summary>在途状态变化(供 UI 刷新按钮可用性)。</summary>
        public event Action<bool> OnBusyChanged;

        public event Action<string> OnError;

        // ── 构造 / 挂接 ─────────────────────────────────────

        public PetClient(IBattleTransport transport, Func<bool> cultivationWritesBlocked = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _cultivationWritesBlocked = cultivationWritesBlocked;
            _net.RegisterNotify(MessageIds.NotifyPetListChanged, HandleListChanged);
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>生产入口:创建实例并登记为单例(GameClient 构造时调用)。</summary>
        public static PetClient Attach(IBattleTransport transport, Func<bool> cultivationWritesBlocked = null)
        {
            var client = new PetClient(transport, cultivationWritesBlocked);
            Instance = client;
            return client;
        }

        // ── tip ─────────────────────────────────────────────

        /// <summary>
        /// 宝宝 RPC 的拒绝码放在**响应体** error_message 里(scene handler 写全局 tip → TRANSFER_ERROR_MESSAGE),
        /// 信封 MessageContent.error_message 只承载 gate 级错误。GameClient.Call 只折算信封,
        /// 所以每个响应回调必须先看响应体 tip,否则所有服务端拒绝都会被当成"成功但没列表"。
        /// </summary>
        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        /// <summary>
        /// tip 文案:镜像 data/tip/Tip.xlsx 的 //pet_error 组(base=26000,顺序即表里行序)。
        /// 表改了要同步这里;未收录的编号退回裸编号(全仓既有口径 "tip=N")。
        /// </summary>
        public static string DescribeTip(string what, TipInfoMessage tip)
        {
            if (!HasTip(tip)) return what;
            string text = tip.Id switch
            {
                26000 => "宝宝不存在",
                26001 => "宝宝数据已失效,请联系客服",
                26002 => "可携带的宝宝数量已达上限",
                26003 => "主人等级不足,还不能携带这只宝宝",
                26004 => "这只宝宝已经在出战中",
                26005 => "当前没有出战的宝宝",
                26006 => "操作太频繁,请稍后再试",
                26007 => "宝宝名称不合法",
                26008 => "战斗中不能操作宝宝",
                26009 => "宝宝剩余点数不足",
                26010 => "已分配的点数不能减少,请使用洗点",
                26011 => "超过单项可分配上限",
                26012 => "宝宝属性维度不存在",
                // 契约 §0-1 把 kCurrencyGold 叫"银两"。Tip.xlsx 这一行仍写"金币不足",客户端先改叫法,
                // 表的文案随下一个改 Tip.xlsx 的批次同步(服务端 docs/design/guild-phase2/92-handoff.md §13)。
                26013 => "银两不足",
                26014 => "没有可用的自动加点方案",
                26015 => "宝宝属性没有变化",
                26016 => "宝宝创建失败,请稍后再试",
                (uint)pet_error.KPetNameSensitive => "宝宝名称包含不允许使用的词语，请修改",
                (uint)pet_error.KPetNotTransferable => "该宝宝绑定角色，不可交易、赠送、寄售或分解合成",
                (uint)pet_error.KPetTierMaxReached => "该宝宝已达最高灵阶（圣兽）",
                (uint)pet_error.KPetTierChanged => "宝宝灵阶状态已变化，请刷新后再试",
                (uint)pet_error.KPetTierLevelNotEnough => "宝宝等级不足，暂不能进行灵阶突破",
                (uint)pet_error.KPetTierMaterialNotEnough => "灵阶突破所需材料不足",
                (uint)pet_error.KPetEntryPresetInvalid => "无效的宝宝条目方案",
                _ => Attribute.AttributeClient.DescribeAssetTip(tip.Id) ?? $"tip={tip.Id}",
            };
            return $"{what}:{text}";
        }

        // ── 读 ──────────────────────────────────────────────

        /// <summary>拉取列表(打开界面 / 重连后调用)。</summary>
        public void RequestList()
        {
            if (ListRequestPending) return;
            if (!_net.IsReady)
            {
                OnError?.Invoke("尚未进入游戏,无法查看宝宝");
                return;
            }
            int requestVersion = ++_listRequestVersion;
            ListRequestPending = true;
            _net.Call(MessageIds.GetPetList, new GetPetListRequest(), GetPetListResponse.Parser,
                resp =>
                {
                    if (requestVersion != _listRequestVersion) return;
                    ListRequestPending = false;
                    if (HasTip(resp.ErrorMessage)) { OnError?.Invoke(DescribeTip("拉取宝宝列表失败", resp.ErrorMessage)); return; }
                    ApplyList(resp.Pets);
                },
                err =>
                {
                    if (requestVersion != _listRequestVersion) return;
                    ListRequestPending = false;
                    OnError?.Invoke(err);
                });
        }

        // ── 写 ──────────────────────────────────────────────

        /// <summary>出战(同时只能有一只)。</summary>
        public void Summon(ulong petId)
        {
            if (!BeginWrite(petId == 0 ? "没有选中宝宝" : null)) return;
            int session = _sessionVersion;
            _net.Call(MessageIds.SummonPet, new SummonPetRequest { PetId = petId }, SummonPetResponse.Parser,
                resp => { if (session == _sessionVersion) CompleteWrite("出战失败", resp.ErrorMessage, resp.Pets); },
                err => { if (session == _sessionVersion) FailWrite(err); });
        }

        /// <summary>收回出战宝宝(战斗中服务端会拒绝)。</summary>
        public void Recall()
        {
            if (!BeginWrite(null)) return;
            int session = _sessionVersion;
            _net.Call(MessageIds.RecallPet, new RecallPetRequest(), RecallPetResponse.Parser,
                resp => { if (session == _sessionVersion) CompleteWrite("收回失败", resp.ErrorMessage, resp.Pets); },
                err => { if (session == _sessionVersion) FailWrite(err); });
        }

        /// <summary>
        /// 确认加点:提交某只宝宝「目标已分配值」(全量、幂等)。target 只需包含改动的维度,
        /// 缺省维度服务器视为不变;服务器只接受"只增不减",减点走 <see cref="ResetPoints"/>。
        /// </summary>
        public void Allocate(ulong petId, IReadOnlyDictionary<uint, uint> target)
            => Allocate(petId, target, null);

        /// <summary>本次写请求的完成结果；列表推送和并发查询不会调用此回调。</summary>
        public void Allocate(ulong petId, IReadOnlyDictionary<uint, uint> target, Action<bool, PetListInfo> completed)
        {
            if (!BeginWrite(target != null && target.Count > 0 ? null : "没有需要提交的加点"))
            { completed?.Invoke(false, null); return; }
            var req = new AllocatePetPointsRequest { PetId = petId };
            foreach (var kv in target)
            {
                req.Allocated[kv.Key] = kv.Value;
            }
            int version = ++_allocationRequestVersion;
            _net.Call(MessageIds.AllocatePetPoints, req, AllocatePetPointsResponse.Parser,
                resp =>
                {
                    if (version != _allocationRequestVersion) return;
                    ++_allocationRequestVersion;
                    bool success = !HasTip(resp.ErrorMessage) && resp.Pets != null;
                    CompleteWrite("宝宝加点失败", resp.ErrorMessage, resp.Pets);
                    completed?.Invoke(success, success ? resp.Pets : null);
                },
                err =>
                {
                    if (version != _allocationRequestVersion) return;
                    ++_allocationRequestVersion;
                    FailWrite(err);
                    completed?.Invoke(false, null);
                });
        }

        /// <summary>洗点:清空该宝宝的全部分配,按表扣银两。</summary>
        public void ResetPoints(ulong petId)
        {
            if (!BeginWrite(petId == 0 ? "没有选中宝宝" : null)) return;
            int session = _sessionVersion;
            _net.Call(MessageIds.ResetPetPoints, new ResetPetPointsRequest { PetId = petId },
                ResetPetPointsResponse.Parser,
                resp => { if (session == _sessionVersion) CompleteWrite("宝宝洗点失败", resp.ErrorMessage, resp.Pets); },
                err => { if (session == _sessionVersion) FailWrite(err); });
        }

        /// <summary>
        /// 自动加点:服务器按通用方案算建议分配,**只算不落**。结果经
        /// <see cref="OnAutoSuggestion"/> 回来填进面板预览,玩家点「确认」才走 <see cref="Allocate"/>。
        /// 因为不改服务器状态,不占 <see cref="Busy"/>。
        /// </summary>
        public void AutoAllocate(ulong petId)
        {
            if (CultivationWritesBlocked) { OnError?.Invoke(CultivationLockedMessage); return; }
            if (!_net.IsReady)
            {
                OnError?.Invoke("尚未进入游戏,无法自动加点");
                return;
            }
            int session = _sessionVersion;
            _net.Call(MessageIds.AutoAllocatePetPoints, new AutoAllocatePetPointsRequest { PetId = petId },
                AutoAllocatePetPointsResponse.Parser,
                resp =>
                {
                    if (session != _sessionVersion) return;
                    if (HasTip(resp.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeTip("自动加点失败", resp.ErrorMessage));
                        return;
                    }
                    var suggestion = new Dictionary<uint, uint>();
                    foreach (var kv in resp.Suggested)
                    {
                        suggestion[kv.Key] = kv.Value;
                    }
                    OnAutoSuggestion?.Invoke(resp.PetId, suggestion);
                },
                err => { if (session == _sessionVersion) OnError?.Invoke(err); });
        }

        /// <summary>改名(按表扣银两)。</summary>
        public void Rename(ulong petId, string name)
        {
            if (!BeginWrite(string.IsNullOrWhiteSpace(name) ? "宝宝名不能为空" : null)) return;
            int session = _sessionVersion;
            _net.Call(MessageIds.RenamePet, new RenamePetRequest { PetId = petId, Name = name },
                RenamePetResponse.Parser,
                resp => { if (session == _sessionVersion) CompleteWrite("宝宝改名失败", resp.ErrorMessage, resp.Pets); },
                err => { if (session == _sessionVersion) FailWrite(err); });
        }

        /// <summary>宝宝灵阶突破（普通→变异→灵兽→神兽→圣兽）；超时保留原幂等键重试，避免重复扣材扣银。</summary>
        public void AscendTier(ulong petId, uint expectedTier, Action<bool, uint, PetListInfo, string> completed)
        {
            if (_pendingAscend != null && _ascendPlayerId != MyPlayerId) _pendingAscend = null;
            if (_pendingAscend != null && _pendingAscend.PetId != petId)
            {
                completed?.Invoke(false, 0, null, "请先确认另一只宝宝的灵阶突破结果。");
                return;
            }
            if (_pendingAscend == null && (petId == 0 || expectedTier == 0 || expectedTier >= 5))
            {
                completed?.Invoke(false, 0, null, expectedTier >= 5 ? "该宝宝已达最高灵阶（圣兽）。" : "未选中可突破的宝宝。");
                return;
            }
            if (!BeginWrite(null))
            {
                completed?.Invoke(false, 0, null, CultivationWritesBlocked ? CultivationLockedMessage : "当前无法进行灵阶突破。");
                return;
            }
            if (_pendingAscend == null)
            {
                ulong reqId = ((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 12) ^ (++_ascendRequestCounter & 0xFFFu);
                if (reqId == 0) reqId = 1;
                _pendingAscend = new AscendPetTierRequest
                {
                    PetId = petId,
                    ExpectedTier = expectedTier,
                    ClientRequestId = reqId,
                };
                _ascendPlayerId = MyPlayerId;
            }
            var sent = _pendingAscend.Clone();
            int session = _sessionVersion;
            ulong owner = MyPlayerId;
            _net.Call(MessageIds.AscendPetTier, sent, AscendPetTierResponse.Parser,
                resp =>
                {
                    if (session != _sessionVersion || owner != MyPlayerId) return;
                    bool rejected = HasTip(resp.ErrorMessage);
                    bool exact = !rejected && resp.ClientRequestId == sent.ClientRequestId && resp.Pets != null && resp.NewTier > sent.ExpectedTier;
                    if (rejected || exact) _pendingAscend = null;
                    SetBusy(false);
                    if (resp.Pets != null) ApplyList(resp.Pets);
                    string error = exact ? null : rejected
                        ? DescribeTip("灵阶突破失败", resp.ErrorMessage)
                        : "灵阶突破结果尚未确认，请重试确认。";
                    completed?.Invoke(exact, resp.NewTier, resp.Pets, error);
                },
                err =>
                {
                    if (session != _sessionVersion || owner != MyPlayerId) return;
                    SetBusy(false);
                    completed?.Invoke(false, 0, null, "灵阶突破结果未知，可重试确认。" + err);
                });
        }

        /// <summary>GM:发一只宝宝(开发调试用;核心线唯一获取入口,上线前经 gate GM 鉴权收口)。</summary>
        public void GmGrantPet(uint petTableId)
        {
            if (!HasList) { OnError?.Invoke("请先等待宝宝列表加载完成"); return; }
            if (!BeginWrite(petTableId == 0 ? "没有选中宝宝种类" : null)) return;
            int version = ++_grantRequestVersion;
            _net.Call(MessageIds.GmGrantPet, new GmGrantPetRequest { PetTableId = petTableId },
                GmGrantPetResponse.Parser,
                resp => { if (version == _grantRequestVersion) CompleteWrite("GM 发宝宝失败", resp.ErrorMessage, resp.Pets); },
                err => { if (version == _grantRequestVersion) FailWrite(err); });
        }

        // ── 内部 ────────────────────────────────────────────

        private bool BeginWrite(string rejectReason)
        {
            if (CultivationWritesBlocked)
            {
                OnError?.Invoke(CultivationLockedMessage);
                return false;
            }
            if (!_net.IsReady)
            {
                OnError?.Invoke("尚未进入游戏");
                return false;
            }
            if (rejectReason != null)
            {
                OnError?.Invoke(rejectReason);
                return false;
            }
            if (Busy)
            {
                OnError?.Invoke("上一次操作还在处理中,请稍候");
                return false;
            }
            SetBusy(true);
            return true;
        }

        /// <summary>写请求回包统一收口:先看响应体 tip(拒绝 → 报错、保留旧列表),再应用全量列表。</summary>
        private void CompleteWrite(string what, TipInfoMessage tip, PetListInfo list)
        {
            if (HasTip(tip))
            {
                FailWrite(DescribeTip(what, tip));
                return;
            }
            SetBusy(false);
            ApplyList(list);
        }

        private void FailWrite(string message)
        {
            SetBusy(false);
            OnError?.Invoke(message);
        }

        private void SetBusy(bool value)
        {
            if (Busy == value) return;
            Busy = value;
            OnBusyChanged?.Invoke(value);
        }

        private void ApplyList(PetListInfo list)
        {
            if (list == null)
            {
                // 服务器只在成功路径填 pets;为空说明协议层出错,保留旧列表并报错,
                // 不要清成 null(会让整个界面塌成空白)
                OnError?.Invoke("服务器未返回宝宝列表");
                return;
            }
            // 较早发起的全量查询不能回滚本端已确认的条目版本。
            if (Pets != null) foreach (var incoming in list.Pets)
                foreach (var current in Pets.Pets)
                    if (current.PetId == incoming.PetId && current.Entries != null && incoming.Entries != null &&
                        (current.Entries.StateVersion > incoming.Entries.StateVersion ||
                         current.Entries.ConfigVersion > incoming.Entries.ConfigVersion)) incoming.Entries = current.Entries.Clone();
            Pets = list;
            ++_listRequestVersion;
            ListRequestPending = false;
            OnList?.Invoke(list);
        }

        private void HandleListChanged(MessageContent content)
        {
            var message = PetListChangedS2C.Parser.ParseFrom(content.SerializedMessage);
            ApplyList(message.Pets);
        }

        /// <summary>登出、换角、静默重连或跨区重定向时清空列表、商店序号与在途请求。</summary>
        public void Reset()
        {
            InvalidateShop();
            InvalidateEntries();
            _pendingAscend = null;
            ++_sessionVersion;
            ++_listRequestVersion;
            ++_grantRequestVersion;
            ++_allocationRequestVersion;
            ListRequestPending = false;
            Pets = null;
            SetBusy(false);
        }

        private void HandleDisconnected() => Reset();
    }
}
