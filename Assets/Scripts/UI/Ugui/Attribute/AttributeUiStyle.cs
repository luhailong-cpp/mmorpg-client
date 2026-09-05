using UnityEngine;

namespace MmorpgClient.UI.Ugui.Attribute
{
    /// <summary>
    /// 属性加点界面的视觉常量。基调沿用 <see cref="QdaoUguiTheme"/>(问道式棕木 + 米黄纸面),
    /// 与战斗层深色底板区分:属性面板是"纸窗",底色偏浅。
    /// </summary>
    public static class AttributeUiStyle
    {
        // ── 窗体布局(设计分辨率 2560x1080,左上角原点,y 向下) ──

        public const float WindowW = 1240f;
        public const float WindowH = 980f;
        public const float WindowX = (QdaoUguiTheme.DesignWidth - WindowW) * 0.5f;
        public const float WindowY = 46f;

        /// <summary>左栏(方案下拉 + 六项二级属性 + 开启新方案)。</summary>
        public const float LeftX = 26f;
        public const float LeftW = 400f;

        /// <summary>右栏(三个池页签 + 加点行 + 重置/确认)。</summary>
        public const float RightX = 452f;
        public const float RightW = 762f;

        // ── 颜色 ──

        public static readonly Color WindowPaper = QdaoUguiTheme.Html("#F3E7D2");
        public static readonly Color WindowBorder = QdaoUguiTheme.Html("#8C6A3F");
        public static readonly Color TitlePlate = QdaoUguiTheme.Html("#C9A15E");
        public static readonly Color TitleText = QdaoUguiTheme.Html("#4A3316");

        public static readonly Color FieldPlate = QdaoUguiTheme.Html("#E7D9C0");
        public static readonly Color FieldLabel = QdaoUguiTheme.Html("#5A4025");
        public static readonly Color FieldValue = QdaoUguiTheme.Html("#3D2914");

        public static readonly Color TabIdle = QdaoUguiTheme.Html("#E7D9C0");
        public static readonly Color TabActive = QdaoUguiTheme.Html("#BFE0BC");
        public static readonly Color TabText = QdaoUguiTheme.Html("#4A3316");
        public static readonly Color TabLockedText = QdaoUguiTheme.Html("#9A8B77");

        public static readonly Color RowName = QdaoUguiTheme.Html("#4A3316");
        public static readonly Color RowValue = QdaoUguiTheme.Html("#2E6FBF");

        public static readonly Color SliderTrack = QdaoUguiTheme.Html("#D6C6A8");
        public static readonly Color SliderFill = QdaoUguiTheme.Html("#5FA35A");
        public static readonly Color SliderHandle = QdaoUguiTheme.Html("#EFE6D2");

        public static readonly Color StepPlate = QdaoUguiTheme.Html("#D8C49C");
        public static readonly Color StepText = QdaoUguiTheme.Html("#3D2914");

        public static readonly Color ActionPlate = QdaoUguiTheme.Html("#D8C49C");
        public static readonly Color ConfirmPlate = QdaoUguiTheme.Html("#9FD09A");
        public static readonly Color ActionText = QdaoUguiTheme.Html("#3D2914");

        public static readonly Color ClosePlate = QdaoUguiTheme.Html("#B4322A");
        public static readonly Color CloseText = QdaoUguiTheme.Cream;

        public static readonly Color HintText = QdaoUguiTheme.Html("#7C6043");
        public static readonly Color RemainText = QdaoUguiTheme.Html("#2E6FBF");
        public static readonly Color WarnText = QdaoUguiTheme.Html("#B4322A");

        public static readonly Color TooltipPlate = new Color(0.11f, 0.09f, 0.07f, 0.93f);
        public static readonly Color TooltipText = QdaoUguiTheme.Cream;

        /// <summary>HUD 入口按钮(挂在战斗入口之下)。</summary>
        public const float EntryX = 2350f;
        public const float EntryY = 382f;
    }
}
