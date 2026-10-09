using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>Presentation-only fields supplied by the caller; no fabricated equipment values.</summary>
    public sealed class EquipDetailCardData
    {
        public string Name = "", IconKey = "", State = "", LevelRequirement = "", ClassRequirement = "";
        public string Description = "", ActionLabel = "";
        public string ItemLevelText = "", ElementText = "", StarsText = "";
        public bool LevelUnmet, ActionEnabled;
        // Optional complete header supplied by richer sources (refinement, role, evolution, etc.).
        // Empty uses the existing level/class contract. Never infer these values from affixes.
        public readonly List<EquipDetailLine> HeaderLines = new List<EquipDetailLine>();
        public readonly List<EquipDetailLine> Lines = new List<EquipDetailLine>();
    }

    public readonly struct EquipDetailLine
    {
        public readonly string Text;
        public readonly Color Color;
        public EquipDetailLine(string text, Color color) { Text = text ?? ""; Color = color; }
    }

    /// <summary>Renders every supplied row, including duplicate labels; long content scrolls intact.</summary>
    public sealed class EquipDetailCard
    {
        public const string RootName = "EquipTooltip", ActionName = "EquipAction";
        public const string LinePrefix = "TooltipLine_", DescriptionName = "TooltipDescription";
        public const float Width = 460, MinHeight = 598, MaxHeight = 880;
        public const float Pad = 38, LineFont = 28, LinePitch = 40, MinLineFont = 24;
        public const float DenseLinePitch = 30, BottomPad = 22;
        public const float DescriptionFont = 24, DescriptionGap = 8, DescriptionMinHeight = 64;
        public const float ActionHeight = 64, ActionGap = 14;
        public static readonly Color TextColor = EquipUiSkin.Ink;
        public static readonly Color MutedColor = EquipUiSkin.Muted;
        public static readonly Color UnmetColor = EquipUiSkin.Unmet;
        private static readonly Color EdgeColor = new Color(.77f, .59f, .27f, .85f);
        private readonly RectTransform _root;
        private readonly Image _plate;
        private readonly float _requestedX, _requestedY;

        public EquipDetailCard(UnityEngine.Transform parent, float x, float y)
        {
            _requestedX = x; _requestedY = y;
            _root = QdaoUguiFactory.CreateRect(RootName, parent, x, y, Width, MinHeight);
            _plate = _root.gameObject.AddComponent<Image>();
            _plate.raycastTarget = true;
            Hide();
        }

        public void Show(EquipDetailCardData data, Action action, Action more = null, Action share = null)
        {
            if (data == null) { Hide(); return; }
            Clear(_root);
            bool skinned = EquipUiSkin.Apply(_plate, "tooltip_panel", EquipUiSkin.Paper);
            float inner = Width - Pad * 2;
            if (!skinned)
                QdaoUguiFactory.CreateImage("TooltipHeader", _root, 8, 8, Width - 16, 104, null).color = EquipUiSkin.Jade;
            var name = Text(_root, data.Name, 64, 50, Width - 128, 36, 30,
                EquipUiSkin.HeaderText, alignment: TextAlignmentOptions.Center);
            name.name = "TooltipName";
            name.enableAutoSizing = true; name.fontSizeMin = 24; name.fontSizeMax = 30;

            Art(_root, "portrait_frame", Pad, 116, 96, 90);
            ItemIcon(_root, data.IconKey, Pad + 12, 125, 72);
            if (!string.IsNullOrEmpty(data.ItemLevelText))
                Text(_root, data.ItemLevelText, Pad + 6, 119, 72, 24, 20, TextColor).name = "TooltipItemLevel";
            if (!string.IsNullOrEmpty(data.ElementText))
                Text(_root, data.ElementText, Pad + 67, 180, 28, 26, 22, UnmetColor).name = "TooltipElement";

            Text(_root, data.State, 146, 112, Width - 146 - Pad - (share == null ? 0 : 76), 28, 24,
                EquipUiSkin.Jade).name = "TooltipState";
            if (share != null)
                EquipUiSkin.Button(_root, "EquipShare", "分享", Width - Pad - 72, 108, 72, 36, share, fontSize: 22);

            float headerY = 142;
            if (data.HeaderLines.Count > 0)
            {
                for (int i = 0; i < data.HeaderLines.Count; i++)
                {
                    var line = data.HeaderLines[i];
                    Text(_root, line.Text, 146, headerY, Width - 146 - Pad, 26, 24,
                        EquipUiSkin.PaperInk(line.Color)).name = "TooltipHeaderLine_" + i;
                    headerY += 26;
                }
            }
            else
            {
                Text(_root, data.LevelRequirement, 146, headerY, Width - 146 - Pad, 28, 24,
                    data.LevelUnmet ? UnmetColor : TextColor).name = "TooltipLevel";
                headerY += 28;
                Text(_root, data.ClassRequirement, 146, headerY, Width - 146 - Pad, 28, 24,
                    TextColor).name = "TooltipClass";
                headerY += 28;
            }
            if (!string.IsNullOrEmpty(data.StarsText))
            {
                Text(_root, data.StarsText, 146, headerY, Width - 146 - Pad, 28, 24,
                    EquipUiSkin.Gold).name = "TooltipStars";
                headerY += 28;
            }
            float sectionY = Mathf.Max(216, headerY + 8);
            Text(_root, "装备属性", Pad, sectionY, 104, 22, 20, MutedColor).name = "TooltipAttributesHeading";
            Rule(Pad + 112, sectionY + 11, inner - 112, 1);
            float bodyY = sectionY + 28;

            bool dense = data.Lines.Count > 8;
            float font = dense ? MinLineFont : LineFont;
            float pitch = dense ? DenseLinePitch : LinePitch;
            var measure = Text(_root, "", 0, 0, inner, pitch, font, TextColor, true);
            measure.name = "TooltipTextMeasure"; measure.gameObject.SetActive(false);
            var rowHeights = new List<float>(data.Lines.Count);
            float bodyHeight = 0;
            foreach (var line in data.Lines)
            {
                float rowHeight = Mathf.Max(pitch, measure.GetPreferredValues(line.Text, inner, float.PositiveInfinity).y + 4);
                rowHeights.Add(rowHeight); bodyHeight += rowHeight;
            }
            bool described = !string.IsNullOrWhiteSpace(data.Description);
            float descriptionHeight = 0;
            if (described)
            {
                measure.fontSize = DescriptionFont;
                descriptionHeight = Mathf.Max(DescriptionMinHeight,
                    measure.GetPreferredValues(data.Description, inner, float.PositiveInfinity).y + 4);
                bodyHeight += DescriptionGap + descriptionHeight;
            }
            float footer = ActionGap + ActionHeight + BottomPad;
            float maxHeight = MaxHeight;
            var parentRect = _root.parent as RectTransform;
            if (parentRect != null && parentRect.rect.height > MinHeight + 32)
                maxHeight = Mathf.Min(maxHeight, parentRect.rect.height - 32);
            float height = Mathf.Clamp(bodyY + bodyHeight + footer, MinHeight, maxHeight);
            float room = height - bodyY - footer;
            bool scroll = bodyHeight > room + .01f;
            UnityEngine.Transform bodyParent = _root;
            float rowX = Pad, rowY = bodyY;

            if (scroll)
            {
                float viewportHeight = Mathf.Max(40, room - 28);
                var viewport = QdaoUguiFactory.CreateRect("TooltipAttributesViewport", _root, Pad, bodyY, inner, viewportHeight);
                viewport.gameObject.AddComponent<RectMask2D>();
                var hit = viewport.gameObject.AddComponent<Image>();
                hit.color = new Color(1, 1, 1, .001f); hit.raycastTarget = true;
                var content = QdaoUguiFactory.CreateRect("TooltipAttributesContent", viewport, 0, 0, inner, bodyHeight);
                var list = viewport.gameObject.AddComponent<ScrollRect>();
                list.viewport = viewport; list.content = content; list.horizontal = false;
                list.movementType = ScrollRect.MovementType.Clamped; list.scrollSensitivity = 35; list.inertia = false;
                bodyParent = content; rowX = 0; rowY = 0;
                Text(_root, $"共 {data.Lines.Count} 条 · 滚动查看全部", Pad, bodyY + viewportHeight + 2, inner, 26, 21,
                    MutedColor, alignment: TextAlignmentOptions.Center).name = "TooltipScrollHint";
            }
            for (int i = 0; i < data.Lines.Count; i++)
            {
                var line = data.Lines[i];
                var label = Text(bodyParent, line.Text, rowX, rowY, inner, rowHeights[i], font,
                    EquipUiSkin.PaperInk(line.Color), true);
                label.name = LinePrefix + i;
                label.overflowMode = TextOverflowModes.Overflow;
                rowY += rowHeights[i];
            }
            if (described)
            {
                var description = Text(bodyParent, data.Description, rowX, rowY + DescriptionGap,
                    inner, descriptionHeight, DescriptionFont, MutedColor, true);
                description.name = DescriptionName;
                description.overflowMode = TextOverflowModes.Overflow;
            }
            _root.sizeDelta = new Vector2(Width, height);
            if (parentRect != null && parentRect.rect.width > 0 && parentRect.rect.height > 0)
                _root.anchoredPosition = new Vector2(
                    Mathf.Clamp(_requestedX, 16, Mathf.Max(16, parentRect.rect.width - Width - 16)),
                    -Mathf.Clamp(_requestedY, 16, Mathf.Max(16, parentRect.rect.height - height - 16)));

            float actionY = height - BottomPad - ActionHeight;
            float actionWidth = more == null ? inner : (inner - 12) / 2;
            if (more != null)
                EquipUiSkin.Button(_root, "EquipMore", "更多", Pad, actionY, actionWidth, ActionHeight, more);
            EquipUiSkin.Button(_root, ActionName, data.ActionLabel,
                more == null ? Pad : Pad + actionWidth + 12, actionY, actionWidth, ActionHeight,
                action, true, data.ActionEnabled && action != null);
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
