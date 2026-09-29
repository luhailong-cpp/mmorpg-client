using System;
using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// <see cref="IBattleChannel"/> 假实现:测试手动设定就绪的战斗(<see cref="ReadyBattleId"/>),
    /// 用 <see cref="RaiseReady"/> / <see cref="RaiseLost"/> 模拟直连握手成功与终结,
    /// 并记录 Retry / EnsureBattle / Abandon 调用。语义贴着 DirectRoutingBattleTransport:
    /// RaiseReady 会同时把该局置为就绪,RaiseLost 会同时清掉就绪(与链路 Verified / Closed 同步)。
    /// </summary>
    internal sealed class FakeBattleChannel : IBattleChannel
    {
        /// <summary>当前就绪的战斗;0 = 未就绪。</summary>
        public ulong ReadyBattleId;

        public readonly List<ulong> Retries = new();
        public readonly List<ulong> Ensures = new();
        public readonly List<ulong> Abandons = new();

        public event Action<ulong, eBattleTicketRole> Ready;
        public event Action<ulong, BattleLinkCloseKind, string> Lost;

        public bool IsReadyFor(ulong battleId) => battleId != 0 && battleId == ReadyBattleId;

        public void Retry(ulong battleId) => Retries.Add(battleId);

        public void EnsureBattle(ulong battleId) => Ensures.Add(battleId);

        public void Abandon(ulong battleId) => Abandons.Add(battleId);

        /// <summary>模拟直连握手通过:先置就绪,再抛事件(与链路先置 Verified 再抛 OnVerified 同序)。</summary>
        public void RaiseReady(ulong battleId,
                               eBattleTicketRole role = eBattleTicketRole.BattleTicketRoleParticipant)
        {
            ReadyBattleId = battleId;
            Ready?.Invoke(battleId, role);
        }

        /// <summary>模拟直连终结:先清就绪,再抛事件(与链路先置 Closed 再抛 OnClosed 同序)。</summary>
        public void RaiseLost(ulong battleId, BattleLinkCloseKind kind, string detail = "test")
        {
            if (ReadyBattleId == battleId) ReadyBattleId = 0;
            Lost?.Invoke(battleId, kind, detail);
        }
    }
}
