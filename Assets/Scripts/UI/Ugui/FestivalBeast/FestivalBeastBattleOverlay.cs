using System;
using MmorpgClient.Game.FestivalBeast;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.FestivalBeast
{
    /// <summary>
    /// 瑞兽迎春战斗内重击点名预告条（对照 04-warning.png）：
    /// 使用无字 horizontal 九宫格 warning_bar（只横向加宽、保持高度 96px）+ 左侧 beast_portrait + 动态文字；
    /// 同步向 BattleScreen 下发被点名目标单位的头顶图标/形状/文字标记；严禁凭控制图标自行取消预警。
    /// </summary>
    public sealed class FestivalBeastBattleOverlay : MonoBehaviour
    {
        public bool IsVisible => gameObject.activeSelf;

        public void Show() => gameObject.SetActive(true);

        public void Hide()
        {
            BattleUiRoot.Instance?.ActiveBattleScreen?.SetFestivalBeastTelegraphMarker(0UL, null);
            gameObject.SetActive(false);
        }

        public void Render(FestivalBeastBattleHudViewModel vm)
        {
            FestivalBeastUiArt.Clear(transform);
            var screen = BattleUiRoot.Instance?.ActiveBattleScreen;
            if (vm == null || !vm.Visible)
            {
                screen?.SetFestivalBeastTelegraphMarker(0UL, null);
                return;
            }

            if (IsVisible)
            {
                screen?.SetFestivalBeastTelegraphMarker(vm.MarkedTargetActorId, vm.TargetMarkerText);
            }

            // 居中避开左上回合框与顶部行动计时环（y=176，宽 1160，保持 warning_bar 源高 96px）。
            const float barW = 1160f;
            const float barH = 96f;
            float barX = (2560f - barW) * 0.5f;
            const float barY = 176f;

            var bar = FestivalBeastUiArt.Rect("TelegraphBarRoot", transform, barX, barY, barW, barH);
            var bg = FestivalBeastUiArt.SlicedImage("WarningBar", bar, 0f, 0f, barW, barH, "warning_bar",
                vm.IsCancelled ? new Color(0.84f, 0.90f, 0.85f, 0.96f)
                    : vm.IsExecutingRound ? new Color(0.99f, 0.90f, 0.78f, 0.98f)
                        : new Color(0.97f, 0.93f, 0.83f, 0.97f));
            bg.raycastTarget = false;

            // 左侧春禧兽圆形主题头像
            FestivalBeastUiArt.SimpleImage("PortraitFrame", bar, 16f, 4f, 88f, 88f, "portrait_frame", FestivalBeastUiArt.WarmGold);
            FestivalBeastUiArt.SimpleImage("BeastPortrait", bar, 22f, 10f, 76f, 76f, "beast_portrait", Color.clear);

            // 状态徽章 + 主文案 + 防御/控制短提示（不把上轮防御画成跨轮保护，也不写“点防御必定安全”）
            string tag = vm.IsExecutingRound ? "本轮重击" : vm.IsWarningActive ? "下轮预告" : vm.IsCancelled ? "重击取消" : "重击结算";
            Color tagColor = vm.IsExecutingRound ? FestivalBeastUiArt.Cinnabar
                : vm.IsWarningActive ? FestivalBeastUiArt.WarnAmber
                    : FestivalBeastUiArt.DeepJade;

            FestivalBeastUiArt.Text("StatusTag", bar, 114f, 12f, 132f, 34f,
                tag, 21f, tagColor, bold: true, alignment: TextAlignmentOptions.MidlineLeft);
            FestivalBeastUiArt.Text("HeadlineText", bar, 248f, 8f, barW - 274f, 44f,
                vm.HeadlineText, 25f, vm.IsCancelled ? FestivalBeastUiArt.Muted : FestivalBeastUiArt.DeepJade,
                bold: true, autoSize: true);

            if (!string.IsNullOrEmpty(vm.HintText))
            {
                FestivalBeastUiArt.Text("HintText", bar, 114f, 52f, barW - 140f, 34f,
                    vm.HintText, 19f, FestivalBeastUiArt.Ink, bold: false, autoSize: true);
            }
        }
    }
}
