using System;
using System.Globalization;
using MmorpgClient.Game.Jubaozhai;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 防手滑「长按 2 秒确认」交互组件（支持鼠标/触控，兼容 WebGL2/IL2CPP，不依赖悬停）。
    /// 计时状态机委托给纯 C# <see cref="HoldToConfirmTimer"/>，禁用或移出按钮立即取消进度。
    /// </summary>
    public sealed class HoldToConfirmButtonBehaviour : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private HoldToConfirmTimer _timer;
        private RectTransform _fillClip;
        private TMP_Text _label;
        private Image _trackImage;
        private float _fillMaxWidth;
        private string _idleLabel = "长按 2 秒确认绑定";
        private string _disabledLabel = "请先勾选上方风险确认";
        private bool _interactable;
        private Action _onCompleted;

        public void Configure(
            RectTransform fillClip,
            float fillMaxWidth,
            TMP_Text label,
            Image trackImage,
            string idleLabel,
            string disabledLabel,
            bool interactable,
            Action onCompleted,
            float requiredSeconds = HoldToConfirmTimer.DefaultRequiredSeconds)
        {
            _timer = new HoldToConfirmTimer(requiredSeconds);
            _fillClip = fillClip;
            _fillMaxWidth = Mathf.Max(4f, fillMaxWidth);
            _label = label;
            _trackImage = trackImage;
            _idleLabel = string.IsNullOrEmpty(idleLabel) ? "长按 2 秒确认绑定" : idleLabel;
            _disabledLabel = string.IsNullOrEmpty(disabledLabel) ? "请先勾选上方风险确认" : disabledLabel;
            _interactable = interactable;
            _onCompleted = onCompleted;
            RefreshVisuals();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_interactable || _timer == null || _timer.Completed) return;
            _timer.BeginHold();
            RefreshVisuals();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            CancelHold();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CancelHold();
        }

        private void OnDisable()
        {
            CancelHold();
        }

        private void Update()
        {
            if (_timer == null || !_timer.IsHolding) return;
            bool completedNow = _timer.Advance(Time.unscaledDeltaTime);
            RefreshVisuals();
            if (completedNow)
            {
                var callback = _onCompleted;
                _onCompleted = null;
                callback?.Invoke();
            }
        }

        private void CancelHold()
        {
            if (_timer == null || _timer.Completed) return;
            _timer.CancelHold();
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            float progress = _timer?.Progress ?? 0f;
            if (_fillClip != null)
            {
                _fillClip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _fillMaxWidth * progress);
            }
            if (_trackImage != null)
            {
                _trackImage.color = _interactable ? Color.white : new Color(0.72f, 0.70f, 0.66f, 0.78f);
            }
            if (_label != null)
            {
                if (!_interactable)
                {
                    _label.text = _disabledLabel;
                }
                else if (_timer != null && _timer.Completed)
                {
                    _label.text = "已确认，正在重新预览…";
                }
                else if (_timer != null && _timer.IsHolding)
                {
                    string elapsed = _timer.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
                    string total = _timer.RequiredSeconds.ToString("0.0", CultureInfo.InvariantCulture);
                    _label.text = $"按住不松手… {elapsed}s / {total}s";
                }
                else
                {
                    _label.text = _idleLabel;
                }
            }
        }
    }
}
