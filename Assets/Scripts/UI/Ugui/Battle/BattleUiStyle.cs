using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 回合战斗 UI 的视觉与节奏常量。
    /// 色彩基调沿用 <see cref="QdaoUguiTheme"/>(棕色系 + 米黄文字),
    /// 战斗语义色(伤害红/治疗绿等)在此集中定义,方便后续接美术时统一替换。
    /// </summary>
    public static class BattleUiStyle
    {
        private const string ArenaBackgroundResourcePath =
            "UI/Ugui/Battle/Backgrounds/qdao_battle_arena_cloud_terrace_2560x1080_v1";
        private static Sprite _arenaBackground;

        // ── 可配的战斗配置常量(表数据接入前先用常量,见任务 open_issues) ──

        /// <summary>PVE 单人匹配的 battle_config_id(对应 DungeonTable id)。</summary>
        public const uint PveSoloBattleConfigId = 1;

        /// <summary>PVP 1v1 匹配的 battle_config_id(0 = 服务器默认规则)。</summary>
        public const uint Pvp1V1BattleConfigId = 0;

        /// <summary>PVE 组队(5人)匹配的 battle_config_id(对应 DungeonTable id;人数由服务端按 min(配置,5) 收口)。</summary>
        public const uint PveTeamBattleConfigId = 1;

        /// <summary>PVP 5v5 匹配的 battle_config_id(0 = 服务器默认规则)。</summary>
        public const uint Pvp5V5BattleConfigId = 0;

        // ── 回合播放节奏 ──

        /// <summary>事件流逐条播放的间隔秒数。</summary>
        public const float EventStepSeconds = 0.55f;

        /// <summary>普通飘字存活时长(秒)。</summary>
        public const float FloatTextSeconds = 0.9f;

        /// <summary>暴击飘字存活时长(秒,更大更久)。</summary>
        public const float CritFloatTextSeconds = 1.25f;

        // ── 面板底色 ──

        public static readonly Color PanelBg      = WithAlpha(QdaoUguiTheme.Html("#2B2016"), 0.94f);
        public static readonly Color PanelBgLight = WithAlpha(QdaoUguiTheme.Html("#3D2914"), 0.96f);
        public static readonly Color BattleBg     = WithAlpha(QdaoUguiTheme.Html("#17100A"), 0.97f);
        public static readonly Color BattleBackdropShade = new Color(0.02f, 0.015f, 0.01f, 0.12f);
        public static readonly Color ModalDim     = new Color(0f, 0f, 0f, 0.6f);

        // ── 控件颜色 ──

        public static readonly Color ButtonPlate       = QdaoUguiTheme.Brown;
        public static readonly Color ButtonPlateAccent = QdaoUguiTheme.SelectedRed;
        public static readonly Color ButtonText        = QdaoUguiTheme.Cream;

        // ── 单位槽状态色 ──

        public static readonly Color SlotIdle       = WithAlpha(QdaoUguiTheme.Html("#33261A"), 0.92f);
        public static readonly Color SlotTargetable = WithAlpha(QdaoUguiTheme.MutedBrown, 0.95f);
        public static readonly Color SlotSelected   = WithAlpha(QdaoUguiTheme.SelectedRed, 0.95f);

        public static readonly Color HpFill = QdaoUguiTheme.Html("#3F9B4E");
        public static readonly Color MpFill = QdaoUguiTheme.Html("#3B6FA8");
        public static readonly Color BarBg  = new Color(0f, 0f, 0f, 0.55f);

        // ── 战斗语义色(飘字/闪光) ──

        public static readonly Color DamageText  = QdaoUguiTheme.Html("#FF5540"); // 伤害红
        public static readonly Color HealText    = QdaoUguiTheme.Html("#52D96A"); // 治疗绿
        public static readonly Color BuffAddText = QdaoUguiTheme.Html("#7FB2FF");
        public static readonly Color BuffCutText = QdaoUguiTheme.Html("#BFBFBF");
        public static readonly Color SkillText   = QdaoUguiTheme.Html("#E8D06B");
        public static readonly Color WarnText    = QdaoUguiTheme.Html("#E5C557");
        public static readonly Color SysText     = QdaoUguiTheme.Cream;

        public static readonly Color DamageFlash = WithAlpha(QdaoUguiTheme.Html("#FF3B28"), 0.9f);
        public static readonly Color HealFlash   = WithAlpha(QdaoUguiTheme.Html("#3F9B4E"), 0.9f);
        public static readonly Color SkillFlash  = WithAlpha(QdaoUguiTheme.Html("#E5C557"), 0.9f);
        public static readonly Color DeathFlash  = new Color(0.05f, 0.02f, 0.02f, 0.95f);

        /// <summary>
        /// The battle art is imported as a full-resolution Texture2D rather
        /// than packed into an atlas. Create and cache one runtime Sprite so
        /// uGUI can display every source pixel without a second asset copy.
        /// </summary>
        public static Sprite ResolveArenaBackground()
        {
            if (_arenaBackground != null) return _arenaBackground;

            var texture = Resources.Load<Texture2D>(ArenaBackgroundResourcePath);
            if (texture == null)
            {
                Debug.LogWarning(
                    $"[BattleUi] Missing arena background: Resources/{ArenaBackgroundResourcePath}.png");
                return null;
            }

            _arenaBackground = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
            _arenaBackground.name = "QdaoBattleArenaBackground";
            return _arenaBackground;
        }

        private static Color WithAlpha(Color color, float alpha)
            => new Color(color.r, color.g, color.b, alpha);
    }
}
