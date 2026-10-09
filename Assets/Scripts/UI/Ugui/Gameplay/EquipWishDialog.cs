using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MmorpgClient.World.Tianyong;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>Reusable selection dialog. Confirmation returns a local draft; this view never sends a refine RPC.</summary>
    public sealed class EquipWishDialog
    {
        public const float Width = 1480, Height = 900;
        public const int OptionsPerPage = 21;
        private readonly RectTransform _root, _frame, _list;
        private readonly EquipWishDialogInput _input;
        private readonly EquipWishSelection _selection = new EquipWishSelection();
        private readonly TMP_Text _title, _summary, _pageLabel;
        private readonly Button _confirm, _clear, _previous, _next, _cancel, _close;
        private readonly List<Selectable> _optionButtons = new List<Selectable>();
        private Action<uint[]> _confirmed;
        private Action _cancelled;
        private GameObject _previousFocus;
        private int _page;
        public bool IsVisible => _root.gameObject.activeSelf;

        public EquipWishDialog(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("EquipWishDialog", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .76f); shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            _input = _root.gameObject.AddComponent<EquipWishDialogInput>();
            _input.CancelRequested = Cancel;
            _frame = QdaoUguiFactory.CreateCenteredRect("WishPanel", _root, Width, Height);
            EquipUiSkin.Art(_frame, "wish_panel", 0, 0, Width, Height, QdaoUguiTheme.PanelPaper, true);
            _title = Text(_frame, "选择期望属性", 260, 54, 960, 74, 44, Cream,
                alignment: TextAlignmentOptions.Center);
            _close = EquipUiSkin.Button(_frame, "CloseWishDialog", "", Width - 113, 24, 80, 80,
                Cancel, artKey: "close");
            _list = QdaoUguiFactory.CreateRect("WishOptions", _frame, 84, 178, 1312, 490);
            _summary = Text(_frame, "", 84, 684, 910, 38, 28, Muted);
            _previous = EquipUiSkin.Button(_frame, "WishPreviousPage", "上一页", 994, 681, 120, 44,
                () => ChangePage(-1), fontSize: 24);
            _pageLabel = Text(_frame, "", 1122, 681, 126, 44, 24, Muted, alignment: TextAlignmentOptions.Center);
            _next = EquipUiSkin.Button(_frame, "WishNextPage", "下一页", 1260, 681, 136, 44,
                () => ChangePage(1), fontSize: 24);
            Text(_frame, "勾选后确认，可随时重新调整。", 84, 729, 1312, 35, 26, Muted,
                alignment: TextAlignmentOptions.Center).name = "WishSelectionHint";
            _clear = EquipUiSkin.Button(_frame, "ClearWishSelection", "全部清除", 84, 789, 280, 68,
                () => { _selection.Clear(); RenderOptions(); FocusFirst(); });
            _cancel = EquipUiSkin.Button(_frame, "CancelWishSelection", "取消", 798, 789, 280, 68, Cancel);
            _confirm = EquipUiSkin.Button(_frame, "ConfirmWishSelection", "确认选择", 1116, 789, 280, 68,
                Confirm, true);
        }

        public void Show(IEnumerable<EquipWishOption> options, IEnumerable<uint> selected,
            Action<uint[]> confirmed, Action cancelled = null, string title = "选择期望属性")
        {
            if (!IsVisible) _previousFocus = EventSystem.current?.currentSelectedGameObject;
            _selection.Reset(options, selected); _page = 0;
            _confirmed = confirmed; _cancelled = cancelled;
            _title.text = string.IsNullOrWhiteSpace(title) ? "选择期望属性" : title;
            _root.SetAsLastSibling(); _root.gameObject.SetActive(true);
            RenderOptions(); FocusFirst();
        }

        public void Hide()
        {
            if (!IsVisible) return;
            _root.gameObject.SetActive(false);
            _confirmed = null; _cancelled = null;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(
                    _previousFocus != null && _previousFocus.activeInHierarchy ? _previousFocus : null);
            _previousFocus = null;
        }

        public void Cancel()
        {
            if (!IsVisible) return;
            var callback = _cancelled; Hide(); callback?.Invoke();
        }

        private void Confirm()
        {
            if (!IsVisible || !_selection.CanConfirm || _confirmed == null) return;
            var callback = _confirmed; var ids = _selection.Snapshot(); Hide(); callback(ids);
        }

        private void ChangePage(int delta)
        {
            int pages = Math.Max(1, (_selection.Options.Count + OptionsPerPage - 1) / OptionsPerPage);
            _page = Math.Max(0, Math.Min(pages - 1, _page + delta));
            RenderOptions(); FocusFirst();
        }

        private void RenderOptions()
        {
            Clear(_list); _optionButtons.Clear();
            var options = _selection.Options;
            int start = _page * OptionsPerPage;
            for (int i = 0; i < OptionsPerPage && start + i < options.Count; ++i)
            {
                var option = options[start + i];
                float x = i % 3 * 440, y = i / 3 * 70;
                var button = QdaoUguiFactory.CreateArtButton("WishOption_" + option.Id, _list,
                    x, y, 426, 64, null, out var background);
                background.color = Color.white;
                var colors = button.colors;
                colors.normalColor = new Color(.11f, .39f, .30f, .055f);
                colors.highlightedColor = new Color(.11f, .39f, .30f, .14f);
                colors.selectedColor = new Color(.83f, .61f, .19f, .24f);
                colors.pressedColor = new Color(.11f, .39f, .30f, .25f);
                colors.colorMultiplier = 1f; button.colors = colors;
                var box = EquipUiSkin.Art(button.transform,
                    _selection.IsSelected(option.Id) ? "checkbox_on" : "checkbox_off", 5, 8, 48, 48,
                    _selection.IsSelected(option.Id) ? QdaoUguiTheme.Html("#176C5F") : QdaoUguiTheme.Html("#CDBA86"));
                var label = Text(button.transform, option.Label, 66, 5, 354, 58, 30, Ink, true);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                button.onClick.AddListener(() => {
                    _selection.Toggle(option.Id);
                    EquipUiSkin.Apply(box, _selection.IsSelected(option.Id) ? "checkbox_on" : "checkbox_off",
                        _selection.IsSelected(option.Id) ? QdaoUguiTheme.Html("#176C5F") : QdaoUguiTheme.Html("#CDBA86"));
                    UpdateSummary();
                });
                _optionButtons.Add(button);
            }
            if (options.Count == 0)
            {
                var unavailable = Text(_list, "尚未提供可选属性\n请等待该装备的属性范围同步后再选择", 70, 125, 1172, 170, 34,
                    Muted, true, TextAlignmentOptions.Center);
                unavailable.name = "WishOptionsUnavailable";
                unavailable.alignment = TextAlignmentOptions.Center;
            }
            int pageCount = Math.Max(1, (options.Count + OptionsPerPage - 1) / OptionsPerPage);
            _previous.gameObject.SetActive(pageCount > 1); _next.gameObject.SetActive(pageCount > 1);
            _pageLabel.gameObject.SetActive(pageCount > 1);
            _previous.interactable = _page > 0; _next.interactable = _page + 1 < pageCount;
            _pageLabel.text = $"{_page + 1} / {pageCount}";
            UpdateSummary(); ConfigureNavigation();
        }

        private void UpdateSummary()
        {
            _summary.text = _selection.CanConfirm ? $"已选择 {_selection.Count} 项" : "可选属性尚未提供";
            _confirm.interactable = _selection.CanConfirm && _confirmed != null;
            _clear.interactable = _selection.CanConfirm;
        }

        private void ConfigureNavigation()
        {
            var controls = new List<Selectable>(_optionButtons);
            foreach (var button in new[] { _previous, _next, _clear, _cancel, _confirm, _close })
                if (button.gameObject.activeSelf && button.interactable) controls.Add(button);
            for (int i = 0; i < controls.Count; ++i)
            {
                var previous = controls[(i + controls.Count - 1) % controls.Count];
                var next = controls[(i + 1) % controls.Count];
                controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = previous, selectOnLeft = previous, selectOnDown = next, selectOnRight = next };
            }
            _input.Controls = controls;
        }
        private void FocusFirst() => _input.FocusFirst();
    }
}
