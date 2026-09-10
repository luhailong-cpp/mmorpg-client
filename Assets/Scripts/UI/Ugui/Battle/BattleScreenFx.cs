using System.Collections;
using MmorpgClient.UI.Ugui.Tweening;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 暴击顿帧(turn-battle-presentation.md §3 Camera/Screen FX:realtime 0.08s):
    /// 战斗表现全部走 realtime tween 且忽略 Time.timeScale,所以顿帧不能用 timeScale;
    /// 这里通过原生调度器的暂停快照冻结当前活跃 tween,再按 realtime 恢复。
    /// 顿帧期间新建的 tween 不受影响(飘字弹出等照常)。
    /// </summary>
    public static class BattleHitStop
    {
        public const float CritFreezeSeconds = 0.08f;

        /// <summary>原生 tween 调度器始终支持顿帧。</summary>
        public static bool IsAvailable => true;

        /// <summary>冻结当前所有 tween seconds 秒(realtime)。返回是否真的冻结了。</summary>
        public static bool Freeze(MonoBehaviour runner, float seconds)
        {
            if (runner == null || seconds <= 0f) return false;
            var snapshot = RealtimeTween.PauseSnapshot();
            runner.StartCoroutine(CoResume(snapshot, seconds));
            return true;
        }

        private static IEnumerator CoResume(RealtimeTweenPauseSnapshot snapshot, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            snapshot.Resume();
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
            RealtimeTween.Kill(_stage);
            var stage = _stage;
            var basePos = _basePos;
            RealtimeTween.Shake(new Vector3(basePos.x, basePos.y, 0f), amplitude, seconds)
                .SetIgnoreEngineTimeScale(true).SetTarget(stage)
                .OnUpdate((RealtimeTweenCallback1)(t =>
                {
                    if (stage == null) return;
                    stage.anchoredPosition = new Vector2(t.Value.x, t.Value.y);
                }))
                .OnComplete((RealtimeTweenCallback)(() => { if (stage != null) stage.anchoredPosition = basePos; }));
        }

        public void Reset()
        {
            if (_stage == null) return;
            RealtimeTween.Kill(_stage);
            _stage.anchoredPosition = _basePos;
        }
    }
}
