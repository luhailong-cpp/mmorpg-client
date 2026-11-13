using System;
using System.Collections;
using System.Collections.Generic;
using Loginpb;
using MmorpgClient.Net;

namespace MmorpgClient.Game
{
    /// <summary>
    /// 同一条 gate 连接上的进场与换角(服务端 docs/design/client-request-ordering.md §6.2「login 会话栅栏」):
    /// <list type="bullet">
    /// <item>进场失败(3023)之后服务端特意保留本连接的登录态,供同一连接重试;但本连接上可能已经绑着那个角色
    ///   (C++ 版 3023 不解绑,Java 版解绑)。要进另一个角色,先 LeaveGame 解除绑定,再 EnterGame。</item>
    /// <item>已绑定角色时换角收到 2005(kLoginInProgress:gate 换角闸回信封、login 栅栏回响应体),按「先 LeaveGame」处理,
    ///   不当成「登录会话不存在」去重新登录:重新 Login 不解除绑定,换角照样被拒。</item>
    /// <item>C++ 版 LeaveGame 会清掉本连接的登录态,之后 EnterGame 回 2028(kLoginSessionNotFound):这时在同一连接上
    ///   带原凭据重新 Login 一次再进(Java 版 LeaveGame 保留账号,不会走到这一步)。</item>
    /// <item>同一角色重发 EnterGame(超时或信封 1003 之后)照常重发:沿用同一个 request_id,不先 LeaveGame。</item>
    /// </list>
    /// 「本连接上可能绑着的角色」= 最近一次发出 EnterGame 的那个(_enterRequestPlayerId:发出即记、断线清零、LeaveGame 成功清零)。
    /// 发出就算:应答没回来时服务端可能已经受理并绑定。
    /// </summary>
    public sealed partial class GameClient
    {
        /// <summary>LeaveGame 结果不明时最多发几次(含第一次);LeaveGame 在服务端可重放。</summary>
        private const int LeaveGameMaxAttempts = 3;

        /// <summary>
        /// 一次登录里进场失败后最多回选角几次。真人选角自带节奏;自动选角器(DevAutoPilot)会立刻再选,
        /// 服务端持续回 3023 时不封顶就成了紧循环。用完之后照旧拆连接回选服。
        /// </summary>
        private const int MaxEnterReselects = 3;

        /// <summary>进场失败后回到选角时给人看的原因。</summary>
        public const string EnterRejectedReselectHint = "进入场景失败，请重新进入或选择其他角色";

        // 最近一次 Login / CreatePlayer 应答里的账号角色(服务端给的顺序;CacheRoleMetadata 维护,断线清空)。回选角时据此重建本区角色列表。
        private readonly List<AccountSimplePlayer> _roleRoster = new();

        /// <summary>一次 EnterGame 应答(或失败)之后下一步做什么,见 <see cref="ClassifyEnterGameReply"/>。</summary>
        public enum EnterGameStep
        {
            /// <summary>应答无错:已受理,去等进场通知。</summary>
            Accepted,
            /// <summary>同一角色的重发撞上前一次的进场链(玩家锁被占):前一次多半已受理,接着等进场通知或进场失败。</summary>
            InProgress,
            /// <summary>响应体带码拒绝:按码收尾(调用方显示「进入游戏失败(tip=N)」)。</summary>
            Rejected,
            /// <summary>传输层失败或信封拒绝:收掉管线。</summary>
            Failed,
            /// <summary>结果不明:同一请求原样重发一次。</summary>
            Resend,
            /// <summary>本连接上还绑着另一个角色:先 LeaveGame,再重发。</summary>
            LeaveThenEnter,
            /// <summary>自己刚 LeaveGame 过、登录态随之被清:同一连接上重新 Login,再重发。</summary>
            LoginThenEnter,
        }

        /// <summary>进 targetRole 之前要不要先 LeaveGame:本连接上可能绑着的是另一个角色。</summary>
        public static bool NeedsLeaveGameBeforeEnter(ulong boundRole, ulong targetRole)
            => boundRole != 0 && boundRole != targetRole;

        /// <summary>
        /// EnterGame 的结局分类(纯函数)。
        /// </summary>
        /// <param name="legacyError">传输层错误串(RpcStatus.ToLegacyError);收到应答时为 null。</param>
        /// <param name="responseTip">响应体里的码;没有应答或没有码时为 0。</param>
        /// <param name="boundBefore">这次发出之前本连接上可能绑着的角色。</param>
        /// <param name="resent">这次已经是结果不明之后的重发。</param>
        /// <param name="left">这一次进场里已经 LeaveGame 过。</param>
        /// <param name="relogged">这一次进场里已经重新 Login 过。</param>
        public static EnterGameStep ClassifyEnterGameReply(string legacyError, uint responseTip, ulong boundBefore,
            ulong targetRole, bool resent, bool left, bool relogged)
        {
            uint tip = responseTip;
            if (legacyError != null)
            {
                // 结果不明(超时 / 信封 1003):同一角色、同一请求原样重发一次,不先 LeaveGame。
                if (ClientRequestKeys.ShouldResendOnce(legacyError)) return resent ? EnterGameStep.Failed : EnterGameStep.Resend;
                // 其余传输层失败(断线 / 没发出 / 解析失败)收掉管线;信封上的码接着按码判。
                if (!RpcStatus.TryParseLegacyServerTip(legacyError, out tip)) return EnterGameStep.Failed;
            }
            if (tip == 0) return EnterGameStep.Accepted;
            if (tip == (uint)login_error.KLoginInProgress)
            {
                if (NeedsLeaveGameBeforeEnter(boundBefore, targetRole) && !left) return EnterGameStep.LeaveThenEnter;
                if (resent && boundBefore == targetRole) return EnterGameStep.InProgress;
            }
            if (tip == (uint)login_error.KLoginSessionNotFound && left && !relogged) return EnterGameStep.LoginThenEnter;
            return legacyError != null ? EnterGameStep.Failed : EnterGameStep.Rejected;
        }

        /// <summary>EnterGameOnConnection 的结果:Response 非 null = 拿到了要交给调用方的应答(受理,或带码的拒绝);null = 管线已收口。</summary>
        private sealed class EnterGameOutcome
        {
            public EnterGameResponse Response;
        }

        /// <summary>
        /// 在当前连接上为 playerId 发 EnterGame,按 <see cref="ClassifyEnterGameReply"/> 处理换角、重发与重新登录,
        /// 直到拿到受理或明确拒绝(写进 outcome),或管线已失败(outcome.Response 为 null)。
        /// </summary>
        private IEnumerator EnterGameOnConnection(int gen, ulong playerId, string passwordFallback, Action<string> onError,
            EnterGameOutcome outcome)
        {
            // 一次进场操作一个请求号;结果不明时的重发原样复用它(login.proto:同一请求的重试必须沿用)。
            var request = new EnterGameRequest { PlayerId = playerId, RequestId = Guid.NewGuid().ToString("N") };
            bool resent = false, left = false, relogged = false;
            if (NeedsLeaveGameBeforeEnter(_enterRequestPlayerId, playerId))
            {
                Log($"[enter] switching role on the same connection: leaving player_id={_enterRequestPlayerId} before entering player_id={playerId}");
                bool leftBound = false;
                yield return LeaveGameOnConnection(gen, onError, done => leftBound = done);
                if (gen != _pipelineGen || !leftBound) yield break;
                left = true;
            }
            // 失败 tip 在发出 EnterGame 之前开始听(见 EnterGameAndWaitScene);LeaveGame 之前收到的旧 tip 不算这一次的。
            _enterFailedTipId = 0;
            _awaitingSceneEntry = true;
            Status("正在进入游戏…");
            while (true)
            {
                ulong boundBefore = _enterRequestPlayerId;
                _enterRequestPlayerId = playerId;
                EnterGameResponse response = null;
                string error = null;
                yield return Call(MessageIds.EnterGame, request, EnterGameResponse.Parser, r => response = r, e => error = e);
                if (gen != _pipelineGen) yield break;
                var step = ClassifyEnterGameReply(error, response?.ErrorMessage?.Id ?? 0, boundBefore, playerId,
                    resent, left, relogged);
                switch (step)
                {
                    case EnterGameStep.Accepted:
                    case EnterGameStep.Rejected:
                        outcome.Response = response;
                        yield break;
                    case EnterGameStep.InProgress:
                        Log($"[enter] resent enter game found the previous enter of player_id={playerId} still in progress; waiting for it");
                        outcome.Response = new EnterGameResponse { PlayerId = playerId };
                        yield break;
                    case EnterGameStep.Resend:
                        resent = true;
                        Log($"[enter] enter game outcome unknown ({error}); resending the same request once");
                        continue;
                    case EnterGameStep.LeaveThenEnter:
                    {
                        Log($"[enter] another role may be bound on this connection; leaving before entering player_id={playerId}");
                        bool leftBound = false;
                        yield return LeaveGameOnConnection(gen, onError, done => leftBound = done);
                        if (gen != _pipelineGen || !leftBound) yield break;
                        left = true;
                        _enterFailedTipId = 0;
                        Status("正在进入游戏…");
                        continue;
                    }
                    case EnterGameStep.LoginThenEnter:
                    {
                        Log("[enter] login session was cleared by LeaveGame; logging in again on the same connection");
                        bool loggedIn = false;
                        yield return ReloginOnConnection(gen, passwordFallback, onError, done => loggedIn = done);
                        if (gen != _pipelineGen || !loggedIn) yield break;
                        relogged = true;
                        Status("正在进入游戏…");
                        continue;
                    }
                    default:
                        FailPipeline(gen, onError, $"enter game: {error}");
                        yield break;
                }
            }
        }

        /// <summary>
        /// 解除本连接上绑定的角色。结果不明(超时 / 信封 1003;LoginRedis 故障时 login 按 fail-closed 回 1003)原样重发,
        /// 最多 <see cref="LeaveGameMaxAttempts"/> 次:服务端的 LeaveGame 可重放(会话栅栏的 Leave 脚本、清登录态、删会话都幂等)。
        /// 确认不了就收掉管线(断线后服务端按断线收尾),不带着可能还在的绑定去进另一个角色。
        /// </summary>
        private IEnumerator LeaveGameOnConnection(int gen, Action<string> onError, Action<bool> done)
        {
            Status("正在退出当前角色…");
            for (int attempt = 1; ; ++attempt)
            {
                bool acknowledged = false;
                string error = null;
                yield return Call(MessageIds.LeaveGame, new LeaveGameRequest(), LoginEmptyResponse.Parser,
                    _ => acknowledged = true, e => error = e);
                if (gen != _pipelineGen) yield break;
                if (acknowledged)
                {
                    // 本连接上不再绑着角色;gate 收到 LeaveGame 时代发 ExitGame 并清会话路由。
                    _enterRequestPlayerId = 0;
                    PlayerId = 0;
                    done(true);
                    yield break;
                }
                if (attempt < LeaveGameMaxAttempts && ClientRequestKeys.ShouldResendOnce(error))
                {
                    Log($"[enter] leave game outcome unknown ({error}); resending");
                    continue;
                }
                Log($"[enter] leave game failed: {error}");
                FailPipeline(gen, onError, "无法退出当前角色，请重新登录");
                done(false);
                yield break;
            }
        }

        /// <summary>LeaveGame 清掉登录态之后在同一连接上重新 Login(凭据同 ConnectAndEnter),刷新角色列表。</summary>
        private IEnumerator ReloginOnConnection(int gen, string passwordFallback, Action<string> onError, Action<bool> done)
        {
            Status("正在验证会话…");
            LoginResponse response = null;
            string error = null;
            yield return Call(MessageIds.Login, BuildSessionLoginRequest(passwordFallback), LoginResponse.Parser,
                r => response = r, e => error = e);
            if (gen != _pipelineGen) yield break;
            if (response == null)
            {
                FailPipeline(gen, onError, $"login: {error}");
                done(false);
                yield break;
            }
            if (response.ErrorMessage != null && response.ErrorMessage.Id != 0)
            {
                FailPipeline(gen, onError, $"登录失败(tip={response.ErrorMessage.Id})");
                done(false);
                yield break;
            }
            if (!string.IsNullOrEmpty(response.AccessToken))
                SetTokens(response.AccessToken, response.RefreshToken, response.AccessTokenExpire);
            CacheRoleMetadata(response.Players);
            done(true);
        }

        /// <summary>
        /// gate 上的 TCP Login:优先用 access_token 复认证(口令不走游戏 TCP),HTTP 那一步没发 token 时才退回口令。
        /// </summary>
        private LoginRequest BuildSessionLoginRequest(string passwordFallback)
            => !string.IsNullOrEmpty(AccessToken)
                ? new LoginRequest { Account = Account, AuthType = "access_token", AuthToken = AccessToken }
                : new LoginRequest { Account = Account, Password = passwordFallback ?? "", AuthType = "password" };

        /// <summary>
        /// 按选中区过滤账号角色;zone_id == 0 的存量角色(加字段前创建)在任何区都可见,避免旧账号一夜之间「角色消失」。
        /// </summary>
        private static List<AccountSimplePlayer> ZonePlayers(IEnumerable<AccountSimplePlayer> roster, uint zoneId)
        {
            var result = new List<AccountSimplePlayer>();
            foreach (var player in roster)
                if (player != null && (zoneId == 0 || player.ZoneId == zoneId || player.ZoneId == 0)) result.Add(player);
            return result;
        }
    }
}
