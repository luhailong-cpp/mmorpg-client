using System;
using MmorpgClient.Game.PlayerFeatures;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>Existing equipment protocol adapter. Gameplay state and requests remain owned by the caller.</summary>
    public sealed class EquipTooltip
    {
        public const string RootName = EquipDetailCard.RootName;
        public const string ActionName = EquipDetailCard.ActionName;
        public const string LinePrefix = EquipDetailCard.LinePrefix;
        public const string DescriptionName = EquipDetailCard.DescriptionName;
        private readonly EquipDetailCard _card;
        public EquipTooltip(UnityEngine.Transform parent, float x, float y) => _card = new EquipDetailCard(parent, x, y);
        public void Show(BagItemInfo item, bool worn, bool actionEnabled, uint characterLevel, Action action)
        {
            if (item == null) { Hide(); return; }
            var data = new EquipDetailCardData {
                Name = ItemName(item), IconKey = item.IconKey, State = worn ? "已穿戴" : "未穿戴",
                LevelRequirement = EquipBagModel.LevelRequirement(item),
                ClassRequirement = EquipBagModel.ClassRequirement(item),
                LevelUnmet = EquipBagModel.LevelUnmet(item, characterLevel), Description = item.Description,
                ActionLabel = worn ? "卸下" : "装备", ActionEnabled = actionEnabled,
            };
            foreach (var line in EquipDisplay.LinesOf(item))
                data.Lines.Add(new EquipDetailLine(EquipDisplay.FormatLine(line),
                    EquipUiStyle.TierColor(EquipDisplay.TierOf(line.Tier))));
            _card.Show(data, action);
        }
        public void Hide() => _card.Hide();
    }
}
