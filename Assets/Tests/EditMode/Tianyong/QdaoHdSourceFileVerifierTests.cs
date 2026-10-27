using System;
using System.IO;
using System.Security.Cryptography;
using MmorpgClient.World;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoHdSourceFileVerifierTests
    {
        private string _directory;
        private string _path;
        private byte[] _png;
        private string _hash;

        [SetUp]
        public void CreateOwnedTemporarySource()
        {
            _directory = Path.Combine(Path.GetTempPath(), "qdao-hd-source-verifier-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "source.png");
            _png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
            File.WriteAllBytes(_path, _png);
            _hash = Hash(_png);
        }

        [TearDown]
        public void RemoveOwnedTemporarySource()
        {
            if (_path != null && File.Exists(_path)) File.Delete(_path);
            if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory);
        }

        [Test]
        public void FreshCheckoutAndTouchPreserveContentIdentity()
        {
            Assert.That(Matches(), Is.True);
            File.SetLastWriteTimeUtc(_path, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            Assert.That(Matches(), Is.True, "A Git checkout or touch does not change signed PNG bytes.");
        }

        [Test]
        public void SameLengthTamperingWithRestoredTimestampIsRejectedAfterPriorSuccess()
        {
            Assert.That(Matches(), Is.True);
            var originalTime = File.GetLastWriteTimeUtc(_path);
            var changed = (byte[])_png.Clone();
            changed[changed.Length - 1] ^= 1;
            File.WriteAllBytes(_path, changed);
            File.SetLastWriteTimeUtc(_path, originalTime);
            Assert.That(new FileInfo(_path).Length, Is.EqualTo(_png.Length));
            Assert.That(File.GetLastWriteTimeUtc(_path), Is.EqualTo(originalTime));
            Assert.That(Matches(), Is.False, "A length/mtime cache must not trust changed bytes.");
        }

        [Test]
        public void MissingSourceIsRejected()
        {
            File.Delete(_path);
            Assert.That(Matches(), Is.False);
        }

        [Test]
        public void WrongLengthDimensionsAndDigestAreRejected()
        {
            Assert.That(QdaoHdSourceFileVerifier.Matches(_path, _png.Length + 1, 1, 1, _hash), Is.False);
            Assert.That(QdaoHdSourceFileVerifier.Matches(_path, _png.Length, 2, 1, _hash), Is.False);
            Assert.That(QdaoHdSourceFileVerifier.Matches(_path, _png.Length, 1, 2, _hash), Is.False);
            Assert.That(QdaoHdSourceFileVerifier.Matches(_path, _png.Length, 1, 1, new string('0', 64)), Is.False);
        }

        [Test]
        public void InvalidPngHeaderIsRejectedEvenWithMatchingHash()
        {
            var changed = (byte[])_png.Clone();
            changed[0] = 0;
            File.WriteAllBytes(_path, changed);
            Assert.That(QdaoHdSourceFileVerifier.Matches(_path, changed.Length, 1, 1, Hash(changed)), Is.False);
        }

        private bool Matches() => QdaoHdSourceFileVerifier.Matches(_path, _png.Length, 1, 1, _hash);

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
