using System.Text;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 结算屏:OnBattleEnd 后展示胜利/失败/平局 + 经验/金钱/道具变动列表,
    /// 点「返回场景」收起战斗 UI。
    /// </summary>
    public sealed class BattleResultPanel
    {
        private const float PanelW = 900f;
        private const float PanelH = 720f;

        private readonly RectTransform _root;
        private readonly TMP_Text _titleText;
        private readonly TMP_Text _roundsText;
        private readonly TMP_Text _bodyText;
        private readonly UiTextButton _confirmButton;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public BattleResultPanel(UnityEngine.Transform parent, System.Action onConfirm)
        {
            float x = (QdaoUguiTheme.DesignWidth - PanelW) * 0.5f;
            float y = (QdaoUguiTheme.DesignHeight - PanelH) * 0.5f;
            var bg = BattleUiWidgets.CreatePanel("BattleResultPanel", parent,
                x, y, PanelW, PanelH, BattleUiStyle.PanelBgLight);
            _root = (RectTransform)bg.transform;

            _titleText = QdaoUguiFactory.CreateText("Title", _root, 0f, 42f, PanelW, 72f,
                string.Empty, 52f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            _roundsText = QdaoUguiFactory.CreateText("Rounds", _root, 0f, 132f, PanelW, 36f,
                string.Empty, 24f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.Center);
            _bodyText = BattleUiWidgets.CreateWrappedText("Body", _root, 120f, 196f, PanelW - 240f, 380f,
                string.Empty, 26f, QdaoUguiTheme.StatusCream);

            _confirmButton = BattleUiWidgets.CreateTextButton("Confirm", _root,
                (PanelW - 260f) * 0.5f, PanelH - 116f, 260f, 76f,
                "返回场景", 26f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
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

            var sb = new StringBuilder();
            if (settlement != null)
            {
                sb.Append("经验:+").Append(settlement.ExpGain).Append('\n');
                sb.Append("金钱:+").Append(settlement.GoldGain).Append('\n');
                sb.Append("获得道具:").Append(FormatItems(settlement.ItemsGained)).Append('\n');
                sb.Append("消耗道具:").Append(FormatItems(settlement.ItemsConsumed)).Append('\n');
                sb.Append("战后状态:HP ").Append(settlement.Health)
                  .Append(" / MP ").Append(settlement.Mana);
                if (settlement.IsDead) sb.Append("(阵亡)");
                if (settlement.Fled) sb.Append("(中途逃离)");
            }
            else
            {
                sb.Append("(未收到结算数据)");
            }
            _bodyText.text = sb.ToString();

            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private static string FormatItems(Google.Protobuf.Collections.RepeatedField<BattleItemEntry> items)
        {
            if (items == null || items.Count == 0) return "无";
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append('、');
                sb.Append("道具").Append(items[i].ItemTableId).Append('×').Append(items[i].Count);
            }
            return sb.ToString();
        }
    }
}
