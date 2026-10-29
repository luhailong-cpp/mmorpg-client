using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>任务栏只派生展示；所有进度和动作能力均来自同一任务快照。</summary>
    public sealed class QuestHudContent : IDisposable
    {
        public event Action<uint, uint> DetailsRequested;
        public event Action ListRequested;
        public event Action RefreshRequested;
        public RectTransform Root { get; }
        private readonly RectTransform _cards;
        private readonly ScrollRect _scroll;
        private readonly TMP_Text _status, _count;
        private readonly Button _refresh;
        private readonly HashSet<ulong> _expanded = new();
        private MissionTrackingState _tracking;
        private GetMissionListResponse _snapshot;
        private bool _loading, _busy;
        private string _error;
        private bool _disposed;

        public QuestHudContent(UnityEngine.Transform mount)
        {
            Root = QdaoUguiFactory.CreateRect("QuestHudContent", mount, 0, 0, 416, 582);
            _count = Label(Root, "仙途指引", 8, 0, 272, 34, 25);
            _refresh = Control(Root, "QuestRefresh", "刷新", 314, 0, 94, 34,
                () => RefreshRequested?.Invoke(), 20);
            var viewport = QdaoUguiFactory.CreateRect("QuestCardsViewport", Root, 4, 40, 408, 430);
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            _cards = QdaoUguiFactory.CreateRect("QuestCards", viewport, 0, 0, 408, 430);
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport; _scroll.content = _cards;
            _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 48;
            Control(Root, "QuestScrollUp", "上翻", 8, 476, 80, 32,
                () => MoveScroll(-240), 18);
            Control(Root, "QuestScrollDown", "下翻", 94, 476, 80, 32,
                () => MoveScroll(240), 18);
            Control(Root, "QuestList", "任务列表", 250, 476, 158, 36,
                () => ListRequested?.Invoke(), 22);
            var auto = Control(Root, "QuestAutoTrackingUnavailable", "自动追踪 · 未开放", 8, 514, 210, 32, null, 18);
            auto.interactable = false;
            Label(Root, "自动选择规则待确认", 230, 514, 178, 32, 17, Muted);
            _status = Label(Root, "任务尚未同步", 44, 540, 328, 24, 17, Muted);
            RenderCards();
        }

        public void SetData(GetMissionListResponse snapshot, bool loading, string error, bool busy,
            MissionTrackingState tracking)
        {
            if (_disposed) return;
            bool changed = !Equals(_snapshot, snapshot) || !ReferenceEquals(_tracking, tracking);
            if (!ReferenceEquals(_tracking, tracking))
            {
                if (_tracking != null) _tracking.OnChanged -= TrackingChanged;
                _tracking = tracking;
                if (_tracking != null) _tracking.OnChanged += TrackingChanged;
            }
            _snapshot = snapshot; _loading = loading; _error = error; _busy = busy;
            _refresh.interactable = !loading && !busy;
            _status.text = !string.IsNullOrWhiteSpace(error)
                ? snapshot == null ? "同步失败，请刷新重试" : "同步失败 · 显示上次记录，请刷新"
                : loading ? snapshot == null ? "正在同步任务…" : "正在更新任务…"
                : snapshot == null ? "任务尚未同步" : "点击任务查看详情 · 暂无定位信息";
            if (changed) RenderCards();
        }

        private void TrackingChanged() { if (!_disposed) RenderCards(); }

        private void RenderCards()
        {
            float offset = _cards.anchoredPosition.y;
            Clear(_cards);
            var missions = _snapshot?.Missions.Where(IsVisibleMission)
                .OrderByDescending(m => _tracking != null && _tracking.CurrentKey == MissionKey(m))
                .ThenByDescending(m => _tracking?.IsTracked(MissionKey(m)) ?? false)
                .ThenBy(m => m.Scope).ThenBy(m => m.MissionId).ToList() ?? new List<PlayerMissionInfo>();
            _expanded.RemoveWhere(key => !missions.Any(m => MissionKey(m) == key));
            _count.text = "仙途指引 · " + missions.Count;
            float y = 0;
            foreach (var mission in missions) y += CreateCard(mission, y) + 8;
            if (missions.Count == 0)
            {
                string empty = _snapshot == null ? "等待任务同步" : "暂无进行中的任务\n可在任务列表查看可接任务";
                var label = Label(_cards, empty, 20, 120, 368, 110, 24, Muted);
                label.textWrappingMode = TextWrappingModes.Normal;
            }
            _cards.sizeDelta = new Vector2(408, Math.Max(430, y));
            _cards.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Math.Max(0, y - 430)));
        }

        private float CreateCard(PlayerMissionInfo mission, float y)
        {
            ulong key = MissionKey(mission);
            bool tracked = _tracking?.IsTracked(key) ?? false;
            bool expanded = _expanded.Contains(key);
            var card = QdaoUguiFactory.CreateRect($"QuestCard_{mission.Scope}_{mission.MissionId}", _cards, 0, y, 408, 100);
            // 大窗装饰缩到紧凑卡片会盖住首行；任务内容用细边纸色面板，外框仍由07绘制。
            var panel = QdaoUguiFactory.CreateImage("QuestCardSurface", card, 0, 0, 408, 100, null, true);
            panel.color = QdaoUguiTheme.Html(tracked ? "#E9EFDA" : "#FFF6E2");
            var edge = panel.gameObject.AddComponent<Outline>();
            edge.effectColor = QdaoUguiTheme.Html(tracked ? "#71895D" : "#C8B286");
            edge.effectDistance = new Vector2(1, -1);
            panel.raycastTarget = true;
            var open = panel.gameObject.AddComponent<Button>(); open.targetGraphic = panel;
            open.onClick.AddListener(() => DetailsRequested?.Invoke(mission.Scope, mission.MissionId));
            var category = Label(card, Category(mission), 12, 6, 70, 28, 17, Muted);
            category.name = "QuestCategory";
            var title = Label(card, MissionName(mission), 86, 5, 310, 29, 23);
            title.name = "QuestTitle";
            title.fontStyle = FontStyles.Bold;
            title.textWrappingMode = TextWrappingModes.Normal;
            float titleHeight = Math.Max(29, title.GetPreferredValues(title.text, 310, float.PositiveInfinity).y + 2);
            title.rectTransform.sizeDelta = new Vector2(310, titleHeight);
            float next = 5 + titleHeight;
            var goals = mission.Objectives.OrderBy(g => g.ObjectiveIndex).ToList();
            if (mission.Status == (PlayerMissionStatus)4)
            {
                next += Paragraph(card, string.IsNullOrWhiteSpace(mission.UnavailableReason) ? "任务已失败" : mission.UnavailableReason,
                    next, "QuestFailure");
            }
            if (expanded)
            {
                foreach (var goal in goals) next += Objective(card, mission, goal, next);
                if (goals.Count == 0) next += Paragraph(card, "暂无目标记录", next, "QuestNoObjectives");
            }
            else if (mission.Status != (PlayerMissionStatus)4)
            {
                var goal = goals.FirstOrDefault(g => !g.Completed) ?? goals.FirstOrDefault();
                if (goal != null) next += Objective(card, mission, goal, next);
                else next += Paragraph(card, mission.Status == (PlayerMissionStatus)3 ? "奖励可领取，请打开详情" : "暂无目标记录", next, "QuestNoObjectives");
            }
            if (goals.Count > 1)
                next += Paragraph(card, $"目标 {goals.Count} 项 · 已达成 {goals.Count(g => g.Completed)} 项", next, "QuestGoalSummary");
            var toggle = Control(card, "QuestTrack_" + key, tracked ? "取消追踪" : "追踪", 270, next, 126, 32,
                () => _tracking?.Toggle(mission), 18);
            toggle.interactable = _tracking != null && ((int)mission.Status == 1 || (int)mission.Status == 3);
            Control(card, "QuestExpand_" + key, expanded ? "收起目标" : "全部目标", 12, next, 126, 32,
                () => { if (!_expanded.Add(key)) _expanded.Remove(key); RenderCards(); }, 18).interactable = goals.Count > 1;
            Control(card, "QuestDetails_" + key, MissionStatus(mission.Status), 146, next, 116, 32,
                () => DetailsRequested?.Invoke(mission.Scope, mission.MissionId), 18);
            float height = next + 36;
            card.sizeDelta = new Vector2(408, height); panel.rectTransform.sizeDelta = new Vector2(408, height);
            return height;
        }

        private static float Objective(UnityEngine.Transform card, PlayerMissionInfo mission, MissionObjectiveInfo goal, float y)
        {
            string description = string.IsNullOrWhiteSpace(goal.Description) ? "目标 " + ((ulong)goal.ObjectiveIndex + 1) : goal.Description;
            string count = goal.Target == 0 ? goal.Completed ? "已达成" : "数量待确认" : $"{goal.Progress}/{goal.Target}";
            return Paragraph(card, (goal.Completed ? "已达成 · " : "") + description + "  " + count, y,
                $"QuestObjective_{mission.Scope}_{mission.MissionId}_{goal.ObjectiveIndex}");
        }

        private static float Paragraph(UnityEngine.Transform parent, string value, float y, string name)
        {
            var text = Label(parent, value, 12, y, 380, 26, 19);
            text.name = name; text.textWrappingMode = TextWrappingModes.Normal;
            float height = Math.Max(26, text.GetPreferredValues(value, 380, float.PositiveInfinity).y + 4);
            text.rectTransform.sizeDelta = new Vector2(380, height);
            return height;
        }
        public static bool IsVisibleMission(PlayerMissionInfo mission)
            => mission != null && ((int)mission.Status == 1 || (int)mission.Status == 3 || (int)mission.Status == 4);
        public static string Category(PlayerMissionInfo mission) => mission.MissionType switch
        { 1 => "主线", 2 => "活动", _ => mission.MissionType == 0 ? "任务" : "类型 " + mission.MissionType };

        private void MoveScroll(float amount)
        { _scroll.StopMovement(); _cards.anchoredPosition = new Vector2(0, Mathf.Clamp(_cards.anchoredPosition.y + amount, 0, Math.Max(0, _cards.rect.height - 430))); }
        private static TMP_Text Label(UnityEngine.Transform parent, string value, float x, float y, float w, float h, float size, Color? color = null)
        { var text = Text(parent, value, x, y, w, h, size, color); text.raycastTarget = false; return text; }
        private static Button Control(UnityEngine.Transform parent, string name, string label, float x, float y, float w, float h, Action click, float size)
        {
            var rect = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var surface = rect.gameObject.AddComponent<Image>();
            surface.color = QdaoUguiTheme.Html("#E0E8CF");
            var edge = rect.gameObject.AddComponent<Outline>();
            edge.effectColor = QdaoUguiTheme.Html("#94A474");
            edge.effectDistance = new Vector2(1, -1);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = surface;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.highlightedColor = new Color(.92f, 1, .85f);
            colors.pressedColor = new Color(.8f, .9f, .73f);
            colors.selectedColor = new Color(.95f, 1, .9f);
            colors.disabledColor = new Color(.86f, .86f, .86f, .6f);
            button.colors = colors;
            if (click != null) button.onClick.AddListener(() => click());
            var text = Label(rect, label, 4, 0, w - 8, h, size);
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_tracking != null) _tracking.OnChanged -= TrackingChanged;
            if (Root != null)
            { Root.gameObject.SetActive(false); if (Application.isPlaying) UnityEngine.Object.Destroy(Root.gameObject); else UnityEngine.Object.DestroyImmediate(Root.gameObject); }
        }
    }
}
