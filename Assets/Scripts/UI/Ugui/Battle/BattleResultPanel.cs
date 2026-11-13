using System;
using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 结算屏(turn-battle-presentation.md §4):OnBattleEnd 后
    /// "胜利 / 失败 / 平局"大字弹入(2.4 → 1.0 BackOut)→ 奖励逐条从右飞入 → 确认按钮淡入;
    /// 点「返回场景」收起(BattleUiRoot 在回调里调 AckBattleEnd,契约不变)。
    /// </summary>
    public sealed class BattleResultPanel
    {
        private const float PanelW = 900f;
        private const float PanelH = 720f;
        private const float RowStartY = 236f;
        private const float RowsViewportHeight = 360f;
        private const float RowHeight = 44f;
        private const float RowDelay = 0.12f;

        private readonly RectTransform _root;
        private readonly CanvasGroup _group;
        private readonly TMP_Text _titleText;
        private readonly TMP_Text _roundsText;
        private readonly RectTransform _rows;
        private readonly ScrollRect _rewardScroll;
        private readonly UiTextButton _confirmButton;
        private readonly CanvasGroup _confirmGroup;
        private readonly List<GameObject> _rowObjects = new List<GameObject>();
        private readonly object _token = new object();

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public BattleResultPanel(UnityEngine.Transform parent, Action onConfirm)
        {
            float x = (QdaoUguiTheme.DesignWidth - PanelW) * 0.5f;
            float y = (QdaoUguiTheme.DesignHeight - PanelH) * 0.5f;
            var bg = BattleUiWidgets.CreatePanel("BattleResultPanel", parent, x, y, PanelW, PanelH, BattleUiStyle.PanelBgLight);
            BattleRoundCounter.ApplyNineSlice(bg, "panel_9slice");
            _root = (RectTransform)bg.transform;
            _group = _root.gameObject.AddComponent<CanvasGroup>();

            _titleText = QdaoUguiFactory.CreateText("Title", _root, 0f, 36f, PanelW, 110f,
                string.Empty, 92f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _titleText.rectTransform.anchoredPosition += new Vector2(PanelW * 0.5f, -55f);
            try
            {
                _titleText.outlineWidth = 0.25f;
                _titleText.outlineColor = new Color32(40, 20, 10, 230);
            }
            catch (Exception)
            {
                // 无 SDF 材质实例时描边不可用
            }
            _roundsText = QdaoUguiFactory.CreateText("Rounds", _root, 0f, 156f, PanelW, 36f,
                string.Empty, 24f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.Center);
            var viewport = QdaoUguiFactory.CreateRect("RowsViewport", _root, 120f, RowStartY, PanelW - 240f, RowsViewportHeight);
            viewport.gameObject.AddComponent<RectMask2D>();
            var surface = viewport.gameObject.AddComponent<Image>();
            surface.color = new Color(1f, 1f, 1f, 0.001f);
            surface.raycastTarget = true;
            _rows = QdaoUguiFactory.CreateRect("Rows", viewport, 0f, 0f, PanelW - 240f, RowsViewportHeight);
            _rewardScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _rewardScroll.viewport = viewport;
            _rewardScroll.content = _rows;
            _rewardScroll.horizontal = false;
            _rewardScroll.vertical = true;
            _rewardScroll.movementType = ScrollRect.MovementType.Clamped;
            _rewardScroll.scrollSensitivity = RowHeight;
            _rewardScroll.inertia = false;

            _confirmButton = BattleUiWidgets.CreateTextButton("Confirm", _root,
                (PanelW - 260f) * 0.5f, PanelH - 116f, 260f, 76f,
                LiveConfirmText, 26f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_confirmButton.Plate, "button_9slice");
            _confirmGroup = _confirmButton.Rect.gameObject.AddComponent<CanvasGroup>();
            _confirmButton.Button.onClick.AddListener(() => onConfirm?.Invoke());

            BattleFidelityArt.ApplyPanel(_root);
            Hide();
        }

        /// <summary>展示结算。fallbackTeamIndex:结算数据缺失时用战斗屏记录的我方 team。</summary>
        public void Show(BattleEndS2C end, uint fallbackTeamIndex)
        {
            if (end == null || _root == null) return;
            var settlement = end.Settlement;
            eBattleOutcome outcome = settlement != null ? settlement.Outcome : end.Outcome;
            uint myTeam = settlement != null ? settlement.PlayerTeamIndex : fallbackTeamIndex;
            // 本局作废(服务端为这一局写了作废决定):没有胜负、奖励、消耗与战后状态,settlement 里只有身份与展示字段。
            // 必须排在胜负判断之前 —— 落进下面的 else 会显示成「失败」,奖励行还会列出「HP 0 / MP 0」。
            bool voided = BattleClient.IsVoidEnd(end);

            ResolveTitle(outcome, myTeam, voided, out string title, out Color titleColor);
            string subtitle = voided ? VoidSubtitle
                : settlement != null ? $"共 {settlement.TotalRounds} 回合" : string.Empty;
            var lines = voided ? BuildVoidLines() : BuildRewardLines(settlement, end.ItemsMailed);
            Present(title, titleColor, subtitle, lines, LiveConfirmText);
        }

        /// <summary>
        /// 补弹一局近期战斗结果(落地补查到的、玩家还没看过的那一局,服务端 docs/design/battle-result-recovery.md §7)。
        /// 胜负口径与 <see cref="Show"/> 相同;记录里没有经验与战后血蓝(只记实际入账的货币与物品),所以不列这两项。
        /// </summary>
        public void ShowRecovered(PlayerBattleResult result)
        {
            if (result == null || _root == null) return;
            bool voided = BattleResultRecoveryClient.IsVoid(result);
            ResolveTitle((eBattleOutcome)result.Outcome, result.TeamIndex, voided, out string title, out Color titleColor);
            Present(title, titleColor, voided ? VoidSubtitle : BuildRecoveredSubtitle(result), BuildRecoveredLines(result),
                RecoveredConfirmText);
        }

        // 胜负大字:作废排在最前(落进「失败」分支会显示成输了);胜 = outcome 对照我方 team。
        private static void ResolveTitle(eBattleOutcome outcome, uint myTeam, bool voided, out string title, out Color color)
        {
            if (voided)
            {
                title = VoidTitle;
                color = QdaoUguiTheme.MutedBrown;
            }
            else if (outcome == eBattleOutcome.BattleOutcomeDraw)
            {
                title = "平 局";
                color = QdaoUguiTheme.MutedBrown;
            }
            else if ((outcome == eBattleOutcome.BattleOutcomeSideAWin && myTeam == 0) ||
                     (outcome == eBattleOutcome.BattleOutcomeSideBWin && myTeam == 1))
            {
                title = "胜 利";
                color = QdaoUguiTheme.Html("#205B46");
            }
            else if (outcome == eBattleOutcome.BattleOutcomeOngoing)
            {
                title = "战斗结束";
                color = BattleFidelityArt.Ink;
            }
            else
            {
                title = "失 败";
                color = BattleUiStyle.DamageText;
            }
        }

        private void Present(string title, Color titleColor, string subtitle, List<string> lines, string confirmText)
        {
            _titleText.text = title;
            _titleText.color = titleColor;
            _roundsText.text = subtitle;
            _confirmButton.SetText(confirmText);
            KillTweens();
            ClearRows();
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _group.alpha = 1f;
            // 奖励数量可配置：内容增长后仍能滚动查看末行，每次展示从顶部开始。
            _rows.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(RowsViewportHeight, lines.Count * RowHeight));
            _rewardScroll.StopMovement();
            _rows.anchoredPosition = Vector2.zero;
            _rewardScroll.verticalNormalizedPosition = 1f;

            // 大字弹入
            var titleRect = _titleText.rectTransform;
            titleRect.localScale = Vector3.one * 2.4f;
            var tc = _titleText.color; tc.a = 0f; _titleText.color = tc;
            RealtimeTween.To(2.4f, 1f, 0.42f).SetEase(RealtimeEase.BackOut).SetIgnoreEngineTimeScale(true).SetTarget(_token)
                .OnUpdate((RealtimeTweenCallback1)(t =>
                {
                    if (titleRect == null) return;
                    titleRect.localScale = new Vector3(t.Value.x, t.Value.x, 1f);
                    var c = _titleText.color;
                    c.a = Mathf.Clamp01((2.4f - t.Value.x) / 1.0f);
                    _titleText.color = c;
                }));

            // 奖励逐条飞入
            float rowsW = PanelW - 240f;
            for (int i = 0; i < lines.Count; i++)
            {
                var text = QdaoUguiFactory.CreateText($"Row{i}", _rows, 0f, i * RowHeight, rowsW, RowHeight,
                    lines[i], 26f, BattleFidelityArt.Ink);
                var go = text.gameObject;
                _rowObjects.Add(go);
                var rect = text.rectTransform;
                var c0 = text.color; c0.a = 0f; text.color = c0;
                float rowY = -i * RowHeight;
                RealtimeTween.To(0f, 1f, 0.3f).SetDelay(0.45f + i * RowDelay).SetEase(RealtimeEase.CubicOut).SetIgnoreEngineTimeScale(true).SetTarget(_token)
                    .OnUpdate((RealtimeTweenCallback1)(t =>
                    {
                        if (go == null) return;
                        float k = t.Value.x;
                        rect.anchoredPosition = new Vector2(260f * (1f - k), rowY);
                        var c = text.color; c.a = k; text.color = c;
                    }));
            }

            // 确认键淡入(等最后一条飞入完)
            _confirmGroup.alpha = 0f;
            _confirmGroup.interactable = false;
            _confirmGroup.blocksRaycasts = false;
            float confirmDelay = 0.45f + lines.Count * RowDelay + 0.2f;
            RealtimeTween.To(0f, 1f, 0.3f).SetDelay(confirmDelay).SetIgnoreEngineTimeScale(true).SetTarget(_token)
                .OnUpdate((RealtimeTweenCallback1)(t => { if (_confirmGroup != null) _confirmGroup.alpha = t.Value.x; }))
                .OnComplete((RealtimeTweenCallback)(() =>
                {
                    if (_confirmGroup == null) return;
                    _confirmGroup.alpha = 1f;
                    _confirmGroup.interactable = true;
                    _confirmGroup.blocksRaycasts = true;
                }));
        }

        public void Hide()
        {
            KillTweens();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// 两个背包都放不下时,服务器把本局获得的道具整批改由邮件发放(BattleEndS2C.items_mailed,
        /// 服务端 docs/design/battle-overflow-mail.md)。这一行告诉玩家去邮箱领,别在背包里找。
        /// 写「改由」不写「已」:收到结算时邮件多半还在寄送途中(存档提交之后才寄),邮件服务不可达时会更晚到。
        /// </summary>
        public const string ItemsMailedLine = "背包已满，以上道具改由邮件发放，请留意邮箱";

        /// <summary>本局作废时结算面板的标题。</summary>
        public const string VoidTitle = "本局作废";
        /// <summary>本局作废时标题下的一行(平时这里是「共 N 回合」)。</summary>
        public const string VoidSubtitle = "战斗未能完成,不计胜负";

        /// <summary>现场结算面板的确认键:收起之后回到场景。</summary>
        public const string LiveConfirmText = "返回场景";
        /// <summary>补弹结果的确认键:玩家已经在场景里,只是看完。</summary>
        public const string RecoveredConfirmText = "知道了";
        /// <summary>补弹结果说明行的开头:告诉玩家这是之前没看到的一局。</summary>
        public const string RecoveredSubtitlePrefix = "上次未查看的战斗 · ";
        /// <summary>服务端只存下了一部分物品行(配置号最小的那些)时补一句。</summary>
        public const string TruncatedLinesLine = "物品较多,只列出一部分";

        /// <summary>
        /// 本局作废时的说明行(纯逻辑,EditMode 可测),代替奖励行:作废的 settlement 里没有任何效果,
        /// 按 <see cref="BuildRewardLines"/> 列出来会是一串 +0 和「HP 0 / MP 0」。
        /// 原因(战斗服故障、超过期限、活动取消)服务端不区分下发,这里只说对玩家有用的事实。
        /// </summary>
        public static List<string> BuildVoidLines()
        {
            return new List<string>
            {
                "没有奖励,也没有消耗",
                "带进战斗的道具已原样放回",
                "可以重新发起战斗",
            };
        }

        /// <summary>
        /// 奖励行文案(纯逻辑,EditMode 可测)。itemsMailed:本局获得的道具改由邮件发放,在道具行之后加一行提示;
        /// 没有获得道具时不加。
        /// </summary>
        public static List<string> BuildRewardLines(BattleSettlementData settlement, bool itemsMailed = false)
        {
            var lines = new List<string>();
            if (settlement == null)
            {
                lines.Add("(未收到结算数据)");
                return lines;
            }
            lines.Add($"经验  +{settlement.ExpGain}");
            if (settlement.CurrencyGains.Count > 0)
            {
                foreach (var currency in settlement.CurrencyGains)
                    lines.Add($"{CurrencyName(currency.CurrencyType)}  +{currency.Amount}");
            }
            else
            {
                // 兼容旧结算：GoldGain 入的是 kCurrencyGold，契约 §0-1 叫“银两”。
                lines.Add($"银两  +{settlement.GoldGain}");
            }
            if (settlement.ItemsGained != null && settlement.ItemsGained.Count > 0)
            {
                foreach (var item in settlement.ItemsGained)
                    lines.Add($"获得  道具{item.ItemTableId} ×{item.Count}");
                if (itemsMailed) lines.Add(ItemsMailedLine);
            }
            else
            {
                lines.Add("获得道具  无");
            }
            if (settlement.ItemsConsumed != null && settlement.ItemsConsumed.Count > 0)
            {
                foreach (var item in settlement.ItemsConsumed)
                    lines.Add($"消耗  道具{item.ItemTableId} ×{item.Count}");
            }
            string state = $"战后状态  HP {settlement.Health} / MP {settlement.Mana}";
            if (settlement.IsDead) state += "(阵亡)";
            if (settlement.Fled) state += "(中途逃离)";
            lines.Add(state);
            return lines;
        }

        /// <summary>
        /// 补弹结果的说明行:这是之前没看到的一局、什么时候结束、打了几回合。
        /// 时间用 finished_at_ms,为 0 时用 settled_at_ms,两者都没有就不写时间。
        /// </summary>
        public static string BuildRecoveredSubtitle(PlayerBattleResult result)
        {
            if (result == null) return string.Empty;
            ulong ms = result.FinishedAtMs != 0 ? result.FinishedAtMs : result.SettledAtMs;
            string when = ms != 0 && ms <= MaxUnixMilliseconds
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).ToLocalTime()
                      .ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " 结束 · "
                : string.Empty;
            return $"{RecoveredSubtitlePrefix}{when}共 {result.TotalRounds} 回合";
        }

        private static readonly ulong MaxUnixMilliseconds = (ulong)DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

        /// <summary>
        /// 补弹结果的明细行(纯逻辑,EditMode 可测),取自服务端存下的近期战斗结果:只记实际入账的货币与物品,
        /// 没有经验与战后血蓝。作废局与现场面板用同一套说明行。
        /// </summary>
        public static List<string> BuildRecoveredLines(PlayerBattleResult result)
        {
            if (result == null) return new List<string> { "(未收到结算数据)" };
            if (BattleResultRecoveryClient.IsVoid(result)) return BuildVoidLines();
            var lines = new List<string>();
            if (result.Currencies.Count > 0)
            {
                foreach (var currency in result.Currencies)
                    lines.Add($"{CurrencyName(currency.CurrencyType)}  +{currency.Amount}");
            }
            else
            {
                lines.Add("获得货币  无");
            }
            if (result.ItemsGained.Count > 0)
            {
                foreach (var item in result.ItemsGained)
                    lines.Add($"获得  道具{item.ItemTableId} ×{item.Count}");
                if (result.ItemsMailed) lines.Add(ItemsMailedLine);
            }
            else
            {
                lines.Add("获得道具  无");
            }
            foreach (var item in result.ItemsConsumed)
                lines.Add($"消耗  道具{item.ItemTableId} ×{item.Count}");
            if (result.LinesTruncated) lines.Add(TruncatedLinesLine);
            if (result.IsDead && result.Fled) lines.Add("战后状态  阵亡、中途逃离");
            else if (result.IsDead) lines.Add("战后状态  阵亡");
            else if (result.Fled) lines.Add("战后状态  中途逃离");
            return lines;
        }

        /// <summary>货币显示名(契约 §0-1 的叫法);不认识的类型原样带编号。</summary>
        private static string CurrencyName(uint currencyType) => currencyType switch
        {
            0 => "银两",
            1 => "灵石",
            2 => "绑定灵石",
            _ => $"货币{currencyType}"
        };

        private void KillTweens()
        {
            RealtimeTween.Kill(_token);
            if (_confirmGroup != null)
            {
                _confirmGroup.alpha = 1f;
                _confirmGroup.interactable = true;
                _confirmGroup.blocksRaycasts = true;
            }
            if (_titleText != null)
            {
                _titleText.rectTransform.localScale = Vector3.one;
                var c = _titleText.color; c.a = 1f; _titleText.color = c;
            }
        }

        private void ClearRows()
        {
            foreach (var go in _rowObjects)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            _rowObjects.Clear();
        }
    }
}
