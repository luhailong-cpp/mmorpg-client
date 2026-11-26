using System;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using UnityEngine;
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

        public static EquipDetailCardData BuildData(BagItemInfo item, bool worn, bool actionEnabled, uint characterLevel)
        {
            var data = new EquipDetailCardData {
                Name = EquipDisplay.NameWithEnhance(ItemName(item), item.EnhanceLevel), IconKey = item.IconKey, State = worn ? "已装备" : "未装备",
                LevelRequirement = EquipBagModel.LevelRequirement(item),
                ClassRequirement = EquipBagModel.ClassRequirement(item),
                LevelUnmet = EquipBagModel.LevelUnmet(item, characterLevel), Description = item.Description,
                ActionLabel = worn ? "卸下" : "装备", ActionEnabled = actionEnabled,
                ItemLevelText = item.EquipLevel == 0 ? "" : item.EquipLevel.ToString(),
            };
            foreach (var line in EquipDisplay.LinesOf(item))
                data.Lines.Add(new EquipDetailLine(EquipDisplay.FormatLine(line),
                    EquipUiStyle.TierColor(EquipDisplay.TierOf(line.Tier))));
            foreach (var slot in item.FixedAffixSlots.OrderBy(entry => entry.Slot).ThenBy(entry => entry.Pool))
            {
                string label = $"{EquipmentScreenModel.PoolName(slot.Pool)} {slot.Slot}";
                if (slot.Selected == null)
                {
                    data.Lines.Add(new EquipDetailLine($"{label}：空位（未选择）", EquipUiSkin.Muted));
                    continue;
                }
                var affix = slot.Selected;
                string quality = EquipmentScreenModel.FormatAffixQualityTag(affix);
                Color color = affix.QualityTier >= 5 ? EquipUiSkin.Gold
                    : affix.QualityTier == 4 ? EquipUiStyle.TierPink
                    : EquipUiStyle.TierBlue;
                data.Lines.Add(new EquipDetailLine(
                    $"{label}：{affix.Name} {EquipmentScreenModel.AffixValue(affix)}{quality}", color));
                string pity = EquipmentScreenModel.FormatPolishPity(affix);
                if (!string.IsNullOrEmpty(pity))
                    data.Lines.Add(new EquipDetailLine(pity, EquipUiSkin.Muted));
            }
            if (item.Artifact != null)
            {
                data.Lines.Add(new EquipDetailLine($"法宝：{item.Artifact.Name}", EquipUiSkin.Gold));
                if (!string.IsNullOrWhiteSpace(item.Artifact.Description))
                    data.Lines.Add(new EquipDetailLine(item.Artifact.Description, EquipUiSkin.Ink));
            }
            foreach (var socket in EquipDisplay.SocketsOf(item))
                data.Lines.Add(new EquipDetailLine(EquipDisplay.FormatSocketLine(socket),
                    socket.GemConfigId != 0 && socket.Attr != null ? EquipUiSkin.Green : EquipUiSkin.Muted));
            if (!string.IsNullOrWhiteSpace(item.CrafterName))
                data.Lines.Add(new EquipDetailLine($"宗师·{item.CrafterName} 亲手铸造", EquipUiSkin.Gold));
            return data;
        }

        public void Show(BagItemInfo item, bool worn, bool actionEnabled, uint characterLevel, Action action,
            Action more = null, Action share = null)
        {
            if (item == null) { Hide(); return; }
            _card.Show(BuildData(item, worn, actionEnabled, characterLevel), action, more, share);
        }
        public void Hide() => _card.Hide();
    }
}

