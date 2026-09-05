using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;
using MmorpgClient.Game.Battle.Presentation;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 特效播放器(turn-battle-presentation.md §3 BattleFx):
    ///  - 序列帧 Image,8 帧按帧条自带 fps(ART_MANIFEST fps_hint,缺省 12fps)播放一遍,
    ///    时长与 delay 按 <see cref="BattleTempo"/> 倍率缩放(与拍时长同步);
    ///  - 按 target / source / 中点定位:调用方给设计坐标锚点(y 向下),按帧条 pivot 贴放;
    ///  - 对象池:Image 复用,播完回池,可同时播多个;
    ///  - 命中帧回调(默认第 4 帧,0 基 3):群攻多目标"同一拍飙血"由调用方在回调里做;
    ///  - Clear():观战抢占/断线时立刻回收全部,所有 tween 作废。
    /// 缺图(LoadFx 返回 null)静默返回 0 时长且立即触发命中回调,保证演出链不断。
    /// </summary>
    public sealed class BattleFxPlayer
    {
        public const float DefaultFps = 12f;
        public const int DefaultHitFrame = 3;

        private readonly RectTransform _layer;
        private readonly Stack<Image> _pool = new Stack<Image>();
        private readonly List<Image> _active = new List<Image>();
        private int _serial;

        public int ActiveCount => _active.Count;
        public int PooledCount => _pool.Count;

        public BattleFxPlayer(RectTransform layer)
        {
            _layer = layer;
        }

        /// <summary>两点中点(群攻大范围特效贴在目标区中心)。</summary>
        public static Vector2 Midpoint(Vector2 a, Vector2 b) => (a + b) * 0.5f;

        /// <summary>
        /// 帧条贴放矩形(设计坐标 y 向下):左上角与边长。anchor 为锚点,pivot 为帧条 pivot(0-1,y 向上)。
        /// 纯计算,EditMode 可测。
        /// </summary>
        public static Rect PlacementRect(Vector2 anchor, float size, Vector2 pivot)
        {
            float left = anchor.x - size * pivot.x;
            float top = anchor.y - size * (1f - pivot.y);
            return new Rect(left, top, size, size);
        }

        /// <summary>
        /// 播放一段特效。返回整段时长(秒,含 delay);onHitFrame 在播到 hitFrame 时触发一次
        /// (缺图时立即触发)。mirror=true 水平镜像(朝向左侧的斩击)。
        /// </summary>
        public float Play(string fxId, Vector2 anchor, float size, float delay = 0f,
            Action onHitFrame = null, int hitFrame = DefaultHitFrame, bool mirror = false, float fpsOverride = 0f)
        {
            var strip = BattleArtCatalog.LoadFx(fxId);
            if (_layer == null || strip == null || strip.Count == 0)
            {
                onHitFrame?.Invoke();
                return 0f;
            }

            float fps = fpsOverride > 0f ? fpsOverride : (strip.Fps > 0f ? strip.Fps : DefaultFps);
            float seconds = BattleTempo.Scale(strip.Count / fps);
            delay = delay > 0f ? BattleTempo.Scale(delay) : 0f;
            var rect = PlacementRect(anchor, size, strip.Pivot);

            var image = Rent();
            image.name = $"Fx_{fxId}_{++_serial}";
            image.sprite = strip.Frames[0];
            image.color = Color.white;
            var rt = image.rectTransform;
            rt.anchoredPosition = new Vector2(rect.x, -rect.y);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            rt.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
            // 镜像时 pivot 在左上,翻转后会往左挂:把位置平移一个宽度
            if (mirror) rt.anchoredPosition += new Vector2(rect.width, 0f);
            rt.SetAsLastSibling();
            image.gameObject.SetActive(delay <= 0f);
            _active.Add(image);

            bool hitFired = false;
            int clampedHit = Mathf.Clamp(hitFrame, 0, strip.Count - 1);
            int lastFrame = -1;
            var go = image.gameObject;
            var tween = GTween.To(0f, strip.Count, seconds).SetEase(EaseType.Linear).SetIgnoreEngineTimeScale(true)
                .SetTarget(go)
                .OnStart((GTweenCallback)(() => { if (go != null) go.SetActive(true); }))
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (go == null) { GTween.Kill(go); return; }
                    int frame = Mathf.Clamp(Mathf.FloorToInt(t.value.x), 0, strip.Count - 1);
                    if (frame != lastFrame)
                    {
                        lastFrame = frame;
                        image.sprite = strip.Frames[frame];
                    }
                    if (!hitFired && frame >= clampedHit)
                    {
                        hitFired = true;
                        SafeInvoke(onHitFrame);
                    }
                }))
                .OnComplete((GTweenCallback)(() =>
                {
                    if (!hitFired) { hitFired = true; SafeInvoke(onHitFrame); }
                    Recycle(image);
                }));
            if (delay > 0f) tween.SetDelay(delay);
            return seconds + Mathf.Max(0f, delay);
        }

        /// <summary>立刻回收全部在播特效(观战抢占/关屏)。</summary>
        public void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var image = _active[i];
                if (image != null) GTween.Kill(image.gameObject);
                ReturnToPool(image);
            }
            _active.Clear();
        }

        /// <summary>销毁全部对象(战斗屏销毁时)。</summary>
        public void Dispose()
        {
            Clear();
            while (_pool.Count > 0)
            {
                var image = _pool.Pop();
                if (image != null) UnityEngine.Object.Destroy(image.gameObject);
            }
        }

        private Image Rent()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled != null) return pooled;
            }
            var image = QdaoUguiFactory.CreateImage("Fx", _layer, 0f, 0f, 256f, 256f, null);
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private void Recycle(Image image)
        {
            _active.Remove(image);
            ReturnToPool(image);
        }

        private void ReturnToPool(Image image)
        {
            if (image == null) return;
            image.sprite = null;
            image.rectTransform.localScale = Vector3.one;
            image.gameObject.SetActive(false);
            _pool.Push(image);
        }

        private static void SafeInvoke(Action action)
        {
            if (action == null) return;
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>
    /// 残影池:冲刺/逃跑时的半透明分身,挂在舞台层、插在本单位之下(同一 sibling 位),
    /// Image 复用(不再每次冲刺 new 两个 GameObject 再 Destroy,避免 Graphic 反复注册/注销触发画布重建)。
    /// pivot 取底中,anchoredPosition 直接给脚底点(UGUI 坐标),镜像用 x 缩放 −1 绕中心翻转。
    /// </summary>
    public sealed class BattleAfterimagePool
    {
        public const float DefaultSeconds = 0.25f;
        public const float DefaultAlpha = 0.45f;
        private static readonly Color GhostTint = new Color(0.8f, 0.9f, 1f, 1f);

        private readonly RectTransform _layer;
        private readonly Stack<Image> _pool = new Stack<Image>();
        private readonly List<Image> _active = new List<Image>();

        public int ActiveCount => _active.Count;
        public int PooledCount => _pool.Count;

        public BattleAfterimagePool(RectTransform layer)
        {
            _layer = layer;
        }

        /// <summary>在 footAnchored(UGUI anchoredPosition,脚底点)放一张 size 大小的残影并淡出。</summary>
        public void Spawn(Sprite sprite, Vector2 footAnchored, Vector2 size, bool mirrored, int siblingIndex,
            float seconds = DefaultSeconds, float alpha = DefaultAlpha)
        {
            if (_layer == null || sprite == null) return;
            var image = Rent();
            image.sprite = sprite;
            image.color = new Color(GhostTint.r, GhostTint.g, GhostTint.b, alpha);
            var rt = image.rectTransform;
            rt.anchoredPosition = footAnchored;
            rt.sizeDelta = size;
            rt.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
            rt.SetSiblingIndex(Mathf.Max(0, siblingIndex));
            image.gameObject.SetActive(true);
            _active.Add(image);

            var go = image.gameObject;
            GTween.To(alpha, 0f, BattleTempo.Scale(seconds)).SetEase(EaseType.QuadOut).SetIgnoreEngineTimeScale(true)
                .SetTarget(go)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (go == null) { GTween.Kill(go); return; }
                    image.color = new Color(GhostTint.r, GhostTint.g, GhostTint.b, t.value.x);
                }))
                .OnComplete((GTweenCallback)(() => Recycle(image)));
        }

        /// <summary>立刻回收全部在播残影(观战抢占/关屏)。</summary>
        public void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var image = _active[i];
                if (image != null) GTween.Kill(image.gameObject);
                ReturnToPool(image);
            }
            _active.Clear();
        }

        public void Dispose()
        {
            Clear();
            while (_pool.Count > 0)
            {
                var image = _pool.Pop();
                if (image != null) UnityEngine.Object.Destroy(image.gameObject);
            }
        }

        private Image Rent()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled != null) return pooled;
            }
            var image = QdaoUguiFactory.CreateImage("Afterimage", _layer, 0f, 0f, 100f, 100f, null);
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.rectTransform.pivot = new Vector2(0.5f, 0f);
            return image;
        }

        private void Recycle(Image image)
        {
            _active.Remove(image);
            ReturnToPool(image);
        }

        private void ReturnToPool(Image image)
        {
            if (image == null) return;
            image.sprite = null;
            image.rectTransform.localScale = Vector3.one;
            image.gameObject.SetActive(false);
            _pool.Push(image);
        }
    }
}
