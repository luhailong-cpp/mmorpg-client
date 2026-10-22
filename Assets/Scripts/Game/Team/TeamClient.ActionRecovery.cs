using System;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using Teampb;

namespace MmorpgClient.Game.Team
{
    public sealed partial class TeamClient
    {
        private DurableRequestFileStore _actionRecovery;
        public DurableRequestFileStore ActionRecovery
        {
            get => _actionRecovery;
            set {
                if (ReferenceEquals(_actionRecovery, value)) return;
                if (_actionRecoveryPlayer != 0) throw new InvalidOperationException("组队恢复存储必须在首次请求前绑定。");
                _actionRecovery = value;
            }
        }
        private IMessage _pendingActionRequest;
        private TeamAction _pendingAction;
        private uint _pendingActionMessage;
        private ulong _pendingActionTarget, _actionRecoveryPlayer;
        private string _pendingActionSaved;
        private bool _actionRecoveryBlocked;
        private bool _actionHadUncertainAttempt;
        private float _actionRetryAt;

        // 一个玩家一个原写意图槽；重连、崩溃与UI重建均重放原UUID和完整正文。
        private void RestoreActionIntent()
        {
            if (PlayerId == 0 || _actionRecoveryPlayer == PlayerId) return;
            _actionRecoveryPlayer = PlayerId;
            _pendingActionRequest = null;
            _pendingActionSaved = null;
            _actionRecoveryBlocked = false;
            _actionHadUncertainAttempt = false;

            try {
                string raw = ActionRecovery?.Load(PlayerId) ?? string.Empty;
                if (raw.Length == 0) return;
                var parts = raw.Split(new[] { '\n' }, 5);
                if (parts.Length != 5 || parts[0] != "1") throw new InvalidOperationException();
                var action = (TeamAction)int.Parse(parts[1], CultureInfo.InvariantCulture);
                uint message = uint.Parse(parts[2], CultureInfo.InvariantCulture);
                ulong target = ulong.Parse(parts[3], CultureInfo.InvariantCulture);
                var request = ParseAction(action, message, Convert.FromBase64String(parts[4]));
                var field = request.Descriptor.FindFieldByName("request_id");
                if (field == null || !Guid.TryParseExact((string)field.Accessor.GetValue(request), "D", out var id) || id == Guid.Empty)
                    throw new InvalidOperationException();
                _pendingAction = action; _pendingActionMessage = message; _pendingActionTarget = target;
                _pendingActionRequest = request; _pendingActionSaved = raw;
                _actionHadUncertainAttempt = true;
                RefreshQueued = true;
            } catch (Exception) {
                _actionRecoveryBlocked = true;
                Status = "组队恢复记录损坏或无法读取，已保留原记录。";
            }
        }

        private bool BindActionIntent(TeamAction action, ulong target, uint message, ref IMessage request, out bool replay)
        {
            replay = false;
            if (_actionRecoveryBlocked) { Reject("组队恢复记录尚未修复，无法发起新操作。"); return false; }
            if (_pendingActionRequest == null) return true;
            if (_pendingAction != action || _pendingActionTarget != target || _pendingActionMessage != message
                || !SameActionPayload(_pendingActionRequest, request)) {
                Reject("上一次组队操作结果尚未确认，正在恢复原请求。"); return false;
            }
            request = _pendingActionRequest.Descriptor.Parser.ParseFrom(_pendingActionRequest.ToByteArray());
            replay = true;
            return true;
        }

        private static bool SameActionPayload(IMessage original, IMessage request)
        {
            if (original.Descriptor != request.Descriptor) return false;
            IMessage CopyWithoutId(IMessage value) {
                var copy = value.Descriptor.Parser.ParseFrom(value.ToByteArray());
                copy.Descriptor.FindFieldByName("request_id").Accessor.Clear(copy);
                return copy;
            }
            return CopyWithoutId(original).Equals(CopyWithoutId(request));
        }

        private void SaveActionIntent(TeamAction action, ulong target, uint message, IMessage request)
        {
            if (_pendingActionRequest != null) return;
            var field = request.Descriptor.FindFieldByName("request_id");
            if (field == null) throw new InvalidOperationException("组队协议缺少稳定请求号");
            field.Accessor.SetValue(request, Guid.NewGuid().ToString("D"));
            string raw = "1\n" + ((int)action).ToString(CultureInfo.InvariantCulture) + "\n"
                + message.ToString(CultureInfo.InvariantCulture) + "\n" + target.ToString(CultureInfo.InvariantCulture)
                + "\n" + Convert.ToBase64String(request.ToByteArray());
            ActionRecovery?.Save(PlayerId, raw);
            _pendingActionRequest = request.Descriptor.Parser.ParseFrom(request.ToByteArray());
            _pendingAction = action; _pendingActionMessage = message; _pendingActionTarget = target;
            _pendingActionSaved = raw;
            _actionHadUncertainAttempt = false;
        }

        private void CompleteActionIntent(TeamResponse response)
        {
            if (response.ErrorMessage != null && TipFaults.IsFault(response.ErrorMessage.Id)) {
                _actionHadUncertainAttempt = true;
                _actionRetryAt = _clock() + ReadRetryDelaySeconds; return;
            }
            try {
                ClearActionIntent();
            } catch (Exception) {
                _actionRecoveryBlocked = true;
                Status = "组队操作已确认，本机原请求尚未清理。";
            }
        }

        private void ClearActionIntent()
        {
            ActionRecovery?.Clear(PlayerId, _pendingActionSaved);
            _pendingActionRequest = null; _pendingActionSaved = null;
            _actionHadUncertainAttempt = false;
        }

        private bool ReplayActionIntent(float now)
        {
            if (_pendingActionRequest == null || _actionRecoveryBlocked || now < _actionRetryAt) return false;
            _actionRetryAt = now + ReadRetryDelaySeconds;
            return Send(_pendingAction, _pendingActionTarget, _pendingActionMessage, _pendingActionRequest,
                TeamResponse.Parser, false,
                reply => FinishWrite(_pendingAction, _pendingActionTarget, reply, "原组队操作已确认。"));
        }

        private static IMessage ParseAction(TeamAction action, uint message, byte[] bytes)
        {
            switch (action) {
                case TeamAction.Create when message == MessageIds.CreateTeam: return CreateTeamRequest.Parser.ParseFrom(bytes);
                case TeamAction.Apply when message == MessageIds.ApplyJoinTeam: return ApplyJoinTeamRequest.Parser.ParseFrom(bytes);
                case TeamAction.Decide when message == MessageIds.HandleApplication: return HandleApplicationRequest.Parser.ParseFrom(bytes);
                case TeamAction.Invite when message == MessageIds.InviteToTeam: return InviteToTeamRequest.Parser.ParseFrom(bytes);
                case TeamAction.RespondInvite when message == MessageIds.RespondInvite: return RespondInviteRequest.Parser.ParseFrom(bytes);
                case TeamAction.Leave when message == MessageIds.LeaveTeam: return LeaveTeamRequest.Parser.ParseFrom(bytes);
                case TeamAction.Kick when message == MessageIds.KickMember: return KickMemberRequest.Parser.ParseFrom(bytes);
                case TeamAction.Transfer when message == MessageIds.TransferLeader: return TransferLeaderRequest.Parser.ParseFrom(bytes);
                case TeamAction.Disband when message == MessageIds.DisbandTeam: return DisbandTeamRequest.Parser.ParseFrom(bytes);
                default: throw new InvalidOperationException("未知组队恢复操作");
            }
        }
    }
}
