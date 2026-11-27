using System;
using System.Collections.Generic;
using System.Globalization;
using Guildpb;
using MmorpgClient.Game.Guild;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Guild.GuildWarUiArt;

namespace MmorpgClient.UI.Ugui.Guild
{
    public enum GuildWarPage { Main, Garrison, Bounty, Logs, Settlement, FieldHud }

    /// <summary>
    /// 帮会全天帮战 V2 交互式 UGUI 窗口（零匹配池 0 秒直打、三灵脉、五槽守旗、固定三档追杀令、结算连战与非阻断预警横幅）：
    /// - 严格绑定 GuildWarClient 不可变快照与权威服务端时间锚，绝不私自计算奖励或胜负；
    /// - 整窗按 1932×814 等比缩放并预留安全区与小游戏胶囊边距，无 Hover 依赖；
    /// - 所有事件监听随窗口销毁清理，无线程/阻塞等待。
    /// </summary>
    public sealed class GuildWarWindow
    {
        public RectTransform Root => _root;
        public RectTransform AlertRoot => _alertRoot;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public bool ModalVisible => _modal != null && _modal.gameObject.activeSelf;
        public GuildWarPage Page { get; private set; } = GuildWarPage.Main;

        public event Action<ulong> WatchBattleRequested;
        public event Action<ulong> ReplayBattleRequested;
        public event Action<uint, ulong> TravelToSceneRequested;
        public event Action OpenGuildShopRequested;
        public event Action BackToGuildRequested;

        private readonly RectTransform _root;
        private readonly RectTransform _frame;
        private readonly Image _panelBg;
        private readonly RectTransform _body;
        private readonly TMP_Text _titleLabel;
        private readonly TMP_Text _statusLabel;
        private readonly Button[] _navTabs = new Button[6];
        private readonly RectTransform _alertRoot;

        private RectTransform _modal;
        private GuildWarClient _client;
        private ulong _myGuildId;
        private bool _isOfficerOrLeader;
        private bool _showGarrisonTargets;
        private int _bountyTab; // 0=本帮追杀令, 1=全区江湖榜, 2=帮会仇敌录
        private GuildWarAlertSnapshot _activeAlert;
        private float _alertHideAtUnscaledSec;
        private GuildWarBountyTier _selectedBountyTier = GuildWarBountyTier.NormalSilver;
        private bool _selectedBountyPublic = true;

        public GuildWarWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("GuildWarWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var backdrop = _root.gameObject.AddComponent<Image>();
            backdrop.color = DeepBackdrop;
            backdrop.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();

            _frame = QdaoUguiFactory.CreateCenteredRect("GuildWarFrame", _root, PanelWidth, PanelHeight);
            float scale = Mathf.Min(2520f / PanelWidth, 1040f / PanelHeight);
            _frame.localScale = new UnityEngine.Vector3(scale, scale, 1f);

            _panelBg = Art(_frame, "GuildWarPanelBg", "panel_02_guild_war_main", 0f, 0f, PanelWidth, PanelHeight);
            _panelBg.raycastTarget = false;

            _titleLabel = Label(_frame, "GuildWarTitle", "帮 会 争 锋", 746f, 24f, 440f, 52f, 40f, LightText, align: TextAlignmentOptions.Center);

            string[] navTitles = { "灵脉争锋", "守旗阵", "仇敌追杀", "帮战大事记", "战况结算", "野外现场" };
            for (int i = 0; i < _navTabs.Length; i++)
            {
                GuildWarPage target = (GuildWarPage)i;
                _navTabs[i] = ActionButton(_frame, "GuildWarTab_" + target, navTitles[i],
                    176f + i * 246f, 84f, 232f, 54f, () => ShowPage(target), primary: i == 0, fontSize: 24f);
            }

            ActionButton(_frame, "GuildWarBackToGuild", "返回帮会", 1664f, 84f, 136f, 54f,
                () => { Hide(); BackToGuildRequested?.Invoke(); }, fontSize: 21f);
            ActionButton(_frame, "GuildWarClose", "关闭", 1776f, 22f, 110f, 54f, Hide, fontSize: 22f);

            _body = QdaoUguiFactory.CreateRect("GuildWarBody", _frame, 148f, 152f, 1636f, 588f);
            _statusLabel = Label(_frame, "GuildWarStatus", "", 156f, 754f, 1280f, 38f, 21f, LabelBrown);
            ActionButton(_frame, "GuildWarRefresh", "刷新战况", 1592f, 744f, 192f, 48f,
                () => _client?.RefreshOverview(), primary: true, fontSize: 22f);

            // 非阻断顶部预警横幅挂在父容器顶层，关闭主窗仍可展示踢馆/追兵预警
            _alertRoot = QdaoUguiFactory.CreateRect("GuildWarAlertOverlay", parent, 440f, 18f, 1680f, 120f);
            _alertRoot.gameObject.SetActive(false);
        }

        public void SetContext(GuildWarClient client, ulong myGuildId, bool isOfficerOrLeader)
        {
            if (_client != null)
            {
                _client.Changed -= Render;
                _client.AlertReceived -= ShowAlertBanner;
                _client.SettlementOpened -= OnSettlementOpened;
                _client.TargetAlreadyInBattle -= ShowConflictSpectateModal;
            }
            _client = client;
            _myGuildId = myGuildId;
            _isOfficerOrLeader = isOfficerOrLeader;
            if (_client != null)
            {
                _client.Changed += Render;
                _client.AlertReceived += ShowAlertBanner;
                _client.SettlementOpened += OnSettlementOpened;
                _client.TargetAlreadyInBattle += ShowConflictSpectateModal;
            }
            Render();
        }

        public void Show(GuildWarPage page = GuildWarPage.Main)
        {
            Page = page;
            _root.gameObject.SetActive(true);
            Render();
            if (_client != null && _client.Overview == null && !_client.OverviewLoading)
                _client.RefreshOverview();
        }

        public void ShowPage(GuildWarPage page)
        {
            Page = page;
            CloseModal();
            Render();
        }

        public void Hide()
        {
            CloseModal();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void DismissAlert()
        {
            _activeAlert = null;
            _alertHideAtUnscaledSec = 0f;
            if (_alertRoot != null) _alertRoot.gameObject.SetActive(false);
        }

        public void Tick(float unscaledTimeSec)
        {
            if (_activeAlert != null && _alertHideAtUnscaledSec > 0f && unscaledTimeSec >= _alertHideAtUnscaledSec)
                DismissAlert();
        }

        public void Dispose()
        {
            if (_client != null)
            {
                _client.Changed -= Render;
                _client.AlertReceived -= ShowAlertBanner;
                _client.SettlementOpened -= OnSettlementOpened;
                _client.TargetAlreadyInBattle -= ShowConflictSpectateModal;
                _client = null;
            }
            Hide();
            DismissAlert();
        }

        private void OnSettlementOpened(GuildWarBattleSettlementSnapshot _)
        {
            Show(GuildWarPage.Settlement);
        }

        public void ShowAlertBanner(GuildWarAlertSnapshot alert)
        {
            if (alert == null || _alertRoot == null) return;
            _activeAlert = alert;
            _alertHideAtUnscaledSec = Time.unscaledTime + 6f;
            GuildUiArt.Clear(_alertRoot);
            bool isFeudOrStreak = alert.Kind == GuildWarAlertKind.FeudChallenge
                || alert.Kind == GuildWarAlertKind.FeudStarted
                || alert.Kind == GuildWarAlertKind.FeudEnded
                || alert.Kind == GuildWarAlertKind.StreakBounty;
            Image bg;
            if (isFeudOrStreak)
            {
                string fallback = alert.Kind == GuildWarAlertKind.StreakBounty ? "alert_banner_bounty" : "alert_banner_siege";
                bg = ArtOrFallback(_alertRoot, "AlertBg", "alert_banner_feud", fallback, 0f, 0f, 1680f, 120f);
            }
            else
            {
                string bannerKey = alert.Kind == GuildWarAlertKind.PursuerEnteredMap || alert.Kind == GuildWarAlertKind.BountyResolved
                    ? "alert_banner_bounty"
                    : "alert_banner_siege";
                bg = Art(_alertRoot, "AlertBg", bannerKey, 0f, 0f, 1680f, 120f);
            }
            bg.raycastTarget = false;
            Label(_alertRoot, "AlertText", GuildWarText.FormatAlert(alert), 130f, 24f, 1116f, 72f, 26f, LightText, wrap: true);
            if (alert.Kind == GuildWarAlertKind.LeylineUnderSiege)
            {
                ActionButton(_alertRoot, "AlertActionSiege", "立即驰援", 1262f, 32f, 168f, 56f,
                    () => { DismissAlert(); _client?.StartLeylineSiege(alert.LeylineId); }, danger: true, fontSize: 22f);
            }
            else if (alert.Kind == GuildWarAlertKind.PursuerEnteredMap || alert.Kind == GuildWarAlertKind.BountyResolved)
            {
                ActionButton(_alertRoot, "AlertActionBounty", "查看追杀", 1262f, 32f, 168f, 56f,
                    () => { DismissAlert(); Show(GuildWarPage.Bounty); }, primary: true, fontSize: 22f);
            }
            else if (alert.Kind == GuildWarAlertKind.FeudChallenge || alert.Kind == GuildWarAlertKind.FeudStarted || alert.Kind == GuildWarAlertKind.FeudEnded)
            {
                ActionButton(_alertRoot, "AlertActionFeud", "查看约战", 1262f, 32f, 168f, 56f,
                    () => { DismissAlert(); Show(GuildWarPage.Main); }, danger: alert.Kind != GuildWarAlertKind.FeudEnded, primary: alert.Kind == GuildWarAlertKind.FeudEnded, fontSize: 22f);
            }
            else if (alert.Kind == GuildWarAlertKind.StreakBounty)
            {
                ActionButton(_alertRoot, "AlertActionStreak", alert.RewardSilver == 0 ? "前往围剿" : "查看战况", 1262f, 32f, 168f, 56f,
                    () => { DismissAlert(); Show(GuildWarPage.Main); }, danger: alert.RewardSilver == 0, primary: alert.RewardSilver > 0, fontSize: 22f);
            }
            ActionButton(_alertRoot, "AlertDismiss", "忽略", 1444f, 32f, 116f, 56f, DismissAlert, fontSize: 21f);
            _alertRoot.gameObject.SetActive(true);
        }

        public void ShowConflictSpectateModal(ulong offenderBattleId)
        {
            if (offenderBattleId == 0) return;
            CloseModal();
            var frame = OpenModal("目标刚刚进入激战", "抢点并发冲突：该对手刚刚开启帮战对局，你可以立即实时观战或切换其他目标。");
            Art(frame, "ConflictSwords", "badge_spectate_swords", 480f, 180f, 120f, 120f, true);
            Label(frame, "ConflictBattleId", "对局编号：" + offenderBattleId.ToString(CultureInfo.InvariantCulture),
                80f, 316f, 920f, 40f, 24f, LabelBrown, align: TextAlignmentOptions.Center);
            ActionButton(frame, "ConflictWatchNow", "实时观战", 160f, 392f, 320f, 68f,
                () => { CloseModal(); WatchBattleRequested?.Invoke(offenderBattleId); }, primary: true, fontSize: 26f);
            ActionButton(frame, "ConflictSwitchTarget", "换个目标", 600f, 392f, 320f, 68f,
                () => { CloseModal(); _client?.RefreshOverview(); }, fontSize: 26f);
        }

        public void Render()
        {
            if (_body == null || _root == null) return;
            GuildUiArt.Clear(_body);

            string panelKey = Page switch
            {
                GuildWarPage.Garrison => "panel_03_garrison_and_conflict",
                GuildWarPage.Bounty => "panel_04_bounty_hunt",
                GuildWarPage.Logs => "panel_05_bounty_post_and_logs",
                GuildWarPage.Settlement => "panel_06_war_settlement",
                GuildWarPage.FieldHud => "panel_08_field_hud",
                _ => "panel_02_guild_war_main",
            };
            _panelBg.sprite = Load(panelKey);
            _titleLabel.text = Page switch
            {
                GuildWarPage.Garrison => "守 旗 阵 驻 防",
                GuildWarPage.Bounty => "仇 敌 与 追 杀 榜",
                GuildWarPage.Logs => "帮 战 大 事 记",
                GuildWarPage.Settlement => "帮 战 结 算",
                GuildWarPage.FieldHud => "野 外 帮 战 现 场",
                _ => "帮 会 争 锋",
            };
            _statusLabel.text = _client?.Status ?? "等待连接帮战服务…";

            for (int i = 0; i < _navTabs.Length; i++)
            {
                bool active = (int)Page == i;
                var img = _navTabs[i].GetComponent<Image>();
                if (img != null) img.sprite = GuildUiArt.Load(active ? "button_primary" : "button_secondary");
                var cap = _navTabs[i].GetComponentInChildren<TMP_Text>();
                if (cap != null) cap.color = active ? LightText : BodyInk;
            }

            if (_client == null)
            {
                Label(_body, "GuildWarNoClient", "请进入角色并加入帮会后查看帮会争锋。", 40f, 220f, 1540f, 80f, 28f, LabelBrown, align: TextAlignmentOptions.Center);
                return;
            }

            switch (Page)
            {
                case GuildWarPage.Main: RenderMain(); break;
                case GuildWarPage.Garrison: RenderGarrison(); break;
                case GuildWarPage.Bounty: RenderBounty(); break;
                case GuildWarPage.Logs: RenderLogs(); break;
                case GuildWarPage.Settlement: RenderSettlement(); break;
                case GuildWarPage.FieldHud: RenderFieldHud(); break;
            }
        }

        private void RenderMain()
        {
            var ov = _client.Overview;
            if (ov == null)
            {
                Card(_body, "GuildWarEmptyCard", 280f, 120f, 1076f, 310f);
                Art(_body, "GuildWarEmptyFlag", "banner_guild_flag", 320f, 146f, 160f, 240f, true);
                string heading = _client.FeatureUnavailable ? "全天帮战服务待开启" : _client.OverviewLoading ? "正在同步帮会争锋战况…" : "暂无帮战战况快照";
                string desc = !string.IsNullOrEmpty(_client.LastError)
                    ? _client.LastError
                    : "无匹配池 0 秒直打 · 八景岛 / 碧渊水宫 / 月汐湾三灵脉争夺 · 四档周战功宝箱与江湖追杀令。";
                Label(_body, "GuildWarEmptyTitle", heading, 510f, 164f, 780f, 44f, 30f, TitleJade);
                Label(_body, "GuildWarEmptyDesc", desc, 510f, 220f, 780f, 108f, 22f, BodyInk, wrap: true);
                ActionButton(_body, "GuildWarEmptyRetry", "重新同步战况", 510f, 344f, 240f, 56f,
                    () => _client.RefreshOverview(), primary: true, enabled: !_client.OverviewLoading, fontSize: 23f);
                ActionButton(_body, "GuildWarOpenShopFromEmpty", "前往帮会商店", 776f, 344f, 240f, 56f,
                    () => { Hide(); OpenGuildShopRequested?.Invoke(); }, fontSize: 23f);
                return;
            }

            ulong nowMs = _client.EstimatedServerNowMs;
            bool busy = _client.ActionInFlight || _client.OverviewLoading;

            // ── 顶栏：周战功四档宝箱 + 灵脉分红 + 战意/挂牌开关 ──
            Card(_body, "TopProgressCard", 0f, 0f, 1636f, 132f);
            var stance = ov.MyStance;
            ulong myMerit = stance?.MyWeeklyMerit ?? 0UL;
            Label(_body, "WeeklyMeritHeading", $"本周全帮战功箱 · 个人战功 {myMerit}（达 30 可领）", 20f, 10f, 660f, 32f, 22f, TitleJade);
            for (int i = 0; i < Math.Min(4, ov.Milestones.Count); i++)
            {
                var m = ov.Milestones[i];
                float cx = 20f + i * 168f;
                var chestBtn = QdaoUguiFactory.CreateArtButton("MilestoneChest_" + m.MilestoneId, _body,
                    cx, 44f, 68f, 68f, Load(ChestKey(m.Claimed, m.Claimable)), out var chestImg);
                chestImg.preserveAspect = true;
                chestBtn.interactable = !busy && m.Claimable && !m.Claimed;
                uint mid = m.MilestoneId;
                chestBtn.onClick.AddListener(() => _client.ClaimMilestone(mid));
                string st = m.Claimed ? "已领" : m.Claimable ? "可领!" : m.RequiredMerit.ToString(CultureInfo.InvariantCulture);
                Label(_body, "MilestoneLabel_" + m.MilestoneId, st, cx + 70f, 52f, 92f, 52f, 20f, m.Claimable && !m.Claimed ? CrimsonText : LabelBrown, wrap: true);
            }

            var div = ov.Dividend;
            uint divClaimable = div?.ClaimableCount ?? 0u;
            ActionButton(_body, "ClaimDividendBtn",
                $"灵脉分红 ({divClaimable}份 · 周{div?.ClaimedThisWeek ?? 0}/{div?.WeeklyCap ?? 12})",
                708f, 20f, 304f, 48f, () => _client.ClaimDividend(),
                primary: divClaimable > 0, enabled: !busy && divClaimable > 0, fontSize: 20f);
            Label(_body, "GuildContributionLabel",
                $"可用帮贡：{ov.ContributionBalance} · 本周帮会产出 {div?.GuildProducedThisWeek ?? 0}/{div?.GuildWeeklyCap ?? 24}",
                708f, 78f, 330f, 32f, 19f, LabelBrown);

            bool peace = stance?.PeaceMode ?? false;
            Art(_body, "MyStanceBadge", peace ? "badge_stance_peace" : "badge_stance_war", 1044f, 20f, 84f, 84f, true);
            string peaceBtnText = peace
                ? "切回战意（0秒）"
                : stance != null && stance.PeaceReadyAtMs > nowMs
                    ? "和平冷却 " + GuildWarText.FormatCountdown(stance.PeaceReadyAtMs, nowMs)
                    : "切和平免战";
            ActionButton(_body, "TogglePeaceBtn", peaceBtnText, 1140f, 16f, 228f, 46f,
                () => _client.SetPeaceMode(!peace), enabled: !busy, fontSize: 20f);

            bool openChallenge = stance?.OpenChallenge ?? false;
            ActionButton(_body, "ToggleOpenChallengeBtn", openChallenge ? "挂牌迎战：开" : "挂牌迎战：关",
                1140f, 70f, 228f, 46f, () => _client.SetOpenChallenge(!openChallenge),
                primary: openChallenge, enabled: !busy, fontSize: 20f);

            uint myStreak = stance?.MyStreak ?? 0u;
            if (myStreak >= 3)
                Art(_body, "MyStreakBadge", StreakBadgeKey(myStreak), 1384f, 20f, 84f, 84f, true);
            Label(_body, "MyStreakText", myStreak > 0 ? $"当前 {myStreak} 连胜" : "整装待发", 1474f, 24f, 150f, 36f, 21f, TitleJade);
            if (ov.ActiveBattleId != 0)
            {
                ulong activeBid = ov.ActiveBattleId;
                ActionButton(_body, "ResumeActiveWarBtn", "回到帮战对局", 1388f, 68f, 232f, 46f,
                    () => WatchBattleRequested?.Invoke(activeBid), danger: true, fontSize: 20f);
            }

            // ── 中栏：八仙岛 / 碧渊水宫 / 月汐湾 三座灵脉卡 ──
            for (int i = 0; i < Math.Min(3, ov.Leylines.Count); i++)
            {
                var ley = ov.Leylines[i];
                float lx = i * 552f;
                Card(_body, "LeylineCard_" + ley.LeylineId, lx, 146f, 532f, 216f);
                Art(_body, "LeylineBg_" + ley.LeylineId, LeylineBackgroundKey(ley.LeylineId), lx + 8f, 154f, 516f, 110f);
                if (ley.ShieldUntilMs > nowMs)
                    Art(_body, "LeylineShield_" + ley.LeylineId, "leyline_shield_overlay", lx + 8f, 154f, 516f, 110f);
                bool isGold = ley.IsGoldLeyline || (ov.GoldLeylineId != 0 && ley.LeylineId == ov.GoldLeylineId);
                bool ownerIsRival = ley.OwnerGuildId != 0 && IsRivalGuild(ov.BannerHonor, ley.OwnerGuildId);
                if (isGold)
                    Art(_body, "LeylineGoldBadge_" + ley.LeylineId, "badge_gold_leyline", lx + 466f, 158f, 52f, 52f, true);
                if (ownerIsRival)
                    Art(_body, "LeylineRivalBadge_" + ley.LeylineId, "badge_rival_guild", lx + (isGold ? 410f : 466f), 158f, 52f, 52f, true);
                float titleW = isGold && ownerIsRival ? 386f : (isGold || ownerIsRival) ? 442f : 496f;
                string tagText = isGold ? "1.5x 金脉热点" : ley.UnderSiege ? "正被踢馆!" : ley.ActiveBattleCount > 0 ? $"激战 {ley.ActiveBattleCount} 场" : "守旗驻守";
                Label(_body, "LeylineName_" + ley.LeylineId,
                    (string.IsNullOrEmpty(ley.Name) ? GuildWarText.LeylineName(ley.LeylineId) : ley.Name) + " · " + tagText,
                    lx + 18f, 160f, titleW, 34f, 23f, LightText);
                string ownerText = ley.IsUnclaimed ? "无主 · 灵脉守卫" : "占领帮：" + ley.OwnerGuildName + $"（守旗 {ley.GarrisonCount}/5）" + (ownerIsRival ? " · 宿敌" : string.Empty);
                Label(_body, "LeylineOwner_" + ley.LeylineId, ownerText, lx + 18f, 198f, titleW, 30f, 21f, LightText);

                // 旗势进度条（横向三段拉伸）
                Art(_body, "LeylineHpTrack_" + ley.LeylineId, "bar_banner_hp_track", lx + 18f, 272f, 326f, 26f);
                bool ally = _myGuildId != 0 && ley.OwnerGuildId == _myGuildId;
                float fillW = Mathf.Max(36f, 326f * ley.HpRatio);
                if (ley.BannerHp > 0)
                    Art(_body, "LeylineHpFill_" + ley.LeylineId, ally ? "bar_banner_hp_fill_ally" : "bar_banner_hp_fill_enemy",
                        lx + 18f, 272f, fillW, 26f);
                string hpText = ley.ShieldUntilMs > nowMs
                    ? $"护盾 {GuildWarText.FormatCountdown(ley.ShieldUntilMs, nowMs)}"
                    : $"{ley.BannerHp}/{ley.BannerMaxHp}";
                Label(_body, "LeylineHpText_" + ley.LeylineId, hpText, lx + 354f, 269f, 164f, 32f, 20f, BodyInk);

                uint lid = ley.LeylineId;
                uint sceneConf = ley.SceneConfigId;
                ulong recSceneId = ley.RecommendedSceneId;
                ulong watchId = ley.FeaturedWatchBattleId;
                bool canOfferFeud = _isOfficerOrLeader && !ally && !ley.IsUnclaimed && ov.ActiveFeud == null;
                if (canOfferFeud)
                {
                    ulong ownerGid = ley.OwnerGuildId;
                    bool feudReady = !busy && ov.NextFeudIssueAtMs <= nowMs;
                    ActionButton(_body, "AttackLeyline_" + lid, "攻打灵脉",
                        lx + 14f, 308f, 132f, 44f, () => _client.StartLeylineSiege(lid),
                        danger: true, enabled: !busy, fontSize: 19f);
                    ActionButton(_body, "WatchLeyline_" + lid, watchId != 0 ? "观战" : "无激战",
                        lx + 152f, 308f, 104f, 44f, () => WatchBattleRequested?.Invoke(watchId),
                        enabled: watchId != 0, fontSize: 19f);
                    ActionButton(_body, "TravelLeyline_" + lid, "前往现场",
                        lx + 262f, 308f, 122f, 44f, () => TravelToSceneRequested?.Invoke(sceneConf, recSceneId),
                        enabled: sceneConf != 0, fontSize: 19f);
                    ActionButton(_body, "IssueFeudLeyline_" + lid,
                        ov.NextFeudIssueAtMs > nowMs ? "冷却中" : "下战书",
                        lx + 390f, 308f, 128f, 44f, () => _client.IssueFeud(ownerGid),
                        primary: feudReady, enabled: feudReady, fontSize: 19f);
                }
                else
                {
                    ActionButton(_body, "AttackLeyline_" + lid, ally ? "驰援守旗" : "攻打灵脉",
                        lx + 18f, 308f, 166f, 44f, () => _client.StartLeylineSiege(lid),
                        danger: !ally, primary: ally, enabled: !busy, fontSize: 20f);
                    ActionButton(_body, "WatchLeyline_" + lid, watchId != 0 ? "观战" : "无激战",
                        lx + 194f, 308f, 146f, 44f, () => WatchBattleRequested?.Invoke(watchId),
                        enabled: watchId != 0, fontSize: 20f);
                    ActionButton(_body, "TravelLeyline_" + lid, "前往现场",
                        lx + 350f, 308f, 164f, 44f, () => TravelToSceneRequested?.Invoke(sceneConf, recSceneId),
                        enabled: sceneConf != 0, fontSize: 20f);
                }
            }

            // ── 底栏：宿敌约战实况条（有活跃/待应战书时展示）+ 连胜悬赏 + 在线活人/守旗阵目标切换 + 0 秒一键快速开战 ──
            Card(_body, "TargetsCard", 0f, 376f, 1272f, 212f);
            var feud = ov.ActiveFeud;
            bool hasFeudStrip = feud != null && (feud.Status == GuildWarFeudStatus.Pending || feud.Status == GuildWarFeudStatus.Active);
            if (hasFeudStrip)
            {
                Card(_body, "ActiveFeudStripBg", 14f, 382f, 1244f, 46f, PaperFill);
                ArtOrFallback(_body, "ActiveFeudBadge", "badge_feud_edict", "badge_rival_guild", 20f, 385f, 40f, 40f, true);
                if (feud.Status == GuildWarFeudStatus.Pending)
                {
                    bool iAmTarget = _myGuildId != 0 && feud.TargetGuildId == _myGuildId;
                    string pendingText = iAmTarget
                        ? $"【宿敌战书待应】[{feud.IssuerGuildName}] 下发 2h 约战（+25% 收益 · 前3场免递减）· 剩 {GuildWarText.FormatCountdown(feud.AcceptDeadlineMs, nowMs)}"
                        : $"【战书待对方应答】已向 [{feud.TargetGuildName}] 下发 2h 约战战书 · 剩 {GuildWarText.FormatCountdown(feud.AcceptDeadlineMs, nowMs)}";
                    Label(_body, "ActiveFeudText", pendingText, 68f, 388f, 880f, 34f, 19f, CrimsonText);
                    ulong fid = feud.FeudId;
                    if (_isOfficerOrLeader && iAmTarget)
                    {
                        ActionButton(_body, "AcceptFeudBtn", "接战应战", 960f, 385f, 138f, 40f,
                            () => _client.RespondFeud(fid, true), danger: true, enabled: !busy, fontSize: 19f);
                        ActionButton(_body, "DeclineFeudBtn", "婉拒战书", 1106f, 385f, 138f, 40f,
                            () => _client.RespondFeud(fid, false), enabled: !busy, fontSize: 19f);
                    }
                    else if (_isOfficerOrLeader && _myGuildId != 0 && feud.IssuerGuildId == _myGuildId)
                    {
                        ActionButton(_body, "CancelFeudBtn", "撤回战书", 1090f, 385f, 154f, 40f,
                            () => _client.RespondFeud(fid, false), enabled: !busy, fontSize: 19f);
                    }
                }
                else
                {
                    string activeText = $"【2h 宿敌约战激战中】{feud.IssuerGuildName} {feud.IssuerWins}:{feud.TargetWins} {feud.TargetGuildName}"
                        + $"（战功 {feud.IssuerMerit}:{feud.TargetMerit} · +25% 收益）· 剩 {GuildWarText.FormatCountdown(feud.EndsAtMs, nowMs)}";
                    Label(_body, "ActiveFeudText", activeText, 68f, 388f, 996f, 34f, 19f, CrimsonText);
                    ActionButton(_body, "FeudFightNowBtn", "约战直打", 1078f, 385f, 166f, 40f,
                        () => _client.StartQuickBattle(ov.GoldLeylineId), danger: true, enabled: !busy, fontSize: 19f);
                }
            }

            float tabsY = hasFeudStrip ? 434f : 388f;
            float rowsStartY = hasFeudStrip ? 482f : 440f;
            int maxTargetRows = hasFeudStrip ? 2 : 3;

            ActionButton(_body, "TargetsTabLive", $"在线迎战活人 ({ov.LiveTargets.Count})",
                16f, tabsY, 256f, 42f, () => { _showGarrisonTargets = false; Render(); },
                primary: !_showGarrisonTargets, fontSize: 20f);
            ActionButton(_body, "TargetsTabGarrison", $"本帮守旗阵 ({ov.GarrisonSlots.Count}/5)",
                284f, tabsY, 240f, 42f, () => { _showGarrisonTargets = true; Render(); },
                primary: _showGarrisonTargets, fontSize: 20f);

            if (ov.StreakBounties.Count > 0)
            {
                var top = ov.StreakBounties[0];
                Art(_body, "TopStreakBadge", StreakBadgeKey(top.Streak), 540f, tabsY - 2f, 46f, 46f, true);
                Label(_body, "TopStreakText",
                    $"连胜悬赏：[{top.GuildName}·{top.PlayerName}] {top.Streak}连胜（挑落额外 +{top.BonusContribution}帮贡 / +{top.BonusSilver}银两）",
                    594f, tabsY + 4f, 660f, 34f, 19f, CrimsonText);
            }

            if (!_showGarrisonTargets)
            {
                if (ov.LiveTargets.Count == 0)
                {
                    Label(_body, "NoLiveTargets", "当前暂无在线挂牌异帮活人，点击右侧「一键快速开战」0 秒直打敌帮守旗阵！",
                        24f, rowsStartY + 18f, 1220f, 52f, 22f, LabelBrown);
                }
                else
                {
                    for (int i = 0; i < Math.Min(maxTargetRows, ov.LiveTargets.Count); i++)
                    {
                        var t = ov.LiveTargets[i];
                        float ry = rowsStartY + i * 48f;
                        bool rival = t.IsRivalGuild || IsRivalGuild(ov.BannerHonor, t.GuildId);
                        if (rival)
                            Art(_body, "LiveTargetRivalBadge_" + i, "badge_rival_guild", 24f, ry + 4f, 34f, 34f, true);
                        string rowText = $"[{t.GuildName}] {t.PlayerName}  Lv.{t.Level}  出战：{t.PetName}  {t.PartySize}人"
                            + (t.Streak >= 3 ? $"  · {t.Streak}连胜" : string.Empty)
                            + (rival ? "  · 宿敌+20%" : string.Empty);
                        Label(_body, "LiveTargetRow_" + i, rowText, rival ? 64f : 24f, ry + 4f, rival ? 940f : 980f, 38f, 21f, BodyInk);
                        if (t.InBattle && t.BattleId != 0)
                        {
                            ulong bid = t.BattleId;
                            ActionButton(_body, "WatchLive_" + i, "观战", 1044f, ry, 206f, 42f,
                                () => WatchBattleRequested?.Invoke(bid), fontSize: 20f);
                        }
                        else
                        {
                            ulong tid = t.PlayerId;
                            uint lid = t.LeylineId;
                            ActionButton(_body, "FightLive_" + i, "直接开战", 1044f, ry, 206f, 42f,
                                () => _client.StartLivePvpBattle(tid, lid), danger: true, enabled: !busy, fontSize: 20f);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < Math.Min(maxTargetRows, ov.GarrisonSlots.Count); i++)
                {
                    var g = ov.GarrisonSlots[i];
                    float ry = rowsStartY + i * 48f;
                    Label(_body, "GarrisonPreview_" + i,
                        $"槽位 #{g.SlotIndex} · {g.PlayerName} (Lv.{g.Level}) · 出战宠：{g.PetName}",
                        24f, ry + 4f, 1220f, 38f, 21f, BodyInk);
                }
                if (ov.GarrisonSlots.Count == 0)
                    Label(_body, "NoGarrisonPreview", "本帮暂未登记守旗阵，可切至「守旗阵」页登记本人战力快照。", 24f, rowsStartY + 18f, 1220f, 48f, 22f, LabelBrown);
            }

            // 右下：0 秒一键快速开战（无匹配池）+ 上场约战荣光摘要 + 帮会商店直达
            Card(_body, "QuickActionCard", 1288f, 376f, 348f, 212f);
            string quickHint = !string.IsNullOrEmpty(ov.LastFeudSummary)
                ? ov.LastFeudSummary
                : "无匹配池 · 0 秒直打\n优先连在线活人，无活人直踢敌帮守旗阵";
            Label(_body, "QuickActionHint", quickHint,
                1308f, 390f, 308f, 64f, 18f, !string.IsNullOrEmpty(ov.LastFeudSummary) ? TitleJade : LabelBrown, wrap: true);
            ActionButton(_body, "QuickBattleBtn", "一键快速开战", 1308f, 464f, 308f, 62f,
                () => _client.StartQuickBattle(ov.GoldLeylineId), danger: true, enabled: !busy, fontSize: 26f);
            ActionButton(_body, "WarToShopBtn", "逛帮会商店（换硬货）", 1308f, 534f, 308f, 42f,
                () => { Hide(); OpenGuildShopRequested?.Invoke(); }, fontSize: 20f);
        }

        private void RenderGarrison()
        {
            var ov = _client.Overview;
            bool busy = _client.ActionInFlight || _client.OverviewLoading;
            Card(_body, "GarrisonHeaderCard", 0f, 0f, 1636f, 92f);
            Label(_body, "GarrisonHeaderTitle", "本帮五槽守旗阵 · 独立快照托管，不占用本人在线与战斗（刷新冷却 60 秒）",
                24f, 24f, 1080f, 42f, 24f, TitleJade);
            ActionButton(_body, "RegisterMyGarrisonBtn", "登记 / 更新我的守旗阵", 1140f, 18f, 300f, 56f,
                () => _client.RegisterMyGarrison(), primary: true, enabled: !busy, fontSize: 22f);
            ActionButton(_body, "RemoveMyGarrisonBtn", "本人下阵", 1456f, 18f, 156f, 56f,
                () => _client.RemoveGarrison(0UL), enabled: !busy, fontSize: 21f);

            var slots = ov?.GarrisonSlots;
            for (int i = 0; i < 5; i++)
            {
                float ry = 108f + i * 94f;
                Card(_body, "GarrisonSlotCard_" + i, 0f, ry, 1636f, 82f);
                GuildWarGarrisonSlotSnapshot slot = slots != null && i < slots.Count ? slots[i] : null;
                if (slot == null)
                {
                    Label(_body, "GarrisonEmpty_" + i, $"守旗槽 #{i + 1} · 空闲（点击右上角即可登记本人真实养成快照）",
                        28f, ry + 22f, 1200f, 38f, 22f, LabelBrown);
                    continue;
                }
                Label(_body, "GarrisonSlotText_" + i,
                    $"守旗槽 #{slot.SlotIndex} · {slot.PlayerName}   Lv.{slot.Level}   出战宠：{slot.PetName}",
                    28f, ry + 22f, 1080f, 38f, 23f, BodyInk);
                if (_isOfficerOrLeader)
                {
                    ulong pid = slot.PlayerId;
                    ActionButton(_body, "PromoteGarrison_" + i, "置顶", 1220f, ry + 18f, 176f, 46f,
                        () => _client.PromoteGarrison(pid), primary: true, enabled: !busy, fontSize: 21f);
                    ActionButton(_body, "KickGarrison_" + i, "移出槽位", 1412f, ry + 18f, 196f, 46f,
                        () => _client.RemoveGarrison(pid), danger: true, enabled: !busy, fontSize: 21f);
                }
            }
        }

        private void RenderBounty()
        {
            var ov = _client.Overview;
            bool busy = _client.ActionInFlight || _client.OverviewLoading;
            ulong nowMs = _client.EstimatedServerNowMs;

            string[] subTabs = { "本帮追杀令", "全区江湖榜", "帮会仇敌录 (24h)" };
            for (int i = 0; i < subTabs.Length; i++)
            {
                int idx = i;
                ActionButton(_body, "BountySubTab_" + i, subTabs[i], i * 268f, 0f, 252f, 48f,
                    () => { _bountyTab = idx; Render(); }, primary: _bountyTab == i, fontSize: 22f);
            }
            var stance = ov?.MyStance;
            Label(_body, "BountyDailyInfo",
                $"今日追杀/反杀赏金：{stance?.MyBountyPayoutsToday ?? 0}/{stance?.MyBountyPayoutLimit ?? 5} · 帮会公费单：{ov?.PublicBountyUsedToday ?? 0}/{ov?.PublicBountyDailyLimit ?? 2}",
                830f, 8f, 800f, 36f, 21f, LabelBrown, align: TextAlignmentOptions.MidlineRight);

            if (_bountyTab == 2)
            {
                var enemies = ov?.Enemies;
                if (enemies == null || enemies.Count == 0)
                {
                    Label(_body, "NoEnemiesLabel", "近 24 小时暂无异帮仇敌记录。", 24f, 160f, 1580f, 48f, 24f, LabelBrown, align: TextAlignmentOptions.Center);
                    return;
                }
                for (int i = 0; i < Math.Min(5, enemies.Count); i++)
                {
                    var e = enemies[i];
                    float ry = 64f + i * 102f;
                    Card(_body, "EnemyRow_" + i, 0f, ry, 1636f, 90f);
                    bool canFeudEnemy = _isOfficerOrLeader && ov != null && ov.ActiveFeud == null && e.GuildId != 0 && e.GuildId != _myGuildId;
                    Label(_body, "EnemyInfo_" + i,
                        $"[{e.GuildName}] {e.PlayerName}  击败本帮「{e.VictimName}」（{GuildWarText.BattleKindLabel(e.BattleKind)}）",
                        24f, ry + 24f, canFeudEnemy ? 1120f : 1280f, 42f, 23f, BodyInk);
                    if (canFeudEnemy)
                    {
                        ulong enemyGid = e.GuildId;
                        bool feudReady = !busy && ov.NextFeudIssueAtMs <= nowMs;
                        ActionButton(_body, "IssueFeudEnemy_" + i,
                            ov.NextFeudIssueAtMs > nowMs ? "战书冷却中" : "下2h战书",
                            1164f, ry + 20f, 174f, 50f,
                            () => _client.IssueFeud(enemyGid),
                            primary: feudReady, enabled: feudReady, fontSize: 21f);
                    }
                    ulong targetPid = e.PlayerId;
                    string targetName = e.PlayerName;
                    ActionButton(_body, "PostBountyForEnemy_" + i, e.CanPostBounty ? "发布追杀令" : "暂不可挂单",
                        1350f, ry + 20f, 256f, 50f,
                        () => OpenPostBountyModal(targetPid, targetName),
                        danger: e.CanPostBounty, enabled: !busy && e.CanPostBounty, fontSize: 22f);
                }
                return;
            }

            var filtered = new List<GuildWarBountySnapshot>();
            if (ov != null)
            {
                for (int i = 0; i < ov.Bounties.Count; i++)
                {
                    var b = ov.Bounties[i];
                    if (_bountyTab == 0 && _myGuildId != 0 && b.IssuerGuildId != _myGuildId) continue;
                    if (_bountyTab == 1 && !b.PublicBoard) continue;
                    filtered.Add(b);
                }
            }
            if (filtered.Count == 0)
            {
                Label(_body, "NoBountiesLabel", "当前榜单暂无生效中的江湖追杀令。", 24f, 160f, 1580f, 48f, 24f, LabelBrown, align: TextAlignmentOptions.Center);
                return;
            }
            for (int i = 0; i < Math.Min(4, filtered.Count); i++)
            {
                var b = filtered[i];
                float ry = 64f + i * 128f;
                Card(_body, "BountyRow_" + i, 0f, ry, 1636f, 114f);
                Art(_body, "BountyBadge_" + i, "badge_bounty_target", 18f, ry + 17f, 80f, 80f, true);
                string locText = !b.TargetOnline ? "暂未露面"
                    : b.TargetInSafeZone ? "藏身归元城安全区"
                    : b.LastSceneConfigId != 0 ? $"{GuildWarText.SceneName(b.LastSceneConfigId)} · {b.LastChannelNo}线" : "点击右侧追踪行踪";
                Label(_body, "BountyTitle_" + i,
                    $"[{b.TargetGuildName}] {b.TargetName} · {GuildWarText.BountyStatusLabel(b.Status)} · 赏金 {b.RewardSilver} 银两（发布：{b.IssuerGuildName}）",
                    114f, ry + 14f, 1060f, 38f, 23f, TitleJade);
                Label(_body, "BountySub_" + i,
                    $"行踪：{locText}  |  目标反杀进度：{b.CounterKills}/{b.CounterKillsToTear}（满即撕单没收50%）  |  剩余：{GuildWarText.FormatCountdown(b.ExpiresAtMs, nowMs)}",
                    114f, ry + 58f, 1080f, 38f, 20f, LabelBrown);

                ulong bid = b.BountyId;
                ulong tid = b.TargetPlayerId;
                uint trackSceneConf = b.LastSceneConfigId;
                ulong trackSceneId = b.LastSceneId;
                bool canTravel = b.TargetOnline && !b.TargetInSafeZone && trackSceneConf != 0;
                bool trackCooldown = b.NextTrackAtMs > nowMs;
                ActionButton(_body, "TrackBounty_" + i,
                    trackCooldown ? "冷却 " + GuildWarText.FormatCountdown(b.NextTrackAtMs, nowMs) : "追踪行踪",
                    1084f, ry + 32f, 168f, 50f, () => _client.TrackBounty(bid),
                    primary: !trackCooldown, enabled: !busy && !trackCooldown, fontSize: 19f);
                ActionButton(_body, "TravelBounty_" + i, "前往追击",
                    1262f, ry + 32f, 164f, 50f, () => TravelToSceneRequested?.Invoke(trackSceneConf, trackSceneId),
                    primary: canTravel, enabled: canTravel, fontSize: 19f);
                ActionButton(_body, "AttackBounty_" + i, "直打结单",
                    1436f, ry + 32f, 176f, 50f, () => _client.StartBountyBattle(bid, tid),
                    danger: true, enabled: !busy && b.CanDirectAttack, fontSize: 20f);
            }
        }

        private void RenderLogs()
        {
            var logs = _client.Overview?.RecentLogs;
            if (logs == null || logs.Count == 0)
            {
                Label(_body, "NoLogsLabel", "本帮暂无近 30 场帮战大事记。", 24f, 200f, 1580f, 48f, 24f, LabelBrown, align: TextAlignmentOptions.Center);
                return;
            }
            for (int i = 0; i < Math.Min(6, logs.Count); i++)
            {
                var log = logs[i];
                float ry = i * 96f;
                Card(_body, "WarLogRow_" + i, 0f, ry, 1636f, 84f);
                string deltaText = log.BannerHpDelta != 0 ? $"旗势 {(log.BannerHpDelta > 0 ? "+" : "")}{log.BannerHpDelta}" : "旗势不变";
                Label(_body, "WarLogText_" + i,
                    $"【{GuildWarText.BattleKindLabel(log.WarBattleKind)}】{log.AttackerGuildName} ({log.AttackerSummary}) VS {log.DefenderGuildName} ({log.DefenderSummary}) · {log.Rounds}回合 · {deltaText}",
                    24f, ry + 22f, 1340f, 40f, 21f, BodyInk);
                if (log.SelfParticipated && log.BattleId != 0)
                {
                    ulong bid = log.BattleId;
                    ActionButton(_body, "ReplayLog_" + i, "战报回放", 1396f, ry + 18f, 216f, 48f,
                        () => ReplayBattleRequested?.Invoke(bid), primary: true, fontSize: 21f);
                }
            }
        }

        private void RenderSettlement()
        {
            var s = _client.ActiveSettlement;
            if (s == null)
            {
                Label(_body, "NoSettlementLabel", "暂无帮战结算记录，完成一场帮战后将在此展示权威收益与连战入口。",
                    24f, 220f, 1580f, 56f, 24f, LabelBrown, align: TextAlignmentOptions.Center);
                return;
            }

            Card(_body, "SettlementMainCard", 120f, 18f, 1396f, 548f);
            string outcomeText = s.Outcome == 1 ? "胜 利" : s.Outcome == 2 ? "惜 败" : s.Outcome == 3 ? "平 局" : "已 结 束";
            Label(_body, "SettlementTitle",
                $"{outcomeText} · {GuildWarText.BattleKindLabel(s.WarBattleKind)} · 共 {s.Rounds} 回合" + (s.Settled ? "" : "（部分奖励确认中…）"),
                160f, 40f, 1316f, 52f, 34f, s.Outcome == 1 ? TitleJade : CrimsonText, align: TextAlignmentOptions.Center);

            if (s.BountyOutcomeTag != 0)
            {
                string jackpot = s.BountyOutcomeTag switch
                {
                    1 => $"终结敌方高连胜！额外悬赏 +{s.BountyJackpotContribution} 帮贡 / +{s.BountyJackpotSilver} 银两",
                    2 => $"追杀成功，替帮雪耻！领走托管赏金 +{s.BountyJackpotSilver} 银两",
                    3 => $"反杀追兵成功！获得反杀赏金 +{s.BountyJackpotSilver} 银两",
                    4 => $"连退追兵，撕毁追杀令！没收赏金 +{s.BountyJackpotSilver} 银两",
                    5 => $"破旗首功！击溃敌方灵脉战旗 +{s.BountyJackpotContribution} 帮贡 / +{s.BountyJackpotSilver} 银两",
                    _ => string.Empty,
                };
                if (!string.IsNullOrEmpty(jackpot))
                {
                    Art(_body, "SettlementJackpotBg", "tag_exclusive_bg", 200f, 102f, 1236f, 52f);
                    Label(_body, "SettlementJackpotText", jackpot, 220f, 106f, 1196f, 44f, 24f, LightText, align: TextAlignmentOptions.Center);
                }
            }

            string matLine = s.MaterialPvpLossIneligible
                ? "660 首套装备材料：+0（未达 3 回合 / 2 手动 / 有效贡献防挂机门槛，不扣 660 额度）"
                : $"660 首套装备材料：+{s.MaterialUnitsAdded} 份（含可交易 {s.MaterialTradableAdded} 份）";
            string gainLine = $"帮战即时收益：帮贡 +{s.ContributionGain}   银两 +{s.SilverGain}   帮会资金 +{s.GuildFundsGain}   周战功 +{s.MeritGain}   当前连胜：{s.StreakAfter}";
            string leyLine = s.LeylineId == 0
                ? "灵脉旗势：本局不涉及灵脉旗势变化"
                : s.BannerShielded
                    ? $"{GuildWarText.LeylineName(s.LeylineId)}：整备护盾保护中（旗势不变）"
                    : $"{GuildWarText.LeylineName(s.LeylineId)}：旗势 {s.BannerHpBefore} → {s.BannerHpAfter}";
            string diminishLine = s.DiminishPercent < 100
                ? $"防互刷提示：与同对手 30 分钟内多次交锋，本场帮战收益按 {s.DiminishPercent}% 结算，换对手立即恢复 100%。"
                : (s.GoldLeylineBonus ? "已享 1.5x 金脉热点加成。" : "")
                  + (s.FeudBonus ? " 已享 2 小时宿敌约战 +25% 收益与前 3 场免递减！" : (s.RivalryBonus ? " 已享宿敌 +20% 加成。" : ""));

            Label(_body, "SettlementMatLine", matLine, 180f, 178f, 1276f, 44f, 23f, BodyInk);
            Label(_body, "SettlementGainLine", gainLine, 180f, 236f, 1276f, 44f, 23f, BodyInk);
            Label(_body, "SettlementLeyLine", leyLine, 180f, 294f, 1276f, 44f, 23f, TitleJade);
            if (!string.IsNullOrEmpty(diminishLine))
                Label(_body, "SettlementDiminishLine", diminishLine, 180f, 352f, 1276f, 44f, 21f, CrimsonText);

            ActionButton(_body, "SettlementContinueWar", "继续开战（0秒连战）", 180f, 466f, 340f, 64f,
                () => _client.StartQuickBattle(s.LeylineId), danger: true, enabled: !_client.ActionInFlight, fontSize: 25f);
            if (s.CanPostBounty && s.BountyCandidatePlayerId != 0)
            {
                ulong candId = s.BountyCandidatePlayerId;
                string candName = s.BountyCandidateName;
                ActionButton(_body, "SettlementPostBounty", "悬赏追杀对手", 550f, 466f, 280f, 64f,
                    () => OpenPostBountyModal(candId, candName), primary: true, fontSize: 24f);
            }
            ActionButton(_body, "SettlementOpenShop", "逛帮会商店", 860f, 466f, 260f, 64f,
                () => { Hide(); OpenGuildShopRequested?.Invoke(); }, fontSize: 23f);
            ActionButton(_body, "SettlementBackMain", "返回帮战主页", 1150f, 466f, 280f, 64f,
                () => ShowPage(GuildWarPage.Main), fontSize: 23f);
        }

        private void RenderFieldHud()
        {
            var ov = _client.Overview;
            var stance = ov?.MyStance;
            bool peace = stance?.PeaceMode ?? false;

            Card(_body, "FieldHudSummaryCard", 0f, 0f, 1636f, 146f);
            Art(_body, "FieldHudFlag", "banner_guild_flag", 24f, 12f, 84f, 122f, true);
            Art(_body, "FieldHudStance", peace ? "badge_stance_peace" : "badge_stance_war", 128f, 24f, 96f, 96f, true);
            Art(_body, "FieldHudFrenzy", "badge_frenzy_round12", 240f, 24f, 96f, 96f, true);
            Art(_body, "FieldHudTagBg", "tag_exclusive_bg", 356f, 22f, 180f, 46f);
            Label(_body, "FieldHudTagText", "帮战限定", 364f, 24f, 164f, 42f, 21f, LightText, align: TextAlignmentOptions.Center);
            Label(_body, "FieldHudRules",
                "野外三图（八景岛 / 碧渊水宫 / 月汐湾）路上任意异帮点选直打；归元城永久安全区；第 12 回合起触发「决胜煞气」防拖局。",
                356f, 76f, 1240f, 54f, 21f, BodyInk, wrap: true);

            var targets = _client.FieldTargets;
            if (targets == null || targets.Count == 0)
            {
                Label(_body, "NoFieldTargets", "当前视野内暂无扫描到的异帮玩家（进入八景岛 / 碧渊水宫 / 月汐湾后由场景自动巡检同步）。",
                    24f, 240f, 1580f, 52f, 23f, LabelBrown, align: TextAlignmentOptions.Center);
                return;
            }
            for (int i = 0; i < Math.Min(4, targets.Count); i++)
            {
                var t = targets[i];
                float ry = 166f + i * 102f;
                Card(_body, "FieldTargetRow_" + i, 0f, ry, 1636f, 88f);
                if (t.ActiveBountyId != 0) Art(_body, "FieldBountyMark_" + i, "badge_bounty_target", 16f, ry + 12f, 64f, 64f, true);
                else if (t.InBattle) Art(_body, "FieldFightMark_" + i, "badge_spectate_swords", 16f, ry + 12f, 64f, 64f, true);
                else if (t.Streak >= 3) Art(_body, "FieldStreakMark_" + i, StreakBadgeKey(t.Streak), 16f, ry + 12f, 64f, 64f, true);

                if (t.IsRivalGuild)
                    Art(_body, "FieldRivalBadge_" + i, "badge_rival_guild", 1288f, ry + 18f, 52f, 52f, true);

                Label(_body, "FieldTargetText_" + i,
                    $"[{t.GuildName}] {t.PlayerName}" + (t.PeaceMode ? " · 和平免战" : " · 战意")
                    + (t.IsRivalGuild ? " · 宿敌+20%" : string.Empty)
                    + (t.ActiveBountyId != 0 ? $" · 追杀赏金 {t.ActiveBountyRewardSilver} 银两" : ""),
                    96f, ry + 22f, t.IsRivalGuild ? 1176f : 1236f, 42f, 23f, BodyInk);

                if (t.InBattle && t.BattleId != 0)
                {
                    ulong bid = t.BattleId;
                    ActionButton(_body, "FieldWatchBtn_" + i, "实时观战", 1360f, ry + 18f, 240f, 52f,
                        () => WatchBattleRequested?.Invoke(bid), primary: true, fontSize: 22f);
                }
                else
                {
                    ulong pid = t.PlayerId;
                    ulong bountyId = t.ActiveBountyId;
                    ActionButton(_body, "FieldAttackBtn_" + i, bountyId != 0 ? "执行追杀" : "发起帮战",
                        1360f, ry + 18f, 240f, 52f, () => _client.AttackFieldPlayer(pid, bountyId),
                        danger: true, enabled: t.CanAttackNow && !_client.ActionInFlight, fontSize: 22f);
                }
            }
        }

        private void OpenPostBountyModal(ulong targetPlayerId, string targetName)
        {
            CloseModal();
            var frame = OpenModal("发布江湖追杀令 · " + targetName,
                "仅开放三档固定悬赏（银两单收 10% 销毁税，同目标同时限 1 单，24 小时过期退本金不退税，目标连反杀 2 人撕单没收 50%）。");

            GuildWarBountyTier[] tiers =
            {
                GuildWarBountyTier.NormalSilver,
                GuildWarBountyTier.HeavySilver,
                GuildWarBountyTier.GuildPublic,
            };
            for (int i = 0; i < tiers.Length; i++)
            {
                var tier = tiers[i];
                bool allowed = tier != GuildWarBountyTier.GuildPublic || _isOfficerOrLeader;
                ActionButton(frame, "BountyTier_" + tier, GuildWarText.BountyTierLabel(tier),
                    80f, 184f + i * 64f, 920f, 52f,
                    () => { _selectedBountyTier = tier; OpenPostBountyModal(targetPlayerId, targetName); },
                    primary: _selectedBountyTier == tier, enabled: allowed, fontSize: 22f);
            }

            ActionButton(frame, "ToggleBountyPublic", _selectedBountyPublic ? "公开范围：全区江湖榜（已勾选）" : "公开范围：仅本帮追杀榜",
                80f, 386f, 480f, 50f,
                () => { _selectedBountyPublic = !_selectedBountyPublic; OpenPostBountyModal(targetPlayerId, targetName); },
                primary: _selectedBountyPublic, fontSize: 21f);

            ActionButton(frame, "ConfirmPostBounty", "确认发令", 620f, 460f, 360f, 64f,
                () => { CloseModal(); _client?.PostBounty(targetPlayerId, _selectedBountyTier, _selectedBountyPublic); },
                danger: true, fontSize: 25f);
            ActionButton(frame, "CancelPostBounty", "取消", 80f, 460f, 240f, 64f, CloseModal, fontSize: 24f);
        }

        private RectTransform OpenModal(string title, string subtitle)
        {
            CloseModal();
            _modal = QdaoUguiFactory.CreateStretch("GuildWarModal", _root, Vector4.zero);
            var dim = _modal.gameObject.AddComponent<Image>();
            dim.color = DeepBackdrop;
            dim.raycastTarget = true;
            var frame = QdaoUguiFactory.CreateCenteredRect("GuildWarModalFrame", _modal, 1080f, 560f);
            Card(frame, "GuildWarModalBg", 0f, 0f, 1080f, 560f, PaperFill);
            Label(frame, "GuildWarModalTitle", title, 64f, 30f, 952f, 50f, 34f, TitleJade);
            Label(frame, "GuildWarModalSub", subtitle, 64f, 92f, 952f, 76f, 21f, LabelBrown, wrap: true);
            return frame;
        }

        private void CloseModal()
        {
            if (_modal == null) return;
            _modal.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_modal.gameObject);
            else UnityEngine.Object.DestroyImmediate(_modal.gameObject);
            _modal = null;
        }

        private static bool IsRivalGuild(GuildWarBannerHonorSnapshot honor, ulong guildId)
        {
            if (honor == null || guildId == 0 || honor.RivalGuildIds == null) return false;
            for (int i = 0; i < honor.RivalGuildIds.Count; i++)
                if (honor.RivalGuildIds[i] == guildId) return true;
            return false;
        }
    }
}
