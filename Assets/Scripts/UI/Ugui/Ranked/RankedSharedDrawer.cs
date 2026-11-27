using System;
using System.Collections.Generic;
using System.Globalization;
using Match;
using MmorpgClient.Game.Rank;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Table;
using Rankpb;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Ranked.RankedArtCatalog;

namespace MmorpgClient.UI.Ugui.Ranked
{
    using Transform = UnityEngine.Transform;
    public enum RankedDrawerTab { Leaderboard = 0, SeasonRecord = 1, Cosmetics = 2, FeaturedBuilds = 3 }

    /// <summary>
    /// 论剑右侧共享抽屉（1920x1080 坐标系，严格采用 component-export-manifest.json 的 pngDisplayRect 摆位）：
    /// Tab 0：排行榜（Top100 / 我的附近 + 翻页 + 异常/未亮段/上季结算中态）；
    /// Tab 1：赛季记录（本季战绩、4/8/12 三档展示权益、宿敌账本与历届榜首）；
    /// Tab 2：已获外观（实景名片与玉符预览、头像纹样/名片底纹/青云玉符佩戴与隐藏开关）；
    /// Tab 3：问道名局（本季高段位热门配招汇总、直看公开名局回放、一键套用已拥有的赛季印与首发灵宠）。
    /// </summary>
    public sealed class RankedSharedDrawer
    {
        private const int DrawerTabCount = 4;
        private const int FeaturedCardsPerPage = 3;
        private const string DefaultFeaturedStatus = "本季高段位实时围观与热门配招：可围观正在激战的高段排位，或观摩回放并一键套用赛季印与首发宠。";

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

        private sealed class FeaturedBuildRowView
        {
            public GameObject Root;
            public Image Bg;
            public TMP_Text TitleText;
            public TMP_Text LoadoutText;
            public TMP_Text SkillsText;
            public Button ReplayBtn;
            public Button CopyBtn;
            public RankedFeaturedBuildView BoundBuild;
        }

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public RankedDrawerTab CurrentTab { get; private set; } = RankedDrawerTab.Leaderboard;

        public event Action Closed;
        public event Action<RankedDrawerTab> TabSelected;
        public event Action<bool> LeaderboardModeRequested; // true = around self, false = top page
        public event Action<uint> LeaderboardPageRequested;
        public event Action LeaderboardRefreshRequested;
        public event Action<RankedCosmeticLoadout> CosmeticUpdateRequested;
        public event Action<ulong> WatchFeaturedReplayRequested;
        public event Action<RankedFeaturedBuildView> ApplyFeaturedBuildRequested;
        public event Action RefreshLiveRankedRequested;
        public event Action<ulong> WatchLiveRankedRequested;

        private readonly RectTransform _root;
        private readonly Image[] _tabImages = new Image[DrawerTabCount];
        private readonly TMP_Text[] _tabLabels = new TMP_Text[DrawerTabCount];
        private readonly GameObject[] _tabBodies = new GameObject[DrawerTabCount];

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

        // Tab 2: 玉符与称号
        private Image _previewCardBg, _previewAvatar, _previewFrame, _previewTalisman;
        private TMP_Text _previewAvatarGlyph;
        private TMP_Text _previewNameText, _previewSubtitleText;
        private readonly CosmeticRowView[] _cosmeticRows = new CosmeticRowView[4];
        private readonly TMP_Text[] _seasonHonorStatus = new TMP_Text[3];
        private TMP_Text _cosmeticStatusText;
        private Sprite _portraitOverride;

        // Tab 3: 问道名局与擂台直播（V6 + V7）
        private TMP_Text _featuredStatusText;
        private TMP_Text _featuredEmptyText;
        private TMP_Text _liveBannerTitle;
        private TMP_Text _liveBannerBody;
        private Button _liveCycleBtn;
        private Button _liveRefreshBtn;
        private Button _liveWatchBtn;
        private readonly List<BattleWatchSummary> _liveRankedMatches = new List<BattleWatchSummary>();
        private int _liveRankedIndex;
        private readonly FeaturedBuildRowView[] _featuredRows = new FeaturedBuildRowView[FeaturedCardsPerPage];
        private GameObject _featuredPagerRoot;
        private Button _featuredPrevBtn;
        private Button _featuredNextBtn;
        private TMP_Text _featuredPageText;
        private int _featuredPageIndex;

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

            // 抽屉底板：采用已验收帮会同源宣纸底 + 细腻金边，避免旧论剑高光杂框（[918, 268, 854, 708]）
            var outerBorder = QdaoUguiFactory.CreateImage("DrawerBorder", _root, 915f, 265f, 860f, 714f, null, true);
            outerBorder.color = SeasonEconomyUiArt.BattleArenaWarmGold;
            var panel = QdaoUguiFactory.CreateImage("DrawerPanel", _root, 918f, 268f, 854f, 708f, null, true);
            panel.color = SeasonEconomyUiArt.BattleArenaPaper;
            panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            CreateGuildArt("DrawerHeaderBar", _root, 938f, 282f, 814f, 56f, "guild_table_header");
            CreateLabel("DrawerTitle", _root, 956f, 286f, 420f, 48f, "论剑档案", 30f, JadeHeading, bold: true);

            // 收起按钮：统一使用已验收帮会次按钮
            var collapse = CreateGuildButton("DrawerCollapse", _root, 1618f, 285f, 122f, 50f,
                "guild_button_secondary", "收起", 21f, JadeDark, out _, out _);
            collapse.onClick.AddListener(Hide);

            // 4 个顶栏页签：统一使用已验收帮会页签切片（guild_tab_selected / guild_tab_normal）
            string[] tabTitles = { "排行榜", "赛季记录", "玉符与称号", "问道名局" };
            float[] tabXs = { 950f, 1148f, 1346f, 1544f };
            for (int i = 0; i < DrawerTabCount; i++)
            {
                int idx = i;
                var tabBtn = CreateGuildButton("DrawerTab_" + i, _root, tabXs[i], 348f, 190f, 59f,
                    i == 0 ? "guild_tab_selected" : "guild_tab_normal",
                    tabTitles[i], 21f, i == 0 ? ButtonGoldText : InkSecondary,
                    out _tabImages[i], out _tabLabels[i]);
                tabBtn.onClick.AddListener(() => SelectTab((RankedDrawerTab)idx));
            }

            BuildLeaderboardTab();
            BuildSeasonRecordTab();
            BuildCosmeticsTab();
            BuildFeaturedBuildsTab();

            _root.gameObject.SetActive(false);
        }

        public void Show(RankedDrawerTab tab)
        {
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            SetFeaturedBuildStatus(DefaultFeaturedStatus);
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
            for (int i = 0; i < DrawerTabCount; i++)
            {
                bool active = (int)tab == i;
                _tabImages[i].sprite = LoadGuildApproved(active ? "guild_tab_selected" : "guild_tab_normal");
                _tabLabels[i].color = active ? ButtonGoldText : InkSecondary;
                _tabBodies[i].SetActive(active);
            }
            TabSelected?.Invoke(tab);
            if (tab == RankedDrawerTab.Leaderboard)
                LeaderboardModeRequested?.Invoke(_aroundMode);
        }

        public void SetFeaturedBuildStatus(string status, bool isError = false)
        {
            if (_featuredStatusText == null) return;
            _featuredStatusText.text = string.IsNullOrEmpty(status) ? DefaultFeaturedStatus : status;
            _featuredStatusText.color = isError ? WarnRed : BronzeHint;
        }

        public void RenderProfile(RankedProfileSnapshot profile, string playerName, bool cosmeticSaving, string statusText,
            Sprite portraitOverride = null)
        {
            _profile = profile;
            _playerName = string.IsNullOrEmpty(playerName) ? "道友" : playerName;
            _portraitOverride = portraitOverride;
            RenderSeasonRecord();
            RenderCosmetics(cosmeticSaving, statusText);
            RenderFeaturedBuilds();
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
                _rankStatusText.text = $"完成 1 场有效论剑即可亮段上榜（{RankedFormatting.FormatAllowedSizesLabel(profile?.AllowedTeamSizes)}）。";
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

            var topBtn = CreateGuildButton("RankFilterTop100", body, 950f, 418f, 132f, 48f,
                "guild_tab_selected", "前 100", 20f, ButtonGoldText, out _filterTopImg, out _filterTopLabel);
            topBtn.onClick.AddListener(() => SetLeaderboardMode(false));

            var selfBtn = CreateGuildButton("RankFilterSelf", body, 1090f, 418f, 132f, 48f,
                "guild_tab_normal", "我的附近", 20f, JadeDark, out _filterSelfImg, out _filterSelfLabel);
            selfBtn.onClick.AddListener(() => SetLeaderboardMode(true));

            _rankRefreshBtn = CreateGuildButton("RankRefresh", body, 1618f, 418f, 118f, 48f,
                "guild_button_secondary", "刷新", 20f, JadeDark, out _, out _);
            _rankRefreshBtn.onClick.AddListener(() => LeaderboardRefreshRequested?.Invoke());

            _rankStatusText = CreateLabel("RankStatus", body, 954f, 472f, 780f, 32f,
                "正在读取排行榜…", 19f, BronzeHint);

            for (int i = 0; i < 5; i++)
            {
                float y = 508f + i * 54f;
                var bg = CreateGuildArt("RankRow_" + i, body, 950f, y, 786f, 52f, "guild_row_normal");
                var rankTxt = CreateLabel("RankNo", bg.transform, 20f, 0f, 130f, 52f, "", 22f, JadeDark, bold: true);
                var nameTxt = CreateLabel("RankName", bg.transform, 160f, 0f, 410f, 52f, "", 22f, InkSecondary);
                var scoreTxt = CreateLabel("RankScore", bg.transform, 580f, 0f, 184f, 52f, "", 22f, JadeAccent,
                    TextAlignmentOptions.MidlineRight, bold: true);
                _rankRows.Add(new RankRowView { Root = bg.gameObject, Bg = bg, RankText = rankTxt, NameText = nameTxt, ScoreText = scoreTxt });
                bg.gameObject.SetActive(false);
            }

            // 固定本人行：采用已验收帮会高亮行（guild_row_self）
            var selfBg = CreateGuildArt("RankSelfRow", body, 950f, 799f, 786f, 72f, "guild_row_self");
            _selfRankText = CreateLabel("SelfRank", selfBg.transform, 22f, 0f, 170f, 72f, "未亮段", 22f, JadeSelected, bold: true);
            _selfNameText = CreateLabel("SelfName", selfBg.transform, 200f, 0f, 320f, 72f, "道友", 22f, JadeDark, bold: true);
            _selfScoreText = CreateLabel("SelfScore", selfBg.transform, 530f, 0f, 234f, 72f, "1500 分", 22f, BronzeHint,
                TextAlignmentOptions.MidlineRight, bold: true);

            // 翻页栏
            _rankPrevBtn = CreateGuildButton("RankPrevPage", body, 950f, 888f, 128f, 48f,
                "guild_button_secondary", "上一页", 19f, JadeDark, out _, out _);
            _rankPrevBtn.onClick.AddListener(() => { if (_currentPage > 1) LeaderboardPageRequested?.Invoke(_currentPage - 1); });

            _rankPageText = CreateLabel("RankPageNo", body, 1100f, 888f, 490f, 48f,
                "第 1 / 1 页", 20f, InkMuted, TextAlignmentOptions.Center);

            _rankNextBtn = CreateGuildButton("RankNextPage", body, 1608f, 888f, 128f, 48f,
                "guild_button_secondary", "下一页", 19f, JadeDark, out _, out _);
            _rankNextBtn.onClick.AddListener(() => { if (_currentPage < _pageCount) LeaderboardPageRequested?.Invoke(_currentPage + 1); });
        }

        private void SetLeaderboardMode(bool around)
        {
            _aroundMode = around;
            if (_filterTopImg != null) _filterTopImg.sprite = LoadGuildApproved(around ? "guild_tab_normal" : "guild_tab_selected");
            if (_filterSelfImg != null) _filterSelfImg.sprite = LoadGuildApproved(around ? "guild_tab_selected" : "guild_tab_normal");
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

            // 本季摘要卡：采用帮会宣纸卡 + 表头栏
            var curCard = CreateGuildArt("SeasonCurrentCard", body, 950f, 428f, 786f, 136f, "guild_row_normal");
            CreateGuildArt("SeasonCurrentHeader", curCard.transform, 0f, 0f, 786f, 46f, "guild_table_header");
            _seasonTitleText = CreateLabel("SeasonCurTitle", curCard.transform, 28f, 6f, 730f, 36f,
                "当前赛季加载中…", 22f, JadeHeading, bold: true);
            _seasonStatsText = CreateLabel("SeasonCurStats", curCard.transform, 28f, 54f, 730f, 72f,
                "", 20f, InkSecondary, wrap: true);

            // 里程碑展示权益卡
            var rewardCard = CreateGuildArt("SeasonRewardCard", body, 950f, 580f, 786f, 176f, "guild_row_normal");
            CreateGuildArt("SeasonRewardHeader", rewardCard.transform, 0f, 0f, 786f, 44f, "guild_table_header");
            CreateLabel("SeasonRewardTitle", rewardCard.transform, 28f, 6f, 730f, 34f,
                "赛季荣誉与展示奖励（永久展示外观，不加任何数值）", 20f, JadeHeading, bold: true);
            _rewardSummaryText = CreateLabel("SeasonRewardBody", rewardCard.transform, 28f, 50f, 730f, 118f,
                "", 18f, InkSecondary, wrap: true);

            // 宿敌账本与历届赛季榜首卡
            var pastCard = CreateGuildArt("PreviousSeasonsCard", body, 950f, 772f, 786f, 148f, "guild_row_normal");
            CreateGuildArt("PastSeasonsHeader", pastCard.transform, 0f, 0f, 786f, 42f, "guild_table_header");
            var rivalryHeaderIcon = SeasonEconomyUiArt.LoadRankedPlayV6Asset("icon_rivalry_crossed");
            float pastTitleX = 28f;
            if (rivalryHeaderIcon != null)
            {
                var rIcon = QdaoUguiFactory.CreateImage("PastSeasonsRivalryIcon", pastCard.transform, 20f, 9f, 24f, 24f, rivalryHeaderIcon, false);
                rIcon.preserveAspect = true;
                pastTitleX = 50f;
            }
            CreateLabel("PastSeasonsTitle", pastCard.transform, pastTitleX, 6f, 758f - pastTitleX, 32f,
                "宿敌账本与历届记录", 20f, JadeHeading, bold: true);
            _pastSeasonsText = CreateLabel("PastSeasonsBody", pastCard.transform, 28f, 48f, 730f, 94f,
                "暂无宿敌交手与已封榜赛季记录。", 17f, InkMuted, wrap: true);

            body.gameObject.SetActive(false);
        }

        private void RenderSeasonRecord()
        {
            if (_profile == null || !_profile.HasSeason)
            {
                _seasonTitleText.text = "论剑档案同步中…";
                _seasonStatsText.text = "连接就绪后将显示本季段位、积分与历届记录。";
                _rewardSummaryText.text = "4 场：头像纹样 · 8 场：名片底纹 · 12 场：青云玉符";
                _pastSeasonsText.text = "暂无宿敌交手与历届赛季记录。";
                return;
            }

            var s = _profile.CurrentSeason;
            var p = _profile.CurrentProgress;
            string stateTag = s.State == RankedSeasonState.Settling ? "上季结算中"
                : s.State == RankedSeasonState.Closed ? "已封榜" : "进行中";
            string periodTag = FormatSeasonWindow(s.StartMs, s.EndMs);
            _seasonTitleText.text = string.IsNullOrEmpty(periodTag)
                ? $"{s.SeasonName}（{s.SeasonKey}） · {stateTag}"
                : $"{s.SeasonName}（{periodTag}） · {stateTag}";
            if (p != null)
            {
                string tier = RankedFormatting.TierDisplayName(p.CurrentTier, p.Qualified);
                string peakTier = RankedFormatting.TierDisplayName(p.PeakTier, p.Qualified);
                string openSizes = FormatOpenTeamSizes(_profile.AllowedTeamSizes);
                _seasonStatsText.text =
                    $"当前：{tier} {RankedFormatting.FormatRating(p.RatingCenti)} 分 · 最高：{peakTier} {RankedFormatting.FormatRating(p.PeakRatingCenti)} 分\n" +
                    $"有效场次：{p.ValidMatches} 场（{p.Wins} 胜 / {p.Losses} 负 / {p.Draws} 平）· 当前开放 {openSizes} 共用积分";
            }

            bool hasAvatar = _profile.HasEntitlement(RankedRewardKind.RankedRewardAvatarPattern);
            bool hasCard = _profile.HasEntitlement(RankedRewardKind.RankedRewardCardBackground);
            bool hasTalisman = _profile.HasEntitlement(RankedRewardKind.RankedRewardTalisman);
            bool hasWinTitle = _profile.HasEntitlement(RankedRewardKind.RankedRewardWinMilestone);
            bool hasRankHonor = _profile.HasEntitlement(RankedRewardKind.RankedRewardTierBadge)
                || _profile.HasEntitlement(RankedRewardKind.RankedRewardTierTitle);
            uint valid = p?.ValidMatches ?? 0u;
            uint winTarget = _profile.MilestoneWinMatches;
            uint wins = p?.Wins ?? 0u;
            _rewardSummaryText.text =
                $"• 4/8/12 场永久外观：头像纹样({(hasAvatar ? "已获" : $"{Math.Min(valid, 4u)}/4")}) · " +
                $"名片底纹({(hasCard ? "已获" : $"{Math.Min(valid, 8u)}/8")}) · " +
                $"青云玉符({(hasTalisman ? "已获" : $"{Math.Min(valid, 12u)}/12")})\n" +
                $"• {winTarget} 胜永久「锋芒」称号：{(hasWinTitle ? "已取得（可于已获外观佩戴）" : $"{Math.Min(wins, winTarget)}/{winTarget}")}\n" +
                $"• 封榜荣誉（云章 / 天枢 / 无双）：{(hasRankHonor ? "已取得永久段位徽章与称号" : "赛季封榜达云章及以上永久解锁")}";

            var lines = new List<string>(5);
            if (_profile.TopRivals != null && _profile.TopRivals.Count > 0)
            {
                var rivalParts = new List<string>(3);
                int rivalCount = Math.Min(_profile.TopRivals.Count, 3);
                for (int i = 0; i < rivalCount; i++)
                {
                    var r = _profile.TopRivals[i];
                    if (r == null || r.OpponentId == 0UL) continue;
                    string oppName = string.IsNullOrEmpty(r.OpponentName) ? $"道友{r.OpponentId}" : r.OpponentName;
                    bool canRevenge = r.LastOutcome == 2u || r.Losses > r.Wins;
                    rivalParts.Add(canRevenge
                        ? $"{oppName} {r.Wins}胜{r.Losses}负(可雪耻)"
                        : $"{oppName} {r.Wins}胜{r.Losses}负");
                }
                if (rivalParts.Count > 0)
                    lines.Add("本季宿敌：" + string.Join(" · ", rivalParts));
            }

            if (_profile.PastSeasons.Count == 0)
            {
                lines.Add(s.State == RankedSeasonState.Settling
                    ? "上季结算中：正在排空终局、封榜并落发赛季荣誉。"
                    : "本季为首测赛季，封榜后可在此查看历届榜首与个人最终名次。");
            }
            else
            {
                for (int i = 0; i < Math.Min(_profile.PastSeasons.Count, 2); i++)
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
            }
            _pastSeasonsText.text = string.Join("\n", lines);
        }

        private void BuildCosmeticsTab()
        {
            var body = QdaoUguiFactory.CreateRect("TabCosmetics", _root, 0f, 0f, DesignWidth, DesignHeight);
            _tabBodies[2] = body.gameObject;

            // 顶部实景预览卡（真实玩家头像由 QdaoCharacterCatalog.LoadPortrait 绑定，严禁回退旧论剑错误人物素材）
            var previewCard = CreateGuildArt("NamePreviewCard", body, 950f, 428f, 786f, 123f, "guild_row_normal");
            _previewCardBg = CreateArt("PreviewCardPattern", previewCard.transform, 16f, 12f, 754f, 99f, "namecard-pattern");
            _previewCardBg.color = new Color(1f, 1f, 1f, 0.35f);
            _previewAvatar = QdaoUguiFactory.CreateImage("PreviewAvatar", previewCard.transform, 32f, 22f, 78f, 78f, null, false);
            _previewAvatar.preserveAspect = true;
            _previewAvatar.color = SeasonEconomyUiArt.BattleArenaJade;
            _previewAvatarGlyph = CreateLabel("PreviewAvatarGlyph", _previewAvatar.transform, 0f, 0f, 78f, 78f,
                "道", 32f, GoldCream, TextAlignmentOptions.Center, bold: true);
            _previewFrame = CreateArt("PreviewFrame", previewCard.transform, 24f, 14f, 94f, 94f, "frame-pattern", preserveAspect: true);
            _previewNameText = CreateLabel("PreviewPlayerName", previewCard.transform, 134f, 22f, 360f, 40f,
                "道友", 26f, JadeDark, bold: true);
            _previewTalisman = CreateArt("PreviewTalisman", previewCard.transform, 496f, 28f, 28f, 28f, "talisman", preserveAspect: true);
            _previewSubtitleText = CreateLabel("PreviewSubtitle", previewCard.transform, 134f, 66f, 610f, 36f,
                "所有赛季外观仅用于展示，不提供任何战斗数值", 18f, InkMuted);

            string[] iconKeys = { "frame-pattern", "namecard-pattern", "talisman", "rank-3" };
            string[] titles = { "主题头像纹样", "主题名片底纹", "永久青云玉符", "永久「锋芒」称号" };
            string[] subs = { "累计 4 场有效论剑解锁", "累计 8 场有效论剑解锁", "累计 12 场有效论剑解锁（支持一键隐藏）", "累计 6 胜或封榜云章以上解锁（可佩戴）" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                float rowY = 556f + i * 68f;
                var rowBg = CreateGuildArt("CosmeticRow_" + i, body, 950f, rowY, 786f, 62f, "guild_row_normal");
                var icon = CreateArt("CosmeticIcon_" + i, rowBg.transform, 18f, 8f, 46f, 46f, iconKeys[i], preserveAspect: true);
                var title = CreateLabel("CosmeticTitle_" + i, rowBg.transform, 78f, 6f, 480f, 28f, titles[i], 20f, JadeDark, bold: true);
                var sub = CreateLabel("CosmeticSub_" + i, rowBg.transform, 78f, 34f, 480f, 24f, subs[i], 16f, InkMuted);

                var btn = CreateGuildButton("CosmeticAction_" + i, rowBg.transform, 598f, 9f, 160f, 44f,
                    "guild_button_secondary", "未解锁", 18f, JadeDark, out var btnImg, out var btnLabel);
                btn.onClick.AddListener(() => OnCosmeticActionClicked(idx));
                _cosmeticRows[i] = new CosmeticRowView
                {
                    RowBg = rowBg, Icon = icon, Title = title, Sub = sub,
                    ActionBtn = btn, ActionBg = btnImg, ActionLabel = btnLabel,
                };
            }

            // 季末荣誉三卡（云章 / 天枢 / 无双，与上方 4 项永久展示外观分区展示，数据完全取自权威档案）
            CreateLabel("SeasonHonorHeading", body, 954f, 830f, 774f, 24f,
                "季末封榜荣誉（云章 / 天枢 / 无双 · 赛季结算永久颁发）", 17f, JadeHeading, bold: true);
            string[] honorTitles = { "云章", "天枢", "无双" };
            string[] honorIcons = { "rank-3", "rank-4", "rank-5" };
            for (int i = 0; i < 3; i++)
            {
                float cardX = 950f + i * 266f;
                var hBg = QdaoUguiFactory.CreateImage("SeasonHonorCard_" + i, body, cardX, 856f, 254f, 64f, null, false);
                hBg.color = SeasonEconomyUiArt.BattleArenaPaper;
                CreateArt("SeasonHonorIcon_" + i, hBg.transform, 8f, 10f, 44f, 44f, honorIcons[i], preserveAspect: true);
                CreateLabel("SeasonHonorTitle_" + i, hBg.transform, 58f, 8f, 188f, 24f, honorTitles[i] + " 荣誉", 18f, JadeDark, bold: true);
                _seasonHonorStatus[i] = CreateLabel("SeasonHonorSub_" + i, hBg.transform, 58f, 34f, 188f, 22f,
                    "赛季封榜达标解锁", 15f, InkMuted);
            }

            _cosmeticStatusText = CreateLabel("CosmeticStatus", body, 954f, 924f, 774f, 30f,
                "", 17f, BronzeHint);

            body.gameObject.SetActive(false);
        }

        private void RenderCosmetics(bool saving, string statusText)
        {
            _previewNameText.text = _playerName;
            _cosmeticStatusText.text = statusText ?? string.Empty;
            var loadout = _profile?.CosmeticLoadout ?? new RankedCosmeticLoadout();

            _previewAvatar.sprite = _portraitOverride;
            _previewAvatar.color = _portraitOverride != null ? Color.white : SeasonEconomyUiArt.BattleArenaJade;
            if (_previewAvatarGlyph != null)
            {
                _previewAvatarGlyph.gameObject.SetActive(_portraitOverride == null);
                if (_portraitOverride == null)
                    _previewAvatarGlyph.text = !string.IsNullOrEmpty(_playerName) ? _playerName.Substring(0, 1) : "道";
            }

            var avatarEnt = _profile?.FindEntitlement(RankedRewardKind.RankedRewardAvatarPattern);
            var cardEnt = _profile?.FindEntitlement(RankedRewardKind.RankedRewardCardBackground);
            var talismanEnt = _profile?.FindEntitlement(RankedRewardKind.RankedRewardTalisman);
            var titleEnt = _profile?.FindEntitlement(RankedRewardKind.RankedRewardWinMilestone)
                ?? _profile?.FindEntitlement(RankedRewardKind.RankedRewardTierTitle);

            bool avatarEquipped = avatarEnt != null && !string.IsNullOrEmpty(loadout.EquippedAvatarPatternCode);
            bool cardEquipped = cardEnt != null && !string.IsNullOrEmpty(loadout.EquippedCardBgCode);
            bool talismanEquipped = talismanEnt != null && !string.IsNullOrEmpty(loadout.EquippedTalismanCode) && !loadout.HideTalisman;
            bool titleEquipped = titleEnt != null && !string.IsNullOrEmpty(loadout.EquippedTitleCode);

            _previewFrame.gameObject.SetActive(avatarEquipped);
            _previewCardBg.gameObject.SetActive(cardEquipped);
            _previewTalisman.gameObject.SetActive(talismanEquipped);
            _previewSubtitleText.text = titleEquipped
                ? $"当前佩戴称号：{RankedFormatting.RewardKindName(titleEnt.Kind)}（仅展示，不加战斗数值）"
                : "所有赛季外观与称号仅用于展示，不提供任何战斗数值";

            UpdateCosmeticRow(0, avatarEnt != null, avatarEquipped, saving,
                avatarEquipped ? "取消佩戴" : "佩戴", "4 场解锁");
            UpdateCosmeticRow(1, cardEnt != null, cardEquipped, saving,
                cardEquipped ? "取消佩戴" : "佩戴", "8 场解锁");
            UpdateCosmeticRow(2, talismanEnt != null, talismanEquipped, saving,
                talismanEquipped ? "隐藏玉符" : "显示玉符", "12 场解锁");
            uint winTarget = _profile?.MilestoneWinMatches ?? RankedFormatting.DefaultMilestoneWinMatches;
            UpdateCosmeticRow(3, titleEnt != null, titleEquipped, saving,
                titleEquipped ? "卸下称号" : "佩戴称号", $"{winTarget} 胜解锁");

            bool hasTierHonor = _profile?.HasEntitlement(RankedRewardKind.RankedRewardTierBadge) == true
                || _profile?.HasEntitlement(RankedRewardKind.RankedRewardTierTitle) == true;
            var curTier = _profile?.CurrentProgress?.CurrentTier ?? RankedTier.Unspecified;
            var requiredTiers = new[] { RankedTier.Yunzhang, RankedTier.Tianshu, RankedTier.Wushuang };
            for (int i = 0; i < 3; i++)
            {
                if (_seasonHonorStatus[i] == null) continue;
                bool reachedNow = _profile?.CurrentProgress?.Qualified == true && curTier >= requiredTiers[i];
                _seasonHonorStatus[i].text = hasTierHonor && reachedNow
                    ? "已获段位荣誉"
                    : reachedNow ? "当前段位已达标" : "封榜达标解锁";
                _seasonHonorStatus[i].color = reachedNow ? JadeAccent : InkMuted;
            }
        }

        private void UpdateCosmeticRow(int idx, bool owned, bool active, bool saving, string ownedLabel, string lockedLabel)
        {
            var row = _cosmeticRows[idx];
            row.RowBg.sprite = LoadGuildApproved(owned ? "guild_row_self" : "guild_row_normal");
            row.ActionBg.sprite = LoadGuildApproved(active ? "guild_button_primary" : "guild_button_secondary");
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
            else if (idx == 3)
            {
                var ent = _profile.FindEntitlement(RankedRewardKind.RankedRewardWinMilestone)
                    ?? _profile.FindEntitlement(RankedRewardKind.RankedRewardTierTitle);
                if (ent == null) return;
                next.EquippedTitleCode = string.IsNullOrEmpty(next.EquippedTitleCode) ? ent.ItemCode : string.Empty;
            }
            CosmeticUpdateRequested?.Invoke(next);
        }

        private void BuildFeaturedBuildsTab()
        {
            var body = QdaoUguiFactory.CreateRect("TabFeaturedBuilds", _root, 0f, 0f, DesignWidth, DesignHeight);
            _tabBodies[3] = body.gameObject;

            _featuredStatusText = CreateLabel("FeaturedStatus", body, 954f, 416f, 778f, 28f,
                DefaultFeaturedStatus, 16f, BronzeHint);
            _featuredStatusText.enableAutoSizing = true;
            _featuredStatusText.fontSizeMin = 12f;
            _featuredStatusText.fontSizeMax = 16f;

            // V7 擂台直播横幅（760×76，位于问道名局顶部，预绑 RankedPlayV7/live_ranked_spectate_banner + icon_live_spectate）
            var liveBannerSprite = SeasonEconomyUiArt.LoadRankedPlayV7Asset("live_ranked_spectate_banner");
            var liveIconSprite = SeasonEconomyUiArt.LoadRankedPlayV7Asset("icon_live_spectate");
            var liveBg = QdaoUguiFactory.CreateImage("LiveRankedSpectateBanner", body, 963f, 448f, 760f, 76f,
                liveBannerSprite ?? LoadGuildApproved("guild_row_self"), false);
            liveBg.type = Image.Type.Simple;
            if (liveBg.sprite == null) liveBg.color = SeasonEconomyUiArt.BattleArenaJadeLight;

            float liveTextX = 20f;
            if (liveIconSprite != null)
            {
                var liveIcon = QdaoUguiFactory.CreateImage("LiveSpectateIcon", liveBg.transform, 16f, 20f, 36f, 36f, liveIconSprite, false);
                liveIcon.preserveAspect = true;
                liveTextX = 60f;
            }
            float liveTextW = 474f - liveTextX;
            _liveBannerTitle = CreateLabel("LiveBannerTitle", liveBg.transform, liveTextX, 10f, liveTextW, 26f,
                "擂台直播 · 高段排位实时围观", 17f, JadeDark, bold: true);
            _liveBannerTitle.enableAutoSizing = true;
            _liveBannerTitle.fontSizeMin = 13f;
            _liveBannerTitle.fontSizeMax = 17f;

            _liveBannerBody = CreateLabel("LiveBannerBody", liveBg.transform, liveTextX, 40f, liveTextW, 26f,
                "正在查询当前高段排位激战…", 15f, InkSecondary);
            _liveBannerBody.enableAutoSizing = true;
            _liveBannerBody.fontSizeMin = 12f;
            _liveBannerBody.fontSizeMax = 15f;

            _liveCycleBtn = CreateGuildButton("LiveCycleBtn", liveBg.transform, 480f, 19f, 78f, 38f,
                "guild_button_secondary", "换一场", 16f, JadeDark, out _, out _);
            _liveCycleBtn.onClick.AddListener(() =>
            {
                if (_liveRankedMatches.Count > 1)
                {
                    _liveRankedIndex = (_liveRankedIndex + 1) % _liveRankedMatches.Count;
                    RefreshLiveBannerView();
                }
            });
            _liveCycleBtn.gameObject.SetActive(false);

            _liveRefreshBtn = CreateGuildButton("LiveRefreshBtn", liveBg.transform, 564f, 19f, 68f, 38f,
                "guild_button_secondary", "刷新", 16f, JadeDark, out _, out _);
            _liveRefreshBtn.onClick.AddListener(() => RefreshLiveRankedRequested?.Invoke());

            _liveWatchBtn = CreateGuildButton("LiveWatchBtn", liveBg.transform, 638f, 19f, 106f, 38f,
                "guild_button_primary", "实时围观", 16f, ButtonGoldText, out _, out _);
            _liveWatchBtn.onClick.AddListener(() =>
            {
                if (_liveRankedMatches.Count == 0) return;
                int idx = Mathf.Clamp(_liveRankedIndex, 0, _liveRankedMatches.Count - 1);
                ulong bid = _liveRankedMatches[idx].BattleId;
                if (bid != 0UL) WatchLiveRankedRequested?.Invoke(bid);
            });
            _liveWatchBtn.interactable = false;

            _featuredEmptyText = CreateLabel("FeaturedEmpty", body, 974f, 630f, 738f, 120f,
                "本赛季暂未产生高段位公开名局样本，完成正式人宠会武对局后将自动汇总热门配招。",
                20f, InkMuted, TextAlignmentOptions.Center, wrap: true);

            var cardSprite = SeasonEconomyUiArt.LoadRankedPlayV6Asset("featured_build_card_base");
            var replayIconSprite = SeasonEconomyUiArt.LoadRankedPlayV6Asset("icon_featured_replay");
            var copyIconSprite = SeasonEconomyUiArt.LoadRankedPlayV6Asset("icon_copy_build");

            for (int i = 0; i < FeaturedCardsPerPage; i++)
            {
                float y = 532f + i * 126f;
                bool hasCardArt = cardSprite != null;
                var bg = QdaoUguiFactory.CreateImage("FeaturedBuildCard_" + i, body, 963f, y, 760f, 120f,
                    hasCardArt ? cardSprite : LoadGuildApproved("guild_row_normal"), false);
                bg.type = Image.Type.Simple;
                if (bg.sprite == null) bg.color = SeasonEconomyUiArt.BattleArenaPaper;

                // 严格对齐 featured_build_card_base.png 左列信息安全区（x=28..558）与右侧双按钮槽（x=591..741, y=15..53 / 67..105）
                float infoX = hasCardArt ? 28f : 18f;
                float infoW = hasCardArt ? 530f : 548f;
                var title = CreateLabel("FeaturedTitle_" + i, bg.transform, infoX, 10f, infoW, 30f, "", 19f, JadeDark, bold: true);
                title.enableAutoSizing = true;
                title.fontSizeMin = 13f;
                title.fontSizeMax = 19f;

                var loadout = CreateLabel("FeaturedLoadout_" + i, bg.transform, infoX, 45f, infoW, 30f, "", 17f, JadeAccent, bold: true);
                loadout.enableAutoSizing = true;
                loadout.fontSizeMin = 13f;
                loadout.fontSizeMax = 17f;

                var skills = CreateLabel("FeaturedSkills_" + i, bg.transform, infoX, 80f, infoW, 30f, "", 16f, InkSecondary);
                skills.enableAutoSizing = true;
                skills.fontSizeMin = 12f;
                skills.fontSizeMax = 16f;

                Button replayBtn;
                TMP_Text replayLbl;
                if (hasCardArt)
                {
                    // 卡片底图已在 [591, 15, 150, 38] 绘好上槽金边宣纸按钮，用透明点击层叠图标与字，杜绝双框重叠
                    replayBtn = QdaoUguiFactory.CreateHitButton("FeaturedReplayBtn_" + i, bg.transform, 591f, 15f, 150f, 38f);
                    replayLbl = CreateLabel("FeaturedReplayBtn_" + i + "_Label", replayBtn.transform, 8f, 0f, 134f, 38f,
                        "观摩名局", 18f, JadeDark, TextAlignmentOptions.Center, bold: true);
                    replayLbl.enableAutoSizing = true;
                    replayLbl.fontSizeMin = 13f;
                    replayLbl.fontSizeMax = 18f;
                }
                else
                {
                    replayBtn = CreateGuildButton("FeaturedReplayBtn_" + i, bg.transform, 591f, 15f, 150f, 38f,
                        "guild_button_secondary", "观摩名局", 18f, JadeDark, out _, out replayLbl);
                }
                AttachSmallButtonIcon(replayBtn.transform, replayLbl, "ReplayIcon_" + i, replayIconSprite, 150f, 38f);

                var copyBtn = CreateGuildButton("FeaturedCopyBtn_" + i, bg.transform, 591f, 67f, 150f, 38f,
                    "guild_button_primary", "一键套用", 18f, ButtonGoldText, out _, out var copyLbl);
                AttachSmallButtonIcon(copyBtn.transform, copyLbl, "CopyIcon_" + i, copyIconSprite, 150f, 38f);

                var row = new FeaturedBuildRowView
                {
                    Root = bg.gameObject,
                    Bg = bg,
                    TitleText = title,
                    LoadoutText = loadout,
                    SkillsText = skills,
                    ReplayBtn = replayBtn,
                    CopyBtn = copyBtn,
                };
                replayBtn.onClick.AddListener(() =>
                {
                    if (row.BoundBuild != null && row.BoundBuild.FeaturedBattleId != 0UL)
                        WatchFeaturedReplayRequested?.Invoke(row.BoundBuild.FeaturedBattleId);
                });
                copyBtn.onClick.AddListener(() =>
                {
                    if (row.BoundBuild != null)
                        ApplyFeaturedBuildRequested?.Invoke(row.BoundBuild);
                });
                bg.gameObject.SetActive(false);
                _featuredRows[i] = row;
            }

            // 热门流派分页栏（每页 3 张，完整覆盖 Top 5 流派，y=914..952）
            var pager = QdaoUguiFactory.CreateRect("FeaturedBuildsPager", body, 963f, 914f, 760f, 38f);
            _featuredPagerRoot = pager.gameObject;
            _featuredPrevBtn = CreateGuildButton("FeaturedPrevPage", pager, 0f, 0f, 112f, 36f,
                "guild_button_secondary", "上一页", 16f, JadeDark, out _, out _);
            _featuredPrevBtn.onClick.AddListener(() =>
            {
                if (_featuredPageIndex > 0) { _featuredPageIndex--; RenderFeaturedBuilds(); }
            });
            _featuredPageText = CreateLabel("FeaturedPageText", pager, 120f, 0f, 520f, 36f,
                "第 1 / 1 页", 17f, InkMuted, TextAlignmentOptions.Center);
            _featuredNextBtn = CreateGuildButton("FeaturedNextPage", pager, 648f, 0f, 112f, 36f,
                "guild_button_secondary", "下一页", 16f, JadeDark, out _, out _);
            _featuredNextBtn.onClick.AddListener(() =>
            {
                int total = _profile?.FeaturedBuilds?.Count ?? 0;
                int maxPage = Math.Max(0, (total - 1) / FeaturedCardsPerPage);
                if (_featuredPageIndex < maxPage) { _featuredPageIndex++; RenderFeaturedBuilds(); }
            });
            _featuredPagerRoot.SetActive(false);

            body.gameObject.SetActive(false);
        }

        public void RenderLiveRankedMatches(IReadOnlyList<BattleWatchSummary> liveMatches, bool loading = false)
        {
            if (loading)
            {
                if (_liveBannerTitle != null) _liveBannerTitle.text = "擂台直播 · 高段排位实时围观";
                if (_liveBannerBody != null) _liveBannerBody.text = "正在刷新当前高段排位激战…";
                if (_liveWatchBtn != null) _liveWatchBtn.interactable = false;
                if (_liveCycleBtn != null) _liveCycleBtn.gameObject.SetActive(false);
                return;
            }
            _liveRankedMatches.Clear();
            if (liveMatches != null)
            {
                for (int i = 0; i < liveMatches.Count; i++)
                {
                    if (liveMatches[i] != null && liveMatches[i].BattleId != 0UL)
                        _liveRankedMatches.Add(liveMatches[i]);
                }
            }
            if (_liveRankedIndex >= _liveRankedMatches.Count) _liveRankedIndex = 0;
            RefreshLiveBannerView();
        }

        private void RefreshLiveBannerView()
        {
            if (_liveBannerTitle == null || _liveBannerBody == null) return;
            if (_liveRankedMatches.Count == 0)
            {
                _liveBannerTitle.text = "擂台直播 · 高段排位实时围观";
                _liveBannerBody.text = "当前暂无高段排位激战，可稍后刷新或先观摩下方名局回放";
                if (_liveWatchBtn != null) _liveWatchBtn.interactable = false;
                if (_liveCycleBtn != null) _liveCycleBtn.gameObject.SetActive(false);
                return;
            }
            int idx = Mathf.Clamp(_liveRankedIndex, 0, _liveRankedMatches.Count - 1);
            var current = _liveRankedMatches[idx];
            _liveBannerTitle.text = _liveRankedMatches.Count > 1
                ? $"擂台直播 · 正在激战（{idx + 1}/{_liveRankedMatches.Count}）"
                : "擂台直播 · 正在激战";
            _liveBannerBody.text = RankedFormatting.FormatLiveRankedMatchSummary(current);
            if (_liveWatchBtn != null) _liveWatchBtn.interactable = true;
            if (_liveCycleBtn != null) _liveCycleBtn.gameObject.SetActive(_liveRankedMatches.Count > 1);
        }

        private static void AttachSmallButtonIcon(Transform btnTransform, TMP_Text label, string iconName,
            Sprite iconSprite, float btnWidth, float btnHeight)
        {
            if (iconSprite == null) return;
            const float iconSize = 24f;
            float iconY = Mathf.Max(0f, (btnHeight - iconSize) * 0.5f);
            var icon = QdaoUguiFactory.CreateImage(iconName, btnTransform, 10f, iconY, iconSize, iconSize, iconSprite, false);
            icon.preserveAspect = true;
            if (label != null && label.rectTransform != null)
            {
                var rt = label.rectTransform;
                rt.anchoredPosition = new Vector2(36f, 0f);
                rt.sizeDelta = new Vector2(Mathf.Max(60f, btnWidth - 44f), btnHeight);
            }
        }

        private void RenderFeaturedBuilds()
        {
            var builds = _profile?.FeaturedBuilds;
            int total = builds != null ? builds.Count : 0;
            if (_featuredEmptyText != null) _featuredEmptyText.gameObject.SetActive(total == 0);

            int pageCount = total > 0 ? ((total + FeaturedCardsPerPage - 1) / FeaturedCardsPerPage) : 1;
            if (_featuredPageIndex >= pageCount) _featuredPageIndex = Math.Max(0, pageCount - 1);
            int startIdx = _featuredPageIndex * FeaturedCardsPerPage;

            if (_featuredPagerRoot != null)
            {
                _featuredPagerRoot.SetActive(total > FeaturedCardsPerPage);
                if (_featuredPageText != null) _featuredPageText.text = $"第 {_featuredPageIndex + 1} / {pageCount} 页（共 {total} 套热门流派）";
                if (_featuredPrevBtn != null) _featuredPrevBtn.interactable = _featuredPageIndex > 0;
                if (_featuredNextBtn != null) _featuredNextBtn.interactable = _featuredPageIndex + 1 < pageCount;
            }

            for (int i = 0; i < FeaturedCardsPerPage; i++)
            {
                var row = _featuredRows[i];
                if (row == null) continue;
                int dataIdx = startIdx + i;
                if (dataIdx >= total || builds[dataIdx] == null)
                {
                    row.BoundBuild = null;
                    row.Root.SetActive(false);
                    continue;
                }

                var b = builds[dataIdx];
                row.BoundBuild = b;
                row.Root.SetActive(true);

                string playerLabel = !string.IsNullOrEmpty(b.FeaturedPlayerName)
                    ? b.FeaturedPlayerName
                    : (b.FeaturedPlayerId != 0UL ? $"道友{b.FeaturedPlayerId}" : "高段名家");
                string scaleTag = b.FeaturedTeamSize >= 1u && b.FeaturedTeamSize <= 5u
                    ? $"{b.FeaturedTeamSize}V{b.FeaturedTeamSize}"
                    : "论剑";
                string winRate = RankedFormatting.FormatWinRatePercent(b.Wins, b.Appearances);
                row.TitleText.text = $"#{dataIdx + 1} 胜率 {winRate}（{b.Wins}胜/{b.Appearances}场）· {RankedFormatting.FormatRating(b.FeaturedRatingCenti)}分 {scaleTag} · {playerLabel}";

                string sealName = b.SeasonSealId != 0u
                    ? (SeasonSealTableManager.Instance?.FindById(b.SeasonSealId)?.Name ?? $"赛季印#{b.SeasonSealId}")
                    : "未配印";
                string petName = b.StarterPetTableId != 0u
                    ? (PetTableManager.Instance?.FindById(b.StarterPetTableId)?.Name ?? $"灵宠#{b.StarterPetTableId}")
                    : "未出战宠";
                row.LoadoutText.text = $"赛季印：{sealName}   ·   首发灵宠：{petName}";

                if (b.CoreSkillIds.Count > 0)
                {
                    var skillNames = new List<string>(b.CoreSkillIds.Count);
                    for (int s = 0; s < b.CoreSkillIds.Count && s < 4; s++)
                    {
                        uint sid = b.CoreSkillIds[s];
                        if (sid == 0u) continue;
                        string sn = SkillTableManager.Instance?.FindById(sid)?.Name;
                        skillNames.Add(string.IsNullOrWhiteSpace(sn) ? $"技能{sid}" : sn);
                    }
                    row.SkillsText.text = skillNames.Count > 0
                        ? "核心招式：" + string.Join(" / ", skillNames)
                        : "核心招式：默认流派";
                }
                else
                {
                    row.SkillsText.text = "核心招式：默认流派";
                }

                row.ReplayBtn.interactable = b.FeaturedBattleId != 0UL;
                row.CopyBtn.interactable = b.SeasonSealId != 0u || b.StarterPetTableId != 0u;
            }
        }

        private static string FormatOpenTeamSizes(IReadOnlyList<uint> sizes)
        {
            if (sizes == null || sizes.Count == 0) return "1V1 / 3V3";
            var labels = new List<string>(sizes.Count);
            for (int i = 0; i < sizes.Count; i++) labels.Add($"{sizes[i]}V{sizes[i]}");
            return string.Join(" / ", labels);
        }

        private static string FormatSeasonWindow(ulong startMs, ulong endMs)
        {
            if (startMs == 0 || endMs == 0) return string.Empty;
            var start = DateTimeOffset.FromUnixTimeMilliseconds((long)startMs).ToLocalTime();
            var end = DateTimeOffset.FromUnixTimeMilliseconds((long)endMs).ToLocalTime();
            return $"{start:yyyy-MM-dd} ~ {end:MM-dd}";
        }
    }
}
