using System;
using System.Collections.Generic;
using System.Globalization;
using Match;

namespace MmorpgClient.Game.Tournament
{
    /// <summary>
    /// 全服赛季战队锦标赛《群仙问道赛》V1 纯 C# 只读展示与格式化辅助（不引用 UnityEngine，WebGL2/IL2CPP 安全）。
    /// </summary>
    public static class TournamentFormatting
    {
        public static string StageDisplayName(TournamentStage stage) => stage switch
        {
            TournamentStage.Registration => "赛季报名",
            TournamentStage.Qualifier => "宽窗海选",
            TournamentStage.Knockout => "全服淘汰赛",
            TournamentStage.Settled => "封榜发奖",
            _ => "未开启",
        };

        public static string SquadStateDisplayName(TournamentSquadState state) => state switch
        {
            TournamentSquadState.Draft => "筹备中",
            TournamentSquadState.Registered => "已锁定报名",
            TournamentSquadState.Eliminated => "已止步",
            TournamentSquadState.Champion => "全服总冠军",
            TournamentSquadState.Disbanded => "已解散",
            _ => "未入队",
        };

        public static string MemberRoleDisplayName(TournamentMemberRole role) => role switch
        {
            TournamentMemberRole.Captain => "队长",
            TournamentMemberRole.Starter => "首发",
            TournamentMemberRole.Substitute => "替补",
            _ => "成员",
        };

        public static string MatchStateDisplayName(TournamentMatchState state) => state switch
        {
            TournamentMatchState.WaitingOpponent => "等待就绪",
            TournamentMatchState.Ready => "备战中",
            TournamentMatchState.InBattle => "激战中",
            TournamentMatchState.Finished => "已决出",
            TournamentMatchState.Bye => "轮空晋级",
            TournamentMatchState.Forfeit => "超时裁决",
            TournamentMatchState.Intermission => "局间换阵",
            _ => "待开赛",
        };

        /// <summary>V2 五行地脉展示元数据（文字说明与配色解耦，绝不单靠颜色区分）。</summary>
        public readonly struct TournamentLeylineInfo
        {
            public TournamentLeylineElement Element { get; }
            public string ShortName { get; }
            public string FullName { get; }
            public string EffectSummary { get; }
            public string SpriteKey { get; }

            public TournamentLeylineInfo(
                TournamentLeylineElement element,
                string shortName,
                string fullName,
                string effectSummary,
                string spriteKey)
            {
                Element = element;
                ShortName = shortName;
                FullName = fullName;
                EffectSummary = effectSummary;
                SpriteKey = spriteKey;
            }
        }

        public static readonly TournamentLeylineInfo[] AllLeylines =
        {
            new TournamentLeylineInfo(TournamentLeylineElement.TournamentLeylineMetal, "金锐", "金锐灵脉", "全体暴击率 +5%，破防直伤 +4%", "v2-a-element-metal"),
            new TournamentLeylineInfo(TournamentLeylineElement.TournamentLeylineWood,  "木生", "木生灵脉", "全体治疗与护盾效果 +10%", "v2-a-element-wood"),
            new TournamentLeylineInfo(TournamentLeylineElement.TournamentLeylineWater, "水润", "水润灵脉", "每回合初恢复 5% 法力，控制抗性 +8%", "v2-a-element-water"),
            new TournamentLeylineInfo(TournamentLeylineElement.TournamentLeylineFire,  "火炽", "火炽灵脉", "全体技能与普攻伤害 +6%", "v2-a-element-fire"),
            new TournamentLeylineInfo(TournamentLeylineElement.TournamentLeylineEarth, "土厚", "土厚灵脉", "全体受击伤害 -5%，首回合开场护盾 +8%", "v2-a-element-earth"),
        };

        public static TournamentLeylineInfo GetLeylineInfo(TournamentLeylineElement element) => element switch
        {
            TournamentLeylineElement.TournamentLeylineMetal => AllLeylines[0],
            TournamentLeylineElement.TournamentLeylineWood => AllLeylines[1],
            TournamentLeylineElement.TournamentLeylineWater => AllLeylines[2],
            TournamentLeylineElement.TournamentLeylineFire => AllLeylines[3],
            TournamentLeylineElement.TournamentLeylineEarth => AllLeylines[4],
            _ => new TournamentLeylineInfo(TournamentLeylineElement.TournamentLeylineUnspecified, "无脉", "常规赛场", "本场未启用五行灵脉加成", string.Empty),
        };

        /// <summary>V2 观战预设喝彩条目（严格白名单 1..6，与服务端及 phrase-01..06 完整对应）。</summary>
        public readonly struct TournamentSpectateCheerPreset
        {
            public uint PhraseId { get; }
            public string Label { get; }
            public string SpriteKey { get; }
            public bool HasNativeFloatArt { get; }

            public TournamentSpectateCheerPreset(uint phraseId, string label, string spriteKey, bool hasNativeFloatArt)
            {
                PhraseId = phraseId;
                Label = label;
                SpriteKey = spriteKey;
                HasNativeFloatArt = hasNativeFloatArt;
            }
        }

        public static readonly TournamentSpectateCheerPreset[] SpectateCheerPresets =
        {
            new TournamentSpectateCheerPreset(1u, "道友加油", "v2-c-phrase-01", false),
            new TournamentSpectateCheerPreset(2u, "稳住阵脚", "v2-c-phrase-02", false),
            new TournamentSpectateCheerPreset(3u, "妙法天成", "v2-c-phrase-03", true),
            new TournamentSpectateCheerPreset(4u, "配合无间", "v2-c-phrase-04", false),
            new TournamentSpectateCheerPreset(5u, "逆转乾坤", "v2-c-phrase-05", false),
            new TournamentSpectateCheerPreset(6u, "一战封神", "v2-c-phrase-06", false),
        };

        public static bool TryGetCheerPreset(uint phraseId, out TournamentSpectateCheerPreset preset)
        {
            if (phraseId >= 1u && phraseId <= (uint)SpectateCheerPresets.Length)
            {
                preset = SpectateCheerPresets[phraseId - 1u];
                return true;
            }
            preset = default;
            return false;
        }

        /// <summary>计算双方真实观战人气对推比例；总和为 0 时返回中性均衡态（绝不伪造领先）。</summary>
        public static (float ratioA, float ratioB, bool isNeutral) ComputePopularitySplit(ulong countA, ulong countB)
        {
            ulong total = countA + countB;
            if (total == 0UL) return (0.5f, 0.5f, true);
            float a = (float)((double)countA / total);
            return (a, 1f - a, false);
        }

        public static string FormatBo3Score(TournamentMatchView match)
        {
            if (match == null) return string.Empty;
            uint bestOf = match.BestOf == 0 ? 1u : match.BestOf;
            if (bestOf <= 1u) return string.Empty;
            return $"BO{bestOf} {match.WinsA}:{match.WinsB}";
        }

        public static string MatchPhaseDisplayName(TournamentMatchPhase phase) => phase switch
        {
            TournamentMatchPhase.Qualifier => "海选赛",
            TournamentMatchPhase.Knockout => "淘汰赛",
            TournamentMatchPhase.Warmup => "热身演练",
            _ => "赛程",
        };

        /// <summary>V3 官方预设 8 枚道家战队纹章元数据（严格白名单 1..8，零玩家自由上传图片）。</summary>
        public readonly struct TournamentEmblemInfo
        {
            public uint Id { get; }
            public string Name { get; }
            public string Glyph { get; }
            public string SpriteKey { get; }

            public TournamentEmblemInfo(uint id, string name, string glyph, string spriteKey)
            {
                Id = id;
                Name = name;
                Glyph = glyph;
                SpriteKey = spriteKey;
            }
        }

        public static readonly TournamentEmblemInfo[] AllEmblems =
        {
            new TournamentEmblemInfo(1u, "青剑", "剑", "v3-g-emblem-01"),
            new TournamentEmblemInfo(2u, "太极", "极", "v3-g-emblem-02"),
            new TournamentEmblemInfo(3u, "丹炉", "炉", "v3-g-emblem-03"),
            new TournamentEmblemInfo(4u, "青莲", "莲", "v3-g-emblem-04"),
            new TournamentEmblemInfo(5u, "玄鹤", "鹤", "v3-g-emblem-05"),
            new TournamentEmblemInfo(6u, "雷印", "雷", "v3-g-emblem-06"),
            new TournamentEmblemInfo(7u, "八卦", "卦", "v3-g-emblem-07"),
            new TournamentEmblemInfo(8u, "云鼎", "鼎", "v3-g-emblem-08"),
        };

        public static uint NormalizeEmblemId(uint emblemId) =>
            emblemId >= 1u && emblemId <= (uint)AllEmblems.Length ? emblemId : 1u;

        public static TournamentEmblemInfo GetEmblemInfo(uint emblemId) =>
            AllEmblems[NormalizeEmblemId(emblemId) - 1u];

        public static string PredictionResultDisplayName(uint resultState) => resultState switch
        {
            1u => "猜中",
            2u => "未中",
            _ => "待结算",
        };

        /// <summary>V4 模块 J：战队单赛季纯荣誉铭牌元数据（6 枚，对应 honor_bits bit0..5，零战力加成）。</summary>
        public readonly struct TournamentHonorInfo
        {
            public int BitIndex { get; }
            public uint BitMask { get; }
            public string Name { get; }
            public string ShortGlyph { get; }
            public string ConditionDesc { get; }
            public string SpriteKey { get; }

            public TournamentHonorInfo(int bitIndex, string name, string shortGlyph, string conditionDesc, string spriteKey)
            {
                BitIndex = bitIndex;
                BitMask = 1u << bitIndex;
                Name = name;
                ShortGlyph = shortGlyph;
                ConditionDesc = conditionDesc;
                SpriteKey = spriteKey;
            }
        }

        public static readonly TournamentHonorInfo[] AllHonors =
        {
            new TournamentHonorInfo(0, "问道初成", "初", "本季海选完赛达标", "v4-j-honor-01"),
            new TournamentHonorInfo(1, "连胜破竹", "胜", "本季海选累计 3 胜及以上", "v4-j-honor-02"),
            new TournamentHonorInfo(2, "不败金身", "稳", "本季海选完赛 ≥3 场且 0 负", "v4-j-honor-03"),
            new TournamentHonorInfo(3, "逆转乾坤", "逆", "赢下打满 3 局的 BO3 淘汰赛", "v4-j-honor-04"),
            new TournamentHonorInfo(4, "零封强敌", "封", "淘汰赛 BO3 以 2:0 零封对手", "v4-j-honor-05"),
            new TournamentHonorInfo(5, "问鼎群仙", "鼎", "夺得本赛季全服总冠军", "v4-j-honor-06"),
        };

        public static bool HasHonorBit(uint honorBits, int bitIndex) =>
            bitIndex >= 0 && bitIndex < AllHonors.Length && (honorBits & (1u << bitIndex)) != 0u;

        public static int CountUnlockedHonors(uint honorBits)
        {
            int count = 0;
            for (int i = 0; i < AllHonors.Length; i++)
            {
                if ((honorBits & AllHonors[i].BitMask) != 0u) count++;
            }
            return count;
        }

        public static string HighlightTagDisplayName(TournamentHighlightTag tag) => tag switch
        {
            TournamentHighlightTag.Final => "总决赛巅峰局",
            TournamentHighlightTag.Bo3Thriller => "决胜三局激战",
            TournamentHighlightTag.ThirdPlace => "季军争夺战",
            TournamentHighlightTag.HighMvp => "高光问鼎局",
            _ => "本季名局",
        };

        public static string FormatMvpMetrics(TournamentMatchView match)
        {
            if (match == null || match.MvpPlayerId == 0UL) return string.Empty;
            return string.Format(CultureInfo.InvariantCulture,
                "战功 {0} · 输出 {1:N0} · 治疗 {2:N0} · 硬控 {3} 次",
                match.MvpScore, match.MvpDamage, match.MvpHealing, match.MvpControls);
        }

        public static string RewardTierDisplayName(TournamentRewardTierKind tier) => tier switch
        {
            TournamentRewardTierKind.TournamentRewardTierChampion => "全服总冠军",
            TournamentRewardTierKind.TournamentRewardTierRunnerUp => "全服亚军",
            TournamentRewardTierKind.TournamentRewardTierThirdPlace => "全服季军",
            TournamentRewardTierKind.TournamentRewardTierTop4 => "全服四强",
            TournamentRewardTierKind.TournamentRewardTierTop8 => "全服八强",
            TournamentRewardTierKind.TournamentRewardTierTop16 => "全服十六强",
            TournamentRewardTierKind.TournamentRewardTierParticipant => "参赛保底",
            TournamentRewardTierKind.TournamentRewardTierCheer => "免费助威命中",
            TournamentRewardTierKind.TournamentRewardTierPrediction => "神机问道纪念",
            _ => "赛季奖励",
        };

        public static string RewardDeliveryDisplayName(TournamentRewardDeliveryState state) => state switch
        {
            TournamentRewardDeliveryState.TournamentRewardDeliveryClaimable => "可领取",
            TournamentRewardDeliveryState.TournamentRewardDeliveryClaimed => "已发至邮箱",
            TournamentRewardDeliveryState.TournamentRewardDeliveryAbandoned => "待核查",
            _ => "未结算",
        };

        /// <summary>按淘汰赛总规模与当前轮次（1起）生成标准中文轮次名。</summary>
        public static string KnockoutRoundTitle(uint knockoutTeamCount, uint roundIndex)
        {
            if (knockoutTeamCount < 2 || roundIndex == 0) return "淘汰赛";
            uint remaining = knockoutTeamCount;
            for (uint r = 1; r < roundIndex && remaining > 2; r++) remaining /= 2u;
            return remaining switch
            {
                2u => "全服总决赛",
                4u => "半决赛",
                8u => "八强赛",
                16u => "十六强赛",
                _ => $"第 {roundIndex} 轮",
            };
        }

        public static string KnockoutMatchTitle(uint knockoutTeamCount, TournamentMatchView match)
        {
            if (match == null) return "淘汰赛";
            if (match.IsThirdPlace) return "季军争夺战";
            return KnockoutRoundTitle(knockoutTeamCount, match.RoundIndex);
        }

        /// <summary>将剩余毫秒数格式化为中文倒计时（不依赖本机系统时钟）。</summary>
        public static string FormatCountdown(ulong remainingMs)
        {
            ulong totalSec = remainingMs / 1000UL;
            ulong days = totalSec / 86400UL;
            ulong hours = (totalSec % 86400UL) / 3600UL;
            ulong minutes = (totalSec % 3600UL) / 60UL;
            ulong seconds = totalSec % 60UL;
            if (days > 0) return $"{days}天{hours}小时";
            if (hours > 0) return $"{hours}小时{minutes:00}分";
            return $"{minutes:00}:{seconds:00}";
        }

        public static string FormatRecord(TournamentSquadView squad)
        {
            if (squad == null) return "0胜 0平 0负 · 0分";
            return string.Format(CultureInfo.InvariantCulture,
                "{0}胜 {1}平 {2}负 · {3}分",
                squad.QualifierWins, squad.QualifierDraws, squad.QualifierLosses, squad.QualifierPoints);
        }
    }

    /// <summary>RPC 465 GetTournamentOverview 的不可变只读快照，所有 Proto 对象均为独立副本。</summary>
    public sealed class TournamentOverviewSnapshot
    {
        public TournamentSeasonView Season { get; }
        public TournamentSquadView MySquad { get; }
        public IReadOnlyList<TournamentInviteView> PendingInvites { get; }
        public TournamentMatchView MyActiveMatch { get; }
        public IReadOnlyList<TournamentMatchView> FeaturedMatches { get; }
        public ulong MyCheeredSquadId { get; }
        public string MyCheeredSquadName { get; }
        public IReadOnlyList<TournamentRewardTierView> RewardTiers { get; }
        public IReadOnlyList<TournamentPlayerRewardView> MyRewards { get; }
        public IReadOnlyList<TournamentSeasonView> HallOfFame { get; }
        public ulong ServerTimeMs { get; }
        public float CapturedAtLocalSeconds { get; }

        public bool HasSeason => Season != null && Season.SeasonId != 0;
        public bool IsEnabled => Season != null && Season.Enabled;
        public bool HasSquad => MySquad != null && MySquad.SquadId != 0;
        public bool HasCheered => MyCheeredSquadId != 0;
        public bool HasPendingInvites => PendingInvites.Count > 0;

        public bool HasClaimableReward
        {
            get
            {
                for (int i = 0; i < MyRewards.Count; i++)
                {
                    if (MyRewards[i].DeliveryState == TournamentRewardDeliveryState.TournamentRewardDeliveryClaimable)
                        return true;
                }
                return false;
            }
        }

        public bool IsCaptain(ulong playerId) =>
            playerId != 0 && MySquad != null && MySquad.CaptainPlayerId == playerId;

        /// <summary>基于服务端下发 ServerTimeMs + 单调本地秒表估算当前权威毫秒时间。</summary>
        public ulong EstimateServerNowMs(float nowLocalSeconds)
        {
            if (ServerTimeMs == 0) return 0UL;
            float delta = nowLocalSeconds - CapturedAtLocalSeconds;
            if (delta <= 0f) return ServerTimeMs;
            return ServerTimeMs + (ulong)(delta * 1000.0);
        }

        /// <summary>当前阶段剩余毫秒数（以服务端下发的四阶段边界为准）。</summary>
        public ulong CurrentStageRemainingMs(float nowLocalSeconds)
        {
            if (Season == null) return 0UL;
            ulong nowMs = EstimateServerNowMs(nowLocalSeconds);
            ulong targetMs = Season.Stage switch
            {
                TournamentStage.Registration => Season.QualifierStartMs,
                TournamentStage.Qualifier => Season.KnockoutStartMs,
                TournamentStage.Knockout => Season.SettleStartMs,
                TournamentStage.Settled => Season.EndMs,
                _ => 0UL,
            };
            return targetMs > nowMs ? targetMs - nowMs : 0UL;
        }

        /// <summary>V2：计算 BO3 局间换阵剩余毫秒数（以服务端 ServerTimeMs 校准本地单调时间，绝不使用系统壁钟）。</summary>
        public ulong IntermissionRemainingMs(TournamentMatchView match, float nowLocalSeconds)
        {
            if (match == null || match.IntermissionDeadlineMs == 0UL) return 0UL;
            ulong nowMs = EstimateServerNowMs(nowLocalSeconds);
            return match.IntermissionDeadlineMs > nowMs ? match.IntermissionDeadlineMs - nowMs : 0UL;
        }

        /// <summary>V2：查找最近一届已诞生总冠军的赛季视图（优先本季，其次历届冠军殿堂首项）。</summary>
        public TournamentSeasonView FindLatestChampionSeason()
        {
            if (Season != null && Season.ChampionSquadId != 0UL) return Season;
            for (int i = 0; i < HallOfFame.Count; i++)
            {
                if (HallOfFame[i] != null && HallOfFame[i].ChampionSquadId != 0UL)
                    return HallOfFame[i];
            }
            return null;
        }

        public TournamentOverviewSnapshot(
            TournamentSeasonView season,
            TournamentSquadView mySquad,
            IReadOnlyList<TournamentInviteView> pendingInvites,
            TournamentMatchView myActiveMatch,
            IReadOnlyList<TournamentMatchView> featuredMatches,
            ulong myCheeredSquadId,
            string myCheeredSquadName,
            IReadOnlyList<TournamentRewardTierView> rewardTiers,
            IReadOnlyList<TournamentPlayerRewardView> myRewards,
            IReadOnlyList<TournamentSeasonView> hallOfFame,
            ulong serverTimeMs,
            float capturedAtLocalSeconds)
        {
            Season = season;
            MySquad = mySquad;
            PendingInvites = pendingInvites;
            MyActiveMatch = myActiveMatch;
            FeaturedMatches = featuredMatches;
            MyCheeredSquadId = myCheeredSquadId;
            MyCheeredSquadName = myCheeredSquadName ?? string.Empty;
            RewardTiers = rewardTiers;
            MyRewards = myRewards;
            HallOfFame = hallOfFame;
            ServerTimeMs = serverTimeMs;
            CapturedAtLocalSeconds = capturedAtLocalSeconds;
        }

        public static TournamentOverviewSnapshot FromResponse(GetTournamentOverviewResponse resp, float localSeconds)
        {
            if (resp == null) return null;
            var invites = new List<TournamentInviteView>(resp.PendingInvites.Count);
            foreach (var inv in resp.PendingInvites) if (inv != null) invites.Add(inv.Clone());

            var featured = new List<TournamentMatchView>(resp.FeaturedMatches.Count);
            foreach (var m in resp.FeaturedMatches) if (m != null) featured.Add(m.Clone());

            var tiers = new List<TournamentRewardTierView>(resp.RewardTiers.Count);
            foreach (var t in resp.RewardTiers) if (t != null) tiers.Add(t.Clone());

            var rewards = new List<TournamentPlayerRewardView>(resp.MyRewards.Count);
            foreach (var r in resp.MyRewards) if (r != null) rewards.Add(r.Clone());

            var hof = new List<TournamentSeasonView>(resp.HallOfFame.Count);
            foreach (var s in resp.HallOfFame) if (s != null) hof.Add(s.Clone());

            return new TournamentOverviewSnapshot(
                resp.CurrentSeason?.Clone(),
                resp.MySquad?.Clone(),
                invites,
                resp.MyActiveMatch?.Clone(),
                featured,
                resp.MyCheeredSquadId,
                resp.MyCheeredSquadName,
                tiers,
                rewards,
                hof,
                resp.ServerTimeMs,
                localSeconds);
        }

        public TournamentOverviewSnapshot WithSquad(TournamentSquadView nextSquad, ulong removeInviteSquadId = 0UL)
        {
            IReadOnlyList<TournamentInviteView> invites = PendingInvites;
            if (removeInviteSquadId != 0UL && PendingInvites.Count > 0)
            {
                var filtered = new List<TournamentInviteView>(PendingInvites.Count);
                for (int i = 0; i < PendingInvites.Count; i++)
                {
                    if (PendingInvites[i].SquadId != removeInviteSquadId)
                        filtered.Add(PendingInvites[i]);
                }
                invites = filtered;
            }
            return new TournamentOverviewSnapshot(
                Season?.Clone(),
                nextSquad?.Clone(),
                invites,
                nextSquad == null ? null : MyActiveMatch?.Clone(),
                FeaturedMatches,
                MyCheeredSquadId,
                MyCheeredSquadName,
                RewardTiers,
                MyRewards,
                HallOfFame,
                ServerTimeMs,
                CapturedAtLocalSeconds);
        }

        public TournamentOverviewSnapshot WithActiveMatch(TournamentMatchView nextMatch) =>
            new TournamentOverviewSnapshot(
                Season?.Clone(),
                MySquad?.Clone(),
                PendingInvites,
                nextMatch?.Clone(),
                FeaturedMatches,
                MyCheeredSquadId,
                MyCheeredSquadName,
                RewardTiers,
                MyRewards,
                HallOfFame,
                ServerTimeMs,
                CapturedAtLocalSeconds);

        public TournamentOverviewSnapshot WithCheered(ulong squadId, string squadName) =>
            new TournamentOverviewSnapshot(
                Season?.Clone(),
                MySquad?.Clone(),
                PendingInvites,
                MyActiveMatch?.Clone(),
                FeaturedMatches,
                squadId,
                squadName ?? string.Empty,
                RewardTiers,
                MyRewards,
                HallOfFame,
                ServerTimeMs,
                CapturedAtLocalSeconds);

        public TournamentOverviewSnapshot WithClaimedRewards(IReadOnlyList<TournamentPlayerRewardView> claimed)
        {
            if (claimed == null || claimed.Count == 0) return this;
            var byKey = new Dictionary<(uint season, TournamentRewardTierKind tier), TournamentPlayerRewardView>(claimed.Count);
            for (int i = 0; i < claimed.Count; i++)
            {
                var c = claimed[i];
                if (c != null) byKey[(c.SeasonId, c.TierKind)] = c.Clone();
            }
            var updated = new List<TournamentPlayerRewardView>(MyRewards.Count);
            for (int i = 0; i < MyRewards.Count; i++)
            {
                var r = MyRewards[i];
                if (byKey.TryGetValue((r.SeasonId, r.TierKind), out var replacement))
                {
                    updated.Add(replacement);
                    byKey.Remove((r.SeasonId, r.TierKind));
                }
                else
                {
                    updated.Add(r.Clone());
                }
            }
            foreach (var leftover in byKey.Values) updated.Add(leftover);
            return new TournamentOverviewSnapshot(
                Season?.Clone(),
                MySquad?.Clone(),
                PendingInvites,
                MyActiveMatch?.Clone(),
                FeaturedMatches,
                MyCheeredSquadId,
                MyCheeredSquadName,
                RewardTiers,
                updated,
                HallOfFame,
                ServerTimeMs,
                CapturedAtLocalSeconds);
        }
    }

    /// <summary>V2 RPC 507/508 观战底部喝彩条的权威聚合快照（按 seq 增量合并，不本地臆造人气）。</summary>
    public sealed class TournamentSpectateCheerSnapshot
    {
        public const int MaxRetainedCheers = 32;

        public ulong BattleId { get; }
        public ulong CountA { get; }
        public ulong CountB { get; }
        public ulong CooldownUntilMs { get; }
        public IReadOnlyList<TournamentSpectateCheerItem> RecentCheers { get; }
        public ulong LatestSeq { get; }
        public ulong ServerTimeMs { get; }
        public float CapturedAtLocalSeconds { get; }

        public TournamentSpectateCheerSnapshot(
            ulong battleId,
            ulong countA,
            ulong countB,
            ulong cooldownUntilMs,
            IReadOnlyList<TournamentSpectateCheerItem> recentCheers,
            ulong serverTimeMs,
            float capturedAtLocalSeconds)
        {
            BattleId = battleId;
            CountA = countA;
            CountB = countB;
            CooldownUntilMs = cooldownUntilMs;
            RecentCheers = recentCheers ?? Array.Empty<TournamentSpectateCheerItem>();
            ulong maxSeq = 0UL;
            for (int i = 0; i < RecentCheers.Count; i++)
            {
                if (RecentCheers[i].Seq > maxSeq) maxSeq = RecentCheers[i].Seq;
            }
            LatestSeq = maxSeq;
            ServerTimeMs = serverTimeMs;
            CapturedAtLocalSeconds = capturedAtLocalSeconds;
        }

        public ulong EstimateServerNowMs(float nowLocalSeconds)
        {
            if (ServerTimeMs == 0UL) return 0UL;
            float delta = nowLocalSeconds - CapturedAtLocalSeconds;
            if (delta <= 0f) return ServerTimeMs;
            return ServerTimeMs + (ulong)(delta * 1000.0);
        }

        public ulong CooldownRemainingMs(float nowLocalSeconds)
        {
            if (CooldownUntilMs == 0UL) return 0UL;
            ulong nowMs = EstimateServerNowMs(nowLocalSeconds);
            return CooldownUntilMs > nowMs ? CooldownUntilMs - nowMs : 0UL;
        }

        public static TournamentSpectateCheerSnapshot Merge(
            TournamentSpectateCheerSnapshot previous,
            ulong battleId,
            ulong countA,
            ulong countB,
            ulong cooldownUntilMs,
            IList<TournamentSpectateCheerItem> incoming,
            ulong serverTimeMs,
            float localSeconds)
        {
            var merged = new List<TournamentSpectateCheerItem>();
            var seen = new HashSet<ulong>();
            if (previous != null && previous.BattleId == battleId)
            {
                for (int i = 0; i < previous.RecentCheers.Count; i++)
                {
                    var item = previous.RecentCheers[i];
                    if (item != null && seen.Add(item.Seq)) merged.Add(item);
                }
            }
            if (incoming != null)
            {
                for (int i = 0; i < incoming.Count; i++)
                {
                    var item = incoming[i];
                    if (item != null && seen.Add(item.Seq)) merged.Add(item.Clone());
                }
            }
            merged.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            if (merged.Count > MaxRetainedCheers)
                merged.RemoveRange(0, merged.Count - MaxRetainedCheers);
            ulong effCooldown = cooldownUntilMs;
            if (effCooldown == 0UL && previous != null && previous.BattleId == battleId && previous.CooldownUntilMs > serverTimeMs)
                effCooldown = previous.CooldownUntilMs;
            return new TournamentSpectateCheerSnapshot(
                battleId, countA, countB, effCooldown, merged, serverTimeMs, localSeconds);
        }
    }

    /// <summary>RPC 472 ListTournamentStandings 的不可变只读快照（海选积分榜 + 淘汰赛对阵树）。</summary>
    public sealed class TournamentStandingsSnapshot
    {
        public TournamentSeasonView Season { get; }
        public IReadOnlyList<TournamentSquadView> Standings { get; }
        public TournamentSquadView MySquad { get; }
        public uint TotalSquads { get; }
        public IReadOnlyList<TournamentMatchView> KnockoutBracket { get; }
        public ulong ServerTimeMs { get; }
        public uint Page { get; }
        public uint PageSize { get; }

        public TournamentStandingsSnapshot(
            TournamentSeasonView season,
            IReadOnlyList<TournamentSquadView> standings,
            TournamentSquadView mySquad,
            uint totalSquads,
            IReadOnlyList<TournamentMatchView> knockoutBracket,
            ulong serverTimeMs,
            uint page,
            uint pageSize)
        {
            Season = season;
            Standings = standings;
            MySquad = mySquad;
            TotalSquads = totalSquads;
            KnockoutBracket = knockoutBracket;
            ServerTimeMs = serverTimeMs;
            Page = page;
            PageSize = pageSize;
        }

        public static TournamentStandingsSnapshot FromResponse(ListTournamentStandingsResponse resp, uint page, uint pageSize)
        {
            if (resp == null) return null;
            var standings = new List<TournamentSquadView>(resp.Standings.Count);
            foreach (var sq in resp.Standings) if (sq != null) standings.Add(sq.Clone());

            var bracket = new List<TournamentMatchView>(resp.KnockoutBracket.Count);
            foreach (var m in resp.KnockoutBracket) if (m != null) bracket.Add(m.Clone());

            return new TournamentStandingsSnapshot(
                resp.Season?.Clone(),
                standings,
                resp.MySquad?.Clone(),
                resp.TotalSquads,
                bracket,
                resp.ServerTimeMs,
                page == 0 ? 1u : page,
                pageSize == 0 ? 20u : pageSize);
        }

        /// <summary>按淘汰赛轮次（1起）分列，方便 UI 动态渲染 2 / 4 / 8 / 16 强列数。</summary>
        public IReadOnlyList<IReadOnlyList<TournamentMatchView>> GroupKnockoutByRound()
        {
            if (KnockoutBracket.Count == 0) return Array.Empty<IReadOnlyList<TournamentMatchView>>();
            var byRound = new SortedDictionary<uint, List<TournamentMatchView>>();
            for (int i = 0; i < KnockoutBracket.Count; i++)
            {
                var m = KnockoutBracket[i];
                if (!byRound.TryGetValue(m.RoundIndex, out var list))
                {
                    list = new List<TournamentMatchView>();
                    byRound[m.RoundIndex] = list;
                }
                list.Add(m);
            }
            var columns = new List<IReadOnlyList<TournamentMatchView>>(byRound.Count);
            foreach (var kv in byRound)
            {
                kv.Value.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
                columns.Add(kv.Value);
            }
            return columns;
        }

        public TournamentStandingsSnapshot WithCheerCount(ulong squadId, uint cheerCount)
        {
            var nextList = new List<TournamentSquadView>(Standings.Count);
            for (int i = 0; i < Standings.Count; i++)
            {
                var sq = Standings[i].Clone();
                if (sq.SquadId == squadId) sq.CheerCount = cheerCount;
                nextList.Add(sq);
            }
            var nextMine = MySquad?.Clone();
            if (nextMine != null && nextMine.SquadId == squadId) nextMine.CheerCount = cheerCount;
            return new TournamentStandingsSnapshot(
                Season?.Clone(),
                nextList,
                nextMine,
                TotalSquads,
                KnockoutBracket,
                ServerTimeMs,
                Page,
                PageSize);
        }

        /// <summary>V3：服务端确认免费竞猜后同步更新对阵树节点选中态与双方预测票数。</summary>
        public TournamentStandingsSnapshot WithMatchPrediction(
            ulong matchId, ulong predictedSquadId, uint predictCountA, uint predictCountB)
        {
            if (matchId == 0UL || KnockoutBracket.Count == 0) return this;
            var nextBracket = new List<TournamentMatchView>(KnockoutBracket.Count);
            for (int i = 0; i < KnockoutBracket.Count; i++)
            {
                var m = KnockoutBracket[i].Clone();
                if (m.MatchId == matchId)
                {
                    m.MyPredictedSquadId = predictedSquadId;
                    m.PredictCountA = predictCountA;
                    m.PredictCountB = predictCountB;
                }
                nextBracket.Add(m);
            }
            return new TournamentStandingsSnapshot(
                Season?.Clone(),
                Standings,
                MySquad?.Clone(),
                TotalSquads,
                nextBracket,
                ServerTimeMs,
                Page,
                PageSize);
        }

        /// <summary>V4 模块 H：服务端确认 BO3 灵脉禁选后同步更新对阵树中的对应对局快照。</summary>
        public TournamentStandingsSnapshot WithUpdatedMatch(TournamentMatchView updated)
        {
            if (updated == null || updated.MatchId == 0UL || KnockoutBracket.Count == 0) return this;
            var nextBracket = new List<TournamentMatchView>(KnockoutBracket.Count);
            for (int i = 0; i < KnockoutBracket.Count; i++)
            {
                var cur = KnockoutBracket[i];
                nextBracket.Add(cur != null && cur.MatchId == updated.MatchId ? updated.Clone() : cur.Clone());
            }
            return new TournamentStandingsSnapshot(
                Season?.Clone(),
                Standings,
                MySquad?.Clone(),
                TotalSquads,
                nextBracket,
                ServerTimeMs,
                Page,
                PageSize);
        }
    }

    /// <summary>V4 模块 I GetTournamentHighlights 的不可变只读快照（《本季名局录》Top 10）。</summary>
    public sealed class TournamentHighlightsSnapshot
    {
        public uint SeasonId { get; }
        public IReadOnlyList<TournamentHighlightEntry> Highlights { get; }
        public ulong ServerTimeMs { get; }

        public TournamentHighlightsSnapshot(
            uint seasonId,
            IReadOnlyList<TournamentHighlightEntry> highlights,
            ulong serverTimeMs)
        {
            SeasonId = seasonId;
            Highlights = highlights ?? Array.Empty<TournamentHighlightEntry>();
            ServerTimeMs = serverTimeMs;
        }

        public static TournamentHighlightsSnapshot FromResponse(GetTournamentHighlightsResponse resp)
        {
            if (resp == null) return null;
            var list = new List<TournamentHighlightEntry>(resp.Highlights.Count);
            for (int i = 0; i < resp.Highlights.Count; i++)
            {
                if (resp.Highlights[i] != null) list.Add(resp.Highlights[i].Clone());
            }
            return new TournamentHighlightsSnapshot(resp.SeasonId, list, resp.ServerTimeMs);
        }
    }

    /// <summary>V3 模块 E GetTournamentPredictionBoard 的不可变只读快照（《神机问道榜》）。</summary>
    public sealed class TournamentPredictionBoardSnapshot
    {
        public uint SeasonId { get; }
        public uint MinHitsForReward { get; }
        public uint MyHitCount { get; }
        public uint MyTotalPoints { get; }
        public uint MyRank { get; }
        public IReadOnlyList<TournamentPredictionEntryView> MyPredictions { get; }
        public IReadOnlyList<TournamentPredictionRankEntry> TopPredictors { get; }
        public ulong ServerTimeMs { get; }

        public TournamentPredictionBoardSnapshot(
            uint seasonId,
            uint minHitsForReward,
            uint myHitCount,
            uint myTotalPoints,
            uint myRank,
            IReadOnlyList<TournamentPredictionEntryView> myPredictions,
            IReadOnlyList<TournamentPredictionRankEntry> topPredictors,
            ulong serverTimeMs)
        {
            SeasonId = seasonId;
            MinHitsForReward = minHitsForReward;
            MyHitCount = myHitCount;
            MyTotalPoints = myTotalPoints;
            MyRank = myRank;
            MyPredictions = myPredictions ?? Array.Empty<TournamentPredictionEntryView>();
            TopPredictors = topPredictors ?? Array.Empty<TournamentPredictionRankEntry>();
            ServerTimeMs = serverTimeMs;
        }

        public static TournamentPredictionBoardSnapshot FromResponse(GetTournamentPredictionBoardResponse resp)
        {
            if (resp == null) return null;
            var mine = new List<TournamentPredictionEntryView>(resp.MyPredictions.Count);
            for (int i = 0; i < resp.MyPredictions.Count; i++)
                if (resp.MyPredictions[i] != null) mine.Add(resp.MyPredictions[i].Clone());
            var top = new List<TournamentPredictionRankEntry>(resp.TopPredictors.Count);
            for (int i = 0; i < resp.TopPredictors.Count; i++)
                if (resp.TopPredictors[i] != null) top.Add(resp.TopPredictors[i].Clone());
            return new TournamentPredictionBoardSnapshot(
                resp.SeasonId,
                resp.MinHitsForReward,
                resp.MyHitCount,
                resp.MyTotalPoints,
                resp.MyRank,
                mine,
                top,
                resp.ServerTimeMs);
        }
    }
}
