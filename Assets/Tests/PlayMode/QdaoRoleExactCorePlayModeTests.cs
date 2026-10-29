using System.Collections;
using System.Linq;
using MmorpgClient.Game;
using MmorpgClient.UI.Ugui.Role;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MmorpgClient.Tests.PlayMode
{
    public sealed partial class QdaoRoleIdentityPlayModeTests
    {
        [UnityTest]
        public IEnumerator FiveAccountRolesKeepApprovedCardSizeAndFifthIdentityRemainsReachable()
        {
            var host=new GameObject("ExactFiveRoleScrollFixture");var ui=host.AddComponent<RoleFlowUi>();
            var roles=Enumerable.Range(1,5).Select(i=>new AccountSimplePlayer{PlayerId=(ulong)(100+i),ClassId=4,Gender=2,AppearanceId=First,Name="道友"+i}).ToArray();
            try
            {
                typeof(RoleFlowUi).GetMethod("BuildCanvas",Hidden).Invoke(ui,null);
                var choice=new GameClient.PlayerChoice();var choose=Choose(ui,roles,choice);Assert.That(choose.MoveNext(),Is.True);yield return null;
                var scroll=host.GetComponentsInChildren<ScrollRect>(true).Single(s=>s.name=="RoleCardsViewport");
                var cards=host.GetComponentsInChildren<Button>().Where(b=>b.name.StartsWith("Role_")).ToArray();
                Assert.That(cards.Length,Is.EqualTo(5));
                Assert.That(cards.All(b=>((RectTransform)b.transform).sizeDelta==new Vector2(532,224)),Is.True,"第四第五角色不得压缩批准卡片");
                Assert.That(scroll.viewport.rect.height,Is.EqualTo(714));Assert.That(scroll.content.rect.height,Is.GreaterThan(scroll.viewport.rect.height));
                scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();
                var last=(RectTransform)cards.Single(b=>b.name=="Role_105").transform;
                // Card reachability uses its own visible rectangle; the circular portrait Mask
                // clips an intentionally oversized image whose hidden quad is not a scroll target.
                Assert.That(scroll.content.anchoredPosition.y,Is.EqualTo(scroll.content.rect.height-scroll.viewport.rect.height).Within(1f),"ScrollRect must reach the actual bottom extent");
                var corners=new UnityEngine.Vector3[4];last.GetWorldCorners(corners);
                var bounds=new UnityEngine.Bounds(scroll.viewport.InverseTransformPoint(corners[0]),UnityEngine.Vector3.zero);
                for(int i=1;i<corners.Length;i++)bounds.Encapsulate(scroll.viewport.InverseTransformPoint(corners[i]));
                Assert.That(bounds.min.x,Is.GreaterThanOrEqualTo(-1f));Assert.That(bounds.max.x,Is.LessThanOrEqualTo(scroll.viewport.rect.width+1f),"Fifth card must also fit horizontally");
                Assert.That(bounds.min.y,Is.GreaterThanOrEqualTo(-scroll.viewport.rect.height-1));Assert.That(bounds.max.y,Is.LessThanOrEqualTo(1),"第五角色可完整滚入视口");
                Click(host,"Role_105");
                Assert.That(FindImage(host,"CharacterArtwork").sprite,Is.SameAs(QdaoCharacterCatalog.LoadPortrait(First)));
                Assert.That(ReadText(host,"RoleNameValue"),Is.EqualTo("道友5"));Assert.That(ReadText(host,"GenderValue"),Is.EqualTo("女"));
                Assert.That(ReadText(host,"LevelValue"),Is.EqualTo("—"),"账号快照没有等级，不可使用原稿28");
                Click(host,"EnterSanctuary");Assert.That(choose.MoveNext(),Is.False);Assert.That(choice.SelectedPlayerId,Is.EqualTo(105UL));Assert.That(choice.CreateNew,Is.False);
                Assert.That(roles[4].Name,Is.EqualTo("道友5"));Assert.That(roles[4].AppearanceId,Is.EqualTo(First));
            }
            finally { Object.Destroy(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OriginalCreateCircleAndSecondaryDetailsPreserveCreationAndBackNavigation()
        {
            var host=new GameObject("ExactCreateCircleFixture");var ui=host.AddComponent<RoleFlowUi>();
            var roles=new[]{new AccountSimplePlayer{PlayerId=601,ClassId=4,Gender=2,AppearanceId=First,Name="真实道友"},new AccountSimplePlayer{PlayerId=602,ClassId=1,Gender=1,AppearanceId=Second}};
            try
            {
                typeof(RoleFlowUi).GetMethod("BuildCanvas",Hidden).Invoke(ui,null);var choice=new GameClient.PlayerChoice();var choose=Choose(ui,roles,choice);Assert.That(choose.MoveNext(),Is.True);yield return null;
                var create=host.GetComponentsInChildren<Button>().Single(b=>b.name=="CreateNewRole");
                Assert.That(create.GetComponent<Image>().sprite.name,Is.EqualTo("exact_role_create"));Assert.That(((RectTransform)create.transform).sizeDelta,Is.EqualTo(new Vector2(532,208)));
                Assert.That(FindImage(host,"ExactSelectionTitle").gameObject.activeInHierarchy,Is.True);
                Click(host,"Role_601");Click(host,"ToggleRoleDetails");Assert.That(ReadText(host,"RoleNameValue"),Is.EqualTo("真实道友"));
                Click(host,"CreateNewRole");Assert.That(FindImage(host,"ExactSelectionTitle").gameObject.activeInHierarchy,Is.False);
                Assert.That(host.GetComponentsInChildren<TMPro.TMP_InputField>(true).Single(i=>i.name=="RoleNameInput").gameObject.activeInHierarchy,Is.True);
                Click(host,"BackFromCreate");Assert.That(FindImage(host,"ExactSelectionTitle").gameObject.activeInHierarchy,Is.True);
                Click(host,"Role_601");Click(host,"EnterSanctuary");Assert.That(choose.MoveNext(),Is.False);Assert.That(choice.SelectedPlayerId,Is.EqualTo(601UL));Assert.That(choice.CreateNew,Is.False);
            }
            finally { Object.Destroy(host); }
            yield return null;
        }
    }
}
