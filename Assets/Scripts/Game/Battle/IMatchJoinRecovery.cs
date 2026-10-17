namespace MmorpgClient.Game.Battle
{
    /// <summary>保存已经发送、结局尚未确认的匹配意图；写失败必须阻止发包。</summary>
    public interface IMatchJoinRecovery
    {
        Match.JoinQueueRequest Read(ulong playerId, out bool cancellationRequested);
        void Save(Match.JoinQueueRequest request, bool cancellationRequested);
        void Clear(ulong playerId);
    }
}
