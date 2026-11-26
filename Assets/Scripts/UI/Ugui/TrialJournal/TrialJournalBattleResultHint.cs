using System;
using MmorpgClient.Game.TrialJournal;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.TrialJournal
{
    /// <summary>
    /// 战斗结算页嵌入式「试炼志行内提示条」（严格还原 D:\work\image\designs\trial-journal-ui-v1\previews\12-battle-result-hint.png）。
    /// 仅在存在有效 trial_journal_entry_id 时显示，不弹窗打断；点击「看试炼志」经宿主回调安全关闭结算并定位到对应关卡格。
    /// </summary>
    public sealed class TrialJournalBattleResultHint
    {
        public const float BarWidth = 820f;
        public const float BarHeight = 82f;

        private readonly RectTransform _root;
        private readonly TMP_Text _headline;
        private readonly TMP_Text _subtitle;
        private readonly Button _openButton;
        private uint _entryId;
        private readonly Action<uint> _onOpen;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public TrialJournalBattleResultHint(
            UnityEngine.Transform parent,
            float x,
            float y,
            Action<uint> onOpen)
        {
            _onOpen = onOpen;
            var plate = TrialJournalUiArt.SlicedOrTint("TrialJournalHintBar", parent,
                x, y, BarWidth, BarHeight, TrialJournalUiArt.LoadSkin("battle_result_hint_bar", "member_row"),
                TrialJournalUiArt.Paper, raycast: true);
            _root = (RectTransform)plate.transform;

            var dedicatedSeal = TrialJournalUiArt.LoadDedicatedOnly("seal_medallion_lit");
            var medallion = QdaoUguiFactory.CreateImage("SealMedallion", _root,
                22f, 15f, 52f, 52f, dedicatedSeal ?? TrialJournalUiArt.CircleSprite());
            if (dedicatedSeal != null)
            {
                medallion.type = Image.Type.Simple;
                medallion.preserveAspect = true;
                medallion.color = Color.white;
            }
            else
            {
                medallion.color = TrialJournalUiArt.SealGoldFill;
                var outline = medallion.gameObject.AddComponent<Outline>();
                outline.effectColor = TrialJournalUiArt.SealGold;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
            TrialJournalUiArt.Label("Mark", medallion.transform, 0f, 0f, 52f, 52f,
                "印", 24f, TrialJournalUiArt.Jade, bold: true, alignment: TextAlignmentOptions.Center);

            _headline = TrialJournalUiArt.Label("Headline", _root, 90f, 12f, 510f, 30f,
                "", 21f, TrialJournalUiArt.Jade, bold: true);
            _subtitle = TrialJournalUiArt.Label("Subtitle", _root, 90f, 44f, 510f, 26f,
                "", 15f, TrialJournalUiArt.Sub);

            _openButton = TrialJournalUiArt.SpriteButton("OpenTrialJournal", _root,
                620f, 15f, 180f, 52f, "看试炼志", 18f,
                primary: false, interactable: true,
                onClick: () =>
                {
                    if (_entryId != 0u) _onOpen?.Invoke(_entryId);
                });
            Hide();
        }

        public bool ShowForRecovered(global::PlayerBattleResult result, global::TrialJournalSnapshot snapshot)
        {
            if (result == null || result.TrialJournalEntryId == 0u)
            {
                Hide();
                return false;
            }
            _entryId = result.TrialJournalEntryId;
            var entry = TrialJournalModels.FindEntry(snapshot, _entryId);
            string entryName = entry != null && !string.IsNullOrEmpty(entry.DisplayName)
                ? entry.DisplayName
                : $"试炼格#{_entryId}";
            int newlyLit = result.TrialNewlyLitSealIds?.Count ?? 0;
            _headline.text = newlyLit > 0 ? $"{entryName} · 战法印 +{newlyLit}" : $"{entryName} · 试炼结算";

            string summary = TrialJournalModels.FormatBattleResultSummary(result, snapshot);
            if (string.IsNullOrEmpty(summary))
            {
                summary = snapshot != null && snapshot.Available
                    ? $"永久道印 {snapshot.TotalPermanentLitSeals}/{snapshot.TotalPermanentSeals} · 点击查看复盘与里程碑"
                    : "点击查看试炼志复盘与里程碑进度";
            }
            _subtitle.text = summary;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            return true;
        }

        public bool ShowForLive(global::BattleEndS2C end, global::TrialJournalSnapshot snapshot)
        {
            uint entryId = end?.Settlement?.TrialFacts?.JournalEntryId ?? 0u;
            if (entryId == 0u)
            {
                Hide();
                return false;
            }
            _entryId = entryId;
            var entry = TrialJournalModels.FindEntry(snapshot, _entryId);
            string entryName = entry != null && !string.IsNullOrEmpty(entry.DisplayName)
                ? entry.DisplayName
                : $"试炼格#{_entryId}";
            _headline.text = $"{entryName} · 试炼志已记档";
            _subtitle.text = snapshot != null && snapshot.Available
                ? $"永久道印 {snapshot.TotalPermanentLitSeals}/{snapshot.TotalPermanentSeals} · 战法印与纪录将在返回后同步"
                : "战法印、配装复盘与里程碑已在结算同步";
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            return true;
        }

        public void SetVerticalPosition(float y)
        {
            if (_root != null) _root.anchoredPosition = new Vector2(_root.anchoredPosition.x, -y);
        }

        public void Hide()
        {
            _entryId = 0u;
            if (_root != null) _root.gameObject.SetActive(false);
        }
    }
}
