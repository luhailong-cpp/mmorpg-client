using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>Presentation data only. The caller supplies authoritative labels, values and actions.</summary>
    public sealed class EquipDetailCardData
    {
        public string Name = "", IconKey = "", State = "", LevelRequirement = "", ClassRequirement = "";
        public string Description = "", ActionLabel = "";
        public bool LevelUnmet, ActionEnabled;
        public readonly List<EquipDetailLine> Lines = new List<EquipDetailLine>();
    }

    public readonly struct EquipDetailLine
    {
        public readonly string Text;
        public readonly Color Color;
        public EquipDetailLine(string text, Color color) { Text = text ?? ""; Color = color; }
    }

    /// <summary>Independent native card usable by the existing protocol adapter and isolated editor fixtures.</summary>
    public sealed class EquipDetailCard
    {
        public const string RootName = "EquipTooltip", ActionName = "EquipAction";
        public const string LinePrefix = "TooltipLine_", DescriptionName = "TooltipDescription";
        public const float Width = 396, MinHeight = 598, MaxHeight = 692;
        public const float Pad = 22, LineFont = 28, LinePitch = 40, MinLineFont = 24;
        public const float DescriptionFont = 24, DescriptionGap = 8, DescriptionMinHeight = 64;
        public const float ActionHeight = 64, ActionGap = 14;
        public static readonly Color TextColor = QdaoUguiTheme.Html("#FFF3D6");
        public static readonly Color MutedColor = QdaoUguiTheme.Html("#B9AE95");
        public static readonly Color UnmetColor = QdaoUguiTheme.Html("#FF5540");
        private static readonly Color EdgeColor = new Color(.77f, .59f, .27f, .85f);
        private readonly RectTransform _root;
        private readonly Image _plate;

        public EquipDetailCard(UnityEngine.Transform parent, float x, float y)
        {
            _root = QdaoUguiFactory.CreateRect(RootName, parent, x, y, Width, MinHeight);
            _plate = _root.gameObject.AddComponent<Image>();
            _plate.raycastTarget = true;
            Hide();
        }

        public void Show(EquipDetailCardData data, Action action)
        {
            if (data == null) { Hide(); return; }
            Clear(_root);
            bool skinned = EquipUiSkin.Apply(_plate, "tooltip_panel", QdaoUguiTheme.Html("#12392F"));
            float inner = Width - Pad * 2;
            // Keep the source artwork's top medallion clear (top safe area: 74 design pixels).
            Art(_root, "portrait_frame", Pad, 78, 68, 60);
            ItemIcon(_root, data.IconKey, Pad + 9, 82, 50);
            var name = Text(_root, data.Name, Pad + 82, 74, inner - 82, 40, 36,
                QdaoUguiTheme.Html("#FFE9A8"), true);
            name.name = "TooltipName";
            name.enableAutoSizing = true; name.fontSizeMin = 24; name.fontSizeMax = 36;
            Text(_root, data.State, Pad + 82, 116, inner - 82, 26, 24, MutedColor).name = "TooltipState";
            float y = 144;
            Rule(Pad, y, inner, 2); y += 10;
            Text(_root, data.LevelRequirement, Pad, y, inner, 32, 28,
                data.LevelUnmet ? UnmetColor : TextColor).name = "TooltipLevel";
            y += 32;
            Text(_root, data.ClassRequirement, Pad, y, inner, 32, 28, TextColor).name = "TooltipClass";
            y += 40; Rule(Pad, y, inner, 2); y += 12;

            bool described = !string.IsNullOrWhiteSpace(data.Description);
            float footer = ActionGap + ActionHeight + Pad;
            float reserved = described ? DescriptionGap + DescriptionMinHeight : 0;
            float room = MaxHeight - y - footer - reserved;
            float pitch = data.Lines.Count == 0 ? LinePitch : Mathf.Min(LinePitch, room / data.Lines.Count);
            bool scroll = pitch * .72f < MinLineFont;
            // Measure native glyph widths: a short CJK label can be wider than a long numeric value.
            var measure = Text(_root, "", 0, 0, inner, LinePitch, LineFont, TextColor);
            measure.name = "TooltipTextMeasure"; measure.gameObject.SetActive(false);
            foreach (var line in data.Lines)
                if (measure.GetPreferredValues(line.Text ?? "", float.PositiveInfinity, float.PositiveInfinity).x > inner)
                { scroll = true; break; }
            float height, linesEnd;
            UnityEngine.Transform linesParent = _root;
            RectTransform scrollContent = null;
            float lineX = Pad, lineY = y;
            if (scroll)
            {
                height = MaxHeight;
                float viewportHeight = room - 28;
                var viewport = QdaoUguiFactory.CreateRect("TooltipAttributesViewport", _root, Pad, y, inner, viewportHeight);
                viewport.gameObject.AddComponent<RectMask2D>();
                var hit = viewport.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, .001f); hit.raycastTarget = true;
                var content = QdaoUguiFactory.CreateRect("TooltipAttributesContent", viewport, 0, 0, inner, data.Lines.Count * LinePitch);
                var list = viewport.gameObject.AddComponent<ScrollRect>();
                list.viewport = viewport; list.content = content; list.horizontal = false;
                list.movementType = ScrollRect.MovementType.Clamped; list.scrollSensitivity = 35; list.inertia = false;
                linesParent = content; scrollContent = content; lineX = 0; lineY = 0; pitch = LinePitch;
                Text(_root, "滚动查看全部属性", Pad, y + viewportHeight + 2, inner, 26, 21, MutedColor,
                    alignment: TextAlignmentOptions.Center).name = "TooltipScrollHint";
                linesEnd = y + room;
            }
            else
            {
                linesEnd = y + data.Lines.Count * pitch;
                height = Mathf.Clamp(linesEnd + reserved + footer, MinHeight, MaxHeight);
            }
            float rowY = lineY;
            for (int i = 0; i < data.Lines.Count; ++i)
            {
                var row = data.Lines[i];
                float font = Mathf.Min(LineFont, pitch * .72f);
                var label = Text(linesParent, row.Text, lineX, rowY, inner, pitch, font, row.Color, scroll);
                label.name = LinePrefix + i;
                if (scroll)
                {
                    label.overflowMode = TextOverflowModes.Overflow;
                    float rowHeight = Mathf.Max(pitch, label.GetPreferredValues(row.Text ?? "", inner, float.PositiveInfinity).y + 8);
                    label.rectTransform.sizeDelta = new Vector2(inner, rowHeight);
                    rowY += rowHeight;
                }
                else rowY += pitch;
            }
            if (scrollContent != null) scrollContent.sizeDelta = new Vector2(inner, rowY);
            _root.sizeDelta = new Vector2(Width, height);
            if (described)
            {
                float top = linesEnd + DescriptionGap;
                Text(_root, data.Description, Pad, top, inner, height - footer - top, DescriptionFont,
                    MutedColor, true).name = DescriptionName;
            }
            EquipUiSkin.Button(_root, ActionName, data.ActionLabel, Pad, height - Pad - ActionHeight,
                inner, ActionHeight, action, true, data.ActionEnabled && action != null);
            if (!skinned)
            {
                Rule(0, 0, Width, 2); Rule(0, height - 2, Width, 2);
                Rule(0, 0, 2, height); Rule(Width - 2, 0, 2, height);
            }
            _root.SetAsLastSibling(); _root.gameObject.SetActive(true);
        }

        public void Hide() => _root.gameObject.SetActive(false);
        private void Rule(float x, float y, float width, float height)
            => QdaoUguiFactory.CreateImage("Rule", _root, x, y, width, height, null).color = EdgeColor;
    }
}
