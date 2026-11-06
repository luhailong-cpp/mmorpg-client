using System;
using System.IO;
using System.Text;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    public sealed class DurableRequestFileStoreTests
    {
        private string _directory;
        private DurableRequestFileStore _store;
        private string PathFor(ulong player) => Path.Combine(_directory, player + ".pending");

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "durable-snapshot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _store = new DurableRequestFileStore(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void MissingRecordHasNoSnapshot()
        {
            Assert.That(_store.ReadSnapshot(1), Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => _store.ReadSnapshot(0));
        }

        [TestCase(0)]
        [TestCase(65537)]
        public void EmptyAndOversizedRecordsCanBeRetiredByTheirRawSnapshot(int length)
        {
            File.WriteAllBytes(PathFor(1), new byte[length]);
            var saved = _store.ReadSnapshot(1);
            Assert.That(saved.Value, Is.EqualTo("!损坏"));
            Assert.That(saved.Matches(_store.ReadSnapshot(1)), Is.True);
            _store.ClearSnapshot(1, saved);
            Assert.That(File.Exists(PathFor(1)), Is.False);
        }

        [Test]
        public void InvalidUtf8CanBeRetiredWithoutDecodingItsBytes()
        {
            File.WriteAllBytes(PathFor(1), new byte[] { 0xc3, 0x28 });
            var saved = _store.ReadSnapshot(1);
            Assert.That(saved.Value, Is.EqualTo("!损坏"));
            _store.ClearSnapshot(1, saved);
            Assert.That(_store.ReadSnapshot(1), Is.Null);
        }

        [Test]
        public void EqualLossyTextDoesNotAuthorizeDeletingDifferentBytes()
        {
            byte[] first = { 0x80 }; byte[] second = { 0x81 };
            Assert.That(Encoding.UTF8.GetString(first), Is.EqualTo(Encoding.UTF8.GetString(second)));
            File.WriteAllBytes(PathFor(1), first);
            var saved = _store.ReadSnapshot(1);
            File.WriteAllBytes(PathFor(1), second);
            var replaced = _store.ReadSnapshot(1);
            Assert.That(saved.Value, Is.EqualTo(replaced.Value));
            Assert.That(saved.Matches(replaced), Is.False);
            Assert.Throws<InvalidOperationException>(() => _store.ClearSnapshot(1, saved));
            Assert.That(File.ReadAllBytes(PathFor(1)), Is.EqualTo(second));
        }

        [Test]
        public void SameLengthReplacementBeyondTextLimitInvalidatesConfirmation()
        {
            byte[] first = new byte[65537];
            for (int i = 0; i < first.Length; i++) first[i] = (byte)'A';
            File.WriteAllBytes(PathFor(1), first);
            var saved = _store.ReadSnapshot(1);
            byte[] second = (byte[])first.Clone(); second[second.Length - 1] = (byte)'B';
            File.WriteAllBytes(PathFor(1), second);
            Assert.That(saved.Matches(_store.ReadSnapshot(1)), Is.False);
            Assert.Throws<InvalidOperationException>(() => _store.ClearSnapshot(1, saved));
            Assert.That(File.ReadAllBytes(PathFor(1)), Is.EqualTo(second));
        }

        [Test]
        public void SnapshotCannotBeUsedForAnotherPlayerOrStoreInstance()
        {
            _store.Save(1, "原请求"); _store.Save(2, "原请求");
            var saved = _store.ReadSnapshot(1);
            var anotherStore = new DurableRequestFileStore(_directory);
            Assert.That(saved.Matches(_store.ReadSnapshot(2)), Is.False);
            Assert.That(saved.Matches(anotherStore.ReadSnapshot(1)), Is.False);
            Assert.Throws<ArgumentException>(() => _store.ClearSnapshot(2, saved));
            Assert.Throws<ArgumentException>(() => anotherStore.ClearSnapshot(1, saved));
            Assert.That(_store.Load(1), Is.EqualTo("原请求"));
            Assert.That(_store.Load(2), Is.EqualTo("原请求"));
        }

        [Test]
        public void LockContentionCannotReadOrDeleteAndDoesNotLeakHandles()
        {
            _store.Save(1, "原请求"); var saved = _store.ReadSnapshot(1);
            using (var guard = new FileStream(PathFor(1) + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Throws<IOException>(() => _store.ReadSnapshot(1));
                Assert.Throws<IOException>(() => _store.ClearSnapshot(1, saved));
                Assert.That(_store.Load(1), Is.EqualTo("原请求"));
            }
            Assert.That(saved.Matches(_store.ReadSnapshot(1)), Is.True);
            _store.ClearSnapshot(1, saved);
            Assert.That(File.Exists(PathFor(1)), Is.False);
        }

        [Test]
        public void UnreadableTargetCannotBeClearedAndDoesNotLeakTheGuard()
        {
            _store.Save(1, "原请求"); var saved = _store.ReadSnapshot(1);
            using (var blocked = new FileStream(PathFor(1), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Throws<IOException>(() => _store.ReadSnapshot(1));
                Assert.Throws<IOException>(() => _store.ClearSnapshot(1, saved));
            }
            Assert.That(_store.Load(1), Is.EqualTo("原请求"));
            _store.ClearSnapshot(1, saved);
            Assert.That(_store.ReadSnapshot(1), Is.Null);
        }

        [Test]
        public void AlreadyRemovedSnapshotCanBeClearedAgain()
        {
            _store.Save(1, "原请求"); var saved = _store.ReadSnapshot(1);
            _store.Clear(1, "原请求");
            Assert.DoesNotThrow(() => _store.ClearSnapshot(1, saved));
            Assert.That(_store.ReadSnapshot(1), Is.Null);
        }

        [Test]
        public void Utf8BomIsAcceptedButUtf16IsNotReinterpretedAsUtf8()
        {
            File.WriteAllText(PathFor(1), "原请求", new UTF8Encoding(true));
            Assert.That(_store.ReadSnapshot(1).Value, Is.EqualTo("原请求"));
            File.WriteAllText(PathFor(1), "原请求", Encoding.Unicode);
            Assert.That(_store.ReadSnapshot(1).Value, Is.EqualTo("!损坏"));
        }

        [Test]
        public void ExistingSaveTransitionAndClearContractsRemainIntact()
        {
            _store.Save(1, "原请求"); var saved = _store.ReadSnapshot(1);
            Assert.That(saved.Value, Is.EqualTo("原请求"));
            Assert.DoesNotThrow(() => _store.Save(1, "原请求"));
            Assert.Throws<InvalidOperationException>(() => _store.Save(1, "另一请求"));
            _store.Transition(1, "原请求", "已更新请求");
            Assert.That(_store.Load(1), Is.EqualTo("已更新请求"));
            Assert.Throws<InvalidOperationException>(() => _store.Clear(1, "原请求"));
            Assert.Throws<InvalidOperationException>(() => _store.ClearSnapshot(1, saved));
            _store.Clear(1, "已更新请求");
            Assert.That(_store.Load(1), Is.Empty);
        }
    }
}
