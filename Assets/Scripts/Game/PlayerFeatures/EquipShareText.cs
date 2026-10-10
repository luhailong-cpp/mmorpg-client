using System.Globalization;
using System.Text;
using MmorpgClient.Game.Social;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>把服务器装备快照转换为聊天纯文本草稿，不创建可点击链接，也不发送消息。</summary>
    public static class EquipShareText
    {
        public static string Build(BagItemInfo item)
        {
            if (item == null || item.ItemId == 0 || item.EquipKind == 0 || item.Count == 0)
                return string.Empty;

            string name = string.IsNullOrWhiteSpace(item.Name)
                ? "装备 #" + item.ConfigId.ToString(CultureInfo.InvariantCulture) : item.Name.Trim();
            var text = new StringBuilder("【").Append(name).Append("】 ")
                .Append(item.EquipLevel.ToString(CultureInfo.InvariantCulture)).Append("级");
            if (item.EquipClass == 0) text.Append(" · 职业不限");
            else text.Append(" · ").Append(string.IsNullOrWhiteSpace(item.EquipClassName)
                ? "职业 #" + item.EquipClass.ToString(CultureInfo.InvariantCulture) : item.EquipClassName.Trim());

            foreach (var line in EquipDisplay.LinesOf(item))
                text.Append("；").Append(EquipDisplay.FormatLine(line));
            return Limit(text.ToString());
        }

        /// <summary>已有草稿一字不动，包括空白。失败时仍返回原草稿，调用方负责提示玩家。</summary>
        public static bool TryCreateDraft(BagItemInfo item, string existingDraft, out string draft)
        {
            draft = existingDraft ?? string.Empty;
            if (draft.Length != 0) return false;
            string equipment = Build(item);
            if (equipment.Length == 0) return false;
            draft = equipment;
            return true;
        }

        private static string Limit(string text)
        {
            if (SocialState.ValidMessage(text)) return text;

            // 按文本元素截断，保留完整代理对与组合字符；计数仍服从聊天的 Unicode 字符口径。
            const string omission = "…";
            var result = new StringBuilder();
            int count = 0, bytes = 0;
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                int length = SocialState.TextLength(element), byteCount = Encoding.UTF8.GetByteCount(element);
                if (count + length > SocialState.MessageLimit - 1 || bytes + byteCount > 512 - 3) break;
                result.Append(element);
                count += length;
                bytes += byteCount;
            }
            return result.Append(omission).ToString();
        }
    }
}
