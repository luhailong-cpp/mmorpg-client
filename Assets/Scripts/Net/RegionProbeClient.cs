using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Google.Protobuf;
using MmorpgClient.Game.Battle;

namespace MmorpgClient.Net
{
    /// <summary>复用battle公网TCP面的短探测；只回显随机挑战，不提供客户端自行填写的延迟。</summary>
    public sealed class RegionProbeClient : IDisposable
    {
        private readonly IBattleTransport _net;
        private readonly Func<IFramedConnection> _factory;
        private readonly Action<Action> _dispatch;
        private readonly ConcurrentQueue<Action> _completed = new();
        private readonly List<Match.SignedNetworkProbe> _receipts = new();
        private IFramedConnection _connection;
        private Match.GetNetworkProbePlanResponse _plan;
        private Action<string> _success;
        private Action<string> _failure;
        private int _generation;
        private int _target;
        private double _now;
        private double _deadline;
        private double _validUntil;
        private string _cachedPlan;
        public bool Busy => _success != null;

        public RegionProbeClient(IBattleTransport net, Func<IFramedConnection> factory, Action<Action> dispatch = null)
        { _net = net ?? throw new ArgumentNullException(nameof(net)); _factory = factory ?? throw new ArgumentNullException(nameof(factory)); _dispatch = dispatch ?? (work => ThreadPool.QueueUserWorkItem(_ => work())); }

        public void Prepare(Action<string> success, Action<string> failure)
        {
            if (Busy) { _success += success; _failure += failure; return; }
            if (_cachedPlan != null && _now < _validUntil) { success?.Invoke(_cachedPlan); return; }
            _success = success; _failure = failure;
            int generation = ++_generation;
            _deadline = _now + 65;
            _net.Call(MessageIds.GetNetworkProbePlan, new Match.GetNetworkProbePlanRequest(), Match.GetNetworkProbePlanResponse.Parser,
                plan =>
                {
                    if (generation != _generation) return;
                    if (plan.ErrorMessage != null && plan.ErrorMessage.Id != 0) { Fail("地域探测暂不可用，请重试。"); return; }
                    if (!plan.Required) { Succeed(string.Empty); return; }
                    if (plan.PlanId.Length == 0 || plan.Targets.Count == 0 || plan.Targets.Count > 16) { Fail("地域探测计划无效。"); return; }
                    _plan = plan; _receipts.Clear(); _target = 0; NextTarget();
                }, error => { if (generation == _generation) Fail("获取地域探测计划失败。"); });
        }

        public void Tick(double now)
        {
            _now = now;
            while (_completed.TryDequeue(out var work)) work();
            _connection?.Poll();
            if (!Busy || _now < _deadline) return;
            if (_connection != null) NextTarget(); else Fail("地域探测超时，请重试。");
        }

        private void NextTarget()
        {
            CloseConnection();
            if (_target >= _plan.Targets.Count)
            {
                if (_receipts.Count == 0) { Fail("无法连接任何战斗机房，请检查网络后重试。"); return; }
                int generation = _generation;
                var request = new Match.CommitNetworkProbeRequest { PlanId = _plan.PlanId };
                request.Receipts.Add(_receipts); _deadline = _now + 15;
                _net.Call(MessageIds.CommitNetworkProbe, request, Match.CommitNetworkProbeResponse.Parser,
                    response =>
                    {
                        if (generation != _generation) return;
                        if (response.ErrorMessage != null && response.ErrorMessage.Id != 0) { Fail("地域探测结果已经失效，请重试。"); return; }
                        _cachedPlan = _plan.PlanId; _validUntil = _now + 30; Succeed(_cachedPlan);
                    }, error => { if (generation == _generation) Fail("提交地域探测失败，请重试。"); });
                return;
            }
            var target = _plan.Targets[_target++];
            if (target.Host.Length == 0 || target.Port == 0 || target.Port > 65535 || target.Grant == null) { NextTarget(); return; }
            var connection = _factory(); _connection = connection;
            int generationAtConnect = _generation;
            connection.OnMessage += message =>
            {
                if (_connection != connection || generationAtConnect != _generation) return;
                if (message is Match.NetworkProbeChallenge challenge)
                {
                    if (challenge.Nonce.Length != 32 || challenge.SampleIndex >= 3) { NextTarget(); return; }
                    try { connection.Send(new Match.NetworkProbeEcho { Nonce = challenge.Nonce, SampleIndex = challenge.SampleIndex }); }
                    catch { NextTarget(); }
                }
                else if (message is Match.NetworkProbeComplete complete)
                {
                    if (complete.Receipt != null && complete.Receipt.Payload.Length > 0 && complete.Receipt.Signature.Length == 32)
                        _receipts.Add(complete.Receipt.Clone());
                    NextTarget();
                }
            };
            connection.OnDisconnected += () => { if (_connection == connection) NextTarget(); };
            _deadline = _now + 3;
            _dispatch(() =>
            {
                Exception failure = null;
                try { connection.Connect(target.Host, (int)target.Port); } catch (Exception error) { failure = error; }
                _completed.Enqueue(() =>
                {
                    if (generationAtConnect != _generation || _connection != connection) { connection.Dispose(); return; }
                    if (failure != null) { NextTarget(); return; }
                    try { connection.Send(new Match.NetworkProbeStart { Grant = target.Grant }); } catch { NextTarget(); }
                });
            });
        }

        private void Succeed(string plan) { var callback = _success; _success = null; _failure = null; callback?.Invoke(plan); }
        private void Fail(string error) { CloseConnection(); ++_generation; var callback = _failure; _success = null; _failure = null; callback?.Invoke(error); }
        private void CloseConnection() { var old = _connection; _connection = null; old?.Dispose(); }
        public void Dispose() { ++_generation; CloseConnection(); _success = null; _failure = null; _cachedPlan = null; _validUntil = 0; }
    }
}
