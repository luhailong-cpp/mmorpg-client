using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game.PlayerFeatures;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 临时格(bag_type = 3)的显示规则,纯函数、不碰 UnityEngine(服务端 docs/design/bag-rule-policy-layering.md §6.6 (5))。
    /// 占用 = Layout.Slots 条数 / Layout.Capacity。数值只来自服务器快照;能不能取回由服务器裁决。
    /// </summary>
    public static class TemporaryBagModel
    {
        /// <summary>占用达到容量的这个百分比就提醒(§6.6 (5) 的建议值)。</summary>
        public const uint NearlyFullPercent = 90;

        /// <summary>平时的说明。</summary>
        public const string Explanation = "人物背包放不下的战斗掉落会暂存在这里，取回后放入人物背包。";

        /// <summary>快满了(≥ 90%,未满)。</summary>
        public const string NearlyFullExplanation = "临时格快满了，请及时把物品取回人物背包。";

        /// <summary>
        /// 已满。与服务端 2026-11-12 起的结算规则一致(docs/design/battle-overflow-mail.md):战斗掉落先进人物背包,
        /// 放不下进临时格,两个都放不下改投邮件;邮件也接不住时(待寄队列满等)那一局的结算整笔延后,
        /// 玩家从临时格取回、腾出位置之后自动补发。
        /// </summary>
        public const string FullExplanation = "临时格已满。新的战斗掉落若人物背包也放不下，将改由邮件发放；"
            + "邮件暂时无法发出时，那一局的结算会延后，取回物品腾出空间后自动补发。";

        /// <summary>临时格由不满变满时的飘字(每次只提示一次)。</summary>
        public const string FullToast = "临时格已满，请在背包「工具 → 临时格」中取回物品";

        /// <summary>已用格数;没有快照时为 0。</summary>
        public static int Used(BagInfo temporary) => temporary?.Layout?.Slots.Count ?? 0;

        /// <summary>容量;没有快照时为 0。</summary>
        public static uint Capacity(BagInfo temporary) => temporary?.Layout?.Capacity ?? 0;

        public static bool IsFull(BagInfo temporary)
        {
            uint capacity = Capacity(temporary);
            return capacity > 0 && (uint)Used(temporary) >= capacity;
        }

        public static bool IsNearlyFull(BagInfo temporary)
        {
            uint capacity = Capacity(temporary);
            return capacity > 0 && (ulong)Used(temporary) * 100 >= (ulong)capacity * NearlyFullPercent;
        }

        /// <summary>「临时格 12/20」;还没拉到快照时「临时格 —」。</summary>
        public static string OccupancyText(BagInfo temporary)
            => temporary?.Layout == null ? "临时格 —" : "临时格 " + Count(temporary);

        /// <summary>背包页状态行上的一句提醒;不到 90% 或还没拉到快照时为 null(不显示)。</summary>
        public static string BagPageNotice(BagInfo temporary)
        {
            if (IsFull(temporary)) return $"临时格已满（{Count(temporary)}），请在「工具」中取回";
            if (IsNearlyFull(temporary)) return $"临时格快满了（{Count(temporary)}），请及时取回";
            return null;
        }

        /// <summary>面板里占用下面的说明句。</summary>
        public static string Explain(BagInfo temporary)
            => IsFull(temporary) ? FullExplanation : IsNearlyFull(temporary) ? NearlyFullExplanation : Explanation;

        /// <summary>
        /// 临时格里的物品,按格位升序(位置只认 slot,BagInfo.items 的顺序无语义)。同一件物品只列一次;
        /// 快照里有物品却没有格位的排在最后,也照样列出 —— 东西在临时格里,玩家就得能取回。
        /// </summary>
        public static IReadOnlyList<BagItemInfo> Items(BagInfo temporary)
        {
            var result = new List<BagItemInfo>();
            if (temporary == null) return result;
            var byId = new Dictionary<ulong, BagItemInfo>();
            foreach (var item in temporary.Items)
                if (item != null && item.ItemId != 0 && !byId.ContainsKey(item.ItemId)) byId.Add(item.ItemId, item);
            var listed = new HashSet<ulong>();
            if (temporary.Layout != null)
            {
                var slots = new List<BagSlotInfo>(temporary.Layout.Slots);
                slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                foreach (var slot in slots)
                    if (byId.TryGetValue(slot.ItemId, out var item) && listed.Add(slot.ItemId)) result.Add(item);
            }
            foreach (var item in temporary.Items)
                if (item != null && byId.ContainsKey(item.ItemId) && listed.Add(item.ItemId)) result.Add(item);
            return result;
        }

        /// <summary>列表一行的文字:装备「青铜剑 +3」,可叠加物品「强化石 ×5」(1 件不写数量);缺名字的用「物品 · 配置号」。</summary>
        public static string RowText(BagItemInfo item)
        {
            if (item == null) return string.Empty;
            string name = GameplayUiArt.ItemName(item);
            if (item.EquipKind != 0) return EquipDisplay.NameWithEnhance(name, item.EnhanceLevel);
            return item.Count > 1 ? name + " ×" + item.Count.ToString(CultureInfo.InvariantCulture) : name;
        }

        private static string Count(BagInfo temporary)
            => Used(temporary).ToString(CultureInfo.InvariantCulture) + "/" + Capacity(temporary).ToString(CultureInfo.InvariantCulture);
    }
}
