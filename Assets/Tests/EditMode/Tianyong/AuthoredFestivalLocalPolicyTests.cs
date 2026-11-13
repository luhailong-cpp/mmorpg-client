using MmorpgClient.World.CurrentMaps;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class AuthoredFestivalLocalPolicyTests
    {
        [TestCase("http://127.0.0.1:18085", "127.0.0.1:28000", true)]
        [TestCase("http://localhost:18085", "localhost:28000", true)]
        [TestCase("http://[::1]:18085", "[::1]:28000", true)]
        [TestCase("http://127.0.0.1:18081", "127.0.0.1:28000", false)]
        [TestCase("http://127.0.0.1:18085", "127.0.0.1:28001", false)]
        [TestCase("http://192.0.2.1:18085", "127.0.0.1:28000", false)]
        [TestCase("http://127.0.0.1:18085", "192.0.2.1:28000", false)]
        [TestCase("http://user@localhost:18085", "localhost:28000", false)]
        [TestCase("ftp://localhost:18085", "localhost:28000", false)]
        [TestCase("http://localhost:18085", "localhost:bad", false)]
        public void RequiresCanonicalLoopbackEndpoints(string gateway, string gate, bool expected)
        {
            Assert.That(AuthoredFestivalLocalPolicy.MatchesEndpoints(gateway, gate), Is.EqualTo(expected));
        }
    }
}
