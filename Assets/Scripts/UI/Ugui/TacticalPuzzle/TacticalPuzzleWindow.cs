using System;
using System.Collections.Generic;
using MmorpgClient.Game.TacticalPuzzle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.TacticalPuzzle.TacticalPuzzleUiArt;

namespace MmorpgClient.UI.Ugui.TacticalPuzzle
{
    /// <summary>
    /// 万象残局坊 V1 主界面与出题编辑器（纯代码 UGUI，1920×1080 基准，完整复用 10 张 V2 手绘无字切片）。
    /// 只读消费 <see cref="TacticalPuzzleClient"/> 快照并通过事件回调把写意图交给 <see cref="TacticalPuzzleUiRoot"/>。
    /// </summary>
    public sealed class TacticalPuzzleWindow : IDisposable
    {
        private const float WindowW = 1760f;
        private const float WindowH = 960f;
        private const float CardW = 836f;
        private const float CardH = 192f;
        private const float CardGapX = 18f;
        private const float CardGapY = 14f;

        private static readonly Match.TacticalPuzzleListTab[] Tabs =
        {
            Match.TacticalPuzzleListTab.Hot,
            Match.TacticalPuzzleListTab.Newest,
            Match.TacticalPuzzleListTab.Hardest,
            Match.TacticalPuzzleListTab.MyCreated,
            Match.TacticalPuzzleListTab.MyCleared,
        };

        public event Action CloseRequested;
        public event Action<Match.TacticalPuzzleListTab, uint> FilterChanged;
        public event Action RefreshRequested;
        public event Action<ulong, bool> StartBattleRequested;
        public event Action<Match.TacticalPuzzleSummaryView> WatchSolutionRequested;
        public event Action<ulong> LikeRequested;
        public event Action<Match.TacticalPuzzleSummaryView> ShareRequested;
        public event Action<ulong> DeleteRequested;
        public event Action<ulong, string, string, uint, global::TacticalPuzzleSpec, bool> SaveDraftRequested;

        private readonly RectTransform _windowRoot;
        private readonly RectTransform _headerHonorRoot;
        private readonly TMP_Text _honorText;
        private readonly Button _createButton;
        private readonly Button _refreshButton;
        private readonly Button _closeButton;
        private readonly RectTransform _tabsRow;
        private readonly RectTransform _tagsRow;
        private readonly RectTransform _listContent;
        private readonly ScrollRect _listScroll;
        private readonly TMP_Text _footerStatus;

        // 编辑器二级模态
        private readonly RectTransform _editorOverlay;
        private readonly RectTransform _editorBody;
        private readonly TMP_Text _editorTitleLabel;
        private TMP_InputField _titleInput;
        private TMP_InputField _hintInput;
        private readonly RectTransform _editorDynamicRoot;

        // 删除确认轻弹窗
        private readonly RectTransform _deleteOverlay;
        private readonly TMP_Text _deleteMessage;
        private ulong _pendingDeletePuzzleId;

        private Match.TacticalPuzzleListTab _selectedTab = Match.TacticalPuzzleListTab.Hot;
        private uint _selectedTagFilter;
        private ulong _highlightPuzzleId;
        private bool _compactCloseOnLeft;

        // 编辑器草稿状态
        private bool _editorOpen;
        private ulong _editingPuzzleId;
        private string _draftTitle = "";
        private string _draftHint = "";
        private uint _draftTagMask;
        private global::TacticalPuzzleSpec _draftSpec = TacticalPuzzleModels.CreateDefaultSpec();
        private int _selectedEnemyIndex;

        public bool IsEditorOpen => _editorOpen && _editorOverlay != null && _editorOverlay.gameObject.activeSelf;

        public TacticalPuzzleWindow(UnityEngine.Transform parent)
        {
            var frame = SlicedOrTint("PuzzleWorkshopFrame", parent,
                (DesignWidth - WindowW) * 0.5f, (DesignHeight - WindowH) * 0.5f,
                WindowW, WindowH, LoadWindowSkin("window_frame", "panel_main"), Paper, raycast: true);
            _windowRoot = (RectTransform)frame.transform;

            // ── 顶部标题与荣誉条 ──
            FixedIcon("EntryGlyph", _windowRoot, 32f, 22f, 58f, 58f, LoadSlice("hud_entry_puzzle"));
            Label("Title", _windowRoot, 102f, 18f, 360f, 38f, "万象残局坊", 32f, Jade, bold: true);
            Label("Subtitle", _windowRoot, 102f, 56f, 480f, 26f,
                "公平模板 · 作者自证可解 · 零日常打卡零战力通胀", 17f, Sub);

            _headerHonorRoot = QdaoUguiFactory.CreateRect("HonorStrip", _windowRoot, 590f, 18f, 590f, 68f);
            var honorBg = _headerHonorRoot.gameObject.AddComponent<Image>();
            honorBg.color = new Color(0.95f, 0.91f, 0.82f, 0.85f);
            honorBg.raycastTarget = false;
            FixedIcon("HonorBadge", _headerHonorRoot, 10f, 8f, 52f, 52f, LoadSlice("honor_puzzle_master"));
            _honorText = Label("HonorText", _headerHonorRoot, 72f, 6f, 506f, 56f,
                "残局初窥 · 破局 0 · 发布 0 · 获赞 0", 18f, GoldInk, bold: true, wrap: true);

            _createButton = ActionButton("CreatePuzzleBtn", _windowRoot, 1200f, 24f, 176f, 52f,
                "创作残局", 21f, primary: true, interactable: true, () => OpenEditor(null));
            _refreshButton = ActionButton("RefreshBtn", _windowRoot, 1388f, 24f, 118f, 52f,
                "刷新", 20f, primary: false, interactable: true, () => RefreshRequested?.Invoke());
            _closeButton = ActionButton("CloseBtn", _windowRoot, WindowW - 144f, 24f, 112f, 52f,
                "关闭", 20f, primary: false, interactable: true, () => CloseRequested?.Invoke());

            // ── 页签与标签筛选行 ──
            _tabsRow = QdaoUguiFactory.CreateRect("TabsRow", _windowRoot, 32f, 96f, WindowW - 64f, 50f);
            _tagsRow = QdaoUguiFactory.CreateRect("TagsRow", _windowRoot, 32f, 154f, WindowW - 64f, 44f);

            // ── 双列残局列表 ScrollRect ──
            var viewport = QdaoUguiFactory.CreateRect("PuzzleListViewport", _windowRoot, 32f, 208f, WindowW - 64f, 682f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0.001f);
            vpImg.raycastTarget = true;

            _listContent = QdaoUguiFactory.CreateRect("PuzzleListContent", viewport, 0f, 0f, WindowW - 64f, 682f);
            _listScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _listScroll.viewport = viewport;
            _listScroll.content = _listContent;
            _listScroll.horizontal = false;
            _listScroll.vertical = true;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            _listScroll.scrollSensitivity = 48f;

            // ── 底栏状态提示 ──
            _footerStatus = Label("FooterStatus", _windowRoot, 36f, 902f, WindowW - 72f, 40f,
                "提示：每道残局皆为公平固定模板，免费随玩随停，破局后可点赞并分享给仙友。", 18f, Sub);

            // ── 出题编辑器模态 ──
            _editorOverlay = QdaoUguiFactory.CreateStretch("PuzzleEditorOverlay", parent, Vector4.zero);
            var edShade = _editorOverlay.gameObject.AddComponent<Image>();
            edShade.color = new Color(0.02f, 0.06f, 0.05f, 0.72f);
            edShade.raycastTarget = true;

            var edFrame = SlicedOrTint("EditorFrame", _editorOverlay,
                (DesignWidth - 1720f) * 0.5f, (DesignHeight - 920f) * 0.5f,
                1720f, 920f, LoadWindowSkin("window_frame", "panel_main"), Paper, raycast: true);
            _editorBody = (RectTransform)edFrame.transform;

            _editorTitleLabel = Label("EditorHeader", _editorBody, 32f, 18f, 900f, 40f,
                "万象残局坊 · 摆盘出题", 28f, Jade, bold: true);
            ActionButton("EditorCloseBtn", _editorBody, 1580f, 16f, 108f, 48f,
                "返回", 20f, primary: false, interactable: true, CloseEditor);

            Label("TitleLbl", _editorBody, 34f, 70f, 110f, 44f, "残局题名：", 19f, Ink, bold: true);
            _titleInput = InputBox("TitleInput", _editorBody, 144f, 70f, 520f, 44f,
                "4～20字，例如：三回合破盾反杀", "", TacticalPuzzleModels.MaxTitleRunes);
            _titleInput.onValueChanged.AddListener(v => _draftTitle = v ?? "");

            Label("HintLbl", _editorBody, 690f, 70f, 110f, 44f, "破局提示：", 19f, Ink, bold: true);
            _hintInput = InputBox("HintInput", _editorBody, 800f, 70f, 886f, 44f,
                "选填，最多40字（如：先驱散固守再集火）", "", TacticalPuzzleModels.MaxHintRunes);
            _hintInput.onValueChanged.AddListener(v => _draftHint = v ?? "");

            _editorDynamicRoot = QdaoUguiFactory.CreateRect("EditorDynamic", _editorBody, 0f, 124f, 1720f, 780f);
            _editorOverlay.gameObject.SetActive(false);

            // ── 删除确认弹窗 ──
            _deleteOverlay = QdaoUguiFactory.CreateStretch("PuzzleDeleteOverlay", parent, Vector4.zero);
            var delShade = _deleteOverlay.gameObject.AddComponent<Image>();
            delShade.color = new Color(0.02f, 0.05f, 0.04f, 0.72f);
            delShade.raycastTarget = true;
            var delCard = SlicedOrTint("DeleteCard", _deleteOverlay,
                (DesignWidth - 680f) * 0.5f, (DesignHeight - 280f) * 0.5f,
                680f, 280f, LoadSlice("puzzle_editor_panel_bg"), Paper, raycast: true);
            Label("DelTitle", delCard.transform, 32f, 28f, 616f, 38f, "确认删除残局", 26f, Jade, bold: true, alignment: TextAlignmentOptions.Center);
            _deleteMessage = Label("DelMsg", delCard.transform, 42f, 84f, 596f, 86f,
                "删除后将无法恢复，且相关挑战与点赞记录将一并移除。", 20f, Ink, wrap: true, alignment: TextAlignmentOptions.Center);
            ActionButton("DelCancel", delCard.transform, 148f, 196f, 176f, 52f, "取消", 20f, false, true, CloseDeleteModal);
            ActionButton("DelConfirm", delCard.transform, 356f, 196f, 176f, 52f, "确认删除", 20f, true, true, () =>
            {
                ulong id = _pendingDeletePuzzleId;
                CloseDeleteModal();
                if (id != 0ul) DeleteRequested?.Invoke(id);
            });
            _deleteOverlay.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            CloseEditor();
            CloseDeleteModal();
        }

        public void ResetForSession()
        {
            _selectedTab = Match.TacticalPuzzleListTab.Hot;
            _selectedTagFilter = 0u;
            _highlightPuzzleId = 0ul;
            CloseEditor();
            CloseDeleteModal();
        }

        public void SetCompactCloseOnLeft(bool compactOnLeft)
        {
            if (_compactCloseOnLeft == compactOnLeft || _closeButton == null) return;
            _compactCloseOnLeft = compactOnLeft;
            var closeRect = (RectTransform)_closeButton.transform;
            var createRect = (RectTransform)_createButton.transform;
            var refreshRect = (RectTransform)_refreshButton.transform;
            if (compactOnLeft)
            {
                // 避让小游戏右上 176×72 胶囊区，把关闭/刷新/创作左移留白
                closeRect.anchoredPosition = new Vector2(24f, -24f);
                createRect.anchoredPosition = new Vector2(1180f, -24f);
                refreshRect.anchoredPosition = new Vector2(1368f, -24f);
            }
            else
            {
                closeRect.anchoredPosition = new Vector2(WindowW - 144f, -24f);
                createRect.anchoredPosition = new Vector2(1200f, -24f);
                refreshRect.anchoredPosition = new Vector2(1388f, -24f);
            }
        }

        public void FocusPuzzle(ulong puzzleId)
        {
            _highlightPuzzleId = puzzleId;
        }

        public bool DismissModalIfOpen()
        {
            if (_deleteOverlay != null && _deleteOverlay.gameObject.activeSelf)
            {
                CloseDeleteModal();
                return true;
            }
            if (IsEditorOpen)
            {
                CloseEditor();
                return true;
            }
            return false;
        }

        public void OpenEditor(Match.TacticalPuzzleSummaryView existing)
        {
            if (existing != null && existing.PuzzleId != 0ul)
            {
                _editingPuzzleId = existing.PuzzleId;
                _draftTitle = existing.Title ?? "";
                _draftHint = existing.HintText ?? "";
                _draftTagMask = existing.TagMask & TacticalPuzzleModels.ValidTagMask;
                _draftSpec = existing.Spec != null ? existing.Spec.Clone() : TacticalPuzzleModels.CreateDefaultSpec();
                _editorTitleLabel.text = $"编辑残局 #{existing.PuzzleId}（修改盘面后须重新自证发布）";
            }
            else
            {
                _editingPuzzleId = 0ul;
                _draftTitle = "";
                _draftHint = "";
                _draftTagMask = 1u; // 默认选中「破盾」
                _draftSpec = TacticalPuzzleModels.CreateDefaultSpec();
                _editorTitleLabel.text = "创作新残局（公平模板 · 自证通关即发布）";
            }
            if (_draftSpec.Enemies.Count == 0)
            {
                _draftSpec.Enemies.Add(TacticalPuzzleModels.CreateDefaultSpec().Enemies[0]);
            }
            _selectedEnemyIndex = 0;
            _titleInput.SetTextWithoutNotify(_draftTitle);
            _hintInput.SetTextWithoutNotify(_draftHint);
            _editorOpen = true;
            _editorOverlay.gameObject.SetActive(true);
            _editorOverlay.SetAsLastSibling();
            RebuildEditorBody();
        }

        public void CloseEditor()
        {
            _editorOpen = false;
            if (_editorOverlay != null) _editorOverlay.gameObject.SetActive(false);
        }

        private void OpenDeleteModal(Match.TacticalPuzzleSummaryView puzzle)
        {
            if (puzzle == null || puzzle.PuzzleId == 0ul) return;
            _pendingDeletePuzzleId = puzzle.PuzzleId;
            _deleteMessage.text = $"确定删除残局「{puzzle.Title}」吗？\n删除后无法恢复，历史挑战与点赞统计也将清除。";
            _deleteOverlay.gameObject.SetActive(true);
            _deleteOverlay.SetAsLastSibling();
        }

        private void CloseDeleteModal()
        {
            _pendingDeletePuzzleId = 0ul;
            if (_deleteOverlay != null) _deleteOverlay.gameObject.SetActive(false);
        }

        public void Render(TacticalPuzzleClient client)
        {
            if (client != null)
            {
                _selectedTab = client.CurrentTab == Match.TacticalPuzzleListTab.Unspecified
                    ? Match.TacticalPuzzleListTab.Hot
                    : client.CurrentTab;
                _selectedTagFilter = client.CurrentTagFilterMask;
            }

            // 荣誉条
            var profile = client?.Profile;
            if (profile != null)
            {
                string honor = TacticalPuzzleModels.DeriveHonorTitle(profile.HonorTier, profile.HonorTitle);
                _honorText.text = $"{honor}  ·  已破局 {profile.TotalClearedCount}  ·  已发布 {profile.PublishedCount}/{TacticalPuzzleModels.MaxPuzzlesPerAuthor}  ·  获赞 {profile.TotalLikesReceived}";
            }
            else
            {
                _honorText.text = "残局初窥  ·  通关任意残局即可积累破局荣誉称谓";
            }

            bool ready = client?.Ready ?? false;
            bool actionPending = client?.ActionPending ?? false;
            _createButton.interactable = ready && !actionPending;
            _refreshButton.interactable = ready && !(client?.ListPending ?? false);

            RenderTabs();
            RenderTags();
            RenderList(client);

            // 底栏文案
            if (client != null && !string.IsNullOrEmpty(client.Status))
            {
                _footerStatus.text = client.Status;
                _footerStatus.color = client.StatusIsError ? ErrorInk : Jade;
            }
            else
            {
                uint total = client?.TotalCount ?? 0u;
                _footerStatus.text = $"当前「{TacticalPuzzleModels.TabName(_selectedTab)}」共 {total} 道残局 · 全员同面板公平破局，不消耗道具，不产出可交易货源。";
                _footerStatus.color = Sub;
            }
        }

        private void RenderTabs()
        {
            ClearChildren(_tabsRow);
            float x = 0f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var tab = Tabs[i];
                bool selected = tab == _selectedTab;
                ActionButton("Tab_" + tab, _tabsRow, x, 2f, 184f, 46f,
                    TacticalPuzzleModels.TabName(tab), 20f, primary: selected, interactable: true,
                    () =>
                    {
                        _selectedTab = tab;
                        FilterChanged?.Invoke(_selectedTab, _selectedTagFilter);
                    });
                x += 196f;
            }
        }

        private void RenderTags()
        {
            ClearChildren(_tagsRow);
            float x = 0f;
            ChipButton("TagAll", _tagsRow, x, 2f, 100f, 40f, "全部标签", _selectedTagFilter == 0u, () =>
            {
                _selectedTagFilter = 0u;
                FilterChanged?.Invoke(_selectedTab, _selectedTagFilter);
            }, 17f);
            x += 110f;

            for (int i = 0; i < TacticalPuzzleModels.TagNames.Length; i++)
            {
                uint bit = 1u << i;
                bool active = (_selectedTagFilter & bit) != 0u;
                string name = TacticalPuzzleModels.TagNames[i];
                float w = name.Length >= 4 ? 124f : 96f;
                ChipButton("Tag_" + i, _tagsRow, x, 2f, w, 40f, name, active, () =>
                {
                    _selectedTagFilter = active ? 0u : bit;
                    FilterChanged?.Invoke(_selectedTab, _selectedTagFilter);
                }, 17f);
                x += w + 10f;
            }
        }

        private void RenderList(TacticalPuzzleClient client)
        {
            ClearChildren(_listContent);
            if (client == null || !client.Ready)
            {
                Label("NotReady", _listContent, 40f, 180f, WindowW - 144f, 60f,
                    "尚未进入游戏，连接就绪后自动拉取万象残局坊。", 24f, Sub, alignment: TextAlignmentOptions.Center);
                _listContent.sizeDelta = new Vector2(WindowW - 64f, 682f);
                return;
            }

            if (client.ListPending && client.Puzzles.Count == 0)
            {
                Label("Loading", _listContent, 40f, 180f, WindowW - 144f, 60f,
                    "正在拉取全服残局谱…", 24f, Jade, alignment: TextAlignmentOptions.Center);
                _listContent.sizeDelta = new Vector2(WindowW - 64f, 682f);
                return;
            }

            var list = client.Puzzles;
            if (list.Count == 0)
            {
                string emptyHint = _selectedTab == Match.TacticalPuzzleListTab.MyCreated
                    ? "你尚未创作残局，点击右上角「创作残局」即可摆盘出题！"
                    : "当前筛选下暂无残局，换个标签或亲手创作一局吧。";
                Label("Empty", _listContent, 40f, 180f, WindowW - 144f, 60f,
                    emptyHint, 23f, Sub, alignment: TextAlignmentOptions.Center);
                _listContent.sizeDelta = new Vector2(WindowW - 64f, 682f);
                return;
            }

            ulong selfPlayerId = client.PlayerId;
            bool busy = client.ActionPending;
            bool replayBusy = client.ReplayPending;
            Sprite cardBgSprite = LoadSlice("puzzle_card_bg");
            Sprite verifiedSeal = LoadSlice("badge_verified_seal");
            Sprite clearedStamp = LoadSlice("badge_cleared_stamp");
            Sprite draftTag = LoadSlice("badge_draft_tag");
            Sprite likeIcon = LoadSlice("icon_puzzle_like");
            Sprite solutionIcon = LoadSlice("icon_puzzle_solution");

            int rows = (list.Count + 1) / 2;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                int col = i % 2;
                int row = i / 2;
                float cx = col * (CardW + CardGapX);
                float cy = row * (CardH + CardGapY);

                var cardImg = SlicedOrTint($"PuzzleCard_{p.PuzzleId}", _listContent, cx, cy, CardW, CardH,
                    cardBgSprite, Paper, raycast: false);
                var card = (RectTransform)cardImg.transform;
                if (_highlightPuzzleId != 0ul && _highlightPuzzleId == p.PuzzleId)
                {
                    var hl = card.gameObject.AddComponent<Outline>();
                    hl.effectColor = TagSelectedBg;
                    hl.effectDistance = new Vector2(3f, -3f);
                }

                bool isDraft = p.State != Match.TacticalPuzzleState.Published;
                bool isOwner = p.AuthorPlayerId != 0ul && p.AuthorPlayerId == selfPlayerId;

                // 标题与作者行（深墨色确保在浅羊皮纸底图清晰高对比，richText = false）
                Label("Title", card, 26f, 16f, 560f, 32f,
                    string.IsNullOrWhiteSpace(p.Title) ? $"无题残局 #{p.PuzzleId}" : p.Title,
                    22f, Jade, bold: true);

                string authorLabel = string.IsNullOrWhiteSpace(p.AuthorName) ? $"道友#{p.AuthorPlayerId}" : p.AuthorName;
                string specBrief = TacticalPuzzleModels.DescribeSpecBrief(p.Spec);
                Label("Sub", card, 26f, 48f, 610f, 24f,
                    $"出题：{authorLabel}  ｜  {specBrief}", 16f, Sub);

                // 右上印章 / 待验题标签（独立角标，不互压）
                if (isDraft)
                {
                    var draftBg = SlicedOrTint("DraftTag", card, CardW - 156f, 18f, 132f, 34f, draftTag, PaperSoft);
                    Label("DraftText", draftBg.transform, 0f, 0f, 132f, 34f, "待验题", 16f, Ink, bold: true, alignment: TextAlignmentOptions.Center);
                }
                else
                {
                    FixedIcon("VerifiedSeal", card, CardW - 72f, 14f, 48f, 48f, verifiedSeal);
                    if (p.SelfCleared)
                    {
                        FixedIcon("ClearedStamp", card, CardW - 126f, 14f, 48f, 48f, clearedStamp);
                    }
                }

                // 提示语与统计行
                string tagText = TacticalPuzzleModels.FormatTagSummary(p.TagMask);
                string hintLine = string.IsNullOrWhiteSpace(p.HintText)
                    ? $"标签：{tagText}"
                    : $"[{tagText}] 提示：{p.HintText}";
                Label("Hint", card, 26f, 76f, CardW - 52f, 24f, hintLine, 16f, Ink);

                string statsLine = isDraft
                    ? "状态：草稿待作者亲手通关 1 次后自动发布全服"
                    : $"{TacticalPuzzleModels.FormatClearRate(p.AttemptCount, p.ClearCount)}  ·  最快 {(p.GlobalBestRounds > 0u ? p.GlobalBestRounds + "回合" : "--")}  ·  作者 {p.AuthorBestRounds}回合  ·  获赞 {p.LikeCount}"
                      + (p.SelfCleared && p.SelfBestRounds > 0u ? $"  ·  我最快 {p.SelfBestRounds}回合" : "");
                Label("Stats", card, 26f, 102f, CardW - 52f, 24f, statsLine, 15f, GoldInk);

                // 底部动作按钮行（全部 >= 44px 触控高度）
                float btnY = 134f;
                if (isDraft)
                {
                    ActionButton("VerifyBtn", card, 26f, btnY, 174f, 44f, "亲自验题", 18f,
                        primary: true, interactable: !busy, () => StartBattleRequested?.Invoke(p.PuzzleId, true));
                    ActionButton("EditBtn", card, 212f, btnY, 116f, 44f, "编辑", 18f,
                        primary: false, interactable: !busy, () => OpenEditor(p));
                    ActionButton("DeleteBtn", card, 340f, btnY, 116f, 44f, "删除", 18f,
                        primary: false, interactable: !busy, () => OpenDeleteModal(p));
                }
                else
                {
                    ActionButton("ChallengeBtn", card, 26f, btnY, 134f, 44f, "挑战", 18f,
                        primary: true, interactable: !busy, () => StartBattleRequested?.Invoke(p.PuzzleId, false));

                    ActionButton("SolutionBtn", card, 170f, btnY, 144f, 44f, "看解法", 17f,
                        primary: false, interactable: !replayBusy && p.HasSolutionReplay,
                        () => WatchSolutionRequested?.Invoke(p), leadingIcon: solutionIcon);

                    bool canLike = !busy && !isOwner && p.SelfCleared && !p.SelfLiked;
                    string likeLabel = p.SelfLiked ? $"已赞({p.LikeCount})" : $"点赞({p.LikeCount})";
                    ActionButton("LikeBtn", card, 324f, btnY, 144f, 44f, likeLabel, 17f,
                        primary: false, interactable: canLike,
                        () => LikeRequested?.Invoke(p.PuzzleId), leadingIcon: likeIcon);

                    ActionButton("ShareBtn", card, 478f, btnY, 112f, 44f, "分享", 17f,
                        primary: false, interactable: true, () => ShareRequested?.Invoke(p));

                    if (isOwner)
                    {
                        ActionButton("OwnerEditBtn", card, 600f, btnY, 102f, 44f, "改题", 17f,
                            primary: false, interactable: !busy, () => OpenEditor(p));
                        ActionButton("OwnerDelBtn", card, 712f, btnY, 96f, 44f, "删除", 17f,
                            primary: false, interactable: !busy, () => OpenDeleteModal(p));
                    }
                }
            }

            float contentH = Mathf.Max(682f, rows * (CardH + CardGapY) + 12f);
            _listContent.sizeDelta = new Vector2(WindowW - 64f, contentH);
        }

        // ── 编辑器动态内容重建 ──────────────────────────────────

        private void RebuildEditorBody()
        {
            ClearChildren(_editorDynamicRoot);
            if (_draftSpec == null) _draftSpec = TacticalPuzzleModels.CreateDefaultSpec();
            if (_draftSpec.Enemies.Count == 0) _draftSpec.Enemies.Add(TacticalPuzzleModels.CreateDefaultSpec().Enemies[0]);
            _selectedEnemyIndex = Mathf.Clamp(_selectedEnemyIndex, 0, _draftSpec.Enemies.Count - 1);

            // 标签选择（最多 2 个） + 回合上限（1..6）
            int selectedTags = TacticalPuzzleModels.PopCountTags(_draftTagMask);
            Label("EdTagLbl", _editorDynamicRoot, 34f, 4f, 150f, 40f, $"标签（{selectedTags}/2）：", 18f, Ink, bold: true);
            float tx = 184f;
            for (int i = 0; i < TacticalPuzzleModels.TagNames.Length; i++)
            {
                uint bit = 1u << i;
                bool active = (_draftTagMask & bit) != 0u;
                string name = TacticalPuzzleModels.TagNames[i];
                float w = name.Length >= 4 ? 114f : 88f;
                ChipButton("EdTag_" + i, _editorDynamicRoot, tx, 2f, w, 40f, name, active, () =>
                {
                    if (active) _draftTagMask &= ~bit;
                    else if (TacticalPuzzleModels.PopCountTags(_draftTagMask) < TacticalPuzzleModels.MaxTagsSelected)
                        _draftTagMask |= bit;
                    RebuildEditorBody();
                }, 17f);
                tx += w + 8f;
            }

            Label("EdRoundLbl", _editorDynamicRoot, 930f, 4f, 160f, 40f, "破局回合上限：", 18f, Ink, bold: true);
            for (uint r = 1u; r <= 6u; r++)
            {
                uint captured = r;
                ChipButton("EdRound_" + r, _editorDynamicRoot, 1094f + (r - 1u) * 86f, 2f, 78f, 40f,
                    $"{r}回合", _draftSpec.RoundLimit == r, () =>
                    {
                        _draftSpec.RoundLimit = captured;
                        RebuildEditorBody();
                    }, 17f);
            }

            Sprite panelSprite = LoadSlice("puzzle_editor_panel_bg");

            // ── 左栏：我方阵营面板（820×586） ──
            var leftBg = SlicedOrTint("EditorLeftPanel", _editorDynamicRoot, 32f, 56f, 820f, 594f, panelSprite, PaperSoft);
            var left = (RectTransform)leftBg.transform;
            Label("LeftHead", left, 28f, 20f, 760f, 34f, "我方公平模板（所有挑战者使用同一面板）", 22f, Jade, bold: true);

            // 人物预设
            Label("CharLbl", left, 28f, 66f, 120f, 40f, "人物流派：", 18f, Ink, bold: true);
            for (int i = 0; i < TacticalPuzzleModels.CharacterPresetIds.Length; i++)
            {
                uint pid = TacticalPuzzleModels.CharacterPresetIds[i];
                ChipButton("Char_" + pid, left, 148f + i * 212f, 64f, 202f, 42f,
                    TacticalPuzzleModels.CharacterPresetName(pid), _draftSpec.CharacterPresetId == pid, () =>
                    {
                        _draftSpec.CharacterPresetId = pid;
                        if (!TacticalPuzzleModels.IsAllowedPresetSkill(_draftSpec.CharacterPresetId, _draftSpec.PetPresetId, _draftSpec.BannedSkillId))
                            _draftSpec.BannedSkillId = 0u;
                        RebuildEditorBody();
                    }, 18f);
            }

            // 灵宠预设
            Label("PetLbl", left, 28f, 122f, 120f, 40f, "出战灵宠：", 18f, Ink, bold: true);
            for (int i = 0; i < TacticalPuzzleModels.PetPresetIds.Length; i++)
            {
                uint pid = TacticalPuzzleModels.PetPresetIds[i];
                ChipButton("Pet_" + pid, left, 148f + i * 160f, 120f, 152f, 42f,
                    TacticalPuzzleModels.PetPresetName(pid), _draftSpec.PetPresetId == pid, () =>
                    {
                        _draftSpec.PetPresetId = pid;
                        if (!TacticalPuzzleModels.IsAllowedPresetSkill(_draftSpec.CharacterPresetId, _draftSpec.PetPresetId, _draftSpec.BannedSkillId))
                            _draftSpec.BannedSkillId = 0u;
                        RebuildEditorBody();
                    }, 17f);
            }

            // 五行阵法
            Label("FormLbl", left, 28f, 178f, 120f, 40f, "五行阵法：", 18f, Ink, bold: true);
            for (int i = 0; i < TacticalPuzzleModels.FormationIds.Length; i++)
            {
                uint fid = TacticalPuzzleModels.FormationIds[i];
                ChipButton("Form_" + fid, left, 148f + i * 106f, 176f, 98f, 42f,
                    TacticalPuzzleModels.FormationName(fid), _draftSpec.FormationId == fid, () =>
                    {
                        _draftSpec.FormationId = fid;
                        RebuildEditorBody();
                    }, 17f);
            }

            // 我方初始血/蓝/盾步进器
            BuildStepper(left, "PlayerHp", 28f, 244f, "初始气血比例", $"{_draftSpec.PlayerInitialHpPct}%",
                () =>
                {
                    int next = Math.Max((int)TacticalPuzzleModels.MinPlayerHpPct, (int)_draftSpec.PlayerInitialHpPct - 10);
                    _draftSpec.PlayerInitialHpPct = (uint)next;
                    RebuildEditorBody();
                },
                () =>
                {
                    int next = Math.Min((int)TacticalPuzzleModels.MaxPlayerHpPct, (int)_draftSpec.PlayerInitialHpPct + 10);
                    _draftSpec.PlayerInitialHpPct = (uint)next;
                    RebuildEditorBody();
                });

            BuildStepper(left, "PlayerMp", 28f, 306f, "初始法力比例", $"{_draftSpec.PlayerInitialMpPct}%",
                () =>
                {
                    int next = Math.Max((int)TacticalPuzzleModels.MinPlayerMpPct, (int)_draftSpec.PlayerInitialMpPct - 10);
                    _draftSpec.PlayerInitialMpPct = (uint)next;
                    RebuildEditorBody();
                },
                () =>
                {
                    int next = Math.Min((int)TacticalPuzzleModels.MaxPlayerMpPct, (int)_draftSpec.PlayerInitialMpPct + 10);
                    _draftSpec.PlayerInitialMpPct = (uint)next;
                    RebuildEditorBody();
                });

            BuildStepper(left, "PlayerShield", 28f, 368f, "开场护盾比例", $"{_draftSpec.PlayerInitialShieldPct}%",
                () =>
                {
                    int next = Math.Max(0, (int)_draftSpec.PlayerInitialShieldPct - 10);
                    _draftSpec.PlayerInitialShieldPct = (uint)next;
                    RebuildEditorBody();
                },
                () =>
                {
                    int next = Math.Min((int)TacticalPuzzleModels.MaxPlayerShieldPct, (int)_draftSpec.PlayerInitialShieldPct + 10);
                    _draftSpec.PlayerInitialShieldPct = (uint)next;
                    RebuildEditorBody();
                });

            // 我方开场不利状态
            Label("DebuffLbl", left, 28f, 440f, 760f, 30f, "我方开场不利状态：", 18f, Ink, bold: true);
            for (uint d = 0u; d <= 3u; d++)
            {
                uint captured = d;
                ChipButton("Debuff_" + d, left, 28f + d * 192f, 478f, 184f, 44f,
                    TacticalPuzzleModels.PlayerDebuffName(d), _draftSpec.PlayerInitialDebuffKind == d, () =>
                    {
                        _draftSpec.PlayerInitialDebuffKind = captured;
                        RebuildEditorBody();
                    }, 16f);
            }

            // ── 右栏：敌方阵营面板（820×594） ──
            var rightBg = SlicedOrTint("EditorRightPanel", _editorDynamicRoot, 868f, 56f, 820f, 594f, panelSprite, PaperSoft);
            var right = (RectTransform)rightBg.transform;
            Label("RightHead", right, 28f, 20f, 420f, 34f, $"敌方阵容（当前 {_draftSpec.Enemies.Count}/3 名）", 22f, Jade, bold: true);

            // 敌方槽位页签 + 添加/移除
            for (int i = 0; i < _draftSpec.Enemies.Count; i++)
            {
                int idx = i;
                ChipButton("EnemyTab_" + i, right, 28f + i * 116f, 64f, 108f, 42f,
                    $"敌方 {i + 1}", _selectedEnemyIndex == i, () =>
                    {
                        _selectedEnemyIndex = idx;
                        RebuildEditorBody();
                    }, 18f);
            }
            if (_draftSpec.Enemies.Count < TacticalPuzzleModels.MaxEnemies)
            {
                ActionButton("AddEnemyBtn", right, 420f, 62f, 168f, 44f, "+ 添加敌方", 18f, false, true, () =>
                {
                    _draftSpec.Enemies.Add(new global::TacticalPuzzleEnemySpec
                    {
                        MonsterTableId = 312u,
                        InitialHpPct = 80u,
                        InitialShieldPct = 0u,
                        SpeedDeltaPct = 0,
                        InitialBuffKind = 0u,
                    });
                    _selectedEnemyIndex = _draftSpec.Enemies.Count - 1;
                    RebuildEditorBody();
                });
            }
            if (_draftSpec.Enemies.Count > TacticalPuzzleModels.MinEnemies)
            {
                ActionButton("RemoveEnemyBtn", right, 602f, 62f, 184f, 44f, "- 移除当前敌方", 18f, false, true, () =>
                {
                    _draftSpec.Enemies.RemoveAt(_selectedEnemyIndex);
                    _selectedEnemyIndex = Mathf.Clamp(_selectedEnemyIndex, 0, _draftSpec.Enemies.Count - 1);
                    RebuildEditorBody();
                });
            }

            var enemy = _draftSpec.Enemies[_selectedEnemyIndex];

            // 敌方模板（2×3 网格）
            Label("MonsterLbl", right, 28f, 118f, 760f, 28f, $"敌方 {_selectedEnemyIndex + 1} 模板：", 18f, Ink, bold: true);
            for (int i = 0; i < TacticalPuzzleModels.MonsterIds.Length; i++)
            {
                uint mid = TacticalPuzzleModels.MonsterIds[i];
                int col = i % 3;
                int row = i / 3;
                ChipButton("Monster_" + mid, right, 28f + col * 256f, 152f + row * 48f, 246f, 42f,
                    TacticalPuzzleModels.MonsterName(mid), enemy.MonsterTableId == mid, () =>
                    {
                        enemy.MonsterTableId = mid;
                        RebuildEditorBody();
                    }, 17f);
            }

            // 敌方血/盾/速度步进器
            BuildStepper(right, "EnemyHp", 28f, 260f, "初始血量比例", $"{enemy.InitialHpPct}%",
                () =>
                {
                    int next = Math.Max((int)TacticalPuzzleModels.MinEnemyHpPct, (int)enemy.InitialHpPct - 10);
                    enemy.InitialHpPct = (uint)next;
                    RebuildEditorBody();
                },
                () =>
                {
                    int next = Math.Min((int)TacticalPuzzleModels.MaxEnemyHpPct, (int)enemy.InitialHpPct + 10);
                    enemy.InitialHpPct = (uint)next;
                    RebuildEditorBody();
                });

            BuildStepper(right, "EnemyShield", 28f, 322f, "开场护盾比例", $"{enemy.InitialShieldPct}%",
                () =>
                {
                    int next = Math.Max(0, (int)enemy.InitialShieldPct - 10);
                    enemy.InitialShieldPct = (uint)next;
                    RebuildEditorBody();
                },
                () =>
                {
                    int next = Math.Min((int)TacticalPuzzleModels.MaxEnemyShieldPct, (int)enemy.InitialShieldPct + 10);
                    enemy.InitialShieldPct = (uint)next;
                    RebuildEditorBody();
                });

            string speedLabel = enemy.SpeedDeltaPct > 0 ? $"+{enemy.SpeedDeltaPct}%" : $"{enemy.SpeedDeltaPct}%";
            BuildStepper(right, "EnemySpeed", 28f, 384f, "先手速度修正", speedLabel,
                () =>
                {
                    enemy.SpeedDeltaPct = Math.Max(TacticalPuzzleModels.MinEnemySpeedDeltaPct, enemy.SpeedDeltaPct - 10);
                    RebuildEditorBody();
                },
                () =>
                {
                    enemy.SpeedDeltaPct = Math.Min(TacticalPuzzleModels.MaxEnemySpeedDeltaPct, enemy.SpeedDeltaPct + 10);
                    RebuildEditorBody();
                });

            // 敌方开场增益（0..4）
            Label("BuffLbl", right, 28f, 452f, 760f, 28f, "开场增益状态：", 18f, Ink, bold: true);
            for (uint b = 0u; b <= 4u; b++)
            {
                uint captured = b;
                int col = (int)(b % 3u);
                int row = (int)(b / 3u);
                ChipButton("EnemyBuff_" + b, right, 28f + col * 256f, 486f + row * 48f, 246f, 42f,
                    TacticalPuzzleModels.EnemyBuffName(b), enemy.InitialBuffKind == b, () =>
                    {
                        enemy.InitialBuffKind = captured;
                        RebuildEditorBody();
                    }, 16f);
            }

            // ── V1.5 盘面加深：场上五行灵脉阵眼 + 本局禁用 1 招预设技能（零新图，复用现有切片） ──
            Label("EdLeylineLbl", _editorDynamicRoot, 34f, 662f, 126f, 40f, "灵脉阵眼：", 17f, Ink, bold: true);
            for (int i = 0; i < TacticalPuzzleModels.LeylineElements.Length; i++)
            {
                var elem = TacticalPuzzleModels.LeylineElements[i];
                ChipButton("EdLeyline_" + i, _editorDynamicRoot, 160f + i * 86f, 660f, 80f, 44f,
                    TacticalPuzzleModels.LeylineName(elem), _draftSpec.LeylineElement == elem, () =>
                    {
                        _draftSpec.LeylineElement = elem;
                        RebuildEditorBody();
                    }, 15f);
            }

            Label("EdBanLbl", _editorDynamicRoot, 700f, 662f, 108f, 40f, "本局禁招：", 17f, Ink, bold: true);
            var availBans = TacticalPuzzleModels.GetAvailableBanSkills(_draftSpec.CharacterPresetId, _draftSpec.PetPresetId);
            float bx = 808f;
            ChipButton("EdBan_0", _editorDynamicRoot, bx, 660f, 82f, 44f, "不禁招", _draftSpec.BannedSkillId == 0u, () =>
            {
                _draftSpec.BannedSkillId = 0u;
                RebuildEditorBody();
            }, 15f);
            bx += 88f;
            float banChipW = availBans.Count > 6 ? 94f : 112f;
            for (int i = 0; i < availBans.Count && bx + banChipW <= 1692f; i++)
            {
                uint sid = availBans[i];
                ChipButton("EdBan_" + sid, _editorDynamicRoot, bx, 660f, banChipW, 44f,
                    TacticalPuzzleModels.PresetSkillName(sid), _draftSpec.BannedSkillId == sid, () =>
                    {
                        _draftSpec.BannedSkillId = sid;
                        RebuildEditorBody();
                    }, 14f);
                bx += banChipW + 6f;
            }

            // ── 底部自证说明与保存按钮 ──
            Label("VerifyRuleNote", _editorDynamicRoot, 34f, 714f, 960f, 56f,
                "自证铁律：新建或改题后，须由作者本人亲手打赢 1 局并存下标准解法录像，残局才会自动向全服公开，从根源杜绝无解死局。",
                17f, GoldInk, bold: true, wrap: true);

            ActionButton("EdCancelBtn", _editorDynamicRoot, 1020f, 716f, 152f, 52f,
                "取消", 20f, primary: false, interactable: true, CloseEditor);
            ActionButton("EdSaveDraftBtn", _editorDynamicRoot, 1186f, 716f, 200f, 52f,
                "仅保存草稿", 20f, primary: false, interactable: true, () =>
                {
                    SaveDraftRequested?.Invoke(_editingPuzzleId, _draftTitle, _draftHint, _draftTagMask, _draftSpec.Clone(), false);
                });
            ActionButton("EdSaveVerifyBtn", _editorDynamicRoot, 1400f, 716f, 286f, 52f,
                "保存并亲自验题", 21f, primary: true, interactable: true, () =>
                {
                    SaveDraftRequested?.Invoke(_editingPuzzleId, _draftTitle, _draftHint, _draftTagMask, _draftSpec.Clone(), true);
                });
        }

        private static void BuildStepper(
            UnityEngine.Transform parent,
            string key,
            float x,
            float y,
            string title,
            string valueText,
            Action onMinus,
            Action onPlus)
        {
            Label(key + "_Lbl", parent, x, y + 6f, 220f, 36f, title, 18f, Ink, bold: true);
            ActionButton(key + "_Minus", parent, x + 236f, y, 88f, 46f, "-10%", 18f, false, true, onMinus);
            var valBox = QdaoUguiFactory.CreateImage(key + "_ValBg", parent, x + 336f, y + 2f, 156f, 42f, null, false);
            valBox.color = Color.white;
            var outline = valBox.gameObject.AddComponent<Outline>();
            outline.effectColor = BorderGold;
            outline.effectDistance = new Vector2(1.2f, -1.2f);
            Label(key + "_Val", valBox.transform, 0f, 0f, 156f, 42f, valueText, 20f, Jade, bold: true, alignment: TextAlignmentOptions.Center);
            ActionButton(key + "_Plus", parent, x + 504f, y, 88f, 46f, "+10%", 18f, false, true, onPlus);
        }
    }
}
