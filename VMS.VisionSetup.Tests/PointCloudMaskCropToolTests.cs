using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// PointCloudMaskCropTool 검증 (2D 마스크 → 3D 점군 크롭):
    /// - 마스크 안의 점만 남김 (organized 점군, 동일 해상도)
    /// - stride 다운샘플/해상도 차이 시 좌표 배율 매핑
    /// - InvertMask / SkipInvalidZ / 빈 결과(점군 유지) 동작
    /// - ExecuteAll 재실행 시 CurrentPointCloud 원본 복원 (파괴적 갱신 보정)
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class PointCloudMaskCropToolTests
    {
        /// <summary>w×h organized 점군. X=col·stride, Y=row·stride (MechMind grab과 동일 규약), Z 상수.</summary>
        private static PointCloudData MakeOrganized(int w, int h, int stride = 1, float z = 10f)
        {
            var xyz = new float[w * h * 3];
            int i = 0;
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    xyz[i++] = col * stride;
                    xyz[i++] = row * stride;
                    xyz[i++] = z;
                }
            return PointCloudData.FromArrays(xyz, null, "test", w, h);
        }

        /// <summary>좌측 leftCols 픽셀 열만 255인 CV_8UC1 마스크.</summary>
        private static Mat MakeLeftMask(int w, int h, int leftCols)
        {
            var mask = new Mat(h, w, MatType.CV_8UC1, Scalar.Black);
            if (leftCols > 0)
            {
                using var left = new Mat(mask, new Rect(0, 0, leftCols, h));
                left.SetTo(255);
            }
            return mask;
        }

        [Fact]
        public void Execute_SameResolutionMask_KeepsOnlyMaskedPoints()
        {
            var service = VisionService.Instance;
            service.CurrentPointCloud = MakeOrganized(4, 4);
            using var mask = MakeLeftMask(4, 4, 2); // 좌측 2열만

            var tool = new PointCloudMaskCropTool();
            var result = tool.Execute(mask);

            Assert.True(result.Success);
            Assert.Equal(16, result.Data["InputPoints"]);
            Assert.Equal(8, result.Data["OutputPoints"]);

            var cropped = service.CurrentPointCloud!;
            Assert.Equal(8, cropped.PointCount);
            for (int i = 0; i < cropped.PointCount; i++)
                Assert.True(cropped.Positions[i].X < 2f);
        }

        [Fact]
        public void Execute_StrideDownsampledCloud_MapsMaskByRatio()
        {
            var service = VisionService.Instance;
            // stride 2: 4x4 그리드가 뎁스맵 8x8을 대표 (X = 0,2,4,6)
            service.CurrentPointCloud = MakeOrganized(4, 4, stride: 2);
            using var mask = MakeLeftMask(8, 8, 4); // 뎁스맵 해상도 마스크, 좌측 절반

            var tool = new PointCloudMaskCropTool();
            var result = tool.Execute(mask);

            // X=0,2 두 열만 마스크 안 (X=4,6 제외) → 4행 × 2열 = 8점
            Assert.True(result.Success);
            Assert.Equal(8, result.Data["OutputPoints"]);
            for (int i = 0; i < service.CurrentPointCloud!.PointCount; i++)
                Assert.True(service.CurrentPointCloud.Positions[i].X < 4f);
        }

        [Fact]
        public void Execute_HigherResolutionMask_MapsMaskByRatio()
        {
            var service = VisionService.Instance;
            service.CurrentPointCloud = MakeOrganized(4, 4); // 뎁스맵 4x4
            using var mask = MakeLeftMask(8, 8, 4); // 컬러 해상도 마스크 (2배)

            var tool = new PointCloudMaskCropTool();
            var result = tool.Execute(mask);

            // scale=2: point X → mask x = X*2 < 4 → X ∈ {0, 1} → 8점
            Assert.True(result.Success);
            Assert.Equal(8, result.Data["OutputPoints"]);
        }

        [Fact]
        public void Execute_InvertMask_KeepsOutsidePoints()
        {
            var service = VisionService.Instance;
            service.CurrentPointCloud = MakeOrganized(4, 4);
            using var mask = MakeLeftMask(4, 4, 2);

            var tool = new PointCloudMaskCropTool { InvertMask = true };
            var result = tool.Execute(mask);

            Assert.True(result.Success);
            Assert.Equal(8, result.Data["OutputPoints"]);
            for (int i = 0; i < service.CurrentPointCloud!.PointCount; i++)
                Assert.True(service.CurrentPointCloud.Positions[i].X >= 2f);
        }

        [Fact]
        public void Execute_SkipInvalidZ_ExcludesZeroDepthPoints()
        {
            var service = VisionService.Instance;
            var cloud = MakeOrganized(4, 4);
            // 좌상단 2점을 측정 실패(Z=0)로
            cloud.Positions[0] = cloud.Positions[0] with { Z = 0f };
            cloud.Positions[1] = cloud.Positions[1] with { Z = 0f };
            service.CurrentPointCloud = cloud;
            using var mask = MakeLeftMask(4, 4, 4); // 전체 마스크

            var tool = new PointCloudMaskCropTool(); // SkipInvalidZ 기본 켬
            var result = tool.Execute(mask);

            Assert.True(result.Success);
            Assert.Equal(14, result.Data["OutputPoints"]);

            var keepAll = new PointCloudMaskCropTool { SkipInvalidZ = false };
            service.CurrentPointCloud = MakeOrganized(4, 4);
            service.CurrentPointCloud.Positions[0] = service.CurrentPointCloud.Positions[0] with { Z = 0f };
            var result2 = keepAll.Execute(mask);
            Assert.Equal(16, result2.Data["OutputPoints"]);
        }

        [Fact]
        public void Execute_EmptyMask_FailsAndKeepsOriginalCloud()
        {
            var service = VisionService.Instance;
            var original = MakeOrganized(4, 4);
            service.CurrentPointCloud = original;
            using var mask = MakeLeftMask(4, 4, 0); // 전부 0

            var tool = new PointCloudMaskCropTool();
            var result = tool.Execute(mask);

            Assert.False(result.Success);
            Assert.Equal(0, result.Data["OutputPoints"]);
            Assert.Same(original, service.CurrentPointCloud); // 점군 유지
        }

        [Fact]
        public void Execute_PreservesIntrinsics_ForDownstreamAutoScale()
        {
            var service = VisionService.Instance;
            var cloud = MakeOrganized(4, 4);
            var intr = new DepthIntrinsics { Fx = 1234, Fy = 1235, Cx = 2, Cy = 2 };
            cloud.Intrinsics = intr;
            service.CurrentPointCloud = cloud;
            using var mask = MakeLeftMask(4, 4, 2);

            var result = new PointCloudMaskCropTool().Execute(mask);

            Assert.True(result.Success);
            Assert.Same(intr, service.CurrentPointCloud!.Intrinsics);
        }

        [Fact]
        public void Execute_UseROI_CropsToRoiRegionOnly()
        {
            var service = VisionService.Instance;
            service.CurrentPointCloud = MakeOrganized(4, 4);
            // 일반 이미지(전체 흰색)를 연결하고 ROI만 그린 시나리오
            using var image = MakeLeftMask(4, 4, 4);

            var tool = new PointCloudMaskCropTool { UseROI = true, ROI = new Rect(0, 0, 2, 4) };
            var result = tool.Execute(image);

            Assert.True(result.Success);
            Assert.Equal(8, result.Data["OutputPoints"]);
            for (int i = 0; i < service.CurrentPointCloud!.PointCount; i++)
                Assert.True(service.CurrentPointCloud.Positions[i].X < 2f);
        }

        [Fact]
        public void Execute_UseROI_OutsideImage_IsIgnored()
        {
            var service = VisionService.Instance;
            service.CurrentPointCloud = MakeOrganized(4, 4);
            using var image = MakeLeftMask(4, 4, 4);

            // ROI가 이미지와 안 겹치면 no-op — 전체 마스크 그대로
            var tool = new PointCloudMaskCropTool { UseROI = true, ROI = new Rect(100, 100, 10, 10) };
            var result = tool.Execute(image);

            Assert.True(result.Success);
            Assert.Equal(16, result.Data["OutputPoints"]);
        }

        [Fact]
        public void Execute_OutputImage_IsEffectiveKeptMask_AndOverlayProduced()
        {
            var service = VisionService.Instance;
            service.CurrentPointCloud = MakeOrganized(4, 4);
            using var image = MakeLeftMask(4, 4, 4); // 전체 흰색

            var tool = new PointCloudMaskCropTool { UseROI = true, ROI = new Rect(0, 0, 2, 4) };
            var result = tool.Execute(image);

            Assert.True(result.Success);
            // OutputImage = 유효 마스크(ROI 반영) — pass-through가 아니라 실제 크롭 범위
            Assert.NotNull(result.OutputImage);
            Assert.Equal(8, Cv2.CountNonZero(result.OutputImage!));
            Assert.NotNull(result.OverlayImage);
            Assert.Equal(3, result.OverlayImage!.Channels());
        }

        // ─── 병렬 브랜치 (ROI 다른 Mask Crop 2개 → Cluster) ───

        [Fact]
        public void ExecuteAll_TwoCropsDisjointRois_ReplaceOnly_SecondFailsWithHint()
        {
            var service = VisionService.Instance;
            service.ClearTools();
            using var image = new Mat(8, 8, MatType.CV_8UC3, new Scalar(255, 255, 255));
            service.SetImage(image);
            service.CurrentPointCloud = MakeOrganized(8, 8);

            var crop1 = new PointCloudMaskCropTool { Name = "Crop1", UseROI = true, ROI = new Rect(0, 0, 2, 8) };
            var crop2 = new PointCloudMaskCropTool { Name = "Crop2", UseROI = true, ROI = new Rect(6, 0, 2, 8) };
            service.AddTool(crop1);
            service.AddTool(crop2);

            try
            {
                var results = service.ExecuteAll();
                Assert.True(results[0].Success);
                // 기존(전부 Replace) 동작: 첫 크롭이 점군을 좁혀 두 번째 ROI에 점이 없음
                Assert.False(results[1].Success);
                Assert.Contains("Union", results[1].Message);
            }
            finally
            {
                service.ClearTools();
                service.CurrentPointCloud = null;
            }
        }

        [Fact]
        public void ExecuteAll_TwoCropsDisjointRois_SecondUnion_ClusterSeesBoth()
        {
            var service = VisionService.Instance;
            service.ClearTools();
            using var image = new Mat(8, 8, MatType.CV_8UC3, new Scalar(255, 255, 255));
            service.SetImage(image);
            service.CurrentPointCloud = MakeOrganized(8, 8);

            var crop1 = new PointCloudMaskCropTool { Name = "Crop1", UseROI = true, ROI = new Rect(0, 0, 2, 8) };
            var crop2 = new PointCloudMaskCropTool
            {
                Name = "Crop2",
                UseROI = true,
                ROI = new Rect(6, 0, 2, 8),
                CombineMode = PointCloudMaskCropTool.CropCombineMode.Union
            };
            var cluster = new PointCloudClusterTool
            {
                Name = "Cluster",
                Tolerance = 1.5f,
                MinPoints = 5,
                DrawOverlay = false,
                OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal
            };
            service.AddTool(crop1);
            service.AddTool(crop2);
            service.AddTool(cluster);
            service.AddConnection(crop1, cluster, ConnectionType.Result);
            service.AddConnection(crop2, cluster, ConnectionType.Result);

            try
            {
                var results = service.ExecuteAll();
                Assert.True(results[0].Success, results[0].Message);
                Assert.True(results[1].Success, results[1].Message);
                Assert.Equal(16, results[1].Data["OutputPoints"]);   // ROI-B에서 자른 점
                Assert.Equal(32, results[1].Data["MergedPoints"]);   // 16 + 16 합집합
                Assert.Equal(32, service.CurrentPointCloud!.PointCount);

                // 두 ROI가 4px 떨어져 있으므로 클러스터 2개 (16점씩)
                Assert.True(results[2].Success, results[2].Message);
                Assert.Equal(2, results[2].Data["ClusterCount"]);

                // 재실행: 점군 복원 후 동일 결과
                var rerun = service.ExecuteAll();
                Assert.Equal(32, rerun[1].Data["MergedPoints"]);
                Assert.Equal(2, rerun[2].Data["ClusterCount"]);
            }
            finally
            {
                service.ClearTools();
                service.CurrentPointCloud = null;
            }
        }

        [Fact]
        public void ExecuteTool_Rerun_RestoresOriginalPointCloud()
        {
            var service = VisionService.Instance;
            service.ClearTools();
            using var image = new Mat(4, 4, MatType.CV_8UC3, new Scalar(255, 255, 255));
            service.SetImage(image);
            service.CurrentPointCloud = MakeOrganized(4, 4);

            var tool = new PointCloudMaskCropTool { UseROI = true, ROI = new Rect(0, 0, 2, 4) };
            service.AddTool(tool);

            try
            {
                var first = service.ExecuteTool(tool);
                Assert.True(first.Success);
                Assert.Equal(16, first.Data["InputPoints"]);
                Assert.Equal(8, service.CurrentPointCloud!.PointCount);

                // Run Selected 반복 — 점군이 계속 좁아지지 않고 원본에서 다시 시작해야 함
                var second = service.ExecuteTool(tool);
                Assert.True(second.Success);
                Assert.Equal(16, second.Data["InputPoints"]);
                Assert.Equal(8, service.CurrentPointCloud!.PointCount);
            }
            finally
            {
                service.ClearTools();
                service.CurrentPointCloud = null;
            }
        }

        [Fact]
        public void Execute_NoPointCloud_Fails()
        {
            VisionService.Instance.CurrentPointCloud = null;
            using var mask = MakeLeftMask(4, 4, 2);

            var result = new PointCloudMaskCropTool().Execute(mask);

            Assert.False(result.Success);
        }

        // ─── ExecuteAll 재실행 시 점군 복원 ───

        [Fact]
        public void ExecuteAll_Rerun_RestoresOriginalPointCloud()
        {
            var service = VisionService.Instance;
            service.ClearTools();
            // 좌측 절반 흰색 → 크롭 마스크로 동작 (Image 연결 없음 → CurrentImage 입력)
            using var image = new Mat(4, 4, MatType.CV_8UC3, Scalar.Black);
            using (var left = new Mat(image, new Rect(0, 0, 2, 4)))
                left.SetTo(new Scalar(255, 255, 255));
            service.SetImage(image);
            service.CurrentPointCloud = MakeOrganized(4, 4);

            var tool = new PointCloudMaskCropTool();
            service.AddTool(tool);

            try
            {
                var first = service.ExecuteAll();
                Assert.True(first[0].Success);
                Assert.Equal(16, first[0].Data["InputPoints"]);
                Assert.Equal(8, service.CurrentPointCloud!.PointCount);

                // 재Grab 없이 재실행 — 크롭된 8점이 아니라 원본 16점에서 다시 시작해야 함
                var second = service.ExecuteAll();
                Assert.True(second[0].Success);
                Assert.Equal(16, second[0].Data["InputPoints"]);
                Assert.Equal(8, service.CurrentPointCloud!.PointCount);
            }
            finally
            {
                service.ClearTools();
                service.CurrentPointCloud = null;
            }
        }

        [Fact]
        public void ExecuteAll_NewCloudBetweenRuns_UsesNewCloudAsInput()
        {
            var service = VisionService.Instance;
            service.ClearTools();
            using var image = new Mat(4, 4, MatType.CV_8UC3, new Scalar(255, 255, 255));
            service.SetImage(image);
            service.CurrentPointCloud = MakeOrganized(4, 4);

            var tool = new PointCloudMaskCropTool();
            service.AddTool(tool);

            try
            {
                service.ExecuteAll();

                // 새 Grab 시뮬레이션 — 복원 대신 새 점군을 입력으로 사용해야 함
                service.CurrentPointCloud = MakeOrganized(3, 3);
                var results = service.ExecuteAll();
                Assert.Equal(9, results[0].Data["InputPoints"]);
            }
            finally
            {
                service.ClearTools();
                service.CurrentPointCloud = null;
            }
        }
    }
}
