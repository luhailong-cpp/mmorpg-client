using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace MmorpgClient.Net
{
    /// <summary>单玩家一个待确认槽。独占锁、落盘刷新与同目录原子提交；未知记录不能被另一请求覆盖。</summary>
    public sealed class DurableRequestFileStore
    {
        private readonly string _directory;
        public DurableRequestFileStore(string directory) { _directory = directory ?? throw new ArgumentNullException(nameof(directory)); }
        public static DurableRequestFileStore ForOrigin(string directory,string gatewayBaseUrl)
        {
            using var hash=SHA256.Create();
            string origin=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(gatewayBaseUrl.TrimEnd('/')))).Replace("-","");
            return new DurableRequestFileStore(Path.Combine(directory,origin));
        }
        private string PathFor(ulong player) => Path.Combine(_directory, player.ToString(CultureInfo.InvariantCulture) + ".pending");
        public string Load(ulong player)
        {
            try
            {
                var path = PathFor(player);
                if (!File.Exists(path)) return "";
                if (new FileInfo(path).Length > 65536) return "!损坏";
                var saved = File.ReadAllText(path, Encoding.UTF8);
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
            if (player == 0 || string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > 65536) throw new InvalidDataException("恢复记录越界");
            Directory.CreateDirectory(_directory);
            var target = PathFor(player); var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using var guard = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            bool exists = File.Exists(target);
            if (exists)
            {
                string current = File.ReadAllText(target, Encoding.UTF8);
                if (current == value) return;
                if (expected == null || current != expected) throw new InvalidOperationException("已有另一笔待确认请求");
            }
            else if (expected != null) throw new InvalidOperationException("原待确认记录已变化");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    var data = Encoding.UTF8.GetBytes(value); output.Write(data, 0, data.Length); output.Flush(true);
                }
                if (exists) File.Replace(temporary, target, null); else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Clear(ulong player, string expected = null)
        {
            Directory.CreateDirectory(_directory); var target = PathFor(player);
            using var guard = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (expected != null && File.Exists(target) && File.ReadAllText(target, Encoding.UTF8) != expected)
                throw new InvalidOperationException("待确认记录已被替换");
            File.Delete(target);
        }
    }
}
