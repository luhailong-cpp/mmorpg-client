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
        // 以下两项只由服务端分页模式使用(ListingSummary.is_favorite / is_mine);本地模式忽略。
        public bool Favorite;
        public bool IsMine;

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
    ///
    /// 服务端分页模式(EnterServerPaging 进入,Reset / SetListings / LoadDemo 退出):
    /// 筛选与翻页只改查询状态并发 QueryChanged,由 JubaozhaiClient 发请求;GetPage 原样返回
    /// ApplyServerPage 注入的那一页,不在本地过滤、排序或改页码;收藏与详情只发事件,
    /// 服务端确认后经 ApplyFavorite / ApplyDetail 写回。每次查询变化 ++QueryVersion,
    /// 回包带着发送时的版本号注入,过期回包自动丢弃。本地模式的行为与此前完全一致。
    /// </summary>
    public sealed class JubaozhaiState
    {
        private readonly List<JubaozhaiListing> _listings = new();
        private readonly HashSet<string> _favorites = new(StringComparer.Ordinal);
        private readonly Func<long> _clock;
        // 服务端分页模式专用状态;本地模式下保持默认值且不参与任何计算。
        private readonly HashSet<string> _detailedIds = new(StringComparer.Ordinal);
        private int _serverTotalCount;
        private int _serverPageCount = 1;
        private long _clockOffsetSeconds;

        public event Action Changed;
        /// <summary>服务端模式下查询条件或页码变化(已 ++QueryVersion)。本地模式从不触发。</summary>
        public event Action QueryChanged;
        /// <summary>服务端模式下请求把 (listingId) 的收藏置为 (favorite);确认前本地状态不变。</summary>
        public event Action<string, bool> FavoriteRequested;
        /// <summary>服务端模式下请求补全 (listingId) 的详情描述。</summary>
        public event Action<string> DetailRequested;
        public bool ServerPaged { get; private set; }
        public int QueryVersion { get; private set; }
        public bool Loading { get; private set; }
        public string ServiceStatus { get; private set; } = "";
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
        public string Status => ServerPaged && ServiceStatus.Length > 0 ? ServiceStatus
            : IsDemo ? JubaozhaiCatalog.DemoNotice : ServiceAvailable ? "" : JubaozhaiCatalog.ServiceUnavailableMessage;

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
            LeaveServerPaging();
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
            var records = Materialize(listings, out var ids);
            // 服务端模式的收藏来自服务端页,不能混进本地整份快照。
            if (ServerPaged) { _favorites.Clear(); LeaveServerPaging(); }
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
            if (ServerPaged)
            {
                // 按上一次服务端回包的页数钳制;服务端仍会再钳一次。
                PageNumber = Math.Min(Math.Max(1, page), _serverPageCount);
                if (PageNumber != previous) QueryPending();
                return;
            }
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
            if (ServerPaged)
            {
                // 不做乐观更新:服务端确认(ApplyFavorite)之前收藏状态保持不变。
                FavoriteRequested?.Invoke(id, !_favorites.Contains(id));
                return _favorites.Contains(id);
            }
            if (!_favorites.Add(id)) _favorites.Remove(id);
            ClampPage(); Changed?.Invoke();
            return _favorites.Contains(id);
        }

        public string RemainingTime(JubaozhaiListing listing)
            => JubaozhaiCatalog.RemainingTime(listing?.ExpiresAtUnixSeconds ?? 0,
                ServerPaged ? _clock() + _clockOffsetSeconds : _clock());

        public JubaozhaiPage GetPage()
        {
            if (ServerPaged)
            {
                // 窗口的 Tick / SelectedListing 每次都会调用这里:只读注入页,不过滤、不改页码。
                return new JubaozhaiPage
                {
                    Items = _listings.Select(item => item.Copy()).ToArray(),
                    TotalCount = _serverTotalCount,
                    PageNumber = PageNumber,
                    PageCount = _serverPageCount
                };
            }
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

        /// <summary>
        /// 进入服务端分页模式并发出一次查询。首次进入清空本地条目与收藏(演示数据不得混入);
        /// 已在服务端模式时保留当前条件与页码,只刷新当前页。
        /// </summary>
        public void EnterServerPaging()
        {
            if (!ServerPaged)
            {
                _listings.Clear(); _favorites.Clear(); _detailedIds.Clear();
                IsDemo = false; ServiceAvailable = false; ServiceStatus = "";
                _serverTotalCount = 0; _serverPageCount = 1; _clockOffsetSeconds = 0;
                PageNumber = 1;
                ServerPaged = true;
            }
            QueryPending();
        }

        /// <summary>
        /// 注入服务端的一页。queryVersion 必须等于当前 QueryVersion(即发送后查询未再变化),
        /// 否则返回 false 且状态不变。条目校验与去重同 SetListings;收藏集合取条目上的 Favorite;
        /// serverNowUnixSeconds &gt; 0 时据此重算服务端时钟偏移,0 表示沿用上次偏移。
        /// </summary>
        public bool ApplyServerPage(int queryVersion, IEnumerable<JubaozhaiListing> items, int totalCount,
            int pageNumber, int pageCount, long serverNowUnixSeconds)
        {
            if (!ServerPaged || queryVersion != QueryVersion) return false;
            // 先物化再改状态:枚举失败时旧页保持完整。
            var records = Materialize(items, out _);
            _listings.Clear(); _listings.AddRange(records);
            _favorites.Clear();
            foreach (var record in records) if (record.Favorite) _favorites.Add(record.Id);
            _detailedIds.Clear();
            _serverTotalCount = Math.Max(0, totalCount);
            _serverPageCount = Math.Max(1, pageCount);
            PageNumber = Math.Min(Math.Max(1, pageNumber), _serverPageCount);
            if (serverNowUnixSeconds > 0) _clockOffsetSeconds = serverNowUnixSeconds - _clock();
            IsDemo = false; ServiceAvailable = true; ServiceStatus = ""; Loading = false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>服务端确认后的收藏状态。只写当前页里的条目:翻页后的回包自带权威标记。</summary>
        public void ApplyFavorite(string id, bool favorite)
        {
            if (!ServerPaged || string.IsNullOrWhiteSpace(id)) return;
            var listing = _listings.FirstOrDefault(item => item.Id == id);
            if (listing == null) return;
            listing.Favorite = favorite;
            if (favorite) _favorites.Add(id); else _favorites.Remove(id);
            Changed?.Invoke();
        }

        /// <summary>写入服务端返回的详情描述;描述未变化时不触发 Changed。</summary>
        public void ApplyDetail(string id, string details)
        {
            if (!ServerPaged || string.IsNullOrWhiteSpace(id)) return;
            var listing = _listings.FirstOrDefault(item => item.Id == id);
            if (listing == null) return;
            _detailedIds.Add(id);
            details ??= "";
            if (listing.Details == details) return;
            listing.Details = details;
            Changed?.Invoke();
        }

        /// <summary>
        /// 请求补全详情。同一页内每条只请求一次:详情写回会触发窗口重绘详情弹窗并再次调用本方法,
        /// 不去重会形成请求循环。重新注入页之后允许再次请求。
        /// </summary>
        public void RequestDetail(string id)
        {
            if (!ServerPaged || string.IsNullOrWhiteSpace(id) || _detailedIds.Contains(id) ||
                !_listings.Any(item => item.Id == id)) return;
            DetailRequested?.Invoke(id);
        }

        /// <summary>由 JubaozhaiClient 写入错误、隔离或读取中文案;服务端模式下 Status 优先返回它。本地模式忽略。</summary>
        public void SetServiceStatus(string message, bool available)
        {
            if (!ServerPaged) return;
            message ??= "";
            if (ServiceStatus == message && ServiceAvailable == available) return;
            ServiceStatus = message; ServiceAvailable = available;
            Changed?.Invoke();
        }

        private void FilterChanged()
        {
            PageNumber = 1;
            if (ServerPaged) { QueryPending(); return; }
            Changed?.Invoke();
        }

        private void QueryPending()
        {
            ++QueryVersion; Loading = true;
            Changed?.Invoke();
            QueryChanged?.Invoke();
        }

        private void LeaveServerPaging()
        {
            // 版本前移:离开服务端模式前发出的请求,回包一律按过期丢弃。
            if (ServerPaged) ++QueryVersion;
            ServerPaged = false; Loading = false; ServiceStatus = "";
            _serverTotalCount = 0; _serverPageCount = 1; _clockOffsetSeconds = 0;
            _detailedIds.Clear();
        }

        private static List<JubaozhaiListing> Materialize(IEnumerable<JubaozhaiListing> listings, out HashSet<string> ids)
        {
            var records = new List<JubaozhaiListing>();
            ids = new HashSet<string>(StringComparer.Ordinal);
            if (listings == null) return records;
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
            return records;
        }
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
