using System;
using System.Text;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Guild
{
    /// <summary>帮会权威快照。每次请求绑定角色与会话代次，旧回包不能污染重连或换角。</summary>
    public sealed class GuildClient : IDisposable
    {
        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private object _observedConnection;
        private int _generation;
        private bool _disposed;
        public ulong PlayerId => _net.PlayerId;
        public GuildInfo Info { get; private set; }
        public GetGuildRankResponse Rank { get; private set; }
        public bool HasLoaded { get; private set; }
        public bool Busy { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public const string RecoveryMessage = "帮会请求状态尚未确认，请重新登录角色后再试。";
        // 与服务端 go/guild/internal/constants 同值；服务端仍会自行校验。
        public const int MaxNameLength = 24;
        // 公告按 UTF-8 字节限长：Gate 单包上限 1KB，按字数给到 500 个汉字的包会在 Gate 被丢弃。
        public const int MaxAnnouncementBytes = 600;
        public const int MaxAnnouncementChars = 200;
        public string Status { get; private set; } = "请刷新帮会信息";
        public bool IsLeader => Info != null && Info.LeaderId == PlayerId;
        public uint Role
        {
            get
            {
                if (Info != null)
                    foreach (var member in Info.Members)
                        if (member.PlayerId == PlayerId) return member.Role;
                return 0;
            }
        }
        // 对齐服务端 UpdateAnnouncementAuthorized：长老(1)和帮主(3)，不放行未实现的副帮主(2)。
        public bool CanEditAnnouncement => Info != null && (Role == 1 || Role == 3);
        public event Action Changed;

        public GuildClient(IBattleTransport net, Func<object> connectionIdentity = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _observedConnection = _connectionIdentity();
            _net.Disconnected += HandleDisconnected;
        }

        public void Reset()
        {
            // 零编号 gRPC 回包无法相关到已取消请求；同连接不得重用其槽位。
            if (Busy) RequiresReconnect = true;
            ++_generation;
            Info = null; Rank = null; HasLoaded = false; Busy = false;
            Status = RequiresReconnect ? RecoveryMessage : "请刷新帮会信息"; Changed?.Invoke();
        }

        public void ObserveConnection()
        {
            object current = _connectionIdentity();
            if (ReferenceEquals(current, _observedConnection)) return;
            // GameClient 静默换 Gate 不一定发 Disconnected；真实连接对象变化才解除隔离。
            _observedConnection = current;
            Busy = false;
            RequiresReconnect = false;
            Reset();
        }
        private void HandleDisconnected()
        {
            // 仅真实连接断开时，旧连接回包才不可能再进入下一连接。
            _observedConnection = _connectionIdentity();
            Busy = false;
            RequiresReconnect = false;
            Reset();
        }
        public void Refresh()
        {
            Request(MessageIds.GetPlayerGuild, new GetPlayerGuildRequest { PlayerId = PlayerId },
                GetPlayerGuildResponse.Parser, response =>
                {
                    if (response.ErrorMessage?.Id == (uint)guild_error.KGuildNotInGuild)
                    { Info = null; HasLoaded = true; Status = "尚未加入帮会，和同道相聚于此。"; return; }
                    if (!Accept(response.ErrorMessage)) return;
                    Apply(response.Guild);
                });
        }

        public void Browse(uint page = 1, uint zoneId = 0)
        {
            // 服务端只返回玩家归属区的榜单，zoneId 仅作请求提示。
            Request(MessageIds.GetGuildRank, new GetGuildRankRequest
                { Page = Math.Max(1, page), PageSize = 5, ZoneId = zoneId },
                GetGuildRankResponse.Parser, response =>
                {
                    if (!Accept(response.ErrorMessage)) return;
                    Rank = response; Status = response.Entries.Count == 0 ? "本区暂无帮会排行。" : "本区帮会排行已更新";
                });
        }

        public void Create(string name, uint zoneId)
        {
            name = (name ?? "").Trim();
            if (!CanJoin() || name.Length == 0 || name.Length > MaxNameLength || zoneId == 0)
            { Reject("请先确认未入帮状态，填写 1–24 字帮名并选择有效区服。"); return; }
            Request(MessageIds.CreateGuild, new CreateGuildRequest { PlayerId = PlayerId, Name = name, ZoneId = zoneId },
                CreateGuildResponse.Parser, response =>
                { if (Accept(response.ErrorMessage)) Apply(response.Guild); });
        }

        public void Join(ulong guildId)
        {
            if (!CanJoin() || guildId == 0) { Reject("请先读取帮会状态，已入帮时不能再加入。"); return; }
            // 服务端(帮会二期)已把「直接加入」rpc JoinGuild 删掉,换成申请制 ApplyJoinGuild:请求只带
            // guild_id,身份取自会话。旧的 JoinGuild 消息号(19)已被服务端改判给别的 RPC,继续用它会打到
            // 错的接口。这里只做最小适配(成功后照旧 Refresh,入帮要等对方审批);「申请已提交」的文案、
            // 我的申请列表 / 撤回等 UI 属于帮会二期的客户端工作。
            Request(MessageIds.ApplyJoinGuild, new ApplyJoinGuildRequest { GuildId = guildId },
                ApplyJoinGuildResponse.Parser, response =>
                { if (Accept(response.ErrorMessage)) Refresh(); });
        }

        public void SaveAnnouncement(string text)
        {
            text = (text ?? "").Trim();
            if (Info == null || !CanEditAnnouncement) { Reject("仅帮主或长老可修改公告。"); return; }
            if (text.Length > MaxAnnouncementChars || Encoding.UTF8.GetByteCount(text) > MaxAnnouncementBytes)
            { Reject("公告最多 " + MaxAnnouncementChars + " 字，请精简后再保存。"); return; }
            Request(MessageIds.SetGuildAnnouncement, new SetAnnouncementRequest
                { GuildId = Info.GuildId, PlayerId = PlayerId, Announcement = text },
                SetAnnouncementResponse.Parser, response =>
                { if (Accept(response.ErrorMessage)) Refresh(); });
        }

        public void Leave()
        {
            if (Info == null || IsLeader) { Reject("帮主需先处理帮会，不能直接退出。"); return; }
            Request(MessageIds.LeaveGuild, new LeaveGuildRequest { PlayerId = PlayerId },
                LeaveGuildResponse.Parser, response =>
                { if (Accept(response.ErrorMessage)) Refresh(); });
        }

        public void Disband()
        {
            if (Info == null || !IsLeader) { Reject("仅帮主可解散帮会。"); return; }
            Request(MessageIds.DisbandGuild, new DisbandGuildRequest { PlayerId = PlayerId },
                DisbandGuildResponse.Parser, response =>
                { if (Accept(response.ErrorMessage)) Refresh(); });
        }

        private bool CanJoin() => HasLoaded && Info == null && !Busy;
        private void Apply(GuildInfo info)
        {
            if (info == null || info.GuildId == 0)
            { Status = "服务器未返回有效帮会信息，请刷新重试。"; return; }
            bool mine = false;
            foreach (var member in info.Members) if (member.PlayerId == PlayerId) mine = true;
            if (!mine) { Status = "帮会成员身份尚未确认，请刷新重试。"; return; }
            Info = info.Clone(); HasLoaded = true; Status = "帮会信息已更新";
        }
        private bool Accept(TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return true;
            Status = tip.Id switch
            {
                (uint)guild_error.KGuildAlreadyInGuild => "你已加入帮会，请刷新查看。",
                (uint)guild_error.KGuildNotFound => "帮会已不存在，请刷新列表。",
                (uint)guild_error.KGuildNotInGuild => "你尚未加入帮会。",
                (uint)guild_error.KGuildFull => "帮会成员已满，请选择其他帮会。",
                (uint)guild_error.KGuildLeaderCantLeave => "帮主不能直接退出帮会。",
                (uint)guild_error.KGuildNotLeader => "此操作仅帮主可用。",
                (uint)guild_error.KGuildNoPermission => "当前身份无权进行此操作。",
                (uint)guild_error.KGuildNotRanked => "此帮会暂未上榜。",
                (uint)guild_error.KGuildNameInvalid => "帮会名称需为 1–24 个字，且不能包含换行等特殊字符。",
                (uint)guild_error.KGuildNameTaken => "该帮会名称已被使用，请换一个。",
                (uint)guild_error.KGuildAnnouncementTooLong => "公告过长，请精简后再保存。",
                (uint)guild_error.KGuildHomeZoneUnknown => "角色所属区服尚未确认，暂时无法使用帮会，请联系管理员。",
                _ => $"帮会服务暂未完成请求（{tip.Id}），请稍后重试。"
            };
            return false;
        }
        private void Reject(string message) { if (Busy) return; Status = message; Changed?.Invoke(); }

        private void Request<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success)
            where T : IMessage<T>
        {
            if (_disposed) return;
            ObserveConnection();
            if (Busy) return;
            if (RequiresReconnect) { Reject(RecoveryMessage); return; }
            if (!_net.IsReady || PlayerId == 0) { Reject("请进入角色后再打开帮会。"); return; }
            int generation = ++_generation;
            ulong player = PlayerId;
            Busy = true; Status = "正在读取帮会，请稍候…"; Changed?.Invoke();
            _net.Call(id, request, parser, response =>
            {
                if (!Current(generation, player)) return;
                Busy = false; success(response); Changed?.Invoke();
            }, error =>
            {
                if (!Current(generation, player)) return;
                Busy = false;
                // GameClient 的 gRPC FIFO 无法区分超时旧响应，必须等真实断线后再发帮会请求。
                RequiresReconnect = true;
                Status = RecoveryMessage;
                Changed?.Invoke();
            });
        }
        private bool Current(int generation, ulong player) =>
            !_disposed && generation == _generation && _net.IsReady && PlayerId == player
            && ReferenceEquals(_observedConnection, _connectionIdentity());
        public void Dispose()
        {
            _net.Disconnected -= HandleDisconnected;
            _disposed = true; ++_generation; Busy = false;
        }
    }
}
