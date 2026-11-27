using System;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Season
{
    /// <summary>
    /// 赛季技能与赛季印客户端网络协调器（RPC 453–455，纯 C#，零 UnityEngine 依赖，WebGL2/IL2CPP 安全）。
    /// 负责拉取赛季快照、按 Revision 单调更新、切换赛季印与幂等追赶，以及断线/切角复位。
    /// </summary>
    public sealed class SeasonClient : IDisposable
    {
        public static SeasonClient Instance { get; private set; }

        public const float MinStateIntervalSeconds = 2.0f;
        public const float MinActionIntervalSeconds = 0.8f;

        public const string UnsupportedMessage = "当前服务器暂未开放赛季系统。";
        public const string NotConnectedMessage = "尚未连接到游戏。";
        public const string StateUnavailableMessage = "暂时无法获取赛季数据。";
        public const string EquipPendingMessage = "正在切换赛季印…";
        public const string EquippedMessage = "已切换赛季印。";
        public const string CatchupPendingMessage = "正在追赶赛季印进度…";
        public const string CatchupDoneMessage = "赛季印进度已更新。";
        public const string TooFastMessage = "操作太快了，请稍候再试。";
        public const string RecoveryMessage = "网络异常，操作结果未知。请重新登录后查看。";

        private readonly IBattleTransport _net;
        private readonly SeasonMessageIds _messageIds;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;

        private object _observedConnection;
        private ulong _observedPlayerId;
        private bool _disposed;
        private int _generation;
        private float _nextStateAt = float.NegativeInfinity;
        private float _nextActionAt = float.NegativeInfinity;
        private bool _stateQueued;

        public event Action Changed;
        public event Action<string> Notice;
        public event Action<SeasonSealCatchupEvent> SealCatchupCompleted;

        public global::SeasonStateSnapshot Snapshot { get; private set; }
        public global::WuxingTowerStateSnapshot TowerSnapshot { get; private set; }
        public bool TowerStatePending { get; private set; }
        public uint EquippedFormationId => Snapshot?.EquippedFormationId ?? 0u;
        public System.Collections.Generic.IReadOnlyList<global::SavedBattleBuildBookmarkView> SavedBuilds =>
            Snapshot != null
                ? (System.Collections.Generic.IReadOnlyList<global::SavedBattleBuildBookmarkView>)Snapshot.SavedBuilds
                : System.Array.Empty<global::SavedBattleBuildBookmarkView>();
        public bool StatePending { get; private set; }
        public bool ActionPending { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "";
        public bool StatusIsError { get; private set; }

        public bool IsSupported => _messageIds.IsSupported;
        public bool Ready => IsSupported && !_disposed && _net.IsReady && _net.PlayerId != 0;

        public SeasonClient(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<float> clock,
            SeasonMessageIds? messageIds = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _messageIds = messageIds ?? SeasonMessageIds.Generated;
            _observedConnection = _connectionIdentity();
            _observedPlayerId = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
            if (!IsSupported) SetStatus(UnsupportedMessage, false);
        }

        public static SeasonClient Attach(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<float> clock,
            SeasonMessageIds? messageIds = null)
        {
            Instance?.Dispose();
            var client = new SeasonClient(transport, connectionIdentity, clock, messageIds);
            Instance = client;
            return client;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_generation;
            StatePending = false;
            ActionPending = false;
            if (Instance == this) Instance = null;
        }

        public void RequestState(bool force = false)
        {
            if (_disposed) return;
            if (!IsSupported) { SetStatus(UnsupportedMessage, false); return; }
            ObserveConnection();
            if (!Ready) return;
            float now = _clock();
            if (StatePending)
            {
                if (force)
                {
                    _stateQueued = true;
                    _nextStateAt = now;
                }
                return;
            }
            if (!force && now < _nextStateAt)
            {
                _stateQueued = true;
                return;
            }
            SendGetState(now);
        }

        public bool EquipSeal(uint sealId)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsSupported) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending) return false;
            if (Snapshot == null || !Snapshot.Available || Snapshot.SeasonId == 0)
                return RejectLocally(SeasonModels.DescribeTip((uint)season_error.KSeasonNotOpen));
            if (sealId == 0)
                return RejectLocally(SeasonModels.DescribeTip((uint)season_error.KSeasonSealInvalid));
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(EquipPendingMessage, false);
            Changed?.Invoke();

            var request = new global::EquipSeasonSealRequest
            {
                SeasonId = Snapshot.SeasonId,
                SealId = sealId,
            };
            _net.Call(_messageIds.EquipSeal, request, global::EquipSeasonSealResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    if (response.State != null) ApplySnapshot(response.State);
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleWriteTip("EquipSeasonSeal", tipId);
                        return;
                    }
                    SetStatus(EquippedMessage, false);
                    Notice?.Invoke(EquippedMessage);
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
                    SetStatus(tipId != 0 ? SeasonModels.DescribeTip(tipId) : StateUnavailableMessage, true);
                    Changed?.Invoke();
                });
            return true;
        }

        /// <summary>幂等追赶赛季印进度；pointsToBuy 为 0 表示按服务端当前剩余上限一次性补满。</summary>
        public bool CatchupSeal(uint pointsToBuy = 0u)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (!IsSupported) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            if (Snapshot == null || !Snapshot.Available || Snapshot.SeasonId == 0)
                return RejectLocally(SeasonModels.DescribeTip((uint)season_error.KSeasonNotOpen));
            uint remaining = SeasonModels.RemainingCatchupPoints(Snapshot);
            if (remaining == 0)
                return RejectLocally(SeasonModels.DescribeTip((uint)season_error.KSeasonSealAlreadyMax));
            if (!Snapshot.CanCatchup)
                return RejectLocally(string.IsNullOrEmpty(Snapshot.ActionUnavailableReason)
                    ? SeasonModels.DescribeTip((uint)season_error.KSeasonCatchupDisabled)
                    : Snapshot.ActionUnavailableReason);
            uint effectivePoints = pointsToBuy == 0 || pointsToBuy > remaining ? remaining : pointsToBuy;
            ulong expectedTotal = effectivePoints == remaining && Snapshot.CatchupRemainingTotalPrice != 0
                ? Snapshot.CatchupRemainingTotalPrice
                : Snapshot.CatchupPricePerPoint * effectivePoints;
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out ulong requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(CatchupPendingMessage, false);
            Changed?.Invoke();

            var request = new global::CatchupSeasonSealRequest
            {
                ClientRequestId = requestKey,
                SeasonId = Snapshot.SeasonId,
                PointsToBuy = effectivePoints,
                ExpectedCurrencyType = Snapshot.CatchupCurrencyType,
                ExpectedTotalPrice = expectedTotal,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.CatchupSeal, request, global::CatchupSeasonSealResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        if (response.State != null) ApplySnapshot(response.State);
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("CatchupSeasonSeal", tipId);
                            return;
                        }
                        string doneMsg = response.Replayed ? "赛季印进度已确认更新。" : CatchupDoneMessage;
                        SetStatus(doneMsg, false);
                        Notice?.Invoke(doneMsg);
                        SealCatchupCompleted?.Invoke(new SeasonSealCatchupEvent(
                            request.SeasonId,
                            response.AddedPoints,
                            response.SpentCurrency,
                            response.Replayed));
                        Changed?.Invoke();
                    },
                    error =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        if (!resent && ClientRequestKeys.ShouldResendOnce(error))
                        {
                            resent = true;
                            Send();
                            return;
                        }
                        HandleCatchupTransportError(error, resent);
                    });
            }
            Send();
            return true;
        }

        public void HandleSceneEntered()
        {
            if (_disposed) return;
            ObserveConnection();
            if (Snapshot == null && !StatePending) RequestState();
        }

        public void HandleBattleSettled()
        {
            if (_disposed) return;
            ObserveConnection();
            if (Ready)
            {
                RequestState(force: true);
                if (TowerSnapshot != null && TowerSnapshot.RunActive)
                    RequestWuxingTowerState(force: true);
            }
        }

        public bool EquipFormation(uint formationId)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (_messageIds.EquipWuxingFormation == 0u) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending) return false;
            if (formationId > 5u) return RejectLocally("无效的五行阵法编号。");
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus("正在布设五行阵法…", false);
            Changed?.Invoke();

            var req = new global::EquipWuxingFormationRequest { FormationId = formationId };
            _net.Call(_messageIds.EquipWuxingFormation, req, global::EquipWuxingFormationResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    if (response.State != null) ApplySnapshot(response.State);
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u) { HandleWriteTip("EquipWuxingFormation", tipId); return; }
                    string done = formationId == 0u ? "已卸下当前五行阵法。" : "已免费切换五行阵法。";
                    SetStatus(done, false);
                    Notice?.Invoke(done);
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    SetStatus(StateUnavailableMessage, true);
                    Changed?.Invoke();
                });
            return true;
        }

        public void RequestWuxingTowerState(bool force = false)
        {
            if (_disposed || _messageIds.GetWuxingTowerState == 0u) return;
            ObserveConnection();
            if (!Ready || (TowerStatePending && !force)) return;
            TowerStatePending = true;
            int generation = _generation;
            Changed?.Invoke();
            _net.Call(_messageIds.GetWuxingTowerState, new global::GetWuxingTowerStateRequest(),
                global::GetWuxingTowerStateResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    TowerStatePending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId == 0u && response.State != null)
                        ApplyTowerSnapshot(response.State);
                    else if (tipId != 0u)
                        SetInfoStatus(SeasonModels.DescribeTip(tipId), true);
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    TowerStatePending = false;
                    SetInfoStatus("暂时无法获取五行幻境状态。", true);
                    Changed?.Invoke();
                });
        }

        public bool StartWuxingTowerRun() => StartWuxingTowerRun(false, 0u, 0u);

        public bool StartWuxingTowerRun(bool useFreePreset, uint characterPresetId = 0u, uint petPresetId = 0u)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (_messageIds.StartWuxingTowerRun == 0u) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending) return false;

            ActionPending = true;
            int generation = _generation;
            var req = new global::StartWuxingTowerRunRequest
            {
                UseFreePreset = useFreePreset,
                CharacterPresetId = useFreePreset ? characterPresetId : 0u,
                PetPresetId = useFreePreset ? petPresetId : 0u,
            };
            SetStatus("正在开启新一轮五行幻境…", false);
            Changed?.Invoke();
            _net.Call(_messageIds.StartWuxingTowerRun, req,
                global::StartWuxingTowerRunResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    if (response.State != null) ApplyTowerSnapshot(response.State);
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u) { HandleWriteTip("StartWuxingTowerRun", tipId); return; }
                    string done = useFreePreset
                        ? "已开启公平 3×3 试炼秘境，可从第 1 关起探索！"
                        : "已开启五行幻境，可从第 1 关起探索！";
                    SetStatus(done, false);
                    Notice?.Invoke(done);
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    SetStatus("开启五行幻境失败，请稍后重试。", true);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool ChooseWuxingRealmBranch(uint branch)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (_messageIds.ChooseWuxingRealmBranch == 0u) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending || TowerSnapshot == null || !TowerSnapshot.RunActive) return false;
            if (branch != 1u && branch != 2u) return RejectLocally("无效的分岔路线选项。");

            ActionPending = true;
            int generation = _generation;
            var req = new global::ChooseWuxingRealmBranchRequest
            {
                ExpectedRunId = TowerSnapshot.RunId,
                Branch = branch,
            };
            SetStatus("正在确认秘境行进路线…", false);
            Changed?.Invoke();
            _net.Call(_messageIds.ChooseWuxingRealmBranch, req,
                global::ChooseWuxingRealmBranchResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    if (response.State != null) ApplyTowerSnapshot(response.State);
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u) { HandleWriteTip("ChooseWuxingRealmBranch", tipId); return; }
                    string done = branch == 1u ? "已选定左路：精英强敌。" : "已选定右路：秘境机缘。";
                    SetStatus(done, false);
                    Notice?.Invoke(done);
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    SetStatus("选择秘境路线失败，请稍后重试。", true);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool ResolveWuxingRealmEvent(uint optionIndex, uint targetBoonId = 0u)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (_messageIds.ResolveWuxingRealmEvent == 0u) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending || TowerSnapshot == null || !TowerSnapshot.RunActive) return false;
            if (optionIndex != 1u && optionIndex != 2u) return RejectLocally("无效的奇遇/休整抉择选项。");

            ActionPending = true;
            int generation = _generation;
            var req = new global::ResolveWuxingRealmEventRequest
            {
                ExpectedRunId = TowerSnapshot.RunId,
                OptionIndex = optionIndex,
                TargetBoonId = targetBoonId,
            };
            SetStatus("正在结算秘境机缘…", false);
            Changed?.Invoke();
            _net.Call(_messageIds.ResolveWuxingRealmEvent, req,
                global::ResolveWuxingRealmEventResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    if (response.State != null) ApplyTowerSnapshot(response.State);
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u) { HandleWriteTip("ResolveWuxingRealmEvent", tipId); return; }
                    string done = response.GrantedBoonId != 0u && CoreLoop.WuxingExpansionModels.TryFindSigil(response.GrantedBoonId, out var boon)
                        ? $"已获得五行奇策「{boon.Name}」！"
                        : "秘境抉择已完成，请继续推进！";
                    SetStatus(done, false);
                    Notice?.Invoke(done);
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    SetStatus("处理秘境事件失败，请稍后重试。", true);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool PickWuxingTowerSigil(uint sigilId)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (_messageIds.PickWuxingTowerSigil == 0u) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending || TowerSnapshot == null || !TowerSnapshot.RunActive) return false;

            ActionPending = true;
            int generation = _generation;
            var req = new global::PickWuxingTowerSigilRequest
            {
                ExpectedRunId = TowerSnapshot.RunId,
                SigilId = sigilId,
            };
            SetStatus("正在铭刻局内五行奇策…", false);
            Changed?.Invoke();
            _net.Call(_messageIds.PickWuxingTowerSigil, req, global::PickWuxingTowerSigilResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    if (response.State != null) ApplyTowerSnapshot(response.State);
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u) { HandleWriteTip("PickWuxingTowerSigil", tipId); return; }
                    SetStatus("已铭刻五行奇策，准备推进下一关！", false);
                    Notice?.Invoke("五行奇策已生效");
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    SetStatus("选取五行奇策失败，请稍后重试。", true);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool AbandonWuxingTowerRun()
        {
            if (_disposed) return false;
            ObserveConnection();
            if (_messageIds.AbandonWuxingTowerRun == 0u) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending || TowerSnapshot == null) return false;

            ActionPending = true;
            int generation = _generation;
            var req = new global::AbandonWuxingTowerRunRequest { ExpectedRunId = TowerSnapshot.RunId };
            Changed?.Invoke();
            _net.Call(_messageIds.AbandonWuxingTowerRun, req, global::AbandonWuxingTowerRunResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    if (response.State != null) ApplyTowerSnapshot(response.State);
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u) { HandleWriteTip("AbandonWuxingTowerRun", tipId); return; }
                    SetStatus("已结束本轮幻境（本周首通记录保留）。", false);
                    Notice?.Invoke("已放弃本轮幻境");
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    SetStatus("操作失败，请稍后重试。", true);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool SaveBattleBuildBookmark(global::SavedBattleBuildBookmarkView bookmark)
        {
            if (_disposed || bookmark == null) return false;
            ObserveConnection();
            if (_messageIds.SaveBattleBuildBookmark == 0u) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending) return false;

            ActionPending = true;
            int generation = _generation;
            var req = new global::SaveBattleBuildBookmarkRequest { Bookmark = bookmark.Clone() };
            Changed?.Invoke();
            _net.Call(_messageIds.SaveBattleBuildBookmark, req, global::SaveBattleBuildBookmarkResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u) { HandleWriteTip("SaveBattleBuildBookmark", tipId); return; }
                    ReplaceSavedBookmarks(response.AllBookmarks);
                    SetStatus("已收藏该道友公开配装。", false);
                    Notice?.Invoke("配装已收藏");
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    SetStatus("收藏配装失败，请稍后重试。", true);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool DeleteBattleBuildBookmark(ulong bookmarkId)
        {
            if (_disposed || bookmarkId == 0ul) return false;
            ObserveConnection();
            if (_messageIds.DeleteBattleBuildBookmark == 0u) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending) return false;

            ActionPending = true;
            int generation = _generation;
            var req = new global::DeleteBattleBuildBookmarkRequest { BookmarkId = bookmarkId };
            Changed?.Invoke();
            _net.Call(_messageIds.DeleteBattleBuildBookmark, req, global::DeleteBattleBuildBookmarkResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u) { HandleWriteTip("DeleteBattleBuildBookmark", tipId); return; }
                    ReplaceSavedBookmarks(response.AllBookmarks);
                    SetStatus("已删除该配装收藏。", false);
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    SetStatus("删除配装收藏失败。", true);
                    Changed?.Invoke();
                });
            return true;
        }

        /// <summary>569 设定/清除「往季徽章追忆」指定目标（0=自动顺延首个未解锁往季；纯免费零收费）。</summary>
        public bool SetRetroHonorTarget(uint seasonId)
        {
            if (_disposed) return false;
            ObserveConnection();
            if (_messageIds.SetRetroHonorTarget == 0u) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (ActionPending) return false;
            float now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);

            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus("正在更新往季徽章追忆目标…", false);
            Changed?.Invoke();

            var req = new global::SetRetroHonorTargetRequest { SeasonId = seasonId };
            _net.Call(_messageIds.SetRetroHonorTarget, req, global::SetRetroHonorTargetResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    if (response.State != null) ApplySnapshot(response.State);
                    uint tipId = response.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u) { HandleWriteTip("SetRetroHonorTarget", tipId); return; }
                    string msg = seasonId == 0u ? "已切换为自动顺延追忆往季徽章。" : "已设定往季徽章追忆目标。";
                    SetStatus(msg, false);
                    Notice?.Invoke(msg);
                    Changed?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0u;
                    SetStatus(tipId != 0u ? SeasonModels.DescribeTip(tipId) : StateUnavailableMessage, true);
                    Changed?.Invoke();
                });
            return true;
        }

        private void ApplyTowerSnapshot(global::WuxingTowerStateSnapshot incoming)
        {
            if (incoming == null) return;
            if (TowerSnapshot != null && incoming.WeekKey == TowerSnapshot.WeekKey && incoming.Revision < TowerSnapshot.Revision)
                return;
            TowerSnapshot = incoming.Clone();
        }

        private void ReplaceSavedBookmarks(Google.Protobuf.Collections.RepeatedField<global::SavedBattleBuildBookmarkView> all)
        {
            if (all == null) return;
            if (Snapshot == null) Snapshot = new global::SeasonStateSnapshot();
            Snapshot.SavedBuilds.Clear();
            for (int i = 0; i < all.Count; ++i)
                Snapshot.SavedBuilds.Add(all[i].Clone());
        }

        public void HandleDisconnected()
        {
            if (_disposed) return;
            Reset();
            Changed?.Invoke();
        }

        public void Tick()
        {
            if (_disposed || !IsSupported) return;
            ObserveConnection();
            float now = _clock();
            if (_stateQueued && Ready && !StatePending && now >= _nextStateAt)
                SendGetState(now);
        }

        private void SendGetState(float now)
        {
            _stateQueued = false;
            StatePending = true;
            _nextStateAt = now + MinStateIntervalSeconds;
            int generation = _generation;
            Changed?.Invoke();

            _net.Call(_messageIds.GetState, new global::GetSeasonStateRequest(), global::GetSeasonStateResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    StatePending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0 || response.State == null)
                    {
                        SetInfoStatus(tipId != 0 ? SeasonModels.DescribeTip(tipId) : StateUnavailableMessage, true);
                    }
                    else
                    {
                        ApplySnapshot(response.State);
                        if (Status == StateUnavailableMessage) SetInfoStatus("", false);
                    }
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    StatePending = false;
                    SetInfoStatus(StateUnavailableMessage, true);
                    Changed?.Invoke();
                });
        }

        private void HandleWriteTip(string opName, uint tipId)
        {
            if (tipId == (uint)common_error.KInvalidParameter) ClientRequestKeys.ReportKeyReused(opName);
            if (tipId == (uint)common_error.KRateLimitExceeded) _nextActionAt = _clock() + MinStateIntervalSeconds;
            SetStatus(SeasonModels.DescribeTip(tipId), true);
            if (tipId == (uint)season_error.KSeasonMismatch
                || tipId == (uint)season_error.KSeasonCatchupPriceChanged
                || tipId == (uint)season_error.KSeasonSealAlreadyMax
                || tipId == (uint)season_error.KSeasonHonorAlreadyUnlocked
                || tipId == (uint)season_error.KSeasonHonorTargetInvalid)
            {
                RequestState(force: true);
            }
            Changed?.Invoke();
        }

        private void HandleCatchupTransportError(string error, bool resent)
        {
            ActionPending = false;
            uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
            if (!resent && tipId != 0 && tipId != (uint)common_error.KServiceUnavailable)
            {
                if (tipId == (uint)common_error.KRateLimitExceeded) _nextActionAt = _clock() + MinStateIntervalSeconds;
                SetStatus(SeasonModels.DescribeTip(tipId), true);
            }
            else
            {
                RequiresReconnect = true;
                SetStatus(RecoveryMessage, true);
            }
            Changed?.Invoke();
        }

        private void ApplySnapshot(global::SeasonStateSnapshot incoming)
        {
            if (incoming == null) return;
            if (Snapshot != null
                && incoming.SeasonId == Snapshot.SeasonId
                && incoming.SeasonTerm == Snapshot.SeasonTerm
                && incoming.Revision < Snapshot.Revision)
                return;
            Snapshot = incoming.Clone();
        }

        private void ObserveConnection()
        {
            var current = _connectionIdentity();
            ulong currentPlayerId = _net.PlayerId;
            if (ReferenceEquals(current, _observedConnection) && currentPlayerId == _observedPlayerId) return;
            _observedConnection = current;
            _observedPlayerId = currentPlayerId;
            Reset();
        }

        private void Reset()
        {
            ++_generation;
            Snapshot = null;
            TowerSnapshot = null;
            TowerStatePending = false;
            StatePending = false;
            ActionPending = false;
            RequiresReconnect = false;
            _stateQueued = false;
            SetStatus(IsSupported ? "" : UnsupportedMessage, false);
        }

        private bool IsCurrent(int generation)
        {
            if (_disposed) return false;
            ObserveConnection();
            return generation == _generation;
        }

        private bool RejectLocally(string message)
        {
            SetStatus(message, true);
            Changed?.Invoke();
            return false;
        }

        private void SetInfoStatus(string message, bool isError)
        {
            if (RequiresReconnect) return;
            SetStatus(message, isError);
        }

        private void SetStatus(string message, bool isError)
        {
            Status = message ?? "";
            StatusIsError = isError && Status.Length != 0;
        }
    }
}
