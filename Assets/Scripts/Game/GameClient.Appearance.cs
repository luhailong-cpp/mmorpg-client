using System;
using MmorpgClient.Net.Generated;
using MmorpgClient.World;

namespace MmorpgClient.Game
{
    public sealed partial class GameClient
    {
        /// <summary>
        /// 场景内任意玩家外观变更通知：(playerId, resolvedAppearanceId, isTrial, trialExpiresAt)。
        /// 涵盖本角色永久切换、1 小时试用开启、提前取消、到期还原以及周边 AOI 玩家实时换皮。
        /// </summary>
        public event Action<ulong, string, bool, ulong> OnActorAppearanceChanged;

        private void RegisterAppearanceHandlers()
        {
            SceneAppearanceClientPlayerRpc.OnNotifyActorAppearanceChanged(
                this,
                HandleActorAppearanceChanged,
                ex => LogError($"[appearance] drop malformed ActorAppearanceChangedS2C: {ex.Message}"));
        }

        private void HandleActorAppearanceChanged(ActorAppearanceChangedS2C ev)
        {
            if (ev == null || ev.PlayerId == 0) return;
            ApplyActorAppearanceChanged(ev.PlayerId, ev.AppearanceId, ev.IsTrial, ev.TrialExpiresAt);
        }

        internal void ApplyActorAppearanceChanged(ulong playerId, string rawAppearanceId, bool isTrial, ulong trialExpiresAt)
        {
            if (playerId == 0) return;
            _knownRoles.TryGetValue(playerId, out var role);
            uint classId = role?.ClassId ?? 0u;
            uint gender = role?.Gender ?? 0u;
            string resolved = QdaoCharacterCatalog.ResolveRole(classId, gender, rawAppearanceId);
            if (!string.IsNullOrEmpty(resolved))
                _sceneAppearances[playerId] = resolved;
            if (!isTrial && role != null && !string.IsNullOrEmpty(rawAppearanceId))
                role.AppearanceId = rawAppearanceId;
            World?.RefreshAppearances();
            OnActorAppearanceChanged?.Invoke(playerId, resolved, isTrial, isTrial ? trialExpiresAt : 0ul);
        }
    }
}
