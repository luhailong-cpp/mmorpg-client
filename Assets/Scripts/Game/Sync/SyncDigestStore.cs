using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MmorpgClient.Game.Sync
{
    /// <summary>
    /// 一个玩家的本地摘要缓存(服务端 docs/design/landing-recovery-sync-digest.md §4.3 第 3 条):摘要代次 g,
    /// 以及各域「已经拉到」的变更计数。它只是提示、不是数据:丢了、坏了、读不到都按「没有缓存」处理(全量拉一次)。
    /// 不存任何 secret。域按 proto 域号(int)存,本文件不依赖生成的 SyncDomain 枚举。
    /// </summary>
    public sealed class SyncDigestRecord
    {
        private readonly Dictionary<int, ulong> _counts = new Dictionary<int, ulong>();

        public SyncDigestRecord(ulong generation)
        {
            Generation = generation;
        }

        /// <summary>摘要代次 g;0 = 服务端当时没有摘要(从未 bump 或已过期)。</summary>
        public ulong Generation { get; }

        /// <summary>域号 → 已拉到的计数。没有记录的域在比对时一律算「脏」。</summary>
        public IReadOnlyDictionary<int, ulong> Counts => _counts;

        public bool TryGetCount(int domain, out ulong count) => _counts.TryGetValue(domain, out count);

        public void SetCount(int domain, ulong count)
        {
            if (domain <= 0) throw new ArgumentOutOfRangeException(nameof(domain), domain, "域号必须为正");
            _counts[domain] = count;
        }
    }

    /// <summary>按玩家分键的本地摘要缓存。实现必须容错:读不到、读失败、内容损坏一律返回 null。</summary>
    public interface ISyncDigestStore
    {
        /// <returns>没有缓存、读失败或内容损坏时返回 null。</returns>
        SyncDigestRecord Load(ulong playerId);

        /// <summary>整份覆盖写入。写失败不抛:最坏是下次落地多拉一次。</summary>
        void Save(ulong playerId, SyncDigestRecord record);

        /// <summary>删除该玩家的缓存(摘要不可用时作废整份缓存)。</summary>
        void Delete(ulong playerId);
    }

    /// <summary>
    /// 缓存的文本编码:<c>1|&lt;g&gt;|&lt;域号&gt;:&lt;计数&gt;,&lt;域号&gt;:&lt;计数&gt;</c>(十进制,域号升序)。
    /// 开头的 1 是格式版本;认不出的版本、任何解析失败都按「没有缓存」处理,不做部分恢复。
    /// </summary>
    public static class SyncDigestCodec
    {
        private const string FormatVersion = "1";

        public static string Encode(SyncDigestRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var domains = new List<int>(record.Counts.Keys);
            domains.Sort();
            var text = new StringBuilder();
            text.Append(FormatVersion).Append('|')
                .Append(record.Generation.ToString(CultureInfo.InvariantCulture)).Append('|');
            for (int i = 0; i < domains.Count; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(domains[i].ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(record.Counts[domains[i]].ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        /// <returns>解析失败返回 null。</returns>
        public static SyncDigestRecord Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] parts = text.Split('|');
            if (parts.Length != 3 || parts[0] != FormatVersion) return null;
            if (!ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong generation))
                return null;
            var record = new SyncDigestRecord(generation);
            if (parts[2].Length == 0) return record;
            foreach (string pair in parts[2].Split(','))
            {
                int colon = pair.IndexOf(':');
                if (colon <= 0) return null;
                if (!int.TryParse(pair.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int domain)
                    || domain <= 0)
                    return null;
                if (!ulong.TryParse(pair.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out ulong count))
                    return null;
                record.SetCount(domain, count);
            }
            return record;
        }
    }

    /// <summary>
    /// PlayerPrefs 实现,键 <c>mmorpg.syncdigest.{playerId}</c>(与 ClientSettings 同一个 "mmorpg." 命名空间)。
    /// PlayerPrefs 是明文存储,这里只放摘要代次与计数。所有读写都吞掉异常(非主线程调用、配额超限等),
    /// 读失败按没有缓存、写失败只会让下次落地多拉。
    /// </summary>
    public sealed class PlayerPrefsSyncDigestStore : ISyncDigestStore
    {
        public const string KeyPrefix = "mmorpg.syncdigest.";

        public static string KeyFor(ulong playerId) => KeyPrefix + playerId.ToString(CultureInfo.InvariantCulture);

        public SyncDigestRecord Load(ulong playerId)
        {
            try
            {
                return SyncDigestCodec.Decode(PlayerPrefs.GetString(KeyFor(playerId), ""));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Save(ulong playerId, SyncDigestRecord record)
        {
            if (record == null) return;
            try
            {
                PlayerPrefs.SetString(KeyFor(playerId), SyncDigestCodec.Encode(record));
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // 写失败:磁盘上仍是上一份(同样不领先于已拉到的数据),下次落地多拉而已。
            }
        }

        public void Delete(ulong playerId)
        {
            try
            {
                PlayerPrefs.DeleteKey(KeyFor(playerId));
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // 删除失败:下次落地读到旧缓存,按代次 / 计数比对,最坏多拉。
            }
        }
    }
}
