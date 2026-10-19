using System;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>One scale for the entire approved composition, in both Edit Mode capture and the live game.</summary>
    [ExecuteAlways]
    public sealed class GameplayDesignFit : MonoBehaviour
    {
        private RectTransform _rect;
        private void Awake() { _rect = (RectTransform)transform; Fit(); }
        private void OnEnable() => Fit();
        private void OnRectTransformDimensionsChange() => Fit();
        private void LateUpdate() => Fit();
        private void Fit()
        {
            if (_rect == null) _rect = transform as RectTransform;
            if (_rect == null || !(_rect.parent is RectTransform parent)) return;
            float scale = Math.Min(parent.rect.width / 1932f, parent.rect.height / 814f);
            if (scale > 0 && Math.Abs(_rect.localScale.x - scale) > .0001f) _rect.localScale = new UnityEngine.Vector3(scale, scale, 1);
        }
    }
}
