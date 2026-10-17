using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using MmorpgClient.Game.Battle;

namespace MmorpgClient.Net
{
    /// <summary>入口与玩家分别隔离；原 Join 以真实原子文件提交，撤销只做原记录的 CAS 状态转换。</summary>
    public sealed class FileMatchJoinRecovery : IMatchJoinRecovery
    {
        private readonly DurableRequestFileStore _files;
        private readonly Dictionary<ulong, string> _owned = new();
        public FileMatchJoinRecovery(string directory, string gatewayBaseUrl)
        {
            _files = DurableRequestFileStore.ForOrigin(directory,gatewayBaseUrl);
        }
        public Match.JoinQueueRequest Read(ulong playerId, out bool cancellationRequested)
        {
            string value = _files.Load(playerId); cancellationRequested = false;
            if (value.Length == 0) return null;
            if (value.Length > 8192 || value.Length < 3 || value[1] != ':' || (value[0] != 'J' && value[0] != 'C')) throw new InvalidDataException("匹配恢复记录损坏或无法读取");
            var request = Match.JoinQueueRequest.Parser.ParseFrom(Convert.FromBase64String(value.Substring(2)));
            if (request.PlayerId != playerId || !Guid.TryParseExact(request.RequestId, "D", out _)) throw new InvalidDataException("匹配恢复身份无效");
            cancellationRequested = value[0] == 'C'; _owned[playerId] = value;
            return request;
        }
        public void Save(Match.JoinQueueRequest request, bool cancellationRequested)
        {
            if (request.PlayerId == 0 || !Guid.TryParseExact(request.RequestId, "D", out _)) throw new ArgumentException("匹配恢复请求无效");
            string body = Convert.ToBase64String(request.ToByteArray());
            string wanted = (cancellationRequested ? "C:" : "J:") + body;
            string before = _files.Load(request.PlayerId);
            if (cancellationRequested && before == "J:" + body) _files.Transition(request.PlayerId, before, wanted);
            else _files.Save(request.PlayerId, wanted);
            _owned[request.PlayerId] = wanted;
        }
        public void Clear(ulong playerId)
        {
            if (!_owned.TryGetValue(playerId, out var original)) return;
            _files.Clear(playerId, original); _owned.Remove(playerId);
        }
    }
}
