using System.Globalization;

namespace MmorpgClient.Game.Common
{
    /// <summary>
    /// 全客户端唯一货币显示名称与格式化工具（对齐服务端 {@code proto/common/base/currency_type.proto}）：
    /// 0 银两（可交易）、1 灵石（可交易）、2 绑定灵石（不可交易）、3 绑定银两（不可交易）。
    /// </summary>
    public static class CurrencyLabels
    {
        public const uint Gold = 0u;
        public const uint Diamond = 1u;
        public const uint BindDiamond = 2u;
        public const uint BindGold = 3u;

        public static string Name(uint currencyType, string unknownFallback = "货币") => currencyType switch
        {
            Gold => "银两",
            Diamond => "灵石",
            BindDiamond => "绑定灵石",
            BindGold => "绑定银两",
            _ => unknownFallback,
        };

        public static string Format(ulong amount, uint currencyType)
            => amount.ToString("#,0", CultureInfo.InvariantCulture) + " " + Name(currencyType);
    }
}
