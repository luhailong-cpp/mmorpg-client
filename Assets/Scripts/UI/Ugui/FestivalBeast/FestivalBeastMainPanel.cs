using System;
using MmorpgClient.Game.FestivalBeast;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.FestivalBeast
{
    /// <summary>
    /// 瑞兽迎春活动主面板（对照 01-activity-main.png，全部文本、进度、时间窗与按钮态由服务端权威视图模型驱动）。
    /// 布局设计基准 2560×1080，主框 1760×900 居中并随外层 SafeArea 自动缩放适配多分辨率。
    /// </summary>
    public sealed class FestivalBeastMainPanel : MonoBehaviour
    {
        private const float FrameX = 400f;
        private const float FrameY = 90f;
        private const float FrameW = 1760f;
        private const float FrameH = 900f;

        private Action _onClose;
        private Action _onOpenRules;
        private Action _onRefresh;
        private Action _onPrimaryAction;
        private Action _onOpenPets;
        private Action _onOpenMaterialClaim;
        private Action _onOpenAppearance;

        public bool IsVisible => gameObject.activeSelf;

        public void Configure(
            Action onClose,
            Action onOpenRules,
            Action onRefresh,
            Action onPrimaryAction,
            Action onOpenPets,
            Action onOpenMaterialClaim,
            Action onOpenAppearance)
        {
            _onClose = onClose;
            _onOpenRules = onOpenRules;
            _onRefresh = onRefresh;
            _onPrimaryAction = onPrimaryAction;
            _onOpenPets = onOpenPets;
            _onOpenMaterialClaim = onOpenMaterialClaim;
            _onOpenAppearance = onOpenAppearance;
        }

        public void Show(FestivalBeastMainPanelViewModel vm)
        {
            gameObject.SetActive(true);
            Render(vm);
        }

        public void Hide() => gameObject.SetActive(false);

        public void Render(FestivalBeastMainPanelViewModel vm)
        {
            if (vm == null) return;
            FestivalBeastUiArt.Clear(transform);

            var frame = FestivalBeastUiArt.Rect("MainFrameRoot", transform, FrameX, FrameY, FrameW, FrameH);
            FestivalBeastUiArt.SlicedImage("WindowFrame", frame, 0f, 0f, FrameW, FrameH, "window_frame", FestivalBeastUiArt.Ivory, raycast: true);

            // 顶部：活动名牌 + 规则/刷新/关闭 + 北京时间 UTC+8 时间窗与倒计时
            FestivalBeastUiArt.SimpleImage("TitlePlate", frame, (FrameW - 620f) * 0.5f, -18f, 620f, 104f, "title_plate", FestivalBeastUiArt.DeepJade);
            FestivalBeastUiArt.Text("TitleText", frame, (FrameW - 460f) * 0.5f, -6f, 460f, 76f,
                vm.IsDemoPreview ? vm.ActivityTitle + "（演示）" : vm.ActivityTitle,
                40f, FestivalBeastUiArt.Ivory, bold: true, alignment: TextAlignmentOptions.Center, autoSize: true);

            FestivalBeastUiArt.SimpleImage("LanternLeft", frame, 58f, 18f, 58f, 62f, "festival_icon_lantern", Color.clear);
            FestivalBeastUiArt.Text("PhaseBadge", frame, 128f, 26f, 250f, 46f,
                vm.PhaseBadgeText, 24f, FestivalBeastUiArt.DeepJade, bold: true, alignment: TextAlignmentOptions.MidlineLeft, autoSize: true);

            FestivalBeastUiArt.Button("TopRulesBtn", frame, FrameW - 365f, 22f, 130f, 54f,
                "规则", _onOpenRules, enabled: true, primary: false, fontSize: 22f);
            FestivalBeastUiArt.Button("TopRefreshBtn", frame, FrameW - 225f, 22f, 124f, 54f,
                "刷新", _onRefresh, enabled: vm.ShowRefreshButton, primary: false, fontSize: 22f);
            FestivalBeastUiArt.CloseButton(frame, FrameW - 86f, 10f, _onClose);

            // 北京时间起止与倒计时条
            FestivalBeastUiArt.SlicedImage("TimeBar", frame, 68f, 92f, FrameW - 136f, 54f, "info_row", new Color(0.95f, 0.91f, 0.82f, 0.96f));
            FestivalBeastUiArt.Text("TimeWindowText", frame, 92f, 96f, 1040f, 46f,
                vm.TimeWindowText, 21f, FestivalBeastUiArt.Ink, bold: false, autoSize: true);
            if (!string.IsNullOrEmpty(vm.CountdownText))
            {
                FestivalBeastUiArt.Text("CountdownText", frame, FrameW - 580f, 96f, 490f, 46f,
                    vm.CountdownText, 22f, FestivalBeastUiArt.Cinnabar, bold: true, alignment: TextAlignmentOptions.MidlineRight, autoSize: true);
            }

            // 左侧：春禧兽展示台与立绘
            FestivalBeastUiArt.SimpleImage("PetPedestal", frame, 64f, 182f, 500f, 560f, "pet_display", new Color(0.92f, 0.89f, 0.8f, 0.5f));
            FestivalBeastUiArt.SimpleImage("BeastIllustration", frame, 82f, 178f, 470f, 430f, "beast_illustration", Color.clear);
            FestivalBeastUiArt.Text("BeastNameTag", frame, 94f, 704f, 440f, 44f,
                "春禧兽 · 专属活动神兽", 24f, FestivalBeastUiArt.DeepJade, bold: true, alignment: TextAlignmentOptions.Center, autoSize: true);
            if (vm.ShowClaimedPetReceipt)
            {
                FestivalBeastUiArt.SlicedImage("ClaimReceiptCard", frame, 64f, 754f, 500f, 96f, "content_card", new Color(0.89f, 0.94f, 0.88f, 0.98f));
                FestivalBeastUiArt.Text("ClaimReceiptText", frame, 84f, 762f, 460f, 80f,
                    vm.ClaimedPetReceiptText, 19f, FestivalBeastUiArt.DeepJade, bold: true, wrap: true, alignment: TextAlignmentOptions.MidlineLeft, autoSize: true);
            }

            // 右侧主体：累计三胜目标 + 三节点进度条（单一目标，非三个领奖箱）
            const float rightX = 602f;
            const float rightW = 1090f;

            FestivalBeastUiArt.SlicedImage("GoalCard", frame, rightX, 162f, rightW, 184f, "content_card", FestivalBeastUiArt.Ivory);
            FestivalBeastUiArt.Text("GoalTitle", frame, rightX + 32f, 174f, 760f, 46f,
                vm.GoalSummaryText, 28f, FestivalBeastUiArt.DeepJade, bold: true, autoSize: true);
            FestivalBeastUiArt.Text("ProgressCounter", frame, rightX + rightW - 220f, 172f, 186f, 50f,
                vm.ProgressText, 34f, FestivalBeastUiArt.Cinnabar, bold: true, alignment: TextAlignmentOptions.MidlineRight);

            // 进度轨道与 3 枚累计节点
            const float trackX = rightX + 76f;
            const float trackY = 258f;
            const float trackW = 730f;
            FestivalBeastUiArt.ProgressBar(frame, trackX, trackY, trackW, 34f, vm.ProgressRatio);
            uint required = Math.Max(1u, vm.RequiredWins);
            for (uint i = 0; i < 3u; ++i)
            {
                bool done = vm.CurrentWins > i;
                float nodeCenterX = trackX + (i == 0 ? 40f : i == 1 ? trackW * 0.5f : trackW - 40f);
                FestivalBeastUiArt.SimpleImage("ProgressNode_" + (i + 1), frame,
                    nodeCenterX - 36f, trackY - 20f, 72f, 72f,
                    done ? "progress_done" : "progress_pending", done ? FestivalBeastUiArt.DeepJade : FestivalBeastUiArt.Muted);
            }
            FestivalBeastUiArt.Text("ProgressNote", frame, rightX + 830f, 240f, 232f, 72f,
                $"累计 {required} 胜领宠\n（非分档宝箱）", 19f, FestivalBeastUiArt.Muted, wrap: true, alignment: TextAlignmentOptions.Center);

            // 收益区：独立两行，严格区分「今日/当期还能赚取」与「已赚取未领取跨日余额」
            FestivalBeastUiArt.SlicedImage("EarnableRow", frame, rightX, 360f, rightW, 74f, "info_row", FestivalBeastUiArt.Ivory);
            FestivalBeastUiArt.SimpleImage("EarnableIcon", frame, rightX + 18f, 372f, 50f, 50f, "activity_icon_taiji", Color.clear);
            FestivalBeastUiArt.Text("EarnableText", frame, rightX + 80f, 368f, rightW - 104f, 58f,
                vm.EarnableMaterialText, 22f, FestivalBeastUiArt.Ink, bold: true, wrap: true, autoSize: true);

            FestivalBeastUiArt.SlicedImage("AvailableRow", frame, rightX, 444f, rightW, 74f, "info_row", FestivalBeastUiArt.Ivory);
            FestivalBeastUiArt.SimpleImage("AvailableIcon", frame, rightX + 18f, 456f, 50f, 50f, "festival_icon_lantern", Color.clear);
            FestivalBeastUiArt.Text("AvailableText", frame, rightX + 80f, 452f, rightW - 280f, 58f,
                vm.AvailableMaterialText, 22f, FestivalBeastUiArt.DeepJade, bold: true, wrap: true, autoSize: true);
            FestivalBeastUiArt.Button("MaterialClaimJumpBtn", frame, rightX + rightW - 186f, 455f, 168f, 52f,
                "领取材料", _onOpenMaterialClaim, enabled: vm.CanJumpMaterialClaim, primary: false, fontSize: 21f);

            // 准备区：当前出战宝宝 + 查看宝宝入口 + 点名机制短说明
            FestivalBeastUiArt.SimpleImage("PetPrepSlot", frame, rightX, 534f, rightW, 146f, "pet_info_slot", FestivalBeastUiArt.Ivory, preserveAspect: false);
            FestivalBeastUiArt.SimpleImage("PetPortraitFrame", frame, rightX + 20f, 550f, 114f, 114f, "portrait_frame", FestivalBeastUiArt.WarmGold);
            FestivalBeastUiArt.SimpleImage("PetPortraitArt", frame, rightX + 30f, 560f, 94f, 94f, "beast_portrait", Color.clear);
            FestivalBeastUiArt.Text("ActivePetNameText", frame, rightX + 148f, 546f, rightW - 350f, 42f,
                vm.ActivePetLabel, 24f, FestivalBeastUiArt.DeepJade, bold: true, autoSize: true);
            FestivalBeastUiArt.Text("ActivePetHintText", frame, rightX + 148f, 588f, rightW - 350f, 34f,
                vm.ActivePetHintText, 19f, FestivalBeastUiArt.Muted, autoSize: true);
            FestivalBeastUiArt.Text("CombatTipText", frame, rightX + 148f, 624f, rightW - 350f, 42f,
                vm.CombatTipText, 19f, FestivalBeastUiArt.WarnAmber, bold: true, wrap: true, autoSize: true);
            FestivalBeastUiArt.Button("ViewPetsBtn", frame, rightX + rightW - 186f, 580f, 168f, 56f,
                "查看宝宝", _onOpenPets, enabled: true, primary: false, fontSize: 22f);

            // 状态提示行（异常 / 恢复 / 未同步）
            if (!string.IsNullOrEmpty(vm.StatusBannerText))
            {
                FestivalBeastUiArt.Text("StatusBannerText", frame, rightX + 12f, 690f, rightW - 24f, 42f,
                    vm.StatusBannerText, 21f, vm.StatusBannerIsError ? FestivalBeastUiArt.Cinnabar : FestivalBeastUiArt.DeepJade,
                    bold: true, wrap: true, alignment: TextAlignmentOptions.MidlineLeft, autoSize: true);
            }

            // 底部按钮行：活动规则 + 低优先级人物外观 + 严格优先级主按钮
            FestivalBeastUiArt.Button("BottomRulesBtn", frame, rightX, 748f, 210f, 76f,
                "活动规则", _onOpenRules, enabled: true, primary: false, fontSize: 26f);

            if (vm.ShowAppearanceEntry)
            {
                FestivalBeastUiArt.Button("AppearanceEntryBtn", frame, rightX + 228f, 756f, 178f, 60f,
                    "人物外观", _onOpenAppearance, enabled: true, primary: false, fontSize: 21f);
            }

            if (vm.PrimaryButtonKind != FestivalBeastPrimaryButtonKind.Hidden)
            {
                FestivalBeastUiArt.Button("PrimaryActionBtn", frame, rightX + rightW - 390f, 742f, 390f, 88f,
                    vm.PrimaryButtonText, _onPrimaryAction, enabled: vm.PrimaryButtonEnabled, primary: true, fontSize: 32f);
            }

            FestivalBeastUiArt.Text("PrimarySubtext", frame, rightX + 420f, 832f, rightW - 420f, 42f,
                vm.PrimarySubtext, 19f, FestivalBeastUiArt.Muted, wrap: true, alignment: TextAlignmentOptions.MidlineRight, autoSize: true);
        }
    }
}
