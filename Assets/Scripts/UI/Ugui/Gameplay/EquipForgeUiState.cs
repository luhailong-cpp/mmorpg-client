using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game.PlayerFeatures;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>强化 / 镶嵌面板此刻处在哪一步。</summary>
    public enum EquipForgeStage
    {
        /// <summary>面板没打开。</summary>
        Closed = 0,
        /// <summary>打开了,但人物背包或装备栏快照还没到:只画 <see cref="EquipForgeView.StageText"/>,按钮全灰。</summary>
        Loading,
        /// <summary>两份快照都在,却找不到这件装备(被卸进仓库、被消耗、换了角色……):提示后由界面关掉面板。</summary>
        Missing,
        Ready,
    }

    /// <summary>状态行的语气,界面按它选颜色。</summary>
    public enum EquipForgeTone
    {
        Normal = 0,
        /// <summary>操作在途。</summary>
        Busy,
        /// <summary>被拒绝 / 网络失败(原因来自数据层,原样显示)。</summary>
        Error,
        /// <summary>强化成功。</summary>
        Success,
        /// <summary>强化掷骰没过。这不是错误:消耗已扣,服务器照常回了快照。</summary>
        Failure,
    }

    /// <summary>一个宝石孔的显示档:空孔 / 镶着且生效 / 镶着但不生效(仍占孔、仍可取下)。</summary>
    public enum EquipForgeSocketKind
    {
        Empty = 0,
        Active,
        Inactive,
    }

    /// <summary>面板发出的三种操作;None = 没有(或在途的是穿上 / 卸下这类别处发起的装备操作)。</summary>
    public enum EquipForgeOperation
    {
        None = 0,
        Enhance,
        Inlay,
        Remove,
    }

    /// <summary>一个按钮的呈现:文字、能不能点、不能点的原因(能点时为空串)。</summary>
    public readonly struct EquipForgeButton
    {
        public readonly string Label;
        public readonly bool Enabled;
        /// <summary>置灰的原因,界面可放在按钮旁的小字或悬浮提示里。能点时为空串。</summary>
        public readonly string Hint;

        public EquipForgeButton(string label, bool enabled, string hint)
        {
            Label = label ?? string.Empty;
            Enabled = enabled;
            Hint = enabled ? string.Empty : hint ?? string.Empty;
        }
    }

    /// <summary>孔位列表里的一行。</summary>
    public sealed class EquipForgeSocketRow
    {
        /// <summary>孔位号(0 起),镶嵌 / 取下时原样传回 <see cref="EquipForgeUiState"/>。</summary>
        public uint Socket { get; internal set; }
        /// <summary>孔位名;服务器没配时已退回「孔位 N」。</summary>
        public string Name { get; internal set; } = string.Empty;
        /// <summary>整行文本,见 EquipDisplay.FormatSocketLine:「攻击孔：攻击宝石·一级（伤害 +30）」/「攻击孔：未镶嵌」。</summary>
        public string Text { get; internal set; } = string.Empty;
        public EquipForgeSocketKind Kind { get; internal set; }
        /// <summary>镶着的宝石图标 key;空孔为空串。</summary>
        public string GemIconKey { get; internal set; } = string.Empty;
        /// <summary>空孔是「镶嵌」(点了调 BeginInlay),占着的孔是「取下」(点了调 RemoveGem)。</summary>
        public EquipForgeButton Button { get; internal set; }
        /// <summary>true = 按钮是「取下」;false = 「镶嵌」。</summary>
        public bool IsRemove { get; internal set; }
        /// <summary>空孔时人物背包里能嵌进这个孔的宝石种数(按 config 计);占着的孔为 0。</summary>
        public int ChoiceCount { get; internal set; }
    }

    /// <summary>选宝石列表里的一项(一种宝石,不是一堆)。</summary>
    public sealed class EquipForgeGemOption
    {
        /// <summary>宝石 config;选中后原样传回 <see cref="EquipForgeUiState.ChooseGem"/>。</summary>
        public uint ConfigId { get; internal set; }
        public string Name { get; internal set; } = string.Empty;
        public string IconKey { get; internal set; } = string.Empty;
        /// <summary>人物背包里这种宝石各堆之和。</summary>
        public ulong Count { get; internal set; }
        /// <summary>「×3」。</summary>
        public string CountText { get; internal set; } = string.Empty;
        /// <summary>「伤害 +30」;服务器没给宝石属性时为空串。</summary>
        public string AttrText { get; internal set; } = string.Empty;
    }

    /// <summary>「给某个孔选宝石」这一步。</summary>
    public sealed class EquipForgeGemPicker
    {
        public uint Socket { get; internal set; }
        public string SocketName { get; internal set; } = string.Empty;
        /// <summary>「选择要镶入「攻击孔」的宝石」。</summary>
        public string Title { get; internal set; } = string.Empty;
        /// <summary>按 config 升序;至少一项(没有可选的宝石时这一步不会出现)。</summary>
        public IReadOnlyList<EquipForgeGemOption> Options { get; internal set; } = Array.Empty<EquipForgeGemOption>();
        /// <summary>此刻能不能选(在途 / 冷却 / 整理背包时为 false,原因在 <see cref="Hint"/>)。</summary>
        public bool CanChoose { get; internal set; }
        public string Hint { get; internal set; } = string.Empty;
    }

    /// <summary>「强化失败会降级,确认吗」这一步。只在下一级的失败规则会降级时出现。</summary>
    public sealed class EquipForgeConfirm
    {
        public string Title { get; internal set; } = string.Empty;
        /// <summary>多行纯文本(用 \n 分行):成功率、消耗、失败后果。</summary>
        public string Message { get; internal set; } = string.Empty;
        public string ConfirmLabel { get; internal set; } = string.Empty;
        public string CancelLabel { get; internal set; } = string.Empty;
        public bool CanConfirm { get; internal set; }
        public string Hint { get; internal set; } = string.Empty;
    }

    /// <summary>一次强化被服务器受理之后的提示(成功或掷骰没过)。</summary>
    public readonly struct EquipForgeNotice
    {
        public readonly ulong ItemId;
        public readonly bool Success;
        /// <summary>操作后的强化等级。</summary>
        public readonly uint Level;
        /// <summary>一句话,可直接做飘字 / toast:「强化成功：青铜剑 +4」。</summary>
        public readonly string Text;

        public EquipForgeNotice(ulong itemId, bool success, uint level, string text)
        {
            ItemId = itemId;
            Success = success;
            Level = level;
            Text = text ?? string.Empty;
        }
    }

    /// <summary>
    /// 强化 / 镶嵌面板的一份完整呈现快照。每次数据变化整份重建;界面只读,不要改它,也不要自己推算数值。
    /// 全部文本都是纯文本(服务器下发的名称原样拼入):一行一个 Text,关掉 richText。
    /// </summary>
    public sealed class EquipForgeView
    {
        public static readonly EquipForgeView Closed = new EquipForgeView();

        public EquipForgeStage Stage { get; internal set; }
        public ulong ItemId { get; internal set; }
        /// <summary>Loading / Missing 时的整块提示;Ready 时为空串。</summary>
        public string StageText { get; internal set; } = string.Empty;

        // ── 头部 ──
        /// <summary>带强化等级的名称「青铜剑 +3」(0 级不带后缀)。</summary>
        public string Title { get; internal set; } = string.Empty;
        public string IconKey { get; internal set; } = string.Empty;
        /// <summary>true = 穿在身上;false = 在行囊里。</summary>
        public bool Worn { get; internal set; }
        /// <summary>「已穿戴」/「行囊中」。</summary>
        public string LocationText { get; internal set; } = string.Empty;

        // ── 强化 ──
        public uint EnhanceLevel { get; internal set; }
        public uint EnhanceMaxLevel { get; internal set; }
        /// <summary>「强化等级 +3 / +12」;强化未开放时「该装备不可强化」。</summary>
        public string EnhanceLevelText { get; internal set; } = string.Empty;
        /// <summary>有下一级预览时为 true;下面四个文本与 <see cref="PreviewLines"/> 只在此时非空。</summary>
        public bool HasNextLevel { get; internal set; }
        /// <summary>「成功率 90%」。</summary>
        public string SuccessRateText { get; internal set; } = string.Empty;
        /// <summary>「消耗 银两 5000、强化石 ×2」。</summary>
        public string CostText { get; internal set; } = string.Empty;
        /// <summary>「持有 银两 12000、强化石 ×5」;银两余额不知道时写「—」。</summary>
        public string OwnedText { get; internal set; } = string.Empty;
        /// <summary>「失败不降级」/「失败降 1 级」。</summary>
        public string FailRuleText { get; internal set; } = string.Empty;
        /// <summary>每条基础属性一行「伤害：46 → 48」。</summary>
        public IReadOnlyList<string> PreviewLines { get; internal set; } = Array.Empty<string>();
        /// <summary>本地快照的强化前置检查结论(置灰原因的来源)。</summary>
        public EquipEnhanceCheck EnhanceCheck { get; internal set; } = EquipEnhanceCheck.Unavailable;
        /// <summary>「强化」按钮;点了调 <see cref="EquipForgeUiState.Enhance"/>。</summary>
        public EquipForgeButton EnhanceButton { get; internal set; } =
            new EquipForgeButton(EquipForgeUiState.EnhanceLabel, false, string.Empty);

        // ── 孔位 ──
        /// <summary>按孔位号升序,含空孔。</summary>
        public IReadOnlyList<EquipForgeSocketRow> Sockets { get; internal set; } = Array.Empty<EquipForgeSocketRow>();
        /// <summary>没有孔时「这件装备没有宝石孔」,否则空串。</summary>
        public string SocketsEmptyText { get; internal set; } = string.Empty;

        // ── 弹层(同一时刻至多一个非 null) ──
        /// <summary>正在给某个孔选宝石;null = 不在这一步。</summary>
        public EquipForgeGemPicker Picker { get; internal set; }
        /// <summary>正在确认一次会降级的强化;null = 不在这一步。</summary>
        public EquipForgeConfirm Confirm { get; internal set; }

        // ── 状态 ──
        /// <summary>面板底部的状态行:本面板打开之后出现的失败原因 / 在途 / 强化结果。没有时为空串。</summary>
        public string StatusText { get; internal set; } = string.Empty;
        public EquipForgeTone StatusTone { get; internal set; }
        /// <summary>穿上 / 卸下 / 强化 / 镶嵌 / 取下有一个在途(数据层同一时刻只放一个)。</summary>
        public bool Busy { get; internal set; }
        /// <summary>离上一次强化 / 镶嵌 / 取下发包不到最小间隔:按钮暂时置灰,时间到了由 Tick 重建。</summary>
        public bool CoolingDown { get; internal set; }
        /// <summary>「刷新」:重新拉人物背包与装备栏;点了调 <see cref="EquipForgeUiState.Refresh"/>。</summary>
        public EquipForgeButton RefreshButton { get; internal set; } =
            new EquipForgeButton(EquipForgeUiState.RefreshLabel, false, string.Empty);
    }

    /// <summary>
    /// 强化 / 镶嵌面板的按钮逻辑(服务端 docs/design/equipment-enhance-inlay.md §6;界面接线见客户端
    /// Docs/EquipmentForgeUI-handoff-20261015.md)。只管「此刻每个按钮能不能点、为什么不能、点了发什么、
    /// 选宝石 / 确认降级这两步、状态行与结果提示」,不碰 UnityEngine:界面读 <see cref="View"/> 来画,
    /// 在 <see cref="Changed"/> 里重画,按钮点击原样转给这里的意图方法。
    /// <para>
    /// 数值一律来自服务器快照,这里不推算;能不能强化 / 镶嵌最终由服务器裁决,这里的置灰只是按本地快照给的提示
    /// (金币 / 材料「不知道」时不置灰)。点击转成数据层调用之前会按同一份快照再判一次,置灰的按钮被点到也不发包。
    /// 强化不是幂等操作:这里绝不自动重发,也不排队。
    /// </para>
    /// 线程:全部在 Unity 主线程(数据层的回调都落在主线程)。
    /// </summary>
    public sealed class EquipForgeUiState : IDisposable
    {
        public const string EnhanceLabel = "强化";
        public const string InlayLabel = "镶嵌";
        public const string RemoveLabel = "取下";
        public const string RefreshLabel = "刷新";
        // 置灰原因与状态行。BusySort / BusyMissionAction 两句与数据层被挡下时报的文案逐字相同。
        internal const string SortingHint = "背包正在整理，请稍候";
        internal const string MissionBusyHint = "任务正在处理中，请稍候";
        internal const string CoolingDownHint = "操作太快，请稍候";
        internal const string NoGemHint = "行囊里没有能嵌入这个孔的宝石";
        internal const string BagSyncFailedText = "行囊同步失败，请点击刷新重试";

        private PlayerFeaturesClient _client;
        private ulong _target;
        private uint? _pickingSocket;
        private bool _confirming;
        // 正在调用数据层的哪一个入口(只在调用期间非 None)。数据层在这次调用里进入在途,就记成 _inFlight。
        // 不能在调用返回后再看 BusyEquip:传输层可能在调用内部同步失败,返回时在途已经结束。
        private EquipForgeOperation _calling;
        private EquipForgeOperation _inFlight;
        // _inFlight 是对哪件装备发的。面板关了再为另一件打开时,那件的在途 / 失败不能挂到这件头上。
        private ulong _inFlightItem;
        // 本面板为当前这件发出的操作刚在这一次 OnChanged 里收口:紧跟着的 OnError 才是它的失败原因(数据层先 OnChanged 后 OnError)。
        private bool _ownOperationJustEnded;
        // 最近一次由本面板发出的强化(结果提示只认它)与发出时的等级 / 名称。
        private ulong _enhanceItem;
        private uint _enhanceLevelBefore;
        private string _enhanceName = string.Empty;
        // 状态行只显示本面板为这件装备发出的操作的失败原因与强化结果:数据层的 EquipmentError / LastEnhanceOutcome
        // 也可能是背包页上穿脱、或之前为另一件装备的操作留下的,拿来显示会让人以为是这次操作的结果。
        private string _freshError;
        private EquipForgeNotice? _freshOutcome;

        /// <summary>数据变化(快照、在途、冷却结束、弹层开合)后触发;界面在这里按 <see cref="View"/> 重画。可能连续触发多次。</summary>
        public event Action Changed;
        /// <summary>
        /// 本面板发出的一次强化被服务器受理(成功或掷骰没过),每次一回;面板已关上也照样触发(玩家可能点完就关了)。
        /// 适合做飘字 / toast。被拒绝、超时不触发(那些走状态行)。
        /// </summary>
        public event Action<EquipForgeNotice> Announced;

        /// <summary>当前呈现快照;从不为 null(关着时是 <see cref="EquipForgeView.Closed"/>)。</summary>
        public EquipForgeView View { get; private set; } = EquipForgeView.Closed;
        public bool IsOpen => _target != 0;
        public ulong TargetItemId => _target;

        /// <summary>
        /// 接到(或换成)一个数据层实例;null = 解绑。换绑一律关闭面板并清掉在途记录:旧连接 / 旧角色的回调不会再进来。
        /// GameplayUiRoot 在 GameClient.Features 换掉时调用。
        /// </summary>
        public void Bind(PlayerFeaturesClient client)
        {
            if (ReferenceEquals(client, _client)) return;
            if (_client != null)
            {
                _client.OnChanged -= OnClientChanged;
                _client.OnError -= OnClientError;
                _client.OnEnhanceResult -= OnEnhanceResult;
            }
            _client = client;
            _calling = _inFlight = EquipForgeOperation.None;
            _inFlightItem = 0;
            _ownOperationJustEnded = false;
            _enhanceItem = 0;
            if (_client != null)
            {
                _client.OnChanged += OnClientChanged;
                _client.OnError += OnClientError;
                _client.OnEnhanceResult += OnEnhanceResult;
            }
            Close();
        }

        /// <summary>
        /// 为一件装备打开面板(人物背包里的或穿在身上的,按物品实例 id);已经开着时换成这一件,两步弹层一并收起。
        /// 两份快照缺哪份才拉哪份:都在时打开面板不产生任何请求;行囊没同步上时从身上装备的卡片进来,会补拉行囊,
        /// 面板先处在 Loading。没有绑定数据层或 id 为 0 时什么都不做。
        /// </summary>
        public void Open(ulong itemId)
        {
            if (_client == null || itemId == 0) return;
            _target = itemId;
            _pickingSocket = null;
            _confirming = false;
            _freshError = null;
            _freshOutcome = null;
            // 下面两个请求会同步触发 OnChanged(→ 重建);目标已经记下,所以重建得出的是 Loading。
            if (_client.CharacterBag == null) _client.RequestBag(PlayerFeaturesClient.CharacterBagType);
            if (_client.Equipment == null) _client.RequestEquipment();
            Rebuild();
        }

        /// <summary>关闭面板(也关掉选宝石 / 确认两步)。在途的操作不受影响,它的结果照样走 <see cref="Announced"/>。</summary>
        public void Close()
        {
            if (_target == 0 && ReferenceEquals(View, EquipForgeView.Closed)) return;
            _target = 0;
            _pickingSocket = null;
            _confirming = false;
            _freshError = null;
            _freshOutcome = null;
            View = EquipForgeView.Closed;
            Changed?.Invoke();
        }

        /// <summary>重新拉人物背包与装备栏(也会收起数据层里上一次装备操作的失败原因)。</summary>
        public void Refresh()
        {
            if (!IsOpen || _client == null || !Build().RefreshButton.Enabled) return;
            _client.RequestBag(PlayerFeaturesClient.CharacterBagType);
            _client.RequestEquipment();
            Rebuild();
        }

        /// <summary>
        /// 点「强化」。按钮置灰时不做任何事。下一级的失败规则会降级时先进入确认这一步(<see cref="EquipForgeView.Confirm"/>),
        /// 否则直接发出。
        /// </summary>
        public void Enhance()
        {
            var view = Build();
            if (view.Stage != EquipForgeStage.Ready || !view.EnhanceButton.Enabled) return;
            var item = FindTarget(out _);
            if (EffectiveFailLoss(item) != 0)
            {
                _pickingSocket = null;
                _confirming = true;
                Rebuild();
                return;
            }
            SendEnhance(item);
        }

        /// <summary>确认那次会降级的强化。不在确认这一步,或此刻不能发,都不做任何事(确认框保持原样)。</summary>
        public void ConfirmEnhance()
        {
            if (!_confirming) return;
            var view = Build();
            if (view.Confirm == null || !view.Confirm.CanConfirm) return;
            _confirming = false;
            SendEnhance(FindTarget(out _));
        }

        public void CancelConfirm()
        {
            if (!_confirming) return;
            _confirming = false;
            Rebuild();
        }

        /// <summary>点某个空孔的「镶嵌」:进入选宝石这一步。孔不存在、已占用、没有可选宝石或按钮置灰时不做任何事。</summary>
        public void BeginInlay(uint socket)
        {
            var row = FindRow(Build(), socket);
            if (row == null || row.IsRemove || !row.Button.Enabled) return;
            _confirming = false;
            _pickingSocket = socket;
            Rebuild();
        }

        /// <summary>
        /// 选定一种宝石,发出镶嵌。只接受列表里有的 config;此刻不能选(在途 / 冷却 / 整理)时不做任何事,选宝石这一步保持打开。
        /// </summary>
        public void ChooseGem(uint gemConfigId)
        {
            var picker = Build().Picker;
            if (picker == null || !picker.CanChoose) return;
            bool listed = false;
            foreach (var option in picker.Options)
                if (option.ConfigId == gemConfigId) { listed = true; break; }
            if (!listed) return;
            _pickingSocket = null;
            ulong itemId = _target;
            uint socket = picker.Socket;
            Call(EquipForgeOperation.Inlay, () => _client.InlayGem(itemId, socket, gemConfigId));
        }

        public void CancelInlay()
        {
            if (!_pickingSocket.HasValue) return;
            _pickingSocket = null;
            Rebuild();
        }

        /// <summary>点某个占着的孔的「取下」:直接发出(免费、可逆,不需要确认)。按钮置灰时不做任何事。</summary>
        public void RemoveGem(uint socket)
        {
            var row = FindRow(Build(), socket);
            if (row == null || !row.IsRemove || !row.Button.Enabled) return;
            _pickingSocket = null;
            _confirming = false;
            ulong itemId = _target;
            Call(EquipForgeOperation.Remove, () => _client.RemoveGem(itemId, socket));
        }

        /// <summary>
        /// 每帧(或定时)调用:发包间隔是按时间走完的,数据层不会为它发通知。冷却状态变了才重建并触发 <see cref="Changed"/>,
        /// 其余时候什么都不做。
        /// </summary>
        public void Tick()
        {
            if (!IsOpen || _client == null) return;
            if ((_client.ForgeCooldownRemainingMs > 0) != View.CoolingDown) Rebuild();
        }

        public void Dispose() => Bind(null);

        // ── 数据层回调 ─────────────────────────────────────

        private void OnClientChanged()
        {
            if (_client == null) return;
            _ownOperationJustEnded = false;
            if (_client.BusyEquip)
            {
                if (_calling != EquipForgeOperation.None && _inFlight == EquipForgeOperation.None)
                {
                    // 本面板的请求刚进入在途:之前的失败原因与强化结果都不再是「这一次」的。
                    _inFlight = _calling;
                    _inFlightItem = _target;
                    _freshError = null;
                    _freshOutcome = null;
                }
            }
            else if (_inFlight != EquipForgeOperation.None)
            {
                _ownOperationJustEnded = IsOpen && _inFlightItem == _target;
                _inFlight = EquipForgeOperation.None;
            }
            if (IsOpen) Rebuild();
        }

        private void OnClientError(string message)
        {
            // 只认本面板为这件装备发出的操作的失败:调用数据层期间报的(发包前的本地拒绝、传输层同步失败),
            // 或者它的在途刚在上一次 OnChanged 里收口(数据层先 OnChanged 后 OnError)。背包页上穿脱的失败、
            // 为另一件装备发出的操作的失败、读背包 / 任务的失败都不记。读取失败另由 FillStatus 按状态显示。
            if (!IsOpen || _client == null || string.IsNullOrEmpty(message)) return;
            bool own = _calling != EquipForgeOperation.None || _ownOperationJustEnded;
            _ownOperationJustEnded = false;
            if (!own || message != _client.EquipmentError) return;
            _freshError = message;
            _freshOutcome = null; // 更新的一件事已经发生:上一次的强化结果不再挂在状态行上
            Rebuild();
        }

        private void OnEnhanceResult(EquipEnhanceOutcome outcome)
        {
            if (_enhanceItem == 0 || outcome.ItemId != _enhanceItem) return;
            var notice = new EquipForgeNotice(outcome.ItemId, outcome.Success, outcome.Level,
                OutcomeText(outcome, _enhanceName, _enhanceLevelBefore));
            if (IsOpen && outcome.ItemId == _target)
            {
                _freshOutcome = notice;
                Rebuild();
            }
            Announced?.Invoke(notice);
        }

        // ── 发出 ──────────────────────────────────────────

        private void SendEnhance(BagItemInfo item)
        {
            if (item == null) return;
            _pickingSocket = null;
            _confirming = false;
            _enhanceItem = item.ItemId;
            _enhanceLevelBefore = item.EnhanceLevel;
            _enhanceName = ItemName(item);
            ulong itemId = item.ItemId;
            Call(EquipForgeOperation.Enhance, () => _client.EnhanceEquip(itemId));
        }

        private void Call(EquipForgeOperation operation, Action call)
        {
            _calling = operation;
            try { call(); }
            finally { _calling = EquipForgeOperation.None; }
            // 本地拒绝 / 同步失败都已经通过 OnChanged / OnError 进来过;这里再按最终状态重建一次。
            if (IsOpen) Rebuild();
        }

        // ── 呈现 ──────────────────────────────────────────

        private void Rebuild()
        {
            View = Build();
            // 两步弹层随数据自动收起:孔已被占、宝石用完、不再需要确认、装备不见了 —— Build 算不出它们,就不再处在那一步。
            if (View.Picker == null) _pickingSocket = null;
            if (View.Confirm == null) _confirming = false;
            Changed?.Invoke();
        }

        /// <summary>
        /// 按此刻的数据层状态整份算出呈现快照。没有副作用:意图方法也用它来复核按钮此刻能不能点。
        /// </summary>
        private EquipForgeView Build()
        {
            if (_target == 0 || _client == null) return EquipForgeView.Closed;
            var client = _client;
            bool cooling = client.ForgeCooldownRemainingMs > 0;
            var view = new EquipForgeView
            {
                ItemId = _target,
                Busy = client.BusyEquip,
                CoolingDown = cooling,
                RefreshButton = new EquipForgeButton(RefreshLabel,
                    !client.BagLoading && !client.EquipmentLoading && !client.BusyEquip && !client.BusySort,
                    client.BusySort ? SortingHint : client.BusyEquip ? CurrentBusyText() : "正在同步…"),
            };
            var bag = client.CharacterBag;
            var equipment = client.Equipment;
            if (bag == null || equipment == null)
            {
                view.Stage = EquipForgeStage.Loading;
                bool failed = (bag == null && !client.BagLoading && !string.IsNullOrEmpty(client.BagError))
                    || (equipment == null && !client.EquipmentLoading && !string.IsNullOrEmpty(client.EquipmentReadError));
                view.StageText = failed ? "行囊或装备栏暂未同步，请点击刷新重试" : "正在同步行囊与装备栏…";
                Disable(view);
                return view;
            }
            var item = FindTarget(out bool worn);
            if (item == null)
            {
                view.Stage = EquipForgeStage.Missing;
                view.StageText = "这件装备已不在行囊或装备栏中";
                Disable(view);
                return view;
            }

            view.Stage = EquipForgeStage.Ready;
            string name = ItemName(item);
            view.Title = EquipDisplay.NameWithEnhance(name, item.EnhanceLevel);
            view.IconKey = item.IconKey ?? string.Empty;
            view.Worn = worn;
            view.LocationText = worn ? "已穿戴" : "行囊中";

            // 整块置灰的原因:在途 > 整理背包 > 任务操作 > 发包间隔。数据层遇到前三种会拒绝或静默丢弃,第四种静默丢弃。
            string gate = client.BusyEquip ? CurrentBusyText() : client.BusySort ? SortingHint :
                client.BusyMissionAction ? MissionBusyHint : cooling ? CoolingDownHint : null;

            FillEnhance(view, item, bag, gate);
            FillSockets(view, item, bag, gate);
            FillStatus(view, client);
            return view;
        }

        private void FillEnhance(EquipForgeView view, BagItemInfo item, BagInfo bag, string gate)
        {
            view.EnhanceLevel = item.EnhanceLevel;
            view.EnhanceMaxLevel = item.EnhanceMaxLevel;
            view.EnhanceLevelText = item.EnhanceMaxLevel == 0 ? "该装备不可强化"
                : $"强化等级 +{Number(item.EnhanceLevel)} / +{Number(item.EnhanceMaxLevel)}";
            var check = EquipBagModel.CheckEnhance(item, bag);
            view.EnhanceCheck = check;
            var next = item.EnhanceNext;
            // 满级 / 未开放 / 实例未初始化时服务器不带预览:那时不显示成功率与消耗(EquipDisplay 同口径)。
            bool hasNext = next != null && check != EquipEnhanceCheck.MaxLevel && item.EnhanceMaxLevel != 0;
            view.HasNextLevel = hasNext;
            if (hasNext)
            {
                view.SuccessRateText = "成功率 " + EquipDisplay.FormatSuccessRate(next.SuccessRate);
                view.CostText = "消耗 " + EquipDisplay.FormatEnhanceCost(next);
                view.OwnedText = OwnedText(next, bag);
                uint loss = EffectiveFailLoss(item);
                view.FailRuleText = loss == 0 ? "失败不降级" : $"失败降 {Number(loss)} 级";
                view.PreviewLines = EquipDisplay.EnhancePreviewLines(item);
            }

            string reason = gate ?? CheckHint(check);
            view.EnhanceButton = new EquipForgeButton(EnhanceLabel, reason == null, reason);

            // 确认这一步只为「会降级」的那一级存在:预览没了、规则变成不降级、按钮因为规则原因灰掉,都算不出来(随即收起)。
            // 在途 / 冷却不收起,只是暂时不能确认。
            uint confirmLoss = EffectiveFailLoss(item);
            if (_confirming && hasNext && confirmLoss != 0 && CheckHint(check) == null)
            {
                view.Confirm = new EquipForgeConfirm
                {
                    Title = "确认强化",
                    Message = $"{view.SuccessRateText}，{view.CostText}。\n" +
                        $"失败会降 {Number(confirmLoss)} 级（+{Number(item.EnhanceLevel)} → +{Number(item.EnhanceLevel - confirmLoss)}），消耗不退还。",
                    ConfirmLabel = "确认强化",
                    CancelLabel = "取消",
                    CanConfirm = gate == null,
                    Hint = gate ?? string.Empty,
                };
            }
        }

        private void FillSockets(EquipForgeView view, BagItemInfo item, BagInfo bag, string gate)
        {
            var rows = new List<EquipForgeSocketRow>();
            EquipForgeGemPicker picker = null;
            foreach (var socket in EquipBagModel.GemSockets(item))
            {
                var row = new EquipForgeSocketRow
                {
                    Socket = socket.Socket,
                    Name = socket.Name,
                    Text = EquipDisplay.FormatSocketLine(socket.Info),
                    Kind = !socket.Occupied ? EquipForgeSocketKind.Empty
                        : socket.Active ? EquipForgeSocketKind.Active : EquipForgeSocketKind.Inactive,
                    GemIconKey = socket.Occupied ? socket.Info.GemIconKey ?? string.Empty : string.Empty,
                    IsRemove = socket.Occupied,
                };
                if (socket.Occupied)
                {
                    // 不生效的宝石照样占着孔,照样给取下入口。
                    row.Button = new EquipForgeButton(RemoveLabel, gate == null, gate);
                }
                else
                {
                    var choices = EquipBagModel.GemChoices(socket.Info, bag);
                    row.ChoiceCount = choices.Count;
                    string reason = gate ?? (choices.Count == 0 ? NoGemHint : null);
                    row.Button = new EquipForgeButton(InlayLabel, reason == null, reason);
                    if (_pickingSocket == socket.Socket && choices.Count != 0)
                        picker = Picker(socket, choices, gate);
                }
                rows.Add(row);
            }
            view.Sockets = rows;
            view.SocketsEmptyText = rows.Count == 0 ? "这件装备没有宝石孔" : string.Empty;
            // 那个孔已被占用、已不存在或宝石用完了:算不出选宝石这一步(随即收起)。两步弹层同一时刻至多一个。
            view.Picker = picker;
            if (picker != null) view.Confirm = null;
        }

        private static EquipForgeGemPicker Picker(EquipGemSocketView socket, List<EquipGemChoice> choices, string gate)
        {
            var options = new List<EquipForgeGemOption>(choices.Count);
            foreach (var choice in choices)
                options.Add(new EquipForgeGemOption
                {
                    ConfigId = choice.ConfigId,
                    Name = EquipDisplay.ItemNameOr(choice.Item?.Name, choice.ConfigId),
                    IconKey = choice.Item?.IconKey ?? string.Empty,
                    Count = choice.Count,
                    CountText = "×" + choice.Count.ToString(CultureInfo.InvariantCulture),
                    AttrText = EquipDisplay.FormatGemAttr(choice.Item?.GemAttr),
                });
            return new EquipForgeGemPicker
            {
                Socket = socket.Socket,
                SocketName = socket.Name,
                Title = $"选择要镶入「{socket.Name}」的宝石",
                Options = options,
                CanChoose = gate == null,
                Hint = gate ?? string.Empty,
            };
        }

        private void FillStatus(EquipForgeView view, PlayerFeaturesClient client)
        {
            // 本面板操作的失败原因 > 在途 > 快照没同步上 > 强化结果。
            // 失败原因只认本面板为这件装备发出的操作报出的、而且此刻仍是装备侧当前错误的那一条:玩家刷新、或下一次操作
            // 开始时数据层会把它收起,这里跟着消失。
            string error = client.EquipmentError;
            if (!string.IsNullOrEmpty(error) && error == _freshError)
            {
                view.StatusText = error;
                view.StatusTone = EquipForgeTone.Error;
            }
            else if (client.BusyEquip)
            {
                view.StatusText = CurrentBusyText();
                view.StatusTone = EquipForgeTone.Busy;
            }
            // 快照没同步上(刷新失败、失败后的自动重拉失败……):置灰与数量都还是旧快照算的,得让玩家知道。按状态显示,
            // 不按事件:下一次读取一开始数据层就把它清掉。装备栏的原因数据层已写成中文;行囊的可能是传输层原文,用固定说法。
            else if (!client.EquipmentLoading && !string.IsNullOrEmpty(client.EquipmentReadError))
            {
                view.StatusText = client.EquipmentReadError;
                view.StatusTone = EquipForgeTone.Error;
            }
            else if (!client.BagLoading && !string.IsNullOrEmpty(client.BagError)
                && client.RequestedBagType == PlayerFeaturesClient.CharacterBagType)
            {
                view.StatusText = BagSyncFailedText;
                view.StatusTone = EquipForgeTone.Error;
            }
            // 强化结果只挂到下一次装备类操作开始为止(不论谁发起的):数据层那时清掉 LastEnhanceOutcome,这里跟着收起。
            else if (_freshOutcome.HasValue && _freshOutcome.Value.ItemId == _target
                && client.LastEnhanceOutcome.HasValue && client.LastEnhanceOutcome.Value.ItemId == _target)
            {
                view.StatusText = _freshOutcome.Value.Text;
                view.StatusTone = _freshOutcome.Value.Success ? EquipForgeTone.Success : EquipForgeTone.Failure;
            }
        }

        private static void Disable(EquipForgeView view)
            => view.EnhanceButton = new EquipForgeButton(EnhanceLabel, false, string.Empty);

        // ── 小工具 ────────────────────────────────────────

        /// <summary>目标此刻在哪:与数据层、服务端同序,先人物背包后装备栏。</summary>
        private BagItemInfo FindTarget(out bool worn)
        {
            worn = false;
            if (_client == null || _target == 0) return null;
            if (_client.CharacterBag != null)
                foreach (var item in _client.CharacterBag.Items)
                    if (item.ItemId == _target) return item;
            if (_client.Equipment != null)
                foreach (var item in _client.Equipment.Items)
                    if (item.ItemId == _target) { worn = true; return item; }
            return null;
        }

        private static EquipForgeSocketRow FindRow(EquipForgeView view, uint socket)
        {
            if (view.Stage != EquipForgeStage.Ready) return null;
            foreach (var row in view.Sockets)
                if (row.Socket == socket) return row;
            return null;
        }

        private static string ItemName(BagItemInfo item) => EquipDisplay.ItemNameOr(item.Name, item.ConfigId);

        /// <summary>
        /// 这一级强化失败实际会掉几级。服务器按「最多掉到 +0」处理(服务器仓 equip_attribute_rules.h 的 LevelAfterFailure),
        /// 所以按当前等级封顶:+0 的装备遇到「失败降 1 级」的行,实际不会掉级,也就不必确认。没有下一级预览时为 0。
        /// </summary>
        private static uint EffectiveFailLoss(BagItemInfo item)
            => item?.EnhanceNext == null ? 0u : Math.Min(item.EnhanceNext.FailLevelLoss, item.EnhanceLevel);

        private static string Number(uint value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>强化按钮因为规则(不是因为在途 / 冷却)灰掉的原因;可以强化或本地无从判断时为 null。</summary>
        private static string CheckHint(EquipEnhanceCheck check) => check switch
        {
            EquipEnhanceCheck.Unavailable => "该装备无法强化",
            EquipEnhanceCheck.MaxLevel => "已强化到最高等级",
            EquipEnhanceCheck.MaterialNotEnough => "强化材料不足",
            EquipEnhanceCheck.GoldNotEnough => "银两不足",
            _ => null,
        };

        /// <summary>在途文案:本面板为这一件发出的说具体操作;背包页上的穿脱、为另一件发出的操作一律「正在处理装备…」。</summary>
        private string CurrentBusyText()
            => BusyText(_inFlight != EquipForgeOperation.None && _inFlightItem == _target ? _inFlight : EquipForgeOperation.None);

        private static string BusyText(EquipForgeOperation operation) => operation switch
        {
            EquipForgeOperation.Enhance => "正在强化…",
            EquipForgeOperation.Inlay => "正在镶嵌…",
            EquipForgeOperation.Remove => "正在取下宝石…",
            _ => "正在处理装备…",
        };

        /// <summary>「持有 银两 12000、强化石 ×5」:只列这一级要消耗的东西;银两余额不知道时写「—」。</summary>
        private static string OwnedText(EquipEnhancePreview next, BagInfo bag)
        {
            var currency = bag?.Currency;
            string gold = currency != null && currency.Values.Count > PlayerFeaturesClient.GoldCurrencyIndex
                ? currency.Values[PlayerFeaturesClient.GoldCurrencyIndex].ToString(CultureInfo.InvariantCulture) : "—";
            string text = "持有 银两 " + gold;
            if (next.MaterialConfigId != 0 && next.MaterialCount != 0)
                text += $"、{EquipDisplay.ItemNameOr(next.MaterialName, next.MaterialConfigId)} ×" +
                    EquipBagModel.CountOf(bag, next.MaterialConfigId).ToString(CultureInfo.InvariantCulture);
            return text;
        }

        /// <summary>强化结果的一句话。等级以服务器回的为准;比发出时低 = 失败掉了级。</summary>
        private static string OutcomeText(EquipEnhanceOutcome outcome, string name, uint levelBefore)
        {
            string level = "+" + Number(outcome.Level);
            if (outcome.Success) return $"强化成功：{name} {level}";
            return outcome.Level < levelBefore
                ? $"强化未成功：{name} 降为 {level}，消耗已扣除"
                : $"强化未成功：{name} 仍为 {level}，消耗已扣除";
        }
    }
}
