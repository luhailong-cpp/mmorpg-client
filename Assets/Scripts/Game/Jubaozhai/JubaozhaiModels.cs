using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MmorpgClient.Game.Jubaozhai
{
    public enum JubaozhaiCategory { Character, Pet, Weapon, Armor, Set, Treasure, Jewelry, SummoningOrder }
    public enum JubaozhaiTab { Consignment, PublicNotice }
    public enum JubaozhaiSection { Consignment, Auction, Shelf }
    public enum JubaozhaiSort { Default, PriceAscending, PriceDescending, LevelDescending, RemainingTimeAscending }

    /// <summary>A display record supplied by a future trading-service adapter; it does not authorize a purchase.</summary>
    [Serializable]
    public sealed class JubaozhaiListing
    {
        public string Id;
        public string Name;
        public JubaozhaiCategory Category;
        public string Subcategory;
        public string School;
        public int Level;
        public decimal Price;
        public string Summary;
        public string Details;
        public string IconKey;
        public long ExpiresAtUnixSeconds;
        public JubaozhaiTab Tab;
        public JubaozhaiSection Section;

        internal JubaozhaiListing Copy() => (JubaozhaiListing)MemberwiseClone();
    }

    public sealed class JubaozhaiPage
    {
        public IReadOnlyList<JubaozhaiListing> Items { get; internal set; }
        public int TotalCount { get; internal set; }
        public int PageNumber { get; internal set; }
        public int PageCount { get; internal set; }
    }

    /// <summary>Final category and school copy from jubaozhai-ui/copy.zh-CN.json.</summary>
    public static class JubaozhaiCatalog
    {
        public const string Title = "聚宝斋";
        public const string SearchPlaceholder = "请输入名称或编号";
        public const string EmptyTitle = "当前类别暂无商品";
        public const string EmptyDescription = "再去别处逛逛吧";
        public const string DemoNotice = "离线演示 · 商品与数值均为界面样例";
        public const string ServiceUnavailableMessage = "聚宝斋服务尚未连接";
        public const string ContactUnavailableMessage = "联系卖家服务尚未接入";
        public const string PurchaseUnavailableMessage = "交易服务尚未接入，暂不可购买";

        private static readonly string[] CategoryNames = { "角色", "宠物", "武器", "防具", "套装", "法宝", "首饰", "召唤令" };
        private static readonly IReadOnlyList<string>[] Subcategories =
        {
            Array.AsReadOnly(new[] { "破军", "玄霄", "逐风", "丹心" }),
            Array.AsReadOnly(new[] { "普通", "灵兽", "变异", "神兽", "元灵", "其他" }),
            Array.AsReadOnly(new[] { "枪", "爪", "剑", "扇", "锤" }),
            Array.AsReadOnly(new[] { "男帽", "女帽", "男衣", "女衣", "鞋子" }),
            Array.AsReadOnly(Array.Empty<string>()),
            Array.AsReadOnly(Array.Empty<string>()),
            Array.AsReadOnly(Array.Empty<string>()),
            Array.AsReadOnly(new[] { "神兽召唤令", "元灵召唤令" })
        };

        public static IReadOnlyList<JubaozhaiCategory> AllCategories { get; } =
            Array.AsReadOnly((JubaozhaiCategory[])Enum.GetValues(typeof(JubaozhaiCategory)));

        public static string CategoryName(JubaozhaiCategory category)
            => IsCategory(category) ? CategoryNames[(int)category] : "";

        public static IReadOnlyList<string> SubcategoriesFor(JubaozhaiCategory category)
            => IsCategory(category) ? Subcategories[(int)category] : Array.Empty<string>();

        public static string FormatPrice(decimal price) => price.ToString("N2", CultureInfo.InvariantCulture) + " 元";

        public static string RemainingTime(long expiresAtUnixSeconds, long nowUnixSeconds)
        {
            if (expiresAtUnixSeconds <= 0) return "—";
            var seconds = Math.Max(0, expiresAtUnixSeconds - nowUnixSeconds);
            if (seconds == 0) return "已结束";
            var days = seconds / 86400;
            var hours = seconds % 86400 / 3600;
            var minutes = seconds % 3600 / 60;
            return days > 0 ? $"{days}天{hours}时" : hours > 0 ? $"{hours}时{minutes}分" : $"{Math.Max(1, minutes)}分";
        }

        internal static bool IsCategory(JubaozhaiCategory category)
            => (int)category >= 0 && (int)category < CategoryNames.Length;
    }

    /// <summary>
    /// Local browsing and session-local favorites. A new instance contains no products.
    /// Inject an authoritative complete snapshot with SetListings; LoadDemo is explicitly opt-in.
    /// No trading, payment, or seller-contact request is simulated by this model.
    /// </summary>
    public sealed class JubaozhaiState
    {
        private readonly List<JubaozhaiListing> _listings = new();
        private readonly HashSet<string> _favorites = new(StringComparer.Ordinal);
        private readonly Func<long> _clock;

        public event Action Changed;
        public bool IsDemo { get; private set; }
        public bool ServiceAvailable { get; private set; }
        public JubaozhaiCategory Category { get; private set; }
        public string Subcategory { get; private set; } = "";
        public JubaozhaiTab Tab { get; private set; }
        public JubaozhaiSection Section { get; private set; }
        public string Search { get; private set; } = "";
        public JubaozhaiSort Sort { get; private set; }
        public int PageNumber { get; private set; } = 1;
        public int PageSize { get; }
        public bool FavoritesOnly { get; private set; }
        public int FavoriteCount => _favorites.Count;
        public string Status => IsDemo ? JubaozhaiCatalog.DemoNotice : ServiceAvailable ? "" : JubaozhaiCatalog.ServiceUnavailableMessage;

        public JubaozhaiState(int pageSize = 4, Func<long> clock = null)
        {
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
            PageSize = pageSize;
            _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        public void Reset()
        {
            _listings.Clear(); _favorites.Clear();
            IsDemo = false; ServiceAvailable = false;
            Category = JubaozhaiCategory.Character;
            Subcategory = Search = "";
            Tab = JubaozhaiTab.Consignment;
            Section = JubaozhaiSection.Consignment;
            Sort = JubaozhaiSort.Default;
            FavoritesOnly = false; PageNumber = 1;
            Changed?.Invoke();
        }

        /// <summary>Replaces the complete catalog with a defensive copy. Call Reset when the account/session changes.</summary>
        public void SetListings(IEnumerable<JubaozhaiListing> listings, bool serviceAvailable = true)
            => ReplaceListings(listings, serviceAvailable, false);

        /// <summary>Only an explicit offline preview should call this; production roots must use SetListings.</summary>
        public void LoadDemo() => ReplaceListings(CreateDemoListings(_clock()), false, true);

        private void ReplaceListings(IEnumerable<JubaozhaiListing> listings, bool serviceAvailable, bool demo)
        {
            // Materialize before changing state: a failing adapter cannot leave half a snapshot installed.
            var records = new List<JubaozhaiListing>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (listings != null)
            {
                foreach (var value in listings)
                {
                    if (value == null || string.IsNullOrWhiteSpace(value.Id) || !JubaozhaiCatalog.IsCategory(value.Category)) continue;
                    var record = value.Copy();
                    record.Id = record.Id.Trim();
                    if (!ids.Add(record.Id)) continue;
                    record.Name ??= "";
                    record.Subcategory ??= "";
                    record.School ??= "";
                    record.Summary ??= "";
                    record.Details ??= "";
                    record.IconKey ??= "";
                    records.Add(record);
                }
            }
            if (IsDemo != demo) _favorites.Clear();
            _favorites.RemoveWhere(id => !ids.Contains(id));
            _listings.Clear(); _listings.AddRange(records);
            IsDemo = demo; ServiceAvailable = serviceAvailable && !demo;
            ClampPage();
            Changed?.Invoke();
        }

        public void SetCategory(JubaozhaiCategory category)
        {
            if (!JubaozhaiCatalog.IsCategory(category)) throw new ArgumentOutOfRangeException(nameof(category));
            if (Category == category) return;
            Category = category; Subcategory = ""; FilterChanged();
        }

        public void SetSubcategory(string subcategory)
        {
            subcategory = (subcategory ?? "").Trim();
            // Empty means the entire category. Unknown labels cannot accidentally match another category.
            if (subcategory.Length > 0 && !JubaozhaiCatalog.SubcategoriesFor(Category).Contains(subcategory))
                throw new ArgumentException("Unknown subcategory for the selected category.", nameof(subcategory));
            if (Subcategory == subcategory) return;
            Subcategory = subcategory; FilterChanged();
        }

        public void SetTab(JubaozhaiTab tab)
        {
            if (!Enum.IsDefined(typeof(JubaozhaiTab), tab)) throw new ArgumentOutOfRangeException(nameof(tab));
            if (Tab == tab) return;
            Tab = tab; FilterChanged();
        }

        public void SetSection(JubaozhaiSection section)
        {
            if (!Enum.IsDefined(typeof(JubaozhaiSection), section)) throw new ArgumentOutOfRangeException(nameof(section));
            if (Section == section) return;
            Section = section; FilterChanged();
        }

        public void SetSearch(string search)
        {
            search = (search ?? "").Trim();
            if (Search == search) return;
            Search = search; FilterChanged();
        }

        public void SetSort(JubaozhaiSort sort)
        {
            if (!Enum.IsDefined(typeof(JubaozhaiSort), sort)) throw new ArgumentOutOfRangeException(nameof(sort));
            if (Sort == sort) return;
            Sort = sort; FilterChanged();
        }

        public void SetPage(int page)
        {
            var previous = PageNumber;
            PageNumber = Math.Max(1, page); ClampPage();
            if (PageNumber != previous) Changed?.Invoke();
        }

        public void SetFavoritesOnly(bool value)
        {
            if (FavoritesOnly == value) return;
            FavoritesOnly = value; FilterChanged();
        }

        public bool IsFavorite(string id) => id != null && _favorites.Contains(id);

        /// <summary>Returns the new local favorite state. Unknown IDs are never added.</summary>
        public bool ToggleFavorite(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !_listings.Any(item => item.Id == id)) return false;
            if (!_favorites.Add(id)) _favorites.Remove(id);
            ClampPage(); Changed?.Invoke();
            return _favorites.Contains(id);
        }

        public string RemainingTime(JubaozhaiListing listing)
            => JubaozhaiCatalog.RemainingTime(listing?.ExpiresAtUnixSeconds ?? 0, _clock());

        public JubaozhaiPage GetPage()
        {
            var filtered = Filtered().ToList();
            int count = PageCountFor(filtered.Count);
            PageNumber = Math.Min(Math.Max(1, PageNumber), count);
            return new JubaozhaiPage
            {
                Items = filtered.Skip((PageNumber - 1) * PageSize).Take(PageSize).Select(item => item.Copy()).ToArray(),
                TotalCount = filtered.Count,
                PageNumber = PageNumber,
                PageCount = count
            };
        }

        private void FilterChanged() { PageNumber = 1; Changed?.Invoke(); }
        private int PageCountFor(int count) => count == 0 ? 1 : (count - 1) / PageSize + 1;
        private void ClampPage() => PageNumber = Math.Min(Math.Max(1, PageNumber), PageCountFor(Filtered().Count()));

        private IEnumerable<JubaozhaiListing> Filtered()
        {
            long now = _clock();
            var query = _listings.Where(item => item.Category == Category && item.Tab == Tab && item.Section == Section &&
                (item.ExpiresAtUnixSeconds <= 0 || item.ExpiresAtUnixSeconds > now));
            if (Subcategory.Length > 0)
                query = query.Where(item => item.Subcategory == Subcategory ||
                    (Category == JubaozhaiCategory.Character && item.School == Subcategory));
            if (Search.Length > 0)
                query = query.Where(item => item.Name.IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Id.IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0);
            if (FavoritesOnly) query = query.Where(item => _favorites.Contains(item.Id));
            return Sort switch
            {
                JubaozhaiSort.PriceAscending => query.OrderBy(item => item.Price).ThenBy(item => item.Id, StringComparer.Ordinal),
                JubaozhaiSort.PriceDescending => query.OrderByDescending(item => item.Price).ThenBy(item => item.Id, StringComparer.Ordinal),
                JubaozhaiSort.LevelDescending => query.OrderByDescending(item => item.Level).ThenBy(item => item.Id, StringComparer.Ordinal),
                JubaozhaiSort.RemainingTimeAscending => query.OrderBy(item => item.ExpiresAtUnixSeconds <= 0 ? long.MaxValue : item.ExpiresAtUnixSeconds)
                    .ThenBy(item => item.Id, StringComparer.Ordinal),
                _ => query
            };
        }

        private static IEnumerable<JubaozhaiListing> CreateDemoListings(long now)
        {
            string[][] names =
            {
                new[] { "青云小道", "松风剑客", "清铃", "玄墨道人" },
                new[] { "玄霆兽（点化）", "碧潭灵蛙（羽化）", "紫翎金鸢（点化）", "玄霆兽（点化）" },
                new[] { "紫霄雷枪", "赤曜龙枪", "紫霄雷枪", "流云破阵枪" },
                new[] { "赤曜龙冠", "乾坤道冠", "霜华月冠", "玄羽灵冠" },
                new[] { "87级 赤曜套装", "93级 游龙套装·枪", "99级 紫微套装", "82级 灵符套装" },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { "召唤令·上古神兽", "召唤令·上古神兽", "召唤令·上古神兽", "召唤令·上古神兽" }
            };
            int[] levels = { 87, 93, 99, 82 };
            string[] schools = { "破军", "玄霄", "逐风", "丹心" };
            string[] iconPrefixes = { "character", "pet", "weapon", "armor", "set", "treasure", "jewelry", "summoning" };
            for (int categoryIndex = 0; categoryIndex < names.Length; categoryIndex++)
            {
                var category = (JubaozhaiCategory)categoryIndex;
                for (int i = 0; i < names[categoryIndex].Length; i++)
                {
                    string subcategory = category switch
                    {
                        JubaozhaiCategory.Character => schools[i],
                        JubaozhaiCategory.Pet => "灵兽",
                        JubaozhaiCategory.Weapon => "枪",
                        JubaozhaiCategory.Armor => i == 2 ? "女帽" : "男帽",
                        JubaozhaiCategory.SummoningOrder => "神兽召唤令",
                        _ => ""
                    };
                    yield return new JubaozhaiListing
                    {
                        Id = $"DEMO-{categoryIndex + 1:00}-{i + 1:000}",
                        Name = names[categoryIndex][i], Category = category, Subcategory = subcategory,
                        School = category == JubaozhaiCategory.Character ? schools[i] : "",
                        Level = levels[i], Price = 1280m + categoryIndex * 320m + i * 680m,
                        IconKey = $"{iconPrefixes[categoryIndex]}_{i + 1:00}",
                        Summary = category == JubaozhaiCategory.Character ? $"{schools[i]} · {levels[i]}级" : $"{subcategory} · {levels[i]}级",
                        Details = "离线演示商品，仅用于检查聚宝斋界面、筛选和分页；编号、属性、价格与剩余时间均为样例。",
                        ExpiresAtUnixSeconds = now + (6 - i) * 86400 + (15 - i * 3) * 3600,
                        Tab = JubaozhaiTab.Consignment, Section = JubaozhaiSection.Consignment
                    };
                }
            }
        }
    }
}
