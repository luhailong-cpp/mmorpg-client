using MmorpgClient.Net;

namespace MmorpgClient.Game.PlayerFeatures
{
    public sealed partial class PlayerFeaturesClient
    {
        /// <summary>
        /// 将人物背包中的指定实例穿入目标装备槽。槽号来自服务器定义，0 也可以是有效槽号；
        /// 未指定槽位的旧调用继续使用 EquipItem(itemId)。类型、占用及佩戴资格由服务器最终裁决，
        /// 不按物品名称或模板合并实例，也不根据可能过期的本地快照自动改换目标槽。
        /// </summary>
        public void EquipItem(ulong itemId, uint targetSlot) => PerformEquipAction("装备失败", false,
            () => itemId == 0 ? "请先选择要装备的物品" : null,
            (complete, fail) => _net.Call(MessageIds.EquipItem,
                new EquipItemRequest { ItemId = itemId, TargetSlot = targetSlot },
                EquipItemResponse.Parser,
                response => complete(response.ErrorMessage, response.Bag, response.Equipment), fail));
    }
}
