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
    /// 五行试炼志 V2 主窗口（1920×1080 三栏手记布局，严格对齐已验收 TrialJournalV2 20 张无字切片与 15 张验收图）。
    /// 只负责纯表现与交互事件回调，不持有网络对象，不改写服务端权威快照。
    /// </summary>
    internal sealed class TrialJournalWindow : IDisposable
    {
        private readonly RectTransform _root;
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
        private uint _pendingRedeemEffectId;
        private string _pendingRedeemName = "";
        private TrialJournalCommentary _readingCommentary;
        private bool _compactCloseOnLeft;
        private uint _selectedVowMask;
        private Match.TrialChallengeCardView _activeChallengeCard;

        // 缓存上次有效快照，满足「刷新保留旧图」与「断线保留旧快照」要求
        private global::TrialJournalSnapshot _lastValidSnapshot;
        private bool _demoBadgeVisible;

        public event Action CloseRequested;
        public event Action<global::TrialJournalEntryView, uint> JumpRequested;
        public event Action<ulong> WatchReplayRequested;
        public event Action<global::TrialJournalEntryView, ulong> ShareChallengeRequested;
        public event Action<uint> ClaimMilestoneRequested;
        public event Action<uint> RedeemVoucherRequested;
        public event Action ViewChanged;
        public event Action RefreshRequested;
        public event Action OpenAppearanceRequested;

        public bool HasModalOpen => _readingCommentary != null || _pendingRedeemItemId != 0u;
        public TrialJournalNavTab ActiveTab => _activeTab;
        public uint SelectedEntryId => _selectedEntryId;
        public uint SelectedVowMask => _selectedVowMask;
        public Match.TrialChallengeCardView ActiveChallengeCard => _activeChallengeCard;

        public TrialJournalWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateRect("TrialJournalWindow", parent, 0f, 0f,
                TrialJournalUiArt.DesignWidth, TrialJournalUiArt.DesignHeight);

            // 主外框、居中标题牌与关闭按钮（对齐 TrialJournalV2 1920×1080 基准坐标）
            TrialJournalUiArt.SlicedOrTint("MainFrame", _root,
                50f, 108f, 1820f, 865f, TrialJournalUiArt.LoadSkin("main_frame", "main_frame"),
                TrialJournalUiArt.Paper, raycast: true);

            var titlePlate = TrialJournalUiArt.FixedArt("TitlePlate", _root,
                605f, 47f, 710f, 145f, TrialJournalUiArt.LoadSkin("title_plate", "title_plate"),
                TrialJournalUiArt.Jade, preserveAspect: true);
            TrialJournalUiArt.Label("TitleMain", titlePlate.transform,
                110f, 26f, 490f, 58f, "试 炼 志", 45f, TrialJournalUiArt.ButtonCream,
                bold: true, wrap: false, alignment: TextAlignmentOptions.Center);
            TrialJournalUiArt.Label("TitleSub", titlePlate.transform,
                140f, 78f, 430f, 30f, "五行试炼 · 道印行旅", 19f, TrialJournalUiArt.ButtonCream,
                bold: false, wrap: false, alignment: TextAlignmentOptions.Center);

            _closeButton = QdaoUguiFactory.CreateArtButton("CloseButton", _root,
                1780f, 115f, 70f, 70f, TrialJournalUiArt.LoadSkin("close_btn", "close"), out var closeImg);
            closeImg.type = Image.Type.Simple;
            closeImg.preserveAspect = true;
            closeImg.color = closeImg.sprite != null ? Color.white : TrialJournalUiArt.Jade;
            _closeButton.onClick.AddListener(() => CloseRequested?.Invoke());
            _closeButtonRect = (RectTransform)_closeButton.transform;

            // 顶栏状态与刷新按钮区 + 三栏内框容器（各自复用 modal_panel 28px 九宫底板）+ 底部信息条
            _headerArea = QdaoUguiFactory.CreateRect("WindowHead", _root, 115f, 183f, 1682f, 50f);
            _leftRail = QdaoUguiFactory.CreateRect("LeftRail", _root, 103f, 239f, 241f, 681f);
            _centerPane = QdaoUguiFactory.CreateRect("CenterPane", _root, 340f, 239f, 911f, 681f);
            _rightPane = QdaoUguiFactory.CreateRect("RightPane", _root, 1253f, 239f, 558f, 681f);
            _footerArea = QdaoUguiFactory.CreateRect("WindowFoot", _root, 115f, 922f, 1675f, 32f);

            _modalLayer = QdaoUguiFactory.CreateStretch("ModalLayer", _root, Vector4.zero);
            _modalLayer.gameObject.SetActive(false);
        }

        public void SetCompactCloseOnLeft(bool closeOnLeft)
        {
            if (_compactCloseOnLeft == closeOnLeft || _closeButtonRect == null) return;
            _compactCloseOnLeft = closeOnLeft;
            float x = closeOnLeft ? 96f : 1780f;
            _closeButtonRect.anchoredPosition = new Vector2(x, -115f);
        }

        public void ResetForSession()
        {
            _lastValidSnapshot = null;
            _selectedEntryId = 0u;
            _selectedArtifactItemId = 0u;
            _pendingRedeemItemId = 0u;
            _pendingRedeemEffectId = 0u;
            _pendingRedeemName = "";
            _readingCommentary = null;
            _activeTab = TrialJournalNavTab.Overview;
            _selectedVowMask = 0u;
            _activeChallengeCard = null;
            _demoBadgeVisible = false;
            HideModals();
        }

        public void SetChallengeTarget(global::TrialJournalSnapshot snapshot, Match.TrialChallengeCardView card)
        {
            _activeChallengeCard = card?.Clone();
            if (_activeChallengeCard != null)
            {
                _selectedVowMask = _activeChallengeCard.VowMask & TrialJournalModels.VowMaskAll;
                FocusTarget(snapshot, _activeChallengeCard.JournalEntryId, 0u);
            }
        }

        public void ClearChallengeTarget()
        {
            _activeChallengeCard = null;
            ViewChanged?.Invoke();
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
                    TrialJournalModels.ChapterWildRealms => TrialJournalNavTab.WildRealms,
                    _ => TrialJournalNavTab.Overview,
                };
            }
        }

        public bool DismissModalIfOpen()
        {
            if (_pendingRedeemItemId != 0u)
            {
                _pendingRedeemItemId = 0u;
                _pendingRedeemEffectId = 0u;
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
            _pendingRedeemEffectId = 0u;
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

            if (!string.IsNullOrEmpty(view.AlertText))
            {
                Color ink = view.AlertTone == TrialJournalBannerTone.Error
                    ? TrialJournalUiArt.ErrorInk
                    : view.AlertTone == TrialJournalBannerTone.Warn
                        ? TrialJournalUiArt.WarnInk
                        : TrialJournalUiArt.Jade;
                var banner = TrialJournalUiArt.SlicedOrBordered("HeaderAlert", _headerArea,
                    0f, 1f, 1340f, 48f, "entry_row_idle",
                    view.AlertTone == TrialJournalBannerTone.Error ? TrialJournalUiArt.ErrorBg : TrialJournalUiArt.InfoBg,
                    TrialJournalUiArt.Gold, 1f);
                TrialJournalUiArt.Label("AlertText", banner.transform, 24f, 0f, 1292f, 48f,
                    view.AlertText, 20f, ink, bold: false, wrap: false);
            }
            else
            {
                TrialJournalUiArt.Label("HeadIntro", _headerArea, 14f, 0f, 1340f, 50f,
                    $"{view.SeasonLabel}  ·  {view.RuleLabel}  ·  {view.SyncPillText}",
                    22f, TrialJournalUiArt.Jade, bold: false, wrap: false);
            }

            // 右侧手动刷新次按钮（高度 50px >= 44px 触控下限）
            string syncLabel = view.Phase == TrialJournalVisualPhase.Disconnected ? "重连后刷新"
                : view.Phase == TrialJournalVisualPhase.Refreshing ? "同步中…"
                : "同步 · 刷新";
            TrialJournalUiArt.SpriteButton("SyncRefresh", _headerArea,
                1470f, 0f, 212f, 50f, syncLabel, 19f,
                primary: false,
                interactable: view.Phase != TrialJournalVisualPhase.Disconnected,
                onClick: () => RefreshRequested?.Invoke());
        }

        private void RenderLeftRail(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            TrialJournalUiArt.ClearChildren(_leftRail);
            TrialJournalUiArt.SlicedOrBordered("LeftPanelBg", _leftRail,
                0f, 0f, 241f, 681f, "modal_panel",
                TrialJournalUiArt.Paper, TrialJournalUiArt.GoldSoft, 1.2f);

            var tabs = new List<(TrialJournalNavTab tab, string title, string sub, int badge)>();
            tabs.Add((TrialJournalNavTab.Overview, "总览", "总进度", 0));
            if (snapshot == null || TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterBreakthrough) != null)
            {
                var ch1 = TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterBreakthrough);
                uint ch1Seals = ch1 != null && ch1.TotalSealCount > 0u ? ch1.TotalSealCount : 15u;
                int ch1Count = ch1?.Entries?.Count > 0 ? ch1.Entries.Count : 5;
                int c1 = TrialJournalModels.CountClaimableMilestones(snapshot, TrialJournalModels.ChapterBreakthrough);
                tabs.Add((TrialJournalNavTab.Breakthrough, "卷一 · 破阵试炼", $"{ch1Count} 格 · {ch1Seals} 印", c1));
            }
            if (snapshot != null && TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterThreeRealms) != null)
            {
                var ch2 = TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterThreeRealms);
                uint ch2Seals = ch2 != null && ch2.TotalSealCount > 0u ? ch2.TotalSealCount : 12u;
                int ch2Count = ch2?.Entries?.Count > 0 ? ch2.Entries.Count : 4;
                int c2 = TrialJournalModels.CountClaimableMilestones(snapshot, TrialJournalModels.ChapterThreeRealms);
                tabs.Add((TrialJournalNavTab.ThreeRealms, "卷二 · 灵障玄毒", $"{ch2Count} 格 · {ch2Seals} 印", c2));
            }
            if (snapshot != null && TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterSeasonal) != null)
            {
                int c3 = TrialJournalModels.CountClaimableMilestones(snapshot, TrialJournalModels.ChapterSeasonal);
                tabs.Add((TrialJournalNavTab.Seasonal, "卷三 · 季游奇谈", "S1 独立归档", c3));
            }
            if (snapshot != null && TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterWildRealms) != null)
            {
                var ch4 = TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterWildRealms);
                uint ch4Seals = ch4 != null && ch4.TotalSealCount > 0u ? ch4.TotalSealCount : 9u;
                int ch4Count = ch4?.Entries?.Count > 0 ? ch4.Entries.Count : 3;
                int c4 = TrialJournalModels.CountClaimableMilestones(snapshot, TrialJournalModels.ChapterWildRealms);
                tabs.Add((TrialJournalNavTab.WildRealms, "卷四 · 三境明雷", $"{ch4Count} 格 · {ch4Seals} 印", c4));
            }
            int msTotal = snapshot?.Milestones?.Count > 0 ? snapshot.Milestones.Count : 13;
            tabs.Add((TrialJournalNavTab.Milestones, "里程碑录", $"{msTotal} 档奖励", view.ClaimableMilestoneCount));

            bool compactRail = tabs.Count >= 6;
            float tabH = compactRail ? 62f : 74f;
            float tabStep = compactRail ? 70f : 86f;
            float y = compactRail ? 18f : 27f;
            for (int i = 0; i < tabs.Count; i++)
            {
                var item = tabs[i];
                bool selected = item.tab == _activeTab;
                Color fallbackBg = selected ? TrialJournalUiArt.Jade : TrialJournalUiArt.CardBg;
                Color fallbackBorder = selected ? TrialJournalUiArt.SelectedLine : TrialJournalUiArt.Gold;
                var plate = TrialJournalUiArt.SlicedOrBordered($"RailTab_{item.tab}", _leftRail,
                    9f, y, 226f, tabH,
                    selected ? "rail_tab_selected" : "rail_tab_idle",
                    fallbackBg, fallbackBorder, selected ? 2f : 1f, raycast: true);
                var btn = plate.gameObject.AddComponent<Button>();
                btn.targetGraphic = plate;
                var targetTab = item.tab;
                btn.onClick.AddListener(() =>
                {
                    if (_activeTab == targetTab) return;
                    _activeTab = targetTab;
                    _selectedEntryId = 0u;
                    ViewChanged?.Invoke();
                });

                // 选中页签为深翠底，必须配浅米金文字；未选中为纸面底，配墨绿与暖褐
                Color titleColor = selected ? TrialJournalUiArt.ButtonCream : TrialJournalUiArt.Jade;
                Color subColor = selected ? TrialJournalUiArt.ButtonCream : TrialJournalUiArt.Sub;
                var titleLabel = TrialJournalUiArt.Label("Title", plate.transform, 10f, compactRail ? 6f : 10f, 206f, 28f,
                    item.title, compactRail ? 19.5f : 21f, titleColor, bold: selected, wrap: false,
                    alignment: TextAlignmentOptions.Center);
                titleLabel.enableAutoSizing = true;
                titleLabel.fontSizeMin = 15f;
                titleLabel.fontSizeMax = compactRail ? 19.5f : 21f;
                TrialJournalUiArt.Label("Sub", plate.transform, 10f, compactRail ? 34f : 41f, 206f, 22f,
                    item.sub, compactRail ? 14.5f : 15.5f, subColor, bold: false, wrap: false,
                    alignment: TextAlignmentOptions.Center);

                if (item.badge > 0)
                {
                    var dot = QdaoUguiFactory.CreateImage("Badge", plate.transform, 202f, -6f, 26f, 26f,
                        TrialJournalUiArt.CircleSprite());
                    dot.color = TrialJournalUiArt.BadgeRed;
                    var dotOutline = dot.gameObject.AddComponent<Outline>();
                    dotOutline.effectColor = TrialJournalUiArt.ButtonCream;
                    dotOutline.effectDistance = new Vector2(1.2f, -1.2f);
                    TrialJournalUiArt.Label("BadgeNum", dot.transform, 0f, 0f, 26f, 26f,
                        item.badge.ToString(), 14f, TrialJournalUiArt.ButtonCream, bold: true,
                        alignment: TextAlignmentOptions.Center);
                }
                y += tabStep;
            }

            // 左下「试炼札记」独立卡片（复用 modal_panel 28px 九宫，对齐验收图 112,714,226,183）
            var note = TrialJournalUiArt.SlicedOrBordered("RailNoteCard", _leftRail,
                9f, 475f, 226f, 183f, "modal_panel",
                TrialJournalUiArt.CardBg, TrialJournalUiArt.GoldSoft, 1f);
            TrialJournalUiArt.Label("NoteHead", note.transform, 16f, 14f, 194f, 32f,
                "试炼札记", 22f, TrialJournalUiArt.Jade, bold: false,
                alignment: TextAlignmentOptions.Center);
            QdaoUguiFactory.CreateImage("NoteRule", note.transform, 22f, 54f, 182f, 1.5f, null)
                .color = TrialJournalUiArt.Gold;

            uint permTotal = view.PermanentTotalSeals > 0u ? view.PermanentTotalSeals : 27u;
            TrialJournalUiArt.Label("Note1", note.transform, 22f, 68f, 184f, 28f,
                $"永久卷共 {permTotal} 印", 17f, TrialJournalUiArt.Ink);
            TrialJournalUiArt.Label("Note2", note.transform, 22f, 100f, 184f, 28f,
                $"宗师门槛 {view.GrandmasterRequiredSeals} 印", 17f, TrialJournalUiArt.Ink);
            TrialJournalUiArt.Label("Note3", note.transform, 22f, 132f, 184f, 28f,
                "卷三独立归档", 17f, TrialJournalUiArt.Sub);
        }

        private void RenderCenterPane(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            TrialJournalUiArt.ClearChildren(_centerPane);
            TrialJournalUiArt.SlicedOrBordered("CenterPanelBg", _centerPane,
                0f, 0f, 911f, 681f, "modal_panel",
                TrialJournalUiArt.Paper, TrialJournalUiArt.GoldSoft, 1.2f);

            // 1. 顶部三张统计指标卡 (15 + i*298, y=13, 287×106)
            bool loading = view.Phase == TrialJournalVisualPhase.FirstLoad;
            uint permTotal = view.PermanentTotalSeals > 0u ? view.PermanentTotalSeals : 27u;
            uint missingGm = view.GrandmasterRequiredSeals > view.PermanentLitSeals
                ? view.GrandmasterRequiredSeals - view.PermanentLitSeals
                : 0u;
            DrawMetricCard(15f, "永久道印",
                $"{view.PermanentLitSeals} / {permTotal}",
                view.GrandmasterReached ? "宗师门槛已达成" : $"距宗师还差 {missingGm} 印",
                loading);

            DrawMetricCard(313f, _activeTab == TrialJournalNavTab.Seasonal ? "本季进度" : "当前卷进度",
                $"{view.ActiveChapterLitSeals} / {view.ActiveChapterTotalSeals}",
                _activeTab == TrialJournalNavTab.Seasonal ? "本季独立归档" : "卷三当季另存",
                loading);

            DrawMetricCard(611f, "待领里程碑",
                view.ClaimableMilestoneCount > 0 ? $"可领取 {view.ClaimableMilestoneCount} 档" : "暂无",
                $"拓印 {view.VoucherBalance}  ·  自用砂 {view.BoundSandBalance}",
                loading);

            // 2. 分节标题条 (section_plate, 14, 133, 372×56)
            string sectionTitle = loading ? "正在载入试炼"
                : _activeTab == TrialJournalNavTab.Milestones ? "里程碑录"
                : _activeTab == TrialJournalNavTab.Breakthrough ? "卷一 · 破阵试炼"
                : _activeTab == TrialJournalNavTab.ThreeRealms ? "卷二 · 灵障玄毒"
                : _activeTab == TrialJournalNavTab.Seasonal ? "卷三 · 季游奇谈"
                : _activeTab == TrialJournalNavTab.WildRealms ? "卷四 · 三境明雷"
                : view.Phase == TrialJournalVisualPhase.Empty ? "尚未开启的旅程" : "五行试炼总览";
            string sectionSide = _activeChallengeCard != null
                ? $"应战中：{_activeChallengeCard.OwnerName} {_activeChallengeCard.TotalRounds}回合 · {TrialJournalModels.FormatVowSummary(_activeChallengeCard.VowMask)}"
                : _activeTab == TrialJournalNavTab.Seasonal
                    ? "3 格 · 6 印 · 当季独立"
                    : _activeTab == TrialJournalNavTab.WildRealms
                        ? "3 格 · 9 印 · 野外三境明雷"
                        : _activeTab == TrialJournalNavTab.Milestones
                            ? $"{snapshot?.Milestones?.Count ?? 13} 档奖励 · 5 篇评语"
                            : $"{view.ActiveChapterLitSeals}/{view.ActiveChapterTotalSeals} 印 · 可滚动查看";

            var secImg = TrialJournalUiArt.SlicedOrTint("SectionPlate", _centerPane,
                14f, 133f, 372f, 56f, TrialJournalUiArt.LoadSkin("section_plate", "section_plate"),
                TrialJournalUiArt.Jade);
            TrialJournalUiArt.Label("SectionTitle", secImg.transform, 32f, 0f, 308f, 56f,
                sectionTitle, 25f, TrialJournalUiArt.ButtonCream, bold: true,
                alignment: TextAlignmentOptions.Center);
            TrialJournalUiArt.Label("SectionSide", _centerPane, 398f, 143f, 492f, 36f,
                sectionSide, 18f, TrialJournalUiArt.Sub, bold: false,
                alignment: TextAlignmentOptions.MidlineRight);

            // 3. 中部可滚动列表区 (x=15, y=198, w=882, h=326)
            var listViewport = QdaoUguiFactory.CreateRect("ListViewport", _centerPane, 15f, 198f, 882f, 326f);
            listViewport.gameObject.AddComponent<RectMask2D>();
            var vpHit = listViewport.gameObject.AddComponent<Image>();
            vpHit.color = new Color(1f, 1f, 1f, 0.001f);
            vpHit.raycastTarget = true;

            var listContent = QdaoUguiFactory.CreateRect("ListContent", listViewport, 0f, 0f, 882f, 326f);
            var scroll = listViewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = listViewport;
            scroll.content = listContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;

            float contentHeight = loading
                ? RenderSkeletonRows(listContent)
                : _activeTab == TrialJournalNavTab.Milestones
                    ? RenderMilestoneRows(listContent, snapshot, view)
                    : RenderEntryRows(listContent, snapshot, view);
            listContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(326f, contentHeight));
            scroll.verticalNormalizedPosition = 1f;

            // 4. 底部法宝凭券八选一区域 (x=14, y=531, w=886, h=134)
            RenderArtifactVoucherStrip(snapshot, view);
        }

        private void DrawMetricCard(float x, string title, string value, string sub, bool skeleton)
        {
            var card = TrialJournalUiArt.SlicedOrBordered("Metric_" + title, _centerPane,
                x, 13f, 287f, 106f, "metric_card",
                TrialJournalUiArt.CardBg, TrialJournalUiArt.Gold, 1.2f);
            if (skeleton)
            {
                QdaoUguiFactory.CreateImage("Sk1", card.transform, 22f, 22f, 148f, 16f, null)
                    .color = TrialJournalUiArt.SkeletonBar;
                QdaoUguiFactory.CreateImage("Sk2", card.transform, 22f, 52f, 216f, 24f, null)
                    .color = QdaoUguiTheme.Html("#DDD9C7");
                return;
            }
            TrialJournalUiArt.Label("Label", card.transform, 21f, 12f, 246f, 26f,
                title, 19f, TrialJournalUiArt.Jade);
            var valLabel = TrialJournalUiArt.Label("Value", card.transform, 22f, 40f, 244f, 36f,
                value, 28f, TrialJournalUiArt.Jade, bold: true);
            valLabel.enableAutoSizing = true;
            valLabel.fontSizeMin = 20f;
            valLabel.fontSizeMax = 28f;
            TrialJournalUiArt.Label("Sub", card.transform, 21f, 76f, 246f, 22f,
                sub, 15.5f, TrialJournalUiArt.Sub);
        }

        private float RenderSkeletonRows(RectTransform parent)
        {
            float y = 4f;
            for (int i = 0; i < 3; i++)
            {
                var row = TrialJournalUiArt.SlicedOrBordered($"Skeleton_{i}", parent,
                    0f, y, 882f, 90f, "entry_row_idle",
                    TrialJournalUiArt.SkeletonBg, TrialJournalUiArt.GoldSoft, 1f);
                var circle = QdaoUguiFactory.CreateImage("Avatar", row.transform, 20f, 15f, 62f, 62f,
                    TrialJournalUiArt.CircleSprite());
                circle.color = TrialJournalUiArt.SkeletonBar;
                QdaoUguiFactory.CreateImage("Line1", row.transform, 108f, 24f, 400f, 18f, null)
                    .color = TrialJournalUiArt.SkeletonBar;
                QdaoUguiFactory.CreateImage("Line2", row.transform, 108f, 54f, 320f, 15f, null)
                    .color = QdaoUguiTheme.Html("#E0DBCB");
                y += 98f;
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
                var empty = TrialJournalUiArt.SlicedOrBordered("EmptyPanel", parent,
                    0f, 6f, 882f, 290f, "entry_row_idle",
                    TrialJournalUiArt.Paper, TrialJournalUiArt.Gold, 1.5f);
                TrialJournalUiArt.Label("Title", empty.transform, 40f, 104f, 802f, 38f,
                    "当前目录暂无已开放试炼", 26f, TrialJournalUiArt.Jade, bold: true,
                    alignment: TextAlignmentOptions.Center);
                TrialJournalUiArt.Label("Body", empty.transform, 40f, 154f, 802f, 32f,
                    "待对应章节或赛季开启后，新的战法印将在此呈现。", 17f, TrialJournalUiArt.Sub,
                    alignment: TextAlignmentOptions.Center);
                return 310f;
            }

            float y = 4f;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool selected = entry.EntryId == _selectedEntryId;
                ulong replayBattleId = ResolveEntryReplayBattleId(entry);
                bool showVowBar = selected && entry.VowSupported && entry.CanEnter;
                bool showReplayShareBar = selected && replayBattleId != 0ul && (entry.SourceKind == 1u || entry.SourceKind == 2u);
                float rowH = !selected
                    ? 90f
                    : 186f + (showVowBar ? 82f : 0f) + (showReplayShareBar ? 54f : 0f);

                var dedicatedRow = TrialJournalUiArt.LoadDedicatedOnly(selected ? "entry_row_selected" : "entry_row_idle");
                var rowImg = TrialJournalUiArt.SlicedOrTint($"Entry_{entry.EntryId}", parent,
                    0f, y, 882f, rowH, dedicatedRow ?? TrialJournalUiArt.LoadChronicle("member_row"),
                    selected ? TrialJournalUiArt.SelectedBg : TrialJournalUiArt.CardBg, raycast: true);
                if (selected && dedicatedRow == null)
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
                    ViewChanged?.Invoke();
                });

                // 左侧圆形头像 + portrait_frame（单卫右下 1 枚，105 双卫左下+右下 2 枚；PVP 格显示徽字）
                if (TrialJournalModels.TryGetEntryMonsterIds(entry.EntryId, out uint bossId, out uint guardId))
                {
                    bool locked = !entry.CanEnter;
                    TrialJournalUiArt.CircularPortrait("BossPortrait", rowImg.transform,
                        16f, selected ? 14f : 10f, 70f, BattleArtCatalog.LoadMonsterPortrait(bossId), locked);
                    var guards = TrialJournalModels.GetEntryGuardMonsterIds(entry.EntryId);
                    if (guards != null && guards.Count >= 2)
                    {
                        TrialJournalUiArt.CircularPortrait("GuardPortrait_0", rowImg.transform,
                            14f, 52f, 34f, BattleArtCatalog.LoadMonsterPortrait(guards[0]), locked);
                        TrialJournalUiArt.CircularPortrait("GuardPortrait_1", rowImg.transform,
                            60f, 52f, 34f, BattleArtCatalog.LoadMonsterPortrait(guards[1]), locked);
                    }
                    else if (guardId != 0u)
                    {
                        TrialJournalUiArt.CircularPortrait("GuardPortrait", rowImg.transform,
                            65f, 51f, 32f, BattleArtCatalog.LoadMonsterPortrait(guardId), locked);
                    }
                }
                else
                {
                    string pvpMark = entry.SourceKind == 3u ? "易" : "武";
                    TrialJournalUiArt.SealMedallion("PvpMark", rowImg.transform,
                        18f, selected ? 16f : 12f, 66f, lit: true, pvpMark, 28f);
                }

                // 中部标题与副标
                int litCount = 0;
                int totalSeals = entry.Seals?.Count ?? 0;
                for (int s = 0; s < totalSeals; s++)
                    if (entry.Seals[s].Lit) litCount++;

                string diffTag = entry.Difficulty == 2u ? "困难" : entry.Difficulty == 1u ? "普通" : "季游";
                TrialJournalUiArt.Label("EntryTitle", rowImg.transform, 104f, 14f, 412f, 32f,
                    $"{entry.EntryId}  {entry.DisplayName}", 24f, TrialJournalUiArt.Jade, bold: true);

                string subLine = !entry.CanEnter
                    ? "困难未解锁 · 需先通任意普通试炼"
                    : entry.SeasonId != 0u
                        ? "季游独立纪录 · 不计入永久印"
                        : $"{diffTag} · {(entry.EntryId == 105u ? "双卫首领格" : "永久首领格")}";
                TrialJournalUiArt.Label("EntrySub", rowImg.transform, 105f, 48f, 412f, 26f,
                    subLine, 17f, TrialJournalUiArt.Sub);

                // 中右侧 2–3 枚独立战法印圆徽（seal_medallion_lit/dim，图形+文字双区分）
                float sealBaseX = totalSeals <= 2 ? 560f : 526f;
                float sealStep = totalSeals <= 2 ? 56f : 50f;
                float sealY = selected ? 14f : 22f;
                for (int s = 0; s < totalSeals; s++)
                {
                    var seal = entry.Seals[s];
                    string slotTag = TrialJournalModels.SealSlotLabel(seal.SealSlotKind);
                    TrialJournalUiArt.SealMedallion($"Seal_{s}", rowImg.transform,
                        sealBaseX + s * sealStep, sealY, 44f, seal.Lit, slotTag, 16f);
                }

                if (selected)
                {
                    string recordMini = entry.SoloRecorded
                        ? $"最佳 {entry.SoloBest.BestRounds} 回合"
                        : entry.CoopRecorded ? $"合战 {entry.CoopBest.BestRounds} 回合"
                        : entry.HasLegacyRecordOnly ? "旧版留档"
                        : "尚无纪录";
                    TrialJournalUiArt.Label("ProgressLine", rowImg.transform, 524f, 62f, 176f, 24f,
                        $"{litCount} / {totalSeals} 印 · {recordMini}", 16f, TrialJournalUiArt.Jade);
                }

                // 最右侧动作按钮（高度 50px >= 44px 触控下限）
                bool jumpValid = TrialJournalModels.TryParseJumpTarget(entry.JumpTarget, out var jump);
                bool canClickJump = view.OnlineWriteEnabled && entry.CanEnter && jumpValid;
                string actionLabel = !entry.CanEnter ? "未解锁"
                    : jump.Kind == TrialJumpKind.RankedPvp ? "前往论剑"
                    : jump.Kind == TrialJumpKind.Polarity5v5 ? "报名 5V5"
                    : jump.RequiredSceneConfigId != 0u ? "前往三境"
                    : litCount == totalSeals && totalSeals > 0 ? "再次挑战" : "前往挑战";
                var capturedEntry = entry;
                uint activeVowMask = selected && entry.VowSupported ? _selectedVowMask : 0u;
                TrialJournalUiArt.SpriteButton($"Jump_{entry.EntryId}", rowImg.transform,
                    709f, 18f, 152f, 50f, actionLabel, 19f,
                    primary: selected && entry.CanEnter,
                    interactable: canClickJump,
                    onClick: () => JumpRequested?.Invoke(capturedEntry, activeVowMask));

                // 展开态详情区（y=91 分割线下方：战法印逐条状态 + 单人配装复盘 / 好友合战纪录）
                if (selected)
                {
                    QdaoUguiFactory.CreateImage("DetailRule", rowImg.transform, 104f, 91f, 754f, 1.5f, null)
                        .color = TrialJournalUiArt.Gold;

                    var sealParts = new List<string>(totalSeals);
                    for (int s = 0; s < totalSeals; s++)
                    {
                        var seal = entry.Seals[s];
                        string slot = TrialJournalModels.SealSlotLabel(seal.SealSlotKind);
                        sealParts.Add($"{slot}·{seal.Title}{(seal.Lit ? "（已亮）" : "（待解）")}");
                    }
                    TrialJournalUiArt.Label("SealLine", rowImg.transform, 104f, 97f, 754f, 26f,
                        string.Join("    ", sealParts), 17f, TrialJournalUiArt.Jade, bold: false);

                    string line2;
                    string line3;
                    if (!entry.CanEnter && !string.IsNullOrEmpty(entry.EnterBlockedReason))
                    {
                        line2 = $"准入提示：{entry.EnterBlockedReason}";
                        line3 = entry.Desc;
                    }
                    else if (entry.SourceKind == 3u || entry.SourceKind == 4u)
                    {
                        line2 = "赛季 PVP 题：仅点亮战法印，不计回合速通";
                        line3 = BuildFirstUnlitSealHint(entry, jump.DisplayHint);
                    }
                    else
                    {
                        line2 = entry.SoloRecorded
                            ? $"单人最佳 {entry.SoloBest.BestRounds} 回合    配装：{FormatLoadout(entry.SoloBest.Loadout)}"
                            : entry.HasLegacyRecordOnly
                                ? TrialJournalModels.LegacyRecordOnlyHint
                                : BuildFirstUnlitSealHint(entry, "单人破阵：通关后自动记录灵宠与法宝搭配");
                        line3 = entry.CoopRecorded
                            ? $"好友合战：{TrialJournalModels.FormatCoopSummary(entry.CoopBest)}"
                            : BuildFirstUnlitSealHint(entry, "好友合战：2–5 人组队通关留念（不设全服竞速榜）");
                    }
                    TrialJournalUiArt.Label("Record1", rowImg.transform, 104f, 125f, 754f, 25f,
                        line2, 16.5f, TrialJournalUiArt.Ink);
                    TrialJournalUiArt.Label("Record2", rowImg.transform, 104f, 152f, 754f, 25f,
                        line3, 16f, TrialJournalUiArt.Sub);

                    float detailY = 182f;
                    if (showVowBar)
                    {
                        string vowBestInfo = entry.BestVowCount > 0u
                            ? $"个人最高：{entry.BestVowCount} 誓 {entry.BestVowRounds} 回合（{TrialJournalModels.FormatVowSummary(entry.BestVowMask)}）"
                            : "勾选自选试炼誓约后发起挑战（零战力加成，仅作自限荣誉）";
                        TrialJournalUiArt.Label("VowHeading", rowImg.transform, 104f, detailY, 754f, 22f,
                            "试炼誓约  ·  " + vowBestInfo, 15.5f, TrialJournalUiArt.Jade, bold: false);

                        for (int v = 0; v < TrialJournalModels.AllVowBits.Length; v++)
                        {
                            uint bit = TrialJournalModels.AllVowBits[v];
                            bool on = (_selectedVowMask & bit) != 0u;
                            float chipX = 104f + v * 188f;
                            var chipImg = TrialJournalUiArt.SlicedOrTint($"VowChip_{entry.EntryId}_{v}", rowImg.transform,
                                chipX, detailY + 28f, 178f, 44f,
                                TrialJournalUiArt.LoadV3Skin(on ? "vow_chip_active" : "vow_chip_idle",
                                    on ? "rail_tab_selected" : "entry_row_idle"),
                                on ? TrialJournalUiArt.Jade : TrialJournalUiArt.CardBg, raycast: true);
                            var chipBtn = chipImg.gameObject.AddComponent<Button>();
                            chipBtn.targetGraphic = chipImg;
                            uint capturedBit = bit;
                            chipBtn.onClick.AddListener(() =>
                            {
                                _selectedVowMask ^= capturedBit;
                                ViewChanged?.Invoke();
                            });
                            var vowIcon = TrialJournalUiArt.LoadV3Skin(VowIconKey(bit), null);
                            if (vowIcon != null)
                            {
                                var iconImg = TrialJournalUiArt.FixedArt("VowIcon", chipImg.transform,
                                    12f, 10f, 24f, 24f, vowIcon, Color.white, preserveAspect: true);
                                iconImg.raycastTarget = false;
                            }
                            float labelX = vowIcon != null ? 40f : 8f;
                            float labelW = vowIcon != null ? 130f : 162f;
                            TrialJournalUiArt.Label("Label", chipImg.transform, labelX, 0f, labelW, 44f,
                                (on ? "✓ " : "") + TrialJournalModels.VowShortLabel(bit), 16.5f,
                                on ? TrialJournalUiArt.ButtonCream : TrialJournalUiArt.Jade,
                                bold: on, wrap: false, alignment: TextAlignmentOptions.Center);
                        }
                        detailY += 82f;
                    }

                    if (showReplayShareBar)
                    {
                        ulong capturedBattleId = replayBattleId;
                        var replayBtn = TrialJournalUiArt.SpriteButton($"Replay_{entry.EntryId}", rowImg.transform,
                            104f, detailY + 2f, 156f, 44f, "观看录像", 17f,
                            primary: false, interactable: view.OnlineWriteEnabled,
                            onClick: () => WatchReplayRequested?.Invoke(capturedBattleId));
                        var replaySkin = TrialJournalUiArt.LoadV3Skin("replay_btn", null);
                        if (replaySkin != null && replayBtn.targetGraphic is Image rImg) rImg.sprite = replaySkin;

                        var shareBtn = TrialJournalUiArt.SpriteButton($"Share_{entry.EntryId}", rowImg.transform,
                            274f, detailY + 2f, 176f, 44f, "发帖约战分享", 17f,
                            primary: true, interactable: view.OnlineWriteEnabled,
                            onClick: () => ShareChallengeRequested?.Invoke(capturedEntry, capturedBattleId));
                        var shareSkin = TrialJournalUiArt.LoadV3Skin("challenge_share_btn", null);
                        if (shareSkin != null && shareBtn.targetGraphic is Image sImg) sImg.sprite = shareSkin;

                        if (_activeChallengeCard != null && _activeChallengeCard.JournalEntryId == entry.EntryId)
                        {
                            TrialJournalUiArt.SpriteButton($"CancelChallenge_{entry.EntryId}", rowImg.transform,
                                464f, detailY + 2f, 136f, 44f, "取消应战", 16.5f,
                                primary: false, interactable: true,
                                onClick: ClearChallengeTarget);
                        }
                    }
                }

                y += rowH + 8f;
            }
            return y;
        }

        private static string VowIconKey(uint vowBit) => vowBit switch
        {
            TrialJournalModels.VowBanControl => "vow_icon_ban_control",
            TrialJournalModels.VowSingleArtifact => "vow_icon_single_artifact",
            TrialJournalModels.VowNoPet => "vow_icon_no_pet",
            TrialJournalModels.VowTightRounds => "vow_icon_tight_rounds",
            _ => null,
        };

        private static ulong ResolveEntryReplayBattleId(global::TrialJournalEntryView entry)
        {
            if (entry == null) return 0ul;
            if (entry.BestVowBattleId != 0ul) return entry.BestVowBattleId;
            if (entry.SoloRecorded && entry.SoloBest != null && entry.SoloBest.BattleId != 0ul) return entry.SoloBest.BattleId;
            if (entry.CoopRecorded && entry.CoopBest != null && entry.CoopBest.BattleId != 0ul) return entry.CoopBest.BattleId;
            return 0ul;
        }

        private static string BuildFirstUnlitSealHint(global::TrialJournalEntryView entry, string fallback)
        {
            if (entry?.Seals != null)
            {
                for (int i = 0; i < entry.Seals.Count; i++)
                {
                    var s = entry.Seals[i];
                    if (s != null && !s.Lit && !string.IsNullOrEmpty(s.Desc))
                        return $"待亮条件[{TrialJournalModels.SealSlotLabel(s.SealSlotKind)}·{s.Title}]：{s.Desc}";
                }
            }
            return fallback;
        }

        private float RenderMilestoneRows(
            RectTransform parent,
            global::TrialJournalSnapshot snapshot,
            TrialJournalUiView view)
        {
            if (snapshot?.Milestones == null || snapshot.Milestones.Count == 0)
            {
                TrialJournalUiArt.Label("NoMilestones", parent, 20f, 110f, 840f, 40f,
                    "暂无可用里程碑记录。", 19f, TrialJournalUiArt.Sub, alignment: TextAlignmentOptions.Center);
                return 310f;
            }

            float y = 4f;
            for (int i = 0; i < snapshot.Milestones.Count; i++)
            {
                var m = snapshot.Milestones[i];
                if (m == null) continue;
                bool highlight = m.Claimable;
                var dedicatedCard = TrialJournalUiArt.LoadDedicatedOnly(highlight ? "entry_row_selected" : "entry_row_idle");
                var card = TrialJournalUiArt.SlicedOrTint($"Milestone_{m.MilestoneId}", parent,
                    0f, y, 882f, 96f, dedicatedCard ?? TrialJournalUiArt.LoadChronicle("member_row"),
                    highlight ? TrialJournalUiArt.SelectedBg : TrialJournalUiArt.CardBg);
                if (highlight && dedicatedCard == null)
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
                    TrialJournalModels.ChapterWildRealms => "卷四",
                    _ => "试炼",
                };
                var badgeSprite = !string.IsNullOrEmpty(m.BadgeKey)
                    ? (TrialJournalUiArt.LoadV3Skin(m.BadgeKey, null) ?? SeasonEconomyUiArt.LoadTrialJournalBadge(m.BadgeKey))
                    : null;
                float textX = badgeSprite != null ? 92f : 22f;
                if (badgeSprite != null)
                {
                    TrialJournalUiArt.FixedArt("BadgeIcon", card.transform, 20f, 20f, 56f, 56f,
                        badgeSprite, Color.clear, preserveAspect: true);
                }

                bool hasCommentary = m.Claimed && !string.IsNullOrWhiteSpace(m.CommentaryTitle);
                float textW = (hasCommentary ? 550f : 690f) - textX;
                TrialJournalUiArt.Label("Title", card.transform, textX, 14f, textW, 30f,
                    $"[{scopeTag}] {m.Title}  （{m.CurrentSeals}/{m.RequiredSeals} 印）", 21f,
                    TrialJournalUiArt.Jade, bold: true);

                var rewardParts = new List<string>();
                if (m.BoundSandCount > 0) rewardParts.Add($"自用重铸砂 ×{m.BoundSandCount}");
                if (m.BoundArtifactVoucherCount > 0) rewardParts.Add($"法宝拓印 ×{m.BoundArtifactVoucherCount}");
                if (!string.IsNullOrEmpty(m.BadgeName)) rewardParts.Add($"印戳「{m.BadgeName}」");
                if (!string.IsNullOrEmpty(m.GrantAppearanceName)) rewardParts.Add($"专属形象「{m.GrantAppearanceName}」");
                if (!string.IsNullOrEmpty(m.GrantPetCosmeticName)) rewardParts.Add($"灵宠幻化「{m.GrantPetCosmeticName}」");
                if (!string.IsNullOrEmpty(m.GrantSkillVfxName)) rewardParts.Add($"技能特效「{m.GrantSkillVfxName}」");
                if (!string.IsNullOrEmpty(m.CommentaryTitle)) rewardParts.Add($"《{m.CommentaryTitle}》");
                TrialJournalUiArt.Label("Rewards", card.transform, textX, 48f, textW, 28f,
                    "奖励：" + string.Join(" · ", rewardParts), 16.5f, TrialJournalUiArt.Sub);

                // 触控按钮统一 >= 44px 高（读评语 127×46，收取/已领取 155×48）
                if (hasCommentary)
                {
                    var commentary = new TrialJournalCommentary(m.MilestoneId, m.CommentaryKey, m.CommentaryTitle, m.CommentaryBody);
                    TrialJournalUiArt.SpriteButton($"Read_{m.MilestoneId}", card.transform,
                        563f, 25f, 127f, 46f, "读评语", 17f, primary: false, interactable: true,
                        onClick: () => OpenCommentary(commentary));
                }

                string btnText = m.Claimed ? "已领取" : m.Claimable ? "收取" : "未达标";
                bool btnEnabled = view.OnlineWriteEnabled && m.Claimable;
                uint capturedId = m.MilestoneId;
                TrialJournalUiArt.SpriteButton($"Claim_{m.MilestoneId}", card.transform,
                    703f, 24f, 155f, 48f, btnText, 19f,
                    primary: m.Claimable, interactable: btnEnabled,
                    onClick: () => ClaimMilestoneRequested?.Invoke(capturedId));

                y += 104f;
            }
            return y;
        }

        private void RenderArtifactVoucherStrip(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            var box = TrialJournalUiArt.SlicedOrBordered("ArtifactSection", _centerPane,
                14f, 531f, 886f, 134f, "artifact_tray",
                new Color(1f, 0.98f, 0.93f, 0.65f), TrialJournalUiArt.Gold, 1.2f);

            TrialJournalUiArt.Label("ArtHeading", box.transform, 22f, 8f, 210f, 32f,
                "法宝凭券 · 八选一", 21f, TrialJournalUiArt.Jade, bold: true);
            TrialJournalUiArt.Label("VoucherHint", box.transform, 236f, 10f, 412f, 28f,
                view.VoucherHintText, 16f,
                view.Phase == TrialJournalVisualPhase.BagFullError ? TrialJournalUiArt.ErrorInk : TrialJournalUiArt.Sub);

            // 兑换按钮高度严格 44px（触控下限）
            TrialJournalUiArt.SpriteButton("RedeemButton", box.transform,
                657f, 3f, 205f, 44f, view.RedeemButtonText, 17f,
                primary: true, interactable: view.RedeemButtonEnabled && _selectedArtifactItemId != 0u,
                onClick: RequestRedeemConfirmation);

            bool loading = view.Phase == TrialJournalVisualPhase.FirstLoad;
            var list = snapshot?.RedeemableArtifacts;
            int count = list != null && list.Count > 0 ? Math.Min(list.Count, 8) : 8;
            bool tilesDisabled = !view.OnlineWriteEnabled || snapshot == null || snapshot.VoucherBalance == 0;
            for (int i = 0; i < count; i++)
            {
                float tx = 20f + i * 106f;
                var opt = list != null && i < list.Count ? list[i] : null;
                uint itemId = opt?.ItemConfigId ?? (uint)(3301 + i);
                uint effectId = opt?.ArtifactEffectId ?? (uint)(i + 1);
                string shortName = opt != null && !string.IsNullOrEmpty(opt.Name) ? opt.Name : $"法宝{i + 1}";
                bool selected = !loading && itemId == _selectedArtifactItemId && !tilesDisabled;

                var tile = TrialJournalUiArt.SlicedOrBordered($"ArtifactTile_{itemId}", box.transform,
                    tx, 46f, 96f, 82f,
                    selected ? "artifact_slot_selected" : "artifact_slot_idle",
                    selected ? TrialJournalUiArt.SelectedBg : TrialJournalUiArt.Paper,
                    selected ? TrialJournalUiArt.SelectedLine : TrialJournalUiArt.Gold,
                    selected ? 2f : 1f, raycast: true);
                if (loading)
                {
                    QdaoUguiFactory.CreateImage("SkIcon", tile.transform, 22f, 12f, 52f, 44f, null)
                        .color = TrialJournalUiArt.SkeletonBar;
                    continue;
                }

                var btn = tile.gameObject.AddComponent<Button>();
                btn.targetGraphic = tile;
                btn.interactable = opt != null;
                uint capturedItem = itemId;
                btn.onClick.AddListener(() =>
                {
                    _selectedArtifactItemId = capturedItem;
                    ViewChanged?.Invoke();
                });

                var icon = TrialJournalUiArt.FixedArt("Icon", tile.transform,
                    24f, 6f, 48f, 48f, TrialJournalUiArt.LoadArtifactIcon(effectId),
                    Color.clear, preserveAspect: true);
                if (tilesDisabled) icon.color = new Color(1f, 1f, 1f, 0.72f);

                TrialJournalUiArt.Label("Name", tile.transform, 4f, 58f, 88f, 20f,
                    shortName, 14f, TrialJournalUiArt.Jade, bold: selected,
                    alignment: TextAlignmentOptions.Center);

                if (opt != null && opt.OwnedCount > 0)
                {
                    TrialJournalUiArt.Label("OwnedCount", tile.transform, 52f, 2f, 36f, 18f,
                        opt.OwnedCount.ToString(), 13f, TrialJournalUiArt.Jade, bold: true,
                        alignment: TextAlignmentOptions.MidlineRight);
                }
            }
        }

        private void RenderRightGrandmasterPane(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            TrialJournalUiArt.ClearChildren(_rightPane);
            TrialJournalUiArt.SlicedOrBordered("RightPanelBg", _rightPane,
                0f, 0f, 558f, 681f, "modal_panel",
                TrialJournalUiArt.Paper, TrialJournalUiArt.GoldSoft, 1.2f);

            // 顶部 section_plate 标题横幅（深翠底 + 浅米金字，对齐验收图 1262,253,540,56）
            var banner = TrialJournalUiArt.SlicedOrTint("MasterBanner", _rightPane,
                9f, 14f, 540f, 56f, TrialJournalUiArt.LoadSkin("section_plate", "section_plate"),
                TrialJournalUiArt.Jade);
            TrialJournalUiArt.Label("MasterTitle", banner.transform, 44f, 0f, 240f, 56f,
                "星阵宗师", 28f, TrialJournalUiArt.ButtonCream, bold: true,
                alignment: TextAlignmentOptions.MidlineLeft);
            TrialJournalUiArt.Label("MasterTag", banner.transform, 310f, 0f, 184f, 56f,
                "M901", 21f, TrialJournalUiArt.ButtonCream, bold: false,
                alignment: TextAlignmentOptions.MidlineRight);

            // M901 角色立绘先绘、底座后叠（底座上方完全透明，前缘自然遮住脚底边缘）
            bool loading = view.Phase == TrialJournalVisualPhase.FirstLoad;
            var gmSprite = loading ? null : AcquireGrandmasterPortrait();
            if (gmSprite != null)
            {
                var gmImg = TrialJournalUiArt.FixedArt("GrandmasterPortrait", _rightPane,
                    154f, 88f, 246f, 224f, gmSprite, Color.clear, preserveAspect: true);
                if (!view.GrandmasterReached)
                    gmImg.color = new Color(0.95f, 0.94f, 0.90f, 0.96f);
            }
            TrialJournalUiArt.FixedArt("Pedestal", _rightPane,
                142f, 86f, 270f, 250f, TrialJournalUiArt.LoadSkin("grandmaster_pedestal", "portrait_pedestal"),
                Color.clear, preserveAspect: true);

            // 门槛说明 + 分割线 + 宗师道印 2×9 网格
            uint totalPerm = snapshot != null && snapshot.TotalPermanentSeals > 0u ? snapshot.TotalPermanentSeals : 27u;
            uint tol = totalPerm > view.GrandmasterRequiredSeals ? totalPerm - view.GrandmasterRequiredSeals : 0u;
            string unlockSummary = loading
                ? "首次快照载入中…"
                : tol > 0u
                    ? $"永久 {view.GrandmasterRequiredSeals} 印解锁 · 余 {tol} 印容错"
                    : $"永久 {view.GrandmasterRequiredSeals} 印解锁";
            TrialJournalUiArt.Label("MasterHeadline", _rightPane, 24f, 354f, 510f, 32f,
                unlockSummary, 21f, TrialJournalUiArt.Jade, bold: false,
                alignment: TextAlignmentOptions.Center);
            QdaoUguiFactory.CreateImage("MasterRule", _rightPane, 37f, 391f, 484f, 1.5f, null)
                .color = TrialJournalUiArt.Gold;

            TrialJournalUiArt.Label("SealsHead", _rightPane, 35f, 397f, 200f, 28f,
                "宗师道印", 21f, TrialJournalUiArt.Jade, bold: false);

            uint litSeals = Math.Min(view.PermanentLitSeals, 18u);
            for (int i = 0; i < 18; i++)
            {
                int col = i % 9;
                int row = i / 9;
                float sx = 37f + col * 56f;
                float sy = 427f + row * 57f;
                if (loading)
                {
                    var sk = QdaoUguiFactory.CreateImage($"SealSk_{i + 1}", _rightPane, sx, sy, 44f, 44f,
                        TrialJournalUiArt.CircleSprite());
                    sk.color = TrialJournalUiArt.SkeletonBar;
                }
                else
                {
                    TrialJournalUiArt.SealMedallion($"Seal_{i + 1}", _rightPane, sx, sy, 44f,
                        lit: i < litSeals, (i + 1).ToString(), 15f);
                }
            }

            // 底部双按钮（左主按钮 282×56，右次按钮 212×56，均 >= 44px 触控下限）
            var gmMilestone = TrialJournalModels.FindGrandmasterMilestone(snapshot);
            bool hasGmCommentary = gmMilestone != null && gmMilestone.Claimed && !string.IsNullOrWhiteSpace(gmMilestone.CommentaryTitle);

            string leftBtnText = view.GrandmasterClaimable ? "领取宗师里程碑"
                : view.GrandmasterClaimed ? "已领宗师里程碑"
                : "查看全部里程碑";
            bool leftBtnEnabled = !loading && (view.GrandmasterClaimable
                ? view.OnlineWriteEnabled
                : !view.GrandmasterClaimed && view.Phase != TrialJournalVisualPhase.Disconnected);
            TrialJournalUiArt.SpriteButton("MasterPrimaryBtn", _rightPane,
                31f, 555f, 282f, 56f, leftBtnText, 20f,
                primary: true, interactable: leftBtnEnabled,
                onClick: () => HandleGrandmasterPrimary(gmMilestone, view));

            string rightBtnText = hasGmCommentary ? "阅读宗师跋文" : "前往衣柜";
            TrialJournalUiArt.SpriteButton("MasterSecondaryBtn", _rightPane,
                329f, 555f, 212f, 56f, rightBtnText, 19f,
                primary: false, interactable: !loading,
                onClick: () =>
                {
                    if (hasGmCommentary)
                    {
                        OpenCommentary(new TrialJournalCommentary(gmMilestone.MilestoneId, gmMilestone.CommentaryKey,
                            gmMilestone.CommentaryTitle, gmMilestone.CommentaryBody));
                    }
                    else
                    {
                        OpenAppearanceRequested?.Invoke();
                    }
                });

            string footnote = loading ? "正在等待服务端权威快照"
                : view.GrandmasterClaimed ? "已入藏易容衣柜，可随时免费切换"
                : view.GrandmasterClaimable ? "已达成：领取后前往衣柜查看衣饰"
                : "星阵衣饰与跋文将在达成后开启";
            TrialJournalUiArt.Label("MasterFoot", _rightPane, 24f, 622f, 510f, 28f,
                footnote, 17f, TrialJournalUiArt.Sub, alignment: TextAlignmentOptions.Center);
        }

        private void RenderFooter(global::TrialJournalSnapshot snapshot, TrialJournalUiView view)
        {
            TrialJournalUiArt.ClearChildren(_footerArea);
            TrialJournalUiArt.Label("FootLeft", _footerArea, 0f, 0f, 1220f, 30f,
                view.FooterText, 16f, TrialJournalUiArt.Sub);
            string rightTag = _demoBadgeVisible
                ? "试炼志 · UI 演示数据"
                : snapshot != null && snapshot.Available
                    ? $"快照修订 R{snapshot.StateRevision}"
                    : "五行试炼志";
            TrialJournalUiArt.Label("FootRight", _footerArea, 1240f, 0f, 435f, 30f,
                rightTag, 16f, TrialJournalUiArt.Sub, alignment: TextAlignmentOptions.MidlineRight);
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
            dim.color = new Color(0.10f, 0.14f, 0.11f, 0.52f);
            dim.raycastTarget = true;

            if (_pendingRedeemItemId != 0u)
            {
                var card = TrialJournalUiArt.SlicedOrTint("RedeemConfirmCard", _modalLayer,
                    580f, 338f, 760f, 380f, TrialJournalUiArt.LoadSkin("modal_panel", "application_card"),
                    TrialJournalUiArt.Paper, raycast: true);
                TrialJournalUiArt.Label("Title", card.transform, 40f, 32f, 680f, 44f,
                    "兑换自用法宝", 30f, TrialJournalUiArt.Jade, bold: true,
                    alignment: TextAlignmentOptions.Center);
                QdaoUguiFactory.CreateImage("Divider", card.transform, 65f, 90f, 630f, 1.5f, null)
                    .color = TrialJournalUiArt.Gold;

                TrialJournalUiArt.FixedArt("ArtifactPreview", card.transform,
                    105f, 118f, 90f, 90f, TrialJournalUiArt.LoadArtifactIcon(_pendingRedeemEffectId),
                    Color.clear, preserveAspect: true);
                TrialJournalUiArt.Label("SelectedName", card.transform, 224f, 126f, 460f, 34f,
                    $"所选法宝：{_pendingRedeemName}", 23f, TrialJournalUiArt.Jade, bold: true);
                TrialJournalUiArt.Label("SelectedSub", card.transform, 224f, 168f, 460f, 32f,
                    "消耗 1 张自用法宝拓印；允许兑换同名第二件。", 19f, TrialJournalUiArt.Sub);
                TrialJournalUiArt.Label("BoundNotice", card.transform, 105f, 232f, 550f, 32f,
                    "仅供自用，不可交易、不可上架、不可拆解。", 20f, TrialJournalUiArt.ErrorInk, bold: false);

                TrialJournalUiArt.SpriteButton("Cancel", card.transform,
                    105f, 294f, 220f, 56f, "取消", 21f, primary: false, interactable: true,
                    onClick: () =>
                    {
                        _pendingRedeemItemId = 0u;
                        _pendingRedeemEffectId = 0u;
                        _pendingRedeemName = "";
                        HideModals();
                    });
                uint itemId = _pendingRedeemItemId;
                TrialJournalUiArt.SpriteButton("Confirm", card.transform,
                    416f, 294f, 240f, 56f, "确认兑换", 21f, primary: true, interactable: true,
                    onClick: () =>
                    {
                        _pendingRedeemItemId = 0u;
                        _pendingRedeemEffectId = 0u;
                        _pendingRedeemName = "";
                        HideModals();
                        RedeemVoucherRequested?.Invoke(itemId);
                    });
                return;
            }

            if (_readingCommentary != null)
            {
                var card = TrialJournalUiArt.SlicedOrTint("CommentaryCard", _modalLayer,
                    440f, 201f, 1040f, 640f, TrialJournalUiArt.LoadSkin("modal_panel", "application_card"),
                    TrialJournalUiArt.Paper, raycast: true);
                TrialJournalUiArt.Label("Header", card.transform, 48f, 28f, 944f, 42f,
                    "留灯院评语", 30f, TrialJournalUiArt.Jade, bold: true,
                    alignment: TextAlignmentOptions.Center);
                TrialJournalUiArt.Label("Origin", card.transform, 62f, 82f, 520f, 28f,
                    "出处：留灯院 · 试炼评语", 18f, TrialJournalUiArt.Sub);
                QdaoUguiFactory.CreateImage("Divider", card.transform, 52f, 122f, 936f, 1.5f, null)
                    .color = TrialJournalUiArt.Gold;

                // 左侧已解锁评语目录切换（对齐 14-review-reading-modal.png）
                var unlocked = TrialJournalModels.CollectUnlockedCommentaries(_lastValidSnapshot);
                bool hasSidebar = unlocked.Count > 1;
                if (hasSidebar)
                {
                    for (int i = 0; i < unlocked.Count && i < 4; i++)
                    {
                        var item = unlocked[i];
                        bool isCurrent = item.MilestoneId == _readingCommentary.MilestoneId
                            || string.Equals(item.Title, _readingCommentary.Title, StringComparison.Ordinal);
                        var tabImg = TrialJournalUiArt.SlicedOrBordered($"ComTab_{i}", card.transform,
                            48f, 152f + i * 76f, 270f, 62f,
                            isCurrent ? "rail_tab_selected" : "rail_tab_idle",
                            isCurrent ? TrialJournalUiArt.Jade : TrialJournalUiArt.CardBg,
                            TrialJournalUiArt.Gold, 1.2f, raycast: true);
                        var tabBtn = tabImg.gameObject.AddComponent<Button>();
                        tabBtn.targetGraphic = tabImg;
                        var captured = item;
                        tabBtn.onClick.AddListener(() =>
                        {
                            _readingCommentary = captured;
                            RenderModal();
                        });
                        TrialJournalUiArt.Label("Label", tabImg.transform, 14f, 0f, 242f, 62f,
                            item.Title, 19f,
                            isCurrent ? TrialJournalUiArt.ButtonCream : TrialJournalUiArt.Jade,
                            bold: isCurrent, alignment: TextAlignmentOptions.Center);
                    }
                }

                float bodyX = hasSidebar ? 356f : 72f;
                float bodyW = hasSidebar ? 624f : 896f;
                TrialJournalUiArt.Label("Title", card.transform, bodyX, 148f, bodyW, 42f,
                    $"《{_readingCommentary.Title}》", 26f, TrialJournalUiArt.Jade, bold: true,
                    alignment: TextAlignmentOptions.Center);
                TrialJournalUiArt.Label("Body", card.transform, bodyX, 206f, bodyW, 316f,
                    _readingCommentary.Body, 21f, TrialJournalUiArt.Ink, bold: false, wrap: true,
                    alignment: TextAlignmentOptions.TopLeft);
                TrialJournalUiArt.SpriteButton("CloseCommentary", card.transform,
                    hasSidebar ? 695f : 402f, 537f, 236f, 56f, "收起卷册", 21f, primary: false, interactable: true,
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
            uint effectId = 0u;
            for (int i = 0; i < _lastValidSnapshot.RedeemableArtifacts.Count; i++)
            {
                var opt = _lastValidSnapshot.RedeemableArtifacts[i];
                if (opt != null && opt.ItemConfigId == _selectedArtifactItemId)
                {
                    name = opt.Name;
                    effectId = opt.ArtifactEffectId;
                    break;
                }
            }
            _readingCommentary = null;
            _pendingRedeemItemId = _selectedArtifactItemId;
            _pendingRedeemEffectId = effectId;
            _pendingRedeemName = name;
            RenderModal();
        }

        private void HandleGrandmasterPrimary(global::TrialJournalMilestoneView gm, TrialJournalUiView view)
        {
            if (gm != null && gm.Claimable && view.OnlineWriteEnabled)
            {
                ClaimMilestoneRequested?.Invoke(gm.MilestoneId);
                return;
            }
            _activeTab = TrialJournalNavTab.Milestones;
            ViewChanged?.Invoke();
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
