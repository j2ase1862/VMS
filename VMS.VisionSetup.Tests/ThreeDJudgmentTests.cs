using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// 3D 도구 판정(v1.42.7) — 3D Geometry(기준값±공차) · Plane Fit(평탄도 상한) ·
    /// PointCloud Cluster(개수·치수) · PointCloud Registration(Confidence 하한).
    /// 종전에는 3D 도구가 값만 내고 OK/NG 를 못 냈다(Registration 은 틀려도 OK).
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class ThreeDJudgmentTests
    {
        private static Mat Img(int w = 40, int h = 40) => new(h, w, MatType.CV_8UC1, Scalar.All(0));

        private static VisionResult PlaneResult(double a, double b, double c, double d, bool success = true)
        {
            var r = new VisionResult { Success = success };
            r.Data["PlaneA"] = a; r.Data["PlaneB"] = b; r.Data["PlaneC"] = c; r.Data["PlaneD"] = d;
            return r;
        }

        private static Geometry3DTool Geo(Geometry3DOperation op, params (string type, VisionResult r)[] sources)
        {
            var geo = new Geometry3DTool { Operation = op, UseManualPoints = false };
            geo.ClearSourceGeometries();
            int i = 0;
            foreach (var (type, r) in sources)
                geo.CollectSourceGeometry($"s{i}", $"S{i++}", type, r);
            geo.FinalizeSourceGeometries();
            return geo;
        }

        // ── 3D Geometry ──

        [Theory]
        [InlineData(90, 2, 2, true)]    // 거리 90 ∈ 88~92
        [InlineData(95, 2, 2, false)]   // 90 ∉ 93~97
        public void Geometry3D_PointToPlane_JudgesDistance(double expected, double minus, double plus, bool pass)
        {
            var cluster = new VisionResult { Success = true };
            cluster.Data["Cluster0_CenterX"] = 10.0; cluster.Data["Cluster0_CenterY"] = 10.0; cluster.Data["Cluster0_CenterZ"] = 10.0;
            cluster.Data["Cluster0_CenterXMm"] = 10.0; cluster.Data["Cluster0_CenterYMm"] = 10.0;
            var geo = Geo(Geometry3DOperation.PointToPlaneDistance,
                ("PlaneFitTool", PlaneResult(0, 0, 1, -100)),        // z = 100 평면
                ("PointCloudClusterTool", cluster));                  // z = 10 점 → 거리 90
            geo.EnableJudgment = true; geo.ExpectedValue = expected; geo.ToleranceMinus = minus; geo.TolerancePlus = plus;

            using var img = Img();
            var r = geo.Execute(img);

            Assert.Equal(pass, r.Success);
            Assert.Equal(pass, r.Data["JudgmentPass"]);
            Assert.Equal(90.0, Convert.ToDouble(r.Data["JudgmentValue"]), 3);
            Assert.Contains(pass ? "판정 OK" : "판정 NG", r.Message);
        }

        [Fact]
        public void Geometry3D_PlaneAngle_JudgesDegrees()
        {
            // z = 0 평면과 x 로 5° 기운 평면
            double rad = 5 * Math.PI / 180;
            var geo = Geo(Geometry3DOperation.PlaneToPlaneAngle,
                ("PlaneFitTool", PlaneResult(0, 0, 1, 0)),
                ("PlaneFitTool", PlaneResult(Math.Sin(rad), 0, Math.Cos(rad), 0)));
            geo.EnableJudgment = true; geo.ExpectedValue = 0; geo.ToleranceMinus = 0; geo.TolerancePlus = 1;

            using var img = Img();
            var r = geo.Execute(img);

            Assert.False(r.Success);
            Assert.Equal(5.0, Convert.ToDouble(r.Data["JudgmentValue"]), 2);
            Assert.Contains("°", r.Message);
        }

        [Fact]
        public void Geometry3D_JudgmentOff_KeepsValueOnlyBehavior()
        {
            var geo = Geo(Geometry3DOperation.PlaneToPlaneDistance,
                ("PlaneFitTool", PlaneResult(0, 0, 1, -100)), ("PlaneFitTool", PlaneResult(0, 0, 1, -130)));
            using var img = Img();
            var r = geo.Execute(img);
            Assert.True(r.Success, r.Message);
            Assert.False(r.Data.ContainsKey("JudgmentPass"));
        }

        [Fact]
        public void Geometry3D_UsesPlane_WhoseOnlyFailureIsItsOwnJudgment()
        {
            // 평탄도 판정만 NG 인 평면 — 평면식은 유효하므로 높이 측정에 계속 쓴다
            var ngPlane = PlaneResult(0, 0, 1, -100, success: false);
            ngPlane.Data["JudgmentPass"] = false;
            var brokenPlane = PlaneResult(0, 0, 1, -100, success: false);   // 판정 아닌 실패 — 버린다

            var cluster = new VisionResult { Success = true };
            cluster.Data["Cluster0_CenterX"] = 0.0; cluster.Data["Cluster0_CenterY"] = 0.0; cluster.Data["Cluster0_CenterZ"] = 40.0;
            cluster.Data["Cluster0_CenterXMm"] = 0.0; cluster.Data["Cluster0_CenterYMm"] = 0.0;

            using var img = Img();
            var ok = Geo(Geometry3DOperation.PointToPlaneDistance, ("PlaneFitTool", ngPlane), ("PointCloudClusterTool", cluster)).Execute(img);
            Assert.True(ok.Success, ok.Message);
            Assert.Equal(60.0, Convert.ToDouble(ok.Data["Distance3D"]), 3);

            var fail = Geo(Geometry3DOperation.PointToPlaneDistance, ("PlaneFitTool", brokenPlane), ("PointCloudClusterTool", cluster)).Execute(img);
            Assert.False(fail.Success);
        }

        // ── Plane Fit ──

        /// <summary>40×40 격자 — Z=100 평면, bump=true 면 가운데 4×4 화소를 3mm 올림.</summary>
        private static PointCloudData Surface(bool bump)
        {
            const int w = 40, h = 40;
            var xyz = new float[w * h * 3];
            int i = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    xyz[i++] = x; xyz[i++] = y;
                    xyz[i++] = bump && x >= 18 && x < 22 && y >= 18 && y < 22 ? 97f : 100f;
                }
            return PointCloudData.FromArrays(xyz, null, "surface", w, h);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void PlaneFit_FlatnessJudgment(bool bump, bool pass)
        {
            var service = VisionService.Instance;
            using var cloud = Surface(bump);
            var (hm, depth, _) = service.GenerateHeightMap(cloud, 0f, 0f, 200f);
            try
            {
                var plane = new PlaneFitTool { SampleStride = 1, RansacThreshold = 0.5, EnableJudgment = true, MaxFlatness = 1.0 };
                var r = plane.Execute(hm);

                Assert.Equal(pass, r.Success);
                Assert.Equal(pass, r.Data["JudgmentPass"]);
                Assert.Contains("평탄도", r.Message);
                Assert.True(r.Data.ContainsKey("PlaneA"));   // NG 여도 평면식은 나온다
            }
            finally
            {
                hm.Dispose(); depth.Dispose();
                service.CurrentHeightMapMetadata = null;
            }
        }

        // ── PointCloud Cluster ──

        /// <summary>10×5 격자 점 덩어리 n개 (서로 50 떨어짐).</summary>
        private static PointCloudData Blocks(int n)
        {
            var xyz = new List<float>();
            for (int b = 0; b < n; b++)
                for (int y = 0; y < 5; y++)
                    for (int x = 0; x < 10; x++)
                    { xyz.Add(b * 50 + x); xyz.Add(y); xyz.Add(10); }
            return PointCloudData.FromArrays(xyz.ToArray());
        }

        private static VisionResult RunCluster(PointCloudClusterTool tool, int blocks)
        {
            var service = VisionService.Instance;
            service.CurrentPointCloud = Blocks(blocks);
            try
            {
                using var img = Img(200, 20);
                return tool.Execute(img);
            }
            finally { service.CurrentPointCloud = null; }
        }

        private static PointCloudClusterTool Cluster() => new()
        {
            Tolerance = 1.5f, MinPoints = 5, DrawOverlay = false,
            OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal,
            EnableJudgment = true,
        };

        [Theory]
        [InlineData(PointCloudClusterTool.ClusterCountMode.Equal, 3, 0, true)]
        [InlineData(PointCloudClusterTool.ClusterCountMode.Equal, 2, 0, false)]
        [InlineData(PointCloudClusterTool.ClusterCountMode.GreaterOrEqual, 2, 0, true)]
        [InlineData(PointCloudClusterTool.ClusterCountMode.LessOrEqual, 2, 0, false)]
        [InlineData(PointCloudClusterTool.ClusterCountMode.Range, 2, 4, true)]
        public void Cluster_CountJudgment(PointCloudClusterTool.ClusterCountMode mode, int expected, int max, bool pass)
        {
            var tool = Cluster();
            tool.CountMode = mode; tool.ExpectedCount = expected; tool.ExpectedCountMax = max;

            var r = RunCluster(tool, 3);

            Assert.Equal(3, r.Data["ClusterCount"]);
            Assert.Equal(pass, r.Success);
            Assert.Equal(pass, r.Data["CountJudgmentPass"]);
        }

        [Fact]
        public void Cluster_ZeroFound_LessOrEqualZero_PassesAsNoForeignObject()
        {
            var tool = Cluster();
            tool.MinPoints = 1000;   // 어떤 덩어리도 통과 못 함 → 0개
            tool.CountMode = PointCloudClusterTool.ClusterCountMode.LessOrEqual; tool.ExpectedCount = 0;

            var r = RunCluster(tool, 2);

            Assert.Equal(0, r.Data["ClusterCount"]);
            Assert.True(r.Success, r.Message);
            Assert.Contains("판정 OK", r.Message);
        }

        [Fact]
        public void Cluster_ZeroFound_JudgmentOff_StillFails()
        {
            var tool = Cluster();
            tool.EnableJudgment = false; tool.MinPoints = 1000;
            Assert.False(RunCluster(tool, 2).Success);
        }

        [Theory]
        [InlineData(9, 4, true)]     // 10×5 격자 → OBB 9×4 (화소, XyScale 1)
        [InlineData(20, 4, false)]
        public void Cluster_SizeJudgment_ChecksEveryReportedCluster(double length, double width, bool pass)
        {
            var tool = Cluster();
            tool.UseCountJudgment = false;
            tool.UseSizeJudgment = true; tool.ExpectedLength = length; tool.ExpectedWidth = width;
            tool.SizeToleranceMinus = 0.5; tool.SizeTolerancePlus = 0.5;

            var r = RunCluster(tool, 3);

            Assert.Equal(pass, r.Success);
            Assert.Equal(pass, r.Data["SizeJudgmentPass"]);
            Assert.Equal(pass ? -1 : 0, r.Data["SizeJudgmentFailIndex"]);
        }

        // ── PointCloud Registration ──

        [Fact]
        public void Registration_ConfidenceJudgment_PassesSameShape_FailsUnrelated()
        {
            var service = VisionService.Instance;
            var refPath = Path.Combine(Path.GetTempPath(), $"reg_judge_{Guid.NewGuid():N}.vpc");
            using (var reference = Blocks(2)) reference.SaveToFile(refPath);
            try
            {
                var tool = new PointCloudRegistrationTool
                {
                    ReferencePath = refPath, EnableCoarseAlignment = false, ApplyTransformToSource = false,
                    ConfidenceDistanceMm = 0.5f, EnableJudgment = true, MinConfidence = 0.5,
                };
                using var img = Img();

                service.CurrentPointCloud = Blocks(2);
                var same = tool.Execute(img);
                Assert.True(same.Success, same.Message);
                Assert.True((bool)same.Data["JudgmentPass"]);

                // 전혀 다른 형상(멀리 떨어진 성긴 점) — 정합은 끝나지만 Confidence 가 낮다
                var far = Enumerable.Range(0, 60).SelectMany(i => new[] { 1000f + i * 7, 500f + (i % 5) * 9, 300f }).ToArray();
                service.CurrentPointCloud = PointCloudData.FromArrays(far);
                var unrelated = tool.Execute(img);
                Assert.False(unrelated.Success);
                Assert.Contains("Confidence", unrelated.Message);

                tool.EnableJudgment = false;   // 종전 동작: 틀려도 OK
                Assert.True(tool.Execute(img).Success);
            }
            finally
            {
                service.CurrentPointCloud = null;
                File.Delete(refPath);
            }
        }

        // ── 저장 왕복 · 설정 VM 왕복 · 템플릿 프리셋 ──

        [Fact]
        public void Serializer_RoundTripsAllJudgmentParameters()
        {
            var geo = (Geometry3DTool)ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(new Geometry3DTool
            { EnableJudgment = true, ExpectedValue = 90, ToleranceMinus = 1.5, TolerancePlus = 2.5 }))!;
            Assert.True(geo.EnableJudgment); Assert.Equal(90, geo.ExpectedValue); Assert.Equal(1.5, geo.ToleranceMinus); Assert.Equal(2.5, geo.TolerancePlus);

            var plane = (PlaneFitTool)ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(new PlaneFitTool
            { EnableJudgment = true, MaxFlatness = 0.8 }))!;
            Assert.True(plane.EnableJudgment); Assert.Equal(0.8, plane.MaxFlatness);

            var reg = (PointCloudRegistrationTool)ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(new PointCloudRegistrationTool
            { EnableJudgment = true, MinConfidence = 0.3 }))!;
            Assert.True(reg.EnableJudgment); Assert.Equal(0.3, reg.MinConfidence);

            var cl = (PointCloudClusterTool)ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(new PointCloudClusterTool
            {
                EnableJudgment = true, UseCountJudgment = false, CountMode = PointCloudClusterTool.ClusterCountMode.Range,
                ExpectedCount = 2, ExpectedCountMax = 5, UseSizeJudgment = true, ExpectedLength = 377, ExpectedWidth = 250,
                SizeToleranceMinus = 4, SizeTolerancePlus = 6,
            }))!;
            Assert.True(cl.EnableJudgment); Assert.False(cl.UseCountJudgment);
            Assert.Equal(PointCloudClusterTool.ClusterCountMode.Range, cl.CountMode);
            Assert.Equal(2, cl.ExpectedCount); Assert.Equal(5, cl.ExpectedCountMax);
            Assert.True(cl.UseSizeJudgment); Assert.Equal(377, cl.ExpectedLength); Assert.Equal(250, cl.ExpectedWidth);
            Assert.Equal(4, cl.SizeToleranceMinus); Assert.Equal(6, cl.SizeTolerancePlus);
        }

        [Fact]
        public void Serializer_OldRecipeWithoutJudgmentKeys_LoadsWithJudgmentOff()
        {
            // v1.42.6 이하 레시피 — 판정 키가 없다 → 종전 동작(판정 꺼짐) 유지
            foreach (var type in new[] { "Geometry3DTool", "PlaneFitTool", "PointCloudRegistrationTool", "PointCloudClusterTool" })
            {
                var tool = ToolSerializer.DeserializeTool(new ToolConfig { ToolType = type, Name = type });
                var prop = tool!.GetType().GetProperty("EnableJudgment")!;
                Assert.False((bool)prop.GetValue(tool)!, $"{type} 판정이 기본으로 켜져 있음");
            }
        }

        [Fact]
        public void SettingsViewModels_ExposeJudgmentProperties_BothWays()
        {
            var geo = new Geometry3DTool();
            var gvm = new Geometry3DToolSettingsViewModel(geo) { EnableJudgment = true, ExpectedValue = 90, ToleranceMinus = 1, TolerancePlus = 2 };
            Assert.True(geo.EnableJudgment); Assert.Equal(90, geo.ExpectedValue); Assert.Equal(1, geo.ToleranceMinus); Assert.Equal(2, geo.TolerancePlus);
            geo.ExpectedValue = 12; Assert.Equal(12, gvm.ExpectedValue);

            var plane = new PlaneFitTool();
            var pvm = new PlaneFitToolSettingsViewModel(plane) { EnableJudgment = true, MaxFlatness = 0.7 };
            Assert.True(plane.EnableJudgment); Assert.Equal(0.7, plane.MaxFlatness);
            plane.MaxFlatness = 2; Assert.Equal(2, pvm.MaxFlatness);

            var reg = new PointCloudRegistrationTool();
            var rvm = new PointCloudRegistrationToolSettingsViewModel(reg) { EnableJudgment = true, MinConfidence = 0.3 };
            Assert.True(reg.EnableJudgment); Assert.Equal(0.3, reg.MinConfidence);
            reg.MinConfidence = 0.6; Assert.Equal(0.6, rvm.MinConfidence);

            var cl = new PointCloudClusterTool();
            var cvm = new PointCloudClusterToolSettingsViewModel(cl)
            {
                EnableJudgment = true, UseCountJudgment = false, CountMode = PointCloudClusterTool.ClusterCountMode.LessOrEqual,
                ExpectedCount = 0, ExpectedCountMax = 3, UseSizeJudgment = true, ExpectedLength = 377, ExpectedWidth = 250,
                SizeToleranceMinus = 4, SizeTolerancePlus = 6,
            };
            Assert.True(cl.EnableJudgment); Assert.False(cl.UseCountJudgment);
            Assert.Equal(PointCloudClusterTool.ClusterCountMode.LessOrEqual, cl.CountMode);
            Assert.Equal(0, cl.ExpectedCount); Assert.Equal(3, cl.ExpectedCountMax);
            Assert.True(cl.UseSizeJudgment); Assert.Equal(377, cl.ExpectedLength); Assert.Equal(250, cl.ExpectedWidth);
            Assert.Equal(4, cl.SizeToleranceMinus); Assert.Equal(6, cl.SizeTolerancePlus);
            cl.ExpectedLength = 100; Assert.Equal(100, cvm.ExpectedLength);
        }

        [Theory]
        [InlineData("pc-deviation")]
        [InlineData("3d-registration-align")]
        public void RegistrationTemplates_PresetConfidenceJudgment(string templateId)
        {
            var template = RecipeTemplateCatalog.Templates.First(t => t.Id == templateId);
            var reg = RecipeTemplateCatalog.CreateTools(template).OfType<PointCloudRegistrationTool>().Single();
            var restored = (PointCloudRegistrationTool)ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(reg))!;
            Assert.True(restored.EnableJudgment);
            Assert.Equal(0.3, restored.MinConfidence);
        }
    }
}
