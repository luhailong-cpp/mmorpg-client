using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// PVP 战报列表面板(docs/handoff/battle-report-replay-client.md §3):
    ///   - 打开时拉一次 ListBattleReports(单飞、不轮询);
    ///   - 可滚动展示服务端返回的全部条目(最多 100 条),严格保持服务端顺序(不按 battle_id 重排);
    ///   - 每条显示胜负、模式、结束时间、回合数、双方成员(名字/等级)与「看回放」按钮;
    ///   - has_replay = false 时置灰为「无回放」;拉取回放期间防连点。
    /// </summary>
    public sealed class BattleReportPanel
    {
        private const float PanelX = 60f;
        private const float PanelY = 90f;
        private const float PanelW = 860f;
        private const float PanelH = 900f;

        private const float ListTop = 148f;
        private const float ListBottomMargin = 92f;
        private const float ListWidth = PanelW - 60f;
        private const float ListHeight = PanelH - ListTop - ListBottomMargin;
        private const float EntryH = 96f;
        private const float EntryGap = 8f;

        private static readonly ulong MaxUnixMilliseconds = (ulong)DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();
        private static readonly Color WinColor = QdaoUguiTheme.Html("#205B46");

        private readonly BattleUiRoot _owner;
        private readonly RectTransform _root;
        private readonly UiTextButton _refreshButton;
        private readonly UiTextButton _closeButton;
        private readonly RectTransform _content;
        private readonly ScrollRect _scroll;
        private readonly TMP_Text _emptyText;
        private readonly TMP_Text _statusText;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<(UiTextButton Button, bool HasReplay)> _watchButtons = new List<(UiTextButton, bool)>();

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public BattleReportPanel(BattleUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;

            var bg = BattleUiWidgets.CreatePanel("BattleReportPanel", parent,
                PanelX, PanelY, PanelW, PanelH, BattleUiStyle.PanelBg);
            _root = (RectTransform)bg.transform;

            QdaoUguiFactory.CreateText("Title", _root, 24f, 16f, 360f, 44f,
                "PVP 战报", 28f, QdaoUguiTheme.Cream);

            _closeButton = BattleUiWidgets.CreateTextButton("Close", _root, PanelW - 64f, 12f, 48f, 48f,
                "×", 28f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _closeButton.Button.onClick.AddListener(Hide);

            _refreshButton = BattleUiWidgets.CreateTextButton("Refresh", _root, 30f, 74f, 180f, 56f,
                "刷新战报", 22f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _refreshButton.Button.onClick.AddListener(RefreshList);

            QdaoUguiFactory.CreateText("Hint", _root, 226f, 84f, PanelW - 256f, 36f,
                "保留最近 100 场 PVP 对局，点击「看回放」复盘", 18f, QdaoUguiTheme.StatusCream);

            var viewport = QdaoUguiFactory.CreateRect("Viewport", _root, 30f, ListTop, ListWidth, ListHeight);
            viewport.gameObject.AddComponent<RectMask2D>();
            var surface = viewport.gameObject.AddComponent<Image>();
            surface.color = new Color(1f, 1f, 1f, 0.001f);
            surface.raycastTarget = true;

            _content = QdaoUguiFactory.CreateRect("Content", viewport, 0f, 0f, ListWidth, ListHeight);
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = EntryH;
            _scroll.inertia = false;

            _emptyText = QdaoUguiFactory.CreateText("Empty", _root, 30f, ListTop + 24f, ListWidth, 60f,
                "暂无 PVP 战报", 22f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.Center);

            _statusText = BattleUiWidgets.CreateWrappedText("Status", _root,
                30f, PanelH - 78f, ListWidth, 62f,
                string.Empty, 18f, QdaoUguiTheme.StatusCream);

            BattleFidelityArt.ApplyPanel(_root);
            Hide();
        }

        public void Show()
        {
            if (_root != null) _root.gameObject.SetActive(true);
            RefreshInteractable();
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        public void SetStatus(string value)
        {
            if (_statusText != null) _statusText.text = value ?? string.Empty;
        }

        public void RefreshList()
        {
            var reports = _owner?.Reports;
            if (reports == null) { SetStatus("战报模块未就绪"); return; }
            if (reports.RefreshList())
            {
                SetStatus("正在刷新战报列表…");
            }
            RefreshInteractable();
        }

        public void RefreshInteractable()
        {
            var reports = _owner?.Reports;
            bool listing = reports != null && reports.IsListing;
            bool fetching = reports != null && reports.IsFetching;
            _refreshButton?.SetInteractable(!listing);
            foreach (var (button, hasReplay) in _watchButtons)
            {
                button?.SetInteractable(hasReplay && !fetching);
            }
        }

        public void ApplyList(IReadOnlyList<Match.BattleReportSummary> reports, ulong myPlayerId)
        {
            ClearRows();
            int count = reports?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var summary = reports[i];
                if (summary == null || summary.BattleId == 0) continue;
                BuildRow(summary, _rows.Count, myPlayerId);
            }

            float contentH = Mathf.Max(ListHeight, _rows.Count * (EntryH + EntryGap));
            _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentH);
            _scroll.StopMovement();
            _content.anchoredPosition = Vector2.zero;
            _scroll.verticalNormalizedPosition = 1f;

            _emptyText.gameObject.SetActive(_rows.Count == 0);
            SetStatus(_rows.Count == 0 ? string.Empty : $"共 {_rows.Count} 场战报");
            RefreshInteractable();
            BattleFidelityArt.ApplyPanel(_root);
        }

        // ── 纯格式化工具 ─────────────────────────────────────

        public static string FormatTimestamp(ulong finishedAtMs)
        {
            if (finishedAtMs == 0 || finishedAtMs > MaxUnixMilliseconds) return "--";
            return DateTimeOffset.FromUnixTimeMilliseconds((long)finishedAtMs)
                .ToLocalTime()
                .ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        public static string FormatMembers(Match.BattleReportSummary summary)
        {
            if (summary == null || summary.Members.Count == 0) return "(无成员快照)";
            var teamA = new List<string>();
            var teamB = new List<string>();
            foreach (var member in summary.Members)
            {
                if (member == null) continue;
                string name = string.IsNullOrWhiteSpace(member.Name) ? $"玩家{member.PlayerId}" : member.Name;
                string item = member.Level > 0 ? $"{name}(Lv.{member.Level})" : name;
                if (member.TeamIndex == 0) teamA.Add(item);
                else teamB.Add(item);
            }
            string left = teamA.Count > 0 ? string.Join("、", teamA) : "A队";
            string right = teamB.Count > 0 ? string.Join("、", teamB) : "B队";
            return $"{left}  VS  {right}";
        }

        public static string VerdictLabel(BattleReportVerdict verdict) => verdict switch
        {
            BattleReportVerdict.Win => "胜",
            BattleReportVerdict.Lose => "负",
            BattleReportVerdict.Draw => "平",
            _ => "—",
        };

        // ── 内部 ─────────────────────────────────────────────

        private void BuildRow(Match.BattleReportSummary summary, int index, ulong myPlayerId)
        {
            float y = index * (EntryH + EntryGap);
            var rowPanel = BattleUiWidgets.CreatePanel($"Report_{summary.BattleId}", _content,
                0f, y, ListWidth, EntryH, BattleUiStyle.PanelBgLight);
            var rowRect = (RectTransform)rowPanel.transform;
            _rows.Add(rowPanel.gameObject);

            var verdict = BattleReportClient.ResolveVerdict(summary, myPlayerId);
            Color badgeColor = verdict switch
            {
                BattleReportVerdict.Win => WinColor,
                BattleReportVerdict.Lose => BattleUiStyle.DamageText,
                _ => QdaoUguiTheme.MutedBrown,
            };
            var badgeBg = BattleUiWidgets.CreatePanel("VerdictBadge", rowRect,
                14f, 18f, 60f, 60f, badgeColor, raycastTarget: false);
            var badgeText = QdaoUguiFactory.CreateText("VerdictText", badgeBg.transform,
                0f, 0f, 60f, 60f, VerdictLabel(verdict), 28f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            badgeText.fontStyle = FontStyles.Bold;

            float textX = 88f;
            float textW = ListWidth - textX - 178f;
            var head = QdaoUguiFactory.CreateText("Head", rowRect, textX, 12f, textW, 34f,
                $"{BattleReportClient.ModeName(summary.Mode)} · {FormatTimestamp(summary.FinishedAtMs)} · 共 {summary.TotalRounds} 回合",
                20f, QdaoUguiTheme.Cream);
            head.textWrappingMode = TextWrappingModes.NoWrap;
            head.overflowMode = TextOverflowModes.Ellipsis;

            var members = QdaoUguiFactory.CreateText("Members", rowRect, textX, 50f, textW, 34f,
                FormatMembers(summary), 18f, QdaoUguiTheme.StatusCream);
            members.textWrappingMode = TextWrappingModes.NoWrap;
            members.overflowMode = TextOverflowModes.Ellipsis;

            bool hasReplay = summary.HasReplay;
            var watchButton = BattleUiWidgets.CreateTextButton("WatchReplay", rowRect,
                ListWidth - 160f, 20f, 144f, 56f,
                hasReplay ? "看回放" : "无回放", 20f,
                hasReplay ? BattleUiStyle.ButtonPlateAccent : BattleUiStyle.ButtonPlate,
                BattleUiStyle.ButtonText);
            watchButton.SetInteractable(hasReplay);
            if (hasReplay)
            {
                ulong battleId = summary.BattleId;
                watchButton.Button.onClick.AddListener(() => OnWatchClicked(battleId));
            }
            _watchButtons.Add((watchButton, hasReplay));
        }

        private void OnWatchClicked(ulong battleId)
        {
            var reports = _owner?.Reports;
            if (reports == null) { SetStatus("战报模块未就绪"); return; }
            if (reports.FetchReplay(battleId))
            {
                SetStatus("正在加载战报回放…");
            }
            RefreshInteractable();
        }

        private void ClearRows()
        {
            foreach (var row in _rows)
            {
                if (row == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(row);
                else UnityEngine.Object.DestroyImmediate(row);
            }
            _rows.Clear();
            _watchButtons.Clear();
        }
    }
}
