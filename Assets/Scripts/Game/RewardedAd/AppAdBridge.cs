#if MMORPG_ENABLE_APP_AD_SDK && (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
using System;

namespace MmorpgClient.Game.RewardedAd
{
    /// <summary>
    /// App 二期第三方聚合广告驱动契约：仅在显式定义 MMORPG_ENABLE_APP_AD_SDK 宏时编译进入 App 包，
    /// 首审构建默认关闭宏以实现零广告 SDK 与零 ATT 隐私框架残留。
    /// </summary>
    public interface IAppAdSdkDriver
    {
        bool IsReady { get; }
        event Action ReadyChanged;
        void Preload(string adUnitId);
        void Show(Action<RewardedAdShowResult> onFinished);
        void CancelPending();
    }

    public sealed class AppAdBridge : IRewardedAdBridge
    {
        private readonly IAppAdSdkDriver _driver;

        public uint ClientPlatform =>
#if UNITY_IOS
            (uint)global::AdClientPlatform.IosApp;
#else
            (uint)global::AdClientPlatform.AndroidApp;
#endif

        public bool IsSupported => _driver != null;
        public bool IsAdReady => _driver != null && _driver.IsReady;

        public event Action ReadyChanged
        {
            add { if (_driver != null) _driver.ReadyChanged += value; }
            remove { if (_driver != null) _driver.ReadyChanged -= value; }
        }

        public AppAdBridge(IAppAdSdkDriver driver)
        {
            _driver = driver;
        }

        public void Preload(string adUnitId)
        {
            if (_driver == null || string.IsNullOrWhiteSpace(adUnitId)) return;
            _driver.Preload(adUnitId.Trim());
        }

        public void Show(Action<RewardedAdShowResult> onFinished)
        {
            if (_driver == null)
            {
                onFinished?.Invoke(new RewardedAdShowResult(RewardedAdShowStatus.Unavailable, "app_ad_driver_missing"));
                return;
            }
            _driver.Show(onFinished);
        }

        public void CancelPending() => _driver?.CancelPending();
    }
}
#endif
