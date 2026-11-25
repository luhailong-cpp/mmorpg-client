using System;
using MmorpgClient.Game.FestivalBeast;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.FestivalBeast
{
    /// <summary>瑞兽迎春规则弹窗（对照 02-rules.png：四条规则行 + 动态文字「知道了」按钮）。</summary>
    public sealed class FestivalBeastRulesDialog : MonoBehaviour
    {
        private const float FrameX = 480f;
        private const float FrameY = 120f;
        private const float FrameW = 1600f;
        private const float FrameH = 840f;

        private Action _onClose;
        public bool IsVisible => gameObject.activeSelf;

        public void Configure(Action onClose) => _onClose = onClose;

        public void Show()
        {
            gameObject.SetActive(true);
            Render();
        }

        public void Hide() => gameObject.SetActive(false);

        private void Render()
        {
            FestivalBeastUiArt.Clear(transform);

            var frame = FestivalBeastUiArt.Rect("RulesFrameRoot", transform, FrameX, FrameY, FrameW, FrameH);
            FestivalBeastUiArt.SlicedImage("WindowFrame", frame, 0f, 0f, FrameW, FrameH, "window_frame", FestivalBeastUiArt.Ivory, raycast: true);

            FestivalBeastUiArt.SimpleImage("TitlePlate", frame, (FrameW - 560f) * 0.5f, -18f, 560f, 98f, "title_plate", FestivalBeastUiArt.DeepJade);
            FestivalBeastUiArt.Text("TitleText", frame, (FrameW - 420f) * 0.5f, -6f, 420f, 72f,
                "玩法说明", 38f, FestivalBeastUiArt.Ivory, bold: true, alignment: TextAlignmentOptions.Center);
            FestivalBeastUiArt.CloseButton(frame, FrameW - 86f, 10f, _onClose);

            // 左侧神兽装饰
            FestivalBeastUiArt.SimpleImage("PetPedestal", frame, 58f, 180f, 430f, 480f, "pet_display", new Color(0.92f, 0.89f, 0.8f, 0.45f));
            FestivalBeastUiArt.SimpleImage("BeastIllustration", frame, 74f, 178f, 400f, 368f, "beast_illustration", Color.clear);

            // 右侧 4 条清晰规则行
            const float listX = 524f;
            const float listW = 1006f;
            float y = 118f;
            var lines = FestivalBeastUiState.RulesLines;
            for (int i = 0; i < lines.Count; ++i)
            {
                const float rowH = 128f;
                FestivalBeastUiArt.SlicedImage("RuleRow_" + i, frame, listX, y, listW, rowH, "info_row", FestivalBeastUiArt.Ivory);
                FestivalBeastUiArt.Text("RuleText_" + i, frame, listX + 34f, y + 12f, listW - 68f, rowH - 24f,
                    lines[i], 23f, FestivalBeastUiArt.Ink, bold: false, wrap: true, alignment: TextAlignmentOptions.MidlineLeft, autoSize: true);
                y += rowH + 16f;
            }

            FestivalBeastUiArt.Button("ConfirmRulesBtn", frame, (FrameW - 340f) * 0.5f, FrameH - 118f, 340f, 82f,
                "知道了", _onClose, enabled: true, primary: true, fontSize: 30f);
        }
    }
}
