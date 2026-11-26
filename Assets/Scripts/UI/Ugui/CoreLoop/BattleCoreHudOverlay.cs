using System.Collections.Generic;
using MmorpgClient.Game;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>
    /// 战斗内公开预告、首领试炼 V2 机制条、战意渐盛、物法易势和护卫保全的纯只读轻量叠加层（零 raycast，绝不拦截舞台点选与确认按钮）。
    /// 顶部条紧贴计时环之下（y=122+，x=760..1860，避开左上回合/记录与右上角色卡/胶囊）；
    /// 宝宝一招提示置左下角（x=36..668, y=1010..1062，避开居中 Confirm/Cancel 目标确认条与右下命令环）。
    /// </summary>
    public sealed class BattleCoreHudOverlay : MonoBehaviour
    {
        private CoreGameplayLoopClient _core;
        private GameClient _game;
        private uint _lastEscalationStacks;
        private BossMechanicBannerView _activeMechanicBanner;
        private float _mechanicBannerUntil;
        private RectTransform _escalationPulseRoot;
#if UNITY_WEBGL && !UNITY_EDITOR
        private const float CapsuleTopPixels = 76f;
        private const float CapsuleRightPixels = 140f;
        private RectTransform _capsuleSafeTopBanners;
        private Canvas _overlayCanvas;
#endif

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _core = core;
            _game = game;
            _lastEscalationStacks = 0u;
            _activeMechanicBanner = null;
            Refresh();
            Hide();
        }

        public void Show() { if (IsVisible) return; gameObject.SetActive(true); Refresh(); }
        public void Hide()
        {
            _activeMechanicBanner = null;
            _lastEscalationStacks = 0u;
            KillEscalationPulse();
            BattleUiRoot.Instance?.ActiveBattleScreen?.SetCoreLoopActorMarkers((IReadOnlyList<ulong>)null, 0ul);
            gameObject.SetActive(false);
        }

        private void KillEscalationPulse()
        {
            if (_escalationPulseRoot == null) return;
            RealtimeTween.Kill(_escalationPulseRoot.gameObject);
            _escalationPulseRoot = null;
        }

        public void ShowMechanicBanner(BossMechanicBannerView banner)
        {
            if (banner == null || string.IsNullOrEmpty(banner.Title)) return;
            _activeMechanicBanner = banner;
            _mechanicBannerUntil = Time.unscaledTime + 2.2f;
            if (IsVisible) Refresh();
        }

        private void Update()
        {
            if (_activeMechanicBanner != null && Time.unscaledTime >= _mechanicBannerUntil)
            {
                _activeMechanicBanner = null;
                if (IsVisible) Refresh();
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void LateUpdate() => ApplyCapsuleAvoidance();

        private void ApplyCapsuleAvoidance()
        {
            if (_capsuleSafeTopBanners == null || _overlayCanvas == null) return;
            var layout = (RectTransform)transform;
            var bounds = layout.rect;
            // 上界包含战意渐盛脉冲放大预留，右界包含最宽的首领状态条。
            var topRight = layout.TransformPoint(new UnityEngine.Vector3(bounds.xMin + 1860f, bounds.yMax - 118f));
            var screenTopRight = RectTransformUtility.WorldToScreenPoint(null, topRight);
            var pixels = _overlayCanvas.pixelRect;
            float offset = 0f;
            if (screenTopRight.x > pixels.xMax - CapsuleRightPixels
                && screenTopRight.y > pixels.yMax - CapsuleTopPixels)
            {
                var belowCapsule = new Vector2(screenTopRight.x, pixels.yMax - CapsuleTopPixels);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(layout, belowCapsule, null, out var localBottom))
                    offset = Mathf.Max(0f, bounds.yMax - 118f - localBottom.y);
            }
            _capsuleSafeTopBanners.anchoredPosition = new Vector2(0f, -offset);
        }
#endif

        public void Refresh()
        {
            if (_core == null) return;
            var hud = _core.BuildBattleHud();
            KillEscalationPulse();
            CoreLoopUiArt.Clear(transform);
            if (hud == null) return;
            var targets = new List<ulong>(4);
            if (hud.Telegraphs != null)
            {
                for (int i = 0; i < hud.Telegraphs.Count; ++i)
                {
                    var entry = hud.Telegraphs[i];
                    if (entry == null || entry.Status == 4u || entry.Status == 5u) continue;
                    if (entry.TargetActorIds != null && entry.TargetActorIds.Count > 0)
                    {
                        for (int t = 0; t < entry.TargetActorIds.Count; ++t)
                        {
                            ulong tid = entry.TargetActorIds[t];
                            if (tid != 0ul && !targets.Contains(tid)) targets.Add(tid);
                        }
                    }
                    else if (entry.TargetActorId != 0ul && !targets.Contains(entry.TargetActorId))
                    {
                        targets.Add(entry.TargetActorId);
                    }
                }
            }
            if (IsVisible)
                BattleUiRoot.Instance?.ActiveBattleScreen?.SetCoreLoopActorMarkers(
                    targets, hud.ProtectedGuardActorId);

            // 顶部信息带继承战斗画布安全区，在计时环下方及两列角色信息之间排列。
            var topBanners = transform;
#if UNITY_WEBGL && !UNITY_EDITOR
            _capsuleSafeTopBanners = QdaoUguiFactory.CreateStretch("BattleTopBanners", transform, Vector4.zero);
            topBanners = _capsuleSafeTopBanners;
            if (_overlayCanvas == null) _overlayCanvas = GetComponentInParent<Canvas>();
            ApplyCapsuleAvoidance();
#endif
            float nextY = 122f;
            var liveState = _game?.Battle?.State;
            if (liveState != null)
            {
                // 五行阵法 / 五行幻境层数 / 自定义切磋规则 紧凑状态带（零 raycast）
                var wuxingTags = new List<string>(4);
                if (liveState.TeamAFormationId > 0u || liveState.TeamBFormationId > 0u)
                {
                    ulong myId = _game.Battle.MyPlayerId;
                    uint myTeam = 0u;
                    for (int i = 0; i < liveState.Actors.Count; ++i)
                    {
                        if (liveState.Actors[i].ActorId == myId) { myTeam = liveState.Actors[i].TeamIndex; break; }
                    }
                    uint myForm = myTeam == 0u ? liveState.TeamAFormationId : liveState.TeamBFormationId;
                    uint enemyForm = myTeam == 0u ? liveState.TeamBFormationId : liveState.TeamAFormationId;
                    string counter = WuxingExpansionModels.FormationCounterLabel(myForm, enemyForm);
                    string fText = $"阵法：{WuxingExpansionModels.FormationName(myForm)} vs {WuxingExpansionModels.FormationName(enemyForm)}";
                    if (!string.IsNullOrEmpty(counter)) fText += $"（{counter}）";
                    wuxingTags.Add(fText);
                }
                if (liveState.TowerContext != null && liveState.TowerContext.Floor > 0u)
                {
                    string ley = WuxingExpansionModels.ElementName((uint)liveState.TowerContext.LeylineElement);
                    wuxingTags.Add($"五行幻境 · 第{liveState.TowerContext.Floor}层 · [{ley}脉] · {liveState.TowerContext.ActiveSigilIds.Count}符印");
                }
                if (liveState.CustomDuelRules != null)
                {
                    wuxingTags.Add("切磋坊 · " + WuxingExpansionModels.FormatCustomDuelRulesSummary(liveState.CustomDuelRules));
                }
                if ((liveState.TrialVowMask & MmorpgClient.Game.TrialJournal.TrialJournalModels.VowMaskAll) != 0u)
                {
                    wuxingTags.Add("试炼誓约 · " + MmorpgClient.Game.TrialJournal.TrialJournalModels.FormatVowSummary(liveState.TrialVowMask));
                }
                if (wuxingTags.Count > 0)
                {
                    CoreLoopUiArt.Image("WuxingBattleInfoPlate", topBanners, 760f, nextY, 1080f, 38f,
                        "hud-strip", new Color(0.94f, 0.93f, 0.84f, 0.92f), raycast: false);
                    var infoLbl = CoreLoopUiArt.Text("WuxingBattleInfoText", topBanners, 776f, nextY + 2f, 1048f, 34f,
                        string.Join("   |   ", wuxingTags), 18f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
                    infoLbl.enableAutoSizing = true;
                    infoLbl.fontSizeMin = 13f;
                    infoLbl.fontSizeMax = 18f;
                    nextY += 42f;
                }
                if (liveState.WuxingSynergyActiveThisRound)
                {
                    CoreLoopUiArt.Image("WuxingSynergyBanner", topBanners, 880f, nextY, 800f, 40f,
                        "hud-strip", new Color(0.13f, 0.44f, 0.32f, 0.94f), raycast: false);
                    CoreLoopUiArt.Text("WuxingSynergyText", topBanners, 896f, nextY + 2f, 768f, 36f,
                        "★ 人宠五行相生合击触发 · 本回合伤害／治疗／护盾 +10% ★",
                        19f, SeasonV1UiArt.BadgeIvoryText, true, false, TextAlignmentOptions.Center);
                    nextY += 44f;
                }
            }
            if (hud.IsBossTrial)
            {
                var phaseSprite = hud.IsBossPhaseTwo ? CoreLoopUiArt.LoadV2("banner_boss_phase2") : null;
                float statusX = 1022f;
                float statusY = nextY;
                if (phaseSprite != null)
                {
                    SeasonEconomyUiArt.CreateSlicedOrSimple(topBanners, "BossPhaseBadgePlate", 772f, nextY, 314f, 56f,
                        phaseSprite, CoreLoopUiArt.Warning, raycast: false);
                    var phaseLbl = CoreLoopUiArt.Text("BossPhaseBadge", topBanners, 824f, nextY + 6f, 210f, 44f,
                        "二阶段 · 破釜决战", 22f, CoreLoopUiArt.Ivory, true, false, TextAlignmentOptions.Center);
                    phaseLbl.enableAutoSizing = true;
                    phaseLbl.fontSizeMin = 15f;
                    phaseLbl.fontSizeMax = 22f;
                    statusX = 1096f;
                    statusY += 8f;
                }
                else
                {
                    string phaseLabel = CoreGameplayLoopModels.IsWildBossTrialKind(hud.BossTrialKind)
                        ? (hud.BossTrialKind == CoreGameplayLoopModels.BossTrialKindWildWind ? "八景岛 · 明雷"
                            : (hud.BossTrialKind == CoreGameplayLoopModels.BossTrialKindWildBell ? "碧渊水宫 · 明雷" : "月汐湾 · 明雷"))
                        : (hud.IsBossPhaseTwo ? "二阶段 · 决战姿态" : "一阶段");
                    CoreLoopUiArt.Badge(topBanners, "BossPhaseBadge", 772f, nextY, 240f,
                        phaseLabel, hud.IsBossPhaseTwo ? CoreLoopUiArt.Warning : CoreLoopUiArt.DeepJade);
                }
                var chips = new List<string>(4);
                if (hud.HasBossWeeklyAffix)
                    chips.Add(hud.BossWeeklyAffix.ShortChipText);
                if (hud.IsBossExposed)
                    chips.Add($"破绽 +{hud.BossExposedPct}%（至 R{hud.BossExposedUntilRound} 末）");
                if (hud.BossMomentumStacks > 0u)
                    chips.Add($"蓄势 {hud.BossMomentumStacks}/3（可驱散）");
                if (hud.HasBossStance)
                {
                    string stanceName = hud.BossStanceType == 1u ? "物防联防阵" : "法防联防阵";
                    chips.Add($"{stanceName} · 余 {hud.BossStanceRemainRounds} 回合（可驱散）");
                }
                string summary = chips.Count > 0 ? string.Join("   |   ", chips) : hud.BossStatusSummaryText;
                if (!string.IsNullOrEmpty(summary))
                {
                    CoreLoopUiArt.Image("BossStatusPlate", topBanners, statusX, statusY, 1858f - statusX, 40f,
                        "hud-strip", hud.IsBossExposed
                            ? new Color(.99f, .92f, .72f, .94f)
                            : new Color(.94f, .92f, .84f, .92f), raycast: false);
                    var statusIcon = hud.IsBossExposed
                        ? SeasonEconomyUiArt.LoadBossTrialV2Icon("icon_exposed")
                        : hud.BossMomentumStacks > 0u
                            ? SeasonEconomyUiArt.LoadBossTrialV2Icon("icon_momentum")
                            : hud.HasBossWeeklyAffix
                                ? CoreLoopUiArt.LoadV2("icon_weekly_affix_" + hud.BossWeeklyAffix.ElementKey)
                                : null;
                    float statusTextX = statusX + 14f;
                    if (statusIcon != null)
                    {
                        var iconImg = QdaoUguiFactory.CreateImage("BossStatusIcon", topBanners, statusX + 8f, statusY + 6f,
                            28f, 28f, statusIcon, false);
                        iconImg.preserveAspect = true;
                        statusTextX = statusX + 42f;
                    }
                    var chipLbl = CoreLoopUiArt.Text("BossStatusText", topBanners, statusTextX, statusY + 2f, 1844f - statusTextX, 36f,
                        summary, 18f, hud.IsBossExposed ? SeasonEconomyUiArt.GoldHighlight : CoreLoopUiArt.DeepJade,
                        true, false, TextAlignmentOptions.MidlineLeft);
                    chipLbl.enableAutoSizing = true;
                    chipLbl.fontSizeMin = 13f;
                    chipLbl.fontSizeMax = 18f;
                }
                nextY += phaseSprite != null ? 62f : 46f;
            }

            if (_activeMechanicBanner != null && Time.unscaledTime < _mechanicBannerUntil)
            {
                bool pos = _activeMechanicBanner.IsPositiveForPlayer;
                var mechanicSprite = pos ? CoreLoopUiArt.LoadV2("banner_boss_exposed") : null;
                float bannerHeight = mechanicSprite != null ? 56f : 44f;
                float bannerInset = mechanicSprite != null ? 72f : 16f;
                if (mechanicSprite != null)
                {
                    SeasonEconomyUiArt.CreateSlicedOrSimple(topBanners, "BossMechanicBack", 860f, nextY, 840f, bannerHeight,
                        mechanicSprite, CoreLoopUiArt.Ivory, raycast: false);
                }
                else
                {
                    CoreLoopUiArt.Image("BossMechanicBack", topBanners, 860f, nextY, 840f, bannerHeight,
                        "hud-strip", pos ? new Color(.98f, .92f, .71f, .95f) : new Color(.94f, .78f, .75f, .95f),
                        raycast: false);
                }
                string mechIconKey = _activeMechanicBanner.MechanicKind switch
                {
                    1u or 2u or 3u => "icon_exposed",
                    6u => "icon_momentum",
                    7u => "icon_sunder",
                    9u => "icon_dishearten",
                    _ => null,
                };
                var mechIcon = string.IsNullOrEmpty(mechIconKey)
                    ? null
                    : SeasonEconomyUiArt.LoadBossTrialV2Icon(mechIconKey);
                float bannerTextX = 860f + bannerInset;
                if (mechIcon != null)
                {
                    var mImg = QdaoUguiFactory.CreateImage("BossMechanicIcon", topBanners,
                        bannerTextX - 4f, nextY + (bannerHeight - 28f) * .5f, 28f, 28f, mechIcon, false);
                    mImg.preserveAspect = true;
                    bannerTextX += 30f;
                }
                string bannerText = string.IsNullOrEmpty(_activeMechanicBanner.Subtitle)
                    ? _activeMechanicBanner.Title
                    : $"{_activeMechanicBanner.Title} · {_activeMechanicBanner.Subtitle}";
                var bannerLbl = CoreLoopUiArt.Text("BossMechanicText", topBanners, bannerTextX, nextY + 2f,
                    1700f - bannerInset - bannerTextX, bannerHeight - 4f, bannerText, 19f,
                    mechanicSprite != null ? CoreLoopUiArt.DeepJade : pos ? SeasonEconomyUiArt.GoldHighlight : CoreLoopUiArt.Warning,
                    true, false, TextAlignmentOptions.Center);
                bannerLbl.enableAutoSizing = true;
                bannerLbl.fontSizeMin = 14f;
                bannerLbl.fontSizeMax = 19f;
                nextY += bannerHeight + 6f;
            }

            var battle = _game?.Battle;
            uint stacks = battle?.EscalationStacks ?? 0u;
            uint escStart = battle?.EscalationStartRound ?? 0u;
            bool nextRoundStartsEsc = stacks == 0u && escStart > 0u && hud.RoundIndex + 1u == escStart;
            if (stacks > 0u || nextRoundStartsEsc)
            {
                bool stackedUp = stacks > _lastEscalationStacks && stacks > 0u;
                _lastEscalationStacks = stacks;
                uint dmgPct = stacks * 10u;
                uint healCut = stacks * 10u > 80u ? 80u : stacks * 10u;
                string escTitle = stacks > 0u ? $"战意渐盛 · 第 {stacks} 层" : $"下一回合（第 {escStart} 回合）开启「战意渐盛」";
                string escSub = stacks > 0u ? $"伤害 +{dmgPct}% · 治疗与新盾 -{healCut}%" : "伤害每回合 +10% · 治疗与新盾 -10%";
                var fireSprite = CoreLoopUiArt.LoadV2("banner_escalation_fire");
                var escSprite = fireSprite ?? SeasonEconomyUiArt.LoadRankedPlayV2Control("escalation-badge-base")
                    ?? SeasonEconomyUiArt.LoadBattleArenaControl("escalation-cinnabar");
                RectTransform pulseRoot;
                if (fireSprite != null)
                {
                    const float width = 600f;
                    const float height = 60f;
                    var container = QdaoUguiFactory.CreateRect("EscalationGroup", topBanners, 980f, nextY, width, height);
                    container.pivot = new Vector2(.5f, .5f);
                    container.anchoredPosition = new Vector2(1280f, -(nextY + height * .5f));
                    SeasonEconomyUiArt.CreateSlicedOrSimple(container, "EscalationBanner", 0f, 0f, width, height,
                        fireSprite, CoreLoopUiArt.Warning, raycast: false);
                    var escTitleLbl = CoreLoopUiArt.Text("EscalationTitle", container, 68f, 7f, 464f, 25f,
                        escTitle, 20f, CoreLoopUiArt.Ivory, true, false, TextAlignmentOptions.Center);
                    escTitleLbl.enableAutoSizing = true;
                    escTitleLbl.fontSizeMin = 14f;
                    escTitleLbl.fontSizeMax = 20f;
                    var escSubLbl = CoreLoopUiArt.Text("EscalationSub", container, 68f, 32f, 464f, 20f,
                        escSub, 16f, CoreLoopUiArt.Ivory, false, false, TextAlignmentOptions.Center);
                    escSubLbl.enableAutoSizing = true;
                    escSubLbl.fontSizeMin = 12f;
                    escSubLbl.fontSizeMax = 16f;
                    pulseRoot = container;
                    nextY += height + 6f;
                }
                else if (escSprite != null)
                {
                    // 优先使用 ranked-play-v2-ui-20261125 低光污 escalation-badge-base.png（550:110 原比例 -> 490x98 居中），全程 raycastTarget=false。
                    const float ex = 1035f;
                    const float ew = 490f;
                    const float eh = 98f;
                    var container = QdaoUguiFactory.CreateRect("EscalationGroup", topBanners, ex, nextY, ew, eh);
                    container.pivot = new Vector2(0.5f, 0.5f);
                    container.anchoredPosition = new Vector2(ex + ew * 0.5f, -(nextY + eh * 0.5f));
                    var escImg = QdaoUguiFactory.CreateImage("EscalationBanner", container, 0f, 0f, ew, eh, escSprite, false);
                    escImg.type = UnityEngine.UI.Image.Type.Simple;
                    escImg.color = Color.white;
                    var titleLbl = CoreLoopUiArt.Text("EscalationTitle", container, 78f, 16f, 392f, 34f,
                        escTitle, 22f, stacks > 0u ? SeasonEconomyUiArt.BattleArenaVermilion : SeasonEconomyUiArt.BattleArenaJade,
                        true, false, TextAlignmentOptions.Center);
                    titleLbl.enableAutoSizing = true; titleLbl.fontSizeMin = 14f; titleLbl.fontSizeMax = 22f;
                    var subLbl = CoreLoopUiArt.Text("EscalationSub", container, 78f, 52f, 392f, 28f,
                        escSub, 17f, SeasonEconomyUiArt.BattleArenaInk, false, false, TextAlignmentOptions.Center);
                    subLbl.enableAutoSizing = true; subLbl.fontSizeMin = 12f; subLbl.fontSizeMax = 17f;
                    pulseRoot = container;
                    nextY += eh + 6f;
                }
                else
                {
                    string escText = $"{escTitle}（{escSub}）";
                    var escStrip = CoreLoopUiArt.Image("EscalationBanner", topBanners, 900f, nextY, 760f, 40f,
                        "hud-strip", stacks > 0u ? new Color(.97f, .86f, .72f, .94f) : new Color(.95f, .92f, .83f, .9f),
                        raycast: false);
                    var escLbl = CoreLoopUiArt.Text("EscalationText", topBanners, 916f, nextY + 2f, 728f, 36f,
                        escText, 19f, stacks > 0u ? CoreLoopUiArt.Warning : CoreLoopUiArt.DeepJade,
                        true, false, TextAlignmentOptions.Center);
                    escLbl.enableAutoSizing = true; escLbl.fontSizeMin = 13f; escLbl.fontSizeMax = 19f;
                    pulseRoot = escStrip != null ? escStrip.rectTransform : null;
                    nextY += 46f;
                }
                if (stackedUp && pulseRoot != null)
                {
                    // 层数真实递增时触发一次 0.18s 1->1.08->1 局部缩放脉冲，同层快照刷新不重播。
                    var rt = pulseRoot;
                    _escalationPulseRoot = rt;
                    RealtimeTween.To(0f, 1f, 0.18f).SetEase(RealtimeEase.SineInOut)
                        .SetIgnoreEngineTimeScale(true).SetTarget(rt.gameObject)
                        .OnUpdate((RealtimeTweenCallback1)(t =>
                        {
                            if (rt == null) return;
                            float s = 1f + 0.08f * Mathf.Sin(t.Value.x * Mathf.PI);
                            rt.localScale = new UnityEngine.Vector3(s, s, 1f);
                        }))
                        .OnComplete((RealtimeTweenCallback)(() =>
                        {
                            if (rt != null) rt.localScale = UnityEngine.Vector3.one;
                        }));
                }
            }
            else
            {
                _lastEscalationStacks = 0u;
            }
            uint weeklyAffixId = battle?.State?.SeasonWeeklyAffixId ?? 0u;
            if (weeklyAffixId != 0u)
            {
                string affixName = MmorpgClient.Game.Season.SeasonModels.DescribeWeeklyAffixShort(weeklyAffixId);
                string affixDetail = MmorpgClient.Game.Season.SeasonModels.DescribeWeeklyAffixDetail(weeklyAffixId);
                if (!string.IsNullOrEmpty(affixName))
                {
                    CoreLoopUiArt.Image("SeasonWeeklyAffixHudBack", topBanners, 960f, nextY, 640f, 34f,
                        "hud-strip", new Color(.89f, .95f, .91f, .92f), raycast: false);
                    var affixLbl = CoreLoopUiArt.Text("SeasonWeeklyAffixHudText", topBanners, 976f, nextY + 2f, 608f, 30f,
                        $"本周环境 · {affixName}（{affixDetail}）", 17f, CoreLoopUiArt.DeepJade,
                        true, false, TextAlignmentOptions.Center);
                    affixLbl.enableAutoSizing = true; affixLbl.fontSizeMin = 12f; affixLbl.fontSizeMax = 17f;
                    nextY += 40f;
                }
            }
            if (hud.HasPolarity)
            {
                bool phys = hud.ActivePolarity == 1u;
                var bannerSprite = SeasonEconomyUiArt.LoadControl(phys ? "polarity_banner_phys" : "polarity_banner_magic");
                var badgeSprite = SeasonEconomyUiArt.LoadIcon(phys ? "badge_polarity_phys" : "badge_polarity_magic");
                if (bannerSprite != null)
                {
                    // 按 561:133 原比例绘制阴阳双势横幅，不拉伸端饰；副条放完整换势说明，确保不裁字。
                    SeasonEconomyUiArt.CreateSlicedOrSimple(topBanners, "PolarityBanner", 1040f, nextY, 480f, 114f,
                        bannerSprite, new Color(.91f, .88f, .73f, .92f), raycast: false);
                    if (badgeSprite != null)
                    {
                        var icon = QdaoUguiFactory.CreateImage("PolarityBadgeIcon", topBanners, 1086f, nextY + 33f, 48f, 48f,
                            badgeSprite, false);
                        icon.preserveAspect = true;
                    }
                    var shortTitle = CoreLoopUiArt.Text("PolarityShort", topBanners, 1152f, nextY + 34f, 256f, 46f,
                        phys ? "物势当令 · 物伤 +15%" : "法势当令 · 法伤 +15%", 22f,
                        phys ? CoreLoopUiArt.Warning : CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
                    shortTitle.enableAutoSizing = true;
                    shortTitle.fontSizeMin = 15f;
                    shortTitle.fontSizeMax = 22f;

                    SeasonEconomyUiArt.CreateSlicedOrSimple(topBanners, "PolarityDetailPlate", 920f, nextY + 110f, 720f, 40f,
                        SeasonEconomyUiArt.LoadControl("notice_banner_soft"), new Color(.96f, .93f, .84f, .93f), raycast: false);
                    var detailText = CoreLoopUiArt.Text("Polarity", topBanners, 960f, nextY + 112f, 640f, 36f,
                        hud.PolarityBannerText, 19f, phys ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade,
                        true, false, TextAlignmentOptions.Center);
                    detailText.enableAutoSizing = true;
                    detailText.fontSizeMin = 14f;
                    detailText.fontSizeMax = 19f;
                    nextY += 156f;
                }
                else
                {
                    CoreLoopUiArt.Image("PolarityBanner", topBanners, 770f, nextY, 1020f, 52f,
                        "hud-strip", new Color(.91f, .88f, .73f, .92f), raycast: false);
                    CoreLoopUiArt.Text("Polarity", topBanners, 796f, nextY + 4f, 968f, 44f,
                        hud.PolarityBannerText, 24f, phys ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade,
                        true, false, TextAlignmentOptions.Center);
                    nextY += 58f;
                }
            }

            if (hud.HasProtectedGuard)
            {
                bool hasGuardHp = SeasonEconomyUiArt.TryFindProtectedGuardHp(
                    _game?.Battle?.State, hud.ProtectedGuardActorId, out string guardName, out ulong guardHp, out ulong guardMaxHp);
                var frameSprite = SeasonEconomyUiArt.LoadControl("guard_hp_frame");
                var shieldSprite = SeasonEconomyUiArt.LoadIcon("badge_protect_guard");
                if (frameSprite != null)
                {
                    // 按 549:129 原比例放置顶部独立护卫血条底框，内槽实时反映权威 BattleStateS2C 护卫气血。
                    const float gx = 1046f;
                    const float gw = 468f;
                    const float gh = 110f;
                    SeasonEconomyUiArt.CreateSlicedOrSimple(topBanners, "GuardBanner", gx, nextY, gw, gh,
                        frameSprite, new Color(.93f, .9f, .78f, .92f), raycast: false);
                    if (shieldSprite != null)
                    {
                        var shield = QdaoUguiFactory.CreateImage("GuardShieldIcon", topBanners, gx + 28f, nextY + 32f, 46f, 46f,
                            shieldSprite, false);
                        shield.preserveAspect = true;
                    }
                    float barX = gx + gw * (180f / 549f);
                    float barY = nextY + gh * (48f / 129f);
                    float barW = gw * (290f / 549f);
                    float barH = gh * (32f / 129f);
                    float hpRatio = hasGuardHp && guardMaxHp > 0UL ? Mathf.Clamp01((float)guardHp / guardMaxHp) : 1f;
                    CoreLoopUiArt.Image("GuardHpTrack", topBanners, barX, barY, barW, barH,
                        null, new Color(.22f, .28f, .24f, .68f), raycast: false);
                    if (hpRatio > 0f)
                        CoreLoopUiArt.Image("GuardHpFill", topBanners, barX + 2f, barY + 2f, Mathf.Max(2f, (barW - 4f) * hpRatio), barH - 4f,
                            null, hpRatio <= 0.3f ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade, raycast: false);
                    string guardLabel = hasGuardHp
                        ? $"须保全 · {guardName} {guardHp}/{guardMaxHp}"
                        : "须保全 · " + hud.ProtectedGuardBannerText;
                    var guardText = CoreLoopUiArt.Text("Guard", topBanners, barX + 6f, barY, barW - 12f, barH,
                        guardLabel, 17f, CoreLoopUiArt.Ivory, true, false, TextAlignmentOptions.Center);
                    guardText.enableAutoSizing = true;
                    guardText.fontSizeMin = 12f;
                    guardText.fontSizeMax = 17f;
                    nextY += gh + 6f;
                }
                else
                {
                    CoreLoopUiArt.Image("GuardBanner", topBanners, 770f, nextY, 1020f, 48f,
                        "hud-strip", new Color(.93f, .9f, .78f, .92f), raycast: false);
                    CoreLoopUiArt.Text("Guard", topBanners, 794f, nextY + 2f, 972f, 44f,
                        hasGuardHp ? $"须保全 · {guardName} {guardHp}/{guardMaxHp}" : "须保全 · " + hud.ProtectedGuardBannerText,
                        22f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
                    nextY += 54f;
                }
            }

            if (hud.HasTelegraphs)
            {
                int visibleCount = Mathf.Min(2, hud.Telegraphs.Count);
                var nowStrip = SeasonEconomyUiArt.LoadControl("telegraph_strip_now");
                var upcomingStrip = SeasonEconomyUiArt.LoadControl("telegraph_strip_upcoming");
                bool useArtStrips = nowStrip != null || upcomingStrip != null;
                for (int i = 0; i < visibleCount; ++i)
                {
                    var telegraph = hud.Telegraphs[i];
                    if (telegraph == null) continue;
                    bool interrupted = telegraph.Status == 4u;
                    if (useArtStrips)
                    {
                        // 两条预告横向并排，保持 520:139 原图比例（468x125），不拉伸卷饰也不压住敌方单位。
                        const float sw = 468f;
                        const float sh = 125f;
                        float sx = visibleCount == 1 ? 1046f : (800f + i * 492f);
                        var stripSprite = telegraph.ExecutesThisRound ? (nowStrip ?? upcomingStrip) : (upcomingStrip ?? nowStrip);
                        var stripImg = SeasonEconomyUiArt.CreateSlicedOrSimple(topBanners, "TelegraphBack" + i,
                            sx, nextY, sw, sh, stripSprite,
                            interrupted ? new Color(.77f, .88f, .81f, .92f) : new Color(.97f, .92f, .8f, .93f), raycast: false);
                        if (interrupted) stripImg.color = new Color(.84f, .92f, .86f, .92f);

                        var intentSprite = SeasonEconomyUiArt.LoadIcon(SeasonEconomyUiArt.IntentIconKey(telegraph.IntentKind));
                        float textX = sx + 112f;
                        float textW = 244f;
                        if (intentSprite != null)
                        {
                            var icon = QdaoUguiFactory.CreateImage("TelegraphIntentIcon" + i, topBanners,
                                sx + 106f, nextY + 40f, 44f, 44f, intentSprite, false);
                            icon.preserveAspect = true;
                            textX = sx + 154f;
                            textW = 204f;
                        }
                        var statusLbl = CoreLoopUiArt.Text("TelegraphStatus" + i, topBanners, textX, nextY + 34f, textW, 22f,
                            telegraph.StatusTag, 16f, interrupted ? CoreLoopUiArt.Jade : CoreLoopUiArt.Warning,
                            true, false, TextAlignmentOptions.MidlineLeft);
                        statusLbl.enableAutoSizing = true;
                        statusLbl.fontSizeMin = 12f;
                        statusLbl.fontSizeMax = 16f;
                        var detailLbl = CoreLoopUiArt.Text("TelegraphDetail" + i, topBanners, textX, nextY + 56f, textW, 38f,
                            telegraph.DetailText, 16f, interrupted ? CoreLoopUiArt.Muted : CoreLoopUiArt.Ink,
                            !interrupted, true, TextAlignmentOptions.TopLeft);
                        detailLbl.enableAutoSizing = true;
                        detailLbl.fontSizeMin = 12f;
                        detailLbl.fontSizeMax = 16f;
                    }
                    else
                    {
                        float y = nextY + i * 52f;
                        CoreLoopUiArt.Image("TelegraphBack" + i, topBanners, 760f, y, 1080f, 46f,
                            "hud-strip", interrupted ? new Color(.77f, .88f, .81f, .92f)
                                : new Color(.97f, .92f, .8f, .93f), raycast: false);
                        CoreLoopUiArt.Badge(topBanners, "TelegraphStatus" + i, 774f, y + 2f, 196f,
                            telegraph.StatusTag, interrupted ? CoreLoopUiArt.Jade : CoreLoopUiArt.Warning);
                        CoreLoopUiArt.Text("TelegraphDetail" + i, topBanners, 982f, y + 2f, 844f, 42f,
                            telegraph.DetailText, 21f, interrupted ? CoreLoopUiArt.Muted : CoreLoopUiArt.Ink,
                            !interrupted, false);
                    }
                }
            }

            // 左下角紧凑提示：严格避开居中 TargetHintRect (x>=724) / ConfirmRect (x>=1310) / HintRect (y>=1048)
            CoreLoopUiArt.Image("PetActionHintBack", transform, 36f, 1010f, 640f, 52f,
                "hud-strip", new Color(.95f, .92f, .83f, .9f), raycast: false);
            CoreLoopUiArt.Text("PetActionHint", transform, 52f, 1014f, 608f, 44f,
                "宝宝选招：攻击／障碍／辅助三类合计施放 1 招",
                19f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.MidlineLeft);
        }
    }
}
