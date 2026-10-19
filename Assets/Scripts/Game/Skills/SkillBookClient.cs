using System;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using Ui = MmorpgClient.UI.Ugui.Skills;

namespace MmorpgClient.Game.Skills
{
    /// <summary>
    /// 角色技能书的业务适配器:把 SceneSkillClientPlayer 的 ListSkills / UpgradeSkill
    /// (服务端 docs/design/class-skills.md §4)接到技能 UI 的 <see cref="Ui.ISkillBookSource"/>。
    ///
    /// 契约(与 AttributeClient 同惯例):
    ///  - **服务器是唯一真相**:每次回包整份替换 <see cref="State"/>.Snapshot,本地不加等级、不扣银两;
    ///    修习回包自带处理后的完整技能书,拒绝时也带,UI 直接按它显示。
    ///  - 修习按 UI 提交的 expectedRank 原样发出;服务端拿它做乐观比较,重放旧请求不会再扣一次。
    ///  - 请求生命周期:同一时刻最多一个读或一个写在途;每个回包都按(会话代际、玩家、连接、请求号)校验,
    ///    换号 / 断线 / 连接替换之后到的旧回包一律丢弃,不覆盖新状态。
    ///  - 超时 / 网络失败不猜结果:修习结果未确认时写进 Error(UI 据此禁用修习、只留刷新),等刷新拿到权威状态。
    ///  - 断线:作废在途请求并清掉权威快照(<see cref="DisconnectedMessage"/>),重连后由 UI 再次 Refresh。
    ///  - 宠物技能:服务端还没有宠物技能协议(class-skills.md §9),宠物上下文只给只读空目录与说明,不伪造数据。
    ///  - <see cref="Changed"/> 只在调用方线程(UI)或传输回调(GameClient.Call,主线程)里触发。
    /// </summary>
    public sealed class SkillBookClient : Ui.ISkillBookSource, IDisposable
    {
        public static SkillBookClient Instance { get; private set; }

        public const string NotInGameMessage = "尚未进入游戏,技能信息未同步";
        public const string LoadingMessage = "正在同步技能信息…";
        public const string UpgradingMessage = "正在修习…";
        public const string EmptyCatalogMessage = "服务器没有下发本职业的技能目录";
        public const string DisconnectedMessage = "连接已断开,技能信息已过期,重连后请刷新";
        public const string PetUnsupportedMessage = "宠物技能服务端尚未开放,暂不能查看与修习";
        public const string InvalidCountMessage = "修习次数只能是 1 次或 10 次";

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<uint, Ui.SkillDefinition> _presentation;
        private object _connection;
        private ulong _player;
        private long _epoch, _serial, _activeRead, _activeWrite;
        private bool _disposed;

        private readonly struct RequestStamp
        {
            public readonly long Epoch, Id;
            public readonly ulong Player;
            public readonly object Connection;
            public RequestStamp(long epoch, long id, ulong player, object connection)
            { Epoch = epoch; Id = id; Player = player; Connection = connection; }
        }

        public event Action Changed;

        public Ui.SkillBookViewState State { get; private set; } = new Ui.SkillBookViewState
        {
            Context = Ui.SkillBookContext.Character(),
            Message = NotInGameMessage,
        };

        /// <param name="connectionIdentity">gate 连接身份(换连接即作废旧回包);缺省用传输对象本身。</param>
        /// <param name="presentation">本地图鉴的展示元数据查询;缺省 <see cref="Ui.SkillCatalog.Find"/>。</param>
        public SkillBookClient(IBattleTransport transport, Func<object> connectionIdentity = null,
            Func<uint, Ui.SkillDefinition> presentation = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? (() => transport);
            _presentation = presentation;
            _connection = _connectionIdentity();
            _player = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>生产入口:创建实例并登记为单例(GameClient 构造时调用)。</summary>
        public static SkillBookClient Attach(IBattleTransport transport, Func<object> connectionIdentity = null)
        {
            Instance?.Dispose();
            var client = new SkillBookClient(transport, connectionIdentity);
            Instance = client;
            return client;
        }

        // ── ISkillBookSource ───────────────────────────────

        public void Refresh(Ui.SkillBookContext context)
        {
            if (_disposed || context == null) return;
            ObserveSession();
            if (context.IsPet)
            {
                _activeRead = 0;
                Publish(PetState(context));
                return;
            }
            if (!Ready)
            {
                Publish(new Ui.SkillBookViewState { Context = context, Message = NotInGameMessage });
                return;
            }
            // 修习回包本身就是最新的完整技能书,等它回来;读请求在途时不叠第二个
            if (_activeWrite != 0 || _activeRead != 0) return;

            var stamp = Capture();
            _activeRead = stamp.Id;
            Publish(new Ui.SkillBookViewState
            {
                Context = context,
                Snapshot = SnapshotFor(context),
                IsLoading = true,
                IsConnected = true,
                Message = LoadingMessage,
            });
            _net.Call(MessageIds.ListSkills, new ListSkillsRequest(), ListSkillsResponse.Parser,
                response =>
                {
                    if (!CurrentRead(stamp)) return;
                    _activeRead = 0;
                    ApplyBook(context, response, null);
                },
                error =>
                {
                    if (!CurrentRead(stamp)) return;
                    _activeRead = 0;
                    PublishError(context, "刷新技能失败:" + error);
                });
        }

        public void Upgrade(Ui.SkillBookContext context, uint skillId, uint expectedRank, uint count)
        {
            if (_disposed || context == null) return;
            ObserveSession();
            if (context.IsPet)
            {
                Publish(PetState(context));
                return;
            }
            if (!Ready)
            {
                Publish(new Ui.SkillBookViewState { Context = context, Message = NotInGameMessage });
                return;
            }
            if (_activeWrite != 0 || _activeRead != 0) return;
            var current = State;
            if (current?.Snapshot == null || !context.Matches(current.Context)) return;  // 先刷新拿到权威技能书
            if (count != 1 && count != 10)
            {
                Publish(Copy(current, message: InvalidCountMessage));
                return;
            }

            var stamp = Capture();
            _activeWrite = stamp.Id;
            string skillName = current.Snapshot.Find(skillId)?.Definition?.name;
            Publish(new Ui.SkillBookViewState
            {
                Context = context,
                Snapshot = current.Snapshot,
                IsBusy = true,
                IsConnected = true,
                Message = UpgradingMessage,
            });
            // expectedRank 用 UI 提交时看到的值,不能换成回包后更新的等级(那样重放保护就失效了)
            var request = new UpgradeSkillRequest { SkillTableId = skillId, ExpectedRank = expectedRank, Count = count };
            _net.Call(MessageIds.UpgradeSkill, request, UpgradeSkillResponse.Parser,
                response => CompleteUpgrade(stamp, context, skillName, response),
                error =>
                {
                    if (!CurrentWrite(stamp)) return;
                    _activeWrite = 0;
                    if (State != null && !context.Matches(State.Context)) return;
                    // 超时 / 断流:服务端可能已经扣费修成,也可能没收到 —— 不猜,标成未确认,等刷新
                    PublishError(context, "修习结果未确认:" + error + ",请刷新查看最新状态");
                });
        }

        // ── 内部 ────────────────────────────────────────────

        private void CompleteUpgrade(RequestStamp stamp, Ui.SkillBookContext context, string skillName,
            UpgradeSkillResponse response)
        {
            if (!CurrentWrite(stamp)) return;
            _activeWrite = 0;
            // 回包到之前界面已经切去看宠物:不拿角色技能书盖掉当前显示,回到角色页时会重新拉取
            if (State != null && !context.Matches(State.Context)) return;
            if (response == null)
            {
                PublishError(context, "服务器未返回修习结果,请刷新");
                return;
            }
            bool rejected = HasTip(response.ErrorMessage);
            string message = rejected
                ? SkillBookMapper.DescribeTip("修习未成功", response.ErrorMessage)
                : SkillBookMapper.DescribeUpgrade(skillName, response.UpgradedCount, response.StopTipId);
            if (response.State != null && !HasTip(response.State.ErrorMessage))
            {
                // 拒绝也回了处理后的完整技能书:按它显示,原因放 Message(不是 Error),不挡下一次修习
                ApplyBook(context, response.State, message);
                return;
            }
            PublishError(context, rejected ? message : "服务器未返回最新技能书,请刷新");
        }

        private void ApplyBook(Ui.SkillBookContext context, ListSkillsResponse response, string message)
        {
            if (response == null)
            {
                PublishError(context, "服务器未返回技能信息");
                return;
            }
            if (HasTip(response.ErrorMessage))
            {
                PublishError(context, SkillBookMapper.DescribeTip("刷新技能失败", response.ErrorMessage));
                return;
            }
            var snapshot = SkillBookMapper.ToSnapshot(response, _presentation);
            Publish(new Ui.SkillBookViewState
            {
                Context = context,
                Snapshot = snapshot,
                IsConnected = true,
                Message = message ?? (snapshot.Entries.Count == 0 ? EmptyCatalogMessage : null),
            });
        }

        /// <summary>出错:保留上一份快照只作展示(UI 在 Error 非空时禁止修习、允许刷新)。</summary>
        private void PublishError(Ui.SkillBookContext context, string error)
        {
            Publish(new Ui.SkillBookViewState
            {
                Context = context,
                Snapshot = SnapshotFor(context),
                IsConnected = Ready,
                Error = error,
            });
        }

        private Ui.SkillBookViewState PetState(Ui.SkillBookContext context) => new Ui.SkillBookViewState
        {
            Context = context,
            Snapshot = new Ui.SkillBookSnapshot { Title = (context.DisplayName ?? "宠物") + "的技能" },
            IsConnected = Ready,
            IsReadOnly = true,
            Message = PetUnsupportedMessage,
        };

        private Ui.SkillBookSnapshot SnapshotFor(Ui.SkillBookContext context) =>
            State != null && context.Matches(State.Context) ? State.Snapshot : null;

        private static Ui.SkillBookViewState Copy(Ui.SkillBookViewState state, string message) => new Ui.SkillBookViewState
        {
            Context = state.Context,
            Snapshot = state.Snapshot,
            IsLoading = state.IsLoading,
            IsBusy = state.IsBusy,
            IsConnected = state.IsConnected,
            IsReadOnly = state.IsReadOnly,
            Message = message,
            Error = state.Error,
        };

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private bool Ready => !_disposed && _net.IsReady && _net.PlayerId != 0;

        private RequestStamp Capture() => new RequestStamp(_epoch, ++_serial, _player, _connection);

        /// <summary>换号或换连接:作废旧会话的全部在途请求与快照。</summary>
        private void ObserveSession()
        {
            if (_disposed) return;
            if (_player != _net.PlayerId || !ReferenceEquals(_connection, _connectionIdentity()))
                ResetSession(NotInGameMessage);
        }

        private bool Current(RequestStamp stamp)
        {
            if (_disposed) return false;
            ObserveSession();
            return _net.IsReady && stamp.Epoch == _epoch && stamp.Player != 0 && stamp.Player == _net.PlayerId
                && ReferenceEquals(stamp.Connection, _connectionIdentity());
        }

        private bool CurrentRead(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeRead;
        private bool CurrentWrite(RequestStamp stamp) => Current(stamp) && stamp.Id == _activeWrite;

        private void ResetSession(string message)
        {
            ++_epoch;
            _activeRead = _activeWrite = 0;
            _player = _net.PlayerId;
            _connection = _connectionIdentity();
            Publish(new Ui.SkillBookViewState
            {
                Context = State?.Context ?? Ui.SkillBookContext.Character(),
                Message = message,
            });
        }

        private void HandleDisconnected()
        {
            if (_disposed) return;
            ResetSession(DisconnectedMessage);
        }

        private void Publish(Ui.SkillBookViewState state)
        {
            State = state;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _net.Disconnected -= HandleDisconnected;
            ++_epoch;
            _activeRead = _activeWrite = 0;
            Changed = null;
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
