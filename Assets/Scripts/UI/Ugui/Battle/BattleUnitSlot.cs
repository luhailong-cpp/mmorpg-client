using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>单位槽高亮态。</summary>
    public enum SlotHighlight
    {
        None,       // 常态
        Targetable, // 目标选择模式下可选
        Selected,   // 已被选为行动目标
    }

    /// <summary>
    /// 战斗单位槽:名字/等级/HP/MP 条/防御与死亡标记/buff 文字缩写,
    /// 并承担回合播放的表现(闪烁/抖动/飘字)。占位质量,纯代码构建。
    /// </summary>
    public sealed class BattleUnitSlot
    {
        public const float Width = 380f;
        public const float Height = 250f;

        public ulong ActorId { get; private set; }
        public uint TeamIndex { get; private set; }
        public bool IsDead { get; private set; }
        public bool Fled { get; private set; }

        private readonly BattleUiRoot _owner;
        private readonly bool _isSelf;
        private readonly RectTransform _rect;
        private readonly Image _bg;
        private readonly Button _button;
        private readonly CanvasGroup _group;
        private readonly TMP_Text _title;
        private readonly UiBar _hpBar;
        private readonly UiBar _mpBar;
        private readonly TMP_Text _stateText;
        private readonly TMP_Text _buffText;
        private readonly Vector2 _basePos;
        private readonly float _designX;
        private readonly float _designY;

        private ulong _maxHealth;
        private ulong _maxMana;
        private SlotHighlight _highlight = SlotHighlight.None;
        private Coroutine _shakeCo;
        private Coroutine _flashCo;

        public BattleUnitSlot(BattleUiRoot owner, UnityEngine.Transform parent, float x, float y,
                              ulong actorId, bool isSelf, System.Action<ulong> onClicked)
        {
            _owner = owner;
            _isSelf = isSelf;
            ActorId = actorId;
            _designX = x;
            _designY = y;

            _bg = BattleUiWidgets.CreatePanel($"UnitSlot_{actorId}", parent, x, y, Width, Height, BattleUiStyle.SlotIdle);
            _rect = (RectTransform)_bg.transform;
            _basePos = _rect.anchoredPosition;
            _group = _bg.gameObject.AddComponent<CanvasGroup>();
            _button = _bg.gameObject.AddComponent<Button>();
            BattleUiWidgets.ConfigureButtonVisual(_button, _bg);
            _button.onClick.AddListener(() => onClicked?.Invoke(ActorId));

            _title = QdaoUguiFactory.CreateText("Title", _rect, 14f, 8f, Width - 28f, 34f,
                string.Empty, 24f, QdaoUguiTheme.Cream);
            _hpBar = BattleUiWidgets.CreateBar("Hp", _rect, 14f, 50f, Width - 28f, 30f, BattleUiStyle.HpFill);
            _mpBar = BattleUiWidgets.CreateBar("Mp", _rect, 14f, 86f, Width - 28f, 24f, BattleUiStyle.MpFill);
            _stateText = QdaoUguiFactory.CreateText("State", _rect, 14f, 118f, Width - 28f, 30f,
                string.Empty, 20f, BattleUiStyle.WarnText);
            _buffText = BattleUiWidgets.CreateWrappedText("Buffs", _rect, 14f, 152f, Width - 28f, Height - 164f,
                string.Empty, 17f, QdaoUguiTheme.StatusCream);
        }

        /// <summary>按权威状态全量刷新本槽。</summary>
        public void Apply(BattleActorState state)
        {
            if (state == null || _bg == null) return;
            ActorId = state.ActorId;
            TeamIndex = state.TeamIndex;
            IsDead = state.IsDead;
            Fled = state.Fled;
            _maxHealth = state.MaxHealth;
            _maxMana = state.MaxMana;

            bool isMonster = state.ActorType == eBattleActorType.BattleActorTypeMonster;
            string name = string.IsNullOrEmpty(state.Name)
                ? (isMonster ? $"怪物{state.MonsterTableId}" : $"玩家{state.ActorId}")
                : state.Name;
            string selfMark = _isSelf ? "★" : string.Empty;
            string kindMark = isMonster ? "[怪]" : "[人]";
            _title.text = $"{selfMark}{kindMark} Lv{state.Level} {name}";

            _hpBar.Set("HP", state.Attributes?.Health ?? 0, _maxHealth);
            _mpBar.Set("MP", state.Attributes?.Mana ?? 0, _maxMana);

            var marks = new StringBuilder();
            if (state.IsDefending) marks.Append("【防】");
            if (state.IsDead) marks.Append("【阵亡】");
            if (state.Fled) marks.Append("【已逃离】");
            _stateText.text = marks.ToString();

            _buffText.text = FormatBuffs(state);
            _group.alpha = (state.IsDead || state.Fled) ? 0.45f : 1f;
            SetHighlight(SlotHighlight.None);
        }

        /// <summary>回合播放期间按事件的 target_health_after 直接刷 HP 条(免推算)。</summary>
        public void SetHealthDuringPlayback(ulong current)
        {
            _hpBar.Set("HP", current, _maxHealth);
        }

        /// <summary>回合播放中的死亡表现(权威状态随后全量刷新兜底)。</summary>
        public void ShowDeadMark()
        {
            IsDead = true;
            if (_stateText != null) _stateText.text = "【阵亡】";
            if (_group != null) _group.alpha = 0.45f;
        }

        public void SetHighlight(SlotHighlight highlight)
        {
            _highlight = highlight;
            if (_bg != null) _bg.color = CurrentBaseColor();
        }

        public void PlayShake()
        {
            Restart(ref _shakeCo, CoShake());
        }

        public void PlayFlash(Color color)
        {
            Restart(ref _flashCo, CoFlash(color));
        }

        /// <summary>在本槽上方生成飘字(父节点用槽的父容器,避免抖动带动飘字)。</summary>
        public void SpawnFloatText(string value, Color color, bool big)
        {
            if (_rect == null) return;
            float x = _designX + (Width - 340f) * 0.5f;
            float y = _designY + 70f;
            BattleUiWidgets.SpawnFloatText(_owner, _rect.parent, x, y, value, color, big);
        }

        public void Destroy()
        {
            if (_owner != null)
            {
                if (_shakeCo != null) _owner.StopCoroutine(_shakeCo);
                if (_flashCo != null) _owner.StopCoroutine(_flashCo);
            }
            _shakeCo = null;
            _flashCo = null;
            if (_bg != null)
                UnityEngine.Object.Destroy(_bg.gameObject);
        }

        private Color CurrentBaseColor()
        {
            switch (_highlight)
            {
                case SlotHighlight.Targetable: return BattleUiStyle.SlotTargetable;
                case SlotHighlight.Selected: return BattleUiStyle.SlotSelected;
                default: return BattleUiStyle.SlotIdle;
            }
        }

        private static string FormatBuffs(BattleActorState state)
        {
            if (state.Buffs == null || state.Buffs.Count == 0) return string.Empty;
            var sb = new StringBuilder("Buff:");
            foreach (var buff in state.Buffs)
            {
                sb.Append(" B").Append(buff.BuffTableId);
                if (buff.Layer > 1) sb.Append('x').Append(buff.Layer);
                sb.Append(buff.RemainRounds > 0 ? $"({buff.RemainRounds}回)" : "(∞)");
            }
            return sb.ToString();
        }

        private void Restart(ref Coroutine handle, IEnumerator routine)
        {
            if (_owner == null || _rect == null) return;
            if (handle != null) _owner.StopCoroutine(handle);
            handle = _owner.StartCoroutine(routine);
        }

        private IEnumerator CoShake()
        {
            const float duration = 0.32f;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < duration)
            {
                if (_rect == null) yield break;
                float falloff = 1f - (Time.realtimeSinceStartup - start) / duration;
                _rect.anchoredPosition = _basePos + new Vector2(
                    UnityEngine.Random.Range(-12f, 12f) * falloff,
                    UnityEngine.Random.Range(-9f, 9f) * falloff);
                yield return null;
            }
            if (_rect != null) _rect.anchoredPosition = _basePos;
            _shakeCo = null;
        }

        private IEnumerator CoFlash(Color color)
        {
            const float duration = 0.3f;
            float start = Time.realtimeSinceStartup;
            while (true)
            {
                if (_bg == null) yield break;
                float t = (Time.realtimeSinceStartup - start) / duration;
                if (t >= 1f) break;
                _bg.color = Color.Lerp(color, CurrentBaseColor(), t);
                yield return null;
            }
            if (_bg != null) _bg.color = CurrentBaseColor();
            _flashCo = null;
        }
    }
}
