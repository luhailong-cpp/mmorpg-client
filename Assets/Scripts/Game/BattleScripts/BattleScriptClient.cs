using System;
using System.Globalization;
using Google.Protobuf;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.BattleScripts
{
    /// <summary>
    /// 战斗剧本(服务端 docs/design/battle-script.md §7):官方剧本与本人剧本的列表、导入提交、删除、按玩法选用。
    /// 剧本只管挂机 / 托管 / 超时时的出招,手动出手永远优先;本人上传的剧本要运营审核通过才能选用。
    ///
    /// 请求语义与 RankClient 同一套:同一时刻只允许一个请求(忙时的新操作直接提示,不排队);会话、连接、玩家与请求代次
    /// 共同拒绝旧回调。写请求(提交 / 删除 / 选用)超时 = 结局未知:置 <see cref="RequiresRefresh"/>,
    /// 刷新列表成功之前不再发写请求,免得在不知道上一笔落没落库时又改一次。写成功后自动刷新列表。
    /// 身份只取 gate 会话,请求里没有 player_id。
    /// </summary>
    public sealed class BattleScriptClient : IDisposable
    {
        public const string LoadingMessage = "正在处理，请稍候…";
        public const string NotInGameMessage = "请进入角色后再打开战斗剧本。";
        public const string RecoveryMessage = "上一次操作的结果尚未确认，请先刷新列表。";
        public const string TransportFailureMessage = "请求未完成，请稍后重试。";
        public const string SubmittedMessage = "已提交，等待审核。审核通过前，对局继续用上一份通过的版本。";
        public const string DeletedMessage = "剧本已删除，选用了它的玩法已一并清除。";
        public const string LoadoutSavedMessage = "选用已保存，下一局起生效。";
        public const string TimeoutError = "rpc timeout";
        public const string ServerTipPrefix = "server tip=";

        private enum Operation { None, List, Submit, Delete, Loadout }

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private object _observedConnection;
        private ulong _observedPlayer;
        private int _generation;
        private bool _disposed;
        private Operation _inFlight = Operation.None;
        private ListBattleScriptsResponse _list;

        public ulong PlayerId => _net.PlayerId;
        public bool Busy => _inFlight != Operation.None;
        /// <summary>上一笔写请求结局未知:刷新列表成功之前拒绝新的写请求。</summary>
        public bool RequiresRefresh { get; private set; }
        public string Status { get; private set; } = "";
        /// <summary>提交被拒为「剧本不合法」时服务端给的位置与原因(英文,如 "script 0 role 2 rule 10: unknown skill 4242")。</summary>
        public string InvalidDetail { get; private set; } = "";
        public ListBattleScriptsResponse List => _list?.Clone();
        public event Action Changed;

        public BattleScriptClient(IBattleTransport net, Func<object> connectionIdentity)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _observedConnection = _connectionIdentity();
            _observedPlayer = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>拉官方剧本、本人剧本与选用。结局未知的写请求也靠它确认。</summary>
        public void Refresh()
        {
            if (!BeginRequest(Operation.List, write: false, out int generation, out ulong player, out object connection)) return;
            _net.Call(MatchServiceRpc.ListBattleScriptsMessageId, new ListBattleScriptsRequest(), ListBattleScriptsResponse.Parser,
                response =>
                {
                    if (!CompleteCurrent(generation, player, connection)) return;
                    if (response.ErrorMessage != null && response.ErrorMessage.Id != 0)
                    {
                        Status = DescribeTip(response.ErrorMessage.Id);
                    }
                    else
                    {
                        _list = response.Clone();
                        RequiresRefresh = false;
                        if (Status == LoadingMessage || Status == RecoveryMessage) Status = "";
                    }
                    Changed?.Invoke();
                }, error => Fail(generation, player, connection, write: false, error));
        }

        /// <summary>
        /// 把一段 protobuf JSON 解析成剧本(玩家「导入剧本」用)。字段名、类型、枚举名都按协议检查,未知字段直接报错;
        /// 只做格式解析,规则是否合法以服务端校验为准(提交被拒时看 <see cref="InvalidDetail"/>)。
        /// </summary>
        public static bool TryParseScript(string json, out global::BattleScript script, out string error)
        {
            script = null;
            error = "";
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "请粘贴剧本内容。";
                return false;
            }
            try
            {
                script = global::BattleScript.Parser.ParseJson(json);
                return true;
            }
            catch (InvalidProtocolBufferException e)
            {
                error = "剧本格式不对：" + e.Message;
            }
            catch (InvalidJsonException e)
            {
                error = "剧本不是合法的 JSON：" + e.Message;
            }
            return false;
        }

        /// <summary>新建(scriptId = 0)或改写本人的剧本。改写要带当前版本号,别处改过会被拒。</summary>
        public void Submit(ulong scriptId, uint expectedRevision, global::BattleScript script)
        {
            if (script == null) { Show("请先导入剧本。"); return; }
            uint limit = _list?.MaxScriptBytes ?? 0;
            if (limit > 0 && script.CalculateSize() > limit)
            {
                Show(DescribeTip((uint)match_error.KMatchBattleScriptTooLarge));
                return;
            }
            if (!BeginRequest(Operation.Submit, write: true, out int generation, out ulong player, out object connection)) return;
            InvalidDetail = "";
            var request = new SubmitBattleScriptRequest { ScriptId = scriptId, ExpectedRevision = expectedRevision, Script = script.Clone() };
            _net.Call(MatchServiceRpc.SubmitBattleScriptMessageId, request, SubmitBattleScriptResponse.Parser,
                response =>
                {
                    if (!CompleteCurrent(generation, player, connection)) return;
                    if (response.ErrorMessage != null && response.ErrorMessage.Id != 0)
                    {
                        Status = DescribeTip(response.ErrorMessage.Id);
                        InvalidDetail = response.InvalidDetail ?? "";
                        Changed?.Invoke();
                        return;
                    }
                    AfterWrite(SubmittedMessage);
                }, error => Fail(generation, player, connection, write: true, error));
        }

        /// <summary>删除本人的剧本(带当前版本号)。</summary>
        public void Delete(ulong scriptId, uint expectedRevision)
        {
            if (scriptId == 0) return;
            if (!BeginRequest(Operation.Delete, write: true, out int generation, out ulong player, out object connection)) return;
            _net.Call(MatchServiceRpc.DeleteBattleScriptMessageId,
                new DeleteBattleScriptRequest { ScriptId = scriptId, ExpectedRevision = expectedRevision }, DeleteBattleScriptResponse.Parser,
                response =>
                {
                    if (!CompleteCurrent(generation, player, connection)) return;
                    if (response.ErrorMessage != null && response.ErrorMessage.Id != 0)
                    {
                        Status = DescribeTip(response.ErrorMessage.Id);
                        Changed?.Invoke();
                        return;
                    }
                    AfterWrite(DeletedMessage);
                }, error => Fail(generation, player, connection, write: true, error));
        }

        /// <summary>为某种对局选用官方剧本。matchMode / battleConfigId 为 0 表示任意。</summary>
        public void ChooseOfficial(uint matchMode, uint battleConfigId, uint officialScriptId)
            => SetLoadout(new BattleScriptLoadoutEntry
            {
                MatchMode = matchMode, BattleConfigId = battleConfigId,
                Origin = eBattleScriptOrigin.BattleScriptOriginOfficial, OfficialScriptId = officialScriptId,
            });

        /// <summary>为某种对局选用本人已通过审核的剧本(评分对局只能选官方剧本,服务端会拒)。</summary>
        public void ChooseOwn(uint matchMode, uint battleConfigId, ulong playerScriptId)
            => SetLoadout(new BattleScriptLoadoutEntry
            {
                MatchMode = matchMode, BattleConfigId = battleConfigId,
                Origin = eBattleScriptOrigin.BattleScriptOriginPlayer, PlayerScriptId = playerScriptId,
            });

        /// <summary>清除这种对局的选用(之后用官方绑定,没有绑定就不带剧本)。</summary>
        public void ClearChoice(uint matchMode, uint battleConfigId)
            => SetLoadout(new BattleScriptLoadoutEntry { MatchMode = matchMode, BattleConfigId = battleConfigId });

        private void SetLoadout(BattleScriptLoadoutEntry entry)
        {
            if (!BeginRequest(Operation.Loadout, write: true, out int generation, out ulong player, out object connection)) return;
            _net.Call(MatchServiceRpc.SetBattleScriptLoadoutMessageId, new SetBattleScriptLoadoutRequest { Entry = entry },
                SetBattleScriptLoadoutResponse.Parser,
                response =>
                {
                    if (!CompleteCurrent(generation, player, connection)) return;
                    if (response.ErrorMessage != null && response.ErrorMessage.Id != 0)
                    {
                        Status = DescribeTip(response.ErrorMessage.Id);
                        Changed?.Invoke();
                        return;
                    }
                    if (_list != null)
                    {
                        _list.Loadout.Clear();
                        _list.Loadout.Add(response.Loadout);
                    }
                    Status = LoadoutSavedMessage;
                    Changed?.Invoke();
                }, error => Fail(generation, player, connection, write: true, error));
        }

        /// <summary>某种对局当前的选用(精确匹配 matchMode / battleConfigId 的那一条);没有返回 null。</summary>
        public BattleScriptLoadoutEntry ChoiceFor(uint matchMode, uint battleConfigId)
        {
            if (_list == null) return null;
            foreach (var entry in _list.Loadout)
            {
                if (entry.MatchMode == matchMode && entry.BattleConfigId == battleConfigId) return entry.Clone();
            }
            return null;
        }

        private bool BeginRequest(Operation operation, bool write, out int generation, out ulong player, out object connection)
        {
            generation = 0;
            player = 0;
            connection = null;
            if (_disposed) return false;
            ObserveConnection();
            if (Busy) { Show(LoadingMessage); return false; }
            if (!_net.IsReady || PlayerId == 0) { Show(NotInGameMessage); return false; }
            if (write && RequiresRefresh) { Show(RecoveryMessage); return false; }
            generation = ++_generation;
            player = PlayerId;
            connection = _observedConnection;
            _inFlight = operation;
            Status = LoadingMessage;
            Changed?.Invoke();
            // Changed 订阅者可能退出角色或 Dispose,不能在其后再发一个已作废的请求。
            return !_disposed && generation == _generation && Busy && _net.IsReady && PlayerId == player
                   && ReferenceEquals(connection, _connectionIdentity());
        }

        private bool CompleteCurrent(int generation, ulong player, object connection)
        {
            if (_disposed || generation != _generation || !_net.IsReady || player == 0 || PlayerId != player
                || !ReferenceEquals(connection, _connectionIdentity())) return false;
            // 完成即消耗代次:重复的 success / error 回调不能再改同一请求的结果
            ++_generation;
            _inFlight = Operation.None;
            return true;
        }

        private void AfterWrite(string message)
        {
            Status = message;
            Changed?.Invoke();
            Refresh();
            // 刷新会把状态换成「正在处理」;写入结果的提示留着,列表回来时保留不清
            if (Busy && Status != message)
            {
                Status = message;
                Changed?.Invoke();
            }
        }

        private void Fail(int generation, ulong player, object connection, bool write, string error)
        {
            if (!CompleteCurrent(generation, player, connection)) return;
            uint tip = ParseEnvelopeTip(error);
            if (write && (error == TimeoutError || error == "timeout" || error == "disconnected"))
            {
                RequiresRefresh = true;
                Status = RecoveryMessage;
            }
            else
            {
                Status = tip == 0 ? TransportFailureMessage : DescribeTip(tip);
            }
            Changed?.Invoke();
        }

        public void Reset()
        {
            if (_disposed) return;
            if (Busy && _inFlight != Operation.List) RequiresRefresh = true;
            ++_generation;
            _inFlight = Operation.None;
            _list = null;
            InvalidDetail = "";
            Status = RequiresRefresh ? RecoveryMessage : "";
            Changed?.Invoke();
        }

        public void ObserveConnection()
        {
            if (_disposed) return;
            object connection = _connectionIdentity();
            if (ReferenceEquals(connection, _observedConnection) && PlayerId == _observedPlayer) return;
            _observedConnection = connection;
            _observedPlayer = PlayerId;
            _inFlight = Operation.None;
            RequiresRefresh = false;
            Reset();
        }

        private void HandleDisconnected()
        {
            _observedConnection = _connectionIdentity();
            _observedPlayer = PlayerId;
            Reset();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_generation;
            _inFlight = Operation.None;
            _list = null;
        }

        private void Show(string status) { Status = status; Changed?.Invoke(); }

        /// <summary>剧本的显示名:服务端不改名字,空名显示「未命名剧本」。</summary>
        public static string DisplayName(global::BattleScript script)
            => string.IsNullOrWhiteSpace(script?.Name) ? "未命名剧本" : script.Name;

        /// <summary>本人剧本的审核状态文字。</summary>
        public static string DescribeReview(PlayerBattleScriptView view)
        {
            if (view == null) return "";
            string current = view.ReviewState switch
            {
                BattleScriptReviewState.BattleScriptReviewPending => "审核中",
                BattleScriptReviewState.BattleScriptReviewApproved => "已通过",
                BattleScriptReviewState.BattleScriptReviewRejected => string.IsNullOrWhiteSpace(view.RejectReason)
                    ? "已驳回" : "已驳回：" + view.RejectReason,
                _ => "",
            };
            if (view.ApprovedRevision != 0 && view.ApprovedRevision != view.Revision)
                current += " · 生效第 " + view.ApprovedRevision.ToString(CultureInfo.InvariantCulture) + " 版";
            return current;
        }

        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)match_error.KMatchBattleScriptInvalid => "剧本内容不合法，请按提示修改后再提交。",
            (uint)match_error.KMatchBattleScriptNotFound => "剧本不存在或已删除。",
            (uint)match_error.KMatchBattleScriptLimitReached => "剧本数量已达上限，请先删除不用的剧本。",
            (uint)match_error.KMatchBattleScriptRevisionConflict => "剧本已在别处修改，请刷新后再试。",
            (uint)match_error.KMatchBattleScriptNotApproved => "剧本还没有通过审核，暂时不能选用。",
            (uint)match_error.KMatchBattleScriptTooLarge => "剧本太长，请精简规则或文字说明。",
            (uint)match_error.KMatchBattleScriptLoadoutInvalid => "这个玩法不能选用该剧本(评分对局只能用官方剧本)。",
            (uint)match_error.KMatchBattleScriptLoadoutFull => "选用的玩法条目已达上限，请先清除不用的条目。",
            (uint)match_error.KMatchInternal => "服务繁忙，请稍后重试。",
            (uint)common_error.KRateLimitExceeded => "操作太快，请稍后再试。",
            (uint)common_error.KServiceUnavailable => "战斗剧本暂时不可用，请稍后再试。",
            _ => "操作失败(" + tipId.ToString(CultureInfo.InvariantCulture) + ")。",
        };

        public static uint ParseEnvelopeTip(string error) => error != null
            && error.StartsWith(ServerTipPrefix, StringComparison.Ordinal)
            && uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out uint tip)
                ? tip : 0;
    }
}
