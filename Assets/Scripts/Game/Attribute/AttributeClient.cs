using System;
using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Attribute
{
    /// <summary>
    /// 角色属性加点网络层(服务端设计:docs/design/player-attribute-allocation.md)。
    ///
    /// 契约要点(与 <see cref="BattleClient"/> / <see cref="SpectateClient"/> 同惯例):
    ///  - 网络依赖收敛在 <see cref="IBattleTransport"/>(纯测试缝,不另起网络栈),
    ///    本类不引用 UnityEngine,可在 EditMode 里用假实现穷打;
    ///  - **服务器是唯一真相**:每个写操作成功后服务器回全量面板,本地整体覆盖
    ///    <see cref="Panel"/>,绝不做本地增量推算(点数/上限/二级属性全在服务器算);
    ///  - 面板未到达前 UI 不允许提交(<see cref="HasPanel"/> 为 false);
    ///  - 一次只允许一个在途写请求(<see cref="Busy"/>),防连点把同一批点数提交两次
    ///    ——服务器侧 Allocate 是"目标值"幂等语义,重复提交也不会多扣点,这里只是省掉无谓往返;
    ///  - 断线:清空面板与在途状态,重连后由 UI 重新 RequestPanel。
    /// </summary>
    public sealed class AttributeClient : IDisposable
    {
        /// <summary>单例挂接(实例由 GameClient 持有,静态 Instance 供 UI 路解析;测试直接 new)。</summary>
        public static AttributeClient Instance { get; private set; }

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private object _connection;
        private ulong _player;
        private long _epoch, _serial, _panelRevision, _latestRead, _latestAuto, _activeWrite;
        private bool _disposed, _refreshAfterWrite;

        private readonly struct RequestStamp
        {
            public readonly long Epoch, Id, Revision;
            public readonly ulong Player;
            public readonly object Connection;
            public RequestStamp(long epoch, long id, long revision, ulong player, object connection)
            { Epoch=epoch; Id=id; Revision=revision; Player=player; Connection=connection; }
        }

        // ── 契约属性 ────────────────────────────────────────

        /// <summary>最新权威面板(null = 尚未拉取/已作废)。</summary>
        public AttributePanelInfo Panel { get; private set; }

        public bool HasPanel => Panel != null;

        /// <summary>有写请求在途(UI 据此禁用提交按钮)。</summary>
        public bool Busy { get; private set; }

        public ulong MyPlayerId => _net.PlayerId;

        // ── 契约事件 ────────────────────────────────────────

        /// <summary>面板更新(拉取/写成功/服务器主动推送);参数即 <see cref="Panel"/>。</summary>
        public event Action<AttributePanelInfo> OnPanel;

        /// <summary>自动加点建议(pool_id, dimension_id → 目标已分配点);只是预览,需玩家确认后再提交。</summary>
        public event Action<uint, IReadOnlyDictionary<uint, uint>> OnAutoSuggestion;

        /// <summary>在途状态变化(供 UI 刷新按钮可用性)。</summary>
        public event Action<bool> OnBusyChanged;

        public event Action<string> OnError;

        // ── 构造/挂接 ───────────────────────────────────────

        public AttributeClient(IBattleTransport transport, Func<object> connectionIdentity = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? (() => transport);
            _connection = _connectionIdentity();
            _player = _net.PlayerId;
            _net.RegisterNotify(MessageIds.NotifyAttributePanelChanged, HandlePanelChanged);
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>生产入口:创建实例并登记为单例(GameClient 构造时调用)。</summary>
        public static AttributeClient Attach(IBattleTransport transport, Func<object> connectionIdentity = null)
        {
            Instance?.Dispose();
            var client = new AttributeClient(transport, connectionIdentity);
            Instance = client;
            return client;
        }

        // ── tip ─────────────────────────────────────────────

        /// <summary>
        /// 属性 RPC 的拒绝码放在**响应体** error_message 里(scene handler 写全局 tip → TRANSFER_ERROR_MESSAGE),
        /// 信封 MessageContent.error_message 只承载 gate 级错误。GameClient.Call 只折算信封,
        /// 所以每个响应回调必须先看响应体 tip,否则所有服务端拒绝都会被当成"成功但没面板"。
        /// 照 BattleClient.HasTip / DescribeTip 惯例。
        /// </summary>
        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        /// <summary>
        /// tip 文案:镜像 data/tip/Tip.xlsx 的 //attribute_error 组(base=25000)。表改了要同步这里;
        /// 2026-09-09 修正:原来写的是 130-144 那套旧号,tip 码轴改成按段发号后就全部对不上了,
        /// 所有服务端拒绝都退化成裸 "tip=25004"。
        /// 未收录的编号退回裸编号(全仓既有口径 "tip=N")。
        /// </summary>
        public static string DescribeTip(string what, TipInfoMessage tip)
        {
            if (!HasTip(tip)) return what;
            string text = tip.Id switch
            {
                25000 => "属性池不存在",
                25001 => "该属性池尚未解锁",
                25002 => "属性维度不存在",
                25003 => "剩余点数不足",
                25004 => "已分配的点数不能减少,请使用重置",
                25005 => "超过单项可分配上限",
                25006 => "加点方案不存在",
                25007 => "加点方案数量已达上限",
                25008 => "方案切换冷却中",
                25009 => "方案名称不合法",
                25010 => "已经是当前方案",
                25011 => "战斗中不能修改属性",
                // 契约 §0-1 把 kCurrencyGold 叫"银两"。Tip.xlsx 这一行仍写"金币不足",客户端先改叫法,
                // 表的文案随下一个改 Tip.xlsx 的批次同步(服务端 docs/design/guild-phase2/92-handoff.md §13)。
                25012 => "银两不足",
                25013 => "没有可用的自动加点方案",
                25014 => "属性没有变化",
                // ── 资产通道(//asset_error base=27000,帮会二期 B4a 起)────────────────
                // 冻结(27003)、封禁(27005)与 GM 路径会把这一段透传给客户端;宠物 / 加点自己的
                // 扣币路径在扣之前先 CanAfford,回的是各自的"银两不足",走不到 27000。
                // 文案逐字镜像 data/tip/Tip.xlsx,表改了要同步这里(与上面各段同一约定)。
                27000 => "货币不足",
                27001 => "背包已满,请腾出空间,之后会重试发放;超过时限可能取消",
                27002 => "战斗中暂不结算,稍后自动重试;超过时限可能取消",
                27003 => "角色迁移中,稍后自动重试;超过时限可能取消",
                27004 => "资产指令无效",
                27005 => "该资产当前被禁止获取",
                27006 => "角色不在线,上线后重试;超过时限可能取消",
                27007 => "资产只发放了一部分,已记录,将由客服补偿",
                27008 => "资产指令校验失败",
                _ => $"tip={tip.Id}",
            };
            return $"{what}:{text}";
        }

        // ── 读 ──────────────────────────────────────────────

        /// <summary>拉取面板(打开界面/重连后调用)。</summary>
        public void RequestPanel()
        {
            if (!Ready("尚未进入游戏,无法查看属性")) return;
            // 写入期间的读取合并到写回包之后，避免读到修改前的面板。
            if (Busy) { _refreshAfterWrite = true; return; }
            var stamp = Capture();
            _latestRead = stamp.Id;
            _net.Call(MessageIds.GetAttributePanel, new GetAttributePanelRequest(),
                GetAttributePanelResponse.Parser,
                resp =>
                {
                    if (!CurrentRead(stamp)) return;
                    _latestRead = 0;
                    if (HasTip(resp.ErrorMessage)) { OnError?.Invoke(DescribeTip("拉取属性面板失败", resp.ErrorMessage)); return; }
                    ApplyPanel(resp.Panel);
                },
                err => { if (CurrentRead(stamp)) { _latestRead = 0; OnError?.Invoke(err); } });
        }

        // ── 写 ──────────────────────────────────────────────

        /// <summary>
        /// 确认加点:提交某池「目标已分配值」(全量、幂等)。target 只需包含改动的维度,
        /// 缺省维度服务器视为不变;服务器只接受"只增不减",减点走 <see cref="ResetPool"/>。
        /// </summary>
        public void Allocate(uint poolId, IReadOnlyDictionary<uint, uint> target)
        {
            if (!BeginWrite(target != null && target.Count > 0 ? null : "没有需要提交的加点", out var stamp))
            {
                return;
            }
            var req = new AllocateAttributePointsRequest { PoolId = poolId };
            foreach (var kv in target)
            {
                req.Allocated[kv.Key] = kv.Value;
            }
            _net.Call(MessageIds.AllocateAttributePoints, req, AllocateAttributePointsResponse.Parser,
                resp => CompleteWrite(stamp, "加点失败", resp.ErrorMessage, resp.Panel),
                err => FailWrite(stamp, err));
        }

        /// <summary>重置(洗点):清空当前方案里该池的全部分配,按表扣银两。</summary>
        public void ResetPool(uint poolId)
        {
            if (!BeginWrite(null, out var stamp))
            {
                return;
            }
            _net.Call(MessageIds.ResetAttributePoints, new ResetAttributePointsRequest { PoolId = poolId },
                ResetAttributePointsResponse.Parser,
                resp => CompleteWrite(stamp, "重置失败", resp.ErrorMessage, resp.Panel),
                err => FailWrite(stamp, err));
        }

        /// <summary>
        /// 自动加点:服务器按职业方案算建议分配,**只算不落**。结果经
        /// <see cref="OnAutoSuggestion"/> 回来填进面板预览,玩家点「确认」才走 <see cref="Allocate"/>。
        /// 因为不改服务器状态,不占 <see cref="Busy"/>。
        /// </summary>
        public void AutoAllocate(uint poolId)
        {
            if (!Ready("尚未进入游戏,无法自动加点")) return;
            if (Busy) { OnError?.Invoke("上一次操作还在处理中,请稍候"); return; }
            var stamp = Capture();
            _latestAuto = stamp.Id;
            _net.Call(MessageIds.AutoAllocateAttributePoints,
                new AutoAllocateAttributePointsRequest { PoolId = poolId },
                AutoAllocateAttributePointsResponse.Parser,
                resp =>
                {
                    if (!CurrentAuto(stamp)) return;
                    _latestAuto = 0;
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
                    OnAutoSuggestion?.Invoke(resp.PoolId, suggestion);
                },
                err => { if (CurrentAuto(stamp)) { _latestAuto = 0; OnError?.Invoke(err); } });
        }

        /// <summary>开启新方案(超出免费数后按表扣银两)。</summary>
        public void CreateScheme(string name)
        {
            if (!BeginWrite(null, out var stamp))
            {
                return;
            }
            _net.Call(MessageIds.CreateAttributeScheme, new CreateAttributeSchemeRequest { Name = name ?? string.Empty },
                CreateAttributeSchemeResponse.Parser,
                resp => CompleteWrite(stamp, "开启新方案失败", resp.ErrorMessage, resp.Panel),
                err => FailWrite(stamp, err));
        }

        /// <summary>切换当前生效方案(服务器侧有冷却,战斗中拒绝)。</summary>
        public void SwitchScheme(uint schemeId)
        {
            if (!BeginWrite(null, out var stamp))
            {
                return;
            }
            _net.Call(MessageIds.SwitchAttributeScheme, new SwitchAttributeSchemeRequest { SchemeId = schemeId },
                SwitchAttributeSchemeResponse.Parser,
                resp => CompleteWrite(stamp, "切换方案失败", resp.ErrorMessage, resp.Panel),
                err => FailWrite(stamp, err));
        }

        public void RenameScheme(uint schemeId, string name)
        {
            if (!BeginWrite(string.IsNullOrWhiteSpace(name) ? "方案名不能为空" : null, out var stamp))
            {
                return;
            }
            _net.Call(MessageIds.RenameAttributeScheme,
                new RenameAttributeSchemeRequest { SchemeId = schemeId, Name = name },
                RenameAttributeSchemeResponse.Parser,
                resp => CompleteWrite(stamp, "方案改名失败", resp.ErrorMessage, resp.Panel),
                err => FailWrite(stamp, err));
        }

        /// <summary>GM:直接设等级(开发调试用;服务器侧上线前经 gate GM 鉴权收口)。</summary>
        public void GmSetLevel(uint level)
        {
            if (!BeginWrite(null, out var stamp))
            {
                return;
            }
            _net.Call(MessageIds.GmSetPlayerLevel, new GmSetPlayerLevelRequest { Level = level },
                GmSetPlayerLevelResponse.Parser,
                resp => CompleteWrite(stamp, "GM 设等级失败", resp.ErrorMessage, resp.Panel),
                err => FailWrite(stamp, err));
        }

        // ── 内部 ────────────────────────────────────────────

        private bool BeginWrite(string rejectReason, out RequestStamp stamp)
        {
            stamp = default;
            if (!Ready("尚未进入游戏")) return false;
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
            _latestRead = _latestAuto = 0;
            stamp = Capture();
            _activeWrite = stamp.Id;
            SetBusy(true);
            return true;
        }

        /// <summary>写请求回包统一收口:先看响应体 tip(拒绝 → 报错、保留旧面板),再应用全量面板。</summary>
        private void CompleteWrite(RequestStamp stamp, string what, TipInfoMessage tip, AttributePanelInfo panel)
        {
            if (!CurrentWrite(stamp)) return;
            if (HasTip(tip))
            {
                FailWrite(stamp, DescribeTip(what, tip));
                return;
            }
            // 协议没有服务器面板版本号。等待期间收到推送时保留推送，并在写入结束后重读确认。
            // 这样既不让旧回包盖住装备推送，也不假定推送一定晚于服务端执行此次写入。
            bool refresh = _refreshAfterWrite || stamp.Revision != _panelRevision;
            if (stamp.Revision == _panelRevision) ApplyPanel(panel);
            FinishWrite(stamp, refresh);
        }

        private void FailWrite(RequestStamp stamp, string message)
        {
            if (!CurrentWrite(stamp)) return;
            bool refresh = _refreshAfterWrite;
            FinishWrite(stamp, refresh);
            if (!Current(stamp)) return;
            OnError?.Invoke(message);
        }

        private void FinishWrite(RequestStamp stamp, bool refresh)
        {
            if (!CurrentWrite(stamp)) return;
            refresh |= _refreshAfterWrite;
            _activeWrite = 0;
            _refreshAfterWrite = false;
            SetBusy(false);
            if (refresh && Current(stamp)) RequestPanel();
        }

        private RequestStamp Capture() => new RequestStamp(_epoch, ++_serial, _panelRevision, _player, _connection);

        private void ObserveSession()
        {
            if (_disposed) return;
            if (_player != _net.PlayerId || !ReferenceEquals(_connection, _connectionIdentity())) Reset();
        }

        private bool Ready(string message)
        {
            if (_disposed) return false;
            ObserveSession();
            if (_net.IsReady && _net.PlayerId != 0) return true;
            OnError?.Invoke(message);
            return false;
        }

        private bool Current(RequestStamp stamp)
        {
            if (_disposed) return false;
            ObserveSession();
            return _net.IsReady && stamp.Epoch == _epoch && stamp.Player != 0 && stamp.Player == _net.PlayerId
                && ReferenceEquals(stamp.Connection, _connectionIdentity());
        }
        private bool CurrentRead(RequestStamp stamp) => Current(stamp) && stamp.Id == _latestRead
            && stamp.Revision == _panelRevision && !Busy;
        private bool CurrentAuto(RequestStamp stamp) => Current(stamp) && stamp.Id == _latestAuto
            && stamp.Revision == _panelRevision && !Busy;
        private bool CurrentWrite(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeWrite && Busy;

        /// <summary>登出、换号或连接替换时作废旧面板和全部回调，不让旧写释放新会话的等待状态。</summary>
        public void Reset()
        {
            ++_epoch;
            ++_panelRevision;
            _latestRead = _latestAuto = _activeWrite = 0;
            _refreshAfterWrite = false;
            _player = _net.PlayerId;
            _connection = _connectionIdentity();
            Panel = null;
            SetBusy(false);
        }

        private void SetBusy(bool value)
        {
            if (Busy == value)
            {
                return;
            }
            Busy = value;
            OnBusyChanged?.Invoke(value);
        }

        private void ApplyPanel(AttributePanelInfo panel)
        {
            if (panel == null)
            {
                // 服务器只在成功路径填 panel;为空说明协议层出错,保留旧面板并报错,
                // 不要清成 null(会让整个界面塌成空白)
                OnError?.Invoke("服务器未返回属性面板");
                return;
            }
            Panel = panel;
            ++_panelRevision;
            _latestAuto = 0;
            OnPanel?.Invoke(panel);
        }

        private void HandlePanelChanged(MessageContent content)
        {
            if (_disposed) return;
            ObserveSession();
            if (!_net.IsReady || _net.PlayerId == 0) return;
            var message = AttributePanelChangedS2C.Parser.ParseFrom(content.SerializedMessage);
            ApplyPanel(message.Panel);
        }

        private void HandleDisconnected() => Reset();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _net.Disconnected -= HandleDisconnected;
            Reset();
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
