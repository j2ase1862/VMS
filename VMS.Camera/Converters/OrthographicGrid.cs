namespace VMS.Camera.Converters
{
    /// <summary>
    /// 비격자 점군을 정사투영할 때 쓴 XY 격자 — 원점(최소 X·Y)과 칸 크기(점군 단위/px).
    /// <see cref="PointCloudConverter.OrthographicToDepthMap32F(Models.PointCloudData, float, out OrthographicGrid)"/>
    /// 가 이 격자로 점을 칸에 넣고, 그 맵에서 나온 2D 마스크를 점군에 되짚을 때도 같은 격자를 쓴다.
    /// </summary>
    public readonly record struct OrthographicGrid(float OriginX, float OriginY, float CellSize, int Width, int Height)
    {
        /// <summary>
        /// 점의 XY 가 들어가는 칸. 격자 밖이면 false.
        /// 최대 X·Y 에 놓인 점은 나눗셈이 정확히 Width·Height 가 되므로 마지막 칸에 넣는다 (비닝과 같은 규칙).
        /// </summary>
        public bool TryGetCell(float x, float y, out int u, out int v)
        {
            u = v = -1;
            if (CellSize <= 0f || !float.IsFinite(x) || !float.IsFinite(y)) return false;

            float fu = (x - OriginX) / CellSize;
            float fv = (y - OriginY) / CellSize;
            if (fu < 0f || fv < 0f || fu > Width || fv > Height) return false;

            u = System.Math.Min(Width - 1, (int)fu);
            v = System.Math.Min(Height - 1, (int)fv);
            return true;
        }
    }
}
