using System;

namespace MmorpgClient.Game.RewardedAd
{
    public enum RewardedAdShowStatus
    {
        Completed = 0,
        DismissedEarly = 1,
        Unavailable = 2,
        Failed = 3,
    }

    public readonly struct RewardedAdShowResult
    {
        public RewardedAdShowStatus Status { get; }
        public string ErrorMessage { get; }
        public string SsvProof { get; }

        public bool IsCompleted => Status == RewardedAdShowStatus.Completed;

        public RewardedAdShowResult(RewardedAdShowStatus status, string errorMessage = "", string ssvProof = "")
        {
            Status = status;
            ErrorMessage = errorMessage ?? string.Empty;
            SsvProof = ssvProof ?? string.Empty;
        }
    }

    /// <summary>
    /// 激励视频广告宿主桥接抽象：
    /// 小游戏端由 <see cref="MinigameAdBridge"/> 直接调用宿主 wx/tt；
    /// 首审 App / 编辑器默认使用 <see cref="NullRewardedAdBridge"/>（零第三方广告 SDK，彻底隐藏入口）。
    /// </summary>
    public interface IRewardedAdBridge
    {
        /// <summary>对齐 proto AdClientPlatform（0=未支持 1=微信小游戏 2=抖音小游戏 3=iOS App 4=Android App）。</summary>
        uint ClientPlatform { get; }

        /// <summary>当前宿主是否具备激励视频播放能力。</summary>
        bool IsSupported { get; }

        /// <summary>当前广告素材是否已加载就绪；未就绪时前端必须隐藏入口（防 App Store 2.1 空按钮拒审）。</summary>
        bool IsAdReady { get; }

        event Action ReadyChanged;

        void Preload(string adUnitId);

        void Show(Action<RewardedAdShowResult> onFinished);

        void CancelPending();
    }
}
