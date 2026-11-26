using System;
using System.Globalization;

namespace MmorpgClient.Game.CoreLoop
{
    /// <summary>角色当前等级、累计经验与未分配属性点的只读快照（纯 C#，零 UnityEngine 依赖，WebGL2/IL2CPP 安全）。</summary>
    public sealed class PlayerExpSnapshot
    {
        public uint Level { get; }
        public ulong TotalExp { get; }
        public ulong CurrentLevelExp { get; }
        public ulong NextLevelExp { get; }
        public bool IsMaxLevel { get; }
        public uint UnallocatedPoints { get; }
        public double ProgressRatio { get; }

        public PlayerExpSnapshot(
            uint level,
            ulong totalExp,
            ulong currentLevelExp,
            ulong nextLevelExp,
            bool isMaxLevel,
            uint unallocatedPoints)
        {
            Level = level;
            TotalExp = totalExp;
            CurrentLevelExp = currentLevelExp;
            NextLevelExp = nextLevelExp;
            IsMaxLevel = isMaxLevel;
            UnallocatedPoints = unallocatedPoints;
            if (isMaxLevel)
            {
                ProgressRatio = 1.0;
            }
            else if (nextLevelExp == 0ul)
            {
                ProgressRatio = 0.0;
            }
            else
            {
                double ratio = (double)currentLevelExp / (double)nextLevelExp;
                ProgressRatio = ratio < 0.0 ? 0.0 : (ratio > 1.0 ? 1.0 : ratio);
            }
        }

        public string ExpProgressText => IsMaxLevel
            ? string.Format(CultureInfo.InvariantCulture, "已满级 · 累计经验 {0:N0}", TotalExp)
            : NextLevelExp > 0ul
                ? string.Format(CultureInfo.InvariantCulture, "经验 {0:N0} / {1:N0}", CurrentLevelExp, NextLevelExp)
                : string.Format(CultureInfo.InvariantCulture, "累计经验 {0:N0}", TotalExp);

        public string CompactExpText => IsMaxLevel
            ? "已满级"
            : NextLevelExp > 0ul
                ? string.Format(CultureInfo.InvariantCulture, "经验 {0:N0}/{1:N0}", CurrentLevelExp, NextLevelExp)
                : string.Format(CultureInfo.InvariantCulture, "经验 {0:N0}", TotalExp);
    }

    /// <summary>单局战斗结算的经验、银两与升级只读展示视图（同时适配现场 BattleEndS2C 与重登补查 PlayerBattleResult）。</summary>
    public sealed class SettlementProgressionView
    {
        public uint LevelBefore { get; }
        public uint LevelAfter { get; }
        public ulong ExpGain { get; }
        public ulong SilverGain { get; }
        public ulong TotalExpAfter { get; }
        public ulong NextLevelTotalExp { get; }
        public bool SilverDiminished { get; }

        public bool LeveledUp => LevelAfter > LevelBefore && LevelBefore > 0u;
        public bool HasVisibleRewards => ExpGain > 0ul || SilverGain > 0ul || LeveledUp;

        public SettlementProgressionView(
            uint levelBefore,
            uint levelAfter,
            ulong expGain,
            ulong silverGain,
            ulong totalExpAfter,
            ulong nextLevelTotalExp,
            bool silverDiminished)
        {
            LevelBefore = levelBefore;
            LevelAfter = levelAfter;
            ExpGain = expGain;
            SilverGain = silverGain;
            TotalExpAfter = totalExpAfter;
            NextLevelTotalExp = nextLevelTotalExp;
            SilverDiminished = silverDiminished;
        }

        public string RewardSummaryLine
        {
            get
            {
                string expPart = ExpGain > 0ul
                    ? string.Format(CultureInfo.InvariantCulture, "经验 +{0:N0}", ExpGain)
                    : string.Empty;
                string silverPart = SilverGain > 0ul
                    ? string.Format(CultureInfo.InvariantCulture, "银两 +{0:N0}", SilverGain)
                    : (SilverDiminished ? "银两 +0" : string.Empty);

                string joined;
                if (!string.IsNullOrEmpty(expPart) && !string.IsNullOrEmpty(silverPart))
                    joined = expPart + " · " + silverPart;
                else if (!string.IsNullOrEmpty(expPart))
                    joined = expPart;
                else
                    joined = silverPart;

                if (SilverDiminished)
                    joined += "（本周银两已触发衰减）";
                return joined;
            }
        }

        public string LevelUpBannerLine => LeveledUp
            ? string.Format(CultureInfo.InvariantCulture, "角色升级：Lv.{0} → Lv.{1}", LevelBefore, LevelAfter)
            : string.Empty;
    }

    /// <summary>角色成长曲线与结算经验/银两视图构造器（纯函数，绝不在客户端自行推算等级或衰减）。</summary>
    public static class PlayerProgressionModels
    {
        /// <summary>货币类型：银两（与服务端 CurrencyType=0 / Wallet.GOLD 对齐）。</summary>
        public const uint CurrencyTypeSilver = 0u;

        public static PlayerExpSnapshot BuildExpSnapshot(AttributePanelInfo panel)
        {
            if (panel == null) return null;
            ulong unallocated = 0ul;
            if (panel.Pools != null)
            {
                for (int i = 0; i < panel.Pools.Count; i++)
                {
                    var pool = panel.Pools[i];
                    if (pool != null && pool.Unlocked)
                    {
                        unallocated += pool.Remaining;
                    }
                }
            }
            uint clampedUnallocated = unallocated > uint.MaxValue ? uint.MaxValue : (uint)unallocated;
            return new PlayerExpSnapshot(
                panel.Level,
                panel.TotalExp,
                panel.CurrentLevelExp,
                panel.NextLevelExp,
                panel.IsMaxLevel,
                clampedUnallocated);
        }

        public static SettlementProgressionView BuildSettlementView(BattleEndS2C end)
        {
            if (end == null) return null;
            var view = new SettlementProgressionView(
                end.LevelBefore,
                end.LevelAfter,
                end.ExpGain,
                end.SilverGain,
                end.TotalExpAfter,
                end.NextLevelTotalExp,
                end.BattleSilverDiminished);
            return view.HasVisibleRewards ? view : null;
        }

        public static SettlementProgressionView BuildSettlementView(PlayerBattleResult result)
        {
            if (result == null) return null;
            ulong silverGain = 0ul;
            if (result.Currencies != null)
            {
                for (int i = 0; i < result.Currencies.Count; i++)
                {
                    var c = result.Currencies[i];
                    if (c != null && c.CurrencyType == CurrencyTypeSilver)
                    {
                        ulong room = ulong.MaxValue - silverGain;
                        silverGain += c.Amount > room ? room : c.Amount;
                    }
                }
            }
            var view = new SettlementProgressionView(
                result.LevelBefore,
                result.LevelAfter,
                result.ExpGain,
                silverGain,
                result.TotalExpAfter,
                result.NextLevelTotalExp,
                result.BattleSilverDiminished);
            return view.HasVisibleRewards ? view : null;
        }
    }
}
