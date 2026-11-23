using System;
using System.Globalization;

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

    /// <summary>
    /// 本机最佳次数的版本化文本编解码器：格式为 <c>1|&lt;bestFlips&gt;</c>。
    /// 任何版本不匹配、格式错误、非正整数或小于理论最少次数（6）的值都按损坏处理。
    /// </summary>
    public static class PetFlipBestRecordCodec
    {
        public const string FormatVersion = "1";

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

        /// <summary>解码原始记录文本；缺失或损坏时返回 false。</summary>
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
