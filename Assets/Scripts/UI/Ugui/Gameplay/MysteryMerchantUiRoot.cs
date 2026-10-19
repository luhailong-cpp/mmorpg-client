using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.MysteryMerchant;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Gameplay
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 神秘商人的宿主(服务端设计 docs/design/mystery-merchant.md §6)。
    /// 网络真相全在 <see cref="MysteryMerchantClient"/>;这里只做四件事:
    ///  - 跟着 GameClient 实例创建 / 释放数据层,并把入场通知、断线转发给它;
    ///  - HUD 角标(「神秘商人 3线 12:34」)与出现公告的飘字;
    ///  - 商人在本线时,在主城里摆出他的形象,并用一块跟随屏幕位置的透明热区接住对他的点击:
    ///    离得远就先走过去,走到了再开店;
    ///  - 商店窗口的宿主:窗口只发意图(购买 / 前往),这里转给数据层 / 切线功能。
    /// </summary>
    public sealed class MysteryMerchantUiRoot : MonoBehaviour
    {
        public static MysteryMerchantUiRoot Instance { get; private set; }
        public MysteryMerchantWindow Window => _window;
        public MysteryMerchantClient Client => _client;

        /// <summary>主画布排序:在背包任务活动(180)之上、线路面板(184)之下。</summary>
        public const int CanvasSortingOrder = 183;
        public const string EntryName = "MysteryMerchantEntry";
        public const string HotspotName = "MysteryMerchantHotspot";
        /// <summary>角标宽度:最长的文案是「神秘商人 59:59 后出现」。</summary>
        public const float EntryWidth = 520f;
        /// <summary>走向商人时希望停在离他多远的地方(不踩到他身上);购买距离配得很小时会自动收近,见 MysteryMerchantNpc.StandOff。</summary>
        public const float ApproachStandOff = 3f;
        /// <summary>点了商人之后最多等多久走到;超过就放弃自动开店(玩家可能被挡住或改了主意)。</summary>
        public const float ApproachTimeoutSeconds = 30f;
        public const string CannotReachText = "走不到神秘商人身边。";
        public const string TravelUnavailableText = "暂时无法前往那条线。";
        public const string OtherMapText = "神秘商人不在这张地图上，先回到他所在的地图。";
        public const string LineGoneText = "神秘商人所在的线暂时进不去。";

        private GameClient _game;
        private MysteryMerchantClient _client;
        private RectTransform _hud;
        private Button _entry;
        private TMP_Text _entryLabel;
        private Button _hotspot;
        private MysteryMerchantWindow _window;
        private MysteryMerchantNpc _npc;
        private bool _available;
        // 点了商人、正在走过去:走到开店距离之内就打开商店。到点 / 改道 / 商人不在了都会取消。
        private bool _approaching;
        private float _approachDeadline;
        private Vector3 _approachTarget;
        // 角标上一次显示的整秒数:倒计时每帧在走,只有整秒变了才重写文字。
        private string _entryTextShown;
        // 上一次购买成功那一刻的背包快照对象。数据层的余额只在那一刻准:背包快照换成了更新的一份之后,以快照为准。
        private global::BagInfo _bagAtPurchase;
        // 商店上一次渲染用的背包快照对象、「前往」的可用性与原因:变了就重画(背包与切线各有自己的数据层,不走本类的变化通知)。
        private global::BagInfo _bagShown;
        private bool _canTravelShown;
        private string _travelBlockedShown = "";
        // 商人在别的线、商店开着时,下一次刷新线路目录的时刻(「前往」能不能点要看那条线此刻的状态)。
        private float _nextDirectoryRefreshAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[MysteryMerchantUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<MysteryMerchantUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var canvasObject = new GameObject("MysteryMerchantCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            // 热区先建、角标后建:两者重叠时角标在上(同一画布内后建的在上层)。
            _hotspot = QdaoUguiFactory.CreateHitButton(HotspotName, _hud, 0, 0, 10, 10);
            _hotspot.onClick.AddListener(HandleNpcClicked);
            _hotspot.gameObject.SetActive(false);
            _entry = CreateEntry(_hud, Toggle);
            _entryLabel = _entry.GetComponentInChildren<TMP_Text>(true);
            _entry.gameObject.SetActive(false);
            _window = new MysteryMerchantWindow(design);
            _window.BuyRequested += quote => { if (_available) _client?.Buy(quote, 1); };
            _window.TravelRequested += TravelToMerchant;
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_window == null) return;   // 重复实例:Awake 里已安排销毁
            Bind(AppBootstrap.Instance?.GameClient);
            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = inGame && !battleLayer;
            if (_client != null)
            {
                _client.InBattle = battleLayer || InBattleOrSpectating(_game);
                _client.Tick();
            }
            _hud.gameObject.SetActive(_available);
            if (!_available)
            {
                // 进战斗、断线、换服途中都收起商店与世界里的形象。
                HidePanel();
                CancelApproach();
                RemoveNpc();
                return;
            }
            if (_client != null)
            {
                SyncNpc();
                RefreshEntry();
                if (_window.IsVisible)
                {
                    RefreshShopInputs();
                    _window.SetCountdown(_client.RemainingMs, _client.UntilNextMs);
                }
                TickApproach();
            }
            if (IsTyping() || !_window.IsVisible) return;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) HidePanel();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
#endif
        }

        public void HidePanel()
        {
            CancelApproach();
            _window?.Hide();
        }

        /// <summary>
        /// 角标的点击:商人在本线且离得远 → 先走过去(到了自动开店);其它情况直接开 / 关商店窗口
        /// (不在本线时窗口里是「前往 N线」,不在场时是下一次出现的倒计时)。
        /// </summary>
        public void Toggle()
        {
            if (_window.IsVisible) { HidePanel(); return; }
            if (!_available || _client == null) return;
            if (_client.Presence == MysteryMerchantPresence.Here && _npc != null && !WithinOpenDistance())
            {
                BeginApproach();
                return;
            }
            OpenShop();
        }

        // ── 纯函数(离线截图与测试直接用)──────────────────────────────────

        /// <summary>
        /// 把数据层此刻的状态整份拍成商店视图。<paramref name="currency"/> 是要显示的余额(由宿主在
        /// 「最近一次购买带回的」与「背包快照」之间挑较新的一份,见 <see cref="DisplayCurrency"/>);只用于显示。
        /// </summary>
        public static MysteryMerchantShopView BuildView(MysteryMerchantClient client, global::CurrencyComp currency,
            bool canTravel, string travelBlockedText = "")
        {
            if (client == null) return default;
            var info = client.Info;
            return new MysteryMerchantShopView
            {
                Presence = client.Presence,
                RoundId = info?.RoundId ?? 0,
                ChannelNo = info?.ChannelNo ?? 0,
                Goods = info?.Goods,
                Ready = client.Ready && !client.RequiresReconnect,
                InBattle = client.InBattle,
                Loading = client.InfoPending || client.InfoQueued,
                BuyingGoodsId = client.BuyingGoodsId,
                RemainingMs = client.RemainingMs,
                UntilNextMs = client.UntilNextMs,
                Balances = currency?.Values,
                CanTravel = canTravel,
                TravelBlockedText = travelBlockedText ?? "",
                Status = client.Status,
                StatusIsError = client.StatusIsError,
            };
        }

        /// <summary>建角标按钮:右上角线路角标的左边,同一行、同样的底图。</summary>
        public static Button CreateEntry(UnityEngine.Transform hud, System.Action click)
        {
            var entry = GameplayUiArt.Button(hud, MysteryMerchantModels.MerchantName,
                BattleUiStyle.HudEntryX - EntryWidth - 16f, BattleUiStyle.HudEntryY(-1), EntryWidth,
                BattleUiStyle.HudEntryHeight, click, true, fontSize: 30);
            entry.name = EntryName;
            return entry;
        }

        /// <summary>
        /// 要显示哪一份余额。数据层的余额(<paramref name="purchaseCurrency"/>)是最近一次购买成功那一刻的,
        /// 当时的背包快照是 <paramref name="bagAtPurchase"/>;此后背包快照换成了另一个对象(重新拉过),
        /// 说明它更新,以它为准。没有购买过就直接用背包快照。
        /// </summary>
        public static global::CurrencyComp DisplayCurrency(global::CurrencyComp purchaseCurrency,
            global::BagInfo bagAtPurchase, global::BagInfo bagNow)
        {
            if (purchaseCurrency != null && ReferenceEquals(bagNow, bagAtPurchase)) return purchaseCurrency;
            return bagNow?.Currency ?? purchaseCurrency;
        }

        /// <summary>
        /// 把屏幕上的点击范围(屏幕像素,原点左下:脚底中心 + 宽高)换成 HUD 根节点里的矩形
        /// (左上原点、y 向下,与 QdaoUguiFactory.CreateRect 同一坐标系)。
        /// <paramref name="hudLocalFeet"/> / <paramref name="hudLocalTop"/> 是脚底中心与头顶中心在 HUD 根节点
        /// 局部坐标系(原点在中心、y 向上)里的位置。
        /// </summary>
        public static Rect HotspotRect(Vector2 hudLocalFeet, Vector2 hudLocalTop, Vector2 hudSize, float widthPerHeight)
        {
            float height = Mathf.Abs(hudLocalTop.y - hudLocalFeet.y);
            float width = height * widthPerHeight;
            float top = Mathf.Max(hudLocalTop.y, hudLocalFeet.y);
            return new Rect(hudLocalFeet.x + hudSize.x * 0.5f - width * 0.5f, hudSize.y * 0.5f - top, width, height);
        }

        // ── 绑定 ────────────────────────────────────────────────────────────

        private void Bind(GameClient game)
        {
            if (ReferenceEquals(_game, game)) return;
            Unbind();
            _game = game;
            if (game != null)
            {
                _client = new MysteryMerchantClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                    () => game.CurrentSceneId, () => Time.realtimeSinceStartup);
                _client.Changed += Render;
                _client.Appeared += HandleAppeared;
                _client.Purchased += HandlePurchased;
                game.OnSceneEntered += HandleSceneEntered;
                game.OnDisconnected += HandleDisconnected;
                // 本组件晚于进场才出现(或 GameClient 中途换了实例)时,入场通知已经错过,自己问一次。
                if (game.InGame && game.IsGateReady && game.CurrentSceneId != 0) _client.RequestInfo();
            }
            Render();
        }

        private void Unbind()
        {
            if (_game != null)
            {
                _game.OnSceneEntered -= HandleSceneEntered;
                _game.OnDisconnected -= HandleDisconnected;
                _game = null;
            }
            if (_client != null)
            {
                _client.Changed -= Render;
                _client.Appeared -= HandleAppeared;
                _client.Purchased -= HandlePurchased;
                _client.Dispose();
                _client = null;
            }
            _bagAtPurchase = null;
            CancelApproach();
            RemoveNpc();
            HidePanel();
        }

        private void HandleSceneEntered(SceneInfoComp scene)
        {
            // 换了场景(含同图切线):上一条线上的形象撤掉,走向商人的意图作废;数据层会重新问。
            CancelApproach();
            RemoveNpc();
            _client?.HandleSceneEntered();
        }

        private void HandleDisconnected()
        {
            CancelApproach();
            RemoveNpc();
            _client?.HandleDisconnected();
            HidePanel();
        }

        // ── 数据层事件 ──────────────────────────────────────────────────────

        /// <summary>数据层的变化通知:角标与商店整份重读。</summary>
        private void Render()
        {
            if (_window == null) return;
            _entryTextShown = null;   // 强制角标下一帧重算
            var bag = PlayerFeaturesClient.Instance?.Bag;
            bool canTravel = EvaluateTravel(out string travelBlocked);
            _bagShown = bag;
            _canTravelShown = canTravel;
            _travelBlockedShown = travelBlocked;
            _window.SetView(BuildView(_client, DisplayCurrency(_client?.Currency, _bagAtPurchase, bag), canTravel, travelBlocked));
        }

        /// <summary>
        /// 商店开着时每帧调:余额来自背包的数据层、「前往」的可用性来自切线的数据层,两者变化都不会触发本类的 Render。
        /// 这里只比较「上次画的是不是还成立」,变了才重画;商人在别的线时顺带定期刷新线路目录
        /// (那条线满了 / 空出来了 / 被回收了,按钮要跟着变)。
        /// </summary>
        private void RefreshShopInputs()
        {
            if (_client.Presence == MysteryMerchantPresence.Elsewhere)
            {
                float now = Time.realtimeSinceStartup;
                var channel = SceneChannelUiRoot.Instance?.Client;
                if (channel != null && now >= _nextDirectoryRefreshAt)
                {
                    _nextDirectoryRefreshAt = now + SceneChannelClient.PanelRefreshIntervalSeconds;
                    if (!channel.SwitchPending) channel.RequestList();
                }
            }
            bool canTravel = EvaluateTravel(out string travelBlocked);
            if (!ReferenceEquals(PlayerFeaturesClient.Instance?.Bag, _bagShown) || canTravel != _canTravelShown
                || travelBlocked != _travelBlockedShown)
                Render();
        }

        private void HandleAppeared(uint channelNo, bool here, long remainingMs)
        {
            if (!_available) return;
            BattleUiRoot.Instance?.ShowToast(MysteryMerchantModels.AnnouncementText(channelNo, here, remainingMs));
        }

        private void HandlePurchased(global::MysteryMerchantGoodsInfo goods, uint count)
        {
            BattleUiRoot.Instance?.ShowToast(MysteryMerchantModels.PurchasedText(goods, count));
            // scene 没有余额 / 背包推送:把背包窗的快照重拉一次,别的窗口看到的余额才跟得上。
            // 重拉回来之前商店显示数据层刚带回的余额;回来之后(快照对象换了)改用快照,见 DisplayCurrency。
            var features = PlayerFeaturesClient.Instance;
            _bagAtPurchase = features?.Bag;
            features?.RequestBag(features.RequestedBagType);
            Render();
        }

        // ── 商店 ────────────────────────────────────────────────────────────

        private void OpenShop()
        {
            if (!_available || _client == null) return;
            HideOtherPanels();
            // 打开即问一次:货架与已购数以服务端此刻的为准(数据层自己管最小间隔)。
            _client.RequestInfo();
            // 余额只用于显示,但也该是新的:每次开店重拉一次人物背包(上次的快照可能是很久以前开背包窗时留下的)。
            var features = PlayerFeaturesClient.Instance;
            if (features != null && !features.BagLoading && !features.BusySort) features.RequestBag(0);
            _nextDirectoryRefreshAt = 0f;   // 商人在别的线的话,开店后的第一帧就刷新一次线路目录
            Render();
            _window.Show();
        }

        private static void HideOtherPanels()
        {
            Skills.SkillUiRoot.Instance?.HidePanel();
            Guild.GuildUiRoot.Instance?.HidePanel();
            MmorpgClient.UI.Ugui.Rank.RankUiRoot.Instance?.HidePanel();
            Team.TeamUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            SceneChannelUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
            Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();
            Mail.MailUiRoot.Instance?.HidePanel();
            Social.SocialUiRoot.Instance?.HidePanel();
        }

        /// <summary>
        /// 「前往 N线」能不能点:商人在别的线、与本玩家在同一张地图,且切线功能此刻放行这条线
        /// (目录里有它、没满、不在冷却 / 战斗 / 队伍跟随中)。判定用的是切线数据层自己的那条规则。
        /// 不能点时 <paramref name="blockedText"/> 给出原因(给玩家看的一句话);商人不在别的线时为 ""。
        /// </summary>
        private bool EvaluateTravel(out string blockedText)
        {
            blockedText = "";
            var info = _client?.Info;
            if (info == null || _game == null || _client.Presence != MysteryMerchantPresence.Elsewhere) return false;
            if (info.SceneConfigId != _game.CurrentSceneConfigId)
            {
                blockedText = OtherMapText;
                return false;
            }
            var channel = SceneChannelUiRoot.Instance?.Client;
            if (channel == null || !channel.HasDirectory)
            {
                blockedText = SceneChannelModels.DescribeBlock(SceneChannelBlockReason.NotReady);
                return false;
            }
            var line = SceneChannelModels.FindLine(channel.Lines, info.SceneId);
            if (line == null)
            {
                blockedText = LineGoneText;
                return false;
            }
            var reason = channel.BlockReasonFor(line);
            if (reason == SceneChannelBlockReason.None) return true;
            blockedText = SceneChannelModels.DescribeBlock(reason, channel.CooldownRemainingSeconds);
            return false;
        }

        private void TravelToMerchant()
        {
            var info = _client?.Info;
            var channel = SceneChannelUiRoot.Instance?.Client;
            if (!_available || info == null || channel == null) return;
            if (channel.RequestSwitch(info.SceneId))
            {
                // 切线已发出:抵达后数据层会收到入场通知并重新问货架。商店先收起,免得挡住切线过程。
                HidePanel();
                return;
            }
            string reason = string.IsNullOrEmpty(channel.Status) ? TravelUnavailableText : channel.Status;
            BattleUiRoot.Instance?.ShowToast(reason, true);
        }

        // ── 世界里的形象与点击 ──────────────────────────────────────────────

        /// <summary>商人在本线、且本玩家就在那张地图里时,摆出(或挪动)他的形象;否则撤掉。</summary>
        private void SyncNpc()
        {
            var info = _client.Info;
            var worldMap = AppBootstrap.Instance != null ? AppBootstrap.Instance.WorldMap : null;
            var mapObject = worldMap != null && worldMap.Map != null ? worldMap.Map.Root : null;
            var mapRoot = mapObject != null ? mapObject.transform : null;
            bool show = _client.Presence == MysteryMerchantPresence.Here && info?.Position != null && mapRoot != null
                        && worldMap.ActiveSceneConfigId == info.SceneConfigId;
            if (!show)
            {
                RemoveNpc();
                return;
            }
            var feet = WorldCoordinateConverter.ServerToUnity(info.Position.X, info.Position.Y, info.Position.Z);
            if (_npc == null || _npc.transform.parent != mapRoot)
            {
                RemoveNpc();
                _npc = MysteryMerchantNpc.Create(mapRoot, feet);
            }
            else if (_npc.FeetPosition != feet)
            {
                _npc.MoveTo(feet);
            }
            PlaceHotspot(worldMap.WorldCamera);
        }

        private void RemoveNpc()
        {
            if (_hotspot != null) _hotspot.gameObject.SetActive(false);
            if (_npc == null) return;
            Destroy(_npc.gameObject);
            _npc = null;
        }

        /// <summary>让透明热区每帧跟着商人在屏幕上的位置。商店开着时不需要(整屏遮罩已经挡住点击)。</summary>
        private void PlaceHotspot(Camera worldCamera)
        {
            if (_npc == null || _window.IsVisible
                || !_npc.TryGetScreenHitBox(worldCamera, out var feetScreen, out var size))
            {
                _hotspot.gameObject.SetActive(false);
                return;
            }
            var topScreen = new Vector2(feetScreen.x, feetScreen.y + size.y);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_hud, feetScreen, null, out var localFeet) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(_hud, topScreen, null, out var localTop))
            {
                _hotspot.gameObject.SetActive(false);
                return;
            }
            var rect = HotspotRect(localFeet, localTop, _hud.rect.size, MysteryMerchantNpc.HitWidthPerHeight);
            var hotspotRect = (RectTransform)_hotspot.transform;
            hotspotRect.anchoredPosition = new Vector2(rect.x, -rect.y);
            hotspotRect.sizeDelta = rect.size;
            _hotspot.gameObject.SetActive(true);
        }

        private void HandleNpcClicked()
        {
            if (!_available || _client == null || _npc == null) return;
            if (WithinOpenDistance()) OpenShop();
            else BeginApproach();
        }

        private TianyongPlayerController LocalController()
        {
            var world = _game?.World;
            if (world == null || !world.HasLocalPlayer || !world.TryGetActor(world.LocalEntity, out var view)) return null;
            return view?.Go != null ? view.Go.GetComponent<TianyongPlayerController>() : null;
        }

        private bool WithinOpenDistance()
        {
            var controller = LocalController();
            if (controller == null || _npc == null || _client?.Info == null) return false;
            return MysteryMerchantNpc.HorizontalDistance(controller.FeetPosition, _npc.FeetPosition)
                   <= MysteryMerchantNpc.OpenDistance(_client.Info.InteractRange);
        }

        private void BeginApproach()
        {
            var controller = LocalController();
            if (controller == null || _npc == null) return;
            // 已开的模态面板会暂停世界移动，先释放输入再寻路。
            HideOtherPanels();
            float standOff = MysteryMerchantNpc.StandOff(_client?.Info?.InteractRange ?? 0, ApproachStandOff);
            var target = MysteryMerchantNpc.ApproachPoint(_npc.FeetPosition, controller.FeetPosition, standOff);
            if (!controller.SetDestination(target))
            {
                BattleUiRoot.Instance?.ShowToast(CannotReachText, true);
                return;
            }
            _approaching = true;
            _approachDeadline = Time.realtimeSinceStartup + ApproachTimeoutSeconds;
            // 记下控制器实际采纳的终点:之后它变了,说明玩家自己点了别处,自动开店的意图随之作废。
            _approachTarget = controller.PathDestination ?? target;
        }

        private void CancelApproach() => _approaching = false;

        private void TickApproach()
        {
            if (!_approaching) return;
            var controller = LocalController();
            if (controller == null || _npc == null || _client.Presence != MysteryMerchantPresence.Here
                || Time.realtimeSinceStartup > _approachDeadline)
            {
                CancelApproach();
                return;
            }
            if (WithinOpenDistance())
            {
                CancelApproach();
                OpenShop();
                return;
            }
            // 还在路上:终点被换掉(玩家点了别处 / 用键盘接管了移动)就不再自动开店。
            var destination = controller.PathDestination;
            if (destination == null || MysteryMerchantNpc.HorizontalDistance(destination.Value, _approachTarget) > 0.5f)
                CancelApproach();
        }

        // ── 角标 ────────────────────────────────────────────────────────────

        private void RefreshEntry()
        {
            if (_entry == null || _client == null) return;
            string label = _client.IsSupported
                ? MysteryMerchantModels.EntryLabel(_client.Presence, _client.Info?.ChannelNo ?? 0,
                    _client.RemainingMs, _client.UntilNextMs)
                : MysteryMerchantModels.MerchantName + " 未开放";
            if (label == _entryTextShown) return;
            _entryTextShown = label;
            _entry.gameObject.SetActive(label != null);
            if (label != null) _entryLabel.text = label;
        }

        private static bool InBattleOrSpectating(GameClient game) =>
            (game.Battle != null && game.Battle.Phase != BattlePhase.None) ||
            (game.Spectate != null && game.Spectate.Phase != SpectatePhase.None);

        private static bool IsTyping()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected != null && (selected.GetComponentInParent<TMP_InputField>()?.isFocused == true ||
                                        selected.GetComponentInParent<InputField>()?.isFocused == true);
        }

        private void OnDisable()
        {
            HidePanel();
            CancelApproach();
            RemoveNpc();
        }

        private void OnDestroy()
        {
            Unbind();
            if (Instance == this) Instance = null;
        }
    }
}
