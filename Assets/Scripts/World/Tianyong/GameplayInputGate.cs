using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.World.Tianyong
{
    /// <summary>Central gate for world input while UI owns keyboard or pointer focus.</summary>
    public static class GameplayInputGate
    {
        private static int _explicitBlockers;

        public static bool IsKeyboardBlocked
        {
            get
            {
                if (_explicitBlockers > 0) return true;
                var selected = EventSystem.current?.currentSelectedGameObject;
                if (selected == null) return false;
                return selected.GetComponentInParent<TMP_InputField>() != null ||
                       selected.GetComponentInParent<InputField>() != null;
            }
        }

        public static bool IsPointerBlocked
            => _explicitBlockers > 0 ||
               (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());

        internal static void AddBlocker() => _explicitBlockers++;
        internal static void RemoveBlocker() => _explicitBlockers = Mathf.Max(0, _explicitBlockers - 1);

        internal static void ResetForTests() => _explicitBlockers = 0;
    }

    /// <summary>Add to a modal/full-screen UI root that should suspend world controls.</summary>
    public sealed class GameplayInputBlocker : MonoBehaviour
    {
        private void OnEnable() => GameplayInputGate.AddBlocker();
        private void OnDisable() => GameplayInputGate.RemoveBlocker();
    }
}
