using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.EquipUiStyle;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public enum GameplayPage { Bag, Missions, Activities }

    /// <summary>Production uGUI views; snapshots also allow isolated editor verification.</summary>
    public sealed partial class GameplayWindow : IDisposable
    {
        public event Action<uint> BagRequested;
        public event Action SortRequested;
        /// <summary>打开 / 刷新背包页时与 <see cref="BagRequested"/> 一起发:装备栏是另一份快照(bag_type = 2)。</summary>
        public event Action EquipmentRequested;
        /// <summary>参数是物品实例 id:穿上人物背包里的装备 / 卸下装备栏里的装备。</summary>
        public event Action<ulong> EquipRequested;
        public event Action<ulong> UnequipRequested;
        public event Action<BagItemInfo> EquipmentShareRequested;
        public event Action AttributesRequested;
        public event Action MissionsRequested;
        public event Action ActivitiesRequested;
        public event Action<uint, uint> MissionAcceptRequested;
        public event Action<uint, uint> MissionClaimRequested;
        public event Action<PlayerMissionInfo> TrackingChanged;
        public event Action Closed;
        public bool IsVisible => _root.gameObject.activeSelf;
        public bool HasEquipmentModal => _actions.IsVisible || _wish.IsVisible || (_forgeDialog?.IsVisible ?? false);
        public bool EquipmentModalOwnsKeyboard => HasEquipmentModal || _modalClosedFrame == Time.frameCount;
        public GameplayPage Page { get; private set; }
        private readonly RectTransform _root, _body;
        private readonly TextMeshProUGUI _title, _subtitle;
        private readonly Button[] _tabs = new Button[3];
        private readonly TMP_InputField _search;
        private readonly EquipTooltip _tooltip;
        private readonly EquipActionsDialog _actions;
        private readonly EquipWishDialog _wish;
        private readonly EquipForgeUiState _forge;
        private readonly EquipForgeDialog _forgeDialog;
        private readonly CanvasGroup _frameControls;
        private ulong _modalItemId;
        private string _equipmentNotice;
        private int _modalClosedFrame = -1;
        private BagInfo _bag, _equipment;
        private GetMissionListResponse _missions;
        private GetActivityListResponse _activities;
        private bool _bagLoading, _missionLoading, _activityLoading, _sorting, _missionActionBusy;
        private bool _equipLoading, _equipBusy;
        private string _bagError, _missionError, _activityError, _equipError, _query = "";
        private int _objectivePage;
        private float _missionObjectiveScroll = 1;
        private int _bagFilter, _missionFilter, _activityFilter, _bagPage, _missionPage, _activityPage, _equipPage;
        private uint _bagType, _selectedActivity, _characterLevel;
        private ulong _selectedMission;
        private MissionTrackingState _missionTracking = new MissionTrackingState();
        private bool _externalMissionTracking;
        // 玩家点过的物品实例 id(人物背包或装备栏里的都算);它此刻在哪个包里每次重画现查。
        private ulong _selectedItem;

        public GameplayWindow(UnityEngine.Transform parent, EquipForgeUiState forge = null, ActivityPreferences activityPreferences = null)
        {
            _activityPreferences = activityPreferences ?? ActivityPreferences.CreateLocal();
            _root = QdaoUguiFactory.CreateStretch("PlayerFeaturesWindow", parent, Vector4.zero);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f); shade.raycastTarget = true;
            _root.gameObject.AddComponent<MmorpgClient.World.Tianyong.GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("NativeGameplayDesign", _root, NativeWidth, NativeHeight);
            frame.gameObject.AddComponent<GameplayDesignFit>();
            _frameControls = frame.gameObject.AddComponent<CanvasGroup>();
            _nativeFrame = QdaoUguiFactory.CreateImage("PageArtwork", frame, 0, 0, NativeWidth, NativeHeight, NativeSprite("inventory-frame"));
            _title = NativeText(frame, "背 包", 755, 27, 420, 67, 52, Cream, TextAlignmentOptions.Center);
            _title.enabled = false;
            _nativeTitle = NativeArt(frame, "original-title-bag", 755, 27, 420, 67);
            _nativeTitle.name = "OriginalPageTitle"; _nativeTitle.raycastTarget = false;
            _subtitle = NativeText(frame, "", 0, 0, 1, 1, 1); _subtitle.gameObject.SetActive(false);
            NativeHit(frame, "CloseWindow", "", 1779, 24, 84, 80, Hide);
            _body = QdaoUguiFactory.CreateRect("PageContent", frame, 0, 0, NativeWidth, NativeHeight);
            _search = QdaoUguiFactory.CreateInputField("BagSearch", frame, 1260, 260, 405, 35,
                "搜索名称或编号", 80, NativeSprite("bag-tab-normal"));
            _search.textComponent.color = Ink; _search.textComponent.fontSize = 22;
            ((TMP_Text)_search.placeholder).color = Muted;
            _search.onValueChanged.AddListener(value => { _query = value; _bagPage = 0; _bagScroll = 1; if (Page == GameplayPage.Bag) Render(); });
            _tooltip = new EquipTooltip(frame, 1255, 145); _tooltip.Hide();
            _actions = new EquipActionsDialog(_root); _wish = new EquipWishDialog(_root);
            _forge = forge; _forgeDialog = forge == null ? null : new EquipForgeDialog(_root, forge);
            Hide();
        }

        public void Show(GameplayPage page)
        {
            CloseEquipmentModals(); _equipmentNotice = null;
            Page = page; _root.gameObject.SetActive(true);
            _missionFilterMenu = false;
            _nativeFrame.sprite = NativeSprite(page == GameplayPage.Bag ? "inventory-frame" : page == GameplayPage.Missions ? "quests-frame" : "activities-frame");
            _title.text = page == GameplayPage.Bag ? "背 包" : page == GameplayPage.Missions ? "任 务" : "活 动";
            _nativeTitle.sprite = NativeSprite(page == GameplayPage.Bag ? "original-title-bag" : page == GameplayPage.Missions ? "original-title-missions" : "original-title-activities");
            _search.gameObject.SetActive(false); _bagTools = _activityTools = false; _activityDetails = 0;
            if (page == GameplayPage.Missions && _missionFilter == 0 && _missions?.Missions.Count > 0 &&
                _missions.Missions.All(m => (int)m.Status == 0)) _missionFilter = 3;
            Render(); Refresh();
        }

        public void Hide() { CloseEquipmentModals(); _tooltip.Hide(); _root.gameObject.SetActive(false); Closed?.Invoke(); }
        public void Dispose() { _missionTracking.OnChanged -= MissionTrackingChanged; if (_root != null) CloseEquipmentModals(); _forgeDialog?.Dispose(); }
        public void Back()
        {
            if (HasEquipmentModal) CloseEquipmentModals();
            else if (Page == GameplayPage.Activities && (_activityClaimConfirmation != 0 || _activityUtility != 0 || _activityDetails != 0 || _activityTools))
            { _activityClaimConfirmation = _activityDetails = 0; _activityUtility = 0; _activityTools = false; Render(); }
            else Hide();
        }
        /// <summary>从人物面板进入时直接筛选装备，仍由正式背包读接口提供数据。</summary>
        public void ShowEquipment()
        {
            _bagType = 0; _bagFilter = 1; _bagPage = 0; _query = string.Empty;
            _search.SetTextWithoutNotify(string.Empty); _search.DeactivateInputField();
            Show(GameplayPage.Bag);
        }
        public void SetEquipmentNotice(string message)
        { _equipmentNotice = message; if (IsVisible && Page == GameplayPage.Bag) Render(); }
        public void Refresh()
        {
            _equipmentNotice = null;
            if (Page == GameplayPage.Bag) { BagRequested?.Invoke(_bagType); EquipmentRequested?.Invoke(); }
            else if (Page == GameplayPage.Missions) MissionsRequested?.Invoke();
            else { ActivitiesRequested?.Invoke(); MissionsRequested?.Invoke(); }
        }
        public void SetBag(BagInfo bag, bool loading, string error, bool sorting)
        { _bag = bag; _bagLoading = loading; _bagError = error; _sorting = sorting; if (IsVisible && Page == GameplayPage.Bag) Render(); }
        /// <summary>装备栏快照(bag_type = 2)。busy = 穿上 / 卸下在途;error 原样显示在背包页的状态行。</summary>
        public void SetEquipment(BagInfo equipment, bool loading, string error, bool busy)
        { _equipment = equipment; _equipLoading = loading; _equipError = error; _equipBusy = busy; if (IsVisible && Page == GameplayPage.Bag) Render(); }
        /// <summary>
        /// 人物背包与装备栏一起换,只重画一次。数据层每次通知都同时带着两份(见 GameplayUiRoot.Changed):
        /// 先后各调一次 <see cref="SetEquipment"/> / <see cref="SetBag"/> 会把整页重建两遍。
        /// </summary>
        public void SetBagPage(BagInfo bag, bool bagLoading, string bagError, bool sorting,
            BagInfo equipment, bool equipLoading, string equipError, bool equipBusy)
        {
            _equipment = equipment; _equipLoading = equipLoading; _equipError = equipError; _equipBusy = equipBusy;
            SetBag(bag, bagLoading, bagError, sorting);
        }
        /// <summary>角色等级(0 = 未知),只用来给 tooltip 的等级要求标红;能不能穿由服务器裁决。</summary>
        public void SetCharacterLevel(uint level)
        { if (_characterLevel == level) return; _characterLevel = level; if (IsVisible && Page == GameplayPage.Bag) Render(); }
        public void SetMissions(GetMissionListResponse missions, bool loading, string error, bool actionBusy = false)
        {
            _missions = missions;
            if (!_externalMissionTracking)
            {
                ulong previous = _missionTracking.CurrentKey;
                _missionTracking.UpdateSnapshot(missions);
                // 保留独立大窗原有清理通知；null是通知，07不再把它当作再次取消另一项的意图。
                if (previous != 0 && !_missionTracking.IsTracked(previous)) TrackingChanged?.Invoke(null);
            }
            _missionLoading = loading; _missionError = error; _missionActionBusy = actionBusy;
            if (IsVisible && (Page == GameplayPage.Missions || Page == GameplayPage.Activities)) Render();
        }
        /// <summary>大窗、活动与HUD共用当前Client的显示偏好；换绑前解除旧订阅。</summary>
        public void SetMissionTracking(MissionTrackingState tracking)
        {
            if (ReferenceEquals(_missionTracking, tracking)) return;
            _missionTracking.OnChanged -= MissionTrackingChanged;
            _externalMissionTracking = tracking != null;
            _missionTracking = tracking ?? new MissionTrackingState();
            if (!_externalMissionTracking) _missionTracking.UpdateSnapshot(_missions);
            _missionTracking.OnChanged += MissionTrackingChanged;
            MissionTrackingChanged();
        }
        private void MissionTrackingChanged()
        { if (IsVisible && Page == GameplayPage.Missions) Render(); }
        public void TrackMissionFromEntry(PlayerMissionInfo mission)
        { if (mission != null) { _missionTracking.Track(mission); TrackingChanged?.Invoke(mission); } }
        public void SetActivities(GetActivityListResponse activities, bool loading, string error,
            bool rewardBusy = false, string rewardError = null)
        {
            _activities = activities; _activityLoading = loading; _activityError = error;
            _activityRewardBusy = rewardBusy; _activityRewardError = rewardError ?? string.Empty;
            if (_activityDetails != 0 && (activities == null || !activities.Activities.Any(a => a.ActivityId == _activityDetails)))
                _activityDetails = 0;
            if (IsVisible && Page == GameplayPage.Activities) Render();
        }
        public void ResetSession()
        {
            SetActivityContext(0);
            _activityCategory = -1; _activityRewardFilter = 0; _activityClaimConfirmation = 0; _activityUtility = 0;
            _activityRewardBusy = false; _activityRewardError = string.Empty;
            _bag = null; _equipment = null; _missions = null; _activities = null;
            if (!_externalMissionTracking) _missionTracking.UpdateSnapshot(null);
            _selectedItem = 0; _selectedMission = 0; _selectedActivity = 0;
            _bagPage = _missionPage = _activityPage = _objectivePage = _equipPage = 0;
            _bagFilter = _missionFilter = _activityFilter = 0; _query = string.Empty; _bagTools = _activityTools = false;
            _bagScroll = _missionScroll = _activityScroll = 1;
            _missionObjectiveScroll = 1;
            _search.SetTextWithoutNotify(string.Empty); _search.DeactivateInputField();
            _bagLoading = _missionLoading = _activityLoading = _sorting = _missionActionBusy = false;
            _equipLoading = _equipBusy = false; _characterLevel = 0;
            _equipmentNotice = null;
            _bagError = _missionError = _activityError = _equipError = string.Empty;
            TrackingChanged?.Invoke(null); Hide();
        }

        private void Render()
        {
            if (HasEquipmentModal && (Page != GameplayPage.Bag || FindEquipment(_modalItemId) == null)) CloseEquipmentModals();
            _tooltip.Hide(); Clear(_body);
            if (Page == GameplayPage.Bag) RenderBag();
            else if (Page == GameplayPage.Missions) RenderMissions();
            else RenderActivities();
        }
        private void Notice(string value, float x = 420, float y = 244, float width = 1500)
        {
            Art(_body, "flower", x + width / 2 - 58, y - 102, 116, 85, true);
            Text(_body, value, x, y, width, 110, 34, Muted, alignment: TextAlignmentOptions.Center);
        }
        private void StateLine(string error, bool loading, float x, float y, float width)
        {
            string value = !string.IsNullOrEmpty(error) ? "暂时未能同步，请点击刷新重试" : loading ? "正在同步…" : "";
            Text(_body, value, x, y, width, 36, 25, !string.IsNullOrEmpty(error) ? QdaoUguiTheme.Html("#9A442D") : Muted);
        }
        private void Pager(int page, int count, int size, float x, float y, Action<int> change)
        {
            int pages = Math.Max(1, (count + size - 1) / size);
            Button(_body, "上一页", x, y, 164, 56, () => change(page - 1), enabled: page > 0, fontSize: 26);
            Text(_body, $"{page + 1} / {pages}", x + 176, y, 114, 56, 27, Muted, alignment: TextAlignmentOptions.Center);
            Button(_body, "下一页", x + 302, y, 164, 56, () => change(page + 1), enabled: page + 1 < pages, fontSize: 26);
        }



        /// <summary>
        /// 背包页的状态行。行囊自身的读取状态优先(沿用通用提示);其次是穿脱 / 装备栏的失败文案,
        /// 原样显示数据层给的原因(与任务页显示领奖失败原因同一做法)。返回是否写了提示。
        /// </summary>
        private bool BagStateLine(float x, float y, float width)
        {
            if (!string.IsNullOrEmpty(_bagError) || _bagLoading) { StateLine(_bagError, _bagLoading, x, y, width); return true; }
            if (!string.IsNullOrEmpty(_equipError)) { Text(_body, _equipError, x, y, width, 36, 25, QdaoUguiTheme.Html("#9A442D")); return true; }
            if (_equipBusy) { Text(_body, "正在处理装备…", x, y, width, 36, 25, Muted); return true; }
            if (!string.IsNullOrEmpty(_equipmentNotice)) { Text(_body, _equipmentNotice, x, y, width, 36, 25, Muted); return true; }
            return false;
        }

        /// <summary>
        /// 左侧装备栏:按服务器下发的槽位定义画出全部格子。空槽写部位名,穿着的画图标,点它即选中。
        /// 格高按槽位数均分栏高;超过一页(<see cref="EquipSlotsPerPage"/> 格)才出现翻页。
        /// </summary>




        private BagItemInfo FindEquipment(ulong id)
        {
            if (id == 0) return null;
            return _equipment?.Items.FirstOrDefault(i => i.ItemId == id) ??
                _bag?.Items.FirstOrDefault(i => i.ItemId == id && i.EquipKind != 0);
        }

        private void OpenEquipmentActions(ulong id)
        {
            var item = FindEquipment(id);
            if (item == null) return;
            _modalItemId = id; SetModalControls(true);
            _actions.Show(EquipDisplay.NameWithEnhance(ItemName(item), item.EnhanceLevel), () => AttributesRequested?.Invoke(),
                () => OpenEquipmentWish(id), EquipmentModalClosed,
                _forge == null ? (Action)null : () => OpenEquipmentForge(id));
        }

        /// <summary>从装备详情进入养成面板；所有操作及置灰由同一份状态对象处理。</summary>
        public void OpenEquipmentForge(ulong id)
        {
            if (_forgeDialog == null || !IsVisible || Page != GameplayPage.Bag || FindEquipment(id) == null) return;
            CloseEquipmentModals();
            // Actions 先关闭再回调，此处必须重新记住目标，避免在途通知将面板当作失效装备关闭。
            _modalItemId = id; SetModalControls(true);
            _forge.Open(id);
            if (_forge.IsOpen) _forgeDialog.Show(EquipmentModalClosed);
            else EquipmentModalClosed();
        }

        private void OpenEquipmentWish(ulong id)
        {
            if (FindEquipment(id) == null) return;
            _modalItemId = id; SetModalControls(true);
            // 当前协议没有可选属性池或炼化接口，不能把现有词条当作完整池，也不能假装保存成功。
            _wish.Show(null, null, null, CloseEquipmentModals, "期望属性 · 尚未开放",
                "炼化功能尚未开放\n可选属性开放后，可在这里勾选期望属性。");
        }

        private void CloseEquipmentModals()
        {
            _actions.Close(); _wish.Hide(); _forgeDialog?.Close(); _forge?.Close(); EquipmentModalClosed();
        }

        private void EquipmentModalClosed() { _modalItemId = 0; SetModalControls(false); }

        private void SetModalControls(bool modal)
        {
            // 弹窗 Update 可能先于根窗口执行；关闭那一帧仍要吞掉 Esc，不能顺手关掉背包。
            if (!modal && !_frameControls.interactable) _modalClosedFrame = Time.frameCount;
            _frameControls.interactable = !modal; _frameControls.blocksRaycasts = !modal;
        }



        private void ShowMission(PlayerMissionInfo mission)
        {
            if (mission == null || _missions == null) return;
            _missionFilter = (int)mission.Status == 0 ? 3 : 0; _objectivePage = 0; _missionObjectiveScroll = 1; _selectedMission = MissionKey(mission);
            var ordered = _missions.Missions.Where(m => ((int)m.Status == 0) == (_missionFilter == 3)).OrderBy(m => m.MissionId).ToList();
            int index = Math.Max(0, ordered.FindIndex(m => MissionKey(m) == _selectedMission));
            _missionScroll = ordered.Count <= 5 ? 1 : 1f - Mathf.Clamp01(index * 101f / Math.Max(1, ordered.Count * 101f - 492));
            Show(GameplayPage.Missions);
        }
        /// <summary>只定位当前快照的同一scope和任务ID，不把旧卡片当成任务状态。</summary>
        public void ShowMission(uint scope, uint missionId)
        { ShowMission(_missions?.Missions.FirstOrDefault(m => m.Scope == scope && m.MissionId == missionId)); }

        private static string ActivitySchedule(PlayerActivityInfo activity)
        {
            if ((int)activity.Status == 0) return "尚未排期";
            return "开始 " + ActivityTime(activity.StartsAtMs) + "\n结束 " + ActivityTime(activity.EndsAtMs) + " 北京时间（UTC+8）";
        }

        private static string ActivityTime(ulong milliseconds)
        {
            if (milliseconds == 0 || milliseconds > 253402300799999ul) return "待公布";
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds).ToOffset(TimeSpan.FromHours(8))
                    .ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException) { return "待公布"; }
        }

    }
}
