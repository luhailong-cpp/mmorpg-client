using System;
using System.Reflection;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Guild;
using MmorpgClient.UI.Ugui.Jubaozhai;
using MmorpgClient.UI.Ugui.Mail;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.UI.Ugui.Rank;
using MmorpgClient.UI.Ugui.Skills;
using MmorpgClient.UI.Ugui.Social;
using MmorpgClient.UI.Ugui.Team;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class UiHostShutdownTests
    {
        [TestCase(typeof(AttributeUiRoot))]
        [TestCase(typeof(BattleUiRoot))]
        [TestCase(typeof(GameplayUiRoot))]
        [TestCase(typeof(GuildUiRoot))]
        [TestCase(typeof(JubaozhaiUiRoot))]
        [TestCase(typeof(MailUiRoot))]
        [TestCase(typeof(PetUiRoot))]
        [TestCase(typeof(RankUiRoot))]
        [TestCase(typeof(SkillUiRoot))]
        [TestCase(typeof(SocialUiRoot))]
        [TestCase(typeof(TeamUiRoot))]
        [TestCase(typeof(TeamFollowUiRoot))]
        [TestCase(typeof(CityTravelUiRoot))]
        [TestCase(typeof(SceneChannelUiRoot))]
        [TestCase(typeof(MysteryMerchantUiRoot))]
        [TestCase(typeof(QuestHudRoot))]
        [TestCase(typeof(WorldSideHudRoot))]
        [TestCase(typeof(WorldHudNavigation))]
        public void UninitializedHostToleratesLateUpdatesAndRepeatedCleanup(Type type)
        {
            var fixture = new GameObject("Uninitialized_" + type.Name);
            fixture.SetActive(false); // 未完成Awake的重复/退出实例，不创建真实会话。
            try
            {
                var owner = fixture.AddComponent(type);
                foreach (var method in new[] { "Update", "LateUpdate", "OnDisable", "OnDestroy", "OnDestroy", "Update", "LateUpdate" })
                {
                    var callback = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
                    if (callback != null)
                        Assert.DoesNotThrow(() => callback.Invoke(owner, null), type.Name + "." + method);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        [Test]
        public void CityPanelRegistryHidesOthersAndPrunesDestroyedOwners()
        {
            var ownerA = new object();
            var ownerB = new object();
            var deadGo = new GameObject("DeadPanelOwner");
            int aHidden = 0, bHidden = 0, deadHidden = 0;
            try
            {
                CityPanelRegistry.Register(ownerA, () => aHidden++);
                CityPanelRegistry.Register(ownerB, () => { bHidden++; CityPanelRegistry.Unregister(ownerB); });
                CityPanelRegistry.Register(deadGo, () => deadHidden++);
                UnityEngine.Object.DestroyImmediate(deadGo);

                CityPanelRegistry.HideOthers(ownerA);

                Assert.That(aHidden, Is.Zero);
                Assert.That(bHidden, Is.EqualTo(1));
                Assert.That(deadHidden, Is.Zero);

                CityPanelRegistry.HideOthers(null);
                Assert.That(aHidden, Is.EqualTo(1));
                Assert.That(bHidden, Is.EqualTo(1));
            }
            finally
            {
                CityPanelRegistry.Unregister(ownerA);
                CityPanelRegistry.Unregister(ownerB);
                if (deadGo != null) UnityEngine.Object.DestroyImmediate(deadGo);
            }
        }
    }
}
