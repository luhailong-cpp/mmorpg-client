using System.IO;
using MmorpgClient.Table;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class ClientTableBootstrapTests
    {
        [TearDown]
        public void TearDown()
        {
            ClientTableBootstrap.ResetForTests();
        }

        [Test]
        public void StreamingAssetsContainsEveryBinaryTableAndLoadsCleanly()
        {
            string tablesDir = Path.Combine(Application.streamingAssetsPath, ClientTableBootstrap.StreamingTablesFolder);
            Assert.That(Directory.Exists(tablesDir), Is.True, $"Missing synced tables directory: {tablesDir}");

            string[] expectedFiles = TableLoader.TableFileNames(useBinary: true);
            Assert.That(expectedFiles.Length, Is.EqualTo(TableLoader.TableCount));
            foreach (string fileName in expectedFiles)
            {
                string fullPath = Path.Combine(tablesDir, fileName);
                Assert.That(File.Exists(fullPath), Is.True, $"Missing binary table file: {fullPath}");
            }

            bool loaded = ClientTableBootstrap.TryLoadFromDirectory(tablesDir);
            Assert.That(loaded, Is.True, ClientTableBootstrap.LastError);
            Assert.That(ClientTableBootstrap.IsLoaded, Is.True);
            Assert.That(ClientTableBootstrap.LastError, Is.Null);
            Assert.That(SkillTableManager.Instance.FindAll().Count, Is.GreaterThan(0));
            Assert.That(MessageLimiterTableManager.Instance.FindAll().Count, Is.GreaterThan(0));
            Assert.That(BuffTableManager.Instance.FindAll().Count, Is.GreaterThan(0));
        }

        [Test]
        public void MissingDirectoryFailsSafelyWithoutThrowing()
        {
            string missingDir = Path.Combine(Path.GetTempPath(), "mmorpg_missing_tables_" + System.Guid.NewGuid().ToString("N"));
            bool loaded = ClientTableBootstrap.TryLoadFromDirectory(missingDir);

            Assert.That(loaded, Is.False);
            Assert.That(ClientTableBootstrap.IsLoaded, Is.False);
            Assert.That(ClientTableBootstrap.LastError, Is.Not.Null.And.Not.Empty);
        }
    }
}
