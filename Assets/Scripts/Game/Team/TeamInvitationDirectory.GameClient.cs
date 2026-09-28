using System;
using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Social;
using MmorpgClient.World;

namespace MmorpgClient.Game.Team
{
    public sealed partial class TeamInvitationDirectory
    {
        public TeamInvitationDirectory(GameClient game, Func<SocialState> socialState)
            : this(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                source => ReadSessionCandidates(game, socialState?.Invoke(), source),
                id => SessionRole(game, id), ready: () => game.InGame && game.IsGateReady && game.PlayerId != 0)
        { }

        private static TeamRole SessionRole(GameClient game, ulong id) => new TeamRole
        {
            PlayerId = id, Name = game.ResolveRoleName(id), CharacterId = game.ResolveCharacterId(id),
            IsOnline = false, OnlineStatusKnown = false,
        };

        private static IEnumerable<TeamRole> ReadSessionCandidates(GameClient game, SocialState social, TeamInvitationSource source)
        {
            if (!game.InGame || !game.IsGateReady) yield break;
            if (source == TeamInvitationSource.Nearby)
            {
                foreach (var actor in game.World.Actors.Values)
                {
                    if (actor == null || actor.Kind != ActorKind.Player || actor.PlayerId == 0 || actor.PlayerId == game.PlayerId) continue;
                    var role = SessionRole(game, actor.PlayerId);
                    if (!string.IsNullOrEmpty(actor.CharacterId)) role.CharacterId = actor.CharacterId;
                    role.IsOnline = true;
                    role.OnlineStatusKnown = true;
                    yield return role;
                }
            }
            else if (source == TeamInvitationSource.Chat && social != null && !social.IsPreview && social.PlayerId == game.PlayerId)
            {
                var ids = new HashSet<ulong>();
                foreach (var channel in SocialState.ChatChannels)
                {
                    var messages = social.Messages(channel);
                    for (int index = messages.Count - 1; index >= 0; --index)
                    {
                        var message = messages[index];
                        if (message == null || message.Local || message.Sender == 0 || message.Sender == game.PlayerId || !ids.Add(message.Sender)) continue;
                        // Chat history authenticates an identity, not its current online presence.
                        yield return SessionRole(game, message.Sender);
                    }
                }
            }
        }
    }
}
