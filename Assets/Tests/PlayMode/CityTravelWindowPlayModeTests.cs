using System.Collections;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    public sealed class CityTravelWindowPlayModeTests
    {
        // 门禁依赖MonoBehaviour.OnEnable/OnDisable，必须在真实播放生命周期验证。
        [UnityTest]
        public IEnumerator OpenMap_BlocksMovementAndCloseRestoresInput()
        {
            var root = new GameObject("MapInputPlayModeTest", typeof(RectTransform));
            var design = QdaoUguiFactory.CreateCenteredRect("Design", root.transform, 2560, 1080);
            var window = new CityTravelWindow(design);
            window.SetDestinations(CityTravelUiRoot.CreateDestinations());
            yield return null;
            bool before = GameplayInputGate.IsKeyboardBlocked;
            try
            {
                window.Show(1, false);
                yield return null;
                Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.True);
                Assert.That(GameplayInputGate.IsPointerBlocked, Is.True);
                window.Hide();
                yield return null;
                Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.EqualTo(before));
            }
            finally
            {
                window.Hide();
                Object.Destroy(root);
            }
        }
    }
}
