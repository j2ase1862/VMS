using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Services;

/// <summary>k-최근접 탐색 결과 하나 — 원본 점 인덱스와 제곱 거리.</summary>
public struct KdNeighbor
{
    public int Index;
    public double DistSq;
}

/// <summary>
/// 정적 3D k-d 트리 — 최근접/k-최근접 탐색 전용 (배열 기반, 재귀 빌드).
/// ICP 대응 탐색과 점군 전처리(이상치 제거·법선 추정)가 공용한다.
/// </summary>
public class KdTree3
{
    private readonly Point3D[] _pts;
    private readonly int[] _idx;

    public KdTree3(IReadOnlyList<Point3D> points)
    {
        _pts = points.ToArray();
        _idx = Enumerable.Range(0, _pts.Length).ToArray();
        Build(0, _pts.Length - 1, 0);
    }

    private void Build(int lo, int hi, int depth)
    {
        if (lo >= hi) return;
        int axis = depth % 3;
        int mid = (lo + hi) / 2;
        NthElement(lo, hi, mid, axis);
        Build(lo, mid - 1, depth + 1);
        Build(mid + 1, hi, depth + 1);
    }

    // quickselect — _idx[lo..hi] 에서 mid 위치에 axis 기준 중앙값 배치
    private void NthElement(int lo, int hi, int mid, int axis)
    {
        while (lo < hi)
        {
            double pivot = Coord(_idx[(lo + hi) / 2], axis);
            int i = lo, j = hi;
            while (i <= j)
            {
                while (Coord(_idx[i], axis) < pivot) i++;
                while (Coord(_idx[j], axis) > pivot) j--;
                if (i <= j) { (_idx[i], _idx[j]) = (_idx[j], _idx[i]); i++; j--; }
            }
            if (mid <= j) hi = j;
            else if (mid >= i) lo = i;
            else return;
        }
    }

    private double Coord(int i, int axis) => axis switch
    {
        0 => _pts[i].X,
        1 => _pts[i].Y,
        _ => _pts[i].Z,
    };

    public Point3D Nearest(Point3D query, out double distance)
    {
        int idx = NearestIndex(query, out distance);
        return _pts[idx];
    }

    public int NearestIndex(Point3D query, out double distance)
    {
        double best = double.MaxValue;
        int bestIdx = -1;
        Search(0, _pts.Length - 1, 0, query, ref best, ref bestIdx);
        distance = Math.Sqrt(best);
        return bestIdx;
    }

    private void Search(int lo, int hi, int depth, Point3D q, ref double bestSq, ref int bestIdx)
    {
        if (lo > hi) return;
        int mid = (lo + hi) / 2;
        var p = _pts[_idx[mid]];
        double dsq = (p - q).LengthSquared;
        if (dsq < bestSq) { bestSq = dsq; bestIdx = _idx[mid]; }

        int axis = depth % 3;
        double diff = axis switch { 0 => q.X - p.X, 1 => q.Y - p.Y, _ => q.Z - p.Z };
        if (diff <= 0)
        {
            Search(lo, mid - 1, depth + 1, q, ref bestSq, ref bestIdx);
            if (diff * diff < bestSq) Search(mid + 1, hi, depth + 1, q, ref bestSq, ref bestIdx);
        }
        else
        {
            Search(mid + 1, hi, depth + 1, q, ref bestSq, ref bestIdx);
            if (diff * diff < bestSq) Search(lo, mid - 1, depth + 1, q, ref bestSq, ref bestIdx);
        }
    }

    /// <summary>
    /// k-최근접 탐색 — buffer(길이 ≥ k)에 제곱 거리 오름차순으로 채우고 개수를 돌려준다.
    /// 질의점 자신이 트리에 있으면 거리 0 으로 포함된다 (호출측에서 건너뛸 것).
    /// buffer 재사용으로 반복 질의 시 할당이 없다.
    /// </summary>
    public int KNearest(Point3D query, int k, KdNeighbor[] buffer)
    {
        if (k <= 0 || _pts.Length == 0) return 0;
        k = Math.Min(k, _pts.Length);
        int count = 0;
        KNearestSearch(0, _pts.Length - 1, 0, query, k, buffer, ref count);
        return count;
    }

    private void KNearestSearch(int lo, int hi, int depth, Point3D q, int k,
        KdNeighbor[] buf, ref int count)
    {
        if (lo > hi) return;
        int mid = (lo + hi) / 2;
        var p = _pts[_idx[mid]];
        double dsq = (p - q).LengthSquared;
        if (count < k || dsq < buf[count - 1].DistSq)
        {
            // 오름차순 유지 삽입 (k 가 작아 삽입 정렬이 힙보다 빠르다)
            int pos = count < k ? count : k - 1;
            if (count < k) count++;
            while (pos > 0 && buf[pos - 1].DistSq > dsq) { buf[pos] = buf[pos - 1]; pos--; }
            buf[pos] = new KdNeighbor { Index = _idx[mid], DistSq = dsq };
        }

        int axis = depth % 3;
        double diff = axis switch { 0 => q.X - p.X, 1 => q.Y - p.Y, _ => q.Z - p.Z };
        if (diff <= 0)
        {
            KNearestSearch(lo, mid - 1, depth + 1, q, k, buf, ref count);
            double worst = count < k ? double.MaxValue : buf[count - 1].DistSq;
            if (diff * diff < worst) KNearestSearch(mid + 1, hi, depth + 1, q, k, buf, ref count);
        }
        else
        {
            KNearestSearch(mid + 1, hi, depth + 1, q, k, buf, ref count);
            double worst = count < k ? double.MaxValue : buf[count - 1].DistSq;
            if (diff * diff < worst) KNearestSearch(lo, mid - 1, depth + 1, q, k, buf, ref count);
        }
    }
}
