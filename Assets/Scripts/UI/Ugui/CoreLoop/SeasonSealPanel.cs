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
        private bool _showTrialJournal;
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
            _showTrialJournal = false;
            _skillsExpanded = false;
            _pendingCatchupQuote = null;
            gameObject.SetActive(true);
            _game?.Season?.RequestState();
            Refresh();
        }

        public void ShowTrialJournal()
        {
            _showTrialJournal = true;
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
            string frameTitle = !_showTrialJournal && snapshot != null && snapshot.Available && !string.IsNullOrWhiteSpace(snapshot.SeasonName)
                ? $"{snapshot.SeasonName} · 赛季印"
                : "赛季印 · 试炼志";
            CoreLoopUiArt.Frame(transform, frameTitle, _root.CloseCurrent);

            CoreLoopUiArt.Button("SeasonTab", transform, 295f, 246f, 230f, 48f,
                "赛季印", Show, true, !_showTrialJournal, 23f, !_showTrialJournal ? "tab_active" : "tab_normal");
            CoreLoopUiArt.Button("JournalTab", transform, 538f, 246f, 230f, 48f,
                "试炼志", () => _root.OpenTrialJournal(), true, _showTrialJournal, 23f, _showTrialJournal ? "tab_active" : "tab_normal");

            if (_showTrialJournal)
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

            if (!snapshot.Available)
            {
                CoreLoopUiArt.Text("Unavailable", transform, 400f, 420f, 1760f, 120f,
                    string.IsNullOrWhiteSpace(snapshot.UnavailableReason) ? "当前暂无进行中的赛季" : snapshot.UnavailableReason,
                    34f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
                CoreLoopUiArt.Button("Retry", transform, 1070f, 575f, 420f, 68f,
                    "刷新赛季资料", () => season?.RequestState(true), season?.StatePending != true, true, 26f);
                return;
            }

            DrawSeasonHeaderPill(snapshot);
            DrawProgressAndLightStats(snapshot);

            bool maxed = snapshot.MaxPoints > 0u && snapshot.ProgressPoints >= snapshot.MaxPoints;
            bool busy = season.ActionPending || season.RequiresReconnect;
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
                796f, 240f, 1060f, 60f, "season_banner", CoreLoopUiArt.DeepJade);
            var bannerText = CoreLoopUiArt.Text("SeasonBannerTitle", banner.transform,
                40f, 0f, 980f, 60f, $"{snapshot.SeasonName} · 单槽赛季印", 27f, SeasonV1UiArt.BadgeIvoryText,
                true, false, TextAlignmentOptions.Center);
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
                SeasonModels.DescribeSourceRuleLine(snapshot), 21f, CoreLoopUiArt.Warning,
                false, false, TextAlignmentOptions.Center);
        }

        private void DrawStandardBody(SeasonClient season, global::SeasonStateSnapshot snapshot, bool busy)
        {
            if (_skillsExpanded)
            {
                DrawExpandedSkillsView(snapshot);
                return;
            }

            // 中部：4 选 1 赛季印列表（每行左图标 + 名称 + 说明 + 右侧「当前佩戴 / 切换」）
            int count = Mathf.Min(snapshot.Seals.Count, 4);
            for (int i = 0; i < count; ++i)
            {
                var seal = snapshot.Seals[i];
                if (seal == null) continue;
                DrawSealRow(season, snapshot, seal, 295f, 456f + i * 82f, 1970f, 74f, busy, compact: false);
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
            // 满级 12/12 态（对齐 03-season-seal-maxed-transparent.png）：左 4 枚赛季印，右 2×2 本季免费技能预览
            int count = Mathf.Min(snapshot.Seals.Count, 4);
            for (int i = 0; i < count; ++i)
            {
                var seal = snapshot.Seals[i];
                if (seal == null) continue;
                DrawSealRow(season, snapshot, seal, 295f, 456f + i * 86f, 960f, 78f, busy, compact: true);
            }

            var previewHeader = SeasonV1UiArt.CreateHorizontalSlice(transform, "FreeSkillsHeader",
                1280f, 456f, 985f, 50f, "season_banner", CoreLoopUiArt.DeepJade);
            var previewTitle = CoreLoopUiArt.Text("FreeSkillsHeaderTitle", previewHeader.transform,
                20f, 0f, 945f, 50f, "本季免费技能预览", 25f, SeasonV1UiArt.BadgeIvoryText,
                true, false, TextAlignmentOptions.Center);
            previewTitle.raycastTarget = false;

            DrawSkillsGrid(snapshot, 1280f, 518f, 482f, 134f, 20f, 14f);

            CoreLoopUiArt.Card(transform, "MaxedNoticeBar", 295f, 814f, 1970f, 64f);
            CoreLoopUiArt.Text("MaxedNoticeText", transform, 315f, 824f, 1930f, 44f,
                "本季进度已满，可随时免费切换赛季印", 27f, CoreLoopUiArt.DeepJade,
                true, false, TextAlignmentOptions.Center);

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
            CoreLoopUiArt.Text("SealDesc_" + seal.SealId, transform, descX, y + 8f, descW, h - 16f,
                seal.Description, compact ? 18f : 21f, CoreLoopUiArt.Ink, false, true);

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
    }
}
