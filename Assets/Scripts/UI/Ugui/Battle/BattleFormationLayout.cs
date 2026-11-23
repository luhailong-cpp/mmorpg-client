using System;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>脚底坐标，以战斗设计画布的左上角为原点。</summary>
    [CreateAssetMenu(fileName = "BattleFormationLayout", menuName = "MMORPG/Battle Formation Layout")]
    public sealed class BattleFormationLayout : ScriptableObject
    {
        public const string ResourcePath = "Battle/BattleFormationLayout";

        [SerializeField] private Vector2[] enemy = CreateDefaults(false);
        [SerializeField] private Vector2[] ally = CreateDefaults(true);
        [Tooltip("敌方整队纵向平移（设计像素，向下为正），保留各槽位原有的深度缩放。")]
        [SerializeField] private float enemyVerticalOffsetPixels;

        public Vector2 GetPosition(bool teamIsMine, int slot) => GetPlacement(teamIsMine, slot).position;

        internal (Vector2 position, float depthYPixels) GetPlacement(bool teamIsMine, int slot)
        {
            ValidateSlot(slot);
            var positions = teamIsMine ? ally : enemy;
            var offset = teamIsMine ? Vector2.zero : new Vector2(0f, enemyVerticalOffsetPixels);
            if (positions != null && slot < positions.Length)
            {
                var position = positions[slot] + offset;
                if (IsInsideCanvas(position)) return (position, positions[slot].y);
            }
            var fallback = GetDefaultPosition(teamIsMine, slot);
            return (IsInsideCanvas(fallback + offset) ? fallback + offset : fallback, fallback.y);
        }

        public void SetPosition(bool teamIsMine, int slot, Vector2 position)
        {
            ValidateSlot(slot);
            if (!IsInsideCanvas(position))
                throw new ArgumentOutOfRangeException(nameof(position), "站位必须位于战斗设计画布内。");

            ref Vector2[] positions = ref (teamIsMine ? ref ally : ref enemy);
            var offset = teamIsMine ? Vector2.zero : new Vector2(0f, enemyVerticalOffsetPixels);
            if (positions == null || positions.Length != BattleStage.SlotsPerTeam)
            {
                var repaired = new Vector2[BattleStage.SlotsPerTeam];
                for (int i = 0; i < repaired.Length; i++) repaired[i] = GetPosition(teamIsMine, i) - offset;
                positions = repaired;
            }
            positions[slot] = position - offset;
        }

        public void ResetToDefaults()
        {
            enemyVerticalOffsetPixels = 0f;
            enemy = CreateDefaults(false);
            ally = CreateDefaults(true);
        }

        internal static Vector2 GetDefaultPosition(bool teamIsMine, int slot)
        {
            bool back = slot >= BattleStage.FrontRowCount;
            Vector2 start = teamIsMine
                ? (back ? new Vector2(1555f, BattleStage.DepthScaleReferenceY) : new Vector2(1475f, 820f))
                : (back ? new Vector2(435f, 451f) : new Vector2(495f, 519f));
            return start + (slot % BattleStage.FrontRowCount) * new Vector2(90f, -17f);
        }

        private static Vector2[] CreateDefaults(bool teamIsMine)
        {
            var positions = new Vector2[BattleStage.SlotsPerTeam];
            for (int slot = 0; slot < positions.Length; slot++)
                positions[slot] = GetDefaultPosition(teamIsMine, slot);
            return positions;
        }

        private static bool IsInsideCanvas(Vector2 position)
            => position.x >= 0f && position.x <= QdaoUguiTheme.DesignWidth
                && position.y >= 0f && position.y <= QdaoUguiTheme.DesignHeight;

        private static void ValidateSlot(int slot)
        {
            if (slot < 0 || slot >= BattleStage.SlotsPerTeam)
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }
}
