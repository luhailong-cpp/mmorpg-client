using System;
using UnityEngine;

namespace MmorpgClient.Core
{
    /// <summary>
    /// 构建时写进安装包的服务地址:Assets/Resources/ClientEndpoints.json(服务端 docs/design/official-passport-registration.md §3.8)。
    ///
    /// 正式包只认这里:启动参数与本机设置改不了网关和通行证地址,免得一个带参数的快捷方式把正版客户端带到钓鱼站
    /// (把通行证口令输给假页面,或把口令登录发给假网关)。编辑器与开发包照旧可以用启动参数、本机设置覆盖(ClientSettings)。
    /// 仓库里放的是本机联调的地址;出正式包时 tools/build_client.ps1 用 -GatewayUrl / -PassportOrigin 写进这份文件,
    /// 构建检查(Assets/Editor/ClientEndpointsBuildGuard.cs)不让正式包带着回环地址或明文 http 出去。
    /// </summary>
    public static class ClientEndpoints
    {
        /// <summary>Resources.Load 用的路径(不带扩展名)。</summary>
        public const string ResourcePath = "ClientEndpoints";

        [Serializable]
        public sealed class Values
        {
            /// <summary>网关 HTTP 根地址,如 https://gateway.example.com。</summary>
            public string gatewayBaseUrl = "";
            /// <summary>官方通行证的地址,如 https://passport.example.com;空 = 这个包不提供官方通行证登录。</summary>
            public string passportOrigin = "";
            /// <summary>《用户协议》与《隐私政策》的地址(首次启动的同意弹窗、通行证注册页都链接它们,§3.9);正式包必填。</summary>
            public string termsUrl = "";
            public string privacyUrl = "";
        }

        private static Values _current;

        /// <summary>本安装包的地址(第一次用时读一次)。</summary>
        public static Values Current => _current ??= Load();

        private static Values Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            return asset == null ? new Values() : Parse(asset.text);
        }

        /// <summary>解析 ClientEndpoints.json;认不出时回空值(正式包的构建检查会拦住空的网关地址)。</summary>
        public static Values Parse(string json)
        {
            Values parsed = null;
            try
            {
                parsed = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<Values>(json);
            }
            catch (ArgumentException)
            {
            }
            parsed ??= new Values();
            parsed.gatewayBaseUrl = (parsed.gatewayBaseUrl ?? string.Empty).Trim().TrimEnd('/');
            parsed.passportOrigin = (parsed.passportOrigin ?? string.Empty).Trim().TrimEnd('/');
            parsed.termsUrl = (parsed.termsUrl ?? string.Empty).Trim();
            parsed.privacyUrl = (parsed.privacyUrl ?? string.Empty).Trim();
            return parsed;
        }

        /// <summary>
        /// 正式包对地址的要求:网关、《用户协议》《隐私政策》必填(没有协议地址过不了商店审核),通行证可空;
        /// 填了的都必须是 https、主机不是回环地址,不带用户信息与片段;网关与通行证还不能带查询串。
        /// </summary>
        public static bool TryValidateForRelease(Values values, out string reason)
        {
            if (values == null)
            {
                reason = "endpoints are missing";
                return false;
            }
            if (!IsReleaseOrigin(values.gatewayBaseUrl, out reason))
            {
                reason = "gatewayBaseUrl " + reason;
                return false;
            }
            if (values.passportOrigin.Length != 0 && !IsReleaseOrigin(values.passportOrigin, out reason))
            {
                reason = "passportOrigin " + reason;
                return false;
            }
            if (!IsReleaseDocument(values.termsUrl, out reason))
            {
                reason = "termsUrl " + reason;
                return false;
            }
            if (!IsReleaseDocument(values.privacyUrl, out reason))
            {
                reason = "privacyUrl " + reason;
                return false;
            }
            reason = string.Empty;
            return true;
        }

        /// <summary>协议页面:https、不是本机,可以带路径与查询串。</summary>
        private static bool IsReleaseDocument(string value, out string reason)
        {
            if (string.IsNullOrEmpty(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                reason = "must be an absolute URL";
                return false;
            }
            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) || uri.IsLoopback ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            {
                reason = "must be an https page that is not on this machine";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private static bool IsReleaseOrigin(string value, out string reason)
        {
            if (string.IsNullOrEmpty(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                reason = "must be an absolute URL";
                return false;
            }
            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                reason = "must use https";
                return false;
            }
            if (uri.IsLoopback || string.IsNullOrEmpty(uri.Host))
            {
                reason = "must not point at this machine";
                return false;
            }
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                reason = "must not contain userinfo, query, or fragment";
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }
}
