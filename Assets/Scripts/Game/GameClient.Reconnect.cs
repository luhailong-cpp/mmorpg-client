using System;
using System.Collections;
using System.Diagnostics;
using MmorpgClient.Net;

namespace MmorpgClient.Game
{
    public sealed partial class GameClient
    {
        // 断线保留只有 25 秒；恢复有 20 秒总期限，失败后交回现有选服流程。
        private const double ReconnectBudgetSeconds = 20;
        // 期限内只重试「还没连上新 gate」的暂时故障：gate 崩溃重启后，网关在旧注册过期前仍可能把玩家分到
        // 已停的 gate，连接被拒或验票前断线。退避 0.5s 起翻倍、封顶 4s、±20% 抖动，避免一批玩家同时重试。
        private const double ReconnectInitialBackoffSeconds = 0.5;
        private const double ReconnectMaxBackoffSeconds = 4;
        private readonly Random _reconnectJitter = new Random();
        // ConnectAndEnter 写：本次连接的 gate 已接受票据、进入登录阶段。之后的失败是登录 / 进场的服务端结论，不重试。
        private bool _pipelineTicketAccepted;
        private bool _reconnecting, _applicationPaused;
        private ulong _reconnectPlayerId;
        private double _reconnectDeadline;
        private string _deviceId;
        public bool IsReconnecting => _reconnecting;

        private static double MonotonicSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private bool TryStartReconnect()
        {
            // 顶号/显式登出走 Disconnect，不从这里自动抢回会话；切服自有独立的迁移管线。
            // 重连只凭 access token(分配 gate 与 gate 上绑定会话都用它),不看账号名:第三方登录的客户端不知道自己的账号名。
            if (_reconnecting || _redirecting || !InGame || PlayerId == 0 || CurrentZoneId == 0 ||
                string.IsNullOrEmpty(AccessToken) || CoroutineRunner == null)
                return false;
            uint zoneId = CurrentZoneId;
            ulong battleId = BattleLink?.BattleId ?? 0;
            _reconnectPlayerId = PlayerId;
            _reconnecting = true;
            _reconnectDeadline = MonotonicSeconds + ReconnectBudgetSeconds;
            int gen = ++_pipelineGen;
            ResetConnectionState();
            Status("连接中断，正在重新连接…");
            CoroutineRunner(ReconnectFlow(gen, zoneId, battleId));
            return true;
        }

        private IEnumerator ReconnectFlow(int gen, uint zoneId, ulong battleId)
        {
            if (gen != _pipelineGen) yield break;
            // 长时间挂起后令牌可能已到期。只用已有 refresh token，不保存或重放密码。
            if (_accessTokenExpire != 0 && _accessTokenExpire <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30 &&
                !string.IsNullOrEmpty(RefreshToken))
            {
                GatewayRefreshTokenResult refreshed = null;
                string refreshError = null;
                yield return _http.RefreshToken(RefreshToken, r => refreshed = r, e => refreshError = e);
                if (gen != _pipelineGen) yield break;
                if (refreshError != null || refreshed == null || refreshed.code != 0 || string.IsNullOrEmpty(refreshed.access_token))
                { FailReconnect(gen, "refresh failed"); yield break; }
                SetTokens(refreshed.access_token, refreshed.refresh_token, refreshed.access_token_expire);
            }

            double backoff = ReconnectInitialBackoffSeconds;
            for (int attempt = 1; ; ++attempt)
            {
                // 每次都重新分配并领新票据；旧 gate 的短期票据不能复用，gate 重启后也可能换地址。
                AssignGateResult assigned = null;
                string error = null;
                // 401(令牌被吊销)不在 IsTransientAssignGateCode 里:重试只会一直被拒,按失败结束,交玩家重新登录。
                yield return _http.AssignGate(BuildAssignGateRequest(zoneId, AccessToken, _deviceId), r => assigned = r, e => error = e);
                if (gen != _pipelineGen) yield break;

                bool retryable;
                if (error != null || assigned == null)
                {
                    retryable = true; // 网关暂时连不上
                    error ??= "assign gate: empty response";
                }
                else if (assigned.code != AssignGateResult.CodeOk)
                {
                    retryable = IsTransientAssignGateCode(assigned.code);
                    error = $"assign gate code={assigned.code}";
                }
                else
                {
                    bool entered = false;
                    string enterError = null;
                    yield return ConnectAndEnter(gen, zoneId, assigned.gate_ip, (int)assigned.gate_port,
                        SafeBase64(assigned.token_payload), SafeBase64(assigned.token_signature), null,
                        () =>
                        {
                            if (gen != _pipelineGen) return;
                            entered = true;
                            CancelReconnect();
                            Log($"[gate] reconnect complete player_id={PlayerId} attempts={attempt}");
                            if (battleId != 0) BattleLink?.HandleReconnectHint(battleId);
                        }, e => enterError = e);
                    if (gen != _pipelineGen || entered) yield break;
                    // 票据被拒、或新 gate 接受票据之后登录 / 进场被拒（令牌失效、被顶号、角色不能进）都是服务端结论，
                    // 重试只会和它对抗；只有连不上、验票前断线或超时才算暂时故障。
                    retryable = !_pipelineTicketAccepted && !_tokenRejectedByGate;
                    error = enterError ?? "connect and enter failed";
                }

                if (!retryable) { FailReconnect(gen, error); yield break; }
                double delay = backoff * (0.8 + 0.4 * _reconnectJitter.NextDouble());
                if (MonotonicSeconds + delay >= _reconnectDeadline)
                { FailReconnect(gen, $"{error}; no time left for attempt {attempt + 1}"); yield break; }
                Log($"[gate] reconnect attempt {attempt} failed, retrying in {delay:F1}s: {error}");
                double retryAt = MonotonicSeconds + delay;
                while (MonotonicSeconds < retryAt)
                {
                    yield return null;
                    if (gen != _pipelineGen) yield break; // 期限到、显式登出或新管线接管都会翻世代
                }
                backoff = Math.Min(backoff * 2, ReconnectMaxBackoffSeconds);
            }
        }

        // 网关的暂时性结论：限流、内部错误、区暂不可用（例如 gate 正在重启）。
        // 排队、区不存在、排队过期是确定结论：恢复不能替玩家排队，交回选服流程。
        private static bool IsTransientAssignGateCode(int code) =>
            code == AssignGateResult.CodeRateLimited || code == AssignGateResult.CodeInternal ||
            code == AssignGateResult.CodeZoneUnavailable;

        private void CheckReconnectDeadline()
        {
            if (_reconnecting && MonotonicSeconds >= _reconnectDeadline)
                FailReconnect(_pipelineGen, "reconnect deadline exceeded");
        }

        private void FailReconnect(int gen, string detail)
        {
            if (gen != _pipelineGen || !_reconnecting) return;
            CancelReconnect();
            ++_pipelineGen; // HTTP 迟到回包和嵌套等待不能在失败通知后重新接管连接。
            Log($"[gate] reconnect failed: {detail}");
            const string text = "重新连接失败，请重新进入游戏";
            Status(text);
            DisconnectInternal(notify: true, forceNotification: true, reason: text);
        }

        private void CancelReconnect()
        {
            _reconnecting = false;
            _reconnectPlayerId = 0;
        }

        /// <summary>由 Unity 两个宿主的 OnApplicationPause 转发；恢复时不沿用可能已被 gate 判死的连接。</summary>
        public void OnApplicationPause(bool paused)
        {
            bool resumed = _applicationPaused && !paused;
            _applicationPaused = paused;
            if (!resumed) return;
            // 先派发已收到的踢线/重定向，避免顶号后恢复前台又自动抢回账号。
            _gate?.Poll();
            if (_gate == null || _redirecting || _reconnecting) return;
            _gate.CloseForResume();
            HandleTransportDisconnected();
        }
    }
}
