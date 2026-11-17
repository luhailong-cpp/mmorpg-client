using System;
using System.Collections.Generic;
using MmorpgClient.Game.Pet;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    /// <summary>普通玩家商店。商品、价格和购买序号均来自服务端。</summary>
    public sealed class PetShopPanel
    {
        private readonly RectTransform _root;
        private readonly RectTransform _content;
        private readonly Func<PetClient> _client;
        private readonly TMP_Text _balance, _details, _status;
        private readonly UiTextButton _buy, _refresh;
        private readonly Image _portrait;
        private readonly List<UiTextButton> _rows = new();
        private uint _selectedId;
        private ulong _confirmedPrice;
        private bool _confirming;

        public PetShopPanel(UnityEngine.Transform parent, Func<PetClient> client, Action close)
        {
            _client = client;
            _root = QdaoUguiFactory.CreateRect("PetShop", parent, 40f, 46f, 1920f, 840f);
            var plate = _root.gameObject.AddComponent<Image>();
            plate.color = PetUiStyle.WindowPaper;
            plate.raycastTarget = true;
            QdaoUguiFactory.CreateText("ShopTitle", _root, 36f, 20f, 900f, 52f,
                "宠物商店", 38f, PetUiStyle.FieldLabel);
            _refresh = Button("ShopRefresh", _root, 1500f, 20f, 180f, 52f, "刷新");
            _refresh.Button.onClick.AddListener(() => { _confirming = false; _client()?.RequestShop(); });
            Button("ShopClose", _root, 1730f, 20f, 150f, 52f, "关闭").Button.onClick.AddListener(() =>
            {
                Hide();
                close?.Invoke();
            });
            _balance = QdaoUguiFactory.CreateText("ShopBalance", _root, 36f, 92f, 1750f, 48f,
                string.Empty, 30f, PetUiStyle.FieldLabel);
            var viewport = QdaoUguiFactory.CreateRect("ShopViewport", _root, 36f, 160f, 900f, 570f);
            viewport.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _content = QdaoUguiFactory.CreateRect("ShopContent", viewport, 0f, 0f, 880f, 570f);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            _portrait = QdaoUguiFactory.CreateImage("ShopPortrait", _root, 1180f, 150f, 300f, 260f, null);
            _portrait.preserveAspect = true;
            _details = BattleUiWidgets.CreateWrappedText("ShopDetails", _root, 1010f, 435f, 830f, 185f,
                "请选择宝宝", 30f, PetUiStyle.FieldLabel);
            _buy = Button("ShopBuy", _root, 1070f, 650f, 720f, 70f, "购买");
            _buy.Button.onClick.AddListener(BuySelected);
            _status = QdaoUguiFactory.CreateText("ShopStatus", _root, 36f, 765f, 1810f, 55f,
                string.Empty, 26f, PetUiStyle.HintText);
            Hide();
        }

        private static UiTextButton Button(string name, UnityEngine.Transform parent, float x, float y, float w, float h, string label)
            => BattleUiWidgets.CreateTextButton(name, parent, x, y, w, h, label, 28f,
                PetUiStyle.ActionPlate, PetUiStyle.ActionText);

        public void Show()
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _confirming = false;
            Refresh();
            _client()?.RequestShop();
        }

        public void Hide() { _root.gameObject.SetActive(false); _confirming = false; }

        public void SetNotice(string message, bool warning)
        {
            _status.text = message ?? string.Empty;
            _status.color = warning ? PetUiStyle.WarnText : PetUiStyle.HintText;
        }

        public void Refresh()
        {
            var client = _client();
            var shop = client?.Shop;
            _balance.text = shop == null ? "银两　—" : $"银两　{shop.Gold:N0}";
            int count = shop?.Offers.Count ?? 0;
            while (_rows.Count < count)
            {
                int index = _rows.Count;
                var row = Button("ShopOffer" + index, _content, 0f, index * 90f, 870f, 80f, string.Empty);
                row.Button.onClick.AddListener(() => Select(index));
                _rows.Add(row);
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].SetVisible(i < count);
                if (i >= count) continue;
                var offer = shop.Offers[i];
                _rows[i].SetText($"{offer.Name}　{offer.PriceGold:N0} 银两　{offer.UnlockLevel}级");
                _rows[i].SetInteractable(client.IsReady && !client.Busy && !client.HasPendingPurchase);
            }
            _content.sizeDelta = new Vector2(880f, Mathf.Max(570f, count * 90f));
            _refresh.SetInteractable(client?.IsReady == true && !client.Busy && !client.ShopRequestPending);
            var selected = client?.FindShopOffer(_selectedId);
            if (selected == null && count > 0) { _selectedId = shop.Offers[0].PetTableId; selected = shop.Offers[0]; }
            _details.text = selected == null ? "当前没有在售宝宝" :
                $"{selected.Name}\n{selected.Desc}\n携带等级　{selected.UnlockLevel}级\n价格　{selected.PriceGold:N0} 银两";
            _portrait.sprite = selected == null ? null : QdaoPetCatalog.LoadPortrait(
                QdaoPetCatalog.Resolve(selected.PetTableId, selected.ModelId)?.id);
            _portrait.enabled = _portrait.sprite != null;
            if (client?.HasPendingPurchase == true)
            {
                _buy.SetText(client.Busy ? "正在确认购买…" : "重试上次购买");
                _buy.SetInteractable(client.IsReady && !client.Busy);
                return;
            }
            if (_confirming && selected?.PriceGold != _confirmedPrice) _confirming = false;
            _buy.SetText(_confirming ? $"确认购买　{_confirmedPrice:N0} 银两" : "购买");
            string reason = client == null ? "宠物模块尚未就绪" : client.PurchaseBlockedReason(_selectedId);
            _buy.SetInteractable(reason == null);
            if (client?.ShopRequestPending == true) SetNotice("正在读取商店…", false);
            else if (reason != null) SetNotice(reason, false);
            else SetNotice(_confirming ? "请确认宝宝和价格，再次点击购买。" : "购买后可回到宝宝列表选择出战。", false);
        }

        private void Select(int index)
        {
            var client = _client();
            if (client?.Shop == null || client.Busy || client.HasPendingPurchase || index >= client.Shop.Offers.Count) return;
            _selectedId = client.Shop.Offers[index].PetTableId;
            _confirming = false;
            Refresh();
        }

        private void BuySelected()
        {
            var client = _client();
            if (client == null || client.Busy) return;
            if (client.HasPendingPurchase) { client.RetryPurchase(); return; }
            var reason = client.PurchaseBlockedReason(_selectedId);
            if (reason != null) { SetNotice(reason, true); return; }
            if (!_confirming)
            {
                _confirmedPrice = client.FindShopOffer(_selectedId).PriceGold;
                _confirming = true;
                Refresh();
                return;
            }
            _confirming = false;
            client.BuyPet(_selectedId, _confirmedPrice);
        }
    }
}
