using System;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>
    /// Player-owned bag, mission and activity snapshots over the existing gate transport.
    /// Lists are server authoritative. A request is bound to its player and connection
    /// epoch so late callbacks cannot repopulate another character's windows.
    /// </summary>
    public sealed class PlayerFeaturesClient : IDisposable
    {
        public static PlayerFeaturesClient Instance { get; private set; }

        private readonly IBattleTransport _net;
        private ulong _snapshotPlayerId;
        private int _epoch;
        private int _bagRequest;
        private int _missionRequest;
        private int _activityRequest;
        private bool _disposed;

        public BagInfo Bag { get; private set; }
        public GetMissionListResponse Missions { get; private set; }
        public GetActivityListResponse Activities { get; private set; }
        public bool HasBag => Bag != null;
        public bool HasMissions => Missions != null;
        public bool HasActivities => Activities != null;
        public bool BagLoading { get; private set; }
        public bool MissionsLoading { get; private set; }
        public bool ActivitiesLoading { get; private set; }
        public bool BusySort { get; private set; }
        public bool BusyMissionAction { get; private set; }
        public uint RequestedBagType { get; private set; }
        public string BagError { get; private set; } = string.Empty;
        public string MissionsError { get; private set; } = string.Empty;
        public string ActivitiesError { get; private set; } = string.Empty;

        public event Action OnChanged;
        public event Action<string> OnError;

        public PlayerFeaturesClient(IBattleTransport transport)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _net.Disconnected += HandleDisconnected;
        }

        public static PlayerFeaturesClient Attach(IBattleTransport transport)
        {
            var client = new PlayerFeaturesClient(transport);
            Instance = client;
            return client;
        }

        public void RequestBag(uint bagType = 0)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (bagType > 3) { FailBag("背包类型不存在"); return; }
            if (BusySort) { FailBag("背包正在整理，请稍候"); return; }

            int epoch = _epoch;
            int request = ++_bagRequest;
            RequestedBagType = bagType;
            if (Bag?.Layout != null && Bag.Layout.BagType != bagType) Bag = null;
            BagLoading = true;
            BagError = string.Empty;
            OnChanged?.Invoke();
            _net.Call(SceneBagClientPlayerGetBagHandler.MessageId,
                new GetBagRequest { BagType = bagType }, GetBagResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _bagRequest) return;
                    BagLoading = false;
                    if (HasTip(response.ErrorMessage)) { FailBag(DescribeTip("读取背包失败", response.ErrorMessage)); return; }
                    if (!ValidBag(response.Bag, bagType)) { FailBag("服务器未返回有效的背包数据"); return; }
                    Bag = response.Bag;
                    BagError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _bagRequest) return;
                    BagLoading = false;
                    FailBag(error);
                });
        }

        /// <summary>Called only by the player's explicit arrange button, never on open.</summary>
        public void SortBag()
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyMissionAction) { FailBag("任务正在处理中，请稍候"); return; }
            if (BusySort || BagLoading) { FailBag("背包正在处理中，请稍候"); return; }
            if (Bag?.Layout == null) { FailBag("请先读取背包"); return; }
            if (!Bag.Layout.CanSort || Bag.Layout.BagType > 1) { FailBag("此背包暂不支持整理"); return; }

            int epoch = _epoch;
            int request = ++_bagRequest;
            uint bagType = Bag.Layout.BagType;
            BusySort = true;
            BagError = string.Empty;
            OnChanged?.Invoke();
            _net.Call(SceneBagClientPlayerSortBagHandler.MessageId,
                new SortBagRequest { BagType = bagType }, SortBagResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _bagRequest) return;
                    BusySort = false;
                    if (HasTip(response.ErrorMessage)) { FailBag(DescribeTip("整理背包失败", response.ErrorMessage)); return; }
                    if (!ValidBag(response.Bag, bagType)) { FailBag("服务器未返回整理后的背包"); return; }
                    Bag = response.Bag;
                    BagError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _bagRequest) return;
                    BusySort = false;
                    FailBag(error);
                });
        }

        public void RequestMissions()
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyMissionAction) return;
            int epoch = _epoch;
            int request = ++_missionRequest;
            MissionsLoading = true;
            MissionsError = string.Empty;
            OnChanged?.Invoke();
            _net.Call(SceneMissionClientPlayerGetMissionListHandler.MessageId,
                new GetMissionListRequest(), GetMissionListResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
                    MissionsLoading = false;
                    if (HasTip(response.ErrorMessage)) { FailMissions(DescribeTip("读取任务失败", response.ErrorMessage)); return; }
                    Missions = response;
                    MissionsError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
                    MissionsLoading = false;
                    FailMissions(error);
                });
        }

        public void RequestActivities()
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyMissionAction) return;
            int epoch = _epoch;
            int request = ++_activityRequest;
            ActivitiesLoading = true;
            ActivitiesError = string.Empty;
            OnChanged?.Invoke();
            _net.Call(SceneActivityClientPlayerGetActivityListHandler.MessageId,
                new GetActivityListRequest(), GetActivityListResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _activityRequest) return;
                    ActivitiesLoading = false;
                    if (HasTip(response.ErrorMessage)) { FailActivities(DescribeTip("读取活动失败", response.ErrorMessage)); return; }
                    Activities = response;
                    ActivitiesError = string.Empty;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _activityRequest) return;
                    ActivitiesLoading = false;
                    FailActivities(error);
                });
        }

        public void AcceptMission(uint scope, uint missionId) => PerformMissionAction(scope, missionId, false);

        public void ClaimMissionReward(uint scope, uint missionId) => PerformMissionAction(scope, missionId, true);

        private void PerformMissionAction(uint scope, uint missionId, bool claim)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyMissionAction) return;
            if (BusySort) { FailMissions("背包正在整理，请稍候"); return; }
            PlayerMissionInfo mission = null;
            if (Missions != null)
                foreach (var entry in Missions.Missions)
                    if (entry.Scope == scope && entry.MissionId == missionId) { mission = entry; break; }
            bool allowed = mission != null && (claim ? mission.CanClaim : mission.CanAccept);
            if (!claim && mission == null && scope == 0 && Activities != null)
                foreach (var activity in Activities.Activities)
                    if (activity.MissionId == missionId && activity.CanParticipate) { allowed = true; break; }
            if (missionId == 0 || !allowed)
            {
                FailMissions(!string.IsNullOrWhiteSpace(mission?.UnavailableReason) ? mission.UnavailableReason :
                    claim ? "当前任务暂不可领奖，请刷新查看" : "当前任务暂不可接取，请刷新查看");
                return;
            }

            int epoch = _epoch;
            int request = ++_missionRequest;
            ++_activityRequest;
            MissionsLoading = ActivitiesLoading = false;
            BusyMissionAction = true;
            MissionsError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
            _net.Call(claim ? SceneMissionClientPlayerClaimMissionRewardHandler.MessageId :
                    SceneMissionClientPlayerAcceptMissionHandler.MessageId,
                new MissionActionRequest { Scope = scope, MissionId = missionId }, GetMissionListResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
                    BusyMissionAction = false;
                    if (HasTip(response.ErrorMessage))
                    { FailMissions(DescribeTip(claim ? "领取奖励失败" : "接取任务失败", response.ErrorMessage)); return; }
                    Missions = response;
                    MissionsError = string.Empty;
                    OnChanged?.Invoke();
                    if (!IsCurrent(epoch, playerId)) return;
                    RequestActivities();
                    if (claim && IsCurrent(epoch, playerId)) RequestBag(RequestedBagType);
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || request != _missionRequest) return;
                    BusyMissionAction = false;
                    FailMissions(error);
                });
        }
        private bool PrepareRequest(out ulong playerId)
        {
            playerId = _net.PlayerId;
            if (_disposed) return false;
            if (_snapshotPlayerId != 0 && _snapshotPlayerId != playerId) ClearSnapshots();
            if (!_net.IsReady || playerId == 0)
            {
                const string error = "尚未进入游戏，请进入角色后再试";
                BagError = MissionsError = ActivitiesError = error;
                OnChanged?.Invoke();
                OnError?.Invoke(error);
                return false;
            }
            _snapshotPlayerId = playerId;
            return true;
        }

        private bool IsCurrent(int epoch, ulong playerId)
            => !_disposed && _epoch == epoch && _net.IsReady && _net.PlayerId == playerId;

        private static bool ValidBag(BagInfo bag, uint bagType)
            => bag?.Layout != null && bag.Layout.BagType == bagType;

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeTip(string operation, TipInfoMessage tip)
            => $"{operation}（错误码 {tip.Id}）";

        private void FailBag(string message)
        {
            BagError = string.IsNullOrWhiteSpace(message) ? "背包请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(BagError);
        }

        private void FailMissions(string message)
        {
            MissionsError = string.IsNullOrWhiteSpace(message) ? "任务请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(MissionsError);
        }

        private void FailActivities(string message)
        {
            ActivitiesError = string.IsNullOrWhiteSpace(message) ? "活动请求失败，请重试" : message;
            OnChanged?.Invoke();
            OnError?.Invoke(ActivitiesError);
        }

        private void HandleDisconnected()
        {
            if (_disposed) return;
            ClearSnapshots();
        }

        private void ClearSnapshots()
        {
            ++_epoch;
            _snapshotPlayerId = 0;
            Bag = null;
            Missions = null;
            Activities = null;
            BagLoading = MissionsLoading = ActivitiesLoading = BusySort = BusyMissionAction = false;
            BagError = MissionsError = ActivitiesError = string.Empty;
            RequestedBagType = 0;
            OnChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _net.Disconnected -= HandleDisconnected;
            ClearSnapshots();
            if (ReferenceEquals(Instance, this)) Instance = null;
            OnChanged = null;
            OnError = null;
        }
    }
}
