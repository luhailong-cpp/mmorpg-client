#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using AOT;
using Paymentpb;
using UnityEngine;

namespace MmorpgClient.Game.Jubaozhai
{
    /// <summary>
    /// 微信 / 抖音小游戏的 JS 支付接口(Assets/Plugins/WebGL/MinigamePay.jslib),直接调宿主的全局对象 wx / tt,不依赖两家的 Unity SDK。
    /// 同一时刻只有一笔支付(PaymentClient 保证),所以 JS 回调只留一个静态槽。
    /// </summary>
    public static class MinigamePay
    {
        // 回调用专门声明的委托类型,不用 Action<>(IL2CPP 下泛型委托不能做 P/Invoke 回调,同 WebSocketDrivers.cs / MiniGameLogin.cs)。
        private delegate void PayCallback(int requestId, string value);

        [DllImport("__Internal")] private static extern int MinigamePayHost();
        [DllImport("__Internal")] private static extern string MinigamePayDeviceOs();
        [DllImport("__Internal")] private static extern void MinigamePayWechatLogin(int requestId, PayCallback callback);
        [DllImport("__Internal")] private static extern void MinigamePayWechat(int requestId, string signData, string paySig, string signature, PayCallback callback);
        [DllImport("__Internal")] private static extern void MinigamePayDouyin(int requestId, string optionsJson, PayCallback callback);

        private static int _activeRequestId;
        private static Action<string> _pending;

        [MonoPInvokeCallback(typeof(PayCallback))]
        private static void Reply(int requestId, string value)
        {
            if (requestId != _activeRequestId) return;
            var pending = _pending;
            _pending = null;
            pending?.Invoke(value ?? "");
        }

        /// <summary>取消在途回调,迟到的 JS 回调按请求号丢弃。</summary>
        public static void Cancel() { ++_activeRequestId; _pending = null; }

        /// <summary>1 = 微信小游戏,2 = 抖音小游戏,0 = 普通浏览器。</summary>
        public static int Host() => MinigamePayHost();
        /// <summary>宿主报的系统:"ios" / "android" / "devtools" 等,取不到为空。</summary>
        public static string DeviceOs() => MinigamePayDeviceOs() ?? "";
        public static void WechatLogin(Action<string> code)
        {
            int id = ++_activeRequestId;
            _pending = code;
            MinigamePayWechatLogin(id, Reply);
        }
        public static void Wechat(string signData, string paySig, string signature, Action<string> error)
        {
            int id = ++_activeRequestId;
            _pending = error;
            MinigamePayWechat(id, signData, paySig, signature, Reply);
        }
        public static void Douyin(string optionsJson, Action<string> error)
        {
            int id = ++_activeRequestId;
            _pending = error;
            MinigamePayDouyin(id, optionsJson, Reply);
        }
    }

    /// <summary>微信小游戏道具直购:wx.requestMidasPaymentGameItem,三个参数都是服务端签好的。</summary>
    public sealed class WechatMinigamePayBridge : PayBridge
    {
        public override void Prepare(Action<string> loginCode) => MinigamePay.WechatLogin(loginCode);
        public override void CancelPending() => MinigamePay.Cancel();
        public override void Launch(CreatePayIntentResponse intent, Action<PayLaunch> done)
        {
            var p = intent.WechatMinigame;
            if (p == null) { done(new PayLaunch { Error = "支付参数缺失，请重试。" }); return; }
            MinigamePay.Wechat(p.SignData, p.PaySig, p.Signature, error => done(new PayLaunch { Error = error.Length == 0 ? "" : "支付没有完成。" }));
        }
    }

    /// <summary>抖音小游戏:tt.requestGamePayment,参数名与抖音文档一致,取值全部来自服务端。</summary>
    public sealed class DouyinMinigamePayBridge : PayBridge
    {
        [Serializable]
        private sealed class Options
        {
            public string mode, currencyType, platform, zoneId, customId, extraInfo, goodName;
            public long env, buyQuantity, goodType, orderAmount;
        }

        public override void CancelPending() => MinigamePay.Cancel();

        public override void Launch(CreatePayIntentResponse intent, Action<PayLaunch> done)
        {
            var p = intent.DouyinMinigame;
            if (p == null) { done(new PayLaunch { Error = "支付参数缺失，请重试。" }); return; }
            string json = JsonUtility.ToJson(new Options
            {
                mode = p.Mode, env = p.Env, currencyType = p.CurrencyType, platform = p.Platform, buyQuantity = p.BuyQuantity,
                zoneId = p.ZoneId, customId = p.CustomId, extraInfo = p.ExtraInfo, goodType = p.GoodType,
                orderAmount = (long)p.OrderAmount, goodName = p.GoodName
            });
            MinigamePay.Douyin(json, error => done(new PayLaunch { Error = error.Length == 0 ? "" : "支付没有完成。" }));
        }
    }
}
#endif
