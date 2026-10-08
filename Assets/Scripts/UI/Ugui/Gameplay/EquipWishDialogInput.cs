using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>Keyboard handling and bounded focus for the reusable native modal.</summary>
    public sealed class EquipWishDialogInput : MonoBehaviour
    {
        public Action CancelRequested;
        public List<Selectable> Controls = new List<Selectable>();
        public void FocusFirst()
        {
            if (Controls.Count > 0 && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(Controls[0].gameObject);
        }
        private void Update()
        {
            bool escape = false, tab = false, reverse = false;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys != null)
            {
                escape = keys.escapeKey.wasPressedThisFrame;
                tab = keys.tabKey.wasPressedThisFrame;
                reverse = keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            escape = Input.GetKeyDown(KeyCode.Escape); tab = Input.GetKeyDown(KeyCode.Tab);
            reverse = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            if (escape) { CancelRequested?.Invoke(); return; }
            var system = EventSystem.current;
            if (system == null || Controls.Count == 0) return;
            var selected = system.currentSelectedGameObject;
            int index = Controls.FindIndex(control => control != null && control.gameObject == selected);
            if (index < 0) { FocusFirst(); return; }
            if (tab)
                system.SetSelectedGameObject(Controls[(index + (reverse ? Controls.Count - 1 : 1)) % Controls.Count].gameObject);
        }
    }
}
