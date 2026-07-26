using System.Collections.Generic;
using System.Numerics;
using VMS.Camera.Models;

namespace VMS.Core.Controls
{
    /// <summary>
    /// 점군 뷰어 측정(거리/각도) 지원 수학 — 픽셀→mm 환산과 화면 투영 피킹.
    /// organized 점군은 X/Y=뎁스맵 픽셀·Z=mm 혼합 단위이므로 Intrinsics 가 있으면
    /// 핀홀 역투영 (x−cx)·z/fx 로 mm 환산 후 거리·각도를 계산한다.
    /// </summary>
    public static class PointCloudMeasurement
    {
        /// <summary>
        /// 측정값을 mm 로 신뢰할 수 있는가 — organized 점군인데 Intrinsics 가 없으면
        /// (.vpc 로드·공유 프레임 등) X/Y 픽셀·Z mm 혼합이라 거리/각도가 왜곡된다.
        /// 비organized 점군(정합 결과·STL 샘플링)은 전 축 mm 로 간주.
        /// </summary>
        public static bool IsMetric(PointCloudData? cloud) =>
            cloud == null
            || cloud.Intrinsics is { IsValid: true }
            || !cloud.IsOrganized;

        /// <summary>데이터 좌표(X/Y px, Z mm)를 핀홀 역투영으로 mm 좌표로 환산. Intrinsics 없으면 그대로.</summary>
        public static Vector3 ToMetric(Vector3 dataPoint, DepthIntrinsics? intrinsics)
        {
            if (intrinsics is not { IsValid: true }) return dataPoint;
            return new Vector3(
                (float)((dataPoint.X - intrinsics.Cx) * dataPoint.Z / intrinsics.Fx),
                (float)((dataPoint.Y - intrinsics.Cy) * dataPoint.Z / intrinsics.Fy),
                dataPoint.Z);
        }

        public static float Distance(Vector3 a, Vector3 b, DepthIntrinsics? intrinsics) =>
            Vector3.Distance(ToMetric(a, intrinsics), ToMetric(b, intrinsics));

        /// <summary>꼭짓점(vertex)에서 a·b 로 뻗는 두 변 사이 각도(도). 변 길이가 0에 수렴하면 NaN.</summary>
        public static float AngleDeg(Vector3 a, Vector3 vertex, Vector3 b, DepthIntrinsics? intrinsics)
        {
            var va = ToMetric(a, intrinsics) - ToMetric(vertex, intrinsics);
            var vb = ToMetric(b, intrinsics) - ToMetric(vertex, intrinsics);
            float la = va.Length();
            float lb = vb.Length();
            if (la < 1e-6f || lb < 1e-6f) return float.NaN;
            float cos = Math.Clamp(Vector3.Dot(va, vb) / (la * lb), -1f, 1f);
            return MathF.Acos(cos) * (180f / MathF.PI);
        }

        /// <summary>
        /// 뷰-투영 행렬 (우수 좌표계). FOV 는 수평 기준 — WPF/Helix PerspectiveCamera 규약과 동일.
        /// </summary>
        public static Matrix4x4 BuildViewProjection(
            Vector3 cameraPosition, Vector3 lookDirection, Vector3 upDirection,
            float horizontalFovDeg, float aspect, float nearPlane, float farPlane)
        {
            aspect = MathF.Max(aspect, 1e-3f);
            var view = Matrix4x4.CreateLookAt(cameraPosition, cameraPosition + lookDirection, upDirection);
            float hFov = horizontalFovDeg * MathF.PI / 180f;
            float vFov = 2f * MathF.Atan(MathF.Tan(hFov * 0.5f) / aspect);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(vFov, aspect, nearPlane, farPlane);
            return view * proj;
        }

        /// <summary>월드 좌표를 화면 픽셀 좌표로 투영. 카메라 뒤에 있으면 false.</summary>
        public static bool TryProjectToScreen(
            Vector3 world, in Matrix4x4 viewProjection,
            double viewportWidth, double viewportHeight,
            out double screenX, out double screenY)
        {
            var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
            if (clip.W <= 1e-6f)
            {
                screenX = 0;
                screenY = 0;
                return false;
            }
            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            screenX = (ndcX + 1f) * 0.5f * viewportWidth;
            screenY = (1f - ndcY) * 0.5f * viewportHeight;
            return true;
        }

        /// <summary>
        /// 마우스 반경(px) 내에 투영되는 점들 중 커서 근접도와 카메라 근접도를 함께
        /// 점수화해 하나를 고른다 — 목표 표면 뒤로 겹쳐 보이는 배경 점 대신 앞 점 선택.
        /// 반경 내 후보가 없으면 -1.
        /// </summary>
        public static int FindNearestPointOnScreen(
            IList<Vector3> points, in Matrix4x4 viewProjection, Vector3 cameraPosition,
            double viewportWidth, double viewportHeight,
            double mouseX, double mouseY, double radiusPx)
        {
            var candIdx = new List<int>();
            var candScreenDist = new List<double>();
            var candCamDist = new List<float>();
            float camMin = float.MaxValue;
            float camMax = float.MinValue;

            for (int i = 0; i < points.Count; i++)
            {
                if (!TryProjectToScreen(points[i], viewProjection, viewportWidth, viewportHeight,
                        out double sx, out double sy))
                    continue;

                double dx = sx - mouseX;
                double dy = sy - mouseY;
                double d2 = dx * dx + dy * dy;
                if (d2 > radiusPx * radiusPx) continue;

                float camDist = Vector3.Distance(points[i], cameraPosition);
                candIdx.Add(i);
                candScreenDist.Add(Math.Sqrt(d2));
                candCamDist.Add(camDist);
                if (camDist < camMin) camMin = camDist;
                if (camDist > camMax) camMax = camDist;
            }

            if (candIdx.Count == 0) return -1;

            float camRange = MathF.Max(camMax - camMin, 1e-6f);
            int best = -1;
            double bestScore = double.MaxValue;
            for (int c = 0; c < candIdx.Count; c++)
            {
                double score = candScreenDist[c] / radiusPx + (candCamDist[c] - camMin) / camRange;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candIdx[c];
                }
            }
            return best;
        }
    }
}
