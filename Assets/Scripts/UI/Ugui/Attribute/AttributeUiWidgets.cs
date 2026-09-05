using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MmorpgClient.UI.Ugui.Battle;

namespace MmorpgClient.UI.Ugui.Attribute
{
    /// <summary>
    /// 只转发 PointerEnter / PointerExit 的悬停中继。不用 EventTrigger:它实现了全部指针接口
    /// (含 IDragHandler),挂在行根节点上会被 PointerInputModule 解析成子按钮的 pointerDrag,
    /// 按住 ± 时手指/鼠标一动超过拖拽阈值就取消点击(触屏尤其明显)。
    /// </summary>
    public sealed class UiPointerHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action OnEnter;
        public Action OnExit;

        public void OnPointerEnter(PointerEventData eventData) => OnEnter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => OnExit?.Invoke();
    }

    /// <summary>
    /// 一行加点条:[名称][数值][−][滑条][+]。
    ///
    /// 交互约束(与服务端"只增不减"契约对齐,见 player-attribute-allocation.md §4):
    ///   滑条与 ± 只能在 [已确认值, 已确认值 + 剩余点] 之间移动 —— 想把点数拿回来
    ///   必须走「重置」。滑条的 minValue 因此是服务器已确认的 allocated,不是 0。
    /// </summary>
    public sealed class UiPointRow
    {
        public RectTransform Rect;
        public TMP_Text NameLabel;
        public TMP_Text ValueLabel;
        public Slider Slider;
        public Button Minus;
        public Button Plus;
        public UiPointerHoverRelay Hover;
        public uint DimensionId;

        /// <summary>服务器已确认的分配值(滑条下界)。</summary>
        public uint Committed;

        /// <summary>本地待提交值(>= Committed)。</summary>
        public uint Pending;

        /// <summary>面板显示值里不随本次加点变化的部分(自然成长 + 外部加成)。</summary>
        public ulong BaseValue;

        private Action<UiPointRow> _onChanged;
        private bool _suppress;

        public void Bind(Action<UiPointRow> onChanged) => _onChanged = onChanged;

        /// <summary>用服务器面板数据重置整行(committed = 权威值,pending 回落到 committed)。</summary>
        public void SetFromServer(uint dimensionId, string name, ulong displayValue, uint allocated,
            uint cap, uint remaining, bool interactable)
        {
            DimensionId = dimensionId;
            Committed = allocated;
            Pending = allocated;
            BaseValue = displayValue >= allocated ? displayValue - allocated : 0;
            if (NameLabel != null) NameLabel.text = name ?? string.Empty;
            ApplyRange(cap, remaining, interactable);
            Refresh();
        }

        /// <summary>只改本地待提交值(自动加点预览 / ± / 滑条),不触碰 committed。</summary>
        public void SetPending(uint value)
        {
            uint clamped = value;
            if (Slider != null)
            {
                clamped = (uint)Mathf.Clamp(value, Slider.minValue, Slider.maxValue);
            }
            if (clamped < Committed) clamped = Committed;
            Pending = clamped;
            Refresh();
        }

        /// <summary>剩余点变化时重算滑条上界(其它行加点会挤占本行的可加空间)。</summary>
        public void ApplyRange(uint cap, uint remaining, bool interactable)
        {
            if (Slider == null) return;
            long max = (long)Pending + remaining;
            if (cap > 0 && max > cap) max = cap;
            if (max < Committed) max = Committed;
            _suppress = true;
            Slider.minValue = Committed;
            Slider.maxValue = max;
            Slider.value = Pending;
            Slider.interactable = interactable && max > Committed;
            _suppress = false;
            if (Minus != null) Minus.interactable = interactable && Pending > Committed;
            if (Plus != null) Plus.interactable = interactable && Pending < max;
        }

        public void Refresh()
        {
            if (ValueLabel != null)
            {
                // 面板显示值 = 不变部分 + 待提交分配;有未提交增量时用「当前→目标」提示
                ulong shown = BaseValue + Pending;
                ValueLabel.text = Pending > Committed
                    ? $"{BaseValue + Committed}<color=#7FD98A>+{Pending - Committed}</color>"
                    : shown.ToString();
            }
            if (Slider != null && !_suppress && !Mathf.Approximately(Slider.value, Pending))
            {
                _suppress = true;
                Slider.value = Pending;
                _suppress = false;
            }
        }

        internal void HandleSliderChanged(float value)
        {
            if (_suppress) return;
            var next = (uint)Mathf.RoundToInt(value);
            if (next < Committed) next = Committed;
            if (next == Pending) return;
            Pending = next;
            Refresh();
            _onChanged?.Invoke(this);
        }

        internal void Step(int delta)
        {
            long next = (long)Pending + delta;
            if (next < Committed) next = Committed;
            if (Slider != null && next > Slider.maxValue) next = (long)Slider.maxValue;
            if (next == Pending) return;
            Pending = (uint)next;
            Refresh();
            _onChanged?.Invoke(this);
        }
    }

    /// <summary>属性界面专用控件工厂(基础控件复用 QdaoUguiFactory / BattleUiWidgets)。</summary>
    public static class AttributeUiWidgets
    {
        /// <summary>只读数值栏:左标签 + 右数值(左栏气血/法力/物伤/法伤/速度/防御)。</summary>
        public static TMP_Text CreateStatField(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height, string label)
        {
            BattleUiWidgets.CreatePanel($"{name}Plate", parent, x, y, width, height,
                AttributeUiStyle.FieldPlate, false);
            QdaoUguiFactory.CreateText($"{name}Label", parent, x + 14f, y, 120f, height,
                label, 24f, AttributeUiStyle.FieldLabel);
            return QdaoUguiFactory.CreateText($"{name}Value", parent, x + 140f, y, width - 154f, height,
                "-", 24f, AttributeUiStyle.FieldValue, TextAlignmentOptions.MidlineLeft);
        }

        /// <summary>
        /// 交互滑条。仓库此前无任何 Slider 用例,这里是第一处:手工装配
        /// fillRect + handleRect(Slider 需要显式指定,否则滑块不跟手)。
        /// </summary>
        public static Slider CreateSlider(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height)
        {
            var root = QdaoUguiFactory.CreateRect(name, parent, x, y, width, height);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = true;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.transition = Selectable.Transition.None;

            // 轨道(细条,垂直居中)
            float trackH = 10f;
            float trackY = (height - trackH) * 0.5f;
            var background = BattleUiWidgets.CreatePanel($"{name}Bg", root, 0f, trackY, width, trackH,
                AttributeUiStyle.SliderTrack);
            background.raycastTarget = true;

            // uGUI Slider.UpdateVisuals 只驱动 fill/handle 的 anchors(DrivenTransformProperties.Anchors),
            // sizeDelta / pivot / anchoredPosition 原样保留:CreateRect 给的左上 pivot + 满尺寸 sizeDelta 会让
            // fill 宽 = 锚点跨度 + 整条轨道宽(下界就铺满、上界溢出一条轨道)、handle 高出一倍。
            // 这里按 Unity 默认 Slider 结构装配:pivot 居中、anchoredPosition 归零、fill sizeDelta=0、handle 固定方块。
            var fillArea = QdaoUguiFactory.CreateRect($"{name}FillArea", root, 0f, trackY, width, trackH);
            var fillRect = QdaoUguiFactory.CreateRect($"{name}Fill", fillArea, 0f, 0f, width, trackH);
            fillRect.pivot = new Vector2(0.5f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;
            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = AttributeUiStyle.SliderFill;
            fill.raycastTarget = false;

            // 滑动区两端各让出半个滑块,滑块中心落在数值位置时不出轨道
            var handleArea = QdaoUguiFactory.CreateRect($"{name}HandleArea", root, height * 0.5f, 0f, width - height, height);
            var handleRect = QdaoUguiFactory.CreateRect($"{name}Handle", handleArea, 0f, 0f, height, height);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(height, height);
            var handle = handleRect.gameObject.AddComponent<Image>();
            handle.color = AttributeUiStyle.SliderHandle;
            handle.raycastTarget = true;

            slider.targetGraphic = handle;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            return slider;
        }

        /// <summary>组装一整行加点条。</summary>
        public static UiPointRow CreatePointRow(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height)
        {
            var row = new UiPointRow();
            row.Rect = QdaoUguiFactory.CreateRect(name, parent, x, y, width, height);
            // 整行一层近透明的射线目标:名称/数值区域也能触发悬停说明(GraphicRaycaster 只对
            // raycastTarget=true 的 Graphic 报命中;子控件在其上,点击不受影响)
            var hit = row.Rect.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.raycastTarget = true;
            row.Hover = row.Rect.gameObject.AddComponent<UiPointerHoverRelay>();

            row.NameLabel = QdaoUguiFactory.CreateText($"{name}Name", row.Rect, 0f, 0f, 100f, height,
                string.Empty, 26f, AttributeUiStyle.RowName);
            row.ValueLabel = QdaoUguiFactory.CreateText($"{name}Value", row.Rect, 104f, 0f, 92f, height,
                "0", 26f, AttributeUiStyle.RowValue, TextAlignmentOptions.MidlineRight);
            row.ValueLabel.richText = true;

            const float buttonSize = 44f;
            float sliderX = 210f + buttonSize + 10f;
            float sliderW = width - sliderX - buttonSize - 16f;

            var minus = BattleUiWidgets.CreateTextButton($"{name}Minus", row.Rect, 210f,
                (height - buttonSize) * 0.5f, buttonSize, buttonSize, "−", 28f,
                AttributeUiStyle.StepPlate, AttributeUiStyle.StepText);
            row.Minus = minus.Button;
            row.Minus.onClick.AddListener(() => row.Step(-1));

            row.Slider = CreateSlider($"{name}Slider", row.Rect, sliderX, (height - 40f) * 0.5f, sliderW, 40f);
            row.Slider.onValueChanged.AddListener(row.HandleSliderChanged);

            var plus = BattleUiWidgets.CreateTextButton($"{name}Plus", row.Rect, width - buttonSize,
                (height - buttonSize) * 0.5f, buttonSize, buttonSize, "+", 28f,
                AttributeUiStyle.StepPlate, AttributeUiStyle.StepText);
            row.Plus = plus.Button;
            row.Plus.onClick.AddListener(() => row.Step(1));

            return row;
        }
    }
}
