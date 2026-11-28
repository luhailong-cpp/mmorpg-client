namespace MmorpgClient.Game.RewardedAd
{
    /// <summary>
    /// 发行形态标识（与服务端 M-8 合规门禁矩阵一一对应）。
    /// </summary>
    public enum ClientDistributionChannel
    {
        Unknown = 0,
        WechatMinigameUnlicensed = 1,
        DouyinMinigameUnlicensed = 2,
        GlobalIos = 3,
        GlobalAndroid = 4,
        SteamPc = 5,
    }

    /// <summary>
    /// 发行渠道功能可见性策略（只读不可变）：
    /// - 国内无版号小游戏物理隐藏全部真钱充值、赠礼与 DLC；
    /// - Steam PC 端物理隐藏全部内置广告；
    /// - 全渠道禁止任何站外切支付。
    /// </summary>
    public readonly struct ClientDistributionFlags
    {
        public ClientDistributionChannel Channel { get; }
        public bool IapEnabled { get; }
        public bool GiftingEnabled { get; }
        public bool SteamDlcEnabled { get; }
        public bool RewardedAdAllowed { get; }
        public bool ExternalPaymentAllowed { get; }

        public ClientDistributionFlags(
            ClientDistributionChannel channel,
            bool iapEnabled,
            bool giftingEnabled,
            bool steamDlcEnabled,
            bool rewardedAdAllowed)
        {
            Channel = channel;
            IapEnabled = iapEnabled;
            GiftingEnabled = giftingEnabled;
            SteamDlcEnabled = steamDlcEnabled;
            RewardedAdAllowed = rewardedAdAllowed;
            ExternalPaymentAllowed = false;
        }
    }

    public static class ClientDistributionPolicy
    {
        private static ClientDistributionChannel s_activeChannel = DefaultChannel();

        public static ClientDistributionChannel ActiveChannel
        {
            get => s_activeChannel;
            set => s_activeChannel = value;
        }

        public static ClientDistributionFlags Current => Resolve(s_activeChannel);

        private static ClientDistributionChannel DefaultChannel()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ClientDistributionChannel.WechatMinigameUnlicensed;
#elif UNITY_STANDALONE && !UNITY_EDITOR
            return ClientDistributionChannel.SteamPc;
#elif UNITY_IOS && !UNITY_EDITOR
            return ClientDistributionChannel.GlobalIos;
#else
            return ClientDistributionChannel.GlobalAndroid;
#endif
        }

        public static ClientDistributionFlags Resolve(ClientDistributionChannel channel) => channel switch
        {
            ClientDistributionChannel.WechatMinigameUnlicensed or ClientDistributionChannel.DouyinMinigameUnlicensed =>
                new ClientDistributionFlags(channel, iapEnabled: false, giftingEnabled: false, steamDlcEnabled: false, rewardedAdAllowed: true),
            ClientDistributionChannel.SteamPc =>
                new ClientDistributionFlags(channel, iapEnabled: true, giftingEnabled: true, steamDlcEnabled: true, rewardedAdAllowed: false),
            ClientDistributionChannel.GlobalIos or ClientDistributionChannel.GlobalAndroid =>
                new ClientDistributionFlags(channel, iapEnabled: true, giftingEnabled: true, steamDlcEnabled: false, rewardedAdAllowed: true),
            _ => new ClientDistributionFlags(channel, iapEnabled: false, giftingEnabled: false, steamDlcEnabled: false, rewardedAdAllowed: false),
        };
    }
}
