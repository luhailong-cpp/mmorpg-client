using System.Collections.Generic;
using System.IO;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class ActivityPreferencesTests
    {
        [Test]
        public void PreferencesPersistPerRoleAndMatchBothActivityIdAndStableDefinitionIdentity()
        {
            var storage = new Dictionary<string, string>();
            ActivityPreferences NewPreferences() => new(key => storage.TryGetValue(key, out var value) ? value : string.Empty,
                (key, value) => storage[key] = value);
            var preferences = NewPreferences();
            Assert.That(preferences.Toggle(7, "stable-a", "活动"), Is.False, "身份未同步不能保存");
            preferences.SetPlayer(100); preferences.SetFilters(1, 4);
            Assert.That(preferences.Toggle(7, "stable-a", "活动"), Is.True);
            preferences.SetPlayer(200);
            Assert.That(preferences.Favorites, Is.Empty); Assert.That(preferences.Category, Is.EqualTo(-1));
            Assert.That(preferences.Contains(7, "stable-a"), Is.False);
            var restored = NewPreferences(); restored.SetPlayer(100);
            Assert.That(restored.Contains(7, "stable-a"), Is.True);
            Assert.That(restored.Contains(7, "stable-b"), Is.False, "重用数字ID不能继承旧收藏");
            Assert.That(restored.Category, Is.EqualTo(1)); Assert.That(restored.Reward, Is.EqualTo(4));
            restored.SetPlayer(0); Assert.That(restored.Favorites, Is.Empty);
        }

        [Test]
        public void PreferenceStorageFailuresAreVisibleWithoutBreakingTheWindow()
        {
            var unreadable = new ActivityPreferences(_ => throw new IOException("fixture"), (_, _) => { });
            Assert.DoesNotThrow(() => unreadable.SetPlayer(100));
            Assert.That(unreadable.Error, Does.Contain("读取失败"));
            var unwritable = new ActivityPreferences(_ => string.Empty, (_, _) => throw new System.UnauthorizedAccessException("fixture"));
            unwritable.SetPlayer(100);
            Assert.That(unwritable.Toggle(7, "stable-a", "活动"), Is.False);
            Assert.That(unwritable.Error, Does.Contain("保存失败"));
            Assert.That(unwritable.Contains(7, "stable-a"), Is.True, "本次本地展示仍可解释，不冒充持久成功");
        }
    }
}
