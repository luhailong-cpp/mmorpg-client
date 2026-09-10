using UnityEngine;
using MmorpgClient.UI.Ugui.Attribute;

namespace MmorpgClient.UI.Ugui.Pet
{
    /// <summary>
    /// 宝宝界面的视觉常量。**颜色全部复用 <see cref="AttributeUiStyle"/>** —— 两个窗是同一套
    /// "纸窗 + 玉绿金饰",复制一份配色只会在改主题时漏改一边。这里只放宝宝独有的布局尺寸。
    /// </summary>
    public static class PetUiStyle
    {
        // ── 窗体布局(设计分辨率 2560x1080,左上角原点,y 向下) ──

        public const float WindowW = 1480f;
        public const float WindowH = 980f;
        public const float WindowX = (QdaoUguiTheme.DesignWidth - WindowW) * 0.5f;
        public const float WindowY = 46f;

        /// <summary>左栏(宝宝列表 + 出战/收回)。</summary>
        public const float LeftX = 64f;
        public const float LeftW = 380f;

        /// <summary>中栏(六项二级属性 + 资质/成长率)。</summary>
        public const float MidX = 472f;
        public const float MidW = 380f;

        /// <summary>右栏(剩余点 + 自动加点 + 四行加点 + 洗点/确认)。</summary>
        public const float RightX = 880f;
        public const float RightW = 536f;

        /// <summary>
        /// 宝宝列表每项的高度与间距。
        /// 约束:列表从 y=140 起排 MaxPetItems 项,末项底边必须落在「出战」按钮
        /// (y = WindowH - 116 = 864)之上,否则满槽时最后一只点不到。
        /// 当前:140 + 9×70 + 64 = 834 < 864 ✓(改这三个数时一起验算)。
        /// </summary>
        public const float PetItemHeight = 64f;
        public const float PetItemGap = 6f;

        /// <summary>列表最多渲染多少项(超出的靠翻页;PetRule.max_pets 默认 10)。</summary>
        public const int MaxPetItems = 10;

        // ── 颜色(转发,不新造一套) ──

        public static readonly Color WindowPaper = AttributeUiStyle.WindowPaper;
        public static readonly Color TitleText = AttributeUiStyle.TitleText;
        public static readonly Color FieldPlate = AttributeUiStyle.FieldPlate;
        public static readonly Color FieldLabel = AttributeUiStyle.FieldLabel;
        public static readonly Color ItemActivePlate = AttributeUiStyle.TabActive;
        public static readonly Color ActionPlate = AttributeUiStyle.ActionPlate;
        public static readonly Color ConfirmPlate = AttributeUiStyle.ConfirmPlate;
        public static readonly Color ActionText = AttributeUiStyle.ActionText;
        public static readonly Color ClosePlate = AttributeUiStyle.ClosePlate;
        public static readonly Color CloseText = AttributeUiStyle.CloseText;
        public static readonly Color HintText = AttributeUiStyle.HintText;
        public static readonly Color RemainText = AttributeUiStyle.RemainText;
        public static readonly Color WarnText = AttributeUiStyle.WarnText;
        public static readonly Color TooltipPlate = AttributeUiStyle.TooltipPlate;
        public static readonly Color TooltipText = AttributeUiStyle.TooltipText;

        /// <summary>HUD 入口按钮:右侧入口列的第四个位置(战斗 / 观战 / 角色 / 宝宝)。</summary>
        public const float EntryX = Battle.BattleUiStyle.HudEntryX;
        public const float EntryY = Battle.BattleUiStyle.HudEntryFirstY
                                    + 3f * (Battle.BattleUiStyle.HudEntryHeight + Battle.BattleUiStyle.HudEntryGap);
    }
}
