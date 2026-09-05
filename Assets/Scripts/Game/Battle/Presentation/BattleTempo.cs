namespace MmorpgClient.Game.Battle.Presentation
{
    /// <summary>
    /// 战斗演出的全局节拍倍率(纯 C#):拍时长(BattleSequencer)、单位动作/残影延迟(BattleUnitView)、
    /// 多段错拍(BattlePresenter)、特效帧率(BattleFxPlayer)、伤害数字寿命(DamageNumberPool)
    /// 全部按同一倍率缩放,保证自动战斗 1.5x / 预算压缩时动作在本拍内播完,不会被下一拍的
    /// BeginAction/ResetBodyTransform 打断瞬移。
    ///
    /// 由 BattlePresenter.SpeedScale 统一写入;开场/胜利等不受拍约束的表现不走这里。
    /// </summary>
    public static class BattleTempo
    {
        public const float MinSpeed = 0.01f;

        private static float s_speed = 1f;

        /// <summary>当前倍率(≥ MinSpeed)。</summary>
        public static float Speed
        {
            get => s_speed;
            set => s_speed = value > MinSpeed ? value : MinSpeed;
        }

        /// <summary>把"1x 下的秒数"换算成当前倍率下的秒数。</summary>
        public static float Scale(float seconds) => seconds / s_speed;

        public static void Reset() => s_speed = 1f;
    }
}
