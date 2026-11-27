using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
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
    ///  - 宠物技能按 pet_id 请求同一技能书接口，回包必须匹配当前查看的实例。
    ///  - <see cref="Changed"/> 只在调用方线程(UI)或传输回调(GameClient.CallMethod,主线程)里触发。
    /// </summary>
    public sealed class SkillBookClient : Ui.ISkillBookSource, Ui.ISkillLoadoutSource, IDisposable
    {
        public static SkillBookClient Instance { get; private set; }

        public const string NotInGameMessage = "尚未进入游戏,技能信息未同步";
        public const string LoadingMessage = "正在同步技能信息…";
        public const string UpgradingMessage = "正在修习…";
        public const string EmptyCatalogMessage = "服务器没有下发可学习的技能目录";
        public const string DisconnectedMessage = "连接已断开,技能信息已过期,重连后请刷新";
        public const string InvalidCountMessage = "修习次数只能是 1 次或 10 次";

        private readonly IBattleTransport _net;
        private readonly IRpcChannel _rpc;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<uint, Ui.SkillDefinition> _presentation;
        private readonly Func<bool> _writesBlocked;
        private object _connection;
        private ulong _player;
        private long _epoch, _serial, _activeRead, _activeWrite;
        private bool _disposed;
        private readonly Dictionary<uint, Ui.SkillDefinition> _battlePresentation = new();
        private readonly HashSet<ulong> _catalogSubjects = new();
        private readonly Dictionary<ulong, List<Action<bool>>> _catalogRequests = new();
        private SaveSkillLoadoutRequest _pendingSaveLoadout;
        private SwitchSkillLoadoutRequest _pendingSwitchLoadout;
        private RentSkillLoadoutSlotsRequest _pendingRentLoadout;
        private ulong _rentRequestSeed = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 16;

        public Ui.SkillDefinition FindBattlePresentation(uint id) =>
            _battlePresentation.TryGetValue(id, out var definition) ? definition : null;

        /// <summary>战斗打开时预取权威目录，不切换或覆盖正在查看的技能书。</summary>
        public void EnsureBattleCatalog(ulong petId, Action<bool> completed)
        {
            ObserveSession();
            if (!Ready) { completed?.Invoke(false); return; }
            if (_catalogSubjects.Contains(petId)) { completed?.Invoke(true); return; }
            if (_catalogRequests.TryGetValue(petId, out var callbacks))
            { callbacks.Add(completed); return; }
            callbacks = new List<Action<bool>> { completed };
            _catalogRequests.Add(petId, callbacks);
            var stamp = Capture();
            SceneSkillClientPlayerRpc.ListSkills(_rpc, new ListSkillsRequest { PetId = petId }, (status, response) =>
            {
                if (!Current(stamp)) return;
                _catalogRequests.Remove(petId);
                bool success = status.IsOk && response != null && response.PetId == petId && !HasTip(response.ErrorMessage);
                if (success) CachePresentation(response, SkillBookMapper.ToSnapshot(response, _presentation));
                foreach (var callback in callbacks) callback?.Invoke(success);
            });
        }

        private void CachePresentation(ListSkillsResponse response, Ui.SkillBookSnapshot snapshot)
        {
            foreach (var entry in snapshot.Entries)
                if (entry.Definition != null) _battlePresentation[entry.Id] = entry.Definition;
            _catalogSubjects.Add(response.PetId);
        }

        private readonly struct RequestStamp
        {
            public readonly long Epoch, Id;
            public readonly ulong Player;
            public readonly object Connection;
            public RequestStamp(long epoch, long id, ulong player, object connection)
            { Epoch = epoch; Id = id; Player = player; Connection = connection; }
        }

        public event Action Changed;
        public event Action<RentSkillLoadoutSlotsResponse> LoadoutSlotsRented;

        public Ui.SkillBookViewState State { get; private set; } = new Ui.SkillBookViewState
        {
            Context = Ui.SkillBookContext.Character(),
            Message = NotInGameMessage,
        };

        /// <param name="transport">只消费当前角色、就绪状态和断线事件。</param>
        /// <param name="rpc">类型化技能 RPC 的调用通道,生产环境为同一个 GameClient。</param>
        /// <param name="connectionIdentity">gate 连接身份(换连接即作废旧回包);缺省用传输对象本身。</param>
        /// <param name="presentation">本地图鉴的展示元数据查询;缺省 <see cref="Ui.SkillCatalog.Find"/>。</param>
        public SkillBookClient(IBattleTransport transport, IRpcChannel rpc, Func<object> connectionIdentity = null,
            Func<uint, Ui.SkillDefinition> presentation = null, Func<bool> writesBlocked = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _connectionIdentity = connectionIdentity ?? (() => transport);
            _presentation = presentation;
            _writesBlocked = writesBlocked;
            _connection = _connectionIdentity();
            _player = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>生产入口:创建实例并登记为单例(GameClient 构造时调用)。</summary>
        public static SkillBookClient Attach(IBattleTransport transport, IRpcChannel rpc, Func<object> connectionIdentity = null,
            Func<bool> writesBlocked = null)
        {
            Instance?.Dispose();
            var client = new SkillBookClient(transport, rpc, connectionIdentity, writesBlocked: writesBlocked);
            Instance = client;
            return client;
        }

        // ── ISkillBookSource ───────────────────────────────

        public void Refresh(Ui.SkillBookContext context)
        {
            if (_disposed || context == null) return;
            ObserveSession();
            if (!Ready)
            {
                Publish(new Ui.SkillBookViewState { Context = context, Message = NotInGameMessage });
                return;
            }
            // 修习回包本身就是最新的完整技能书,等它回来;读请求在途时不叠第二个
            if (State != null && !context.Matches(State.Context)) _activeRead = 0;
            if (_activeWrite != 0 && context.Matches(State?.Context) || _activeRead != 0) return;

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
            SceneSkillClientPlayerRpc.ListSkills(_rpc, new ListSkillsRequest { PetId = context.IsPet ? context.PetId : 0 },
                (status, response) =>
                {
                    if (!CurrentRead(stamp)) return;
                    _activeRead = 0;
                    if (status.IsOk) ApplyBook(context, response, null);
                    else PublishError(context, "刷新技能失败:" + DescribeFailure(status));
                });
        }

        public void Upgrade(Ui.SkillBookContext context, uint skillId, uint expectedRank, uint count)
        {
            if (_disposed || context == null) return;
            ObserveSession();
            if (!Ready)
            {
                Publish(new Ui.SkillBookViewState { Context = context, Message = NotInGameMessage });
                return;
            }
            if (_activeWrite != 0 || _activeRead != 0) return;
            var current = State;
            if (current?.Snapshot == null || !context.Matches(current.Context)) return;  // 先刷新拿到权威技能书
            if (_writesBlocked?.Invoke() == true)
            {
                Publish(Copy(current, message: "战斗中仅可查看，技能修习已锁定。"));
                return;
            }
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
            var request = new UpgradeSkillRequest { SkillTableId = skillId, ExpectedRank = expectedRank, Count = count,
                PetId = context.IsPet ? context.PetId : 0 };
            SceneSkillClientPlayerRpc.UpgradeSkill(_rpc, request,
                (status, response) =>
                {
                    if (status.IsOk)
                    {
                        CompleteUpgrade(stamp, context, skillName, response);
                        return;
                    }
                    if (!CurrentWrite(stamp)) return;
                    _activeWrite = 0;
                    if (State != null && !context.Matches(State.Context)) return;
                    // 超时 / 断流:服务端可能已经扣费修成,也可能没收到 —— 不猜,标成未确认,等刷新
                    string prefix = status.Code == RpcStatusCode.NotSent ? "修习未发送:" : "修习结果未确认:";
                    PublishError(context, prefix + DescribeFailure(status) + ",请刷新查看最新状态");
                });
        }

        // ── ISkillLoadoutSource ────────────────────────────

        public void SaveLoadout(uint schemeId, uint roleTag, IReadOnlyList<uint> passiveSkillIds,
            IReadOnlyList<uint> pursuitSkillIds, IReadOnlyList<uint> openingSkillIds,
            Action<bool, string> completed = null)
        {
            SaveLoadout(new Ui.SkillLoadoutSaveArgs
            {
                SchemeId = schemeId,
                RoleTag = roleTag,
                PassiveSkillIds = passiveSkillIds,
                PursuitSkillIds = pursuitSkillIds,
                OpeningSkillIds = openingSkillIds,
            }, completed);
        }

        public void SaveLoadout(Ui.SkillLoadoutSaveArgs args, Action<bool, string> completed = null)
        {
            if (args == null) { completed?.Invoke(false, "方案参数为空"); return; }
            if (_disposed) { completed?.Invoke(false, "技能服务已销毁"); return; }
            ObserveSession();
            var context = Ui.SkillBookContext.Character();
            if (!Ready) { completed?.Invoke(false, NotInGameMessage); return; }
            if (_activeWrite != 0 || _activeRead != 0) { completed?.Invoke(false, "上一次操作还在处理中"); return; }
            var current = State;
            if (current?.Snapshot?.Loadout == null || !context.Matches(current.Context))
            {
                completed?.Invoke(false, "请先刷新角色技能书");
                return;
            }
            if (_writesBlocked?.Invoke() == true)
            {
                const string locked = "战斗中仅可查看，无法修改战法装配。";
                Publish(Copy(current, message: locked));
                completed?.Invoke(false, locked);
                return;
            }
            if (_pendingSwitchLoadout != null || _pendingRentLoadout != null)
            {
                completed?.Invoke(false, "请先确认未完成的方案切换或槽位租用请求。");
                return;
            }
            string name = args.Name ?? string.Empty;
            var passive = args.PassiveSkillIds ?? Array.Empty<uint>();
            var pursuit = args.PursuitSkillIds ?? Array.Empty<uint>();
            var opening = args.OpeningSkillIds ?? Array.Empty<uint>();
            if (_pendingSaveLoadout == null)
            {
                var req = new SaveSkillLoadoutRequest
                {
                    SchemeId = args.SchemeId,
                    Name = name,
                    RoleTag = args.RoleTag,
                    StateVersion = current.Snapshot.Loadout.StateVersion,
                    RequestId = Guid.NewGuid().ToString("N"),
                    UpdateAttributeAllocation = args.UpdateAttributeAllocation,
                    ResetAttributeIfNeeded = args.ResetAttributeIfNeeded,
                };
                req.PassiveSkillIds.AddRange(passive);
                req.PursuitSkillIds.AddRange(pursuit);
                req.OpeningSkillIds.AddRange(opening);
                if (args.UpdateAttributeAllocation && args.AttributeAllocated != null)
                {
                    foreach (var kv in args.AttributeAllocated)
                        req.AttributeAllocated[kv.Key] = kv.Value;
                }
                _pendingSaveLoadout = req;
            }
            else if (!MatchesPendingSave(_pendingSaveLoadout, args.SchemeId, name, args.RoleTag,
                passive, pursuit, opening, args.UpdateAttributeAllocation, args.AttributeAllocated, args.ResetAttributeIfNeeded))
            {
                completed?.Invoke(false, "上一次战法装配保存尚未确认，请按原方案重试或刷新查看。");
                return;
            }
            var sent = _pendingSaveLoadout.Clone();
            var stamp = Capture();
            _activeWrite = stamp.Id;
            Publish(new Ui.SkillBookViewState
            {
                Context = context,
                Snapshot = current.Snapshot,
                IsBusy = true,
                IsConnected = true,
                Message = "正在保存战法与加点方案…",
            });
            SceneSkillClientPlayerRpc.SaveSkillLoadout(_rpc, sent, (status, response) =>
            {
                if (!CurrentWrite(stamp)) return;
                _activeWrite = 0;
                if (!status.IsOk || response == null)
                {
                    string err = "战法装配结果未确认:" + DescribeFailure(status) + ",可重试或刷新查看";
                    if (context.Matches(State?.Context)) PublishError(context, err);
                    completed?.Invoke(false, err);
                    return;
                }
                bool rejected = HasTip(response.ErrorMessage);
                bool exact = !rejected && response.RequestId == sent.RequestId;
                if (rejected || exact) _pendingSaveLoadout = null;
                string message = rejected
                    ? SkillBookMapper.DescribeTip("保存流派方案失败", response.ErrorMessage)
                    : !string.IsNullOrEmpty(response.AttributeSyncNotice)
                        ? response.AttributeSyncNotice
                        : $"已保存并启用流派方案 {sent.SchemeId}";
                if (!context.Matches(State?.Context))
                {
                    completed?.Invoke(exact, exact ? (string.IsNullOrEmpty(response.AttributeSyncNotice) ? null : response.AttributeSyncNotice) : message);
                    return;
                }
                if (response.State != null && !HasTip(response.State.ErrorMessage))
                {
                    ApplyBook(context, response.State, message);
                }
                else
                {
                    PublishError(context, rejected ? message : "服务器未返回最新战法装配状态,请刷新");
                }
                completed?.Invoke(exact, exact ? (string.IsNullOrEmpty(response.AttributeSyncNotice) ? null : response.AttributeSyncNotice) : message);
            });
        }

        public void SwitchLoadout(uint schemeId, Action<bool, string> completed = null) =>
            SwitchLoadout(schemeId, false, completed);

        public void SwitchLoadout(uint schemeId, bool skipAttributeSync, Action<bool, string> completed = null)
        {
            if (_disposed) { completed?.Invoke(false, "技能服务已销毁"); return; }
            ObserveSession();
            var context = Ui.SkillBookContext.Character();
            if (!Ready) { completed?.Invoke(false, NotInGameMessage); return; }
            if (_activeWrite != 0 || _activeRead != 0) { completed?.Invoke(false, "上一次操作还在处理中"); return; }
            var current = State;
            if (current?.Snapshot?.Loadout == null || !context.Matches(current.Context))
            {
                completed?.Invoke(false, "请先刷新角色技能书");
                return;
            }
            if (_writesBlocked?.Invoke() == true)
            {
                const string locked = "战斗中仅可查看，无法切换战法流派。";
                Publish(Copy(current, message: locked));
                completed?.Invoke(false, locked);
                return;
            }
            if (_pendingSaveLoadout != null || _pendingRentLoadout != null)
            {
                completed?.Invoke(false, "请先确认未完成的战法装配保存或槽位租用请求。");
                return;
            }
            if (_pendingSwitchLoadout == null)
            {
                _pendingSwitchLoadout = new SwitchSkillLoadoutRequest
                {
                    SchemeId = schemeId,
                    StateVersion = current.Snapshot.Loadout.StateVersion,
                    RequestId = Guid.NewGuid().ToString("N"),
                    SkipAttributeSync = skipAttributeSync,
                };
            }
            else if (_pendingSwitchLoadout.SchemeId != schemeId || _pendingSwitchLoadout.SkipAttributeSync != skipAttributeSync)
            {
                completed?.Invoke(false, "上一次方案切换尚未确认，请按原方案重试或刷新查看。");
                return;
            }
            var sent = _pendingSwitchLoadout.Clone();
            var stamp = Capture();
            _activeWrite = stamp.Id;
            Publish(new Ui.SkillBookViewState
            {
                Context = context,
                Snapshot = current.Snapshot,
                IsBusy = true,
                IsConnected = true,
                Message = "正在切换流派方案…",
            });
            SceneSkillClientPlayerRpc.SwitchSkillLoadout(_rpc, sent, (status, response) =>
            {
                if (!CurrentWrite(stamp)) return;
                _activeWrite = 0;
                if (!status.IsOk || response == null)
                {
                    string err = "切换战法流派结果未确认:" + DescribeFailure(status) + ",可重试或刷新查看";
                    if (context.Matches(State?.Context)) PublishError(context, err);
                    completed?.Invoke(false, err);
                    return;
                }
                bool rejected = HasTip(response.ErrorMessage);
                bool exact = !rejected && response.RequestId == sent.RequestId;
                if (rejected || exact) _pendingSwitchLoadout = null;
                string message = rejected
                    ? SkillBookMapper.DescribeTip("切换战法流派失败", response.ErrorMessage)
                    : !string.IsNullOrEmpty(response.AttributeSyncNotice)
                        ? response.AttributeSyncNotice
                        : $"已切换至流派方案 {sent.SchemeId}";
                if (!context.Matches(State?.Context))
                {
                    completed?.Invoke(exact, exact ? (string.IsNullOrEmpty(response.AttributeSyncNotice) ? null : response.AttributeSyncNotice) : message);
                    return;
                }
                if (response.State != null && !HasTip(response.State.ErrorMessage))
                {
                    ApplyBook(context, response.State, message);
                }
                else
                {
                    PublishError(context, rejected ? message : "服务器未返回最新战法装配状态,请刷新");
                }
                completed?.Invoke(exact, exact ? (string.IsNullOrEmpty(response.AttributeSyncNotice) ? null : response.AttributeSyncNotice) : message);
            });
        }

        public void RentLoadoutSlots(uint targetRentedSlots, uint addPeriods, ulong expectedCostDiamond,
            Action<bool, string> completed = null)
        {
            if (_disposed) { completed?.Invoke(false, "技能服务已销毁"); return; }
            ObserveSession();
            var context = Ui.SkillBookContext.Character();
            if (!Ready) { completed?.Invoke(false, NotInGameMessage); return; }
            if (_activeWrite != 0 || _activeRead != 0) { completed?.Invoke(false, "上一次操作还在处理中"); return; }
            var current = State;
            bool syncSkillView = current?.Snapshot != null && context.Matches(current.Context);
            if (_writesBlocked?.Invoke() == true)
            {
                const string locked = "战斗中仅可查看，无法开通或续期方案槽位。";
                if (syncSkillView) Publish(Copy(current, message: locked));
                completed?.Invoke(false, locked);
                return;
            }
            if (_pendingSaveLoadout != null || _pendingSwitchLoadout != null)
            {
                completed?.Invoke(false, "请先确认未完成的战法方案操作。");
                return;
            }
            if (_pendingRentLoadout == null)
            {
                ulong reqId = ++_rentRequestSeed;
                if (reqId == 0UL) reqId = ++_rentRequestSeed;
                _pendingRentLoadout = new RentSkillLoadoutSlotsRequest
                {
                    ClientRequestId = reqId,
                    TargetRentedSlots = targetRentedSlots,
                    AddPeriods = addPeriods,
                    ExpectedCostDiamond = expectedCostDiamond,
                };
            }
            else if (_pendingRentLoadout.TargetRentedSlots != targetRentedSlots
                || _pendingRentLoadout.AddPeriods != addPeriods
                || _pendingRentLoadout.ExpectedCostDiamond != expectedCostDiamond)
            {
                completed?.Invoke(false, "上一次槽位租用请求尚未确认，请按原档位重试或刷新查看。");
                return;
            }
            var sent = _pendingRentLoadout.Clone();
            var stamp = Capture();
            _activeWrite = stamp.Id;
            if (syncSkillView)
            {
                Publish(new Ui.SkillBookViewState
                {
                    Context = context,
                    Snapshot = current.Snapshot,
                    IsBusy = true,
                    IsConnected = true,
                    Message = "正在开通或续期方案槽位…",
                });
            }
            SceneSkillClientPlayerRpc.RentSkillLoadoutSlots(_rpc, sent, (status, response) =>
            {
                if (!CurrentWrite(stamp)) return;
                _activeWrite = 0;
                if (!status.IsOk || response == null)
                {
                    string err = "方案槽位租用结果未确认:" + DescribeFailure(status) + ",可按原档位安全重试或刷新查看";
                    if (context.Matches(State?.Context)) PublishError(context, err);
                    completed?.Invoke(false, err);
                    return;
                }
                bool rejected = HasTip(response.ErrorMessage);
                bool exact = !rejected && response.ClientRequestId == sent.ClientRequestId;
                if (rejected || exact) _pendingRentLoadout = null;
                string message = rejected
                    ? SkillBookMapper.DescribeTip("开通方案槽位失败", response.ErrorMessage)
                    : $"流派席位已生效（装备+战法+加点共享）：额外 +{response.RentedExtraSlots} 套（消耗 {response.SpentDiamond} 灵石）";
                if (context.Matches(State?.Context))
                {
                    if (response.State != null && !HasTip(response.State.ErrorMessage))
                    {
                        ApplyBook(context, response.State, message);
                    }
                    else if (syncSkillView)
                    {
                        PublishError(context, rejected ? message : "服务器未返回最新方案槽位状态,请刷新");
                    }
                }
                if (exact) LoadoutSlotsRented?.Invoke(response);
                completed?.Invoke(exact, message);
            });
        }

        private static bool MatchesPendingSave(SaveSkillLoadoutRequest pending, uint schemeId, string name, uint roleTag,
            IReadOnlyList<uint> passive, IReadOnlyList<uint> pursuit, IReadOnlyList<uint> opening,
            bool updateAttr, IReadOnlyDictionary<uint, uint> attr, bool resetAttr)
        {
            if (pending.SchemeId != schemeId || pending.Name != name || pending.RoleTag != roleTag
                || pending.UpdateAttributeAllocation != updateAttr || pending.ResetAttributeIfNeeded != resetAttr
                || !pending.PassiveSkillIds.SequenceEqual(passive)
                || !pending.PursuitSkillIds.SequenceEqual(pursuit)
                || !pending.OpeningSkillIds.SequenceEqual(opening))
                return false;
            if (!updateAttr) return true;
            int count = attr?.Count ?? 0;
            if (pending.AttributeAllocated.Count != count) return false;
            if (attr != null)
            {
                foreach (var kv in attr)
                    if (!pending.AttributeAllocated.TryGetValue(kv.Key, out uint existing) || existing != kv.Value)
                        return false;
            }
            return true;
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
            if (response.PetId != (context.IsPet ? context.PetId : 0))
            {
                PublishError(context, "服务器返回的技能归属不匹配，请刷新");
                return;
            }
            var snapshot = SkillBookMapper.ToSnapshot(response, _presentation);
            if (!context.IsPet && !string.IsNullOrEmpty(snapshot.Loadout?.LastRequestId))
            {
                string requestId = snapshot.Loadout.LastRequestId;
                if (_pendingSaveLoadout?.RequestId == requestId) _pendingSaveLoadout = null;
                if (_pendingSwitchLoadout?.RequestId == requestId) _pendingSwitchLoadout = null;
            }
            CachePresentation(response, snapshot);
            if (context.IsPet) snapshot.Title = (context.DisplayName ?? "宝宝") + "的技能";
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

        private static string DescribeFailure(RpcStatus status) => status.Code switch
        {
            RpcStatusCode.NotSent => "连接尚未就绪",
            RpcStatusCode.Disconnected => "连接已断开",
            RpcStatusCode.Timeout => "请求超时",
            RpcStatusCode.ParseError => "服务器响应无法读取",
            RpcStatusCode.ServerTip => SkillBookMapper.DescribeTip(status.TipId),
            _ => "请求失败",
        };

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
            _pendingSaveLoadout = null;
            _pendingSwitchLoadout = null;
            _pendingRentLoadout = null;
            _battlePresentation.Clear();
            _catalogSubjects.Clear();
            _catalogRequests.Clear();
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
