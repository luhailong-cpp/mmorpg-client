using UnityEngine;
using MmorpgClient.UI.Ugui.Attribute;

namespace MmorpgClient.UI.Ugui.Pet
{
    /// <summary>宝宝效果图的原生布局常量，设计坐标 2560 × 1080。</summary>
    public static class PetUiStyle
    {
        public const float WindowW = 2010f;
        public const float WindowH = 918f;
        public const float WindowX = 224f;
        public const float WindowY = 102f;
        public const float LeftX = 72f;
        public const float LeftW = 530f;
        public const float MidX = 724f;
        public const float MidW = 530f;
        public const float RightX = MidX;
        public const float RightW = 1122f;
        public const float PetItemHeight = 160f;
        public const float PetItemGap = 10f;
        public const float RosterHeight = 670f;
        public const int MaxPetItems = 10;

        public static readonly Color WindowPaper = AttributeUiStyle.WindowPaper;
        public static readonly Color TitleText = QdaoRefreshArt.Ivory;
        public static readonly Color FieldPlate = QdaoUguiTheme.Html("#F3EAD8");
        public static readonly Color ValuePlate = QdaoUguiTheme.Html("#E9E1D1");
        public static readonly Color FieldLabel = QdaoUguiTheme.Html("#4B3925");
        public static readonly Color FieldValue = QdaoUguiTheme.Html("#284E3E");
        public static readonly Color PointValue = QdaoUguiTheme.Html("#2685B2");
        public static readonly Color Gold = QdaoUguiTheme.Html("#C6AB72");
        public static readonly Color ItemActivePlate = AttributeUiStyle.TabActive;
        public static readonly Color ActionPlate = AttributeUiStyle.ActionPlate;
        public static readonly Color ConfirmPlate = AttributeUiStyle.ConfirmPlate;
        public static readonly Color ActionText = AttributeUiStyle.ActionText;
        public static readonly Color ClosePlate = AttributeUiStyle.ClosePlate;
        public static readonly Color CloseText = AttributeUiStyle.CloseText;
        public static readonly Color HintText = QdaoUguiTheme.Html("#7A694A");
        public static readonly Color RemainText = QdaoUguiTheme.Html("#305D48");
        public static readonly Color WarnText = AttributeUiStyle.WarnText;
        public static readonly Color TooltipPlate = AttributeUiStyle.TooltipPlate;
        public static readonly Color TooltipText = AttributeUiStyle.TooltipText;
        public const float EntryX = Battle.BattleUiStyle.HudEntryX;
        public const float EntryY = Battle.BattleUiStyle.HudEntryFirstY
            + 3f * (Battle.BattleUiStyle.HudEntryHeight + Battle.BattleUiStyle.HudEntryGap);
    }
}
