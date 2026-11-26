using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Net.Generated;
using MmorpgClient.Table;

namespace MmorpgClient.Game.Season
{
    /// <summary>赛季技能、赛季印、五行阵法、五行幻境与配装收藏夹客户端消息号（proto/scene/player_season.proto）。</summary>
    public readonly struct SeasonMessageIds
    {
        public uint GetState { get; }
        public uint EquipSeal { get; }
        public uint CatchupSeal { get; }
        public uint EquipWuxingFormation { get; }
        public uint GetWuxingTowerState { get; }
        public uint StartWuxingTowerRun { get; }
        public uint PickWuxingTowerSigil { get; }
        public uint AbandonWuxingTowerRun { get; }
        public uint SaveBattleBuildBookmark { get; }
        public uint DeleteBattleBuildBookmark { get; }
        public uint SetRetroHonorTarget { get; }

        public bool IsSupported => GetState != 0 && EquipSeal != 0 && CatchupSeal != 0;

        public SeasonMessageIds(
            uint getState,
            uint equipSeal,
            uint catchupSeal,
            uint equipWuxingFormation = 0u,
            uint getWuxingTowerState = 0u,
            uint startWuxingTowerRun = 0u,
            uint pickWuxingTowerSigil = 0u,
            uint abandonWuxingTowerRun = 0u,
            uint saveBattleBuildBookmark = 0u,
            uint deleteBattleBuildBookmark = 0u,
            uint setRetroHonorTarget = 0u)
        {
            GetState = getState;
            EquipSeal = equipSeal;
            CatchupSeal = catchupSeal;
            EquipWuxingFormation = equipWuxingFormation;
            GetWuxingTowerState = getWuxingTowerState;
            StartWuxingTowerRun = startWuxingTowerRun;
            PickWuxingTowerSigil = pickWuxingTowerSigil;
            AbandonWuxingTowerRun = abandonWuxingTowerRun;
            SaveBattleBuildBookmark = saveBattleBuildBookmark;
            DeleteBattleBuildBookmark = deleteBattleBuildBookmark;
            SetRetroHonorTarget = setRetroHonorTarget;
        }

        public static SeasonMessageIds Generated => new SeasonMessageIds(
            SceneSeasonClientPlayerRpc.GetSeasonStateMessageId,
            SceneSeasonClientPlayerRpc.EquipSeasonSealMessageId,
            SceneSeasonClientPlayerRpc.CatchupSeasonSealMessageId,
            SceneSeasonClientPlayerRpc.EquipWuxingFormationMessageId,
            SceneSeasonClientPlayerRpc.GetWuxingTowerStateMessageId,
            SceneSeasonClientPlayerRpc.StartWuxingTowerRunMessageId,
            SceneSeasonClientPlayerRpc.PickWuxingTowerSigilMessageId,
            SceneSeasonClientPlayerRpc.AbandonWuxingTowerRunMessageId,
            SceneSeasonClientPlayerRpc.SaveBattleBuildBookmarkMessageId,
            SceneSeasonClientPlayerRpc.DeleteBattleBuildBookmarkMessageId,
            SceneSeasonClientPlayerRpc.SetRetroHonorTargetMessageId);
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
            (uint)season_error.KSeasonHonorAlreadyUnlocked => "该赛季永久徽章已解锁，无需再设为追忆目标。",
            (uint)season_error.KSeasonHonorTargetInvalid => "所选往季徽章追忆目标无效，请重新选择。",
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

        public static string CurrencyName(uint currencyType) => MmorpgClient.Game.Common.CurrencyLabels.Name(currencyType);

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

        /// <summary>按服务端无限期号快照格式化赛季横幅标题（单季常开 / 无限循环期号轮次）。</summary>
        public static string FormatSeasonTermBanner(global::SeasonStateSnapshot state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.SeasonName)) return "单槽赛季印";
            if (state.CyclePoolSize > 0u && state.SeasonTerm > 0u)
                return $"第 {Math.Max(1u, state.CycleIndex)} 轮 · 第 {state.SeasonTerm} 届 · {state.SeasonName}";
            if (state.SeasonTerm > 1u)
                return $"第 {state.SeasonTerm} 届 · {state.SeasonName}";
            return $"{state.SeasonName} · 单槽赛季印";
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
                4u => value > 0 ? $"{sealName} · 急救增疗 +{value}" : $"{sealName} · 急救增疗",
                5u => value > 0 ? $"{sealName} · 首盾增容 +{value}" : $"{sealName} · 首盾增容",
                6u => value > 0 ? $"{sealName} · 减伤 -{value}" : $"{sealName} · 护阵减伤",
                7u => value > 0 ? $"{sealName} · 回蓝 +{value}" : $"{sealName} · 聚息回蓝",
                101u => value > 0 ? $"技印共鸣 · {sealName}碎盾返蓝 +{value}" : $"技印共鸣 · {sealName}碎盾返蓝",
                102u => value > 0 ? $"技印共鸣 · {sealName}破甲 +{value}回合" : $"技印共鸣 · {sealName}破甲延长",
                103u => value > 0 ? $"技印共鸣 · {sealName}护盾 +{value}" : $"技印共鸣 · {sealName}灵障增盾",
                104u => value > 0 ? $"技印共鸣 · {sealName}省蓝 -{value}" : $"技印共鸣 · {sealName}蓄势省蓝",
                105u => value > 0 ? $"技印共鸣 · {sealName}急救返蓝 +{value}" : $"技印共鸣 · {sealName}急救返蓝",
                106u => value > 0 ? $"技印共鸣 · {sealName}首盾回蓝 +{value}" : $"技印共鸣 · {sealName}首盾回蓝",
                107u => value > 0 ? $"技印共鸣 · {sealName}增疗 +{value}" : $"技印共鸣 · {sealName}守印增疗",
                108u => value > 0 ? $"技印共鸣 · {sealName}增盾 +{value}" : $"技印共鸣 · {sealName}防御增盾",
                _ => value > 0 ? $"{sealName}触发 ({value})" : $"{sealName}触发",
            };
        }

        /// <summary>优先读取服务端表驱动共鸣说明，零客户端硬编码；仅当热更未下发且为 S1 时兼容兜底。</summary>
        public static string DescribeSealResonance(global::SeasonSealOption seal, uint seasonId)
        {
            if (seal == null) return string.Empty;
            if (seal.ResonanceKind == 0u && seal.ResonanceSkillId != 0u) return string.Empty;
            if (!string.IsNullOrWhiteSpace(seal.ResonanceDesc)) return seal.ResonanceDesc;
            return DescribeSealResonance(seasonId, seal.SealId);
        }

        /// <summary>返回 S1 兜底「技印共鸣」一句说明（非 S1 或新季请优先使用表驱动重载）。</summary>
        public static string DescribeSealResonance(uint seasonId, uint sealId)
        {
            if (seasonId != 1u) return string.Empty;
            return sealId switch
            {
                101u => "技印共鸣 · 搭配「破阵裂锋」碎盾返还 30% 法力消耗",
                102u => "技印共鸣 · 搭配「破阵灵雷」破盾使减防延长 +1 回合",
                103u => "技印共鸣 · 触发减伤次轮施放「固阵灵障」护盾 +10%（每场1次）",
                104u => "技印共鸣 · 防御回蓝次轮施放「破阵蓄势」耗蓝减半",
                _ => string.Empty,
            };
        }

        /// <summary>将战后结算包中的单槽赛季印实战统计格式化为一句功劳摘要（零触发返回空串）。</summary>
        public static string FormatSettlementContributionLine(
            uint sealId,
            ulong contributionValue,
            uint triggerCount,
            uint resonanceCount,
            Func<uint, string> sealNameLookup = null)
        {
            if (sealId == 0u || (contributionValue == 0UL && triggerCount == 0u && resonanceCount == 0u))
                return string.Empty;
            string sealName = sealNameLookup != null
                ? sealNameLookup(sealId)
                : SeasonSealTableManager.Instance?.FindById(sealId)?.Name;
            if (string.IsNullOrWhiteSpace(sealName)) sealName = "赛季印";
            uint effectId = SeasonSealTableManager.Instance?.FindById(sealId)?.EffectId ?? 0u;
            string metric = (effectId, sealId) switch
            {
                (1u or 2u, _) or (_, 101u or 102u) => contributionValue > 0UL
                    ? $"累计削盾 {contributionValue.ToString("N0", CultureInfo.InvariantCulture)}"
                    : $"触发破盾 ×{triggerCount}",
                (4u, _) => contributionValue > 0UL
                    ? $"急救增疗 {contributionValue.ToString("N0", CultureInfo.InvariantCulture)}"
                    : $"触发急救增疗 ×{triggerCount}",
                (5u, _) => contributionValue > 0UL
                    ? $"首盾增容 {contributionValue.ToString("N0", CultureInfo.InvariantCulture)}"
                    : $"触发首盾增容 ×{triggerCount}",
                (6u, _) or (_, 103u) => contributionValue > 0UL
                    ? $"护阵减伤/增效 {contributionValue.ToString("N0", CultureInfo.InvariantCulture)}"
                    : $"触发护阵 ×{triggerCount}",
                (7u, _) or (_, 104u) => contributionValue > 0UL
                    ? $"回蓝/省蓝 {contributionValue.ToString("N0", CultureInfo.InvariantCulture)}"
                    : $"触发聚息 ×{triggerCount}",
                _ => contributionValue > 0UL
                    ? $"累计贡献 {contributionValue.ToString("N0", CultureInfo.InvariantCulture)}"
                    : $"触发 ×{triggerCount}",
            };
            return resonanceCount > 0u
                ? $"本场{sealName}{metric} · 技印共鸣 ×{resonanceCount}"
                : $"本场{sealName}{metric}";
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

        /// <summary>按权威周词缀编号返回战斗 HUD 简洁名称（0 = 无）。</summary>
        public static string DescribeWeeklyAffixShort(uint affixId) => affixId switch
        {
            1u => "护盾过载周",
            2u => "灵息充盈周",
            3u => "先机守势周",
            4u => "锋芒竞速周",
            _ => string.Empty,
        };

        /// <summary>按权威周词缀编号返回全员对称效果简述（供战斗 HUD 与排位摘要使用）。</summary>
        public static string DescribeWeeklyAffixDetail(uint affixId) => affixId switch
        {
            1u => "全员对称：双方技能护盾 +15%",
            2u => "全员对称：普通防御额外回蓝 +3%",
            3u => "全员对称：前 2 回合受到直伤 -8%",
            4u => "全员对称：攻击无盾目标直伤 +5%",
            _ => string.Empty,
        };

        /// <summary>从战后 BattleEndS2C 与当前赛季快照构建结算页「赛季印进度与实战功劳」只读视图。</summary>
        public static SeasonSettlementProgressView BuildSettlementProgressView(
            global::BattleEndS2C end,
            global::SeasonStateSnapshot snapshot = null,
            Func<uint, (string seasonName, string honorTitle)> ruleLookup = null)
        {
            if (end == null) return null;
            string contributionLine = string.Empty;
            var settlement = end.Settlement;
            if (settlement != null && settlement.FinalSeasonSealId != 0u)
            {
                uint finalSealId = settlement.FinalSeasonSealId;
                Func<uint, string> sealNameFromSnap = id =>
                {
                    if (snapshot?.Seals != null)
                    {
                        for (int i = 0; i < snapshot.Seals.Count; i++)
                        {
                            var s = snapshot.Seals[i];
                            if (s != null && s.SealId == id && !string.IsNullOrWhiteSpace(s.Name))
                                return s.Name;
                        }
                    }
                    return SeasonSealTableManager.Instance?.FindById(id)?.Name;
                };
                contributionLine = FormatSettlementContributionLine(
                    finalSealId,
                    settlement.SeasonSealContributionValue,
                    settlement.SeasonSealTriggerCount,
                    settlement.SeasonSealResonanceCount,
                    sealNameFromSnap);
            }
            bool hasRetro = end.RetroHonorSeasonId != 0u && end.RetroHonorPointsAdded > 0u;
            if (end.SeasonSealMaxPoints == 0u && string.IsNullOrEmpty(contributionLine) && !hasRetro) return null;
            uint seasonId = end.SeasonId != 0u ? end.SeasonId : (snapshot?.SeasonId ?? 0u);
            string seasonName = snapshot != null && snapshot.SeasonId == seasonId && !string.IsNullOrWhiteSpace(snapshot.SeasonName)
                ? snapshot.SeasonName
                : null;
            string honorTitle = null;
            string honorBadgeIconKey = null;
            if (snapshot?.Honors != null)
            {
                for (int i = 0; i < snapshot.Honors.Count; i++)
                {
                    var h = snapshot.Honors[i];
                    if (h != null && h.SeasonId == seasonId)
                    {
                        if (string.IsNullOrWhiteSpace(seasonName)) seasonName = h.SeasonName;
                        honorTitle = h.HonorTitleName;
                        honorBadgeIconKey = h.HonorBadgeIconKey;
                        break;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(seasonName) || string.IsNullOrWhiteSpace(honorTitle) || string.IsNullOrWhiteSpace(honorBadgeIconKey))
            {
                if (ruleLookup != null)
                {
                    var pair = ruleLookup(seasonId);
                    if (string.IsNullOrWhiteSpace(seasonName)) seasonName = pair.seasonName;
                    if (string.IsNullOrWhiteSpace(honorTitle)) honorTitle = pair.honorTitle;
                }
                else
                {
                    var row = SeasonRuleTableManager.Instance?.FindById(seasonId);
                    if (row != null)
                    {
                        if (string.IsNullOrWhiteSpace(seasonName)) seasonName = row.Name;
                        if (string.IsNullOrWhiteSpace(honorTitle)) honorTitle = row.HonorTitleName;
                        if (string.IsNullOrWhiteSpace(honorBadgeIconKey)) honorBadgeIconKey = row.HonorBadgeIconKey;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(seasonName)) seasonName = "本季赛季印";

            string retroSeasonName = string.Empty;
            string retroTitleName = string.Empty;
            if (hasRetro)
            {
                uint rsid = end.RetroHonorSeasonId;
                if (snapshot?.Honors != null)
                {
                    for (int i = 0; i < snapshot.Honors.Count; i++)
                    {
                        var h = snapshot.Honors[i];
                        if (h != null && h.SeasonId == rsid)
                        {
                            retroSeasonName = h.SeasonName ?? string.Empty;
                            retroTitleName = h.HonorTitleName ?? string.Empty;
                            break;
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(retroSeasonName) || string.IsNullOrWhiteSpace(retroTitleName))
                {
                    if (ruleLookup != null)
                    {
                        var pair = ruleLookup(rsid);
                        if (string.IsNullOrWhiteSpace(retroSeasonName)) retroSeasonName = pair.seasonName ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(retroTitleName)) retroTitleName = pair.honorTitle ?? string.Empty;
                    }
                    else
                    {
                        var rRow = SeasonRuleTableManager.Instance?.FindById(rsid);
                        if (rRow != null)
                        {
                            if (string.IsNullOrWhiteSpace(retroSeasonName)) retroSeasonName = rRow.Name ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(retroTitleName)) retroTitleName = rRow.HonorTitleName ?? string.Empty;
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(retroSeasonName)) retroSeasonName = $"第 {rsid} 季";
            }

            return new SeasonSettlementProgressView(
                seasonId,
                seasonName,
                end.SeasonSealPointsAdded,
                end.SeasonSealProgressAfter,
                end.SeasonSealMaxPoints,
                end.SeasonHonorNewlyUnlocked,
                honorTitle ?? string.Empty,
                honorBadgeIconKey ?? string.Empty,
                contributionLine,
                hasRetro ? end.RetroHonorSeasonId : 0u,
                retroSeasonName,
                retroTitleName,
                hasRetro ? end.RetroHonorPointsAdded : 0u,
                hasRetro ? end.RetroHonorProgressAfter : 0u,
                hasRetro ? end.RetroHonorMaxPoints : 0u,
                hasRetro && end.RetroHonorNewlyUnlocked);
        }

        /// <summary>
        /// 按服务端当前赛季快照动态组装至多 3 套「本季推荐流派（一键切印 + 免费技能搭配）」，
        /// 并自动标亮契合本周对称环境词缀的流派（适配 S1 / S2 及后续赛季）。
        /// </summary>
        public static IReadOnlyList<SeasonBuildPresetView> BuildRecommendedLoadouts(global::SeasonStateSnapshot state)
        {
            if (state == null || !state.Available || state.Seals == null || state.Seals.Count == 0)
                return Array.Empty<SeasonBuildPresetView>();

            var recommended = new HashSet<uint>();
            if (state.WeeklyAffix?.RecommendedSealIds != null)
            {
                for (int i = 0; i < state.WeeklyAffix.RecommendedSealIds.Count; i++)
                    recommended.Add(state.WeeklyAffix.RecommendedSealIds[i]);
            }

            global::SeasonSealOption offenseSeal = null;
            global::SeasonSealOption guardSeal = null;
            global::SeasonSealOption manaSeal = null;
            for (int i = 0; i < state.Seals.Count; i++)
            {
                var seal = state.Seals[i];
                if (seal == null) continue;
                if ((seal.EffectId == 1u || seal.EffectId == 2u) &&
                    (offenseSeal == null || (recommended.Contains(seal.SealId) && !recommended.Contains(offenseSeal.SealId))))
                    offenseSeal = seal;
                else if ((seal.EffectId == 6u || seal.EffectId == 5u) &&
                    (guardSeal == null || (recommended.Contains(seal.SealId) && !recommended.Contains(guardSeal.SealId))))
                    guardSeal = seal;
                else if ((seal.EffectId == 7u || seal.EffectId == 4u) &&
                    (manaSeal == null || (recommended.Contains(seal.SealId) && !recommended.Contains(manaSeal.SealId))))
                    manaSeal = seal;
            }

            string offenseSkills = JoinSkillNamesByTarget(state, targetTeam: 1u, fallback: "本季输出技");
            string supportSkills = JoinSkillNamesByTarget(state, targetTeam: 2u, fallback: "本季增益/护盾技");

            var list = new List<SeasonBuildPresetView>(3);
            if (offenseSeal != null)
            {
                list.Add(new SeasonBuildPresetView(
                    "breaker",
                    "破盾强攻流",
                    offenseSeal.SealId,
                    offenseSeal.Name,
                    offenseSkills,
                    recommended.Contains(offenseSeal.SealId),
                    offenseSeal.SealId == state.EquippedSealId));
            }
            if (guardSeal != null)
            {
                list.Add(new SeasonBuildPresetView(
                    "guard",
                    "稳守反击流",
                    guardSeal.SealId,
                    guardSeal.Name,
                    supportSkills,
                    recommended.Contains(guardSeal.SealId),
                    guardSeal.SealId == state.EquippedSealId));
            }
            if (manaSeal != null)
            {
                list.Add(new SeasonBuildPresetView(
                    "sustain",
                    "蓄息拉扯流",
                    manaSeal.SealId,
                    manaSeal.Name,
                    supportSkills,
                    recommended.Contains(manaSeal.SealId),
                    manaSeal.SealId == state.EquippedSealId));
            }
            return list;
        }

        public static global::SeasonHonorView FindCurrentHonor(global::SeasonStateSnapshot state)
        {
            if (state?.Honors == null) return null;
            for (int i = 0; i < state.Honors.Count; i++)
            {
                var h = state.Honors[i];
                if (h != null && (h.IsCurrentSeason || (state.SeasonId != 0u && h.SeasonId == state.SeasonId)))
                    return h;
            }
            return null;
        }

        public static int CountUnlockedHonors(global::SeasonStateSnapshot state)
        {
            if (state?.Honors == null) return 0;
            int count = 0;
            for (int i = 0; i < state.Honors.Count; i++)
                if (state.Honors[i] != null && state.Honors[i].Unlocked) ++count;
            return count;
        }

        private static string JoinSkillNamesByTarget(global::SeasonStateSnapshot state, uint targetTeam, string fallback)
        {
            if (state?.SeasonSkills == null || state.SeasonSkills.Count == 0) return fallback;
            var names = new List<string>(2);
            for (int i = 0; i < state.SeasonSkills.Count && names.Count < 2; i++)
            {
                var sk = state.SeasonSkills[i];
                if (sk != null && sk.TargetTeam == targetTeam && !string.IsNullOrWhiteSpace(sk.Name))
                    names.Add(sk.Name);
            }
            return names.Count > 0 ? string.Join(" + ", names) : fallback;
        }
    }

    /// <summary>战后结算窗口「赛季印进度、往季追忆与本场功劳」只读视图（零 UnityEngine 依赖）。</summary>
    public sealed class SeasonSettlementProgressView
    {
        public uint SeasonId { get; }
        public string SeasonName { get; }
        public uint PointsAdded { get; }
        public uint ProgressAfter { get; }
        public uint MaxPoints { get; }
        public bool HonorNewlyUnlocked { get; }
        public string HonorTitleName { get; }
        public string HonorBadgeIconKey { get; }
        public string ContributionLine { get; }
        public uint RetroHonorSeasonId { get; }
        public string RetroHonorSeasonName { get; }
        public string RetroHonorTitleName { get; }
        public uint RetroHonorPointsAdded { get; }
        public uint RetroHonorProgressAfter { get; }
        public uint RetroHonorMaxPoints { get; }
        public bool RetroHonorNewlyUnlocked { get; }
        public bool HasRetroHonor => RetroHonorSeasonId != 0u && RetroHonorPointsAdded > 0u;
        public bool IsMaxed => MaxPoints > 0u && ProgressAfter >= MaxPoints;

        public SeasonSettlementProgressView(
            uint seasonId,
            string seasonName,
            uint pointsAdded,
            uint progressAfter,
            uint maxPoints,
            bool honorNewlyUnlocked,
            string honorTitleName,
            string honorBadgeIconKey = "",
            string contributionLine = "",
            uint retroHonorSeasonId = 0u,
            string retroHonorSeasonName = "",
            string retroHonorTitleName = "",
            uint retroHonorPointsAdded = 0u,
            uint retroHonorProgressAfter = 0u,
            uint retroHonorMaxPoints = 0u,
            bool retroHonorNewlyUnlocked = false)
        {
            SeasonId = seasonId;
            SeasonName = seasonName;
            PointsAdded = pointsAdded;
            ProgressAfter = progressAfter;
            MaxPoints = maxPoints;
            HonorNewlyUnlocked = honorNewlyUnlocked;
            HonorTitleName = honorTitleName;
            HonorBadgeIconKey = honorBadgeIconKey ?? string.Empty;
            ContributionLine = contributionLine ?? string.Empty;
            RetroHonorSeasonId = retroHonorSeasonId;
            RetroHonorSeasonName = retroHonorSeasonName ?? string.Empty;
            RetroHonorTitleName = retroHonorTitleName ?? string.Empty;
            RetroHonorPointsAdded = retroHonorPointsAdded;
            RetroHonorProgressAfter = retroHonorProgressAfter;
            RetroHonorMaxPoints = retroHonorMaxPoints;
            RetroHonorNewlyUnlocked = retroHonorNewlyUnlocked;
        }
    }

    /// <summary>本季一键推荐流派视图（仅复用战外免费切印 RPC 454 + 本季免费技能引导）。</summary>
    public readonly struct SeasonBuildPresetView
    {
        public string PresetKey { get; }
        public string Title { get; }
        public uint SealId { get; }
        public string SealName { get; }
        public string PairedSkillsSummary { get; }
        public bool RecommendedThisWeek { get; }
        public bool IsCurrentlyEquipped { get; }

        public SeasonBuildPresetView(
            string presetKey,
            string title,
            uint sealId,
            string sealName,
            string pairedSkillsSummary,
            bool recommendedThisWeek,
            bool isCurrentlyEquipped)
        {
            PresetKey = presetKey;
            Title = title;
            SealId = sealId;
            SealName = sealName;
            PairedSkillsSummary = pairedSkillsSummary;
            RecommendedThisWeek = recommendedThisWeek;
            IsCurrentlyEquipped = isCurrentlyEquipped;
        }
    }
}
