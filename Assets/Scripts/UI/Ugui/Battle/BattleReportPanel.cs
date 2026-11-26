using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Season;
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
    ///   - 每条显示胜负、模式、结束时间、回合数、双方成员(名字/等级/职业与职业图标)与「看回放」按钮;
    ///   - has_replay = false 时置灰为「无回放」;拉取回放期间防连点。
    /// </summary>
    public sealed class BattleReportPanel
    {
        private const float PanelX = 60f;
        private const float PanelY = 90f;
        private const float PanelW = 860f;
        private const float PanelH = 900f;

        private const float ListTop = 186f;
        private const float ListBottomMargin = 92f;
        private const float ListWidth = PanelW - 60f;
        private const float ListHeight = PanelH - ListTop - ListBottomMargin;
        private const float EntryH = 116f;
        private const float EntryGap = 8f;
        private const float ClassIconSize = 28f;
        private const float ClassIconGap = 4f;

        private static readonly ulong MaxUnixMilliseconds = (ulong)DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();
        private static readonly Color WinColor = QdaoUguiTheme.Html("#205B46");

        private readonly BattleUiRoot _owner;
        private readonly RectTransform _root;
        private readonly UiTextButton _refreshButton;
        private readonly UiTextButton _closeButton;
        private readonly TMP_Text _statsText;
        private readonly UiTextButton _filterAllButton;
        private readonly UiTextButton _filterWinButton;
        private readonly UiTextButton _filterLoseButton;
        private readonly UiTextButton _filterModeButton;
        private readonly UiTextButton _bookmarksToggleButton;
        private readonly RectTransform _content;
        private readonly ScrollRect _scroll;
        private readonly TMP_Text _emptyText;
        private readonly TMP_Text _statusText;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<(UiTextButton Button, bool HasReplay)> _watchButtons = new List<(UiTextButton, bool)>();
        private readonly List<Match.BattleReportSummary> _allReports = new List<Match.BattleReportSummary>();
        private readonly List<Match.MatchMode> _availableModes = new List<Match.MatchMode>();
        private ulong _myPlayerId;
        private BattleReportVerdictFilter _verdictFilter = BattleReportVerdictFilter.All;
        private Match.MatchMode _modeFilter = Match.MatchMode.Unspecified;
        private bool _showingBookmarks;
        private BattleReplay _inspectedReplay;

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

            _refreshButton = BattleUiWidgets.CreateTextButton("Refresh", _root, 30f, 78f, 152f, 46f,
                "刷新战报", 20f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _refreshButton.Button.onClick.AddListener(RefreshList);

            _statsText = QdaoUguiFactory.CreateText("StatsSummary", _root, 196f, 82f, PanelW - 226f, 38f,
                "保留最近 100 场 PVP 对局，点击「看回放」复盘", 18f, QdaoUguiTheme.StatusCream);
            _statsText.overflowMode = TextOverflowModes.Ellipsis;

            _filterAllButton = BattleUiWidgets.CreateTextButton("FilterAll", _root, 30f, 134f, 96f, 40f,
                "√ 全部", 18f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _filterAllButton.Button.onClick.AddListener(() => SetVerdictFilter(BattleReportVerdictFilter.All));

            _filterWinButton = BattleUiWidgets.CreateTextButton("FilterWin", _root, 134f, 134f, 96f, 40f,
                "胜局", 18f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _filterWinButton.Button.onClick.AddListener(() => SetVerdictFilter(BattleReportVerdictFilter.Win));

            _filterLoseButton = BattleUiWidgets.CreateTextButton("FilterLose", _root, 238f, 134f, 96f, 40f,
                "败局", 18f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _filterLoseButton.Button.onClick.AddListener(() => SetVerdictFilter(BattleReportVerdictFilter.Lose));

            _filterModeButton = BattleUiWidgets.CreateTextButton("FilterMode", _root, 348f, 134f, 224f, 40f,
                "模式：全部", 18f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _filterModeButton.Button.onClick.AddListener(CycleModeFilter);

            _bookmarksToggleButton = BattleUiWidgets.CreateTextButton("BookmarksToggle", _root, 586f, 134f, 210f, 40f,
                "配装收藏夹", 18f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _bookmarksToggleButton.Button.onClick.AddListener(() =>
            {
                _showingBookmarks = !_showingBookmarks;
                _inspectedReplay = null;
                if (_showingBookmarks) SeasonClient.Instance?.RequestState();
                RenderFiltered();
            });

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
            var season = SeasonClient.Instance;
            if (season != null)
            {
                season.Changed -= HandleSeasonBookmarksChanged;
                season.Changed += HandleSeasonBookmarksChanged;
            }
            RefreshInteractable();
        }

        public void Hide()
        {
            var season = SeasonClient.Instance;
            if (season != null) season.Changed -= HandleSeasonBookmarksChanged;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void HandleSeasonBookmarksChanged()
        {
            if (IsVisible && _showingBookmarks && _inspectedReplay == null)
                RenderFiltered();
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
            _myPlayerId = myPlayerId;
            _allReports.Clear();
            _availableModes.Clear();
            var seenModes = new HashSet<Match.MatchMode>();
            int count = reports?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var summary = reports[i];
                if (summary == null || summary.BattleId == 0) continue;
                _allReports.Add(summary);
                if (summary.Mode != Match.MatchMode.Unspecified && seenModes.Add(summary.Mode))
                    _availableModes.Add(summary.Mode);
            }
            if (_modeFilter != Match.MatchMode.Unspecified && !seenModes.Contains(_modeFilter))
                _modeFilter = Match.MatchMode.Unspecified;

            UpdateStatsHeader();
            RenderFiltered();
        }

        public void SetVerdictFilter(BattleReportVerdictFilter filter)
        {
            _verdictFilter = filter;
            RenderFiltered();
        }

        public void SetModeFilter(Match.MatchMode mode)
        {
            _modeFilter = mode;
            RenderFiltered();
        }

        private void CycleModeFilter()
        {
            if (_availableModes.Count == 0)
            {
                SetModeFilter(Match.MatchMode.Unspecified);
                return;
            }
            if (_modeFilter == Match.MatchMode.Unspecified)
            {
                SetModeFilter(_availableModes[0]);
                return;
            }
            int idx = _availableModes.IndexOf(_modeFilter);
            SetModeFilter(idx >= 0 && idx + 1 < _availableModes.Count
                ? _availableModes[idx + 1]
                : Match.MatchMode.Unspecified);
        }

        private void UpdateStatsHeader()
        {
            if (_statsText == null) return;
            var stats = BattleReplayAnalyzer.ComputeListStats(_allReports, _myPlayerId);
            if (stats.Total == 0)
            {
                _statsText.text = "保留最近 100 场 PVP 对局，点击「看回放」复盘";
                return;
            }
            string streak = stats.CurrentStreak >= 2
                ? $" · {stats.CurrentStreak}连胜"
                : stats.CurrentStreak <= -2
                    ? $" · {-stats.CurrentStreak}连败"
                    : string.Empty;
            string drawPart = stats.Draws > 0 ? $" {stats.Draws}平" : string.Empty;
            _statsText.text = $"近 {stats.Total} 场 · 胜率 {stats.WinRatePercent}% · {stats.Wins}胜 {stats.Losses}负{drawPart}{streak}";
        }

        private void RenderFiltered()
        {
            ClearRows();
            if (_bookmarksToggleButton != null)
                _bookmarksToggleButton.SetText(_showingBookmarks ? "√ 返回战报" : "配装收藏夹");

            if (_inspectedReplay != null)
            {
                RenderInspectedBuilds(_inspectedReplay);
            }
            else if (_showingBookmarks)
            {
                RenderBookmarksView();
            }
            else
            {
                var visible = BattleReplayAnalyzer.FilterReports(_allReports, _myPlayerId, _verdictFilter, _modeFilter);
                for (int i = 0; i < visible.Count; i++)
                {
                    BuildRow(visible[i], _rows.Count, _myPlayerId);
                }
            }

            float contentH = Mathf.Max(ListHeight, _rows.Count * (EntryH + EntryGap));
            _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentH);
            _scroll.StopMovement();
            _content.anchoredPosition = Vector2.zero;
            _scroll.verticalNormalizedPosition = 1f;

            _filterAllButton?.SetText(_verdictFilter == BattleReportVerdictFilter.All ? "√ 全部" : "全部");
            _filterWinButton?.SetText(_verdictFilter == BattleReportVerdictFilter.Win ? "√ 胜局" : "胜局");
            _filterLoseButton?.SetText(_verdictFilter == BattleReportVerdictFilter.Lose ? "√ 败局" : "败局");
            _filterModeButton?.SetText(_modeFilter == Match.MatchMode.Unspecified
                ? "模式：全部"
                : $"模式：{BattleReportClient.ModeName(_modeFilter)}");

            bool filtered = _verdictFilter != BattleReportVerdictFilter.All || _modeFilter != Match.MatchMode.Unspecified;
            if (_inspectedReplay != null)
            {
                _emptyText.text = "该场旧战报未记录公开配装摘要";
                _emptyText.gameObject.SetActive(_inspectedReplay.ActorBuilds.Count == 0);
            }
            else if (_showingBookmarks)
            {
                _emptyText.text = "暂无收藏配装，点击战报「看配装」即可一键收藏";
                _emptyText.gameObject.SetActive(_rows.Count == 0);
            }
            else
            {
                _emptyText.text = _allReports.Count == 0 ? "暂无 PVP 战报" : "当前筛选条件下暂无战报";
                _emptyText.gameObject.SetActive(_rows.Count == 0);
                SetStatus(_allReports.Count == 0
                    ? string.Empty
                    : filtered
                        ? $"已筛选 {_rows.Count} / {_allReports.Count} 场战报"
                        : $"共 {_rows.Count} 场战报");
            }
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
                string className = BattleReportClient.ClassName(member.ClassId);
                string item = !string.IsNullOrEmpty(className) && member.Level > 0
                    ? $"{name}({className} Lv.{member.Level})"
                    : !string.IsNullOrEmpty(className)
                        ? $"{name}({className})"
                        : member.Level > 0
                            ? $"{name}(Lv.{member.Level})"
                            : name;
                if (member.TeamIndex == 0) teamA.Add(item);
                else teamB.Add(item);
            }
            string left = teamA.Count > 0 ? string.Join("、", teamA) : "A队";
            string right = teamB.Count > 0 ? string.Join("、", teamB) : "B队";
            return $"{left}  VS  {right}";
        }

        /// <summary>职业图标资源名(与选角 RoleFlowUi.ClassBadges 同口径;未知/未带职业回落太极徽章)。</summary>
        public static string ClassIconAssetName(uint classId) => classId switch
        {
            1 => "round_badge_sword",
            2 => "round_badge_water",
            3 => "round_badge_furnace",
            4 => "round_badge_mountain",
            _ => "round_badge_taiji",
        };

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
            string stampKey = verdict switch
            {
                BattleReportVerdict.Win => BattleReportUiArt.StampWin,
                BattleReportVerdict.Lose => BattleReportUiArt.StampLose,
                _ => BattleReportUiArt.StampDraw,
            };
            var badgeBg = BattleUiWidgets.CreatePanel("VerdictBadge", rowRect,
                14f, 28f, 60f, 60f, badgeColor, raycastTarget: false);
            BattleReportUiArt.ApplyOptional(badgeBg, stampKey, badgeColor);
            var badgeText = QdaoUguiFactory.CreateText("VerdictText", badgeBg.transform,
                0f, 0f, 60f, 60f, VerdictLabel(verdict), 28f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            badgeText.fontStyle = FontStyles.Bold;

            float textX = 88f;
            float textW = ListWidth - textX - 178f;
            var head = QdaoUguiFactory.CreateText("Head", rowRect, textX, 10f, textW, 30f,
                $"{BattleReportClient.ModeName(summary.Mode)} · {FormatTimestamp(summary.FinishedAtMs)} · 共 {summary.TotalRounds} 回合",
                20f, QdaoUguiTheme.Cream);
            head.textWrappingMode = TextWrappingModes.NoWrap;
            head.overflowMode = TextOverflowModes.Ellipsis;

            var members = QdaoUguiFactory.CreateText("Members", rowRect, textX, 44f, textW, 28f,
                FormatMembers(summary), 18f, QdaoUguiTheme.StatusCream);
            members.textWrappingMode = TextWrappingModes.NoWrap;
            members.overflowMode = TextOverflowModes.Ellipsis;

            BuildClassIcons(rowRect, summary, textX, 76f, textW);

            bool hasReplay = summary.HasReplay;
            var buildButton = BattleUiWidgets.CreateTextButton("InspectBuild", rowRect,
                ListWidth - 294f, 30f, 124f, 56f,
                "看配装", 18f,
                hasReplay ? BattleUiStyle.ButtonPlate : BattleUiStyle.ButtonPlate,
                BattleUiStyle.ButtonText);
            buildButton.SetInteractable(hasReplay);
            if (hasReplay)
            {
                ulong battleId = summary.BattleId;
                buildButton.Button.onClick.AddListener(() => OnInspectBuildClicked(battleId));
            }
            _watchButtons.Add((buildButton, hasReplay));

            var watchButton = BattleUiWidgets.CreateTextButton("WatchReplay", rowRect,
                ListWidth - 160f, 30f, 144f, 56f,
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

        private static void BuildClassIcons(RectTransform rowRect, Match.BattleReportSummary summary,
            float x, float y, float maxWidth)
        {
            var container = QdaoUguiFactory.CreateRect("ClassIcons", rowRect, x, y, maxWidth, ClassIconSize + 2f);
            if (summary == null || summary.Members.Count == 0) return;

            var teamA = new List<uint>();
            var teamB = new List<uint>();
            foreach (var member in summary.Members)
            {
                if (member == null) continue;
                if (member.TeamIndex == 0) teamA.Add(member.ClassId);
                else teamB.Add(member.ClassId);
            }

            float cursorX = 0f;
            for (int i = 0; i < teamA.Count && cursorX + ClassIconSize <= maxWidth; i++)
            {
                AddClassIcon(container, $"TeamA_ClassIcon_{i}", cursorX, teamA[i]);
                cursorX += ClassIconSize + ClassIconGap;
            }
            if (teamA.Count > 0 && teamB.Count > 0 && cursorX + 28f <= maxWidth)
            {
                var vs = QdaoUguiFactory.CreateText("ClassIconsVs", container, cursorX, 0f, 28f, ClassIconSize,
                    "VS", 15f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.Center);
                vs.raycastTarget = false;
                cursorX += 28f + ClassIconGap;
            }
            for (int i = 0; i < teamB.Count && cursorX + ClassIconSize <= maxWidth; i++)
            {
                AddClassIcon(container, $"TeamB_ClassIcon_{i}", cursorX, teamB[i]);
                cursorX += ClassIconSize + ClassIconGap;
            }
        }

        private static void AddClassIcon(RectTransform parent, string name, float x, uint classId)
        {
            var icon = QdaoUguiFactory.CreateImage(name, parent, x, 0f, ClassIconSize, ClassIconSize,
                QdaoRefreshArt.Load(ClassIconAssetName(classId)), raycastTarget: false);
            icon.preserveAspect = true;
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

        private void OnInspectBuildClicked(ulong battleId)
        {
            var reports = _owner?.Reports;
            if (reports == null) { SetStatus("战报模块未就绪"); return; }
            if (reports.FetchReplay(battleId, replay =>
            {
                _inspectedReplay = replay;
                _showingBookmarks = false;
                RenderFiltered();
            }))
            {
                SetStatus("正在加载该局公开配装摘要…");
            }
            RefreshInteractable();
        }

        private void RenderInspectedBuilds(BattleReplay replay)
        {
            var headerRow = BattleUiWidgets.CreatePanel("BuildInspectHead", _content,
                0f, 0f, ListWidth, EntryH, BattleUiStyle.PanelBgLight);
            var hRect = (RectTransform)headerRow.transform;
            _rows.Add(headerRow.gameObject);
            QdaoUguiFactory.CreateText("InspectTitle", hRect, 18f, 14f, ListWidth - 200f, 38f,
                $"对局 #{replay.BattleId} 公开脱敏配装（共 {replay.ActorBuilds.Count} 人）", 21f, QdaoUguiTheme.Cream);
            QdaoUguiFactory.CreateText("InspectSub", hRect, 18f, 58f, ListWidth - 200f, 34f,
                "仅展示公开阵法、五行、赛季印与出战灵宠，不含私密属性与背包", 16f, QdaoUguiTheme.StatusCream);
            var backBtn = BattleUiWidgets.CreateTextButton("BackToReports", hRect, ListWidth - 168f, 30f, 152f, 54f,
                "返回列表", 18f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            backBtn.Button.onClick.AddListener(() => { _inspectedReplay = null; RenderFiltered(); });

            if (replay.StartState != null && replay.StartState.BannedSkillIds.Count > 0)
            {
                float banY = _rows.Count * (EntryH + EntryGap);
                var banRow = BattleUiWidgets.CreatePanel("BuildInspectBans", _content,
                    0f, banY, ListWidth, EntryH, BattleUiStyle.PanelBgLight);
                var bRect = (RectTransform)banRow.transform;
                _rows.Add(banRow.gameObject);
                QdaoUguiFactory.CreateText("InspectBanTitle", bRect, 18f, 14f, ListWidth - 36f, 34f,
                    $"本局战前禁招（共禁 {replay.StartState.BannedSkillIds.Count} 招 · 双方对称生效）",
                    19f, QdaoUguiTheme.Cream);
                QdaoUguiFactory.CreateText("InspectBanDetail", bRect, 18f, 56f, ListWidth - 36f, 44f,
                    BattleSkillBanModels.FormatTeamBansSummary(
                        replay.StartState.TeamABannedSkillIds,
                        replay.StartState.TeamBBannedSkillIds),
                    17f, QdaoUguiTheme.StatusCream);
            }

            for (int i = 0; i < replay.ActorBuilds.Count; ++i)
            {
                var build = replay.ActorBuilds[i];
                if (build == null) continue;
                float y = _rows.Count * (EntryH + EntryGap);
                var row = BattleUiWidgets.CreatePanel($"Build_{build.PlayerId}_{i}", _content,
                    0f, y, ListWidth, EntryH, BattleUiStyle.PanelBgLight);
                var rRect = (RectTransform)row.transform;
                _rows.Add(row.gameObject);

                string teamTag = build.TeamIndex == 0u ? "[A队]" : "[B队]";
                QdaoUguiFactory.CreateText("BuildHead", rRect, 18f, 14f, ListWidth - 200f, 34f,
                    $"{teamTag} {WuxingExpansionModels.FormatBuildSummary(build)}", 18f, QdaoUguiTheme.Cream);
                QdaoUguiFactory.CreateText("BuildSkills", rRect, 18f, 56f, ListWidth - 200f, 44f,
                    $"技能数：{build.SkillTableIds.Count} · 幻境符印：{build.WuxingSigilIds.Count} 枚", 16f, QdaoUguiTheme.StatusCream);

                var captured = build;
                ulong battleId = replay.BattleId;
                var saveBtn = BattleUiWidgets.CreateTextButton("SaveBookmark", rRect, ListWidth - 168f, 30f, 152f, 54f,
                    "一键收藏", 18f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
                saveBtn.Button.onClick.AddListener(() =>
                {
                    var season = SeasonClient.Instance;
                    if (season == null) { SetStatus("赛季模块未就绪"); return; }
                    var bm = new global::SavedBattleBuildBookmarkView
                    {
                        Title = string.IsNullOrWhiteSpace(captured.Name) ? $"战报#{battleId}" : $"{captured.Name}的配装",
                        SourceBattleId = battleId,
                        SourcePlayerId = captured.PlayerId,
                        SourcePlayerName = captured.Name ?? string.Empty,
                        ClassId = captured.ClassId,
                        FormationId = captured.FormationId,
                        SeasonSealId = captured.SeasonSealId,
                        ActivePetTableId = captured.ActivePetTableId,
                        ActivePetName = captured.ActivePetName ?? string.Empty,
                    };
                    for (int s = 0; s < captured.SkillTableIds.Count; ++s) bm.SkillTableIds.Add(captured.SkillTableIds[s]);
                    for (int g = 0; g < captured.WuxingSigilIds.Count; ++g) bm.WuxingSigilIds.Add(captured.WuxingSigilIds[g]);
                    season.SaveBattleBuildBookmark(bm);
                    SetStatus($"已提交收藏：{bm.Title}");
                });
            }
            SetStatus($"正在查看战报 #{replay.BattleId} 的公开配装");
        }

        private void RenderBookmarksView()
        {
            var season = SeasonClient.Instance;
            var builds = season?.SavedBuilds;
            int count = builds?.Count ?? 0;
            for (int i = 0; i < count; ++i)
            {
                var bm = builds[i];
                if (bm == null) continue;
                float y = _rows.Count * (EntryH + EntryGap);
                var row = BattleUiWidgets.CreatePanel($"Bm_{bm.BookmarkId}", _content,
                    0f, y, ListWidth, EntryH, BattleUiStyle.PanelBgLight);
                var rRect = (RectTransform)row.transform;
                _rows.Add(row.gameObject);

                QdaoUguiFactory.CreateText("BmTitle", rRect, 18f, 12f, ListWidth - 340f, 34f,
                    $"{bm.Title} · {WuxingExpansionModels.FormationName(bm.FormationId)}", 19f, QdaoUguiTheme.Cream);
                string detail = $"来源：{bm.SourcePlayerName} · 印#{bm.SeasonSealId} · 宠：{bm.ActivePetName}";
                QdaoUguiFactory.CreateText("BmDetail", rRect, 18f, 52f, ListWidth - 340f, 44f,
                    detail, 16f, QdaoUguiTheme.StatusCream);

                uint formId = bm.FormationId;
                uint sealId = bm.SeasonSealId;
                ulong bmId = bm.BookmarkId;
                var applyBtn = BattleUiWidgets.CreateTextButton("ApplyBm", rRect, ListWidth - 310f, 30f, 154f, 54f,
                    "套用阵/印", 17f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
                applyBtn.Button.onClick.AddListener(() =>
                {
                    if (season == null) return;
                    bool needFormation = formId <= 5u && formId != season.EquippedFormationId;
                    if (needFormation)
                    {
                        season.EquipFormation(formId);
                        SetStatus($"已切换阵法：{WuxingExpansionModels.FormationName(formId)}" +
                            (sealId > 0u ? "（再点一次可套用赛季印）" : string.Empty));
                    }
                    else if (sealId > 0u)
                    {
                        season.EquipSeal(sealId);
                        SetStatus($"已套用赛季印 #{sealId}");
                    }
                    else
                    {
                        SetStatus("当前阵法与赛季印已与该收藏一致");
                    }
                });
                var delBtn = BattleUiWidgets.CreateTextButton("DelBm", rRect, ListWidth - 144f, 30f, 128f, 54f,
                    "删除", 17f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
                delBtn.Button.onClick.AddListener(() =>
                {
                    season?.DeleteBattleBuildBookmark(bmId);
                    SetStatus("已请求删除收藏配装，请稍候刷新");
                });
            }
            SetStatus($"配装收藏夹：{count} / {WuxingExpansionModels.MaxSavedBuildBookmarks}");
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
