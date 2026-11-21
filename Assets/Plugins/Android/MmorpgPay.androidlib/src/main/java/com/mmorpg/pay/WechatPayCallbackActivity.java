package com.mmorpg.pay;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

import com.tencent.mm.opensdk.modelbase.BaseReq;
import com.tencent.mm.opensdk.modelbase.BaseResp;
import com.tencent.mm.opensdk.openapi.IWXAPI;
import com.tencent.mm.opensdk.openapi.IWXAPIEventHandler;
import com.tencent.mm.opensdk.openapi.WXAPIFactory;

/**
 * 微信支付回调页(通过 AndroidManifest.xml 的 activity-alias 挂到 ${applicationId}.wxapi.WXPayEntryActivity)。
 * 只做两件事:把回调 Intent 交给 OpenSDK 处理,然后立刻 finish() 回到游戏。
 * 客户端不采信微信 SDK 的支付结果,到账与否由 PaymentClient 轮询服务端确认。
 */
public final class WechatPayCallbackActivity extends Activity implements IWXAPIEventHandler {
    private IWXAPI api;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        api = WXAPIFactory.createWXAPI(this, null, false);
        handle(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handle(intent);
    }

    private void handle(Intent intent) {
        try {
            if (api != null && intent != null) {
                api.handleIntent(intent, this);
            }
        } catch (RuntimeException ignored) {
            // 畸形或外部伪造的 Intent 不崩溃,直接关闭透明页返回游戏。
        } finally {
            finish();
        }
    }

    @Override
    public void onReq(BaseReq req) {
        // 支付回调不处理微信主动请求。
    }

    @Override
    public void onResp(BaseResp resp) {
        // 不采信客户端回调结果,服务端回调与查单才是唯一依据。
    }
}
