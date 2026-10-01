using System;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Battle
{
    /// <summary>
    /// 战斗直连通道的就绪状态与恢复入口(turn-based §22 D74)。
    ///
    /// 收缩后直连是战斗的唯一通路:四条战斗 RPC 只能在直连就绪时发出,开局到握手之间、
    /// 断线恢复期间的战斗帧不会经大厅回落(D68)。BattleClient / SpectateClient 据此
    /// 推迟请求、在就绪时补拉状态、在终结时收敛相位。
    ///
    /// 刻意不并进 <see cref="IBattleTransport"/>(接口隔离):那个接口有 8 个实现,
    /// 属性 / 宝宝 / 帮会 / 社交 / 聚宝斋等非战斗模块与测试替身都不该为直连状态改动。
    /// 生产实现是 <see cref="DirectRoutingBattleTransport"/>;BattleClient / SpectateClient
    /// 把它作为可选构造参数,传 null 视为「永远就绪」(演出台与既有测试保持原语义)。
    ///
    /// 线程模型:全部成员与事件都在主线程(由 GameClient.Tick 驱动)。
    /// </summary>
    public interface IBattleChannel
    {
        /// <summary>直连已为该局验证通过,可以发战斗 RPC。battleId 为 0 恒为 false。</summary>
        bool IsReadyFor(ulong battleId);

        /// <summary>
        /// 直连握手通过(参数:battle_id、票据角色)。同一局断线重连成功会再次触发;
        /// 订阅方据此补拉状态、补发挂起的请求。观众直连就绪时服务端会随即推一份
        /// SpectateStateS2C 快照(D69),参战者则由客户端 GetBattleState 补拉。
        /// </summary>
        event Action<ulong, eBattleTicketRole> Ready;

        /// <summary>
        /// 直连终结(参数:终结时服务的 battle_id、结构化原因、诊断串)。
        /// <see cref="BattleLinkCloseKind.BattleGone"/> = 战斗已结束(终局包可能丢了),应收敛回空闲;
        /// <see cref="BattleLinkCloseKind.Unreachable"/> = 本局连不上 battle 节点,应提示并允许手动重连;
        /// <see cref="BattleLinkCloseKind.Superseded"/> = 通道改服务另一局,该局从此收不到战斗帧
        /// (观战方应退出该局观战;它不是战斗已结束的权威信号,参战方不据此收场);
        /// Ended / HostClosed 为正常收尾或宿主关闭,订阅方通常无需处理。
        /// </summary>
        event Action<ulong, BattleLinkCloseKind, string> Lost;

        /// <summary>手动重连:重置恢复预算并立即补签。该局已在连接 / 已验证 / 补签在途时无事。</summary>
        void Retry(ulong battleId);

        /// <summary>开局自愈:本通道不服务该局(分配包丢失)或已终结时补签;同局恢复中不打扰。</summary>
        void EnsureBattle(ulong battleId);

        /// <summary>
        /// 放弃该局直连(未就绪时退出观战用):本地终结、不再重连,以 Ended 收尾。
        /// 不是本通道服务的局时无事。
        /// </summary>
        void Abandon(ulong battleId);
    }
}
