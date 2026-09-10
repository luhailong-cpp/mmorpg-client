using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Attribute
{
    /// <summary>Character-only layout for the accepted, affinity-free painted reference.</summary>
    internal static class CharacterAttributeVisuals
    {
        internal const string PaintedRoot = "UI/Ugui/AttributesPaintedV2/";
        internal const float WindowW = 2016f;
        internal const float WindowH = 928f;
        internal const float WindowX = (QdaoUguiTheme.DesignWidth - WindowW) * 0.5f - 32f;
        internal const float WindowY = 102f;
        internal const float LeftX = 100f;
        internal const float LeftW = 516f;
        internal const float RightX = 714f;
        internal const float RightW = 1182f;
        internal static readonly Color ValueBlue = QdaoUguiTheme.Html("#28799C");

        internal static Sprite Load(string name) => QdaoUguiTheme.RequireSprite(PaintedRoot + name);

        internal static void Skin(Image image, string name)
        {
            var sprite = Load(name);
            image.sprite = sprite;
            image.color = Color.white;
            image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = image.type == Image.Type.Simple;
            image.pixelsPerUnitMultiplier = Mathf.Max(0.01f,
                sprite.rect.height / Mathf.Max(1f, image.rectTransform.rect.height));
        }

        internal static Image Panel(string name, UnityEngine.Transform parent, float x, float y,
            float width, float height, string sprite)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, width, height, Load(sprite));
            Skin(image, sprite);
            return image;
        }

        internal static UiTextButton Button(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height, string text, bool primary = false, float fontSize = 34f)
        {
            var control = BattleUiWidgets.CreateTextButton(name, parent, x, y, width, height,
                text, fontSize, Color.white, primary ? QdaoRefreshArt.Ivory : QdaoRefreshArt.Ink);
            Skin(control.Plate, primary ? "button_primary" : "button_secondary");
            control.Button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = control.Button.colors;
            colors.highlightedColor = new Color(1f, 0.96f, 0.82f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.78f, 0.77f, 0.72f, 1f);
            colors.fadeDuration = 0.12f;
            control.Button.colors = colors;
            return control;
        }

        internal static TMP_Text Stat(string name, UnityEngine.Transform parent,
            float x, float y, float width, string label)
        {
            QdaoUguiFactory.CreateText(name + "Label", parent, x, y, 142f, 70f,
                label, 34f, QdaoRefreshArt.Ink);
            Panel(name + "ValuePlate", parent, x + 150f, y, width - 150f, 70f, "stat_field");
            return QdaoUguiFactory.CreateText(name + "Value", parent, x + 170f, y,
                width - 192f, 70f, "—", 32f, QdaoRefreshArt.Ink);
        }

        internal static UiTextButton SideTab(string name, UnityEngine.Transform parent,
            float y, string label, bool active)
        {
            var control = BattleUiWidgets.CreateTextButton(name, parent, WindowW - 12f, y,
                100f, 176f, label, 34f, active ? QdaoRefreshArt.Jade : AttributeUiStyle.FieldPlate,
                active ? QdaoRefreshArt.Ivory : QdaoRefreshArt.Ink);
            Skin(control.Plate, active ? "tab_vertical_selected" : "tab_vertical_normal");
            control.Label.color = active ? QdaoRefreshArt.Ivory : QdaoRefreshArt.Ink;
            control.Button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            return control;
        }

        internal static void SelectSideTab(UiTextButton control, bool active)
        {
            Skin(control.Plate, active ? "tab_vertical_selected" : "tab_vertical_normal");
            control.Label.color = active ? QdaoRefreshArt.Ivory : QdaoRefreshArt.Ink;
        }

        internal static UiPointRow PointRow(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height)
        {
            // Reuse the authoritative pending/committed control model and its Slider event wiring.
            var row = AttributeUiWidgets.CreatePointRow(name, parent, x, y, width, height);
            Place(row.NameLabel.rectTransform, 0f, 0f, 150f, height);
            row.NameLabel.fontSize = 34f;
            Place(row.ValueLabel.rectTransform, 150f, 0f, 170f, height);
            row.ValueLabel.fontSize = 32f;
            row.ValueLabel.color = ValueBlue;

            const float step = 66f;
            const float sliderX = 430f;
            float sliderWidth = width - sliderX - step - 36f;
            Place((RectTransform)row.Minus.transform, 340f, (height - step) * 0.5f, step, step);
            Place((RectTransform)row.Plus.transform, width - step, (height - step) * 0.5f, step, step);
            Skin(row.Minus.GetComponent<Image>(), "step_minus");
            Skin(row.Plus.GetComponent<Image>(), "step_plus");
            foreach (var button in new[] { row.Minus, row.Plus })
            {
                button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                // The exact sprites contain only the fixed +/- symbols.
                button.GetComponentInChildren<TMP_Text>().gameObject.SetActive(false);
            }

            var slider = row.Slider;
            slider.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            Place((RectTransform)slider.transform, sliderX, (height - 60f) * 0.5f, sliderWidth, 60f);
            var track = slider.transform.Find(name + "SliderBg").GetComponent<Image>();
            Place(track.rectTransform, 0f, 18f, sliderWidth, 24f);
            Skin(track, "slider_track");
            Place((RectTransform)slider.fillRect.parent, 0f, 18f, sliderWidth, 24f);
            var fill = slider.fillRect.GetComponent<Image>();
            Skin(fill, "slider_fill");
            fill.pixelsPerUnitMultiplier = 23f / 24f;
            Place((RectTransform)slider.handleRect.parent, 30f, 0f, sliderWidth - 60f, 60f);
            slider.handleRect.sizeDelta = new Vector2(60f, 60f);
            Skin(slider.handleRect.GetComponent<Image>(), "slider_thumb");
            slider.transition = Selectable.Transition.ColorTint;
            return row;
        }

        internal static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
