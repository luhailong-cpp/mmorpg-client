using System;
using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 战报回放屏附加层（仅 _replay 模式激活）：
    ///   - 左侧高光时刻快捷跳转条（首杀/连斩/最高爆发/复活回春/决胜回合，最多 5 枚，点击直达该回合）；
    ///   - 居中可切换「数据复盘」抽屉（双方总伤害/总治疗对比、MVP 冠冕与各单位伤害/治疗/承伤/暴击/击败条）。
    /// 优先加载 Resources/UI/Ugui/BattleReportV2/ 切图，缺图时自动回落现有宣纸/玄玉皮肤。
    /// </summary>
    internal sealed class BattleReplayOverlay
    {
        public static readonly Rect HighlightStripRect = new Rect(40f, 520f, 248f, 360f);
        public static readonly Rect StatsPanelRect = new Rect(690f, 148f, 1180f, 760f);

        private const float PillHeight = 46f;
        private const float PillGap = 8f;
        private const float RowHeight = 76f;
        private const float RowGap = 8f;

        private static readonly Color DamageBarColor = QdaoUguiTheme.Html("#B4472B");
        private static readonly Color HealBarColor = QdaoUguiTheme.Html("#2E7D5B");
        private static readonly Color BarTrackColor = new Color(0.18f, 0.14f, 0.10f, 0.35f);
        private static readonly Color MvpGold = QdaoUguiTheme.Html("#D9A441");

        private readonly Action<int> _onJumpRound;
        private readonly RectTransform _highlightRoot;
        private readonly RectTransform _highlightList;
        private readonly List<GameObject> _highlightPills = new List<GameObject>();

        private readonly RectTransform _statsRoot;
        private readonly TMP_Text _statsSummaryText;
        private readonly Image _mvpBadgeIcon;
        private readonly TMP_Text _mvpBadgeText;
        private readonly RectTransform _statsContent;
        private readonly ScrollRect _statsScroll;
        private readonly List<GameObject> _statsRows = new List<GameObject>();

        private BattleReplayAnalysis _analysis;

        public bool IsStatsVisible => _statsRoot != null && _statsRoot.gameObject.activeSelf;

        public BattleReplayOverlay(UnityEngine.Transform hudParent, Action<int> onJumpRound)
        {
            _onJumpRound = onJumpRound;

            // ── 左侧高光时刻快捷跳转条 ──
            var strip = BattleUiWidgets.CreatePanel("ReplayHighlightStrip", hudParent,
                HighlightStripRect.x, HighlightStripRect.y, HighlightStripRect.width, HighlightStripRect.height,
                BattleUiStyle.PanelBg, raycastTarget: false);
            _highlightRoot = (RectTransform)strip.transform;
            BattleRoundCounter.ApplyNineSlice(strip, "panel_9slice");

            var stripTitle = QdaoUguiFactory.CreateText("HighlightTitle", _highlightRoot,
                14f, 10f, HighlightStripRect.width - 28f, 32f,
                "高光时刻 · 点击跳转", 18f, QdaoUguiTheme.Cream, TextAlignmentOptions.MidlineLeft);
            stripTitle.fontStyle = FontStyles.Bold;

            _highlightList = QdaoUguiFactory.CreateRect("HighlightList", _highlightRoot,
                12f, 48f, HighlightStripRect.width - 24f, HighlightStripRect.height - 58f);

            // ── 居中数据复盘面板 ──
            var statsBg = BattleUiWidgets.CreatePanel("ReplayStatsPanel", hudParent,
                StatsPanelRect.x, StatsPanelRect.y, StatsPanelRect.width, StatsPanelRect.height,
                BattleUiStyle.PanelBg);
            _statsRoot = (RectTransform)statsBg.transform;

            QdaoUguiFactory.CreateText("Title", _statsRoot, 24f, 16f, 420f, 44f,
                "战报数据复盘", 28f, QdaoUguiTheme.Cream);

            var closeBtn = BattleUiWidgets.CreateTextButton("Close", _statsRoot,
                StatsPanelRect.width - 64f, 12f, 48f, 48f,
                "×", 28f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            closeBtn.Button.onClick.AddListener(HideStats);

            _statsSummaryText = QdaoUguiFactory.CreateText("StatsSummary", _statsRoot,
                32f, 84f, StatsPanelRect.width - 360f, 44f,
                string.Empty, 20f, QdaoUguiTheme.Cream, TextAlignmentOptions.MidlineLeft);

            var mvpBox = BattleUiWidgets.CreatePanel("MvpBox", _statsRoot,
                StatsPanelRect.width - 314f, 82f, 282f, 48f, MvpGold, raycastTarget: false);
            _mvpBadgeIcon = QdaoUguiFactory.CreateImage("MvpIcon", mvpBox.transform,
                8f, 6f, 36f, 36f, BattleReportUiArt.TryLoad(BattleReportUiArt.BadgeMvp), raycastTarget: false);
            if (_mvpBadgeIcon.sprite == null) _mvpBadgeIcon.gameObject.SetActive(false);
            _mvpBadgeText = QdaoUguiFactory.CreateText("MvpBadgeText", mvpBox.transform,
                46f, 0f, 228f, 48f, string.Empty, 20f, QdaoUguiTheme.Cream, TextAlignmentOptions.MidlineLeft);
            _mvpBadgeText.fontStyle = FontStyles.Bold;
            _mvpBadgeText.overflowMode = TextOverflowModes.Ellipsis;

            float viewTop = 144f;
            float viewW = StatsPanelRect.width - 64f;
            float viewH = StatsPanelRect.height - viewTop - 28f;
            var viewport = QdaoUguiFactory.CreateRect("Viewport", _statsRoot, 32f, viewTop, viewW, viewH);
            viewport.gameObject.AddComponent<RectMask2D>();
            var surf = viewport.gameObject.AddComponent<Image>();
            surf.color = new Color(1f, 1f, 1f, 0.001f);
            surf.raycastTarget = true;

            _statsContent = QdaoUguiFactory.CreateRect("Content", viewport, 0f, 0f, viewW, viewH);
            _statsScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _statsScroll.viewport = viewport;
            _statsScroll.content = _statsContent;
            _statsScroll.horizontal = false;
            _statsScroll.vertical = true;
            _statsScroll.movementType = ScrollRect.MovementType.Clamped;
            _statsScroll.scrollSensitivity = RowHeight;
            _statsScroll.inertia = false;

            BattleFidelityArt.ApplyPanel(_statsRoot);
            SetVisible(false);
        }

        public void Bind(BattleReplayAnalysis analysis)
        {
            _analysis = analysis;
            RebuildHighlights();
            RebuildStats();
            HideStats();
        }

        public void SetVisible(bool replayVisible)
        {
            bool showHighlights = replayVisible && _analysis != null && _analysis.Highlights.Count > 0;
            if (_highlightRoot != null) _highlightRoot.gameObject.SetActive(showHighlights);
            if (!replayVisible) HideStats();
        }

        public void ToggleStats()
        {
            if (_statsRoot == null || _analysis == null) return;
            bool next = !_statsRoot.gameObject.activeSelf;
            _statsRoot.gameObject.SetActive(next);
            if (next) _statsRoot.SetAsLastSibling();
        }

        public void HideStats()
        {
            if (_statsRoot != null) _statsRoot.gameObject.SetActive(false);
        }

        public void Clear()
        {
            _analysis = null;
            DestroyAll(_highlightPills);
            DestroyAll(_statsRows);
            SetVisible(false);
        }

        private void RebuildHighlights()
        {
            DestroyAll(_highlightPills);
            if (_analysis == null || _analysis.Highlights.Count == 0)
            {
                if (_highlightRoot != null) _highlightRoot.gameObject.SetActive(false);
                return;
            }

            float w = HighlightStripRect.width - 24f;
            for (int i = 0; i < _analysis.Highlights.Count; i++)
            {
                var h = _analysis.Highlights[i];
                float y = i * (PillHeight + PillGap);
                var btn = BattleUiWidgets.CreateTextButton($"Highlight_{h.RoundNumber}", _highlightList,
                    0f, y, w, PillHeight, h.Label, 18f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
                BattleRoundCounter.ApplyNineSlice(btn.Plate, "button_9slice");
                btn.Label.name = "HighlightPillText";

                string iconKey = h.Kind switch
                {
                    BattleReplayHighlightKind.FirstBlood or BattleReplayHighlightKind.MultiKill or BattleReplayHighlightKind.DecidingTurn
                        => BattleReportUiArt.IconHighlightKill,
                    BattleReplayHighlightKind.ClutchHeal => BattleReportUiArt.IconHighlightHeal,
                    _ => BattleReportUiArt.IconHighlightBurst,
                };
                var sprite = BattleReportUiArt.TryLoad(iconKey);
                if (sprite != null)
                {
                    var icon = QdaoUguiFactory.CreateImage("Icon", btn.Rect, 8f, 9f, 28f, 28f, sprite, raycastTarget: false);
                    icon.preserveAspect = true;
                    btn.Label.rectTransform.offsetMin = new Vector2(40f, 0f);
                }

                int round = h.RoundNumber;
                btn.Button.onClick.AddListener(() => _onJumpRound?.Invoke(round));
                _highlightPills.Add(btn.Rect.gameObject);
            }

            float stripH = 56f + _analysis.Highlights.Count * (PillHeight + PillGap);
            _highlightRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, stripH);
        }

        private void RebuildStats()
        {
            DestroyAll(_statsRows);
            if (_analysis == null) return;

            string mode = BattleReportClient.ModeName((Match.MatchMode)_analysis.MatchMode);
            _statsSummaryText.text =
                $"{mode} · 共 {_analysis.RecordedRounds} 回合   |   A队 伤 {BattleReplayAnalyzer.FormatCompact(_analysis.TeamADamage)} / 疗 {BattleReplayAnalyzer.FormatCompact(_analysis.TeamAHealing)}   VS   B队 伤 {BattleReplayAnalyzer.FormatCompact(_analysis.TeamBDamage)} / 疗 {BattleReplayAnalyzer.FormatCompact(_analysis.TeamBHealing)}";

            bool hasMvp = _analysis.MvpActorId != 0 && !string.IsNullOrEmpty(_analysis.MvpName);
            _mvpBadgeText.text = hasMvp ? $"本场 MVP · {_analysis.MvpName}" : "暂无 MVP 数据";

            float viewW = StatsPanelRect.width - 64f;
            ulong maxDmg = Math.Max(1UL, _analysis.MaxActorDamage);
            ulong maxHeal = Math.Max(1UL, _analysis.MaxActorHealing);

            for (int i = 0; i < _analysis.Actors.Count; i++)
            {
                var actor = _analysis.Actors[i];
                float y = i * (RowHeight + RowGap);
                var row = BattleUiWidgets.CreatePanel($"ActorStat_{actor.ActorId}", _statsContent,
                    0f, y, viewW, RowHeight, BattleUiStyle.PanelBgLight, raycastTarget: false);
                var rect = (RectTransform)row.transform;
                _statsRows.Add(row.gameObject);

                string teamTag = actor.TeamIndex == 0 ? "[A]" : "[B]";
                string petTag = actor.IsPet ? "[宠]" : string.Empty;
                string mvpTag = actor.ActorId == _analysis.MvpActorId ? " ★MVP" : string.Empty;
                var nameText = QdaoUguiFactory.CreateText("ActorName", rect,
                    16f, 8f, 260f, 32f,
                    $"{teamTag}{petTag} {actor.Name}{mvpTag}", 20f,
                    actor.ActorId == _analysis.MvpActorId ? MvpGold : QdaoUguiTheme.Cream,
                    TextAlignmentOptions.MidlineLeft);
                nameText.overflowMode = TextOverflowModes.Ellipsis;

                var metaText = QdaoUguiFactory.CreateText("ActorMeta", rect,
                    16f, 42f, 260f, 26f,
                    $"击败 {actor.Kills} · 暴击 {actor.CritCount} · 承伤 {BattleReplayAnalyzer.FormatCompact(actor.DamageTaken)}",
                    16f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.MidlineLeft);
                metaText.overflowMode = TextOverflowModes.Ellipsis;

                // 伤害条
                BuildStatBar(rect, "DamageBar", 290f, 12f, 380f, 22f,
                    actor.DamageDealt / (float)maxDmg, BattleReportUiArt.BarDamageFill, DamageBarColor,
                    $"输出 {BattleReplayAnalyzer.FormatCompact(actor.DamageDealt)}");

                // 治疗/护盾条
                BuildStatBar(rect, "HealBar", 290f, 42f, 380f, 22f,
                    actor.HealingDone / (float)maxHeal, BattleReportUiArt.BarHealFill, HealBarColor,
                    $"治疗/盾 {BattleReplayAnalyzer.FormatCompact(actor.HealingDone)}");

                var peakText = QdaoUguiFactory.CreateText("PeakHit", rect,
                    690f, 20f, viewW - 706f, 36f,
                    actor.PeakHit > 0 ? $"单次峰值 {BattleReplayAnalyzer.FormatCompact(actor.PeakHit)}" : "—",
                    18f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.MidlineRight);
                peakText.overflowMode = TextOverflowModes.Ellipsis;
            }

            float contentH = Mathf.Max(StatsPanelRect.height - 172f, _analysis.Actors.Count * (RowHeight + RowGap));
            _statsContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentH);
            _statsScroll.StopMovement();
            _statsContent.anchoredPosition = Vector2.zero;
            _statsScroll.verticalNormalizedPosition = 1f;
            BattleFidelityArt.ApplyPanel(_statsRoot);
        }

        private static void BuildStatBar(RectTransform parent, string name, float x, float y, float width, float height,
            float ratio, string fillKey, Color fallbackColor, string caption)
        {
            var track = BattleUiWidgets.CreatePanel(name + "_Track", parent, x, y, width, height, BarTrackColor, raycastTarget: false);
            float clamped = Mathf.Clamp01(ratio);
            if (clamped > 0f)
            {
                float fillW = Mathf.Min(width, Mathf.Max(20f, clamped * width));
                var fill = BattleUiWidgets.CreatePanel(name + "_Fill", track.transform, 0f, 0f, fillW, height, fallbackColor, raycastTarget: false);
                BattleReportUiArt.ApplyOptional(fill, fillKey, fallbackColor, slicedIfBordered: true);
            }
            var label = QdaoUguiFactory.CreateText(name + "_Label", track.transform,
                8f, 0f, width - 16f, height, caption, 15f, QdaoUguiTheme.Cream, TextAlignmentOptions.MidlineLeft);
            label.name = "HighlightPillText";
        }

        private static void DestroyAll(List<GameObject> items)
        {
            foreach (var item in items)
            {
                if (item == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(item);
                else UnityEngine.Object.DestroyImmediate(item);
            }
            items.Clear();
        }
    }
}
