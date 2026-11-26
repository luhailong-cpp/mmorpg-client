using MmorpgClient.Game;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Season;
using MmorpgClient.UI.Ugui.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>
    /// 「破阵之季 · 赛季印」单槽赛季面板与试炼志页签（对齐 designs/season-ui-v1 三态效果图）。
    /// 全部数值、报价与可操作态严格取自服务端 <see cref="global::SeasonStateSnapshot"/>。
    /// </summary>
    public sealed class SeasonSealPanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private GameClient _game;
        private int _activeTab; // 0=赛季印, 1=五行阵法, 2=试炼志, 3=十季荣誉墙
        private bool _skillsExpanded;
        private SeasonCatchupQuote? _pendingCatchupQuote;
        private int _milestonePage;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _root = root;
            _game = game;
            Refresh();
            Hide();
        }

        public void Show()
        {
            _activeTab = 0;
            _skillsExpanded = false;
            _pendingCatchupQuote = null;
            gameObject.SetActive(true);
            _game?.Season?.RequestState();
            Refresh();
        }

        public void ShowHonorsWall()
        {
            _activeTab = 3;
            _skillsExpanded = false;
            _pendingCatchupQuote = null;
            gameObject.SetActive(true);
            _game?.Season?.RequestState();
            Refresh();
        }

        public void ShowFormations()
        {
            _activeTab = 1;
            _skillsExpanded = false;
            _pendingCatchupQuote = null;
            gameObject.SetActive(true);
            _game?.Season?.RequestState();
            Refresh();
        }

        public void ShowTrialJournal()
        {
            _activeTab = 2;
            _pendingCatchupQuote = null;
            gameObject.SetActive(true);
            _game?.TrialJournal?.RequestSnapshot();
            Refresh();
        }

        public void Hide()
        {
            _pendingCatchupQuote = null;
            gameObject.SetActive(false);
        }

        public void Refresh()
        {
            if (_root == null || _game == null) return;
            var season = _game.Season;
            var snapshot = season?.Snapshot;
            ValidatePendingQuote(snapshot);

            CoreLoopUiArt.Clear(transform);
            string frameTitle = _activeTab switch
            {
                1 => "五行阵法 · 人宠相生",
                3 => "十季荣誉墙 · 往季追忆",
                0 when snapshot != null && snapshot.Available && !string.IsNullOrWhiteSpace(snapshot.SeasonName)
                    => $"{snapshot.SeasonName} · 赛季印",
                _ => "赛季印 · 试炼志",
            };
            CoreLoopUiArt.Frame(transform, frameTitle, _root.CloseCurrent);

            CoreLoopUiArt.Button("SeasonTab", transform, 295f, 246f, 150f, 48f,
                "赛季印", Show, true, _activeTab == 0, 21f, _activeTab == 0 ? "tab_active" : "tab_normal");
            CoreLoopUiArt.Button("HonorsTab", transform, 453f, 246f, 150f, 48f,
                "荣誉墙", ShowHonorsWall, true, _activeTab == 3, 21f, _activeTab == 3 ? "tab_active" : "tab_normal");
            CoreLoopUiArt.Button("FormationTab", transform, 611f, 246f, 155f, 48f,
                "五行阵法", ShowFormations, true, _activeTab == 1, 21f, _activeTab == 1 ? "tab_active" : "tab_normal");
            CoreLoopUiArt.Button("JournalTab", transform, 774f, 246f, 146f, 48f,
                "试炼志", () => _root.OpenTrialJournal(), true, _activeTab == 2, 21f, _activeTab == 2 ? "tab_active" : "tab_normal");

            if (_activeTab == 1)
            {
                DrawWuxingFormationsView(season);
                return;
            }
            if (_activeTab == 2)
            {
                DrawTrialJournal();
                return;
            }

            if (snapshot == null)
            {
                CoreLoopUiArt.Text("Loading", transform, 400f, 420f, 1760f, 120f,
                    season?.StatePending == true ? "正在同步赛季印数据…" : (season?.Status ?? "正在同步赛季印…"),
                    34f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
                CoreLoopUiArt.Button("Retry", transform, 1070f, 575f, 420f, 68f,
                    "刷新赛季资料", () => season?.RequestState(true), season?.StatePending != true, true, 26f);
                return;
            }

            bool busy = season.ActionPending || season.RequiresReconnect;
            if (_activeTab == 3)
            {
                DrawHonorsWall(season, snapshot, busy);
                return;
            }

            if (!snapshot.Available)
            {
                CoreLoopUiArt.Text("Unavailable", transform, 400f, 340f, 1760f, 80f,
                    string.IsNullOrWhiteSpace(snapshot.UnavailableReason) ? "当前暂无进行中的赛季" : snapshot.UnavailableReason,
                    34f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
                if (snapshot.Honors.Count > 0)
                    DrawHonorsStrip(snapshot, 434f);
                CoreLoopUiArt.Button("Retry", transform, 1070f, snapshot.Honors.Count > 0 ? 730f : 520f, 420f, 68f,
                    "刷新赛季资料", () => season?.RequestState(true), season?.StatePending != true, true, 26f);
                return;
            }

            DrawSeasonHeaderPill(snapshot);
            DrawProgressAndLightStats(snapshot);

            bool maxed = snapshot.MaxPoints > 0u && snapshot.ProgressPoints >= snapshot.MaxPoints;
            if (maxed)
                DrawMaxedBody(season, snapshot, busy);
            else
                DrawStandardBody(season, snapshot, busy);

            if (!string.IsNullOrEmpty(season.Status))
            {
                CoreLoopUiArt.Text("SeasonStatus", transform, 300f, 946f, 1960f, 34f,
                    season.Status, 21f, season.StatusIsError ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade,
                    false, false, TextAlignmentOptions.MidlineLeft);
            }

            if (_pendingCatchupQuote.HasValue)
                DrawCatchupConfirmModal(season, snapshot, _pendingCatchupQuote.Value, busy);
        }

        private void DrawSeasonHeaderPill(global::SeasonStateSnapshot snapshot)
        {
            string timeLabel = SeasonModels.FormatSeasonTimeLabel(snapshot.EndsAtMs, snapshot.ServerNowMs);
            var banner = SeasonV1UiArt.CreateHorizontalSlice(transform, "SeasonBanner",
                936f, 240f, 924f, 60f, "season_banner", CoreLoopUiArt.DeepJade);
            var bannerText = CoreLoopUiArt.Text("SeasonBannerTitle", banner.transform,
                28f, 0f, 868f, 60f, SeasonModels.FormatSeasonTermBanner(snapshot), 26f, SeasonV1UiArt.BadgeIvoryText,
                true, false, TextAlignmentOptions.Center);
            bannerText.enableAutoSizing = true;
            bannerText.fontSizeMin = 17f;
            bannerText.fontSizeMax = 26f;
            bannerText.raycastTarget = false;

            SeasonV1UiArt.CreateBadgePill(transform, "SeasonTimePill",
                1876f, 244f, 260f, 52f, "season_badge_free", timeLabel, 22f,
                new Color(0.95f, 0.91f, 0.78f, 0.96f), CoreLoopUiArt.DeepJade);
        }

        private void DrawProgressAndLightStats(global::SeasonStateSnapshot snapshot)
        {
            const float y = 314f;
            const float h = 128f;

            // 左侧：单槽赛季印徽章 + 进度条（6 / 12 + 战斗/追赶拆分）
            CoreLoopUiArt.Image("ProgressPlate", transform, 295f, y, 560f, h,
                "hud-strip", CoreLoopUiArt.DeepJade);
            var progressBg = QdaoUguiFactory.CreateImage("ProgressJadeTint", transform, 299f, y + 4f, 552f, h - 8f, null);
            progressBg.color = new Color(0.11f, 0.35f, 0.27f, 0.94f);
            progressBg.raycastTarget = false;

            var equipped = SeasonModels.FindEquippedSeal(snapshot);
            SeasonV1UiArt.CreateFixedIcon(transform, "EquippedSlotFrame", 316f, y + 14f, 100f, 100f,
                SeasonV1UiArt.LoadControl("season_seal_slot_frame"), CoreLoopUiArt.Gold);
            SeasonV1UiArt.CreateFixedIcon(transform, "EquippedSlotIcon", 328f, y + 26f, 76f, 76f,
                SeasonV1UiArt.LoadSealIcon(equipped?.SealId ?? snapshot.EquippedSealId, equipped?.IconKey),
                CoreLoopUiArt.Ivory);

            CoreLoopUiArt.Text("ProgressFraction", transform, 434f, y + 10f, 396f, 44f,
                $"{snapshot.ProgressPoints} / {snapshot.MaxPoints}", 34f, SeasonV1UiArt.BadgeIvoryText,
                true, false, TextAlignmentOptions.Center);

            float ratio = SeasonEconomyUiArt.ComputeProgressRatio(snapshot.ProgressPoints, snapshot.MaxPoints);
            var track = QdaoUguiFactory.CreateImage("ProgressTrack", transform, 442f, y + 60f, 380f, 22f, null);
            track.color = new Color(0.08f, 0.20f, 0.16f, 0.92f);
            track.raycastTarget = false;
            var fill = QdaoUguiFactory.CreateImage("ProgressFill", transform, 445f, y + 63f,
                Mathf.Max(0f, 374f * ratio), 16f, null);
            fill.color = new Color(0.30f, 0.80f, 0.56f, 1f);
            fill.raycastTarget = false;

            CoreLoopUiArt.Text("ProgressBreakdown", transform, 434f, y + 88f, 396f, 30f,
                SeasonModels.DescribeProgressBreakdown(snapshot), 20f, SeasonV1UiArt.BadgeIvoryText,
                false, false, TextAlignmentOptions.Center);

            // 右侧：轻属性区（只展示气血与防御，明文注明不含速度与控制）
            CoreLoopUiArt.Card(transform, "LightStatsCard", 872f, y, 1393f, h);
            string hpLine = $"气血加成  +{SeasonModels.FormatBpsOneDecimal(snapshot.CurrentMaxHealthBps)}（满级 +{SeasonModels.FormatBpsOneDecimal(snapshot.CapMaxHealthBps)}）";
            string defLine = $"防御加成  +{SeasonModels.FormatBpsOneDecimal(snapshot.CurrentDefenseBps)}（满级 +{SeasonModels.FormatBpsOneDecimal(snapshot.CapDefenseBps)}）";
            CoreLoopUiArt.Text("HealthBonus", transform, 902f, y + 12f, 650f, 42f,
                hpLine, 27f, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Text("DefenseBonus", transform, 1582f, y + 12f, 655f, 42f,
                defLine, 27f, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Text("NoSpeedNote", transform, 902f, y + 56f, 1335f, 28f,
                "──  不含速度、暴击与控制属性  ──", 20f, CoreLoopUiArt.Muted,
                false, false, TextAlignmentOptions.Center);
            CoreLoopUiArt.Text("SourceRuleLine", transform, 902f, y + 88f, 1335f, 30f,
                BuildRuleAndHonorLine(snapshot), 20f, CoreLoopUiArt.Warning,
                false, false, TextAlignmentOptions.Center);
        }

        private string BuildRuleAndHonorLine(global::SeasonStateSnapshot snapshot)
        {
            string baseRule = SeasonModels.DescribeSourceRuleLine(snapshot);
            var currentHonor = SeasonModels.FindCurrentHonor(snapshot);
            if (currentHonor == null || string.IsNullOrWhiteSpace(currentHonor.HonorTitleName))
                return baseRule;
            int unlockedTotal = SeasonModels.CountUnlockedHonors(snapshot);
            string honorTag = currentHonor.Unlocked
                ? $"永久称谓「{currentHonor.HonorTitleName}」已解锁（零数值 · 累计 {unlockedTotal} 枚）"
                : $"满级赠永久零数值称谓「{currentHonor.HonorTitleName}」";
            return $"{baseRule}  |  {honorTag}";
        }

        private void DrawWeeklyAffixAndPresetsBar(SeasonClient season, global::SeasonStateSnapshot snapshot, bool busy, float y)
        {
            // 左侧：本周全员对称环境词缀条；右侧：3 套本季一键推荐流派（免费切印 + 搭配技能）
            var affix = snapshot.WeeklyAffix;
            bool hasAffix = affix != null && affix.AffixId != 0u;
            CoreLoopUiArt.Card(transform, "WeeklyAffixCard", 295f, y, 760f, 56f);
            if (hasAffix)
            {
                string title = $"第 {affix.WeekIndex} 周 · {affix.Name}";
                string desc = !string.IsNullOrWhiteSpace(affix.Description)
                    ? affix.Description
                    : SeasonModels.DescribeWeeklyAffixDetail(affix.AffixId);
                CoreLoopUiArt.Text("WeeklyAffixTitle", transform, 312f, y + 4f, 728f, 24f,
                    title, 19f, CoreLoopUiArt.DeepJade, true);
                var descLbl = CoreLoopUiArt.Text("WeeklyAffixDesc", transform, 312f, y + 28f, 728f, 24f,
                    desc, 16f, CoreLoopUiArt.Ink);
                descLbl.enableAutoSizing = true; descLbl.fontSizeMin = 12f; descLbl.fontSizeMax = 16f;
            }
            else
            {
                CoreLoopUiArt.Text("WeeklyAffixEmpty", transform, 312f, y + 14f, 728f, 28f,
                    "本周全员对称环境：常规平衡对局", 18f, CoreLoopUiArt.Muted);
            }

            var presets = SeasonModels.BuildRecommendedLoadouts(snapshot);
            float startX = 1070f;
            float gap = 12f;
            int count = Mathf.Min(presets.Count, 3);
            if (count == 0) return;
            float cardW = (1195f - gap * (count - 1)) / count;
            for (int i = 0; i < count; ++i)
            {
                var p = presets[i];
                float x = startX + i * (cardW + gap);
                CoreLoopUiArt.Card(transform, "PresetCard_" + p.PresetKey, x, y, cardW, 56f, selected: p.RecommendedThisWeek);
                string header = p.RecommendedThisWeek ? $"{p.Title} ★本周" : p.Title;
                var hLbl = CoreLoopUiArt.Text("PresetTitle_" + p.PresetKey, transform, x + 10f, y + 4f, cardW - 108f, 24f,
                    header, 17f, p.RecommendedThisWeek ? SeasonEconomyUiArt.GoldHighlight : CoreLoopUiArt.DeepJade, true);
                hLbl.enableAutoSizing = true; hLbl.fontSizeMin = 12f; hLbl.fontSizeMax = 17f;

                var sLbl = CoreLoopUiArt.Text("PresetSkills_" + p.PresetKey, transform, x + 10f, y + 29f, cardW - 108f, 22f,
                    $"{p.SealName} · {p.PairedSkillsSummary}", 14f, CoreLoopUiArt.Muted);
                sLbl.enableAutoSizing = true; sLbl.fontSizeMin = 11f; sLbl.fontSizeMax = 14f;

                uint sealId = p.SealId;
                bool canApply = !busy && snapshot.CanSwitchSeal && !p.IsCurrentlyEquipped;
                CoreLoopUiArt.Button("ApplyPreset_" + p.PresetKey, transform, x + cardW - 94f, y + 10f, 84f, 36f,
                    p.IsCurrentlyEquipped ? "已配" : "一键配",
                    () => season.EquipSeal(sealId), canApply, p.RecommendedThisWeek && !p.IsCurrentlyEquipped, 15f);
            }
        }

        private void DrawHonorsStrip(global::SeasonStateSnapshot snapshot, float y)
        {
            int count = Mathf.Min(snapshot.Honors.Count, 10);
            int rows = count > 5 ? 2 : 1;
            float panelH = 48f + rows * 108f;
            CoreLoopUiArt.Card(transform, "SeasonHonorsBar", 295f, y, 1970f, panelH);
            CoreLoopUiArt.Text("SeasonHonorsTitle", transform, 320f, y + 10f, 1920f, 30f,
                $"历季永久纪念印记与称谓（已解锁 {SeasonModels.CountUnlockedHonors(snapshot)}/{snapshot.Honors.Count} · 零战斗属性 · 永不收回）",
                22f, CoreLoopUiArt.DeepJade, true);
            for (int i = 0; i < count; ++i)
            {
                var h = snapshot.Honors[i];
                if (h == null) continue;
                int col = i % 5;
                int row = i / 5;
                float x = 315f + col * 388f;
                float iy = y + 48f + row * 104f;
                var badgeImg = SeasonV1UiArt.CreateFixedIcon(transform, "HonorBadge_" + h.SeasonId,
                    x, iy + 8f, 64f, 64f,
                    SeasonV1UiArt.LoadHonorBadgeIcon(h.SeasonId, h.HonorBadgeIconKey),
                    h.Unlocked ? CoreLoopUiArt.Gold : CoreLoopUiArt.Muted);
                if (!h.Unlocked) badgeImg.color = new Color(0.72f, 0.72f, 0.72f, 0.52f);
                string statusText = h.Unlocked
                    ? $"「{h.HonorTitleName}」已解锁"
                    : $"进度 {h.ProgressPoints}/{h.MaxPoints}";
                var nameLbl = CoreLoopUiArt.Text("HonorName_" + h.SeasonId, transform, x + 74f, iy + 10f, 300f, 28f,
                    $"S{h.SeasonId} · {h.SeasonName}", 19f, CoreLoopUiArt.Ink, true);
                nameLbl.enableAutoSizing = true; nameLbl.fontSizeMin = 13f; nameLbl.fontSizeMax = 19f;
                var stLbl = CoreLoopUiArt.Text("HonorStatus_" + h.SeasonId, transform, x + 74f, iy + 40f, 300f, 26f,
                    statusText, 17f, h.Unlocked ? SeasonEconomyUiArt.GoldHighlight : CoreLoopUiArt.Muted);
                stLbl.enableAutoSizing = true; stLbl.fontSizeMin = 12f; stLbl.fontSizeMax = 17f;
            }
        }

        private void DrawHonorsWall(SeasonClient season, global::SeasonStateSnapshot snapshot, bool busy)
        {
            int unlockedTotal = SeasonModels.CountUnlockedHonors(snapshot);
            int total = snapshot.Honors.Count;
            global::SeasonHonorView retroTarget = null;
            for (int i = 0; i < snapshot.Honors.Count; ++i)
            {
                var h = snapshot.Honors[i];
                if (h != null && (h.IsRetroTarget || (snapshot.RetroHonorTargetSeasonId != 0u && h.SeasonId == snapshot.RetroHonorTargetSeasonId)))
                {
                    retroTarget = h;
                    break;
                }
            }

            CoreLoopUiArt.Card(transform, "HonorsWallSummary", 295f, 314f, 1970f, 76f);
            CoreLoopUiArt.Text("HonorsWallHeadline", transform, 320f, 322f, 1620f, 30f,
                $"已永久解锁 {unlockedTotal}/{total} 枚纪念徽章与称谓（纯荣誉零战斗属性 · 跨轮次永不收回 · 全靠免费对局获取）",
                21f, CoreLoopUiArt.DeepJade, true);
            string retroSummary = retroTarget != null
                ? $"满级后免费追忆目标：S{retroTarget.SeasonId} {retroTarget.SeasonName}「{retroTarget.HonorTitleName}」（{retroTarget.ProgressPoints}/{retroTarget.MaxPoints} · {(snapshot.RetroHonorTargetExplicit ? "已手动指定" : "自动顺延")}）"
                : (total > 0 && unlockedTotal >= total
                    ? "全部十季永久纪念徽章均已集齐！"
                    : "本季进度满 12/12 后，继续排位或首领试炼将自动免费累计未解锁往季徽章");
            var subLbl = CoreLoopUiArt.Text("HonorsWallRetroLine", transform, 320f, 354f, 1620f, 28f,
                retroSummary, 18f, retroTarget != null ? SeasonEconomyUiArt.GoldHighlight : CoreLoopUiArt.Muted,
                retroTarget != null);
            subLbl.enableAutoSizing = true; subLbl.fontSizeMin = 13f; subLbl.fontSizeMax = 18f;

            CoreLoopUiArt.Button("ResetRetroHonorAuto", transform, 2006f, 327f, 236f, 50f,
                "恢复自动顺延", () => season.SetRetroHonorTarget(0u),
                !busy && snapshot.CanSwitchSeal && snapshot.RetroHonorTargetExplicit, false, 20f);

            const float cardW = 382f;
            const float cardH = 248f;
            const float gapX = 15f;
            const float gapY = 16f;
            int count = Mathf.Min(snapshot.Honors.Count, 10);
            for (int i = 0; i < count; ++i)
            {
                var h = snapshot.Honors[i];
                if (h == null) continue;
                int col = i % 5;
                int row = i / 5;
                float cx = 295f + col * (cardW + gapX);
                float cy = 404f + row * (cardH + gapY);
                bool isRetro = h.IsRetroTarget || (snapshot.RetroHonorTargetSeasonId != 0u && h.SeasonId == snapshot.RetroHonorTargetSeasonId);
                CoreLoopUiArt.Card(transform, "HonorWallCard_" + h.SeasonId, cx, cy, cardW, cardH,
                    selected: h.Unlocked || isRetro);

                var badgeImg = SeasonV1UiArt.CreateFixedIcon(transform, "HonorWallBadge_" + h.SeasonId,
                    cx + 16f, cy + 16f, 80f, 80f,
                    SeasonV1UiArt.LoadHonorBadgeIcon(h.SeasonId, h.HonorBadgeIconKey),
                    h.Unlocked ? CoreLoopUiArt.Gold : CoreLoopUiArt.Muted);
                if (!h.Unlocked) badgeImg.color = new Color(0.72f, 0.72f, 0.72f, 0.52f);

                var sName = CoreLoopUiArt.Text("HonorWallSeason_" + h.SeasonId, transform, cx + 108f, cy + 16f, 258f, 30f,
                    $"S{h.SeasonId} · {h.SeasonName}", 21f, CoreLoopUiArt.DeepJade, true);
                sName.enableAutoSizing = true; sName.fontSizeMin = 15f; sName.fontSizeMax = 21f;

                var tName = CoreLoopUiArt.Text("HonorWallTitle_" + h.SeasonId, transform, cx + 108f, cy + 48f, 258f, 26f,
                    $"称谓「{h.HonorTitleName}」", 17f, h.Unlocked ? SeasonEconomyUiArt.GoldHighlight : CoreLoopUiArt.Ink, h.Unlocked);
                tName.enableAutoSizing = true; tName.fontSizeMin = 13f; tName.fontSizeMax = 17f;

                string tagText = h.Unlocked ? "永久已解锁"
                    : h.IsCurrentSeason ? "本季进行中"
                    : isRetro ? "当前追忆目标"
                    : "往季未解锁";
                Color tagColor = h.Unlocked ? SeasonEconomyUiArt.GoldHighlight
                    : (h.IsCurrentSeason || isRetro) ? CoreLoopUiArt.Jade
                    : CoreLoopUiArt.Muted;
                CoreLoopUiArt.Text("HonorWallTag_" + h.SeasonId, transform, cx + 108f, cy + 78f, 258f, 24f,
                    tagText, 16f, tagColor, true);

                uint maxPts = h.MaxPoints > 0u ? h.MaxPoints : 12u;
                uint curPts = h.Unlocked ? maxPts : System.Math.Min(h.ProgressPoints, maxPts);
                CoreLoopUiArt.Text("HonorWallProg_" + h.SeasonId, transform, cx + 16f, cy + 114f, 350f, 24f,
                    $"累计进度 {curPts} / {maxPts}", 16f, CoreLoopUiArt.Ink);
                CoreLoopUiArt.Image("HonorWallTrack_" + h.SeasonId, transform, cx + 16f, cy + 144f, 350f, 12f,
                    null, new Color(0.76f, 0.80f, 0.72f, 0.92f));
                float ratio = SeasonEconomyUiArt.ComputeProgressRatio(curPts, maxPts);
                if (ratio > 0f)
                {
                    CoreLoopUiArt.Image("HonorWallFill_" + h.SeasonId, transform, cx + 16f, cy + 144f, 350f * ratio, 12f,
                        null, h.Unlocked ? SeasonEconomyUiArt.GoldHighlight : CoreLoopUiArt.Jade);
                }

                if (h.Unlocked)
                {
                    SeasonV1UiArt.CreateBadgePill(transform, "HonorWallDone_" + h.SeasonId,
                        cx + 16f, cy + 178f, 350f, 48f, "season_badge_equipped", "永久已解锁（永不收回）", 18f,
                        CoreLoopUiArt.DeepJade, SeasonV1UiArt.BadgeIvoryText);
                }
                else if (h.IsCurrentSeason)
                {
                    SeasonV1UiArt.CreateBadgePill(transform, "HonorWallCur_" + h.SeasonId,
                        cx + 16f, cy + 178f, 350f, 48f, "season_badge_free", "本季免费累计", 18f,
                        new Color(0.84f, 0.92f, 0.85f, 0.96f), CoreLoopUiArt.DeepJade);
                }
                else if (isRetro)
                {
                    SeasonV1UiArt.CreateBadgePill(transform, "HonorWallActive_" + h.SeasonId,
                        cx + 16f, cy + 178f, 350f, 48f, "season_badge_equipped",
                        snapshot.RetroHonorTargetExplicit ? "追忆中（已指定）" : "追忆中（自动顺延）", 18f,
                        CoreLoopUiArt.Jade, SeasonV1UiArt.BadgeIvoryText);
                }
                else
                {
                    uint capturedSid = h.SeasonId;
                    CoreLoopUiArt.Button("SetRetroTarget_" + capturedSid, transform, cx + 16f, cy + 178f, 350f, 48f,
                        "设为追忆目标", () => season.SetRetroHonorTarget(capturedSid),
                        !busy && snapshot.CanSwitchSeal, true, 19f);
                }
            }

            if (!string.IsNullOrEmpty(season.Status))
            {
                CoreLoopUiArt.Text("HonorsWallStatus", transform, 300f, 938f, 1960f, 34f,
                    season.Status, 21f, season.StatusIsError ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade,
                    false, false, TextAlignmentOptions.MidlineLeft);
            }
        }

        private void DrawStandardBody(SeasonClient season, global::SeasonStateSnapshot snapshot, bool busy)
        {
            if (_skillsExpanded)
            {
                DrawExpandedSkillsView(snapshot);
                return;
            }

            // 本周全员对称词缀 + 一键推荐流派条
            DrawWeeklyAffixAndPresetsBar(season, snapshot, busy, 448f);

            // 中部：4 选 1 赛季印列表（每行左图标 + 名称 + 说明 + 右侧「当前佩戴 / 切换」）
            int count = Mathf.Min(snapshot.Seals.Count, 4);
            for (int i = 0; i < count; ++i)
            {
                var seal = snapshot.Seals[i];
                if (seal == null) continue;
                DrawSealRow(season, snapshot, seal, 295f, 512f + i * 68f, 1970f, 62f, busy, compact: false);
            }

            string switchHint = snapshot.CanSwitchSeal
                ? "战外免费自由切换"
                : (!string.IsNullOrWhiteSpace(snapshot.ActionUnavailableReason)
                    ? snapshot.ActionUnavailableReason
                    : "当前暂不可切换或追赶赛季印");
            CoreLoopUiArt.Text("SwitchHint", transform, 295f, 786f, 1970f, 28f,
                switchHint, 21f, snapshot.CanSwitchSeal ? CoreLoopUiArt.Muted : CoreLoopUiArt.Warning,
                false, false, TextAlignmentOptions.Center);

            // 底部轻追赶区：灵石余额 + 补 1 点 + 一键补满
            CoreLoopUiArt.Card(transform, "CatchupBar", 295f, 818f, 1970f, 66f);
            string currencyName = SeasonModels.CurrencyName(snapshot.CatchupCurrencyType);
            CoreLoopUiArt.Text("CurrencyBalance", transform, 324f, 830f, 620f, 42f,
                $"{currencyName}余额：{snapshot.CurrencyBalance:N0}", 26f, CoreLoopUiArt.Ink, true);

            if (snapshot.CatchupPricePerPoint > 0UL)
            {
                bool hasOneQuote = SeasonModels.TryBuildCatchupQuote(snapshot, 1u, out var oneQuote);
                bool hasFullQuote = SeasonModels.TryBuildCatchupQuote(snapshot, 0u, out var fullQuote);
                bool canOpenOne = !busy && hasOneQuote;
                bool canOpenFull = !busy && hasFullQuote;

                CoreLoopUiArt.Button("CatchupOne", transform, 1080f, 825f, 470f, 52f,
                    $"补 1 点（{snapshot.CatchupPricePerPoint} {currencyName}）",
                    () => OpenCatchupConfirm(oneQuote), canOpenOne, false, 23f);

                ulong fullPrice = hasFullQuote ? fullQuote.TotalPrice : snapshot.CatchupRemainingTotalPrice;
                uint rem = SeasonModels.RemainingCatchupPoints(snapshot);
                CoreLoopUiArt.Button("CatchupFull", transform, 1575f, 825f, 665f, 52f,
                    $"一键补满剩余 {rem} 点（{fullPrice} {currencyName}）",
                    () => OpenCatchupConfirm(fullQuote), canOpenFull, true, 23f);
            }
            else
            {
                CoreLoopUiArt.Text("CatchupDisabled", transform, 1080f, 830f, 1160f, 42f,
                    "本季仅通过正规排位与首领试炼免费累计进度", 23f, CoreLoopUiArt.Muted,
                    false, false, TextAlignmentOptions.MidlineRight);
            }

            // 最底：可折叠「本季免费技能」预览条
            DrawFreeSkillsToggleStrip(snapshot, 892f);
        }

        private void DrawMaxedBody(SeasonClient season, global::SeasonStateSnapshot snapshot, bool busy)
        {
            // 满级 12/12 态：顶条展示本周对称词缀 + 一键推荐流派；左 4 枚赛季印，右 2×2 本季免费技能预览
            DrawWeeklyAffixAndPresetsBar(season, snapshot, busy, 448f);

            int count = Mathf.Min(snapshot.Seals.Count, 4);
            for (int i = 0; i < count; ++i)
            {
                var seal = snapshot.Seals[i];
                if (seal == null) continue;
                DrawSealRow(season, snapshot, seal, 295f, 512f + i * 74f, 960f, 68f, busy, compact: true);
            }

            var previewHeader = SeasonV1UiArt.CreateHorizontalSlice(transform, "FreeSkillsHeader",
                1280f, 512f, 985f, 44f, "season_banner", CoreLoopUiArt.DeepJade);
            var previewTitle = CoreLoopUiArt.Text("FreeSkillsHeaderTitle", previewHeader.transform,
                20f, 0f, 945f, 44f, "本季免费技能预览", 23f, SeasonV1UiArt.BadgeIvoryText,
                true, false, TextAlignmentOptions.Center);
            previewTitle.raycastTarget = false;

            DrawSkillsGrid(snapshot, 1280f, 564f, 482f, 114f, 20f, 12f);

            var currentHonor = SeasonModels.FindCurrentHonor(snapshot);
            global::SeasonHonorView retroTarget = null;
            for (int i = 0; i < snapshot.Honors.Count; ++i)
            {
                var h = snapshot.Honors[i];
                if (h != null && !h.Unlocked && (h.IsRetroTarget || (snapshot.RetroHonorTargetSeasonId != 0u && h.SeasonId == snapshot.RetroHonorTargetSeasonId)))
                {
                    retroTarget = h;
                    break;
                }
            }
            string maxedMsg;
            if (retroTarget != null)
            {
                maxedMsg = $"本季已满 · 继续免费对局自动追忆「{retroTarget.SeasonName} · {retroTarget.HonorTitleName}」（{retroTarget.ProgressPoints}/{retroTarget.MaxPoints}）";
            }
            else if (currentHonor != null && currentHonor.Unlocked && !string.IsNullOrWhiteSpace(currentHonor.HonorTitleName))
            {
                maxedMsg = $"本季进度已满 · 已永久解锁零数值纪念称谓「{currentHonor.HonorTitleName}」，可随时免费切换赛季印";
            }
            else
            {
                maxedMsg = "本季进度已满，可随时免费切换赛季印";
            }
            CoreLoopUiArt.Card(transform, "MaxedNoticeBar", 295f, 814f, 1970f, 64f);
            if (currentHonor != null && currentHonor.Unlocked)
            {
                SeasonV1UiArt.CreateFixedIcon(transform, "MaxedHonorBadge", 315f, 822f, 48f, 48f,
                    SeasonV1UiArt.LoadHonorBadgeIcon(currentHonor.SeasonId, currentHonor.HonorBadgeIconKey),
                    CoreLoopUiArt.Gold);
            }
            var maxedLbl = CoreLoopUiArt.Text("MaxedNoticeText", transform, 375f, 824f, 1635f, 44f,
                maxedMsg, 23f, CoreLoopUiArt.DeepJade,
                true, false, TextAlignmentOptions.MidlineLeft);
            maxedLbl.enableAutoSizing = true;
            maxedLbl.fontSizeMin = 15f;
            maxedLbl.fontSizeMax = 23f;

            CoreLoopUiArt.Button("OpenHonorsWallFromMaxed", transform, 2028f, 821f, 220f, 50f,
                "十季荣誉墙", ShowHonorsWall, true, retroTarget != null, 20f);

            DrawMaxedFreeSkillsFooter(snapshot, 886f);
        }

        private void DrawSealRow(
            SeasonClient season,
            global::SeasonStateSnapshot snapshot,
            global::SeasonSealOption seal,
            float x,
            float y,
            float w,
            float h,
            bool busy,
            bool compact)
        {
            bool isEquipped = seal.SealId == snapshot.EquippedSealId || seal.Equipped;
            string cardSprite = isEquipped ? "season_seal_card_equipped" : "season_seal_card_normal";
            SeasonV1UiArt.CreateHorizontalSlice(transform, "SealCard_" + seal.SealId, x, y, w, h,
                cardSprite, isEquipped ? SeasonV1UiArt.EquippedCardTint : SeasonV1UiArt.NormalCardTint);

            float iconSize = h - 14f;
            SeasonV1UiArt.CreateFixedIcon(transform, "SealIcon_" + seal.SealId,
                x + 20f, y + 7f, iconSize, iconSize,
                SeasonV1UiArt.LoadSealIcon(seal.SealId, seal.IconKey), CoreLoopUiArt.Ivory);

            float nameX = x + 20f + iconSize + 16f;
            float nameW = compact ? 156f : 210f;
            CoreLoopUiArt.Text("SealName_" + seal.SealId, transform, nameX, y + 8f, nameW, h - 16f,
                seal.Name, compact ? 25f : 29f, CoreLoopUiArt.DeepJade, true);

            float descX = nameX + nameW + 14f;
            float rightW = compact ? 170f : 220f;
            float descW = Mathf.Max(140f, w - (descX - x) - rightW - 28f);
            string resonanceHint = SeasonModels.DescribeSealResonance(seal, snapshot.SeasonId);
            if (!string.IsNullOrEmpty(resonanceHint))
            {
                float innerH = h - 10f;
                float topH = innerH * 0.54f;
                float bottomH = innerH - topH;
                var descLbl = CoreLoopUiArt.Text("SealDesc_" + seal.SealId, transform, descX, y + 5f, descW, topH,
                    seal.Description, compact ? 16f : 19f, CoreLoopUiArt.Ink, false, false);
                descLbl.enableAutoSizing = true; descLbl.fontSizeMin = 12f; descLbl.fontSizeMax = compact ? 16f : 19f;
                var resLbl = CoreLoopUiArt.Text("SealResonance_" + seal.SealId, transform, descX, y + 5f + topH, descW, bottomH,
                    resonanceHint, compact ? 14f : 17f, CoreLoopUiArt.DeepJade, true, false);
                resLbl.enableAutoSizing = true; resLbl.fontSizeMin = 11f; resLbl.fontSizeMax = compact ? 14f : 17f;
            }
            else
            {
                CoreLoopUiArt.Text("SealDesc_" + seal.SealId, transform, descX, y + 8f, descW, h - 16f,
                    seal.Description, compact ? 18f : 21f, CoreLoopUiArt.Ink, false, true);
            }

            float actionX = x + w - rightW - 18f;
            float actionY = y + (h - 48f) * 0.5f;
            if (isEquipped)
            {
                SeasonV1UiArt.CreateBadgePill(transform, "SealEquipped_" + seal.SealId,
                    actionX, actionY, rightW, 48f, "season_badge_equipped", "当前佩戴", 22f,
                    CoreLoopUiArt.DeepJade, SeasonV1UiArt.BadgeIvoryText);
            }
            else
            {
                uint capturedId = seal.SealId;
                bool canSwitch = !busy && snapshot.CanSwitchSeal;
                CoreLoopUiArt.Button("EquipSeal_" + capturedId, transform,
                    actionX, actionY, rightW, 48f, "切换",
                    () => season.EquipSeal(capturedId), canSwitch, false, 22f);
            }
        }

        private void DrawFreeSkillsToggleStrip(global::SeasonStateSnapshot snapshot, float y)
        {
            CoreLoopUiArt.Card(transform, "FreeSkillsBar", 295f, y, 1970f, 50f);
            var tag = SeasonV1UiArt.CreateHorizontalSlice(transform, "FreeSkillsTag",
                307f, y + 5f, 470f, 40f, "season_banner", CoreLoopUiArt.DeepJade);
            var tagText = CoreLoopUiArt.Text("FreeSkillsTagText", tag.transform,
                16f, 0f, 438f, 40f, $"本季 {snapshot.SeasonSkills.Count} 个免费技能（已自动生效）", 21f,
                SeasonV1UiArt.BadgeIvoryText, true, false, TextAlignmentOptions.Center);
            tagText.raycastTarget = false;

            CoreLoopUiArt.Text("FreeSkillsSummary", transform, 796f, y + 6f, 1140f, 38f,
                "全员免费自动生效 · 随等级成长 · 不花银两修习 · 季末随赛季轮换", 20f, CoreLoopUiArt.Ink);

            CoreLoopUiArt.Button("ToggleSkillsPreview", transform, 1966f, y + 6f, 286f, 38f,
                "展开技能预览 ▾", () => { _skillsExpanded = true; Refresh(); }, true, false, 20f);
        }

        private void DrawMaxedFreeSkillsFooter(global::SeasonStateSnapshot snapshot, float y)
        {
            CoreLoopUiArt.Card(transform, "MaxedSkillsFooter", 295f, y, 1970f, 52f);
            var tag = SeasonV1UiArt.CreateHorizontalSlice(transform, "MaxedSkillsTag",
                307f, y + 6f, 360f, 40f, "season_banner", CoreLoopUiArt.DeepJade);
            var tagText = CoreLoopUiArt.Text("MaxedSkillsTagText", tag.transform,
                14f, 0f, 332f, 40f, $"本季 {snapshot.SeasonSkills.Count} 个免费技能", 21f,
                SeasonV1UiArt.BadgeIvoryText, true, false, TextAlignmentOptions.Center);
            tagText.raycastTarget = false;

            CoreLoopUiArt.Text("MaxedSkillsBullets", transform, 688f, y + 7f, 1280f, 38f,
                "✓ 免费自动生效    ✓ 随等级成长    ✓ 无需银两修习    ✓ 季末轮换",
                21f, CoreLoopUiArt.DeepJade, true);

            CoreLoopUiArt.Button("OpenSkillBookFromSeason", transform, 1995f, y + 7f, 256f, 38f,
                "打开技能书", OpenSkillBook, true, false, 20f);
        }

        private void DrawExpandedSkillsView(global::SeasonStateSnapshot snapshot)
        {
            var header = SeasonV1UiArt.CreateHorizontalSlice(transform, "ExpandedSkillsHeader",
                295f, 456f, 1970f, 54f, "season_banner", CoreLoopUiArt.DeepJade);
            var title = CoreLoopUiArt.Text("ExpandedSkillsTitle", header.transform,
                24f, 0f, 1500f, 54f, $"本季免费限定技能（共 {snapshot.SeasonSkills.Count} 项 · 随角色等级自动成长）",
                25f, SeasonV1UiArt.BadgeIvoryText, true);
            title.raycastTarget = false;

            DrawSkillsGrid(snapshot, 295f, 524f, 972f, 152f, 26f, 18f);

            CoreLoopUiArt.Button("CollapseSkillsPreview", transform, 1650f, 868f, 290f, 54f,
                "返回赛季印 ▴", () => { _skillsExpanded = false; Refresh(); }, true, false, 23f);
            CoreLoopUiArt.Button("OpenSkillBookExpanded", transform, 1960f, 868f, 305f, 54f,
                "前往技能书查看", OpenSkillBook, true, true, 23f);
        }

        private void DrawSkillsGrid(
            global::SeasonStateSnapshot snapshot,
            float startX,
            float startY,
            float cardW,
            float cardH,
            float gapX,
            float gapY)
        {
            int count = Mathf.Min(snapshot.SeasonSkills.Count, 4);
            for (int i = 0; i < count; ++i)
            {
                var skill = snapshot.SeasonSkills[i];
                if (skill == null) continue;
                int col = i % 2;
                int row = i / 2;
                float x = startX + col * (cardW + gapX);
                float y = startY + row * (cardH + gapY);
                CoreLoopUiArt.Card(transform, "SeasonSkillCard_" + skill.SkillTableId, x, y, cardW, cardH);

                float iconSize = Mathf.Min(104f, cardH - 22f);
                var icon = SeasonV1UiArt.CreateFixedIcon(transform, "SeasonSkillIcon_" + skill.SkillTableId,
                    x + 14f, y + (cardH - iconSize) * 0.5f, iconSize, iconSize,
                    SeasonV1UiArt.LoadSkillIcon(skill.SkillTableId, skill.IconKey), CoreLoopUiArt.DeepJade);
                SeasonV1UiArt.CreateSeasonCornerBadge(icon.transform, "CornerBadge", iconSize - 26f, 2f, 24f);

                float textX = x + 14f + iconSize + 14f;
                float textW = cardW - (textX - x) - 14f;
                CoreLoopUiArt.Text("SeasonSkillName_" + skill.SkillTableId, transform,
                    textX, y + 10f, Mathf.Max(120f, textW - 148f), 34f,
                    skill.Name, 24f, CoreLoopUiArt.DeepJade, true);

                string badgeText = skill.Unlocked ? "本季免费" : $"{skill.UnlockLevel}级解锁";
                SeasonV1UiArt.CreateBadgePill(transform, "SeasonSkillFreeBadge_" + skill.SkillTableId,
                    x + cardW - 144f, y + 10f, 130f, 34f, "season_badge_free", badgeText, 17f,
                    CoreLoopUiArt.Jade, SeasonV1UiArt.BadgeIvoryText);

                string rankText = skill.Unlocked
                    ? $"已自动生效 · {skill.EffectiveRank}/{skill.MaxRank}重"
                    : $"未解锁（需 {skill.UnlockLevel} 级）";
                CoreLoopUiArt.Text("SeasonSkillRank_" + skill.SkillTableId, transform,
                    textX, y + 44f, textW, 24f, rankText, 17f, CoreLoopUiArt.Jade);

                CoreLoopUiArt.Text("SeasonSkillDesc_" + skill.SkillTableId, transform,
                    textX, y + 68f, textW, cardH - 76f,
                    skill.Description, 17f, CoreLoopUiArt.Muted, false, true);
            }
        }

        private void DrawCatchupConfirmModal(
            SeasonClient season,
            global::SeasonStateSnapshot snapshot,
            SeasonCatchupQuote quote,
            bool busy)
        {
            var shade = QdaoUguiFactory.CreateImage("CatchupModalShade", transform, 0f, 0f, 2560f, 1080f, null, true);
            shade.color = new Color(0.02f, 0.08f, 0.06f, 0.65f);

            const float mx = 770f;
            const float my = 250f;
            const float mw = 1020f;
            const float mh = 560f;
            CoreLoopUiArt.Card(transform, "CatchupModalFrame", mx, my, mw, mh, selected: true);

            var banner = SeasonV1UiArt.CreateHorizontalSlice(transform, "CatchupModalBanner",
                mx + 180f, my + 20f, 660f, 70f, "season_banner", CoreLoopUiArt.DeepJade);
            var title = CoreLoopUiArt.Text("CatchupModalTitle", banner.transform,
                20f, 0f, 620f, 70f, "灵石轻追赶", 32f, SeasonV1UiArt.BadgeIvoryText,
                true, false, TextAlignmentOptions.Center);
            title.raycastTarget = false;

            var closeBtn = QdaoUguiFactory.CreateArtButton("CatchupModalClose", transform,
                mx + mw - 78f, my + 18f, 60f, 60f, CoreLoopUiArt.Load("close"), out _);
            closeBtn.onClick.AddListener(() => { _pendingCatchupQuote = null; Refresh(); });

            string currencyName = SeasonModels.CurrencyName(quote.CurrencyType);
            bool affordable = snapshot.CurrencyBalance >= quote.TotalPrice;
            CoreLoopUiArt.Card(transform, "CatchupCostBox", mx + 120f, my + 120f, mw - 240f, 92f);
            CoreLoopUiArt.Text("CatchupCostText", transform, mx + 140f, my + 132f, mw - 280f, 68f,
                $"消耗  {quote.TotalPrice:N0} {currencyName}", 38f,
                affordable ? CoreLoopUiArt.DeepJade : CoreLoopUiArt.Warning, true, false, TextAlignmentOptions.Center);

            SeasonV1UiArt.CreateBadgePill(transform, "CatchupProgressArrow",
                mx + 250f, my + 236f, mw - 500f, 62f, "season_badge_free",
                $"{quote.ProgressBefore}/{quote.MaxPoints}  →  {quote.ProgressAfter}/{quote.MaxPoints}",
                29f, new Color(0.84f, 0.92f, 0.85f, 0.96f), CoreLoopUiArt.DeepJade);

            CoreLoopUiArt.Card(transform, "CatchupPromiseBox", mx + 90f, my + 322f, mw - 180f, 78f);
            CoreLoopUiArt.Text("CatchupPromiseText", transform, mx + 106f, my + 332f, mw - 212f, 58f,
                affordable
                    ? "付费追赶与免费玩法同封顶上限，满级后不可再买"
                    : $"{currencyName}余额不足（当前 {snapshot.CurrencyBalance:N0}，需要 {quote.TotalPrice:N0}）",
                23f, affordable ? CoreLoopUiArt.Ink : CoreLoopUiArt.Warning, false, true, TextAlignmentOptions.Center);

            CoreLoopUiArt.Button("CatchupConfirmCancel", transform, mx + 120f, my + 436f, 350f, 76f,
                "取消", () => { _pendingCatchupQuote = null; Refresh(); }, !busy, false, 28f);

            var capturedQuote = quote;
            CoreLoopUiArt.Button("CatchupConfirmSubmit", transform, mx + mw - 470f, my + 436f, 350f, 76f,
                "确认", () =>
                {
                    _pendingCatchupQuote = null;
                    season.CatchupSeal(capturedQuote.RequestPoints);
                }, !busy && affordable && snapshot.CanCatchup, true, 28f);
        }

        private void OpenCatchupConfirm(SeasonCatchupQuote quote)
        {
            _pendingCatchupQuote = quote;
            Refresh();
        }

        private void ValidatePendingQuote(global::SeasonStateSnapshot snapshot)
        {
            if (!_pendingCatchupQuote.HasValue) return;
            var q = _pendingCatchupQuote.Value;
            if (snapshot == null || !snapshot.Available || snapshot.SeasonId != q.SeasonId ||
                snapshot.ProgressPoints != q.ProgressBefore || !snapshot.CanCatchup ||
                snapshot.CatchupCurrencyType != q.CurrencyType)
            {
                // 快照已刷新（如刚结算一局或报价变化）：关闭旧确认框，避免按旧报价误提交。
                _pendingCatchupQuote = null;
            }
        }

        private void OpenSkillBook()
        {
            _root?.CloseCurrent();
            SkillUiRoot.Open();
        }

        private void DrawTrialJournal()
        {
            var journal = _game.TrialJournal;
            var snapshot = journal?.Snapshot;
            if (snapshot == null || !snapshot.Available)
            {
                CoreLoopUiArt.Text("JournalUnavailable", transform, 350f, 390f, 1840f, 100f,
                    snapshot?.UnavailableReason ?? "正在同步试炼志…", 32f,
                    CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
                CoreLoopUiArt.Button("JournalRetry", transform, 1060f, 555f, 420f, 70f,
                    "刷新试炼志", () => journal?.RequestSnapshot(true), true, true, 27f);
                return;
            }

            CoreLoopUiArt.Text("JournalProgress", transform, 315f, 320f, 1480f, 60f,
                $"永久点亮 {snapshot.TotalPermanentLitSeals}/{snapshot.TotalPermanentSeals} · 可领里程碑 {journal.ClaimableMilestoneCount}",
                30f, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Button("OpenFullJournal", transform, 1825f, 322f, 420f, 56f,
                "打开完整三卷试炼志与法宝兑换", _root.OpenTrialJournal, true, false, 21f);
            var milestones = snapshot.Milestones;
            int pageCount = Mathf.Max(1, (milestones.Count + 3) / 4);
            _milestonePage = Mathf.Clamp(_milestonePage, 0, pageCount - 1);
            for (int visible = 0; visible < 4; ++visible)
            {
                int index = _milestonePage * 4 + visible;
                if (index >= milestones.Count) break;
                var milestone = milestones[index];
                if (milestone == null) continue;
                float x = 310f + (visible % 2) * 980f;
                float y = 405f + (visible / 2) * 224f;
                CoreLoopUiArt.Card(transform, "Milestone" + index, x, y, 940f, 202f);
                CoreLoopUiArt.Text("MilestoneName" + index, transform, x + 22f, y + 12f, 720f, 42f,
                    milestone.Title, 27f, CoreLoopUiArt.DeepJade, true);
                CoreLoopUiArt.Badge(transform, "MilestoneStatus" + index, x + 718f, y + 13f, 195f,
                    milestone.Claimed ? "已领取" : milestone.Claimable ? "可领取" : "未达成",
                    milestone.Claimable ? CoreLoopUiArt.Jade : CoreLoopUiArt.Muted);
                CoreLoopUiArt.Text("MilestoneDesc" + index, transform, x + 22f, y + 59f, 880f, 53f,
                    milestone.Desc, 21f, CoreLoopUiArt.Ink, false, true);
                CoreLoopUiArt.Text("MilestoneSeals" + index, transform, x + 22f, y + 119f, 510f, 32f,
                    $"战法印 {milestone.CurrentSeals}/{milestone.RequiredSeals} · 绑定砂 {milestone.BoundSandCount} · 拓印券 {milestone.BoundArtifactVoucherCount}",
                    20f, CoreLoopUiArt.Muted);
                uint milestoneId = milestone.MilestoneId;
                CoreLoopUiArt.Button("ClaimMilestone" + index, transform, x + 707f, y + 139f, 208f, 52f,
                    milestone.Claimed ? "已领取" : "领取里程碑",
                    () => journal.ClaimMilestone(milestoneId),
                    milestone.Claimable && snapshot.CanOperate, true, 23f);
            }
            CoreLoopUiArt.Button("PrevMilestonePage", transform, 835f, 860f, 210f, 59f,
                "上一页", () => { --_milestonePage; Refresh(); }, _milestonePage > 0, false, 23f);
            CoreLoopUiArt.Text("MilestonePage", transform, 1075f, 867f, 270f, 45f,
                $"{_milestonePage + 1} / {pageCount}", 25f, CoreLoopUiArt.Muted,
                false, false, TextAlignmentOptions.Center);
            CoreLoopUiArt.Button("NextMilestonePage", transform, 1370f, 860f, 210f, 59f,
                "下一页", () => { ++_milestonePage; Refresh(); }, _milestonePage + 1 < pageCount, false, 23f);
            if (!string.IsNullOrEmpty(journal.Status))
                CoreLoopUiArt.Text("JournalStatus", transform, 330f, 928f, 1840f, 34f,
                    journal.Status, 21f, journal.StatusIsError ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade);
        }

        private void DrawWuxingFormationsView(SeasonClient season)
        {
            uint equipped = season?.EquippedFormationId ?? 0u;
            bool busy = season == null || season.ActionPending;

            // 人宠五行相生共鸣实时预览条
            uint myClassId = _game != null ? _game.ResolveRoleClassId(_game.PlayerId) : 0u;
            uint activePetTableId = 0u;
            var petList = _game?.Pets?.Pets;
            if (petList != null && petList.ActivePetId != 0ul)
            {
                for (int i = 0; i < petList.Pets.Count; ++i)
                {
                    if (petList.Pets[i].PetId == petList.ActivePetId)
                    {
                        activePetTableId = petList.Pets[i].PetTableId;
                        break;
                    }
                }
            }
            uint playerElem = WuxingExpansionModels.ElementOfClass(myClassId);
            uint petElem = WuxingExpansionModels.ElementOfPet(activePetTableId);
            bool synergyReady = WuxingExpansionModels.AreElementsMutuallyGenerating(playerElem, petElem);

            CoreLoopUiArt.Card(transform, "SynergyPreviewCard", 295f, 312f, 1970f, 96f, selected: synergyReady);
            var pElemIcon = WuxingExpansionsUiArt.LoadElementIcon(playerElem);
            var petElemIcon = WuxingExpansionsUiArt.LoadElementIcon(petElem);
            if (pElemIcon != null)
                SeasonV1UiArt.CreateFixedIcon(transform, "PlayerElemIcon", 316f, 336f, 48f, 48f, pElemIcon, Color.clear);
            if (petElemIcon != null)
                SeasonV1UiArt.CreateFixedIcon(transform, "PetElemIcon", 370f, 336f, 48f, 48f, petElemIcon, Color.clear);
            float synTextX = (pElemIcon != null || petElemIcon != null) ? 430f : 326f;
            CoreLoopUiArt.Text("SynergyTitle", transform, synTextX, 322f, 1520f, 38f,
                WuxingExpansionModels.DescribeSynergy(myClassId, activePetTableId),
                23f, synergyReady ? CoreLoopUiArt.DeepJade : CoreLoopUiArt.Ink, true);
            CoreLoopUiArt.Text("FormationCounterRule", transform, synTextX, 364f, 1520f, 32f,
                "五阵循环互克（+5% 直伤）：天覆(金) → 地载(土) → 风扬(木) → 云垂(水) → 虎翼(火) → 天覆(金) · 全员免费自由切换",
                19f, CoreLoopUiArt.Muted, false);
            CoreLoopUiArt.Button("UnequipFormation", transform, 1990f, 332f, 245f, 54f,
                equipped == 0u ? "当前未布阵" : "卸下当前阵法",
                () => season?.EquipFormation(0u), !busy && equipped != 0u, false, 20f);

            // 左侧：1:1 八卦五行阵盘（formation_compass_bg.png），5 枚阵法徽章精准落入外环五槽
            CoreLoopUiArt.Card(transform, "FormationCompassCard", 295f, 422f, 530f, 502f);
            const float compassSize = 460f;
            const float compassX = 330f;
            const float compassY = 442f;
            var compassBg = WuxingExpansionsUiArt.LoadFormationCompassBg();
            if (compassBg != null)
                SeasonV1UiArt.CreateFixedIcon(transform, "FormationCompassBg", compassX, compassY, compassSize, compassSize, compassBg, Color.white);

            // 中心安全区（对齐 CONTROL-USAGE.md：246..522 / 768）显示当前阵法名
            CoreLoopUiArt.Text("CompassCenterName", transform, compassX + 148f, compassY + 190f, 164f, 38f,
                WuxingExpansionModels.FormationName(equipped), 22f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
            CoreLoopUiArt.Text("CompassCenterSub", transform, compassX + 148f, compassY + 230f, 164f, 44f,
                equipped > 0u ? "当前已布设" : "点击外环布阵", 16f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);

            var formations = WuxingExpansionModels.Formations;
            // 五槽中心比例（顺时针：顶/右上/右下/左下/左上，精确对齐重绘 512×512 formation_compass_bg.png 五圆槽质心）
            Vector2[] socketNorm =
            {
                new Vector2(0.500f, 0.168f),
                new Vector2(0.796f, 0.378f),
                new Vector2(0.689f, 0.707f),
                new Vector2(0.304f, 0.707f),
                new Vector2(0.199f, 0.378f),
            };
            for (int i = 0; i < formations.Length && i < socketNorm.Length; ++i)
            {
                var def = formations[i];
                bool isEquipped = equipped == def.Id;
                float badgeSize = isEquipped ? 72f : 62f;
                float bx = compassX + socketNorm[i].x * compassSize - badgeSize * 0.5f;
                float by = compassY + socketNorm[i].y * compassSize - badgeSize * 0.5f;
                var socketIcon = WuxingExpansionsUiArt.LoadFormationIcon(def.Id) ?? WuxingExpansionsUiArt.LoadElementIcon(def.Element);
                if (socketIcon != null)
                    SeasonV1UiArt.CreateFixedIcon(transform, "CompassBadge" + def.Id, bx, by, badgeSize, badgeSize, socketIcon, Color.clear);
                uint capturedSocketId = def.Id;
                var hitBtn = QdaoUguiFactory.CreateRect("CompassHit" + def.Id, transform, bx, by, badgeSize, badgeSize);
                var hitImg = hitBtn.gameObject.AddComponent<UnityEngine.UI.Image>();
                hitImg.color = isEquipped ? new Color(1f, 0.88f, 0.42f, 0.16f) : new Color(0f, 0f, 0f, 0f);
                hitImg.raycastTarget = true;
                var btn = hitBtn.gameObject.AddComponent<UnityEngine.UI.Button>();
                btn.interactable = !busy && !isEquipped;
                btn.onClick.AddListener(() => season?.EquipFormation(capturedSocketId));
            }

            // 右侧：5 套免费道家阵法详情与切换列表
            for (int i = 0; i < formations.Length; ++i)
            {
                var def = formations[i];
                float fy = 422f + i * 102f;
                bool isEquipped = equipped == def.Id;
                CoreLoopUiArt.Card(transform, "FormationRow" + def.Id, 842f, fy, 1423f, 92f, selected: isEquipped);

                var formIcon = WuxingExpansionsUiArt.LoadFormationIcon(def.Id) ?? WuxingExpansionsUiArt.LoadElementIcon(def.Element);
                if (formIcon != null)
                    SeasonV1UiArt.CreateFixedIcon(transform, "FormIcon" + def.Id, 862f, fy + 16f, 60f, 60f, formIcon, Color.clear);

                var elemIcon = WuxingExpansionsUiArt.LoadElementIcon(def.Element);
                if (elemIcon != null)
                    SeasonV1UiArt.CreateFixedIcon(transform, "FormElemIcon" + def.Id, 930f, fy + 26f, 40f, 40f, elemIcon, Color.clear);

                CoreLoopUiArt.Text("FormName" + def.Id, transform, 980f, fy + 10f, 200f, 38f,
                    def.Name, 25f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.MidlineLeft);
                CoreLoopUiArt.Text("FormElemText" + def.Id, transform, 980f, fy + 50f, 200f, 28f,
                    $"{WuxingExpansionModels.ElementName(def.Element)}行阵法", 16f, CoreLoopUiArt.Muted, false, false, TextAlignmentOptions.MidlineLeft);
                CoreLoopUiArt.Text("FormBonus" + def.Id, transform, 1188f, fy + 14f, 800f, 64f,
                    def.BonusSummary, 20f, CoreLoopUiArt.Ink, false, true, TextAlignmentOptions.MidlineLeft);

                uint capturedId = def.Id;
                CoreLoopUiArt.Button("EquipFormBtn" + def.Id, transform, 2006f, fy + 19f, 236f, 54f,
                    isEquipped ? "当前已布设" : "免费布设",
                    () => season?.EquipFormation(capturedId), !busy && !isEquipped, !isEquipped, 21f);
            }

            if (season != null && !string.IsNullOrEmpty(season.Status))
                CoreLoopUiArt.Text("FormationStatus", transform, 300f, 940f, 1960f, 34f,
                    season.Status, 21f, season.StatusIsError ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade);
        }
    }
}
