using System;
using System.Collections;
using MmorpgClient.Net;


namespace MmorpgClient.Game
{
    // 实名与未成年人防沉迷(服务端 docs/design/real-name-anti-addiction.md §5.2~§5.7)。
    // 服务端是唯一的裁决者:gate 上的 Login 拦没实名的和不在可玩时段的未成年人,到点由续租踢线。
    // 客户端只做三件事:实名窗提交、不在可玩时段时不去排队、到点前提醒。小游戏包由平台实名,服务端不会要求实名。
    public sealed partial class GameClient
    {
        public const string RealNameRequiredMessage = "根据国家规定，需要完成实名认证才能进入游戏";
        public const string MinorNotInPlayTimeMessage = "根据国家规定，未成年人仅可在周五、周六、周日和法定节假日20:00-21:00进行游戏";
        public const string MinorPlayTimeOverMessage = "未成年人今日游戏时间已结束，请注意休息";

        /// <summary>最近一次 /api/login 或 /api/real-name 带回的防沉迷状态;部署没开防沉迷时全为默认值。</summary>
        public GatewayAntiAddiction AntiAddiction { get; private set; } = new GatewayAntiAddiction();

        // 到点前提醒的时刻(分钟),从早到晚。
        private static readonly int[] MinorReminderMinutes = { 10, 5, 1 };
        private int _minorReminderNext;

        private void ApplyAntiAddiction(GatewayAntiAddiction status)
        {
            AntiAddiction = status ?? new GatewayAntiAddiction();
            _minorReminderNext = 0;
        }

        /// <summary>
        /// 提交实名(用这次登录签发的 access token)。onDone 收到所有带 code 的结论,文案用 <see cref="DescribeRealNameResult"/>;
        /// 应答带回的状态写进 <see cref="AntiAddiction"/>。onError 只表示传输或解析失败。姓名和身份证号不记日志、不保存。
        /// </summary>
        public IEnumerator SubmitRealName(uint zoneId, string name, string idNumber,
                                          Action<GatewayRealNameResult> onDone, Action<string> onError)
        {
            GatewayRealNameResult result = null;
            string err = null;
            yield return _http.SubmitRealName(new GatewayRealNameRequest
            {
                zone_id = zoneId, access_token = AccessToken, name = name, id_number = idNumber,
            }, r => result = r, e => err = e);
            if (err != null) { onError(err); yield break; }
            if (result.code == GatewayRealNameResult.CodeOk || result.code == GatewayRealNameResult.CodeRejected)
                ApplyAntiAddiction(result.anti_addiction);
            Log($"real-name submit code={result.code} tip={result.tip}");
            onDone(result);
        }

        /// <summary>实名提交结论的文案;成功返回 null。</summary>
        public static string DescribeRealNameResult(GatewayRealNameResult r)
        {
            switch (r.code)
            {
                case GatewayRealNameResult.CodeOk: return null;
                case GatewayRealNameResult.CodeAuthRejected: return ChannelCredentialRejectedMessage;
                case GatewayRealNameResult.CodeRateLimited: return "操作过于频繁，请稍后重试";
                case GatewayRealNameResult.CodeRejected:
                    if (r.tip == (uint)login_error.KLoginRealNameInvalid) return "姓名或身份证号有误，请核对后重试";
                    if (r.tip == (uint)login_error.KLoginRealNamePending) return "实名认证中，请稍后重新登录";
                    if (r.tip == (uint)login_error.KLoginRealNameTooManyAttempts) return "今日实名认证次数已达上限，请明天再试";
                    if (r.tip == (uint)login_error.KLoginRealNameAlreadyBound) return "该账号已完成实名认证";
                    return $"实名认证未通过(tip={r.tip})";
                default: return "实名认证服务繁忙，请稍后重试";
            }
        }

        /// <summary>gate 上的 Login 因防沉迷被拒时的文案;不是这两种原因返回 null。</summary>
        public static string DescribeAntiAddictionTip(uint tipId)
        {
            if (tipId == (uint)login_error.KLoginRealNameRequired) return RealNameRequiredMessage;
            if (tipId == (uint)login_error.KLoginMinorNotInPlayTime) return MinorNotInPlayTimeMessage;
            return null;
        }

        /// <summary>到点前 10 / 5 / 1 分钟各提醒一次。到点踢线由服务端做,提醒漏了不影响限制。</summary>
        private void TickMinorPlayTimeReminder()
        {
            if (!InGame || !AntiAddiction.minor || AntiAddiction.play_deadline_ms <= 0) return;
            if (_minorReminderNext >= MinorReminderMinutes.Length) return;
            long remainingMs = AntiAddiction.play_deadline_ms - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int minutes = MinorReminderMinutes[_minorReminderNext];
            if (remainingMs > minutes * 60_000L) return;
            // 一次跳过已经错过的提醒点(例如进游戏时只剩 3 分钟),只提示离现在最近的那一个。
            while (_minorReminderNext + 1 < MinorReminderMinutes.Length
                   && remainingMs <= MinorReminderMinutes[_minorReminderNext + 1] * 60_000L)
                _minorReminderNext++;
            int shown = Math.Max(1, (int)Math.Ceiling(remainingMs / 60_000.0));
            _minorReminderNext++;
            if (remainingMs <= 0) return;
            Status($"未成年人今日游戏时间还剩约 {shown} 分钟，到点将自动下线");
        }
    }
}
