using UnityEngine;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 仅调整装备窗口自己的尺寸、中心和统一缩放，不改父容器、Canvas 或 CanvasScaler。
    /// Screen.safeArea 使用屏幕像素，经父容器转换后与其可用范围求交。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class EquipmentUiFit : MonoBehaviour
    {
        public Vector2 referenceSize = new(EquipmentUiArt.Width, EquipmentUiArt.Height);

        private RectTransform _rect;
        private bool _fitting;
        private bool _reportedInvalidReference;

        private void Awake() => Fit();
        private void OnEnable() => Fit();
        private void OnValidate() => Fit();
        private void OnRectTransformDimensionsChange() => Fit();
        private void LateUpdate() => Fit();

        /// <summary>页面改变 referenceSize 后可立即调用；屏幕和安全区变化也会自动更新。</summary>
        public void Fit()
        {
            if (_fitting) return;
            if (_rect == null) _rect = transform as RectTransform;
            if (_rect == null || !(_rect.parent is RectTransform parent)) return;
            _fitting = true;
            try
            {
                if (!PositiveFinite(referenceSize.x) || !PositiveFinite(referenceSize.y))
                {
                    if (!_reportedInvalidReference)
                        Debug.LogError("[EquipmentUi] Reference size must contain two positive finite dimensions.", this);
                    _reportedInvalidReference = true;
                    _rect.localScale = new UnityEngine.Vector3(0, 0, 1);
                    return;
                }
                _reportedInvalidReference = false;
                var available = AvailableRect(parent);
                if (available.width <= 0 || available.height <= 0)
                {
                    _rect.localScale = new UnityEngine.Vector3(0, 0, 1);
                    return;
                }
                float scale = Mathf.Min(available.width / referenceSize.x, available.height / referenceSize.y);
                var center = new Vector2(.5f, .5f);
                if (_rect.anchorMin != center) _rect.anchorMin = center;
                if (_rect.anchorMax != center) _rect.anchorMax = center;
                if (_rect.pivot != center) _rect.pivot = center;
                if (_rect.sizeDelta != referenceSize) _rect.sizeDelta = referenceSize;
                var offset = available.center - parent.rect.center;
                var position = new UnityEngine.Vector3(offset.x, offset.y, 0);
                if (_rect.anchoredPosition3D != position) _rect.anchoredPosition3D = position;
                var uniformScale = new UnityEngine.Vector3(scale, scale, 1);
                if (_rect.localScale != uniformScale) _rect.localScale = uniformScale;
                if (_rect.localRotation != Quaternion.identity) _rect.localRotation = Quaternion.identity;
            }
            finally
            {
                _fitting = false;
            }
        }

        private static Rect AvailableRect(RectTransform parent)
        {
            var available = parent.rect;
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas == null) return available;
            canvas = canvas.rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (canvas.renderMode == RenderMode.WorldSpace && camera == null) return available;
            var pixels = canvas.pixelRect;
            if (pixels.width <= 0 || pixels.height <= 0) return available;
            // 离屏 RenderTexture 和非主显示器不使用主屏的刘海区域。
            if (canvas.targetDisplay == 0 && (camera == null || camera.targetTexture == null) &&
                Screen.safeArea.width > 0 && Screen.safeArea.height > 0)
                pixels = Intersect(pixels, Screen.safeArea);
            if (pixels.width <= 0 || pixels.height <= 0) return new Rect();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixels.min, camera, out var min) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixels.max, camera, out var max))
                return new Rect();
            var safeLocal = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            return Intersect(available, safeLocal);
        }

        private static Rect Intersect(Rect left, Rect right)
        {
            float x = Mathf.Max(left.xMin, right.xMin);
            float y = Mathf.Max(left.yMin, right.yMin);
            return new Rect(x, y, Mathf.Max(0, Mathf.Min(left.xMax, right.xMax) - x),
                Mathf.Max(0, Mathf.Min(left.yMax, right.yMax) - y));
        }

        private static bool PositiveFinite(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
