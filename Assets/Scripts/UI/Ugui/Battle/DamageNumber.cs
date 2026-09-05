using System;
using System.Collections.Generic;
using FairyGUI;
using TMPro;
using UnityEngine;
using MmorpgClient.Game.Battle.Presentation;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 伤害数字的纯逻辑部分(文案 / 尺寸 / 多目标错位避让),EditMode 可测。
    /// </summary>
    public static class DamageNumberLayout
    {
        /// <summary>弹出:1.4 → 1.0,0.12s。</summary>
        public const float PopScaleFrom = 1.4f;
        public const float PopSeconds = 0.12f;
        /// <summary>上飘 60px,整段约 1.2s 后淡出完毕。</summary>
        public const float RiseDistance = 60f;
        public const float LifeSeconds = 1.2f;
        /// <summary>淡出从整段的 55% 开始。</summary>
        public const float FadeStart = 0.55f;
        /// <summary>暴击数字放大 1.3。</summary>
        public const float CritScale = 1.3f;
        /// <summary>
        /// 48×64 字格 → 格高 61px、字形高约 46px、描边约 3px(spec §1:字高 36-40、厚描边;
        /// 2026-09-04 帧验收 0.62 时字只有 ~30px 且描边不到 2px,看着像细描边普通字)。
        /// </summary>
        public const float BaseGlyphScale = 0.95f;
        /// <summary>字格重叠排布(描边格有留白)。</summary>
        public const float GlyphAdvance = 0.84f;

        /// <summary>错位避让:同一时间窗内、水平距离内已有数字时的位移。</summary>
        public const float AvoidWindowSeconds = 0.7f;
        public const float AvoidRadiusX = 150f;
        public const float AvoidStepX = 46f;
        public const float AvoidStepY = 30f;

        /// <summary>文案:伤害 "-N"、治疗 "+N"、闪避 "闪"、暴击 "暴击-N"(字集含 暴/击)。</summary>
        public static string FormatText(long value, NumberKind kind)
        {
            switch (kind)
            {
                case NumberKind.Miss: return "闪";
                case NumberKind.Heal: return "+" + Math.Abs(value);
                case NumberKind.Crit: return "暴击-" + Math.Abs(value);
                default: return "-" + Math.Abs(value);
            }
        }

        public static float GlyphScale(NumberKind kind)
            => kind == NumberKind.Crit ? BaseGlyphScale * CritScale : BaseGlyphScale;

        /// <summary>普通伤害的字色(digits_normal 字集是"白字黑边",按 Image.color 乘成红字黑边)。</summary>
        public static readonly Color DamageTint = new Color(1f, 0.2f, 0.16f, 1f);

        /// <summary>
        /// 字格着色:普通伤害红(spec §1:红色粗体、深色厚描边),暴击/治疗/闪避字集本身已是金/绿/灰,不再乘色。
        /// 字集是白字黑边时乘色只染字面,描边保持深色。
        /// </summary>
        public static Color GlyphTint(NumberKind kind)
            => kind == NumberKind.Normal ? DamageTint : Color.white;

        /// <summary>
        /// 群攻多目标同拍飙血时,第 targetIndex 个目标的数字水平错位(±20、±40…交替),
        /// 让相邻目标的数字不重叠也不排成一条直线。
        /// </summary>
        public static float GroupOffsetX(int targetIndex)
        {
            if (targetIndex <= 0) return 0f;
            int lane = (targetIndex + 1) / 2;
            return (targetIndex % 2 == 1 ? 1f : -1f) * lane * 20f;
        }

        /// <summary>
        /// 第 collisions 个重叠数字的偏移:左右交替错开、逐个抬高,避免群攻多目标数字叠在一起。
        /// 0 → (0,0);1 → (+46,-30);2 → (-46,-60);3 → (+92,-90)…(y 向下为正,负为抬高)
        /// </summary>
        public static Vector2 AvoidOffset(int collisions)
        {
            if (collisions <= 0) return Vector2.zero;
            int lane = (collisions + 1) / 2;
            float sign = collisions % 2 == 1 ? 1f : -1f;
            return new Vector2(sign * lane * AvoidStepX, -collisions * AvoidStepY);
        }

        /// <summary>统计 recent 中与 x 在避让半径内、且仍在时间窗内的条目数。</summary>
        public static int CountCollisions(IReadOnlyList<(float x, float time)> recent, float x, float now)
        {
            int n = 0;
            if (recent == null) return 0;
            for (int i = 0; i < recent.Count; i++)
            {
                if (now - recent[i].time > AvoidWindowSeconds) continue;
                if (Mathf.Abs(recent[i].x - x) <= AvoidRadiusX) n++;
            }
            return n;
        }
    }

    /// <summary>
    /// 伤害数字对象池(turn-battle-presentation.md §3 DamageNumber):
    ///  - 用 Battle/UI/digits_{normal,crit,heal,miss} 字集拼数字;字集缺失退回 TMP 文本;
    ///  - 弹出缩放(1.4→1.0,0.12s)+ 上飘 60px + 淡出,约 1.2s;暴击放大 1.3;
    ///  - 群攻多目标错位避让(DamageNumberLayout.AvoidOffset);
    ///  - Clear() 立刻回收全部(观战抢占/断线)。
    /// </summary>
    public sealed class DamageNumberPool
    {
        private sealed class Entry
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public readonly List<Image> Glyphs = new List<Image>();
            public TMP_Text Fallback;
        }

        private const int MaxGlyphs = 14;

        private readonly RectTransform _layer;
        private readonly Stack<Entry> _pool = new Stack<Entry>();
        private readonly List<Entry> _active = new List<Entry>();
        private readonly List<(float x, float time)> _recent = new List<(float, float)>();

        public int ActiveCount => _active.Count;

        public DamageNumberPool(RectTransform layer)
        {
            _layer = layer;
        }

        /// <summary>
        /// 在 head(设计坐标,单位头顶点)上方弹出一个数字;delay 用于多段/多目标错拍。
        /// delay / 弹出 / 寿命全部按 BattleTempo 倍率缩放,与拍时长同步。
        /// </summary>
        public void Show(Vector2 head, long value, NumberKind kind, float delay = 0f)
        {
            if (_layer == null) return;
            delay = delay > 0f ? BattleTempo.Scale(delay) : 0f;
            float popSeconds = BattleTempo.Scale(DamageNumberLayout.PopSeconds);
            float lifeSeconds = BattleTempo.Scale(DamageNumberLayout.LifeSeconds);
            float now = Time.realtimeSinceStartup + delay;
            PruneRecent(now);
            int collisions = DamageNumberLayout.CountCollisions(_recent, head.x, now);
            _recent.Add((head.x, now));
            var offset = DamageNumberLayout.AvoidOffset(collisions);

            string text = DamageNumberLayout.FormatText(value, kind);
            var entry = Rent();
            var root = entry.Root;
            root.name = $"Number_{text}";
            entry.Group.alpha = 0f;

            float glyphScale = DamageNumberLayout.GlyphScale(kind);
            var font = BattleArtCatalog.LoadDigits(VariantOf(kind));
            float width = 0f, height = 64f * glyphScale;
            if (font != null && LayoutGlyphs(entry, font, text, glyphScale, DamageNumberLayout.GlyphTint(kind), out width))
            {
                entry.Fallback.gameObject.SetActive(false);
                height = font.CellHeight * glyphScale;
            }
            else
            {
                HideGlyphs(entry);
                entry.Fallback.gameObject.SetActive(true);
                entry.Fallback.text = text;
                entry.Fallback.fontSize = kind == NumberKind.Crit ? 48f * DamageNumberLayout.CritScale : 44f;
                entry.Fallback.color = kind == NumberKind.Heal ? BattleUiStyle.HealText
                    : kind == NumberKind.Miss ? BattleUiStyle.BuffCutText : DamageNumberLayout.DamageTint;
                width = 400f;
                height = 80f;
            }

            root.sizeDelta = new Vector2(Mathf.Max(width, 10f), height);
            // 容器 pivot 在中心:head 已是 HP 条上沿,再抬 12px + 半个字高(从条上方弹出);
            // 起点不进顶部 HUD 带(敌方后排的数字会顶到行动预告条)
            var center = new Vector2(head.x + offset.x, head.y - 12f - height * 0.5f + offset.y);
            center.y = Mathf.Max(center.y, BattleStage.HudTopBand + height * 0.5f);
            var startPos = new Vector2(center.x, -center.y);
            root.anchoredPosition = startPos;
            root.localScale = Vector3.one * DamageNumberLayout.PopScaleFrom;
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
            _active.Add(entry);

            var go = root.gameObject;
            GTween.To(DamageNumberLayout.PopScaleFrom, 1f, popSeconds)
                .SetDelay(delay).SetEase(EaseType.BackOut).SetIgnoreEngineTimeScale(true).SetTarget(go)
                .OnStart((GTweenCallback)(() => { if (entry.Group != null) entry.Group.alpha = 1f; }))
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (go == null) { GTween.Kill(go); return; }
                    root.localScale = new Vector3(t.value.x, t.value.x, 1f);
                }));
            GTween.To(0f, 1f, lifeSeconds)
                .SetDelay(delay).SetEase(EaseType.QuadOut).SetIgnoreEngineTimeScale(true).SetTarget(go)
                .OnUpdate((GTweenCallback1)(t =>
                {
                    if (go == null) { GTween.Kill(go); return; }
                    float p = t.value.x;
                    root.anchoredPosition = startPos + new Vector2(0f, DamageNumberLayout.RiseDistance * p);
                    if (entry.Group != null)
                    {
                        float fs = DamageNumberLayout.FadeStart;
                        entry.Group.alpha = p < fs ? 1f : 1f - (p - fs) / (1f - fs);
                    }
                }))
                .OnComplete((GTweenCallback)(() => Recycle(entry)));
        }

        /// <summary>立刻回收全部数字。</summary>
        public void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var entry = _active[i];
                if (entry?.Root != null) GTween.Kill(entry.Root.gameObject);
                ReturnToPool(entry);
            }
            _active.Clear();
            _recent.Clear();
        }

        public void Dispose()
        {
            Clear();
            while (_pool.Count > 0)
            {
                var entry = _pool.Pop();
                if (entry?.Root != null) UnityEngine.Object.Destroy(entry.Root.gameObject);
            }
        }

        // ── 内部 ─────────────────────────────────────────────

        private static string VariantOf(NumberKind kind)
        {
            switch (kind)
            {
                case NumberKind.Crit: return "crit";
                case NumberKind.Heal: return "heal";
                case NumberKind.Miss: return "miss";
                default: return "normal";
            }
        }

        private bool LayoutGlyphs(Entry entry, DigitFont font, string text, float scale, Color tint, out float width)
        {
            width = 0f;
            float cellW = font.CellWidth * scale, cellH = font.CellHeight * scale;
            float advance = cellW * DamageNumberLayout.GlyphAdvance;
            int used = 0;
            foreach (char c in text)
            {
                if (used >= MaxGlyphs) break;
                var sprite = font.Lookup(c);
                if (sprite == null) continue;
                var glyph = GetGlyph(entry, used);
                glyph.sprite = sprite;
                glyph.color = tint;
                glyph.rectTransform.sizeDelta = new Vector2(cellW, cellH);
                glyph.rectTransform.anchoredPosition = new Vector2(used * advance, 0f);
                glyph.gameObject.SetActive(true);
                used++;
            }
            for (int i = used; i < entry.Glyphs.Count; i++) entry.Glyphs[i].gameObject.SetActive(false);
            if (used == 0) return false;
            // 容器宽 = 字串宽、pivot 在中心:字格从容器左上角起顺排(anchor/pivot 均为左上)即自然居中
            width = (used - 1) * advance + cellW;
            return true;
        }

        private static void HideGlyphs(Entry entry)
        {
            foreach (var glyph in entry.Glyphs) glyph.gameObject.SetActive(false);
        }

        private Image GetGlyph(Entry entry, int index)
        {
            while (entry.Glyphs.Count <= index)
            {
                var glyph = QdaoUguiFactory.CreateImage($"G{entry.Glyphs.Count}", entry.Root, 0f, 0f, 48f, 64f, null);
                glyph.preserveAspect = true;
                glyph.raycastTarget = false;
                entry.Glyphs.Add(glyph);
            }
            return entry.Glyphs[index];
        }

        private Entry Rent()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled?.Root != null) return pooled;
            }
            var entry = new Entry();
            entry.Root = QdaoUguiFactory.CreateRect("Number", _layer, 0f, 0f, 200f, 64f);
            entry.Root.pivot = new Vector2(0.5f, 0.5f);
            entry.Group = entry.Root.gameObject.AddComponent<CanvasGroup>();
            entry.Group.blocksRaycasts = false;
            entry.Group.interactable = false;
            // 退回文本时容器固定 400×80,文本铺满容器
            entry.Fallback = QdaoUguiFactory.CreateText("Text", entry.Root, 0f, 0f, 400f, 80f, string.Empty, 40f,
                BattleUiStyle.DamageText, TextAlignmentOptions.Center);
            entry.Fallback.fontStyle = FontStyles.Bold;
            BattleUiWidgets.ApplyOutline(entry.Fallback, 0.3f, new Color32(0, 0, 0, 240));
            entry.Fallback.gameObject.SetActive(false);
            return entry;
        }

        private void Recycle(Entry entry)
        {
            _active.Remove(entry);
            ReturnToPool(entry);
        }

        private void ReturnToPool(Entry entry)
        {
            if (entry?.Root == null) return;
            entry.Root.localScale = Vector3.one;
            entry.Root.gameObject.SetActive(false);
            _pool.Push(entry);
        }

        private void PruneRecent(float now)
        {
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                if (now - _recent[i].time > DamageNumberLayout.AvoidWindowSeconds) _recent.RemoveAt(i);
            }
        }
    }
}
