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
    /// <summary>装备详情的辅助操作。未开放的养成能力只说明状态，不发送虚构请求。</summary>
    public sealed class EquipActionsDialog
    {
        private readonly RectTransform _root;
        private readonly TMP_Text _name;
        private readonly EquipWishDialogInput _input;
        private readonly Button _openForge;
        private Action _attributes, _wish, _forge, _closed;
        private GameObject _returnFocus;
        public bool IsVisible => _root.gameObject.activeSelf;

        public EquipActionsDialog(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("EquipActionsDialog", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .76f); shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            _input = _root.gameObject.AddComponent<EquipWishDialogInput>();
            _input.CancelRequested = Close;
            var frame = QdaoUguiFactory.CreateCenteredRect("EquipActionsPanel", _root, 900, 730);
            Art(frame, "main_frame", 0, 0, 900, 730);
            Art(frame, "title_plate", 210, -20, 480, 114);
            Text(frame, "装备详情", 250, -5, 400, 84, 42, Cream, alignment: TextAlignmentOptions.Center);
            _name = Text(frame, "", 70, 118, 760, 55, 34, Ink, alignment: TextAlignmentOptions.Center);
            _name.enableAutoSizing = true; _name.fontSizeMin = 24; _name.fontSizeMax = 34;
            var attributes = Button(frame, "查看人物属性", 100, 202, 700, 76,
                () => { var action = _attributes; Close(); action?.Invoke(); }, true);
            attributes.name = "EquipViewAttributes";
            _openForge = Button(frame, "强化 / 镶嵌", 100, 298, 700, 76,
                () => { var action = _forge; Close(); action?.Invoke(); }, true);
            _openForge.name = "EquipOpenForge";
            var wish = Button(frame, "期望属性", 100, 394, 700, 76,
                () => { var action = _wish; Close(); action?.Invoke(); });
            wish.name = "EquipOpenWish";
            Text(frame, "洗练尚未开放\n期望属性将在炼化功能开放后可选。", 90, 490, 720, 82,
                27, Muted, true).name = "EquipDevelopmentStatus";
            var close = Button(frame, "返回装备", 270, 609, 360, 72, Close);
            close.name = "EquipCloseActions";
            _input.Controls = new List<Selectable> { attributes, _openForge, wish, close };
            for (int i = 0; i < _input.Controls.Count; i++)
            {
                var previous = _input.Controls[(i + _input.Controls.Count - 1) % _input.Controls.Count];
                var next = _input.Controls[(i + 1) % _input.Controls.Count];
                _input.Controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = previous, selectOnLeft = previous, selectOnDown = next, selectOnRight = next };
            }
        }

        public void Show(string itemName, Action attributes, Action wish, Action closed, Action forge = null)
        {
            _returnFocus = EventSystem.current?.currentSelectedGameObject;
            _name.text = itemName; _attributes = attributes; _wish = wish; _closed = closed;
            _forge = forge; _openForge.interactable = forge != null;
            _root.SetAsLastSibling(); _root.gameObject.SetActive(true); _input.FocusFirst();
        }

        public void Close()
        {
            if (!IsVisible) return;
            var callback = _closed;
            _root.gameObject.SetActive(false); _attributes = _wish = _forge = _closed = null;
            EventSystem.current?.SetSelectedGameObject(
                _returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null; callback?.Invoke();
        }
    }
}
