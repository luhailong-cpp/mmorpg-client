using System.Reflection;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 战斗画布可见区(Expand)× HUD 可交互控件:任何常见分辨率下按钮矩形都必须落在可见设计区内。
    /// 对照用例说明旧的 MatchWidthOrHeight 0.5 在 1920×1080 会把「取消自动」裁出屏。
    /// </summary>
    public sealed class BattleUiLayoutTests
    {
        private static readonly (float w, float h)[] Resolutions =
        {
            (2560f, 1080f), // 设计分辨率
            (1920f, 1080f), // 最常见桌面
            (1920f, 1200f), // 16:10
            (2340f, 1080f), // 手机 19.5:9
            (1280f, 720f),
            (1366f, 768f),
            (2560f, 1440f),
            (3440f, 1440f), // 更宽:横向多出空间
        };

        [Test]
        public void Expand_VisibleRect_AlwaysCoversWholeDesignArea()
        {
            foreach (var (w, h) in Resolutions)
            {
                var visible = BattleUiLayout.VisibleDesignRect(w, h);
                var design = new Rect(0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
                Assert.IsTrue(BattleUiLayout.Contains(visible, design), $"{w}×{h}:Expand 下设计面应整体可见,实际 {visible}");
            }
        }

        [Test]
        public void AllInteractiveHudRects_AreInsideVisibleArea_AtEveryResolution()
        {
            var rects = BattleUiLayout.InteractiveRects();
            Assert.Greater(rects.Count, 12);
            foreach (var (w, h) in Resolutions)
            {
                var visible = BattleUiLayout.VisibleDesignRect(w, h);
                foreach (var (name, rect) in rects)
                {
                    Assert.IsTrue(BattleUiLayout.Contains(visible, rect), $"{w}×{h}:{name} {rect} 超出可见区 {visible}");
                    Assert.IsTrue(BattleUiLayout.Contains(new Rect(0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight), rect),
                        $"{name} {rect} 超出设计面");
                }
            }
        }

        [Test]
        public void LegacyMatchWidthOrHeight_WouldClipCancelAutoAt1080p()
        {
            // 复审证据:1920×1080、match 0.5 → scale 0.866,画布宽 2217,两侧各裁 171.5px
            var visible = BattleUiLayout.VisibleDesignRectMatchWidthOrHeight(1920f, 1080f, 0.5f);
            Assert.AreEqual(171.5f, visible.xMin, 1f);
            var cancelAuto = BattleCommandRing.AutoKeyRect((int)AutoBattleCommand.CancelAuto);
            Assert.IsFalse(BattleUiLayout.Contains(visible, cancelAuto), "旧模式下「取消自动」应当被裁出屏(这就是要改 Expand 的原因)");
            var defend = BattleCommandRing.ButtonRect((int)BattleCommand.Defend);
            Assert.IsFalse(BattleUiLayout.Contains(visible, defend));
        }

        [Test]
        public void HudBands_DoNotOverlapEachOther()
        {
            // 顶部带:行动预告条底边 ≤ HudTopBand;底部带:确认条顶边 ≥ HudBottomBand;自动三键/命令环不压舞台横向区
            float orderBottom = BattleActionOrderBar.Top + BattleActionOrderBar.TileHeight;
            Assert.LessOrEqual(orderBottom, BattleStage.HudTopBand);
            Assert.GreaterOrEqual(BattleScreen.ConfirmRect.yMin, BattleStage.HudBottomBand);
            Assert.GreaterOrEqual(BattleScreen.CancelRect.yMin, BattleStage.HudBottomBand);
            Assert.GreaterOrEqual(BattleScreen.TargetHintRect.yMin, BattleStage.HudBottomBand);

            // 预告条满排不压左上战斗记录键与右上计时环
            float orderLeft = (QdaoUguiTheme.DesignWidth - BattleActionOrderBar.FullWidth) * 0.5f;
            Assert.GreaterOrEqual(orderLeft, BattleScreen.LogButtonRect.xMax);
            Assert.LessOrEqual(orderLeft + BattleActionOrderBar.FullWidth, BattleScreen.TimerRect.xMin);
            Assert.LessOrEqual(BattleScreen.TimerRect.xMax, BattlePartyCards.CardRect(0).xMin);
        }
        [Test]
        public void ProductionGroundPixels_AndUnitFeet_StayAlignedAcrossAspectRatios()
        {
            var fixture = new GameObject("BattleGroundAlignmentTest");
            try
            {
                new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(fixture.transform, false);
                var ownerGo = new GameObject("BattleOwner");
                ownerGo.SetActive(false);
                ownerGo.transform.SetParent(fixture.transform, false);
                var owner = ownerGo.AddComponent<BattleUiRoot>();
                owner.enabled = false;
                ownerGo.SetActive(true);
                typeof(BattleUiRoot).GetMethod("BuildCanvas", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(owner, null);
                var canvas = ownerGo.GetComponentInChildren<Canvas>(true);
                canvas.GetComponent<CanvasScaler>().enabled = false;
                canvas.renderMode = RenderMode.WorldSpace;
                var canvasRect = (RectTransform)canvas.transform;
                var layer = canvas.transform.Find("BattleLayer");
                layer.gameObject.SetActive(true);
                var stage = (RectTransform)layer.Find("BattleRoot/BattleScreen/World/Stage");
                Image art = null;
                foreach (var image in layer.GetComponentsInChildren<Image>(true))
                    if (image.name == "BattleArenaArt") art = image;
                Assert.NotNull(stage);
                Assert.NotNull(art);
                Assert.NotNull(art.sprite);
                Assert.AreEqual(2560f / 1080f, art.sprite.rect.width / art.sprite.rect.height, 0.0001f);
                layer.Find("BattleRoot/BattleScreen").gameObject.SetActive(true);

                foreach (var (width, height) in Resolutions)
                {
                    var visible = BattleUiLayout.VisibleDesignRect(width, height);
                    canvasRect.sizeDelta = visible.size;
                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
                    var picture = art.rectTransform;
                    foreach (bool mine in new[] { false, true })
                    for (int slot = 0; slot < BattleStage.SlotsPerTeam; slot++)
                    {
                        var foot = BattleStage.SlotPosition(mine, slot);
                        var groundPixel = picture.TransformPoint(new UnityEngine.Vector3(
                            picture.rect.xMin + foot.x / 2560f * picture.rect.width,
                            picture.rect.yMax - foot.y / 1080f * picture.rect.height, 0f));
                        var actorFoot = stage.TransformPoint(new UnityEngine.Vector3(
                            stage.rect.xMin + foot.x, stage.rect.yMax - foot.y, 0f));
                        Assert.Less(UnityEngine.Vector3.Distance(groundPixel, actorFoot), 0.01f,
                            $"{width}x{height}: team={mine} slot={slot} ground drifted under the unit");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }
    }
}
