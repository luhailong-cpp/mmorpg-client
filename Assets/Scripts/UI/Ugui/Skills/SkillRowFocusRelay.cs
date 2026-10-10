using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MmorpgClient.UI.Ugui.Skills
{
    /// <summary>只处理键盘选中，保留父 ScrollRect 对鼠标及触屏拖动的处理。</summary>
    public sealed class SkillRowFocusRelay : MonoBehaviour, ISelectHandler
    {
        public Action Selected;
        public void OnSelect(BaseEventData eventData) => Selected?.Invoke();
    }
}
