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
    /// <summary>Native presentation for the forge snapshot. Intent methods remain in EquipForgeUiState.</summary>
    public sealed class EquipForgeDialog : IDisposable
    {
        public const float Width = 1480, Height = 900;
        public const int GemsPerPage = 6;
        private const int PreviewLinesPerPage = 3;
        private readonly EquipForgeUiState _forge;
        private readonly RectTransform _root, _frame, _ready, _icon, _preview, _sockets;
        private readonly RectTransform _pickerLayer, _pickerList, _confirmLayer;
        private readonly EquipWishDialogInput _input;
        private readonly ScrollRect _socketScroll;
        private readonly TMP_Text _title, _location, _stage, _status, _level, _rate, _fail, _cost, _owned;
        private readonly TMP_Text _enhanceHint, _refreshHint, _previewPageLabel;
        private readonly TMP_Text _pickerTitle, _pickerContext, _pickerHint, _pickerPageLabel, _confirmTitle, _confirmMessage, _confirmHint;
        private readonly Button _enhance, _refresh, _close, _back, _previewPrevious, _previewNext;
        private readonly Button _pickerPrevious, _pickerNext, _cancelInlay, _confirmEnhance, _cancelConfirm;
        private readonly List<Selectable> _socketButtons = new List<Selectable>();
        private readonly List<Selectable> _gemButtons = new List<Selectable>();
        private Action _closed;
        private GameObject _returnFocus;
        private ulong _itemId;
        private uint? _pickerSocket;
        private int _gemPage, _previewPage;
        private int _activeLayer;
        private string _layerReturnFocusName;
        private bool _disposed;

        public bool IsVisible => !_disposed && _root != null && _root.gameObject.activeSelf;

        public EquipForgeDialog(UnityEngine.Transform parent, EquipForgeUiState forge)
        {
            _forge = forge ?? throw new ArgumentNullException(nameof(forge));
            _root = QdaoUguiFactory.CreateStretch("EquipForgeDialog", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            Shade(_root, .76f);
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            _input = _root.gameObject.AddComponent<EquipWishDialogInput>();
            _input.CancelRequested = CancelTopLayer;
            _frame = QdaoUguiFactory.CreateCenteredRect("EquipForgePanel", _root, Width, Height);
            EquipUiSkin.Art(_frame, "wish_panel", 0, 0, Width, Height, EquipUiSkin.Paper, true);
            Label(_frame, "EquipForgeHeading", "强化 / 镶嵌", 260, 54, 960, 74, 44,
                EquipUiSkin.HeaderText, alignment: TextAlignmentOptions.Center);
            _close = EquipUiSkin.Button(_frame, "EquipForgeClose", "", Width - 113, 24, 80, 80,
                Close, artKey: "close");
            _icon = QdaoUguiFactory.CreateRect("EquipForgeIcon", _frame, 84, 148, 94, 94);
            _title = Label(_frame, "EquipForgeTitle", "", 202, 146, 1194, 54, 38, EquipUiSkin.Ink);
            Fit(_title, 26, 38);
            _location = Label(_frame, "EquipForgeLocation", "", 202, 204, 1194, 34, 26, EquipUiSkin.Muted);
            _status = Label(_frame, "EquipForgeStatus", "", 84, 250, 1312, 58, 28, EquipUiSkin.Jade, true);
            Fit(_status, 23, 28);
            _stage = Label(_frame, "EquipForgeStage", "", 140, 370, 1200, 220, 34,
                EquipUiSkin.Muted, true, TextAlignmentOptions.Center);
            _stage.alignment = TextAlignmentOptions.Center;
            _ready = QdaoUguiFactory.CreateRect("EquipForgeReady", _frame, 0, 0, Width, Height);
            Label(_ready, "EquipForgeEnhanceHeading", "装备强化", 84, 318, 620, 42, 34, EquipUiSkin.Jade);
            Label(_ready, "EquipForgeSocketsHeading", "宝石镶嵌", 758, 318, 638, 42, 34, EquipUiSkin.Jade);
            _level = Label(_ready, "EquipForgeLevel", "", 84, 366, 620, 38, 30, EquipUiSkin.Ink);
            _rate = Label(_ready, "EquipForgeSuccessRate", "", 84, 410, 284, 36, 28, EquipUiSkin.Jade);
            _fail = Label(_ready, "EquipForgeFailRule", "", 378, 410, 326, 36, 27, EquipUiSkin.Gold);
            _cost = Label(_ready, "EquipForgeCost", "", 84, 454, 620, 62, 28, EquipUiSkin.Ink, true);
            _owned = Label(_ready, "EquipForgeOwned", "", 84, 524, 620, 62, 27, EquipUiSkin.Muted, true);
            Fit(_cost, 23, 28); Fit(_owned, 23, 27);
            Label(_ready, "EquipForgePreviewHeading", "属性预览", 84, 596, 288, 34, 27, EquipUiSkin.Jade);
            _preview = QdaoUguiFactory.CreateRect("EquipForgePreview", _ready, 84, 640, 620, 96);
            _previewPrevious = EquipUiSkin.Button(_ready, "EquipForgePreviewPrevious", "上一页", 398, 592, 100, 42,
                () => ChangePreviewPage(-1), fontSize: 22);
            _previewPageLabel = Label(_ready, "EquipForgePreviewPage", "", 500, 592, 100, 42, 22,
                EquipUiSkin.Muted, alignment: TextAlignmentOptions.Center);
            _previewNext = EquipUiSkin.Button(_ready, "EquipForgePreviewNext", "下一页", 604, 592, 100, 42,
                () => ChangePreviewPage(1), fontSize: 22);
            _socketScroll = SocketScroll(_ready, out _sockets);
            _enhanceHint = Label(_ready, "EquipForgeEnhanceHint", "", 84, 742, 620, 38, 24, EquipUiSkin.Muted, true);
            Fit(_enhanceHint, 20, 24);
            _enhance = EquipUiSkin.Button(_ready, "EquipForgeEnhance", "强化", 84, 789, 300, 68, _forge.Enhance, true);
            _refreshHint = Label(_frame, "EquipForgeRefreshHint", "", 760, 742, 636, 38, 24, EquipUiSkin.Muted, true);
            Fit(_refreshHint, 20, 24);
            _refresh = EquipUiSkin.Button(_frame, "EquipForgeRefresh", "刷新", 798, 789, 280, 68, _forge.Refresh);
            _back = EquipUiSkin.Button(_frame, "EquipForgeBack", "返回装备", 1116, 789, 280, 68, Close);

            _pickerLayer = QdaoUguiFactory.CreateStretch("EquipForgeGemPicker", _root, Vector4.zero);
            Shade(_pickerLayer, .68f);
            var pickerFrame = QdaoUguiFactory.CreateCenteredRect("EquipForgeGemPickerPanel", _pickerLayer, 1160, 780);
            EquipUiSkin.Art(pickerFrame, "wish_panel", 0, 0, 1160, 780, EquipUiSkin.Paper, true);
            _pickerTitle = Label(pickerFrame, "EquipForgePickerTitle", "", 110, 52, 940, 70, 34,
                EquipUiSkin.HeaderText, alignment: TextAlignmentOptions.Center);
            Fit(_pickerTitle, 24, 34);
            _pickerContext = Label(pickerFrame, "EquipForgePickerContext", "", 76, 139, 1008, 35, 27,
                EquipUiSkin.Muted, alignment: TextAlignmentOptions.Center);
            Fit(_pickerContext, 23, 27);
            Label(pickerFrame, "EquipForgePickerInstruction", "选中后立即镶嵌。", 76, 177, 1008, 27, 23,
                EquipUiSkin.Muted, alignment: TextAlignmentOptions.Center);
            _pickerList = QdaoUguiFactory.CreateRect("EquipForgeGemOptions", pickerFrame, 76, 216, 1008, 384);
            _pickerPrevious = EquipUiSkin.Button(pickerFrame, "EquipForgeGemPrevious", "上一页", 76, 596, 160, 48,
                () => ChangeGemPage(-1), fontSize: 25);
            _pickerPageLabel = Label(pickerFrame, "EquipForgeGemPage", "", 256, 596, 648, 48, 25,
                EquipUiSkin.Muted, alignment: TextAlignmentOptions.Center);
            _pickerNext = EquipUiSkin.Button(pickerFrame, "EquipForgeGemNext", "下一页", 924, 596, 160, 48,
                () => ChangeGemPage(1), fontSize: 25);
            _pickerHint = Label(pickerFrame, "EquipForgePickerHint", "", 76, 654, 1008, 40, 24,
                EquipUiSkin.Muted, true, TextAlignmentOptions.Center);
            _pickerHint.alignment = TextAlignmentOptions.Center;
            _cancelInlay = EquipUiSkin.Button(pickerFrame, "EquipForgeCancelInlay", "取消", 430, 702, 300, 58,
                _forge.CancelInlay);
            _pickerLayer.gameObject.SetActive(false);

            _confirmLayer = QdaoUguiFactory.CreateStretch("EquipForgeEnhanceConfirm", _root, Vector4.zero);
            Shade(_confirmLayer, .68f);
            var confirmFrame = QdaoUguiFactory.CreateCenteredRect("EquipForgeEnhanceConfirmPanel", _confirmLayer, 1060, 600);
            EquipUiSkin.Art(confirmFrame, "wish_panel", 0, 0, 1060, 600, EquipUiSkin.Paper, true);
            _confirmTitle = Label(confirmFrame, "EquipForgeConfirmTitle", "", 150, 40, 760, 68, 38,
                EquipUiSkin.HeaderText, alignment: TextAlignmentOptions.Center);
            _confirmMessage = Label(confirmFrame, "EquipForgeConfirmMessage", "", 80, 166, 900, 190, 31,
                EquipUiSkin.Ink, true);
            Fit(_confirmMessage, 25, 31);
            _confirmHint = Label(confirmFrame, "EquipForgeConfirmHint", "", 80, 376, 900, 52, 25,
                EquipUiSkin.Muted, true, TextAlignmentOptions.Center);
            _confirmHint.alignment = TextAlignmentOptions.Center;
            _cancelConfirm = EquipUiSkin.Button(confirmFrame, "EquipForgeCancelConfirm", "取消", 110, 474, 360, 70,
                _forge.CancelConfirm);
            _confirmEnhance = EquipUiSkin.Button(confirmFrame, "EquipForgeConfirmEnhance", "确认强化", 590, 474, 360, 70,
                _forge.ConfirmEnhance, true);
            _confirmLayer.gameObject.SetActive(false);
            _forge.Changed += OnChanged;
        }

        /// <summary>The owner opens the state for an item before showing this view.</summary>
        public void Show(Action closed = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EquipForgeDialog));
            if (!IsVisible) _returnFocus = EventSystem.current?.currentSelectedGameObject;
            _closed = closed;
            _root.SetAsLastSibling(); _root.gameObject.SetActive(true);
            Render();
        }

        public void Close()
        {
            if (_disposed) return;
            _forge.Close();
            Hide(true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _forge.Changed -= OnChanged;
            _forge.Close();
            Hide(false);
            _disposed = true;
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
                else UnityEngine.Object.DestroyImmediate(_root.gameObject);
            }
        }

        private void OnChanged()
        {
            if (IsVisible) Render();
        }

        private void Hide(bool notify)
        {
            if (!IsVisible) return;
            var callback = _closed;
            _closed = null;
            _root.gameObject.SetActive(false);
            _input.Controls.Clear();
            _pickerSocket = null;
            _activeLayer = 0; _layerReturnFocusName = null;
            var focus = _returnFocus;
            _returnFocus = null;
            EventSystem.current?.SetSelectedGameObject(focus != null && focus.activeInHierarchy ? focus : null);
            if (notify) callback?.Invoke();
        }

        private void CancelTopLayer()
        {
            if (!IsVisible) return;
            if (_forge.View.Picker != null) _forge.CancelInlay();
            else if (_forge.View.Confirm != null) _forge.CancelConfirm();
            else Close();
        }

        private void Render()
        {
            var view = _forge.View;
            if (view.Stage == EquipForgeStage.Closed) { Hide(true); return; }
            string selectedName = EventSystem.current?.currentSelectedGameObject?.name;
            int layer = view.Picker != null ? 1 : view.Confirm != null ? 2 : 0;
            if (layer != _activeLayer)
            {
                if (_activeLayer == 0) _layerReturnFocusName = selectedName;
                if (layer == 0)
                {
                    selectedName = _layerReturnFocusName;
                    _layerReturnFocusName = null;
                }
                _activeLayer = layer;
            }
            if (_itemId != view.ItemId)
            {
                _itemId = view.ItemId; _previewPage = _gemPage = 0;
                _socketScroll.verticalNormalizedPosition = 1;
            }
            _title.text = view.Title; _location.text = view.LocationText;
            Clear(_icon);
            if (view.Stage == EquipForgeStage.Ready) ItemIcon(_icon, view.IconKey, 0, 0, 94);
            _stage.text = view.StageText;
            _stage.gameObject.SetActive(view.Stage != EquipForgeStage.Ready);
            _ready.gameObject.SetActive(view.Stage == EquipForgeStage.Ready);
            _status.text = view.StatusText; _status.color = Tone(view.StatusTone);
            SetButton(_refresh, view.RefreshButton.Label, view.RefreshButton.Enabled);
            _refreshHint.text = view.RefreshButton.Hint;
            _level.text = view.EnhanceLevelText;
            _rate.text = view.HasNextLevel ? view.SuccessRateText : string.Empty;
            _fail.text = view.HasNextLevel ? view.FailRuleText : string.Empty;
            _cost.text = view.HasNextLevel ? view.CostText : string.Empty;
            _owned.text = view.HasNextLevel ? view.OwnedText : string.Empty;
            SetButton(_enhance, view.EnhanceButton.Label, view.EnhanceButton.Enabled);
            _enhanceHint.text = view.EnhanceButton.Hint;
            RenderPreview(view);
            RenderSockets(view);
            RenderPicker(view.Picker);
            RenderConfirm(view.Confirm);
            ConfigureNavigation(selectedName, view);
        }

        private void RenderPreview(EquipForgeView view)
        {
            Clear(_preview);
            int pages = Math.Max(1, (view.PreviewLines.Count + PreviewLinesPerPage - 1) / PreviewLinesPerPage);
            _previewPage = Math.Max(0, Math.Min(pages - 1, _previewPage));
            for (int i = 0; i < PreviewLinesPerPage && _previewPage * PreviewLinesPerPage + i < view.PreviewLines.Count; i++)
                Label(_preview, "EquipForgePreview_" + (_previewPage * PreviewLinesPerPage + i),
                    view.PreviewLines[_previewPage * PreviewLinesPerPage + i], 0, i * 32, 620, 32, 26, EquipUiSkin.Ink);
            _previewPrevious.gameObject.SetActive(pages > 1); _previewNext.gameObject.SetActive(pages > 1);
            _previewPageLabel.gameObject.SetActive(pages > 1);
            _previewPrevious.interactable = _previewPage > 0;
            _previewNext.interactable = _previewPage + 1 < pages;
            _previewPageLabel.text = $"{_previewPage + 1} / {pages}";
        }

        private void RenderSockets(EquipForgeView view)
        {
            float scrollY = _sockets.anchoredPosition.y;
            Clear(_sockets); _socketButtons.Clear();
            _sockets.sizeDelta = new Vector2(638, Math.Max(372, view.Sockets.Count * 124));
            for (int i = 0; i < view.Sockets.Count; i++)
            {
                var row = view.Sockets[i];
                uint socket = row.Socket;
                bool remove = row.IsRemove;
                float y = i * 124;
                var host = QdaoUguiFactory.CreateRect("EquipForgeSocketRow_" + socket, _sockets, 0, y, 622, 116);
                var plate = host.gameObject.AddComponent<Image>();
                plate.color = new Color(.14f, .35f, .27f, .045f); plate.raycastTarget = false;
                if (!string.IsNullOrEmpty(row.GemIconKey)) ItemIcon(host, row.GemIconKey, 12, 14, 62);
                else EquipUiSkin.Art(host, "checkbox_off", 18, 22, 48, 48, EquipUiSkin.Gold);
                var text = Label(host, "EquipForgeSocketText_" + socket, row.Text, 88, 10, 350, 64, 26,
                    row.Kind == EquipForgeSocketKind.Inactive ? EquipUiSkin.Muted : EquipUiSkin.Ink, true);
                Fit(text, 21, 26);
                var button = EquipUiSkin.Button(host, "EquipForgeSocket_" + socket, row.Button.Label, 458, 14, 148, 58,
                    () => { if (remove) _forge.RemoveGem(socket); else _forge.BeginInlay(socket); },
                    enabled: row.Button.Enabled, fontSize: 27);
                var hint = Label(host, "EquipForgeSocketHint_" + socket, row.Button.Hint, 88, 79, 518, 32, 22,
                    EquipUiSkin.Muted);
                Fit(hint, 18, 22);
                int rowIndex = i;
                var focus = button.gameObject.AddComponent<EquipForgeSocketFocus>();
                focus.Selected = () => RevealSocket(rowIndex);
                _socketButtons.Add(button);
            }
            if (view.Sockets.Count == 0)
                Label(_sockets, "EquipForgeSocketsEmpty", view.SocketsEmptyText, 24, 40, 590, 100, 28,
                    EquipUiSkin.Muted, true, TextAlignmentOptions.Center).alignment = TextAlignmentOptions.Center;
            _sockets.anchoredPosition = new Vector2(0, Mathf.Clamp(scrollY, 0, Math.Max(0, _sockets.sizeDelta.y - 372)));
        }

        private void RenderPicker(EquipForgeGemPicker picker)
        {
            _pickerLayer.gameObject.SetActive(picker != null);
            Clear(_pickerList); _gemButtons.Clear();
            if (picker == null) { _pickerSocket = null; return; }
            if (_pickerSocket != picker.Socket) { _pickerSocket = picker.Socket; _gemPage = 0; }
            _pickerTitle.text = "选择宝石"; _pickerContext.text = picker.Title; _pickerHint.text = picker.Hint;
            int pages = Math.Max(1, (picker.Options.Count + GemsPerPage - 1) / GemsPerPage);
            _gemPage = Math.Max(0, Math.Min(pages - 1, _gemPage));
            for (int i = 0; i < GemsPerPage && _gemPage * GemsPerPage + i < picker.Options.Count; i++)
            {
                var option = picker.Options[_gemPage * GemsPerPage + i];
                uint configId = option.ConfigId;
                var button = EquipUiSkin.Button(_pickerList, "EquipForgeGem_" + configId, "",
                    i % 2 * 512, i / 2 * 128, 496, 116, () => _forge.ChooseGem(configId), enabled: picker.CanChoose);
                ItemIcon(button.transform, option.IconKey, 14, 22, 70);
                var name = Label(button.transform, "EquipForgeGemName_" + configId, option.Name, 100, 12, 356, 42, 28,
                    EquipUiSkin.Ink);
                Fit(name, 21, 28);
                Label(button.transform, "EquipForgeGemCount_" + configId, option.CountText, 320, 62, 96, 42, 25,
                    EquipUiSkin.Jade, alignment: TextAlignmentOptions.MidlineRight);
                var attr = Label(button.transform, "EquipForgeGemAttr_" + configId, option.AttrText, 100, 62, 204, 42, 25,
                    EquipUiSkin.Muted, true);
                Fit(attr, 20, 25);
                _gemButtons.Add(button);
            }
            _pickerPrevious.gameObject.SetActive(pages > 1); _pickerNext.gameObject.SetActive(pages > 1);
            _pickerPageLabel.text = $"第 {_gemPage + 1} / {pages} 页 · 共 {picker.Options.Count} 种宝石";
            _pickerPrevious.interactable = _gemPage > 0; _pickerNext.interactable = _gemPage + 1 < pages;
        }

        private void RenderConfirm(EquipForgeConfirm confirm)
        {
            _confirmLayer.gameObject.SetActive(confirm != null);
            if (confirm == null) return;
            _confirmTitle.text = confirm.Title; _confirmMessage.text = confirm.Message; _confirmHint.text = confirm.Hint;
            SetButton(_confirmEnhance, confirm.ConfirmLabel, confirm.CanConfirm);
            SetButton(_cancelConfirm, confirm.CancelLabel, true);
        }

        private void ConfigureNavigation(string selectedName, EquipForgeView view)
        {
            var controls = new List<Selectable>();
            if (view.Picker != null)
            {
                foreach (var button in _gemButtons) AddControl(controls, button);
                AddControl(controls, _pickerPrevious); AddControl(controls, _pickerNext); AddControl(controls, _cancelInlay);
            }
            else if (view.Confirm != null)
            {
                // Cancellation owns the initial focus so opening the warning cannot accidentally submit it.
                AddControl(controls, _cancelConfirm); AddControl(controls, _confirmEnhance);
            }
            else
            {
                AddControl(controls, _enhance);
                AddControl(controls, _previewPrevious); AddControl(controls, _previewNext);
                foreach (var button in _socketButtons) AddControl(controls, button);
                AddControl(controls, _refresh); AddControl(controls, _back); AddControl(controls, _close);
            }
            for (int i = 0; i < controls.Count; i++)
            {
                var previous = controls[(i + controls.Count - 1) % controls.Count];
                var next = controls[(i + 1) % controls.Count];
                controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = previous, selectOnLeft = previous, selectOnDown = next, selectOnRight = next };
            }
            _input.Controls = controls;
            if (EventSystem.current == null || controls.Count == 0) return;
            var selected = controls.Find(control => control.name == selectedName);
            EventSystem.current.SetSelectedGameObject(selected != null ? selected.gameObject : controls[0].gameObject);
        }

        private void ChangeGemPage(int delta)
        {
            if (!IsVisible || _forge.View.Picker == null) return;
            _gemPage += delta; Render();
        }

        private void ChangePreviewPage(int delta)
        {
            if (!IsVisible) return;
            _previewPage += delta; Render();
        }

        private void RevealSocket(int index)
        {
            float top = index * 124, bottom = top + 116, offset = _sockets.anchoredPosition.y;
            if (top < offset) offset = top;
            else if (bottom > offset + 372) offset = bottom - 372;
            _sockets.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Math.Max(0, _sockets.sizeDelta.y - 372)));
        }

        private static ScrollRect SocketScroll(UnityEngine.Transform parent, out RectTransform content)
        {
            var viewport = QdaoUguiFactory.CreateRect("EquipForgeSocketsViewport", parent, 758, 374, 638, 372);
            viewport.gameObject.AddComponent<RectMask2D>();
            var surface = viewport.gameObject.AddComponent<Image>();
            surface.color = new Color(1, 1, 1, .001f); surface.raycastTarget = true;
            content = QdaoUguiFactory.CreateRect("EquipForgeSockets", viewport, 0, 0, 638, 372);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48; scroll.inertia = false;
            return scroll;
        }

        private static void AddControl(List<Selectable> controls, Selectable control)
        {
            if (control != null && control.gameObject.activeInHierarchy && control.interactable) controls.Add(control);
        }

        private static TMP_Text Label(UnityEngine.Transform parent, string name, string value,
            float x, float y, float w, float h, float size, Color color, bool wrap = false,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var text = Text(parent, value, x, y, w, h, size, color, wrap, alignment);
            text.name = name;
            return text;
        }

        private static void Fit(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true; text.fontSizeMin = min; text.fontSizeMax = max;
        }

        private static void SetButton(Button button, string label, bool enabled)
        {
            button.interactable = enabled;
            button.GetComponentInChildren<TMP_Text>(true).text = label;
        }

        private static void Shade(RectTransform root, float alpha)
        {
            var shade = root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, alpha); shade.raycastTarget = true;
        }

        private static Color Tone(EquipForgeTone tone) => tone switch
        {
            EquipForgeTone.Error => QdaoUguiTheme.Html("#9A442D"),
            EquipForgeTone.Success => EquipUiSkin.Green,
            EquipForgeTone.Failure => EquipUiSkin.Gold,
            EquipForgeTone.Busy => EquipUiSkin.Jade,
            _ => EquipUiSkin.Muted,
        };
    }
}
