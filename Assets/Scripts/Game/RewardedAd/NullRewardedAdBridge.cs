using System;

namespace MmorpgClient.Game.RewardedAd
{
    /// <summary>
    /// 默认空广告桥（用于编辑器、PC 端与未定义 MMORPG_ENABLE_APP_AD_SDK 的首审 App 构建）：
    /// 恒定未支持、未就绪，确保首审包中入口彻底隐藏且不携带任何广告 SDK 符号。
    /// </summary>
    public sealed class NullRewardedAdBridge : IRewardedAdBridge
    {
        public static readonly NullRewardedAdBridge Instance = new NullRewardedAdBridge();

        public uint ClientPlatform => (uint)global::AdClientPlatform.Unspecified;
        public bool IsSupported => false;
        public bool IsAdReady => false;

        public event Action ReadyChanged
        {
            add { }
            remove { }
        }

        public void Preload(string adUnitId) { }

        public void Show(Action<RewardedAdShowResult> onFinished)
        {
            onFinished?.Invoke(new RewardedAdShowResult(RewardedAdShowStatus.Unavailable, "ad_bridge_disabled"));
        }

        public void CancelPending() { }
    }
}
