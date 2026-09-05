using System;
using System.Collections.Generic;
using FairyGUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MmorpgClient.Game.Battle.Presentation;
// FairyGUI 也有 Image 类型(只借用其 GTween),UI 图一律指 UGUI 的 Image
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>伤害数字种类(对应 digits_&lt;variant&gt; 字集)。</summary>
    public enum NumberKind
    {
        Normal,
        Crit,
        Heal,
        Miss,
    }

    /// <summary>
    /// 单位表现视图(替代 BattleUnitSlot 的色块槽):
    ///  - Image 角色(玩家:QdaoHeadbandBoy 跑步条 E/W 首帧作 idle;怪物:Battle/Monsters/&lt;id&gt; 帧条,缺则程序化剪影),
    ///    资源解析全部走 <see cref="BattleArtCatalog"/>;
    ///  - 脚底椭圆阴影、头顶 HP/MP 双细条(缓动 + 掉血虚影)、脚下名字(阵营配色 + 描边)、buff 图标行、状态徽标;
    ///    这些"名牌"挂在独立的名牌层(plateParent,立绘/特效之上)的 <see cref="Plate"/> 上,由
    ///    <see cref="BattlePlateFollower"/> 每帧 LateUpdate 跟随单位根(位移/缩放/透明),
    ///    条的高度按立绘**实际可见顶点**(BattleArtCatalog.MeasureVisibleTop)贴着头顶 8px,而不是固定偏移;
    ///  - 动作 API:PlayIdle / PlayAttackLunge / PlayCast / PlayHit / PlayDeath / PlayWin / PlayDodge,
    ///    全部程序化(位移/缩放/闪白/残影,GTween realtime);若 Battle/Characters/&lt;id&gt;/&lt;action&gt;_E_strip 存在则切帧播放;
    ///  - 伤害数字(digits 字集,缺则 TMP 飘字)与特效帧条(SpawnFx)挂在舞台层,不随本单位位移;
    ///  - 根节点 pivot 在脚底:SetPlacement(foot, scale) 直接用 BattleStage 的槽位坐标与缩放。
    /// 与 BattleUnitSlot 的旧接口(Apply / SetHealthDuringPlayback / ShowDeadMark / SetHighlight /
    /// PlayShake / PlayFlash / SpawnFloatText / Destroy)保持兼容,便于 BattleScreen 平滑替换。
    /// </summary>
    public sealed class BattleUnitView
    {
        public const float RootWidth = 260f;
        public const float RootHeight = 340f;
        /// <summary>根节点内的脚底线(从顶部算)。</summary>
        public const float GroundY = 300f;
        /// <summary>
        /// 名牌最高能伸到脚底之上多少设计像素(缩放 1):立绘可见高上限 200 + 头顶留白 8 + MP/HP 条 + buff 行。
        /// BattleStage 用它保证敌方后排的 buff 行不进顶部 HUD 带。
        /// </summary>
        public const float OverheadReach = 256f;
        /// <summary>立绘可见顶点到 MP 条底边的留白。</summary>
        public const float OverheadGap = 8f;
        /// <summary>立绘可见高的兜底(GPU 回读失败时;22 套角色帧条实测 152~170)。</summary>
        public const float DefaultVisibleTop = 170f;
        /// <summary>可见高的合法区间(避免异常贴图把条推出根节点)。</summary>
        public const float MinVisibleTop = 60f;
        public const float MaxVisibleTop = 200f;
        public const float PlayerHeight = 230f;
        public const float MonsterHeight = 200f;

        /// <summary>冲刺攻击从开始到"挥击命中"的秒数(演出层据此触发目标受击)。</summary>
        public const float AttackHitDelaySeconds = 0.32f;
        /// <summary>冲刺攻击回位起点 / 回位时长(1x 秒数)。</summary>
        public const float AttackReturnStartSeconds = 0.55f;
        public const float AttackReturnSeconds = 0.25f;
        /// <summary>
        /// 冲刺攻击整段动作时长(1x):冲刺 0.22 → 挥击 → 回位到 0.80。必须 ≤ TurnPlan.AttackSeconds,
        /// 否则下一拍的 BeginAction/PlayHit 会杀掉回位 tween 并瞬移(TurnPlanTests 断言)。
        /// 全部 Tween/Delay 走 BattleTempo 倍率,所以任何倍率下该关系都成立。
        /// </summary>
        public const float AttackActionSeconds = AttackReturnStartSeconds + AttackReturnSeconds;
        /// <summary>施法从开始到"释放"的秒数。</summary>
        public const float CastReleaseDelaySeconds = 0.45f;
        /// <summary>施法收势起点 / 收势时长(1x 秒数)。</summary>
        public const float CastSettleStartSeconds = 0.55f;
        public const float CastSettleSeconds = 0.2f;
        /// <summary>施法整段程序化动作时长(1x),须 ≤ TurnPlan.CastSeconds。</summary>
        public const float CastActionSeconds = CastSettleStartSeconds + CastSettleSeconds;
        /// <summary>冲刺时停在目标前方的距离(设计像素)。</summary>
        public const float LungeStopDistance = 150f;

        /// <summary>头顶条宽(spec §1:约单位宽的 60%)。</summary>
        private const float BarWidth = 120f;
        private const float HpBarHeight = 8f;
        private const float MpBarHeight = 6f;
        private const float BuffIconSize = 26f;
        private const int MaxBuffIcons = 6;

        // ── 只读状态 ──
        public ulong ActorId { get; private set; }
        public uint TeamIndex { get; private set; }
        public bool TeamIsMine { get; }
        public bool IsSelf { get; }
        public int Slot { get; private set; }
        public bool IsDead { get; private set; }
        public bool Fled { get; private set; }
        public bool IsMonster { get; private set; }
        public uint MonsterTableId { get; private set; }
        public string CharacterId { get; private set; } = BattleArtCatalog.DefaultCharacterId;
        /// <summary>当前目标高亮态(目标选择模式下 BattleScreen 据此判可选/已选)。</summary>
        public SlotHighlight Highlight => _highlight;
        /// <summary>脚底点(设计坐标,y 向下)。</summary>
        public Vector2 FootPosition { get; private set; }
        public float Scale { get; private set; } = 1f;
        public RectTransform Root => _root;
        /// <summary>立绘可见顶点到脚底的高度(设计像素,缩放 1;由贴图实际不透明像素测得)。</summary>
        public float VisibleTop => _visibleTop;
        /// <summary>头顶点(设计坐标)= HP 条上沿:伤害数字从条上方弹出。</summary>
        public Vector2 HeadPosition => new Vector2(FootPosition.x, FootPosition.y - (GroundY - _hpY) * Scale);
        /// <summary>胸口点(设计坐标):命中特效锚点(可见高的 55%)。</summary>
        public Vector2 ChestPosition => new Vector2(FootPosition.x, FootPosition.y - _visibleTop * Scale * 0.55f);
        /// <summary>名牌根(独立名牌层;未提供名牌层时为根节点的子节点)。</summary>
        public RectTransform Plate => _plate;
        /// <summary>朝向(敌方朝右 E,我方朝左 W)。</summary>
        public bool FacingEast => _facingEast;
        /// <summary>最近一次 Apply 的权威状态(Abort 复位时按它重刷)。</summary>
        public BattleActorState LastState => _lastState;
        /// <summary>当前显示的 HP(播放中随 SetHealthDuringPlayback 更新;BuffTick 回血/掉血判定用)。</summary>
        public ulong CurrentHealth => _health;
        public ulong CurrentMana => _mana;

        /// <summary>共享特效播放器(BattleScreen 注入;未注入时退回单次 Image 播放)。</summary>
        public BattleFxPlayer Fx { get; set; }
        /// <summary>共享伤害数字池(BattleScreen 注入;未注入时退回内联拼字/TMP 飘字)。</summary>
        public DamageNumberPool Numbers { get; set; }
        /// <summary>共享残影池(BattleScreen 注入;未注入时退回单次 Image 创建/销毁)。</summary>
        public BattleAfterimagePool Ghosts { get; set; }

        private readonly BattleUiRoot _owner;
        private readonly UnityEngine.Transform _parent;
        private readonly RectTransform _root;
        private readonly CanvasGroup _group;
        private readonly Image _ring;
        private readonly Image _shadow;
        private readonly Image _body;
        private readonly RectTransform _bodyRect;
        private readonly Image _flash;
        private readonly Image _hitArea;
        private readonly Image _hpGhost;
        private readonly Image _hpFill;
        private readonly RectTransform _hpFillRect;
        private readonly RectTransform _hpGhostRect;
        private readonly Image _mpFill;
        private readonly RectTransform _mpFillRect;
        private readonly RectTransform _buffRow;
        private readonly RectTransform _plate;
        private readonly CanvasGroup _plateGroup;
        private readonly RectTransform _hpBgRect;
        private readonly RectTransform _mpBgRect;
        private readonly TMP_Text _name;
        private readonly Image _badgePlate;
        private readonly TMP_Text _badgeText;
        private readonly List<GameObject> _buffIcons = new List<GameObject>();
        private readonly object _idleToken = new object(); // idle 独立 tween 目标,可单独 Kill

        private ulong _maxHealth;
        private ulong _maxMana;
        private ulong _health;
        private ulong _mana;
        private float _bodyHeight = PlayerHeight;
        /// <summary>立绘可见顶点到脚底的高度(缩放 1)。</summary>
        private float _visibleTop = DefaultVisibleTop;
        /// <summary>HP 条底板在根内的 y(从顶部算),LayoutOverhead 算出;HeadPosition 由它推。</summary>
        private float _hpY = GroundY - DefaultVisibleTop - OverheadGap - MpBarHeight - 2f - HpBarHeight;
        /// <summary>
        /// 本体 Image 底边的 anchored y。UGUI Image 不认 sprite pivot,帧条的脚底线在格子内 8%(FeetPivot)处,
        /// 故按 sprite pivot 把底边再往下推,让画出来的脚踩在 GroundY(阴影中心)上。
        /// </summary>
        private float _bodyBaseY = -GroundY;
        private bool _mirrored;
        private bool _facingEast;
        private SlotHighlight _highlight = SlotHighlight.None;
        private Sprite _idleSprite;
        private StripAnim _idleStrip;
        private bool _destroyed;
        private BattleActorState _lastState;

        public BattleUnitView(BattleUiRoot owner, UnityEngine.Transform parent, ulong actorId,
            bool isSelf, bool teamIsMine, int slot, Action<ulong> onClicked, UnityEngine.Transform plateParent = null)
        {
            _owner = owner;
            _parent = parent;
            ActorId = actorId;
            IsSelf = isSelf;
            TeamIsMine = teamIsMine;
            Slot = slot;
            _facingEast = BattleArtCatalog.FacingEast(teamIsMine);

            _root = QdaoUguiFactory.CreateRect($"Unit_{actorId}", parent, 0f, 0f, RootWidth, RootHeight);
            _root.pivot = new Vector2(0.5f, 1f - GroundY / RootHeight); // pivot 落在脚底
            _group = _root.gameObject.AddComponent<CanvasGroup>();

            // 目标高亮环(脚底,最底层)
            var ring = BattleArtCatalog.LoadSpawnRing(teamIsMine) ?? BattleArtCatalog.ShadowSprite;
            _ring = QdaoUguiFactory.CreateImage("Ring", _root, RootWidth * 0.5f - 110f, GroundY - 55f, 220f, 110f, ring);
            _ring.preserveAspect = false;
            _ring.gameObject.SetActive(false);

            // 脚底阴影
            _shadow = QdaoUguiFactory.CreateImage("Shadow", _root, RootWidth * 0.5f - 60f, GroundY - 22f, 120f, 44f,
                BattleArtCatalog.ShadowSprite);
            _shadow.color = new Color(0f, 0f, 0f, 0.55f);

            // 角色本体:pivot 底中,落在脚底
            _body = QdaoUguiFactory.CreateImage("Body", _root, 0f, 0f, PlayerHeight, PlayerHeight, null);
            _bodyRect = _body.rectTransform;
            _bodyRect.pivot = new Vector2(0.5f, 0f);
            _bodyRect.anchoredPosition = new Vector2(RootWidth * 0.5f, _bodyBaseY);
            _body.preserveAspect = true;

            // 闪白覆盖层(加色材质;shader 缺失时退回 alpha 白闪)
            var flashRect = QdaoUguiFactory.CreateStretch("Flash", _body.transform, Vector4.zero);
            _flash = flashRect.gameObject.AddComponent<Image>();
            _flash.raycastTarget = false;
            _flash.preserveAspect = true;
            _flash.color = new Color(1f, 1f, 1f, 0f);
            var flashMat = BattleArtCatalog.FlashMaterial;
            if (flashMat != null) _flash.material = flashMat;

            // 点击区(透明)
            _hitArea = QdaoUguiFactory.CreateImage("Hit", _root, RootWidth * 0.5f - 80f, GroundY - 240f, 160f, 240f, null, true);
            _hitArea.color = new Color(1f, 1f, 1f, 0f);
            var button = _hitArea.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClicked?.Invoke(ActorId));

            // 名牌根:独立名牌层(立绘/特效之上)+ 跟随组件;没给名牌层时退回挂在根下(坐标系与根一致)
            _plate = QdaoUguiFactory.CreateRect($"Plate_{actorId}", plateParent != null ? plateParent : _root, 0f, 0f, RootWidth, RootHeight);
            if (plateParent != null)
            {
                _plate.pivot = _root.pivot;
                _plate.anchoredPosition = _root.anchoredPosition;
                _plateGroup = _plate.gameObject.AddComponent<CanvasGroup>();
                _plateGroup.blocksRaycasts = false;
                _plateGroup.interactable = false;
                var follower = _plate.gameObject.AddComponent<BattlePlateFollower>();
                follower.Bind(_root, _group, _plateGroup);
            }

            // 头顶 HP/MP 细条(y 由 LayoutOverhead 按立绘可见顶点定)
            float barX = RootWidth * 0.5f - BarWidth * 0.5f;
            var hpBg = BattleUiWidgets.CreatePanel("HpBg", _plate, barX, 36f, BarWidth, HpBarHeight, BattleUiStyle.BarBg, false);
            _hpBgRect = hpBg.rectTransform;
            _hpGhost = BattleUiWidgets.CreatePanel("HpGhost", hpBg.transform, 1f, 1f, BarWidth - 2f, HpBarHeight - 2f,
                new Color(1f, 0.55f, 0.45f, 0.85f), false);
            _hpGhostRect = _hpGhost.rectTransform;
            _hpFill = BattleUiWidgets.CreatePanel("HpFill", hpBg.transform, 1f, 1f, BarWidth - 2f, HpBarHeight - 2f, BattleUiStyle.HpFill, false);
            _hpFillRect = _hpFill.rectTransform;
            var mpBg = BattleUiWidgets.CreatePanel("MpBg", _plate, barX, 46f, BarWidth, MpBarHeight, BattleUiStyle.BarBg, false);
            _mpBgRect = mpBg.rectTransform;
            _mpFill = BattleUiWidgets.CreatePanel("MpFill", mpBg.transform, 1f, 1f, BarWidth - 2f, MpBarHeight - 2f, BattleUiStyle.MpFill, false);
            _mpFillRect = _mpFill.rectTransform;

            // buff 图标行(条上方,左对齐)
            _buffRow = QdaoUguiFactory.CreateRect("Buffs", _plate, barX, 4f, BarWidth + 60f, BuffIconSize + 2f);

            // 脚下名字(阵营配色 + 黑描边;≥22px,宽 220 不压相邻单位)
            _name = QdaoUguiFactory.CreateText("Name", _plate, RootWidth * 0.5f - 110f, GroundY + 4f, 220f, 34f, string.Empty, 24f,
                NameColor(false), TextAlignmentOptions.Center);
            _name.fontStyle = FontStyles.Bold;
            BattleUiWidgets.ApplyOutline(_name, 0.26f, new Color32(0, 0, 0, 235));

            // 状态徽标(防御/阵亡/逃离):条右侧
            _badgePlate = BattleUiWidgets.CreatePanel("Badge", _plate, barX + BarWidth + 6f, 58f, 44f, 24f,
                BattleUiStyle.PanelBg, false);
            _badgeText = QdaoUguiFactory.CreateText("BadgeText", _badgePlate.transform, 0f, 0f, 44f, 24f, string.Empty, 16f,
                BattleUiStyle.WarnText, TextAlignmentOptions.Center);
            _badgePlate.gameObject.SetActive(false);

            ApplyBodySprite(null, false);
        }

        /// <summary>
        /// 按立绘可见顶点排头顶块:MP 条底边贴着可见顶点上方 <see cref="OverheadGap"/>,
        /// 其上依次 HP 条、buff 行;徽标贴条右侧。缩放由根/名牌根统一施加,所以这里全是缩放 1 的根内坐标。
        /// </summary>
        private void LayoutOverhead()
        {
            float top = Mathf.Clamp(_visibleTop, MinVisibleTop, MaxVisibleTop);
            float mpY = GroundY - top - OverheadGap - MpBarHeight;
            float hpY = mpY - 2f - HpBarHeight;
            float buffY = hpY - 4f - (BuffIconSize + 2f);
            _hpY = hpY;
            if (_mpBgRect != null) _mpBgRect.anchoredPosition = new Vector2(_mpBgRect.anchoredPosition.x, -mpY);
            if (_hpBgRect != null) _hpBgRect.anchoredPosition = new Vector2(_hpBgRect.anchoredPosition.x, -hpY);
            if (_buffRow != null) _buffRow.anchoredPosition = new Vector2(_buffRow.anchoredPosition.x, -buffY);
            if (_badgePlate != null) _badgePlate.rectTransform.anchoredPosition = new Vector2(_badgePlate.rectTransform.anchoredPosition.x, -(hpY - 5f));
        }

        // ── 布局 ────────────────────────────────────────────

        /// <summary>按舞台槽位摆放:脚底点 + 缩放(近大远小)。</summary>
        public void SetPlacement(Vector2 foot, float scale, int slot = -1)
        {
            FootPosition = foot;
            Scale = scale;
            if (slot >= 0) Slot = slot;
            if (_root == null) return;
            _root.anchoredPosition = new Vector2(foot.x, -foot.y);
            _root.localScale = new UnityEngine.Vector3(scale, scale, 1f);
            if (_plateGroup != null && _plate != null)
            {
                // 跟随组件下一帧 LateUpdate 才同步,这里先对齐,免得开局闪一帧
                _plate.anchoredPosition = _root.anchoredPosition;
                _plate.localScale = _root.localScale;
            }
        }

        /// <summary>绘制顺序:脚底 y 大的后画(调用方按 BattleStage.CompareDepth 排好序后依次调用);名牌同序。</summary>
        public void SetSiblingIndex(int index)
        {
            if (_root != null) _root.SetSiblingIndex(index);
            if (_plateGroup != null && _plate != null) _plate.SetSiblingIndex(index);
        }

        // ── 权威状态 ─────────────────────────────────────────

        /// <summary>按权威状态全量刷新(名字/等级/HP/MP/buff/徽标/外观)。</summary>
        public void Apply(BattleActorState state)
        {
            if (state == null || _root == null) return;
            _lastState = state;
            ActorId = state.ActorId;
            TeamIndex = state.TeamIndex;
            _maxHealth = state.MaxHealth;
            _maxMana = state.MaxMana;
            _health = state.Attributes?.Health ?? 0;
            _mana = state.Attributes?.Mana ?? 0;

            bool wasMonster = IsMonster;
            uint prevMonster = MonsterTableId;
            IsMonster = state.ActorType == eBattleActorType.BattleActorTypeMonster;
            MonsterTableId = state.MonsterTableId;
            CharacterId = BattleArtCatalog.CharacterIdFor(state);
            if (_idleSprite == null || wasMonster != IsMonster || prevMonster != MonsterTableId)
                ResolveAppearance();

            string name = string.IsNullOrEmpty(state.Name)
                ? (IsMonster ? $"怪物{state.MonsterTableId}" : $"玩家{state.ActorId}")
                : state.Name;
            _name.text = $"{(IsSelf ? "★" : string.Empty)}Lv{state.Level} {name}";
            _name.color = NameColor(IsMonster);

            SetBarsImmediate();
            RebuildBuffIcons(state);

            bool dead = state.IsDead;
            bool fled = state.Fled;
            if (dead && !IsDead) ShowDeadMark();           // 权威说已死而本地没播过:直接落终态
            if (!dead && IsDead) ReviveVisual();            // 复活(PVE 多回合复活)
            IsDead = dead;
            Fled = fled;
            if (fled) ShowBadge("逃", BattleUiStyle.BuffCutText);
            else if (dead) ShowBadge("亡", BattleUiStyle.DamageText);
            else if (state.IsDefending) ShowBadge("防", BattleUiStyle.WarnText);
            else HideBadge();

            // 死亡:尸体不保留(spec §1),立绘与名牌一并隐藏;逃离:半透明留位
            _group.alpha = dead ? 0f : fled ? 0.4f : 1f;
            SetHighlight(SlotHighlight.None);
            if (!dead && !fled && !GTween.IsTweening(_idleToken)) PlayIdle();
        }

        /// <summary>回合播放期间按 target_health_after 刷 HP(带缓动与掉血虚影)。</summary>
        public void SetHealthDuringPlayback(ulong current)
        {
            ulong prev = _health;
            _health = current;
            float ratio = Ratio(current, _maxHealth);
            GTween.Kill(_hpFillRect);
            GTween.Kill(_hpGhostRect);
            float fromW = _hpFillRect != null ? _hpFillRect.sizeDelta.x : 0f;
            float toW = (BarWidth - 2f) * ratio;
            Tween(fromW, toW, 0.15f, EaseType.QuadOut, t => SetWidth(_hpFillRect, t.value.x)).SetTarget(_hpFillRect);
            if (current < prev)
            {
                // 虚影停留 0.3s 再缩,形成"刚掉的血"残留(按倍率缩放)
                float ghostFrom = _hpGhostRect != null ? _hpGhostRect.sizeDelta.x : fromW;
                GTween.To(Mathf.Max(ghostFrom, fromW), toW, BattleTempo.Scale(0.4f)).SetDelay(BattleTempo.Scale(0.3f)).SetEase(EaseType.QuadOut)
                    .SetIgnoreEngineTimeScale(true).SetTarget(_hpGhostRect)
                    .OnUpdate((GTweenCallback1)(t => SetWidth(_hpGhostRect, t.value.x)));
            }
            else
            {
                SetWidth(_hpGhostRect, toW);
            }
            _hpFill.color = ratio < 0.3f ? BattleUiStyle.DamageText : BattleUiStyle.HpFill;
        }

        /// <summary>回合播放期间刷 MP(target_mana_after / 施法耗蓝)。</summary>
        public void SetManaDuringPlayback(ulong current)
        {
            _mana = current;
            float toW = (BarWidth - 2f) * Ratio(current, _maxMana);
            GTween.Kill(_mpFillRect);
            float fromW = _mpFillRect != null ? _mpFillRect.sizeDelta.x : 0f;
            Tween(fromW, toW, 0.2f, EaseType.QuadOut, t => SetWidth(_mpFillRect, t.value.x)).SetTarget(_mpFillRect);
        }

        /// <summary>回合播放中的死亡终态(PlayDeath 播完或权威状态直接给):整个单位(含名牌/条/buff)隐藏,尸体不保留。</summary>
        public void ShowDeadMark()
        {
            IsDead = true;
            KillActionTweens();
            ShowBadge("亡", BattleUiStyle.DamageText);
            if (_group != null) _group.alpha = 0f;
            if (_body != null) _body.color = new Color(0.45f, 0.45f, 0.5f, 1f);
        }

        public void SetHighlight(SlotHighlight highlight)
        {
            _highlight = highlight;
            if (_ring == null) return;
            switch (highlight)
            {
                case SlotHighlight.Targetable:
                    _ring.gameObject.SetActive(true);
                    _ring.color = new Color(1f, 0.9f, 0.45f, 0.75f);
                    break;
                case SlotHighlight.Selected:
                    _ring.gameObject.SetActive(true);
                    _ring.color = new Color(1f, 0.35f, 0.25f, 0.95f);
                    break;
                default:
                    _ring.gameObject.SetActive(false);
                    break;
            }
        }

        // ── 动作 API ─────────────────────────────────────────

        /// <summary>待机:呼吸缩放 ±2%(或 idle 帧条 6fps 循环)。死亡/逃离不播。</summary>
        public void PlayIdle()
        {
            if (_root == null || IsDead || Fled) return;
            GTween.Kill(_idleToken);
            ResetBodyTransform();
            if (_idleStrip != null && _idleStrip.Count > 1)
            {
                var strip = _idleStrip;
                GTween.To(0f, strip.Count, strip.DurationSeconds).SetEase(EaseType.Linear).SetRepeat(-1)
                    .SetIgnoreEngineTimeScale(true).SetTarget(_idleToken)
                    .OnUpdate((GTweenCallback1)(t => SetFrame(strip, t.value.x)));
                return;
            }
            GTween.To(1f, 1.02f, 0.9f).SetEase(EaseType.SineInOut).SetRepeat(-1, true)
                .SetIgnoreEngineTimeScale(true).SetTarget(_idleToken)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (_bodyRect == null) { GTween.Kill(_idleToken); return; }
                    _bodyRect.localScale = new UnityEngine.Vector3(_mirrored ? -1f : 1f, t.value.x, 1f);
                }));
        }

        /// <summary>
        /// 冲刺攻击:冲到目标前方(残影)→ 挥击(命中时刻 = <see cref="AttackHitDelaySeconds"/>,回调 onHit)→ 回位。
        /// 有 attack 帧条则挥击段切帧,命中帧触发 onHit。
        /// </summary>
        public void PlayAttackLunge(Vector2 targetFoot, Action onHit = null)
        {
            if (_root == null || IsDead) return;
            BeginAction();

            var from = FootPosition;
            var dir = targetFoot - from;
            float dist = dir.magnitude;
            var dest = dist > 1f ? targetFoot - dir / dist * LungeStopDistance : from;
            var strip = BattleArtCatalog.LoadCharacterAction(CharacterId, "attack", _facingEast);
            if (IsMonster) strip = BattleArtCatalog.LoadMonsterAction(MonsterTableId, "attack", _facingEast);

            // 1) 冲刺 0.22s + 残影
            Tween(0f, 1f, 0.22f, EaseType.QuadOut, t =>
            {
                if (_root == null) return;
                var p = Vector2.LerpUnclamped(from, dest, t.value.x);
                _root.anchoredPosition = new Vector2(p.x, -p.y);
            });
            Delay(0.07f, () => SpawnAfterimage());
            Delay(0.14f, () => SpawnAfterimage());

            // 2) 挥击(0.22s 起,命中 0.32s)
            Delay(0.22f, () =>
            {
                if (_root == null) return;
                if (strip != null && strip.Count > 0)
                {
                    int hitFrame = Mathf.Min(3, strip.Count - 1);
                    bool fired = false;
                    PlayStrip(strip, 0.3f, frame =>
                    {
                        if (!fired && frame >= hitFrame) { fired = true; onHit?.Invoke(); }
                    }, () => { if (!fired) { fired = true; onHit?.Invoke(); } });
                }
                else
                {
                    float tilt = _facingEast ? -18f : 18f;
                    Tween(0f, 1f, 0.1f, EaseType.QuadOut, t =>
                    {
                        if (_bodyRect == null) return;
                        float k = t.value.x;
                        _bodyRect.localRotation = Quaternion.Euler(0f, 0f, tilt * k);
                        _bodyRect.localScale = new UnityEngine.Vector3((_mirrored ? -1f : 1f) * (1f + 0.12f * k), 1f + 0.12f * k, 1f);
                    }, () =>
                    {
                        onHit?.Invoke();
                        Tween(1f, 0f, 0.12f, EaseType.QuadIn, t =>
                        {
                            if (_bodyRect == null) return;
                            float k = t.value.x;
                            _bodyRect.localRotation = Quaternion.Euler(0f, 0f, tilt * k);
                            _bodyRect.localScale = new UnityEngine.Vector3((_mirrored ? -1f : 1f) * (1f + 0.12f * k), 1f + 0.12f * k, 1f);
                        });
                    });
                }
            });

            // 3) 回位 0.25s(0.55s 起),结束回 idle;整段 = AttackActionSeconds ≤ TurnPlan.AttackSeconds
            Delay(AttackReturnStartSeconds, () =>
            {
                if (_root == null) return;
                ResetBodyTransform();
                Tween(0f, 1f, AttackReturnSeconds, EaseType.QuadIn, t =>
                {
                    if (_root == null) return;
                    var p = Vector2.LerpUnclamped(dest, from, t.value.x);
                    _root.anchoredPosition = new Vector2(p.x, -p.y);
                }, () => EndAction());
            });
        }

        /// <summary>施法:原地抬手聚气(缩放 1.06 + 金色脉冲 + buff_rise 光柱),释放时刻回调 onRelease。</summary>
        public void PlayCast(Action onRelease = null)
        {
            if (_root == null || IsDead) return;
            BeginAction();

            var strip = IsMonster
                ? BattleArtCatalog.LoadMonsterAction(MonsterTableId, "cast", _facingEast)
                : BattleArtCatalog.LoadCharacterAction(CharacterId, "cast", _facingEast);
            SpawnFx("buff_rise", true, 300f);

            if (strip != null && strip.Count > 0)
            {
                int hitFrame = Mathf.Min(3, strip.Count - 1);
                bool fired = false;
                PlayStrip(strip, 0.7f, frame =>
                {
                    if (!fired && frame >= hitFrame) { fired = true; onRelease?.Invoke(); }
                }, () =>
                {
                    if (!fired) { fired = true; onRelease?.Invoke(); }
                    EndAction();
                });
                return;
            }

            // 聚气:0.35s 放大 + 抬升;释放:0.45s;收势:0.55s 起 0.2s(整段 CastActionSeconds ≤ TurnPlan.CastSeconds)
            Tween(0f, 1f, 0.35f, EaseType.QuadOut, t =>
            {
                if (_bodyRect == null) return;
                float k = t.value.x;
                _bodyRect.localScale = new UnityEngine.Vector3((_mirrored ? -1f : 1f) * (1f + 0.06f * k), 1f + 0.06f * k, 1f);
                _bodyRect.anchoredPosition = new Vector2(RootWidth * 0.5f, _bodyBaseY + 10f * k);
            });
            FlashTint(new Color(1f, 0.85f, 0.4f, 1f), 0.45f, 0.8f);
            Delay(CastReleaseDelaySeconds, () => onRelease?.Invoke());
            Delay(CastSettleStartSeconds, () =>
            {
                Tween(1f, 0f, CastSettleSeconds, EaseType.QuadIn, t =>
                {
                    if (_bodyRect == null) return;
                    float k = t.value.x;
                    _bodyRect.localScale = new UnityEngine.Vector3((_mirrored ? -1f : 1f) * (1f + 0.06f * k), 1f + 0.06f * k, 1f);
                    _bodyRect.anchoredPosition = new Vector2(RootWidth * 0.5f, _bodyBaseY + 10f * k);
                }, () => EndAction());
            });
        }

        /// <summary>受击:闪白一帧 + 后仰位移(普通 8px / 暴击 16px,0.15s 回弹)+ 命中星爆;暴击额外缩放顿一下。</summary>
        public void PlayHit(bool isCrit)
        {
            if (_root == null) return;
            KillActionTweens();
            ResetBodyTransform();

            var strip = IsMonster
                ? BattleArtCatalog.LoadMonsterAction(MonsterTableId, "hit", _facingEast)
                : BattleArtCatalog.LoadCharacterAction(CharacterId, "hit", _facingEast);

            FlashWhite(isCrit ? 0.22f : 0.16f, isCrit ? 1f : 0.85f);
            SpawnFx("hit_star", false, isCrit ? 320f : 240f);

            float push = (isCrit ? 16f : 8f) * (TeamIsMine ? 1f : -1f); // 远离战场中心
            var basePos = new Vector2(FootPosition.x, -FootPosition.y);
            Tween(0f, 1f, 0.06f, EaseType.QuadOut, t =>
            {
                if (_root == null) return;
                _root.anchoredPosition = basePos + new Vector2(push * t.value.x, 0f);
            }, () =>
            {
                Tween(1f, 0f, 0.15f, EaseType.BackOut, t =>
                {
                    if (_root == null) return;
                    _root.anchoredPosition = basePos + new Vector2(push * t.value.x, 0f);
                }, () =>
                {
                    if (_root != null) _root.anchoredPosition = basePos;
                    if (!IsDead) PlayIdle();
                });
            });

            if (strip != null && strip.Count > 0)
            {
                PlayStrip(strip, 0.25f, null, null);
            }
            else if (isCrit)
            {
                Tween(1.1f, 1f, 0.2f, EaseType.QuadOut, t =>
                {
                    if (_bodyRect == null) return;
                    _bodyRect.localScale = new UnityEngine.Vector3((_mirrored ? -1f : 1f) * t.value.x, t.value.x, 1f);
                });
            }
        }

        /// <summary>闪避:侧移 24px 半透明再回位。</summary>
        public void PlayDodge()
        {
            if (_root == null) return;
            KillActionTweens();
            ResetBodyTransform();
            float side = TeamIsMine ? 24f : -24f;
            var basePos = new Vector2(FootPosition.x, -FootPosition.y);
            Tween(0f, 1f, 0.12f, EaseType.QuadOut, t =>
            {
                if (_root == null) return;
                _root.anchoredPosition = basePos + new Vector2(side * t.value.x, 0f);
                _group.alpha = 1f - 0.5f * t.value.x;
            }, () =>
            {
                Tween(1f, 0f, 0.18f, EaseType.QuadIn, t =>
                {
                    if (_root == null) return;
                    _root.anchoredPosition = basePos + new Vector2(side * t.value.x, 0f);
                    _group.alpha = 1f - 0.5f * t.value.x;
                }, () => { if (!IsDead) PlayIdle(); });
            });
        }

        /// <summary>死亡:灰化 + 倒地(旋转 75° + 下沉)+ 渐隐到 0(名牌随之消失),死亡消散特效;1.0s 后落终态。</summary>
        public void PlayDeath()
        {
            if (_root == null) return;
            BeginAction();
            IsDead = true;
            HideBadge();

            var strip = IsMonster
                ? BattleArtCatalog.LoadMonsterAction(MonsterTableId, "die", _facingEast)
                : BattleArtCatalog.LoadCharacterAction(CharacterId, "die", _facingEast);
            SpawnFx("death_dissolve", true, 300f);

            if (strip != null && strip.Count > 0)
            {
                PlayStrip(strip, 0.9f, null, () => ShowDeadMark());
                Tween(1f, 0f, 1.0f, EaseType.QuadIn, t => { if (_group != null) _group.alpha = t.value.x; });
                return;
            }

            float fall = TeamIsMine ? -75f : 75f;
            var startColor = _body.color;
            Tween(0f, 1f, 0.5f, EaseType.QuadIn, t =>
            {
                if (_bodyRect == null) return;
                float k = t.value.x;
                _bodyRect.localRotation = Quaternion.Euler(0f, 0f, fall * k);
                _bodyRect.anchoredPosition = new Vector2(RootWidth * 0.5f, _bodyBaseY - 12f * k);
                _body.color = Color.Lerp(startColor, new Color(0.45f, 0.45f, 0.5f, 1f), k);
            }, () =>
            {
                Tween(1f, 0f, 0.5f, EaseType.QuadIn, t => { if (_group != null) _group.alpha = t.value.x; },
                    () => ShowDeadMark());
            });
        }

        /// <summary>防御:金色护盾泡(闪金 + 轻微下蹲)+ "防" 徽标。</summary>
        public void PlayDefend()
        {
            if (_root == null || IsDead) return;
            BeginAction();
            ShowBadge("防", BattleUiStyle.WarnText);
            FlashTint(new Color(1f, 0.85f, 0.35f, 1f), 0.5f, 0.9f);
            Tween(0f, 1f, 0.12f, EaseType.QuadOut, t =>
            {
                if (_bodyRect == null) return;
                float k = t.value.x;
                _bodyRect.localScale = new UnityEngine.Vector3((_mirrored ? -1f : 1f) * (1f + 0.08f * k), 1f - 0.08f * k, 1f);
            }, () =>
            {
                Tween(1f, 0f, 0.2f, EaseType.BackOut, t =>
                {
                    if (_bodyRect == null) return;
                    float k = t.value.x;
                    _bodyRect.localScale = new UnityEngine.Vector3((_mirrored ? -1f : 1f) * (1f + 0.08f * k), 1f - 0.08f * k, 1f);
                }, () => EndAction());
            });
        }

        /// <summary>逃跑:成功 → 向阵营外侧冲出并淡到 0.2 且标记逃离;失败 → 起步又缩回 + 抖动。</summary>
        public void PlayFlee(bool success)
        {
            if (_root == null || IsDead) return;
            BeginAction();
            var basePos = new Vector2(FootPosition.x, -FootPosition.y);
            // 我方在右下,向右下跑出;敌方向左上
            var dir = TeamIsMine ? new Vector2(1f, -0.35f).normalized : new Vector2(-1f, 0.35f).normalized;
            if (success)
            {
                Delay(0.05f, () => SpawnAfterimage());
                Delay(0.15f, () => SpawnAfterimage());
                Tween(0f, 1f, 0.45f, EaseType.QuadIn, t =>
                {
                    if (_root == null) return;
                    float k = t.value.x;
                    _root.anchoredPosition = basePos + dir * (320f * k);
                    if (_group != null) _group.alpha = 1f - 0.8f * k;
                }, () =>
                {
                    Fled = true;
                    ShowBadge("逃", BattleUiStyle.BuffCutText);
                    if (_root != null) _root.anchoredPosition = basePos;
                    if (_group != null) _group.alpha = 0.4f;
                    GTween.Kill(_idleToken);
                });
                return;
            }
            Tween(0f, 1f, 0.18f, EaseType.QuadOut, t =>
            {
                if (_root == null) return;
                _root.anchoredPosition = basePos + dir * (60f * t.value.x);
            }, () =>
            {
                Tween(1f, 0f, 0.22f, EaseType.BackOut, t =>
                {
                    if (_root == null) return;
                    _root.anchoredPosition = basePos + dir * (60f * t.value.x);
                }, () => EndAction());
            });
        }

        /// <summary>获得 buff:脚下 buff_rise 光柱 + 图标弹入(权威状态到达前先占位)。</summary>
        public void PlayBuffGain(uint buffId)
        {
            if (_root == null) return;
            SpawnFx("buff_rise", true, 300f);
            FlashTint(new Color(0.55f, 0.85f, 1f, 1f), 0.35f, 0.7f);
            if (_buffRow == null || _buffIcons.Count >= MaxBuffIcons) return;
            float x = _buffIcons.Count * (BuffIconSize + 3f);
            var sprite = BattleArtCatalog.LoadBuffIcon(buffId);
            GameObject go;
            if (sprite != null)
            {
                var icon = QdaoUguiFactory.CreateImage($"Buff_{buffId}", _buffRow, x, 0f, BuffIconSize, BuffIconSize, sprite);
                icon.preserveAspect = true;
                go = icon.gameObject;
            }
            else
            {
                var plate = BattleUiWidgets.CreatePanel($"Buff_{buffId}", _buffRow, x, 0f, BuffIconSize, BuffIconSize,
                    new Color(0.16f, 0.24f, 0.42f, 0.95f), false);
                QdaoUguiFactory.CreateText("T", plate.transform, 0f, 0f, BuffIconSize, BuffIconSize,
                    buffId.ToString(), 12f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
                go = plate.gameObject;
            }
            _buffIcons.Add(go);
            var rect = (RectTransform)go.transform;
            rect.localScale = UnityEngine.Vector3.zero;
            GTween.To(0f, 1f, BattleTempo.Scale(0.28f)).SetEase(EaseType.BackOut).SetIgnoreEngineTimeScale(true).SetTarget(go)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (go == null) { GTween.Kill(go); return; }
                    rect.localScale = new UnityEngine.Vector3(t.value.x, t.value.x, 1f);
                }));
        }

        /// <summary>失去 buff:灰闪一下(图标行由权威状态重建)。</summary>
        public void PlayBuffLose(uint buffId)
        {
            if (_root == null) return;
            FlashTint(new Color(0.6f, 0.6f, 0.65f, 1f), 0.3f, 0.6f);
        }

        /// <summary>
        /// 观战抢占/断线/跳过:杀掉本单位全部 tween,按最近权威状态复位(位置/缩放/透明/条/徽标),
        /// 活着的重新进 idle。
        /// </summary>
        public void ResetVisual()
        {
            if (_root == null || _destroyed) return;
            KillActionTweens();
            GTween.Kill(_idleToken);
            GTween.Kill(_hpFillRect);
            GTween.Kill(_hpGhostRect);
            GTween.Kill(_mpFillRect);
            if (_flash != null)
            {
                GTween.Kill(_flash);
                _flash.color = new Color(1f, 1f, 1f, 0f);
            }
            if (_body != null) _body.color = IsDead ? new Color(0.45f, 0.45f, 0.5f, 1f) : Color.white;
            if (_body != null && _idleSprite != null)
            {
                _body.sprite = _idleSprite;
                _flash.sprite = _idleSprite;
            }
            ResetBodyTransform();
            if (_lastState != null) Apply(_lastState);
            else
            {
                if (_group != null) _group.alpha = IsDead ? 0f : Fled ? 0.4f : 1f;
                if (!IsDead && !Fled) PlayIdle();
            }
        }

        /// <summary>胜利:原地跳两下。</summary>
        public void PlayWin()
        {
            if (_root == null || IsDead || Fled) return;
            BeginAction();
            var strip = IsMonster ? null : BattleArtCatalog.LoadCharacterAction(CharacterId, "win", _facingEast);
            if (strip != null && strip.Count > 0)
            {
                PlayStrip(strip, 0.8f, null, () => EndAction());
                return;
            }
            GTween.To(0f, 28f, 0.2f).SetEase(EaseType.QuadOut).SetRepeat(3, true)
                .SetIgnoreEngineTimeScale(true).SetTarget(this)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (_bodyRect == null) return;
                    _bodyRect.anchoredPosition = new Vector2(RootWidth * 0.5f, _bodyBaseY + t.value.x);
                }))
                .OnComplete((GTweenCallback)(() => EndAction()));
        }

        // ── 旧接口兼容 ──────────────────────────────────────

        public void PlayShake() => PlayHit(false);

        public void PlayFlash(Color color) => FlashTint(color, 0.3f, 0.8f);

        /// <summary>兼容旧槽的文字飘字(挂在舞台层,不随本单位位移)。</summary>
        public void SpawnFloatText(string value, Color color, bool big)
        {
            if (_parent == null) return;
            var head = HeadPosition;
            BattleUiWidgets.SpawnFloatText(_owner, _parent, head.x - 170f, head.y - 40f, value, color, big);
        }

        // ── 数字 / 特效 ──────────────────────────────────────

        /// <summary>
        /// 伤害/治疗数字(digits 字集;缺字集退回 TMP 飘字):头顶弹出缩放 → 上飘 60px → 淡出,约 1.2s;
        /// xOffset/delay 用于群攻多目标错位。
        /// </summary>
        public void ShowNumber(long value, NumberKind kind, float xOffset = 0f, float delay = 0f)
        {
            if (_parent == null) return;
            if (Numbers != null)
            {
                var headPos = HeadPosition;
                Numbers.Show(new Vector2(headPos.x + xOffset, headPos.y), value, kind, delay);
                return;
            }
            string variant = kind == NumberKind.Crit ? "crit" : kind == NumberKind.Heal ? "heal" : kind == NumberKind.Miss ? "miss" : "normal";
            var font = BattleArtCatalog.LoadDigits(variant);
            string text = kind == NumberKind.Miss ? "闪"
                : kind == NumberKind.Heal ? $"+{Math.Abs(value)}"
                : $"-{Math.Abs(value)}";
            if (kind == NumberKind.Crit) text = "暴击" + text;

            if (font == null)
            {
                Color color = kind == NumberKind.Heal ? BattleUiStyle.HealText
                    : kind == NumberKind.Miss ? BattleUiStyle.BuffCutText : BattleUiStyle.DamageText;
                if (delay > 0f) Delay(delay, () => SpawnFloatText(text, color, kind == NumberKind.Crit));
                else SpawnFloatText(text, color, kind == NumberKind.Crit);
                return;
            }

            float k = kind == NumberKind.Crit ? 0.95f : 0.72f; // 48×64 格 → 屏幕字高约 46 / 35
            float cellW = font.CellWidth * k, cellH = font.CellHeight * k;
            var head = HeadPosition;
            var container = QdaoUguiFactory.CreateRect("Number", _parent, head.x - 200f + xOffset, head.y - 70f - cellH, 400f, cellH + 10f);
            container.pivot = new Vector2(0.5f, 0.5f);
            container.anchoredPosition += new Vector2(200f, -(cellH + 10f) * 0.5f);
            var group = container.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.alpha = delay > 0f ? 0f : 1f;

            int glyphs = 0;
            foreach (char c in text) if (font.Lookup(c) != null) glyphs++;
            float total = glyphs * cellW * 0.86f;
            float x = 200f - total * 0.5f;
            foreach (char c in text)
            {
                var sprite = font.Lookup(c);
                if (sprite == null) continue;
                QdaoUguiFactory.CreateImage($"D_{c}", container, x, 5f, cellW, cellH, sprite).preserveAspect = true;
                x += cellW * 0.86f;
            }

            var go = container.gameObject;
            var startPos = container.anchoredPosition;
            GTween.To(1.5f, 1f, 0.16f).SetDelay(delay).SetEase(EaseType.BackOut).SetIgnoreEngineTimeScale(true).SetTarget(go)
                .OnStart((GTweenCallback)(() => { if (group != null) group.alpha = 1f; }))
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (go == null) { GTween.Kill(go); return; }
                    container.localScale = new UnityEngine.Vector3(t.value.x, t.value.x, 1f);
                }));
            GTween.To(0f, 1f, 1.15f).SetDelay(delay + 0.1f).SetEase(EaseType.QuadOut).SetIgnoreEngineTimeScale(true).SetTarget(go)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (go == null) { GTween.Kill(go); return; }
                    float p = t.value.x;
                    container.anchoredPosition = startPos + new Vector2(0f, 60f * p);
                    if (group != null) group.alpha = p < 0.6f ? 1f : 1f - (p - 0.6f) / 0.4f;
                }))
                .OnComplete((GTweenCallback)(() => { if (go != null) UnityEngine.Object.Destroy(go); }));
        }

        /// <summary>
        /// 在本单位上播一段特效帧条(Battle/Fx/&lt;fxId&gt;_strip):atFeet 用脚底锚点(pivot 取帧条 pivot),
        /// 否则贴胸口;缺图静默返回 0。返回时长(秒)。
        /// </summary>
        public float SpawnFx(string fxId, bool atFeet, float size = 300f)
        {
            if (_parent == null) return 0f;
            if (Fx != null)
                return Fx.Play(fxId, atFeet ? FootPosition : ChestPosition, size * Mathf.Max(0.6f, Scale), 0f, null, BattleFxPlayer.DefaultHitFrame, !_facingEast);
            var strip = BattleArtCatalog.LoadFx(fxId);
            if (strip == null || strip.Count == 0) return 0f;

            var anchor = atFeet ? FootPosition : ChestPosition;
            float top = anchor.y - size * (1f - strip.Pivot.y);
            float left = anchor.x - size * strip.Pivot.x;
            var image = QdaoUguiFactory.CreateImage($"Fx_{fxId}", _parent, left, top, size, size, strip.Frames[0]);
            image.preserveAspect = true;
            image.transform.SetAsLastSibling();
            var go = image.gameObject;
            float seconds = strip.DurationSeconds;
            GTween.To(0f, strip.Count, seconds).SetEase(EaseType.Linear).SetIgnoreEngineTimeScale(true).SetTarget(go)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (go == null) { GTween.Kill(go); return; }
                    image.sprite = strip.FrameAt(t.value.x / strip.Count);
                }))
                .OnComplete((GTweenCallback)(() => { if (go != null) UnityEngine.Object.Destroy(go); }));
            return seconds;
        }

        // ── 销毁 ────────────────────────────────────────────

        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            GTween.Kill(this);
            GTween.Kill(_idleToken);
            GTween.Kill(_hpFillRect);
            GTween.Kill(_hpGhostRect);
            GTween.Kill(_mpFillRect);
            if (_flash != null) GTween.Kill(_flash);
            if (_plateGroup != null && _plate != null) UnityEngine.Object.Destroy(_plate.gameObject);
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        }

        // ── 内部:外观 ──────────────────────────────────────

        private void ResolveAppearance()
        {
            _idleStrip = null;
            Sprite sprite = null;
            bool mirrored = false;
            float height;
            if (IsMonster)
            {
                var strip = BattleArtCatalog.LoadMonsterAction(MonsterTableId, "idle", _facingEast);
                if (strip != null && strip.Count > 0)
                {
                    sprite = strip.Frames[0];
                    mirrored = strip.Mirrored;
                    _idleStrip = strip;
                    height = PlayerHeight;
                }
                else
                {
                    sprite = BattleArtCatalog.GetMonsterSilhouette(MonsterTableId);
                    mirrored = !_facingEast; // 剪影按 E 向画,W 向镜像
                    height = MonsterHeight;
                }
            }
            else
            {
                var strip = BattleArtCatalog.LoadCharacterAction(CharacterId, "idle", _facingEast);
                if (strip != null && strip.Count > 0)
                {
                    sprite = strip.Frames[0];
                    mirrored = strip.Mirrored;
                    _idleStrip = strip;
                }
                else
                {
                    sprite = BattleArtCatalog.LoadPlayerIdle(_facingEast, out mirrored);
                }
                height = PlayerHeight;
            }
            _bodyHeight = height;
            ApplyBodySprite(sprite, mirrored);
        }

        private void ApplyBodySprite(Sprite sprite, bool mirrored)
        {
            _idleSprite = sprite;
            _mirrored = mirrored;
            if (_body == null) return;
            float footInset = 0f;
            if (sprite != null && sprite.rect.height > 0f)
                footInset = Mathf.Clamp01(sprite.pivot.y / sprite.rect.height) * _bodyHeight; // pivot 为像素值
            _bodyBaseY = -GroundY - footInset;
            if (sprite == null)
            {
                // 连玩家跑步条都没有:深色方块占位
                _body.sprite = null;
                _body.color = new Color(0.2f, 0.16f, 0.28f, 1f);
                _bodyRect.sizeDelta = new Vector2(_bodyHeight * 0.55f, _bodyHeight);
            }
            else
            {
                _body.sprite = sprite;
                _body.color = Color.white;
                float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
                _bodyRect.sizeDelta = new Vector2(_bodyHeight * aspect, _bodyHeight);
            }
            _flash.sprite = sprite;
            _hitArea.rectTransform.sizeDelta = new Vector2(Mathf.Max(120f, _bodyRect.sizeDelta.x * 0.8f), _bodyHeight);
            _hitArea.rectTransform.anchoredPosition = new Vector2(RootWidth * 0.5f - _hitArea.rectTransform.sizeDelta.x * 0.5f, -(GroundY - _bodyHeight));
            // 头顶块按立绘实际可见顶点排(贴图透明边距各不相同,固定偏移会让条悬空在头顶 90px 处)
            _visibleTop = sprite != null
                ? BattleArtCatalog.MeasureVisibleTop(sprite, _bodyHeight, DefaultVisibleTop)
                : _bodyHeight;
            LayoutOverhead();
            ResetBodyTransform();
        }

        private void SetFrame(StripAnim strip, float index)
        {
            if (_body == null || strip == null || strip.Count == 0) return;
            var sprite = strip.FrameAt(index / strip.Count);
            if (sprite == null) return;
            _body.sprite = sprite;
            _flash.sprite = sprite;
        }

        /// <summary>切帧播放一段动作(整段用 seconds 秒(1x),而非帧条自身 fps,以对齐拍时长;按倍率缩放)。</summary>
        private void PlayStrip(StripAnim strip, float seconds, Action<int> onFrame, Action onDone)
        {
            if (strip == null || strip.Count == 0) { onDone?.Invoke(); return; }
            _mirrored = strip.Mirrored;
            int lastFrame = -1;
            GTween.To(0f, strip.Count, BattleTempo.Scale(seconds)).SetEase(EaseType.Linear).SetIgnoreEngineTimeScale(true).SetTarget(this)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (_body == null) return;
                    int frame = Mathf.Clamp(Mathf.FloorToInt(t.value.x), 0, strip.Count - 1);
                    if (frame == lastFrame) return;
                    lastFrame = frame;
                    _body.sprite = strip.Frames[frame];
                    _flash.sprite = strip.Frames[frame];
                    _bodyRect.localScale = new UnityEngine.Vector3(_mirrored ? -1f : 1f, 1f, 1f);
                    onFrame?.Invoke(frame);
                }))
                .OnComplete((GTweenCallback)(() =>
                {
                    if (_body != null && _idleSprite != null)
                    {
                        _body.sprite = _idleSprite;
                        _flash.sprite = _idleSprite;
                    }
                    onDone?.Invoke();
                }));
        }

        private void ResetBodyTransform()
        {
            if (_bodyRect == null) return;
            _bodyRect.localScale = new UnityEngine.Vector3(_mirrored ? -1f : 1f, 1f, 1f);
            _bodyRect.localRotation = Quaternion.identity;
            _bodyRect.anchoredPosition = new Vector2(RootWidth * 0.5f, _bodyBaseY);
            if (_root != null) _root.anchoredPosition = new Vector2(FootPosition.x, -FootPosition.y);
        }

        private void BeginAction()
        {
            KillActionTweens();
            GTween.Kill(_idleToken);
            ResetBodyTransform();
        }

        private void EndAction()
        {
            ResetBodyTransform();
            if (!IsDead && !Fled) PlayIdle();
        }

        private void KillActionTweens() => GTween.Kill(this);

        private void ReviveVisual()
        {
            if (_body != null) _body.color = Color.white;
            ResetBodyTransform();
        }

        // ── 内部:闪白 / 残影 ───────────────────────────────

        private void FlashWhite(float seconds, float peak) => FlashTint(Color.white, seconds, peak);

        private void FlashTint(Color color, float seconds, float peak)
        {
            if (_flash == null) return;
            var target = _flash;
            bool additive = _flash.material != null && _flash.material.shader != null
                            && _flash.material.shader.name == "Battle/UiAdditive";
            GTween.Kill(target);
            // 无加色材质时:白色覆盖层只会显示原图,改用米白高亮 + 降低峰值
            float cap = additive ? peak : peak * 0.6f;
            var tint = additive ? color : Color.Lerp(color, new Color(1f, 0.97f, 0.85f, 1f), 0.5f);
            // "闪白一帧":立刻打到峰值(暴击顿帧时定格在这一帧),随后线性衰减
            target.color = new Color(tint.r, tint.g, tint.b, cap);
            GTween.To(0f, 1f, BattleTempo.Scale(seconds)).SetEase(EaseType.Linear).SetIgnoreEngineTimeScale(true).SetTarget(target)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (target == null) { GTween.Kill(target); return; }
                    float p = t.value.x;
                    float a = p < 0.15f ? 1f : 1f - (p - 0.15f) / 0.85f;
                    target.color = new Color(tint.r, tint.g, tint.b, a * cap);
                }))
                .OnComplete((GTweenCallback)(() => { if (target != null) target.color = new Color(1f, 1f, 1f, 0f); }));
        }

        private void SpawnAfterimage()
        {
            if (_parent == null || _root == null || _body == null || _body.sprite == null) return;
            var pos = _root.anchoredPosition;
            float w = _bodyRect.sizeDelta.x * Scale, h = _bodyRect.sizeDelta.y * Scale;
            if (Ghosts != null)
            {
                // 池化残影:脚底点 = 根 anchoredPosition 再加本体底边偏移(_bodyBaseY 相对 GroundY)
                var foot = new Vector2(pos.x, pos.y + (_bodyBaseY + GroundY) * Scale);
                Ghosts.Spawn(_body.sprite, foot, new Vector2(w, h), _mirrored, Mathf.Max(0, _root.GetSiblingIndex()));
                return;
            }
            var ghost = QdaoUguiFactory.CreateImage("Afterimage", _parent, pos.x - w * 0.5f, -pos.y - h, w, h, _body.sprite);
            ghost.preserveAspect = true;
            ghost.color = new Color(0.8f, 0.9f, 1f, 0.45f);
            if (_mirrored) ghost.rectTransform.localScale = new UnityEngine.Vector3(-1f, 1f, 1f);
            ghost.transform.SetSiblingIndex(Mathf.Max(0, _root.GetSiblingIndex()));
            var go = ghost.gameObject;
            GTween.To(0.45f, 0f, 0.25f).SetEase(EaseType.QuadOut).SetIgnoreEngineTimeScale(true).SetTarget(go)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (go == null) { GTween.Kill(go); return; }
                    ghost.color = new Color(0.8f, 0.9f, 1f, t.value.x);
                }))
                .OnComplete((GTweenCallback)(() => { if (go != null) UnityEngine.Object.Destroy(go); }));
        }

        // ── 内部:条 / buff / 徽标 ──────────────────────────

        private void SetBarsImmediate()
        {
            GTween.Kill(_hpFillRect);
            GTween.Kill(_hpGhostRect);
            GTween.Kill(_mpFillRect);
            float hp = Ratio(_health, _maxHealth);
            SetWidth(_hpFillRect, (BarWidth - 2f) * hp);
            SetWidth(_hpGhostRect, (BarWidth - 2f) * hp);
            SetWidth(_mpFillRect, (BarWidth - 2f) * Ratio(_mana, _maxMana));
            if (_hpFill != null) _hpFill.color = hp < 0.3f ? BattleUiStyle.DamageText : BattleUiStyle.HpFill;
        }

        private void RebuildBuffIcons(BattleActorState state)
        {
            foreach (var go in _buffIcons)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            _buffIcons.Clear();
            if (_buffRow == null || state.Buffs == null) return;

            int index = 0;
            foreach (var buff in state.Buffs)
            {
                if (index >= MaxBuffIcons) break;
                float x = index * (BuffIconSize + 3f);
                var sprite = BattleArtCatalog.LoadBuffIcon(buff.BuffTableId);
                GameObject go;
                if (sprite != null)
                {
                    var icon = QdaoUguiFactory.CreateImage($"Buff_{buff.BuffTableId}", _buffRow, x, 0f, BuffIconSize, BuffIconSize, sprite);
                    icon.preserveAspect = true;
                    go = icon.gameObject;
                }
                else
                {
                    // 字母块:深底 + buff id
                    var plate = BattleUiWidgets.CreatePanel($"Buff_{buff.BuffTableId}", _buffRow, x, 0f, BuffIconSize, BuffIconSize,
                        new Color(0.16f, 0.24f, 0.42f, 0.95f), false);
                    QdaoUguiFactory.CreateText("T", plate.transform, 0f, 0f, BuffIconSize, BuffIconSize,
                        buff.BuffTableId.ToString(), 12f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
                    go = plate.gameObject;
                }
                if (buff.Layer > 1)
                {
                    QdaoUguiFactory.CreateText("Layer", go.transform, BuffIconSize - 14f, BuffIconSize - 14f, 14f, 14f,
                        buff.Layer.ToString(), 11f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
                }
                _buffIcons.Add(go);
                index++;
            }
        }

        private void ShowBadge(string text, Color color)
        {
            if (_badgePlate == null) return;
            _badgePlate.gameObject.SetActive(true);
            _badgeText.text = text;
            _badgeText.color = color;
        }

        private void HideBadge()
        {
            if (_badgePlate != null) _badgePlate.gameObject.SetActive(false);
        }

        private Color NameColor(bool isMonster)
        {
            if (IsSelf) return QdaoUguiTheme.Html("#8FE3FF");
            if (TeamIsMine) return QdaoUguiTheme.Html("#6FB4FF");
            return isMonster ? QdaoUguiTheme.Html("#A9D6FF") : QdaoUguiTheme.Html("#FF9A6F");
        }

        // ── 内部:tween 小工具 ──────────────────────────────

        /// <summary>动作 tween(seconds 为 1x 秒数,按 BattleTempo 倍率缩放,与拍时长同步)。</summary>
        private GTweener Tween(float from, float to, float seconds, EaseType ease, GTweenCallback1 onUpdate, GTweenCallback onComplete = null)
        {
            var tween = GTween.To(from, to, BattleTempo.Scale(seconds)).SetEase(ease).SetIgnoreEngineTimeScale(true).SetTarget(this).OnUpdate(onUpdate);
            if (onComplete != null) tween.OnComplete(onComplete);
            return tween;
        }

        /// <summary>动作延时(seconds 为 1x 秒数,按 BattleTempo 倍率缩放)。</summary>
        private void Delay(float seconds, Action action)
        {
            if (action == null) return;
            GTween.DelayedCall(BattleTempo.Scale(seconds)).SetIgnoreEngineTimeScale(true).SetTarget(this)
                .OnComplete((GTweenCallback)(() => { if (!_destroyed) action(); }));
        }

        private static void SetWidth(RectTransform rect, float width)
        {
            if (rect == null) return;
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), rect.sizeDelta.y);
        }

        private static float Ratio(ulong current, ulong max)
            => max == 0 ? 0f : Mathf.Clamp01((float)((double)current / max));
    }
}
