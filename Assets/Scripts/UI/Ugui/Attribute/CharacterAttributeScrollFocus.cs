using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Attribute
{
    /// <summary>Keep keyboard-selected allocation controls inside the masked viewport.</summary>
    public sealed class CharacterAttributeScrollFocus : MonoBehaviour, ISelectHandler
    {
        private ScrollRect _scroll;
        private RectTransform _row;

        internal static void Bind(Selectable control, ScrollRect scroll, RectTransform row)
        {
            var focus = control.gameObject.AddComponent<CharacterAttributeScrollFocus>();
            focus._scroll = scroll;
            focus._row = row;
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (_scroll == null || _row == null || !_scroll.isActiveAndEnabled) return;
            var viewport = _scroll.viewport;
            var content = _scroll.content;
            if (viewport == null || content == null) return;

            Canvas.ForceUpdateCanvases();
            UnityEngine.Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, _row);
            float delta = bounds.max.y > viewport.rect.yMax
                ? viewport.rect.yMax - bounds.max.y
                : bounds.min.y < viewport.rect.yMin ? viewport.rect.yMin - bounds.min.y : 0f;
            if (Mathf.Approximately(delta, 0f)) return;
            _scroll.StopMovement();
            var position = content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + delta, 0f,
                Mathf.Max(0f, content.rect.height - viewport.rect.height));
            content.anchoredPosition = position;
        }
    }
}
