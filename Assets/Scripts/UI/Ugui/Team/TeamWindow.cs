using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Team.TeamUiArt;

namespace MmorpgClient.UI.Ugui.Team
{
    /// <summary>
    /// Member and application (or received-invite) lists share one view, driven only by
    /// authoritative state; every button re-checks its permission before emitting an intent.
    /// </summary>
    public sealed class TeamWindow
    {
        public const int RowsPerPage = 4;
        public const int MaxMemberCards = 5;
        public event Action RefreshRequested;
        public event Action<ulong, bool> DecisionRequested;
        public event Action CreateRequested;
        public event Action<ulong> ApplyRequested;
        public event Action<ulong> InviteRequested;
        public event Action InviteBrowseRequested;
        public event Action<ulong, bool> InviteResponseRequested;
        public event Action LeaveRequested;
        public event Action<ulong> KickRequested;
        public event Action<ulong> TransferRequested;
        public event Action DisbandRequested;
        public event Action StartMatchRequested;
        /// <summary>队伍补位匹配:参数是目标人数(大于当前队伍人数)。直接开战仍走 StartMatchRequested。</summary>
        public event Action<uint> BackfillRequested;
        public event Action Closed;
        public bool IsVisible => _root.gameObject.activeSelf;
        public bool IsModalOpen => _modal != null;
        /// <summary>生产宿主提供队伍视图新鲜度；离线预览可省略，不影响只读刷新与浏览。</summary>
        public Func<bool> FreshViewAvailable { get; set; }
        // Kept for callers that open a particular list. Both lists remain visible.
        public TeamPage Page { get; private set; }
        public int MemberPageIndex { get; private set; }
        public int ApplicationPageIndex { get; private set; }
        public int PageIndex => Page == TeamPage.Applications ? ApplicationPageIndex : MemberPageIndex;

        private static readonly Color BodyInk = QdaoUguiTheme.Html("#304736");
        private static readonly Color BodyMuted = QdaoUguiTheme.Html("#70634E");
        private static readonly Color Jade = QdaoUguiTheme.Html("#205B46");
        private static readonly Color Border = QdaoUguiTheme.Html("#C7B68E");
        private static readonly Color ErrorInk = QdaoUguiTheme.Html("#9A3B2E");
        private const string BusyHint = "上一个组队请求仍在处理中，请稍候。";
        private static TMP_FontAsset _bodyFont;
        private readonly RectTransform _root, _members, _applications;
        private readonly CanvasGroup _frameControls;
        private readonly Image _exactApplicationTitle, _exactRefresh;
        private readonly Image _summaryFallbackPlate, _exactLeaderState, _exactFooterSync, _exactInstruction;
        private readonly TMP_Text _teamCountValue, _teamIdValue, _memberVacancyValue;
        private readonly TMP_Text _summary, _status, _memberCount, _applicationCount, _applicationTitle, _leaderState, _footerSync;
        private readonly TMP_Text _memberPageLabel, _applicationPageLabel, _refreshLabel, _startMatchLabel;
        private readonly Button _refresh, _close, _memberPrevious, _memberNext, _applicationPrevious, _applicationNext;
        private readonly Button _startMatch, _invite, _disband, _leave, _create, _apply;
        private TeamUiState _state;
        private GameObject _returnFocus, _modalReturnFocus;
        // Row buttons are rebuilt by every Render, so the opener is also remembered by name and list.
        private string _modalReturnName;
        private UnityEngine.Transform _modalReturnList;
        private RectTransform _modal;
        private Func<bool> _modalAllowed;
        private bool _modalLeader;
        private TMP_Text _modalHint;
        // Set only while a dialog re-checks its permission: a request in flight is transient and must not
        // close the dialog or silently drop its action.
        private bool _ignoreBusy;

        public TeamWindow(UnityEngine.Transform parent)
        {
            _root=QdaoUguiFactory.CreateStretch("TeamWindow",parent,Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade=_root.gameObject.AddComponent<Image>();shade.color=new Color(.025f,.10f,.08f,.68f);shade.raycastTarget=true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            // All main-panel measurements are original-art pixels, with one uniform display scale.
            var frame=QdaoUguiFactory.CreateCenteredRect("TeamFrame",_root,1931,814);
            frame.localScale=UnityEngine.Vector3.one*(2560f/1931f);
            _frameControls=frame.gameObject.AddComponent<CanvasGroup>();
            ExactArt(frame,"team-skin",0,0,1931,814);
            _close=QdaoUguiFactory.CreateHitButton("CloseTeamWindow",frame,1747,32,86,86);
            _close.navigation=new Navigation{mode=Navigation.Mode.Automatic};_close.onClick.AddListener(Hide);
            _summaryFallbackPlate=ExactArt(frame,"summary-fallback",1206,111,338,43);
            _summary=Label(frame,"队伍信息尚未同步",1207,112,332,36,20,BodyInk);
            _teamCountValue=Label(frame,"",1288,114,51,36,20,BodyInk,TextAlignmentOptions.Center);
            _teamIdValue=Label(frame,"",1462,114,68,36,20,BodyInk,TextAlignmentOptions.Center);
            Art(frame,"badge_self",1545,110,159,44);
            _leaderState=Label(frame,"待同步",1553,110,143,44,24,Cream,TextAlignmentOptions.Center);
            _exactLeaderState=ExactArt(frame,"leader-state",1543,108,161,47);
            _memberCount=Label(frame,"",913,162,134,34,25,Color.clear,TextAlignmentOptions.MidlineRight);
            _memberVacancyValue=Label(frame,"",973,162,30,34,25,BodyMuted,TextAlignmentOptions.Center);
            _memberVacancyValue.enableAutoSizing=true;_memberVacancyValue.fontSizeMin=16;_memberVacancyValue.fontSizeMax=25;
            ExactArt(frame,"application-title-empty",1180,164,177,56);
            _applicationTitle=Text(frame,"申请列表",1185,165,186,49,35,Cream);
            _exactApplicationTitle=ExactArt(frame,"application-title",1180,164,177,56);
            var countDot=ExactArt(frame,"application-count-empty",1367,168,50,48);countDot.name="ApplicationCountBadge";
            _applicationCount=Label(frame,"",1370,168,45,48,32,Cream,TextAlignmentOptions.Center);
            _applicationCount.enableAutoSizing=true;_applicationCount.fontSizeMin=18;_applicationCount.fontSizeMax=32;
            _applicationCount.name="TeamApplicationCount";
            _members=QdaoUguiFactory.CreateRect("TeamMembers",frame,197,201,851,516);
            _applications=QdaoUguiFactory.CreateRect("TeamApplications",frame,1110,238,619,399);
            _memberPrevious=Control(frame,"上页",638,150,83,37,()=>MovePage(TeamPage.Members,-1),size:20);_memberPrevious.name="MemberPreviousPage";
            _memberPageLabel=Label(frame,"",724,150,85,37,20,BodyMuted,TextAlignmentOptions.Center);
            _memberNext=Control(frame,"下页",812,150,83,37,()=>MovePage(TeamPage.Members,1),size:20);_memberNext.name="MemberNextPage";
            _applicationPrevious=Control(frame,"上页",1529,178,66,37,()=>MovePage(TeamPage.Applications,-1),size:18);_applicationPrevious.name="ApplicationPreviousPage";
            _applicationPageLabel=Label(frame,"",1595,178,60,37,18,BodyMuted,TextAlignmentOptions.Center);
            _applicationNext=Control(frame,"下页",1656,178,66,37,()=>MovePage(TeamPage.Applications,1),size:18);_applicationNext.name="ApplicationNextPage";
            _status=Label(frame,"",1112,667,612,45,23,BodyMuted,TextAlignmentOptions.Center);
            _exactInstruction=ExactArt(frame,"application-instruction",1276,664,283,39);
            _footerSync=Label(frame,"队伍信息待同步",288,734,258,43,23,BodyMuted);
            _exactFooterSync=ExactArt(frame,"footer-sync",284,732,174,45);
            // Added gameplay actions retain fixed slots inside the original panel's footer.
            _startMatch=Control(frame,"开始战斗",568,733,166,44,ConfirmStartMatch,true,size:23);_startMatch.name="StartTeamMatch";
            _startMatchLabel=_startMatch.GetComponentInChildren<TMP_Text>(true);
            _invite=Control(frame,"邀请道友",741,733,166,44,PromptInvite,size:23);_invite.name="InviteToTeam";
            _disband=Control(frame,"解散队伍",914,733,166,44,ConfirmDisband,size:23);_disband.name="DisbandTeam";
            _create=Control(frame,"创建队伍",914,733,166,44,()=>{if(CanCreate)CreateRequested?.Invoke();},true,size:23);_create.name="CreateTeam";
            _leave=Control(frame,"离开队伍",1087,733,166,44,ConfirmLeave,size:23);_leave.name="LeaveTeam";
            _apply=Control(frame,"申请入队",1087,733,166,44,PromptApply,size:23);_apply.name="ApplyJoinTeam";
            _refresh=Control(frame,"刷新",1538,725,181,54,()=>{if(CanAct)RefreshRequested?.Invoke();},size:27);_refresh.name="RefreshTeam";
            _refreshLabel=_refresh.GetComponentInChildren<TMP_Text>(true);
            _exactRefresh=ExactArt(_refresh.transform,"refresh",-2,0,184,54);
            _refresh.targetGraphic=_exactRefresh;
            UpdateFooter(false,false,false);
        }

        public void SetState(TeamUiState state)
        {
            _state = state;
            if (IsVisible) Render();
        }

        /// <summary>The invitation overlay owns both pointer and keyboard focus while it is visible.</summary>
        public void SetCovered(bool covered)
        {
            _frameControls.interactable = !covered;
            _frameControls.blocksRaycasts = !covered;
        }

        public void Show(TeamPage page = TeamPage.Members)
        {
            bool opening = !IsVisible;
            if (opening) _returnFocus = EventSystem.current?.currentSelectedGameObject;
            // Legacy Approved callers land on members; approval history is never rendered.
            Page = page == TeamPage.Applications ? TeamPage.Applications : TeamPage.Members;
            _root.gameObject.SetActive(true);
            Render();
            if (opening && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_close.gameObject);
        }

        public void Hide()
        {
            CloseModal();
            bool visible = IsVisible;
            _root.gameObject.SetActive(false);
            if (visible && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null;
            if (visible) Closed?.Invoke();
        }

        /// <summary>Escape: an open dialog closes first, then the window.</summary>
        public void Back()
        {
            if (IsModalOpen) CloseModal();
            else Hide();
        }

        public void ResetSession()
        {
            CloseModal();
            Hide();
            _state = null;
            MemberPageIndex = ApplicationPageIndex = 0;
            Page = TeamPage.Members;
            Clear(_members);
            Clear(_applications);
        }

        private bool CanAct => _state != null && _state.ServiceAvailable && (_ignoreBusy || !_state.IsBusy);
        private bool CanWrite => CanAct && FreshViewAvailable?.Invoke() != false;
        private bool CanJoin => CanWrite && _state.HasLoaded && !_state.HasTeam;
        private bool CanCreate => CanJoin && !_state.CreationCoolingDown;
        private bool CanManage => CanWrite && _state.IsLeader && !_state.MatchStarting;
        // 补位匹配中不锁名单(名单一变服务端就让全队退出匹配),只是不能再发起一次开战。
        private bool CanStartMatch => CanManage && !_state.MatchQueued;
        private bool CanLeave => CanWrite && _state.HasTeam && !_state.MatchStarting;
        // Invitations and rejections stay open while the roster is locked for a match.
        private bool CanInvite => CanWrite && _state.IsLeader && !_state.IsFull;
        private bool CanApprove => CanWrite && _state.IsLeader && !_state.IsFull && !_state.MatchStarting;
        private bool CanReject => CanWrite && _state.IsLeader;
        private bool NoTeam => _state != null && _state.HasLoaded && !_state.HasTeam;

        private void MovePage(TeamPage page, int delta)
        {
            Page = page;
            if (page == TeamPage.Applications) ApplicationPageIndex += delta;
            else MemberPageIndex += delta;
            Render();
        }

        private void Render()
        {
            string focusName = null;
            RectTransform focusContainer = null;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null)
            {
                if (selected.transform.IsChildOf(_members)) focusContainer = _members;
                else if (selected.transform.IsChildOf(_applications)) focusContainer = _applications;
                if (focusContainer != null) focusName = selected.name;
            }
            Clear(_members);
            Clear(_applications);
            var data = _state?.Snapshot;
            bool hasTeam = _state?.HasTeam == true;
            bool noTeam = NoTeam;
            bool leader = _state?.IsLeader == true;
            int members = data?.Members.Count ?? 0;
            int capacity = Math.Max(1, data?.Capacity ?? MaxMemberCards);
            int applications = data?.Applications.Count ?? 0;
            int invites = _state?.Invites.Count ?? 0;
            int rightItems = noTeam ? invites : applications;
            // 队员看不到申请明细,标题计数取服务器给的总数,与正文"N 条待处理申请"一致
            int applicationTotal = hasTeam && !leader ? (int)(_state.Snapshot?.ApplicationCount ?? 0) : applications;
            _summary.text=hasTeam ? $"队伍人数 {members}/{capacity}   队伍编号 {data.TeamId}"
                : noTeam ? $"尚未入队 · 玩家编号 {data.LocalPlayerId}" : "队伍信息尚未同步";
            _summaryFallbackPlate.gameObject.SetActive(!hasTeam);
            _summary.color=hasTeam?Color.clear:BodyInk;
            _teamCountValue.text=hasTeam?$"{members}/{capacity}":"";
            _teamIdValue.text=hasTeam?data.TeamId.ToString():"";
            _leaderState.text=hasTeam?(leader?"你是队长":"你是队员"):"尚未入队";
            _exactLeaderState.gameObject.SetActive(hasTeam&&leader);
            _leaderState.color=hasTeam&&leader?Color.clear:Cream;
            _footerSync.text=_state?.HasLoaded==true?"队伍信息已同步":"队伍信息待同步";
            _exactFooterSync.gameObject.SetActive(_state?.HasLoaded==true);
            _footerSync.color=_state?.HasLoaded==true?Color.clear:BodyMuted;
            _memberCount.text=$"空余 {Math.Max(0,capacity-members)} 席";
            _memberVacancyValue.text=Math.Max(0,capacity-members).ToString();
            _applicationTitle.text = noTeam ? "收到的邀请" : "申请列表";
            _exactApplicationTitle.gameObject.SetActive(!noTeam);
            _applicationTitle.color = noTeam ? Cream : Color.clear;
            _applicationCount.text = (noTeam ? invites : applicationTotal).ToString();
            _status.text = StatusText();
            bool originalHint=_status.text=="同意申请后，道友将加入你的队伍。";
            _exactInstruction.gameObject.SetActive(originalHint);
            _status.color=originalHint?Color.clear:BodyMuted;
            _refresh.interactable = CanAct;
            _refreshLabel.text = _state?.IsLoading == true ? "同步中…" : "刷新";
            _exactRefresh.gameObject.SetActive(_state?.IsLoading != true);
            _refreshLabel.color = _state?.IsLoading == true ? Ink : Color.clear;
            UpdateFooter(hasTeam, leader, noTeam);
            int memberPages = noTeam ? 1 : 1 + (Math.Max(capacity, members) - 1) / MaxMemberCards;
            int applicationPages = Math.Max(1, (rightItems + RowsPerPage - 1) / RowsPerPage);
            MemberPageIndex = Mathf.Clamp(MemberPageIndex, 0, memberPages - 1);
            ApplicationPageIndex = Mathf.Clamp(ApplicationPageIndex, 0, applicationPages - 1);
            Pagination(_memberPrevious, _memberNext, _memberPageLabel, MemberPageIndex, memberPages);
            Pagination(_applicationPrevious, _applicationNext, _applicationPageLabel, ApplicationPageIndex, applicationPages);
            if (noTeam)
            {
                RenderNoTeam();
                RenderInvites();
            }
            else
            {
                RenderMembers(capacity);
                RenderApplications();
            }
            if (focusName != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(FocusInList(focusContainer, focusName));
            else if (selected != null && !Usable(selected) && selected.transform.IsChildOf(_root) && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_close.gameObject);
            // An open dialog follows the state: it closes once its action is no longer permitted (a request
            // in flight does not count) or the local player's leadership changed, so its text never goes stale.
            if (_modal != null && (!ModalPermitted() || (_state?.IsLeader == true) != _modalLeader)) CloseModal();
            else if (_modalHint != null && _state?.IsBusy != true && _modalHint.text == BusyHint) _modalHint.text = "";
        }

        /// <summary>Rebuilt rows keep focus in the same list: same button, else its first usable one, else refresh / close.</summary>
        private GameObject FocusInList(UnityEngine.Transform list, string name)
        {
            if (list != null)
            {
                Button first = null;
                foreach (var button in list.GetComponentsInChildren<Button>())
                {
                    if (!button.interactable) continue;
                    if (button.name == name) return button.gameObject;
                    if (first == null) first = button;
                }
                if (first != null) return first.gameObject;
            }
            return _refresh.interactable ? _refresh.gameObject : _close.gameObject;
        }

        private static bool Usable(GameObject target) =>
            target != null && target.activeInHierarchy &&
            (!target.TryGetComponent(out Selectable selectable) || selectable.interactable);

        private string StatusText()
        {
            if (_state == null) return "组队服务暂不可用，请稍后再试。";
            if (!string.IsNullOrWhiteSpace(_state.Status)) return _state.Status;
            if (_state.IsBusy) return "正在同步队伍，请稍候…";
            if (!_state.ServiceAvailable) return "组队服务暂不可用，请稍后再试。";
            if (!_state.HasLoaded) return "正在连接组队服务…";
            if (!_state.HasTeam) return "尚未加入队伍：可创建队伍、申请入队或接受邀请。";
            if (_state.MatchStarting) return "队伍正在集合进入战斗，名单暂时锁定。";
            if (_state.MatchQueued)
                // 状态栏只有一行(936 宽、28 号、溢出省略),这句话要控制在 30 个全角字以内;完整规则在开战确认框和排队面板里。
                return _state.Snapshot.MatchMode == Match.MatchMode.PvpTeam
                    ? $"匹配 PVP（{_state.Snapshot.MatchTargetSize}v{_state.Snapshot.MatchTargetSize}），等待真人；任一队员可取消。"
                    : $"队伍匹配中（目标 {_state.Snapshot.MatchTargetSize} 人）：名单变动或任一队员取消即退出匹配。";
            if (!_state.IsLeader) return "你是队员，入队申请由队长处理。";
            if (_state.IsFull) return "队伍已满，可拒绝剩余申请。";
            return "同意申请后，道友将加入你的队伍。";
        }

        private void UpdateFooter(bool hasTeam, bool leader, bool noTeam)
        {
            Footer(_startMatch, leader, CanStartMatch);
            Footer(_invite, leader || (noTeam && InviteBrowseRequested != null),
                InviteBrowseRequested != null ? CanAct && _state.HasLoaded : CanInvite);
            Footer(_disband, leader, CanManage);
            Footer(_leave, hasTeam, CanLeave);
            Footer(_create, noTeam, CanCreate);
            Footer(_apply, noTeam, CanJoin);
            _startMatchLabel.text = _state?.MatchStarting == true ? "集合中…"
                : _state?.MatchQueued == true ? "匹配中…" : "开始战斗";
        }

        private static void Footer(Button button, bool visible, bool enabled)
        {
            button.gameObject.SetActive(visible);
            button.interactable = visible && enabled;
        }

        private static void Pagination(Button previous, Button next, TMP_Text label, int page, int pages)
        {
            previous.interactable = page > 0;
            next.interactable = page + 1 < pages;
            previous.gameObject.SetActive(pages > 1);
            next.gameObject.SetActive(pages > 1);
            label.gameObject.SetActive(pages > 1);
            label.text = $"{page + 1} / {pages}";
        }

        private void RenderNoTeam()
        {
            var card=Row(_members,"NoTeamCard",0,0,851,516,"application_card");
            Label(card,"你尚未加入队伍",42,155,767,61,34,BodyInk,TextAlignmentOptions.Center,bold:true);
            Label(card,"可以创建自己的队伍，或输入道友编号申请加入；收到的邀请会显示在右侧。",60,243,731,120,26,
                BodyMuted,TextAlignmentOptions.Center,wrap:true);
        }

        private void RenderMembers(int capacity)
        {
            var data=_state?.Snapshot;bool manageRows=_state?.IsLeader==true;
            int start=MemberPageIndex*MaxMemberCards;
            int visible=Math.Min(MaxMemberCards,Math.Max(capacity,data?.Members.Count??0)-start);
            for(int i=0;i<visible;i++)
            {
                int slot=start+i;var role=data!=null&&slot<data.Members.Count?data.Members[slot]:null;
                bool self=role!=null&&role.PlayerId==data.LocalPlayerId;
                bool highlighted=role!=null&&_state.HighlightPlayerId!=0&&role.PlayerId==_state.HighlightPlayerId;
                float[] rowY={0,108,216,323,423}; float[] rowHeight={100,101,102,94,91};
                var row=ExactRow(_members,"TeamMemberSlot_"+slot,0,rowY[i],851,rowHeight[i],"member-row-"+i,
                    highlighted?QdaoUguiTheme.Html("#F6D9D2"):Color.white);
                if(role==null)
                {
                    ExactArt(row,i==4?"empty-row-4":"empty-row-3",0,0,851,rowHeight[i]);
                    Label(row,"空余席位",175,8,430,44,29,Color.clear);
                    Label(row,"等待道友加入",175,56,430,34,23,Color.clear);
                    continue;
                }
                float[] portraitX={41,42,41,44,44};float[] portraitY={0,0,-2,3,1};float[] portraitSize={105,103,104,88,88};
                Portrait(row,role,portraitX[i],portraitY[i],portraitSize[i]);
                Label(row,DisplayName(role),173,9,236,49,40,bold:true).name="TeamName_"+role.PlayerId;
                Label(row,Level(role)+" · "+School(role),173,56,236,35,26,BodyInk).name="TeamLevel_"+role.PlayerId;
                bool leader=role.PlayerId==data.LeaderId;
                ExactArt(row,leader?"role-leader":"role-member",410,28,122,44);
                Label(row,leader?"队长":"队员",421,30,102,42,26,Color.clear,TextAlignmentOptions.Center);
                if(self)
                {
                    ExactArt(row,"self-state",545,29,88,42);
                    Label(row,"自己",555,31,74,40,23,Color.clear,TextAlignmentOptions.Center);
                }
                else if(manageRows)
                {
                    ulong id=role.PlayerId;
                    bool transferring=_state.PendingAction==TeamAction.Transfer&&_state.PendingTarget==id;
                    bool kicking=_state.PendingAction==TeamAction.Kick&&_state.PendingTarget==id;
                    var transfer=Control(row,transferring?"处理中":"转让",542,32,76,40,()=>ConfirmTransfer(id),
                        enabled:CanManage&&role.IsOnline,size:transferring?16:21);transfer.name="Transfer_"+id;
                    var kick=Control(row,kicking?"处理中":"请离",624,32,76,40,()=>ConfirmKick(id),
                        enabled:CanManage,size:kicking?16:21);kick.name="Kick_"+id;
                }
                bool originalOnlineState=role.OnlineStatusKnown&&(!role.IsOnline||!role.InBattle);
                if(originalOnlineState)ExactArt(row,role.IsOnline?"status-online":"status-offline",702,29,138,46);
                else if(role.OnlineStatusKnown)Art(row,role.IsOnline?"status_online":"status_offline",710,38,23,23,true);
                Label(row,!role.OnlineStatusKnown?"待同步":role.IsOnline?(role.InBattle?"战斗中":"在线"):"离线",
                    741,27,95,45,25,originalOnlineState?Color.clear:role.IsOnline?BodyInk:BodyMuted,TextAlignmentOptions.MidlineRight);
            }
        }

        private void RenderApplications()
        {
            var list=_state?.Snapshot?.Applications;
            if(list==null||list.Count==0)
            {
                bool member=_state!=null&&_state.HasTeam&&!_state.IsLeader;
                Row(_applications,"EmptyApplications",0,0,619,399,"application_card");
                string title=member?"入队申请由队长处理":_state?.IsBusy==true?"正在同步申请…":"暂无入队申请";
                Label(_applications,title,25,120,569,62,33,BodyInk,TextAlignmentOptions.Center,bold:true);
                string detail=member?$"队伍当前有 {_state.Snapshot.ApplicationCount} 条待处理申请。":_state?.ServiceAvailable==true
                    ?"收到申请后，可在这里查看道友资料。":"组队开放后，可在这里查看并处理申请。";
                Label(_applications,detail,52,206,515,93,25,BodyMuted,TextAlignmentOptions.Center,wrap:true);return;
            }
            int start=ApplicationPageIndex*RowsPerPage,count=Math.Min(RowsPerPage,list.Count-start);bool compact=count>2;
            for(int i=0;i<count;i++)
            {
                var role=list[start+i];if(role==null)continue;
                var row=compact?Row(_applications,"TeamRow_"+role.PlayerId,0,i*101,619,94,"application_card")
                    :ExactRow(_applications,"TeamRow_"+role.PlayerId,0,i*204,619,194,"application-card-"+i,Color.white);
                Portrait(row,role,compact?14:19,compact?9:8,compact?76:130);
                Label(row,DisplayName(role),compact?103:168,compact?5:21,compact?208:420,compact?41:50,
                    compact?27:42,bold:!compact).name="TeamName_"+role.PlayerId;
                Label(row,Level(role)+" · "+School(role),compact?103:168,compact?49:77,compact?208:420,compact?34:41,
                    compact?20:28,BodyInk).name="TeamDetails_"+role.PlayerId;
                bool pending=_state?.PendingPlayerId==role.PlayerId;float y=compact?25:121;
                var reject=Control(row,"拒绝",compact?326:131,y,compact?123:219,compact?50:60,
                    ()=>Decide(role.PlayerId,false),enabled:CanReject,size:compact?22:30);reject.name="Reject_"+role.PlayerId;
                var approve=Control(row,pending?"处理中":"同意",compact?459:363,y,compact?138:224,compact?50:60,
                    ()=>Decide(role.PlayerId,true),primary:true,enabled:CanApprove,size:pending?23:compact?22:30);approve.name="Approve_"+role.PlayerId;
            }
        }

        private void RenderInvites()
        {
            var list = _state?.Invites;
            if (list == null || list.Count == 0)
            {
                Row(_applications, "EmptyInvites", 0, 0, 619, 399, "application_card");
                Label(_applications, "暂无组队邀请", 25, 120, 569, 62, 33, BodyInk, TextAlignmentOptions.Center, bold: true);
                Label(_applications, "道友向你发出邀请后，会显示在这里；邀请 60 秒内有效。", 52, 206, 515, 93, 25,
                    BodyMuted, TextAlignmentOptions.Center, wrap: true);
                return;
            }
            int start = ApplicationPageIndex * RowsPerPage;
            int count = Math.Min(RowsPerPage, list.Count - start);
            bool compact = count > 2;
            for (int i = 0; i < count; i++)
            {
                var invite = list[start + i];
                if (invite == null) continue;
                ulong teamId = invite.TeamId;
                var inviter = invite.Inviter ?? new TeamRole { PlayerId = invite.LeaderId };
                var row = Row(_applications, "TeamInvite_" + teamId, 0, i * (compact ? 101 : 204), 619,
                    compact ? 94 : 194, "application_card");
                Portrait(row, inviter, compact ? 14 : 19, compact ? 9 : 8, compact ? 76 : 130);
                Label(row, $"{DisplayName(inviter)} 邀请你入队", compact ? 103 : 168, compact ? 5 : 21,
                    compact ? 208 : 420, 50, compact ? 27 : 33, bold: true).name = "TeamInviteName_" + teamId;
                Label(row, $"队伍编号 {teamId}  ·  {invite.MemberCount} / 5 人", compact ? 103 : 168, compact ? 49 : 77,
                    compact ? 208 : 420, 41, compact ? 20 : 25, BodyMuted).name = "TeamInviteDetails_" + teamId;
                bool pending = _state.PendingAction == TeamAction.RespondInvite && _state.PendingTarget == teamId;
                float y = compact ? 25 : 122;
                var reject = Control(row, "谢绝", compact ? 326 : 135, y, compact ? 123 : 214,
                    compact ? 50 : 57, () => RespondInvite(teamId, false), enabled: CanJoin, size: compact ? 22 : 30);
                reject.name = "RejectInvite_" + teamId;
                var accept = Control(row, pending ? "处理中" : "加入", compact ? 459 : 365, y, compact ? 138 : 214,
                    compact ? 50 : 57, () => RespondInvite(teamId, true), primary: true,
                    enabled: CanJoin, size: pending ? 27 : compact ? 22 : 30);
                accept.name = "AcceptInvite_" + teamId;
            }
        }

        private void Decide(ulong playerId, bool approve)
        {
            if (approve ? !CanApprove : !CanReject) return;
            foreach (var role in _state.Snapshot.Applications)
                if (role != null && role.PlayerId == playerId) { DecisionRequested?.Invoke(playerId, approve); return; }
        }

        private void RespondInvite(ulong teamId, bool accept)
        {
            if (!CanJoin) return;
            foreach (var invite in _state.Invites)
                if (invite != null && invite.TeamId == teamId) { InviteResponseRequested?.Invoke(teamId, accept); return; }
        }

        private void PromptApply()
        {
            if (!CanJoin) return;
            TargetPrompt("申请入队", "输入对方的玩家编号，申请加入其所在的队伍。", "提交申请",
                () => CanJoin, id => ApplyRequested?.Invoke(id));
        }

        private void PromptInvite()
        {
            if (InviteBrowseRequested != null)
            {
                if (CanAct && _state.HasLoaded) InviteBrowseRequested.Invoke();
                return;
            }
            if (!CanInvite) return;
            TargetPrompt("邀请道友", "输入道友的玩家编号，对方同意后即可入队；邀请 60 秒内有效。", "发出邀请",
                () => CanInvite, id => InviteRequested?.Invoke(id));
        }

        private void ConfirmLeave() => RequestLeaveConfirmation();

        /// <summary>轻量HUD复用原有离队确认；确认意图只属于打开弹窗时的那一支队伍。</summary>
        public void RequestLeaveConfirmation(Func<bool> permission = null)
        {
            if (!CanLeave || permission?.Invoke() == false) return;
            ulong expectedTeamId = _state.Snapshot.TeamId;
            ulong expectedMembershipEpoch = _state.Snapshot.MembershipEpoch;
            Confirm("离开队伍", _state.IsLeader ? "你是队长，离队后队长将自动转给在线队员。确定离开吗？" : "确定离开当前队伍吗？",
                () => CanLeave && _state.Snapshot.TeamId == expectedTeamId &&
                    _state.Snapshot.MembershipEpoch == expectedMembershipEpoch && permission?.Invoke() != false,
                () => LeaveRequested?.Invoke());
        }

        private void ConfirmDisband()
        {
            if (!CanManage) return;
            Confirm("解散队伍", "解散后所有队员都将离开队伍，确定解散吗？",
                () => CanManage, () => DisbandRequested?.Invoke());
        }

        private const string DirectStartDescription = "按当前队伍人数立即进入 PVE 副本，无需凑满 5 人。全队一起入场，集合期间名单锁定。确定开始吗？";

        private static string BackfillDescription(uint target) =>
            $"全队进入匹配队列，凑满 {target} 人后一同进入副本（怪物数量随人数增加）。匹配期间名单变动或任一队员取消，全队都会退出匹配。";

        /// <summary>目标人数按钮的文案:当前人数那一档是直接开战,更大的档是匹配补位;选中的带「√」。</summary>
        public static string StartTargetLabel(int size, int memberCount, bool selected) =>
            (selected ? "√" : "") + (size == memberCount ? $"现有{size}人" : $"凑满{size}人");

        /// <summary>
        /// 开战确认框。队伍没满副本上限时多一排目标人数:当前人数 = 直接开战(StartMatchRequested),
        /// 更大 = 整队进人数档队列等补位(BackfillRequested)。默认选直接开战,与加这排按钮之前的行为一致。
        /// </summary>
        private void ConfirmStartMatch()
        {
            if (!CanStartMatch) return;
            int memberCount = _state.Snapshot.Members.Count;
            // 人数是这个确认框的前提(选项、说明、确认后的行为都按打开时的人数算):名单一变就不再允许,
            // Render 会把框关掉(与队长变更同一机制),确认时也会复核。
            bool StillAllowed() => CanStartMatch && _state.Snapshot.Members.Count == memberCount;
            Confirm("开始 PVE 战斗", DirectStartDescription, StillAllowed, () => StartMatchRequested?.Invoke());
        }

        private void ConfirmTransfer(ulong playerId)
        {
            var role = FindMember(playerId);
            if (!CanManage || role == null || !role.IsOnline || role.PlayerId == _state.Snapshot.LocalPlayerId) return;
            Confirm("转让队长", $"确定将队长转让给 {DisplayName(role)} 吗？",
                () => CanManage && FindMember(playerId)?.IsOnline == true, () => TransferRequested?.Invoke(playerId));
        }

        private void ConfirmKick(ulong playerId)
        {
            var role = FindMember(playerId);
            if (!CanManage || role == null || role.PlayerId == _state.Snapshot.LocalPlayerId) return;
            Confirm("请离队员", $"确定将 {DisplayName(role)} 请离队伍吗？",
                () => CanManage && FindMember(playerId) != null, () => KickRequested?.Invoke(playerId));
        }

        private TeamRole FindMember(ulong playerId)
        {
            var members = _state?.Snapshot?.Members;
            if (members == null || playerId == 0) return null;
            foreach (var role in members)
                if (role != null && role.PlayerId == playerId) return role;
            return null;
        }

        private RectTransform Modal(string title, string description, Func<bool> allowed)
        {
            CloseModal();
            _modalReturnFocus = EventSystem.current?.currentSelectedGameObject;
            _modalReturnName = _modalReturnFocus != null ? _modalReturnFocus.name : null;
            _modalReturnList = _modalReturnFocus == null ? null
                : _modalReturnFocus.transform.IsChildOf(_members) ? _members
                : _modalReturnFocus.transform.IsChildOf(_applications) ? _applications : null;
            _modalAllowed = allowed;
            _modalLeader = _state?.IsLeader == true;
            // Dialogs hang off the window root, so Render never destroys them or their typed text.
            _modal = QdaoUguiFactory.CreateStretch("TeamModal", _root, Vector4.zero);
            var dim = _modal.gameObject.AddComponent<Image>();
            dim.color = new Color(.03f, .10f, .08f, .74f);
            dim.raycastTarget = true;
            var frame = QdaoUguiFactory.CreateCenteredRect("TeamModalFrame", _modal, 1100, 560);
            Art(frame, "application_card", 0, 0, 1100, 560);
            Label(frame, title, 66, 44, 968, 72, 42, bold: true).name = "TeamModalTitle";
            Label(frame, description, 66, 128, 968, 110, 29, BodyMuted, wrap: true).name = "TeamModalDescription";
            return frame;
        }

        /// <summary>The permission is re-checked on confirm; the state may have changed while the dialog was open.</summary>
        private void Confirm(string title, string description, Func<bool> allowed, Action action)
        {
            var frame = Modal(title, description, allowed);
            _modalHint = Label(frame, "", 66, 356, 968, 44, 25, ErrorInk);
            _modalHint.name = "TeamModalError";
            var cancel = Control(frame, "取消", 66, 440, 300, 82, CloseModal);
            cancel.name = "TeamModalCancel";
            var confirm = Control(frame, "确认", 692, 440, 340, 82, () => SubmitModal(action), primary: true);
            confirm.name = "TeamModalConfirm";
            EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
        }

        /// <summary>
        /// Emits only when still permitted. A request in flight (a silent refresh counts) keeps the dialog
        /// open with a hint instead of dropping the action; a lost permission closes it without emitting.
        /// </summary>
        private void SubmitModal(Action action)
        {
            if (_modalAllowed?.Invoke() == true)
            {
                CloseModal();
                action();
                return;
            }
            if (_state != null && _state.IsBusy && ModalPermitted())
            {
                if (_modalHint != null) _modalHint.text = BusyHint;
                return;
            }
            CloseModal();
        }

        /// <summary>The open dialog's permission with any in-flight request ignored.</summary>
        private bool ModalPermitted()
        {
            if (_modalAllowed == null) return false;
            _ignoreBusy = true;
            try { return _modalAllowed(); }
            finally { _ignoreBusy = false; }
        }

        private void TargetPrompt(string title, string description, string confirmLabel, Func<bool> allowed,
            Action<ulong> submit)
        {
            var frame = Modal(title, description, allowed);
            Solid(frame, "TeamTargetInputBorder", 149, 269, 802, 78, Border);
            Solid(frame, "TeamTargetInputPaper", 150, 270, 800, 76, QdaoUguiTheme.Html("#FFF8E9"));
            var input = QdaoUguiFactory.CreateInputField("TeamTargetInput", frame, 150, 270, 800, 76, "输入对方的玩家编号", 20);
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.textComponent.font = BodyFont();
            input.textComponent.fontSize = 30;
            input.textComponent.richText = false;
            input.textComponent.color = BodyInk;
            input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            if (input.placeholder is TMP_Text hint)
            {
                hint.font = BodyFont();
                hint.fontSize = 27;
                hint.color = BodyMuted;
                hint.richText = false;
            }
            var error = Label(frame, "", 150, 356, 800, 44, 25, ErrorInk);
            error.name = "TeamModalError";
            _modalHint = error;
            var cancel = Control(frame, "取消", 66, 440, 300, 82, CloseModal);
            cancel.name = "TeamModalCancel";
            var confirm = Control(frame, confirmLabel, 692, 440, 340, 82, () =>
            {
                if (!ulong.TryParse(input.text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) || id == 0)
                {
                    error.text = "请输入正确的玩家编号（纯数字）。";
                    return;
                }
                if (_state != null && id == _state.Snapshot.LocalPlayerId)
                {
                    error.text = "不能填写自己的编号。";
                    return;
                }
                SubmitModal(() => submit(id));
            }, primary: true);
            confirm.name = "TeamModalConfirm";
            EventSystem.current?.SetSelectedGameObject(input.gameObject);
        }

        private void CloseModal()
        {
            if (_modal == null) return;
            _modal.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_modal.gameObject);
            else UnityEngine.Object.DestroyImmediate(_modal.gameObject);
            _modal = null;
            _modalAllowed = null;
            _modalHint = null;
            // A row opener is destroyed by any Render while the dialog is open: fall back to the button of
            // the same name in its list, then the list's first usable one, then refresh / close (as Render).
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(Usable(_modalReturnFocus) ? _modalReturnFocus
                    : FocusInList(_modalReturnList, _modalReturnName));
            _modalReturnFocus = null;
            _modalReturnName = null;
            _modalReturnList = null;
        }

        private static void Portrait(UnityEngine.Transform parent, TeamRole role, float x, float y, float size)
        {
            string id = role.CharacterId;
            // This marker is issued only by the explicit offline capture fixture, never inferred from a player id.
            if(!string.IsNullOrEmpty(id)&&id.StartsWith(TeamUiArt.PreviewPortraitPrefix,StringComparison.Ordinal))
            {
                string key="preview_portrait_"+id.Substring(TeamUiArt.PreviewPortraitPrefix.Length);
                var sample=Art(parent,key,x,y,size,size,true);sample.name="TeamPortrait_"+role.PlayerId;return;
            }
            if (string.IsNullOrEmpty(id) && role.ClassId >= 1 && role.ClassId <= 4 && (role.Gender == 1 || role.Gender == 2))
                id = QdaoCharacterCatalog.ResolveRole(role.ClassId, role.Gender);
            var sprite = QdaoCharacterCatalog.LoadPortrait(id);
            var mask = QdaoUguiFactory.CreateImage("PortraitMask", parent, x, y, size, size,
                QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = QdaoUguiFactory.CreateImage("TeamPortrait_" + role.PlayerId, mask.transform,
                -size * .5f, -size * .07f, size * 2, size * 2, sprite);
            portrait.preserveAspect = true;
            portrait.gameObject.SetActive(sprite != null);
            Art(parent, "portrait_frame", x, y, size, size);
            if (sprite == null) Label(parent, "待同步", x + 3, y + size * .3f, size - 6, size * .4f,
                20, BodyMuted, TextAlignmentOptions.Center);
        }

        private static RectTransform Row(UnityEngine.Transform parent, string name, float x, float y, float w, float h,
            string key, Color? tint = null)
        {
            var row = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var paper = Art(row, key, 0, 0, w, h);
            paper.color = tint ?? Color.white;
            return row;
        }

        private static void Solid(UnityEngine.Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null);
            image.color = color;
        }

        private static TMP_FontAsset BodyFont()
        {
            // The approved artwork uses a Chinese calligraphic serif; match it in native labels.
            return _bodyFont!=null?_bodyFont:(_bodyFont=QdaoUguiTheme.ResolveFont());
        }

        private static TextMeshProUGUI Label(UnityEngine.Transform parent, string value, float x, float y, float w, float h,
            float size, Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
            bool bold = false, bool wrap = false)
        {
            // Noto CJK line metrics exceed the old KaiTi boxes; Ellipsis otherwise hides a whole line.
            h = Mathf.Max(h, size * 1.18f);
            var label = Text(parent, value, x, y, w, h, size, color ?? BodyInk, alignment: alignment);
            label.font = BodyFont();
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            return label;
        }

        private static Button Control(UnityEngine.Transform parent, string value, float x, float y, float w, float h,
            Action click, bool primary = false, bool enabled = true, string key = null, float size = 30)
        {
            var button = Button(parent, value, x, y, w, h, click, primary, enabled, key, size);
            var caption = button.GetComponentInChildren<TMP_Text>(true);
            caption.font = BodyFont();
            // Only approved fixed application actions use baked source captions. Pending labels remain native.
            if (w >= 200 && w <= 230 && h >= 50 && h <= 60 && (value == "拒绝" || value == "同意"))
            {
                button.image.sprite = QdaoUguiTheme.RequireSprite("UI/Ugui/TeamExact20261017/" + (value == "拒绝" ? "reject" : "approve"));
                button.image.type = Image.Type.Simple;
                caption.color = Color.clear;
            }
            return button;
        }

        private static RectTransform ExactRow(UnityEngine.Transform parent,string name,float x,float y,float w,float h,string key,Color tint)
        {
            var row=QdaoUguiFactory.CreateRect(name,parent,x,y,w,h);
            ExactArt(row,key,0,0,w,h).color=tint;return row;
        }

        private static Image ExactArt(UnityEngine.Transform parent, string key, float x, float y, float w, float h)
        {
            var image = QdaoUguiFactory.CreateImage("Exact_" + key, parent, x, y, w, h,
                QdaoUguiTheme.RequireSprite("UI/Ugui/TeamExact20261017/" + key));
            image.type = Image.Type.Simple; image.raycastTarget = false;
            return image;
        }

        private static string DisplayName(TeamRole role) => TeamViewMapper.DisplayName(role);
        private static string Level(TeamRole role) => role.Level > 0 ? $"{role.Level} 级" : "等级待同步";
        private static string School(TeamRole role) => !string.IsNullOrWhiteSpace(role.SchoolName) ? role.SchoolName
            : role.ClassId switch { 1 => "破军", 2 => "玄霄", 3 => "丹心", 4 => "逐风", _ => "门派待同步" };
    }
}
