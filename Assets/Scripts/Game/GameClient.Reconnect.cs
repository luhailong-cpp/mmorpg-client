using System;
using System.Collections;
using System.Diagnostics;
using MmorpgClient.Net;

namespace MmorpgClient.Game
{
    public sealed partial class GameClient
    {
        // 断线保留只有 25 秒；只做一次有总期限的恢复，失败后交回现有选服流程。
        private const double ReconnectBudgetSeconds = 20;
        private bool _reconnecting, _applicationPaused;
        private ulong _reconnectPlayerId;
        private double _reconnectDeadline;
        private string _deviceId;
        public bool IsReconnecting => _reconnecting;

        private static double MonotonicSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private bool TryStartReconnect()
        {
            // 顶号/显式登出走 Disconnect，不从这里自动抢回会话；切服自有独立的迁移管线。
            if (_reconnecting || _redirecting || !InGame || PlayerId == 0 || CurrentZoneId == 0 ||
                string.IsNullOrEmpty(Account) || string.IsNullOrEmpty(AccessToken) || CoroutineRunner == null)
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

            // 重新分配并领新票据；旧 gate 的短期票据不能复用，gate 重启后也可能换地址。
            AssignGateResult assigned = null;
            string error = null;
            yield return _http.AssignGate(new Net.AssignGateRequest
            {
                zone_id = zoneId, account = Account, device_id = _deviceId ?? "", queue_token = ""
            }, r => assigned = r, e => error = e);
            if (gen != _pipelineGen) yield break;
            if (error != null || assigned == null || assigned.code != AssignGateResult.CodeOk)
            { FailReconnect(gen, error ?? $"assign gate code={assigned?.code}"); yield break; }

            yield return ConnectAndEnter(gen, zoneId, assigned.gate_ip, (int)assigned.gate_port,
                SafeBase64(assigned.token_payload), SafeBase64(assigned.token_signature), null,
                () =>
                {
                    if (gen != _pipelineGen) return;
                    CancelReconnect();
                    Log($"[gate] reconnect complete player_id={PlayerId}");
                    if (battleId != 0) BattleLink?.HandleReconnectHint(battleId);
                }, e => FailReconnect(gen, e));
        }

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
