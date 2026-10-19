namespace Teampb
{
    /// <summary>
    /// 组队补位状态的业务别名；字段和序列化统一由正式 team.proto 生成。
    /// 恢复字段与补位字段已分配独立编号，不再通过未知字段声明协议成员。
    /// </summary>
    public static class TeamBackfillProtocol
    {
        public const TeamMatchState QueuedState = TeamMatchState.Queued;
        public const TeamChangeReason MatchQueuedReason = TeamChangeReason.MatchQueued;
        public const TeamChangeReason MatchQueueCanceledReason = TeamChangeReason.MatchQueueCanceled;
    }
}
