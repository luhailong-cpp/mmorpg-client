using MmorpgClient.Game;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>
    /// 战斗内公开预告、物法易势和护卫保全的纯只读轻量叠加层（零 raycast，绝不拦截舞台点选与确认按钮）。
    /// 顶部条紧贴计时环之下（y=132+，x=760..1860，避开左上回合/记录与右上角色卡/胶囊）；
    /// 宝宝一招提示置左下角（x=36..668, y=1010..1062，避开居中 Confirm/Cancel 目标确认条与右下命令环）。
    /// </summary>
    public sealed class BattleCoreHudOverlay : MonoBehaviour
    {
        private CoreGameplayLoopClient _core;
        private GameClient _game;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _core = core;
            _game = game;
            Refresh();
            Hide();
        }

        public void Show() { if (IsVisible) return; gameObject.SetActive(true); Refresh(); }
        public void Hide()
        {
            BattleUiRoot.Instance?.ActiveBattleScreen?.SetCoreLoopActorMarkers(0ul, 0ul, 0ul);
            gameObject.SetActive(false);
        }

        public void Refresh()
        {
            if (_core == null) return;
            var hud = _core.BuildBattleHud();
            CoreLoopUiArt.Clear(transform);
            if (hud == null) return;
            ulong firstTelegraphTargetId = 0ul;
            ulong secondTelegraphTargetId = 0ul;
            if (hud.Telegraphs != null)
                for (int i = 0; i < hud.Telegraphs.Count && i < 2; ++i)
                {
                    var entry = hud.Telegraphs[i];
                    if (entry == null || entry.TargetActorId == 0ul || entry.Status == 4u) continue;
                    if (firstTelegraphTargetId == 0ul) firstTelegraphTargetId = entry.TargetActorId;
                    else if (entry.TargetActorId != firstTelegraphTargetId)
                    { secondTelegraphTargetId = entry.TargetActorId; break; }
                }
            if (IsVisible)
                BattleUiRoot.Instance?.ActiveBattleScreen?.SetCoreLoopActorMarkers(
                    firstTelegraphTargetId, secondTelegraphTargetId, hud.ProtectedGuardActorId);

            // 顶部居中信息带：严格在 TimerRect (y=24..114) 之下、左列 (x<=740) 之右、右上角色卡 (x>=2190) 之左
            float nextY = 122f;
            if (hud.HasPolarity)
            {
                bool phys = hud.ActivePolarity == 1u;
                var bannerSprite = SeasonEconomyUiArt.LoadControl(phys ? "polarity_banner_phys" : "polarity_banner_magic");
                var badgeSprite = SeasonEconomyUiArt.LoadIcon(phys ? "badge_polarity_phys" : "badge_polarity_magic");
                if (bannerSprite != null)
                {
                    // 按 561:133 原比例绘制阴阳双势横幅，不拉伸端饰；副条放完整换势说明，确保不裁字。
                    SeasonEconomyUiArt.CreateSlicedOrSimple(transform, "PolarityBanner", 1040f, nextY, 480f, 114f,
                        bannerSprite, new Color(.91f, .88f, .73f, .92f), raycast: false);
                    if (badgeSprite != null)
                    {
                        var icon = QdaoUguiFactory.CreateImage("PolarityBadgeIcon", transform, 1086f, nextY + 33f, 48f, 48f,
                            badgeSprite, false);
                        icon.preserveAspect = true;
                    }
                    var shortTitle = CoreLoopUiArt.Text("PolarityShort", transform, 1152f, nextY + 34f, 256f, 46f,
                        phys ? "物势当令 · 物伤 +15%" : "法势当令 · 法伤 +15%", 22f,
                        phys ? CoreLoopUiArt.Warning : CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
                    shortTitle.enableAutoSizing = true;
                    shortTitle.fontSizeMin = 15f;
                    shortTitle.fontSizeMax = 22f;

                    SeasonEconomyUiArt.CreateSlicedOrSimple(transform, "PolarityDetailPlate", 920f, nextY + 110f, 720f, 40f,
                        SeasonEconomyUiArt.LoadControl("notice_banner_soft"), new Color(.96f, .93f, .84f, .93f), raycast: false);
                    var detailText = CoreLoopUiArt.Text("Polarity", transform, 960f, nextY + 112f, 640f, 36f,
                        hud.PolarityBannerText, 19f, phys ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade,
                        true, false, TextAlignmentOptions.Center);
                    detailText.enableAutoSizing = true;
                    detailText.fontSizeMin = 14f;
                    detailText.fontSizeMax = 19f;
                    nextY += 156f;
                }
                else
                {
                    CoreLoopUiArt.Image("PolarityBanner", transform, 770f, nextY, 1020f, 52f,
                        "hud-strip", new Color(.91f, .88f, .73f, .92f), raycast: false);
                    CoreLoopUiArt.Text("Polarity", transform, 796f, nextY + 4f, 968f, 44f,
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
                    SeasonEconomyUiArt.CreateSlicedOrSimple(transform, "GuardBanner", gx, nextY, gw, gh,
                        frameSprite, new Color(.93f, .9f, .78f, .92f), raycast: false);
                    if (shieldSprite != null)
                    {
                        var shield = QdaoUguiFactory.CreateImage("GuardShieldIcon", transform, gx + 28f, nextY + 32f, 46f, 46f,
                            shieldSprite, false);
                        shield.preserveAspect = true;
                    }
                    float barX = gx + gw * (180f / 549f);
                    float barY = nextY + gh * (48f / 129f);
                    float barW = gw * (290f / 549f);
                    float barH = gh * (32f / 129f);
                    float hpRatio = hasGuardHp && guardMaxHp > 0UL ? Mathf.Clamp01((float)guardHp / guardMaxHp) : 1f;
                    CoreLoopUiArt.Image("GuardHpTrack", transform, barX, barY, barW, barH,
                        null, new Color(.22f, .28f, .24f, .68f), raycast: false);
                    if (hpRatio > 0f)
                        CoreLoopUiArt.Image("GuardHpFill", transform, barX + 2f, barY + 2f, Mathf.Max(2f, (barW - 4f) * hpRatio), barH - 4f,
                            null, hpRatio <= 0.3f ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade, raycast: false);
                    string guardLabel = hasGuardHp
                        ? $"须保全 · {guardName} {guardHp}/{guardMaxHp}"
                        : "须保全 · " + hud.ProtectedGuardBannerText;
                    var guardText = CoreLoopUiArt.Text("Guard", transform, barX + 6f, barY, barW - 12f, barH,
                        guardLabel, 17f, CoreLoopUiArt.Ivory, true, false, TextAlignmentOptions.Center);
                    guardText.enableAutoSizing = true;
                    guardText.fontSizeMin = 12f;
                    guardText.fontSizeMax = 17f;
                    nextY += gh + 6f;
                }
                else
                {
                    CoreLoopUiArt.Image("GuardBanner", transform, 770f, nextY, 1020f, 48f,
                        "hud-strip", new Color(.93f, .9f, .78f, .92f), raycast: false);
                    CoreLoopUiArt.Text("Guard", transform, 794f, nextY + 2f, 972f, 44f,
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
                        var stripImg = SeasonEconomyUiArt.CreateSlicedOrSimple(transform, "TelegraphBack" + i,
                            sx, nextY, sw, sh, stripSprite,
                            interrupted ? new Color(.77f, .88f, .81f, .92f) : new Color(.97f, .92f, .8f, .93f), raycast: false);
                        if (interrupted) stripImg.color = new Color(.84f, .92f, .86f, .92f);

                        var intentSprite = SeasonEconomyUiArt.LoadIcon(SeasonEconomyUiArt.IntentIconKey(telegraph.IntentKind));
                        float textX = sx + 112f;
                        float textW = 244f;
                        if (intentSprite != null)
                        {
                            var icon = QdaoUguiFactory.CreateImage("TelegraphIntentIcon" + i, transform,
                                sx + 106f, nextY + 40f, 44f, 44f, intentSprite, false);
                            icon.preserveAspect = true;
                            textX = sx + 154f;
                            textW = 204f;
                        }
                        var statusLbl = CoreLoopUiArt.Text("TelegraphStatus" + i, transform, textX, nextY + 34f, textW, 22f,
                            telegraph.StatusTag, 16f, interrupted ? CoreLoopUiArt.Jade : CoreLoopUiArt.Warning,
                            true, false, TextAlignmentOptions.MidlineLeft);
                        statusLbl.enableAutoSizing = true;
                        statusLbl.fontSizeMin = 12f;
                        statusLbl.fontSizeMax = 16f;
                        var detailLbl = CoreLoopUiArt.Text("TelegraphDetail" + i, transform, textX, nextY + 56f, textW, 38f,
                            telegraph.DetailText, 16f, interrupted ? CoreLoopUiArt.Muted : CoreLoopUiArt.Ink,
                            !interrupted, true, TextAlignmentOptions.TopLeft);
                        detailLbl.enableAutoSizing = true;
                        detailLbl.fontSizeMin = 12f;
                        detailLbl.fontSizeMax = 16f;
                    }
                    else
                    {
                        float y = nextY + i * 52f;
                        CoreLoopUiArt.Image("TelegraphBack" + i, transform, 760f, y, 1080f, 46f,
                            "hud-strip", interrupted ? new Color(.77f, .88f, .81f, .92f)
                                : new Color(.97f, .92f, .8f, .93f), raycast: false);
                        CoreLoopUiArt.Badge(transform, "TelegraphStatus" + i, 774f, y + 2f, 196f,
                            telegraph.StatusTag, interrupted ? CoreLoopUiArt.Jade : CoreLoopUiArt.Warning);
                        CoreLoopUiArt.Text("TelegraphDetail" + i, transform, 982f, y + 2f, 844f, 42f,
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
