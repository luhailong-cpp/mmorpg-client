#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Threading;
using Paymentpb;
using UnityEngine;

namespace MmorpgClient.Game.Jubaozhai
{
    // 安卓 App 的微信支付与支付宝。两家 SDK 都经反射调用,不在编译期依赖它们的库:
    // 打包时要带上微信 OpenSDK(com.tencent.mm.opensdk:wechat-sdk-android)与支付宝 SDK(com.alipay.sdk:alipaysdk-android),
    // 微信还要在应用包名下提供 wxapi.WXPayEntryActivity(付完靠它回到游戏)。SDK 的支付结果一律不取,回去等服务端的结论。

    /// <summary>微信支付(App):把服务端签好的 7 个参数原样填进 PayReq。</summary>
    public sealed class WechatAppPayBridge : PayBridge
    {
        public override void Launch(CreatePayIntentResponse intent, Action<PayLaunch> done)
        {
            var p = intent.WechatApp;
            if (p == null) { done(new PayLaunch { Error = "支付参数缺失，请重试。" }); return; }
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null) { done(new PayLaunch { Error = "支付界面不可用，请重试。" }); return; }
                using var factory = new AndroidJavaClass("com.tencent.mm.opensdk.openapi.WXAPIFactory");
                using var api = factory.CallStatic<AndroidJavaObject>("createWXAPI", activity, p.AppId, false);
                if (api == null) { done(new PayLaunch { Error = "微信支付组件初始化失败。" }); return; }
                api.Call<bool>("registerApp", p.AppId);
                using var request = new AndroidJavaObject("com.tencent.mm.opensdk.modelpay.PayReq");
                request.Set("appId", p.AppId); request.Set("partnerId", p.PartnerId); request.Set("prepayId", p.PrepayId);
                request.Set("packageValue", p.PackageValue); request.Set("nonceStr", p.NonceStr);
                request.Set("timeStamp", p.Timestamp); request.Set("sign", p.Sign);
                done(api.Call<bool>("sendReq", request) ? default : new PayLaunch { Error = "没能打开微信，请确认已安装微信。" });
            }
            catch (AndroidJavaException) { done(new PayLaunch { Error = "这个安装包没有带微信支付组件。" }); }
            catch (Exception) { done(new PayLaunch { Error = "打开微信支付失败，请稍后再试。" }); }
        }
    }

    /// <summary>支付宝(App):PayTask.payV2 是阻塞调用,放后台线程,完成后回主线程。</summary>
    public sealed class AlipayAppPayBridge : PayBridge
    {
        public override void Launch(CreatePayIntentResponse intent, Action<PayLaunch> done)
        {
            string order = intent.AlipayApp?.OrderString ?? "";
            if (order.Length == 0) { done(new PayLaunch { Error = "支付参数缺失，请重试。" }); return; }
            var main = SynchronizationContext.Current; // Unity 主线程的上下文
            if (main == null) { done(new PayLaunch { Error = "支付界面不可用，请重试。" }); return; }
            new Thread(() =>
            {
                var result = default(PayLaunch);
                bool attached = false;
                try
                {
                    AndroidJNI.AttachCurrentThread();
                    attached = true;
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    if (activity == null) { result.Error = "支付界面不可用，请重试。"; return; }
                    using var task = new AndroidJavaObject("com.alipay.sdk.app.PayTask", activity);
                    using var answer = task.Call<AndroidJavaObject>("payV2", order, true);
                }
                catch (AndroidJavaException) { result.Error = "这个安装包没有带支付宝支付组件。"; }
                catch (Exception) { result.Error = "打开支付宝失败，请稍后再试。"; }
                finally
                {
                    if (attached)
                    {
                        try { AndroidJNI.DetachCurrentThread(); } catch (Exception) { }
                    }
                    main.Post(_ => done(result), null);
                }
            }) { IsBackground = true }.Start();
        }
    }
}
#endif
