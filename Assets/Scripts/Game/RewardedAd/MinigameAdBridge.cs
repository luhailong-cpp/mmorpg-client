#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using AOT;

namespace MmorpgClient.Game.RewardedAd
{
    /// <summary>
    /// 微信 / 抖音小游戏原生激励视频桥（调用 Assets/Plugins/WebGL/MinigameAd.jslib）：
    /// 严格单线程主线程回调、显式非泛型委托 + [MonoPInvokeCallback] 防 IL2CPP AOT 裁剪。
    /// </summary>
    public sealed class MinigameAdBridge : IRewardedAdBridge
    {
        private delegate void AdStatusCallback(int requestId, string result);
        private delegate void AdReadyCallback(int ready);

        [DllImport("__Internal")] private static extern int MinigameAdHost();
        [DllImport("__Internal")] private static extern void MinigameAdSetReadyCallback(AdReadyCallback callback);
        [DllImport("__Internal")] private static extern void MinigameAdPreload(string adUnitId);
        [DllImport("__Internal")] private static extern int MinigameAdIsReady(string adUnitId);
        [DllImport("__Internal")] private static extern void MinigameAdShow(int requestId, string adUnitId, AdStatusCallback callback);
        [DllImport("__Internal")] private static extern void MinigameAdCancel();

        private static int _activeRequestId;
        private static Action<RewardedAdShowResult> _pendingShow;
        private static MinigameAdBridge _activeInstance;

        private readonly uint _platform;
        private string _adUnitId = string.Empty;
        private bool _isReady;

        public uint ClientPlatform => _platform;
        public bool IsSupported => _platform != (uint)global::AdClientPlatform.Unspecified;
        public bool IsAdReady => IsSupported && !string.IsNullOrEmpty(_adUnitId)
            && (_isReady || MinigameAdIsReady(_adUnitId) != 0);

        public event Action ReadyChanged;

        public MinigameAdBridge()
        {
            int host = MinigameAdHost();
            _platform = host == 2
                ? (uint)global::AdClientPlatform.DouyinMinigame
                : (host == 1 ? (uint)global::AdClientPlatform.WechatMinigame : (uint)global::AdClientPlatform.Unspecified);
            _activeInstance = this;
            if (IsSupported)
            {
                MinigameAdSetReadyCallback(OnJsReadyChanged);
            }
        }

        public void Preload(string adUnitId)
        {
            if (!IsSupported || string.IsNullOrWhiteSpace(adUnitId)) return;
            _adUnitId = adUnitId.Trim();
            MinigameAdPreload(_adUnitId);
        }

        public void Show(Action<RewardedAdShowResult> onFinished)
        {
            if (!IsSupported || string.IsNullOrEmpty(_adUnitId))
            {
                onFinished?.Invoke(new RewardedAdShowResult(RewardedAdShowStatus.Unavailable, "minigame_ad_not_ready"));
                return;
            }
            int requestId = ++_activeRequestId;
            _pendingShow = onFinished;
            MinigameAdShow(requestId, _adUnitId, OnJsShowFinished);
        }

        public void CancelPending()
        {
            ++_activeRequestId;
            _pendingShow = null;
            if (IsSupported) MinigameAdCancel();
        }

        [MonoPInvokeCallback(typeof(AdReadyCallback))]
        private static void OnJsReadyChanged(int ready)
        {
            var inst = _activeInstance;
            if (inst == null) return;
            bool next = ready != 0;
            if (inst._isReady == next) return;
            inst._isReady = next;
            inst.ReadyChanged?.Invoke();
        }

        [MonoPInvokeCallback(typeof(AdStatusCallback))]
        private static void OnJsShowFinished(int requestId, string result)
        {
            if (requestId != _activeRequestId) return;
            var pending = _pendingShow;
            _pendingShow = null;
            if (pending == null) return;

            string token = result ?? string.Empty;
            if (string.Equals(token, "ended", StringComparison.Ordinal))
            {
                pending.Invoke(new RewardedAdShowResult(RewardedAdShowStatus.Completed));
            }
            else if (string.Equals(token, "closed", StringComparison.Ordinal))
            {
                pending.Invoke(new RewardedAdShowResult(RewardedAdShowStatus.DismissedEarly));
            }
            else if (string.Equals(token, "unavailable", StringComparison.Ordinal))
            {
                pending.Invoke(new RewardedAdShowResult(RewardedAdShowStatus.Unavailable, token));
            }
            else
            {
                pending.Invoke(new RewardedAdShowResult(RewardedAdShowStatus.Failed, token));
            }
        }
    }
}
#endif
