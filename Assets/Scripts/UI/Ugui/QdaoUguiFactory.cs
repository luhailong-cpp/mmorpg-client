using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>Small, deterministic helpers for constructing pixel-measured uGUI trees.</summary>
    public static class QdaoUguiFactory
    {
        public static RectTransform CreateRect(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float width,
            float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = UnityEngine.Vector3.one;
            rect.localRotation = Quaternion.identity;
            return rect;
        }

        public static RectTransform CreateStretch(string name, UnityEngine.Transform parent, Vector4 inset)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset.x, inset.y);
            rect.offsetMax = new Vector2(-inset.z, -inset.w);
            rect.localScale = UnityEngine.Vector3.one;
            rect.localRotation = Quaternion.identity;
            return rect;
        }

        public static RectTransform CreateCenteredRect(
            string name,
            UnityEngine.Transform parent,
            float width,
            float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = UnityEngine.Vector3.one;
            rect.localRotation = Quaternion.identity;
            return rect;
        }

        public static Image CreateAspectFillImage(
            string name,
            UnityEngine.Transform parent,
            Sprite sprite,
            float aspectRatio)
        {
            var rect = CreateCenteredRect(name, parent, 1f, 1f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.type = Image.Type.Simple;

            var fitter = rect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = aspectRatio;
            return image;
        }

        public static Image CreateImage(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float width,
            float height,
            Sprite sprite,
            bool raycastTarget = false)
        {
            var rect = CreateRect(name, parent, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = raycastTarget;
            image.preserveAspect = false;
            image.type = Image.Type.Simple;
            return image;
        }

        public static TextMeshProUGUI CreateText(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float width,
            float height,
            string value,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var rect = CreateRect(name, parent, x, y, width, height);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = QdaoUguiTheme.ResolveFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.text = value ?? string.Empty;
            return text;
        }

        public static Button CreateHitButton(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float width,
            float height)
        {
            var rect = CreateRect(name, parent, x, y, width, height);
            var hitImage = rect.gameObject.AddComponent<Image>();
            hitImage.color = new Color(1f, 1f, 1f, 0.001f);
            hitImage.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hitImage;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        /// <summary>
        /// Creates a real, visible uGUI control: the Image is both the artwork
        /// and the Button targetGraphic. This is preferred over a transparent
        /// hit layer for every production control that owns a visual state.
        /// </summary>
        public static Button CreateArtButton(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float width,
            float height,
            Sprite sprite,
            out Image image)
        {
            var rect = CreateRect(name, parent, x, y, width, height);
            image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = true;
            image.preserveAspect = false;
            image.type = Image.Type.Simple;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.96f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.62f, 0.62f, 0.62f, 0.72f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            return button;
        }

        public static TMP_InputField CreateInputField(
            string name,
            UnityEngine.Transform parent,
            float x,
            float y,
            float width,
            float height,
            string placeholderText,
            int characterLimit = 20,
            Sprite backgroundSprite = null)
        {
            var rect = CreateRect(name, parent, x, y, width, height);
            var hitImage = rect.gameObject.AddComponent<Image>();
            hitImage.sprite = backgroundSprite;
            hitImage.color = backgroundSprite != null
                ? Color.white
                : new Color(1f, 1f, 1f, 0.001f);
            hitImage.raycastTarget = true;
            hitImage.preserveAspect = false;
            hitImage.type = Image.Type.Simple;

            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = hitImage;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = characterLimit;

            var viewport = CreateStretch("Viewport", rect, new Vector4(12f, 8f, 8f, 8f));
            viewport.gameObject.AddComponent<RectMask2D>();

            var textRect = CreateStretch("Text", viewport, Vector4.zero);
            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = QdaoUguiTheme.ResolveFont();
            text.fontSize = 20f;
            text.color = QdaoUguiTheme.Brown;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            var placeholderRect = CreateStretch("Placeholder", viewport, Vector4.zero);
            var placeholder = placeholderRect.gameObject.AddComponent<TextMeshProUGUI>();
            placeholder.font = QdaoUguiTheme.ResolveFont();
            placeholder.fontSize = 20f;
            placeholder.color = new Color(
                QdaoUguiTheme.MutedBrown.r,
                QdaoUguiTheme.MutedBrown.g,
                QdaoUguiTheme.MutedBrown.b,
                0.78f);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            placeholder.raycastTarget = false;
            placeholder.text = placeholderText;

            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
        }
    }
}
