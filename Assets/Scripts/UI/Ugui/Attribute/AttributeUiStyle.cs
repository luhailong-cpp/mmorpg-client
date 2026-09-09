using UnityEngine;

namespace MmorpgClient.UI.Ugui.Attribute
{
    /// <summary>
    /// 属性加点界面的视觉常量。基调沿用 <see cref="QdaoUguiTheme"/>(玉绿金饰 + 奶油纸面),
    /// 与战斗层深色底板区分:属性面板是"纸窗",底色偏浅。
    /// </summary>
    public static class AttributeUiStyle
    {
        // ── 窗体布局(设计分辨率 2560x1080,左上角原点,y 向下) ──

        public const float WindowW = 1480f;
        public const float WindowH = 980f;
        public const float WindowX = (QdaoUguiTheme.DesignWidth - WindowW) * 0.5f;
        public const float WindowY = 46f;

        /// <summary>左栏(方案下拉 + 六项二级属性 + 开启新方案)。</summary>
        public const float LeftX = 64f;
        public const float LeftW = 440f;

        /// <summary>右栏(三个池页签 + 加点行 + 重置/确认)。</summary>
        public const float RightX = 552f;
        public const float RightW = 864f;

        // ── 颜色 ──

        public static readonly Color WindowPaper = QdaoUguiTheme.Html("#FFF7DE");
        public static readonly Color WindowBorder = QdaoUguiTheme.Html("#C59645");
        public static readonly Color TitlePlate = QdaoUguiTheme.Html("#176C5F");
        public static readonly Color TitleText = QdaoRefreshArt.Ivory;

        public static readonly Color FieldPlate = QdaoUguiTheme.Html("#F8ECCE");
        public static readonly Color FieldLabel = QdaoUguiTheme.Html("#5A4025");
        public static readonly Color FieldValue = QdaoUguiTheme.Html("#244C3E");

        public static readonly Color TabIdle = QdaoUguiTheme.Html("#F8ECCE");
        public static readonly Color TabActive = QdaoUguiTheme.Html("#176C5F");
        public static readonly Color TabText = QdaoUguiTheme.Html("#344D43");
        public static readonly Color TabLockedText = QdaoUguiTheme.Html("#9A8B77");

        public static readonly Color RowName = QdaoUguiTheme.Html("#344D43");
        public static readonly Color RowValue = QdaoUguiTheme.Html("#166B5B");

        public static readonly Color SliderTrack = QdaoUguiTheme.Html("#CDBA86");
        public static readonly Color SliderFill = QdaoUguiTheme.Html("#287B66");
        public static readonly Color SliderHandle = QdaoUguiTheme.Html("#EFE6D2");

        public static readonly Color StepPlate = QdaoUguiTheme.Html("#D8C49C");
        public static readonly Color StepText = QdaoUguiTheme.Html("#244C3E");

        public static readonly Color ActionPlate = QdaoUguiTheme.Html("#D8C49C");
        public static readonly Color ConfirmPlate = QdaoUguiTheme.Html("#176C5F");
        public static readonly Color ActionText = QdaoUguiTheme.Html("#244C3E");

        public static readonly Color ClosePlate = QdaoUguiTheme.Html("#B4322A");
        public static readonly Color CloseText = QdaoUguiTheme.Cream;

        public static readonly Color HintText = QdaoUguiTheme.Html("#7C6043");
        public static readonly Color RemainText = QdaoUguiTheme.Html("#166B5B");
        public static readonly Color WarnText = QdaoUguiTheme.Html("#B4322A");

        public static readonly Color TooltipPlate = new Color(0.11f, 0.09f, 0.07f, 0.93f);
        public static readonly Color TooltipText = QdaoUguiTheme.Cream;

        /// <summary>HUD 入口按钮:右侧入口列的第三个位置(战斗 / 观战 / 角色),尺寸风格同 <see cref="Battle.BattleUiStyle"/>。</summary>
        public const float EntryX = Battle.BattleUiStyle.HudEntryX;
        public const float EntryY = Battle.BattleUiStyle.HudEntryFirstY
                                    + 2f * (Battle.BattleUiStyle.HudEntryHeight + Battle.BattleUiStyle.HudEntryGap);
    }
}
