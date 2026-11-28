package com.mmorpg.secure;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * AndroidKeyStore AES-256-GCM 凭据存储桥(只存可吊销的 RefreshToken,不存密码)。
 * 密钥留在硬件/系统 KeyStore 内不导出,密文写进应用私有 SharedPreferences(MODE_PRIVATE),解密失败时清除损坏密文并 fail-closed。
 */
public final class SecureStoreBridge {
    private static final String KEYSTORE_PROVIDER = "AndroidKeyStore";
    private static final String KEY_ALIAS = "mmorpg_client_auth_aes256_gcm_v1";
    private static final String PREFS_NAME = "mmorpg_secure_auth_v1";
    private static final String TRANSFORMATION = "AES/GCM/NoPadding";
    private static final int GCM_IV_BYTES = 12;
    private static final int GCM_TAG_BITS = 128;

    private SecureStoreBridge() {}

    public static synchronized boolean save(Context context, String key, String secret) {
        if (context == null || key == null || key.isEmpty() || secret == null || secret.isEmpty()) {
            return false;
        }
        try {
            SecretKey aesKey = getOrCreateKey();
            Cipher cipher = Cipher.getInstance(TRANSFORMATION);
            cipher.init(Cipher.ENCRYPT_MODE, aesKey);
            byte[] iv = cipher.getIV();
            if (iv == null || iv.length != GCM_IV_BYTES) {
                return false;
            }
            cipher.updateAAD(key.getBytes(StandardCharsets.UTF_8));
            byte[] encrypted = cipher.doFinal(secret.getBytes(StandardCharsets.UTF_8));
            byte[] blob = new byte[iv.length + encrypted.length];
            System.arraycopy(iv, 0, blob, 0, iv.length);
            System.arraycopy(encrypted, 0, blob, iv.length, encrypted.length);
            SharedPreferences prefs = context.getApplicationContext().getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
            return prefs.edit().putString(key, Base64.encodeToString(blob, Base64.NO_WRAP)).commit();
        } catch (Throwable ignored) {
            return false;
        }
    }

    public static synchronized String load(Context context, String key) {
        if (context == null || key == null || key.isEmpty()) {
            return "";
        }
        try {
            SharedPreferences prefs = context.getApplicationContext().getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
            String encoded = prefs.getString(key, null);
            if (encoded == null || encoded.isEmpty()) {
                return "";
            }
            byte[] blob = Base64.decode(encoded, Base64.NO_WRAP);
            if (blob.length <= GCM_IV_BYTES) {
                prefs.edit().remove(key).apply();
                return "";
            }
            KeyStore ks = KeyStore.getInstance(KEYSTORE_PROVIDER);
            ks.load(null);
            SecretKey aesKey = (SecretKey) ks.getKey(KEY_ALIAS, null);
            if (aesKey == null) {
                prefs.edit().remove(key).apply();
                return "";
            }
            Cipher cipher = Cipher.getInstance(TRANSFORMATION);
            cipher.init(Cipher.DECRYPT_MODE, aesKey, new GCMParameterSpec(GCM_TAG_BITS, blob, 0, GCM_IV_BYTES));
            cipher.updateAAD(key.getBytes(StandardCharsets.UTF_8));
            byte[] plain = cipher.doFinal(blob, GCM_IV_BYTES, blob.length - GCM_IV_BYTES);
            return new String(plain, StandardCharsets.UTF_8);
        } catch (Throwable ignored) {
            try {
                SharedPreferences prefs = context.getApplicationContext().getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
                prefs.edit().remove(key).apply();
            } catch (Throwable ignored2) {
            }
            return "";
        }
    }

    public static synchronized void delete(Context context, String key) {
        if (context == null || key == null || key.isEmpty()) {
            return;
        }
        try {
            SharedPreferences prefs = context.getApplicationContext().getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
            prefs.edit().remove(key).commit();
        } catch (Throwable ignored) {
        }
    }

    private static SecretKey getOrCreateKey() throws Exception {
        KeyStore ks = KeyStore.getInstance(KEYSTORE_PROVIDER);
        ks.load(null);
        KeyStore.Entry existing = ks.getEntry(KEY_ALIAS, null);
        if (existing instanceof KeyStore.SecretKeyEntry) {
            return ((KeyStore.SecretKeyEntry) existing).getSecretKey();
        }
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE_PROVIDER);
        generator.init(new KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return generator.generateKey();
    }
}
