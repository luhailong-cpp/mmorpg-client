using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MmorpgClient.Game.PetFlip
{
    /// <summary>
    /// 本机最佳次数持久化接缝。只读写单条非敏感字符串，不保存整副牌、账号、密码或令牌。
    /// 实现必须容错：读失败返回 false，写失败返回 false，不抛出异常阻塞游玩。
    /// </summary>
    public interface IPetFlipBestRecordStore
    {
        /// <summary>读取原始编码串。不存在时返回 true 且 rawValue 为空；存储读取出错返回 false。</summary>
        bool TryReadRaw(out string rawValue);

        /// <summary>保存原始编码串。写入成功返回 true；失败返回 false。</summary>
        bool TryWriteRaw(string rawValue);
    }

    /// <summary>单桶持久化最佳记录（今日灵签额外携带 yyyyMMdd 日期键）。</summary>
    public readonly struct PetFlipStoredBestEntry
    {
        public int BestFlipCount { get; }
        /// <summary>今日灵签模式记录所属的本地公历日期键（yyyyMMdd）；永久记录为 0。</summary>
        public int DateKey { get; }

        public PetFlipStoredBestEntry(int bestFlipCount, int dateKey = 0)
        {
            BestFlipCount = bestFlipCount;
            DateKey = dateKey;
        }
    }

    /// <summary>
    /// 本机最佳次数的版本化文本编解码器：
    /// - V1 格式：<c>1|&lt;bestFlips&gt;</c>（仅轻松 12 张经典模式存在记录时沿用，保持 100% 向后兼容）；
    /// - V2 格式：<c>2|&lt;diff&gt;:&lt;mode&gt;=&lt;bestFlips&gt;[@&lt;yyyyMMdd&gt;],...</c>（支持多难度与多模式分桶独立存档）。
    /// 任何版本不匹配、格式错误、非法枚举或小于该难度理论最少次数（6 / 8）的值均按损坏处理。
    /// </summary>
    public static class PetFlipBestRecordCodec
    {
        public const string FormatVersion = "1";
        public const string FormatVersionV2 = "2";

        public static string Encode(int bestFlipCount)
        {
            if (bestFlipCount < PetFlipConstants.MinimumPossibleFlips)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bestFlipCount),
                    bestFlipCount,
                    "最佳翻牌次数不能小于 6。");
            }

            return FormatVersion + "|" + bestFlipCount.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>解码 V1 单桶记录；缺失或损坏返回 false。</summary>
        public static bool TryDecode(string rawValue, out int bestFlipCount)
        {
            bestFlipCount = 0;
            if (string.IsNullOrEmpty(rawValue))
                return false;

            string[] parts = rawValue.Split('|');
            if (parts.Length != 2 || !string.Equals(parts[0], FormatVersion, StringComparison.Ordinal))
                return false;

            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
                return false;

            if (parsed < PetFlipConstants.MinimumPossibleFlips)
                return false;

            bestFlipCount = parsed;
            return true;
        }

        /// <summary>编码多桶记录；若仅包含默认轻松 12 张经典纪录，输出 V1 精简格式以保持完全兼容。</summary>
        public static string EncodeTable(IReadOnlyDictionary<PetFlipRecordBucketKey, PetFlipStoredBestEntry> records)
        {
            if (records == null || records.Count == 0)
                throw new ArgumentException("记录表不能为空。", nameof(records));

            var defaultKey = new PetFlipRecordBucketKey(PetFlipDifficulty.Relaxed12, PetFlipGameMode.Classic);
            if (records.Count == 1 && records.TryGetValue(defaultKey, out PetFlipStoredBestEntry single) && single.DateKey == 0)
                return Encode(single.BestFlipCount);

            var keys = new List<PetFlipRecordBucketKey>(records.Keys);
            keys.Sort();

            var sb = new StringBuilder();
            sb.Append(FormatVersionV2).Append('|');
            for (int i = 0; i < keys.Count; i++)
            {
                PetFlipRecordBucketKey key = keys[i];
                PetFlipStoredBestEntry entry = records[key];
                int minFlips = PetFlipConstants.GetMinimumFlips(key.Difficulty);
                if (entry.BestFlipCount < minFlips)
                    throw new ArgumentOutOfRangeException(nameof(records), $"桶 ({key.Difficulty},{key.Mode}) 的最佳次数不能小于 {minFlips}。");

                if (i > 0) sb.Append(',');
                sb.Append(((int)key.Difficulty).ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(((int)key.Mode).ToString(CultureInfo.InvariantCulture))
                    .Append('=')
                    .Append(entry.BestFlipCount.ToString(CultureInfo.InvariantCulture));
                if (key.Mode == PetFlipGameMode.DailyChallenge && entry.DateKey > 0)
                    sb.Append('@').Append(entry.DateKey.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>兼容解码 V1 与 V2 文本；任意字段损坏整份判定失败（按暂无纪录兜底）。</summary>
        public static bool TryDecodeTable(
            string rawValue,
            out Dictionary<PetFlipRecordBucketKey, PetFlipStoredBestEntry> records)
        {
            records = null;
            if (string.IsNullOrEmpty(rawValue))
                return false;

            if (TryDecode(rawValue, out int legacyBest))
            {
                records = new Dictionary<PetFlipRecordBucketKey, PetFlipStoredBestEntry>
                {
                    [new PetFlipRecordBucketKey(PetFlipDifficulty.Relaxed12, PetFlipGameMode.Classic)] =
                        new PetFlipStoredBestEntry(legacyBest)
                };
                return true;
            }

            string[] parts = rawValue.Split('|');
            if (parts.Length != 2 || !string.Equals(parts[0], FormatVersionV2, StringComparison.Ordinal) || parts[1].Length == 0)
                return false;

            var parsedMap = new Dictionary<PetFlipRecordBucketKey, PetFlipStoredBestEntry>();
            string[] items = parts[1].Split(',');
            for (int i = 0; i < items.Length; i++)
            {
                string item = items[i];
                int eq = item.IndexOf('=');
                int colon = eq > 0 ? item.IndexOf(':', 0, eq) : -1;
                if (colon <= 0 || eq <= colon + 1 || eq >= item.Length - 1)
                    return false;

                if (!int.TryParse(item.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int diffRaw)
                    || (diffRaw != (int)PetFlipDifficulty.Relaxed12 && diffRaw != (int)PetFlipDifficulty.Advanced16))
                    return false;

                if (!int.TryParse(item.Substring(colon + 1, eq - colon - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int modeRaw)
                    || (modeRaw != (int)PetFlipGameMode.Classic && modeRaw != (int)PetFlipGameMode.DailyChallenge && modeRaw != (int)PetFlipGameMode.SpiritEye))
                    return false;

                string valuePart = item.Substring(eq + 1);
                int at = valuePart.IndexOf('@');
                int flips;
                int dateKey = 0;
                if (at >= 0)
                {
                    if ((PetFlipGameMode)modeRaw != PetFlipGameMode.DailyChallenge)
                        return false;
                    if (!int.TryParse(valuePart.Substring(0, at), NumberStyles.None, CultureInfo.InvariantCulture, out flips))
                        return false;
                    string datePart = valuePart.Substring(at + 1);
                    if (datePart.Length != 8 || !int.TryParse(datePart, NumberStyles.None, CultureInfo.InvariantCulture, out dateKey) || dateKey < 19700101)
                        return false;
                }
                else
                {
                    if (!int.TryParse(valuePart, NumberStyles.None, CultureInfo.InvariantCulture, out flips))
                        return false;
                }

                var difficulty = (PetFlipDifficulty)diffRaw;
                var mode = (PetFlipGameMode)modeRaw;
                if (flips < PetFlipConstants.GetMinimumFlips(difficulty))
                    return false;

                var key = new PetFlipRecordBucketKey(difficulty, mode);
                if (parsedMap.ContainsKey(key))
                    return false;

                parsedMap[key] = new PetFlipStoredBestEntry(flips, dateKey);
            }

            if (parsedMap.Count == 0)
                return false;

            records = parsedMap;
            return true;
        }
    }

    /// <summary>内存版最佳次数存储，供 EditMode 测试及无持久化沙盒使用。</summary>
    public sealed class InMemoryPetFlipBestRecordStore : IPetFlipBestRecordStore
    {
        public string RawValue { get; set; }
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }
        public int ReadCallCount { get; private set; }
        public int WriteCallCount { get; private set; }

        public InMemoryPetFlipBestRecordStore(string initialRawValue = null)
        {
            RawValue = initialRawValue;
        }

        public bool TryReadRaw(out string rawValue)
        {
            ReadCallCount++;
            if (FailReads)
            {
                rawValue = null;
                return false;
            }

            rawValue = RawValue;
            return true;
        }

        public bool TryWriteRaw(string rawValue)
        {
            WriteCallCount++;
            if (FailWrites)
                return false;

            RawValue = rawValue;
            return true;
        }
    }
}
