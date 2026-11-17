using System;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Pet
{
    public sealed partial class PetClient
    {
        public GetPetShopResponse Shop { get; private set; }
        public bool ShopRequestPending { get; private set; }
        public bool HasPendingPurchase => _pendingPurchase != null;
        public event Action OnShopChanged;
        public event Action<ulong> OnPurchased;

        private int _shopRequestVersion;
        private int _purchaseRequestVersion;
        private BuyPetRequest _pendingPurchase;
        private ulong _purchasePlayerId;

        public void RequestShop()
        {
            if (!IsReady) { OnError?.Invoke("尚未进入游戏，无法查看宠物商店"); return; }
            if (ShopRequestPending || Busy) return;
            if (_pendingPurchase != null && _purchasePlayerId != MyPlayerId) _pendingPurchase = null;
            int version = ++_shopRequestVersion;
            ShopRequestPending = true;
            _net.Call(MessageIds.GetPetShop, new GetPetShopRequest(), GetPetShopResponse.Parser,
                response =>
                {
                    if (version != _shopRequestVersion) return;
                    ShopRequestPending = false;
                    if (HasTip(response.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeTip("读取宠物商店失败", response.ErrorMessage));
                        OnShopChanged?.Invoke();
                        return;
                    }
                    Shop = response;
                    OnShopChanged?.Invoke();
                },
                error =>
                {
                    if (version != _shopRequestVersion) return;
                    ShopRequestPending = false;
                    OnError?.Invoke(error);
                    OnShopChanged?.Invoke();
                });
            OnShopChanged?.Invoke();
        }

        public PetShopOffer FindShopOffer(uint petTableId)
        {
            if (Shop == null) return null;
            foreach (var offer in Shop.Offers)
                if (offer.PetTableId == petTableId) return offer;
            return null;
        }

        public string PurchaseBlockedReason(uint petTableId)
        {
            if (!IsReady) return "尚未进入游戏";
            if (Busy) return "正在等待服务器确认";
            if (_pendingPurchase != null) return "上次购买结果待确认，请先重试上次购买";
            if (Shop == null || ShopRequestPending) return "请先刷新商店";
            var offer = FindShopOffer(petTableId);
            if (offer == null || offer.PriceGold == 0) return "请选择在售宝宝";
            if (!HasList) return "请先等待宝宝列表加载完成";
            if (Pets.Pets.Count >= Pets.MaxPets) return "可携带的宝宝数量已达上限";
            if (Shop.Gold < offer.PriceGold) return "银两不足";
            if (Shop.NextPurchaseSequence == 0) return "商店暂时无法购买，请刷新";
            return null;
        }

        public void BuyPet(uint petTableId, ulong displayedPrice)
        {
            string reason = PurchaseBlockedReason(petTableId);
            if (reason != null) { OnError?.Invoke(reason); return; }
            if (FindShopOffer(petTableId).PriceGold != displayedPrice)
            {
                OnError?.Invoke("价格已变化，请重新确认购买");
                return;
            }
            _pendingPurchase = new BuyPetRequest
            {
                PetTableId = petTableId,
                PurchaseSequence = Shop.NextPurchaseSequence,
                PriceGold = displayedPrice,
            };
            _purchasePlayerId = MyPlayerId;
            RetryPurchase();
        }

        // 超时后保留原序号、种类与报价，重试只能确认同一笔购买。
        public void RetryPurchase()
        {
            if (_pendingPurchase == null || _purchasePlayerId != MyPlayerId) return;
            if (!BeginWrite(null)) return;
            int version = ++_purchaseRequestVersion;
            ++_shopRequestVersion;
            ShopRequestPending = false;
            _net.Call(MessageIds.BuyPet, _pendingPurchase.Clone(), BuyPetResponse.Parser,
                response =>
                {
                    if (version != _purchaseRequestVersion) return;
                    _pendingPurchase = null;
                    if (HasTip(response.ErrorMessage))
                    {
                        FailWrite(DescribeTip("购买宝宝失败", response.ErrorMessage));
                        RequestShop();
                        return;
                    }
                    if (Shop != null)
                    {
                        Shop.Gold = response.Gold;
                        Shop.NextPurchaseSequence = response.NextPurchaseSequence;
                    }
                    CompleteWrite("购买宝宝失败", response.ErrorMessage, response.Pets);
                    if (response.Pets != null && response.PetId != 0) OnPurchased?.Invoke(response.PetId);
                    OnShopChanged?.Invoke();
                },
                error =>
                {
                    if (version != _purchaseRequestVersion) return;
                    FailWrite("购买结果尚未确认，请重试上次购买。" + error);
                    OnShopChanged?.Invoke();
                });
        }

        private void InvalidateShop()
        {
            ++_shopRequestVersion;
            ++_purchaseRequestVersion;
            ShopRequestPending = false;
            Shop = null;
            OnShopChanged?.Invoke();
        }
    }
}
