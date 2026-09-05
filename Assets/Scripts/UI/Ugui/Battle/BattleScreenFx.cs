using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FairyGUI;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 暴击顿帧(turn-battle-presentation.md §3 Camera/Screen FX:realtime 0.08s):
    /// 战斗表现全部走 GTween 且忽略 Time.timeScale,所以顿帧不能用 timeScale;
    /// 这里把 TweenManager 当前活跃的 tween 全部 SetPaused,再由宿主协程按 realtime 恢复。
    /// TweenManager 是 FairyGUI 的 internal 类,活跃列表只能反射取;取不到时降级为不顿帧(不报错)。
    /// 顿帧期间新建的 tween 不受影响(飘字弹出等照常)。
    /// </summary>
    public static class BattleHitStop
    {
        public const float CritFreezeSeconds = 0.08f;

        private static FieldInfo s_activeField;
        private static FieldInfo s_countField;
        private static bool s_resolved;
        private static bool s_available;
        private static int s_depth;

        /// <summary>顿帧是否可用(反射解析 TweenManager 成功)。</summary>
        public static bool IsAvailable
        {
            get
            {
                Resolve();
                return s_available;
            }
        }

        /// <summary>冻结当前所有 tween seconds 秒(realtime);runner 提供协程宿主。返回是否真的冻结了。</summary>
        public static bool Freeze(MonoBehaviour runner, float seconds)
        {
            if (runner == null || seconds <= 0f) return false;
            Resolve();
            if (!s_available) return false;
            var paused = new List<GTweener>();
            try
            {
                var active = s_activeField.GetValue(null) as GTweener[];
                int count = (int)s_countField.GetValue(null);
                if (active == null) return false;
                for (int i = 0; i < count && i < active.Length; i++)
                {
                    var tween = active[i];
                    if (tween == null || tween.completed) continue;
                    tween.SetPaused(true);
                    paused.Add(tween);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BattleHitStop] 顿帧不可用:{e.Message}");
                s_available = false;
                foreach (var t in paused) t.SetPaused(false);
                return false;
            }
            s_depth++;
            runner.StartCoroutine(CoResume(paused, seconds));
            return true;
        }

        private static IEnumerator CoResume(List<GTweener> paused, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            s_depth = Math.Max(0, s_depth - 1);
            foreach (var tween in paused)
            {
                if (tween == null) continue;
                try { tween.SetPaused(false); }
                catch (Exception) { }
            }
        }

        private static void Resolve()
        {
            if (s_resolved) return;
            s_resolved = true;
            try
            {
                var type = typeof(GTween).Assembly.GetType("FairyGUI.TweenManager");
                s_activeField = type?.GetField("_activeTweens", BindingFlags.NonPublic | BindingFlags.Static);
                s_countField = type?.GetField("_totalActiveTweens", BindingFlags.NonPublic | BindingFlags.Static);
                s_available = s_activeField != null && s_countField != null;
            }
            catch (Exception)
            {
                s_available = false;
            }
        }
    }

    /// <summary>
    /// 舞台级屏幕表现:震屏(整个单位层 ±px 随机抖动后回中)、黑边(预留)。
    /// Reset() 在 Abort 时把舞台根节点复位。
    /// </summary>
    public sealed class BattleCameraFx
    {
        public const float CritShakePixels = 6f;
        public const float CritShakeSeconds = 0.25f;

        private readonly RectTransform _stage;
        private readonly Vector2 _basePos;

        public BattleCameraFx(RectTransform stage)
        {
            _stage = stage;
            _basePos = stage != null ? stage.anchoredPosition : Vector2.zero;
        }

        /// <summary>震屏:amplitude 像素、seconds 秒,衰减到 0 后回中。</summary>
        public void Shake(float amplitude = CritShakePixels, float seconds = CritShakeSeconds)
        {
            if (_stage == null) return;
            GTween.Kill(_stage);
            var stage = _stage;
            var basePos = _basePos;
            GTween.Shake(new Vector3(basePos.x, basePos.y, 0f), amplitude, seconds)
                .SetIgnoreEngineTimeScale(true).SetTarget(stage)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (stage == null) return;
                    stage.anchoredPosition = new Vector2(t.value.x, t.value.y);
                }))
                .OnComplete((GTweenCallback)(() => { if (stage != null) stage.anchoredPosition = basePos; }));
        }

        public void Reset()
        {
            if (_stage == null) return;
            GTween.Kill(_stage);
            _stage.anchoredPosition = _basePos;
        }
    }
}
