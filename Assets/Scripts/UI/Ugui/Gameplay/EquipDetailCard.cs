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
        public const float Pad = 38, LineFont = 28, LinePitch = 40, MinLineFont = 24;
        public const float BottomPad = 22;
        public const float DescriptionFont = 24, DescriptionGap = 8, DescriptionMinHeight = 64;
        public const float ActionHeight = 64, ActionGap = 14;
        public static readonly Color TextColor = EquipUiSkin.Ink;
        public static readonly Color MutedColor = EquipUiSkin.Muted;
        public static readonly Color UnmetColor = EquipUiSkin.Unmet;
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
            bool skinned = EquipUiSkin.Apply(_plate, "tooltip_panel", EquipUiSkin.Paper);
            float inner = Width - Pad * 2;
            if (!skinned)
                QdaoUguiFactory.CreateImage("TooltipHeader", _root, 8, 8, Width - 16, 118, null).color = EquipUiSkin.Jade;
            // The jade nameplate is fixed by the sprite's top nine-slice border; the icon and
            // requirements sit on paper below it, so the central taiji crest remains clear.
            var name = Text(_root, data.Name, 64, 50, Width - 128, 36, 30,
                EquipUiSkin.HeaderText, alignment: TextAlignmentOptions.Center);
            name.name = "TooltipName";
            name.enableAutoSizing = true; name.fontSizeMin = 24; name.fontSizeMax = 30;
            Art(_root, "portrait_frame", Pad, 136, 104, 96);
            ItemIcon(_root, data.IconKey, Pad + 16, 147, 72);
            Text(_root, data.State, 150, 140, Width - 150 - Pad, 28, 24, EquipUiSkin.Jade).name = "TooltipState";
            float y = 172;
            Text(_root, data.LevelRequirement, 150, y, Width - 150 - Pad, 28, 24,
                data.LevelUnmet ? UnmetColor : TextColor).name = "TooltipLevel";
            y += 28;
            Text(_root, data.ClassRequirement, 150, y, Width - 150 - Pad, 28, 24, TextColor).name = "TooltipClass";
            Section("TooltipAttributesHeading", "装备属性", 240, inner);
            y = 268;

            bool described = !string.IsNullOrWhiteSpace(data.Description);
            float footer = ActionGap + ActionHeight + BottomPad;
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
                var label = Text(linesParent, row.Text, lineX, rowY, inner, pitch, font, EquipUiSkin.PaperInk(row.Color), scroll);
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
            EquipUiSkin.Button(_root, ActionName, data.ActionLabel, Pad, height - BottomPad - ActionHeight,
                inner, ActionHeight, action, true, data.ActionEnabled && action != null);
            if (!skinned)
            {
                Rule(0, 0, Width, 2); Rule(0, height - 2, Width, 2);
                Rule(0, 0, 2, height); Rule(Width - 2, 0, 2, height);
            }
            _root.SetAsLastSibling(); _root.gameObject.SetActive(true);
        }

        public void Hide() => _root.gameObject.SetActive(false);
        private void Section(string name, string label, float y, float inner)
        {
            Text(_root, label, Pad, y, 104, 22, 20, MutedColor).name = name;
            Rule(Pad + 112, y + 11, inner - 112, 1);
        }
        private void Rule(float x, float y, float width, float height)
            => QdaoUguiFactory.CreateImage("Rule", _root, x, y, width, height, null).color = EdgeColor;
    }
}
