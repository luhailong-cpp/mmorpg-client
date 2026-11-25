using System;
using MmorpgClient.Game.FestivalBeast;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.FestivalBeast
{
    /// <summary>
    /// 嵌入既有 BattleResultPanel 下方的瑞兽迎春结算区块（对照 03-victory.png 与第五节要求）。
    /// 区分普通胜利 / 首次三胜 / 已有资格再胜 / 已拥有再胜 / 失败逃跑作废 / 结算处理中六类状态，
    /// 材料文案统一使用「本局新增可领取额度 +N」，领取神兽须等服务端持久确认且不自动顶替出战宝宝。
    /// </summary>
    public sealed class FestivalBeastSettlementSection : MonoBehaviour
    {
        private Action _onClaimBeast;
        private Action _onOpenActivity;
        private Action _onOpenMaterialClaim;

        public bool IsVisible => gameObject.activeSelf;

        public void Configure(Action onClaimBeast, Action onOpenActivity, Action onOpenMaterialClaim)
        {
            _onClaimBeast = onClaimBeast;
            _onOpenActivity = onOpenActivity;
            _onOpenMaterialClaim = onOpenMaterialClaim;
        }

        public void Show(FestivalBeastSettlementViewModel vm)
        {
            gameObject.SetActive(true);
            Render(vm);
        }

        public void Hide() => gameObject.SetActive(false);

        public void Render(FestivalBeastSettlementViewModel vm)
        {
            if (vm == null) return;
            FestivalBeastUiArt.Clear(transform);

            var rect = transform as RectTransform;
            float width = rect != null && rect.rect.width > 0f ? rect.rect.width : 820f;
            float height = rect != null && rect.rect.height > 0f ? rect.rect.height : 176f;
            float scale = Mathf.Min(width / 820f, height / 176f);
            float offsetX = (width - 820f * scale) * 0.5f;
            float offsetY = (height - 176f * scale) * 0.5f;

            var body = FestivalBeastUiArt.Rect("FestivalSettlementBody", transform, offsetX, offsetY, 820f, 176f);
            body.localScale = new UnityEngine.Vector3(scale, scale, 1f);

            FestivalBeastUiArt.SlicedImage("CardBg", body, 0f, 0f, 820f, 176f, "content_card", FestivalBeastUiArt.Ivory);
            FestivalBeastUiArt.SimpleImage("PortraitFrame", body, 14f, 34f, 108f, 108f, "portrait_frame", FestivalBeastUiArt.WarmGold);
            FestivalBeastUiArt.SimpleImage("BeastPortrait", body, 22f, 42f, 92f, 92f, "beast_portrait", Color.clear);

            const float textX = 136f;
            const float textW = 472f;
            FestivalBeastUiArt.Text("Headline", body, textX, 10f, textW, 34f,
                vm.HeadlineText, 24f, vm.IsSettlingOrUnknown ? FestivalBeastUiArt.WarnAmber : FestivalBeastUiArt.DeepJade,
                bold: true, autoSize: true);
            FestivalBeastUiArt.Text("ProgressLine", body, textX, 46f, textW, 28f,
                vm.ProgressLineText, 20f, FestivalBeastUiArt.Ink, bold: true, autoSize: true);
            FestivalBeastUiArt.Text("MaterialGainLine", body, textX, 76f, textW, 28f,
                vm.MaterialGainLineText, 20f, FestivalBeastUiArt.DeepJade, bold: true, autoSize: true);
            FestivalBeastUiArt.Text("MaterialBalanceLine", body, textX, 106f, textW, 26f,
                vm.MaterialBalanceLineText, 17f, FestivalBeastUiArt.Muted, autoSize: true);
            FestivalBeastUiArt.Text("SubNote", body, textX, 134f, textW, 32f,
                vm.SubNoteText, 16f, FestivalBeastUiArt.Muted, wrap: true, autoSize: true);

            // 右侧操作区（触控高度 ≥ 52px）
            const float btnX = 620f;
            const float btnW = 186f;
            if (vm.ShowClaimBeastButton)
            {
                FestivalBeastUiArt.Button("ClaimBeastBtn", body, btnX, 16f, btnW, 64f,
                    vm.ClaimBeastButtonText, _onClaimBeast, enabled: vm.ClaimBeastButtonEnabled, primary: true, fontSize: 23f);
                FestivalBeastUiArt.Button("BackActivityBtn", body, btnX, 96f, btnW, 56f,
                    "返回活动", _onOpenActivity, enabled: true, primary: false, fontSize: 20f);
            }
            else if (vm.ShowMaterialJumpButton)
            {
                FestivalBeastUiArt.Button("OpenActivityBtn", body, btnX, 16f, btnW, 60f,
                    vm.ShowRetryButton ? "返回活动" : "查看活动", _onOpenActivity, enabled: true, primary: true, fontSize: 21f);
                FestivalBeastUiArt.Button("ClaimMaterialBtn", body, btnX, 92f, btnW, 56f,
                    "领取材料", _onOpenMaterialClaim, enabled: true, primary: false, fontSize: 20f);
            }
            else
            {
                FestivalBeastUiArt.Button("OpenActivityBtn", body, btnX, 56f, btnW, 64f,
                    "返回活动", _onOpenActivity, enabled: !vm.IsSettlingOrUnknown, primary: false, fontSize: 22f);
            }
        }
    }
}
