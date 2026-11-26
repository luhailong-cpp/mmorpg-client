using MmorpgClient.Game;
using MmorpgClient.Game.AfkPass;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.BattleScripts;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.BattleScripts;
using MmorpgClient.UI.Ugui.Skills;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 挂机月卡的宿主(服务端设计 docs/design/afk-monthly-card.md §7)。网络真相全在 <see cref="AfkPassClient"/>;这里只做三件事:
    ///  - 跟着 GameClient 实例创建 / 释放数据层,把入场通知、断线转发给它;快到期 / 已到期提醒用飘字显示;
    ///  - 城内入口按钮「挂机月卡」(经 <see cref="WorldHudNavigation"/> 归进「历练」分类);
    ///  - 面板:剩余时长、价格与叠加上限、持有灵石、开通 / 续费(两次点击确认,一次双击不算)。
    ///
    /// 视觉:这块界面没有效果图,只做功能版。按客户端 AGENTS.md「UI 原图还原验收」,视觉验收是未解决项,不宣称与原图一致。
    /// </summary>
    public sealed class AfkPassUiRoot : MonoBehaviour
    {
        public static AfkPassUiRoot Instance { get; private set; }
        public AfkPassClient Client => _client;
        public bool IsPanelVisible => _window != null && _window.gameObject.activeSelf;

        /// <summary>主画布排序:在背包任务活动(180)之上、神秘商人(183)之下。</summary>
        public const int CanvasSortingOrder = 182;
        // 测试与离线截图按名字找控件。
        public const string EntryName = "AfkPassEntry";
        public const string BuyButtonName = "AfkPassBuy";
        public const string CloseButtonName = "AfkPassClose";
        public const string AutoReturnToggleName = "AfkPassAutoReturnToggle";
        public const string OpenBattleScriptButtonName = "AfkPassOpenBattleScript";
        public const string RecapWindowName = "HostedRecapWindow";
        public const string RecapConfirmButtonName = "HostedRecapConfirm";
        public const string EntryText = "挂机月卡";
        /// <summary>第一次点击之后多久内的第二次点击算确认;超时要重新点。</summary>
        public const float ConfirmWindowSeconds = 5f;
        /// <summary>第一次点击之后至少过这么久,第二次点击才算确认:一次双击不该扣钱。</summary>
        public const float ConfirmMinIntervalSeconds = 0.4f;

        private const float FrameWidth = 1080, FrameHeight = 760;
        private const float RecapWidth = 920, RecapHeight = 620;
        private static readonly Color ErrorInk = QdaoUguiTheme.Html("#9A442D");
        private static readonly Color AccentJade = QdaoUguiTheme.Html("#1F6F52");

        private GameClient _game;
        private AfkPassClient _client;
        private BattleScriptClient _boundBattleScripts;
        private RectTransform _hud;
        private RectTransform _window;
        private RectTransform _recapWindow;
        private Button _entry, _buy, _autoReturnButton, _battleScriptButton, _recapConfirmButton;
        private TMP_Text _state, _price, _balance, _benefits, _status, _buyLabel, _autoReturnLabel, _battleScriptSummary;
        private TMP_Text _recapSubtitle, _recapBattles, _recapExp, _recapSilver, _recapItems;
        private bool _available;
        private ulong _shownRecapStartedAt;
        private float _armedAt = float.NegativeInfinity;
        private uint _armedCurrency;
        private ulong _armedPrice;
        // 按钮此刻显示的是「确认开通」:时限到了要收回来。
        private bool _confirmShown;
        // 状态行上一次显示的文字:倒计时每帧在走,只有文字变了才重写。
        private string _stateShown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[AfkPassUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<AfkPassUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);
            var canvasObject = new GameObject("AfkPassCanvas", typeof(RectTransform), typeof(Canvas),
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
            // 入口的位置由 WorldHudNavigation 接管(归进分类面板);这里给的坐标只是注册前的初值。
            _entry = GameplayUiArt.Button(_hud, EntryText, BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(8),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, TogglePanel, true, fontSize: 32);
            _entry.name = EntryName;
            WorldHudNavigation.Register(WorldHudEntry.AfkPass, (RectTransform)_entry.transform, _entry,
                _entry.GetComponentInChildren<TMP_Text>(true));
            BuildWindow(design);
            BuildRecapWindow(design);
            _hud.gameObject.SetActive(false);
        }

        private void BuildWindow(RectTransform design)
        {
            _window = QdaoUguiFactory.CreateStretch("AfkPassWindow", design, Vector4.zero);
            var shade = _window.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f);
            shade.raycastTarget = true;
            _window.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("AfkPassFrame", _window, FrameWidth, FrameHeight);
            GameplayUiArt.ApplyOptionalAfkTeamSprite(GameplayUiArt.Art(frame, "main_frame", 0, 0, FrameWidth, FrameHeight), "afk_pass_panel_bg");
            var infoCardSprite = GameplayUiArt.TryLoadAfkTeamArt("afk_pass_info_card");
            if (infoCardSprite != null)
            {
                var infoCard = QdaoUguiFactory.CreateImage("AfkPassInfoCard", frame, 80, 114, 920, 268, infoCardSprite);
                infoCard.type = infoCardSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                infoCard.raycastTarget = false;
            }
            GameplayUiArt.Text(frame, EntryText, 80, 38, 600, 60, 40);
            _state = GameplayUiArt.Text(frame, "", 80, 118, 920, 56, 36);
            _price = GameplayUiArt.Text(frame, "", 80, 182, 920, 44, 26, GameplayUiArt.Muted);
            _balance = GameplayUiArt.Text(frame, "", 80, 230, 920, 44, 26, GameplayUiArt.Muted);
            _benefits = GameplayUiArt.Text(frame,
                AfkPassModels.HostingDescription + "\n" + AfkPassModels.LightCardBenefitsSummary(null),
                80, 278, 920, 104, 21, GameplayUiArt.Muted, wrap: true);

            // 托管策略区:离队/解散自动回主城安全区 + 战斗剧本直连
            _autoReturnButton = GameplayUiArt.Button(frame, "离队/解散自动回主城：关闭", 80, 390, 460, 62,
                HandleAutoReturnToggle, false, fontSize: 24);
            _autoReturnButton.name = AutoReturnToggleName;
            GameplayUiArt.ApplyOptionalAfkTeamSprite(_autoReturnButton.image, "afk_pass_toggle_off");
            _autoReturnLabel = _autoReturnButton.GetComponentInChildren<TMP_Text>(true);

            _battleScriptButton = GameplayUiArt.Button(frame, "配置托管剧本", 564, 390, 260, 62,
                OpenBattleScriptPanel, false, fontSize: 24);
            _battleScriptButton.name = OpenBattleScriptButtonName;
            GameplayUiArt.ApplyOptionalAfkTeamSprite(_battleScriptButton.image, "afk_pass_btn_secondary");

            var scriptBarSprite = GameplayUiArt.TryLoadAfkTeamArt("afk_pass_script_bar");
            if (scriptBarSprite != null)
            {
                var scriptBar = QdaoUguiFactory.CreateImage("AfkPassScriptBar", frame, 80, 462, 920, 52, scriptBarSprite);
                scriptBar.type = scriptBarSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                scriptBar.raycastTarget = false;
            }
            _battleScriptSummary = GameplayUiArt.Text(frame, "组队副本托管剧本：默认出招", 80, 466, 920, 44, 24, AccentJade);

            _status = GameplayUiArt.Text(frame, "", 80, 526, 920, 48, 25, GameplayUiArt.Muted);
            var close = GameplayUiArt.Button(frame, "关闭", 80, 628, 240, 80, HidePanel);
            close.name = CloseButtonName;
            GameplayUiArt.ApplyOptionalAfkTeamSprite(close.image, "afk_pass_btn_secondary");
            _buy = GameplayUiArt.Button(frame, "开通", 600, 628, 400, 80, HandleBuyClicked, true);
            _buy.name = BuyButtonName;
            GameplayUiArt.ApplyOptionalAfkTeamSprite(_buy.image, "afk_pass_btn_primary");
            _buyLabel = _buy.GetComponentInChildren<TMP_Text>(true);
            _window.gameObject.SetActive(false);
        }

        private void BuildRecapWindow(RectTransform design)
        {
            _recapWindow = QdaoUguiFactory.CreateStretch(RecapWindowName, design, Vector4.zero);
            var shade = _recapWindow.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .72f);
            shade.raycastTarget = true;
            _recapWindow.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("HostedRecapFrame", _recapWindow, RecapWidth, RecapHeight);
            GameplayUiArt.ApplyOptionalAfkTeamSprite(GameplayUiArt.Art(frame, "main_frame", 0, 0, RecapWidth, RecapHeight), "hosted_recap_bg");
            GameplayUiArt.Text(frame, "离线托管收获简报", 70, 40, 780, 60, 40, alignment: TextAlignmentOptions.Center);
            _recapSubtitle = GameplayUiArt.Text(frame, "", 70, 112, 780, 56, 23, GameplayUiArt.Muted,
                alignment: TextAlignmentOptions.Center, wrap: true);
            _recapBattles = BuildRecapRow(frame, 198, "icon_recap_battle", null);
            _recapExp = BuildRecapRow(frame, 260, "icon_recap_exp", AccentJade);
            _recapSilver = BuildRecapRow(frame, 322, "icon_recap_silver", AccentJade);
            _recapItems = BuildRecapRow(frame, 384, "icon_recap_item", null);

            var scriptBtn = GameplayUiArt.Button(frame, "配置剧本", 70, 496, 210, 72, OpenBattleScriptFromRecap, false, fontSize: 25);
            scriptBtn.name = "HostedRecapBattleScript";
            GameplayUiArt.ApplyOptionalAfkTeamSprite(scriptBtn.image, "hosted_recap_btn_secondary");
            var passBtn = GameplayUiArt.Button(frame, "挂机月卡", 304, 496, 210, 72, OpenPassFromRecap, false, fontSize: 25);
            passBtn.name = "HostedRecapOpenPass";
            GameplayUiArt.ApplyOptionalAfkTeamSprite(passBtn.image, "hosted_recap_btn_secondary");
            _recapConfirmButton = GameplayUiArt.Button(frame, "收下确认", 548, 496, 302, 72, ConfirmRecap, true, fontSize: 28);
            _recapConfirmButton.name = RecapConfirmButtonName;
            GameplayUiArt.ApplyOptionalAfkTeamSprite(_recapConfirmButton.image, "hosted_recap_btn_confirm");
            _recapWindow.gameObject.SetActive(false);
        }

        private static TMP_Text BuildRecapRow(RectTransform frame, float y, string iconKey, Color? color)
        {
            var rowSprite = GameplayUiArt.TryLoadAfkTeamArt("hosted_recap_row_bg");
            if (rowSprite != null)
            {
                var bg = QdaoUguiFactory.CreateImage("RecapRowBg_" + iconKey, frame, 96, y, 728, 52, rowSprite);
                bg.type = rowSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                bg.raycastTarget = false;
            }
            var iconSprite = GameplayUiArt.TryLoadAfkTeamArt(iconKey);
            float textX = 96f;
            float textW = 728f;
            if (iconSprite != null)
            {
                var icon = QdaoUguiFactory.CreateImage("RecapIcon_" + iconKey, frame, 108, y + 4, 44, 44, iconSprite);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                textX = 164f;
                textW = 648f;
            }
            return GameplayUiArt.Text(frame, "", textX, y, textW, 52, 28, color);
        }

        private void Update()
        {
            if (_window == null || _hud == null) { Unbind(); return; }
            Bind(AppBootstrap.Instance?.GameClient);
            BindBattleScripts(BattleScriptUiRoot.Instance?.Client);
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = _game != null && _game.InGame && _game.IsGateReady && !battleLayer && (_client?.IsSupported ?? false);
            _client?.Tick();
            _hud.gameObject.SetActive(_available);
            if (!_available)
            {
                HidePanel();
                HideRecap();
                return;
            }
            MaybeShowPendingRecap();
            if (!IsPanelVisible) return;
            UpdateStateText();
            if (_confirmShown && Time.unscaledTime - _armedAt > ConfirmWindowSeconds) Render();   // 确认时限到了:收回按钮文字
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            Unbind();
            if (Instance == this) Instance = null;
        }

        // ── 面板 ────────────────────────────────────────────────────────────

        public void TogglePanel()
        {
            if (IsPanelVisible) HidePanel();
            else ShowPanel();
        }

        public void ShowPanel()
        {
            if (!_available || _window == null) return;
            CityPanelRegistry.HideOthers(this);
            HideRecap();
            _armedAt = float.NegativeInfinity;
            _window.gameObject.SetActive(true);
            _client?.RequestInfo();   // 打开时问一次:余额与剩余时长以服务端此刻为准
            var scripts = BattleScriptUiRoot.Instance?.Client;
            if (scripts != null && scripts.List == null && !scripts.Busy) scripts.Refresh();
            Render();
        }

        public void HidePanel()
        {
            _armedAt = float.NegativeInfinity;
            if (_window != null) _window.gameObject.SetActive(false);
        }

        private void HideRecap()
        {
            if (_recapWindow != null) _recapWindow.gameObject.SetActive(false);
        }

        private void MaybeShowPendingRecap()
        {
            if (_recapWindow == null || _recapWindow.gameObject.activeSelf || IsPanelVisible) return;
            var recap = _client?.PendingRecap;
            if (recap == null || !_client.HasPromptableRecap || recap.StartedAt == _shownRecapStartedAt) return;
            _shownRecapStartedAt = recap.StartedAt;
            RenderRecap(recap);
            _recapWindow.gameObject.SetActive(true);
        }

        private void RenderRecap(global::HostedRecapSummary recap)
        {
            if (recap == null || _recapWindow == null) return;
            ulong duration = AfkPassModels.RecapDurationSeconds(recap);
            _recapSubtitle.text = $"离线托管时长：{AfkPassModels.FormatDuration(duration)}（仅汇总实际结算收益，无额外产出）";
            _recapBattles.text = $"完成战斗：{recap.BattleCount} 场（胜利 {recap.WinCount} 场）";
            _recapExp.text = $"获得经验：+{recap.ExpGained}";
            _recapSilver.text = $"获得银两：+{recap.SilverGained}";
            _recapItems.text = $"获得物品：{recap.ItemCount} 件";
        }

        private void ConfirmRecap()
        {
            ulong startedAt = _client?.PendingRecap?.StartedAt ?? _shownRecapStartedAt;
            HideRecap();
            if (startedAt != 0) _client?.AckHostedRecap(startedAt);
        }

        private void OpenBattleScriptFromRecap()
        {
            ConfirmRecap();
            BattleScriptUiRoot.Instance?.Toggle();
        }

        private void OpenPassFromRecap()
        {
            ConfirmRecap();
            ShowPanel();
        }

        private void HandleAutoReturnToggle()
        {
            if (_client == null || _client.SettingsPending) return;
            _client.SetAutoReturnCity(!_client.AutoReturnCityOnTeamEnd);
        }

        private void OpenBattleScriptPanel()
        {
            HidePanel();
            BattleScriptUiRoot.Instance?.Toggle();
        }

        private string DescribeHostedBattleScript()
        {
            var scripts = BattleScriptUiRoot.Instance?.Client;
            if (scripts == null) return "组队副本托管剧本：未连接";
            var list = scripts.List;
            if (list == null) return scripts.Busy ? "组队副本托管剧本：读取中…" : "组队副本托管剧本：点击右侧查看与配置";
            var entry = scripts.ChoiceFor((uint)Match.MatchMode.PveTeam, 0)
                ?? scripts.ChoiceFor((uint)Match.MatchMode.PveSolo, 0);
            if (entry == null) return "组队副本托管剧本：默认出招（未选用自定义剧本）";
            if (entry.OfficialScriptId != 0)
            {
                foreach (var official in list.OfficialScripts)
                    if (official.ScriptId == entry.OfficialScriptId)
                        return $"组队副本托管剧本：官方 · {BattleScriptClient.DisplayName(official)}";
                return $"组队副本托管剧本：官方剧本 #{entry.OfficialScriptId}";
            }
            if (entry.PlayerScriptId != 0)
            {
                foreach (var own in list.MyScripts)
                    if (own.ScriptId == entry.PlayerScriptId)
                        return $"组队副本托管剧本：自订 · {BattleScriptClient.DisplayName(own.Content)}";
                return $"组队副本托管剧本：自订剧本 #{entry.PlayerScriptId}";
            }
            return "组队副本托管剧本：默认出招";
        }

        private void HandleBuyClicked()
        {
            if (_client == null) return;
            var info = _client.Info;
            if (info == null || !_client.ForSale) return;
            float now = Time.unscaledTime;
            if (now - _armedAt > ConfirmWindowSeconds || info.PriceCurrencyType != _armedCurrency || info.Price != _armedPrice)
            {
                // 第一次点击:只亮出「确认开通(价格)」,不扣钱;记下报价,期间热更涨价则撤回确认。
                _armedAt = now;
                _armedCurrency = info.PriceCurrencyType;
                _armedPrice = info.Price;
                Render();
                return;
            }
            if (now - _armedAt < ConfirmMinIntervalSeconds) return;   // 双击的第二下不算确认
            _armedAt = float.NegativeInfinity;
            _client.Buy();
            Render();
        }

        private void Render()
        {
            if (_window == null) return;
            var info = _client?.Info;
            _stateShown = null;
            UpdateStateText();
            if (info == null)
            {
                _price.text = "";
                _balance.text = "";
            }
            else if (_client.ForSale)
            {
                string currency = AfkPassModels.CurrencyName(info.PriceCurrencyType);
                _price.text = $"{info.Price} {currency} / {info.PeriodDays} 天，可叠加，剩余最多 {info.MaxRemainingDays} 天";
                _balance.text = $"持有{currency}：{info.Balance}";
            }
            else
            {
                _price.text = AfkPassModels.NotForSaleText;
                _balance.text = "";
            }
            if (_benefits != null)
            {
                _benefits.text = AfkPassModels.HostingDescription + "\n" + AfkPassModels.LightCardBenefitsSummary(info);
            }
            string status = _client?.Status ?? "";
            _status.text = status;
            _status.color = (_client?.StatusIsError ?? false) ? ErrorInk : GameplayUiArt.Muted;

            bool pending = _client != null && _client.BuyPending;
            bool canBuy = _client != null && _client.Ready && _client.ForSale && !pending && !_client.RequiresReconnect;
            if (info == null || info.PriceCurrencyType != _armedCurrency || info.Price != _armedPrice)
                _armedAt = float.NegativeInfinity;
            _buy.interactable = canBuy;
            _confirmShown = canBuy && info != null && Time.unscaledTime - _armedAt <= ConfirmWindowSeconds;
            string action = _client != null && _client.Active ? "续费" : "开通";
            _buyLabel.text = pending ? $"{action}中…"
                : _confirmShown ? $"确认{action}（{info.Price} {AfkPassModels.CurrencyName(info.PriceCurrencyType)}）"
                : action;

            bool settingsReady = _client != null && _client.Ready && _client.SupportsSettings && !_client.SettingsPending && info != null;
            bool autoReturn = _client != null && _client.AutoReturnCityOnTeamEnd;
            if (_autoReturnButton != null)
            {
                _autoReturnButton.interactable = settingsReady;
                var toggleSprite = GameplayUiArt.TryLoadAfkTeamArt(autoReturn ? "afk_pass_toggle_on" : "afk_pass_toggle_off");
                if (toggleSprite != null && _autoReturnButton.image != null)
                {
                    _autoReturnButton.image.sprite = toggleSprite;
                    _autoReturnButton.image.type = toggleSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                }
            }
            if (_autoReturnLabel != null)
            {
                _autoReturnLabel.text = _client?.SettingsPending == true
                    ? "保存托管设置中…"
                    : autoReturn ? "离队/解散自动回主城：已开启" : "离队/解散自动回主城：已关闭";
            }
            if (_battleScriptButton != null) _battleScriptButton.interactable = _available && BattleScriptUiRoot.Instance != null;
            if (_battleScriptSummary != null) _battleScriptSummary.text = DescribeHostedBattleScript();
        }

        // 只写状态行的文字(倒计时),不碰别的控件。
        private void UpdateStateText()
        {
            if (_state == null) return;
            string text = _client?.Info == null
                ? (_client?.InfoPending ?? false ? "正在查询挂机月卡…" : "")
                : _client.RemainingText;
            if (text == _stateShown) return;
            _stateShown = text;
            _state.text = text;
        }

        // ── 绑定 ────────────────────────────────────────────────────────────

        private void Bind(GameClient game)
        {
            if (ReferenceEquals(_game, game)) return;
            Unbind();
            _game = game;
            if (game != null)
            {
                _client = new AfkPassClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                    () => Time.realtimeSinceStartup);
                _client.Changed += Render;
                _client.Reminder += HandleReminder;
                _client.Purchased += HandlePurchased;
                game.OnSceneEntered += HandleSceneEntered;
                game.OnDisconnected += HandleDisconnected;
                // 本组件晚于进场才出现(或 GameClient 中途换了实例)时,入场通知已经错过,自己问一次。
                if (game.InGame && game.IsGateReady && game.CurrentSceneId != 0) _client.RequestInfo();
            }
            Render();
        }

        private void BindBattleScripts(BattleScriptClient client)
        {
            if (ReferenceEquals(_boundBattleScripts, client)) return;
            if (_boundBattleScripts != null) _boundBattleScripts.Changed -= HandleBattleScriptsChanged;
            _boundBattleScripts = client;
            if (_boundBattleScripts != null) _boundBattleScripts.Changed += HandleBattleScriptsChanged;
        }

        private void HandleBattleScriptsChanged()
        {
            if (IsPanelVisible && _battleScriptSummary != null)
                _battleScriptSummary.text = DescribeHostedBattleScript();
        }

        private void Unbind()
        {
            BindBattleScripts(null);
            if (_game != null)
            {
                _game.OnSceneEntered -= HandleSceneEntered;
                _game.OnDisconnected -= HandleDisconnected;
                _game = null;
            }
            if (_client != null)
            {
                _client.Changed -= Render;
                _client.Reminder -= HandleReminder;
                _client.Purchased -= HandlePurchased;
                _client.Dispose();
                _client = null;
            }
            _shownRecapStartedAt = 0;
            HidePanel();
            HideRecap();
        }

        private void HandleSceneEntered(SceneInfoComp scene) => _client?.HandleSceneEntered();

        private void HandleDisconnected()
        {
            _shownRecapStartedAt = 0;
            _client?.HandleDisconnected();
            HidePanel();
            HideRecap();
        }

        private void HandlePurchased()
        {
            _game?.Attributes?.RequestPanel();
            _game?.Skills?.Refresh(SkillBookContext.Character());
            _game?.Features?.RequestEquipmentMaterialReward();
        }

        private static void HandleReminder(string text) => BattleUiRoot.Instance?.ShowToast(text);
    }
}
