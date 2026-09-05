using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>文字按钮组合控件:底板 Image + 居中文本 + Button。</summary>
    public sealed class UiTextButton
    {
        public RectTransform Rect;
        public Image Plate;
        public TMP_Text Label;
        public Button Button;

        public void SetInteractable(bool value)
        {
            if (Button != null) Button.interactable = value;
        }

        public void SetText(string value)
        {
            if (Label != null) Label.text = value ?? string.Empty;
        }

        public void SetVisible(bool value)
        {
            if (Rect != null && Rect.gameObject.activeSelf != value)
                Rect.gameObject.SetActive(value);
        }
    }

    /// <summary>数值进度条(HP/MP):背景 + 按比例伸缩的填充 + 覆盖文本。</summary>
    public sealed class UiBar
    {
        public Image Background;
        public RectTransform FillRect;
        public Image Fill;
        public TMP_Text Label;

        private readonly float _fullWidth;

        public UiBar(float fullWidth)
        {
            _fullWidth = fullWidth;
        }

        public void Set(string prefix, ulong current, ulong max)
        {
            float ratio = max == 0 ? 0f : Mathf.Clamp01((float)((double)current / max));
            if (FillRect != null)
                FillRect.sizeDelta = new Vector2(Mathf.Max(0f, _fullWidth * ratio), FillRect.sizeDelta.y);
            if (Label != null)
                Label.text = $"{prefix} {current}/{max}";
        }
    }

    /// <summary>
    /// 战斗 UI 专用小部件工厂。基础控件(矩形/文本/输入框)复用
    /// <see cref="QdaoUguiFactory"/>,此处只补齐纯色面板/文字按钮/进度条/飘字。
    /// 与既有 Qdao 视图一致:纯代码构建,不依赖 prefab。
    /// </summary>
    public static class BattleUiWidgets
    {
        public static long NowUnixMs()
            => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>
        /// 给 TMP 文字加描边。TMP_Text.outlineWidth 只写材质的 _OutlineWidth,而工程字体(SimKai SDF)用的是
        /// Mobile/Distance Field shader,描边靠 OUTLINE_ON 关键字开关 —— 不打开关键字描边根本不画
        /// (2026-09-04 帧验收:脚下名字"无描边"就是这个原因)。这里在实例材质上把关键字一并打开。
        /// </summary>
        public static void ApplyOutline(TMP_Text text, float width, Color32 color)
        {
            if (text == null) return;
            try
            {
                text.outlineWidth = width;
                text.outlineColor = color;
                var mat = text.fontMaterial; // 取实例材质(首次访问即拷贝)
                if (mat != null)
                {
                    mat.EnableKeyword("OUTLINE_ON");
                    mat.SetFloat("_OutlineWidth", width);
                    mat.SetColor("_OutlineColor", color);
                }
            }
            catch (System.Exception)
            {
                // 字体无 SDF 材质实例时描边不可用,忽略
            }
        }

        /// <summary>纯色面板(无 sprite 的 Image)。</summary>
        public static Image CreatePanel(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height, Color color, bool raycastTarget = true)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, width, height, null, raycastTarget);
            image.color = color;
            return image;
        }

        /// <summary>铺满父节点的整幅遮罩/底色(适配任意窗口比例)。</summary>
        public static Image CreateStretchPanel(string name, UnityEngine.Transform parent,
            Color color, bool raycastTarget = true)
        {
            var rect = QdaoUguiFactory.CreateStretch(name, parent, Vector4.zero);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        public static UiTextButton CreateTextButton(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height, string text, float fontSize,
            Color plateColor, Color textColor)
        {
            var widget = new UiTextButton();
            widget.Plate = CreatePanel($"{name}Plate", parent, x, y, width, height, plateColor);
            widget.Rect = (RectTransform)widget.Plate.transform;
            widget.Button = widget.Plate.gameObject.AddComponent<Button>();
            ConfigureButtonVisual(widget.Button, widget.Plate);
            widget.Label = QdaoUguiFactory.CreateText($"{name}Text", widget.Rect, 0f, 0f, width, height,
                text, fontSize, textColor, TextAlignmentOptions.Center);
            return widget;
        }

        /// <summary>与 Qdao 视图一致的按钮着色反馈配置。</summary>
        public static void ConfigureButtonVisual(Button button, Graphic visual)
        {
            if (button == null || visual == null) return;
            button.targetGraphic = visual;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.62f, 0.62f, 0.62f, 0.72f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        public static UiBar CreateBar(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height, Color fillColor)
        {
            var bar = new UiBar(width - 4f);
            bar.Background = CreatePanel($"{name}Bg", parent, x, y, width, height, BattleUiStyle.BarBg, false);
            bar.FillRect = QdaoUguiFactory.CreateRect($"{name}Fill", bar.Background.transform,
                2f, 2f, width - 4f, height - 4f);
            bar.Fill = bar.FillRect.gameObject.AddComponent<Image>();
            bar.Fill.color = fillColor;
            bar.Fill.raycastTarget = false;
            bar.Label = QdaoUguiFactory.CreateText($"{name}Text", bar.Background.transform,
                8f, 0f, width - 16f, height, string.Empty,
                Mathf.Min(18f, height - 6f), QdaoUguiTheme.Cream, TextAlignmentOptions.MidlineLeft);
            return bar;
        }

        /// <summary>可换行文本(工厂默认 NoWrap,战斗描述/Buff 列表需要换行)。</summary>
        public static TextMeshProUGUI CreateWrappedText(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height, string value, float fontSize, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var text = QdaoUguiFactory.CreateText(name, parent, x, y, width, height, value, fontSize, color, alignment);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

        /// <summary>
        /// 在 parent 的设计坐标 (x, y) 处生成一条向上漂浮并淡出的文字。
        /// runner 提供协程宿主(BattleUiRoot)。
        /// </summary>
        public static void SpawnFloatText(MonoBehaviour runner, UnityEngine.Transform parent,
            float x, float y, string value, Color color, bool big)
        {
            if (runner == null || parent == null) return;
            float fontSize = big ? 46f : 30f;
            float duration = big ? BattleUiStyle.CritFloatTextSeconds : BattleUiStyle.FloatTextSeconds;
            var text = QdaoUguiFactory.CreateText("FloatText", parent, x, y, 340f, 64f,
                value, fontSize, color, TextAlignmentOptions.Center);
            runner.StartCoroutine(CoFloat(text, duration));
        }

        private static IEnumerator CoFloat(TextMeshProUGUI text, float duration)
        {
            if (text == null) yield break;
            var rect = (RectTransform)text.transform;
            var from = rect.anchoredPosition;
            float start = Time.realtimeSinceStartup;
            while (true)
            {
                if (text == null) yield break; // 战斗屏被关闭/重建时对象可能已销毁
                float t = (Time.realtimeSinceStartup - start) / duration;
                if (t >= 1f) break;
                rect.anchoredPosition = from + new Vector2(0f, 96f * t);
                var c = text.color;
                c.a = 1f - t * t;
                text.color = c;
                yield return null;
            }
            if (text != null)
                UnityEngine.Object.Destroy(text.gameObject);
        }
    }
}
