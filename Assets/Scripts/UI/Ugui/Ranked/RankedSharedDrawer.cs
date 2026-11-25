using System;
using System.Collections.Generic;
using System.Globalization;
using Match;
using MmorpgClient.Game.Rank;
using MmorpgClient.Game.Ranked;
using Rankpb;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Ranked.RankedArtCatalog;

namespace MmorpgClient.UI.Ugui.Ranked
{
    using Transform = UnityEngine.Transform;
    public enum RankedDrawerTab { Leaderboard = 0, SeasonRecord = 1, Cosmetics = 2 }

    /// <summary>
    /// 论剑右侧共享抽屉（1920x1080 坐标系，严格采用 component-export-manifest.json 的 pngDisplayRect 摆位）：
    /// Tab 0：排行榜（Top100 / 我的附近 + 翻页 + 异常/未亮段/上季结算中态）；
    /// Tab 1：赛季记录（本季战绩、4/8/12 三档展示权益、历届榜首与从缺/结算中态）；
    /// Tab 2：已获外观（实景名片与玉符预览、头像纹样/名片底纹/青云玉符佩戴与隐藏开关）。
    /// </summary>
    public sealed class RankedSharedDrawer
    {
        private sealed class RankRowView
        {
            public GameObject Root;
            public Image Bg;
            public TMP_Text RankText;
            public TMP_Text NameText;
            public TMP_Text ScoreText;
        }

        private sealed class CosmeticRowView
        {
            public Image RowBg;
            public Image Icon;
            public TMP_Text Title;
            public TMP_Text Sub;
            public Button ActionBtn;
            public Image ActionBg;
            public TMP_Text ActionLabel;
        }

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public RankedDrawerTab CurrentTab { get; private set; } = RankedDrawerTab.Leaderboard;

        public event Action Closed;
        public event Action<bool> LeaderboardModeRequested; // true = around self, false = top page
        public event Action<uint> LeaderboardPageRequested;
        public event Action LeaderboardRefreshRequested;
        public event Action<RankedCosmeticLoadout> CosmeticUpdateRequested;

        private readonly RectTransform _root;
        private readonly Image[] _tabImages = new Image[3];
        private readonly TMP_Text[] _tabLabels = new TMP_Text[3];
        private readonly GameObject[] _tabBodies = new GameObject[3];

        // Tab 0: 排行榜
        private bool _aroundMode;
        private Image _filterTopImg, _filterSelfImg;
        private TMP_Text _filterTopLabel, _filterSelfLabel;
        private TMP_Text _rankStatusText;
        private TMP_Text _rankPageText;
        private Button _rankPrevBtn, _rankNextBtn, _rankRefreshBtn;
        private readonly List<RankRowView> _rankRows = new List<RankRowView>(5);
        private TMP_Text _selfRankText, _selfNameText, _selfScoreText;

        // Tab 1: 赛季记录
        private TMP_Text _seasonTitleText, _seasonStatsText;
        private TMP_Text _rewardSummaryText;
        private TMP_Text _pastSeasonsText;

        // Tab 2: 已获外观
        private Image _previewCardBg, _previewAvatar, _previewFrame, _previewTalisman;
        private TMP_Text _previewNameText, _previewSubtitleText;
        private readonly CosmeticRowView[] _cosmeticRows = new CosmeticRowView[3];
        private TMP_Text _cosmeticStatusText;

        private RankedProfileSnapshot _profile;
        private string _playerName = "道友";
        private uint _currentPage = 1;
        private ulong _pageCount = 1;

        public RankedSharedDrawer(Transform designParent)
        {
            _root = QdaoUguiFactory.CreateRect("RankedSharedDrawer", designParent, 0f, 0f, DesignWidth, DesignHeight);

            // 点击抽屉外透明遮罩可收起抽屉，但不破坏主界面操作
            var backdrop = QdaoUguiFactory.CreateHitButton("DrawerBackdrop", _root, 0f, 0f, DesignWidth, DesignHeight);
            backdrop.onClick.AddListener(Hide);

            // 抽屉底板：严格使用 pngDisplayRect [918, 268, 854, 708]
            var panel = CreateArt("DrawerPanel", _root, 918f, 268f, 854f, 708f, "drawer-panel", preserveAspect: false, raycastTarget: true);
            panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            CreateLabel("DrawerTitle", _root, 956f, 286f, 420f, 48f, "论剑档案", 30f, JadeHeading, bold: true);

            // 收起按钮：pngDisplayRect [1618, 285, 122, 52]
            var collapse = CreateArtButton("DrawerCollapse", _root, 1618f, 285f, 122f, 52f,
                "drawer-collapse", "收起", 22f, JadeDark, out _, out _);
            collapse.onClick.AddListener(Hide);

            // 3 个顶栏页签：pngDisplayRect y=348, w=248, h=59, x=950 / 1211 / 1472
            string[] tabTitles = { "排行榜", "赛季记录", "已获外观" };
            float[] tabXs = { 950f, 1211f, 1472f };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var tabBtn = CreateArtButton("DrawerTab_" + i, _root, tabXs[i], 348f, 248f, 59f,
                    i == 0 ? "drawer-tab-selected" : "drawer-tab-idle",
                    tabTitles[i], 23f, i == 0 ? ButtonGoldText : InkSecondary,
                    out _tabImages[i], out _tabLabels[i]);
                tabBtn.onClick.AddListener(() => SelectTab((RankedDrawerTab)idx));
            }

            BuildLeaderboardTab();
            BuildSeasonRecordTab();
            BuildCosmeticsTab();

            _root.gameObject.SetActive(false);
        }

        public void Show(RankedDrawerTab tab)
        {
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            SelectTab(tab);
        }

        public void Hide()
        {
            if (!IsVisible) return;
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        public void SelectTab(RankedDrawerTab tab)
        {
            CurrentTab = tab;
            for (int i = 0; i < 3; i++)
            {
                bool active = (int)tab == i;
                _tabImages[i].sprite = Load(active ? "drawer-tab-selected" : "drawer-tab-idle");
                _tabLabels[i].color = active ? ButtonGoldText : InkSecondary;
                _tabBodies[i].SetActive(active);
            }
            if (tab == RankedDrawerTab.Leaderboard)
                LeaderboardModeRequested?.Invoke(_aroundMode);
        }

        public void RenderProfile(RankedProfileSnapshot profile, string playerName, bool cosmeticSaving, string statusText)
        {
            _profile = profile;
            _playerName = string.IsNullOrEmpty(playerName) ? "道友" : playerName;
            RenderSeasonRecord();
            RenderCosmetics(cosmeticSaving, statusText);
        }

        public void RenderLeaderboard(RankClient rankClient, RankedProfileSnapshot profile)
        {
            _profile = profile;
            if (rankClient == null)
            {
                _rankStatusText.text = "排行榜服务未连接，请稍后重试。";
                ClearRankRows();
                return;
            }

            bool settling = profile != null && profile.IsSeasonSettling;
            bool qualified = profile?.CurrentProgress?.Qualified == true;
            var page = rankClient.Page;
            var around = rankClient.Around;
            ulong limit = rankClient.RankLimit;

            if (rankClient.Waiting)
                _rankStatusText.text = RankClient.LoadingMessage;
            else if (!string.IsNullOrEmpty(rankClient.Status))
                _rankStatusText.text = settling ? $"上季结算中 · {rankClient.Status}" : rankClient.Status;
            else if (settling)
                _rankStatusText.text = "上季结算中，当前展示既有快照，封榜后切换新季榜。";
            else if (!qualified)
                _rankStatusText.text = "完成 1 场有效论剑即可亮段上榜（五档人数共用一榜）。";
            else
                _rankStatusText.text = _aroundMode ? "当前展示：我的附近名次" : "当前展示：本季前 100 名（精确排名）";

            IList<RankEntry> entries = _aroundMode
                ? (IList<RankEntry>)around?.Entries ?? Array.Empty<RankEntry>()
                : (IList<RankEntry>)page?.Entries ?? Array.Empty<RankEntry>();
            RankEntry self = _aroundMode ? around?.Self : page?.Self;

            _currentPage = rankClient.CurrentPage;
            _pageCount = rankClient.PageCount;
            _rankPageText.text = _aroundMode ? "附近名次" : $"第 {_currentPage} / {_pageCount} 页";
            _rankPrevBtn.interactable = !_aroundMode && !rankClient.Waiting && _currentPage > 1;
            _rankNextBtn.interactable = !_aroundMode && !rankClient.Waiting && _currentPage < _pageCount;

            for (int i = 0; i < _rankRows.Count; i++)
            {
                var row = _rankRows[i];
                if (i < entries.Count)
                {
                    var e = entries[i];
                    row.Root.SetActive(true);
                    row.RankText.text = RankClient.FormatRowRank(e, limit);
                    row.NameText.text = string.IsNullOrEmpty(e.Name) ? $"道友 {e.EntityId}" : e.Name;
                    row.ScoreText.text = RankedFormatting.FormatRating(e.Score) + " 分";
                }
                else
                {
                    row.Root.SetActive(false);
                }
            }

            if (self != null && (self.Rank > 0 || self.BeyondRankLimit))
            {
                _selfRankText.text = RankClient.FormatSelfRank(self, limit);
                _selfNameText.text = string.IsNullOrEmpty(self.Name) ? _playerName : self.Name;
                _selfScoreText.text = RankedFormatting.FormatRating(self.Score) + " 分";
            }
            else if (profile?.CurrentProgress != null)
            {
                _selfRankText.text = qualified ? "榜外" : "未亮段";
                _selfNameText.text = _playerName;
                _selfScoreText.text = $"{RankedFormatting.FormatRating(profile.CurrentProgress.RatingCenti)} 分 · {RankedFormatting.TierDisplayName(profile.CurrentProgress.CurrentTier, qualified)}";
            }
            else
            {
                _selfRankText.text = "暂未上榜";
                _selfNameText.text = _playerName;
                _selfScoreText.text = "--";
            }
        }

        private void BuildLeaderboardTab()
        {
            var body = QdaoUguiFactory.CreateRect("TabLeaderboard", _root, 0f, 0f, DesignWidth, DesignHeight);
            _tabBodies[0] = body.gameObject;

            var topBtn = CreateArtButton("RankFilterTop100", body, 950f, 418f, 132f, 48f,
                "rank-filter-top100", "前 100", 20f, ButtonGoldText, out _filterTopImg, out _filterTopLabel);
            topBtn.onClick.AddListener(() => SetLeaderboardMode(false));

            var selfBtn = CreateArtButton("RankFilterSelf", body, 1090f, 418f, 116f, 48f,
                "rank-filter-self", "我的附近", 20f, JadeDark, out _filterSelfImg, out _filterSelfLabel);
            selfBtn.onClick.AddListener(() => SetLeaderboardMode(true));

            _rankRefreshBtn = CreateArtButton("RankRefresh", body, 1618f, 418f, 118f, 48f,
                "drawer-collapse", "刷新", 20f, JadeDark, out _, out _);
            _rankRefreshBtn.onClick.AddListener(() => LeaderboardRefreshRequested?.Invoke());

            _rankStatusText = CreateLabel("RankStatus", body, 954f, 472f, 780f, 32f,
                "正在读取排行榜…", 19f, BronzeHint);

            for (int i = 0; i < 5; i++)
            {
                float y = 508f + i * 54f;
                string key = (i % 2 == 0) ? "rank-row-light" : "rank-row-alternate";
                var bg = CreateArt("RankRow_" + i, body, 950f, y, 786f, 52f, key);
                var rankTxt = CreateLabel("RankNo", bg.transform, 20f, 0f, 130f, 52f, "", 22f, JadeDark, bold: true);
                var nameTxt = CreateLabel("RankName", bg.transform, 160f, 0f, 410f, 52f, "", 22f, InkSecondary);
                var scoreTxt = CreateLabel("RankScore", bg.transform, 580f, 0f, 184f, 52f, "", 22f, JadeAccent,
                    TextAlignmentOptions.MidlineRight, bold: true);
                _rankRows.Add(new RankRowView { Root = bg.gameObject, Bg = bg, RankText = rankTxt, NameText = nameTxt, ScoreText = scoreTxt });
                bg.gameObject.SetActive(false);
            }

            // 固定本人行：pngDisplayRect [950, 799, 786, 72]
            var selfBg = CreateArt("RankSelfRow", body, 950f, 799f, 786f, 72f, "rank-self-row");
            _selfRankText = CreateLabel("SelfRank", selfBg.transform, 22f, 0f, 170f, 72f, "未亮段", 22f, JadeSelected, bold: true);
            _selfNameText = CreateLabel("SelfName", selfBg.transform, 200f, 0f, 320f, 72f, "道友", 22f, JadeDark, bold: true);
            _selfScoreText = CreateLabel("SelfScore", selfBg.transform, 530f, 0f, 234f, 72f, "1500 分", 22f, BronzeHint,
                TextAlignmentOptions.MidlineRight, bold: true);

            // 翻页栏
            _rankPrevBtn = CreateArtButton("RankPrevPage", body, 950f, 888f, 128f, 48f,
                "drawer-collapse", "上一页", 19f, JadeDark, out _, out _);
            _rankPrevBtn.onClick.AddListener(() => { if (_currentPage > 1) LeaderboardPageRequested?.Invoke(_currentPage - 1); });

            _rankPageText = CreateLabel("RankPageNo", body, 1100f, 888f, 490f, 48f,
                "第 1 / 1 页", 20f, InkMuted, TextAlignmentOptions.Center);

            _rankNextBtn = CreateArtButton("RankNextPage", body, 1608f, 888f, 128f, 48f,
                "drawer-collapse", "下一页", 19f, JadeDark, out _, out _);
            _rankNextBtn.onClick.AddListener(() => { if (_currentPage < _pageCount) LeaderboardPageRequested?.Invoke(_currentPage + 1); });
        }

        private void SetLeaderboardMode(bool around)
        {
            _aroundMode = around;
            _filterTopLabel.color = around ? JadeDark : ButtonGoldText;
            _filterSelfLabel.color = around ? ButtonGoldText : JadeDark;
            LeaderboardModeRequested?.Invoke(_aroundMode);
        }

        private void ClearRankRows()
        {
            foreach (var r in _rankRows) r.Root.SetActive(false);
        }

        private void BuildSeasonRecordTab()
        {
            var body = QdaoUguiFactory.CreateRect("TabSeasonRecord", _root, 0f, 0f, DesignWidth, DesignHeight);
            _tabBodies[1] = body.gameObject;

            // 本季摘要卡：pngDisplayRect [950, 428, 786, 136]
            var curCard = CreateArt("SeasonCurrentCard", body, 950f, 428f, 786f, 136f, "season-current-card");
            _seasonTitleText = CreateLabel("SeasonCurTitle", curCard.transform, 28f, 16f, 730f, 38f,
                "当前赛季加载中…", 24f, JadeHeading, bold: true);
            _seasonStatsText = CreateLabel("SeasonCurStats", curCard.transform, 28f, 60f, 730f, 64f,
                "", 20f, InkSecondary, wrap: true);

            // 里程碑展示权益卡：pngDisplayRect [950, 580, 786, 176]
            var rewardCard = CreateArt("SeasonRewardCard", body, 950f, 580f, 786f, 176f, "season-reward-card");
            CreateLabel("SeasonRewardTitle", rewardCard.transform, 28f, 14f, 730f, 36f,
                "赛季累计有效场次奖励（永久展示外观，不加任何数值）", 21f, JadeHeading, bold: true);
            _rewardSummaryText = CreateLabel("SeasonRewardBody", rewardCard.transform, 28f, 54f, 730f, 110f,
                "", 19f, InkSecondary, wrap: true);

            // 历届赛季与榜首卡：pngDisplayRect [950, 772, 786, 135]
            var pastCard = CreateArt("PreviousSeasonsCard", body, 950f, 772f, 786f, 135f, "previous-seasons-card");
            CreateLabel("PastSeasonsTitle", pastCard.transform, 28f, 12f, 730f, 34f,
                "历届赛季与榜首记录", 21f, JadeHeading, bold: true);
            _pastSeasonsText = CreateLabel("PastSeasonsBody", pastCard.transform, 28f, 48f, 730f, 78f,
                "暂无已封榜的历届赛季记录。", 19f, InkMuted, wrap: true);

            body.gameObject.SetActive(false);
        }

        private void RenderSeasonRecord()
        {
            if (_profile == null || !_profile.HasSeason)
            {
                _seasonTitleText.text = "论剑档案同步中…";
                _seasonStatsText.text = "连接就绪后将显示本季段位、积分与历届记录。";
                _rewardSummaryText.text = "4 场：头像纹样 · 8 场：名片底纹 · 12 场：青云玉符";
                _pastSeasonsText.text = "暂无历届赛季记录。";
                return;
            }

            var s = _profile.CurrentSeason;
            var p = _profile.CurrentProgress;
            string stateTag = s.State == RankedSeasonState.Settling ? "上季结算中"
                : s.State == RankedSeasonState.Closed ? "已封榜" : "进行中";
            _seasonTitleText.text = $"{s.SeasonName}（{s.SeasonKey}） · {stateTag}";
            if (p != null)
            {
                string tier = RankedFormatting.TierDisplayName(p.CurrentTier, p.Qualified);
                string peakTier = RankedFormatting.TierDisplayName(p.PeakTier, p.Qualified);
                _seasonStatsText.text =
                    $"当前：{tier} {RankedFormatting.FormatRating(p.RatingCenti)} 分 · 最高：{peakTier} {RankedFormatting.FormatRating(p.PeakRatingCenti)} 分\n" +
                    $"有效场次：{p.ValidMatches} 场（{p.Wins} 胜 / {p.Losses} 负 / {p.Draws} 平）· 五档人数共用积分与场次";
            }

            bool hasAvatar = _profile.HasEntitlement(RankedRewardKind.RankedRewardAvatarPattern);
            bool hasCard = _profile.HasEntitlement(RankedRewardKind.RankedRewardCardBackground);
            bool hasTalisman = _profile.HasEntitlement(RankedRewardKind.RankedRewardTalisman);
            uint valid = p?.ValidMatches ?? 0u;
            _rewardSummaryText.text =
                $"• 4 场 · 主题头像纹样：{(hasAvatar ? "已取得（永久）" : $"{Math.Min(valid, 4u)}/4")}\n" +
                $"• 8 场 · 主题名片底纹：{(hasCard ? "已取得（永久）" : $"{Math.Min(valid, 8u)}/8")}\n" +
                $"• 12 场 · 永久青云玉符：{(hasTalisman ? "已取得（永久）" : $"{Math.Min(valid, 12u)}/12")}";

            if (_profile.PastSeasons.Count == 0)
            {
                _pastSeasonsText.text = s.State == RankedSeasonState.Settling
                    ? "上季结算中：正在排空终局、封榜并落发赛季荣誉。"
                    : "本季为首测赛季，封榜后可在此查看历届榜首与个人最终名次。";
            }
            else
            {
                var lines = new List<string>();
                for (int i = 0; i < Math.Min(_profile.PastSeasons.Count, 3); i++)
                {
                    var ps = _profile.PastSeasons[i];
                    if (ps.State == RankedSeasonState.Settling)
                    {
                        lines.Add($"{ps.SeasonName}：上季结算中（封榜与奖励落账进行中）");
                    }
                    else if (ps.ChampionPlayerId != 0)
                    {
                        string champName = string.IsNullOrEmpty(ps.ChampionName) ? $"道友 {ps.ChampionPlayerId}" : ps.ChampionName;
                        lines.Add($"{ps.SeasonName}：榜首 {champName}（{RankedFormatting.FormatRating(ps.ChampionRatingCenti)} 分）");
                    }
                    else
                    {
                        lines.Add($"{ps.SeasonName}：已封榜 · 本季无双榜首从缺");
                    }
                }
                if (_profile.History.Count > 0)
                {
                    var last = _profile.History[0];
                    string rankStr = last.FinalRank > 0 ? $"第 {last.FinalRank} 名" : "未上榜";
                    lines.Add($"本人上季：{last.SeasonKey} · {RankedFormatting.TierDisplayName(last.CurrentTier, last.Qualified)} · {rankStr}");
                }
                _pastSeasonsText.text = string.Join("\n", lines);
            }
        }

        private void BuildCosmeticsTab()
        {
            var body = QdaoUguiFactory.CreateRect("TabCosmetics", _root, 0f, 0f, DesignWidth, DesignHeight);
            _tabBodies[2] = body.gameObject;

            // 顶部实景预览卡：pngDisplayRect [950, 428, 786, 123]
            var previewCard = CreateArt("NamePreviewCard", body, 950f, 428f, 786f, 123f, "name-preview-card");
            _previewCardBg = CreateArt("PreviewCardPattern", previewCard.transform, 16f, 12f, 754f, 99f, "namecard-pattern");
            _previewCardBg.color = new Color(1f, 1f, 1f, 0.35f);
            _previewAvatar = CreateArt("PreviewAvatar", previewCard.transform, 32f, 22f, 78f, 78f, "avatar-pozheng", preserveAspect: true);
            _previewFrame = CreateArt("PreviewFrame", previewCard.transform, 24f, 14f, 94f, 94f, "frame-pattern", preserveAspect: true);
            _previewNameText = CreateLabel("PreviewPlayerName", previewCard.transform, 134f, 22f, 360f, 40f,
                "道友", 26f, JadeDark, bold: true);
            _previewTalisman = CreateArt("PreviewTalisman", previewCard.transform, 496f, 28f, 28f, 28f, "talisman", preserveAspect: true);
            _previewSubtitleText = CreateLabel("PreviewSubtitle", previewCard.transform, 134f, 66f, 610f, 36f,
                "所有赛季外观仅用于展示，不提供任何战斗数值", 18f, InkMuted);

            string[] iconKeys = { "frame-pattern", "namecard-pattern", "talisman" };
            string[] titles = { "主题头像纹样", "主题名片底纹", "永久青云玉符" };
            string[] subs = { "累计 4 场有效论剑解锁", "累计 8 场有效论剑解锁", "累计 12 场有效论剑解锁（支持一键隐藏）" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                float rowY = 564f + i * 112f;
                var rowBg = CreateArt("CosmeticRow_" + i, body, 950f, rowY, 786f, 104f, "appearance-row-locked");
                var icon = CreateArt("CosmeticIcon_" + i, rowBg.transform, 26f, 16f, 72f, 72f, iconKeys[i], preserveAspect: true);
                var title = CreateLabel("CosmeticTitle_" + i, rowBg.transform, 114f, 16f, 430f, 36f, titles[i], 23f, JadeDark, bold: true);
                var sub = CreateLabel("CosmeticSub_" + i, rowBg.transform, 114f, 54f, 430f, 34f, subs[i], 18f, InkMuted);

                // 操作按钮：相对设计坐标 x=1548 -> 相对 row(950) 为 598, y=23, w=160, h=57
                var btn = CreateArtButton("CosmeticAction_" + i, rowBg.transform, 598f, 23f, 160f, 57f,
                    "appearance-action-idle", "未解锁", 20f, JadeDark, out var btnImg, out var btnLabel);
                btn.onClick.AddListener(() => OnCosmeticActionClicked(idx));
                _cosmeticRows[i] = new CosmeticRowView
                {
                    RowBg = rowBg, Icon = icon, Title = title, Sub = sub,
                    ActionBtn = btn, ActionBg = btnImg, ActionLabel = btnLabel,
                };
            }

            _cosmeticStatusText = CreateLabel("CosmeticStatus", body, 956f, 906f, 774f, 38f,
                "", 19f, BronzeHint);

            body.gameObject.SetActive(false);
        }

        private void RenderCosmetics(bool saving, string statusText)
        {
            _previewNameText.text = _playerName;
            _cosmeticStatusText.text = statusText ?? string.Empty;
            var loadout = _profile?.CosmeticLoadout ?? new RankedCosmeticLoadout();

            uint charPreset = _profile?.SelectedPresets?.CharacterPresetId ?? RankedFormatting.DefaultCharacterPresetId;
            _previewAvatar.sprite = CharacterAvatar(charPreset);

            var avatarEnt = _profile?.FindEntitlement(RankedRewardKind.RankedRewardAvatarPattern);
            var cardEnt = _profile?.FindEntitlement(RankedRewardKind.RankedRewardCardBackground);
            var talismanEnt = _profile?.FindEntitlement(RankedRewardKind.RankedRewardTalisman);

            bool avatarEquipped = avatarEnt != null && !string.IsNullOrEmpty(loadout.EquippedAvatarPatternCode);
            bool cardEquipped = cardEnt != null && !string.IsNullOrEmpty(loadout.EquippedCardBgCode);
            bool talismanEquipped = talismanEnt != null && !string.IsNullOrEmpty(loadout.EquippedTalismanCode) && !loadout.HideTalisman;

            _previewFrame.gameObject.SetActive(avatarEquipped);
            _previewCardBg.gameObject.SetActive(cardEquipped);
            _previewTalisman.gameObject.SetActive(talismanEquipped);

            UpdateCosmeticRow(0, avatarEnt != null, avatarEquipped, saving,
                avatarEquipped ? "取消佩戴" : "佩戴", "4 场解锁");
            UpdateCosmeticRow(1, cardEnt != null, cardEquipped, saving,
                cardEquipped ? "取消佩戴" : "佩戴", "8 场解锁");
            UpdateCosmeticRow(2, talismanEnt != null, talismanEquipped, saving,
                talismanEquipped ? "隐藏玉符" : "显示玉符", "12 场解锁");
        }

        private void UpdateCosmeticRow(int idx, bool owned, bool active, bool saving, string ownedLabel, string lockedLabel)
        {
            var row = _cosmeticRows[idx];
            row.RowBg.sprite = Load(owned ? "appearance-row-owned" : "appearance-row-locked");
            row.ActionBg.sprite = Load(active ? "appearance-action-selected" : "appearance-action-idle");
            row.ActionLabel.text = saving ? "保存中…" : (owned ? ownedLabel : lockedLabel);
            row.ActionLabel.color = active ? ButtonGoldText : (owned ? JadeDark : InkMuted);
            row.ActionBtn.interactable = owned && !saving;
        }

        private void OnCosmeticActionClicked(int idx)
        {
            if (_profile == null) return;
            var next = _profile.CosmeticLoadout?.Clone() ?? new RankedCosmeticLoadout();
            if (idx == 0)
            {
                var ent = _profile.FindEntitlement(RankedRewardKind.RankedRewardAvatarPattern);
                if (ent == null) return;
                next.EquippedAvatarPatternCode = string.IsNullOrEmpty(next.EquippedAvatarPatternCode) ? ent.ItemCode : string.Empty;
            }
            else if (idx == 1)
            {
                var ent = _profile.FindEntitlement(RankedRewardKind.RankedRewardCardBackground);
                if (ent == null) return;
                next.EquippedCardBgCode = string.IsNullOrEmpty(next.EquippedCardBgCode) ? ent.ItemCode : string.Empty;
            }
            else if (idx == 2)
            {
                var ent = _profile.FindEntitlement(RankedRewardKind.RankedRewardTalisman);
                if (ent == null) return;
                if (string.IsNullOrEmpty(next.EquippedTalismanCode))
                {
                    next.EquippedTalismanCode = ent.ItemCode;
                    next.HideTalisman = false;
                }
                else
                {
                    next.HideTalisman = !next.HideTalisman;
                }
            }
            CosmeticUpdateRequested?.Invoke(next);
        }
    }
}
