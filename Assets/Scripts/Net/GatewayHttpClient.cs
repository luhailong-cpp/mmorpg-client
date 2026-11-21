using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace MmorpgClient.Net
{
    /// <summary>
    /// Result of POST /api/assign-gate and POST /api/queue-status.
    /// Mirrors the Java gateway AssignGateResponse shape (snake_case JSON).
    ///
    /// code semantics (see gateway AssignGateService):
    ///   0   admitted   — gate_ip/port/token* populated, connect now
    ///   100 queueing   — queue_token/queue_rank/queue_total/retry_after_ms populated
    ///   401 access token rejected — log in again (not a transient failure)
    ///   404 zone_not_found
    ///   410 queue expired — restart from /api/assign-gate
    ///   429 rate-limited
    ///   503 zone unavailable (maintenance / closed / not open yet)
    ///   500 internal error
    /// </summary>
    [Serializable]
    public class AssignGateResult
    {
        public const int CodeOk           = 0;
        public const int CodeQueueing     = 100;
        // access token 无效(过期、已吊销)或没带:要重新登录,不是暂时故障。
        public const int CodeAuthRejected = 401;
        public const int CodeZoneNotFound = 404;
        public const int CodeQueueExpired = 410;
        public const int CodeRateLimited  = 429;
        public const int CodeInternal     = 500;
        public const int CodeZoneUnavailable = 503;

        public int code;
        public string gate_ip;
        public uint gate_port;
        // 同一台 gate 给小游戏的 WebSocket 地址;这台 gate 不接小游戏时应答里没有这个字段(为 null)。
        // 小游戏连它,原生包仍连 gate_ip / gate_port。
        public string gate_ws_url;
        // base64-encoded by Jackson when these come from byte[] fields.
        public string token_payload;
        public string token_signature;
        public long token_deadline;
        // queue payload (code == 100)
        public string queue_source;   // "login" (real rank) or "ratelimit" (estimate)
        public string queue_token;
        public long queue_rank;
        public long queue_total;
        public long retry_after_ms;
        public string error;
    }

    [Serializable]
    public class ServerListZone
    {
        public uint zone_id;
        public string name;
        public string status;         // OPEN / MAINTENANCE / CLOSED / PREVIEW
        public string load_level;     // SMOOTH / BUSY / FULL / null
        public string maintenance_msg;
        public long open_time;
        public bool is_new;
        // 服务端按负载算出的推荐(2026-11-10 起):爆满、暂停创角、进不去的区不会被推荐。
        public bool recommended;
        // 本区暂停创建新角色:已有角色照常进,新角色只能建在别的区(服务端建角回 kLoginZoneCreateRoleClosed)。
        public bool create_role_closed;
    }

    [Serializable]
    public class ServerListResponse
    {
        public ServerListZone[] zones;
    }

    [Serializable]
    public class AssignGateRequest
    {
        public uint zone_id;
        // /api/login 签发的 access token:服务端只认它解出的账号(已有在线会话的免排队、排队身份)。
        // 2026-11-12 之前这里是自报的 account,服务端已不再读它。
        public string access_token;
        public string device_id;
        public string queue_token;
    }

    [Serializable]
    public class QueueStatusRequest
    {
        public string queue_token;
        public uint zone_id;
    }

    /// <summary>Request body for POST /api/login (gateway LoginRequest, snake_case).</summary>
    [Serializable]
    public class GatewayLoginRequest
    {
        public uint zone_id;
        public string account;
        public string password;
        public string auth_type;   // LoginAuthTypes 里的取值;"" 等同 "password"
        public string auth_token;
        public string device_id;
    }

    [Serializable]
    public class GatewayPlayerInfo
    {
        public ulong player_id;
        public string name;
        public int level;
        // 这个角色现在所在的区(2026-11-12 起网关下发;合服之后是目标区)。0 = 不知道:老网关没发,或加字段之前的存量角色。
        // 账号的角色列表是全服的,排队之前用它判断「所选区暂停创角、而账号在这个区没有角色」(GameClient.MustCreateRoleInClosedZone)。
        public uint zone_id;
    }

    /// <summary>
    /// Response of POST /api/login. code semantics (gateway LoginResponse):
    /// 0 ok / 100 queueing / 101 queue timeout / 401 auth rejected /
    /// 429 rate-limited / 500 internal.
    /// </summary>
    [Serializable]
    public class GatewayLoginResult
    {
        public const int CodeOk           = 0;
        public const int CodeQueueing     = 100;
        public const int CodeQueueTimeout = 101;
        public const int CodeAuthRejected = 401;
        public const int CodeRateLimited  = 429;
        public const int CodeInternal     = 500;

        public int code;
        public string message;
        public long retry_after_ms;
        public long queue_pos;
        public GatewayPlayerInfo[] players;
        public string access_token;
        public string refresh_token;
        public long access_token_expire;
        public long refresh_token_expire;
        // 账号注销(服务端 docs/design/account-deletion.md §4.3、§4.5;时刻一律 Unix 秒)。
        // 0 正常、1 冷静期(只能撤销,不能进游戏)、2 执行中:不是 0 时 EnterZone 不再分配 gate。
        public uint account_deletion_state;
        public long account_deletion_execute_after;
        // 这次登录建了新账号,而同一身份以前的账号已注销完成:完成时刻;其余 0。只有建新账号的那一次登录会带。
        public long previous_account_deleted_at;
        // 防沉迷状态(服务端 docs/design/real-name-anti-addiction.md §5.3)。部署没开防沉迷时网关不发,JsonUtility 填成全默认值。
        public GatewayAntiAddiction anti_addiction;
    }

    /// <summary>防沉迷状态:/api/login 与 /api/real-name 的应答都带。</summary>
    [Serializable]
    public class GatewayAntiAddiction
    {
        // 本次登录的渠道要游戏方实名,而账号还没实名:先弹实名窗,提交 /api/real-name。
        public bool real_name_required;
        // 已实名且未满 18 周岁。
        public bool minor;
        // 未成年人本次能玩到的时刻(Unix 毫秒,到点服务端踢线);0 = 不限,或现在不在可玩时段。
        public long play_deadline_ms;
    }

    /// <summary>Request body for POST /api/real-name。姓名和身份证号只发这一次,客户端不存。</summary>
    [Serializable]
    public class GatewayRealNameRequest
    {
        public uint zone_id;
        public string access_token;
        public string name;
        public string id_number;
    }

    /// <summary>
    /// Response of POST /api/real-name. code:0 已实名(或本渠道不需要)/ 401 登录已失效 / 422 实名没通过(看 tip)/
    /// 429 操作太频繁 / 500 服务器异常。
    /// </summary>
    [Serializable]
    public class GatewayRealNameResult
    {
        public const int CodeOk           = 0;
        public const int CodeAuthRejected = 401;
        public const int CodeRejected     = 422;
        public const int CodeRateLimited  = 429;

        public int code;
        public string message;
        public uint tip;
        public GatewayAntiAddiction anti_addiction;
    }

    [Serializable]
    public class GatewayRefreshTokenRequest
    {
        public string refresh_token;
    }

    /// <summary>
    /// Request body for POST /api/account/delete(服务端 docs/design/account-deletion.md §5.2)。凭据字段与 /api/login 同义:
    /// 注销要求重新认证,不收 access token。zone_id 只用来选路。
    /// </summary>
    [Serializable]
    public class GatewayAccountDeletionRequest
    {
        public uint zone_id;
        public string auth_type;
        public string auth_token;
        public string account;
        public string password;
        public string authorization_code; // 只有 Apple:这次重新授权拿到的授权码,login 换票后立刻撤销(§8)
    }

    /// <summary>Request body for POST /api/account/delete/cancel(§4.4)。</summary>
    [Serializable]
    public class GatewayCancelAccountDeletionRequest
    {
        public uint zone_id;
        public string access_token; // 冷静期内 /api/login 签发的 access token
    }

    [Serializable]
    public class GatewayAccountDeletionBlocker
    {
        public ulong player_id;
        public string reason; // 低基数枚举串(guild_leader、role_listed、team_membership 等),文案见 GameClient.DescribeAccountDeletionBlocker
    }

    /// <summary>
    /// Response of POST /api/account/delete and /api/account/delete/cancel. code 与 /api/login 同一套
    /// (<see cref="GatewayLoginResult"/> 的 Code* 常量);客户端按 tip 分支,不解析 message(GameClient.ClassifyAccountDeletion)。
    /// </summary>
    [Serializable]
    public class GatewayAccountDeletionResult
    {
        public int code;
        public string message;
        public uint tip;           // login 回的 tip 码,0 = 没有
        public uint state;         // 0 正常 / 1 冷静期 / 2 执行中
        public long execute_after; // 冷静期满的时刻,Unix 秒
        public GatewayAccountDeletionBlocker[] blockers; // 预检没过(tip kLoginAccountDeletionBlocked)时的挂起清单
    }

    [Serializable]
    public class GatewayRefreshTokenResult
    {
        public int code;
        public string message;
        public string access_token;
        public string refresh_token;
        public long access_token_expire;
        public long refresh_token_expire;
    }

    [Serializable]
    public class AnnouncementItem
    {
        public long id;
        public string title;
        public string content;
        public string type;
        public long start_time;
        public long end_time;
    }

    [Serializable]
    public class AnnouncementResponse
    {
        public AnnouncementItem[] items;
    }

    /// <summary>
    /// Talks to the Java gateway HTTP API (port 8081). Drives the whole
    /// pre-gate flow: /api/login -> /api/server-list -> /api/assign-gate
    /// (+ /api/queue-status polling) before the TCP connection to gate.
    /// All controllers reply HTTP 200 with a `code` field in the body, so
    /// callers must branch on the parsed code, not the HTTP status.
    /// POST 接口的 HTTP 非 200 一律交 onError,不读正文(见 PostJson)。
    /// </summary>
    public sealed class GatewayHttpClient
    {
        private readonly string _baseUrl;
        public GatewayHttpClient(string baseUrl) { _baseUrl = baseUrl.TrimEnd('/'); }

        public IEnumerator GetServerList(Action<ServerListResponse> onSuccess, Action<string> onError)
        {
            using var req = UnityWebRequest.Get(_baseUrl + "/api/server-list");
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                onError($"GET /api/server-list failed: {req.error}");
                yield break;
            }
            try
            {
                var parsed = JsonUtility.FromJson<ServerListResponse>(req.downloadHandler.text);
                onSuccess(parsed);
            }
            catch (Exception ex) { onError($"parse server-list: {ex.Message}"); }
        }

        public IEnumerator GetAnnouncements(Action<AnnouncementResponse> onSuccess, Action<string> onError)
        {
            using var req = UnityWebRequest.Get(_baseUrl + "/api/announcement");
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                onError($"GET /api/announcement failed: {req.error}");
                yield break;
            }

            try
            {
                var parsed = JsonUtility.FromJson<AnnouncementResponse>(req.downloadHandler.text);
                onSuccess(parsed);
            }
            catch (Exception ex) { onError($"parse announcement: {ex.Message}"); }
        }

        /// <summary>
        /// HTTP login (the "new path"): verifies credentials and returns the
        /// access/refresh token pair plus the account's player list. onDone
        /// receives every terminal shape — branch on <see cref="GatewayLoginResult.code"/>.
        /// onError 只有网络 / HTTP 失败(含 HTTP 非 200)与解析失败。
        /// </summary>
        public IEnumerator Login(GatewayLoginRequest body,
                                 Action<GatewayLoginResult> onDone, Action<string> onError)
            => PostJson("/api/login", JsonUtility.ToJson(body), onDone, onError);

        /// <summary>实名提交(服务端 docs/design/real-name-anti-addiction.md §5.2)。分支看 <see cref="GatewayRealNameResult.code"/>。</summary>
        public IEnumerator SubmitRealName(GatewayRealNameRequest body,
                                          Action<GatewayRealNameResult> onDone, Action<string> onError)
            => PostJson("/api/real-name", JsonUtility.ToJson(body), onDone, onError);

        /// <summary>
        /// Ask login for a gate. onDone receives every terminal shape,
        /// including queueing (code=100) — branch on <see cref="AssignGateResult.code"/>.
        /// onError 是网络 / HTTP 失败:没收到回包、HTTP 非 200,或正文解析不了。
        /// </summary>
        public IEnumerator AssignGate(AssignGateRequest body,
                                      Action<AssignGateResult> onDone, Action<string> onError)
            => PostJson("/api/assign-gate", JsonUtility.ToJson(body), onDone, onError);

        /// <summary>Poll the login queue with the queue_token from AssignGate. onError 同 <see cref="AssignGate"/>。</summary>
        public IEnumerator QueryQueueStatus(string queueToken, uint zoneId,
                                            Action<AssignGateResult> onDone, Action<string> onError)
            => PostJson("/api/queue-status",
                JsonUtility.ToJson(new QueueStatusRequest { queue_token = queueToken, zone_id = zoneId }),
                onDone, onError);

        /// <summary>One-shot refresh-token rotation (the old refresh token is consumed).</summary>
        public IEnumerator RefreshToken(string refreshToken,
                                        Action<GatewayRefreshTokenResult> onDone, Action<string> onError)
            => PostJson("/api/refresh-token",
                JsonUtility.ToJson(new GatewayRefreshTokenRequest { refresh_token = refreshToken }),
                onDone, onError);

        /// <summary>
        /// 申请注销账号(服务端 docs/design/account-deletion.md §4.2)。onDone 收一切带内结论(按 tip 分支,
        /// 见 GameClient.ClassifyAccountDeletion);onError 只有传输 / 解析失败。
        /// 超时放宽到 15 秒:服务端要先向 Apple 换票并撤销、再逐个角色试探挂起,比登录慢;申请幂等,超时后重试不会重复记。
        /// </summary>
        public IEnumerator DeleteAccount(GatewayAccountDeletionRequest body,
                                         Action<GatewayAccountDeletionResult> onDone, Action<string> onError)
            => PostJson("/api/account/delete", JsonUtility.ToJson(body), onDone, onError, timeoutSeconds: 15);

        /// <summary>撤销注销(§4.4):凭冷静期内 /api/login 签发的 access token。</summary>
        public IEnumerator CancelAccountDeletion(uint zoneId, string accessToken,
                                                 Action<GatewayAccountDeletionResult> onDone, Action<string> onError)
            => PostJson("/api/account/delete/cancel",
                JsonUtility.ToJson(new GatewayCancelAccountDeletionRequest { zone_id = zoneId, access_token = accessToken ?? string.Empty }),
                onDone, onError);

        private IEnumerator PostJson<T>(string path, string json,
                                        Action<T> onDone, Action<string> onError, int timeoutSeconds = 5) where T : class
        {
            using var req = new UnityWebRequest(_baseUrl + path, "POST");
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = timeoutSeconds;

            yield return req.SendWebRequest();

            // 两版网关的业务结果恒在 HTTP 200 的正文里(只有 hotfix-check 回 400,它不走这里)。非 200 是代理或框架的错误页,
            // 不读正文的 code:Spring 缺省错误页没有 code 字段,会被读成 0「成功 / 放行」;代理的 503 也不是区服维护。
            // 一律按网络 / HTTP 失败交 onError(服务端 open-server-rate-limit-design.md §11.5 第 3、5 条)。
            if (req.result != UnityWebRequest.Result.Success || req.responseCode != 200)
            {
                onError($"POST {path} failed: {(string.IsNullOrEmpty(req.error) ? "HTTP " + req.responseCode : req.error)}");
                yield break;
            }

            T parsed;
            try { parsed = JsonUtility.FromJson<T>(req.downloadHandler.text); }
            catch (Exception ex) { onError($"parse {path}: {ex.Message}"); yield break; }
            if (parsed == null) { onError($"{path}: empty response"); yield break; }
            onDone(parsed);
        }
    }
}
