// 官方通行证(Casdoor OIDC)在 iOS 上的系统认证会话(服务端 docs/design/official-passport-registration.md §7)。
//
// 用 ASWebAuthenticationSession 在 App 内弹出系统认证页,不用 Application.OpenURL 跳到 Safari:
//   - App Store 审核指南 4.0 会拒掉「把用户带到 Safari 去登录 / 注册」的做法;
//   - 回调只交给发起会话的这个 App,别的 App 注册同名 scheme 也截不到;
//   - App 一直在前台,不会因为被系统回收而丢掉内存里的 code_verifier。
// 结果是回调地址原文,经 UnitySendMessage 交给 C#(Assets/Scripts/Net/PassportSignIn.cs);玩家取消或会话起不来时交空串。
// 回调地址里带一次性授权码:只经这一条回调交出,这里不记日志、不落盘。

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <AuthenticationServices/AuthenticationServices.h>

// 都由 Unity 导出的 Xcode 工程提供(UnityInterface.h / UnityAppController.mm)。
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);
extern "C" UIWindow* UnityGetMainWindow(void);

@interface MmorpgPassportWebAuthContext : NSObject <ASWebAuthenticationPresentationContextProviding>
@end

@implementation MmorpgPassportWebAuthContext

- (ASPresentationAnchor)presentationAnchorForWebAuthenticationSession:(ASWebAuthenticationSession*)session
{
    return UnityGetMainWindow();
}

@end

// 会话和它的 presentationContextProvider(weak)都要持有到回调为止。C# 侧保证同一时间只有一个请求在途。
static ASWebAuthenticationSession* gMmorpgPassportSession = nil;
static MmorpgPassportWebAuthContext* gMmorpgPassportContext = nil;
// 每次发起或取消加一。回调只在自己那一代还是当前代时才交给 C#:
// 我们主动取消的会话也会走到回调(带「已取消」错误),那一条不能被当成下一次请求的结果。
static NSInteger gMmorpgPassportGeneration = 0;

extern "C" void MmorpgPassportWebAuth_Cancel(void)
{
    gMmorpgPassportGeneration++;
    [gMmorpgPassportSession cancel];
    gMmorpgPassportSession = nil;
}

extern "C" void MmorpgPassportWebAuth_Start(const char* authorizeUrl, const char* callbackScheme, const char* gameObject, const char* method)
{
    NSString* target = gameObject != NULL ? [NSString stringWithUTF8String:gameObject] : @"";
    NSString* callback = method != NULL ? [NSString stringWithUTF8String:method] : @"";
    NSURL* url = authorizeUrl != NULL ? [NSURL URLWithString:[NSString stringWithUTF8String:authorizeUrl]] : nil;
    NSString* scheme = callbackScheme != NULL ? [NSString stringWithUTF8String:callbackScheme] : nil;

    MmorpgPassportWebAuth_Cancel();
    NSInteger generation = gMmorpgPassportGeneration;
    if (url == nil || scheme.length == 0)
    {
        UnitySendMessage(target.UTF8String, callback.UTF8String, "");
        return;
    }

    ASWebAuthenticationSession* session = [[ASWebAuthenticationSession alloc]
        initWithURL:url
        callbackURLScheme:scheme
        completionHandler:^(NSURL* callbackURL, NSError* error) {
            dispatch_async(dispatch_get_main_queue(), ^{
                if (generation != gMmorpgPassportGeneration) return;
                gMmorpgPassportSession = nil;
                // 玩家取消或会话失败时 callbackURL 为 nil:交空串,C# 按取消处理。
                NSString* result = callbackURL != nil ? callbackURL.absoluteString : @"";
                UnitySendMessage(target.UTF8String, callback.UTF8String, result.UTF8String);
            });
        }];
    if (gMmorpgPassportContext == nil) gMmorpgPassportContext = [[MmorpgPassportWebAuthContext alloc] init];
    session.presentationContextProvider = gMmorpgPassportContext;
    // 不与 Safari 共享 cookie:通行证应用本来就不留登录会话,也免掉「某 App 想使用某网站登录」的系统弹窗。
    session.prefersEphemeralWebBrowserSession = YES;
    gMmorpgPassportSession = session;
    if (![session start])
    {
        gMmorpgPassportSession = nil;
        UnitySendMessage(target.UTF8String, callback.UTF8String, "");
    }
}
