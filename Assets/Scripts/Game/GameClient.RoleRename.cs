using System;
using MmorpgClient.Game.Role;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game
{
    public sealed partial class GameClient
    {
        private bool _roleRenameInFlight;

        /// <summary>角色改名是否正在等待服务端审核或全服目录提交。</summary>
        public bool RoleRenameInFlight => _roleRenameInFlight;

        /// <summary>
        /// 角色改名最终结果事件:(succeeded, newName, message, nextRenameUnixSec)。
        /// 同步立即拒绝或异步推送结果时都在 Unity 主线程触发。
        /// </summary>
        public event Action<bool, string, string, ulong> OnRoleRenameOutcome;

        private void RegisterRoleRenameHandlers()
        {
            SceneClientPlayerCommonRpc.OnNotifyPlayerRenamed(
                this,
                HandlePlayerRenamedNotify,
                ex => LogError($"[rename] drop malformed PlayerRenamedNotify: {ex.Message}"));
        }

        private void ResetRoleRenameState()
        {
            _roleRenameInFlight = false;
        }

        /// <summary>
        /// 发起角色改名。客户端先做字符集与空白预检,通过后调用 SceneClientPlayerCommon.RenamePlayer;
        /// 返回 true 表示请求已发往服务端(最终结局经 <see cref="OnRoleRenameOutcome"/> 通知),false 表示本地即已拒绝。
        /// </summary>
        public bool RequestRoleRename(string rawName, Action<bool, string> onImmediateOutcome = null)
        {
            if (!InGame || !IsGateReady || PlayerId == 0)
            {
                const string msg = "尚未进入游戏，暂时无法改名";
                onImmediateOutcome?.Invoke(false, msg);
                OnRoleRenameOutcome?.Invoke(false, null, msg, 0);
                return false;
            }
            if (_roleRenameInFlight)
            {
                const string msg = "已有角色改名正在进行中，请稍后再试";
                onImmediateOutcome?.Invoke(false, msg);
                OnRoleRenameOutcome?.Invoke(false, null, msg, 0);
                return false;
            }
            if (!RoleNameRules.TryNormalize(rawName, out string normalized, out string normErr))
            {
                onImmediateOutcome?.Invoke(false, normErr);
                OnRoleRenameOutcome?.Invoke(false, null, normErr, 0);
                return false;
            }
            string currentName = ResolveRoleName(PlayerId);
            if (!string.IsNullOrEmpty(currentName) && string.Equals(normalized, currentName, StringComparison.Ordinal))
            {
                const string msg = "新角色名与当前名字相同";
                onImmediateOutcome?.Invoke(false, msg);
                OnRoleRenameOutcome?.Invoke(false, null, msg, 0);
                return false;
            }

            _roleRenameInFlight = true;
            SceneClientPlayerCommonRpc.RenamePlayer(
                this,
                new RenamePlayerRequest { Name = normalized },
                (rpcStatus, resp) =>
                {
                    if (!rpcStatus.IsOk)
                    {
                        _roleRenameInFlight = false;
                        uint envelopeTip = rpcStatus.Code == RpcStatusCode.ServerTip ? rpcStatus.TipId : 0;
                        string errText = envelopeTip != 0
                            ? RoleNameRules.RenameTipText(envelopeTip, null, 0)
                            : $"角色改名请求失败：{rpcStatus}";
                        onImmediateOutcome?.Invoke(false, errText);
                        OnRoleRenameOutcome?.Invoke(false, null, errText, 0);
                        return;
                    }
                    uint respTip = resp?.ErrorMessage?.Id ?? 0;
                    if (respTip != 0)
                    {
                        _roleRenameInFlight = false;
                        string tipText = RoleNameRules.RenameTipText(respTip, resp.ErrorMessage.Parameters, 0);
                        onImmediateOutcome?.Invoke(false, tipText);
                        OnRoleRenameOutcome?.Invoke(false, null, tipText, 0);
                        return;
                    }
                    // 已受理:不强行改写 _roleRenameInFlight = true,防止极快回环时推先于应答清掉标志后再被覆写。
                    const string pendingMsg = "改名申请已提交，正在审核与登记…";
                    onImmediateOutcome?.Invoke(true, pendingMsg);
                    Status(pendingMsg);
                });
            return true;
        }

        private void HandlePlayerRenamedNotify(PlayerRenamedNotify notify)
        {
            _roleRenameInFlight = false;
            if (notify == null) return;
            uint tipId = notify.ErrorMessage?.Id ?? 0;
            if (tipId != 0)
            {
                string errText = RoleNameRules.RenameTipText(tipId, notify.ErrorMessage.Parameters, notify.NextRenameTime);
                Log($"[rename] rejected tip={tipId}");
                Status(errText);
                OnRoleRenameOutcome?.Invoke(false, null, errText, notify.NextRenameTime);
                return;
            }
            string newName = notify.Name ?? string.Empty;
            if (!string.IsNullOrEmpty(newName) && PlayerId != 0 && _knownRoles.TryGetValue(PlayerId, out var role))
            {
                role.Name = newName;
                World?.RefreshDisplayNames();
            }
            string okMsg = string.IsNullOrEmpty(newName) ? "角色改名成功" : $"角色名已修改为 {newName}";
            Log($"[rename] succeeded player={PlayerId} name={newName}");
            Status(okMsg);
            OnRoleRenameOutcome?.Invoke(true, newName, okMsg, notify.NextRenameTime);
        }
    }
}
