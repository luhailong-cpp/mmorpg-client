using System;
using System.Globalization;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Season
{
    /// <summary>赛季技能与赛季印客户端消息号（proto/scene/player_season.proto，默认取生成常量 453–455）。</summary>
    public readonly struct SeasonMessageIds
    {
        public uint GetState { get; }
        public uint EquipSeal { get; }
        public uint CatchupSeal { get; }

        public bool IsSupported => GetState != 0 && EquipSeal != 0 && CatchupSeal != 0;

        public SeasonMessageIds(uint getState, uint equipSeal, uint catchupSeal)
        {
            GetState = getState;
            EquipSeal = equipSeal;
            CatchupSeal = catchupSeal;
        }

        public static SeasonMessageIds Generated => new SeasonMessageIds(
            SceneSeasonClientPlayerRpc.GetSeasonStateMessageId,
            SceneSeasonClientPlayerRpc.EquipSeasonSealMessageId,
            SceneSeasonClientPlayerRpc.CatchupSeasonSealMessageId);
    }

    /// <summary>赛季印进度追赶成功回执（不可变值对象，跨模块事件传递安全）。</summary>
    public readonly struct SeasonSealCatchupEvent
    {
        public uint SeasonId { get; }
        public uint AddedPoints { get; }
        public ulong SpentCurrency { get; }
        public bool Replayed { get; }

        public SeasonSealCatchupEvent(uint seasonId, uint addedPoints, ulong spentCurrency, bool replayed)
        {
            SeasonId = seasonId;
            AddedPoints = addedPoints;
            SpentCurrency = spentCurrency;
            Replayed = replayed;
        }
    }

    /// <summary>
    /// 赛季技能与赛季印纯 C# 文案与只读推导（零 UnityEngine 依赖，WebGL2/IL2CPP 安全）。
    /// 所有赛季开启态、印记进度、追赶定价与是否已装备严格以服务端 <see cref="global::SeasonStateSnapshot"/> 为唯一真相。
    /// </summary>
    public static class SeasonModels
    {
        /// <summary>将服务端 season_error 与通用错误码转换为中文提示。</summary>
        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)season_error.KSeasonNotOpen => "当前暂无进行中的赛季。",
            (uint)season_error.KSeasonMismatch => "赛季已切换，请刷新页面后重试。",
            (uint)season_error.KSeasonSealInvalid => "所选赛季印无效或不属于本季。",
            (uint)season_error.KSeasonSealAlreadyEquipped => "已装备该赛季印。",
            (uint)season_error.KSeasonSealAlreadyMax => "本季赛季印进度已满，无需继续追赶。",
            (uint)season_error.KSeasonCatchupDisabled => "当前未开放赛季印追赶。",
            (uint)season_error.KSeasonCatchupPriceChanged => "赛季印追赶定价已刷新，请确认最新报价后重试。",
            (uint)season_error.KSeasonCatchupCurrencyNotEnough => "所需货币不足，无法追赶赛季印进度。",
            (uint)season_error.KSeasonFrozen => "当前角色状态暂不可操作赛季印，请稍后再试。",
            (uint)season_error.KSeasonInBattle => "战斗中不可切换或追赶赛季印，请在结算后重试。",
            (uint)season_error.KSeasonSkillNoUpgrade => "赛季技能随赛季自动生效，无需单独升级。",
            (uint)season_error.KSeasonUnavailable => "赛季服务暂不可用，请稍后再试。",
            (uint)common_error.KRateLimitExceeded => "操作太快了，请稍候再试。",
            (uint)common_error.KServiceUnavailable => "服务暂时不可用，请稍后再试。",
            (uint)common_error.KInvalidParameter => "请求参数无效，请刷新页面后再试。",
            _ => $"赛季操作失败（{tipId}）。",
        };

        /// <summary>万分比转展示百分比（600 -> "6%"，450 -> "4.5%"）。</summary>
        public static string FormatBpsPercent(uint bps)
        {
            if (bps % 100u == 0u) return (bps / 100u).ToString(CultureInfo.InvariantCulture) + "%";
            return (bps / 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }

        public static string CurrencyName(uint currencyType) => currencyType switch
        {
            0u => "银两",
            1u => "灵石",
            2u => "绑定灵石",
            _ => "货币",
        };

        public static global::SeasonSealOption FindEquippedSeal(global::SeasonStateSnapshot state)
        {
            if (state?.Seals == null || state.EquippedSealId == 0) return null;
            for (int i = 0; i < state.Seals.Count; i++)
            {
                var seal = state.Seals[i];
                if (seal != null && seal.SealId == state.EquippedSealId) return seal;
            }
            return null;
        }

        public static uint RemainingCatchupPoints(global::SeasonStateSnapshot state)
        {
            if (state == null || state.ProgressPoints >= state.MaxPoints) return 0u;
            return state.MaxPoints - state.ProgressPoints;
        }
    }
}
