using System;
using System.Globalization;
using MmorpgClient.Net.Generated;
using MmorpgClient.Table;

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

    /// <summary>赛季印进度追赶报价（纯 C# 只读值对象，金额严格来自服务端快照）。</summary>
    public readonly struct SeasonCatchupQuote
    {
        public uint SeasonId { get; }
        public uint PointsToBuy { get; }
        public uint RequestPoints { get; }
        public uint CurrencyType { get; }
        public ulong TotalPrice { get; }
        public uint ProgressBefore { get; }
        public uint ProgressAfter { get; }
        public uint MaxPoints { get; }
        public bool IsFullFill { get; }

        public SeasonCatchupQuote(
            uint seasonId,
            uint pointsToBuy,
            uint requestPoints,
            uint currencyType,
            ulong totalPrice,
            uint progressBefore,
            uint progressAfter,
            uint maxPoints,
            bool isFullFill)
        {
            SeasonId = seasonId;
            PointsToBuy = pointsToBuy;
            RequestPoints = requestPoints;
            CurrencyType = currencyType;
            TotalPrice = totalPrice;
            ProgressBefore = progressBefore;
            ProgressAfter = progressAfter;
            MaxPoints = maxPoints;
            IsFullFill = isFullFill;
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

        /// <summary>万分比转一位小数百分比（150 -> "1.5%"，300 -> "3.0%"，对齐效果图展示口径）。</summary>
        public static string FormatBpsOneDecimal(uint bps)
            => (bps / 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

        /// <summary>按服务端墙钟格式化赛季剩余时间；endsAtMs == 0 表示首测常开。</summary>
        public static string FormatSeasonTimeLabel(ulong endsAtMs, ulong serverNowMs)
        {
            if (endsAtMs == 0UL) return "首测常开";
            if (endsAtMs <= serverNowMs) return "本季已结束";
            ulong remainingSec = (endsAtMs - serverNowMs) / 1000UL;
            ulong days = remainingSec / 86400UL;
            ulong hours = (remainingSec % 86400UL) / 3600UL;
            ulong minutes = (remainingSec % 3600UL) / 60UL;
            if (days > 0UL) return hours > 0UL ? $"剩余 {days}天{hours}时" : $"剩余 {days}天";
            if (hours > 0UL) return $"剩余 {hours}小时";
            return $"剩余 {Math.Max(1UL, minutes)}分";
        }

        public static string DescribeProgressBreakdown(global::SeasonStateSnapshot state)
            => state == null ? string.Empty : $"战斗 {state.BattlePoints} 点 + 追赶 {state.CatchupPoints} 点";

        public static string DescribeSourceRuleLine(global::SeasonStateSnapshot state)
            => state == null
                ? string.Empty
                : $"正规排位胜+{state.PvpWinPoints}/完赛+{state.PvpOtherPoints} · 首领试炼胜+{state.PveWinPoints} · 无每日上限";

        /// <summary>将表内 icon_key 规范化为 Unity Resources 相对路径，阻断越界路径。</summary>
        public static string NormalizeIconResource(string iconKey, string fallbackPrefix, uint id)
        {
            if (!string.IsNullOrWhiteSpace(iconKey))
            {
                string key = iconKey.Trim().Replace('\\', '/');
                if (!key.Contains("..") && !key.Contains(":"))
                {
                    const string prefix = "Assets/Resources/";
                    if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        key = key.Substring(prefix.Length);
                    int slash = key.LastIndexOf('/');
                    int dot = key.LastIndexOf('.');
                    if (dot > slash) key = key.Substring(0, dot);
                    if (key.Length > 0) return key;
                }
            }
            return id == 0u || string.IsNullOrEmpty(fallbackPrefix) ? string.Empty : fallbackPrefix + id;
        }

        /// <summary>
        /// 按服务端快照构造追赶确认报价；requestedPoints == 0 表示一键补满，> 0 表示精确补点。
        /// 严禁在本地估算或覆盖服务端单价与剩余总价。
        /// </summary>
        public static bool TryBuildCatchupQuote(
            global::SeasonStateSnapshot state,
            uint requestedPoints,
            out SeasonCatchupQuote quote)
        {
            quote = default;
            if (state == null || !state.Available || state.SeasonId == 0u || !state.CanCatchup)
                return false;
            uint remaining = RemainingCatchupPoints(state);
            if (remaining == 0u || state.CatchupPricePerPoint == 0UL)
                return false;

            bool fullFill = requestedPoints == 0u || requestedPoints >= remaining;
            uint points = fullFill ? remaining : requestedPoints;
            ulong totalPrice = fullFill && state.CatchupRemainingTotalPrice != 0UL
                ? state.CatchupRemainingTotalPrice
                : state.CatchupPricePerPoint * points;
            if (totalPrice == 0UL) return false;

            quote = new SeasonCatchupQuote(
                state.SeasonId,
                points,
                fullFill ? 0u : points,
                state.CatchupCurrencyType,
                totalPrice,
                state.ProgressPoints,
                state.ProgressPoints + points,
                state.MaxPoints,
                fullFill);
            return true;
        }

        /// <summary>按当前快照动态判别技能是否属于本季免费赛季技能（只存 id、现查表，不缓存行引用）。</summary>
        public static bool IsSeasonalSkill(uint skillTableId)
        {
            if (skillTableId == 0u) return false;
            var row = SkillTableManager.Instance?.FindById(skillTableId);
            return row != null && row.SeasonId > 0u;
        }

        /// <summary>格式化战斗内赛季印触发飘字/战报短句（现查 SeasonSealTable，缺表优雅降级）。</summary>
        public static string FormatSeasonSealTriggerText(uint seasonSealId, uint effectId, long value,
            Func<uint, string> sealNameLookup = null)
        {
            string sealName = sealNameLookup != null
                ? sealNameLookup(seasonSealId)
                : SeasonSealTableManager.Instance?.FindById(seasonSealId)?.Name;
            if (string.IsNullOrWhiteSpace(sealName)) sealName = "赛季印";
            return effectId switch
            {
                1u or 2u => value > 0 ? $"{sealName} · 破盾 -{value}" : $"{sealName} · 破盾触发",
                6u => value > 0 ? $"{sealName} · 减伤 -{value}" : $"{sealName} · 护阵减伤",
                7u => value > 0 ? $"{sealName} · 回蓝 +{value}" : $"{sealName} · 聚息回蓝",
                _ => value > 0 ? $"{sealName}触发 ({value})" : $"{sealName}触发",
            };
        }

        /// <summary>格式化战斗内永久法宝触发飘字/战报短句。</summary>
        public static string FormatArtifactTriggerText(uint artifactEffectId, long value)
            => artifactEffectId switch
            {
                1u or 2u => value > 0 ? $"法宝破盾 -{value}" : "法宝破盾触发",
                6u => value > 0 ? $"法宝减伤 -{value}" : "法宝减伤触发",
                7u => value > 0 ? $"法宝回蓝 +{value}" : "法宝回蓝触发",
                _ => value > 0 ? $"法宝触发 ({value})" : "法宝触发",
            };
    }
}
