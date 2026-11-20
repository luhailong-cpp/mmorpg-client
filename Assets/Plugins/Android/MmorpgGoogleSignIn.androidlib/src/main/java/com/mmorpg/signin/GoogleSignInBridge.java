package com.mmorpg.signin;

import android.app.Activity;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.Looper;

import androidx.credentials.ClearCredentialStateRequest;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.ClearCredentialException;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.NoCredentialException;

import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.concurrent.Executor;

/**
 * Sign in with Google 原生桥(服务端 docs/design/account-deletion.md §3.2),经 Credential Manager 发起。
 * 玩家点按钮才拉起,所以用 GetSignInWithGoogleOption。结果是一段 JSON,交给 Unity 侧(Assets/Scripts/Net/PlatformSignIn.cs):
 * <pre>
 *   成功 {"ok":true,"identity_token":"..."}
 *   失败 {"ok":false,"error":"canceled"|"no_account"|"failed"}
 * </pre>
 * 不依赖 UnityPlayer(这个库的编译路径上没有 Unity 的类):Unity 侧用 AndroidJavaProxy 实现 {@link Callback}。
 * 令牌只经回调交出,这里不记日志、不落盘。
 */
public final class GoogleSignInBridge {
    /** Unity 侧实现;在 Android 主线程上被调用,每次请求恰好回调一次。 */
    public interface Callback {
        void onResult(String json);
    }

    private GoogleSignInBridge() {
    }

    /** serverClientId 是服务端 AuthProviders.Google.ClientId(Web 类型的 OAuth 客户端 ID)。 */
    public static void request(final Activity activity, final String serverClientId, final Callback cb) {
        if (activity == null) {
            deliver(cb, failure("failed"));
            return;
        }
        activity.runOnUiThread(() -> {
            try {
                GetCredentialRequest request = new GetCredentialRequest.Builder()
                        .addCredentialOption(new GetSignInWithGoogleOption.Builder(serverClientId).build())
                        .build();
                CredentialManager.create(activity).getCredentialAsync(activity, request, new CancellationSignal(),
                        mainExecutor(), new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                            @Override
                            public void onResult(GetCredentialResponse response) {
                                deliver(cb, idTokenResult(response.getCredential()));
                            }

                            @Override
                            public void onError(GetCredentialException e) {
                                String error = e instanceof GetCredentialCancellationException ? "canceled"
                                        : e instanceof NoCredentialException ? "no_account" : "failed";
                                deliver(cb, failure(error));
                            }
                        });
            } catch (RuntimeException e) {
                // 参数不对(如 serverClientId 为空)时 Builder 直接抛:也要回一次,Unity 侧在等。
                deliver(cb, failure("failed"));
            }
        });
    }

    /** 注销申请成功后清掉凭据状态,免得下次自动选中刚注销的账号(§3.2)。 */
    public static void clearCredentialState(final Activity activity, final Callback cb) {
        if (activity == null) {
            deliver(cb, failure("failed"));
            return;
        }
        activity.runOnUiThread(() -> {
            try {
                CredentialManager.create(activity).clearCredentialStateAsync(new ClearCredentialStateRequest(),
                        new CancellationSignal(), mainExecutor(),
                        new CredentialManagerCallback<Void, ClearCredentialException>() {
                            @Override
                            public void onResult(Void unused) {
                                deliver(cb, "{\"ok\":true}");
                            }

                            @Override
                            public void onError(ClearCredentialException e) {
                                deliver(cb, failure("failed"));
                            }
                        });
            } catch (RuntimeException e) {
                deliver(cb, failure("failed"));
            }
        });
    }

    private static String idTokenResult(Credential credential) {
        if (!(credential instanceof CustomCredential)
                || !GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
            return failure("failed");
        }
        String idToken;
        try {
            idToken = GoogleIdTokenCredential.createFrom(credential.getData()).getIdToken();
        } catch (Exception e) {
            // 解析失败抛 GoogleIdTokenParsingException;Kotlin 侧未必声明 @Throws,按 Exception 接才编得过。
            return failure("failed");
        }
        if (idToken == null || idToken.isEmpty()) {
            return failure("failed");
        }
        try {
            return new JSONObject().put("ok", true).put("identity_token", idToken).toString();
        } catch (JSONException e) {
            return failure("failed");
        }
    }

    private static String failure(String error) {
        try {
            return new JSONObject().put("ok", false).put("error", error).toString();
        } catch (JSONException e) {
            return "{\"ok\":false,\"error\":\"failed\"}";
        }
    }

    private static Executor mainExecutor() {
        Handler handler = new Handler(Looper.getMainLooper());
        return handler::post;
    }

    private static void deliver(Callback cb, String json) {
        if (cb != null) {
            cb.onResult(json);
        }
    }
}
