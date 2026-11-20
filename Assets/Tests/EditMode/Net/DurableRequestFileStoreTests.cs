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

        // ── 归属修订号:角色换主人之前留下的记录按不存在处理 ──

        [Test]
        public void RecordWrittenUnderOneOwnerRevisionIsAbsentAfterTheRoleChangesOwner()
        {
            ulong revision = 7;
            var store = new DurableRequestFileStore(_directory, () => revision);
            store.Save(1, "原请求");
            Assert.That(File.ReadAllText(PathFor(1), Encoding.UTF8), Is.EqualTo("@owner-revision:7\n原请求"));
            Assert.That(store.Load(1), Is.EqualTo("原请求"));
            Assert.That(store.ReadSnapshot(1).Value, Is.EqualTo("原请求"));

            revision = 0; // 这次进场没拿到修订号:未知不作废任何记录
            Assert.That(store.Load(1), Is.EqualTo("原请求"));
            Assert.That(_store.Load(1), Is.EqualTo("原请求"), "没有修订号来源的存储同样照读");

            revision = 8; // 角色过户之后
            Assert.That(store.Load(1), Is.Empty);
            Assert.That(File.Exists(PathFor(1)), Is.True, "Load 不持锁,不删文件");
            Assert.That(store.ReadSnapshot(1), Is.Null);
            Assert.That(File.Exists(PathFor(1)), Is.False, "持锁读到作废记录就地删除");
        }

        [Test]
        public void SupersededRecordNeitherBlocksNorAuthorizesTheNewOwner()
        {
            ulong revision = 7;
            var store = new DurableRequestFileStore(_directory, () => revision);
            store.Save(1, "前主人的请求");

            revision = 8;
            Assert.Throws<InvalidOperationException>(() => store.Transition(1, "前主人的请求", "换状态"), "作废记录不能当作原记录做状态转换");
            Assert.DoesNotThrow(() => store.Save(1, "新主人的请求"), "作废记录不占槽位");
            Assert.That(store.Load(1), Is.EqualTo("新主人的请求"));
            Assert.That(File.ReadAllText(PathFor(1), Encoding.UTF8), Is.EqualTo("@owner-revision:8\n新主人的请求"));

            revision = 9;
            Assert.DoesNotThrow(() => store.Clear(1, "对不上的内容"), "作废记录不参与比对,直接删");
            Assert.That(File.Exists(PathFor(1)), Is.False);
        }

        [Test]
        public void RecordWithoutRevisionStaysVisibleAndKeepsItsSlot()
        {
            _store.Save(1, "旧格式请求");
            ulong revision = 8;
            var store = new DurableRequestFileStore(_directory, () => revision);
            Assert.That(store.Load(1), Is.EqualTo("旧格式请求"), "写入时修订号未知的记录不能判成前主人的");
            Assert.Throws<InvalidOperationException>(() => store.Save(1, "另一请求"));
            Assert.DoesNotThrow(() => store.Save(1, "旧格式请求"));
            store.Clear(1, "旧格式请求");
            Assert.That(store.Load(1), Is.Empty);
        }

        [Test]
        public void ValueShapedLikeTheRevisionLineIsRejectedAndAMalformedLineIsNotGuessed()
        {
            Assert.Throws<InvalidDataException>(() => _store.Save(1, "@owner-revision:7\n伪造"));
            File.WriteAllText(PathFor(1), "@owner-revision:x\n正文", new UTF8Encoding(false));
            ulong revision = 8;
            var store = new DurableRequestFileStore(_directory, () => revision);
            Assert.That(store.Load(1), Is.EqualTo("@owner-revision:x\n正文"), "首行不合规时整份原样交给调用方按损坏处理");
        }
    }
}
