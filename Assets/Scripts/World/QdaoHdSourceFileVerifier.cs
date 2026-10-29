using System;
using System.IO;
using System.Security.Cryptography;

namespace MmorpgClient.World
{
    /// <summary>Checks portable source identity without loading a Unity texture or trusting file timestamps.</summary>
    public static class QdaoHdSourceFileVerifier
    {
        public static bool Matches(string path, long expectedBytes, int expectedWidth, int expectedHeight,
            string expectedSha256)
        {
            if (string.IsNullOrEmpty(path) || expectedBytes < 24 || expectedWidth <= 0 || expectedHeight <= 0 ||
                expectedSha256 == null || expectedSha256.Length != 64) return false;
            try
            {
                // The handle also prevents another Windows writer from changing the file during this check.
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length != expectedBytes) return false;
                var header = new byte[24];
                if (stream.Read(header, 0, header.Length) != header.Length || header[0] != 137 || header[1] != 80 ||
                    header[2] != 78 || header[3] != 71 || header[4] != 13 || header[5] != 10 || header[6] != 26 ||
                    header[7] != 10 || header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82)
                    return false;
                var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                if (width != expectedWidth || height != expectedHeight) return false;
                stream.Position = 0;
                using var sha = SHA256.Create();
                var actualSha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                // Always hash on a real check: an edit can preserve both length and modification time.
                return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
