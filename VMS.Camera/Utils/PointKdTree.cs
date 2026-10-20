using System;
using System.Collections.Generic;
using System.Numerics;

namespace VMS.Camera.Utils
{
    /// <summary>
    /// 3D 점 최근접 탐색용 KD-트리 — ICP 가 반복마다 소스 점 전부의 최근접 기준점을 찾을 때 쓴다.
    ///
    /// <para>예전에는 소스 점마다 기준 점 전부와 거리를 쟀다(N·M). 3만 점끼리면 ICP 한 번이 25~30초였고,
    /// Registration 의 Coarse 가 출발 자세 여러 개에서 ICP 를 끝까지 돌리면서(#552) 그 시간이 2~4배가 됐다.
    /// 기준 점군은 ICP 내내 그대로이므로 트리를 한 번 만들고 반복마다 질의한다(N·log M).</para>
    ///
    /// <para><b>결과는 전수 비교와 같다.</b> 근사 탐색이 아니라 정확한 최근접이고, 거리가 같으면 전수 비교처럼
    /// 번호가 작은 점을 고른다 — 격자처럼 등거리 점이 많은 점군에서도 대응점이 한 점도 달라지지 않는다.
    /// NaN 좌표 점은 트리에 넣지 않는다(전수 비교도 건너뛰었다).</para>
    ///
    /// <para>만든 뒤에는 읽기만 하므로 여러 스레드에서 동시에 질의해도 된다.</para>
    /// </summary>
    public sealed class PointKdTree
    {
        private readonly Vector3[] _points;
        private readonly int[] _index;   // 트리 순서로 놓인 원래 점 번호 — 구간 [lo,hi) 의 가운데가 노드
        private readonly byte[] _axis;   // 노드(가운데 위치)마다 나눈 축 0=X 1=Y 2=Z
        private readonly int _count;

        public PointKdTree(Vector3[] points, int count)
        {
            _points = points;
            var valid = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                var p = points[i];
                if (!float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsNaN(p.Z)) valid.Add(i);
            }
            _index = valid.ToArray();
            _count = _index.Length;
            _axis = new byte[_count];
            Build(0, _count);
        }

        /// <summary>트리에 들어간 점 수 (NaN 제외).</summary>
        public int Count => _count;

        /// <summary>가장 가까운 점의 원래 번호. 점이 없거나 질의 좌표가 NaN 이면 -1.</summary>
        public int Nearest(Vector3 q)
        {
            if (_count == 0 || float.IsNaN(q.X) || float.IsNaN(q.Y) || float.IsNaN(q.Z)) return -1;
            float best = float.MaxValue;
            int bestIdx = -1;
            Search(q, 0, _count, ref best, ref bestIdx);
            return bestIdx;
        }

        private void Build(int lo, int hi)
        {
            if (hi - lo <= 1)
            {
                if (hi - lo == 1) _axis[lo] = 0;
                return;
            }

            // 퍼짐이 가장 큰 축으로 나눈다 — 평평한 부품(Z 퍼짐이 작음)에서 깊이 순서보다 균형이 좋다
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = lo; i < hi; i++)
            {
                var p = _points[_index[i]];
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
            float sx = maxX - minX, sy = maxY - minY, sz = maxZ - minZ;
            byte axis = sx >= sy && sx >= sz ? (byte)0 : sy >= sz ? (byte)1 : (byte)2;

            Array.Sort(_index, lo, hi - lo, new AxisComparer(_points, axis));
            int mid = (lo + hi) >> 1;
            _axis[mid] = axis;
            Build(lo, mid);
            Build(mid + 1, hi);
        }

        private void Search(Vector3 q, int lo, int hi, ref float best, ref int bestIdx)
        {
            if (lo >= hi) return;
            int mid = (lo + hi) >> 1;
            int pi = _index[mid];
            var p = _points[pi];

            float dx = q.X - p.X, dy = q.Y - p.Y, dz = q.Z - p.Z;
            float d = dx * dx + dy * dy + dz * dz;
            if (d < best || (d == best && pi < bestIdx))
            {
                best = d;
                bestIdx = pi;
            }
            if (hi - lo == 1) return;

            float diff = _axis[mid] switch { 0 => dx, 1 => dy, _ => dz };
            if (diff < 0)
            {
                Search(q, lo, mid, ref best, ref bestIdx);
                // 등거리 동점까지 찾아야 번호가 작은 점을 고를 수 있으므로 같을 때도 반대쪽을 본다
                if (diff * diff <= best) Search(q, mid + 1, hi, ref best, ref bestIdx);
            }
            else
            {
                Search(q, mid + 1, hi, ref best, ref bestIdx);
                if (diff * diff <= best) Search(q, lo, mid, ref best, ref bestIdx);
            }
        }

        private sealed class AxisComparer : IComparer<int>
        {
            private readonly Vector3[] _p;
            private readonly byte _axis;
            public AxisComparer(Vector3[] p, byte axis) { _p = p; _axis = axis; }

            public int Compare(int a, int b)
            {
                float va = _axis switch { 0 => _p[a].X, 1 => _p[a].Y, _ => _p[a].Z };
                float vb = _axis switch { 0 => _p[b].X, 1 => _p[b].Y, _ => _p[b].Z };
                int c = va.CompareTo(vb);
                return c != 0 ? c : a.CompareTo(b);
            }
        }
    }
}
