using System;
using System.Collections.Generic;
using UnityEngine;
using MmorpgClient.Game.Battle;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// HUD 的纯逻辑(行动预告序 / 命令环排布 / 头像分派 / 队友卡片排序),不建对象,EditMode 直接测。
    /// </summary>
    public static class BattleHudLogic
    {
        /// <summary>命令环按钮数(攻击/法术/防御/道具/召唤/逃跑/自动)。</summary>
        public const int CommandCount = 7;

        /// <summary>
        /// 行动预告序:优先服务端 TurnResultS2C.action_order(D3),过滤掉不在 actors 里的 id;
        /// 为空(旧服务端)时按 speed 降序本地排序(同速按 actor_id 升序),跳过已死亡/已逃离。
        /// </summary>
        public static List<ulong> ResolveActionOrder(IEnumerable<BattleActorState> actors, IReadOnlyList<ulong> serverOrder)
        {
            var result = new List<ulong>();
            var known = new Dictionary<ulong, BattleActorState>();
            if (actors != null)
            {
                foreach (var actor in actors)
                {
                    if (actor != null) known[actor.ActorId] = actor;
                }
            }

            if (serverOrder != null && serverOrder.Count > 0)
            {
                for (int i = 0; i < serverOrder.Count; i++)
                {
                    ulong id = serverOrder[i];
                    if (known.Count == 0 || known.ContainsKey(id)) result.Add(id);
                }
                if (result.Count > 0) return result;
            }

            var alive = new List<BattleActorState>();
            foreach (var actor in known.Values)
            {
                if (actor.IsDead || actor.Fled) continue;
                alive.Add(actor);
            }
            alive.Sort((a, b) =>
            {
                ulong sa = SpeedOf(a), sb = SpeedOf(b);
                if (sa != sb) return sb.CompareTo(sa);
                return a.ActorId.CompareTo(b.ActorId);
            });
            foreach (var actor in alive) result.Add(actor.ActorId);
            return result;
        }

        public static ulong SpeedOf(BattleActorState actor) => actor?.Attributes?.Speed ?? 0UL;

        /// <summary>
        /// 命令环第 index 个按钮的中心(相对环心,设计坐标 y 向下):从正上方起顺时针均分。
        /// </summary>
        public static Vector2 RingPosition(int index, int count, float radius)
        {
            if (count <= 0) return Vector2.zero;
            float angle = -Mathf.PI * 0.5f + Mathf.PI * 2f * index / count; // 屏幕坐标 y 向下:-90° 为正上
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        /// <summary>玩家头像索引(22 张 qdao_v3 立绘按 actor_id 稳定分派)。</summary>
        public static int PortraitIndexFor(ulong actorId, int portraitCount)
        {
            if (portraitCount <= 0) return 0;
            ulong h = actorId * 11400714819323198485UL + 0x9E3779B97F4A7C15UL;
            return (int)((h >> 20) % (ulong)portraitCount);
        }

        /// <summary>
        /// 右上角色卡顺序:自己第一,其后同队队友按 actor_id 升序,最多 maxCards 张;
        /// 观战(myId=0)时取下排队伍(myTeam)的前几位。
        /// </summary>
        public static List<BattleActorState> PartyCardOrder(IEnumerable<BattleActorState> actors, ulong myId, uint myTeam, int maxCards)
        {
            var result = new List<BattleActorState>();
            if (actors == null || maxCards <= 0) return result;
            BattleActorState self = null;
            var mates = new List<BattleActorState>();
            foreach (var actor in actors)
            {
                if (actor == null || actor.TeamIndex != myTeam) continue;
                // 宝宝不占角色卡:它跟着主人站(BattleStage.PetOwnerOf),不是一名队友。
                // 不跳过的话带宝宝进 4 人队,会有一个人类队友被自己的宝宝挤掉。
                if (BattleStage.PetOwnerOf(actor) != 0UL) continue;
                if (myId != 0 && actor.ActorId == myId) self = actor;
                else mates.Add(actor);
            }
            mates.Sort((a, b) => a.ActorId.CompareTo(b.ActorId));
            if (self != null) result.Add(self);
            foreach (var mate in mates)
            {
                if (result.Count >= maxCards) break;
                result.Add(mate);
            }
            return result;
        }

        /// <summary>PVP 判定:敌方含玩家即 PVP(一期规则:PVP 不可逃跑)。</summary>
        public static bool IsPvp(IEnumerable<BattleActorState> actors, uint myTeam)
        {
            if (actors == null) return false;
            foreach (var actor in actors)
            {
                if (actor == null || actor.TeamIndex == myTeam) continue;
                if (actor.ActorType != eBattleActorType.BattleActorTypeMonster) return true;
            }
            return false;
        }

        /// <summary>
        /// 直连未就绪多久之后才亮「正在连接」横幅(秒)。开局时分配包与开局包几乎同时到,
        /// 直连通常在一秒内握手完成;宽限期内只靠开局 toast 与底部提示,免得每局开场横幅闪一下。
        /// </summary>
        public const float ChannelConnectingBannerDelaySeconds = 1.5f;

        /// <summary>
        /// 战斗直连提示横幅的显示档位(turn-based §22 D74,直连是战斗唯一通路):
        /// 不在回合内(未开局 / 已收尾 / 观战)→ 不显示;直连就绪 → 不显示;
        /// 已判定连不上(恢复预算用完)→ 失败横幅 + 重新连接;其余未就绪超过宽限期 → 连接中横幅。
        /// </summary>
        /// <param name="inBattle">本人参战且相位为 WaitingAction / Resolving。</param>
        /// <param name="ready">BattleClient.IsBattleChannelReady。</param>
        /// <param name="failed">BattleClient.IsBattleChannelFailed。</param>
        /// <param name="notReadySeconds">本次连续未就绪已持续的秒数(就绪时传 0)。</param>
        public static BattleChannelBanner DecideChannelBanner(bool inBattle, bool ready, bool failed, float notReadySeconds)
        {
            if (!inBattle || ready) return BattleChannelBanner.Hidden;
            if (failed) return BattleChannelBanner.Failed;
            return notReadySeconds >= ChannelConnectingBannerDelaySeconds
                ? BattleChannelBanner.Connecting
                : BattleChannelBanner.Hidden;
        }

        /// <summary>
        /// 已开的参战屏是否要用 BattleClient.State 补刷(turn-based §22 D74)。直连就绪补拉只把
        /// BattleClient.State 换成新对象 —— 不经回合播放,相位也可能不变(断线前后都是 WaitingAction),
        /// 没有事件会把它交给屏。开局 / 回合结果路径下屏上状态与 BattleClient.State 是同一个对象,
        /// 所以按引用比较即可,不会重复刷新。回合播放中不插手(播完落终态后若仍分叉再补);
        /// 只在回合内补刷,收场(None / Ended)由相位事件处理。
        /// </summary>
        /// <param name="battleOpen">参战屏已开(观战屏不算)。</param>
        /// <param name="playing">回合表现正在播放。</param>
        /// <param name="phase">BattleClient.Phase。</param>
        /// <param name="clientState">BattleClient.State。</param>
        /// <param name="screenState">BattleScreen 屏上正显示的状态对象。</param>
        public static bool NeedsScreenResync(bool battleOpen, bool playing, BattlePhase phase,
                                             BattleStateS2C clientState, BattleStateS2C screenState)
        {
            if (!battleOpen || playing || clientState == null) return false;
            if (phase != BattlePhase.WaitingAction && phase != BattlePhase.Resolving) return false;
            return !ReferenceEquals(clientState, screenState);
        }
    }

    /// <summary>战斗直连提示横幅的显示档位(见 <see cref="BattleHudLogic.DecideChannelBanner"/>)。</summary>
    public enum BattleChannelBanner : byte
    {
        Hidden,
        /// <summary>正在连接战斗服务器(握手中 / 断线自动恢复中)。</summary>
        Connecting,
        /// <summary>无法连接战斗服务器(恢复预算用完),附「重新连接」按钮。</summary>
        Failed,
    }

    /// <summary>
    /// 战斗画布的可见区计算与 HUD 可交互控件清单(纯计算,EditMode 断言"任何分辨率下按钮都在屏内")。
    /// 战斗画布:CanvasScaler ScaleWithScreenSize + 参考 2560×1080 + Expand,设计根居中锚定。
    /// </summary>
    public static class BattleUiLayout
    {
        /// <summary>
        /// Expand 模式:scale = min(屏宽/设计宽, 屏高/设计高),设计面永远整体可见,只在较松的轴上多出空间。
        /// 返回屏幕覆盖的设计坐标矩形(y 向下;设计根居中,所以可能有负的 x/y)。
        /// </summary>
        public static Rect VisibleDesignRect(float screenWidth, float screenHeight)
        {
            float scale = Mathf.Min(screenWidth / QdaoUguiTheme.DesignWidth, screenHeight / QdaoUguiTheme.DesignHeight);
            float w = screenWidth / scale, h = screenHeight / scale;
            return new Rect((QdaoUguiTheme.DesignWidth - w) * 0.5f, (QdaoUguiTheme.DesignHeight - h) * 0.5f, w, h);
        }

        /// <summary>
        /// 对照:MatchWidthOrHeight 模式(scale = 2^lerp(log2 宽比, log2 高比, match))的可见设计矩形。
        /// 1920×1080、match 0.5 时两侧各裁 171px —— 这就是旧画布把「取消自动」裁出屏的原因。
        /// </summary>
        public static Rect VisibleDesignRectMatchWidthOrHeight(float screenWidth, float screenHeight, float match)
        {
            float logW = Mathf.Log(screenWidth / QdaoUguiTheme.DesignWidth, 2f);
            float logH = Mathf.Log(screenHeight / QdaoUguiTheme.DesignHeight, 2f);
            float scale = Mathf.Pow(2f, Mathf.Lerp(logW, logH, match));
            float w = screenWidth / scale, h = screenHeight / scale;
            return new Rect((QdaoUguiTheme.DesignWidth - w) * 0.5f, (QdaoUguiTheme.DesignHeight - h) * 0.5f, w, h);
        }

        /// <summary>矩形 inner 是否整体落在 outer 内(设计坐标,允许 epsilon)。</summary>
        public static bool Contains(Rect outer, Rect inner, float epsilon = 0.01f)
            => inner.xMin >= outer.xMin - epsilon && inner.yMin >= outer.yMin - epsilon
               && inner.xMax <= outer.xMax + epsilon && inner.yMax <= outer.yMax + epsilon;

        /// <summary>战斗屏全部可交互控件矩形(设计坐标,y 向下)。</summary>
        public static List<(string name, Rect rect)> InteractiveRects()
        {
            var list = new List<(string, Rect)>();
            for (int i = 0; i < BattleHudLogic.CommandCount; i++)
                list.Add(($"命令环[{(BattleCommand)i}]", BattleCommandRing.ButtonRect(i)));
            for (int i = 0; i < BattleCommandRing.AutoKeyCount; i++)
                list.Add(($"自动三键[{(AutoBattleCommand)i}]", BattleCommandRing.AutoKeyRect(i)));
            for (int i = 0; i < BattlePartyCards.MaxCards; i++)
                list.Add(($"角色卡[{i}]", BattlePartyCards.CardRect(i)));
            list.Add(("回合框", new Rect(BattleScreen.RoundCounterX, BattleScreen.RoundCounterY, BattleRoundCounter.Width, BattleRoundCounter.Height)));
            list.Add(("战斗记录键", BattleScreen.LogButtonRect));
            list.Add(("计时环", BattleScreen.TimerRect));
            list.Add(("退出观战", BattleScreen.StopWatchRect));
            list.Add(("确认行动", BattleScreen.ConfirmRect));
            list.Add(("取消", BattleScreen.CancelRect));
            list.Add(("重新连接", BattleScreen.ReconnectRect));
            list.Add(("行动预告条", new Rect((QdaoUguiTheme.DesignWidth - BattleActionOrderBar.FullWidth) * 0.5f,
                BattleActionOrderBar.Top, BattleActionOrderBar.FullWidth, BattleActionOrderBar.TileHeight)));
            return list;
        }
    }
}
