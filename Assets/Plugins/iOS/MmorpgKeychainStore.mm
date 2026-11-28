// iOS Keychain 凭据存储(防盗号记住登录态:仅存可吊销的 RefreshToken,绝不存明文密码)。
// 使用 kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly:绑定本机,不进入 iCloud 钥匙串同步或备份。

#import <Foundation/Foundation.h>
#import <Security/Security.h>
#import <stdlib.h>
#import <string.h>

static NSMutableDictionary* MmorpgKeychainBaseQuery(const char* service, const char* account)
{
    NSString* svc = service != NULL ? [NSString stringWithUTF8String:service] : @"";
    NSString* acct = account != NULL ? [NSString stringWithUTF8String:account] : @"";
    return [@{
        (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: svc,
        (__bridge id)kSecAttrAccount: acct,
    } mutableCopy];
}

extern "C" int MmorpgKeychain_Save(const char* service, const char* account, const char* secret)
{
    if (service == NULL || account == NULL || secret == NULL || secret[0] == '\0') return 0;
    NSData* data = [NSData dataWithBytes:secret length:strlen(secret)];
    NSMutableDictionary* query = MmorpgKeychainBaseQuery(service, account);
    SecItemDelete((__bridge CFDictionaryRef)query);
    query[(__bridge id)kSecValueData] = data;
    query[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly;
    OSStatus status = SecItemAdd((__bridge CFDictionaryRef)query, NULL);
    return status == errSecSuccess ? 1 : 0;
}

extern "C" char* MmorpgKeychain_Load(const char* service, const char* account)
{
    if (service == NULL || account == NULL) return NULL;
    NSMutableDictionary* query = MmorpgKeychainBaseQuery(service, account);
    query[(__bridge id)kSecReturnData] = @YES;
    query[(__bridge id)kSecMatchLimit] = (__bridge id)kSecMatchLimitOne;
    CFTypeRef result = NULL;
    OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
    if (status != errSecSuccess || result == NULL) return NULL;
    NSData* data = (__bridge_transfer NSData*)result;
    if (data.length == 0) return NULL;
    char* copy = (char*)malloc(data.length + 1);
    if (copy == NULL) return NULL;
    memcpy(copy, data.bytes, data.length);
    copy[data.length] = '\0';
    return copy;
}

extern "C" void MmorpgKeychain_Delete(const char* service, const char* account)
{
    if (service == NULL || account == NULL) return;
    NSMutableDictionary* query = MmorpgKeychainBaseQuery(service, account);
    SecItemDelete((__bridge CFDictionaryRef)query);
}

extern "C" void MmorpgKeychain_Free(char* ptr)
{
    if (ptr == NULL) return;
    memset(ptr, 0, strlen(ptr));
    free(ptr);
}
