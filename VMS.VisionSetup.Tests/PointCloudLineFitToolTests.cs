using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// PointCloud Line Fit (v1.42.7) — 3D 직선 피팅·직진도·3D Geometry 점-직선 거리 공급.
    /// 종전에는 직선을 내는 도구가 없어 3D Geometry 의 PointToLineDistance3D 를 쓸 수 없었다.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class PointCloudLineFitToolTests
    {
        /// <summary>
        /// (100,200,1800) 에서 XY 방향 30° · Z 로 약간 기운 길이 300 의 레일(폭 6, 잡음 ±0.3) + 이탈점 200개.
        /// bendMm > 0 이면 가운데가 Z 로 불룩한 곡선.
        /// </summary>
        private static PointCloudData Rail(float bendMm = 0)
        {
            var rnd = new Random(7);
            var dir = Vector3.Normalize(new Vector3(MathF.Cos(MathF.PI / 6), MathF.Sin(MathF.PI / 6), 0.05f));
            var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitZ));
            var start = new Vector3(100, 200, 1800);
            var xyz = new List<float>();
            for (int i = 0; i <= 300; i++)
                for (int k = -3; k <= 3; k++)
                {
                    float t = i;
                    var p = start + dir * t + side * k * 0.5f
                            + new Vector3(0, 0, bendMm * 4 * (t / 300f) * (1 - t / 300f))
                            + new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.6f;
                    xyz.AddRange(new[] { p.X, p.Y, p.Z });
                }
            for (int i = 0; i < 200; i++)   // 이탈점 — 레일에서 멀리 흩어진 잡음
                xyz.AddRange(new[] { 100f + (float)rnd.NextDouble() * 300, 200f + (float)rnd.NextDouble() * 200, 1750f + (float)rnd.NextDouble() * 100 });
            xyz.AddRange(new[] { 50f, 50f, 0f });   // 측정 실패 화소(Z=0)는 점이 아니다
            return PointCloudData.FromArrays(xyz.ToArray());
        }

        private static VisionResult Run(PointCloudLineFitTool tool, PointCloudData cloud)
        {
            var service = VisionService.Instance;
            service.CurrentPointCloud = cloud;
            try
            {
                using var img = new Mat(600, 600, MatType.CV_8UC1, Scalar.All(0));
                var r = tool.Execute(img);
                r.ReleaseMats();
                return r;
            }
            finally { service.CurrentPointCloud = null; }
        }

        [Fact]
        public void Fit_RecoversRailDirectionLengthAndStraightness_DespiteOutliers()
        {
            using var cloud = Rail();
            var r = Run(new PointCloudLineFitTool { DistanceThreshold = 2f }, cloud);

            Assert.True(r.Success, r.Message);
            Assert.Equal(30.0, Convert.ToDouble(r.Data["AngleXY"]), 0);
            Assert.Equal(2.9, Convert.ToDouble(r.Data["Elevation"]), 0);          // asin(0.05/|d|) ≈ 2.86°
            Assert.InRange(Convert.ToDouble(r.Data["Length"]), 295, 305);
            Assert.InRange(Convert.ToDouble(r.Data["MaxDeviation"]), 0.5, 2.0);    // 폭 ±1.5 + 잡음
            Assert.True(Convert.ToInt32(r.Data["InlierCount"]) >= 301 * 7 - 5);    // 레일 점은 거의 다, 이탈점은 제외
            Assert.True(Convert.ToInt32(r.Data["TotalPoints"]) == 301 * 7 + 200); // Z=0 화소 제외
        }

        [Theory]
        [InlineData(0f, 5f, true)]
        [InlineData(8f, 10f, false)]   // 가운데 8mm 불룩 → 직진도 초과 (인라이어 거리가 휨보다 커야 휜 부분이 잡힌다)
        public void StraightnessJudgment(float bend, float threshold, bool pass)
        {
            using var cloud = Rail(bend);
            var tool = new PointCloudLineFitTool { DistanceThreshold = threshold, EnableJudgment = true, MaxStraightness = 4.0 };
            var r = Run(tool, cloud);

            Assert.Equal(pass, r.Success);
            Assert.Equal(pass, r.Data["JudgmentPass"]);
            Assert.Contains("직진도", r.Message);
        }

        [Fact]
        public void StraightnessJudgment_WarnsWhenThresholdHidesBend()
        {
            // 인라이어 거리 2 < 상한 4 — 8mm 휜 레일의 가운데가 직선 밖으로 빠져 OK 로 보일 수 있다 → 경고
            using var cloud = Rail(8f);
            var r = Run(new PointCloudLineFitTool { DistanceThreshold = 2f, EnableJudgment = true, MaxStraightness = 4.0 }, cloud);
            Assert.Contains("⚠ Distance Threshold", r.Message);
        }

        [Fact]
        public void Fit_TooFewPoints_Fails()
        {
            using var cloud = PointCloudData.FromArrays(new[] { 0f, 0f, 1f, 1f, 1f, 1f });
            var r = Run(new PointCloudLineFitTool { MinInliers = 50 }, cloud);
            Assert.False(r.Success);
            Assert.Contains("부족", r.Message);
        }

        [Fact]
        public void Geometry3D_PointToLine_UsesLineFitSource_AndClusterRawPoint()
        {
            using var cloud = Rail();
            var line = Run(new PointCloudLineFitTool { DistanceThreshold = 2f }, cloud);

            // 레일 시작점에서 수직으로 40 떨어진 점 (원좌표). mm 중심은 일부러 엉뚱한 값 — 원좌표를 써야 한다
            var dir = new Vector3(Convert.ToSingle(line.Data["LineDirX"]), Convert.ToSingle(line.Data["LineDirY"]), Convert.ToSingle(line.Data["LineDirZ"]));
            var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitZ));
            var p = new Vector3(Convert.ToSingle(line.Data["LinePointX"]), Convert.ToSingle(line.Data["LinePointY"]), Convert.ToSingle(line.Data["LinePointZ"])) + side * 40;
            var cluster = new VisionResult { Success = true };
            cluster.Data["Cluster0_CenterX"] = (double)p.X; cluster.Data["Cluster0_CenterY"] = (double)p.Y; cluster.Data["Cluster0_CenterZ"] = (double)p.Z;
            cluster.Data["Cluster0_CenterXMm"] = -999.0; cluster.Data["Cluster0_CenterYMm"] = -999.0;

            var geo = new Geometry3DTool { Operation = Geometry3DOperation.PointToLineDistance3D, UseManualPoints = false,
                                           EnableJudgment = true, ExpectedValue = 40, ToleranceMinus = 0.5, TolerancePlus = 0.5 };
            geo.ClearSourceGeometries();
            geo.CollectSourceGeometry("l", "Rail", "PointCloudLineFitTool", line);
            geo.CollectSourceGeometry("c", "Part", "PointCloudClusterTool", cluster);
            geo.FinalizeSourceGeometries();
            Assert.Contains(geo.SourceGeometries, s => s.HasLine);

            using var img = new Mat(10, 10, MatType.CV_8UC1, Scalar.All(0));
            var r = geo.Execute(img);
            Assert.True(r.Success, r.Message);
            Assert.Equal(40.0, Convert.ToDouble(r.Data["Distance3D"]), 1);
        }

        [Fact]
        public void Serializer_And_SettingsViewModel_RoundTrip()
        {
            var tool = new PointCloudLineFitTool
            {
                DistanceThreshold = 3.5f, Iterations = 800, MinInliers = 120, DrawOverlay = false,
                EnableJudgment = true, MaxStraightness = 2.5,
            };
            var restored = Assert.IsType<PointCloudLineFitTool>(ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(tool)));
            Assert.Equal(3.5f, restored.DistanceThreshold); Assert.Equal(800, restored.Iterations); Assert.Equal(120, restored.MinInliers);
            Assert.False(restored.DrawOverlay); Assert.True(restored.EnableJudgment); Assert.Equal(2.5, restored.MaxStraightness);

            var t = new PointCloudLineFitTool();
            var vm = new PointCloudLineFitToolSettingsViewModel(t)
            { DistanceThreshold = 4f, Iterations = 500, MinInliers = 30, DrawOverlay = false, EnableJudgment = true, MaxStraightness = 1.2 };
            Assert.Equal(4f, t.DistanceThreshold); Assert.Equal(500, t.Iterations); Assert.Equal(30, t.MinInliers);
            Assert.False(t.DrawOverlay); Assert.True(t.EnableJudgment); Assert.Equal(1.2, t.MaxStraightness);
            t.MaxStraightness = 0.7; Assert.Equal(0.7, vm.MaxStraightness);
        }

        [Fact]
        public void Registered_InFactory_Palette_AndHelp()
        {
            Assert.IsType<PointCloudLineFitTool>(VisionService.CreateTool("PointCloudLineFitTool"));
            Assert.NotNull(HelpContent.GetToolHelp("PointCloudLineFitTool"));
            var clone = Assert.IsType<PointCloudLineFitTool>(new PointCloudLineFitTool { MaxStraightness = 9 }.Clone());
            Assert.Equal(9, clone.MaxStraightness);
        }
    }
}
