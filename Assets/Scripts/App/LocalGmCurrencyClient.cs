#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Net;
using MmorpgClient.Game.Pet;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.App
{
    /// <summary>本地开发包的加银两入口；正式包不编译本类，权限仍由服务器裁决。</summary>
    public sealed class LocalGmCurrencyClient
    {
        public readonly struct Session
        {
            public readonly IRpcChannel Channel;
            public readonly object Connection;
            public readonly ulong PlayerId;
            public readonly bool InGame, Ready;
            public readonly string Gateway, Gate;
            public Session(IRpcChannel channel, object connection, ulong playerId, bool inGame, bool ready, string gateway, string gate)
            { Channel = channel; Connection = connection; PlayerId = playerId; InGame = inGame; Ready = ready; Gateway = gateway; Gate = gate; }
            public bool Allowed => Channel != null && Connection != null && PlayerId != 0 && InGame && Ready && MatchesEndpoints(Gateway, Gate);
            public bool IsSame(Session other) => ReferenceEquals(Channel, other.Channel) && ReferenceEquals(Connection, other.Connection) && PlayerId == other.PlayerId;
        }

        private readonly Func<Session> _session;
        private readonly Action<Session> _refresh;
        private Session _displaySession;
        public bool Pending { get; private set; }
        public bool OutcomeUnknown { get; private set; }
        public bool Warning { get; private set; }
        public string Notice { get; private set; } = "本地测试：每次点击增加 10 万银两";
        public bool CanSend => !Pending && _session().Allowed;
        public event Action Changed;

        public LocalGmCurrencyClient(Func<Session> session, Action<Session> refresh)
        { _session = session ?? throw new ArgumentNullException(nameof(session)); _refresh = refresh; _displaySession = session(); }

        public void SynchronizeSession()
        {
            var current = _session();
            if (_displaySession.IsSame(current)) return;
            _displaySession = current;
            if (!Pending) SetNotice("本地测试：每次点击增加 10 万银两", false, false);
        }

        public static bool MatchesEndpoints(string gateway, string gate)
        {
            return Uri.TryCreate(gateway, UriKind.Absolute, out var http) &&
                (http.Scheme == Uri.UriSchemeHttp || http.Scheme == Uri.UriSchemeHttps) && LocalEndpoint(http) &&
                Uri.TryCreate("tcp://" + gate, UriKind.Absolute, out var tcp) && tcp.Scheme == "tcp" && LocalEndpoint(tcp);
        }

        private static bool LocalEndpoint(Uri uri)
            => uri.Port > 0 && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
                string.IsNullOrEmpty(uri.Fragment) && (uri.AbsolutePath == "/" || uri.AbsolutePath.Length == 0) &&
                (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                 (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)));

        public void AddSilver()
        {
            // 每次真实发包前重新核验，隐藏按钮不是授权边界。
            if (Pending) return;
            var requestSession = _session();
            if (!requestSession.Allowed) { SetNotice("仅允许已进入本地测试服的角色使用。", true, false); return; }
            Pending = true;
            SetNotice("正在等待服务器确认，请勿重复提交…", false, false);
            try
            {
                requestSession.Channel.CallMethod(SceneCurrencyClientPlayerRpc.GmAddCurrencyMessageId,
                    new GmAddCurrencyRequest { CurrencyType = 0, Amount = 100000 }, GmAddCurrencyResponse.Parser,
                    (status, response) =>
                    {
                        Pending = false;
                        var current = _session();
                        if (!requestSession.IsSame(current) || !current.Allowed)
                        { SetNotice("角色或连接已变化，请查看原角色余额；不会自动重试。", true, true); return; }
                        if (!status.IsOk)
                        {
                            bool unknown = status.OutcomeUnknown || status.Code == RpcStatusCode.ParseError ||
                                (status.Code == RpcStatusCode.ServerTip && TipFaults.IsFault(status.TipId));
                            string detail = status.Code == RpcStatusCode.ServerTip
                                ? PetClient.DescribeTip("服务器返回", new TipInfoMessage { Id = status.TipId })
                                : status.Code == RpcStatusCode.NotSent ? "请求未发出" : "通信异常";
                            SetNotice((unknown ? "结果未知，请先核对背包余额。" : "未增加银两。") + detail + "；不会自动重试。", true, unknown);
                            return;
                        }
                        if (response == null)
                        { SetNotice("结果未知：服务器响应为空，请先核对余额；不会自动重试。", true, true); return; }
                        if (response.ErrorMessage?.Id > 0)
                        {
                            bool unknown = TipFaults.IsFault(response.ErrorMessage.Id);
                            SetNotice(PetClient.DescribeTip(unknown ? "结果未知，请核对余额" : "增加银两失败", response.ErrorMessage), true, unknown);
                            return;
                        }
                        _refresh?.Invoke(requestSession);
                        SetNotice($"已增加 100,000 银两；服务器余额 {response.BalanceAfter:N0}", false, false);
                    });
            }
            catch (Exception)
            {
                Pending = false;
                SetNotice("结果未知：请求异常，请先核对余额；不会自动重试。", true, true);
            }
        }

        private void SetNotice(string text, bool warning, bool unknown)
        { Notice = text; Warning = warning; OutcomeUnknown = unknown; Changed?.Invoke(); }
    }
}
#endif
