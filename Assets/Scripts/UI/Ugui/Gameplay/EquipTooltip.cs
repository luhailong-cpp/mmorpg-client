using System;
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
            foreach (var v1 in EquipmentScreenModel.BagTooltipV1Lines(item))
                data.Lines.Add(new EquipDetailLine(v1.Text, ToneColor(v1.Tone)));
            foreach (var socket in EquipDisplay.SocketsOf(item))
                data.Lines.Add(new EquipDetailLine(EquipDisplay.FormatSocketLine(socket),
                    socket.GemConfigId != 0 && socket.Attr != null ? EquipUiSkin.Green : EquipUiSkin.Muted));
            return data;
        }

        private static Color ToneColor(EquipmentScreenModel.TooltipLineTone tone) => tone switch
        {
            EquipmentScreenModel.TooltipLineTone.Supreme => EquipUiSkin.Gold,
            EquipmentScreenModel.TooltipLineTone.Precious => EquipUiStyle.TierPink,
            EquipmentScreenModel.TooltipLineTone.Rare => EquipUiStyle.TierBlue,
            EquipmentScreenModel.TooltipLineTone.Muted => EquipUiSkin.Muted,
            _ => EquipUiSkin.Ink,
        };

        public void Show(BagItemInfo item, bool worn, bool actionEnabled, uint characterLevel, Action action,
            Action more = null, Action share = null)
        {
            if (item == null) { Hide(); return; }
            _card.Show(BuildData(item, worn, actionEnabled, characterLevel), action, more, share);
        }
        public void Hide() => _card.Hide();
    }
}

