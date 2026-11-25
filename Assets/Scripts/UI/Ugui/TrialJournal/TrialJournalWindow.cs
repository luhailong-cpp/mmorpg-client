using System;
using System.Collections.Generic;
using MmorpgClient.Game.TrialJournal;
using MmorpgClient.Table;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.TrialJournal
{
    /// <summary>
    /// 五行试炼志主窗口（1920×1080 道家 Q 版三栏手记布局，严格还原 D:\work\image\designs\trial-journal-ui-v1 验收图）。
    /// 只负责纯表现与交互事件回调，不持有网络对象，不改写服务端权威快照。
    /// </summary>
    internal sealed class TrialJournalWindow : IDisposable
    {
        private readonly RectTransform _root;
        private readonly RectTransform _inner;
        private readonly RectTransform _headerArea;
        private readonly RectTransform _leftRail;
        private readonly RectTransform _centerPane;
        private readonly RectTransform _rightPane;
        private readonly RectTransform _footerArea;
        private readonly RectTransform _modalLayer;
        private readonly Button _closeButton;
        private readonly RectTransform _closeButtonRect;

        private QdaoCharacterCatalog.CreationPortraitLease _grandmasterLease;
        private TrialJournalNavTab _activeTab = TrialJournalNavTab.Overview;
        private uint _selectedEntryId;
        private uint _selectedArtifactItemId;
        private uint _pendingRedeemItemId;
        private string _pendingRedeemName = "";
        private TrialJournalCommentary _readingCommentary;
        private bool _compactCloseOnLeft;

        // 用于缓存上次非空快照，满足九态中「刷新保留旧图」与「断线保留旧快照」要求
        private global::TrialJournalSnapshot _lastValidSnapshot;
        private bool _demoBadgeVisible;

        public event Action CloseRequested;
        public event Action<global::TrialJournalEntryView> JumpRequested;
        public event Action<uint> ClaimMilestoneRequested;
        public event Action<uint> RedeemVoucherRequested;
        public event Action RefreshRequested;
        public event Action OpenAppearanceRequested;

        public bool HasModalOpen => _readingCommentary != null || _pendingRedeemItemId != 0u;
        public TrialJournalNavTab ActiveTab => _activeTab;
        public uint SelectedEntryId => _selectedEntryId;

        public TrialJournalWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateRect("TrialJournalWindow", parent, 0f, 0f,
                TrialJournalUiArt.DesignWidth, TrialJournalUiArt.DesignHeight);

            // 外框与顶部云纹标题牌（严格按 style.css 尺寸定位，底板无字）
            var frame = TrialJournalUiArt.SlicedOrTint("MainFrame", _root,
                50f, 135f, 1820f, 865f, TrialJournalUiArt.LoadChronicle("main_frame"),
                TrialJournalUiArt.Paper, raycast: true);
            frame.pixelsPerUnitMultiplier = 1.05f;

            var titlePlate = TrialJournalUiArt.FixedArt("TitlePlate", _root,
                605f, 78f, 710f, 145f, TrialJournalUiArt.LoadChronicle("title_plate"),
                TrialJournalUiArt.Jade, preserveAspect: true);
            TrialJournalUiArt.Label("TitleMain", titlePlate.transform,
                90f, 18f, 530f, 64f, "试 炼 志", 46f, TrialJournalUiArt.ButtonCream,
                bold: true, wrap: false, alignment: TextAlignmentOptions.Center);
            TrialJournalUiArt.Label("TitleSub", titlePlate.transform,
                140f, 76f, 430f, 32f, "道 印 与 宗 师 之 路", 17f, TrialJournalUiArt.ButtonCream,
                bold: false, wrap: false, alignment: TextAlignmentOptions.Center);

            _closeButton = QdaoUguiFactory.CreateArtButton("CloseButton", _root,
                1754f, 166f, 70f, 70f, TrialJournalUiArt.LoadChronicle("close"), out var closeImg);
            closeImg.type = Image.Type.Simple;
            closeImg.preserveAspect = true;
            closeImg.color = closeImg.sprite != null ? Color.white : TrialJournalUiArt.Jade;
            _closeButton.onClick.AddListener(() => CloseRequested?.Invoke());
            _closeButtonRect = (RectTransform)_closeButton.transform;

            // 内容容器 (108, 231, 1704×716)
            _inner = QdaoUguiFactory.CreateRect("WindowInner", _root, 108f, 231f, 1704f, 716f);
            _headerArea = QdaoUguiFactory.CreateRect("WindowHead", _inner, 0f, 0f, 1704f, 46f);
            _leftRail = QdaoUguiFactory.CreateRect("LeftRail", _inner, 0f, 58f, 252f, 606f);
            _centerPane = QdaoUguiFactory.CreateRect("CenterPane", _inner, 274f, 58f, 886f, 606f);
            _rightPane = QdaoUguiFactory.CreateRect("RightPane", _inner, 1188f, 58f, 516f, 606f);
            _footerArea = QdaoUguiFactory.CreateRect("WindowFoot", _inner, 0f, 676f, 1704f, 40f);

            _modalLayer = QdaoUguiFactory.CreateStretch("ModalLayer", _root, Vector4.zero);
            _modalLayer.gameObject.SetActive(false);
        }

        public void SetCompactCloseOnLeft(bool closeOnLeft)
        {
            if (_compactCloseOnLeft == closeOnLeft || _closeButtonRect == null) return;
            _compactCloseOnLeft = closeOnLeft;
            float x = closeOnLeft ? 96f : 1754f;
            _closeButtonRect.anchoredPosition = new Vector2(x, -166f);
        }

        public void ResetForSession()
        {
            _lastValidSnapshot = null;
            _selectedEntryId = 0u;
            _selectedArtifactItemId = 0u;
            _pendingRedeemItemId = 0u;
            _pendingRedeemName = "";
            _readingCommentary = null;
            _activeTab = TrialJournalNavTab.Overview;
            _demoBadgeVisible = false;
            HideModals();
        }

        public void FocusTarget(global::TrialJournalSnapshot snapshot, uint entryId, uint chapterId)
        {
            if (entryId != 0u)
            {
                _selectedEntryId = entryId;
                _activeTab = TrialJournalUiState.ResolveTabForEntry(snapshot, entryId);
            }
            else if (chapterId != 0u)
            {
                _activeTab = chapterId switch
                {
                    TrialJournalModels.ChapterBreakthrough => TrialJournalNavTab.Breakthrough,
                    TrialJournalModels.ChapterThreeRealms => TrialJournalNavTab.ThreeRealms,
                    TrialJournalModels.ChapterSeasonal => TrialJournalNavTab.Seasonal,
                    _ => TrialJournalNavTab.Overview,
                };
            }
        }

        public bool DismissModalIfOpen()
        {
            if (_pendingRedeemItemId != 0u)
            {
                _pendingRedeemItemId = 0u;
                _pendingRedeemName = "";
                HideModals();
                return true;
            }
            if (_readingCommentary != null)
            {
                _readingCommentary = null;
                HideModals();
                return true;
            }
            return false;
        }

        public void OpenCommentary(TrialJournalCommentary commentary)
        {
            if (commentary == null || string.IsNullOrWhiteSpace(commentary.Title)) return;
            _pendingRedeemItemId = 0u;
            _readingCommentary = commentary;
            RenderModal();
        }

        public void Render(
            global::TrialJournalSnapshot liveSnapshot,
            bool snapshotPending,
            bool actionPending,
            bool ready,
            bool requiresReconnect,
            string clientStatus,
            bool statusIsError,
            bool demoMode = false)
        {
            _demoBadgeVisible = demoMode;
            if (liveSnapshot != null && liveSnapshot.Available)
            {
                _lastValidSnapshot = liveSnapshot;
            }
            else if (liveSnapshot == null && ready && !requiresReconnect && !snapshotPending)
            {
                // 切角或主动复位时清空旧缓存；刷新中/断线保留上次有效快照
                _lastValidSnapshot = null;
            }

            var effectiveSnapshot = (liveSnapshot != null && liveSnapshot.Available)
                ? liveSnapshot
                : _lastValidSnapshot;

            var view = TrialJournalUiState.Evaluate(
                effectiveSnapshot, snapshotPending, actionPending, ready,
                requiresReconnect, clientStatus, statusIsError,
                _activeTab, _selectedEntryId);

            EnsureDefaultSelections(effectiveSnapshot);
            RenderHeader(view);
            RenderLeftRail(effectiveSnapshot, view);
            RenderCenterPane(effectiveSnapshot, view);
            RenderRightGrandmasterPane(effectiveSnapshot, view);
            RenderFooter(effectiveSnapshot, view);
            RenderModal();
        }

        private void EnsureDefaultSelections(global::TrialJournalSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Available) return;
            if (_activeTab != TrialJournalNavTab.Milestones)
            {
                var visible = TrialJournalUiState.FilterEntries(snapshot, _activeTab);
                bool found = false;
                for (int i = 0; i < visible.Count; i++)
                {
                    if (visible[i].EntryId == _selectedEntryId) { found = true; break; }
                }
                if (!found && visible.Count > 0)
                    _selectedEntryId = visible[0].EntryId;
            }
            if (snapshot.RedeemableArtifacts != null && snapshot.RedeemableArtifacts.Count > 0)
            {
                bool foundArt = false;
                for (int i = 0; i < snapshot.RedeemableArtifacts.Count; i++)
                {
                    if (snapshot.RedeemableArtifacts[i].ItemConfigId == _selectedArtifactItemId)
                    {
                        foundArt = true;
                        break;
                    }
                }
                if (!foundArt)
                    _selectedArtifactItemId = snapshot.RedeemableArtifacts[0].ItemConfigId;
            }
        }

        private void RenderHeader(TrialJournalUiView view)
        {
            TrialJournalUiArt.ClearChildren(_headerArea);
            // 底部分割金线
            TrialJournalUiArt.BorderedBox("HeadRule", _headerArea, 0f, 44f, 1704f, 1.5f,
                TrialJournalUiArt.GoldSoft, TrialJournalUiArt.GoldSoft, 0.5f);

            if (!string.IsNullOrEmpty(view.AlertText))
            {
                Color bg = view.AlertTone == TrialJournalBannerTone.Error ? TrialJournalUiArt.ErrorBg : TrialJournalUiArt.InfoBg;
                Color border = view.AlertTone == TrialJournalBannerTone.Error
                    ? QdaoUguiTheme.Html("#D8A899")
                    : TrialJournalUiArt.InfoBorder;
                Color ink = view.AlertTone == TrialJournalBannerTone.Error ? TrialJournalUiArt.ErrorInk : TrialJournalUiArt.Jade;
                var pill = TrialJournalUiArt.BorderedBox("HeaderAlert", _headerArea, 6f, 4f, 1320f, 34f, bg, border, 1f);
                TrialJournalUiArt.Label("AlertText", pill.transform, 14f, 0f, 1292f, 34f,
                    view.AlertText, 16f, ink, bold: false, wrap: false);
            }
            else
            {
                TrialJournalUiArt.Label("HeadIntro", _headerArea, 8f, 4f, 1280f, 34f,
                    $"◇  修行有迹，试炼成章  |  {view.SeasonLabel}", 19f, TrialJournalUiArt.Ink, bold: false);
            }

            // 右侧规则版号 + 同步状态胶囊 + 手动刷新
            TrialJournalUiArt.Label("RuleLabel", _headerArea, 1346f, 4f, 210f, 34f,
                view.RuleLabel, 17f, TrialJournalUiArt.Sub, bold: false, wrap: false,
                alignment: TextAlignmentOptions.MidlineRight);

            Color syncBg = view.SyncPillTone switch
            {
                TrialJournalBannerTone.Error => TrialJournalUiArt.ErrorBg,
                TrialJournalBannerTone.Warn => TrialJournalUiArt.WarnBg,
                _ => TrialJournalUiArt.InfoBg,
            };
            Color syncBorder = view.SyncPillTone switch
            {
                TrialJournalBannerTone.Error => QdaoUguiTheme.Html("#D8B0A0"),
                TrialJournalBannerTone.Warn => QdaoUguiTheme.Html("#DDC090"),
                _ => TrialJournalUiArt.InfoBorder,
            };
            Color syncInk = view.SyncPillTone switch
            {
                TrialJournalBannerTone.Error => TrialJournalUiArt.ErrorInk,
                TrialJournalBannerTone.Warn => TrialJournalUiArt.WarnInk,
                _ => TrialJournalUiArt.Jade,
            };
            var syncBtnImg = TrialJournalUiArt.BorderedBox("SyncPill", _headerArea, 1568f, 5f, 128f, 32f,
                syncBg, syncBorder, 1f, raycast: true);
            var syncBtn = syncBtnImg.gameObject.AddComponent<Button>();
            syncBtn.targetGraphic = syncBtnImg;
            syncBtn.onClick.AddListener(() => RefreshRequested?.Invoke());
            TrialJournalUiArt.Label("SyncText", syncBtn.transform, 4f, 0f, 120f, 32f,
                view.SyncPillText, 15f, syncInk, bold: true, wrap: false,
                alignment: TextAlignmentOptions.Center);
        }

        private void RenderLeftRail(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            TrialJournalUiArt.ClearChildren(_leftRail);
            // 右侧竖向金线
            TrialJournalUiArt.BorderedBox("RailDivider", _leftRail, 250f, 0f, 1.5f, 602f,
                TrialJournalUiArt.GoldSoft, TrialJournalUiArt.GoldSoft, 0.5f);

            TrialJournalUiArt.Label("RailTitle", _leftRail, 8f, 0f, 224f, 34f,
                "试炼目录", 28f, TrialJournalUiArt.Jade, bold: true);
            TrialJournalUiArt.Label("RailSub", _leftRail, 8f, 35f, 224f, 18f,
                "TRIAL JOURNAL", 11f, TrialJournalUiArt.MutedLabel, bold: false);

            var tabs = new List<(TrialJournalNavTab tab, string title, string sub, int badge)>();
            tabs.Add((TrialJournalNavTab.Overview, "总览", "全卷道印与试炼", 0));
            if (snapshot == null || TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterBreakthrough) != null)
            {
                int c1 = TrialJournalModels.CountClaimableMilestones(snapshot, TrialJournalModels.ChapterBreakthrough);
                tabs.Add((TrialJournalNavTab.Breakthrough, "卷一 · 破阵", "回春与护势 · 12印", c1));
            }
            if (snapshot != null && TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterThreeRealms) != null)
            {
                int c2 = TrialJournalModels.CountClaimableMilestones(snapshot, TrialJournalModels.ChapterThreeRealms);
                tabs.Add((TrialJournalNavTab.ThreeRealms, "卷二 · 灵障", "灵障与玄毒 · 12印", c2));
            }
            if (snapshot != null && TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterSeasonal) != null)
            {
                int c3 = TrialJournalModels.CountClaimableMilestones(snapshot, TrialJournalModels.ChapterSeasonal);
                tabs.Add((TrialJournalNavTab.Seasonal, "卷三 · 季游", "破阵之季 · 独立存档", c3));
            }
            tabs.Add((TrialJournalNavTab.Milestones, "里程碑录", "拓印 · 砂 · 留灯评语", view.ClaimableMilestoneCount));

            float y = 66f;
            for (int i = 0; i < tabs.Count; i++)
            {
                var item = tabs[i];
                bool selected = item.tab == _activeTab;
                Color bg = selected ? TrialJournalUiArt.SelectedBg : TrialJournalUiArt.CardBg;
                Color border = selected ? TrialJournalUiArt.SelectedLine : QdaoUguiTheme.Html("#D8C8A7");
                var plate = TrialJournalUiArt.BorderedBox($"RailTab_{item.tab}", _leftRail,
                    4f, y, 226f, 74f, bg, border, selected ? 2f : 1f, raycast: true);
                var btn = plate.gameObject.AddComponent<Button>();
                btn.targetGraphic = plate;
                var targetTab = item.tab;
                btn.onClick.AddListener(() =>
                {
                    if (_activeTab == targetTab) return;
                    _activeTab = targetTab;
                    _selectedEntryId = 0u;
                    RefreshRequested?.Invoke(); // 触发一次轻量重绘，不强刷网络
                });
                TrialJournalUiArt.Label("Title", plate.transform, 16f, 10f, 168f, 30f,
                    item.title, 21f, selected ? TrialJournalUiArt.Jade : TrialJournalUiArt.Ink, bold: true);
                TrialJournalUiArt.Label("Sub", plate.transform, 16f, 42f, 198f, 22f,
                    item.sub, 13f, TrialJournalUiArt.Sub, bold: false);

                if (item.badge > 0)
                {
                    var dot = QdaoUguiFactory.CreateImage("Badge", plate.transform, 188f, 12f, 26f, 26f,
                        TrialJournalUiArt.CircleSprite());
                    dot.color = TrialJournalUiArt.SealGold;
                    TrialJournalUiArt.Label("BadgeNum", dot.transform, 0f, 0f, 26f, 26f,
                        item.badge.ToString(), 13f, TrialJournalUiArt.ButtonCream, bold: true,
                        alignment: TextAlignmentOptions.Center);
                }
                y += 84f;
            }

            // 底部说明
            TrialJournalUiArt.BorderedBox("NoteDivider", _leftRail, 8f, 504f, 220f, 1f,
                TrialJournalUiArt.GoldSoft, TrialJournalUiArt.GoldSoft, 0.5f);
            var noteDot = QdaoUguiFactory.CreateImage("NoteDot", _leftRail, 10f, 524f, 10f, 10f,
                TrialJournalUiArt.CircleSprite());
            noteDot.color = QdaoUguiTheme.Html("#67A07B");
            TrialJournalUiArt.Label("NoteText", _leftRail, 28f, 516f, 200f, 82f,
                "每枚道印均有来处；永久卷与季游卷分别陈列，零数值打卡压力。", 14f,
                QdaoUguiTheme.Html("#816D4C"), bold: false, wrap: true, alignment: TextAlignmentOptions.TopLeft);
        }

        private void RenderCenterPane(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            TrialJournalUiArt.ClearChildren(_centerPane);

            // 1. 顶部三张统计卡片 (h=106)
            bool loading = view.Phase == TrialJournalVisualPhase.FirstLoad;
            string permVal = loading ? "— / 18" : $"{view.PermanentLitSeals} / {view.GrandmasterRequiredSeals}";
            string permSub = loading
                ? "等待首次快照"
                : view.GrandmasterReached
                    ? $"总永久 {view.PermanentLitSeals}/{view.PermanentTotalSeals} · 宗师已达成"
                    : $"总永久 {view.PermanentLitSeals}/{view.PermanentTotalSeals} · 距宗师差 {view.GrandmasterRequiredSeals - view.PermanentLitSeals} 印";
            DrawMetricCard(0f, "已点亮永久道印", permVal, permSub);

            string chapVal = loading ? "— / —" : $"{view.ActiveChapterLitSeals} / {view.ActiveChapterTotalSeals}";
            string chapSub = loading ? "首领 · 灵障 · 季游" : $"{view.ActiveChapterLabel} 进度";
            DrawMetricCard(299f, _activeTab == TrialJournalNavTab.Seasonal ? "本季试炼" : "当前卷进度", chapVal, chapSub);

            string claimVal = loading ? "— 项" : $"{view.ClaimableMilestoneCount} 项";
            string claimSub = loading
                ? "里程碑与法宝凭券"
                : $"拓印 ×{view.VoucherBalance}  ·  自用砂 ×{view.BoundSandBalance}";
            DrawMetricCard(598f, "待领取里程碑", claimVal, claimSub);

            // 2. 分节标题条 (section_plate)
            string sectionTitle = view.Phase == TrialJournalVisualPhase.FirstLoad ? "正在读取试炼"
                : _activeTab == TrialJournalNavTab.Milestones ? "印记里程碑与评语"
                : _activeTab == TrialJournalNavTab.Breakthrough ? "卷一 · 破阵试炼"
                : _activeTab == TrialJournalNavTab.ThreeRealms ? "卷二 · 灵障玄毒"
                : _activeTab == TrialJournalNavTab.Seasonal ? "卷三 · 季游奇谈 S1"
                : view.Phase == TrialJournalVisualPhase.Empty ? "尚未开启的旅程" : "正在进行的试炼";
            string sectionSide = _activeTab == TrialJournalNavTab.Seasonal
                ? "本季独立存档 · 错过不影响永久印"
                : _activeTab == TrialJournalNavTab.Milestones
                    ? "一次性绑定拓印 · 自用重铸砂 · 零战力荣誉"
                    : "点击关卡展开战法印明细与配装复盘";

            var secImg = TrialJournalUiArt.SlicedOrTint("SectionPlate", _centerPane,
                0f, 118f, 372f, 56f, TrialJournalUiArt.LoadChronicle("section_plate"),
                TrialJournalUiArt.Jade);
            TrialJournalUiArt.Label("SectionTitle", secImg.transform, 24f, 0f, 324f, 56f,
                sectionTitle, 24f, TrialJournalUiArt.ButtonCream, bold: true,
                alignment: TextAlignmentOptions.Center);
            TrialJournalUiArt.Label("SectionSide", _centerPane, 386f, 128f, 496f, 36f,
                sectionSide, 15f, TrialJournalUiArt.MutedLabel, bold: false,
                alignment: TextAlignmentOptions.MidlineRight);

            // 3. 中部可滚动列表区 (y=180, h=286)
            var listViewport = QdaoUguiFactory.CreateRect("ListViewport", _centerPane, 0f, 180f, 886f, 286f);
            listViewport.gameObject.AddComponent<RectMask2D>();
            var vpHit = listViewport.gameObject.AddComponent<Image>();
            vpHit.color = new Color(1f, 1f, 1f, 0.001f);
            vpHit.raycastTarget = true;

            var listContent = QdaoUguiFactory.CreateRect("ListContent", listViewport, 0f, 0f, 886f, 286f);
            var scroll = listViewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = listViewport;
            scroll.content = listContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;

            float contentHeight = 0f;
            if (loading)
            {
                contentHeight = RenderSkeletonRows(listContent);
            }
            else if (_activeTab == TrialJournalNavTab.Milestones)
            {
                contentHeight = RenderMilestoneRows(listContent, snapshot, view);
            }
            else
            {
                contentHeight = RenderEntryRows(listContent, snapshot, view);
            }
            listContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(286f, contentHeight));
            scroll.verticalNormalizedPosition = 1f;

            // 4. 底部法宝凭券八选一区域 (y=472, h=134)
            RenderArtifactVoucherStrip(snapshot, view);
        }

        private void DrawMetricCard(float x, string title, string value, string sub)
        {
            var card = TrialJournalUiArt.BorderedBox("Metric_" + title, _centerPane,
                x, 0f, 287f, 106f, TrialJournalUiArt.CardBg, QdaoUguiTheme.Html("#D5C19C"), 1.2f);
            // 左侧细玉绿竖条（对齐 .metric::before）
            QdaoUguiFactory.CreateImage("Accent", card.transform, 12f, 14f, 3.5f, 78f, null)
                .color = QdaoUguiTheme.Html("#93B49B");
            TrialJournalUiArt.Label("Label", card.transform, 24f, 10f, 250f, 24f,
                title, 15f, TrialJournalUiArt.Sub);
            TrialJournalUiArt.Label("Value", card.transform, 24f, 34f, 250f, 40f,
                value, 31f, TrialJournalUiArt.Jade, bold: true);
            TrialJournalUiArt.Label("Sub", card.transform, 24f, 76f, 250f, 22f,
                sub, 12.5f, TrialJournalUiArt.MutedLabel);
        }

        private float RenderSkeletonRows(RectTransform parent)
        {
            float y = 4f;
            for (int i = 0; i < 3; i++)
            {
                var row = TrialJournalUiArt.BorderedBox($"Skeleton_{i}", parent,
                    0f, y, 882f, 88f, TrialJournalUiArt.SkeletonBg, QdaoUguiTheme.Html("#E1D7C3"), 1f);
                var circle = QdaoUguiFactory.CreateImage("Avatar", row.transform, 16f, 12f, 64f, 64f,
                    TrialJournalUiArt.CircleSprite());
                circle.color = TrialJournalUiArt.SkeletonBar;
                QdaoUguiFactory.CreateImage("Line1", row.transform, 98f, 22f, 380f, 16f, null)
                    .color = TrialJournalUiArt.SkeletonBar;
                QdaoUguiFactory.CreateImage("Line2", row.transform, 98f, 48f, 250f, 12f, null)
                    .color = TrialJournalUiArt.SkeletonBar;
                QdaoUguiFactory.CreateImage("Btn", row.transform, 748f, 24f, 112f, 38f, null)
                    .color = TrialJournalUiArt.SkeletonBar;
                y += 96f;
            }
            return y;
        }

        private float RenderEntryRows(
            RectTransform parent,
            global::TrialJournalSnapshot snapshot,
            TrialJournalUiView view)
        {
            var entries = TrialJournalUiState.FilterEntries(snapshot, _activeTab);
            if (entries.Count == 0)
            {
                var empty = TrialJournalUiArt.BorderedBox("EmptyPanel", parent,
                    0f, 4f, 882f, 276f, TrialJournalUiArt.Paper, TrialJournalUiArt.Gold, 1.5f);
                TrialJournalUiArt.Label("Mark", empty.transform, 0f, 46f, 882f, 56f,
                    "◈", 48f, TrialJournalUiArt.Gold, alignment: TextAlignmentOptions.Center);
                TrialJournalUiArt.Label("Title", empty.transform, 40f, 112f, 802f, 38f,
                    "当前目录暂无已开放试炼", 26f, TrialJournalUiArt.Jade, bold: true,
                    alignment: TextAlignmentOptions.Center);
                TrialJournalUiArt.Label("Body", empty.transform, 40f, 158f, 802f, 32f,
                    "待对应章节或赛季开启后，新的战法印将在此呈现。", 16f, TrialJournalUiArt.Sub,
                    alignment: TextAlignmentOptions.Center);
                return 286f;
            }

            float y = 4f;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool selected = entry.EntryId == _selectedEntryId;
                float rowH = selected ? 186f : 90f;

                var rowImg = TrialJournalUiArt.SlicedOrTint($"Entry_{entry.EntryId}", parent,
                    0f, y, 882f, rowH, TrialJournalUiArt.LoadChronicle("member_row"),
                    selected ? TrialJournalUiArt.SelectedBg : TrialJournalUiArt.CardBg, raycast: true);
                if (selected)
                {
                    rowImg.color = TrialJournalUiArt.SelectedBg;
                    var outline = rowImg.gameObject.AddComponent<Outline>();
                    outline.effectColor = TrialJournalUiArt.SelectedLine;
                    outline.effectDistance = new Vector2(2f, -2f);
                }
                var rowBtn = rowImg.gameObject.AddComponent<Button>();
                rowBtn.targetGraphic = rowImg;
                uint capturedEntryId = entry.EntryId;
                rowBtn.onClick.AddListener(() =>
                {
                    _selectedEntryId = capturedEntryId;
                    RefreshRequested?.Invoke();
                });

                // 左侧头像框：PVE 复用宝宝美术库高清立绘，PVP 显示徽章
                var portraitBg = QdaoUguiFactory.CreateImage("PortraitBg", rowImg.transform,
                    14f, 10f, 70f, 70f, TrialJournalUiArt.CircleSprite());
                portraitBg.color = QdaoUguiTheme.Html("#E6EAD6");
                if (TrialJournalModels.TryGetEntryMonsterIds(entry.EntryId, out uint bossId, out uint guardId))
                {
                    var bossSprite = BattleArtCatalog.LoadMonsterPortrait(bossId);
                    var bossImg = TrialJournalUiArt.FixedArt("BossPortrait", rowImg.transform,
                        16f, 12f, 66f, 66f, bossSprite, Color.clear, preserveAspect: true);
                    if (guardId != 0u)
                    {
                        var guardBg = QdaoUguiFactory.CreateImage("GuardBg", rowImg.transform,
                            60f, 54f, 28f, 28f, TrialJournalUiArt.CircleSprite());
                        guardBg.color = TrialJournalUiArt.Paper;
                        TrialJournalUiArt.FixedArt("GuardPortrait", guardBg.transform,
                            2f, 2f, 24f, 24f, BattleArtCatalog.LoadMonsterPortrait(guardId),
                            Color.clear, preserveAspect: true);
                    }
                }
                else
                {
                    string pvpMark = entry.SourceKind == 3u ? "易" : "武";
                    TrialJournalUiArt.Label("PvpMark", portraitBg.transform, 0f, 0f, 70f, 70f,
                        pvpMark, 30f, TrialJournalUiArt.Jade, bold: true, alignment: TextAlignmentOptions.Center);
                }

                // 中间标题 + 小印章状态行
                int litCount = 0;
                int totalSeals = entry.Seals?.Count ?? 0;
                for (int s = 0; s < totalSeals; s++)
                    if (entry.Seals[s].Lit) litCount++;

                string diffTag = entry.Difficulty == 2u ? "困难" : entry.Difficulty == 1u ? "普通" : "季游";
                TrialJournalUiArt.Label("EntryTitle", rowImg.transform, 98f, 10f, 456f, 30f,
                    $"{entry.DisplayName}  [{diffTag}]", 20f,
                    selected ? TrialJournalUiArt.Jade : TrialJournalUiArt.Ink, bold: true);

                // 2–3 枚小印章徽标（图形+文字双标识，不只靠颜色）
                float sealX = 98f;
                for (int s = 0; s < totalSeals; s++)
                {
                    var seal = entry.Seals[s];
                    var pill = TrialJournalUiArt.BorderedBox($"SealChip_{s}", rowImg.transform,
                        sealX, 46f, 114f, 28f,
                        seal.Lit ? TrialJournalUiArt.SealGoldFill : TrialJournalUiArt.SealDimFill,
                        seal.Lit ? TrialJournalUiArt.SealGold : TrialJournalUiArt.Gold, 1f);
                    string slotTag = TrialJournalModels.SealSlotLabel(seal.SealSlotKind);
                    string mark = seal.Lit ? "★" : "○";
                    TrialJournalUiArt.Label("Text", pill.transform, 4f, 0f, 106f, 28f,
                        $"{mark}{slotTag}·{seal.Title}", 12.5f,
                        seal.Lit ? QdaoUguiTheme.Html("#5E4B2B") : TrialJournalUiArt.SealDimInk,
                        bold: seal.Lit, alignment: TextAlignmentOptions.Center);
                    sealX += 120f;
                }

                // 右侧进度 + 单人最好回合
                TrialJournalUiArt.Label("ProgressNum", rowImg.transform, 578f, 12f, 146f, 30f,
                    $"{litCount} / {totalSeals}", 20f, QdaoUguiTheme.Html("#4A755A"),
                    bold: true, alignment: TextAlignmentOptions.MidlineRight);
                string recordMini = entry.SoloRecorded
                    ? $"单人 {entry.SoloBest.BestRounds} 回合"
                    : entry.CoopRecorded ? $"合战 {entry.CoopBest.BestRounds} 回合"
                    : entry.HasLegacyRecordOnly ? "旧版留档"
                    : "战法印进度";
                TrialJournalUiArt.Label("ProgressSub", rowImg.transform, 578f, 44f, 146f, 24f,
                    recordMini, 13.5f, TrialJournalUiArt.Sub, alignment: TextAlignmentOptions.MidlineRight);

                // 最右侧动作按钮
                bool jumpValid = TrialJournalModels.TryParseJumpTarget(entry.JumpTarget, out var jump);
                bool canClickJump = view.OnlineWriteEnabled && entry.CanEnter && jumpValid;
                string actionLabel = !entry.CanEnter ? "未解锁"
                    : jump.Kind == TrialJumpKind.RankedPvp ? "前往论剑"
                    : jump.Kind == TrialJumpKind.Polarity5v5 ? "报名 5V5"
                    : litCount == totalSeals && totalSeals > 0 ? "再次挑战" : "前往挑战";
                var capturedEntry = entry;
                TrialJournalUiArt.FlatActionButton($"Jump_{entry.EntryId}", rowImg.transform,
                    740f, 22f, 126f, 44f, actionLabel, 16f,
                    primary: selected && entry.CanEnter,
                    interactable: canClickJump,
                    onClick: () => JumpRequested?.Invoke(capturedEntry));

                // 展开态详情区（战法印逐条条件 + 单人配装复盘 / 好友合战纪录 / 准入原因）
                if (selected)
                {
                    TrialJournalUiArt.BorderedBox("DetailDivider", rowImg.transform,
                        16f, 88f, 850f, 1f, TrialJournalUiArt.GoldSoft, TrialJournalUiArt.GoldSoft, 0.5f);

                    float detailY = 94f;
                    for (int s = 0; s < totalSeals; s++)
                    {
                        var seal = entry.Seals[s];
                        string prefix = seal.Lit ? "● 已点亮" : "○ 未点亮";
                        string slot = TrialJournalModels.SealSlotLabel(seal.SealSlotKind);
                        TrialJournalUiArt.Label($"SealDesc_{s}", rowImg.transform, 20f, detailY, 540f, 22f,
                            $"{prefix} [{slot}·{seal.Title}] {seal.Desc}", 13f,
                            seal.Lit ? TrialJournalUiArt.Jade : TrialJournalUiArt.Sub, bold: seal.Lit);
                        detailY += 23f;
                    }

                    // 右半侧：战绩与复盘快照（或 PVP 说明 / 未解锁原因）
                    string rightLine1;
                    string rightLine2;
                    if (!entry.CanEnter && !string.IsNullOrEmpty(entry.EnterBlockedReason))
                    {
                        rightLine1 = $"准入提示：{entry.EnterBlockedReason}";
                        rightLine2 = entry.Desc;
                    }
                    else if (entry.SourceKind == 3u || entry.SourceKind == 4u)
                    {
                        rightLine1 = "赛季 PVP 题：仅点亮战法印，不计回合速通";
                        rightLine2 = jump.DisplayHint;
                    }
                    else
                    {
                        string soloText = entry.SoloRecorded
                            ? $"单人最佳 {entry.SoloBest.BestRounds} 回合 · {FormatLoadout(entry.SoloBest.Loadout)}"
                            : entry.HasLegacyRecordOnly
                                ? TrialJournalModels.LegacyRecordOnlyHint
                                : "单人破阵：通关后自动记录宝宝与法宝搭配";
                        string coopText = entry.CoopRecorded
                            ? $"好友合战：{TrialJournalModels.FormatCoopSummary(entry.CoopBest)}"
                            : "好友合战：2–5 人组队通关留念（不设全服竞速榜）";
                        rightLine1 = soloText;
                        rightLine2 = coopText;
                    }
                    TrialJournalUiArt.Label("Record1", rowImg.transform, 568f, 96f, 298f, 38f,
                        rightLine1, 13f, TrialJournalUiArt.Ink, bold: false, wrap: true,
                        alignment: TextAlignmentOptions.TopLeft);
                    TrialJournalUiArt.Label("Record2", rowImg.transform, 568f, 138f, 298f, 38f,
                        rightLine2, 12.5f, TrialJournalUiArt.Sub, bold: false, wrap: true,
                        alignment: TextAlignmentOptions.TopLeft);
                }

                y += rowH + 8f;
            }
            return y;
        }

        private float RenderMilestoneRows(
            RectTransform parent,
            global::TrialJournalSnapshot snapshot,
            TrialJournalUiView view)
        {
            if (snapshot?.Milestones == null || snapshot.Milestones.Count == 0)
            {
                TrialJournalUiArt.Label("NoMilestones", parent, 20f, 110f, 840f, 40f,
                    "暂无可用里程碑记录。", 18f, TrialJournalUiArt.Sub, alignment: TextAlignmentOptions.Center);
                return 286f;
            }

            float y = 4f;
            for (int i = 0; i < snapshot.Milestones.Count; i++)
            {
                var m = snapshot.Milestones[i];
                if (m == null) continue;
                bool highlight = m.Claimable;
                var card = TrialJournalUiArt.SlicedOrTint($"Milestone_{m.MilestoneId}", parent,
                    0f, y, 882f, 96f, TrialJournalUiArt.LoadChronicle("member_row"),
                    highlight ? TrialJournalUiArt.SelectedBg : TrialJournalUiArt.CardBg);
                if (highlight)
                {
                    card.color = TrialJournalUiArt.SelectedBg;
                    var outline = card.gameObject.AddComponent<Outline>();
                    outline.effectColor = TrialJournalUiArt.SelectedLine;
                    outline.effectDistance = new Vector2(2f, -2f);
                }

                string scopeTag = m.ScopeChapterId switch
                {
                    TrialJournalModels.ScopeAllPermanent => "全永久宗师",
                    TrialJournalModels.ChapterBreakthrough => "卷一",
                    TrialJournalModels.ChapterThreeRealms => "卷二",
                    TrialJournalModels.ChapterSeasonal => $"季游 S{m.SeasonId}",
                    _ => "试炼",
                };
                TrialJournalUiArt.Label("Title", card.transform, 18f, 10f, 470f, 28f,
                    $"[{scopeTag}] {m.Title}  （{m.CurrentSeals}/{m.RequiredSeals} 印）", 19f,
                    TrialJournalUiArt.Jade, bold: true);

                var rewardParts = new List<string>();
                if (m.BoundSandCount > 0) rewardParts.Add($"仅自用重铸砂 ×{m.BoundSandCount}");
                if (m.BoundArtifactVoucherCount > 0) rewardParts.Add($"自用法宝拓印 ×{m.BoundArtifactVoucherCount}");
                if (!string.IsNullOrEmpty(m.BadgeName)) rewardParts.Add($"印戳「{m.BadgeName}」");
                if (!string.IsNullOrEmpty(m.GrantAppearanceName)) rewardParts.Add($"专属形象「{m.GrantAppearanceName}」");
                if (!string.IsNullOrEmpty(m.CommentaryTitle)) rewardParts.Add($"《{m.CommentaryTitle}》");
                TrialJournalUiArt.Label("Rewards", card.transform, 18f, 40f, 610f, 24f,
                    string.Join("  ·  ", rewardParts), 14f, TrialJournalUiArt.Ink, bold: false);
                TrialJournalUiArt.Label("Desc", card.transform, 18f, 66f, 610f, 22f,
                    m.Desc, 12.5f, TrialJournalUiArt.Sub);

                bool hasCommentary = m.Claimed && !string.IsNullOrWhiteSpace(m.CommentaryTitle);
                if (hasCommentary)
                {
                    var commentary = new TrialJournalCommentary(m.MilestoneId, m.CommentaryKey, m.CommentaryTitle, m.CommentaryBody);
                    TrialJournalUiArt.FlatActionButton($"Read_{m.MilestoneId}", card.transform,
                        638f, 26f, 106f, 42f, "读评语", 15f, primary: false, interactable: true,
                        onClick: () => OpenCommentary(commentary));
                }

                string btnText = m.Claimed ? "已领取" : m.Claimable ? "收取" : "未达标";
                bool btnEnabled = view.OnlineWriteEnabled && m.Claimable;
                uint capturedId = m.MilestoneId;
                TrialJournalUiArt.FlatActionButton($"Claim_{m.MilestoneId}", card.transform,
                    754f, 26f, 112f, 42f, btnText, 16f,
                    primary: m.Claimable, interactable: btnEnabled,
                    onClick: () => ClaimMilestoneRequested?.Invoke(capturedId));

                y += 104f;
            }
            return y;
        }

        private void RenderArtifactVoucherStrip(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            var box = TrialJournalUiArt.BorderedBox("ArtifactSection", _centerPane,
                0f, 472f, 886f, 134f, new Color(1f, 0.98f, 0.93f, 0.65f),
                QdaoUguiTheme.Html("#D7C7A4"), 1.2f);

            TrialJournalUiArt.Label("ArtHeading", box.transform, 14f, 6f, 140f, 32f,
                "法宝凭券", 22f, TrialJournalUiArt.Jade, bold: true);
            TrialJournalUiArt.Label("VoucherHint", box.transform, 160f, 8f, 540f, 28f,
                view.VoucherHintText, 13.5f,
                view.Phase == TrialJournalVisualPhase.BagFullError ? TrialJournalUiArt.ErrorInk : TrialJournalUiArt.MutedLabel,
                alignment: TextAlignmentOptions.MidlineRight);

            TrialJournalUiArt.FlatActionButton("RedeemButton", box.transform,
                712f, 6f, 160f, 32f, view.RedeemButtonText, 14.5f,
                primary: true, interactable: view.RedeemButtonEnabled && _selectedArtifactItemId != 0u,
                onClick: RequestRedeemConfirmation);

            var list = snapshot?.RedeemableArtifacts;
            int count = list != null && list.Count > 0 ? Math.Min(list.Count, 8) : 8;
            bool tilesDisabled = !view.OnlineWriteEnabled || snapshot == null || snapshot.VoucherBalance == 0;
            for (int i = 0; i < count; i++)
            {
                float tx = 14f + i * 108f;
                var opt = list != null && i < list.Count ? list[i] : null;
                uint itemId = opt?.ItemConfigId ?? (uint)(3301 + i);
                uint effectId = opt?.ArtifactEffectId ?? (uint)(i + 1);
                string shortName = opt != null && !string.IsNullOrEmpty(opt.Name) ? opt.Name : $"法宝{i + 1}";
                bool selected = itemId == _selectedArtifactItemId && !tilesDisabled;

                var tile = TrialJournalUiArt.BorderedBox($"ArtifactTile_{itemId}", box.transform,
                    tx, 44f, 96f, 82f,
                    selected ? TrialJournalUiArt.SelectedBg : TrialJournalUiArt.Paper,
                    selected ? TrialJournalUiArt.SelectedLine : QdaoUguiTheme.Html("#DAC89E"),
                    selected ? 2f : 1f, raycast: true);
                var btn = tile.gameObject.AddComponent<Button>();
                btn.targetGraphic = tile;
                btn.interactable = opt != null;
                uint capturedItem = itemId;
                btn.onClick.AddListener(() =>
                {
                    _selectedArtifactItemId = capturedItem;
                    RefreshRequested?.Invoke();
                });

                var icon = TrialJournalUiArt.FixedArt("Icon", tile.transform,
                    20f, 4f, 56f, 56f, TrialJournalUiArt.LoadArtifactIcon(effectId),
                    Color.clear, preserveAspect: true);
                if (tilesDisabled) icon.color = new Color(1f, 1f, 1f, 0.65f);

                TrialJournalUiArt.Label("Name", tile.transform, 2f, 60f, 92f, 20f,
                    shortName, 12f, TrialJournalUiArt.Sub, alignment: TextAlignmentOptions.Center);

                if (opt != null && opt.OwnedCount > 0)
                {
                    var badge = TrialJournalUiArt.BorderedBox("Owned", tile.transform,
                        60f, 2f, 34f, 18f, TrialJournalUiArt.InfoBg, TrialJournalUiArt.InfoBorder, 0.8f);
                    TrialJournalUiArt.Label("Count", badge.transform, 0f, 0f, 34f, 18f,
                        $"×{opt.OwnedCount}", 11f, TrialJournalUiArt.Jade, bold: true,
                        alignment: TextAlignmentOptions.Center);
                }
            }
        }

        private void RenderRightGrandmasterPane(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            TrialJournalUiArt.ClearChildren(_rightPane);
            // 左侧竖向金线
            TrialJournalUiArt.BorderedBox("RightDivider", _rightPane, 0f, 0f, 1.5f, 602f,
                TrialJournalUiArt.GoldSoft, TrialJournalUiArt.GoldSoft, 0.5f);

            // 头部标题 + M901 标签
            TrialJournalUiArt.Label("MasterTitle", _rightPane, 18f, 0f, 240f, 38f,
                "星阵宗师", 30f, TrialJournalUiArt.Jade, bold: true);
            TrialJournalUiArt.Label("MasterTag", _rightPane, 260f, 8f, 238f, 28f,
                "M901 · 置顶里程碑", 14.5f, QdaoUguiTheme.Html("#947A53"),
                alignment: TextAlignmentOptions.MidlineRight);
            TrialJournalUiArt.BorderedBox("MasterRule", _rightPane, 18f, 42f, 480f, 1f,
                TrialJournalUiArt.GoldSoft, TrialJournalUiArt.GoldSoft, 0.5f);

            // 立绘底座 + 星阵宗师 1024×1024 HD 立绘
            var pedestal = TrialJournalUiArt.FixedArt("Pedestal", _rightPane,
                123f, 52f, 270f, 250f, TrialJournalUiArt.LoadChronicle("portrait_pedestal"),
                TrialJournalUiArt.CardBg, preserveAspect: false);
            var gmSprite = AcquireGrandmasterPortrait();
            var gmImg = TrialJournalUiArt.FixedArt("GrandmasterPortrait", pedestal.transform,
                15f, 6f, 240f, 218f, gmSprite, Color.clear, preserveAspect: true);
            if (!view.GrandmasterReached && gmSprite != null)
                gmImg.color = new Color(0.92f, 0.90f, 0.86f, 0.94f);

            // 标题与说明
            TrialJournalUiArt.Label("MasterHeadline", _rightPane, 18f, 306f, 480f, 32f,
                view.GrandmasterTitle, 23f, TrialJournalUiArt.Jade, bold: true,
                alignment: TextAlignmentOptions.Center);
            TrialJournalUiArt.Label("MasterDesc", _rightPane, 28f, 338f, 460f, 44f,
                view.GrandmasterDesc, 13.5f, TrialJournalUiArt.Sub, bold: false, wrap: true,
                alignment: TextAlignmentOptions.Top);

            // 18 枚宗师道印 2×9 圆形网格（对齐 04-in-progress.png / 05-grandmaster-18-seals.png）
            uint litSeals = Math.Min(view.PermanentLitSeals, 18u);
            var circle = TrialJournalUiArt.CircleSprite();
            for (int i = 0; i < 18; i++)
            {
                int col = i % 9;
                int row = i / 9;
                float sx = 24f + col * 52f;
                float sy = 392f + row * 50f;
                bool on = i < litSeals && view.Phase != TrialJournalVisualPhase.FirstLoad;
                var sealBg = QdaoUguiFactory.CreateImage($"Seal_{i + 1}", _rightPane, sx, sy, 44f, 44f, circle);
                sealBg.color = on ? TrialJournalUiArt.SealGoldFill : TrialJournalUiArt.SealDimFill;
                var outline = sealBg.gameObject.AddComponent<Outline>();
                outline.effectColor = on ? TrialJournalUiArt.SealGold : QdaoUguiTheme.Html("#C3B08C");
                outline.effectDistance = new Vector2(1.4f, -1.4f);
                TrialJournalUiArt.Label("Num", sealBg.transform, 0f, 0f, 44f, 44f,
                    (i + 1).ToString(), 17f,
                    on ? QdaoUguiTheme.Html("#5E4B2B") : TrialJournalUiArt.SealDimInk,
                    bold: true, alignment: TextAlignmentOptions.Center);
            }

            // 底部主按钮（button_primary）
            var gmMilestone = TrialJournalModels.FindGrandmasterMilestone(snapshot);
            TrialJournalUiArt.SpriteButton("MasterCta", _rightPane,
                115f, 504f, 286f, 56f, view.GrandmasterButtonText, 21f,
                primary: true, interactable: view.GrandmasterButtonEnabled,
                onClick: () => HandleGrandmasterCta(gmMilestone, view));

            string footnote = view.GrandmasterClaimed
                ? "点击前往易容衣柜即可随时切换「星阵宗师」"
                : "里程碑 M901 · 永久 18/24 印免费解锁（零战力）";
            var footLabel = TrialJournalUiArt.Label("MasterFoot", _rightPane, 18f, 566f, 480f, 26f,
                footnote, 12.5f, TrialJournalUiArt.MutedLabel, alignment: TextAlignmentOptions.Center);
            if (view.GrandmasterClaimed)
            {
                var hit = QdaoUguiFactory.CreateHitButton("OpenWardrobeHit", _rightPane, 115f, 564f, 286f, 28f);
                hit.onClick.AddListener(() => OpenAppearanceRequested?.Invoke());
                footLabel.color = TrialJournalUiArt.Jade;
            }
        }

        private void RenderFooter(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            TrialJournalUiArt.ClearChildren(_footerArea);
            TrialJournalUiArt.BorderedBox("FootRule", _footerArea, 0f, 0f, 1704f, 1f,
                TrialJournalUiArt.GoldSoft, TrialJournalUiArt.GoldSoft, 0.5f);
            TrialJournalUiArt.Label("FootLeft", _footerArea, 8f, 8f, 1280f, 28f,
                view.FooterText, 14f, QdaoUguiTheme.Html("#927E63"));
            string rightTag = _demoBadgeVisible
                ? "试炼志 · UI 演示数据"
                : snapshot != null && snapshot.Available
                    ? $"试炼志 · 权威修订 #{snapshot.StateRevision}"
                    : "五行试炼志";
            TrialJournalUiArt.Label("FootRight", _footerArea, 1300f, 8f, 396f, 28f,
                rightTag, 14f, TrialJournalUiArt.MutedLabel, alignment: TextAlignmentOptions.MidlineRight);
        }

        private void RenderModal()
        {
            TrialJournalUiArt.ClearChildren(_modalLayer);
            if (!HasModalOpen)
            {
                _modalLayer.gameObject.SetActive(false);
                return;
            }
            _modalLayer.gameObject.SetActive(true);
            _modalLayer.SetAsLastSibling();

            var dim = QdaoUguiFactory.CreateStretch("ModalDim", _modalLayer, Vector4.zero)
                .gameObject.AddComponent<Image>();
            dim.color = new Color(0.02f, 0.06f, 0.05f, 0.55f);
            dim.raycastTarget = true;

            if (_pendingRedeemItemId != 0u)
            {
                var card = TrialJournalUiArt.SlicedOrTint("RedeemConfirmCard", _modalLayer,
                    580f, 340f, 760f, 380f, TrialJournalUiArt.LoadChronicle("application_card"),
                    TrialJournalUiArt.Paper, raycast: true);
                TrialJournalUiArt.Label("Title", card.transform, 40f, 32f, 680f, 44f,
                    $"确认兑换自用法宝「{_pendingRedeemName}」？", 28f, TrialJournalUiArt.Jade,
                    bold: true, alignment: TextAlignmentOptions.Center);
                TrialJournalUiArt.Label("Body", card.transform, 52f, 102f, 656f, 130f,
                    "将消耗 1 张自用法宝拓印，兑换 1 件对应法宝放入人物背包。\n"
                    + "· 兑换所得法宝永久仅自用，不可交易、不可上架、不可拆解；\n"
                    + "· 允许兑换同名第二件以搭配双件流派。",
                    19f, TrialJournalUiArt.Ink, bold: false, wrap: true,
                    alignment: TextAlignmentOptions.TopLeft);
                TrialJournalUiArt.SpriteButton("Cancel", card.transform,
                    124f, 274f, 220f, 58f, "取消", 20f, primary: false, interactable: true,
                    onClick: () => { _pendingRedeemItemId = 0u; _pendingRedeemName = ""; HideModals(); });
                uint itemId = _pendingRedeemItemId;
                TrialJournalUiArt.SpriteButton("Confirm", card.transform,
                    416f, 274f, 220f, 58f, "确认兑换", 20f, primary: true, interactable: true,
                    onClick: () =>
                    {
                        _pendingRedeemItemId = 0u;
                        _pendingRedeemName = "";
                        HideModals();
                        RedeemVoucherRequested?.Invoke(itemId);
                    });
                return;
            }

            if (_readingCommentary != null)
            {
                var card = TrialJournalUiArt.SlicedOrTint("CommentaryCard", _modalLayer,
                    440f, 210f, 1040f, 640f, TrialJournalUiArt.LoadChronicle("application_card"),
                    TrialJournalUiArt.Paper, raycast: true);
                TrialJournalUiArt.Label("Chapter", card.transform, 48f, 30f, 944f, 28f,
                    "留 灯 院 · 试 炼 评 语", 17f, TrialJournalUiArt.MutedLabel,
                    alignment: TextAlignmentOptions.Center);
                TrialJournalUiArt.Label("Title", card.transform, 48f, 66f, 944f, 48f,
                    _readingCommentary.Title, 32f, TrialJournalUiArt.Jade, bold: true,
                    alignment: TextAlignmentOptions.Center);
                TrialJournalUiArt.BorderedBox("Divider", card.transform, 80f, 124f, 880f, 1.5f,
                    TrialJournalUiArt.GoldSoft, TrialJournalUiArt.GoldSoft, 0.5f);
                TrialJournalUiArt.Label("Body", card.transform, 72f, 152f, 896f, 368f,
                    _readingCommentary.Body, 22f, TrialJournalUiArt.Ink, bold: false, wrap: true,
                    alignment: TextAlignmentOptions.TopLeft);
                TrialJournalUiArt.SpriteButton("CloseCommentary", card.transform,
                    400f, 544f, 240f, 58f, "收起卷册", 20f, primary: true, interactable: true,
                    onClick: () => { _readingCommentary = null; HideModals(); });
            }
        }

        private void HideModals()
        {
            TrialJournalUiArt.ClearChildren(_modalLayer);
            if (_modalLayer != null) _modalLayer.gameObject.SetActive(false);
        }

        private void RequestRedeemConfirmation()
        {
            if (_selectedArtifactItemId == 0u || _lastValidSnapshot?.RedeemableArtifacts == null) return;
            string name = $"法宝#{_selectedArtifactItemId}";
            for (int i = 0; i < _lastValidSnapshot.RedeemableArtifacts.Count; i++)
            {
                var opt = _lastValidSnapshot.RedeemableArtifacts[i];
                if (opt != null && opt.ItemConfigId == _selectedArtifactItemId)
                {
                    name = opt.Name;
                    break;
                }
            }
            _readingCommentary = null;
            _pendingRedeemItemId = _selectedArtifactItemId;
            _pendingRedeemName = name;
            RenderModal();
        }

        private void HandleGrandmasterCta(global::TrialJournalMilestoneView gm, TrialJournalUiView view)
        {
            if (gm != null && gm.Claimed && !string.IsNullOrWhiteSpace(gm.CommentaryTitle))
            {
                OpenCommentary(new TrialJournalCommentary(gm.MilestoneId, gm.CommentaryKey, gm.CommentaryTitle, gm.CommentaryBody));
                return;
            }
            if (gm != null && gm.Claimable && view.OnlineWriteEnabled)
            {
                ClaimMilestoneRequested?.Invoke(gm.MilestoneId);
                return;
            }
            _activeTab = TrialJournalNavTab.Milestones;
            RefreshRequested?.Invoke();
        }

        private Sprite AcquireGrandmasterPortrait()
        {
            if (_grandmasterLease != null && _grandmasterLease.IsValid)
                return _grandmasterLease.Sprite;
            _grandmasterLease?.Dispose();
            _grandmasterLease = QdaoCharacterCatalog.AcquireCreationPortrait("20_star_formation_master_girl");
            return _grandmasterLease != null && _grandmasterLease.IsValid
                ? _grandmasterLease.Sprite
                : QdaoCharacterCatalog.LoadPortrait("20_star_formation_master_girl");
        }

        private static string FormatLoadout(global::TrialJournalLoadoutSnapshot loadout)
        {
            return TrialJournalModels.FormatSoloLoadoutSummary(
                loadout,
                id => PetTableManager.Instance.FindById(id)?.Name,
                id => EquipArtifactTableManager.Instance.FindById(id)?.Name,
                id => SeasonSealTableManager.Instance.FindById(id)?.Name);
        }

        public void Dispose()
        {
            _grandmasterLease?.Dispose();
            _grandmasterLease = null;
        }
    }
}
