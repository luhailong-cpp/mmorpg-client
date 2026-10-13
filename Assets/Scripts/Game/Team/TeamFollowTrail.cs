using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.Game.Team
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 队长走过的轨迹:一条从旧到新的折线,末端是队长当前位置(队首)。
    /// 跟随的队员不直奔队长,而是站在"沿这条折线往回数 N 米"的点上,所以队伍会排成一列、
    /// 拐弯时一个接一个地拐,而不是所有人挤成一团抄近路。
    ///
    /// 只做几何,不碰 Unity 运行时(Time / Transform / 输入),可以在编辑器外直接单测。
    /// 距离一律只算水平面(x/z),y 原样带着走。
    /// </summary>
    public sealed class TeamFollowTrail
    {
        // 每个记录点的里程:队长从这条轨迹的起点走到该点累计的弧长,与 _points 一一对应。
        // 用 double:里程只增不减地累加一整个会话,float 走到几万米后分辨率就不够 1 厘米了。
        private readonly List<double> _odometers = new List<double>();

        /// <summary>相邻两个记录点的最小间距。太密浪费、太疏拐弯会被削平。</summary>
        public const float SampleSpacing = 0.5f;
        /// <summary>一次停步回拉最多从末端收回这么长。远端外推冲过头的量不会超过它;再多就不是回拉了。</summary>
        public const float MaxRetractLength = 1.5f;

        // 相交轨迹的投影会有浮点舍入差异;0.1 毫米内视为同样近,优先保留靠近队首的段。
        private const float ProjectionTieTolerance = 0.0001f;

        // 已落定的记录点,从旧到新。队首 _head 不在表里,除非它刚好被落定为最后一个点。
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly float _maxLength;
        private Vector3 _head;
        // 自上一次落定新记录点以来已经从末端收回的弧长。
        private float _retracted;

        /// <param name="maxLength">保留的最大弧长;更老的部分被裁掉。必须容得下整支队伍再加一段追赶余量。</param>
        public TeamFollowTrail(float maxLength)
        {
            _maxLength = Mathf.Max(SampleSpacing * 2f, maxLength);
        }

        /// <summary>是否已经有队首。没有时其它查询都没有意义。</summary>
        public bool HasHead { get; private set; }

        /// <summary>队长当前位置。</summary>
        public Vector3 Head => _head;

        /// <summary>
        /// 每次清空 / 重起(<see cref="Clear"/>、<see cref="Reset"/>)加一。里程只在同一代轨迹内可比,
        /// 持有里程的一方(<see cref="TeamFollowCursor"/>)据此发现自己记的是上一条轨迹。
        /// </summary>
        public int Epoch { get; private set; }

        /// <summary>
        /// 队首的里程:队长沿这条轨迹走到当前位置累计的弧长。停步回拉把末端收回时会相应变小。
        /// 轨迹上一点"距队首的弧长" = 队首里程 - 该点里程。
        /// </summary>
        public double HeadOdometer
        {
            get
            {
                if (!HasHead || _points.Count == 0) return 0d;
                int last = _points.Count - 1;
                return _odometers[last] + Horizontal(_head, _points[last]);
            }
        }

        /// <summary>从队首沿折线到最老记录点的弧长。</summary>
        public float Length
        {
            get
            {
                if (!HasHead || _points.Count == 0) return 0f;
                float length = Horizontal(_head, _points[_points.Count - 1]);
                for (int i = _points.Count - 1; i > 0; i--) length += Horizontal(_points[i], _points[i - 1]);
                return length;
            }
        }

        public void Clear()
        {
            _points.Clear();
            _odometers.Clear();
            Epoch++;
            HasHead = false;
            _head = default;
            _retracted = 0f;
        }

        /// <summary>
        /// 以 head 为队首重新起一条轨迹。tailBehindHead 是"队长身后"的若干点,[0] 离队长最近、
        /// 越往后越远;刚看到队长时用它让队员直接排到队长背后,而不是全部叠在队长脚下等他走出去。
        /// 可以为 null 或空(此时所有槽位都落在队长脚下,队长一走就自然拉开)。
        /// </summary>
        public void Reset(Vector3 head, IReadOnlyList<Vector3> tailBehindHead)
        {
            _points.Clear();
            _odometers.Clear();
            Epoch++;
            if (tailBehindHead != null)
            {
                for (int i = tailBehindHead.Count - 1; i >= 0; i--) AppendPoint(tailBehindHead[i]);
            }
            AppendPoint(head);
            _head = head;
            HasHead = true;
            _retracted = 0f;
            Trim();
        }

        /// <summary>
        /// 报告队长的最新位置(每帧一次)。
        /// </summary>
        /// <param name="head">队长当前位置。</param>
        /// <param name="allowRetract">
        /// 队首往回退时是否把退过的那一小段从末端收掉。只应在队长**站定**时传 true:远端角色靠外推显示,
        /// 停步时可能先冲过头再被拉回停点,不收的话末端会留下一个"向前再折返"的小尖角,队员跟着走过去
        /// 再走回来。队长带着速度往回走是真的掉头,必须传 false —— 那时轨迹要折成发夹留着:收掉的话
        /// 槽位会一直落在"队首再往前"的方向上,队员被赶着跑在队长前面。
        /// 即使传 true,自上一次落定新点以来最多也只收回 <see cref="MaxRetractLength"/>。
        /// </param>
        public void Push(Vector3 head, bool allowRetract = false)
        {
            if (!HasHead)
            {
                Reset(head, null);
                return;
            }

            _head = head;
            if (allowRetract)
            {
                // 新位置比最后一个记录点更靠近倒数第二个点 = 队首退过了最后一个点,它已经不在路上了。
                // 用严格小于:队长站着不动时队首与最后一个点重合,不能每帧把它收掉再加回来。
                while (_points.Count >= 2 &&
                       Horizontal(head, _points[_points.Count - 2]) <
                       Horizontal(_points[_points.Count - 1], _points[_points.Count - 2]))
                {
                    float segment = Horizontal(_points[_points.Count - 1], _points[_points.Count - 2]);
                    if (_retracted + segment > MaxRetractLength) break;
                    _retracted += segment;
                    _points.RemoveAt(_points.Count - 1);
                    _odometers.RemoveAt(_odometers.Count - 1);
                }
            }

            if (Horizontal(head, _points[_points.Count - 1]) >= SampleSpacing)
            {
                AppendPoint(head);
                _retracted = 0f;
                Trim();
            }
        }

        /// <summary>
        /// 从队首沿轨迹往回 distance 米处的点。distance 小于等于 0 返回队首;
        /// 超过轨迹总长返回最老的记录点(队伍比轨迹长时后排的人会叠在那里,直到队长走出足够远)。
        /// </summary>
        public Vector3 PointBehindHead(float distance)
        {
            if (!HasHead) return default;
            if (distance <= 0f || _points.Count == 0) return _head;

            Vector3 from = _head;
            float remaining = distance;
            for (int i = _points.Count - 1; i >= 0; i--)
            {
                Vector3 to = _points[i];
                float segment = Horizontal(from, to);
                if (segment >= remaining && segment > 0f) return Vector3.Lerp(from, to, remaining / segment);
                remaining -= segment;
                from = to;
            }
            return _points[0];
        }

        /// <summary>
        /// 把 position 投影到轨迹上:返回最近点距队首的弧长,lateral 是 position 到那一点的水平距离
        /// (即"偏离轨迹多远")。轨迹自交时取离队首更近的那一处,等于允许队员抄掉队长绕的圈。
        /// </summary>
        public float DistanceBehindHead(Vector3 position, out float lateral)
        {
            lateral = float.PositiveInfinity;
            if (!HasHead) return 0f;

            float best = 0f;
            lateral = Horizontal(position, _head);

            Vector3 from = _head;
            float walked = 0f;
            for (int i = _points.Count - 1; i >= 0; i--)
            {
                Vector3 to = _points[i];
                float segment = Horizontal(from, to);
                if (segment > 0f)
                {
                    float t = ProjectOntoSegment(position, from, to);
                    float distance = Horizontal(position, Vector3.Lerp(from, to, t));
                    // 排除浮点投影误差后确实更近才替换,避免自交点被较旧的路线抢走。
                    if (distance < lateral - ProjectionTieTolerance)
                    {
                        lateral = distance;
                        best = walked + segment * t;
                    }
                    walked += segment;
                }
                from = to;
            }
            return best;
        }

        /// <summary>
        /// 把 position 投影到轨迹上里程落在 [fromOdometer, toOdometer] 之内的那一部分。
        /// 返回 false 表示窗口内没有轨迹(窗口整个落在已裁掉的旧段之外,或在队首之前)。
        ///
        /// 为什么要按里程开窗:队长沿原路折返、或在同一条街上来回走时,轨迹的几段在几何上重叠,
        /// 只按"哪一段离我最近"选,选中的段会随着厘米级的偏差在新旧两段之间来回跳 —— 两段的行进方向
        /// 相反,队员就在原地一步向前一步向后。调用方记着自己上一帧的里程,只在它附近找,就不会被
        /// 几十米之前走过的那一趟抢走。
        /// </summary>
        /// <param name="preferOdometer">
        /// 几处差不多一样近(相差不超过 tieTolerance)时取里程离它最近的一处;
        /// 传 <see cref="double.PositiveInfinity"/> 表示取离队首最近的。
        /// </param>
        /// <param name="odometer">最近点的里程。</param>
        /// <param name="lateral">position 到最近点的水平距离。</param>
        public bool TryProject(Vector3 position, double fromOdometer, double toOdometer, double preferOdometer,
            float tieTolerance, out double odometer, out float lateral)
        {
            odometer = 0d;
            lateral = float.PositiveInfinity;
            if (!HasHead || _points.Count == 0 || fromOdometer > toOdometer) return false;

            // 第一遍找窗口内的最近距离,第二遍在"差不多一样近"的候选里按里程偏好挑:
            // 一遍做完的话,一串两两相近的候选会让结果顺着链条漂走。
            float nearest = float.PositiveInfinity;
            for (int pass = 0; pass < 2; pass++)
            {
                bool found = false;
                double headOdometer = HeadOdometer;
                // 队首自己也是候选:窗口只盖到队首、或 position 在队首前面时,最近点就是它。
                if (headOdometer >= fromOdometer && headOdometer <= toOdometer)
                    Consider(pass, Horizontal(position, _head), headOdometer, preferOdometer, tieTolerance,
                        ref nearest, ref found, ref odometer, ref lateral);

                Vector3 newer = _head;
                double newerOdometer = headOdometer;
                for (int i = _points.Count - 1; i >= 0; i--)
                {
                    Vector3 older = _points[i];
                    double olderOdometer = _odometers[i];
                    // 从队首往回走,里程单调不增:整段都比窗口旧,后面的只会更旧。
                    if (newerOdometer < fromOdometer) break;
                    float segment = Horizontal(newer, older);
                    if (segment > 0f && olderOdometer <= toOdometer)
                    {
                        // t = 0 在新端,t = 1 在旧端;把 t 夹到窗口盖住的那一截上。
                        float low = newerOdometer > toOdometer ? (float)((newerOdometer - toOdometer) / segment) : 0f;
                        float high = olderOdometer < fromOdometer ? (float)((newerOdometer - fromOdometer) / segment) : 1f;
                        float t = Mathf.Clamp(ProjectOntoSegment(position, newer, older), Mathf.Clamp01(low), Mathf.Clamp01(high));
                        Consider(pass, Horizontal(position, Vector3.Lerp(newer, older, t)), newerOdometer - segment * t,
                            preferOdometer, tieTolerance, ref nearest, ref found, ref odometer, ref lateral);
                    }
                    newer = older;
                    newerOdometer = olderOdometer;
                }
                if (pass == 0 && float.IsPositiveInfinity(nearest)) return false;
            }
            return true;
        }

        private static void Consider(int pass, float distance, double candidate, double prefer, float tieTolerance,
            ref float nearest, ref bool found, ref double odometer, ref float lateral)
        {
            if (pass == 0)
            {
                if (distance < nearest) nearest = distance;
                return;
            }
            if (distance > nearest + tieTolerance) return;
            if (found && !CloserTo(prefer, candidate, odometer)) return;
            found = true;
            odometer = candidate;
            lateral = distance;
        }

        private static bool CloserTo(double prefer, double candidate, double current)
            => double.IsPositiveInfinity(prefer)
                ? candidate > current
                : System.Math.Abs(candidate - prefer) < System.Math.Abs(current - prefer);

        private void AppendPoint(Vector3 point)
        {
            double odometer = _points.Count == 0
                ? 0d
                : _odometers[_odometers.Count - 1] + Horizontal(point, _points[_points.Count - 1]);
            _points.Add(point);
            _odometers.Add(odometer);
        }

        private void Trim()
        {
            // 从最老的一端裁,但永远留至少两个点,保证还有一段可以站。
            float length = Length;
            while (_points.Count > 2)
            {
                float oldest = Horizontal(_points[0], _points[1]);
                if (length - oldest < _maxLength) break;
                _points.RemoveAt(0);
                _odometers.RemoveAt(0);
                length -= oldest;
            }
        }

        private static float ProjectOntoSegment(Vector3 point, Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            float lengthSquared = dx * dx + dz * dz;
            if (lengthSquared <= 0f) return 0f;
            float t = ((point.x - from.x) * dx + (point.z - from.z) * dz) / lengthSquared;
            return Mathf.Clamp01(t);
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// 一个队员"走到轨迹的哪儿了":所在那一代轨迹与里程。由 <see cref="TeamFollowPlanner"/> 逐帧推进,
    /// 调用方只负责存着,以及在自己不再沿轨迹走的时候(暂离、被寻路带离、换队长)丢掉它(= default)。
    /// </summary>
    public struct TeamFollowCursor
    {
        public TeamFollowCursor(int epoch, double odometer)
        {
            Valid = true;
            Epoch = epoch;
            Odometer = odometer;
        }

        public bool Valid { get; }
        public int Epoch { get; }
        public double Odometer { get; }
    }
}
