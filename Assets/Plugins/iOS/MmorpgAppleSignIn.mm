// Sign in with Apple 原生桥(服务端 docs/design/account-deletion.md §3.1)。
// 不请求任何 scope:账号只用 identity token 里的 sub,不收邮箱和姓名。
// 结果是一段 JSON,经 UnitySendMessage 交给 C# 的常驻接收对象(Assets/Scripts/Net/PlatformSignIn.cs):
//   成功 {"ok":true,"identity_token":"...","authorization_code":"..."}
//   失败 {"ok":false,"error":"canceled"|"failed"|"unsupported"}
// 令牌与授权码只经这一条回调交出,这里不记日志、不落盘。

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <AuthenticationServices/AuthenticationServices.h>

// 都由 Unity 导出的 Xcode 工程提供(UnityInterface.h / UnityAppController.mm)。
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);
extern "C" UIWindow* UnityGetMainWindow(void);

static void MmorpgAppleSignInSend(NSString* gameObject, NSString* method, NSDictionary* result)
{
    NSData* data = [NSJSONSerialization dataWithJSONObject:result options:0 error:nil];
    NSString* json = data != nil ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : nil;
    if (json == nil) json = @"{\"ok\":false,\"error\":\"failed\"}";
    UnitySendMessage(gameObject.UTF8String, method.UTF8String, json.UTF8String);
}

static NSString* MmorpgUtf8(NSData* data)
{
    return data != nil ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : nil;
}

API_AVAILABLE(ios(13.0))
@interface MmorpgAppleSignInDelegate : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding>
@property (nonatomic, copy) NSString* gameObject;
@property (nonatomic, copy) NSString* method;
@property (nonatomic, strong) ASAuthorizationController* controller;
@end

// ASAuthorizationController 的 delegate 是 weak:这里持有到回调为止。回调后不清,留到下一次请求时替换,
// 免得在 controller 自己的回调栈里把它释放掉。C# 侧保证同一时间只有一个请求在途。
static NSObject* gMmorpgAppleSignInPending = nil;

@implementation MmorpgAppleSignInDelegate

- (void)authorizationController:(ASAuthorizationController*)controller
   didCompleteWithAuthorization:(ASAuthorization*)authorization
{
    ASAuthorizationAppleIDCredential* credential = nil;
    if ([authorization.credential isKindOfClass:[ASAuthorizationAppleIDCredential class]])
        credential = (ASAuthorizationAppleIDCredential*)authorization.credential;
    NSString* identityToken = MmorpgUtf8(credential.identityToken);
    NSString* authorizationCode = MmorpgUtf8(credential.authorizationCode);
    if (identityToken.length == 0)
    {
        MmorpgAppleSignInSend(self.gameObject, self.method, @{ @"ok": @NO, @"error": @"failed" });
        return;
    }
    MmorpgAppleSignInSend(self.gameObject, self.method, @{
        @"ok": @YES,
        @"identity_token": identityToken,
        @"authorization_code": authorizationCode ?: @""
    });
}

- (void)authorizationController:(ASAuthorizationController*)controller didCompleteWithError:(NSError*)error
{
    BOOL canceled = [error.domain isEqualToString:ASAuthorizationErrorDomain] && error.code == ASAuthorizationErrorCanceled;
    MmorpgAppleSignInSend(self.gameObject, self.method, @{ @"ok": @NO, @"error": canceled ? @"canceled" : @"failed" });
}

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController*)controller
{
    return UnityGetMainWindow();
}

@end

extern "C" void MmorpgAppleSignIn_Request(const char* gameObject, const char* method)
{
    NSString* target = gameObject != NULL ? [NSString stringWithUTF8String:gameObject] : @"";
    NSString* callback = method != NULL ? [NSString stringWithUTF8String:method] : @"";
    if (@available(iOS 13.0, *))
    {
        // 不设 requestedScopes:不要邮箱和姓名。
        ASAuthorizationAppleIDRequest* request = [[[ASAuthorizationAppleIDProvider alloc] init] createRequest];
        MmorpgAppleSignInDelegate* pending = [[MmorpgAppleSignInDelegate alloc] init];
        pending.gameObject = target;
        pending.method = callback;
        pending.controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[ request ]];
        pending.controller.delegate = pending;
        pending.controller.presentationContextProvider = pending;
        gMmorpgAppleSignInPending = pending;
        [pending.controller performRequests];
        return;
    }
    MmorpgAppleSignInSend(target, callback, @{ @"ok": @NO, @"error": @"unsupported" });
}
