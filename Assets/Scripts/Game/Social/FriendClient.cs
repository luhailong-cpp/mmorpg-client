using System;
using System.Collections.Generic;
using Friendpb;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Social
{
    /// <summary>申请列表由服务端裁定；写操作只使用所见申请的 request_id，推送只触发补拉。</summary>
    public sealed class FriendClient : IDisposable
    {
        private readonly IBattleTransport _net;
        private readonly Func<object> _identity;
        private readonly Func<bool> _ready;
        private readonly List<FriendRequest> _pending = new();
        private object _connection;
        private ulong _player;
        private int _session, _operation;
        private bool _disposed, _refreshQueued, _hasLoaded;

        public IReadOnlyList<FriendRequest> Pending { get; }
        public bool Busy { get; private set; }
        public bool HasFreshPending { get; private set; }
        public ulong LastSubmittedRequestId { get; private set; }
        public string Status { get; private set; } = "请刷新好友申请。";
        public event Action Changed;

        /// <summary>
        /// 落地恢复的读取确认钩子(服务端 docs/design/landing-recovery-sync-digest.md §4.3,由 LandingSyncBindings 设置)。
        /// GetPendingRequests 真正发出时调用一次,返回的委托在这次申请列表成功应用后调用一次;null 表示不需要确认。
        /// 发出时取、成功后才调:只有发出晚于摘要到达的拉取才能推进缓存版本(L-2)。
        /// </summary>
        public Func<Action> SyncReadStarted { get; set; }

        /// <summary>
        /// 后台排队刷新申请列表(落地摘要标脏时调用)。在途时排到当前请求之后;空闲且可用时立即拉取;
        /// 不可用时什么也不做、也不写提示,等下次落地或玩家打开申请窗口。
        /// 摘要变化可能来自被动方(设计 §3.5 第 4 条),这里只拉取,红点只能来自拉回的申请列表。
        /// </summary>
        public void QueueRefresh()
        {
            ObserveConnection();
            if (_disposed) return;
            if (Busy) { _refreshQueued = true; return; }
            if (!_ready() || !_net.IsReady || _net.PlayerId == 0) return;
            RefreshPending();
        }

        public FriendClient(IBattleTransport net, Func<object> connectionIdentity = null, Func<bool> ready = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _identity = connectionIdentity ?? (() => net);
            _ready = ready ?? (() => _net.IsReady && _net.PlayerId != 0);
            _connection = _identity(); _player = _net.PlayerId;
            Pending = _pending.AsReadOnly();
            _net.Disconnected += Disconnected;
            // 传输接口没有按订阅者注销的能力，弱引用避免已销毁面板被 GameClient 长期保留。
            var weak = new WeakReference<FriendClient>(this);
            _net.RegisterNotify(ClientPlayerFriendNotifyFriendEventHandler.MessageId, content =>
            {
                if (weak.TryGetTarget(out var client)) client.Notify(content);
            });
        }

        public void ObserveConnection()
        {
            if (_disposed) return;
            object connection = _identity();
            if (!ReferenceEquals(connection, _connection) || _player != _net.PlayerId)
            {
                _connection = connection;
                Reset();
            }
        }

        public void Reset()
        {
            if (_disposed) return;
            ++_session; ++_operation;
            _player = _net.PlayerId;
            Busy = HasFreshPending = _refreshQueued = _hasLoaded = false;
            LastSubmittedRequestId = 0;
            _pending.Clear();
            Publish("会话已更新，请刷新好友申请。");
        }

        private void Disconnected()
        {
            _connection = _identity();
            Reset();
        }

        public void RefreshPending()
        {
            ObserveConnection();
            if (_disposed) return;
            if (Busy) { _refreshQueued = true; return; }
            if (!Ready()) return;
            HasFreshPending = false;
            var syncAck = SyncReadStarted?.Invoke();
            Call(ClientPlayerFriendGetPendingRequestsHandler.MessageId, new GetPendingRequestsRequest(),
                GetPendingRequestsResponse.Parser, response =>
                {
                    if (!Accept(response.ErrorMessage)) return;
                    var next = new List<FriendRequest>();
                    var seen = new HashSet<ulong>();
                    bool missingId = false;
                    foreach (var request in response.Requests)
                    {
                        if (request.FromPlayerId == 0 || request.FromPlayerId == _player || request.ToPlayerId != _player)
                            continue;
                        if (request.RequestId == 0) missingId = true;
                        else if (!seen.Add(request.RequestId)) continue;
                        next.Add(request.Clone());
                    }
                    _pending.Clear(); _pending.AddRange(next);
                    _hasLoaded = HasFreshPending = true;
                    syncAck?.Invoke();
                    Publish(missingId ? "部分申请缺少编号，请刷新后再处理。"
                        : next.Count == 0 ? "暂无待处理的好友申请。" : "好友申请已从服务器更新。");
                });
        }

        public bool AddFriend(ulong targetPlayerId)
        {
            ObserveConnection();
            if (targetPlayerId == 0 || targetPlayerId == _player)
            { Publish("请输入其他道友的玩家编号。"); return false; }
            if (!Ready()) return false;
            LastSubmittedRequestId = 0;
            Call(ClientPlayerFriendAddFriendHandler.MessageId,
                new AddFriendRequest { TargetPlayerId = targetPlayerId }, AddFriendResponse.Parser, response =>
                {
                    if (!Accept(response.ErrorMessage)) return;
                    if (response.RequestId == 0)
                    { Publish("服务器未返回申请编号，请刷新核对后重试。"); return; }
                    // 重复 pending 返回相同编号也属于成功，不生成第二个本地申请或关系。
                    LastSubmittedRequestId = response.RequestId;
                    Publish("好友申请已提交，等待对方处理。");
                });
            return true;
        }

        public bool AcceptFriend(ulong requestId) => Decide(requestId, true);
        public bool RejectFriend(ulong requestId) => Decide(requestId, false);

        private bool Decide(ulong requestId, bool accept)
        {
            ObserveConnection();
            if (!Ready()) return false;
            var request = _pending.Find(item => item.RequestId == requestId);
            if (requestId == 0 || request == null || !HasFreshPending)
            {
                Publish("这份申请已失效或缺少编号，正在刷新申请列表。");
                RefreshPending();
                return false;
            }
            ulong from = request.FromPlayerId;
            HasFreshPending = false;
            if (accept)
                Call(ClientPlayerFriendAcceptFriendHandler.MessageId,
                    new AcceptFriendRequest { RequestId = requestId, FromPlayerId = from }, AcceptFriendResponse.Parser,
                    response => DecisionCompleted(response.ErrorMessage));
            else
                Call(ClientPlayerFriendRejectFriendHandler.MessageId,
                    new RejectFriendRequest { RequestId = requestId, FromPlayerId = from }, RejectFriendResponse.Parser,
                    response => DecisionCompleted(response.ErrorMessage));
            return true;
        }

        private void DecisionCompleted(TipInfoMessage error)
        {
            if (!Accept(error)) return;
            // 不按 sender 删除：同一人可能已有新一代申请，始终重拉权威列表再展示。
            Publish("服务器已确认处理，正在刷新申请列表。");
            _refreshQueued = true;
        }

        private void Notify(MessageContent content)
        {
            ObserveConnection();
            if (_disposed || !_ready() || content == null) return;
            try { FriendEventS2C.Parser.ParseFrom(content.SerializedMessage); }
            catch (InvalidProtocolBufferException) { Publish("好友通知无法读取，请手动刷新申请。"); return; }
            // 推送不携带权威申请代次，不在本地新增、删除或接受申请。
            if (_hasLoaded) RefreshPending();
            else Publish("好友状态有更新，请刷新申请列表。");
        }

        private bool Ready()
        {
            if (_disposed || Busy) return false;
            if (!_ready() || !_net.IsReady || _net.PlayerId == 0)
            { Publish("请进入角色后处理好友申请。"); return false; }
            return true;
        }

        private bool Accept(TipInfoMessage error)
        {
            if (error == null || error.Id == 0) return true;
            Publish("好友服务未完成请求（" + error.Id + "），请刷新列表核对。");
            return false;
        }

        private void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success) where T : IMessage<T>
        {
            int session = _session, operation = ++_operation;
            ulong player = _player; object connection = _connection;
            Busy = true; Publish("正在同步好友申请…");
            _net.Call(id, request, parser, response =>
            {
                if (!Current(session, operation, player, connection)) return;
                Busy = false; success(response); Drain();
            }, error =>
            {
                if (!Current(session, operation, player, connection)) return;
                Busy = false; HasFreshPending = false; _refreshQueued = false;
                Publish("请求结果尚未确认，请刷新好友申请核对；不会自动重复处理。");
            });
        }

        private bool Current(int session, int operation, ulong player, object connection) =>
            !_disposed && session == _session && operation == _operation && _net.IsReady && _ready()
            && _net.PlayerId == player && ReferenceEquals(connection, _identity());

        private void Drain()
        {
            if (!_refreshQueued || Busy || _disposed) return;
            _refreshQueued = false;
            RefreshPending();
        }

        private void Publish(string message) { Status = message; Changed?.Invoke(); }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; ++_session; ++_operation;
            Busy = false; _refreshQueued = false;
            _net.Disconnected -= Disconnected;
        }
    }
}
