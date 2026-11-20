using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 单玩家一个待确认槽。独占锁、落盘刷新与同目录原子提交；未知记录不能被另一请求覆盖。
    /// <para>
    /// 槽位属于「角色 + 写入时的归属修订号」。构造时给了修订号来源(ownerRevision,取 GameClient.OwnerRevision)的存储,
    /// 把写入时的修订号记在文件首行。读到的记录与当前修订号都已知且不同,说明角色换过主人:过户前服务端要求这名角色的
    /// 在途操作全部了结,所以这条记录已经作废,一律按不存在处理,不恢复也不重发。任一方未知(0、旧格式文件)时照旧保留,
    /// 不会把本人的记录藏起来。
    /// </para>
    /// </summary>
    public sealed class DurableRequestFileStore
    {
        private const string OwnerPrefix = "@owner-revision:";
        private readonly string _directory;
        private readonly Func<ulong> _ownerRevision;
        public DurableRequestFileStore(string directory, Func<ulong> ownerRevision = null)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _ownerRevision = ownerRevision;
        }
        public static DurableRequestFileStore ForOrigin(string directory,string gatewayBaseUrl,Func<ulong> ownerRevision=null)
        {
            using var hash=SHA256.Create();
            string origin=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(gatewayBaseUrl.TrimEnd('/')))).Replace("-","");
            return new DurableRequestFileStore(Path.Combine(directory,origin),ownerRevision);
        }
        private string PathFor(ulong player) => Path.Combine(_directory, player.ToString(CultureInfo.InvariantCulture) + ".pending");

        private ulong CurrentRevision => _ownerRevision?.Invoke() ?? 0;

        /// <summary>拆出首行记的修订号,返回正文。没有首行标记(旧格式,或写入时修订号未知)时 recorded 为 0、正文就是全文。</summary>
        private static string Unwrap(string text, out ulong recorded)
        {
            recorded = 0;
            if (text == null || !text.StartsWith(OwnerPrefix, StringComparison.Ordinal)) return text;
            int end = text.IndexOf('\n');
            if (end < 0 || !ulong.TryParse(text.Substring(OwnerPrefix.Length, end - OwnerPrefix.Length), NumberStyles.None,
                    CultureInfo.InvariantCulture, out recorded) || recorded == 0)
            {
                // 首行不合规:不猜,整份原样交给调用方,由它按损坏处理。
                recorded = 0;
                return text;
            }
            return text.Substring(end + 1);
        }

        /// <summary>这条记录是角色换主人之前留下的(两边修订号都已知且不同)。</summary>
        private static bool Superseded(ulong recorded, ulong current) => current != 0 && recorded != 0 && recorded != current;

        private static ulong ReadRecorded(string path, out string value)
        {
            value = Unwrap(File.ReadAllText(path, Encoding.UTF8), out ulong recorded);
            return recorded;
        }

        /// <summary>同一存储实例、角色与原始字节的快照；Value 只供解析，不能作为删除凭证。</summary>
        public sealed class Snapshot
        {
            private readonly DurableRequestFileStore _owner;
            private readonly ulong _player;
            private readonly string _path;
            private readonly long _length;
            private readonly byte[] _digest;
            public string Value { get; }

            internal Snapshot(DurableRequestFileStore owner, ulong player, string path, long length, byte[] digest, string value)
            {
                _owner = owner; _player = player; _path = path; _length = length;
                _digest = (byte[])digest.Clone(); Value = value;
            }

            internal bool BelongsTo(DurableRequestFileStore owner, ulong player, string path) =>
                ReferenceEquals(_owner, owner) && _player == player && _path == path;

            public bool Matches(Snapshot other)
            {
                if (other == null || !other.BelongsTo(_owner, _player, _path)
                    || _length != other._length || _digest.Length != other._digest.Length) return false;
                for (int i = 0; i < _digest.Length; i++)
                    if (_digest[i] != other._digest[i]) return false;
                return true;
            }
        }

        /// <summary>
        /// 手动恢复时取得用于判坏与确认删除的同一份字节。超长文件仍流式扫描完整内容，内存有界，耗时随文件大小增长。
        /// 不存在返回 null；锁冲突、读取和权限故障抛出，不能把“无法读取”当成已获准删除的记录。
        /// </summary>
        public Snapshot ReadSnapshot(ulong player)
        {
            if (player == 0) throw new ArgumentOutOfRangeException(nameof(player));
            string target = Path.GetFullPath(PathFor(player));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using var guard = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return ReadSnapshotUnderLock(player, target);
        }

        /// <summary>只删除玩家已经确认的原始内容；快照跨角色、跨存储实例或内容被替换时均拒绝。</summary>
        public void ClearSnapshot(ulong player, Snapshot expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            string target = Path.GetFullPath(PathFor(player));
            if (!expected.BelongsTo(this, player, target)) throw new ArgumentException("快照不属于当前角色或存储", nameof(expected));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using var guard = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Snapshot current = ReadSnapshotUnderLock(player, target);
            if (current == null) return;
            if (!expected.Matches(current)) throw new InvalidOperationException("待确认记录已被替换");
            // 读取句柄已关闭，否则 Windows 的 FileShare.Read 会挡住自己的删除。
            // 比较至删除始终持 .lock，和 Save / Transition / Clear 共用同库写者协议；不承诺阻止绕锁的外部文件操作。
            File.Delete(target);
        }

        private Snapshot ReadSnapshotUnderLock(ulong player, string target)
        {
            FileStream input;
            try { input = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            long length;
            byte[] digest;
            string value = "!损坏";
            ulong recorded = 0;
            using (input)
            using (var hash = SHA256.Create())
            {
                length = input.Length;
                digest = hash.ComputeHash(input);
                if (length > 0 && length <= 65536)
                {
                    input.Position = 0;
                    try
                    {
                        // 只接受 UTF8，兼容其 BOM；禁用其他编码探测，避免不同原始字节被容错解码成同一条记录。
                        using var reader = new StreamReader(input, new UTF8Encoding(true, true), false, 4096, true);
                        string text = Unwrap(reader.ReadToEnd(), out recorded);
                        if (!string.IsNullOrEmpty(text)) value = text;
                    }
                    catch (DecoderFallbackException) { value = "!损坏"; }
                }
            }
            // 换主人之前留下的记录已作废:两个调用方都持着 .lock,就地删掉,按不存在回。读取句柄此时已关闭。
            if (Superseded(recorded, CurrentRevision)) { File.Delete(target); return null; }
            return new Snapshot(this, player, target, length, digest, value);
        }

        public string Load(ulong player)
        {
            try
            {
                var path = PathFor(player);
                if (!File.Exists(path)) return "";
                if (new FileInfo(path).Length > 65536) return "!损坏";
                // 不持锁,只读不删:作废记录留给下一次 Save / Clear / ReadSnapshot 在锁内处理。
                if (Superseded(ReadRecorded(path, out string saved), CurrentRevision)) return "";
                return string.IsNullOrEmpty(saved) ? "!损坏" : saved;
            }
            catch (IOException) { return "!无法读取"; }
            catch (UnauthorizedAccessException) { return "!无法读取"; }
        }
        public void Save(ulong player, string value) => Commit(player, value, null);
        public void Transition(ulong player, string expected, string value)
        {
            if (string.IsNullOrEmpty(expected)) throw new ArgumentException("缺少原待确认记录");
            Commit(player, value, expected);
        }
        private void Commit(ulong player, string value, string expected)
        {
            ulong revision = CurrentRevision;
            // 修订号已知时记在首行;正文不得以首行标记开头,否则读回时会被当成修订号。
            string content = revision == 0 ? value : OwnerPrefix + revision.ToString(CultureInfo.InvariantCulture) + "\n" + value;
            if (player == 0 || string.IsNullOrEmpty(value) || value.StartsWith(OwnerPrefix, StringComparison.Ordinal)
                || Encoding.UTF8.GetByteCount(content) > 65536) throw new InvalidDataException("恢复记录越界");
            Directory.CreateDirectory(_directory);
            var target = PathFor(player); var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using var guard = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            bool onDisk = File.Exists(target);
            string current = null;
            // 换主人之前留下的记录已作废:当它不存在,新记录直接顶替。
            bool exists = onDisk && !Superseded(ReadRecorded(target, out current), revision);
            if (exists)
            {
                if (current == value) return;
                if (expected == null || current != expected) throw new InvalidOperationException("已有另一笔待确认请求");
            }
            else if (expected != null) throw new InvalidOperationException("原待确认记录已变化");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    var data = Encoding.UTF8.GetBytes(content); output.Write(data, 0, data.Length); output.Flush(true);
                }
                if (onDisk) File.Replace(temporary, target, null); else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Clear(ulong player, string expected = null)
        {
            Directory.CreateDirectory(_directory); var target = PathFor(player);
            using var guard = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            // 换主人之前留下的记录已作废,不参与比对,直接删。
            if (expected != null && File.Exists(target)
                && !Superseded(ReadRecorded(target, out string current), CurrentRevision) && current != expected)
                throw new InvalidOperationException("待确认记录已被替换");
            File.Delete(target);
        }
    }
}
