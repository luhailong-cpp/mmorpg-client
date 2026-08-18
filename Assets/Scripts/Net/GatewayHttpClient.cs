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
        public const int CodeZoneNotFound = 404;
        public const int CodeQueueExpired = 410;
        public const int CodeRateLimited  = 429;
        public const int CodeInternal     = 500;
        public const int CodeZoneUnavailable = 503;

        public int code;
        public string gate_ip;
        public uint gate_port;
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
        public bool recommended;
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
        public string account;
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
        public string auth_type;   // "" / "password" / "wechat" / "qq" / "satoken" / "access_token"
        public string auth_token;
        public string device_id;
    }

    [Serializable]
    public class GatewayPlayerInfo
    {
        public ulong player_id;
        public string name;
        public int level;
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
    }

    [Serializable]
    public class GatewayRefreshTokenRequest
    {
        public string refresh_token;
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
        /// onError fires only for transport / parse failures.
        /// </summary>
        public IEnumerator Login(GatewayLoginRequest body,
                                 Action<GatewayLoginResult> onDone, Action<string> onError)
            => PostJson("/api/login", JsonUtility.ToJson(body), onDone, onError);

        /// <summary>
        /// Ask login for a gate. onDone receives every terminal shape,
        /// including queueing (code=100) — branch on <see cref="AssignGateResult.code"/>.
        /// </summary>
        public IEnumerator AssignGate(AssignGateRequest body,
                                      Action<AssignGateResult> onDone, Action<string> onError)
            => PostJson("/api/assign-gate", JsonUtility.ToJson(body), onDone, onError);

        /// <summary>Poll the login queue with the queue_token from AssignGate.</summary>
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

        private IEnumerator PostJson<T>(string path, string json,
                                        Action<T> onDone, Action<string> onError) where T : class
        {
            using var req = new UnityWebRequest(_baseUrl + path, "POST");
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 5;

            yield return req.SendWebRequest();

            // Spring error handlers may reply 4xx/5xx with a JSON body that
            // still carries `code`; prefer the parsed body when present.
            string text = req.downloadHandler?.text;
            if (req.result != UnityWebRequest.Result.Success && string.IsNullOrEmpty(text))
            {
                onError($"POST {path} failed: {req.error}");
                yield break;
            }

            T parsed;
            try { parsed = JsonUtility.FromJson<T>(text); }
            catch (Exception ex) { onError($"parse {path}: {ex.Message}"); yield break; }
            if (parsed == null) { onError($"{path}: empty response"); yield break; }
            onDone(parsed);
        }
    }
}
