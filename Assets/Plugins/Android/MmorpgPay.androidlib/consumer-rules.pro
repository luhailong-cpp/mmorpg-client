# 微信 OpenSDK(反射调用 WXAPIFactory / PayReq 及回调页)
-keep class com.tencent.mm.opensdk.** { *; }
-keep class com.tencent.wxop.** { *; }
-keep class com.tencent.mm.sdk.** { *; }
-keep class com.mmorpg.pay.WechatPayCallbackActivity { *; }

# 支付宝 App 支付 SDK(反射调用 PayTask.payV2 及 AIDL 接口)
-keep class com.alipay.android.app.IAlixPay { *; }
-keep class com.alipay.android.app.IAlixPay$Stub { *; }
-keep class com.alipay.android.app.IRemoteServiceCallback { *; }
-keep class com.alipay.android.app.IRemoteServiceCallback$Stub { *; }
-keep class com.alipay.sdk.app.PayTask { public *; }
-keep class com.alipay.sdk.app.AuthTask { public *; }
-keep class com.alipay.sdk.app.H5PayCallback { *; }
-keep class com.alipay.sdk.util.** { *; }
