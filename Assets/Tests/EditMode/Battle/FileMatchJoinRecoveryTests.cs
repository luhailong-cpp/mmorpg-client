using System;
using System.IO;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class FileMatchJoinRecoveryTests
    {
        private string _directory;
        [SetUp] public void Setup() { _directory = Path.Combine(Path.GetTempPath(), "match-intent-test-" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup()
        {
            if(!Directory.Exists(_directory))return;
            Assert.That(Path.GetDirectoryName(Path.GetFullPath(_directory)),Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
            Assert.That(Path.GetFileName(_directory),Does.StartWith("match-intent-test-"));
            Directory.Delete(_directory,true);
        }
        private static Match.JoinQueueRequest Request() => new() { PlayerId = 11, RequestId = Guid.NewGuid().ToString("D"), Mode = Match.MatchMode._1V1, NetworkPlanId = "original-plan" };

        [Test] public void AtomicJoinAndCancellationSurviveReopenWithoutChangingOriginalRequest()
        {
            var request = Request(); var store = new FileMatchJoinRecovery(_directory, "https://fixture.test");
            store.Save(request, false); store.Save(request, true);
            var reopened = new FileMatchJoinRecovery(_directory, "https://fixture.test");
            Assert.That(reopened.Read(11, out var cancelled), Is.EqualTo(request)); Assert.That(cancelled, Is.True);
            reopened.Clear(11); Assert.That(reopened.Read(11, out _), Is.Null);
        }
        [Test] public void AnotherRequestOrOldClearCannotOverwriteCancellation()
        {
            var request = Request(); var old = new FileMatchJoinRecovery(_directory, "https://fixture.test"); old.Save(request, false);
            var current = new FileMatchJoinRecovery(_directory, "https://fixture.test"); current.Save(request, true);
            Assert.Throws<InvalidOperationException>(() => old.Clear(11));
            Assert.Throws<InvalidOperationException>(() => current.Save(Request(), false));
            Assert.That(current.Read(11, out var cancelled), Is.EqualTo(request)); Assert.That(cancelled, Is.True);
        }
        [Test] public void CorruptRecordAndUnavailableDirectoryFailClosed()
        {
            var request = Request(); var store = new FileMatchJoinRecovery(_directory, "https://fixture.test"); store.Save(request, false);
            File.WriteAllText(Path.Combine(Directory.GetDirectories(_directory)[0], "11.pending"), "");
            Assert.Throws<InvalidDataException>(() => store.Read(11, out _));
            Assert.Throws<InvalidOperationException>(() => store.Save(Request(), false));
            string blocked=Path.Combine(_directory,"unavailable");File.WriteAllText(blocked,"not a directory");
            Assert.Throws<IOException>(()=>new FileMatchJoinRecovery(blocked,"https://fixture.test").Save(Request(),false));
        }
    }
}
