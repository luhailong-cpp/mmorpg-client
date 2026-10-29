using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public sealed partial class GameplayWindow
    {
        public event Action<uint, ulong, uint, ulong> ActivityRewardClaimRequested;
        public event Action ActivityChallengeRequested;
        public event Action ActivityContestRequested;
        private readonly ActivityPreferences _activityPreferences;
        private int _activityCategory = -1, _activityRewardFilter;
        private bool _activityRewardBusy;
        private string _activityRewardError = string.Empty;
        private uint _activityClaimConfirmation;
        private uint _activityClaimRule;
        private ulong _activityClaimPeriod, _activityClaimRevision;
        private int _activityUtility;

        /// <summary>收藏仅属于当前角色；连接与角色真值由装配层提供，0表示尚未同步。</summary>
        public void SetActivityContext(ulong playerId)
        {
            if (_activityPreferences.PlayerId == playerId) return;
            _activityPreferences.SetPlayer(playerId);
            _activities = null; _activityRewardBusy = false; _activityRewardError = string.Empty;
            _activityCategory = _activityPreferences.Category;
            _activityRewardFilter = _activityPreferences.Reward;
            _activityScroll = 1; _activityDetails = _activityClaimConfirmation = 0; _activityUtility = 0;
            if (IsVisible && Page == GameplayPage.Activities) Render();
        }

        private void SetActivityFilters(int category, int reward)
        {
            _activityCategory = category; _activityRewardFilter = reward;
            _activityPreferences.SetFilters(category, reward);
            _activityScroll = 1; _activityDetails = _activityClaimConfirmation = 0; _activityUtility = 0;
            Render();
        }

        private static RectTransform ActivityPanel(UnityEngine.Transform parent, string name, float x, float y, float w, float h)
        {
            var panel = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var paper = Art(panel, "content_panel", 0, 0, w, h);
            paper.raycastTarget = true;
            return panel;
        }

        private bool MatchesActivity(PlayerActivityInfo activity)
        {
            bool category = _activityCategory switch
            {
                0 => _activityPreferences.Contains(activity.ActivityId, activity.DefinitionKey),
                1 => (int)activity.Category == 1,
                2 => (int)activity.Category == 2,
                3 => (int)activity.Category == 3,
                4 => (int)activity.Status == 1,
                5 => (int)activity.Category == 0 || (int)activity.Category == 4,
                _ => true
            };
            return category && (_activityRewardFilter == 0 || activity.RewardTypes.Any(t => (int)t == _activityRewardFilter));
        }

        private static int ActivityOrder(PlayerActivityInfo activity) => (int)activity.Status switch { 2 => 0, 1 => 1, 0 => 2, _ => 3 };
        private PlayerMissionInfo ActivityMission(PlayerActivityInfo activity) => _missions?.Missions.FirstOrDefault(
            m => m.Scope == activity.MissionScope && m.MissionId == activity.MissionId && (int)m.Status != 0);
        private bool ActivitySnapshotUsable => _activities != null && !_activityLoading && string.IsNullOrEmpty(_activityError);

        private string ActivityReason(PlayerActivityInfo activity, PlayerMissionInfo mission)
        {
            if (!string.IsNullOrEmpty(_missionError)) return _missionError;
            if (_missionActionBusy) return "正在处理任务，请稍候…";
            if (!ActivitySnapshotUsable) return _activityLoading ? "正在同步活动安排…" : "请刷新活动后重试";
            if (mission != null) return mission.CanClaim ? "任务奖励可领取" : MissionStatus(mission.Status);
            if (!string.IsNullOrWhiteSpace(activity.UnavailableReason)) return activity.UnavailableReason;
            if ((int)activity.EntryKind != 1 || activity.MissionId == 0) return "活动入口尚未提供";
            if (_missionLoading || _missions == null) return _missionLoading ? "正在同步任务资格…" : "任务尚未同步，请刷新";
            if (activity.CompletionCountKnown && activity.CompletionLimit != 0 && activity.CompletedCount >= activity.CompletionLimit) return "本期次数已用完";
            return activity.CanParticipate ? "可接取活动任务" : "活动暂不可参与";
        }

        private bool CanUseActivity(PlayerActivityInfo activity, PlayerMissionInfo mission) => ActivitySnapshotUsable &&
            !_missionActionBusy && !_missionLoading && string.IsNullOrEmpty(_missionError) && _missions != null &&
            (mission != null || ((int)activity.EntryKind == 1 && activity.MissionId != 0 && activity.CanParticipate &&
                (!activity.CompletionCountKnown || activity.CompletionLimit == 0 || activity.CompletedCount < activity.CompletionLimit)));

        private void UseActivity(uint activityId)
        {
            // 事件闭包只携带稳定ID；点击时重新从最新快照取资格，旧详情不能提交已经失效的活动。
            var activity = _activities?.Activities.FirstOrDefault(a => a.ActivityId == activityId);
            if (activity == null) return;
            var mission = ActivityMission(activity);
            if (!CanUseActivity(activity, mission)) return;
            _selectedActivity = activityId;
            if (mission != null) ShowMission(mission);
            else MissionAcceptRequested?.Invoke(activity.MissionScope, activity.MissionId);
        }

        private static string ActivityCounts(PlayerActivityInfo activity) => !activity.CompletionCountKnown ? "次数 未提供" :
            "次数 " + activity.CompletedCount + "/" + (activity.CompletionLimit == 0 ? "—" : activity.CompletionLimit.ToString());
        private static string ActivityRewardText(IEnumerable<PlayerActivityRewardItem> rewards)
        {
            var list = rewards?.Select(r => (string.IsNullOrWhiteSpace(r.Name) ? "物品 " + r.ItemConfigId : r.Name) + " ×" + r.Count +
                (string.IsNullOrWhiteSpace(r.BindingDescription) ? string.Empty : "（" + r.BindingDescription + "）")).ToArray();
            return list == null || list.Length == 0 ? "奖励明细未提供" : string.Join("、", list);
        }

        private void RenderActivities()
        {
            // Original four large navigation plaques stay at their approved positions. The current
            // protocol has welfare rather than festival metadata; all six real filters live in Tools.
            string[] categories = { "今日推荐", "每日活动", "限时活动", "节庆活动" };
            string[] icons = { "activity-menu-scroll", "activity-menu-taiji", "activity-menu-hourglass", "activity-menu-lantern" };
            int[] filters = { -1, 1, 2, -2 };
            for (int i = 0; i < categories.Length; ++i)
            {
                int category = filters[i]; bool selected = _activityCategory == category;
                var button = NativeButton(_body, "ActivityPrimary_" + i, "", selected ? "activity-nav-selected" : "activity-nav",
                    166, 96 + i * 109, 319, 104, () => SetActivityFilters(category, _activityRewardFilter), category != -2);
                NativeArt(button.transform, icons[i], 26, 15, 80, 81);
                NativeText(button.transform, categories[i], 108, 18, 175, 65, 32, selected ? Cream : Ink, TextAlignmentOptions.Center);
                if (selected) NativeArt(button.transform, "activity-selected-circle", 258, 30, 48, 48).name = "ActivitySelectionMark";
            }
            string heading = _activityCategory switch { 0 => "我的收藏", 1 => "每日活动", 2 => "限时活动", 3 => "福利活动", 4 => "即将开启", 5 => "其他活动", _ => "今日推荐" };
            NativeText(_body, heading, 539, 91, 807, 63, 44);
            NativeHit(_body, "RefreshActivityFooter", "刷新", 1514, 110, 100, 38, Refresh,
                !_activityLoading && !_missionActionBusy && !_activityRewardBusy, 24);
            NativeHit(_body, "ActivityTools", "筛选", 1647, 110, 100, 38, () => { _activityTools = !_activityTools; Render(); }, font: 24);

            var list = _activities?.Activities.Where(MatchesActivity)
                .Where(a => _activityFilter == 0 || (_activityFilter == 1 ? (int)a.Status == 2 : (int)a.Status < 2))
                .OrderBy(ActivityOrder).ThenBy(a => a.ActivityId).ToList() ?? new List<PlayerActivityInfo>();
            var stale = _activityCategory == 0 && _activityRewardFilter == 0
                ? _activityPreferences.Favorites.Where(f => _activities != null && !_activities.Activities.Any(a => a.ActivityId == f.activityId && a.DefinitionKey == f.definitionKey)).ToArray()
                : Array.Empty<ActivityPreferences.Favorite>();
            if (!list.Any(a => a.ActivityId == _selectedActivity)) _selectedActivity = 0;
            if (_activityDetails != 0 && !list.Any(a => a.ActivityId == _activityDetails)) _activityDetails = 0;
            int total = list.Count + stale.Length;
            var cards = NativeScroll(_body, "ActivityCardsScroll", 522, 159, 1256, 495, 495,
                _activityScroll, p => _activityScroll = p, true, Math.Max(1256, total * 418 - 8));
            for (int i = 0; i < list.Count; ++i) RenderActivityCard(cards, list[i], i * 418, 0);
            for (int i = 0; i < stale.Length; ++i)
            {
                var favorite = stale[i]; int slot = list.Count + i;
                var card = QdaoUguiFactory.CreateRect("UnavailableFavorite_" + favorite.activityId, cards, slot * 418, 0, 410, 495);
                NativeArt(card, "activity-card", 0, 0, 410, 495);
                NativeText(card, string.IsNullOrEmpty(favorite.displayName) ? "活动 " + favorite.activityId : favorite.displayName, 28, 29, 354, 90, 34, wrap: true);
                NativeText(card, "已下架或配置身份已更新", 28, 160, 354, 150, 28, Muted, wrap: true).overflowMode = TextOverflowModes.Ellipsis;
                NativeButton(card, "RemoveFavorite_" + favorite.activityId, "移除收藏", "activity-button", 41, 412, 327, 66,
                    () => { _activityPreferences.Toggle(favorite.activityId, favorite.definitionKey, favorite.displayName); Render(); }, font: 30, jade: true);
            }
            string state = !string.IsNullOrEmpty(_activityError) ? _activityError : _activityLoading ? "正在同步活动安排…" :
                _activities == null ? "活动尚未同步，请刷新" : total == 0 ? (_activityCategory == 0 ? "暂无收藏，可在活动详情中收藏" : "没有符合筛选的活动，可清除筛选查看") : string.Empty;
            if (state.Length != 0) NativeStatus(state, 548, total == 0 ? 348 : 627, 1180, !string.IsNullOrEmpty(_activityError));
            if (!string.IsNullOrEmpty(_activityPreferences.Error)) NativeStatus(_activityPreferences.Error, 548, total == 0 ? 394 : 627, 1180, true);
            RenderActivityVitality();
            if (_activityDetails != 0) RenderActivityDetails(_activities.Activities.First(a => a.ActivityId == _activityDetails));
            if (_activityUtility != 0) RenderActivityUtility();
            if (_activityTools) RenderActivityTools();
            if (_activityClaimConfirmation != 0) RenderActivityClaimConfirmation();
        }

        private void RenderActivityCard(UnityEngine.Transform parent, PlayerActivityInfo activity, float x, float y)
        {
            var card = QdaoUguiFactory.CreateRect("Activity_" + activity.ActivityId, parent, x, y, 410, 495);
            NativeArt(card, "activity-card", 0, 0, 410, 495);
            if (!string.IsNullOrWhiteSpace(activity.IconKey)) ItemIcon(card, activity.IconKey, 24, 21, 155);
            NativeText(card, ActivityName(activity), 196, 15, 200, 62, 34);
            string status = (int)activity.Status == 0 ? "未排期" : ActivityStatus(activity.Status);
            string badge = (int)activity.Category == 2 ? "activity-badge-blue" : (int)activity.Category == 3 ? "activity-badge-pink" : "activity-badge-green";
            NativeArt(card, badge, 200, 80, 115, 40);
            NativeText(card, status, 200, 80, 115, 40, 22, Cream, TextAlignmentOptions.Center);
            NativeHit(card, "ActivityDetails_" + activity.ActivityId, "详情", 330, 80, 64, 40,
                () => { _activityDetails = activity.ActivityId; Render(); }, font: 22);
            var mission = ActivityMission(activity);
            bool importantReason = !string.IsNullOrWhiteSpace(_missionError) || _missionActionBusy || !string.IsNullOrWhiteSpace(activity.UnavailableReason);
            string summary = importantReason ? ActivityReason(activity, mission) : string.IsNullOrWhiteSpace(activity.Description) ? ActivityReason(activity, mission) : activity.Description;
            NativeText(card, summary, 201, 129, 190, 64, 24, !string.IsNullOrWhiteSpace(_missionError) ? QdaoUguiTheme.Html("#9A442D") : Ink, wrap: true).overflowMode = TextOverflowModes.Ellipsis;
            NativeArt(card, "activity-progress-label", 44, 216, 55, 29);
            // Activity completion counts are authoritative. Mission objective progress is a separate fact.
            var completion = NativeText(card, ActivityCounts(activity), 0, 0, 1, 1, 1); completion.name = "ActivityCompletionCount"; completion.enabled = false;
            string count = !activity.CompletionCountKnown ? "—/—" : activity.CompletedCount + "/" + (activity.CompletionLimit == 0 ? "—" : activity.CompletionLimit.ToString());
            NativeText(card, count, 250, 208, 116, 43, 28, Ink, TextAlignmentOptions.MidlineRight).name = "ActivityProgress";
            NativeArt(card, "activity-reward-heading", 26, 257, 359, 36).name = "ActivityRewardHeading";
            var rewards = QdaoUguiFactory.CreateRect("ActivityRewards", card, 72, 297, 268, 112);
            for (int i = 0; i < 2; ++i)
            {
                float left = i * 148;
                NativeArt(rewards, "activity-reward-empty-frame", left, 0, 120, 112).name = "ActivityRewardEmptySlot_" + i;
                var reward = activity.Rewards.ElementAtOrDefault(i);
                if (reward == null) NativeText(rewards, "—", left, 34, 120, 44, 25, Muted, TextAlignmentOptions.Center);
                else
                {
                    if (!string.IsNullOrWhiteSpace(reward.IconKey)) ItemIcon(rewards, reward.IconKey, left + 15, 6, 80);
                    else NativeText(rewards, string.IsNullOrWhiteSpace(reward.Name) ? "物品 " + reward.ItemConfigId : reward.Name, left + 5, 8, 110, 72, 22, Ink, TextAlignmentOptions.Center, true);
                    NativeText(rewards, "×" + reward.Count, left + 5, 81, 110, 27, 22, Ink, TextAlignmentOptions.MidlineRight);
                }
            }
            NativeButton(card, "ActivityAction_" + activity.ActivityId, _missionActionBusy ? "处理中…" : mission != null ? "查看任务" : "前往参与",
                "activity-button", 41, 412, 327, 66, () => UseActivity(activity.ActivityId), CanUseActivity(activity, mission), 32, true);
        }

        private void RenderActivityTools()
        {
            var panel = ActivityPanel(_body, "ActivityToolsPanel", 526, 164, 1238, 468);
            NativeText(panel, "活动筛选", 30, 17, 800, 53, 35);
            NativeHit(panel, "CloseActivityTools", "返回活动", 1040, 20, 167, 49, () => { _activityTools = false; Render(); }, font: 25);
            NativeText(panel, "活动分类", 30, 92, 164, 44, 28);
            string[] categories = { "我的收藏", "日常活动", "限时活动", "福利活动", "即将开启", "其他活动" };
            for (int i = 0; i < categories.Length; ++i)
            {
                int category = i; bool selected = _activityCategory == i;
                NativeButton(panel, "ActivityCategory_" + i, categories[i], selected ? "bag-tab-selected" : "bag-tab-normal",
                    200 + i % 3 * 330, 83 + i / 3 * 67, 310, 58,
                    () => { _activityTools = false; SetActivityFilters(category, _activityRewardFilter); }, font: 29, jade: selected);
            }
            NativeText(panel, "奖励筛选", 30, 238, 164, 44, 28);
            string[] rewards = { "全部", "经验", "道行", "道具", "装备" };
            for (int i = 0; i < rewards.Length; ++i)
            {
                int filter = i; bool selected = _activityRewardFilter == i;
                NativeButton(panel, "ActivityRewardFilter_" + i, rewards[i], selected ? "bag-tab-selected" : "bag-tab-normal",
                    200 + i * 195, 232, 184, 52, () => { _activityTools = false; SetActivityFilters(_activityCategory, filter); }, font: 27, jade: selected);
            }
            NativeText(panel, "活动状态", 30, 310, 164, 44, 28);
            string[] statuses = { "全部活动", "进行中", "待开放" };
            for (int i = 0; i < statuses.Length; ++i)
            {
                int filter = i; bool selected = _activityFilter == i;
                NativeButton(panel, "ActivityStatusFilter_" + i, statuses[i], selected ? "bag-tab-selected" : "bag-tab-normal",
                    200 + i * 330, 300, 310, 52, () => { _activityFilter = filter; _activityScroll = 1; _activityTools = false; Render(); }, font: 27, jade: selected);
            }
            var vitality = _activities?.Vitality;
            if (vitality != null && vitality.Available)
                NativeText(panel, "本期 " + ActivityTime(vitality.PeriodStartsAtMs) + " 至 " + ActivityTime(vitality.PeriodEndsAtMs) +
                    " · 下次刷新 " + ActivityTime(vitality.PeriodEndsAtMs) + " UTC+8", 30, 359, 1170, 28, 17, Muted);
            NativeHit(panel, "ClearActivityFilters", "清除筛选", 30, 394, 155, 47,
                () => { _activityTools = false; _activityFilter = 0; SetActivityFilters(-1, 0); }, font: 23);
            NativeHit(panel, "ActivityStatistics", "统计", 197, 394, 108, 47, () => { _activityTools = false; _activityUtility = 1; Render(); }, _activities != null, 23);
            NativeHit(panel, "ActivityReminder", "提醒", 316, 394, 108, 47, null, false, 23);
            NativeHit(panel, "ActivityNotifications", "推送", 435, 394, 108, 47, null, false, 23);
            NativeButton(panel, "ActivityMainTab", "活动", "bag-tab-selected", 566, 394, 157, 47, null, false, 25, true);
            NativeButton(panel, "ActivityChallengeTab", "挑战", "bag-tab-normal", 740, 394, 157, 47, () => ActivityChallengeRequested?.Invoke(), ActivityChallengeRequested != null, 25);
            NativeButton(panel, "ActivityContestTab", "赛事", "bag-tab-normal", 914, 394, 157, 47, () => ActivityContestRequested?.Invoke(), ActivityContestRequested != null, 25);
            string navigationReason = ActivityChallengeRequested == null && ActivityContestRequested == null ? "；挑战、赛事入口尚未开放" :
                ActivityChallengeRequested == null ? "；挑战入口尚未开放" : ActivityContestRequested == null ? "；赛事入口尚未开放" : string.Empty;
            NativeText(panel, "节庆分类、提醒与推送尚未开放" + navigationReason, 30, 444, 1145, 24, 17, Muted);
        }

        private void RenderActivityDetails(PlayerActivityInfo activity)
        {
            var panel = ActivityPanel(_body, "ActivityDetailsPanel", 526, 164, 1238, 468);
            ItemIcon(panel, activity.IconKey, 23, 20, 91);
            NativeText(panel, ActivityName(activity), 133, 18, 745, 55, 36);
            bool favorite = _activityPreferences.Contains(activity.ActivityId, activity.DefinitionKey);
            NativeHit(panel, "FavoriteActivity_" + activity.ActivityId, favorite ? "取消收藏" : "收藏", 905, 22, 143, 49,
                () => { _activityPreferences.Toggle(activity.ActivityId, activity.DefinitionKey, ActivityName(activity)); Render(); },
                _activityPreferences.CanSave && !string.IsNullOrWhiteSpace(activity.DefinitionKey), 25);
            NativeHit(panel, "CloseActivityDetails_" + activity.ActivityId, "返回活动", 1050, 22, 163, 49,
                () => { _activityDetails = 0; Render(); }, font: 25);
            string description = string.IsNullOrWhiteSpace(activity.Description) ? "玩法说明未提供" : activity.Description;
            string period = string.IsNullOrWhiteSpace(activity.CompletionPeriod) ? "次数周期未提供" : activity.CompletionPeriod;
            string details = ActivitySchedule(activity) + "\n" + ActivityCounts(activity) + " · " + period + "\n" +
                (_activities?.Vitality?.Available == true ? "活跃贡献 " + activity.VitalityPoints : "活跃尚未开放") + "\n奖励：" + ActivityRewardText(activity.Rewards) + "\n\n" + description;
            NativeParagraph(panel, details, 33, 120, 1166, 230, 26);
            var mission = ActivityMission(activity);
            NativeText(panel, ActivityReason(activity, mission), 33, 369, 836, 39, 24, Muted);
            NativeButton(panel, "ActivityDetailAction_" + activity.ActivityId, mission != null ? "查看任务" : "前往", "activity-button", 971, 371, 228, 62,
                () => UseActivity(activity.ActivityId), CanUseActivity(activity, mission), 29, true);
            if (!_activityPreferences.CanSave || string.IsNullOrWhiteSpace(activity.DefinitionKey))
                NativeText(panel, !_activityPreferences.CanSave ? "角色身份未同步，收藏不可用" : "活动稳定身份未配置，收藏不可用", 33, 418, 886, 31, 20, Muted);
        }

        private void RenderActivityVitality()
        {
            var vitality = _activities?.Vitality;
            // Durable recovery, period identity and eligibility remain backend-owned.
            if (!string.IsNullOrEmpty(_activityRewardError)) NativeStatus(_activityRewardError, 543, 627, 1205, true);
            else if (_activityRewardBusy) NativeStatus("正在确认活跃奖励…", 543, 627, 1205);
            NativeText(_body, "本期活跃", 545, 683, 170, 57, 34);
            bool available = vitality != null && vitality.Available;
            NativeText(_body, available ? vitality.Points + "/" + vitality.PointsLimit : "—/—", 715, 683, 100, 57, 27, available ? Ink : Muted).name = "ActivityVitalityPoints";
            var track = QdaoUguiFactory.CreateImage("ActivityVitalityTrack", _body,
                799, 697, 650, 29, NativeSprite("activity-vitality-empty-track"), false);
            track.type = Image.Type.Sliced; track.preserveAspect = false; track.pixelsPerUnitMultiplier = 1f;
            float ratio = available && vitality.PointsLimit > 0 ? Mathf.Clamp01((float)vitality.Points / vitality.PointsLimit) : 0;
            var fill = QdaoUguiFactory.CreateImage("ActivityVitalityFill", _body,
                799, 697, 650 * ratio, 29, NativeSprite("activity-vitality-source-fill"), false);
            fill.type = Image.Type.Sliced; fill.preserveAspect = false; fill.pixelsPerUnitMultiplier = 1f; fill.enabled = ratio > 0;
            if (!available)
            {
                string unavailable = vitality == null || string.IsNullOrEmpty(vitality.UnavailableReason) ? "活跃规则尚未开放" : vitality.UnavailableReason;
                NativeText(_body, unavailable, 799, 737, 650, 26, 18, Muted);
                string[] art = { "activity-chest-inactive-purple", "activity-chest-inactive-purple", "activity-chest-inactive-red", "activity-chest-inactive-red-last" };
                float[] x = { 946, 1094, 1237, 1376 };
                for (int i = 0; i < art.Length; ++i)
                    NativeArt(_body, art[i], x[i], 665, 73, 66).name = "ActivityVitalityUnavailableChest_" + i;
            }
            else
            {
                // Any number of real thresholds stays reachable; the first four occupy the original chest strip.
                var rewards = vitality.Rewards.OrderBy(r => r.RequiredPoints).ThenBy(r => r.ThresholdId).ToList();
                var area = NativeScroll(_body, "ActivityVitalityRewardsScroll", 943, 656, 510, 111, 111, 1, _ => { }, true, Math.Max(510, rewards.Count * 143 - 61));
                for (int i = 0; i < rewards.Count; ++i)
                {
                    var reward = rewards[i]; string state = reward.Claimed ? (reward.Durable ? "已领取" : "确认中") : reward.CanClaim ? "可领取" : vitality.Points < reward.RequiredPoints ? "未达成" : "不可领取";
                    var button = NativeHit(area, "ActivityVitalityChest_" + reward.ThresholdId, "", i * 143, 0, 82, 111,
                        () => { _activityClaimConfirmation = reward.ThresholdId; _activityClaimRule = vitality.RuleId;
                            _activityClaimPeriod = vitality.PeriodId; _activityClaimRevision = vitality.ConfigurationRevision; Render(); }, !_activityRewardBusy);
                    NativeArt(button.transform, i < 2 ? "activity-chest-inactive-purple" : "activity-chest-inactive-red", 3, 9, 73, 66);
                    var label = NativeText(button.transform, reward.RequiredPoints + "活跃\n" + state, -24, 75, 130, 36, 14, Muted, TextAlignmentOptions.Center);
                    label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
                }
            }
            NativeButton(_body, "ActivityCalendar", "活动日历", "bag-button-normal", 1564, 684, 194, 66,
                () => { _activityTools = false; _activityUtility = 2; Render(); }, _activities != null, 32);
        }

        private void RenderActivityClaimConfirmation()
        {
            var vitality = _activities?.Vitality;
            var reward = vitality?.Rewards.FirstOrDefault(r => r.ThresholdId == _activityClaimConfirmation);
            if (vitality == null || !vitality.Available || reward == null || vitality.RuleId != _activityClaimRule ||
                vitality.PeriodId != _activityClaimPeriod || vitality.ConfigurationRevision != _activityClaimRevision)
            { _activityClaimConfirmation = 0; return; }
            var shade = QdaoUguiFactory.CreateRect("ActivityConfirmationShade", _body, 526, 164, 1238, 468).gameObject.AddComponent<Image>();
            shade.color = new Color(.02f, .12f, .08f, .48f); shade.raycastTarget = true;
            var panel = ActivityPanel(_body, "ActivityClaimConfirmation", 750, 245, 780, 329);
            NativeText(panel, reward.RequiredPoints + "活跃奖励", 28, 19, 723, 52, 34);
            NativeParagraph(panel, ActivityRewardText(reward.Items), 28, 86, 723, 109, 25);
            string reason = reward.Claimed ? (reward.Durable ? "本期已领取" : "领取确认中，请刷新恢复原请求") : !reward.CanClaim ? reward.UnavailableReason : string.Empty;
            NativeText(panel, reason, 28, 203, 723, 39, 22, Muted);
            uint rule = vitality.RuleId, threshold = reward.ThresholdId;
            ulong period = vitality.PeriodId, revision = vitality.ConfigurationRevision;
            NativeButton(panel, "ConfirmActivityReward", _activityRewardBusy ? "处理中…" : "确认领取", "bag-button-primary", 421, 255, 330, 54,
                () =>
                {
                    var latest = _activities?.Vitality;
                    var current = latest?.Rewards.FirstOrDefault(r => r.ThresholdId == threshold);
                    if (!ActivitySnapshotUsable || _activityRewardBusy || latest == null || !latest.Available ||
                        latest.RuleId != rule || latest.PeriodId != period || latest.ConfigurationRevision != revision ||
                        current == null || !current.CanClaim || current.Claimed) return;
                    _activityClaimConfirmation = 0;
                    ActivityRewardClaimRequested?.Invoke(rule, period, threshold, revision);
                }, ActivitySnapshotUsable && !_activityRewardBusy && reward.CanClaim && !reward.Claimed && ActivityRewardClaimRequested != null, 28, true);
            NativeButton(panel, "CancelActivityReward", "返回", "bag-button-normal", 28, 255, 330, 54,
                () => { _activityClaimConfirmation = 0; Render(); }, font: 28);
        }

        private void RenderActivityUtility()
        {
            var panel = ActivityPanel(_body, "ActivityUtilityPanel", 526, 164, 1238, 468);
            NativeText(panel, _activityUtility == 1 ? "活动次数统计" : "正式排期日历", 30, 17, 990, 53, 35);
            NativeHit(panel, "CloseActivityUtility", "返回活动", 1040, 20, 167, 49, () => { _activityUtility = 0; Render(); }, font: 25);
            var source = _activities?.Activities.OrderBy(a => a.StartsAtMs).ThenBy(a => a.ActivityId).ToList() ?? new List<PlayerActivityInfo>();
            var content = NativeScroll(panel, "ActivityUtilityScroll", 30, 90, 1176, 347, Math.Max(347, source.Count * 105), 1, _ => { });
            for (int i = 0; i < source.Count; ++i)
            {
                var activity = source[i];
                NativeText(content, ActivityName(activity), 6, i * 105, 398, 39, 28);
                NativeText(content, _activityUtility == 1 ? ActivityCounts(activity) + " · " + (string.IsNullOrEmpty(activity.CompletionPeriod) ? "周期未提供" : activity.CompletionPeriod) : ActivitySchedule(activity),
                    408, i * 105, 754, 92, 25, Muted, wrap: true);
            }
            if (source.Count == 0) NativeText(content, "尚无正式活动配置", 20, 77, 1100, 55, 29, Muted, TextAlignmentOptions.Center);
        }

    }
}
