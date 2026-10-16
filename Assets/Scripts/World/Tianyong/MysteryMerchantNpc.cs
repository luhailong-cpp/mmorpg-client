using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 神秘商人在主城里的形象(服务端设计:docs/design/mystery-merchant.md §6.3)。
    ///
    /// 服务端没有神秘商人的实体:它只告诉客户端「这一轮商人在哪条线、站在哪个坐标」,形象由客户端自己摆。
    /// 所以这个对象纯粹是显示:没有碰撞体(不挡路、不参与寻路),不向服务端上报任何东西;
    /// 能不能买由服务端按玩家的真实坐标裁决。点击由界面层的一块跟随屏幕位置的透明热区负责
    /// (<c>MysteryMerchantUiRoot</c>),不在这里做物理拾取 —— 主城的点地寻路本来就不用物理射线。
    ///
    /// 形象暂用角色名册里的「灯穗小使」(<see cref="AppearanceId"/>)顶替:主干里还没有专属的商人立绘。
    /// 换成专属美术时只改这一个常量(或改成按资源路径加载),别处不受影响。
    /// </summary>
    public sealed class MysteryMerchantNpc : MonoBehaviour
    {
        public const string ObjectName = "[MysteryMerchantNpc]";
        public const string DisplayName = "神秘商人";
        /// <summary>顶替用的角色形象 id(QdaoCharacterCatalog:23_lantern_courier「灯穗小使」)。</summary>
        public const string AppearanceId = "23_lantern_courier";
        /// <summary>点击热区按这个世界尺寸估算(角色帧高约 9.8 单位,人物本身约占中间一半宽)。</summary>
        public const float HitWorldHeight = 9.8f;
        public const float HitWidthPerHeight = 0.5f;

        /// <summary>脚底点(世界坐标)。</summary>
        public Vector3 FeetPosition => transform.position;

        /// <summary>
        /// 在 <paramref name="parent"/> 下摆出商人。占位几何体只在运行时换成贴图形象
        /// (编辑器模式下的测试保留几何体,与 ActorWorld 对其它角色的处理一致)。
        /// </summary>
        public static MysteryMerchantNpc Create(Transform parent, Vector3 feetPosition)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = ObjectName;
            // 纯显示:去掉图元自带的碰撞体,否则它会挡住角色控制器。
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = feetPosition;

            var label = WorldNameplate.Create(go.transform, DisplayName, WorldNameplate.NpcColor);
            WorldLabelBillboard.Attach(label.gameObject);
            if (Application.isPlaying) QdaoBoySpriteAnimator.TryAttach(go, AppearanceId);
            return go.AddComponent<MysteryMerchantNpc>();
        }

        public void MoveTo(Vector3 feetPosition) => transform.position = feetPosition;

        /// <summary>水平面上的距离(忽略高度),与服务端判定购买距离的口径一致。</summary>
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 玩家走向商人时的落脚点:商人到玩家连线上、距商人 <paramref name="standOff"/> 的那一点
        /// (不让玩家踩到商人身上)。玩家已经比 standOff 更近、或两者重合时返回玩家当前位置(不必再走)。
        /// </summary>
        public static Vector3 ApproachPoint(Vector3 merchantFeet, Vector3 playerFeet, float standOff)
        {
            var offset = new Vector3(playerFeet.x - merchantFeet.x, 0f, playerFeet.z - merchantFeet.z);
            float distance = offset.magnitude;
            if (distance <= standOff || distance < 0.0001f) return playerFeet;
            var point = merchantFeet + offset / distance * standOff;
            point.y = playerFeet.y;
            return point;
        }

        /// <summary>
        /// 购买距离是服务端按它记录的玩家坐标判的,而坐标上报有延迟:客户端走到比它近
        /// <paramref name="margin"/> 的地方才开店,免得「看着到了,一点购买却被告知太远」。
        /// 结果不小于 1。
        /// </summary>
        public static float OpenDistance(uint interactRange, float margin = 2f) => Mathf.Max(1f, interactRange - margin);

        /// <summary>
        /// 走向商人时停在离他多远的地方:通常是 <paramref name="preferred"/>(不踩到他身上),但一定比开店距离
        /// (<see cref="OpenDistance"/>)近半个单位以上 —— 否则策划把购买距离配小之后,玩家会停在开店距离之外,
        /// 走到了店却永远不开。结果不小于 0.5。
        /// </summary>
        public static float StandOff(uint interactRange, float preferred = 3f) =>
            Mathf.Max(0.5f, Mathf.Min(preferred, OpenDistance(interactRange) - 0.5f));

        /// <summary>
        /// 商人在屏幕上的点击范围(屏幕像素,原点左下):脚底中心 + 宽高。商人在相机背后或相机为空时返回 false。
        /// </summary>
        public bool TryGetScreenHitBox(Camera worldCamera, out Vector2 feetScreen, out Vector2 size)
        {
            feetScreen = default;
            size = default;
            if (worldCamera == null) return false;
            var feet = worldCamera.WorldToScreenPoint(FeetPosition);
            // 贴图是始终正对相机的面片,所以「头顶」沿相机的上方向取,而不是世界的上方向
            // (俯视角下世界上方向投到屏幕上会被压短,热区就比看到的人矮一截)。
            var head = worldCamera.WorldToScreenPoint(FeetPosition + worldCamera.transform.up * HitWorldHeight);
            if (feet.z <= 0f || head.z <= 0f) return false;
            float height = Mathf.Abs(head.y - feet.y);
            if (height < 1f) return false;
            feetScreen = new Vector2(feet.x, Mathf.Min(feet.y, head.y));
            size = new Vector2(height * HitWidthPerHeight, height);
            return true;
        }
    }
}
