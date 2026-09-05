using System;
using System.Collections.Generic;
using FairyGUI;
using TMPro;
using UnityEngine;
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
        private const float RowHeight = 44f;
        private const float RowDelay = 0.12f;

        private readonly RectTransform _root;
        private readonly CanvasGroup _group;
        private readonly TMP_Text _titleText;
        private readonly TMP_Text _roundsText;
        private readonly RectTransform _rows;
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
            _rows = QdaoUguiFactory.CreateRect("Rows", _root, 120f, RowStartY, PanelW - 240f, 360f);
            _rows.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

            _confirmButton = BattleUiWidgets.CreateTextButton("Confirm", _root,
                (PanelW - 260f) * 0.5f, PanelH - 116f, 260f, 76f,
                "返回场景", 26f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_confirmButton.Plate, "button_9slice");
            _confirmGroup = _confirmButton.Rect.gameObject.AddComponent<CanvasGroup>();
            _confirmButton.Button.onClick.AddListener(() => onConfirm?.Invoke());

            Hide();
        }

        /// <summary>展示结算。fallbackTeamIndex:结算数据缺失时用战斗屏记录的我方 team。</summary>
        public void Show(BattleEndS2C end, uint fallbackTeamIndex)
        {
            if (end == null || _root == null) return;
            var settlement = end.Settlement;
            eBattleOutcome outcome = settlement != null ? settlement.Outcome : end.Outcome;
            uint myTeam = settlement != null ? settlement.PlayerTeamIndex : fallbackTeamIndex;

            string title;
            Color titleColor;
            if (outcome == eBattleOutcome.BattleOutcomeDraw)
            {
                title = "平 局";
                titleColor = QdaoUguiTheme.MutedBrown;
            }
            else if ((outcome == eBattleOutcome.BattleOutcomeSideAWin && myTeam == 0) ||
                     (outcome == eBattleOutcome.BattleOutcomeSideBWin && myTeam == 1))
            {
                title = "胜 利";
                titleColor = BattleUiStyle.WarnText;
            }
            else if (outcome == eBattleOutcome.BattleOutcomeOngoing)
            {
                title = "战斗结束";
                titleColor = QdaoUguiTheme.Cream;
            }
            else
            {
                title = "失 败";
                titleColor = BattleUiStyle.DamageText;
            }
            _titleText.text = title;
            _titleText.color = titleColor;
            _roundsText.text = settlement != null ? $"共 {settlement.TotalRounds} 回合" : string.Empty;

            var lines = BuildRewardLines(settlement);
            KillTweens();
            ClearRows();
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _group.alpha = 1f;

            // 大字弹入
            var titleRect = _titleText.rectTransform;
            titleRect.localScale = Vector3.one * 2.4f;
            var tc = _titleText.color; tc.a = 0f; _titleText.color = tc;
            GTween.To(2.4f, 1f, 0.42f).SetEase(EaseType.BackOut).SetIgnoreEngineTimeScale(true).SetTarget(_token)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (titleRect == null) return;
                    titleRect.localScale = new Vector3(t.value.x, t.value.x, 1f);
                    var c = _titleText.color;
                    c.a = Mathf.Clamp01((2.4f - t.value.x) / 1.0f);
                    _titleText.color = c;
                }));

            // 奖励逐条飞入
            float rowsW = PanelW - 240f;
            for (int i = 0; i < lines.Count; i++)
            {
                var text = QdaoUguiFactory.CreateText($"Row{i}", _rows, 0f, i * RowHeight, rowsW, RowHeight,
                    lines[i], 26f, QdaoUguiTheme.StatusCream);
                var go = text.gameObject;
                _rowObjects.Add(go);
                var rect = text.rectTransform;
                var c0 = text.color; c0.a = 0f; text.color = c0;
                float rowY = -i * RowHeight;
                GTween.To(0f, 1f, 0.3f).SetDelay(0.45f + i * RowDelay).SetEase(EaseType.CubicOut).SetIgnoreEngineTimeScale(true).SetTarget(_token)
                    .OnUpdate((GTweenCallback1)(t =>
                    {
                        if (go == null) return;
                        float k = t.value.x;
                        rect.anchoredPosition = new Vector2(260f * (1f - k), rowY);
                        var c = text.color; c.a = k; text.color = c;
                    }));
            }

            // 确认键淡入(等最后一条飞入完)
            _confirmGroup.alpha = 0f;
            _confirmGroup.interactable = false;
            _confirmGroup.blocksRaycasts = false;
            float confirmDelay = 0.45f + lines.Count * RowDelay + 0.2f;
            GTween.To(0f, 1f, 0.3f).SetDelay(confirmDelay).SetIgnoreEngineTimeScale(true).SetTarget(_token)
                .OnUpdate((GTweenCallback1)(t => { if (_confirmGroup != null) _confirmGroup.alpha = t.value.x; }))
                .OnComplete((GTweenCallback)(() =>
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

        /// <summary>奖励行文案(纯逻辑,EditMode 可测)。</summary>
        public static List<string> BuildRewardLines(BattleSettlementData settlement)
        {
            var lines = new List<string>();
            if (settlement == null)
            {
                lines.Add("(未收到结算数据)");
                return lines;
            }
            lines.Add($"经验  +{settlement.ExpGain}");
            lines.Add($"金钱  +{settlement.GoldGain}");
            if (settlement.ItemsGained != null && settlement.ItemsGained.Count > 0)
            {
                foreach (var item in settlement.ItemsGained)
                    lines.Add($"获得  道具{item.ItemTableId} ×{item.Count}");
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

        private void KillTweens()
        {
            GTween.Kill(_token);
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
