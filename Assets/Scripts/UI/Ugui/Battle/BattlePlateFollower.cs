using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 名牌跟随器:挂在 <see cref="BattleUnitView"/> 的名牌根(独立名牌层)上,每帧 LateUpdate 把单位根的
    /// anchoredPosition / localScale / CanvasGroup.alpha 原样复制过来。名牌层与舞台层是同尺寸、同锚点的
    /// 全屏 Rect(BattleScreen.CreateLayer),坐标系一致,所以直接拷 anchoredPosition 即可。
    /// 之所以不把名牌放进单位根:名牌要画在所有立绘之上(相邻单位不能盖住名字/血条)、特效之上(群攻特效
    /// 不能把血条盖没),而单位根按脚底 y 排序、特效层又在舞台层之上,只能分层 + 跟随。
    /// 单位根被销毁时自毁。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattlePlateFollower : MonoBehaviour
    {
        private RectTransform _target;
        private CanvasGroup _targetGroup;
        private CanvasGroup _selfGroup;
        private RectTransform _self;

        public void Bind(RectTransform target, CanvasGroup targetGroup, CanvasGroup selfGroup)
        {
            _target = target;
            _targetGroup = targetGroup;
            _selfGroup = selfGroup;
            _self = transform as RectTransform;
            Sync();
        }

        private void LateUpdate() => Sync();

        private void Sync()
        {
            if (_self == null) return;
            if (_target == null)
            {
                // 单位根已销毁(BattleUnitView.Destroy 会先销毁名牌;这里是兜底)
                Destroy(gameObject);
                return;
            }
            _self.anchoredPosition = _target.anchoredPosition;
            _self.localScale = _target.localScale;
            if (_selfGroup != null && _targetGroup != null) _selfGroup.alpha = _targetGroup.alpha;
        }
    }
}
