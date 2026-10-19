using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Skills
{
    /// <summary>技能表现层入口。业务自行绑定来源；未绑定时仅提供只读职业图鉴。</summary>
    public sealed class SkillUiRoot : MonoBehaviour
    {
        public static SkillUiRoot Instance { get; private set; }
        public ISkillBookSource Source { get; private set; }
        public SkillBookContext Context { get; private set; } = SkillBookContext.Character();
        public bool IsVisible => _panel?.IsVisible == true;
        private SkillPanel _panel;
        private uint _previewClass = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn() => EnsureSpawned();
        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var root = new GameObject("[SkillUi]");
            DontDestroyOnLoad(root);
            root.AddComponent<SkillUiRoot>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }
        public static void Open()
        {
            EnsureSpawned();
            Instance.ShowContext(SkillBookContext.Character());
        }
        public static void OpenPet(ulong petId, string name)
        {
            EnsureSpawned();
            Instance.ShowContext(SkillBookContext.Pet(petId, name));
        }
        /// <summary>战斗列表仅取展示元数据；不据此判断技能归属或施放合法性。</summary>
        public static SkillDefinition FindPresentation(uint id) =>
            Instance?.Source?.State?.Snapshot?.Find(id)?.Definition ?? SkillCatalog.Find(id);
        private void ShowContext(SkillBookContext context)
        {
            Gameplay.MysteryMerchantUiRoot.Instance?.HidePanel();
            Attribute.AttributeUiRoot.Instance?.HidePanel();
            Pet.PetUiRoot.Instance?.HidePanel();
            Gameplay.GameplayUiRoot.Instance?.HidePanel();
            Gameplay.CityTravelUiRoot.Instance?.HidePanel();
            Gameplay.SceneChannelUiRoot.Instance?.HidePanel();
            Guild.GuildUiRoot.Instance?.HidePanel();
            MmorpgClient.UI.Ugui.Rank.RankUiRoot.Instance?.HidePanel();
            Team.TeamUiRoot.Instance?.HidePanel();
            Social.SocialUiRoot.Instance?.HidePanel();
            Mail.MailUiRoot.Instance?.HidePanel();
            Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();
            Context = context;
            EnsurePanel();
            ApplySource();
            _panel.Show();
            RequestRefresh();
        }
        public void HidePanel() => _panel?.Hide();
        public void Bind(ISkillBookSource source)
        {
            if (ReferenceEquals(Source, source)) return;
            if (Source != null) Source.Changed -= ApplySource;
            Source = source;
            if (Source != null) Source.Changed += ApplySource;
            ApplySource();
            if (IsVisible) RequestRefresh();
        }
        public void Unbind(ISkillBookSource source)
        {
            if (!ReferenceEquals(Source, source)) return;
            Bind(null);
            HidePanel();
        }
        public void RequestRefresh()
        {
            if (Source == null) { ApplySource(); return; }
            var state = Source.State;
            if (state != null && Context.Matches(state.Context) && (state.IsBusy || state.IsLoading)) return;
            Source.Refresh(Context);
            ApplySource();
        }
        public void RequestUpgrade(uint id, uint expectedRank, uint count)
        {
            var state = Source?.State;
            if (state == null || !Context.Matches(state.Context) || !state.IsConnected || state.IsReadOnly ||
                state.IsBusy || state.IsLoading || (count != 1 && count != 10)) return;
            var entry = state.Snapshot?.Find(id);
            if (entry == null || entry.Rank != expectedRank || !SkillPanel.CanUpgrade(state, entry)) return;
            Source.Upgrade(Context, id, expectedRank, count);
            ApplySource();
        }
        public void SelectPreviewClass(uint classId)
        {
            if (Source != null || Context.IsPet || classId < 1 || classId > 4) return;
            _previewClass = classId;
            ApplySource();
        }
        private void ApplySource()
        {
            if (_panel == null) return;
            if (Source == null)
            {
                var snapshot = new SkillBookSnapshot { ClassId = _previewClass };
                if (!Context.IsPet)
                    foreach (var definition in SkillCatalog.ForClass(_previewClass))
                        snapshot.Entries.Add(new SkillEntryState { Id = definition.id, Definition = definition });
                _panel.Apply(new SkillBookViewState { Context = Context, Snapshot = snapshot, IsReadOnly = true,
                    Message = Context.IsPet ? "宠物技能信息未同步" : "技能信息未同步 · 仅浏览职业图鉴" });
                return;
            }
            var state = Source.State;
            // 不展示其他身份的快照，也不能把迟到回包变成永久 loading；刷新必须可用。
            _panel.Apply(state != null && Context.Matches(state.Context) ? state :
                new SkillBookViewState { Context = Context,
                    Error = "当前技能信息尚未同步，请点击刷新重试。" });
        }
        private void EnsurePanel()
        {
            if (_panel != null) return;
            var canvasObject = new GameObject("[SkillUgui]", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 162;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("SkillWindowRoot", canvasObject.transform,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            _panel = new SkillPanel(this, root);
        }
        private void Update()
        {
            if (!IsVisible) return;
            var game = AppBootstrap.Instance?.GameClient;
            if (game != null && (!game.InGame || !game.IsGateReady)) { HidePanel(); return; }
            if (Battle.BattleUiRoot.Instance?.IsBattleLayerVisible == true) { HidePanel(); return; }
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected?.GetComponentInParent<TMPro.TMP_InputField>()?.isFocused == true) return;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) HidePanel();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
#endif
        }
        private void OnDestroy()
        {
            if (Source != null) Source.Changed -= ApplySource;
            Source = null;
            if (Instance == this) Instance = null;
        }
    }
}
