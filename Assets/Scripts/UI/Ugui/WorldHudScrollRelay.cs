using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>Keep native ScrollRect gestures working across feature-owned HUD canvases.</summary>
    public sealed class WorldHudScrollRelay : MonoBehaviour, IScrollHandler, IInitializePotentialDragHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public ScrollRect Target { get; set; }
        public void OnScroll(PointerEventData data) { if (Target != null && Target.isActiveAndEnabled) Target.OnScroll(data); }
        public void OnInitializePotentialDrag(PointerEventData data)
        { if (Target != null && Target.isActiveAndEnabled) Target.OnInitializePotentialDrag(data); }
        public void OnBeginDrag(PointerEventData data) { if (Target != null && Target.isActiveAndEnabled) Target.OnBeginDrag(data); }
        public void OnDrag(PointerEventData data) { if (Target != null && Target.isActiveAndEnabled) Target.OnDrag(data); }
        public void OnEndDrag(PointerEventData data) { if (Target != null && Target.isActiveAndEnabled) Target.OnEndDrag(data); }
    }
}
