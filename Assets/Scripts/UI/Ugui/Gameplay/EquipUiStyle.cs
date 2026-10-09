using MmorpgClient.Game.PlayerFeatures;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 背包页装备栏与装备 tooltip 的视觉常量(设计分辨率 2560x1080,左上角原点,y 向下)。
    /// 属性档次沿用服务端契约;颜色使用象牙纸面上可读的深色同色系。
    /// </summary>
    public static class EquipUiStyle
    {
        // ── 背包页分栏(GameplayWindow 的 PageContent 内坐标,总宽 2040):分类栏 | 装备栏 | 物品格 | 详情栏 ──

        /// <summary>分类栏、装备栏、详情栏共用的栏高。</summary>
        public const float PanelH = 614f;

        /// <summary>装备栏:一列槽位,夹在分类栏(0..338)与物品格之间。</summary>
        public const float EquipColumnX = 352f;
        public const float EquipColumnW = 136f;
        /// <summary>槽位区在栏内的起点(上面是「装备栏」标题)与底部留白。</summary>
        public const float EquipSlotsTop = 62f;
        public const float EquipSlotsBottomPad = 10f;
        public const float EquipSlotGap = 6f;
        /// <summary>槽位少时每格的最大间距;槽位多了按栏高均分,一页最多 <see cref="EquipSlotsPerPage"/> 格。</summary>
        public const float EquipSlotMaxPitch = 108f;
        public const int EquipSlotsPerPage = 8;
        public const float EquipPagerH = 46f;

        /// <summary>物品格:7 列,列距比加装备栏之前收了 2(格子本身 148x140 不变)。</summary>
        public const float GridX = 502f;
        public const float GridPitchX = 156f;
        public const float GridW = 6f * GridPitchX + 148f;

        /// <summary>详情栏;装备 tooltip 卡片盖在它上面。</summary>
        public const float DetailX = 1600f;
        public const float DetailW = 440f;

        // ── tooltip 卡片 ──

        /// <summary>详情卡的首选挂载位置;卡片按完整属性高度展开并夹在父窗口内。</summary>
        public const float TooltipInsetX = 22f;
        public const float TooltipInsetY = 8f;
        public const float TooltipW = EquipDetailCard.Width;
        /// <summary>卡片至少盖住详情栏内容区;更多属性进入滚动视口,不无限缩小文字。</summary>
        public const float TooltipMinH = EquipDetailCard.MinHeight;
        public const float TooltipMaxH = EquipDetailCard.MaxHeight;
        public const float TooltipPad = EquipDetailCard.Pad;
        public const float TooltipIconFrameW = 104f;
        public const float TooltipIconFrameH = 96f;
        public const float TooltipIcon = 72f;
        public const float TooltipNameFont = 36f;
        public const float TooltipStateFont = 24f;
        public const float TooltipRequireFont = 28f;
        public const float TooltipRequireH = 38f;
        public const float TooltipLineFont = 28f;
        public const float TooltipLinePitch = 40f;
        /// <summary>物品说明:排在属性行与按钮之间,至少留两行的高度;卡片没长到上限时用掉中间的全部空白。</summary>
        public const float TooltipDescFont = 24f;
        public const float TooltipDescGap = 8f;
        public const float TooltipDescMinH = 64f;
        public const float TooltipActionH = 64f;
        public const float TooltipActionGap = 14f;
        public const float TooltipBorder = 2f;

        public static readonly Color TooltipPlate = EquipUiSkin.Paper;
        public static readonly Color TooltipEdge = new Color(0.77f, 0.59f, 0.27f, 0.85f);
        public static readonly Color TooltipName = EquipUiSkin.HeaderText;
        /// <summary>基础属性行、已满足的要求行。</summary>
        public static readonly Color TooltipText = EquipUiSkin.Ink;
        public static readonly Color TooltipMuted = EquipUiSkin.Muted;

        public static readonly Color TierBlue = EquipUiSkin.Blue;
        public static readonly Color TierPink = EquipUiSkin.Pink;
        public static readonly Color TierYellow = EquipUiSkin.Gold;
        /// <summary>预留:本期服务器不掷绿色属性。</summary>
        public static readonly Color TierGreen = EquipUiSkin.Green;
        /// <summary>不满足佩戴要求。</summary>
        public static readonly Color Unmet = EquipUiSkin.Unmet;

        /// <summary>属性行颜色。基础属性与客户端不认识的档(服务器先加了新档)都用正文色,不猜颜色。</summary>
        public static Color TierColor(EquipAttrTier tier) => tier switch
        {
            EquipAttrTier.Blue => TierBlue,
            EquipAttrTier.Pink => TierPink,
            EquipAttrTier.Yellow => TierYellow,
            EquipAttrTier.Green => TierGreen,
            _ => TooltipText,
        };
    }
}
