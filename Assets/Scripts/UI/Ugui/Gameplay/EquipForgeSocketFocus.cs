using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>Reveals a focused socket without intercepting its parent scroll view's pointer gestures.</summary>
    public sealed class EquipForgeSocketFocus : MonoBehaviour, ISelectHandler
    {
        public Action Selected;

        public void OnSelect(BaseEventData eventData) => Selected?.Invoke();
    }
}
